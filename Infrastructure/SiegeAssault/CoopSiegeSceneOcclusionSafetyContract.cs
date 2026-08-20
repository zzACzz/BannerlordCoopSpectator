using System;

namespace CoopSpectator.Infrastructure
{
    internal readonly struct CoopSiegeSceneOcclusionSafetyDecision
    {
        public CoopSiegeSceneOcclusionSafetyDecision(
            bool disableSceneOcclusion,
            string reason)
        {
            DisableSceneOcclusion = disableSceneOcclusion;
            Reason = reason ?? string.Empty;
        }

        public bool DisableSceneOcclusion { get; }

        public string Reason { get; }
    }

    internal static class CoopSiegeSceneOcclusionSafetyContract
    {
        private const string SiegeMissionWithDeploymentShell = "SiegeMissionWithDeployment";

        public static CoopSiegeSceneOcclusionSafetyDecision Resolve(
            bool isRemoteClient,
            bool isSiegeBattle,
            string missionShell,
            string siegeSubtype,
            string runtimeScene)
        {
            if (!isRemoteClient)
            {
                return KeepEnabled("not-remote-client");
            }

            if (!isSiegeBattle)
            {
                return KeepEnabled("not-siege-battle");
            }

            if (!EqualsNormalized(missionShell, SiegeMissionWithDeploymentShell))
            {
                return KeepEnabled("mission-shell-not-siege-with-deployment");
            }

            return Disable("remote-client-siege-software-occlusion-safety");
        }

        private static CoopSiegeSceneOcclusionSafetyDecision Disable(string reason)
        {
            return new CoopSiegeSceneOcclusionSafetyDecision(
                disableSceneOcclusion: true,
                reason: reason);
        }

        private static CoopSiegeSceneOcclusionSafetyDecision KeepEnabled(string reason)
        {
            return new CoopSiegeSceneOcclusionSafetyDecision(
                disableSceneOcclusion: false,
                reason: reason);
        }

        private static bool EqualsNormalized(string value, string expected)
        {
            return string.Equals(
                (value ?? string.Empty).Trim(),
                expected,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
