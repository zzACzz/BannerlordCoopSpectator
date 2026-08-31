# Build, Test, and Debug Guide

Last source verification: **2026-08-28**
Last automation-control verification: **2026-08-31**

## Safety first: builds deploy

The main projects are not compile-only by default.

### Client project side effects

`CoopSpectator.csproj` defines `DeployModToGame` after `Build`. A normal build can:

- remove the installed legacy `Modules\CoopSpectatorMP` directory;
- copy the client module descriptor, DLLs, XML data, and GUI assets into the installed Bannerlord tree;
- copy a multiplayer dependency when found;
- invoke `BuildAndDeployDedicatedModule` by default because `BuildDedicatedServerModule=true`.

### Dedicated project side effects

`DedicatedServer/CoopSpectatorDedicated.csproj` defines build/deployment targets that can:

- replace the output Harmony runtime asset;
- stage `SandBox.dll` and `TaleWorlds.CampaignSystem.dll` from the client installation;
- create/update the installed `Modules\CoopSpectatorDedicated` tree;
- copy server/client binaries and module XML data;
- copy SandBox/SandBoxCore descriptors and XML data into the dedicated installation;
- copy `battle_terrain_*` and supported hideout scene assets;
- copy `TaleWorlds.MountAndBlade.Multiplayer.dll` into the dedicated client bin.

Therefore:

1. Present a plan and obtain explicit approval before any build.
2. Confirm the exact `BannerlordRootDir` and `DedicatedServerRootDir` first.
3. Stop and report if installed directories are absent or point at an unexpected game version.
4. Treat a build as an external deployment operation, not a read-only validation step.
5. Restart a running dedicated process after deployment before trusting runtime results.

The repository does not currently define a documented, verified property that disables all client and dedicated deploy targets. `BuildDedicatedServerModule=false` prevents the chained dedicated build, but it does not disable client deployment.

### Known write-surface matrix

| Invocation | Compiles | Repository writes | Installed client writes | Installed dedicated writes |
|---|---:|---:|---:|---:|
| Normal `CoopSpectator.csproj` build | Yes | `bin/`, `obj/` | Yes | Yes by default through the chained dedicated build |
| Client build with `BuildDedicatedServerModule=false` | Yes | `bin/`, `obj/` | Yes | No chained dedicated build |
| `CoopSpectatorDedicated.csproj` build | Yes | dedicated `bin/`, `obj/`, staged output | No writes expected; reads client assets | Yes |
| Contract-test `dotnet run` | Yes | test `bin/`, `obj/`; possible test-specific temporary output | No expected module deployment | No expected module deployment |
| `scripts/CoopDevLoop.ps1` with no action switches | Yes | build outputs | Yes | Yes |
| `scripts/CreateReleasePackage.ps1` without `-SkipBuild` | Yes | build outputs and recreated `dist/` packages | Yes through project targets | Yes through project targets |
| `scripts/CreateReleasePackage.ps1 -SkipBuild -GitHubAssetsOnly` | No | recreates GitHub client/host archives and temporary staging under `dist/releases/` | No | No |
| `scripts/CreateReleasePackage.ps1 -SkipBuild -NexusAssetsOnly` | No | recreates validated Nexus client/HostLite archives under `dist/releases/<version>/Nexus/` | No | No |
| `scripts/CreateReleasePackage.ps1 -SkipBuild -ReleaseAssetsOnly` | No | recreates both GitHub and Nexus archive sets under `dist/releases/<version>/` | No | No |
| `scripts/Test-RepositoryHygiene.ps1` | No | no content/output writes; Git may refresh index metadata | No | No |
| `scripts/Invoke-CoopTest.ps1 -Command Doctor|Contracts|CompileOnly` | `Contracts`/`CompileOnly` only | Writes only the selected temporary automation run root; compile-only outputs remain below it | No | No |
| `scripts/Invoke-CoopTest.ps1 -Command Feasibility` | No | Writes only the selected temporary automation run root; product runtimes may write normal external logs/configuration | No module writes | No module writes |
| `scripts/Invoke-CoopTest.ps1 -Command Inspect|Recover` without `-ApplyRecovery` | No | Read-only for the existing run | No | No |
| `scripts/Start-CoopBattleTestClient.ps1 -ValidateOnly` | No | No expected repository or run-root writes | No | No |
| `run_battle_test_client.bat` / standalone live `Start-CoopBattleTestClient.ps1` | No | Current source fails before run-root creation; standalone mode cannot prove dedicated ownership | No | No |
| `Start-CoopBattleTestClient.ps1 -UseExistingRunContract` | No | Writes only the aggregate runner's selected temporary automation run root; Bannerlord writes its normal external logs/configuration | No | No |

