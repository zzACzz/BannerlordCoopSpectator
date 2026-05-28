using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    public static class ListedShellVisualBootstrapPatch
    {
        private static readonly HashSet<string> LoggedSuppressedVisualBootstrapKeys =
            new HashSet<string>(StringComparer.Ordinal);

        public static void Apply(Harmony harmony)
        {
            try
            {
                harmony.Patch(
                    AccessTools.Method(typeof(MissionMultiplayerGameModeBase), nameof(MissionMultiplayerGameModeBase.HandleAgentVisualSpawning)),
                    prefix: new HarmonyMethod(typeof(ListedShellVisualBootstrapPatch), nameof(HandleAgentVisualSpawning_Prefix)));
                ModLogger.Info("ListedShellVisualBootstrapPatch: patched MissionMultiplayerGameModeBase.HandleAgentVisualSpawning.");
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellVisualBootstrapPatch: failed to patch HandleAgentVisualSpawning: " + ex.Message);
            }

            try
            {
                harmony.Patch(
                    AccessTools.Method(typeof(MultiplayerMissionAgentVisualSpawnComponent), nameof(MultiplayerMissionAgentVisualSpawnComponent.SpawnAgentVisualsForPeer)),
                    prefix: new HarmonyMethod(typeof(ListedShellVisualBootstrapPatch), nameof(SpawnAgentVisualsForPeer_Prefix)));
                ModLogger.Info("ListedShellVisualBootstrapPatch: patched MultiplayerMissionAgentVisualSpawnComponent.SpawnAgentVisualsForPeer.");
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellVisualBootstrapPatch: failed to patch SpawnAgentVisualsForPeer: " + ex.Message);
            }
        }

        public static bool HandleAgentVisualSpawning_Prefix(
            MissionMultiplayerGameModeBase __instance,
            NetworkCommunicator spawningNetworkPeer,
            AgentBuildData spawningAgentBuildData,
            int troopCountInFormation,
            bool useCosmetics)
        {
            Mission mission = __instance?.Mission ?? Mission.Current;
            MissionPeer missionPeer = spawningNetworkPeer?.GetComponent<MissionPeer>();
            if (!ShouldSuppressListedShellNativeVisualBootstrap(mission, missionPeer, out string reason))
                return true;

            if (!CoopMissionSpawnLogic.TryArmListedShellNativeSpawnCompatibilityState(
                    mission,
                    missionPeer,
                    spawningNetworkPeer,
                    "ListedShellVisualBootstrapPatch.HandleAgentVisualSpawning"))
            {
                return true;
            }

            LogSuppressedVisualBootstrap(
                mission,
                missionPeer,
                spawningNetworkPeer,
                "MissionMultiplayerGameModeBase.HandleAgentVisualSpawning",
                reason);
            return false;
        }

        public static bool SpawnAgentVisualsForPeer_Prefix(
            MultiplayerMissionAgentVisualSpawnComponent __instance,
            MissionPeer missionPeer,
            AgentBuildData buildData,
            int selectedEquipmentSetIndex,
            bool isBot,
            int totalTroopCount)
        {
            Mission mission = __instance?.Mission ?? Mission.Current;
            NetworkCommunicator peer = missionPeer?.GetNetworkPeer();
            if (!ShouldSuppressListedShellNativeVisualBootstrap(mission, missionPeer, out string reason))
                return true;

            if (!CoopMissionSpawnLogic.TryArmListedShellNativeSpawnCompatibilityState(
                    mission,
                    missionPeer,
                    peer,
                    "ListedShellVisualBootstrapPatch.SpawnAgentVisualsForPeer"))
            {
                return true;
            }

            LogSuppressedVisualBootstrap(
                mission,
                missionPeer,
                peer,
                "MultiplayerMissionAgentVisualSpawnComponent.SpawnAgentVisualsForPeer",
                reason);
            return false;
        }

        private static bool ShouldSuppressListedShellNativeVisualBootstrap(
            Mission mission,
            MissionPeer missionPeer,
            out string reason)
        {
            reason = string.Empty;
            if (!GameNetwork.IsServer)
            {
                reason = "not-server";
                return false;
            }

            if (mission == null || missionPeer == null)
            {
                reason = "mission-or-peer-missing";
                return false;
            }

            bool wrappedListedShell =
                mission.GetMissionBehavior<ListedShellEquipmentCompatibilityComponent>() != null ||
                mission.GetMissionBehavior<ListedShellVisualCompatibilityComponent>() != null;
            if (!wrappedListedShell)
            {
                reason = "not-wrapped-listed-shell";
                return false;
            }

            if (!CoopMissionSpawnLogic.RequiresListedShellNativeSpawnCompatibility(mission, missionPeer))
            {
                reason = "listed-shell-compatibility-not-required";
                return false;
            }

            reason = "wrapped-listed-shell-native-visual-bootstrap";
            return true;
        }

        private static void LogSuppressedVisualBootstrap(
            Mission mission,
            MissionPeer missionPeer,
            NetworkCommunicator peer,
            string source,
            string reason)
        {
            string logKey =
                (mission?.SceneName ?? "unknown") +
                "|" +
                (peer?.Index.ToString() ?? "none") +
                "|" +
                (missionPeer?.SelectedTroopIndex.ToString() ?? "none") +
                "|" +
                source;
            if (!LoggedSuppressedVisualBootstrapKeys.Add(logKey))
                return;

            ModLogger.Info(
                "ListedShellVisualBootstrapPatch: suppressed listed-shell native visual bootstrap. " +
                "Source=" + source +
                " Mission=" + (mission?.SceneName ?? "unknown") +
                " Peer=" + (peer?.UserName ?? peer?.Index.ToString() ?? "none") +
                " TeamIndex=" + (missionPeer?.Team?.TeamIndex.ToString() ?? "null") +
                " TroopIndex=" + (missionPeer?.SelectedTroopIndex.ToString() ?? "null") +
                " Reason=" + (reason ?? "unknown") + ".");
        }
    }
}
