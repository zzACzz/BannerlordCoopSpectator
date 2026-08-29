# BannerlordCoopSpectator3 Agent Guide

This file is the project-local entry point for AI-assisted engineering. The canonical living knowledge base is `docs/ai/`.

## Mandatory working protocol

1. Perform read-only investigation first.
2. Before changing code, files, configuration, build outputs, external runtime state, or Git state, present a separate implementation plan and wait for the user's explicit approval.
3. Implement only the latest approved plan and only within its explicit scope.
4. Before fixing behavior for one battle type, inspect whether the same failure can occur in every other relevant scenario listed in `docs/ai/RUNTIME_FLOWS.md`.
5. Treat every build, test, restore, generator, packaging script, and development helper as potentially writable until its exact effects are known.
6. Preserve unrelated local changes and external state.
7. Complete a requirement-by-requirement compliance audit and all required documentation updates before declaring the task complete.

## Approval scope

Approval applies only to the latest presented plan and only to the following items explicitly listed in that plan:

- files, types, and methods to change;
- commands and tools to run;
- affected scenarios and runtime roles;
- validation steps and expected write surfaces;
- documentation to create or update;
- Git operations, if any.

If new evidence changes the root cause, solution, affected files or methods, scenario impact, validation approach, risks, external write surfaces, documentation impact, or Git operations, stop and present a revised plan. Previous approval does not cover the revised plan.

Approval must never be inferred from silence, a previous task, an earlier approval, or general agreement with the direction.

## Pre-approval investigation

Before approval, the agent may perform read-only investigation:

- read and search source, project files, configuration, and documentation;
- inspect logs, exceptions, stack traces, existing dumps, and existing generated artifacts;
- run read-only Git commands such as `git status`, `git diff`, `git log`, `git show`, and `git ls-files`;
- inspect existing binaries with tools that do not write to the repository, installed game, dedicated server, or environment.

Before approval, the agent must not:

- create, edit, move, rename, or delete files;
- run builds, tests, package restore, deployment, packaging, code generation, or formatting;
- install or update tools, packages, workloads, or dependencies;
- change IDE, project, process, game, server, network, or environment configuration;
- run a binary-inspection workflow that emits new files or modifies caches in the project/environment;
- change Git state.

If it is unclear whether a command writes state, treat it as writable and include it in the plan.

## Required implementation plan

Every implementation plan must state:

1. Objective and current evidence.
2. Proposed solution and why it is appropriate.
3. Exact files, types, and methods to change.
4. Scenario and runtime-role impact, including why any listed scenario is not applicable.
5. Exact commands to run and every known repository, client, dedicated-server, or external destination they may modify.
6. Validation levels to perform: source inspection, contract tests, build, runtime verification, and regression verification.
7. Risks, failure behavior, and rollback proposal.
8. Git operations requested, if any.
9. A `Documentation Impact` section listing:
   - living documents read;
   - living documents to update;
   - new documents to create;
   - relevant documents intentionally not changed and why.

## Build, test, and deployment authorization

- Do not run a build merely to inspect the project. Both main project files contain deployment targets that can write into installed Bannerlord client and dedicated-server directories.
- Treat a build as a potentially deploying operation until `docs/ai/BUILD_TEST_DEBUG.md` documents and verifies otherwise.
- A plan containing a build must name the exact command, project, configuration, expected repository outputs, and every external client/server destination it can modify.
- Tests are also writable operations because they can restore packages, compile projects, create `bin/` and `obj/`, write temporary files, or exercise scripts.
- Approval for source edits does not authorize an unlisted build, test, restore, deployment, package, launcher, or process-control command.

## Git discipline

- Implementation approval does not authorize staging, commit, branch creation, checkout, switch, merge, rebase, push, or any other Git state change unless that exact operation was separately included and approved.
- By default, staging and commit require separate explicit approval after the final changed-file list, diff review, validation results, and repository status have been shown.
- Record the read-only baseline with `git status --short` before implementation when Git is available. Treat every pre-existing change as unrelated and protected.
- Do not use `git add .` or `git add -A`. Stage only explicitly approved paths.
- Before requesting commit approval, show `git status --short`, inspect the final diff, run `git diff --check`, and report unrelated/generated files separately.
- Warn before commit if LF/CRLF conversion, whole-file rewriting, formatting churn, generated output, or another noisy diff is present.
- A checkpoint commit must be explicitly identified as a checkpoint, not presented as a clean production fix.
- Do not run destructive or history-rewriting operations such as `reset --hard`, `clean`, `restore`, `stash`, `rebase`, `commit --amend`, force push, or branch deletion without separate explicit approval for the exact command and targets.
- The repository canonical line ending is LF for text files, except `.bat` and `.cmd`, which use CRLF. Do not override the policy in `.gitattributes`, `.editorconfig`, or repository-local Git configuration without an approved plan.
- Before a commit, run `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Test-RepositoryHygiene.ps1 -AllowDirty`; after the commit, run it again without `-AllowDirty`.

