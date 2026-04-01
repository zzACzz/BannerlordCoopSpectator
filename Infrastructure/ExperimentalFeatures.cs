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

        /// <summary>
        /// Replaces the native TDM team/class picker with a custom coop overlay
        /// that reads and writes the authoritative bridge files directly.
        /// </summary>
        public const bool EnableCustomCoopSelectionOverlay = true;

        /// <summary>
        /// Temporary crash-isolation flag: create the custom mission gauntlet layer
        /// without loading the CoopSelection movie. This lets us prove whether the
        /// hard crash is inside LoadMovie/prefab binding or earlier in mission view startup.
        /// </summary>
        public const bool EnableCustomCoopSelectionMovieLoad = true;

        /// <summary>
        /// Preferred runtime path for campaign encounters: open the exact
        /// singleplayer battle scene in MP Battle shell instead of remapping it
        /// to a coarse official mp_battle_map bucket.
        /// Re-enabled after dedicated exact-scene bootstrap staging proved that
        /// `SandBox`/`SandBoxCore` assets and `battle_terrain_*` path resolution
        /// are now available in the modded dedicated runtime.
        /// </summary>
        public const bool EnableDirectCampaignBattleSceneRuntime = true;

        /// <summary>
        /// Dedicated-only runtime probe for early scene resolution facts:
        /// loaded modules, owned scenes, full-path resolution, and unique-scene-id
        /// resolution for control `mp_battle_map_*` scenes and target `battle_terrain_*`
        /// scenes. Safe because it only logs and does not alter scene pairing.
        /// </summary>
        public const bool EnableDedicatedSceneContractProbe = true;

        /// <summary>
        /// Dedicated-only exact campaign scene bootstrap probe. Extends the base
        /// scene-resolution probe with runtime file availability checks,
        /// `sp_battle_scenes.xml` registry inspection, `TaleWorlds.CampaignSystem`
        /// availability checks, and a controlled pre/post
        /// `PairSceneNameToModuleName(..., "SandBoxCore")` test for `battle_terrain_*`.
        /// Intended to gather hard facts for exact-scene hosting, not to alter
        /// mission startup behavior.
        /// </summary>
        public const bool EnableDedicatedExactCampaignSceneBootstrapProbe = true;

        /// <summary>
        /// Full contract diagnostics for battle-map runtime: MissionState.OpenNew overloads,
        /// mission initializer patch state, live mission map-patch/spawn-path facts, and
        /// deployment-plan / formation-plan / scene-spawn-entry summaries.
        /// This is intentionally log-heavy and meant for diagnosis, not steady-state play.
        /// </summary>
        public const bool EnableBattleMapFullContractDiagnostics = true;

        /// <summary>
        /// Exact campaign scene bootstrap path: replace the hybrid delayed
        /// materialization layer with a native-like `MissionAgentSpawnLogic`
        /// flow backed by snapshot-driven custom troop suppliers.
        /// </summary>
        public const bool EnableExactCampaignNativeArmyBootstrap = true;

        /// <summary>
        /// Battle-map client safety switch: keep MissionLobbyEquipmentNetworkComponent
        /// enabled because native gauntlet class-loadout initialization dereferences
        /// it unconditionally during mission-screen startup.
        /// </summary>
        public const bool EnableBattleMapClientEquipmentNetworkComponent = true;
    }
}
