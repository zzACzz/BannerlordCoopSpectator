# Hosted Battle Runtime Architecture Audit (2026-04-26)

Branch baseline for this audit:

- `codex/runtime-regression-checkpoint-2026-04-26`
- `894ee21` `Checkpoint post-MVP runtime investigation`

This note exists to stop blind reruns and local fixes that break adjacent paths.

Related next-phase handoff:

- `docs/EXACT_TEMPLATE_PATH_HANDOFF_2026-04-26.md`

That note records the decision to move toward a full exact-template path and
explicitly retire silent fallback as the target runtime model.

The current hosted battle runtime is fragile because it has multiple parallel
"truth" layers for the same peer, side, entry, and controlled agent.

## Bottom-Line Finding

Yes, the current structure is too fragile.

The main issue is not one bad patch. The issue is architectural:

1. server authority,
2. network transport,
3. bridge-file fallback,
4. UI selection state,
5. client visual overlay state,
6. commander/order-control ownership

all participate in the same spawn/control lifecycle.

That means a local fix in one layer can silently invalidate assumptions in the
others.

## Implemented Restructuring On This Branch

The first structural slice is now in code on
`codex/runtime-regression-checkpoint-2026-04-26`.

Implemented:

1. canonical per-peer session projection:
   - `Infrastructure/CoopBattlePeerSessionState.cs`
   - one snapshot now reads:
     - requested side
     - assigned side
     - runtime side
     - committed side
     - effective UI side
     - explicit selection
     - selection request
     - spawn request
     - spawn runtime
     - lifecycle runtime
     - controlled live agent

2. read/gate paths moved to the session projection:
   - active-life checks
   - queued-spawn checks
   - entry-claim checks
   - respawn gating
   - spawnable-peer gating
   - `EntryStatusSnapshot` lifecycle/status projection

3. passive lifecycle write-side collapsed into one derived path:
   - pending-request refresh
   - spawn wait/reject recovery
   - direct spawn queue
   - side/unit selection intent

4. circular dependency reduced:
   - canonical session `alive/queued` checks no longer treat the old lifecycle
     state as authoritative
   - `requested side` and `committed side` are now separated for lifecycle and
     spawn gating

## Current Runtime Planes

### 1. Authoritative battle roster plane

Owned by:

- `Infrastructure/BattleSnapshotRuntimeState.cs`
- `Campaign/BattleRosterFile.cs`

Purpose:

- stores battle parties, entries, side state, equipment, commander candidates
- feeds bootstrap, UI labels, selection entry resolution, client visual overlay

Key fact:

- this plane is both network-driven and file-driven
- remote join correctness depends on not mixing stale local file state with live
  network state

### 2. Authoritative peer selection plane

Owned by:

- `Infrastructure/CoopBattleAuthorityState.cs`

Purpose:

- requested side
- assigned side
- explicit troop selection
- explicit entry selection
- fallback/effective selection for UI defaults

Key fact:

- this must not mutate from read paths
- explicit claim and UI default must stay separate

### 3. Spawn intent and lifecycle plane

Owned by:

- `Infrastructure/CoopBattleSelectionRequestState.cs`
- `Infrastructure/CoopBattleSpawnRequestState.cs`
- `Infrastructure/CoopBattleSpawnRuntimeState.cs`
- `Infrastructure/CoopBattlePeerLifecycleRuntimeState.cs`

Purpose:

- turns selection into a spawn request
- tracks pending spawn
- tracks whether a peer is alive, waiting, respawnable, or no-side

Key fact:

- this is the real "peer battle session" state, but it is currently split across
  several stores instead of one finite-state model

### 4. Mission bootstrap and battlefield materialization plane

Owned by:

- `Mission/CoopMissionBehaviors.cs`
- `Infrastructure/ExactCampaignArmyBootstrap.cs`

Purpose:

- chooses bootstrap peer/side
- bridges `Mission.PlayerTeam` / `Mission.PlayerEnemyTeam`
- materializes exact-scene armies
- tracks live agents back to authoritative entry ids

Key fact:

- this plane is global to the mission, but parts of it still depend on peer-side
  state

### 5. Transport/status plane

Owned by:

- `Mission/CoopMissionNetworkBridge.cs`
- `Infrastructure/CoopBattleEntryStatusBridgeFile.cs`

Purpose:

- sends `BattleSnapshot`
- sends peer-scoped `EntryStatusSnapshot`
- persists host local status file for UI/runtime

Key fact:

- `EntryStatusSnapshot` is overloaded:
  - readiness gate
  - UI payload
  - lifecycle payload
  - selection payload
  - spawn payload
  - host-start authority payload

### 6. Client UI and local bridge plane

Owned by:

- `UI/CoopSelectionUiHelpers.cs`
- `UI/CoopMissionSelectionView.cs`
- `Infrastructure/CoopBattleSelectionBridgeFile.cs`
- `Infrastructure/CoopBattleSpawnBridgeFile.cs`

Purpose:

- team screen
- unit screen
- local current-selection persistence
- local `SpawnNow` / `StartBattle` actions

