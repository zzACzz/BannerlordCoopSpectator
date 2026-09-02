# Architecture

Last source verification: **2026-08-28**
Last automation-control source verification: **2026-09-02**
Last automation-control live verification: **2026-09-01**

## System objective

`BannerlordCoopSpectator3` projects a live single-player campaign encounter into a multiplayer mission without surrendering the campaign's exact identity and aftermath rules. The campaign host captures the encounter, a dedicated server simulates the mission authoritatively, remote clients join the exact scene safely, and the result is applied back to the original campaign exactly once.

The implementation deliberately combines native Bannerlord systems with narrow cooperative adapters. It does not implement a replacement engine, a fully replicated campaign, or a second custom agent-spawn simulation.

## Runtime roles and authority

| Role | Owns | Must not own |
|---|---|---|
| Campaign host | Campaign state, encounter detection, battle snapshot construction, dedicated launch/notification, final campaign writeback | Authoritative remote mission simulation after handoff |
| Dedicated server | Mission topology, teams, native physical spawns, AI, combat, deployment, phases, control, casualties, final result | Campaign mutation |
| Remote client | Safe scene loading, local rendering, UI, player intent, acknowledged materialization, controlled-agent presentation | Physical spawn authority, campaign truth, result authority |
| Local file bridges | Same-machine coordination, command requests, status, result exchange, diagnostics | Network truth for remote mission peers |
| TCP prototype layer | Host/listener state and campaign-level start notifications | Mission agent/state synchronization |
| Bannerlord mission network | Pre-mission contract and in-mission authoritative messages | Direct campaign persistence |

## High-level component graph

```text
SubModule.cs (client/campaign startup)
  |-- CoopRuntime / NetworkManager -------------------- TCP host-state/start channel
  |-- BattleDetector --------------------------------- capture + launch + writeback
  |-- CoopLobbyAutomationController ------------------ default-off run-scoped lobby intent
  |-- Harmony patches -------------------------------- native compatibility boundaries
  |-- campaign behaviors ----------------------------- dispatcher/journal/hero/map prototype
  `-- optional game-mode registration

DedicatedServer/SubModule.cs
  |-- game-mode registration and official Battle override
  |-- exact-scene and runtime safety patches
  |-- delayed mission observer ----------------------- attach after native mission stabilizes
  `-- CoopMissionSpawnLogic + CoopMissionNetworkBridge

Mission mode
  |-- native multiplayer lobby/battle/deployment behaviors
  |-- CoopMissionNetworkBridge ----------------------- authoritative mission transport
  |-- CoopMissionSpawnLogic -------------------------- server runtime owner
  |-- scenario-specific runtime/controller
  |-- CoopBattlePowerNetworkController
  `-- client views/selection UI

BattleSnapshotMessage
  |-- scene/topology/atmosphere
  |-- sides -> parties -> ordered troop entries
  |-- identity/body/equipment/mount/combat profile/perks
  |-- siege context and engine state
  `-- campaign binding and casualty policy
```

## Startup composition

### Client and campaign assembly

`SubModule.cs` is the client/campaign entry point. On load it:

- resets exact-runtime process state;
- initializes `CoopRuntime.Network` and `BattleDetector`;
- subscribes network handlers;
- applies attribute and manually grouped Harmony patches;
- registers available multiplayer modes when `HAS_GAMEMODE` is compiled;
- installs assembly-load reapplication for targets loaded after the module.

On campaign start it registers:

- `HostStateBroadcaster`;
- `SpectatorStateReceiver`;
- `ClientBattleNotification`;
- `MainThreadDispatcherPumpBehavior`;
- `BattleResultWritebackJournalBehavior`;
- `PlayerHeroCreationCampaignBehavior`;
- the campaign-map prototype publisher only when explicitly enabled.

For non-campaign games it installs cooperative wrappers for agent stats, strike magnitude, damage application, mission difficulty, and battle morale.

Notable client patch state:

- main `BattleShellSuppressionPatch` application is disabled because the broad mission-loading interception caused native access violations;
- diagnostics-only siege client loading hooks remain enabled;
- the finished-loading readiness gate is enabled;
- experimental late-join hooks for `HandleLateNewClientAfterLoadingFinished`, `SendAgentsToPeer`, and `SendMissilesToPeer` are disabled;
- exact bootstrap, pre-spawn loadout, network-object bootstrap, culture/class compatibility, and display-name consumers are enabled.

