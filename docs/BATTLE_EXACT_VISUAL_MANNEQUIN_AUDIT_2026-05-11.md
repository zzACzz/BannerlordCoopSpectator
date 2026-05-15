# Battle Exact Visual Mannequin Audit 2026-05-11

## Goal

Fix the source of mannequin corruption, not the aftermath.

This note freezes the currently proven links around:

- `battle exact visual path`
- `mounted hero / cavalry visual refresh`
- shared mannequin / tableau renderer state

and explicitly records what is **not** proven, so the next fixes do not reopen the `Party/Loot` crash corridor.

## What Is Already Proven

### 1. The recent campaign crash came from the recovery patch, not from the original mannequin bug

The deferred `CampaignVisualResetPatch` was the direct trigger of the new `Party/Loot` campaign crash.

Reason:

- `Helpers.PartyScreenHelper.OpenScreenAsNormal()` and `OpenScreenAsLoot()` synchronously create `PartyState`, initialize `PartyScreenLogic`, and `PushState` before returning.
- Our postfix/deferred reset therefore ran after the screen was already alive.
- The patch then soft-reset `BannerlordTableauManager` and invoked `ValidateAgentVisualsReseted` on `Campaign.MapSceneWrapper`, which is not the same renderer path as the active `Party/Loot` mannequin screen.

Relevant references:

- [Patches/CampaignVisualResetPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/CampaignVisualResetPatch.cs:13)
- [Helpers.PartyScreenHelper.decompiled.cs](/C:/dev/projects/BannerlordCoopSpectator3/.ilspy_tmp/campaignhelper/Helpers.PartyScreenHelper.decompiled.cs:39)
- [Helpers.PartyScreenHelper.decompiled.cs](/C:/dev/projects/BannerlordCoopSpectator3/.ilspy_tmp/campaignhelper/Helpers.PartyScreenHelper.decompiled.cs:185)
- [Helpers.PartyScreenHelper.decompiled.cs](/C:/dev/projects/BannerlordCoopSpectator3/.ilspy_tmp/campaignhelper/Helpers.PartyScreenHelper.decompiled.cs:212)
- [BannerlordTableauManager.cs](/C:/dev/projects/BannerlordCoopSpectator3/.ilspy_tmp/mountandblade/TaleWorlds.MountAndBlade/BannerlordTableauManager.cs:5)
- [TaleWorlds.MountAndBlade.MBAgentRendererSceneController.decompiled.cs](/C:/dev/projects/BannerlordCoopSpectator3/.ilspy_tmp/mannequin_audit/TaleWorlds.MountAndBlade.MBAgentRendererSceneController.decompiled.cs:36)

### 2. Runtime exact character objects are not the active source right now

`ExactCampaignRuntimeObjectRegistry` is currently disabled:

- [Infrastructure/ExperimentalFeatures.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/ExperimentalFeatures.cs:112)

So the current mannequin issue is **not** explained by synthetic runtime `BasicCharacterObject` instances leaking into campaign UI.

### 3. Current unit-selection UI no longer uses `CharacterTableauWidget`

The live module prefab no longer renders a mannequin in `CoopClassLoadout`.

Relevant reference:

- [Module/CoopSpectator/GUI/Prefabs/CoopClassLoadout.xml](/C:/dev/projects/BannerlordCoopSpectator3/Module/CoopSpectator/GUI/Prefabs/CoopClassLoadout.xml:29)

This means:

- the **current** global mannequin corruption is not being caused by the current `CoopClassLoadout` screen itself
- the older selection-screen corruption is still relevant as a historical clue, but not as the live source in the current prefab

### 4. `UpdateSpawnEquipmentAndRefreshVisuals(...)` is a heavy native operation, not a cosmetic one

Vanilla `Agent.UpdateSpawnEquipmentAndRefreshVisuals(...)` does all of this:

1. assigns new `SpawnEquipment`
2. on server, broadcasts `SynchronizeAgentSpawnEquipment`
3. clears visual components
4. calls `Mission.OnEquipItemsFromSpawnEquipment(...)`
5. clears weapon meshes
6. fills `MissionEquipment` from `SpawnEquipment`
7. re-equips from spawn equipment
8. updates agent properties
9. may wield initial weapons
10. preloads for rendering

Relevant decompile:

- [Agent.cs](/C:/dev/projects/BannerlordCoopSpectator3/.ilspy_tmp/2026-05-10_reconnect_server_audit/Agent.cs:3612)

This is the strongest proven reason to treat all exact visual refresh paths as renderer-sensitive and native-sensitive.

## Current Exact Battle Visual Path

### A. Snapshot collection

Campaign battle snapshot captures exact per-entry data:

- exact combat weapons
- armor
- horse
- horse harness
- hero body properties

Relevant state fields:

- [Infrastructure/BattleSnapshotRuntimeState.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/BattleSnapshotRuntimeState.cs:87)
- [Infrastructure/BattleSnapshotRuntimeState.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/BattleSnapshotRuntimeState.cs:124)