This table records current project/script behavior, not a guarantee that an arbitrary command is safe. Every approved plan containing one of these operations must still state the exact command and resolved destinations.

## Build profiles

### Client/campaign

- Project: `CoopSpectator.csproj`
- Framework: `.NET Framework 4.7.2`
- Platform: `x64`
- Output: `Module\CoopSpectator\bin\Win64_Shipping_Client`
- Package: `Lib.Harmony 2.4.2`
- Game references: installed client DLLs when present, otherwise selected local DLL fallbacks.
- `GameMode/` is excluded by default and re-included with `HAS_GAMEMODE` only if a compatible multiplayer assembly is found.
- `DedicatedServer/`, `Tests/`, build outputs, and temporary decompilation trees are excluded.

### Dedicated

- Project: `DedicatedServer\CoopSpectatorDedicated.csproj`
- Framework: `.NET Framework 4.7.2`
- Platform: `x64`
- Constant: `COOPSPECTATOR_DEDICATED`
- Assembly name: `CoopSpectator`
- Output: dedicated module server bin, copied to server and client bin locations.
- Source graph: explicit linked list of shared game modes, mission behaviors, network messages, infrastructure, models, and patches.
- Recommended reference profile: `UseDedicatedServerRefs=true`.

Never resolve a compile failure by copying arbitrary client DLLs into the dedicated reference set or the reverse. Compile-time/runtime assembly drift can produce successful builds followed by native startup failures, missing methods, or Harmony target mismatches.

## Version caveat

Current project metadata is inconsistent:

- a client project comment recommends Bannerlord `1.3.14` references;
- the dedicated project explicitly errors when required Bannerlord `1.4.8` SandBox scene-script runtime files are missing;
- dated reverse-engineering reports also record Bannerlord `1.4.8` evidence.

Do not “fix” the comment or declare support from this documentation alone. Before a release or low-level compatibility change, capture:

- installed game and dedicated server versions;
- file versions and hashes of relevant TaleWorlds assemblies;
- the actual reference paths printed by the build;
- the runtime module/DLL paths in client and server logs;
- a scenario smoke-test matrix for that exact pair.

## Approved build command patterns

Run only after explicit approval and after substituting the active worktree/install paths.

### Client plus default chained dedicated deployment

```powershell
dotnet build .\CoopSpectator.csproj -c Debug `
  /p:BannerlordRootDir="C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord" `
  /p:DedicatedServerRootDir="C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Dedicated Server"
```

### Client only, still deploying the client module

```powershell
dotnet build .\CoopSpectator.csproj -c Debug `
  /p:BuildDedicatedServerModule=false `
  /p:BannerlordRootDir="C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord"
```

### Dedicated with dedicated references

```powershell
dotnet build .\DedicatedServer\CoopSpectatorDedicated.csproj -c Debug `
  /p:UseDedicatedServerRefs=true `
  /p:BannerlordRootDir="C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord" `
  /p:DedicatedServerRootDir="C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Dedicated Server"
```

`BUILD_RUNBOOK.md` contains shorter historical commands, but its dedicated root example omits the current default Steam segment. Prefer explicit paths verified on the current machine.

### Side-effect-free compile-only mode

`Directory.Build.props` and both main project files support `CoopCompileOnly=true`. The mode defaults to false, so the historical developer deployment workflow above remains unchanged. When true, the caller must provide an absolute `CoopCompileOutputRoot`; output, intermediate files, project extensions, and restored packages are redirected below that root, while `DeployModToGame`, `BuildAndDeployDedicatedModule`, and `DeployServerToDedicated` are disabled.

Prefer the reviewed runner instead of calling the projects manually:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-CoopTest.ps1 `
  -Command CompileOnly `
  -RunId m2a-local-compile-01
```

The runner writes below `%TEMP%\CoopSpectator\Automation\<RunId>`, recursively inventories the installed client, legacy-client, and dedicated module trees before and after, compiles the client and dedicated projects independently, and fails if any installed inventory changes. It does not stage or load the resulting DLLs.

The authoritative 2026-08-31 proof is `m2a-compile-only-20260831-03`; both version `0.3.2` outputs compiled, all four assertions passed, and the before/after installed-inventory JSON files shared SHA-256 `9A467236DE7B7FACF18B8C54B947EE8CACEA2D2D2C3B754C3EA3510BE37010CA`.

## Contract tests

Contract tests are standalone `net8.0` executables. They are the safest automated validation layer because most link narrow pure production contracts rather than loading Bannerlord.

Run one project:

```powershell
dotnet run --project .\Tests\CoopBattleStartup.ContractTests\CoopBattleStartup.ContractTests.csproj -c Release
```

Run all contract-test projects through the canonical inventory:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-CoopTest.ps1 `
  -Command Contracts `
  -RunId m2a-local-contracts-01 `
  -All
```

