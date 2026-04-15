# Working Spawn Baseline And Diff Plan

Date: 2026-04-13
Project: `BannerlordCoopSpectator3`
Validated mainline head at time of writing: `f773ab3637edb0552936b5dbc9694d997614aa80` plus local spawn-core restore
Reference working spawn commit used for restore: `a4352504ba0cec89be4badfd40ba8fab8512dbda` (`a435250`)

## Why this document exists

The 2026-04-13 regression cycle mixed three different change threads:

1. perk / captain runtime work
2. battle spawn / battlefield materialization runtime work
3. separate connectivity bridge changes for `public` and `Radmin VPN` joins

The key failure was not only "perk work broke spawn". A parallel connectivity bridge chain existed outside the active Codex context, so symptoms were misattributed to the perk/runtime investigation for too long.

This document freezes the now-working baseline and defines the safe diff boundaries for future work.

## Validated working baseline

The latest clean rerun was successful. Battle opened, side selection worked, possession worked, battle advanced, and results were written back.

Server evidence from `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\logs\rgl_log_15080.txt`:

- `2461`: phase entered `SideSelection`
- `30154`: `CoopMissionSpawnLogic: materialized army replace-bot succeeded`
- `30166`: phase entered `Deployment`
- `30168`: phase entered `PreBattleHold`
- `30227`: phase entered `BattleActive`
- `30478`: phase entered `BattleEnded`
- `30484`: `CoopMissionSpawnLogic: battle result snapshot written`
- `30486`: `CoopMissionSpawnLogic: authoritative battle completion detected`

Client evidence:

- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_34784.txt:2749`
  `CoopMissionSelectionView: coop selection layer added.`
- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_23304.txt:2897`
  `BattleMapSpawnHandoffPatch: exact commander order menu interaction...`

This is the current control baseline. Do not change it casually.

## What the current working baseline actually is

This is not a full rollback to the old project state.

It is:

- latest mainline join / public / VPN layer kept intact
- spawn-core restored to the working behavior signature from `a435250`
- incompatible network selection transport removed

Concretely:

### Preserved from newer join/public/VPN work

- `Commands/CoopConsoleCommands.cs`
- `DedicatedHelper/DedicatedHelperLauncher.cs`
- `DedicatedHelper/DedicatedServerHostingMode.cs`
- `DedicatedHelper/DedicatedServerLaunchSettings.cs`
- `Campaign/CoopDedicatedServerSettingsMapView.cs`
- `Campaign/CoopDedicatedServerSettingsVM.cs`
- `Campaign/GauntletCoopDedicatedServerSettingsView.cs`
- `Patches/LobbyCustomGameLocalJoinPatch.cs`
- `Patches/LobbyJoinResultSelfJoinArmPatch.cs`
- `Patches/LocalJoinAddressPatch.cs`
- `Patches/MapScreenEscapeMenuPatch.cs`
- supporting package / module wiring in `SubModule.cs` and project files

### Restored to working spawn-core behavior

- `Mission/CoopMissionBehaviors.cs`
- `UI/CoopMissionSelectionView.cs`
- `GameMode/MissionMultiplayerCoopBattleMode.cs`
- `GameMode/MissionMultiplayerTdmCloneMode.cs`
- `Patches/MissionStateOpenNewPatches.cs`

### Removed because it conflicted with the restored spawn path

- `Mission/CoopMissionNetworkBridge.cs`

The dedicated project source list was updated accordingly:

- `DedicatedServer/CoopSpectatorDedicated.csproj`

## Root lesson from this regression

There were hidden parallel changes in a separate connectivity chain. Because they were not part of the active reviewed diff, we treated runtime failures as if they came from the perk/captain branch alone.

Rule going forward:

- if another branch or local change chain exists, it must be declared before runtime debugging starts
- if a failure could belong to more than one change thread, first reduce to one active diff bucket

## Protected spawn baseline

Until the perk work is reintroduced and revalidated, treat these files as protected:

- `Mission/CoopMissionBehaviors.cs`
- `UI/CoopMissionSelectionView.cs`
- `GameMode/MissionMultiplayerCoopBattleMode.cs`
- `GameMode/MissionMultiplayerTdmCloneMode.cs`
- `Patches/MissionStateOpenNewPatches.cs`
- `Patches/BattleMapSpawnHandoffPatch.cs`

