using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;

namespace CoopSpectator.Campaign.Capture
{
    internal static class ExactCampaignHeroCaptureRuntime
    {
        private static readonly object Sync = new object();
        private static MapEvent _pendingBattle;
        private static string _pendingResultId;
        private static string _pendingMode;
        private static BattleSideEnum _pendingWinnerSide;
        private static bool _pendingMainPartyIsSoleEligibleCaptor;
        private static readonly List<Hero> PendingCandidates = new List<Hero>();

        public static bool TryArm(
            MapEvent battle,
            CoopBattleResultBridgeFile.BattleResultSnapshot result,
            string mode,
            IEnumerable<Hero> defeatedUnconsciousHeroes,
            out string diagnostics)
        {
            diagnostics = "capture-contract-not-armed";
            BattleSideEnum winnerSide = ResolveWinnerSide(result?.WinnerSide);

            lock (Sync)
            {
                ClearNoLock();

                if (battle == null ||
                    result?.IsFinalStage != true ||
                    result.DefenderPushedBack ||
                    winnerSide == BattleSideEnum.None ||
                    battle.PlayerSide == BattleSideEnum.None ||
                    battle.PlayerSide != winnerSide)
                {
                    diagnostics =
                        "capture-contract-ineligible" +
                        " Mode=" + (mode ?? "unknown") +
                        " IsFinalStage=" + (result?.IsFinalStage ?? false) +
                        " DefenderPushedBack=" + (result?.DefenderPushedBack ?? false) +
                        " WinnerSide=" + winnerSide +
                        " PlayerSide=" + (battle?.PlayerSide.ToString() ?? "None");
                    return false;
                }

                _pendingBattle = battle;
                _pendingResultId = ResolveResultId(result);
                _pendingMode = mode ?? result.BattleStage ?? "unknown";
                _pendingWinnerSide = winnerSide;
                _pendingMainPartyIsSoleEligibleCaptor =
                    IsMainPartySoleEligibleCaptor(battle, winnerSide);

                if (defeatedUnconsciousHeroes != null)
                {
                    foreach (Hero hero in defeatedUnconsciousHeroes)
                    {
                        if (hero == null || PendingCandidates.Contains(hero))
                            continue;

                        PendingCandidates.Add(hero);
                    }
                }

                diagnostics =
                    "armed" +
                    " Mode=" + _pendingMode +
                    " ResultId=" + _pendingResultId +
                    " WinnerSide=" + _pendingWinnerSide +
                    " Candidates=" + PendingCandidates.Count +
                    " MainPartySoleEligibleCaptor=" + _pendingMainPartyIsSoleEligibleCaptor;
                return true;
            }
        }

        public static bool TryPrepareForNativeDistribution(
            MapEvent battle,
            out string diagnostics)
        {
            diagnostics = "capture-contract-not-pending";
            List<Hero> candidates;
            string resultId;
            string mode;
            BattleSideEnum winnerSide;

            lock (Sync)
            {
                if (_pendingBattle == null)
                    return false;

                if (!ReferenceEquals(_pendingBattle, battle))
                {
                    ClearNoLock();
                    diagnostics = "capture-contract-battle-mismatch";
                    return true;
                }

                candidates = new List<Hero>(PendingCandidates);
                resultId = _pendingResultId;
                mode = _pendingMode;
                winnerSide = _pendingWinnerSide;
            }

            if (!IsCapturingBattleState(battle, winnerSide))
            {
                diagnostics =
                    "Mode=" + mode +
                    " ResultId=" + resultId +
                    " Action=skip-non-capturing-battle-state";
                return true;
            }

            int alreadyWounded = 0;
            int reappliedWoundedState = 0;
            int alreadyCaptured = 0;
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

                if (hero.IsPrisoner)
                {
                    alreadyCaptured++;
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
                    reappliedSamples.Add(HeroId(hero));
            }

            diagnostics =
                "Mode=" + mode +
                " ResultId=" + resultId +
                " Candidates=" + candidates.Count +
                " AlreadyWounded=" + alreadyWounded +
                " ReappliedWoundedState=" + reappliedWoundedState +
                " AlreadyCaptured=" + alreadyCaptured +
                " SkippedDead=" + skippedDead +
                " SkippedDeathMarked=" + skippedDeathMarked +
                " SkippedCannotBecomePrisoner=" + skippedCannotBecomePrisoner +
                " Samples=[" + string.Join("; ", reappliedSamples) + "]" +
                " Action=prepare-before-native-capture-distribution";
            return true;
        }

