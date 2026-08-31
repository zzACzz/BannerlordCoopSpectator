# Battle Test Automation Milestone 2B.2B Live Feasibility Attempts and Runner Findings

Status: **Dedicated loaded identity, discrete command emission, bounded discovery, and exact cleanup confirmed; native console readiness, UDP visibility, and client connection not reached; another runner correction is required**

Execution and correction date: **2026-08-31**

Attempt repository revision: `9f4ee733d8e9b83b5fef4f194140daf97a0d89a8`

Clean rerun repository revision: `70a40db6aa71a27e6b1afa0892118ac77871c1a3`

Installed game-side binary source revision: `12abf36`

This document records both Milestone 2B live feasibility attempts, their exact evidence boundaries, the shared runner defects they exposed, exact recovery/cleanup, and the non-runtime verification between attempts. It does not claim a client connection, campaign startup, cooperative mission, completed battle, L2, or L3 evidence.

## 1. Outcome and evidence boundary

Run `m2b2-live-feasibility-20260831-01` launched the exact dedicated executable as PID `141096`. The dedicated module reported:

- state `ModuleReady`;
- module SHA-256 `1A9723A3249582FABCF08D3778C10CF448944B928549E6D9582FA7F3C0770626`;
- an exact match to its expected SHA-256;
- `ResultPolicy=Suppress`;
- the expected `RunId`, token fingerprint, executable path, PID, and process start time.

This establishes `ConfirmedLoadedHash` for the dedicated role only. The run did not establish native server visibility, UDP ownership, client process creation, client loaded identity, normal-lobby handoff, network connection, campaign startup, or cooperative battle behavior.

## 2. Attempt timeline

The fresh run root was created at:

```text
C:\Users\Admin\AppData\Local\Temp\CoopSpectator\Automation\m2b2-live-feasibility-20260831-01
```

The manifest bound the clean runner revision `9f4ee733d8e9b83b5fef4f194140daf97a0d89a8` to the staged client hash `B576B8EA0FB223126A65E062CB562FD15815DF8BA1ADDB1797506914B48D7928` and dedicated hash `1A9723A3249582FABCF08D3778C10CF448944B928549E6D9582FA7F3C0770626`.

The dedicated process started at `2026-08-31T06:02:04.6121436Z` and acknowledged its module at `2026-08-31T06:02:19.6276569Z`. The runner then recorded one malformed console-command event:

```text
ServerName AC_COOP_M2B2_20260831_01 MaxNumberOfPlayers 16 GameType TeamDeathmatch Map mp_tdm_map_001 add_map_to_usable_maps mp_tdm_map_001 TeamDeathmatch start_game
```

The six intended commands were concatenated into one line. Consequently:

- no requested UDP listener appeared;
- no client process was launched;
- no campaign was loaded;
- no cooperative mission or battle began;
- no result was consumed or written by campaign automation.

The port-wait deadline ended at approximately `2026-08-31T06:05:19Z`, after which the runner stalled in descendant discovery rather than reaching terminal cleanup. The exact still-live runner identity was revalidated and stopped so that read-only `Inspect` and recovery could proceed.

`Recover -ApplyRecovery` acquired the abandoned run and found exactly one recorded live product identity: dedicated PID `141096`. Recovery requested graceful close, did not use forced termination, and confirmed zero remaining owned processes. The known watchdog and console-host descendants also exited. Ports `7210` and `7777` were free afterward.

The protected global result file resolved through the redirected Documents folder to:

```text
C:\Users\Admin\OneDrive\Documents\Mount and Blade II Bannerlord\CoopSpectator\battle_result.json
```

Its SHA-256 remained `D5EF79D59FA97EF4C95BB7AB31803AE1F475EB24498F4469B83CD3B7AD955AD3`. Installed module inventories and hashes also remained unchanged. Post-recovery doctor run `m2b2-postrecover-doctor-20260831-01` returned only the expected `RuntimeVersionCombinationNotYetVerified` blocker.

## 3. Shared root causes

### Bootstrap command construction

The runner built the intended command list through a PowerShell comma/addition expression whose parse result was a single aggregate object. Direct probes in Windows PowerShell 5.1 and PowerShell 7 both returned a count of one. This defect is in the shared `Feasibility` runner path and therefore is not specific to `TeamDeathmatch` or to one future battle scenario.

### Descendant discovery

