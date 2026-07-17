using System;
using CoopSpectator.Network.Messages;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    internal static class ExactLandBattleCommanderDeploymentRuntime
    {
        private static readonly object Sync = new object();

        private static Mission _activeMission;
        private static bool _deploymentLifecycleFinished;
        private static Mission _manualPlacementMission;
        private static bool _manualPlacementActive;
        private static bool _manualPlacementPreviousTeleportingAgents;

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
                }

                return true;
            }
        }

        public static bool TryBeginManualPlacement(Mission mission, out string diagnostics)
        {
            diagnostics = "deployment-runtime-inactive";
            if (!IsDeploymentRuntimeActive(mission))
                return false;

            return TryBeginValidatedManualPlacement(mission, out diagnostics);
        }

        public static bool TryBeginClientManualPlacement(Mission mission, out string diagnostics)
        {
            if (!TryValidateClientManualPlacement(mission, out diagnostics))
                return false;

            Mission.MissionTeamAITypeEnum previousMissionTeamAiType = mission.MissionTeamAIType;
            mission.MissionTeamAIType = Mission.MissionTeamAITypeEnum.FieldBattle;

            bool placementStarted = TryBeginValidatedManualPlacement(
                mission,
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

        private static bool TryBeginValidatedManualPlacement(Mission mission, out string diagnostics)
        {
            lock (Sync)
            {
                if (!ReferenceEquals(_activeMission, mission))
                {
                    RestoreManualPlacementStateLocked();
                    _activeMission = mission;
                    _deploymentLifecycleFinished = false;
                }

                if (_deploymentLifecycleFinished)
                {
                    diagnostics = "deployment-already-finished";
                    return false;
                }

                if (_manualPlacementActive && ReferenceEquals(_manualPlacementMission, mission))
                {
                    mission.IsTeleportingAgents = true;
                    diagnostics = "manual-placement-already-active";
                    return true;
                }

                RestoreManualPlacementStateLocked();
                _manualPlacementMission = mission;
                _manualPlacementPreviousTeleportingAgents = mission.IsTeleportingAgents;
                _manualPlacementActive = true;
                mission.IsTeleportingAgents = true;
            }

            diagnostics = "manual-placement-started";
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
                        agent.ForceUpdateCachedAndFormationValues(
                            updateOnlyMovement: true,
                            arrangementChangeAllowed: false));
                    formation.SetHasPendingUnitPositions(hasPendingUnitPositions: false);
                    formation.SetMovementOrder(MovementOrder.MovementOrderStop);
                    deployedFormationCount++;
                }

                diagnostics =
                    "auto-deployed-existing-agents" +
                    " Side=" + side +
                    " TeamIndex=" + team.TeamIndex +
                    " Formations=" + deployedFormationCount;
                return deployedFormationCount > 0;
            }
            catch (Exception ex)
            {
                diagnostics =
                    "auto-deploy-faulted " +
                    ex.GetType().Name + ":" + ex.Message +
                    " Side=" + side +
                    " Formations=" + deployedFormationCount;
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

            bool attackerDeployed = TryAutoDeploySide(
                mission,
                BattleSideEnum.Attacker,
                out string attackerDiagnostics);
            bool defenderDeployed = TryAutoDeploySide(
                mission,
                BattleSideEnum.Defender,
                out string defenderDiagnostics);
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
            return ExactLandBattleScenarioContract.IsValidatedScenario(
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
            return ExactLandBattleScenarioContract.IsValidatedScenario(
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
