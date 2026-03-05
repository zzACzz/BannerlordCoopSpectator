# HUMAN_NOTES_MULTIPLAYER_PROGRESS.md (v1.9)

## Головна ідея моду (vision)

Я хочу створити кооперативний режим для Mount & Blade II: Bannerlord, де **хост** грає кампанію (Singleplayer/Campaign), паралельно піднімає *виділений сервер* (Dedicated Server) для мультиплеєра, і інші гравці можуть приєднуватись саме до *битв кампанії*.

Потік такий:
- Хост у кампанії входить у бій (encounter/mission у Campaign).
- Мод детектить старт бою і дає команду Dedicated Server стартувати відповідну MP-місію.
- Дані конкретної битви з кампанії (сторони, сцена, війська/юнiти та інше необхідне) переносяться у мультиплеєрну місію.
- Інші гравці заходять на цей dedicated і грають бій разом з хостом: за нього або проти нього, беручи під контроль юнітів.
- Після завершення MP-бою результат (втрати, полонені, перемога/поразка тощо) повертається назад у кампанію хоста.


## Де ми зараз


- **Хост:** Синглплеєр з модом → кампанія → `coop.dedicated_start` → дедик працює. При вході хоста в битву мод відправляє **start_mission** (HTTP web panel з логіном), при виході — **end_mission**. **Підтверджено:** коли хост у синглплеєрі входить у битву — на дедик-сервері теж починається битва (GET /Manager/start_mission після логіну).
- **Клієнт:** Запуск через **батник** `run_mp_with_mod.bat` → Multiplayer → Custom Server List → Join → екран **Awaiting Server**.
- **Що далі за планом:** Тест **повного циклу з клієнтом**: клієнт на Awaiting Server → хост заходить у битву → перевірити, чи клієнт **автоматично** переходить у місію (ванільний MP-флоу); потім хост виходить з битви → перевірити end_mission і повернення клієнта на Awaiting Server. Чек-лист нижче в розділі «Тест повного циклу (Етап 3.2)».


### Як вирішили минулі проблеми


| Проблема | Рішення |
|----------|---------|
| Клієнт на тій самій машині не міг приєднатися (лобі повертала публічний IP → NAT/RemoveNetworkPeer) | Патч **LobbyCustomGameLocalJoinPatch** (reflection) на `LobbyGameStateCustomGameClient.StartMultiplayer`: підстановка 127.0.0.1 замість публічного IP. Константа `PublicIpToReplace` у `Patches/LobbyCustomGameLocalJoinPatch.cs`. Dedicated запуск з `--multihome 0.0.0.0 --port 7210` (у `DedicatedHelperLauncher`). |
| Лаунчер у вкладці Мультиплеєр не показує наш мод (лише Native + Multiplayer) | Запуск клієнта через **батник** `run_mp_with_mod.bat`: виклик `Bannerlord.exe /multiplayer _MODULES_*Native*Multiplayer*Bannerlord.Harmony*CoopSpectator*_MODULES_` з папки гри. Файл у корені проєкту, шлях у батнику підлаштовується під інсталяцію. |
| Ручне копіювання мода в папку гри після збірки | Постбілд у **CoopSpectator.csproj** (таргет **DeployModToGame**): після Build копіюються `SubModule.xml` і вихід збірки в `$(BannerlordRootDir)\Modules\CoopSpectator` та `Modules\CoopSpectatorMP`. |


## Етапи виконано (статус)


| Етап | Статус | Примітка |
|------|--------|----------|
| Етап 1 (підготовка, середовище, Hello World) | ✅ Виконано | План bannerlord_coop_plan.md §1 |
| Етап 2 (Spectator: broadcaster, UI, блокування контролю) | ✅ Виконано | §2 |
| Етап 3.1 (детекція початку битви у хоста) | ✅ Виконано | BattleDetector, BATTLE_START |
| Dedicated Helper — запуск (Етап 1) | ✅ Виконано | coop.dedicated_start, токен, конфіг, start_game, --multihome 0.0.0.0 --port 7210 |
| Dedicated Helper — IPC (Етап 3b) | ✅ Виконано | BattleDetector → SendStartMission/SendEndMission, Manager API, Dashboard AdminPassword |
| Етап 3.2 (клієнт через Custom Server List) | 🔄 Частково | Join і Awaiting Server працюють; start_mission на сервері при вході хоста в битву — підтверджено. **Далі:** тест з клієнтом на Awaiting Server → перехід клієнта в місію та повернення при end_mission. |
| Етап 3.2.1 (вхід клієнта в MP-місію, TCP/coop_join) | ⏳ За потреби | Якщо ванільний флоу не переведе клієнта в місію |
| Етап 3.3–3.5 (меню юніта, spawn, повернення spectator) | ⏳ Далі по плану | Після робочого переходу клієнта в місію |


