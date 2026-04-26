using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    internal static class BattleCommanderResolver
    {
        public static RosterEntryState ResolveCommanderEntry(BattleRuntimeState runtimeState, BattleSideEnum side)
        {
            BattleSideState sideState = ResolveSideState(runtimeState, side);
            return ResolveCommanderEntry(runtimeState, side, sideState?.Entries);
        }

        public static RosterEntryState ResolveCommanderEntry(
            BattleRuntimeState runtimeState,
            BattleSideEnum side,
            IEnumerable<RosterEntryState> candidates)
        {
            if (runtimeState == null || side == BattleSideEnum.None || candidates == null)
                return null;

            BattleSideState sideState = ResolveSideState(runtimeState, side);
            if (sideState == null)
                return null;

            List<RosterEntryState> candidateEntries = candidates
                .Where(entry => entry != null)
                .ToList();
            if (candidateEntries.Count <= 0)
                return null;

            string leaderPartyId = sideState.LeaderPartyId ?? string.Empty;
            bool isPlayerSide =
                sideState.IsPlayerSide ||
                candidateEntries.Any(entry => IsHeroRoleEntry(entry, "player"));
            List<RosterEntryState> commanderSignalEntries = candidateEntries
                .Where(entry => HasCommanderIdentitySignal(runtimeState, isPlayerSide, entry))
                .ToList();
            if (commanderSignalEntries.Count > 0)
            {
                return commanderSignalEntries
                    .OrderBy(entry => GetCommanderSelectionPriority(runtimeState, isPlayerSide, entry))
                    .ThenByDescending(entry => PartyMatchesSideLeader(entry, leaderPartyId))
                    .ThenByDescending(entry => PartyLeaderHeroMatchesEntry(runtimeState, entry))
                    .ThenByDescending(entry => entry.IsHero)
                    .ThenByDescending(entry => entry.HeroLevel)
                    .ThenByDescending(entry => entry.Tier)
                    .ThenByDescending(entry => entry.BaseHitPoints)
                    .ThenBy(entry => entry.EntryId, StringComparer.Ordinal)
                    .FirstOrDefault();
            }

            return ResolveFallbackCommanderEntry(runtimeState, sideState, candidateEntries);
        }

        public static bool IsCommanderEntry(BattleRuntimeState runtimeState, BattleSideEnum side, RosterEntryState entry)
        {
            if (entry == null)
                return false;

            RosterEntryState commanderEntry = ResolveCommanderEntry(runtimeState, side);
            return commanderEntry != null &&
                   string.Equals(commanderEntry.EntryId, entry.EntryId, StringComparison.Ordinal);
        }

        private static int GetCommanderSelectionPriority(
            BattleRuntimeState runtimeState,
            bool isPlayerSide,
            RosterEntryState entry)
        {
            if (entry == null)
                return int.MaxValue;

            if (isPlayerSide && IsHeroRoleEntry(entry, "player"))
                return 0;

            if (IsHeroRoleEntry(entry, "lord"))
                return 1;

            if (PartyLeaderHeroMatchesEntry(runtimeState, entry))
                return 2;

            if (isPlayerSide && IsHeroRoleEntry(entry, "companion", "wanderer"))
                return 3;

            if (entry.IsHero)
                return 4;

            return 10;
        }

        private static bool HasCommanderIdentitySignal(
            BattleRuntimeState runtimeState,
            bool isPlayerSide,
            RosterEntryState entry)
        {
            return GetCommanderSelectionPriority(runtimeState, isPlayerSide, entry) < 10;
        }

        private static RosterEntryState ResolveFallbackCommanderEntry(
            BattleRuntimeState runtimeState,
            BattleSideState sideState,
            IEnumerable<RosterEntryState> candidateEntries)
        {
            if (sideState == null || candidateEntries == null)
                return null;

            string leaderPartyId = sideState.LeaderPartyId ?? string.Empty;
            return candidateEntries
                .Where(entry => entry != null)
                .OrderByDescending(entry => PartyMatchesSideLeader(entry, leaderPartyId))
                .ThenByDescending(entry => PartyLeaderHeroMatchesEntry(runtimeState, entry))
                .ThenByDescending(entry => entry.IsHero)
                .ThenByDescending(entry => entry.HeroLevel)
                .ThenByDescending(entry => entry.Tier)
                .ThenByDescending(entry => entry.BaseHitPoints)
                .ThenByDescending(entry => entry.Count)
                .ThenBy(entry => entry.EntryId, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        private static bool PartyMatchesSideLeader(RosterEntryState entry, string leaderPartyId)
        {
            return entry != null &&
                   !string.IsNullOrWhiteSpace(leaderPartyId) &&
                   string.Equals(entry.PartyId, leaderPartyId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool PartyLeaderHeroMatchesEntry(BattleRuntimeState runtimeState, RosterEntryState entry)
        {
            if (runtimeState?.PartiesById == null ||
                entry == null ||
                string.IsNullOrWhiteSpace(entry.PartyId) ||
                string.IsNullOrWhiteSpace(entry.HeroId) ||
                !runtimeState.PartiesById.TryGetValue(entry.PartyId, out BattlePartyState partyState))
            {
                return false;
            }

            string leaderHeroId = partyState?.Modifiers?.LeaderHeroId;
            return !string.IsNullOrWhiteSpace(leaderHeroId) &&
                   string.Equals(entry.HeroId, leaderHeroId, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsHeroRoleEntry(RosterEntryState entry, params string[] roles)
        {
            if (entry == null)
                return false;

            if (roles == null || roles.Length == 0)
                return entry.IsHero && !string.IsNullOrWhiteSpace(entry.HeroRole);

            string heroRole = entry.HeroRole ?? string.Empty;
            foreach (string role in roles)
            {
                if (string.Equals(heroRole, role, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static BattleSideState ResolveSideState(BattleRuntimeState runtimeState, BattleSideEnum side)
        {
            if (runtimeState?.Sides == null || side == BattleSideEnum.None)
                return null;

            return runtimeState.Sides.FirstOrDefault(candidate =>
            {
                if (candidate == null)
                    return false;

                string canonicalKey =
                    !string.IsNullOrWhiteSpace(candidate.CanonicalSideKey)
                        ? candidate.CanonicalSideKey
                        : candidate.SideId;
                if (string.Equals(canonicalKey, "attacker", StringComparison.OrdinalIgnoreCase))
                    return side == BattleSideEnum.Attacker;
                if (string.Equals(canonicalKey, "defender", StringComparison.OrdinalIgnoreCase))
                    return side == BattleSideEnum.Defender;
                return false;
            });
        }
    }
}
