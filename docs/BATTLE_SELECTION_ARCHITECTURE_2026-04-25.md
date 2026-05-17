# Battle Selection Architecture (2026-04-25)

This note records the current architecture for coop battle side selection, entry selection, prebattle materialization, spawn handoff, and commander order control.

It exists to reduce blind reruns. The core rule is:

- `effective selection` is what the UI may default to.
- `explicit selection/claim` is what must hide an entry from other peers.

Those two states must not be conflated.

## Current Flow

1. Campaign/battle snapshot builds the authoritative roster.
2. `CoopBattleAuthorityState` stores side assignment plus per-peer explicit troop/entry selections.
3. `CoopMissionSpawnLogic.ResolveSelectableEntryIdsForStatus(...)` builds the selectable list for a specific peer.
4. `BuildEntryStatusSnapshotForPeer(...)` publishes that peer-scoped snapshot through `CoopMissionNetworkBridge`.
5. Client UI reads the snapshot and renders side/unit selection.
6. A peer explicitly selecting an entry updates `CoopBattleAuthorityState` and then that entry becomes claimed for other peers.
7. Spawn uses `CoopBattleSelectionRequestState` -> `CoopBattleSpawnRequestState` -> direct spawn/materialized-agent handoff.

## Files That Matter

- `Infrastructure/CoopBattleAuthorityState.cs`
  - authoritative side assignment
  - allowed troop/entry rosters
  - explicit per-peer troop/entry selection
  - effective fallback selection for UI/status
- `Mission/CoopMissionBehaviors.cs`
  - `ResolveSelectableEntryIdsForStatus`
  - claim filtering
  - peer spawn readiness
  - preferred class alignment
  - entry status snapshot build/write
- `Mission/CoopMissionNetworkBridge.cs`
  - server/client payload transport for entry status and battle snapshot
- `Infrastructure/BattleCommanderResolver.cs`
  - commander entry resolution per side
- `Patches/BattleMapSpawnHandoffPatch.cs`
  - local commander order-control/UI suppression and promotion logic
- `Infrastructure/BattleSnapshotRuntimeState.cs`
  - battle roster, side states, entry states
- `Infrastructure/ExactCampaignArmyBootstrap.cs`
  - authoritative side/entry identity for materialized agents

## Critical State Split

### Effective Selection

`CoopBattleAuthorityState.GetSelectionState(...)` resolves:

- requested side
- assigned side
- effective troop id
- effective entry id

This may fall back to the first allowed troop/entry when the peer has not explicitly selected anything yet.

This state is valid for:

- UI default highlight
- status text
- fallback class matching

This state is not valid for ownership/claim decisions by itself.

### Explicit Selection

Explicit selection is only what was intentionally written into:

- `_selectedTroopIdByPeer`
- `_selectedEntryIdByPeer`

via:

- `TrySetSelectedEntryId(...)`
- `TrySetSelectedTroopId(...)`

or what is already committed as:

- pending spawn request
- controlled agent identity

Only explicit selection should make an entry disappear from other peers.

## Root Cause Found On 2026-04-25

Two separate bugs were mixed together:

1. `GetSelectionState()` used to write fallback/default troop and entry ids back into the explicit dictionaries while merely reading state.
2. claim filtering (`DoesPeerClaimEntryId`) treated `GetSelectionState().EntryId` as an actual claim.

Effect:

- `AssignSide` could make the first allowed entry look claimed even before any real entry click.
- on player side that was usually the commander entry.
- prebattle selectable lists then became peer-dependent and incomplete.
- host/client snapshots could disagree about commander availability.

That explains the observed pattern:

- units already materialized
- commander missing for host before remote client even picked a side
- commander appearing later for another peer
- `allowed-prebattle-claim-filtered` dropping commander too early

## Fix Direction Applied

### 1. Stop read paths from mutating explicit selection

`CoopBattleAuthorityState` now keeps fallback resolution read-only for non-explicit selection.

Meaning:

- UI can still get a default selected entry
- `HasExplicitSelection(...)` now reflects real explicit selection again
- explicit-claim logic is no longer polluted by status/UI fallback reads

### 2. Claim only on explicit entry ownership

`DoesPeerClaimEntryId(...)` should treat an entry as claimed only if at least one of these is true:

- explicit selected entry id matches
- pending spawn request entry id matches
- controlled agent resolves to that authoritative entry id

This matches the desired semantics:

- everyone can see all currently available entries
- once one peer actually takes/selects a concrete entry, it disappears for others

### 3. No special commander reservation in prebattle list ownership

The hosted-local-peer commander reservation path was a wrong abstraction for the current desired behavior.

Desired behavior is simpler:

- commander is not pre-reserved for host
- commander is hidden only after a real peer claim

## Selectable List Construction

`ResolveSelectableEntryIdsForStatus(...)` is the main selector list builder.

Pre-battle:

- source starts from allowed authoritative roster
- once materialized agents exist in `PreBattleHold`, live materialized entries are merged with allowed roster
- this avoids dropping late-resolving commander/live entries

Battle active:

- source prefers live materialized eligible agents
- if live state exists but no eligible entries remain, list is empty by design

Claim filtering:

- runs peer-specifically
- happens after source list resolution
- must only remove explicitly claimed entries

Important:

- `TryWriteEntryStatusSnapshot(...)` logs a peer-scoped universe based on `ResolvePrimaryControllablePeer(...)`
- that log is useful, but it is not a truly global list for all peers

## Primary Peer Resolution

Two different decisions were incorrectly sharing one shortcut:

1. which peer should anchor exact-campaign bootstrap
2. which peer should represent the local hosted player for bridge-file driven control

The old shortcut was:

- take the first synchronized non-server peer

That is wrong on a hosted dedicated flow because the remote client can synchronize before the local self-join host peer.

Effects seen in logs:

- host could not fully select/spawn until remote client chose a side
- bootstrap could follow the remote peer instead of the host
- host/client snapshots diverged even though materialized agents already existed

Current rule:

- if `HostSelfJoinRedirectState` persisted a hosted local peer marker, `ResolveHostedLocalMissionPeer(...)` must win
- only if that hosted local peer cannot be resolved should code fall back to the first synchronized peer

This rule now applies both to:

- `ResolveExactCampaignBootstrapPeer(...)`
- `ResolvePrimaryControllablePeer(...)`

The marker lookup is now cached in-memory after a successful read so frequent status writes do not keep hitting disk.

## Spawn Request Path

Selection and spawn are intentionally split:

- side/entry selection updates authority state
- spawn request is separate
- `ShouldAutoQueueSelectionFromAuthority(...)` is currently `false`

Relevant stores:

- `CoopBattleSelectionRequestState`
- `CoopBattleSpawnRequestState`
- `CoopBattleSpawnRuntimeState`
- `CoopBattlePeerLifecycleRuntimeState`

This means a peer can have:

- assigned side
- visible selectable list
- default highlighted entry

without yet having an explicit spawn request.

## Commander and Order Control

Commander status has two separate layers:

1. roster-level commander identity
   - `BattleCommanderResolver`
2. local order-control/UI ownership after spawn
   - `BattleMapSpawnHandoffPatch`

Do not mix these.

Roster commander problems usually come from:

- wrong side roster
- missing commander resolution
- incorrect claim filtering

Order UI problems usually come from:

- unresolved controlled entry identity
- local general/order-controller state not promoted yet
- formation `PlayerOwner` / `HasPlayerControlledTroop` / `IsPlayerTroopInFormation` not aligned

## Prebattle Hold Semantics

There are two separate requirements before battle start:

1. armies must stay inert and not auto-attack
2. a real commander must still be able to reposition formations before battle start

The bad implementation was:

- every tick during `PreBattleHold`, re-apply:
  - `MovementOrderStop`
  - `FiringOrderHoldYourFire`
  - formation AI-control changes

That preserved inertia, but it also overwrote commander-issued prebattle orders every frame.

Current rule:

- apply the initial hold when a formation first enters the prebattle hold set
- keep global AI paused until `BattleActive`
- once a formation/team is already owned by a real player-general, stop reasserting hold on every tick

That keeps the prebattle freeze for untouched formations, while letting a commander move troops before the host starts the battle.

## ILSpy Anchors

The following vanilla points were checked with `ilspycmd` and are important:

- `TaleWorlds.MountAndBlade.MultiplayerTeamSelectComponent.ChangeTeamServer(...)`
  - resets troop selection indices on team change
  - clears visuals and spawn timer state
- `TaleWorlds.MountAndBlade.MissionMultiplayerGameModeBase.HandleAgentVisualSpawning(...)`
  - part of vanilla visual-spawn pipeline used before direct control handoff
- `TaleWorlds.MountAndBlade.Formation`
  - `PlayerOwner`
  - `HasPlayerControlledTroop`
  - `IsPlayerTroopInFormation`
  - `SetControlledByAI(...)`
  - `OnAgentControllerChanged(...)`
- `TaleWorlds.MountAndBlade.MultiplayerWarmupComponent`
  - warmup is its own vanilla state machine and is not identical to our coop prebattle hold

## What To Verify In The Next Rerun

Before any explicit entry selection:

- host and remote side snapshots should both contain commander if commander exists on that side
- there should be no premature `-claim-filtered` loss caused by side assignment alone
- host should be able to see/select commander without waiting for another peer to pick a side

After one peer explicitly selects an entry:

- that exact entry should disappear from the selectable list of other peers
- no other entries should disappear with it

Prebattle behavior:

- armies stay materialized
- AI remains held
- commander can issue orders once actually controlling commander
- those orders must persist before battle start instead of being overwritten on the next hold tick

## Anti-Patterns To Avoid

- using `GetSelectionState()` as proof of claim
- writing fallback/default UI state into explicit authority dictionaries
- reserving commander ownership through peer identity instead of explicit entry claim
- treating the first synchronized peer as equivalent to the hosted local player
- reapplying prebattle hold every tick after a commander has already taken order control
- assuming a snapshot/log built for one peer represents the same selectable list for every peer
