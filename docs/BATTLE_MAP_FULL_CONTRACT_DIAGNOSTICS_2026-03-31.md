# Battle-Map Full Contract Diagnostics

Date: 2026-03-31  
Project: `BannerlordCoopSpectator3`

## Purpose

This diagnostic pass exists to stop guesswork around campaign encounter transfer and battle-map deployment.

The current question is not "did agents spawn somewhere?" but:

1. Did campaign encounter patch context reach `MissionInitializerRecord`?
2. Did the live mission on dedicated keep that patch context?
3. Did the mission build spawn paths or scene-based formation frames from it?
4. If not, did `DefaultMissionDeploymentPlan` lack scene spawn entries, team plans, or valid formation frames?

## Current implementation stage

We are **not** at exact `1:1` campaign scene transfer.

Current runtime path is:

1. Campaign battle detects `battle_terrain_*` and encounter position/direction.
2. Resolver chooses surrogate `mp_battle_map_*`.
3. Campaign patch context is transported as:
   - `MapPatchSceneIndex`
   - `PatchCoordinates`
   - `PatchEncounterDir`
4. MP runtime is expected to use that context to choose spawn paths / deployment inside the surrogate MP scene.

So the current blocker is **dedicated deployment/runtime contract on the surrogate MP map**, not the absence of campaign encounter data.

## Diagnostic layers added

### 1. `MissionState.OpenNew` contract

Logged by:

- `BattleMapContractDiagnostics: MissionState.OpenNew contract snapshot`

Purpose:

- prove which `OpenNew` overloads exist in the current runtime
- detect signature drift before assuming the Harmony prefix should have fired

## 2. Mission initializer record state

Logged by:

- `BattleMapContractDiagnostics: mission initializer record`

Emitted from:

- `CampaignMapPatchMissionInit.TryApply(...)`
- `MissionStateOpenNewPatches.OpenNew_Prefix(...)`
- `MissionMultiplayerCoopBattleMode.StartMultiplayerGame(...)`

Purpose:

- show the exact `MissionInitializerRecord` state before apply, after apply, and before mission open
- answer whether `SceneHasMapPatch`, `PatchCoordinates`, and `PatchEncounterDir` were actually written

## 3. Live mission runtime contract

Logged by:

- `BattleMapContractDiagnostics: mission runtime contract`
- `BattleMapContractDiagnostics: mission patch/spawn-path snapshot`

Emitted from:

- `CoopMissionClientLogic.AfterStart`
- `TryInitializeServerMissionRuntimeState(...)`

Purpose:

- show whether the created mission still has `HasSceneMapPatch=True`
- compare the reflected private initializer record with the public mission state
- show:
  - `IsFieldBattle`
  - `HasSpawnPath`
  - mission boundaries
  - patch encounter position/direction
  - attacker/defender initial spawn path validity
  - reinforcement path counts

This layer answers whether the patch context died **before** or **after** mission creation.

## 4. Deployment-plan and scene-spawn-entry contract

Logged by:

- `BattleMapContractDiagnostics: deployment plan contract`

Emitted from:

- `TryRepairMaterializedDeploymentPlan(...)`
- `TryResolveRepairedMaterializedSpawnFrame(...)`

Purpose:

- dump `DefaultMissionDeploymentPlan` state when repair runs
- summarize:
  - per-team `InitialPlanMade` / `ReinforcementPlanMade`
  - troop counts
  - spawn-path / target offsets
  - deployment-boundary availability
  - active formation-plan summaries
  - reflected `_formationSceneSpawnEntries` coverage

This is the key layer for answering:

- are scene spawn entries missing?
- are they present only for one side?
- do formation plans have dimensions but no frame?
- do reinforcement entries exist while initial entries do not?

## How to read the next run

### Case A: initializer record is correct, runtime mission loses patch

Look for:

- `mission initializer record ... SceneHasMapPatch=True`
- but later `mission runtime contract ... HasSceneMapPatch=False`

Meaning:

- patch context was written before open
- but the created mission did not keep it
- focus next on mission creation path / dedicated `OpenNew` contract / server-only startup branch

### Case B: runtime mission keeps patch, but no spawn path is built

Look for:

- `mission runtime contract ... HasSceneMapPatch=True`
- `mission patch/spawn-path snapshot ... AttackerInitialPath={Valid=False} DefenderInitialPath={Valid=False}`

Meaning:

- patch context survived
- but battle spawn path selection on the surrogate scene did not initialize
- focus next on `HasSpawnPath`, field-battle flags, and native spawn-path selector prerequisites

### Case C: no spawn path, scene deployment entries missing

Look for:

- `deployment plan contract ... SceneSpawnEntries={... MissingInitial=[...] ...}`
- and later `formation-plan-has-no-frame`

Meaning:

- the mission fell back to field-battle scene-data planning
- but surrogate scene spawn entities for required formations are absent or asymmetric
- focus next on scene spawn entry inventory and map-specific formation tags

### Case D: scene entries exist, but team/formation plan still has no frame

Look for:

- `SceneSpawnEntries` showing entities present
- but `FormationPlans=[... HasDimensions=True HasFrame=False ...]`

Meaning:

- failure is deeper than tag absence
- focus next on native `DefaultDeploymentPlan` planning path and troop/layout inputs

## Files touched by this diagnostics pass

- `Infrastructure/BattleMapContractDiagnostics.cs`
- `Infrastructure/CampaignMapPatchMissionInit.cs`
- `Infrastructure/ExperimentalFeatures.cs`
- `Patches/MissionStateOpenNewPatches.cs`
- `GameMode/MissionMultiplayerCoopBattleMode.cs`
- `Mission/CoopMissionBehaviors.cs`

## Expected log markers for the next run

- `BattleMapContractDiagnostics: MissionState.OpenNew contract snapshot`
- `BattleMapContractDiagnostics: mission initializer record`
- `BattleMapContractDiagnostics: mission runtime contract`
- `BattleMapContractDiagnostics: mission patch/spawn-path snapshot`
- `BattleMapContractDiagnostics: deployment plan contract`

## Practical conclusion

After this pass, the next run should tell us exactly which contract breaks first:

- `initializer record`
- `live mission patch state`
- `spawn path generation`
- `scene spawn entry inventory`
- `formation plan frame creation`

That is the point of this iteration. No new gameplay fix should be attempted until these markers are read.

## Findings from the first full diagnostic run

Logs analyzed:

- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_85960.txt`
- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_83088.txt`
- `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\logs\rgl_log_71444.txt`

Validated facts:

1. Host/client initializer patch path works.
   - Host/client show `MissionState.OpenNew Battle post-apply ... SceneHasMapPatch=True`.

2. Dedicated did **not** receive the same patched initializer.
   - Dedicated shows:
     - `MissionStateOpenNewPatches.Apply failed`
     - `GameModeOverridePatches.Apply failed`
   - The common cause is `MissingMethodException` inside Harmony patch application.

3. Dedicated live mission started with the wrong battle semantics.
   - `mission runtime contract ... IsFieldBattle=False HasSpawnPath=False HasSceneMapPatch=False`
   - reflected initializer record on the live mission stayed zeroed

4. Because `Mission.IsFieldBattle=False`, `DefaultMissionDeploymentPlan` reads the scene as a non-field battle.
   - Decompile shows `ReadSpawnEntitiesFromScene(_mission.IsFieldBattle)`.
   - With `IsFieldBattle=False`, it reads siege-style spawn tags, not field-battle `attacker_*` / `defender_*` tags.
   - On `mp_battle_map_001` that produced empty `FormationSceneSpawnEntry` coverage and then `HasFrame=False`.

5. Therefore the first exact root cause is:
   - dedicated battle-map mission starts without patched initializer record
   - and without `MissionTeamAITypeEnum.FieldBattle`
   - which makes deployment planning read the wrong scene tag model
   - and forces fallback `ffa-scene`

## Current precise repair direction

The next runtime fix should happen before/at server runtime initialization, not in later spawn heuristics:

1. repair the live dedicated mission contract directly
   - apply campaign patch context to the live mission initializer record
   - force `MissionTeamAIType = FieldBattle`
2. reinitialize battle spawn-path selector on the live mission
3. let deployment repair rebuild using field-battle scene tags instead of siege tags

This is the first fix attempted after the diagnostic run.

## Findings from the second diagnostic run

Logs analyzed:

- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_67000.txt`
- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_46416.txt`
- `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\logs\rgl_log_85744.txt`

Validated facts:

1. The live dedicated repair only half-worked.
   - Dedicated successfully forced `MissionTeamAIType=FieldBattle`.
   - Runtime contract then showed `IsFieldBattle=True`.

