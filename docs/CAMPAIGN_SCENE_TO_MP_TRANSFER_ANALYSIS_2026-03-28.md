# Campaign Scene To Multiplayer Transfer Analysis

## Goal

Перенести не просто "назву карти", а правильний battle-scene context з campaign у multiplayer runtime, щоб:

- позбутися залежності від `mp_tdm_map_001`
- перестати спиратися на тимчасові hardcoded spawn points
- підготувати основу для великих battle tests проти лордів і армій

## Current State

Поточний stable runtime усе ще змішує офіційний MP bootstrap і наш custom coop battle flow:

- mission bootstrap іде через `MissionState.OpenNew("MultiplayerTeamDeathmatch", ...)`
- dedicated startup config теж фіксує `GameType TeamDeathmatch`
- listed test scene зараз фіксований як `mp_tdm_map_001`
- spawn fallback у runtime теж має hardcoded safe coordinates саме для `mp_tdm_map_001`

Ключові файли:

- `GameMode/MissionMultiplayerCoopBattleMode.cs`
- `DedicatedHelper/DedicatedHelperLauncher.cs`
- `Mission/CoopMissionBehaviors.cs`

## Main Finding

Зараз у проєкті змішані три різні поняття "scene":

1. `Campaign world map scene`
   - це `Campaign.Current.MapSceneWrapper`
   - це НЕ battle scene
   - поточний `BattleStartMessage.MapScene` зараз береться саме звідси

2. `Campaign singleplayer battle scene`
   - це реальний `battle_terrain_*` scene, який vanilla обирає для польової битви
   - саме його треба вважати правильним carrier-ом даних battle terrain

3. `Multiplayer runtime scene`
   - це scene, який реально відкриває dedicated/client mission bootstrap
   - зараз це `mp_tdm_map_001`
   - надалі це має бути або офіційна MP battle map, або окремий сумісний runtime scene

Висновок:

`BattleStartMessage.MapScene` у поточному вигляді має неправильну семантику для майбутнього переносу карти в MP.

## Vanilla Campaign Scene Selection

Vanilla campaign уже має правильний selector path для battle scenes:

1. беремо позицію на карті кампанії
2. через `Campaign.Current.MapSceneWrapper.GetMapPatchAtPosition(...)` отримуємо `MapPatchData`
3. через `Campaign.Current.Models.SceneModel.GetBattleSceneForMapPatch(mapPatch, isNavalEncounter)` отримуємо actual battle scene id

Підтверджені API:

- `TaleWorlds.CampaignSystem.Map.IMapScene.GetMapPatchAtPosition(CampaignVec2&)`
- `TaleWorlds.CampaignSystem.ComponentInterfaces.SceneModel.GetBattleSceneForMapPatch(MapPatchData, bool)`

Дані battle scenes далі живуть у:

- `GameSceneDataManager.SingleplayerBattleScenes`
- `SandBox/ModuleData/sp_battle_scenes.xml`

Тобто правильний campaign-side source of truth для terrain scene уже існує в самому vanilla pipeline.

## Multiplayer Scene Registry Findings

У `Native/ModuleData/Multiplayer/MultiplayerScenes.xml` уже є офіційні battle-mode MP сцени:

- `mp_battle_map_001`
- `mp_battle_map_002`
- `mp_battle_map_003`

Вони прив’язані до `GameType name="Battle"`.

Окремо там є TDM сцени:

- `mp_tdm_map_001`
- `mp_tdm_map_001_spring`
- інші `mp_tdm_*`

Висновок:

для першої безпечної міграції є сенс цілитися не в raw singleplayer scenes, а спершу в офіційні MP battle scenes, бо вони ближчі до потрібної semantics і менше зав’язані на TDM spawn assumptions.

## Dedicated / Bootstrap Constraints

Поточний dedicated flow має жорстке обмеження:

- startup config приймає офіційні `GameType`
- custom `GameType=CoopBattle` як значення dedicated config не є стабільним шляхом
- поточний безпечний bootstrap тримається на `GameType TeamDeathmatch`