### Dedicated assembly

`DedicatedServer/SubModule.cs` is compiled only by the dedicated project. It:

- registers cooperative modes and overrides official `Battle` with `CoopBattle`;
- applies dedicated-compatible Harmony patches;
- registers SandBox scene-script types needed by exact campaign scenes;
- probes scene/runtime state when diagnostic flags are enabled;
- keeps a mission observer that waits for a stable native mission before attaching cooperative behavior owners;
- installs the same five mission-model wrappers used by the client assembly;
- integrates with the dedicated web-panel path.

The dedicated observer skips normal battle startup for hero creator and campaign-map prototype modes. For a battle mission it can attach `CoopMissionNetworkBridge` and `CoopMissionSpawnLogic` dynamically, then run their dedicated bootstrap/observer ticks.

The dedicated build enables the narrowed battle-shell suppression behavior, but the dangerous manual mission-load bypass remains disabled inside the patch. This is not equivalent to the disabled broad client hook.

## Mission-mode architecture

### General cooperative battle

`MissionMultiplayerCoopBattleMode` is the primary mode for field, village, sally-out, hideout-adjacent land, and other campaign-derived battle shells.

It chooses the native multiplayer shell by scene context:

- campaign/battle-map scene: `MultiplayerBattle`;
- non-battle-map fallback: `MultiplayerTeamDeathmatch`.

It resolves `SiegeAssaultMissionOpenBridge` before opening the mission. Exact siege assault with deployment is delegated to `MissionMultiplayerCoopSiegeAssaultWithDeploymentMode`.

The server behavior set is native-first: lobby, custom battle behavior, timer, team selection, mission boundaries, polls/admin, optional native helpers, cooperative network bridge and battle-power controller. `CoopMissionSpawnLogic` is deferred on dedicated runtime so the observer can attach it after mission stabilization; it can be inserted directly outside that path.

The client behavior set adds native client battle/visual/equipment/team/boundary helpers plus cooperative network/power behaviors and views. Battle-map clients intentionally keep `MissionLobbyEquipmentNetworkComponent`, because native Gauntlet loadout initialization dereferences it.

Scenario-specific commander deployment support is appended for field, village, sally-out, and generic land contracts.

### Siege assault with deployment

`MissionMultiplayerCoopSiegeAssaultWithDeploymentMode` wraps native `SiegeMissionWithDeployment` behavior rather than simulating siege deployment from scratch.

The stack includes native warmup, battle timer, siege interaction, siege spawn, team selection, boundaries, siege engines, deployment handlers/controllers, and cooperative runtime layers. On the client, the siege lobby equipment component and single-player formation-marker UI remain disabled by feature policy, while the custom cooperative selection overlay remains enabled.

The server remains the only physical spawner. Initial siege client materialization is paced and acknowledged; later reinforcement waves remain native. The old staged custom reinforcement system is present but disabled.

### Isolated modes

- Day and night hideouts use separate modes, contracts, controllers, and scene manifests. The night path adds ambush signaling and phase control.
- Hero creation uses a dedicated mission network and a file-backed campaign request/result bridge.
- The campaign map prototype has isolated network, rendering, bridge, and scene-load state and is disabled by default.
- `TdmClone` code remains for experimentation, but its feature switch is off.

## The battle snapshot

`BattleStartMessage` carries launch metadata and a `BattleSnapshotMessage`. The snapshot is the cross-process logical description of the encounter.

Top-level data includes:

- `BattleId`, `InstanceId`, campaign-binding version, campaign ID, and casualty rules;
- exact scene, map patch, terrain direction, mission shell, battle size, corpse/mount/wave limits;
- scenario kind and siege subtype/context;
- time of day, weather, atmosphere, difficulty, and player side;
- crafted weapon definitions;
- attacker/defender sides and frozen captain data.

Each side contains culture, banner, appearance, morale, parties, and mission-ready entry order. Each party carries campaign identity, role/modifier/perk data, and troop entries. Each troop entry can carry:

- stable entry/party/side IDs and count/wounded state;
- original campaign character and multiplayer-safe spawn-template IDs;
- hero identity, name, body, age, gender, culture, banner, and combat profile;
- complete equipment slots, modifiers, amounts, crafted-item keys, and mount data;
- server pre-spawn contract flags and perk information.

`BattleSnapshotRuntimeState.SetCurrent` normalizes the snapshot, builds lookup projections, annotates spawn contracts, and exposes the current in-memory view to mission systems.

