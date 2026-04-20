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

## Addendum 2026-04-20

Fresh VPN/Radmin logs changed the blocker picture:

- dedicated startup is correct and log-backed:
  - host launcher emitted `/customserverhost "26.70.145.140"`
  - dedicated parsed `StartupInfo.CustomServerHostIP="26.70.145.140"`
- native browser/join payload is still wrong for `VpnOverlay`:
  - selected custom-game entry used `99.235.249.111:7210`
  - `JoinCustomGameResultMessage` still carried `GameServerProperties.Address=99.235.249.111`
  - host self-join succeeded only because our one-shot localhost rewrite converted that WAN address to `127.0.0.1`

That means the old inference was too optimistic:

- native `RegisterGame(..., overriddenIP)` receives the overlay host override
- but the actual listed/custom-game manager path used by this player-hosted dedicated flow still publishes the observed WAN address back to clients

So the real divergence is now log-backed:

- native dedicated bootstrap honors `/customserverhost`
- native remote custom-game browser/join path for this flow does not return that overlay host to the client payload

## Updated blocker list

### Public official

- no fresh public-listed rerun yet on the new diagnostics cycle
- still need proof whether the public/listed path is healthy or diverges in a different place than `VpnOverlay`

### VPN / overlay

- exact blocker is now confirmed:
  - remote peers do not receive the host overlay address from native server-list or join-result payloads
  - they receive the observed WAN address instead
- therefore remote VPN join cannot be made reliable only by passing `/customserverhost`

### Self-join

- still healthy:
  - one-shot host localhost rewrite remains the correct and proven path

### Remote join

- new required workaround contract:
  - remote peer must arm a one-shot overlay redirect before `JoinCustomGameResultMessage` is consumed
  - the final `GameServerProperties.Address` must be rewritten from native WAN -> configured overlay host only for the matched join target

## Current branch workaround contract

This branch now uses the smallest local workaround instead of broad patching:

1. `LobbyClient.RequestJoinCustomGame(...)` logs the exact selected browser entry and arms a one-shot VPN redirect only for that target.
2. `LobbyClient.OnJoinCustomGameResultMessage(...)` still prioritizes host self-join localhost rewrite.
3. If the join is not host self-join, and the selected target matches the armed VPN redirect, the patch rewrites `GameServerProperties.Address` from native WAN to the locally configured overlay host before native handoff continues.

The local overlay host source is still the existing `DedicatedServerLaunchSettings` abstraction:

- `HostingMode=VpnOverlay`
- `AdvertisedHostAddress=<host overlay IP/DNS>`

Those settings are now persisted client-side even without starting dedicated, so a remote tester can set them once in the existing dedicated settings UI and then join through the normal custom-game list.

## Next rerun checklist

For the remote VPN peer:

1. open the existing dedicated settings UI once
2. set `Hosting Mode = VPN/Overlay`
3. set `Advertised Host Address = 26.70.145.140`
4. close the panel
5. join the listed custom game normally

Authoritative lines expected on the next run:

- `DedicatedHelper [settings] persisted preferred launch settings... hostingMode=VpnOverlay advertisedHostAddress=26.70.145.140`
- `LobbyRequestJoinDiagnosticsPatch: selected custom game join target... address=99.235.249.111 ... vpnRedirectArmed=True advertisedHostAddress=26.70.145.140`
- `LobbyJoinResultSelfJoinArmPatch: native join result handled... address=26.70.145.140 ... vpnRedirectApplied=True`
- `LocalJoinAddressPatch: final StartMultiplayerOnClient address. originalAddress=26.70.145.140 finalAddress=26.70.145.140 ... selfJoinRedirect=False`

## Addendum 2026-04-20B

Fresh VPN rerun where the host did not self-join and only the remote client attempted to connect:

- host-side bootstrap is still correct and unchanged:
  - launcher persisted `hostingMode=VpnOverlay advertisedHostAddress=26.70.145.140`
  - launcher started `DedicatedCustomServer.Starter.exe` with `/customserverhost "26.70.145.140"`
  - dedicated parsed `StartupInfo.CustomServerHostIP="26.70.145.140"`
- remote client loaded the new connectivity patches, but did not enter the VPN redirect branch:
  - `LobbyRequestJoinDiagnosticsPatch` fired
  - selected custom-game entry was still `address=99.235.249.111 port=7210`
  - decisive line: `vpnRedirectArmed=False advertisedHostAddress=(default)`
  - `LobbyJoinResultSelfJoinArmPatch` therefore kept `vpnRedirectApplied=False`
  - `LocalJoinAddressPatch` handed native `finalAddress=99.235.249.111`

