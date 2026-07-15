using System;
using CoopSpectator.Network.Messages;

namespace CoopSpectator.Infrastructure.LordsHall
{
    internal static class LordsHallScenarioContract
    {
        public const string SiegeSubtype = "LordsHall";
        public const string MissionShell = "SiegeLordsHallFightMission";
        public const string SceneLocationId = "lordshall";
        public const string SiegeState = "InTheLordsHall";
        public const string MissionSceneLevels = "siege";

        public static bool IsLordsHallScenario(BattleScenarioContextMessage scenarioContext)
        {
            return scenarioContext?.IsSiegeBattle == true &&
                   IsLordsHallSiegeContext(scenarioContext.SiegeContext);
        }

        public static bool IsLordsHallSiegeContext(BattleSiegeContextMessage siegeContext)
        {
            return siegeContext != null &&
                   string.Equals(siegeContext.SiegeSubtype, SiegeSubtype, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsLordsHallMissionShell(string missionShell)
        {
            return string.Equals(missionShell, MissionShell, StringComparison.Ordinal);
        }

        public static bool IsValidatedScenario(
            BattleScenarioContextMessage scenarioContext,
            string runtimeScene,
            out string diagnostics)
        {
            diagnostics = "scenario-null";
            if (!IsLordsHallScenario(scenarioContext))
                return false;

            BattleSiegeContextMessage siegeContext = scenarioContext.SiegeContext;
            if (!string.Equals(siegeContext.CurrentSiegeState, SiegeState, StringComparison.Ordinal))
            {
                diagnostics = "siege-state-mismatch Value=" + (siegeContext.CurrentSiegeState ?? "null");
                return false;
            }

            if (!string.Equals(siegeContext.SceneLocationId, SceneLocationId, StringComparison.Ordinal))
            {
                diagnostics = "scene-location-mismatch Value=" + (siegeContext.SceneLocationId ?? "null");
                return false;
            }

            if (!IsLordsHallMissionShell(siegeContext.MissionShell))
            {
                diagnostics = "mission-shell-mismatch Value=" + (siegeContext.MissionShell ?? "null");
                return false;
            }

            if (string.IsNullOrWhiteSpace(siegeContext.SettlementId))
            {
                diagnostics = "settlement-id-empty";
                return false;
            }

            string expectedScene = siegeContext.MissionInitializerSceneName ?? string.Empty;
            if (!siegeContext.HasMissionInitializerRecord || string.IsNullOrWhiteSpace(expectedScene))
            {
                diagnostics = "mission-initializer-scene-empty";
                return false;
            }

            if (string.IsNullOrWhiteSpace(runtimeScene) ||
                !string.Equals(expectedScene, runtimeScene, StringComparison.OrdinalIgnoreCase))
            {
                diagnostics =
                    "runtime-scene-mismatch Runtime=" + runtimeScene +
                    " Expected=" + expectedScene;
                return false;
            }

            diagnostics =
                "validated Settlement=" + siegeContext.SettlementId +
                " Scene=" + (runtimeScene ?? string.Empty) +
                " MissionShell=" + siegeContext.MissionShell;
            return true;
        }
    }
}
