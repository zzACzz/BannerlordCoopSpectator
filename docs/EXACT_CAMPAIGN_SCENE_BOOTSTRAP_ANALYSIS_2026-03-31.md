# Exact Campaign Scene Bootstrap Analysis

Date: 2026-03-31
Project: `BannerlordCoopSpectator3`

## Question

If we want true `1:1` campaign battle-scene transfer, what exact vanilla pipeline produces `battle_terrain_*`, and which parts of that contract must be recreated or bridged for multiplayer/dedicated?

## Short Answer

Vanilla campaign field battles already have a clear exact-scene contract.

It is not:

- "pick terrain type and guess a scene"
- "open a surrogate MP map and hope native deployment adapts"

It is:

1. campaign map scene produces `MapPatchData`
2. `SceneModel.GetBattleSceneForMapPatch(...)` resolves the exact `battle_terrain_*`
3. vanilla builds a `MissionInitializerRecord` with patch coordinates and encounter direction
4. campaign opens the singleplayer `Battle` mission shell on that exact scene

So the next exact-transfer track should focus on reproducing this bootstrap contract, not on further surrogate-map spawn heuristics.

## Proven Vanilla Exact-Scene Pipeline

### 1. Campaign startup loads the singleplayer battle-scene registry

Decompile of `TaleWorlds.CampaignSystem` shows:

- `Campaign.OnInitialize()` calls `GameSceneDataManager.Initialize()`
- for new/saved campaigns it then calls `InitializeScenes()`
- `InitializeScenes()` scans active modules and loads:
  - `sp_battle_scenes.xml`
  - `conversation_scenes.xml`
  - `meeting_scenes.xml`

Important consequence:

- exact field-battle scene selection is data-driven from active module XML
- it is not hardcoded only in engine native code

### 2. The campaign map scene provides the battle-terrain patch lookup

Decompile of `SandBox.MapScene` shows:

- campaign main-map load calls `MBMapScene.GetBattleSceneIndexMap(...)`
- `MapScene` stores:
  - `_battleTerrainIndexMap`
  - `_battleTerrainIndexMapWidth`
  - `_battleTerrainIndexMapHeight`
- `GetMapPatchAtPosition(...)` converts world-map position into:
  - `sceneIndex`
  - `normalizedCoordinates`

Important consequence:

- the world map already contains the patch-index data that drives exact battle scene selection
- this is the same semantic layer we have been exporting as:
  - `MapPatchSceneIndex`
  - `MapPatchNormalizedX`
  - `MapPatchNormalizedY`

### 3. Vanilla selects the exact `battle_terrain_*` through `SceneModel`

Decompile of `TaleWorlds.CampaignSystem.GameComponents.DefaultSceneModel.GetBattleSceneForMapPatch(...)` shows:

- primary lookup:
  - `GameSceneDataManager.Instance.SingleplayerBattleScenes`
  - filtered by `scene.MapIndices.Contains(mapPatch.sceneIndex)`
  - and `scene.IsNaval == isNavalEncounter`
- fallback:
  - current terrain environment type
  - then random eligible scene

Important consequence:

- exact field-battle scene resolution already exists in managed code
- we do not need to invent our own campaign-scene chooser if we can bootstrap this model path

### 4. Vanilla field-battle encounter builds a rich `MissionInitializerRecord`

Decompile of `TaleWorlds.CampaignSystem` encounter path shows:

- `MapPatchData mapPatchAtPosition = Campaign.Current.MapSceneWrapper.GetMapPatchAtPosition(MobileParty.MainParty.Position);`
- `string battleSceneForMapPatch = Campaign.Current.Models.SceneModel.GetBattleSceneForMapPatch(mapPatchAtPosition, isNavalEncounter);`
- `MissionInitializerRecord rec = new MissionInitializerRecord(battleSceneForMapPatch);`

Then vanilla fills:

- `rec.TerrainType`
- `rec.DamageToFriendsMultiplier`
- `rec.DamageFromPlayerToFriendsMultiplier`
- `rec.NeedsRandomTerrain = false`
- `rec.PlayingInCampaignMode = true`
- `rec.RandomTerrainSeed`
- `rec.AtmosphereOnCampaign`
- `rec.SceneHasMapPatch = true`
- `rec.DecalAtlasGroup = 2`
- `rec.PatchCoordinates = mapPatchAtPosition.normalizedCoordinates`
- `rec.PatchEncounterDir = (attackerPos - defenderPos).Normalized()`

Branching after that:

- naval -> `CampaignMission.OpenNavalBattleMission(rec)`
- caravan/villager -> `CampaignMission.OpenCaravanBattleMission(rec, ...)`
- normal field battle -> `CampaignMission.OpenBattleMission(rec)`

Important consequence:

- our custom battle-start snapshot is already converging toward the real vanilla contract
- especially:
  - exact battle scene id
  - patch coordinates
  - encounter direction

### 5. Vanilla exact scene then opens under the SP `Battle` shell

Decompile of `SandBox.CampaignMissionManager` and `SandBox.SandBoxMissions` shows:

- `CampaignMission.OpenBattleMission(rec)` delegates to `SandBoxMissions.OpenBattleMission(rec)`
- that calls `MissionState.OpenNew("Battle", rec, ...)`

