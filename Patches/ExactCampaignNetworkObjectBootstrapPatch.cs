using System;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
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
                PatchOnRead(
                    harmony,
                    typeof(CreateAgent),
                    nameof(CreateAgent_OnRead_Prefix),
                    nameof(CreateAgent_OnRead_Postfix));
                PatchOnWrite(harmony, typeof(CreateAgent), nameof(CreateAgent_OnWrite_Prefix));
                PatchOnRead(harmony, typeof(SynchronizeAgentSpawnEquipment), nameof(SynchronizeAgentSpawnEquipment_OnRead_Prefix));
                PatchOnRead(harmony, typeof(CreateAgentVisuals), nameof(CreateAgentVisuals_OnRead_Prefix));
                ModLogger.Info("ExactCampaignNetworkObjectBootstrapPatch: applied CreateAgent OnRead/OnWrite diagnostics and network message OnRead bootstrap prefixes.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("ExactCampaignNetworkObjectBootstrapPatch.Apply failed.", ex);
            }
        }

        private static void PatchOnRead(Harmony harmony, Type targetType, string prefixName, string postfixName = null)
        {
            var target = AccessTools.Method(targetType, "OnRead");
            var prefix = AccessTools.Method(typeof(ExactCampaignNetworkObjectBootstrapPatch), prefixName);
            var postfix = string.IsNullOrWhiteSpace(postfixName)
                ? null
                : AccessTools.Method(typeof(ExactCampaignNetworkObjectBootstrapPatch), postfixName);
            if (target == null || prefix == null || (!string.IsNullOrWhiteSpace(postfixName) && postfix == null))
            {
                ModLogger.Info(
                    "ExactCampaignNetworkObjectBootstrapPatch: target not found. " +
                    "Type=" + (targetType?.FullName ?? "null") +
                    " Prefix=" + prefixName +
                    " Postfix=" + (postfixName ?? "null"));
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix),
                postfix: postfix != null ? new HarmonyMethod(postfix) : null);
        }

        private static void PatchOnWrite(Harmony harmony, Type targetType, string prefixName)
        {
            var target = AccessTools.Method(targetType, "OnWrite");
            var prefix = AccessTools.Method(typeof(ExactCampaignNetworkObjectBootstrapPatch), prefixName);
            if (target == null || prefix == null)
            {
                ModLogger.Info(
                    "ExactCampaignNetworkObjectBootstrapPatch: OnWrite target not found. " +
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

        private static void CreateAgent_OnRead_Postfix(CreateAgent __instance, bool __result)
        {
            try
            {
                if (__instance == null)
                    return;

                bool snapshotReady = CoopMissionNetworkBridge.IsClientCurrentBattleSnapshotApplied(out string snapshotReadinessSummary);
                ExactCreateAgentCorridorDiagnostics.ObserveClientCreateAgentOnRead(
                    __instance,
                    __result,
                    snapshotReady,
                    snapshotReadinessSummary,
                    "CreateAgent.OnRead postfix");
            }
            catch (Exception ex)
            {
                ModLogger.Info("ExactCampaignNetworkObjectBootstrapPatch: CreateAgent.OnRead postfix failed open: " + ex.Message);
            }
        }

        private static void CreateAgent_OnWrite_Prefix(CreateAgent __instance)
        {
            try
            {
                ExactCreateAgentCorridorDiagnostics.TrySanitizeServerCreateAgentToServerSpawnBaseline(
                    __instance,
                    out string _);
                ExactCreateAgentCorridorDiagnostics.ObserveServerCreateAgentOnWrite(
                    __instance,
                    "CreateAgent.OnWrite prefix");
            }
            catch (Exception ex)
            {
                ModLogger.Info("ExactCampaignNetworkObjectBootstrapPatch: CreateAgent.OnWrite prefix failed open: " + ex.Message);
            }
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
