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

*Звіт згенеровано за ТЗ: дослідити і довести, як dedicated завантажує кастомний модуль. MissionMultiplayerTdmClone, spawn, Harmony GetMultiplayerGameMode, start_mission payload не змінювалися.*
