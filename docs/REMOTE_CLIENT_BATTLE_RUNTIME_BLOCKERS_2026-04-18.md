# Remote Client Battle Runtime Blockers (2026-04-18)

## Scope

This note is limited to the fresh host/dedicated/remote-client rerun from:

- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_25640.txt`
- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_56200.txt`
- `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\logs\rgl_log_57248.txt`
- `C:\Users\Admin\Downloads\VVS logs\rgl_log_4864.txt`
- `C:\Users\Admin\Downloads\VVS logs\watchdog_log_4864.txt`
- `C:\Users\Admin\Downloads\VVS logs\CrashUploader.7984.txt`

This is not a new broad runtime rewrite plan. It only records the lowest-level blockers now backed by code and logs.

## Confirmed Findings

### 1. Remote client test used a stale package, not the current branch build

The reported remote-client run was not using the same client DLL that is now built from the branch.

Current built client DLL after the transport restore and payload diagnostics:

- `C:\dev\projects\BannerlordCoopSpectator3\Module\CoopSpectator\bin\Win64_Shipping_Client\CoopSpectator.dll`
- SHA256: `5471449E0E293B8D011DD7703473838AFF24E5DDB7668F99D3E9FAAFFC9B99F5`

Old `dist` client package DLL before the refresh:

- `C:\dev\projects\BannerlordCoopSpectator3\dist\CoopSpectator_ClientPackage\Modules\CoopSpectator\bin\Win64_Shipping_Client\CoopSpectator.dll`
- SHA256: `2C64C3473E0F41F464E342520831A79B028D10BA06BCAA75A59B5705321AC661`

The package has now been refreshed, and the `dist` DLL hash matches the current branch build:

- `C:\dev\projects\BannerlordCoopSpectator3\dist\CoopSpectator_ClientPackage\Modules\CoopSpectator\bin\Win64_Shipping_Client\CoopSpectator.dll`
- SHA256: `5471449E0E293B8D011DD7703473838AFF24E5DDB7668F99D3E9FAAFFC9B99F5`

This means the original remote-client logs remain useful for blocker identification, but not as validation of the newly restored transport path.

### 2. Dedicated had a real patch-apply blocker during battle startup

In dedicated log `rgl_log_57248.txt`, multiple Harmony patch applications fail with:

- `System.MissingMethodException: Method not found: 'Void System.Reflection.Emit.ILGenerator.MarkSequencePoint(...)'`

The same log also shows:

- `SelectedDMDType=MonoMod.Utils.DMDCecilGenerator`

That is consistent with the dedicated runtime compat choosing the wrong DMD generator for this environment.

Affected dedicated patch groups in that log include:

- `MissionStateOpenNewPatches`
- `ExactCampaignArmyBootstrapPatch`
- `ExactCampaignPreSpawnLoadoutPatch`
- multiple `BattleMapSpawnHandoffPatch` hooks
- `BattleShellSuppressionPatch`
- `MultiplayerHeroClassOverridePatch`
- `MultiplayerCharacterClassFallbackPatch`
- `ServerChangeCultureCanonicalizationPatch`
- `CampaignCombatProfileAgentStatsPatch.ApplyWeaponDamageOnly`

This is sufficient to explain broken remote battle materialization/equipment state.

### 3. The reported run had no authoritative remote battle transport on the active dedicated runtime

The reported remote client log shows:

- `CoopBattleNetworkRequestTransport: sent client request. Kind=Spectate ...`

The dedicated log from the same run shows:

- `Handler not found for network message CoopSpectator.Network.Messages.CoopBattleSelectionClientRequestMessage`

That is a low-level proof that the remote peer did send the custom selection request, but the active server runtime for that test did not have the corresponding handler registered. That is sufficient to explain `0 selectable` and the lack of authoritative remote battle-entry state.

### 4. Source now contains a restored authoritative network transport, but it still needs a fresh rerun

The branch now restores a narrow network bridge in:

- `Mission/CoopMissionNetworkBridge.cs`

The restored path does the following:

- registers the client payload handler
- registers the server selection handler
- syncs entry-status snapshots to a specific peer
- syncs battle snapshots to a specific peer
- applies payload chunks on the client into:
  - `CoopBattleEntryStatusBridgeFile`
  - `BattleSnapshotRuntimeState`

Supporting source changes in this same session:

- `UI/CoopMissionSelectionView.cs`
  - selection/spectate/spawn requests now go through `CoopBattleNetworkRequestTransport`
- `GameMode/MissionMultiplayerCoopBattleMode.cs`
  - appends `CoopMissionNetworkBridge` to server/client battle stacks
- `Patches/MissionStateOpenNewPatches.cs`
  - appends `CoopMissionNetworkBridge` to wrapped battle and TDM mission stacks
- `Mission/CoopMissionBehaviors.cs`
  - restored peer-addressed selection/status/spawn seams for the transport layer
- `DedicatedServer/CoopSpectatorDedicated.csproj`
  - now includes `..\Mission\CoopMissionNetworkBridge.cs`

So the transport gap identified by the logs has been addressed in source and build artifacts. What remains is a fresh validation rerun.

### 5. Reported symptoms match the current gap

The user-reported symptoms are consistent with the findings above:

- `0 selectable` in team selection: remote client has no authoritative selectable-entry snapshot locally
- naked units / missing visible weapons in spectator: remote client lacks fully applied battle snapshot/equipment/runtime state
- crash near early battle initialization: remote client crash is native (`0xC0000005`), and happened while battle state was already inconsistent

The crash exists, but the missing remote authoritative battle state must be treated as the first blocker before trying to overfit a crash-only fix.

## Changes Applied In This Session

### Dedicated runtime compat

Adjusted dedicated Harmony runtime compat in:

- `C:\dev\projects\BannerlordCoopSpectator3\DedicatedServer\SubModule.cs`

The preferred DMD generator order is now:

1. `MonoMod.Utils.DMDEmitDynamicMethodGenerator`
2. `MonoMod.Utils.DMDEmitMethodBuilderGenerator`
3. `MonoMod.Utils.DMDCecilGenerator`

Reason: dedicated logs showed `DMDCecilGenerator` correlating with `ILGenerator.MarkSequencePoint` patch-apply failures.

### Remote battle transport restored

Restored the authoritative network bridge and transport wiring in:

- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionNetworkBridge.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\UI\CoopMissionSelectionView.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\GameMode\MissionMultiplayerCoopBattleMode.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Patches\MissionStateOpenNewPatches.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\DedicatedServer\CoopSpectatorDedicated.csproj`

Reason: the reported logs proved a broken server-side contract for `CoopBattleSelectionClientRequestMessage`, and the current branch needed the smallest possible authoritative transport restore rather than another broad runtime rewrite.

### Artifacts refreshed

Rebuilt:

- `dotnet build .\CoopSpectator.csproj -c Debug`
- `dotnet build .\DedicatedServer\CoopSpectatorDedicated.csproj -c Debug`

Note: one parallel build attempt hit a temporary Defender lock on `DedicatedServer\obj\Debug\CoopSpectator.dll`; a sequential dedicated build succeeded afterward.

Refreshed:

- `C:\dev\projects\BannerlordCoopSpectator3\dist\CoopSpectator_ClientPackage`
- `C:\dev\projects\BannerlordCoopSpectator3\dist\CoopSpectator_ClientPackage.zip`

After refresh, `dist` client DLL hash matches current built client DLL:

- SHA256: `5471449E0E293B8D011DD7703473838AFF24E5DDB7668F99D3E9FAAFFC9B99F5`

## Current Blockers

### Blocker A: dedicated patch-apply rerun needed

Need a fresh dedicated rerun to confirm the new log line shows an emit-based DMD generator and that the earlier patch-apply failures are gone.

### Blocker B: restored remote battle transport needs live validation

The transport is now present in source and the package is refreshed, but there is still no fresh rerun proving the complete chain:

- server registers `CoopMissionNetworkBridge`
- remote peer request is handled
- peer receives entry-status snapshot
- peer receives battle snapshot
- selection UI becomes populated
- equipment/materialization on the remote observer is no longer degraded

### Blocker C: crash should be re-evaluated only after A and B are separated

The native client crash should be rechecked after:

- remote client uses the refreshed package
- dedicated applies battle patches cleanly

Otherwise the crash signal is mixed with stale binaries and incomplete authoritative battle state.

## Next Smallest Steps

1. Rerun with the refreshed `dist` client package and the updated dedicated module.
2. Confirm dedicated startup log now selects an emit-based DMD generator and no longer logs the earlier patch-apply failures.
3. Capture fresh remote-client logs from the new package.
4. Confirm the new authoritative lines appear:
   - `CoopMissionNetworkBridge: registered server selection request handler.`
   - `CoopMissionNetworkBridge: registered client payload chunk handler.`
   - one server-side line handling the remote request
   - one server-side line sending payloads to the peer
   - one client-side line applying entry-status and battle-snapshot payloads
5. Only if the fresh rerun still shows `0 selectable` / missing equipment, continue narrowing the remaining runtime delta.

Do not re-open spawn/perk parity rewrites until the remote transport contract is restored and proven by logs.

## Addendum: Follow-Up Rerun And Narrow Fixes

This addendum records the next fresh rerun and the smallest new fixes applied after reviewing:

- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_53944.txt`
- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_52276.txt`
- `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\logs\rgl_log_44096.txt`
- `C:\Users\Admin\Downloads\VVS logs\rgl_log_15248.txt`
- `C:\Users\Admin\Downloads\VVS logs\watchdog_log_15248.txt`

### Addendum Finding 1. Dedicated DMD override existed in logs but was not actually honored by Harmony

Dedicated log `rgl_log_44096.txt` still showed:

- `CoopSpectatorDedicated: configured Harmony runtime compat... SelectedDMDType=MonoMod.Utils.DMDEmitDynamicMethodGenerator`
- followed by the same `ILGenerator.MarkSequencePoint(...)` patch-apply failures

Low-level decompile of the shipped `0Harmony.dll` (`Lib.Harmony 2.4.2`) showed why:

- `DynamicMethodDefinition.Generate()` reads the `DMDType` switch as a string token / alias
- our code was passing a `System.Type` object into `Harmony.SetSwitch("DMDType", ...)`

So the previous log line looked correct, but Harmony silently ignored the override and fell back to its native generator selection path.

Applied fix:

- `C:\dev\projects\BannerlordCoopSpectator3\DedicatedServer\SubModule.cs`
  - now sets `DMDType` using recognized string aliases:
    - `dynamicmethod`
    - `methodbuilder`
    - `cecil`
  - logs both `SelectedDMDType=` and `SelectedDMDSwitch=`

### Addendum Finding 2. Entry-status transport was self-flooding on the server

Dedicated log `rgl_log_44096.txt` showed thousands of lines like:

- `CoopMissionNetworkBridge: sent payload to peer. Peer=... Kind=EntryStatusSnapshot TransmissionId=...`

Source review identified a concrete cause:

- `Mission/CoopMissionBehaviors.cs`
  - `BuildEntryStatusSnapshotForPeer(...)` sets `UpdatedUtc = DateTime.UtcNow`
- `Mission/CoopMissionNetworkBridge.cs`
  - dedupe compared the fully serialized JSON payload string

Because `UpdatedUtc` changed every tick, the payload never stabilized, dedupe never hit, and the dedicated kept re-sending `EntryStatusSnapshot` continuously.

Applied fix:

- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionNetworkBridge.cs`
  - dedupe for `EntryStatusSnapshot` now compares a normalized serialization that ignores `UpdatedUtc`
  - the actual payload still keeps the real timestamp for client-side freshness checks

