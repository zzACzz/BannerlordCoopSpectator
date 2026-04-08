# TDM-Style Team And Unit Selection Handoff

Date: 2026-04-08
Project: `BannerlordCoopSpectator3`
Focus: replace the current coop selection overlay with familiar `TDM`-style side selection and unit selection shells, while keeping the coop-authoritative runtime and avoiding a rollback to native `TDM` mechanics

## Executive Summary

State at this handoff:

- exact campaign battle runtime is stable enough to shift focus away from scene/bootstrap/firefighting and onto UI migration;
- commander death no longer crashes dedicated, battle continues, and battle results still write back to campaign;
- hero combat XP writeback is validated enough for current progress tracking;
- large-battle reinforcement spawn identity is materially better and surrogate models are no longer the main blocker;
- the current visible blocker for entry UX is now the coop selection interface itself.

Current practical status:

- the current custom overlay works functionally, but looks unlike native multiplayer and is not acceptable as the long-term player-facing UI;
- the user wants the familiar `TDM` side-selection and unit/class-selection shell;
- the current custom unit list is stale after deaths because it is built from authority-allowed ids, not from a live alive-only roster;
- the next task should focus on reusing or copying the native `TDM` UI shell only, while keeping our own coop wiring and not restoring the disabled vanilla `TDM` entry mechanics.

## Validated Current State

These areas are stable enough and are not the current frontier:

- exact campaign scene transfer works;
- dedicated exact-scene startup works;
- client loads the real campaign battlefield;
- large-battle bootstrap and reinforcement waves work well enough to move on;
- battle completion and campaign return path work;
- commander death no longer crashes dedicated;
- battle still finishes and writes `battle_result` after commander death;
- hero combat XP writeback is validated enough by logs and user-visible progression;
- friendly formation markers / overhead type icons are present again in battle-map runtime.

## Parked But Not Current Blockers

These are not the next task unless fresh logs make them urgent:

- exact commander perk parity is still incomplete;
- bonus ammo perks such as extra arrows / javelins are not yet native-exact;
- some reinforcement display names may still drift even when the model/body is already correct;
- deeper reward / modifier parity can be revisited later.

## Do Not Reopen Without New Log-Backed Reason

- exact scene transfer;
- battle completion after last enemy dies;
- commander-death crash path;
- hero combat XP writeback for direct combat hits;
- broad reinforcement surrogate-model debugging as the primary issue;
- overhead formation marker restoration.

## Current User-Facing UI Problem

The current custom selection overlay is functionally usable, but it has two major issues:

1. it does not look like the familiar native `TDM` interface players expect;
2. after death, its side/unit list is stale and keeps showing all side entries instead of only currently alive selectable units.

The user specifically wants:

- the familiar native `TDM` side-selection shell;
- the familiar native `TDM` unit/class-selection shell;
- these shells shown in a separate proper menu flow, not as the current crude custom panel;
- after death, returning to side selection with a list built from units that are still alive.

## What The Current Code Actually Does

### Current custom overlay path

The current custom mission UI lives in:

- `UI/CoopMissionSelectionView.cs`

This file owns:

- the custom `MissionView`;
- the custom gauntlet layer / movie loading;
- the custom `CoopSelectionVM`;
- the side list and unit list shown to the player.

Important current behavior:

- `RefreshFromRuntime(...)` reads `CoopBattleEntryStatusBridgeFile.ReadStatus()` and `BattleSnapshotRuntimeState.GetState()`;
- `Sides = BuildSideItems(...)`;
- `Units = BuildUnitItems(...)`;
- `BuildUnitItems(...)` iterates `ResolveAllowedSelectionIds(status, effectiveSide)`.

### Why the current list is stale

The stale-list bug is structural, not cosmetic.

`ResolveAllowedSelectionIds(...)` currently resolves from:

- `AllowedEntryIds`
- `AttackerAllowedEntryIds`
- `DefenderAllowedEntryIds`

in:

- `Infrastructure/CoopBattleEntryStatusBridgeFile.cs`

Those status fields are written on the server from:

- `Mission/CoopMissionBehaviors.cs`
- `TryWriteEntryStatusSnapshot(...)`
- `CoopBattleAuthorityState.GetAllowedEntryIds(...)`

Meaning:

- the current UI is driven by the authority-permitted selection universe;
- it is not driven by a live alive-only roster;
- therefore dead entries can remain visible even though they are no longer valid player-facing choices.

This is the key root cause for the new task.

## Native TDM Shell Findings

### Native team selection shell

Decompile shows:

- `.codex_tmp/decompiled_multiplayer_gauntlet/TaleWorlds.MountAndBlade.Multiplayer.GauntletUI.Mission/MissionGauntletTeamSelection.cs`

Important findings:

- it is a `MissionView`;
- it opens `GauntletLayer("MultiplayerTeamSelection", ...)`;
- it loads movie `MultiplayerTeamSelection`;
- it depends on native multiplayer behaviors such as:
  - `MissionNetworkComponent`
  - `MultiplayerTeamSelectComponent`
  - `MissionLobbyComponent`
  - `MissionGauntletMultiplayerScoreboard`
- it uses proper mission-dialog lifecycle:
  - `MissionScreen.SetDisplayDialog(...)`
  - camera lock
  - `InputRestrictions`

### Native class loadout shell

Decompile shows:

- `.codex_tmp/decompiled_multiplayer_gauntlet/TaleWorlds.MountAndBlade.Multiplayer.GauntletUI.Mission/MissionGauntletClassLoadout.cs`

Important findings:

- it is also a `MissionView`;
- it opens `GauntletLayer("MultiplayerClassLoadout", ...)`;
- it loads movie `MultiplayerClassLoadout`;
- it is tightly coupled to native multiplayer systems:
  - `MissionLobbyEquipmentNetworkComponent`
  - `MissionMultiplayerGameModeBaseClient`
  - `MultiplayerTeamSelectComponent`
  - `MissionRepresentativeBase`
  - `MissionNetworkComponent`

### Native mission view stack

Decompile shows native `MultiplayerBattle` and `TeamDeathmatch` register these entry UI paths through:

- `.codex_tmp/decompiled_multiplayer_view/TaleWorlds.MountAndBlade.Multiplayer.View.MissionViews/MultiplayerMissionViews.cs`

Important implication:

- the familiar `TDM` UX is not just XML;
- it is a shell layered over native multiplayer team/class-selection mechanics;
- blindly re-enabling those behaviors would risk reopening the exact problems we intentionally bypassed.

## Existing Repo Guardrails That Must Stay Intentional

These are not accidental and should not be reverted casually:

- `Patches/VanillaEntryUiSuppressionPatch.cs`
- `Patches/MissionStateOpenNewPatches.cs`

Current behavior:

- vanilla `MissionGauntletTeamSelection` is suppressed;
- vanilla `MissionGauntletClassLoadout` is suppressed;
- wrapped `MultiplayerBattle` client stack removes native entry gauntlet behaviors;
- our custom coop selection view is appended instead.

Conclusion:

- do not "fix" this task by simply re-enabling native `MissionGauntletTeamSelection` and `MissionGauntletClassLoadout`;
- those classes are tied to native `TDM`/lobby/class-loadout mechanics that we intentionally decoupled from coop.

## Native Assets Worth Reusing Or Copying

These are the exact familiar shell assets the user wants to resemble:

- `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\Multiplayer\GUI\Prefabs\TeamSelection\MultiplayerTeamSelection.xml`
- `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\Multiplayer\GUI\Prefabs\TeamSelection\MultiplayerTeamSelectionItem.xml`
- `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\Multiplayer\GUI\Prefabs\ClassLoadout\MultiplayerClassLoadout.xml`
- `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\Multiplayer\GUI\Prefabs\ClassLoadout\MultiplayerClassLoadoutClassGroup.xml`
- `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\Multiplayer\GUI\Prefabs\ClassLoadout\MultiplayerClassLoadoutItemTab.xml`
- `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\Multiplayer\GUI\Prefabs\ClassLoadout\MultiplayerClassLoadoutPerkPopup.xml`
- `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\Multiplayer\GUI\Prefabs\ClassLoadout\MultiplayerClassLoadoutUsageItemTab.xml`

These are the right visual references.
They are not proof that the native view-model and network contract should be restored unchanged.

## Recommended Direction

Preferred direction for the next task:

- keep our own coop mission behaviors and bridge-driven authority flow;
- keep vanilla suppression in place unless there is a new log-backed reason to change it;
- reuse or copy the native `TDM` shell visuals and dialog behavior;
- wire those shells to coop-owned selection/spawn functionality;
- replace the stale allowed-id unit source with an authoritative alive-only selection source.

In practice, that likely means:

- either copy `MultiplayerTeamSelection.xml` and `MultiplayerClassLoadout.xml` into coop-owned prefab movies;
- or build near-`1:1` copies with our own movie names and view-models;
- keep our own `MissionView` owner classes;
- keep our own callbacks for side selection, unit selection, spawn, reset, and battle start;
- do not bind the UI back to native `MissionLobbyEquipmentNetworkComponent` / class-loadout toggle logic.

## Data Contract Requirement For Alive-Only Lists

The next task should not assume that `AllowedEntryIds` is enough.

The side/unit menu after death must be based on:

- only the side the player is allowed to choose;
- only entries that are still alive / currently selectable;
- authoritative server-side truth, not client guesswork.

That probably means one of these:

1. extend the existing status/selection bridge with alive-only selectable entry ids per side;
2. derive alive-only lists from authoritative runtime state already mirrored to the client;
3. or combine authority-allowed ids with a live battle-state filter, but only if that filter is actually authoritative and not stale.

Do not solve this with a client-only heuristic that merely hides some rows visually.

## Recommended Implementation Shape

Reasonable target architecture:

- a coop-owned team-selection `MissionView` that mimics native `MissionGauntletTeamSelection` shell behavior;
- a coop-owned unit-selection `MissionView` or modal sub-shell that mimics native `MissionGauntletClassLoadout`;
- coop-owned VMs and button callbacks;
- explicit open/close lifecycle using `MissionScreen.SetDisplayDialog(...)`, layer focus, and input restrictions;
- automatic reopen to side selection after commander death or loss of controlled agent;
- unit lists that only show alive selectable entries.

## Acceptance Criteria For The Next Task

- the player sees familiar `TDM`-style side selection instead of the current crude custom panel;
- the player sees familiar `TDM`-style unit/class selection shell for coop troop entry;
- no vanilla `TDM` team/class mechanics are reintroduced just to make the UI appear;
- after death, the player returns to side selection;
- the available units shown after death are alive-only and update correctly;
- coop spawn / reset / battle start still use coop-owned authority flow and bridge files;
- no regression in battle startup, respawn flow, or commander-death stability.

## Recommended Reading In A New Window

Read these first:

- `docs/README.md`
- `PROJECT_CONTEXT.md`
- `docs/COOP_SELECTION_UI_TECHNICAL_MAP_2026-03-27.md`
- this file
- `BUILD_RUNBOOK.md`

Useful code files:

- `UI/CoopMissionSelectionView.cs`
- `Infrastructure/CoopBattleEntryStatusBridgeFile.cs`
- `Infrastructure/BattleSnapshotRuntimeState.cs`
- `Mission/CoopMissionBehaviors.cs`
- `Patches/VanillaEntryUiSuppressionPatch.cs`
- `Patches/MissionStateOpenNewPatches.cs`

Useful native / decompile references:

- `.codex_tmp/decompiled_multiplayer_gauntlet/TaleWorlds.MountAndBlade.Multiplayer.GauntletUI.Mission/MissionGauntletTeamSelection.cs`
- `.codex_tmp/decompiled_multiplayer_gauntlet/TaleWorlds.MountAndBlade.Multiplayer.GauntletUI.Mission/MissionGauntletClassLoadout.cs`
- `.codex_tmp/decompiled_multiplayer_view/TaleWorlds.MountAndBlade.Multiplayer.View.MissionViews/MultiplayerMissionViews.cs`
- `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\Multiplayer\GUI\Prefabs\TeamSelection\MultiplayerTeamSelection.xml`
- `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\Multiplayer\GUI\Prefabs\ClassLoadout\MultiplayerClassLoadout.xml`

## Final Practical Conclusion

The next task is no longer "make selection work somehow".

The real job now is:

- keep the coop-authoritative selection/spawn system;
- replace the current crude overlay with native-looking `TDM` shells;
- and fix the data contract so that post-death selection is based on alive units, not on a stale authority-allowed universe.

That is a focused UI/runtime-contract task, not a return to generic `TDM` spawn mechanics.
