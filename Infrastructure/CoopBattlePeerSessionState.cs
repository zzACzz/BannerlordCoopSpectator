using System;
using System.Collections.Generic;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    internal enum CoopBattlePeerSessionStage
    {
        None = 0,
        NoPeer = 1,
        NoSide = 2,
        SideAssigned = 3,
        EntrySelected = 4,
        SpawnQueued = 5,
        Alive = 6,
        DeadAwaitingRespawn = 7,
        Waiting = 8,
        BattleEnded = 9,
    }

    internal sealed class CoopBattlePeerSessionSnapshot
    {
        public int PeerIndex { get; set; } = -1;
        public string PeerName { get; set; } = string.Empty;
        public BattleSideEnum RequestedSide { get; set; }
        public BattleSideEnum AssignedSide { get; set; }
        public BattleSideEnum RuntimeSide { get; set; }
        public BattleSideEnum CommittedSide =>
            AssignedSide != BattleSideEnum.None
                ? AssignedSide
                : RuntimeSide;
        public BattleSideEnum EffectiveSide { get; set; }
        public string EffectiveTroopId { get; set; }
        public string EffectiveEntryId { get; set; }
        public string ExplicitTroopId { get; set; }
        public string ExplicitEntryId { get; set; }
        public BattleSideEnum SelectionRequestSide { get; set; }
        public string SelectionRequestTroopId { get; set; }
        public string SelectionRequestEntryId { get; set; }
        public BattleSideEnum SpawnRequestSide { get; set; }
        public string SpawnRequestTroopId { get; set; }
        public string SpawnRequestEntryId { get; set; }
        public bool HasActiveControlledAgent { get; set; }
        public int ControlledAgentIndex { get; set; } = -1;
        public string ControlledEntryId { get; set; }
        public CoopBattleSpawnStatus SpawnStatus { get; set; }
        public string SpawnStatusSource { get; set; } = string.Empty;
        public string SpawnStatusReason { get; set; } = string.Empty;
        public CoopBattlePeerLifecycleStatus LifecycleStatus { get; set; }
        public string LifecycleSource { get; set; } = string.Empty;
        public int DeathCount { get; set; }
        public CoopBattlePeerSessionStage SessionStage { get; set; }

        public bool HasExplicitSelection =>
            !string.IsNullOrWhiteSpace(ExplicitTroopId) ||
            !string.IsNullOrWhiteSpace(ExplicitEntryId);

        public bool HasEffectiveSelection =>
            !string.IsNullOrWhiteSpace(EffectiveTroopId) ||
            !string.IsNullOrWhiteSpace(EffectiveEntryId);

        public bool HasSelectionRequest =>
            !string.IsNullOrWhiteSpace(SelectionRequestTroopId) ||
            !string.IsNullOrWhiteSpace(SelectionRequestEntryId);

        public bool HasPendingSpawnRequest =>
            !string.IsNullOrWhiteSpace(SpawnRequestTroopId) ||
            !string.IsNullOrWhiteSpace(SpawnRequestEntryId);

        public string PreferredTroopId =>
            !string.IsNullOrWhiteSpace(SpawnRequestTroopId)
                ? SpawnRequestTroopId
                : !string.IsNullOrWhiteSpace(SelectionRequestTroopId)
                    ? SelectionRequestTroopId
                    : !string.IsNullOrWhiteSpace(ExplicitTroopId)
                        ? ExplicitTroopId
                        : EffectiveTroopId;

        public string PreferredEntryId =>
            !string.IsNullOrWhiteSpace(SpawnRequestEntryId)
                ? SpawnRequestEntryId
                : !string.IsNullOrWhiteSpace(SelectionRequestEntryId)
                    ? SelectionRequestEntryId
                    : !string.IsNullOrWhiteSpace(ExplicitEntryId)
                        ? ExplicitEntryId
                        : !string.IsNullOrWhiteSpace(EffectiveEntryId)
                            ? EffectiveEntryId
                            : ControlledEntryId;

        public bool HasQueuedSpawnRuntime =>
            HasPendingSpawnRequest ||
            SpawnStatus == CoopBattleSpawnStatus.Pending ||
            SpawnStatus == CoopBattleSpawnStatus.Validating ||
            SpawnStatus == CoopBattleSpawnStatus.Validated;

        public bool OccupiesActiveCoopLife =>
            HasActiveControlledAgent ||
            SpawnStatus == CoopBattleSpawnStatus.Spawned;

        public bool ClaimsEntryId(string entryId)
        {
            if (string.IsNullOrWhiteSpace(entryId))
                return false;

            return string.Equals(ExplicitEntryId, entryId, StringComparison.Ordinal) ||
                   string.Equals(SpawnRequestEntryId, entryId, StringComparison.Ordinal) ||
                   string.Equals(ControlledEntryId, entryId, StringComparison.Ordinal);
        }
    }

    internal static class CoopBattlePeerSessionState
    {
        private static readonly Dictionary<int, string> _lastLoggedTransitionKeyByPeer = new Dictionary<int, string>();

        public static bool TryBuild(Mission mission, MissionPeer missionPeer, string source, out CoopBattlePeerSessionSnapshot snapshot)
        {
            snapshot = null;
            NetworkCommunicator networkPeer = missionPeer?.GetNetworkPeer();
            if (missionPeer == null || networkPeer == null)
                return false;

            CoopBattleAuthorityState.PeerSelectionState selectionState = CoopBattleAuthorityState.GetSelectionState(missionPeer);
            CoopBattleAuthorityState.TryGetExplicitSelectedTroopId(missionPeer, out string explicitTroopId);
            CoopBattleAuthorityState.TryGetExplicitSelectedEntryId(missionPeer, out string explicitEntryId);
            CoopBattleSelectionRequestState.TryGetRequest(missionPeer, out CoopBattleSelectionRequestState.PeerSelectionRequestState selectionRequest);
            CoopBattleSpawnRequestState.TryGetPendingRequest(missionPeer, out CoopBattleSpawnRequestState.PeerSpawnRequestState spawnRequest);
            CoopBattleSpawnRuntimeState.TryGetState(missionPeer, out PeerSpawnRuntimeState spawnRuntimeState);
            CoopBattlePeerLifecycleRuntimeState.TryGetState(missionPeer, out PeerLifecycleRuntimeState lifecycleRuntimeState);

            Agent controlledAgent = missionPeer.ControlledAgent;
            bool hasActiveControlledAgent = controlledAgent != null && controlledAgent.IsActive();
            string controlledEntryId = null;
            if (hasActiveControlledAgent)
                CoopMissionSpawnLogic.TryResolveAuthoritativeTrackedEntryId(controlledAgent, out controlledEntryId);

            BattleSideEnum runtimeSide =
                mission != null &&
                missionPeer.Team != null &&
                !ReferenceEquals(missionPeer.Team, mission.SpectatorTeam)
                    ? missionPeer.Team.Side
                    : missionPeer.Team?.Side ?? BattleSideEnum.None;
            BattleSideEnum effectiveSide = selectionState.Side != BattleSideEnum.None
                ? selectionState.Side
                : runtimeSide != BattleSideEnum.None
                    ? runtimeSide
                    : selectionState.RequestedSide;
            CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();

            snapshot = new CoopBattlePeerSessionSnapshot
            {
                PeerIndex = networkPeer.Index,
                PeerName = missionPeer.Name?.ToString() ?? networkPeer.UserName ?? networkPeer.Index.ToString(),
                RequestedSide = selectionState.RequestedSide,
                AssignedSide = selectionState.Side,
                RuntimeSide = runtimeSide,
                EffectiveSide = effectiveSide,
                EffectiveTroopId = selectionState.TroopId,
                EffectiveEntryId = selectionState.EntryId,
                ExplicitTroopId = explicitTroopId,
                ExplicitEntryId = explicitEntryId,
                SelectionRequestSide = selectionRequest.Side,
                SelectionRequestTroopId = selectionRequest.TroopId,
                SelectionRequestEntryId = selectionRequest.EntryId,
                SpawnRequestSide = spawnRequest.Side,
                SpawnRequestTroopId = spawnRequest.TroopId,
                SpawnRequestEntryId = spawnRequest.EntryId,
                HasActiveControlledAgent = hasActiveControlledAgent,
                ControlledAgentIndex = hasActiveControlledAgent ? controlledAgent.Index : -1,
                ControlledEntryId = controlledEntryId,
                SpawnStatus = spawnRuntimeState.Status,
                SpawnStatusSource = spawnRuntimeState.Source ?? string.Empty,
                SpawnStatusReason = spawnRuntimeState.Reason ?? string.Empty,
                LifecycleStatus = lifecycleRuntimeState.Status,
                LifecycleSource = lifecycleRuntimeState.Source ?? string.Empty,
                DeathCount = lifecycleRuntimeState.DeathCount,
                SessionStage = ResolveStage(currentPhase, effectiveSide, hasActiveControlledAgent, selectionState, selectionRequest, spawnRequest, spawnRuntimeState, lifecycleRuntimeState)
            };

            return true;
        }

        public static void LogTransition(CoopBattlePeerSessionSnapshot snapshot, string source)
        {
            if (snapshot == null || snapshot.PeerIndex < 0)
                return;

            string transitionKey =
                snapshot.SessionStage + "|" +
                snapshot.RequestedSide + "|" +
                snapshot.AssignedSide + "|" +
                snapshot.RuntimeSide + "|" +
                snapshot.EffectiveSide + "|" +
                (snapshot.ExplicitEntryId ?? string.Empty) + "|" +
                (snapshot.SelectionRequestEntryId ?? string.Empty) + "|" +
                (snapshot.SpawnRequestEntryId ?? string.Empty) + "|" +
                (snapshot.ControlledEntryId ?? string.Empty) + "|" +
                snapshot.HasActiveControlledAgent + "|" +
                snapshot.SpawnStatus + "|" +
                snapshot.LifecycleStatus;
            if (_lastLoggedTransitionKeyByPeer.TryGetValue(snapshot.PeerIndex, out string previousKey) &&
                string.Equals(previousKey, transitionKey, StringComparison.Ordinal))
            {
                return;
            }

            _lastLoggedTransitionKeyByPeer[snapshot.PeerIndex] = transitionKey;
            ModLogger.Info(
                "CoopBattlePeerSessionState: transition. " +
                "Peer=" + (snapshot.PeerName ?? snapshot.PeerIndex.ToString()) +
                " Stage=" + snapshot.SessionStage +
                " RequestedSide=" + snapshot.RequestedSide +
                " AssignedSide=" + snapshot.AssignedSide +
                " RuntimeSide=" + snapshot.RuntimeSide +
                " EffectiveSide=" + snapshot.EffectiveSide +
                " EffectiveEntryId=" + (snapshot.EffectiveEntryId ?? "null") +
                " ExplicitEntryId=" + (snapshot.ExplicitEntryId ?? "null") +
                " SelectionRequestEntryId=" + (snapshot.SelectionRequestEntryId ?? "null") +
                " SpawnRequestEntryId=" + (snapshot.SpawnRequestEntryId ?? "null") +
                " ControlledEntryId=" + (snapshot.ControlledEntryId ?? "null") +
                " HasAgent=" + snapshot.HasActiveControlledAgent +
                " SpawnStatus=" + snapshot.SpawnStatus +
                " LifecycleStatus=" + snapshot.LifecycleStatus +
                " Source=" + (source ?? "unknown"));
        }

        public static bool TryMigratePeerIndex(int previousPeerIndex, int currentPeerIndex, string source)
        {
            if (previousPeerIndex < 0 || currentPeerIndex < 0 || previousPeerIndex == currentPeerIndex)
                return false;

            if (!_lastLoggedTransitionKeyByPeer.TryGetValue(previousPeerIndex, out string previousTransitionKey))
                return false;

            _lastLoggedTransitionKeyByPeer.Remove(previousPeerIndex);
            _lastLoggedTransitionKeyByPeer[currentPeerIndex] = previousTransitionKey;
            return true;
        }

        public static CoopBattlePeerLifecycleStatus ResolvePassiveLifecycleStatus(
            CoopBattlePeerSessionSnapshot snapshot,
            BattleSideEnum authoritativeSide,
            bool canRespawn,
            bool preferSpawnQueued,
            bool preserveDeadAwaitingRespawn)
        {
            if (snapshot == null)
                return CoopBattlePeerLifecycleStatus.NoPeer;

            if (snapshot.HasActiveControlledAgent)
                return CoopBattlePeerLifecycleStatus.Alive;

            if (authoritativeSide == BattleSideEnum.None)
                return CoopBattlePeerLifecycleStatus.NoSide;

            if (preferSpawnQueued || snapshot.HasPendingSpawnRequest || snapshot.HasQueuedSpawnRuntime)
                return CoopBattlePeerLifecycleStatus.SpawnQueued;

            if (!snapshot.HasEffectiveSelection && !snapshot.HasSelectionRequest && !snapshot.HasExplicitSelection)
                return CoopBattlePeerLifecycleStatus.AwaitingSelection;

            if (preserveDeadAwaitingRespawn &&
                snapshot.LifecycleStatus == CoopBattlePeerLifecycleStatus.DeadAwaitingRespawn &&
                !canRespawn)
            {
                return CoopBattlePeerLifecycleStatus.DeadAwaitingRespawn;
            }

            return canRespawn
                ? CoopBattlePeerLifecycleStatus.Respawnable
                : CoopBattlePeerLifecycleStatus.Waiting;
        }

        private static CoopBattlePeerSessionStage ResolveStage(
            CoopBattlePhase currentPhase,
            BattleSideEnum effectiveSide,
            bool hasActiveControlledAgent,
            CoopBattleAuthorityState.PeerSelectionState selectionState,
            CoopBattleSelectionRequestState.PeerSelectionRequestState selectionRequest,
            CoopBattleSpawnRequestState.PeerSpawnRequestState spawnRequest,
            PeerSpawnRuntimeState spawnRuntimeState,
            PeerLifecycleRuntimeState lifecycleRuntimeState)
        {
            if (currentPhase >= CoopBattlePhase.BattleEnded)
                return CoopBattlePeerSessionStage.BattleEnded;

            if (hasActiveControlledAgent ||
                lifecycleRuntimeState.Status == CoopBattlePeerLifecycleStatus.Alive)
            {
                return CoopBattlePeerSessionStage.Alive;
            }

            if (effectiveSide == BattleSideEnum.None ||
                lifecycleRuntimeState.Status == CoopBattlePeerLifecycleStatus.NoSide)
            {
                return CoopBattlePeerSessionStage.NoSide;
            }

            if (spawnRuntimeState.Status == CoopBattleSpawnStatus.Spawned ||
                lifecycleRuntimeState.Status == CoopBattlePeerLifecycleStatus.DeadAwaitingRespawn)
            {
                return CoopBattlePeerSessionStage.DeadAwaitingRespawn;
            }

            if (!string.IsNullOrWhiteSpace(spawnRequest.TroopId) ||
                !string.IsNullOrWhiteSpace(spawnRequest.EntryId) ||
                spawnRuntimeState.Status == CoopBattleSpawnStatus.Pending ||
                spawnRuntimeState.Status == CoopBattleSpawnStatus.Validating ||
                spawnRuntimeState.Status == CoopBattleSpawnStatus.Validated ||
                lifecycleRuntimeState.Status == CoopBattlePeerLifecycleStatus.SpawnQueued)
            {
                return CoopBattlePeerSessionStage.SpawnQueued;
            }

            if (!string.IsNullOrWhiteSpace(selectionState.TroopId) ||
                !string.IsNullOrWhiteSpace(selectionState.EntryId) ||
                !string.IsNullOrWhiteSpace(selectionRequest.TroopId) ||
                !string.IsNullOrWhiteSpace(selectionRequest.EntryId))
            {
                return CoopBattlePeerSessionStage.EntrySelected;
            }

            if (selectionState.Side != BattleSideEnum.None ||
                selectionState.RequestedSide != BattleSideEnum.None)
            {
                return CoopBattlePeerSessionStage.SideAssigned;
            }

            return CoopBattlePeerSessionStage.Waiting;
        }
    }
}
