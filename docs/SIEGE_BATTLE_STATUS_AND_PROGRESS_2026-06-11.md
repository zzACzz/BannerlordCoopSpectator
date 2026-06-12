# Siege Battle Status And Progress (2026-06-11)

## Мета документа

Цей документ фіксує поточний робочий стан саме для `siege battle` (битв-облог) у гілці `codex/v0.1.1-refresh`.

Він описує:

- як зараз проходить `battle flow` (послідовність роботи бою) для облогових сценаріїв;
- що саме вже реалізовано в моді;
- які підсистеми вже стабілізовані;
- що ще лишилось перевірити `end-to-end` (наскрізними прогонами).

## Межі документа

Цей документ покриває лише такі siege-підтипи:

- `SiegeAssault` (основний штурм облоги)
- `SallyOut` (вилазка)
- `Relief` (бій зовні під час деблокади)
- `LordsHall` (бій у залі лорда)
- `BlockadeSallyOut` (вилазка під час блокади)
- `Blockade` (блокада) як `non-mission path` (шлях без запуску звичайної наземної місії)

Цей документ не замінює окремі статусні документи по:

- `field battle` (польовій битві)
- `village battle` (сільській битві)

## Поточний підтверджений стан

- Система облог уже виділена в окремий `subtype-aware flow` (потік, який розрізняє підтипи облоги), а не змішана в одну спільну гілку з `field battle`.
- `Campaign/BattleDetector.cs` уже вміє окремо визначати `SiegeAssault`, `SallyOut`, `Relief`, `LordsHall`, `BlockadeSallyOut` і `Blockade`.
- `Battle snapshot` (знімок стану бою) вже переносить `ScenarioContext` (контекст сценарію), `SiegeContext` (контекст облоги), `MissionReadyEntryOrder` (порядок складу, який native-код реально допустив до місії) і дані по siege engines (облогових машинах).
- `Dedicated server` (виділений сервер) і mission runtime (виконуваний стан місії) вже мають окрему логіку запуску для `SallyOut`, `Relief` і `LordsHall`.
- `LordsHall` уже не намагається працювати як звичайний `field/siege spawn shell` (каркас звичайного польового або зовнішнього облогового spawn), а йде через окремий controller (контролер місії).
- `Blockade` свідомо залишений поза `land mission runtime` (виконуваним станом наземної місії) і зараз трактується як guarded path (навмисно обмежений шлях без запуску стандартного бойового runtime).
- `Battle result writeback` (запис результатів бою назад у campaign) уже інтегрований для siege-сценаріїв через спільний aftermath pipeline (ланцюг післябойової обробки), але повна бойова валідація всієї матриці сценаріїв ще не завершена.
- Останній крок стабілізації вже прибрав два критичних ризики для `LordsHall`:
  - `allowed control roster` (склад юнітів, дозволених для вибору й контролю) тепер обмежується `mission-ready roster`;
  - `fallback XP writeback` (резервний запис досвіду) більше не розподіляє XP по юнітах, які не брали фактичної участі в indoor-місії.

## Оновлення після live-прогону міської облоги (2026-06-11)

- Підтверджено окремий дефект саме для `SiegeAssault` по місту:
  - native Bannerlord відкривав siege-місію на fortification-сцені `empire_town_d`;
  - але `BattleDetector` забирав `MapScene` через `SceneModel.GetBattleSceneForMapPatch(...)`, тобто як `battle_terrain_*`;
  - через це `battle_roster.json` і `start_mission` переводили `dedicated server` у польову сцену замість siege-сцени.
- Виправлення вже внесено в код:
  - для `SiegeAssault` по місту або фортеці, якщо native вже відкрив не-польову fortification-сцену і це не `LordsHall`, `BattleDetector` тепер бере `MapScene` саме з `Mission.Current.SceneName`;
  - `CampaignToMultiplayerSceneResolver` додатково маркує деградований випадок `SiegeAssault -> battle_terrain_*` окремим `resolver source` для діагностики.
- Межі правки:
  - не змінюється `field battle`;
  - не змінюється `village battle`;
  - не змінюється `SallyOut`, `BlockadeSallyOut`, `Relief` і `LordsHall`.
- Після цієї правки потрібен повторний live-прогін міста і фортеці, щоб підтвердити, що `dedicated` більше стартує на fortification-сцені, а не на `battle_terrain_*`.

## Оновлення після crash/root-cause дослідження `SiegeAssault` (2026-06-12)

- Підтверджено, що попередня правка scene-routing (маршрутизації сцени) спрацювала:
  - `BattleDetector` тепер передає в snapshot саме fortification-сцену (`empire_town_d`), а не польовий `battle_terrain_*`;
  - `MissionState.OpenNew` на `dedicated server` більше не деградує `SiegeAssault` у польову сцену.
- Новий підтверджений `crash root cause` (коренева причина падіння) локалізовано вже не в scene-bootstrap (запуску сцени), а в пізньому ввімкненні `TeamAI` (бойового ШІ команд):
  - сервер падав у `Team.AddTeamAI(...)`;
  - стек доходив до `TacticDefendCastle -> TeamQuerySystem.RemainingPowerRatio`;
  - у dump (дампі пам’яті) було підтверджено, що в місії відсутній `BattlePowerCalculationLogic` (логіка підрахунку бойової сили), хоча `CasualtyHandler` (облік втрат) уже був.
- Поточна ізольована правка в моді:
  - `Infrastructure/ExactCampaignArmyBootstrap.cs` тепер перед активацією `Siege` / `SallyOut` `TeamAI` примусово перевіряє й, за потреби, додає мінімальні native-передумови:
    - `CasualtyHandler`
    - `BattlePowerCalculationLogic`
  - якщо ці prerequisite behaviors (обов’язкові поведінкові модулі-передумови) не вдалося підняти, `TeamAI` не активується і місія не повинна падати в цьому місці.
- Важлива нова неоднозначність, яку вже підтверджено декомпіляцією native-коду:
  - `SandBoxMissions.OpenSiegeMissionNoDeployment(...)` у поточній версії гри створює `MissionCombatantsLogic(... MissionTeamAITypeEnum.FieldBattle ...)`, а не `Siege`;
  - тобто наш поточний exact-runtime (точний runtime перенесення) усе ще відрізняється від native no-deployment siege-контракту по командному AI;
  - це поки що не виправлялося в коді цим кроком, бо це вже окрема архітектурна зміна з вищим ризиком регресії.