### Addendum Finding 3. Initial sync was also duplicating battle/status sends during peer synchronization

Dedicated log `rgl_log_44096.txt` showed duplicated initial sends on the same peer:

- two `BattleSnapshot` transmissions for `AC`
- two `BattleSnapshot` transmissions for `XCTwnik`

The sync callback was force-clearing the per-peer dedupe cache and then force-sending again, which duplicated startup traffic when the server tick had already emitted the initial payload.

Applied fix:

- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionNetworkBridge.cs`
  - `HandleNewClientAfterSynchronized(...)` no longer clears the peer dedupe cache or force-resends identical startup payloads

### Addendum Finding 4. The receive side still needed one more authoritative diagnostic

Client log `rgl_log_15248.txt` showed:

- `CoopMissionNetworkBridge: registered client payload chunk handler.`

but still did not show any payload-apply line. To narrow the next rerun without broad spam, the bridge now logs exactly one authoritative receive step per transmission:

- `received first payload chunk`
- `assembled client payload`
- existing `applied client payload`

Applied fix:

- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionNetworkBridge.cs`

### Updated artifacts after the addendum fixes

Rebuilt again:

- `dotnet build .\CoopSpectator.csproj -c Debug`
- `dotnet build .\DedicatedServer\CoopSpectatorDedicated.csproj -c Debug`

Refreshed again:

- `C:\dev\projects\BannerlordCoopSpectator3\dist\CoopSpectator_ClientPackage`
- `C:\dev\projects\BannerlordCoopSpectator3\dist\CoopSpectator_ClientPackage.zip`

Current client DLL hash after this addendum refresh:

- built DLL SHA256: `DD4B3187F98D8A61EA73E09CED2FD8CEE87C0CCC8CDB7FF7E96182EB5C6D1C7A`
- `dist` DLL SHA256: `DD4B3187F98D8A61EA73E09CED2FD8CEE87C0CCC8CDB7FF7E96182EB5C6D1C7A`

### Updated smallest next rerun checks

1. In dedicated log, confirm:
   - `SelectedDMDSwitch=dynamicmethod` or `SelectedDMDSwitch=methodbuilder`
   - no repeat of the old `ILGenerator.MarkSequencePoint(...)` patch-apply failures
2. In client log, confirm the new chain:
   - `registered client payload chunk handler`
   - `received first payload chunk`
   - `assembled client payload`
   - `applied client payload`
3. Only after that, re-evaluate any remaining:
   - `0 selectable`
   - missing equipment / naked units
   - spawn/materialization failure
   - crash

## Addendum: Dedicated Dump-Backed Crash Site And Runtime Asset Fix

This addendum records the next fresh rerun and the narrow fixes applied after reviewing:

- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_49448.txt`
- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_51588.txt`
- `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\logs\rgl_log_53784.txt`
- `C:\Users\Admin\Downloads\VVS logs\rgl_log_2636.txt`
- `C:\Users\Admin\Downloads\VVS logs\watchdog_log_2636.txt`
- `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\crashes\2026-04-18_17.26.47\dump.dmp`

### Addendum Finding 5. Dedicated crash site is the initial `EntryStatusSnapshot` send from `HandleNewClientAfterSynchronized(...)`

The dedicated log stopped after:

- `CoopMissionNetworkBridge: sent payload to peer. Peer=XCTwnik Kind=BattleSnapshot TransmissionId=1 Bytes=39283 Chunks=44`

The dedicated dump then narrowed the actual crash site. `dumpinspect` on:

- `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\crashes\2026-04-18_17.26.47\dump.dmp`

showed the crashing managed thread inside:

- `CoopSpectator.MissionBehaviors.CoopMissionNetworkBridge.TrySendPayload(...)`
- `CoopSpectator.MissionBehaviors.CoopMissionNetworkBridge.TrySendEntryStatusToPeer(...)`
- `CoopSpectator.MissionBehaviors.CoopMissionNetworkBridge.HandleNewClientAfterSynchronized(...)`
- native write path: `CoopBattlePayloadChunkMessage.OnWrite -> GameNetworkMessage.WriteByteArrayToPacket(...)`

So the server was not dying on an abstract “after join” boundary. It was dying while trying to emit the immediate post-sync `EntryStatusSnapshot` chunk stream from the synchronized callback.

Applied fix:

- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionNetworkBridge.cs`
  - removed direct payload sends from `HandleNewClientAfterSynchronized(...)`
  - left initial payload delivery to the already authoritative `OnUdpNetworkHandlerTick()` sync path
  - added a single authoritative log:
    - `deferred initial payload sync to UDP tick`

### Addendum Finding 6. Dedicated was loading the wrong `0Harmony.dll` runtime asset

Low-level inspection showed:

- dedicated starter runtime:
  - `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Dedicated Server\bin\Win64_Shipping_Server\TaleWorlds.Starter.DotNetCore.runtimeconfig.json`
  - `tfm = net6.0`
- our dedicated module output before this fix still carried:
  - `0Harmony.dll` with target hint `.NETFramework,Version=v4.7.2`

That mismatch cleanly explains why the dedicated runtime still produced:

- `MissingMethodException: Void System.Reflection.Emit.ILGenerator.MarkSequencePoint(...)`

even after the `DMDType=dynamicmethod` switch was set correctly.

Applied fix:

- `C:\dev\projects\BannerlordCoopSpectator3\DedicatedServer\CoopSpectatorDedicated.csproj`
  - added `DedicatedHarmonyRuntimeAsset=$(NuGetPackageRoot)lib.harmony\2.4.2\lib\net6.0\0Harmony.dll`
  - added a dedicated-only post-build override that replaces the output/deployed `0Harmony.dll` with the `net6.0` asset before deploy diagnostics and deploy copy

Verified after rebuild:

- local dedicated module `0Harmony.dll` target hint = `.NETCoreApp,Version=v6.0`
- deployed dedicated module `0Harmony.dll` target hint = `.NETCoreApp,Version=v6.0`
- both hashes = `12EED32DE73312B376F616EE8659BA5D3766D96859181728437FA85C6B5CE309`

### Updated artifacts after the dump-backed fixes

Rebuilt again:

- `dotnet build .\DedicatedServer\CoopSpectatorDedicated.csproj -c Debug /p:UseDedicatedServerRefs=true`
- `dotnet build .\CoopSpectator.csproj -c Debug`

Refreshed again:

- `C:\dev\projects\BannerlordCoopSpectator3\dist\CoopSpectator_ClientPackage`
- `C:\dev\projects\BannerlordCoopSpectator3\dist\CoopSpectator_ClientPackage.zip`

Current client DLL hash after this addendum refresh:

- built DLL SHA256: `EC0802F85F8CDA6B26A1E2C99B50B5AA959878BED1BA8F6F89D93B3B9A8C18EC`
- `dist` DLL SHA256: `EC0802F85F8CDA6B26A1E2C99B50B5AA959878BED1BA8F6F89D93B3B9A8C18EC`

### Updated smallest next rerun checks

1. In dedicated log, confirm:
   - `Runtime Harmony override copied`
   - `SelectedDMDSwitch=dynamicmethod`
   - no repeat of the old `ILGenerator.MarkSequencePoint(...)` patch-apply failures
2. In dedicated log, confirm the new startup join path:
   - `deferred initial payload sync to UDP tick`
   - then normal `sent payload to peer` lines from UDP tick, not from the synchronize callback
3. In remote client log, confirm:
   - `received first payload chunk`
   - `assembled client payload`
   - `applied client payload`
4. Only after that, re-evaluate any remaining:
   - `0 selectable`
   - missing equipment / naked units
   - host spawn/materialization

## Addendum: Payload Chunk Read Contract And Remapped Team Deployment Plan Fix

Log set for this addendum:

- `C:\Users\Admin\Downloads\VVS logs\rgl_log_13032.txt`
- `C:\Users\Admin\Downloads\VVS logs\watchdog_log_13032.txt`
- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_33920.txt`
- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_57648.txt`
- `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\logs\rgl_log_42824.txt`

### Addendum Finding 7. `CoopBattlePayloadChunkMessage` used the wrong native byte-array packet contract

The latest run narrowed the remote `0 selectable` blocker from a generic transport suspicion to a concrete packet read/write mismatch.

Authoritative evidence from the logs:

- dedicated did send the payload stream:
  - `CoopMissionNetworkBridge: sent payload to peer. Peer=XCTwnik Kind=BattleSnapshot TransmissionId=2 Bytes=40534 Chunks=46`
  - `CoopMissionNetworkBridge: sent payload to peer. Peer=XCTwnik Kind=EntryStatusSnapshot TransmissionId=4 Bytes=3814 Chunks=5`
- remote client had the bridge behavior and handler registration:
  - `CoopMissionNetworkBridge: registered client payload chunk handler.`
- but remote client never logged:
  - `received first payload chunk`
  - `assembled client payload`
  - `applied client payload`

Decompile of native byte-array messages showed the exact contract:

- native `WriteByteArrayToPacket(...)` already serializes the byte-array payload in the form expected by native `ReadByteArrayFromPacket(...)`
- native messages such as `SendVoiceToPlay` and `SendVoiceRecord` do not write a separate custom length field before `WriteByteArrayToPacket(...)`

Our custom `CoopBattlePayloadChunkMessage` was doing both:

- writing a manual `payloadLength` field
- then calling `WriteByteArrayToPacket(...)`
- then reading the manual length field
- then calling `ReadByteArrayFromPacket(...)`

That double-length contract does not match native behavior, so the client silently rejected the packet before it ever reached `HandleServerPayloadChunk(...)`.

Applied fix:

- `C:\dev\projects\BannerlordCoopSpectator3\Network\Messages\CoopBattleSelectionNetworkMessages.cs`
  - removed the extra manual chunk-length field from `CoopBattlePayloadChunkMessage`
  - switched `OnRead()` to the native pattern:
    - allocate `MaxChunkBytes`
    - call `ReadByteArrayFromPacket(...)`
    - trim to the exact returned byte count

Expected impact on next rerun:

- remote client should finally log:
  - `received first payload chunk`
  - `assembled client payload`
  - `applied client payload`
- if that happens, `0 selectable` should stop being blocked by missing payload delivery and move to whatever the next narrower issue is, if any

### Addendum Finding 8. Native bootstrap was still missing a deployment plan entry for remapped `team #0`

The host materialization failure is now also narrowed to a concrete contract mismatch before agent creation.

Authoritative evidence from the dedicated log:

- immediately before the exception, the mission team snapshot showed:
  - `Teams=[#0 Side=Attacker ...; #1 Side=Attacker ...; #2 Side=Defender ...]`
- but the deployment plan snapshot still showed only:
  - `TeamPlans=[Team=#1/Attacker Plan=DefaultTeamDeploymentPlan, Team=#2/Defender Plan=DefaultTeamDeploymentPlan]`
- then native bootstrap threw:
  - `ExactCampaignArmyBootstrap: initialization failed with exception ...`
  - stack root:
    - `TaleWorlds.MountAndBlade.MissionAgentSpawnLogic.Init(Boolean spawnDefenders, Boolean spawnAttackers, MissionSpawnSettings& reinforcementSpawnSettings)`

Decompile of `MissionAgentSpawnLogic.Init(...)` showed that native init iterates every `Mission.Teams` entry and calls:

- `_deploymentPlan.SetSpawnWithHorses(team, spawnWithHorses);`

So once our runtime had a remapped `team #0` with `Side=Attacker`, native init expected a matching deployment-plan entry for that team as well. Because only `#1` and `#2` had plan entries, the remapped team path was still broken before materialization.

Applied fix:

- `C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\ExactCampaignArmyBootstrap.cs`
  - after `PushInitTeamSideSanitization(...)`, re-runs `TryEnsureDeploymentPlanTeamPlans(...)` under the sanitized team-side view
  - includes the post-sanitization result in the pre-init contract diagnostics via:
    - `DeploymentPlanBridge={... PostSanitization={...}}`

Expected impact on next rerun:

- dedicated should stop throwing `MissionAgentSpawnLogic.Init(...)` null-reference during bootstrap
- the pre-init diagnostics should now show a post-sanitization bridge line that covers the remapped team, typically with `#0/Attacker`
- host-side materialization can then move forward far enough to prove whether there is any next blocker after bootstrap

### Updated artifacts after the packet-contract and deployment-plan fixes

Rebuilt again:

- `dotnet build .\CoopSpectator.csproj -c Debug`
- `dotnet build .\DedicatedServer\CoopSpectatorDedicated.csproj -c Debug /p:UseDedicatedServerRefs=true`

Refreshed again:

- `C:\dev\projects\BannerlordCoopSpectator3\dist\CoopSpectator_ClientPackage`
- `C:\dev\projects\BannerlordCoopSpectator3\dist\CoopSpectator_ClientPackage.zip`

Current client DLL hash after this addendum refresh:

- built DLL SHA256: `B2C803622C42365351315CD497F26BF2F5A2F100A08EB08CCD67B2C7644836F3`
- `dist` DLL SHA256: `B2C803622C42365351315CD497F26BF2F5A2F100A08EB08CCD67B2C7644836F3`

### Updated smallest next rerun checks

1. In remote client log, confirm the first payload is now actually read:
   - `received first payload chunk`
   - `assembled client payload`
   - `applied client payload`
2. In dedicated log, confirm the pre-init bridge now covers the remapped team:
   - `DeploymentPlanBridge={... PostSanitization={added-missing-team-plans ... #0/Attacker ...}}`
3. In dedicated log, confirm native bootstrap no longer throws:
   - no repeat of `MissionAgentSpawnLogic.Init(...)` null-reference
   - no repeat of `ExactCampaignArmyBootstrap: initialization failed with exception`
4. Only after those two fixes are confirmed live, re-evaluate any residual:
   - remote `0 selectable`
   - missing materialization
   - host falling back to spectator

### Addendum Finding 9. The next dedicated crash was on the first `BattleSnapshot` write, and the remote peer was still on the older client DLL

Fresh authoritative artifacts from the next run:

- host logs:
  - `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_59320.txt`
  - `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_53268.txt`
- dedicated log:
  - `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\logs\rgl_log_57932.txt`
- dedicated dump:
  - `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\crashes\2026-04-18_18.14.55\dump.dmp`
- remote logs:
  - `C:\Users\Admin\Downloads\VVS logs\rgl_log_13032.txt`
  - `C:\Users\Admin\Downloads\VVS logs\watchdog_log_13032.txt`

#### 9a. The dedicated crash had moved out of bootstrap and into the first staged payload send

The dedicated log showed the new post-sync path was reached cleanly:

- `Server: AC is now synchronized.`
- `CoopMissionNetworkBridge: deferred initial payload sync to UDP tick. Peer=AC Reason=post-synchronize callback safety.`

The log then stopped before any successful payload-send completion line.

The dedicated dump resolved the exact managed crash stack:

- `ManagedCallbacks.ScriptingInterfaceOfIMBNetwork.WriteByteArrayToPacket(Byte[], Int32, Int32)`
- `TaleWorlds.MountAndBlade.Network.Messages.GameNetworkMessage.WriteByteArrayToPacket(Byte[], Int32, Int32)`
- `CoopSpectator.Network.Messages.CoopBattlePayloadChunkMessage.OnWrite()`
- `CoopSpectator.MissionBehaviors.CoopMissionNetworkBridge.TrySendPayload(...)`
- `CoopSpectator.MissionBehaviors.CoopMissionNetworkBridge.TrySendBattleSnapshotToPeer(...)`
- `CoopSpectator.MissionBehaviors.CoopMissionNetworkBridge.TrySyncBattleSnapshotPayloads()`
- `CoopSpectator.MissionBehaviors.CoopMissionNetworkBridge.OnUdpNetworkHandlerTick()`

