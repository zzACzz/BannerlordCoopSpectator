# Exact Transfer Contract Spec

Date: 2026-05-02

## Goal

Define the exact transfer adapter contract that maps campaign battle snapshot data
into the native Bannerlord multiplayer spawn contract for strict exact heroes.

Selected strategy:

- do not build a custom multiplayer runtime
- do not continue expanding surrogate runtime shims
- adapt campaign data into the native multiplayer/TDM spawn lifecycle
- implement only after the full contract is explicit

Immediate scope:

- main hero
- lords
- companions / other exact personal hero entries
- especially mounted remote hero client materialization

Non-goals for this phase:

- bulk troop exact 1:1 rollout
- more post-spawn visual repair as a primary path
- more speculative guard/fallback patches without a contract-level reason

## Current conclusion

The problem is not just field mismatch. The problem is:

- field mapping
- legal event sequence
- rider/mount lifecycle
- peer binding timing
- commander-control ownership timing
- death/respawn cleanup timing

This is a contract problem, not a single bug.

## Native multiplayer spawn contract

### Client materialization sequence

Observed native client-side sequence for spawned agents:

1. `CreateAgent`
2. `Mission.SpawnAgent(agentBuildData)`
3. `SetAgentPeer`
4. `SynchronizeAgentSpawnEquipment`
5. `SetWieldedItemIndex`
6. `SetWeaponNetworkData` / `SetWeaponAmmoData`
7. `ReplaceBotWithPlayer` where applicable
8. `SetAgentHealth` / death / remove lifecycle

### Native `CreateAgent` contract

Decompiled `MissionNetworkComponent.HandleServerEventCreateAgent(...)` shows that
the client builds `AgentBuildData` from the network payload with these core inputs:

- `Character`
- `Peer`
- `Monster`
- `SpawnEquipment`
- `MissionEquipment`
- `BodyPropertiesSeed`
- `IsFemale`
- `TeamIndex`
- `Position`
- `Direction`
- `FormationIndex`
- `MountAgentIndex`
- `IsPlayerAgent`
- `ClothingColor1`
- `ClothingColor2`

Important native branch behavior:

- if `IsPlayerAgent == true`, the client reads body properties from
  `missionPeer.Peer.BodyProperties`
- if `IsPlayerAgent == false`, the client derives body properties from
  `character.GetBodyPropertiesMin/Max()` and `character.BodyPropertyRange`
- if no formation is present and `missionPeer != null`, banner comes from
  `missionPeer.Peer.BannerCode`
- the handler finishes by calling `Mission.SpawnAgent(agentBuildData)` and then
  touching `.MountAgent`

This means `CreateAgent` is already a composite contract:

- identity contract
- body contract
- formation/banner contract
- mount-index contract

### Native `Mission.SpawnAgent(...)` expectations

From decompiled `Mission.SpawnAgent(...)`:

- `AgentCharacter` must be valid
- age/body/gender are normalized before full spawn completes
- team, colors, origin, formation, position, direction, equipment, and mount index
  are consumed during spawn
- body properties can be applied before the rest of the lifecycle continues

This means that bad identity/body/equipment state is not just a visual problem.
It can break native materialization before later recovery hooks even run.

### Native post-spawn handlers

Key decompiled handlers:

- `SetAgentPeer` only binds `MissionPeer` to an already-existing agent
- `SynchronizeAgentSpawnEquipment` calls
  `UpdateSpawnEquipmentAndRefreshVisuals(...)`
- `ReplaceBotWithPlayer` reassigns a bot agent to a peer, formation, and health
- `SetWieldedItemIndex` assumes the target agent and equipment state are already
  valid
- `SetAgentHealth` assumes the target agent index resolves to a live agent

Therefore:

- `CreateAgent` materialization failure cannot be reliably repaired later by
  treating `SetAgentPeer` or visual refresh as if they were spawn success
- later handlers are consumers of spawn success, not substitutes for it

## Campaign-side contract

### Authoritative entry model

The current authoritative battle snapshot entry is
`Infrastructure/BattleSnapshotRuntimeState.cs::RosterEntryState`.

Core fields already available:

- identity:
  - `EntryId`
  - `SideId`
  - `PartyId`
  - `CharacterId`
  - `OriginalCharacterId`
  - `SpawnTemplateId`
  - `CultureId`
