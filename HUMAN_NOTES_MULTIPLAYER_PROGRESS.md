# HUMAN_NOTES_MULTIPLAYER_PROGRESS.md (v1.7)

## Етапи виконано (статус)

| Етап | Статус | Примітка |
|------|--------|----------|
| Етап 1 (підготовка, середовище, Hello World) | ✅ Виконано | План bannerlord_coop_plan.md §1 |
| Етап 2 (Spectator: broadcaster, UI, блокування контролю) | ✅ Виконано | §2 |
| Етап 3.1 (детекція початку битви у хоста) | ✅ Виконано | BattleDetector, BATTLE_START |
| Dedicated Helper — запуск (Етап 1) | ✅ Виконано | coop.dedicated_start, токен, конфіг, start_game з конфігу |
| Dedicated Helper — IPC (Етап 3b) | ✅ Виконано | BattleDetector → SendStartMission/SendEndMission, Manager API (GET /Manager/…), Dashboard AdminPassword |
| Етап 3.2 (клієнт через Custom Server List) | 🔄 Тестування | Потрібно перевірити: клієнт Join → хост у битву → чи клієнт переходить у місію |
| Етап 3.2.1 (вхід клієнта в MP-місію, TCP/coop_join) | ⏳ Не реалізовано | Дослідження Multiplayer DLL; відкриття місії на клієнті |
| Етап 3.3–3.5 (меню юніта, spawn, повернення spectator) | ⏳ Далі по плану | Після робочого підключення клієнта |

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

- **Manager API** (перевірено в DevTools): GET `http://127.0.0.1:7210/Manager/start_mission` та `/Manager/end_mission` — використовуються в `TrySendCommandViaHttp`; у грі показуються повідомлення типу "Coop: start_mission → dedik (HTTP)". Константа **ShowDedicatedCommandUiFeedback** у DedicatedServerCommands.cs — встановити `false`, щоб прибрати ці повідомлення з екрану.
- Запасний варіант: **stdin** процесу дедик-сервера (якщо запущений з моду), метод `DedicatedHelperLauncher.TrySendConsoleLine(line)`.
- **start_game**: автоматично через конфіг (AddConfigFileOnly). **Token doctor** (зроблено): пошук токена в обох варіантах папки, підказка в лог; викликається при `coop.dedicated_start` і `coop.dedicated_open_tokens`.

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
- **Мережа:** Порт **7210 (UDP і TCP)** відкритий на хостові (якщо клієнт на іншому ПК — фаєрвол/роутер). Для тесту на одній машині достатньо localhost.

### Як перевірити покроково

1. **Запуск дедик-сервера (хост)**  
   - Завантажити кампанію, відкрити консоль (`~` або `Alt+~`).  
   - Ввести: `coop.dedicated_start` (або `coop.dedicated_start 7210`).  
   - Очікуваний результат: у консолі гри повідомлення на кшталт "Dedicated Helper started (PID …, port 7210). Server visible in Custom Server List…". Вікно дедик-сервера відкрито, у його консолі немає "Disconnected from custom battle server manager".  
   - Якщо токен не знайдено — з’явиться підказка; виконати `customserver.gettoken` у мультиплеєрі, потім знову `coop.dedicated_start`.

2. **Сервер у списку (клієнт)**  
   - На клієнті: головне меню → **Multiplayer** → **Custom Server List**.  
   - Оновити список; має з’явитися сервер типу **"Coop Spectator"** (або назва з конфігу).  
   - Якщо не з’являється: перевірити на хостові консоль дедик-сервера (чи виконано start_game, чи є рядки про listening); перевірити порт 7210 і фаєрвол.

3. **Підключення клієнта**  
   - Натиснути **Join** на сервері "Coop Spectator".  
   - Очікуваний результат: клієнт заходить у intermission (екран очікування/лобі дедик-сервера), без таймаутів і повідомлень про некоректний стан.  
   - Якщо помилка "Connection is not at the correct state" — це повідомлення від офіційного лобі; почекати 5–10 с після відкриття Multiplayer і спробувати знову; не використовувати перед цим `coop.dedicated_join_local` / `coop.test_mp_join` без перезапуску гри.

4. **start_mission / end_mission (хост у битві)**  
   - На хостові зайти в кампанію і **увійти в битву** (наприклад, атакувати ворога на карті).  
   - Очікуваний результат: у грі хоста коротке повідомлення "Coop: start_mission → dedik (HTTP)" (якщо не вимкнено в коді).  
   - На клієнті: перевірити, чи **дедик перевів клієнта в місію** (завантаження сцени, поява битви). Якщо так — потік Custom Server List + start_mission працює.  
   - Вийти з битви на хостові (втеча або завершення). Очікуваний результат: повідомлення "Coop: end_mission → dedik (HTTP)"; клієнт повертається в intermission.

5. **Додатково (Dashboard)**  
   - У браузері відкрити `http://localhost:7210`, пароль **coopforever**.  
   - Переконатися, що вкладка Manager/Terminal відкривається (при потребі можна вручну натиснути End Mission / Start Mission для перевірки).

### Короткий чек-лист перевірки

- [ ] Хост: `coop.dedicated_start` без помилок, вікно дедик-сервера без "Disconnected".  
- [ ] Клієнт: Custom Server List показує сервер "Coop Spectator".  
- [ ] Клієнт: Join успішний, екран intermission.  
- [ ] Хост заходить у битву → у грі хоста з’являється "start_mission → dedik (HTTP)" (або запис у лог).  
- [ ] Клієнт переходить у місію (завантаження битви) після start_mission.  
- [ ] Хост виходить з битви → "end_mission → dedik (HTTP)" → клієнт знову в intermission.
