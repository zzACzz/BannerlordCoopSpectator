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

        internal sealed class CasualtyRosterChange
        {
            private int _beforeNumber;
            private int _beforeWounded;
            private bool _beforeCaptured;

            internal TroopRoster Roster { get; set; }

            internal CharacterObject Character { get; set; }

            internal int NumberDelta { get; set; }

            internal int WoundedDelta { get; set; }

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

                Roster.AddToCounts(
                    Character,
                    NumberDelta,
                    insertAtFront: false,
                    woundedCount: WoundedDelta);
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
                    casualtyChanges.Add(new CasualtyRosterChange
                    {
                        Roster = aggregate.MapEventParty.DiedInBattle,
                        Character = aggregate.Character,
                        NumberDelta = aggregate.Killed,
                        WoundedDelta = 0
                    });
                    killed += aggregate.Killed;
                }

                if (aggregate.Wounded > 0)
                {
                    casualtyChanges.Add(new CasualtyRosterChange
                    {
                        Roster = aggregate.MapEventParty.WoundedInBattle,
                        Character = aggregate.Character,
                        NumberDelta = aggregate.Wounded,
                        WoundedDelta = aggregate.Wounded
                    });
                    wounded += aggregate.Wounded;
                }
            }

            List<ContributionChange> contributionChanges = BuildContributionChanges(
                result,
                winnerSide,
                winnerParties,
                allPartiesById,
                out string contributionDiagnostics,
                out int contributionDelta);

            string resultId = ResolveResultId(result);
            string preparationDiagnostics =
                "ResultId=" + resultId +
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
            BattleSideEnum defeatedSide,
            IDictionary<string, MapEventParty> allPartiesById,
            ISet<MapEventParty> defeatedParties,
            out Dictionary<string, CasualtyAggregate> aggregates,
            out string diagnostics)
        {
            aggregates = new Dictionary<string, CasualtyAggregate>(StringComparer.OrdinalIgnoreCase);
            diagnostics = "casualties-ready";

            foreach (CoopBattleResultBridgeFile.BattleResultEntrySnapshot entry in
                     (result.Entries ?? new List<CoopBattleResultBridgeFile.BattleResultEntrySnapshot>())
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

                aggregate.Killed += killed;
                aggregate.Wounded += wounded;
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
