# Invariants and Risks

Last source verification: **2026-08-28**
Last automation-control source/contract verification: **2026-08-31**
Last dedicated-control runtime verification: **2026-08-31** (`m2b2c-client-handoff-live-20260831-01`)

## Non-negotiable runtime invariants

### 1. One physical agent spawner

Native server mission logic is the only owner allowed to create physical battle agents. Cooperative code may:

- supply snapshot-driven troop data;
- inject body/equipment into `AgentBuildData` before spawn;
- delay and replay the native client `CreateAgent` message;
- bind stable entry identity and peer control after materialization;
- repair presentation only when the exact pre-spawn path cannot express a safe field.

It must not create a second client-side or server-side physical agent for the same logical entry.

Why protected: duplicate creators cause ownership conflicts, duplicate corpses, wrong death routing, invalid native pointers, mismatched agent indices, and non-deterministic cleanup.

### 2. Stable entry identity outranks agent index

The durable identity is the snapshot entry ID plus the current materialization/generation context. Agent indices are temporary transport/runtime handles and can be reused after removal.

Never:

- match a delayed creation packet to an entry by index alone;
- delete a pending packet solely because a removal/death references the same index;
- apply old ownership, equipment, death, or control state to a new generation;
- treat list position as stable campaign identity.

### 3. Topology contract precedes campaign-scene open

A remote client must receive and validate the compact pre-mission scene/mode/scenario contract before opening a campaign-derived mission. The later full snapshot cannot safely repair a client that already opened the wrong scene or mission shell.

Mismatch and timeout must fail closed with a controlled abort.

### 4. Full snapshot identity must match topology

Battle ID, instance ID, scene, game mode, battle index, scenario, schema, and hashes must remain consistent across:

- campaign capture;
- `battle_roster.json`;
- dedicated mission start;
- pre-mission topology contract;
- V2 snapshot manifest/chunks;
- runtime snapshot state;
- materialized entry map;
- reconnect state;
- `battle_result.json` and writeback journal.

Do not silently substitute a “close enough” scene or payload after a peer accepted a different contract.

### 5. Initial materialization is not reinforcement handling

The client pacing queue exists to distribute the initial native agent burst. After the client sends the valid scenario-specific readiness acknowledgement, active-battle `CreateAgent` messages are native reinforcements and must remain immediate.

The initial-complete marker resets only for a new mission/general cleanup, not for every later entry-map update. Reopening it during battle can strand reinforcements in a completed initial queue and route their death/removal traffic to stale agents.

### 6. Server authority is final

Clients send intent for side, entry, spectator mode, spawn, commander orders, formation orders, siege-machine selection, ladder interaction, and AI-control handoff. The server validates and publishes the accepted state.

Local bridge files and UI state must never override server authority.

### 7. Readiness is multi-dimensional

Do not collapse these into one boolean:

- pre-mission contract active;
- full snapshot applied/acknowledged;
- entry-status snapshot current;
- authoritative agent-entry map current/acknowledged;
- scenario initial materialization ready;
- village/field/deployment boundary ready;
- side/entry selected;
- commander/deployment ready;
- reconnect finalization ready;
- native mission loading finished.

Each has a different owner, invalidation event, and transmission/version.

### 8. Per-mission static state is fully reset

The code uses substantial static process state. Every new mission, normal end, startup abort, and relevant disconnect/reconnect replacement must clear or rebind it.

Protected reset surfaces include snapshot, topology, phase, authority, selection, spawn, lifecycle, reconnect, control, exact transfer cache, agent ledger, deferred create-agent queue, initial readiness, commander/deployment, boundaries, siege object maps, machine/ladder state, result state, and observer guards.

### 9. Campaign writeback is exactly once

The campaign host must validate active campaign identity and stable result identity, apply all affected systems successfully, then journal the result. A failed attempt that did not journal may retry; a journaled result must not apply again.

Multi-stage casualties must be aggregated once. Do not apply stage deltas and then apply the same final total.

### 10. Scenario contracts remain isolated

Field, village, siege assault, sally out, siege ambush, relief, lords hall, and hideout paths have different native mission stacks and assumptions.

