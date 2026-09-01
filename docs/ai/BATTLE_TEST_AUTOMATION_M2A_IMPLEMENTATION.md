# Battle Test Automation Milestone 2A Implementation Report

Status: **Complete — non-runtime foundation only**

Implementation and verification date: **2026-08-31**

Specification: [BATTLE_TEST_AUTOMATION_SPEC.md](BATTLE_TEST_AUTOMATION_SPEC.md), Revision 6 working tree

Named machine profile: **`LOCAL-LAPTOP-4IUGGR23-UNVERIFIED`**

## 1. Outcome

Milestone 2A is implemented and verified at L0/L1 without launching Bannerlord, the dedicated server, a multiplayer client, a campaign, or a battle mission.

The repository now has:

- an explicit `CoopCompileOnly=true` project mode that redirects output, intermediate files, project extensions, and restored packages below a caller-owned run root while disabling all installation deployment targets;
- one PowerShell runner with `Doctor`, `Contracts`, and `CompileOnly` commands;
- a canonical manifest covering all 20 current contract-test projects;
- run-scoped manifest, role, nonce-fingerprint, lease, heartbeat, status, event, outcome, assertion, artifact, and reproduction contracts;
- strict atomic JSON and append-safe JSONL helpers plus RUN-010 fault-injection coverage;
- a named environment report that records binary versions/hashes, selected TaleWorlds dependencies, Git/EOL hygiene, writable roots, Steam state, and TCP/UDP ownership for ports 7210 and 7777.

This milestone does not remove the Milestone 1 runtime blockers. It proves that the current working source compiles safely and that the non-runtime contracts are executable. It does not prove that those binaries can be staged and loaded by Bannerlord, that a client can join the owned server, or that any battle phase or result path works automatically.

## 2. Authoritative verification runs

### 2.1 Environment doctor

- Run ID: `m2a-doctor-20260831-06`
- Root: `C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2a-doctor-20260831-06`
- Outcome: `EnvironmentBlocked`
- Exit code: `10`
- Expected blockers:
  - `InstalledClientHashDiffersFromRepositoryOutput`;
  - `InstalledDedicatedHashDiffersFromRepositoryOutput`;
  - `RuntimeVersionCombinationNotYetVerified`.
- Git/EOL policy: passed with dirty-state reporting separated from policy validation.
- Required ports: inspection available; no TCP/UDP owner on 7210 or 7777.
- Product process launch: none.
- Reproduction descriptor: emitted without plaintext nonce or credentials and with a distinct retry `RunId`.

The doctor observed installed client/dedicated modules at version `0.3.1`. The repository module outputs available before the compile-only run were version `0.3.2` with different hashes. The unverified machine-profile name and `EnvironmentBlocked` outcome are deliberate; no supported runtime version matrix is being inferred.

### 2.2 Full contract inventory

- Run ID: `m2a-contracts-20260831-07`
- Root: `C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2a-contracts-20260831-07`
- Outcome: `Pass`
- Exit code: `0`
- Canonical projects: `20`
- Passed: `20`
- Failed: `0`
- Formal assertions: `20`, all `Pass`
- Per-project output: combined log plus separate stdout and stderr files.
- Product process launch: none.

The runner validates that `Tests/contract-tests.manifest.json` exactly equals the discovered `.csproj` inventory before scheduling. It preserves every project result and continues after a project failure when continued execution is safe.

### 2.3 Client and dedicated compile-only proof

- Run ID: `m2a-compile-only-20260831-03`
- Root: `C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2a-compile-only-20260831-03`
- Outcome: `Pass`
- Exit code: `0`
- Client output: version `0.3.2`, SHA-256 `4243360FA4FEA8F13CAD27147AB0060E48AD62C90B8517D88FC4E5B01C07B258`
- Dedicated output: version `0.3.2`, SHA-256 `1D2BCAE905A8634D96593BB35D3E8D2AA0636701B40A935D555D472543BFF66C`
- Formal assertions: `4`, all `Pass`.
- Product process launch: none.

The complete before/after installed-module inventory JSON files have the same SHA-256:

```text
9A467236DE7B7FACF18B8C54B947EE8CACEA2D2D2C3B754C3EA3510BE37010CA
```

The inventory covers:

- client `Modules\CoopSpectator`;
- legacy client `Modules\CoopSpectatorMP`;
- dedicated `Modules\CoopSpectatorDedicated`.

Both build logs explicitly report compile-only mode. No `DeployModToGame`, `BuildAndDeployDedicatedModule`, or `DeployServerToDedicated` action ran.

## 3. Implemented surface

| Surface | Purpose |
|---|---|
| `Directory.Build.props` | Shared, default-false compile-only output/intermediate/package isolation and local `bin`/`obj` exclusion |
| `CoopSpectator.csproj` | Client compile-only guard, deployment suppression, and implicit dedicated-build suppression |
| `DedicatedServer/CoopSpectatorDedicated.csproj` | Dedicated compile-only guard and installation-deployment suppression |
| `scripts/Invoke-CoopTest.ps1` | Single L0/L1 entry point, run ownership, doctor, aggregate contracts, compile-only proof, assertions, logs, and stable outcomes |
| `Tests/contract-tests.manifest.json` | Reviewed full contract-test inventory |
| `Infrastructure/Automation/CoopAutomationRunContract.cs` | Manifest/role/lease/envelope/event/outcome/recovery/known-issue contracts |
| `Infrastructure/Automation/CoopAutomationProtocolFileIO.cs` | Strict atomic JSON, bounded shared reads, append-safe JSONL, and inbox-to-processed movement |
| `Tests/CoopAutomationProtocol.ContractTests` | Protocol compatibility, correlation, ordering, lease, recovery, outcome, known-issue, and RUN-010 fault tests |
| `Tests/CoopCompileOnly.ContractTests` | Static proof that both project files retain safe compile-only guards |
| `Module/CoopSpectator/CoopShaderCacheModeSwitch.ps1` | Default-empty contract-test watcher readiness signal used only to remove a process-start race |
| `Tests/CoopShaderCacheModeSwitch.ContractTests` | Deterministic watcher readiness before the fault-injection wrapper is terminated |

