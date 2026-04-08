# New Window Prompt: Exact Campaign 1:1 Transfer And Friendly Banners

Continue work in `C:\dev\projects\BannerlordCoopSpectator3`.

First read:

- `docs/README.md`
- `PROJECT_CONTEXT.md`
- `docs/EXACT_CAMPAIGN_COMMANDER_CONTROL_HANDOFF_2026-04-02.md`
- `docs/EXACT_CAMPAIGN_1_TO_1_TRANSFER_AND_FRIENDLY_BANNERS_HANDOFF_2026-04-07.md`

Current validated state:

- exact `1:1` campaign scene transfer works
- dedicated starts exact campaign scenes
- client loads the real campaign battlefield
- exact roster transfer now works well enough that a fresh first battle can be visually `1:1`
- large-battle reinforcements work
- battle completion after last enemy dies was already fixed
- second-battle stale snapshot contamination was fixed
- commander ownership/control state is log-backed as correct

Do not reopen solved areas without new log-backed reason:

- exact scene transfer
- reinforcements / battle completion
- second-battle stale snapshot leak
- general commander ownership handoff if logs still show `PlayerOwner` and `HasPlayerControlled=True`

Current blocker:

- friendly overhead banners / troop icons above allied player-controlled units are still missing in battle-map runtime

Latest logs tied to this issue:

- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_127224.txt`
- `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_128072.txt`
- `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\logs\rgl_log_139320.txt`

Latest log-backed conclusion:

- ownership is correct
- the missing banners are not caused by commander control loss
- native multiplayer battle uses `MissionAgentLabelUIHandler` for overhead troop labels
- earlier patch targeted only `MissionFormationMarkerUIHandler`, which was the wrong native view for this symptom
- latest code now injects `MissionAgentLabelUIHandler` into coop battle-map runtime, but this still needs validation in a fresh run
- if that still does not restore banners, the next likely root cause is `ExactCampaignSnapshotAgentOrigin.Banner => null`

Key files:

- `GameMode/MissionMultiplayerCoopBattleMode.cs`
- `Infrastructure/ExactCampaignArmyBootstrap.cs`
- `Mission/CoopMissionBehaviors.cs`
- `Patches/BattleMapSpawnHandoffPatch.cs`

Important decompile references:

- `.codex_tmp/MissionAgentLabelView.cs`
- `.codex_tmp/decompiled_multiplayer_view/TaleWorlds.MountAndBlade.Multiplayer.View.MissionViews/MultiplayerMissionViews.cs`
- `.codex_tmp/decompiled_multiplayer_view/TaleWorlds.MountAndBlade.Multiplayer.View.MissionViews/MultiplayerPracticeMissionViews.cs`
- `.codex_tmp/decompiled_mountandblade/TaleWorlds.MountAndBlade/PartyGroupAgentOrigin.cs`

What I want next:

1. Validate the latest `MissionAgentLabelUIHandler` injection on one fresh battle after full restart of client and dedicated.
2. In client logs confirm whether:
   - `CoopBattle client: injected agent label and formation marker mission views...` appears
   - `MissionAgentLabelUIHandler creation skipped/failed` does not appear
3. If banners are still missing, do a native-parity patch for exact origin banner data:
   - carry non-null `Banner` in `ExactCampaignSnapshotAgentOrigin`
   - prefer leader party clan banner, then party map faction banner, then side/team fallback
4. Keep conclusions log-backed, not guessed.
5. Prefer cleaner native parity over new workaround UI layers.

Important:

- use `ilspycmd` when needed
- focus specifically on native overhead troop banner / label contract
- avoid reopening old solved areas unless fresh logs point there
