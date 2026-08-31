# Code Map

Last source verification: **2026-08-28**
Last automation-control source verification: **2026-08-31**

## Repository map

| Path | Responsibility |
|---|---|
| `SubModule.cs` | Client/campaign startup, Harmony grouping, game-mode registration, campaign behavior/model registration |
| `CoopRuntime.cs` | Process-level access to `NetworkManager` |
| `CoopSpectator.csproj` | Client/campaign compilation, conditional game-mode inclusion, client deployment, default chained dedicated build |
| `DedicatedServer/CoopSpectatorDedicated.csproj` | Explicit dedicated source graph, dedicated references, runtime staging, server deployment |
| `Campaign/` | Encounter capture, snapshot creation, dedicated launch, campaign adapters, result validation/writeback, hero creation |
| `GameMode/` | Multiplayer mode registration and native/cooperative mission behavior composition |
| `Mission/` | Live mission state owners, spawn/phase lifecycle, network transport, battle power, scenario controllers |
| `MissionModels/` | Campaign-derived combat, damage, difficulty, and morale models used in multiplayer mission runtime |
| `Infrastructure/` | Snapshot/runtime state, contracts, bridges, scenario adapters, feature flags, exact-transfer utilities, and run-scoped automation contracts |
| `Network/` | TCP prototype layer, pre-mission topology component, and network message schemas |
| `Patches/` | Harmony boundaries around native lobby, mission load, spawn, control, UI, siege, and compatibility code |
| `UI/` | Gauntlet views/view-models for selection, deployment, hero creator, hideout, battle power, and map prototype |
| `Commands/` | `coop.*` console commands and synthetic test controls |
| `DedicatedHelper/` | Dedicated launch settings/commands, hosting mode, helper launcher, web-panel authentication |
| `Module/` | Client and dedicated module descriptors, XML game data, GUI prefabs, shader-cache helper |
| `Tests/` | Standalone `net8.0` contract-test executables linked to narrow production contracts |
| `scripts/` | Development loop, release packaging, installed-DLL inventory audit, repository hygiene, and battle-test client launcher core |
| `docs/` | Dated investigations, status reports, specifications, and the living `docs/ai/` index |

Generated and investigative areas such as `bin/`, `obj/`, `.buildcheck/`, `.codex_tmp*/`, `dist/`, `work/`, copied root DLLs, ZIP packages, and decompilation scratch files are not primary source.

## Primary entry points

### `SubModule.cs`

Start here for client/campaign startup questions.

Key responsibilities and methods:

- `OnSubModuleLoad`: reset state, initialize runtime/networking, apply patches, register modes.
- `TryApplyHarmonyPatches`: client patch groups and isolation flags.
- assembly-load callback: reapplies patches whose target assembly arrived late.
- `OnGameStart`: campaign behaviors or non-campaign mission-model wrappers.
- application tick: network dispatcher, campaign map prototype, hero creation, default-off lobby automation controller, and `BattleDetector.Tick`.
- `OnSubModuleUnloaded`: shutdown and event cleanup.

Important local constants at the top of the file define which manual patch groups are active. Do not infer them from patch class existence.

### `DedicatedServer/SubModule.cs`

Start here for dedicated startup, registration, observer, or web-panel questions.

Key responsibilities:

- module-load proof logging and exact scene-script registration;
- dedicated Harmony compatibility and patch registration;
- cooperative game-mode registration and official mode overrides;
- application-tick mission classification and stable-start observation;
- delayed attachment of `CoopMissionNetworkBridge` and `CoopMissionSpawnLogic`;
- dedicated knockout outcome and shared mission-model registration;
- web-panel hooks and server lifecycle cleanup.

### `Campaign/BattleDetector.cs`

The campaign orchestration hotspot. It owns far more than detection:

- decides when a campaign encounter is eligible;
- validates supported scenario contracts;
- resolves scenario kind, siege subtype, scene, map patch, mission shell, and atmosphere;
- builds `BattleStartMessage` and the complete `BattleSnapshotMessage`;
- writes `battle_roster.json`;
- starts the helper/dedicated mission or sends a start notification;
- monitors campaign mission exit and authoritative result availability;
- validates campaign/result identity and applies casualties, hero outcomes, loot, prisoners, XP, and scenario-specific aftermath;
- prevents duplicate writeback through stable IDs and journaling.

Search by method before scanning this very large file. Useful anchors include:

- `Tick`
- `TryGetUnsupportedCoopMissionReason`
- `TrySendBattleStart`
- `TryStartDedicatedMissionForCampaignHost`
- `BuildBattleStartPayload`
- `ResolveScenarioKind`
- `ResolveSiegeSubtype`
- `TryWriteBattleRosterFile`
- result/writeback methods containing `BattleResult`, `Aftermath`, `Casualty`, `Prisoner`, or `Journal`.

### `Mission/CoopMissionBehaviors.cs`

This file contains two large top-level behavior owners:

- `CoopMissionClientLogic`: client mission lifecycle, local presentation/observer behavior, result reception, cleanup.
- `CoopMissionSpawnLogic`: authoritative battle runtime, roster entry tracking, teams, phases, selection/spawn, agent ledger, native reinforcements, battle completion, and result production.

Dedicated runtime also calls static bootstrap/observer entry points on `CoopMissionSpawnLogic`. Because this file owns many static dictionaries and scenario gates, any modification requires a full scenario and sequential-mission reset review.

Useful search anchors:

- `TryRunDedicatedMissionNetworkBootstrap`
- `TryRunDedicatedMissionObserver`
- `RunCoopBattleSpawnOwnerTick`
- `RunCoopBattlePhaseOwnerTick`
- `TryCompleteBattleIfResolved`
- `TryResolveAuthoritativeTrackedEntryId`
- `MarkInitialClientMaterializationComplete`
- methods containing `InitialMaterialization`, `Reinforcement`, `Result`, `Reset`, or `BattleEnded`.

### `Mission/CoopMissionNetworkBridge.cs`

The in-mission network and deployment hotspot. It owns:

- full battle snapshot transport V2;
- client/server message registration;
- entry-status and materialized-agent mapping transfers;
- selection, spawn, spectator, control, reconnect, and phase messages;
- initial materialization readiness for multiple scenarios;
- field/village/sally-out deployment boundary state;
- commander formation state and siege-machine state;
- siege mission-object ID maps and ladder interaction state;
- server/client readiness barriers and retry timers.

Use symbol search aggressively. Relevant method groups are clustered by message handler or scenario name, but this is not a small single-purpose class.

## Campaign adapters and aftermath

| Scenario | Campaign capture/validation | Result/aftermath |
|---|---|---|
| Field/general land | `Campaign/LandBattle/ExactLandBattleCampaignBattleAdapter.cs` | `ExactLandBattleNativeAftermathBridge.cs`, `ExactLandBattleEncounterContinuationPatch.cs` |
| Village | `Campaign/VillageBattle/ExactVillageBattleCampaignBattleAdapter.cs` | general land/native aftermath plus scenario data |
| Siege assault | `Campaign/SiegeAssault/ExactSiegeAssaultCampaignBattleAdapter.cs` | `ExactSiegeAssaultNativeAftermathRuntime.cs`, `ExactSiegeAssaultNativeAftermathCommitPatch.cs` |
| Sally out | `Campaign/SallyOut/SallyOutCampaignBattleAdapter.cs` | general result pipeline with sally-out contract data |
| Siege ambush | `Campaign/SiegeAmbush/SiegeAmbushCampaignBattleAdapter.cs` | `ExactSiegeAmbushNativeAftermathRuntime.cs` |
| Relief | `Campaign/Relief/ExactReliefCampaignBattleAdapter.cs` | scenario controller plus general writeback |
| Lords hall | `Campaign/LordsHall/LordsHallCampaignBattleAdapter.cs` | `LordsHallResultBridge.cs` |
| Day hideout | `Campaign/Hideout/HideoutCampaignBattleAdapter.cs` | `ExactHideoutNativeAftermathRuntime.cs` |
| Night hideout | `Campaign/Hideout/HideoutAmbushCampaignBattleAdapter.cs` | hideout aftermath plus ambush contract |
| Hero capture | n/a | `Campaign/Capture/ExactCampaignHeroCaptureRuntime.cs`, patch |

`BattleResultWritebackJournalBehavior` and `Infrastructure/CoopBattleResultCampaignGuardContract.cs` are cross-scenario idempotency/identity boundaries.

## Game modes and mission composition

