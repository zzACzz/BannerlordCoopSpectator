# Battle Test Automation Specification

Status: **Canonical implementation specification — implementation in progress**
Specification date: **2026-09-02**
Revision: **17 — Milestone 2B safety closure implemented and contract-verified; live confirmation pending**
Live-evidence repository baseline: **`d0648edf2f5033ec1ff48efe547a645c587e2ca1`** (`d0648ed`)
Current published connection corrections: **`d1af692`**, **`3fdcda3`**
Companion audit: [BATTLE_TEST_AUTOMATION_AUDIT.md](BATTLE_TEST_AUTOMATION_AUDIT.md)

| Revision | Summary |
|---|---|
| 1 | Initial source-grounded target architecture and evidence ladder |
| 2 | Practical P0 sequencing, level-specific scenario criteria, staging, lifecycle, failure, fixture, and resource policies |
| 3 | L3 result consistency, multi-client role instances, milestone applicability, completion-mode boundaries, and exactly-once capability gate |
| 4 | Mandatory natural-completion coverage for every supported battle adapter, natural L4 claim boundaries, and fast-versus-full suite policy |
| 5 | Default-off run-scoped client launch/join intent, exact local-server selection, secret handling, acknowledgement states, and native-join deadline semantics |
| 6 | Implemented compile-only, aggregate-runner, environment-doctor, run/protocol, assertion, fault-injection, and artifact contracts for Milestone 2A |
| 7 | Fail-closed loaded-role identity, run-scoped host ownership, result suppression, exact cleanup/recovery, and the minimum native Team Deathmatch bootstrap required for connection feasibility |
| 8 | Separate native console-readiness and command-acceptance evidence, singular runner result objects, and PID-correlated native log capture required by clean live-rerun findings |
| 9 | Exact owned-process native-output readiness/readback, primary-versus-terminal outcomes, and PID-log collection implemented and contract-verified before another live run |
| 10 | Immediate provisional ownership after process creation, bounded executable-path acquisition, exact-path validation, correct internal-failure classification, and cleanup despite identity-enrichment failure |
| 11 | Redirected standard handles demoted to supplementary evidence; a run-scoped dedicated readiness, command-intent, and acknowledgement channel is required before another live connection attempt |
| 12 | Live-proven dedicated readiness/seven-step bootstrap/UDP ownership, UTC-type-safe schema-v4 client handoff, retained client cleanup identity, and required-versus-optional PID-log evidence |
| 13 | Corrected exact-assembly client resolver live-proven; one bounded native platform-login attempt, explicit login outcomes, terminal client-status retention, and automatic client PID-log evidence implemented and contract-verified before server discovery |
| 14 | Native platform login and repeated server-list retrieval live-proven; `Map`/`UniqueMapId` selection semantics corrected after installed-runtime inspection, with the clean connection rerun still pending |
| 15 | Corrected exact server selection and native join live-proven; the handoff observer moved to the actual `GameNetwork.StartMultiplayerOnClient` boundary, and timeout reports retain the last validated non-terminal client status |
| 16 | Handoff-corrected client loaded and terminal `Connected` live-proven; Milestone 1 capability blockers closed; remaining Milestone 2B role-liveness, machine-lock, cancellation/recovery-contract, and crash/hang evidence gaps made explicit |
| 17 | Protocol 1.1 role-health, shared-lock, cancellation, RecoveryV2, and failure-evidence source/contracts implemented; real console cancellation and exact synthetic ownership/recovery contracts passed; clean staging/live confirmation remains pending |

The companion audit remains the source-fact baseline. Revisions 2–17 refine implementation requirements and ordering without changing the audit's historical source findings. The narrow client launch/join slice, Milestone 2A non-runtime foundation, Milestone 2B runner-safety foundation, dedicated-control channel, and Milestone 2B.3A safety closure now exist. Their evidence boundaries are recorded in [BATTLE_TEST_AUTOMATION_CLIENT_JOIN_IMPLEMENTATION.md](BATTLE_TEST_AUTOMATION_CLIENT_JOIN_IMPLEMENTATION.md), [BATTLE_TEST_AUTOMATION_M2A_IMPLEMENTATION.md](BATTLE_TEST_AUTOMATION_M2A_IMPLEMENTATION.md), [BATTLE_TEST_AUTOMATION_M2B1_RUNTIME_FOUNDATION.md](BATTLE_TEST_AUTOMATION_M2B1_RUNTIME_FOUNDATION.md), [BATTLE_TEST_AUTOMATION_M2B2_FEASIBILITY.md](BATTLE_TEST_AUTOMATION_M2B2_FEASIBILITY.md), [BATTLE_TEST_AUTOMATION_M2B2C_RUNNER_CORRECTION.md](BATTLE_TEST_AUTOMATION_M2B2C_RUNNER_CORRECTION.md), [BATTLE_TEST_AUTOMATION_M2B2D_DEDICATED_CONTROL.md](BATTLE_TEST_AUTOMATION_M2B2D_DEDICATED_CONTROL.md), and [BATTLE_TEST_AUTOMATION_M2B3_SAFETY_CLOSURE.md](BATTLE_TEST_AUTOMATION_M2B3_SAFETY_CLOSURE.md).

Run `m2e-live-r1-01` first live-proved native platform login and repeated server-list retrieval but exposed the invalid scene-`Map`-as-`UniqueMapId` filter. Published correction `e55f1bd` removed that filter. Clean run `m2e1-live-r1-01` then live-proved exact server selection, native `RequestJoinCustomGame(...)` acceptance, the real `GameNetwork.StartMultiplayerOnClient(...)` call, one-shot same-machine loopback rewrite, `InCustomGame`, native `Join game successful`, server `CreatePlayer`, and the visible `Awaiting Server` state. Its formal result remained `Timeout` because the controller's `NetworkHandoff` notification was attached only to a historical four-argument `LobbyGameStateCustomGameClient.StartMultiplayer` signature that does not exist in installed `1.4.8`; the actual parameterless lobby method calls `GameNetwork.StartMultiplayerOnClient(...)`. Published correction `d1af692` moves notification ownership to that actual lowest-level boundary, and `3fdcda3` preserves the last validated non-terminal client status in timeout reports without changing the outcome. Focused contracts and canonical run `m2e1-handoff-fix-contracts-20260901-01` passed at 22/22. Fresh clean-published run `m2e1-handoff-pub-compile-20260901-01` from exact local/upstream revision `a7bd528` produced selected client SHA-256 `7CC2D759806D2F02D8BEBA15BCBC01EF61DDE4975004728F3D7E6C8332977E97`, and controlled transaction `m2e1-handoff-stage-r1` installed only its DLL/PDB while retaining the complete prior tree. Clean run `m2e1-handoff-live-r1-01` then loaded that exact client plus dedicated SHA-256 `BD328AAC4F2A64C28D3EDCE28BCE3D72FF164BDAF817D9460B302ED538702A78`, observed `NetworkHandoff`, reached terminal `Connected`, retained native join and server `CreatePlayer` evidence, returned `Pass`, cleaned both exact roles gracefully without force, released the ports, and preserved the protected result. This closes the Milestone 1 connection/control blockers but proves no campaign, cooperative mission, battle, L2, or L3 behavior. Milestone 2B.3A now implements the previously missing safety surface at source/contract level; staging and one clean live confirmation remain pending.

## 1. Purpose

The project currently requires a developer to start or load a campaign encounter, start a dedicated server mission, launch a multiplayer client, join the server, select an entry, start the battle, and inspect the outcome after most changes.

This specification defines a safe automation system that reduces those manual runs while preserving the real Bannerlord campaign, dedicated-server, multiplayer-client, mission, and result-writeback paths.

The target is not “a test that says the battle works.” The target is a ladder of evidence that states exactly what was exercised:

- pure contracts and compilation;
- dedicated mission opening and spawn materialization;
- real multiplayer peer readiness and battle lifecycle;
- real campaign extraction and exactly-once result application;
- repeated-run cleanup and stability.

Implementation of this specification requires a separate approved implementation plan. This document does not authorize code, build, deployment, runtime, or Git changes by itself.

## 2. Normative language

`MUST`, `MUST NOT`, `REQUIRED`, `SHOULD`, and `MAY` are normative.

- **Run**: one isolated invocation with a unique `RunId`.
- **RoleType**: `Runner`, `CampaignHost`, `DedicatedServer`, or `MultiplayerClient`.
- **RoleInstanceId**: the unique identity of one role instance inside a run.
- **Authoritative state**: the existing module or TaleWorlds state that owns the fact being tested.
- **Observer state**: a read-only automation projection of authoritative state.
- **Canonical result**: a result eligible for consumption by a campaign host.
- **Fixture**: immutable recorded boundary data plus provenance and integrity metadata.
- **Test profile**: the complete opt-in configuration that enables automation-only behavior.
- **Active milestone**: the single approved implementation milestone whose requirements may be claimed in the current completion report.
- **Controlled lifecycle completion**: an authoritative, automation-requested mission completion after the required L3 readiness and control evidence has been reached; it is not proof of natural combat completion.
- **Natural battle completion**: a mission outcome reached through the normal Bannerlord combat-end and victory/defeat path without an automation-requested early completion, forced winner, direct result write, or synthetic phase transition.
- **Full scenario coverage**: the maintained evidence set for a supported adapter includes its required L2 facts, controlled L3 lifecycle, at least one natural L3 completion, and any L4 evidence explicitly claimed for that adapter.

A run MAY contain multiple `MultiplayerClient` role instances. The first P0 field lifecycle requires one client, but the protocol, manifest, status, event, and process models MUST NOT assume that `RoleType` is unique within a run.

## 3. Goals

### G-001 — Reduce manual validation frequency

Provide one-command validation at each evidence level so most source changes can be checked before a full manual campaign/server/client run.

### G-002 — Exercise production paths

Runtime tests MUST use the normal encounter contracts, mission-opening route, native physical spawn path, network transfer, readiness acknowledgements, authoritative combat state, result builder, and campaign writeback path appropriate to the selected level.

### G-003 — Produce trustworthy evidence

Every run MUST retain enough identity, state, log, payload, and result evidence to explain what ran and why it passed, failed, timed out, or was blocked by the environment.

### G-004 — Be safe for the developer machine and campaign

Automation MUST NOT deploy during a compile-only check, consume a smoke result in a campaign, terminate unrelated processes, reuse stale bridge state, or silently test binaries different from the recorded source/build identity.

### G-005 — Cover every supported battle adapter incrementally

The final suite MUST cover field, village, siege assault, sally out, siege ambush, relief, lords hall, day hideout, and night hideout. Unsupported blockade variants MUST be rejection tests.

### G-006 — Prove natural battle completion

The final suite MUST include at least one maintained `NaturalBattleEnd` run for every supported battle adapter. Reaching `PreBattleHold`, reaching `BattleActive`, or producing a result through `ControlledLifecycleEnd` MUST NOT by itself be described as full battle coverage. Controlled completion remains the fast lifecycle gate; natural completion is the required proof of native combat-end routing, outcome reconciliation, and post-mission cleanup.

## 4. Non-goals

The first implementation MUST NOT attempt to:

- replace Bannerlord networking with an in-memory substitute for runtime proof;
- replace `BattleStartMessage` or `BattleSnapshotMessage` with another authoritative scenario model;
- make TaleWorlds AI and combat globally deterministic;
- treat a server-only mission as proof of a complete multiplayer lifecycle;
- automate camera feel, UI usability, or subjective visual parity;
- replace the existing standalone contract-test inventory with a new test framework;
- introduce named pipes before a run-scoped atomic file protocol is shown insufficient;
- implement campaign-map automation; that work requires a separate future specification built on the shared runner contracts;
- enable automation or verbose diagnostics in normal production runs.

## 5. Protected architecture and authority rules

### SAF-001 — One source of truth

Automation MUST observe the existing `CoopBattlePhaseRuntimeState`, network readiness state, spawn state, and result/writeback state. It MUST NOT maintain an independent battle phase that can overrule or substitute for them.

### SAF-002 — Normal intent paths

Commands such as side selection, entry selection, ready acknowledgement, and battle start MUST pass through the existing validation and authority paths used by a real peer. Test code MUST NOT set phase fields, fabricate acknowledgements, inject physical agents, or mark completion directly.

### SAF-003 — Native physical spawn ownership

The server's native mission stack remains the only physical agent spawner. Automation MAY validate plans and observe agents but MUST NOT add a parallel test spawner.

### SAF-004 — Default-off profile

Automation-only behavior MUST be disabled unless all required profile values are present and mutually consistent:

