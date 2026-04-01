# Exact Campaign Post-Spawn Army Bootstrap Analysis

Date: 2026-04-01  
Project: `BannerlordCoopSpectator3`

## Goal

З'ясувати по native code і свіжих exact-scene логах, чи поточний blocker ще сидить у captain/formation handoff, чи вже зсунувся в deeper army bootstrap після успішного player spawn.

## Fresh runtime evidence

Run artifacts:

- client/local process: `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_97080.txt`
- host/local process: `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_61756.txt`
- dedicated: `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\logs\rgl_log_94492.txt`
- dedicated watchdog: `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\logs\watchdog_log_94492.txt`

### 1. Captain/select-all handoff is no longer the active unknown

Client log now shows that local suppression triggers on the exact-scene spawn handshake:

- `BattleMapSpawnHandoffPatch: suppressed local OrderController.SelectAllFormations during exact-scene spawn handshake...`

at:

- `rgl_log_97080.txt:2836`

Practical consequence:

- `OrderController.SelectAllFormations` is no longer an unverified hypothesis.
- The earlier captain/select-all path was real, but it is now actively suppressed on the client at the exact moment `AssignFormationToPlayer` arrives.

### 2. Exact-scene player spawn still succeeds

Dedicated log shows the player possession chain completes:

- on-demand possession seed completes
- `materialized army replace-bot succeeded`
- peer runtime state becomes `Spawned`
- lifecycle becomes `Alive`
- `possessed materialized army agent via vanilla replace-bot flow`

at:

- `rgl_log_94492.txt:2071`
- `rgl_log_94492.txt:2077`
- `rgl_log_94492.txt:2081`
- `rgl_log_94492.txt:2083`
- `rgl_log_94492.txt:2085`

So the current blocker is no longer:

- exact scene loading
- initial on-demand seed creation
- replace-bot possession itself
- local `SelectAllFormations` echo

### 3. The server now dies after possession, during the delayed post-spawn phase

After successful possession, dedicated enters:

- `Phase=Deployment`
- `Phase=PreBattleHold`
- `deferring automated battlefield materialization on exact campaign scene after player possession...`

at:

- `rgl_log_94492.txt:2093`
- `rgl_log_94492.txt:2095`
- `rgl_log_94492.txt:2102`
- `rgl_log_94492.txt:2112`

There are no later managed spawn logs after the defer window. The process then dies with the same native AV class:

- `ExceptionCode: 0xC0000005`
- read parameter `0x40a80`

in:

- `watchdog_log_94492.txt`

This means the crash boundary has moved past:

- exact-scene load
- player possession
- early captain/select-all handoff

The live blocker is now in the post-possession army bootstrap stage.

## Native campaign contract

Decompile through `ilspycmd` confirms the exact field-battle lifecycle.

### 1. `SandBox.SandBoxMissions.OpenBattleMission`

Native campaign `OpenBattleMission(rec)` builds the battle with:

- `CreateCampaignMissionAgentSpawnLogic(...)`
- `BattleSpawnLogic("battle_set")`
- `SandBoxBattleMissionSpawnHandler`
- `AssignPlayerRoleInTeamMissionController`
- `SandboxGeneralsAndCaptainsAssignmentLogic`
- `BattleDeploymentMissionController`
- `BattleDeploymentHandler`

This means campaign battle startup is a coordinated stack, not a single spawn call.

### 2. `CreateCampaignMissionAgentSpawnLogic`

Campaign armies come from:

- `PartyGroupTroopSupplier(MapEvent.PlayerMapEvent, BattleSideEnum.Defender, ...)`
- `PartyGroupTroopSupplier(MapEvent.PlayerMapEvent, BattleSideEnum.Attacker, ...)`

So native campaign army spawn is sourced directly from `MapEvent.PlayerMapEvent`, not from our MP materialized entry layer.

### 3. `SandBoxBattleMissionSpawnHandler.AfterStart`

After mission start, campaign calls:

- `MissionAgentSpawnLogic.InitWithSinglePhase(...)`

using the attacker/defender counts from `MapEvent`.

This is the native bootstrap that seeds the battle-sized spawn phases.

### 4. `BattleDeploymentMissionController`

Native deployment controller does the following:

- `OnAfterStart()`
  - disables spawning for both sides
  - disables reinforcements
- `OnSetupTeamsOfSide(side)`
  - enables spawn for that side with `enforceSpawning: true`
  - calls `MissionAgentSpawnLogic.OnSideDeploymentOver(side)`
- `AfterDeploymentFinished()`
  - re-enables reinforcements
  - removes `BattleDeploymentHandler`

So campaign field battle does not start by spawning one possessed unit and then manually dripping AI afterwards. It first lets the deployment system bring both sides into a consistent deployed state.

### 5. `MissionAgentSpawnLogic`

Critical behavior from decompile:

- `OnMissionTick()` on server only proceeds if `CheckDeployment()` succeeds.
- `CheckDeployment()`:
  - reserves troops for each side
  - builds deployment plans per team
  - uses spawn paths when `Mission.HasSpawnPath == true`
  - spawns initial troops for sides whose deployment plan is ready and spawn is enabled
  - once initial spawn is complete, calls `OnSideDeploymentOver(side)`
- `SetReinforcementsSpawnEnabled(true)` opens later reinforcement waves only after deployment flow is complete.

This is the exact native answer to "how do two armies appear on the field in campaign?"

They are not spawned by a post-possession MP materialization pass. They are spawned by `MissionAgentSpawnLogic` while deployment is still authoritative and before reinforcement timing begins.

### 6. `AssignPlayerRoleInTeamMissionController`

Relevant fact:

- `OnTeamDeployed(team)` sets `team.PlayerOrderController.Owner = Agent.Main`
- then calls `team.PlayerOrderController.SelectAllFormations()`

This confirms that formation/captain logic belongs to the deployment-role layer, but in the latest logs this early `SelectAllFormations` is already suppressed on the client.

So commander/captain analysis is useful for later gameplay parity, but it is no longer the strongest current suspect for the exact-scene crash.

## Current architectural conclusion

We now have a cleaner picture:

1. Exact `battle_terrain_*` transfer works.
2. Player possession via exact-scene on-demand seed works.
3. Early captain/select-all handoff is already intercepted on the client.
4. The remaining blocker is that post-spawn army bootstrap is still hybrid.

Current exact-scene MP path:

- create one on-demand seed
- possess it via MP replace-bot
- defer automated battlefield materialization
- later try to materialize armies through custom `CoopMissionSpawnLogic`

Native campaign path:

- initialize `MissionAgentSpawnLogic` from `MapEvent.PlayerMapEvent`
- let deployment controller enable side spawns
- spawn both armies through deployment-ready plans
- only then proceed into deployment completion and reinforcements

That mismatch is now the most likely root cause.

## Answer to the commander-mode question

### Is it worth deeply researching formation/commander mode now?

Yes, but not as the immediate next root-cause hunt.

It is relevant because:

- long-term we need main hero and enemy leader to command armies
- campaign battle already uses `AssignPlayerRoleInTeamMissionController`
- campaign battle already uses `SandboxGeneralsAndCaptainsAssignmentLogic`

But for the current spawn blocker it is not the best next target anymore, because the live evidence already shows the earlier captain/select-all boundary has been suppressed successfully.

### Is the picture complete enough to continue spawn work?

We now have enough picture to stop guessing about captain mode.

We do not yet have a complete implementation recipe for the final fix, because the remaining missing piece is:

- how to bridge exact-scene runtime from "player possessed" to "native two-army deployment/bootstrap" without falling back to our custom delayed materialization path.

## Recommended next step

The next analysis/code direction should target native army bootstrap, not broader commander UI:

1. Compare our exact-scene post-possession path against native `MissionAgentSpawnLogic` deployment lifecycle.
2. Determine whether we can:
   - reuse native `MissionAgentSpawnLogic` to seed both armies from campaign-derived suppliers, or
   - reproduce its deployment-phase side activation order closely enough inside the MP runtime.
3. Avoid further blind patches in `AssignFormationToPlayer` / `SelectAllFormations` unless new logs move the boundary back there.

## Bottom line

Commander/captain systems matter for the final product, but the current blocker is narrower:

- not "how do commanders work?"
- but "how do we hand off from exact-scene player possession into native-style two-army deployment/bootstrap?"

That is the next layer that needs exact analysis and then implementation.
