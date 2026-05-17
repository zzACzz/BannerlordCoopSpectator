# Exact Campaign 1:1 Transfer Handoff (2026-04-30)

Branch for this work:

- `codex/runtime-regression-checkpoint-2026-04-26`

This note is for the next window.

Primary goal:

- finish the exact `1:1` transfer path for campaign units into hosted multiplayer
- stop relying on unstable client-side visual correction as the long-term model
- re-check the whole possession/runtime contract from the lowest-level evidence up

## Current Situation

The project is no longer in the "large battle always explodes immediately" state.

What is already materially improved:

1. hosted large battles now bootstrap and run far more often without early total
   client crash
2. bulk AI no longer mass-spawns into the old `CreateAgent /
   SetWieldedItemIndex` failure storm
3. bot-count overflow (`298/299` into a `0..255` network contract) was clamped
4. exact-entry diagnostics now exist
5. per-agent spawn tracing now exists
6. exact-template compatibility is visible instead of silently hidden behind
   fallback

What remains as the main blocker:

- the `player-controlled mounted hero` handoff on the client is still wrong
- the local client commander is still visually corrupted
- the remote host hero is still visually corrupted from the client's point of
  view
- client death/cleanup still sometimes ends in native crash

In short:

- server runtime is much healthier now
- bulk AI path is much healthier now
- the remaining unstable path is the possession/visual/death lifecycle for the
  selected player-controlled exact hero

## What Was Implemented On This Branch

### 1. Exact-entry diagnostics

Added explicit compatibility reporting for hosted battle entries:

- `C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\ExactBattleEntryCompatibilityBridgeFile.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`

Generated runtime file:

- `Documents\Mount and Blade II Bannerlord\CoopSpectator\battle_entry_compatibility.txt`

Purpose:

- prove whether an entry is really `exact-supported`
- show fallback/degraded paths explicitly
- expose mount, weapon, wield, and hosted materialization risk per `EntryId`

### 2. Per-agent spawn trace

Added explicit per-agent runtime trace:

- `C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\ExactBattleAgentSpawnTraceBridgeFile.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`

Generated runtime file:

- `Documents\Mount and Blade II Bannerlord\CoopSpectator\battle_agent_spawn_trace.txt`

Purpose:

- map client `agent index` back to exact `EntryId`
- prove which path created or replaced the agent
- record spawn equipment, initial wield, overlay decisions, and warnings

### 3. Hosted runtime stabilization

Implemented over the course of this branch:

- battle snapshot ack gating
- peer/session cleanup toward a more canonical runtime state
- stricter exact-entry validation
- bulk-AI crash isolation
- clamp for controlled-bot replication counts
- safer native bootstrap sizing for hosted battles

Key files touched:

- `C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\CoopBattlePeerLifecycleRuntimeState.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\CoopBattleSpawnRuntimeState.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\ExactCampaignArmyBootstrap.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionNetworkBridge.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Network\Messages\CoopBattleSelectionNetworkMessages.cs`

### 4. Bulk AI spawn contract tightening

Implemented:

- blocking unsafe exact weapon injection for degraded/unsupported hosted entries
- avoiding partial hybrid equipment states that caused fists-only or invalid
  spawn states
- forcing clearer separation between native-template spawn and exact-loadout
  injection

Key files:

- `C:\dev\projects\BannerlordCoopSpectator3\Patches\ExactCampaignPreSpawnLoadoutPatch.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`

### 5. Client exact-visual queue/watchdog

Implemented:

- pending exact visual refresh queue for client hero overlays
- watchdog escalation for stuck hero visual refresh
- fallback battle-map observer because `CoopMissionClientLogic` is skipped in
  crash-isolation battle-map client stack

Key files:

- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\GameMode\MissionBehaviorDiagnostic.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\GameMode\MissionMultiplayerCoopBattleMode.cs`

Important:

- the most recent logs provided by the user were still pre-validation for this
  fallback observer path
- the latest code now calls
  `CoopMissionSpawnLogic.TryRunClientExactCampaignVisualObserver(...)` from
  `MissionBehaviorDiagnostic.OnMissionTick(...)` for battle-map clients
- this must be revalidated from fresh logs

## What The Latest Logs Proved

Latest user-provided client logs:

- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_27116.txt`
- `C:\ProgramData\Mount and Blade II Bannerlord\logs\watchdog_log_27116.txt`

Latest user-provided host logs:

- `C:\Users\Admin\Downloads\Telegram Desktop\battle_entry_compatibility.txt`
- `C:\Users\Admin\Downloads\Telegram Desktop\battle_agent_spawn_trace.txt`
- `C:\Users\Admin\Downloads\Telegram Desktop\rgl_log_11016.txt`
- `C:\Users\Admin\Downloads\Telegram Desktop\rgl_log_11988.txt`
- `C:\Users\Admin\Downloads\Telegram Desktop\rgl_log_18024.txt`

### Proven good

1. hosted exact entries for the relevant commander heroes do resolve on the
   host as `exact-supported`
2. host-side battle runtime is stable enough to keep progressing
3. queue creation for client exact visual refresh did happen for:
   - local client commander
   - remote host hero

### Proven bad

1. on the client, exact visual refresh was queued but never completed
2. `CoopMissionClientLogic` was not actually present in the battle-map client
   stack during those runs
3. because of that, the client-side observer never ticked, so the queued exact
   visual refresh never executed
4. client death still ended in native crash after `SetAgentHealth(0)` followed
   by weapon-drop cleanup on the dead controlled hero

### Evidence that mattered most

From `rgl_log_27116.txt`:

- `MissionBehaviorDiagnostic AfterStart ENTER` exists
- `CoopMissionClientLogic AfterStart ENTER` does not exist
- `queued client exact visual overlay refresh ... AgentIndex=222 ...`
- `queued client exact visual overlay refresh ... AgentIndex=7 ...`
- no `completed pending client exact visual refresh`
- no `watchdog applied client exact hero visual overlay`
- no `escalating stuck pending client exact hero visual refresh`

From `watchdog_log_27116.txt`:

- native `0xC0000005` still happens after death

## Current Architectural Reading

The present architecture is still a hybrid:

1. native MP creates or replaces a player-controlled agent
2. exact campaign state is then partially or fully layered back on top
3. on hosted battle-map runtime, some client-side exact visual repair is still
   required to approximate the campaign unit

That hybrid path is exactly what still produces:

- naked mounted hero with only partial equipment visible
- wrong remote hero appearance
- stale horse/rider mesh contamination like the extra leg
- fragile death cleanup on the exact hero path

## The Correct Long-Term Target

The correct target is still:

1. server materializes the real exact campaign unit
2. the resulting spawn/equipment/mount state is natively admissible to MP
3. the client takes control of that already-correct exact unit
4. client-side repair/overlay becomes minimal or unnecessary

That means the final architecture should move away from:

- repeated post-spawn visual overlays
- duplicated rider/horse correction
- possession paths that require client-only reconstruction of exact state

## What Must Be Re-Analyzed From The Lowest Level

The next window must not continue broad hypothesis patches.

It must re-check the exact mounted hero path from the bottom up:

1. `Mission.SpawnAgent`
2. `AgentBuildData.Equipment`
3. exact horse / horse harness application
4. `CreateAgent`
5. `SetAgentPeer`
6. `SynchronizeAgentSpawnEquipment`
7. local and remote client mission-view / mission-logic observer stack
8. death path:
   - `SetAgentHealth`
   - drop / ammo / wield cleanup
   - mounted rider/horse separation

Use evidence in this order:

1. fresh logs
2. exact per-agent trace
3. decompilation / `ilspycmd`
4. concrete exact entry/equipment examples
5. only then code changes

