using System;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Tableaus.Thumbnails;

namespace CoopSpectator.Patches
{
    internal static class CoopReplicaPortraitPosePatch
    {
        private const string OwnerId = "com.coopspectator.replica-portrait-pose";
        [ThreadStatic] private static Frame _frame;
        internal static bool IsInstalled { get; private set; }
        private sealed class Frame
        {
            internal object Cache;
            internal CoopReplicaPortraitCreationData Request;
            internal bool Selecting;
        }
        internal static void Apply()
        {
            if (IsInstalled || !ExperimentalFeatures.EnableCampaignMapPrototype ||
                Environment.GetEnvironmentVariable("COOPSPECTATOR_PORTRAIT_POSE_OBSERVER") != "1") return;
            var h = new Harmony(OwnerId);
            try
            {
                var type = typeof(CharacterThumbnailCache);
                h.Patch(AccessTools.Method(type, "OnCreateTexture"),
                    new HarmonyMethod(typeof(CoopReplicaPortraitPosePatch), nameof(Begin)),
                    finalizer: new HarmonyMethod(typeof(CoopReplicaPortraitPosePatch), nameof(End)));
                h.Patch(AccessTools.Method(type, "CreateCharacterBaseEntity"),
                    new HarmonyMethod(typeof(CoopReplicaPortraitPosePatch), nameof(BeginSelection)),
                    finalizer: new HarmonyMethod(typeof(CoopReplicaPortraitPosePatch), nameof(EndSelection)));
                h.Patch(AccessTools.Method(type, "GetPoseParamsFromCharacterCode"),
                    postfix: new HarmonyMethod(typeof(CoopReplicaPortraitPosePatch), nameof(Select)));
                IsInstalled = true;
            }
            catch (Exception ex)
            {
                h.UnpatchAll(OwnerId);
                ModLogger.Error("Replica portrait pose hooks unavailable; removed.", ex);
            }
        }
        private static void Begin(object __instance, CharacterThumbnailCreationData __0, out Frame __state)
        {
            __state = _frame;
            _frame = new Frame { Cache = __instance, Request = GameNetwork.IsMultiplayer ? __0 as CoopReplicaPortraitCreationData : null };
        }
        private static Exception End(Exception __exception, Frame __state)
        { _frame = __state; return __exception; }
        private static void BeginSelection(object __instance, CharacterCode __0)
        {
            if (_frame?.Request != null && ReferenceEquals(_frame.Cache, __instance) &&
                ReferenceEquals(_frame.Request.CharacterCode, __0)) _frame.Selecting = true;
        }
        private static Exception EndSelection(Exception __exception)
        { if (_frame != null) _frame.Selecting = false; return __exception; }
        private static void Select(object __instance, CharacterCode __0, ref string __1)
        {
            if (_frame?.Request == null || !_frame.Selecting || !ReferenceEquals(_frame.Cache, __instance) ||
                !ReferenceEquals(_frame.Request.CharacterCode, __0)) return;
            __1 = "lord_" + _frame.Request.Pose;
        }
    }
}
