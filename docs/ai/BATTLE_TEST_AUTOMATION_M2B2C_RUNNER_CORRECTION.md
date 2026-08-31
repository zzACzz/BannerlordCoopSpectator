# Battle Test Automation Milestone 2B.2C Runner Correction

Status: **Aggregate-runner and client-launcher post-start ownership corrections published; dedicated ownership/cleanup live-verified; dedicated control channel blocked before client launch**

Verification date: **2026-08-31**

Source baseline: `30f42c3d30126993b01d1673a755a1a34947ecde`

Published implementation revision: `e62f536e2d75ac32a60b623260909cc5245bfc5b`

Aggregate ownership correction source baseline: `1bbdd077b1899de4c6d82cde37f179e4d39516ea`

Published aggregate ownership correction: `8f68d433b575b466e42818e9cb1eaabc05f5d865`

Client-launcher handoff source baseline: `8f68d433b575b466e42818e9cb1eaabc05f5d865`

Published client-launcher handoff correction: `711f2cac20ca05d3e81233aa6acfa60816dcce99`

This milestone implements the Revision 8 console-readiness, command-acceptance, singular-result, and native-log evidence gates exposed by `m2b2-live-feasibility-rerun-20260831-01`. It changes only the aggregate runner, its tested core helper, its focused contract project, and living documentation. It does not change either game module, install a binary, launch a product process, establish a client connection, start a campaign, or provide L2/L3 battle evidence.

## 1. Native readiness and bootstrap acceptance

ILSpy inspection of the installed, hash-pinned dedicated assemblies established the lowest available authoritative lifecycle point:

- `InitialListedGameServerState.OnActivate` writes `is ready! You can now enter console commands` only after the listed-server state is active;
- native standard input reaches `GameNetwork.HandleConsoleCommand`, which forwards to `IGameNetworkHandler.OnHandleConsoleCommand` and then `DedicatedServerConsoleCommandManager.HandleConsoleCommand`;
- option changes emit exact `--Changed: <option>, to: <value>` readbacks;
- successful `ServerSideIntermissionManager.StartGame` emits `--Game is starting...` and `--Selected scene: <map>` only after the usable-map and startup gates pass.

The Milestone 2B.2C `Feasibility` source redirects and continuously drains the exact owned dedicated process stdout/stderr into run artifacts. Its original contract requires the hash-bound `ModuleReady` status first, then separately requires the native ready message before writing any bootstrap command. It sends the four option commands one at a time and requires their exact readbacks. It then sends `add_map_to_usable_maps` and `start_game` and requires both native start-game markers for `mp_tdm_map_001`. The UDP-visibility deadline begins only after this evidence succeeds. Missing readiness or command evidence produces an exact bounded failure instead of a generic port timeout. Section 8 records the later clean live proof that redirected standard streams do not provide this evidence for the exact starter profile, so this source contract is not the final control-channel design.

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
| `m2b2c-client-handoff-contracts-20260831-01` | Client-launcher handoff source passed the complete 22/22 inventory, including focused Windows PowerShell 5.1/PowerShell 7 ordering and synthetic post-start cleanup coverage; no product process launched |
| `m2b2c-client-handoff-compile-20260831-01` | Client-launcher handoff source built both modules with exit code `0`; installed inventories remained unchanged; no product process launched |

The final compile-only client SHA-256 was `38712B7FE759576D23CA9CED49E9CDF01A46C69318498580AC1876C4A1795160`; it is a dirty-source verification output under the run root, not a staged runtime identity. The dedicated compile-only SHA-256 remained `1A9723A3249582FABCF08D3778C10CF448944B928549E6D9582FA7F3C0770626`. The authoritative installed runtime identities remain the unchanged Milestone 2B.2A hashes.

## 5. Clean live validation outcome

Run `m2b2c-live-feasibility-20260831-01` started from clean local/remote revision `e62f536`, a fresh run root, one valid Steam process, free ports `7210` and `7777`, and the exact installed Milestone 2B.2A hashes. `Process.Start()` successfully created dedicated PID `113376`, but the immediate `Get-Process` projection returned a null `.Path`. `Get-CoopProcessIdentity` passed that null value to `Get-FileHash -LiteralPath`, so the runner terminated with `AssertionFailed` before writing its process-start event, provisional ownership record, or redirected-output capture.

The dedicated role continued independently and published `ModuleReady` for PID `113376` with the exact expected loaded hash. No bootstrap command, UDP listener, client launch, campaign, mission, or battle followed. Because ownership registration had not completed, the generated runtime-cleanup inventory was empty even though the starter and its Watchdog descendant remained live. Exact manual recovery revalidated PID, executable path, start time, and parentage: the starter was a direct child of runner PID `120428`, and Watchdog PID `124728` was its direct child. The starter accepted graceful close; only the Watchdog required a forced stop. No related process or port owner remained afterward.

The installed client/dedicated hashes and protected `battle_result.json` hash remained unchanged. All three exact PID `113376` native logs existed under `C:\ProgramData\Mount and Blade II Bannerlord\logs`, but the runner could not collect them because the exact identity object had never been created. The outcome is a shared runner defect affecting every future battle type, not a game-module, map, or battle-adapter failure.

The required aggregate-runner correction is now implemented. Immediately after the dedicated `Process.Start()` returns, the runner records a provisional identity containing a unique launch-operation ID, PID, exact requested executable path, runner parent PID, and narrow launch window, and publishes it to the recovery inventory before identity enrichment. Promotion is bounded, accepts either the normal `Process.Path` value or a `Win32_Process.ExecutablePath` fallback, requires exact requested-path equality, cross-checks process creation time and parent PID, and hashes only the validated path. Promotion replaces the provisional inventory entry instead of creating a second owner.