`clrstack -a` for the crashing thread also exposed the exact first-send values:

- `payloadKind = BattleSnapshot`
- `chunkIndex = 0`
- `chunkCount = 46`
- `size = 0x380 = 896`

So the live blocker was no longer the earlier `MissionAgentSpawnLogic.Init(...)` failure. The server was now crashing on the first large `BattleSnapshot` chunk write during the normal UDP sync loop.

#### 9b. The remote `0 selectable` result from this run was not from the newest package

The host and remote client binaries did not match in this run.

Host logs showed the current build:

- `CLIENT_BINARY_ID ... MVID=cabdf3e6-199a-4340-ad51-ad556ec1fe95 LastWriteUtc=2026-04-18T18:05:24.9727699Z`

Remote log still showed the older client DLL:

- `CLIENT_BINARY_ID ... MVID=ba5ae4a2-2432-4af7-aed5-28a7e283aea3 LastWriteUtc=2026-04-18T17:39:40.0000000Z`

That means the remote `0 selectable` observation from this particular run still came from the older client package and could not yet validate or invalidate the latest transport-side fixes.

#### Applied fix after the dump-backed crash analysis

`CoopBattlePayloadChunkMessage` and `CoopMissionNetworkBridge` were narrowed again to a smaller and slower staged transport:

- `C:\dev\projects\BannerlordCoopSpectator3\Network\Messages\CoopBattleSelectionNetworkMessages.cs`
  - reduced `MaxChunkBytes` from `896` to `256`
- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionNetworkBridge.cs`
  - replaced instant full-payload dump with queued per-peer transmissions
  - sends only `2` chunks per payload kind per UDP tick
  - logs one authoritative line when a transmission is queued:
    - `queued payload transmission`
  - logs one authoritative line when the transmission completes:
    - `completed payload transmission`

The intent of this change is narrow:

- avoid the first large `896-byte` `BattleSnapshot` write that the dedicated dump proved was crashing
- keep the transport authoritative and in-network
- avoid broad logging spam while still exposing queue/start-complete boundaries

#### Updated artifacts after the staged-chunk transport fix

Rebuilt again:

- `dotnet build .\CoopSpectator.csproj -c Debug`
- `dotnet build .\DedicatedServer\CoopSpectatorDedicated.csproj -c Debug /p:UseDedicatedServerRefs=true`

Refreshed again:

- `C:\dev\projects\BannerlordCoopSpectator3\dist\CoopSpectator_ClientPackage`
- `C:\dev\projects\BannerlordCoopSpectator3\dist\CoopSpectator_ClientPackage.zip`

Current client DLL hash after this addendum refresh:

- built DLL SHA256: `4B60B5D3B1C90940839FB62809758616022B41C8D081AD3A59C996256AB2DB7A`
- `dist` DLL SHA256: `4B60B5D3B1C90940839FB62809758616022B41C8D081AD3A59C996256AB2DB7A`
- `dist` zip SHA256: `743A51E17625EF558271375F8393600014C9CCCEF22FC5709298DF4C75CC5497`

#### Updated smallest next rerun checks

1. First prove the remote machine is finally on the refreshed package:
   - remote `CLIENT_BINARY_ID` should no longer show the older `MVID=ba5ae4a2-2432-4af7-aed5-28a7e283aea3`
   - the refreshed package should report `MVID=bfcfeb49-0978-40e3-ba9b-6135b9c86c04` and `LastWriteUtc=2026-04-18T18:28:18.9707763Z`
2. In dedicated log, prove the large first-write crash is gone:
   - `queued payload transmission`
   - no crash immediately after the first queued `BattleSnapshot`
   - `completed payload transmission`
3. In remote client log, prove the payload is finally being assembled on the new package:
   - `received first payload chunk`
   - `assembled client payload`
   - `applied client payload`
4. Only after those checks pass, re-evaluate any remaining:
   - remote `0 selectable`
   - missing army materialization
   - host fallback to spectator

### Addendum Finding 10. Battle completion was authoritative, but dead peers could still cross over to the enemy during `BattleActive`

Fresh authoritative artifacts from the successful connectivity/spawn run:

- host logs:
  - `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_46496.txt`
  - `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_61340.txt`
- dedicated log:
  - `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\logs\rgl_log_54376.txt`
- remote logs:
  - `C:\Users\Admin\Downloads\VVS logs\rgl_log_14388.txt`
  - `C:\Users\Admin\Downloads\VVS logs\watchdog_log_14388.txt`

#### 10a. Dedicated already detected battle completion correctly

The dedicated log proved that the authoritative mission-side completion logic was firing:

- `CoopMissionSpawnLogic: battle completion audit. AttackerActive=0 DefenderActive=7`
- `CoopBattlePhaseRuntimeState: phase updated. Phase=BattleEnded`
- `CoopBattleResultBridgeFile: wrote result ... WinnerSide=Defender`
- `CoopMissionSpawnLogic: authoritative battle completion detected. WinnerSide=Defender ... AwaitingHostEndMission=True.`
- `Multiplayer game mission ending`

So the primary failure was not "the server never detected the winner". The authoritative completion path and writeback bridge were already alive in this run.

#### 10b. The real divergence was a mid-battle side-switch after death

The dedicated log showed the host peer (`AC`) die on the attacker side:

- `CoopMissionSpawnLogic: peer returned to respawnable state ... Peer=AC`

Then, while the phase was still `BattleActive`, the same peer was allowed to switch onto the defender side:

- `CoopBattleAuthorityState: side request updated. Peer=AC PreviousRequestedSide=Attacker RequestedSide=Defender`
- `CoopBattleAuthorityState: authoritative side assigned. Peer=AC PreviousSide=Attacker Side=Defender`
- `CoopMissionSpawnLogic: materialized army replace-bot succeeded. Peer=AC ... PendingEntryId=defender|...`

The remote client log mirrored the same contract drift:

- client sent `CoopBattleSelectionClientRequest Kind=SelectSide Side=Defender`
- the next applied payload showed `AssignedSide=Defender ... CanRespawn=True`

That explains the player-visible symptom. The battle *did* end authoritatively, but before that the dead host peer could cross from the defeated attacker side into the living defender roster and continue materializing inside the enemy army.

#### Applied fix after the log-backed side-switch analysis

The fix stays narrow and authoritative:

- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`
  - rejects cross-side selection during `BattleActive` before `TryRequestSide(...)` mutates the requested-side state
  - uses the current committed side (`assigned` first, otherwise live runtime team) as the lock source
  - adds one authoritative log line:
    - `CoopMissionSpawnLogic: rejected cross-side selection during active battle. ...`
  - forces `CanRespawn` to return `false` once `CoopBattlePhaseRuntimeState` is already `BattleEnded`
- `C:\dev\projects\BannerlordCoopSpectator3\UI\CoopSelectionUiHelpers.cs`
  - adds `CanSelectSide(...)` so team buttons mirror the authoritative side lock during `BattleActive`
- `C:\dev\projects\BannerlordCoopSpectator3\UI\CoopSelectionShellViewModels.cs`
  - uses the new `CanSelectSide(...)` gate for team-button enablement

The intent is specific:

- keep late/no-side joins working
- stop already-committed peers from swapping to the enemy mid-battle
- stop stale `CanRespawn=True` status from surviving past `BattleEnded`
- avoid a broad rewrite of completion/writeback flow that the logs already proved is executing

#### Updated artifacts after the side-lock fix

Rebuilt again:

- `dotnet build .\CoopSpectator.csproj -c Debug`
- `dotnet build .\DedicatedServer\CoopSpectatorDedicated.csproj -c Debug /p:UseDedicatedServerRefs=true`

Refreshed again:

- `C:\dev\projects\BannerlordCoopSpectator3\dist\CoopSpectator_ClientPackage`
- `C:\dev\projects\BannerlordCoopSpectator3\dist\CoopSpectator_ClientPackage.zip`

Current client package hashes after this refresh:

- built DLL SHA256: `9D64B97EEB83DAD8587F4BF5EF9609A174A4B6460260E5B7EF66C4917EA447EF`
- `dist` DLL SHA256: `9D64B97EEB83DAD8587F4BF5EF9609A174A4B6460260E5B7EF66C4917EA447EF`
- `dist` zip SHA256: `07AC2D9D785025B2F8E2132B3560C8AD2CE733333B5C22174FA3A0445228169A`

#### Updated smallest next rerun checks

1. In dedicated log, when a dead peer clicks the enemy side during `BattleActive`, confirm the new authoritative rejection:
   - `CoopMissionSpawnLogic: rejected cross-side selection during active battle. Peer=... CurrentSide=Attacker RequestedSide=Defender Phase=BattleActive`
2. In client/team-selection UI, confirm the opposite-side button is no longer enabled once the peer is already locked to a side during `BattleActive`.
3. After the last attacker dies, confirm post-end status does not keep advertising respawn:
   - remote `EntryStatusSnapshot ... CanRespawn=False`
