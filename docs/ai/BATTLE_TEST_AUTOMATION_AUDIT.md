# Battle Test Automation Audit

Status: **Source-verified design audit with completed Milestone 1 and Milestone 2A addenda**
Audit date: **2026-08-29**
Repository baseline: **`f5bd90ddb6a361f4341ea3771b4acadc266d684b`** (`f5bd90d`)
Milestone 1 addendum date: **2026-08-31**
Milestone 1 repository revision: **`3c513084ebbe9c99daa0b65849fab7b39b913ee1`** (`3c51308`)
Milestone 2A addendum date: **2026-08-31**
Superseded proposal: `TZ_Codex_Automation_Coop_Battles_and_Global_Map.md`
Replacement specification: [BATTLE_TEST_AUTOMATION_SPEC.md](BATTLE_TEST_AUTOMATION_SPEC.md)
Runtime feasibility report: [BATTLE_TEST_AUTOMATION_M1_FEASIBILITY.md](BATTLE_TEST_AUTOMATION_M1_FEASIBILITY.md)
Milestone 2A report: [BATTLE_TEST_AUTOMATION_M2A_IMPLEMENTATION.md](BATTLE_TEST_AUTOMATION_M2A_IMPLEMENTATION.md)

## 1. Audit purpose and evidence boundary

This audit determines whether the supplied automation proposal can be implemented safely in the current `BannerlordCoopSpectator3` architecture and what must change before implementation.

The audit covered:

- the campaign encounter capture and result-application paths;
- dedicated mission opening, phase progression, peer readiness, spawning, and result publication;
- client topology, snapshot transfer, materialization acknowledgements, and battle-start gates;
- scenario adapters for field, village, siege, sally-out, siege-ambush, relief, lords-hall, and hideout missions;
- existing local bridge files and atomic file helpers;
- build and deployment targets in both project files;
- the standalone contract-test projects and repository scripts;
- the existing campaign-map prototype;
- selected installed dedicated-server assemblies by decompilation, as local supporting evidence only.

No main module build, deployment, Bannerlord launch, live multiplayer run, campaign writeback run, or crash test was performed during the original 2026-08-29 design audit. The bounded 2026-08-31 Milestone 1 addendum launched the installed dedicated and multiplayer client roles without opening a mission; only claims explicitly linked to the feasibility report are runtime-verified.

## 2. Executive verdict

The supplied proposal is useful as a direction document, but it is not safe to implement as written.

- Approximately **60–70%** of its goals and general testing principles are reusable.
- Approximately **25–30%** is implementation-ready against the current source.
- The proposal should be **replaced**, not incrementally patched, because several central assumptions conflict with current authority, readiness, build, and result-writeback behavior.

The most important correction is the test boundary:

> A dedicated server without an assigned and synchronized multiplayer peer can validate mission opening and materialization up to `PreBattleHold`, but it cannot honestly prove `BattleActive`, combat, authoritative completion, result correctness, or campaign writeback.

The safe automation program therefore needs distinct levels:

1. fast contract validation;
2. dedicated spawn smoke ending at `PreBattleHold`;
3. dedicated plus automated multiplayer client lifecycle;
4. full campaign extraction and result writeback;
5. sequential, soak, and randomized coverage;
6. a separate extension of the existing campaign-map prototype.

## 3. Current source architecture

### 3.1. Authority flow

```text
Campaign host
  BattleDetector + scenario adapter
  -> BattleStartMessage + BattleSnapshotMessage
  -> battle_roster.json
  -> dedicated mission start request
             |
             v
Dedicated server
  MissionMultiplayerCoopBattleMode + exact mission shell
  -> authoritative teams, formations, agents, combat, and completion
  -> pre-mission topology + chunked full snapshot to clients
             |
             v
Multiplayer client
  validates topology
  -> receives/materializes the snapshot
  -> selects an entry and controls an authoritative agent
  -> acknowledges readiness required by the server phase gates
             |
             v
Campaign host
  reads battle_result.json
  -> validates campaign/battle identity
  -> applies aftermath exactly once
  -> journals the consumed result
```

There must remain one authoritative state model for each boundary. Automation may observe the existing runtime state and submit normal intents; it must not create a second battle-phase machine, spawn authority, result authority, or campaign truth.

### 3.2. Existing contracts that automation must reuse

