using System;
using System.Linq;
using CoopSpectator.Network.Messages;

namespace CoopSpectator.Infrastructure.SiegeAmbush
{
    internal static class SiegeAmbushScenarioContract
    {
        public const string SiegeSubtype = "SiegeAmbush";
        public const string ResultStage = "SiegeAmbush";
        public const string SceneLocationId = "center";

        public static bool IsSiegeAmbushScenario(BattleScenarioContextMessage scenarioContext)
        {
            return scenarioContext?.IsSiegeBattle == true &&
                   IsSiegeAmbushSiegeContext(scenarioContext.SiegeContext);
        }

        public static bool IsSiegeAmbushSiegeContext(BattleSiegeContextMessage siegeContext)
        {
            return siegeContext != null &&
                   string.Equals(
                       siegeContext.SiegeSubtype,
                       SiegeSubtype,
                       StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSiegeAmbushResult(
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
            diagnostics = "scenario-null";
            if (!IsSiegeAmbushScenario(scenarioContext))
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
                    "mission-shell-invalid Value=" +
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

            string expectedScene = siegeContext.MissionInitializerSceneName ?? string.Empty;
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
                    "runtime-scene-mismatch Runtime=" + (runtimeScene ?? string.Empty) +
                    " Expected=" + expectedScene;
                return false;
            }

            if (siegeContext.AttackerSiegeEngines == null ||
                !siegeContext.AttackerSiegeEngines.Any(
                    engine =>
                        engine != null &&
                        !string.IsNullOrWhiteSpace(engine.EngineTypeId) &&
                        engine.Health > 0f))
            {
                diagnostics = "prepared-attacker-siege-engines-missing";
                return false;
            }

            if (siegeContext.WallHitPointRatios != null &&
                siegeContext.WallHitPointRatios.Any(ratio => ratio <= 0f))
            {
                diagnostics = "breached-wall-present";
                return false;
            }

            diagnostics =
                "validated Settlement=" + siegeContext.SettlementId +
                " Scene=" + runtimeScene +
                " AttackerSiegeEngines=" + siegeContext.AttackerSiegeEngines.Count +
                " SceneHasMapPatch=False";
            return true;
        }
    }
}
