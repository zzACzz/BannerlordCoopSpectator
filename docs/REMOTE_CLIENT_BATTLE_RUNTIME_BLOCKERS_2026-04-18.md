# Remote Client Battle Runtime Blockers (2026-04-18)

## 2026-04-26 Delta

The startup crash boundary is still behind us. The current failures are lower-level runtime ownership and prebattle-control defects:

- server-side `primary peer` and exact-campaign bootstrap were still allowed to bind to the first synchronized peer instead of the hosted local self-join peer. If the remote client synchronized first, host-side selection/bootstrap could be effectively routed through the wrong peer.
- prebattle formation hold was preserving army freeze by reasserting stop/hold-fire every tick. That also erased valid commander-issued prebattle movement orders, which matches the symptom where commanders could give commands before battle start but troops did not obey until battle start.
- the generated `BannerlordCoopCampaign_v0.1.1_LightRelease.zip` in `dist` was stale and did not contain the current client/host DLLs. The release script now generates the light package directly so future reruns do not accidentally validate old binaries.

The next rerun should be evaluated against host-first controllability and prebattle commander authority, not against the already-fixed early dedicated startup crash.

## 2026-04-25 Delta

The current blocker set has moved past early dedicated battle startup. Fresh host/client logs now point to three narrower runtime issues during battle entry selection and commander ownership:

- `PreBattleHold` could expose already materialized armies while still publishing an empty live selectable-entry list. The status path now falls back to the authoritative allowed prebattle roster instead of returning `live-prebattle-empty`.
- sides without hero-tagged leaders, especially looter/bandit parties, could end up with no commander entry at all. Commander resolution now falls back to the strongest side-leader candidate when no hero-role signal exists.
- local order-control guards were fail-open when `ControlledEntryId` was still unresolved, which allowed non-commanders to inherit commander flags and order UI. The guard now keeps suppression active until the controlled entry identity resolves.
- the deeper selection blocker is architectural: `GetSelectionState()` was previously writing fallback/default entry ids into explicit authority state, and claim filtering treated that fallback as a real claim. That could remove commander from prebattle selectable lists immediately after `AssignSide`, before any peer explicitly chose the commander.

The next rerun should be evaluated against selection ownership and commander availability, not the earlier `Mission.Initialize` startup crash boundary.

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

## 20. Large-battle enemy reinforcements starved by PlayerTeam drift (2026-04-23)

### 20a. Fresh finding from the latest rerun

The large-battle rerun produced an exact server-side contract break, not a vague "reinforcements are flaky" symptom.

Dedicated logs showed:

- native bootstrap initialized with a stable attacker-oriented contract:
  - `PlayerSide=Attacker`
  - `AppliedPlayerTeam=Attacker#1`
  - `AppliedPlayerEnemyTeam=Defender#2`
- later during the same mission, our runtime bridge flipped that mission contract to the remote defender peer:
  - `AppliedPlayerTeam=Defender#2`
  - `AppliedPlayerEnemyTeam=Attacker#1`

This matters because native `MissionAgentSpawnLogic` reinforcement spawning ultimately resolves spawn teams through native `Mission.GetAgentTeam(IAgentOriginBase troopOrigin, bool isPlayerSide)`, and decompile confirms that method maps non-player-side origins to `Mission.Current.PlayerEnemyTeam`.

That means our exact-native troop suppliers were built once with `isPlayerSide` fixed at bootstrap init, but later we changed `Mission.PlayerTeam / PlayerEnemyTeam` underneath the active native spawn logic. In the large battle this let the defender reinforcement side reserve troops (`UnsuppliedBySide` fell, `HasReserved=True` appeared), but those batches never materialized and `SpawnedLastBatch` stayed `0`.

### 20b. Applied smallest fix

Updated:

- `C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\ExactCampaignArmyBootstrap.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`

Applied change:

1. Exact native bootstrap now captures the active `Mission.PlayerTeam` / `Mission.PlayerEnemyTeam` contract at successful initialization.
2. Once exact native bootstrap is active for the mission, `TryEnsureExactCampaignNativeArmyBootstrap(...)` no longer re-bridges mission player teams from whichever peer is currently authoritative.
3. Reinforcement sync now also re-applies the captured mission player-team contract if runtime drift changed it.

New authoritative log:

- `ExactCampaignArmyBootstrap: restored native player team contract after runtime drift...`

### 20c. Expected next rerun behavior

1. In the large battle, after bootstrap init there should no longer be a later server log that flips:
   - `AppliedPlayerTeam=Defender#2 AppliedPlayerEnemyTeam=Attacker#1`
2. If some other runtime path still mutates those mission team pointers, the dedicated log should now show:
   - `restored native player team contract after runtime drift...`
3. Enemy reinforcements should no longer get stuck in the old state where:
   - `RemainingBySide[Defender>0]`
   - `UnsuppliedBySide[Defender]` drains toward `0`
   - but `SpawnedLastBatch=0` and no `native reinforcement batch spawned` ever appears.

## 21. Remote client reused stale local battle_roster snapshot after host swap (2026-04-23)

### 21a. Fresh finding from host/client role swap rerun

When the previous host became a remote client and joined someone else's small battle, the client still loaded `battle_roster.json` from its own local Documents folder on `MissionState.OpenNew`.

Fresh evidence:

- client `rgl_log_43408.txt`:
  - `BattleSnapshotRuntimeState: snapshot updated. Source=battle-roster-file Sides=2 Entries=582`
  - `BattleRosterFile: read snapshot with 2 sides from ...\battle_roster.json`
- the mission that actually opened was `battle_terrain_001` for the new small battle, so that `582`-entry snapshot was stale local data from the old hosted run, not authoritative data from the new remote host.

### 21b. Root cause

We still allowed remote custom-game joins to fall back to the local `battle_roster.json` in three places:

1. `CampaignMapPatchMissionInit.TryResolveSnapshot(...)`
2. `EnsureClientBattleSnapshotFreshForMission(...)`
3. `RefreshAllowedTroopsFromRoster(...)`

That was acceptable for host self-join on one machine, but wrong for remote join after a host/client swap because the local file belonged to the client's previous campaign-hosted battle.

### 21c. Applied smallest fix

Updated:

- `C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\CustomGameJoinContextState.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Patches\LobbyJoinResultSelfJoinArmPatch.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\CampaignMapPatchMissionInit.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\GameMode\MissionMultiplayerCoopBattleMode.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\BattleSnapshotRuntimeState.cs`

Applied change:

1. Added `CustomGameJoinContextState`, updated directly from `LobbyJoinResultSelfJoinArmPatch`.
2. Host self-join keeps `allowLocalBattleRosterFallback=True`.
3. Any remote custom-game join now sets `allowLocalBattleRosterFallback=False`.
4. On every successful join result, stale in-memory `BattleSnapshotRuntimeState` is cleared before mission load.
5. Client-side mission/bootstrap paths now skip local `battle_roster.json` fallback for remote joins.

### 21d. Expected next rerun behavior

1. Remote client should log:
   - `CustomGameJoinContextState: updated current custom-game join context ... allowLocalBattleRosterFallback=False`
2. Remote client should log:
   - `BattleSnapshotRuntimeState: snapshot cleared. Source=LobbyJoinResultSelfJoinArmPatch join-result ...`
3. Remote client should no longer log:
   - `BattleRosterFile: read snapshot ... battle_roster.json`
   during a remote mission open.

## 22. Remote client could not spawn in large pre-battle because selectable list still advertised reserve entries (2026-04-23)

### 22a. Fresh finding from rerun with working reinforcements

Reinforcements were green in this run, but the remote client still failed its initial spawn until it disconnected and rejoined mid-battle.

Fresh evidence:

- dedicated `rgl_log_29888.txt` repeatedly logged:
  - `materialized army possession found no eligible candidate ... EntryMatches=0 TroopMatches=0`
- just before that same pending spawn, dedicated still advertised:
  - `selectable entry universe updated ... Phase=PreBattleHold AttackerSource=allowed-prebattle-claim-filtered AttackerCount=543`
- client `rgl_log_9048.txt` received:
  - `EntryStatusSnapshot ... SelectedEntryId=attacker|player_party|aserai_footman|mp_light_infantry_aserai_troop|variant-3 ... CanRespawn=True`

So the client was allowed to pick a reserve exact-entry that was valid in the full roster but not currently materialized on the battlefield.

### 22b. Root cause

`ResolveSelectableEntryIdsForStatus(...)` still returned the full `allowed-prebattle` roster for every `currentPhase < BattleActive`.

That was wrong once exact/materialized battlefield state already existed during `PreBattleHold`, because pending exact-entry possession can only succeed against currently materialized live agents.

### 22c. Applied smallest fix

Updated:

- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`

Applied change:

1. During `PreBattleHold`, if the requested side already has tracked battlefield entry state, selectable entries now come from current live materialized entry ids instead of the full reserve roster.
2. That live list is still claim-filtered per viewing peer.
3. If tracked battlefield state exists but no live entries remain, the source now becomes `live-prebattle-empty` and the peer is no longer told it can respawn into reserve-only entries.

### 22d. Expected next rerun behavior

1. Dedicated should log:
   - `AttackerSource=live-prebattle-materialized` or `DefenderSource=live-prebattle-materialized`
   during `Phase=PreBattleHold` once materialized battlefield state exists.
2. The old bad state should no longer appear:
   - `Phase=PreBattleHold ... allowed-prebattle ...`
   for a side that already has tracked materialized entries.
3. Remote peers should no longer get stuck on:
   - `materialized army possession found no eligible candidate ... EntryMatches=0 TroopMatches=0`
   after choosing a supposedly selectable pre-battle entry.

## 23. Dedicated crash on clean host machine during first mission-open observer tick (2026-04-24)

### 23a. Fresh finding from dedicated dump + host-only logs

The crash was not a lobby/join disconnect. It was a real dedicated native crash on a clean host machine.

Fresh evidence:

- dedicated `watchdog_log_12976.txt` reported:
  - `ExceptionCode: 0xC0000005`
  - `ExceptionAddress: 0x7ffcc7b9591a`
- `module_list.txt` + dump module map placed that address inside:
  - `FairyTale.Library.dll`
- managed dump stack on the crashing thread showed:
  - `CoopSpectator.dll!CoopSpectator.MissionBehaviors.CoopMissionSpawnLogic..cctor()`
  - `CoopSpectator.dll!CoopSpectator.MissionBehaviors.CoopMissionSpawnLogic.TryRunDedicatedMissionObserver(Mission)`
  - `CoopSpectator.dll!CoopSpectator.SubModule.OnApplicationTick(Single)`

At the same time, host logs already proved the earlier module-path fix was active:

- exact item registry built with real items, not zero
- `BattleSnapshotRuntimeState` loaded correctly
- crash still happened right after `IMono_MBMission::create_mission`

So the strongest low-level boundary was no longer item availability. It was the first dedicated observer touch of `CoopMissionSpawnLogic` during the mission-open timing window.

### 23b. Root cause

The first fix confirmed the original hypothesis only partially.

It did move the crash away from the immediate `CoopMissionSpawnLogic..cctor()` boundary, but the next foreign-host dump still showed the dedicated process dying before the mission fully left the native `StartUp` window.

Local successful logs showed that the old observer path still attached `CoopMissionNetworkBridge` / `CoopMissionSpawnLogic` while mission mode was still `StartUp`, right after `IMono_MBMission::create_mission`.

So the real boundary was not just "wait one second". The real boundary was:

1. mission must exist,
2. native mission mode must leave `StartUp`,
3. core native mission behaviors must already exist,
4. only then may the dedicated observer and mission-model override touch the mission.

### 23c. Applied smallest fix

Updated:

- `C:\dev\projects\BannerlordCoopSpectator3\DedicatedServer\SubModule.cs`

Applied change:

1. Replaced the pure fixed-delay observer activation with a state-based gate in the dedicated submodule.
2. Dedicated now waits until all of these are true:
   - mission scene name is non-empty
   - mission mode is no longer `StartUp`
   - `MissionLobbyComponent`, `MultiplayerTimerComponent`, and `MultiplayerTeamSelectComponent` already exist
3. That ready state must stay true for `3` consecutive ticks before activation.
4. `DedicatedKnockoutOutcomeModelOverride.UpdateForMission(...)` is now also held behind the same state gate, so it no longer mutates mission models during the early `StartUp` window.
5. A `15s` timeout remains only as an emergency fallback, not as the primary contract.

This keeps the fix narrow: still no broad spawn/runtime rewrite, but now the activation depends on native mission state instead of machine speed.

### 23d. Expected next rerun behavior

1. Dedicated should first log:
   - `CoopSpectatorDedicated: deferred dedicated mission observer activation pending native mission-ready state...`
2. While the mission is still early, dedicated should log:
   - `CoopSpectatorDedicated: waiting before activating dedicated mission observer... Stage=waiting-native-ready-state ...`
3. Once the mission leaves `StartUp` and the native behavior stack is present, dedicated should log:
   - `CoopSpectatorDedicated: activating dedicated mission observer after native mission-ready state stabilized...`
4. The old crash should no longer happen during the early post-`IMono_MBMission::create_mission` window.

### 23e. Foreign-host rerun showed the direct mission behavior factory was still earlier than the observer gate

Fresh low-level review after the next foreign-host crash showed the state-based observer gate still did not protect the earliest coop touchpoint on dedicated.

Reason:

1. `MissionMultiplayerCoopBattleMode.BuildServerMissionBehaviorsForCoopBattle(...)` still injected:
   - `MissionBehaviorDiagnostic`
   - `CoopMissionNetworkBridge`
   - `CoopMissionSpawnLogic`
   directly into the initial dedicated mission behavior list.
2. Those behaviors therefore still entered `AfterStart()` during native mission-open, before the new observer/state gate in `DedicatedServer/SubModule.OnApplicationTick(...)` could even run.

Applied narrow follow-up fix:

- updated `C:\dev\projects\BannerlordCoopSpectator3\GameMode\MissionMultiplayerCoopBattleMode.cs`
- dedicated server mission factory now logs:
  - `deferred CoopMissionNetworkBridge/CoopMissionSpawnLogic initial mission behavior injection for dedicated process`
- on dedicated, initial server mission behavior construction no longer injects the coop mission behaviors directly
- the state-based dedicated observer is now the only path that attaches coop server mission behaviors after native mission-ready stabilization

Expected rerun behavior after this follow-up:

1. Dedicated should first log the new mission-factory defer line.
2. Dedicated should then log the existing state-gate lines from section `23d`.
3. Only after stabilization should dedicated log the observer attachment of:
   - `CoopMissionNetworkBridge`
   - `CoopMissionSpawnLogic`

### 23f. Dedicated `rgl_log_10768` proved the new build was active and the crash is now earlier than any coop mission-behavior attach

Fresh foreign-host dedicated logs finally included the missing dedicated `rgl_log_10768.txt`, which closed the previous uncertainty about whether the host had actually installed the new package.

Authoritative proof from that log:

1. The dedicated process was running the new server build:
   - `SERVER_BINARY_ID ... MVID=27f52031-9e5b-4cdd-b418-f625a3d50c0b`
2. The module-path / item-registry fixes were active and healthy:
   - `ModulePathHelper: resolved module root via explicit game root environment...`
   - `ExactCampaignRuntimeItemRegistry: built exact campaign item index. IndexedItems=1265`
3. The new state-based gate was also active:
   - `deferred dedicated mission observer activation pending native mission-ready state...`
   - `waiting before activating dedicated mission observer... Mode=StartUp ...`
4. But there were still **no** logs for:
   - `CoopBattle CreateBehaviorsForMission ...`
   - `deferred CoopMissionNetworkBridge/CoopMissionSpawnLogic initial mission behavior injection for dedicated process`
   - `activating dedicated mission observer after native mission-ready state stabilized`
   - `dedicated observer attached CoopMissionNetworkBridge ...`
   - `dedicated observer attached CoopMissionSpawnLogic ...`

This changed the low-level conclusion:

1. The foreign-host crash is no longer attributable to:
   - missing base modules
   - missing item catalogs
   - early observer activation
   - early dedicated observer attach of coop mission behaviors
2. The server now dies while the official native `MultiplayerBattle` mission is still in `StartUp`, after `MissionState.OpenNew EXIT` and before the mission ever becomes observer-ready.
3. The absence of any `MissionMultiplayerCoopBattleMode` factory logs also indicates the foreign-host path is currently crashing inside the native official `MultiplayerBattle` startup contract, before our custom coop behavior factory becomes relevant.

Applied follow-up diagnostics for the next rerun:

- updated `C:\dev\projects\BannerlordCoopSpectator3\Patches\MissionStateOpenNewPatches.cs`
  - logs authoritative handler metadata for `MissionState.OpenNew(...)`
- updated `C:\dev\projects\BannerlordCoopSpectator3\Patches\BattleShellSuppressionPatch.cs`
  - logs a one-shot observation on entry to native `MultiplayerWarmupComponent.AfterStart`

These diagnostics are intended to answer the next exact question:

1. Which mission behavior handler is actually passed into native `MissionState.OpenNew("MultiplayerBattle", ...)` on foreign-host dedicated startup?
2. Does the official native lifecycle reach `MultiplayerWarmupComponent.AfterStart`, or does the crash happen even earlier during battle-shell startup?

### 23g. Foreign-host rerun proved the dedicated server uses the official native `OpenBattleMission` handler and dies before the first server battle-shell `AfterStart`

Fresh host-only logs answered both diagnostic questions from `23f`.

Authoritative dedicated evidence:

1. `MissionState.OpenNew handler contract...` logged:
   - `HandlerDeclaringType=TaleWorlds.MountAndBlade.Multiplayer.MultiplayerMissions+<>c`
   - `HandlerMethod=<OpenBattleMission>b__3_0`
   - `IsServer=True`
2. `MissionState.OpenNew EXIT missionName=MultiplayerBattle` was reached successfully.
3. There were still no server-side logs for:
   - `deferred dedicated mission observer activation...`
   - `waiting before activating dedicated mission observer...`
   - `BattleShellSuppressionPatch: observed native MultiplayerWarmupComponent.AfterStart entry...`

At the same time, the remote client log for the same run did reach:

- `BattleShellSuppressionPatch: observed native MultiplayerWarmupComponent.AfterStart entry...`

This narrows the current foreign-host crash boundary to:

1. **after** `MissionState.OpenNew("MultiplayerBattle", ...)` returns on dedicated,
2. **before** the first dedicated `Mission.AfterStart` / `MultiplayerWarmupComponent.AfterStart` battle-shell step becomes observable,
3. while the dedicated server is still in the official native `OpenBattleMission` lifecycle rather than any coop-specific mission handler.

Applied next-step diagnostics:

- updated `C:\dev\projects\BannerlordCoopSpectator3\Patches\BattleShellSuppressionPatch.cs`
  - logs one-shot server battle-start steps for:
    - `Mission.AfterStart`
    - `MissionLobbyComponent.AfterStart`
    - `MultiplayerRoundController.AfterStart`
    - `MissionMultiplayerFlagDomination.AfterStart`
    - `MultiplayerWarmupComponent.AfterStart`

The next foreign-host rerun should now reveal the last official server battle-start step that occurs before the native access violation.

### 23h. Foreign-host rerun with `SuccessfulPatches=11` still died before `FinishMissionLoading`

Fresh dedicated `rgl_log_12136.txt` proved the host had already installed the next diagnostics build:

1. `SERVER_BINARY_ID ... MVID=dcd7236f-e06b-4e36-8988-0bdb7a5d741b`
2. `BattleShellSuppressionPatch: patched TaleWorlds.MountAndBlade.MissionState.FinishMissionLoading.`
3. `BattleShellSuppressionPatch: patched TaleWorlds.MountAndBlade.Mission.Tick.`
4. `BattleShellSuppressionPatch: native warmup/timer suppression patch pass completed. SuccessfulPatches=11.`

The same log then reached:

1. `MissionState.OpenNew handler contract... HandlerMethod=<OpenBattleMission>b__3_0 ... IsServer=True`
2. `IMono_MBMission::create_mission`
3. `MissionState.OpenNew EXIT missionName=MultiplayerBattle`

And still contained **none** of:

1. `BattleShellSuppressionPatch: observed MissionState.FinishMissionLoading entry...`
2. `BattleShellSuppressionPatch: observed native Mission.Tick during mission-loading window...`
3. `BattleShellSuppressionPatch: observed official battle startup step. Source=Mission.AfterStart ...`

This moved the crash boundary again:

1. It is now **after** `MissionState.OpenNew EXIT`,
2. but still **before** any observed `FinishMissionLoading` or mission-loading `Mission.Tick`,
3. which means the foreign-host crash is earlier than the `FinishMissionLoading -> CurrentMission.Tick -> AfterStart` window.

Applied next-step diagnostics:

- updated `C:\dev\projects\BannerlordCoopSpectator3\Patches\BattleShellSuppressionPatch.cs`
  - now also logs one-shot early mission-loading lifecycle steps for:
    - `MissionState.OnActivate`
    - `MissionState.OnTick` while mission state is `NewlyCreated/Initializing`
    - `Mission.ClearUnreferencedResources`
    - `MissionState.LoadMission`
    - `Mission.Initialize`
    - `Mission.OnMissionStateActivate`

The next foreign-host rerun should now tell us whether the dedicated server dies:

1. before state activation,
2. during the first loading tick,
3. during `ClearUnreferencedResources`,
4. or inside `Mission.Initialize` itself.

### 23i. Foreign-host rerun proved the dedicated server now reaches `MissionState.LoadMission` but still dies before `Mission.Initialize`

Fresh dedicated `rgl_log_6540.txt` finally crossed the earlier `OpenNew EXIT` boundary and confirmed the next exact loading step.

Authoritative evidence:

1. The host again ran the latest diagnostics build:
   - `SERVER_BINARY_ID ... MVID=844f8b99-0780-49c5-93f7-ea5f579867e0`
   - `SuccessfulPatches=17`
2. The dedicated server now logged:
   - `Source=MissionState.OnActivate`
   - `Source=Mission.OnMissionStateActivate`
   - `Source=MissionState.OnTick loading-step`
   - `Source=Mission.ClearUnreferencedResources`
   - `Source=MissionState.LoadMission`
3. The same log still contained **no**:
   - `Source=Mission.Initialize`
   - `Source=Mission.Initialize completed`
   - `Source=MissionState.LoadMission completed`

This narrows the current foreign-host crash boundary to:

1. after `MissionState.LoadMission` entry,
2. after `Mission.ClearUnreferencedResources`,
3. but before the first observed `Mission.Initialize` entry.

Given the native `LoadMission()` sequence, the remaining window is now essentially:

1. `missionBehavior.OnMissionScreenPreLoad()` for one of the official battle-shell behaviors, or
2. `Utilities.ClearOldResourcesAndObjects()` / immediate return from it right before `CurrentMission.Initialize()`.

Applied next-step diagnostics:

- updated `C:\dev\projects\BannerlordCoopSpectator3\Patches\BattleShellSuppressionPatch.cs`
  - added probes for:
    - `TaleWorlds.Engine.Utilities.ClearOldResourcesAndObjects` prefix/postfix
    - `MissionState.LoadMission` postfix
    - `Mission.Initialize` postfix

The next foreign-host rerun should now tell us whether the dedicated server dies:

1. inside engine cleanup,
2. immediately after engine cleanup returns,
3. or on the first managed/native entry into `Mission.Initialize()`.

### 23j. Foreign-host rerun proved the dedicated server now reaches `TickLoading completed` and `LoadMission completed`, but still dies before any observed `Mission.Initialize`

Fresh dedicated `rgl_log_8256.txt` moved the boundary one more step forward and revealed an internal inconsistency worth probing directly.

Authoritative evidence:

1. The host again ran the latest diagnostics build:
   - `SERVER_BINARY_ID ... MVID=3e1d5f62-2fbf-45fd-b26a-6c6f9020cbc0`
   - `SuccessfulPatches=25`
2. The same dedicated log now shows:
   - `Source=MissionState.TickLoading`
   - `Source=MissionState.TickLoading completed`
   - `Source=MissionState.LoadMission`
   - `Source=MissionState.LoadMission completed`
3. The same run still contains **none** of:
   - `Source=Mission.Initialize`
   - `Source=Mission.Initialize completed`
   - `Source=MissionState.FinishMissionLoading`

This means the foreign-host crash boundary is now:

1. after `MissionState.TickLoading completed`,
2. after `MissionState.LoadMission completed`,
3. but still before any observed `Mission.Initialize` / `FinishMissionLoading` progression.

That is notable because the native `MissionState.LoadMission()` contract decompiles to:

1. `MissionBehavior.OnMissionScreenPreLoad()`
2. `Utilities.ClearOldResourcesAndObjects()`
3. `_missionInitializing = true`
4. `CurrentMission.Initialize()`

So the next question is no longer "does it reach `LoadMission`?", but rather:

1. what `_missionInitializing` and `_tickCountBeforeLoad` look like on the exact `TickLoading / LoadMission` boundary,
2. and whether the crash happens right at the `CurrentMission.Initialize()` edge or whether the mission-state bookkeeping itself is drifting before that.

Applied next-step diagnostics:

- updated `C:\dev\projects\BannerlordCoopSpectator3\Patches\BattleShellSuppressionPatch.cs`
  - added a dedicated `mission-state loader boundary` log for:
    - `MissionState.TickLoading`
    - `MissionState.TickLoading completed`
    - `MissionState.LoadMission`
    - `MissionState.LoadMission completed`
  - this now logs:
    - `_missionInitializing`
    - `_tickCountBeforeLoad`
    - `Mission.CurrentState`
    - `Mission.IsLoadingFinished`

The next foreign-host rerun should now tell us whether the dedicated server:

1. leaves `LoadMission` with `_missionInitializing=false` unexpectedly,
2. leaves `LoadMission` with `_missionInitializing=true` but no visible `Mission.Initialize`,
3. or dies exactly on the first transition from `NewlyCreated` toward `Initializing`.

### 23k. Foreign-host rerun proved `LoadMission completed` still leaves `_missionInitializing=false` and `MissionState=NewlyCreated`

Fresh dedicated `rgl_log_13116.txt` confirmed an exact loader-state inconsistency rather than merely "another crash near mission startup".

Authoritative evidence:

1. The host again ran the latest diagnostics build:
   - `SERVER_BINARY_ID ... MVID=4a8086f8-9158-4dbe-a180-0960e8d06b33`
   - `SuccessfulPatches=25`
2. The same log now shows:
   - `Source=MissionState.TickLoading ... MissionInitializing=False TickCountBeforeLoad=0`
   - `Source=MissionState.TickLoading completed ... MissionInitializing=False TickCountBeforeLoad=0`
   - `Source=MissionState.LoadMission ... MissionInitializing=False TickCountBeforeLoad=1`
   - `Source=MissionState.LoadMission completed ... MissionInitializing=False TickCountBeforeLoad=1`
3. The same run still contains **none** of:
   - `Source=Mission.Initialize`
   - `Source=Mission.Initialize completed`
   - `Source=MissionState.FinishMissionLoading`

This is now a direct contradiction with the decompiled native contract for `MissionState.LoadMission()`, which still is:

1. `MissionBehavior.OnMissionScreenPreLoad()`
2. `Utilities.ClearOldResourcesAndObjects()`
3. `_missionInitializing = true`
4. `CurrentMission.Initialize()`

So the current foreign-host crash boundary is no longer just "around `LoadMission`". It is now specifically:

1. after managed `MissionState.LoadMission completed` returns,
2. while mission state still reports `NewlyCreated`,
3. and while `_missionInitializing` still reports `false`,
4. which strongly suggests the failure happens on or immediately before the native/managed transition that should move the mission into `Initializing`.

Applied next-step diagnostics:

- updated `C:\dev\projects\BannerlordCoopSpectator3\Patches\BattleShellSuppressionPatch.cs`
  - now also logs `Mission.set_CurrentState(...)` transition requests

The next foreign-host rerun should now tell us whether:

1. `Mission.CurrentState` ever requests `Initializing`,
2. that request is emitted and then the process dies immediately after it,
3. or the transition request never happens at all on the crashing machine.

### 23l. Foreign-host rerun proved the current crash happens inside early `Mission.ClearUnreferencedResources(...)`, before `TickLoading`

Fresh dedicated `rgl_log_17236.txt` moved the boundary again and disproved the earlier assumption that the crash was already inside `TickLoading` or `LoadMission`.

Authoritative evidence:

1. The host again ran the latest diagnostics build:
   - `SERVER_BINARY_ID ... MVID=085bc03c-b23c-456f-94c5-7da309bd4521`
   - `SuccessfulPatches=26`
2. The same log now shows the following sequence:
   - `Mission.CurrentState transition request ... PreviousState=NewlyCreated NextState=NewlyCreated`
   - `Source=MissionState.OnActivate`
   - `Source=Mission.OnMissionStateActivate`
   - `MissionState.OpenNew EXIT missionName=MultiplayerBattle`
   - `Source=MissionState.OnTick loading-step`
   - `Source=Mission.ClearUnreferencedResources ... ForceClearGPUResources=True`
3. The same run still contains **none** of:
   - `Source=Mission.ClearUnreferencedResources completed`
   - `Source=MissionState.TickLoading`
   - `Source=MissionState.LoadMission`
   - `Source=Mission.Initialize`

At first this looked contradictory with the earlier `LoadMission` hypothesis, so `MissionState.OnTick(...)` was re-decompiled. The native contract there is:

1. if `CurrentMission.CurrentState == NewlyCreated`,
2. call `CurrentMission.ClearUnreferencedResources(CurrentMission.NeedsMemoryCleanup)`,
3. then call `TickLoading(realDt)`.

Separately, `Mission.ClearUnreferencedResources(bool forceClearGPUResources)` decompiles to:

1. `Common.MemoryCleanupGC()`
2. if `forceClearGPUResources`, call `MBAPI.IMBMission.ClearResources(Pointer)`

That matches the foreign-host dump and Visual Studio inspection:

1. native AV still occurs in `FairyTale.Library.dll`
2. faulting instruction writes through `[rsi+rax]`
3. register snapshot showed `RAX=0`, `RSI=FFFFFFFFFFFFFFFF`
4. so the native cleanup path is writing through an invalid sentinel pointer `-1`

Applied stabilization fix:

- updated `C:\dev\projects\BannerlordCoopSpectator3\Patches\BattleShellSuppressionPatch.cs`
  - `Mission.ClearUnreferencedResources(...)` now short-circuits only for:
    - dedicated server process
    - scene-aware battle runtime
    - `MissionState=NewlyCreated`
    - `IsLoadingFinished=false`
    - `ForceClearGPUResources=true`
  - added authoritative skip log:
    - `skipped early dedicated Mission.ClearUnreferencedResources to avoid native startup crash`
    - includes `MissionPointer=...`

This is intentionally narrow: it does not change client runtime, host self-join runtime, or later mission cleanup; it only avoids the exact foreign-host native cleanup call that currently crashes before the mission can even reach `TickLoading`.

### 23m. Foreign-host rerun proved the `ClearUnreferencedResources` guard works, and the next crash boundary is now inside `MissionBehavior.OnMissionScreenPreLoad()`

Fresh dedicated `rgl_log_1072.txt` confirmed that the early cleanup guard is now active and effective:

1. the host ran the new build:
   - `SERVER_BINARY_ID ... MVID=ea6a386a-7886-44ec-a61e-46d2c6a97e62`
   - `SuccessfulPatches=27`
2. the same log now shows:
   - `skipped early dedicated Mission.ClearUnreferencedResources to avoid native startup crash`
   - `Mission.ClearUnreferencedResources completed`
   - `MissionState.TickLoading`
   - `MissionState.TickLoading completed`
   - `MissionState.LoadMission`
3. but it still contains **none** of:
   - `Utilities.ClearOldResourcesAndObjects`
   - `MissionState.LoadMission completed`
   - `Mission.Initialize`

That is now a direct decompile-backed boundary, because `MissionState.LoadMission()` is:

1. `foreach (MissionBehavior missionBehavior in CurrentMission.MissionBehaviors) missionBehavior.OnMissionScreenPreLoad();`
2. `Utilities.ClearOldResourcesAndObjects();`
3. `_missionInitializing = true;`
4. `CurrentMission.Initialize();`

So after the cleanup guard moved the crash forward, the next failure point is no longer ambiguous:

1. the server enters `LoadMission`,
2. but never reaches `Utilities.ClearOldResourcesAndObjects`,
3. so the crash is now inside one of the mission behavior `OnMissionScreenPreLoad()` calls.

Applied stabilization fix:

- updated `C:\dev\projects\BannerlordCoopSpectator3\Patches\BattleShellSuppressionPatch.cs`
  - `MissionState.LoadMission` prefix now patches the actual `OnMissionScreenPreLoad()` override on every current mission behavior
  - those preload methods are then skipped only when:
    - running in dedicated server process
    - scene-aware battle runtime
    - `MissionState=NewlyCreated`
    - `IsLoadingFinished=false`
  - new authoritative logs:
    - `patched mission behavior preload hook. BehaviorType=...`
    - `skipped dedicated MissionBehavior.OnMissionScreenPreLoad during early battle startup ...`

This remains narrow: it does not affect client runtime and does not globally suppress mission behavior preloads; it only bypasses the early dedicated-only pre-screen preload phase that now appears to be the next native crash trigger on the foreign host machine.

### 23n. Foreign-host rerun proved the preload guard was not actually active yet, because Harmony was still targeting inherited `MissionBehavior.OnMissionScreenPreLoad()` methods incorrectly

Fresh foreign-host `rgl_log_11168.txt` moved the boundary again:

1. the early `Mission.ClearUnreferencedResources(...)` guard still works
2. `MissionState.TickLoading` and `MissionState.TickLoading completed` are now observed
3. `MissionState.LoadMission` and even `MissionState.LoadMission completed` are now observed
4. but the log still shows:
   - `failed to patch mission behavior preload hooks: You can only patch implemented methods/constructors. Patch the declared method virtual System.Void TaleWorlds.MountAndBlade.MissionBehavior::OnMissionScreenPreLoad() instead.`
5. the dedicated server then still crashes before any later engine cleanup / mission initialize steps

That means the previous preload stabilization idea was directionally right, but the implementation was not actually active on the foreign host machine:

1. inherited/non-overridden behavior methods were being discovered via normal reflection
2. Harmony rejected those targets because they were not declared on the concrete behavior type
3. so the early dedicated `OnMissionScreenPreLoad()` bypass was never fully in place

Applied fix:

- updated `C:\dev\projects\BannerlordCoopSpectator3\Patches\BattleShellSuppressionPatch.cs`
  - base `TaleWorlds.MountAndBlade.MissionBehavior.OnMissionScreenPreLoad()` is now patched directly during the normal startup patch pass
  - runtime preload patching now only targets real declared overrides via `BindingFlags.DeclaredOnly`
  - inherited base methods are no longer re-patched as if they were concrete behavior implementations

This keeps the fix narrow while making it real:

1. behaviors that inherit the base preload hook are now covered by the base patch
2. behaviors that implement their own override are still patched explicitly at runtime
3. the foreign host should no longer fail the preload-guard setup itself before mission startup continues

### 23o. Foreign-host rerun proved the base preload hook is now active, but `LoadMission` still crashes before postfix

Fresh foreign-host `rgl_log_1128.txt` confirmed the new dedicated build was installed:

1. `SERVER_BINARY_ID ... MVID=817d0ebd-9bd0-4238-96ea-bfb406363009`
2. `BattleShellSuppressionPatch: patched TaleWorlds.MountAndBlade.MissionBehavior.OnMissionScreenPreLoad.`
3. `SuccessfulPatches=28`

The early cleanup guard still works and the mission reaches:

1. `MissionState.TickLoading`
2. `MissionState.TickLoading completed`
3. `MissionState.LoadMission`

After `MissionState.LoadMission`, the log now contains one more authoritative line:

1. `Mission.get_IsLoadingFinished completed ... Result=False`

and then the dedicated server still dies with native AV before:

1. `MissionState.LoadMission completed`
2. `Utilities.ClearOldResourcesAndObjects`
3. `Mission.Initialize`

That means the base preload hook is at least entering the window now, but the current telemetry is still insufficient to say whether the crash is:

1. inside the first concrete `MissionBehavior.OnMissionScreenPreLoad()` call,
2. inside our own preload guard while resolving the exact runtime behavior type,
3. or on the next behavior in the same loop before `LoadMission` can return.

Applied diagnostic hardening:

- updated `C:\dev\projects\BannerlordCoopSpectator3\Patches\BattleShellSuppressionPatch.cs`
  - removed `mission.IsLoadingFinished` from the dedicated preload skip predicate
  - removed `mission.IsLoadingFinished` from the preload skip log payload
  - added:
    - `observed mission behavior preload stack ...`
    - `entering MissionBehavior.OnMissionScreenPreLoad ...`

This keeps the fix narrow while turning the next foreign-host rerun into an exact runtime behavior-stack capture instead of another blind `LoadMission` crash.

### 23p. Foreign-host rerun proved the crash is now after the fifth skipped preload behavior, before engine cleanup

Fresh foreign-host logs:

- `C:\Users\Admin\Downloads\VVS logs\rgl_log_7420.txt`
- `C:\Users\Admin\Downloads\VVS logs\watchdog_log_7420.txt`

Authoritative evidence:

1. The host ran the expected package build:
   - `SERVER_BINARY_ID ... MVID=bfcf6163-aa71-4111-a890-da0efd663bd8`
   - `SuccessfulPatches=28`
2. The early `Mission.ClearUnreferencedResources(true)` guard still works:
   - `skipped early dedicated Mission.ClearUnreferencedResources to avoid native startup crash`
   - `Mission.ClearUnreferencedResources completed`
3. The dedicated server reaches:
   - `MissionState.TickLoading`
   - `MissionState.TickLoading completed`
   - `observed mission behavior preload stack`
   - `MissionState.LoadMission`
   - `MissionState.LoadMission completed`
4. The captured preload stack has 26 official battle-shell behaviors. The last observed per-behavior skip is:
   - `TaleWorlds.MountAndBlade.SpawnComponent#4`
