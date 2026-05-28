# Coop Runtime Architecture Audit

Date: `2026-05-28`

## Table Of Contents
- [1. Goal](#1-goal)
- [2. Current Runtime Shape](#2-current-runtime-shape)
- [3. Main Components](#3-main-components)
- [4. End-To-End Flow](#4-end-to-end-flow)
- [5. Exact Transfer And Materialization Model](#5-exact-transfer-and-materialization-model)
- [6. Deferred Packet Model](#6-deferred-packet-model)
- [7. Current Reproducible Failure Clusters](#7-current-reproducible-failure-clusters)
- [8. What The Latest Client Crash Actually Shows](#8-what-the-latest-client-crash-actually-shows)
- [9. TDM / Vanilla MP Coupling](#9-tdm--vanilla-mp-coupling)
- [10. Invariants We Need To Preserve](#10-invariants-we-need-to-preserve)
- [11. What Is Probably Wrong In The Current Design](#11-what-is-probably-wrong-in-the-current-design)
- [12. Recommended Next Directions](#12-recommended-next-directions)
- [13. Suggested Clean-Core Migration Scope](#13-suggested-clean-core-migration-scope)
- [14. Reference Files](#14-reference-files)

## 1. Goal
This document is a single navigation map for the current coop battle runtime.

It is meant to answer:
- what currently runs on top of vanilla MP / TDM
- where campaign battle state enters the MP runtime
- where exact transfer is applied
- where deferred network replay is applied
- which invariants are already known to be fragile
- which failures are separate, and which are consequences of the same design choice

This is intentionally a runtime/system audit, not a bugfix note.

## 2. Current Runtime Shape
The mod does **not** currently run as a clean coop-only mission core.

It currently works as:
- vanilla MP / TDM mission startup
- wrapped mission behavior stack
- coop network bridge layered on top
- coop client/server mission logic layered on top
- exact transfer layered on top
- battle-map specific handoff patches layered on top
- vanilla UI and vanilla network behavior selectively suppressed or replaced

That means the system is currently a **hybrid runtime**:
- part vanilla MP
- part coop authoritative snapshot model
- part exact transfer model
- part battle-map crash isolation / replay patching

The current system therefore depends heavily on **ordering**:
- order of mission behavior injection
- order of payload arrival
- order of `CreateAgent` vs follow-up packets
- order of side selection vs battle snapshot bootstrap
- order of rider/mount link materialization

## 3. Main Components
### Mission behavior stack
- Wrapped mission stack injection happens in [MissionStateOpenNewPatches.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/MissionStateOpenNewPatches.cs:235)
- Battle client wrapper currently adds:
  - `CoopMissionNetworkBridge`
  - `CoopMissionClientLogic`
  - `CoopMissionSelectionView`

### Coop authoritative transport
- Network payload transport lives in [CoopMissionNetworkBridge.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionNetworkBridge.cs:180)
- Two central payload families are:
  - `EntryStatusSnapshot`
  - `AuthoritativeMaterializedAgentEntrySnapshot`

### Coop mission runtime logic
- Main client/server runtime logic lives in [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs:32)
- This file currently owns a very large part of:
  - exact runtime bootstrap
  - strict hero transfer
  - client materialization tracking
  - selection shell state
  - mounted link repair / exact visual follow-up

### Battle-map packet interception and replay
- The heavy handoff and replay patch lives in [BattleMapSpawnHandoffPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/BattleMapSpawnHandoffPatch.cs:129)
- This is currently the most critical runtime patch for:
  - client `CreateAgent`
  - deferred packet queues
  - replay after battle snapshot readiness
  - exact diagnostics around agent materialization

### Exact transfer builders and runtime item support
- Contract construction: [ExactTransferContractBuilder.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/ExactTransferContractBuilder.cs:12)
- Dedicated pre-spawn resolution: [ExactCreateAgentServerPreSpawnContractResolver.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/ExactCreateAgentServerPreSpawnContractResolver.cs:33)
- Runtime item support: [ExactCampaignRuntimeItemRegistry.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/ExactCampaignRuntimeItemRegistry.cs:14)

### UI suppression / replacement
- Vanilla intermission suppression: [IntermissionVmCrashGuardPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/IntermissionVmCrashGuardPatch.cs:18)
- Vanilla team/class UI suppression: [VanillaEntryUiSuppressionPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/VanillaEntryUiSuppressionPatch.cs:17)
- Coop selection shell: [CoopMissionSelectionView.cs](/C:/dev/projects/BannerlordCoopSpectator3/UI/CoopMissionSelectionView.cs:16)

## 4. End-To-End Flow
### Server
1. Campaign battle is converted into runtime battle data.
2. Dedicated battle-map mission is opened through wrapped MP mission startup.
3. Server materializes agents in mission.
4. Server periodically emits:
   - `EntryStatusSnapshot`
   - `AuthoritativeMaterializedAgentEntrySnapshot`
5. Client is expected to use the first payload family for UI state and the second for authoritative ordinary-agent binding.

### Client
1. Wrapped MP mission opens.
2. Vanilla intermission / entry UI is partially suppressed.
3. Coop selection shell appears.
4. Client receives battle snapshot and exact runtime catalog.
5. Client receives agent-related network packets.
6. Ordinary `CreateAgent` may be deferred until authoritative materialization data is ready.
7. Deferred `CreateAgent` packets are replayed later.
8. Follow-up packets are also replayed later:
   - `SetWieldedItemIndex`
   - `SynchronizeAgentSpawnEquipment`
   - weapon data / reload data / usage data

The design only works if replay order is strict and complete.

## 5. Exact Transfer And Materialization Model
There are effectively **three** overlapping identity systems:

1. **Vanilla network agent identity**
- native `AgentIndex`
- vanilla `CreateAgent`
- vanilla follow-up packets

2. **Battle snapshot / entry identity**
- coop entry ids
- side + troop + layout + source party identity

3. **Exact transfer identity**
- strict hero contracts
- exact weapon layouts
- exact runtime characters / shells / items

The current runtime tries to reconcile them in real time.

This is why `ShouldRequireExplicitMaterializationEntryId(...)` is critical:
- [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs:9599)

That method now gates ordinary agent exact-binding until the authoritative materialized map is non-empty.

This was the correct direction, but it only solved the **first** ordering bug.

## 6. Deferred Packet Model
The current handoff patch has separate deferred queues for many packet types.

Examples in [BattleMapSpawnHandoffPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/BattleMapSpawnHandoffPatch.cs:129):
- deferred `CreateAgent`
- deferred `SetAgentActionSet`
- deferred `AgentSetFormation`
- deferred `SynchronizeAgentSpawnEquipment`
- deferred `SetWieldedItemIndex`
- deferred weapon network data / ammo / reload / usage

Replay helpers are split by packet family.

Important examples:
- `TryReplayDeferredClientCreateAgents(...)`: [BattleMapSpawnHandoffPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/BattleMapSpawnHandoffPatch.cs:5768)
- `TryReplayDeferredClientSetWieldedItemIndex(...)`: [BattleMapSpawnHandoffPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/BattleMapSpawnHandoffPatch.cs:7030)

This means replay correctness depends on:
- queue completeness
- removal policy
- dependency order between queues
- "agent exists" checks
- rider/mount readiness checks

## 7. Current Reproducible Failure Clusters
There are two separate families of failures.

### A. Dedicated server combat hang / crash
This is the long-running bolt / mounted target issue. The latest narrowing indicates:
- not raw `HitWorld`
- not raw `Stick`
- not raw `AttachWeaponToBone`
- likely later native mounted-ranged bookkeeping after a bolt hit on mounted targets

This family is still unresolved, but it is **not** the same issue as the current client visual crash.

### B. Client side-selection / visual crash
This is the currently active blocker.

This family has evolved through several visible symptoms:
- vanilla `MPIntermissionVM` null-path
- vanilla entry UI suppression timing
- pre-authoritative ordinary exact-binding
- now: deferred replay mismatch between `CreateAgent` and follow-up packets

## 8. What The Latest Client Crash Actually Shows
Latest relevant run:
- client crash process: [watchdog_log_22060.txt](/C:/ProgramData/Mount%20and%20Blade%20II%20Bannerlord/logs/watchdog_log_22060.txt:1)
- server did not crash: [watchdog_log_54660.txt](/C:/ProgramData/Mount%20and%20Blade%20II%20Bannerlord/logs/watchdog_log_54660.txt:1)

### What is already fixed in this run
- vanilla intermission callback is suppressed: [rgl_log_22060.txt](/C:/ProgramData/Mount%20and%20Blade%20II%20Bannerlord/logs/rgl_log_22060.txt:3499)
- exact runtime bootstrap is deferred while still in selection gate: [rgl_log_22060.txt](/C:/ProgramData/Mount%20and%20Blade%20II%20Bannerlord/logs/rgl_log_22060.txt:3649)
- early authoritative snapshots are empty and ordinary `CreateAgent` is correctly deferred:
  - empty snapshot: [rgl_log_22060.txt](/C:/ProgramData/Mount%20and%20Blade%20II%20Bannerlord/logs/rgl_log_22060.txt:776372)
  - deferred `CreateAgent`: [rgl_log_22060.txt](/C:/ProgramData/Mount%20and%20Blade%20II%20Bannerlord/logs/rgl_log_22060.txt:776461)

### What changed after the new gate
- later, a non-empty authoritative snapshot arrives: [rgl_log_22060.txt](/C:/ProgramData/Mount%20and%20Blade%20II%20Bannerlord/logs/rgl_log_22060.txt:785419)
- deferred `CreateAgent` replay starts succeeding, but first success starts at `AgentIndex=98`: [rgl_log_22060.txt](/C:/ProgramData/Mount%20and%20Blade%20II%20Bannerlord/logs/rgl_log_22060.txt:783794)

### The new concrete failure
For earlier deferred agents, follow-up packets are still processed later even though the agent never materialized locally.

Examples:
- missing agents `80..86`: [rgl_log_22060.txt](/C:/ProgramData/Mount%20and%20Blade%20II%20Bannerlord/logs/rgl_log_22060.txt:785615)
- missing agents `36, 38, 40, 42, 44, 46`: [rgl_log_22060.txt](/C:/ProgramData/Mount%20and%20Blade%20II%20Bannerlord/logs/rgl_log_22060.txt:785669)

The concrete exception is:
- `TaleWorlds.Core.MBNotFoundException`
- `Agent with index ... could not be found while reading reference from packet`

This means:
- readiness gating now works
- but replay queue coupling still does not

### Secondary signal
Among replayed mounted agents, some riders exist while mount runtime tracking is still absent during mounted follow-up events:
- `MountRuntime={... Stage=absent ...}` for ordinary mounted replay: [rgl_log_22060.txt](/C:/ProgramData/Mount%20and%20Blade%20II%20Bannerlord/logs/rgl_log_22060.txt:785339)

This is weaker than the missing-agent signal, but still important.

## 9. TDM / Vanilla MP Coupling
The current runtime still inherits too much from vanilla MP / TDM assumptions:
- mission startup
- team selection lifecycle
- agent network packet ordering
- mounted lifecycle assumptions
- lobby / entry UI behavior

The coop runtime then overrides those assumptions incrementally:
- suppress a vanilla callback
- wrap a vanilla stack
- defer a packet
- replay a packet later
- rebind ordinary agents to authoritative entries

That is why the system keeps surfacing new failures after a local fix:
- each local patch corrects one violated invariant
- but other queues or callbacks still assume the original vanilla timeline

## 10. Invariants We Need To Preserve
The runtime needs these invariants to hold:

1. No ordinary exact-binding before authoritative materialized map is non-empty.
2. No follow-up packet for an agent before local `CreateAgent` has successfully materialized that agent.
3. No mounted follow-up packet before both rider and mount link are available for mounted paths that require them.
4. Vanilla intermission / entry UI must not own the flow after coop selection shell is authoritative.
5. Exact hero path and ordinary AI path must not share assumptions unless explicitly designed to.

Right now invariant `2` is the clearest active failure.

## 11. What Is Probably Wrong In The Current Design
The system currently looks like it has **queue-local correctness**, but not **global replay correctness**.

In plain terms:
- `CreateAgent` queue knows when to wait
- `SetWieldedItemIndex` queue knows to wait if `CreateAgent` is still deferred
- but after the authoritative snapshot arrives, we still end up in a state where:
  - some `CreateAgent` messages never produce a local agent
  - yet their follow-up queues continue replaying

So the likely design bug is one of:
- partial `CreateAgent` replay gap
- silent `CreateAgent` replay non-materialization without hard dependency rollback
- replay ordering that allows dependent queues to advance despite missing agents
- early removal or non-persistence of `DeferredClientCreateAgentPayload` for part of the agent range

The key observation is this:
- successful replay begins at `AgentIndex=98`
- failing follow-up packets cluster below that

That strongly suggests the issue is not random corruption. It looks like a **systematic replay gap in an early slice of deferred agents**.

## 12. Recommended Next Directions
### Short-term stabilization on current architecture
Focus on replay correctness, not more UI suppression or horse experiments.

Specifically:
- audit why some deferred `CreateAgent` entries below `98` never materialize
- make dependent queues hard-block on confirmed local agent existence
- prevent follow-up replay from running for agent indices whose `CreateAgent` never succeeded
- treat mounted rider/mount late-bind as a separate second-phase dependency

### Medium-term architecture cleanup
Split the runtime explicitly into:
- strict hero path
- ordinary AI path
- mounted ordinary AI path

Right now those paths share too much infrastructure and too many assumptions.

## 13. Suggested Clean-Core Migration Scope
If the project moves away from TDM-centered runtime patching, the clean-core target should be:

1. Keep vanilla MP transport only where needed.
2. Own selection flow completely.
3. Own ordinary agent materialization lifecycle completely.
4. Use one authoritative source for agent-entry identity.
5. Use one authoritative source for mount lifecycle after spawn.
6. Avoid replaying vanilla follow-up packets into a half-custom lifecycle unless their prerequisites are fully satisfied by coop-owned state.

That migration should be based on this runtime inventory, not started blindly.

## 14. Reference Files
- Mission wrapper and behavior injection:
  - [MissionStateOpenNewPatches.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/MissionStateOpenNewPatches.cs:235)
- Coop network bridge:
  - [CoopMissionNetworkBridge.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionNetworkBridge.cs:1064)
  - [CoopMissionNetworkBridge.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionNetworkBridge.cs:2097)
- Main mission runtime logic:
  - [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs:32)
  - [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs:9599)
  - [CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs:6491)
- Battle-map handoff and replay:
  - [BattleMapSpawnHandoffPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/BattleMapSpawnHandoffPatch.cs:5768)
  - [BattleMapSpawnHandoffPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/BattleMapSpawnHandoffPatch.cs:7030)
  - [BattleMapSpawnHandoffPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/BattleMapSpawnHandoffPatch.cs:10495)
- Exact transfer infrastructure:
  - [ExactTransferContractBuilder.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/ExactTransferContractBuilder.cs:12)
  - [ExactCreateAgentServerPreSpawnContractResolver.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/ExactCreateAgentServerPreSpawnContractResolver.cs:33)
  - [ExactCampaignRuntimeItemRegistry.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/ExactCampaignRuntimeItemRegistry.cs:14)
- UI suppression / overlay:
  - [IntermissionVmCrashGuardPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/IntermissionVmCrashGuardPatch.cs:18)
  - [VanillaEntryUiSuppressionPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/VanillaEntryUiSuppressionPatch.cs:17)
  - [CoopMissionSelectionView.cs](/C:/dev/projects/BannerlordCoopSpectator3/UI/CoopMissionSelectionView.cs:16)