| File | Role |
|---|---|
| `GameMode/MissionMultiplayerCoopBattleMode.cs` | General cooperative campaign battle mission opener and behavior stack |
| `GameMode/MissionMultiplayerCoopSiegeAssaultWithDeploymentMode.cs` | Native siege-with-deployment wrapper and cooperative siege stack |
| `GameMode/MissionMultiplayerCoopHideoutDayMode.cs` | Day hideout mission stack |
| `GameMode/MissionMultiplayerCoopHideoutNightMode.cs` | Night ambush mission stack |
| `GameMode/MissionMultiplayerCoopHeroCreatorMode.cs` | Hero-creation mission stack |
| `GameMode/MissionMultiplayerCoopCampaignMapPrototypeMode.cs` | Opt-in campaign map replica/prototype |
| `GameMode/MissionMultiplayerCoopTdmMode.cs` | Cooperative TDM-derived mode |
| `GameMode/MissionMultiplayerTdmCloneMode.cs` | Disabled custom TDM experiment |
| `GameMode/MissionBehaviorHelpers.cs` | Reflection/compatibility helpers for optional native behaviors |
| `GameMode/MissionBehaviorDiagnostic.cs` | Mission stack diagnostics |

Each mode usually has server/client marker classes beside its `*Mode.cs` file. The mode class builds the behaviors; the marker classes help Bannerlord registration and side-specific identification.

## Snapshot, identity, and exact transfer

| Concern | Files |
|---|---|
| Wire/domain snapshot model | `Network/Messages/BattleStartMessage.cs` |
| Snapshot line codec | `Network/Messages/BattleStartMessageCodec.cs` |
| Compact binary snapshot payload | `Infrastructure/BattleSnapshotBinarySerializer.cs` |
| Current normalized runtime projection | `Infrastructure/BattleSnapshotRuntimeState.cs` |
| Exact contract definition | `Infrastructure/ExactTransferSpawnContract.cs` |
| Contract construction | `Infrastructure/ExactTransferContractBuilder.cs` |
| Contract validation | `Infrastructure/ExactTransferContractValidator.cs` |
| Per-entry cache | `Infrastructure/ExactTransferContractRuntimeCache.cs` |
| Transfer stage state/machine | `Infrastructure/ExactTransferRuntimeState.cs`, `ExactTransferStageMachine.cs` |
| Server pre-spawn resolution | `Infrastructure/ExactCreateAgentServerPreSpawnContractResolver.cs` |
| Pre-spawn body/equipment injection | `Patches/ExactCampaignPreSpawnLoadoutPatch.cs` |
| Native/client create-agent corridor | `Patches/BattleMapSpawnHandoffPatch.cs` |
| Item/object catalogs | `ExactCampaignObjectCatalogBootstrap.cs`, `ExactCampaignRuntimeItemRegistry.cs`, `ExactCampaignRuntimeObjectRegistry.cs` |
| MP compatibility mapping | `CampaignMultiplayerHeroClassResolver.cs`, class fallback/override patches |
| Equipment/mirror policy | `CoopCampaignMirrorEquipmentResolver.cs`, `ExactWeaponSlotMaterializationPolicy.cs` |
| Mounted identity/link contract | `CoopMountedHeroMountLinkContract.cs` |

The runtime object registry file exists but its feature is disabled. Do not treat its types as the active path.

## Scenario infrastructure

### Commander deployment

`Infrastructure/CommanderDeployment/` contains shared contracts plus separate runtimes for:

- generic campaign deployment;
- ordinary field battle;
- generic land battle;
- sally out;
- village battle.

The readiness, facing-order, and commander-death handoff contracts are intentionally isolated and contract-testable.

### Field/general land

- `Infrastructure/LandBattle/ExactLandBattleScenarioContract.cs`
- `Infrastructure/LandBattle/ExactFieldBattleInitialMaterializationRuntime.cs`
- field and land commander-deployment runtimes
- field boundary calculation currently lives primarily in `CoopMissionNetworkBridge`.

### Village battle

- `ExactVillageBattleScenarioContract.cs`
- `ExactVillageBattleInitialMaterializationRuntime.cs`
- `ExactVillageBattleDeploymentBoundaryRuntime.cs`
- `ExactVillageBattleCommanderDeploymentRuntime.cs`

Village boundary data is versioned, hashed, transmitted, applied, and acknowledged separately from general materialization readiness.

### Siege assault

`Infrastructure/SiegeAssault/` contains several distinct concerns:

- `SiegeAssaultMissionOpenBridge`: selects/captures the native mission-opening profile.
- `CoopPreMissionTopologyRuntimeState`: pre-open scene/mode contract state.
- `ExactCampaignSiegeAssaultWithDeploymentRuntime`: deployment lifecycle.
- `ExactCampaignSiegeAssaultNoDeploymentRuntime`: isolated no-deployment path.
- `ExactSiegeAssaultInitialMaterializationRuntime`: client pacing/readiness for initial foot army.
- `CoopSiegeMachineDeploymentController`: machine selection, auto-deploy, native controller synchronization, detachments, and visual normalization.
- `SiegeMissionObjectIdBridge` / `SiegeMissionObjectIdMapRuntime`: stable synchronized object mapping.
- ladder interaction and merlon parity contracts/runtimes.
- peer/perk and formation-membership safety contracts.
- `CoopSiegeSceneOcclusionSafetyContract`: narrow remote-client decision for exact `SiegeMissionWithDeployment` scene occlusion.

Do not combine machine selection, unused-machine finalization, ladder state, scene-object ID mapping, initial army materialization, and reinforcement ownership into one conceptual fix. They touch different owners.

### Siege ambush

- `Infrastructure/SiegeAmbush/SiegeAmbushScenarioContract.cs`
- `ExactSiegeAmbushInitialMaterializationRuntime.cs`
- `Mission/CoopExactCampaignSiegeAmbushMissionController.cs`
- `Network/Messages/CoopSiegeAmbushOrderNetworkMessages.cs`
- `Patches/ExactSiegeAmbushDeploymentControllerPatch.cs`

Mounted rider/mount grouping is valid here; external siege assault is foot-only.

### Relief, sally out, and lords hall

- contracts: `Infrastructure/Relief/`, `Infrastructure/SallyOut/`, `Infrastructure/LordsHall/`;
- mission controllers: `CoopExactCampaignReliefMissionController`, `CoopExactCampaignLordsHallMissionController`, and `Mission/LordsHall/LordsHallMissionRuntime.cs`;
- campaign adapters are listed above.

### Hideouts

- `Infrastructure/Hideout/`: day/night scenario contracts, boss-phase/ambush rules, scene manifest, mannequin isolation.
- `Mission/`: day/night mission controllers, boss phase, ambush network controller, runtime scene manifest.
- `UI/`: stealth view, call-troops cinematic, boss cinematic/conversation, objectives, markers.
- `Patches/HideoutAmbushArrowBarrelPatch.cs`: scenario interaction patch.

## Runtime state and local bridges

### Battle/session state

- `CoopBattlePhaseRuntimeState`
- `CoopBattleAuthorityState`
- `CoopBattleSelectionIntentState` and `CoopBattleSelectionRequestState`
- `CoopBattleSpawnIntentState`, `CoopBattleSpawnRequestState`, `CoopBattleSpawnRuntimeState`
- `CoopBattlePeerLifecycleRuntimeState`
- `CoopBattlePeerReconnectState`
- `CoopBattlePeerSessionState`
- `CoopBattleAgentControlRuntimeState`

These types overlap deliberately: intent, authoritative request, spawn result, lifecycle, reconnect, and composed UI/session state are different layers. Before adding another flag, locate the existing owner.

### Bridge files

- roster: `Campaign/BattleRosterFile.cs`
- result: `Infrastructure/CoopBattleResultBridgeFile.cs`
- phase: `CoopBattlePhaseBridgeFile.cs`
- entry status: `CoopBattleEntryStatusBridgeFile.cs`
- selection: `CoopBattleSelectionBridgeFile.cs`
- spawn requests: `CoopBattleSpawnBridgeFile.cs`
- hero creation: `CoopHeroCreationBridgeFile.cs`
- campaign map prototype: `CoopCampaignMapPrototypeBridgeFile.cs`
- synthetic role matrix: `CoopRoleMatrixStreamBridgeFile.cs`
- exact diagnostic artifacts: `ExactBattleAgentSpawnTraceBridgeFile.cs`, `ExactBattleEntryCompatibilityBridgeFile.cs`, `ExactBattleRuntimeBundleBridgeFile.cs`
- shared I/O: `AtomicBridgeFileIO.cs`
- temporary local-host redirect state: `HostSelfJoinRedirectState.cs`

### Battle-test client join

