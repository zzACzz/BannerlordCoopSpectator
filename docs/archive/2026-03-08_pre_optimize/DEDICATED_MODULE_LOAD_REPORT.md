# Звіт: як dedicated завантажує кастомний модуль

**Мета:** З’ясувати, яким способом CoopSpectatorDedicated може бути реально завантажений dedicated server’ом без поломки manager/helper connection.

**Доказ завантаження:** лише реальне виконання `OnSubModuleLoad()` (файл у Temp або рядок у консолі дедика). Наявність DLL на диску не є доказом.

---

## 1. Фактичний command line реального dedicated process

- **Хто стартує:** Helper запускає **DedicatedCustomServer.Starter.exe** (WorkingDirectory = папка exe). Аргументи залежать від режиму: при **SteamLikeLaunch=true** передаються лише safe args: `--multihome 0.0.0.0`, `--port <port>`, опційно `/dedicatedcustomserverconfigfile <file>`. Без _MODULES_.
- **Дочірній процес:** Starter часто породжує **дочірній процес** (наприклад DedicatedCustomServer.exe), у чиїй консолі відображається лог і вводяться команди. Саме цей процес виконує ігровий рушій і парсить command line.
- **Де взяти точний command line:**
  - У **логі гри** (rgl_log_*.txt) після `coop.dedicated_start` шукати:
    - `DedicatedHelper [after Start (Starter)] PID=…` — це PID Starter.
    - `DedicatedHelper [WMI PID <pid>] CommandLine=…` — command line процесу з цим PID (спочатку Starter, потім, якщо знайдено дочірній, — child).
    - `DedicatedHelper: switched to child process StarterPID=… ChildPID=…` — далі рядок `DedicatedHelper [WMI PID <ChildPID>] CommandLine=…` є **повним command line реального dedicated process** (того, що показує консоль і виводить "Command Args: ..." у своєму лозі).
  - У **консолі самого дедика** у перших рядках часто виводиться `Command Args: ...` — це той самий набір аргументів, що отримав процес.

**Приклад з твого логу:**  
`Command Args: --multihome 0.0.0.0 --port 7210 /dedicatedcustomserverconfigfile ds_config_coop_start.txt  _MODULES_*Native*Multiplayer*_MODULES_ ...`  
Тобто фактичний dedicated process отримує _MODULES_*Native*Multiplayer*_MODULES_ (без DedicatedCustomServerHelper і без CoopSpectatorDedicated). Цей рядок і є «точний command line» того процесу, який крутить місію.

---

## 2. Звідки dedicated бере список модулів

- **Джерело:** Список модулів береться з **command line** процесу, який запускає ігровий рушій (дочірній процес, напр. DedicatedCustomServer.exe). Ключове значення має аргумент у форматі **`_MODULES_*Module1*Module2*...*_MODULES_`**. Рушій парсить цей аргумент і завантажує лише зазначені модулі (і їх залежності).
- **Хто формує command line при SteamLikeLaunch:** Ми передаємо Starter лише `--multihome`, `--port`, опційно config file. **Starter сам додає решту**, зокрема блок `_MODULES_*Native*Multiplayer*_MODULES_`. Код Starter’а не в нашому репозиторії (це exe з інсталяції Dedicated Server); конфіг, з якого Starter міг би читати список модулів, у нашому проєкті не використовується і в стандартній інсталяції не налаштовується для кастомних модулів.
- **Що саме зараз підвантажується:** У твоєму лозі видно лише **Native** і **Multiplayer** у _MODULES_. DedicatedCustomServer (WebPanel, Helper тощо) підвантажуються як підмодулі Multiplayer, тому в самому _MODULES_ їх немає.
- **Де можна підставити CoopSpectatorDedicated:** Єдиний механізм, який рушій гарантовано підтримує, — це **наявність модуля в _MODULES_ у command line**. Тобто потрібно, щоб у фінальному command line дочірнього процесу було щось на кшталт `_MODULES_*Native*Multiplayer*...*CoopSpectatorDedicated*_MODULES_`. Це можливо лише якщо цей рядок передасть або Starter (ми не керуємо його кодом), або ми самі, передавши його в args при запуску Starter. При передачі нами Starter все одно додає свій блок _MODULES_, що призводить до проблеми нижче.

---

## 3. Чи можна підключити CoopSpectatorDedicated без поломки helper

**Висновок: ні**, у поточному helper flow без зміни поведінки Starter — не можна.

