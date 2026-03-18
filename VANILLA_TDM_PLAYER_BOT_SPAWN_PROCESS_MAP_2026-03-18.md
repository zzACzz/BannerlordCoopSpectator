## Purpose
Build one consolidated map of how spawn, respawn, preview visuals, player agents, and bots work in the current vanilla TDM shell that CoopSpectator is using.

This document is meant to stop future work from rediscovering the same lifecycle fragments one by one.

## Scope
This map is specifically about the current integration target:
- vanilla multiplayer mission runtime
- TDM spawning behavior
- coop authority layered on top of that shell

It does not try to describe every MP mode in Bannerlord.
It focuses on the classes and processes that actually affect the current coop implementation.

## Main vanilla owners
Relevant vanilla classes inspected:
- `TaleWorlds.MountAndBlade.TeamDeathmatchSpawningBehavior`
- `TaleWorlds.MountAndBlade.SpawningBehaviorBase`
- `TaleWorlds.MountAndBlade.SpawnComponent`
- `TaleWorlds.MountAndBlade.MissionMultiplayerGameModeBase`
- `TaleWorlds.MountAndBlade.MissionLobbyEquipmentNetworkComponent`
- `TaleWorlds.MountAndBlade.MultiplayerMissionAgentVisualSpawnComponent`
- `TaleWorlds.MountAndBlade.MultiplayerTeamSelectComponent`
- `TaleWorlds.MountAndBlade.MissionLobbyComponent`
- `TaleWorlds.MountAndBlade.MissionPeer`
- `Mission.ReplaceBotWithPlayer(...)` / `HandleServerEventReplaceBotWithPlayer(...)`

Current coop integration points:
- [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs)
- [BattleSnapshotRuntimeState.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/BattleSnapshotRuntimeState.cs)

## High-level model
Vanilla TDM spawn is not one call.

It is a staged state machine over:
- `MissionPeer.Team`
- `MissionPeer.Culture`
- `MissionPeer.SelectedTroopIndex`
- `MissionPeer.SpawnTimer`
- `MissionPeer.HasSpawnedAgentVisuals`
- `MissionPeer.EquipmentUpdatingExpired`
- MP gold
- preview visuals
- final agent creation

The most important architectural lesson is:
- `CreateAgentVisuals`
- `CreateAgent`
- gold deduction
- perk/equipment finalize
- removal of preview visuals

are all part of the same lifecycle.

Trying to replace only the last step is what repeatedly produced broken states.

## Vanilla TDM player spawn lifecycle
### 1. Team selection establishes spawn eligibility
`MultiplayerTeamSelectComponent` is not cosmetic.

Important effects on team change:
- resets `SelectedTroopIndex = 0`
- resets `NextSelectedTroopIndex = 0`
- resets `SpawnTimer`
- sets `WantsToSpawnAsBot = false`
- sets `HasSpawnTimerExpired = false`
- removes pending visuals if needed

Meaning:
- team/side assignment is a real spawn-state reset
- if peer team is stale, later spawn/control state is stale too

### 2. Loadout/equipment is wired to preview visuals
`MissionLobbyEquipmentNetworkComponent` is attached to:
- `OnMyAgentVisualSpawned -> OpenLoadout`
- `OnMyAgentSpawnedFromVisual -> CloseLoadout`
- `OnMyAgentVisualRemoved -> CloseLoadout`

Meaning:
- loadout UI is not separate from spawn
- preview visuals are an intentional lifecycle stage

### 3. TDM creates preview visuals before the real player agent
`TeamDeathmatchSpawningBehavior.SpawnAgents()` checks peers and, when valid, creates preview visuals via:
- `AgentVisualSpawnComponent.SpawnAgentVisualsForPeer(...)`
- `GameMode.HandleAgentVisualSpawning(...)`

Important peer gates in TDM:
- `ControlledAgent == null`
- `HasSpawnedAgentVisuals == false`
- `Team != spectator`
- `TeamInitialPerkInfoReady == true`
- `SpawnTimer.Check(...) == true`
- class is affordable under current gold

Important output of this stage:
- `CreateAgentVisuals`
- `HasSpawnedAgentVisuals = true`
- `EquipmentUpdatingExpired = false`

Meaning:
- preview visuals are the normal first spawn phase

### 4. Spawn equipment may still be edited after visuals
`SpawnComponent.SetEarlyAgentVisualsDespawning(...)` does not directly create an agent.

What it really changes:
- `missionPeer.EquipmentUpdatingExpired = true`

Then `SpawningBehaviorBase.CanUpdateSpawnEquipment(...)` starts returning `false`.

This is the key gate for moving from:
- preview visuals
to:
- real agent creation

### 5. `SpawningBehaviorBase.OnTick()` creates the real player agent
This is the core finalize stage.