So this rerun does not prove a new code regression in the VPN redirect implementation.
It proves that the remote tester did not have the local overlay override configured on that machine.

Observed native sequence on the remote machine:

- native join still reached `Join game successful`
- about 20 seconds later the client returned to lobby and the dedicated logged `RemoveNetworkPeer`

That second-stage disconnect is real, but it should not be debugged as the primary VPN blocker yet.
This rerun never exercised the intended overlay branch because the remote machine stayed on the native WAN address.

Updated interpretation:

- bootstrap contract for `VpnOverlay` remains healthy
- native listed join payload still returns WAN for this player-hosted dedicated flow
- local workaround remains valid, but only when the remote machine has:
  - `HostingMode=VpnOverlay`
  - `AdvertisedHostAddress=<host overlay IP>`

Therefore the next authoritative VPN rerun must first prove:

1. remote client logs `vpnRedirectArmed=True`
2. remote client logs `vpnRedirectApplied=True`
3. remote client logs `finalAddress=26.70.145.140`

Only after that is it worth opening the later `RemoveNetworkPeer` path as a separate blocker.

## Addendum 2026-04-20C

Fresh rerun where the remote tester explicitly tried the campaign-side VPN setup first still did not enter the overlay branch.

What the logs proved:

- host side stayed correct:
  - launcher persisted `hostingMode=VpnOverlay advertisedHostAddress=26.70.145.140`
  - dedicated started with `/customserverhost "26.70.145.140"`
  - dedicated parsed `StartupInfo.CustomServerHostIP="26.70.145.140"`
- remote client still joined with native WAN:
  - `LobbyRequestJoinDiagnosticsPatch ... vpnRedirectArmed=False advertisedHostAddress=(default)`
  - `LobbyJoinResultSelfJoinArmPatch ... vpnRedirectApplied=False`
  - `LocalJoinAddressPatch ... finalAddress=99.235.249.111`

The missing proof was earlier than multiplayer join:

- the remote campaign log only persisted `hostingMode=PublicListed advertisedHostAddress=(default)`
- there was no authoritative `VpnOverlay` persist line at all

So the blocker for this rerun was no longer "join patch ignored persisted VPN settings".
It was "the remote campaign-side settings session never produced a persisted `VpnOverlay` snapshot".

Branch change after this finding:

- the campaign settings VM now logs:
  - initialization snapshot
  - explicit hosting-mode switches
  - final close snapshot
- the map view now persists one final normalized settings snapshot on `OnFinalize`, even if the player closes the view/game without starting dedicated
- `DedicatedHelperLauncher.GetCurrentLaunchSettings()` now logs when it falls back to defaults because no persisted settings were found

That means the next rerun should be judged on these earlier authoritative lines first:

1. campaign/client:
   - `CoopDedicatedServerSettingsVM: switched hosting mode to VpnOverlay.`
   - `CoopDedicatedServerSettingsVM: persisted close snapshot. ... hostingMode=VpnOverlay advertisedHostAddress=26.70.145.140`
2. multiplayer/client:
   - either `DedicatedHelper [settings] loaded persisted launch settings. hostingMode=VpnOverlay advertisedHostAddress=26.70.145.140`
   - or, if missing, `DedicatedHelper [settings] no persisted launch settings found. Falling back to defaults.`
3. join path:
   - `vpnRedirectArmed=True`
   - `vpnRedirectApplied=True`
   - `finalAddress=26.70.145.140`

## Addendum 2026-04-20D

Fresh rerun plus the new settings diagnostics exposed the exact local bug in the client-side VPN workaround.

Observed facts:

- remote campaign run now clearly persisted the correct overlay settings:
  - `CoopDedicatedServerSettingsVM: switched hosting mode to VpnOverlay.`
  - `DedicatedHelper [settings] persisted preferred launch settings... hostingMode=VpnOverlay advertisedHostAddress=26.70.145.140`
  - `CoopDedicatedServerSettingsVM: persisted close snapshot... hostingMode=VpnOverlay advertisedHostAddress=26.70.145.140`
- host and dedicated bootstrap remained correct:
  - `/customserverhost "26.70.145.140"`
  - `StartupInfo.CustomServerHostIP="26.70.145.140"`
- but the remote multiplayer run still logged:
  - `vpnRedirectArmed=False advertisedHostAddress=(default)`
  - `vpnRedirectApplied=False`
  - `finalAddress=99.235.249.111`

