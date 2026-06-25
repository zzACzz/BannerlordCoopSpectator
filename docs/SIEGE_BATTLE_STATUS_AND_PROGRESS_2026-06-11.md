# Стан розробки Siege Battle

Оновлено: 2026-06-25.

Це living status document (живий документ стану), який треба продовжувати вести в нових чатах під час розробки режиму облоги в `BannerlordCoopSpectator3`.

## Актуальний зріз на 2026-06-25

Останній підтверджений добрий стан: commander deployment menu (командирське меню розстановки) для облоги вже підтримує створення формацій, повзунки, миттєве переміщення формацій, нижнє меню наказів F1-F9, кнопки пріоритезації бійців і видимі deployment boundaries (межі розстановки). Після зміни пріоритезації бійці одразу переставляються на актуальні місця без додаткового руху формації. Після виправлення scene preparation split (розділення підготовки сцени між сервером і клієнтом) стіни та баштові секції в `empire_town_d` підтверджено відображаються цілими на клієнті.

Окремо підтверджено новий етап по siege machines (облогових машинах): атакуюча баліста розміщується через меню розстановки, переміщується між точками і після кожного розміщення отримує стрільця. Попередня проблема, коли стрілець з'являвся лише при першому розміщенні або залишався біля старої точки після зняття балісти, більше не відтворюється в останньому прогоні.

Також підтверджено Auto-Deploy (автоматичну розстановку) для атакуючої балісти: після натискання кнопки авто-розстановки баліста з'являється на клієнті без ручного вибору точки і має стрільця. Для цього сервер після фактичного розміщення публікує semantic siege machine state (смисловий стан облогової машини), а клієнт застосовує його через той самий client-side apply path (клієнтський шлях застосування), який уже працював для ручного розміщення.

Останній контрольний commit (фіксація змін) перед поточним етапом:

- `726773e Fix siege deployment boundary visualization`.

### Що зараз підтверджено прогонами

- `SiegeMissionWithDeployment` завантажується до карти без поточного підтвердженого crash (падіння) на старті місії.
- Матеріалізація військ у siege scene (сцені облоги) працює: оборонці з'являються біля або всередині стін, атакуючі - ззовні.
- Стіни, ворота і баштові секції в `empire_town_d` після останнього прогону відображаються цілими, а не як уже зруйновані.
- Логи підтверджують правильний scene preparation split: на сервері `CoopSiegeAssaultWithDeployment` бере `campaign-ratios` (коефіцієнти зі стану кампанії) з `RawValues=[1,1] OutputValues=[1,1]`, а на клієнті є `skipped server-only siege scene preparation on client`.
- Меню вибору сторони і меню вибору бійця працюють для облоги окремо від field battle flow (послідовності польової битви).
- Вибір commander (командира) відкриває campaign-like Order of Battle menu (меню бойового порядку, подібне до кампанії).
- Після входу в меню розстановки камера більше не перемикається на бійців при натисканні миші.
- Активні формації вибираються по одній: при виборі нової формації попередня втрачає активність.
- Повзунки існуючих формацій змінюють кількість бійців у формаціях.
- Вибрані формації переміщуються миттєво в позначене прапорцями місце, як у кампанійному меню розстановки, а не йдуть туди звичайною ходьбою.
- Нові формації створюються через dropdown (випадаючий список), отримують бійців через повзунки і після останнього виправлення теж переміщують своїх бійців.
- Нижнє order menu (меню наказів) у deployment phase (фазі розстановки) відображається як у кампанії: є іконки наказів і підказки клавіш F1-F9.
- Formation priority filters (фільтри пріоритезації формації) працюють: shield (щит), heavy armor (важка броня), low tier/recruits (низький рівень/новобранці), high tier/veterans (високий рівень/ветерани) та інші native filter traits (рідні ознаки фільтрів) передаються на сервер і застосовуються через `TroopFilteringUtilities`.
- Після зміни кнопок пріоритезації native rearrange (рідна перестановка формацій) одразу завершується локальним placement finalize (завершенням розстановки позицій), тому бійці, які вийшли або зайшли у формацію, миттєво стають на свої місця як у кампанії.
- Deployment boundaries (межі розстановки) тепер підтверджено видно на клієнті як прапорці і прозору візуальну стіну. Логічне обмеження позицій працювало раніше, але тепер командир також бачить межі зони, як у кампанійному меню розстановки.
- UI icons (іконки інтерфейсу) для додавання формації і вибору типу формації підтягуються після завантаження потрібних sprite categories (категорій графічних ресурсів).
- Siege machine deployment (розміщення облогових машин) для атакуючої балісти підтверджено працює: командир може поставити балісту, прибрати її і повторно розмістити на іншій доступній точці.
- Ballista operator assignment (призначення стрільця до балісти) підтверджено працює після кожного deploy (розміщення), а не лише після першого.
- Auto-Deploy siege machine deployment (автоматичне розміщення облогових машин) підтверджено працює для атакуючої балісти: після кнопки авто-розстановки баліста з'являється на клієнті зі стрільцем без ручного розміщення.
- Controlled disband (кероване зняття машини з точки) тепер перед приховуванням машини виконує `ReleaseAgents` (явне звільнення агентів): старий `MovingAgent` / `UserAgent` (агент, що йде до точки або використовує її) від'єднується від старої балісти і повертається у формацію.
- Server-side deployment point resolution (серверне зіставлення точки розміщення) виправлено для нестабільних client/server ids (клієнтських і серверних ідентифікаторів): для typed siege machine request (запиту з типом облогової машини) сервер обирає nearest matching point (найближчу відповідну точку) за позицією, тому повторне розміщення більше не відкидається як `skip-invalid-deployment-point`.
- Для актуального siege projection (проєкції облоги) у списку типів залишені тільки релевантні варіанти:
  - infantry (піхота);
  - ranged (стрільці);
  - infantry + ranged (змішана піхота і стрільці).
