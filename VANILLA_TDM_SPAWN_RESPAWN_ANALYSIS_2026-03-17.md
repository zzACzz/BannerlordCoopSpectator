## Purpose
Build a full-enough map of the vanilla TDM spawn/respawn pipeline so future work is based on dependency knowledge, not trial-and-error.

This document answers:
- what actually drives vanilla player spawn
- what state and behaviors it depends on
- where respawn comes from
- what our current coop integration is relying on
- whether it is realistic to keep vanilla spawn or replace it with a custom one

## Vanilla classes inspected
Decompiled/inspected:
- `TeamDeathmatchSpawningBehavior`
- `SpawningBehaviorBase`
- `SpawnComponent`
- `MissionMultiplayerGameModeBase`
- `MultiplayerMissionAgentVisualSpawnComponent`
- `MissionLobbyEquipmentNetworkComponent`
- `MultiplayerTeamSelectComponent`
- `MissionNetworkComponent`
- `MissionPeer`

Current coop touchpoints:
- [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs)
- [MissionMultiplayerCoopTdmMode.cs](/C:/dev/projects/BannerlordCoopSpectator3/GameMode/MissionMultiplayerCoopTdmMode.cs)
- [MissionMultiplayerCoopTdm.cs](/C:/dev/projects/BannerlordCoopSpectator3/GameMode/MissionMultiplayerCoopTdm.cs)
- [MissionMultiplayerCoopTdmClient.cs](/C:/dev/projects/BannerlordCoopSpectator3/GameMode/MissionMultiplayerCoopTdmClient.cs)

## High-level vanilla ownership map
### 1. `MultiplayerTeamSelectComponent`
Responsibilities:
- change team / side
- reset some spawn-related peer state on team change
- notify UI (`OnSelectingTeam`, `OnMyTeamChange`, `OnUpdateTeams`)
- if visuals already exist, remove them on team change

Important state effects:
- resets `SelectedTroopIndex` to `0`
- resets `SpawnTimer`
- clears `WantsToSpawnAsBot`
- clears `HasSpawnTimerExpired`
- may remove pending visuals

Meaning:
- side/team assignment is not cosmetic
- it is a hard spawn prerequisite and also a reset point

### 2. `MissionLobbyEquipmentNetworkComponent`
Responsibilities:
- bridges loadout/equipment UI with spawn visuals
- exposes `OnToggleLoadout`
- exposes `OnEquipmentRefreshed`
- on client, listens to:
  - `OnMyAgentVisualSpawned`
  - `OnMyAgentSpawnedFromVisual`
  - `OnMyAgentVisualRemoved`

Meaning:
- vanilla treats "visual preview" as a real stage with its own equipment-edit window
- spawn is intentionally split into:
  - visual preview
  - final agent creation

### 3. `MultiplayerMissionAgentVisualSpawnComponent`
Responsibilities:
- create preview visuals for a peer
- remove visuals for a peer
- emit lifecycle callbacks:
  - `OnMyAgentVisualSpawned`
  - `OnMyAgentSpawnedFromVisual`
  - `OnMyAgentVisualRemoved`

Meaning:
- preview visuals are not UI-only fluff
- they are part of the spawn handshake

### 4. `MissionMultiplayerGameModeBase`
Responsibilities relevant to spawn:
- sends `CreateAgentVisuals`
- tracks gold via `GetCurrentGoldForPeer(...)` / `ChangeCurrentGoldForPeer(...)`
- sets `HasSpawnedAgentVisuals = true`
- sets `EquipmentUpdatingExpired = false`

Important finding:
- `ChangeCurrentGoldForPeer(...)` broadcasts `SyncGoldsForSkirmish`
- if the value is negative, the packet becomes invalid and the client can crash

Meaning:
- MP gold/economy is part of the spawn contract in vanilla TDM
- even if coop does not conceptually use gold, vanilla TDM still does

### 5. `SpawningBehaviorBase`
This is the core lifecycle owner.

Responsibilities:
- initialize spawn stack references
- subscribe to `OnEquipmentRefreshed`
- decide when visuals may exist
- decide when visuals can turn into real agents
- create final agents with `spawnFromAgentVisuals: true`
- run post-spawn finalize

