# Проблема кінних лучників і кінних застрільщиків у materialization battle agents

Дата: 2026-06-09
Проєкт: `BannerlordCoopSpectator3`
Гілка дослідження: `codex/v0.1.1-refresh`
Статус: виправлено і підтверджено ручним прогоном

## Короткий висновок

Проблема була не в одному місці, а в двох послідовних шарах:

1. `mounted + ranged` (кінний + дальній бій) для більшості культур згортався у звичайну `light cavalry shell` (легку кавалерійську MP-оболонку), тому рушій стартував таких бійців як ближню кавалерію.
2. Після додавання окремих `horse archer shell` (оболонок кінного стрільця) сервер ще й помилково вважав частковий `pre-spawn inject` (передстартовий інжект спорядження) повним exact weapon loadout (точним стартовим набором зброї) і через це пропускав післяспавнове серверне накладання кампанійської зброї.

Саме комбінація цих двох дефектів пояснювала весь набір симптомів:

- кінні лучники та кінні застрільщики йшли в ближній бій;
- у гравця не було окремої `horse archer group` (групи кінних стрільців);
- після проміжного фіксу всі вершники могли лишатися на однаковій native shell (нативній шаблонній оболонці);
- кінні метальщики не кидали дротики;
- після вселення вершник міг ламатися на атаці та перемиканні зброї.

## Початково підтверджені симптоми

До виправлення логи і ручні прогони підтверджували таке:

- кампанійні `mounted ranged` (кінні стрілецькі) записи мапилися у `mp_coop_light_cavalry_*`;
- передбойовий вибір бачив їх як звичайну кавалерію;
- native spawn path (рідний шлях створення агента) піднімав melee cavalry loadout (набір кавалерійської зброї ближнього бою);
- окремої кінно-стрілецької групи у гравця не з'являлося.

Це було підтверджено, зокрема, по:

- [rgl_log_143188.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_143188.txt:30956>)
- [rgl_log_143188.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_143188.txt:32765>)
- [rgl_log_143188.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_143188.txt:33385>)
- [rgl_log_117016.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_117016.txt:16529>)

## Друга коренева причина, яку виявлено вже після введення horse archer shell

Після розширення MP-оболонок кінні лучники почали стріляти, але кінні метальщики ще не кидали дротики, а частина вершників після вселення виглядала так, ніби залишилась на однаковому native спорядженні.

Додатковий розбір логів показав точну причину:

- `BattleSnapshotRuntimeState` (стан runtime-знімка бою) уже знав, що для частини mounted entry (кінних записів) `ServerCreatePreSpawnIncludesWeapons=false`;
- водночас `ExactCampaignPreSpawnLoadoutPatch` (патч передстартового накладання спорядження) все одно ставив прапорець, що інжект спорядження був;
- `CoopMissionBehaviors` (головна місійна логіка) трактував сам факт будь-якого `equipment injected` (інжекту спорядження) як ознаку того, що точна передстартова зброя вже повністю застосована;
- через це `server-authoritative overlay` (серверне авторитетне післяспавнове накладання точного спорядження) пропускав саме той крок, який мав замінити native стартову зброю на кампанійну.

Це було видно по логах:

- [rgl_log_105048.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_105048.txt:24574>)
  `Inject=True, Weapons=False, Mount=True`
- [rgl_log_105048.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_105048.txt:24681>)
  `Inject=True, Weapons=False, Mount=True`
- [rgl_log_105048.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_105048.txt:26135>)
  `AppliedEquipment=pre-spawn-exact-loadout EquipmentMisses=overlay-skipped`
- [rgl_log_105048.txt](</C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_105048.txt:26136>)
  `AppliedEquipment=pre-spawn-exact-loadout EquipmentMisses=overlay-skipped`

Тобто сервер помилково пропускав exact weapon refresh (точне післяспавнове оновлення зброї) саме для тих кінних AI-бійців, у яких передстартовий інжект був лише частковим.

## Що змінено в коді

### 1. Розширено вибір mounted ranged shell

У таких файлах:

