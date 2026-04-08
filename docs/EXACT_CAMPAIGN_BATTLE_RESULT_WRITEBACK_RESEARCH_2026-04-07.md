## Scope

This note maps the native campaign battle-result pipeline against the current coop exact-battle pipeline.

Focus:

- hero combat skill growth from battle hits
- troop XP from hits and kills
- battle reward writeback back to campaign
- perk and modifier parity inside the multiplayer battle runtime
- practical implementation options for "return all battle results to campaign"

## Native campaign contract

### Mission-time hit flow

Singleplayer campaign battle uses `SandBox.Missions.MissionLogics.BattleAgentLogic`.

- `BattleAgentLogic.OnAgentHit(...)` forwards every hit into `affectorAgent.Origin.OnScoreHit(...)`.
  - Source: native decompile of `SandBox.Missions.MissionLogics.BattleAgentLogic`
- `PartyGroupAgentOrigin.OnScoreHit(...)` forwards into the real campaign troop supplier.
  - Source: [.codex_tmp/PartyGroupAgentOrigin.cs](../.codex_tmp/PartyGroupAgentOrigin.cs):128-131
- `MapEventSide.OnTroopScoreHit(...)` delegates to `MapEventParty.OnTroopScoreHit(...)`.
  - Source: native decompile of `TaleWorlds.CampaignSystem.MapEvents.MapEventSide`:857-860
- `MapEventParty.OnTroopScoreHit(...)` does three important things:
  - computes XP through `Campaign.Current.Models.CombatXpModel.GetXpFromHit(...)`
  - for non-heroes, accumulates troop XP in the flattened battle roster
  - for heroes, dispatches `CampaignEventDispatcher.Instance.OnHeroCombatHit(...)`
  - increments `ContributionToBattle` by the rounded XP gained
  - Source: native decompile of `TaleWorlds.CampaignSystem.MapEvents.MapEventParty`

### Native hero skill growth

Singleplayer campaign battle also runs hero skill growth directly inside mission logic.

- `BattleAgentLogic.OnScoreHit(...)` calls `EnemyHitReward(...)`.
- `EnemyHitReward(...)` calls `SkillLevelingManager.OnCombatHit(...)`.
- `DefaultSkillLevelingManager.OnCombatHit(...)` applies:
  - weapon skill XP from `CombatXpModel.GetXpFromHit(...)`
  - shot-difficulty XP bonus through `CombatXpModel.GetXpMultiplierFromShotDifficulty(...)`
  - extra `Riding` XP for mounted attackers
  - extra `Athletics` XP for foot attackers
  - commander `Tactics` XP
  - `Roguery` XP for sneak attacks
  - captain-radius and party perk effects through `CombatXpModel`
  - Sources:
    - native decompile of `SandBox.Missions.MissionLogics.BattleAgentLogic`:109-233
    - native decompile of `TaleWorlds.CampaignSystem.CharacterDevelopment.DefaultSkillLevelingManager`
    - native decompile of `TaleWorlds.CampaignSystem.GameComponents.DefaultCombatXpModel`

### Native battle-end commit flow

When the battle is finalized, campaign commits the accumulated results in this order:

- `MapEvent.CommitCalculatedMapEventResults()`
  - `CommitXPGains()`
  - `ApplyRenownAndInfluenceChanges()`
  - `ApplyRewardsAndChanges()`
  - Source: native decompile of `TaleWorlds.CampaignSystem.MapEvents.MapEvent`:1922-1926

For XP:

- `MapEventSide.CommitXpGains()` calls `MapEventParty.CommitXpGain()` for every party on the side.
  - Source: native decompile of `TaleWorlds.CampaignSystem.MapEvents.MapEventSide`:392-397
- `MapEventParty.CommitXpGain()`:
  - converts accumulated roster XP into `Party.MemberRoster.AddXpToTroop(...)`
  - generates shared XP where applicable
  - converts overflow XP into leader-skill growth through `SkillLevelingManager.OnBattleEnded(...)`
  - Source: native decompile of `TaleWorlds.CampaignSystem.MapEvents.MapEventParty`

For renown/influence/morale/gold:

- `MapEventSide.DistributeRenownAndInfluence(...)` calculates per-party reward shares from `ContributionToBattle`.
  - Source: native decompile of `TaleWorlds.CampaignSystem.MapEvents.MapEventSide`:400-481
- `MapEventSide.ApplyRenownAndInfluenceChanges()` applies:
  - `GainRenownAction.Apply(...)`
  - `GainKingdomInfluenceAction.ApplyForBattle(...)`
  - Source: native decompile of `TaleWorlds.CampaignSystem.MapEvents.MapEventSide`:484-500
- `MapEventSide.ApplyFinalRewardsAndChanges()` applies:
  - morale changes
  - plundered gold and gold loss
  - Source: native decompile of `TaleWorlds.CampaignSystem.MapEvents.MapEventSide`:538-560

### Native reward modifiers

Native battle rewards are not raw contribution math only.

- `DefaultBattleRewardModel.CalculateRenownGain(...)` adds perk and cultural-feat bonuses.
- `DefaultBattleRewardModel.CalculateInfluenceGain(...)` adds perk bonuses.
- `DefaultBattleRewardModel.CalculateMoraleGainVictory(...)` adds perk bonuses.
- Source: native decompile of `TaleWorlds.CampaignSystem.GameComponents.DefaultBattleRewardModel`

## Current coop exact-battle contract

### Mission-time origins are not campaign-backed

Current exact runtime does not execute the native campaign `OnScoreHit` pipeline.

- exact AI troops use `ExactCampaignSnapshotAgentOrigin`
- player-controlled human spawn uses `BasicBattleAgentOrigin`
- both origins have no-op `OnScoreHit(...)`
  - Sources:
    - [Infrastructure/ExactCampaignArmyBootstrap.cs](../Infrastructure/ExactCampaignArmyBootstrap.cs):1958-1964
    - [Mission/CoopMissionBehaviors.cs](../Mission/CoopMissionBehaviors.cs):14626-14635
    - [.codex_tmp/decompiled_mountandblade/TaleWorlds.MountAndBlade/BasicBattleAgentOrigin.cs](../.codex_tmp/decompiled_mountandblade/TaleWorlds.MountAndBlade/BasicBattleAgentOrigin.cs):59-65

This means multiplayer exact battles do not update native campaign progression during the mission itself.

### Current battle-result bridge

Current coop battle runtime records a post-battle snapshot into `battle_result.json`.

- schema:
  - entries contain counts, kills, wounds, routed, `DamageDealt`, `DamageTaken`
  - combat events contain attacker/victim ids, `WeaponSkillHint`, `WeaponClassHint`, `IsFatal`, `Damage`, `HitDistance`, `ShotDifficulty`
  - Source: [Infrastructure/CoopBattleResultBridgeFile.cs](../Infrastructure/CoopBattleResultBridgeFile.cs):13-80
- events are recorded on mission `OnScoreHit(...)`
  - Source: [Mission/CoopMissionBehaviors.cs](../Mission/CoopMissionBehaviors.cs):8435-8538
- synthetic fatal events are also added when needed
  - Source: [Mission/CoopMissionBehaviors.cs](../Mission/CoopMissionBehaviors.cs):8796-8834
- final snapshot is written at battle end
  - Source: [Mission/CoopMissionBehaviors.cs](../Mission/CoopMissionBehaviors.cs):9074-9139

Important limitation:

- combat events are capped at `16384`
- dropped events are counted in `DroppedCombatEventCount`
- current campaign writeback only logs this drop count; it does not compensate for partial event loss
- Sources:
  - [Mission/CoopMissionBehaviors.cs](../Mission/CoopMissionBehaviors.cs):3814,8505-8508,9093
  - [Campaign/BattleDetector.cs](../Campaign/BattleDetector.cs):442-478

### Current campaign writeback

Campaign-side writeback lives in `BattleDetector.ApplyBattleResultWriteback(...)`.

- casualties and hit point writeback are applied for all encounter parties that can be resolved
  - Source: [Campaign/BattleDetector.cs](../Campaign/BattleDetector.cs):542-591,715-905
- combat XP writeback is currently main-party-only
  - Source: [Campaign/BattleDetector.cs](../Campaign/BattleDetector.cs):908-1045