## Communication architecture

### TCP coordination layer

`NetworkManager`, `TcpClientConnection`, and `TcpLineProtocol` provide a small line-based TCP channel. Campaign behaviors use it for host state and start notification. Work scheduled from the network thread is marshalled through `MainThreadDispatcher`.

This layer predates the full mission transport. Avoid extending it with agent-level or mission-authority state.

### Pre-mission topology layer

`CoopPreMissionTopologyNetworkComponent` is inserted before the base network component. The server advertises a compact contract containing the scene/mode/scenario facts required to decide whether a client may open the mission.

The client defers native `LoadMission` or `InitializeCustomGame` until a matching contract activates. A mismatch or ten-second timeout triggers a controlled abort instead of opening a potentially incompatible campaign scene.

`CoopPreMissionTopologyRuntimeState` owns the active contract, schema/hash validation, a five-minute contract lifetime, and scene/battle-index matching. `PendingBattleMissionStartupState` carries scenario context across the deferred open boundary.

### In-mission layer

`CoopMissionNetworkBridge` owns the main Bannerlord mission-message protocol. It transports:

- the full battle snapshot;
- entry availability/status;
- authoritative materialized agent-index to entry-ID mappings;
- player selection, spawn, lifecycle, and control state;
- battle phase and battle power;
- commander formation/deployment state;
- siege-machine, siege-object-ID, ladder, and boundary state;
- scenario-specific initial materialization readiness;
- reconnect-finalization readiness;
- the disabled experimental materialized-reinforcement batch protocol.

Full snapshot transport V2 is always selected in current source. It uses schema version 1, a manifest, compressed chunks, an initial four-chunk window, at most eight in-flight chunks per peer, range acknowledgements every four new chunks, retry timers, a completion acknowledgement, integrity checks, and abort messages.

The full snapshot is validated against the already accepted pre-mission contract. A mismatch is terminal for the mission; it is not silently repaired.

### File bridges

Most persistent bridge files are stored below:

`Documents\Mount and Blade II Bannerlord\CoopSpectator`

Key files:

| File | Direction/purpose |
|---|---|
| `battle_roster.json` | Campaign host to dedicated initial battle payload |
| `battle_result.json` | Dedicated authoritative result to campaign host |
| `battle_entry_status.txt` | Current entry availability/status |
| `battle_phase_status.txt` | Current battle phase/status |
| `battle_phase_start.request` | Local start-battle request |
| `battle_select_side.request` | Local side selection request |
| `battle_select_troop.request` | Local troop/entry selection request |
| `battle_select_spectator.request` | Local spectator request |
| `battle_selection_current.txt` | Current local selection projection |
| `battle_spawn_now.request` | Local immediate-spawn request |
| `battle_force_respawnable.request` | Local respawn-state request |
| `hero_creation_request.json` / `result.json` / `progress.json` | Campaign and hero-creator mission exchange |
| `campaign_map_prototype_*.txt` | Prototype host/catalog/dynamic map state |
| `role_matrix_stream_progress.txt` / `role_matrix_unsafe.csv` | Synthetic role-matrix tooling |
| `battle_agent_spawn_trace.txt` | Opt-in exact spawn trace |
| `battle_entry_compatibility.txt` | Exact entry compatibility report |
| `battle_runtime_bundle.txt` | Diagnostic bundle/path manifest |

`HostSelfJoinRedirectState` uses temporary marker files instead of the Documents bridge folder.

The initial battle-test client-control bridge is deliberately separate from production battle bridges. It uses `%TEMP%\CoopSpectator\Automation\<RunId>\commands\client-join.request.json` and `state\client-join.status.json`, binds the intent to a token hash and loaded module hash, and writes status with strict atomic replacement. It is orchestration state only: it requests a normal lobby join but does not own network truth, battle readiness, mission phases, or results.

`AtomicBridgeFileIO` is the common safe-write/read helper. Preserve stable-read and atomic-write semantics for JSON/status bridges; partial reads can corrupt startup or writeback decisions.

### Battle-test automation control plane

Milestone 2A adds an isolated non-runtime control plane below `%TEMP%\CoopSpectator\Automation\<RunId>`. It does not replace any campaign, dedicated, mission-network, battle-phase, spawn, result, or writeback authority.

