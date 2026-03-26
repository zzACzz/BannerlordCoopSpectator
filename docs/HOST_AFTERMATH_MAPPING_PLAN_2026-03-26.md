# Host Aftermath Mapping Plan

Date: 2026-03-26
Project: `BannerlordCoopSpectator3`
Focus: `host-side mapping / recompute / approximation strategy`

Related doc:

- `docs/CAMPAIGN_AFTERMATH_AND_DEDICATED_DATA_MAP_2026-03-26.md`

## 1. Purpose

This document defines the intended host-side strategy for each battle aftermath bucket:

- what must come from dedicated as authoritative runtime output
- what should be recomputed on host using vanilla campaign models
- what can be approximated
- what is currently blocked

This is the explicit planning layer between:

- vanilla aftermath model
- current dedicated data availability
- future writeback implementation

## 2. Design rule

Use dedicated as the source of truth for battle-state deltas.

Use host campaign code as the source of truth for campaign aftermath semantics.

In practice:

- dedicated should answer:
  - who spawned
  - who survived
  - who died
  - who was knocked unconscious
  - who routed
  - who took damage
  - who won

- host should answer:
  - how vanilla converts those outcomes into campaign rewards and aftermath
  - how vanilla distributes loot/prisoners/gold/rewards
  - how vanilla applies campaign-side consequences

## 3. Bucket-by-bucket host mapping

## 3.1 Winner / loser

### Goal

Apply final battle winner to campaign encounter resolution.

### Dedicated authoritative input

- `battle_result.WinnerSide`

### Host strategy

- keep current direct winner writeback
- keep encounter result bridge / mission exit bridge logic

### Status

- already working

### Risk

- low

## 3.2 Troop casualties / wounded / routed

### Goal

Make host party rosters match mission outcome.

### Dedicated authoritative input

- per-entry:
  - `SnapshotCount`
  - `SnapshotWoundedCount`
  - `KilledCount`
  - `UnconsciousCount`
  - `RoutedCount`
  - `OtherRemovedCount`
  - `RemovedCount`

### Host strategy

- aggregate by `(partyId + heroId/originalCharacterId/characterId)`
- resolve target `TroopRoster` entry on host
- set:
  - desired total count
  - desired wounded count
- keep special hero handling outside regular troop count path

### Status

- already working for core casualty transfer

### Risk

- low for normal battles
- medium if future multi-round continuation is introduced

## 3.3 Hero / companion wound state

### Goal

Reflect knockout/wound outcomes for heroes and companions.

### Dedicated authoritative input

- per-entry:
  - `IsHero`
  - `KilledCount`
  - `UnconsciousCount`
  - `OtherRemovedCount`

### Host strategy

- if hero entry removed in wound-like way, call host wound path
- do not route hero state through regular troop count writeback

### Status

- already working

### Risk

- low

## 3.4 Hero / companion current HP

### Goal

Carry remaining health loss for surviving heroes back into campaign.

### Dedicated authoritative input

- per-entry:
  - `DamageTaken`
- mission-end reconciliation of observed damage taken

### Host strategy

- if hero survived and was not wounded:
  - reduce current HP by aggregated damage taken
  - clamp to `[1, maxHp]`

### Status

- already working

### Risk

- medium because damage is aggregate-only, not exact vanilla HP event replay

## 3.5 Troop XP

### Goal

Award battle XP to regular troops in a way close enough to vanilla.

### Dedicated authoritative input

Preferred:

- `CombatEvents`

Fallback:

- enemy casualty pool
- per-entry participation weights from snapshot counts

### Host strategy

Primary path:

- for each combat event where attacker party is main party:
  - resolve attacker troop
  - resolve victim troop
  - call `CombatXpModel.GetXpFromHit(...)`
  - apply troop XP to roster entry

Fallback path:

- for each enemy casualty bucket:
  - resolve victim troop type
  - estimate fatal hit XP from victim base HP
  - distribute weighted XP across main-party participants using snapshot participation weights

### Why host-side, not dedicated-side

- vanilla XP uses campaign `CombatXpModel`
- vanilla troop XP commit then passes through campaign training/shared XP systems
- dedicated MP mission is weak at reliable hit capture

### Current status

- partial
- primary path exists but combat events are unreliable
- fallback path exists but not yet validated live