- `Infrastructure/Automation/CoopAutomationJoinContract.cs`: schema, request validation, SHA-256 binding, exact server selection, and terminal-state rules.
- `Infrastructure/Automation/CoopAutomationJoinBridge.cs`: complete environment/profile validation, loaded-assembly hash, run-scoped request read, and strict atomic status publication.
- `Multiplayer/Automation/CoopLobbyAutomationDriver.cs`: narrow reflection adapter over TaleWorlds `NetworkMain.GameClient`, custom-server discovery, and custom-game join.
- `Multiplayer/Automation/CoopLobbyAutomationController.cs`: main-thread polling, exact local-server ownership gate, native join lifecycle, acknowledgement, and safe cancellation boundary.
- `Commands/CoopAutomationConsoleCommands.cs`: `coop.automation_join` status/start/cancel surface.
- `Patches/LobbyCustomGameLocalJoinPatch.cs`: existing normal lobby handoff and loopback-rewrite boundary; now also notifies the run-scoped controller.

This path is disabled by default, is independent of battle type, and stops at network connection evidence. It does not open a mission or own battle readiness/result state.

### Battle-test automation foundation

- `Directory.Build.props`: shared default-false compile-only output/intermediate/package routing.
- `Infrastructure/Automation/CoopAutomationRunContract.cs`: manifest, role instance, port/fixture identity, lease, envelope, event, stable outcome/reason, recovery, and known-issue contracts.
- `Infrastructure/Automation/CoopAutomationProtocolFileIO.cs`: strict atomic JSON, bounded shared reads, append-safe JSONL, and same-volume inbox processing.
- `Infrastructure/Automation/CoopAutomationRuntimeContract.cs`: strict runtime profile, loaded-role identity, `Suppress` result policy, and run-scoped owned-host validation.
- `Infrastructure/Automation/CoopAutomationRuntimeBridge.cs`: loaded assembly hashing, role status publication, result-policy resolution, and exact live owned-host confirmation.
- `scripts/Invoke-CoopTest.ps1`: `Doctor`, full `Contracts`, client/dedicated `CompileOnly`, runtime `Feasibility`, existing-run `Inspect`, and opt-in exact `Recover`; owns the run root, runner lock, nonce fingerprint, lease, status, events, assertions, provisional-to-verified process identities, cleanup, logs, and reports. A dedicated launch is provisionally inventoried before bounded exact path/start/parent enrichment, and enrichment failure is an internal runner outcome that does not bypass cleanup.
- `scripts/CoopAutomationRunner.Core.ps1`: deterministic dedicated bootstrap commands, bounded process observation with validated `Win32_Process` path fallback, provisional/verified identity matching, PID-reuse-resistant exact cleanup, stdout/stderr capture, and pure in-memory descendant discovery from one bounded process snapshot.
- `scripts/Start-CoopBattleTestClient.ps1`: standalone validation plus aggregate-owned live launch through `-UseExistingRunContract`; inherits the runner token/root/hash/result policy, records immediate provisional PID/path/parent/window ownership, performs bounded exact identity observation, publishes the schema-v3 final handoff, and exactly cleans pre-handoff failures.
- `Tests/contract-tests.manifest.json`: exact reviewed inventory consumed by the aggregate runner.

This layer remains non-authoritative for campaign and battle state. Milestone 2A stays non-runtime. Milestone 2B.1 adds source-prepared process/role ownership and a bounded connectivity probe that may create only a vanilla `TeamDeathmatch` bootstrap after fail-closed result isolation; it does not stage modules, load a campaign fixture, advance cooperative battle phases, publish campaign-consumable results, or claim L2/L3 evidence.

## Network files

### Transport components

- `Network/NetworkManager.cs`: TCP server/client role and message dispatch.
- `Network/TcpClientConnection.cs`: connection lifecycle and line receive loop.
- `Network/TcpLineProtocol.cs`: framing/encoding.
- `Network/CoopPreMissionTopologyNetworkComponent.cs`: global pre-mission Bannerlord component.

### Message groups