- Якщо **не** передавати _MODULES_ (як зараз при SteamLikeLaunch): Starter підставляє лише `*Native*Multiplayer*`, наш модуль у список не потрапляє, OnSubModuleLoad нашого SubModule не викликається.
- Якщо **передавати** наш _MODULES_ (наприклад, з DedicatedCustomServerHelper і CoopSpectatorDedicated): Starter все одно додає **свій** блок _MODULES_*Native*Multiplayer*_MODULES_. Парсер гри використовує **останній** _MODULES_ у рядку → наш список замінюється на тільки Native+Multiplayer → DedicatedCustomServerHelper зникає зі списку → **Disconnected from custom battle server manager**.

Тобто або наш модуль не завантажується, або ламається підключення до manager.

---

## 4. Чому саме _MODULES_ ламає manager (одна конкретна причина)

**Причина:** Starter додає свій блок _MODULES_ **після** наших аргументів. У результаті в command line спочатку йде наш _MODULES_*...*DedicatedCustomServerHelper*...*_MODULES_, а потім блок _MODULES_*Native*Multiplayer*_MODULES_. Рушій (або його парсер аргументів) при наявності кількох _MODULES_ використовує **останній** — тобто лише Native і Multiplayer. Список модулів, які реально завантажуються, стає без DedicatedCustomServerHelper. Без Helper процес не підключається до custom battle server manager коректно → з’являється "Disconnected from custom battle server manager".

Тобто проблема не в тому, що «немає другого _MODULES_» і не лише в «порядку модулів», а в тому, що **Starter підставляє свій _MODULES_ в кінці, і саме він перезаписує наш** (останній виграває). Можливість підключити кастомний модуль без поломки manager у поточній схемі (Helper → Starter → child) відсутня, поки Starter так поводиться.

---

## 5. Мінімальний безпечний експеримент (крок 3)

У коді додано **тимчасовий debug-режим** (не для production):

- **DedicatedHelperLauncher.cs:** константа `DebugTryAddCoopSpectatorDedicatedToModules = false`. Якщо поставити **true**, при SteamLikeLaunch до safe args додасться `_MODULES_*Native*Multiplayer*DedicatedCustomServerHelper*CoopSpectatorDedicated*_MODULES_`.
- У лог пишеться: `DedicatedHelper [DEBUG] Added _MODULES_ with CoopSpectatorDedicated. Expect Starter to append its own _MODULES_ and overwrite → Disconnected. Check child process CommandLine in log.`
- Після запуску в лозі гри з’являються Starter PID, потім (якщо є дочірній процес) Child PID і **повний CommandLine дочірнього процесу** (WMI). У консолі дедика очікується "Disconnected from custom battle server manager", а в CommandLine дочірнього процесу — останній _MODULES_ лише *Native*Multiplayer*.

Експеримент підтверджує: при передачі нашого _MODULES_ Starter перезаписує його своїм, тому кастомний модуль так підключити без поломки manager не можна.

---

## 6. Рекомендований наступний крок

1. **Зняти фактичний command line** (для підтвердження): один раз запустити `coop.dedicated_start`, дочекатися появи в лозі рядків `DedicatedHelper: switched to child process...` та `DedicatedHelper [WMI PID <ChildPID>] CommandLine=…` і зберегти цей CommandLine у звіт/нотатки — це і є точний command line реального dedicated process.
2. **Варіанти далі (без зміни TDMClone/spawn/Harmony/start_mission):**
   - Шукати офіційний спосіб конфігурування модулів для Dedicated Server (лаунчер Steam, конфіг у папці Dedicated Server тощо), якщо Taleworlds надає можливість додати модуль без зміни command line з боку Starter.
   - Або розглянути запуск **не через Starter**, а напряму **core exe** (якщо він є в інсталяції і приймає _MODULES_), і передавати повний набір args із нашого боку — з обережністю (інша робоча директорія, інші залежності).
   - Залишати поточний production flow (SteamLikeLaunch, без нашого _MODULES_) і не очікувати виконання OnSubModuleLoad нашого dedicated-модуля, поки не з’явиться підтримка з боку Starter або офіційний спосіб підключення кастомного модуля.

---

---

## ТЗ 1 — окремий debug launch для modded dedicated

Додано окремий режим **DebugModdedDedicatedLaunch** (константа в `DedicatedHelperLauncher.cs`, за замовчуванням `false`). Коли `true`:

- Запускається **DedicatedCustomServer.Starter.exe** з явним _MODULES_: `_MODULES_*Native*Multiplayer*CoopSpectatorDedicated*_MODULES_` (без SteamLikeLaunch).
- Передаються `--multihome 0.0.0.0`, `--port`, опційно `/dedicatedcustomserverauthtoken`, `/LogOutputPath` у `%TEMP%\CoopSpectatorDedicated_logs`.
- У лог виводяться: повний exe, workingDir, args, рядок _MODULES_, PID Starter, PID child (якщо є), шляхи до `%TEMP%\CoopSpectatorDedicated_loaded.txt`, `CoopSpectatorDedicated_error.txt`, папки LogOutputPath.

