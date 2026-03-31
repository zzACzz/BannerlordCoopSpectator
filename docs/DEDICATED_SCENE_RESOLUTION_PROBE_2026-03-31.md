# Dedicated Scene Resolution Probe

Date: 2026-03-31  
Project: `BannerlordCoopSpectator3`

## Purpose

This probe is the next evidence-gathering step for exact `1:1` campaign scene transfer.

We already know:

- current stable runtime still opens `mp_battle_map_*`
- direct `battle_terrain_*` loading crashed in MP shell
- stock dedicated install is missing `SandBoxCore` battle-scene assets

What we still need from live runtime is narrower:

- which modules the dedicated process reports as loaded
- whether the runtime can resolve `battle_terrain_*` scene paths at all
- whether the runtime can derive `UniqueSceneId` for those scenes
- whether the runtime sees those scenes as owned by `Multiplayer`, `SandBoxCore`, or neither

## Instrumentation

Dedicated-only startup probe was added in:

- `DedicatedServer/SceneContractProbe.cs`
- invoked from `DedicatedServer/SubModule.cs` during `OnSubModuleLoad()`
- guarded by `ExperimentalFeatures.EnableDedicatedSceneContractProbe`

The probe is log-only. It does **not** call `PairSceneNameToModuleName(...)` and does not alter mission startup behavior.

## Scenes Covered

Control scenes:

- `mp_battle_map_001`
- `mp_battle_map_002`

Target campaign scenes:

- `battle_terrain_n`
- `battle_terrain_biome_087b`

## What It Logs

### 1. Loaded modules

Marker:

- `DedicatedSceneContractProbe: loaded modules.`

Fields:

- total module count
- whether `Multiplayer` is loaded
- whether `SandBoxCore` is loaded
- module name sample

### 2. Module-owned scene lists

Marker:

- `DedicatedSceneContractProbe: module-owned scenes.`

Modules probed:

- `Multiplayer`
- `SandBoxCore`

Fields:

- scene count
- whether the list contains `mp_battle_map_001`
- whether the list contains `battle_terrain_n`
- whether the list contains `battle_terrain_biome_087b`
- sample of returned scene ids

### 3. Scene path and unique-id resolution

Marker:

- `DedicatedSceneContractProbe: scene resolution.`

For each target scene it logs:

- `PathResolved`
- `FullPath`
- `UniqueSceneIdResolved`
- reflective dump of the resolved `UniqueSceneId` object, if any

## How To Read The Results

### Case A: `mp_*` resolves, `battle_terrain_*` does not

Meaning:

- stock dedicated runtime still cannot see campaign field-battle scenes as real loadable scene assets
- exact `1:1` remains blocked before any mission-shell work

### Case B: `battle_terrain_*` path resolves, but `SandBoxCore` is not loaded and ownership is absent

Meaning:

- raw scene lookup may be possible through mounted assets or paired paths
- map ownership / startup contract is still a separate blocker

### Case C: `battle_terrain_*` path and `UniqueSceneId` both resolve

Meaning:

- exact-scene research becomes more promising
- next step should be a controlled startup experiment around `Map`, `UniqueSceneId`, and dedicated custom-game registration

### Case D: `GetSingleModuleScenesOfModule("SandBoxCore")` is empty or throws

Meaning:

- even with local files present on disk, the runtime may not have the module mounted in a way that scene ownership APIs can see

## Expected Log Markers For Next Run

Search for:

- `DedicatedSceneContractProbe: begin startup probe.`
- `DedicatedSceneContractProbe: loaded modules.`
- `DedicatedSceneContractProbe: module-owned scenes. Module=Multiplayer`
- `DedicatedSceneContractProbe: module-owned scenes. Module=SandBoxCore`
- `DedicatedSceneContractProbe: scene resolution. Scene=mp_battle_map_001`
- `DedicatedSceneContractProbe: scene resolution. Scene=battle_terrain_n`
- `DedicatedSceneContractProbe: scene resolution. Scene=battle_terrain_biome_087b`
- `DedicatedSceneContractProbe: end startup probe.`

## Practical Next Step

Run a fresh dedicated startup and inspect the new probe output before retrying any direct `battle_terrain_*` mission load.

This probe should decide whether the next research layer is:

1. asset/module mounting
2. map ownership / registration
3. mission startup contract

Without that runtime evidence, further `1:1` scene experiments would still be mostly blind.