- `COOPSPECTATOR_TEST_AUTOMATION=1`;
- an absolute automation run root;
- a valid `RunId`;
- an unpredictable per-run nonce used to reject stale or cross-run commands;
- an explicit role;
- an explicit result policy.

An incomplete or malformed profile MUST fail closed and preserve normal production behavior.

The runner generates the nonce before launching any role. It is a local correlation and accidental-cross-talk safeguard, not authentication against a malicious local process. P0 MUST NOT add a complex cryptographic protocol unless the transport or threat model changes. The plaintext nonce MAY exist only in current-user-protected role environment or run control records; it MUST NOT appear in process arguments, fixture metadata, shareable artifacts, or reproduction commands. The manifest stores only its non-reversible fingerprint. A missing or mismatched nonce MUST reject the record with a distinct stable reason; it is never repaired by guessing or silently adopting another run.

### SAF-005 — No ungated diagnostics

New diagnostic work in tick, spawn, network-message, agent, or mission hot paths MUST be disabled by default or gated by the explicit test profile or existing verbose diagnostics. Runtime-sensitive native state inspection requires a separate risk justification in the implementation plan.

### SAF-006 — Adjacent scenario review

Before fixing any failure found by automation, the implementation task MUST inspect all other battle types sharing the affected detector, adapter, mission shell, spawn path, readiness gate, or writeback path.

## 6. Evidence levels and run modes

### L0 — `EnvironmentDoctor`

Purpose: prove that a run can start safely.

It MUST validate and record:

- repository revision and dirty state;
- repository line-ending and local Git policy through the existing hygiene script, reported separately from runtime compatibility;
- game, dedicated server, launcher, module, and dependency paths;
- file versions and SHA-256 values of the client and dedicated mod assemblies;
- selected TaleWorlds assembly identities needed for compatibility;
- build profile and expected artifact source;
- required ports and current owning processes;
- availability of writable run and artifact roots;
- absence of a conflicting live run using the same identifiers or ports;
- enabled experimental features relevant to the scenario.

It MUST NOT mutate installed modules merely to determine whether the environment is valid.

Repository hygiene failure MUST block a source-based completion or pre-commit claim. It SHOULD remain a clearly visible warning, rather than the sole runtime blocker, when an explicitly requested diagnostic run uses already installed binaries and does not build or claim source equivalence.

### L1 — `FastContracts`

Purpose: execute pure or source-level checks without launching Bannerlord.

It MUST include:

- an aggregate run of every project in the reviewed contract-test inventory; the current baseline count is 17;
- a full-suite option that remains authoritative for contract-test completion;
- the side-effect-free compile operation after that operation is implemented and proven safe;
- structured per-project duration, outcome, stdout, and stderr.

A selected subset MUST be reported as a subset and MUST NOT be described as the full contract suite.

Automated source-diff impact selection is deferred until stable runtime fixtures and scenario tags exist. Before then, focused selection MAY be explicit, but the full inventory remains the completion authority.

### L2 — `DedicatedSpawnSmoke`

Purpose: prove the dedicated mission opens and materializes the scenario correctly without claiming a complete battle.

The pass boundary is `PreBattleHold` or the scenario-equivalent stable pre-battle state. A passing run MUST prove:

- the requested mission and exact scenario route opened;
- campaign/battle/stage identity matches the fixture;
- expected scene and mission shell are active;
- expected teams, formations, and entry identities exist;
- expected initial agents materialized through the native spawn path;
- equipment, mounts, hero identities, and scenario structures match stable assertions;
- no unhandled exception or fatal invariant failure occurred;
- cleanup completed without a consumable campaign result.

It MUST NOT claim:

- `BattleActive`;
- player control or peer readiness;
- authoritative combat completion;
- correct final casualties;
- canonical result correctness;
- campaign writeback.

### L3 — `ClientLifecycle`

Purpose: prove a real multiplayer client can complete the normal readiness path and participate in the authoritative mission.

A passing run MUST prove:

- the client connected to the owned dedicated process;
- the pre-mission topology contract was received and accepted;
- the full snapshot was transferred, decoded, and acknowledged;
- the client loaded the expected scene;
- side and entry selection used normal authority validation;
- required materialization/map/scenario acknowledgements reached the server;
- the server reached `BattleActive` through normal gates;
- the controlled authoritative agent became active where the scenario permits it;
- the declared `ControlledLifecycleEnd` or `NaturalBattleEnd` boundary produced one isolated authoritative result;
- both processes shut down or returned to their expected post-mission state cleanly.

This level MAY use an automated visible client. Headless operation is not a requirement unless source/runtime investigation proves it compatible with the actual client path.

The first L3 vertical slice MAY use `ControlledLifecycleEnd` to establish a fast, diagnosable developer loop. That run proves readiness, control, result publication, disposal, and cleanup only. A supported adapter is not fully covered at L3 until a separate maintained `NaturalBattleEnd` run also passes for that adapter.

### L4 — `CampaignE2E`

Purpose: prove the complete campaign-to-battle-to-campaign workflow.

A passing run MUST prove:

- a real campaign host created the start and snapshot contracts from a live supported encounter or a controlled test save;
- the same run started the dedicated mission and client lifecycle;
- the dedicated result matches campaign, battle instance, and battle stage identity;
- the campaign accepted the result exactly once;
- the correct scenario adapter applied aftermath;
- the consumed result was journaled exactly once after successful application;
- a repeated notification or stale result cannot apply the aftermath twice.

A server replay from a recorded roster is not `CampaignE2E` because it does not prove campaign extraction or writeback.

Controlled L4 runs MAY be retained for recovery, duplicate-result, and interruption testing. An adapter MUST NOT be described as fully covered at L4 until at least one correlated L4 run for that adapter reaches `NaturalBattleEnd` and proves the applicable natural outcome, result, aftermath, and journal assertions.

### L5 — `Sequential`, `Soak`, and `RandomizedInvariant`

Purpose: find stale static state, cleanup races, ordering defects, and bounded stability problems.

These modes MUST build on a passing lower-level scenario. They MUST record every iteration separately while retaining a parent run identity.

The maintained L5 inventory MUST include selected `NaturalBattleEnd` iterations after the corresponding natural L3/L4 scenario is stable. Repeating only `PreBattleHold` or `ControlledLifecycleEnd` runs cannot establish full-battle sequential or soak stability.

Randomized tests MUST randomize supported inputs and ordering while asserting invariants. They MUST NOT require identical AI casualty sequences across runs.

L5 runs MUST sample per-owned-process CPU time, private working set, handle count, process count, log and artifact growth, free artifact-disk space, and iteration duration. Resource thresholds begin as diagnostic baselines and become functional failure limits only after an approved calibration records the machine profile, scenario, sample method, and allowed variance.

### ENV-001 — Initial execution profile

The initial supported runtime profile is a local Windows developer machine with verified Bannerlord client and dedicated-server installations. L0 and L1 MAY later run in CI. L2–L5 MUST NOT be advertised as CI-capable, unattended, or portable until launcher, authentication, anti-cheat, local-connect, display/session, licensing, and crash-reporting prerequisites are verified for a named machine profile.

### ENV-002 — Game and server version matrix

The environment doctor MUST compare the active client, dedicated server, TaleWorlds assemblies, module binaries, fixtures, and controlled saves against an explicit compatibility matrix. The current documentation conflict between client references to 1.3.14 and dedicated staging references to 1.4.8 is unresolved; until verified together, no runtime profile may claim either combination as supported. A mismatch is `EnvironmentBlocked` with exact version and hash evidence.

## 7. Outcome taxonomy

Every terminal run and every scenario iteration MUST use exactly one primary outcome:

| Outcome | Meaning |
|---|---|
| `Pass` | All mandatory assertions for the selected level were observed |
| `EnvironmentBlocked` | Required path, version, hash, port, permission, process, or configuration precondition was not satisfied |
| `PreconditionsFailed` | The input scenario or requested mode was invalid before the tested runtime action began |
| `AssertionFailed` | The product ran but a required state or invariant was false |
| `Crash` | An owned product process terminated unexpectedly or produced fatal crash evidence |
| `Timeout` | A required authoritative state did not arrive within its configured deadline |
| `RunnerInternalError` | The orchestrator failed independently of a product assertion |
| `Cancelled` | The user or owning controller explicitly interrupted the run and cleanup was attempted |

An HTTP success response, file existence, process existence, or elapsed delay is never sufficient by itself for `Pass`.

### OUT-001 — Stable process exit codes

The runner MUST return a stable non-zero process exit code for every non-pass outcome:

| Outcome | Exit code |
|---|---:|
| `Pass` | 0 |
| `EnvironmentBlocked` | 10 |
| `PreconditionsFailed` | 11 |
| `AssertionFailed` | 20 |
| `Crash` | 30 |
| `Timeout` | 31 |
| `RunnerInternalError` | 40 |
| `Cancelled` | 50 |

A multi-scenario invocation MUST preserve every per-scenario outcome and select its invocation outcome in this order: `RunnerInternalError`, `Crash`, `Timeout`, `AssertionFailed`, `Cancelled`, `PreconditionsFailed`, `EnvironmentBlocked`, `Pass`. It returns the exit code mapped to that selected outcome.

### OUT-002 — Failure reasons

Primary outcomes MUST be refined by stable reason codes without creating false pass outcomes. The initial reason vocabulary MUST cover at least `RunIdMismatch`, `NonceMismatch`, `TopologyRejected`, `SnapshotDecodeFailed`, `MaterializationAckTimeout`, `ReadinessGateStuck`, `ControlledAgentNotSpawned`, `ResultIdentityMismatch`, `NoHeartbeat`, `NoProgress`, and `CrashReporterDetected`.

### OUT-003 — Retry and reproduction policy

Automatic retry is disabled by default. An explicitly requested retry MUST create a distinct attempt with its own evidence and MUST NOT replace or downgrade the first outcome. Every failed, crashed, timed-out, or cancelled run SHOULD retain a redacted one-command reproduction descriptor containing the exact fixture, profile, mode, binary identities, and non-secret parameters.

### OUT-004 — Known native issues

A known TaleWorlds or operating-system issue MAY annotate an observed non-pass result, but MUST NOT convert it to `Pass`. The annotation MUST include exact affected versions/hashes, an evidence reference, quarantine reason, review/expiry condition, and original outcome. An unexpected pass of a quarantined case MUST trigger review rather than silently removing the record.

### OUT-005 — Singular structured runner result

Each aggregate runner command MUST emit exactly one structured result object containing its primary `Outcome`, reason, and artifact path. Side-effect helpers, cleanup operations, process waits, collection mutations, native-tool calls, and diagnostic probes MUST NOT leak incidental values into that result pipeline. The final manifest, runner status, process exit code, reproduction descriptor, and command-specific report MUST preserve the same primary outcome unless a later cleanup or manifest-publication failure legitimately supersedes it as `RunnerInternalError`; any supersession MUST retain the original product outcome as a separate field rather than erase it.

The validator MUST handle the native result shape of every supported shell. In particular, Windows PowerShell 5.1 `[ordered]` dictionaries MUST be validated by exact dictionary keys and normalized before property access. Zero results, multiple results, incidental helper output, null results, and missing required keys/properties MUST fail explicitly.

Contract tests MUST exercise at least pass, assertion failure, timeout followed by graceful cleanup, timeout followed by forced cleanup, already-exited descendants, and cleanup failure. They MUST fail if the command returns zero or multiple result objects or if the final manifest disagrees with the command-specific report without an explicit supersession record.

## 8. Run isolation protocol

### RUN-001 — Root

Each run MUST have an absolute isolated root, by default under:

```text
%TEMP%\CoopSpectator\Automation\<RunId>
```

The caller MAY provide another artifact root. Production bridge locations MUST remain unchanged when the automation profile is disabled.

### RUN-002 — Required artifact categories

Every run MUST separate manifest, commands, role status, ordered events, exact payloads, logs/crashes/results/process evidence, and temporary work. The following physical layout is RECOMMENDED, not a compatibility contract:

```text
<RunRoot>/
  manifest.json
  commands/
    inbox/
    processed/
  status/
    runner.json
    campaign-host-01.json
    dedicated-server-01.json
    multiplayer-client-01.json
    multiplayer-client-02.json
  events/
    events.jsonl
  payloads/
  artifacts/
    logs/
    crashes/
    results/
    processes/
  work/
```

Equivalent layouts MAY be used when the same categories, isolation, and discoverability are preserved. Additional files MUST have their purpose and retention documented in `manifest.json`.

### RUN-003 — Manifest identity

The manifest MUST include at least:

- protocol and manifest schema versions;
- `RunId`, creation time in UTC, requested level, scenario kind, and stage;
- random per-run nonce fingerprint, never the plaintext nonce;
- repository revision and dirty state;
- runner build identity;
- client and dedicated module version and SHA-256;
- game and dedicated executable versions;
- effective feature flags, result policy, and declared completion mode where applicable;
- a role-instance list containing `RoleType`, `RoleInstanceId`, capabilities, executable path, process ID, and start time after launch;
- observed parent IDs and process-tree snapshots when the operating-system launcher path exposes them reliably;
- ports and their verified owners;
- input fixture identity and hashes;
- configured state deadlines;
- terminal outcome and terminal reason when complete.

### RUN-004 — Atomic writes

Commands, status snapshots, manifests, and final result metadata MUST be published by write-to-temporary-file followed by same-volume replace/move semantics. Existing `AtomicBridgeFileIO` behavior SHOULD be reused or extracted into a shared contract.

Readers MUST tolerate sharing and MUST reject malformed, truncated, stale, or identity-mismatched content.

### RUN-005 — Role correlation and cross-run rejection

Every command and status record MUST carry:

- protocol version;
- `RunId`;
- source and target `RoleType`/`RoleInstanceId`;
- monotonically increasing sequence number within its stream;
- issued/updated UTC time;
- the per-run nonce or equivalent local correlation proof;
- campaign, battle instance, and battle stage identity when known.

A role instance MUST reject commands for another run, nonce, role type, role instance, campaign, battle, or stage. Duplicate commands MUST be idempotent or rejected with a stable duplicate outcome. Sequence numbers are monotonic per role-instance stream, not merely per role type.

### RUN-006 — Observer behavior

Each role-instance status MUST project authoritative state and cite its source. For example, a battle-phase field must identify `CoopBattlePhaseRuntimeState` as its source rather than a runner-assigned value.

The event journal MUST be append-safe and ordered by per-role-instance sequence. Every role instance MUST record UTC wall-clock time and monotonic elapsed time from role-instance start. Cross-process wall-clock order MAY be reconstructed but MUST NOT be treated as perfectly synchronized or used as the sole correctness oracle.

### RUN-007 — Exclusive ownership

The runner MUST acquire exclusive ownership of the `RunId`. It MUST refuse a root that contains an active run or identity-incompatible artifacts. Reuse for read-only inspection MAY be supported through a separate command that cannot launch or mutate roles.

Ownership MUST include a lease containing runner process identity, process start time, lease creation time, last heartbeat, and expiry policy. An expired lease marks a run as potentially abandoned, not safe to delete. The runner MUST require read-only inspection followed by explicit exact-root cleanup or recovery.

### RUN-008 — Parallel execution and machine locks

L0 and L1 runs MAY execute concurrently only when output/intermediate directories and ports are isolated. L2–L5 MUST acquire an exclusive runtime lock for every shared game installation, dedicated installation, module location, user/profile bridge root, and fixed port set they can mutate or consume. A lock conflict is `EnvironmentBlocked`; automation MUST NOT attach to or terminate the other run.

### RUN-009 — Protocol compatibility

The command/status protocol MUST define major and minor versions. An unknown major version MUST be rejected. Minor-version compatibility, unknown-field behavior, and required capabilities MUST be explicit. Before accepting control commands, each role instance MUST report its supported protocol version, capabilities, binary identity, `RunId`, `RoleInstanceId`, and nonce correlation result.

### RUN-010 — File-protocol fault injection

Contract tests MUST cover concurrent read/write, malformed and partial content, duplicate/reordered/stale commands, process failure before and after acknowledgement, repeat reads of processed commands, temporary file locks, and simulated write failure. Local same-volume atomic semantics are the initial supported storage profile. Network shares or cross-volume publication MUST be rejected or treated as unverified until separately tested.

## 9. Build safety and binary identity

### BLD-001 — Explicit compile-only mode

Both project files MUST implement a shared, explicitly named compile-only property. The recommended public property is:

```text
CoopCompileOnly=true
```

When enabled:

- `DeployModToGame` MUST NOT run;
- `BuildAndDeployDedicatedModule` MUST NOT run implicitly;
- `DeployServerToDedicated` MUST NOT run;
- no installed client or dedicated module path may be created, removed, or changed;
- output and intermediate paths MUST be caller-configurable so automation can place them under the run root;
- the client and dedicated projects MUST be invokable explicitly and independently;
- build diagnostics MUST state that compile-only mode is active.

The property MUST default to false to preserve the documented developer deployment workflow.

### BLD-002 — Proof of no external writes

Contract validation for compile-only mode MUST inspect the project targets. The first runtime verification of the mode MUST also compare relevant installed module directories before and after the build. The test passes only when no installed file changed.

### BLD-003 — Tested binary match

Before any L2–L5 run, the environment doctor MUST hash the actual assemblies loaded or about to be loaded from the client and dedicated module locations. Expected and actual identities MUST match the manifest. A mismatch is `EnvironmentBlocked`.

### BLD-004 — No implicit stale-output trust

Files in `dist/`, module output folders, root DLL copies, or installed modules MUST NOT be assumed current based on timestamp alone.

### BLD-005 — Runtime binary staging policy

Every L2–L5 run MUST choose and record one runtime binary mode:

| Mode | Use | Mandatory behavior |
|---|---|---|
| `UseExistingInstalled` | Explicit diagnosis of the currently installed module | No source-equivalence claim unless installed hashes match an identified build output |
| `StageIsolated` | Preferred automated mode if Bannerlord supports an isolated module root or equivalent verified arrangement | Stage only into a run-owned or automation-owned location without changing the developer's normal module |
| `DeployWithRestore` | Fallback only when isolated staging is infeasible | Explicit opt-in, exclusive installation lock, exact pre-image backup, atomic/copy verification, and bounded restore with before/after hashes |

The feasibility milestone MUST determine which modes are actually supported by the installed client and dedicated server. No mode may be advertised before a real load proves it.

### BLD-006 — End-to-end binary identity chain

The manifest MUST correlate the repository revision, compile output hash, staged/deployed hash, process-loaded assembly path, and hash reported by the loaded role. A mismatch is `EnvironmentBlocked`. A failed restore is `RunnerInternalError`, preserves all recovery evidence, and blocks further runtime runs against that installation until explicit inspection.

| Identity state | Meaning | Allowed claim |
|---|---|---|
| `ConfirmedLoadedHash` | The attached role reports its loaded assembly path and hash, matching the intended staged/deployed file | Full source-equivalent L2–L4 claims |
| `ConfirmedPathHashOnly` | The intended on-disk path/hash was verified before launch, but the loaded role did not confirm it | Diagnostic evidence only; no source-equivalent runtime pass |
| `UnverifiedLoadedIdentity` | Loaded source/path cannot be established | `EnvironmentBlocked` for source-equivalent runtime testing |
| `LoadedHashMismatch` | Loaded role reports a different path or hash | `EnvironmentBlocked` and staging/installation investigation |

## 10. Process and mission lifecycle

### PROC-001 — Exact process ownership

The runner MUST record every process it creates and every verified descendant by process ID, executable path, and start time; it SHOULD also record parent relationship and a process-tree snapshot when observable. Cleanup MUST target only verified owned processes.

Immediately after `Process.Start()` returns a PID, the runner MUST establish a provisional cleanup identity bound to the exact requested executable path and launch operation before any fallible identity-enrichment step. Executable-path acquisition MUST be bounded and MUST NOT rely exclusively on a single immediate `Process.Path` read. On Windows, a null/transient path MAY fall back to a verified `Win32_Process.ExecutablePath` record or bounded retry, but the resolved full path MUST equal the exact requested executable before hashing or promotion to verified ownership. Failure to enrich identity after successful creation MUST be `RunnerInternalError`, MUST retain enough provisional PID/path/start/parent evidence for exact cleanup, and MUST still execute cleanup. It MUST NOT escape as `AssertionFailed` or leave a created process absent from recovery evidence.

Broad termination by executable name, service name, or wildcard is prohibited.

On Windows, the runner SHOULD use a Job Object or an equivalently verified ownership mechanism when compatible with the Bannerlord launcher chain. Process parentage is supporting evidence, not the sole ownership proof.

### PROC-002 — Port ownership

Before launch, required ports MUST be free or owned by a process explicitly adopted into the run. After launch, the manifest MUST record the verified process owning each port.

### PROC-003 — State-based startup

The runner MUST distinguish:

- process created;
- server registered/available;
- game started;
- mission start command accepted;
- mission current and continuing;
- module behavior attached;
- roster/snapshot loaded;
- authoritative phase reached.

Short polling/debounce delays MAY be used internally, but elapsed time alone MUST NOT advance the run state.

### PROC-004 — Bounded waits

Every wait MUST have a named configurable deadline, last-observed state, and timeout artifact. Every live role MUST publish a heartbeat independently of state progress. The runner MUST distinguish `NoHeartbeat` from `NoProgress`: a responsive process can remain stuck forever in one state. A timeout MUST identify the expected state, current state, responsible role, time since heartbeat, time since progress, elapsed duration, and relevant recent events.

### PROC-005 — Graceful cleanup

Cleanup order SHOULD be:

1. stop or end the owned mission through its normal command path;
2. request graceful role shutdown;
3. wait for bounded exit;
4. terminate only still-running owned processes;
5. record exit codes and final process inventory;
6. verify that no run-scoped command remains unprocessed and no owned process remains.

If cleanup cannot stop an owned process, the run MUST remain non-pass, retain the runtime lock, and emit exact recovery instructions. A force-cleanup operation MUST preview exact process and file targets and revalidate ownership immediately before acting.

`Recover` MUST remain read-only until the user selects an exact recovery action. It MUST revalidate process ID, executable path, and process start time; show every proposed process/file/lock action; launch no new product process; never delete the run root automatically; and write a recovery report. It may release a runtime lock only after verifying that no owned process or shared bridge/staging state remains active.

### PROC-006 — Back-to-back reset proof

The suite MUST run at least two scenarios sequentially in the same supported lifecycle where applicable and verify reset of mission statics, peer payload state, acknowledgements, chunk/reconnect state, village state, ladder state, bridge identity, and result publication guards.

### PROC-007 — Role lifecycle projections

The runner MUST define allowed orchestration transitions for each role. These are projections and coordination states; they MUST NOT replace or advance the authoritative module phase.

Minimum dedicated projection:

```text
Created -> ProcessStarted -> ModuleAttached -> InputAccepted
  -> MissionOpening -> MissionCurrent -> Materializing -> PreBattleHold
  -> [BattleActive] -> CompletionRequested -> MissionDisposed -> Exited
```

Minimum client projection:

```text
Created -> ProcessStarted -> ModuleReady -> Connecting -> TopologyAccepted
  -> SnapshotDecoded -> SceneLoaded -> EntrySelected -> ReadinessAcknowledged
  -> ControlledAgentActive -> PostMission -> Exited
```

Square-bracketed states are level-dependent. Each transition MUST name its authoritative evidence source, and invalid commands for the current state MUST be rejected with a stable reason. A reconnect MUST be represented explicitly rather than silently regressing the same role state.

Every role-instance status MUST include `HeartbeatUtc`, monotonic elapsed time, `LastProgressUtc`, monotonic time since progress, `StateEnteredUtc`, state revision, authoritative source, and last structured error.

### PROC-008 — Crash, modal, and swallowed-failure detection

The feasibility milestone MUST inventory native crash reporters, Windows error dialogs, launcher helpers, and other blocking descendants for the active machine profile. An owned crash reporter or modal associated with an owned product process MUST classify the product attempt as `Crash`, collect evidence, and enter owned cleanup; it MUST NOT be treated as a generic timeout or terminated by broad name.

When product code catches an exception but can no longer guarantee correctness of the current automated battle, the test profile MUST publish a structured `FatalAutomationFailure` event while retaining the normal production log. The event MUST include a stable failure code, role, stage, exception type/message/stack when available, relevant mission and stable entity identities, and whether the process or only the test is fatal.

On crash or no-progress timeout, the runner SHOULD collect a crash or hang dump when available. Failure to create a dump MUST still produce `crash.json` or `hang.json`, the last successful state, recent events, and the last relevant entry/agent identity.

### PROC-009 — Cancellation and runner failure

Ctrl+C or an explicit cancellation request MUST stop scheduling new work, mark the current attempt `Cancelled`, and execute owned cleanup. If the runner itself terminates before cleanup, the lease, process inventory, runtime lock, and run root MUST allow the next invocation to inspect and recover without assuming the run is dead.

### PROC-010 — Time and observability budgets

Each named machine profile MUST define measured budgets for L1 total time and, as applicable, process start, mission current, snapshot transfer, materialization, `PreBattleHold`, `BattleActive`, controlled completion, natural combat completion, and cleanup. Controlled and natural completion MUST have separate measured budgets. Initial feasibility runs establish baselines; later approval may convert calibrated limits into functional failures.