---


## 1) Що ми тепер знаємо “від і до”
1. Net-стек Bannerlord’а запускається через:
   - `MultiplayerMain.Initialize(new GameNetworkHandler())` (звичайний MP)
   - або `InitializeAsDedicatedServer(...)` (dedicated).
   Вони всередині викликають `GameNetwork.Initialize(handler)`.


2. `GameNetwork.Initialize(handler)`:
   - виставляє `_handler : IGameNetworkHandler`,
   - ініціалізує MBNetwork, списки peers, handlers,
   - викликає `handler.OnInitialize()`.


3. Хост (listen custom server) піднімається через:
   - `LobbyGameStatePlayerBasedCustomServer.HandleServerStartMultiplayer()`:
     - PreStartMultiplayerOnServer
     - StartMultiplayerLobbyMission(Custom)
     - StartMultiplayerGame(CustomGameType, CustomGameScene)
     - чекати Mission.State == Continuing
     - StartMultiplayerOnServer(9999)
     - CreateServerPeer + ClientFinishedLoading(MyPeer)


4. Join custom server:
   - клієнт просить join через lobby (Diamond),
   - lobby на хості викликає `OnClientWantsToConnectCustomGame(...)`
     - тут, якщо місія Running і є слоти:
       - збирається PlayerConnectionInfo[] і викликається
         `GameNetwork.HandleNewClientsConnect(...)`
       - через `AddNewPlayersOnServer` engine створює NetworkCommunicator:
         - виділяє Index (peerIndex),
         - генерує SessionKey (рандом 1..4000),
         - викликає `PrepareNewUdpSession(peerIndex, sessionKey)` в native-частині,
         - додає peer у NetworkPeers, розсилає CreatePlayer, викликає UDP handlers і `_handler.OnPlayerConnectedToServer`.
       - назад у lobby повертається масив з PeerIndex/SessionKey.


   - на клієнті lobby викликає
     `MissionBasedMultiplayerGameMode.JoinCustomGame(joinGameData)` →
     `LobbyGameStateCustomGameClient` →
     `GameNetwork.StartMultiplayerOnClient(address, port, sessionKey, peerIndex)`.


5. Усе це працює тільки якщо:
   - GameNetwork вже ініціалізований handler’ом,
   - місія на хості вже в `Continuing`,
   - серверний net-стек в режимі MultiServer/MultiClientServer,
   - і не порушені перевірки CanAddNewPlayersOnServer/MaxNumberOfPlayers.


## 2) Чому в коопі з кампанії MyPeer = null
Бо ми:
- не запускаємо `MultiplayerMain.Initialize(...)` + `GameNetwork.Initialize(GameNetworkHandler)`,
- не проходимо через LobbyGameState* (немає lobby mission, немає StartMultiplayerOnServer/Client у правильний момент),
- не викликаємо AddNewPlayerOnServer/PrepareNewUdpSession з campaign-коду.


У результаті:
- на хості MyPeer ніколи не виставляється,
- на клієнті StartMultiplayerOnClient або не викликається, або отримує “лівий” peerIndex/sessionKey,
- engine не створює повноцінний NetworkCommunicator, тому місія не може відкритися.


## 3) Конкретний план для кооп-кампанії (перша ітерація)


### 3.1 Ініціалізація net-стеку (один раз на сесію)
- З campaign-модуля (SubModule) при старті коопу:
  - викликати `MultiplayerMain.Initialize(new GameNetworkHandler())`
    (або знайти в коді, який саме handler використовується в ванілі і використати його).
  - це виставить `_handler`, ініціалізує MBNetwork і compression.


