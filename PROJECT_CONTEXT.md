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

## In progress
- Spawn за campaign roster.
- Надійне призначення контролю агентів.
- Spectator transitions без десинків.

## Engineering principles
- Використовувати ванільний MP flow, не ламати його.
- Сервер авторитетний у spawn/control.
- Мінімум ризикових патчів.
- Ключові lifecycle переходи завжди логуються.

## Hard constraints
- Не змішувати client/dedicated DLL reference профілі.
- `GameTypeId` має бути узгоджений у code/config/runtime.
- Dedicated stack не повинен містити client-only behaviors.

## Definition of done (iteration)
- 3 послідовні battle cycles без critical crash.
- Clients стабільно отримують та втрачають контроль агента за очікуваним flow.
- Логи достатні для root-cause без ручного decompile.
