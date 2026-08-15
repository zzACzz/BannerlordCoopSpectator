using System;

namespace CoopSpectator.Infrastructure
{
    internal enum CoopMaterializedAgentEntrySnapshotAcknowledgementPhase
    {
        InitialBarrier = 0,
        BattleActive = 1,
        BattleEnded = 2
    }

    internal readonly struct CoopMaterializedAgentEntrySnapshotApplyResult
    {
        public CoopMaterializedAgentEntrySnapshotApplyResult(
            int expectedEntryCount,
            int transportedMappingCount,
            int appliedMappingCount,
            bool appliedSuccessfully)
        {
            ExpectedEntryCount = expectedEntryCount;
            TransportedMappingCount = transportedMappingCount;
            AppliedMappingCount = appliedMappingCount;
            AppliedSuccessfully = appliedSuccessfully;
        }

        public int ExpectedEntryCount { get; }

        public int TransportedMappingCount { get; }

        public int AppliedMappingCount { get; }

        public bool AppliedSuccessfully { get; }
    }

    internal static class CoopMaterializedAgentEntrySnapshotContract
    {
        public static CoopMaterializedAgentEntrySnapshotApplyResult Evaluate(
            int expectedEntryCount,
            int transportedMappingCount,
            int appliedMappingCount)
        {
            bool appliedSuccessfully =
                expectedEntryCount > 0 &&
                transportedMappingCount == expectedEntryCount &&
                appliedMappingCount == expectedEntryCount;
            return new CoopMaterializedAgentEntrySnapshotApplyResult(
                expectedEntryCount,
                transportedMappingCount,
                appliedMappingCount,
                appliedSuccessfully);
        }

        public static bool ShouldAcknowledgeSuccess(
            bool payloadMatched,
            CoopMaterializedAgentEntrySnapshotAcknowledgementPhase phase,
            CoopMaterializedAgentEntrySnapshotApplyResult applyResult)
        {
            if (!payloadMatched)
                return false;

            if (phase == CoopMaterializedAgentEntrySnapshotAcknowledgementPhase.InitialBarrier)
                return applyResult.AppliedSuccessfully;

            if (phase != CoopMaterializedAgentEntrySnapshotAcknowledgementPhase.BattleActive)
                return false;

            return applyResult.ExpectedEntryCount > 0 &&
                   applyResult.TransportedMappingCount == applyResult.ExpectedEntryCount &&
                   applyResult.AppliedMappingCount >= 0 &&
                   applyResult.AppliedMappingCount <= applyResult.TransportedMappingCount;
        }

        public static bool IsRetryDue(
            DateTime completedUtc,
            DateTime nowUtc,
            TimeSpan retryDelay)
        {
            if (completedUtc == DateTime.MinValue)
                return true;

            TimeSpan safeRetryDelay = retryDelay < TimeSpan.Zero ? TimeSpan.Zero : retryDelay;
            return nowUtc - completedUtc >= safeRetryDelay;
        }
    }
}
