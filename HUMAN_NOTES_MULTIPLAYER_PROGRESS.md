# HUMAN_NOTES_MULTIPLAYER_PROGRESS.md (v1.5)

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
- Після запуску: у консолі **дедик-сервера** ввести **start_game**, щоб сервер з’явився в Custom Server List. Клієнти приєднуються через Multiplayer -> Custom Server List (порт 7210 за замовчуванням).
- Далі по плану: IPC для автоматичної відправки start_game / start_mission / end_mission з кампанії (Етап 3b, 5).
