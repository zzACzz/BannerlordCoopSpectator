# Етап 3.3: spectator / unit selection / spawn — технічний план

## Поточний стан (підтверджено)

- Listed dedicated стабільний; клієнт заходить через Custom Server List; start_mission / end_mission працюють; повторний цикл у тій самій сесії підтверджений.
- На dedicated зараз використовується **ванільний TeamDeathmatch** (CleanModuleLoadOnly = true на дедику — наш TdmClone не реєструється). Місія відкривається ванільним кодом з ванільним списком behaviors.
- **CoopMissionClientLogic** і **CoopMissionSpawnLogic** (Mission/CoopMissionBehaviors.cs) у поточному listed flow **не додаються** до місії — вони визначені лише для наших game mode (CoopBattle, TdmClone), які зараз не використовуються на дедику.

---

## 1. У якому стані клієнт з’являється після входу в місію

У ванільному TDM flow:

1. **Intermission (lobby):** клієнт бачить екран вибору команди (team select) і класу (class/equipment). Це **MissionLobbyComponent** + **MultiplayerTeamSelectComponent** + **MissionLobbyEquipmentNetworkComponent**.
2. **Після вибору і spawn:** клієнт отримує агента (Agent), **Agent.Main** встановлюється, гравець керує юнітом.
3. **Якщо не вибрав команду/клас або ще не заспавнився:** **Agent.Main == null** — стан можна вважати «без агента» (фактично spectator до першого spawn).
4. **Після смерті агента:** ванільний TDM може повернути гравця в «spectator» (камера вільного огляду) або показати екран вибору знову — залежить від режиму (round-based тощо).

**Висновок:** потрібно логувати на клієнті та сервері поточний стан (mission entered, has agent, is spectator, team, spawn) щоб точно визначити, що саме бачить клієнт у нашому поточному flow.

---

## 2. Що перевикористати з Етапу 2 (campaign spectator / broadcaster)

| Компонент | Де використовується | Чи підходить для MP-місії |
|-----------|---------------------|----------------------------|
| **HostStateBroadcaster** | Campaign: хост відправляє STATE (позиція партії) по TCP клієнтам | Ні — це для кампанії (карта), не для всередини MP-битви. У MP-місії мережа — GameNetwork (UDP), не наш TCP. |
| **SpectatorStateReceiver** | Campaign: клієнт приймає STATE і показує UI «spectator» | Ні — те саме, кампанія. |
| **BlockPartyMovementPatch** | Campaign: блокує рух партії для клієнта | Ні — у MP-місії немає «партії на карті». |
| **BlockSettlementMenusPatch** / **BlockTownMenuPatch** | Campaign: блокує меню поселень для клієнта | Ні — у битві інший контекст. |
| **CoopMissionClientLogic** | MP-місія: лог «in battle», перевірка Agent.Main | **Так** — розширити логуванням (mission entered, game mode, peer, has agent, is spectator, team, spawn, controlled agent changed, returned to spectator). |
| **CoopMissionSpawnLogic** | MP-місія (сервер): roster, майбутній спавн | **Так** — додати логування peer connected, spawn requested/completed. |

Тобто для Етапу 3.3 **перевикористовуємо** лише **CoopMissionClientLogic** і **CoopMissionSpawnLogic**, розширивши їх логами та (далі) логікою spectator/unit selection/spawn. Campaign spectator UI/broadcaster залишаються для кампанії; всередині MP-місії використовуємо GameNetwork і Mission API.

---

## 3. Класи / patch points для spectator state та spawn

| Що | Клас / місце | Примітка |
|----|----------------|----------|
| **Стан гравця в місії** | `Agent.Main`, `MissionPeer` (з GameNetwork), `MissionRepresentativeBase` | Agent.Main != null ⇒ гравець має керованого агента. MissionPeer — мережевий представник гравця (Team, ControlledAgent). |
| **Spectator (немає агента)** | Фактично `Agent.Main == null` після входу в місію або після смерті | Ванільний код може використовувати «spectator» камеру; ми логуємо has agent / is spectator. |
| **Team / side** | `MissionPeer?.Team`, `Agent?.Team?.Side` | Для логів team index / side. |
| **Unit selection (меню юніта)** | Ваніль: **MultiplayerTeamSelectComponent**, **MissionLobbyEquipmentNetworkComponent** | Вони вже є у ванільному TDM. Найменший шлях — не замінювати, а доповнити: логувати вибір і spawn. |
| **Spawn / possession** | Сервер: spawn через `Mission.SpawnAgent` з `AgentBuildData.Controller(AgentControllerType.Player)`. Клієнт: отримує синхронізованого агента, рушій встановлює **Agent.Main**. | Ванільний TDM вже робить spawn гравця; ми можемо логувати події або (пізніше) примусово призначати контроль. |
| **Повернення в spectator** | Після смерті агента ваніль може перевести в «spectator» або показати екран вибору знову. | Логувати: controlled agent died / removed, Agent.Main став null. |