- reward writeback is currently main-party-only
  - Source: [Campaign/BattleDetector.cs](../Campaign/BattleDetector.cs):1193-1548

### What currently transfers

#### Hero skill XP

Implemented only for attackers belonging to `MobileParty.MainParty`.

- per combat event:
  - computes `xpFromHit` with native `CombatXpModel.GetXpFromHit(...)`
  - resolves `WeaponSkillHint`
  - applies `HeroDeveloper.AddSkillXp(...)`
  - Source: [Campaign/BattleDetector.cs](../Campaign/BattleDetector.cs):941-1027

This means:

- main-hero and companions in the main party can receive weapon-skill XP from recorded hits
- allied lords and allied parties outside the main party do not receive this XP

#### Troop XP

Implemented only for troops belonging to `MobileParty.MainParty`.

- per combat event:
  - computes `xpFromHit` with native `CombatXpModel.GetXpFromHit(...)`
  - applies `MemberRoster.AddXpToTroop(...)`
  - Source: [Campaign/BattleDetector.cs](../Campaign/BattleDetector.cs):1030-1043
- if no combat events exist at all, fallback distributes XP from enemy casualties across main-party participants
  - Source: [Campaign/BattleDetector.cs](../Campaign/BattleDetector.cs):1047-1190

This means:

- main-party troops can gain XP from hits and kills
- allied non-main-party troops do not currently gain battle XP through this writeback path

#### Renown, influence, morale, gold

Implemented only for the main party reward target.

- reward projection uses committed `MapEventParty` aftermath if available, otherwise falls back to `BattleRewardModel`
  - Source: [Campaign/BattleDetector.cs](../Campaign/BattleDetector.cs):1202-1368,2144-2174
- reward apply uses:
  - `GainRenownAction.Apply(...)`
  - `GainKingdomInfluenceAction.ApplyForBattle(...)`
  - `RecentEventsMorale += ...`
  - `GiveGoldAction.ApplyBetweenCharacters(...)`
  - Source: [Campaign/BattleDetector.cs](../Campaign/BattleDetector.cs):1438-1548

### What does not currently transfer exactly

#### Native hero combat progression parity is incomplete

Current writeback does not reproduce the full native `SkillLevelingManager.OnCombatHit(...)` contract.

Missing today:

- shot-difficulty XP multiplier
- extra `Athletics` XP
- extra `Riding` XP
- commander `Tactics` XP
- `Roguery` from sneak attack
- mounted-vs-foot movement bonus logic
- "under command" logic used by native mission code
- captain-radius behavior used in native XP computation

Why:

- current battle-result event schema does not carry the full mission-time inputs needed by native `OnCombatHit(...)`
- current campaign writeback does not call `SkillLevelingManager.OnCombatHit(...)`; it directly applies `AddSkillXp(...)`

#### Native troop XP parity is incomplete

Current writeback does not reproduce the full native `MapEventParty.CommitXpGain()` behavior.

Missing today:

- troop XP for allied non-main-party parties
- shared XP generation path
- overflow conversion via `SkillLevelingManager.OnBattleEnded(...)`
- exact per-troop native progression through `UniqueTroopDescriptor`

#### Native contribution parity is incomplete

Native campaign increments `ContributionToBattle` through `MapEventParty.OnTroopScoreHit(...)`.

Current exact runtime:

- snapshots contribution from campaign into battle-start modifiers
- uses that value for reward projection fallback later
- does not update real `MapEventParty.ContributionToBattle` from mission hits during the battle

#### Perks and battle modifiers are approximate, not exact

Current multiplayer battle runtime sends real perk ids and party-role perk ids in the battle snapshot:

- source snapshot build:
  - [Campaign/BattleDetector.cs](../Campaign/BattleDetector.cs):3934-3978,5968-6107
  - [Network/Messages/BattleStartMessage.cs](../Network/Messages/BattleStartMessage.cs):75-153

But mission-side application is a heuristic aggregation layer:

- troop perk ids are collapsed into counts by skill family:
  - melee count
  - ranged count
  - athletics count
  - riding count
  - Source: [Mission/CoopMissionBehaviors.cs](../Mission/CoopMissionBehaviors.cs):8349-8391
