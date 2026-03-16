using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    internal static class CoopBattleSelectionRequestState
    {
        internal readonly struct PeerSelectionRequestState
        {
            public PeerSelectionRequestState(int peerIndex, BattleSideEnum side, string troopId, string entryId, string source, DateTime updatedUtc)
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

        private static readonly Dictionary<int, PeerSelectionRequestState> _requestsByPeer = new Dictionary<int, PeerSelectionRequestState>();

        public static void Reset()
        {
            _requestsByPeer.Clear();
        }

        public static bool TryGetRequest(MissionPeer missionPeer, out PeerSelectionRequestState requestState)
        {
            requestState = default;
            NetworkCommunicator networkPeer = missionPeer?.GetNetworkPeer();
            if (networkPeer == null)
                return false;

            return _requestsByPeer.TryGetValue(networkPeer.Index, out requestState);
        }

        public static bool TryQueueFromAuthoritySelection(MissionPeer missionPeer, string source)
        {
            NetworkCommunicator networkPeer = missionPeer?.GetNetworkPeer();
            if (networkPeer == null)
                return false;

            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            if (selectionState.Side == BattleSideEnum.None || string.IsNullOrWhiteSpace(selectionState.TroopId))
                return false;

            if (_requestsByPeer.TryGetValue(networkPeer.Index, out PeerSelectionRequestState previousRequest) &&
                previousRequest.Side == selectionState.Side &&
                string.Equals(previousRequest.TroopId, selectionState.TroopId, StringComparison.Ordinal) &&
                string.Equals(previousRequest.EntryId, selectionState.EntryId, StringComparison.Ordinal))
            {
                return true;
            }

            PeerSelectionRequestState requestState = new PeerSelectionRequestState(
                networkPeer.Index,
                selectionState.Side,
                selectionState.TroopId,
                selectionState.EntryId,
                source,
                DateTime.UtcNow);
            _requestsByPeer[networkPeer.Index] = requestState;

            ModLogger.Info(
                "CoopBattleSelectionRequestState: selection request queued. " +
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

            if (!_requestsByPeer.Remove(networkPeer.Index))
                return;

            ModLogger.Info(
                "CoopBattleSelectionRequestState: selection request cleared. " +
                "Peer=" + (networkPeer.UserName ?? networkPeer.Index.ToString()) +
                " Source=" + source);
        }
    }
}
