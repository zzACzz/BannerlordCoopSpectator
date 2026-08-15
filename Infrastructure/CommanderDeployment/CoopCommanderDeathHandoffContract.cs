namespace CoopSpectator.Infrastructure
{
    internal static class CoopCommanderDeathHandoffContract
    {
        public static bool ShouldReleaseRemovedAgentOwnership(
            bool isServer,
            bool hasMission,
            bool hasAgent,
            bool hasPlayableTeam,
            bool ownsGeneralRole,
            bool ownsPlayerOrderController,
            int ownedFormationCount)
        {
            return isServer &&
                   hasMission &&
                   hasAgent &&
                   hasPlayableTeam &&
                   (ownsGeneralRole || ownsPlayerOrderController || ownedFormationCount > 0);
        }

        public static bool ShouldIssueChargeOrder(
            bool useNativeExactSiegeFormationAi,
            int activeFormationUnitCount)
        {
            return !useNativeExactSiegeFormationAi && activeFormationUnitCount > 0;
        }

        public static bool ShouldReleaseFormationOwnership(
            bool ownedByRemovedAgent,
            bool releasedGeneralOwnership,
            int activeFormationUnitCount,
            bool hasDifferentActivePlayerOwner)
        {
            if (ownedByRemovedAgent)
                return true;

            return releasedGeneralOwnership &&
                   activeFormationUnitCount > 0 &&
                   !hasDifferentActivePlayerOwner;
        }
    }
}