**Як перевірити:** увімкни `DebugModdedDedicatedLaunch = true`, виконай `coop.dedicated_start`. Якщо після старту дедика з’явився файл `%TEMP%\CoopSpectatorDedicated_loaded.txt` — модуль завантажився. Якщо ні — або _MODULES_ не дійшов до процесу, або SubModule.xml/DLL/залежності неправильні.

---

## ТЗ B1 — чотири launch preset'и для таблиці

У **DedicatedHelperLauncher.cs** константа **`LaunchPresetB1`**: **0** = PlainOfficialArgs, **1** = ModdedMixedArgs, **2** = ModdedOnly, **3** = ModdedOnlyWithToken. **-1** = вимкнено (звичайний flow).

Для кожного preset у лозі: **exact command line**, підказка перевірити в консолі дедика: *CoopSpectatorDedicated minimal mode active? dashboard startup? Logging in? Login Failed? Disconnected from custom battle server manager?*

**Таблиця (заповнити після тестів):**

| launch preset       | module load (y/n) | manager login (y/n) | dashboard (y/n) | shutdown reason |
|---------------------|-------------------|----------------------|-----------------|------------------|
| PlainOfficialArgs   | y                 | y                    | y               |                  |
| ModdedMixedArgs     |                   |                      |                 | Disconnected     |
| ModdedOnly          | y                 | y                    | y               |                  |
| ModdedOnlyWithToken |                   |                      |                 | Disconnected     |

- **PlainOfficialArgs**: без CoopSpectatorDedicated (лише --multihome, --port). Starter додає _MODULES_ сам.
- **ModdedMixedArgs**: наш _MODULES_ + другий _MODULES_ + /dedicatedcustomserver ... /playerhosteddedicatedserver (відомий падаючий кейс).
- **ModdedOnly**: тільки наш _MODULES_ + /LogOutputPath, без другого _MODULES_, без /dedicatedcustomserver.
- **ModdedOnlyWithToken**: як ModdedOnly + /dedicatedcustomserverauthtoken.

---

## ТЗ C1 — еталонний робочий modded launch (ModdedOfficialNoTokenArg)

Константа **`ModdedOfficialNoTokenArg`** у DedicatedHelperLauncher: коли **true**, запуск у named mode **ModdedOfficialNoTokenArg**:

- **_MODULES_***Native*Multiplayer*CoopSpectatorDedicated*_MODULES_
- **/LogOutputPath** у `%TEMP%\CoopSpectatorDedicated_logs`
- **official tail**: `/dedicatedcustomserver 7210 USER 0 /playerhosteddedicatedserver`
- **Без** `/dedicatedcustomserverauthtoken` (токен лише з офіційного оточення/файлів)

У лог виводиться: *Using token from official environment/files, not from arg.*

---

## ТЗ C2 — нормалізація official tail (без дублювання)

У **DedicatedHelperLauncher** перед кожним запуском викликається **NormalizeDedicatedArguments(arguments)**:

- Якщо рядок вже містить **/playerhosteddedicatedserver** більше одного разу — дублікат блоку ` /dedicatedcustomserver ... /playerhosteddedicatedserver` видаляється.
- Якщо вже є **_MODULES_*Native*Multiplayer*_MODULES_** більше одного разу — дублікат видаляється.

Константи для guard: **OfficialModulesTail**, **PlayerHostedSuffix**, **DedicatedCustomServerPrefix**. Нормалізація застосовується для ModdedOfficialNoTokenArg, B1, DEBUG Modded і основного flow перед Process.Start.

---

## C2 cleanup: хто формує tail, прибирання дубля

**Хто формує перший і другий tail у Command Args:**

1. **Перший tail** (наш helper): **BuildArgumentsModdedOfficialNoTokenArg** раніше додавав до рядка аргументів блок `/dedicatedcustomserver &lt;port&gt; USER 0 /playerhosteddedicatedserver`. Цей рядок ми передаємо **DedicatedCustomServer.Starter.exe** як Arguments при Process.Start.
2. **Другий tail** (Starter): **DedicatedCustomServer.Starter.exe** при запуску **дочірнього** процесу (той, що виводить Command Args у консоль) сам **дописує** свій фіксований блок: `_MODULES_*Native*Multiplayer*_MODULES_ /dedicatedcustomserver &lt;port&gt; USER 0 /playerhosteddedicatedserver`. Код Starter не в репозиторії (exe з інсталяції).
3. **Іншого wrapper’а** немає: ланцюжок це наш процес → Starter → child (DedicatedCustomServer / рушій).

