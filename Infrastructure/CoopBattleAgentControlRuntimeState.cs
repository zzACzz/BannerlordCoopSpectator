using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    public enum CoopBattleAgentControlMode
    {
        PlayerControlled = 0,
        AiObserved = 1
    }

    public enum CoopBattleAgentControlRequestKind
    {
        DelegateToAi = 0,
        ReclaimFromAi = 1
    }

    public readonly struct CoopBattleAgentControlState
    {
        public CoopBattleAgentControlState(
            int peerIndex,
            CoopBattleAgentControlMode mode,
            int agentIndex,
            string entryId,
            string troopId,
            BattleSideEnum side,
            int teamIndex,
            int formationIndex,
            bool wasGeneral,
            bool wasCaptain,
            int revision,
            int lastRequestId,
            string source,
            DateTime updatedUtc)
        {
            PeerIndex = peerIndex;
            Mode = mode;
            AgentIndex = agentIndex;
            EntryId = entryId;
            TroopId = troopId;
            Side = side;
            TeamIndex = teamIndex;
            FormationIndex = formationIndex;
            WasGeneral = wasGeneral;
            WasCaptain = wasCaptain;
            Revision = revision;
            LastRequestId = lastRequestId;
            Source = source;
            UpdatedUtc = updatedUtc;
        }

        public int PeerIndex { get; }
        public CoopBattleAgentControlMode Mode { get; }
        public int AgentIndex { get; }
        public string EntryId { get; }
        public string TroopId { get; }
        public BattleSideEnum Side { get; }
        public int TeamIndex { get; }
        public int FormationIndex { get; }
        public bool WasGeneral { get; }
        public bool WasCaptain { get; }
        public int Revision { get; }
        public int LastRequestId { get; }
        public string Source { get; }
        public DateTime UpdatedUtc { get; }
        public bool IsAiObserved => Mode == CoopBattleAgentControlMode.AiObserved && AgentIndex >= 0;
    }

    internal readonly struct CoopBattleAgentControlPendingClientRequest
    {
        public CoopBattleAgentControlPendingClientRequest(
            CoopBattleAgentControlRequestKind kind,
            int expectedAgentIndex,
            int requestId,
            DateTime requestedUtc)
        {
            Kind = kind;
            ExpectedAgentIndex = expectedAgentIndex;
            RequestId = requestId;
            RequestedUtc = requestedUtc;
        }

        public CoopBattleAgentControlRequestKind Kind { get; }
        public int ExpectedAgentIndex { get; }
        public int RequestId { get; }
        public DateTime RequestedUtc { get; }
    }

    internal static class CoopBattleAgentControlRuntimeState
    {
        private static readonly Dictionary<int, CoopBattleAgentControlState> ServerStatesByPeer =
            new Dictionary<int, CoopBattleAgentControlState>();

        private static CoopBattleAgentControlState _clientState;
        private static bool _hasClientState;
        private static CoopBattleAgentControlPendingClientRequest _pendingClientRequest;
        private static bool _hasPendingClientRequest;
        private static int _nextClientRequestId = 1;

        public static void ResetServer(string source)
        {
            ServerStatesByPeer.Clear();
        }

        public static void ResetClient(string source)
        {
            _clientState = default;
            _hasClientState = false;
            _pendingClientRequest = default;
            _hasPendingClientRequest = false;
            _nextClientRequestId = 1;
        }

        public static bool TryGetServerState(int peerIndex, out CoopBattleAgentControlState state)
        {
            return ServerStatesByPeer.TryGetValue(peerIndex, out state);
        }

        public static bool TryGetServerState(MissionPeer missionPeer, out CoopBattleAgentControlState state)
        {
            state = default;
            NetworkCommunicator peer = missionPeer?.GetNetworkPeer();
            return peer != null && TryGetServerState(peer.Index, out state);
        }

        public static bool TryGetClientState(out CoopBattleAgentControlState state)
        {
            state = _clientState;
            return _hasClientState;
        }

        public static bool IsClientAiObserved()
        {
            return _hasClientState && _clientState.IsAiObserved;
        }

        public static bool IsClientDelegateTransitionPending()
        {
            return _hasPendingClientRequest &&
                   _pendingClientRequest.Kind == CoopBattleAgentControlRequestKind.DelegateToAi;
        }

        public static bool IsClientReclaimTransitionPending()
        {
            return _hasPendingClientRequest &&
                   _pendingClientRequest.Kind == CoopBattleAgentControlRequestKind.ReclaimFromAi;
        }

        public static bool IsClientAiObservationOrTransitionActive()
        {
            return IsClientAiObserved() || IsClientDelegateTransitionPending() || IsClientReclaimTransitionPending();
        }

        public static bool TryGetPendingClientRequest(out CoopBattleAgentControlPendingClientRequest request)
        {
            request = _pendingClientRequest;
            return _hasPendingClientRequest;
        }

        public static int BeginClientRequest(CoopBattleAgentControlRequestKind kind, int expectedAgentIndex)
        {
            int requestId = _nextClientRequestId;
            _nextClientRequestId = _nextClientRequestId == int.MaxValue ? 1 : _nextClientRequestId + 1;
            _pendingClientRequest = new CoopBattleAgentControlPendingClientRequest(
                kind,
                expectedAgentIndex,
                requestId,
                DateTime.UtcNow);
            _hasPendingClientRequest = true;
            return requestId;
        }

        public static void CancelPendingClientRequest(int requestId)
        {
            if (!_hasPendingClientRequest || _pendingClientRequest.RequestId != requestId)
                return;

            _pendingClientRequest = default;
            _hasPendingClientRequest = false;
        }

        public static void ExpirePendingClientRequest(TimeSpan timeout)
        {
            if (!_hasPendingClientRequest || DateTime.UtcNow - _pendingClientRequest.RequestedUtc < timeout)
                return;

            _pendingClientRequest = default;
            _hasPendingClientRequest = false;
        }

        public static CoopBattleAgentControlState MarkServerAiObserved(
            NetworkCommunicator peer,
            Agent agent,
            string entryId,
            BattleSideEnum side,
            int teamIndex,
            int formationIndex,
            bool wasGeneral,
            bool wasCaptain,
            int requestId,
            string source)
        {
            int peerIndex = peer?.Index ?? -1;
            int revision = GetNextServerRevision(peerIndex);
            var state = new CoopBattleAgentControlState(
                peerIndex,
                CoopBattleAgentControlMode.AiObserved,
                agent?.Index ?? -1,
                NormalizeId(entryId),
                NormalizeId((agent?.Character as BasicCharacterObject)?.StringId),
                side,
                teamIndex,
                formationIndex,
                wasGeneral,
                wasCaptain,
                revision,
                Math.Max(0, requestId),
                source ?? string.Empty,
                DateTime.UtcNow);
            ServerStatesByPeer[peerIndex] = state;
            LogServerTransition(state, peer, source);
            return state;
        }

        public static CoopBattleAgentControlState MarkServerPlayerControlled(
            NetworkCommunicator peer,
            Agent agent,
            int requestId,
            string source)
        {
            int peerIndex = peer?.Index ?? -1;
            ServerStatesByPeer.TryGetValue(peerIndex, out CoopBattleAgentControlState previous);
            int revision = GetNextServerRevision(peerIndex);
            var state = new CoopBattleAgentControlState(
                peerIndex,
                CoopBattleAgentControlMode.PlayerControlled,
                agent?.Index ?? -1,
                previous.EntryId,
                NormalizeId((agent?.Character as BasicCharacterObject)?.StringId) ?? previous.TroopId,
                agent?.Team?.Side ?? previous.Side,
                agent?.Team?.TeamIndex ?? previous.TeamIndex,
                agent?.Formation?.Index ?? previous.FormationIndex,
                previous.WasGeneral,
                previous.WasCaptain,
                revision,
                Math.Max(previous.LastRequestId, requestId),
                source ?? string.Empty,
                DateTime.UtcNow);
            ServerStatesByPeer[peerIndex] = state;
            LogServerTransition(state, peer, source);
            return state;
        }

        public static bool TryMarkServerObservedAgentUnavailable(
            int agentIndex,
            string source,
            out int peerIndex,
            out CoopBattleAgentControlState state)
        {
            peerIndex = -1;
            state = default;
            foreach (KeyValuePair<int, CoopBattleAgentControlState> pair in ServerStatesByPeer)
            {
                if (!pair.Value.IsAiObserved || pair.Value.AgentIndex != agentIndex)
                    continue;

                peerIndex = pair.Key;
                CoopBattleAgentControlState previous = pair.Value;
                state = new CoopBattleAgentControlState(
                    pair.Key,
                    CoopBattleAgentControlMode.PlayerControlled,
                    -1,
                    previous.EntryId,
                    previous.TroopId,
                    previous.Side,
                    previous.TeamIndex,
                    previous.FormationIndex,
                    previous.WasGeneral,
                    previous.WasCaptain,
                    previous.Revision + 1,
                    previous.LastRequestId,
                    source ?? string.Empty,
                    DateTime.UtcNow);
                ServerStatesByPeer[pair.Key] = state;
                ModLogger.Info(
                    "CoopBattleAgentControlRuntimeState: observed AI agent became unavailable. " +
                    "PeerIndex=" + pair.Key +
                    " AgentIndex=" + agentIndex +
                    " Source=" + (source ?? "unknown"));
                return true;
            }

            return false;
        }

        public static bool ApplyClientAuthoritativeState(
            int localPeerIndex,
            CoopBattleAgentControlMode mode,
            int agentIndex,
            string entryId,
            BattleSideEnum side,
            int teamIndex,
            int formationIndex,
            bool wasGeneral,
            bool wasCaptain,
            int revision,
            int acknowledgedRequestId,
            string source,
            out CoopBattleAgentControlState previousState)
        {
            previousState = _clientState;
            if (_hasClientState && revision < _clientState.Revision)
                return false;

            bool wasChanged = !_hasClientState ||
                              _clientState.Mode != mode ||
                              _clientState.AgentIndex != agentIndex ||
                              _clientState.Revision != revision;
            _clientState = new CoopBattleAgentControlState(
                localPeerIndex,
                mode,
                agentIndex,
                NormalizeId(entryId),
                _clientState.TroopId,
                side,
                teamIndex,
                formationIndex,
                wasGeneral,
                wasCaptain,
                revision,
                Math.Max(0, acknowledgedRequestId),
                source ?? string.Empty,
                DateTime.UtcNow);
            _hasClientState = true;

            if (_hasPendingClientRequest &&
                (acknowledgedRequestId <= 0 || acknowledgedRequestId >= _pendingClientRequest.RequestId))
            {
                _pendingClientRequest = default;
                _hasPendingClientRequest = false;
            }

            return wasChanged;
        }

        public static bool TryGetActiveServerObservedAgent(
            Mission mission,
            MissionPeer missionPeer,
            out Agent agent,
            out CoopBattleAgentControlState state)
        {
            agent = null;
            state = default;
            if (mission == null || missionPeer == null || !TryGetServerState(missionPeer, out state) || !state.IsAiObserved)
                return false;

            return TryResolveAgent(mission, state.AgentIndex, requireActive: true, out agent);
        }

        public static bool TryGetActiveClientObservedAgent(Mission mission, out Agent agent)
        {
            agent = null;
            return mission != null &&
                   _hasClientState &&
                   _clientState.IsAiObserved &&
                   TryResolveAgent(mission, _clientState.AgentIndex, requireActive: true, out agent);
        }

        public static bool TryResolveAgent(Mission mission, int agentIndex, bool requireActive, out Agent agent)
        {
            agent = null;
            if (mission?.AllAgents == null || agentIndex < 0)
                return false;

            for (int i = 0; i < mission.AllAgents.Count; i++)
            {
                Agent candidate = mission.AllAgents[i];
                if (candidate == null || candidate.Index != agentIndex)
                    continue;

                if (requireActive && !candidate.IsActive())
                    return false;

                agent = candidate;
                return true;
            }

            return false;
        }

        public static bool TryMigratePeerIndex(int previousPeerIndex, int currentPeerIndex, string source)
        {
            if (previousPeerIndex < 0 || currentPeerIndex < 0 || previousPeerIndex == currentPeerIndex)
                return false;

            if (!ServerStatesByPeer.TryGetValue(previousPeerIndex, out CoopBattleAgentControlState previous))
                return false;

            var migrated = new CoopBattleAgentControlState(
                currentPeerIndex,
                previous.Mode,
                previous.AgentIndex,
                previous.EntryId,
                previous.TroopId,
                previous.Side,
                previous.TeamIndex,
                previous.FormationIndex,
                previous.WasGeneral,
                previous.WasCaptain,
                previous.Revision + 1,
                previous.LastRequestId,
                source ?? string.Empty,
                DateTime.UtcNow);
            ServerStatesByPeer.Remove(previousPeerIndex);
            ServerStatesByPeer[currentPeerIndex] = migrated;
            ModLogger.Info(
                "CoopBattleAgentControlRuntimeState: migrated peer control state. " +
                "PreviousPeerIndex=" + previousPeerIndex +
                " CurrentPeerIndex=" + currentPeerIndex +
                " Mode=" + migrated.Mode +
                " AgentIndex=" + migrated.AgentIndex +
                " Source=" + (source ?? "unknown"));
            return true;
        }

        private static int GetNextServerRevision(int peerIndex)
        {
            return ServerStatesByPeer.TryGetValue(peerIndex, out CoopBattleAgentControlState previous)
                ? previous.Revision + 1
                : 1;
        }

        private static string NormalizeId(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static void LogServerTransition(
            CoopBattleAgentControlState state,
            NetworkCommunicator peer,
            string source)
        {
            ModLogger.Info(
                "CoopBattleAgentControlRuntimeState: server control transition. " +
                "Peer=" + (peer?.UserName ?? state.PeerIndex.ToString()) +
                " Mode=" + state.Mode +
                " AgentIndex=" + state.AgentIndex +
                " EntryId=" + (state.EntryId ?? "null") +
                " FormationIndex=" + state.FormationIndex +
                " Revision=" + state.Revision +
                " RequestId=" + state.LastRequestId +
                " Source=" + (source ?? "unknown"));
        }
    }
}
