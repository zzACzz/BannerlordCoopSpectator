namespace CoopSpectator.Infrastructure
{
    internal enum CoopClientWeaponRuntimeSafetyDecision
    {
        Allow = 0,
        Defer = 1,
        Suppress = 2
    }

    internal sealed class CoopClientWeaponRuntimeSafetyResult
    {
        public CoopClientWeaponRuntimeSafetyResult(
            CoopClientWeaponRuntimeSafetyDecision decision,
            string reason)
        {
            Decision = decision;
            Reason = reason;
        }

        public CoopClientWeaponRuntimeSafetyDecision Decision { get; private set; }
        public string Reason { get; private set; }
        public bool IsNativeSafe
        {
            get { return Decision == CoopClientWeaponRuntimeSafetyDecision.Allow; }
        }
    }

    internal static class CoopClientWeaponRuntimeSafetyContract
    {
        public static CoopClientWeaponRuntimeSafetyResult Evaluate(
            bool isCoopClientContext,
            bool snapshotReady,
            bool hasDeferredAgentBootstrap,
            bool agentExists,
            bool agentActive,
            bool requestTargetsWeaponSlot,
            bool requestedSlotOccupied,
            bool validateUsageIndex,
            bool usageCatalogReadable,
            int requestedUsageIndex,
            int usageCount)
        {
            if (!isCoopClientContext)
                return Allow("outside-coop-client-context");

            if (!snapshotReady || hasDeferredAgentBootstrap)
            {
                return new CoopClientWeaponRuntimeSafetyResult(
                    CoopClientWeaponRuntimeSafetyDecision.Defer,
                    !snapshotReady
                        ? "battle-snapshot-not-ready"
                        : "agent-bootstrap-deferred");
            }

            if (!agentExists)
                return Suppress("agent-missing");

            if (!agentActive)
                return Suppress("agent-inactive");

            if (!requestTargetsWeaponSlot)
                return Allow("request-does-not-target-weapon-slot");

            if (!requestedSlotOccupied)
                return Suppress("requested-weapon-slot-empty");

            if (!validateUsageIndex)
                return Allow("occupied-weapon-slot");

            if (!usageCatalogReadable)
                return Suppress("weapon-usage-catalog-unavailable");

            if (usageCount <= 0)
                return Suppress("weapon-usage-catalog-empty");

            if (requestedUsageIndex < 0 || requestedUsageIndex >= usageCount)
            {
                return Suppress(
                    "requested-usage-index-invalid:" +
                    requestedUsageIndex +
                    ":count=" + usageCount);
            }

            return Allow("occupied-weapon-slot-and-valid-usage");
        }

        private static CoopClientWeaponRuntimeSafetyResult Allow(string reason)
        {
            return new CoopClientWeaponRuntimeSafetyResult(
                CoopClientWeaponRuntimeSafetyDecision.Allow,
                reason);
        }

        private static CoopClientWeaponRuntimeSafetyResult Suppress(string reason)
        {
            return new CoopClientWeaponRuntimeSafetyResult(
                CoopClientWeaponRuntimeSafetyDecision.Suppress,
                reason);
        }
    }
}
