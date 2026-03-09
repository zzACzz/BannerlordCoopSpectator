# BannerlordCoopSpectator — Project Context (share this in new chats)

## Goal
Build a **coop spectator** mod for **Mount & Blade II: Bannerlord v1.3.14 (Steam)**:
- Host plays campaign normally.
- Clients spectate host’s campaign state.
- Later: clients join battles as host’s troops.

## Environment (known working)
- **Game version**: 1.3.14
- **Game path / Modules**: `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules`
- **MSBuild**: `C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`
- **Two instances test**:
  - Host: run via Steam
  - Client: run via `Launcher.Native.exe`

## How to build (PowerShell)
```powershell
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' .\CoopSpectator.csproj /t:Restore /p:Configuration=Release
& 'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe' .\CoopSpectator.csproj /t:Build   /p:Configuration=Release
```
Output:
- `Module\CoopSpectator\bin\Win64_Shipping_Client\CoopSpectator.dll`
- `Module\CoopSpectator\bin\Win64_Shipping_Client\0Harmony.dll` (bundled Harmony)

## How to install
Copy:
- `Module\CoopSpectator`
to:
- `...\Mount & Blade II Bannerlord\Modules\CoopSpectator`

## How we update the mod in the game folder (fast loop)
Instead of copying the whole module every time, we usually copy only the changed binaries into the already-installed module:

```powershell
$dstBin = 'C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\CoopSpectator\bin\Win64_Shipping_Client'
$srcBin = 'C:\dev\projects\BannerlordCoopSpectator\Module\CoopSpectator\bin\Win64_Shipping_Client'

Copy-Item -LiteralPath (Join-Path $srcBin 'CoopSpectator.dll') -Destination (Join-Path $dstBin 'CoopSpectator.dll') -Force
Copy-Item -LiteralPath (Join-Path $srcBin 'CoopSpectator.pdb') -Destination (Join-Path $dstBin 'CoopSpectator.pdb') -Force

# Harmony is bundled via NuGet (Lib.Harmony) and outputs as 0Harmony.dll
Copy-Item -LiteralPath (Join-Path $srcBin '0Harmony.dll') -Destination (Join-Path $dstBin '0Harmony.dll') -Force
```

If Windows denies writing into `Program Files (x86)`, run the shell as Administrator or copy manually.

## What to paste into a NEW chat (token efficient)
Paste:
1) This file (`PROJECT_CONTEXT.md`)
2) Your current goal for this chat (one-sentence)
3) Only the 1–3 files you expect to change (optional, but helps)

Avoid pasting large files every time (`bannerlord_coop_plan.md`, large logs, DLL dumps) unless they’re directly relevant.

## Runtime commands (console)
- `coop.host [port]` (default 7777)
- `coop.join <ip> [port]`
- `coop.send <message...>`
- `coop.status`

## Protocol (TCP line-based)
- One message = one line (`\n`)
- Key message types:
  - `STATE:{json}` — host campaign state to clients
  - (reserved) `BATTLE_START:{json}`, `BATTLE_END:{json}`

## What is already working (DONE)
### Foundation
- SDK-style `net472` project builds and outputs to module folder.
- References game DLLs from `bin\Win64_Shipping_Client` (fallback to local copies).
- TCP server/client works; messages delivered.
- “Mod loaded” message shows in campaign.

### Spectator MVP
- Host sends `STATE:{json}` every ~2 seconds in campaign when **Role=Server**.
- Client receives/parses `STATE:{json}` when **Role=Client**, shows periodic `HOST: ...` status.

### Client restrictions (Harmony is bundled, Bannerlord.Harmony module NOT required)
- Movement blocked for client:
  - `MobileParty.SetMoveGoToPoint`
  - `MobileParty.SetMoveGoToSettlement`
  - `MobileParty.SetMoveEngageParty`
  - `MobileParty.SetMoveGoToInteractablePoint`
- Settlement menus blocked for client:
  - `GameMenuManager.SetNextMenu(string menuId)` blocks `town/castle/village/settlement/keep/tavern/market/arena` menu ids
- Note: user reports blocks work, but the “Spectator disabled…” UI message does not always show.

## Current known issues
- Client sometimes does not see the “Spectator disabled…” UI message when a menu/move is blocked.
  - Suspected cause: message timing/UI availability; may need to enqueue via dispatcher and show on next tick.

## Next tasks (TODO)
1) **Filter network spam**: stop showing `NET: STATE:{json}` in `SubModule` (only show non-STATE debug).
2) **Reliable user feedback**: make blocked-action message always appear (show on next tick via `MainThreadDispatcher.Enqueue`).
3) **Town-menu block refinement**: narrow/verify menuId patterns if false positives appear.
4) **Next milestone**: battle detection + invitation message (`BATTLE_START`) — later stage.

## Full roadmap (from `bannerlord_coop_plan.md`) broken into chat-sized tasks

