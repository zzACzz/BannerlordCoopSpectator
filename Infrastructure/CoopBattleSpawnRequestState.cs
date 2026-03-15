using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    internal static class CoopBattleSpawnRequestState
    {
        internal readonly struct PeerSpawnRequestState
        {
            public PeerSpawnRequestState(int peerIndex, BattleSideEnum side, string troopId, string entryId, string source, DateTime updatedUtc)
            {
                PeerIndex = peerIndex;
                Side = side;
                TroopId = troopId;
                EntryId = entryId;
                Source = source;
                UpdatedUtc = updatedUtc;
            }

            public int PeerIndex { get; }
            public BattleSideEnum Side { get; }
            public string TroopId { get; }
            public string EntryId { get; }
            public string Source { get; }
            public DateTime UpdatedUtc { get; }
        }

        private static readonly Dictionary<int, PeerSpawnRequestState> _pendingRequestsByPeer = new Dictionary<int, PeerSpawnRequestState>();

        public static void Reset()
        {
            _pendingRequestsByPeer.Clear();
        }

        public static bool HasPendingRequest(MissionPeer missionPeer)
        {
            return TryGetPendingRequest(missionPeer, out _);
        }

        public static bool TryGetPendingRequest(MissionPeer missionPeer, out PeerSpawnRequestState requestState)
        {
            requestState = default;
            NetworkCommunicator networkPeer = missionPeer?.GetNetworkPeer();
            if (networkPeer == null)
                return false;

            return _pendingRequestsByPeer.TryGetValue(networkPeer.Index, out requestState);
        }

        public static bool TryQueueFromSelection(MissionPeer missionPeer, string source)
        {
            NetworkCommunicator networkPeer = missionPeer?.GetNetworkPeer();
            if (networkPeer == null)
                return false;

            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            if (selectionState.Side == BattleSideEnum.None || string.IsNullOrWhiteSpace(selectionState.TroopId))
                return false;

            if (_pendingRequestsByPeer.TryGetValue(networkPeer.Index, out PeerSpawnRequestState previousRequest) &&
                previousRequest.Side == selectionState.Side &&
                string.Equals(previousRequest.TroopId, selectionState.TroopId, StringComparison.Ordinal) &&
                string.Equals(previousRequest.EntryId, selectionState.EntryId, StringComparison.Ordinal))
            {
                return true;
            }

            PeerSpawnRequestState requestState = new PeerSpawnRequestState(
                networkPeer.Index,
                selectionState.Side,
                selectionState.TroopId,
                selectionState.EntryId,
                source,
                DateTime.UtcNow);
            _pendingRequestsByPeer[networkPeer.Index] = requestState;

            ModLogger.Info(
                "CoopBattleSpawnRequestState: pending spawn request queued. " +
                "Peer=" + (networkPeer.UserName ?? networkPeer.Index.ToString()) +
                " Side=" + selectionState.Side +
                " TroopId=" + selectionState.TroopId +
                " EntryId=" + (selectionState.EntryId ?? "null") +
                " Source=" + source);
            return true;
        }

        public static void Clear(MissionPeer missionPeer, string source)
        {
            NetworkCommunicator networkPeer = missionPeer?.GetNetworkPeer();
            if (networkPeer == null)
                return;

            if (!_pendingRequestsByPeer.Remove(networkPeer.Index))
                return;

            ModLogger.Info(
                "CoopBattleSpawnRequestState: pending spawn request cleared. " +
                "Peer=" + (networkPeer.UserName ?? networkPeer.Index.ToString()) +
                " Source=" + source);
        }
    }
}
