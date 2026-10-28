using System;
using CoopSpectator.Infrastructure;
using CoopSpectator.UI;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.Tableaus.Thumbnails;

namespace CoopSpectator.Patches
{
    // Only our exact request/code reference receives detached equipment. Nested
    // stock requests explicitly mask the scope, including exception paths.
    internal static class CoopDetachedPortraitEquipmentPatch
    {
        private const string Owner = "com.coopspectator.detached-portrait-equipment";
        [ThreadStatic] private static Frame _frame;
        internal static bool IsInstalled { get; private set; }
        private sealed class Frame
        {
            internal object Cache;
            internal CoopDetachedPortraitCreationData Request;
            internal bool Selecting;
        }
        internal static void Apply()
        {
            if (IsInstalled || !ExperimentalFeatures.EnableCampaignMapPrototype) return;
            var harmony = new Harmony(Owner);
            try
            {
                harmony.Patch(AccessTools.Method(typeof(CharacterThumbnailCache), "OnCreateTexture"),
                    new HarmonyMethod(typeof(CoopDetachedPortraitEquipmentPatch), nameof(Begin)),
                    finalizer: new HarmonyMethod(typeof(CoopDetachedPortraitEquipmentPatch), nameof(End)));
                harmony.Patch(AccessTools.Method(typeof(CharacterCode), "CalculateEquipment"),
                    prefix: new HarmonyMethod(typeof(CoopDetachedPortraitEquipmentPatch), nameof(Equipment)));
                harmony.Patch(AccessTools.Method(typeof(CharacterThumbnailCache), "CreateCharacterBaseEntity"),
                    prefix: new HarmonyMethod(typeof(CoopDetachedPortraitEquipmentPatch), nameof(BeginSelection)),
                    finalizer: new HarmonyMethod(typeof(CoopDetachedPortraitEquipmentPatch), nameof(EndSelection)));
                harmony.Patch(AccessTools.Method(typeof(CharacterThumbnailCache), "GetPoseParamsFromCharacterCode"),
                    postfix: new HarmonyMethod(typeof(CoopDetachedPortraitEquipmentPatch), nameof(Pose)));
                IsInstalled = true;
            }
            catch (Exception ex)
            {
                harmony.UnpatchAll(Owner);
                ModLogger.Error("Detached portrait hooks unavailable; removed.", ex);
            }
        }
        private static void Begin(object __instance, CharacterThumbnailCreationData __0, out Frame __state)
        {
            __state = _frame;
            _frame = __0 is CoopDetachedPortraitCreationData own && GameNetwork.IsClient ?
                new Frame { Cache = __instance, Request = own } : null;
        }
        private static Exception End(Exception __exception, Frame __state)
        { _frame = __state; return __exception; }
        private static bool Equipment(CharacterCode __instance, ref Equipment __result)
        {
            if (_frame?.Request == null || !ReferenceEquals(_frame.Request.CharacterCode, __instance)) return true;
            __result = new Equipment();
            __result.FillFrom(_frame.Request.Binding.Equipment);
            return false;
        }
        private static void BeginSelection(object __instance, CharacterCode __0)
        {
            if (_frame?.Request != null && ReferenceEquals(_frame.Cache, __instance) &&
                ReferenceEquals(_frame.Request.CharacterCode, __0)) _frame.Selecting = true;
        }
        private static Exception EndSelection(Exception __exception)
        { if (_frame != null) _frame.Selecting = false; return __exception; }
        private static void Pose(object __instance, CharacterCode __0, ref string __1)
        {
            if (_frame?.Request == null || !_frame.Selecting || !ReferenceEquals(_frame.Cache, __instance) ||
                !ReferenceEquals(_frame.Request.CharacterCode, __0) || !__0.IsHero || _frame.Request.Binding.Pose < 0) return;
            __1 = "lord_" + _frame.Request.Binding.Pose;
        }
    }
}
