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
        /// Siege replay client isolation: keep the custom coop selection overlay
        /// out of SiegeMissionWithDeployment while native scene loading is being
        /// stabilized. Battle and village flows continue to use the shared flag.
        /// </summary>
        public const bool EnableSiegeReplayCustomCoopSelectionOverlay = true;

        /// <summary>
        /// Siege replay client isolation: keep the singleplayer formation marker
        /// Gauntlet view out of SiegeMissionWithDeployment while native scene
        /// loading is being stabilized.
        /// </summary>
        public const bool EnableSiegeReplayFormationMarkerUi = false;

        /// <summary>
        /// Siege replay client isolation: keep MissionLobbyEquipmentNetworkComponent
        /// out of SiegeMissionWithDeployment while isolating the native client
        /// crash during scene loading. Other mission flows keep their own policy.
        /// </summary>
        public const bool EnableSiegeReplayLobbyEquipmentNetworkComponent = false;

        /// <summary>
        /// Siege replay server isolation: skip the battle-flow team bootstrap
        /// during SiegeMissionWithDeployment AfterStart while isolating the
        /// native crash around Mission.Teams.Add/team synchronization.
        /// </summary>
        public const bool EnableSiegeReplayServerTeamBootstrap = false;

        /// <summary>
        /// Siege replay server diagnostics: log the low-level team-add corridor
        /// around MissionLobbyComponent.EarlyStart and Mission.Teams.Add without
        /// changing team creation or synchronization behavior.
        /// </summary>
        public static readonly bool EnableSiegeReplayTeamAddDiagnostics =
            IsEnvironmentFlagEnabled("COOPSPECTATOR_SIEGE_TEAM_ADD_DIAGNOSTICS");

        /// <summary>
        /// Siege replay server fix: create native-like attacker/defender teams
        /// before MissionLobbyComponent.EarlyStart creates the spectator team.
        /// This is scoped to SiegeMissionWithDeployment runtime only.
        /// </summary>
        public const bool EnableSiegeReplayEarlyNativeTeamBootstrap = true;

        /// <summary>
        /// Siege replay server fix: mount DefaultBattleMissionAgentSpawnLogic in
        /// the initial SiegeMissionWithDeployment stack before native deployment
        /// controllers initialize, then let the exact army bootstrap apply the
        /// native with-deployment false/false spawn contract.
        /// </summary>
        public const bool EnableSiegeReplayInitialNativeSpawnLogicBootstrap = true;

        /// <summary>
        /// Siege replay materialization reset: use the same bot-first
        /// materialized-army flow as field battles, while keeping the siege
        /// deployment plan and forcing cavalry entries onto foot.
        /// </summary>
        public const bool EnableSiegeReplayFieldMaterializedArmyRuntime = true;

        /// <summary>
        /// Exact external-siege experiment: let the native spawn logic own both the
        /// initial armies and later reinforcement waves while the coop deployment
        /// phase keeps ownership of commander selection and formation placement.
        /// </summary>
        public const bool EnableExactSiegeFullNativeArmySpawnRuntime = true;

        /// <summary>
        /// Experimental staged siege reinforcement runtime: preflight exact contracts,
        /// materialize agents incrementally, and hold the wave until every client is ready.
        /// Keep disabled while the client materialization corridor is being redesigned.
        /// </summary>
        public const bool EnableMaterializedSiegeReinforcementRuntime = false;

        /// <summary>
        /// Exact ordinary field-battle initial materialization: pace native client
        /// CreateAgent replay and require a field-specific readiness acknowledgement
        /// before battle start. Kept separate from the validated SallyOut runtime.
        /// </summary>
        public const bool EnableExactFieldBattleInitialMaterializationRuntime = true;

        /// <summary>
        /// Exact village-battle initial materialization: pace native client
        /// CreateAgent replay and require a village-specific readiness acknowledgement
        /// before battle start. State remains independent from FieldBattle and SallyOut.
        /// </summary>
        public const bool EnableExactVillageBattleInitialMaterializationRuntime = true;

        /// <summary>
        /// Exact external-siege initial materialization: pace the native client
        /// CreateAgent replay and require a siege-specific readiness acknowledgement
        /// before battle start. The native server remains the only army spawner,
        /// reinforcements remain native, and siege cavalry stays projected to foot.
        /// </summary>
        public const bool EnableExactSiegeAssaultInitialMaterializationRuntime = true;

        /// <summary>
        /// Exact SiegeMissionWithDeployment scene initializer profile: mirror the
        /// native campaign siege initializer fields that affect scene material
        /// and campaign-mode object setup, while keeping map-patch repair out of
        /// open siege scenes.
        /// </summary>
        public static readonly bool EnableExactSiegeCampaignSceneInitializerProfile = true;

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
        public static readonly bool EnableDedicatedSceneContractProbe =
            IsEnvironmentFlagEnabled("COOPSPECTATOR_DEDICATED_SCENE_CONTRACT_DIAGNOSTICS");

        /// <summary>
        /// Dedicated-only exact campaign scene bootstrap probe. Extends the base
        /// scene-resolution probe with runtime file availability checks,
        /// `sp_battle_scenes.xml` registry inspection, `TaleWorlds.CampaignSystem`
        /// availability checks, and a controlled pre/post
        /// `PairSceneNameToModuleName(..., "SandBoxCore")` test for `battle_terrain_*`.
        /// Intended to gather hard facts for exact-scene hosting, not to alter
        /// mission startup behavior.
        /// </summary>
        public static readonly bool EnableDedicatedExactCampaignSceneBootstrapProbe =
            IsEnvironmentFlagEnabled("COOPSPECTATOR_EXACT_SCENE_BOOTSTRAP_DIAGNOSTICS");

        /// <summary>
        /// Explicit investigation switch for surrogate troop names inside the
        /// custom coop selection overlay. Keep this limited to targeted manual
        /// reruns because it emits extra logs from snapshot and class-list refresh
        /// paths.
        /// </summary>
        public static readonly bool EnableBattleSelectionDisplayNameDiagnostics =
            IsEnvironmentFlagEnabled("COOPSPECTATOR_BATTLE_SELECTION_NAME_DIAGNOSTICS");

        /// <summary>
        /// Detailed selectable-entry and battle-start readiness audits emitted
        /// while entry-status snapshots are built. Disabled by default.
        /// </summary>
        public static readonly bool EnableBattleEntryStatusDiagnostics =
            IsEnvironmentFlagEnabled("COOPSPECTATOR_BATTLE_ENTRY_STATUS_DIAGNOSTICS");

        /// <summary>
        /// Full contract diagnostics for battle-map runtime: MissionState.OpenNew overloads,
        /// mission initializer patch state, live mission map-patch/spawn-path facts, and
        /// deployment-plan / formation-plan / scene-spawn-entry summaries.
        /// This is intentionally log-heavy and meant for diagnosis, not steady-state play.
        /// </summary>
        public static readonly bool EnableBattleMapFullContractDiagnostics =
            IsEnvironmentFlagEnabled("COOPSPECTATOR_BATTLE_MAP_CONTRACT_DIAGNOSTICS");

        /// <summary>
        /// Targeted mission-startup diagnostics for battle-shell lifecycle and
        /// Mission.IsLoadingFinished observations. Disabled by default because
        /// the observed property is queried from a hot loading path.
        /// </summary>
        public static readonly bool EnableBattleShellStartupDiagnostics =
            IsEnvironmentFlagEnabled("COOPSPECTATOR_BATTLE_SHELL_DIAGNOSTICS");

        /// <summary>
        /// Targeted runtime diagnostics for exact campaign army materialization.
        /// Disabled by default because it scans the live mission agent set.
        /// </summary>
        public static readonly bool EnableExactCampaignArmyRuntimeDiagnostics =
            IsEnvironmentFlagEnabled("COOPSPECTATOR_EXACT_ARMY_RUNTIME_DIAGNOSTICS");

        /// <summary>
        /// Detailed exact-agent contract trace and runtime bundle files. Disabled
        /// by default because materialized-agent events can write once per agent.
        /// </summary>
        public static readonly bool EnableExactBattleAgentContractDiagnostics =
            IsEnvironmentFlagEnabled("COOPSPECTATOR_EXACT_AGENT_CONTRACT_DIAGNOSTICS");

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
        public const bool EnableExactCampaignRuntimeObjectRegistry = false;

        /// <summary>
        /// Stable exact-loadout path: keep multiplayer-safe surrogate characters
        /// for mission/network identity, but inject snapshot equipment/body into
        /// `AgentBuildData` before native `Mission.SpawnAgent` runs. This avoids
        /// the fragile `CreateAgent -> SynchronizeAgentSpawnEquipment` repair loop
        /// while preserving the exact-native reinforcement/completion stack.
        /// </summary>
        public const bool EnableExactCampaignPreSpawnLoadoutInjection = true;

        /// <summary>
        /// Battle-map client safety switch: keep MissionLobbyEquipmentNetworkComponent
        /// enabled because native gauntlet class-loadout initialization dereferences
        /// it unconditionally during mission-screen startup.
        /// </summary>
        public const bool EnableBattleMapClientEquipmentNetworkComponent = true;

        /// <summary>
        /// Targeted hot-path diagnostics for exact SiegeAssault mission-object sync
        /// on the wrapped MP client shell. Disabled by default because it hooks
        /// `SynchronizeMissionObject` and can emit extra logs during large siege
        /// object bursts.
        /// </summary>
        public static readonly bool EnableExactSiegeMissionObjectSyncDiagnostics =
            IsEnvironmentFlagEnabled("COOPSPECTATOR_EXACT_SIEGE_SYNC_DIAGNOSTICS");

        /// <summary>
        /// Targeted hot-path diagnostics for exact CreateAgent payload comparison.
        /// Disabled by default because it scans battle rosters and emits large logs
        /// for every materialized agent.
        /// </summary>
        public static readonly bool EnableExactCreateAgentCorridorDiagnostics =
            IsEnvironmentFlagEnabled("COOPSPECTATOR_EXACT_CREATE_AGENT_DIAGNOSTICS");

        /// <summary>
        /// Targeted live AI wield-state diagnostics for exact external sieges.
        /// Disabled by default because it samples the active mission agent set.
        /// The observer is read-only and never repairs or changes agent equipment.
        /// </summary>
        public static readonly bool EnableAiWieldStateDiagnostics =
            IsEnvironmentFlagEnabled("COOPSPECTATOR_AI_WIELD_DIAGNOSTICS");

        private static bool IsEnvironmentFlagEnabled(string variableName)
        {
            return string.Equals(
                global::System.Environment.GetEnvironmentVariable(variableName),
                "1",
                global::System.StringComparison.Ordinal);
        }
    }
}
