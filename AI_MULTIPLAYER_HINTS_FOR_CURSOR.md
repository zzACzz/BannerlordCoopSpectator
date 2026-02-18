# AI_MULTIPLAYER_HINTS_FOR_CURSOR.md (v1.5)

## Goal
Provide a decompiled, end‑to‑end map of vanilla Bannerlord MP (custom & matchmaker) so an LLM can:
- explain "MyPeer is null" when starting from campaign,
- and design a coop-campaign networking pipeline that reuses vanilla MP.

## Official dedicated/custom server reference
- Dedicated servers default UDP port: 7210. [page:0]
- `start_game` => server visible & accessible (intermission). [page:0]
- `start_mission` => mission mode. [page:0]
- `start_game_and_mission` shorthand. [page:0]

## 1) MP bootstrap and global handler

### 1.1 MultiplayerMain
`MultiplayerMain.Initialize(IGameNetworkHandler gameNetworkHandler)`:
- Sets `MBCommon.CurrentGameType = Single`
- `GameNetwork.InitializeCompressionInfos()`
- If not already initialized:
  - `GameNetwork.Initialize(gameNetworkHandler)`
- Sets PermaMute callback.

`MultiplayerMain.InitializeAsDedicatedServer(IGameNetworkHandler gameNetworkHandler)`:
- Sets `MBCommon.CurrentGameType = MultiServer`
- Initializes compression + GameNetwork.Initialize(handler)
- For dedicated: applies bandwidth & tick rate from `GameStartupInfo`. [page:0]

Static ctor:
- Initializes ServiceAddressManager and DiamondClientApplication (lobby).
- Sets `NetworkMain.GameClient` and `NetworkMain.CommunityClient`.

### 1.2 GameNetwork.Initialize(IGameNetworkHandler handler)
- `_handler = handler` (type: `IGameNetworkHandler`)
- Allocates:
  - `VirtualPlayers[1023]`
  - `NetworkPeers = new List<NetworkCommunicator>()`
  - `DisconnectedNetworkPeers = new List<NetworkCommunicator>()`
- `MBNetwork.Initialize(new NetworkCommunication())`
- `NetworkComponents = new List<UdpNetworkComponent>()`
- `NetworkHandlers = new List<IUdpNetworkHandler>()`
- `_handler.OnInitialize()`

Implication:
Any MP use (server or client) must call MultiplayerMain.Initialize*(handler) once so GameNetwork has a valid handler and network stacks.

## 2) Server/client start

### 2.1 Server start (listen-host)
`GameNetwork.StartMultiplayerOnServer(int port)`:
- Debug print
- `PreStartMultiplayerOnServer()`:
  - sets `MBCommon.CurrentGameType` to MultiServer or MultiClientServer based on IsDedicatedServer
  - `ClientPeerIndex = -1`
- `InitializeServerSide(port)`
- `StartMultiplayer()`

### 2.2 Client start
`GameNetwork.StartMultiplayerOnClient(string serverAddress, int port, int sessionKey, int playerIndex)`:
- Sets `MBCommon.CurrentGameType = MultiClient`
- `ClientPeerIndex = playerIndex`
- `InitializeClientSide(serverAddress, port, sessionKey, playerIndex)`
- `StartMultiplayer()`
- Registers network message handlers.

These are invoked from:
- `LobbyGameStatePlayerBasedCustomServer.HandleServerStartMultiplayer()` (server side)
- `LobbyGameStateMatchmakerClient.StartMultiplayer()` / `LobbyGameStateCustomGameClient.StartMultiplayer()` (client side).

## 3) Join flows

### 3.1 Matchmaker join (client)
- Lobby event: `LobbyGameClientHandler.HandleBattleJoining(BattleServerInformationForClient info)`
  - Waits until no LobbyPracticeState
  - Creates `LobbyGameStateMatchmakerClient`
  - `SetStartingParameters(handler, info.PeerIndex, info.SessionKey, info.ServerAddress, (int)info.ServerPort, info.GameType, info.SceneName)`
  - Pushes state
- `LobbyGameStateMatchmakerClient.StartMultiplayer()`:
  - `GameNetwork.StartMultiplayerOnClient(address, port, sessionKey, playerIndex)`
  - `BannerlordNetwork.StartMultiplayerLobbyMission(Matchmaker)`
  - `Module.CurrentModule.StartMultiplayerGame(gameType, scene)`

### 3.2 Custom game join (client)
- Lobby callback: `LobbyGameClientHandler.OnJoinCustomGameResponse(bool success, JoinGameData joinGameData, ...)`
  - if success:
    `Module.CurrentModule.GetMultiplayerGameMode(joinGameData.GameServerProperties.GameType).JoinCustomGame(joinGameData)`