Key fact:

- the UI sometimes reconstructs state from:
  - network `EntryStatusSnapshot`
  - local selection bridge
  - `BattleSnapshotRuntimeState`
  - fallback allowed lists

That is too many sources for one screen.

### 7. Client visual and commander-control plane

Owned by:

- `Patches/BattleMapSpawnHandoffPatch.cs`
- client-exact overlay code inside `Mission/CoopMissionBehaviors.cs`

Purpose:

- finalize local visuals after `CreateAgent` / `SetAgentPeer` /
  `SynchronizeAgentSpawnEquipment`
- suppress early follow/order-control transitions
- promote exact commander to general control

Key fact:

- this plane must never become authoritative for entry identity
- it should only decorate and stabilize native client handoff

## The Cycles That Keep Breaking

### Cycle A: bootstrap side <-> readiness <-> materialization

Current shape:

1. exact bootstrap wants a side
2. readiness may want materialized live entries
3. materialization may wait for bootstrap
4. side assignment may wait for UI

That creates "loading forever" or "host cannot spawn until remote client picks a
side" failures.

### Cycle B: selectable list <-> live agents <-> spawn request

Current shape:

1. `ResolveSelectableEntryIdsForStatus(...)` may prefer live materialized agents
2. spawn requires a selectable entry
3. readiness requires selectable entries
4. live agent identity itself depends on exact bootstrap and overlay resolution

That creates "counts are visible but class screen is empty" and "commander
appears/disappears" failures.

### Cycle C: entry identity <-> visuals <-> equipment sync

Current shape:

1. peer selects an authoritative entry
2. client receives native `CreateAgent`, `SetAgentPeer`,
   `SynchronizeAgentSpawnEquipment`
3. local patch tries to map native agent back to authoritative entry
4. visual overlay applies equipment/body/horse

If entry resolution is wrong for even one tick, the client can see:

- naked units
- wrong horse
- wrong class model
- crash after control handoff

### Cycle D: commander control <-> local identity <-> native order controller

Current shape:

1. commander promotion depends on controlled entry identity
2. local order suppression runs before identity is always resolved
3. native commander assignment updates `Formation.PlayerOwner`
4. `Formation.PlayerOwner` changes AI control
5. `OrderController.SelectAllFormations()` emits client network traffic

That creates:

- commander flicker
- non-commander getting flags
- host cannot command before client actions
- pre-battle orders being overwritten

## Engine Contracts Verified With ILSpy

These are not guesses. They were checked against Bannerlord assemblies with
`ilspycmd`.

### `MissionNetworkComponent`

Observed native behavior:

- `HandleServerEventCreateAgent(...)`
  - builds the native agent directly from network `CreateAgent`
  - uses server-provided spawn equipment and formation/team inputs
- `HandleServerEventSetAgentPeer(...)`
  - only sets `agent.MissionPeer = missionPeer`
- `HandleServerEventSynchronizeAgentEquipment(...)`
  - directly calls `UpdateSpawnEquipmentAndRefreshVisuals(...)`
- `HandleServerEventAssignFormationToPlayer(...)`
  - calls `Team.AssignPlayerAsSergeantOfFormation(...)`

Implication:

- client handoff is staged across several native events
- our exact-visual finalization must tolerate incomplete identity until that
  chain finishes

### `Team.AssignPlayerAsSergeantOfFormation(...)`

Observed native behavior:

- sets `formation.PlayerOwner = peer.ControlledAgent`
- sets banner
- sets order-controller owner
- calls `formation.SetControlledByAI(false)`
- local peer then runs `PlayerOrderController.SelectAllFormations()`

Implication:

- commander assignment is not cosmetic
- it changes both formation AI ownership and UI/controller behavior

### `Formation.PlayerOwner`

Observed native behavior:

- setter calls `SetControlledByAI(value == null)`

Implication:

- any patch that touches `PlayerOwner` or commander assignment is also touching
  formation AI control

### `MissionPeer.FollowedAgent`

Observed native behavior:

- client setter emits `SetFollowedAgent(...)` back over the network

Implication:

- early local follow-switches can echo into the handshake unless they are
  deliberately suppressed

### `OrderController.SelectAllFormations(...)`

Observed native behavior:

- client path sends `SelectAllFormations` network message
- local selectable-formations state depends on `Formation.PlayerOwner`

Implication:

- if we promote order control before identity is stable, we can send the wrong
  native commander interaction

### `MultiplayerMissionAgentVisualSpawnComponent`

Observed native behavior:

- visual spawn uses network-provided equipment
- horse presence is inferred from equipment mount slot
- visual spawn point allocation is peer-based and separate from real battlefield
  possession

Implication:

- wrong entry-to-agent mapping naturally produces naked/wrong-mount visuals even
  before a hard crash

## Why Constant Regressions Happened

The regressions are explainable:

1. one peer lifecycle is represented in too many places
2. some layers are authoritative and others are heuristic, but the boundaries
   are blurry
