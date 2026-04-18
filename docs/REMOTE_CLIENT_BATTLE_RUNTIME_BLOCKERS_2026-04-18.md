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
