using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Campaign.LandBattle;
using CoopSpectator.Campaign.LordsHall;
using CoopSpectator.Infrastructure;
using TaleWorlds.CampaignSystem.MapEvents;

namespace CoopSpectator.Campaign.SiegeAssault
{
    internal static class ExactSiegeAssaultNativeAftermathRuntime
    {
        private const string SiegeAssaultStage = "SiegeAssault";
        private static readonly object Sync = new object();
        private static MapEvent _pendingBattle;
        private static string _pendingResultId;
        private static ExactLandBattleNativeAftermathBridge.Preparation _pendingPreparation;
        private static string _lastCommittedResultId;

        internal static bool IsFinalSiegeResult(
            MapEvent battle,
            CoopBattleResultBridgeFile.BattleResultSnapshot result)
        {
            if (battle?.IsSiegeAssault != true ||
                !battle.IsPlayerMapEvent ||
                result?.IsFinalStage != true ||
                result.DefenderPushedBack ||
                !IsResolvedWinner(result.WinnerSide))
            {
                return false;
            }

            return string.Equals(
                       result.BattleStage,
                       SiegeAssaultStage,
                       StringComparison.OrdinalIgnoreCase) ||
                   LordsHallResultBridge.IsLordsHallResult(result);
        }

        internal static bool TryArm(
            MapEvent battle,
            CoopBattleResultBridgeFile.BattleResultSnapshot result,
            IEnumerable<CoopBattleResultBridgeFile.BattleResultEntrySnapshot> previousStageEntries,
            out string diagnostics)
        {
            diagnostics = "not-final-siege-result";
            if (!IsFinalSiegeResult(battle, result))
                return false;

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
                    _pendingPreparation != null)
                {
                    diagnostics =
                        "already-armed ResultId=" + resultId +
                        " NativeAftermath={" + _pendingPreparation.Diagnostics + "}";
                    return true;
                }

                ClearPendingNoLock(rollback: true);

                List<CoopBattleResultBridgeFile.BattleResultEntrySnapshot> combinedEntries =
                    (previousStageEntries ??
                     Enumerable.Empty<CoopBattleResultBridgeFile.BattleResultEntrySnapshot>())
                    .Where(entry => entry != null)
                    .Concat(
                        result.Entries ??
                        new List<CoopBattleResultBridgeFile.BattleResultEntrySnapshot>())
                    .Where(entry => entry != null)
                    .ToList();
                if (!ExactLandBattleNativeAftermathBridge.TryPrepareCasualtyLedgers(
                        battle,
                        result,
                        combinedEntries,
                        out ExactLandBattleNativeAftermathBridge.Preparation preparation,
                        out string preparationDiagnostics))
                {
                    diagnostics =
                        "native-aftermath-prepare-failed ResultId=" + resultId +
                        " Entries=" + combinedEntries.Count +
                        " Diagnostics={" + preparationDiagnostics + "}";
                    return false;
                }

                _pendingBattle = battle;
                _pendingResultId = resultId;
                _pendingPreparation = preparation;
                diagnostics =
                    "armed ResultId=" + resultId +
                    " PreviousStageEntries=" +
                    (previousStageEntries?.Count(entry => entry != null) ?? 0) +
                    " CurrentStageEntries=" +
                    (result.Entries?.Count(entry => entry != null) ?? 0) +
                    " NativeAftermath={" + preparation.Diagnostics + "}";
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

                string resultId = _pendingResultId;
                string preparationDiagnostics = _pendingPreparation.Diagnostics;
                _pendingPreparation.Commit();
                _lastCommittedResultId = resultId;
                ClearPendingNoLock(rollback: false);
                diagnostics =
                    "committed ResultId=" + resultId +
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
                _pendingPreparation.Rollback();
                ClearPendingNoLock(rollback: false);
                diagnostics = "rolled-back ResultId=" + pendingResultId;
                return true;
            }
        }

        private static void ClearPendingNoLock(bool rollback)
        {
            if (rollback)
                _pendingPreparation?.Rollback();

            _pendingBattle = null;
            _pendingResultId = null;
            _pendingPreparation = null;
        }

        private static bool IsResolvedWinner(string winnerSide)
        {
            return string.Equals(
                       winnerSide,
                       "Attacker",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       winnerSide,
                       "Defender",
                       StringComparison.OrdinalIgnoreCase);
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