Examples:

- external siege assault is foot-only; siege ambush can use rider/mount groups;
- field and village deployment boundaries have separate geometry/readiness contracts;
- lords hall is an indoor stage with separate result bridging;
- blockade variants are explicitly unsupported;
- hideout day/night paths use different controllers and phase rules.

Never broaden a condition from one scenario to `IsBattleMap`/`IsSiege` without proving the other subtype behavior.

### 11. Native behavior composition and order are contracts

Many TaleWorlds behaviors resolve peers, teams, spawn logic, siege controllers, lobby equipment, or views during early lifecycle callbacks. Removing or moving a behavior can crash later code that appears unrelated.

Preserve server/client separation and validate required behavior presence after composing a stack.

### 12. Client and dedicated reference profiles stay separate

Both products emit an assembly named `CoopSpectator`, but they compile different entry points and reference sets. Dedicated must use its explicit linked-source graph and compatible server references. Client-only Campaign/UI dependencies must not leak into dedicated runtime.

### 13. Diagnostics do not become production work

Per-frame, per-agent, synchronized-message, native-object, and full-roster diagnostics must be disabled by default and require explicit verbose opt-in. Log formatting and collection must remain inside the gate.

Diagnostics that mutate native/runtime state are not “just logging” and need a separate safety justification.

Temporary diagnostic code must be removed before the task is considered complete unless the approved plan explicitly retains it as production diagnostics. Retention requires explicit approval and documentation of:

- its default disabled/enabled state;
- every activation mechanism;
- expected execution frequency and performance cost;
- output volume and storage location;
- native, network, synchronization, and lifecycle risks;
- the scenarios and runtime roles in which it was validated.

### 14. Campaignless conversation safety does not fabricate campaign state

`CampaignlessConversationMissionSafetyPatch` may short-circuit `ConversationMission.OneToOneConversationAgent` with a null result only when `Campaign.Current` or its `ConversationManager` is unavailable. When both exist, the original native getter must run unchanged.

The dedicated build has no campaign state and therefore always uses the guarded null path if this getter is reached. The patch is a crash guard, not a replacement conversation manager and not permission to run campaign-only conversation behavior on multiplayer/dedicated roles.

### 15. Exact-siege scene occlusion changes only on a matching remote client

`CoopSiegeSceneOcclusionSafetyContract` may disable scene occlusion only when all of these facts are true:

- the role is a remote client, not server/listen server;
- a matching active pre-mission topology contract exists;
- the scenario is a siege battle;
- the mission shell equals `SiegeMissionWithDeployment`;
- runtime and topology scene names are both present and equal after normalization.

Missing/mismatched topology, non-siege scenarios, different mission shells, and server/listen-server roles must preserve native occlusion. The accepted decision is applied in `MissionMultiplayerCoopSiegeAssaultWithDeploymentClient.OnBehaviorInitialize` before base initialization/renderer activation.

### 16. Battle-test lobby control remains run-scoped and non-authoritative

`CoopLobbyAutomationController` is disabled unless the complete explicit automation profile is present. It may request the normal TaleWorlds lobby join only after the request matches the `RunId`, run-token hash, loaded client-module hash, command lifetime, exact server identity, run-scoped owned-host record, still-live dedicated PID/path/start time, and active local UDP port. Automation must neither trust nor mutate the production persisted local-host marker.

The controller must not create an alternate connection path, rewrite any address except through the existing validated local-host loopback patch, issue `start_game`, open a mission, fabricate readiness, or publish battle results. The server password must remain environment-only and absent from commands, artifacts, status, and logs. Status publication must remain strictly atomic and state-change-driven; per-tick logging is prohibited.

Request expiry may block a native join from starting. Once TaleWorlds owns the native join task, the module must not claim that the operation was cancelled or terminally expired; the external runner owns the timeout and exact-process cleanup decision.

### 17. Compile-only and run ownership never imply runtime proof

`CoopCompileOnly=true` must always require an absolute caller-owned output root, suppress all three installation deployment targets, suppress the client's implicit dedicated build, and keep output/intermediate/package state below the exact run root. The proof must compare recursive before/after SHA-256 inventories of the installed client, legacy-client, and dedicated module trees.