3. global mission readiness and per-peer readiness are mixed
4. `EntryStatusSnapshot` carries both data and policy
5. client UI still reconstructs truth instead of consuming one stable contract
6. host self-join special handling leaks across transport, bootstrap, and UI
7. patches are compensating for native staged handoff instead of modeling it
   explicitly

This is exactly why the system feels like it "worked until one more fix landed".
The behavior is emergent, not bounded by a strict state model.

## Invariants That Must Be Made Explicit

These invariants should become hard rules.

### Global mission invariants

1. `BattleSnapshotRuntimeState` is the only authoritative roster universe for
   remote clients during battle runtime.
2. Materialization readiness is a mission-global state, not a peer-side effect.
3. Exact bootstrap side resolution must not depend on whichever peer happened to
   synchronize first.

### Peer lifecycle invariants

1. A peer must have exactly one lifecycle state.
2. A peer must have at most one explicit selected entry.
3. A peer must have at most one pending spawn request.
4. A peer-controlled live agent must resolve to exactly one authoritative
   `EntryId`.

### UI invariants

1. Team screen reads one contract.
2. Unit screen reads one contract.
3. Counts and actual entry lists must come from the same source version.
4. UI defaults must never create or mutate ownership.

### Commander invariants

1. commander roster identity is separate from commander control ownership
2. promotion to general control only happens after authoritative controlled
   entry resolution
3. pre-battle hold must freeze autonomous battle progress without overwriting
   valid commander orders every tick

## Recommended Restructure

Do not keep patching this as a collection of local conditions.

### Step 1. Define one server-authoritative peer session model

Introduce one explicit state object for each peer, conceptually:

- `PeerName`
- `RequestedSide`
- `AssignedSide`
- `ExplicitSelectedEntryId`
- `SpawnRequestedEntryId`
- `PossessedEntryId`
- `LifecycleState`
- `CommanderControlState`
- `ReadinessStage`

Everything else should derive from that plus global mission state.

### Step 2. Split global mission state from peer state

Mission-global:

- snapshot loaded
- bootstrap active
- armies materialized
- battle phase
- battle start allowed

Peer-local:

- side chosen
- entry chosen
- spawn queued
- possessed
- commander or non-commander

Do not compute one from the other implicitly in ten places.

### Step 3. Make `EntryStatusSnapshot` a read-model only

It should describe state, not decide it.

That means:

- no UI fallback writes
- no claim mutation
- no hidden "effective selection becomes explicit selection" path
- no transport compaction that changes contract semantics

If transport needs compaction, the UI contract must explicitly support it.

### Step 4. Reduce active truth sources for remote clients

For remote joins during battle runtime:

- network snapshot/status should be primary
- local `battle_roster.json` fallback should be disabled
- local exact-visual overlay must not invent authoritative entry identity

Host self-join can remain a special case, but that special case must be bounded
to host-only code paths.

### Step 5. Treat client visual overlay as post-possession decoration

Do not let client overlay queues decide who the peer "really is".

Authoritative identity order should be:

1. spawn request entry id
2. server-confirmed possessed entry id
3. tracked materialized entry id
4. visual overlay cache only as a last fallback

### Step 6. Add transition-grade diagnostics

Log only on state transitions, not on every tick.

We need one stable audit line per peer transition:

- `NoSide -> SideAssigned`
- `SideAssigned -> EntrySelected`
- `EntrySelected -> SpawnQueued`
- `SpawnQueued -> Possessed`
- `Possessed -> CommanderControlReady`
- `Possessed -> DeadAwaitingRespawn`

This will cut reruns more than adding more freeform logs.

## Files That Should Be Treated As One Atomic Runtime Cluster

These should not be patched independently anymore:

- `Mission/CoopMissionBehaviors.cs`
- `Mission/CoopMissionNetworkBridge.cs`
- `Infrastructure/CoopBattleAuthorityState.cs`
- `Infrastructure/CoopBattleEntryStatusBridgeFile.cs`
- `Infrastructure/BattleSnapshotRuntimeState.cs`
- `Infrastructure/ExactCampaignArmyBootstrap.cs`
- `Patches/BattleMapSpawnHandoffPatch.cs`
- `UI/CoopSelectionUiHelpers.cs`
- `UI/CoopMissionSelectionView.cs`

If a change touches one of these and changes runtime behavior, the whole cluster
must be reviewed as one system.

## Practical Rule For The Next Fixes

Before changing runtime code again:

1. define which plane owns the fix
2. define which invariants the fix must preserve
3. define which other planes are forbidden to change
4. verify the relevant native engine contract first

If a proposed fix crosses:

- bootstrap/materialization,
- selection/readiness,
- client visual finalization,
- commander control

it is too wide and should be split first.

## Immediate Next Direction

The right next step is not another symptom patch.

The right next step is:

1. keep the restored latest checkpoint as the working branch,
2. derive one explicit peer session state machine from the current split stores,
3. make `EntryStatusSnapshot` a pure projection of that state machine,
4. narrow `BattleMapSpawnHandoffPatch` so it only handles native client event
   ordering, not authoritative identity decisions.

That is the path to a structure that is harder to break.
