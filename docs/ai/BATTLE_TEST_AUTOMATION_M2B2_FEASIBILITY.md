# Battle Test Automation Milestone 2B.2B Live Feasibility Attempts and Runner Findings

Status: **Corrected client resolver live-confirmed; native platform-login source/contracts implemented, staging and live proof pending**

Execution and correction date: **2026-09-01**

Attempt repository revision: `9f4ee733d8e9b83b5fef4f194140daf97a0d89a8`

Clean rerun repository revision: `70a40db6aa71a27e6b1afa0892118ac77871c1a3`

Latest clean validation repository revision: `711f2cac20ca05d3e81233aa6acfa60816dcce99`

Dedicated-control live validation repository revision: `21042956b7928726dacb50c2de258437d5a65c6e`

Installed game-side binary source revision: `12abf36`

This document records the Milestone 2B live feasibility attempts through the first dedicated-control/client-launch run, their exact evidence boundaries, the shared runner defects they exposed, exact recovery/cleanup, and the non-runtime verification between attempts. It does not claim a client connection, campaign startup, cooperative mission, completed battle, L2, or L3 evidence.

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
| Milestone 2B.2C focused runner contracts | `%LOCALAPPDATA%\Temp\CoopSpectator\Automation\m2b2c-runner-focused-20260831-07` |
| Milestone 2B.2C final full contract inventory | `%LOCALAPPDATA%\Temp\CoopSpectator\Automation\m2b2c-full-contracts-20260831-03` |
| Milestone 2B.2C final compile-only verification | `%LOCALAPPDATA%\Temp\CoopSpectator\Automation\m2b2c-compile-only-20260831-02` |
| Milestone 2B.2C first clean live validation | `%LOCALAPPDATA%\Temp\CoopSpectator\Automation\m2b2c-live-feasibility-20260831-01` |
| Published ownership-correction clean live validation | `%LOCALAPPDATA%\Temp\CoopSpectator\Automation\m2b2c-client-handoff-live-20260831-01` |
| Revision 12 full live rerun | `%LOCALAPPDATA%\Temp\CoopSpectator\Automation\m2d-live-r2-01` |
| Exact NetworkMain resolver canonical contracts | `%LOCALAPPDATA%\Temp\CoopSpectator\Automation\m2d-r13-c1` |
| Exact NetworkMain resolver compile-only output | `%LOCALAPPDATA%\Temp\CoopSpectator\CompileOnly\networkmain-resolver-20260901` |

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

## 9. Implemented correction before another live run

Milestone 2B.2C implements all three shared runner corrections without changing either game module:

1. the exact owned dedicated process stdout/stderr is continuously drained and retained; after `ModuleReady`, the runner requires the native `InitialListedGameServerState.OnActivate` readiness message instead of sleeping;
2. each option command requires its exact native `--Changed` readback, while usable-map plus `start_game` acceptance requires the native game-start and selected-scene messages before the UDP deadline begins;
3. both Boolean-returning cleanup waits suppress their output, aggregate dispatch rejects zero/multiple/incomplete results, Windows PowerShell ordered dictionaries are normalized, and primary-versus-terminal outcomes are retained separately.

The correction also copies only `rgl_log_<PID>.txt`, `rgl_log_errors_<PID>.txt`, and `watchdog_log_<PID>.txt` for the exact dedicated identity into the run root and writes a SHA-256 inventory. These controls apply to all future battle types because command readiness, result classification, log capture, and cleanup are shared orchestration concerns.

Focused run `m2b2c-runner-focused-20260831-07` passed in Windows PowerShell 5.1 and PowerShell 7. Final full run `m2b2c-full-contracts-20260831-03` passed 22/22 with matching primary/terminal `Pass`. Final compile-only run `m2b2c-compile-only-20260831-02` passed both project builds and proved installed inventories unchanged. See [BATTLE_TEST_AUTOMATION_M2B2C_RUNNER_CORRECTION.md](BATTLE_TEST_AUTOMATION_M2B2C_RUNNER_CORRECTION.md).

