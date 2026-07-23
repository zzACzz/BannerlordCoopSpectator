using System;
using System.Collections.Generic;
using CoopSpectator.Campaign.Relief;
using CoopSpectator.Campaign.SallyOut;
using CoopSpectator.Campaign.VillageBattle;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.Relief;
using CoopSpectator.Infrastructure.SallyOut;
using CoopSpectator.Infrastructure.VillageBattle;
using CoopSpectator.Network.Messages;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.Core;

namespace CoopSpectator.Campaign.LandBattle
{
    internal static class ExactLandBattleCampaignBattleAdapter
    {
        private const string FieldBattleResultStage = "Battle";

        private static readonly object FinalEncounterCompletionSync = new object();
        private static MapEvent _pendingFinalEncounterBattle;
        private static string _pendingFinalEncounterResultId;
        private static string _pendingFinalEncounterWinnerSide;
        private static string _pendingFinalEncounterMode;
        private static string _lastConsumedFinalEncounterResultId;
        private static readonly Dictionary<Hero, int> PendingFinalEncounterHeroHitPoints =
            new Dictionary<Hero, int>();

        public static bool IsFinalEncounterResult(
            MapEvent battle,
            CoopBattleResultBridgeFile.BattleResultSnapshot result)
        {
            return TryValidateFinalEncounterResult(battle, result, out _, out _);
        }

        public static bool TryArmFinalEncounterCompletion(
            MapEvent battle,
            CoopBattleResultBridgeFile.BattleResultSnapshot result,
            IEnumerable<KeyValuePair<Hero, int>> heroHitPoints,
            out string diagnostics)
        {
            diagnostics = "contract-not-armed";
            if (!TryValidateFinalEncounterResult(
                    battle,
                    result,
                    out string mode,
                    out string validationDiagnostics))
            {
                diagnostics = validationDiagnostics;
                return false;
            }

            string resultId = ResolveResultId(result);
            lock (FinalEncounterCompletionSync)
            {
                _lastConsumedFinalEncounterResultId = null;
                ClearFinalEncounterCompletionNoLock();
                _pendingFinalEncounterBattle = battle;
                _pendingFinalEncounterResultId = resultId;
                _pendingFinalEncounterWinnerSide = result.WinnerSide;
                _pendingFinalEncounterMode = mode;

                if (heroHitPoints != null)
                {
                    foreach (KeyValuePair<Hero, int> pair in heroHitPoints)
                    {
                        if (pair.Key == null || !pair.Key.IsAlive)
                            continue;

                        PendingFinalEncounterHeroHitPoints[pair.Key] = Math.Max(1, pair.Value);
                    }
                }

                diagnostics =
                    "armed Mode=" + mode +
                    " ResultId=" + resultId +
                    " WinnerSide=" + result.WinnerSide +
                    " CachedHeroHp=" + PendingFinalEncounterHeroHitPoints.Count;
                return true;
            }
        }

        public static bool TryConsumeFinalEncounterCompletion(
            MapEvent battle,
            out string diagnostics)
        {
            diagnostics = "contract-not-pending";
            Dictionary<Hero, int> heroHitPoints;
            string resultId;
            string winnerSide;
            string mode;

            lock (FinalEncounterCompletionSync)
            {
                if (_pendingFinalEncounterBattle == null)
                    return false;

                if (!ReferenceEquals(_pendingFinalEncounterBattle, battle))
                {
                    ClearFinalEncounterCompletionNoLock();
                    diagnostics = "contract-battle-mismatch";
                    return false;
                }

                resultId = _pendingFinalEncounterResultId;
                winnerSide = _pendingFinalEncounterWinnerSide;
                mode = _pendingFinalEncounterMode;
                heroHitPoints = new Dictionary<Hero, int>(PendingFinalEncounterHeroHitPoints);
                ClearFinalEncounterCompletionNoLock();
            }

            int reappliedHeroHitPoints = 0;
            int skippedDeadHeroes = 0;
            var reappliedSamples = new List<string>();
            foreach (KeyValuePair<Hero, int> pair in heroHitPoints)
            {
                Hero hero = pair.Key;
                if (hero == null || !hero.IsAlive)
                {
                    skippedDeadHeroes++;
                    continue;
                }

                int desiredHitPoints = Math.Max(1, pair.Value);
                int currentHitPoints = hero.HitPoints;
                if (currentHitPoints == desiredHitPoints)
                    continue;

                hero.HitPoints = desiredHitPoints;
                reappliedHeroHitPoints++;
                if (reappliedSamples.Count < 8)
                {
                    reappliedSamples.Add(
                        (hero.StringId ?? "unknown-hero") +
                        ":" + currentHitPoints + "->" + desiredHitPoints);
                }
            }

            diagnostics =
                "Mode=" + (mode ?? "unknown") +
                " ResultId=" + resultId +
                " WinnerSide=" + winnerSide +
                " CachedHeroHp=" + heroHitPoints.Count +
                " ReappliedHeroHp=" + reappliedHeroHitPoints +
                " SkippedDeadHeroes=" + skippedDeadHeroes +
                " ReappliedSamples=[" + string.Join("; ", reappliedSamples) + "]";
            lock (FinalEncounterCompletionSync)
            {
                _lastConsumedFinalEncounterResultId = resultId;
            }
            return true;
        }

