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
        public List<RosterEntryState> Entries { get; set; } = new List<RosterEntryState>();
        public List<string> TroopIds { get; set; } = new List<string>();
    }

    public sealed class RosterEntryState
    {
        public string EntryId { get; set; }
        public string SideId { get; set; }
        public string PartyId { get; set; }
        public string CharacterId { get; set; }
        public string TroopName { get; set; }
        public int Count { get; set; }
        public int WoundedCount { get; set; }
        public bool IsHero { get; set; }
        public bool IsMounted { get; set; }
        public int Tier { get; set; }
    }

    public sealed class BattleSideProjectionState
    {
        public string SideId { get; set; }
        public string SideText { get; set; }
        public string CanonicalSideKey { get; set; }
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
        public List<BattleRosterEntryProjectionState> Entries { get; set; } = new List<BattleRosterEntryProjectionState>();
        public List<string> TroopIds { get; set; } = new List<string>();
    }

    public sealed class BattleRosterEntryProjectionState
    {
        public string EntryId { get; set; }
        public string SideId { get; set; }
        public string PartyId { get; set; }
        public string CharacterId { get; set; }
        public string TroopName { get; set; }
        public int Count { get; set; }
        public int WoundedCount { get; set; }
        public bool IsHero { get; set; }
        public bool IsMounted { get; set; }
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

            lock (Sync)
            {
                _current = snapshot;
                _projection = BuildProjection(snapshot);
                _state = BuildState(_projection);
                _source = source ?? "unknown";
                _updatedUtc = DateTime.UtcNow;
            }

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

            ModLogger.Info("BattleSnapshotRuntimeState: snapshot cleared. Reason=" + (_source ?? "unknown"));
        }

        public static List<string> FlattenTroopIds(BattleSnapshotMessage snapshot)
        {
            if (snapshot?.Sides == null || snapshot.Sides.Count == 0)
                return new List<string>();

            return snapshot.Sides
                .Where(side => side?.Troops != null)
                .SelectMany(side => side.Troops)
                .Select(troop => troop?.CharacterId)
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
                    string.Equals(candidate.CharacterId, troopId, StringComparison.OrdinalIgnoreCase));
                return entry?.EntryId;
            }
        }

        public static BasicCharacterObject TryResolveCharacterObject(string entryId)
        {
            BattleRosterEntryProjectionState entry = GetEntry(entryId);
            if (entry == null || string.IsNullOrWhiteSpace(entry.CharacterId))
                return null;

            try
            {
                return MBObjectManager.Instance.GetObject<BasicCharacterObject>(entry.CharacterId);
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
                        TotalManCount = partySnapshot.TotalManCount
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
                        .Select(entry => entry.CharacterId)
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
                    .Select(entry => entry.CharacterId)
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
                Snapshot = projection?.Snapshot
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
                        TotalManCount = partyProjection.TotalManCount
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
                        TroopName = entryProjection.TroopName,
                        Count = entryProjection.Count,
                        WoundedCount = entryProjection.WoundedCount,
                        IsHero = entryProjection.IsHero,
                        IsMounted = entryProjection.IsMounted,
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
                    .Select(entry => entry.CharacterId)
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
            if (troop == null || string.IsNullOrWhiteSpace(troop.CharacterId))
                return null;

            return new BattleRosterEntryProjectionState
            {
                EntryId = ResolveEntryId(troop, canonicalSideKey, fallbackPartyId, ordinal),
                SideId = canonicalSideKey,
                PartyId = string.IsNullOrWhiteSpace(troop.PartyId) ? fallbackPartyId : troop.PartyId,
                CharacterId = troop.CharacterId,
                TroopName = troop.TroopName,
                Count = troop.Count,
                WoundedCount = troop.WoundedCount,
                IsHero = troop.IsHero,
                IsMounted = troop.IsMounted,
                Tier = troop.Tier
            };
        }

        private static string ResolveEntryId(TroopStackInfo troop, string canonicalSideKey, string fallbackPartyId, int ordinal)
        {
            if (!string.IsNullOrWhiteSpace(troop?.EntryId))
                return troop.EntryId;

            string partyId = string.IsNullOrWhiteSpace(troop?.PartyId) ? fallbackPartyId : troop.PartyId;
            string characterId = troop?.CharacterId ?? "unknown";
            return (canonicalSideKey ?? "unknown") + "|" + (partyId ?? "party") + "|" + characterId + "|" + ordinal;
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