No module restaging was required because no game-side source changed. Revision `e62f536` committed and pushed this correction before the next approved live attempt. Until a later run observes the native redirected-output contract, UDP ownership, client loaded identity, handoff, and `Connected`, none of those facts—and no L2/L3 battle claim—are established.

## 10. Milestone 2B.2C clean live validation

Run `m2b2c-live-feasibility-20260831-01` used clean local/remote revision `e62f536e2d75ac32a60b623260909cc5245bfc5b`, the exact installed hashes, one valid Steam process, free ports, no pre-existing product process, and a fresh run root. The protected result pre-image was SHA-256 `D5EF79D59FA97EF4C95BB7AB31803AE1F475EB24498F4469B83CD3B7AD955AD3`.

Dedicated `Process.Start()` succeeded with PID `113376`. Immediate identity capture then failed because `Get-Process.Path` was null and `Get-CoopProcessIdentity` passed it to `Get-FileHash -LiteralPath`. The runner reported `AssertionFailed` before recording the dedicated identity or creating stdout/stderr capture. The dedicated process later published exact `ModuleReady` for the expected `1A9723...` assembly, proving the launch itself and the game-side runtime profile were valid, but no command, UDP endpoint, client, campaign, or mission followed.

This exposed two related shared-runner defects: process creation was not provisionally owned before fallible identity enrichment, and the runner classified its own post-start identity failure as a product assertion failure. The empty automatic cleanup inventory therefore did not include the live starter or Watchdog. Exact manual recovery validated their PID/path/start/parent chain, gracefully stopped the starter, forcibly stopped only its Watchdog child, and left no product process or port owner. Installed module hashes and the protected result file remained unchanged. See [BATTLE_TEST_AUTOMATION_M2B2C_RUNNER_CORRECTION.md](BATTLE_TEST_AUTOMATION_M2B2C_RUNNER_CORRECTION.md) for the required correction contract.

The aggregate-runner correction was subsequently implemented and published as `8f68d43`: immediate provisional dedicated ownership, bounded exact executable-path resolution with a validated `Win32_Process` fallback, exact launch-window/parent validation, in-place promotion, explicit `RunnerInternalError` classification, and exact cleanup of provisional identities. The client launcher is now hardened against the same boundary: it publishes provisional identity immediately after creation, verifies exact PID/path/start/parent evidence, treats atomic schema-v3 `client-launch.json` publication as the handoff, and cleans the exact client on every earlier exception. Focused contracts passed in Windows PowerShell 5.1 and PowerShell 7; `m2b2c-client-handoff-contracts-20260831-01` passed 22/22 and `m2b2c-client-handoff-compile-20260831-01` passed both builds with installed inventories unchanged. This is non-runtime evidence only; client launch, loaded identity, lobby handoff, and connection remain unverified.

## 11. Published-correction clean live validation

Run `m2b2c-client-handoff-live-20260831-01` used clean local/remote revision `711f2cac20ca05d3e81233aa6acfa60816dcce99`, exact installed `0.3.2` client and dedicated hashes, running Steam, free ports `7210` and `7777`, no pre-existing product process, a fresh run root, and the same protected-result pre-image.

The corrected runner established provisional ownership immediately after creating dedicated PID `153416`, promoted it to a verified identity using exact PID/path/start/parent/launch-window evidence, and retained the expected executable hash and path-evidence source. The dedicated role published exact `ModuleReady`; PID-correlated native evidence subsequently recorded successful TaleWorlds connection/login and repeating successful `AliveMessage` traffic. This proves the staged dedicated module was loaded and remained operational during the bounded wait.

