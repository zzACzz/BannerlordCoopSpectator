# CoopBattle Behavior Stack Inventory

Date: 2026-03-27
Project: `BannerlordCoopSpectator3`
Focus: `clean CoopBattle runtime / behavior inventory / prune plan`

Related docs:

- `docs/CLEAN_COOP_BATTLE_BOOTSTRAP_PLAN_2026-03-27.md`
- `docs/HOST_AFTERMATH_MAPPING_PLAN_2026-03-26.md`

Primary code references:

- `GameMode/MissionMultiplayerCoopBattleMode.cs`
- `GameMode/MissionMultiplayerCoopBattle.cs`
- `GameMode/MissionMultiplayerCoopBattleClient.cs`
- `Mission/CoopMissionBehaviors.cs`
- `GameMode/MissionBehaviorHelpers.cs`
- `GameMode/MissionMultiplayerTdmCloneMode.cs`

## 1. Purpose

This document inventories the current `CoopBattle` mission behavior stack after the
first explicit server/client split.

The goal is to classify each behavior as:

- `required`
- `optional`
- `removable-later`

and to define the first safe prune path for migrating away from `TDM` gameplay
dependencies without breaking mission bootstrap or client join.

## 2. Immediate finding fixed before this inventory

Before this pass, `MissionMultiplayerCoopBattleMode` added several client-side
behaviors even when `GameNetwork.IsServer == true`.

That included:

- `MissionMultiplayerCoopBattleClient`
- `CoopMissionClientLogic`
- `MissionLobbyEquipmentNetworkComponent`

This was inconsistent with the already learned `TdmClone` safety rules.

The current code now has an explicit split:

- server stack builder
- client stack builder
- server/client sanity validation

This does not yet change gameplay semantics, but it removes a known bootstrap risk.

## 3. Current server stack

Current builder:

- `MissionMultiplayerCoopBattleMode.BuildServerMissionBehaviorsForCoopBattle(...)`

Current server behaviors:

1. `MissionLobbyComponent`
2. `MissionMultiplayerCoopBattle`
3. `MultiplayerAchievementComponent` if available
4. `MultiplayerTimerComponent`
5. `MissionHardBorderPlacer`
6. `MissionBoundaryPlacer`
7. `MissionBoundaryCrossingHandler`
8. `MultiplayerPollComponent`
9. `MultiplayerAdminComponent`
10. `MultiplayerGameNotificationsComponent` on non-dedicated only
11. `MissionOptionsComponent` if available
12. `MissionScoreboardComponent` on non-dedicated only
13. `MissionMatchHistoryComponent` if available
14. `EquipmentControllerLeaveLogic` if available
15. `MissionRecentPlayersComponent` if available
16. `MultiplayerPreloadHelper` if available
17. `MissionAgentPanicHandler` if available
18. `AgentHumanAILogic` if available
19. `MissionBehaviorDiagnostic`
20. `CoopMissionSpawnLogic`

Server behaviors intentionally excluded:

- `MissionMultiplayerCoopBattleClient`
- `CoopMissionClientLogic`
- `MultiplayerMissionAgentVisualSpawnComponent`
- `MissionLobbyEquipmentNetworkComponent`
- `MultiplayerTeamSelectComponent`

## 4. Current client stack

Current builder:

- `MissionMultiplayerCoopBattleMode.BuildClientMissionBehaviorsForCoopBattle(...)`

Current client behaviors:

1. `MissionLobbyComponent`
2. `MissionMultiplayerCoopBattleClient`
3. `MultiplayerAchievementComponent` if available
4. `MultiplayerTimerComponent`
5. `MultiplayerMissionAgentVisualSpawnComponent` if available
6. `MissionLobbyEquipmentNetworkComponent` only if visual spawn exists
7. `MissionHardBorderPlacer`
8. `MissionBoundaryPlacer`
9. `MissionBoundaryCrossingHandler`
10. `MultiplayerPollComponent`
11. `MultiplayerAdminComponent`
12. `MultiplayerGameNotificationsComponent` on non-dedicated only
13. `MissionOptionsComponent` if available
14. `MissionScoreboardComponent` on non-dedicated only
15. `MissionMatchHistoryComponent` if available
16. `EquipmentControllerLeaveLogic` if available
17. `MissionRecentPlayersComponent` if available
18. `MultiplayerPreloadHelper` if available
19. `MissionBehaviorDiagnostic`
20. `CoopMissionClientLogic`

Client behaviors intentionally excluded:

- `MissionMultiplayerCoopBattle`
- `CoopMissionSpawnLogic`
- `MultiplayerTeamSelectComponent`

## 5. Classification

## 5.1 Server: required now

These should stay in the first clean-mode migration unless a concrete replacement is
implemented in the same pass.

