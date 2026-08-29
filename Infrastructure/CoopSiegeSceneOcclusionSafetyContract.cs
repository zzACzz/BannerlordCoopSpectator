using System;

namespace CoopSpectator.Infrastructure
{
    internal readonly struct CoopSiegeSceneOcclusionSafetyDecision
    {
        public CoopSiegeSceneOcclusionSafetyDecision(bool disableSceneOcclusion, string reason)
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
            bool hasMatchingPreMissionTopology,
            bool isSiegeBattle,
            string missionShell,
            string runtimeScene,
            string topologyScene)
        {
            if (!isRemoteClient)
                return KeepEnabled("not-remote-client");

            if (!hasMatchingPreMissionTopology)
                return KeepEnabled("matching-pre-mission-topology-missing");

            if (!isSiegeBattle)
                return KeepEnabled("not-siege-battle");

            if (!EqualsNormalized(missionShell, SiegeMissionWithDeploymentShell))
                return KeepEnabled("mission-shell-not-siege-with-deployment");

            if (string.IsNullOrWhiteSpace(runtimeScene) || string.IsNullOrWhiteSpace(topologyScene))
                return KeepEnabled("scene-name-missing");

            if (!EqualsNormalized(runtimeScene, topologyScene))
                return KeepEnabled("runtime-topology-scene-mismatch");

            return Disable("remote-client-exact-siege-software-occlusion-safety");
        }

        private static CoopSiegeSceneOcclusionSafetyDecision Disable(string reason)
        {
            return new CoopSiegeSceneOcclusionSafetyDecision(true, reason);
        }

        private static CoopSiegeSceneOcclusionSafetyDecision KeepEnabled(string reason)
        {
            return new CoopSiegeSceneOcclusionSafetyDecision(false, reason);
        }

        private static bool EqualsNormalized(string value, string expected)
        {
            return string.Equals(
                (value ?? string.Empty).Trim(),
                (expected ?? string.Empty).Trim(),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