The run terminated with the preserved primary and terminal `Timeout`: `Timed out waiting for captured-text evidence 'NativeConsoleReady'.` No bootstrap command was sent. Redirected `stdout.txt` and `stderr.txt` remained empty. The exact `rgl_log_153416.txt` reached `323574` bytes, began with the engine's native-console startup line, and was associated with an owned `conhost.exe` descendant, but it did not contain the `Console.WriteLine` readiness text. ILSpy reconfirmed that `InitialListedGameServerState.OnActivate` owns that text. The available evidence therefore disproves redirected standard output as the sole observable contract for this starter profile; it does not prove that the lifecycle point itself failed. Standard-input command consumption remains unverified because no command was allowed before readiness evidence.

Automatic cleanup gracefully stopped the exact starter without a forced stop, recorded the Watchdog and console-host descendants as no longer running, retained an empty `RemainingOwnedProcesses` set, and proved runner-lock release. Post-run process and port inspection was clean. The three installed module hashes, protected `battle_result.json` hash/length/timestamp, repository revision, and clean worktree state matched their preflight values.

The client launcher was not invoked, so its live process-handoff behavior remains unverified. No UDP endpoint, client loaded identity, lobby selection, network handoff, connection, campaign, mission, or battle was reached, and the report explicitly retained `CampaignStarted=false`, `CampaignBattleFixtureOpened=false`, and `L2OrL3PassClaimed=false`.

The next gate is not another unchanged retry. It is a separately approved, default-off, run/token/hash/process-bound dedicated readiness, command-intent, and acknowledgement channel. This shared bootstrap precedes all scenario routing, so its blocker applies equally to field, village, siege, sally-out, ambush, relief, lords-hall, hideout, sequential, and reconnect work.

## 12. Source follow-up

[BATTLE_TEST_AUTOMATION_M2B2D_DEDICATED_CONTROL.md](BATTLE_TEST_AUTOMATION_M2B2D_DEDICATED_CONTROL.md) records the source/contracts implementation of that exact channel. The dedicated role now observes the authoritative listed-server lifecycle event, validates a fixed bootstrap request, applies native commands on the main tick, and publishes seven ordered state-backed acknowledgements. Full contracts passed 22/22 and compile-only proved the installed module inventories unchanged.

Controlled run `m2b2d-stage-20260901-01` staged the new published dedicated output with SHA-256 `BD328AAC4F2A64C28D3EDCE28BCE3D72FF164BDAF817D9460B302ED538702A78`, preserved the exact prior tree, and launched no product process. Section 13 records the later live result.

## 13. Dedicated-control and client-launch live validation

Clean run `m2b2d-live-feasibility-20260901-01` used published revision `21042956b7928726dacb50c2de258437d5a65c6e`, the explicit installed client hash `B576B8EA0FB223126A65E062CB562FD15815DF8BA1ADDB1797506914B48D7928`, and the staged dedicated hash `BD328AAC4F2A64C28D3EDCE28BCE3D72FF164BDAF817D9460B302ED538702A78` in both dedicated bins. The exact dedicated role published its loaded hash, authoritative lifecycle readiness, terminal seven-step `BootstrapAccepted`, native `start_game` confirmation for `TeamDeathmatch`/`mp_tdm_map_001`, and exact UDP ownership on port `7210`.

The real multiplayer client launched as the aggregate runner's child, loaded the selected client module, and later published `ModuleReady` plus `WaitingForLobby`. The aggregate stopped earlier in its own control flow because `EntryStartUtc`, already deserialized as a UTC `System.DateTime`, was converted through a culture-dependent string and shifted from `22:00:48Z` to false `19:00:48Z`. That invalid imported identity also prevented automatic cleanup from recognizing the live client. Exact PID/path/parent/start/arguments/run/token/hash evidence proved ownership, and manual postflight closed the client gracefully without force.

Final reporting was independently superseded when native-log capture required `watchdog_log_<dedicated PID>.txt`; this profile produced exact `rgl_log` and `rgl_log_errors` files but no server-PID watchdog file. Revision 12 changes the launcher handoff to schema v4 with a complete verified identity, normalizes dates without stringifying `DateTime`, registers cleanup authority before revalidation, and records an absent optional watchdog log as `NotProduced`. Focused tests passed in Windows PowerShell 5.1 and PowerShell 7, and short-root canonical run `m2d-c-01` passed all 22 projects. The earlier long-root canonical attempt is retained as non-final evidence of the existing Windows path-length limitation; its only failure occurred before one long-named siege test executable could start.

