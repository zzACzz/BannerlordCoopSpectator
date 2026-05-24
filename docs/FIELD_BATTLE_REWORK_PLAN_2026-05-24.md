# План переробки польової битви на правильну архітектуру

Дата: `2026-05-24`

Статус документа: робочий план, який можна брати як основний маршрут переробки

## 1. Короткий висновок

Поточний шлях з `shell`, `surrogate`, `overlay` і післястворювальною підміною спорядження не треба розвивати далі як основу системи.

Простими словами:
- `shell` = фальшива оболонка бійця
- `surrogate` = замінник справжнього кампанійного бійця
- `overlay` = накладка поверх уже створеного агента

Цей шлях упирається не в один конкретний баг, а в неправильну межу системи:
- кампанія є господарем стратегічного стану світу;
- dedicated-сервер має бути господарем тактичного бою;
- між ними має їхати не живий `runtime`, а чистий договір даних.

`runtime` = живе середовище виконання, тобто реальні об'єкти гри, які зараз існують у пам'яті.

Для першого етапу ціллю є тільки польова битва.
Лігва, облоги і морські бої не є частиною цього першого плану.

## 2. Що вже перевірено на найнижчому рівні

Нижче не припущення, а точки, які перевірені по декомпіляції і по поточному коду моду.

### 2.1. У кампанії є рідний стабільний ідентифікатор конкретного бійця

Є `UniqueTroopDescriptor`.

Простими словами:
- це не просто "тип війська";
- це ідентифікатор конкретного бійця всередині поточного бойового набору.

Перевірено в:
- `tmp_decompile_campaignsystem/TaleWorlds.CampaignSystem.TroopSuppliers/PartyGroupTroopSupplier.cs`
- `tmp_decompile_campaignsystem/TaleWorlds.CampaignSystem.AgentOrigins/PartyGroupAgentOrigin.cs`
- `tmp_decompile_campaignsystem/TaleWorlds.CampaignSystem.MapEvents/MapEventSide.cs`

Що це означає для плану:
- пункт "передавати на сервер не тільки стаки, а окремі екземпляри бійців" здійсненний;
- пункт "повертати результат не загальним числом, а по конкретних бійцях" теж здійсненний.

`stack` = група однакових військ, наприклад 20 однакових піхотинців.

### 2.2. Поточний host вже вміє дістати mission-ready порядок бійців

Перевірено в:
- `Campaign/BattleDetector.cs`

Ключова точка:
- метод `BuildMissionReadyEntryOrder(...)` уже ходить у `MakeReadyForMission`.

Простими словами:
- мод уже частково добирається до того місця, де кампанія готує бійців до реальної місії;
- отже, експорт правильного порядку ідентичностей не треба вигадувати з нуля.

### 2.3. Сервер може народжувати агента одразу з потрібним спорядженням

Перевірено в:
- `.codex_tmp/decompiled_mountandblade/TaleWorlds.MountAndBlade/AgentBuildData.cs`
- `.codex_tmp/decompiled_mountandblade/TaleWorlds.MountAndBlade/Mission.cs`
- `.codex_tmp/decompiled_mountandblade/NetworkMessages.FromServer/CreateAgent.cs`
- `.codex_tmp/decompiled_mountandblade/TaleWorlds.MountAndBlade/MissionNetworkComponent.cs`

Ключові факти:
- `AgentBuildData` приймає `Equipment`;
- `AgentBuildData` приймає `MissionEquipment`;
- `Mission` передає це далі в `CreateAgent`;
- мережевий шлях теж передає `SpawnEquipment` і `MissionEquipment`.

Простими словами:
- правильний шлях існує;
- не треба будувати нову архітектуру навколо підміни спорядження після створення агента.

### 2.4. Кампанія вміє прийняти назад втрати рідним шляхом