        public static bool TryReconcileBeforeNativeConversation(
            MapEvent battle,
            out string diagnostics)
        {
            diagnostics = "capture-contract-not-pending";
            List<Hero> candidates;
            string resultId;
            string mode;
            BattleSideEnum winnerSide;
            bool mainPartyIsSoleEligibleCaptor;

            lock (Sync)
            {
                if (_pendingBattle == null)
                    return false;

                if (!ReferenceEquals(_pendingBattle, battle))
                {
                    ClearNoLock();
                    diagnostics = "capture-contract-battle-mismatch";
                    return true;
                }

                candidates = new List<Hero>(PendingCandidates);
                resultId = _pendingResultId;
                mode = _pendingMode;
                winnerSide = _pendingWinnerSide;
                mainPartyIsSoleEligibleCaptor = _pendingMainPartyIsSoleEligibleCaptor;
                ClearNoLock();
            }

            if (!mainPartyIsSoleEligibleCaptor)
            {
                diagnostics =
                    "Mode=" + mode +
                    " ResultId=" + resultId +
                    " Candidates=" + candidates.Count +
                    " Action=skip-main-party-not-sole-eligible-captor";
                return true;
            }

            if (!IsCapturingBattleState(battle, winnerSide))
            {
                diagnostics =
                    "Mode=" + mode +
                    " ResultId=" + resultId +
                    " Action=skip-non-capturing-battle-state";
                return true;
            }

            PlayerEncounter encounter = PlayerEncounter.Current;
            TroopRoster captureRoster = encounter?.RosterToReceiveLootPrisoners;
            if (captureRoster == null)
            {
                diagnostics =
                    "Mode=" + mode +
                    " ResultId=" + resultId +
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
                    queuedSamples.Add(HeroId(hero));
            }

            diagnostics =
                "Mode=" + mode +
                " ResultId=" + resultId +
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

        private static bool IsCapturingBattleState(
            MapEvent battle,
            BattleSideEnum expectedWinnerSide)
        {
            return battle != null &&
                   expectedWinnerSide != BattleSideEnum.None &&
                   battle.WinningSide == expectedWinnerSide &&
                   battle.WinningSide == battle.PlayerSide &&
                   battle.RetreatingSide == BattleSideEnum.None;
        }

        private static bool IsMainPartySoleEligibleCaptor(
            MapEvent battle,
            BattleSideEnum winnerSide)
        {
            if (battle == null || winnerSide == BattleSideEnum.None)
                return false;

            int eligibleCaptorCount = 0;
            bool mainPartyEligible = false;
            foreach (MapEventParty winnerParty in battle.GetMapEventSide(winnerSide).Parties)
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

        private static BattleSideEnum ResolveWinnerSide(string winnerSide)
        {
            if (string.Equals(winnerSide, "Attacker", StringComparison.OrdinalIgnoreCase))
                return BattleSideEnum.Attacker;
            if (string.Equals(winnerSide, "Defender", StringComparison.OrdinalIgnoreCase))
                return BattleSideEnum.Defender;
            return BattleSideEnum.None;
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

        private static string HeroId(Hero hero)
        {
            return hero?.StringId ?? hero?.Name?.ToString() ?? "unknown-hero";
        }

        private static void ClearNoLock()
        {
            _pendingBattle = null;
            _pendingResultId = null;
            _pendingMode = null;
            _pendingWinnerSide = BattleSideEnum.None;
            _pendingMainPartyIsSoleEligibleCaptor = false;
            PendingCandidates.Clear();
        }
    }
}