The exact code divergence was local to `DedicatedHelperLauncher.GetCurrentLaunchSettings()`:

- it called `TryValidateAndNormalize(_currentLaunchSettings, ...)` even when `_currentLaunchSettings == null`
- that method returns a valid normalized default (`PublicListed`) for `null`
- so the multiplayer client returned defaults immediately
- and never reached the persisted-settings read path at all

That means the previous workaround design is still viable.
The bug was in our implementation of the local settings source, not in the native join-result rewrite concept itself.

Branch fix:

- `GetCurrentLaunchSettings()` now reads persisted settings before accepting defaults when `_currentLaunchSettings` is null
- default settings are only materialized after the persisted read path has been attempted

The next authoritative VPN rerun should therefore show:

1. client multiplayer:
   - `DedicatedHelper [settings] loaded persisted launch settings. hostingMode=VpnOverlay advertisedHostAddress=26.70.145.140`
2. client join request:
   - `vpnRedirectArmed=True advertisedHostAddress=26.70.145.140`
3. join-result handoff:
   - `vpnRedirectApplied=True`
   - `finalAddress=26.70.145.140`

## Addendum 2026-04-20E

Fresh reruns after the `GetCurrentLaunchSettings()` fix confirm that the `VpnOverlay` workaround path is now alive end-to-end.

Authoritative client-side proof:

- remote client loaded the persisted overlay settings:
  - `DedicatedHelper [settings] loaded persisted launch settings. hostingMode=VpnOverlay advertisedHostAddress=26.70.145.140`
- join request armed the VPN redirect:
  - `LobbyRequestJoinDiagnosticsPatch ... vpnRedirectArmed=True advertisedHostAddress=26.70.145.140`
- join-result rewrite applied the overlay host:
  - `LobbyJoinResultSelfJoinArmPatch ... success=True ... address=26.70.145.140 ... armedSelfJoin=False vpnRedirectApplied=True vpnRedirectAddress=26.70.145.140`
- final client connect target stayed on the overlay address:
  - `LocalJoinAddressPatch ... originalAddress=26.70.145.140 finalAddress=26.70.145.140 ... selfJoinRedirect=False`
- native client join completed:
  - `Join game successful`

Authoritative dedicated-side proof:

- dedicated still launched with the expected overlay bootstrap:
  - `StartupInfo ... CustomServerHostIP="26.70.145.140"`
- payload delivery reached the remote peer:
  - `CoopMissionNetworkBridge: queued payload transmission. Peer=XCTwnik ...`
  - `CoopMissionNetworkBridge: completed payload transmission. Peer=XCTwnik ...`
- battle start readiness became authoritative:
  - `CoopMissionSpawnLogic: battle start readiness audit ... CanStartBattle=True ...`

Authoritative mission/runtime proof on the remote client:

- remote peer received and assembled both payload kinds:
  - `received first payload chunk. Kind=BattleSnapshot ...`
  - `received first payload chunk. Kind=EntryStatusSnapshot ...`
  - `assembled client payload. Kind=EntryStatusSnapshot ...`
  - `applied client payload. Kind=EntryStatusSnapshot AssignedSide=Attacker ... SelectableEntryIds=... CanRespawn=True`

Observed gameplay result from the paired reruns:

- remote client joined the custom-game lobby through VPN
- remote client loaded the mission and possessed a unit
- host self-join still used the separate localhost rewrite path
- spawn-before-materialization stayed correctly blocked
- battle end and return-to-lobby / next-mission flow also completed in later reruns

Interpretation:

- the previously unresolved VPN blocker was real, but it was local to our persisted-settings source
- the current workaround design remains valid:
  - keep the native custom-game session contract
  - rewrite only the final join address from WAN to overlay host on remote peers
  - keep host self-join on its own localhost-only path

Current status after these reruns:

- `VpnOverlay` connection path: log-backed working
- host self-join path: still working
- spawn/materialization gate: working on the tested reruns
- battle end / lobby return / next mission: working on the tested reruns
- `PublicListed` player-hosted connectivity: still a separate open contract and not proven green by these VPN runs

One first-run anomaly was observed by the tester:

- host-side team-selection briefly showed unavailable/vanished when the remote peer selected first

That anomaly did not reproduce on the immediately following rerun where two battles completed successfully with different pick order. There is currently no log-backed evidence that it is caused by IP identity collision, and no repeatable blocker has been isolated from it yet.