**Patch points (де вішати логіку):**

- **MissionLogic (наш)** — `CoopMissionClientLogic`: `AfterStart`, `OnMissionTick` (періодичний лог стану: has agent, is spectator, team), `OnAgentBuild` / події зміни контролю якщо є в API.
- **MissionLogic (сервер)** — `CoopMissionSpawnLogic`: після старту місії лог roster; далі — логування peer connected (через GameNetwork event або tick перевірка), spawn requested/completed (якщо знайдемо події/методи ваніля).
- **Harmony:** опційно — патч на метод, що викликається при призначенні контролю гравцю над агентом (на клієнті), щоб логувати «controlled agent changed»; або обходитися лише перевіркою Agent.Main у tick.

---

## 4. Мінімальний робочий шлях (перша ітерація)

**Варіанти:**

- **A) Клієнт завжди входить як spectator** — не показувати team/class select, не спавнити; лише вільна камера. Потребує змін ванільних behaviors (вимкнути/обійти team select, не викликати spawn для нашого peer). Ризик: порушити sync.
- **B) Клієнт може вибрати юніта** — залишити ванільний team/class select і spawn. Мінімум: переконатися, що це вже працює, додати логування. Якщо ваніль дає вибір і spawn — це вже «мінімальний vertical slice».
- **C) Хост/сервер примусово призначає контроль** — сервер після spawn одного з AI-агентів перепризначає Controller на гравця (peer). Складніше (потрібна синхронізація, знати peer → agent).

**Рекомендація для першої ітерації:** **B** — використати ванільний flow (team select + class + spawn), додати повне логування (mission entered, game mode, peer, has agent, is spectator, team, spawn, controlled agent changed, returned to spectator). Якщо в логах виявиться, що клієнт заходить без вибору або без агента — тоді досліджувати A або C.

**Vertical slice:** клієнт у бою → клієнт отримує контроль над одним юнітом (через ванільний вибір або майбутнє примусове призначення) → після смерті/завершення контролю повертається у spectator (лог «returned to spectator»).

---

## 5. Ризики для sync і mission lifecycle

- **Додавання наших MissionLogic до місії:** якщо інжектити через Harmony у ванільний список behaviors, потрібно не змінювати порядок ініціалізації критичних компонентів (MissionLobbyComponent, spawn, network sync). Додавати наші behaviors **в кінець** списку.
- **Зміна контролю над агентом (possession):** тільки сервер повинен викликати spawn/зміну Controller; клієнт отримує оновлення по мережі. Якщо будемо робити примусове призначення — тільки на сервері через офіційний API.
- **Mission lifecycle:** OnMissionResultReady / завершення місії вже логуються в CoopMissionClientLogic; не змінювати момент виходу з місії.

---

## 6. Інжекція наших behaviors у місію

- **Якщо використовується наш game mode (TdmClone):** у `MissionMultiplayerTdmCloneMode.CreateBehaviorsForMission` додати `CoopMissionClientLogic` (завжди) і `CoopMissionSpawnLogic` (на сервері). Аналогічно для CoopBattle.
- **Якщо на дедику ванільний TeamDeathmatch:** ваніль відкриває місію своїм кодом, наші behaviors не додаються. Варіанти:
  - **Опція 1:** увімкнути TdmClone на дедику (вимкнути CleanModuleLoadOnly, конфіг з GameType TdmClone) — тоді наш список behaviors з CoopMissionClientLogic/CoopMissionSpawnLogic використовується.
  - **Опція 2:** Harmony-патч на створення списку behaviors при відкритті мультиплеєрної місії (наприклад, обгортка делегата, що повертає список, з додаванням наших behaviors у кінець), щоб працювало і з ванільним TDM.