The pre-existing client-control implementation was upgraded at this milestone to join schema 2 / protocol 1.0 role identities so that it could share the Milestone 2A correlation model. Revision 13 subsequently advances the join request to schema 3 for explicit native platform-login evidence; protocol 1.0 role identities remain unchanged. This historical Milestone 2A evidence still carries no Bannerlord-runtime claim.

## 4. Protocol and fault evidence

Passing contracts cover:

- supported, unknown-major, and unsupported-minor protocol versions;
- cross-run, nonce, source-role, and target-role rejection;
- duplicate, stale, reordered/gapped command sequences;
- valid, expired, identity-mismatched, and invalid lease timelines;
- process failure before acknowledgement, after non-terminal acknowledgement, and after terminal acknowledgement through explicit recovery-state classification;
- concurrent atomic replacement with active readers;
- malformed, empty, oversized, and partial JSON/JSONL content;
- concurrent journal writers and temporary writer locks;
- simulated commit failure, preservation of the prior complete value, and temporary-file cleanup;
- same-volume inbox-to-processed movement, duplicate processed destinations, and repeat reads;
- unknown-field compatibility inside protocol 1.0;
- stable exit-code precedence, required failure-reason vocabulary, and known-native-issue annotations that cannot convert a non-pass outcome to `Pass`.

Local same-volume filesystem semantics are the only verified profile. Cross-volume moves are rejected by the implementation; network-share publication remains unverified and unsupported.

## 5. Requirement completion audit

| Requirement group | Status | Evidence |
|---|---|---|
| SAF-001–SAF-003 | Satisfied for M2A | No phase, readiness, result, or spawn authority was added; no runtime process was launched |
| SAF-004 | Satisfied | Product automation remains default-off; run nonce plaintext is never persisted or placed in arguments |
| SAF-005 | Satisfied | No hot-path diagnostic was added; shader watcher readiness is explicit test-only input and default-empty |
| L0 | Satisfied with expected runtime blockers | Named `Doctor` evidence records hygiene, paths, identities, dependencies, roots, ports, feature flags, Steam, and matrix status without installed mutation |
| L1 | Satisfied | Full 20-project aggregate and independent client/dedicated compile-only proof |
| OUT-001 | Satisfied | Stable exit codes and precedence are contract-tested and exercised by `Pass=0` and `EnvironmentBlocked=10` runs |
| OUT-002 | Satisfied at the contract layer | Required stable reason vocabulary exists; runtime-only reasons are not falsely claimed as observed |
| OUT-003 | Satisfied for M2A | No automatic retry; non-pass doctor retains a redacted distinct-attempt reproduction descriptor |
| OUT-004 | Satisfied at the contract layer | Exact-version/hash annotation contract preserves the original non-pass and requires review on unexpected pass |
| RUN-001–RUN-007 | Satisfied for non-runtime roles | Exact fresh root, categorized artifacts, manifest identity, atomic writes, role correlation, ordered events, exclusive lock, and lease evidence |
| RUN-009 | Satisfied | Protocol 1.0 compatibility and unknown-field policy are explicit and tested |
| RUN-010 | Satisfied | Fault coverage listed in Section 4 passes |
| BLD-001–BLD-004 | Satisfied | Default-false property, isolated output/intermediate/package paths, explicit independent builds, and before/after installed-tree proof |
| TST-001/TST-002/TST-004/TST-005 | Satisfied for M2A | Exact canonical inventory, full report, full option, continued safe scheduling, and broad non-runtime regression run |
| ART-001/ART-002 base schema | Satisfied for M2A | Manifest/status/events, empty command/payload categories, logs, identities, ports, assertions, lock release, and non-pass reproduction evidence |
| No runtime process launch | Satisfied | All authoritative M2A runs observed no product process before or after |
| No Bannerlord battle pass claim | Satisfied | Only L0/L1 evidence is reported |

## 6. Resolved implementation failures

The implementation process exposed and fixed foundation defects before the authoritative runs:

- Windows PowerShell 5.1 required a real same-directory backup path for atomic replacement instead of a null backup argument;
- mandatory empty non-terminal outcome binding required an explicit empty-string allowance;
- strict-mode empty `Compare-Object` output required array normalization;
- redirected intermediate directories required explicit exclusion of each project's old local `bin` and `obj` trees;
- PowerShell pipeline capture changed detached-process timing and exposed a shader-watcher race; direct process control plus a test-only readiness handshake removed it;
- the environment doctor initially checked only the first dedicated multiplayer dependency path and was aligned with the project's actual fallback order.

Failed preliminary run roots remain separate and were not overwritten. They are diagnostic history, not authoritative pass evidence.

## 7. Remaining gate

Milestone 2B remains blocked by the existing Milestone 1 capability findings. Before any client connection or mission-open action, the next approved plan must still prove:

1. safe current-build staging and role-confirmed loaded hashes for both modules;
2. run-scoped result isolation with the `Suppress` policy before mission start;
3. exact runtime process/port ownership, cancellation, cleanup, and recovery;
4. a connection-only normal-lobby run with request/acknowledgement and exact server/client correlation.

No staging, deployment, `start_game`, client join, campaign load, mission open, battle completion, result publication, or campaign writeback was performed by Milestone 2A.
