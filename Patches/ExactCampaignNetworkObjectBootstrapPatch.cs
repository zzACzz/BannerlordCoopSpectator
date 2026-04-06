using System;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using NetworkMessages.FromServer;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    public static class ExactCampaignNetworkObjectBootstrapPatch
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                PatchOnRead(harmony, typeof(CreateAgent), nameof(CreateAgent_OnRead_Prefix));
                PatchOnRead(harmony, typeof(SynchronizeAgentSpawnEquipment), nameof(SynchronizeAgentSpawnEquipment_OnRead_Prefix));
                PatchOnRead(harmony, typeof(CreateAgentVisuals), nameof(CreateAgentVisuals_OnRead_Prefix));
                ModLogger.Info("ExactCampaignNetworkObjectBootstrapPatch: prefixes applied to network message OnRead methods.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("ExactCampaignNetworkObjectBootstrapPatch.Apply failed.", ex);
            }
        }

        private static void PatchOnRead(Harmony harmony, Type targetType, string prefixName)
        {
            var target = AccessTools.Method(targetType, "OnRead");
            var prefix = AccessTools.Method(typeof(ExactCampaignNetworkObjectBootstrapPatch), prefixName);
            if (target == null || prefix == null)
            {
                ModLogger.Info(
                    "ExactCampaignNetworkObjectBootstrapPatch: target not found. " +
                    "Type=" + (targetType?.FullName ?? "null") +
                    " Prefix=" + prefixName);
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
        }

        private static void CreateAgent_OnRead_Prefix()
        {
            EnsureLoadedForCurrentMission("CreateAgent.OnRead");
        }

        private static void SynchronizeAgentSpawnEquipment_OnRead_Prefix()
        {
            EnsureLoadedForCurrentMission("SynchronizeAgentSpawnEquipment.OnRead");
        }

        private static void CreateAgentVisuals_OnRead_Prefix()
        {
            EnsureLoadedForCurrentMission("CreateAgentVisuals.OnRead");
        }

        private static void EnsureLoadedForCurrentMission(string source)
        {
            try
            {
                if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                    return;

                Mission mission = Mission.Current;
                string sceneName = mission?.SceneName;
                if (string.IsNullOrWhiteSpace(sceneName))
                    return;

                if (!MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(sceneName) ||
                    !SceneRuntimeClassifier.IsCampaignBattleScene(sceneName))
                {
                    return;
                }

                ExactCampaignObjectCatalogBootstrap.EnsureLoaded(
                    "client-network-onread:" + source + ":" + sceneName);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "ExactCampaignNetworkObjectBootstrapPatch: EnsureLoaded failed open. " +
                    "Source=" + source +
                    " Error=" + ex.Message);
            }
        }
    }
}
