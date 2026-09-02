# Milestone 2B.3A Runtime Safety Closure

Status: **Runtime binaries staged; role health and connection live-confirmed; per-resource shared-lock correction source/contract-verified; clean publication and repeat live lock proof pending**
Source verification date: **2026-09-02**
Protocol: **1.1**

## 1. Scope and evidence boundary

This change set closes the source/contract gaps identified after clean connection run `m2e1-handoff-live-r1-01`. It is battle-type-independent and changes no campaign fixture, mission adapter, battle phase, result application, or installed module tree.

The verified source surface now covers:

- live-role heartbeat, progress, state-entry, state-revision, monotonic elapsed-time, authoritative-source, and structured-error fields;
- distinct `NoHeartbeat` and `NoProgress` runner classification;
- canonical shared locks for the game installation, dedicated installation, automation bridge root, machine profile, and fixed UDP ports;
- explicit run-scoped `Cancel` plus real console-signal retention of cleanup control;
- `RecoveryV2` read-only preview, active-lock refusal, exact action preview, immediate PID/path/start revalidation, PID-reuse rejection, reporting, and verified lock release;
- exact path plus owned-process-tree or command-line-PID correlation for CrashUploader, Watchdog, and WerFault support processes;
- structured `FatalAutomationFailure`, `CrashReporterDetected`, `crash.json`, and `hang.json` evidence paths.

Controlled transaction `m2b3a-stage-01` later installed the clean-published client/dedicated binaries with complete retained pre-images. Clean run `m2b3a-live-r1-01` live-confirmed schema-2 role health, exact loaded identities, native login, terminal `Connected`, result suppression, exact graceful cleanup, and protected-result preservation. It opened no campaign fixture or cooperative battle and made no L2/L3 claim. Its shared-lock artifact exposed a separate aggregate construction defect described below, so it is not complete RUN-008 proof.

## 2. Protocol and capability changes

The compatible protocol minor version advances from `1.0` to `1.1`. Existing `1.0` request envelopes remain readable where the request schema permits backward compatibility. New safety behavior is fail-closed behind explicit capabilities:

- `RoleHealthV1`;
- `CancellationV1`;
- `RecoveryV2`;
- `FailureEvidenceV1`.

Runtime role status uses schema 2 and requires exact protocol 1.1. Dedicated bootstrap and client-join request schemas remain independently versioned; their generated status files may advertise protocol 1.1 without silently reinterpreting legacy request fields.

## 3. Role health projection

`CoopAutomationRuntimeBridge` retains role identity only after the explicit automation profile, run root, token, result policy, and loaded module hash validate. The client controller and dedicated control bridge call the common projection from their existing application ticks. No new `SubModule` hook or battle adapter was added.

The bridge writes at most once per second unless the authoritative state changes. It is disabled outside the explicit automation profile. State progress advances `StateRevision` and updates `LastProgressUtc`; heartbeat publication advances independently. Monotonic elapsed values are derived from `Stopwatch`, not wall-clock subtraction.

The aggregate validates exact schema/capability/run/token/role identity before accepting health. A stale heartbeat yields `NoHeartbeat`; a current heartbeat with stale progress yields `NoProgress`.

## 4. Shared runtime locks

`Feasibility` acquires all shared resources in canonical sorted order after manifest and lease publication and before any product process launch. Lock records are retained under `artifacts/processes/shared-runtime-locks.json`. Conflicts return `EnvironmentBlocked`. Cleanup releases every held stream and then reopens each exact lock path exclusively; results are retained in `shared-runtime-lock-release.json`.

The lock set covers:

- automation bridge root;
- exact game installation root;
- exact dedicated installation root;
- machine/profile identity;
- requested UDP port;
- fixed UDP port `7777`.

First clean live run `m2b3a-live-r1-01` exposed that the production PowerShell argument expression concatenated all six intended ids into one space-separated string. The core lock primitive was correct, and its literal-array contract passed, but the live artifact contained one record and therefore protected only an identical complete resource combination rather than every overlapping resource. The defect precedes all battle adapters and affects every runtime scenario equally.

The source correction adds `Get-CoopSharedRuntimeResourceIdsCore`, constructs each id independently with explicit cardinality, validates the exact acquired set before product launch, and releases any unexpected acquisition before failing. Cross-PowerShell contracts now require six records for ports `7210`/`7777`, five when the requested port is also `7777`, and an independent collision for every generated resource.

## 5. Cancellation

`Invoke-CoopTest.ps1 -Command Cancel -RunId <id>` reads an existing run only, requires protocol 1.1 plus `CancellationV1`, validates the exact live runner PID/path/start identity, requires a fresh lease and an actively held runner lock, then atomically writes one target-bound cancellation request. It cannot cancel an uncertain or inactive run.

The active aggregate checks the request during bounded waits and child-process polling. Console cancellation uses a minimal C# `Console.CancelKeyPress` handler that only sets an atomic flag and requests deferred cancellation; cleanup and artifact I/O remain on the main runner thread. Terminal acknowledgement is written as `Cancelled` after exact cleanup.

## 6. RecoveryV2

`Inspect` and `Recover` without `-ApplyRecovery` remain read-only. Preview reports every exact proposed action and states `DeletesRunRoot=false`. Apply additionally requires protocol/capability evidence, matching manifest/lease/inventory identity, and exclusive acquisition of the abandoned runner lock.