The runner requires `Tests/contract-tests.manifest.json` to equal the discovered project inventory, continues after an isolated project failure, and writes structured JSON/Markdown results plus combined, stdout, and stderr logs for every project.

The older ad-hoc equivalent is retained only for diagnosis:

```powershell
Get-ChildItem .\Tests -Recurse -Filter *.csproj |
  Sort-Object FullName |
  ForEach-Object {
    dotnet run --project $_.FullName -c Release
    if ($LASTEXITCODE -ne 0) { throw "Contract test failed: $($_.FullName)" }
  }
```

The 20 current projects cover:

- campaignless conversation safety;
- battle power/HUD math;
- result campaign guard and stable read cache;
- battle startup, commander, mount, equipment, and relief contracts;
- campaign map prototype codecs and bounds;
- dedicated siege hero-class/perk safety;
- ladder/merlon visual parity and ladder interaction;
- hero battle progression and hero creation;
- hideout boss/ambush rules;
- remote siege occlusion safety;
- shader-cache mode-switch script behavior;
- siege formation membership safety;
- SandBox scene-script registration;
- native aftermath/casualty aggregation;
- run-scoped client join request/server-selection/status behavior;
- general automation protocol, outcome, lease/recovery, known-issue, and file-fault contracts;
- compile-only project guards.

Test interpretation:

- Passing contract tests prove deterministic contract behavior at the linked source revision.
- They do not load native mission scenes, exercise Harmony targets, transmit real Bannerlord messages, or validate client visuals.
- A new native/runtime fix should add a pure contract test when its decision can be extracted without native state, then still receive the appropriate real mission run.

No tests were run during the 2026-08-28 documentation pass.

The authoritative Milestone 2A full run `m2a-contracts-20260831-07` passed all 20 projects and emitted 20 passing assertion records without launching a product process. See [BATTLE_TEST_AUTOMATION_M2A_IMPLEMENTATION.md](BATTLE_TEST_AUTOMATION_M2A_IMPLEMENTATION.md).

Focused validation recorded on 2026-08-29:

- `CampaignlessConversationMissionSafety.ContractTests` passed for checkpoint `9c939c5`;
- `CoopRemoteSiegeOcclusionSafety.ContractTests` passed for checkpoint `fcc8920`;
- no client/dedicated module build, deployment, native mission load, two-machine run, or visual validation was performed for those checkpoints.

## Repository and line-ending hygiene

Source-verified on 2026-08-29.

The canonical repository policy is:

- `.gitattributes` stores text as LF;
- `.bat` and `.cmd` are the only CRLF text exceptions;
- known DLL, executable, symbol, dump, archive, and image formats are explicitly binary;
- `.editorconfig` mirrors the same editor-facing line-ending policy without forcing a repository-wide encoding rewrite;
- repository-local Git configuration is `core.autocrlf=false`, `core.eol=lf`, and `core.safecrlf=true`.

Validate before a commit while approved changes are still present:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-RepositoryHygiene.ps1 -AllowDirty
```

Validate again after the commit:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-RepositoryHygiene.ps1
```

The script checks the repository-local Git configuration, index/worktree EOL metadata reported by `git ls-files --eol`, declared LF/CRLF policy, mixed endings, and final working-tree cleanliness. It treats files classified by Git as binary/non-text as outside text normalization.

`.gitignore` excludes repository-local build checks, `work/`, the known accidental PowerShell host directory, generated root/`dist` ZIP files, and unpacked `dist/BannerlordCoopCampaign_v*/` packages. Tracked release artifacts remain tracked; ignore rules do not hide later modifications to them.

Line-ending policy changes must remain isolated from production fixes. If normalization produces a large content diff rather than metadata-only refresh, stop before commit and inspect the affected paths and encodings.

## Development helper scripts

### `scripts/Invoke-CoopTest.ps1`

Milestone 2A commands are non-runtime only:

```powershell
# Named L0 environment report. Exit 10 is expected while the runtime matrix/hashes are blocked.
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-CoopTest.ps1 -Command Doctor -RunId doctor-01

# Full canonical L1 inventory.
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-CoopTest.ps1 -Command Contracts -RunId contracts-01 -All

# Independent client/dedicated L1 builds below the run root, with installed-tree proof.
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-CoopTest.ps1 -Command CompileOnly -RunId compile-01
```

`RunId` roots are fresh-only. Do not delete or reuse a failed root implicitly. The manifest stores only a nonce fingerprint; non-pass runs add a redacted reproduction descriptor with a distinct proposed retry ID. `Doctor` accepts `-MachineProfileName` or `COOPSPECTATOR_MACHINE_PROFILE`; an omitted value becomes `LOCAL-<machine>-UNVERIFIED` and must not be presented as a supported runtime matrix.