The run changed no installed module, protected result, or repository file. Final postflight found zero product processes and zero owners of ports `7210` and `7777`. Lobby selection, network handoff, connection, campaign, cooperative mission, battle, L2, and L3 remain unverified until a clean published-correction rerun passes.

## 14. Revision 12 live rerun and exact NetworkMain resolver correction

Clean run `m2d-live-r2-01` used local/remote revision `70ff97b2922391f1e7a28afb9dd0da024739dd32`, installed client SHA-256 `B576B8EA0FB223126A65E062CB562FD15815DF8BA1ADDB1797506914B48D7928`, and installed dedicated SHA-256 `BD328AAC4F2A64C28D3EDCE28BCE3D72FF164BDAF817D9460B302ED538702A78`. The dedicated role reached authoritative readiness, accepted all seven ordered bootstrap steps, confirmed `IsPlaying=true` for `TeamDeathmatch`/`mp_tdm_map_001`, and owned UDP port `7210`. The real client published exact `ModuleReady`, and the aggregate accepted schema-v4 PID/path/parent/start/hash evidence.

The run terminated with primary and final `AssertionFailed`: `Client join failed: RequestExpired: The client join request expired before the native join was started.` Automatic cleanup gracefully stopped the exact client and dedicated identities without forced termination, observed support descendants already stopped, and retained an empty `RemainingOwnedProcesses` set. Native dedicated, error, and watchdog logs were captured. Postflight found no product process or owner of ports `7210`/`7777`; the repository remained clean at the same local/remote revision; installed module hashes were unchanged; and protected `battle_result.json` retained SHA-256 `D5EF79D59FA97EF4C95BB7AB31803AE1F475EB24498F4469B83CD3B7AD955AD3`, length `217264`, and timestamp.

The client source log recorded `NetworkMain Initialized` at `01:52:14.945` and `MultiplayerLobbyGauntletScreen::HandleActivate` at `01:52:15.306`, while automation status still reported `NetworkMain.GameClient is not available yet.` Lowest-level ILSpy inspection located `TaleWorlds.MountAndBlade.NetworkMain` in installed `TaleWorlds.MountAndBlade.dll`; it is absent from `TaleWorlds.MountAndBlade.Multiplayer.dll`. The source adapter had selected the latter assembly and then called `Assembly.GetType`, which cannot traverse referenced assemblies. The observed wait was therefore a deterministic source lookup defect, independent of battle type. Client REST-session failures remain possible later environment evidence, but they did not cause this structural lookup failure.

The correction changes only the client source adapter: it selects exact assembly `TaleWorlds.MountAndBlade`, resolves exact type `TaleWorlds.MountAndBlade.NetworkMain`, and distinguishes missing assembly, type, `GameClient` property, and null property value. A dynamic-assembly contract rejects the historical mismatch and accepts only the exact defining assembly. Focused tests passed, canonical run `m2d-r13-c1` passed all 22/22 projects, and isolated client/dedicated builds completed with zero errors. Candidate client SHA-256 is `2437D2386E306C12154553D641B477E123A247938C9C2F944F870BB36F5D6887`; dedicated output remains `BD328AAC4F2A64C28D3EDCE28BCE3D72FF164BDAF817D9460B302ED538702A78`. The installed binaries were not changed. Runtime revalidation remains pending until the corrected client is reviewed, published, staged with a retained pre-image, and exercised by a new clean `Feasibility` run.

## 15. Published resolver staging and native platform-login finding

### 15.1 Fresh published build and artifact selection