When `SpawningBehaviorBase.OnTick()` sees:
- `ControlledAgent == null`
- `HasSpawnedAgentVisuals == true`
- `CanUpdateSpawnEquipment(peer) == false`

it builds the final agent cache and calls:
- `Mission.SpawnAgent(agentBuildData, spawnFromAgentVisuals: true)`

Then vanilla finalizes:
- `agent.AddComponent(new MPPerksAgentComponent(agent))`
- `agent.MountAgent?.UpdateAgentProperties()`
- HP adjustment from spawn perks
- `agent.Health = agent.HealthLimit`
- `agent.WieldInitialWeapons()`
- `missionPeer.SpawnCountThisRound++`
- `OnPeerSpawnedFromVisuals`
- `OnAllAgentsFromPeerSpawnedFromVisuals`
- `AgentVisualSpawnComponent.RemoveAgentVisuals(...)`
- `missionPeer.HasSpawnedAgentVisuals = false`
- perk `SpawnEnd` event

Meaning:
- real player spawn is a finalize bundle, not a single ownership packet

### 6. TDM deducts class cost after spawn-from-visuals
`TeamDeathmatchSpawningBehavior.OnAllAgentsFromPeerSpawnedFromVisuals(...)` does:
- resolve current hero class from `SelectedTroopIndex`
- subtract `TroopCasualCost`
- call `GameMode.ChangeCurrentGoldForPeer(...)`

`MissionMultiplayerGameModeBase.ChangeCurrentGoldForPeer(...)` broadcasts:
- `SyncGoldsForSkirmish`

Meaning:
- MP gold is part of the vanilla spawn contract
- if gold goes negative, the packet is invalid
- this is why coop needed the gold-floor fix before visuals finalize

## Respawn lifecycle in vanilla TDM
Death/reset is also stateful.

Observed vanilla behavior:
- on death, `MissionLobbyComponent` resets:
  - `SpawnTimer`
  - `WantsToSpawnAsBot = false`
  - `HasSpawnTimerExpired = false`
- later, once `SpawnTimer.Check(...)` passes:
  - `HasSpawnTimerExpired = true`

Then TDM spawn can go through the same preview -> finalize path again.

Meaning:
- respawn is not just "can spawn now"
- it is timer state + visuals state + team/class/gold state together

## Vanilla bot models
This is the part that matters most for future "normal army spawn" work.

There are at least two distinct bot patterns in vanilla multiplayer.

### A. Free team bots
`SpawningBehaviorBase.OnTick()` has a branch for:
- `NumberOfBotsTeam1`
- `NumberOfBotsTeam2`

If opposing-team mode is active and there is capacity, it calls:
- `SpawnBot(team, culture)`

`SpawnBot(...)`:
- chooses a random `TroopCharacter` from MP hero classes of that culture
- gets spawn frame from `SpawnComponent.GetSpawnFrame(...)`
- builds random battle equipment/body
- calls `Mission.SpawnAgent(agentBuildData)`
- marks AI alarm state
- tracks bot counts per side in `_botsCountForSides`

Meaning:
- this is "ambient team bot spawning"
- these bots are not preview-based player agents

### B. Player-led formation bodies
`SpawningBehaviorBase.OnTick()` also builds not only the player body, but additional bodies tied to the peer:
- first body uses `MissionPeer(component)`
- extra bodies use `OwningMissionPeer(component)`

These come from:
- `NumberOfBotsPerFormation`
- perk-derived troop count
- banner bearer rules

These bodies are arranged around the player/formation and belong to the peer's formation context.

Meaning:
- vanilla already has a concept of one peer being associated with multiple battle bodies
- but that concept is formation/bot-control aware, not generic possession

### C. Bot-to-player replacement
This is the most important future reference point.

Vanilla has a real "replace existing bot with player" path:
- client sends `RequestToSpawnAsBot`
- server sets `WantsToSpawnAsBot = true` when `HasSpawnTimerExpired`
- later, if `ControlledFormation` still has AI units, vanilla picks one
- then calls `Mission.ReplaceBotWithPlayer(newAgent, missionPeer)`

What `ReplaceBotWithPlayer(...)` does:
- clears owning mission peer from the bot
- sets `botAgent.MissionPeer = missionPeer`
- attaches bot to `missionPeer.ControlledFormation`
- syncs health/mount health to client
- updates `BotsUnderControlAlive`
- reassigns formation sergeant ownership if needed
- there is also a network message `ReplaceBotWithPlayer`

Meaning:
- vanilla does have an existing-agent control transfer path
- but it is not a raw `SetAgentPeer` style possession
- it is coupled to:
  - controlled formations
  - bots-under-control accounting
  - dedicated replacement network flow

This is a much better future reference than the earlier coop possession spike.