### `scripts/CoopDevLoop.ps1`

Actions:

- `-BuildClient`
- `-BuildDedicated`
- `-RestartDedicated`
- `-LaunchClient`
- `-RestartClient`
- `-CheckLogs`
- `-UseRelease`

With no action switches it builds client and dedicated, then checks logs. It can forcibly stop running Bannerlord/dedicated processes when restart switches are used.

Critical path caveat: its default `ProjectRoot` is `C:\dev\projects\BannerlordCoopSpectator3`, which may not be the active Codex worktree. Always pass the resolved worktree explicitly:

```powershell
$projectRoot = (Resolve-Path .).Path
powershell -ExecutionPolicy Bypass -File .\scripts\CoopDevLoop.ps1 `
  -ProjectRoot $projectRoot `
  -BuildClient -BuildDedicated -CheckLogs
```

This example still builds/deploys and requires approval.

The script's default log markers focus on native agent visuals, spawn ownership, and controlled-agent handoff. Update the marker list when the current investigation needs different evidence; do not infer general health from those five markers alone.

### `run_battle_test_client.bat` and `scripts/Start-CoopBattleTestClient.ps1`

These files implement the initial default-off, run-scoped multiplayer-client validation and normal-lobby join intent. They do not build, deploy, run the shader-cache helper, issue `start_game`, or automate UI. Standalone `-ValidateOnly` remains supported. A live launch requires `-UseExistingRunContract` and inherits the aggregate runner's existing token, root, expected hash, `Suppress` result policy, and exact owned-server evidence.

The historical short entry point is:

```text
run_battle_test_client.bat <RunId> "<ExactServerName>" <ExpectedInstalledClientSha256> [Port]
```

Current source intentionally rejects its live launch after validation because the wrapper has no exact dedicated-process contract. Use `scripts/Invoke-CoopTest.ps1 -Command Feasibility` for a separately approved live connectivity probe.

The live launcher requires all of the following before process creation:

- Steam is already running in the current interactive user session;
- the Bannerlord executable, `SubModule.xml`, installed `CoopSpectator.dll`, and bundled `0Harmony.dll` exist;
- the installed client module's SHA-256 exactly equals the command's expected hash;
- the `RunId` is fresh and has no existing request or status under `%TEMP%\CoopSpectator\Automation\<RunId>`.

After launch and before the native join request, the automation profile requires `state/dedicated-host.json` to match the `RunId`, token hash, server name/port, and exact still-live dedicated PID/path/start time, and requires that UDP port to be active. Automation neither trusts nor mutates the production persisted local-host marker.

The optional server password is deliberately not a command-line parameter. Set it only in the current shell before launch:

```powershell
$env:COOPSPECTATOR_AUTOMATION_SERVER_PASSWORD = '<password>'
```

or in `cmd.exe`:

```text
set COOPSPECTATOR_AUTOMATION_SERVER_PASSWORD=<password>
```

The launcher passes the secret only through the child environment. Request, status, launch artifact, and logs record only whether it was supplied. Clear the shell variable after the run.

For a non-launching environment/hash check, call the PowerShell core directly:

```powershell
pwsh -NoProfile -File .\scripts\Start-CoopBattleTestClient.ps1 `
  -RunId M2B-validate-only `
  -ServerName AC_COOP `
  -ExpectedClientModuleSha256 <64-hex-sha256> `
  -ValidateOnly
```

`-ValidateOnly` does not create the run root or start Bannerlord. A live run writes:

- `commands/client-join.request.json` — token-hash-, RunId-, command-, lifetime-, module-hash-, and server-bound intent;
- `state/client-join.status.json` — strictly atomic module acknowledgement;
- `artifacts/processes/client-launch.provisional.json` — immediate PID/path/parent/launch-window ownership before fallible enrichment;
- `artifacts/processes/client-launch.json` — verified schema-v3 PID/path/parent/start identity and atomic ownership handoff to the aggregate runner;
- `artifacts/processes/client-launch.cleanup.json` — best-effort primary-error and exact-cleanup evidence when handoff fails before final publication;
- `work/client-launch.lock` — exclusive launcher ownership for the selected `RunId`.

The module status distinguishes readiness, lobby/server wait, native join request/acceptance, network handoff, connection, failure, and safe pre-join cancellation. `coop.automation_join status` reads the in-process summary; `coop.automation_join cancel` refuses to label an already started native join as cancelled.

