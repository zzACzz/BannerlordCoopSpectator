# Project Context

## Mission
Створити стабільний кооператив для Bannerlord, де multiplayer-битви підключені до кампанії хоста.

## Target gameplay loop
1. Host у Campaign.
2. Host запускає dedicated helper.
3. При вході в бій: `start_mission`.
4. Clients join через Custom Server List.
5. Clients отримують контроль юнітів.
6. Після бою: `end_mission`, повернення у campaign flow.

## Stable today
- Dedicated startup path працює.
- Listed server join працює.
- Mission start/end цикл повторюється в межах однієї сесії.
- Vanilla `TeamDeathmatch` listed baseline стабільний.
- `battle_roster.json` path працює: SP write -> dedicated read -> MP-safe surrogate resolve.
- Campaign scene transfer у `mp_battle_map_*` працює.
- Battle-map client load працює.
- Custom battle-map selection overlay працює.
- Large battle-map client spawn тепер проходить без crash у validated run.
- Result / prisoner / aftermath return path назад у campaign працює.

## In progress
- Повторювані великі battle-map цикли без regressions.
- Довгостроково чистіший formation/captain handoff після spawn.
- Spectator / respawn transitions без десинків.
- Better deployment / spawn frame quality for large battles.
- Runtime-contract analysis for true `1:1` campaign scene transfer into MP.
- Exact campaign-scene bootstrap analysis for `battle_terrain_*`: `MapPatchData -> SceneModel -> MissionInitializerRecord -> SP Battle shell`.
- Exact campaign army-spawn and spawn-zone analysis for `battle_terrain_*`: native `MissionAgentSpawnLogic + PartyGroupTroopSupplier + BattleSpawnPathSelector + field-battle formation tags`.
- Exact campaign post-spawn army-bootstrap analysis now shows that local `SelectAllFormations` suppression works and the live blocker has shifted into the post-possession army bootstrap layer, not early captain handoff.
- Dedicated exact campaign-scene bootstrap probe for runtime files, `sp_battle_scenes.xml`, campaign assembly availability, and manual `PairSceneNameToModuleName(..., "SandBoxCore")`.
- Exact campaign-scene bootstrap staging is now implemented on dedicated deploy/launch path: staged `SandBox` / `SandBoxCore` official assets plus expanded modded `_MODULES_` list (`Native -> SandBoxCore -> Sandbox -> Multiplayer -> CoopSpectatorDedicated`).
- Exact campaign-scene runtime selection is re-enabled again after staging; dedicated mission selection now pre-registers `battle_terrain_*` as usable maps and rejects web-panel apply unless `Map/GameType` really changed to the requested exact scene.
- Exact-scene client crash at `Loading xml file: SceneObj/battle_terrain_n/scene.xscene.` was traced to a client bootstrap mismatch: crash artifacts showed the multiplayer client still launched with `_MODULES_*Native*Multiplayer*Bannerlord.Harmony*CoopSpectator*_MODULES_`, without `SandBoxCore` / `Sandbox`, while exact `battle_terrain_*` scenes live in those official modules. Client launch entry points now use `_MODULES_*Native*SandBoxCore*Sandbox*Multiplayer*Bannerlord.Harmony*CoopSpectator*_MODULES_` for exact-scene tests.
- Dedicated scene-resolution probe to verify live module mount / scene path / unique-scene-id facts before any new exact-scene experiments.
- Full contract diagnostics for `MissionInitializerRecord -> live mission -> spawn path -> deployment plan -> formation frame` on battle-map runtime.

## Current architectural conclusion
- Observer/tick hacks для `MissionPeer.SelectedTroopIndex` непридатні.
- Late ownership transfer після vanilla spawn непридатний: дає "напівживого" агента.
- Правильний довгостроковий напрямок: не підміняти vanilla TDM class selection, а прибрати залежність від TDM troop selection UI і робити власний coop-controlled spawn path після вибору сторони.
- Великий battle-map crash виявився пов'язаним не з map-load, а з native formation/captain handoff при spawn у непорожню AI-формацію.
- Поточний стабільний baseline використовує battle-map-specific client handoff guards, а не грубе видалення native startup components.
- Exact `1:1` campaign scene transfer поки не блокується браком encounter data; поточний blocker виглядає як native runtime contract: MP map registry, dedicated asset set, і різниця між SP battle stack та MP custom-game stack.
- Vanilla campaign exact battle startup already has a clear managed contract: `MapSceneWrapper.GetMapPatchAtPosition -> SceneModel.GetBattleSceneForMapPatch -> MissionInitializerRecord(SceneHasMapPatch/PatchCoordinates/PatchEncounterDir) -> CampaignMission.OpenBattleMission(rec)`.
- Stock dedicated runtime додатково виглядає прив'язаним до multiplayer-owned map path: локально немає `SandBoxCore` `battle_terrain_*` assets, а listed/map-server flow центрований на `Multiplayer` scenes.
- Exact-scene bootstrap staging moved the blocker forward: in the modded dedicated runtime `SandBox` / `SandBoxCore`, `sp_battle_scenes.xml`, and `battle_terrain_*` path resolution are now present, so the remaining exact-transfer risk is no longer missing assets but runtime scene selection / manager-map contract / mission startup on the exact scene itself.
- Native campaign army spawn on exact scenes is now decompiled and understood: SP battles seed armies through `MissionAgentSpawnLogic` backed by `PartyGroupTroopSupplier(MapEvent.PlayerMapEvent, ...)`, while spawn zones are built from `spawn_path_*` plus field-battle `attacker_*` / `defender_*` formation entries. Our current exact-scene runtime is still hybrid because player possession is MP-style while army bootstrap is custom.
- Latest exact-scene evidence narrows the current blocker further: client-side `OrderController.SelectAllFormations` suppression already works, so the remaining gap is the handoff from successful player possession into native-style two-army deployment/bootstrap.
- Поточний surrogate `mp_battle_map_*` path уперся в asset-level стелю: у цих сценах немає native field-battle `spawn_path_*` і `attacker_*` / `defender_*` tags, тому true campaign-faithful deployment там не досягається лише runtime flags.

## Engineering principles
- Використовувати ванільний MP flow, не ламати його.
- Сервер авторитетний у spawn/control.
- Мінімум ризикових патчів.
- Ключові lifecycle переходи завжди логуються.

## Hard constraints
- Не змішувати client/dedicated DLL reference профілі.
- `GameTypeId` має бути узгоджений у code/config/runtime.
- Dedicated stack не повинен містити client-only behaviors.
- Campaign troop ids і MP troop ids — різні простори ідентифікаторів; між ними потрібен явний mapping layer.

## Definition of done (iteration)
- 3 послідовні battle cycles без critical crash.
- Clients стабільно отримують та втрачають контроль агента за очікуваним flow.
- Логи достатні для root-cause без ручного decompile.
- Великий battle-map spawn працює не лише в одному run, а повторюється стабільно.
