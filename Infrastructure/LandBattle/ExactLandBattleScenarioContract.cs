using System;
using CoopSpectator.Infrastructure.Relief;
using CoopSpectator.Infrastructure.SallyOut;
using CoopSpectator.Infrastructure.VillageBattle;
using CoopSpectator.Network.Messages;

namespace CoopSpectator.Infrastructure
{
    internal static class ExactLandBattleScenarioContract
    {
        public const string FieldBattleScenarioKind = "FieldBattle";
        public const string FieldBattleCampaignBattleType = "FieldBattle";

        public static bool IsLandBattleScenario(BattleScenarioContextMessage scenarioContext)
        {
            return ExactReliefScenarioContract.IsReliefScenario(scenarioContext) ||
                   SallyOutScenarioContract.IsSallyOutScenario(scenarioContext) ||
                   IsVillageBattleScenario(scenarioContext) ||
                   IsFieldBattleScenario(scenarioContext);
        }

        public static bool IsVillageBattleScenario(BattleScenarioContextMessage scenarioContext)
        {
            return ExactVillageBattleScenarioContract.IsVillageBattleScenario(
                scenarioContext);
        }

        public static bool IsFieldBattleScenario(BattleScenarioContextMessage scenarioContext)
        {
            return scenarioContext != null &&
                   !scenarioContext.IsSiegeBattle &&
                   string.Equals(
                       scenarioContext.ScenarioKind,
                       FieldBattleScenarioKind,
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(
                       scenarioContext.CampaignBattleType,
                       FieldBattleCampaignBattleType,
                       StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsValidatedFieldBattleScenario(
            BattleScenarioContextMessage scenarioContext,
            string runtimeScene,
            out string diagnostics)
        {
            return IsValidatedFieldBattleScenario(
                ResolveCurrentSnapshot(),
                scenarioContext,
                runtimeScene,
                out diagnostics);
        }

        public static bool IsValidatedPreMissionFieldBattleScenario(
            BattleScenarioContextMessage scenarioContext,
            string runtimeScene,
            out string diagnostics)
        {
            diagnostics = "not-field-battle-scenario";
            if (!IsFieldBattleScenario(scenarioContext))
                return false;

            if (string.IsNullOrWhiteSpace(runtimeScene) ||
                !SceneRuntimeClassifier.IsCampaignBattleScene(runtimeScene))
            {
                diagnostics =
                    "field-battle-runtime-scene-not-exact Runtime=" +
                    (runtimeScene ?? string.Empty);
                return false;
            }

            diagnostics =
                "Mode=FieldBattle" +
                " CampaignBattleType=" +
                (scenarioContext.CampaignBattleType ?? string.Empty) +
                " Scene=" + runtimeScene +
                " Validation=PreMissionTopology";
            return true;
        }

        public static bool IsValidatedScenario(
            BattleScenarioContextMessage scenarioContext,
            string runtimeScene,
            out string diagnostics)
        {
            if (ExactReliefScenarioContract.IsReliefScenario(scenarioContext))
            {
                return ExactReliefScenarioContract.IsValidatedScenario(
                    scenarioContext,
                    runtimeScene,
                    out diagnostics);
            }

            if (SallyOutScenarioContract.IsSallyOutScenario(scenarioContext))
            {
                bool validated = SallyOutScenarioContract.IsValidatedScenario(
                    scenarioContext,
                    runtimeScene,
                    out diagnostics);
                if (validated)
                    diagnostics = "Mode=SallyOut " + diagnostics;
                return validated;
            }

            if (IsVillageBattleScenario(scenarioContext))
            {
                return ExactVillageBattleScenarioContract.IsValidatedScenario(
                    scenarioContext,
                    runtimeScene,
                    out diagnostics);
            }

            return IsValidatedFieldBattleScenario(
                ResolveCurrentSnapshot(),
                scenarioContext,
                runtimeScene,
                out diagnostics);
        }

        public static bool IsValidatedScenario(
            BattleSnapshotMessage snapshot,
            string runtimeScene,
            out string diagnostics)
        {
            BattleScenarioContextMessage scenarioContext = snapshot?.ScenarioContext;
            if (ExactReliefScenarioContract.IsReliefScenario(scenarioContext))
            {
                return ExactReliefScenarioContract.IsValidatedScenario(
                    scenarioContext,
                    runtimeScene,
                    out diagnostics);
            }

            if (SallyOutScenarioContract.IsSallyOutScenario(scenarioContext))
            {
                bool validated = SallyOutScenarioContract.IsValidatedScenario(
                    scenarioContext,
                    runtimeScene,
                    out diagnostics);
                if (validated)
                    diagnostics = "Mode=SallyOut " + diagnostics;
                return validated;
            }

            if (IsVillageBattleScenario(scenarioContext))
            {
                return ExactVillageBattleScenarioContract.IsValidatedScenario(
                    snapshot,
                    runtimeScene,
                    out diagnostics);
            }

            return IsValidatedFieldBattleScenario(
                snapshot,
                scenarioContext,
                runtimeScene,
                out diagnostics);
        }

        private static bool IsValidatedFieldBattleScenario(
            BattleSnapshotMessage snapshot,
            BattleScenarioContextMessage scenarioContext,
            string runtimeScene,
            out string diagnostics)
        {
            diagnostics = "not-field-battle-scenario";
            if (!IsFieldBattleScenario(scenarioContext))
                return false;

            if (snapshot == null)
            {
                diagnostics = "field-battle-snapshot-null";
                return false;
            }

            if (!snapshot.IsPlayerMapEvent)
            {
                diagnostics = "field-battle-not-player-map-event";
                return false;
            }

            if (string.IsNullOrWhiteSpace(runtimeScene) ||
                !SceneRuntimeClassifier.IsCampaignBattleScene(runtimeScene))
            {
                diagnostics =
                    "field-battle-runtime-scene-not-exact Runtime=" +
                    (runtimeScene ?? string.Empty);
                return false;
            }

            bool sceneMatches =
                SceneNamesMatch(runtimeScene, snapshot.MultiplayerScene) ||
                SceneNamesMatch(runtimeScene, snapshot.MapScene);
            if (!sceneMatches)
            {
                diagnostics =
                    "field-battle-runtime-scene-mismatch Runtime=" + runtimeScene +
                    " Multiplayer=" + (snapshot.MultiplayerScene ?? string.Empty) +
                    " Campaign=" + (snapshot.MapScene ?? string.Empty);
                return false;
            }

            if (snapshot.MapPatchSceneIndex < 0)
            {
                diagnostics = "field-battle-map-patch-scene-index-missing";
                return false;
            }

            if (!snapshot.HasPatchEncounterDirection)
            {
                diagnostics = "field-battle-encounter-direction-missing";
                return false;
            }

            diagnostics =
                "Mode=FieldBattle Scene=" + runtimeScene +
                " MapPatchSceneIndex=" + snapshot.MapPatchSceneIndex +
                " EncounterDirection=" + snapshot.PatchEncounterDirX.ToString("0.###") +
                "," + snapshot.PatchEncounterDirY.ToString("0.###");
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
                   string.Equals(sceneName, candidate, StringComparison.OrdinalIgnoreCase);
        }
    }
}