| Boundary | Existing source contract | Audit conclusion |
|---|---|---|
| Campaign encounter | `BattleDetector`, `BattleStartMessage`, `BattleSnapshotMessage` | Reuse; do not create a parallel `BattleScenario` domain model |
| Scenario-specific capture | `ExactLandBattleCampaignBattleAdapter` and other scenario adapters | Reuse their validation and routing rules |
| Campaign-to-dedicated file | `Campaign/BattleRosterFile.cs` | Archive the exact bytes and metadata; isolate the bridge root during automation |
| Pre-mission client contract | topology contract and `CoopMissionNetworkBridge` | Required for a real client lifecycle run |
| Full snapshot | `BattleSnapshotBinarySerializer`, current schema 19, transport V2 schema 1 | Record encoding and schema explicitly; preserve exact bytes |
| Runtime phase | `CoopBattlePhaseRuntimeState` | Observe it; never force it forward |
| Entry readiness | `CoopBattleEntryStatusBridgeFile` and in-mission acknowledgements | Reuse normal selection and acknowledgement paths |
| Battle result | `CoopBattleResultBridgeFile` and result snapshot contract | Isolate smoke output; validate identity before any consumption |
| Campaign writeback | `BattleDetector` and `BattleResultWritebackJournalBehavior` | Only a full campaign run may claim applied-and-journaled success |
| Atomic local I/O | `AtomicBridgeFileIO` | Reuse or extract its contract instead of introducing another first-choice transport |

## 4. Critical findings

### A-01 — A server-only full lifecycle is infeasible

Severity: **Critical**

`CoopMissionBehaviors` and `CoopMissionNetworkBridge` require assigned, controlled, synchronized peers and scenario-specific acknowledgements before battle start. The hideout path also explicitly rejects zero assigned or controlled peers. The phase does not become `BattleActive` until eligible peers receive the frozen captain state.

Consequences:

- a server-only test may prove mission opening, scene/scenario selection, teams, formations, agents, equipment, mounts, and readiness up to `PreBattleHold`;
- it must not report full lifecycle success;
- combat, completion, and result correctness require an automated multiplayer client;
- campaign aftermath requires a campaign host connected to the same isolated run.

Required replacement: split the old `LifecycleSmoke` idea into `DedicatedSpawnSmoke`, `ClientLifecycle`, and `CampaignE2E`.

### A-02 — Aborting a smoke mission can publish a plausible result

Severity: **Critical**

`CoopMissionSpawnLogic.OnEndMission` invokes result publication. The result builder does not require that the mission reached `BattleActive`. Once runtime entries exist, an early mission end can therefore create a structurally plausible `battle_result.json`.

Consequences:

- a smoke test must never share its result path with a live campaign;
- smoke output must be explicitly non-consumable or canonical publication must be suppressed under a validated test profile;
- deleting the file after the fact is insufficient because a campaign process could race the deletion;
- `ResultWrittenOnce` and `ResultAppliedAndJournaledOnce` are different acceptance criteria.

### A-03 — The normal build is not a safe compile-only operation

Severity: **Critical**

`CoopSpectator.csproj` runs `DeployModToGame` after `Build`, then runs `BuildAndDeployDedicatedModule` by default. `DedicatedServer/CoopSpectatorDedicated.csproj` stages runtime files and runs `DeployServerToDedicated` after `Build`.

Consequences:

- an automation build can alter installed client and dedicated modules;
- a test run can unintentionally mix current source with stale or partially deployed binaries;
- no build step belongs in unattended automation until both projects expose and verify an explicit side-effect-free compile mode.

### A-04 — Existing bridge paths are global and only partly atomic

Severity: **High**

The current campaign roster, battle phase, start request, and battle result paths are shared under the normal `CoopSpectator` documents folder. `battle_roster.json`, `battle_phase_status.txt`, the start request, and `battle_result.json` use direct writes. The entry-status bridge is better scoped and uses `AtomicBridgeFileIO`, but it still has no automation run identity.

Consequences:

- stale files can be mistaken for current-run state;
- concurrent or sequential runs can cross-consume commands or results;
- readers can observe partial writes;
- the automation transport must be run-scoped, authenticated, sequenced, and atomic.

### A-05 — “Exact payload” is ambiguous without a boundary

Severity: **High**

The current system uses multiple payload representations:

- campaign-to-dedicated JSON containing troop IDs and the full snapshot;
- campaign-host notification JSON prefixed by `BATTLE_START:`;
- dedicated-to-client binary snapshot data with `CSB1`, schema 19, optional compression, and transport V2 chunking;
- a JSON fallback for selected transport conditions.

A generic `payload.bin` cannot establish which serializer, boundary, schema, or source assembly produced it.

Required metadata: payload kind, source and destination roles, encoding, serializer, schema version, content SHA-256, source revision, module hashes, and game/dedicated versions.

### A-06 — A second runtime state machine would create false authority

Severity: **High**

The old proposal describes a broad automation state machine. The module already owns the actual battle phases and readiness gates. A second state machine that can independently mark phases successful or force transitions would conceal runtime failures.

Required replacement: the automation status is a read-only projection of authoritative state plus runner orchestration state. Commands must enter through the same validated intent paths as a real user or peer.

### A-07 — Fixed sleeps and process-name termination are unsafe

Severity: **High**

The current helper path treats HTTP command acceptance as success and contains short fixed delays. `scripts/CoopDevLoop.ps1` uses broad process-name cleanup and is intended for a developer loop, not isolated automation. Decompiled local server code also shows that `start_mission` continues asynchronously after command acceptance.

Required replacement:

- state-based readiness with bounded timeouts;
- ownership of exact process IDs and descendants created by the run;
- no termination by broad executable name;
- explicit port and process inventory in the run manifest;
- graceful mission/process stop before owned-process termination.

### A-08 — Binary identity must be part of every runtime result

Severity: **High**

The locally installed client and dedicated mod DLLs inspected during the audit predated the source baseline. This demonstrates that a successful launch can still exercise stale code.

Every runtime run must record and compare:

- Git revision and dirty state;
- client and dedicated module file hashes;
- relevant game/server assembly versions and hashes;
- build profile and source of deployed artifacts;
- process executable paths and start times.

A mismatch is `EnvironmentBlocked`, not a product assertion failure.

### A-09 — Global combat determinism is not available

Severity: **Medium**

`BattleSnapshotMessage` carries deterministic input data and an atmosphere seed, but it does not expose a global TaleWorlds AI/combat random seed. Automation can make fixtures, serializers, validation, selection, and structural assertions deterministic. It cannot promise identical full-AI casualty sequences.

Assertions over AI outcomes must therefore use invariants or bounded expectations, not byte-identical results.

### A-10 — The campaign-map track is not greenfield

Severity: **High**

The repository already contains an opt-in `CoopCampaignMapPrototype` with schema 10, catalog and dynamic revisions, deltas, chunk transport, visibility policy, bridge code, network messages, UI, and contract tests.

Any future map automation must extend and audit that subsystem. It must not introduce a second world-event or world-snapshot domain as proposed by the old document.

### A-11 — Fast tests already use a deliberate standalone structure

Severity: **Medium**

The repository contains 17 standalone `.NET 8` console contract-test projects. Replacing them with a new test framework is not required to gain one-command execution.

Required replacement: add an aggregate runner and a change-impact map while retaining the existing test projects unless a separate migration is justified.

## 5. Supported scenario surface

Support is conditional on the live scenario contract, not merely on the presence of an adapter.

| Scenario | Current route and critical facts | Automation consequence |
|---|---|---|
| Field battle | Land adapter; direct or terrain-resolved field scene | Validate both scene-selection branches over time |
| Village battle | Village adapter and village boundary contract | Validate village scene and boundary revision/hash |
| Siege assault | Exact campaign scene, deployment shell, engine identity/health, foot-only restrictions | Requires deployment and engine assertions plus client acknowledgements |
| Sally out | Distinct campaign and mission routing; blockade variants excluded | Verify correct controller set and reversed aftermath semantics |
| Siege ambush | Player-defender contract and native sally-out behavior | Verify role orientation and attacker engine result handling |
| Relief battle | Field-style battle with siege settlement context | Preserve and validate settlement identity |
| Lords hall | Player attacker, ordered participants, indoor scene/controller | Validate battle stage and ordered participant identity |
| Day hideout | Native hideout controller and reflected participant counts | Treat private API compatibility as version-sensitive |
| Night hideout | Ambush path, sentries/stealth, private fields, participant order | Treat private API compatibility as version-sensitive |
| Blockade / blockade sally out | Explicitly unsupported | Reject before process launch and never publish a result |

Before fixing a failure in one scenario, implementation work must check whether the same mechanism is shared by adjacent scenarios.

