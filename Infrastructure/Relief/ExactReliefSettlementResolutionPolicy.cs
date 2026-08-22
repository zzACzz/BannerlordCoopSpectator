using System;
using System.Collections.Generic;

namespace CoopSpectator.Infrastructure.Relief
{
    internal enum ExactReliefSettlementResolutionStatus
    {
        NoMatch,
        Resolved,
        Ambiguous
    }

    internal enum ExactReliefBesiegerBattleSide
    {
        None,
        Attacker,
        Defender
    }

    internal sealed class ExactReliefPartyDescriptor
    {
        public ExactReliefPartyDescriptor(
            string partyId,
            string armyLeaderPartyId)
        {
            PartyId = partyId ?? string.Empty;
            ArmyLeaderPartyId = armyLeaderPartyId ?? string.Empty;
        }

        public string PartyId { get; }

        public string ArmyLeaderPartyId { get; }
    }

    internal sealed class ExactReliefSiegeCandidateDescriptor
    {
        public ExactReliefSiegeCandidateDescriptor(
            string settlementId,
            bool isActive,
            IReadOnlyList<ExactReliefPartyDescriptor> besiegerParties)
        {
            SettlementId = settlementId ?? string.Empty;
            IsActive = isActive;
            BesiegerParties = besiegerParties ?? Array.Empty<ExactReliefPartyDescriptor>();
        }

        public string SettlementId { get; }

        public bool IsActive { get; }

        public IReadOnlyList<ExactReliefPartyDescriptor> BesiegerParties { get; }
    }

    internal sealed class ExactReliefSettlementResolution
    {
        public ExactReliefSettlementResolution(
            ExactReliefSettlementResolutionStatus status,
            ExactReliefSiegeCandidateDescriptor candidate,
            ExactReliefBesiegerBattleSide besiegerBattleSide,
            int matchingCandidateCount)
        {
            Status = status;
            Candidate = candidate;
            BesiegerBattleSide = besiegerBattleSide;
            MatchingCandidateCount = matchingCandidateCount;
        }

        public ExactReliefSettlementResolutionStatus Status { get; }

        public ExactReliefSiegeCandidateDescriptor Candidate { get; }

        public string SettlementId => Candidate?.SettlementId ?? string.Empty;

        public ExactReliefBesiegerBattleSide BesiegerBattleSide { get; }

        public int MatchingCandidateCount { get; }
    }

    internal static class ExactReliefSettlementResolutionPolicy
    {
        private const string RequiredCampaignBattleType = "SiegeOutside";

        public static ExactReliefSettlementResolution Resolve(
            string campaignBattleType,
            IReadOnlyList<ExactReliefPartyDescriptor> attackerParties,
            IReadOnlyList<ExactReliefPartyDescriptor> defenderParties,
            IReadOnlyList<ExactReliefSiegeCandidateDescriptor> siegeCandidates)
        {
            if (!string.Equals(
                    campaignBattleType,
                    RequiredCampaignBattleType,
                    StringComparison.OrdinalIgnoreCase))
            {
                return NoMatch();
            }

            attackerParties = attackerParties ?? Array.Empty<ExactReliefPartyDescriptor>();
            defenderParties = defenderParties ?? Array.Empty<ExactReliefPartyDescriptor>();
            siegeCandidates = siegeCandidates ?? Array.Empty<ExactReliefSiegeCandidateDescriptor>();
            if (attackerParties.Count == 0 || defenderParties.Count == 0)
                return NoMatch();

            ExactReliefSiegeCandidateDescriptor resolvedCandidate = null;
            ExactReliefBesiegerBattleSide resolvedSide = ExactReliefBesiegerBattleSide.None;
            int matchingCandidateCount = 0;

            for (int i = 0; i < siegeCandidates.Count; i++)
            {
                ExactReliefSiegeCandidateDescriptor candidate = siegeCandidates[i];
                if (candidate?.IsActive != true ||
                    string.IsNullOrWhiteSpace(candidate.SettlementId))
                {
                    continue;
                }

                bool attackerMatches = SideMatchesBesiegerParties(
                    attackerParties,
                    candidate.BesiegerParties);
                bool defenderMatches = SideMatchesBesiegerParties(
                    defenderParties,
                    candidate.BesiegerParties);

                if (attackerMatches == defenderMatches)
                    continue;

                matchingCandidateCount++;
                if (matchingCandidateCount == 1)
                {
                    resolvedCandidate = candidate;
                    resolvedSide = attackerMatches
                        ? ExactReliefBesiegerBattleSide.Attacker
                        : ExactReliefBesiegerBattleSide.Defender;
                }
            }

            if (matchingCandidateCount == 0)
                return NoMatch();

            if (matchingCandidateCount > 1)
            {
                return new ExactReliefSettlementResolution(
                    ExactReliefSettlementResolutionStatus.Ambiguous,
                    null,
                    ExactReliefBesiegerBattleSide.None,
                    matchingCandidateCount);
            }

            return new ExactReliefSettlementResolution(
                ExactReliefSettlementResolutionStatus.Resolved,
                resolvedCandidate,
                resolvedSide,
                matchingCandidateCount);
        }

        private static ExactReliefSettlementResolution NoMatch()
        {
            return new ExactReliefSettlementResolution(
                ExactReliefSettlementResolutionStatus.NoMatch,
                null,
                ExactReliefBesiegerBattleSide.None,
                0);
        }

        private static bool SideMatchesBesiegerParties(
            IReadOnlyList<ExactReliefPartyDescriptor> battleSideParties,
            IReadOnlyList<ExactReliefPartyDescriptor> besiegerParties)
        {
            if (battleSideParties == null || besiegerParties == null)
                return false;

            for (int battleIndex = 0; battleIndex < battleSideParties.Count; battleIndex++)
            {
                ExactReliefPartyDescriptor battleParty = battleSideParties[battleIndex];
                if (battleParty == null)
                    continue;

                for (int siegeIndex = 0; siegeIndex < besiegerParties.Count; siegeIndex++)
                {
                    if (BelongsToSameArmyGroup(
                            battleParty,
                            besiegerParties[siegeIndex]))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool BelongsToSameArmyGroup(
            ExactReliefPartyDescriptor left,
            ExactReliefPartyDescriptor right)
        {
            if (left == null || right == null)
                return false;

            return SameNonEmptyId(left.PartyId, right.PartyId) ||
                   SameNonEmptyId(left.PartyId, right.ArmyLeaderPartyId) ||
                   SameNonEmptyId(left.ArmyLeaderPartyId, right.PartyId) ||
                   SameNonEmptyId(left.ArmyLeaderPartyId, right.ArmyLeaderPartyId);
        }

        private static bool SameNonEmptyId(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left) &&
                   !string.IsNullOrWhiteSpace(right) &&
                   string.Equals(left, right, StringComparison.Ordinal);
        }
    }
}
