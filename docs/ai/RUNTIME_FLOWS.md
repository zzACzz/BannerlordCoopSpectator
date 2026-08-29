# Runtime Flows

Last source verification: **2026-08-28**

## Runtime state machines

### Battle phase

`CoopBattlePhaseRuntimeState` defines the shared coarse phase order:

```text
None (0)
  -> Loading (10)
  -> SideSelection (20)
  -> UnitSelection (30)
  -> Deployment (40)
  -> PreBattleHold (50)
  -> BattleActive (60)
  -> BattleEnded (70)
```

Transitions are intended to be monotonic unless an explicit reset/regression path is justified. The phase is mirrored to `battle_phase_status.txt` for local tools. A phase alone is not sufficient readiness: snapshot, entry-map, player selection, deployment, and scenario materialization have independent gates.

### Peer session

`CoopBattlePeerSessionState` composes several authoritative state owners into a diagnostic/UI view:

```text
NoPeer -> NoSide -> SideAssigned -> EntrySelected -> SpawnQueued
                                               \-> Waiting
SpawnQueued -> Alive -> DeadAwaitingRespawn -> SpawnQueued
any active stage -> BattleEnded
```

The composed stage must not become a second authority. It derives from peer presence, side/entry authority, selection requests, spawn requests, spawn runtime, lifecycle state, controlled agent, reconnect state, and battle phase.

### Exact entry transfer

`ExactTransferStageMachine` tracks one logical entry through native materialization:

```text
None
 -> SnapshotResolved
 -> ContractBuilt
 -> ContractValidated
 -> PreSpawnPrepared
 -> CreateAgentPayloadObserved
 -> RiderMaterialized
 -> [MountMaterialized -> MountLinkVerified]
 -> PeerBound
 -> EquipmentSynchronized
 -> ExactReady
 -> [CommanderReady]
 -> DeathObserved
 -> CleanupComplete

Any non-cleanup active stage -> Failed
```

The mount branch is conditional. External siege assault is foot-only; field, village, sally-out, and siege-ambush contracts may include mounted groups according to scenario policy.

## Flow 1: campaign encounter capture

Primary owner: `Campaign/BattleDetector.cs`.

1. The client/campaign application tick calls `BattleDetector.Tick`.
2. The detector observes `PlayerEncounter`, `MapEvent`, the current mission, settlement, and scene.
3. It resolves a scenario kind:
   - `Siege` when a siege subtype is active;
   - day/night hideout contract when appropriate;
   - `VillageBattle` for a village encounter/scene;
   - `FieldBattle` for other map events;
   - `Unknown` otherwise.
4. For siege context it resolves subtype in this priority: blockade, blockade sally out, siege ambush, lords hall, relief, sally out, siege assault.
5. `TryGetUnsupportedCoopMissionReason` validates the active mission against scenario-specific adapters. It rejects blockade variants immediately and rejects any other scenario whose live contract is invalid.
6. The detector resolves scene, map patch, mission shell, terrain direction, weather/time, settlement/siege data, parties, exact troops, heroes, equipment, mounts, perks, combat profiles, casualty policy, and frozen captains.
7. It builds a `BattleStartMessage` containing the complete `BattleSnapshotMessage`.
8. It assigns battle/instance/campaign identities used later by network validation and writeback de-duplication.

Failure behavior: do not partially start an unsupported or invalid scenario. The detector retries eligible start work on a delay, but contract failure is surfaced as a guarded unsupported path.

## Flow 2: host-to-dedicated launch

1. The campaign host writes `battle_roster.json` through `BattleRosterFile` when the payload contains usable troops.
2. Depending on hosting mode, it either:
   - sends a `BATTLE_START` notification through the TCP coordination layer; or
   - invokes `TryStartDedicatedMissionForCampaignHost` to launch/notify the local dedicated helper.
3. Dedicated launch settings select public/VPN/local behavior, tokens, executable, arguments, game type, scene, and battle index.
4. Host-self-join markers may redirect the local campaign client into the listed mission without confusing it with an unrelated peer.
5. The dedicated mode reads the roster snapshot and resolves the same scenario/scene contract.

