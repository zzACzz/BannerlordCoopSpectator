using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Network.Messages;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    internal enum CoopSiegeLadderMissionObjectTarget
    {
        AnyRegistered = 0,
        Ladder = 1,
        AttackerStandingPoint = 2
    }

    internal sealed class CoopSiegeLadderInteractionPointSnapshot
    {
        public int OnWallNavMeshId { get; set; }
        public MissionObjectId ServerLadderId { get; set; }
        public MissionObjectId ServerStandingPointId { get; set; }
        public CoopSiegeLadderInteractionPointRole Role { get; set; }
        public int LadderState { get; set; }
        public bool RootDisabled { get; set; }
        public bool RootDestroyed { get; set; }
        public bool RootDeactivated { get; set; }
        public bool RootVisible { get; set; }
        public bool PointDeactivated { get; set; }
        public bool PointDisabledForPlayers { get; set; }
        public bool PointHasUser { get; set; }
        public int PointUserAgentIndex { get; set; } = -1;

        public CoopSiegeLadderInteractionPointSnapshot Clone()
        {
            return (CoopSiegeLadderInteractionPointSnapshot)MemberwiseClone();
        }
    }

    internal static class CoopSiegeLadderInteractionRuntime
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<int, CoopSiegeLadderInteractionPointSnapshot> SnapshotsByServerPointId =
            new Dictionary<int, CoopSiegeLadderInteractionPointSnapshot>();
        private static readonly Dictionary<int, MissionObjectId> LocalIdByServerId =
            new Dictionary<int, MissionObjectId>();
        private static readonly Dictionary<int, MissionObjectId> ServerIdByLocalId =
            new Dictionary<int, MissionObjectId>();
        private static readonly Dictionary<int, CoopSiegeLadderInteractionObjectKind> ObjectKindByServerId =
            new Dictionary<int, CoopSiegeLadderInteractionObjectKind>();
        private static Mission _clientMission;

        public static void Reset(string source)
        {
            lock (Sync)
            {
                ResetLocked();
            }
        }

        public static bool IsExactSiegeAssaultContext(Mission mission)
        {
            if (mission == null ||
                !SceneRuntimeClassifier.IsExactSiegeAssaultWithDeploymentScene(
                    mission.SceneName ?? string.Empty))
            {
                return false;
            }

            BattleScenarioContextMessage scenarioContext = null;
            try
            {
                scenarioContext =
                    BattleSnapshotRuntimeState.GetScenarioContext() ??
                    BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                    BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            }
            catch
            {
            }

            return ExactCampaignSiegeAssaultWithDeploymentRuntime.IsSiegeAssaultScenario(scenarioContext);
        }

        public static List<CoopSiegeLadderInteractionPointSnapshot> BuildServerSnapshots(
            Mission mission,
            out string diagnostics)
        {
            diagnostics = "skip-context";
            var result = new List<CoopSiegeLadderInteractionPointSnapshot>();
            if (!GameNetwork.IsServer || mission == null || !IsExactSiegeAssaultContext(mission))
                return result;

            List<SiegeLadder> ladders = EnumerateLadders(mission);
            Dictionary<int, int> ladderCountByNavMeshId = ladders
                .GroupBy(ladder => ladder.OnWallNavMeshId)
                .ToDictionary(group => group.Key, group => group.Count());
            int skippedAmbiguousLadderCount = 0;
            int skippedAmbiguousPointCount = 0;
            foreach (SiegeLadder ladder in ladders)
            {
                if (ladder == null ||
                    ladder.Id.Id < 0 ||
                    !ladderCountByNavMeshId.TryGetValue(ladder.OnWallNavMeshId, out int ladderCount) ||
                    ladderCount != 1)
                {
                    skippedAmbiguousLadderCount++;
                    continue;
                }

                List<KeyValuePair<StandingPoint, CoopSiegeLadderInteractionPointRole>> points =
                    EnumerateAttackerStandingPoints(ladder);
                Dictionary<CoopSiegeLadderInteractionPointRole, int> pointCountByRole = points
                    .GroupBy(pair => pair.Value)
                    .ToDictionary(group => group.Key, group => group.Count());
                foreach (KeyValuePair<StandingPoint, CoopSiegeLadderInteractionPointRole> pair in points)
                {
                    StandingPoint point = pair.Key;
                    if (point == null ||
                        point.Id.Id < 0 ||
                        !pointCountByRole.TryGetValue(pair.Value, out int pointCount) ||
                        pointCount != 1)
                    {
                        skippedAmbiguousPointCount++;
                        continue;
                    }

                    result.Add(new CoopSiegeLadderInteractionPointSnapshot
                    {
                        OnWallNavMeshId = ladder.OnWallNavMeshId,
                        ServerLadderId = ladder.Id,
                        ServerStandingPointId = point.Id,
                        Role = pair.Value,
                        LadderState = (int)ladder.State,
                        RootDisabled = ladder.IsDisabled,
                        RootDestroyed = ladder.IsDestroyed,
                        RootDeactivated = ladder.IsDeactivated,
                        RootVisible = SafeIsVisible(ladder),
                        PointDeactivated = point.IsDeactivated,
                        PointDisabledForPlayers = point.IsDisabledForPlayers,
                        PointHasUser = point.HasUser,
                        PointUserAgentIndex = point.UserAgent?.Index ?? -1
                    });
                }
            }

            diagnostics =
                "Ladders=" + ladders.Count +
                " Snapshots=" + result.Count +
                " SkippedAmbiguousLadders=" + skippedAmbiguousLadderCount +
                " SkippedAmbiguousPoints=" + skippedAmbiguousPointCount;
            return result;
        }

        public static bool ObserveAuthoritativePointState(
            Mission mission,
            int onWallNavMeshId,
            MissionObjectId serverLadderId,
            MissionObjectId serverStandingPointId,
            CoopSiegeLadderInteractionPointRole role,
            int ladderState,
            bool rootDisabled,
            bool rootDestroyed,
            bool rootDeactivated,
            bool rootVisible,
            bool pointDeactivated,
            bool pointDisabledForPlayers,
            bool pointHasUser,
            int pointUserAgentIndex,
            out string diagnostics)
        {
            diagnostics = "skip-context";
            if (!IsRemoteClientContext(mission) ||
                serverLadderId.Id < 0 ||
                serverStandingPointId.Id < 0 ||
                role == CoopSiegeLadderInteractionPointRole.Invalid)
            {
                return false;
            }

            lock (Sync)
            {
                EnsureMissionLocked(mission);
                SnapshotsByServerPointId[serverStandingPointId.Id] =
                    new CoopSiegeLadderInteractionPointSnapshot
                    {
                        OnWallNavMeshId = onWallNavMeshId,
                        ServerLadderId = serverLadderId,
                        ServerStandingPointId = serverStandingPointId,
                        Role = role,
                        LadderState = ladderState,
                        RootDisabled = rootDisabled,
                        RootDestroyed = rootDestroyed,
                        RootDeactivated = rootDeactivated,
                        RootVisible = rootVisible,
                        PointDeactivated = pointDeactivated,
                        PointDisabledForPlayers = pointDisabledForPlayers,
                        PointHasUser = pointHasUser,
                        PointUserAgentIndex = pointHasUser ? pointUserAgentIndex : -1
                    };
            }

            bool applied = TryApplyCachedAuthoritativeState(mission, out string applyDiagnostics);
            diagnostics = "cached Apply={" + applyDiagnostics + "}";
            return applied;
        }

        public static void ObserveServerLadderState(
            Mission mission,
            MissionObjectId serverLadderId,
            int ladderState)
        {
            UpdateSnapshotsForRoot(
                mission,
                serverLadderId,
                snapshot => snapshot.LadderState = ladderState);
        }

        public static void ObserveServerRootDisabled(
            Mission mission,
            MissionObjectId serverLadderId,
            bool rootDisabled)
        {
            UpdateSnapshotsForRoot(
                mission,
                serverLadderId,
                snapshot => snapshot.RootDisabled = rootDisabled);
        }

        public static void ObserveServerRootVisibility(
            Mission mission,
            MissionObjectId serverLadderId,
            bool rootVisible)
        {
            UpdateSnapshotsForRoot(
                mission,
                serverLadderId,
                snapshot => snapshot.RootVisible = rootVisible);
        }

        public static void ObserveServerPointDeactivated(
            Mission mission,
            MissionObjectId serverPointId,
            bool isDeactivated)
        {
            UpdateSnapshotForPoint(
                mission,
                serverPointId,
                snapshot => snapshot.PointDeactivated = isDeactivated);
        }

        public static void ObserveServerPointDisabledForPlayers(
            Mission mission,
            MissionObjectId serverPointId,
            bool isDisabledForPlayers)
        {
            UpdateSnapshotForPoint(
                mission,
                serverPointId,
                snapshot => snapshot.PointDisabledForPlayers = isDisabledForPlayers);
        }

        public static void ObserveServerPointUser(
            Mission mission,
            MissionObjectId serverPointId,
            int agentIndex)
        {
            UpdateSnapshotForPoint(
                mission,
                serverPointId,
                snapshot =>
                {
                    snapshot.PointHasUser = agentIndex >= 0;
                    snapshot.PointUserAgentIndex = agentIndex;
                });
        }

        public static void ObserveServerUserStopped(
            Mission mission,
            int agentIndex)
        {
            if (!IsRemoteClientContext(mission) || agentIndex < 0)
                return;

            lock (Sync)
            {
                EnsureMissionLocked(mission);
                foreach (CoopSiegeLadderInteractionPointSnapshot snapshot in
                         SnapshotsByServerPointId.Values)
                {
                    if (snapshot.PointHasUser &&
                        snapshot.PointUserAgentIndex == agentIndex)
                    {
                        snapshot.PointHasUser = false;
                        snapshot.PointUserAgentIndex = -1;
                    }
                }
            }
        }

        public static bool TryApplyCachedAuthoritativeState(
            Mission mission,
            out string diagnostics)
        {
            ResolveLocalControl(out BattleSideEnum localSide, out bool isPlayerControlled);
            return TryApplyCachedAuthoritativeState(
                mission,
                localSide,
                isPlayerControlled,
                out diagnostics);
        }

        public static bool TryApplyCachedAuthoritativeState(
            Mission mission,
            BattleSideEnum localSide,
            bool isPlayerControlled,
            out string diagnostics)
        {
            diagnostics = "skip-context";
            if (!IsRemoteClientContext(mission))
                return false;

            List<CoopSiegeLadderInteractionPointSnapshot> snapshots;
            lock (Sync)
            {
                EnsureMissionLocked(mission);
                snapshots = SnapshotsByServerPointId.Values
                    .Select(snapshot => snapshot.Clone())
                    .ToList();
            }

            if (snapshots.Count == 0)
            {
                diagnostics = "no-authoritative-snapshots";
                return false;
            }

            List<SiegeLadder> allLadders = EnumerateLadders(mission);
            int mappedCount = 0;
            int mutatedCount = 0;
            int ambiguousCount = 0;
            foreach (CoopSiegeLadderInteractionPointSnapshot snapshot in snapshots)
            {
                int authoritativeIdentityCount = snapshots.Count(candidate =>
                    candidate.OnWallNavMeshId == snapshot.OnWallNavMeshId &&
                    candidate.Role == snapshot.Role);
                List<SiegeLadder> localLadders = allLadders
                    .Where(ladder => ladder.OnWallNavMeshId == snapshot.OnWallNavMeshId)
                    .ToList();
                List<KeyValuePair<StandingPoint, CoopSiegeLadderInteractionPointRole>> localPoints =
                    localLadders.Count == 1
                        ? EnumerateAttackerStandingPoints(localLadders[0])
                            .Where(pair => pair.Value == snapshot.Role)
                            .ToList()
                        : new List<KeyValuePair<StandingPoint, CoopSiegeLadderInteractionPointRole>>();
                if (authoritativeIdentityCount != 1 ||
                    localLadders.Count != 1 ||
                    localPoints.Count != 1)
                {
                    ambiguousCount++;
                    continue;
                }

                SiegeLadder localLadder = localLadders[0];
                StandingPoint localPoint = localPoints[0].Key;
                bool bijectiveMapping = TryRegisterMappingPair(
                    snapshot,
                    localLadder,
                    localPoint);
                if (bijectiveMapping)
                    mappedCount++;

                bool localRootVisible = SafeIsVisible(localLadder);
                bool authoritativeStateAllowsUse =
                    CoopSiegeLadderInteractionContract.IsAttackerLiftStatePotentiallyUsable(
                        snapshot.LadderState);
                bool localStateAllowsUse =
                    CoopSiegeLadderInteractionContract.IsAttackerLiftStatePotentiallyUsable(
                        (int)localLadder.State);
                int effectiveLadderState =
                    authoritativeStateAllowsUse && localStateAllowsUse
                        ? snapshot.LadderState
                        : (int)SiegeLadder.LadderState.OnWall;
                var input = new CoopSiegeLadderInteractionDecisionInput(
                    isExactCampaignSiegeAssault: true,
                    isRemoteClient: true,
                    isPlayerControlled: isPlayerControlled,
                    isLocalAttacker: localSide == BattleSideEnum.Attacker,
                    objectKind: CoopSiegeLadderInteractionObjectKind.AttackerStandingPoint,
                    authoritativeIdentityCount: authoritativeIdentityCount,
                    localLadderCount: localLadders.Count,
                    localPointCount: localPoints.Count,
                    isBijectiveMapping: bijectiveMapping,
                    ladderState: effectiveLadderState,
                    rootDisabled: snapshot.RootDisabled || localLadder.IsDisabled,
                    rootDestroyed: snapshot.RootDestroyed || localLadder.IsDestroyed,
                    rootDeactivated: snapshot.RootDeactivated || localLadder.IsDeactivated,
                    rootVisible: snapshot.RootVisible && localRootVisible,
                    authoritativePointDeactivated: snapshot.PointDeactivated,
                    authoritativePointDisabledForPlayers: snapshot.PointDisabledForPlayers,
                    authoritativePointHasUser: snapshot.PointHasUser,
                    localPointDeactivated: localPoint.IsDeactivated,
                    localPointDisabledForPlayers: localPoint.IsDisabledForPlayers);
                CoopSiegeLadderInteractionDecision decision =
                    CoopSiegeLadderInteractionContract.Decide(input);
                if (!decision.ShouldMutate)
                    continue;

                localPoint.IsDeactivated = decision.DesiredDeactivated;
                localPoint.IsDisabledForPlayers = decision.DesiredDisabledForPlayers;
                mutatedCount++;
            }

            diagnostics =
                "Snapshots=" + snapshots.Count +
                " Mapped=" + mappedCount +
                " Mutated=" + mutatedCount +
                " Ambiguous=" + ambiguousCount +
                " LocalSide=" + localSide +
                " PlayerControlled=" + isPlayerControlled;
            return mutatedCount > 0;
        }

        public static bool TryTranslateServerMissionObjectId(
            Mission mission,
            MissionObjectId serverMissionObjectId,
            CoopSiegeLadderMissionObjectTarget target,
            out MissionObjectId localMissionObjectId)
        {
            localMissionObjectId = serverMissionObjectId;
            if (!IsRemoteClientContext(mission) || serverMissionObjectId.Id < 0)
                return false;

            lock (Sync)
            {
                EnsureMissionLocked(mission);
                if (!LocalIdByServerId.TryGetValue(serverMissionObjectId.Id, out MissionObjectId mappedId) ||
                    !ObjectKindByServerId.TryGetValue(
                        serverMissionObjectId.Id,
                        out CoopSiegeLadderInteractionObjectKind objectKind) ||
                    !TargetAccepts(target, objectKind))
                {
                    return false;
                }

                localMissionObjectId = mappedId;
                return true;
            }
        }

        public static bool TryTranslateLocalAttackerPointId(
            Mission mission,
            MissionObjectId localMissionObjectId,
            out MissionObjectId serverMissionObjectId)
        {
            serverMissionObjectId = localMissionObjectId;
            if (!IsRemoteClientContext(mission) || localMissionObjectId.Id < 0)
                return false;

            lock (Sync)
            {
                EnsureMissionLocked(mission);
                if (!ServerIdByLocalId.TryGetValue(localMissionObjectId.Id, out MissionObjectId mappedId) ||
                    !ObjectKindByServerId.TryGetValue(
                        mappedId.Id,
                        out CoopSiegeLadderInteractionObjectKind objectKind) ||
                    objectKind != CoopSiegeLadderInteractionObjectKind.AttackerStandingPoint)
                {
                    return false;
                }

                serverMissionObjectId = mappedId;
                return true;
            }
        }

        private static bool IsRemoteClientContext(Mission mission)
        {
            return mission != null &&
                   GameNetwork.IsClient &&
                   !GameNetwork.IsServer &&
                   IsExactSiegeAssaultContext(mission);
        }

        private static void ResolveLocalControl(
            out BattleSideEnum localSide,
            out bool isPlayerControlled)
        {
            localSide = BattleSideEnum.None;
            isPlayerControlled = false;
            if (CoopBattleAgentControlRuntimeState.TryGetClientState(
                    out CoopBattleAgentControlState state))
            {
                localSide = state.Side;
                isPlayerControlled = state.Mode == CoopBattleAgentControlMode.PlayerControlled;
                return;
            }

            try
            {
                Agent mainAgent = Mission.Current?.MainAgent;
                localSide = mainAgent?.Team?.Side ?? BattleSideEnum.None;
                isPlayerControlled = mainAgent?.IsPlayerControlled == true;
            }
            catch
            {
            }
        }

        private static void UpdateSnapshotsForRoot(
            Mission mission,
            MissionObjectId serverLadderId,
            Action<CoopSiegeLadderInteractionPointSnapshot> update)
        {
            if (!IsRemoteClientContext(mission) || serverLadderId.Id < 0 || update == null)
                return;

            lock (Sync)
            {
                EnsureMissionLocked(mission);
                foreach (CoopSiegeLadderInteractionPointSnapshot snapshot in
                         SnapshotsByServerPointId.Values)
                {
                    if (snapshot.ServerLadderId.Id == serverLadderId.Id)
                        update(snapshot);
                }
            }
        }

        private static void UpdateSnapshotForPoint(
            Mission mission,
            MissionObjectId serverPointId,
            Action<CoopSiegeLadderInteractionPointSnapshot> update)
        {
            if (!IsRemoteClientContext(mission) || serverPointId.Id < 0 || update == null)
                return;

            lock (Sync)
            {
                EnsureMissionLocked(mission);
                if (SnapshotsByServerPointId.TryGetValue(
                        serverPointId.Id,
                        out CoopSiegeLadderInteractionPointSnapshot snapshot))
                {
                    update(snapshot);
                }
            }
        }

        private static bool TryRegisterMappingPair(
            CoopSiegeLadderInteractionPointSnapshot snapshot,
            SiegeLadder localLadder,
            StandingPoint localPoint)
        {
            if (snapshot == null || localLadder == null || localPoint == null)
                return false;

            lock (Sync)
            {
                if (!CanRegisterMappingLocked(snapshot.ServerLadderId, localLadder.Id) ||
                    !CanRegisterMappingLocked(snapshot.ServerStandingPointId, localPoint.Id))
                {
                    return false;
                }

                RegisterMappingLocked(
                    snapshot.ServerLadderId,
                    localLadder.Id,
                    CoopSiegeLadderInteractionObjectKind.SiegeLadder);
                RegisterMappingLocked(
                    snapshot.ServerStandingPointId,
                    localPoint.Id,
                    CoopSiegeLadderInteractionObjectKind.AttackerStandingPoint);
                return true;
            }
        }

        private static bool CanRegisterMappingLocked(
            MissionObjectId serverId,
            MissionObjectId localId)
        {
            if (serverId.Id < 0 || localId.Id < 0)
                return false;

            if (LocalIdByServerId.TryGetValue(serverId.Id, out MissionObjectId existingLocalId) &&
                existingLocalId.Id != localId.Id)
            {
                return false;
            }

            if (ServerIdByLocalId.TryGetValue(localId.Id, out MissionObjectId existingServerId) &&
                existingServerId.Id != serverId.Id)
            {
                return false;
            }

            return true;
        }

        private static void RegisterMappingLocked(
            MissionObjectId serverId,
            MissionObjectId localId,
            CoopSiegeLadderInteractionObjectKind objectKind)
        {
            LocalIdByServerId[serverId.Id] = localId;
            ServerIdByLocalId[localId.Id] = serverId;
            ObjectKindByServerId[serverId.Id] = objectKind;
        }

        private static bool TargetAccepts(
            CoopSiegeLadderMissionObjectTarget target,
            CoopSiegeLadderInteractionObjectKind objectKind)
        {
            switch (target)
            {
                case CoopSiegeLadderMissionObjectTarget.AnyRegistered:
                    return CoopSiegeLadderInteractionContract.IsSupportedObjectKind(objectKind);
                case CoopSiegeLadderMissionObjectTarget.Ladder:
                    return objectKind == CoopSiegeLadderInteractionObjectKind.SiegeLadder;
                case CoopSiegeLadderMissionObjectTarget.AttackerStandingPoint:
                    return objectKind == CoopSiegeLadderInteractionObjectKind.AttackerStandingPoint;
                default:
                    return false;
            }
        }

        private static List<SiegeLadder> EnumerateLadders(Mission mission)
        {
            var result = new List<SiegeLadder>();
            if (mission == null)
                return result;

            try
            {
                if (mission.ActiveMissionObjects != null)
                {
                    foreach (SiegeLadder ladder in
                             mission.ActiveMissionObjects.FindAllWithType<SiegeLadder>())
                    {
                        if (ladder != null && !result.Contains(ladder))
                            result.Add(ladder);
                    }
                }
            }
            catch
            {
            }

            try
            {
                if (mission.MissionObjects != null)
                {
                    foreach (SiegeLadder ladder in
                             mission.MissionObjects.FindAllWithType<SiegeLadder>())
                    {
                        if (ladder != null && !result.Contains(ladder))
                            result.Add(ladder);
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        private static List<KeyValuePair<StandingPoint, CoopSiegeLadderInteractionPointRole>>
            EnumerateAttackerStandingPoints(SiegeLadder ladder)
        {
            var result =
                new List<KeyValuePair<StandingPoint, CoopSiegeLadderInteractionPointRole>>();
            if (ladder == null)
                return result;

            IEnumerable<StandingPoint> rootedPoints;
            try
            {
                rootedPoints = ladder.GameEntity
                    .CollectScriptComponentsWithTagIncludingChildrenRecursive<StandingPoint>(
                        ladder.AttackerTag)
                    .ToList();
            }
            catch
            {
                return result;
            }

            foreach (StandingPoint point in rootedPoints)
            {
                if (point == null ||
                    !TryResolveRole(
                        ladder,
                        point,
                        out CoopSiegeLadderInteractionPointRole role))
                {
                    continue;
                }

                result.Add(
                    new KeyValuePair<StandingPoint, CoopSiegeLadderInteractionPointRole>(
                        point,
                        role));
            }

            return result;
        }

        private static bool TryResolveRole(
            SiegeLadder ladder,
            StandingPoint point,
            out CoopSiegeLadderInteractionPointRole role)
        {
            role = CoopSiegeLadderInteractionPointRole.Invalid;
            try
            {
                return ladder != null &&
                       point != null &&
                       CoopSiegeLadderInteractionContract.TryResolveAttackerPointRole(
                           point.GameEntity.HasTag(ladder.AttackerTag),
                           point.GameEntity.HasTag(ladder.DefenderTag),
                           point is StandingPointWithWeaponRequirement,
                           point.GameEntity.HasTag(ladder.AmmoPickUpTag),
                           point.GameEntity.HasTag(ladder.RightStandingPointTag),
                           point.GameEntity.HasTag(ladder.FrontStandingPointTag),
                           out role);
            }
            catch
            {
                return false;
            }
        }

        private static bool SafeIsVisible(SiegeLadder ladder)
        {
            try
            {
                return ladder?.GameEntity.IsVisibleIncludeParents() == true;
            }
            catch
            {
                return false;
            }
        }

        private static void EnsureMissionLocked(Mission mission)
        {
            if (ReferenceEquals(_clientMission, mission))
                return;

            ResetLocked();
            _clientMission = mission;
        }

        private static void ResetLocked()
        {
            SnapshotsByServerPointId.Clear();
            LocalIdByServerId.Clear();
            ServerIdByLocalId.Clear();
            ObjectKindByServerId.Clear();
            _clientMission = null;
        }
    }
}
