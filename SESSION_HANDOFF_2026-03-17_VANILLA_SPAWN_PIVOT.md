## Goal of this handoff
Document the pivot from direct/manual coop player spawn to the vanilla TDM spawn pipeline, the root cause that made infantry finally spawn alive, and the safest next steps.

## Current proven state
Working now:
- vanilla TDM `Team Selection` overlay is suppressed as presentation
- side selection is driven by coop authority
- troop selection is driven by coop authority
- infantry can spawn as a live/usable player-controlled agent
- the client no longer crashes on infantry spawn from negative gold sync

Current constraints:
- `Ctrl+T` reset is intentionally disabled
- direct/manual `Mission.SpawnAgent(...)` player spawn is intentionally retired from runtime
- vanilla spawn path is now the only proven-good path for live agents

## What the actual root cause was
There were two layered problems.

### 1. Direct/manual player spawn was the wrong architectural path
We spent many iterations trying to make direct coop spawn behave like a normal vanilla MP player spawn.
That path kept producing "half-alive" agents even after:
- agent visuals
- ownership sync
- equipment refresh
- peer team/culture/troop index sync
- main agent controller repair

Conclusion:
- the issue was not one missing flag
- direct/manual spawn was bypassing too much of the vanilla `TeamDeathmatch` / `SpawningBehaviorBase` lifecycle

### 2. Vanilla spawn then exposed a hidden MP gold/economy precondition
After pivoting back to vanilla spawn, infantry finally reached:
- `CreateAgent`
- `SetAgentHealth`
- `SetAgentIsPlayer`
- `SetAgentPeer`

But the client still crashed immediately after spawn.

The exact crash cause from logs:
- dedicated wrote invalid compressed value `-20`
- client then failed on `SyncGoldsForSkirmish`

Root source:
- vanilla `TeamDeathmatchSpawningBehavior.OnAllAgentsFromPeerSpawnedFromVisuals()` deducts `TroopCasualCost`
- our coop runtime effectively had `0` gold
- vanilla deducted class cost anyway
- result: negative gold packet, then client crash

The fix:
- before `SetEarlyAgentVisualsDespawning(...)`, server now raises the peer gold floor to at least the selected class `TroopCasualCost`
- vanilla deduction then lands on `0`, not negative

## Important code state
Main file:
- [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs)

Important runtime decisions now encoded:
- `EnableDirectCoopPlayerSpawnExperiment = false`
- server tick no longer calls the retired direct spawn path
- client authoritative status requests vanilla spawn via `RequestToSpawnAsBot()`
- server finalizes pending visuals via `SetEarlyAgentVisualsDespawning(...)`
- server ensures vanilla spawn gold floor before visuals finalize
- own entry menu now says `Ctrl+T disabled`

Important log line for the gold fix:
- `CoopMissionSpawnLogic: raised vanilla spawn gold floor before visuals finalize...`

## Do not regress into these paths
Do not return to:
- manual player `Mission.SpawnAgent(...)` as the primary coop spawn path
- trying to revive "half-alive" direct spawn with more finalize flags
- risky behavior removal of core vanilla mission behaviors
- overlay click adaptation as a control source

## Best next steps
### 1. Stabilize the vanilla path further
Validate:
- cavalry spawn
- repeated respawns after death
- side switch then spawn
- troop switch then spawn

### 2. Redesign `Ctrl+T` against vanilla lifecycle
Do not restore the previous detach/fade hacks.

`Ctrl+T` should become one of:
- a vanilla-compatible reset-to-spectator/request-new-spawn path
- or a pure authority-state reset that lets vanilla own the actual respawn transition

### 3. Clean up leftover legacy code in a separate pass
Safe cleanup candidates:
- remove retired direct-spawn helper calls that are no longer referenced
- remove stale diagnostics that only existed for the abandoned direct-spawn investigation
- keep only diagnostics that still help on vanilla spawn / respawn validation

## Short summary
The breakthrough was not a UI fix and not a tiny spawn flag.

The real fix was:
1. stop forcing manual direct player spawn
2. let vanilla TDM spawn create the real player agent
3. satisfy vanilla's hidden MP economy precondition so spawn packets stay valid

That is the current architecture to preserve.
