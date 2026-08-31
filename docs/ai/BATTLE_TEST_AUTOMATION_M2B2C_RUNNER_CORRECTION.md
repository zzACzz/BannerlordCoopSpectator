# Battle Test Automation Milestone 2B.2C Runner Correction

Status: **Aggregate-runner post-start ownership correction implemented and non-runtime verified; client-launcher handoff hardening, review, commit, push, and clean live validation remain**

Verification date: **2026-08-31**

Source baseline: `30f42c3d30126993b01d1673a755a1a34947ecde`

Published implementation revision: `e62f536e2d75ac32a60b623260909cc5245bfc5b`

Post-live correction source baseline: `1bbdd077b1899de4c6d82cde37f179e4d39516ea` (working-tree verification; correction commit not assigned yet)

This milestone implements the Revision 8 console-readiness, command-acceptance, singular-result, and native-log evidence gates exposed by `m2b2-live-feasibility-rerun-20260831-01`. It changes only the aggregate runner, its tested core helper, its focused contract project, and living documentation. It does not change either game module, install a binary, launch a product process, establish a client connection, start a campaign, or provide L2/L3 battle evidence.

## 1. Native readiness and bootstrap acceptance

ILSpy inspection of the installed, hash-pinned dedicated assemblies established the lowest available authoritative lifecycle point:

- `InitialListedGameServerState.OnActivate` writes `is ready! You can now enter console commands` only after the listed-server state is active;
- native standard input reaches `GameNetwork.HandleConsoleCommand`, which forwards to `IGameNetworkHandler.OnHandleConsoleCommand` and then `DedicatedServerConsoleCommandManager.HandleConsoleCommand`;
- option changes emit exact `--Changed: <option>, to: <value>` readbacks;
- successful `ServerSideIntermissionManager.StartGame` emits `--Game is starting...` and `--Selected scene: <map>` only after the usable-map and startup gates pass.

`Feasibility` now redirects and continuously drains the exact owned dedicated process stdout/stderr into run artifacts. It still requires the hash-bound `ModuleReady` status first, but then separately requires the native ready message before writing any bootstrap command. It sends the four option commands one at a time and requires their exact readbacks. It then sends `add_map_to_usable_maps` and `start_game` and requires both native start-game markers for `mp_tdm_map_001`. The UDP-visibility deadline begins only after this evidence succeeds. Missing readiness or command evidence produces an exact bounded failure instead of a generic port timeout.

The capture is asynchronous, bounded to a finite in-memory tail, continuously drained during all dedicated/client waits, and finalized only after exact process cleanup. A synthetic child-process contract exercises stdout, stderr, delayed lines, end-of-stream races, sequence boundaries, complete evidence, and missing evidence in Windows PowerShell 5.1 and PowerShell 7.

## 2. Singular result and cleanup correction

Both Boolean-returning `Process.WaitForExit(int)` calls are now isolated behind `Wait-CoopProcessExitNoOutput`, which explicitly discards the Boolean. Aggregate dispatch captures every pipeline value and rejects zero or multiple values before accepting a result.

Windows PowerShell 5.1 exposes an `[ordered]` result as `System.Collections.Specialized.OrderedDictionary` without adapting its keys into normal `PSObject` properties. The first full verification run executed all 22 contract projects successfully but then exposed this adapter boundary in the new validator. The validator now recognizes `IDictionary`, requires exact `Outcome`, `Reason`, and `ArtifactPath` keys, and normalizes the result into one object. Focused contracts cover zero results, incidental Boolean plus result, missing required fields, ordered-dictionary normalization, and preservation of the primary outcome.

The manifest and feasibility report now distinguish `PrimaryOutcome`/`PrimaryReason` from the terminal outcome. A cleanup, output-finalization, native-log-capture, or remaining-owned-process failure may supersede the terminal result as `RunnerInternalError`, but it cannot erase the original product outcome.

## 3. PID-correlated native logs

After exact process cleanup, `Feasibility` copies only these three files for the recorded dedicated PID:

```text
rgl_log_<PID>.txt
rgl_log_errors_<PID>.txt
watchdog_log_<PID>.txt
```

The source root is `%ProgramData%\Mount and Blade II Bannerlord\logs`. Every file must exist and have a last-write timestamp compatible with the exact recorded process start. Copies are retained below `artifacts\logs\dedicated\native`, and `inventory.json` records the process identity, source/destination paths, lengths, timestamps, and SHA-256 values. Source logs are never modified or deleted.

## 4. Verification evidence

| Run | Result |
|---|---|
| `m2b2c-runner-focused-20260831-07` | Focused runner contracts passed in Windows PowerShell `5.1.26100.9168` and PowerShell `7.6.4` |
| `m2b2c-full-contracts-20260831-01` | All 22 projects passed, then the new validator correctly exposed the Windows PowerShell ordered-dictionary adapter gap; retained as non-final diagnostic evidence |
| `m2b2c-full-contracts-20260831-03` | Final canonical inventory passed 22/22; manifest `PrimaryOutcome=Pass` and `TerminalOutcome=Pass` |
| `m2b2c-compile-only-20260831-02` | Client and dedicated builds exited `0`; installed inventories remained unchanged; no product process launched |
| `m2b2c-r10-contracts-20260831-02` | Final post-live Revision 10 source passed the complete 22/22 inventory; manifest primary and terminal outcomes were `Pass`; no product process launched |
| `m2b2c-r10-compile-20260831-02` | Final post-live Revision 10 client and dedicated builds exited `0`; installed inventories remained unchanged; no product process launched |