- Що це означає практично:
  - поточний крок закриває відому точку серверного падіння під час активації siege `TeamAI`;
  - але він не означає, що spawn у `SiegeAssault` уже приведений до native-поведінки;
  - проблема спавну всередині міста залишається окремим відкритим blocker (блокером) і тепер її треба розбирати через розрив між нашим hybrid runtime (гібридним runtime) і native no-deployment siege-контрактом, а не через map scene routing.

## Оновлення після вирівнювання `SiegeAssault` під native `no deployment` контракт (2026-06-12)

- Для `SiegeAssault` прийнято й закодовано окреме архітектурне рішення:
  - зовнішній штурм облоги більше не намагається жити як `MissionTeamAIType=Siege`;
  - замість цього `ExactCampaignArmyBootstrap` і `CampaignMapPatchMissionInit` тепер вирівнюють такий сценарій під native `OpenSiegeMissionNoDeployment(...)` семантику;
  - на практиці це означає `MissionTeamAITypeEnum.FieldBattle` для `SiegeAssault`, а не `Siege`.
- Чому це зроблено:
  - поточний coop-runtime (кооперативний runtime) для міської облоги все ще піднімається в `MultiplayerBattle` shell (мультиплеєрній оболонці місії) з гібридним набором mission teams (команд місії);
  - пізнє примусове ввімкнення native siege `TeamAI` на такому runtime вело до server crash (серверного падіння) у `BattlePowerCalculationLogic -> TacticDefendCastle`.
- Який ефект очікується від цього кроку:
  - сервер більше не повинен падати саме на шляху активації siege `TeamAI` для `SiegeAssault`;
  - `SallyOut`, `BlockadeSallyOut`, `Relief`, `LordsHall` і `Blockade` цим кроком не переводяться на нову семантику й лишаються на своїх окремих контрактах.
- Що цей крок свідомо НЕ закриває:
  - він сам по собі не гарантує правильний spawn contract (контракт спавну) атакуючих поза стінами й оборонців на стінах;
  - якщо після цієї правки спавн і далі піде в місті, наступний root cause (коренева причина) вже буде шукатися в `spawn path`/`deployment plan` (шляху спавну й плані розгортання), а не в `TeamAI`.

## Підтипи облог і їхній поточний runtime-стан

### SiegeAssault

- Визначається як `battle.IsSiegeAssault == true`, якщо це не `LordsHall`.
- Працює як зовнішня siege-місія через загальний `exact campaign bootstrap` (точний bootstrap перенесення кампанійного бою).
- Не використовує окремий custom controller (власний контролер місії), а спирається на native `MissionAgentSpawnLogic` (рідну логіку spawn агентів) плюс наш exact-transfer layer (шар точного перенесення стану).
- Для `no deployment` шляху тепер вирівнюється під `MissionTeamAITypeEnum.FieldBattle`, а не під `MissionTeamAITypeEnum.Siege`.
- Поточний live-launch (живий шлях запуску) на `dedicated server` підтверджено як `MissionState.OpenNew(... "MultiplayerBattle" ...)` через native `MultiplayerMissions.OpenBattleMission(...)`, а не як окремий `MultiplayerSiege`-запуск.
- Через це зовнішня `SiegeAssault` зараз фактично живе в `hybrid shell` (гібридній оболонці місії) між campaign `OpenSiegeMissionNoDeployment(...)` і official `MultiplayerBattle`.
- Використовує загальний `battle result` і `writeback` pipeline.

### SallyOut

- Визначається як `battle.IsSallyOut == true`.
- Для mission AI (бойового ШІ місії) переводиться в `MissionTeamAITypeEnum.SallyOut`.
- Для spawn contract (контракту створення військ) використовує `BattleSpawnLogic.SallyOutTag` і `Mission.BattleSizeType.SallyOut`.
- Працює через загальний `ExactCampaignArmyBootstrap`.

### BlockadeSallyOut

- Визначається як `battle.IsBlockadeSallyOut == true`.
- На рівні runtime зараз прирівнюється до `SallyOut`, тобто використовує ту саму `SallyOut`-орієнтовану spawn/AI гілку.
- Потребує окремого live-підтвердження прогонами, що native-семантика блокадної вилазки не має прихованих відмінностей від звичайної `SallyOut`.

### Relief

- Визначається як `battle.IsSiegeOutside == true`.
- Для spawn contract використовує `BattleSpawnLogic.ReliefForceAttackTag`.
- Працює через загальний `ExactCampaignArmyBootstrap`, але з окремою subtype-aware (чутливою до підтипу) конфігурацією стартового spawn.
- Потребує live-перевірки, що розкладка сторін і `winner/aftermath semantics` (семантика переможця й післябойового результату) повністю збігаються з native-поведінкою.

### LordsHall

- Визначається як:
  - `battle.IsSiegeAssault == true` разом із `Settlement.SiegeState.InTheLordsHall`; або
  - через keep-scene heuristic (евристику keep-сцени), якщо сцена вже схожа на indoor keep fight.
- Native Bannerlord для цього сценарію використовує окрему indoor-місію на кшталт `OpenSiegeLordsHallFightMission(...)`.
- У нашому runtime цей сценарій уже переведений на окремий `CoopExactCampaignLordsHallMissionController`.
- `MissionTeamAIType` для `LordsHall` зараз іде як `NoTeamAI`.
- Склад для місії береться не з усього бойового snapshot-а, а лише з `MissionReadyEntryOrder`.
- Ліміти indoor-складу вже закладені в runtime:
  - defender cap (верхня межа захисників): `27`
  - attacker cap (верхня межа нападників): `19`
- Підтягування підкріплень attacker's side (сторони нападника) вже іде через окрему indoor-логіку controller-а.
- `Allowed roster`, `selection`, `battle result seeding` і `fallback XP` уже підрівняні під indoor-склад місії.

### Blockade

- Визначається як `battle.IsBlockade == true`.
- Це не звичайна наземна облога й не звичайна land mission (наземна місія).
- Поточна стратегія модa: не намагатися відкривати `Blockade` через той самий runtime, що для `SiegeAssault` / `SallyOut` / `Relief`.
- У `ExactCampaignArmyBootstrap` для `Blockade` уже стоїть guarded refusal (навмисна відмова від запуску land-mission контракту).
- Це усвідомлене архітектурне рішення, а не незавершена випадковість.