5. The next behavior in the stack is:
   - `TaleWorlds.MountAndBlade.MissionHardBorderPlacer#5`
6. The same log still contains no:
   - `Utilities.ClearOldResourcesAndObjects`
   - `Mission.Initialize`
7. The watchdog confirms the same native access violation shape:
   - `ExceptionCode: 0xC0000005`
   - `Parameter-0: 0x1`
   - `Parameter-1: 0xffffffffffffffff`

This narrows the active crash boundary to the native/managed preload dispatch loop itself: after returning from the skipped `SpawnComponent` preload call and before the next observable preload entry or engine cleanup.

Applied stabilization fix:

- updated `C:\dev\projects\BannerlordCoopSpectator3\Patches\BattleShellSuppressionPatch.cs`
  - `MissionState.LoadMission` now has a dedicated-only manual preload bypass for:
    - dedicated server process
    - scene-aware battle runtime
    - `MissionState=NewlyCreated`
  - the bypass skips the whole early `MissionBehavior.OnMissionScreenPreLoad()` loop and then runs the remaining decompiled `LoadMission()` contract:
    - `Utilities.ClearOldResourcesAndObjects()`
    - `_missionInitializing = true`
    - `CurrentMission.Initialize()`
  - added authoritative log:
    - `skipped dedicated MissionBehavior.OnMissionScreenPreLoad loop during early battle startup`

