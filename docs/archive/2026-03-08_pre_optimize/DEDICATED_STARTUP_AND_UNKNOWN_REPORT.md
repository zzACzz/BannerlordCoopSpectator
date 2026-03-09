# Dedicated startup crash + Listed server "Unknown" — Factual report

## 1. Literal consistency (TdmClone GameTypeId)

| Місце | Джерело | Значення |
|-------|--------|----------|
| **(a) Client registration** | `SubModule.TryRegisterTdmCloneForClient()` → `new MissionMultiplayerTdmCloneMode(MissionMultiplayerTdmCloneMode.GameModeId)` | `GameModeId` = `CoopGameModeIds.TdmClone` = `"TdmClone"` |
| **(b) Dedicated registration** | `DedicatedServer.SubModule.RegisterCoopBattleGameMode()` → `AddMultiplayerGameMode(new MissionMultiplayerTdmCloneMode(MissionMultiplayerTdmCloneMode.GameModeId))` | той самий `"TdmClone"` |
| **(c) Dedicated startup config / map rotation** | `DedicatedHelperLauncher.TryWriteStartupConfigForListedTest()` → `GameType` + `add_map_to_usable_maps ...` | `UseTdmCloneForListedTest ? CoopGameModeIds.TdmClone : "TeamDeathmatch"` → `"TdmClone"` при listed-test |
| **(d) Host battle-start / start_mission** | `DedicatedServerCommands.SendStartMission()` — лише лог; payload `start_mission` не передає gameTypeId, дедик бере тип з поточного конфігу/rotation | Очікуваний GameType на дедику = `CoopGameModeIds.TdmClone` (лог для перевірки) |

Висновок: один джерело істини — `CoopGameModeIds.TdmClone` = `"TdmClone"`. Client reg, dedi reg і listed-test config використовують його; літеральна узгодженість є.

---

## 2. Чи TdmClone на дедику реально зареєстрований як custom MP mode для listed server?

Так. У `DedicatedServer.SubModule` (не лише клієнт):

- `RegisterCoopBattleGameMode()` викликає `Module.CurrentModule.AddMultiplayerGameMode(new MissionMultiplayerTdmCloneMode(MissionMultiplayerTdmCloneMode.GameModeId))`, тобто реєструє режим під ім’ям `"TdmClone"`.
- Це виконується при завантаженні модуля дедика (`OnSubModuleLoad`, коли `CleanModuleLoadOnly = false`).
- Конфіг listed-test з `GameType TdmClone` і `add_map_to_usable_maps <scene> TdmClone` призводить до того, що дедик при `start_game` шукає режим за іменем `"TdmClone"` і отримує наш `MissionMultiplayerTdmCloneMode`. Реєстрація на стороні дедика є і використовується.

---

## 3. Чому listed server показується як "Unknown"?

- Це **відображення типу гри в UI списку серверів** (наприклад, колонка «Game mode» або аналог).
- Сервер у відповіді списку передає ідентифікатор режиму (наприклад, `"TdmClone"`). Клієнт для показу назви робить lookup за цим id (наприклад, через `str_multiplayer_game_type.TdmClone` або офіційну назву).
- У нашому модулі є `Module/CoopSpectator/ModuleData/multiplayer_strings.xml` з `str_multiplayer_official_game_type_name.TdmClone` і `str_multiplayer_game_type.TdmClone` = "TDM Clone". Ці рядки завантажуються модулем **клієнта** (CoopSpectator).
- Ймовірне пояснення "Unknown":
  - або **UI списку серверів** при визначенні назви режиму використовує лише вбудовані (Native) типи і для будь-якого іншого id показує "Unknown";
  - або механізм резолву назви для custom game type у цьому екрані не підвантажує рядки з нашого модуля (порядок завантаження модулів / інший контекст).

Тобто **це косметичний симптом відображення в UI**, а не доказ того, що реєстрація або ідентичність режиму на дедику неповні. Сам режим на сервері зареєстрований під `"TdmClone"`, клієнт приєднується до нього (HAS_GAMEMODE, join, mission open — успішні). Якщо потрібно "TDM Clone" замість "Unknown" у списку, треба або змінити спосіб, яким гра показує назву режиму для custom type, або перевірити, чи підхід до рядків (module load order, id для server list) підтримує наш модуль.

---

## 4. Додані server-side логи (знайти першу точку падіння після create_mission)

Додані максимально ранні логи на сервері:

| Точка | Файл | Лог |
|-------|------|-----|
| Вхід у фабрику behaviors | `MissionMultiplayerTdmCloneMode.cs` | `TdmClone CreateBehaviorsForMission ENTER IsServer=... IsDedicated=...` |
| Кожен behavior перед поверненням движку | там же | `TdmClone [server] GetMissionBehaviors yielding #N FullTypeName` |
| Кінець повернення списку | там же | `TdmClone [server] GetMissionBehaviors yielded all N behaviors.` |
| OnBehaviorInitialize (TdmClone server) | `MissionMultiplayerTdmClone.cs` | `MissionMultiplayerTdmClone OnBehaviorInitialize (server)` |
| Перед ініціалізацією команд/спавну | `MissionMultiplayerTdmClone.cs` | `MissionMultiplayerTdmClone OnMissionTick: calling InitializeTeamsAndMinimalSpawn (server)` |
| AfterStart | `MissionBehaviorDiagnostic.cs` | `MissionBehaviorDiagnostic AfterStart ENTER` |
| AfterStart | `CoopMissionBehaviors.cs` (client logic) | `CoopMissionClientLogic AfterStart ENTER` |
| AfterStart | `CoopMissionBehaviors.cs` (spawn logic) | `CoopMissionSpawnLogic AfterStart ENTER` |

