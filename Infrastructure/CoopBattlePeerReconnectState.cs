using System;
using System.Collections.Generic;
using CoopSpectator.GameMode;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    internal static class CoopBattlePeerReconnectState
    {
        internal readonly struct ActiveBattleReconnectFinalizeGateState
        {
            public ActiveBattleReconnectFinalizeGateState(
                int peerIndex,
                int transmissionId,
                bool readyAcknowledged,
                DateTime armedUtc,
                DateTime readyAcknowledgedUtc,
                string source,
                string reason)
            {
                PeerIndex = peerIndex;
                TransmissionId = transmissionId;
                ReadyAcknowledged = readyAcknowledged;
                ArmedUtc = armedUtc;
                ReadyAcknowledgedUtc = readyAcknowledgedUtc;
                Source = source ?? string.Empty;
                Reason = reason ?? string.Empty;
            }

            public int PeerIndex { get; }
            public int TransmissionId { get; }
            public bool ReadyAcknowledged { get; }
            public DateTime ArmedUtc { get; }
            public DateTime ReadyAcknowledgedUtc { get; }
            public string Source { get; }
            public string Reason { get; }
        }

        private static readonly Dictionary<string, int> _lastKnownPeerIndexByPlayerId =
            new Dictionary<string, int>(StringComparer.Ordinal);

        private static readonly Dictionary<int, string> _playerIdByPeerIndex =
            new Dictionary<int, string>();

        private static readonly Dictionary<int, ActiveBattleReconnectFinalizeGateState> _activeBattleReconnectFinalizeGateByPeerIndex =
            new Dictionary<int, ActiveBattleReconnectFinalizeGateState>();

        private static bool _peerSynchronizationHookInstalled;

        public static void EnsureHooksInstalled()
        {
            if (_peerSynchronizationHookInstalled)
                return;

            NetworkCommunicator.OnPeerSynchronized += HandlePeerSynchronized;
            _peerSynchronizationHookInstalled = true;
        }

        public static void Reset(string source)
        {
            int trackedPlayers = _lastKnownPeerIndexByPlayerId.Count;
            int trackedFinalizeGates = _activeBattleReconnectFinalizeGateByPeerIndex.Count;
            _lastKnownPeerIndexByPlayerId.Clear();
            _playerIdByPeerIndex.Clear();
            _activeBattleReconnectFinalizeGateByPeerIndex.Clear();

            if (trackedPlayers <= 0 && trackedFinalizeGates <= 0)
                return;

            ModLogger.Info(
                "CoopBattlePeerReconnectState: cleared tracked reconnect identities. " +
                "TrackedPlayers=" + trackedPlayers +
                " TrackedFinalizeGates=" + trackedFinalizeGates +
                " Source=" + (source ?? "unknown"));
        }

        public static void ArmActiveBattleReconnectFinalizeGate(
            NetworkCommunicator networkPeer,
            int transmissionId,
            string source,
            string reason)
        {
            if (!ShouldTrackPeer(networkPeer))
                return;

            int peerIndex = networkPeer.Index;
            DateTime nowUtc = DateTime.UtcNow;
            bool hadExistingState = _activeBattleReconnectFinalizeGateByPeerIndex.TryGetValue(
                peerIndex,
                out ActiveBattleReconnectFinalizeGateState existingState);
            int effectiveTransmissionId =
                transmissionId > 0
                    ? transmissionId
                    : hadExistingState
                        ? existingState.TransmissionId
                        : 0;
            bool readyAcknowledged =
                hadExistingState &&
                existingState.ReadyAcknowledged &&
                existingState.TransmissionId > 0 &&
                effectiveTransmissionId == existingState.TransmissionId;
            DateTime readyAcknowledgedUtc =
                readyAcknowledged
                    ? existingState.ReadyAcknowledgedUtc
                    : DateTime.MinValue;
            DateTime armedUtc =
                hadExistingState && existingState.ArmedUtc != DateTime.MinValue
                    ? existingState.ArmedUtc
                    : nowUtc;

            ActiveBattleReconnectFinalizeGateState nextState = new ActiveBattleReconnectFinalizeGateState(
                peerIndex,
                effectiveTransmissionId,
                readyAcknowledged,
                armedUtc,
                readyAcknowledgedUtc,
                source,
                reason);
            if (hadExistingState &&
                existingState.TransmissionId == nextState.TransmissionId &&
                existingState.ReadyAcknowledged == nextState.ReadyAcknowledged &&
                string.Equals(existingState.Source, nextState.Source, StringComparison.Ordinal) &&
                string.Equals(existingState.Reason, nextState.Reason, StringComparison.Ordinal))
            {
                return;
            }

            _activeBattleReconnectFinalizeGateByPeerIndex[peerIndex] = nextState;
            ModLogger.Info(
                "CoopBattlePeerReconnectState: armed active-battle reconnect finalize gate. " +
                "Peer=" + (networkPeer.UserName ?? peerIndex.ToString()) +
                " PeerIndex=" + peerIndex +
                " TransmissionId=" + effectiveTransmissionId +
                " ReadyAcknowledged=" + readyAcknowledged +
                " Reason=" + (reason ?? "null") +
                " Source=" + (source ?? "unknown"));
        }

        public static bool TryAcknowledgeActiveBattleReconnectFinalizeGate(
            NetworkCommunicator networkPeer,
            int transmissionId,
            string source)
        {
            if (!ShouldTrackPeer(networkPeer) || transmissionId <= 0)
                return false;

            int peerIndex = networkPeer.Index;
            if (!_activeBattleReconnectFinalizeGateByPeerIndex.TryGetValue(
                    peerIndex,
                    out ActiveBattleReconnectFinalizeGateState existingState))
            {
                return false;
            }

            int expectedTransmissionId = existingState.TransmissionId;
            if (expectedTransmissionId > 0 && transmissionId < expectedTransmissionId)
            {
                ModLogger.Info(
                    "CoopBattlePeerReconnectState: ignored stale active-battle reconnect finalize ack. " +
                    "Peer=" + (networkPeer.UserName ?? peerIndex.ToString()) +
                    " PeerIndex=" + peerIndex +
                    " TransmissionId=" + transmissionId +
                    " ExpectedTransmissionId=" + expectedTransmissionId +
                    " Source=" + (source ?? "unknown"));
                return false;
            }

            int acknowledgedTransmissionId = Math.Max(expectedTransmissionId, transmissionId);
            _activeBattleReconnectFinalizeGateByPeerIndex.Remove(peerIndex);

            ModLogger.Info(
                "CoopBattlePeerReconnectState: acknowledged and cleared active-battle reconnect finalize gate. " +
                "Peer=" + (networkPeer.UserName ?? peerIndex.ToString()) +
                " PeerIndex=" + peerIndex +
                " TransmissionId=" + acknowledgedTransmissionId +
                " ArmedUtc=" + existingState.ArmedUtc.ToString("O") +
                " Source=" + (source ?? "unknown"));
            return true;
        }

        public static bool TryGetActiveBattleReconnectFinalizeGateState(
            NetworkCommunicator networkPeer,
            out ActiveBattleReconnectFinalizeGateState state)
        {
            state = default;
            if (networkPeer == null)
                return false;

            return _activeBattleReconnectFinalizeGateByPeerIndex.TryGetValue(networkPeer.Index, out state);
        }

        public static bool TryGetActiveBattleReconnectFinalizeGateState(
            MissionPeer missionPeer,
            out ActiveBattleReconnectFinalizeGateState state)
        {
            state = default;
            NetworkCommunicator networkPeer = missionPeer?.GetNetworkPeer();
            if (networkPeer == null)
                return false;

            return TryGetActiveBattleReconnectFinalizeGateState(networkPeer, out state);
        }

        public static void ClearActiveBattleReconnectFinalizeGate(NetworkCommunicator networkPeer, string source)
        {
            if (networkPeer == null)
                return;

            if (!_activeBattleReconnectFinalizeGateByPeerIndex.Remove(networkPeer.Index))
                return;

            ModLogger.Info(
                "CoopBattlePeerReconnectState: cleared active-battle reconnect finalize gate. " +
                "Peer=" + (networkPeer.UserName ?? networkPeer.Index.ToString()) +
                " PeerIndex=" + networkPeer.Index +
                " Source=" + (source ?? "unknown"));
        }

        public static void ObserveDisconnect(NetworkCommunicator networkPeer, string source)
        {
            if (!ShouldTrackPeer(networkPeer))
                return;

            string playerIdKey = TryGetStablePlayerIdKey(networkPeer);
            if (string.IsNullOrWhiteSpace(playerIdKey))
                return;

            _playerIdByPeerIndex.Remove(networkPeer.Index);
            _lastKnownPeerIndexByPlayerId[playerIdKey] = networkPeer.Index;

            ModLogger.Info(
                "CoopBattlePeerReconnectState: recorded disconnected peer identity for reconnect migration. " +
                "Peer=" + (networkPeer.UserName ?? networkPeer.Index.ToString()) +
                " PeerIndex=" + networkPeer.Index +
                " PlayerId=" + playerIdKey +
                " Source=" + (source ?? "unknown"));
        }

        public static void ObserveSynchronizedPeer(NetworkCommunicator networkPeer, string source)
        {
            if (!ShouldTrackPeer(networkPeer))
                return;

            string playerIdKey = TryGetStablePlayerIdKey(networkPeer);
            if (string.IsNullOrWhiteSpace(playerIdKey))
                return;

            int currentPeerIndex = networkPeer.Index;
            bool migrated = false;
            int previousPeerIndex = -1;
            if (_lastKnownPeerIndexByPlayerId.TryGetValue(playerIdKey, out previousPeerIndex) &&
                previousPeerIndex >= 0 &&
                previousPeerIndex != currentPeerIndex)
            {
                migrated |= CoopBattleAuthorityState.TryMigratePeerIndex(previousPeerIndex, currentPeerIndex, source);
                migrated |= CoopBattleSelectionRequestState.TryMigratePeerIndex(previousPeerIndex, currentPeerIndex, source);
                migrated |= CoopBattleSpawnRequestState.TryMigratePeerIndex(previousPeerIndex, currentPeerIndex, source);
                migrated |= CoopBattleSpawnRuntimeState.TryMigratePeerIndex(previousPeerIndex, currentPeerIndex, source);
                migrated |= CoopBattlePeerLifecycleRuntimeState.TryMigratePeerIndex(previousPeerIndex, currentPeerIndex, source);
                migrated |= CoopBattlePeerStatsRuntimeState.TryMigratePeerIndex(previousPeerIndex, currentPeerIndex, source);
                migrated |= CoopBattlePeerSessionState.TryMigratePeerIndex(previousPeerIndex, currentPeerIndex, source);
                migrated |= TryMigrateActiveBattleReconnectFinalizeGate(previousPeerIndex, currentPeerIndex, source);

                _playerIdByPeerIndex.Remove(previousPeerIndex);
            }

            _lastKnownPeerIndexByPlayerId[playerIdKey] = currentPeerIndex;
            _playerIdByPeerIndex[currentPeerIndex] = playerIdKey;

            ModLogger.Info(
                "CoopBattlePeerReconnectState: observed synchronized peer. " +
                "Peer=" + (networkPeer.UserName ?? currentPeerIndex.ToString()) +
                " PeerIndex=" + currentPeerIndex +
                " PlayerId=" + playerIdKey +
                " PreviousPeerIndex=" + previousPeerIndex +
                " MigratedState=" + migrated +
                " Source=" + (source ?? "unknown"));
        }

        private static void HandlePeerSynchronized(NetworkCommunicator networkPeer)
        {
            if (!GameNetwork.IsServer)
                return;

            ObserveSynchronizedPeer(networkPeer, "NetworkCommunicator.OnPeerSynchronized");
        }

        private static bool ShouldTrackPeer(NetworkCommunicator networkPeer)
        {
            if (!GameNetwork.IsServer || networkPeer == null || networkPeer.IsServerPeer)
                return false;

            Mission mission = Mission.Current;
            return mission != null && MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName);
        }

        private static string TryGetStablePlayerIdKey(NetworkCommunicator networkPeer)
        {
            if (networkPeer?.VirtualPlayer == null)
                return null;

            object playerId = null;
            try
            {
                playerId = networkPeer.VirtualPlayer
                    .GetType()
                    .GetProperty("Id")
                    ?.GetValue(networkPeer.VirtualPlayer, null);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopBattlePeerReconnectState: failed to reflect VirtualPlayer.Id. " +
                    "Peer=" + (networkPeer.UserName ?? networkPeer.Index.ToString()) +
                    " Error=" + ex.Message);
            }

            string playerIdKey = playerId?.ToString();
            string peerName = networkPeer.UserName?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(playerIdKey) && string.IsNullOrWhiteSpace(peerName))
                return null;

            return (playerIdKey ?? string.Empty).Trim() + "|" + peerName;
        }

        private static bool TryMigrateActiveBattleReconnectFinalizeGate(
            int previousPeerIndex,
            int currentPeerIndex,
            string source)
        {
            if (previousPeerIndex < 0 || currentPeerIndex < 0 || previousPeerIndex == currentPeerIndex)
                return false;

            if (!_activeBattleReconnectFinalizeGateByPeerIndex.TryGetValue(
                    previousPeerIndex,
                    out ActiveBattleReconnectFinalizeGateState previousState))
            {
                return false;
            }

            ActiveBattleReconnectFinalizeGateState migratedState = new ActiveBattleReconnectFinalizeGateState(
                currentPeerIndex,
                previousState.TransmissionId,
                previousState.ReadyAcknowledged,
                previousState.ArmedUtc,
                previousState.ReadyAcknowledgedUtc,
                previousState.Source,
                previousState.Reason);
            _activeBattleReconnectFinalizeGateByPeerIndex.Remove(previousPeerIndex);
            _activeBattleReconnectFinalizeGateByPeerIndex[currentPeerIndex] = migratedState;

            ModLogger.Info(
                "CoopBattlePeerReconnectState: migrated active-battle reconnect finalize gate. " +
                "PreviousPeerIndex=" + previousPeerIndex +
                " CurrentPeerIndex=" + currentPeerIndex +
                " TransmissionId=" + migratedState.TransmissionId +
                " ReadyAcknowledged=" + migratedState.ReadyAcknowledged +
                " Source=" + (source ?? "unknown"));
            return true;
        }
    }
}
