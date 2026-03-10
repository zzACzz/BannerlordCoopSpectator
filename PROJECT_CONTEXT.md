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

## In progress
- Власний coop spawn flow поверх vanilla mission baseline.
- Spawn за campaign roster без залежності від TDM troop menu.
- Надійне призначення контролю агентів без late ownership transfer.
- Spectator transitions без десинків.

## Current architectural conclusion
- Observer/tick hacks для `MissionPeer.SelectedTroopIndex` непридатні.
- Late ownership transfer після vanilla spawn непридатний: дає "напівживого" агента.
- Правильний довгостроковий напрямок: не підміняти vanilla TDM class selection, а прибрати залежність від TDM troop selection UI і робити власний coop-controlled spawn path після вибору сторони.

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
