using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    internal enum CoopBattleSpawnStatus
    {
        None = 0,
        Pending = 1,
        Validating = 2,
        Validated = 3,
        Rejected = 4,
        Spawned = 5,
    }

    internal readonly struct PeerSpawnRuntimeState
    {
        public PeerSpawnRuntimeState(
            int peerIndex,
            BattleSideEnum side,
            string troopId,
            string entryId,
            CoopBattleSpawnStatus status,
            string source,
            string reason,
            DateTime updatedUtc)
        {
            PeerIndex = peerIndex;
            Side = side;
            TroopId = troopId;
            EntryId = entryId;
            Status = status;
            Source = source;
            Reason = reason;
            UpdatedUtc = updatedUtc;
        }

        public int PeerIndex { get; }
        public BattleSideEnum Side { get; }
        public string TroopId { get; }
        public string EntryId { get; }
        public CoopBattleSpawnStatus Status { get; }
        public string Source { get; }
        public string Reason { get; }
        public DateTime UpdatedUtc { get; }
    }

    internal static class CoopBattleSpawnRuntimeState
    {
        private static readonly Dictionary<int, PeerSpawnRuntimeState> _statesByPeer = new Dictionary<int, PeerSpawnRuntimeState>();

        public static void Reset()
        {
            _statesByPeer.Clear();
        }

        public static bool TryGetState(MissionPeer missionPeer, out PeerSpawnRuntimeState state)
        {
            state = default;
            NetworkCommunicator networkPeer = missionPeer?.GetNetworkPeer();
            if (networkPeer == null)
                return false;

            return _statesByPeer.TryGetValue(networkPeer.Index, out state);
        }

        public static void Clear(MissionPeer missionPeer, string source)
        {
            NetworkCommunicator networkPeer = missionPeer?.GetNetworkPeer();
            if (networkPeer == null)
                return;

            if (!_statesByPeer.Remove(networkPeer.Index))
                return;

            ModLogger.Info(
                "CoopBattleSpawnRuntimeState: cleared. " +
                "Peer=" + (networkPeer.UserName ?? networkPeer.Index.ToString()) +
                " Source=" + source);
        }

        public static void MarkPending(CoopBattleSpawnRequestState.PeerSpawnRequestState requestState)
        {
            SetState(requestState.PeerIndex, requestState.Side, requestState.TroopId, requestState.EntryId, CoopBattleSpawnStatus.Pending, requestState.Source, "queued");
        }

        public static void MarkValidating(MissionPeer missionPeer, string source)
        {
            NetworkCommunicator networkPeer = missionPeer?.GetNetworkPeer();
            if (networkPeer == null)
                return;

            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            SetState(networkPeer.Index, selectionState.Side, selectionState.TroopId, selectionState.EntryId, CoopBattleSpawnStatus.Validating, source, "validating");
        }

        public static void MarkValidated(MissionPeer missionPeer, string troopId, string entryId, string source)
        {
            NetworkCommunicator networkPeer = missionPeer?.GetNetworkPeer();
            if (networkPeer == null)
                return;

            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            SetState(networkPeer.Index, selectionState.Side, troopId, entryId, CoopBattleSpawnStatus.Validated, source, "validated");
        }

        public static void MarkRejected(MissionPeer missionPeer, string source, string reason)
        {
            NetworkCommunicator networkPeer = missionPeer?.GetNetworkPeer();
            if (networkPeer == null)
                return;

            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            SetState(networkPeer.Index, selectionState.Side, selectionState.TroopId, selectionState.EntryId, CoopBattleSpawnStatus.Rejected, source, reason);
        }

        public static void MarkSpawned(MissionPeer missionPeer, string troopId, string entryId, string source)
        {
            NetworkCommunicator networkPeer = missionPeer?.GetNetworkPeer();
            if (networkPeer == null)
                return;

            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            SetState(networkPeer.Index, selectionState.Side, troopId, entryId, CoopBattleSpawnStatus.Spawned, source, "spawned");
        }

        private static void SetState(
            int peerIndex,
            BattleSideEnum side,
            string troopId,
            string entryId,
            CoopBattleSpawnStatus status,
            string source,
            string reason)
        {
            PeerSpawnRuntimeState nextState = new PeerSpawnRuntimeState(
                peerIndex,
                side,
                string.IsNullOrWhiteSpace(troopId) ? null : troopId.Trim(),
                string.IsNullOrWhiteSpace(entryId) ? null : entryId.Trim(),
                status,
                source,
                reason,
                DateTime.UtcNow);

            if (_statesByPeer.TryGetValue(peerIndex, out PeerSpawnRuntimeState previousState) &&
                previousState.Status == nextState.Status &&
                previousState.Side == nextState.Side &&
                string.Equals(previousState.TroopId, nextState.TroopId, StringComparison.Ordinal) &&
                string.Equals(previousState.EntryId, nextState.EntryId, StringComparison.Ordinal) &&
                string.Equals(previousState.Reason, nextState.Reason, StringComparison.Ordinal))
            {
                return;
            }

            _statesByPeer[peerIndex] = nextState;

            ModLogger.Info(
                "CoopBattleSpawnRuntimeState: state updated. " +
                "Peer=" + peerIndex +
                " Status=" + status +
                " Side=" + side +
                " TroopId=" + (nextState.TroopId ?? "null") +
                " EntryId=" + (nextState.EntryId ?? "null") +
                " Reason=" + (reason ?? "null") +
                " Source=" + source);
        }
    }
}