Clean published revision `729fd2c325019ca026787b4ffcccf91beeced755` was compiled under run `m2d-r13-pub-b1`. Both client and dedicated builds passed with installed inventories unchanged. The fresh client DLL SHA-256 was `1C500501CE25D4A520782F61B338F9D8D0A4C591A4748E80991A521B63379250`; the PDB SHA-256 was `8A63F4F566C8AC650BE923269F885CCD0B9B659661FDC132FEF680967AEF10F0`; and the dedicated DLL remained `BD328AAC4F2A64C28D3EDCE28BCE3D72FF164BDAF817D9460B302ED538702A78`.

The fresh DLL differed from the earlier `2437...6887` candidate despite identical source revision. Diagnostic root `%LOCALAPPDATA%\Temp\CoopSpectator\Diagnostics\m2d-r13-binary-compare-01` records equal file size, 173 differing bytes, and 620/620 identical ILSpy-decompiled C# files. The only decompiled-project difference was generated `HintPath` metadata containing the isolated build/PDB path. Because the published build was the freshest complete evidence chain, `1C500...79250` was selected for staging rather than assuming byte-for-byte reproducibility.

### 15.2 Corrected-client staging transaction

Preparation `m2d-client-stage-r1` failed before installation because the PowerShell comparison expression did not normalize an empty result to an array. The first `m2d-client-stage-r2` apply preflight likewise failed before lock acquisition or file movement because a helper had `return[ordered]` without required whitespace. Both failures are retained as non-mutating preparation evidence.

The corrected `m2d-client-stage-r2` transaction then moved the entire installed 32-file client tree to `%LOCALAPPDATA%\Temp\CoopSpectator\Automation\m2d-client-stage-r2\backup\client\CoopSpectator` and installed the prepared tree. Only the DLL/PDB pair changed. The previous tree fingerprint was `A53B10EA8413A0AFCDAE90035115F72F6ED6DAD71B5D5A953563A0CDC51597F5`; the prepared and installed fingerprint was `14DF5E30B7838D72E0F69F70A31C7BE6CF8FF13767A33F48A1C69A539D6A0AF0`. Dedicated inventories and hashes remained unchanged. Protected `battle_result.json` retained SHA-256 `D5EF79D59FA97EF4C95BB7AB31803AE1F475EB24498F4469B83CD3B7AD955AD3`, length `217264`, and timestamp. Validate-only run `m2d-client-stage-validate-r1` passed without product launch.

### 15.3 Clean live result

Run `m2d-live-r3-01` loaded the exact staged client SHA-256 `1C500...79250` and dedicated SHA-256 `BD328...02A78`. The dedicated role reached authoritative readiness, accepted all seven ordered bootstrap steps, confirmed native `start_game`, `TeamDeathmatch` on `mp_tdm_map_001`, and exact UDP port `7210` ownership. The aggregate accepted exact client PID `159536`, process identity, schema-v4 handoff, and loaded hash.

The corrected adapter resolved `NetworkMain.GameClient` and advanced to `WaitingForLobby` with `LobbyState=Idle`. This closes the wrong-assembly resolver finding. The client did not reach `AtLobby`, request a server list, select the local server, or start a native join. It expired with terminal client status `Failed` / `RequestExpired`: `The client join request expired before the native join was started.`

The user-observed exact-PID screenshot at `artifacts/screenshots/user-observed-client-not-logged-in.png` shows the stock `Not Logged In` screen. Its SHA-256 is `7EBD0BFE09137CF481F594E9D7460EA33E21931A5F270D9353C88138F187B8E8`. Exact client-PID native logs retained under `artifacts/logs/client/native` record automation `ModuleReady`, `WaitingForLobby` / `Idle`, `NetworkMain Initialized`, `MultiplayerLobbyGauntletScreen::HandleActivate`, and two native REST requests that failed before assignment. The screenshot and logs correlate the timeout with the stock platform-login boundary rather than server discovery, battle routing, or the corrected resolver.

Automatic cleanup gracefully stopped exact client PID `159536` and dedicated PID `159040` without force. Postflight found no owned product process and no owner of ports `7210` or `7777`; local and remote repository revision stayed `729fd2c325019ca026787b4ffcccf91beeced755`; installed hashes and the protected result remained unchanged.

