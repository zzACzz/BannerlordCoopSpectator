# Reconnect And Dedicated Crash Audit (2026-05-10)

Purpose: keep reconnect fixes and dedicated-server crash work in separate corridors, with engine links verified from `ilspycmd` instead of assumptions.

## 1. Proven engine facts from ilspy

Source files decompiled with `ilspycmd` into:
- `.ilspy_tmp/2026-05-10_reconnect_server_audit/MissionNetworkComponent.cs`
- `.ilspy_tmp/2026-05-10_reconnect_server_audit/Mission.cs`
- `.ilspy_tmp/2026-05-10_reconnect_server_audit/Agent.cs`

### 1.1 Client reconnect crash corridor is real

Vanilla `MissionNetworkComponent` handlers call `Mission.MissionNetworkHelper.GetAgentFromIndex(...)` and then immediately call agent methods with no null guard:

- `HandleServerEventSetWeaponNetworkData`
- `HandleServerEventSetWeaponAmmoData`
- `HandleServerEventSetWeaponReloadPhase`
- `HandleServerEventWeaponUsageIndexChangeMessage`
- `HandleServerEventStartSwitchingWeaponUsageIndex`
- `HandleServerEventSetAgentHealth`
- `HandleServerEventSetAgentActionSet`
- `HandleServerEventMakeAgentDead`
- `HandleServerEventAttachWeaponToWeaponInAgentEquipmentSlot`
- `HandleServerEventSynchronizeAgentEquipment`

Decompiled evidence:
- `.ilspy_tmp/2026-05-10_reconnect_server_audit/MissionNetworkComponent.cs:330-377`
- `.ilspy_tmp/2026-05-10_reconnect_server_audit/MissionNetworkComponent.cs:740-769`

Important engine behavior:
- `.ilspy_tmp/2026-05-10_reconnect_server_audit/Mission.cs:385-392`
- `Mission.MissionNetworkHelper.GetAgentFromIndex(agentIndex, canBeNull: false)` throws if the agent does not exist yet.

Meaning:
- if reconnect snapshot bootstrap has not materialized the agent locally yet,
- and a live combat message arrives early,
- vanilla client code is unsafe by default.

This is the precise reason the reconnect client crash fix belongs in client-side message deferral and replay.

### 1.2 Our reconnect patch is client-gated

`Patches/BattleMapSpawnHandoffPatch.cs` only enables the safe-string-id reconnect path through:
- `ShouldUseSafeStringIdCreateAgentPathOnClient(...)`
- `Patches/BattleMapSpawnHandoffPatch.cs:1302-1313`

That gate returns `false` on server:
- `if (GameNetwork.IsServer || mission == null) return false;`

Replay also waits for snapshot-ready state:
- `Patches/BattleMapSpawnHandoffPatch.cs:1411-1425`

Current deferred/replay list in this patch:
- `CreateAgent`
- `SetAgentActionSet`
- `SynchronizeAgentEquipment`
- `AttachWeaponToWeaponInAgentEquipmentSlot`
- `SetWeaponNetworkData`
- `SetWeaponAmmoData`
- `SetWeaponReloadPhase`
- `SetWieldedItemIndex`
- `StartSwitchingWeaponUsageIndex`
- `WeaponUsageIndexChangeMessage`
- `SetAgentHealth`
- `MakeAgentDead`

Meaning:
- reconnect crash fixes in this file are supposed to stay in the client corridor;
- they are not the place to change dedicated exact equipment behavior.

### 1.3 Dedicated exact equipment path is a separate subsystem

Dedicated exact path is enabled by:
- `Mission/CoopMissionBehaviors.cs:4192-4199`
- `UseDedicatedSafeStringIdExactEquipmentPathOnServer()`

When that dedicated-safe path is active, pre-spawn equipment injection is explicitly disabled:
- `Patches/ExactCampaignPreSpawnLoadoutPatch.cs:183-185`