| File | Messages |
|---|---|
| `BattleStartMessage.cs` | Complete campaign battle domain model |
| `BattleStartMessageCodec.cs` | TCP/start payload codec |
| `CoopPreMissionTopologyNetworkMessages.cs` | Pre-open compact scene/mode/scenario contract |
| `CoopBattleSelectionNetworkMessages.cs` | Snapshot chunks/control, entry status, selection, phase, materialization, deployment, siege state |
| `CoopBattleAgentControlNetworkMessages.cs` | Controlled-agent/AI authority state |
| `CoopBattlePowerNetworkMessages.cs` | Side power/casualty comparison |
| `CoopSiegeAssaultOrderNetworkMessages.cs` | Siege assault formation/player orders |
| `CoopSiegeAmbushOrderNetworkMessages.cs` | Siege ambush orders |
| `CoopHideoutBossPhaseNetworkMessages.cs` | Hideout boss-stage state |
| `CoopHideoutAmbushNetworkMessages.cs` | Night ambush state/interactions |
| `CoopHeroCreationNetworkMessages.cs` | Hero draft/progress/result transport |
| `CoopCampaignMapPrototypeNetworkMessages.cs` | Prototype host state |
| `CoopCampaignMapReplicaNetworkMessages.cs` | Replica/catalog state |
| `HostGameState.cs` / codec | TCP host state |
| `NetworkMessagePrefixes.cs` | Stable message prefixes/constants |

`CoopBattleSelectionNetworkMessages.cs` is broad and should eventually be split by protocol concern, but that refactor would affect registration and wire compatibility and must not be incidental.

## Harmony patch map

### Mission open and topology

- `PreMissionTopologyContractPatch`
- `MissionStateOpenNewPatches`
- `BattleShellSuppressionPatch`
- `SiegeMissionGameTypeAliasPatch`
- `FinishedLoadingMissionReadyGatePatch`
- `LateJoinPeerBootstrapGatePatch` (code present; dangerous hooks disabled by startup flags)

### Exact spawn and network compatibility

- `ExactCampaignArmyBootstrapPatch`
- `ExactCampaignNetworkObjectBootstrapPatch`
- `ExactCampaignPreSpawnLoadoutPatch`
- `BattleMapSpawnHandoffPatch`
- `ClientChangeCultureCanonicalizationPatch`
- `ServerChangeCultureCanonicalizationPatch`
- `MultiplayerCharacterClassFallbackPatch`
- `MultiplayerHeroClassOverridePatch`
- `CampaignCombatProfileAgentStatsPatch`
- `CoopBattleDisplayNameConsumerPatch`

### Mission safety and control

- `CampaignlessConversationMissionSafetyPatch`
- `MissionItemUsageSetFlagsGuardPatch`
- `CoopNetworkSafeAgentBlowPatch`
- `CoopBotsControlledCountPatch`
- `CoopMissionLobbySpawnPeriodGuardPatch`
- `CommanderDeploymentMissionNetworkComponentPatch`
- `CoopBattleEscapeMenuAiControlPatch`

`CampaignlessConversationMissionSafetyPatch` targets the native `ConversationMission.OneToOneConversationAgent` getter. Its pure `CampaignlessConversationMissionSafetyContract` allows the original getter when both campaign and conversation manager exist and returns a null result only when that campaign state is unavailable.

### Siege-specific

- `CoopSiegeFormationMembershipSafetyPatch`
- `CoopSiegeLadderInteractionPatch`
- `ExactSiegeLadderMerlonVisualParityPatch`
- `ExactSiegeAmbushDeploymentControllerPatch`
- `ExactSiegeStageAdvancePatch`
- `OrderOfBattleSiegeProjectedCountsPatch`

### Lobby/UI/diagnostics

- game-mode override and lobby join patches;
- entry/HUD suppression patches;
- mission camera preview patch;
- optional character tableau/visual diagnostics patches;
- campaign map prototype scene-load patch.

When diagnosing a patch, inspect both `Apply` targeting and the startup flag/call site. A patch class can exist but be inactive, partially applied, or reapplied after assembly load.

## UI and assets

Primary views/view-models:

- `CoopMissionSelectionView` and `CoopSelectionShellViewModels`: side/unit/spawn/deployment shell;
- `CoopSiegeMachineDeploymentVM` and `CoopSiegeOrderOfBattleVM`: siege deployment and projected counts;
- `CoopBattlePowerScoreView`, `CoopBattleAiControlHintVM`: battle HUD additions;
- hero creator view/model/culture selection;
- day/night hideout objective, stealth, cinematic, conversation, and marker views;
- `CoopCampaignMapPrototypeMissionView`, party visual, overlays, and replica info. The mission view collects meshes tagged `ticked_map_entity`, calls `MBMapScene.TickVisuals` while the map scene is render-ready, and clears those cached arrays during scene release.

