## Goal

Finish variant 2 by replacing the current hybrid symptom-repair approach with a
safer server-first exact-transfer core for strict exact heroes.

The immediate target is not "all units". The immediate target is:

- main hero
- lords
- other exact personal hero entries

especially the failing remote mounted hero path on clients.

## Refactor Principle

The next phase must stop treating post-spawn client repair as the main path.

Repair/fallback code may remain, but only as:

- diagnostics
- crash guards
- last-resort temporary recovery

The primary path must become an explicit transfer state machine with stage
invariants.

## Current Problem To Solve First

The most important broken path is:

server exact mounted hero is valid ->
client receives `CreateAgent` for rider with mount index ->
client fails native materialization during or before normal mount linkage ->
later overlay code treats rider as if materialization had succeeded ->
remote hero appears as infantry or disappears ->
command/control observes wrong semantics ->
later lifecycle churn crashes the client.

## Target Architecture

### Transfer stages

For strict exact heroes, the runtime should model these stages explicitly:

1. `EntryResolved`
2. `ClassResolved`
3. `PreSpawnInjected`
4. `ClientCreateAgentObserved`
5. `RiderMaterialized`
6. `MountMaterialized`
7. `MountLinkVerified`
8. `PeerBound`
9. `ExactVisualApplied`
10. `CommanderControlEnabled`
11. `DeathCleanupComplete`

No later stage should be marked complete if an earlier required stage is still
missing.

### Hard invariants

For mounted strict exact heroes:

- `ExactVisualApplied` is illegal while `MountLinkVerified` is false.
- `CommanderControlEnabled` is illegal while hero identity is unresolved.
- a remote hero may not participate in formation-selection semantics while its
  personal hero transfer state is incomplete.
- a queued refresh is never equivalent to applied state.
- death-time cleanup must clear both rider and mount transfer state together.

## Proposed Refactor Work Packages

### Work package 1: Introduce a dedicated transfer-state model

Create one explicit runtime state object for strict exact hero transfer.

Suggested contents:

- `EntryId`
- `AgentIndex`
- `ExpectedMountAgentIndex`
- `ObservedCreateAgent`
- `ObservedSetAgentPeer`
- `ObservedSynchronizeEquipment`
- `RiderMaterialized`
- `MountMaterialized`
- `MountLinkVerified`
- `ExactVisualApplied`
- `CommanderControlEnabled`
- timestamps / retries / failure reason

This state must become the source of truth for exact hero transfer progress.

Current scattered caches should become implementation details behind this model,
not peer-level truth on their own.

### Work package 2: Split payload observation from materialization success

Right now we frequently know mount payload data before we know whether the mount
really exists locally.

That distinction must become explicit:

- payload observed
- native object materialized

Do not let payload knowledge imply success.

### Work package 3: Make `CreateAgent` the primary mount-contract checkpoint

The current logs show that the real break happens at or before client
`HandleServerEventCreateAgent`.

That means future logic should branch from this fact:

- if mounted exact hero reaches `CreateAgent` but local mount is not materialized,
  transition to `MaterializationFailed`
- do not allow later stages to masquerade as success

This is the right place to decide whether:

- we can recover safely
- or we must degrade the hero path in a controlled way

### Work package 4: Gate exact finalize on stage completion, not heuristics

`TryFinalizeClientExactCampaignVisualForAgent(...)` should not decide from a mix
of `SpawnEquipment`, `MountAgent`, pending queue state, and local observations.

Instead:

- it should consult the explicit transfer state
- for mounted strict exact heroes it should refuse finalization until
  `MountLinkVerified`

### Work package 5: Decouple commander-control enablement from unstable spawn state

The order UI, formation ownership, general promotion, and selection suppression
must depend on transfer-stage readiness.

For strict exact heroes:

- if transfer state is incomplete, commander-control enablement is blocked
- if blocked, the runtime should use a clear degraded state rather than mixing
  troop semantics with commander semantics

This should remove the current symptom where the remote host becomes formation-like
and can be selected together with troops.

### Work package 6: Redesign cleanup around rider+mount as one lifecycle unit

Death, despawn, respawn, and agent-index reuse must clear the transfer state for
the whole mounted pair, not only whichever index was observed first.

That includes:

- rider -> mount mapping
- mount -> rider mapping
- entry binding
- applied flags
- pending refresh state
- commander-control state if tied to that hero

### Work package 7: Reduce runtime diagnostics to invariant-based logs

The current diagnostics are useful but too fragmented.

The next iteration should log stage transitions, for example:

- `StrictHeroTransfer Stage=ClientCreateAgentObserved`
- `StrictHeroTransfer Stage=MountMaterialized`
- `StrictHeroTransfer Stage=MountLinkVerified`
- `StrictHeroTransfer Stage=Blocked Reason=CreateAgentExceptionBeforeMountMaterialization`

This gives far better signal per run than many low-level logs that still require
manual reconstruction.

## Recommended Implementation Sequence

### Phase A: Architecture scaffold

1. add the transfer-state runtime object
2. route current rider/mount payload tracking into it
3. add stage transition logging
4. do not change behavior yet except where needed for consistency

Expected result:

we can describe every strict exact hero by stage instead of by scattered logs.

### Phase B: Remote mounted hero gating

1. make remote mounted hero finalization depend on transfer state
2. block `ExactVisualApplied` until mount link is verified
3. block commander-control enablement while transfer incomplete
4. keep existing repair code only as optional transition attempts

Expected result:

the client should stop "pretending success" for broken remote mounted hero spawn.

### Phase C: Controlled degradation policy

If remote mounted hero materialization still fails at native `CreateAgent`, add one
explicit degraded state rather than many implicit half-states.

For example:

- `StrictHeroTransfer State=MaterializationFailed`
- remote commander is not exposed to commander-control path
- visual overlay is not marked applied
- later death/health guards use this state directly

This is safer than partial success.

### Phase D: True server-first completion

Once the strict exact hero state machine is stable:

1. reduce dependency on client visual recovery
2. confirm mounted heroes stabilize across several death/respawn cycles
3. then extend the same model to safer troop subsets

## Why This Is Safer

This path is safer because it replaces:

- hidden assumptions
- overlapping caches
- post-hoc repair
- queued-vs-applied ambiguity

with:

- explicit stage progression
- explicit blocking conditions
- explicit degraded states
- one place to reason about correctness

## What Not To Do Next

Do not spend many more runs on small local fixes such as:

- one more mount refresh tweak
- one more wield guard
- one more delayed retry
- one more order UI suppression change

unless the change is directly part of the state-machine refactor above.

That pattern already gave diminishing returns.

## Definition Of Success For This Refactor Phase

This phase is successful when strict exact hero transfer satisfies all of:

- remote mounted hero on client is either fully mounted and visible or explicitly
  marked degraded, never half-materialized
- no remote exact hero reaches `ExactVisualApplied` without verified mount link
- no commander-control path activates on an unresolved hero transfer
- repeated death/respawn cycles do not crash the client
- logs describe failures by stage, not by forensic reconstruction

## Deliverables

1. transfer-state core
2. invariant-based logs
3. remote mounted hero gating
4. commander-control gating on transfer readiness
5. cleanup and reuse rules for rider+mount lifecycle

Only after these are stable should the project expand the same model toward full
1:1 transfer for ordinary troops.