Automation instrumentation MUST also have a measured overhead budget against an equivalent normal run. Hot-path observation SHOULD use aggregation or sampling. Unbounded per-tick, per-agent, or per-message logging is prohibited even in the test profile.

## 11. Payload recording and fixtures

### PAY-001 — Record existing boundaries

The recorder MUST capture the exact serialized bytes that cross an existing boundary. It MUST NOT translate them into a new authoritative model and then test only that model.

Required payload kinds include:

| Payload kind | Boundary | Expected representation |
|---|---|---|
| `CampaignRoster` | Campaign host to dedicated process | Exact `battle_roster.json` bytes |
| `BattleStartNotification` | Campaign host to campaign client coordination | Exact `BATTLE_START:` message bytes/text |
| `PreMissionTopology` | Dedicated server to multiplayer client | Exact existing topology payload |
| `FullBattleSnapshot` | Dedicated server to multiplayer client | Exact serializer bytes before transport chunking |
| `TransportManifestAndChunks` | Dedicated server to multiplayer client | Optional transport-level capture for chunk/retry defects |
| `BattleResult` | Dedicated server to campaign host | Exact isolated result bytes |

### PAY-002 — Payload metadata

Each captured payload MUST have metadata containing:

- payload kind and boundary;
- source and destination roles;
- encoding and compression;
- serializer name and schema version;
- transport schema when applicable;
- byte length and SHA-256;
- campaign, battle instance, and stage identity;
- source repository revision;
- source module version and hash;
- game/server version identity;
- creation time and capture reason;
- redaction declaration.

### PAY-003 — Immutable fixtures

Accepted fixtures MUST be immutable. Any intentional regeneration MUST create a new fixture version and explain the schema/source/version change. Tests MUST verify all declared hashes before use.

### PAY-004 — Replay truth boundary

A recorded campaign roster MAY drive L1 or L2/L3 replay. It cannot prove that the current campaign capture logic generated the same data. Only L4 may claim live campaign capture and writeback.

### PAY-005 — Schema compatibility

Fixtures MUST include at least one current-schema sample and selected older supported schema samples where the source explicitly supports backward reading. Unsupported schema behavior MUST fail closed with a structured reason.

### PAY-006 — Fixture lifecycle

Each fixture MUST have a stable fixture ID, revision, status (`Current`, `LegacySupported`, `Incompatible`, or `Quarantined`), compatible game/server/module range, generator identity, acceptance revision/date, provenance, and deprecation or quarantine reason when applicable. Regeneration MUST create a changelog entry and MUST NOT overwrite or delete an older fixture still referenced by a supported test.

Large fixture storage MAY use Git LFS or an external artifact store only when integrity and retrieval are deterministic. The repository MUST retain a small reviewable manifest even when payload bytes live elsewhere.

### PAY-007 — Exact versus shareable evidence

Exact payload bytes and sanitized/shareable bundles are distinct artifacts. Sanitization MUST create a new derivative with its own hash and redaction manifest; it MUST NOT be labeled as the exact captured payload. Controlled campaign saves and account/profile data MUST follow the same rule.

Real user campaign saves containing personal data, account identifiers, Steam linkage, credentials, or unrelated playthrough state MUST NOT be committed to the repository. Controlled test saves MUST be purpose-created and sanitized for repository storage or retained in an access-controlled artifact store referenced by an integrity-checked manifest.

### PAY-008 — Oracle provenance

Expected values MUST state how they were obtained. An assertion SHOULD NOT compute its expected value exclusively through the same production method being tested. Stable fixture review, an independent contract calculation, native source evidence, or before/after campaign evidence MUST provide an independent oracle for critical identity, count, and writeback assertions.

## 12. Result isolation and exactly-once evidence

### RES-001 — Explicit result policy

Every runtime run MUST choose one policy:

| Policy | Allowed levels | Result location | Allowed claim | Campaign consumer |
|---|---|---|---|---|
| `Suppress` | L2 | No result file is published | Mission/spawn evidence only | None |
| `Isolated` | L2 diagnostic or L3 | Run root only | No authoritative result claim at L2; required isolated authoritative result at L3 | None |
| `CampaignConsumable` | L4 only | Isolated campaign-host bridge for the correlated run | Authoritative result plus separately proven writeback assertions | Only the campaign host with the same `RunId` and validated nonce |

The default for L2 MUST be `Suppress`.

### RES-002 — Isolation before mission start

All participating roles MUST agree on the isolated result root and policy before the mission opens. Moving or deleting a canonical result after mission end is not an acceptable safety mechanism.

### RES-003 — Distinct assertions

Automation MUST report these assertions separately:

- `ResultPublicationAttempted`;
- `ResultWrittenOnce`;
- `ResultIdentityValidated`;
- `ResultAppliedOnce`;
- `ResultJournaledAfterApplyOnce`;
- `DuplicateResultRejected`.

L2 MUST require that no campaign-consumable result exists. L3 MUST require `ResultPublicationAttempted`, `ResultWrittenOnce`, and `ResultIdentityValidated`. L4 MUST require all applicable result and writeback assertions.

### RES-004 — Early-abort regression

A dedicated regression test MUST create enough runtime entry state for the normal result builder to be capable of output, end the mission before `BattleActive`, and prove that no campaign-consumable result can escape the L2 policy.

### RES-005 — Identity validation

No campaign result may be applied unless campaign ID, battle instance ID, battle stage, and stable result ID match the active run and expected campaign state.

### RES-006 — Durable writeback recovery evidence

L4 evidence MUST distinguish `Received`, `IdentityValidated`, `ApplicationStarted`, `ApplicationCommitted`, `Journaled`, and `Acknowledged`. These are writeback evidence states, not permission for the runner to apply campaign data. The implementation plan MUST explicitly handle interruption between `ApplicationCommitted` and `Journaled` so recovery cannot apply the result twice or silently lose an already committed result.

### RES-007 — Completion mode and claim boundary

Every L3/L4 result record MUST declare one completion mode:

| Completion mode | What it proves | What it MUST NOT claim by itself |
|---|---|---|
| `ControlledLifecycleEnd` | Client readiness, `BattleActive`, controlled-agent ownership, result publication, mission disposal, and cleanup | Natural victory routing, natural combat-end detection, or casualty correctness |
| `NaturalBattleEnd` | The controlled lifecycle plus the applicable natural completion and reconciliation invariants | Globally deterministic AI outcome |

The completion command for `ControlledLifecycleEnd` MUST still enter through an approved authoritative intent and MUST NOT write or force result state directly.

A `NaturalBattleEnd` run MUST NOT issue that completion command after `BattleActive`, force a winner, directly kill/remove agents to manufacture an outcome, write result state, or advance a phase synthetically. If natural completion exceeds its declared deadline, the run is `Timeout`; it MUST NOT fall back to `ControlledLifecycleEnd` and report a pass.

### RES-008 — Mandatory natural-completion coverage

The maintained suite MUST include at least one passing real-client L3 `NaturalBattleEnd` run for every supported adapter in SCN-001–SCN-009. A controlled run remains valid evidence for its declared boundary, but it cannot satisfy this requirement.

Every natural-completion run MUST prove:

1. the scenario reached `BattleActive` through its normal topology, snapshot, materialization, selection, and readiness gates;
2. no automation-requested early-completion intent was accepted after `BattleActive`;
3. the normal authoritative mission path emitted the terminal combat state, outcome, and winner/loser facts applicable to the scenario;
4. initial, surviving, removed, wounded, killed, routed, captured, hero, mount, formation, stage, settlement, wall, gate, and engine facts reconcile wherever the scenario exposes them;
5. the normal result builder published exactly one isolated result with matching campaign, battle, stage, and run identity;
6. mission disposal, role shutdown or post-mission return, exact process cleanup, and run-state cleanup completed without a leaked process, lock, port, command, or bridge record.

Fixtures MAY use small or intentionally asymmetric armies to keep natural completion bounded, but their inputs MUST be fixed before mission start and must still pass through the normal production paths. Assertions MUST follow RT-004: exact identities and accounting remain exact, while AI-driven timing and casualty distribution use justified invariant or bounded assertions.

For a multi-stage scenario, `NaturalBattleEnd` means that every expected stage transition completes through its normal route and that the final scenario result reconciles the full stage sequence. Ending only the first mission or publishing only an intermediate-stage result does not satisfy RES-008.

A controlled L4 run MAY test recovery or failure handling, but an adapter cannot claim full L4 coverage until a same-adapter L4 `NaturalBattleEnd` run proves native completion, result validation, aftermath application, and journal persistence in one correlated run.

## 13. Runtime assertions

### RT-001 — Common L2 assertions

Every supported scenario fixture MUST define stable expectations for:

- scenario kind and subtype;
- scene identity and resolution source;
- mission shell/controllers;
- campaign and battle-stage identity;
- attacker/defender orientation;
- team and formation counts or bounded expectations;
- entry stable IDs and character IDs;
- initial healthy counts;
- equipment-set identity or exact stable equipment assertions;
- mount policy and expected mounted entries;
- hero/captain identity;
- scenario-specific structures such as siege engines or hideout participants;
- zero fatal invariant violations.

### RT-002 — Common L3 assertions

Every supported client lifecycle fixture MUST additionally define:

- required client role-instance count and per-instance identity;
- topology acceptance;
- snapshot transfer and decode completion;
- materialization acknowledgement completion;
- side/entry selection result;
- authoritative controlled-agent identity;
- transition to `BattleActive` through normal readiness;
- expected behavior when a required acknowledgement is absent;
- exactly-one isolated result publication at the selected completion boundary.

Every L3 fixture MUST declare its completion mode. A `NaturalBattleEnd` fixture MUST additionally define:

- the authoritative natural-completion signal and terminal-state source;
- the expected scenario stage sequence;
- proof that no controlled-completion command was accepted after `BattleActive`;
- winner/loser and outcome reconciliation rules;
- applicable entry, casualty, hero, mount, formation, settlement, wall, gate, and engine conservation rules;
- a bounded natural-combat deadline whose expiration produces `Timeout`;
- expected mission-disposal and post-mission role states.

### RT-003 — Common L4 assertions

Every campaign end-to-end fixture MUST additionally define:

- live encounter eligibility and rejection reason behavior;
- capture/source identity;
- campaign map/mission transition behavior;
- scenario adapter selected for aftermath;
- casualties, hero health/capture, settlement, or engine results relevant to that scenario;
- exactly-once application and journal persistence;
- duplicate/stale result rejection.

An L4 fixture that contributes to full adapter coverage MUST use `NaturalBattleEnd` and correlate the native terminal outcome with the result consumed by the campaign. Its before/after oracle MUST cover every scenario-relevant casualty, hero health/capture, settlement, wall, gate, engine, stage, and aftermath fact exposed by that adapter. A controlled L4 fixture MUST be labeled as targeted lifecycle/recovery evidence and cannot be the adapter's only L4 completion evidence.

### RT-004 — Tolerance policy

Exact assertions are REQUIRED for identities, schemas, route selection, counts derived deterministically from the fixture, equipment contracts, phase ordering, and exactly-once behavior.

Invariant or bounded assertions are REQUIRED for timing, AI decisions, combat duration, and casualty distributions not controlled by a source-level seed. Every tolerance MUST include a reason; “flaky” is not a reason.

Examples of bounded facts include calibrated time-to-phase ranges, dynamically regrouped formation counts, and AI-driven casualty distributions. Examples of invariants include non-negative counts, stable identity conservation, no duplicate agent ownership, and result totals that reconcile with initial and surviving/removed entries. A bound MUST name its machine/scenario baseline and review condition.

### RT-005 — Crash-regression dimensions

The regression inventory MUST tag fixtures independently of battle type. Required dimensions include, as applicable:

- crafted hero weapons, crossbows/ammunition stacks, throwing stacks, shields/banners/emblems, mount equipment, rider death, mount death, and invalid/partial equipment;
- wounded main hero, companion substitution, missing multiplayer character, commander fallback, multiple heroes per formation, and one player with multiple formations;
- join before mission, join during scene load, disconnect after selection, reconnect during battle, completion during disconnect, early abort, duplicate result, and consecutive battles;
- 1v1, small, normal, near-agent-limit, reinforcement, mounted, corpse, and agent-budget pressure.

The runner MUST use fixture tags and a reviewable risk-based or pairwise selection policy rather than requiring the full Cartesian product. Any automated change-impact selection is deferred until this inventory is stable.

### RT-006 — Stability and suite-quality evidence

