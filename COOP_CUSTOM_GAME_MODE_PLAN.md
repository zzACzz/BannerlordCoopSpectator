# План: кастомний game mode + server-side spawn (вибір юнітів з кампанії)

**Мета:** клієнти в MP-битві обирають лише юнітів з roster кампанії (battle_roster.json); сервер створює агентів за цим списком.

---

## Фаза 1 — Модуль на дедику (для тесту)

| Крок | Що робимо | Статус |
|------|-----------|--------|
| 1.1 | **DedicatedServerType** у SubModule.xml змінити з `none` на `all`. | ✅ |
| 1.2 | **Деплой на Dedicated Server:** після Build копіюється в `$(DedicatedServerRootDir)\Modules\CoopSpectator\bin\Win64_Shipping_Client` (DedicatedServerRootDir = BannerlordRootDir\..\Mount & Blade II Dedicated Server; можна перевизначити /p:DedicatedServerRootDir=...). | ✅ |
| 1.3 | **Запуск дедика з нашим модулем:** DedicatedHelperLauncher при SteamLikeLaunch додає `_MODULES_*Native*Multiplayer*DedicatedCustomServerHelper*CoopSpectator*_MODULES_` (константа IncludeCoopSpectatorOnDedicated = true). | ✅ |
| 1.4 | Перевірка: після start_mission у логах дедика має з'явитися рядок від CoopMissionSpawnLogic (читання battle_roster.json). | ⏳ |

**Результат фази 1:** на дедику виконується наш код, читається roster з файлу. Місія поки що ванільна (TD), але ми готові до підстановки режиму.

---

## Фаза 2 — Кастомний game mode (офіційний підхід)

| Крок | Що робимо | Статус |
|------|-----------|--------|
| 2.1 | Додати посилання на **TaleWorlds.MountAndBlade.Multiplayer** (DLL з гри або з Dedicated Server `Win64_Shipping_Server`). | ✅ |
| 2.2 | **Реєстрація game mode:** у SubModule.OnSubModuleLoad викликати `Module.CurrentModule.AddMultiplayerGameMode(new MissionMultiplayerCoopBattleMode("CoopBattle"))`. | ✅ |
| 2.3 | **Клас режиму:** створити клас, що наслідує `MissionBasedMultiplayerGameMode`, перевизначити `StartMultiplayerGame(scene)` з нашими MissionBehavior (spawn з roster, team select, тощо). | ✅ (скелет: GameMode/MissionMultiplayerCoopBattleMode.cs, MissionMultiplayerCoopBattle, MissionMultiplayerCoopBattleClient) |
| 2.4 | **Список класів з roster:** підставити `CampaignRosterTroopIds` туди, де задаються selectable troops. | ⏳ |
| 2.5 | **Конфіг/start_mission:** вказати ім'я нашого game mode (CoopBattle), щоб при start_mission сервер запускав нашу місію. | ✅ (GameType CoopBattle + add_map_to_usable_maps у TryWriteStartupConfig; _MODULES_ з CoopSpectator у safe args) |

---

## Фаза 3 — Server-side spawn

| Крок | Що робимо | Статус |
|------|-----------|--------|
| 3.1 | **SpawningBehavior:** для кожного peer створювати агента з вибраним troop ID з roster (AgentBuildData, Controller = Player). | ⏳ |
| 3.2 | Клієнт надсилає "обрано troop_id"; сервер зберігає вибір і при спавні використовує відповідний BasicCharacterObject. | ⏳ |
| 3.3 | Fallback при порожньому roster: дефолтний troop або лог. | ⏳ |

---

## Фаза 4 — UI вибору юнітів

| Варіант | Опис | Статус |
|--------|------|--------|
| A | Підставити наш список у ванільний UI вибору класу. | ⏳ |
| B | Власний Gauntlet-екран з списком з roster. | ⏳ |

---

## Узгодження

- [ ] Порядок фаз 1 → 2 → 3 → 4 підходить?
- [ ] Один білд (Client DLL) для дедика прийнятний?
- [ ] Назва game mode: "CoopSpectator" чи інша?

Після тесту Фази 1 (логи дедика) — переходимо до Фази 2.

---

## Known startup watchdog crash: WebPanel threadpool starvation

**Симптом:** Watchdog кидає `"Couldn't start the game in time. Crashing intentionally so a new server can start."` (ServerSideIntermissionManager.Tick). У VS dump analysis: "Thread pool thread or asynchronous task blocked on a synchronous call"; у Parallel/Async Call Stacks — .NET ThreadPool Worker блокується в `ManualResetEventSlim.Wait` / `TaskAwaiter.GetResult` у стеку ASP.NET Core WebHost (WebHostExtensions.Run / WaitForTokenShutdown).

**Root cause:** У TaleWorlds модулі DedicatedCustomServer WebPanel запускає `_host.Run()` всередині `Task.Run(...)`. `Run()` є блокуючим і тримає threadpool-потік назавжди → threadpool starvation → watchdog не встигає і крашить процес.

**Fix (реалізовано через Harmony-патч у CoopSpectatorDedicated):** Не викликати `IWebHost.Run()` на threadpool. Варіанти: (1) запускати WebHost через `StartAsync()` (non-blocking) + при unload викликати `StopAsync` і `Dispose`; (2) fallback: запускати оригінальний код (з `_host.Run()`) на окремому **Thread** (IsBackground = true), а не через `Task.Run`, щоб блокувався виділений потік, а не threadpool. У нашому патчі використано варіант (2): DedicatedCustomGameServerStateActivated виконується на виділеному потоці; OnSubModuleUnloaded — StopAsync + Dispose. Не використовувати `.Wait()`, `.Result`, `GetAwaiter().GetResult()` у коді, пов’язаному з host lifecycle.

**Перевірка:** Вимкнути WebPanel (якщо можливо) — таймаут має зникнути; після застосування патча — сервер не падає watchdog при штатному старті, у дампі немає threadpool starvation від WebPanel.
