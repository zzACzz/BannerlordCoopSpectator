# Exact Campaign 1:1 Transfer And Friendly Banners Handoff

Date: 2026-04-07
Project: `BannerlordCoopSpectator3`
Focus: exact campaign `1:1` troop transfer status, validated solved areas, and the remaining blocker for friendly overhead banners above player-controlled troops

## Executive Summary

State at this handoff:

- exact campaign scene transfer is working;
- dedicated starts exact campaign battle scenes;
- client loads the real campaign battlefield;
- exact roster transfer now works well enough that a fresh first battle can visually start with `1:1` troops instead of surrogate armies;
- large-battle reinforcements and authoritative battle finish were already fixed earlier and should not be reopened without new log-backed evidence;
- the previous cross-battle stale snapshot contamination bug was fixed;
- the current visible blocker is no longer roster transfer itself, but missing friendly overhead banners / troop markers above player-controlled formations.

Current practical status:

- first battle can be visually correct `1:1`;
- second-battle snapshot contamination was fixed in the recent runs;
- commander ownership and order control state are log-backed as correct;
- friendly overhead troop banners are still missing in battle-map runtime;
- latest patch now targets the native `MissionAgentLabelUIHandler` path and is built/deployed, but still needs validation in a fresh run.

## Validated Solved Areas

Do not reopen these without new log-backed reason:

- exact campaign scene transfer;
- dedicated exact-scene startup;
- client exact battlefield loading;
- native-like exact army bootstrap;
- large-battle reinforcements;
- authoritative battle completion after last enemy dies;
- second-battle stale snapshot leak across missions;
- exact names / body / face identity transfer as the main blocker.

## Current User-Visible State

Latest user report before this handoff:

- a fresh single battle visually showed correct `1:1` units;
- there were still no friendly banners / overhead icons above allied units;
- because of that, there was no point testing a second battle yet.

Relevant logs for this exact banner issue:

- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_127224.txt`
- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_128072.txt`
- `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\logs\rgl_log_139320.txt`

## What The Logs Already Prove

### Commander ownership is correct

Client log already proves that local order ownership exists and is not the blocker:

- `BattleMapSpawnHandoffPatch: exact commander order VM state...`
- in `rgl_log_127224.txt` around line `5158`:
  - `PlayerHasAnyTroopUnderThem=True`
  - `SelectedFormations=[Infantry,Ranged,Cavalry]`
  - formation states show `PlayerOwner=32`
  - active formations show `HasPlayerControlled=True`

Conclusion:

- missing overhead banners are not caused by lost commander control;
- the problem is in the label / marker rendering path, not in ownership handoff.

### Dedicated release logs are not the root cause

Dedicated log shows later battle-phase formation release details, but those are post-start transitions and do not explain why the overhead labels are absent from the beginning of the battle.

## Native Contract Findings

### Previous target was wrong

Earlier work targeted `MissionFormationMarkerUIHandler`.

Decompile showed that this handler is for formation markers tied to order-menu / selection behavior, not the always-visible overhead troop banners the user cares about.

### Correct native view for troop banners

Decompile of native multiplayer view stacks showed:

- `MultiplayerBattle` includes `ViewCreator.CreateMissionAgentLabelUIHandler(mission)`
- `MultiplayerPractice` includes `ViewCreator.CreateMissionAgentLabelUIHandler(mission)` and also `CreateMissionFormationMarkerUIHandler(mission)`

Meaning:

- the real native battle-mode path for overhead troop labels is `MissionAgentLabelUIHandler`, not only the formation-marker handler.

### Exact native label logic

Decompile of `MissionAgentLabelView` showed:

- `OnAgentBuild(Agent agent, Banner banner)` calls `InitAgentLabel(agent, banner)`
- `InitAgentLabel(...)` uses:
  - `Banner val = peerBanner ?? agent.Origin.Banner;`
- if that banner is null, no label mesh is created

Visibility rules also require:

- ally team match;
- `BannerlordConfig.FriendlyTroopsBannerOpacity > 0`;
- either always-show option or the relevant input state.

Important interpretation:

- if the mission view is missing, labels can never appear;
- even if the view exists, labels still need non-null banner data through `banner` or `agent.Origin.Banner`.

## Exact Origin Banner Gap

Current exact origin implementation still has a likely secondary gap:

- `Infrastructure/ExactCampaignArmyBootstrap.cs`
- `ExactCampaignSnapshotAgentOrigin`
- `IAgentOriginBase.Banner => null`
- `SetBanner(Banner banner)` is effectively a no-op

Native parity reference:

- `PartyGroupAgentOrigin.Banner` returns the party leader clan banner or party faction banner
- `CustomBattleAgentOrigin.Banner` returns the combatant banner

Meaning:

- if the newly added `MissionAgentLabelUIHandler` view alone does not solve the problem, the next most likely native-contract gap is that exact agents still expose `Origin.Banner == null`.

## Latest Code Change In Working Tree

Latest patch added the missing native battle label view into coop battle-map runtime:

- `GameMode/MissionMultiplayerCoopBattleMode.cs`
  - around line `286`: add `MissionAgentLabelUIHandler`
  - around line `445`: add reflection-based `TryCreateMissionAgentLabelUiHandler(Mission mission)`

The previous formation-marker injection remains, but now battle-map runtime also tries to create the actual native agent-label handler.

Build/deploy status:

- client build succeeded and deployed
- dedicated build succeeded and deployed
- deployed client DLL timestamp:
  - `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\CoopSpectator\bin\Win64_Shipping_Client\CoopSpectator.dll`
  - `2026-04-07 16:34:38`
- deployed dedicated DLL timestamp:
  - `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Dedicated Server\Modules\CoopSpectatorDedicated\bin\Win64_Shipping_Server\CoopSpectator.dll`
  - `2026-04-07 16:34:36`

## What Needs Validation Next

Run one fresh battle only, after full restart of client and dedicated.

Expected client log markers:

- `CoopBattle client: injected agent label and formation marker mission views for battle-map runtime parity with native multiplayer battle/practice stacks.`
- no:
  - `MissionAgentLabelUIHandler creation skipped`
  - `MissionAgentLabelUIHandler creation failed`

Expected visual outcome:

- friendly overhead troop banners / circles should appear again over player-controlled allied units

## If Banners Are Still Missing After The Latest Patch

Do not return to solved roster-transfer areas first.

Next narrow step should be:

1. Patch `ExactCampaignSnapshotAgentOrigin` to carry a real non-null `Banner`
2. Source that banner from the exact campaign side / party contract
3. Re-test one battle only

Best native-parity source order:

1. leader party clan banner
2. party map faction banner
3. side/team fallback banner if exact party banner is unavailable

The goal is to match what native `PartyGroupAgentOrigin` already does, instead of inventing a new custom label system.

## Recommended Reading In A New Window

Read these first:

- `docs/README.md`
- `PROJECT_CONTEXT.md`
- `docs/EXACT_CAMPAIGN_COMMANDER_CONTROL_HANDOFF_2026-04-02.md`
- this file

Useful code files:

- `GameMode/MissionMultiplayerCoopBattleMode.cs`
- `Infrastructure/ExactCampaignArmyBootstrap.cs`
- `Mission/CoopMissionBehaviors.cs`
- `Patches/BattleMapSpawnHandoffPatch.cs`

Useful decompile references:

- `.codex_tmp/MissionAgentLabelView.cs`
- `.codex_tmp/decompiled_multiplayer_view/TaleWorlds.MountAndBlade.Multiplayer.View.MissionViews/MultiplayerMissionViews.cs`
- `.codex_tmp/decompiled_multiplayer_view/TaleWorlds.MountAndBlade.Multiplayer.View.MissionViews/MultiplayerPracticeMissionViews.cs`

## Final Practical Conclusion

This is no longer a broad architecture unknown.

At this handoff the banner problem is narrowed to a small native-parity gap:

- either the correct native mission view was missing;
- or exact agents still provide no banner data to that view;
- or both.

The latest patch closes the first gap.
If validation fails, the next step is the second gap in exact origin banner data.