Перевірено в:
- `tmp_decompile_campaignsystem/TaleWorlds.CampaignSystem.MapEvents/MapEventSide.cs`
- `tmp_decompile_campaignsystem/TaleWorlds.CampaignSystem.MapEvents/MapEventParty.cs`
- `tmp_decompile_campaignsystem/TaleWorlds.CampaignSystem.Encounters/PlayerEncounter.cs`
- `tmp_decompile_campaignsystem/TaleWorlds.CampaignSystem.MapEvents/MapEvent.cs`

Ключові точки:
- `MapEventSide.OnTroopKilled(...)`
- `MapEventSide.OnTroopWounded(...)`
- `MapEventSide.OnTroopRouted(...)`
- `MapEventSide.OnTroopScoreHit(...)`
- `MapEventSide.CommitXpGains()`
- `MapEvent.CalculateAndCommitMapEventResults()`

Простими словами:
- кампанія вже має свій рідний шлях, як зафіксувати вбитих, поранених, тих хто втік, і досвід;
- якщо ми повернемо результат у правильній формі, нам не треба вручну вигадувати половину післябойової логіки.

### 2.5. Перша переробка польової битви не заблокована відсутністю чистого listed custom mode

`listed mode` = режим, який видно у списку серверів.

Перевірено в:
- `DedicatedServer/SubModule.cs`
- `DedicatedServer/Patches/GameModeOverridePatches.cs`
- `DedicatedHelper/DedicatedHelperLauncher.cs`
- `GameMode/MissionMultiplayerCoopBattle.cs`
- `Infrastructure/CoopGameModeIds.cs`

Ключовий висновок:
- перший правильний шлях для польової битви можна будувати всередині сумісного профілю `Battle`;
- тобто список серверів і повністю чистий окремий публічний тип не є обов'язковим блокером для цієї переробки.

### 2.6. Кампанійні бойові моделі на dedicated уже частково є

Перевірено в:
- `DedicatedServer/SubModule.cs`
- `DedicatedServer/Mission/DedicatedKnockoutOutcomeModelOverride.cs`
- `DedicatedServer/Patches/DedicatedKnockoutOutcomePatches.cs`

Ключові точки:
- `CoopCampaignDerivedAgentStatCalculateModel`
- `CoopCampaignDerivedStrikeMagnitudeCalculationModel`
- `CoopCampaignDerivedAgentApplyDamageModel`
- `CoopCampaignDerivedMissionDifficultyModel`
- dedicated override для нокаутів

Простими словами:
- нам не треба переписувати весь бойовий розрахунок з нуля;
- частину кампанійної бойової математики вже можна зберегти.

## 3. Що з цього випливає

План нижче здійсненний.

У ньому немає пунктів, які прямо ламаються при глибокій перевірці.

Але є важлива умова:
- ми не намагаємось перенести на сервер живі кампанійні об'єкти;
- ми переносимо точний опис бою;
- сервер проводить бій;
- кампанія приймає точний результат.

## 4. Що залишаємо, а що перестаємо вважати основою

### 4.1. Залишаємо

- запуск dedicated-сервера;
- вхід гравців у мережеву місію;
- завантаження сцен;
- загальну транспортну оболонку передачі бойових даних;
- кампанійні combat-model wrappers на dedicated;
- значну частину логування і діагностики.

### 4.2. Перестаємо використовувати як фундамент

- `Infrastructure/ExactCampaignArmyBootstrap.cs`
- `Patches/ExactCampaignPreSpawnLoadoutPatch.cs`
- `Patches/BattleMapSpawnHandoffPatch.cs`
- aggregate writeback у `Campaign/BattleDetector.cs`

Простими словами:
- ці частини можуть тимчасово лишатись у коді під прапорами сумісності;
- але нова польова битва не повинна залежати від них для correctness.

`correctness` = правильність результату, а не просто відсутність крешу.

## 5. Цільова архітектура для польової битви

### 5.1. Campaign export bridge

Потрібен окремий шар експорту з кампанії.

`bridge` = міст між двома частинами системи.

