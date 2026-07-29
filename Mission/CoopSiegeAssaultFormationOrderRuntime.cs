using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Infrastructure;
using CoopSpectator.Network.Messages;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Objects.Siege;

namespace CoopSpectator.MissionBehaviors
{
    internal static class CoopSiegeAssaultFormationOrderRuntime
    {
        private static readonly Dictionary<Formation, CoopSiegeAssaultFormationOrderKind>
            ActiveOrdersByFormation =
                new Dictionary<Formation, CoopSiegeAssaultFormationOrderKind>();

        private static Mission _trackedMission;

        internal static bool TryApply(
            Mission mission,
            Team team,
            IEnumerable<Formation> formations,
            CoopSiegeAssaultFormationOrderKind orderKind,
            out string diagnostics)
        {
            diagnostics = "invalid-context";
            if (!GameNetwork.IsServer ||
                mission == null ||
                team == null ||
                formations == null ||
                !ReferenceEquals(team.Mission, mission) ||
                !(team.TeamAI is TeamAISiegeComponent))
            {
                return false;
            }

            EnsureMission(mission);
            List<Formation> requestedFormations = formations
                .Where(formation =>
                    formation != null &&
                    ReferenceEquals(formation.Team, team) &&
                    formation.CountOfUnits > 0 &&
                    formation.Index >= 0 &&
                    formation.Index < (int)FormationClass.NumberOfRegularFormations)
                .Distinct()
                .ToList();
            if (requestedFormations.Count <= 0)
            {
                diagnostics = "no-active-formations";
                return false;
            }

            switch (orderKind)
            {
                case CoopSiegeAssaultFormationOrderKind.AttackGate:
                    return TryAttackGate(
                        mission,
                        team,
                        requestedFormations,
                        out diagnostics);

                case CoopSiegeAssaultFormationOrderKind.AssaultWalls:
                    return TryApplyAttackerLaneBehavior(
                        mission,
                        team,
                        requestedFormations,
                        orderKind,
                        requireNonGateLane: true,
                        out diagnostics);

                case CoopSiegeAssaultFormationOrderKind.UseSiegeMachines:
                    return TryApplyAttackerLaneBehavior(
                        mission,
                        team,
                        requestedFormations,
                        orderKind,
                        requireNonGateLane: false,
                        out diagnostics);

                case CoopSiegeAssaultFormationOrderKind.OccupyArcherPositions:
                    return TryOccupyArcherPositions(
                        mission,
                        team,
                        requestedFormations,
                        out diagnostics);

                default:
                    diagnostics = "unsupported-order-kind";
                    return false;
            }
        }

        internal static void CancelPlayerDirectedOrders(
            Mission mission,
            Team team,
            IEnumerable<Formation> formations,
            string source)
        {
            if (!GameNetwork.IsServer ||
                mission == null ||
                team == null ||
                formations == null)
            {
                return;
            }

            EnsureMission(mission);
            var canceledFormations = new List<Formation>();
            foreach (Formation formation in formations.Distinct())
            {
                if (formation == null ||
                    !ReferenceEquals(formation.Team, team) ||
                    !ActiveOrdersByFormation.TryGetValue(
                        formation,
                        out CoopSiegeAssaultFormationOrderKind activeOrder))
                {
                    continue;
                }

                ActiveOrdersByFormation.Remove(formation);
                ResetFormationBehavior(formation);
                if (activeOrder == CoopSiegeAssaultFormationOrderKind.AssaultWalls ||
                    activeOrder == CoopSiegeAssaultFormationOrderKind.UseSiegeMachines)
                {
                    StopUsingSiegeMachines(mission, formation);
                }

                canceledFormations.Add(formation);
            }

            if (canceledFormations.Count <= 0)
                return;

            CoopMissionNetworkBridge.UpdateVoluntaryFormationAiControl(
                mission,
                team,
                canceledFormations,
                isAiControlled: false,
                "cancel-player-directed-siege-assault-order:" +
                (source ?? "unknown"));

            if (CoopDebugConfig.OrderOfBattleDiagnostics)
            {
                ModLogger.Info(
                    "CoopSiegeAssaultFormationOrderRuntime: canceled player-directed orders. " +
                    "Side=" + team.Side +
                    " Formations=" + FormatFormationIndices(canceledFormations) +
                    " Source=" + (source ?? "unknown"));
            }
        }