Updated package after this fix:

- `C:\dev\projects\BannerlordCoopSpectator3\dist\BannerlordCoopCampaign_v0.1.1_Release.zip`
- SHA256: `C5035EEDD2950D61B009538CDA16D99AFFFB58976F8246049D6B0B3DCC41AE28`
- Host DLL MVID: `afda82df-cff9-4eb4-a6bc-37fce9bde1e6`
- Client DLL MVID: `f4c19e2f-c634-4514-ab12-240267951aa2`

The next foreign-host rerun should check for:

1. `skipped dedicated MissionBehavior.OnMissionScreenPreLoad loop during early battle startup`
2. `Utilities.ClearOldResourcesAndObjects`
3. `Utilities.ClearOldResourcesAndObjects completed`
4. `Mission.Initialize`
5. `Mission.Initialize completed`
6. `MissionState.FinishMissionLoading`

If it still crashes, the new boundary should be either engine cleanup or mission initialize, and the new dump is worth collecting.

### 23q. Foreign-host rerun entered the manual preload bypass but still died before engine cleanup

Fresh foreign-host logs:

- `C:\Users\Admin\Downloads\VVS logs\rgl_log_15868.txt`
- `C:\Users\Admin\Downloads\VVS logs\watchdog_log_15868.txt`

Authoritative evidence:

1. The host ran the expected package build:
   - `SERVER_BINARY_ID ... MVID=afda82df-cff9-4eb4-a6bc-37fce9bde1e6`
   - `SuccessfulPatches=28`
2. The old early cleanup guard still works:
   - `skipped early dedicated Mission.ClearUnreferencedResources to avoid native startup crash`
   - `Mission.ClearUnreferencedResources completed`
3. The server entered the new manual preload bypass:
   - `Source=MissionState.LoadMission dedicated manual preload bypass`
4. The same log still contains no:
   - `skipped dedicated MissionBehavior.OnMissionScreenPreLoad loop during early battle startup`
   - `Utilities.ClearOldResourcesAndObjects`
   - `Mission.Initialize`
5. The watchdog again confirms the same native access violation shape:
   - `ExceptionCode: 0xC0000005`
   - `Parameter-0: 0x1`
   - `Parameter-1: 0xffffffffffffffff`

This means the crash is now inside the first manual bypass bookkeeping/logging window, not in the original per-behavior preload loop and not yet inside `Utilities.ClearOldResourcesAndObjects`.

Applied next-step hardening:

- updated `C:\dev\projects\BannerlordCoopSpectator3\Patches\BattleShellSuppressionPatch.cs`
  - removed post-entry `MissionBehaviors` inspection from the manual bypass path
  - removed `LogMissionStateLoaderObservation(...)` from the manual bypass path because it calls `Mission.IsLoadingFinished`
  - removed per-behavior skip logging from the manual bypass path
  - added minimal breadcrumb logs:
    - `dedicated manual MissionState.LoadMission preload bypass step. Step=entered`
    - `Step=before engine cleanup`
    - `Step=after engine cleanup`
    - `Step=before mission-initializing flag`
    - `Step=before Mission.Initialize`
    - `Step=completed`

