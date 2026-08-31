# AI Knowledge Base

Last source verification: **2026-08-31**
Last bounded runtime-feasibility verification: **2026-08-31** (`m2b2-live-feasibility-rerun-20260831-01`, clean revision `70a40db`; exact dedicated loaded hash, six separate commands, bounded discovery, and exact cleanup confirmed; native console readiness, UDP visibility, and client launch were not reached)
Last automation-control source/contract verification: **2026-08-31** (no game process launched)
Last automation-foundation verification: **2026-08-31** (`m2a-contracts-20260831-07` and `m2a-compile-only-20260831-03`, no product process launched)
Last controlled on-disk staging verification: **2026-08-31** (`m2b2-stage-20260831-02` and `m2b2-poststage-doctor-20260831-01`, clean revision `12abf36`, no product process launched)
Last Milestone 2B.1 source/contract/compile verification: **2026-08-31** (`m2b1-final-contracts-20260831-03`, 21/21, and `m2b1-final-compile-only-20260831-02`; no product process launched; installed modules unchanged)
Last live-runner correction verification: **2026-08-31** (`m2b2-fix-final-contracts-20260831-01`, 22/22, and `m2b2-fix-compile-only-20260831-01`; no product process launched; installed modules unchanged)
Repository: `BannerlordCoopSpectator3`
Verification scope: static review of source, project files, module descriptors, tests, scripts, existing documentation, selected local server assemblies, and ILSpy output, plus the separately documented Milestone 1 installed-runtime probe, post-M1 client-control validation, Milestone 2A contracts/compile-only proof, controlled `0.3.2` staging, Milestone 2B.1 runtime-safety source/contracts, and two bounded live feasibility attempts. The latest clean rerun confirms the dedicated loaded hash under `ResultPolicy=Suppress`, six discrete command writes, bounded descendant discovery, and exact cleanup. It also proves that module readiness precedes native console readiness on this machine; no UDP listener or client appeared, so client identity, connection, and campaign/battle behavior remain unverified.

## Purpose

This directory is the canonical living documentation for future Codex work. It is intentionally organized by engineering question rather than development date. Dated files in the parent `docs/` directory remain valuable evidence, but many describe intermediate states, experiments, or planned changes.

The project converts a single-player Bannerlord campaign encounter into a synchronized multiplayer mission, runs the authoritative battle on a dedicated server, and writes the result back into the host campaign. The difficult part is preserving exact campaign identity, equipment, armies, scene topology, deployment, casualties, and aftermath while using Bannerlord's multiplayer networking and mission runtime safely.

## Document map