4. Re-evaluate host mission exit timing only if the battle still visibly hangs *after* the cross-side handoff is gone.

## 11. Post-victory dedicated fall + pre-materialization spawn contract (2026-04-18 evening rerun)

### 11a. Dedicated crash was a native mission-ending timer contract violation, not a battle-result failure

Fresh evidence from:

- `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\logs\rgl_log_60860.txt`
- `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\crashes\2026-04-18_19.20.56\dump.dmp`

showed that authoritative completion and writeback were already correct:

- dedicated reached `CoopBattlePhaseRuntimeState: phase updated. Phase=BattleEnded`
- dedicated wrote `battle result snapshot written ... WinnerSide=Attacker`
- dedicated logged `authoritative battle completion detected ... AwaitingHostEndMission=True`
- host campaign log consumed the result and wrote it back successfully

The dedicated fall was a separate post-victory fault. `dotnet-dump` showed:

- wrapper throw site: `TaleWorlds.MountAndBlade.ListedServer.ServerSideIntermissionManager.Tick(...)`
- wrapped task fault: `System.NullReferenceException`

Native decompile then showed the exact contract in `MissionLobbyComponent.SetStateEndingAsServer()`:

- set `CurrentMultiplayerState = Ending`
- call `_timerComponent.StartTimerAsServer(PostMatchWaitDuration)`
- immediately read `_timerComponent.GetCurrentTimerStartTime()`

Our own logs proved we were still suppressing `MultiplayerTimerComponent.StartTimerAsServer` at mission end:

- `Multiplayer game mission ending`
- `BattleShellSuppressionPatch: suppressed native battle shell path. Source=MultiplayerTimerComponent.StartTimerAsServer ...`

That is enough to explain the `NullReferenceException`: native `SetStateEndingAsServer()` dereferenced `_missionTimer` after our suppression patch skipped the timer initialization.

### 11b. Applied smallest fix for the native end-mission contract

Updated:

- `C:\dev\projects\BannerlordCoopSpectator3\Patches\BattleShellSuppressionPatch.cs`

The patch now lets native shell/timer methods pass through once either condition is true:

- `MissionLobbyComponent.CurrentMultiplayerState == Ending`
- `CoopBattlePhaseRuntimeState >= BattleEnded`

and logs one authoritative line:

- `BattleShellSuppressionPatch: allowed native battle shell path for end transition. ...`

This keeps the earlier coop runtime suppression alive during the active bootstrap/runtime window, but stops breaking native listed-server intermission teardown once the match is actually ending.

### 11c. The first failed launch was a real spawn contract gap before battlefield readiness

The same rerun also confirmed a separate low-level gap:

- server still advertised `SelectableEntrySource=allowed-prebattle`
- client could send `SpawnNow`
- current `TryQueueSpawnIntentForPeer(...)` accepted spawn requests without checking battlefield readiness
- current `CanPeerRespawnFromCoopRuntime(...)` could still return `true` with no live/selectable entries as long as the peer had side + selection and no active agent

That matched the player-visible failure mode from the first launch:

- remote peer could press spawn before armies were safely ready/materialized
- spawn then drifted into an invalid map-edge/glitched state

### 11d. Applied smallest fix for spawn/respawn readiness

Updated:

- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`

Added a shared authoritative readiness check that now requires:

- peer side is assigned
- at least one current selectable entry exists for that side
- `AreBattlefieldArmiesReadyForStart(...)` is already `true`

Then wired that gate into both:

- `CanPeerRespawnFromCoopRuntime(...)`
- `TryQueueSpawnIntentForPeer(...)`

`SpawnNow` also now rejects stale selected entries that are no longer in the current selectable set and logs one authoritative line:

- `CoopMissionSpawnLogic: rejected spawn request because peer is not spawn-ready. ...`
- or `CoopMissionSpawnLogic: rejected spawn request because current entry is not selectable. ...`

### 11e. Rebuild + refreshed package after the two fixes

Rebuilt successfully:

- `dotnet build .\CoopSpectator.csproj -c Debug`
- `dotnet build .\DedicatedServer\CoopSpectatorDedicated.csproj -c Debug /p:UseDedicatedServerRefs=true`

Refreshed again:

- `C:\dev\projects\BannerlordCoopSpectator3\dist\CoopSpectator_ClientPackage`
- `C:\dev\projects\BannerlordCoopSpectator3\dist\CoopSpectator_ClientPackage.zip`

Current package hashes after this refresh:

- built DLL SHA256: `9E5EB4B285C8E5FA30E43521E23B0D5A4A64487EC5F886518CF28160532035A9`
- `dist` DLL SHA256: `9E5EB4B285C8E5FA30E43521E23B0D5A4A64487EC5F886518CF28160532035A9`
- `dist` zip SHA256: `C46835E9A747AF561690E077848041FD2383AA7B7F97EFB1727C112F7C8E7F97`

### 11f. Updated next rerun checks

1. On battle end, dedicated should now log:
   - `BattleShellSuppressionPatch: allowed native battle shell path for end transition. Source=MultiplayerTimerComponent.StartTimerAsServer ...`
2. Dedicated must no longer produce the old `ServerSideIntermissionManager.Tick -> Couldn't start the game in time` crash after a successful battle result/writeback.
3. In any early/prepared-but-not-ready window, a premature remote spawn click should now be rejected by log rather than queued:
   - `CoopMissionSpawnLogic: rejected spawn request because peer is not spawn-ready. ...`
4. Once armies are really ready, normal spawn should still continue through the existing replace-bot/materialization path.

## 12. Addendum 2026-04-20F - caravan battle surrogate names + remote client crash

Fresh paired reruns exposed two separate low-level runtime defects after the earlier VPN/battle fixes:

1. Selection UI and in-world labels fell back to surrogate identifiers during the second battle:
   - selection list showed raw entry ids like `attacker|player_party|caravan_guard_aserai|mp_coop_light_cavalry_aserai_troop`
   - agent labels could fall back to names like `Coop Jawwal`
2. Remote client later crashed during the caravan battle after mission load / live possession.

### 12a. Root cause #1 - second battle snapshot never reached the remote client

Authoritative proof from the rerun:

- the first battle still delivered a normal `BattleSnapshot` to the remote client
- the second battle did not
- dedicated repeatedly logged:
  - `CoopMissionNetworkBridge: payload too large for staged chunk transport. Kind=BattleSnapshot Bytes=96177 Chunks=376 ChunkBytes=256`

At that point our staged transport hard-cap was still effectively `255` chunks. The caravan/own-army `BattleSnapshot` needed `376`, so the server rejected that payload before queueing it for the remote peer.

That explains the raw UI strings:

- the remote client still had only the previous battle snapshot/runtime scene
- it did keep receiving `EntryStatusSnapshot` updates with the new caravan `EntryId`s
- UI display resolution therefore had no exact roster state for those ids and fell back to the raw entry-id text

### 12b. Root cause #2 - stale file refresh kept reloading the old scene snapshot into the live mission

The remote crash was also log-backed. During the second battle the client repeatedly logged:

- `CoopMissionSpawnLogic: refreshed client battle snapshot for mission. Mission=battle_terrain_biome_094 ... SceneMismatch=True ... RefreshedSnapshotKey=...battle_terrain_029...`
- followed immediately by:
  - `reset client exact visual overlay assignment state`
  - `reset client mission runtime state`
- then native `SetWieldedItemIndex` traffic continued

The actual bug was local:

- `EnsureClientBattleSnapshotFreshForMission(...)` called `BattleRosterFileHelper.ReadSnapshot()`
- `BattleRosterFileHelper.ReadSnapshot()` mutates `BattleSnapshotRuntimeState`
- on the remote client, the local roster file still contained the previous battle snapshot for `battle_terrain_029`
- scene mismatch logic therefore kept re-injecting stale snapshot state into the active `battle_terrain_biome_094` mission and resetting runtime underneath live agent/equipment replication

### 12c. Root cause #3 - display-name override only handled heroes

`AgentDisplayNamePatch` / `TryResolveExactDisplayNameForAgent(...)` only overrode display names for hero entries. Non-hero agents therefore kept the fallback mission-safe materialization names instead of the exact roster display names when available.

### 12d. Applied smallest fixes

Updated:

- `C:\dev\projects\BannerlordCoopSpectator3\Campaign\BattleRosterFile.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\BattleSnapshotRuntimeState.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\UI\CoopSelectionUiHelpers.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionNetworkBridge.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Network\Messages\CoopBattleSelectionNetworkMessages.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Patches\AgentDisplayNamePatch.cs`

Applied changes:

1. Added `BattleRosterFileHelper.PeekSnapshot()` so client-side scene-mismatch probes can inspect the roster file without mutating `BattleSnapshotRuntimeState`.
2. Changed `EnsureClientBattleSnapshotFreshForMission(...)` so scene-mismatch refresh only applies a file snapshot when the candidate scene actually matches the active mission scene.
3. Raised staged payload transport support from `255` to `1023` chunks:
   - `CoopBattlePayloadChunkMessage.MaxChunkCount = 1023`
   - `PendingPayloadTransmission.Create(...)` now uses that limit
