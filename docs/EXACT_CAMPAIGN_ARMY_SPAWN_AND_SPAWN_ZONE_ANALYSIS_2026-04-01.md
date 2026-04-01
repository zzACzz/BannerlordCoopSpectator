# Exact Campaign Army Spawn And Spawn-Zone Analysis

Date: 2026-04-01  
Project: `BannerlordCoopSpectator3`

## Goal

З'ясувати не припущеннями, а по native code, як кампанія:

1. відкриває exact `battle_terrain_*` сцену,
2. створює армії,
3. обирає spawn zone / spawn path,
4. чому наш exact-scene MP runtime зараз відрізняється від цього контракту.

## Primary findings

### 1. Native campaign army spawn не використовує наш current MP materialization path

Decompile через `ilspycmd` показав:

- `SandBox.SandBoxMissions.OpenBattleMission(MissionInitializerRecord rec)` додає:
  - `MissionAgentSpawnLogic`
  - `BattleSpawnLogic("battle_set")`
  - `SandBoxBattleMissionSpawnHandler`
  - `AssignPlayerRoleInTeamMissionController`
  - `SandboxGeneralsAndCaptainsAssignmentLogic`
  - `BattleDeploymentMissionController`
  - `BattleDeploymentHandler`
- `CreateCampaignMissionAgentSpawnLogic(...)` створює `MissionAgentSpawnLogic` з
  `PartyGroupTroopSupplier(MapEvent.PlayerMapEvent, BattleSideEnum.Defender/Attacker, ...)`.
- `SandBoxMissionSpawnHandler.OnBehaviorInitialize()` бере:
  - `Mission.GetMissionBehavior<MissionAgentSpawnLogic>()`
  - `MapEvent.PlayerMapEvent`
- `SandBoxBattleMissionSpawnHandler.AfterStart()` викликає
  `MissionAgentSpawnLogic.InitWithSinglePhase(...)`.

Практичний висновок:

- native campaign battle сіє армії з `MapEvent.PlayerMapEvent`
- це SP battle contract, не MP `replace-bot + materialized entries + possess one seed`

### 2. Campaign spawn zone складається з двох native шарів

#### 2.1 Spawn path layer

`BattleSpawnPathSelector.Initialize()`:

- читає всі paths сцени через `MBSceneUtilities.GetAllSpawnPaths(mission.Scene)`
- якщо `Mission.HasSceneMapPatch()`:
  - бере patch encounter position через `Mission.GetPatchSceneEncounterPosition`
  - бере patch direction через `Mission.GetPatchSceneEncounterDirection`
  - обирає best initial path по відстані до encounter position і alignment з direction

Тобто campaign patch context не "спавнить напряму", а вибирає правильний `spawn_path_*`.

#### 2.2 Formation spawn entity layer

`DefaultMissionDeploymentPlan.ReadSpawnEntitiesFromScene(_mission.IsFieldBattle)`:

- у field battle очікує scene tags:
  - `attacker_infantry`, `attacker_ranged`, `attacker_cavalry`, ...
  - `defender_infantry`, `defender_ranged`, `defender_cavalry`, ...
- ці tags мапляться в `FormationSceneSpawnEntry[,]`
- далі deployment plan робить formation frames уже на основі цих entries

Практичний висновок:

- campaign-like spawn zone це не одна точка
- це поєднання:
  - `spawn_path_*`
  - field-battle formation tags / prefab set

### 3. Exact `battle_terrain_*` scenes мають потрібні native assets

Перевірка exact scene assets показала:

- `battle_terrain_n/scene.xscene` містить `sp_battle_set`
- `battle_terrain_n/scene.xscene` містить `spawn_path_01`, `spawn_path_02`, `spawn_path_03`, `spawn_path_04`

Тобто exact campaign scenes мають native spawn-zone infrastructure, якої бракувало surrogate `mp_battle_map_*`.

## Current exact-scene MP gap

Поточний runtime уже дійшов до:

- exact `battle_terrain_*` load на client і dedicated
- successful on-demand seed + `replace-bot`
- successful player possession

Але далі ми все ще не живемо на native campaign army-spawn contract.

Зараз у нас:

- player possession іде через MP authoritative path
- battle after-start materialization іде через наш `CoopMissionSpawnLogic`
- formation/captain handoff усе ще проходить через MP `AssignFormationToPlayer`

Тобто exact scene вже campaign-like, але army bootstrap усе ще hybrid.

## 2026-04-01 concrete finding

Server-side suppression для `SelectAllFormations` не працював не через неправильну умову, а через build/runtime gap:

- `BattleMapSpawnHandoffPatch` був у client module
- але не був включений у `DedicatedServer/CoopSpectatorDedicated.csproj`
- і не викликався з `DedicatedServer/SubModule.cs`

Через це dedicated не мав:

- `BattleMapSpawnHandoffPatch: prefix applied to MissionNetworkComponent.HandleClientEventSelectAllFormations.`
- `BattleMapSpawnHandoffPatch: suppressed server-side SelectAllFormations during exact-scene spawn handshake`

Цей gap уже закритий у коді 2026-04-01.

## Current architectural conclusion

Якщо ціль:

- "створити spawn zone як у кампанії"
- "бачити 2 армії й вселятись у одного бійця"

то правильний напрямок уже не в сліпих materialization fallback'ах, а в зближенні з native campaign contract:

1. exact `battle_terrain_*` scene
2. valid map patch context
3. valid `spawn_path_*`
4. valid field-battle formation entries
5. army bootstrap, який не ламає MP peer possession / captain handoff

## Immediate next check

Після dedicated rebuild/restart треба перевірити, що сервер тепер реально має:

- `BattleMapSpawnHandoffPatch: prefix applied to MissionNetworkComponent.HandleClientEventSelectAllFormations.`
- `BattleMapSpawnHandoffPatch: suppressed server-side SelectAllFormations during exact-scene spawn handshake`

Якщо після цього exact-scene server crash лишиться, наступний root cause вже треба шукати не в відсутньому dedicated patch, а в deeper hybrid boundary між:

- exact campaign scene,
- MP peer possession,
- native captain/deployment systems,
- нашим post-possession army materialization.