Do not edit them for perk work unless logs or code prove that the exact missing perk behavior cannot be fixed elsewhere.

## Perk/captain diff buckets

Future work must be split into these buckets.

### Bucket A: personal perk transfer only

Allowed files:

- `Campaign/BattleDetector.cs`
- `Network/Messages/BattleStartMessage.cs`
- `Infrastructure/BattleSnapshotRuntimeState.cs`
- narrow, additive parts of `Mission/CoopMissionBehaviors.cs`

Goal:

- main hero personal perks
- companion personal perks

Constraint:

- no spawn/bootstrap/materialization changes
- no new network bridge layer

### Bucket B: captain source only

Goal:

- use native deployment / formation captain assignment as the source of captain effects

Constraint:

- commander/captain selection UI must remain native
- no change to how armies spawn
- no new mission startup logic

### Bucket C: captain perk runtime apply

Goal:

- apply captain effects from live `formation.Captain`

Constraint:

- start with a very small exact subset
- validate in `20+ troops` battles only after baseline smoke tests are green

### Bucket D: role perks

Goal:

- `PartyLeader`
- `ArmyCommander`
- `Scout`
- `Quartermaster`
- `Engineer`
- `Surgeon`

Constraint:

- separate from personal and captain buckets
- many of these are conditional/runtime-context dependent and should not be mixed into earlier tests

## Safe reintroduction order

1. Freeze and document the working spawn baseline.
2. Keep join/public/VPN layer unchanged while perk work resumes.
3. Reintroduce `main hero` personal perk subset.
4. Reintroduce `companions` personal perk subset.
5. Validate that commander possession still works.
6. Reintroduce live `formation.Captain` source only.
7. Reintroduce a small exact captain perk subset.
8. Reintroduce role perks last.

## Mandatory smoke tests after each step

Every iteration must pass these checks before the next one starts:

1. Small battle:
   - mission loads
   - side selection appears
   - armies materialize before deploy
   - deploy possesses selected unit
   - battle can end normally
2. Large battle:
   - correct roster count
   - no stale roster/status mix
   - no server crash
3. Campaign aftermath:
   - battle returns to campaign

For captain-only work add:

4. `20+ troops` battle:
   - native deployment menu appears
   - commander can assign formation captains
   - formation captain source is visible in logs before captain perks are validated

## Diagnostics-first rule for perk and damage work

Perk and damage work must not proceed as "change code, then guess from gameplay".

Before each rerun:

1. identify the exact native runtime function or model that is supposed to own the behavior
2. identify whether the current path is:
   - native model replacement
   - Harmony postfix/prefix
   - mission behavior fallback
   - snapshot/materialization overlay
3. add one log line at the authoritative server point and, if relevant, one mirrored client log line
4. state in advance which exact log line should prove success or failure

During each rerun:

1. prefer one focused scenario per run
2. keep weapon/loadout/perk combinations narrow enough that one missing effect is attributable
3. check logs before inferring from feel alone

For lower-level combat work, the preferred diagnostic points are:

- `AgentStatCalculateModel`
- `StrikeMagnitudeCalculationModel`
- `AgentApplyDamageModel`
- `Mission.OnScoreHit`
- `MissionCombatMechanicsHelper`

If the effect is not visible in these points, do not add new higher-level perk glue until the lower-level path is understood.

### Current diagnostics priority for ranged damage

1. prove whether `GetWeaponDamageMultiplier(...)` was reached on authoritative server
2. prove whether `WeaponClass -> Skill` resolution matches the intended campaign skill
3. prove whether extra damage was added in `OnScoreHit` fallback
4. if campaign-vs-MP gap remains, move to `StrikeMagnitudeCalculationModel`
5. only after that inspect `AgentApplyDamageModel`

## Practical rule for the next Codex window

If a proposed change touches both:

- spawn / bootstrap / mission startup / battlefield materialization
- perk / captain / role effect logic

split it first.

No mixed diff should be merged or even tested as one unit again.