Snapshot collection references:

- [Campaign/BattleDetector.cs](/C:/dev/projects/BannerlordCoopSpectator3/Campaign/BattleDetector.cs:6677)
- [Campaign/BattleDetector.cs](/C:/dev/projects/BannerlordCoopSpectator3/Campaign/BattleDetector.cs:7604)

### B. Snapshot activation

When snapshot becomes active:

1. runtime state is built
2. exact item registry is ensured
3. runtime exact character registry is asked to sync, but feature is disabled

Relevant reference:

- [Infrastructure/BattleSnapshotRuntimeState.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/BattleSnapshotRuntimeState.cs:266)

### C. Server pre-spawn exact loadout path

Current active feature flags:

- pre-spawn exact loadout injection = enabled
- runtime exact object registry = disabled

Relevant references:

- [Infrastructure/ExperimentalFeatures.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/ExperimentalFeatures.cs:112)
- [Infrastructure/ExperimentalFeatures.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/ExperimentalFeatures.cs:119)

Server `Mission.SpawnAgent` prefix path:

1. build exact transfer contract
2. decide whether to inject weapons / cape / armor / mount at create time
3. build pre-spawn `Equipment`
4. optionally inject `BodyProperties`
5. write exact payload into `AgentBuildData`

Relevant reference:

- [Patches/ExactCampaignPreSpawnLoadoutPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/ExactCampaignPreSpawnLoadoutPatch.cs:18)

### D. Post-spawn exact overlay path

For native agents we still have post-spawn overlay logic:

- [Mission/CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs:7853)

Inside `TryApplyExactCampaignSnapshotOverlayToNativeAgent(...)`:

1. resolve `RosterEntryState`
2. clone seed equipment from current `agent.SpawnEquipment`
3. build overlay equipment via `BuildSnapshotEquipmentForReplaceBot(...)`
4. apply snapshot item overrides with `TryApplyMaterializedEquipmentOverrides(...)`
5. call `agent.UpdateSpawnEquipmentAndRefreshVisuals(spawnEquipment)`
6. optionally refresh wield state
7. optionally refresh mount visuals
8. update agent properties

Relevant references:

- [Mission/CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs:7962)
- [Mission/CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs:8018)
- [Mission/CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs:8115)
- [Mission/CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs:15199)
- [Mission/CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs:15277)

### E. Mounted hero / cavalry special path

Mounted visual repair is its own dangerous branch.

If rider and expected horse visuals mismatch, the code:

1. clones `riderAgent.MountAgent.SpawnEquipment`
2. applies snapshot horse / harness overrides
3. calls `riderAgent.MountAgent.UpdateSpawnEquipmentAndRefreshVisuals(...)`
4. if that fails, tries a manual fallback path

Relevant reference:

- [Mission/CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs:15199)

This is the strongest currently active candidate for corrupting shared mounted visual state.

## Narrowed Runtime Finding After Later Audits

The broad `non-hero` corridor is no longer the main source.

What later log comparisons proved:

1. `non-hero` server create-time contract now reaches client runtime correctly
2. `non-hero` infantry / cavalry mostly skip destructive client exact refresh
3. the remaining destructive path is the mounted `main_hero` / hero corridor with:
   - `ExactEntryContract=degraded-weapon-fallback`
   - `WeaponContractSupported=False`
   - `VisualContractSupported=True`
   - `Weapon2Risk=True`
4. this hero path was still queuing an early client visual-only rebuild from `CreateAgent`
5. the same hero later stabilizes through the equipment-sync path, so the early rebuild is the unstable step

Practical consequence:

- do **not** assume mannequin corruption is still caused by generic troop exact refresh
- current highest-risk path is early mounted hero visual rebuild before `SynchronizeAgentSpawnEquipment`
- future fixes should prefer waiting for hero equipment sync over forcing an early rider/mount rebuild

## What Is Not Proven

### 1. Not proven: broken mannequin data is saved into campaign save data

At the moment there is no hard proof that bad battle visual data is serialized into campaign save data and then reloaded later.

What is more strongly supported:

- battle exact visual path corrupts an in-process renderer / mannequin state
- later UI screens reuse it

### 2. Not proven: exact runtime item registry is the root cause

`ExactCampaignRuntimeItemRegistry` is enabled and preserves loaded exact items across snapshot clear:

- [Infrastructure/ExactCampaignRuntimeItemRegistry.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/ExactCampaignRuntimeItemRegistry.cs:158)

That means it remains a candidate worth remembering.

But there is still no proof that it is the thing producing the sideways rider / standing horse mannequin corruption.

### 3. Not proven: server snapshot collection is already wrong

Snapshot collection may still be correct while later visual application is wrong.

Right now the strongest evidence points more to:

- apply-time visual refresh
- mounted repair
- renderer cache / tableau reuse

than to incorrect capture in `BattleDetector`.