Updated package after this hardening:

- `C:\dev\projects\BannerlordCoopSpectator3\dist\BannerlordCoopCampaign_v0.1.1_Release.zip`
- SHA256: `A28051103E42AC4ADFA2B11D74263B5B57A7E8FF6854AF39E7B45A345562AD26`
- Host DLL MVID: `e0d1a207-d4ea-4706-9195-ea5bd46fa94d`
- Client DLL MVID: `2108a49f-3206-44b9-bd50-31da5f6e2158`

The next foreign-host rerun should check the last observed `dedicated manual MissionState.LoadMission preload bypass step` value. If it reaches `before engine cleanup` and then dies, the next stabilization target is skipping `Utilities.ClearOldResourcesAndObjects()` in this dedicated early-start path. If it reaches `before Mission.Initialize` and then dies, the new dump is useful.

### 23r. ILSpy confirmed the engine cleanup wrapper and the dedicated manual bypass now skips it

Fresh foreign-host logs:

- `C:\Users\Admin\Downloads\VVS logs\rgl_log_10024.txt`
- `C:\Users\Admin\Downloads\VVS logs\watchdog_log_10024.txt`

Authoritative evidence:

1. The host ran the expected package build:
   - `SERVER_BINARY_ID ... MVID=e0d1a207-d4ea-4706-9195-ea5bd46fa94d`
   - `SuccessfulPatches=28`
2. The old early cleanup guard still works:
   - `skipped early dedicated Mission.ClearUnreferencedResources to avoid native startup crash`
   - `Mission.ClearUnreferencedResources completed`
3. The server entered the minimal manual preload bypass and reached:
   - `dedicated manual MissionState.LoadMission preload bypass step. Step=entered`
   - `dedicated manual MissionState.LoadMission preload bypass step. Step=before engine cleanup`
4. The same log still contains no:
   - `dedicated manual MissionState.LoadMission preload bypass step. Step=after engine cleanup`
   - `Mission.Initialize`
   - `Mission.Initialize completed`
   - `MissionState.FinishMissionLoading`
5. The watchdog again confirms the same native access violation shape:
   - `ExceptionCode: 0xC0000005`
   - `Parameter-0: 0x1`
   - `Parameter-1: 0xffffffffffffffff`

This narrows the active crash boundary to `TaleWorlds.Engine.Utilities.ClearOldResourcesAndObjects()` in the dedicated early-start `MissionState.LoadMission` path.

ILSpy confirmation:

- `MissionState.TickLoading()` calls `LoadMission()` once `_tickCountBeforeLoad > 0` and `_missionInitializing == false`.
- `MissionState.LoadMission()` runs:
  - every `MissionBehavior.OnMissionScreenPreLoad()`
  - `Utilities.ClearOldResourcesAndObjects()`
  - `_missionInitializing = true`
  - `CurrentMission.Initialize()`
- `Utilities.ClearOldResourcesAndObjects()` is only a managed wrapper over:
  - `EngineApplicationInterface.IUtil.ClearOldResourcesAndObjects()`

This means the current crash is not a broad client/host connection issue. It is another native engine resource-cleanup call that is unsafe during foreign-host dedicated battle startup, similar in shape to the already-fixed early `Mission.ClearUnreferencedResources(true)` crash.

Applied stabilization fix:

- updated `C:\dev\projects\BannerlordCoopSpectator3\Patches\BattleShellSuppressionPatch.cs`
  - the dedicated-only manual `MissionState.LoadMission` preload bypass now skips `Utilities.ClearOldResourcesAndObjects()`
  - it proceeds directly to:
    - `_missionInitializing = true`
    - `Mission.Initialize()`
  - added authoritative breadcrumb:
    - `dedicated manual MissionState.LoadMission preload bypass step. Step=skipped engine cleanup`

Updated package after this fix:

- `C:\dev\projects\BannerlordCoopSpectator3\dist\BannerlordCoopCampaign_v0.1.1_Release.zip`
- SHA256: `31E0C1C83E1486055B63592E2609AD0F356E2A7A95050764B9183309D613D038`
- Host DLL MVID: `40f081ab-364a-4ca7-ad2c-881b2e19ef75`
- Client DLL MVID: `2442be01-daf5-44c2-b3f2-2689c6f74864`

The next foreign-host rerun should check for:

1. `dedicated manual MissionState.LoadMission preload bypass step. Step=skipped engine cleanup`
2. `dedicated manual MissionState.LoadMission preload bypass step. Step=before mission-initializing flag`
3. `dedicated manual MissionState.LoadMission preload bypass step. Step=before Mission.Initialize`
4. `Mission.Initialize`
5. `Mission.Initialize completed`
6. `MissionState.FinishMissionLoading`

For the `10024` run, a dump is not needed because the breadcrumb boundary is already exact. If the next run reaches `before Mission.Initialize` and then dies, the dump becomes useful again.

### 23s. Foreign-host rerun moved the active boundary up to MissionState.OnTick before TickLoading

Fresh foreign-host logs:

- `C:\Users\Admin\Downloads\VVS logs\rgl_log_11836.txt`
- `C:\Users\Admin\Downloads\VVS logs\watchdog_log_11836.txt`

Authoritative evidence:

1. The host ran the expected package build:
   - `SERVER_BINARY_ID ... MVID=40f081ab-364a-4ca7-ad2c-881b2e19ef75`
   - `SuccessfulPatches=28`
2. The server reached mission creation and activation:
   - `MissionState.OpenNew EXIT missionName=MultiplayerBattle`
   - `MissionState.OnActivate`
   - `Mission.OnMissionStateActivate`
3. The last observed mod boundary was:
   - `BattleShellSuppressionPatch: observed early mission-loading lifecycle step. Source=MissionState.OnTick loading-step ... MissionState=NewlyCreated ...`
4. The same log contains no runtime entry for:
   - `Mission.ClearUnreferencedResources`
   - `MissionState.TickLoading`
   - `MissionState.LoadMission`
   - `dedicated manual MissionState.LoadMission preload bypass step`
   - `Mission.Initialize`
5. The watchdog again confirms the same native access violation shape:
   - `ExceptionCode: 0xC0000005`
   - `Parameter-0: 0x1`
   - `Parameter-1: 0xffffffffffffffff`
   - dump path: `C:\Users\user\AppData\Local\Temp\CoopSpectatorDedicated_logs/\crashes\2026-04-24_21.10.24\dump.dmp`

ILSpy confirmation for the new boundary:

- `MissionState.OnTick(float realDt)` starts with:
  - `base.OnTick(realDt)`
  - delayed disconnect check
  - `CurrentMission.ClearUnreferencedResources(CurrentMission.NeedsMemoryCleanup)` while `CurrentState == NewlyCreated`
  - `TickLoading(realDt)`
- The `11836` log stops after our `MissionState.OnTick` prefix and before the `Mission.ClearUnreferencedResources` prefix, which means the active crash boundary is now inside original `MissionState.OnTick` before the loading branch reaches `TickLoading`.

Applied stabilization fix:

- updated `C:\dev\projects\BannerlordCoopSpectator3\Patches\BattleShellSuppressionPatch.cs`
  - `MissionState.OnTick` prefix now returns `false` for dedicated-only early battle startup loading states:
    - dedicated server process
    - scene-aware battle runtime
    - `MissionState=NewlyCreated` or `MissionState=Initializing`
  - the bypass skips original `base.OnTick` and the early `ClearUnreferencedResources` call
  - it manually invokes private `MissionState.TickLoading(realDt)` so the existing `LoadMission`/`FinishMissionLoading` instrumentation remains authoritative
  - added authoritative breadcrumbs:
    - `dedicated manual MissionState.OnTick loading bypass step. Step=entered`
    - `Step=skipped base OnTick and ClearUnreferencedResources`
    - `Step=before manual TickLoading`
    - `Step=after manual TickLoading`

Updated package after this fix:

- `C:\dev\projects\BannerlordCoopSpectator3\dist\BannerlordCoopCampaign_v0.1.1_Release.zip`
- SHA256: `DEC13028A9544F1DE860F5371A0F677367DE2790A63D58420A1F424A1E45D0C9`
- Host DLL MVID: `405c49e5-7295-4dc9-bb28-0fb66c1acba5`
- Client DLL MVID: `0f2e4636-b6d9-493d-b2f8-e311186f1b16`

The next foreign-host rerun should check for:

1. `dedicated manual MissionState.OnTick loading bypass step. Step=entered`
2. `dedicated manual MissionState.OnTick loading bypass step. Step=skipped base OnTick and ClearUnreferencedResources`
3. `dedicated manual MissionState.OnTick loading bypass step. Step=before manual TickLoading`
4. `MissionState.TickLoading`
5. `dedicated manual MissionState.LoadMission preload bypass step. Step=skipped engine cleanup`
6. `dedicated manual MissionState.LoadMission preload bypass step. Step=before Mission.Initialize`
7. `Mission.Initialize`
8. `Mission.Initialize completed`
9. `MissionState.FinishMissionLoading`

For the `11836` run, a dump is not needed because the log boundary is already exact. If the next run reaches `before Mission.Initialize` or `MissionState.FinishMissionLoading` and then dies, the dump becomes useful again.

### 23t. Foreign-host rerun proved reflection TickLoading re-entered the unsafe original private path

Fresh foreign-host logs:

- `C:\Users\Admin\Downloads\VVS logs\rgl_log_6660.txt`
- `C:\Users\Admin\Downloads\VVS logs\watchdog_log_6660.txt`

Authoritative evidence:

1. The host ran the expected package build:
   - `SERVER_BINARY_ID ... MVID=405c49e5-7295-4dc9-bb28-0fb66c1acba5`
   - `SuccessfulPatches=28`
2. The dedicated `MissionState.OnTick` bypass worked:
   - `dedicated manual MissionState.OnTick loading bypass step. Step=entered`
   - `Step=skipped base OnTick and ClearUnreferencedResources`
   - `Step=before manual TickLoading`
3. The server entered `MissionState.TickLoading`:
   - `observed mission-state loader boundary. Source=MissionState.TickLoading ... MissionInitializing=False TickCountBeforeLoad=0`
4. The same log contains no:
   - `MissionState.LoadMission`
   - `dedicated manual MissionState.LoadMission preload bypass step`
   - `Mission.Initialize`