Repeated suites MUST report first-attempt outcome, pass rate, failure reason distribution, median and tail duration, and resource trend. A successful retry MUST NOT erase an earlier failure. Required iteration counts and failure thresholds MUST be calibrated per scenario/profile before they become release gates; until then they are diagnostic targets recorded in the milestone plan.

## 14. Scenario acceptance matrix

Scenario requirements are cumulative by declared evidence level: L3 requires the applicable L2 facts plus its L3 row, and L4 requires the applicable L2/L3 facts plus its L4 row. A scenario is never required to satisfy a higher-level row to complete a lower-level run.

The `L3 natural` rows below are additional L3 acceptance criteria for RES-008, not a new evidence level. A controlled L3 run may pass before the natural fixture exists, but the adapter remains only partially covered. Final maintained coverage for SCN-001–SCN-009 requires both the ordinary L3 row and the corresponding `L3 natural` row. Every scenario report MUST state `Controlled only`, `Natural passing`, or `Natural blocked` rather than collapsing those states into one L3 label.

### SCN-001 — Field battle

| Level | Mandatory acceptance |
|---|---|
| L2 | Scene identity and resolution source, land-adapter identity, party orientation, initial teams/formations/entries, mounted policy, equipment, and captain mapping |
| L3 | Real-client topology/snapshot/readiness, selected and controlled entry, normal transition to `BattleActive`, and one isolated result |
| L3 natural | Native combat-end and winner/loser evidence, entry/casualty/hero/mount reconciliation, natural field mission disposal, and one naturally produced isolated result |
| L4 | Live campaign capture, reconciled casualties/hero outcomes, land-battle aftermath, exactly-once application, and journal evidence |

The maintained fixture set MUST eventually cover both a directly selected field scene and the terrain-resolver fallback. The first vertical slice is one mixed infantry/cavalry fixture with at least one hero or captain; the second scene-resolution branch MUST NOT block that first L3 result.

### SCN-002 — Village battle

| Level | Mandatory acceptance |
|---|---|
| L2 | Exact village scene, village boundary revision/hash, initial orientation/entries/materialization, mounted policy, equipment, and captain mapping |
| L3 | Village topology and materialization acknowledgements, controlled entry, normal `BattleActive`, and one isolated result |
| L3 natural | Natural combat end without a village-boundary stall, winner/loser and entry/casualty/hero/mount reconciliation, mission disposal, and one naturally produced isolated result |
| L4 | Live village capture, relevant casualties/hero outcomes, village aftermath adapter, application, and journal evidence |

Minimum vertical slice: one mixed-party village fixture whose boundary differs measurably from a normal field battle.

### SCN-003 — Siege assault

| Level | Mandatory acceptance |
|---|---|
| L2 | Exact campaign scene, siege deployment shell, orientation, foot-only entry restrictions, initial wall/side deployment facts, exact initial engine identities/health, relevant ladder/topology facts, and initial native materialization |
| L3 | At least one real remote client, deployment/materialization acknowledgements, remote-only occlusion safety, engine topology, normal `BattleActive`, controlled agent, and correct isolated result stage |
| L3 natural | Native siege victory/defeat, stage, wall/gate/engine, entry/casualty/hero, and mission-disposal reconciliation with one naturally produced isolated siege result |
| L4 | Live siege capture, engine/wall/casualty outcomes, siege aftermath, exactly-once application, and journal evidence |

The first L2 vertical slice is one external assault containing engines on both sides. The first L3 slice adds at least one remote client. Siege work follows a passing field L3 and MUST NOT block the first useful field-client milestone.

### SCN-004 — Sally out

| Level | Mandatory acceptance |
|---|---|
| L2 | Supported sally-out route, mission controller set, map/battle spawn behavior, role orientation, initial materialization, and absence of an unintended `SallyOutMissionController` where the current route excludes it |
| L3 | Real-client readiness/control, normal `BattleActive`, and one isolated result with correct stage/orientation |
| L3 natural | Native sally-out completion with reversed orientation preserved through winner/loser, entry/casualty/hero, stage, disposal, and naturally produced result evidence |
| L4 | Live capture and reversed sally-out aftermath with exactly-once application and journal evidence |

Blockade and blockade-sally-out rejection is governed by SCN-010.

### SCN-005 — Siege ambush

| Level | Mandatory acceptance |
|---|---|
| L2 | Player-defender orientation, native sally-out/ambush controller behavior, mount policy, initial siege facts, and native materialization |
| L3 | Topology and acknowledgement gates, controlled entry, normal `BattleActive`, and isolated attacker engine-result/stage evidence |
| L3 natural | Native ambush completion with attacker/defender orientation, stage, engine, entry/casualty/hero/mount, disposal, and naturally produced result reconciliation |
| L4 | Live attacker engine outcomes and exact siege-ambush aftermath with application/journal evidence |

### SCN-006 — Relief battle

| Level | Mandatory acceptance |
|---|---|
| L2 | Field-style scene routing with retained siege settlement context, subtype `Relief`, settlement identity, orientation, and initial materialization |
| L3 | Real-client readiness/control, normal `BattleActive`, and one isolated relief-stage result |
| L3 natural | Native relief completion preserving settlement/subtype identity through winner/loser, entry/casualty/hero, disposal, and naturally produced result reconciliation |
| L4 | Live capture and relief aftermath preserving settlement identity, with application/journal evidence |

### SCN-007 — Lords hall

| Level | Mandatory acceptance |
|---|---|
| L2 | Player-attacker orientation, exact indoor scene/controller, battle stage, ordered participant identity, and initial materialization |
| L3 | Real-client entry/control, normal scenario progression, and one isolated lords-hall-stage result |
| L3 natural | Every expected indoor stage completes normally with ordered participant, winner/loser, casualty/hero, disposal, and final naturally produced result reconciliation |
| L4 | Live capture and lords-hall aftermath with application/journal evidence |

Reordered participants MUST be a contract or precondition mismatch test; it is not a requirement to launch an invalid runtime mission.

### SCN-008 — Day hideout

| Level | Mandatory acceptance |
|---|---|
| L2 | Native day-hideout controller, reflected participant counts, participant identities, and initial native materialization |
| L3 | Real player entry/control, required readiness, active scenario progression, and boss-phase behavior where applicable |
| L3 natural | Native hideout and boss-stage completion where applicable, with participant/casualty/hero, stage, disposal, and naturally produced result reconciliation |
| L4 | Live hideout capture and aftermath with application/journal evidence |

A game-version incompatibility in reflected private members MUST be reported as a specific environment/compatibility failure.

### SCN-009 — Night hideout ambush

| Level | Mandatory acceptance |
|---|---|
| L2 | Night/ambush route, private-field compatibility, ordered participants, initial sentry/stealth facts exposed by the existing contract, and native materialization |
| L3 | Real player entry/control, readiness, active stealth/ambush progression, and one isolated result |
| L3 natural | Native stealth/ambush and boss-stage completion where applicable, with ordered participant/casualty/hero, stage, disposal, and naturally produced result reconciliation |
| L4 | Live night-hideout capture and aftermath with application/journal evidence |

Night hideout MUST have a separate fixture from day hideout.

### SCN-010 — Unsupported scenarios

Blockade, blockade sally out, and any supported-name scenario whose live contract is invalid MUST fail before process launch with `PreconditionsFailed`. No roster start command or result may be published.

## 15. Automated multiplayer client requirements

### CLI-001 — Real engine client

Every automated client role instance counted toward L3 MUST run an actual compatible Bannerlord multiplayer client, load the production module assemblies, and establish the normal network/session path. A mock network client MAY test pure protocol contracts but cannot satisfy L3.

### CLI-002 — Controlled account/session assumptions

The environment doctor MUST report any launcher, authentication, profile, anti-cheat, lobby, or local-connect prerequisite. The runner MUST not claim portability until those prerequisites have been verified on another intended machine profile.

Clean run `m2d-live-r3-01` proved that a running Steam process and a loaded `NetworkMain.GameClient` do not by themselves establish an authenticated lobby session. The exact client reached `LobbyState=Idle`; the stock authentication view displayed `Not Logged In`; and the join request expired without a server-list request. Installed-version ILSpy inspection proved that the stock `MPAuthenticationVM.ExecuteLogin()` delegates to public `LobbyState.TryLogin()`, which checks platform privileges, creates the platform login provider, and calls `LobbyClient.Connect` without accepting a module-owned password.

For the default-off feasibility profile, the client driver MAY invoke that exact public `LobbyState.TryLogin()` path on the main application tick only after all of the following are proven: the active game state is the exact installed `TaleWorlds.MountAndBlade.LobbyState`, its `LobbyClient` is reference-equal to the resolved `NetworkMain.GameClient`, the client state is `Idle`, no login is already active, and no login attempt has been issued for the command. The driver MUST issue at most one login attempt, retain the returned `Task`, and publish distinct waiting, success, fault, cancellation, privilege-denial, and still-idle terminal evidence. It MUST NOT synthesize UI input, accept or persist account credentials, bypass platform privilege checks, or retry authentication blindly. Authentication failure is an environment/session failure unless lower-level evidence establishes a product or automation defect. This gate is shared by every battle type.

On every client terminal outcome, the aggregate runner MUST read and preserve the final run-scoped client status in the feasibility report even when the wait helper reports failure by exception. It MUST also inventory the exact client-PID native engine/error/watchdog log set with the same required/optional policy used for the dedicated role. Evidence capture failure MAY supersede the primary outcome only as an explicit runner/evidence failure; it MUST NOT erase the primary client outcome or its terminal status.

### CLI-003 — No fake readiness

The client driver MAY automate UI, console, or test-only intent entry, but the module must still execute normal topology validation, snapshot decode, materialization, selection, and acknowledgements.

### CLI-004 — First client milestone

The first L3 vertical slice MUST be a field battle and is part of P0 because it is the first milestone that removes the developer's manual server/client/join/readiness loop. It MUST contain separate controlled and natural runs as defined by Milestone 5; the controlled run establishes the fast loop and the natural run establishes the first full-battle proof. The second major L3 family SHOULD be siege assault because it exercises deployment, engine topology, foot-only policy, and the widest current acknowledgement surface.

### CLI-005 — Negative readiness test

At least one test MUST intentionally withhold one required acknowledgement and prove the server does not reach `BattleActive`, then report the exact missing readiness fact rather than a generic timeout.

### CLI-006 — Early client feasibility gate

Before building the full runtime orchestrator, a bounded feasibility investigation MUST determine whether the actual client can be launched, connected locally, associated with the owned server, observed through module readiness, and driven through a supported connect intent without manual UI. It MUST inventory launcher, authentication, anti-cheat, display/session, multi-instance, and crash-reporter constraints.

The feasibility result is design evidence, not L3 gameplay evidence. Temporary investigative commands or scripts MUST NOT become production authority paths, and any code or environment change still requires its own approved plan.

### CLI-007 — Multi-client coverage

The first P0 field L3 requires one client. Before the suite may claim cooperative multi-peer coverage, it MUST add at least these two-client cases using distinct `RoleInstanceId` values:

- same side with different entries;
- different sides where the scenario contract supports opposing player participation;
- simultaneous readiness;
- conflicting selection of one entry;
- one client disconnects while the other remains;
- one client reconnects during active battle;
- one client has acknowledged materialization while the other is still pending.

Server readiness, selection ownership, acknowledgements, controlled agents, disconnect/reconnect state, results, and cleanup evidence MUST be attributable to the exact client role instance. Failure of one client MUST NOT cause the runner to misclassify or terminate the other as unowned.

### CLI-008 — Run-scoped native-lobby join intent

The first local one-client control path MUST be disabled by default and MUST become active only when `COOPSPECTATOR_TEST_AUTOMATION=1`, a valid `RunId`, an absolute run root, and a sufficiently strong run token are supplied together. The run root MUST resolve to `%TEMP%\CoopSpectator\Automation\<RunId>` for this initial profile. Normal production launch without that complete profile MUST remain unchanged.

Before process creation, the launcher MUST verify that Steam is running in the current interactive session, all required client files exist, and the installed client module SHA-256 equals the explicitly requested hash. The launcher MUST create a fresh request bound to the `RunId`, positive sequence, unique command ID, creation/expiry times, run-token hash, and expected module hash; it MUST refuse a reused run containing an existing request or status. The un-hashed run token MUST be passed only through the child-process environment.

The client module MUST recompute its loaded assembly hash and reject mismatched run, token, schema, lifetime, or binary identity before issuing a join. Request expiry bounds whether the native join may start; after the native join task has started, the module MUST NOT report a false cancellation or terminal expiry while TaleWorlds may still complete the non-cancellable operation. The external runner retains bounded timeout and exact-process cleanup authority.