Every automation run root is fresh and exclusively owned by its `RunId`. Persist only the nonce fingerprint; reject cross-run, nonce, role-instance, duplicate, stale, reordered, malformed, partial, incompatible-protocol, and identity-mismatched records. An expired lease is abandoned evidence to inspect, not permission to delete or reuse the root.

An L0/L1 `Pass` proves only the named environment assertion, contract inventory, or compile operation. It must not be reported as loaded-binary, client/server connection, mission-open, battle-lifecycle, natural-completion, result, or writeback evidence.

### 18. Dedicated readiness and commands require an observable run-scoped channel

`ModuleReady`, process liveness, service authentication, a fixed delay, or an unacknowledged write must never be treated as proof that the dedicated command handler is ready or that a bootstrap command was accepted.

For the exact staged profile, `DedicatedCustomServer.Starter.exe` may create or rebind a native console after process creation. Redirected stdout/stderr may remain empty even while native logs and service heartbeats continue, and PID-correlated `rgl_log` output does not necessarily include .NET `Console.WriteLine` text. Redirected standard input/output and native logs are therefore supplementary evidence only.

Before another live connection attempt, the external runner must own an allowlisted command intent, and the exact dedicated role must publish run/token/hash/process-bound atomic acknowledgements for readiness, applied options, usable-map acceptance, and start-game progression. The control path must remain disabled by default, must not expose a generic production command surface, and must not perform per-tick file I/O or logging when automation is disabled.

## Known current limitations and unverified areas

### Source-verified limitations

- `TdmClone` is disabled.
- Custom staged siege reinforcement materialization is disabled; native reinforcements are the stable path.
- Runtime character/hero-class object registry is disabled; surrogate identity plus exact pre-spawn loadout is active.
- Broad client battle-shell suppression is disabled after native access-violation crashes; diagnostics-only hooks remain.
- Invasive late-join hooks for native late-client handling and agent/missile sends are disabled.
- Siege single-player formation-marker UI and siege lobby equipment component are disabled.
- Blockade and blockade sally out are unsupported.
- Core battle roster and phase/start-request paths are still shared under the normal Documents profile and have no automation `RunId`. Milestone 2B.1 makes only result publication fail-closed: a complete explicit automation profile with exact `Suppress` records a run-scoped decision and cannot write campaign-consumable `battle_result.json`; an invalid enabled profile rejects publication instead of falling back to production. This permits only the minimum vanilla connectivity bootstrap, not a campaign fixture or L2/L3 battle run.
- A default-off run-scoped command/acknowledgement path now exists in source for the normal lobby join flow, but it has only isolated contract evidence. It is not Bannerlord-runtime-verified and does not remove the M1 connection/control blockers until a named connection rerun proves the exact handoff.
- No run-scoped dedicated readiness/command acknowledgement path exists yet. The current aggregate runner's redirected standard-handle path timed out before command dispatch in `m2b2c-client-handoff-live-20260831-01`; another unchanged retry is not an acceptable substitute.
- The Milestone 2A runner, protocol, full 20-project aggregate, and compile-only mode are source/build/test verified. They intentionally provide no staging, loaded-hash, process-cleanup, connection, mission, or battle evidence; those remain Milestone 2B or later.

### Recorded runtime gaps

From the July 2026 materialization status:

- reconnect into an already active external siege was not practically verified;
- exact final ownership of every captured lord was not separately verified;
- a nonfatal `EquipWeaponFromSpawnedItemEntity` missing mission-object warning remained observable;
- exact cross-process `MBGUID` identity for crafted weapons was not proven; stable pre-registered mirror keys are the safe path;
- field-specific materialization/boundary behavior should not be transferred to other scenarios without its own test phase.
- the campaignless conversation and remote exact-siege occlusion decision contracts passed focused tests on 2026-08-29, but their Harmony/native renderer behavior was not runtime-verified in Bannerlord after the checkpoint commits;
- campaign-map tagged-mesh visual ticking is source-verified, but its visual result and per-frame cost were not runtime-verified after checkpoint `28ec8ca`.
- the 2026-08-31 Milestone 1 probe loaded installed client and dedicated modules version `0.3.1`, while repository outputs were version `0.3.2` with different hashes; the successful launches are installed-profile evidence only;
- the same probe confirmed that Steam must already be running for the direct multiplayer client launch on the named machine profile;
- the dedicated module authenticated without `start_game`, but port `7210` remained unbound and client connection/control were not tested because result isolation was not yet safe.
- clean published-revision run `m2b2c-client-handoff-live-20260831-01` verified dedicated provisional/verified ownership, exact graceful cleanup, released ports/lock, unchanged installed hashes, and unchanged protected result; it timed out on the unobservable redirected console-readiness channel before any command or client launch.
- the 2026-08-31 post-M1 client-control implementation passed isolated source compilation, request/server-selection/atomic-status contracts, PowerShell parsing, and launcher `-ValidateOnly` checks; no game process was started, and no live normal-lobby handoff or connection was observed;
- the developer reports stable manual battle runs before releasing `0.3.2`; this is separate manual regression evidence and does not alter the M1 fact that its own locally loaded artifacts were `0.3.1`.
- `m2a-contracts-20260831-07` passed all 20 contract projects and `m2a-compile-only-20260831-03` compiled both `0.3.2` assemblies without changing installed module inventories; neither run launched Bannerlord or verified runtime loading.

### Planned but not source-verified

`docs/EXACT_SIEGE_UNUSED_MACHINE_FINALIZATION_FIX_TZ_2026-08-21_V3.md` specifies a two-phase server operation that:

- builds and validates a no-side-effect finalization plan;
- preserves exactly selected machines;
- disables/removes unused machines from native AI/controller state;
- publishes state only after successful application;
- waits for both sides and supports repeated calls/late join;
- enforces postconditions and contract tests.

The 2026-08-28 source review found extensive selection, auto-deploy, visual normalization, controller synchronization, and detachment logic, but did not find the named unused-machine finalization operation or its specified result/barrier contract. Treat the V3 document as a pending design until source and tests prove otherwise.

### Version uncertainty

Project comments mention both Bannerlord 1.3.14 and required 1.4.8 runtime files. Any native/Harmony/decompilation conclusion is version-bound until hashes and installation versions are recorded.

The initial automation feasibility profile is recorded in [BATTLE_TEST_AUTOMATION_M1_FEASIBILITY.md](BATTLE_TEST_AUTOMATION_M1_FEASIBILITY.md) with Steam manifest build IDs, runtime build marker, executable hashes, module hashes, and exact evidence boundaries. It does not resolve the broader supported-version matrix.

## Risk map by subsystem

| Subsystem | Risk | Typical failure | Required review |
|---|---|---|---|
| `BattleDetector` | Critical | wrong scenario, incomplete snapshot, duplicated/incorrect aftermath | all scenario adapters, campaign IDs, result journal |
| `CoopMissionBehaviors` | Critical | stale state, duplicate spawn owner, stuck phase, wrong result | server/client split, reset paths, sequential battle |
| `CoopMissionNetworkBridge` | Critical | protocol deadlock, stale ack, mismatch, excessive traffic | message schema, send/handler pair, invalidation, reconnect |
| `BattleMapSpawnHandoffPatch` | Critical | client crash, duplicate/missing agent, stale index | all materialization scenarios, native handler ownership |
| Game-mode behavior stacks | Critical | native startup crash or missing component | server/client list, order, lifecycle callback dependencies |
| Exact army/object/item bootstrap | High | unresolved identity, wrong DLL/catalog state | client/dedicated catalogs, stable ordering, fallback policy |
| Exact pre-spawn loadout | High | wrong body/equipment or native payload incompatibility | hero/troop/mount/crafted item, server/client parity |
| Siege machine controller | Critical | AI waits forever, invisible active object, bad detachment | both sides, native controller lists, point/weapon identity |
| Remote exact-siege occlusion guard | High | native renderer crash or over-broad loss of occlusion | remote-only role, accepted topology, exact shell/scene, adjacent scenarios |
| Campaignless conversation safety patch | High | native getter crash or suppressed valid campaign conversation | Harmony target/version, campaign/null paths, client and dedicated registration |
| Campaign map prototype visual tick | Medium | stale tagged meshes, missing animation, or excessive per-frame work | tag collection, scene release, render-ready gate, runtime profiling |
| Deployment boundaries | High | invalid orders, flags in wrong place, client/server mismatch | scenario-specific geometry, version/hash ack, rollback |
| Control/reconnect state | High | wrong peer controls agent, stale peer index | disconnect/rejoin, index migration, death/respawn |
| Result bridge/writeback | Critical | lost or duplicated campaign effects | atomic/stable read, IDs, journal timing, stage aggregation |
| UI/prefab binding | Medium/High | native Gauntlet crash or unusable selection | view-model/XML binding, required native components |
| Project/deploy targets | Critical operational | overwrite wrong installation or load stale DLL | resolved paths, side effects, DLL stamps, restart |