- [Campaign/BattleDetector.cs](/C:/dev/projects/BannerlordCoopSpectator3/Campaign/BattleDetector.cs)
- [Infrastructure/CampaignMultiplayerHeroClassResolver.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/CampaignMultiplayerHeroClassResolver.cs)
- [Infrastructure/BattleSnapshotRuntimeState.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/BattleSnapshotRuntimeState.cs)

замість безумовного зведення `mounted` до `light cavalry` додано окремий шлях для `mounted + ranged`, який резолвиться в `mp_coop_light_horse_archer_<culture>_*`.

Наслідок:

- кінні лучники та кінні застрільщики більше не стартують як звичайна ближня кавалерія;
- у гравця формується окрема `horse archer` роль;
- рушій отримує стартову native shell, яка вже ближча до правильної тактичної поведінки.

### 2. Додано coop horse archer divisions і characters

У:

- [Module/CoopSpectator/ModuleData/coopspectator_mpcharacters.xml](/C:/dev/projects/BannerlordCoopSpectator3/Module/CoopSpectator/ModuleData/coopspectator_mpcharacters.xml)
- [Module/CoopSpectator/ModuleData/coopspectator_mpclassdivisions.xml](/C:/dev/projects/BannerlordCoopSpectator3/Module/CoopSpectator/ModuleData/coopspectator_mpclassdivisions.xml)

додано окремі `mp_coop_light_horse_archer_*` записи для культур, які раніше не мали coop-оболонки кінного стрільця.

Наслідок:

- новому resolver path (шляху підбору шаблону) тепер є куди резолвитися;
- mounted ranged більше не падає назад у легку кавалерію тільки через відсутність MP-класу.

### 3. Виправлено умову пропуску exact weapon overlay

У [Mission/CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs) стара перевірка:

- "чи був хоч якийсь `equipment injected`"

була замінена на правильну перевірку:

- "чи був повний `pre-spawn exact weapon loadout`"
- тобто чи `WasEquipmentInjectedForEntry(entryId) == true` і одночасно `ServerCreatePreSpawnIncludesWeapons == true`

Це змінило поведінку в двох критичних місцях:

- `server-authoritative overlay` більше не пропускає mounted AI, у яких зброя не була реально матеріалізована до спавну;
- `replace-bot reapply` (повторне накладання після вселення або заміни бота) більше не вважає таку часткову передстартову матеріалізацію завершеною.

Наслідок:

- кінні метальщики отримують свої кампанійські дротики;
- кінні лучники отримують свою кампанійну дальню зброю;
- після вселення вершник більше не застрягає на поламаному шаблонному наборі зброї.

## Підтверджений фінальний результат

Після фінального фіксу користувач підтвердив ручним прогоном:

- усе працює;
- кінні лучники реально стріляють;
- кінні застрільщики кидають дротики;
- після вселення вершник не ламається;
- атака і перемикання зброї більше не розходяться з кампанійським станом;
- проблема однакового native спорядження у всіх вершників зникла.

Окремо build (збірка) також пройшов успішно:

- `dotnet build CoopSpectator.csproj -c Debug`

Автоматичне розгортання на клієнт і dedicated server (виділений сервер) теж відбулося.

## Що саме виявилося хибним припущенням

Хибним було вважати, що після введення horse archer shell проблема повністю залишається лише у formation logic (логіці формацій) або в mount path (шляху роботи з конем).

Насправді:

- перший дефект був у shell resolution (логіці вибору MP-оболонки);
- другий дефект був у overlay gate (умові, яка вирішує, чи треба ще раз накладати точну кампанійну зброю після спавну).

Без закриття обох шарів одночасно система лишалася напівробочою.

## Підсумок

Проблему кінних лучників і кінних застрільщиків на цій гілці закрито таким ланцюгом:

1. `mounted ranged` перестав зводитися в `light cavalry`;
2. для потрібних культур з'явилися окремі coop `horse archer` MP-оболонки;
3. сервер перестав вважати частковий `pre-spawn` повною exact materialization (точною матеріалізацією);
4. післяспавнове точне накладання кампанійської зброї знову стало працювати саме для тих mounted AI-бійців, яким воно реально потрібне.

Фінальний стан:

- stable native start (стабільний нативний старт) з правильною mounted ranged роллю;
- коректна доматеріалізація кампанійського спорядження;
- робочий бій і робоче вселення без регресії на кінноті.