### Recommended next implementation move

- validate fallback path in live runs before trying to perfect combat-event fidelity

### Risk

- high for exactness
- medium for acceptable gameplay progression

## 3.6 Hero / companion skill XP

### Goal

Award battle skill XP to heroes/companions.

### Dedicated authoritative input

Preferred:

- `CombatEvents`
- `WeaponSkillHint`

Fallback:

- casualty-pool approximation plus best-fit combat skill selection

### Host strategy

Primary path:

- compute `xpFromHit` with `CombatXpModel.GetXpFromHit(...)`
- apply to hero with skill hint

Fallback path:

- estimate per-casualty XP
- assign to best combat skill for that hero entry

### Current status

- partial

### Recommended next move

- keep this behind troop XP validation
- accept approximation before exact hit fidelity

### Risk

- high for skill attribution fidelity

## 3.7 Renown

### Goal

Apply battle renown in vanilla-like way.

### Dedicated authoritative input

- winner side
- side/party composition
- party contribution proxy if available from snapshot

### Host recompute inputs

- current campaign `MapEvent`
- party roles on attacker/defender side
- `BattleRewardModel.CalculateRenownGain(...)`
- contribution shares

### Host strategy

Do not serialize renown from dedicated.

Instead:

1. restore final casualty state on host
2. identify winner / defeated sides
3. reconstruct per-party contribution shares
4. call host reward model to compute renown
5. apply through campaign actions or equivalent host-safe path

### Blocking issue

Current dedicated runtime does not produce vanilla-equivalent contribution totals.

### Recommendation

Short term:

- use snapshot `ContributionToBattle` when present as initial contribution weight
- if needed, blend with battle participation / surviving share / spawn counts

Long term:

- expose or reconstruct better contribution weights explicitly

### Risk

- medium

## 3.8 Influence

### Goal

Apply kingdom influence gains for battle.

### Dedicated authoritative input

- same as renown

### Host strategy

- same recompute pattern as renown
- compute via host `BattleRewardModel.CalculateInfluenceGain(...)`

### Recommendation

- implement together with renown in one host reward pass

### Risk

- medium

## 3.9 Morale reward / morale aftermath

### Goal

Apply post-battle morale changes to parties in vanilla-like way.

### Dedicated authoritative input

- winner side
- snapshot side/party morale only as context, not final aftermath result

### Host strategy

- recompute morale reward using host `BattleRewardModel.CalculateMoraleGainVictory(...)`
- apply defeat morale separately where vanilla would

### Important note

Snapshot morale values are useful as pre-battle context, not as final aftermath output.

### Risk

- medium

## 3.10 Gold lost / plundered gold

### Goal

Apply post-battle gold transfer.

### Dedicated authoritative input

- winner / defeated side only

### Host recompute inputs

- defeated parties on host
- host `BattleRewardModel.CalculatePlunderedGoldAmountFromDefeatedParty(...)`
- host winner share logic

### Host strategy

- recompute on host entirely
- do not try to serialize final gold values from dedicated

### Risk

- low to medium

## 3.11 Looted items from defeated party inventories

### Goal

Match vanilla item loot distribution as closely as feasible.

### Dedicated authoritative input

- none required beyond winner / defeated sides if host campaign parties still hold their campaign inventories

### Host strategy

- recompute from defeated host party item rosters after battle resolution
- use host reward-model distribution logic or equivalent reproduction

### Important note

This is campaign-inventory aftermath, not mission runtime output.

### Current status

- not implemented

### Risk

- medium

## 3.12 Loot from casualties

### Goal

Produce casualty-derived item loot.

### Dedicated authoritative input

- defeated casualty rosters by troop type

### Host strategy

- use casualty buckets already written back or preserved from result
- compute casualty loot from defeated troop types using host reward model assumptions

### Current blocker

- no host-side dedicated implementation yet

### Risk

- medium

## 3.13 Prisoner transfer from defeated survivors

### Goal

Transfer defeated wounded/surrendered survivors into winner prisoner rosters.

### Dedicated authoritative input

- defeated final member outcomes:
  - wounded
  - surrendered equivalents if supported
  - remaining defeated members

### Host strategy

