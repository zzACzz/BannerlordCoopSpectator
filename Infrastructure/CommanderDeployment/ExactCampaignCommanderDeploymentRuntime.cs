using System.Collections.Generic;
using CoopSpectator.Network.Messages;
using CoopSpectator.Infrastructure.SiegeAmbush;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    internal static class ExactCampaignCommanderDeploymentRuntime
    {
        private static readonly object SiegeDeploymentSync = new object();
        private static Mission _siegeDeploymentMission;
        private static bool _siegeDeploymentLifecycleFinished;
        private static readonly HashSet<BattleSideEnum> CompletedSiegeDeploymentSides =
            new HashSet<BattleSideEnum>();

        public static bool IsExactLandBattleScenario(
            Mission mission,
            BattleScenarioContextMessage scenarioContext)
        {
            return mission != null &&
                   ExactLandBattleScenarioContract.IsValidatedScenario(
                       scenarioContext,
                       mission.SceneName,
                       out _);
        }

        public static bool IsCommanderDeploymentScenario(
            Mission mission,
            BattleScenarioContextMessage scenarioContext)
        {
            return ExactCampaignSiegeAssaultWithDeploymentRuntime.IsExactSiegeWithDeploymentScenario(scenarioContext) ||
                   IsExactLandBattleScenario(mission, scenarioContext);
        }

        public static bool ShouldPreserveMountedFormationClasses(
            Mission mission,
            BattleScenarioContextMessage scenarioContext,
            BattleSideEnum side)
        {
            if (mission == null)
                return false;

            if (IsExactLandBattleScenario(mission, scenarioContext))
                return true;

            return SiegeAmbushScenarioContract.IsSiegeAmbushScenario(scenarioContext) &&
                   side != BattleSideEnum.None &&
                   ExactCampaignSiegeAssaultWithDeploymentRuntime
                       .ShouldDeploymentPlanSpawnWithHorses(
                           mission,
                           side);
        }

        public static bool IsDeploymentRuntimeActive(Mission mission)
        {
            if (ExactCampaignSiegeAssaultWithDeploymentRuntime.IsDeploymentRuntimeActive(mission))
                return true;

            return ExactLandBattleCommanderDeploymentRuntime.IsDeploymentRuntimeActive(mission);
        }

        public static bool IsDeploymentPhaseBlockingBattleStart(Mission mission)
        {
            if (ExactCampaignSiegeAssaultWithDeploymentRuntime.IsDeploymentRuntimeActive(mission))
            {
                return ExactCampaignSiegeAssaultWithDeploymentRuntime
                    .IsDeploymentPhaseBlockingBattleStart(mission);
            }

            return ExactLandBattleCommanderDeploymentRuntime.IsDeploymentPhaseBlockingBattleStart(mission);
        }

        public static bool HasSideDeploymentFinished(
            Mission mission,
            BattleSideEnum side)
        {
            if (ExactCampaignSiegeAssaultWithDeploymentRuntime.IsDeploymentRuntimeActive(mission))
            {
                if (side == BattleSideEnum.None)
                    return false;

                EnsureSiegeDeploymentState(mission);
                lock (SiegeDeploymentSync)
                {
                    return ReferenceEquals(_siegeDeploymentMission, mission) &&
                           CompletedSiegeDeploymentSides.Contains(side);
                }
            }

            return ExactLandBattleCommanderDeploymentRuntime.HasSideDeploymentFinished(
                mission,
                side);
        }

        public static bool TryAutoDeployDeploymentOnly(
            Mission mission,
            BattleSideEnum side,
            out string diagnostics)
        {
            if (ExactCampaignSiegeAssaultWithDeploymentRuntime.IsDeploymentRuntimeActive(mission))
            {
                return ExactCampaignSiegeAssaultWithDeploymentRuntime.TryAutoDeployDeploymentOnly(
                    mission,
                    side,
                    out diagnostics);
            }

            return ExactLandBattleCommanderDeploymentRuntime.TryAutoDeploySide(
                mission,
                side,
                out diagnostics);
        }

        public static bool TryBeginManualFormationPlacement(
            Mission mission,
            BattleSideEnum side,
            out string diagnostics)
        {
            if (ExactCampaignSiegeAssaultWithDeploymentRuntime.IsDeploymentRuntimeActive(mission))
            {
                diagnostics = "native-siege-deployment-placement";
                return true;
            }

            return ExactLandBattleCommanderDeploymentRuntime.TryBeginManualPlacement(
                mission,
                side,
                out diagnostics);
        }

        public static bool TryBeginClientManualFormationPlacement(Mission mission, out string diagnostics)
        {
            return ExactLandBattleCommanderDeploymentRuntime.TryBeginClientManualPlacement(
                mission,
                out diagnostics);
        }

        public static bool IsClientManualFormationPlacementActive(Mission mission)
        {
            return ExactLandBattleCommanderDeploymentRuntime.IsClientManualPlacementActive(mission);
        }

        public static void EndManualFormationPlacement(Mission mission, string source)
        {
            ExactLandBattleCommanderDeploymentRuntime.EndManualPlacement(mission, source);
        }

        public static bool TryCompleteCommanderDeployment(
            Mission mission,
            BattleSideEnum side,
            out bool deploymentLifecycleFinished,
            out string diagnostics)
        {
            if (ExactCampaignSiegeAssaultWithDeploymentRuntime.IsDeploymentRuntimeActive(mission))
            {
                if (side == BattleSideEnum.None)
                {
                    deploymentLifecycleFinished = false;
                    diagnostics = "side-none";
                    return false;
                }

                EnsureSiegeDeploymentState(mission);
                List<BattleSideEnum> completedSides;
                bool bothSidesFinished;
                lock (SiegeDeploymentSync)
                {
                    if (!ReferenceEquals(_siegeDeploymentMission, mission))
                    {
                        deploymentLifecycleFinished = false;
                        diagnostics = "active-mission-mismatch";
                        return false;
                    }

                    if (_siegeDeploymentLifecycleFinished)
                    {
                        deploymentLifecycleFinished = true;
                        diagnostics = "deployment-already-finished";
                        return true;
                    }

                    CompletedSiegeDeploymentSides.Add(side);
                    completedSides = new List<BattleSideEnum>(CompletedSiegeDeploymentSides);
                    bothSidesFinished =
                        CompletedSiegeDeploymentSides.Contains(BattleSideEnum.Attacker) &&
                        CompletedSiegeDeploymentSides.Contains(BattleSideEnum.Defender);
                }

                if (!bothSidesFinished)
                {
                    deploymentLifecycleFinished = false;
                    diagnostics =
                        "side-deployment-finished" +
                        " Side=" + side +
                        " CompletedSides=[" + string.Join(",", completedSides) + "]" +
                        " LifecycleFinished=False";
                    return true;
                }

                bool completed =
                    ExactCampaignSiegeAssaultWithDeploymentRuntime
                        .TryForceAutoDeployAndFinishDeploymentPreservingSides(
                            mission,
                            completedSides,
                            out string finishDiagnostics);
                bool stillBlocking =
                    ExactCampaignSiegeAssaultWithDeploymentRuntime
                        .IsDeploymentPhaseBlockingBattleStart(mission);
                deploymentLifecycleFinished = completed && !stillBlocking;
                if (deploymentLifecycleFinished)
                    MarkSiegeDeploymentLifecycleFinished(mission);

                diagnostics =
                    "both-sides-deployment-finished" +
                    " Side=" + side +
                    " CompletedSides=[" + string.Join(",", completedSides) + "]" +
                    " Finished=" + completed +
                    " StillBlocking=" + stillBlocking +
                    " FinishDiagnostics={" + finishDiagnostics + "}";
                return completed;
            }

            return ExactLandBattleCommanderDeploymentRuntime.TryCompleteSideDeployment(
                mission,
                side,
                out deploymentLifecycleFinished,
                out diagnostics);
        }

        public static bool TryForceFinishForBattleStartRequest(
            Mission mission,
            IEnumerable<BattleSideEnum> activeCommanderSides,
            out string diagnostics)
        {
            if (ExactCampaignSiegeAssaultWithDeploymentRuntime.IsDeploymentRuntimeActive(mission))
            {
                EnsureSiegeDeploymentState(mission);
                List<BattleSideEnum> completedSides;
                lock (SiegeDeploymentSync)
                    completedSides = new List<BattleSideEnum>(CompletedSiegeDeploymentSides);

                var preservedSides = new HashSet<BattleSideEnum>(completedSides);
                var validatedActiveCommanderSides = new List<BattleSideEnum>();
                BattleScenarioContextMessage scenarioContext =
                    BattleSnapshotRuntimeState.GetScenarioContext() ??
                    BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                    BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
                if (SiegeAmbushScenarioContract.IsSiegeAmbushScenario(scenarioContext) &&
                    activeCommanderSides != null)
                {
                    foreach (BattleSideEnum activeCommanderSide in activeCommanderSides)
                    {
                        if (activeCommanderSide == BattleSideEnum.None ||
                            !preservedSides.Add(activeCommanderSide))
                        {
                            continue;
                        }

                        validatedActiveCommanderSides.Add(activeCommanderSide);
                    }
                }

                bool completed =
                    ExactCampaignSiegeAssaultWithDeploymentRuntime
                        .TryForceAutoDeployAndFinishDeploymentPreservingSides(
                            mission,
                            preservedSides,
                            out string finishDiagnostics);
                bool stillBlocking =
                    ExactCampaignSiegeAssaultWithDeploymentRuntime
                        .IsDeploymentPhaseBlockingBattleStart(mission);
                if (completed && !stillBlocking)
                    MarkSiegeDeploymentLifecycleFinished(mission);

                diagnostics =
                    "battle-start-authority-forced-finish" +
                    " ReadySides=[" + string.Join(",", completedSides) + "]" +
                    " ActiveCommanderSides=[" +
                    string.Join(",", validatedActiveCommanderSides) + "]" +
                    " PreservedSides=[" + string.Join(",", preservedSides) + "]" +
                    " Finished=" + completed +
                    " StillBlocking=" + stillBlocking +
                    " FinishDiagnostics={" + finishDiagnostics + "}";
                return completed && !stillBlocking;
            }

            if (ExactLandBattleCommanderDeploymentRuntime.IsDeploymentRuntimeActive(mission))
            {
                return ExactLandBattleCommanderDeploymentRuntime.TryForceAutoDeployBothSidesAndFinish(
                    mission,
                    out diagnostics);
            }

            diagnostics = "deployment-runtime-inactive";
            return false;
        }

        public static bool TryEnsureScenarioReadyBeforeBattleStart(
            Mission mission,
            out string diagnostics)
        {
            if (ExactCampaignSiegeAssaultWithDeploymentRuntime.IsDeploymentRuntimeActive(mission))
            {
                return ExactCampaignSiegeAssaultWithDeploymentRuntime
                    .TryEnsureAutoDeployedSiegeMachinesBeforeBattleStart(mission, out diagnostics);
            }

            diagnostics = "not-required-exact-land-battle";
            return true;
        }

        public static bool ShouldTreatAllowedPrebattleSelectableSourceAsReady(
            Mission mission,
            BattleSideEnum side,
            CoopBattlePhase currentPhase,
            string selectableSource)
        {
            if (ExactCampaignSiegeAssaultWithDeploymentRuntime.IsDeploymentRuntimeActive(mission))
            {
                return ExactCampaignSiegeAssaultWithDeploymentRuntime
                    .ShouldTreatAllowedPrebattleSelectableSourceAsReady(
                        mission,
                        side,
                        currentPhase,
                        selectableSource);
            }

            return ExactLandBattleCommanderDeploymentRuntime.IsDeploymentRuntimeActive(mission) &&
                   side != BattleSideEnum.None &&
                   currentPhase >= CoopBattlePhase.SideSelection &&
                   currentPhase < CoopBattlePhase.BattleActive &&
                   !string.IsNullOrWhiteSpace(selectableSource) &&
                   selectableSource.StartsWith("allowed-prebattle", System.StringComparison.OrdinalIgnoreCase);
        }

        public static void ResetRuntimeState(Mission mission, string source)
        {
            lock (SiegeDeploymentSync)
            {
                if (mission == null ||
                    _siegeDeploymentMission == null ||
                    ReferenceEquals(_siegeDeploymentMission, mission))
                {
                    _siegeDeploymentMission = null;
                    _siegeDeploymentLifecycleFinished = false;
                    CompletedSiegeDeploymentSides.Clear();
                }
            }

            ExactLandBattleCommanderDeploymentRuntime.ResetRuntimeState(mission, source);
        }

        private static void EnsureSiegeDeploymentState(Mission mission)
        {
            if (mission == null)
                return;

            bool nativeLifecycleFinished =
                !ExactCampaignSiegeAssaultWithDeploymentRuntime
                    .IsDeploymentPhaseBlockingBattleStart(mission);
            lock (SiegeDeploymentSync)
            {
                if (!ReferenceEquals(_siegeDeploymentMission, mission))
                {
                    _siegeDeploymentMission = mission;
                    _siegeDeploymentLifecycleFinished = false;
                    CompletedSiegeDeploymentSides.Clear();
                }

                if (!nativeLifecycleFinished)
                    return;

                _siegeDeploymentLifecycleFinished = true;
                CompletedSiegeDeploymentSides.Add(BattleSideEnum.Attacker);
                CompletedSiegeDeploymentSides.Add(BattleSideEnum.Defender);
            }
        }

        private static void MarkSiegeDeploymentLifecycleFinished(Mission mission)
        {
            lock (SiegeDeploymentSync)
            {
                if (!ReferenceEquals(_siegeDeploymentMission, mission))
                    return;

                _siegeDeploymentLifecycleFinished = true;
                CompletedSiegeDeploymentSides.Add(BattleSideEnum.Attacker);
                CompletedSiegeDeploymentSides.Add(BattleSideEnum.Defender);
            }
        }
    }
}
