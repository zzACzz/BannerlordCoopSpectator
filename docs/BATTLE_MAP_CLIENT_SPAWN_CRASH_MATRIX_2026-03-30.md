# Battle-Map Client Spawn Crash Matrix

Date: 2026-03-30
Scope: experiment log for battle-map startup, spawn crash isolation, and final stabilization

## Purpose

This matrix exists so future windows do not retry already disproven ideas and can see exactly which factor finally unlocked the large-battle spawn.

## Experiment Matrix

| ID | Change | Result | Conclusion |
| --- | --- | --- | --- |
| E01 | Move scene selection from hardcoded `mp_tdm_map_001` to scene-aware `set_options` path. | Dedicated opened battle-map runtime successfully. | Scene transfer path is working and is no longer the main blocker. |
| E02 | Switch battle-map shell from fake TDM bootstrap to `MultiplayerBattle` / `Battle`. | Client and dedicated both stopped failing for simple shell mismatch reasons. | Correct shell matters; do not go back to mixed `CoopBattle/Battle` option mismatch. |
| E03 | Strip battle-map server stack too aggressively, including `MissionLobbyComponent`. | Dedicated crashed earlier, before usable startup markers. | `MissionLobbyComponent` is startup-critical for battle-map server runtime. |
| E04 | Restore native bootstrap-critical server behaviors selectively. | Dedicated eventually reached stable battle-map startup. | Server startup problem is mostly solved. |
| E05 | Reintroduce client native bootstrap chain for battle-map. | Client finally loaded battle-map without mission-load crash. | Client bootstrap still needs native components; do not over-strip them early. |
| E06 | Remove `MultiplayerTeamSelectComponent` from wrapped Battle client stack. | Client started crashing already on mission load. | This removal is wrong for current bootstrap; keep the component alive during startup. |
| E07 | Keep `MultiplayerTeamSelectComponent`, but suppress native gauntlet entry UI and inject `CoopSelection`. | Client loads map, custom overlay works, selection works. | This is the workable startup baseline. |
| E08 | Add `G`-hotkey path to battle-map overlay. | `G` starts battle correctly after spawn. | Coop start-battle ownership works on battle-map. |
| E09 | Neutralize native 30-second end by stretching warmup/timer on dedicated. | Battle no longer auto-ends in 30 seconds; native banner remains cosmetic. | Functional timer issue solved enough to continue. |
| E10 | Replace fixed side caps with campaign battle-size budget. | Large battle-map runtime now materializes much larger armies, for example `95 vs 105`. | Campaign battle size now influences runtime caps; this is the correct direction. |
| E11 | Initial large battle-map spawn run after startup stabilization. | Dedicated successfully performs `replace-bot` possession. Client still crashes after spawn sync. | The blocker is a client-side post-spawn crash, not startup, not dedicated, not scene transfer. |
| E12 | Relax / refactor native entry UI suppression around spawn-handshake. | Client still crashes. | Native gauntlet suppression was not the last decisive factor. |
| E13 | Suppress local `MissionPeer.FollowedAgent` network echo during spawn-handshake. | Echo disappeared from logs, but crash remained. | Useful cleanup, but not the final root cause. |
| E14 | Send explicit `SetAgentOwningMissionPeer(... peer.VirtualPlayer)` after replace-bot. | Client received the rebind, but crash remained. | Ownership rebind was necessary alignment, not the final fix. |
| E15 | Finalize local player visuals immediately after `SetAgentPeer`. | Helps normalize local handoff and remains part of the stable baseline, but large-battle crash still remained on its own. | Keep this in the stack, but it was not sufficient alone. |
| E16 | Compare crash-run large battle vs success-run tiny battle. | Large run produced live formation handoff with many AI bots; tiny run was effectively near-solo. | The strong differentiator is formation/captain handoff in large battles. |
| E17 | Suppress local `AssignFormationToPlayer` during battle-map spawn-handshake for live formations. | Large battle-map spawn succeeded on `mp_battle_map_002`; client stayed alive. | This was the decisive isolation and is the current key fix. |

## Final Resolution Summary

The crash was ultimately tied to native formation/captain handoff during client spawn in large battles.

Important observed pattern:

- tiny battle success: the player effectively entered an almost solo formation
- large battle crash: the player immediately entered a non-empty cavalry formation and hit `AssignFormationToPlayer`

The final stable client-side handoff sequence is:

1. local visual finalize after `SetAgentPeer`
2. suppress local `AssignFormationToPlayer` during the narrow spawn-handshake
3. suppress local `MissionPeer.FollowedAgent` network echo

## Validated Evidence Of Success

Latest successful large-battle run:

- Client log: `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_81592.txt`
- Host log: `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_38372.txt`
- Dedicated log: `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\logs\rgl_log_71520.txt`

Key markers:

- host resolved `battle_terrain_biome_087b -> mp_battle_map_002`
- dedicated materialized `95 vs 105`
- dedicated `materialized army replace-bot succeeded`
- client `BattleMapSpawnHandoffPatch: suppressed local AssignFormationToPlayer during battle-map spawn handshake`
- client `BattleMapSpawnHandoffPatch: suppressed local MissionPeer.FollowedAgent network echo during battle-map spawn handshake`
- client remained alive after the old crash boundary

## Hard Rules For Future Windows

1. Do not remove `MultiplayerTeamSelectComponent` from the wrapped Battle client stack again without new evidence.
2. Do not re-open scene-transfer or dedicated-startup hypotheses unless logs clearly move the boundary earlier.
3. Treat `AssignFormationToPlayer` / formation captain handoff as the key large-battle differentiator.
4. Change one client handoff factor at a time.
5. Preserve the current stable handoff patch stack until a cleaner long-term replacement is proven.