- party role perks are collapsed into role counts:
  - party leader
  - army commander
  - captain
  - scout
  - quartermaster
  - engineer
  - surgeon
  - Source: [Mission/CoopMissionBehaviors.cs](../Mission/CoopMissionBehaviors.cs):8365-8379
- actual mission effect application scales `AgentDrivenProperties` with generic formulas such as:
  - `ComputePerkPositiveFactor(...)`
  - `ComputePartySkillPositiveFactor(...)`
  - Source:
    - [Mission/CoopMissionBehaviors.cs](../Mission/CoopMissionBehaviors.cs):9506-9731
    - [Mission/CoopMissionBehaviors.cs](../Mission/CoopMissionBehaviors.cs):10028-10209

Conclusion:

- perks and battle modifiers are not native-exact today
- they are an approximation based on counts and skill buckets, not per-perk native behavior

## Practical conclusions

### Current state by feature

- weapon-skill growth for main-hero and main-party companions: partially implemented
- weapon-skill growth for allied lords in other parties: not implemented
- troop XP from combat for main-party troops: partially implemented
- troop XP from combat for allied non-main-party troops: not implemented
- overflow leadership and roguery gains from troop excess XP: not implemented
- renown growth for the main party leader/clan: implemented
- influence, morale, gold for the main party: implemented
- per-party reward application for all allied parties: not implemented
- exact in-battle perk and modifier parity: not implemented

### Best implementation options

#### Option A: recommended next step

Keep the current battle-result bridge, but upgrade it to native-like writeback.

Do next:

- extend `BattleResultCombatEventSnapshot` with the missing hero-XP inputs needed for `SkillLevelingManager.OnCombatHit(...)`
  - at minimum: attacker mounted state, horse charge, sneak attack, team kill, movement-speed bonus, captain/commander identity, and victim hitpoint ratio or equivalent pre-hit data
- switch hero hit writeback from direct `AddSkillXp(...)` to `SkillLevelingManager.OnCombatHit(...)`
- extend combat XP writeback from main party only to all resolved encounter parties
- after troop XP apply, reproduce overflow behavior with `SkillLevelingManager.OnBattleEnded(...)`
- if `DroppedCombatEventCount > 0`, do not silently treat the result as exact

Why this is the best short-to-medium path:

- it reuses the already working prisoner/writeback style architecture
- it avoids trying to run full campaign battle logic inside multiplayer mission runtime
- it can be validated incrementally with existing writeback audit logs

#### Option B: long-term exact parity

Carry real `UniqueTroopDescriptor` data from campaign snapshot into exact runtime and back.

Goal:

- replay `MapEventSide.OnTroopScoreHit(...)` or `MapEventParty.OnTroopScoreHit(...)` against real campaign battle parties instead of approximating with ids and roster matching

Benefits:

- native troop XP accumulation
- native contribution accumulation
- native battle-end `CommitXpGain()` behavior

Costs:

- larger snapshot contract change
- exact descriptor preservation across spawn, reinforcement, and battle-result serialization
- higher risk and more invasive refactor

#### Option C: minimal patch

If the goal is fastest gameplay value rather than exact parity:

- keep current main-party event-based XP path
- add missing `Athletics`, `Riding`, `Tactics`, and overflow `Leadership`/`Roguery`
- expand writeback to allied player-side parties only

This is faster, but it will still remain an approximation rather than native parity.

## Recommended next implementation target

Recommended order:

1. add full hero-combat event data needed for `SkillLevelingManager.OnCombatHit(...)`
2. switch hero hit progression writeback to native `SkillLevelingManager`
3. generalize combat XP writeback from main-party-only to all resolved encounter parties
4. add overflow `SkillLevelingManager.OnBattleEnded(...)`
5. add explicit audit logs per party for:
   - hero skill XP
   - troop XP
   - overflow XP
   - renown/influence/morale/gold
   - dropped combat events

## Runtime validation hooks already available

`BattleDetector` already logs a writeback summary line with:

- `TroopXp=...`
- `HeroSkillXp=...`
- `HeroRawXp=...`
- `RewardProjection[...] Renown=...`
- `RewardApply[...] Renown=...`
- Source: [Campaign/BattleDetector.cs](../Campaign/BattleDetector.cs):466-525

So after implementation, the next validation run can be log-backed without adding a new log format first.
