using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Tableaus;
using TaleWorlds.MountAndBlade.View.Tableaus.Thumbnails;

namespace CoopSpectator.Patches
{
    // Observation only. No native arguments, results, RNG or scene state change.
    // Separate rollout gate until installed-library/native validation is complete.
    internal static class CoopPortraitPoseObservationPatch
    {
        private const string OwnerId = "com.coopspectator.portrait-pose-observer";
        private static readonly ConditionalWeakTable<object, Owner> Owners = new ConditionalWeakTable<object, Owner>();
        [ThreadStatic] private static Frame _frame;
        private static bool _installed;
        private static readonly FieldInfo CachesField = typeof(ThumbnailCacheManager).GetField(
            "_thumbnailCaches", BindingFlags.Instance | BindingFlags.NonPublic);

        private sealed class Owner
        {
            internal readonly CoopPortraitPoseEvidence Evidence = new CoopPortraitPoseEvidence();
            internal readonly Dictionary<string, Pending> Pending = new Dictionary<string, Pending>(StringComparer.Ordinal);
        }
        private sealed class Pending
        {
            internal Owner Owner;
            internal string Key;
            internal long Ticket;
            internal int Pose = -1;
        }
        private sealed class Frame
        {
            internal object Cache;
            internal CharacterCode Character;
            internal Owner Owner;
            internal string Key;
            internal Pending Pending;
            internal bool Selecting;
        }

        internal static void Apply()
        {
            if (_installed || !ExperimentalFeatures.EnableCampaignMapPrototype ||
                Environment.GetEnvironmentVariable("COOPSPECTATOR_PORTRAIT_POSE_OBSERVER") != "1") return;
            var harmony = new Harmony(OwnerId);
            try
            {
                var type = typeof(CharacterThumbnailCache);
                Patch(harmony, AccessTools.Method(type, "OnCreateTexture"), nameof(BeginCreation), null, nameof(EndCreation));
                Patch(harmony, AccessTools.Method(type, "CreateCharacterBaseEntity"), nameof(BeginSelection), null, nameof(EndSelection));
                Patch(harmony, AccessTools.Method(type, "GetPoseParamsFromCharacterCode"), null, nameof(ObservePose), null);
                var map = type.GetInterfaceMap(typeof(IThumbnailCache));
                Patch(harmony, Target(map, "OnThumbnailRenderCompleted"), nameof(BeginCompletion), nameof(EndCompletion), null);
                Patch(harmony, Target(map, "Clear"), nameof(Clear), null, null);
                Patch(harmony, Target(map, "Destroy"), nameof(Close), null, null);
                Patch(harmony, AccessTools.Method(type, "OnRequestCancelled"), nameof(Cancel), null, null);
                _installed = true;
                ModLogger.Info("Portrait pose observation installed (explicit rollout gate).");
            }
            catch (Exception ex)
            {
                harmony.UnpatchAll(OwnerId);
                ModLogger.Error("Portrait pose observation unavailable; hooks removed.", ex);
            }
        }

