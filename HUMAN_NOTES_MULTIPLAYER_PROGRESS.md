# Progress Notes

## Current objective
Закрити repeatable vertical slice кооп-битви на battle-map:
join -> unit control -> battle -> aftermath -> return.

## Confirmed working
- Dedicated helper із campaign стартує.
- Сервер видно у Custom Server List.
- Client join працює.
- `start_mission` і `end_mission` відпрацьовують.
- Повторний цикл у межах однієї dedicated-сесії стабільний.
- Vanilla `TeamDeathmatch` mission load стабільний без client/dedicated crash.
- SP roster передається в dedicated через `battle_roster.json`.
- Campaign roster мапиться в MP-safe `mp_*` surrogate ids.
- Dedicated резолвить surrogate у валідний `BasicCharacterObject`.
- Campaign battle scene transfer у battle-map runtime працює.
- Custom battle-map selection overlay працює.
- Explicit side/unit selection + `Spawn` + `G`-start battle працюють.
- Battle aftermath / prisoner return path назад у campaign працює.
- Validated large battle-map spawn тепер працює без client crash.

## Active engineering tasks
1. Підтвердити, що великий battle-map spawn стабільний у повторних циклах, не лише в одному run.
2. Перевірити, чи suppress `AssignFormationToPlayer` не ламає командування/каптанський gameplay після старту бою.
3. Зменшити flicker у unit list під час authoritative side/entry correction.
4. Поліпшити deployment / spawn frame quality для великих battle-map боїв.
5. Прогнати e2e matrix на 2-4 клієнти.

## Known risks
- Version mismatch client vs dedicated.
- Patch compatibility між build версіями гри.
- Mission behavior order та side effects.
- Hybrid native MP shell все ще існує навколо coop battle-map flow.
- Large battle path still produces many spawn-frame fallbacks.
- Current spawn stabilization still relies on narrow native handoff suppression patches.

## Important findings from this session
- Crash у battle-map spawn виявився великим-battle-specific, а не загальним map-load issue.
- Ключова різниця між crash-run і success-run була не просто в карті, а в live formation handoff:
  - малий бій давав майже solo agent;
  - великий бій одразу давав player'у непорожню cavalry formation з AI bots.
- Вирішальний фактор був client-side `AssignFormationToPlayer` / captain handoff під час spawn-handshake.
- Поточний working stack для spawn stabilization:
  - explicit owning-peer rebind after replace-bot;
  - local visual finalize after `SetAgentPeer`;
  - suppress local `MissionPeer.FollowedAgent` network echo;
  - suppress local `AssignFormationToPlayer` during battle-map spawn handshake for live formations.

## Recommended next implementation
1. Прогнати кілька великих battle-map сценаріїв поспіль з різними terrain -> `mp_battle_map_*`.
2. Перевірити post-spawn command/control behavior після suppress `AssignFormationToPlayer`.
3. Якщо з'явиться regression, ізолювати вже `BotsControlledChange` / `ControlledFormation` metadata окремо від spawn possession.
4. Після стабілізації spawn перейти до UX/cleanup:
   - selection flicker;
   - native warmup banner;
   - deployment zones / spawn frames.

## Next milestone acceptance
- 3 битви поспіль без critical помилок.
- Кожен клієнт хоча б 1 раз контролює surrogate-агента без TDM UI side effects.
- Повернення в intermission стабільне.
- Troubleshooting зафіксований у 1 короткому playbook.