`scripts/Invoke-CoopTest.ps1` is the only current general runner entry point. For non-runtime `Doctor`, `Contracts`, and `CompileOnly`, source-prepared runtime `Feasibility`, `Inspect`, and `Recover`, and exact-run `Cancel`, it owns:

- a fresh exact run root and exclusive runner lock;
- manifest schema 1, protocol 1.1 capability gates, and backward-readable protocol 1.0 requests;
- a random nonce whose SHA-256 fingerprint, never plaintext, is persisted;
- the `Runner/runner-01` role instance, process identity, capabilities, lease/heartbeat, atomic status, and ordered events;
- categorized artifacts, assertion records, stable terminal outcomes, and non-pass reproduction metadata.

`CoopAutomationRunContract` defines the cross-role identity and compatibility model, while `CoopAutomationProtocolFileIO` defines the verified local same-volume file semantics. Protocol 1.1 adds explicit `RoleHealthV1`, `CancellationV1`, `RecoveryV2`, and `FailureEvidenceV1` capability negotiation without reinterpreting historical 1.0 payloads. The client-join request retains schema 3 compatibility with exact `Runner/runner-01 -> MultiplayerClient/multiplayer-client-01` identity and explicit native platform-login evidence.

Milestone 2B.1 adds `CoopAutomationRuntimeContract` and `CoopAutomationRuntimeBridge`. An explicitly enabled role must validate the exact run root, token, expected loaded assembly SHA-256, and `Suppress` result policy before publishing `ModuleReady`. The aggregate runner records a local server only after verifying that the requested UDP endpoint belongs to the exact dedicated process or its descendant. The client then revalidates a token-bound run-scoped owner record, live PID/path/start time, and UDP endpoint instead of trusting or mutating the production host marker.

The dedicated server does not bind/list the local endpoint before native `start_game`. Therefore the external `Feasibility` runner may authorize a minimum vanilla `TeamDeathmatch` bootstrap only after dedicated loaded-hash and result-suppression gates plus an authoritative readiness acknowledgement. The clean `70a40db` rerun proved that `ModuleReady` is published from `OnSubModuleLoad` before the native `IGameNetworkHandler.OnHandleConsoleCommand` path is ready. Revision 10 then established immediate provisional process ownership, bounded exact identity promotion, and exact cleanup.

Clean published-revision run `m2b2c-client-handoff-live-20260831-01` live-verified that ownership/cleanup path but disproved the next transport assumption: the starter created or rebound a native console while redirected stdout/stderr remained empty, and its PID-correlated `rgl_log` did not contain the `Console.WriteLine` readiness text.

Milestone 2B.2D adds `CoopAutomationDedicatedControlContract` and `CoopAutomationDedicatedControlBridge`. The bridge observes the public `InitialListedGameServerState.OnActivated` lifecycle event, publishes run/token/hash/process-bound readiness, claims one fixed allowlisted bootstrap request atomically, invokes `GameNetwork.HandleConsoleCommand` from the dedicated main tick, and verifies native option, usable-map, and listed-server state before publishing seven ordered acknowledgements. Redirected standard handles and native logs are supplementary only. Run `m2b2d-live-feasibility-20260901-01` runtime-confirmed dedicated SHA-256 `BD328AAC4F2A64C28D3EDCE28BCE3D72FF164BDAF817D9460B302ED538702A78`, all seven acknowledgements, native `start_game`, UDP ownership, real client launch, and the client loaded hash. Revision 12 corrects the aggregate UTC/process-handoff defect exposed before lobby connection. Clean rerun `m2d-live-r2-01` then confirmed that correction plus graceful exact cleanup and exposed the next source boundary: `TaleWorlds.MountAndBlade.NetworkMain` is defined by `TaleWorlds.MountAndBlade.dll`, not `TaleWorlds.MountAndBlade.Multiplayer.dll`. The default-off client adapter now resolves the exact defining assembly and fails distinctly for missing assembly/type/property evidence. This shared bootstrap and lobby path precede all battle adapters and have no campaign fixture, cooperative battle authority, campaign result publication, or L2/L3 evidence status. See [BATTLE_TEST_AUTOMATION_M2B2D_DEDICATED_CONTROL.md](BATTLE_TEST_AUTOMATION_M2B2D_DEDICATED_CONTROL.md).