When that external timeout occurs after at least one valid client status was observed, the aggregate report MUST retain the last validated non-terminal status with its original `IsTerminal=false` value. Retaining evidence MUST NOT promote the status to `Connected`, supersede the timeout outcome, or weaken exact run/token validation.

The launcher handoff MUST retain one complete verified client process identity: PID, exact executable path and SHA-256, aggregate-runner parent PID, process start time, original launch-start/launch-observed window, launch-operation ID, role instance, and path-evidence source. The aggregate runner MUST validate and register that identity before any fallible live revalidation so cleanup authority survives a later runner defect. UTC fields read from JSON MUST be normalized without first converting a deserialized `DateTime` to a culture-dependent string; the same contract MUST pass when the JSON provider returns either an ISO string or a `System.DateTime` value.

Server discovery and joining MUST use the normal `NetworkMain.GameClient` lobby path: `GetCustomGameServerList()` followed by `RequestJoinCustomGame(...)`. Selection MUST require exactly one server matching the requested name and port plus any declared game-type or unique-map filters. The automation profile MUST NOT trust or mutate the production persisted local-host marker. It MUST instead validate a run-scoped owned-host record bound to the exact `RunId`, run-token hash, server name, UDP port, process ID, process start time, and executable path; the recorded process identity MUST still be live and the UDP port MUST be active before joining. Multiple exact matches MUST fail rather than select the first result.

The native `Map` value is a scene name such as `mp_tdm_map_001`; it is not `GameServerEntry.UniqueMapId`. A non-empty `UniqueMapId` request filter MAY be supplied only when its value comes from the authoritative native unique-scene identifier for the same registered scene. It MUST NOT be copied, inferred, or synthesized from the `Map` value. When that authoritative value is unavailable, the request MUST omit the optional unique-map filter and continue to require the run-unique server name, exact port, declared game type, singular match, and exact owned-host proof. This rule applies before every battle adapter.

A no-match selection proves only that no returned entry satisfied every active selector. It MUST NOT be documented as proof that the server was absent from the lobby list unless retained evidence distinguishes the list contents or at least the total-entry and name/port-candidate counts. Extending list diagnostics remains subject to the default-off, bounded, state-change-only diagnostic rules.

The authoritative native handoff observation MUST be attached to `GameNetwork.StartMultiplayerOnClient(string,int,int,int)`, after any one-shot loopback rewrite has produced the final address. An optional version-specific lobby reflection patch MUST NOT be the sole source of `NetworkHandoff` evidence and MUST NOT emit a duplicate handoff notification. The controller MAY claim terminal `Connected` only after this handoff was observed and both `GameNetwork.IsClient` and `GameNetwork.IsSessionActive` are true.

Native investigation established that the dedicated server does not expose the requested local UDP endpoint or normal lobby listing before `start_game`. After result publication is already fail-closed as `Suppress` and the dedicated role has reported the expected loaded module hash, the external runner MAY issue only the minimum standard dedicated commands needed to create an isolated vanilla `TeamDeathmatch` bootstrap mission and expose the owned server. `ModuleReady` MUST NOT be treated as native console readiness: before the first command, the runner MUST require a distinct run-scoped acknowledgement tied to an authoritative native lifecycle point where `IGameNetworkHandler.OnHandleConsoleCommand` can receive input. A fixed delay or the existence of the process alone is insufficient.

For the current hash-pinned dedicated version, `InitialListedGameServerState.OnActivate` remains the authoritative native lifecycle point at which console commands become eligible. Its interactive console text is `is ready! You can now enter console commands`. Clean run `m2b2c-client-handoff-live-20260831-01` proved that `DedicatedCustomServer.Starter.exe` can create or rebind a native console while redirected stdout/stderr remain empty and the PID-correlated `rgl_log` omits that `Console.WriteLine` text. The automation MUST therefore observe the lifecycle point through a separate run-scoped acknowledgement bound to the exact `RunId`, token hash, role instance, process identity, and loaded module hash. Redirected standard output/error and PID-correlated native logs MAY be retained as supplementary evidence, but MUST NOT be the sole readiness channel.

Each bootstrap command MUST originate from an allowlisted, bounded, run-scoped command intent created by the external runner after result suppression and loaded-role validation. The dedicated role MUST validate the request identity and publish a strictly atomic acknowledgement for readiness, each applied option, usable-map acceptance, and start-game progression. Current option commands require exact value readback from the authoritative option state or an equivalently strong native acknowledgement. `add_map_to_usable_maps` and `start_game` require authoritative evidence that the usable map was accepted and game startup progressed to the selected scene; an unacknowledged write is insufficient. Redirected standard input MAY be used only after a named live probe proves that the exact starter version consumes it; it MUST NOT be assumed from synthetic child-process tests. The runner MUST establish accepted `start_game` evidence before beginning the UDP-visibility deadline. If readiness or command acceptance cannot be observed, the run MUST fail with an exact readiness/acceptance reason rather than a generic port timeout. Exact PID-correlated `rgl_log` and `rgl_log_errors` files MUST be retained below the run root even when TaleWorlds ignores the requested log-output directory. A matching `watchdog_log` MUST be copied and validated when the exact process profile produces it; otherwise the inventory MUST retain an explicit `NotProduced` record and MUST NOT supersede the primary runtime outcome. An existing but stale or identity-incompatible optional log remains an internal evidence failure. This bootstrap MUST use no campaign fixture, campaign save, cooperative battle snapshot, battle-phase advancement, or campaign result consumer, and MUST NOT be classified as L2 or L3 battle evidence.

A server password MUST NOT be accepted as a command-line argument or persisted in the request, launch artifact, status, or logs. When required, it MAY be inherited through a protected child-process environment value; artifacts MAY record only whether a password was supplied.

The module MUST write run-scoped status atomically and only on state or failure-detail changes. At minimum, it MUST distinguish module readiness, lobby wait, server-list request, server wait, join request, join acceptance, network handoff, connection, explicit pre-join cancellation, and failure. The launcher and client module MUST NOT issue `start_game`, fabricate readiness, or use UI automation. Only the external runner may perform the preceding minimum native bootstrap, under the stated result-isolation and ownership gates. Source and contract-test completion of this requirement does not promote the Milestone 1 connection/control capability rows until a named runtime rerun proves the exact client/server correlation.

## 16. Campaign end-to-end requirements

### CAM-001 — Controlled source state

Each L4 scenario MUST use a documented test save or deterministic setup procedure that reaches a real supported campaign encounter. Save identity and hash MUST be recorded when redistribution/licensing permits.

### CAM-002 — Native capture

The campaign host MUST invoke the normal `BattleDetector` eligibility and adapter capture path. Directly constructing the final snapshot in the runner does not satisfy L4.

### CAM-003 — Same-run continuity

Campaign host, dedicated server, every participating client role instance, roster, battle result, and journal evidence MUST share the same correlated `RunId`, validated per-run nonce, campaign ID, battle instance ID, and stage.

### CAM-004 — Writeback safety

The runner MUST retain before/after evidence for affected campaign facts and prove the result was applied once. Replaying the same result MUST not apply it again.

### CAM-005 — Recovery behavior

An interrupted run MUST leave enough journal/lease/result evidence to distinguish “not applied,” “applied but final runner acknowledgement lost,” and “rejected.” The runner MUST not retry writeback blindly.

### CAM-006 — Exactly-once capability gate

Before Milestone 9 may claim exactly-once campaign writeback, implementation and runtime evidence MUST prove at least one of these capabilities:

1. the applied result ID and relevant aftermath state persist in the same durable campaign-save boundary;
2. campaign application is independently idempotent by stable result ID and can be replayed safely; or
3. a durable recovery record unambiguously distinguishes every interruption point without inferring application from process exit alone.

If none can be proven, exactly-once is `Not Verifiable` and Milestone 9 MUST NOT be reported complete. A journal written after an independently committed campaign mutation is not sufficient proof by itself.

## 17. Contract-test aggregation and change impact

### TST-001 — Preserve the existing projects

The aggregate runner MUST discover every project listed by a reviewed contract-test manifest or equivalent canonical inventory and preserve each project's exit code and output. The source baseline for this revision contains 17 standalone `.NET 8` console contract-test projects; that number is informative, not normative.

### TST-002 — Stable aggregate report

The aggregate report MUST contain project name, command, start/end UTC, duration, exit code, outcome, and log paths. Failure of one test SHOULD NOT hide the outcomes of tests that can still run safely.

### TST-003 — Deferred change-impact map

After stable fixture tags and runtime suites exist, the repository MUST contain a reviewable mapping from source path patterns to affected contract projects and runtime scenarios. Broad shared paths such as snapshot models, network bridge, mission behaviors, result contracts, and scenario routing MUST select all relevant tests. This automation is not a P0 prerequisite.

### TST-004 — Safe default

The default fast command MAY select an impacted subset when a Git diff is available, but completion and pre-commit verification MUST offer an explicit full-contract option.

### TST-005 — Initial human-review routing

Until TST-003 is implemented, each approved change plan MUST select tests using at least this review guide:

| Change surface | Minimum evidence to consider |
|---|---|
| Pure contract, DTO, serializer, or validator | Relevant L1 contracts; compatibility fixtures when schemas change |
| Project/build/deployment target | L0 plus compile-only proof; staging proof before runtime claims |
| Phase or readiness gate | Relevant L1, L2, L3, and a missing-readiness negative test |
| Spawn, equipment, entry, hero, mount, or formation | Relevant L1 and L2; controlled L3 when peer/control state is involved; affected natural L3 when combat accounting or lifetime is affected |
| Snapshot/chunk/reconnect transport | Relevant L1 and L3, including failure/retry contracts |
| Combat AI, agent death/removal, victory routing, mission completion, or casualty accounting | Relevant L1 plus every affected `NaturalBattleEnd` L3 scenario |
| Result builder/publication | Relevant L1, L2 early-abort isolation, controlled L3 result, and affected natural L3 result reconciliation |
| Campaign result application/journal | Relevant L1 and L4 exactly-once/recovery evidence; affected natural L4 when combat outcome or aftermath mapping changes |
| Scenario detector/adapter | Relevant L1 and the applicable L2, controlled L3, natural L3, and claimed L4 rows for that scenario |
| Cleanup, process, lease, or file bridge | Relevant L1 plus two-run sequential evidence at the lowest affected runtime level |
| Broad shared infrastructure | Every affected scenario/role identified by explicit adjacent-scenario review |

### TST-006 — Natural-completion suite selection

The command surface MUST provide:

- a fast controlled lifecycle command that does not claim natural completion;
- an explicit command for one selected adapter's `NaturalBattleEnd` run;
- an explicit full-natural-suite command covering every maintained SCN-001–SCN-009 natural fixture;
- a report field that distinguishes a selected subset from the full natural suite.

Until TST-003 is implemented, the approved change plan MUST select natural scenarios manually using TST-005. Changes to combat AI, agent lifetime/death/removal, victory routing, mission completion, result building, casualty reconciliation, scenario-stage progression, or campaign aftermath MUST include every affected maintained natural scenario. A documentation-only, build-only, pure serialization, or UI-only change MAY omit natural runtime tests only when the completion report records why those production paths cannot be affected.

The full natural suite is the authority for repository-wide natural battle coverage. A passing impacted subset is useful change evidence but MUST NOT be reported as a full-natural-suite pass.

## 18. Artifacts and reporting

### ART-001 — Minimum retained artifacts

Every run MUST retain:

- final manifest;
- per-role-instance status snapshots and ordered event journal;
- commands and their acknowledgements;
- exact input payloads and metadata/hashes;
- stdout, stderr, and relevant module/game logs;
- process and port inventory;
- module/game binary identities;
- assertion report with evidence links;
- declared completion mode and, for `NaturalBattleEnd`, terminal-state source, winner/loser, reconciliation, and timeout evidence;
- cleanup report;
- result and writeback evidence allowed by the selected level;
- crash or hang dump/metadata when available;
- resource/time samples required by L5 or the active performance investigation;
- first-attempt outcome and any explicitly requested retry attempts;
- a redacted reproduction descriptor for non-pass outcomes.

### ART-002 — Assertion record

Each assertion MUST include:

- stable assertion ID;
- required evidence level;
- expected fact;
- observed fact;
- source `RoleType`/`RoleInstanceId` and authoritative source;
- first/last relevant event sequence;
- outcome;
- artifact links.

### ART-003 — Redaction

Launcher credentials, dedicated-server authentication tokens, account secrets, and equivalent sensitive values MUST NOT be stored in the run root, fixture metadata, process arguments, logs, process inventories, or reproduction commands. They SHOULD be supplied through an environment-specific secure source; only a non-reversible fingerprint MAY be recorded. Account identifiers not required for diagnosis, unrelated user paths, and other personal data MUST not be written to shareable artifacts. The manifest stores only the per-run nonce fingerprint.