        internal static void Reset(Mission mission, string source)
        {
            if (mission != null &&
                _trackedMission != null &&
                !ReferenceEquals(mission, _trackedMission))
            {
                return;
            }

            ActiveOrdersByFormation.Clear();
            _trackedMission = null;

            if (CoopDebugConfig.OrderOfBattleDiagnostics)
            {
                ModLogger.Info(
                    "CoopSiegeAssaultFormationOrderRuntime: reset. Source=" +
                    (source ?? "unknown"));
            }
        }

        private static bool TryAttackGate(
            Mission mission,
            Team team,
            List<Formation> formations,
            out string diagnostics)
        {
            diagnostics = "attacker-team-required";
            if (team.Side != BattleSideEnum.Attacker ||
                !(team.TeamAI is TeamAISiegeComponent siegeAi))
            {
                return false;
            }

            CastleGate gate = IsAttackableGate(siegeAi.OuterGate, team.Side)
                ? siegeAi.OuterGate
                : IsAttackableGate(siegeAi.InnerGate, team.Side)
                    ? siegeAi.InnerGate
                    : null;
            if (gate == null)
            {
                diagnostics = "no-closed-attackable-gate";
                return false;
            }

            CancelPlayerDirectedOrders(
                mission,
                team,
                formations,
                "replace-with-attack-gate");
            CoopMissionNetworkBridge.UpdateVoluntaryFormationAiControl(
                mission,
                team,
                formations,
                isAiControlled: false,
                "player-directed-attack-gate");

            GameEntity gateEntity = GameEntity.CreateFromWeakEntity(gate.GameEntity);
            foreach (Formation formation in formations)
            {
                ResetFormationBehavior(formation);
                formation.SetControlledByAI(false, false);
                formation.SetMovementOrder(
                    MovementOrder.MovementOrderAttackEntity(
                        gateEntity,
                        surroundEntity: false));
            }

            diagnostics =
                "applied Gate=" + gate.Id +
                " Formations=" + FormatFormationIndices(formations);
            return true;
        }