The original discovery loop fetched the process list and then issued recursive `Get-CimInstance` calls for every candidate and ancestry hop. The observed machine snapshot contained 316 processes; at the configured depth, a single root could cause up to 5,056 WMI queries. The live runner therefore had no practical end-to-end bound after its port deadline. This defect also affects the shared runtime ownership path for every future battle type.

## 4. Correction

The correction adds `scripts/CoopAutomationRunner.Core.ps1` and keeps its algorithms independent of Bannerlord runtime code:

- `New-CoopDedicatedBootstrapCommands` constructs exactly six ordered command strings with explicit list insertion;
- `Get-CoopDescendantProcessRecordsFromSnapshot` traverses one supplied process snapshot entirely in memory;
- invalid process records, duplicate PIDs, cycles, unrelated branches, and excessive descendant counts fail closed;
- `Invoke-CoopTest.ps1` captures one CIM snapshot with a 10-second operation timeout;
- descendant registration is capped at 256 identities and a 30-second total deadline;
- each discovered identity is revalidated by PID, executable path, parent, and start time before registration;
- the bounded snapshot is retained as `artifacts/processes/runtime-process-tree-snapshot.json`;
- discovery failure records `RunnerInternalError` and `OwnedDescendantDiscoveryFailed`, then still proceeds to exact root cleanup.

The correction touches only explicit automation-runner scripts and contract tests. It does not add production hot-path diagnostics and does not alter client or dedicated game runtime source.

## 5. Correction verification

| Check | Result |
|---|---|
| PowerShell parser validation | Passed for `scripts/CoopAutomationRunner.Core.ps1` and `scripts/Invoke-CoopTest.ps1` in Windows PowerShell 5.1 and PowerShell 7 |
| Focused command/tree contracts | `m2b2-fix-runner-focused-20260831-02` passed in Windows PowerShell 5.1.26100.9168 and PowerShell 7.6.4 |
| Full canonical contracts | `m2b2-fix-final-contracts-20260831-01` passed 22/22 projects |
| Compile-only | `m2b2-fix-compile-only-20260831-01` passed; installed client and dedicated inventories were unchanged |
| Harmless real process-tree probe | One hidden PowerShell child was found from a single 320-process snapshot; two descendants were resolved in 527 ms; the exact child was stopped; no Bannerlord product process was launched |
| Lowest-level client binary comparison | ILSpy produced identical decompiled IL for the staged clean client DLL and the dirty-tree compile output; both IL trees hashed to `97F1A619A54F9F3493202A57B7AE947DBD95E1D6E17B8D1A2C09D3C410598857` |

The compile-only client PE hash was `305F63FD8E2B3D1E578921E9A3B56517F6AD74887E78920C71810DA4BFF3B763`, while the staged clean client PE hash remains `B576B8EA0FB223126A65E062CB562FD15815DF8BA1ADDB1797506914B48D7928`. The identical IL proves that this difference is build/debug metadata from the dirty source-verification context, not a production C# change. The dedicated compile hash remained `1A9723A3249582FABCF08D3778C10CF448944B928549E6D9582FA7F3C0770626`.

## 6. Retained evidence roots

| Purpose | Root |
|---|---|
| First live attempt and exact recovery | `%LOCALAPPDATA%\Temp\CoopSpectator\Automation\m2b2-live-feasibility-20260831-01` |
| Clean correction-revision live rerun | `%LOCALAPPDATA%\Temp\CoopSpectator\Automation\m2b2-live-feasibility-rerun-20260831-01` |
| Focused corrected-runner contracts | `%LOCALAPPDATA%\Temp\CoopSpectator\Automation\m2b2-fix-runner-focused-20260831-02` |
| Final full contract inventory | `%LOCALAPPDATA%\Temp\CoopSpectator\Automation\m2b2-fix-final-contracts-20260831-01` |
| Compile-only verification | `%LOCALAPPDATA%\Temp\CoopSpectator\Automation\m2b2-fix-compile-only-20260831-01` |
| ILSpy comparison | `%LOCALAPPDATA%\Temp\CoopSpectator\Automation\m2b2-fix-il-compare-20260831-01` |

## 7. Clean correction-revision live rerun