Gauntlet prefabs live in `Module/CoopSpectator/GUI/Prefabs/`. When changing a view-model property or command, search the matching XML binding. `CoopSelection.xml` and `CoopCommanderDeployment.xml` are primary selection/deployment assets.

Game data under `Module/CoopSpectator/ModuleData/` defines custom/mirror items, crafting pieces, multiplayer characters/classes, and strings. Exact equipment resolution may depend on both snapshot data and these stable XML registrations.

## Console commands

All commands use the `coop` prefix.

### Connectivity and launch

From `CoopConsoleCommands`:

- `coop.host`
- `coop.join`
- `coop.send`
- `coop.status`
- `coop.test_mp_launch`
- `coop.test_mp_server`
- `coop.test_mp_mission`
- `coop.test_mp_join`
- `coop.dedicated_start`
- `coop.dedicated_start_vpn`
- `coop.dedicated_open_tokens`
- `coop.test_mp_team`
- `coop.automation_join status`
- `coop.automation_join start <RunId>`
- `coop.automation_join cancel`

### Selection and spawn

From `CoopBattleSelectionConsoleCommands`:

- `coop.select_side`
- `coop.select_troop`
- `coop.spawn_now`
- `coop.force_respawnable`
- `coop.entry_status`
- `coop.side_options`
- `coop.select_side_index`
- `coop.troop_options`
- `coop.select_troop_index`
- side-scoped troop variants and `coop.entry_menu`.

### Phase and synthetic scenarios

- `coop.phase_status`
- `coop.start_battle`
- `coop.test_campaign_roster`
- `coop.test_hero_roster`
- `coop.test_battle`
- campaign map prototype console command(s) in `CoopCampaignMapPrototypeConsoleCommands`.

Console commands often write bridge requests that the authoritative mission consumes. They do not bypass mission authority.

## Contract tests

Each project is a small `net8.0` executable. Most link a narrow production contract directly and avoid loading Bannerlord runtime.

| Test project | Main contract area |
|---|---|
| `CampaignlessConversationMissionSafety.ContractTests` | Campaign-null conversation safety |
| `CoopBattlePower.ContractTests` | Power and multiplayer HUD calculations |
| `CoopBattleResultCampaignGuard.ContractTests` | Campaign/result ID validation and journaling |
| `CoopBattleResultReadCache.ContractTests` | Stable file-stamp cache semantics |
| `CoopBattleStartup.ContractTests` | Startup, exact hero/equipment, commander, mount, relief, and safety contracts |
| `CoopCampaignMapPrototype.ContractTests` | Map codec, chunk/catalog, bounds and scene rules |
| `CoopExactCampaignSiegePeerPerkSafety.ContractTests` | Hero-class and perk safety on dedicated siege |
| `CoopExactSiegeLadderMerlonVisualParity.ContractTests` | Remote ladder/merlon visibility decisions |
| `CoopHeroBattleProgression.ContractTests` | Weapon identity, XP, ammo/perk, fatal-event matching |
| `CoopHeroCreation.ContractTests` | Draft budgets, chunks, progress, and state machine |
| `CoopHideoutBossPhase.ContractTests` | Day/night hideout and boss/ambush rules |
| `CoopRemoteSiegeOcclusionSafety.ContractTests` | Remote client occlusion safety |
| `CoopShaderCacheModeSwitch.ContractTests` | Module PowerShell wrapper state changes |
| `CoopSiegeFormationMembershipSafety.ContractTests` | Stored formation membership/coordinates |
| `CoopSiegeLadderInteraction.ContractTests` | Ladder interaction mutation guards |
| `CoopSiegeSceneScriptRegistration.ContractTests` | SandBox managed scene-script registration |
| `CoopAutomationJoin.ContractTests` | Run/token/hash request validation, exact server selection, atomic status, and automation source-graph compilation |
| `CoopAutomationProtocol.ContractTests` | Protocol compatibility, role/run/nonce ordering, lease/recovery, stable outcomes/reasons, known issues, atomic/append file faults, locks, and repeat reads |
| `CoopAutomationRuntime.ContractTests` | Strict runtime profile, loaded-role hash status, fail-closed result suppression, and exact live owned-host validation |
| `CoopCompileOnly.ContractTests` | Shared compile-only property, output routing, and deployment-target guards in both main project files |
| `NativeAftermath.ContractTests` | Casualty math, staged siege totals, hideout/final siege aftermath |