Important peer gates seen in the decompile:
- `ControlledAgent == null`
- `HasSpawnedAgentVisuals == false` for preview creation
- `HasSpawnedAgentVisuals == true && !CanUpdateSpawnEquipment(peer)` for final agent creation
- `Team != spectator`
- `TeamInitialPerkInfoReady == true`
- `SpawnTimer.Check(...) == true`

Important finalization done in vanilla after real spawn:
- `Mission.SpawnAgent(..., spawnFromAgentVisuals: true)`
- `agent.AddComponent(new MPPerksAgentComponent(agent))`
- `agent.MountAgent?.UpdateAgentProperties()`
- on-spawn perk HP adjustment
- `agent.Health = agent.HealthLimit`
- `agent.WieldInitialWeapons()`
- `missionPeer.SpawnCountThisRound++`
- invoke:
  - `OnPeerSpawnedFromVisuals`
  - `OnAllAgentsFromPeerSpawnedFromVisuals`
- `AgentVisualSpawnComponent.RemoveAgentVisuals(...)`
- `missionPeer.HasSpawnedAgentVisuals = false`
- perk `SpawnEnd`

Meaning:
- this is exactly the layer manual/direct spawn kept bypassing
- this is why direct spawn kept producing "half-alive" agents

### 6. `TeamDeathmatchSpawningBehavior`
This is the TDM-specific specialization on top of `SpawningBehaviorBase`.

Responsibilities:
- spawn preview visuals for eligible peers
- after final spawn, deduct class casual cost from gold

Important finding:
- `OnAllAgentsFromPeerSpawnedFromVisuals(MissionPeer peer)` subtracts `TroopCasualCost`

Meaning:
- even after final agent creation, TDM still performs a game-mode-specific spawn step
- this was the source of the `-20` invalid gold packet crash in coop

### 7. `MissionNetworkComponent`
Responsibilities relevant here:
- handles:
  - `RequestToSpawnAsBot`
  - `CreateAgentVisuals`
  - `RemoveAgentVisualsForPeer`
- on client, creates visuals from `CreateAgentVisuals`
- on server, `RequestToSpawnAsBot` only does:
  - if `HasSpawnTimerExpired`, set `WantsToSpawnAsBot = true`

Important nuance:
- decompile of vanilla TDM player spawn gates did not show `WantsToSpawnAsBot` as the main trigger for TDM player visual spawn
- TDM visual spawn path itself gates primarily on:
  - team
  - timer
  - perks ready
  - selected troop
  - gold

Meaning:
- `RequestToSpawnAsBot()` is not the whole story for TDM player spawn
- it is likely more central in other MP modes or bot-replacement flows

### 8. `MissionPeer`
Important spawn state fields:
- `SelectedTroopIndex`
- `EquipmentUpdatingExpired`
- `TeamInitialPerkInfoReady`
- `HasSpawnedAgentVisuals`
- `WantsToSpawnAsBot`
- `SpawnTimer`
- `HasSpawnTimerExpired`
- `SpawnCountThisRound`

Meaning:
- vanilla spawn is largely a state machine over `MissionPeer`

## Vanilla spawn lifecycle
### A. Pre-spawn readiness
Vanilla needs the peer to be in a valid state:
- non-spectator team
- valid culture/team context
- valid `SelectedTroopIndex`
- `TeamInitialPerkInfoReady`
- respawn timer ready
- enough gold for selected class

### B. Preview visuals stage
When the peer is eligible, TDM/SpawningBehaviorBase creates preview visuals:
- `GameMode.HandleAgentVisualSpawning(...)`
- sends `CreateAgentVisuals`
- sets:
  - `HasSpawnedAgentVisuals = true`
  - `EquipmentUpdatingExpired = false`

At this point:
- the peer does not yet have the final spawned agent
- vanilla still allows equipment/loadout update logic to run

### C. Transition from visuals to real agent
This happens when:
- `HasSpawnedAgentVisuals == true`
- `CanUpdateSpawnEquipment(peer) == false`

Normal vanilla way:
- equipment window closes / update phase ends