Important boundary: `battle_roster.json` is initial same-machine handoff, not the remote-client transport. Remote peers receive authoritative state through Bannerlord networking after connection.

## Flow 3: pre-mission topology handshake

Primary owners:

- `Network/CoopPreMissionTopologyNetworkComponent.cs`
- `Infrastructure/SiegeAssault/CoopPreMissionTopologyRuntimeState.cs`
- `Patches/PreMissionTopologyContractPatch.cs`
- `Infrastructure/PendingBattleMissionStartupState.cs`

Sequence:

1. The global pre-mission component is registered ahead of the native base network component.
2. The server creates a compact topology contract containing the scene, game mode, battle/scenario identity, and contract hash/schema.
3. The server advertises it periodically and when a peer connects.
4. Native client `LoadMission` or `InitializeCustomGame` is intercepted before scene open.
5. If the request needs a cooperative topology contract and no matching active contract exists, the native open request is stored and deferred.
6. The received contract is validated for schema, hash, scene, battle index, scenario, and lifetime.
7. A valid contract activates runtime state and arms any required `PendingBattleMissionStartupState` context.
8. The deferred native request is resumed.
9. A mismatch or ten-second wait timeout clears pending state and triggers a controlled mission abort. The client is not allowed to guess and open the scene.

This ordering protects exact campaign scenes whose assets, mission shell, and scenario behaviors differ from official multiplayer maps.

## Flow 4: mission open and behavior composition

### General battle mode

1. `MissionMultiplayerCoopBattleMode.StartMultiplayerGame(scene)` classifies the scene.
2. It selects `MultiplayerBattle` for battle-map/campaign-derived scenes and a TDM shell for fallback scenes.
3. It resolves/captures siege-open context; siege assault with deployment branches to its specialized mode.
4. It applies campaign map-patch and initializer context.
5. It calls `MissionState.OpenNew` with a native-first cooperative behavior factory.
6. The factory validates server/client composition, removes client-only behaviors from the server, and requires compatible scoreboard/helper dependencies where applicable.
7. Scenario-specific commander deployment behavior is appended only for a matching contract.

### Siege assault with deployment

1. `SiegeAssaultMissionOpenBridge` chooses the exact native siege initializer profile.
2. The specialized mode wraps `SiegeMissionWithDeployment`.
3. Native siege behaviors are created, including siege engines and deployment handlers/controllers.
4. Cooperative network, power, machine-selection, materialization, and UI layers are appended according to side and feature policy.
5. Dedicated `CoopMissionSpawnLogic` attachment remains deferred to the stable mission observer.

Mission behavior ordering is a compatibility contract. A class being present somewhere in the list does not prove correct startup; native components may query one another during `EarlyStart`, `AfterStart`, or synchronized load callbacks.

## Flow 5: dedicated mission stabilization and bootstrap

Primary owner: dedicated `SubModule` observer plus `CoopMissionSpawnLogic`.

1. The dedicated application tick ignores unrelated modes and waits for a mission that is stable enough for cooperative observation.
2. It attaches `CoopMissionNetworkBridge` when missing.
3. It attaches `CoopMissionSpawnLogic` when the chosen mode deferred it.
4. The spawn logic resets process-wide per-mission state:
   - snapshot projections;
   - phase and bridge status;
   - selection/spawn/authority/lifecycle/control maps;
   - reconnect state;
   - exact transfer caches and materialization state;
   - scenario-specific deployment/scene-object state.
5. It loads/validates the battle snapshot, bootstraps required object/item catalogs, and ensures attacker/defender teams.
6. Exact native army bootstrap supplies snapshot-driven troop data to the native spawn logic.
7. The observer continues calling spawn-owner and phase-owner ticks.

Failure behavior should be fail-closed for missing exact contracts or topology mismatch, but native compatibility helpers often log and fall back for optional behaviors. Distinguish a required invariant from an optional diagnostic/helper when reading a failure.

## Flow 6: full in-mission snapshot transfer

Primary owner: `CoopMissionNetworkBridge`.

Current transport is V2, schema 1.