### 15.4 Lowest-level login boundary

ILSpy inspection of the installed game assemblies located exact view-model type `TaleWorlds.MountAndBlade.Multiplayer.ViewModelCollection.Lobby.Authentication.MPAuthenticationVM`. Its stock `ExecuteLogin()` delegates to `LobbyState.TryLogin()`. The installed public `TaleWorlds.MountAndBlade.LobbyState.TryLogin()` method sets the logging state, performs multiplayer/crossplay/user-generated-content privilege checks, obtains the platform `CreateLobbyClientLoginProvider()`, and calls the native lobby client's connection path when the game client is `Idle`. No module-owned password or supported direct-connect bypass is involved.

The source implementation therefore remains inside the complete default-off automation profile and invokes at most one native `TryLogin()` attempt from the main application tick. It requires the exact active `LobbyState`, reference equality between that state's `LobbyClient` and the resolved `NetworkMain.GameClient`, `Idle`, and `IsLoggingIn=false`; retains the returned task; and publishes explicit waiting, success, fault, cancellation, privilege-denied, and still-idle evidence. UI clicking, credential storage, authentication bypass, and blind retries remain out of scope.

The official TaleWorlds API site confirms that Bannerlord exposes versioned modding API documentation, but no indexed page for this exact current `LobbyState.TryLogin()` implementation was found. The installed `1.4.8` DLL is therefore the authoritative source for this runtime-specific boundary. The native login gate is shared by all later battle scenarios; this run provides no campaign, cooperative mission, L2, or L3 evidence.

Two runner evidence gaps were identified with the login change: `feasibility.json` left `ClientJoinStatus=null` when the status wait threw even though `state/client-join.status.json` contained the terminal failure, and client PID-native logs required manual post-cleanup collection. The source/contract follow-up below closes both gaps before another live run.

### 15.5 Native platform-login source and contract follow-up

Revision 13 source resolves the exact installed `TaleWorlds.MountAndBlade.LobbyState`, follows `Game.Current.GameStateManager.ActiveState`, requires exact state identity and reference equality with the resolved `NetworkMain.GameClient`, and invokes public parameterless `TryLogin()` only from the main application tick while the client is `Idle`, no login is active, and privilege is not known denied. The controller retains one returned task, blocks repeat attempts and false cancellation, and publishes distinct requesting, waiting, success, fault, cancellation, privilege-denied, and still-idle evidence through join-request schema 3. It adds no UI input, credentials, privilege bypass, or generic authentication surface.

The aggregate runner now attaches the final client status to the failure exception, re-reads and validates the run/token-bound terminal status during cleanup when needed, preserves it in `feasibility.json`, and captures separate exact-PID native-log inventories for the dedicated and client roles. The historical `NativeLogInventory` field remains as the dedicated-role compatibility alias.

Focused join contracts prove exact assembly/type/state/client identity, single invocation, denial and already-logging no-retry behavior, non-lobby rejection, client mismatch rejection, and distinct controller outcomes. Focused runner contracts prove terminal status propagation and distinct client/dedicated PID-log routing in Windows PowerShell 5.1 and PowerShell 7. Final canonical run `m2e-contracts-20260901-03` passed 22/22 after the last test update. Final compile-only run `m2e-compile-20260901-02` built both projects after the last source-text update without changing installed inventories or launching a product process; it produced client candidate SHA-256 `6DE0B8AE1F38626FD11B7BDE59E86AD7AA106CD7E96596C7FECB2E137C4E8551` and unchanged dedicated output SHA-256 `BD328AAC4F2A64C28D3EDCE28BCE3D72FF164BDAF817D9460B302ED538702A78`.

This closes only the source, contract, compile, and runner-evidence portions of the gate. The candidate is not installed. No new client, dedicated, campaign, mission, or battle process was launched, and installed modules plus the protected result remain unchanged. Publication, transactional client-only staging, and a clean bounded live feasibility run remain separately controlled next steps.
