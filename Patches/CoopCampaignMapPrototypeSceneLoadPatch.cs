using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Engine;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Main_map contains campaign-only scene components which dereference
    /// Campaign.Current. The isolated multiplayer renderer has no Campaign and
    /// does not consume those caches, so skip only their initialization during
    /// the explicitly scoped prototype Scene.Read call.
    /// </summary>
    public static class CoopCampaignMapPrototypeSceneLoadPatch
    {
        private const string SiegeCacheTargetTypeName =
            "SandBox.CampaignMapSiegePrefabEntityCache";
        private const string SnowAndRainTargetTypeName =
            "SandBox.View.Map.SnowAndRainTextureDefiner";
        private static readonly object ApplyLock = new object();
        private static bool _siegeCacheApplied;
        private static bool _snowAndRainApplied;
        private static bool _siegeCacheUnavailableLogged;
        private static bool _snowAndRainUnavailableLogged;
        private static bool _siegeCacheSkipLogged;
        private static bool _snowAndRainInitializationLogged;

        public static void Apply(Harmony harmony)
        {
            if (harmony == null ||
                !ExperimentalFeatures.EnableCampaignMapPrototype)
            {
                return;
            }

            lock (ApplyLock)
            {
                TryApplyTarget(
                    harmony,
                    SiegeCacheTargetTypeName,
                    "OnInit",
                    nameof(SiegeCacheOnInitPrefix),
                    ref _siegeCacheApplied,
                    ref _siegeCacheUnavailableLogged);
                TryApplyTarget(
                    harmony,
                    SnowAndRainTargetTypeName,
                    "SetDataToScene",
                    nameof(SnowAndRainSetDataToScenePrefix),
                    ref _snowAndRainApplied,
                    ref _snowAndRainUnavailableLogged);
            }
        }

        private static void TryApplyTarget(
            Harmony harmony,
            string targetTypeName,
            string targetMethodName,
            string prefixMethodName,
            ref bool applied,
            ref bool unavailableLogged)
        {
            if (applied)
                return;

            Type targetType = AccessTools.TypeByName(targetTypeName);
            MethodInfo targetMethod = targetType == null
                ? null
                : AccessTools.Method(targetType, targetMethodName);
            MethodInfo prefixMethod = AccessTools.Method(
                typeof(CoopCampaignMapPrototypeSceneLoadPatch),
                prefixMethodName);
            if (targetMethod == null || prefixMethod == null)
            {
                if (!unavailableLogged)
                {
                    unavailableLogged = true;
                    ModLogger.Info(
                        "CoopCampaignMapPrototypeSceneLoadPatch: target unavailable; waiting for assembly load. Target=" +
                        targetTypeName + "." + targetMethodName + ".");
                }

                return;
            }

            harmony.Patch(
                targetMethod,
                prefix: new HarmonyMethod(prefixMethod));
            applied = true;
            ModLogger.Info(
                "CoopCampaignMapPrototypeSceneLoadPatch: scoped prefix applied. Target=" +
                targetTypeName + "." + targetMethodName + ".");
        }

        private static bool SiegeCacheOnInitPrefix()
        {
            if (!ShouldSkipCampaignOnlyInitialization())
                return true;

            if (!_siegeCacheSkipLogged)
            {
                _siegeCacheSkipLogged = true;
                ModLogger.Info(
                    "CoopCampaignMapPrototypeSceneLoadPatch: skipped campaign-only siege prefab cache for isolated Main_map.");
            }

            return false;
        }

        private static bool SnowAndRainSetDataToScenePrefix(
            SnowAndRainTextureDefiner __instance)
        {
            if (!ShouldSkipCampaignOnlyInitialization())
                return true;

            try
            {
                Scene scene = __instance?.Scene;
                Texture texture = __instance?.SnowAndRainTexture;
                int dimension = __instance?.WeatherNodeGridWidthAndHeight ?? 0;
                if (scene == null || texture == null || dimension <= 0)
                {
                    ModLogger.Info(
                        "CoopCampaignMapPrototypeSceneLoadPatch: isolated snow/rain initialization unavailable. " +
                        "Scene=" + (scene != null) +
                        " Texture=" + (texture != null) +
                        " Dimension=" + dimension + ".");
                    return false;
                }

                scene.CreateDynamicRainTexture(dimension, dimension);
                scene.SetDynamicSnowTexture(texture);
                if (!_snowAndRainInitializationLogged)
                {
                    _snowAndRainInitializationLogged = true;
                    ModLogger.Info(
                        "CoopCampaignMapPrototypeSceneLoadPatch: initialized isolated snow/rain scene resources without Campaign.Current. Dimension=" +
                        dimension + ".");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Error(
                    "CoopCampaignMapPrototypeSceneLoadPatch: isolated snow/rain initialization failed.",
                    ex);
            }

            return false;
        }

        private static bool ShouldSkipCampaignOnlyInitialization()
        {
            return ExperimentalFeatures.EnableCampaignMapPrototype &&
                   CoopCampaignMapPrototypeSceneLoadScope.IsActive &&
                   TaleWorlds.CampaignSystem.Campaign.Current == null;
        }
    }
}