## Потік даних і виконання

### 1. Campaign layer (рівень кампанії)

Ключові файли:

- `Campaign/BattleDetector.cs`
- `Network/Messages/BattleStartMessage.cs`
- `Infrastructure/BattleSnapshotRuntimeState.cs`

Що вже відбувається:

- `BattleDetector` визначає, що бій є siege-сценарієм.
- `ResolveSiegeSubtype(...)` розкладає його на конкретний підтип.
- `BuildSiegeContextSafe(...)` наповнює:
  - `SiegeSubtype`
  - `SettlementId`
  - `SettlementKind`
  - `SceneLocationId`
  - `CurrentSiegeState`
  - `WallLevel`
  - `WallHitPointRatios`
  - список підготовлених/активних siege engines
- `BuildMissionReadyEntryOrder(...)` викликає native `MakeReadyForMission(...)` і будує `MissionReadyEntryOrder` на базі реально виділеного місією складу.
- Далі це все потрапляє в `BattleStartMessage` і в `BattleSnapshotRuntimeState`.

### 2. Dedicated server layer (рівень виділеного сервера)

Ключові файли:

- `Infrastructure/CampaignMapPatchMissionInit.cs`
- `Infrastructure/ExactCampaignArmyBootstrap.cs`
- `Patches/MissionStateOpenNewPatches.cs`

Що вже відбувається:

- mission init (ініціалізація місії) читає `ScenarioContext` і `SiegeContext`.
- `ResolveMissionTeamAiType(...)` уже розрізняє:
  - `SallyOut` / `BlockadeSallyOut` -> `SallyOut`
  - `LordsHall` / `Blockade` -> `NoTeamAI`
  - інші siege-сценарії лишаються на своїй загальній гілці
- `ExactCampaignArmyBootstrap.TryResolveBootstrapScenarioContract(...)` уже розрізняє підтипи облоги й обирає:
  - загальний native spawn logic path (шлях через рідну spawn-логіку) для зовнішніх облогових боїв;
  - окремий `LordsHallController` path (шлях через indoor-контролер) для `LordsHall`;
  - guarded no-mission path для `Blockade`.

### 3. Mission layer (рівень місії)

Ключові файли:

- `Mission/CoopMissionBehaviors.cs`
- `Mission/CoopExactCampaignLordsHallMissionController.cs`
- `Infrastructure/ExactCampaignArmyBootstrap.cs`

Що вже відбувається:

- Для `SiegeAssault`, `SallyOut`, `BlockadeSallyOut` і `Relief` runtime іде через exact bootstrap поверх native mission spawn logic.
- Для `LordsHall`:
  - місія запускає окремий indoor-controller;
  - спавнить defenders (захисників) по indoor area markers (маркерних зонах);
  - спавнить attackers (нападників) обмеженим складом;
  - керує pullback logic (логікою відходу захисників між зонами);
  - окремо керує reinforcement gate (вмиканням підкріплень).
- `CoopMissionBehaviors.RefreshAllowedTroopsFromRoster(...)` уже обмежує `allowed control roster` до `MissionReadyEntryOrder` саме для `LordsHall`.
- `TryCompleteBattleIfResolved(...)` і `TryWriteBattleResultSnapshot(...)` уже працюють і для siege-runtime.

### 4. Writeback layer (рівень запису результатів назад)

Ключові файли:

- `Mission/CoopMissionBehaviors.cs`
- `Campaign/BattleDetector.cs`
- `Infrastructure/CoopBattleResultBridgeFile.cs`

Що вже відбувається:

- mission runtime записує `battle_result.json` через `CoopBattleResultBridgeFile`.
- `Campaign/BattleDetector` читає його й запускає `ApplyBattleResultWriteback(...)`.
- Розбір casualties (втрат), XP, heroes, prisoners (полонених), loot (здобичі) і reward projection (проєкції нагород) іде через спільний aftermath pipeline.
- Для `LordsHall` вже додано захист від хибного `fallback XP` по юнітах, яких indoor-місія не заспавнила й не залучала.

## Що вже реалізовано в цій гілці

### Крок 1. Siege-aware scene routing and bootstrap guards

Підсумок:

- siege-контекст уже не губиться на вході в battle runtime;
- `Blockade` уже відсічений від невірного land-mission шляху;
- базовий `scene bootstrap` (запуск сцени) для облоги вже відокремлений від стабільних `field/village` гілок.

Пов’язаний коміт:

- `d68f559`

### Крок 2. SallyOut / BlockadeSallyOut / Relief bootstrap

Підсумок:

- додано subtype-aware розв’язання spawn-тегів і mission AI;
- `SallyOut`, `BlockadeSallyOut` і `Relief` уже мають окрему battle-ініціалізацію, а не падають у загальний польовий сценарій.

Пов’язаний коміт:

- `34fe7f8`

### Крок 3. LordsHall bootstrap

Підсумок:

- додано `CoopExactCampaignLordsHallMissionController`;
- `LordsHall` переведений на окрему indoor-логіку;
- підключено `MissionReadyEntryOrder` як джерело indoor-складу;
- виставлено caps `27/19`;
- `NoTeamAI` і reinforcement control уже інтегровані.

Пов’язаний коміт:

- `26bfa57`

### Крок 4. Runtime stabilization for LordsHall result/writeback alignment

Підсумок:

- `allowed control roster` для `LordsHall` уже урізаний до `mission-ready roster`;
- `fallback XP writeback` уже не розмазує XP по неучасниках indoor-місії;
- це зменшує ризик прихованого drift (розсинхронізації) між mission roster і campaign writeback.

Пов’язаний коміт:

- `e6e3243`

## Критичні файли та підсистеми

Campaign / snapshot:

- `Campaign/BattleDetector.cs`
- `Infrastructure/BattleSnapshotRuntimeState.cs`
- `Network/Messages/BattleStartMessage.cs`

Mission bootstrap:

- `Infrastructure/CampaignMapPatchMissionInit.cs`
- `Infrastructure/ExactCampaignArmyBootstrap.cs`
- `Patches/MissionStateOpenNewPatches.cs`