1. The server serializes and compresses the full snapshot.
2. It creates a transport manifest with battle/instance identity, sizes, chunk count, and integrity hashes.
3. A connecting client receives or requests the manifest.
4. The client validates manifest identity against the accepted pre-mission topology contract.
5. The server sends an initial window of chunks and advances within per-tick/per-peer limits.
6. The client records chunks, sends range acknowledgements periodically, and requests missing/stalled ranges.
7. Retry policy covers manifest delivery, initial chunk request, stalled progress, bootstrap request, and final acknowledgement.
8. The client rejects invalid chunk metadata, hash mismatch, decompression/deserialize failure, snapshot identity mismatch, or topology mismatch.
9. On success, the client calls `BattleSnapshotRuntimeState.SetCurrent` and sends a complete acknowledgement.
10. The server records the peer's acknowledged transmission before satisfying later readiness gates.
11. Terminal failures send/consume an abort and prevent unsafe continuation.

Key current limits/timers from source:

- two snapshot chunks per payload/tick;
- initial four-chunk window;
- maximum eight in-flight chunks per peer;
- range acknowledgement after four new chunks;
- manifest retry after two seconds;
- initial chunk request retry after 350 ms;
- stalled range acknowledgement after three seconds;
- assembly idle timeout after fifteen seconds;
- final snapshot acknowledgement retry after six seconds.

Do not change these in isolation from memory pressure, native message timing, reconnect, and large-roster tests.

## Flow 7: native spawn and exact materialization

### Server side

1. The normalized snapshot resolves an ordered entry and multiplayer-safe spawn template.
2. `ExactTransferContractBuilder` builds the exact spawn contract.
3. The validator enforces body/equipment/mount/identity/policy consistency.
4. Exact catalog bootstrap resolves original campaign items/characters where supported and stable mirror items where required.
5. `ExactCampaignPreSpawnLoadoutPatch` applies final snapshot body/equipment to `AgentBuildData` before native spawn.
6. Native `MissionAgentSpawnLogic` creates the agent and owns physical lifecycle.
7. The authoritative entry ledger binds stable entry identity to the created agent/index/generation.
8. Native networking emits `CreateAgent` and subsequent synchronized state.

### Client initial replay

1. `BattleMapSpawnHandoffPatch` observes incoming native creation payloads.
2. For a supported initial-materialization scenario, early payloads are deferred rather than processed in one large burst.
3. Rider and mount payloads are grouped where the scenario permits mounts.
4. The scenario-specific materialization runtime advances a small budget per tick.
5. Each deferred payload is passed back to the native handler; the client does not create a parallel surrogate agent.
6. The materialized-entry mapping is applied and validated.
7. The client checks expected entry count/hash, active agents, side, mounts, and queue stability according to scenario.
8. The client sends a scenario-specific ready acknowledgement.
9. The server requires readiness from all assigned peers before leaving the pre-battle hold.

### Initial/reinforcement boundary

The successful readiness acknowledgement marks initial client materialization complete. After that point, new native `CreateAgent` messages are active-battle reinforcements and must be processed immediately through the native corridor.

Do not classify a payload by agent index alone. Bannerlord can reuse an index after an old agent is removed; late removal/death traffic must not target the new generation.

## Flow 8: player selection, spawn, and control

1. UI or console input creates side/entry/spectator intent.
2. A mission network request is preferred; local file requests are fallback/tooling coordination.
3. The server validates allowed side, entry availability, current phase, peer state, duplicate selection, and scenario policy.
4. `CoopBattleAuthorityState` records the authoritative side/entry.
5. A spawn request advances through validating, validated/rejected, and spawned runtime state.
6. The server binds the peer to the authoritative materialized agent.
7. `CoopBattleAgentControlRuntimeState` and peer lifecycle state reflect control/AI handoff.
8. Control and session projections are sent back to clients and shown by the selection/AI-control UI.
9. On death, the lifecycle can become `DeadAwaitingRespawn`; a later approved spawn returns it to queued/alive.

Commander selection additionally activates formation control, deployment readiness, facing orders, and commander-death handoff contracts. Siege machine selection is server validated and synchronized independently of troop selection.

## Flow 9: phase ownership and battle start

Primary owner: `CoopMissionSpawnLogic.RunCoopBattlePhaseOwnerTick` plus scenario deployment controllers.

