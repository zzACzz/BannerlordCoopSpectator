using System;
using CoopSpectator.Network.Messages;

namespace CoopSpectator.Infrastructure.Relief
{
    internal static class ExactReliefScenarioContract
    {
        public const string CampaignBattleType = "SiegeOutside";
        public const string SiegeSubtype = "Relief";
        public const string ResultStage = "Relief";
        public const string SceneLocationId = "center";

        public static bool IsReliefScenario(
            BattleScenarioContextMessage scenarioContext)
        {
            return scenarioContext?.IsSiegeBattle == true &&
                   string.Equals(
                       scenarioContext.ScenarioKind,
                       "Siege",
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(
                       scenarioContext.CampaignBattleType,
                       CampaignBattleType,
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(
                       scenarioContext.SiegeContext?.SiegeSubtype,
                       SiegeSubtype,
                       StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsReliefResult(
            CoopBattleResultBridgeFile.BattleResultSnapshot result)
        {
            return string.Equals(
                result?.BattleStage,
                ResultStage,
                StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsValidatedScenario(
            BattleScenarioContextMessage scenarioContext,
            string runtimeScene,
            out string diagnostics)
        {
            diagnostics = "not-relief-scenario";
            if (!IsReliefScenario(scenarioContext))
                return false;

            BattleSiegeContextMessage siegeContext = scenarioContext.SiegeContext;
            if (string.IsNullOrWhiteSpace(siegeContext.SettlementId))
            {
                diagnostics = "settlement-id-empty";
                return false;
            }

            if (!string.Equals(
                    siegeContext.SettlementKind,
                    "Town",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    siegeContext.SettlementKind,
                    "Castle",
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostics =
                    "settlement-kind-invalid Value=" +
                    (siegeContext.SettlementKind ?? string.Empty);
                return false;
            }

            if (!string.Equals(
                    siegeContext.SceneLocationId,
                    SceneLocationId,
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostics =
                    "scene-location-invalid Value=" +
                    (siegeContext.SceneLocationId ?? string.Empty);
                return false;
            }

            if (!CampaignMissionShellRuntimeState.IsWithDeploymentMissionShell(
                    siegeContext.MissionShell))
            {
                diagnostics =
                    CampaignMissionShellRuntimeState.IsNoDeploymentMissionShell(
                        siegeContext.MissionShell)
                        ? "mission-shell-unsupported-no-deployment"
                        : "mission-shell-invalid Value=" +
                          (siegeContext.MissionShell ?? string.Empty);
                return false;
            }

            if (!siegeContext.HasMissionInitializerRecord)
            {
                diagnostics = "mission-initializer-missing";
                return false;
            }

            if (siegeContext.MissionInitializerSceneHasMapPatch)
            {
                diagnostics = "mission-initializer-map-patch-enabled";
                return false;
            }

            string expectedScene =
                siegeContext.MissionInitializerSceneName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(expectedScene))
            {
                diagnostics = "mission-initializer-scene-empty";
                return false;
            }

            if (string.IsNullOrWhiteSpace(runtimeScene) ||
                !string.Equals(
                    expectedScene,
                    runtimeScene,
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostics =
                    "runtime-scene-mismatch Runtime=" +
                    (runtimeScene ?? string.Empty) +
                    " Expected=" + expectedScene;
                return false;
            }

            diagnostics =
                "Mode=Relief" +
                " Settlement=" + siegeContext.SettlementId +
                " Scene=" + runtimeScene +
                " MissionShell=" + siegeContext.MissionShell +
                " SceneHasMapPatch=False";
            return true;
        }
    }
}
