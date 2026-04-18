# Connectivity Contract: Public Official And VPN/Overlay

Date: 2026-04-18
Project: `BannerlordCoopSpectator3`
Scope: only connection bootstrap and join path. No spawn/perk/runtime rewrites.

## Baseline

- Battle/runtime is treated as protected baseline.
- This document only covers:
  - `PublicListed`
  - `VpnOverlay`
  - host self-join
  - remote join

## Native contract

### 1. Dedicated bootstrap

Native dedicated startup parses these command-line flags in `TaleWorlds.MountAndBlade.Module.StartupInfo`:

- `/dedicatedcustomserver <port> <region> <permission>`
- `/playerhosteddedicatedserver`
- `/dedicatedcustomserverconfigfile <file>`
- `/customserverhost <host>`

Observed native fields from decompile:

- `StartupInfo.ServerPort`
- `StartupInfo.ServerRegion`
- `StartupInfo.Permission`
- `StartupInfo.PlayerHostedDedicatedServer`
- `StartupInfo.CustomServerHostIP`

Native listed/custom-server start then flows as follows:

1. `ServerSideIntermissionManager.StartGame()` waits until the listed/custom server is connected and registered.
2. It computes `portToUse` and `regionToUse`.
3. If `PlayerHostedDedicatedServer` is true, native forces region to `USER`.
4. It calls `IServerSideIntermissionManagerHandler.OnGameStart(selectedGameType, selectedScene, uniqueSceneId, gameDefinitionId, gameModule, portToUse, regionToUse)`.
5. `DedicatedCustomServerIntermissionManagerHandler.OnGameStart(...)` calls:
   `DedicatedCustomGameServer.RegisterGame(..., Module.CurrentModule.StartupInfo.Permission, Module.CurrentModule.StartupInfo.CustomServerHostIP)`.
6. `CustomBattleServer.RegisterGame(...)` sends `RegisterCustomGameMessage(...)` to the custom battle server manager with:
   - `ServerName`
   - `ServerAddress`
   - `Port`
   - `Region`
   - `Permission`
   - `IsOverridingIP`

Important low-level detail:

- `CustomBattleServer.RegisterGame(...)` sets `ServerAddress` from either:
  - `Application.Parameters["CustomBattleServer.Host.Address"]`, or
  - the explicit `overriddenIP` argument.
- When `overriddenIP != ""`, native sets `IsOverridingIP = true`.

Inference from this contract:

- `PublicListed` should leave `ServerAddress` unset and let the manager use the observed public/listed address.
- `VpnOverlay` should set `ServerAddress=<overlay IP/DNS>` and `IsOverridingIP=true`, causing the manager to advertise that overlay address instead of the observed public address.

### 2. Client custom-game join

Native join flows as follows:

1. `LobbyClient.RequestJoinCustomGame(...)` sends `RequestJoinCustomGameMessage`.
2. The client receives `JoinCustomGameResultMessage`.
3. `LobbyClient.OnJoinCustomGameResultMessage(...)`:
   - runs `JoinGameData.GameServerProperties.CheckAndReplaceProxyAddress(...)`
   - stores `LastBattleServerAddressForClient`
   - stores `LastBattleServerPortForClient`
   - calls handler `OnJoinCustomGameResponse(...)`
4. `LobbyGameStateCustomGameClient.SetStartingParameters(...)` stores:
   - `address`
   - `port`
   - `peerIndex`
   - `sessionKey`
5. `LobbyGameStateCustomGameClient.StartMultiplayer()` calls:
   `GameNetwork.StartMultiplayerOnClient(address, port, sessionKey, peerIndex)`.
6. `GameNetwork.StartMultiplayerOnClient(...)` only forwards those values into `InitializeClientSide(...)`.

Native `JoinCustomGameResultMessage` payload is split exactly as:

- server name: `JoinGameData.GameServerProperties.Name`
- address: `JoinGameData.GameServerProperties.Address`
- port: `JoinGameData.GameServerProperties.Port`
- session key: `JoinGameData.SessionKey`
- peer index: `JoinGameData.PeerIndex`

Other directly available native fields:

- `JoinGameData.GameServerProperties.HostName`
- `JoinGameData.GameServerProperties.IsOfficial`
- `JoinCustomGameResultMessage.MatchId`
- `JoinCustomGameResultMessage.IsAdmin`

### 3. Host self-join vs remote join

Native remote join and host self-join are the same until the final address is about to be passed into `GameNetwork.StartMultiplayerOnClient(...)`.

Native does not rewrite public/VPN addresses to localhost.

That means:

- `localhost` rewrite is only valid for the same machine that launched the dedicated.
- Any global or broad localhost rewrite breaks remote peers.
- The correct place for host-only rewrite is after the native join result exists, but before `InitializeClientSide(...)` consumes the address.