Published native-login run `m2e-live-r1-01` loaded exact client SHA-256 `8089FC9FF0DB230AC358D4B5DDE611B73FEEBA16E6B7BFF3EB3126866E7C1FBB`, resolved the exact `NetworkMain.GameClient`, invoked one gated `LobbyState.TryLogin()` task, reached `AtLobby`, and completed 52 successful custom-server-list responses. It did not click UI, store credentials, bypass platform authentication, or retry blindly. No entry passed every selector, but retained evidence did not include list-entry fields and therefore cannot prove server absence. Installed-runtime inspection found that the runner conflated scene `Map` with the separate serialized `UniqueMapId` field. Runner-only correction `e55f1bd` omits that optional filter when no authoritative native value is available while retaining run-unique name/port/game-type, singular-match, and owned-host gates.

Clean corrected-selection run `m2e1-live-r1-01` selected the exact run-owned server and proved native join through `GameNetwork.StartMultiplayerOnClient(...)`, but remained at non-terminal `JoinAccepted`. Installed-runtime IL inspection located the missed observer at an absent historical lobby signature. Corrections `d1af692` and `3fdcda3` moved notification to the actual lowest-level post-rewrite boundary and retained timeout evidence. Clean published compile `m2e1-handoff-pub-compile-20260901-01` from `a7bd528` produced client SHA-256 `7CC2D759806D2F02D8BEBA15BCBC01EF61DDE4975004728F3D7E6C8332977E97`, and controlled transaction `m2e1-handoff-stage-r1` installed only its DLL/PDB while retaining the complete prior client tree. Clean run `m2e1-handoff-live-r1-01` then loaded that exact client, observed the post-rewrite `NetworkHandoff`, reached terminal `Connected`, retained native join success plus server `CreatePlayer`, returned `Pass`, and completed exact graceful cleanup. Because this connection gate precedes every mission adapter, the finding, correction, and proof are shared by every battle type.

The external aggregate remains responsible for evidence and cleanup. The runner preserves terminal client status through thrown wait failures and a validated cleanup fallback; after `3fdcda3`, it also preserves the last validated non-terminal status on external timeout while retaining the timeout outcome. It inventories exact client-PID native logs separately from dedicated logs before cleanup completes. Neither evidence correction changes native login authority or campaign/battle authority.

Milestone 2B.3A implements the remaining runner-safety boundary in source and contracts: dedicated/client heartbeat and progress projection, monotonic health timing, distinct `NoHeartbeat`/`NoProgress`, canonical shared-resource locks, exact aggregate cancellation, `RecoveryV2`, and owned structured crash/modal/hang evidence. These mechanisms reuse the existing client and dedicated application ticks, remain automation-profile gated, and add no battle adapter. Clean publication, staging, and a live failure-oriented verification remain required before the Milestone 2B gate can be closed and campaign-fixture work can begin. See [BATTLE_TEST_AUTOMATION_M2B3_SAFETY_CLOSURE.md](BATTLE_TEST_AUTOMATION_M2B3_SAFETY_CLOSURE.md).

The compile-only build plane is separate from both installed modules and repository module output folders. `Directory.Build.props` redirects all build/package state below the run root only when `CoopCompileOnly=true`; both main project files independently suppress their deployment targets in that mode. Normal developer builds retain their historical deployment behavior because the property defaults to false.

## Exact agent transfer architecture

The exact-agent path separates logical identity, spawn preparation, physical creation, client replay, and post-spawn authority:

1. The snapshot resolves a stable troop entry.
2. `ExactTransferContractBuilder` builds body, equipment, mount, peer, wield, control, cleanup, and spawn policy.
3. `ExactTransferContractValidator` rejects incomplete or inconsistent contracts.
4. `ExactTransferContractRuntimeCache` and `ExactTransferRuntimeState` retain per-entry progress.
5. `ExactCampaignPreSpawnLoadoutPatch` modifies `AgentBuildData` before native `Mission.SpawnAgent`.
6. Native server logic creates the physical agent and sends the native network message.
7. `BattleMapSpawnHandoffPatch` observes, defers, groups, and replays initial client creation in scenario-specific paced corridors.
8. The authoritative materialized-entry mapping binds agent index/generation to stable entry identity.
9. Peer ownership, equipment, mount link, commander state, death, and cleanup advance the exact-transfer state machine.

The active architecture deliberately keeps `EnableExactCampaignRuntimeObjectRegistry=false` and `EnableExactCampaignPreSpawnLoadoutInjection=true`: multiplayer-safe character identities remain in the network layer, while exact body and equipment are inserted before native spawn.

## State ownership