Mission runtime:

- `Mission/CoopMissionBehaviors.cs`
- `Mission/CoopExactCampaignLordsHallMissionController.cs`

Writeback:

- `Infrastructure/CoopBattleResultBridgeFile.cs`
- `Campaign/BattleDetector.cs`

Build integration:

- `DedicatedServer/CoopSpectatorDedicated.csproj`

## Відомі обмеження і відкриті неоднозначності

- Повний live-proof (живе підтвердження) для `SiegeAssault` по місту і по фортеці ще не зафіксований у цьому циклі робіт.
- Для `SiegeAssault` відкритий уже не лише `MissionTeamAIType`-розрив, а повний `launch/spawn contract mismatch` (розрив між контрактами запуску й спавну) між:
  - campaign `OpenSiegeMissionNoDeployment(...)`;
  - official `MultiplayerBattle`;
  - official `MultiplayerSiege`.
- Поточний `SiegeAssault` усе ще не має підтвердженого native-equivalent spawn contract (еквівалентного native-контракту спавну) для атакуючих поза стінами та оборонців на стінах.
- Для перевірених campaign fortification-scene (кампанійних сцен укріплень) лишається технічна неоднозначність: частина spawn-маркерів може жити не в самій `.xscene`, а у вкладених prefab (заготовках об’єктів) або інших бінарних ресурсах сцени.
- Повний live-proof для `SallyOut`, `Relief` і `BlockadeSallyOut` ще не завершений.
- `Blockade` поки що залишається окремим `non-mission path`; для нього ще не будувався окремий battle-runtime, і це наразі свідоме рішення.
- Потрібно живим прогоном перевірити, чи всі keep / lords hall сцени стабільно містять потрібні `FightAreaMarker`-об’єкти.
- Потрібно живим прогоном перевірити, чи немає `winner-side edge case` (рідкісного краєвого випадку з переможцем) при взаємному вичерпанні сторін в indoor-сценарії.
- Потрібно окремо звірити, що reconnect / повторний вхід не ламає `selection` і `commander handoff` для siege-підтипів.

## Що лишилось протестувати прогонами

### Обов’язкова siege-матриця

- `SiegeAssault` на місті
- `SiegeAssault` на фортеці
- `SallyOut` на місті
- `SallyOut` на фортеці
- `Relief` на місті
- `Relief` на фортеці
- `LordsHall` на місті
- `LordsHall` на фортеці
- `BlockadeSallyOut`
- `Blockade` як `non-mission path`

### Для кожного прогону треба підтвердити

- правильне визначення `siege subtype` (підтипу облоги)
- правильну runtime scene (сцену виконання)
- правильний `MissionTeamAIType`
- правильний `spawn contract`
- правильний `player side / enemy side handoff` (перехід сторони гравця і ворожої сторони)
- правильне `battle completion`
- правильний `battle result writeback`
- коректний вихід із місії назад у campaign

### Обов’язкова regression-перевірка

- `field battle`
- `village battle`

Мета цієї regression-перевірки:

- підтвердити, що siege-ізоляція не розламала вже стабільні battle flows.

## Оновлення після stabilization-кроку для `SiegeAssault` spawn path (2026-06-12)

- Підтверджено, що поточний blocker (блокер) для міської/фортечної `SiegeAssault` уже не в scene routing (маршрутизації сцени) і не в `MissionTeamAIType`.
- Поточний підтверджений розрив тепер такий:
  - runtime уже відкриває правильну fortification-scene (сцену фортифікації);
  - `MissionTeamAIType` для `siege no deployment` (облоги без етапу розстановки) уже вирівняний до `FieldBattle`;
  - але `HasSpawnPath=False`, тобто native spawn path selector (нативний селектор маршрутів спавну) не встигає або не може стабільно ініціалізуватись у потрібний момент.
- Окремо підтверджено другий конфліктний шар:
  - поверх coop siege runtime (кооперативного виконання облоги) продовжувала жити vanilla MP warmup shell (стандартна мережева оболонка прогріву та вибору сторони);
  - саме цей шар вів у `WarmupSpawningBehavior -> FFASpawnFrameBehavior`, тобто в невірний MP spawn flow (мережевий потік спавну), який не відповідає облозі.
- Поточна правка в коді робить дві ізольовані речі:
  - на dedicated server (виділеному сервері) знову вмикає `BattleShellSuppressionPatch`, але вже в safe mode (безпечному режимі), де приглушується native warmup/timer shell (нативна оболонка прогріву/таймера), а старий manual mission-load bypass (ручний обхід завантаження місії) лишається вимкненим;
  - для `SiegeAssault` native exact bootstrap (нативоподібний точний bootstrap армії) і automated battlefield materialization (автоматична матеріалізація поля бою) тепер не мають права продовжувати роботу, поки місія не отримає реальний `spawn path`.
- Практичний наслідок цього кроку:
  - мод більше не повинен ініціалізувати native siege assault spawn (нативний стартовий спавн штурму) на неповному контракті місії;
  - мод більше не повинен паралельно запускати fallback materialization (резервну матеріалізацію) в місті, якщо `spawn path` для `SiegeAssault` ще не готовий.
- Що цей крок ще не доводить без live-прогону:
  - що `HasSpawnPath` тепер стабільно доходить до `True` на реальному міському й фортецькому `SiegeAssault`;
  - що атакуючі вже фактично спавняться поза стінами, а оборонці на стінах;
  - що відключення vanilla MP warmup shell не відкриває новий regressions path (шлях регресії) у переході до спостереження/вселення.

## Оновлення після кроку для `SiegeAssault` early startup pass-through (2026-06-12)

- Підтверджена нова робоча гіпотеза: поточний blocker (блокер) для міської/фортецької `SiegeAssault` сидить ще до spawn path (маршруту спавну), на стику між native startup (нативним стартом місії) і `BattleShellSuppressionPatch` (патчем приглушення нативної бойової оболонки).
- Симптом цього розриву в логах був стабільний:
  - dedicated server (виділений сервер) залишався в `Mode=StartUp`, `ModeReady=False`, `MissionState=Initializing`, `IsLoadingFinished=False`;
  - клієнт через це зависав у `Loading data` і не доходив до готового battle-data contract (контракту готових даних бою).
