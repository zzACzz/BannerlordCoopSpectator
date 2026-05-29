using System;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    internal static class LateJoinPeerStateReplayOwnershipPatch
    {
        private static readonly HashSet<int> DeferredPeerStateReplayPeerIndices = new HashSet<int>();
        private static readonly HashSet<string> LoggedDiagnosticKeys = new HashSet<string>(StringComparer.Ordinal);

        private static MethodInfo _sendTeamsToPeerMethod;
        private static MethodInfo _sendTeamRelationsToPeerMethod;
        private static MethodInfo _sendFormationInformationMethod;
        private static MethodInfo _sendAgentsToPeerMethod;
        private static MethodInfo _sendSpawnedMissionObjectsToPeerMethod;
        private static MethodInfo _synchronizeMissionObjectsToPeerMethod;
        private static MethodInfo _sendMissilesToPeerMethod;
        private static MethodInfo _sendTroopSelectionInformationMethod;

        public static void Apply(Harmony harmony)
        {
            try
            {
                Type missionNetworkComponentType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.MissionNetworkComponent");
                if (missionNetworkComponentType == null)
                {
                    ModLogger.Info("LateJoinPeerStateReplayOwnershipPatch: MissionNetworkComponent type not found. Skip.");
                    return;
                }

                MethodInfo sendExistingObjectsToPeerMethod = ResolveMissionNetworkPeerMethod(
                    missionNetworkComponentType,
                    "SendExistingObjectsToPeer");
                MethodInfo prefix = typeof(LateJoinPeerStateReplayOwnershipPatch).GetMethod(
                    nameof(SendExistingObjectsToPeer_Prefix),
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (sendExistingObjectsToPeerMethod == null || prefix == null)
                {
                    ModLogger.Info("LateJoinPeerStateReplayOwnershipPatch: SendExistingObjectsToPeer patch targets unavailable. Skip.");
                    return;
                }

                _sendTeamsToPeerMethod = ResolveMissionNetworkPeerMethod(missionNetworkComponentType, "SendTeamsToPeer");
                _sendTeamRelationsToPeerMethod = ResolveMissionNetworkPeerMethod(missionNetworkComponentType, "SendTeamRelationsToPeer");
                _sendFormationInformationMethod = ResolveMissionNetworkPeerMethod(missionNetworkComponentType, "SendFormationInformation");
                _sendAgentsToPeerMethod = ResolveMissionNetworkPeerMethod(missionNetworkComponentType, "SendAgentsToPeer");
                _sendSpawnedMissionObjectsToPeerMethod = ResolveMissionNetworkPeerMethod(missionNetworkComponentType, "SendSpawnedMissionObjectsToPeer");
                _synchronizeMissionObjectsToPeerMethod = ResolveMissionNetworkPeerMethod(missionNetworkComponentType, "SynchronizeMissionObjectsToPeer");
                _sendMissilesToPeerMethod = ResolveMissionNetworkPeerMethod(missionNetworkComponentType, "SendMissilesToPeer");
                _sendTroopSelectionInformationMethod = ResolveMissionNetworkPeerMethod(missionNetworkComponentType, "SendTroopSelectionInformation");
                if (_sendTeamsToPeerMethod == null ||
                    _sendTeamRelationsToPeerMethod == null ||
                    _sendFormationInformationMethod == null ||
                    _sendAgentsToPeerMethod == null ||
                    _sendSpawnedMissionObjectsToPeerMethod == null ||
                    _synchronizeMissionObjectsToPeerMethod == null ||
                    _sendMissilesToPeerMethod == null ||
                    _sendTroopSelectionInformationMethod == null)
                {
                    ModLogger.Info("LateJoinPeerStateReplayOwnershipPatch: required MissionNetworkComponent helper methods unavailable. Skip.");
                    return;
                }

                harmony.Patch(sendExistingObjectsToPeerMethod, prefix: new HarmonyMethod(prefix));
                ModLogger.Info("LateJoinPeerStateReplayOwnershipPatch: patched MissionNetworkComponent.SendExistingObjectsToPeer.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("LateJoinPeerStateReplayOwnershipPatch.Apply failed.", ex);
            }
        }

        internal static void TryReplayDeferredPeerState(NetworkCommunicator peer, Mission mission, string source)
        {
            if (!GameNetwork.IsServer || peer == null)
                return;

            if (!DeferredPeerStateReplayPeerIndices.Contains(peer.Index))
                return;

            if (!ShouldOwnNativePeerStateReplay(mission))
            {
                DeferredPeerStateReplayPeerIndices.Remove(peer.Index);
                return;
            }

            if (!TrySendAuthoritativePeerStateReplayToPeer(peer, mission, requireSnapshotReady: true, source: source))
                return;

            DeferredPeerStateReplayPeerIndices.Remove(peer.Index);
            ModLogger.Info(
                "LateJoinPeerStateReplayOwnershipPatch: replayed deferred authoritative late-join bootstrap gate without native peer-team replay. " +
                "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                " Scene=" + (mission?.SceneName ?? "unknown") +
                " Source=" + (source ?? "unknown"));
        }

        internal static void ClearDeferredPeerState(NetworkCommunicator peer, string source)
        {
            if (peer == null)
                return;

            if (DeferredPeerStateReplayPeerIndices.Remove(peer.Index))
            {
                ModLogger.Info(
                    "LateJoinPeerStateReplayOwnershipPatch: cleared deferred authoritative peer state replay. " +
                    "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                    " Source=" + (source ?? "unknown"));
            }
        }

        internal static void ClearAllDeferredPeerState(string source)
        {
            if (DeferredPeerStateReplayPeerIndices.Count <= 0)
                return;

            int count = DeferredPeerStateReplayPeerIndices.Count;
            DeferredPeerStateReplayPeerIndices.Clear();
            ModLogger.Info(
                "LateJoinPeerStateReplayOwnershipPatch: cleared all deferred authoritative peer state replays. " +
                "Count=" + count +
                " Source=" + (source ?? "unknown"));
        }

        private static bool SendExistingObjectsToPeer_Prefix(object __instance, NetworkCommunicator networkPeer)
        {
            if (!ShouldOwnNativePeerStateReplay(__instance, networkPeer, out Mission mission))
                return true;

            try
            {
                SendExistingObjectsToPeerOwned(__instance, mission, networkPeer);
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Error(
                    "LateJoinPeerStateReplayOwnershipPatch: owned SendExistingObjectsToPeer failed; falling back to native path. " +
                    "Peer=" + (networkPeer?.UserName ?? "null"),
                    ex);
                return true;
            }
        }

        private static void SendExistingObjectsToPeerOwned(object instance, Mission mission, NetworkCommunicator networkPeer)
        {
            CoopSessionTransportPrimitives.SendExistingObjectsBegin(networkPeer);
            CoopSessionTransportPrimitives.SendSynchronizeMissionTimeTracker(networkPeer, (float)MissionTime.Now.ToSeconds);

            InvokePrivateNetworkPeerMethod(instance, _sendTeamsToPeerMethod, networkPeer);
            InvokePrivateNetworkPeerMethod(instance, _sendTeamRelationsToPeerMethod, networkPeer);

            if (!TrySendAuthoritativePeerStateReplayToPeer(
                    networkPeer,
                    mission,
                    requireSnapshotReady: true,
                    source: "LateJoinPeerStateReplayOwnershipPatch.SendExistingObjectsToPeerOwned"))
            {
                DeferredPeerStateReplayPeerIndices.Add(networkPeer.Index);
            }
            else
            {
                DeferredPeerStateReplayPeerIndices.Remove(networkPeer.Index);
            }

            InvokePrivateNetworkPeerMethod(instance, _sendFormationInformationMethod, networkPeer);
            InvokePrivateNetworkPeerMethod(instance, _sendAgentsToPeerMethod, networkPeer);
            InvokePrivateNetworkPeerMethod(instance, _sendSpawnedMissionObjectsToPeerMethod, networkPeer);
            InvokePrivateNetworkPeerMethod(instance, _synchronizeMissionObjectsToPeerMethod, networkPeer);
            InvokePrivateNetworkPeerMethod(instance, _sendMissilesToPeerMethod, networkPeer);
            InvokePrivateNetworkPeerMethod(instance, _sendTroopSelectionInformationMethod, networkPeer);
            networkPeer.SendExistingObjects(mission);

            CoopSessionTransportPrimitives.SendExistingObjectsEnd(networkPeer);
        }

        private static bool TrySendAuthoritativePeerStateReplayToPeer(
            NetworkCommunicator networkPeer,
            Mission mission,
            bool requireSnapshotReady,
            string source)
        {
            if (!GameNetwork.IsServer || networkPeer == null || networkPeer.IsServerPeer || mission == null)
                return false;

            if (!ShouldOwnNativePeerStateReplay(mission))
                return false;

            if (requireSnapshotReady &&
                !CoopMissionNetworkBridge.IsPeerCurrentBattleSnapshotBootstrapReady(networkPeer, out string readinessSummary))
            {
                string logKey =
                    "defer-peer-state|" +
                    networkPeer.Index + "|" +
                    (source ?? "unknown") + "|" +
                    (mission.SceneName ?? "unknown");
                if (LoggedDiagnosticKeys.Add(logKey))
                {
                    ModLogger.Info(
                        "LateJoinPeerStateReplayOwnershipPatch: deferred authoritative late-join bootstrap gate until snapshot-ready. " +
                        "Peer=" + (networkPeer.UserName ?? networkPeer.Index.ToString()) +
                        " Scene=" + (mission.SceneName ?? "unknown") +
                        " Readiness={" + (readinessSummary ?? "unknown") + "}" +
                        " Source=" + (source ?? "unknown"));
                }

                return false;
            }

            string appliedLogKey =
                "owned-peer-state|" +
                networkPeer.Index + "|" +
                (source ?? "unknown") + "|" +
                (mission.SceneName ?? "unknown");
            if (LoggedDiagnosticKeys.Add(appliedLogKey))
            {
                ModLogger.Info(
                    "LateJoinPeerStateReplayOwnershipPatch: snapshot-ready late-join bootstrap no longer sends native SetPeerTeam replay. " +
                    "Peer=" + (networkPeer.UserName ?? networkPeer.Index.ToString()) +
                    " Scene=" + (mission.SceneName ?? "unknown") +
                    " Source=" + (source ?? "unknown"));
            }

            return true;
        }

        private static void InvokePrivateNetworkPeerMethod(object instance, MethodInfo method, NetworkCommunicator networkPeer)
        {
            if (instance == null || method == null)
                return;

            method.Invoke(instance, new object[] { networkPeer });
        }

        private static MethodInfo ResolveMissionNetworkPeerMethod(Type targetType, string methodName)
        {
            return targetType?.GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(NetworkCommunicator) },
                null);
        }

        private static bool ShouldOwnNativePeerStateReplay(object instance, NetworkCommunicator networkPeer, out Mission mission)
        {
            mission = (instance as MissionBehavior)?.Mission ?? Mission.Current;
            return ShouldOwnNativePeerStateReplay(mission) &&
                GameNetwork.IsServer &&
                networkPeer != null &&
                !networkPeer.IsServerPeer;
        }

        private static bool ShouldOwnNativePeerStateReplay(Mission mission)
        {
            if (mission == null)
                return false;

            if (!MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                return false;

            return mission.GetMissionBehavior<CoopMissionNetworkBridge>() != null;
        }
    }
}
