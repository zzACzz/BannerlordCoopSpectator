# Battle-Map Status And Handoff

Date: 2026-03-30
Project: `BannerlordCoopSpectator3`
Scope: current validated state after battle-map client spawn stabilization

## Purpose

This is the current source of truth for battle-map runtime work.

It answers:

- what now works end-to-end
- what the decisive spawn fix was
- which logs prove it
- what still remains before the feature can be considered stable

## Latest Validated Run

Primary evidence for the current stable baseline:

- Client log: `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_81592.txt`
- Host log: `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_38372.txt`
- Dedicated log: `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\logs\rgl_log_71520.txt`

## Executive Summary

The battle-map path has crossed the previous spawn blocker.

Validated in the latest large-battle run:

- campaign battle scene resolved to `mp_battle_map_002`
- dedicated started battle-map runtime cleanly
- dedicated materialized `95 vs 105` runtime agents under `BattleSizeBudget=200`
- client loaded the map and used the custom `CoopSelection` overlay
- client spawned successfully in the large battle without the old spawn crash
- dedicated entered `PreBattleHold` and then `BattleActive`
- battle continued normally instead of dying at spawn-handshake

This means the main blocker is no longer `client crash on battle-map spawn`.

## Current Validated Working Areas

| Area | Status | Notes |
| --- | --- | --- |
| Campaign battle detection | Working | `BattleDetector` resolves live battle and writes runtime payload. |
| Scene transfer | Working | Campaign battle scene is mapped to `mp_battle_map_*`. |
| Dedicated battle-map startup | Working | Dedicated starts `Battle` runtime on battle-map without startup crash. |
| Client mission load on battle-map | Working | Client reaches `MissionScreen` and initializes battle-map runtime. |
| Custom battle-map selection overlay | Working | Side selection, unit selection, explicit `Spawn`, and `G`-start battle work. |
| Large-battle runtime population | Working baseline | Latest validated run materialized `95 vs 105` runtime agents with budget `200`. |
| Replace-bot possession | Working | Dedicated performs `materialized army replace-bot succeeded` and possession remains stable on client. |
| Client spawn handoff | Working baseline | Large-battle client spawn no longer crashes. |
| Result writeback to campaign | Working | Battle results, prisoners, and aftermath already return to campaign. |
| Native 30-second timer | Functionally neutralized | Warmup banner remains cosmetic, but runtime no longer auto-ends after 30 seconds. |

## Root Cause Summary

The final crash cause was narrower than the earlier hypotheses.

The decisive difference between failing large battles and working tiny battles was not just scene choice:

- tiny battles effectively spawned the player into an almost solo formation
- large battles spawned the player into a live AI formation and immediately triggered native captain/formation handoff

The dangerous client-side handoff window was:

1. `ReplaceBotWithPlayer`
2. `SetAgentIsPlayer`
3. `SetAgentPeer`
4. local visual/equipment handoff
5. `AssignFormationToPlayer`
6. `SynchronizeAgentSpawnEquipment`
7. local `FollowedAgent` echo

In the large-battle crash path, `AssignFormationToPlayer` arrived while the player was being attached to a non-empty cavalry formation. That was the strongest differentiator between crash-runs and success-runs.

## Fixes That Make Up The Current Stable Baseline

These should be treated as a set until a cleaner replacement exists:

1. Keep the native battle-map bootstrap stack intact enough for load stability.
2. Keep source-level native entry UI suppression instead of tearing out startup-critical components.
3. Explicitly rebind owning peer after replace-bot with `SetAgentOwningMissionPeer(... peer.VirtualPlayer)`.
4. Finalize local player visuals immediately after `SetAgentPeer`.
5. Suppress local `MissionPeer.FollowedAgent` network echo during battle-map spawn handshake.
6. Suppress local `AssignFormationToPlayer` during battle-map spawn handshake when the player is being attached to a live AI formation.

Primary owner file:

- `Patches/BattleMapSpawnHandoffPatch.cs`

Supporting files:

- `Patches/VanillaEntryUiSuppressionPatch.cs`
- `Mission/CoopMissionBehaviors.cs`
- `SubModule.cs`

## Log Proof Of The Fix

### Host

From `rgl_log_38372.txt`:

- `BattleDetector: multiplayer runtime scene candidate resolved ... RuntimeScene=mp_battle_map_002`
- `DedicatedServerCommands: applying scene-aware mission selection before start_mission ... RequestedScene=mp_battle_map_002`

### Dedicated

From `rgl_log_71520.txt`:

- `battlefield armies materialized. AttackerAgents=95 DefenderAgents=105 BattleSizeBudget=200`
- `materialized army replace-bot succeeded ... ActiveAiUnitsInFormation=16`
- `possessed materialized army agent via vanilla replace-bot flow`
- `Phase=PreBattleHold`
- `Phase=BattleActive`

The same run later performed another replace-bot possession successfully as well, which is extra confidence that the flow is no longer dying only on the first spawn.

### Client

From `rgl_log_81592.txt`:

- `BattleMapSpawnHandoffPatch: finalized local player agent visuals after SetAgentPeer for battle-map handoff`
- `Processing message: AssignFormationToPlayer`
- `BattleMapSpawnHandoffPatch: suppressed local AssignFormationToPlayer during battle-map spawn handshake`
- `Processing message: SynchronizeAgentSpawnEquipment`
- `BattleMapSpawnHandoffPatch: suppressed local MissionPeer.FollowedAgent network echo during battle-map spawn handshake`

Most importantly, the client did not crash after this boundary and later exited normally to lobby.

## What Is Still Not Fully Closed

| Area | Status | Notes |
| --- | --- | --- |
| Repeated large-battle reliability | Needs validation | One validated large run is good evidence, not final proof. |
| Command/captain gameplay after spawn | Needs validation | Current fix suppresses local `AssignFormationToPlayer`; hidden regressions are still possible. |
| Spawn frame quality | Incomplete | Large battles still show many `failed to resolve formation spawn frame` fallbacks. |
| Deployment zones / campaign-like placement | Partial | Runtime population works, but spatial placement is still primitive. |
| Native warmup banner | Cosmetic issue | Banner still looks like vanilla MP warmup. |
| Selection list flicker | Minor UX issue | Likely caused by authoritative side/entry correction churn. |
| Multi-client matrix | Not yet closed | Still needs 2-4 client validation. |

## Recommended Next Work Order

1. Re-run several large battle-map scenarios on different terrain -> `mp_battle_map_*` mappings.
2. Verify that the player can still command troops correctly after the current `AssignFormationToPlayer` suppression.
3. If a new spawn regression appears, isolate `BotsControlledChange` / `ControlledFormation` metadata next, not scene transfer or map load.
4. Improve spawn/deployment frame resolution for large armies.
5. Reduce selection flicker and remove cosmetic native warmup leftovers.
6. Run 2-4 client stability tests.