| Document | Use it for |
|---|---|
| [ARCHITECTURE.md](ARCHITECTURE.md) | System boundaries, authority, data ownership, major components, and active feature flags |
| [CODE_MAP.md](CODE_MAP.md) | Finding the correct file, class, subsystem, adapter, patch, UI, bridge, or test |
| [RUNTIME_FLOWS.md](RUNTIME_FLOWS.md) | Campaign-to-mission startup, topology handshake, snapshot transfer, spawn/materialization, phases, reconnect, completion, and scenario routing |
| [BUILD_TEST_DEBUG.md](BUILD_TEST_DEBUG.md) | Reference profiles, side-effecting builds, contract tests, scripts, logs, diagnostics, and safe validation |
| [RELEASE_PACKAGING.md](RELEASE_PACKAGING.md) | Canonical GitHub/Nexus archive layouts, packaging commands, exclusions, validation, and publication boundaries |
| [BATTLE_TEST_AUTOMATION_AUDIT.md](BATTLE_TEST_AUTOMATION_AUDIT.md) | Source-verified feasibility findings, unsafe assumptions in the supplied proposal, and the required evidence ladder |
| [BATTLE_TEST_AUTOMATION_SPEC.md](BATTLE_TEST_AUTOMATION_SPEC.md) | Canonical staged requirements and acceptance criteria for battle-test automation |
| [BATTLE_TEST_AUTOMATION_M1_FEASIBILITY.md](BATTLE_TEST_AUTOMATION_M1_FEASIBILITY.md) | Named-machine runtime-feasibility evidence, capability decisions, blockers, and the revised pre-runtime gate plan |
| [BATTLE_TEST_AUTOMATION_CLIENT_JOIN_IMPLEMENTATION.md](BATTLE_TEST_AUTOMATION_CLIENT_JOIN_IMPLEMENTATION.md) | Post-M1 run-scoped launcher/lobby-control implementation, validation evidence, requirement audit, and remaining runtime gates |
| [BATTLE_TEST_AUTOMATION_M2A_IMPLEMENTATION.md](BATTLE_TEST_AUTOMATION_M2A_IMPLEMENTATION.md) | Completed non-runtime runner/protocol/compile-only implementation, authoritative L0/L1 evidence, requirement audit, and Milestone 2B boundary |
| [BATTLE_TEST_AUTOMATION_M2B_STAGING.md](BATTLE_TEST_AUTOMATION_M2B_STAGING.md) | Controlled `0.3.2` on-disk installation identity, retained `0.3.1` pre-images, post-install doctor evidence, and the remaining loaded-hash gate |
| [BATTLE_TEST_AUTOMATION_M2B1_RUNTIME_FOUNDATION.md](BATTLE_TEST_AUTOMATION_M2B1_RUNTIME_FOUNDATION.md) | Fail-closed runtime identity/result-isolation source, exact owned-host/cleanup/recovery control, native bootstrap correction, contract evidence, and live-run boundary |
| [BATTLE_TEST_AUTOMATION_M2B2_STAGING.md](BATTLE_TEST_AUTOMATION_M2B2_STAGING.md) | Current committed-source staging identity, retained prior `0.3.2` trees, clean-build hash chain, post-stage doctor evidence, and the connection-only live gate |
| [BATTLE_TEST_AUTOMATION_M2B2_FEASIBILITY.md](BATTLE_TEST_AUTOMATION_M2B2_FEASIBILITY.md) | First live feasibility evidence, exact dedicated loaded identity, bounded recovery, runner root causes, correction verification, and clean-rerun boundary |
| [INVARIANTS_AND_RISKS.md](INVARIANTS_AND_RISKS.md) | Protected contracts, known limitations, high-risk files, regression matrix, and change checklists |

The repository-root [AGENTS.md](../../AGENTS.md) is the concise instruction and routing entry point.

## Sixty-second mental model

```text
Campaign host
  detects a native campaign encounter
  builds BattleStartMessage + full BattleSnapshotMessage
  writes battle_roster.json and starts/notifies the dedicated mission
            |
            v
Dedicated server
  opens the exact campaign scene through a multiplayer mission shell
  sends a compact pre-mission topology contract before client scene load
  owns native physical agent creation and authoritative mission state
            |
            v
Remote client
  refuses unsafe scene load until the topology contract matches
  opens the mission, receives the chunked full snapshot, and acknowledges it
  paces initial native CreateAgent replay for supported large-battle scenarios
  selects a side/entry, controls an authoritative agent, and renders UI
            |
            v
Battle end
  dedicated writes battle_result.json with authoritative entry outcomes
  campaign host validates campaign/result identity, applies aftermath once,
  journals the writeback, and exits/continues the native campaign flow
```

There are two distinct communication layers:

1. `NetworkManager` and its TCP line protocol handle the older campaign-host coordination path, including host state and `BATTLE_START` notifications.
2. Bannerlord mission networking handles the pre-mission topology contract and all in-mission authoritative state. `CoopMissionNetworkBridge` is the central in-mission transport.

Local bridge files are a third mechanism, but they coordinate processes, commands, and tooling on a machine. They are not a replacement for authoritative mission networking.

## Current verified runtime snapshot

### Builds and module identity

- Client project: `CoopSpectator.csproj`, `net472`, `x64`, assembly version `0.3.2`.
- Dedicated project: `DedicatedServer/CoopSpectatorDedicated.csproj`, `net472`, `x64`, constant `COOPSPECTATOR_DEDICATED`.
- Both outputs use assembly name `CoopSpectator`, but compile different `SubModule` implementations and different source/reference profiles.
- Client module ID: `CoopSpectator`.
- Dedicated module ID: `CoopSpectatorDedicated`.
- The client build conditionally compiles `GameMode/` when a compatible `TaleWorlds.MountAndBlade.Multiplayer.dll` is available.
- The normal client build triggers client deployment and, by default, a dedicated build/deployment. See [BUILD_TEST_DEBUG.md](BUILD_TEST_DEBUG.md) before building.

