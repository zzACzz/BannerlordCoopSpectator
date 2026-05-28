namespace CoopSpectator.Infrastructure
{
    /// <summary>
    /// Central feature flags for staged runtime cleanup and exact-transfer work.
    /// </summary>
    public static class ExperimentalFeatures
    {
        /// <summary>
        /// Keep the official vanilla listed mission shell, but wrap its mission-open
        /// behavior factory so coop-specific selection and spawn logic can attach.
        /// </summary>
        public const bool EnableVanillaMissionWrapping = true;

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
        /// Dedicated exact-scene runtime currently hosts multiplayer mission code
        /// without the singleplayer campaign object catalogs loaded. This flag
        /// enables an EditorGame-style bootstrap of `Items`,
        /// `EquipmentRosters`, `NPCCharacters`, and `SPCultures` so exact
        /// campaign roster entries can resolve their original troop ids instead
        /// of falling back to `mp_*` templates.
        /// </summary>
        public const bool EnableExactCampaignObjectCatalogBootstrap = true;

        /// <summary>
        /// Exact hero equipment parity path: when a campaign hero item id still
        /// does not direct-resolve after the generic catalog bootstrap, load the
        /// exact item xml node into `MBObjectManager` on both sides in a stable
        /// order before equipment sync. This is the low-level alternative to
        /// `compat-standin` mappings.
        /// </summary>
        public const bool EnableExactCampaignRuntimeItemRegistry = true;

        /// <summary>
        /// Multiplayer mission systems assume every spawned character belongs to
        /// an `MPHeroClass`. When exact campaign agents use original
        /// `BasicCharacterObject` ids, this flag maps them to a surrogate
        /// `MPHeroClass` for MP-only stat/visual/mission representative code,
        /// while keeping the spawned character itself unchanged.
        /// </summary>
        public const bool EnableCampaignCharacterMpHeroClassFallback = true;

        /// <summary>
        /// Pre-spawn exact roster path: snapshot entries materialize into
        /// runtime `BasicCharacterObject` / `MPHeroClass` objects before native
        /// agent creation, so `CreateAgent` carries the final name, body, and
        /// loadout instead of relying on post-spawn visual overlays.
        /// </summary>
        public const bool EnableExactCampaignRuntimeObjectRegistry = true;

        /// <summary>
        /// Stable exact-loadout path: keep multiplayer-safe surrogate characters
        /// for mission/network identity, but inject snapshot equipment/body into
        /// `AgentBuildData` before native `Mission.SpawnAgent` runs. This avoids
        /// the fragile `CreateAgent -> SynchronizeAgentSpawnEquipment` repair loop
        /// while preserving the exact-native reinforcement/completion stack.
        /// </summary>
        public const bool EnableExactCampaignPreSpawnLoadoutInjection = true;

        /// <summary>
        /// First field-battle rework slice: publish a separate canonical battle
        /// contract next to the legacy snapshot payload. This does not switch the
        /// runtime to the new server materialization path yet; it only starts the
        /// data-contract separation from `BattleDetector`.
        /// </summary>
        public const bool EnableCanonicalFieldBattleContract = true;

        /// <summary>
        /// Host-side validation hook for the new field-battle import path:
        /// after export, rebuild a live descriptor-seed index from the current
        /// campaign battle and verify that exported mission participants can be
        /// rebound back to live `MapEventSide` troops without stack heuristics.
        /// </summary>
        public const bool EnableCanonicalFieldBattleImportBridgeProbe = true;

        /// <summary>
        /// Host-side casualty replay for the canonical field-battle result path.
        /// When the dedicated result contains per-instance outcomes and the
        /// replay fully binds back to live campaign descriptors, prefer native
        /// `MapEventSide` callbacks over legacy aggregate roster patching.
        /// </summary>
        public const bool EnableCanonicalFieldBattleResultWriteback = true;
    }
}