Інтерпретація при краші дедика:

- Останній рядок виду `GetMissionBehaviors yielding #K TypeName` — движок отримав behavior #K; краш відбувається або при додаванні саме цього behavior, або при отриманні/додаванні наступного (#K+1).
- Якщо з’явився `yielded all N behaviors` — краш після повного формування списку (наприклад, при створенні mission controller або при переході стану місії).
- Якщо є `MissionMultiplayerTdmClone OnBehaviorInitialize` але немає `calling InitializeTeamsAndMinimalSpawn` — краш до першого тику TdmClone (наприклад, в іншому behavior при AfterStart або при ініціалізації).
- Порядок логів AfterStart покаже, до якого behavior’а місія дійшла перед падінням.

Перехід стану місії до Continuing (MissionState / MissionMode) відбувається у рушії; прямих логів цього переходу в поточних змінах немає — орієнтуватися на послідовність AfterStart і yielding.

---

## 5. Головна мета

Знайти **першу server-side точку падіння** після `create_mission`: за чергою логів (yielding → AfterStart → OnBehaviorInitialize → InitializeTeamsAndMinimalSpawn) визначити останній успішний крок перед крашем. Spectator/gameplay logic в цій фазі не змінювали.

---

## 6. Краш до CreateBehaviorsForMission ENTER (pre-factory phase)

Якщо dedicated падає **раніше**, ніж з’являється лог `CreateBehaviorsForMission ENTER`, краш у найранішій фазі bootstrap — між викликом `MissionState.OpenNew` і першим викликом фабрики behaviors движком.

### Додані логи і обгортки (dedicated + client)

| Лог / точка | Призначення |
|-------------|-------------|
| `TdmClone StartMultiplayerGame ENTER` | Вхід у наш game mode start. |
| `TdmClone about to call MissionState.OpenNew` | Прямо перед викликом OpenNew (після цього — код движка, потім create_mission). |
| **Harmony:** `MissionState.OpenNew ENTER` | Вхід у OpenNew (движок). |
| **Harmony:** `MissionState.OpenNew EXIT` | Вихід з OpenNew (якщо побачили EXIT — краш не всередині тіла OpenNew). |
| `TdmClone behavior factory delegate INVOKED by engine` | **Перша точка, де движок викликає нашу фабрику** (після create_mission). Якщо цього логу немає — краш до виклику фабрики (нативний create_mission або managed код до нього). |
| `TdmClone MissionState.OpenNew returned` | Наш код після OpenNew (якщо OpenNew повертається синхронно). |
| `StartMultiplayerGame EXCEPTION` | Managed-виняток у StartMultiplayerGame (повний stack). |
| `GetBehaviorsForMissionWrapper EXCEPTION` | Виняток у фабриці behaviors. |

### Try/catch

- Весь тіло `StartMultiplayerGame` обгорнуто в try/catch з логом повного винятку.
- Делегат фабрики передається як обгортка `GetBehaviorsForMissionWrapper`, яка логує виклик движком і обгортає виклик `CreateBehaviorsForMission` у try/catch.

### Мінімальний dedicated-режим (тимчасовий)

- **Прапорець:** `MinimalDedicatedMissionMode.UseMinimalDedicatedMode` (Infrastructure) = `true`.
- **На dedicated**, коли прапорець увімкнено:
  - `CreateBehaviorsForMission` повертає **мінімальний список**: лише `MissionLobbyComponent`, `MissionMultiplayerTdmClone`, `MissionMultiplayerTdmCloneClient` (без boundary/placer/poll/scoreboard тощо).
  - `MissionMultiplayerTdmClone` у `OnMissionTick` **не викликає** `InitializeTeamsAndMinimalSpawn` — без створення команд і спавну агентів.
- Мета: перевірити, чи dedicated взагалі переживає стадію після create_mission при мінімальній кількості behaviors. Після діагностики вимкнути (`UseMinimalDedicatedMode => false`) і повернути повний список.

### Як читати логи при краші до фабрики

1. Є `MissionState.OpenNew ENTER`, немає `OpenNew EXIT` → краш **всередині** OpenNew (нативний create_mission або managed код до виклику фабрики).
2. Є `OpenNew EXIT`, немає `behavior factory delegate INVOKED` → фабрику движок ще не викликав; можливий асинхронний виклик пізніше або краш у іншому потоці після OpenNew.
3. Є `behavior factory delegate INVOKED`, немає `CreateBehaviorsForMission ENTER` → малоймовірно (ми одразу викликаємо CreateBehaviorsForMission у wrapper); якщо так — краш на першому рядку CreateBehaviorsForMission.
4. Managed-краш → у логах буде `StartMultiplayerGame EXCEPTION` або `GetBehaviorsForMissionWrapper EXCEPTION` з повним stack trace.