### Registered game modes

- `CoopBattle`: primary campaign battle mode; also replaces the official `Battle` registration.
- `CoopTdm`: cooperative TDM-derived mode.
- `CoopHeroCreator`: multiplayer hero-creation mission.
- `CoopHideoutDay` and `CoopHideoutNight`: isolated hideout paths.
- `CoopCampaignMapPrototype`: disabled unless `COOPSPECTATOR_CAMPAIGN_MAP_PROTOTYPE=1`.
- `TdmClone`: compiled support exists but its experiment flag is disabled.
- Official `TeamDeathmatch` remains the stable listed baseline; passive diagnostics injection is enabled.

### Scenario support

Supported when their scenario contracts validate:

- ordinary field battle;
- village battle;
- siege assault with deployment;
- sally out;
- siege ambush;
- relief battle;
- lords hall stage;
- day hideout assault;
- night hideout ambush.

Explicitly rejected:

- blockade;
- blockade sally out;
- any supported scenario whose active mission does not satisfy its required contract.

Do not interpret the presence of an adapter as unconditional support. `BattleDetector.TryGetUnsupportedCoopMissionReason` validates live mission facts and fails closed for invalid contracts.

### Active materialization strategy

- The native server mission stack is the only physical spawner.
- The full campaign snapshot provides exact logical identity and loadout data.
- Multiplayer-safe surrogate characters remain the network identity mechanism.
- `ExactCampaignPreSpawnLoadoutPatch` injects snapshot body/equipment before native spawn.
- Client initial `CreateAgent` replay is paced for field, village, siege assault, and siege ambush paths.
- Native reinforcements remain active; custom staged siege reinforcement materialization is disabled.
- The runtime object registry experiment is disabled.
- Full snapshot transport V2 is active with schema version 1, manifests, chunk windows, range acknowledgements, retries, completion acknowledgement, and abort handling.

### Battle-test client control

- A default-off, run-scoped multiplayer-client launcher and normal-lobby join controller now exist in source.
- The request is bound to a `RunId`, token hash, expected loaded client hash, exact server name/port, and a run-scoped owned-host proof tied to a live PID/path/start time and active UDP port.
- The path passed isolated contract/source compilation and non-launching environment validation on 2026-08-31.
- The exact `0.3.2` client and dedicated binaries compiled from clean pushed revision `12abf36` are installed and hash-confirmed on disk. Both live attempts confirmed the exact dedicated loaded hash with `ResultPolicy=Suppress`; the clean `70a40db` rerun additionally confirmed six separate command writes and bounded cleanup. Server discovery, client loaded-role acknowledgement, join handoff, and connection were not reached.
- The launcher and client module do not issue `start_game` or automate battle phases/results. The Milestone 2B `Feasibility` runner may issue only the minimum vanilla `TeamDeathmatch` bootstrap after loaded-hash, suppression, and the new native console-readiness gate. The clean rerun proves that `ModuleReady` alone is too early; another runner correction is required before a new live attempt. This bootstrap is not campaign or L2/L3 battle evidence.

### Battle-test non-runtime foundation

- `scripts/Invoke-CoopTest.ps1` owns fresh run roots and exposes non-runtime `Doctor`, `Contracts`, and `CompileOnly`, plus bounded `Feasibility`, read-only `Inspect`, and opt-in exact `Recover` control. `scripts/CoopAutomationRunner.Core.ps1` supplies deterministic dedicated bootstrap commands and pure in-memory descendant discovery from one bounded process snapshot.
- `Tests/contract-tests.manifest.json` is the canonical 22-project inventory; historical Milestone 2A run `m2a-contracts-20260831-07` passed its then-current 20, clean-revision run `m2b2-contracts-20260831-01` passed the then-current 21, and correction run `m2b2-fix-final-contracts-20260831-01` passed all current 22.
- `CoopCompileOnly=true` redirects all build/package state below the run root and disables installation deployment; `m2b2-prestage-compile-20260831-01` compiled both version `0.3.2` assemblies from clean revision `12abf36` and proved installed module inventories unchanged before controlled staging.
- Protocol 1.0 covers run/nonce/role identity, leases, ordered events, stable outcomes/reasons, known-issue annotations, atomic/append file behavior, recovery classification, and fault injection.
- The post-install doctor no longer reports an installed/repository hash mismatch. It remains `EnvironmentBlocked` only for `RuntimeVersionCombinationNotYetVerified`; no L2–L5 runtime claim exists.

