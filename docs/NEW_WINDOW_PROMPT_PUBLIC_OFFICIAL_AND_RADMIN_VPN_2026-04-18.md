# New Window Prompt: Public Official Connection And Radmin VPN

Continue work in `C:\dev\projects\BannerlordCoopSpectator3`.

First read:

- `C:\dev\projects\BannerlordCoopSpectator3\docs\README.md`
- `C:\dev\projects\BannerlordCoopSpectator3\PROJECT_CONTEXT.md`
- `C:\dev\projects\BannerlordCoopSpectator3\docs\WORKING_SPAWN_BASELINE_AND_DIFF_PLAN_2026-04-13.md`
- `C:\dev\projects\BannerlordCoopSpectator3\docs\COOP_BATTLE_STABILIZATION_AND_CONNECTIVITY_HANDOFF_2026-04-18.md`

## Current state you must treat as baseline

Battle/runtime work is stable enough to move on.

Do not casually reopen:

- spawn/materialization
- exact crafted-item transfer
- perk/stat low-level bridges
- battle completion / writeback

This new thread is only about connection bootstrap and join flow.

## Goal

Make both of these work reliably:

1. `Public` official connection
2. `Radmin VPN` / overlay connection

This includes:

- starting the dedicated in the correct mode
- getting the correct advertised/join address
- making host self-join work without breaking remote joins
- making remote joins work in both official-public and VPN-overlay modes

## Critical rule

Do this the same way the successful battle branch was solved:

- low-level first
- `ilspycmd` first
- diagnostics first
- then narrow fixes

Do not start by adding more high-level patches or guessing from symptoms.

## Required code focus

Read these files first:

- `C:\dev\projects\BannerlordCoopSpectator3\Commands\CoopConsoleCommands.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\DedicatedHelper\DedicatedServerLaunchSettings.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\DedicatedHelper\DedicatedServerHostingMode.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\DedicatedHelper\DedicatedHelperLauncher.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\HostSelfJoinRedirectState.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Patches\LobbyCustomGameLocalJoinPatch.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Patches\LobbyJoinResultSelfJoinArmPatch.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Patches\LocalJoinAddressPatch.cs`

## What you must decompile with ilspycmd

Do not stop at our code. Decompile the native path that actually owns join/bootstrap behavior.

At minimum inspect:

### Client/lobby join path

- `TaleWorlds.MountAndBlade.Lobby.LobbyGameStateCustomGameClient.StartMultiplayer(...)`
- `TaleWorlds.MountAndBlade.Diamond.LobbyClient.OnJoinCustomGameResultMessage(...)`
- `TaleWorlds.MountAndBlade.GameNetwork.StartMultiplayerOnClient(...)`

### Server/bootstrap path

- official dedicated/custom-server bootstrap flow used by `DedicatedCustomServer.Starter.exe`
- native handling of:
  - listed/public custom servers
  - host/address/port/session payloads
  - custom server host override
  - self-join vs remote join

### Data flow questions you must answer from decompile

1. Where does the official public-listed join path get the final address and port?
2. Where does the host self-join path diverge from a remote client join?
3. When is localhost rewrite valid, and when does it break remote players?
4. How is the advertised host used in VPN/overlay mode?
5. Which exact native payload carries:
   - server name
   - address
   - port
   - session key
   - peer index

## Known project-side clues

The current connectivity layer already has distinct concepts:

- `DedicatedServerHostingMode.PublicListed`
- `DedicatedServerHostingMode.VpnOverlay`
- `AdvertisedHostAddress`
- one-shot host self-join redirect
- `/customserverhost`
- modded official dedicated flow

That means the next task is not to invent a new abstraction. It is to verify where the current layer diverges from the native contract.

## Required working method

1. Before changing logic, write down the current native connection contract.
2. Add diagnostics at authoritative points only.
3. Validate `Public` and `VPN` as two separate paths.
4. Fix only one narrow layer at a time.
5. Keep battle/runtime untouched unless logs prove the bug crosses that boundary.

## Diagnostics you should add first

Add precise logs around:

- dedicated launch argument construction
- hosting mode resolution
- advertised host resolution
- self-join redirect arming
- self-join redirect consumption
- native join result payload handling
- final address actually handed to `StartMultiplayerOnClient(...)`

Do not add broad spam. Add one authoritative line per step.

## Success criteria

### Public official connection

- host starts dedicated from campaign in official/listed mode
- server appears and remains joinable
- host can join its own dedicated locally without breaking remote address behavior
- another player can join from a different machine/network through the intended public path

### Radmin VPN / overlay connection

- host starts dedicated in `VpnOverlay` mode
- advertised host is the correct Radmin/VPN address
- remote player on the same overlay can join
- no accidental localhost rewrite for non-host peers
- no fallback path that works only for the host machine

## Deliverables expected from the new thread

1. A short connectivity contract document:
   - what native does now
   - where our code diverges
2. A narrow blocker list:
   - `Public`
   - `VPN`
   - self-join
   - remote join
3. Then only the smallest code changes needed to close those blockers

## Important warning

Earlier regression time was wasted because parallel connectivity changes existed outside the active debugging context and were misattributed to spawn/perk work.

Do not repeat that.

This thread should stay strictly inside:

- connection bootstrap
- join path
- official public path
- Radmin VPN path

and must not casually reopen battle runtime.
