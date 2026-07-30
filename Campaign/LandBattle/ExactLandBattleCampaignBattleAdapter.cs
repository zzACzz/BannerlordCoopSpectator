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
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
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
        private static MapEvent _pendingFinalFieldBattleHeroCaptureBattle;
        private static string _pendingFinalFieldBattleHeroCaptureResultId;
        private static bool _pendingFinalFieldBattleMainPartyIsSoleEligibleCaptor;
        private static readonly List<Hero> PendingFinalFieldBattleHeroCaptureCandidates =
            new List<Hero>();

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
            IEnumerable<Hero> defeatedUnconsciousHeroes,
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
                ClearFinalFieldBattleHeroCaptureNoLock();
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

                if (string.Equals(mode, "FieldBattle", StringComparison.Ordinal) &&
                    IsPlayerWinner(battle, result.WinnerSide))
                {
                    _pendingFinalFieldBattleHeroCaptureBattle = battle;
                    _pendingFinalFieldBattleHeroCaptureResultId = resultId;
                    _pendingFinalFieldBattleMainPartyIsSoleEligibleCaptor =
                        IsMainPartySoleEligibleCaptor(battle);

                    if (defeatedUnconsciousHeroes != null)
                    {
                        foreach (Hero hero in defeatedUnconsciousHeroes)
                        {
                            if (hero == null || PendingFinalFieldBattleHeroCaptureCandidates.Contains(hero))
                                continue;

                            PendingFinalFieldBattleHeroCaptureCandidates.Add(hero);
                        }
                    }
                }

                diagnostics =
                    "armed Mode=" + mode +
                    " ResultId=" + resultId +
                    " WinnerSide=" + result.WinnerSide +
                    " CachedHeroHp=" + PendingFinalEncounterHeroHitPoints.Count +
                    " HeroCaptureCandidates=" + PendingFinalFieldBattleHeroCaptureCandidates.Count +
                    " MainPartySoleEligibleCaptor=" + _pendingFinalFieldBattleMainPartyIsSoleEligibleCaptor;
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
                    ClearFinalFieldBattleHeroCaptureNoLock();
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

        public static bool TryReconcileFinalFieldBattleHeroCaptures(
            MapEvent battle,
            out string diagnostics)
        {
            diagnostics = "hero-capture-contract-not-pending";
            List<Hero> candidates;
            string resultId;
            bool mainPartyIsSoleEligibleCaptor;

            lock (FinalEncounterCompletionSync)
            {
                if (_pendingFinalFieldBattleHeroCaptureBattle == null)
                    return false;

                resultId = _pendingFinalFieldBattleHeroCaptureResultId;
                if (!ReferenceEquals(_pendingFinalFieldBattleHeroCaptureBattle, battle))
                {
                    ClearFinalFieldBattleHeroCaptureNoLock();
                    diagnostics = "hero-capture-contract-battle-mismatch";
                    return true;
                }

                if (!string.Equals(
                        _lastConsumedFinalEncounterResultId,
                        resultId,
                        StringComparison.Ordinal))
                {
                    ClearFinalFieldBattleHeroCaptureNoLock();
                    diagnostics = "hero-capture-contract-final-encounter-not-consumed";
                    return true;
                }

                candidates = new List<Hero>(PendingFinalFieldBattleHeroCaptureCandidates);
                mainPartyIsSoleEligibleCaptor = _pendingFinalFieldBattleMainPartyIsSoleEligibleCaptor;
                ClearFinalFieldBattleHeroCaptureNoLock();
            }

            if (!mainPartyIsSoleEligibleCaptor)
            {
                diagnostics =
                    "ResultId=" + resultId +
                    " Action=skip-main-party-not-sole-eligible-captor";
                return true;
            }

            if (battle == null ||
                battle.WinningSide == BattleSideEnum.None ||
                battle.WinningSide != battle.PlayerSide ||
                battle.RetreatingSide != BattleSideEnum.None)
            {
                diagnostics =
                    "ResultId=" + resultId +
                    " Action=skip-non-capturing-battle-state";
                return true;
            }

            PlayerEncounter encounter = PlayerEncounter.Current;
            TroopRoster captureRoster = encounter?.RosterToReceiveLootPrisoners;
            if (captureRoster == null)
            {
                diagnostics =
                    "ResultId=" + resultId +
                    " Action=skip-capture-roster-unavailable";
                return true;
            }

            int queued = 0;
            int alreadyQueued = 0;
            int alreadyCaptured = 0;
            int skippedInvalid = 0;
            int skippedDead = 0;
            int skippedDeathMarked = 0;
            int skippedCannotBecomePrisoner = 0;
            int reappliedWoundedState = 0;
            var queuedSamples = new List<string>();
            foreach (Hero hero in candidates)
            {
                if (hero == null)
                {
                    skippedInvalid++;
                    continue;
                }

                if (!hero.IsAlive)
                {
                    skippedInvalid++;
                    skippedDead++;
                    continue;
                }

                if (hero.DeathMark != KillCharacterAction.KillCharacterActionDetail.None)
                {
                    skippedInvalid++;
                    skippedDeathMarked++;
                    continue;
                }

                if (!hero.CanBecomePrisoner())
                {
                    skippedInvalid++;
                    skippedCannotBecomePrisoner++;
                    continue;
                }

                if (hero.IsPrisoner)
                {
                    alreadyCaptured++;
                    continue;
                }

                if (GetRosterCount(captureRoster, hero.CharacterObject) > 0)
                {
                    alreadyQueued++;
                    continue;
                }

                if (!hero.IsWounded)
                {
                    hero.MakeWounded(null, KillCharacterAction.KillCharacterActionDetail.None);
                    reappliedWoundedState++;
                }

                captureRoster.AddToCounts(hero.CharacterObject, 1);
                queued++;
                if (queuedSamples.Count < 8)
                    queuedSamples.Add(hero.StringId ?? hero.Name?.ToString() ?? "unknown-hero");
            }

            diagnostics =
                "ResultId=" + resultId +
                " Candidates=" + candidates.Count +
                " Queued=" + queued +
                " AlreadyQueued=" + alreadyQueued +
                " AlreadyCaptured=" + alreadyCaptured +
                " SkippedInvalid=" + skippedInvalid +
                " SkippedDead=" + skippedDead +
                " SkippedDeathMarked=" + skippedDeathMarked +
                " SkippedCannotBecomePrisoner=" + skippedCannotBecomePrisoner +
                " ReappliedWoundedState=" + reappliedWoundedState +
                " Samples=[" + string.Join("; ", queuedSamples) + "]" +
                " Action=reconcile-before-native-captured-lord-conversation";
            return true;
        }

        public static bool TryPrepareFinalFieldBattleHeroCapturesForNativeDistribution(
            MapEvent battle,
            out string diagnostics)
        {
            diagnostics = "hero-capture-contract-not-pending";
            List<Hero> candidates;
            string resultId;
            bool mainPartyIsSoleEligibleCaptor;

            lock (FinalEncounterCompletionSync)
            {
                if (_pendingFinalFieldBattleHeroCaptureBattle == null)
                    return false;

                resultId = _pendingFinalFieldBattleHeroCaptureResultId;
                if (!ReferenceEquals(_pendingFinalFieldBattleHeroCaptureBattle, battle))
                {
                    ClearFinalFieldBattleHeroCaptureNoLock();
                    diagnostics = "hero-capture-contract-battle-mismatch";
                    return true;
                }

                if (!string.Equals(
                        _lastConsumedFinalEncounterResultId,
                        resultId,
                        StringComparison.Ordinal))
                {
                    diagnostics = "hero-capture-contract-final-encounter-not-consumed";
                    return true;
                }

                candidates = new List<Hero>(PendingFinalFieldBattleHeroCaptureCandidates);
                mainPartyIsSoleEligibleCaptor = _pendingFinalFieldBattleMainPartyIsSoleEligibleCaptor;
            }

            if (!mainPartyIsSoleEligibleCaptor)
            {
                diagnostics =
                    "ResultId=" + resultId +
                    " Action=skip-main-party-not-sole-eligible-captor";
                return true;
            }

            if (battle == null ||
                battle.WinningSide == BattleSideEnum.None ||
                battle.WinningSide != battle.PlayerSide ||
                battle.RetreatingSide != BattleSideEnum.None)
            {
                diagnostics =
                    "ResultId=" + resultId +
                    " Action=skip-non-capturing-battle-state";
                return true;
            }

            int alreadyWounded = 0;
            int reappliedWoundedState = 0;
            int skippedDead = 0;
            int skippedDeathMarked = 0;
            int skippedCannotBecomePrisoner = 0;
            var reappliedSamples = new List<string>();
            foreach (Hero hero in candidates)
            {
                if (hero == null || !hero.IsAlive)
                {
                    skippedDead++;
                    continue;
                }

                if (hero.DeathMark != KillCharacterAction.KillCharacterActionDetail.None)
                {
                    skippedDeathMarked++;
                    continue;
                }

                if (!hero.CanBecomePrisoner())
                {
                    skippedCannotBecomePrisoner++;
                    continue;
                }

                if (hero.IsWounded)
                {
                    alreadyWounded++;
                    continue;
                }

                hero.MakeWounded(null, KillCharacterAction.KillCharacterActionDetail.None);
                reappliedWoundedState++;
                if (reappliedSamples.Count < 8)
                    reappliedSamples.Add(hero.StringId ?? hero.Name?.ToString() ?? "unknown-hero");
            }

            diagnostics =
                "ResultId=" + resultId +
                " Candidates=" + candidates.Count +
                " AlreadyWounded=" + alreadyWounded +
                " ReappliedWoundedState=" + reappliedWoundedState +
                " SkippedDead=" + skippedDead +
                " SkippedDeathMarked=" + skippedDeathMarked +
                " SkippedCannotBecomePrisoner=" + skippedCannotBecomePrisoner +
                " Samples=[" + string.Join("; ", reappliedSamples) + "]" +
                " Action=prepare-before-native-capture-distribution";
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

        private static bool IsPlayerWinner(MapEvent battle, string winnerSide)
        {
            if (battle == null || battle.PlayerSide == BattleSideEnum.None)
                return false;

            return (battle.PlayerSide == BattleSideEnum.Attacker &&
                    string.Equals(winnerSide, "Attacker", StringComparison.OrdinalIgnoreCase)) ||
                   (battle.PlayerSide == BattleSideEnum.Defender &&
                    string.Equals(winnerSide, "Defender", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsMainPartySoleEligibleCaptor(MapEvent battle)
        {
            if (battle == null || battle.WinningSide == BattleSideEnum.None)
                return false;

            int eligibleCaptorCount = 0;
            bool mainPartyEligible = false;
            foreach (MapEventParty winnerParty in battle.GetMapEventSide(battle.WinningSide).Parties)
            {
                PartyBase party = winnerParty?.Party;
                if (party?.MemberRoster == null ||
                    party.MemberRoster.Count <= 0 ||
                    winnerParty.ContributionToBattle <= 0)
                {
                    continue;
                }

                MobileParty mobileParty = party.MobileParty;
                if (mobileParty != null &&
                    (mobileParty.IsVillager ||
                     mobileParty.IsCaravan ||
                     mobileParty.IsPatrolParty ||
                     ((mobileParty.IsGarrison || mobileParty.IsMilitia) &&
                      mobileParty.CurrentSettlement?.IsVillage == true)))
                {
                    continue;
                }

                eligibleCaptorCount++;
                if (party == PartyBase.MainParty)
                    mainPartyEligible = true;
            }

            return mainPartyEligible && eligibleCaptorCount == 1;
        }

        private static int GetRosterCount(TroopRoster roster, CharacterObject character)
        {
            if (roster == null || character == null)
                return 0;

            foreach (TroopRosterElement element in roster.GetTroopRoster())
            {
                if (element.Character == character)
                    return Math.Max(0, element.Number);
            }

            return 0;
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

        private static void ClearFinalFieldBattleHeroCaptureNoLock()
        {
            _pendingFinalFieldBattleHeroCaptureBattle = null;
            _pendingFinalFieldBattleHeroCaptureResultId = null;
            _pendingFinalFieldBattleMainPartyIsSoleEligibleCaptor = false;
            PendingFinalFieldBattleHeroCaptureCandidates.Clear();
        }
    }
}
