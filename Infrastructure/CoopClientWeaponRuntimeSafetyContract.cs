using System;
using System.Collections.Generic;

namespace CoopSpectator.Infrastructure
{
    internal static class CoopExactHeroAgentCapabilityFlagContract
    {
        public const int CanUseAllBowsMountedBit = 0x1000000;
        public const int CanReloadAllXBowsMountedBit = 0x2000000;
        public const int CanDeflectArrowsWithTwoHandedWeaponBit = 0x4000000;

        public static int ResolveDesiredFlagBits(IEnumerable<string> perkIds)
        {
            if (perkIds == null)
            {
                return 0;
            }

            int desiredFlagBits = 0;
            foreach (string perkId in perkIds)
            {
                if (string.Equals(perkId, "BowHorseMaster", StringComparison.OrdinalIgnoreCase))
                {
                    desiredFlagBits |= CanUseAllBowsMountedBit;
                }
                else if (string.Equals(perkId, "CrossbowMountedCrossbowman", StringComparison.OrdinalIgnoreCase))
                {
                    desiredFlagBits |= CanReloadAllXBowsMountedBit;
                }
                else if (string.Equals(perkId, "TwoHandedProjectileDeflection", StringComparison.OrdinalIgnoreCase))
                {
                    desiredFlagBits |= CanDeflectArrowsWithTwoHandedWeaponBit;
                }
            }

            return desiredFlagBits;
        }

        public static int MergeWithCurrentFlagBits(int currentFlagBits, IEnumerable<string> perkIds)
        {
            return currentFlagBits | ResolveDesiredFlagBits(perkIds);
        }
    }

    internal sealed class CoopPossessionCrossbowRuntimeSyncResult
    {
        public CoopPossessionCrossbowRuntimeSyncResult(
            bool shouldSynchronize,
            string reason)
        {
            ShouldSynchronize = shouldSynchronize;
            Reason = reason;
        }

        public bool ShouldSynchronize { get; private set; }
        public string Reason { get; private set; }
    }

    internal static class CoopPossessionCrossbowRuntimeSyncContract
    {
        private const int MaximumSupportedReloadPhase = 10;

        public static CoopPossessionCrossbowRuntimeSyncResult Evaluate(
            bool isServer,
            bool targetPeerActive,
            bool targetPeerRemote,
            bool agentExists,
            bool agentActive,
            bool agentHuman,
            bool exactHero,
            bool agentAiControlled,
            bool exactWeaponResolutionAvailable,
            bool mainHandMatchesResolution,
            bool mainHandIsCrossbow,
            bool compatibleAmmoAvailable,
            int chamberAmmo,
            int maximumChamberAmmo,
            int reloadPhase,
            int reloadPhaseCount)
        {
            if (!isServer)
                return Skip("not-server");
            if (!targetPeerActive)
                return Skip("target-peer-inactive");
            if (!targetPeerRemote)
                return Skip("target-peer-local-server");
            if (!agentExists)
                return Skip("agent-missing");
            if (!agentActive)
                return Skip("agent-inactive");
            if (!agentHuman)
                return Skip("agent-not-human");
            if (!exactHero)
                return Skip("entry-not-exact-hero");
            if (!agentAiControlled)
                return Skip("agent-not-ai-controlled-before-possession");
            if (!exactWeaponResolutionAvailable)
                return Skip("exact-weapon-resolution-unavailable");
            if (!mainHandMatchesResolution)
                return Skip("main-hand-does-not-match-resolution");
            if (!mainHandIsCrossbow)
                return Skip("main-hand-not-crossbow");
            if (!compatibleAmmoAvailable)
                return Skip("compatible-ammo-unavailable");
            if (chamberAmmo <= 0)
                return Skip("crossbow-chamber-empty");
            if (maximumChamberAmmo <= 0 || chamberAmmo > maximumChamberAmmo)
                return Skip("crossbow-chamber-ammo-invalid");
            if (reloadPhaseCount <= 0 || reloadPhaseCount > MaximumSupportedReloadPhase)
                return Skip("reload-phase-count-invalid");
            if (reloadPhase != reloadPhaseCount)
                return Skip("crossbow-not-at-terminal-reload-phase");

            return new CoopPossessionCrossbowRuntimeSyncResult(
                true,
                "loaded-terminal-crossbow");
        }

        private static CoopPossessionCrossbowRuntimeSyncResult Skip(string reason)
        {
            return new CoopPossessionCrossbowRuntimeSyncResult(false, reason);
        }
    }

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
