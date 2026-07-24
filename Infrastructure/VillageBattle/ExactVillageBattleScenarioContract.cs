using System;
using CoopSpectator.Network.Messages;

namespace CoopSpectator.Infrastructure.VillageBattle
{
    internal static class ExactVillageBattleScenarioContract
    {
        public const string ScenarioKind = "VillageBattle";
        public const string ResultStage = "Battle";
        public const string Mode = "VillageBattle";

        public static bool IsVillageBattleScenario(
            BattleScenarioContextMessage scenarioContext)
        {
            return scenarioContext != null &&
                   !scenarioContext.IsSiegeBattle &&
                   string.Equals(
                       scenarioContext.ScenarioKind,
                       ScenarioKind,
                       StringComparison.OrdinalIgnoreCase) &&
                   IsSupportedCampaignBattleType(
                       scenarioContext.CampaignBattleType);
        }

        public static bool IsSupportedCampaignBattleType(string campaignBattleType)
        {
            return string.Equals(
                       campaignBattleType,
                       "FieldBattle",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       campaignBattleType,
                       "Raid",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       campaignBattleType,
                       "IsForcingVolunteers",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       campaignBattleType,
                       "IsForcingSupplies",
                       StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsValidatedScenario(
            BattleScenarioContextMessage scenarioContext,
            string runtimeScene,
            out string diagnostics)
        {
            return IsValidatedScenario(
                ResolveCurrentSnapshot(),
                scenarioContext,
                runtimeScene,
                out diagnostics);
        }

        public static bool IsValidatedPreMissionScenario(
            BattleScenarioContextMessage scenarioContext,
            string runtimeScene,
            out string diagnostics)
        {
            diagnostics = "not-village-battle-scenario";
            if (!IsVillageBattleScenario(scenarioContext))
                return false;

            if (string.IsNullOrWhiteSpace(runtimeScene) ||
                !SceneRuntimeClassifier.IsVillageBattleScene(runtimeScene))
            {
                diagnostics =
                    "village-battle-runtime-scene-not-exact Runtime=" +
                    (runtimeScene ?? string.Empty);
                return false;
            }

            diagnostics =
                "Mode=" + Mode +
                " CampaignBattleType=" +
                (scenarioContext.CampaignBattleType ?? string.Empty) +
                " Scene=" + runtimeScene +
                " Validation=PreMissionTopology";
            return true;
        }

        public static bool IsValidatedScenario(
            BattleSnapshotMessage snapshot,
            string runtimeScene,
            out string diagnostics)
        {
            return IsValidatedScenario(
                snapshot,
                snapshot?.ScenarioContext,
                runtimeScene,
                out diagnostics);
        }

        private static bool IsValidatedScenario(
            BattleSnapshotMessage snapshot,
            BattleScenarioContextMessage scenarioContext,
            string runtimeScene,
            out string diagnostics)
        {
            diagnostics = "not-village-battle-scenario";
            if (!IsVillageBattleScenario(scenarioContext))
                return false;

            if (snapshot == null)
            {
                diagnostics = "village-battle-snapshot-null";
                return false;
            }

            if (!snapshot.IsPlayerMapEvent)
            {
                diagnostics = "village-battle-not-player-map-event";
                return false;
            }

            if (string.IsNullOrWhiteSpace(runtimeScene) ||
                !SceneRuntimeClassifier.IsVillageBattleScene(runtimeScene))
            {
                diagnostics =
                    "village-battle-runtime-scene-not-exact Runtime=" +
                    (runtimeScene ?? string.Empty);
                return false;
            }

            bool sceneMatches =
                SceneNamesMatch(runtimeScene, snapshot.MultiplayerScene) ||
                SceneNamesMatch(runtimeScene, snapshot.MapScene);
            if (!sceneMatches)
            {
                diagnostics =
                    "village-battle-runtime-scene-mismatch Runtime=" + runtimeScene +
                    " Multiplayer=" + (snapshot.MultiplayerScene ?? string.Empty) +
                    " Campaign=" + (snapshot.MapScene ?? string.Empty);
                return false;
            }

            if (snapshot.MapPatchSceneIndex >= 0)
            {
                diagnostics =
                    "village-battle-unexpected-map-patch-scene-index Index=" +
                    snapshot.MapPatchSceneIndex;
                return false;
            }

            if (snapshot.HasPatchEncounterDirection)
            {
                diagnostics = "village-battle-unexpected-encounter-direction";
                return false;
            }

            diagnostics =
                "Mode=" + Mode +
                " CampaignBattleType=" +
                (scenarioContext.CampaignBattleType ?? string.Empty) +
                " Scene=" + runtimeScene +
                " MapPatchSceneIndex=" + snapshot.MapPatchSceneIndex +
                " EncounterDirection=None";
            return true;
        }

        private static BattleSnapshotMessage ResolveCurrentSnapshot()
        {
            return BattleSnapshotRuntimeState.GetCurrent() ??
                   BattleSnapshotRuntimeState.GetState()?.Snapshot;
        }

        private static bool SceneNamesMatch(string sceneName, string candidate)
        {
            return !string.IsNullOrWhiteSpace(sceneName) &&
                   !string.IsNullOrWhiteSpace(candidate) &&
                   string.Equals(
                       sceneName,
                       candidate,
                       StringComparison.OrdinalIgnoreCase);
        }
    }
}