- hero identity:
  - `HeroId`
  - `HeroRole`
  - `HeroOccupationId`
  - `HeroClanId`
  - `HeroTemplateId`
  - `HeroBodyProperties`
  - `HeroLevel`
  - `HeroAge`
  - `HeroIsFemale`
  - `IsHero`
- combat profile:
  - `IsMounted`
  - `IsRanged`
  - `HasShield`
  - `HasThrown`
  - `BaseHitPoints`
  - `PerkIds`
- stats:
  - attributes and weapon/riding/athletics skills
- exact combat equipment:
  - `CombatItem0Id..CombatItem3Id`
  - `CombatItem0Amount..CombatItem3Amount`
  - `CombatHeadId`
  - `CombatBodyId`
  - `CombatLegId`
  - `CombatGlovesId`
  - `CombatCapeId`
  - `CombatHorseId`
  - `CombatHorseHarnessId`

This is enough to describe the exact personal hero state, but not yet enough to
guarantee that each piece is fed to the native multiplayer runtime at the correct
time and in the correct shape.

### Runtime exact object layer

`Infrastructure/ExactCampaignRuntimeObjectRegistry.cs` already creates:

- runtime `BasicCharacterObject` per `EntryId`
- runtime `MPHeroClass` wrapper per `EntryId`

That layer can:

- inject battle equipment
- inject exact body properties
- derive mounted/ranged/runtime formation traits

This is an important building block for the selected strategy:

- we should prefer exact runtime objects and explicit adapter contracts
- we should not continue mutating native payloads into troop surrogates as a
  long-term primary mechanism

### Current direct-spawn reference path

`Mission/CoopMissionBehaviors.cs::SpawnCoopControlledAgent(...)` is useful as a
reference implementation because it already demonstrates a more explicit local
construction path:

- resolve authoritative team
- compute spawn frame
- build exact snapshot equipment
- build `AgentBuildData`
- apply entry identity/body
- call `Mission.SpawnAgent(...)`
- bind ownership and mission peer
- optionally refresh visuals / wield initial weapons

This is not the final solution for multiplayer hero transfer, but it is the best
existing reference inside the repo for a clean adapter-style construction path.

## Mapping matrix

### Identity and class

Native requirement:

- multiplayer-valid `Character`
- multiplayer-valid `AgentOrigin`
- multiplayer-valid class semantics

Campaign sources:

- `CharacterId`
- `OriginalCharacterId`
- `SpawnTemplateId`
- `HeroTemplateId`
- runtime exact object registry

Adapter decision:

- strict exact heroes should resolve to an explicit runtime exact character/class
  contract
- this resolution must be stable before spawn
- runtime spawn must not degrade to troop surrogate as a final architecture

### Body contract

Native requirement:

- valid body properties at `CreateAgent`
- valid age / gender consistency
- valid `BodyPropertyRange` if native random-body branch is used

Campaign sources:

- `HeroBodyProperties`
- `HeroAge`
- `HeroIsFemale`

Adapter decision:

- strict exact heroes should avoid native random-body derivation whenever possible
- exact body must be part of the pre-spawn contract, not a late visual patch

### Equipment contract

Native requirement:

- `SpawnEquipment`
- `MissionEquipment`
- stable weapon slots
- valid horse / harness slot semantics

Campaign sources:

- `CombatItem0..3`
- armor slots
- `CombatHorseId`
- `CombatHorseHarnessId`

Adapter decision:

- exact personal heroes should build one canonical snapshot equipment contract
- slot policy must be explicit:
  - pre-spawn-safe
  - post-bind-sync-only
  - unsafe / deferred

### Mount contract

Native requirement:

- rider `MountAgentIndex`
- mount materializes as a native agent
- rider/mount link must exist before exact-ready state

Campaign sources:

- `IsMounted`
- `CombatHorseId`
- `CombatHorseHarnessId`

Adapter decision:

- mount contract is first-class, not just part of visuals
- `ExactReady` is illegal until rider and mount are both materialized and linked

### Peer binding contract

Native requirement:

- `SetAgentPeer` after agent exists
- peer body/banner/team semantics become valid only after a real peer bind

Campaign sources:

- player/peer ownership from battle/session state
- entry claim and commander ownership data

Adapter decision:

- peer binding must be modeled as a separate stage
- no commander-ready state before peer-bound state is valid

### Wield contract

Native requirement:

- weapon slots exist and are synchronized
- wield operations happen only after valid agent/equipment state

Campaign sources:

- exact equipment
- derived initial wield preference

Adapter decision:

- initial wield must be derived once into an explicit sub-contract
- no more heuristic “refresh until it looks right” as primary behavior

### Commander-control contract

Native requirement:

- controlled agent identity is stable
- formation ownership and order UI semantics bind to the right agent

Campaign sources:

- entry ownership
- peer selection / selected entry
- side / party / commander identity

Adapter decision:

- commander-control enablement is a later stage than spawn
- remote hero may not enter commander-control semantics while transfer is incomplete

### Cleanup contract

Native requirement:

- death/remove/update messages target valid rider and mount indices
- respawn/index reuse does not leak prior state

Campaign sources:

- entry identity
- mounted pair identity
- respawn claims / selected entry state

Adapter decision:

- rider+mount cleanup must be one lifecycle unit
- state clear must happen at pair scope, not per-agent cache fragment

## Hard invariants for implementation

For mounted strict exact heroes:

- `CreateAgentAccepted` is not the same as `RiderMaterialized`
- `RiderMaterialized` is not the same as `MountMaterialized`
- `MountMaterialized` is not the same as `MountLinked`
- `ExactReady` is illegal while `MountLinked == false`
- `CommanderReady` is illegal while `ExactReady == false`
- queued refresh is never equivalent to applied state
- death cleanup must clear rider and mount state together

## What the latest failed runs prove

1. Server-side strict exact hero pre-spawn injection can already produce the right
   exact equipment contract for the host hero.
2. The local client still fails earlier, inside native `CreateAgent`, before a
   valid rider/mount pair exists.
3. Surrogate payload mutation degrades visuals and semantics while still not
   fixing the root failure.
4. Therefore the next safe step is not another runtime shim. It is a clean
   contract-first redesign of the exact transfer adapter.

## Analysis work packages before implementation

### Package A: Complete native lifecycle spec

Document, with code references, the exact legal order and assumptions for:

- `CreateAgent`
- `Mission.SpawnAgent`
- `SetAgentPeer`
- `SynchronizeAgentSpawnEquipment`
- `SetWieldedItemIndex`
- `ReplaceBotWithPlayer`
- `SetAgentHealth`
- death / remove / respawn

Deliverable:

- one native lifecycle diagram
- one list of “must already be valid at this stage”

### Package B: Complete campaign-source matrix

For each field consumed by the native lifecycle, document:

- source in `RosterEntryState`
- whether direct / derived / missing
- whether safe pre-spawn / safe post-bind / unsafe

Deliverable:

- one mapping matrix with no blank rows

### Package C: Explicit adapter contract object

Define the shape of the new adapter object before code:

- `IdentityContract`
- `BodyContract`
- `EquipmentContract`
- `MountContract`
- `PeerBindingContract`
- `InitialWieldContract`
- `CommanderControlContract`
- `CleanupContract`

Deliverable:

- one C#-oriented structural spec

### Package D: Exact hero state machine

Define the legal stages and transitions:

- `SnapshotResolved`
- `ClassResolved`
- `PreSpawnPrepared`
- `CreateAgentAccepted`
- `RiderMaterialized`
- `MountMaterialized`
- `MountLinked`
- `PeerBound`
- `EquipmentSynchronized`
- `ExactReady`
- `CommanderReady`
- `DeathCleaned`

Deliverable:

- one transition table
- one list of forbidden transitions

### Package E: Implementation gate review

No new hero spawn implementation starts until all are true:

- every native field has a campaign source or an explicit derivation rule
- every lifecycle stage has an owner and preconditions
- every mounted-hero failure mode has a contract-level policy
- no surrogate troop fallback remains part of the target hero path

## Planned implementation sequence after analysis gate

1. remove surrogate-as-primary-path from strict exact hero design
2. introduce the explicit exact transfer adapter object
3. implement hero-first server-side contract assembly
4. implement client stage machine without fake applied states
5. enable only:
   - main hero
   - lords
   - companions
6. run multi-death / multi-respawn validation
7. only then expand to safer troop subsets

## Current status

Analysis has confirmed the direction:

- selected path: adapter to native multiplayer contract
- rejected path: keep expanding surrogate runtime shims
- implementation status: blocked on completing the full contract spec

No further speculative runtime repair work should be added until this analysis gate
is closed.