## High-risk files and why

### `Mission/CoopMissionBehaviors.cs`

Large coupled owner for spawn, phase, result, teams, selection, reinforcement, materialization readiness, and dedicated observation. A change near one scenario often uses shared static data. Search all call sites and reset methods before editing.

### `Mission/CoopMissionNetworkBridge.cs`

Combines protocol transport with deployment/siege synchronization. Message handlers run in sensitive synchronized contexts; some send work is deliberately deferred because direct sends from synchronization callbacks crashed dedicated runtime.

Never add chunk sends or large work directly to a synchronized callback without checking the existing post-synchronize safety comments/path.

### `Patches/BattleMapSpawnHandoffPatch.cs`

Interposes on native creation/removal/equipment traffic. Small ordering mistakes can become native access violations. Check rider/mount grouping, external-siege foot projection, initial-complete boundary, reused index generations, and native handler invocation exactly once.

### `Campaign/BattleDetector.cs`

Both input and output edge of the entire system. It can make a fix appear successful in mission while corrupting campaign aftermath. Any change to the snapshot schema or scenario detection must trace through dedicated consumption, client network transfer, result production, and writeback.

### `Infrastructure/SiegeAssault/CoopSiegeMachineDeploymentController.cs`

Touches deployment points, siege weapons, synchronized objects, scene entities, native controller collections, formations, detachments, and AI. Fail-open visibility changes can leave invisible active machines; partial mutations can desynchronize native and cooperative state.

### Project files

`AfterTargets=Build` causes external writes. A routine compile command can update installed game/server modules and stage large asset trees. Never run through an unverified worktree/root.

## Unsafe anti-patterns

- “The agent index matches, so this is the same troop.”
- “The client can spawn a visual surrogate now and reconcile later.”
- “Battle phase is active, so all initial creation packets are reinforcements.”
- “The full snapshot arrived, so the client may open whatever scene it requested.”
- “The bridge file says selected, so the server should accept it.”
- “This worked for field battle, so use it for all land/siege missions.”
- “The patch class exists, therefore the patch is active.”
- “The technical specification is newer, therefore it is implemented.”
- “The build succeeded, therefore the correct installed DLL ran.”
- “A log-only change is harmless in a per-agent callback.”
- “Clear the one dictionary that failed; other static state will self-correct.”
- “Catch the native compatibility error and continue with partially mutated siege state.”
- “Copy the missing client DLL into dedicated until it compiles.”

## Change checklists

### Snapshot schema or serializer

- [ ] Update model, binary serializer, and any legacy codec together.
- [ ] Preserve/advance schema/version deliberately.
- [ ] Update manifest/hash validation and size limits.
- [ ] Update server capture and client runtime projection.
- [ ] Check dedicated explicit compile includes.
- [ ] Check result/writeback if the field affects identity or aftermath.
- [ ] Add round-trip/invalid-input contract coverage.
- [ ] Test a large roster and reconnect retransmission.
- [ ] Update `ARCHITECTURE.md` and `RUNTIME_FLOWS.md`.

### Network message or readiness gate