- Поточна правка ізолює тільки цей ранній етап:
  - у `Patches/BattleShellSuppressionPatch.cs` для `SiegeAssault` на dedicated server доданий server-only startup pass-through (серверний тимчасовий пропуск стартового нативного шляху);
  - пропуск діє лише поки місія ще в `StartUp`, `NewlyCreated` або `Initializing`;
  - після виходу з цього вікна suppression (приглушення) знову працює як раніше.
- Принципово важливо, що цей крок не змінює:
  - `MissionTeamAIType` для `siege no deployment` (облоги без етапу розстановки);
  - `spawn-path gate` (умову очікування маршруту спавну);
  - `materialization guard` (запобіжник матеріалізації), який і далі не дозволяє штучно спавнити армії в місті, поки `HasSpawnPath=False`.
- Практичний сенс цього кроку:
  - дати native warmup/timer startup (нативному старту прогріву і таймера) завершити ранню ініціалізацію місії;
  - прибрати deadlock (взаємне блокування станів), де coop runtime чекає готовності місії, а місія не доходить до готовності через надто раннє suppression.
- Локальна верифікація цього кроку:
  - `dotnet build C:\dev\projects\BannerlordCoopSpectator3\CoopSpectator.csproj -c Release` пройшов без нових errors (помилок);
  - `dotnet build C:\dev\projects\BannerlordCoopSpectator3\DedicatedServer\CoopSpectatorDedicated.csproj -c Release` пройшов без нових errors;
  - лишилися тільки попередні warnings (попередження), які вже були в проєкті.
- Що має підтвердити наступний live-run (живий прогін):
  - чи виходить `SiegeAssault` із `StartUp` і чи зникає вічний `Loading data`;
  - якщо так, чи стає наступним видимим blocker саме `HasSpawnPath=False`, а не ранній mission startup.

## Оновлення після exact launch/spawn-contract дослідження `SiegeAssault` (2026-06-12)

- Нові логи й декомпіляція вже дозволяють зафіксувати точніший root cause (кореневу причину), ніж попередня гіпотеза про `HasSpawnPath=False`.
- Що підтверджено по живому запуску:
  - реальна місія відкривається через `MissionState.OpenNew` з `MissionName=MultiplayerBattle`;
  - `HandlerMethod` у live-логах іде як native `<OpenBattleMission>b__3_0`;
  - тобто поточний runtime для `SiegeAssault` реально стартує в official `MultiplayerBattle`, а не в official `MultiplayerSiege`.
- Що підтверджено декомпіляцією official `MultiplayerBattle`:
  - `MultiplayerMissions.OpenBattleMission(...)` створює `MissionMultiplayerFlagDomination`;
  - цей шлях використовує `FlagDominationSpawnFrameBehavior` (поведінку вибору точок спавну для battle/TDM-подібної оболонки) і `FlagDominationSpawningBehavior` (поведінку самого мережевого спавну);
  - для initial spawn (початкового спавну) ця гілка шукає `starting`-зону, а не `sp_zone_0`.
- Що підтверджено декомпіляцією official `MultiplayerSiege`:
  - `MultiplayerMissions.OpenSiegeMission(...)` створює `MissionMultiplayerSiege`;
  - цей шлях використовує `SiegeSpawnFrameBehavior` (поведінку вибору точок спавну для мережевої облоги) і `SiegeSpawningBehavior` (поведінку самого мережевого облогового спавну);
  - `SiegeSpawnFrameBehavior` працює через `sp_zone_0`, `spawnpoint`, `attacker`, `defender`.
- Що підтверджено декомпіляцією campaign `siege no deployment` (облоги без етапу розстановки):
  - `SandBoxMissions.OpenSiegeMissionNoDeployment(...)` не використовує `MissionMultiplayerSiege`;
  - він будує місію через `BattleSpawnLogic("battle_set" / "sally_out_set" / "relief_force_attack_set")`;
  - додає `MissionCombatantsLogic(FieldBattle або SallyOut)`, `CampaignSiegeStateHandler`, `CreateCampaignMissionAgentSpawnLogic(...)`, `BattlePowerCalculationLogic`, `SandBoxBattleMissionSpawnHandler`.
- Практичний висновок із цих трьох шарів:
  - поточна проблема спавну більше не описується як "сцена неправильна" або "потрібно лише дочекатися `HasSpawnPath=True`";
  - фактичний розрив у тому, що ми запускаємо campaign `SiegeAssault`-сцену в official `MultiplayerBattle` shell (офіційній мультиплеєрній бойовій оболонці), тоді як native multiplayer siege живе на іншому spawn-контракті й інших scene tags (тегах сцени).
- Що підтверджено по сценах:
  - перевірені campaign fortification-scene `empire_town_d`, `empire_town_j_siege`, `empire_siege_001` містять siege-орієнтовані маркери типу `attacker_wait_pos`, `strategycameraattacker`, `strategycameradefender`, `archer_position_attacker`;
  - текстовий тег `sp_zone_0` знайдено в `Modules\Native\SceneObj\mp_siege_map_*`, але не в перевірених campaign fortification-scenes;
  - це сильний індикатор, що official `MultiplayerSiege` прив'язаний до окремого MP scene contract (контракту сцени мультиплеєрної облоги), а не до звичайних campaign town/castle scenes (кампанійних сцен міста/фортеці).
- Що це означає для нашого обраного напряму `siege no deployment`:
  - правильна ціль для coop spectator (кооперативного спостерігача) зараз не в тому, щоб насильно перевести міську облогу на official `MultiplayerSiege`;
  - правильна ціль у тому, щоб усередині ізольованого coop-шару відтворити саме campaign `OpenSiegeMissionNoDeployment(...)` spawn contract.
- Що це означає для подальшої діагностики:
  - `Mission.HasSpawnPath` більше не можна вважати єдиним або достатнім критерієм готовності `SiegeAssault`;
  - якщо live-місія й далі стартує як `MultiplayerBattle`, наступні правки треба звіряти вже не лише з generic spawn path selector (загальним селектором маршрутів спавну), а з точним campaign no-deployment spawn flow (точним потоком спавну кампанійної облоги без розстановки).

## Оновлення після винесення `SiegeAssaultNoDeployment` в окремий runtime-path (виконуваний шлях) (2026-06-12)