Current on-disk boundary: controlled run `m2b2-stage-20260831-02` installed the exact `0.3.2` client and dedicated binaries compiled from clean pushed revision `12abf36`. The client hash is `B576B8EA0FB223126A65E062CB562FD15815DF8BA1ADDB1797506914B48D7928`; both dedicated bins use `1A9723A3249582FABCF08D3778C10CF448944B928549E6D9582FA7F3C0770626`. Complete immediately preceding `0.3.2` installation trees remain under that run root, and the historical `m2b-install-20260831-01` root still retains the earlier `0.3.1` pre-images. Post-stage doctor run `m2b2-poststage-doctor-20260831-01` reports only `RuntimeVersionCombinationNotYetVerified`.

The staging transaction itself established `ConfirmedPathHashOnly`. All three live attempts later established `ConfirmedLoadedHash` for the dedicated module only. Clean-revision rerun `m2b2-live-feasibility-rerun-20260831-01` used PID `123600`, exact hash `1A9723A3249582FABCF08D3778C10CF448944B928549E6D9582FA7F3C0770626`, and `ResultPolicy=Suppress`. It emitted six separate commands but did not reach native command acceptance or server visibility, so the client was not launched and its loaded hash, correlated handoff, and connection remain unverified. Do not use this as L2/L3 battle evidence. See [BATTLE_TEST_AUTOMATION_M2B2_FEASIBILITY.md](BATTLE_TEST_AUTOMATION_M2B2_FEASIBILITY.md).

### `scripts/Invoke-CoopTest.ps1` Milestone 2B.1 controls

`Feasibility` requires a fresh `RunId`, exact installed client/dedicated hashes, Steam, no pre-existing Bannerlord/dedicated/crash-reporter process, and an unowned requested UDP port. It enables fail-closed `Suppress`, launches the exact dedicated role, requires its loaded hash, issues the minimum vanilla `TeamDeathmatch`/`start_game` bootstrap required for native server visibility, verifies the exact UDP owner, launches the client through `-UseExistingRunContract`, requires `Connected`, stops only recorded exact process identities, and proves the global `battle_result.json` did not change. This command is connectivity evidence only, never L2/L3 battle evidence.

The first live attempt (`m2b2-live-feasibility-20260831-01`) reached exact dedicated-module acknowledgement but emitted all six bootstrap commands as one concatenated console line. No UDP listener appeared, no client or campaign was launched, and no cooperative mission began. The runner then exceeded its intended bound while repeatedly querying process ancestry. `Inspect` and `Recover -ApplyRecovery` found exactly the recorded dedicated identity and stopped it gracefully without a forced termination; ports `7210` and `7777` were free afterward and the protected global result hash remained unchanged.

The corrected runner obtains six explicit commands from `scripts/CoopAutomationRunner.Core.ps1`. Descendant registration now takes one CIM process snapshot with a 10-second operation timeout, traverses that snapshot in memory with cycle and duplicate protection, caps discovery at 256 descendants, and caps registration at 30 seconds. A discovery failure is recorded as `RunnerInternalError`, but exact root cleanup still runs. `artifacts/processes/runtime-process-tree-snapshot.json` preserves the bounded discovery evidence.

`Tests/CoopAutomationRunner.ContractTests` exercises the exact six-command sequence in Windows PowerShell 5.1 and PowerShell 7, alternate map/game values, nested and unrelated process branches, cycles, duplicate PIDs, empty input, and the descendant cap. Run `m2b2-fix-final-contracts-20260831-01` passed the complete 22/22 inventory. Compile-only run `m2b2-fix-compile-only-20260831-01` passed with installed module inventories unchanged. The client PE hash differed because of dirty-tree debug metadata, but ILSpy decompilation produced identical IL for that output and the staged clean client DLL. No production game hot path was changed.

Clean committed rerun `m2b2-live-feasibility-rerun-20260831-01` confirmed the discrete commands and completed one 316-process descendant snapshot in 808 ms, but timed out waiting for UDP port `7210`. Native log timing proves the first command was written at `10:04:23.414`, before engine initialization completed, login began at `10:04:25.094`, login succeeded at `10:04:25.525`, and the first alive response arrived at `10:04:27.377`. `ModuleReady` is written from `OnSubModuleLoad` and must no longer be treated as native console readiness. Require a distinct authoritative readiness acknowledgement and per-command acceptance/readback; do not add a fixed sleep.

The run-specific feasibility report preserved the correct `Timeout`, while the final manifest incorrectly became `RunnerInternalError` because `Process.WaitForExit(int)` leaked a Boolean into the aggregate PowerShell result pipeline. Milestone 2B.2C suppresses both Boolean-returning waits, rejects zero/multiple/incomplete aggregate results, normalizes Windows PowerShell ordered dictionaries, and records `PrimaryOutcome` separately from a legitimately superseding terminal result. It also copies only `rgl_log_<PID>.txt`, `rgl_log_errors_<PID>.txt`, and `watchdog_log_<PID>.txt` from `C:\ProgramData\Mount and Blade II Bannerlord\logs` into the run root with a SHA-256 inventory.