Run `m2b2-live-feasibility-rerun-20260831-01` used clean committed and pushed revision `70a40db6aa71a27e6b1afa0892118ac77871c1a3`, exact installed client hash `B576B8EA0FB223126A65E062CB562FD15815DF8BA1ADDB1797506914B48D7928`, and exact installed dedicated hash `1A9723A3249582FABCF08D3778C10CF448944B928549E6D9582FA7F3C0770626`. Steam was running, the repository was clean, ports `7210` and `7777` were free, no product process existed, and the run root was fresh.

The run proved that the first correction works in a real dedicated process:

- dedicated PID `123600` reported `ModuleReady`, the exact expected loaded hash, and `ResultPolicy=Suppress`;
- events 3–8 recorded exactly six separate commands in the intended order;
- the bounded process-tree snapshot completed in 808 ms from one 316-process snapshot and found exactly the watchdog and console-host descendants;
- exact cleanup gracefully stopped the dedicated process without forced termination and observed both descendants already stopped;
- `Inspect` validated the manifest, lease, and three recorded identities and reported `LiveExactProcessCount=0`;
- no client, campaign, cooperative mission, or battle started;
- ports `7210` and `7777` were free afterward;
- installed module hashes and the protected result hash `D5EF79D59FA97EF4C95BB7AB31803AE1F475EB24498F4469B83CD3B7AD955AD3` remained unchanged;
- no crash artifact or crash-reporter process appeared.

The authoritative feasibility report correctly recorded `Outcome=Timeout` and reason `Timed out waiting for owned UDP port 7210.` The client was not launched because the owned port gate never passed.

The terminal manifest separately recorded `RunnerInternalError` with `The property 'Outcome' cannot be found on this object.` This disagreement is a runner defect, not evidence that the product timeout did not occur. `Stop-CoopExactProcessIdentity` calls the Boolean-returning `Process.WaitForExit(int)` without suppressing its output. That incidental Boolean joins the intended structured function result in the PowerShell pipeline, so the aggregate caller does not receive exactly one object with an `Outcome` property. The same shared cleanup path can affect any scenario or terminal outcome.

## 8. Native readiness evidence

The native dedicated log is:

```text
C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_123600.txt
```

Its SHA-256 is `A3A6101A4FF58CEF48FCC2C12916FFF6E559345EC15D71AF02CA107CDFEC0EAF`. The native error-log SHA-256 is `0BD87E4E81DDA2DE0F484CB3C7F398788CBE256C4849492EDFB7C24A652B36CC`, and the watchdog-log SHA-256 is `2BF35E61AD7DC7D23BE3CBC43C4CBD755869D3F5372402FAE7C649EB6B4785CF`.

The runner emitted its first command at local time `10:04:23.414`. The native log proves that startup initialization was still active through at least `10:04:25.079`, login began at `10:04:25.094`, login succeeded at `10:04:25.525`, and the first successful alive response arrived at `10:04:27.377`. The exact server name, map, usable-map command, and `start_game` text do not appear in the native log.

Lowest-level source and ILSpy inspection explains the ordering gap:

- the mod writes `ModuleReady` from `DedicatedServer.SubModule.OnSubModuleLoad` immediately after hashing its assembly;
- the native `DedicatedServerConsoleCommandManager.HandleConsoleCommand` is reached later through `IGameNetworkHandler.OnHandleConsoleCommand`;
- therefore `ModuleReady` proves loaded binary identity and result policy but does not prove that the native console-command handler can yet receive standard input.

The strongest evidence-supported inference is that the six correctly separated commands were written before the native console path became ready and were not accepted. This rerun does not prove that the selected map or game type is invalid because command acceptance was never established.

The requested `/LogOutputPath` did not place these native logs below the run root; they remained under `C:\ProgramData`. Future authoritative attempts must collect the exact PID-correlated native logs into the run artifact set.

## 9. Required correction before another live run

The next implementation plan must address all three shared runner defects:

1. add a distinct, run-scoped native console-ready acknowledgement after `ModuleReady`; an elapsed sleep is not sufficient;
2. require acceptance/readback evidence for every bootstrap command, especially `start_game`, before waiting for UDP visibility;
3. guarantee that every aggregate runner command emits exactly one structured result object and add timeout/cleanup contracts that reject incidental PowerShell pipeline output.

It must also retain or copy the exact PID-correlated native, error, and watchdog logs below the run root. These requirements apply to all future battle types because command readiness, result classification, log capture, and cleanup are shared orchestration concerns.

No module restaging is required for the already tested binaries. No further live rerun is authorized by this report; code correction, verification, commit/push, and a fresh live `RunId` each remain separate planned gates.