### Runtime evidence retained from dated reports

The most relevant recorded manual evidence is summarized in [CURRENT_EXACT_BATTLE_MATERIALIZATION_RUNTIME_2026-07-27.md](../CURRENT_EXACT_BATTLE_MATERIALIZATION_RUNTIME_2026-07-27.md):

- field battle materialization and deployment boundaries were validated on one and two machines;
- village materialization and boundaries were validated on one and two machines;
- external siege initial materialization, native reinforcements, sequential battles, final completion, and result writeback were exercised successfully;
- active-battle external-siege reconnect was still unverified in that report;
- exact final ownership of every captured lord was not separately verified;
- the custom staged siege reinforcement path remained frozen.

Treat those results as evidence for the recorded revision, not as an automatic guarantee after later code changes.

## Evidence taxonomy

Use these labels when maintaining this knowledge base:

- **Source-verified**: directly confirmed in current source/configuration.
- **Build-verified**: the named project/configuration compiled successfully; this does not prove runtime behavior.
- **Test-verified**: the named automated or contract tests passed for the recorded revision.
- **Runtime-verified**: backed by a named log, dump, or recorded manual run for a known revision.
- **Regression-verified**: the stated affected and adjacent scenarios/roles were rerun successfully.
- **Planned**: described in a technical specification but not confirmed in current source.
- **Historical**: useful explanation of an earlier architecture, failure, or experiment.
- **Unknown**: requires a fresh source or runtime investigation.

Important current example: [EXACT_SIEGE_UNUSED_MACHINE_FINALIZATION_FIX_TZ_2026-08-21_V3.md](../EXACT_SIEGE_UNUSED_MACHINE_FINALIZATION_FIX_TZ_2026-08-21_V3.md) is a detailed **planned** design. During the 2026-08-28 source review, the named two-phase unused-machine finalization operation was not found in `CoopSiegeMachineDeploymentController` or `CoopMissionNetworkBridge`. Do not cite that specification as implemented behavior without re-checking the source.

## Requirement and completion audit

The mandatory completion protocol is defined in [AGENTS.md](../../AGENTS.md). Before a task governed by a technical specification or approved plan is called complete:

1. Convert every mandatory requirement and acceptance criterion into an auditable checklist item.
2. Link each item to implementation evidence, validation evidence, affected scenarios/roles, and documentation impact.
3. Mark each item `Satisfied`, `Partially Satisfied`, `Not Satisfied`, or `Not Verifiable`.
4. Distinguish source inspection, implementation, build, automated tests, Bannerlord runtime verification, and regression verification.
5. Do not report 100% completion while a mandatory item is partial, unsatisfied, or lacks verification required by its acceptance criteria.

If runtime validation was not performed, use precise language such as “implemented and source-verified, but not runtime-verified in Bannerlord.”

## How to start a task