- `MissionLobbyComponent`
  Reason: baseline multiplayer mission/lobby wiring.

- `MissionMultiplayerCoopBattle`
  Reason: this is the actual clean gameplay runtime base.

- `MultiplayerTimerComponent`
  Reason: low-risk standard MP mission dependency.

- `MissionHardBorderPlacer`
  Reason: already treated as required in code and helper comments.

- `MissionBoundaryPlacer`
  Reason: already treated as required in code and helper comments.

- `MissionBoundaryCrossingHandler`
  Reason: boundary UI/runtime can crash without it.

- `MultiplayerPollComponent`
  Reason: already identified as expected by vanilla MP shell.

- `MultiplayerAdminComponent`
  Reason: standard MP server-side support behavior; low-risk to keep for now.

- `CoopMissionSpawnLogic`
  Reason: authoritative coop battle runtime and battle result bridge depend on it.

## 5.2 Client: required now

- `MissionLobbyComponent`
  Reason: baseline multiplayer mission/lobby wiring.

- `MissionMultiplayerCoopBattleClient`
  Reason: mode-specific client-side game mode contract.

- `MultiplayerTimerComponent`
  Reason: low-risk standard MP mission dependency.

- `MissionHardBorderPlacer`
  Reason: treated as required in code.

- `MissionBoundaryPlacer`
  Reason: treated as required in code.

- `MissionBoundaryCrossingHandler`
  Reason: needed for boundary UI/runtime safety.

- `MultiplayerPollComponent`
  Reason: expected by vanilla MP shell.

- `CoopMissionClientLogic`
  Reason: current coop entry flow, UI suppression, class filtering, and battle bridge
  live here.

## 5.3 Conditionally required now

These are not universally mandatory, but removing them now would be premature.

- `MultiplayerMissionAgentVisualSpawnComponent`
  Reason: needed if `MissionLobbyEquipmentNetworkComponent` is used.

- `MissionLobbyEquipmentNetworkComponent`
  Reason: depends on visual spawn and still supports current entry/equipment shell.

- `MissionOptionsComponent`
  Reason: helper comments and diagnostics indicate vanilla UI expects it.

- `MissionBehaviorDiagnostic`
  Reason: not gameplay-critical, but still important during migration.

## 5.4 Keep for now, candidate `removable-later`

These are not the first target for removal, but they are not core to coop battle
semantics.

- `MultiplayerAchievementComponent`
- `MultiplayerGameNotificationsComponent`
- `MissionScoreboardComponent`
- `MissionMatchHistoryComponent`
- `EquipmentControllerLeaveLogic`
- `MissionRecentPlayersComponent`
- `MultiplayerPreloadHelper`
- `MissionAgentPanicHandler`
- `AgentHumanAILogic`

Notes:

- `MissionScoreboardComponent` is especially sensitive.
- `TdmClone` research already showed it may be required if
  `MissionCustomGameServerComponent` participates in the stack.
- Therefore it is `removable-later`, not `remove-now`.

## 5.5 Explicitly not part of clean CoopBattle runtime

These should stay out unless a later UI phase intentionally reintroduces them.

- `MultiplayerTeamSelectComponent`
  Reason: current coop entry flow deliberately avoids vanilla team-select overlay.

- TDM score/round ownership
  Reason: this is exactly what the clean runtime is trying to escape.

## 6. First safe prune path

The first prune pass should not try to make the stack tiny.

It should only make the stack honest and explicitly custom-owned.

Recommended order:

1. Keep the current split server/client stack.
2. Keep boundary, poll, options, and current visual/equipment chain intact.
3. Keep diagnostics during migration.
4. Do not touch scoreboard yet.
5. Only after a stable clean `CoopBattle` live run:
   - test whether `MissionMatchHistoryComponent` is removable
   - test whether `MissionRecentPlayersComponent` is removable
   - test whether `MultiplayerAchievementComponent` is removable
   - test whether `MultiplayerPreloadHelper` is removable

## 7. Most important migration rule

For the next coding phases, treat behaviors in three buckets:

- battle-critical:
  - `MissionMultiplayerCoopBattle`
  - `CoopMissionSpawnLogic`
  - `CoopMissionClientLogic`

- bootstrap-critical:
  - lobby
  - timer
  - boundary handlers
  - poll
  - options
  - visual/equipment chain

- convenience or shell-level:
  - scoreboard
  - notifications
  - achievements
  - history/recent/preload

The next runtime migration should modify the first bucket first, not the third.

## 8. Recommended next coding step

The next useful implementation step is:

- move more battle-flow ownership into `MissionMultiplayerCoopBattle` and
  `CoopMissionSpawnLogic`
- while leaving the bootstrap-critical shell intact

In other words:

- do not start by deleting UI/support behaviors
- start by moving actual battle lifecycle ownership out of remaining TDM assumptions
