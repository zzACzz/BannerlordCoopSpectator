using System;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    internal static class LateJoinSelectedTroopReplaySuppressionPatch
    {
        private static readonly HashSet<string> LoggedSuppressionKeys = new HashSet<string>(StringComparer.Ordinal);

        public static void Apply(Harmony harmony)
        {
            try
            {
                Type missionNetworkComponentType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.MissionNetworkComponent");
                if (missionNetworkComponentType == null)
                {
                    ModLogger.Info("LateJoinSelectedTroopReplaySuppressionPatch: MissionNetworkComponent type not found. Skip.");
                    return;
                }

                MethodInfo sendTroopSelectionInformation = missionNetworkComponentType.GetMethod(
                    "SendTroopSelectionInformation",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(NetworkCommunicator) },
                    null);
                MethodInfo prefix = typeof(LateJoinSelectedTroopReplaySuppressionPatch).GetMethod(
                    nameof(SendTroopSelectionInformation_Prefix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (sendTroopSelectionInformation == null || prefix == null)
                {
                    ModLogger.Info("LateJoinSelectedTroopReplaySuppressionPatch: SendTroopSelectionInformation patch targets unavailable. Skip.");
                    return;
                }

                harmony.Patch(sendTroopSelectionInformation, prefix: new HarmonyMethod(prefix));
                ModLogger.Info("LateJoinSelectedTroopReplaySuppressionPatch: patched MissionNetworkComponent.SendTroopSelectionInformation.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("LateJoinSelectedTroopReplaySuppressionPatch.Apply failed.", ex);
            }
        }

        private static bool SendTroopSelectionInformation_Prefix(object __instance, NetworkCommunicator networkPeer)
        {
            if (!ShouldSuppressNativeSelectedTroopLateJoinReplay(__instance, networkPeer, out Mission mission))
                return true;

            string logKey =
                (mission?.SceneName ?? "unknown") + "|" +
                (networkPeer?.Index.ToString() ?? "null") + "|" +
                (networkPeer?.UserName ?? "null");
            if (LoggedSuppressionKeys.Add(logKey))
            {
                ModLogger.Info(
                    "LateJoinSelectedTroopReplaySuppressionPatch: suppressed native late-join UpdateSelectedTroopIndex replay. " +
                    "Peer=" + (networkPeer?.UserName ?? "null") +
                    " Scene=" + (mission?.SceneName ?? "unknown") +
                    " Reason=CoopMissionNetworkBridge owns authoritative selection sync.");
            }

            return false;
        }

        private static bool ShouldSuppressNativeSelectedTroopLateJoinReplay(
            object instance,
            NetworkCommunicator peer,
            out Mission mission)
        {
            mission = (instance as MissionBehavior)?.Mission ?? Mission.Current;
            if (!GameNetwork.IsServer || peer == null || peer.IsServerPeer || mission == null)
                return false;

            if (!MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                return false;

            return mission.GetMissionBehavior<CoopMissionNetworkBridge>() != null;
        }
    }
}