- `MissionBasedMultiplayerGameMode.JoinCustomGame(JoinGameData joinGameData)`:
  - Create `LobbyGameStateCustomGameClient`
  - `SetStartingParameters(NetworkMain.GameClient,
                           joinGameData.GameServerProperties.Address,
                           joinGameData.GameServerProperties.Port,
                           joinGameData.PeerIndex,
                           joinGameData.SessionKey)`
  - PushState
- `LobbyGameStateCustomGameClient.StartMultiplayer()`:
  - `GameNetwork.StartMultiplayerOnClient(address, port, sessionKey, peerIndex)`
  - `BannerlordNetwork.StartMultiplayerLobbyMission(Custom)`

## 4) Host boot & accept (listen custom server)

### 4.1 Host boot
`LobbyGameStatePlayerBasedCustomServer.HandleServerStartMultiplayer()`:
1) `GameNetwork.PreStartMultiplayerOnServer()`
2) `BannerlordNetwork.StartMultiplayerLobbyMission(Custom)`
3) `Module.CurrentModule.StartMultiplayerGame(CustomGameType, CustomGameScene)`
4) Wait for `Mission.Current.State == Continuing`
5) `GameNetwork.StartMultiplayerOnServer(9999)`
6) If `_gameClient.IsInGame`:
   - `BannerlordNetwork.CreateServerPeer()`
   - if not dedicated: `GameNetwork.ClientFinishedLoading(GameNetwork.MyPeer)`
7) Chat privilege check

### 4.2 Accept path for custom join (server)
`LobbyGameClientHandler.OnClientWantsToConnectCustomGame(PlayerJoinGameData[] playerJoinData)`:
- Preconditions:
  - Mission.Current != null && Mission.State == Continuing
  - Not banned; capacity check vs MaxNumberOfPlayers
- Builds `PlayerConnectionInfo[]` with:
  - PlayerData, UsedCosmetics, IsAdmin, IpAddress, Name
- Calls:
  `GameNetwork.AddPlayersResult r = GameNetwork.HandleNewClientsConnect(infos, false)`

`GameNetwork.HandleNewClientsConnect(...)`:
- `AddPlayersResult r = GameNetwork.AddNewPlayersOnServer(infos, isAdmin)`
- if success: `_handler.OnNewPlayerConnect(info, r.NetworkPeers[i])`

`GameNetwork.AddNewPlayersOnServer(infos, serverPeer)`:
- `flag = MBAPI.IMBNetwork.CanAddNewPlayersOnServer(infos.Length)`
- For each info:
  - read "IsAdmin" param
  - call `AddNewPlayerOnServer(info, serverPeer, isAdmin)` -> NetworkCommunicator
- Return AddPlayersResult { NetworkPeers, Success = flag }

### 4.3 Core: AddNewPlayerOnServer
`GameNetwork.AddNewPlayerOnServer(PlayerConnectionInfo info, bool serverPeer, bool isAdmin)`:

- Index allocation:
  - if info == null: `num = MBAPI.IMBNetwork.AddNewBotOnServer()`
  - else: `num = MBAPI.IMBNetwork.AddNewPlayerOnServer(serverPeer)`

- SessionKey:
  - `int sessionKey = 0`
  - if !serverPeer: `sessionKey = GameNetwork.GetSessionKeyForPlayer()`
  - later: `networkCommunicator.SessionKey = sessionKey`
  - if !serverPeer: `GameNetwork.PrepareNewUdpSession(num, sessionKey)`

- Reconnect handling:
  - checks `DisconnectedNetworkPeers` by PlayerId; can reuse communicator and update index/params.

- New communicator:
  - if no reconnect: `communicator = NetworkCommunicator.CreateAsServer(info, num, isAdmin)`

- Register:
  - `VirtualPlayers[communicator.VirtualPlayer.Index] = communicator.VirtualPlayer`
  - if serverPeer && IsServer:
    - `ClientPeerIndex = num`
    - `MyPeer = networkCommunicator`
  - `networkCommunicator.SetServerPeer(serverPeer)`
  - `GameNetwork.AddNetworkPeer(networkCommunicator)`
  - `info.NetworkPeer = networkCommunicator`

- Broadcasts:
  - Send `CreatePlayer(...)` events to:
    - mission record,
    - existing peers,
    - new peer (for others),
    - also for disconnected peers.
- Notify handlers:
  - for each IUdpNetworkHandler in NetworkHandlers:
    - `udpNetworkHandler.HandleNewClientConnect(info)`
  - `GameNetwork._handler.OnPlayerConnectedToServer(networkCommunicator)`

## 5) SessionKey & UDP
`GameNetwork.GetSessionKeyForPlayer()`:
- `return new Random(DateTime.Now.Millisecond).Next(1, 4001);`

`GameNetwork.PrepareNewUdpSession(int peerIndex, int sessionKey)`:
- `MBAPI.IMBNetwork.PrepareNewUdpSession(peerIndex, sessionKey)` (native).

