using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.Relief;
using CoopSpectator.Network.Messages;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Source.Missions;

namespace CoopSpectator.Campaign.Relief
{
    internal static class ExactReliefCampaignBattleAdapter
    {
        private static readonly FieldInfo MissionInitializerRecordBackingField =
            typeof(Mission).GetField(
                "<InitializerRecord>k__BackingField",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly PropertyInfo MissionInitializerRecordProperty =
            typeof(Mission).GetProperty(
                "InitializerRecord",
                BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool IsCampaignBattle(MapEvent battle)
        {
            return battle?.IsSiegeOutside == true &&
                   battle.IsPlayerMapEvent &&
                   battle.PlayerSide != BattleSideEnum.None;
        }

        public static bool IsCampaignStage(
            MapEvent battle,
            Settlement settlement)
        {
            return IsCampaignBattle(battle) &&
                   settlement?.IsFortification == true &&
                   settlement.SiegeEvent != null;
        }

        public static bool TryValidateActiveMission(
            MapEvent battle,
            Settlement settlement,
            Mission mission,
            out string expectedScene,
            out string diagnostics)
        {
            expectedScene = string.Empty;
            diagnostics = "not-relief-campaign-battle";
            if (!IsCampaignBattle(battle))
                return false;

            if (!IsCampaignStage(battle, settlement))
            {
                diagnostics =
                    "campaign-stage-invalid Settlement=" +
                    (settlement?.StringId ?? "null") +
                    " IsFortification=" +
                    (settlement?.IsFortification ?? false) +
                    " HasSiegeEvent=" +
                    (settlement?.SiegeEvent != null) +
                    " PlayerSide=" +
                    (battle?.PlayerSide.ToString() ?? "None");
                return false;
            }

            if (mission == null)
            {
                diagnostics = "mission-null";
                return false;
            }

            if (!TryGetMissionInitializerRecord(
                    mission,
                    out MissionInitializerRecord initializerRecord))
            {
                diagnostics = "mission-initializer-missing";
                return false;
            }

            expectedScene = initializerRecord.SceneName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(expectedScene))
                expectedScene = mission.SceneName ?? string.Empty;

            if (string.IsNullOrWhiteSpace(expectedScene) ||
                !string.Equals(
                    mission.SceneName,
                    expectedScene,
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostics =
                    "scene-mismatch Runtime=" +
                    (mission.SceneName ?? "null") +
                    " Expected=" +
                    (expectedScene ?? "null");
                return false;
            }

            if (initializerRecord.SceneHasMapPatch)
            {
                diagnostics = "mission-initializer-map-patch-enabled";
                return false;
            }

            if (!CampaignMissionShellRuntimeState.TryGetMissionShell(
                    expectedScene,
                    out string missionShell,
                    out string missionShellDiagnostics) ||
                !CampaignMissionShellRuntimeState
                    .IsWithDeploymentMissionShell(missionShell))
            {
                diagnostics =
                    "mission-shell-unsupported {" +
                    missionShellDiagnostics + "}";
                return false;
            }

            if (mission.GetMissionBehavior<BattleSpawnLogic>() == null)
            {
                diagnostics = "native-battle-spawn-logic-missing";
                return false;
            }

            if (mission.GetMissionBehavior<SallyOutMissionController>() == null)
            {
                diagnostics = "native-relief-controller-missing";
                return false;
            }

            diagnostics =
                "validated Settlement=" + settlement.StringId +
                " Scene=" + expectedScene +
                " PlayerSide=" + battle.PlayerSide +
                " MissionShell=" + missionShell +
                " SceneHasMapPatch=False";
            return true;
        }

        public static bool TryValidateFinalEncounterResult(
            MapEvent battle,
            BattleSnapshotMessage snapshot,
            CoopBattleResultBridgeFile.BattleResultSnapshot result,
            out string diagnostics)
        {
            diagnostics = "not-exact-relief";
            Settlement settlement = battle?.MapEventSettlement;
            if (!IsCampaignStage(battle, settlement))
                return false;

            BattleScenarioContextMessage scenarioContext =
                snapshot?.ScenarioContext;
            if (!string.Equals(
                    scenarioContext?.SiegeContext?.SettlementId,
                    settlement.StringId,
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostics =
                    "relief-settlement-mismatch Snapshot=" +
                    (scenarioContext?.SiegeContext?.SettlementId ??
                     string.Empty) +
                    " Live=" + settlement.StringId;
                return false;
            }

            string runtimeScene =
                result?.MapScene ??
                snapshot?.MultiplayerScene ??
                snapshot?.MapScene;
            if (!ExactReliefScenarioContract.IsValidatedScenario(
                    scenarioContext,
                    runtimeScene,
                    out string scenarioDiagnostics))
            {
                diagnostics =
                    "relief-scenario-invalid {" +
                    scenarioDiagnostics + "}";
                return false;
            }

            if (!ExactReliefScenarioContract.IsReliefResult(result))
            {
                diagnostics =
                    "relief-result-stage-mismatch Stage=" +
                    (result?.BattleStage ?? string.Empty);
                return false;
            }

            diagnostics =
                "validated-final-relief" +
                " Settlement=" + settlement.StringId +
                " Scenario={" + scenarioDiagnostics + "}";
            return true;
        }

        private static bool TryGetMissionInitializerRecord(
            Mission mission,
            out MissionInitializerRecord initializerRecord)
        {
            initializerRecord = default;
            if (mission == null)
                return false;

            try
            {
                object boxedRecord =
                    MissionInitializerRecordProperty?.GetValue(mission, null) ??
                    MissionInitializerRecordBackingField?.GetValue(mission);
                if (boxedRecord is MissionInitializerRecord record)
                {
                    initializerRecord = record;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }
    }
}