- [ ] Find definition, registration, sender, handler, acknowledgement, retry, reset, and peer-removal paths.
- [ ] Define version/transmission identity and stale-message behavior.
- [ ] Avoid sends from known unsafe synchronized callbacks.
- [ ] Check join, reconnect, timeout, abort, and second mission.
- [ ] Confirm a missing peer cannot block forever and a stale peer cannot satisfy readiness.
- [ ] Update runtime flow and diagnostics.

### Spawn/materialization/equipment

- [ ] Confirm native server remains the sole physical spawner.
- [ ] Trace stable entry identity and generation.
- [ ] Check hero and regular troop, infantry/ranged/mounted where legal.
- [ ] Check rider/mount ordering and cleanup.
- [ ] Check crafted/static mirror items and modifiers/ammo.
- [ ] Preserve initial vs reinforcement boundary.
- [ ] Evaluate field, village, siege assault, siege ambush, sally out, hideouts, relief, and lords hall.
- [ ] Test death/removal, index reuse, respawn, reconnect, and sequential battle.

### Mission behavior stack or startup patch

- [ ] Compare server and client factories.
- [ ] Check `EarlyStart`, `AfterStart`, load-finished, and observer attachment timing.
- [ ] Confirm required native companion components.
- [ ] Confirm dedicated explicit source includes and target assemblies.
- [ ] Check exact scene, official multiplayer scene, hero creator, hideout, and prototype exclusions.
- [ ] Validate controlled abort on topology mismatch.

### Siege machine/ladder/deployment

- [ ] Separate authoritative selection from local visuals.
- [ ] Resolve stable mission-object and deployment-point identity.
- [ ] Validate both attacker and defender, including empty side.
- [ ] Check native controller collections before and after mutation.
- [ ] Check detachments and formation membership.
- [ ] Check visibility, disabled state, hit points, destruction, ladders, gates, and AI path/tactic behavior.
- [ ] Avoid partial application; use preflight plus explicit result if implementing V3 finalization.
- [ ] Check repeated calls, late join, and next mission reset.

### Result/aftermath

- [ ] Validate campaign ID and stable result ID.
- [ ] Preserve stable file read and atomic write.
- [ ] Apply before journaling; retry only unjournaled failures.
- [ ] Check killed/wounded totals, multi-stage aggregation, hero HP/death, capture owner, XP, loot, prisoners, morale, settlement ownership, and encounter continuation.
- [ ] Evaluate every scenario adapter, not just the reported battle type.
- [ ] Test duplicate result consumption and sequential battles.

### Diagnostics

- [ ] State the hypothesis and exact decision the probe will distinguish.
- [ ] Gate through verbose mode plus a focused flag.
- [ ] Keep string construction and scans inside the gate.
- [ ] Deduplicate/rate-limit frequent events.
- [ ] Do not mutate runtime state unless explicitly designed and approved.
- [ ] Remove or disable after evidence is collected.
- [ ] Document flag, expected markers, log path, and cleanup.

## Regression matrix for shared runtime changes

| Axis | Cases |
|---|---|
| Scenario | field, village, siege assault, sally out, siege ambush, relief, lords hall, day hideout, night hideout, unsupported blockade guard |
| Side | attacker, defender, spectator |
| Role | campaign host, dedicated server, local client, remote client |
| Agent | ordinary troop, hero, commander, ranged/ammo, mounted where legal |
| Lifecycle | load, select, deploy, start, reinforcement, death, respawn, end, writeback |
| Network | first join, delayed packet, disconnect, reconnect, stale acknowledgement, peer index change |
| Sequence | first mission, controlled abort, retry, second mission |
| Result | victory/defeat, killed/wounded, hero capture, siege stages, hideout boss/ambush |

Not every fix needs every cell, but shared changes must explain which cells are irrelevant and why.

## Documentation risks

- Dated documents are not ordered by authority; a later date may be a proposal rather than a completed change.
- Existing files mix Ukrainian and English and sometimes preserve old mode names or architecture.
- Runtime evidence may refer to a source revision not identifiable from the document alone.
- Project and script default paths may target `C:\dev\projects\...` while active work occurs in a Codex worktree.
- Comments can lag feature flags.

Use current source first, and update this knowledge base whenever a protected invariant or verified limitation changes.