## Current repo vs native contract

### Matches native contract

- `DedicatedServerHostingMode.VpnOverlay` maps to `/customserverhost "<advertised host>"`.
- `DedicatedServerHostingMode.PublicListed` leaves host override empty.
- `DedicatedHelperLauncher` modded official flow already passes `/customserverhost` only when `UsesAdvertisedHostOverride()` is true.
- `HostSelfJoinRedirectState` intentionally performs a one-shot host-only rewrite instead of a global localhost rewrite.

### Confirmed divergence

There is one confirmed code/native mismatch:

- `Patches/LobbyCustomGameLocalJoinPatch.cs` targeted:
  - assembly `TaleWorlds.MountAndBlade.Lobby`
  - type `TaleWorlds.MountAndBlade.Lobby.LobbyGameStateCustomGameClient`
- Native 1.3.14 actually uses:
  - assembly `TaleWorlds.MountAndBlade.Multiplayer.dll`
  - type `TaleWorlds.MountAndBlade.LobbyGameStateCustomGameClient`

Observed proof from live logs:

- `LobbyCustomGameLocalJoinPatch: TaleWorlds.MountAndBlade.Lobby not loaded, skip.`

So our intended authoritative lobby-stage patch point was not attached to the real native type.

### Current self-join behavior already proven

Live logs already prove:

- native join result patch armed the one-shot self-join rewrite
- `GameNetwork.StartMultiplayerOnClient` consumed it

Observed proof:

- `HostSelfJoinRedirectState: armed localhost self-join rewrite. serverName=ZZZ_COOP_TEST_7210 address=85.238.97.249 port=7210.`
- `HostSelfJoinRedirectState: GameNetwork.StartMultiplayerOnClient redirecting own dedicated join "85.238.97.249" -> 127.0.0.1 ...`

So host self-join is currently working at the final `GameNetwork` stage, even though the earlier lobby-stage patch target was wrong.

## Blocker list

### Public official

- Confirmed fixed-in-code blocker:
  - wrong native target for `LobbyGameStateCustomGameClient` patch
- Remaining validation blocker:
  - no fresh remote non-host log set proving the public-listed join result address/port and final `StartMultiplayerOnClient(...)` address on another machine
- Remaining bootstrap proof blocker:
  - no fresh dedicated-side log proving the started process parsed the expected native `StartupInfo` values on the current build

### VPN / overlay

- Contract looks correct from decompile:
  - `/customserverhost` reaches `StartupInfo.CustomServerHostIP`
  - `CustomServerHostIP` is passed into `RegisterGame(..., overriddenIP)`
  - `RegisterCustomGameMessage` carries `ServerAddress` plus `IsOverridingIP`
- Remaining validation blocker:
  - no fresh log-backed run proving the dedicated parsed `CustomServerHostIP=<overlay address>` and that the client join result address matches the same overlay address
- Remaining connectivity blocker:
  - no fresh remote overlay peer log proving there is no accidental localhost rewrite for non-host peers

### Self-join

- Confirmed fixed-in-code blocker:
  - wrong lobby-stage patch target prevented authoritative diagnostics at `LobbyGameStateCustomGameClient.StartMultiplayer(...)`
- Remaining validation blocker:
  - rerun needed to confirm arm -> lobby handoff -> final `GameNetwork` consumption ordering on the current build

### Remote join

- Intent looks correct:
  - only a one-shot host-local rewrite exists
- Remaining validation blocker:
  - rerun needed to show `selfJoinRedirect=false` on non-host joins
- Remaining proof blocker:
  - need logs from a second machine/network path for both `PublicListed` and `VpnOverlay`

## Diagnostics added in this branch

One authoritative line per step:

- `DedicatedHelperLauncher`
  - resolved launch settings
  - final starter args
- dedicated runtime `SubModule`
  - parsed native `StartupInfo`
- `LobbyJoinResultSelfJoinArmPatch`
  - native join-result payload handling
- `LobbyCustomGameLocalJoinPatch`
  - lobby handoff address before `GameNetwork`
- `LocalJoinAddressPatch`
  - final address actually passed to `GameNetwork.StartMultiplayerOnClient(...)`

## Practical rerun checklist

For `PublicListed`, the next rerun should prove:

1. dedicated startup log shows `CustomServerHostIP=(default)`
2. join result log shows public/listed address and port
3. host machine join shows one-shot localhost rewrite
4. remote machine join shows the same public/listed address, with `selfJoinRedirect=false`

For `VpnOverlay`, the next rerun should prove:

1. dedicated startup log shows `CustomServerHostIP=<overlay address>`
2. launch args contain `/customserverhost "<overlay address>"`
3. join result log shows the same overlay address
4. remote overlay client reaches the server with `selfJoinRedirect=false`
