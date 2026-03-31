# Dedicated Map Server And Scene Contract Analysis

Date: 2026-03-31
Project: `BannerlordCoopSpectator3`

## Question

Can the dedicated custom server host an exact campaign field battle scene such as `battle_terrain_n`, or is the dedicated pipeline hard-wired to multiplayer-owned maps?

## Short Answer

The dedicated pipeline is strongly centered on multiplayer-owned maps.

Three concrete facts now back this up:

1. The dedicated install does not ship `SandBoxCore` `battle_terrain_*` scene assets.
2. The listed-server map helper enumerates owned maps from `Utilities.GetSingleModuleScenesOfModule("Multiplayer")`.
3. Startup/register/load paths all revolve around `Map`, `GameType`, and `UniqueSceneId`, not around a campaign battle-scene contract.

This does not prove exact `1:1` transfer is impossible forever, but it proves that dedicated-side support will need more than passing a different scene string.

## Validated Findings

### 1. Dedicated install is an asset-thin runtime

Observed module set in the local dedicated install:

- `Native`
- `Multiplayer`
- `FastMode`
- local coop modules

Not present:

- `SandBox`
- `SandBoxCore`

Practical consequence:

- `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Dedicated Server\Modules\Native\SceneObj\mp_battle_map_001` exists
- `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Dedicated Server\Modules\SandBoxCore\SceneObj\battle_terrain_n` does not exist
- `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Dedicated Server\Modules\SandBoxCore\SceneObj\battle_terrain_biome_087b` does not exist

This is already a hard blocker for exact field-battle scene hosting on the stock dedicated install.

### 2. Dedicated map-server ownership is explicitly tied to the `Multiplayer` module

Decompile of `TaleWorlds.MountAndBlade.ListedServer.MapServer.ListedServerMapServerSubModule` shows:

- `ModuleName = "Multiplayer"`
- `_ownedMaps = Utilities.GetSingleModuleScenesOfModule("Multiplayer")`
- `MapList` returns only maps contained in `_ownedMaps`

Important implication:

- the map-server list endpoint is not a generic "all scenes the engine can load" list
- it is intentionally filtered to scenes owned by the `Multiplayer` module

This is a strong argument that the listed/custom-server download helper is built for MP scene ownership, not for arbitrary campaign scenes.

### 3. Map archiving uses scene-path resolution, not campaign semantics

Decompile of `TaleWorlds.MountAndBlade.ListedServer.MapServer.ArchivedMap` shows:

- current/archive creation calls `Utilities.GetFullFilePathOfScene(mapName)`
- it then zips the resolved scene directory with `ZipFile.CreateFromDirectory(...)`

Implication:

- if a scene name can be resolved to a real scene directory, the map-server can archive it
- but this only helps after scene ownership/selection/startup have already accepted that map name

So archive creation is not the first blocker. Scene availability and scene ownership come first.

### 4. Dedicated listed-server startup/register path is based on `Map` and `UniqueSceneId`

Decompile of `TaleWorlds.MountAndBlade.ListedServer.ServerSideIntermissionManager` shows:

- server startup takes `MultiplayerOptions.OptionType.Map`
- it computes `uniqueSceneId` via `Utilities.TryGetUniqueIdentifiersForScene(map)`
- it registers the game with selected `gameType`, `map`, and `uniqueSceneId`
- mission start later sends `LoadMission(gameType, map, battleIndex)` and calls `Module.CurrentModule.StartMultiplayerGame(gameType, map)`

Important implication:

- dedicated mission start is still built around the MP custom-game tuple `(GameType, Map, UniqueSceneId)`
- there is no dedicated-side slot here for campaign scene metadata

### 5. Dedicated custom server expects multiplayer mission shell behavior

Decompile of `TaleWorlds.MountAndBlade.DedicatedCustomServer.MissionCustomGameServerComponent` shows a runtime built around:

- `MissionMultiplayerGameModeBase`
- `MissionScoreboardComponent`
- `MissionLobbyComponent`
- MP warmup / round components
- battle results logged through `CustomBattleServer`

This confirms the dedicated custom-game server is not a thin transport layer over arbitrary mission types. It expects an MP mission shell with MP behaviors.

### 6. Dedicated listed-server registration includes loaded module names

Decompile of `DedicatedCustomServerIntermissionManagerHandler` shows the connection/registration path passes:

- `Utilities.GetModulesNames()`
- optional-module policy from startup info

This matters because:

- even if we later add extra assets/modules to the dedicated runtime, they become part of the server-side contract
- that may affect registration, compatibility, and client expectations

## Local Data Evidence

### Multiplayer scene registry in dedicated install

`C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Dedicated Server\Modules\Native\ModuleData\Multiplayer\MultiplayerScenes.xml`

Observed:

- `mp_battle_map_001`
- `mp_battle_map_002`
- `mp_battle_map_003`
- other `mp_*` modes

Not observed:

- `battle_terrain_n`
- `battle_terrain_biome_087b`
- any `battle_terrain_*`

### Stock dedicated scene assets

Observed under dedicated `SceneObj`:

- official `mp_battle_map_*`
- other `mp_*` scenes

Not observed:

- campaign field-battle `battle_terrain_*` directories

## Engineering Consequences

### What this proves

- Exact `1:1` campaign scene transfer is not blocked only by our mod code.
- The stock dedicated environment itself is biased toward MP-owned scenes.
- A naive `Map=battle_terrain_n` strategy is not a credible implementation path.

### What this does not yet prove

- Whether a custom dedicated runtime augmented with extra modules/assets could host `battle_terrain_*`
- Whether `Utilities.TryGetUniqueIdentifiersForScene("battle_terrain_n")` would succeed in such an augmented runtime
- Whether clients could then consume/download such a scene cleanly through the listed-server map helper

## Current Best Hypothesis

For exact `1:1` campaign scene hosting on dedicated, we will probably need some combination of:

1. Additional scene assets and possibly extra modules on dedicated
2. A way to make the scene visible as an owned/accepted custom-game map
3. A mission-shell bridge that keeps MP networking happy while tolerating SP field-battle scene semantics

Without all three, exact transfer is unlikely to stabilize.

## Recommended Next Experiments

1. Isolated asset experiment
   - Clone or stage a dedicated runtime with `SandBoxCore` scene assets available.
   - Check whether `battle_terrain_*` becomes path-resolvable there.

2. Unique-scene-id experiment
   - Verify whether `Utilities.TryGetUniqueIdentifiersForScene("battle_terrain_*")` works in that augmented runtime.

3. Ownership experiment
   - Determine whether the map-server/listed-server flow still rejects scenes that are not owned by `Multiplayer`, even if assets exist.

4. Only then retry direct load
   - Do not retry `battle_terrain_*` load on the stock dedicated install; that path is now known to be under-provisioned.

## Practical Decision For Now

Treat stock dedicated support for exact campaign field-battle scenes as unproven and currently blocked.

For stable development:

- keep `mp_battle_map_*` as the stable runtime path
- keep improving deployment/spawn fidelity there
- keep exact `1:1` work on a dedicated research track, starting with asset/mounting validation