Він повинен зібрати:
- тип бою;
- сцену;
- випадкове зерно;
- сторони;
- загони;
- командирів;
- підкріплення;
- параметри розгортання;
- окремий запис на кожного бійця.

`seed` або зерно = число, яке робить випадковість відтворюваною.

### 5.2. Canonical battle contract

Потрібен один головний формат даних бою.

`canonical` = єдиний основний правильний формат.

Його робочі частини:
- `BattleContext`
- `BattleSide`
- `BattleParty`
- `TroopInstance`
- `HeroState`
- `EquipmentSpec`
- `FormationAssignment`
- `ReinforcementPlan`

Найважливіше:
- `TroopInstance` має бути не на рівні стака, а на рівні окремого бійця;
- кожен `TroopInstance` має мати стабільний `instance_id`;
- для кампанійного походження він має бути прив'язаний до `UniqueTroopDescriptor` або його безпечного serialized-відбитка.

`serialized` = перетворений у формат даних для передачі або збереження.

### 5.3. Dedicated materializer

Потрібен окремий шар, який будує бій на сервері з canonical contract.

`materializer` = частина, яка створює реальних агентів із опису даних.

Він повинен:
- створити сторони;
- створити команди;
- розкласти формації;
- створити агентів одразу з правильними `Equipment` і `MissionEquipment`;
- застосувати hero/body/perk/banner state до створення або під час безпечного server-side spawn path.

Тут принцип такий:
- не спочатку неправильний MP-агент, а потім підміни;
- а одразу правильний агент із правильних даних.

### 5.4. Server result capture

Потрібен окремий шар збору результату бою по конкретних бійцях.

Він повинен зібрати:
- хто живий;
- хто вбитий;
- хто поранений;
- хто втік;
- хто втратив коня;
- хто герой і в якому він стані;
- хто скільки завдав шкоди або XP-подій, якщо це треба для точного імпорту.

Для цього потрібен власний server-side origin class, який зберігає:
- `instance_id`
- `side_id`
- `party_id`
- `hero_id`, якщо є
- фінальний стан

### 5.5. Campaign import bridge

Потрібен окремий шар імпорту результату назад у live campaign state.

`live campaign state` = реальний поточний стан кампанії в пам'яті.

Правильний шлях:
- заново привести `MapEventSide` у mission-ready стан;
- виділити тих самих healthy participants;
- зіставити їх із поверненими `instance_id`;
- програти назад `Killed`, `Wounded`, `Routed`, `ScoreHit`;
- встановити результат сторін;
- дати `PlayerEncounter` і `MapEvent` завершити native aftermath.

`aftermath` = післябойові наслідки.

## 6. Робочий план впровадження

Нижче план, по якому можна реально йти в коді.

### Етап 1. Заморозити старий exact-transfer як тимчасову спадщину

- [ ] Не розширювати `shell/surrogate/overlay` шлях новими фіксами для польової битви.
- [ ] Ввести окремий feature flag для нової архітектури польового бою.
- [ ] Позначити старі шари як legacy only для fallback і порівняння.

`legacy` = стара спадкова система, яку тимчасово тримаємо поруч, але не вважаємо правильною основою.

Перевірка здійсненності:
- це організаційний етап;
- він не має низькорівневого блокера.

### Етап 2. Винести новий canonical contract для польової битви

- [ ] Створити новий контракт старту бою без `ServerCreate*` полів.
- [ ] Додати `TroopInstance` як окрему сутність.
- [ ] Для кожного `TroopInstance` передавати `instance_id`, `character_id`, `hero_id`, `hp`, `equipment`, `mount`, `perks`, `banner effect`, `formation assignment`.
- [ ] Для підкріплень передавати не "кількість десь збоку", а явний план хвиль або черги появи.

Перевірка здійсненності:
- це підтримується тим, що host уже доходить до `MakeReadyForMission`;
- `UniqueTroopDescriptor` існує;
- мережевий транспорт великих пакетів у моді вже є.