5. The watchdog again confirms the same native access violation shape:
   - `ExceptionCode: 0xC0000005`
   - `Parameter-0: 0x1`
   - `Parameter-1: 0xffffffffffffffff`
   - dump path: `C:\Users\user\AppData\Local\Temp\CoopSpectatorDedicated_logs/\crashes\2026-04-24_21.20.55\dump.dmp`

This means invoking private `MissionState.TickLoading(realDt)` by reflection still re-enters the unsafe original private loading path before our `LoadMission` prefix can take over. Practically, the dedicated startup path must not call original `TickLoading` at all.

Applied stabilization fix:

- updated `C:\dev\projects\BannerlordCoopSpectator3\Patches\BattleShellSuppressionPatch.cs`
  - the dedicated-only `MissionState.OnTick` bypass no longer invokes original private `TickLoading`
  - it manually performs the small decompiled `TickLoading` contract:
    - increments `_tickCountBeforeLoad`
    - calls the already hardened manual `LoadMission` bypass
    - skips `Utilities.SetLoadingScreenPercentage(0.01f)` in this early dedicated path
  - for later `_missionInitializing=true` ticks, it checks `Mission.IsLoadingFinished` and manually invokes `FinishMissionLoading` only when ready
  - added authoritative breadcrumbs:
    - `dedicated manual MissionState.OnTick loading bypass step. Step=manual TickLoading advanced tick count`
    - `Step=before manual LoadMission`
    - `Step=after manual LoadMission`
    - `Step=skipped loading screen percentage`

Updated package after this fix:

- `C:\dev\projects\BannerlordCoopSpectator3\dist\BannerlordCoopCampaign_v0.1.1_Release.zip`
- SHA256: `648F1635446E9E51020A27B45391782D9189A2B195F1D657C8DF8C901B1DFB41`
- Host DLL MVID: `40ad70cd-b26b-49bc-bf72-d43d19093476`
- Client DLL MVID: `15b0842a-5d19-4a2d-bbf1-f063916133cf`

The next foreign-host rerun should check for:

1. `dedicated manual MissionState.OnTick loading bypass step. Step=manual TickLoading advanced tick count`
2. `dedicated manual MissionState.OnTick loading bypass step. Step=before manual LoadMission`
3. `dedicated manual MissionState.LoadMission preload bypass step. Step=skipped engine cleanup`
4. `dedicated manual MissionState.LoadMission preload bypass step. Step=before Mission.Initialize`
5. `Mission.Initialize`
6. `Mission.Initialize completed`
7. `dedicated manual MissionState.OnTick loading bypass step. Step=after manual LoadMission`
8. `dedicated manual MissionState.OnTick loading bypass step. Step=skipped loading screen percentage`

For the `6660` run, a dump is not needed because the log boundary is already exact. If the next run reaches `before Mission.Initialize` and then dies, collect the dump.

### 23u. Foreign-host rerun showed even OnTick breadcrumbs are unsafe before direct LoadMission

Fresh foreign-host logs:

- `C:\Users\Admin\Downloads\VVS logs\rgl_log_9612.txt`
- `C:\Users\Admin\Downloads\VVS logs\watchdog_log_9612.txt`

Authoritative evidence:

1. The host ran the expected package build:
   - `SERVER_BINARY_ID ... MVID=40ad70cd-b26b-49bc-bf72-d43d19093476`
   - `SuccessfulPatches=28`
2. The dedicated `MissionState.OnTick` bypass reached:
   - `dedicated manual MissionState.OnTick loading bypass step. Step=entered`
   - `Step=skipped base OnTick and ClearUnreferencedResources`
3. The same log contains no:
   - `manual TickLoading advanced tick count`
   - `before direct manual LoadMission`
   - `dedicated manual MissionState.LoadMission preload bypass step`
4. The watchdog again confirms the same native access violation shape:
   - `ExceptionCode: 0xC0000005`
   - `Parameter-0: 0x1`
   - `Parameter-1: 0xffffffffffffffff`
   - dump path: `C:\Users\user\AppData\Local\Temp\CoopSpectatorDedicated_logs/\crashes\2026-04-24_21.28.53\dump.dmp`

This means even extra `MissionState.OnTick` breadcrumb/property activity is unsafe before entering the manual `LoadMission` path. The fix now prioritizes doing the work first and letting the existing `LoadMission` breadcrumbs define the next boundary.

Applied stabilization fix:

- updated `C:\dev\projects\BannerlordCoopSpectator3\Patches\BattleShellSuppressionPatch.cs`
  - in dedicated `NewlyCreated` battle startup, the `MissionState.OnTick` prefix now immediately calls `TryHandleDedicatedEarlyLoadMissionWithoutPreload(...)`
  - removed the `NewlyCreated` OnTick breadcrumbs before the manual load call
  - keeps original `OnTick` skipped even if manual LoadMission is unavailable, to avoid re-entering the known unsafe native path

Verified the packaged host DLL with ILSpy:

- `TryHandleDedicatedEarlyMissionStateOnTick(...)`
  - checks dedicated server, current mission, `NewlyCreated`/`Initializing`, and scene-aware battle runtime
  - for `NewlyCreated`, directly calls `TryHandleDedicatedEarlyLoadMissionWithoutPreload(missionStateInstance)`
  - no longer logs `entered`, `skipped base OnTick`, or `before direct manual LoadMission` in that branch

Updated package after this fix:

- `C:\dev\projects\BannerlordCoopSpectator3\dist\BannerlordCoopCampaign_v0.1.1_Release.zip`
- SHA256: `67DF72429A7EE27A553CAD3AB182C7631BDACA894ED2A3BC05C4CD1C4934E731`
- Host DLL MVID: `78544a60-df5b-4e0b-8c52-598f4e655fbb`
- Client DLL MVID: `cdc34b73-f774-4b71-8ced-c496cbb859cf`

The next foreign-host rerun should check for:

1. `dedicated manual MissionState.LoadMission preload bypass step. Step=entered`
2. `dedicated manual MissionState.LoadMission preload bypass step. Step=skipped engine cleanup`
3. `dedicated manual MissionState.LoadMission preload bypass step. Step=before mission-initializing flag`
4. `dedicated manual MissionState.LoadMission preload bypass step. Step=before Mission.Initialize`
5. `Mission.Initialize`
6. `Mission.Initialize completed`

For the `9612` run, a dump is not needed because the log boundary is exact. If the next run reaches `before Mission.Initialize` and then dies, collect the dump.

### 23v. Crash/no-crash comparison showed the foreign host is missing staged SandBox/SandBoxCore assets

Compared crash-machine logs:

- `C:\Users\Admin\Downloads\VVS logs\rgl_log_9612.txt`
- `C:\Users\Admin\Downloads\VVS logs\watchdog_log_9612.txt`

against local no-crash logs:

- `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\logs\rgl_log_55308.txt`
- `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\logs\watchdog_log_55308.txt`

Authoritative difference:

1. The crash machine did not have the same dedicated runtime layout:
   - crash host: `SERVER_BINARY_ID ... Win64_Shipping_Server\CoopSpectator.dll ... MVID=40ad70cd-b26b-49bc-bf72-d43d19093476`
   - local no-crash host: `SERVER_BINARY_ID ... Win64_Shipping_Client\CoopSpectator.dll ... MVID=78544a60-df5b-4e0b-8c52-598f4e655fbb`
2. The crash machine had no staged scene registry/assets in the dedicated `Modules` tree:
   - `module-owned scenes. Module=SandBoxCore Count=0 ContainsBattleTerrainN=False ContainsBattleTerrainBiome087b=False`
   - `scene resolution. Scene=battle_terrain_n PathResolved=False`
   - `scene resolution. Scene=battle_terrain_biome_087b PathResolved=False`
   - `exact bootstrap runtime files ... HasSandBoxModule=False HasSandBoxCoreModule=False HasSpBattleScenesXml=False HasBattleTerrainNScene=False HasBattleTerrainBiome087bScene=False`
3. The local no-crash machine did have those files:
   - `module-owned scenes. Module=SandBoxCore Count=98 ContainsBattleTerrainN=True ContainsBattleTerrainBiome087b=True`
   - `scene resolution. Scene=battle_terrain_n PathResolved=True`
   - `scene resolution. Scene=battle_terrain_biome_087b PathResolved=True ... UniqueSceneIdResolved=True`
   - `exact bootstrap runtime files ... HasSandBoxModule=True HasSandBoxCoreModule=True HasSpBattleScenesXml=True HasBattleTerrainNScene=True HasBattleTerrainBiome087bScene=True`

Conclusion:

- this is not primarily a Windows 10 vs Windows 11 signal
- this is not primarily a port/firewall signal, because both hosts reach the pre-client mission startup boundary
- the strongest mismatch is packaging/deployment: local MSBuild deploy stages `SandBox`/`SandBoxCore` runtime assets into the dedicated install, but the release zip only shipped `CoopSpectatorDedicated`

Applied packaging fix:

- updated `C:\dev\projects\BannerlordCoopSpectator3\scripts\CreateReleasePackage.ps1`
  - release `Host\Modules` now includes:
    - `SandBox\SubModule.xml`
    - `SandBox\ModuleData\**\*.xml`
    - `SandBoxCore\SubModule.xml`
    - `SandBoxCore\ModuleData\**\*.xml`
    - `SandBoxCore\SceneObj\battle_terrain*\**\*`
  - release `Host\Modules\CoopSpectatorDedicated\bin` now mirrors local dedicated deploy:
    - `Win64_Shipping_Server\CoopSpectator.dll`
    - `Win64_Shipping_Client\CoopSpectator.dll`
    - `Win64_Shipping_Client\TaleWorlds.MountAndBlade.Multiplayer.dll`

Verified package contents:

- `Host\Modules\SandBox\ModuleData\sp_battle_scenes.xml`
- `Host\Modules\SandBoxCore\SceneObj\battle_terrain_001\scene.xscene`
- `Host\Modules\SandBoxCore\SceneObj\battle_terrain_n\scene.xscene`
- `Host\Modules\SandBoxCore\SceneObj\battle_terrain_biome_087b\scene.xscene`
- `SandboxXmlCount=1570`
- `SandboxCoreXmlCount=81`
- `BattleTerrainDirCount=98`
- `BattleTerrainFileCount=686`
- `HostClientBinFileCount=3`

Updated package:

- `C:\dev\projects\BannerlordCoopSpectator3\dist\BannerlordCoopCampaign_v0.1.1_Release.zip`
- SHA256: `363D184EB16A990C0A6EAC354CA90F3623CCFE36EA11F278427603C6288304DA`
- Size: `1002047285` bytes
- Host `Win64_Shipping_Server` MVID: `78544a60-df5b-4e0b-8c52-598f4e655fbb`
- Host `Win64_Shipping_Client` MVID: `78544a60-df5b-4e0b-8c52-598f4e655fbb`

