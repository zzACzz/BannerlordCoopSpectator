# Session Handoff - 2026-03-22 - Writeback And Battle Completion

## Outcome

`equipment fidelity` is effectively closed, `hero/companion/lord` runtime path is working, and the live `stats / skills / perks / party modifiers` layer is already applied in runtime.

The current active frontier is:
- `campaign result / casualty / xp / writeback`
- `authoritative battle completion`

The main issue discovered in the latest live test was:
- the battle result was **not** written when the mission ended through the current dedicated flow
- the user also exited in a way that broke the normal campaign loop (`main menu`), which is not a valid writeback scenario

Because of that, the session pivoted from raw writeback audit to:
- making battle completion authoritative inside the coop mission
- writing result **at the moment one side is fully eliminated**
- only then trying to return host/client to campaign

## What is already working

### 1. Equipment layer

Synthetic full-campaign roster coverage reached:
- `TopMisses=[(none)]`
- `TopNormalizedFallbacks=[(none)]`

This means:
- direct imported equipment works for the main catalog
- final MP-incompatible tail is covered by `compat-standin`
- no unresolved equipment item remains in the synthetic harness

### 2. Hero path

Working end-to-end:
- `main hero`
- `companion`
- `lord`

This includes:
- hero identity metadata
- hero runtime template policy
- hero equipment overrides
- possession/runtime shell

### 3. Combat profile layer

Working layers:
- `skills`
- `derived attributes`
- `perks`
- `mounted/riding`
- `party modifiers`

Validated on:
- synthetic all-campaign troops
- synthetic live heroes
- real live campaign battle

### 4. Party/commander modifiers

Live dedicated logs already showed working counters like:
- `party-morale`
- `party-tactics`
- `party-captain`
- `party-quartermaster`
- `party-surgeon`
- `party-engineer`
- `party-scout`

So this is not data-only anymore; runtime application is already in place.

## Current active problem

### Previous bad test

The latest user test before this handoff was not authoritative for writeback because:
- the user left the battle to the main menu
- that breaks the normal campaign return flow
- host then cannot be treated as a stable receiver of battle results

### Real blocker found in logs

In that same run:
- dedicated did **not** log `battle result snapshot written`
- host did **not** log `consumed battle_result writeback audit`
- `battle_result.json` was not observed

This showed that:
- result writing was not reliably happening in the old mission-end lifecycle
- `OnEndMission()` was not a reliable hook for this dedicated flow

## What was implemented after that

### 1. XP-ready result schema

The battle result schema now carries not only casualties, but also raw combat contribution and compact combat-event data.

Implemented in:
- [CoopBattleResultBridgeFile.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/CoopBattleResultBridgeFile.cs)

New data includes:
- per-entry:
  - `ScoreHitCount`
  - `HitsTakenCount`
  - `FatalHitCount`
  - `KillsInflictedCount`
  - `UnconsciousInflictedCount`
  - `RoutedInflictedCount`
  - `DamageDealt`
  - `DamageTaken`
- battle-level:
  - `CombatEvents`
  - `DroppedCombatEventCount`

This is the foundation for later XP writeback via campaign-side `CombatXpModel`.

### 2. Mission-side combat event tracking

Implemented in:
- [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs)

Added:
- `OnScoreHit(...)` tracking
- removal tracking with attacker attribution
- compact combat events with:
  - attacker/victim entry ids
  - weapon skill hint
  - weapon class hint
  - damage
  - shot difficulty
  - hit distance
  - fatal flag

### 3. Campaign-side result audit consume

Implemented in:
- [BattleDetector.cs](/C:/dev/projects/BannerlordCoopSpectator3/Campaign/BattleDetector.cs)

Host-side audit now knows how to consume and summarize:
- casualties
- hits
- damage
- combat event count
- dropped combat event count

### 4. Main lifecycle fix attempt

The old result-writing hook was moved to:
- `OnMissionResultReady(...)` as primary
- `OnEndMission()` kept as fallback

This was still not enough by itself, because the mission must first reach a valid completion state.

### 5. Authoritative battle completion

This is the newest and still unvalidated pass.

Implemented in:
- [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs)
- [CoopBattleEntryStatusBridgeFile.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/CoopBattleEntryStatusBridgeFile.cs)

