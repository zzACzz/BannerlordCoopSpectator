# Milestone 2B.3A Runtime Safety Closure

Status: **Source and contracts implemented; staging and live confirmation pending**
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

This is not live game evidence. No staging, Bannerlord launch, dedicated launch, campaign, cooperative mission, battle, L2, or L3 action occurred.

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

The first completed-run apply attempt exposed a strict-mode empty-collection defect when no shared-runtime-lock artifact existed. It stopped before `recovery.json` publication and had no process target. The collection was normalized explicitly and the identical apply was repeated successfully.

Compile-only candidate identities from the dirty source-verification run are:

- client: `EB488263D5A92F50ECAF55AEE4423197E0D126C81A61772B8BEA9AEDB5B99BB3`;
- dedicated: `2E1494BCAEE1DCE440B4373BBA99A4F724B9C32519AACD486DE8F041C0CA1414`.

These hashes are not staging candidates because the manifest correctly records `RepositoryDirty=true`.

## 9. Remaining gate

Milestone 2B.3A is complete only at the source/contract level. Before promoting the Milestone 2B runtime-safety rows to live-confirmed:

1. commit and push the reviewed source and documentation separately as approved;
2. rerun contracts and compile-only from the clean published revision;
3. stage only the selected clean binaries with a retained pre-image;
4. execute a separately approved clean `Feasibility` run;
5. verify live role heartbeats/progress, shared lock acquisition/release, exact cleanup, unchanged protected result, and no `crash.json`/`hang.json` on the passing path.

Fault-path JSON and recovery contracts remain source/synthetic evidence until an explicitly approved non-destructive fault-injection run is performed. Milestone 3 must not start from this document alone.