### 3.2 Хост: запуск server-side, поки гравці в campaign
- Створити або перевикористати MP-місію (аналог lobby mission), яка може працювати “у фоні”.
- Для хоста:
  - викликати щось максимально близьке до
    `LobbyGameStatePlayerBasedCustomServer.HandleServerStartMultiplayer()`:
    - PreStartMultiplayerOnServer()
    - BannerlordNetwork.StartMultiplayerLobbyMission(Custom)
    - StartMultiplayerGame(CustomGameType, CustomGameScene) — тут можна підставити “порожню” або технічну сцену
    - дочекатися Mission.State.Continuing
    - StartMultiplayerOnServer(hostPort)
    - CreateServerPeer()
    - ClientFinishedLoading(MyPeer)


- Після цього сервер повинен пропускати клієнтів через стандартний `OnClientWantsToConnectCustomGame` (або наш аналог, що викликає HandleNewClientsConnect).


### 3.3 Клієнт: приєднання до хоста
- Замість прямого “open mission по IP:port” робити:
  - або повноцінний Diamond-join (важче),
  - або свій простий handshake поверх TCP/HTTP:
    - хост по запиту видає `(peerIndex, sessionKey, address, port)` для кожного клієнта, використовуючи `AddNewPlayerOnServer(info, serverPeer:false, ...)` так само, як це робить LobbyGameClientHandler.
    - клієнт, отримавши ці дані, викликає:
      `GameNetwork.StartMultiplayerOnClient(address, port, sessionKey, peerIndex)`.


- Якщо йти ближче до ванілі — можна спробувати створити/заповнити `JoinGameData` і викликати
  `MissionBasedMultiplayerGameMode.JoinCustomGame(joinGameData)`, що автоматично пушне `LobbyGameStateCustomGameClient`.


### 3.4 Перемикання битв у кампанії
- Після того, як з’явився стабільний MP‑лейер (хост+клієнти в одній місії/ланці):
  - для кожної битви кампанії:
    - переводити всіх гравців у потрібну MP-місію (через `Module.CurrentModule.StartMultiplayerGame(gameType, scene)` або свій MissionCreator),
    - зберігати підключення (не закривати net-стек),
    - по завершенні битви повертатися в “intermission/кампанію”, не вимикаючи GameNetwork.


Це збігається з офіційною моделлю `start_game` (підключення + intermission) → `start_mission` (битва). [page:0]


## 4) Ризики/обмеження
- Частина логіки (MBAPI.IMBNetwork.*, PrepareNewUdpSession) — native, її не можна обійти чисто C#-ом.
  Треба або викликати ті самі API, або використовувати їх через вже наявні рантайм-методи.
- Diamond/lobby дає ще авторизацію/матчмейкінг; для LAN коопу це можна обійти, але тоді доведеться робити свій мінімальний handshake (ти все одно маєш права хоста).
- Глибока інтеграція з campaign UI/flow (збереження, пауза, сінглплеєрні стейти) — окрема задача поверх цієї мережевої схеми.


## 5) План “успішний мод = 1 клік + без перепідключень” (Dedicated Helper)
Рішення: робимо headless dedicated server у фоні (helper), який видимий в офіційному Custom Server List, а кампанія хоста керує ним через локальний IPC.


Чому dedicated:
- Офіційна модель: `start_game` робить сервер видимим, гравці в intermission; `start_mission` переводить у місію. Це ідеально під “один конект на всю сесію” (місії можна перемикати без disconnect). [page:0]
- Dedicated сервери за замовчуванням UDP 7210. [page:0]


Token/логін:
- Анонімний хостинг не підтримується.
- Токен генерується з клієнта Bannerlord у мультиплеєрі через `customserver.gettoken`, зберігається в Documents\\Mount & Blade II Bannerlord\\Tokens. [page:0]
- Токен прив’язаний до акаунта; рекомендують не ділитись; токен має термін дії ~3 місяці. [page:0]


UX:
- Host: натиснув “Host Co-op” → мод автоматично стартує helper dedicated у фоні + запускає `start_game` → сервер з’являється в списку.
- Client: Multiplayer → Custom Server List → `[COOP] <name>` → Join (1 клік).
- Бої: helper робить start_mission / rotation; кампанія синхронізується через IPC.


Результат:
- Нема пошуку IP.
- Нема перепідключень між боями (тільки перемикання місій у межах одного dedicated сеансу). [page:0]


