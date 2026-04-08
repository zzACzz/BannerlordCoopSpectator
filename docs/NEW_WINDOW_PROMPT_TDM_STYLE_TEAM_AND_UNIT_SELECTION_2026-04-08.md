# New Window Prompt: TDM-Style Team And Unit Selection

Continue work in `C:\dev\projects\BannerlordCoopSpectator3`.

First read:

- `docs/README.md`
- `PROJECT_CONTEXT.md`
- `docs/COOP_SELECTION_UI_TECHNICAL_MAP_2026-03-27.md`
- `docs/TDM_STYLE_TEAM_AND_UNIT_SELECTION_HANDOFF_2026-04-08.md`
- `BUILD_RUNBOOK.md`

Current validated state:

- exact campaign scene transfer works
- dedicated starts exact campaign scenes
- client loads the real campaign battlefield
- large-battle bootstrap and reinforcements work well enough to move focus forward
- commander death no longer crashes dedicated
- battle continues and returns results to campaign after commander death
- hero combat XP writeback is validated enough for the main hero
- friendly formation markers / overhead type icons are present again in battle-map runtime

Parked but not current blocker:

- exact commander perk parity is still incomplete
- bonus ammo perks are not native-exact yet
- some reinforcement display names may still drift

Do not reopen solved or parked areas without new log-backed reason:

- exact scene transfer
- battle completion / campaign writeback
- commander-death crash path
- broad reinforcement surrogate debugging
- overhead formation-marker restoration
- commander perk parity / bonus-ammo work

Current blocker:

- the current coop selection UI is functionally usable but ugly and not native-looking
- after death it keeps showing stale unit lists instead of only currently alive selectable units
- the goal now is to move to familiar `TDM`-style side selection and unit/class selection shells

What the user wants:

- players should see the familiar `TDM` side-selection interface
- players should see the familiar `TDM` unit/class-selection interface
- this should happen in a proper new menu flow, not the current crude custom overlay
- after death, the player should return to side selection
- the unit list after death must contain only units that are still alive / selectable

Important existing repo facts:

- the current custom UI lives in `UI/CoopMissionSelectionView.cs`
- the stale-list bug exists because the current UI is built from `AllowedEntryIds` / `AttackerAllowedEntryIds` / `DefenderAllowedEntryIds` in `CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot`
- those ids come from authority-allowed selection state, not from a live alive-only roster
- the repo intentionally suppresses native vanilla entry UI in:
  - `Patches/VanillaEntryUiSuppressionPatch.cs`
  - `Patches/MissionStateOpenNewPatches.cs`
- do not "solve" this by blindly re-enabling native `MissionGauntletTeamSelection` / `MissionGauntletClassLoadout`

Native references to study:

- `.codex_tmp/decompiled_multiplayer_gauntlet/TaleWorlds.MountAndBlade.Multiplayer.GauntletUI.Mission/MissionGauntletTeamSelection.cs`
- `.codex_tmp/decompiled_multiplayer_gauntlet/TaleWorlds.MountAndBlade.Multiplayer.GauntletUI.Mission/MissionGauntletClassLoadout.cs`
- `.codex_tmp/decompiled_multiplayer_view/TaleWorlds.MountAndBlade.Multiplayer.View.MissionViews/MultiplayerMissionViews.cs`
- `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\Multiplayer\GUI\Prefabs\TeamSelection\MultiplayerTeamSelection.xml`
- `C:\Program Files (x86)\Steam\steamapps\common\Mount & Blade II Bannerlord\Modules\Multiplayer\GUI\Prefabs\ClassLoadout\MultiplayerClassLoadout.xml`

Key project files:

- `UI/CoopMissionSelectionView.cs`
- `Infrastructure/CoopBattleEntryStatusBridgeFile.cs`
- `Infrastructure/BattleSnapshotRuntimeState.cs`
- `Mission/CoopMissionBehaviors.cs`
- `Patches/VanillaEntryUiSuppressionPatch.cs`
- `Patches/MissionStateOpenNewPatches.cs`

What I want next:

1. Analyze the native `TDM` team-selection and class-loadout shell carefully, including prefab and VM contracts.
2. Choose the safest approach to get the same visual shell in coop:
   - either copy the native prefab shells `1:1` under coop-owned movie names
   - or build near-identical copies
   - but keep coop-owned mission views, VMs, and callbacks
3. Implement the new side-selection shell and unit-selection shell without restoring native `TDM` team/class mechanics.
4. Make post-death selection alive-only:
   - side selection reopens after death
   - selectable unit list is built from currently alive units only
   - do not rely only on stale `AllowedEntryIds`
5. Keep conclusions code-backed and log-backed, not guessed.

Preferred direction:

- prefer shell reuse / shell copying over native-mechanics reuse
- prefer a clean coop-owned contract over hacks that re-enable native `MissionLobbyEquipmentNetworkComponent` logic
- if the existing runtime data is not enough for alive-only selection, extend the authoritative data contract instead of hiding rows client-side

Important:

- use `ilspycmd` if needed
- focus specifically on `TDM` UI shell parity and alive-only selection data
- do not reopen old solved areas unless fresh logs point there
