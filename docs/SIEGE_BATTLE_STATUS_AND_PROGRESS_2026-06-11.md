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
- Для `SiegeAssault` лишається відкрита native-невідповідність між нашим `MissionTeamAIType=Siege` шляхом і native `OpenSiegeMissionNoDeployment(...)`, який у поточній версії гри стартує через `MissionCombatantsLogic(... FieldBattle ...)`.
- Поточний `SiegeAssault` усе ще не має підтвердженого native-equivalent spawn contract (еквівалентного native-контракту спавну) для атакуючих поза стінами та оборонців на стінах.
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

## Короткий висновок

На поточний момент siege-система вже не знаходиться в стадії "немає архітектури". Архітектурний каркас уже є:

- облоги відокремлені від `field battle` і `village battle`;
- підтипи облог уже класифікуються окремо;
- `SallyOut`, `Relief` і `LordsHall` уже мають окремі runtime-рішення;
- `Blockade` уже відсічений від неправильного шляху;
- `LordsHall` уже доведений до стану, де roster, control і writeback узгоджені між собою.

Головне, що лишилось до наступного етапу, це не нова велика реалізація, а системний пакет `end-to-end` прогонів по всій siege-матриці.
