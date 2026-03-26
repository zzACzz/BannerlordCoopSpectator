# Campaign Aftermath And Dedicated Data Map

Date: 2026-03-26
Project: `BannerlordCoopSpectator3`
Focus: `campaign result / casualty / xp / writeback`

## 1. Executive summary

Vanilla Bannerlord does not push battle aftermath into campaign through one large "battle result" DTO.

The actual flow is:

1. `PlayerEncounter` moves the encounter state machine to `ApplyResults`.
2. `PlayerEncounter.DoApplyMapEventResults()` calls `MapEvent.CalculateAndCommitMapEventResults()`.
3. `MapEvent` distributes loot/prisoners/casualties and commits rewards.
4. `MapEventSide` / `MapEventParty` apply XP, renown, influence, morale, gold, relation side-effects, and party state changes.
5. `PlayerEncounter` then runs the post-battle UX flow:
   - helped hero conversations
   - captured hero flow
   - free/capture prisoner hero flow
   - troop/prisoner loot screen
   - item loot screen
   - ship loot screen
   - encounter end

For our coop pipeline this means the correct target is not "reconstruct a combat log first".
The correct target is:

`vanilla aftermath datum -> authoritative dedicated source or approximation -> host-side application`

## 2. Vanilla campaign aftermath model

## 2.1 Main entry points

Relevant vanilla entry points found through decompilation:

- `PlayerEncounter.DoApplyMapEventResults()`
- `MapEvent.CalculateAndCommitMapEventResults()`
- `MapEvent.CommitCalculatedMapEventResults()`
- `MapEventSide.CommitXpGains()`
- `MapEventSide.ApplyRenownAndInfluenceChanges()`
- `MapEventSide.ApplyFinalRewardsAndChanges()`

Observed vanilla order:

1. `CampaignEventDispatcher.Instance.OnPlayerBattleEnd(_mapEvent)`
2. `_mapEvent.CalculateAndCommitMapEventResults()`
3. If player won:
   - `DoPlayerVictory()`
   - capture/free hero steps
   - loot party
   - loot inventory
   - loot ships
4. Encounter end/finalize

## 2.2 Core persistent aftermath carrier

The main per-party aftermath carrier is `TaleWorlds.CampaignSystem.MapEvents.MapEventParty`.

Fields/properties that directly matter for writeback:

- `DiedInBattle`
- `WoundedInBattle`
- `RoutedInBattle`
- per-troop XP tracked in the internal flattened roster and committed via `CommitXpGain()`
- `GainedRenown`
- `GainedInfluence`
- `MoraleChange`
- `PlunderedGold`
- `GoldLost`
- `ContributionToBattle`
- `RosterToReceiveLootMembers`
- `RosterToReceiveLootPrisoners`
- `RosterToReceiveLootItems`

This is the most important vanilla conclusion:

Vanilla aftermath is fundamentally party-scoped, not mission-log-scoped.

## 2.3 Vanilla aftermath buckets

### A. Casualties / wounded / routed

Source in vanilla:

- `MapEventParty.DiedInBattle`
- `MapEventParty.WoundedInBattle`
- `MapEventParty.RoutedInBattle`

Commit path:

- party member rosters are already mutated during battle simulation/runtime
- post-battle distribution also processes remaining defeated members

Notes:

- routed survivors are a separate bucket from killed/unconscious
- remaining defeated members may still be converted into prisoners/fugitives after battle

### B. Troop XP

Source in vanilla:

- `MapEventParty.OnTroopScoreHit(...)`
- internal flattened roster accumulates `XpGained`
- `MapEventParty.CommitXpGain()`

Vanilla semantics:

- regular troop hit XP is accumulated during battle
- then battle XP is converted through `PartyTrainingModel.CalculateXpGainFromBattles(...)`
- shared XP may also be generated
- overflow for eligible cases can feed `SkillLevelingManager.OnBattleEnded(...)`

Important conclusion:

Vanilla troop XP is not just "sum of raw damage". It is post-processed through party training/shared XP rules.

### C. Hero / companion skill XP

Source in vanilla:

- `MapEventParty.OnTroopScoreHit(...)`
- for hero attackers it calls `CampaignEventDispatcher.Instance.OnHeroCombatHit(...)`

Observed side effects:

- hero combat hit XP is handled by campaign systems listening to combat-hit events
- perk side-effects can trigger from hero combat hits

Important conclusion:

Hero combat XP is event-driven in vanilla, not only end-of-battle aggregate-driven.

### D. Renown / influence / morale

Source in vanilla:

- `MapEventSide.DistributeRenownAndInfluence(...)`
- `MapEventSide.ApplyRenownAndInfluenceChanges()`
- `MapEventSide.ApplyFinalRewardsAndChanges()`

Per-party results:

- `GainedRenown`
- `GainedInfluence`
- `MoraleChange`

Computation inputs include:

- side renown/influence value
- contribution share
- battle reward model
- perk bonuses
- side strength ratio adjustments

Important conclusion:

These are not derived from combat events directly. They are derived mostly from winner/loser/contribution/reward-model calculations.

### E. Gold

Source in vanilla:

- `MapEvent.CalculateMapEventResults()`
- `LootDefeatedPartyGold(...)`
- `MapEventSide.ApplyFinalRewardsAndChanges()`

Per-party results:

- `GoldLost`
- `PlunderedGold`

Important conclusion:

Gold is also a reward-model / defeated-party property calculation, not a mission combat-log product.

### F. Prisoners / captives / defeated survivors

Source in vanilla:

- `LootDefeatedPartyMembers(...)`
- `LootDefeatedPartyPrisoners(...)`

What happens:

- wounded defeated troops become capturable prisoners
- if defeated side surrendered, healthy remaining troops can also become prisoners
- defeated hero members can become prisoners or fugitives
- existing prisoners on defeated side can be transferred to winners or released

Important conclusion:

This area depends on defeated-side remaining member roster state plus winner allocation probabilities, not on hit logs.

### G. Looted troops / rescued prisoners / looted items / ships

Source in vanilla:

- `LootDefeatedPartyMembers(...)`
- `LootDefeatedPartyPrisoners(...)`
- `LootDefeatedPartyItems(...)`
- `LootDefeatedPartyCasualties(...)`
- `LootDefeatedPartyShips(...)`

Player-facing post-battle UX:

- `DoLootParty()`
- `DoLootInventory()`
- `DoLootShips()`

Important conclusion:

Loot is multi-bucket:

- member loot
- prisoner loot
- item loot
- casualty-generated item loot
- naval ship loot

### H. Relation side-effects

Observed sources:

- `DefaultBattleRewardModel.GetPlayerGainedRelationAmount(...)`
- `PlayerEncounter.DoPlayerVictory()` chooses helped leaders for post-battle relation flow
- `MapEventSide.DistributeRenownAndInfluence(...)` applies some relation changes against villagers/caravans/notables
- `CharacterRelationCampaignBehavior.MapEventEnded(...)` applies perk-based relation bonuses on map-event end

Important conclusion:

Relation outcomes are partly deterministic from battle roles and perk state, not from mission damage logs.

### I. Battle continuation / multi-round / retreat semantics

Source:

- `PlayerEncounter.CheckIfBattleShouldContinueAfterBattleMission()`
- `MapEvent.CheckIfBattleShouldContinueAfterBattleMission(...)`

Inputs:

- `CampaignBattleResult`
- `EnemyRetreated`
- `EnemyPulledBack`
- side survivors after mission

Important conclusion:

The campaign aftermath model explicitly supports "battle not fully resolved yet".
This matters for future multi-round continuation support.

## 3. What dedicated side currently has

## 3.1 Pre-battle snapshot data available on dedicated

Your snapshot/runtime model already carries a large amount of campaign-origin data:

- battle id / type / scene / player side
- side ids and leader party ids
- side morale
- party ids / names / `IsMainParty`
- party modifier data:
  - leader/owner/scout/quartermaster/engineer/surgeon hero ids
  - morale / recent-events morale / morale change
  - contribution to battle
  - leadership/tactics/scouting/steward/engineering/medicine skills
  - perk id lists by role
- per-entry data:
  - entry id / side id / party id
  - character id / original character id / spawn template id
  - troop name / culture
  - hero identity metadata
  - hero body / level / age / gender
  - mounted/ranged/shield/thrown flags
  - combat-related attributes and skills
  - base hit points
  - perk ids
  - combat equipment ids
  - snapshot `Count`
  - snapshot `WoundedCount`

This means:

- party identity is good
- troop identity is good
- hero/companion identity is good
- many reward-model inputs are already present in snapshot form

## 3.2 Dedicated mission runtime data currently available

Current dedicated mission runtime tracks per materialized entry:

- `MaterializedSpawnCount`
- `ActiveCount`
- `RemovedCount`
- `KilledCount`
- `UnconsciousCount`
- `RoutedCount`
- `OtherRemovedCount`
- `ScoreHitCount`
- `HitsTakenCount`
- `FatalHitCount`
- `KillsInflictedCount`
- `UnconsciousInflictedCount`
- `RoutedInflictedCount`
- `DamageDealt`
- `DamageTaken`

Current dedicated mission runtime also tracks:

- agent index -> entry id mapping
- agent index -> side mapping
- per-agent observed damage taken via reconciliation at battle end
- recorded combat events with:
  - attacker entry/party/character ids
  - victim entry/party/character ids
  - weapon skill hint
  - weapon class hint
  - blocked / siege-engine / fatal flags
  - damage
  - hit distance
  - shot difficulty
  - mission time
- winner side resolved from authoritative battle completion logic

## 3.3 Dedicated battle-result payload currently produced

Current `battle_result` already carries:

- battle id / type / map scene / source
- winner side
- player side
- entries with:
  - identity fields
  - snapshot count / wounded count
  - materialized spawn count
  - active / removed / killed / unconscious / routed / other removed
  - hit / fatal / kill counters
  - damage dealt / taken
- combat event list
- dropped combat-event count

## 3.4 Dedicated-side reliability level by bucket

Reliable today:

- winner side
- per-entry casualties
- killed / unconscious / routed / removed totals
- hero/companion identity
- snapshot roster identity
- battle start side/party/entry metadata
- end-of-battle observed damage taken for surviving/removed agents

Partially reliable today:

- score-hit counts
- damage dealt
- combat event stream
- weapon skill hints

Not available today as authoritative dedicated outputs:

- post-battle loot rosters
- post-battle prisoner transfer rosters
- gold lost / plundered gold
- renown / influence / morale rewards
- relation deltas
- fugitive/captured hero disposition
- figurehead/ship loot outcome

## 4. Mapping: vanilla aftermath datum -> dedicated source status

| Vanilla datum | Vanilla carrier | Dedicated source now | Status |
|---|---|---|---|
| Winner side | `MapEvent` | authoritative battle completion + active side counts | strong |
| Casualties killed/unconscious/routed | `MapEventParty` rosters | per-entry materialized runtime + mission reconciliation | strong |
| Hero wound / HP | member roster + hero state | per-entry identity + damage taken + removal outcome | strong for current scope |
| Troop XP | `MapEventParty.CommitXpGain()` | combat events or fallback casualty pool | weak/partial |
| Hero/companion skill XP | `OnHeroCombatHit` event chain | combat events with weapon skill hints | weak/partial |
| Renown | `MapEventParty.GainedRenown` | not captured; could be host-side recomputed from winner/contribution/reward model | host recompute candidate |
| Influence | `MapEventParty.GainedInfluence` | not captured; could be host-side recomputed | host recompute candidate |
| Morale reward | `MapEventParty.MoraleChange` | not captured as aftermath result; snapshot has pre-battle morale only | host recompute candidate |
| Gold lost/plundered | `GoldLost` / `PlunderedGold` | not captured | host recompute candidate |
| Looted items | item rosters | not captured | missing |
| Looted members | member/prisoner rosters | not captured | missing |
| Looted prisoners | member/prisoner rosters | not captured | missing |
| Captured/freed heroes | loot member/prisoner flow | not captured | missing |
| Relation effects | reward model + behaviors | not captured directly | host recompute candidate, but broader scope |
| Battle continuation flags | `CampaignBattleResult` | winner/side exhaustion partially available; retreat/pullback not modeled | partial |