### ART-004 — Retention

Passing runs MAY use bounded retention. Failing, crashing, and timeout runs SHOULD be retained until explicitly cleaned. Each machine profile MUST eventually define calibrated limits by age, count, and total bytes after real artifact-size measurement; before calibration, cleanup remains explicit. Cleanup tooling MUST target exact run roots, show what will be removed, and refuse cleanup when free-space pressure cannot be resolved without touching unowned data.

## 19. Runner command surface

The implementation SHOULD expose one PowerShell entry point, recommended as:

```text
scripts/Invoke-CoopTest.ps1
```

Required logical commands:

| Command | Required result |
|---|---|
| `Doctor` | L0 report only |
| `Contracts` | L1 selected or full aggregate report |
| `CompileOnly` | L1 client/dedicated compile with no external deployment |
| `Feasibility` | bounded server/client/staging capability investigation; never an L2/L3 pass |
| `Record` | Typed payload archive with provenance |
| `DedicatedSpawnSmoke` | L2 run |
| `ClientLifecycle` | L3 controlled run by default, or an explicitly selected completion mode; never merges controlled and natural claims |
| `NaturalBattle` | One selected adapter's L3 `NaturalBattleEnd` run |
| `NaturalSuite` | Full maintained SCN-001–SCN-009 `NaturalBattleEnd` suite with per-scenario outcomes |
| `CampaignE2E` | L4 run with an explicit controlled or natural completion mode |
| `Sequential` | repeated L2, L3, or L4 runs |
| `Soak` | bounded repeated run with per-iteration evidence |
| `Inspect` | read-only rendering of an existing run |
| `Cleanup` | exact owned-run cleanup with safe preview/confirmation policy |
| `Recover` | inspect an abandoned lease, validate ownership, and perform explicitly selected recovery |

Parameter names and syntax may be refined during the implementation plan, but separate evidence levels and safety semantics are mandatory.

## 20. Implementation milestones

### Milestone 0 — Documentation baseline

Deliverables:

- this specification;
- the companion source audit;
- knowledge-base navigation links.

Exit criteria:

- documents are internally linked;
- requirements distinguish source, build, contract, runtime, and campaign evidence;
- no implementation is implied by the documentation status.

### Milestone 1 — Source-drift and runtime-feasibility gates

Priority: **P0**

Deliverables:

- re-check this specification and the companion audit against the current source revision;
- verify the current project targets, bridge paths, result-on-mission-end hazard, readiness gates, process launch chain, and installed binary identities;
- determine whether the dedicated server can be launched/stopped by an exactly owned process path;
- determine whether the real multiplayer client can be launched, associated with the owned server, and driven through a stable local-connect intent;
- inventory launcher, authentication, anti-cheat, user/display session, crash reporter, modal window, multi-instance, and port constraints;
- determine which BLD-005 staging mode is feasible for client and dedicated roles;
- record a named initial machine/version profile and measured startup observations.

The feasibility report MUST contain this capability matrix:

| Capability | Allowed status | Minimum evidence |
|---|---|---|
| Dedicated executable starts and is exactly owned | `Confirmed` / `Blocked` | Process and ownership artifact |
| Dedicated module loads | `Confirmed` / `Blocked` | Module event and loaded identity state |
| Client starts and is exactly owned | `Confirmed` / `Blocked` | Process and ownership artifact |
| Client module becomes ready | `Confirmed` / `Blocked` | Module event and loaded identity state |
| Client connects to the owned server | `Confirmed` / `Blocked` | Network/module correlation event |
| Approved control intent reaches the client/module path | `Confirmed` / `Blocked` | Command and acknowledgement evidence |
| At least one safe staging mode works | `Confirmed` / `Blocked` | Before/staged/loaded hash chain |
| Exact cleanup and lock release work | `Confirmed` / `Blocked` | Cleanup and final process/lock report |

Exit criteria:

- every source-specific assumption is `Confirmed`, `Changed`, or `Unknown` with evidence;
- server and client launch/connect feasibility has a reproducible result and explicit blockers;
- no feasibility action is reported as L2 or L3 evidence;
- no battle mission is opened until result isolation and exact cleanup are already safe for that investigation;
- if safe staging, client connection, or exact ownership is infeasible, the plan is revised before building the full orchestrator.

No essential capability row may remain `Unknown` when Milestone 1 closes. A `Blocked` row is a valid feasibility finding, but it blocks Milestone 2B and all L2–L5 work until a revised approved plan resolves or explicitly changes the target capability.

### Milestone 2A — Non-runtime foundation

Priority: **P0**

Implementation status: **Complete at L0/L1 on 2026-08-31.** See [BATTLE_TEST_AUTOMATION_M2A_IMPLEMENTATION.md](BATTLE_TEST_AUTOMATION_M2A_IMPLEMENTATION.md). The recorded `EnvironmentBlocked` doctor outcome preserves the unresolved runtime version/hash blockers and does not block the completed non-runtime compile/contracts evidence.

Deliverables:

- compile-only property in both project files;
- aggregate runner for the full current contract-test inventory;
- minimal environment doctor and named machine/version profile;
- isolated run root, manifest, role-instance model, nonce, lease/heartbeat, atomic command/status/event contracts, and stable exit codes;
- protocol compatibility and full RUN-010 file-protocol fault-injection contracts;
- minimum assertion/artifact/report model;
- no runtime process launch.

Exit criteria:

- compile-only verification changes no installed module file;
- every contract project in the reviewed inventory can run through one command;
- concurrent publication, malformed/partial content, duplicate/reordered/stale/cross-run commands, interrupted acknowledgement, repeated reads, temporary locking, simulated write failure, and incompatible protocol are covered by passing contracts;
- production behavior is unchanged with automation disabled;
- no Bannerlord battle pass is claimed by this milestone.

### Milestone 2B — Runtime safety foundation

Priority: **P0**

Implementation status: **The connection-feasibility slice is live-complete. Milestone 2B.3A implements protocol-1.1 role liveness/progress, canonical shared runtime locks, real console and explicit cancellation contracts, RecoveryV2, and exact owned failure-evidence correlation. Focused contracts pass in Windows PowerShell 5.1 and PowerShell 7.6.4; final dirty-source aggregate run `m2b3a-final-c-01` passed 22/22 with verified runner-lock release, and compile-only run `m2b3a-final-b-01` built both modules without changing installed inventories or launching a product process. Milestone 2B remains in progress until this safety surface is committed, clean-published, staged, and confirmed by a new live `Feasibility` run. Crash/hang fault artifacts remain source/synthetic evidence. No campaign, cooperative mission, battle, L2, or L3 evidence is claimed.** See [BATTLE_TEST_AUTOMATION_M2B3_SAFETY_CLOSURE.md](BATTLE_TEST_AUTOMATION_M2B3_SAFETY_CLOSURE.md).

Deliverables:

- exact process/port ownership, runtime locks, and role-instance process inventory;
- one verified runtime staging mode with end-to-end binary hash chain;
- loaded-role state plus separate native console-readiness observation before native mission bootstrap;
- a minimum runner-owned vanilla `TeamDeathmatch` bootstrap, used only because the native dedicated server does not bind/list the local UDP server beforehand;
- per-command acceptance/readback, singular runner-result, and required-versus-optional PID-correlated native-log evidence;
- cancellation, exact cleanup, abandoned-run inspection, and `Recover` flow;
- `Suppress` result policy and early-abort safety contracts;
- structured fatal-failure event and minimum crash/hang evidence.

Exit criteria:

- every essential Milestone 1 capability is `Confirmed`;
- staging and role-reported `ConfirmedLoadedHash` identities match the intended build;
- the minimum native bootstrap and normal-lobby connection are correlated to the exact run-scoped server process and client role without a campaign fixture or battle-evidence claim;
- cancellation and simulated runner failure preserve exact recovery evidence;
- cleanup stops only owned processes and releases only verified owned locks;
- `Recover` passes its read-only, revalidation, preview, action, and reporting contract tests;
- result isolation is configured before any later mission-open action;
- production behavior is unchanged with automation disabled;
- no Bannerlord battle pass is claimed by this milestone.

### Milestone 3 — Exact field fixture

Priority: **P0**

Deliverables:

- opt-in recording at only the existing boundaries needed by the first field slice;
- one current-schema mixed infantry/cavalry field fixture with a hero or captain;
- exact raw payload bytes, metadata, hashes, oracle provenance, compatibility status, and a sanitized derivative when sharing is needed;
- corruption and schema-mismatch contract tests;
- redacted one-command reproduction descriptor.

Exit criteria:

- replay input bytes hash-match the recorded bytes;
- every captured payload identifies boundary, serializer, schema, source version, and binary identity;
- expected critical values have an independent oracle;
- production serialization output is unchanged when recording is off;
- no parallel authoritative scenario model exists.

### Milestone 4 — Field dedicated spawn smoke

Priority: **P0**

Deliverables:

- state-based dedicated launch and mission-open orchestration for the field fixture;
- authoritative observer projection through `PreBattleHold`;
- field L2 identity, scene, team, formation, entry, equipment, mount, hero/captain, and materialization assertions;
- exact owned-process cleanup;
- isolated early-abort result regression;
- two sequential L2 attempts.

Exit criteria:

- the field slice satisfies common L2 requirements and only the SCN-001 L2 row;
- no campaign-consumable result exists before, during, or after abort;
- the second attempt cannot consume stale files, phases, commands, or result guards from the first;
- HTTP acceptance and elapsed delay are not pass evidence;
- cleanup leaves no owned process or runtime lock;
- artifacts identify the exact loaded binaries and last authoritative state.

### Milestone 5 — First useful field client lifecycle

Priority: **P0**

Deliverables:

- real client launch/connect driver;
- topology, snapshot, materialization, selection, control, and readiness evidence;
- field L3 vertical slice using the Milestone 3 fixture;
- missing-acknowledgement negative test;
- `ControlledLifecycleEnd` plus one `Isolated` result;
- a separate small, bounded field `NaturalBattleEnd` run using fixed pre-mission inputs and the same production client/server paths;
- natural terminal-state, winner/loser, entry/casualty/hero/mount, result, and mission-disposal evidence;
- mission-disposal/behavior-removal and client/server cleanup evidence;
- a second field lifecycle attempt proving stale-state reset;
- crash-reporter, fatal-event, no-heartbeat, and no-progress handling;
- separate measured controlled-loop and natural-loop time budgets and reproduction commands.

Exit criteria:

- the server reaches `BattleActive` only through normal peer gates;
- controlled-agent identity is proven;
- the controlled run produces and validates one isolated authoritative result;
- the controlled manifest and report label completion as `ControlledLifecycleEnd` and make no natural combat-end or casualty-correctness claim;
- the separate natural run issues no accepted early-completion intent after `BattleActive`, observes the native terminal outcome, reconciles the field entry/casualty/hero/mount facts, and produces one naturally derived isolated result;
- the natural manifest and report label completion as `NaturalBattleEnd` and retain its terminal-state source and timeout evidence;
- no campaign application is claimed;
- withheld readiness prevents `BattleActive` with the exact missing fact;
- client and server cleanup is exact for both completion modes and repeatable across a second attempt;
- a failure preserves first-attempt outcome, last progress, logs, payloads, state journal, and crash/hang metadata;
- separate one-command controlled and natural runs remove the manual server/client/connect/select/start/finish loop for this field fixture.

### Milestone 6 — Crash-regression inventory

Priority: **P1**

Deliverables:

- fixture tags for the RT-005 equipment, hero, lifecycle, and scale dimensions relevant to known project failures;
- current highest-risk field regression fixtures, beginning with crafted/equipment, mount/rider, hero/commander fallback, reconnect, early-abort, and consecutive-battle cases;
- natural-completion tags for regressions that can affect combat accounting, agent lifetime, victory routing, result construction, or mission disposal;
- the CLI-007 two-client field cases after the one-client P0 lifecycle is stable;
- reviewable risk-based or pairwise selection;
- known-native-issue annotations without pass conversion;
- first-attempt stability and duration reporting.

Exit criteria:

- each promoted regression has an immutable fixture, independent oracle, stable assertion/failure ID, and exact reproduction descriptor;
- selected regressions pass at their declared evidence level;
- every regression tagged as natural-completion-sensitive selects the applicable maintained `NaturalBattleEnd` scenario and cannot be closed by a controlled-only pass;
- retries cannot hide the first outcome;
- every multi-client selection, acknowledgement, controlled-agent, disconnect/reconnect, result, and cleanup fact is attributed to the exact `RoleInstanceId`;
- the inventory does not require the full Cartesian product.

