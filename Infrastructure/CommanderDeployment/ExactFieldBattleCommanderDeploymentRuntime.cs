using System;
using System.Collections.Generic;
using CoopSpectator.Network.Messages;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    internal static class ExactFieldBattleCommanderDeploymentRuntime
    {
        private static readonly object Sync = new object();

        private static Mission _activeMission;
        private static bool _deploymentLifecycleFinished;
        private static Mission _manualPlacementMission;
        private static bool _manualPlacementActive;
        private static bool _manualPlacementPreviousTeleportingAgents;
        private static readonly HashSet<BattleSideEnum> ActiveManualPlacementSides =
            new HashSet<BattleSideEnum>();
        private static readonly HashSet<BattleSideEnum> CompletedDeploymentSides =
            new HashSet<BattleSideEnum>();

        public static bool IsDeploymentRuntimeActive(Mission mission)
        {
            if (!TryValidateRuntime(mission, out _))
                return false;

            lock (Sync)
            {
                if (!ReferenceEquals(_activeMission, mission))
                {
                    RestoreManualPlacementStateLocked();
                    _activeMission = mission;
                    _deploymentLifecycleFinished = false;
                    CompletedDeploymentSides.Clear();
                }

                return true;
            }
        }

        public static bool TryBeginManualPlacement(
            Mission mission,
            BattleSideEnum side,
            out string diagnostics)
        {
            diagnostics = "deployment-runtime-inactive";
            if (!IsDeploymentRuntimeActive(mission))
                return false;

            if (side == BattleSideEnum.None)
            {
                diagnostics = "manual-placement-side-none";
                return false;
            }

            return TryBeginValidatedManualPlacement(
                mission,
                side,
                enforceSideLifecycle: true,
                out diagnostics);
        }

        public static bool TryBeginClientManualPlacement(Mission mission, out string diagnostics)
        {
            if (!TryValidateClientManualPlacement(mission, out diagnostics))
                return false;

            Mission.MissionTeamAITypeEnum previousMissionTeamAiType = mission.MissionTeamAIType;
            mission.MissionTeamAIType = Mission.MissionTeamAITypeEnum.FieldBattle;

            bool placementStarted = TryBeginValidatedManualPlacement(
                mission,
                BattleSideEnum.None,
                enforceSideLifecycle: false,
                out string placementDiagnostics);
            diagnostics =
                placementDiagnostics +
                " PreviousMissionTeamAIType=" + previousMissionTeamAiType +
                " CurrentMissionTeamAIType=" + mission.MissionTeamAIType;
            return placementStarted;
        }

        public static bool IsClientManualPlacementActive(Mission mission)
        {
            if (mission == null || !GameNetwork.IsClient)
                return false;

            lock (Sync)
            {
                return _manualPlacementActive &&
                       ReferenceEquals(_manualPlacementMission, mission);
            }
        }

        public static bool ShouldAcceptClientFormationLayoutState(
            Mission mission,
            BattleSideEnum side,
            out string diagnostics)
        {
            diagnostics = "client-manual-placement-inactive";
            if (side == BattleSideEnum.None ||
                !TryValidateClientManualPlacement(mission, out diagnostics))
            {
                return false;
            }

            lock (Sync)
            {
                if (!ReferenceEquals(_activeMission, mission) ||
                    !ReferenceEquals(_manualPlacementMission, mission) ||
                    !_manualPlacementActive ||
                    _deploymentLifecycleFinished)
                {
                    diagnostics =
                        "client-manual-placement-inactive" +
                        " ActiveMission=" + ReferenceEquals(_activeMission, mission) +
                        " PlacementMission=" + ReferenceEquals(_manualPlacementMission, mission) +
                        " PlacementActive=" + _manualPlacementActive +
                        " LifecycleFinished=" + _deploymentLifecycleFinished;
                    return false;
                }

                diagnostics =
                    "client-manual-placement-active" +
                    " Side=" + side +
                    " CompletedSides=[" + string.Join(",", CompletedDeploymentSides) + "]";
                return true;
            }
        }

        private static bool TryBeginValidatedManualPlacement(
            Mission mission,
            BattleSideEnum side,
            bool enforceSideLifecycle,
            out string diagnostics)
        {
            lock (Sync)
            {
                if (!ReferenceEquals(_activeMission, mission))
                {
                    RestoreManualPlacementStateLocked();
                    _activeMission = mission;
                    _deploymentLifecycleFinished = false;
                    CompletedDeploymentSides.Clear();
                }

                if (_deploymentLifecycleFinished)
                {
                    diagnostics = "deployment-already-finished";
                    return false;
                }

                if (enforceSideLifecycle && CompletedDeploymentSides.Contains(side))
                {
                    diagnostics = "side-deployment-already-finished Side=" + side;
                    return false;
                }

                if (_manualPlacementActive && ReferenceEquals(_manualPlacementMission, mission))
                {
                    if (enforceSideLifecycle)
                        ActiveManualPlacementSides.Add(side);
                    mission.IsTeleportingAgents = true;
                    diagnostics =
                        "manual-placement-already-active" +
                        " Side=" + side +
                        " ActiveSides=[" + string.Join(",", ActiveManualPlacementSides) + "]";
                    return true;
                }

                RestoreManualPlacementStateLocked();
                _manualPlacementMission = mission;
                _manualPlacementPreviousTeleportingAgents = mission.IsTeleportingAgents;
                _manualPlacementActive = true;
                if (enforceSideLifecycle)
                    ActiveManualPlacementSides.Add(side);
                mission.IsTeleportingAgents = true;
            }

            diagnostics =
                "manual-placement-started" +
                " Side=" + side +
                " ActiveSides=[" + string.Join(",", ActiveManualPlacementSides) + "]";
            return true;
        }

        public static void EndManualPlacement(Mission mission, string source)
        {
            lock (Sync)
            {
                if (!_manualPlacementActive ||
                    mission != null &&
                    _manualPlacementMission != null &&
                    !ReferenceEquals(_manualPlacementMission, mission))
                {
                    return;
                }

                RestoreManualPlacementStateLocked();
            }
        }

        public static bool IsDeploymentPhaseBlockingBattleStart(Mission mission)
        {
            return IsDeploymentRuntimeActive(mission) && !HasDeploymentLifecycleFinished(mission);
        }

        public static bool HasDeploymentLifecycleFinished(Mission mission)
        {
            if (!IsDeploymentRuntimeActive(mission))
                return false;

            lock (Sync)
            {
                return ReferenceEquals(_activeMission, mission) && _deploymentLifecycleFinished;
            }
        }

        public static bool HasSideDeploymentFinished(
            Mission mission,
            BattleSideEnum side)
        {
            if (side == BattleSideEnum.None || !IsDeploymentRuntimeActive(mission))
                return false;

            lock (Sync)
            {
                return ReferenceEquals(_activeMission, mission) &&
                       CompletedDeploymentSides.Contains(side);
            }
        }

        public static bool TryAutoDeploySide(
            Mission mission,
            BattleSideEnum side,
            out string diagnostics)
        {
            diagnostics = "mission-null";
            if (!IsDeploymentRuntimeActive(mission))
            {
                diagnostics = "deployment-runtime-inactive";
                return false;
            }

            if (side == BattleSideEnum.None)
            {
                diagnostics = "side-none";
                return false;
            }

            Team team = ResolveBattleTeam(mission, side);
            if (team == null)
            {
                diagnostics = "team-missing Side=" + side;
                return false;
            }

            if (!mission.GetDeploymentPlan<IMissionDeploymentPlan>(out IMissionDeploymentPlan deploymentPlan) ||
                deploymentPlan == null)
            {
                diagnostics = "deployment-plan-missing Side=" + side;
                return false;
            }

            int deployedFormationCount = 0;
            int activeUnitCount = 0;
            int teleportedUnitCount = 0;
            var failures = new List<string>();
            bool previousTeleportingAgents = mission.IsTeleportingAgents;
            try
            {
                deploymentPlan.RemakeDeploymentPlan(team);
                if (!deploymentPlan.IsPlanMade(team))
                {
                    diagnostics = "deployment-plan-not-made Side=" + side;
                    return false;
                }

                mission.IsTeleportingAgents = true;
                foreach (Formation formation in team.FormationsIncludingEmpty)
                {
                    if (formation == null || formation.CountOfUnits <= 0)
                        continue;

                    IFormationDeploymentPlan formationPlan =
                        deploymentPlan.GetFormationPlan(team, formation.FormationIndex);
                    mission.GetFormationSpawnFrame(
                        team,
                        formation.FormationIndex,
                        isReinforcement: false,
                        out WorldPosition spawnPosition,
                        out TaleWorlds.Library.Vec2 spawnDirection);

                    if (formationPlan?.HasDimensions == true)
                        formation.SetFormOrder(FormOrder.FormOrderCustom(formationPlan.PlannedWidth));

                    formation.SetMovementOrder(MovementOrder.MovementOrderMove(spawnPosition));
                    formation.SetFacingOrder(FacingOrder.FacingOrderLookAtDirection(spawnDirection));
                    formation.SetPositioning(
                        spawnPosition,
                        spawnDirection,
                        formation.ArrangementOrder.GetUnitSpacing());
                    formation.ApplyActionOnEachUnit(agent =>
                    {
                        if (agent == null || !agent.IsActive())
                            return;

                        activeUnitCount++;
                        agent.ForceUpdateCachedAndFormationValues(
                            updateOnlyMovement: false,
                            arrangementChangeAllowed: false);
                        WorldPosition orderPosition =
                            formation.GetOrderPositionOfUnit(agent);
                        if (!orderPosition.IsValid)
                        {
                            failures.Add(
                                formation.FormationIndex +
                                ":unit-" +
                                agent.Index +
                                "-order-position-invalid");
                            return;
                        }

                        agent.TeleportToPosition(orderPosition.GetGroundVec3());
                        teleportedUnitCount++;
                    });
                    formation.SetHasPendingUnitPositions(hasPendingUnitPositions: false);
                    formation.SetMovementOrder(MovementOrder.MovementOrderStop);
                    deployedFormationCount++;
                }

                diagnostics =
                    "auto-deployed-existing-agents" +
                    " Side=" + side +
                    " TeamIndex=" + team.TeamIndex +
                    " Formations=" + deployedFormationCount +
                    " ActiveUnits=" + activeUnitCount +
                    " TeleportedUnits=" + teleportedUnitCount +
                    " Failures=[" + string.Join("; ", failures) + "]";
                return deployedFormationCount > 0 &&
                       activeUnitCount > 0 &&
                       teleportedUnitCount == activeUnitCount;
            }
            catch (Exception ex)
            {
                diagnostics =
                    "auto-deploy-faulted " +
                    ex.GetType().Name + ":" + ex.Message +
                    " Side=" + side +
                    " Formations=" + deployedFormationCount +
                    " ActiveUnits=" + activeUnitCount +
                    " TeleportedUnits=" + teleportedUnitCount +
                    " Failures=[" + string.Join("; ", failures) + "]";
                return false;
            }
            finally
            {
                mission.IsTeleportingAgents = previousTeleportingAgents;
            }
        }

        public static bool TryForceAutoDeployBothSidesAndFinish(
            Mission mission,
            out string diagnostics)
        {
            diagnostics = "deployment-runtime-inactive";
            if (!IsDeploymentRuntimeActive(mission))
                return false;

            bool attackerAlreadyFinished =
                HasSideDeploymentFinished(mission, BattleSideEnum.Attacker);
            bool defenderAlreadyFinished =
                HasSideDeploymentFinished(mission, BattleSideEnum.Defender);
            string attackerDiagnostics = "preserved-completed-side";
            string defenderDiagnostics = "preserved-completed-side";
            bool attackerDeployed = attackerAlreadyFinished;
            bool defenderDeployed = defenderAlreadyFinished;
            if (!attackerAlreadyFinished)
            {
                attackerDeployed = TryAutoDeploySide(
                    mission,
                    BattleSideEnum.Attacker,
                    out attackerDiagnostics);
            }

            if (!defenderAlreadyFinished)
            {
                defenderDeployed = TryAutoDeploySide(
                    mission,
                    BattleSideEnum.Defender,
                    out defenderDiagnostics);
            }

            if (!attackerDeployed || !defenderDeployed)
            {
                diagnostics =
                    "both-sides-auto-deploy-incomplete" +
                    " Attacker=" + attackerDeployed +
                    " AttackerDiagnostics={" + attackerDiagnostics + "}" +
                    " Defender=" + defenderDeployed +
                    " DefenderDiagnostics={" + defenderDiagnostics + "}";
                return false;
            }

            bool deploymentFinished = TryFinishDeployment(
                mission,
                out string finishDiagnostics);
            bool stillBlocking = IsDeploymentPhaseBlockingBattleStart(mission);
            diagnostics =
                "both-sides-auto-deploy" +
                " Attacker={" + attackerDiagnostics + "}" +
                " Defender={" + defenderDiagnostics + "}" +
                " Finished=" + deploymentFinished +
                " StillBlocking=" + stillBlocking +
                " FinishDiagnostics={" + finishDiagnostics + "}";
            return deploymentFinished && !stillBlocking;
        }

        public static bool TryCompleteSideDeployment(
            Mission mission,
            BattleSideEnum side,
            out bool deploymentLifecycleFinished,
            out string diagnostics)
        {
            deploymentLifecycleFinished = false;
            diagnostics = "deployment-runtime-inactive";
            if (!IsDeploymentRuntimeActive(mission))
                return false;

            if (side == BattleSideEnum.None)
            {
                diagnostics = "side-none";
                return false;
            }

            lock (Sync)
            {
                if (!ReferenceEquals(_activeMission, mission))
                {
                    diagnostics = "active-mission-mismatch";
                    return false;
                }

                if (_deploymentLifecycleFinished)
                {
                    deploymentLifecycleFinished = true;
                    diagnostics = "deployment-already-finished";
                    return true;
                }

                CompletedDeploymentSides.Add(side);
                ActiveManualPlacementSides.Remove(side);
                bool bothSidesFinished =
                    CompletedDeploymentSides.Contains(BattleSideEnum.Attacker) &&
                    CompletedDeploymentSides.Contains(BattleSideEnum.Defender);
                if (bothSidesFinished)
                {
                    _deploymentLifecycleFinished = true;
                    RestoreManualPlacementStateLocked();
                }
                else if (_manualPlacementActive)
                {
                    mission.IsTeleportingAgents = true;
                }

                deploymentLifecycleFinished = _deploymentLifecycleFinished;
                diagnostics =
                    "side-deployment-finished" +
                    " Side=" + side +
                    " CompletedSides=[" + string.Join(",", CompletedDeploymentSides) + "]" +
                    " ActiveSides=[" + string.Join(",", ActiveManualPlacementSides) + "]" +
                    " LifecycleFinished=" + deploymentLifecycleFinished;
                return true;
            }
        }

        public static bool TryFinishDeployment(Mission mission, out string diagnostics)
        {
            diagnostics = "deployment-runtime-inactive";
            if (!IsDeploymentRuntimeActive(mission))
                return false;

            lock (Sync)
            {
                if (!ReferenceEquals(_activeMission, mission))
                {
                    diagnostics = "active-mission-mismatch";
                    return false;
                }

                _deploymentLifecycleFinished = true;
                CompletedDeploymentSides.Add(BattleSideEnum.Attacker);
                CompletedDeploymentSides.Add(BattleSideEnum.Defender);
                RestoreManualPlacementStateLocked();
            }

            diagnostics = "formation-only-deployment-finished";
            return true;
        }

        public static void ResetRuntimeState(Mission mission, string source)
        {
            lock (Sync)
            {
                if (mission != null &&
                    _activeMission != null &&
                    !ReferenceEquals(_activeMission, mission))
                {
                    return;
                }

                RestoreManualPlacementStateLocked();
                _activeMission = null;
                _deploymentLifecycleFinished = false;
                CompletedDeploymentSides.Clear();
            }
        }

        private static void RestoreManualPlacementStateLocked()
        {
            if (!_manualPlacementActive)
                return;

            Mission placementMission = _manualPlacementMission;
            bool previousTeleportingAgents = _manualPlacementPreviousTeleportingAgents;
            _manualPlacementMission = null;
            _manualPlacementActive = false;
            _manualPlacementPreviousTeleportingAgents = false;
            ActiveManualPlacementSides.Clear();

            if (placementMission == null)
                return;

            try
            {
                placementMission.IsTeleportingAgents = previousTeleportingAgents;
            }
            catch
            {
            }
        }

        private static bool TryValidateRuntime(Mission mission, out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            if (!ExactCampaignArmyBootstrap.IsActive(mission))
            {
                diagnostics = "exact-bootstrap-inactive";
                return false;
            }

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetScenarioContext() ??
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            return ExactLandBattleScenarioContract.IsValidatedFieldBattleScenario(
                scenarioContext,
                mission.SceneName,
                out diagnostics);
        }

        private static bool TryValidateClientManualPlacement(Mission mission, out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            if (!GameNetwork.IsClient)
            {
                diagnostics = "not-client";
                return false;
            }

            if (!GameNetwork.IsSessionActive)
            {
                diagnostics = "client-session-inactive";
                return false;
            }

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetScenarioContext() ??
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            return ExactLandBattleScenarioContract.IsValidatedFieldBattleScenario(
                scenarioContext,
                mission.SceneName,
                out diagnostics);
        }

        private static Team ResolveBattleTeam(Mission mission, BattleSideEnum side)
        {
            if (mission?.Teams == null)
                return null;

            foreach (Team team in mission.Teams)
            {
                if (team != null &&
                    !ReferenceEquals(team, mission.SpectatorTeam) &&
                    team.Side == side)
                {
                    return team;
                }
            }

            return null;
        }
    }
}