So server-side flow becomes:
1. native `CreateAgent` uses a native template baseline;
2. exact equipment is applied later by post-spawn overlay;
3. overlay refresh goes through `agent.UpdateSpawnEquipmentAndRefreshVisuals(...)`.

Key post-spawn overlay entry point:
- `Mission/CoopMissionBehaviors.cs:7849-8103`

Exact server-side refresh call:
- `Mission/CoopMissionBehaviors.cs:8102`

### 1.4 `UpdateSpawnEquipmentAndRefreshVisuals(...)` is a heavy native-sensitive operation

Decompiled engine body:
- `.ilspy_tmp/2026-05-10_reconnect_server_audit/Agent.cs:3612-3632`

What it does:
- replaces `SpawnEquipment`
- if server: broadcasts `SynchronizeAgentSpawnEquipment`
- clears visual components
- calls `Mission.OnEquipItemsFromSpawnEquipment(...)`
- clears weapon meshes
- copies equipment from spawn equipment
- equips items from spawn equipment
- updates agent properties
- if not client/replay: may `WieldInitialWeapons()`
- preloads for rendering

Meaning:
- this is not a lightweight cosmetic call;
- on dedicated it changes gameplay-facing agent state and touches native agent internals.

## 2. What is proven from logs

### 2.1 Reconnect client crash

Previous reconnect logs proved:
- snapshot manifest/chunks arrived,
- but live combat messages for missing agents arrived before local materialization completed,
- and client then crashed with native `0xC0000005`.

This matches the vanilla engine facts in section 1.1.

### 2.2 Dedicated server crash in the latest failing run

Dedicated crash log:
- `C:/Users/Admin/Downloads/Telegram Desktop/watchdog_log_4872.txt:12-13`

The earliest proven divergence happens before the dedicated AV itself:
- host bad run first sends `GET /Manager/start_mission` at `08:01:00.644`
- that request times out at `08:01:03.647`
- host immediately sends a second `GET /Manager/start_mission` at `08:01:03.654`
- `C:/Users/Admin/Downloads/Telegram Desktop/rgl_log_5788.txt:3330-3346`

Dedicated bad run shows why this is unsafe:
- first `MissionState.OpenNew ENTER missionName=MultiplayerBattle` at `08:01:00.650`
- second `MissionState.OpenNew ENTER missionName=MultiplayerBattle` at `08:01:03.655`
- first `EXIT` only at `08:01:03.764`
- `C:/Users/Admin/Downloads/Telegram Desktop/rgl_log_4872.txt:2185`
- `C:/Users/Admin/Downloads/Telegram Desktop/rgl_log_4872.txt:29071`
- `C:/Users/Admin/Downloads/Telegram Desktop/rgl_log_4872.txt:29091`

So in the bad run the second HTTP retry lands while the first `MultiplayerBattle` startup is still in progress.

Good run shows the opposite ordering:
- host first `GET /Manager/start_mission` at `07:22:53.101`
- host timeout at `07:22:56.103`
- dedicated first `MissionState.OpenNew EXIT` at `07:22:56.145`
- only then does host send the second `GET /Manager/start_mission` at `07:22:56.153`
- `C:/Users/Admin/Downloads/Telegram Desktop/rgl_log_14724.txt:3325-3341`
- `C:/Users/Admin/Downloads/Telegram Desktop/rgl_log_12332.txt:2291`
- `C:/Users/Admin/Downloads/Telegram Desktop/rgl_log_12332.txt:29203`

This proves:
- the dangerous duplication is tied specifically to `start_mission` timeout retry while the first mission open is still running;
- not to reconnect client replay itself.

What happened before the crash:
- battle bootstrap completed
- exact runtime state sync completed
- mission entered `PreBattleHold`
- payload transmissions to clients were already happening
- repeated `battle phase AI hold state applied` logs continued