**Що ми додаємо самі (modded official flow):**

- `--multihome 0.0.0.0`
- `--port &lt;port&gt;`
- опційно `/dedicatedcustomserverconfigfile ds_config_coop_listed_test.txt`
- `_MODULES_*Native*Multiplayer*CoopSpectatorDedicated*_MODULES_`
- `/LogOutputPath "..."`

**Що гарантовано додає Starter автоматично:**

- `_MODULES_*Native*Multiplayer*_MODULES_`
- `/dedicatedcustomserver &lt;port&gt; USER 0 /playerhosteddedicatedserver`

**Фікс дублювання:** У **BuildArgumentsModdedOfficialNoTokenArg** більше **не** додаємо `/dedicatedcustomserver ... /playerhosteddedicatedserver`. До Starter передаємо лише наш набір вище; Starter один раз дописує official tail при формуванні command line дочірнього процесу. У фінальному Command Args лишається **один** коректний launch path.

**Чому дубль був:** Ми додавали official tail у нашому рядку, а Starter теж додавав його при збиранні child command line → у child потрапляло два рази один і той самий блок.

**Чому сервер раніше жив попри дубль:** Рушій/парсер аргументів, ймовірно, використовує **останній** блок (наприклад останній _MODULES_ / останній /dedicatedcustomserver) або ігнорує дублікати, тому один коректний набір застосовувався і сервер працював. Дубль лише засмічував лог.

**Єдиний правильний варіант args тепер:** Наш рядок до Starter = тільки наші args (multihome, port, config file якщо є, _MODULES_ з CoopSpectatorDedicated, LogOutputPath). Фінальний Command Args у child = наш рядок + один блок від Starter (`_MODULES_*Native*Multiplayer*_MODULES_ /dedicatedcustomserver &lt;port&gt; USER 0 /playerhosteddedicatedserver`). Перед стартом у лог виводиться **LaunchPlan**: mode=ModdedOfficial, OurArgs=..., ExpectedStarterAddsArgs=true, ConfigInjectionMode=ListedTest|None.

**Після cleanup:** Дубль `/dedicatedcustomserver ... /playerhosteddedicatedserver` зник. У Command Args усе ще **два блоки _MODULES_**: (1) наш `_MODULES_*Native*Multiplayer*CoopSpectatorDedicated*_MODULES_`, (2) блок від Starter `_MODULES_*Native*Multiplayer*_MODULES_`.

---

## Подвійний _MODULES_: норма чи зайве дублювання

- **Чи це норма:** Так. Ми **не можемо** прибрати наш блок (потрібен CoopSpectatorDedicated). Starter **завжди** дописує свій блок; ми не керуємо його кодом. Тому два блоки _MODULES_ у фінальному Command Args — **очікувана структура** поточного flow (Helper → Starter → child).
- **Шкідливий чи косметичний:** У поточному робочому стані (listed dedicated, клієнт приєднується, start_mission/end_mission) наш модуль завантажується і manager стабільний. Тобто рушій або використовує **перший** _MODULES_ (наш), або якось об’єднує обидва. Другий блок (від Starter) у будь-якому випадку **не ламає** роботу — подвійний _MODULES_ можна вважати **косметичним** (надлишковий другий блок у рядку) або частиною офіційної схеми злиття args. Додаткового cleanup до «одного _MODULES_» без зміни Starter неможливо.

---

## Scene selection у working flow

- У конфігу listed-test ми задаємо **add_map_to_usable_maps mp_tdm_map_001 TeamDeathmatch** і start_game. Тим не менш у логах dedicated може фігурувати **mp_skirmish_spawn_test** (наприклад рядок «Selected scene: mp_skirmish_spawn_test»).
- **Звідки може братися mp_skirmish_spawn_test:** (1) дефолтна сцена рушія для стану intermission / «до першої місії»; (2) внутрішній fallback тестової сцени (skirmish); (3) лог може відноситися до іншого підсистеми (наприклад лобі/тест), а не до фактичної місії після start_mission. Код, що виводить «Selected scene», у нашому репозиторії відсутній — це лог з нативного dedicated/рушія.
- **Що перевірити:** у логах дедика знайти, де саме виводиться «Selected scene» (який клас/файл), і чи змінюється сцена на mp_tdm_map_001 після виконання start_mission або вибору карти в admin panel. Якщо фактична місія вже йде на mp_tdm_map_001, то mp_skirmish_spawn_test у лозі — лише дефолт/косметика.

