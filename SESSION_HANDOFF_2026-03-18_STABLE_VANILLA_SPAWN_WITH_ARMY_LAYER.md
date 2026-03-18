## Goal of this handoff
Freeze the current stable runtime after the army-materialization spike and record what is proven-good versus what remains experimental.

## Current stable runtime
Working now:
- coop authority still owns:
  - selected side
  - selected troop
  - spawn intent
  - entry-status / menu / hotkeys
- vanilla TDM overlay remains suppressed as presentation
- battlefield armies can be materialized as AI background/context agents
- player spawn works correctly through vanilla TDM / `SpawningBehaviorBase`
- player respawn works correctly through the same vanilla path
- gold is spent correctly on spawn via the vanilla economy path
- spawned agents are live/usable again

Current stable architecture:
- AI armies may exist in the mission
- player-controlled bodies are still created by vanilla spawn
- possession of existing AI agents is not part of the stable runtime

## What was learned from the army-materialization spike
### 1. Materialized armies can exist safely as an AI layer
The battlefield army spike did prove something useful:
- both attacker and defender agent groups can be materialized at mission start
- they can be placed on valid TDM spawn anchors
- they can remain in the mission as background army context

This is useful for the long-term "armies exist, players enter battle bodies" model.

### 2. Possession-first was not equivalent to vanilla player spawn
When runtime was changed from:
- `vanilla player spawn`
to:
- `take control of an already existing AI agent`

the old "half-alive" problem came back.

Important clues from logs:
- gold was not spent on possession-first spawn
- the player did not go through the full proven vanilla spawn lifecycle
- even after team/culture/troop-index sync was improved, possession-first still produced broken gameplay

Conclusion:
- `SetAgentPeer` / `SetAgentIsPlayer` / `SetAgentOwningMissionPeer` are not enough
- taking control of an existing agent is not currently a valid replacement for vanilla player spawn
- this path must stay experimental until its full lifecycle is understood

### 3. Materialized army visuals are still using mission-safe MP templates
The current army layer is not yet a faithful campaign battle identity layer.

What this means:
- materialized infantry/cavalry can still look like standard MP-safe troops
- this is expected with the current fallback/template model
- do not interpret current materialized visuals as "final campaign troop authenticity"

## Important runtime decisions now encoded
Main file:
- [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs)

Important flags:
- `EnableDirectCoopPlayerSpawnExperiment = false`
- `EnableMaterializedArmyPossessionExperiment = false`

Meaning:
- direct/manual `Mission.SpawnAgent(...)` player spawn remains retired
- possession-first of materialized army agents remains disabled in stable runtime
- stable path is:
  - coop authority prepares vanilla-valid side/culture/class state
  - vanilla creates the real player agent
  - coop keeps AI armies as a separate context layer

## What the latest logs confirmed
Client / dedicated logs now show the expected stable path:
- pending spawn request queued
- gold floor raised before visuals finalize
- pending vanilla visuals finalized
- `CreateAgent`
- `SyncGoldsForSkirmish`
- control finalize diagnostics with correct live state

Most important negative confirmation:
- no `possessed materialized army agent` in the stable successful run

## Do not regress into these paths
Do not return to:
- direct/manual player `Mission.SpawnAgent(...)` as runtime path
- possession-first existing-agent control as the default spawn path
- trying to "fix" possession with one more ownership/control flag

If possession is revisited later, it should be a separate spike with fresh lifecycle analysis, not a quick patch on top of the stable spawn flow.

## Best next steps
### 1. Preserve the stable runtime
Treat this as the current baseline:
- AI armies materialized
- player spawn/respawn via vanilla

Do not mix another possession experiment into the stable branch without a flag.

### 2. Decide the next research direction explicitly
There are now two valid branches:

Branch A: stabilize the current product path
- improve army materialization fidelity
- improve deployment / pre-battle hold
- keep player spawn vanilla

Branch B: research true "control existing army agent"
- map full vanilla ownership/control/economy lifecycle
- understand whether existing-agent possession can ever be made equivalent
- only then re-enable possession behind a flag

### 3. Prefer battle-system work over spawn-core churn
Spawn/respawn for the player is finally in a proven-good state.

Higher-value next work is now likely:
- pre-battle hold with real two-army behavior
- deployment semantics
- result/writeback groundwork
- better snapshot-driven army materialization

## Short summary
Current stable architecture is:
1. coop authority selects side/troop/spawn intent
2. AI armies may be materialized as battlefield context
3. vanilla TDM spawn still creates the actual player-controlled agent
4. respawn follows the same vanilla path

That is the baseline to preserve.