## Current Most Likely Narrow Fault Zones

These are the highest-value areas to re-check first:

### 1. Battle-map client observer gap

Even though a fallback observer was just added, it still needs validation.

Files:

- `C:\dev\projects\BannerlordCoopSpectator3\GameMode\MissionBehaviorDiagnostic.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`

Question:

- does the queued exact visual refresh now really execute on the client?

### 2. Mounted hero visual contamination

Files:

- `C:\dev\projects\BannerlordCoopSpectator3\Patches\BattleMapSpawnHandoffPatch.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`

Question:

- are we still applying rider/horse visual state twice, or at the wrong
  lifecycle moment, for player-controlled exact mounted heroes?

### 3. Death cleanup on exact mounted hero

Question:

- after the hero dies, does native cleanup still assume a different equipment /
  mount state than the one we constructed?

The latest logs strongly suggest this path is still live:

- death
- drop path
- native crash

## What Should Be Done Next

### Phase 1. Fresh low-level revalidation

Run a fresh test after the latest fallback observer patch and confirm whether
the client log now contains:

1. `MissionBehaviorDiagnostic: running battle-map client exact visual observer fallback...`
2. `completed pending client exact visual refresh`
   or
3. `escalating stuck pending client exact hero visual refresh...`
4. `watchdog applied client exact hero visual overlay`

If those lines do not appear, stop and prove why the observer path is still not
executing.

### Phase 2. Rebuild the mounted hero possession contract on evidence

Do not patch blindly.

For the `player-controlled mounted hero` path, explicitly prove:

1. which exact equipment slots are present at spawn
2. which slots are later reapplied
3. whether horse/rider visual state is duplicated
4. whether local commander and remote host use the same or different client
   exact-refresh route

### Phase 3. Reduce client-side overlay dependency

The end state should move toward:

- server-authoritative exact spawn
- client ownership handoff
- minimal client exact visual repair

not toward more and more local patch layering

## Process Rules For The Next Window

These rules are important and intentional:

1. start with a fresh low-level analysis again, even if it repeats some work
2. assume we may still have missed a lower-level contract detail
3. before any implementation, provide exactly 3 concrete solution options
4. for each option, state:
   - what layer it changes
   - why it may work
   - main risk
   - whether it is temporary triage or target architecture
5. wait for user choice before implementing one of those options

This is required so the next window does not jump directly into another narrow
patch without checking whether a cleaner option exists.

## Files The Next Window Should Read First

1. `C:\dev\projects\BannerlordCoopSpectator3\docs\EXACT_TEMPLATE_PATH_HANDOFF_2026-04-26.md`
2. `C:\dev\projects\BannerlordCoopSpectator3\docs\HOSTED_BATTLE_RUNTIME_ARCHITECTURE_AUDIT_2026-04-26.md`
3. `C:\dev\projects\BannerlordCoopSpectator3\docs\EXACT_CAMPAIGN_1_TO_1_TRANSFER_HANDOFF_2026-04-30.md`

Then inspect:

1. `C:\dev\projects\BannerlordCoopSpectator3\GameMode\MissionMultiplayerCoopBattleMode.cs`
2. `C:\dev\projects\BannerlordCoopSpectator3\GameMode\MissionBehaviorDiagnostic.cs`
3. `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`
4. `C:\dev\projects\BannerlordCoopSpectator3\Patches\BattleMapSpawnHandoffPatch.cs`
5. `C:\dev\projects\BannerlordCoopSpectator3\Patches\ExactCampaignPreSpawnLoadoutPatch.cs`

## Final Summary

We are no longer debugging a generic hosted battle collapse.

We are now debugging a much narrower contract:

- exact mounted hero possession
- exact hero visual/state parity on remote client
- exact hero death cleanup

That is progress.

But the next window should validate the latest observer fix first, then re-open
the mounted exact hero path from the lowest possible level before choosing the
next implementation step.
