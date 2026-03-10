# Progress Notes

## Current objective
Закрити vertical slice кооп-битви: join -> unit control -> spectator -> return.

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

## Active engineering tasks
1. Прибрати залежність від TDM troop selection UI.
2. Зробити власний coop spawn flow після вибору сторони.
3. Peer -> troop -> agent pipeline на сервері без late ownership transfer.
4. Returned-to-spectator логіка після смерті.
5. Повний e2e тест на 2-4 клієнти.

## Known risks
- Version mismatch client vs dedicated.
- Patch compatibility між build версіями гри.
- Mission behavior order та side effects.
- Vanilla TDM class-selection path активно скидає/перезаписує наші обхідні підміни.
- Late control transfer дає "напівживого" агента: можна замахуватись, але не можна нормально діяти.

## Important findings from this session
- Patch/observer підміни `MissionPeer.SelectedTroopIndex` ламають vanilla TDM flow:
  - або зникає troop menu;
  - або гравець лишається spectator після вибору сторони;
  - або все одно spawn-иться ванільний TDM боєць.
- Late ownership transfer після vanilla spawn теж непридатний:
  - поруч з'являється другий TDM агент;
  - surrogate-agent стає "напівживим".
- Прямий spawn campaign ids у MP runtime непридатний.
- Працює тільки явний mapping:
  - `SP troop/hero` -> `MP-safe mp_* surrogate` -> `BasicCharacterObject` на dedicated.

## Recommended next implementation
1. Залишити `GameType=TeamDeathmatch` як стабільний baseline.
2. Не чіпати більше vanilla troop selection/class request path через observer hacks.
3. Після вибору сторони:
   - або автоматично запускати власний coop spawn,
   - або показувати власний простий coop unit picker;
   vanilla TDM troop menu не використовувати як джерело істини.
4. Сервер має spawn-ити одного дозволеного surrogate-агента відразу як "правильного" агента гравця, а не робити пізній swap ownership.

## Next milestone acceptance
- 3 битви поспіль без critical помилок.
- Кожен клієнт хоча б 1 раз контролює surrogate-агента без TDM UI side effects.
- Повернення в intermission стабільне.
- Troubleshooting зафіксований у 1 короткому playbook.
