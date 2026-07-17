using System;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.VillageBattle;
using CoopSpectator.Network.Messages;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.Core;

namespace CoopSpectator.Campaign.VillageBattle
{
    internal static class ExactVillageBattleCampaignBattleAdapter
    {
        public static bool IsCampaignBattle(MapEvent battle)
        {
            return battle != null &&
                   battle.IsPlayerMapEvent &&
                   battle.PlayerSide != BattleSideEnum.None &&
                   !battle.IsNavalMapEvent &&
                   battle.MapEventSettlement?.IsVillage == true &&
                   ExactVillageBattleScenarioContract.IsSupportedCampaignBattleType(
                       battle.EventType.ToString());
        }

        public static bool TryValidateFinalEncounterResult(
            MapEvent battle,
            BattleSnapshotMessage snapshot,
            CoopBattleResultBridgeFile.BattleResultSnapshot result,
            out string diagnostics)
        {
            diagnostics = "not-exact-village-battle";
            if (!IsCampaignBattle(battle))
                return false;

            BattleScenarioContextMessage scenarioContext = snapshot?.ScenarioContext;
            string campaignBattleType = battle.EventType.ToString();
            if (!string.Equals(
                    scenarioContext?.CampaignBattleType,
                    campaignBattleType,
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostics =
                    "village-battle-campaign-type-mismatch Snapshot=" +
                    (scenarioContext?.CampaignBattleType ?? string.Empty) +
                    " Live=" + campaignBattleType;
                return false;
            }

            string runtimeScene =
                result?.MapScene ??
                snapshot?.MultiplayerScene ??
                snapshot?.MapScene;
            if (!ExactVillageBattleScenarioContract.IsValidatedScenario(
                    snapshot,
                    runtimeScene,
                    out string scenarioDiagnostics))
            {
                diagnostics =
                    "village-battle-scenario-invalid {" +
                    scenarioDiagnostics + "}";
                return false;
            }

            if (!string.Equals(
                    result?.BattleStage,
                    ExactVillageBattleScenarioContract.ResultStage,
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostics =
                    "village-battle-result-stage-mismatch Stage=" +
                    (result?.BattleStage ?? string.Empty);
                return false;
            }

            diagnostics =
                "validated-final-village-battle " +
                "CampaignBattleType=" + campaignBattleType +
                " Settlement=" +
                (battle.MapEventSettlement?.StringId ?? string.Empty) +
                " Scenario={" + scenarioDiagnostics + "}";
            return true;
        }
    }
}
