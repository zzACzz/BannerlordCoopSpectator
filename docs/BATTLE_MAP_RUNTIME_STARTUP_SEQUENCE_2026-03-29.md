# Battle-Map Runtime Startup Sequence

Date: 2026-03-29
Project: `BannerlordCoopSpectator3`
Focus: `campaign battle scene -> mp_battle_map_* -> dedicated/client startup`

## Purpose

This note fixes the current working picture of how the battle-map runtime is started, which components are involved, and where the current crash boundary sits.

The immediate goal is to stop guessing which behavior matters and instead reason from:
- who creates the mission shell
- who builds the behavior stack
- what actually runs first
- where the runtime dies

## Main Layers

There are five distinct layers in the current flow.

1. Campaign host scene selection
2. Dedicated helper scene/game-mode apply
3. Dedicated mission shell creation
4. Server/client mission behavior bootstrap
5. Coop runtime ownership after mission start

These are not interchangeable. A failure in one layer can look similar to another one unless the boundaries are logged.

## Source Of Truth Files

- `C:\dev\projects\BannerlordCoopSpectator3\Campaign\BattleDetector.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\CampaignToMultiplayerSceneResolver.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\DedicatedHelper\DedicatedServerCommands.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\SubModule.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\GameMode\MissionMultiplayerCoopBattleMode.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\GameMode\MissionMultiplayerCoopBattle.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\GameMode\MissionMultiplayerCoopBattleClient.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\GameMode\MissionBehaviorDiagnostic.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Patches\MissionStateOpenNewPatches.cs`

## Confirmed Runtime Chain

### 1. Campaign host resolves the real battle scene

The host no longer sends only `SandBox.MapScene`.

It now resolves:
- real campaign battle scene, for example `battle_terrain_n`
- world-map scene separately
- multiplayer runtime candidate, currently `mp_battle_map_001`

This layer is already working.

### 2. Dedicated helper applies scene-aware startup

The helper uses the web panel options API.

Current intent before mission start:
- `GameType=CoopBattle`
- `Map=mp_battle_map_001`

This layer is also already working: dedicated really opens `mp_battle_map_001`.

### 3. Mission shell selection

`MissionMultiplayerCoopBattleMode.StartMultiplayerGame(scene)` currently chooses:
- `MultiplayerBattle` shell for `mp_battle_map_*`
- `MultiplayerTeamDeathmatch` shell otherwise

This is important: current battle-map startup is no longer pretending to be `MultiplayerTeamDeathmatch`.

### 4. Behavior stack creation

`MissionMultiplayerCoopBattleMode.CreateBehaviorsForMission(...)` builds different stacks for server and client.

For battle-map runtime, the stack is a hybrid:
- native bootstrap-critical behaviors are retained selectively
- coop-specific runtime owners are still present
- some old TDM-specific UI/runtime behaviors remain disabled on the client

This is the main experimental surface right now.

### 5. Coop runtime ownership

Once mission startup survives, these are the intended owners:
- `MissionMultiplayerCoopBattle` owns game-mode runtime and early team setup
- `CoopMissionSpawnLogic` owns snapshot import, authority state, selection/spawn/materialization
- `MissionMultiplayerCoopBattleClient` owns client game-mode bootstrap
- `CoopMissionClientLogic` and custom selection UI are currently disabled on battle maps for crash isolation

## Observed Order Vs Assumed Order

The most important lesson from logs:

`behavior list order != guaranteed visible execution order`

In the latest battle-map runs, the observed `AfterStart` logging order on dedicated was:

1. `MissionBehaviorDiagnostic AfterStart ENTER`
2. `CoopMissionSpawnLogic AfterStart ENTER`
3. `CoopBattle server: AfterStart ENTER`
4. `CoopBattle server: AfterStart EXIT`

This matters because naive reasoning from list index is wrong.

The stack can be printed as:
- `[0] MissionMultiplayerCoopBattle`
- `...`
- `[10] MissionBehaviorDiagnostic`
- `[11] CoopMissionSpawnLogic`

yet the visible startup logs still show other `AfterStart` callbacks before the game-mode log line. The working rule should therefore be:

do not infer runtime order only from behavior indices; trust explicit logs instead.

## Current Confirmed Crash Boundaries

### Variant A: no `MissionLobbyComponent`

Outcome:
- dedicated dies earlier
- logs stop before `MissionBehaviorDiagnostic AfterStart ENTER`
- logs stop before `CoopBattle server: AfterStart EXIT`

