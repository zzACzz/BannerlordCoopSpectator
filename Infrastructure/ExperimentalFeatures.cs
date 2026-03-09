namespace CoopSpectator.Infrastructure
{
    /// <summary>
    /// Central feature flags for switching between the stable vanilla listed flow
    /// and the experimental custom TdmClone path.
    /// </summary>
    public static class ExperimentalFeatures
    {
        /// <summary>
        /// Stable baseline: use vanilla TeamDeathmatch in listed flow and keep the
        /// custom TdmClone game-mode path disabled until reintroduced deliberately.
        /// </summary>
        public const bool EnableTdmCloneExperiment = false;

        /// <summary>
        /// Stable reintroduction stage 1: keep vanilla TeamDeathmatch as the listed
        /// mode, but allow passive diagnostic behaviors to be appended at mission open.
        /// </summary>
        public const bool EnableVanillaTeamDeathmatchDiagnosticsInjection = true;
    }
}