The corrected `Feasibility` process redirects and continuously drains dedicated stdout/stderr. After loaded-hash confirmation it requires the exact native `InitialListedGameServerState.OnActivate` ready message. The four option commands each require their exact native `--Changed` readback; usable-map plus `start_game` require native game-start and selected-scene readbacks before the UDP deadline begins. Fixed startup sleeps are forbidden. Focused run `m2b2c-runner-focused-20260831-07` passed under Windows PowerShell 5.1 and PowerShell 7; final full run `m2b2c-full-contracts-20260831-03` passed 22/22.

Clean live run `m2b2c-live-feasibility-20260831-01` from pushed revision `e62f536` successfully created dedicated PID `113376`, but its immediate `Get-Process.Path` was null. `Get-CoopProcessIdentity` passed the null path to `Get-FileHash -LiteralPath`, so the run became `AssertionFailed` before provisional ownership, stdout/stderr capture, or the process-start event. The role later published exact `ModuleReady`, proving the launch and loaded module were valid. The empty generated cleanup inventory did not contain the still-live starter/Watchdog; exact manual recovery validated their identity/parent chain, gracefully stopped the starter, forcibly stopped only Watchdog, and left all ports, installed hashes, and the protected result unchanged. Treat this as `RunnerInternalError`, never a product assertion or battle failure.

The post-live aggregate correction now records a provisional launch identity before any path/start/hash enrichment. Bounded identity resolution accepts a validated `Win32_Process.ExecutablePath` fallback when `Process.Path` is null, requires the exact requested path and launch-time/parent evidence, promotes the inventory entry in place, and classifies enrichment failure as `RunnerInternalError` while exact cleanup still runs. Focused tests passed in Windows PowerShell 5.1 and PowerShell 7, final run `m2b2c-r10-contracts-20260831-02` passed 22/22, and final run `m2b2c-r10-compile-20260831-02` built both modules with installed inventories unchanged and no product process launch.

`Inspect` renders an existing run without mutation. `Recover` also remains read-only unless `-ApplyRecovery` is supplied; the apply form acquires the abandoned run lock and revalidates each recorded PID, executable path, and start time immediately before stopping it. It does not delete the run root.

The current `0.3.2` binaries include the game-side runtime foundation and remain bound to clean pushed revision `12abf36` by the Milestone 2B.2A staging record. Milestone 2B.2C changes no game-side source, so restaging is not required. Aggregate correction `8f68d43` is published. The client launcher now records provisional ownership immediately after `Process.Start`, performs bounded exact path/start/parent enrichment, publishes the final artifact as the ownership handoff, and cleans the exact client on every earlier exception. Focused cross-PowerShell tests, `m2b2c-client-handoff-contracts-20260831-01` (22/22), and `m2b2c-client-handoff-compile-20260831-01` passed without product launch or installed-inventory change. Review, commit, and push this client correction before another live run. Do not proceed to campaign automation first.

### `scripts/CreateReleasePackage.ps1`

Unless `-SkipBuild` is specified, it performs client and dedicated Release builds. It then deletes/recreates only the `dist/` directories and ZIP files selected by the active mode.

Asset-only modes:

- `-GitHubAssetsOnly`: creates the full GitHub client and host archives.
- `-NexusAssetsOnly`: requires the matching GitHub archives to exist, then derives client and HostLite Nexus archives, embeds both localized README/CHANGELOG pairs, removes BAT/PS1/PDB files, and validates retained entries by path, length, and SHA-256.
- `-ReleaseAssetsOnly`: creates the GitHub archives first and then the derived Nexus archives in one run.

`-LightOnly` remains the legacy light-package mode. The canonical artifact names, root layouts, exclusions, validation rules, and publication boundaries are documented in [RELEASE_PACKAGING.md](RELEASE_PACKAGING.md).

This script is destructive inside `dist/` and writes release packages. Verify exact targets before execution and never use it as a compile smoke test.

### `scripts/DllInventoryAudit.ps1`

Scans installed client/dedicated trees and writes a report plus CSV. It is useful for assembly availability and version drift, but it mutates repository report artifacts and its embedded historical conclusion may be stale. Plan its outputs before running it.

## Runtime logs and artifacts

### Main game logs

The module writes through `TaleWorlds.Library.Debug.Print` with prefix:

```text
[CoopSpectator] LEVEL: message
```

Common current locations from the development script:

- client: `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_*.txt`;
- dedicated: `%LOCALAPPDATA%\Temp\CoopSpectatorDedicated_logs\logs\rgl_log_*.txt`.

