using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using SandBox.View.Map;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;
using TWCampaign = TaleWorlds.CampaignSystem.Campaign;

namespace CoopSpectator.Patches
{
    public static class CampaignVisualResetPatch
    {
        private static readonly HashSet<string> LoggedTargets = new HashSet<string>(StringComparer.Ordinal);

        public static void Apply(Harmony harmony)
        {
            TryPatchMapScreenMethod(harmony, "OnResume");
            TryPatchMapScreenMethod(harmony, "OnActivate");
            TryPatchHelperMethodBySimpleTypeName(harmony, "InventoryScreenHelper", "OpenScreenAsInventory");
            TryPatchHelperMethodBySimpleTypeName(harmony, "PartyScreenHelper", "OpenScreenAsNormal");
        }

        private static void TryPatchMapScreenMethod(Harmony harmony, string methodName)
        {
            try
            {
                MethodInfo target = typeof(MapScreen).GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo postfix = typeof(CampaignVisualResetPatch).GetMethod(
                    nameof(MapScreenLifecycle_Postfix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (target == null || postfix == null)
                {
                    ModLogger.Info("CampaignVisualResetPatch: skip patch, MapScreen target not found. Method=" + methodName);
                    return;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                LogPatchedTarget("MapScreen." + methodName);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CampaignVisualResetPatch: failed to patch MapScreen." + methodName + ": " + ex.Message);
            }
        }

        private static void TryPatchHelperMethodBySimpleTypeName(Harmony harmony, string simpleTypeName, string methodName)
        {
            try
            {
                Type helperType = ResolveTypeBySimpleName(simpleTypeName);
                MethodInfo prefix = typeof(CampaignVisualResetPatch).GetMethod(
                    nameof(HelperOpenScreen_Prefix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (helperType == null || prefix == null)
                {
                    ModLogger.Info("CampaignVisualResetPatch: skip helper patch, type not found. Type=" + simpleTypeName + " Method=" + methodName);
                    return;
                }

                bool patchedAny = false;
                foreach (MethodInfo target in helperType
                    .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(candidate => string.Equals(candidate.Name, methodName, StringComparison.Ordinal)))
                {
                    harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                    LogPatchedTarget(helperType.FullName + "." + target.Name);
                    patchedAny = true;
                }

                if (!patchedAny)
                {
                    ModLogger.Info("CampaignVisualResetPatch: no matching helper methods found. Type=" + helperType.FullName + " Method=" + methodName);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CampaignVisualResetPatch: failed helper patch for " + simpleTypeName + "." + methodName + ": " + ex.Message);
            }
        }

        private static Type ResolveTypeBySimpleName(string simpleTypeName)
        {
            if (string.IsNullOrWhiteSpace(simpleTypeName))
                return null;

            Type resolved = AccessTools.TypeByName(simpleTypeName);
            if (resolved != null)
                return resolved;

            try
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type candidate = assembly
                        .GetTypes()
                        .FirstOrDefault(type => string.Equals(type?.Name, simpleTypeName, StringComparison.Ordinal));
                    if (candidate != null)
                        return candidate;
                }
            }
            catch
            {
            }

            return null;
        }

        private static void MapScreenLifecycle_Postfix()
        {
            TrySoftResetCampaignCharacterTableaus("MapScreen lifecycle");
            TryValidateCampaignAgentVisuals("MapScreen lifecycle");
        }

        private static void HelperOpenScreen_Prefix()
        {
            TrySoftResetCampaignCharacterTableaus("Campaign screen helper");
            TryValidateCampaignAgentVisuals("Campaign screen helper");
        }

        private static void TrySoftResetCampaignCharacterTableaus(string source)
        {
            try
            {
                Type tableauManagerType = typeof(BannerlordTableauManager);
                FieldInfo scenesField = tableauManagerType.GetField(
                    "_tableauCharacterScenes",
                    BindingFlags.Static | BindingFlags.NonPublic);
                FieldInfo initializedField = tableauManagerType.GetField(
                    "_isTableauRenderSystemInitialized",
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (scenesField == null || initializedField == null)
                    return;

                scenesField.SetValue(null, new Scene[5]);
                initializedField.SetValue(null, false);
                BannerlordTableauManager.InitializeCharacterTableauRenderSystem();
                ModLogger.Info("CampaignVisualResetPatch: soft-reset BannerlordTableauManager. Source=" + source);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CampaignVisualResetPatch: soft-reset BannerlordTableauManager failed. Source=" + source + " Message=" + ex.Message);
            }
        }

        private static void TryValidateCampaignAgentVisuals(string source)
        {
            try
            {
                object mapScene = TWCampaign.Current?.MapSceneWrapper;
                if (mapScene == null)
                    return;

                MethodInfo validateMethod = mapScene.GetType().GetMethod(
                    "ValidateAgentVisualsReseted",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (validateMethod == null)
                    return;

                validateMethod.Invoke(mapScene, Array.Empty<object>());
                ModLogger.Info("CampaignVisualResetPatch: invoked ValidateAgentVisualsReseted. Source=" + source);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CampaignVisualResetPatch: ValidateAgentVisualsReseted failed. Source=" + source + " Message=" + ex.Message);
            }
        }

        private static void LogPatchedTarget(string targetLabel)
        {
            if (string.IsNullOrWhiteSpace(targetLabel) || !LoggedTargets.Add(targetLabel))
                return;

            ModLogger.Info("CampaignVisualResetPatch: applied patch to " + targetLabel + ".");
        }
    }
}