## 6. Disposition of the supplied proposal

| Proposal area | Disposition | Required correction |
|---|---|---|
| Goal of reducing manual campaign/server/client runs | Retain | Measure reductions by test level and evidence class |
| Test-only feature gating | Retain | Default off; require an internally consistent run profile and token |
| State-based waits | Retain | Bind waits to existing authoritative phase/readiness state |
| Run correlation and artifacts | Retain and strengthen | Add isolated root, role identity, sequence, token, binary identity, and result policy |
| Scenario record/replay | Rewrite | Archive existing boundary payloads instead of creating a parallel scenario model |
| Dedicated automation bridge | Rewrite | Use run-scoped atomic files first; defer named pipes until evidence requires them |
| Server-only lifecycle smoke | Reject | Replace with a smoke that stops at `PreBattleHold` and cannot publish consumable results |
| Automatic multiplayer client | Retain as essential | Promote it from optional convenience to the proof boundary for `BattleActive` and result generation |
| Full campaign end-to-end test | Retain | Require extraction from a real campaign state and applied-and-journaled result evidence |
| Full-AI deterministic replay | Narrow | Use deterministic inputs and structural/bounded assertions |
| Broad initial regression matrix | Stage | Begin with representative vertical slices, then expand to every supported adapter |
| New fast-test project/framework | Reject as default | Aggregate the 17 existing console contract-test projects |
| Generic `payload.bin` | Reject | Archive typed, versioned exact boundary payloads with hashes |
| Broad process cleanup | Reject | Stop only processes created and recorded by the run |
| Global map greenfield architecture | Reject | Extend the existing schema-10 prototype and tests |
| Immediate AGENTS.md automation policy changes | Defer | Update governance only after executable commands and evidence exist |

## 7. Required validation ladder

| Level | Name | What it may prove | What it may not claim |
|---|---|---|---|
| L0 | `EnvironmentDoctor` | Paths, versions, hashes, ports, configuration, clean isolation preconditions | Any gameplay behavior |
| L1 | Fast contracts and compile-only | Pure contracts, serializers, validators, source compilation | Bannerlord runtime behavior |
| L2 | `DedicatedSpawnSmoke` | Exact mission opens and materializes correctly through `PreBattleHold` | `BattleActive`, combat, canonical result, campaign aftermath |
| L3 | `ClientLifecycle` | Real peer connect, topology/snapshot transfer, entry selection, acknowledgements, `BattleActive`, battle result | Campaign extraction or writeback |
| L4 | `CampaignE2E` | Campaign capture, server/client battle, validated result application and journal | Unrelated UI or visual parity unless explicitly asserted |
| L5 | Sequential/soak/randomized | Reset safety, stale-state resistance, bounded stability, invariant coverage | Globally deterministic casualty sequences |

Manual verification remains appropriate for camera feel, UI usability, and visual parity that cannot be reduced to stable runtime facts.

## 8. Local native-server evidence

The audit decompiled selected installed assemblies to verify command semantics. This evidence is local, version-specific, and non-normative:

- the listed-server command surface includes `start_game_and_mission`, `start_game`, `start_mission`, and `end_mission`;
- mission start is asynchronous and proceeds through server registration/intermission/load state;
- HTTP acceptance is not proof that the mission became current and continuing;
- at least one native “battle started” signal depends on peer team changes.

Recorded local hashes for reproducibility of this audit only:

| Artifact | SHA-256 |
|---|---|
| Listed server assembly | `C7D27584FCE431B2D3734EB88C8DF52EF3B1BC8C5729F7FCE690CC277DA577E3` |
| Dedicated custom server assembly | `9C3105229ABDFCD3486B1E8BBC6A9F3309C61BF3B6B67E2BD8F01E8AABEF4BE6` |
| Dedicated starter executable | `A1A095ED807CE8EA710CC65DC0D6014A563EF7578EEF9DC293E5E401D7DEB41C` |
| Installed dedicated CoopSpectator assembly | `A21ED2F00465584B603FB67DFEA292EAFB37C258E22C1FAC1356B008862E92C1` |
| Installed client CoopSpectator assembly | `5D7372EEC2A63B2EF4A2324F30BF9868F900A37F96DD5A0834E42FB394B3A65D` |

These values must not be hard-coded as supported versions. The future environment doctor must discover and report the identities for each run.