2. The campaign patch context still did not stick to the live mission.
   - Diagnostics showed:
     - `initializer pre-apply ... SceneHasMapPatch=False`
     - `initializer post-apply ... SceneHasMapPatch=True`
     - immediately after that: `live-mission-record ... SceneHasMapPatch=False`
   - Final runtime state still showed `HasSceneMapPatch=False HasSpawnPath=False`.

3. The reason was semantic, not native.
   - `MissionInitializerRecord` was verified with `ilspycmd` to be a `struct`, not a class.
   - Our previous helper patched it by value, so diagnostics after apply described only a mutated copy.
   - The actual live mission storage remained unchanged.

4. Therefore the second exact root cause is:
   - dedicated live-contract repair mutated a copied `MissionInitializerRecord`
   - the patched struct was not written back to the mission
   - `SceneHasMapPatch`, `PatchCoordinates`, and `PatchEncounterDir` stayed zeroed on the real mission
   - spawn-path reinitialization then correctly reported `HasSpawnPath=False`

## Current precise repair direction after the second run

The live repair path must:

1. read the actual `MissionInitializerRecord` from mission backing storage
2. patch it by `ref`
3. write the patched struct back to the live mission
4. only then re-evaluate `HasSceneMapPatch` and attempt spawn-path initialization

This is now the current fix under test.

## Findings from the third diagnostic run and asset inspection

Logs analyzed:

- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_63396.txt`
- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_77672.txt`
- `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\logs\rgl_log_2620.txt`

Validated facts:

1. The live dedicated initializer repair now works.
   - Diagnostics show:
     - `initializer pre-apply ... SceneHasMapPatch=False`
     - `initializer post-apply ... SceneHasMapPatch=True`
     - `live-mission-record ... SceneHasMapPatch=True`
   - Runtime repair now reports `InitializerPatched=True`.

2. The mission now has the correct patch context but still has no spawn path.
   - Dedicated runtime now shows:
     - `IsFieldBattle=True`
     - `HasSceneMapPatch=True`
     - `HasSpawnPath=False`
   - So the remaining failure is no longer about patch-context delivery.

3. Native field deployment still cannot resolve formation scene entries on `mp_battle_map_001`.
   - Deployment diagnostics show:
     - `SceneSpawnEntries={Defender{InitialEntities=0/11 ...} | Attacker{InitialEntities=0/11 ...}}`
     - `FormationPlans ... HasFrame=False SpawnClass=Unset`
   - Fallback `ffa-scene` spawn then starts from a valid but layout-unaware path.

4. `ilspycmd` + raw scene inspection explain why this happens.
   - `MBSceneUtilities.GetAllSpawnPaths(Scene)` only loads paths named `spawn_path_00` through `spawn_path_31`.
   - `BattleSpawnPathSelector.Initialize()` succeeds only if at least one such spawn path exists.
   - `DefaultMissionDeploymentPlan.ReadSpawnEntitiesFromScene(true)` looks for scene tags:
     - `attacker_infantry`, `attacker_ranged`, `attacker_cavalry`, ...
     - `defender_infantry`, `defender_ranged`, `defender_cavalry`, ...
   - Raw asset inspection of:
     - `Modules\Native\SceneObj\mp_battle_map_001\scene.xscene`
     - `Modules\Native\SceneObj\mp_battle_map_002\scene.xscene`
     showed:
     - no `spawn_path_*` entries
     - no `attacker_*` / `defender_*` field-battle spawn tags
     - `mp_battle_map_001` contains generic/siege/ambient paths only
     - `mp_battle_map_002` has `<Paths/>`

5. Therefore the third exact root cause is asset-level.
   - current surrogate `mp_battle_map_*` scenes do not satisfy the native field-battle deployment contract
   - even after the mission contract is repaired correctly, native field deployment still has no usable spawn paths and no field-battle formation entities to bind to
   - the remaining random/ffa spawn behavior is expected under these scene assets

## Current practical conclusion

The blocker is no longer in campaign-context transport or live mission initialization.

The blocker is now:

1. surrogate `mp_battle_map_*` scene content lacks native field-battle spawn assets
2. therefore native field deployment cannot be made reliable by runtime flags alone

This means the next implementation step should not be another spawn heuristic patch. It should be one of:

1. create/patch battle-map scenes that actually contain `spawn_path_*` plus `attacker_*` / `defender_*` field tags
2. or implement a custom spawn-layout system that does not depend on native field deployment assets
3. or revisit exact scene transfer only after dedicated asset-mount/runtime constraints are solved
