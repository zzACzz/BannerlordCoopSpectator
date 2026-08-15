namespace CoopSpectator.Infrastructure
{
    internal enum CoopClientBootstrapTransitionKind
    {
        MissionBoundary,
        BattleSnapshotRefresh,
        AuthoritativeSnapshotReady
    }

    internal readonly struct CoopClientBootstrapTransitionResult
    {
        public CoopClientBootstrapTransitionResult(
            bool preserveDeferredPayloads,
            int pendingPayloadCount,
            int replayedPayloadCount,
            int clearedPayloadCount,
            int skippedAlreadyMaterializedPayloadCount)
        {
            PreserveDeferredPayloads = preserveDeferredPayloads;
            PendingPayloadCount = pendingPayloadCount;
            ReplayedPayloadCount = replayedPayloadCount;
            ClearedPayloadCount = clearedPayloadCount;
            SkippedAlreadyMaterializedPayloadCount = skippedAlreadyMaterializedPayloadCount;
        }

        public bool PreserveDeferredPayloads { get; }

        public int PendingPayloadCount { get; }

        public int ReplayedPayloadCount { get; }

        public int ClearedPayloadCount { get; }

        public int SkippedAlreadyMaterializedPayloadCount { get; }
    }

    internal static class CoopClientBootstrapResetContract
    {
        public static bool ShouldPreserveDeferredPayloads(CoopClientBootstrapTransitionKind transitionKind)
        {
            return transitionKind != CoopClientBootstrapTransitionKind.MissionBoundary;
        }

        public static CoopClientBootstrapTransitionResult Evaluate(
            CoopClientBootstrapTransitionKind transitionKind,
            int pendingPayloadCount,
            int alreadyMaterializedPayloadCount = 0)
        {
            int normalizedPendingPayloadCount = pendingPayloadCount > 0 ? pendingPayloadCount : 0;
            int normalizedAlreadyMaterializedPayloadCount = alreadyMaterializedPayloadCount > 0
                ? alreadyMaterializedPayloadCount
                : 0;
            if (normalizedAlreadyMaterializedPayloadCount > normalizedPendingPayloadCount)
                normalizedAlreadyMaterializedPayloadCount = normalizedPendingPayloadCount;

            if (transitionKind == CoopClientBootstrapTransitionKind.BattleSnapshotRefresh)
            {
                return new CoopClientBootstrapTransitionResult(
                    preserveDeferredPayloads: true,
                    pendingPayloadCount: normalizedPendingPayloadCount,
                    replayedPayloadCount: 0,
                    clearedPayloadCount: 0,
                    skippedAlreadyMaterializedPayloadCount: 0);
            }

            if (transitionKind == CoopClientBootstrapTransitionKind.AuthoritativeSnapshotReady)
            {
                return new CoopClientBootstrapTransitionResult(
                    preserveDeferredPayloads: true,
                    pendingPayloadCount: 0,
                    replayedPayloadCount: normalizedPendingPayloadCount - normalizedAlreadyMaterializedPayloadCount,
                    clearedPayloadCount: 0,
                    skippedAlreadyMaterializedPayloadCount: normalizedAlreadyMaterializedPayloadCount);
            }

            return new CoopClientBootstrapTransitionResult(
                preserveDeferredPayloads: false,
                pendingPayloadCount: 0,
                replayedPayloadCount: 0,
                clearedPayloadCount: normalizedPendingPayloadCount,
                skippedAlreadyMaterializedPayloadCount: 0);
        }
    }
}
