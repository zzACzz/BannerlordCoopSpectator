# Проблема прискорення пішого бійця при перемиканні зброї

Дата: 2026-06-09  
Проєкт: `BannerlordCoopSpectator3`  
Досліджена заморожена гілка: `codex/field-battle-rework`  
Локальний checkpoint (контрольна зафіксована точка): `acbe9c8`  

## Симптом

У точному перенесенні кампанійських бійців у битву спостерігався дефект:

- піший боєць після перемикання на іншу зброю починав рухатись швидше, ніж повинен;
- ефект був не лише в героя, а і в звичайних бійців;
- у замороженій гілці проблема була послаблена лише частково;
- окремі піхотні або злізлі з коня бійці, зокрема мамлюк з дворучною булавою, все одно могли розганятись.

## Що саме було перевірено

У замороженій гілці по історії змін знайдено два ключові коміти, які реально стосувались саме цього дефекту:

1. `cc4ce2c` - `Reapply speed baseline and ordinary AI weapon injection`
2. `3d5cf26` - `Guard local pre-battle weapon switch native corridors`

Саме вони і давали той частковий ефект, який раніше спостерігався в тестах.

## Коміт `cc4ce2c`: що він робив по суті

Цей коміт працював у трьох файлах:

- `Mission/CoopMissionBehaviors.cs`
- `MissionModels/CoopCampaignDerivedAgentStatCalculateModel.cs`
- `Infrastructure/ExactCreateAgentServerPreSpawnContractResolver.cs`

### 1. Приглушення приблизних перерахунків швидкості для exact hero personal profile

Головна логіка була в `Mission/CoopMissionBehaviors.cs`.

У методі, де до агента застосовуються бойові корекції профілю, коміт ввів такі захисні прапори:

- `exactHeroPersonalProfileActive`
- `suppressApproximateExactHeroWeaponSpeedAdjustments`
- `suppressApproximateExactHeroMovementAdjustments`

Ці прапори вмикались лише якщо одночасно виконувались дві умови:

- місія вже працює через `CoopCampaignDerivedAgentStatCalculateModel`;
- профіль агента проходить `IsHeroProfileEligibleForExactPersonalPerks(...)`.

Далі коміт почав вибірково пропускати приблизні надбавки, які раніше могли повторно накручувати швидкість:

- `TryApplyPrimaryWeaponSkillDrivenStats(...)`
- `TryApplyEnduranceDrivenStats(...)`
- рухові частини `TryApplyPerkDrivenStats(...)`
- рухові частини `TryApplyPartyModifierDrivenStats(...)`
- частину швидкісних корекцій у `TryApplyMountedHumanRidingDrivenStats(...)`

Практичний зміст цього кроку був такий:

- коли exact-профіль героя вже дає точні бойові числа через нижчий рівень `AgentStatCalculateModel`,
- верхній шар більше не повинен вдруге додавати наближені бонуси до руху та швидкості роботи зброї.

Саме це і було головним частковим анти-прискорювальним фіксом у замороженій гілці.

### 2. Залежність від low-level exact stat model

У `MissionModels/CoopCampaignDerivedAgentStatCalculateModel.cs` коміт опирався на те, що активна місія справді працює через наш точний модельний шар.

Критична перевірка:

- `CoopCampaignDerivedAgentStatCalculateModel.IsActiveForMission(mission)`

Це означає, що старий частковий фікс не був універсальним reset (скиданням) усіх рухових бонусів у будь-якому випадку.  
Він спрацьовував лише в тому коридорі, де:

- активний саме наш derived model (успадкований шар бойових статів),
- і агент належить до категорії `exact personal hero`.

### 3. Додатковий ordinary-entry hybrid path

У `Infrastructure/ExactCreateAgentServerPreSpawnContractResolver.cs` цей самий коміт також повернув частину `ordinary entry hybrid create-agent safe` логіки для не-геройських записів:

- `ordinaryEntryHybridCreateAgentSafe`
- `payloadDiagnostic.ClientCreateAgentSafe`
- `!HasExactPersonalHeroIdentity(entryState)`

Це важливий контекст, але це не є основним виправленням прискорення.  
Цей шматок радше зберігав працездатність частини гібридного створення звичайних агентів, ніж вирішував сам root cause (кореневу причину) дефекту швидкості.

## Коміт `3d5cf26`: що він робив по суті

Другий важливий коміт працював у файлі:

- `Patches/BattleMapSpawnHandoffPatch.cs`

Він додав захист локальних клієнтських native corridor (внутрішніх рідних коридорів рушія), які могли змінювати зброю ще до завершення безпечної передбойової матеріалізації.

Були закриті такі точки:

- `Agent.SetWieldedItemIndexAsClient`
- `Agent.StartSwitchingWeaponUsageIndexAsClient`
- `Agent.SetUsageIndexOfWeaponInSlotAsClient`
- додатково пов'язані локальні шляхи `TryToWieldWeaponInSlot` і `WieldNextWeapon`

Ключовий сенс цього кроку:

- якщо `deferred pre-battle safe hold` (відкладене передбойове безпечне утримання стартової зброї) ще не матеріалізований локально,
- клієнтські native-виклики перемикання зброї тимчасово пригнічуються,
- щоб рушій не встиг зіпсувати локальний бойовий стан агента раніше за наш контрактний handoff (етап передачі керування і стану).

Отже, цей коміт не лікував саму математику швидкості напряму.  
Він прибирав один із тригерів, який міг запускати небажані повторні переходи стану зброї до старту бою.

## Чому старий фікс був лише частковим

### 1. Захист був орієнтований насамперед на exact personal hero

Найважливіше обмеження коміту `cc4ce2c` таке:

- прапори `suppressApproximateExactHeroWeaponSpeedAdjustments` і `suppressApproximateExactHeroMovementAdjustments`
- вмикались через `IsHeroProfileEligibleForExactPersonalPerks(...)`

Тобто старий захист адресно відсікав повторні приблизні рухові й швидкісні бонуси саме в hero-path (геройському шляху), а не по всіх звичайних піхотних агентах.

Через це мамлюк з дворучною булавою логічно міг лишатись поза цим контуром, якщо:

- у момент розгону він не проходив як `exact personal hero`;
- або його рухові бонуси все ще йшли через звичайну approximate-логіку;
- або швидкість повторно переобчислювалась уже після того коридору, який старий захист реально перекривав.

### 2. Guard у `3d5cf26` покривав лише pre-battle local client corridor

Коміт `3d5cf26` зупиняв локальні клієнтські перемикання зброї, коли ще триває передбойова відкладена активація.

Але якщо прискорення виникало:

- вже після старту бою;
- або після фактичної materialization (матеріалізації) safe-hold стану;
- або через інший runtime corridor (коридор виконання в рушії), який не проходить через ці локальні клієнтські методи;

то цей guard (захист) уже не був достатнім.

### 3. Approximate movement math не була прибрана системно для ordinary troops

У `Mission/CoopMissionBehaviors.cs` і далі лишались джерела приблизних рухових множників:

- `TryApplyEnduranceDrivenStats(...)`
- `TryApplyAthleticsPerkDrivenStats(...)`
- `TryApplyPartyQuartermasterDrivenStats(...)`
- частина корекцій у `TryApplyMountedHumanRidingDrivenStats(...)`

Старий partial fix (частковий фікс) не перебудовував всю схему так, щоб ordinary troop path (шлях звичайних бійців) гарантовано не отримував повторного множення швидкості при зміні зброї.

## Чому мамлюк з дворучною булавою все одно міг розганятись

Найімовірніше пояснення за результатами дослідження таке:

1. Старий захист був точно не глобальним, а переважно hero-scoped (обмеженим героїчним шляхом).
2. Дворучна зброя якраз входила до набору, для якого робилось окреме приглушення швидкісних корекцій, але лише в `exactHeroPersonalProfileActive`.
3. Якщо конкретний мамлюк не потрапляв у цю гілку, він знову проходив через approximate weapon/movement adjustments (наближені корекції зброї та руху).
4. Додатково прискорення могло повторно активуватись не лише під час `SetWieldedItemIndexAsClient`, а і в пізнішому runtime-коридорі після бойової активації.

Тому спостереження "частково працювало, але мамлюк усе одно розганявся" повністю узгоджується з реальною архітектурою старого фіксу.

## Висновок

У замороженій гілці не існувало повного системного рішення проблеми прискорення при зміні зброї.  
Там було два часткових запобіжники:

1. точкове приглушення approximate speed/movement adjustments (приблизних корекцій швидкості зброї та руху) для exact-героїв;
2. блокування локальних передбойових native weapon-switch corridor (рідних коридорів перемикання зброї рушія) до завершення safe-hold матеріалізації.

Саме тому ефект був помітний, але не завершений.

## Що це означає для майбутнього повного фіксу

Для 100% стабільного виправлення треба буде окремо дослідити й закрити всі місця, де швидкість може переобчислюватись повторно для ordinary troops, а не лише для exact-героїв.

Мінімальний список цілей для такого дослідження:

1. повний ланцюг викликів під час перемикання зброї в пішого агента після materialization;
2. усі повторні входи в `UpdateAgentStats` і суміжні місця перерахунку `DrivenProperty`;
3. різницю між hero-path і ordinary troop path;
4. момент, коли зміна зброї вдруге запускає approximate movement logic;
5. окремо випадок з дворучною піхотною зброєю після зняття з коня або після старту бою.

Поточний висновок: заморожена гілка містила корисну частину досвіду, але не готовий фінальний фікс.

## Що реалізовано в поточній гілці `codex/v0.1.1-refresh`

Після окремого low-level дослідження (дослідження на нижньому рівні викликів і стейтів рушія) в поточній гілці було закрито дві реальні причини прискорення.

### 1. Стабільна baseline-схема (схема базових значень) для людських persistent driven properties

У `Mission/CoopMissionBehaviors.cs` було розділено дві категорії людських `DrivenProperty`:

- звичайні weapon-sensitive (чутливі до зміни активної зброї);
- persistent human properties (стійкі людські властивості), які не повинні щоразу отримувати нову базу від зміни зброї.

До persistent-групи винесено:

- `ArmorEncumbrance`
- `CombatMaxSpeedMultiplier`
- `TopSpeedReachDuration`

Для них введено окремі:

- `HumanPersistentDrivenPropertyBaselineSignature`
- `HumanPersistentDrivenPropertyBaselines`
- `HumanPersistentDrivenPropertyAccumulatedScales`

Практичний сенс цього кроку:

- зміна зброї більше не створює нову помилкову baseline (базову точку) для бойової швидкості пішого агента;
- вже застосований руховий масштаб не починає повторно накопичуватись як нова норма;
- ordinary troop path (шлях звичайного бійця) більше не залежить від того, яку зброю агент зараз тримає в руці, коли мова саме про стійкі швидкісні властивості.

### 2. Стабільна baseline-схема для exact defense perks (точних захисних перків)

У `MissionModels/CoopCampaignDerivedAgentStatCalculateModel.cs` було прибрано повторне множення поточних значень захисту саме на вже змінене значення.

Замість цього введено:

- `_exactDefenseDrivenPropertyBaselines`
- `GetExactDefenseDrivenPropertyBaseline(...)`
- `ResolveExactDefenseDrivenPropertyBaseline(...)`
- `TryApplyExactDefenseDrivenPropertyScale(...)`
- `TrySetDrivenProperty(...)`

Для `AthleticsFormFittingArmor` і `AthleticsIgnorePain` тепер береться стабільна baseline (база), а не вже зменшене або вже посилене поточне значення.