| State | Primary owner |
|---|---|
| Current battle snapshot and projections | `BattleSnapshotRuntimeState` |
| Pre-mission contract | `CoopPreMissionTopologyRuntimeState` |
| Deferred mission start | `PendingBattleMissionStartupState` |
| Battle phase | `CoopBattlePhaseRuntimeState` |
| Peer side/entry authority | `CoopBattleAuthorityState` |
| Selection intent/request | `CoopBattleSelectionIntentState`, `CoopBattleSelectionRequestState` |
| Spawn intent/request/result | `CoopBattleSpawnIntentState`, `CoopBattleSpawnRequestState`, `CoopBattleSpawnRuntimeState` |
| Peer lifecycle and reconnect | `CoopBattlePeerLifecycleRuntimeState`, `CoopBattlePeerReconnectState` |
| Composed peer session view | `CoopBattlePeerSessionState` |
| Controlled agent mapping | `CoopBattleAgentControlRuntimeState` |
| Exact per-entry transfer | `ExactTransferContractRuntimeCache`, `ExactTransferRuntimeState` |
| Result de-duplication | `BattleResultWritebackJournalBehavior`, campaign guard contracts |
| Automation run identity/lease/outcomes | `CoopAutomationRunContract`, `scripts/Invoke-CoopTest.ps1` |
| Automation file publication | `CoopAutomationProtocolFileIO`, runner atomic JSON/JSONL helpers |

Many owners are static and process-wide. Mission transition cleanup is therefore architectural, not cosmetic.

## Feature configuration

Current source defaults in `Infrastructure/ExperimentalFeatures.cs`:

### Enabled stable/current paths

- custom cooperative selection overlay, including siege replay;
- early native siege team bootstrap;
- initial native siege spawn-logic bootstrap;
- field-materialized siege replay compatibility;
- full native siege army spawn;
- field, village, siege-assault, and siege-ambush initial materialization;
- exact siege campaign scene-initializer profile;
- custom selection movie load;
- direct campaign battle scene runtime;
- exact native army and object-catalog bootstrap;
- runtime item registry;
- campaign-character multiplayer hero-class fallback;
- exact pre-spawn loadout injection;
- battle-map client equipment network component.

### Disabled experiments/isolation paths

- custom `TdmClone` experiment;
- siege single-player formation-marker UI;
- siege lobby equipment component;
- siege server team bootstrap in the later battle-flow phase;
- staged materialized siege reinforcements;
- runtime character/hero-class object registry.

### Explicit opt-in paths

- campaign map prototype: `COOPSPECTATOR_CAMPAIGN_MAP_PROTOTYPE=1`;
- battle-test client lobby control: `COOPSPECTATOR_TEST_AUTOMATION=1` plus matching `COOPSPECTATOR_AUTOMATION_RUN_ID`, `COOPSPECTATOR_AUTOMATION_RUN_ROOT`, and `COOPSPECTATOR_AUTOMATION_RUN_TOKEN`; the server password is an optional child-environment secret and is never persisted;
- verbose diagnostics master gate: `COOPSPECTATOR_VERBOSE_DIAGNOSTICS=1`;
- most targeted high-volume diagnostics additionally require their own environment variable. See [BUILD_TEST_DEBUG.md](BUILD_TEST_DEBUG.md).

## Dependency and deployment boundaries

- Client code may reference CampaignSystem and client UI assemblies.
- Dedicated code must compile through its explicit linked-source list and dedicated reference profile.
- `GameMode/` is conditionally included in the client build, but explicitly linked into the dedicated build.
- Both module manifests load the same assembly/type name in different runtime products; edits to shared startup assumptions can affect both even though the `SubModule` source differs.
- Exact scenes require staged SandBox/SandBoxCore data and scene assets on dedicated runtime.
- Module XML and GUI assets are part of runtime behavior, not packaging-only decoration.

## Architectural interpretation rules

- Native behavior names in a mission stack do not imply native campaign authority; the dedicated mission still consumes a frozen campaign snapshot.
- A client visual repair is not equivalent to exact pre-spawn identity. Prefer the pre-spawn contract path when it is available.
- A valid numerical agent index is not a durable identity because Bannerlord can reuse indices after removal.
- A successful mission open is not proof of readiness; snapshot, entry-map, scenario materialization, and player/session gates are separate.
- A successful build is not proof of a safe runtime because many failures occur in native mission loading, synchronized message handling, or sequential cleanup.
