using System;

namespace CoopSpectator.Infrastructure
{
    /// <summary>
    /// One-shot host-side decision that separates the authoritative wall result
    /// from Bannerlord's otherwise unconditional siege-stage advance.
    /// </summary>
    public static class ExactSiegeStageOutcomeRuntimeState
    {
        private static readonly object Sync = new object();
        private static bool _pending;
        private static string _resultId;
        private static string _battleInstanceId;
        private static string _settlementId;
        private static bool _allowStageAdvance;
        private static bool _defenderPushedBack;
        private static bool _isFinalStage;
        private static int _routedDefenderCount;

        public static void Arm(
            CoopBattleResultBridgeFile.BattleResultSnapshot result,
            string settlementId)
        {
            if (result == null ||
                !string.Equals(result.BattleStage, "SiegeAssault", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(result.WinnerSide, "Attacker", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            lock (Sync)
            {
                _pending = true;
                _resultId = result.ResultId;
                _battleInstanceId = result.BattleInstanceId ?? result.BattleId;
                _settlementId = settlementId ?? string.Empty;
                // CampaignSiegeStateHandler advances the settlement siege state after every
                // attacker victory. DefenderPushedBack only selects whether healthy survivors
                // can continue into the inner/citadel stage; it must not block the state advance
                // that also finalizes a victory when every defender is killed or wounded.
                _allowStageAdvance = true;
                _defenderPushedBack = result.DefenderPushedBack;
                _isFinalStage = result.IsFinalStage;
                _routedDefenderCount = Math.Max(0, result.RoutedDefenderCount);
            }
        }

        public static bool TryConsume(
            string settlementId,
            out bool allowStageAdvance,
            out string diagnostics)
        {
            lock (Sync)
            {
                allowStageAdvance = true;
                diagnostics = string.Empty;
                if (!_pending)
                    return false;

                if (!string.IsNullOrWhiteSpace(_settlementId) &&
                    !string.Equals(_settlementId, settlementId, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                allowStageAdvance = _allowStageAdvance;
                diagnostics =
                    "ResultId=" + (_resultId ?? "null") +
                    " BattleInstanceId=" + (_battleInstanceId ?? "null") +
                    " SettlementId=" + (_settlementId ?? "null") +
                    " DefenderPushedBack=" + _defenderPushedBack +
                    " IsFinalStage=" + _isFinalStage +
                    " RoutedDefenders=" + _routedDefenderCount +
                    " AllowStageAdvance=" + _allowStageAdvance;
                _pending = false;
                return true;
            }
        }
    }
}