Практичний сенс цього кроку:

- `ArmorEncumbrance` більше не сповзає щораз нижче при повторних `UpdateAgentStats`;
- armor-driven properties (властивості броні) більше не отримують безкінечне повторне масштабування;
- exact hero path (точний геройський шлях) перестає псувати власні числа під час довгого бою.

## Підтвердження успішного прогону по логах

Для підтвердження було перевірено:

- `C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_143188.txt`
- `C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_117016.txt`
- `C:/ProgramData/Mount and Blade II Bannerlord/logs/rgl_log_144224.txt`
- `C:/ProgramData/Mount and Blade II Bannerlord/logs/watchdog_log_143188.txt`
- `C:/ProgramData/Mount and Blade II Bannerlord/logs/watchdog_log_117016.txt`
- `C:/ProgramData/Mount and Blade II Bannerlord/logs/watchdog_log_144224.txt`

### 1. Краш цього прогону не підтвердився

У трьох `watchdog`-логах не було зафіксовано `exception event` (подію винятку).  
Процеси завершилися без явного crash path (шляху крашу) саме в цьому прогоні.

### 2. Піхота більше не показує аномальний розгін

Авторитетна бойова телеметрія в цьому прогоні йде із server log (серверного логу) `rgl_log_143188.txt`.

У ньому для проблемних піших агентів видно вже нормальні значення:

- агент `77`, `mp_light_infantry_aserai_troop`: `MoveSpeedMod=0.05`
- агент `82`, `mp_light_infantry_aserai_troop`: `MoveSpeedMod=0.007`
- агент `56`, `mp_shock_infantry_aserai_troop`: `MoveSpeedMod=-0`
- агент `60`, `mp_shock_infantry_aserai_troop`: `MoveSpeedMod=0.058`
- агент `55`, `mp_shock_infantry_aserai_troop`: `MoveSpeedMod=-0.005`
- агент `62`, `mp_shock_infantry_aserai_troop`: `MoveSpeedMod=0.022`
- герой-агент `97`: `MoveSpeedMod=0.084`, `0.098`, `0`, `0.115`

Це принципово відрізняється від попередніх проблемних прогонів, де піхота доходила до значень на кшталт `2+`, `3+` і вище.

### 3. Великі `MoveSpeedMod` лишилися лише там, де вони очікувані

Ті великі значення, які все ще видно в логах:

- агент `158`: `MoveSpeedMod=6.354`
- агент `166`: `MoveSpeedMod=6.144`
- агент `262`: `MoveSpeedMod=5.941`

усі належать до `mp_coop_light_cavalry_aserai_troop`.

Тобто аномалія більше не сидить у пішому weapon-switch path (шляху перемикання зброї в піхоти), а великі числа лишилися в кінноті, де вони пояснюються нормальною бойовою кінематикою удару верхи.

### 4. Server-side exact defense drift (серверне сповзання exact defense бази) не повторився

У `rgl_log_143188.txt` видно лише стартове застосування точного захисного оверрайду:

- агент `97`: `ArmorEncumbrance=6.1->5.185`
- агент `132`: `ArmorEncumbrance=4.8->4.08`

Повторного server-side drift до `4.08 -> 3.468 -> 2.9478 ...` у цьому прогоні не видно.

## Поточний статус

На поточному прогоні проблема прискорення пішого бійця при зміні зброї вважається підтверджено виправленою для актуальної гілки `codex/v0.1.1-refresh`.

Що саме підтверджено:

- бій стартує і йде далі;
- явного крашу в перевірених `watchdog`-логах немає;
- піхота не показує попереднього аномального `MoveSpeedMod`;
- exact defense drift на серверній стороні не повернувся.

Що окремо не змішується з цією проблемою:

- client intermission exceptions (клієнтські винятки в міжраундовому меню), які видно в `rgl_log_117016.txt`, але вони не підтверджують повернення саме цього speed bug (бага швидкості).