        private static bool TryApplyAttackerLaneBehavior(
            Mission mission,
            Team team,
            List<Formation> formations,
            CoopSiegeAssaultFormationOrderKind orderKind,
            bool requireNonGateLane,
            out string diagnostics)
        {
            diagnostics = "attacker-team-required";
            if (team.Side != BattleSideEnum.Attacker)
                return false;

            List<SiegeLane> candidateLanes = (TeamAISiegeComponent.SiegeLanes ?? new List<SiegeLane>())
                .Where(lane =>
                    lane != null &&
                    (!requireNonGateLane || !lane.HasGate) &&
                    (orderKind == CoopSiegeAssaultFormationOrderKind.AssaultWalls
                        ? IsUsableWallAssaultLane(lane)
                        : HasActivePrimarySiegeWeapon(lane)))
                .ToList();
            if (candidateLanes.Count <= 0)
            {
                diagnostics = requireNonGateLane
                    ? "no-usable-wall-assault-lane"
                    : "no-active-primary-siege-machine";
                return false;
            }

            var eligibleFormations = new List<Formation>();
            foreach (Formation formation in formations)
            {
                if (formation.AI == null)
                    continue;

                if (orderKind == CoopSiegeAssaultFormationOrderKind.AssaultWalls)
                {
                    if (formation.AI.GetBehavior<BehaviorAssaultWalls>() == null ||
                        formation.AI.GetBehavior<BehaviorUseSiegeMachines>() == null ||
                        formation.AI.GetBehavior<BehaviorWaitForLadders>() == null)
                    {
                        continue;
                    }
                }
                else if (formation.AI.GetBehavior<BehaviorUseSiegeMachines>() == null)
                {
                    continue;
                }

                eligibleFormations.Add(formation);
            }

            if (eligibleFormations.Count <= 0)
            {
                diagnostics = "no-formations-with-required-siege-behaviors";
                return false;
            }

            CancelPlayerDirectedOrders(
                mission,
                team,
                eligibleFormations,
                "replace-with-" + orderKind);

            foreach (Formation formation in eligibleFormations)
            {
                SiegeLane lane = candidateLanes
                    .OrderBy(candidate => GetFormationLaneDistanceSquared(formation, candidate))
                    .First();
                formation.AI.Side = lane.LaneSide;
                formation.AI.ResetBehaviorWeights();
                TacticComponent.SetDefaultBehaviorWeights(formation);
                formation.AI.SetBehaviorWeight<BehaviorCharge>(0.1f);

                if (orderKind == CoopSiegeAssaultFormationOrderKind.AssaultWalls)
                {
                    formation.AI.SetBehaviorWeight<BehaviorAssaultWalls>(1f);
                    formation.AI.SetBehaviorWeight<BehaviorUseSiegeMachines>(1f);
                    formation.AI.SetBehaviorWeight<BehaviorWaitForLadders>(1f);
                    lane.SetLastAssignedFormation(team.TeamIndex, formation);
                }
                else
                {
                    formation.AI.SetBehaviorWeight<BehaviorUseSiegeMachines>(1f);
                    if (formation.AI.GetBehavior<BehaviorWaitForLadders>() != null)
                    {
                        formation.AI.SetBehaviorWeight<BehaviorWaitForLadders>(0.25f);
                    }
                }

                ActiveOrdersByFormation[formation] = orderKind;
            }

            CoopMissionNetworkBridge.UpdateVoluntaryFormationAiControl(
                mission,
                team,
                eligibleFormations,
                isAiControlled: true,
                "player-directed-siege-assault-order:" + orderKind);

            diagnostics =
                "applied Order=" + orderKind +
                " Formations=" + FormatFormationIndices(eligibleFormations) +
                " Rejected=" + (formations.Count - eligibleFormations.Count);
            return true;
        }

        private static bool TryOccupyArcherPositions(
            Mission mission,
            Team team,
            List<Formation> formations,
            out string diagnostics)
        {
            diagnostics = "defender-team-required";
            if (team.Side != BattleSideEnum.Defender ||
                !(team.TeamAI is TeamAISiegeDefender defenderAi))
            {
                return false;
            }

            List<ArcherPosition> availablePositions = defenderAi.ArcherPositions
                .Where(position => position?.Entity != null)
                .ToList();
            if (availablePositions.Count <= 0)
            {
                diagnostics = "no-archer-positions";
                return false;
            }

            List<Formation> rangedFormations = formations
                .Where(formation =>
                    formation.AI?.GetBehavior<BehaviorShootFromCastleWalls>() != null &&
                    formation.QuerySystem != null &&
                    formation.QuerySystem.IsRangedFormation)
                .OrderByDescending(formation => formation.CountOfUnits)
                .ToList();
            if (rangedFormations.Count <= 0)
            {
                diagnostics = "no-ranged-formations";
                return false;
            }

            CancelPlayerDirectedOrders(
                mission,
                team,
                rangedFormations,
                "replace-with-occupy-archer-positions");

            var acceptedFormations = new List<Formation>();
            foreach (Formation formation in rangedFormations)
            {
                if (availablePositions.Count <= 0)
                    break;

                ArcherPosition archerPosition = availablePositions
                    .OrderBy(position =>
                        formation.CachedAveragePosition.DistanceSquared(
                            position.Entity.GlobalPosition.AsVec2))
                    .First();
                availablePositions.Remove(archerPosition);

                formation.AI.Side = archerPosition.GetArcherPositionClosestSide();
                formation.AI.ResetBehaviorWeights();
                TacticComponent.SetDefaultBehaviorWeights(formation);
                BehaviorShootFromCastleWalls behavior =
                    formation.AI.SetBehaviorWeight<BehaviorShootFromCastleWalls>(1f);
                behavior.ArcherPosition = archerPosition.Entity;
                archerPosition.SetLastAssignedFormation(team.TeamIndex, formation);
                ActiveOrdersByFormation[formation] =
                    CoopSiegeAssaultFormationOrderKind.OccupyArcherPositions;
                acceptedFormations.Add(formation);
            }

            if (acceptedFormations.Count <= 0)
            {
                diagnostics = "no-available-archer-position-assignments";
                return false;
            }

            CoopMissionNetworkBridge.UpdateVoluntaryFormationAiControl(
                mission,
                team,
                acceptedFormations,
                isAiControlled: true,
                "player-directed-occupy-archer-positions");

            diagnostics =
                "applied Formations=" + FormatFormationIndices(acceptedFormations) +
                " Rejected=" + (formations.Count - acceptedFormations.Count);
            return true;
        }

