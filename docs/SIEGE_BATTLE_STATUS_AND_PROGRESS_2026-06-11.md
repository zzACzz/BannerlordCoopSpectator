# Siege Battle Working Status (2026-06-13)

## Мета документа

Це робочий документ по siege system (системі облог) у `BannerlordCoopSpectator3`.

Він має містити лише:

- поточні підтверджені факти;
- спростовані гіпотези, які більше не можна використовувати як основу для рішень;
- відкриті питання;
- наслідки для наступного плану впровадження.

Документ не ведеться як історичний журнал усіх проміжних здогадок.

## Межі документа

Документ покриває лише siege-підтипи:

- `SiegeAssault` (основний штурм облоги);
- `SallyOut` (вилазка);
- `BlockadeSallyOut` (вилазка під час блокади);
- `Relief` (бій зовні під час деблокади);
- `LordsHall` (бій у залі лорда);
- `Blockade` (блокада як non-mission path, тобто шлях без запуску звичайної наземної місії).

`Field battle` (польова битва) і `village battle` (бій у селі) лишаються окремими стабільними системами й тут розглядаються тільки як regression boundary (межа регресійної перевірки).

## Поточний підтверджений стан

- Для поточного міського штурму campaign host (хост кампанії) відкриває саме `SiegeMissionWithDeployment`, а не `SiegeMissionNoDeployment`.
- Поточна runtime scene (сцена виконання) правильно визначається як `empire_town_d`, тобто проблема більше не в scene routing (маршрутизації сцени) до `battle_terrain_*`.
- Snapshot transport (транспорт знімка стану) між campaign, dedicated server і client працює: клієнт отримує `V2 battle snapshot payload`.
- Перед exact bootstrap (точною нативоподібною ініціалізацією) dedicated server уже встигає відновити `Mission.PlayerTeam` і `Mission.PlayerEnemyTeam`.
- Поточний основний blocker (блокер) для live-прогону `SiegeAssaultWithDeployment` виникає раніше за побудову `deployment plan` (плану розгортання): сервер валиться на `SiegeDeploymentMissionController`.
- Після цього збою система скочується в старий fallback path (резервний шлях виконання) з `HasSpawnPath=False`, через що materialization (матеріалізація бійців) не доходить до бойової готовності.
- Клієнт зависає на `Loading Battle Data` не через втрату snapshot, а як вторинний наслідок того, що сервер не добудовує authoritative runtime state (авторитетний стан виконання).

## Джерела підтвердження

Ключові свіжі джерела:

- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_46748.txt`
- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_43604.txt`
- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_48820.txt`
- `C:\ProgramData\Mount and Blade II Bannerlord\logs\watchdog_log_43604.txt`
- `C:\dev\projects\BannerlordCoopSpectator3\tmp_sandboxmissions_ildasm.txt`
- `C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\ExactCampaignArmyBootstrap.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\SiegeAssault\ExactCampaignSiegeAssaultWithDeploymentRuntime.cs`

Ключові підтверджені логові точки:

- host:
  - `rgl_log_46748.txt:4115` -> `CampaignMissionShellRuntimeState: captured siege mission shell. MissionShell=SiegeMissionWithDeployment`
  - `rgl_log_46748.txt:4117` -> `Opening new mission SiegeMissionWithDeployment`
  - `rgl_log_46748.txt:4914` -> `RuntimeScene=empire_town_d Terrain=SiegeAssault`
- dedicated:
  - `rgl_log_43604.txt:50022` -> `AppliedPlayerTeam=Attacker#2 AppliedPlayerEnemyTeam=Defender#3`
  - `rgl_log_43604.txt:50034` -> optional skip `CampaignSiegeStateHandler` на dedicated server
  - `rgl_log_43604.txt:50036` -> `SiegeDeploymentMissionController={Existing=False Created=False Reason=NullReferenceException...}`
  - `rgl_log_43604.txt:50054` -> `HasSpawnPath=False`
  - `rgl_log_43604.txt:50056` -> deferred battlefield materialization
  - `rgl_log_43604.txt:49934` -> `AuthoritativeMaterializedAgentEntrySnapshot ... EntryCount=0`
- client:
  - `rgl_log_48820.txt:6317` -> `applied V2 battle snapshot payload on client`
  - `rgl_log_48820.txt:25919` -> `BattleDataReady=False ... AuthoritativeMaterializedAgentEntryCount=0`
  - численні `SynchronizeMissionObject` defer-повідомлення після цього

## Поточний стан по siege-підтипах

### SiegeAssault

- `SiegeAssault` більше не вважається одним сценарієм.
- Для нього вже існують окремі runtime-path (шляхи виконання):
  - `SiegeAssaultWithDeployment`
  - `SiegeAssaultNoDeployment`