        public static bool WasFinalEncounterCompletionConsumed(string resultId)
        {
            if (string.IsNullOrWhiteSpace(resultId))
                return false;

            lock (FinalEncounterCompletionSync)
            {
                return string.Equals(
                    _lastConsumedFinalEncounterResultId,
                    resultId,
                    StringComparison.Ordinal);
            }
        }

        private static bool TryValidateFinalEncounterResult(
            MapEvent battle,
            CoopBattleResultBridgeFile.BattleResultSnapshot result,
            out string mode,
            out string diagnostics)
        {
            mode = string.Empty;
            diagnostics = "not-exact-land-battle";
            if (battle == null || battle.PlayerSide == BattleSideEnum.None)
            {
                diagnostics = "campaign-battle-invalid";
                return false;
            }

            if (result?.IsFinalStage != true || result.DefenderPushedBack)
            {
                diagnostics = "not-final-land-battle-result";
                return false;
            }

            if (!IsResolvedWinner(result.WinnerSide))
            {
                diagnostics = "winner-unresolved";
                return false;
            }

            if (SallyOutCampaignBattleAdapter.IsCampaignBattle(battle))
            {
                if (!string.Equals(
                        result.BattleStage,
                        SallyOutScenarioContract.ResultStage,
                        StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics = "sally-out-result-stage-mismatch";
                    return false;
                }

                mode = SallyOutScenarioContract.ResultStage;
                diagnostics = "validated-final-sally-out";
                return true;
            }

            BattleSnapshotMessage snapshot =
                BattleSnapshotRuntimeState.GetCurrent() ??
                BattleSnapshotRuntimeState.GetState()?.Snapshot;
            if (ExactReliefCampaignBattleAdapter.IsCampaignBattle(battle))
            {
                if (!ExactReliefCampaignBattleAdapter
                        .TryValidateFinalEncounterResult(
                            battle,
                            snapshot,
                            result,
                            out diagnostics))
                {
                    return false;
                }

                mode = ExactReliefScenarioContract.ResultStage;
                return true;
            }

            if (ExactVillageBattleCampaignBattleAdapter.IsCampaignBattle(battle))
            {
                if (!ExactVillageBattleCampaignBattleAdapter
                        .TryValidateFinalEncounterResult(
                            battle,
                            snapshot,
                            result,
                            out diagnostics))
                {
                    return false;
                }

                mode = ExactVillageBattleScenarioContract.Mode;
                return true;
            }

            if (battle.IsFieldBattle != true ||
                !ExactLandBattleScenarioContract.IsFieldBattleScenario(snapshot?.ScenarioContext))
            {
                diagnostics = "not-exact-field-battle";
                return false;
            }

            if (!string.Equals(
                    result.BattleStage,
                    FieldBattleResultStage,
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostics = "field-battle-result-stage-mismatch";
                return false;
            }

            mode = "FieldBattle";
            diagnostics = "validated-final-field-battle";
            return true;
        }

        private static bool IsResolvedWinner(string winnerSide)
        {
            return string.Equals(winnerSide, "Attacker", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(winnerSide, "Defender", StringComparison.OrdinalIgnoreCase);
        }

        private static string ResolveResultId(CoopBattleResultBridgeFile.BattleResultSnapshot result)
        {
            if (!string.IsNullOrWhiteSpace(result?.ResultId))
                return result.ResultId;
            if (!string.IsNullOrWhiteSpace(result?.BattleInstanceId))
                return result.BattleInstanceId;
            return (result?.BattleId ?? "null") + "|" + result?.UpdatedUtc.ToString("O");
        }

        private static void ClearFinalEncounterCompletionNoLock()
        {
            _pendingFinalEncounterBattle = null;
            _pendingFinalEncounterResultId = null;
            _pendingFinalEncounterWinnerSide = null;
            _pendingFinalEncounterMode = null;
            PendingFinalEncounterHeroHitPoints.Clear();
        }
    }
}