1. `Loading`: snapshot/topology/mission bootstrap is incomplete.
2. `SideSelection`: peers choose attacker/defender/spectator.
3. `UnitSelection`: peers choose stable snapshot entries.
4. `Deployment`: commander and scenario-specific formation/machine placement occurs.
5. `PreBattleHold`: server waits for required snapshot, entry-map, materialization, boundary, deployment, and peer readiness.
6. `BattleActive`: native AI/combat/reinforcement flow is released.
7. `BattleEnded`: result is frozen/written and further startup/spawn work must stop.

The local `battle_phase_start.request` and `coop.start_battle` command express intent. They do not override missing authoritative readiness.

Warmup/timer/team-selection native components and cooperative phase state can coexist. When diagnosing an early or stuck start, inspect both the native mission behavior and the cooperative gate that currently owns progress.

## Scenario routing matrix

| Scenario | Mission/deployment owner | Initial materialization | Mount policy | Aftermath/status |
|---|---|---|---|---|
| Field battle | General CoopBattle + field commander runtime | `ExactFieldBattleInitialMaterializationRuntime` | Rider/mount groups allowed | Source-enabled; field boundaries runtime-verified on two machines in July 2026 |
| Village battle | CoopBattle + village commander/boundary runtimes | `ExactVillageBattleInitialMaterializationRuntime` | Rider/mount groups allowed | Source-enabled; materialization/boundaries runtime-verified in July 2026 |
| Siege assault | Specialized siege-with-deployment mode | `ExactSiegeAssaultInitialMaterializationRuntime` | Foot-only projection | Source-enabled; initial army/native reinforcements/completion runtime-verified in July 2026 |
| Sally out | CoopBattle + sally-out commander runtime | shared scenario-aware initial path | Mounted entries may exist | Contract-gated; inspect latest focused report before changing |
| Siege ambush | Specialized controller/patch and ambush orders | `ExactSiegeAmbushInitialMaterializationRuntime` | Rider/mount grouping allowed | Contract-gated; source path active, materialization flag enabled |
| Relief | Relief adapter/controller and settlement policy | general exact path | Scenario contract decides | Contract-gated; specific settlement resolution contract |
| Lords hall | Indoor controller/runtime | scenario-specific controller | No general assumption | Contract-gated; separate result bridge |
| Day hideout | Day hideout mode/controller | hideout-specific lifecycle | Scene contract decides | Contract-gated; native aftermath bridge |
| Night hideout | Night mode, ambush controller/network | hideout ambush lifecycle | Scene contract decides | Contract-gated; boss/ambush phase contracts |
| Blockade | none | none | n/a | Explicitly unsupported |
| Blockade sally out | none | none | n/a | Explicitly unsupported |

Do not reuse the external-siege foot-only assumption in siege ambush or land battle. Do not reuse the field boundary algorithm in village/siege without a dedicated contract and run.

## Flow 10: siege deployment and machine state

1. Siege context from the campaign snapshot identifies available/deployed attacker and defender engines.
2. The server initializes native siege engine/deployment handlers.
3. `CoopSiegeMachineDeploymentController` applies player selection or side auto-deployment, using native deployment points and controller collections.
4. It normalizes mission-object visibility/state, synchronizes native `SiegeWeaponsController`, prepares formations, and assigns/destroys detachments as needed.
5. `CoopMissionNetworkBridge` broadcasts stable machine/deployment-point identity and selected state.
6. Clients resolve the machine through ID bridge first, with guarded type/position fallbacks, then normalize local visuals/controller state.
7. Ladder interaction and merlon visibility have separate synchronized contracts and patches.
8. Formation orders to attack siege weapons are validated server-side.

Current planning boundary: the V3 document for finalizing *unused* siege machines specifies a new two-phase operation, side barrier, postconditions, and late-join ordering. That exact operation was not found in current source during this review. Existing auto-deploy/selection/visibility logic must not be mislabeled as completed V3 finalization.

## Flow 11: reconnect and late join

Current source contains reconnect-aware state and transport:

1. A reconnecting peer is associated with prior peer/session identity where possible.
2. The server resends topology/full snapshot and waits for a valid current transmission acknowledgement.
3. Entry-status and materialized-agent mappings are retransmitted and acknowledged.
4. Active mission phase, selection/control, commander/deployment, siege objects, and scenario readiness are resent.
5. A reconnect-finalize readiness acknowledgement gates final peer activation.
6. Peer-index migration updates authority/control/session dictionaries and removes stale state.