4. Centralized exact/friendly display-name resolution in `BattleSnapshotRuntimeState.ResolveEntryDisplayName(...)` and reused it in:
   - selection UI
   - agent display-name override
5. Broadened agent display-name override so non-hero entries can also resolve to exact roster names instead of surrogate materialization labels.

### 12e. Rebuild + refreshed package after the fixes

Rebuilt successfully:

- `dotnet build .\CoopSpectator.csproj -c Debug`
- `dotnet build .\DedicatedServer\CoopSpectatorDedicated.csproj -c Debug /p:UseDedicatedServerRefs=true`

Refreshed again:

- `C:\dev\projects\BannerlordCoopSpectator3\dist\CoopSpectator_ClientPackage`
- `C:\dev\projects\BannerlordCoopSpectator3\dist\CoopSpectator_ClientPackage.zip`

Current package hashes after this refresh:

- built DLL SHA256: `3239B27F089417103AABFDA6684E2F722FB194A39260ED3323475765D93568B0`
- `dist` DLL SHA256: `3239B27F089417103AABFDA6684E2F722FB194A39260ED3323475765D93568B0`
- `dist` zip SHA256: `BB4ADE6172550CEC985BA20E015CAA44F5038CE77C0F2EC6F82FCCE6F7120338`

### 12f. Updated next rerun checks

1. Dedicated must no longer log:
   - `payload too large for staged chunk transport. Kind=BattleSnapshot ...`
2. Remote client should now receive a second battle snapshot for the caravan scene instead of keeping only the previous battle scene.
3. On scene mismatch with a stale local file snapshot, client should now log:
   - `CoopMissionSpawnLogic: skipped stale client battle snapshot refresh because candidate scene does not match mission. ...`
   and must not keep resetting runtime state underneath live mission replication.
4. Selection list and in-world names should resolve to friendly troop/hero names instead of raw entry ids or surrogate fallback labels.

## 13. Addendum 2026-04-20G - dedicated crash after host died and re-possessed another soldier

### 13a. Exact native crash site

The dedicated crash was dump-backed, not inferred from truncated logs.

`watchdog_log_13276.txt` produced a managed dump under:

- `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\crashes\2026-04-20_03.27.39\dump.dmp`

`dotnet-dump` showed:

- exception: `System.InvalidOperationException`
- message: `Sequence contains more than one matching element`
- native/managed stack:
  - `System.Linq.Enumerable.SingleOrDefault(...)`
  - `TaleWorlds.MountAndBlade.MissionLobbyComponent.OnBotKills(...)`
  - `TaleWorlds.MountAndBlade.DedicatedCustomServer.MissionCustomGameServerComponent.OnBotKills(...)`
  - `TaleWorlds.MountAndBlade.MissionLobbyComponent.OnAgentRemoved(...)`

Decompile of native `MissionLobbyComponent.OnBotKills(...)` confirmed the exact predicate:

- `GameNetwork.NetworkPeers.SingleOrDefault(x => x.GetComponent<MissionPeer>() != null && x.GetComponent<MissionPeer>().ControlledFormation == botAgent.Formation)`

That means native scoreboard/lobby code assumes there is at most one `MissionPeer` owning a given `ControlledFormation`.

### 13b. Why our runtime violated that contract

In the crashing run:

- remote peer `XCTwnik` first possessed `aserai_footman`:
  - `rgl_log_13276.txt:55815` -> `Formation=Infantry ... CommanderControl=(captain)`
- later host peer `AC` died as `main_hero`, entered `DeadAwaitingRespawn`, then possessed `battanian_oathsworn`:
  - `rgl_log_13276.txt:78349` -> `DeadAwaitingRespawn`
  - `rgl_log_13276.txt:80185` -> `Formation=Infantry ... CommanderControl=(captain)`

Both peers were therefore non-commanders on the same `Infantry` formation. Our `TryReplaceMaterializedBotWithPlayer(...)` still assigned:

- `missionPeer.ControlledFormation = targetFormation`
- non-zero `BotsUnderControlAlive/Total`

for every replace-bot possession, including non-commanders.

That left two live `MissionPeer`s with the same `ControlledFormation == Infantry`, and the next native `OnBotKills(...)` call fataled on `SingleOrDefault(...)`.

### 13c. Applied smallest fix

Updated:

- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`

Applied change:

1. After `TryPromoteExactCampaignCommanderPeerToGeneralControl(...)`, non-commander replace-bot possessions now immediately normalize back to no formation ownership:
   - clear `missionPeer.ControlledFormation`
   - force `BotsUnderControlTotal=0`
   - force `BotsUnderControlAlive=0`
   - reset formation player-owner state via `TryResetMaterializedFormationPlayerState(...)`
   - broadcast `BotsControlledChange(..., 0, 0)`
2. Commander/general path is left unchanged:
   - if `CommanderControl=general`, the peer retains native/general formation ownership
3. Added one authoritative log line for the new branch:
   - `CoopMissionSpawnLogic: cleared non-commander formation ownership after replace-bot. ...`

This keeps the exact commander path alive while removing the duplicate native `ControlledFormation` state that caused the server crash.

### 13d. Rebuild + refreshed package after the fix

Rebuilt successfully:

- `dotnet build .\CoopSpectator.csproj -c Debug`
- `dotnet build .\DedicatedServer\CoopSpectatorDedicated.csproj -c Debug /p:UseDedicatedServerRefs=true`

### 13e. Updated next rerun checks

1. Dedicated must log for non-commander respawn:
   - `CoopMissionSpawnLogic: cleared non-commander formation ownership after replace-bot. ...`
2. After such a respawn, peers on the same side/formation must no longer keep non-zero bot ownership counts simultaneously.
3. Dedicated must no longer crash in native `MissionLobbyComponent.OnBotKills(...)` when either of those formations scores a bot kill.

## 14. Addendum 2026-04-20H - giant battle transport scalability and surrogate names

### 14a. Exact giant-battle outcome from the fresh logs

The final oversized battle did not hang because of a timer-backed forced finish.

Host log:

- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_8032.txt:550788`
  - `BattleDetector: consumed battle_result writeback audit ... WinnerSide=Attacker Entries=770 ...`

So the battle-completion/writeback path stayed alive even in the oversized run.

### 14b. Exact low-level blocker

The real blocker was `BattleSnapshot` transport scale on the remote-client sync path.

Dedicated repeatedly logged:

- `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\logs\rgl_log_4800.txt:254213`
  - `CoopMissionNetworkBridge: payload too large for staged chunk transport. Kind=BattleSnapshot Bytes=1864687 Chunks=7284 ChunkBytes=256`

This same line kept repeating throughout the giant battle window, which means the dedicated could not enqueue the oversized battle snapshot for staged transmission at all.

At the same time the host did build the correct giant battle snapshot locally:

- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_8032.txt:124143`
  - `BattleSnapshotRuntimeState: snapshot updated. Source=battle-roster-file Sides=2 Entries=770 ...`

But the remote client never got a fresh `CoopMissionNetworkBridge` snapshot for that giant battle. Its last received transport snapshot stayed from a much smaller battle:

- `C:\Users\Admin\Downloads\VVS logs\rgl_log_2080.txt:232092`
  - `BattleSnapshotRuntimeState: snapshot updated. Source=CoopMissionNetworkBridge Sides=2 Entries=22 ...`

Later, during the giant battle, the client only logged stale-refresh rejects:

- `C:\Users\Admin\Downloads\VVS logs\rgl_log_2080.txt:282112`
  - `CoopMissionSpawnLogic: skipped stale client battle snapshot refresh because candidate scene does not match mission. ... PreviousSnapshotKey=... attacker:31:9,defender:44:13`

That explains both visible symptoms:

1. giant-battle remote runtime lagged badly because it never switched to the authoritative giant-battle roster snapshot,
2. surrogate/raw roster labels appeared because the client was resolving entry identity against stale snapshot state instead of the current 770-entry battle roster.

### 14c. Why the previous transport still failed after earlier chunk-limit work

Even after raising staged transport support for medium-large payloads, the giant battle exceeded that ceiling:

- previous wire payload needed `7284` chunks at `256` bytes each,
- previous `CoopBattlePayloadChunkMessage.MaxChunkCount` ceiling was still too low for that case.

There was also a second scaling bug in our own hot path:

- `TrySyncBattleSnapshotPayloads(...)` serialized the full battle snapshot JSON again before checking pending staged-transmission state,
- so once the battle snapshot got huge, dedicated kept paying repeated megabyte-scale serialization cost every UDP tick even though the payload still could not be transported.

### 14d. Applied smallest fix

Updated:

- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionNetworkBridge.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Network\Messages\CoopBattleSelectionNetworkMessages.cs`

Applied changes:

1. Raised staged transport ceiling again:
   - `CoopBattlePayloadChunkMessage.MaxChunkCount = 8191`
2. Added battle-snapshot transport payload caching keyed by snapshot identity:
   - giant `BattleSnapshot` JSON is now serialized once per snapshot key instead of every tick