- Поточний live reproducer (відтворюваний живий сценарій) для міського штурму йде через `SiegeAssaultWithDeployment`.
- Саме цей шлях зараз має критичний blocker у фазі bootstrap.
- `SiegeAssaultNoDeployment` лишається окремим кодовим шляхом, але в поточному документі немає нового live-підтвердження, що саме він використовується для останнього міського прогону.

### SallyOut

- Ізольований у власний subtype-aware path (шлях, чутливий до підтипу).
- Для mission AI (бойового ШІ місії) іде через `MissionTeamAITypeEnum.SallyOut`.
- Для spawn contract (контракту створення військ) використовує `BattleSpawnLogic.SallyOutTag`.
- Поточних свіжих live-регресій у цьому документі не зафіксовано.

### BlockadeSallyOut

- На рівні runtime зараз іде через ту саму гілку, що й `SallyOut`.
- Потребує окремого live-proof (живого підтвердження), що нативна семантика блокадної вилазки не має додаткових відмінностей.

### Relief

- Має окремий subtype-aware spawn path.
- Використовує `BattleSpawnLogic.ReliefForceAttackTag`.
- Лишається на загальному `ExactCampaignArmyBootstrap`, але не змішується з `SiegeAssault`.

### LordsHall

- Уже ізольований в окремий `CoopExactCampaignLordsHallMissionController`.
- Не залежить від зовнішнього siege spawn shell.
- Використовує indoor roster (внутрішній склад місії) з `MissionReadyEntryOrder`.
- Поточний документ не виявив нових blocker для цього сценарію.

### Blockade

- Навмисно лишається окремим `non-mission path`.
- Не повинен запускатися як звичайна наземна місія через той самий bootstrap, що `SiegeAssault`, `SallyOut` чи `Relief`.

## Native launch sequence для `SiegeAssaultWithDeployment`

Декомпіляція `SandBoxMissions.OpenSiegeMissionWithDeployment(...)` показує такий підтверджений порядок:

1. Створюється `BattleSpawnLogic`.
2. Додаються базові mission behavior (місійні компоненти): `MissionOptionsComponent`, `CampaignMissionComponent`, `BattleEndLogic`, `BattleReinforcementsSpawnController`.
3. Додається `MissionCombatantsLogic`.
4. Додається `SiegeMissionPreparationHandler`.
5. Додається `CampaignSiegeStateHandler`.
6. Додається `SandBoxSiegeMissionSpawnHandler` або інший підтиповий controller залежно від сценарію.
7. Створюється `CreateCampaignMissionAgentSpawnLogic(...)`, тобто native `DefaultBattleMissionAgentSpawnLogic`.
8. Додаються battle/runtime logic (бойові та runtime-компоненти): `BattlePowerCalculationLogic`, `BattleObserverMissionLogic`, `BattleAgentLogic`, `BattleSurgeonLogic`, `MountAgentLogic`, `BannerBearerLogic`, `AgentHumanAILogic`, `AmmoSupplyLogic`, `AgentVictoryLogic`, `AssignPlayerRoleInTeamMissionController`, `SandboxGeneralsAndCaptainsAssignmentLogic`, `MissionAgentPanicHandler`, `MissionBoundaryPlacer`, `MissionBoundaryCrossingHandler`, `AgentMoraleInteractionLogic`, `HighlightsController`, `BattleHighlightsController`, `EquipmentControllerLeaveLogic`.
9. Лише після цього додаються siege deployment behavior:
   - `MissionSiegeEnginesLogic`
   - `SiegeDeploymentHandler`
   - `SiegeDeploymentMissionController`

Ключовий факт: native код піднімає `DefaultBattleMissionAgentSpawnLogic` раніше за `SiegeDeploymentHandler` і `SiegeDeploymentMissionController`.

## Поточний dedicated bootstrap sequence для `SiegeAssaultWithDeployment`

Поточний код на dedicated server іде так:

1. `ExactCampaignArmyBootstrap` читає scenario/siege context.
2. Піднімає `SiegeMissionPreparationHandler`, якщо це потрібно для siege-сцени.
3. Пробує підняти `CampaignSiegeStateHandler`, але на dedicated server це зараз best-effort step (необов'язковий крок).
4. Занадто рано викликає `ExactCampaignSiegeAssaultWithDeploymentRuntime.TryEnsureMissionBehaviorContract(...)`.
5. Цей helper додає:
   - `MissionSiegeEnginesLogic`
   - `SiegeDeploymentHandler`
   - `SiegeDeploymentMissionController`
6. Той самий helper одразу вручну викликає `OnBehaviorInitialize()` і `AfterStart()`.
7. Лише пізніше `ExactCampaignArmyBootstrap` створює native `DefaultBattleMissionAgentSpawnLogic`.
8. Ще пізніше додається `BattleReinforcementsSpawnController`.
9. Лише після цього код доходить до `TryPrepareDeploymentPlanContract(...)` і `InitWithSinglePhase(...)`.

Ключовий факт: у нас `SiegeDeploymentMissionController` створюється й стартує до того, як у місії гарантовано є native spawn logic у тому самому порядку, який очікує native `SiegeMissionWithDeployment`.

## Підтверджені розбіжності між native і dedicated

### Розбіжність 1. Порядок bootstrap

- Native `spawn logic` створюється раніше.
- У нас `deployment behavior` створюються раніше.
- Це підтверджено і декомпіляцією, і кодом.

### Розбіжність 2. Ручний lifecycle

- Наш helper `TryEnsureMissionBehaviorAvailable(...)` не тільки додає behavior у місію, а й одразу викликає:
  - `OnBehaviorInitialize()`
  - `AfterStart()`
- Це означає, що поведінка стартує в поточному live-runtime, а не в тому порядку, у якому її б піднімав native `MissionState.OpenNew(...)`.

### Розбіжність 3. Optional skip `CampaignSiegeStateHandler`

- На dedicated server `CampaignSiegeStateHandler` зараз не є обов'язковим, бо його підйом падає на `FileNotFoundException` по `TaleWorlds.CampaignSystem`.
- Для поточного прогону це вже не є первинною точкою падіння, бо bootstrap іде далі.
- Але це лишається підтвердженою відмінністю від campaign-host шляху.

### Розбіжність 4. Сервер надсилає порожній battle readiness state

- `AuthoritativeMaterializedAgentEntrySnapshot` з `EntryCount=0` і `EntryStatusSnapshot` відправляються до того, як bootstrap стає готовим.
- Це пояснює, чому клієнт формально отримує дані, але не переходить у готовий бій.

## Спростовані або застарілі гіпотези

Ці гіпотези більше не можна використовувати як основу для нових рішень:

- `Поточний міський SiegeAssault працює через SiegeMissionNoDeployment`
  - спростовано; для поточного host-прогону підтверджено `SiegeMissionWithDeployment`.
- `Поточна причина спавну в місті ще в routing на battle_terrain_*`
  - спростовано; сцена вже коректно йде як `empire_town_d`.
- `Поточний blocker це transport snapshot між server і client`
  - спростовано; клієнт отримує й застосовує `V2 battle snapshot payload`.
- `Поточний blocker це відсутність Mission.PlayerTeam перед bootstrap`
  - спростовано для цього прогону; у логах перед exact bootstrap уже є `AppliedPlayerTeam=Attacker#2`.
- `Порожній deployment plan є першою точкою відмови`
  - застаріло як первинне пояснення для поточного прогону; код навіть не доходить до нового побудовника `deployment plan`.
- `Головна проблема поточного міського штурму в MissionTeamAIType=Siege`
  - застаріло як головне пояснення саме для цього live-run; тепер точка зриву раніша і сидить на `SiegeDeploymentMissionController`.

## Поточний робочий root cause

Найсильніше підтверджене поточне пояснення таке:

- `SiegeAssaultWithDeployment` на dedicated server порушує native order (нативний порядок) створення mission behavior.
- Через це `SiegeDeploymentMissionController` стартує в неправильному lifecycle context (контексті життєвого циклу), ще до того, як місія приведена до очікуваного native deployment state.
- Саме це найкраще пояснює:
  - `NullReferenceException` у `SiegeDeploymentMissionController`;
  - відкат у fallback path;
  - `HasSpawnPath=False`;
  - `EntryCount=0`;
  - клієнтський `Loading Battle Data`.

Це ще не "закрита" коренева причина в сенсі повної перевірки фіксом і новим прогоном, але це вже найточніший робочий висновок на поточних даних.

## Secondary symptoms після primary failure

Нижче симптоми, які зараз не виглядають первинною причиною:

- `Loading Battle Data` на клієнті;
- масові `SynchronizeMissionObject` defer-повідомлення;
- відсутність готових authoritative agent entries;
- спавн у місті замість зовнішніх siege-позицій;
- візуальний drift (розсинхрон) по стінах і воротах.

Їх треба оцінювати повторно лише після того, як `SiegeAssaultWithDeployment` перестане валитися на bootstrap.

## Наслідки для поточного плану

Попередній план у цілому лишається правильним, але тепер він став точнішим.

Що треба робити першим:

1. Перебудувати `SiegeAssaultWithDeployment` bootstrap так, щоб `DefaultBattleMissionAgentSpawnLogic` і `BattleReinforcementsSpawnController` з'являлися раніше за `MissionSiegeEnginesLogic`, `SiegeDeploymentHandler` і `SiegeDeploymentMissionController`.
2. Лишити цю зміну ізольованою тільки для `SiegeAssaultWithDeployment`.
3. Після цього повторити live-run і тільки тоді перевіряти:
   - чи працює зовнішній спавн атакуючих;
   - чи оборонці стають на defensive positions (оборонні позиції);
   - чи лишається проблема з `SynchronizeMissionObject`.

Що поки не треба робити як перший крок:

- не переписувати весь spawn placement logic (логіку розстановки спавну);
- не міняти `SallyOut`, `Relief`, `LordsHall` або `field/village` flows;
- не робити нові висновки про `deployment plan`, поки bootstrap не доходить до цього етапу.

## Останній кодовий крок (ще без live-підтвердження)

- `ExactCampaignArmyBootstrap` більше не піднімає `MissionSiegeEnginesLogic`, `SiegeDeploymentHandler` і `SiegeDeploymentMissionController` до створення `DefaultBattleMissionAgentSpawnLogic`.
- Для `SiegeAssaultWithDeployment` ці deployment behavior тепер додаються лише після `DefaultBattleMissionAgentSpawnLogic` і `BattleReinforcementsSpawnController`.
- `ExactCampaignSiegeAssaultWithDeploymentRuntime` більше не створює `SiegeDeploymentHandler(false)` і `SiegeDeploymentMissionController(false)` жорстко як defender-oriented path (шлях, орієнтований на захисника).
- Замість цього в обидва behavior передається фактичний `playerSide` (сторона гравця), щоб їхній внутрішній deployment lifecycle ближче відповідав native `SiegeMissionWithDeployment`.
- Цей крок ще не доводить, що зовнішній спавн уже виправлено, але прибирає підтверджений `ordering mismatch` і явну помилку з жорстким defender flag (прапором захисника).

## Відкриті питання

- Чи зникне `NullReferenceException`, якщо просто вирівняти порядок створення behavior під native sequence?
- Чи вистачить лише reorder (перестановки порядку), чи доведеться додатково міняти спосіб ручного виклику `OnBehaviorInitialize()/AfterStart()`?
- Чи є `SynchronizeMissionObject` storm (шторм відкладених синхронізацій) суто вторинним наслідком, чи це окремий blocker наступного рівня?
- Чи буде `SiegeAssaultNoDeployment` потрібний для окремих сценаріїв на кшталт already-breached assault (штурму вже пробитих укріплень)? Поточний документ цього ще не підтверджує.
- Чи потребує окремої синхронізації wall/gate state (стан стін і воріт) після стабілізації bootstrap?

## Критичні файли та підсистеми

Campaign / snapshot:

- `Campaign/BattleDetector.cs`
- `Infrastructure/BattleSnapshotRuntimeState.cs`
- `Infrastructure/BattleSnapshotBinarySerializer.cs`
- `Network/Messages/BattleStartMessage.cs`
- `Infrastructure/CampaignMissionShellRuntimeState.cs`

Dedicated bootstrap:

- `Infrastructure/CampaignMapPatchMissionInit.cs`
- `Infrastructure/ExactCampaignArmyBootstrap.cs`
- `Infrastructure/SiegeAssault/ExactCampaignSiegeAssaultWithDeploymentRuntime.cs`
- `Infrastructure/SiegeAssault/ExactCampaignSiegeAssaultNoDeploymentRuntime.cs`
- `Patches/MissionStateOpenNewPatches.cs`

Mission runtime:

- `Mission/CoopMissionBehaviors.cs`
- `Mission/CoopExactCampaignLordsHallMissionController.cs`

Writeback:

- `Infrastructure/CoopBattleResultBridgeFile.cs`
- `Campaign/BattleDetector.cs`

## Мінімальна siege regression-матриця

Обов'язково лишається перевірити:

- `SiegeAssault` на місті;
- `SiegeAssault` на фортеці;
- `SallyOut` на місті;
- `SallyOut` на фортеці;
- `Relief` на місті;
- `Relief` на фортеці;
- `LordsHall` на місті;
- `LordsHall` на фортеці;
- `BlockadeSallyOut`;
- `Blockade` як `non-mission path`.

Окремо для regression safety (безпеки від регресій):

- `field battle`
- `village battle`

## Короткий висновок

Станом на `2026-06-13` головна проблема поточного live `SiegeAssault` вже не в scene routing, не в snapshot transport і не в загальному `Loading Battle Data`.

Поточний основний blocker сидить у `SiegeAssaultWithDeployment` bootstrap: dedicated server створює й стартує deployment behavior раніше, ніж це робить native `OpenSiegeMissionWithDeployment`.

Саме це зараз є правильною основою для наступного кодового фіксу.