The behavior stack is SP battle oriented, including:

- `MissionAgentSpawnLogic`
- `BattleSpawnLogic("battle_set")`
- `SandBoxBattleMissionSpawnHandler`
- `CampaignMissionComponent`
- `MissionCombatantsLogic(... FieldBattle ...)`
- `BattleDeploymentMissionController`
- `BattleDeploymentHandler`

Important consequence:

- exact `battle_terrain_*` in vanilla does not run under `MultiplayerBattle`
- it runs under the campaign/sandbox `Battle` mission shell

## What This Means For Our Project

### Proven now

We already know enough to mirror vanilla exact-scene bootstrap semantics:

- campaign-side exact scene id
- map-patch scene index
- normalized patch coordinates
- encounter direction

So exact transfer is no longer blocked by missing gameplay data.

### The remaining gap is bootstrap/runtime compatibility

To host exact campaign scenes in multiplayer/dedicated, we need some equivalent of these vanilla pieces:

1. `GameSceneDataManager` populated from `sp_battle_scenes.xml`
2. `SandBoxCore` `battle_terrain_*` scene assets actually mounted and path-resolvable
3. a scene-resolution path that accepts the exact scene id
4. a mission shell that can tolerate:
   - exact SP battle scenes
   - networking / dedicated / coop control requirements

### Surrogate MP maps are now clearly the wrong layer for exact fidelity

Recent diagnostics already proved:

- current `mp_battle_map_*` scenes do not contain native field-battle assets such as:
  - `spawn_path_*`
  - `attacker_*` / `defender_*` formation tags
- therefore native field deployment on surrogate maps hits an asset ceiling

This does not make surrogate maps useless, but it does mean:

- they are not the path to true `1:1` campaign scene fidelity

## Current Best Exact-Transfer Hypothesis

The most promising exact-transfer path now looks like this:

1. reuse vanilla campaign scene-selection logic
   - exact scene id from `GetBattleSceneForMapPatch`

2. reuse vanilla mission-initializer semantics
   - `SceneHasMapPatch`
   - `PatchCoordinates`
   - `PatchEncounterDir`
   - `PlayingInCampaignMode`
   - atmosphere / terrain data

3. add our own glue only where MP/dedicated cannot natively follow SP
   - module/asset mounting
   - scene registration / scene ownership
   - mission/network bridge

That is directly analogous to the prisoner path:

- use vanilla data/meaning where possible
- add custom transport/bridge only where vanilla runtime contract breaks

## High-Risk Unknowns Still Open

### 1. Dedicated module and asset mount contract

We already know stock dedicated does not ship `SandBoxCore` field-battle assets.

Open question:

- if we augment dedicated with the needed modules/assets, will engine scene resolution and map archiving accept `battle_terrain_*` cleanly?

### 2. Listed-server / custom-game scene ownership contract

We already know listed server flow is centered on:

- `GameType`
- `Map`
- `UniqueSceneId`
- multiplayer-owned map lists

Open question:

- can exact campaign scenes be registered/served there with glue, or do we need a parallel/custom load path?

### 3. Mission-shell compatibility

Vanilla exact scenes run under SP `Battle`, not `MultiplayerBattle`.

Open question:

- do we bridge MP networking into a `Battle`-like shell
- or do we keep MP shell and recreate missing SP semantics ourselves

This is now the main architectural fork.

## Recommended Next Experiments

### 1. Bootstrap parity experiment

Goal:

- prove that our host-side resolver matches vanilla campaign exact-scene selection for real encounters

Method:

- compare our `BattleDetector` scene output against vanilla `DefaultSceneModel.GetBattleSceneForMapPatch(...)`
- do this for several terrain / forest / naval cases

### 2. Dedicated bootstrap experiment

Goal:

- determine whether dedicated can be taught to load the same battle-scene registry as campaign

Method:

- initialize or emulate `GameSceneDataManager`
- load `sp_battle_scenes.xml` from active modules
- verify scene lookup for `battle_terrain_*`

### 3. Scene-resolution experiment in augmented runtime

Goal:

- verify whether exact scene ids become acceptable once assets/modules are present

Method:

- probe:
  - `Engine.Utilities.TryGetFullFilePathOfScene("battle_terrain_*")`
  - `Engine.Utilities.TryGetUniqueIdentifiersForScene("battle_terrain_*")`
  - optional `PairSceneNameToModuleName(...)`

### 4. Minimal exact-scene mission-start prototype

Goal:

- test exact scene hosting without dragging in the whole current coop battle runtime

Method:

- separate prototype path
- exact `battle_terrain_*`
- vanilla-style `MissionInitializerRecord`
- minimal behavior stack chosen intentionally

This should be isolated from current surrogate-map spawn heuristics.

## Practical Decision

For true campaign-scene fidelity, exact-scene transfer is now the correct strategic path.

The next implementation work on that path should be:

- bootstrap analysis
- registry/bootstrap probes
- minimal exact-scene hosting prototype

It should not be:

- more surrogate-map spawn heuristics
- more attempts to coerce `mp_battle_map_*` into behaving like SP field-battle scenes