## Diagnostics discipline

- Keep new hot-path diagnostics disabled by default and gated by an explicit verbose setting plus a focused diagnostic switch where appropriate.
- Keep string construction, collection scans, per-agent work, and native/network state inspection inside the gate.
- Treat diagnostics touching native objects, agents, synchronized mission objects, network messages, or runtime-sensitive state as potentially dangerous production code.
- Temporary diagnostic code must be removed before task completion unless the approved plan explicitly justifies retaining it.
- Retaining diagnostics in production requires explicit approval and documentation of default state, activation mechanism, performance cost, output volume, and runtime risk.

## Verification and completion

Compilation success is not runtime verification. A task must not be described as fully fixed or 100% complete solely because the source was edited or a build succeeded.

Before completion, derive the requirement baseline from:

1. the user's latest explicit request;
2. the active technical specification, if supplied;
3. the latest approved plan and acceptance criteria;
4. canonical invariants, contracts, and accepted architecture decisions.

Perform a requirement-by-requirement compliance audit. For every requirement, report:

- implementation location or other evidence;
- validation performed;
- affected scenario and runtime role coverage;
- one status: `Satisfied`, `Partially Satisfied`, `Not Satisfied`, or `Not Verifiable`.

The final report must distinguish:

- source inspection completed;
- implementation completed;
- build verified;
- automated/contract tests verified;
- runtime verified in Bannerlord;
- regression verified.

For each required scenario and role, report `Passed`, `Failed`, `Not Run`, or `Not Applicable`. If runtime validation was not performed, state explicitly that the implementation is not runtime verified.

Do not declare the task complete while any mandatory requirement is `Partially Satisfied` or `Not Satisfied`. A mandatory `Not Verifiable` item must be disclosed as a remaining verification gap and handled according to the user's acceptance criteria.

Documentation updates required by the approved change are part of completion, not optional follow-up work.

## Failure and rollback behavior

- If validation fails but the root cause, scope, files, and risk model remain unchanged, the agent may correct its own in-scope implementation and revalidate.
- If failure reveals a different root cause, new files/methods, a broader scenario impact, a new external write, or a different risk, stop and present a revised plan.
- Do not automatically discard changes or use destructive Git commands after a failed implementation.
- Report the exact failed state, preserve unrelated work, and propose a targeted rollback or repair.
- A rollback that changes files or Git state requires explicit approval unless the latest approved plan explicitly included that exact rollback action.

## User communication

- User-facing plans, explanations, status updates, and final reports must be in Ukrainian unless the user explicitly requests another language.
- Repository documentation, technical specifications, investigation reports, and bug reports must be written in English.
- In Ukrainian responses, explain each important non-obvious English technical term in Ukrainian at its first use.
- Add `Навчальний блок` only to the final response after a completed task or completed work stage. Do not add it to plans, approval requests, status updates, or short technical clarifications.

## Read order for a new task

1. `docs/ai/README.md`
2. The task-specific living document:
   - architecture or ownership: `docs/ai/ARCHITECTURE.md`
   - file or symbol location: `docs/ai/CODE_MAP.md`
   - startup, battle, reconnect, or writeback behavior: `docs/ai/RUNTIME_FLOWS.md`
   - build, tests, logs, or debugging: `docs/ai/BUILD_TEST_DEBUG.md`
   - unsafe changes and protected contracts: `docs/ai/INVARIANTS_AND_RISKS.md`
3. Current source and project files referenced by that document.
4. Only then consult dated reports under `docs/` for historical evidence or a focused investigation.

## Truth model

Use different evidence orders for different questions.

### Current implementation truth

Use this order to determine what the current revision actually implements:

1. Current source, project files, module descriptors, and feature flags.
2. Reproducible runtime evidence from the same source revision and exact client/dedicated game builds.
3. Current automated tests and explicitly recorded manual validation for that revision.
4. `docs/ai/` living documentation.
5. Dated reports, specifications, archives, and investigative artifacts.

### Intended behavior truth

Use this order to determine what the system is supposed to do:

1. The user's latest explicit instruction and latest approved plan.
2. Canonical invariants, contracts, and accepted architecture decisions.
3. Approved active technical specifications and acceptance criteria.
4. Tests that explicitly encode intended behavior.
5. Current source.
6. Historical reports and superseded specifications.

If current source contradicts an approved requirement, invariant, or contract, do not silently treat the source as the new requirement. Report the discrepancy and request approval for the intended resolution.

If source inspection and runtime evidence disagree, do not choose one silently. Record the discrepancy and investigate it.

A dated technical specification is not proof that its design was implemented. For example, the 2026-08-21 V3 unused-siege-machine finalization document describes a planned change; the reviewed source does not yet expose the specified finalization operation.

## Architectural guardrails

- The campaign host owns campaign truth and builds the full battle snapshot.
- The dedicated server owns authoritative mission simulation and physical agent creation.
- Clients consume the pre-mission topology contract before opening campaign-derived scenes, then receive the full battle snapshot inside the mission.
- Native server spawn logic remains the single physical agent spawner. Client-side materialization paces or replays native network creation; it must not create a second physical agent.
- Exact campaign identity is keyed by stable entry identity and generation-aware mappings, never by an agent index alone.
- Initial materialization and later native reinforcements are separate corridors. Do not re-route active-battle reinforcements into an initial replay queue.
- File bridges coordinate local processes and tools; Bannerlord `GameNetwork` messages are the authoritative mission client/server transport. Do not merge those responsibilities accidentally.
- Client and dedicated builds use different reference profiles and different startup classes. Never solve a reference problem by mixing arbitrary client and dedicated DLLs.
- Per-mission static state must be reset on mission start, mission end, abort, reconnect replacement, and sequential-battle transitions as applicable.
- Runtime feature truth lives in `Infrastructure/ExperimentalFeatures.cs`; comments and dated reports may lag behind it.

## Scenario impact checklist

For changes to snapshot capture, mission opening, spawn, deployment, control, completion, or writeback, explicitly evaluate:

- ordinary field battle;
- village battle;
- siege assault with deployment;
- sally out;
- siege ambush;
- relief battle;
- lords hall stage;
- day hideout assault;
- night hideout ambush;
- sequential battles and reconnect paths;
- unsupported blockade and blockade-sally-out guards.

Do not generalize a scenario-specific runtime without its own contract and test stage.

## Repository boundaries

Treat these as implementation inputs:

- `Campaign/`, `GameMode/`, `Infrastructure/`, `Mission/`, `MissionModels/`, `Network/`, `Patches/`, `UI/`, `Commands/`, `DedicatedServer/`, and `DedicatedHelper/`;
- `SubModule.cs`, `CoopRuntime.cs`, both project files, and both module descriptors;
- `Module/CoopSpectator/ModuleData/` and `Module/CoopSpectator/GUI/`;
- `Tests/` and `scripts/`.

Treat `bin/`, `obj/`, `.buildcheck/`, `.codex_tmp*/`, `dist/`, `work/`, ZIP packages, local DLL copies, and temporary decompilation output as generated, packaged, or investigative artifacts unless a task explicitly targets them.

## Documentation maintenance contract

Update the relevant `docs/ai/` file in the same approved change whenever any of these change:

- component ownership or mission behavior composition;
- battle snapshot schema, network message flow, or file bridge format;
- scenario support, scenario routing, readiness gates, or phase transitions;
- feature-flag defaults or diagnostic environment variables;
- build/deploy behavior, reference profile, test projects, or tooling;
- a protected invariant, known risk, or runtime validation result.

Each technical fact must have one canonical home. Other documents should link to that location or summarize only the context they uniquely own rather than repeat the full statement.

Before creating a persistent document, search for an existing canonical document that already owns the topic. Link every new persistent document from `docs/README.md`, `docs/ai/README.md`, or the nearest relevant index.

Use repository-relative paths in repository documentation. Do not store user-specific worktree paths, secrets, tokens, or machine-specific configuration unless a document explicitly labels the value as a local example that is necessary for its purpose.

Keep living documents concise, source-oriented, optimized for Codex retrieval, and reviewable by a human. Stable headings, exact identifiers, explicit status, canonical ownership, and source links are preferred over duplicating source code or documenting every class and method.

Record a new verification date only for sections and source facts that were actually re-read. Do not imply that unrelated sections or Bannerlord runtime behavior were revalidated.

Preserve dated reports as evidence; do not silently rewrite history. Mark plans, implemented changes, runtime-verified results, superseded specifications, and archived investigations distinctly.
