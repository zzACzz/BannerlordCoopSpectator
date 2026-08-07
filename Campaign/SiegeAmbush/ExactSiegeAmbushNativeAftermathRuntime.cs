using System;
using CoopSpectator.Campaign.LandBattle;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.SiegeAmbush;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.Core;

namespace CoopSpectator.Campaign.SiegeAmbush
{
    internal static class ExactSiegeAmbushNativeAftermathRuntime
    {
        private static readonly object Sync = new object();
        private static MapEvent _pendingBattle;
        private static string _pendingResultId;
        private static ExactLandBattleNativeAftermathBridge.Preparation _pendingPreparation;
        private static BattleSideEnum _pendingWinnerSide;
        private static BattleState _previousBattleState;
        private static bool _winnerStatePrepared;
        private static bool _winnerStateChanged;
        private static string _lastCommittedResultId;

        internal static bool IsDecisiveResult(
            MapEvent battle,
            CoopBattleResultBridgeFile.BattleResultSnapshot result)
        {
            return battle?.IsSiegeAmbush == true &&
                   battle.IsPlayerMapEvent &&
                   result?.IsFinalStage == true &&
                   !result.DefenderPushedBack &&
                   SiegeAmbushScenarioContract.IsSiegeAmbushResult(result) &&
                   TryResolveWinnerSide(result.WinnerSide, out _);
        }

        internal static bool TryArm(
            MapEvent battle,
            CoopBattleResultBridgeFile.BattleResultSnapshot result,
            out string diagnostics)
        {
            diagnostics = "not-decisive-siege-ambush-result";
            if (!IsDecisiveResult(battle, result) ||
                !TryResolveWinnerSide(result.WinnerSide, out BattleSideEnum winnerSide))
            {
                return false;
            }

            string resultId = ResolveResultId(result);
            lock (Sync)
            {
                if (string.Equals(
                        _lastCommittedResultId,
                        resultId,
                        StringComparison.Ordinal))
                {
                    diagnostics = "already-committed ResultId=" + resultId;
                    return true;
                }

                if (ReferenceEquals(_pendingBattle, battle) &&
                    string.Equals(
                        _pendingResultId,
                        resultId,
                        StringComparison.Ordinal) &&
                    _pendingPreparation != null &&
                    _pendingWinnerSide == winnerSide)
                {
                    diagnostics =
                        "already-armed ResultId=" + resultId +
                        " Winner=" + winnerSide +
                        " NativeAftermath={" + _pendingPreparation.Diagnostics + "}";
                    return true;
                }

                ClearPendingNoLock(rollback: true);

                if (!ExactLandBattleNativeAftermathBridge.TryPrepareCasualtyLedgers(
                        battle,
                        result,
                        result.Entries,
                        out ExactLandBattleNativeAftermathBridge.Preparation preparation,
                        out string preparationDiagnostics))
                {
                    diagnostics =
                        "native-aftermath-prepare-failed ResultId=" + resultId +
                        " Diagnostics={" + preparationDiagnostics + "}";
                    return false;
                }

                _pendingBattle = battle;
                _pendingResultId = resultId;
                _pendingPreparation = preparation;
                _pendingWinnerSide = winnerSide;
                diagnostics =
                    "armed ResultId=" + resultId +
                    " Winner=" + winnerSide +
                    " WinnerStateDeferred=True" +
                    " NativeAftermath={" + preparation.Diagnostics + "}";
                return true;
            }
        }

        internal static bool TryBeginNativeCalculation(
            MapEvent battle,
            out string diagnostics)
        {
            diagnostics = "not-pending";
            lock (Sync)
            {
                if (_pendingBattle == null ||
                    _pendingPreparation == null ||
                    _pendingWinnerSide == BattleSideEnum.None)
                {
                    return false;
                }

                if (!ReferenceEquals(_pendingBattle, battle))
                {
                    diagnostics = "pending-battle-mismatch";
                    return false;
                }

                if (_winnerStatePrepared)
                {
                    diagnostics =
                        "already-prepared ResultId=" + _pendingResultId +
                        " Winner=" + _pendingWinnerSide +
                        " Changed=" + _winnerStateChanged;
                    return true;
                }

                _previousBattleState = battle.BattleState;
                BattleState targetBattleState =
                    ToVictoryBattleState(_pendingWinnerSide);
                if (targetBattleState == BattleState.None)
                {
                    diagnostics = "winner-state-unresolved";
                    return false;
                }

                if (_previousBattleState != targetBattleState)
                {
                    battle.SetOverrideWinner(_pendingWinnerSide);
                    _winnerStateChanged = true;
                }

                _winnerStatePrepared = true;
                diagnostics =
                    "prepared ResultId=" + _pendingResultId +
                    " PreviousState=" + _previousBattleState +
                    " TargetState=" + targetBattleState +
                    " Changed=" + _winnerStateChanged;
                return true;
            }
        }