Our coop assist:
- [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs#L5809) calls `SpawnComponent.SetEarlyAgentVisualsDespawning(...)`
- this effectively tells vanilla the preview stage may end early

### D. Final real-agent spawn
`SpawningBehaviorBase` then:
- spawns agent with `spawnFromAgentVisuals: true`
- runs full post-spawn finalize
- removes preview visuals
- fires spawn completion events

### E. TDM-specific post-spawn economy
`TeamDeathmatchSpawningBehavior` deducts `TroopCasualCost` from the peer's gold.

This was the hidden blocker we hit:
- coop runtime had effectively `0` gold
- infantry casual cost was `20`
- server sent `-20`
- client crashed on `SyncGoldsForSkirmish`

Our fix:
- [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs#L5853) raises the peer gold floor before finalizing visuals

## Vanilla respawn lifecycle
Respawn is not separate from spawn. It is the same state machine restarted.

Observed vanilla responsibilities:
- on death / removal, MP systems update death counters
- `SpawnTimer.Reset(...)`
- `WantsToSpawnAsBot = false`
- `HasSpawnTimerExpired = false`

Later:
- once `SpawnTimer.Check(...)` passes, `HasSpawnTimerExpired = true`
- after that, the peer may again enter the preview -> final spawn cycle

Important meaning:
- respawn is primarily timer/state driven
- not just "call spawn again"

## What our current coop integration actually does
### Server-side coop responsibilities
Current coop code is no longer trying to own final player spawn.

It now mostly owns:
- authoritative side/team intent
- authoritative troop selection
- conversion from coop troop id to valid vanilla `SelectedTroopIndex`
- pending spawn request bookkeeping
- gold floor shim for TDM
- early finalize of visuals so vanilla completes spawn without waiting on vanilla UI

Key methods:
- [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs#L3799) `TryForceAuthoritativePeerTeams(...)`
- [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs#L4683) `TryApplySpawnIntentToPrimaryPeer(...)`
- [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs#L5738) `TryForcePreferredHeroClassForPeer(...)`
- [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs#L5809) `TryFinalizePendingVanillaSpawnVisuals(...)`
- [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs#L5853) `TryEnsureVanillaSpawnGoldFloor(...)`

### Client-side coop responsibilities
Current client mostly owns:
- bridge/hotkey/UI authority
- vanilla overlay suppression
- status mirroring
- optional vanilla spawn request message

Key method:
- [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs#L341) `TryRequestVanillaSpawnFromAuthoritativeStatus(...)`

## Most important architectural conclusion
### Continuing with vanilla spawn
This is the lower-risk path.

Reason:
- vanilla spawn is not just `SpawnAgent(...)`
- it is a coordinated lifecycle across:
  - team selection
  - troop index and perks
  - visual preview
  - equipment update window
  - final spawn-from-visual
  - perk finalize
  - economy/gold
  - respawn timers

We already proved:
- direct/manual player spawn produced "half-alive" agents
- vanilla spawn produces live infantry once its hidden preconditions are satisfied

So the strongest recommendation is:
- keep vanilla responsible for real player spawn/respawn
- keep coop authoritative for side/troop/intent/presentation

### Replacing with a fully custom spawn
This is only realistic if we replace much more than the final `SpawnAgent(...)` call.

A true custom replacement would need to own or reimplement:
- preview visuals lifecycle or remove it entirely
- equipment preview/update semantics
- client and server possession/finalize parity
- MPPerksAgentComponent and on-spawn perk effects
- spawn timers / respawn gates
- economy/class-cost semantics or a clean removal of them
- team change reset semantics
- `MissionPeer` state transitions that vanilla views expect

Meaning:
- a custom spawn may be possible
- but only as part of a more custom MP game mode stack
- not as a shallow replacement inside the current TDM-backed mission

## Recommendation
### Near-term
Stay with vanilla spawn/respawn for actual player agent creation.

Focus on:
- cavalry validation
- repeated respawn validation
- side switch -> spawn validation
- troop switch -> spawn validation
- redesigning `Ctrl+T` as a vanilla-compatible reset path

### Medium-term
Document and preserve the minimal vanilla contract coop must satisfy:
- correct team
- correct selected troop index
- valid gold floor
- preview visuals allowed to finalize
- no conflicting authority writers fighting over selected troop

### Long-term
Only consider a custom spawn replacement if the goal becomes:
- a more fully custom MP mission mode
- not "TDM with some spawn overrides"

## Short verdict
Vanilla TDM spawn is complicated, but it is now understandable.

The key takeaway is:
- we should not judge "custom spawn viability" by whether we can call `Mission.SpawnAgent(...)`
- we should judge it by whether we are willing to replace the whole vanilla spawn state machine around it

Right now the practical engineering choice is:
- continue with vanilla spawn
- continue reducing coop integration to "feed vanilla the right state, then let it spawn"