        private static void EnsureMission(Mission mission)
        {
            if (ReferenceEquals(_trackedMission, mission))
                return;

            ActiveOrdersByFormation.Clear();
            _trackedMission = mission;
        }

        private static void ResetFormationBehavior(Formation formation)
        {
            if (formation?.AI == null)
                return;

            formation.AI.ResetBehaviorWeights();
            TacticComponent.SetDefaultBehaviorWeights(formation);
        }

        private static void StopUsingSiegeMachines(Mission mission, Formation formation)
        {
            if (mission?.ActiveMissionObjects == null || formation == null)
                return;

            foreach (SiegeWeapon siegeWeapon in
                     mission.ActiveMissionObjects.FindAllWithType<SiegeWeapon>())
            {
                if (siegeWeapon != null && siegeWeapon.IsUsedByFormation(formation))
                    formation.StopUsingMachine(siegeWeapon);
            }
        }

        private static bool IsAttackableGate(CastleGate gate, BattleSideEnum side)
        {
            return gate != null &&
                   !gate.IsDestroyed &&
                   !gate.IsDeactivated &&
                   !gate.IsGateOpen &&
                   gate.GetOrder(side) == OrderType.AttackEntity;
        }

        private static bool IsUsableWallAssaultLane(SiegeLane lane)
        {
            if (lane == null || lane.HasGate)
                return false;

            try
            {
                if (lane.CalculateIsLaneUnusable())
                    return false;
            }
            catch
            {
                return false;
            }

            return lane.IsBreach || HasActivePrimarySiegeWeapon(lane);
        }

        private static bool HasActivePrimarySiegeWeapon(SiegeLane lane)
        {
            if (lane?.PrimarySiegeWeapons == null)
                return false;

            foreach (IPrimarySiegeWeapon primarySiegeWeapon in lane.PrimarySiegeWeapons)
            {
                if (primarySiegeWeapon is SiegeWeapon siegeWeapon &&
                    !siegeWeapon.IsDestroyed &&
                    !siegeWeapon.IsDeactivated &&
                    !siegeWeapon.IsDisabled &&
                    !primarySiegeWeapon.HasCompletedAction())
                {
                    return true;
                }
            }

            return false;
        }

        private static float GetFormationLaneDistanceSquared(
            Formation formation,
            SiegeLane lane)
        {
            try
            {
                return formation.CachedAveragePosition.DistanceSquared(
                    lane.GetCurrentAttackerPosition().GetNavMeshVec3().AsVec2);
            }
            catch
            {
                return float.MaxValue;
            }
        }

        private static string FormatFormationIndices(IEnumerable<Formation> formations)
        {
            return string.Join(
                ",",
                (formations ?? Array.Empty<Formation>())
                    .Where(formation => formation != null)
                    .Select(formation => formation.Index.ToString())
                    .ToArray());
        }
    }
}
