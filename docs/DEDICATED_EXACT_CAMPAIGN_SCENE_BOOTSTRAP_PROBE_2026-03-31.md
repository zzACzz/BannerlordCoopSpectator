# Dedicated Exact Campaign Scene Bootstrap Probe

Date: 2026-03-31  
Project: `BannerlordCoopSpectator3`

## Purpose

This probe is the next analysis-first step after:

- `docs/EXACT_CAMPAIGN_SCENE_BOOTSTRAP_ANALYSIS_2026-03-31.md`
- `docs/DEDICATED_SCENE_RESOLUTION_PROBE_2026-03-31.md`

We already know the vanilla managed exact-scene contract.

We still need narrower dedicated-side facts:

- does the runtime installation actually contain `SandBox` / `SandBoxCore` exact-scene assets
- is `sp_battle_scenes.xml` present in the live engine base path
- is `TaleWorlds.CampaignSystem` even loadable in the dedicated process
- if we manually pair `battle_terrain_*` to `SandBoxCore`, does scene path resolution start working

This is intentionally a probe, not a gameplay fix.

## Instrumentation

Implemented in:

- `DedicatedServer/SceneContractProbe.cs`
- guarded by `ExperimentalFeatures.EnableDedicatedExactCampaignSceneBootstrapProbe`
- invoked from the existing dedicated startup probe path during `OnSubModuleLoad()`

It does not open missions.

It does one controlled mutating experiment:

- `Utilities.PairSceneNameToModuleName(scene, "SandBoxCore")`

That experiment is only used to measure whether manual scene pairing is enough to make exact `battle_terrain_*` resolvable in live dedicated runtime.

## What It Logs

### 1. Runtime file availability

Marker:

- `DedicatedSceneContractProbe: exact bootstrap runtime files.`

Fields:

- `EngineBasePath`
- `ProcessBaseDirectory`
- presence of `Modules/SandBox`
- presence of `Modules/SandBoxCore`
- presence of `Modules/SandBox/ModuleData/sp_battle_scenes.xml`
- presence of `TaleWorlds.CampaignSystem.dll`
- presence of `battle_terrain_n/scene.xscene`
- presence of `battle_terrain_biome_087b/scene.xscene`

### 2. Campaign assembly availability

Marker:

- `DedicatedSceneContractProbe: campaign assembly availability.`

Fields:

- whether `TaleWorlds.CampaignSystem` is already loaded
- whether `GameSceneDataManager` type can be resolved
- whether `DefaultSceneModel` type can be resolved

### 3. `sp_battle_scenes.xml` registry summary

Marker:

- `DedicatedSceneContractProbe: sp battle scenes registry.`

Fields:

- file existence
- scene count
- exact XML entry summary for:
  - `battle_terrain_n`
  - `battle_terrain_biome_087b`

### 4. Manual exact-scene pairing test

Marker:

- `DedicatedSceneContractProbe: exact scene manual pair probe.`

Fields:

- expected file path under `Modules/SandBoxCore/SceneObj/...`
- pre-pair `TryGetFullFilePathOfScene`
- pre-pair `TryGetUniqueIdentifiersForScene`
- whether `PairSceneNameToModuleName(..., "SandBoxCore")` succeeded
- post-pair `TryGetFullFilePathOfScene`
- post-pair `TryGetUniqueIdentifiersForScene`

## How To Read The Results

### Case A: files are missing before any pairing

Meaning:

- exact-scene hosting is blocked at installation / asset-mount level
- no managed bootstrap recreation can fix this alone

### Case B: files exist, but pre-pair and post-pair both fail

Meaning:

- dedicated blocker sits deeper than simple module-name pairing
- likely engine scene registry / custom-game hosting contract

### Case C: files exist, pre-pair fails, post-pair resolves path

Meaning:

- manual scene pairing is a viable bridge candidate
- next step should move toward a minimal exact-scene hosting prototype

### Case D: path resolves after pair, but `UniqueSceneId` still fails

Meaning:

- file visibility alone is not enough
- unique-scene metadata or archive/map-server contract remains unresolved

## Expected Markers For Next Run

Search for:

- `DedicatedSceneContractProbe: begin exact campaign bootstrap probe.`
- `DedicatedSceneContractProbe: exact bootstrap runtime files.`
- `DedicatedSceneContractProbe: campaign assembly availability.`
- `DedicatedSceneContractProbe: sp battle scenes registry.`
- `DedicatedSceneContractProbe: exact scene manual pair probe. Scene=battle_terrain_n`
- `DedicatedSceneContractProbe: exact scene manual pair probe. Scene=battle_terrain_biome_087b`
- `DedicatedSceneContractProbe: end exact campaign bootstrap probe.`

## Practical Next Step

Run a fresh dedicated restart and inspect these markers before any new exact-scene mission-open experiment.

This probe should answer whether the next blocker is primarily:

1. missing assets in the dedicated runtime root
2. missing campaign assembly/scene registry availability
3. scene-name to module-name pairing
4. deeper exact-scene hosting / unique-scene-id contract

## First Validated Result

Run validated from:

- dedicated log: `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\logs\rgl_log_86460.txt`
- host log: `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_62196.txt`
- client log: `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_87440.txt`

Observed facts:

- host still knows the exact campaign scene:
  - campaign `MissionState.OpenNew` starts from `battle_terrain_n`
- runtime transfer into dedicated still opens surrogate `mp_battle_map_001`
- dedicated startup probe reports:
  - `HasSandBoxModule=False`
  - `HasSandBoxCoreModule=False`
  - `HasSpBattleScenesXml=False`
  - `HasCampaignSystemServerDll=False`
  - `HasCampaignSystemClientDll=False`
  - `HasLoadedCampaignSystemAssembly=False`
  - `GameSceneDataManagerTypeResolved=False`
  - `DefaultSceneModelTypeResolved=False`
- direct `battle_terrain_*` scene resolution still fails before and after manual pairing:
  - `PrePairResolved=False`
  - `PairSucceeded=True`
  - `PostPairResolved=False`

Interpretation:

- on the current stock dedicated install, exact `battle_terrain_*` hosting is blocked before mission startup
- the blocker is not missing campaign encounter data and not just missing scene-name pairing
- the dedicated runtime root itself currently lacks the required `SandBox` / `SandBoxCore` exact-scene files and `CampaignSystem` layer

Practical consequence:

- no further surrogate-map spawn heuristics will get us to true `1:1` exact scene transfer
- the next exact-transfer experiment must start with a dedicated asset/bootstrap environment that actually contains `SandBox`, `SandBoxCore`, and the needed assemblies or an equivalent custom bridge