## Current Best Root-Cause Hypothesis

The most defensible hypothesis right now is:

1. exact battle path creates a mounted visual state that is internally inconsistent for renderer / mannequin systems
2. this most likely happens during post-spawn exact refresh, especially on the rider or mount branch
3. the corruption is process-local shared renderer / tableau state, not necessarily bad save data
4. later mannequin screens reuse that state and render a sideways rider / standing horse style failure

Why this hypothesis is stronger than the alternatives:

- runtime exact characters are disabled
- current `CoopClassLoadout` no longer uses `CharacterTableauWidget`
- `UpdateSpawnEquipmentAndRefreshVisuals(...)` is known to rebuild visuals aggressively
- mounted repair path calls the same rebuild path again on the mount agent

## New Confirmed Blind Spot

`ClientVisualOnly` exact overlay was effectively blind to server-side pre-spawn exact loadout injection.

Reason:

- `HasExactCampaignPreSpawnLoadoutInjected(...)` explicitly returns `false` on non-server processes.
- So a client could receive an agent that was already created with the correct exact loadout, but local exact visual logic would still treat it as needing a post-spawn refresh.
- That opened an unnecessary second call into `Agent.UpdateSpawnEquipmentAndRefreshVisuals(...)` on the client.

Relevant reference:

- [Mission/CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs:7824)

This does not prove every mannequin corruption came from the same call site, but it is now a hard reason to prefer `already-exact` detection over unconditional client refresh.

## Guardrails For Future Fixes

Do **not** do these blindly again:

1. Do not reset `BannerlordTableauManager` after `Party/Loot/Inventory` screen is already alive.
2. Do not call `ValidateAgentVisualsReseted` on `Campaign.MapSceneWrapper` as a generic mannequin repair step.
3. Do not assume `visual refresh` is safe just because weapons are excluded.
4. Do not assume the source is save persistence until that is proven separately.

## Next Investigation Targets

### Highest-value checks

1. Compare pre-refresh and post-refresh rider state for mounted exact entries:
   - human `SpawnEquipment`
   - human `MissionEquipment`
   - `MountAgent.SpawnEquipment`
   - `MountAgent.AgentVisuals` state if available in logs

2. Compare server pre-spawn injected equipment vs post-spawn overlay equipment for the same mounted entry.

3. Check whether any mounted exact path mixes:
   - rider foot posture assumptions
   - horse slot present
   - mount agent visual refresh
   - body properties copied from hero state

4. Verify whether mannequin corruption appears only after:
   - mounted hero overlay
   - mounted troop overlay
   - mount visual fallback
   - or any exact visual refresh at all

### Files to inspect first

- [Mission/CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs:7853)
- [Mission/CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs:15099)
- [Mission/CoopMissionBehaviors.cs](/C:/dev/projects/BannerlordCoopSpectator3/Mission/CoopMissionBehaviors.cs:15199)
- [Patches/ExactCampaignPreSpawnLoadoutPatch.cs](/C:/dev/projects/BannerlordCoopSpectator3/Patches/ExactCampaignPreSpawnLoadoutPatch.cs:18)
- [Infrastructure/BattleSnapshotRuntimeState.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/BattleSnapshotRuntimeState.cs:266)
- [Infrastructure/ExactCampaignRuntimeItemRegistry.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/ExactCampaignRuntimeItemRegistry.cs:158)
- [Infrastructure/ExperimentalFeatures.cs](/C:/dev/projects/BannerlordCoopSpectator3/Infrastructure/ExperimentalFeatures.cs:112)
- [UI/CoopSelectionUiHelpers.cs](/C:/dev/projects/BannerlordCoopSpectator3/UI/CoopSelectionUiHelpers.cs:389)
- [Agent.cs](/C:/dev/projects/BannerlordCoopSpectator3/.ilspy_tmp/2026-05-10_reconnect_server_audit/Agent.cs:3612)
- [BannerlordTableauManager.cs](/C:/dev/projects/BannerlordCoopSpectator3/.ilspy_tmp/mountandblade/TaleWorlds.MountAndBlade/BannerlordTableauManager.cs:5)
- [TaleWorlds.MountAndBlade.MBAgentRendererSceneController.decompiled.cs](/C:/dev/projects/BannerlordCoopSpectator3/.ilspy_tmp/mannequin_audit/TaleWorlds.MountAndBlade.MBAgentRendererSceneController.decompiled.cs:36)
- [TaleWorlds.Core.CharacterCode.decompiled.cs](/C:/dev/projects/BannerlordCoopSpectator3/.ilspy_tmp/mannequin_audit3/TaleWorlds.Core.CharacterCode.decompiled.cs:1)

## Practical Conclusion

Right now the safest path is:

- keep the crashy campaign recovery patch disabled
- investigate battle exact visual application itself
- focus first on mounted post-spawn overlay and mounted manual refresh
- treat save-data corruption as an unproven alternative, not as the main assumption