Relevant lines:
- `C:/Users/Admin/Downloads/Telegram Desktop/rgl_log_4872.txt:38499`
- `C:/Users/Admin/Downloads/Telegram Desktop/rgl_log_4872.txt:38505`
- `C:/Users/Admin/Downloads/Telegram Desktop/rgl_log_4872.txt:38513`
- `C:/Users/Admin/Downloads/Telegram Desktop/rgl_log_4872.txt:38525`
- `C:/Users/Admin/Downloads/Telegram Desktop/rgl_log_4872.txt:38548`
- `C:/Users/Admin/Downloads/Telegram Desktop/rgl_log_4872.txt:38633`

Last local managed line before crash is from:
- `Mission/CoopMissionBehaviors.cs:8945-8975`

That method only sets:
- `mission.PauseAITick = shouldPauseAi;`

Engine property body:
- `.ilspy_tmp/2026-05-10_reconnect_server_audit/Mission.cs:1180-1188`

Meaning:
- the dedicated AV is not proven to be caused by the `PauseAITick` assignment itself;
- the visible crash window is later native mission ticking after bootstrap.

## 3. What is proven vs. what is still not proven

### Proven

- Reconnect client crash is caused by live server messages reaching vanilla client handlers before the referenced agent exists locally.
- `BattleMapSpawnHandoffPatch.cs` is the correct corridor for reconnect hardening.
- Dedicated exact equipment path is a separate corridor from reconnect message deferral.
- Dedicated bad run enters `MissionState.OpenNew("MultiplayerBattle", ...)` twice because `DedicatedServerCommands.TrySendCommandViaHttp("start_mission")` retries after a timeout while the first mission open is still active.
- Dedicated crash in the failing run happens after exact bootstrap and after `PreBattleHold`, not during first-entry handshake and not during the earlier `FinishedLoading` mismatch.

### Not yet proven

- The exact native call that kills the dedicated server.
- Whether the dedicated AV is caused directly by post-spawn exact overlay, by mounted exact overlay specifically, or by a later native tick that only becomes unsafe because of prior overlay state.

## 4. Safe change boundary rules

When fixing reconnect client crash:
- only touch client-side deferral/replay in `Patches/BattleMapSpawnHandoffPatch.cs`;
- keep all new guards behind `ShouldUseSafeStringIdCreateAgentPathOnClient(...)`;
- do not mix reconnect fixes with server exact overlay changes unless a dedicated-only proof exists.

When investigating dedicated server crash:
- do not assume reconnect deferral changes are the cause without A/B proof;
- first protect `start_mission` from timeout-driven duplicate `OpenNew`;
- only after that instrument or feature-flag server-side exact overlay separately if a dedicated crash still remains;
- keep dedicated exact experiments isolated from client reconnect logic.

## 5. Recommended next proof steps before server-side fixes

1. Protect `start_mission` timeout path from issuing a second `/Manager/start_mission` while the first `MissionState.OpenNew` is still active.
   - Narrowest safe rule: treat `start_mission` timeout as "probably accepted/in progress" and suppress only the timeout retry, not all HTTP retries.

2. If dedicated still falls after that, add dedicated-only diagnostics around server exact overlay, not around reconnect replay.
   - Entry/exit logs around `TryApplyExactCampaignSnapshotOverlayToNativeAgent(...)`
   - Per-agent logs before and after `UpdateSpawnEquipmentAndRefreshVisuals(...)`
   - Separate mounted vs non-mounted overlay markers

3. If needed, add a temporary dedicated-only kill switch for mounted exact post-spawn overlay to isolate cavalry/mount involvement.

## 6. Working conclusion as of 2026-05-10

Data is sufficient to continue fixing reconnect client crashes safely in the client handoff corridor.

Data is also sufficient to apply one narrow dedicated fix with confidence: prevent timeout-driven duplicate `start_mission` from reopening `MultiplayerBattle` while the first open is still in progress.

If a dedicated AV still remains after that fix, the next proof target becomes server-side exact post-spawn overlay instrumentation.