Actual locations can change with launcher/runtime configuration. Prefer the latest non-error `rgl_log_*.txt` belonging to the process under investigation, and correlate process ID/time with the runtime bundle.

### Milestone 1 verified launch profile

The bounded `M1-20260831T013501Z-4114d2df` feasibility run on 2026-08-31 established an installed-runtime profile without building, deploying, opening a mission, or connecting a client. The canonical report is [BATTLE_TEST_AUTOMATION_M1_FEASIBILITY.md](BATTLE_TEST_AUTOMATION_M1_FEASIBILITY.md).

Verified launch facts for that named profile:

- the dedicated module loaded in `DedicatedCustomServer.Starter.exe` when launched directly with the explicit module list, port, and run-owned log directory;
- the Starter process remained the role process; `Watchdog.exe` and `conhost.exe` were owned descendants rather than a replacement dedicated child;
- the dedicated stock token-file authentication flow worked without passing token content on the command line;
- omitting the startup configuration and `start_game` allowed module/authentication feasibility to be tested without opening a mission, but port `7210` did not become a listening game port;
- direct multiplayer client launch exited before module load when Steam was unavailable and succeeded after Steam was running in the same interactive session;
- the client shader-cache helper and UI automation were not used;
- both roles closed gracefully by exact PID/path/start-time ownership and required no forced termination.

For that M1 run, the loaded modules were installed version `0.3.1`, while the repository outputs were `0.3.2` with different hashes. Those commands are therefore diagnostic evidence for that historical installed profile only. The developer separately reports that `0.3.2` was released after stable manual battle runs; the M1 mismatch limits only what that specific automated probe proves and does not classify `0.3.2` as unstable. The later `m2b-install-20260831-01` operation first closed the `0.3.2` on-disk staging gate, and `m2b2-stage-20260831-02` subsequently replaced it with the clean committed Milestone 2B.1 runtime-safety build. The normal-lobby control path is still not Bannerlord-runtime-verified. L2/L3 still require role-confirmed loaded hashes, result isolation, and a successful connection-only rerun.

### Battle bridge/diagnostic folder

Usually:

```text
%USERPROFILE%\Documents\Mount and Blade II Bannerlord\CoopSpectator
```

Important artifacts:

- `battle_roster.json`
- `battle_result.json`
- phase/entry/selection/spawn request/status files
- `battle_agent_spawn_trace.txt`
- `battle_entry_compatibility.txt`
- `battle_runtime_bundle.txt`
- hero creation and campaign map prototype files.

The runtime bundle records process/log/crash paths and links to exact trace artifacts. Use it to correlate evidence from multiple processes.

### Shared diagnostic overrides

`CoopDebugConfig` can share possession/morale overrides through:

```text
%PROGRAMDATA%\Mount and Blade II Bannerlord\CoopSpectator\debug_overrides.txt
```

This file affects runtime diagnostics outside the repository. Record and restore its state after an approved targeted investigation.

## Diagnostic flags

### Independent functional opt-in

```text
COOPSPECTATOR_CAMPAIGN_MAP_PROTOTYPE=1
```

This enables an experimental feature, not merely logging. Keep it separate from diagnostic runs unless the prototype is the subject.

### Master diagnostic gate

Most verbose probes require:

```text
COOPSPECTATOR_VERBOSE_DIAGNOSTICS=1
```

Then enable only the targeted second-level flag:

| Flag | Area |
|---|---|
| `COOP_DEBUG_TEXTS` | debug UI text |
| `COOP_DEBUG_DEDICATED_STDIO` | dedicated standard-output diagnostics |
| `COOPSPECTATOR_OOB_DIAGNOSTICS` | order-of-battle counts/formations |
| `COOPSPECTATOR_FIELD_BOUNDARY_DIAGNOSTICS` | field deployment boundary geometry |
| `COOPSPECTATOR_VILLAGE_BOUNDARY_DIAGNOSTICS` | village boundary geometry |
| `COOPSPECTATOR_POSSESSION_DIAGNOSTICS` | controlled agent/corpse/possession |
| `COOPSPECTATOR_MORALE_DIAGNOSTICS` | siege morale/panic/retreat |
| `COOPSPECTATOR_COMBAT_MODEL_DIAGNOSTICS` | cooperative combat model |
| `COOPSPECTATOR_HIDEOUT_BOSS_CHOREOGRAPHY_DIAGNOSTICS` | hideout boss choreography |
| `COOPSPECTATOR_SIEGE_TEAM_ADD_DIAGNOSTICS` | native siege team-add boundary |
| `COOPSPECTATOR_DEDICATED_SCENE_CONTRACT_DIAGNOSTICS` | dedicated scene resolution |
| `COOPSPECTATOR_EXACT_SCENE_BOOTSTRAP_DIAGNOSTICS` | exact-scene file/catalog/bootstrap |
| `COOPSPECTATOR_BATTLE_SELECTION_NAME_DIAGNOSTICS` | selection identity/name projection |
| `COOPSPECTATOR_BATTLE_ENTRY_STATUS_DIAGNOSTICS` | entry status/readiness |
| `COOPSPECTATOR_BATTLE_MAP_CONTRACT_DIAGNOSTICS` | initializer/live mission/deployment contract |
| `COOPSPECTATOR_BATTLE_SHELL_DIAGNOSTICS` | mission loading shell |
| `COOPSPECTATOR_EXACT_ARMY_RUNTIME_DIAGNOSTICS` | exact live army scan |
| `COOPSPECTATOR_EXACT_AGENT_CONTRACT_DIAGNOSTICS` | per-entry transfer trace/bundle |
| `COOPSPECTATOR_EXACT_CREATE_AGENT_PAYLOAD_DIAGNOSTICS` | experimental payload profile sweep |
| `COOPSPECTATOR_EXACT_SIEGE_SYNC_DIAGNOSTICS` | siege mission-object synchronization |
| `COOPSPECTATOR_EXACT_CREATE_AGENT_DIAGNOSTICS` | high-volume create-agent corridor |
| `COOPSPECTATOR_AI_WIELD_DIAGNOSTICS` | live AI weapon-wield state |