Conclusion:
- removing `MissionLobbyComponent` from the battle-map server stack is currently too aggressive
- some native startup dependency still expects whatever it contributes

### Variant B: `MissionLobbyComponent` present, `MissionScoreboardComponent` present

Outcome:
- dedicated reaches:
  - `MissionBehaviorDiagnostic AfterStart ENTER`
  - `CoopMissionSpawnLogic AfterStart ENTER`
  - `CoopBattle server: AfterStart ENTER`
  - `CoopBattle server: AfterStart EXIT`
- then dies before:
  - `CoopBattle server: first mission tick entered`
  - any peer sync logs such as `AC has finished loading`

Conclusion:
- crash boundary moved later
- team creation was necessary and is now fixed
- crash still sits in very early post-`AfterStart`, pre-first-tick startup

### Variant C: `MissionLobbyComponent` present, `MissionScoreboardComponent` removed

This is the current active experiment.

Expected value:
- if runtime survives, scoreboard was the likely harmful native piece
- if runtime still dies at the same boundary, the problem is deeper than scoreboard and likely sits in another native server bootstrap component

## What Is Already Proven

The following are no longer the primary blocker:

- campaign scene extraction
- scene resolver
- dedicated option apply
- `MultiplayerBattle` shell switch
- client registration of `CoopBattle`
- client mission entry
- client loading of `mp_battle_map_001`
- attacker/defender team existence

These used to be blockers earlier, but are now either working or moved behind the current crash boundary.

## Working Hypotheses Ranked

### Most likely

Some native server bootstrap component that remains in the battle-map stack still assumes a fuller vanilla `MultiplayerBattle` environment than we provide.

Current top suspects:
- `MissionScoreboardComponent`
- `MultiplayerTeamSelectComponent`
- `MultiplayerPollComponent`
- `MultiplayerAdminComponent`
- interplay between `MissionLobbyComponent` and one of the above

### Less likely

The crash is caused by our own coop runtime in `CoopMissionSpawnLogic`.

Reason:
- the crash persists even when risky coop runtime is deferred
- it happens before the first server tick
- the current boundary is still inside native early startup

### Already disproven or strongly weakened

- missing attacker/defender teams
- wrong shell type
- client not loading the map
- mission name / game-type mismatch between client and server

## Practical Dependency Rules

At this point these rules should be treated as current truth.

1. `MissionLobbyComponent` is startup-critical for battle-map server runtime until proven otherwise.
2. Early team creation is startup-critical and must happen inside `MissionMultiplayerCoopBattle.AfterStart`, not only on the first mission tick.
3. Client-side crash isolation and server-side crash isolation are different problems; do not merge them mentally.
4. `MissionScoreboardComponent` is known to be required in some TDM-clone paths, but is not yet proven safe on `MultiplayerBattle` battle-map runtime.
5. `MissionLobbyEquipmentNetworkComponent` is expected to be absent on the dedicated server and should not be used as a crash indicator there.

## Next-Step Discipline

Future startup experiments should change only one of these groups at a time:

1. `MissionLobbyComponent`
2. `MissionScoreboardComponent`
3. `TeamSelect / Poll / Admin`
4. boundary trio
5. coop runtime ownership after first tick

If two groups change together, the result becomes hard to attribute.

## Immediate Current Experiment

Current code now does this on battle-map server runtime:
- keeps `MissionLobbyComponent`
- keeps `MultiplayerTeamSelectComponent`
- keeps boundary trio
- keeps `MultiplayerPollComponent` and `MultiplayerAdminComponent`
- keeps `MissionOptionsComponent`
- removes `MissionScoreboardComponent`
- keeps early team creation in `MissionMultiplayerCoopBattle.AfterStart`
- logs both `MissionBehaviorDiagnostic AfterStart EXIT` and `CoopBattle server: AfterStart EXIT`

The next run should answer one precise question:

does the runtime survive when `MissionScoreboardComponent` is the only removed native server bootstrap piece?

## What To Log Next

The next dedicated run should be judged by these markers:

1. `MissionBehaviorDiagnostic AfterStart ENTER`
2. `MissionBehaviorDiagnostic AfterStart EXIT`
3. `CoopBattle server: AfterStart ENTER`
4. `CoopBattle server: AfterStart EXIT`
5. `CoopBattle server: first mission tick entered`
6. any later sync logs such as:
   - `AC has finished loading`
   - `Sending all existing objects`
   - `Syncing a team`

This is the current minimum sequence map needed to reason about the crash without guessing.