### Етап 3. Побудувати правильний export bridge з кампанії

- [ ] Винести логіку експорту з `BattleDetector` в окремий клас, наприклад `CampaignFieldBattleExportBridge`.
- [ ] Брати не тільки сторони і стаки, а повний mission-ready список екземплярів бійців.
- [ ] Заморозити порядок учасників і їх відповідність `instance_id` ще до передачі на dedicated.
- [ ] Окремо витягнути hero data, body properties, perks, banner effects, commander bindings.

`bindings` = прив'язки, наприклад який командир веде яку формацію.

Перевірка здійсненності:
- поточний `BuildMissionReadyEntryOrder(...)` уже доводить, що host-side дорога до mission-ready списку існує;
- тут немає потреби вигадувати нову native identity.

### Етап 4. Побудувати server-side materializer без overlay-основи

- [ ] Створити окремий компонент, наприклад `CoopFieldBattleMaterializer`.
- [ ] Давати йому canonical contract і тільки його.
- [ ] Створювати агентів через `AgentBuildData` з прямим `Equipment` і `MissionEquipment`.
- [ ] Не покладатися на `ExactCampaignPreSpawnLoadoutPatch` як на головний шлях correctness.
- [ ] Не покладатися на `BattleMapSpawnHandoffPatch` для масового переписування spawn/equipment corridor.

`corridor` = технічний коридор проходження даних від створення агента до появи у клієнтів.

Перевірка здійсненності:
- `Mission` і `CreateAgent` уже вміють передавати правильне spawn equipment;
- отже, цей етап не впирається в неможливість мережевого spawn path.

### Етап 5. Додати власний origin/result layer на dedicated

- [ ] Створити server-side origin class для кожного `TroopInstance`.
- [ ] Збирати фінальний стан бійця через `SetWounded`, `SetKilled`, `SetRouted`, `OnScoreHit`.
- [ ] Зберігати зв'язок `agent -> instance_id`.
- [ ] Підготувати результат як `CanonicalBattleResultContract`.

Перевірка здійсненності:
- native логіки `BattleAgentLogic` і `CustomBattleAgentLogic` уже працюють через ці callback-и;
- отже, власний server-side origin для накопичення truth можливий.

`callback` = виклик назад у наш об'єкт, коли сталася подія.

### Етап 6. Побудувати campaign import bridge замість aggregate writeback

- [ ] Створити окремий клас, наприклад `CampaignFieldBattleImportBridge`.
- [ ] Перестати вважати `battle_result.json` aggregate summary головним джерелом правди.
- [ ] На host-side зіставляти повернені `instance_id` з live mission-ready descriptors.
- [ ] Програвати `Killed`, `Wounded`, `Routed` у `MapEventSide`.
- [ ] За потреби програвати `ScoreHit` і завершувати `CommitXpGains`.
- [ ] Потім ставити підсумок бою і віддавати управління native campaign aftermath.

`aggregate summary` = зведення лише по загальних числах, без окремих бійців.

Перевірка здійсненності:
- `MapEventSide` і `MapEventParty` мають рідні методи для цих подій;
- `PlayerEncounter` і `MapEvent` далі мають свій шлях післябиттєвого завершення.

### Етап 7. Підключити новий шлях тільки для польової битви

- [ ] Спочатку переключити лише `field battle`.
- [ ] Старий шлях лишити тимчасово тільки як fallback і засіб порівняння.
- [ ] Не змішувати першу інтеграцію польового бою з лігвами, облогою і морем.

Перевірка здійсненності:
- польова битва має найменше спеціальної mission logic;
- це найменший безпечний зріз для міграції.

### Етап 8. Після стабілізації видалити залежність від shell/exact-overlay для поля

- [ ] Прибрати використання legacy bootstrap у польовому бою.
- [ ] Прибрати залежність польового бою від post-create equipment injection.
- [ ] Прибрати залежність польового бою від aggregate aftermath patching.