Other source-local flags also exist for AI hold, captain perks, and hideout mannequin isolation. Search the exact use site before enabling them; some may be gated differently or alter isolation behavior.

### Diagnostic discipline

- Enable one narrow probe set at a time.
- Record the process, scene, battle/scenario ID, peer count, phase, and timestamps.
- Disable it after the run.
- Do not leave per-agent, per-frame, synchronized-message, or live-agent-scan diagnostics active in normal runtime.
- Never add string formatting or full-agent iteration to a hot path outside the gate.
- If a probe touches native state, synchronized objects, agent equipment, or mission behavior collections, classify it as risky runtime code even if its stated purpose is logging.

## Low-level investigation workflow

Use source first, then descend to lower levels when the contract is unclear.

1. Reproduce with a narrow scenario and collect both client and dedicated logs.
2. Align timestamps with `battle_runtime_bundle.txt`, process IDs, result/roster files, and the exact battle ID.
3. Identify the last managed log boundary and whether the failure is managed exception, native access violation, deadlock/stall, or protocol mismatch.
4. Search current source and all affected battle types before changing anything.
5. Use `ilspycmd` against the exact installed DLL to inspect native managed contracts, overloads, field ownership, and behavior order. Record DLL hash/version and decompiled symbol names.
6. Use WinDbg for crash dumps, native stacks, exception records, loaded modules, and managed/native boundary correlation.
7. Use IDA Free only when decompiled managed code and WinDbg evidence cannot explain the native transition.
8. Convert the decision rule into a pure contract and contract test where possible.
9. Add runtime diagnostics only if the existing logs cannot distinguish hypotheses, and keep them gated.
10. Validate the fix across the scenario matrix and sequential mission/reconnect boundaries.

Do not generalize behavior discovered in one game DLL version without checking the actual runtime version.

## Smoke-test matrix after runtime changes

Select the smallest matrix proportional to the change, but any shared spawn/network/phase/result change should include:

| Dimension | Minimum cases |
|---|---|
| Process topology | one-machine host + dedicated/client, then remote second machine |
| Scenario | affected case plus field, village, siege assault, and relevant special subtype |
| Agent type | infantry, ranged, mounted where legal, hero/commander |
| Timing | initial join, battle start, reinforcement, death/respawn, battle end |
| Lifecycle | first battle, second sequential battle, abort/retry |
| Network | assigned peer ready, disconnect, reconnect/late join where supported |
| Result | casualties, wounded, hero outcome, prisoners/loot, exactly-once journal |

For siege changes additionally cover attacker/defender, empty machine selection, auto-deploy, machine destruction, ladders/gates, and AI progress.

## Build/runtime mismatch checklist

If source appears fixed but runtime behavior is unchanged:

1. Confirm the command used the intended worktree, not the script's default project root.
2. Capture source DLL output timestamp/size/hash.
3. Capture installed client module DLL timestamp/size/hash.
4. Capture installed dedicated server and dedicated client-bin DLL timestamps/size/hash.
5. Confirm both module descriptors and module IDs loaded.
6. Confirm the runtime log prints the expected assembly path/version and patch registration markers.
7. Confirm the dedicated process was restarted after deployment.
8. Check stale `CoopSpectatorMP`, alternate module copies, `dist/`, or launcher module lists.
9. Check client/dedicated reference paths and multiplayer DLL source.
10. Only then treat the mismatch as a logic failure.