- У коді з’явився окремий helper (допоміжний модуль) `Infrastructure/SiegeAssault/ExactCampaignSiegeAssaultNoDeploymentRuntime.cs`, який ізолює саме зовнішній штурм `SiegeAssault` від інших siege-підтипів.
- Що тепер робиться тільки для `SiegeAssault`:
  - перед `InitWithSinglePhase(...)` примусово викликається `BattleSpawnLogic.OnPreMissionTick(0f)`, щоб підготувати `battle_set` (набір сценових маркерів для бойового розгортання) ще в нашому late bootstrap (пізньому bootstrap-етапі запуску);
  - `spawn horses` (спавн коней) для обох сторін жорстко вимикається, як у native `OpenSiegeMissionNoDeployment(...)`;
  - runtime mode (режим виконання) тепер фіксується окремо як `SiegeAssaultNoDeployment`, а не змішується з загальним `NativeSpawnLogic`.
- Друга ізоляційна правка внесена в `CampaignMapPatchMissionInit.TryRepairLiveMissionContract(...)`:
  - для `SiegeAssault` більше не виконується безумовний `BattleSpawnPathSelector.Initialize()` (перезапуск селектора шляхів спавну);
  - це зроблено свідомо, бо для `no deployment` шляху native-контракт не спирається на `mission.HasSpawnPath` як на обов’язкову передумову;
  - у лог тепер пишеться `SpawnPathRepairSkipped=true/false`, щоб наступний live-прогін (живий прогін) дав точну відповідь, чи цей repair-path (шлях ремонту контракту) ще десь втручається.
- `ExactCampaignArmyBootstrap` тепер додатково:
  - реєструє `BattlePowerCalculationLogic` для `SiegeAssault`, якщо її бракує;
  - запускає для `SiegeAssault` окремий contract snapshot (знімок контракту запуску) з міткою `RuntimeContract={SiegeAssaultNoDeployment}`;
  - використовує спільну runtime-синхронізацію підкріплень і залишків військ для обох spawn-logic path (шляхів, які спираються на нативну spawn-логіку): `NativeSpawnLogic` і `SiegeAssaultNoDeployment`.
- `DedicatedServer/CoopSpectatorDedicated.csproj` також оновлено, щоб серверна збірка реально включала новий `SiegeAssault` helper, а не компілювала старий набір `Infrastructure` без нього.
- Поточний підтверджений технічний стан цього кроку:
  - `dotnet build CoopSpectator.csproj -c Release` проходить успішно;
  - `dotnet build DedicatedServer/CoopSpectatorDedicated.csproj -c Release` проходить успішно;
  - залишаються звичні попередження `MSB3277` по `System.Management` і `CS0162` по unreachable code (недосяжному коду), але нових compile error (помилок компіляції) після цього кроку немає.
- Що ще НЕ підтверджено цим кроком:
  - що атакуючі реально почнуть спавнитись поза стінами, а оборонці на стінах;
  - що `battle_set` на fortification-scene (сцені укріплення) тепер вибирається правильно не лише в коді, а й у live runtime (живому виконанні місії);
  - що `Loading Data` (екран завантаження даних) більше не застрягатиме, якщо попередній blocker (блокер) справді був у старому spawn-path repair (ремонті шляху спавну).

## Оновлення після client `scene contract` crash-дослідження `SiegeAssault` (2026-06-12)

- Підтверджено окрему кореневу проблему саме на client-side (боці клієнта) для `SiegeAssault`:
  - server відкривав `empire_town_d` вже з `SceneLevels=level_1 siege` і `SceneHasMapPatch=True`;
  - client входив у `MissionState.OpenNew(...)` раніше, ніж отримував `CoopMissionNetworkBridge.V2` snapshot;
  - через це `SceneRuntimeClassifier` ще не бачив siege-контекст, `CampaignMapPatchMissionInit.TryApply(...)` виходив до `TryResolveSnapshot(...)`, і місія відкривалась як базове місто без siege-рівня.
- Наслідок підтверджено логами:
  - на client масово йшли `MissionObject ... could not be found`;
  - далі через кілька секунд йшов native crash `0xC0000005`;
  - тобто новий blocker (блокер) був не в самій materialization (матеріалізації бійців), а раніше, у розсинхронізації `scene contract` між server і client.
- Внесена локальна правка:
  - `Infrastructure/CampaignMapPatchMissionInit.cs` тепер вміє рано підняти snapshot з `battle_roster.json` ще до `scene-aware` перевірки;
  - цей prime (раннє підняття знімка стану) спрацьовує тільки якщо runtime snapshot ще порожній, дозволений `local battle roster fallback` (резерв з local battle roster файлу) і `runtimeScene` збігається з `MapScene`/`MultiplayerScene` із snapshot;
  - після цього `TryApply(...)` вже може виставити `SceneLevels=level_N siege` і `SceneHasMapPatch=True` ще до фактичного `MissionState.OpenNew`.
- Межі цієї правки:
  - вона не міняє `field battle` і `village battle`;
  - вона не міняє server bootstrap (серверний bootstrap);
  - вона не доводить, що spawn у місті є вже окремою проблемою, бо раніше client взагалі падав через wrong scene contract (неправильний контракт сцени).

## Оновлення після звірки `SiegeAssaultNoDeployment` з native `OpenSiegeMissionNoDeployment(...)` (2026-06-12)

- Новий live-run (живий прогін) підтвердив, що:
  - місія більше не падає;
  - `scene contract` (контракт сцени) уже відкривається правильно;
  - але `spawn` (точки появи бійців) все ще йде в місті;
  - стіни та ворота можуть спочатку стояти, а потім візуально переходити у зруйнований стан.
