using System;

namespace CoopSpectator.Infrastructure
{
    public readonly struct ExactCasualtyLedgerDelta
    {
        public ExactCasualtyLedgerDelta(int numberDelta, int woundedDelta)
        {
            NumberDelta = numberDelta;
            WoundedDelta = woundedDelta;
        }

        public int NumberDelta { get; }

        public int WoundedDelta { get; }
    }

    public static class ExactCasualtyLedgerMath
    {
        public static int CombineStageCounts(int accumulatedCount, int stageCount)
        {
            long combined =
                (long)Math.Max(0, accumulatedCount) +
                Math.Max(0, stageCount);
            if (combined > int.MaxValue)
                throw new OverflowException("casualty-stage-count-overflow");

            return (int)combined;
        }

        public static ExactCasualtyLedgerDelta PlanMissingDelta(
            int currentNumber,
            int currentWounded,
            int desiredMinimumNumber,
            int desiredMinimumWounded)
        {
            currentNumber = Math.Max(0, currentNumber);
            currentWounded = Math.Max(0, Math.Min(currentNumber, currentWounded));
            desiredMinimumNumber = Math.Max(0, desiredMinimumNumber);
            desiredMinimumWounded = Math.Max(
                0,
                Math.Min(desiredMinimumNumber, desiredMinimumWounded));

            int targetNumber = Math.Max(currentNumber, desiredMinimumNumber);
            int targetWounded = Math.Min(
                targetNumber,
                Math.Max(currentWounded, desiredMinimumWounded));
            return new ExactCasualtyLedgerDelta(
                targetNumber - currentNumber,
                targetWounded - currentWounded);
        }

        public static int ResolveEffectiveParticipantCount(
            int activeCount,
            int killedCount,
            int unconsciousCount,
            int routedCount,
            int otherRemovedCount)
        {
            int total = CombineStageCounts(activeCount, killedCount);
            total = CombineStageCounts(total, unconsciousCount);
            total = CombineStageCounts(total, routedCount);
            return CombineStageCounts(total, otherRemovedCount);
        }

        public static int ResolveEffectiveSurvivorCount(
            int activeCount,
            int unconsciousCount,
            int routedCount)
        {
            int total = CombineStageCounts(activeCount, unconsciousCount);
            return CombineStageCounts(total, routedCount);
        }

        public static bool ShouldSkipTerminalAgentReconciliation(
            bool isActive,
            bool wasTerminalRemovalRecorded)
        {
            return !isActive && wasTerminalRemovalRecorded;
        }
    }
}