| Task | Read first | Then inspect |
|---|---|---|
| Campaign encounter detection or aftermath | `RUNTIME_FLOWS.md` | `Campaign/BattleDetector.cs`, `Campaign/*Adapter.cs`, result contracts |
| Mission startup or scene mismatch | `ARCHITECTURE.md`, `RUNTIME_FLOWS.md` | topology component/state, mission mode, open patches |
| Spawn, equipment, mount, hero, or visual parity | `INVARIANTS_AND_RISKS.md` | snapshot model, exact transfer contracts, pre-spawn patch, spawn handoff patch |
| Battle phase, readiness, selection, respawn | `RUNTIME_FLOWS.md` | mission behaviors, network bridge, authority/session/runtime state classes |
| Siege deployment, machines, ladders, or AI | all three core maps | siege mode, siege infrastructure, network bridge, siege patches, latest focused reports |
| UI or player commands | `CODE_MAP.md` | `UI/`, `Commands/`, matching GUI prefab |
| Build/runtime mismatch | `BUILD_TEST_DEBUG.md` | both project files, module descriptors, actual DLL stamps and logs |
| Release packaging or archive layout | `RELEASE_PACKAGING.md`, `BUILD_TEST_DEBUG.md` | `scripts/CreateReleasePackage.ps1`, release documents, module descriptors, generated archives |
| Battle-test automation | `BATTLE_TEST_AUTOMATION_AUDIT.md`, `BATTLE_TEST_AUTOMATION_SPEC.md`, `BATTLE_TEST_AUTOMATION_M1_FEASIBILITY.md`, `BATTLE_TEST_AUTOMATION_CLIENT_JOIN_IMPLEMENTATION.md`, `BATTLE_TEST_AUTOMATION_M2A_IMPLEMENTATION.md`, `BATTLE_TEST_AUTOMATION_M2B_STAGING.md`, `BATTLE_TEST_AUTOMATION_M2B1_RUNTIME_FOUNDATION.md`, `BATTLE_TEST_AUTOMATION_M2B2_STAGING.md`, `BATTLE_TEST_AUTOMATION_M2B2_FEASIBILITY.md` | compile-only targets, aggregate runner/protocol, historical and current-source controlled staging evidence, live feasibility evidence, runtime identity/result isolation, run-scoped launcher/control files, bridge files, mission readiness, result publication/writeback, scenario adapters, installed-runtime capability evidence |
| New diagnostics | `INVARIANTS_AND_RISKS.md`, `BUILD_TEST_DEBUG.md` | `ExperimentalFeatures.cs`, `CoopDebugConfig.cs`, target hot path |

## Maintenance workflow

Every implementation plan must include a `Documentation Impact` section. When an approved change alters behavior:

1. List the living documents read, documents to update, documents to create, and relevant documents intentionally unchanged with a reason.
2. Re-read the affected implementation and adjacent scenario adapters.
3. Update the narrowest relevant living document in the same approved change.
4. Keep each technical fact in one canonical home; link or summarize from other documents instead of duplicating the full statement.
5. Update cross-document links or tables only where the relationship changed.
6. Record a new verification date only for sections whose affected source facts were rechecked.
7. Add runtime evidence only after a real run; distinguish it from build or source verification.
8. Preserve dated reports. If a report is obsolete, label it historical from the living index rather than rewriting it.
9. If the change creates a new lasting subsystem, add it to `CODE_MAP.md`; if it changes authority or ownership, also update `ARCHITECTURE.md` and `INVARIANTS_AND_RISKS.md`.
10. Include required documentation updates in the final requirement-compliance audit.

### Change-to-document matrix

| Change type | Required living documentation |
|---|---|
| New directory, subsystem, major type, command, or test | `CODE_MAP.md` |
| Authority, ownership, dependency, module, or feature default | `ARCHITECTURE.md` |
| Startup sequence, messages, state transition, readiness, reconnect, result flow | `RUNTIME_FLOWS.md` |
| Build target, deployment behavior, script, environment variable, log path, test command | `BUILD_TEST_DEBUG.md` |
| Guardrail, unsafe pattern, known limitation, regression surface | `INVARIANTS_AND_RISKS.md` |
| Any of the above | this index if navigation or the current snapshot changes |

## Known documentation hazards

- Root and dated documents span many revisions from March through August 2026. Their dates matter.
- `PROJECT_CONTEXT.md` and `BUILD_RUNBOOK.md` are useful but predate several exact-scene and materialization changes.
- Some dated files are implementation plans, not completion reports.
- The client project comment recommends Bannerlord `1.3.14`, while dedicated staging explicitly refers to required Bannerlord `1.4.8` SandBox runtime files. Treat the supported game version as unresolved until installation metadata, assembly versions, and a runtime matrix are checked together.
- Generated packages and copied DLLs can outlive the source that produced them. Never infer source state from `dist/`, module `bin/`, root DLL copies, or ZIP files alone.