- Додаткова звірка з native `IL` (проміжним кодом .NET) показала дві точні розбіжності саме в `SiegeAssaultNoDeployment`:
  - наш coop-runtime додавав `SiegeMissionPreparationHandler` (хендлер підготовки облогових об'єктів), хоча в перевіреному `OpenSiegeMissionNoDeployment(...)` цей шлях не підтверджений;
  - наш coop-runtime не підіймав `CampaignSiegeStateHandler` (хендлер кампанійного стану облоги), хоча native `OpenSiegeMissionNoDeployment(...)` його додає.
- Практичний висновок:
  - для `SiegeAssaultNoDeployment` ми мали `deployment divergence` (розходження з deployment-логікою, тобто з логікою сценарію, де є етап розгортання перед боєм);
  - це добре пояснює окремо і пізнє візуальне руйнування стін/воріт, і те, чому поточний siege runtime (режим виконання облоги) лишався гібридним навіть після стабілізації старту місії.
- Поточний кодовий крок:
  - `ExactCampaignArmyBootstrap` тепер не додає `SiegeMissionPreparationHandler` для `SiegeAssaultNoDeployment`;
  - замість цього для `SiegeAssaultNoDeployment` окремо забезпечується `CampaignSiegeStateHandler` через `reflection` (рефлексію, тобто runtime-створення native типу за іменем без жорсткої compile-time залежності);
  - у `contract snapshot` (знімку контракту запуску) тепер окремо логуються:
    - `SiegeScenePrep={...}`;
    - `SiegeStateHandler={...}`;
    - `SiegeAssaultScenePrep={...}`.
- Що цей крок уже має перевірити наступний live-run:
  - чи зникне пізній візуальний перехід стін і воріт у зруйнований стан;
  - чи зміниться вибір стартових позицій `battle_set` після вирівнювання no-deployment contract (контракту режиму без етапу розгортання);
  - чи залишиться проблема спавну окремим blocker (блокером), уже без домішки deployment-підготовки.

## Оновлення після діагностики `Loading Battle Data` та зриву `ExactCampaignArmyBootstrap` (2026-06-12)

- Новий live-run (живий прогін) уточнив, що поточний blocker уже не в самому `battle snapshot transport` (транспорті мережевого snapshot бою):
  - client отримує `V2 battle snapshot manifest` (маніфест V2 snapshot бою), запитує всі `chunk` (частини), доходить до `TransmissionId=1` і застосовує snapshot;
  - server приймає `range ack` (підтвердження діапазону chunk-ів) і `complete ack` (підтвердження завершення), тобто транспорт snapshot працює до кінця;
  - після цього client більше не падає від відсутнього snapshot, але зависає на `Loading Battle Data`.
- Точний ланцюг відмови тепер підтверджений логами:
  - до `20:07:44` client уже має застосований snapshot;
  - о `20:07:50` server підтверджує `V2 battle snapshot completion` (завершення V2 snapshot бою) і починає слати `EntryStatusSnapshot` (знімок стану вибору/готовності);
  - цей статус приходить як `BattleDataReady=False`, `Lifecycle=NoSide`, `SelectableEntryCount=0`, `CanStartBattle=False`, причина `Loading battle data...`;
  - у той самий момент server намагається активувати `ExactCampaignArmyBootstrap` (точний нативоподібний bootstrap армій), але падає на `CampaignSiegeStateHandler` (хендлері кампанійного стану облоги);
  - через цей зрив runtime не переходить у `SiegeAssaultNoDeployment`, а лишається у старому `FieldBattle shell` (оболонці польового бою), де далі безкінечно працює `spawn path gate` (запобіжник по готовності шляху spawn) з `HasSpawnPath=False`.
- Практичний висновок:
  - `Loading Battle Data` зараз є вже вторинним симптомом;
  - первинний root cause (коренева причина) у тому, що `SiegeAssaultNoDeployment` не добігає до активного exact-bootstrap runtime-path (виконуваного шляху точного bootstrap), бо `CampaignSiegeStateHandler` зриває ініціалізацію на dedicated server (виділеному сервері);
  - старий `spawn path gate` після цього лише консервує місію в `SideSelection` (виборі сторони) і не дає перейти до справжнього assault spawn flow (потоку спавну зовнішнього штурму).
- Додаткова перевірка native-коду:
  - декомпіляція `SandBox.Missions.MissionLogics.CampaignSiegeStateHandler` показала, що його конструктор читає `PlayerEncounter.Battle`;
  - для dedicated server це сильний індикатор, що handler спирається на campaign-local encounter context (локальний кампанійний контекст encounter-події), якого на виділеному сервері може не бути або він може бути неповним.
- Поточний кодовий крок після цієї діагностики:
  - `Infrastructure/ExactCampaignArmyBootstrap.cs` тепер не валить весь `SiegeAssaultNoDeployment` bootstrap, якщо `CampaignSiegeStateHandler` не вдалося підняти саме на dedicated server;
  - для таких відмов handler переводиться у `best-effort` (необов’язковий крок), а не в критичну помилку;
  - одночасно розширена діагностика `TargetInvocationException` (винятку виклику через reflection), щоб лог показував не лише wrapper (зовнішню оболонку винятку), а й `inner exception` (внутрішній виняток).
- Що має перевірити наступний live-run:
  - чи зможе `ExactCampaignArmyBootstrap` тепер реально перейти в активний `SiegeAssaultNoDeployment` runtime-path;
  - чи зникне нескінченний `Loading Battle Data`;
  - чи перестане старий `spawn path gate` бути головним виконуваним шляхом;
  - і лише після цього стане видно, чи є окремий залишковий blocker саме в assault spawn positions (позиціях спавну штурму) або `battle_set` selection (виборі battle_set).

## Оновлення після підтвердження host `SiegeMissionWithDeployment` і розділення `SiegeAssault` shell-path (2026-06-12)

- Нове підтвердження по реальному campaign host (хосту кампанії):
  - для поточного міського штурму host відкриває не `SiegeMissionNoDeployment`, а саме `SiegeMissionWithDeployment`;
  - отже попереднє зведення всіх `SiegeAssault` у `SiegeAssaultNoDeployment` було архітектурно неправильним для цієї гілки облоги;
  - саме це пояснює, чому server/client починали жити в різних `scene contract` (контрактах сцени): host мав deployment-shell (оболонку з фазою розгортання), а coop-runtime на dedicated server (виділеному сервері) насильно зводив той самий сценарій у no-deployment path (шлях без фази розгортання).
- Ізольований кодовий крок цього етапу:
  - у `Network/Messages/BattleStartMessage.cs` та `Infrastructure/BattleSnapshotBinarySerializer.cs` додано нове поле `MissionShell` (тип нативної місії для siege-сценарію);
  - schema version (версію схеми) snapshot піднято з `4` до `5`, щоб `MissionShell` стабільно передавався між campaign, dedicated server і client;
  - у `Patches/MissionStateOpenNewPatches.cs` додано capture (захоплення) реального host mission shell у момент `MissionState.OpenNew(...)`;
  - для цього з’явився окремий `Infrastructure/CampaignMissionShellRuntimeState.cs`, який короткоживуче зберігає лише підтверджені siege-shell значення `SiegeMissionWithDeployment` або `SiegeMissionNoDeployment`;
  - у `Campaign/BattleDetector.cs` цей shell тепер підтягується в `BattleSiegeContextMessage`, тобто siege snapshot більше не втрачає інформацію про те, яку саме native облогу відкрив host.
- Розділення runtime-path (шляхів виконання) тепер стало явним:
  - `Infrastructure/SiegeAssault/ExactCampaignSiegeAssaultNoDeploymentRuntime.cs` більше не вважає `SiegeMissionWithDeployment` своїм сценарієм;
  - додано новий файл `Infrastructure/SiegeAssault/ExactCampaignSiegeAssaultWithDeploymentRuntime.cs`;
  - `ExactCampaignArmyBootstrap` і `CampaignMapPatchMissionInit` тепер розводять `SiegeAssaultWithDeployment` та `SiegeAssaultNoDeployment` окремо, а не через одну спільну гілку.
- Що тепер робиться саме для `SiegeAssaultWithDeployment`:
  - mission team AI (тип командного AI сторін) переводиться в `Siege`, а не в `FieldBattle`;
  - більше не пропускається `spawn path repair` (ремонт шляху spawn), який раніше спеціально скипався лише для no-deployment path;
  - піднімається окремий deployment behavior contract (контракт поведінок для фази розгортання): `MissionSiegeEnginesLogic`, `SiegeDeploymentHandler`, `SiegeDeploymentMissionController`;
  - spawn horses (спавн коней) як і раніше примусово вимикається для обох сторін;
  - bootstrap log (лог ініціалізації) тепер фіксує окремий `RuntimeContract={SiegeAssaultWithDeployment}`.
- Що залишається непідтвердженим після цього кроку:
  - чи дасть цей shell-split (розділення оболонок місії) правильний spawn атакуючих поза стінами, а оборонців на стінах;
  - чи зникне `Loading Battle Data`, якщо його джерелом був саме попередній no-deployment/runtime mismatch (розрив між no-deployment шляхом і реальною runtime-оболонкою);
  - чи достатньо поточного deployment behavior contract без окремої емуляції full deployment phase (повної фази розгортання).
- Поточний технічний стан етапу:
  - `dotnet build CoopSpectator.csproj -c Debug` проходить успішно;
  - `dotnet build DedicatedServer/CoopSpectatorDedicated.csproj -c Debug` проходить успішно;
  - нових compile error (помилок компіляції) на цьому кроці не з’явилось;
  - залишаються старі попередження `MSB3277` по `System.Management` і `CS0162` по unreachable code (недосяжному коду).

## Оновлення після явного побудування `deployment plan` для `SiegeAssaultWithDeployment` (2026-06-12)

- Новий крок ізолює проблему саме в `SiegeAssaultWithDeployment` (штурмі облоги з deployment phase, тобто з фазою розстановки) і не змінює `field battle` (польовий бій), `village battle` (бій у селі), `SallyOut` (вилазку), `Relief` (бій деблокади) чи `LordsHall` (бій у залі лорда).
- Що саме змінено в коді:
  - у `Infrastructure/SiegeAssault/ExactCampaignSiegeAssaultWithDeploymentRuntime.cs` додано окремий `deployment plan contract builder` (побудовник контракту плану розстановки), який:
    - бере війська прямо з `IMissionTroopSupplier` (постачальника військ місії);
    - рахує формації та mounted/foot composition (склад піших і кінних) по кожній місійній команді;
    - примусово будує `MakeDeploymentPlan` і `MakeReinforcementDeploymentPlan` для всіх бойових команд місії;
    - жорстко ставить `SetSpawnWithHorses(..., false)` для siege assault (облогового штурму), щоб кіннота не заходила в сцену верхи.
  - у `Infrastructure/ExactCampaignArmyBootstrap.cs` цей крок тепер викликається лише в гілці `SiegeAssaultWithDeployment` перед `InitWithSinglePhase(...)`.
- Чому це зроблено:
  - native `MissionAgentSpawnLogic.InitWithSinglePhase(...)` (рідна ініціалізація однієї фази спавну) не будує `deployment plan` сам по собі;
  - побудова плану відбувається пізніше в `CheckDeployment()` (внутрішній перевірці готовності розстановки), але для цього місія вже має мати коректні `team plans` (плани команд) і придатні troop counts (кількості військ по формаціях);
  - попередня наша логіка гарантувала лише наявність порожніх `team plans`, але не заповнювала їх військами.
- Яку гіпотезу перевіряє наступний live-run (живий прогін):
  - чи підуть атакуючі в зовнішні siege spawn positions (позиції спавну облоги зовні стін), а не в міський центр;
  - чи підуть оборонці на стіни/внутрішні defensive positions (оборонні позиції), а не в ті самі міські точки;
  - чи зникне зависання `Loading Battle Data` (екрана завантаження бойових даних), якщо його причиною був саме порожній/непобудований deployment-контракт.
- Поточний технічний стан після цього кроку:
  - `dotnet build CoopSpectator.csproj -c Release` пройшов успішно;
  - `dotnet build DedicatedServer/CoopSpectatorDedicated.csproj -c Release` пройшов успішно;
  - нових compile errors (помилок компіляції) не з’явилось;
  - залишились старі попередження `MSB3277` по `System.Management` і `CS0162` по unreachable code (недосяжному коду).

## Короткий висновок

На поточний момент siege-система вже не знаходиться в стадії "немає архітектури". Архітектурний каркас уже є:

- облоги відокремлені від `field battle` і `village battle`;
- підтипи облог уже класифікуються окремо;
- `SallyOut`, `Relief` і `LordsHall` уже мають окремі runtime-рішення;
- `Blockade` уже відсічений від неправильного шляху;
- `LordsHall` уже доведений до стану, де roster, control і writeback узгоджені між собою.

Головне, що лишилось до наступного етапу, це не нова велика реалізація, а системний пакет `end-to-end` прогонів по всій siege-матриці.