        internal static bool TryCommit(
            MapEvent battle,
            out string diagnostics)
        {
            diagnostics = "not-pending";
            lock (Sync)
            {
                if (_pendingBattle == null || _pendingPreparation == null)
                    return false;

                if (!ReferenceEquals(_pendingBattle, battle))
                {
                    diagnostics = "pending-battle-mismatch";
                    return false;
                }

                if (!_winnerStatePrepared)
                {
                    diagnostics = "winner-state-not-prepared";
                    return false;
                }

                string resultId = _pendingResultId;
                string preparationDiagnostics = _pendingPreparation.Diagnostics;
                BattleSideEnum winnerSide = _pendingWinnerSide;
                _pendingPreparation.Commit();
                _lastCommittedResultId = resultId;
                ClearPendingNoLock(rollback: false);
                diagnostics =
                    "committed ResultId=" + resultId +
                    " Winner=" + winnerSide +
                    " NativeAftermath={" + preparationDiagnostics + "}";
                return true;
            }
        }

        internal static bool TryRollback(
            MapEvent battle,
            string resultId,
            out string diagnostics)
        {
            diagnostics = "not-pending";
            lock (Sync)
            {
                if (_pendingBattle == null || _pendingPreparation == null)
                    return false;

                if (!ReferenceEquals(_pendingBattle, battle) ||
                    (!string.IsNullOrWhiteSpace(resultId) &&
                     !string.Equals(
                         _pendingResultId,
                         resultId,
                         StringComparison.Ordinal)))
                {
                    diagnostics = "pending-result-mismatch";
                    return false;
                }

                string pendingResultId = _pendingResultId;
                BattleState previousBattleState = _previousBattleState;
                bool winnerStateChanged = _winnerStateChanged;
                ClearPendingNoLock(rollback: true);
                diagnostics =
                    "rolled-back ResultId=" + pendingResultId +
                    " WinnerStateChanged=" + winnerStateChanged +
                    " RestoredState=" + previousBattleState;
                return true;
            }
        }

        private static void ClearPendingNoLock(bool rollback)
        {
            if (rollback)
            {
                _pendingPreparation?.Rollback();
                RestorePreviousBattleStateNoLock();
            }

            _pendingBattle = null;
            _pendingResultId = null;
            _pendingPreparation = null;
            _pendingWinnerSide = BattleSideEnum.None;
            _previousBattleState = BattleState.None;
            _winnerStatePrepared = false;
            _winnerStateChanged = false;
        }

        private static void RestorePreviousBattleStateNoLock()
        {
            if (!_winnerStatePrepared || !_winnerStateChanged || _pendingBattle == null)
                return;

            switch (_previousBattleState)
            {
                case BattleState.AttackerVictory:
                    _pendingBattle.SetOverrideWinner(BattleSideEnum.Attacker);
                    break;
                case BattleState.DefenderVictory:
                    _pendingBattle.SetOverrideWinner(BattleSideEnum.Defender);
                    break;
                case BattleState.DefenderPullBack:
                    _pendingBattle.SetDefenderPulledBack();
                    break;
                default:
                    _pendingBattle.SetOverrideWinner(BattleSideEnum.None);
                    break;
            }
        }

        private static bool TryResolveWinnerSide(
            string winnerSide,
            out BattleSideEnum battleSide)
        {
            if (string.Equals(
                    winnerSide,
                    nameof(BattleSideEnum.Attacker),
                    StringComparison.OrdinalIgnoreCase))
            {
                battleSide = BattleSideEnum.Attacker;
                return true;
            }

            if (string.Equals(
                    winnerSide,
                    nameof(BattleSideEnum.Defender),
                    StringComparison.OrdinalIgnoreCase))
            {
                battleSide = BattleSideEnum.Defender;
                return true;
            }

            battleSide = BattleSideEnum.None;
            return false;
        }

        private static BattleState ToVictoryBattleState(BattleSideEnum winnerSide)
        {
            if (winnerSide == BattleSideEnum.Attacker)
                return BattleState.AttackerVictory;
            if (winnerSide == BattleSideEnum.Defender)
                return BattleState.DefenderVictory;
            return BattleState.None;
        }

        private static string ResolveResultId(
            CoopBattleResultBridgeFile.BattleResultSnapshot result)
        {
            if (!string.IsNullOrWhiteSpace(result?.ResultId))
                return result.ResultId;
            if (!string.IsNullOrWhiteSpace(result?.BattleInstanceId))
                return result.BattleInstanceId;
            return (result?.BattleId ?? "null") + "|" +
                   result?.UpdatedUtc.ToString("O");
        }
    }
}
