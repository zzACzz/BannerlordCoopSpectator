# Session Handoff - 2026-03-19 - 7C Battle Flow Engage

## Outcome

`PreBattleHold -> Ctrl+B -> BattleActive` now works with both materialized armies engaging.

This closes the current `7c` battle-flow spike for the existing runtime:
- armies materialize
- assigned peer possesses an existing materialized agent
- `PreBattleHold` keeps armies idle
- `Ctrl+B` transitions to `BattleActive`
- both sides now receive release/engage behavior and move into combat

## What was fixed

### 1. Formation-level hold/release

`CoopMissionBehaviors.TryApplyBattlePhaseFormationHold(...)` now applies:
- `MovementOrderStop`
- `FiringOrderHoldYourFire`
- `SetControlledByAI(true, true)`

during `PreBattleHold`, and then:
- `MovementOrderCharge`
- `FiringOrderFireAtWill`
- direct AI engage pulse

during `BattleActive`.

### 2. TeamAI absence workaround

In this runtime, materialized armies often do **not** have a usable `TeamAI`.

Logs showed:
- `DelegatedTeams=0`

So battle start could not rely on:
- `team.ResetTactic()`
- `team.DelegateCommandToAI()`

as the main wake-up path.

### 3. Direct engage pulse

`TryPulseFormationAiEngage(...)` now wakes AI agents directly by setting:
- automatic target selection
- `WatchState.Alarmed`
- `AIStateFlag.Alarmed`
- target formation / target agent
- `ForceAiBehaviorSelection()`

This was enough to get AI formations moving even without active `TeamAI`.

### 4. Player-formation asymmetry fix

The critical bug was:
- formations containing the player were skipped during `PreBattleHold`
- therefore they never entered `_battlePhaseHeldFormationKeys`
- and release logic originally touched only previously-held formations

Result:
- one side could engage
- the side containing the player could stay frozen

Fix:
- `BattleActive` release/pulse no longer depends on `wasHeld`
- all live battle formations are released/pulsed
- player-containing formations still skip only the player body at agent-pulse level

This is the change that made **both** sides attack.

## Current stable state

Working:
- live campaign battle snapshot
- dual identity snapshot contract (`OriginalCharacterId`, `SpawnTemplateId`)
- AI army materialization for both sides
- side switching
- existing-agent possession via `ReplaceBotWithPlayer(...)`
- `Ctrl+T` reset / repeat possession
- `PreBattleHold -> BattleActive`
- both sides engage after `Ctrl+B`

Still known issues:
- abandoned old bodies can still become weird / partially broken after later combat interactions
- AI army fidelity is still surrogate/template-based, not true 1:1 campaign runtime bodies
- player spawn is not yet architecturally final; possession path is working spike-quality, not final cleaned system

## Important files

- Main runtime logic:
  - `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`

- Previous possession handoff:
  - `C:\dev\projects\BannerlordCoopSpectator3\SESSION_HANDOFF_2026-03-19_7B_EXISTING_AGENT_POSSESSION.md`

## Key logs that confirmed the fix

Dedicated log after the final fix showed:
- `battle phase formation release detail ...`
- `battle phase formation hold state applied. Phase=BattleActive ... PulsedAgents=...`

User-visible result:
- both sides now go into attack after `Ctrl+B`

## Recommended next step

Do **not** go back into `Ctrl+B`/battle-start debugging unless a new regression appears.

Best next direction:
1. cleanup/polish current battle-flow state
2. improve AI army fidelity / mapping quality
3. later revisit final architecture for `existing-agent possession`

Lowest-value next step right now:
- further polishing temporary player-to-army re-anchor behavior

Highest-value next step right now:
- improve `1:1` army transfer fidelity while keeping current battle-flow stable
