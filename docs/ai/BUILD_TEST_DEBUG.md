# Build, Test, and Debug Guide

Last source verification: **2026-08-28**

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

## Contract tests

Contract tests are standalone `net8.0` executables. They are the safest automated validation layer because most link narrow pure production contracts rather than loading Bannerlord.

Run one project:

```powershell
dotnet run --project .\Tests\CoopBattleStartup.ContractTests\CoopBattleStartup.ContractTests.csproj -c Release
```

Run all contract-test projects:

```powershell
Get-ChildItem .\Tests -Recurse -Filter *.csproj |
  Sort-Object FullName |
  ForEach-Object {
    dotnet run --project $_.FullName -c Release
    if ($LASTEXITCODE -ne 0) { throw "Contract test failed: $($_.FullName)" }
  }
```

The 17 current projects cover:

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
- native aftermath/casualty aggregation.

Test interpretation:

- Passing contract tests prove deterministic contract behavior at the linked source revision.
- They do not load native mission scenes, exercise Harmony targets, transmit real Bannerlord messages, or validate client visuals.
- A new native/runtime fix should add a pure contract test when its decision can be extracted without native state, then still receive the appropriate real mission run.

No tests were run during the 2026-08-28 documentation pass.

## Development helper scripts

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

### `scripts/CreateReleasePackage.ps1`

Unless `-SkipBuild` is specified, it performs client and dedicated Release builds. It then deletes/recreates selected `dist/` directories and ZIP files. Modes include `-LightOnly` and `-GitHubAssetsOnly`.

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
