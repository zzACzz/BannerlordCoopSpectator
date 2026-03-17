## Goal
Start a new focused workstream: remove or prevent the vanilla TDM `Team Selection` overlay from appearing in coop missions, without breaking client `join`.

## Current state
Core coop flow is already ours and works:
- own side selection
- own troop selection
- own spawn
- own respawn/reset
- own entry status/menu
- own client hotkeys
- own client text HUD/menu

Stable facts:
- client `join` is currently stable
- own spawn/respawn flow works
- TDM overlay is no longer the source of truth
- but vanilla `Team Selection` / TDM overlay still appears visually and interferes with validation

## What is already done
### Strong milestones completed
- Stage 5: completed as a strong milestone
- Stage 6: strong milestone, enough to move on from backend/core work

### Working own control surface
SP console commands:
- `coop.select_side attacker|defender`
- `coop.select_side_index 1|2`
- `coop.side_options`
- `coop.select_troop <troopId|entryId>`
- `coop.troop_options`
- `coop.troop_options_side attacker|defender`
- `coop.select_troop_index <n>`
- `coop.select_troop_index_side attacker|defender <n>`
- `coop.spawn_now`
- `coop.force_respawnable`
- `coop.entry_status`
- `coop.entry_menu`

Client hotkeys:
- `Ctrl+1/2` -> side
- `Ctrl+Q/E` -> troop
- `Ctrl+R` -> spawn
- `Ctrl+T` -> respawn reset
- `Ctrl+M` -> toggle text menu

### Important runtime pieces
- `CoopBattleSelectionIntentState`
- `CoopBattleSelectionRequestState`
- `CoopBattleSpawnIntentState`
- `CoopBattleSpawnRequestState`
- `CoopBattleSpawnRuntimeState`
- `CoopBattlePeerLifecycleRuntimeState`
- `CoopBattleEntryStatusBridgeFile`

## Key files
- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Commands\CoopBattleSelectionConsoleCommands.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\CoopBattleEntryStatusBridgeFile.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\CoopBattlePeerLifecycleRuntimeState.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Patches\MissionStateOpenNewPatches.cs`

## What was tried and should NOT be repeated blindly
These approaches repeatedly caused breakage or dead ends:

### 1. Aggressive behavior removal from wrapped TDM stack
Tried removing:
- `MissionGauntletTeamSelection`
- `MissionGauntletClassLoadout`
- `MultiplayerTeamSelectComponent`

Result:
- often broke client `join`
- caused null lifecycle in `MissionGauntletClassLoadout`
- left vanilla UI in partially initialized state

### 2. Layer detaching / TopScreen remove hacks
Tried:
- reflection-based `RemoveLayer(...)`
- forced suspend/deactivate/finalize on gauntlet layers

Result:
- unstable
- also caused `join` crashes or brittle UI lifecycle problems

### 3. Overlay click adaptation
Tried:
- mirroring legacy overlay side/troop into our bridge
- syncing authoritative status back into bridge
- cooling down bridge auto team change after overlay click
- preferring `RequestedSide` over `AssignedSide` in pre-spawn lifecycle

Result:
- vanilla `Changed team to ...` clearly fires
- but TDM overlay still runs its own state machine
- own bridge and vanilla overlay keep competing
- not reliable enough

Conclusion:
- do not spend many more iterations adapting overlay clicks
- focus on removing/preventing the overlay itself as presentation

## Important observations from logs
- Vanilla overlay side clicks do reach the client runtime:
  - `Changed team to: ...`
- Dedicated sometimes sees resulting team-side sync and assigns side
- But overlay still remains visible and authoritative visual state keeps drifting
- This is a structural conflict between vanilla TDM UI state machine and coop state

## Current recommended direction
New focused task:
1. Investigate exactly what creates/shows the TDM `Team Selection` overlay in current coop runtime
2. Find a safe point to:
   - prevent its creation, or
   - suppress its visual appearance only
3. Keep `join` stable
4. Do not touch own spawn/selection core unless required

## Practical objective for next chat
The next chat should focus on:
- discovering the exact creator/path of the `Team Selection` overlay
- preventing or hiding it safely
- not on adapting its clicks
- not on reworking backend spawn/selection

## Suggested prompt for the next chat
Use this as the opening prompt:

```text
We are working in C:\dev\projects\BannerlordCoopSpectator3.

Context:
Core coop flow is already custom and working:
- own side selection
- own troop selection
- own spawn
- own respawn/reset
- own entry status/menu
- own client hotkeys
- own text HUD/menu

Stable facts:
- client join is currently stable
- own spawn/respawn flow works
- TDM Team Selection overlay still appears visually and interferes with validation

Important constraints:
- do NOT repeat aggressive behavior removal blindly
- do NOT remove MultiplayerTeamSelectComponent again without a very specific reason
- do NOT do risky reflection-based layer removal that destabilizes join
- focus on discovering what actually creates/shows the overlay and how to prevent or hide it safely

Goal:
Find a safe way to remove or prevent the appearance of the vanilla TDM Team Selection overlay in coop missions, while keeping join stable.

Key files:
- C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs
- C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\CoopBattleEntryStatusBridgeFile.cs
- C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\CoopBattlePeerLifecycleRuntimeState.cs
- C:\dev\projects\BannerlordCoopSpectator3\Patches\MissionStateOpenNewPatches.cs

Current recommendation:
Treat the TDM overlay as a presentation problem now, not a control problem. Do not spend more time adapting its clicks.
```