        private static MethodInfo Target(InterfaceMapping map, string name)
        {
            for (int i = 0; i < map.InterfaceMethods.Length; i++)
                if (map.InterfaceMethods[i].Name == name) return map.TargetMethods[i];
            throw new MissingMethodException(name);
        }
        private static void Patch(Harmony h, MethodInfo target, string prefix, string postfix, string finalizer)
        {
            if (target == null) throw new MissingMethodException("Native portrait observer target");
            // Interface maps can return an inherited reflected method. Harmony
            // requires the implementation obtained from its declaring type.
            foreach (var declared in target.DeclaringType.GetMethods(BindingFlags.Instance |
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (declared.Module == target.Module && declared.MetadataToken == target.MetadataToken)
                { target = declared; break; }
            h.Patch(target, Method(prefix), Method(postfix), null, Method(finalizer));
        }
        private static HarmonyMethod Method(string name) => name == null ? null :
            new HarmonyMethod(typeof(CoopPortraitPoseObservationPatch), name);

        internal static bool TryGet(object cache, string exactRenderKey, out int pose)
        {
            pose = -1;
            return cache is CharacterThumbnailCache && Owners.TryGetValue(cache, out var owner) &&
                owner.Evidence.TryGet(exactRenderKey, out pose);
        }

        // Caller supplies only a currently visible portrait's normalized key.
        // Never enumerate observed heroes or choose an arbitrary cache on ambiguity.
        internal static bool TryGetCurrent(string exactRenderKey, out int pose)
        {
            pose = -1;
            if (!_installed || GameNetwork.IsMultiplayer || TaleWorlds.CampaignSystem.Campaign.Current == null ||
                ThumbnailCacheManager.Current == null || CachesField == null) return false;
            var caches = CachesField.GetValue(ThumbnailCacheManager.Current) as System.Collections.IEnumerable;
            if (caches == null) return false;
            CharacterThumbnailCache selected = null;
            foreach (var cache in caches)
                if (cache is CharacterThumbnailCache character)
                {
                    if (selected != null) return false;
                    selected = character;
                }
            return selected != null && TryGet(selected, exactRenderKey, out pose);
        }

        private static void BeginCreation(object __instance, CharacterThumbnailCreationData __0, out Frame __state)
        {
            __state = _frame;
            // Push a boundary even for ineligible/nested requests: their selector
            // must never be mistaken for an outer hero's selector.
            var frame = new Frame { Cache = __instance };
            _frame = frame;
            if (!(__instance is CharacterThumbnailCache) || TaleWorlds.CampaignSystem.Campaign.Current == null || GameNetwork.IsMultiplayer ||
                __0?.CharacterCode == null || !__0.CharacterCode.IsHero || string.IsNullOrEmpty(__0.RenderId) ||
                __0.RenderId.Length > CoopPortraitPoseEvidence.MaxRenderKeyCharacters) return;
            frame.Character = __0.CharacterCode;
            frame.Key = __0.RenderId;
            frame.Owner = Owners.GetValue(__instance, _ => new Owner());
        }

        private static void BeginSelection(object __instance, CharacterCode __0)
        {
            var frame = _frame;
            if (frame?.Owner == null || !ReferenceEquals(frame.Cache, __instance) ||
                !ReferenceEquals(frame.Character, __0)) return;
            lock (frame.Owner)
            {
                long ticket = frame.Owner.Evidence.Begin(frame.Key);
                if (ticket == 0) return;
                frame.Pending = new Pending { Owner = frame.Owner, Key = frame.Key, Ticket = ticket };
                frame.Owner.Pending[frame.Key] = frame.Pending;
                frame.Selecting = true;
            }
        }

        private static void ObservePose(object __instance, CharacterCode __0, string __1)
        {
            var frame = _frame;
            if (frame?.Pending == null || !frame.Selecting || !ReferenceEquals(frame.Cache, __instance) ||
                !ReferenceEquals(frame.Character, __0) || frame.Pending.Pose >= 0) return;
            if (__1 != null && __1.Length == 6 && __1.StartsWith("lord_", StringComparison.Ordinal) &&
                __1[5] >= '0' && __1[5] <= '7') frame.Pending.Pose = __1[5] - '0';
        }

        private static Exception EndSelection(Exception __exception)
        {
            if (_frame != null) _frame.Selecting = false;
            return __exception;
        }
        private static Exception EndCreation(Exception __exception, Frame __state)
        {
            var current = _frame;
            _frame = __state;
            if (__exception != null && current?.Pending != null) Remove(current.Pending);
            return __exception;
        }

        private static void BeginCompletion(object __instance, string __0, out Pending __state)
        {
            __state = null;
            if (!(__instance is CharacterThumbnailCache) || !Owners.TryGetValue(__instance, out var owner) || __0 == null) return;
            lock (owner) owner.Pending.TryGetValue(__0, out __state);
        }
        private static void EndCompletion(bool __result, Texture __1, Pending __state)
        {
            if (__state == null) return;
            lock (__state.Owner)
            {
                if (__result && !ReferenceEquals(__1, null))
                    __state.Owner.Evidence.Complete(__state.Key, __state.Ticket, __state.Pose);
                else __state.Owner.Evidence.Cancel(__state.Key, __state.Ticket);
                if (__state.Owner.Pending.TryGetValue(__state.Key, out var pending) && ReferenceEquals(pending, __state))
                    __state.Owner.Pending.Remove(__state.Key);
            }
        }

        private static void Remove(Pending pending)
        {
            lock (pending.Owner)
            {
                pending.Owner.Evidence.Cancel(pending.Key, pending.Ticket);
                if (pending.Owner.Pending.TryGetValue(pending.Key, out var current) && ReferenceEquals(current, pending))
                    pending.Owner.Pending.Remove(pending.Key);
            }
        }
        private static void Cancel(object __instance, string __0)
        {
            if (!(__instance is CharacterThumbnailCache) || !Owners.TryGetValue(__instance, out var owner) || __0 == null) return;
            lock (owner)
            {
                if (owner.Pending.TryGetValue(__0, out var pending)) Remove(pending);
                // Completed entries also cease to be evidence on native eviction.
                owner.Evidence.Forget(__0);
            }
        }
        private static void Clear(object __instance)
        {
            if (!(__instance is CharacterThumbnailCache) || !Owners.TryGetValue(__instance, out var owner)) return;
            lock (owner) { owner.Pending.Clear(); owner.Evidence.Clear(); }
        }
        private static void Close(object __instance)
        {
            if (!(__instance is CharacterThumbnailCache) || !Owners.TryGetValue(__instance, out var owner)) return;
            lock (owner) { owner.Pending.Clear(); owner.Evidence.Close(); }
        }
    }
}