Перевірка здійсненності:
- якщо етапи 2-7 завершені, цей етап є природним завершенням міграції.

## 7. Які пункти точно не варто тягнути в нову основу

- не тягнути `ServerCreateUseStringIdExactEquipmentPath` та подібні поля в canonical contract;
- не тягнути surrogate characters як основну бойову модель;
- не тягнути post-create overlay як джерело correctness;
- не тягнути aggregate-only battle result як головну правду;
- не тягнути manual prisoner patching як фінальну архітектуру для поля.

## 8. Що треба зробити першим у коді

Ось порядок першої практичної хвилі змін.

### 8.1. Новий документований контракт

- [ ] Додати нові типи контракту старту і результату бою.
- [ ] Не ламати одразу старий `BattleStartMessage`, а завести новий шлях поруч.

Рекомендовані нові файли:
- `Network/Messages/CanonicalBattleContract.cs`
- `Network/Messages/CanonicalBattleResultContract.cs`

### 8.2. Новий export bridge

- [ ] Винести export з `BattleDetector` в окремий клас.
- [ ] Зробити перший варіант тільки для `field battle`.

Рекомендовані нові файли:
- `Campaign/CampaignFieldBattleExportBridge.cs`

### 8.3. Новий import bridge

- [ ] Винести import/writeback з `BattleDetector` в окремий клас.
- [ ] Основою мають бути `MapEventSide` callbacks, а не ручне агрегатне редагування ростерів.

Рекомендовані нові файли:
- `Campaign/CampaignFieldBattleImportBridge.cs`

### 8.4. Новий dedicated materializer і result layer

- [ ] Створити server-side materializer.
- [ ] Створити server-side origin/result tracking.

Рекомендовані нові файли:
- `DedicatedServer/Mission/CoopFieldBattleMaterializer.cs`
- `DedicatedServer/Mission/CoopFieldBattleAgentOrigin.cs`
- `DedicatedServer/Mission/CoopFieldBattleResultCollector.cs`

## 9. Критерії готовності першої правильної польової битви

Перша версія вважається правильною тільки якщо виконані всі умови нижче.

- [ ] На сервер їде не тільки stack summary, а окремі troop instances.
- [ ] Герої, HP, спорядження, коні, perks і banner effects відтворюються через canonical contract.
- [ ] Польовий бій не залежить від post-create equipment overlay для правильності.
- [ ] Результат повертається не тільки загальними числами, а по `instance_id`.
- [ ] Host-side aftermath не базується на ручних евристиках як основному шляху.
- [ ] `PlayerEncounter` і `MapEvent` все ще виконують native campaign finalization.

`finalization` = завершення події бою з усіма наслідками.

## 10. Реалістична оцінка обсягу

Це велика переробка ядра польового бою, але не переписування всього моду з нуля.

Простими словами:
- мережеву оболонку і частину існуючого збору даних можна зберегти;
- ядро породження армії на сервері і повернення результату в кампанію треба перебудувати серйозно.

## 11. Ризики, які лишаються навіть після правильної архітектури

- точна сумісність деяких banner effects може вимагати окремого server-side застосування;
- XP fidelity може потребувати або детального replay `ScoreHit`, або окремого погодженого спрощення;
- reinforcement timing може потребувати окремого калібрування проти campaign path;
- пізніше при лігвах, облогах і морі знадобляться окремі battle-type adapters.

`fidelity` = наскільки точно ми збігаємось з оригінальною логікою.

`adapters` = окремі шари перекладу для різних типів бою.

## 12. Підсумок

Для польової битви правильний маршрут такий:
- кампанія експортує точний опис бою;
- dedicated проводить бій як тактичний господар;
- сервер повертає результат по конкретних бійцях;
- кампанія застосовує наслідки у свої рідні `MapEvent` і `PlayerEncounter`.

Саме по цьому плану далі треба міняти мод.