---

## Висновок перед переходом до Етап 3.3

| Питання | Висновок |
|--------|----------|
| **Подвійний _MODULES_ шкідливий чи косметичний?** | Косметичний (або очікувана структура). Робочий стан підтверджує: наш модуль завантажується, manager стабільний. Прибрати другий блок без зміни Starter неможливо. |
| **Повторний цикл battle transition — стабільний milestone?** | Так. Підтверджено: вихід з бою в SP і повторний вхід у новий бій у тій самій dedicated-сесії без втрати працездатності; start_mission/end_mission працюють. Етап 3.2 можна вважати виконаним. |
| **Останній технічний борг перед 3.3** | (1) Дослідити джерело scene selection у логах (чому фігурує mp_skirmish_spawn_test; чи це дефолт/лобі, чи фактична місія). (2) За бажанням — зафіксувати фінальний launch path у одному місці (наш args + що додає Starter). |

---

## ТЗ C3 — coop.dedicated_start на modded official flow

Feature flag **`UseModdedDedicatedOfficialFlow = true`**: коли увімкнено, **coop.dedicated_start** використовує production-like modded launch:

- Dedicated запускається з **CoopSpectatorDedicated** у _MODULES_ (ті самі args, що в ModdedOfficialNoTokenArg).
- **Не передається** `/dedicatedcustomserverauthtoken`; токен лише з офіційного місця (Documents\...\Tokens).
- Після старту в лог: exact args, «token arg disabled by design», «expecting official token resolution».

Що перевірити: у консолі дедика — *CoopSpectatorDedicated minimal mode active*, *Logging in*, перший *RestObjectRequestMessage* успішний, далі *AliveMessage*, нема *Disconnected from custom battle server manager*; сервер у Custom Server List; dashboard/manager живий. Harmony та override TeamDeathmatch поки не повертати.

---

## Чому при ручному start_game сервер бере mp_skirmish_spawn_test, а не наш preset

**Джерело startup state у Steam-like / modded flow:**

- **Scene, GameType, ServerName, AdminPassword** задаються **тільки** якщо передано **config file** через `/dedicatedcustomserverconfigfile &lt;file&gt;`. Вміст файлу виконується рядок за рядком (AdminPassword, ServerName, GameType, add_map_to_usable_maps, start_game тощо).
- Якщо **config file не передається** (наприклад у modded official flow без `UseStartupConfigInModdedOfficialFlow`), dedicated стартує без конфігу — у логах може бути "Command file is null". Тоді:
  - **ServerName**, **AdminPassword**, **GameType** залишаються дефолтними (ванільні значення гри).
  - **start_game** не виконується автоматично — користувач вводить його вручну в консолі.
  - Після ручного **start_game** сервер вибирає **дефолтну сцену/режим** — у ванільній поведінці це часто **mp_skirmish_spawn_test** (тестова сцена з spawnpoint’ами), а не наш intended preset (наприклад **mp_tdm_map_001** для TDM).

**Висновок:** наш intended preset (scene, server name, admin password) застосовується **лише коли** ми пишемо startup config і передаємо його аргументом. У modded official flow раніше ми **не** викликали `TryWriteStartupConfig` і **не** передавали `/dedicatedcustomserverconfigfile`, тому при ручному start_game сервер використовував вбудований дефолт (mp_skirmish_spawn_test тощо).

**Що зроблено:** додано **UseStartupConfigInModdedOfficialFlow** і **TryWriteStartupConfigForListedTest**: при увімкненні в modded flow записується тестовий конфіг (scene=mp_tdm_map_001, ServerName=ZZZ_COOP_TEST_7210, AdminPassword=coopforever, add_map_to_usable_maps, start_game) і передається `/dedicatedcustomserverconfigfile ds_config_coop_listed_test.txt`. Це дозволяє перевірити появу сервера в Custom Server List з нашим ім’ям/паролем/сценою без повернення до token arg (який ламав manager). У лог виводяться явні [startup] значення: config applied, path, scene, gameType, serverName, adminPassword source, start_game sent via, а також прапорці SteamLikeLaunch, AddConfigFileOnly, AddPortOnly, AddTokenOnly, AddTokenAndPortOnly.

---

*Звіт згенеровано за ТЗ: дослідити і довести, як dedicated завантажує кастомний модуль. MissionMultiplayerTdmClone, spawn, Harmony GetMultiplayerGameMode, start_mission payload не змінювалися.*
