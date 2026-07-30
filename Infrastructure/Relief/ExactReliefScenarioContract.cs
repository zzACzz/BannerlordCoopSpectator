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
            return scenarioContext != null &&
                   !scenarioContext.IsSiegeBattle &&
                   string.Equals(
                       scenarioContext.ScenarioKind,
                       ExactLandBattleScenarioContract.FieldBattleScenarioKind,
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

            if (!siegeContext.HasMissionInitializerRecord)
            {
                diagnostics = "mission-initializer-missing";
                return false;
            }

            if (!siegeContext.MissionInitializerSceneHasMapPatch)
            {
                diagnostics = "mission-initializer-map-patch-disabled";
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
                !SceneRuntimeClassifier.IsCampaignBattleScene(runtimeScene) ||
                !string.Equals(
                    expectedScene,
                    runtimeScene,
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostics =
                    "relief-field-runtime-scene-invalid Runtime=" +
                    (runtimeScene ?? string.Empty) +
                    " SourceMissionScene=" + expectedScene;
                return false;
            }

            diagnostics =
                "Mode=Relief" +
                " Settlement=" + siegeContext.SettlementId +
                " Scene=" + runtimeScene +
                " SourceMissionScene=" + expectedScene +
                " SceneHasMapPatch=True" +
                " TacticalMode=FieldBattle";
            return true;
        }
    }
}
