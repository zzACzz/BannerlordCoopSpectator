using System;
using System.Collections.Generic;

namespace CoopSpectator.Infrastructure
{
    public struct CoopSiegeFormationPositionedMatch
    {
        public CoopSiegeFormationPositionedMatch(int fileIndex, int rankIndex)
        {
            FileIndex = fileIndex;
            RankIndex = rankIndex;
        }

        public int FileIndex { get; }

        public int RankIndex { get; }
    }

    public static class CoopSiegeFormationMembershipSafetyContract
    {
        public static bool ShouldInspect(
            bool isServer,
            bool isExactCampaignSiege,
            bool hasBoundMissionPeer)
        {
            return isServer &&
                   isExactCampaignSiege &&
                   hasBoundMissionPeer;
        }

        public static int ResolveCanonicalMatchIndex(
            int storedFileIndex,
            int storedRankIndex,
            IReadOnlyList<CoopSiegeFormationPositionedMatch> positionedMatches)
        {
            if (positionedMatches == null || positionedMatches.Count == 0)
                return -1;

            for (int i = 0; i < positionedMatches.Count; i++)
            {
                CoopSiegeFormationPositionedMatch match = positionedMatches[i];
                if (match.FileIndex == storedFileIndex &&
                    match.RankIndex == storedRankIndex)
                {
                    return i;
                }
            }

            int canonicalIndex = 0;
            for (int i = 1; i < positionedMatches.Count; i++)
            {
                CoopSiegeFormationPositionedMatch candidate = positionedMatches[i];
                CoopSiegeFormationPositionedMatch current = positionedMatches[canonicalIndex];
                if (candidate.FileIndex < current.FileIndex ||
                    (candidate.FileIndex == current.FileIndex &&
                     candidate.RankIndex < current.RankIndex))
                {
                    canonicalIndex = i;
                }
            }

            return canonicalIndex;
        }

        public static int[] ResolveRedundantMatchIndices(
            int canonicalMatchIndex,
            int positionedMatchCount)
        {
            if (positionedMatchCount <= 1 ||
                canonicalMatchIndex < 0 ||
                canonicalMatchIndex >= positionedMatchCount)
            {
                return Array.Empty<int>();
            }

            int[] redundantIndices = new int[positionedMatchCount - 1];
            int resultIndex = 0;
            for (int i = 0; i < positionedMatchCount; i++)
            {
                if (i != canonicalMatchIndex)
                    redundantIndices[resultIndex++] = i;
            }

            return redundantIndices;
        }
    }
}
