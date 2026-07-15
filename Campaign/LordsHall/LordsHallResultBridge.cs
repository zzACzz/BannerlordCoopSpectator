using System;
using CoopSpectator.Infrastructure;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Settlements;

namespace CoopSpectator.Campaign.LordsHall
{
    internal static class LordsHallResultBridge
    {
        public static bool IsLordsHallResult(CoopBattleResultBridgeFile.BattleResultSnapshot result)
        {
            return string.Equals(result?.BattleStage, "LordsHall", StringComparison.OrdinalIgnoreCase);
        }

        public static bool ShouldIncludeInFinalSiegeDefenderPreview(
            CoopBattleResultBridgeFile.BattleResultSnapshot result)
        {
            if (result == null ||
                result.DefenderPushedBack ||
                !string.Equals(result.WinnerSide, "Attacker", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.Equals(result.BattleStage, "SiegeAssault", StringComparison.OrdinalIgnoreCase) ||
                   IsLordsHallResult(result);
        }

        public static bool ShouldPreservePendingStage(
            Settlement settlement,
            MapEvent battle,
            string pendingSettlementId)
        {
            return !string.IsNullOrWhiteSpace(pendingSettlementId) &&
                   settlement?.IsFortification == true &&
                   string.Equals(settlement.StringId, pendingSettlementId, StringComparison.OrdinalIgnoreCase) &&
                   battle?.IsSiegeAssault == true;
        }
    }
}