- Cavalry (вершники) і horse archers (кінні лучники) прибрані з цього меню, бо в поточній облозі коні примусово знімаються і ці типи ламали відповідність між слотами та реальними агентами.

### Поточна архітектура розстановки

- `CoopSiegeOrderOfBattleVM` є safe copy bridge (безпечним мостом-копією) навколо кампанійного `OrderOfBattleVM`, а не прямим запуском небезпечного campaign flow (кампанійної послідовності).
- Composition payload (пакет складу формацій) передає бажані кількості піхоти і стрільців по кожному слоту формації, а також active priority filters (активні фільтри пріоритезації), а не нестабільні client-side agent indexes (клієнтські індекси агентів).
- Server-authoritative sync (серверне остаточне застосування синхронізації) на сервері сам розкладає реальних агентів по формаціях згідно з бажаними кількостями і фільтрами пріоритезації.
- Layout payload (пакет позицій формацій) окремо передає позицію, напрямок і ширину бойової лінії для формації.
- Shadow selection (тіньовий стан вибраних формацій) синхронізує вибір формацій з клієнтського меню у серверний order controller (контролер наказів), щоб рух застосовувався саме до вибраних слотів.
- Для восьми слотів Order of Battle потрібно використовувати `FormationClass.NumberOfRegularFormations`. `FormationClass.NumberOfDefaultFormations` дорівнює чотирьом базовим бойовим класам і не є лімітом слотів меню розстановки.
- Нові формації повинні бути зареєстровані не лише у view model (моделі даних інтерфейсу), а й у bridge/sync path (шляху мосту та синхронізації), інакше вони будуть виглядати наповненими в UI, але не матимуть серверних агентів для руху.
- Поточний composition payload має version 2 (версію 2), де запис формації містить: index (індекс формації), infantry count (кількість піхоти), ranged count (кількість стрільців), infantry filter mask (маску фільтрів піхоти) і ranged filter mask (маску фільтрів стрільців). Version 1 (версія 1) лишена для backward compatibility (зворотної сумісності).
- Filter sync (синхронізація фільтрів) побудована як priority behavior (поведінка пріоритезації), а не hard filtering (жорстке відсікання): якщо бійців з потрібною ознакою не вистачає, рушій добирає інших.
- Boundary visualization (візуалізація меж) розділена з logical clamp (логічним обмеженням позиції). Якщо клієнтський `DeploymentPlan` (план розстановки) ще не має готових меж для малювання, візуальний шар читає scene tags (теги сцени) через `MBSceneUtilities.GetDeploymentBoundaries` і будує ті самі межі з `deployment_castle_boundary...`, які використовує кампанія.
- Для scene-tag fallback (резервного шляху через теги сцени) точки межі проходять через `RadialSortBoundary` (радіальне сортування точок) і `FindConvexHull` (побудову опуклої оболонки), щоб форма контуру відповідала кампанійному способу побудови.
- Siege scene preparation (підготовка сцени облоги) тепер розділена за відповідальністю: `SiegeMissionPreparationHandler` (обробник підготовки облогової сцени) додається лише в server stack (серверний стек поведінок), а client stack (клієнтський стек поведінок) не запускає серверну логіку вибору цілих або зламаних стін.
- `wallHitPointRatios` (коефіцієнти цілісності стін) більше не підміняються штучним forced-intact (примусовим "стіни цілі") у безпечному випадку. Для сцени з двома breakable wall segments (руйнованими сегментами стін) використовуються нормалізовані значення з campaign snapshot (знімка стану кампанії).
- `CoopSiegeMachineDeploymentController` є controlled deployment controller (контролером керованого розміщення облогових машин), який не викликає небезпечний campaign deployment flow (кампанійну послідовність розміщення) напряму, а відтворює потрібну поведінку через безпечні MP-side operations (операції, безпечні для мультиплеєру).
- При deploy (розміщенні) облогової машини контролер синхронізує `DeploymentPoint` (точку розміщення), `SiegeWeapon` (облогову машину), `MissionSiegeWeaponsController` (рідний контролер облогових машин), visible/physics state (видимість і фізичний стан), forced use (примусове використання машини формаціями) і detachment assignment (призначення агента до машини).
- Якщо native `SiegeDeploymentHandler.AutoAssignDetachmentsForDeployment` (рідний обробник автоматичного призначення агентів до машин) недоступний або не може бути викликаний, використовується owned auto assign (власне автоматичне призначення): тимчасово дозволяється AI ticking (тик штучного інтелекту), агенти проходять через `TickAgent`, а `DetachmentManager.TickDetachments` призначає стрільця на доступний слот.
- При clear/disband (очищенні або знятті машини з точки) контролер перед зміною видимості і стану машини явно звільняє агентів із її `StandingPoint` (точки взаємодії): `UserAgent`, `MovingAgent`, `DefendingAgents` (агенти, що використовують, йдуть до або захищають точку) і агентів із `agent.Detachment == siegeWeapon`.
- `CoopMissionNetworkBridge` виконує server-side fallback resolution (серверне резервне зіставлення) для `DeploymentPoint`: якщо переданий `MissionObjectId` (ідентифікатор об'єкта місії) не валідний на сервері, точка зіставляється за позицією та типом облогової машини. Для typed request (запиту з типом машини) пріоритет має найближча відповідна точка будь-якого enabled/disabled state (активного або вимкненого стану), а не перша активна точка.
- Для Auto-Deploy (автоматичної розстановки) `CoopMissionNetworkBridge` додатково публікує `CoopCommanderDeploymentSiegeMachineStateMessage` як server-to-client state message (повідомлення стану від сервера до клієнта). Повідомлення передає side (сторону), `DeploymentPoint` (точку розміщення), позицію точки, `SiegeWeapon` (облогову машину), тип машини і clear flag (ознаку очищення), щоб клієнт міг знайти локальний відповідник навіть при нестабільних `MissionObjectId`.
- Клієнтський handler (обробник) цього стану використовує id-first, fallback-by-position/type resolution (спочатку зіставлення за ідентифікатором, потім резервне зіставлення за позицією і типом) і викликає `TryApplyCommanderDeploymentSiegeMachineSelectionLocally(...)`. Тому ручне розміщення і Auto-Deploy сходяться на одному client-side apply path (клієнтському шляху застосування), а не мають дві різні візуальні реалізації.

### Доктрина для небезпечних кампанійних елементів

Поточний робочий підхід: якщо native campaign component (рідний кампанійний компонент) небезпечно запускати в multiplayer mission shell (мультиплеєрній оболонці місії), його поведінку треба копіювати або відтворювати у моді через безпечний adapter (адаптер сумісності), а не тягнути весь campaign state (кампанійний стан).

Для меню розстановки це означає:

- копіювати лише потрібну поведінку і дані;
- замінювати кампанійні зв'язки на власні MP-safe contracts (контракти, безпечні для мультиплеєру);
- не змішувати siege replay (відтворення облоги) з польовими або village battle (битва в селі) flow.

### Що ще не завершено

- Перевірити `Ready` button flow (послідовність кнопки готовності): після ручної розстановки командир має вселятися у вибраного командира, а місія має коректно перейти далі.
- Довести host start flow (послідовність старту хостом): якщо командир не завершив або командира немає, решта має дорозставитися auto-deploy (автоматичною розстановкою). Машинну частину Auto-Deploy для атакуючої балісти через кнопку вже підтверджено, але host start path (шлях старту хостом) ще треба окремо прогнати.
- Перевірити вселення звичайних гравців після завершення deployment phase (фази розстановки).
- Окремо стабілізувати вихід з lobby (лобі) і multiplayer menu (мультиплеєрного меню), бо раніше там були crash reports (звіти про падіння).
- Базову attacker ballista flow (послідовність роботи балісти атакуючих) підтверджено для ручного розміщення і Auto-Deploy (автоматичної розстановки). Далі треба перевірити інші siege engines (облогові машини), defender-side machines (машини сторони оборони), ladders (драбини), gates/breaches (ворота і проломи) та AI assault behavior (поведінку штурму штучного інтелекту).
- Чорні ділянки сцени ще треба окремо перевірити як scene/material pass (перевірку сцени та матеріалів), але відсутні стіни і баштові секції більше не є підтвердженою проблемою після виправлення scene preparation split.

### Маркери для наступних прогонів

- У логах після зміни складу формацій очікується `CoopCommanderDeploymentFormationAssignments`. Для восьми слотів більше не має бути короткого пакета лише на чотири записи.
- Після створення нової формації і руху прапорцями мають переміщуватися саме її бійці.
- Після зміни повзунка бійці мають перейти у нову формацію на сервері і рухатися разом з нею без додаткових ручних обхідних дій.
- Після натискання priority filter button (кнопки фільтра пріоритезації) очікується негайне переставлення бійців без додаткового руху формації.
- При переміщенні балісти між точками в логах має бути `ReleaseAgents={... Released=1 Attached=1 ...}` для старої точки, якщо біля старої машини вже був стрілець.
- Після нового deploy (розміщення) балісти в логах має бути `AutoAssign={... DetachedAgents=1 ...}` або еквівалентний native auto assign (рідне автоматичне призначення), а біля балісти має стояти стрілець.
- Після натискання Auto-Deploy (автоматичної розстановки) у серверних діагностиках очікується `SiegeMachineStatePublished=True`, а на клієнті - застосування `CoopCommanderDeploymentSiegeMachineStateMessage` через local apply (локальне застосування). Візуально баліста має з'явитися одразу зі стрільцем.
- У `FallbackResolution` для повторного розміщення балісти не має бути `skip-invalid-deployment-point`; очікувані діагностичні маркери: `NearestSelectionReason`, `NearestAnyStrictMatch=True` або інший валідний selected point (обрана точка).

## Призначення

Документ фіксує актуальний стан `SiegeAssault` з `SiegeMissionWithDeployment`, поточну поведінку commander deployment menu, цільову поведінку режиму і наступні технічні кроки.

Це не історичний журнал усіх спроб. Застарілі гіпотези треба прибирати або переносити в блок "Спростовано", якщо вони ще корисні як запобіжник від повторення помилок.

## Кінцева ціль

Зробити coop siege assault (кооперативний штурм облоги) максимально 1:1 як кампанійна битва при осаді стін міста або фортеці.

Цільовий mission shell (оболонка місії) для кампанійного штурму:

- `SiegeMissionWithDeployment`.

Нецільові шляхи:

- `OpenSiegeMissionNoDeployment` - лише reference path (референсний шлях для порівняння), не ціль для 1:1 replay (відтворення один в один).
- `MultiplayerBattle` з вручну доінжектованими siege behavior (поведінками облоги) - відхилений hybrid path (гібридний шлях), бо він змішує field battle flow (послідовність польової битви) з native siege deployment lifecycle (рідним життєвим циклом облогової розстановки).

`SiegeAssault` має лишатися окремим flow (послідовністю роботи), не змішаним із field battle (польовою битвою), village battle (битвою в селі), `SallyOut`, `Relief`, `LordsHall`, `BlockadeSallyOut` або `Blockade`.

## Цільовий gameplay flow

1. Host (хост) запускає кампанійну облогу.
2. Сервер відкриває `SiegeMissionWithDeployment` і входить у `Deployment` phase (фазу розстановки).
3. Клієнти під час завантаження місії обирають сторону, як у польовій битві.
4. Після вибору сторони клієнти обирають бійця з roster (списку доступних записів армії), як у польовій битві.
5. На відміну від польової битви, клієнти не вселяються в бійців одразу.
6. До завершення deployment phase звичайні гравці чекають у стані очікування.
7. У кожної сторони є commander (командир), якщо в roster є валідний командирський запис.
8. Якщо клієнт обирає командира своєї сторони, він входить у native commander deployment flow (рідну послідовність командирської розстановки).
9. Командир бачить campaign-like deployment menu (меню розстановки як у кампанії).
10. Командир може:
    - вручну розставити війська;
    - натиснути auto deploy (автоматична розстановка).
11. Після завершення deployment commander вселяється у свого бійця.
12. Після завершення deployment звичайні клієнти вселяються у вибраних бійців.
13. Якщо host стартує бій до ручного завершення:
    - сторона без командира отримує server-side auto deploy (серверну автоматичну розстановку);
    - сторона з командиром, який не завершив розстановку, отримує auto deploy для нерозставлених або незавершених частин;
    - після цього місія переходить у `BattleActive`.

## Історичний контекст нижче

Секції нижче залишені як історичний контекст і як запобіжник від повторення старих помилок. Якщо вони суперечать актуальному зрізу 2026-06-21, пріоритет має актуальний зріз вище.

## Історичний стан до стабілізації 2026-06-21

> Примітка: цей блок описує попередню фазу, коли основним blocker (блокером) були завантаження сцени, падіння клієнта і пошук стабільного mission shell. Він не є актуальним станом після commit `655a074`.

### Що вже не є головним blocker

- Shell routing (маршрутизація оболонки місії) вже не є головним blocker: live shell (фактична оболонка місії) доходить до `SiegeMissionWithDeployment`.
- Server-side blocker (серверний блокер) по `MultiplayerMissionAgentVisualSpawnComponent` знято: серверний стек більше не повинен тягнути client-only behavior (поведінку лише для клієнта).
- Server-side blocker по `ConsoleMatchStartEndHandler` знято: на сервері він optional (необов'язковий).
- Старий дефіцит campaign object data (кампанійних об'єктних даних) на клієнті більше не є підтвердженим поточним blocker:
  - `CampaignCharacterType=TaleWorlds.CampaignSystem.CharacterObject`;
  - `CampaignCultureType=TaleWorlds.CampaignSystem.CultureObject`.

### Що підтверджено останніми прогонами

- Сервер відкриває siege mission (місію облоги).
- Сервер збирає siege stack (стек поведінок облоги).
- Сервер доходить до mission behavior preload (попереднього завантаження поведінок місії).
- У серверному preload stack вже були:
  - `SpawnComponent`;
  - `MissionSiegeEnginesLogic`;
  - `SiegeDeploymentHandler`;
  - `SiegeDeploymentMissionController`;
  - `MissionNetworkComponent`.
- Клієнт відкриває `SiegeMissionWithDeployment`.
- Клієнтський stack (стек поведінок клієнта) збирається успішно.
- `MissionSiegeEnginesLogic factory succeeded`.
- `client validation passed`.
- Клієнт доходить до `MissionScreen`.
- Клієнт починає завантажувати сцену `empire_town_d`.
- Далі клієнт падає в native crash (нативне падіння) під час або після завантаження сцени.

### Остання зафіксована межа серверного падіння

Останній проаналізований прогін перед оновленням документа:

- сервер: `rgl_log_72528.txt` / `watchdog_log_72528.txt`;
- клієнт: `rgl_log_55424.txt` / `watchdog_log_55424.txt`;
- SP host (single-player host, локальний кампанійний хост): `rgl_log_44140.txt` / `watchdog_log_44140.txt`;
- клієнтський dump (дамп падіння): `C:\ProgramData\Mount and Blade II Bannerlord\crashes\2026-06-16_03.40.18\dump.dmp`;
- серверний dump: `C:\ProgramData\Mount and Blade II Bannerlord\crashes\2026-06-16_03.40.20\dump.dmp`.

Сервер доходив до:

- `CoopSiegeAssaultWithDeployment server: AfterStart completed. Scene=empire_town_d Mode=Deployment HasAttacker=True HasDefender=True`;
- `observed native MultiplayerWarmupComponent.AfterStart entry`;
- `suppressed dedicated siege replay MultiplayerWarmupComponent.AfterStart native original during deployment`.

Нові маркери для:

- `MultiplayerWarmupComponent.OnPreDisplayMissionTick`;
- `MultiplayerTimerComponent.StartTimerAsServer`;

не з'явились.

Висновок: серверний crash window (вікно падіння) після приглушення `MultiplayerWarmupComponent.AfterStart`, але до `OnPreDisplayMissionTick` / `StartTimerAsServer`.

## Останній впроваджений діагностичний крок

Файл:

- `C:\dev\projects\BannerlordCoopSpectator3\Patches\BattleShellSuppressionPatch.cs`

Додано вузькі server-only postfix markers (серверні маркери після завершення методу) для:

- `TaleWorlds.MountAndBlade.Mission.AfterStart`;
- `TaleWorlds.MountAndBlade.MissionLobbyComponent.AfterStart`;
- `TaleWorlds.MountAndBlade.MissionCustomGameServerComponent.AfterStart`;
- `TaleWorlds.MountAndBlade.MissionNetworkComponent.AfterStart`;
- `TaleWorlds.MountAndBlade.MissionLobbyEquipmentNetworkComponent.AfterStart`;
- `TaleWorlds.MountAndBlade.MultiplayerTeamSelectComponent.AfterStart`.

Також додано:

- `TryPatchPostfixMethod`;
- `PatchPostfixMethod`;
- `LogAfterStartPostfixObservation`;
- `HasMissionBehaviorTypeName`.

Важливо:

- server stack (серверний стек поведінок) цим кроком не змінювався;
- нових suppression (приглушень оригінальних методів) не додавалось;
- це лише діагностика boundary completion (перевірки, які межі `AfterStart` завершились перед падінням).

Build status (стан збірки) після цього кроку:

- `dotnet build CoopSpectator.csproj -c Release` - успішно, `0 Error(s)`;
- `dotnet build DedicatedServer\CoopSpectatorDedicated.csproj -c Release` - успішно, `0 Error(s)`;
- залишились старі warnings (попередження): `CS0162 unreachable code` і `System.Management` version conflict;
- git stage / commit / branch не виконувались;
- Git попереджав, що `Patches\BattleShellSuppressionPatch.cs` може бути переведений з `LF` у `CRLF` при наступному торканні Git.

## Актуальна інтерпретація наступного прогону

У новому серверному `rgl_log` треба знайти patch markers (маркери встановлення патчів):

- `BattleShellSuppressionPatch: patched postfix TaleWorlds.MountAndBlade.Mission.AfterStart`;
- `BattleShellSuppressionPatch: patched postfix TaleWorlds.MountAndBlade.MissionLobbyComponent.AfterStart`;
- `BattleShellSuppressionPatch: patched postfix TaleWorlds.MountAndBlade.MissionCustomGameServerComponent.AfterStart`;
- `BattleShellSuppressionPatch: patched postfix TaleWorlds.MountAndBlade.MissionNetworkComponent.AfterStart`;
- `BattleShellSuppressionPatch: patched postfix TaleWorlds.MountAndBlade.MissionLobbyEquipmentNetworkComponent.AfterStart`;
- `BattleShellSuppressionPatch: patched postfix TaleWorlds.MountAndBlade.MultiplayerTeamSelectComponent.AfterStart`.

Потім треба знайти runtime markers (маркери виконання під час місії):

- `BattleShellSuppressionPatch: observed AfterStart postfix boundary. Source=... completed`.

Логіка:

- якщо є `Mission.AfterStart completed`, то `Mission.AfterStart` не є точкою падіння;
- якщо є `MissionLobbyComponent.AfterStart completed`, але немає `MissionNetworkComponent.AfterStart completed`, crash window між ними;
- якщо всі `AfterStart` postfix markers є, crash уже після `AfterStart chain` (ланцюга після старту), і наступна діагностика має перейти до tick/deployment/scene activation boundary (межі тіку, розстановки або активації сцени).

## Поточний кодовий зріз

### Siege mission stack

Ключовий файл:

- `C:\dev\projects\BannerlordCoopSpectator3\GameMode\MissionMultiplayerCoopSiegeAssaultWithDeploymentMode.cs`

Серверний stack будується окремо від клієнтського:

- `BuildServerMissionBehaviors(...)`;
- `BuildClientMissionBehaviors(...)`;
- `ValidateServerStackSanity(...)`;
- `ValidateClientStackSanity(...)`.

Серверний stack містить:

- `MissionLobbyComponent`;
- `MissionMultiplayerCoopSiegeAssaultWithDeployment`;
- `MultiplayerWarmupComponent`;
- `MultiplayerTimerComponent`;
- `MissionLobbyEquipmentNetworkComponent`;
- `SpawnComponent`;
- `MultiplayerTeamSelectComponent`;
- boundary/hard-border behaviors (поведінки меж сцени);
- `MissionSiegeEnginesLogic`;
- `SiegeDeploymentHandler`;
- `SiegeDeploymentMissionController`;
- `CoopMissionNetworkBridge`.

На dedicated server (виділеному сервері) `CoopMissionSpawnLogic` свідомо не додається одразу, поки native siege bootstrap (рідний старт облоги) нестабільний.

Клієнтський stack містить:

- `MissionLobbyComponent`;
- `MultiplayerWarmupComponent`;
- `MissionMultiplayerCoopSiegeAssaultWithDeploymentClient`;
- `MultiplayerTimerComponent`;
- `MultiplayerMissionAgentVisualSpawnComponent`;
- `ConsoleMatchStartEndHandler`;
- `MultiplayerTeamSelectComponent`;
- boundary/hard-border behaviors;
- `MissionSiegeEnginesLogic`;
- `SiegeDeploymentHandler`;
- `SiegeDeploymentMissionController`;
- `MissionBehaviorDiagnostic`;
- `CoopMissionNetworkBridge`;
- optional UI behaviors (необов'язкові UI поведінки), які зараз використовуються для isolation (ізоляції) клієнтського падіння.

Client-only isolation toggles (перемикачі ізоляції клієнтських поведінок) уже є для:

- `MissionLobbyEquipmentNetworkComponent`;
- `MissionGauntletFormationMarker`;
- `CoopMissionSelectionView`.

### Deployment runtime

Ключовий файл:

- `C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\SiegeAssault\ExactCampaignSiegeAssaultWithDeploymentRuntime.cs`

Важливі методи:

- `IsSiegeAssaultScenario(...)`;
- `ShouldMountLiveDeploymentControllers(...)`;
- `TryEnsureMissionBehaviorContract(...)`;
- `IsDeploymentRuntimeActive(...)`;
- `IsDeploymentPhaseBlockingBattleStart(...)`;
- `HasDeploymentLifecycleFinished(...)`;
- `ShouldBlockPeerRespawnUntilBattleActive(...)`;
- `TryForceAutoDeployAndFinishDeployment(...)`;
- `TryPrepareDeploymentPlanContract(...)`;
- `TryApplyNativeLikeSpawnHandlerContract(...)`.

Фактичний стан:

- deployment runtime (рантайм розстановки) має власний стан активної місії;
- battle start (старт бою) блокується, поки deployment runtime активний і native deployment lifecycle не завершений;
- звичайний respawn/possession (повторне створення/вселення у бійця) блокується до `BattleActive`;
- `TryForceAutoDeployAndFinishDeployment(...)` відтворює мінімальний native-like auto deploy path (шлях автоматичної розстановки, подібний до рідного):
  - `CoopSiegeMachineDeploymentController.TryAutoDeploySide(...)`;
  - `CoopMissionNetworkBridge.TryBroadcastCommanderDeploymentSiegeMachineState(...)`;
  - `DeployAllSiegeWeaponsOfPlayer()`;
  - `AutoDeployTeamUsingTeamAI(...)` або `AutoDeployTeamUsingDeploymentPlan(...)`;
  - `ForceUpdateAllUnits()`;
  - `FinishDeployment()`.

Поточний ризик: attacker-side Auto-Deploy (автоматичну розстановку сторони атакуючих) підтверджено для балісти, але для кінцевої цілі треба окремо прогнати обидві сторони і сценарій старту бою хостом:

- сторона без командира;
- сторона з командиром, який не завершив ручну розстановку.

### Battle phase і spawn gates

Ключовий файл:

- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`

Важливі методи:

- `TryConsumeBattlePhaseRequests(...)`;
- `TryResolveBattleDataReadinessForPeer(...)`;
- `TryResolvePeerSpawnAvailability(...)`;
- `ShouldAllowExactSiegeCommanderDeploymentSpawn(...)`;
- `TryMatchExactSiegeCommanderDeploymentSelection(...)`;
- `TryAssignExactCampaignCommanders(...)`.

Фактичний стан:

- host start request (запит старту від хоста) під час exact siege deployment спочатку проходить через `TryForceAutoDeployAndFinishDeployment(...)`;
- якщо deployment still blocking (розстановка все ще блокує старт), перехід у `BattleActive` не відбувається;
- звичайний peer (мережевий гравець) під час deployment отримує стан `CommanderDeployment`, якщо він не має права на commander deployment spawn;
- commander-only exception (виняток лише для командира) дозволяє peer пройти readiness/spawn gate під час `Deployment`, якщо його вибраний entry (запис ростеру) збігається з commander entry.

Це відповідає цільовій ідеї: звичайні гравці чекають завершення розстановки, командир може отримати ранній доступ до native deployment menu.

### Selection UI

Ключові файли:

- `C:\dev\projects\BannerlordCoopSpectator3\UI\CoopMissionSelectionView.cs`;
- `C:\dev\projects\BannerlordCoopSpectator3\UI\CoopSelectionUiHelpers.cs`;
- `C:\dev\projects\BannerlordCoopSpectator3\UI\CoopSelectionShellViewModels.cs`.

Фактичний стан:

- selection UI (інтерфейс вибору) вже вміє показувати commander badge (позначку командира);
- commander entry визначається через `BattleCommanderResolver`;
- `CoopMissionSelectionView` пригнічує кастомний overlay (накладений інтерфейс), якщо активний native deployment UI:
  - `IsNativeDeploymentUiActive()`;
  - `MissionScreen.IsDeploymentActive`.

Це важливо для кінцевої цілі: коли commander переходить у native deployment menu, наш custom selection overlay не повинен перекривати рідний інтерфейс розстановки.

## Спростовано або більше не є робочим напрямком

- `OpenSiegeMissionNoDeployment` не є ціллю для кампанійного `SiegeAssault`.
- Просте відкриття `MultiplayerBattle` на siege scene (сцені облоги) не дає 1:1 кампанійної облоги.
- Ручне додавання `SiegeDeploymentHandler` і `SiegeDeploymentMissionController` у неправильний shell не є безпечним шляхом.
- Поточний crash не треба більше пояснювати старим дефіцитом `CharacterObject` / `CultureObject` на клієнті без повторної перевірки логів.
- Візуальний loading screen (екран завантаження) не означає, що прогресу немає: останні зміни вже переносили blocker між різними native boundaries (нативними межами).
- Зламані або відсутні стіни в `empire_town_d` не треба латати примусовим перемиканням мешів на клієнті. Root cause (коренева причина) була в тому, що клієнт запускав серверну `SiegeMissionPreparationHandler` scene preparation (підготовку сцени), а коректний шлях - лишити цю відповідальність серверу і передавати безпечні `wallHitPointRatios`.

## Відкриті ризики

- `MissionLobbyEquipmentNetworkComponent` на клієнті може бути потрібний для native siege spawn, але раніше був підозрюваним client-only crash node (вузлом клієнтського падіння).
- `MissionCustomGameClientComponent` і custom selection overlay можуть конфліктувати з native deployment UI, якщо їх увімкнути до стабільної scene/deployment boundary.
- `MissionGauntletFormationMarker` / formation marker UI (UI маркерів формацій) може чіпати deployment/order UI у невдалий момент.
- `TryForceAutoDeployAndFinishDeployment(...)` ще треба окремо підтвердити прогоном для обох сторін і host start flow (послідовності старту хостом). Button Auto-Deploy (автоматична розстановка через кнопку) для атакуючої балісти вже підтверджена.
- Для справжнього 1:1 потрібен не тільки старт місії, а й повна order replication (мережеве відтворення наказів) для командирської ручної розстановки.
- Чорні ділянки землі/матеріалів у сцені ще не закриті цим виправленням і потребують окремого дослідження, якщо вони заважають тестуванню штурму.

## Наступний порядок роботи

1. Після нового прогону спочатку аналізувати останні серверні й клієнтські `rgl_log` та `watchdog_log`.
2. Для стін перевіряти маркери: серверний `campaign-ratios` з очікуваними `RawValues` / `OutputValues`, клієнтський `skipped server-only siege scene preparation on client`, а також `SiegeSceneObjectParityProbeBehavior` на клієнті і сервері.
3. Наступний функціональний крок після стабілізації стін - перевірити завершення `Ready` / host start flow (послідовності кнопки готовності та старту хостом) і вселення командира/звичайних гравців після завершення розстановки.
4. Якщо чорні ділянки сцени заважатимуть штурму, планувати окремий scene/material pass (перевірку сцени та матеріалів), не змішуючи його з логікою меню розстановки.
5. Не змінювати server stack без окремого плану.
6. Не повертати польову або village battle логіку в siege flow.

## Правило для нового чату

У новому чаті цей файл треба вважати головним handoff document (документом передачі контексту) по режиму облоги.

Новий чат має:

- перед кожним новим етапом читати цей файл;
- після кожного підтвердженого прогону оновлювати блоки:
  - "Поточний підтверджений стан";
  - "Остання зафіксована межа серверного падіння";
  - "Актуальна інтерпретація наступного прогону";
  - "Відкриті ризики";
  - "Наступний порядок роботи";
- прибирати неактуальні припущення, а не накопичувати історичний шум;
- чітко розділяти:
  - реалізовано в коді;
  - підтверджено логами;
  - цільова поведінка;
  - гіпотеза;
  - спростовано.

Перед будь-якими code changes (змінами коду), build (збіркою), git operation (операцією Git) або file edit (редагуванням файлу) новий чат має спочатку дати план і чекати явного `ок`.

## Ключові файли

- `C:\dev\projects\BannerlordCoopSpectator3\GameMode\MissionMultiplayerCoopSiegeAssaultWithDeploymentMode.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\GameMode\MissionMultiplayerCoopSiegeAssaultWithDeployment.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\GameMode\MissionMultiplayerCoopSiegeAssaultWithDeploymentClient.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\SiegeAssault\ExactCampaignSiegeAssaultWithDeploymentRuntime.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\SiegeAssault\CoopSiegeMachineDeploymentController.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\SiegeAssault\SiegeAssaultMissionOpenBridge.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionNetworkBridge.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Patches\BattleShellSuppressionPatch.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Patches\MissionStateOpenNewPatches.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\UI\CoopMissionSelectionView.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\UI\CoopSiegeMachineDeploymentVM.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\UI\CoopSelectionUiHelpers.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\UI\CoopSelectionShellViewModels.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\BattleCommanderResolver.cs`

## Native reference targets

Для low-level research (низькорівневого дослідження) звірятись із:

- `SandBox.SandBoxMissions.OpenSiegeMissionWithDeployment(...)`;
- `SandBox.Missions.MissionLogics.SandBoxSiegeMissionSpawnHandler`;
- `TaleWorlds.MountAndBlade.SiegeMissionPreparationHandler`;
- `TaleWorlds.MountAndBlade.DeploymentPoint`;
- `TaleWorlds.MountAndBlade.BattleSpawnPathSelector`;
- `TaleWorlds.MountAndBlade.DefaultBattleMissionAgentSpawnLogic`;
- `TaleWorlds.MountAndBlade.Missions.Handlers.SiegeDeploymentHandler`;
- `TaleWorlds.MountAndBlade.SiegeDeploymentMissionController`;
- `MissionOrderDeploymentControllerVM.ExecuteAutoDeploy()`;
- `DeploymentHandler.FinishDeployment()`.

Сцена для поточного replay path (шляху відтворення):

- `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\SandBoxCore\SceneObj\empire_town_d\scene.xscene`.
