namespace CoopSpectator.Infrastructure
{
    internal enum CoopMountedHeroMountLinkDecision
    {
        NotRequired = 0,
        LinkVerified = 1,
        RepairInitialLink = 2,
        PreserveRuntimeDismount = 3
    }

    internal static class CoopMountedHeroMountLinkContract
    {
        public static CoopMountedHeroMountLinkDecision Evaluate(
            bool isClient,
            bool snapshotExpectsMount,
            int trackedMountAgentIndex,
            int liveMountAgentIndex,
            bool hasVerifiedLiveMountLink)
        {
            if (!isClient || !snapshotExpectsMount)
                return CoopMountedHeroMountLinkDecision.NotRequired;

            if (liveMountAgentIndex >= 0)
                return CoopMountedHeroMountLinkDecision.LinkVerified;

            if (trackedMountAgentIndex < 0)
                return CoopMountedHeroMountLinkDecision.NotRequired;

            return hasVerifiedLiveMountLink
                ? CoopMountedHeroMountLinkDecision.PreserveRuntimeDismount
                : CoopMountedHeroMountLinkDecision.RepairInitialLink;
        }
    }
}