3. Added `gzip` compression for `BattleSnapshot` wire payloads:
   - if compressed bytes are smaller than raw JSON bytes, staged transport now sends the compressed wire payload and restores JSON on the client
4. Added dynamic per-tick chunk budget for large `BattleSnapshot` transmissions only:
   - small payload behavior stays narrow
   - large giant-battle snapshots flush faster without broadening `EntryStatusSnapshot` traffic
5. Added one authoritative preparation log line:
   - `CoopMissionNetworkBridge: prepared battle snapshot transport payload. ComparisonKey=... RawBytes=... WireBytes=... Compressed=... Chunks=... Entries=...`

This keeps the current staged transport contract intact instead of inventing a separate channel.

### 14e. Rebuild + refreshed package after the fix

Rebuilt successfully:

- `dotnet build .\CoopSpectator.csproj -c Debug`
- `dotnet build .\DedicatedServer\CoopSpectatorDedicated.csproj -c Debug /p:UseDedicatedServerRefs=true`

Refreshed again:

- `C:\dev\projects\BannerlordCoopSpectator3\dist\CoopSpectator_ClientPackage`
- `C:\dev\projects\BannerlordCoopSpectator3\dist\CoopSpectator_ClientPackage.zip`

Current package hashes after this refresh:

- built DLL SHA256: `6CBF3CB6929610E6C90D7BE7BCA496151819C1290EFEFE3E1DDBE2C288FAFE46`
- `dist` DLL SHA256: `6CBF3CB6929610E6C90D7BE7BCA496151819C1290EFEFE3E1DDBE2C288FAFE46`
- `dist` zip SHA256: `742AB16A30DDC11827C984ACC176C41CFBDC723746877C2BF45A77D09EAD59EF`

### 14f. Updated next rerun checks

1. Dedicated must log the new preparation line for the giant battle:
   - `prepared battle snapshot transport payload ... RawBytes=... WireBytes=... Compressed=... Chunks=... Entries=770`
2. Dedicated must no longer log:
   - `payload too large for staged chunk transport. Kind=BattleSnapshot ...`
3. Remote client must receive a fresh giant-battle snapshot:
   - `BattleSnapshotRuntimeState: snapshot updated. Source=CoopMissionNetworkBridge Sides=2 Entries=770 ...`
4. Giant-battle selection/in-world names should resolve to friendly roster names again instead of surrogate/raw entry ids.
5. If the next oversized run still lags badly after transport succeeds, only then open a separate blocker for runtime materialization/per-tick cost. The current fresh logs do not justify opening that layer yet.

## 15. Exact entry spawn drift in huge battle rerun (2026-04-20)

### 15a. Fresh log-backed finding

The large-battle transport fix held: dedicated prepared and staged a compressed battle snapshot successfully, and the remote client updated to `Entries=581`. The next blocker was different:

- when the host selected an exact cavalry entry such as `attacker|player_party|eastern_mounted_mercenary_t4|mp_coop_light_cavalry_sturgia_troop|variant-2`,
- the pending spawn request could later be rewritten by `TryForcePreferredHeroClassForPeer(...)` into a different allowed entry such as:
  - `attacker|player_party|imperial_elite_cataphract|mp_coop_light_cavalry_empire_troop`, then
  - later even `attacker|player_party|caravan_master_empire|mp_coop_heavy_infantry_empire_troop|variant-1`,
- and after a later manual retry the host finally possessed a completely different ranged unit:
  - `attacker|player_party|forest_people_tier_1|mp_light_ranged_empire_troop|variant-1`.

This was not just “selected unit died before spawn.” Our own pending-request refresh path was silently mutating exact-entry spawn intent into troop-match / surrogate fallback.

### 15b. Exact divergence point

The mutation path was:

1. `TryForcePreferredHeroClassForPeer(...)`
2. `TryResolvePreferredHeroClassForPeer(...)`
3. `ApplyAuthoritativePreferredSelection(...)`
4. `CoopBattleSpawnRequestState.TryQueueFromSelection(... pending-request-refresh)`

If the original exact entry was gone, the code still refreshed the pending spawn request from:

- `exact troop id match entry`, or
- `peer-culture surrogate entry`

instead of rejecting the stale exact selection.

### 15c. Applied smallest fix

Updated:

- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`

Applied change:

- before any auto fallback selection is allowed to refresh a pending spawn request, we now detect:
  - pending spawn has an exact `EntryId`,
  - resolved fallback `preferredEntry` is missing or different,
- and then reject that pending spawn instead of mutating it.

Authoritative behavior now is:

1. clear pending selection request
2. clear pending spawn request
3. mark spawn runtime `Rejected` with reason `exact selected entry no longer spawnable`
4. advance lifecycle back toward respawnable/selection state
5. emit one authoritative line:
   - `CoopMissionSpawnLogic: canceled pending exact-entry spawn instead of mutating to fallback selection ...`

This preserves the player contract:

- exact unit chosen -> either exact unit is still spawnable,
- or spawn is canceled and the player must choose again,
- but the server no longer silently possesses a different surrogate unit.

### 15d. Additional interpretation from the same run

The “naked riders” seen in the huge battle are not yet log-backed as a separate missing-equipment regression. In the same run, mounted entries such as `eleftheroi_tier_1` were injected with snapshot loadouts like:

- `Body=armored_baggy_trunks`
- `Body=baggy_trunks`

and exact overlay lines showed `EquipmentMisses=overlay-skipped`, not missing-equipment failures. So for now this looks more like actual source loadout data than a confirmed new materialization bug.

### 15e. Rebuild + refreshed package after the fix

Rebuilt successfully:

- `dotnet build .\CoopSpectator.csproj -c Debug`
- `dotnet build .\DedicatedServer\CoopSpectatorDedicated.csproj -c Debug /p:UseDedicatedServerRefs=true`

Refreshed again:

- `C:\dev\projects\BannerlordCoopSpectator3\dist\CoopSpectator_ClientPackage`
- `C:\dev\projects\BannerlordCoopSpectator3\dist\CoopSpectator_ClientPackage.zip`

Current package hashes after this refresh:

- built DLL SHA256: `37B77FCB33D1A36BFF6F0339EF39A70D7473771949E1DB7AEE56882D0E5B52CA`
- `dist` zip SHA256: `A08F1CBA64816C87A953AA50C91A6F05FBF47C28C6158851298E1EFC128B19BB`

### 15f. Updated next rerun checks

1. If an exact selected unit dies before possession, dedicated must log:
   - `canceled pending exact-entry spawn instead of mutating to fallback selection ...`
2. Dedicated must no longer log new `pending-request-refresh` lines that rewrite the host from the original exact entry into:
   - `exact troop id match entry`, or
   - `peer-culture surrogate entry`
   while the old exact spawn is still pending.
3. The host should either:
   - possess the exact selected entry, or
   - be forced to pick again after the exact entry disappears.
4. If raw/surrogate unit names still appear after this rerun, open that as a separate display-name contract issue with fresh logs. It is no longer justified to mix it with the exact-entry possession bug.

## 16. Reconnect respawn exact-entry bridge + safe mid-battle side switching (2026-04-20)

### 16a. Fresh log-backed findings

From:

- `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\logs\rgl_log_19764.txt`
- `C:\Users\Admin\Downloads\VVS logs\rgl_log_14108.txt`

the previous exact-entry protection was confirmed working as designed, but it exposed a narrower reconnect/respawn blocker:

1. the peer selected a valid exact entry
2. the spawn request was queued with that exact `EntryId`
3. `TryForcePreferredHeroClassForPeer(...)` still ran the peer-culture bridge
4. `TryResolvePreferredHeroClassForPeer(...)` chose a `peer-culture surrogate`
5. the exact-entry protection correctly refused to mutate the pending spawn
6. spawn was rejected as:
   - `exact selected entry no longer spawnable`

So the real defect was no longer the rejection itself. The defect was that hero-class bridging for vanilla spawn visuals was still trying to rewrite authoritative exact-entry respawns.

The same review also confirmed that mid-battle side switching was still blocked by our own hard reject in `TryApplySelectionIntentToPeer(...)`, even though the code already had a safer defer path for peers that still occupy an active coop life.

### 16b. Applied smallest fix

Updated:

- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`

Applied changes:

1. When a peer has a pending spawn request with an explicit exact `EntryId`, the hero-class bridge now becomes bridge-only:
   - it may still choose a `preferredTroopIndex` for vanilla visuals/spawn plumbing,
   - but it no longer rewrites authoritative selection or refreshes the pending spawn request to a surrogate entry.
2. During `BattleActive`, cross-side selection is no longer hard-rejected up front.
   - if the peer still occupies an active coop life, the existing defer path stays in charge,
   - if the peer is already dead / no longer occupies an active coop life, the side change may proceed immediately.

This restores the intended contract:

- exact selected entry remains authoritative during reconnect/death respawns,
- surrogate class bridging is allowed only as a vanilla index bridge,
- side switching during battle is available again without reopening the earlier active-life crash path.

### 16c. Rebuild + refreshed package after the fix

Rebuilt successfully:

- `dotnet build .\CoopSpectator.csproj -c Debug`
- `dotnet build .\DedicatedServer\CoopSpectatorDedicated.csproj -c Debug /p:UseDedicatedServerRefs=true`

Refreshed again:

- `C:\dev\projects\BannerlordCoopSpectator3\dist\CoopSpectator_ClientPackage`
- `C:\dev\projects\BannerlordCoopSpectator3\dist\CoopSpectator_ClientPackage.zip`

Current package hashes after this refresh:

- built DLL SHA256: `DF85DF54B6C971EEB8AC8366A59F1DC8B3A2120118785169ACA5148D658FE9C1`
- packaged client DLL SHA256: `DF85DF54B6C971EEB8AC8366A59F1DC8B3A2120118785169ACA5148D658FE9C1`
- `dist` zip SHA256: `F1AF13FBA3AE52753761DAC2E86E274D33649A0B2B2BDC5CD26D2C6F1554B07B`

### 16d. Updated next rerun checks

1. After reconnect/death, dedicated should no longer log:
   - `canceled pending exact-entry spawn instead of mutating to fallback selection ... Source=peer-culture surrogate`
   for otherwise valid exact-entry respawns.
2. Dedicated should instead log the usual successful bridge/apply path and then either:
   - `possessed materialized army agent via vanilla replace-bot flow`, or
   - `coop direct spawn succeeded`
   for the exact selected entry.
3. During `BattleActive`, when a living peer requests the opposite side, dedicated should log:
   - `deferred cross-side selection until peer leaves active coop life ...`
4. After death / no active coop life, side switching should work again without reopening the old crash behavior.

## 17. Commander-death FollowMe crash + forced cross-side respawn contract (2026-04-22)

### 17a. Fresh log-backed findings

From:

- `C:\Users\Admin\Downloads\VVS logs\rgl_log_16724.txt`
- `C:\Users\Admin\Downloads\VVS logs\AC logs\rgl_log_38728.txt`

the reconnect possession fix stayed green, but two narrower issues remained:

1. `SelectSide` during battle was already reaching the server and being applied in authority state.
   - dedicated logged repeated `authoritative side assigned ... Applied=True`
   - so the remaining problem was not server-side refusal
   - it was the fact that a living peer still occupied an active coop life, and our current contract only deferred the effective switch
2. the dedicated crash after commander death was consistent with stale commander/order ownership.
   - the remote peer became cavalry commander and issued `SetOrderWithAgent FollowMe ...`
   - after that peer died, dedicated moved the peer to spectator/respawnable state
   - native then started logging `peer.ControlledAgent == null` / `peersTeam == null`
   - shortly after, watchdog produced the dedicated dump

Native decompile confirmed the low-level risk point:

- `TaleWorlds.MountAndBlade.MissionLobbyComponent.OnBotKills(...)` uses `SingleOrDefault(...)` over peers matched by `ControlledFormation`
- `OrderController.SetOrderWithAgent(OrderType.FollowMe, agent)` leaves selected formations following the commander agent until something explicitly replaces that movement order

So the real gap was that our `lost-controlled-agent` / `force-respawnable` paths cleared peer state, but did not fully release team-level commander/order ownership and did not actively replace commander-driven follow orders with AI fallback orders.

### 17b. Applied smallest fix

Updated:

- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`

Applied changes:

1. Added `TryReleasePeerOrderOwnershipAfterLeavingActiveLife(...)` and `TryClearDeadPeerOrderControllerOwnership(...)`.
   - runs before `MovePeerToSpectatorHoldingState(...)`
   - clears dead peer `PlayerOwner` / player-troop flags on owned formations
   - clears dead peer `OrderController.Owner`
   - if the peer was the team general, clears `GeneralAgent`, disables player-general role, delegates the team back to AI, and forces formation fallback orders to `Charge + FireAtWill`
   - emits one authoritative log line:
     - `released post-life commander ownership back to AI ...`
2. Hooked that cleanup into both:
   - `TryRefreshPendingSpawnRequests(...)` lost-controlled-agent recovery
   - `TryForcePeerRespawnable(...)`
3. Changed active-life cross-side selection contract:
   - if a living peer requests the opposite side during battle, the server now forces that peer into respawnable state first
   - then applies the requested side immediately
   - authoritative log:
     - `applied cross-side selection by forcing peer out of active coop life ...`

This keeps the change narrow:

- no spawn/runtime rewrite
- no new commander abstraction
- only cleanup at the exact death/forced-leave boundary plus reuse of the existing respawnable path

### 17c. Rebuild + refreshed package after the fix

Rebuilt successfully:

- `dotnet build .\CoopSpectator.csproj -c Debug`
- `dotnet build .\DedicatedServer\CoopSpectatorDedicated.csproj -c Debug /p:UseDedicatedServerRefs=true`

Refreshed again:

- `C:\dev\projects\BannerlordCoopSpectator3\dist\CoopSpectator_ClientPackage`
- `C:\dev\projects\BannerlordCoopSpectator3\dist\CoopSpectator_ClientPackage.zip`

Current package hashes after this refresh:

- built DLL SHA256: `78ACCC1D9D9AB787BF95538072F42C34C419398E48643A7ED570B412D3483A66`
- packaged client DLL SHA256: `78ACCC1D9D9AB787BF95538072F42C34C419398E48643A7ED570B412D3483A66`
- `dist` zip SHA256: `3FAA5416BB19057EB890E8AAF0422859FE0933A7553FD4629F066D54D423E942`

### 17d. Updated next rerun checks

1. On commander death after `FollowMe`, dedicated should now log:
   - `released post-life commander ownership back to AI ... ReleasedGeneralOwnership=True ...`
2. The same death should no longer end with the old native spam pattern staying alive until crash:
   - `peer.ControlledAgent == null`
   - `peersTeam == null`
3. For living mid-battle side switch, dedicated should now log:
   - `applied cross-side selection by forcing peer out of active coop life ...`
4. After that forced switch, the peer should be able to pick/spawn on the new side instead of only carrying a deferred request.

## 18. Post-death side selection UI still disabled despite valid server-side selection state (2026-04-22)

### 18a. Fresh finding from the latest rerun

The commander-death crash fix held, but post-death side switching was still visually unavailable.

The fresh logs showed this was no longer a server-side `SelectSide` rejection:

- dedicated still handled side requests after death:
  - `CoopMissionNetworkBridge: handled client selection request. Peer=XCTwnik Kind=SelectSide Side=Defender ... Applied=True`
- the client also kept receiving valid `EntryStatusSnapshot` payloads with:
  - `AssignedSide=Defender`
  - non-empty `SelectableEntryIds`
  - both `CanRespawn=True` and later `CanRespawn=False` snapshots during the same post-death window

So the real gap was narrower: during `BattleActive`, the team-selection UI still disabled the opposite side button whenever `AssignedSide` was already set, even if the peer no longer had any live controlled agent.

### 18b. Applied smallest fix

Updated:

- `C:\dev\projects\BannerlordCoopSpectator3\UI\CoopSelectionUiHelpers.cs`

Applied change:

1. Narrowed `CanSelectSide(...)` so that during `BattleActive` it now allows side selection when:
   - the side still has selectable entries, and
   - the authoritative status says `HasAgent=false`

This keeps the server-side side-selection contract unchanged and only removes the stale UI lock for peers that are already post-life / back in the selection overlay.

### 18c. Expected next rerun behavior

1. After death, if the selection overlay is visible and the opposite side has selectable entries, that side button should no longer stay disabled just because `AssignedSide` still points to the old side.
2. The next decisive server log for a real cross-side switch should be:
   - `applied cross-side selection by forcing peer out of active coop life ...`
3. If the player is already dead / agentless, the switch should go through without looking like a locked team-selection shell.

## 19. Commander-death fallback order verified + one-shot host start hint (2026-04-22)

### 19a. Fresh finding from the latest rerun

The latest rerun confirmed that the commander-death crash path stayed fixed, and the dedicated log also showed that the fallback attack order path really executed for the dead team general:

- `released post-life commander ownership back to AI ... ReleasedGeneralOwnership=True ReleasedFormations=8 ChargedFormations=3 ClearedOrderControllers=2 PulsedAgents=22`

That is sufficient log-backed proof that the post-death commander cleanup no longer only clears ownership, but also pushes affected formations into the fallback `Charge + FireAtWill` path.

### 19b. Applied smallest UX fix

Updated:

- `C:\dev\projects\BannerlordCoopSpectator3\UI\CoopMissionSelectionView.cs`

Applied change:

1. Added a one-shot local instruction that appears the first time the local host-controlled peer possesses a unit while battle start is actually available.
2. Message text:
   - `Coop Battle: press H to start the battle.`
3. The message is gated by the existing `CanStartBattle` contract and does not change battle start behavior or hotkey handling.

### 19c. Expected next rerun behavior

1. On the host machine, the first valid pre-battle possession should now show the one-shot instruction:
   - `Coop Battle: press H to start the battle.`
2. Client should not get that hint unless it also satisfies the same start-authority contract.
3. The host log should contain:
   - `CoopMissionSelectionView: showed one-shot start battle instruction for local host-controlled peer.`
