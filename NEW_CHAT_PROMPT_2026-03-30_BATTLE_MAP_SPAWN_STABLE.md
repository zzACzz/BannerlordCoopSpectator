# New Chat Prompt - 2026-03-30 - Battle-Map Spawn Stable

Continuing work in `C:\dev\projects\BannerlordCoopSpectator3`.

Start by reading:

- `docs/README.md`
- `PROJECT_CONTEXT.md`
- `HUMAN_NOTES_MULTIPLAYER_PROGRESS.md`
- `docs/BATTLE_MAP_STATUS_AND_HANDOFF_2026-03-30.md`
- `docs/BATTLE_MAP_CLIENT_SPAWN_CRASH_MATRIX_2026-03-30.md`

## Current validated state

- campaign battle scene transfer to battle-map MP runtime works
- dedicated startup on `mp_battle_map_*` is stable
- client loads battle-map successfully
- custom `CoopSelection` overlay works on battle-map
- side selection, unit selection, `Spawn`, and `G`-start battle are implemented
- battle results, prisoners, and aftermath already return to campaign
- native 30-second timer is functionally neutralized
- large battle-map client spawn no longer crashes

## Latest validated evidence

Large-battle success run:

- client log: `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_81592.txt`
- host log: `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_38372.txt`
- dedicated log: `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\logs\rgl_log_71520.txt`

Key proof points:

- host resolved `battle_terrain_biome_087b -> mp_battle_map_002`
- dedicated materialized `95 vs 105` runtime agents under `BattleSizeBudget=200`
- dedicated completed `materialized army replace-bot succeeded`
- client completed:
  - `SetAgentPeer`
  - `BattleMapSpawnHandoffPatch: finalized local player agent visuals after SetAgentPeer`
  - `BattleMapSpawnHandoffPatch: suppressed local AssignFormationToPlayer during battle-map spawn handshake`
  - `SynchronizeAgentSpawnEquipment`
  - `BattleMapSpawnHandoffPatch: suppressed local MissionPeer.FollowedAgent network echo during battle-map spawn handshake`
- client stayed alive and later cleanly exited to lobby instead of crashing

## Root cause summary

The decisive remaining crash cause was not scene transfer, not dedicated startup, not equipment sync alone, and not map size alone.

The large-battle path was triggering native captain/formation handoff during spawn:

- `AssignFormationToPlayer`
- `BotsControlledChange`
- local formation selection / captain flow

This only became dangerous when the player spawned into a non-empty AI formation, which happened in large battles and did not reproduce in tiny test battles.

## Current fix set to preserve

Do not casually remove these without new evidence:

- source-level native entry UI suppression for battle-map authoritative spawn path
- explicit `SetAgentOwningMissionPeer(... peer.VirtualPlayer)` rebind after replace-bot
- client-side visual finalization after `SetAgentPeer`
- suppression of local `MissionPeer.FollowedAgent` network echo
- suppression of local `AssignFormationToPlayer` during battle-map spawn handshake when the peer is being attached to a live formation

Primary file:

- `Patches/BattleMapSpawnHandoffPatch.cs`

Supporting files touched in this investigation:

- `Patches/VanillaEntryUiSuppressionPatch.cs`
- `Mission/CoopMissionBehaviors.cs`
- `SubModule.cs`

## Remaining work

1. Validate repeated large-battle cycles, not just one successful spawn.
2. Confirm whether suppressing local `AssignFormationToPlayer` has any hidden regression in command/captain gameplay after battle start.
3. Reduce selection UI flicker caused by authoritative side/entry correction churn.
4. Improve spawn/deployment frames. Large battles still show many `failed to resolve formation spawn frame` fallbacks.
5. Hide or replace the cosmetic native warmup banner.
6. Run 2-4 client matrix and long-session reliability tests.

## Working assumptions for the next window

- Treat battle-map client spawn crash as resolved for the current baseline.
- Treat formation/captain handoff as the important large-battle differentiator.
- Avoid re-opening already disproven hypotheses unless a new regression appears.
- Prefer one-factor changes and log-backed validation.