What it now does:
- during `BattleActive`, dedicated checks live active non-mount agents per side
- if one side reaches `0`, dedicated:
  - sets phase to `BattleEnded`
  - records authoritative `WinnerSide`
  - records `BattleCompletionReason`
  - writes `battle_result` immediately

### 6. Battle-ended bridge to client/host

`battle_entry_status.txt` now also carries:
- `WinnerSide`
- `BattleCompletionReason`

Client-side logic now:
- observes `BattleEnded`
- logs the winner/reason
- tries to locally end the mission through reflection-based local exit path

This is intentionally a best-effort reaction:
- the important part is that result should already be written on dedicated before local exit becomes relevant

## Key files

Main runtime:
- [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs)

Campaign host:
- [BattleDetector.cs](/C:/dev/projects/BannerlordCoopSpectator3/Campaign/BattleDetector.cs)

Battle result bridge:
- [CoopBattleResultBridgeFile.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/CoopBattleResultBridgeFile.cs)

Battle status bridge:
- [CoopBattleEntryStatusBridgeFile.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/CoopBattleEntryStatusBridgeFile.cs)

Battle phase bridge/runtime:
- [CoopBattlePhaseBridgeFile.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/CoopBattlePhaseBridgeFile.cs)
- [CoopBattlePhaseRuntimeState.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/CoopBattlePhaseRuntimeState.cs)

Snapshot/runtime DTO:
- [BattleStartMessage.cs](/C:/dev/projects/BannerlordCoopSpectator3/Network/Messages/BattleStartMessage.cs)
- [BattleSnapshotRuntimeState.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/BattleSnapshotRuntimeState.cs)

Dedicated build:
- [CoopSpectatorDedicated.csproj](/C:/dev/projects/BannerlordCoopSpectator3/DedicatedServer/CoopSpectatorDedicated.csproj)

## Current expected test flow

The next correct validation is **not**:
- leaving the battle to main menu

The next correct validation **is**:
1. restart dedicated completely
2. start a real live battle:
   - `main hero + companion`
   - against an army with a lord
3. finish the battle normally:
   - one side must fully die / become eliminated
4. do **not** manually leave to main menu
5. let the new authoritative battle completion path fire
6. after mission exit, return to campaign

## What the next logs must contain

### Dedicated log

Must contain:
- `authoritative battle completion detected`
- `battle result snapshot written`

Should also contain:
- `WinnerSide=...`
- `Reason=attacker-eliminated|defender-eliminated|mutual-elimination`

### Host log

Must contain:
- `BattleDetector: consumed battle_result writeback audit`

Should also contain:
- `Hits=...`
- `Damage=...`
- `CombatEvents=...`
- `DroppedCombatEvents=...`

## If the next test partially fails

### Case A: dedicated writes result, host consumes it

Then the next step is:
- real roster apply/writeback
- casualties/wounded updates
- troop XP
- hero/companion/lord wound + XP writeback

### Case B: dedicated writes result, but client/host mission does not auto-close

Then the next step is narrow:
- fix local mission-exit reaction
- do **not** rework writeback schema again

### Case C: dedicated still does not write result

Then the next step is:
- inspect the dedicated battle-end lifecycle after `BattleEnded`
- hook an even earlier authoritative path if needed
- but keep the current side-elimination detection as the source of truth

## Recommended next implementation after validation

If the next live run is good:
1. apply casualties/wounded back to `TroopRoster`
2. compute troop XP on host from recorded combat events via campaign `CombatXpModel`
3. write hero/companion/lord wound state and XP via `Hero` / `HeroDeveloper`
4. only later think about full reward/reputation/economy parity

## Build state at handoff

Both builds are green:
- [CoopSpectator.csproj](/C:/dev/projects/BannerlordCoopSpectator3/CoopSpectator.csproj)
- [CoopSpectatorDedicated.csproj](/C:/dev/projects/BannerlordCoopSpectator3/DedicatedServer/CoopSpectatorDedicated.csproj)

Known non-blocking noise:
- old `CS0162` unreachable warnings
- old `System.Management` reference conflict warnings

They are not the current blocker.
