using System;
using CoopSpectator.Campaign.LandBattle;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.Hideout;
using TaleWorlds.CampaignSystem.MapEvents;

namespace CoopSpectator.Campaign.Hideout
{
    /// <summary>
    /// Arms the native map-event casualty ledgers for a completed cooperative
    /// hideout assault. The ledgers are committed only after Bannerlord's own
    /// CalculateAndCommitMapEventResults path succeeds.
    /// </summary>
    internal static class ExactHideoutNativeAftermathRuntime
    {
        private static readonly object Sync = new object();
        private static MapEvent _pendingBattle;
        private static string _pendingResultId;
        private static ExactLandBattleNativeAftermathBridge.Preparation _pendingPreparation;
        private static ExactLandBattleNativeAftermathBridge.Preparation _pendingEffectiveRosterPreparation;
        private static string _lastCommittedResultId;

        internal static bool IsFinalHideoutResult(
            MapEvent battle,
            CoopBattleResultBridgeFile.BattleResultSnapshot result)
        {
            return battle?.IsHideoutBattle == true &&
                   battle.IsPlayerMapEvent &&
                   result?.IsFinalStage == true &&
                   !result.DefenderPushedBack &&
                   CoopHideoutBossPhaseContract.IsSupportedDayHideoutSceneName(result.MapScene) &&
                   IsResolvedWinner(result.WinnerSide);
        }

        internal static bool TryArm(
            MapEvent battle,
            CoopBattleResultBridgeFile.BattleResultSnapshot result,
            out string diagnostics)
        {
            diagnostics = "not-final-hideout-result";
            if (!IsFinalHideoutResult(battle, result))
                return false;

            string resultId = ResolveResultId(result);
            lock (Sync)
            {
                if (string.Equals(_lastCommittedResultId, resultId, StringComparison.Ordinal))
                {
                    diagnostics = "already-committed ResultId=" + resultId;
                    return true;
                }

                if (ReferenceEquals(_pendingBattle, battle) &&
                    string.Equals(_pendingResultId, resultId, StringComparison.Ordinal) &&
                    _pendingPreparation != null &&
                    _pendingEffectiveRosterPreparation != null)
                {
                    diagnostics =
                        "already-armed ResultId=" + resultId +
                        " NativeAftermath={" + _pendingPreparation.Diagnostics + "}" +
                        " EffectiveRoster={" + _pendingEffectiveRosterPreparation.Diagnostics + "}";
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

                if (!ExactLandBattleNativeAftermathBridge.TryPrepareEffectiveDefeatedMemberRoster(
                        battle,
                        result,
                        out ExactLandBattleNativeAftermathBridge.Preparation effectiveRosterPreparation,
                        out string effectiveRosterDiagnostics))
                {
                    preparation.Rollback();
                    diagnostics =
                        "effective-roster-prepare-failed ResultId=" + resultId +
                        " Diagnostics={" + effectiveRosterDiagnostics + "}";
                    return false;
                }

                _pendingBattle = battle;
                _pendingResultId = resultId;
                _pendingPreparation = preparation;
                _pendingEffectiveRosterPreparation = effectiveRosterPreparation;
                diagnostics =
                    "armed ResultId=" + resultId +
                    " Entries=" + (result.Entries?.Count ?? 0) +
                    " NativeAftermath={" + preparation.Diagnostics + "}" +
                    " EffectiveRoster={" + effectiveRosterPreparation.Diagnostics + "}";
                return true;
            }
        }

        internal static bool TryCommit(MapEvent battle, out string diagnostics)
        {
            diagnostics = "not-pending";
            lock (Sync)
            {
                if (_pendingBattle == null ||
                    _pendingPreparation == null ||
                    _pendingEffectiveRosterPreparation == null)
                    return false;
                if (!ReferenceEquals(_pendingBattle, battle))
                {
                    diagnostics = "pending-battle-mismatch";
                    return false;
                }

                string resultId = _pendingResultId;
                string preparationDiagnostics = _pendingPreparation.Diagnostics;
                string effectiveRosterDiagnostics = _pendingEffectiveRosterPreparation.Diagnostics;
                _pendingPreparation.Commit();
                _pendingEffectiveRosterPreparation.Commit();
                _lastCommittedResultId = resultId;
                ClearPendingNoLock(rollback: false);
                diagnostics =
                    "committed ResultId=" + resultId +
                    " NativeAftermath={" + preparationDiagnostics + "}" +
                    " EffectiveRoster={" + effectiveRosterDiagnostics + "}";
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
                if (_pendingBattle == null ||
                    _pendingPreparation == null ||
                    _pendingEffectiveRosterPreparation == null)
                    return false;
                if (!ReferenceEquals(_pendingBattle, battle) ||
                    (!string.IsNullOrWhiteSpace(resultId) &&
                     !string.Equals(_pendingResultId, resultId, StringComparison.Ordinal)))
                {
                    diagnostics = "pending-result-mismatch";
                    return false;
                }

                string pendingResultId = _pendingResultId;
                _pendingEffectiveRosterPreparation.Rollback();
                _pendingPreparation.Rollback();
                ClearPendingNoLock(rollback: false);
                diagnostics = "rolled-back ResultId=" + pendingResultId;
                return true;
            }
        }

        private static void ClearPendingNoLock(bool rollback)
        {
            if (rollback)
            {
                _pendingEffectiveRosterPreparation?.Rollback();
                _pendingPreparation?.Rollback();
            }

            _pendingBattle = null;
            _pendingResultId = null;
            _pendingPreparation = null;
            _pendingEffectiveRosterPreparation = null;
        }

        private static bool IsResolvedWinner(string winnerSide)
        {
            return string.Equals(winnerSide, "Attacker", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(winnerSide, "Defender", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveResultId(
            CoopBattleResultBridgeFile.BattleResultSnapshot result)
        {
            if (!string.IsNullOrWhiteSpace(result?.ResultId))
                return result.ResultId;
            if (!string.IsNullOrWhiteSpace(result?.BattleInstanceId))
                return result.BattleInstanceId;
            return (result?.BattleId ?? "null") + "|" + result?.UpdatedUtc.ToString("O");
        }
    }
}