Для першої ітерації достатньо додати наші behaviors у **TdmClone** (і за потреби в CoopBattle), щоб при переході на TdmClone на дедику ми мали повне логування; паралельно можна реалізувати опцію 2 для ванільного TDM.

---

## 7. Чек-лист реалізації (перша ітерація)

1. [x] Розширити **CoopMissionClientLogic**: лог mission entered, mission mode, peer connected/synchronized, player has agent, player is spectator, team index/side; в OnMissionTick — controlled agent changed, returned to spectator (коли Agent.Main з ненульового стає null).
2. [x] Розширити **CoopMissionSpawnLogic** (сервер): лог mission entered, mission mode, peer count (after start і періодично в tick).
3. [x] Додати **CoopMissionClientLogic** (і на сервері **CoopMissionSpawnLogic**) у **TdmClone** і **CoopBattle** CreateBehaviorsForMission.
4. [ ] Опційно: Harmony-патч для інжекції цих же behaviors у ванільний TDM (поточний listed flow використовує ваніль TDM на дедику — наші behaviors там поки не додаються; щоб мати логи без зміни game mode — потрібен патч або перехід на TdmClone на дедику).
5. [ ] **Тест (налаштовано):** (1) DedicatedServer: `CleanModuleLoadOnly = false`. (2) Launcher: `UseTdmCloneForListedTest = true` (конфіг пише GameType TdmClone, add_map_to_usable_maps mp_tdm_map_001 TdmClone). (3) Клієнт: збірка з Multiplayer DLL (BannerlordRootDir або копія DLL у корені проєкту) → у SubModule викликається `TryRegisterTdmCloneForClient()` — реєстрація TdmClone для Join до сервера з TdmClone. Запустити: кампанія → coop.dedicated_start → клієнт (run_mp_with_mod.bat) → Custom Server List → Join до ZZZ_COOP_TEST_7210 → увійти в місію → зібрати логи.
6. [x] Логіка «returned to spectator»: при переході Agent.Main з non-null у null логуємо «returned to spectator (agent lost or died)».

---

## 8. Fallback: 3.3 logging у ванільному TeamDeathmatch без реєстрації TdmClone на клієнті

Якщо клієнтську збірку з GameMode/TdmClone не вдається використовувати (немає Multiplayer DLL, конфлікти залежностей або не хочеться тягнути реєстрацію game mode), можна отримати той самий набір логів (mission entered, has agent, is spectator, team, controlled agent changed, returned to spectator) у **ванільному** TeamDeathmatch через **точечний Harmony-патч**:

1. **Що патчити:** метод, що формує список `MissionLogic` при відкритті мультиплеєрної місії (на клієнті та/або сервері). У ванільному TDM це робиться у відповідному game mode (наприклад, у типі, що відповідає TeamDeathmatch). Потрібно знайти точку, де створюється список behaviors (наприклад, `CreateBehaviors` / `GetMissionBehaviors` або аналог у TaleWorlds.MountAndBlade.Multiplayer).
2. **Що інжектувати:** у кінець списку behaviors додати **CoopMissionClientLogic** (на клієнті) та **CoopMissionSpawnLogic** (на сервері). Ці класи не залежать від нашого game mode — вони приймають `Mission` і логують стан; їх можна збирати в клієнтській збірці без посилання на TaleWorlds.MountAndBlade.Multiplayer, якщо вони не використовують типи з Multiplayer (зараз вони використовують Mission, Agent, GameNetwork — останнє може бути з MountAndBlade або Multiplayer). Якщо GameNetwork з Multiplayer — потрібен мінімальний reference лише для того DLL або reflection для викликів.
3. **Умова інжекції:** виконувати патч лише коли `Mission.Mode` або еквівалент відповідає мультиплеєрному TeamDeathmatch (щоб не чіпати кампанійні місії).
4. **Результат:** клієнт заходить на listed dedicated з ванільним TDM (CleanModuleLoadOnly = true, GameType TeamDeathmatch), без реєстрації TdmClone на клієнті; наш Harmony додає CoopMissionClientLogic у список behaviors → логи 3.3 з’являються так само, як у TdmClone. Сервер при потребі — аналогічно з CoopMissionSpawnLogic.

Цей fallback не вимагає клієнтської реєстрації TdmClone і не вимагає зміни game type на дедику; достатньо одного Harmony-патча на точку формування списку behaviors для мультиплеєрної місії.
