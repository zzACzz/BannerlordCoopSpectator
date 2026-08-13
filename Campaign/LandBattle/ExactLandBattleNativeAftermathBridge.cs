using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Campaign.LandBattle
{
    internal static class ExactLandBattleNativeAftermathBridge
    {
        private static readonly FieldInfo ContributionToBattleField =
            typeof(MapEventParty).GetField(
                "_contributionToBattle",
                BindingFlags.Instance | BindingFlags.NonPublic);

        internal sealed class Preparation
        {
            private readonly List<CasualtyRosterChange> _casualtyChanges;
            private readonly List<ContributionChange> _contributionChanges;
            private bool _applied;
            private bool _committed;

            internal Preparation(
                string resultId,
                List<CasualtyRosterChange> casualtyChanges,
                List<ContributionChange> contributionChanges,
                string diagnostics)
            {
                ResultId = resultId;
                _casualtyChanges = casualtyChanges ?? new List<CasualtyRosterChange>();
                _contributionChanges = contributionChanges ?? new List<ContributionChange>();
                Diagnostics = diagnostics ?? "prepared";
            }

            internal string ResultId { get; }

            internal string Diagnostics { get; }

            internal bool TryApply(out string diagnostics)
            {
                diagnostics = "not-applied";
                if (_applied)
                {
                    diagnostics = "already-applied";
                    return true;
                }

                try
                {
                    foreach (CasualtyRosterChange change in _casualtyChanges)
                        change.CaptureBeforeState();
                    foreach (ContributionChange change in _contributionChanges)
                        change.CaptureBeforeState();

                    foreach (CasualtyRosterChange change in _casualtyChanges)
                        change.Apply();
                    foreach (ContributionChange change in _contributionChanges)
                        change.Apply();

                    _applied = true;
                    diagnostics = "applied " + Diagnostics;
                    return true;
                }
                catch (Exception ex)
                {
                    _applied = true;
                    Rollback();
                    diagnostics = "apply-failed:" + ex.Message;
                    return false;
                }
            }

            internal void Commit()
            {
                if (!_applied)
                    return;

                _committed = true;
            }

            internal void Rollback()
            {
                if (!_applied || _committed)
                    return;

                for (int i = _contributionChanges.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        _contributionChanges[i].Rollback();
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Verbose(
                            "ExactLandBattleNativeAftermathBridge: contribution rollback failed. " +
                            ex.Message);
                    }
                }

                for (int i = _casualtyChanges.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        _casualtyChanges[i].Rollback();
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Verbose(
                            "ExactLandBattleNativeAftermathBridge: casualty rollback failed. " +
                            ex.Message);
                    }
                }

                _applied = false;
            }
        }

        private sealed class CasualtyAggregate
        {
            internal MapEventParty MapEventParty { get; set; }

            internal CharacterObject Character { get; set; }

            internal int Killed { get; set; }

            internal int Wounded { get; set; }
        }

        private sealed class EffectiveRosterAggregate
        {
            internal MapEventParty MapEventParty { get; set; }

            internal CharacterObject Character { get; set; }

            internal int ParticipantCount { get; set; }

            internal int SurvivorCount { get; set; }

            internal int WoundedSurvivorCount { get; set; }
        }

        internal sealed class CasualtyRosterChange
        {
            private int _beforeNumber;
            private int _beforeWounded;
            private bool _beforeCaptured;

            internal TroopRoster Roster { get; set; }

            internal CharacterObject Character { get; set; }

            internal int NumberDelta { get; set; }

            internal int WoundedDelta { get; set; }

            internal bool ApplyMissingOnly { get; set; }

            internal int DesiredMinimumNumber { get; set; }

            internal int DesiredMinimumWounded { get; set; }

            internal bool ApplyAbsoluteTarget { get; set; }

            internal int DesiredNumber { get; set; }

            internal int DesiredWounded { get; set; }

            internal void CaptureBeforeState()
            {
                int index = Roster.FindIndexOfTroop(Character);
                _beforeNumber = index >= 0 ? Roster.GetElementNumber(index) : 0;
                _beforeWounded = index >= 0 ? Roster.GetElementWoundedNumber(index) : 0;
                _beforeCaptured = true;
            }

            internal void Apply()
            {
                if (!_beforeCaptured)
                    throw new InvalidOperationException("casualty-before-state-not-captured");

                if (ApplyAbsoluteTarget)
                {
                    int desiredNumber = Math.Max(0, DesiredNumber);
                    int desiredWounded = Math.Max(0, Math.Min(desiredNumber, DesiredWounded));
                    int index = Roster.FindIndexOfTroop(Character);
                    if (index < 0)
                    {
                        if (desiredNumber > 0)
                        {
                            Roster.AddToCounts(
                                Character,
                                desiredNumber,
                                insertAtFront: false,
                                woundedCount: desiredWounded);
                        }
                        return;
                    }

                    Roster.AddToCountsAtIndex(
                        index,
                        desiredNumber - _beforeNumber,
                        desiredWounded - _beforeWounded,
                        0,
                        removeDepleted: true);
                    return;
                }

                int numberDelta = NumberDelta;
                int woundedDelta = WoundedDelta;
                if (ApplyMissingOnly)
                {
                    ExactCasualtyLedgerDelta delta =
                        ExactCasualtyLedgerMath.PlanMissingDelta(
                            _beforeNumber,
                            _beforeWounded,
                            DesiredMinimumNumber,
                            DesiredMinimumWounded);
                    numberDelta = delta.NumberDelta;
                    woundedDelta = delta.WoundedDelta;
                }

                if (numberDelta == 0 && woundedDelta == 0)
                    return;

                Roster.AddToCounts(
                    Character,
                    numberDelta,
                    insertAtFront: false,
                    woundedCount: woundedDelta);
            }

            internal void Rollback()
            {
                if (!_beforeCaptured)
                    return;

                int index = Roster.FindIndexOfTroop(Character);
                int currentNumber = index >= 0 ? Roster.GetElementNumber(index) : 0;
                int currentWounded = index >= 0 ? Roster.GetElementWoundedNumber(index) : 0;
                if (currentNumber == _beforeNumber && currentWounded == _beforeWounded)
                    return;

                if (index < 0)
                {
                    Roster.AddToCounts(
                        Character,
                        _beforeNumber,
                        insertAtFront: false,
                        woundedCount: _beforeWounded);
                    return;
                }

                Roster.AddToCountsAtIndex(
                    index,
                    _beforeNumber - currentNumber,
                    _beforeWounded - currentWounded,
                    0,
                    removeDepleted: true);
            }
        }

        internal sealed class ContributionChange
        {
            private int _beforeValue;
            private bool _beforeCaptured;

            internal MapEventParty MapEventParty { get; set; }

            internal int Delta { get; set; }

            internal void CaptureBeforeState()
            {
                if (ContributionToBattleField == null)
                    throw new MissingFieldException(typeof(MapEventParty).FullName, "_contributionToBattle");

                _beforeValue = (int)ContributionToBattleField.GetValue(MapEventParty);
                _beforeCaptured = true;
            }

            internal void Apply()
            {
                if (!_beforeCaptured)
                    throw new InvalidOperationException("contribution-before-state-not-captured");

                long desiredValue = (long)_beforeValue + Delta;
                if (desiredValue > int.MaxValue)
                    throw new OverflowException("contribution-overflow");

                ContributionToBattleField.SetValue(MapEventParty, (int)desiredValue);
            }

            internal void Rollback()
            {
                if (_beforeCaptured && ContributionToBattleField != null)
                    ContributionToBattleField.SetValue(MapEventParty, _beforeValue);
            }
        }

        internal static bool TryPrepare(
            MapEvent battle,
            CoopBattleResultBridgeFile.BattleResultSnapshot result,
            out Preparation preparation,
            out string diagnostics)
        {
            return TryPrepareCore(
                battle,
                result,
                result?.Entries,
                validateCasualtyEvents: true,
                includeContributionChanges: true,
                applyMissingCasualtiesOnly: false,
                out preparation,
                out diagnostics);
        }

        internal static bool TryPrepareCasualtyLedgers(
            MapEvent battle,
            CoopBattleResultBridgeFile.BattleResultSnapshot result,
            IEnumerable<CoopBattleResultBridgeFile.BattleResultEntrySnapshot> casualtyEntries,
            out Preparation preparation,
            out string diagnostics)
        {
            return TryPrepareCore(
                battle,
                result,
                casualtyEntries,
                validateCasualtyEvents: false,
                includeContributionChanges: false,
                applyMissingCasualtiesOnly: true,
                out preparation,
                out diagnostics);
        }

        internal static bool TryPrepareEffectiveDefeatedMemberRoster(
            MapEvent battle,
            CoopBattleResultBridgeFile.BattleResultSnapshot result,
            out Preparation preparation,
            out string diagnostics)
        {
            preparation = null;
            diagnostics = "not-prepared";
            if (battle == null || result?.Entries == null)
            {
                diagnostics = "battle-or-result-null";
                return false;
            }

            if (!TryResolveWinnerSide(result.WinnerSide, out BattleSideEnum winnerSide))
            {
                diagnostics = "winner-unresolved";
                return false;
            }

            BattleSideEnum defeatedSide = winnerSide.GetOppositeSide();
            List<MapEventParty> defeatedParties = GetParties(battle, defeatedSide);
            if (defeatedParties.Count == 0)
            {
                diagnostics = "defeated-parties-missing";
                return false;
            }

            Dictionary<string, MapEventParty> allPartiesById = BuildPartyIndex(battle);
            var defeatedPartySet = new HashSet<MapEventParty>(defeatedParties);
            var aggregates = new Dictionary<string, EffectiveRosterAggregate>(StringComparer.OrdinalIgnoreCase);
            foreach (CoopBattleResultBridgeFile.BattleResultEntrySnapshot entry in
                     result.Entries.Where(item => item != null))
            {
                if (!TryResolveParty(allPartiesById, entry.PartyId, out MapEventParty mapEventParty))
                {
                    if (IsSideId(entry.SideId, defeatedSide))
                    {
                        diagnostics = "effective-roster-party-unresolved:" + (entry.PartyId ?? "null");
                        return false;
                    }
                    continue;
                }

                if (!defeatedPartySet.Contains(mapEventParty))
                    continue;

                CharacterObject character = TryResolveCharacter(
                    entry.HeroId,
                    entry.OriginalCharacterId,
                    entry.CharacterId);
                if (character == null)
                {
                    diagnostics =
                        "effective-roster-character-unresolved:" +
                        (entry.HeroId ?? entry.OriginalCharacterId ?? entry.CharacterId ?? "null");
                    return false;
                }

                if (character.IsHero)
                    continue;

                string key = BuildCasualtyKey(mapEventParty, character);
                if (!aggregates.TryGetValue(key, out EffectiveRosterAggregate aggregate))
                {
                    aggregate = new EffectiveRosterAggregate
                    {
                        MapEventParty = mapEventParty,
                        Character = character
                    };
                    aggregates[key] = aggregate;
                }

                aggregate.ParticipantCount = ExactCasualtyLedgerMath.CombineStageCounts(
                    aggregate.ParticipantCount,
                    ExactCasualtyLedgerMath.ResolveEffectiveParticipantCount(
                        entry.ActiveCount,
                        entry.KilledCount,
                        entry.UnconsciousCount,
                        entry.RoutedCount,
                        entry.OtherRemovedCount));
                aggregate.SurvivorCount = ExactCasualtyLedgerMath.CombineStageCounts(
                    aggregate.SurvivorCount,
                    ExactCasualtyLedgerMath.CombineStageCounts(
                        Math.Max(0, entry.SnapshotWoundedCount),
                        ExactCasualtyLedgerMath.ResolveEffectiveSurvivorCount(
                            entry.ActiveCount,
                            entry.UnconsciousCount,
                            entry.RoutedCount)));
                aggregate.WoundedSurvivorCount = ExactCasualtyLedgerMath.CombineStageCounts(
                    aggregate.WoundedSurvivorCount,
                    ExactCasualtyLedgerMath.CombineStageCounts(
                        Math.Max(0, entry.SnapshotWoundedCount),
                        Math.Max(0, entry.UnconsciousCount)));
            }

            var rosterChanges = new List<CasualtyRosterChange>();
            int participantTotal = 0;
            int survivorTotal = 0;
            int woundedTotal = 0;
            foreach (EffectiveRosterAggregate aggregate in aggregates.Values)
            {
                TroopRoster memberRoster = aggregate.MapEventParty?.Party?.MemberRoster;
                if (memberRoster == null)
                {
                    diagnostics = "effective-roster-member-roster-missing";
                    return false;
                }

                rosterChanges.Add(new CasualtyRosterChange
                {
                    Roster = memberRoster,
                    Character = aggregate.Character,
                    ApplyAbsoluteTarget = true,
                    DesiredNumber = aggregate.SurvivorCount,
                    DesiredWounded = aggregate.WoundedSurvivorCount
                });
                participantTotal = ExactCasualtyLedgerMath.CombineStageCounts(
                    participantTotal,
                    aggregate.ParticipantCount);
                survivorTotal = ExactCasualtyLedgerMath.CombineStageCounts(
                    survivorTotal,
                    aggregate.SurvivorCount);
                woundedTotal = ExactCasualtyLedgerMath.CombineStageCounts(
                    woundedTotal,
                    aggregate.WoundedSurvivorCount);
            }

            string preparationDiagnostics =
                "ResultId=" + ResolveResultId(result) +
                " EffectiveRosterEntries=" + aggregates.Count +
                " Participants=" + participantTotal +
                " Survivors=" + survivorTotal +
                " WoundedSurvivors=" + woundedTotal;
            preparation = new Preparation(
                ResolveResultId(result),
                rosterChanges,
                new List<ContributionChange>(),
                preparationDiagnostics);
            if (!preparation.TryApply(out string applyDiagnostics))
            {
                preparation = null;
                diagnostics = applyDiagnostics;
                return false;
            }

            diagnostics = applyDiagnostics;
            ModLogger.Verbose(
                "ExactLandBattleNativeAftermathBridge: prepared effective defeated member roster. " +
                preparationDiagnostics);
            return true;
        }

        private static bool TryPrepareCore(
            MapEvent battle,
            CoopBattleResultBridgeFile.BattleResultSnapshot result,
            IEnumerable<CoopBattleResultBridgeFile.BattleResultEntrySnapshot> casualtyEntries,
            bool validateCasualtyEvents,
            bool includeContributionChanges,
            bool applyMissingCasualtiesOnly,
            out Preparation preparation,
            out string diagnostics)
        {
            preparation = null;
            diagnostics = "not-prepared";
            if (battle == null || result == null)
            {
                diagnostics = "battle-or-result-null";
                return false;
            }

            if (!TryResolveWinnerSide(result.WinnerSide, out BattleSideEnum winnerSide))
            {
                diagnostics = "winner-unresolved";
                return false;
            }

            BattleSideEnum defeatedSide = winnerSide.GetOppositeSide();
            List<MapEventParty> winnerParties = GetParties(battle, winnerSide);
            List<MapEventParty> defeatedParties = GetParties(battle, defeatedSide);
            if (winnerParties.Count == 0 || defeatedParties.Count == 0)
            {
                diagnostics = "battle-parties-missing";
                return false;
            }

            Dictionary<string, MapEventParty> allPartiesById = BuildPartyIndex(battle);
            var defeatedPartySet = new HashSet<MapEventParty>(defeatedParties);
            if (!TryBuildCasualtyAggregates(
                    result,
                    casualtyEntries,
                    validateCasualtyEvents,
                    defeatedSide,
                    allPartiesById,
                    defeatedPartySet,
                    out Dictionary<string, CasualtyAggregate> casualtyAggregates,
                    out string casualtyDiagnostics))
            {
                diagnostics = casualtyDiagnostics;
                return false;
            }

            var casualtyChanges = new List<CasualtyRosterChange>();
            int killed = 0;
            int wounded = 0;
            foreach (CasualtyAggregate aggregate in casualtyAggregates.Values)
            {
                if (aggregate.Killed > 0)
                {
                    var change = new CasualtyRosterChange
                    {
                        Roster = aggregate.MapEventParty.DiedInBattle,
                        Character = aggregate.Character,
                        NumberDelta = aggregate.Killed,
                        WoundedDelta = 0
                    };
                    if (applyMissingCasualtiesOnly)
                    {
                        change.ApplyMissingOnly = true;
                        change.DesiredMinimumNumber = aggregate.Killed;
                        change.DesiredMinimumWounded = 0;
                    }
                    casualtyChanges.Add(change);
                    killed += aggregate.Killed;
                }

                if (aggregate.Wounded > 0)
                {
                    var change = new CasualtyRosterChange
                    {
                        Roster = aggregate.MapEventParty.WoundedInBattle,
                        Character = aggregate.Character,
                        NumberDelta = aggregate.Wounded,
                        WoundedDelta = aggregate.Wounded
                    };
                    if (applyMissingCasualtiesOnly)
                    {
                        change.ApplyMissingOnly = true;
                        change.DesiredMinimumNumber = aggregate.Wounded;
                        change.DesiredMinimumWounded = aggregate.Wounded;
                    }
                    casualtyChanges.Add(change);
                    wounded += aggregate.Wounded;
                }
            }

            List<ContributionChange> contributionChanges;
            string contributionDiagnostics;
            int contributionDelta;
            if (includeContributionChanges)
            {
                contributionChanges = BuildContributionChanges(
                    result,
                    winnerSide,
                    winnerParties,
                    allPartiesById,
                    out contributionDiagnostics,
                    out contributionDelta);
            }
            else
            {
                contributionChanges = new List<ContributionChange>();
                contributionDiagnostics = "disabled-casualty-ledgers-only";
                contributionDelta = 0;
            }

            string resultId = ResolveResultId(result);
            string preparationDiagnostics =
                "ResultId=" + resultId +
                " CasualtyMode=" +
                (applyMissingCasualtiesOnly ? "missing-only" : "additive") +
                " CasualtyEntries=" + casualtyAggregates.Count +
                " Died=" + killed +
                " Wounded=" + wounded +
                " ContributionDelta=" + contributionDelta +
                " Contribution={" + contributionDiagnostics + "}";
            preparation = new Preparation(
                resultId,
                casualtyChanges,
                contributionChanges,
                preparationDiagnostics);
            if (!preparation.TryApply(out string applyDiagnostics))
            {
                preparation = null;
                diagnostics = applyDiagnostics;
                return false;
            }

            diagnostics = applyDiagnostics;
            ModLogger.Verbose(
                "ExactLandBattleNativeAftermathBridge: prepared native aftermath ledgers. " +
                preparationDiagnostics);
            return true;
        }

        private static bool TryBuildCasualtyAggregates(
            CoopBattleResultBridgeFile.BattleResultSnapshot result,
            IEnumerable<CoopBattleResultBridgeFile.BattleResultEntrySnapshot> casualtyEntries,
            bool validateCasualtyEvents,
            BattleSideEnum defeatedSide,
            IDictionary<string, MapEventParty> allPartiesById,
            ISet<MapEventParty> defeatedParties,
            out Dictionary<string, CasualtyAggregate> aggregates,
            out string diagnostics)
        {
            aggregates = new Dictionary<string, CasualtyAggregate>(StringComparer.OrdinalIgnoreCase);
            diagnostics = "casualties-ready";

            foreach (CoopBattleResultBridgeFile.BattleResultEntrySnapshot entry in
                     (casualtyEntries ?? Enumerable.Empty<CoopBattleResultBridgeFile.BattleResultEntrySnapshot>())
                         .Where(item => item != null))
            {
                int killed = Math.Max(0, entry.KilledCount);
                int wounded = Math.Max(0, entry.UnconsciousCount);
                if (killed == 0 && wounded == 0)
                    continue;

                if (!TryResolveParty(allPartiesById, entry.PartyId, out MapEventParty mapEventParty))
                {
                    if (IsSideId(entry.SideId, defeatedSide))
                    {
                        diagnostics = "casualty-party-unresolved:" + (entry.PartyId ?? "null");
                        return false;
                    }

                    continue;
                }

                if (!defeatedParties.Contains(mapEventParty))
                    continue;

                CharacterObject character = TryResolveCharacter(
                    entry.HeroId,
                    entry.OriginalCharacterId,
                    entry.CharacterId);
                if (character == null)
                {
                    diagnostics =
                        "casualty-character-unresolved:" +
                        (entry.HeroId ?? entry.OriginalCharacterId ?? entry.CharacterId ?? "null");
                    return false;
                }

                string key = BuildCasualtyKey(mapEventParty, character);
                if (!aggregates.TryGetValue(key, out CasualtyAggregate aggregate))
                {
                    aggregate = new CasualtyAggregate
                    {
                        MapEventParty = mapEventParty,
                        Character = character
                    };
                    aggregates[key] = aggregate;
                }

                aggregate.Killed = ExactCasualtyLedgerMath.CombineStageCounts(
                    aggregate.Killed,
                    killed);
                aggregate.Wounded = ExactCasualtyLedgerMath.CombineStageCounts(
                    aggregate.Wounded,
                    wounded);
            }

            if (!validateCasualtyEvents)
            {
                diagnostics = "casualty-events-not-validated;authoritative-combined-entries-used";
                return true;
            }

            if (!TryValidateCasualtyEvents(
                    result,
                    defeatedSide,
                    allPartiesById,
                    defeatedParties,
                    aggregates,
                    out diagnostics))
            {
                return false;
            }

            return true;
        }

        private static bool TryValidateCasualtyEvents(
            CoopBattleResultBridgeFile.BattleResultSnapshot result,
            BattleSideEnum defeatedSide,
            IDictionary<string, MapEventParty> allPartiesById,
            ISet<MapEventParty> defeatedParties,
            IDictionary<string, CasualtyAggregate> entryAggregates,
            out string diagnostics)
        {
            diagnostics = "casualty-events-absent;entry-aggregates-used";
            List<CoopBattleResultBridgeFile.BattleResultCasualtyEventSnapshot> casualtyEvents =
                result.CasualtyEvents?
                    .Where(item => item != null)
                    .ToList() ??
                new List<CoopBattleResultBridgeFile.BattleResultCasualtyEventSnapshot>();
            if (casualtyEvents.Count == 0)
                return true;

            var eventAggregates =
                new Dictionary<string, CasualtyAggregate>(StringComparer.OrdinalIgnoreCase);
            foreach (CoopBattleResultBridgeFile.BattleResultCasualtyEventSnapshot casualtyEvent in casualtyEvents)
            {
                bool killed = string.Equals(
                    casualtyEvent.Outcome,
                    "Killed",
                    StringComparison.OrdinalIgnoreCase);
                bool wounded =
                    string.Equals(casualtyEvent.Outcome, "Wounded", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(casualtyEvent.Outcome, "Unconscious", StringComparison.OrdinalIgnoreCase);
                if (!killed && !wounded)
                    continue;

                if (!TryResolveParty(
                        allPartiesById,
                        casualtyEvent.VictimPartyId,
                        out MapEventParty mapEventParty))
                {
                    if (IsSideId(casualtyEvent.VictimSideId, defeatedSide))
                    {
                        diagnostics =
                            "casualty-event-party-unresolved:" +
                            (casualtyEvent.VictimPartyId ?? "null");
                        return false;
                    }

                    continue;
                }

                if (!defeatedParties.Contains(mapEventParty))
                    continue;

                CharacterObject character = TryResolveCharacter(
                    casualtyEvent.VictimHeroId,
                    casualtyEvent.VictimOriginalCharacterId,
                    casualtyEvent.VictimCharacterId);
                if (character == null)
                {
                    diagnostics =
                        "casualty-event-character-unresolved:" +
                        (casualtyEvent.VictimHeroId ??
                         casualtyEvent.VictimOriginalCharacterId ??
                         casualtyEvent.VictimCharacterId ??
                         "null");
                    return false;
                }

                string key = BuildCasualtyKey(mapEventParty, character);
                if (!eventAggregates.TryGetValue(key, out CasualtyAggregate aggregate))
                {
                    aggregate = new CasualtyAggregate
                    {
                        MapEventParty = mapEventParty,
                        Character = character
                    };
                    eventAggregates[key] = aggregate;
                }

                if (killed)
                    aggregate.Killed++;
                if (wounded)
                    aggregate.Wounded++;
            }

            var keys = new HashSet<string>(entryAggregates.Keys, StringComparer.OrdinalIgnoreCase);
            keys.UnionWith(eventAggregates.Keys);
            foreach (string key in keys)
            {
                entryAggregates.TryGetValue(key, out CasualtyAggregate entryAggregate);
                eventAggregates.TryGetValue(key, out CasualtyAggregate eventAggregate);
                int entryKilled = entryAggregate?.Killed ?? 0;
                int entryWounded = entryAggregate?.Wounded ?? 0;
                int eventKilled = eventAggregate?.Killed ?? 0;
                int eventWounded = eventAggregate?.Wounded ?? 0;
                if (entryKilled == eventKilled && entryWounded == eventWounded)
                    continue;

                diagnostics =
                    "casualty-event-mismatch:" + key +
                    " entries=" + entryKilled + "/" + entryWounded +
                    " events=" + eventKilled + "/" + eventWounded;
                return false;
            }

            diagnostics = "casualty-events-validated:" + eventAggregates.Count;
            return true;
        }

        private static List<ContributionChange> BuildContributionChanges(
            CoopBattleResultBridgeFile.BattleResultSnapshot result,
            BattleSideEnum winnerSide,
            IList<MapEventParty> winnerParties,
            IDictionary<string, MapEventParty> allPartiesById,
            out string diagnostics,
            out int totalDelta)
        {
            diagnostics = "single-winner-share;replay-not-required";
            totalDelta = 0;
            if (winnerParties.Count <= 1)
                return new List<ContributionChange>();

            if (result.DroppedCombatEventCount > 0)
            {
                diagnostics =
                    "skipped-incomplete-events:dropped=" +
                    result.DroppedCombatEventCount;
                return new List<ContributionChange>();
            }

            if (ContributionToBattleField == null)
            {
                diagnostics = "skipped-contribution-field-missing";
                return new List<ContributionChange>();
            }

            CombatXpModel combatXpModel =
                TaleWorlds.CampaignSystem.Campaign.Current?.Models?.CombatXpModel;
            if (combatXpModel == null)
            {
                diagnostics = "skipped-combat-xp-model-missing";
                return new List<ContributionChange>();
            }

            var winnerPartySet = new HashSet<MapEventParty>(winnerParties);
            var entriesById = (result.Entries ?? new List<CoopBattleResultBridgeFile.BattleResultEntrySnapshot>())
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.EntryId))
                .GroupBy(entry => entry.EntryId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var deltas = new Dictionary<MapEventParty, int>();
            foreach (CoopBattleResultBridgeFile.BattleResultCombatEventSnapshot combatEvent in
                     (result.CombatEvents ?? new List<CoopBattleResultBridgeFile.BattleResultCombatEventSnapshot>())
                         .Where(item => item != null))
            {
                entriesById.TryGetValue(
                    combatEvent.AttackerEntryId ?? string.Empty,
                    out CoopBattleResultBridgeFile.BattleResultEntrySnapshot attackerEntry);
                entriesById.TryGetValue(
                    combatEvent.VictimEntryId ?? string.Empty,
                    out CoopBattleResultBridgeFile.BattleResultEntrySnapshot victimEntry);
                string attackerPartyId =
                    combatEvent.AttackerPartyId ??
                    attackerEntry?.PartyId;
                bool eventClaimsWinnerSide =
                    IsSideId(combatEvent.AttackerSideId, winnerSide) ||
                    IsSideId(attackerEntry?.SideId, winnerSide);
                if (!TryResolveParty(
                        allPartiesById,
                        attackerPartyId,
                        out MapEventParty attackerParty))
                {
                    if (eventClaimsWinnerSide)
                    {
                        diagnostics =
                            "skipped-unresolved-winner-party:" +
                            (attackerPartyId ?? "null");
                        return new List<ContributionChange>();
                    }

                    continue;
                }

                if (!winnerPartySet.Contains(attackerParty))
                    continue;
                if (combatEvent.IsTeamKill)
                    continue;

                CharacterObject attackerCharacter = TryResolveCharacter(
                    attackerEntry?.HeroId,
                    attackerEntry?.OriginalCharacterId ?? combatEvent.AttackerOriginalCharacterId,
                    attackerEntry?.CharacterId ?? combatEvent.AttackerCharacterId);
                CharacterObject victimCharacter = TryResolveCharacter(
                    victimEntry?.HeroId,
                    victimEntry?.OriginalCharacterId ?? combatEvent.VictimOriginalCharacterId,
                    victimEntry?.CharacterId ?? combatEvent.VictimCharacterId);
                if (attackerCharacter == null || victimCharacter == null)
                {
                    diagnostics =
                        "skipped-unresolved-combat-character:" +
                        (combatEvent.AttackerEntryId ?? "null") + "/" +
                        (combatEvent.VictimEntryId ?? "null");
                    return new List<ContributionChange>();
                }

                int damage = Math.Max(
                    0,
                    (int)Math.Round(combatEvent.Damage, MidpointRounding.AwayFromZero));
                int delta;
                try
                {
                    ExplainedNumber explained = combatXpModel.GetXpFromHit(
                        attackerCharacter,
                        null,
                        victimCharacter,
                        attackerParty.Party,
                        damage,
                        combatEvent.IsFatal,
                        CombatXpModel.MissionTypeEnum.Battle);
                    delta = Math.Max(0, explained.RoundedResultNumber);
                }
                catch (Exception ex)
                {
                    diagnostics = "skipped-combat-xp-failed:" + ex.Message;
                    return new List<ContributionChange>();
                }

                if (delta <= 0)
                    continue;

                deltas.TryGetValue(attackerParty, out int currentDelta);
                long combinedDelta = (long)currentDelta + delta;
                if (combinedDelta > int.MaxValue)
                {
                    diagnostics = "skipped-contribution-delta-overflow";
                    return new List<ContributionChange>();
                }

                deltas[attackerParty] = (int)combinedDelta;
            }

            var changes = deltas
                .Where(pair => pair.Value > 0)
                .Select(pair => new ContributionChange
                {
                    MapEventParty = pair.Key,
                    Delta = pair.Value
                })
                .ToList();
            long combinedTotalDelta = deltas.Values.Aggregate(0L, (sum, value) => sum + value);
            if (combinedTotalDelta > int.MaxValue)
            {
                diagnostics = "skipped-contribution-total-overflow";
                return new List<ContributionChange>();
            }

            totalDelta = (int)combinedTotalDelta;
            diagnostics =
                "exact:event-count=" + (result.CombatEvents?.Count ?? 0) +
                ";party-count=" + changes.Count;
            return changes;
        }

        private static Dictionary<string, MapEventParty> BuildPartyIndex(MapEvent battle)
        {
            var result = new Dictionary<string, MapEventParty>(StringComparer.OrdinalIgnoreCase);
            foreach (MapEventParty party in GetParties(battle, BattleSideEnum.Attacker))
                RegisterParty(result, party);
            foreach (MapEventParty party in GetParties(battle, BattleSideEnum.Defender))
                RegisterParty(result, party);
            return result;
        }

        private static void RegisterParty(
            IDictionary<string, MapEventParty> partiesById,
            MapEventParty mapEventParty)
        {
            if (mapEventParty?.Party == null)
                return;

            string partyBaseId = mapEventParty.Party.Id;
            string mobilePartyId = mapEventParty.Party.MobileParty?.StringId;
            string settlementId = mapEventParty.Party.Settlement?.StringId;
            if (!string.IsNullOrWhiteSpace(partyBaseId))
                partiesById[partyBaseId] = mapEventParty;
            if (!string.IsNullOrWhiteSpace(mobilePartyId))
                partiesById[mobilePartyId] = mapEventParty;
            if (!string.IsNullOrWhiteSpace(settlementId))
                partiesById[settlementId] = mapEventParty;
        }

        private static bool TryResolveParty(
            IDictionary<string, MapEventParty> partiesById,
            string partyId,
            out MapEventParty mapEventParty)
        {
            mapEventParty = null;
            return !string.IsNullOrWhiteSpace(partyId) &&
                   partiesById != null &&
                   partiesById.TryGetValue(partyId, out mapEventParty);
        }

        private static List<MapEventParty> GetParties(MapEvent battle, BattleSideEnum side)
        {
            if (battle == null)
                return new List<MapEventParty>();

            return (side == BattleSideEnum.Attacker
                    ? battle.AttackerSide?.Parties
                    : battle.DefenderSide?.Parties)?
                .Where(party => party != null)
                .ToList() ??
                new List<MapEventParty>();
        }

        private static CharacterObject TryResolveCharacter(
            string heroId,
            string originalCharacterId,
            string characterId)
        {
            Hero hero = TryResolveHero(heroId);
            if (hero?.CharacterObject != null)
                return hero.CharacterObject;

            foreach (string id in new[] { originalCharacterId, characterId, heroId })
            {
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                try
                {
                    CharacterObject character =
                        MBObjectManager.Instance?.GetObject<BasicCharacterObject>(id) as CharacterObject;
                    if (character != null)
                        return character;
                }
                catch
                {
                }
            }

            return null;
        }

        private static Hero TryResolveHero(string heroId)
        {
            if (string.IsNullOrWhiteSpace(heroId))
                return null;
            if (string.Equals(Hero.MainHero?.StringId, heroId, StringComparison.OrdinalIgnoreCase))
                return Hero.MainHero;

            try
            {
                Hero objectManagerHero = MBObjectManager.Instance?.GetObject<Hero>(heroId);
                if (objectManagerHero != null)
                    return objectManagerHero;
            }
            catch
            {
            }

            try
            {
                return (Hero.AllAliveHeroes ?? Enumerable.Empty<Hero>())
                    .FirstOrDefault(hero =>
                        string.Equals(hero?.StringId, heroId, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        private static string BuildCasualtyKey(
            MapEventParty mapEventParty,
            CharacterObject character)
        {
            string partyId =
                mapEventParty?.Party?.MobileParty?.StringId ??
                mapEventParty?.Party?.Settlement?.StringId ??
                mapEventParty?.Party?.Id ??
                "unknown-party";
            return partyId + "|" + (character?.StringId ?? "unknown-character");
        }

        private static bool TryResolveWinnerSide(
            string winnerSide,
            out BattleSideEnum battleSide)
        {
            if (string.Equals(winnerSide, "Attacker", StringComparison.OrdinalIgnoreCase))
            {
                battleSide = BattleSideEnum.Attacker;
                return true;
            }

            if (string.Equals(winnerSide, "Defender", StringComparison.OrdinalIgnoreCase))
            {
                battleSide = BattleSideEnum.Defender;
                return true;
            }

            battleSide = BattleSideEnum.None;
            return false;
        }

        private static bool IsSideId(string sideId, BattleSideEnum side)
        {
            return string.Equals(
                sideId,
                side == BattleSideEnum.Attacker ? "attacker" : "defender",
                StringComparison.OrdinalIgnoreCase);
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