## 6) Токен для public dedicated (як зробити максимально зручно)
Ми приймаємо умову: хост має мати доступ до Multiplayer login.


Офіційний шлях (найнадійніший):
- Зайти в Bannerlord multiplayer lobby.
- Відкрити консоль ALT + ~.
- Ввести `customserver.gettoken`. [page:0]
- Файл токена з’явиться тут:
  `Documents\\Mount & Blade II Bannerlord\\Tokens`. [page:0]
- Токен прив’язаний до акаунта, його не радять поширювати, і він expires через 3 місяці. [page:0]


Рішення для UX (без “магії”):
- Мод при натисканні Host:
  - перевіряє наявність токена в Tokens folder,
  - якщо немає — показує майстер (wizard) з інструкцією + кнопкою "Open Tokens Folder",
  - після появи токена — запускає helper dedicated автоматично.
- Опційно: дозволити вставити токен в UI та передавати helper’у через launch argument:
  `/dedicatedcustomserverauthtoken ...` (тоді файл не потрібен). [page:0]


## 4) Dedicated Helper — реалізовано (Етап 1)


- **coop.dedicated_start [port] [token]** — запускає Mount & Blade II Dedicated Server. Токен шукається в кількох папках: MyDocuments, OneDrive\\Documents, OneDrive\\Документи; файл `DedicatedCustomServerAuthToken.txt` має пріоритет.
- **coop.dedicated_open_tokens** — відкриває папку Tokens у провіднику (якщо токен не знайдено, підказка в консолі пропонує цю команду).
- Після запуску: **start_game** виконується автоматично з конфігу (файл `ds_config_coop_start.txt` у Modules\Native). У конфіг також записується **AdminPassword coopforever** — для входу в Dashboard (http://localhost:7210) → Terminal/Manager. Клієнти приєднуються через Multiplayer → Custom Server List (порт 7210 за замовчуванням).
- IPC для start_mission / end_mission з кампанії реалізовано (Етап 3b): виклики з BattleDetector, відправка через Manager API.


### Dedicated Helper — діагностика логів


- **Лог гри (rgl_log_*.txt)** — у `C:\ProgramData\Mount and Blade II Bannerlord\logs\` (або в папці гри). Тут з’являються рядки мода: `[CoopSpectator] INFO: DedicatedHelper: ...` і після останнього білду — `DedicatedHelper launch: exe=... | workingDir=... | args=...` (токен у args замаскований як `(token)`). Тут **немає** повідомлення "Disconnected from custom battle server manager".
- **"Disconnected from custom battle server manager"** виводить **процес дедик-сервера** (вікно DedicatedCustomServer.Starter.exe). Щоб порівняти з запуском зі Steam: потрібні перші 30–50 рядків з **консолі дедик-сервера** (або його лог-файлу, якщо є) при запуску з моду і при запуску зі Steam.
- Якщо в rgl_log є "wrote startup config" і "Server will be visible" — це старий білд (конфіг передавався). Після перезбірки має бути рядок "DedicatedHelper launch:" і повідомлення "In the server console type start_game" (без конфігу).


### Dedicated Helper — вирішення Disconnected (працює)


- **Причина**: Коли мод передавав токен + порт + наш _MODULES_ з DedicatedCustomServerHelper, Starter додавав свій блок _MODULES_*Native*Multiplayer*_MODULES_ (без Helper). Останній _MODULES_ перезаписував наш → сервер без Helper → Disconnected.
- **Рішення**: Запуск як Steam — не передавати наш _MODULES_ і конфіг. SteamLikeLaunch=true, AddTokenAndPortOnly=false: 0 args, WorkingDirectory = папка exe. Токен з Documents\\...\\Tokens. Starter сам додає свої args; AliveMessage ок.
- **Захист від регресії**: Якщо ввімкнути AddTokenAndPortOnly — у лог пишеться WARN, що цей режим ламає manager-конект.


### Dedicated Helper — ізоляція тестів (який arg ламає)


- У коді три окремі прапорці для тесту (ставити **лише один** true за раз): **AddPortOnly**, **AddTokenOnly**, **AddConfigFileOnly**. Перезапускати dedicated і дивитися: AliveMessage = ок, Disconnected = цей arg ламає. Мета: з’ясувати, чи ламає лише /port, лише token-arg, чи configfile.
- Якщо ламає тільки /port — токен можна буде передавати аргументом. Якщо ламає token-arg — лишаємось на токені з Documents.


### Dedicated Helper — інтеграція BattleDetector (Етап 3b, зроблено)


- **BattleDetector** при вході хоста в місію: після broadcast BATTLE_START викликається `DedicatedServerCommands.SendStartMission()`.
- При виході хоста з місії: викликається `DedicatedServerCommands.SendEndMission()`.


### Dedicated Helper — відправка команд (Етап 3b, реалізовано)


- **Пріоритет:** спочатку **stdin** процесу дедик-сервера (якщо він запущений з моду через `coop.dedicated_start`) — команда передається так само, як при ручному вводі в консолі; потім fallback **HTTP** (Manager API або інші URL), якщо процесу немає (наприклад, дедик запущений з Steam). У грі: "Coop: start_mission → dedik (stdin)" або "… → dedik (HTTP)".
- **У лозі:** шукати `SendCommand(…) sent via stdin` / `sent via HTTP`; для HTTP додатково логуються URL, StatusCode, початок body (діагностика endpoint).
- **HTTP (web panel з авторизацією):** основний робочий канал, коли stdin недоступний. Мод логіниться в web panel: GET `/Auth?ReturnUrl=%2F` → витягує `__RequestVerificationToken` з HTML → POST логін (password + token), зберігає cookie `.AspNetCore.Cookies`, потім GET `http://127.0.0.1:7210/Manager/start_mission` та `/Manager/end_mission` з цим cookie. Клас **WebPanelAuth** (EnsureSignedIn, CookieContainer); пароль з **DedicatedHelperLauncher.GetDashboardAdminPassword()** (той самий, що в конфігу AdminPassword).
- **start_game**: автоматично через конфіг (AddConfigFileOnly). **Token doctor** (зроблено): пошук токена в обох варіантах папки, підказка в лог; викликається при `coop.dedicated_start` і `coop.dedicated_open_tokens`.


### Dedicated Helper — Starter vs дочірній процес (діагностика PID)


- Мод запускає **DedicatedCustomServer.Starter.exe**; він може породити **дочірній процес** (наприклад DedicatedCustomServer.exe), у чиїй консолі ти вводиш команди вручну. Тоді stdin, записаний у Starter, ніхто не читає.
- **Після старту** у лозі шукай: `DedicatedHelper [after Start (Starter)] PID=… ProcessName=… MainModule.FileName=…` — це процес, у який мод зараз пише stdin.
- Якщо знайдено дочірній процес (WMI ParentProcessId), у лозі з’явиться: `DedicatedHelper: switched to child process PID=… Name=…` — далі stdin відправляється цьому процесу (у нього може бути StandardInput == null, тоді fallback на HTTP).
- **Зіставлення з Task Manager:** Task Manager → Details → увімкни колонку PID. Порівняй PID з логу з PID того вікна консолі, де вручну вводиш start_mission. Якщо різні — мод пише не в ту консоль; підміна на child має це виправити. Якщо після підміни в лозі `StandardInput is null for PID=…` — процес правильний, але stdin недоступний (тоді основний канал — HTTP через DevTools).


### Діагностика: якщо при вході в битву (синглплеєр) ніяких дій на сервері не відбувається


- **Очікувана поведінка:** Коли хост у кампанії заходить у битву (будь-який загін), **BattleDetector** має помітити появу `Mission.Current` і викликати `DedicatedServerCommands.SendStartMission()`. У грі з’являється повідомлення "Coop: start_mission → dedik (HTTP)" (або "… not sent (check game log)").
- **Що перевірити:**
  1. **Лог гри (rgl_log_*.txt)** — шукати рядки `[CoopSpectator]`:
     - `BattleDetector: mission entered (Mission.Current set)` — означає, що мод побачив вхід у місію.
     - `BattleDetector: not TCP host — sending start_mission to dedicated` — гілка для кампанії без coop.host (синглплеєр з дедиком).
     - `BattleDetector: SendStartMission() returned true/false` — чи команда відправлена. У лозі також: `SendCommand(…) sent via stdin` (дедик з моду) або `sent via HTTP`; для HTTP логуються URL, StatusCode, початок body.
     - Якщо цих рядків **немає** при вході в битву — можливо, `OnApplicationTick` не викликається під час завантаження битви або `Mission.Current` в кампанії встановлюється пізніше/інакше; тоді варто розглянути підписку на подію старту місії (наприклад, campaign encounter).
  2. **Дедик запущений:** `coop.dedicated_start` виконано, вікно дедик-сервера відкрито, у консолі дедику немає "Disconnected from custom battle server manager".
  3. **PID і процес:** у лозі є `DedicatedHelper [after Start]` та/або `[TrySendConsoleLine] PID=… ProcessName=…`. Порівняй цей PID з Task Manager (Details, колонка PID) — вікно консолі, куди вручну вводиш start_mission, має той самий PID. Якщо ні — див. розділ «Starter vs дочірній процес».
  4. **Dashboard на 7210:** у браузері відкрити `http://127.0.0.1:7210` — має відкритися панель (логін AdminPassword з конфігу). Якщо сторінка не відкривається — дедик не слухає 7210 або фаєрвол блокує.
- **Якщо start_mission у лозі повертає true, але дедик не переходить у mission mode** — перевірити консоль/лог самого дедик-сервера (чи отримує він HTTP-запит і що робить з командою).


### Етап 3.2 — наступний фокус: клієнт через Custom Server List


- **Поточний потік:** Хост кампанії + дедик (coop.dedicated_start) → при вході/виході з битви мод шле start_mission/end_mission на Manager API → дедик переходить у mission mode.
- **Що тестувати:** Клієнт приєднується через Multiplayer → Custom Server List до сервера "[COOP] Coop Spectator". Коли хост у кампанії заходить у битву (викликається start_mission) — чи дедик автоматично переводить уже підключених клієнтів у місію (ванільний MP-флоу). Тест: дві машини або два інстанси — одна campaign+dedik, друга клієнт; клієнт Join через Custom Server List; хост входить у битву → перевірити, чи клієнт отримує завантаження місії.
- **Альтернативний шлях (TCP):** Клієнт через `coop_join` отримує BATTLE_START по нашому TCP; ClientBattleNotification показує countdown; відкриття місії на клієнті ще не реалізовано (потрібне дослідження Multiplayer DLL, §3.2.1 плану).


---


## Що необхідно для тесту і як перевірити


### Що потрібно мати


- **Хост:** Один ПК з Bannerlord + мод Coop Spectator + встановлений **Mount & Blade II: Dedicated Server** (Steam → Інструменти).
- **Токен:** Згенерований у мультиплеєрі командою `customserver.gettoken` (консоль ALT+~), файл у `Documents\Mount & Blade II Bannerlord\Tokens\DedicatedCustomServerAuthToken.txt`. Якщо немає — команда `coop.dedicated_open_tokens` відкриє папку, підказка в консолі.
- **Клієнт:** Другий ПК з Bannerlord і модом, або друга копія гри на тому ж ПК (для тесту "до себе").
- **Мережа:** Порт **7210 (UDP і TCP)** відкритий на хостові (якщо клієнт на іншому ПК — фаєрвол/роутер). Для тесту на **одній машині** мод підставляє **127.0.0.1** замість публічного IP (патч **LobbyCustomGameLocalJoinPatch** / **LocalJoinAddressPatch**), щоб Join йшов на localhost; інакше лобі повертає публічний IP і підключення з того ж ПК часто не проходить через NAT.


### Лаунчер: вкладка Мультиплеєр не показує наш мод — рішення: батник


- **Проблема:** Лаунчер у вкладці **Мультиплеєр** показує лише **Native** і **Multiplayer**; сторонні моди (Coop Spectator, Coop Spectator (MP)) там не з’являються. У грі немає переходу з синглплеєру в мультиплеєр — це окремі точки входу.
- **Рішення (перевірено):** Запускати клієнта **не з лаунчера**, а через **батник** з викликом `Bannerlord.exe` з аргументом `/multiplayer` і списком модулів. У проєкті є файл **`run_mp_with_mod.bat`** (корінь репозиторію). Він переходить у `bin\Win64_Shipping_Client` гри і запускає:
  `Bannerlord.exe /multiplayer _MODULES_*Native*Multiplayer*Bannerlord.Harmony*CoopSpectator*_MODULES_`
  Шлях у батнику за замовчуванням: `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client` — підкоригуй під свою інсталяцію, якщо потрібно.
- Після запуску батника гра відкривається в режимі мультиплеєру **з модом CoopSpectator** (і патчем localhost для підключення на ту ж машину). Далі: Multiplayer → Custom Server List → Join → екран **Awaiting Server**. Хост на тій самій машині теж може приєднатися до свого дедик-сервера цим способом.


### Як перевірити покроково


1. **Запуск дедик-сервера (хост)**  
   - Завантажити кампанію, відкрити консоль (`~` або `Alt+~`).  
   - Ввести: `coop.dedicated_start` (або `coop.dedicated_start 7210`).  
   - Очікуваний результат: у консолі гри повідомлення на кшталт "Dedicated Helper started (PID …, port 7210). Server visible in Custom Server List…". Вікно дедик-сервера відкрито, у його консолі немає "Disconnected from custom battle server manager".  
   - Якщо токен не знайдено — з’явиться підказка; виконати `customserver.gettoken` у мультиплеєрі, потім знову `coop.dedicated_start`.


2. **Запуск клієнта (з модом)**  
   - **На одній машині або коли лаунчер не показує мод у вкладці Мультиплеєр:** запустити **`run_mp_with_mod.bat`** з кореня проєкту (шлях у батнику вказує на `...\Mount & Blade II Bannerlord\bin\Win64_Shipping_Client` — змінити під себе, якщо треба). Гра відкриється в мультиплеєрі з модулями Native, Multiplayer, Bannerlord.Harmony, CoopSpectator.  
   - Інакше: запуск з лаунчера, вкладка Мультиплеєр (без мода, тільки Native + Multiplayer).


3. **Сервер у списку і підключення (клієнт)**  
   - У грі клієнта: головне меню → **Multiplayer** → **Custom Server List** → оновити список → має з’явитися сервер "Coop Spectator".  
   - Натиснути **Join**. На **одній машині** мод (LobbyCustomGameLocalJoinPatch) підставить 127.0.0.1 замість публічного IP.  
   - Очікуваний результат: клієнт заходить на екран **Awaiting Server** (intermission), без RemoveNetworkPeer/таймаутів.  
   - Якщо помилка "Connection is not at the correct state" — почекати 5–10 с після відкриття Multiplayer і спробувати знову.  
   - **Два ПК:** у `Patches/LobbyCustomGameLocalJoinPatch.cs` константу `PublicIpToReplace` не використовувати для підстановки, або вимкнути патч для зовнішніх клієнтів.


4. **start_mission / end_mission (хост у битві)**  
   - На хостові зайти в кампанію і **увійти в битву** (наприклад, атакувати ворога на карті).  
   - Очікуваний результат: у грі хоста "Coop: start_mission → dedik (HTTP)"; на дедик-сервері починається битва (підтверджено).  
   - **Тест повного циклу:** клієнт має бути вже на екрані **Awaiting Server**. Перевірити: чи клієнт **автоматично** завантажує місію (перехід у битву). Якщо так — Етап 3.2 виконано.  
   - Вийти з битви на хостові (втеча або завершення). Очікуваний результат: "Coop: end_mission → dedik (HTTP)"; клієнт повертається на екран Awaiting Server.


5. **Додатково (Dashboard)**  
   - У браузері відкрити `http://localhost:7210`, пароль **coopforever**.  
   - Переконатися, що вкладка Manager/Terminal відкривається (при потребі можна вручну натиснути End Mission / Start Mission для перевірки).


### Тест повного циклу (Етап 3.2) — чек-лист


Виконуй по порядку; після виконання пунктів 4–5 Етап 3.2 можна вважати завершеним (або переходити до 3.2.1, якщо клієнт не переходить у місію).


1. [x] Хост: кампанія, `coop.dedicated_start`, дедик без "Disconnected".  
2. [x] Клієнт: запуск через `run_mp_with_mod.bat` → Multiplayer → Custom Server List → Join → екран **Awaiting Server**.  
3. [x] Хост заходить у битву (синглплеєр) → у грі хоста з’являється "start_mission → dedik (HTTP)" (перевірено окремо в синглплеєрі).  
4. [ ] **Клієнт на Awaiting Server:** хост у битві — чи клієнт **автоматично** завантажує місію і потрапляє в битву? (Якщо ні — див. Етап 3.2.1.)  
5. [ ] Хост виходить з битви → у грі "end_mission → dedik (HTTP)" → клієнт повертається на екран **Awaiting Server**.


### Таймаут ListedServer ("Couldn't start the game in time")


- **Причина:** не native crash, а навмисний kill з ListedServer: місія не доходить до стану "started/ready" за таймаут → `ServerSideIntermissionManager.Tick` кидає exception.
- **Що зроблено:** (1) Сцена змінена з `mp_skirmish_spawn_test` на ванільну TDM **mp_tdm_map_001** у конфігу дедика (TryWriteStartupConfig) і в fallback-списку PoC. (2) На дедику не додаються scoreboard/notifications; mission name = "MultiplayerTeamDeathmatch".
- **Діагностика повного exception:** перед запуском дедика з гри встановити `COOP_DEBUG_DEDICATED_STDIO=1`; після крашу переглянути **dedicated_stdout.log** (у working dir процесу дедиката) — туди пишуться stdout/stderr, включно з stack trace та inner exception.
- **Якщо таймаут лишиться:** тест з мінімальним списком behaviors на дедику (лише MissionLobby + CoopTdm + таймер), далі додавати по одному (team select, admin, spawn тощо), щоб знайти блокуючий. Детекція дедика зараз по імені процесу (Dedicated); при можливості замінити на GameNetwork.IsDedicatedServer.

“TDMClone (1:1 копія) — правило узгодження ID”
Проблема
Є кейс: ванільний TDM працює, але коли робиш копію того ж режиму з іншою назвою/ID, dedicated починає крашитись або ловиться таймаут ListedServer (“Couldn’t start the game in time”). Це майже завжди означає не “складність задачі”, а розсинхрон gameType ID між 3 місцями: payload start_mission (host), startup config/rotation (dedicated), registration game mode (MP модуль).
​
Правильне правило
Коли додаємо новий режим-клон (наприклад TdmClone), ми міняємо ID одночасно на клієнті/хості та на dedicated, щоб усі сторони бачили один і той самий GameTypeId. Інакше клієнт/хост просить стартанути одне, а dedicated налаштований/зареєстрований під інше → інтермісія не доходить до “started/ready”, таймаут/exception.

Мінімальний план “1:1 копія TDM”
Контрольна точка (працює): CoopTdm реєструється в MP модулі (“CoopTdm registered … Registered MP game mode CoopTdm”).

Host payload: в момент BattleDetector/BATTLE_START (або де формуєш команду) логуй і передавай gameTypeId. Для клону має бути TdmClone, і це має бути єдиний “бізнес‑змістовний” change.

Dedicated startup config/rotation: helper має писати конфіг симетрично до робочого прикладу, тобто аналог GameTypeCoopTdm, addmap mptdmmap001 CoopTdm, але для клону: GameTypeTdmClone, addmap mptdmmap001 TdmClone (карта та інше — без змін).

MP registration: у MP модулі має бути реєстрація під той самий ID (Registered MP game mode TdmClone), як у тебе вже є для CoopTdm.

Матриця перевірки Native TDM vs TDMClone: після кожного запуску звіряємо 3 рядки в логах: (a) registration, (b) host start_mission payload, (c) dedicated startup config — ID має збігатися у всіх трьох.

Практика для дебагу (щоб Cursor не “плавав”)
Зробити один shared const/enum GameTypeId і логувати його в 3 місцях: registration (MP), start_mission payload (host), startup config (dedicated helper). Ціль — за 1 запуск одразу бачити розсинхрон.

### TdmClone впроваджено (етап 1:1 копія)
- **CoopGameModeIds** (Infrastructure/CoopGameModeIds.cs): константи CoopTdm, TdmClone.
- **Реєстрація (дедик):** SubModule реєструє MissionMultiplayerTdmCloneMode; лог: `TdmClone registered. [ID check] GameTypeId=TdmClone`.
- **Startup config:** TryWriteStartupConfig пише GameType TdmClone та addmap mptdmmap001 TdmClone; лог: `[ID check] GameTypeId=TdmClone`.
- **Host:** SendStartMission логує `[ID check] expected GameTypeId on dedicated=TdmClone`.
- **multiplayer_strings.xml:** додано TdmClone ("TDM Clone").