The installed client hash above is historical evidence from the original audit. The Milestone 1 run later loaded client hash `9B271E4E0CFA3AD0FF2DB4B3ACC5A69AE6405E833D52ECB4E1A4C0FDCA8C1B31`; use the run-specific report rather than either value as a permanent supported-version rule.

## 9. Recommendation

Do not implement the supplied proposal directly. Use [BATTLE_TEST_AUTOMATION_SPEC.md](BATTLE_TEST_AUTOMATION_SPEC.md) as the canonical implementation specification.

The first implementation milestone should establish safety and trustworthy evidence before attempting to launch Bannerlord:

1. a verified side-effect-free compile mode;
2. an aggregate runner for the existing contract tests;
3. an environment and binary identity doctor;
4. a run-scoped, atomic, token-bound automation protocol;
5. explicit result-isolation policy and contract tests;
6. only then, a dedicated spawn smoke that ends safely at `PreBattleHold`.

This ordering addresses the current manual-testing burden without risking installed modules, live campaign results, unrelated processes, or false-positive runtime reports.

## 10. Milestone 1 runtime-feasibility addendum

The bounded run `M1-20260831T013501Z-4114d2df` tested the installed public Steam profile on `LAPTOP-4IUGGR23` without a build, deployment, `start_game`, mission, client connection, or campaign action.

Confirmed findings:

- `DedicatedCustomServer.Starter.exe` was the module-hosting dedicated role; its exact entry identity and owned `Watchdog`/`conhost` descendants were recorded and cleaned up gracefully;
- the dedicated `CoopSpectatorDedicated` module loaded and the stock token-file flow authenticated;
- the real multiplayer client loaded `CoopSpectator` and reached a responsive multiplayer window after Steam was started;
- exact bounded cleanup stopped only owned processes, required no forced termination, left the inventoried ports free, and released the exclusive probe lock;
- the monitored global battle bridge files were unchanged.

Changed assumptions and blockers:

- Steam is a real prerequisite for direct client launch on this profile;
- the installed/loaded client and dedicated modules are `0.3.1`, while repository outputs are `0.3.2` with different hashes;
- the dedicated module loaded from its `Win64_Shipping_Client` module directory, not from the project output's server directory;
- no-start-game authentication did not bind port `7210`;
- at the time of the M1 run, no supported run-scoped normal-lobby command/acknowledgement path drove the client to the exact owned server;
- a connection attempt remains unsafe until result publication is isolated or suppressed.

The canonical evidence, capability matrix, staging-mode disposition, and revised gate plan are in [BATTLE_TEST_AUTOMATION_M1_FEASIBILITY.md](BATTLE_TEST_AUTOMATION_M1_FEASIBILITY.md). Milestone 2A may proceed. Milestone 2B and L2–L5 runtime work remain gated by the blocked client connection/control rows and current-build identity proof.

Post-audit update: the developer reports that `0.3.2` was released after stable manual battle runs. This does not change the M1 loaded-binary observation or imply instability; it is a separate manual evidence source. A subsequent approved change added the default-off run-scoped normal-lobby control path in source and contract tests. That implementation is documented in [BATTLE_TEST_AUTOMATION_CLIENT_JOIN_IMPLEMENTATION.md](BATTLE_TEST_AUTOMATION_CLIENT_JOIN_IMPLEMENTATION.md), but no Bannerlord connection rerun has occurred, so the historical M1 runtime capability rows remain blocked.

## 11. Milestone 2A non-runtime addendum

The approved Milestone 2A implementation completed the audit's first four non-runtime recommendations. `m2a-contracts-20260831-07` passed all 20 reviewed contract projects, and `m2a-compile-only-20260831-03` independently compiled both current `0.3.2` projects below an isolated run root while recursive installed-module inventories remained identical. The runner now provides the named environment doctor, run/role/nonce/lease/event protocol, stable outcomes/reasons, assertions, artifact categories, reproduction descriptor, and required file-fault contracts.

No product process was launched by those runs. Result isolation, current-build staging/loaded-hash proof, runtime ownership/cleanup/recovery, live connection, mission opening, and battle evidence remain outside Milestone 2A. The exact completion audit and artifact boundaries are in [BATTLE_TEST_AUTOMATION_M2A_IMPLEMENTATION.md](BATTLE_TEST_AUTOMATION_M2A_IMPLEMENTATION.md).
