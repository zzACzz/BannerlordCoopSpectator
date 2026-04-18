# Coop Battle Stabilization And Connectivity Handoff

Date: 2026-04-18
Project: `BannerlordCoopSpectator3`
Validated working head at time of writing: `6eb32dc` (`fix names`)
Important earlier milestone kept in this baseline: `8116fdc` (`fix crafted weapon`)

## Why this document exists

The battle/runtime branch was finally pushed back into a usable state after a long cycle of regressions around:

- spawn and battlefield materialization
- exact campaign equipment transfer
- personal perk/stat transfer
- companion identity and naming
- UI/render side effects from preview and campaign mannequin investigations

The next task is different:

- `Public` official connection
- `Radmin VPN` / overlay connection

That work must be isolated from battle/runtime, because hidden parallel connectivity changes previously polluted spawn/perk debugging and cost a full day of false leads.

## Current move-on state

Move-on threshold is reached.

From the latest successful battle-flow rerun:

- client selection layer opened
- player army materialized
- main hero and companions spawned with exact low-level overlay/equipment paths
- exact crafted item path for `battania_polearm_1_t5` stayed working
- battle completed and aftermath wrote back correctly

User-facing summary at the time of handoff:

- unit rendering is working again
- battle runtime is usable again
- one residual cosmetic issue may still remain around the second companion display name showing a surrogate label in some HUD path

That remaining name issue is no longer a blocker for moving to the connectivity thread.

## Log-backed evidence from the successful runtime state

Client evidence from `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_52828.txt`:

- `3008`
  `CoopMissionSelectionView: coop selection layer added.`
- `3086`
  `BattleSnapshotRuntimeState: resolved mission-safe fallback character for battle entry ... CharacterObject_1660 ... FallbackCharacterId=mp_light_infantry_vlandia_hero`
- `3680`
  `CoopMissionSpawnLogic: applied client-local exact campaign visual overlay ... EntryId=attacker|player_party|CharacterObject_1660|mp_skirmisher_vlandia_hero ... TroopName=Halgard the Brave`
- `3714`
  `CoopMissionSpawnLogic: applied client-local exact campaign visual overlay ... CharacterObject_1653 ... TroopName=Iara the Healer`
- `3742`
  `CoopMissionSpawnLogic: applied client-local exact campaign visual overlay ... main_hero ... TroopName=Yorig`

Dedicated evidence from `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\logs\rgl_log_53752.txt`:

- `39285`
  `CoopMissionSpawnLogic: battle result snapshot written ...`
- `39287`
  `CoopMissionSpawnLogic: authoritative battle completion detected ...`

## What was achieved in this branch

### 1. Spawn and battlefield materialization were recovered

The project returned from:

- empty battlefield
- spectator-only fallback
- load-time server crashes
- late mission clears
- client-side spawn sync gaps

to a working battle flow with:

- side selection
- deploy
- possession
- battle progression
- aftermath/writeback

### 2. Exact campaign equipment transfer now works at the low level

This was the biggest equipment milestone:

- exact hero equipment ids survive snapshot transfer
- mission-safe fallback remains only where exact parity is still intentionally needed
- crafted item parity was pushed deep enough that `battania_polearm_1_t5` stopped degrading into a surrogate spear

Key result:

- Lara can now carry the intended crafted polearm instead of a stand-in weapon

### 3. Personal perk/stat transfer was moved to the native/lower layers

This branch stopped treating perks as a broad top-level glue problem and instead moved behavior into lower runtime paths:

- ammo parity
- mounted crossbow reload parity
- bow/javelin damage parity improvements
- melee / hp / armor buckets
- hero/companion low-level stat and damage integration

The effective method was:

- decompile native behavior
- identify the real owning model/function
- instrument one authoritative log point
- implement a narrow low-level bridge

### 4. Companions now participate in the same hero-agent low-level path

The work no longer applies only to `main_hero`.

Companions now have:

- exact equipment path
- low-level perk/stat path
- fallback-aware exact overlay path

The remaining surrogate-name issue is narrow and display-path-specific, not a broad hero-runtime failure anymore.

### 5. The branch also taught a negative lesson about preview/tableau work

The campaign mannequin / `CharacterTableauWidget` / shared render-scene path became a large distraction.

Important conclusion:

- native tableau/campaign mannequin problems can leak across unrelated screens
- this is a separate problem from battle spawn, perk transfer, and exact equipment parity
- do not reopen that work casually while doing connectivity

## Stable areas that should not be reopened casually

Treat these as protected unless fresh logs prove the connectivity bug crosses into them:

- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\BattleSnapshotRuntimeState.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\ExactCampaignRuntimeItemRegistry.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Patches\BattleMapSpawnHandoffPatch.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\MissionModels\CoopCampaignDerivedAgentStatCalculateModel.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\MissionModels\CoopCampaignDerivedStrikeMagnitudeCalculationModel.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\MissionModels\CoopCampaignDerivedAgentApplyDamageModel.cs`

## Connectivity layer that should be investigated next

These files are the correct starting point for the new thread:

- `C:\dev\projects\BannerlordCoopSpectator3\Commands\CoopConsoleCommands.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\DedicatedHelper\DedicatedServerLaunchSettings.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\DedicatedHelper\DedicatedServerHostingMode.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\DedicatedHelper\DedicatedHelperLauncher.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Infrastructure\HostSelfJoinRedirectState.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Patches\LobbyCustomGameLocalJoinPatch.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Patches\LobbyJoinResultSelfJoinArmPatch.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Patches\LocalJoinAddressPatch.cs`

## What the next thread must not do

Do not repeat the earlier failure mode:

- do not mix connectivity investigation with spawn/perk/runtime rewrites
- do not guess from gameplay before reading the low-level connect/join flow
- do not reintroduce hidden parallel diffs

## Proven working method to reuse in the next thread

The approach that actually worked here was:

1. go to the native/lower layer first
2. use `ilspycmd`
3. identify the real owning class/function/model
4. instrument one authoritative log point before changing behavior
5. make the smallest possible fix
6. validate with a clean rerun

That same method should now be applied to `Public` official connection and `Radmin VPN` connectivity.
