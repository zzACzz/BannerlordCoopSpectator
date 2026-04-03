# Exact Campaign Commander Control Handoff

Date: 2026-04-02
Project: `BannerlordCoopSpectator3`
Focus: перенесення commander/general control з campaign battle у multiplayer exact-scene runtime

## Executive Summary

Станом на цей handoff:

- exact `1:1` campaign scene transfer працює;
- дві армії на `battle_terrain_*` уже спавняться у correct campaign-like zones;
- player spawn / possession працює;
- battle start, active battle, victory, return to campaign, prisoner/casualty writeback уже працюють;
- current blocker уже не в карті, не в spawn армій і не в battle completion;
- current blocker у commander order/control handoff на client exact-scene runtime.

Поточний practical split:

- functional control:
  частково працює;
- campaign-like command UI:
  ще не перенесений, client досі живе на multiplayer order handler.

## What Is Already Working

### Exact campaign battle runtime

- Host correctly resolves `battle_terrain_*` as runtime scene.
- Dedicated starts exact campaign scene instead of surrogate `mp_battle_map_*`.
- Client visually loads the real campaign battlefield.
- Native-like army bootstrap on exact scene now works enough to seed both armies.

### Battle flow

- side / unit selection works;
- player can spawn through existing coop UI;
- armies fight;
- `G` start battle path works;
- victory / end battle / return to campaign work;
- prisoners, troop losses, and hero HP writeback work on real campaign state.

### Commander assignment baseline

Shared commander resolver is implemented:

- hero side commander = player hero;
- enemy side commander = lord if present;
- otherwise enemy commander = highest-tier troop entry.

Relevant files:

- [BattleCommanderResolver.cs](C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/BattleCommanderResolver.cs)
- [ExactCampaignArmyBootstrap.cs](C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/ExactCampaignArmyBootstrap.cs)
- [CoopMissionBehaviors.cs](C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs)
- [BattleMapSpawnHandoffPatch.cs](C:/dev/projects/BannerlordCoopSpectator3/Patches/BattleMapSpawnHandoffPatch.cs)

## What Was Done For Commander Control

### Server-side

- commander entry selection added through shared resolver;
- live commander agent assignment added;
- `Team.GeneralAgent` and `Formation.Captain` synced for exact-scene runtime;
- commander peer can be promoted from `sergeant`-like possession to `general` control;
- server already accepts multi-formation movement orders in validated small-battle runs.

Validated evidence:

- in small battle dedicated logs, server receives `SetOrderWithTwoPositions MoveToLineSegment` with `number of selected formations: 2`;
- this proves server-side multi-formation order execution already works once valid selection reaches it.

### Client-side

Implemented over multiple iterations:

- delayed promotion to general state after `BotsControlledChange`;
- local finalization after `Agent.Main` attach;
- maintenance tick to keep `Mission.PlayerTeam`, `PlayerOrderController.Owner`, `PlayerOwner` on formations, and order VM in sync;
- suppression of early crash-prone `SelectAllFormations` during spawn handshake;
- latest working-tree change:
  promotion no longer requires `MissionPeer.ControlledFormation`; it can fall back to `controlledAgent.Formation`;
- latest working-tree change:
  auto-select fallback can now call real `PlayerOrderController.SelectAllFormations(false)` if troop VM method is not enough.

## Current Observed Behavior

### Small battle

Validated behavior:

- user can partially command through mouse move orders;
- server logs show those orders are applied to `2` selected formations;
- client logs still showed only partial local ownership/finalization in prior runs;
- command menu still does not behave like campaign;
- numeric formation shortcuts and full order menu are still incomplete.

Interpretation:

- functional commander control is partially alive;
- client order UI is still stuck in a limited multiplayer-oriented state.

### Large battle

Observed before latest unvalidated patch:

- server had proper general-control state and multiple owned formations;
- client often failed to reach local `promoted/finalized/maintained` commander state;
- therefore large battle commander control looked dead from player perspective.

Latest working-tree fix for this:

- [BattleMapSpawnHandoffPatch.cs](C:/dev/projects/BannerlordCoopSpectator3/Patches/BattleMapSpawnHandoffPatch.cs#L759)
- [BattleMapSpawnHandoffPatch.cs](C:/dev/projects/BannerlordCoopSpectator3/Patches/BattleMapSpawnHandoffPatch.cs#L1087)

This fix is built and deployed, but still needs validation in a fresh run.

## Root Architectural Conclusion

The remaining problem is not:

- exact scene transfer;
- army spawn zones;
- player possession;
- battle completion;
- server-side movement order execution.

The remaining problem is:

- client-side commander order/control handoff;
- specifically the bridge from multiplayer exact-scene spawn/possession into a stable full general-control local order state;
- plus a separate UI gap: campaign-like command presentation is not yet ported.

## Important Native Findings

### OrderController

Decompile shows `OrderController` itself supports the full order set, not only movement.

Meaning:

- the server-side order core is not inherently limited to move orders.

### MissionOrderVM

Both singleplayer and multiplayer Gauntlet handlers create `MissionOrderVM`, but multiplayer constructs it with:

- `new MissionOrderVM(orderController, false, true)`

Singleplayer uses:

- `new MissionOrderVM(orderController, IsDeployment, false)`

Meaning:

- there is a real native distinction between SP and MP order UI/runtime mode;
- even when functional control works, the current client still lives on MP order UI assumptions.

### GauntletOrderUIHandler

Shortcut / toggle-order behavior depends on local input path inside `GauntletOrderUIHandler.TickInput`.

Meaning:

- if exact-scene commander bridge only patches ownership but not enough of the local handler state, the player may still get:
  - movement marker behavior,
  - but not full command menu / full shortcut behavior.

## Current Working-Tree Hypothesis

There are now two likely layers:

1. Functional layer

- make sure large battles also reach local `promoted -> finalized -> maintained`;
- make sure local selected formations become the full owned set on client.

2. UI layer

- once functional control is stable, decide whether:
  - to keep MP order UI and patch it deeper,
  - or switch exact campaign scenes to a singleplayer-like order UI path.

## Recommended Next Step

Validate the latest built patch first.

In the next clean run:

- restart both dedicated and client;
- test one large battle and one small battle;
- collect:
  - client log
  - host log
  - dedicated log

The most important client markers are:

- `BattleMapSpawnHandoffPatch: promoted local exact-scene commander to general control after BotsControlledChange`
- `BattleMapSpawnHandoffPatch: finalized local exact-scene commander order control after Agent.Main attach`
- `BattleMapSpawnHandoffPatch: maintained local exact-scene commander order control ... FormationsWithUnits=... OwnedFormationsWithUnits=... AutoSelectAllInvoked=...`

What to look for:

- in large battle:
  these lines must finally appear;
- in small battle:
  `OwnedFormationsWithUnits` should match real owned formations;
- if `AutoSelectAllInvoked=True` and full control is still missing, the next layer is almost certainly UI-mode, not ownership.

## After That

If functional control is still partial even after the latest patch:

- instrument `MissionOrderVM` / `MissionOrderTroopControllerVM` state on client exact scene;
- decide whether to pivot from patching MP order UI to using SP order UI semantics on exact campaign scenes.

If functional control becomes correct:

- next task is explicit campaign-style commander UI migration:
  formation strip, order menu behavior, and command UX matching campaign instead of MP.