The next foreign-host test must install the whole `Host` payload into the dedicated server root so that the target machine has `Modules\CoopSpectatorDedicated`, `Modules\SandBox`, and `Modules\SandBoxCore` side by side.

### 23w. Foreign host reached battle startup but missed custom CoopSpectator ModuleData

Fresh comparison:

- foreign host/dedicated: `C:\Users\Admin\Downloads\VVS logs\rgl_log_10272.txt`
- foreign host/campaign: `C:\Users\Admin\Downloads\VVS logs\rgl_log_2288.txt`
- foreign remote client: `C:\Users\Admin\Downloads\VVS logs\AC logs\rgl_log_60000.txt`
- local working dedicated: `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\logs\rgl_log_57104.txt`
- local working host/client logs: `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_44184.txt`, `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_61308.txt`, `C:\Users\Admin\Downloads\Telegram Desktop\rgl_log_16708.txt`

Authoritative progress:

1. The foreign-host startup crash is gone with the staged scene package.
2. The foreign host now loads the expected server binary:
   - `SERVER_BINARY_ID ... Win64_Shipping_Client\CoopSpectator.dll ... MVID=78544a60-df5b-4e0b-8c52-598f4e655fbb`
3. The foreign host now resolves dedicated battle scenes:
   - `HasSandBoxModule=True`
   - `HasSandBoxCoreModule=True`
   - `HasSpBattleScenesXml=True`
   - `HasBattleTerrainNScene=True`
   - `HasBattleTerrainBiome087bScene=True`

New blocker:

1. The foreign host still loaded only the vanilla multiplayer character catalog:
   - `CoopMissionSpawnLogic: loaded BasicCharacterObject count = 89`
2. Custom coop characters were missing:
   - `closest loaded BasicCharacterObject ids for 'mp_coop_looter_troop' = [...]`
   - `direct lookup failed for 'mp_coop_looter_troop'. Trying guaranteed mission-safe fallback 'imperial_infantryman'.`
3. The release `Host\Modules\CoopSpectatorDedicated` folder had no `ModuleData` at all, while the local working dedicated install did have:
   - `coopspectator_crafting_pieces.xml`
   - `coopspectator_items.xml`
   - `coopspectator_mpcharacters.xml`
   - `coopspectator_mpclassdivisions.xml`
   - `multiplayer_strings.xml`

This explains the new foreign-host symptom: scenes were fixed, but the dedicated server still did not load the custom coop unit/class definitions. It therefore fell back to vanilla mission-safe characters and could materialize units with wrong/partial character contracts.

Applied packaging fix:

- updated `C:\dev\projects\BannerlordCoopSpectator3\scripts\CreateReleasePackage.ps1`
  - `Host\Modules\CoopSpectatorDedicated\ModuleData` now receives all XML files from `Module\CoopSpectator\ModuleData`, matching the local MSBuild dedicated deploy target
- updated release README templates to explicitly require these host-side files after copy

Verified package contents:

- `Host\Modules\CoopSpectatorDedicated\ModuleData\coopspectator_mpcharacters.xml`
- `Host\Modules\CoopSpectatorDedicated\ModuleData\coopspectator_mpclassdivisions.xml`
- `Host\Modules\CoopSpectatorDedicated\ModuleData\coopspectator_items.xml`
- `Host\Modules\CoopSpectatorDedicated\ModuleData\coopspectator_crafting_pieces.xml`
- `Host\Modules\CoopSpectatorDedicated\ModuleData\multiplayer_strings.xml`
- `Host\Modules\CoopSpectatorDedicated\bin\Win64_Shipping_Client\CoopSpectator.dll`
- `Host\Modules\SandBox\ModuleData\sp_battle_scenes.xml`
- `Host\Modules\SandBoxCore\SceneObj\battle_terrain_001\scene.xscene`

Updated package:

- `C:\dev\projects\BannerlordCoopSpectator3\dist\BannerlordCoopCampaign_v0.1.1_Release.zip`
- SHA256: `FBAC9BB81187789A7E100F55495BCE26A474151E7D95AB93E575B472A7328B6E`
- Size: `1002092406` bytes
- Host `Win64_Shipping_Server` MVID: `78544a60-df5b-4e0b-8c52-598f4e655fbb`
- Host `Win64_Shipping_Client` MVID: `78544a60-df5b-4e0b-8c52-598f4e655fbb`

Next foreign-host rerun should check:

1. `SERVER_BINARY_ID ... MVID=78544a60-df5b-4e0b-8c52-598f4e655fbb`
2. `HasSandBoxModule=True HasSandBoxCoreModule=True HasSpBattleScenesXml=True`
3. `opening ..\..\Modules\CoopSpectatorDedicated/ModuleData/coopspectator_mpcharacters.xml`
4. `opening ..\..\Modules\CoopSpectatorDedicated/ModuleData/coopspectator_mpclassdivisions.xml`
5. `CoopMissionSpawnLogic: loaded BasicCharacterObject count` should be greater than the old vanilla-only `89`
6. there should be no repeated direct lookup failure for `mp_coop_looter_troop`

Separate note from the local successful run:

- `watchdog_log_57104.txt` did record a native `0xC0000005` after the battle had ended and after:
  - `EndGameAsServer called`
  - `Starting to clean up the current mission now.`
  - `--Mission is closed`
- dump path: `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\crashes\2026-04-25_12.09.57\dump.dmp`
- this is a real shutdown/mission-close crash, but it is separate from the current foreign-host pre-spawn/loadout issue because the battle had already completed successfully.

### 23x. Dump confirmed post-battle crash was our unsafe ClearUnreferencedResources diagnostic during MissionState finalize

Fresh logs/dump:

- dedicated host log: `C:\Users\Admin\Downloads\VVS logs\rgl_log_2364.txt`
- dedicated watchdog: `C:\Users\Admin\Downloads\VVS logs\watchdog_log_2364.txt`
- dump: `C:\Users\Admin\Downloads\VVS logs\dump.dmp`
- remote client log: `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_10448.txt`

Authoritative progress:

1. Foreign-host battle now completes successfully.
2. Custom ModuleData is loaded on dedicated:
   - `opening ..\..\Modules\CoopSpectatorDedicated/ModuleData/coopspectator_mpcharacters.xml`
3. Result/end flow reaches the expected end boundary:
   - `CoopMissionSpawnLogic: authoritative battle completion detected... AwaitingHostEndMission=True`
   - `EndGameAsServer called`
   - `Starting to clean up the current mission now.`
   - `I called EndMissionInternal`
   - `Mission.CurrentState ... EndingNextFrame`
   - `Mission.CurrentState ... Over`
   - `--Mission is closed`
4. Watchdog then records the same native access violation shape as the previous local successful run:
   - `ExceptionCode: 0xC0000005`
   - `Parameter-0: 0x0`
   - `Parameter-1: 0xe46348`

Dump stack:

- crashing thread was in:
  - `CoopSpectator.Patches.BattleShellSuppressionPatch.LogMissionStateLifecycleObservation(...)`
  - called from `BattleShellSuppressionPatch.Mission_ClearUnreferencedResources_Prefix(...)`
  - during `TaleWorlds.MountAndBlade.Mission.OnMissionStateFinalize(Boolean)`
  - during `TaleWorlds.MountAndBlade.MissionState.OnFinalize()`
  - during `GameStateManager.OnPopState(...)`

Conclusion:

- this crash is not a battle result bridge failure
- this crash is not missing scene/item/module data
- this crash is our diagnostic prefix touching mission/native properties during mission-state finalization after the battle is already complete

Applied stabilization fix:

- updated `C:\dev\projects\BannerlordCoopSpectator3\Patches\BattleShellSuppressionPatch.cs`
  - `Mission.ClearUnreferencedResources` prefix/postfix no longer logs detailed mission lifecycle observations on dedicated after the early loading states
  - `LogIsLoadingFinishedObservation(...)` no longer touches dedicated mission state after `NewlyCreated` / `Initializing`
  - added a guarded `TryGetMissionState(...)` helper so the early skip path checks managed mission state before touching scene/native properties

Updated package:

- `C:\dev\projects\BannerlordCoopSpectator3\dist\BannerlordCoopCampaign_v0.1.1_Release.zip`
- SHA256: `663771B4936317C91A3A6569A3BB4CB55F91A8C3A495AA678288C3E516798BBB`
- Size: `1002092807` bytes
- Host `Win64_Shipping_Server` MVID: `10275574-cc55-4160-9e31-becdf16a2c50`
- Host `Win64_Shipping_Client` MVID: `10275574-cc55-4160-9e31-becdf16a2c50`

Next rerun should check:

1. battle still completes and result reaches campaign/client
2. dedicated still reaches:
   - `EndGameAsServer called`
   - `Starting to clean up the current mission now.`
   - `I called EndMissionInternal`
   - `--Mission is closed`
3. no `watchdog_log` native `0xC0000005` after mission close
4. no dump folder with `dump.dmp` after successful shutdown

### 23y. Crash-free run exposed pre-possession materialization delay and unstable regular-troop commander fallback

Fresh logs:

- dedicated/host-side: `C:\Users\Admin\Downloads\VVS logs\rgl_log_10840.txt`, `C:\Users\Admin\Downloads\VVS logs\rgl_log_15252.txt`, `C:\Users\Admin\Downloads\VVS logs\rgl_log_15988.txt`
- remote client: `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_62440.txt`

Authoritative progress:

1. The dedicated shutdown/startup crash is no longer present in this run.
2. The host loaded the expected runtime data:
   - battle snapshot contained `main_hero -> mp_light_cavalry_battania_hero`
   - custom coop entries were selectable on both sides
3. The observed "army only materializes when the client enters selection/spawns" symptom was caused by our own guard:
   - `CoopMissionSpawnLogic: deferring initial battlefield materialization on exact campaign scene until a synchronized peer has a controlled agent...`
4. The observed defender commander flicker was caused by `BattleCommanderResolver` falling back to an arbitrary regular troop when a side had no hero/leader entry. Bandit looter variants are valid selectable troops, but they are not stable commander identities.

Applied stabilization fix:

- updated `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`
  - exact campaign battle materialization is no longer deferred just because no synchronized peer has a controlled agent yet
  - the existing native exact-scene bootstrap guard remains intact
- updated `C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\BattleCommanderResolver.cs`
  - commander resolution now requires a real identity signal: player hero, lord, party leader hero, player-side companion/wanderer, or other hero entry
  - regular non-hero troops no longer receive the `COMMANDER` badge

Updated package:

- `C:\dev\projects\BannerlordCoopSpectator3\dist\BannerlordCoopCampaign_v0.1.1_Release.zip`
- SHA256: `6502C3E13D393FA5CA25D25207901B768C58254E6302AF668CB7EF37A75B61EC`
- Size: `1002092778` bytes
- Host `Win64_Shipping_Server` MVID: `8406aea4-2398-4ebf-9f31-b9c073568345`
- Host `Win64_Shipping_Client` MVID: `8406aea4-2398-4ebf-9f31-b9c073568345`
- Client `Win64_Shipping_Client` MVID: `0adb78e7-6291-4429-becf-2aaf4aac4997`

Next rerun should check:

1. dedicated should no longer log:
   - `deferring initial battlefield materialization on exact campaign scene until a synchronized peer has a controlled agent`
2. battlefield armies should materialize before a client gains player possession
3. bandit/looter side should not show a flickering `COMMANDER` badge on regular troops
4. player-side commander remains the main hero entry when that exact entry is available to the viewing peer; if another peer already claimed that body, it should be absent from the second peer's selectable list by design

### 23z. Dump confirmed materialized AI spawn crash in native formation frame lookup

Fresh logs/dump:

- foreign dedicated host logs: `C:\Users\Admin\Downloads\Telegram Desktop\rgl_log_844.txt`, `C:\Users\Admin\Downloads\Telegram Desktop\rgl_log_24768.txt`, `C:\Users\Admin\Downloads\Telegram Desktop\rgl_log_5976.txt`
- foreign dedicated watchdog: `C:\Users\Admin\Downloads\Telegram Desktop\watchdog_log_5976.txt`
- dump: `C:\Users\Admin\Downloads\Telegram Desktop\Crash_2026-04-25_15.11.10\2026-04-25_15.11.10\dump.dmp`
- local client logs from the paired run: `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_34744.txt`, `C:\ProgramData\Mount and Blade II Bannerlord\logs\watchdog_log_34744.txt`

Authoritative progress:

1. The old early startup crash is still gone. The foreign dedicated host reaches mission loading, the dedicated observer activates, and the mission gets into side selection.
2. The crash moved forward into battlefield army materialization:
   - `CoopSpectatorDedicated: activating dedicated mission observer...`
   - `CoopMissionSpawnLogic: dedicated observer attached CoopMissionNetworkBridge`
   - `CoopMissionSpawnLogic: dedicated observer attached CoopMissionSpawnLogic`
   - `ExactCampaignArmyBootstrap: deferred native-like army bootstrap initialization. Scene=battle_terrain_biome_028 Reason=authoritative-side-none ...`
   - `CoopMissionSpawnLogic: materialized battlefield entry spawn begin... SpawnFrameSource=formation-repaired`
3. The watchdog records a native access violation:
   - `ExceptionCode: 0xC0000005`
   - `ExceptionAddress: 0x7ffc0c5b5e1d`
4. `dotnet-dump` shows the crashing managed/native interop stack on thread `0x343c`:
   - `ManagedCallbacks.ScriptingInterfaceOfIScene.WorldPositionValidateZ(...)`
   - `TaleWorlds.Engine.WorldPosition.ValidateZ(...)`
   - `TaleWorlds.Engine.WorldPosition.GetGroundZ()`
   - `TaleWorlds.Engine.WorldPosition.GetGroundVec3()`
   - `TaleWorlds.MountAndBlade.DefaultFormationDeploymentPlan.CreateNewDeploymentWorldPosition(...)`
   - `TaleWorlds.MountAndBlade.Mission.GetFormationSpawnFrame(...)`
   - `CoopSpectator.MissionBehaviors.CoopMissionSpawnLogic.ResolveMaterializedArmySpawnFrame(...)`
   - `CoopSpectator.MissionBehaviors.CoopMissionSpawnLogic.SpawnMaterializedAgentsForEntry(...)`
   - `CoopSpectator.MissionBehaviors.CoopMissionSpawnLogic.MaterializeArmyForSide(...)`

Conclusion:

- this is not the previous `Mission.ClearUnreferencedResources(true)` or mission-close crash
- this is not a Windows 10 vs Windows 11 issue
- the current native crash boundary is our materialized AI path calling Bannerlord's `Mission.GetFormationSpawnFrame`, which can crash inside scene/world-position Z validation on this dedicated startup path
- the `authoritative-side-none` log also showed that the previous materialization loosened too far and could begin before any synchronized peer had a stable authoritative side

Applied stabilization fix:

- updated `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`
  - exact campaign initial materialization now waits for at least one synchronized non-server peer with an authoritative side, rather than waiting for a controlled agent
  - the defer log now reports the peer/side counters, for example `AssignedPeers=0`
  - materialized AI spawn no longer calls native `Mission.GetFormationSpawnFrame`
  - materialized AI spawn no longer builds a `WorldPosition` only to call `GetGroundVec3()`
  - materialized AI spawn now uses a plain `Vec3` from repaired deployment plan, then FFA spawn frame fallback, then direct fallback

Updated package:

- `C:\dev\projects\BannerlordCoopSpectator3\dist\BannerlordCoopCampaign_v0.1.1_Release.zip`
- SHA256: `EC95D740F32D509F0C0A7500206C2B1563C866C666FA189D3BA6B7D63D2D50FB`
- Host `Win64_Shipping_Server` MVID: `f59b2c57-f6ff-4f4b-9c15-b33ce3e00a80`
- Host `Win64_Shipping_Client` MVID: `f59b2c57-f6ff-4f4b-9c15-b33ce3e00a80`
- Client `Win64_Shipping_Client` MVID: `14762f62-5853-40d2-babe-5b528d7da4a6`

Next rerun should check:

1. no crash/dump at the old stack:
   - `Mission.GetFormationSpawnFrame`
   - `WorldPositionValidateZ`
   - `ResolveMaterializedArmySpawnFrame`
2. before a peer side is authoritative, dedicated should log:
   - `deferring initial battlefield materialization on exact campaign scene until a synchronized peer has an authoritative side`
3. after side selection, materialized entries should use:
   - `SpawnFrameSource=formation-repaired`
   - or `SpawnFrameSource=ffa-scene`
   - or `SpawnFrameSource=direct-fallback`
4. dedicated should not show the old bad sequence where `Reason=authoritative-side-none` is immediately followed by materialized battlefield spawn
5. if it still crashes, collect the new foreign host dedicated `rgl_log_<pid>.txt`, `watchdog_log_<pid>.txt`, and `dump.dmp`; the stack should tell us whether the next boundary moved past spawn-frame resolution

### 23aa. Host commander spawn/control blocked by peer-order bootstrap and weak pre-battle hold

Fresh logs:

- foreign host game client: `C:\Users\Admin\Downloads\Telegram Desktop\rgl_log_13340.txt`
- foreign dedicated host: `C:\Users\Admin\Downloads\Telegram Desktop\rgl_log_7676.txt`
- foreign launcher/helper: `C:\Users\Admin\Downloads\Telegram Desktop\rgl_log_7672.txt`
- remote client: `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_49624.txt`

Authoritative findings:

1. There was no native crash in this run. The remaining failure was battle runtime state, not startup.
2. The host selected the player-side commander, but never sent `SpawnNow` because the status snapshot still reported `CanRespawn=False`.
3. The dedicated exact native bootstrap kept logging `Reason=authoritative-side-none` after the host had selected a side because it used the first synchronized peer as the bootstrap peer. In this run that first peer was the remote client `AC`, not the visual host `XCTwnik`.
4. Once `AC` selected Attacker, the exact bootstrap initialized and `AC` could spawn as the commander. That made the issue look host-specific, but the root cause was peer selection order.
5. After `AC` spawned, the server moved to `PreBattleHold`, but `PauseAITick=False` and the formation hold affected `0` formations. Active troop counts then dropped during `PreBattleHold`, confirming that armies were fighting before the host start command.
6. The client commander was also briefly classified as non-commander because local `ControlledEntryId` had not resolved yet:
   - `suppressed local OrderController.SelectAllFormations ... ControlledEntryId=null CommanderEntryId=attacker|player_party|main_hero|... SuppressionPhase=non-commander`

Applied stabilization fix:

- updated `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`
  - exact native campaign bootstrap now selects a synchronized peer with an authoritative side and committed selection instead of blindly taking the first synchronized peer
  - battle phase readiness now counts peers with authoritative selection state even if they are still in spectator/holding team, so one spawned client cannot advance the battle past an unspawned host
  - pre-battle AI pause now remains enabled until `BattleActive`
  - formation hold is re-applied during pre-battle and tracks unit-count changes, so newly spawned native exact formations are not left on old attack orders
  - pre-battle formation hold now disables AI control and applies stop/hold-fire until battle start
- updated `C:\dev\projects\BannerlordCoopSpectator3\Patches\BattleMapSpawnHandoffPatch.cs`
  - local/server non-commander order suppression now defers when `CommanderEntryId` is known but `ControlledEntryId` is still missing during spawn handoff
  - this prevents exact commander order control from being stripped during the short identity sync gap

Updated packages:

- full: `C:\dev\projects\BannerlordCoopSpectator3\dist\BannerlordCoopCampaign_v0.1.1_Release.zip`
  - SHA256: `77E8D54A8942A4B4FEEE9AC0C4336D2704416B4B2ADAC23B8DC270CB183A12F1`
  - Size: `1002094651` bytes
- light: `C:\dev\projects\BannerlordCoopSpectator3\dist\BannerlordCoopCampaign_v0.1.1_LightRelease.zip`
  - SHA256: `99F56F24F6B9FD34AF79035B539CBE2DCB0AE34C762371BB8BB7E74FD3B75F23`
  - Size: `4566928` bytes
- Host `Win64_Shipping_Server` MVID: `222e29b1-1e53-4f9e-8281-56502bba141f`
- Host `Win64_Shipping_Client` MVID: `222e29b1-1e53-4f9e-8281-56502bba141f`
- Client `Win64_Shipping_Client` MVID: `091d09d5-da20-4b95-b9e2-7e73b8c686ce`

Next rerun should check:

1. after the visual host selects side/commander, dedicated should initialize exact bootstrap without waiting for the remote client to select:
   - `ExactCampaignArmyBootstrap: initialized native-like army bootstrap`
   - no repeated `Reason=authoritative-side-none` after host side selection
2. host client should receive `CanRespawn=True` for the selected commander and then emit `Writing message. Kind=SpawnNow`
3. while not all assigned peers are controlled, dedicated should not report `CanStartBattle=True`
4. during `PreBattleHold`, dedicated should report `PauseAITick=True` and active attacker/defender counts should not drop before start
5. if local commander entry identity is briefly missing, logs should show:
   - `deferred non-commander suppression until controlled entry identity resolves`
   - not `suppressed local OrderController.SelectAllFormations ... SuppressionPhase=non-commander` for the exact commander
