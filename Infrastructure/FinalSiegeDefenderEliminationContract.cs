using System;

namespace CoopSpectator.Infrastructure
{
    public static class FinalSiegeDefenderEliminationContract
    {
        public const string SiegeAssaultStage = "SiegeAssault";
        public const string AttackerSide = "Attacker";
        public const string DefenderSide = "Defender";
        public const string DefenderEliminatedReason = "defender-eliminated";

        public static bool IsTerminalDefenderElimination(
            string battleStage,
            string winnerSide,
            bool defenderPushedBack,
            bool isFinalStage,
            string completionReason)
        {
            return isFinalStage &&
                   !defenderPushedBack &&
                   string.Equals(
                       battleStage,
                       SiegeAssaultStage,
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(
                       winnerSide,
                       AttackerSide,
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(
                       completionReason,
                       DefenderEliminatedReason,
                       StringComparison.OrdinalIgnoreCase);
        }

        public static bool ShouldNormalizeDefenderRemoval(
            string battleStage,
            string winnerSide,
            bool defenderPushedBack,
            bool isFinalStage,
            string completionReason,
            string aggregateSide)
        {
            return IsTerminalDefenderElimination(
                       battleStage,
                       winnerSide,
                       defenderPushedBack,
                       isFinalStage,
                       completionReason) &&
                   string.Equals(
                       aggregateSide,
                       DefenderSide,
                       StringComparison.OrdinalIgnoreCase);
        }

        public static int ResolveDesiredWoundedCount(
            int desiredCount,
            int snapshotWoundedCount,
            int unconsciousCount,
            int otherRemovedCount,
            bool normalizeUnclassifiedRemoval)
        {
            int boundedDesiredCount = Math.Max(0, desiredCount);
            int woundedCount =
                Math.Max(0, snapshotWoundedCount) +
                Math.Max(0, unconsciousCount);
            if (normalizeUnclassifiedRemoval)
                woundedCount += Math.Max(0, otherRemovedCount);

            return Math.Max(
                0,
                Math.Min(boundedDesiredCount, woundedCount));
        }

        public static bool ShouldWoundUnclassifiedRemovedHero(
            int activeCount,
            int killedCount,
            int unconsciousCount,
            int otherRemovedCount,
            bool normalizeUnclassifiedRemoval)
        {
            return normalizeUnclassifiedRemoval &&
                   activeCount <= 0 &&
                   killedCount <= 0 &&
                   unconsciousCount <= 0 &&
                   otherRemovedCount > 0;
        }
    }
}