### Stage 1 — Foundation / Preparation (DONE)
> Plan sections: “ЕТАП 1” (1.1–1.4)
- Chat “1.1 Environment + build pipeline” (DONE): create `net472` project, references to game DLLs, output path to module bin, MSBuild commands.
- Chat “1.2 Hello World SubModule” (DONE): load message + per-tick dispatcher.
- Chat “1.4 TCP networking prototype” (DONE): `coop.host/join/send/status`, line-based TCP.

### Stage 2 — Spectator Mode (PARTIAL DONE)
> Plan sections: “ЕТАП 2” (2.1–2.3)
- Chat “2.1 Protocol + DTO (STATE:{json})” (DONE): `HostGameState`, `HostGameStateCodec`, game `Newtonsoft.Json` reference.
- Chat “2.1 Broadcaster” (DONE): `Campaign/HostStateBroadcaster.cs` sends `STATE` every ~2s when Role=Server.
- Chat “2.2 Spectator UI” (TODO): replace spammy messages with a small overlay/HUD (ViewModel) showing host position/action; keep 2s refresh.
- Chat “2.3 Block client control” (DONE): bundled Harmony + movement/menu restrictions for Role=Client.
- Chat “2.5.4 Filter STATE spam” (DONE):
  - Change: `SubModule.cs`
  - Goal: do NOT display `NET: STATE:{json}` in UI (still allow other debug messages).
  - Done when: client only sees `HOST:` updates (from `SpectatorStateReceiver`) and no raw JSON spam.
- Chat “2.5.x Reliable block UI message” (DONE):
  - Change: `Infrastructure/GameUi.cs` (optional), `Patches/Block*Patch.cs`
  - Goal: whenever a client action is blocked, show a short message reliably.
  - Approach: do NOT call UI directly inside Harmony Prefix; instead do `MainThreadDispatcher.Enqueue(() => GameUi.ShowMessage(...))` with a cooldown.
  - Done when: client consistently sees “Spectator: … disabled” when attempting blocked actions.
- Chat “2.3/2.5 Tighten remaining blocks” (DONE):
  - Change: `Patches/BlockPartyMovementPatch.cs`, `Patches/BlockSettlementMenusPatch.cs`
  - Goal: cover any remaining `MobileParty.SetMove*` or menu transitions that still allow moving/interaction.

### Stage 3 — Battle Integration (TODO)
> Plan sections: “ЕТАП 3” (3.1–3.5)
- Chat “3.1 Battle start detection” (DONE):
  - Add `Campaign/BattleDetector.cs` to detect battle start for host (events or Harmony).
  - Send `BATTLE_START:{json}` to clients (scene/map info + side + troop list).
- Chat “3.1 Client battle notification” (DONE):
  - Client receives `BATTLE_START` and shows countdown/notification; blocks campaign input further.
- Chat “3.2 MP mission conversion (research)” (TODO, likely multiple chats):
  - Goal: find how Bannerlord creates MP missions and reproduce minimal start from code.
  - Deliverable: a PoC that can start a MP-like mission from campaign (even without full sync).
- Chat “3.3 Troop selection UI” (TODO):
  - Build a simple selection screen for client based on troop list from host.
  - Client sends `TROOP_SELECTED:{id}` back.
- Chat “3.4 Spawn system (client agents)” (TODO):
  - MissionLogic that spawns/assigns agents for network peers using selected troop.
- Chat “3.5 Return to spectator after battle” (TODO):
  - Detect mission end, send `BATTLE_END:{json}`, return clients to spectator campaign loop.

### Stage 4 — Testing & Polish (TODO)
> Plan sections: “ЕТАП 4” (4.1–4.4)
- Chat “4.1 Test checklist automation (manual script)” (TODO): document repeatable scenarios and expected outcomes.
- Chat “4.2 Networking robustness” (TODO): reconnects, timeouts, message validation, rate limiting.
- Chat “4.2 Smoothness” (TODO): client-side interpolation/buffering of host positions.
- Chat “4.2 Memory/leaks cleanup” (TODO): ensure event unsubs and thread cleanup paths are correct.
- Chat “4.3 UI/UX improvements” (TODO): lobby/HUD indicators, ping, client list, better messages.
- Chat “4.4 Docs polish” (TODO): README troubleshooting, known issues, install steps, compatibility notes.

### Stage 5 — Release & Support (TODO)
> Plan sections: “ЕТАП 5” (5.1–5.3)
- Chat “5.1 Packaging + versioning” (TODO): prepare release zip structure, changelog, version bump.
- Chat “5.1 Publish checklist” (TODO): Nexus/ModDB/GitHub release notes + screenshots.
- Chat “5.2 Feedback loop” (TODO): issue template, log collection instructions, crash reporting strategy.
- Chat “5.3 Roadmap planning” (TODO): decide v1.1/v1.2 features (more players, spectator camera, etc.).

## Important repo files
- `CoopSpectator.csproj`
- `SubModule.cs`
- `Network/NetworkManager.cs`
- `Campaign/HostStateBroadcaster.cs`
- `Campaign/SpectatorStateReceiver.cs`
- `Patches/BlockPartyMovementPatch.cs`
- `Patches/BlockSettlementMenusPatch.cs`
- `README.md`