## 6) High-level explanation for "MyPeer is null"
- `MyPeer` on listen host is assigned only if:
  - `AddNewPlayerOnServer(info, serverPeer: true, ...)` is called on server side,
  - and `GameNetwork.IsServer` is true.
- Clients get valid PeerIndex/SessionKey only when:
  - server accepts via `OnClientWantsToConnectCustomGame` or matchmaker equivalent,
  - `CanAddNewPlayersOnServer` is true,
  - and `AddNewPlayerOnServer(info, serverPeer:false, ...)` runs, generating sessionKey and calling PrepareNewUdpSession.
- Starting “MP from campaign” without:
  - calling `MultiplayerMain.Initialize(handler)`,
  - running lobby/MP states (`LobbyGameStatePlayerBasedCustomServer`, `LobbyGameStateCustomGameClient` / Matchmaker),
  - starting lobby missions and MP game mode,
  - and letting `AddNewPlayerOnServer` path execute,
will leave `MyPeer` null and prevent mission opening.

## 7) Coop campaign implications
To reuse vanilla MP netcode from campaign:
- Must ensure GameNetwork is initialized with the proper IGameNetworkHandler via `MultiplayerMain.Initialize(new GameNetworkHandler())` (or equivalent).
- Must start host and client through the same LobbyGameState* sequences or reproduce their calls:
  - Host: PreStartMultiplayerOnServer → StartMultiplayerOnServer(port) after mission is ready → CreateServerPeer → ClientFinishedLoading(MyPeer) → accept clients via AddNewPlayerOnServer.
  - Client: StartMultiplayerOnClient(address, port, sessionKey, peerIndex) using values minted by server; ideally via LobbyGameStateCustomGameClient/Matchmaker.
- Mission switching should follow dedicated server model: intermission (lobby mission) → start_mission. [page:0]
## 8) Public one-click goal (Dedicated Helper design)
Target UX:
- Host clicks "Host Co-op" in campaign mod.
- A dedicated helper server starts in background, becomes visible in official Custom Server List.
- Clients join with one click; no IP sharing; connection persists across missions.

Constraints:
- Dedicated custom server hosting requires:
  - Multiplayer login (Diamond) to generate a token (`customserver.gettoken`).
  - Anonymous hosting not supported.
  - Token is tied to the Bannerlord account; recommended to keep private; expires after ~3 months. [page:0]
- Dedicated servers default UDP port 7210 (can be overridden via launch args). [page:0]
- Server becomes visible when `start_game` is issued; players wait in intermission; `start_mission` switches to mission mode. [page:0]

Implication:
- For a "public, streamer-friendly" coop mod, the cleanest solution is to host a dedicated server process (helper) that stays online and switches missions without disconnects (intermission <-> mission). [page:0]

## 9) How to integrate with our decompiled MP pipeline
Two-process topology:
A) Dedicated Helper (public server)
- Runs Multiplayer module + coop module.
- Uses official dedicated lifecycle (`start_game`, `start_mission`, etc.). [page:0]
- Keeps a persistent MP session; clients never reconnect between battles.

B) Campaign Host (singleplayer campaign client)
- Does NOT need to be visible in official server list.
- Controls the dedicated helper via local IPC (127.0.0.1 TCP / named pipe):
  - StartMission(scene, gameType, rules)
  - EndMission(results)
  - SyncCampaignState(delta snapshots)

Network notes:
- Vanilla MP peer/session handling (peerIndex/sessionKey + PrepareNewUdpSession) remains entirely inside the dedicated helper’s GameNetwork path; clients join via the standard StartMultiplayerOnClient flow.
- This removes the need for clients to know host IP; discovery is the official server browser entry itself. [page:0]

## 10) Dedicated Helper auth token (chosen approach)
We require Multiplayer login for hosting.

Official token flow:
- User logs into Bannerlord multiplayer lobby.
- Opens console (ALT + ~) and runs: `customserver.gettoken`. [page:0]
- Token is saved to: `Documents\\Mount & Blade II Bannerlord\\Tokens`. [page:0]
- Token is tied to the user account; recommended to keep private; expires after 3 months. [page:0]
- Token can alternatively be passed via launch argument:
  `/dedicatedcustomserverauthtoken [authentication_token_contents]` (then local token file is not required). [page:0]

Implementation decision:
- Do NOT attempt to automatically generate the token from campaign.
- Coop mod provides guided UX:
  - Detect token file presence in Documents\\...\\Tokens.
  - If missing/expired: show a step-by-step popup with a button "Open Tokens Folder" + exact console command.
  - Start Dedicated Helper only when token is present OR when user pastes token into the mod UI (optional) and we pass it via `/dedicatedcustomserverauthtoken ...`. [page:0]

Rationale:
- Keeps us aligned with official dedicated server hosting pipeline and avoids brittle automation.
