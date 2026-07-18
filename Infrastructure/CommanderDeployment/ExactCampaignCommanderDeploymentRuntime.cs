using CoopSpectator.Network.Messages;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    internal static class ExactCampaignCommanderDeploymentRuntime
    {
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

        public static bool TryBeginManualFormationPlacement(Mission mission, out string diagnostics)
        {
            if (ExactCampaignSiegeAssaultWithDeploymentRuntime.IsDeploymentRuntimeActive(mission))
            {
                diagnostics = "native-siege-deployment-placement";
                return true;
            }

            return ExactLandBattleCommanderDeploymentRuntime.TryBeginManualPlacement(mission, out diagnostics);
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

        public static bool TryFinishDeployment(Mission mission, out string diagnostics)
        {
            if (ExactCampaignSiegeAssaultWithDeploymentRuntime.IsDeploymentRuntimeActive(mission))
            {
                return ExactCampaignSiegeAssaultWithDeploymentRuntime.TryForceAutoDeployAndFinishDeployment(
                    mission,
                    out diagnostics);
            }

            return ExactLandBattleCommanderDeploymentRuntime.TryFinishDeployment(mission, out diagnostics);
        }

        public static bool TryForceFinishForBattleStartRequest(
            Mission mission,
            out string diagnostics)
        {
            if (ExactCampaignSiegeAssaultWithDeploymentRuntime.IsDeploymentRuntimeActive(mission))
            {
                return ExactCampaignSiegeAssaultWithDeploymentRuntime.TryForceAutoDeployAndFinishDeployment(
                    mission,
                    out diagnostics);
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
            ExactLandBattleCommanderDeploymentRuntime.ResetRuntimeState(mission, source);
        }
    }
}