## Current coop stable runtime
### What is stable now
Stable runtime currently means:
- coop authority decides side/troop/spawn intent
- battlefield armies may be materialized as AI context
- player spawn/respawn still goes through vanilla TDM spawn

That stable path is currently proven by logs:
- `pending spawn request queued`
- `raised vanilla spawn gold floor before visuals finalize`
- `finalized pending vanilla spawn visuals for agent creation`
- `CreateAgent`
- `SyncGoldsForSkirmish`
- correct `control finalize diagnostics`

### What coop currently injects into vanilla
Before vanilla spawn finalization, coop currently forces:
- authoritative team
- fixed mission culture
- preferred troop index
- visuals finalize when coop says spawn should proceed
- gold floor so vanilla class-cost deduction remains valid

Meaning:
- coop currently works best as an authority layer that prepares valid vanilla state
- not as a replacement for vanilla spawn finalization

## Current coop army materialization layer
`TryEnsureBattlefieldArmiesMaterialized(...)` currently:
- reads allowed entry states by side
- resolves `BasicCharacterObject` from snapshot entry or fallback troop id
- spawns AI agents directly with `Mission.SpawnAgent(buildData, spawnFromAgentVisuals: false)`

Important properties of this layer:
- no preview visuals
- no `MissionPeer`
- no vanilla player gold/economy path
- no preview -> finalize lifecycle
- no `ReplaceBotWithPlayer(...)`

Meaning:
- current materialized armies are not equivalent to vanilla player or vanilla formation-bot spawn
- they are a separate custom AI layer

## Why current materialized armies still look like generic MP troops
This is currently expected.

Materialization resolves troops through:
- `BattleSnapshotRuntimeState.TryResolveCharacterObject(entryId)`
- fallback `ResolveAllowedCharacter(characterId)`

If direct character lookup fails, coop falls back to mission-safe MP troop ids in:
- `TryResolveGuaranteedMissionSafeTroopId(...)`

Examples:
- `mp_coop_heavy_infantry_empire_troop -> mp_heavy_infantry_empire_troop`
- `mp_coop_heavy_infantry_vlandia_troop -> mp_heavy_infantry_vlandia_troop`

Meaning:
- current armies are using MP-safe proxies
- they are not yet authentic campaign troop bodies/equipment

## Why the possession-first spike failed
The earlier `army-possession` spike did:
- materialize custom AI agents
- later attach player control to one of those existing agents

But it still produced "half-alive" results.

Reason:
- it bypassed the proven vanilla player-spawn lifecycle
- no preview -> finalize bundle
- no vanilla `CreateAgent` path for the player body
- no vanilla gold spend at spawn
- no `ReplaceBotWithPlayer(...)` semantics
- no controlled-formation bot replacement model

Even after team/culture/troop-index sync got better, that was still not enough.

Conclusion:
- raw possession of an existing custom AI body is not currently a valid replacement for vanilla player spawn

## What this implies for the next army-spawn iteration
### 1. Do not touch the stable player spawn path
Player spawn/respawn is finally proven-good.

Preserve:
- vanilla player spawn
- gold path
- visuals finalize path

### 2. Improve army materialization separately
The next useful step is not possession.

It is:
- better snapshot character resolution
- fewer mission-safe fallback proxies
- better count/party/side fidelity
- clearer distinction between:
  - authentic snapshot agent
  - MP-safe proxy agent

### 3. If future possession is revisited, anchor it to vanilla bot replacement
If the design goal remains:
- "armies already exist, player enters one"

the better vanilla reference is:
- `Mission.ReplaceBotWithPlayer(...)`

not:
- direct custom ownership reassignment

This likely means any future possession spike should be designed around:
- controlled formation semantics
- owning mission peer semantics
- bot-under-control accounting
- replacement network flow

or else be done in a fully custom mode that no longer relies on TDM spawn semantics.

## Recommended next research after this document
1. Audit snapshot character resolution quality:
   - which `CharacterId` values resolve directly
   - which ones fall back to MP-safe proxies
   - which side/party/entry buckets are losing fidelity
2. Build a "normal army spawn" plan:
   - stable player spawn stays vanilla
   - AI army layer becomes more snapshot-faithful
3. Only after that, decide whether:
   - player should continue spawning as a fresh vanilla body near the army
   - or a future `ReplaceBotWithPlayer` style path is realistic

## Short summary
Vanilla TDM has:
- a preview-visual spawn phase
- a real player-agent finalize phase
- a gold deduction phase
- a separate bot spawning model
- a separate bot-to-player replacement model

Current coop works when it:
- owns authority state
- prepares valid vanilla spawn state
- lets vanilla create the real player body

Current coop breaks when it:
- tries to replace vanilla player spawn with direct possession of a custom AI body

That is the process map to preserve for future work.
