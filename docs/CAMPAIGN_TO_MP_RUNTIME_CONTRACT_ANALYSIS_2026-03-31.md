# Campaign To MP Runtime Contract Analysis

Date: 2026-03-31
Project: `BannerlordCoopSpectator3`

## Question

Can we transfer the campaign battle map to multiplayer `1:1`, or are we missing critical data?

## Short Answer

We are not blocked by missing campaign encounter data anymore.

We are blocked by runtime compatibility:

- singleplayer field battles and multiplayer custom games do not boot through the same mission contract
- dedicated custom game flow is built around multiplayer map ids and multiplayer scene registry
- the dedicated server install does not ship the `SandBoxCore` `battle_terrain_*` scene assets

So exact `1:1` scene transfer is not disproven forever, but naive scene-name substitution is not a viable implementation.

## Validated Findings

### 1. Campaign-side data is already sufficient for encounter context

Current host-side snapshot/export already carries enough information to reconstruct campaign encounter semantics inside a runtime scene:

- `BattleSceneName`
- `WorldMapScene`
- `MapPatchSceneIndex`
- `MapPatchNormalizedX`
- `MapPatchNormalizedY`
- `PatchEncounterDir`

This means the current blocker is not "we do not know which campaign map/location the battle belongs to".

### 2. Singleplayer field battle startup is scene-direct and behavior-rich

Vanilla singleplayer battle startup opens the exact scene directly:

- [`BannerlordMissions.cs`](C:/dev/projects/BannerlordCoopSpectator3/.codex_tmp/decompiled_mountandblade/TaleWorlds.MountAndBlade/BannerlordMissions.cs#L119)
- [`MissionState.cs`](C:/dev/projects/BannerlordCoopSpectator3/.codex_tmp/decompiled_mountandblade/TaleWorlds.MountAndBlade/MissionState.cs#L302)

Important SP field-battle properties:

- `MissionState.OpenNew("CustomBattle", new MissionInitializerRecord(scene) { ... })`
- scene id is the actual battle scene, such as `battle_terrain_*`
- startup stack includes SP battle behaviors such as:
  - `MissionAgentSpawnLogic`
  - `CustomBattleMissionSpawnHandler`
  - `MissionCombatantsLogic`
  - `BattleDeploymentMissionController`
  - `BattleDeploymentHandler`

Relevant decompile evidence:

- [`BannerlordMissions.cs`](C:/dev/projects/BannerlordCoopSpectator3/.codex_tmp/decompiled_mountandblade/TaleWorlds.MountAndBlade/BannerlordMissions.cs#L119)
- [`BannerlordMissions.cs`](C:/dev/projects/BannerlordCoopSpectator3/.codex_tmp/decompiled_mountandblade/TaleWorlds.MountAndBlade/BannerlordMissions.cs#L151)

### 3. Multiplayer custom-game startup is game-type driven, not scene-direct in the same sense

Vanilla MP startup first resolves a multiplayer game mode by name:

- [`Module.cs`](C:/dev/projects/BannerlordCoopSpectator3/.codex_tmp/decompiled_mountandblade/TaleWorlds.MountAndBlade/Module.cs#L1597)

Custom-game mission/network bootstrap then works with a much smaller contract:

- [`InitializeCustomGameMessage.cs`](C:/dev/projects/BannerlordCoopSpectator3/.codex_tmp/decompiled_mountandblade/NetworkMessages.FromServer/InitializeCustomGameMessage.cs#L17)
- [`LoadMission.cs`](C:/dev/projects/BannerlordCoopSpectator3/.codex_tmp/decompiled_mountandblade/NetworkMessages.FromServer/LoadMission.cs#L15)

Those messages carry only:

- `GameType`
- `Map`
- `BattleIndex`

There is no vanilla MP payload here for:

- campaign battle scene id
- patch coordinates
- encounter direction
- SP deployment metadata

### 4. MP map selection is registry-based around usable multiplayer map ids

Vanilla custom-game voting/selection deals with `Map` ids that are explicitly registered as usable multiplayer maps:

- [`CustomGameUsableMap.cs`](C:/dev/projects/BannerlordCoopSpectator3/.codex_tmp/decompiled_mountandblade/TaleWorlds.MountAndBlade/CustomGameUsableMap.cs#L6)
- [`MultiplayerIntermissionVotingManager.cs`](C:/dev/projects/BannerlordCoopSpectator3/.codex_tmp/decompiled_mountandblade/TaleWorlds.MountAndBlade/MultiplayerIntermissionVotingManager.cs#L80)
- [`MultiplayerIntermissionUsableMapAdded.cs`](C:/dev/projects/BannerlordCoopSpectator3/.codex_tmp/decompiled_mountandblade/NetworkMessages.FromServer/MultiplayerIntermissionUsableMapAdded.cs#L23)

This is another concrete sign that "just pass `battle_terrain_n` as the map id" is not the same thing as following the vanilla MP contract.

### 5. Current map-patch transport works only inside the chosen runtime scene

Our current coop path applies campaign patch context onto `MissionInitializerRecord`:

- [`MissionMultiplayerCoopBattleMode.cs`](C:/dev/projects/BannerlordCoopSpectator3/GameMode/MissionMultiplayerCoopBattleMode.cs#L444)

That context is then consumed by mission-side deployment/spawn helpers:

- [`Mission.cs`](C:/dev/projects/BannerlordCoopSpectator3/.codex_tmp/decompiled_mountandblade/TaleWorlds.MountAndBlade/Mission.cs#L6575)
- [`Mission.cs`](C:/dev/projects/BannerlordCoopSpectator3/.codex_tmp/decompiled_mountandblade/TaleWorlds.MountAndBlade/Mission.cs#L6580)
- [`Mission.cs`](C:/dev/projects/BannerlordCoopSpectator3/.codex_tmp/decompiled_mountandblade/TaleWorlds.MountAndBlade/Mission.cs#L6599)
- [`BattleSpawnPathSelector.cs`](C:/dev/projects/BannerlordCoopSpectator3/.codex_tmp/decompiled_mountandblade/TaleWorlds.MountAndBlade/BattleSpawnPathSelector.cs#L103)

Important consequence:

- `SceneHasMapPatch`, `PatchCoordinates`, and `PatchEncounterDir` only influence spawn-path/deployment choice inside the already loaded scene
- they do not change the runtime scene into the exact campaign terrain scene

This explains why `mp_battle_map_*` can become semantically better aligned with campaign position while still looking visually different from the campaign battle scene.

## Asset And Registry Constraints

### Client install

Campaign field battle scenes are registered in SP data and live under `SandBoxCore` scene assets:

- [`sp_battle_scenes.xml`](C:/Program%20Files%20(x86)/Steam/steamapps/common/Mount%20&%20Blade%20II%20Bannerlord/Modules/SandBox/ModuleData/sp_battle_scenes.xml#L192)
- [`sp_battle_scenes.xml`](C:/Program%20Files%20(x86)/Steam/steamapps/common/Mount%20&%20Blade%20II%20Bannerlord/Modules/SandBox/ModuleData/sp_battle_scenes.xml#L376)
- `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\SandBoxCore\SceneObj\battle_terrain_n`
- `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\SandBoxCore\SceneObj\battle_terrain_biome_087b`

### Multiplayer registry

Vanilla multiplayer scene registry for `Battle` lists only official MP battle maps:

- [`MultiplayerScenes.xml`](C:/Program%20Files%20(x86)/Steam/steamapps/common/Mount%20&%20Blade%20II%20Bannerlord/Modules/Native/ModuleData/Multiplayer/MultiplayerScenes.xml#L73)
- [`MultiplayerScenes.xml`](C:/Program%20Files%20(x86)/Steam/steamapps/common/Mount%20&%20Blade%20II%20Bannerlord/Modules/Native/ModuleData/Multiplayer/MultiplayerScenes.xml#L78)
- [`MultiplayerScenes.xml`](C:/Program%20Files%20(x86)/Steam/steamapps/common/Mount%20&%20Blade%20II%20Bannerlord/Modules/Native/ModuleData/Multiplayer/MultiplayerScenes.xml#L83)

No `battle_terrain_*` entries were found there.

### Dedicated install

The dedicated server install currently contains only:

- `Native`
- `Multiplayer`
- local coop modules

It does not ship `SandBoxCore`, and direct checks show:

- `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Dedicated Server\Modules\SandBoxCore\SceneObj\battle_terrain_n` -> missing
- `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Dedicated Server\Modules\SandBoxCore\SceneObj\battle_terrain_biome_087b` -> missing
- `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Dedicated Server\Modules\Native\SceneObj\mp_battle_map_001` -> present

This is a strong hard constraint: even before solving mission-behavior compatibility, the dedicated package does not currently carry the SP battle scene assets we would need for exact `1:1` loading.

Dedicated-side project assumptions already reflect that reduced module set:

- [`CoopSpectatorDedicated.csproj`](C:/dev/projects/BannerlordCoopSpectator3/DedicatedServer/CoopSpectatorDedicated.csproj#L84)

## What The Recent Experiments Proved

### Proven

- Campaign encounter context extraction is good enough for map-patch transport.
- `MissionInitializerRecord.SceneHasMapPatch` path is real and affects battle spawn-path selection.
- `mp_battle_map_*` runtime plus patch context is a valid semantic-improvement path.

### Also proven

- Direct `battle_terrain_*` loading in the current MP experiment path is not stable enough to treat as a baseline.
- Forcing dedicated startup deeper into custom `CoopBattle` can apply patch context, but it introduced a later dedicated crash and is not a safe baseline.

### Not yet proven

- That the dedicated map-server/custom-game system can safely host non-multiplayer scene ids.
- That a dedicated install augmented with SP assets would be enough by itself.
- That SP field-battle scenes can run under MP networking if asset availability is solved.

## Current Engineering Conclusion

Exact `1:1` campaign scene transfer is still an open research problem, not a solved implementation task.

The main blockers are now:

1. `Dedicated asset gap`
   - dedicated server install does not include `SandBoxCore` `battle_terrain_*` scenes

2. `MP registry / custom-game contract gap`
   - vanilla custom-game flow expects multiplayer map ids and compatible game types

3. `Mission behavior contract gap`
   - SP field battles boot with a different behavior stack than MP battle shell

Therefore:

- we do not just need "more campaign data"
- we need more engine/runtime knowledge

## Data Still Missing

These are the next unknowns worth investigating:

1. Exact `DedicatedCustomServer` map-server rules
   - Does it hard-require maps from MP registry only?
   - Can it archive/load a scene outside `Native/ModuleData/Multiplayer/MultiplayerScenes.xml`?

2. Dedicated asset mounting rules
   - Can a dedicated install be extended to mount `SandBoxCore` scene assets safely?
   - If yes, is that enough for battle scene load, or do more SP modules need to be present?

3. Minimal hybrid behavior stack
   - Which SP field-battle behaviors are truly required for `battle_terrain_*` correctness?
   - Which of them are incompatible with MP mission networking?

## Recommended Next Analysis Order

1. Investigate dedicated map-server / archive requirements more deeply.
2. Verify whether dedicated can be extended with `SandBoxCore` assets in an isolated experiment.
3. Compare minimum SP field-battle behaviors against minimum MP custom-game behaviors.
4. Keep the stable baseline on `mp_battle_map_*` while the above is unresolved.

## Practical Decision For Current Development

Until the runtime-contract questions above are answered, treat these as separate tracks:

- `Stable track`
  - keep using `mp_battle_map_*`
  - improve deployment/spawn quality with campaign patch context

- `Research track`
  - investigate true `1:1` scene transfer feasibility
  - do not merge it into the stable runtime path without dedicated-compatible proof