## 5. Strategic conclusions

## 5.1 XP is the only aftermath bucket that truly needs combat-level signal

Most of the other aftermath buckets do not require per-hit event fidelity.

They are driven by:

- winner/loser
- side/party contribution
- defeated-party rosters
- reward models
- perk/model calculations on host campaign side

This means the current instinct to "unlock everything by getting perfect combat events" is only valid for XP and a subset of perk-driven combat side-effects.

## 5.2 Loot / prisoners / gold / renown / influence should probably be host-recomputed

For these buckets, the correct source of truth is likely:

- host campaign `MapEvent` / `PartyBase`
- host-side reward models
- host-side defeated/winner roster state after casualty writeback

Dedicated does not need to emit final values for these if host can recompute vanilla-equivalent outcomes after battle state has been written back.

## 5.3 Dedicated should focus on authoritative battle-state deltas, not full aftermath simulation

The dedicated server is strongest at:

- who spawned
- who survived
- who was killed / knocked out / routed
- who took damage
- who won

It is weak at:

- campaign economy aftermath
- prisoner redistribution
- loot distribution
- relation/reward models

## 5.4 Current XP fallback direction is valid

Because `OnScoreHit` / combat-event extraction is unreliable, fallback XP derived from casualty pools is a reasonable intermediate path.

It will not be vanilla-perfect, but it can unblock:

- troop progression
- hero/companion progression

while broader aftermath buckets are rebuilt using host-side campaign models.

## 6. Recommended next implementation order

## 6.1 Immediate next step

Build a host-side mapping table:

- `vanilla aftermath datum`
- `host can recompute from current campaign state?`
- `requires dedicated authoritative delta?`
- `requires approximation?`

## 6.2 Concrete priority order

1. Keep dedicated authoritative for:
   - winner side
   - casualties
   - wound / HP
   - routed / removed counts

2. Finish host-side XP path:
   - primary: combat-event-based if events exist
   - fallback: casualty-pool approximation

3. After XP, implement host-side recomputation for:
   - renown
   - influence
   - morale change
   - gold lost / plundered gold

4. Only after that, tackle:
   - prisoner transfer
   - rescued prisoners
   - looted troop transfer
   - item loot
   - hero capture/release flow

## 6.3 Things that should not block the next iteration

- perfect hit timeline
- perfect dealt-damage totals
- exact weapon-skill attribution for every hit

Those matter mostly for fidelity of XP and combat-triggered perk side-effects, not for the whole aftermath stack.

## 7. Repo-specific current state cross-check

Current project code already matches the above split reasonably well:

- host writeback in `Campaign/BattleDetector.cs` already applies:
  - troop casualty writeback
  - hero wound writeback
  - hero HP writeback
  - troop XP writeback
  - hero skill XP writeback
- dedicated result generation in `Mission/CoopMissionBehaviors.cs` already captures:
  - roster identity
  - materialized spawn/removal counts
  - per-entry damage/hit aggregates
  - combat-event snapshots
  - winner side

The main mismatch is:

- vanilla uses party-scoped aftermath/reward distribution
- current coop XP logic still expects a combat-event stream to be the central truth

That expectation is too broad for the rest of aftermath and should stay limited to XP-related fidelity work.