### Milestone 7 — Siege assault L2 and L3

Priority: **P1**

Deliverables:

- exact external siege fixture with engines on both sides;
- SCN-003 L2 scene/deployment/engine/materialization coverage;
- real remote-client SCN-003 L3 deployment/readiness/occlusion/control coverage;
- correct isolated siege result-stage evidence;
- a separate bounded siege `NaturalBattleEnd` run with native victory/defeat, wall/gate/engine, entry/casualty/hero, result, and disposal reconciliation;
- siege-specific crash regressions and a second sequential lifecycle.

Exit criteria:

- common and SCN-003 assertions pass only at their applicable declared levels;
- siege cannot reach `BattleActive` without its required acknowledgements;
- field L3 remains passing or its affected shared-path regressions are explicitly reported;
- engine and deployment identities reconcile across server/client evidence;
- the natural siege run issues no accepted early-completion intent after `BattleActive` and satisfies the SCN-003 `L3 natural` row;
- no stale siege, ladder, chunk, reconnect, or result state survives the second attempt.

### Milestone 8 — Remaining supported adapters

Priority: **P1/P2**

Deliverables:

- village, sally out, siege ambush, relief, lords hall, day hideout, and night hideout fixtures;
- applicable SCN-002 and SCN-004–SCN-009 L2 and L3 rows;
- one maintained bounded `NaturalBattleEnd` run for each of those supported adapters, including complete expected stage progression for multi-stage scenarios;
- explicit blockade and invalid-live-contract rejection tests;
- version-sensitive hideout compatibility diagnostics;
- adjacent-scenario regression evidence for shared changes.

Exit criteria:

- every currently supported adapter has a passing L2 fixture;
- every adapter intended for automated playable coverage has an applicable passing L3 slice;
- every supported adapter in this milestone satisfies its `L3 natural` row or is reported `Natural blocked`, in which case full adapter coverage and the milestone remain incomplete;
- unsupported cases fail before process launch and publish no roster/start/result;
- each report states which L4 aftermath facts remain unverified.

### Milestone 9 — Campaign end to end

Priority: **P2**

Deliverables:

- controlled campaign saves/setup procedures and privacy/integrity policy;
- campaign host driver using native detector/adapter capture;
- same-run server/client integration;
- a field L4 `NaturalBattleEnd` vertical slice first and a siege-assault L4 `NaturalBattleEnd` slice second;
- durable exactly-once result application and journal/recovery assertions;
- CAM-006 capability-gate evidence;
- controlled L4 recovery/interruption cases where useful, clearly separated from natural-completion coverage;
- other adapter L4 slices according to risk, with at least one natural L4 run before any adapter is called fully covered at L4.

Exit criteria:

- live capture, natural battle completion, result validation, aftermath, and journal evidence form one correlated field run and one correlated siege-assault run;
- the field slice satisfies SCN-001 L4 before siege or other adapters can block its completion;
- the siege slice satisfies SCN-003 L4 and its natural-completion assertions;
- duplicate result application is rejected;
- interruption around application/journaling has an unambiguous recovery outcome;
- at least one CAM-006 capability is proven; otherwise the milestone remains `Not Verifiable`;
- before/after campaign facts match scenario expectations;
- the report clearly separates unautomated visual/UI checks.

### Milestone 10 — Sequential, soak, and randomized invariants

Priority: **P3 / deferred from L0–L4 delivery**

Deliverables:

- bounded iteration runner;
- reset/stale-state assertions;
- selected natural-completion sequential and soak profiles after their lower-level natural scenarios are stable;
- calibrated time/resource baselines and diagnostic thresholds;
- calibrated instrumentation-overhead and retention limits;
- reproducible randomized input/ordering seed;
- failure minimization or exact failing-seed retention;
- suite stability metrics.

Exit criteria:

- every iteration has independent artifacts and a parent summary;
- natural profiles retain completion mode, terminal-state source, outcome, reconciliation, and timeout evidence for every iteration;
- no owned process, lock, or bridge state leaks between iterations;
- first-attempt failures remain visible;
- resource/time trends are evaluated against an approved profile baseline;
- nondeterministic AI outcomes are evaluated by documented invariants.

Campaign-map automation is outside this specification. A future `CAMPAIGN_MAP_AUTOMATION_SPEC.md` MAY reuse the run, process, fixture, protocol, and artifact contracts only after the battle foundation is stable; it MUST extend the existing `CoopCampaignMapPrototype` rather than create parallel authority.

## 21. Source anchors at the audited baseline

This table is an advisory navigation aid for the source baseline named at the top of the document, not a permanent file-layout contract. Each milestone still requires a source re-check and an approved plan.

| Area | Existing files expected to be reviewed or changed |
|---|---|
| Compile-only build | `CoopSpectator.csproj`, `DedicatedServer/CoopSpectatorDedicated.csproj` |
| Run-root/atomic protocol | `Infrastructure/AtomicBridgeFileIO.cs`; `Infrastructure/Automation/CoopAutomationJoinContract.cs`; `Infrastructure/Automation/CoopAutomationJoinBridge.cs` |
| Existing bridge isolation | `Campaign/BattleRosterFile.cs`, `Infrastructure/CoopBattlePhaseBridgeFile.cs`, `Infrastructure/CoopBattleEntryStatusBridgeFile.cs`, `Infrastructure/CoopBattleResultBridgeFile.cs` |
| Runtime observation | `Infrastructure/CoopBattlePhaseRuntimeState.cs`, `Mission/CoopMissionBehaviors.cs`, `Mission/CoopMissionNetworkBridge.cs` |
| Campaign capture/writeback | `Campaign/BattleDetector.cs`, scenario adapters, `BattleResultWritebackJournalBehavior` |
| Client lobby control | `Multiplayer/Automation/CoopLobbyAutomationDriver.cs`; `Multiplayer/Automation/CoopLobbyAutomationController.cs`; `Commands/CoopAutomationConsoleCommands.cs`; existing local-host lobby patches |
| Process orchestration | `run_battle_test_client.bat`; `scripts/Start-CoopBattleTestClient.ps1`; dedicated helper and command classes; `scripts/CoopDevLoop.ps1` is reference only, not the runner base |
| Fast tests | existing `Tests/*.ContractTests` projects plus `Tests/CoopAutomationJoin.ContractTests` and a future aggregate runner |
| Documentation | `docs/ai/BUILD_TEST_DEBUG.md`, `CODE_MAP.md`, `RUNTIME_FLOWS.md`, `INVARIANTS_AND_RISKS.md`, and this specification as behavior is implemented |

New file and type names MUST be finalized in each milestone plan after checking compilation boundaries between the client and dedicated projects. Shared contracts must not accidentally pull client-only campaign/UI references into the dedicated build.

## 22. Requirement verification matrix

For the active implementation milestone, the completion report MUST contain one row per mandatory requirement changed or claimed. Requirements outside the approved milestone do not require placeholder rows.

| Field | Required content |
|---|---|
| Requirement ID | Stable ID from this specification or a newly approved extension |
| Status | `Satisfied`, `Partially Satisfied`, `Not Satisfied`, or `Not Verifiable` |
| Implementation evidence | Files, types, and relevant methods |
| Validation evidence | Exact command/run ID and result |
| Evidence class | Source, build, contract test, L2, L3, L4, or regression |
| Affected scenarios/roles | Explicit list |
| Documentation impact | Updated documents or reason unchanged |
| Residual risk | Remaining unverified or manual surface |

When one requirement ID contains multiple independent mandatory bullets or fields, its matrix row MUST include an itemized sub-checklist (for example `RUN-003.a`, `RUN-003.b`) so partial compliance cannot be hidden behind one status.

No milestone may be reported as complete while one of its mandatory exit criteria is partial, unsatisfied, or not verifiable at its required evidence level.

### 22.1. Requirement applicability by milestone

Safety prohibitions apply whenever their subject exists. This table defines when positive capabilities and their verification first become mandatory; it prevents future requirements from expanding an earlier active milestone.

| Requirement group | First mandatory milestone | Scope at that milestone |
|---|---|---|
| Source-drift checks, ENV-001/002, CLI-006, BLD-005 feasibility, PROC-001/002 discovery | Milestone 1 | Capability decision and evidence only; no L2/L3 claim |
| SAF-001–SAF-005, L0/L1 contracts, OUT-001–OUT-004, RUN-001–RUN-007, RUN-009/010, BLD-001–BLD-004, TST-001/002/004/005, base ART schema | Milestone 2A | Non-runtime contracts and compile-only proof |
| CLI-008, RUN-008, BLD-005/006 runtime proof, PROC-001–PROC-005, PROC-007–PROC-009, RES-001/002/004 contracts | Milestone 2B | Run-scoped normal-lobby control, minimum vanilla server bootstrap, process, staging, lock, cleanup, recovery, and result isolation without a campaign fixture or L2/L3 battle pass |
| PAY-001–PAY-004, PAY-007/008 | Milestone 3 | Current field fixture boundaries only |
| L2, PROC-006, RT-001, RES-001/002/004 runtime proof, SCN-001 L2 | Milestone 4 | One-client-free field dedicated smoke |
| L3, RT-002, RES-003/005/007/008, CLI-001–CLI-005, TST-006 initial commands, SCN-001 L3 and L3 natural, PROC-010 developer-loop baseline | Milestone 5 | One real client, separate `ControlledLifecycleEnd` and field `NaturalBattleEnd` runs |
| PAY-005/006 compatibility lifecycle, RT-005/006, OUT-004 quarantine use, CLI-007 | Milestone 6 or first earlier compatibility claim | High-risk regressions and multi-client field coverage |
| SCN-003 L2/L3/L3 natural and RES-008 siege coverage | Milestone 7 | Siege assault controlled and natural completion |
| SCN-002 and SCN-004–SCN-010 applicable L2/L3 rows, SCN-002 and SCN-004–SCN-009 L3 natural rows, remaining RES-008 coverage | Milestone 8 | Remaining adapters, their natural completion, and rejection cases |
| L4, RT-003, RES-006/008 L4 boundary, CAM-001–CAM-006, applicable scenario L4 rows | Milestone 9 | Live campaign capture, natural field and siege completion, aftermath, and capability-gated writeback |
| L5, full PROC-010 overhead limits, calibrated ART-004 retention, resource/stability gates, selected natural sequential/soak profiles | Milestone 10 | Sequential, soak, randomized, and full-battle stability evidence |
| TST-003 automated change-impact mapping | After Milestone 6 inventory is stable | Optimization only; never replaces explicit adjacent-scenario review |

If a milestone intentionally implements a later requirement early, that requirement becomes part of the active approved scope and must appear in the completion matrix. It MUST NOT pull unrelated requirements from the same future milestone into scope automatically.

## 23. Definition of done for one automated scenario

A scenario is complete at a declared level only when:

1. the immutable fixture or controlled campaign setup is identified;
2. environment and binary identities are recorded and match expectations;
3. every common assertion for the level passes;
4. every scenario-specific assertion in section 14 applicable to the declared evidence level passes;
5. negative/rejection behavior relevant to that scenario passes;
6. result policy is enforced before mission start;
7. exact owned-process and run-state cleanup succeeds;
8. artifacts are complete and internally correlated;
9. at least one repeated run proves stale state is not reused when required by the milestone;
10. living documentation reflects implemented behavior and remaining limitations;
11. manual-only validation is listed explicitly rather than silently omitted.
12. its completion report declares `Controlled only`, `Natural passing`, or `Natural blocked`; full L3 scenario coverage requires a passing `L3 natural` row, and full L4 scenario coverage requires at least one correlated L4 `NaturalBattleEnd` run.

A scenario MAY be complete for an explicitly declared lower boundary while natural coverage is still pending. It MUST then be described as, for example, “L2 passing” or “controlled L3 passing,” not “fully automated” or “fully covered.” A scenario marked `Natural blocked` remains incomplete for full scenario coverage and MUST retain the exact blocker and required follow-up milestone.

## 24. Final target

When all milestones are complete, the normal development loop should become:

```text
source change
  -> impacted fast contracts
  -> safe compile-only check
  -> affected dedicated smoke scenarios
  -> affected controlled client lifecycle scenarios
  -> affected natural-completion scenarios when TST-005/006 selects them
  -> campaign end-to-end only when campaign capture/writeback is in scope
  -> manual UI/camera/visual check only when relevant
```

The command surface also retains an explicit full-natural-suite run across every maintained SCN-001–SCN-009 fixture. This does not eliminate every real Bannerlord run. It makes each remaining run intentional, evidence-backed, and proportional to the risk of the change while preventing fast controlled completion from being mistaken for full battle coverage.
