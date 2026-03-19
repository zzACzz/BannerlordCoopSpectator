## Session Handoff — 2026-03-19 — 7b Existing-Agent Possession

### Current proven state

- `live-battle snapshot` works from campaign `MapEvent`
- dual-identity contract works:
  - `OriginalCharacterId`
  - `SpawnTemplateId`
- AI armies materialize for both sides
- player no longer needs a fresh vanilla body by default in the 7b path
- `Mission.ReplaceBotWithPlayer(...)` is now the working possession primitive
- repeat respawn via `Ctrl+T` and repeated side switch now work

### What 7b currently means

Current runtime is now:

- armies spawn first as AI
- player requests a troop
- server finds an eligible materialized agent
- server calls vanilla `ReplaceBotWithPlayer(...)`
- client regains `MainAgent` correctly even on repeated replace-bot respawns

This is a real step toward the target model:

- armies exist in zones
- player takes over an existing battlefield body

### Important fixes now in code

- materialized possession uses `Mission.ReplaceBotWithPlayer(...)`
- target materialized agents are assigned to a valid formation before possession
- client stale `Agent.Main` is released after `Ctrl+T`
- client can restore `MainAgent` from `MissionPeer.ControlledAgent` after repeated replace-bot possession
- reset now clears:
  - `SetAgentPeer(..., null)` path
  - `SetAgentOwningMissionPeer(..., null)` path
  - `SetAgentIsPlayer(..., false)` path
  - formation player-state flags
  - `ControlledFormation`
  - bot counters

### Known issue

There is still a residual old-body bug after repeated possession/reset cycles:

- sometimes a previously controlled body becomes `half-alive` after being hit
- sometimes red damage feedback still flashes when hitting an old body
- behavior is better than before, but not fully clean

This is now considered a `known issue`, not a blocker for continued work.

Reason:

- core 7b loop already works
- the remaining bug appears to be in the reused old-agent reset tail, not in the possession architecture itself

### Battle-flow follow-up started

`CanStartBattle` / `Ctrl+B` readiness is now tightened to use a real authoritative condition instead of just phase text:

- armies must be materialized
- at least one peer must be assigned
- all assigned peers must currently control an active agent
- phase must be `PreBattleHold`

This keeps battle start semantics aligned with the actual possession/runtime state.

### Recommended next step

Do not spend more time right now on the old-body bug unless it blocks a new feature.

Next work should continue from here:

1. use current 7b loop as the working possession spike
2. continue battle-flow / pre-battle behavior on top of it
3. return to old-body cleanup later as a bounded polish/debug task