- after casualty writeback, run a host-side defeated-survivor redistribution phase
- mimic vanilla logic:
  - wounded defeated troops become capturable
  - surrender/remaining defeated troops may become capturable
  - heroes become prisoners or fugitives

### Current blocker

- current coop result does not model surrender / post-battle survivor disposition explicitly

### Recommendation

- phase 1:
  - implement only wounded defeated troop capture
- phase 2:
  - implement healthy surrendered survivor capture
- phase 3:
  - implement hero capture/fugitive logic

### Risk

- high

## 3.14 Released / rescued prisoners from defeated prison rosters

### Goal

Transfer prisoners held by defeated parties back to winners or released state.

### Dedicated authoritative input

- none required if host campaign defeated party prison rosters are intact

### Host strategy

- operate entirely on host from current defeated party prison rosters
- redistribute according to vanilla-like rules

### Recommendation

- treat as separate subsystem from defeated-member capture

### Risk

- medium

## 3.15 Relation effects

### Goal

Preserve major post-battle relation changes.

### Dedicated authoritative input

- usually none beyond winner/loser and involved parties

### Host strategy

- recompute only high-signal relation effects first:
  - helped heroes
  - captured hero flow
  - simple perk-based winner relation effects

### Recommendation

- do not block broader aftermath implementation on full relation parity

### Risk

- high for full parity
- low for staged partial parity

## 3.16 Full battle continuation semantics

### Goal

Support cases where one mission does not fully resolve campaign battle.

### Dedicated authoritative input

- explicit continuation flags would be needed:
  - retreat
  - pullback
  - unresolved remaining forces

### Host strategy

- defer

### Reason

- current project already works best in resolved battle flow
- continuation adds semantic complexity across encounter/menu/map-event state

### Risk

- very high

## 4. Recommended ownership split

## 4.1 Dedicated owns

- authoritative battle completion
- winner side
- entry identity
- casualties by entry
- hero wound/HP outcome inputs
- damage taken aggregates
- best-effort combat events

## 4.2 Host owns

- roster writeback
- hero wound/HP application
- XP application
- reward-model recomputation
- gold / renown / influence / morale application
- prisoner / loot aftermath logic
- relation aftermath

## 4.3 Approximation zone

Approximation is acceptable for:

- troop XP fallback
- hero skill XP fallback
- contribution-share reconstruction
- first-pass prisoner allocation

Approximation is not acceptable for:

- winner side
- casualty totals
- hero wound state
- hero current HP loss

## 5. Recommended implementation sequence

## Phase 1: stabilize progression

1. Validate fallback troop XP in live runs
2. Validate fallback hero/companion XP in live runs
3. Add structured audit logs for:
   - XP applied
   - casualty source
   - fallback distribution weights

## Phase 2: host reward recompute

4. Implement one host reward pass for:
   - renown
   - influence
   - morale change
   - gold lost / plundered

5. Keep this pass explicit and isolated from casualty writeback

## Phase 3: aftermath transfer

6. Implement defeated survivor capture
7. Implement rescued prisoner transfer
8. Implement casualty/item loot recompute

## Phase 4: hero/post-battle UX parity

9. Implement captured hero flow
10. Implement free/capture prisoner hero flow
11. Implement helped-hero relation flow if needed

## 6. Immediate next coding target

The next coding target should not be "more combat callback debugging".

It should be:

`host reward recompute scaffold`

Specifically:

1. create a host-side aftermath pass boundary after casualty/HP writeback
2. define an internal DTO for recomputed host rewards:
   - renown
   - influence
   - morale
   - gold
3. feed it from:
   - host `MapEvent`
   - current battle_result winner/casualty state
   - snapshot party contribution metadata

This gives forward progress even if combat-event fidelity remains weak.

## 7. What remains explicitly blocked by missing dedicated data

Still blocked or weak until dedicated exposes more:

- exact hero combat skill attribution
- exact troop XP parity
- exact battle contribution share parity
- exact retreat/pullback continuation semantics
- exact post-battle prisoner disposition for unresolved survivors

## 8. Implementation posture

For the next iterations, prefer:

- explicit host recompute
- isolated aftermath passes
- audit-friendly logs
- approximation with declared limits

Avoid:

- trying to reproduce all vanilla aftermath by growing `battle_result` into a full campaign DTO
- blocking reward/loot/prisoner work on perfect combat-event capture