Це означає:

- не можна просто "передати campaign scene id і відкрити її як завгодно"
- потрібен explicit runtime scene resolution layer між campaign data і MP mission bootstrap

## Spawn Problem Root Cause

Поточна проблема spawn не лише в "поганих координатах".

Насправді зараз є одразу три шари технічного боргу:

- wrong scene semantics у payload (`MapScene` не battle scene)
- bootstrap зафіксований на `mp_tdm_map_001`
- spawn fallback має hardcoded coordinates під одну TDM сцену

Тому прямий перехід до великих lord battles на поточній схемі справді ризикований.

## Recommended Direction

Так, напрям "спочатку нормально перенести карту/scene context, а вже потім добивати великі battle tests" є правильним.

Але безпечний шлях не такий:

- `campaign MapScene -> одразу відкриваємо це в MP`

Правильніший phased path такий:

1. **Fix payload semantics**
   - перестати брати `BattleStartMessage.MapScene` з `Campaign.Current.MapSceneWrapper.ToString()/GetMapSceneName`
   - замість цього обчислювати actual campaign battle scene id через `MapPatchData + SceneModel.GetBattleSceneForMapPatch`

2. **Add explicit scene transfer model**
   - у payload треба не лише `scene id`, а мінімум:
   - `CampaignBattleSceneId`
   - `MapPatchSceneIndex`
   - за потреби terrain metadata / environment terrain types

3. **Add runtime scene resolver**
   - окремий mapping layer:
   - `campaign battle scene -> multiplayer runtime scene`
   - він не має бути implicit

4. **First runtime target: official MP battle scenes**
   - перша безпечна версія має мапити campaign terrain buckets на `mp_battle_map_001..003`
   - це вже краще за `mp_tdm_map_001`

5. **Only later evaluate direct SP scene loading**
   - тільки після того, як:
   - payload semantics правильні
   - resolver працює
   - spawn derivation від scene працює
   - client join не ламається

## Why Not Load Singleplayer Battle Scenes Immediately

Бо це найризикованіший перший крок.

Невідомі наперед речі:

- чи підтримує поточний MP mission bootstrap пряме відкриття `battle_terrain_*`
- чи не зламається client join
- чи є сумісні spawn points / navmesh assumptions
- чи не зламаються MP-specific mission behaviors

Тобто direct SP scene loading може стати наступним етапом, але не першим.

## Proposed Implementation Order

### Phase 1: Semantic Fix

- замінити current `MapScene` source на true campaign battle scene selector
- не міняти ще runtime scene
- просто почати логувати:
  - current world-map scene wrapper
  - selected campaign battle scene id
  - map patch data

### Phase 2: Runtime Resolver

- створити `CampaignToMultiplayerSceneResolver`
- перша таблиця мапінгу:
  - plain/steppe/desert/forest buckets -> `mp_battle_map_001..003`
- не чіпати ще direct SP scenes

### Phase 3: Spawn Derivation

- прибрати hardcoded `mp_tdm_map_001` spawn fallback
- перейти на scene-derived spawn logic
- якщо scene не дає валідних spawn points, мати explicit fallback per runtime scene

### Phase 4: Bootstrap Switch

- перевести stable test flow з `mp_tdm_map_001` на одну з `mp_battle_map_*`
- тільки після успішних тестів розширювати resolver

### Phase 5: Optional Direct SP Scene Experiment

- окремий експериментальний path
- не змішувати його з main stable runtime

## Practical Conclusion

Найправильніший наступний coding target не "одразу вантажити campaign карту в MP", а:

- спочатку зробити правильний extraction actual campaign battle scene id
- потім додати explicit scene resolver
- і тільки після цього переводити runtime зі `mp_tdm_map_001` на scene-aware battle maps

Це дає:

- менше ризику зламати client join
- чистішу діагностику
- правильну основу для spawn logic
- кращу підготовку до large army / lord battle tests