However, the invasive native late-join patch hooks for sending agents/missiles and handling late loaded peers are disabled in both startup configurations. Do not assume every active-battle late-join case is solved merely because reconnect protocol messages exist.

Known recorded gap: reconnecting a client into an already active external siege was not runtime-verified in the July 2026 materialization report.

## Flow 12: battle completion and result writeback

### Dedicated result production

1. The phase owner determines that the scenario's native/cooperative completion condition is satisfied.
2. Authoritative entry ledger, active/dead/wounded state, team result, and scenario stage outcomes are frozen.
3. Scenario-specific rules contribute casualties and aftermath data.
4. `CoopBattleResultBridgeFile` atomically writes `battle_result.json`.
5. The battle phase becomes `BattleEnded`.
6. Dedicated lobby/mission end transition begins, and startup/spawn observers must no longer re-enter battle initialization.

### Campaign consumption

1. `BattleDetector` waits for a stable result file; read-cache logic rejects a file whose stamp changes during the read.
2. Campaign guard logic validates active campaign ID, battle/result identity, modern/legacy policy, and prior journal state.
3. The host applies per-entry casualties, hero HP/death/wound, XP/progression, prisoners/capture, loot, morale/contribution, settlement/siege consequences, and scenario-specific native aftermath.
4. A stable result ID is journaled only after successful application.
5. Retry remains possible after validation/application failure that did not journal success.
6. The campaign mission/encounter exits or advances to the next native stage.

Idempotency boundary: never journal before successful application, and never reapply a journaled stable result. Multi-stage siege casualties must be summed once, not applied separately and then again in a final aggregate.

## Flow 13: sequential missions and cleanup

Sequential battle support depends on clearing state that would otherwise survive because many owners are static.

At minimum, inspect cleanup for:

- battle snapshot/runtime projections;
- topology contract and pending startup;
- authority, selection, spawn, lifecycle, reconnect, and control state;
- exact transfer caches and agent-index generation ledger;
- deferred native create-agent queues;
- initial materialization completion/readiness per scenario;
- deployment boundaries and commander state;
- siege mission-object IDs, ladder/machine state, and controller caches;
- battle power, result, phase, and local bridge request/status files;
- mission observer attachment guards.

Cleanup must occur on normal end, controlled abort, failed startup, connection loss where appropriate, and before a new mission initializes. A successful first battle followed by a broken second battle is usually evidence of an incomplete reset, not a scene-specific spawn defect.

## Failure-oriented trace guide

| Symptom | Trace backward in this order |
|---|---|
| Client opens wrong/crashing scene | pre-mission contract -> pending open -> initializer/map patch -> dedicated asset staging |
| Client stuck loading | topology accepted -> full snapshot manifest/chunks -> snapshot apply -> finished-loading gate -> entry map/materialization ack |
| Initial army freezes client | deferred `CreateAgent` queue -> scenario pacing -> group dependency -> per-tick budget -> verbose diagnostics state |
| Reinforcement missing/crash | initial-complete boundary -> reused index/generation -> late removal/death -> native immediate handler |
| Wrong hero/equipment | snapshot entry -> original/template IDs -> catalog/mirror resolution -> exact contract -> pre-spawn injection -> payload observation |
| Mount/rider mismatch | scenario mount policy -> paired entry IDs -> payload order -> mount link verification -> cleanup generation |
| Cannot start after deployment | phase -> commander readiness -> boundary ack -> scenario materialization ack -> assigned-peer set -> native warmup/hold |
| Siege AI waits forever | selected machine -> native controller collections -> deployment point -> detachment -> unused machine visibility/disabled state -> pathing/tactic |
| Result not applied | result file stability -> campaign ID -> result ID -> journal -> scenario aftermath -> campaign exit |
| Result applied twice | stable ID construction -> journal write timing -> retry path -> multi-stage aggregation |
| Second mission fails | mission-end reset -> static caches -> observer guards -> bridge files -> reused indices/transmission IDs |
