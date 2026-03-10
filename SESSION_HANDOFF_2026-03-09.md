# Session Handoff 2026-03-09

## Stable baseline
- `GameType=TeamDeathmatch`
- listed join stable
- mission load stable
- `start_mission` / `end_mission` stable
- SP roster -> MP-safe surrogate -> dedicated resolve stable

## New confirmed working baseline
The first end-to-end playable MP-safe control-unit path is now working.

Confirmed in runtime:
- player picks side in vanilla TDM
- server forces coop-safe class for that side/culture before vanilla spawn
- vanilla TDM spawn menu opens
- client-side class picker is filtered down to our coop-safe control unit for that faction
- previewed unit is our coop rider for that faction
- player presses spawn and receives a fully functional MP agent
- attacks, weapon switching, mounted movement, and dismount all work
- death returns player to spectator and the same filtered coop picker flow can be used again
- switching teams produces the coop rider for the new faction as well
- after switching teams, the picker is re-filtered again and keeps only the coop rider instead of restoring vanilla TDM subclasses

This is the first proven "not half-alive" path.

## Working architecture
1. SP host detects battle.
2. SP writes `battle_roster.json`.
3. Dedicated reads roster.
4. Campaign troops are normalized into coop control-troop ids.
5. Dedicated resolves a faction-appropriate `mp_coop_*` class.
6. Dedicated forces `MissionPeer.SelectedTroopIndex` to that coop class.
7. Client-side `MissionGauntletClassLoadout` is filtered to coop-safe entries only.
8. Vanilla `TeamDeathmatchSpawningBehavior` performs the actual spawn.

Current proven mapping shape:
- `SP roster` -> `mp_coop_light_cavalry_<culture>_troop`
- then vanilla MP spawn path creates the real player agent

## Important technical conclusions
- The winning direction is not manual agent spawning.
- The winning direction is not late ownership transfer.
- The winning direction is not direct campaign troop ids in MP runtime.
- The winning direction is:
  - create MP-safe coop units
  - let vanilla MP spawn pipeline create the player agent
  - keep server-side authority over which coop class is selected

## What was disproven
- Post-spawn ownership transfer.
  - Produced half-alive agents and broken combat behavior.
- Direct manual player `SpawnAgent(... MissionPeer ...)` inside vanilla TDM.
  - Produced half-alive or invalid agents, boundary issues, and duplicate/phantom behavior.
- Harmony-based dedicated override of `GetMPHeroClassForPeer(...)`.
  - Unreliable in dedicated runtime due to patch/apply failures.
- Expensive coop class costs in TDM.
  - Vanilla TDM rejected them and oscillated `SelectedTroopIndex` between forced value and `0`.

## Why the current version started working
- Coop MP-safe classes are now loaded on both client and dedicated.
- Dedicated no longer relies on Harmony for class override.
- Dedicated forces `SelectedTroopIndex` through mission logic before vanilla spawn.
- Coop control classes have zero TDM cost, so vanilla spawn no longer rejects them for insufficient gold.
- Client-side `MultiplayerClassLoadoutVM.Classes -> HeroClassGroupVM.SubClasses` is now filtered to coop-safe `mp_coop_*` units.
- The class-loadout filter is keyed by active datasource + team/culture/troop context, so it reapplies after spectator return and side switch.

## Current data set
Coop-safe mounted control units exist for:
- `sturgia`
- `battania`
- `vlandia`
- `empire`
- `aserai`
- `khuzait`

Additional infantry control unit exists for:
- `mp_coop_heavy_infantry_empire_*`

## Current user-facing behavior
- TDM troop menu still appears.
- Its list is now partially repurposed into a coop picker.
- In the current slice it can be reduced to only the coop control rider for the active faction.
- It is no longer the real source of truth for which troop the player gets.
- Server-side coop class forcing is the source of truth.
- Client-side class-loadout filtering keeps the menu aligned with the forced coop class on initial side pick and after side switch.
- TDM visuals/perk framing are still temporary and should be treated as transitional UI.

## Recommended next implementation
### Phase 1
Stabilize the current win:
- keep vanilla TDM spawn path
- keep server-authoritative coop class forcing
- avoid new manual spawn experiments

### Phase 2
Reduce UI mismatch:
- keep using the filtered TDM menu as the temporary coop picker
- rename/remove leftover TDM semantics where possible
- decide whether to stay with a heavily adapted class-loadout screen or replace it with a dedicated coop picker later

### Phase 3
Scale roster coverage:
- add more control units beyond light cavalry
- create closer 1:1 MP-safe definitions for priority SP units
- expand from "one per faction" to a curated subset of campaign troops

### Phase 4
Gameplay rules:
- validate `death -> spectator -> respawn`
- then decide final coop life-cycle rules
- only after that design final player unit selection UX

## Newly proven runtime facts
- The adapted picker can now survive:
  - first side selection
  - spawn
  - death back to spectator
  - side switch
  - second spawn with the new faction's coop rider
- Dedicated logs continue to confirm faction-correct coop spawns such as:
  - `Coop Jawwal`
  - `Coop Raider`
- The remaining UX problem is no longer "can we force the right unit?" but "how far do we want to evolve this filtered picker away from TDM visuals?"

## Files to start from next session
- `C:\dev\projects\BannerlordCoopSpectator3\PROJECT_CONTEXT.md`
- `C:\dev\projects\BannerlordCoopSpectator3\HUMAN_NOTES_MULTIPLAYER_PROGRESS.md`
- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Campaign\BattleDetector.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Module\CoopSpectator\ModuleData\coopspectator_mpcharacters.xml`
- `C:\dev\projects\BannerlordCoopSpectator3\Module\CoopSpectator\ModuleData\coopspectator_mpclassdivisions.xml`

## Guardrails for next session
- Do not go back to manual direct player spawn inside vanilla TDM.
- Do not invest further in late ownership transfer.
- Do not treat vanilla TDM menu choice as authoritative gameplay state.
- Preserve the current proven path until a replacement UI is ready.
- Do not regress the client-side class-loadout filter that now reapplies after side switch.
