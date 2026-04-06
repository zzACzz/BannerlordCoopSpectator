using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Network.Messages;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Infrastructure
{
    public sealed class BattleSnapshotProjectionState
    {
        public BattleSnapshotMessage Snapshot { get; set; }
        public List<BattleSideProjectionState> Sides { get; set; } = new List<BattleSideProjectionState>();
        public Dictionary<string, BattleSideProjectionState> SidesByKey { get; set; } = new Dictionary<string, BattleSideProjectionState>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, BattlePartyProjectionState> PartiesById { get; set; } = new Dictionary<string, BattlePartyProjectionState>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, BattleRosterEntryProjectionState> EntriesById { get; set; } = new Dictionary<string, BattleRosterEntryProjectionState>(StringComparer.OrdinalIgnoreCase);
        public List<string> FlatTroopIds { get; set; } = new List<string>();
    }

    public sealed class BattleRuntimeState
    {
        public BattleSnapshotMessage Snapshot { get; set; }
        public int BattleSizeBudget { get; set; }
        public int ReinforcementWaveCount { get; set; }
        public string BattleSizeBudgetSource { get; set; }
        public List<BattleSideState> Sides { get; set; } = new List<BattleSideState>();
        public Dictionary<string, BattleSideState> SidesByKey { get; set; } = new Dictionary<string, BattleSideState>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, BattlePartyState> PartiesById { get; set; } = new Dictionary<string, BattlePartyState>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, RosterEntryState> EntriesById { get; set; } = new Dictionary<string, RosterEntryState>(StringComparer.OrdinalIgnoreCase);
        public List<string> FlatTroopIds { get; set; } = new List<string>();
    }

    public sealed class BattleSideState
    {
        public string SideId { get; set; }
        public string SideText { get; set; }
        public string CanonicalSideKey { get; set; }
        public string LeaderPartyId { get; set; }
        public float SideMorale { get; set; }
        public bool IsPlayerSide { get; set; }
        public int TotalManCount { get; set; }
        public List<BattlePartyState> Parties { get; set; } = new List<BattlePartyState>();
        public List<RosterEntryState> Entries { get; set; } = new List<RosterEntryState>();
        public List<string> TroopIds { get; set; } = new List<string>();
    }

    public sealed class BattlePartyState
    {
        public string PartyId { get; set; }
        public string PartyName { get; set; }
        public string SideId { get; set; }
        public bool IsMainParty { get; set; }
        public int TotalManCount { get; set; }
        public BattlePartyModifierState Modifiers { get; set; } = new BattlePartyModifierState();
        public List<RosterEntryState> Entries { get; set; } = new List<RosterEntryState>();
        public List<string> TroopIds { get; set; } = new List<string>();
    }

    public sealed class BattlePartyModifierState
    {
        public string LeaderHeroId { get; set; }
        public string OwnerHeroId { get; set; }
        public string ScoutHeroId { get; set; }
        public string QuartermasterHeroId { get; set; }
        public string EngineerHeroId { get; set; }
        public string SurgeonHeroId { get; set; }
        public float Morale { get; set; }
        public float RecentEventsMorale { get; set; }
        public float MoraleChange { get; set; }
        public int ContributionToBattle { get; set; }
        public int LeaderLeadershipSkill { get; set; }
        public int LeaderTacticsSkill { get; set; }
        public int ScoutScoutingSkill { get; set; }
        public int QuartermasterStewardSkill { get; set; }
        public int EngineerEngineeringSkill { get; set; }
        public int SurgeonMedicineSkill { get; set; }
        public List<string> PartyLeaderPerkIds { get; set; } = new List<string>();
        public List<string> ArmyCommanderPerkIds { get; set; } = new List<string>();
        public List<string> CaptainPerkIds { get; set; } = new List<string>();
        public List<string> ScoutPerkIds { get; set; } = new List<string>();
        public List<string> QuartermasterPerkIds { get; set; } = new List<string>();
        public List<string> EngineerPerkIds { get; set; } = new List<string>();
        public List<string> SurgeonPerkIds { get; set; } = new List<string>();
    }

    public sealed class RosterEntryState
    {
        public string EntryId { get; set; }
        public string SideId { get; set; }
        public string PartyId { get; set; }
        public string CharacterId { get; set; }
        public string OriginalCharacterId { get; set; }
        public string SpawnTemplateId { get; set; }
        public string TroopName { get; set; }
        public string CultureId { get; set; }
        public string HeroId { get; set; }
        public string HeroRole { get; set; }
        public string HeroOccupationId { get; set; }
        public string HeroClanId { get; set; }
        public string HeroTemplateId { get; set; }
        public string HeroBodyProperties { get; set; }
        public int HeroLevel { get; set; }
        public float HeroAge { get; set; }
        public bool HeroIsFemale { get; set; }
        public int Count { get; set; }
        public int WoundedCount { get; set; }
        public bool IsHero { get; set; }
        public bool IsMounted { get; set; }
        public bool IsRanged { get; set; }
        public bool HasShield { get; set; }
        public bool HasThrown { get; set; }
        public int AttributeVigor { get; set; }
        public int AttributeControl { get; set; }
        public int AttributeEndurance { get; set; }
        public int SkillOneHanded { get; set; }
        public int SkillTwoHanded { get; set; }
        public int SkillPolearm { get; set; }
        public int SkillBow { get; set; }
        public int SkillCrossbow { get; set; }
        public int SkillThrowing { get; set; }
        public int SkillRiding { get; set; }
        public int SkillAthletics { get; set; }
        public int BaseHitPoints { get; set; }
        public List<string> PerkIds { get; set; } = new List<string>();
        public string CombatItem0Id { get; set; }
        public string CombatItem1Id { get; set; }
        public string CombatItem2Id { get; set; }
        public string CombatItem3Id { get; set; }
        public string CombatHeadId { get; set; }
        public string CombatBodyId { get; set; }
        public string CombatLegId { get; set; }
        public string CombatGlovesId { get; set; }
        public string CombatCapeId { get; set; }
        public string CombatHorseId { get; set; }
        public string CombatHorseHarnessId { get; set; }
        public int Tier { get; set; }
    }

    public sealed class BattleSideProjectionState
    {
        public string SideId { get; set; }
        public string SideText { get; set; }
        public string CanonicalSideKey { get; set; }
        public string LeaderPartyId { get; set; }
        public float SideMorale { get; set; }
        public bool IsPlayerSide { get; set; }
        public int TotalManCount { get; set; }
        public List<BattlePartyProjectionState> Parties { get; set; } = new List<BattlePartyProjectionState>();
        public List<BattleRosterEntryProjectionState> Entries { get; set; } = new List<BattleRosterEntryProjectionState>();
        public List<string> TroopIds { get; set; } = new List<string>();
    }

    public sealed class BattlePartyProjectionState
    {
        public string PartyId { get; set; }
        public string PartyName { get; set; }
        public string SideId { get; set; }
        public bool IsMainParty { get; set; }
        public int TotalManCount { get; set; }
        public BattlePartyModifierProjectionState Modifiers { get; set; } = new BattlePartyModifierProjectionState();
        public List<BattleRosterEntryProjectionState> Entries { get; set; } = new List<BattleRosterEntryProjectionState>();
        public List<string> TroopIds { get; set; } = new List<string>();
    }

    public sealed class BattlePartyModifierProjectionState
    {
        public string LeaderHeroId { get; set; }
        public string OwnerHeroId { get; set; }
        public string ScoutHeroId { get; set; }
        public string QuartermasterHeroId { get; set; }
        public string EngineerHeroId { get; set; }
        public string SurgeonHeroId { get; set; }
        public float Morale { get; set; }
        public float RecentEventsMorale { get; set; }
        public float MoraleChange { get; set; }
        public int ContributionToBattle { get; set; }
        public int LeaderLeadershipSkill { get; set; }
        public int LeaderTacticsSkill { get; set; }
        public int ScoutScoutingSkill { get; set; }
        public int QuartermasterStewardSkill { get; set; }
        public int EngineerEngineeringSkill { get; set; }
        public int SurgeonMedicineSkill { get; set; }
        public List<string> PartyLeaderPerkIds { get; set; } = new List<string>();
        public List<string> ArmyCommanderPerkIds { get; set; } = new List<string>();
        public List<string> CaptainPerkIds { get; set; } = new List<string>();
        public List<string> ScoutPerkIds { get; set; } = new List<string>();
        public List<string> QuartermasterPerkIds { get; set; } = new List<string>();
        public List<string> EngineerPerkIds { get; set; } = new List<string>();
        public List<string> SurgeonPerkIds { get; set; } = new List<string>();
    }

    public sealed class BattleRosterEntryProjectionState
    {
        public string EntryId { get; set; }
        public string SideId { get; set; }
        public string PartyId { get; set; }
        public string CharacterId { get; set; }
        public string OriginalCharacterId { get; set; }
        public string SpawnTemplateId { get; set; }
        public string TroopName { get; set; }
        public string CultureId { get; set; }
        public string HeroId { get; set; }
        public string HeroRole { get; set; }
        public string HeroOccupationId { get; set; }
        public string HeroClanId { get; set; }
        public string HeroTemplateId { get; set; }
        public string HeroBodyProperties { get; set; }
        public int HeroLevel { get; set; }
        public float HeroAge { get; set; }
        public bool HeroIsFemale { get; set; }
        public int Count { get; set; }
        public int WoundedCount { get; set; }
        public bool IsHero { get; set; }
        public bool IsMounted { get; set; }
        public bool IsRanged { get; set; }
        public bool HasShield { get; set; }
        public bool HasThrown { get; set; }
        public int AttributeVigor { get; set; }
        public int AttributeControl { get; set; }
        public int AttributeEndurance { get; set; }
        public int SkillOneHanded { get; set; }
        public int SkillTwoHanded { get; set; }
        public int SkillPolearm { get; set; }
        public int SkillBow { get; set; }
        public int SkillCrossbow { get; set; }
        public int SkillThrowing { get; set; }
        public int SkillRiding { get; set; }
        public int SkillAthletics { get; set; }
        public int BaseHitPoints { get; set; }
        public List<string> PerkIds { get; set; } = new List<string>();
        public string CombatItem0Id { get; set; }
        public string CombatItem1Id { get; set; }
        public string CombatItem2Id { get; set; }
        public string CombatItem3Id { get; set; }
        public string CombatHeadId { get; set; }
        public string CombatBodyId { get; set; }
        public string CombatLegId { get; set; }
        public string CombatGlovesId { get; set; }
        public string CombatCapeId { get; set; }
        public string CombatHorseId { get; set; }
        public string CombatHorseHarnessId { get; set; }
        public int Tier { get; set; }
    }

    public static class BattleSnapshotRuntimeState
    {
        private static readonly object Sync = new object();
        private static BattleSnapshotMessage _current;
        private static BattleSnapshotProjectionState _projection;
        private static BattleRuntimeState _state;
        private static string _source;
        private static DateTime _updatedUtc;

        public static void SetCurrent(BattleSnapshotMessage snapshot, string source)
        {
            if (snapshot?.Sides == null || snapshot.Sides.Count == 0)
                return;

            BattleRuntimeState runtimeState;
            lock (Sync)
            {
                _current = snapshot;
                _projection = BuildProjection(snapshot);
                _state = BuildState(_projection);
                runtimeState = _state;
                _source = source ?? "unknown";
                _updatedUtc = DateTime.UtcNow;
            }

            ExactCampaignRuntimeObjectRegistry.SyncFromState(runtimeState, _source);

            ModLogger.Info(
                "BattleSnapshotRuntimeState: snapshot updated. " +
                "Source=" + (_source ?? "unknown") +
                " Sides=" + (snapshot.Sides?.Count ?? 0) +
                " Entries=" + (_projection?.EntriesById?.Count ?? 0) +
                " BattleId=" + (snapshot.BattleId ?? "null"));
        }

        public static BattleSnapshotMessage GetCurrent()
        {
            lock (Sync)
            {
                return _current;
            }
        }

        public static string GetSource()
        {
            lock (Sync)
            {
                return _source;
            }
        }

        public static DateTime GetUpdatedUtc()
        {
            lock (Sync)
            {
                return _updatedUtc;
            }
        }

        public static BattleSnapshotProjectionState GetProjection()
        {
            lock (Sync)
            {
                return _projection;
            }
        }

        public static BattleRuntimeState GetState()
        {
            lock (Sync)
            {
                return _state;
            }
        }

        public static BattleSideState GetSideState(string canonicalSideKey)
        {
            if (string.IsNullOrWhiteSpace(canonicalSideKey))
                return null;

            lock (Sync)
            {
                if (_state?.SidesByKey == null)
                    return null;

                _state.SidesByKey.TryGetValue(canonicalSideKey, out BattleSideState sideState);
                return sideState;
            }
        }

        public static void Clear(string reason)
        {
            lock (Sync)
            {
                _current = null;
                _projection = null;
                _state = null;
                _source = reason ?? "cleared";
                _updatedUtc = DateTime.UtcNow;
            }

            ExactCampaignRuntimeObjectRegistry.Clear(reason);

            ModLogger.Info("BattleSnapshotRuntimeState: snapshot cleared. Reason=" + (_source ?? "unknown"));
        }

        public static List<string> FlattenTroopIds(BattleSnapshotMessage snapshot)
        {
            if (snapshot?.Sides == null || snapshot.Sides.Count == 0)
                return new List<string>();

            return snapshot.Sides
                .Where(side => side?.Troops != null)
                .SelectMany(side => side.Troops)
                .Select(troop => ResolveSpawnTemplateId(troop))
                .Where(characterId => !string.IsNullOrWhiteSpace(characterId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static BattleRosterEntryProjectionState GetEntry(string entryId)
        {
            if (string.IsNullOrWhiteSpace(entryId))
                return null;

            lock (Sync)
            {
                if (_projection?.EntriesById == null)
                    return null;

                _projection.EntriesById.TryGetValue(entryId, out BattleRosterEntryProjectionState entry);
                return entry;
            }
        }

        public static RosterEntryState GetEntryState(string entryId)
        {
            if (string.IsNullOrWhiteSpace(entryId))
                return null;

            lock (Sync)
            {
                if (_state?.EntriesById == null)
                    return null;

                _state.EntriesById.TryGetValue(entryId, out RosterEntryState entry);
                return entry;
            }
        }

        public static string TryResolveEntryId(string canonicalSideKey, string troopId)
        {
            if (string.IsNullOrWhiteSpace(canonicalSideKey) || string.IsNullOrWhiteSpace(troopId))
                return null;

            lock (Sync)
            {
                if (_state?.SidesByKey == null ||
                    !_state.SidesByKey.TryGetValue(canonicalSideKey, out BattleSideState sideState) ||
                    sideState?.Entries == null)
                {
                    return null;
                }

                RosterEntryState entry = sideState.Entries.FirstOrDefault(candidate =>
                    candidate != null &&
                    string.Equals(ResolveSpawnTemplateId(candidate), troopId, StringComparison.OrdinalIgnoreCase));
                return entry?.EntryId;
            }
        }

        public static BasicCharacterObject TryResolveCharacterObject(string entryId)
        {
            BasicCharacterObject runtimeCharacter = ExactCampaignRuntimeObjectRegistry.TryResolveCharacter(entryId);
            if (runtimeCharacter != null)
                return runtimeCharacter;

            BattleRosterEntryProjectionState entry = GetEntry(entryId);
            if (!string.IsNullOrWhiteSpace(entry?.OriginalCharacterId))
            {
                try
                {
                    BasicCharacterObject originalCharacter = MBObjectManager.Instance.GetObject<BasicCharacterObject>(entry.OriginalCharacterId);
                    if (originalCharacter != null)
                        return originalCharacter;
                }
                catch
                {
                }
            }

            if (!string.IsNullOrWhiteSpace(entry?.HeroTemplateId))
            {
                try
                {
                    BasicCharacterObject heroTemplateCharacter = MBObjectManager.Instance.GetObject<BasicCharacterObject>(entry.HeroTemplateId);
                    if (heroTemplateCharacter != null)
                        return heroTemplateCharacter;
                }
                catch
                {
                }
            }

            string spawnTemplateId = ResolveSpawnTemplateId(entry);
            if (string.IsNullOrWhiteSpace(spawnTemplateId))
                return null;

            try
            {
                return MBObjectManager.Instance.GetObject<BasicCharacterObject>(spawnTemplateId);
            }
            catch
            {
                return null;
            }
        }

        private static BattleSnapshotProjectionState BuildProjection(BattleSnapshotMessage snapshot)
        {
            var projection = new BattleSnapshotProjectionState
            {
                Snapshot = snapshot
            };

            if (snapshot?.Sides == null || snapshot.Sides.Count == 0)
                return projection;

            foreach (BattleSideSnapshotMessage sideSnapshot in snapshot.Sides.Where(side => side != null))
            {
                var sideProjection = new BattleSideProjectionState
                {
                    SideId = sideSnapshot.SideId,
                    SideText = sideSnapshot.SideText,
                    CanonicalSideKey = NormalizeSideKey(sideSnapshot.SideText ?? sideSnapshot.SideId),
                    LeaderPartyId = sideSnapshot.LeaderPartyId,
                    SideMorale = sideSnapshot.SideMorale,
                    IsPlayerSide = sideSnapshot.IsPlayerSide,
                    TotalManCount = sideSnapshot.TotalManCount
                };
                projection.Sides.Add(sideProjection);
                projection.SidesByKey[sideProjection.CanonicalSideKey] = sideProjection;

                foreach (BattlePartySnapshotMessage partySnapshot in sideSnapshot.Parties.Where(party => party != null))
                {
                    string partyId = string.IsNullOrWhiteSpace(partySnapshot.PartyId)
                        ? "party_" + projection.PartiesById.Count
                        : partySnapshot.PartyId;
                    var partyProjection = new BattlePartyProjectionState
                    {
                        PartyId = partyId,
                        PartyName = partySnapshot.PartyName,
                        SideId = sideProjection.CanonicalSideKey,
                        IsMainParty = partySnapshot.IsMainParty,
                        TotalManCount = partySnapshot.TotalManCount,
                        Modifiers = BuildPartyModifierProjection(partySnapshot.Modifiers)
                    };
                    sideProjection.Parties.Add(partyProjection);
                    projection.PartiesById[partyProjection.PartyId] = partyProjection;

                    foreach (TroopStackInfo troop in partySnapshot.Troops.Where(entry => entry != null))
                    {
                        BattleRosterEntryProjectionState entryProjection = BuildEntryProjection(troop, sideProjection.CanonicalSideKey, partyProjection.PartyId, projection.EntriesById.Count);
                        if (entryProjection == null || projection.EntriesById.ContainsKey(entryProjection.EntryId))
                            continue;

                        projection.EntriesById[entryProjection.EntryId] = entryProjection;
                        partyProjection.Entries.Add(entryProjection);
                        sideProjection.Entries.Add(entryProjection);
                    }

                    partyProjection.TroopIds = partyProjection.Entries
                        .Select(entry => ResolveSpawnTemplateId(entry))
                        .Where(characterId => !string.IsNullOrWhiteSpace(characterId))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                foreach (TroopStackInfo troop in sideSnapshot.Troops.Where(entry => entry != null))
                {
                    string entryId = ResolveEntryId(troop, sideProjection.CanonicalSideKey, troop.PartyId, projection.EntriesById.Count);
                    if (projection.EntriesById.ContainsKey(entryId))
                        continue;

                    BattleRosterEntryProjectionState entryProjection = BuildEntryProjection(troop, sideProjection.CanonicalSideKey, troop.PartyId, projection.EntriesById.Count);
                    if (entryProjection == null)
                        continue;

                    projection.EntriesById[entryProjection.EntryId] = entryProjection;
                    sideProjection.Entries.Add(entryProjection);
                }

                sideProjection.TroopIds = sideProjection.Entries
                    .Select(entry => ResolveSpawnTemplateId(entry))
                    .Where(characterId => !string.IsNullOrWhiteSpace(characterId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            projection.FlatTroopIds = projection.Sides
                .SelectMany(side => side.TroopIds)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return projection;
        }

        private static BattleRuntimeState BuildState(BattleSnapshotProjectionState projection)
        {
            var state = new BattleRuntimeState
            {
                Snapshot = projection?.Snapshot,
                BattleSizeBudget = projection?.Snapshot?.BattleSizeBudget ?? 0,
                ReinforcementWaveCount = projection?.Snapshot?.ReinforcementWaveCount ?? 0,
                BattleSizeBudgetSource = projection?.Snapshot?.BattleSizeBudgetSource
            };

            if (projection == null)
                return state;

            foreach (BattleSideProjectionState sideProjection in projection.Sides.Where(side => side != null))
            {
                var sideState = new BattleSideState
                {
                    SideId = sideProjection.SideId,
                    SideText = sideProjection.SideText,
                    CanonicalSideKey = sideProjection.CanonicalSideKey,
                    LeaderPartyId = sideProjection.LeaderPartyId,
                    SideMorale = sideProjection.SideMorale,
                    IsPlayerSide = sideProjection.IsPlayerSide,
                    TotalManCount = sideProjection.TotalManCount
                };
                state.Sides.Add(sideState);
                state.SidesByKey[sideState.CanonicalSideKey] = sideState;

                foreach (BattlePartyProjectionState partyProjection in sideProjection.Parties.Where(party => party != null))
                {
                    var partyState = new BattlePartyState
                    {
                        PartyId = partyProjection.PartyId,
                        PartyName = partyProjection.PartyName,
                        SideId = partyProjection.SideId,
                        IsMainParty = partyProjection.IsMainParty,
                        TotalManCount = partyProjection.TotalManCount,
                        Modifiers = BuildPartyModifierState(partyProjection.Modifiers)
                    };
                    sideState.Parties.Add(partyState);
                    state.PartiesById[partyState.PartyId] = partyState;
                }

                foreach (BattleRosterEntryProjectionState entryProjection in sideProjection.Entries.Where(entry => entry != null))
                {
                    var entryState = new RosterEntryState
                    {
                        EntryId = entryProjection.EntryId,
                        SideId = entryProjection.SideId,
                        PartyId = entryProjection.PartyId,
                        CharacterId = entryProjection.CharacterId,
                        OriginalCharacterId = entryProjection.OriginalCharacterId,
                        SpawnTemplateId = entryProjection.SpawnTemplateId,
                        TroopName = entryProjection.TroopName,
                        CultureId = entryProjection.CultureId,
                        HeroId = entryProjection.HeroId,
                        HeroRole = entryProjection.HeroRole,
                        HeroOccupationId = entryProjection.HeroOccupationId,
                        HeroClanId = entryProjection.HeroClanId,
                        HeroTemplateId = entryProjection.HeroTemplateId,
                        HeroBodyProperties = entryProjection.HeroBodyProperties,
                        HeroLevel = entryProjection.HeroLevel,
                        HeroAge = entryProjection.HeroAge,
                        HeroIsFemale = entryProjection.HeroIsFemale,
                        Count = entryProjection.Count,
                        WoundedCount = entryProjection.WoundedCount,
                        IsHero = entryProjection.IsHero,
                        IsMounted = entryProjection.IsMounted,
                        IsRanged = entryProjection.IsRanged,
                        HasShield = entryProjection.HasShield,
                        HasThrown = entryProjection.HasThrown,
                        AttributeVigor = entryProjection.AttributeVigor,
                        AttributeControl = entryProjection.AttributeControl,
                        AttributeEndurance = entryProjection.AttributeEndurance,
                        SkillOneHanded = entryProjection.SkillOneHanded,
                        SkillTwoHanded = entryProjection.SkillTwoHanded,
                        SkillPolearm = entryProjection.SkillPolearm,
                        SkillBow = entryProjection.SkillBow,
                        SkillCrossbow = entryProjection.SkillCrossbow,
                        SkillThrowing = entryProjection.SkillThrowing,
                        SkillRiding = entryProjection.SkillRiding,
                        SkillAthletics = entryProjection.SkillAthletics,
                        BaseHitPoints = entryProjection.BaseHitPoints,
                        PerkIds = entryProjection.PerkIds != null ? new List<string>(entryProjection.PerkIds) : new List<string>(),
                        CombatItem0Id = entryProjection.CombatItem0Id,
                        CombatItem1Id = entryProjection.CombatItem1Id,
                        CombatItem2Id = entryProjection.CombatItem2Id,
                        CombatItem3Id = entryProjection.CombatItem3Id,
                        CombatHeadId = entryProjection.CombatHeadId,
                        CombatBodyId = entryProjection.CombatBodyId,
                        CombatLegId = entryProjection.CombatLegId,
                        CombatGlovesId = entryProjection.CombatGlovesId,
                        CombatCapeId = entryProjection.CombatCapeId,
                        CombatHorseId = entryProjection.CombatHorseId,
                        CombatHorseHarnessId = entryProjection.CombatHorseHarnessId,
                        Tier = entryProjection.Tier
                    };
                    sideState.Entries.Add(entryState);
                    state.EntriesById[entryState.EntryId] = entryState;

                    if (!string.IsNullOrWhiteSpace(entryState.PartyId) &&
                        state.PartiesById.TryGetValue(entryState.PartyId, out BattlePartyState partyState))
                    {
                        partyState.Entries.Add(entryState);
                    }
                }

                sideState.TroopIds = sideState.Entries
                    .Select(entry => ResolveSpawnTemplateId(entry))
                    .Where(characterId => !string.IsNullOrWhiteSpace(characterId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            foreach (BattlePartyState partyState in state.PartiesById.Values)
            {
                partyState.TroopIds = partyState.Entries
                    .Select(entry => entry.CharacterId)
                    .Where(characterId => !string.IsNullOrWhiteSpace(characterId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            state.FlatTroopIds = state.Sides
                .SelectMany(side => side.TroopIds)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return state;
        }

        private static BattleRosterEntryProjectionState BuildEntryProjection(TroopStackInfo troop, string canonicalSideKey, string fallbackPartyId, int ordinal)
        {
            string spawnTemplateId = ResolveSpawnTemplateId(troop);
            if (troop == null || string.IsNullOrWhiteSpace(spawnTemplateId))
                return null;

            return new BattleRosterEntryProjectionState
            {
                EntryId = ResolveEntryId(troop, canonicalSideKey, fallbackPartyId, ordinal),
                SideId = canonicalSideKey,
                PartyId = string.IsNullOrWhiteSpace(troop.PartyId) ? fallbackPartyId : troop.PartyId,
                CharacterId = spawnTemplateId,
                OriginalCharacterId = ResolveOriginalCharacterId(troop),
                SpawnTemplateId = spawnTemplateId,
                TroopName = troop.TroopName,
                CultureId = troop.CultureId,
                HeroId = troop.HeroId,
                HeroRole = troop.HeroRole,
                HeroOccupationId = troop.HeroOccupationId,
                HeroClanId = troop.HeroClanId,
                HeroTemplateId = troop.HeroTemplateId,
                HeroBodyProperties = troop.HeroBodyProperties,
                HeroLevel = troop.HeroLevel,
                HeroAge = troop.HeroAge,
                HeroIsFemale = troop.HeroIsFemale,
                Count = troop.Count,
                WoundedCount = troop.WoundedCount,
                IsHero = troop.IsHero,
                IsMounted = troop.IsMounted,
                IsRanged = troop.IsRanged,
                HasShield = troop.HasShield,
                HasThrown = troop.HasThrown,
                AttributeVigor = troop.AttributeVigor,
                AttributeControl = troop.AttributeControl,
                AttributeEndurance = troop.AttributeEndurance,
                SkillOneHanded = troop.SkillOneHanded,
                SkillTwoHanded = troop.SkillTwoHanded,
                SkillPolearm = troop.SkillPolearm,
                SkillBow = troop.SkillBow,
                SkillCrossbow = troop.SkillCrossbow,
                SkillThrowing = troop.SkillThrowing,
                SkillRiding = troop.SkillRiding,
                SkillAthletics = troop.SkillAthletics,
                BaseHitPoints = troop.BaseHitPoints,
                PerkIds = troop.PerkIds != null ? new List<string>(troop.PerkIds) : new List<string>(),
                CombatItem0Id = troop.CombatItem0Id,
                CombatItem1Id = troop.CombatItem1Id,
                CombatItem2Id = troop.CombatItem2Id,
                CombatItem3Id = troop.CombatItem3Id,
                CombatHeadId = troop.CombatHeadId,
                CombatBodyId = troop.CombatBodyId,
                CombatLegId = troop.CombatLegId,
                CombatGlovesId = troop.CombatGlovesId,
                CombatCapeId = troop.CombatCapeId,
                CombatHorseId = troop.CombatHorseId,
                CombatHorseHarnessId = troop.CombatHorseHarnessId,
                Tier = troop.Tier
            };
        }

        private static BattlePartyModifierProjectionState BuildPartyModifierProjection(BattlePartyModifierSnapshotMessage modifierSnapshot)
        {
            if (modifierSnapshot == null)
                return new BattlePartyModifierProjectionState();

            return new BattlePartyModifierProjectionState
            {
                LeaderHeroId = modifierSnapshot.LeaderHeroId,
                OwnerHeroId = modifierSnapshot.OwnerHeroId,
                ScoutHeroId = modifierSnapshot.ScoutHeroId,
                QuartermasterHeroId = modifierSnapshot.QuartermasterHeroId,
                EngineerHeroId = modifierSnapshot.EngineerHeroId,
                SurgeonHeroId = modifierSnapshot.SurgeonHeroId,
                Morale = modifierSnapshot.Morale,
                RecentEventsMorale = modifierSnapshot.RecentEventsMorale,
                MoraleChange = modifierSnapshot.MoraleChange,
                ContributionToBattle = modifierSnapshot.ContributionToBattle,
                LeaderLeadershipSkill = modifierSnapshot.LeaderLeadershipSkill,
                LeaderTacticsSkill = modifierSnapshot.LeaderTacticsSkill,
                ScoutScoutingSkill = modifierSnapshot.ScoutScoutingSkill,
                QuartermasterStewardSkill = modifierSnapshot.QuartermasterStewardSkill,
                EngineerEngineeringSkill = modifierSnapshot.EngineerEngineeringSkill,
                SurgeonMedicineSkill = modifierSnapshot.SurgeonMedicineSkill,
                PartyLeaderPerkIds = modifierSnapshot.PartyLeaderPerkIds != null ? new List<string>(modifierSnapshot.PartyLeaderPerkIds) : new List<string>(),
                ArmyCommanderPerkIds = modifierSnapshot.ArmyCommanderPerkIds != null ? new List<string>(modifierSnapshot.ArmyCommanderPerkIds) : new List<string>(),
                CaptainPerkIds = modifierSnapshot.CaptainPerkIds != null ? new List<string>(modifierSnapshot.CaptainPerkIds) : new List<string>(),
                ScoutPerkIds = modifierSnapshot.ScoutPerkIds != null ? new List<string>(modifierSnapshot.ScoutPerkIds) : new List<string>(),
                QuartermasterPerkIds = modifierSnapshot.QuartermasterPerkIds != null ? new List<string>(modifierSnapshot.QuartermasterPerkIds) : new List<string>(),
                EngineerPerkIds = modifierSnapshot.EngineerPerkIds != null ? new List<string>(modifierSnapshot.EngineerPerkIds) : new List<string>(),
                SurgeonPerkIds = modifierSnapshot.SurgeonPerkIds != null ? new List<string>(modifierSnapshot.SurgeonPerkIds) : new List<string>()
            };
        }

        private static BattlePartyModifierState BuildPartyModifierState(BattlePartyModifierProjectionState modifierProjection)
        {
            if (modifierProjection == null)
                return new BattlePartyModifierState();

            return new BattlePartyModifierState
            {
                LeaderHeroId = modifierProjection.LeaderHeroId,
                OwnerHeroId = modifierProjection.OwnerHeroId,
                ScoutHeroId = modifierProjection.ScoutHeroId,
                QuartermasterHeroId = modifierProjection.QuartermasterHeroId,
                EngineerHeroId = modifierProjection.EngineerHeroId,
                SurgeonHeroId = modifierProjection.SurgeonHeroId,
                Morale = modifierProjection.Morale,
                RecentEventsMorale = modifierProjection.RecentEventsMorale,
                MoraleChange = modifierProjection.MoraleChange,
                ContributionToBattle = modifierProjection.ContributionToBattle,
                LeaderLeadershipSkill = modifierProjection.LeaderLeadershipSkill,
                LeaderTacticsSkill = modifierProjection.LeaderTacticsSkill,
                ScoutScoutingSkill = modifierProjection.ScoutScoutingSkill,
                QuartermasterStewardSkill = modifierProjection.QuartermasterStewardSkill,
                EngineerEngineeringSkill = modifierProjection.EngineerEngineeringSkill,
                SurgeonMedicineSkill = modifierProjection.SurgeonMedicineSkill,
                PartyLeaderPerkIds = modifierProjection.PartyLeaderPerkIds != null ? new List<string>(modifierProjection.PartyLeaderPerkIds) : new List<string>(),
                ArmyCommanderPerkIds = modifierProjection.ArmyCommanderPerkIds != null ? new List<string>(modifierProjection.ArmyCommanderPerkIds) : new List<string>(),
                CaptainPerkIds = modifierProjection.CaptainPerkIds != null ? new List<string>(modifierProjection.CaptainPerkIds) : new List<string>(),
                ScoutPerkIds = modifierProjection.ScoutPerkIds != null ? new List<string>(modifierProjection.ScoutPerkIds) : new List<string>(),
                QuartermasterPerkIds = modifierProjection.QuartermasterPerkIds != null ? new List<string>(modifierProjection.QuartermasterPerkIds) : new List<string>(),
                EngineerPerkIds = modifierProjection.EngineerPerkIds != null ? new List<string>(modifierProjection.EngineerPerkIds) : new List<string>(),
                SurgeonPerkIds = modifierProjection.SurgeonPerkIds != null ? new List<string>(modifierProjection.SurgeonPerkIds) : new List<string>()
            };
        }

        private static string ResolveEntryId(TroopStackInfo troop, string canonicalSideKey, string fallbackPartyId, int ordinal)
        {
            if (!string.IsNullOrWhiteSpace(troop?.EntryId))
                return troop.EntryId;

            string partyId = string.IsNullOrWhiteSpace(troop?.PartyId) ? fallbackPartyId : troop.PartyId;
            string originalCharacterId = ResolveOriginalCharacterId(troop) ?? "unknown";
            string characterId = ResolveSpawnTemplateId(troop) ?? "unknown";
            return (canonicalSideKey ?? "unknown") + "|" + (partyId ?? "party") + "|" + originalCharacterId + "|" + characterId + "|" + ordinal;
        }

        private static string ResolveSpawnTemplateId(TroopStackInfo troop)
        {
            if (troop == null)
                return null;

            return !string.IsNullOrWhiteSpace(troop.SpawnTemplateId)
                ? troop.SpawnTemplateId
                : troop.CharacterId;
        }

        private static string ResolveSpawnTemplateId(BattleRosterEntryProjectionState entry)
        {
            if (entry == null)
                return null;

            return !string.IsNullOrWhiteSpace(entry.SpawnTemplateId)
                ? entry.SpawnTemplateId
                : entry.CharacterId;
        }

        private static string ResolveSpawnTemplateId(RosterEntryState entry)
        {
            if (entry == null)
                return null;

            return !string.IsNullOrWhiteSpace(entry.SpawnTemplateId)
                ? entry.SpawnTemplateId
                : entry.CharacterId;
        }

        private static string ResolveOriginalCharacterId(TroopStackInfo troop)
        {
            if (troop == null)
                return null;

            return !string.IsNullOrWhiteSpace(troop.OriginalCharacterId)
                ? troop.OriginalCharacterId
                : troop.CharacterId;
        }

        private static string NormalizeSideKey(string sideText)
        {
            if (string.Equals(sideText, "Attacker", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sideText, "attacker", StringComparison.OrdinalIgnoreCase))
            {
                return "attacker";
            }

            if (string.Equals(sideText, "Defender", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sideText, "defender", StringComparison.OrdinalIgnoreCase))
            {
                return "defender";
            }

            return string.IsNullOrWhiteSpace(sideText) ? "unknown" : sideText.Trim().ToLowerInvariant();
        }
    }
}
