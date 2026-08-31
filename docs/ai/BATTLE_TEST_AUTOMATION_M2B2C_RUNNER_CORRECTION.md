# Battle Test Automation Milestone 2B.2C Runner Correction

Status: **Source, focused contracts, full canonical contracts, and compile-only verification complete; clean committed live rerun pending**

Verification date: **2026-08-31**

Source baseline: `30f42c3d30126993b01d1673a755a1a34947ecde`

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

The final compile-only client SHA-256 was `38712B7FE759576D23CA9CED49E9CDF01A46C69318498580AC1876C4A1795160`; it is a dirty-source verification output under the run root, not a staged runtime identity. The dedicated compile-only SHA-256 remained `1A9723A3249582FABCF08D3778C10CF448944B928549E6D9582FA7F3C0770626`. The authoritative installed runtime identities remain the unchanged Milestone 2B.2A hashes.

## 5. Evidence boundary and next gate

This milestone proves source behavior and controlled process-capture primitives, not Bannerlord runtime acceptance. Whether the dedicated executable sends all native readiness/readback messages through redirected stdout remains a live hypothesis until the next bounded run.

No game-side binary changed, so module restaging is not required. The next gate is review, a documentation/source commit and push, followed by one separately approved clean-revision connection-only `Feasibility` rerun with a fresh `RunId`. That run must stop before campaign automation and must not claim L2 or L3 evidence unless its explicit connection criteria are reached.