The final compile-only client SHA-256 was `38712B7FE759576D23CA9CED49E9CDF01A46C69318498580AC1876C4A1795160`; it is a dirty-source verification output under the run root, not a staged runtime identity. The dedicated compile-only SHA-256 remained `1A9723A3249582FABCF08D3778C10CF448944B928549E6D9582FA7F3C0770626`. The authoritative installed runtime identities remain the unchanged Milestone 2B.2A hashes.

## 5. Clean live validation outcome

Run `m2b2c-live-feasibility-20260831-01` started from clean local/remote revision `e62f536`, a fresh run root, one valid Steam process, free ports `7210` and `7777`, and the exact installed Milestone 2B.2A hashes. `Process.Start()` successfully created dedicated PID `113376`, but the immediate `Get-Process` projection returned a null `.Path`. `Get-CoopProcessIdentity` passed that null value to `Get-FileHash -LiteralPath`, so the runner terminated with `AssertionFailed` before writing its process-start event, provisional ownership record, or redirected-output capture.

The dedicated role continued independently and published `ModuleReady` for PID `113376` with the exact expected loaded hash. No bootstrap command, UDP listener, client launch, campaign, mission, or battle followed. Because ownership registration had not completed, the generated runtime-cleanup inventory was empty even though the starter and its Watchdog descendant remained live. Exact manual recovery revalidated PID, executable path, start time, and parentage: the starter was a direct child of runner PID `120428`, and Watchdog PID `124728` was its direct child. The starter accepted graceful close; only the Watchdog required a forced stop. No related process or port owner remained afterward.

The installed client/dedicated hashes and protected `battle_result.json` hash remained unchanged. All three exact PID `113376` native logs existed under `C:\ProgramData\Mount and Blade II Bannerlord\logs`, but the runner could not collect them because the exact identity object had never been created. The outcome is a shared runner defect affecting every future battle type, not a game-module, map, or battle-adapter failure.

The required aggregate-runner correction is now implemented. Immediately after the dedicated `Process.Start()` returns, the runner records a provisional identity containing a unique launch-operation ID, PID, exact requested executable path, runner parent PID, and narrow launch window, and publishes it to the recovery inventory before identity enrichment. Promotion is bounded, accepts either the normal `Process.Path` value or a `Win32_Process.ExecutablePath` fallback, requires exact requested-path equality, cross-checks process creation time and parent PID, and hashes only the validated path. Promotion replaces the provisional inventory entry instead of creating a second owner.

Exact cleanup now consumes both provisional and verified identities through the tested core primitive. A provisional identity must match PID, requested path, launch window, and expected parent; a verified identity must match PID, path, and exact start time. PID reuse, path substitution, and parent substitution are rejected before a forced stop. Identity-enrichment exceptions carry an explicit `RunnerInternalError` outcome hint, so the feasibility catch no longer misclassifies this runner defect as `AssertionFailed`, and `finally` still discovers descendants and runs cleanup.

Focused contracts exercise null `Process.Path`, validated `Win32_Process` fallback, bounded path failure, exact-path rejection, launch-window and parent checks, verified and provisional PID-reuse rejection, and real cleanup of a synthetic provisionally owned process in Windows PowerShell 5.1 and PowerShell 7. The aggregate runner also provisionally adopts the client immediately after reading the launch artifact and before re-enrichment.

## 6. Remaining cross-script client boundary

`Start-CoopBattleTestClient.ps1` still creates the multiplayer process and then reads `StartTime` and publishes `client-launch.json`. The aggregate runner can adopt and clean the client once that artifact exists, but it cannot recover the PID if the launcher fails after process creation and before artifact publication. This is the same class of post-start handoff risk across a separate script boundary, although it was not the cause of `m2b2c-live-feasibility-20260831-01` and the client was not reached in that run.

The client launcher was outside the approved correction file set and was therefore not changed silently. A separate reviewed plan must add a fail-closed launch handoff or equivalent exact recovery evidence, with focused failure-injection coverage, before a live feasibility rerun is allowed to reach client creation.

## 7. Evidence boundary and next gate

This milestone proves source behavior and controlled process-capture primitives, not Bannerlord runtime acceptance. The first clean live attempt did not reach output-capture creation, so whether the dedicated executable sends all native readiness/readback messages through redirected stdout remains a live hypothesis.

No game-side binary changed, so module restaging is not required. The aggregate correction has passed focused cross-PowerShell contracts, the full canonical inventory, and compile-only verification, but it remains uncommitted and live-unverified. The next gates are review, the separate client-launcher handoff correction, final non-runtime verification, commit, and push. Only then may a clean live rerun be approved. That rerun must stop before campaign automation and must not claim L2 or L3 evidence unless its explicit connection criteria are reached.
