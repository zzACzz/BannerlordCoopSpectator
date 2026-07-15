using System;
using CoopSpectator.Network.Messages;

namespace CoopSpectator.Infrastructure.SallyOut
{
    internal static class SallyOutScenarioContract
    {
        public const string SiegeSubtype = "SallyOut";
        public const string ResultStage = "SallyOut";

        public static bool IsSallyOutScenario(BattleScenarioContextMessage scenarioContext)
        {
            return scenarioContext?.IsSiegeBattle == true &&
                   IsSallyOutSiegeContext(scenarioContext.SiegeContext);
        }

        public static bool IsSallyOutSiegeContext(BattleSiegeContextMessage siegeContext)
        {
            return siegeContext != null &&
                   string.Equals(siegeContext.SiegeSubtype, SiegeSubtype, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsValidatedScenario(
            BattleScenarioContextMessage scenarioContext,
            string runtimeScene,
            out string diagnostics)
        {
            diagnostics = "scenario-null";
            if (!IsSallyOutScenario(scenarioContext))
                return false;

            BattleSiegeContextMessage siegeContext = scenarioContext.SiegeContext;
            if (string.IsNullOrWhiteSpace(siegeContext.SettlementId))
            {
                diagnostics = "settlement-id-empty";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(siegeContext.SceneLocationId))
            {
                diagnostics =
                    "unexpected-settlement-scene-location Value=" +
                    siegeContext.SceneLocationId;
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

            string expectedScene = siegeContext.MissionInitializerSceneName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(expectedScene))
            {
                diagnostics = "mission-initializer-scene-empty";
                return false;
            }

            if (string.IsNullOrWhiteSpace(runtimeScene) ||
                !string.Equals(expectedScene, runtimeScene, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics =
                    "runtime-scene-mismatch Runtime=" + (runtimeScene ?? string.Empty) +
                    " Expected=" + expectedScene;
                return false;
            }

            diagnostics =
                "validated Settlement=" + siegeContext.SettlementId +
                " Scene=" + runtimeScene +
                " SceneHasMapPatch=True";
            return true;
        }
    }
}
