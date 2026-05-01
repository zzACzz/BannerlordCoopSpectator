## Scope

This audit covers the exact transfer pipeline for campaign hero entries that become
multiplayer-controlled agents in battle scenes, with focus on the failing remote
mounted hero path observed on the client.

This is not a full audit of the whole mod. It is a focused audit of the transfer
contract:

1. Snapshot entry state
2. Multiplayer class resolution
3. Server pre-spawn exact equipment injection
4. Native mission spawn
5. Client network materialization
6. Peer binding, mount linkage, exact visual finalize
7. Replace-bot, commander control, death, respawn

## Current Intended Architecture

### 1. Entry state is authoritative

`BattleSnapshotRuntimeState` owns the campaign-derived `RosterEntryState`.
That state is supposed to be the single gameplay truth for:

- character id
- exact equipment
- body and hero identity
- mount and harness
- exact hero flags and perks

### 2. Hero class resolution maps campaign identity to MP native classes

`Infrastructure/CampaignMultiplayerHeroClassResolver.cs` maps a campaign-origin
character into a native MP class so the runtime can pass through the multiplayer
spawn contract.

Important current detail:

- already-MP hero ids such as `mp_light_cavalry_battania_hero` are now tried as
  direct candidates first
- surrogate troop/hero templates are only fallback candidates

### 3. Server pre-spawn injection is the target architecture for strict exact heroes

`Patches/ExactCampaignPreSpawnLoadoutPatch.cs` patches `Mission.SpawnAgent`.

For entries that satisfy the strict exact personal hero contract, the server injects:

- exact weapons
- exact visual slots that are considered safe
- body properties

For bulk AI troops, native template equipment is intentionally kept in place.

This means the architecture already distinguishes between:

- strict exact heroes: intended server-first path
- bulk troops: intentionally degraded native-template path

### 4. Client network path is still a hybrid contract

`Patches/BattleMapSpawnHandoffPatch.cs` hooks:

- `HandleServerEventCreateAgent`
- `HandleServerEventSetAgentPeer`
- `HandleServerEventSynchronizeAgentEquipment`
- `HandleServerEventSetAgentHealth`
- `HandleServerEventSetWieldedItemIndex`
- formation assignment and commander-control handoff

`Mission/CoopMissionBehaviors.cs` then tries to resolve the exact entry id and
apply or queue client exact visual overlay.

This layer currently does two jobs at once:

1. recovery from native MP spawn mismatch
2. the practical mechanism that completes hero exact materialization on clients

That is the core architectural tension.

### 5. Commander possession and formation control are coupled to agent identity

`Mission/CoopMissionBehaviors.cs` uses `ReplaceBotWithPlayer`, formation ownership
normalization, controlled bot counts, and delayed general-control promotion.

This means that if the player-facing hero agent is mis-materialized, command/control
logic will also observe a bad identity and can treat the wrong thing as a normal
formation unit.

This matches the observed symptom where the host appears as infantry and gets
selected together with formations.

## What The Logs Prove

Recent runs show a stable asymmetry:

1. Server/host side usually has a valid mounted pair for the host hero.
2. The failing local client sees `CreateAgent` for remote hero rider `222` with
   mount `223`.
3. The same local client throws `Exception in handler of CreateAgent`.
4. After that, the local client still receives later network messages for the rider.
5. The local client often reaches `SetAgentPeer` and exact visual finalize with
   rider data, but without a live `MountAgent`.
6. The result is:
   - remote host appears as infantry or partially materialized
   - later disappears
   - command/selection logic observes wrong agent semantics
   - crash occurs later after enough lifecycle churn

This means the main failing point is not a late visual mismatch. The main failing
point is earlier:

the local client does not reliably complete native materialization of the remote
mounted commander path.

## Structural Problems In The Current Design

### A. "Finalize" currently means two different things

The current client finalize path mixes:

- "queued for later"
- "overlay actually applied"

That makes progress logs look better than the runtime really is and encourages
symptom-driven interpretation.

### B. State is scattered across many partial caches

Mounted hero state is distributed across:

- tracked rider -> mount mappings
- payload rider -> mount mappings
- entry id caches
- pending overlay queues
- applied overlay flags
- materialized army caches

This creates too many ways to have "almost correct" state after partial failure.

### C. Post-spawn repair became a primary mechanism

Client overlay, mount visual refresh, manual fallback, live wield refresh, and
death guards were all useful for diagnosis and stabilization.

But together they became a second architecture layered on top of the intended
server-first architecture.

That is acceptable as temporary instrumentation. It is not a reliable long-term
core.

### D. Command/control is too close to unstable spawn identity

Formation ownership, selected formations, followed agent, controlled agent, and
general-control promotion still sit close to the same unstable hero materialization
path.

That is why a bad hero spawn can leak into order UI behavior instead of staying
isolated inside visuals.

### E. We do not have an explicit stage machine with invariants

The transfer path behaves like a state machine, but the code does not model it as
one. That is why many fixes were added as local guards instead of stage-checked
transitions.

## Self-Critique Of The Current Implementation Strategy

### What was correct

The diagnostic and stabilization work was not wasted.

It gave us:

- reproducible logs
- narrow failing agent ids
- proof that server and client do not fail in the same place
- proof that the root issue is the remote mounted hero path
- proof that many post-death crashes are downstream, not primary

Without that work, a large architecture refactor would still be blind.

### What was incorrect

The implementation strategy stayed in symptom-repair mode for too long.

I kept trying to improve:

- mount visual repair
- live wield refresh
- death guards
- stale cache cleanup
- selection/control guards

before forcing a hard architectural checkpoint.

That was reasonable early, but after repeated runs showed the same core
`CreateAgent -> remote mounted host hero -> missing mount` asymmetry, I should have
pivoted earlier.

### The most important mistake

I overestimated how much of the problem still lived in post-spawn visual recovery.

The logs now show the opposite:

- by the time overlay code runs, the local client may already have lost the mount
  materialization contract
- a repair layer cannot reliably restore a native object lifecycle that did not
  complete cleanly

### Secondary mistake

I allowed "queued/deferred/applied" signals to remain too ambiguous for too long.

That made some iterations look like progress even when the local client still
ended up with the same broken mounted-host outcome.

## Critique Of The User's Diagnosis

The user is mostly right.

It is fair to say that many runs taught us how the system actually behaves rather
than moving the feature to completion.

It is also fair to say that this reveals an incomplete mental model of the transfer
contract.

The user is only slightly wrong if this is interpreted as "all recent work was a
mistake". That would be too strong.

The recent work was necessary to isolate the contract boundary. The real issue is
that the strategy was not pivoted soon enough after that boundary became clear.

## Recommended Safe Path Forward

Do not continue with another long sequence of local guard fixes first.

The safer path is now:

1. write an explicit stage model for exact hero transfer
2. define invariants for each stage
3. redesign the remote mounted hero client path around those invariants
4. keep current repair logic only as diagnostics and guard rails

### Required stage model

For strict exact heroes, the pipeline should be modeled as:

1. entry resolved
2. class resolved
3. pre-spawn exact loadout injected on server
4. native rider materialized on client
5. native mount materialized on client
6. rider <-> mount link verified
7. peer bound
8. exact visual finalize allowed
9. commander control allowed
10. death cleanup complete

Any stage that fails should block later stages, not pretend success.

### Required invariants

Examples:

- mounted exact hero may not enter "exact visual applied" while mount link is absent
- commander-control promotion may not happen while hero identity is unresolved
- remote mounted hero may not be treated as a formation-selectable troop while its
  personal hero contract is incomplete
- queued refresh may not be logged as equivalent to applied refresh

### Required refactor target

The next implementation phase should focus on a narrower and more architectural
change:

make remote mounted hero spawn on the client a first-class staged contract,
instead of trying to infer a correct final state from whatever survives after the
native handler fails.

## Bottom Line

Yes: a broader architecture audit of this transfer pipeline is the correct move now.

No: continuing with many more small reactive fixes before that audit would not be
the safest path.

The current state of the project is good enough to justify this pivot because the
core failing boundary is now known.
