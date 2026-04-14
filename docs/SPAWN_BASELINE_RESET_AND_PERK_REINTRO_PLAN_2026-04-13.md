# Spawn Baseline Reset And Perk Reintro Plan

Date: 2026-04-13
Project: `BannerlordCoopSpectator3`
Control baseline commit: `f773ab3637edb0552936b5dbc9694d997614aa80` (`f773ab3`)

## Why this reset exists

The 2026-04-13 perk/captain investigation produced useful analysis, but it also mixed in battle-runtime experiments around:

- exact-scene bootstrap on dedicated
- late agent resync
- mission object sync guards
- startup deferrals
- client spawn handoff patches
- deployment / phase-owner / readiness changes

The long debug cycle showed that the current blocker is not perk math by itself. The observed regressions moved through:

- empty battlefield on the client
- broken side/selectability state
- startup hangs / crashes
- successful server-side materialization followed by failed client population
- successful `replace-bot` followed by broken post-possession readiness state

That means the immediate priority is to recover a known-good spawn baseline before continuing captain/perk work.

## What was preserved before rollback

The full dirty-session diff was saved to:

- `.codex_tmp/perk-runtime-session-backup-2026-04-13.patch`

This backup exists so the perk/captain work can be selectively re-applied later instead of being rediscovered from scratch.

## What was rolled back

Battle-runtime files were returned to the control baseline so spawn can be revalidated first:

- `Campaign/BattleDetector.cs`
- `Infrastructure/BattleSnapshotRuntimeState.cs`
- `Infrastructure/CoopBattleEntryStatusBridgeFile.cs`
- `Mission/CoopMissionBehaviors.cs`
- `Mission/CoopMissionNetworkBridge.cs`
- `Network/Messages/BattleStartMessage.cs`
- `Patches/BattleMapSpawnHandoffPatch.cs`
- `SubModule.cs`

Temporary runtime patch file removed from the active baseline:

- `Patches/MissionObjectSyncCrashGuardPatch.cs`

Intentionally not part of this rollback:

- `Commands/CoopConsoleCommands.cs`
- `DedicatedHelper/DedicatedHelperLauncher.cs`

Those changes are outside the perk/spawn regression thread and should be evaluated separately.

## Required smoke-test baseline

Do not resume perk work until all of these are true again on a clean rerun:

1. Exact campaign battle opens in MP without server crash.
2. Team/entry UI shows the correct battlefield roster for both sides.
3. After choosing a side, armies visibly materialize on the client battlefield.
4. After choosing an entry and pressing `Deploy`, the player possesses a live unit instead of remaining in spectator.
5. Small battle and large battle both pass the same flow.
6. Battle still returns results to campaign.

## Runtime invariants to log and protect

When spawn is healthy, the next investigation window should verify these invariants in order:

1. `SelectSide` is handled on dedicated.
2. Entry-status file and roster file are from the same battle.
3. Initial battlefield materialization happens before player deploy.
4. Client receives live battlefield agents, not only UI roster data.
5. `SpawnNow` leads to possession of a live battlefield unit.
6. Post-possession phase transition does not clear or forget active armies.

Any future change that breaks one of these should be reverted or feature-flagged immediately.

## Reintroduction order after spawn is green

Re-add work in this order only:

1. Documentation + logs proving stable spawn.
2. Captain source only:
   - use native deployment/order-of-battle UI as the source of `formation.Captain`
   - do not change spawn/bootstrap while validating this
3. Personal perk subset for main hero only.
4. Personal perk subset for companions.
5. Captain perk subset driven from live `formation.Captain`.
6. Party leader / role perks (`Scout`, `Quartermaster`, `Engineer`, `Surgeon`, `ArmyCommander`).

Each step should be behind either:

- a small isolated patch set, or
- a feature flag that can be disabled without touching spawn-core

## Practical rule for the next window

If a change touches both:

- spawn/bootstrap/materialization/handoff, and
- perk/captain logic

split it before merging.

Spawn-core and perk-runtime must not be debugged as one blended system again.

## Short summary

The safe working assumption is:

- current failure is a runtime spawn/handoff regression, not a pure perk regression
- spawn baseline must be revalidated first
- perk/captain work should return only in narrow, log-backed slices