Exact cleanup now consumes both provisional and verified identities through the tested core primitive. A provisional identity must match PID, requested path, launch window, and expected parent; a verified identity must match PID, path, and exact start time. PID reuse, path substitution, and parent substitution are rejected before a forced stop. Identity-enrichment exceptions carry an explicit `RunnerInternalError` outcome hint, so the feasibility catch no longer misclassifies this runner defect as `AssertionFailed`, and `finally` still discovers descendants and runs cleanup.

Focused contracts exercise null `Process.Path`, validated `Win32_Process` fallback, bounded path failure, exact-path rejection, launch-window and parent checks, verified and provisional PID-reuse rejection, and real cleanup of a synthetic provisionally owned process in Windows PowerShell 5.1 and PowerShell 7. The aggregate runner also provisionally adopts the client immediately after reading the launch artifact and before re-enrichment.

## 6. Hardened cross-script client boundary

`Start-CoopBattleTestClient.ps1` now uses the tested runner core. Immediately after `Process.Start()` returns, it creates an exact provisional client identity from the PID, requested executable path, aggregate-runner parent PID, narrow launch window, and unique launch-operation ID. It atomically publishes `client-launch.provisional.json`, performs bounded exact path/start/parent observation with the validated `Win32_Process` fallback, and only then atomically publishes verified schema-v3 `client-launch.json`.

The final launch artifact is the ownership handoff boundary. Before it exists, every post-start exception triggers exact cleanup through the provisional identity and is propagated as `RunnerInternalError`; best-effort `client-launch.cleanup.json` retains the primary failure and cleanup result. After final publication, no fallible user-output step remains and local wrapper/lock disposal cannot invalidate the handoff. The aggregate runner then adopts and re-enriches the verified client identity as before.

Focused source contracts enforce the operation order and inject a synthetic post-start artifact failure against a real child process in Windows PowerShell 5.1 and PowerShell 7. The process is proven absent after exact provisional cleanup. This closes the ordinary exception path; abrupt machine or runner termination remains governed by retained run artifacts and the existing explicit recovery command.

## 7. Evidence boundary and next gate

The ownership milestone now has both source evidence and a bounded Bannerlord runtime proof for the dedicated role. It does not have client-launch, connection, campaign, mission, or battle proof.

No game-side binary changed in the aggregate/client ownership corrections, so the clean live run continued to use the exact staged `0.3.2` module hashes. The client-launcher correction is published, but its post-start handoff remains live-unverified because the dedicated bootstrap never reached the client-launch boundary.

## 8. Clean live ownership proof and console-channel blocker

Run `m2b2c-client-handoff-live-20260831-01` started from clean local and remote revision `711f2cac20ca05d3e81233aa6acfa60816dcce99`, a fresh run root, one valid Steam process, free ports `7210` and `7777`, no product process, exact installed client hash `B576B8EA0FB223126A65E062CB562FD15815DF8BA1ADDB1797506914B48D7928`, exact installed dedicated hash `1A9723A3249582FABCF08D3778C10CF448944B928549E6D9582FA7F3C0770626`, and protected-result pre-image `D5EF79D59FA97EF4C95BB7AB31803AE1F475EB24498F4469B83CD3B7AD955AD3`.

The runner provisionally owned and then exactly verified dedicated PID `153416`, including requested path, parent PID `107340`, launch window, start time, executable SHA-256, and `ProcessAndWin32Process` path evidence. The exact dedicated module published `ModuleReady`; its native log then showed successful TaleWorlds connection/login and continuing `AliveMessage` responses until the bounded timeout.

The runner timed out waiting for `NativeConsoleReady`. Both redirected `stdout.txt` and `stderr.txt` remained zero bytes. The exact PID-correlated `rgl_log_153416.txt` reached `323574` bytes and began with `Mount and Blade II Bannerlord Console Started...`, while the owned process tree contained a `conhost.exe` descendant. ILSpy reconfirmed that `InitialListedGameServerState.OnActivate` emits the required ready text through `Console.WriteLine`. The ready text was absent from both redirected streams and the PID-correlated native log. This proves that redirected standard output is not a trustworthy sole evidence channel for this exact starter profile; it does not prove that the native lifecycle point failed to occur. Redirected standard input also remains unverified because fail-closed ordering prevented any command write.

No bootstrap command, UDP endpoint, client process, client loaded identity, lobby handoff, connection, campaign, mission, or battle was reached. The report correctly retained `CampaignStarted=false`, `CampaignBattleFixtureOpened=false`, and `L2OrL3PassClaimed=false`.

Cleanup revalidated the exact starter identity, requested graceful close, used no forced stop, registered the exact Watchdog and `conhost` descendants, and left `RemainingOwnedProcesses` empty. The runner lock was released and reacquired, both required ports were free, the installed module hashes and protected result were unchanged, and the repository remained clean. This is successful live proof of the corrected dedicated ownership/cleanup path, not a successful connection feasibility result.

The next implementation gate is a separately approved run-scoped dedicated readiness, command-intent, and acknowledgement channel tied to the exact run, token hash, process identity, and loaded module hash. Standard streams and PID-native logs remain useful retained diagnostics but cannot be the sole control contract. The shared bootstrap occurs before scenario selection, so this blocker applies to every future battle type rather than one map or adapter. Another unchanged retry or a longer timeout is not justified.