Every action revalidates PID, executable path, and process start immediately before graceful/forced stop. A live PID with a shifted start time or mismatched path/parent is reported in `RejectedIdentities` and is never stopped. `recovery.json` records preview actions, actual actions, exact remaining identities, shared-lock release probes, and either `Recovered` or `RecoveryIncomplete`. The run root is never deleted automatically.

## 7. Failure evidence and cleanup

Crash/modal helpers are not killed by process name. Correlation requires an allowlisted exact executable path plus either owned process-tree membership or an exact owned PID in the helper command line. A correlated live helper is promoted into the same exact identity inventory before cleanup.

Crash evidence selects `crash.json`; non-crash deadline/hang evidence selects `hang.json`. Each record includes last role state/revision/heartbeat/progress, recent event lines, exact owned identities, correlated helper evidence, correlation ownership failures, and an explicit dump-attempt state. Lack of a configured dump collector cannot suppress the JSON failure artifact.

## 8. Verification evidence

| Verification | Result |
|---|---|
| Focused protocol/runtime/join/runner contracts | Passed |
| Runner primitives under Windows PowerShell 5.1 | Passed |
| Runner primitives under PowerShell 7.6.4 | Passed |
| Real isolated `CTRL_BREAK_EVENT` delivery to both PowerShell hosts | Passed; handler retained cleanup control and observed the signal |
| Explicit `Cancel` against an exact synthetic active runner | Passed; protocol/run/nonce/target identity retained |
| `Recover` preview against an active synthetic runner | Passed; read-only, no report mutation, runner remained alive |
| `Recover -ApplyRecovery` against an active synthetic runner | Passed with `EnvironmentBlocked`; runner remained alive |
| Synthetic exact owned-process cleanup | Passed |
| Shifted process-start PID-reuse rejection | Passed |
| Exact crash-helper process correlation | Passed; unrelated same-path helper rejected |
| Final aggregate run `m2b3a-final-c-01` | Passed 22/22; protocol 1.1 terminal `Pass`; verified runner-lock release; dirty source verification only |
| Completed-run recovery preview | Passed; no `recovery.json` was created |
| Completed-run `Recover -ApplyRecovery` after empty-lock correction | `Recovered`; `coop-runtime-recovery-v2`, no run-root deletion, locks released |
| Final compile-only run `m2b3a-final-b-01` | Passed; client and dedicated compiled; installed inventories unchanged; no product process launched; verified runner-lock release |
| Clean-published aggregate `m2b3a-pub-c-01` | Passed 22/22 from exact clean local/upstream revision `5d6a6c6`; protocol 1.1; runner lock released |
| Clean-published compile `m2b3a-pub-b-01` | Passed; installed inventories unchanged; no product process launched; client `A363...0075`, dedicated `2E14...1414` |
| Controlled staging `m2b3a-stage-01` | Installed exact clean binaries; complete 32-file client and 218-file dedicated pre-images retained; protected result unchanged; no product process launched |
| Launcher validation `m2b3a-poststage-client-validate-01` | Passed exact client hash/version and live Steam checks without product launch |
| Clean live run `m2b3a-live-r1-01` | Connection/role-health slice passed; exact hashes, live schema-2 heartbeat/progress, native login, terminal `Connected`, graceful cleanup, free ports, no crash/hang artifact, protected result unchanged |
| Live shared-lock artifact audit | Failed RUN-008 closure: one concatenated lock record instead of six independent records |
| Exact production-expression reproduction | Reproduced identically in Windows PowerShell 5.1 and PowerShell 7.6.4 |
| Shared-lock construction correction contracts | Passed in both PowerShell hosts; every generated resource independently collides and releases |
| Dirty-source aggregate `m2b3a-lockfix-c-01` | Passed 22/22; no product process launched |

The first completed-run apply attempt exposed a strict-mode empty-collection defect when no shared-runtime-lock artifact existed. It stopped before `recovery.json` publication and had no process target. The collection was normalized explicitly and the identical apply was repeated successfully.

Selected clean-published identities installed by `m2b3a-stage-01` are:

- client: `A363B19BFFBDA8EEDEE99EEB90E12DA0BFD508C2EA67DE70A5ACAC9DC91C0075`;
- client PDB: `E031D36E6F3F114A171EDDE12E6DFA2B7E14BCCFBC98BD57B9BC737640595DEF`;
- dedicated: `2E1494BCAEE1DCE440B4373BBA99A4F724B9C32519AACD486DE8F041C0CA1414`.

The retained client pre-image contains DLL `7CC2...7E97`; the dedicated pre-image contains both DLLs at `BD328...02A78`. The staging operation changed only the client DLL/PDB and two dedicated DLL paths. No rollback was required.

## 9. Remaining gate

Role health, exact binary identity, connection, cleanup, and passing-path crash/hang absence are now live-confirmed. RUN-008 per-resource lock isolation remains open. Before promoting the complete Milestone 2B runtime-safety row to live-confirmed:

1. commit and push the per-resource lock correction and revised evidence separately as approved;
2. rerun the 22-project inventory from the exact clean published revision;
3. retain the already installed DLLs because the correction changes only runner scripts/tests and documentation;
4. execute a separately approved clean `Feasibility` run;
5. require exactly six independently recorded/released resources for the default `7210`/`7777` profile, plus the already proven heartbeat/progress, exact cleanup, unchanged protected result, and passing-path crash/hang absence.

Fault-path JSON and recovery contracts remain source/synthetic evidence until an explicitly approved non-destructive fault-injection run is performed. Milestone 3 must not start from this document alone.