These tests prove contract logic only. They do not prove native mission loading, synchronized network ordering, or visual/runtime stability.

## Scripts

- `scripts/CoopDevLoop.ps1`: optional client/dedicated builds, process restart/launch, DLL timestamp checks, and log-marker scanning. With no action switches it builds both and checks logs. Its default `ProjectRoot` points to `C:\dev\projects\BannerlordCoopSpectator3`, not necessarily the active Codex worktree; pass `-ProjectRoot` explicitly.
- `scripts/CreateReleasePackage.ps1`: Release builds unless `-SkipBuild`, then recreates selected `dist/` artifacts. `-GitHubAssetsOnly` creates full GitHub client/host archives, `-NexusAssetsOnly` derives validated Nexus client/HostLite archives from existing GitHub archives, and `-ReleaseAssetsOnly` creates both sets. Nexus validation excludes BAT/PS1/PDB files and compares every retained payload entry and embedded release document by SHA-256. See [RELEASE_PACKAGING.md](RELEASE_PACKAGING.md) for the canonical layouts and workflow.
- `scripts/DllInventoryAudit.ps1`: scans installed client/dedicated trees and writes repository reports/CSV. Its historical conclusion may not match the current installed version.
- `scripts/Test-RepositoryHygiene.ps1`: read-only validation of repository-local Git EOL configuration, tracked text EOL state, mixed endings, and optional final working-tree cleanliness.
- `scripts/Invoke-CoopTest.ps1`: canonical fresh-root L0/L1 runner for environment facts, all reviewed contract projects, and side-effect-free main-project compilation.
- `scripts/Start-CoopBattleTestClient.ps1`: validates Steam, exact installed client hash, and run identity; `-ValidateOnly` performs no launch/run-root write; an aggregate-owned live call writes only the selected temporary run root, provisionally owns and verifies the exact multiplayer executable, and cleans any failure before final handoff.
- `run_battle_test_client.bat`: historical standalone wrapper for the PowerShell client launcher. Current source is validation-only for this route; live automation must inherit exact server ownership from aggregate `Feasibility`. The password remains environment-only and is not a positional argument.

## High-risk hotspots

These are high risk because of responsibility breadth and native/runtime sensitivity, not merely file length:

1. `Mission/CoopMissionBehaviors.cs`: mission lifecycle, static state, spawn, phases, result.
2. `Patches/BattleMapSpawnHandoffPatch.cs`: native `CreateAgent` interception and replay.
3. `Mission/CoopMissionNetworkBridge.cs`: protocol, retries, acknowledgements, deployment, siege objects.
4. `Campaign/BattleDetector.cs`: capture plus all scenario routing and campaign aftermath.
5. `Infrastructure/ExactCampaignArmyBootstrap.cs`: native supplier/bootstrap boundary.
6. `Infrastructure/SiegeAssault/CoopSiegeMachineDeploymentController.cs`: native siege controller state, detachments, visibility, and AI-sensitive machines.
7. `GameMode/MissionMultiplayerCoop*Mode.cs`: exact native behavior-stack composition and ordering.
8. `SubModule.cs` and `DedicatedServer/SubModule.cs`: patch registration differences and late-load behavior.
9. `UI/CoopMissionSelectionView.cs`: broad player-facing selection/deployment state integration.

## Search recipes

Run from repository root:

```powershell
# Find a type or method without searching generated output.
rg -n --glob '*.cs' --glob '!bin/**' --glob '!obj/**' --glob '!dist/**' 'SymbolName'

# Find every runtime feature flag and its use sites.
rg -n --glob '*.cs' 'ExperimentalFeatures\.|COOPSPECTATOR_'

# Find a network message definition and all handlers/senders.
rg -n --glob '*.cs' 'CoopSomeMessage'

# Find mission-state cleanup for a subsystem.
rg -n --glob '*.cs' 'Reset|Clear|OnEndMission|OnRemoveBehavior' Mission Infrastructure Patches

# Find scenario routing rather than guessing from file names.
rg -n --glob '*.cs' 'ScenarioKind|SiegeSubtype|IsCampaignBattle|TryValidateActiveMission'

# Find console command names.
rg -n --glob '*.cs' 'CommandLineArgumentFunction' Commands
```

Prefer symbol-first reading over opening the largest files at line 1.
