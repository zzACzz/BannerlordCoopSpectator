# Session Handoff 2026-03-09

## Stable baseline
- `GameType=TeamDeathmatch`
- listed join stable
- mission load stable
- `start_mission` / `end_mission` stable
- leaving SP battle no longer crashes dedicated

## Confirmed working pipeline
1. SP host detects battle.
2. SP writes `battle_roster.json`.
3. Dedicated reads roster.
4. Campaign troops/heroes are mapped to `mp_*` surrogate ids.
5. Dedicated resolves surrogate to `BasicCharacterObject`.

Example confirmed in logs:
- `main_hero` -> `mp_light_cavalry_sturgia_troop`
- dedicated resolves it to `Raider`

## What was disproven
- Post-spawn ownership transfer is not viable.
  - Result: "half-alive" rider, second vanilla TDM agent nearby, broken combat controls.
- Observer/tick forcing of `MissionPeer.SelectedTroopIndex` is not viable.
  - Result: troop menu disappears, player stays spectator, or vanilla TDM still wins.
- Direct use of campaign troop ids in MP runtime is not viable.

## Current conclusion
The problem is no longer crash/debug. The problem is architecture.

The correct pivot is:
- keep vanilla `TeamDeathmatch` mission/bootstrap as the stable baseline;
- stop trying to override vanilla TDM troop selection through observer hacks;
- stop using vanilla TDM troop menu as the source of truth for coop unit control.

## Recommended next implementation
### Phase A
After side selection, bypass vanilla TDM troop selection as a gameplay source.

Options:
1. simplest: auto-spawn one allowed surrogate immediately;
2. better: show a tiny custom coop picker with only allowed units.

### Phase B
Server spawns the chosen surrogate as the player's real agent directly.

Important:
- no late ownership swap
- no extra diagnostic agent
- no SelectedTroopIndex hacks from mission tick

### Phase C
Only after Phase B is stable:
- multiple allowed units
- explicit unit choice
- equipment/stat overrides from campaign
- later appearance replication

## Files to start from next session
- `C:\dev\projects\BannerlordCoopSpectator3\PROJECT_CONTEXT.md`
- `C:\dev\projects\BannerlordCoopSpectator3\HUMAN_NOTES_MULTIPLAYER_PROGRESS.md`
- `C:\dev\projects\BannerlordCoopSpectator3\Mission\CoopMissionBehaviors.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Campaign\BattleDetector.cs`
- `C:\dev\projects\BannerlordCoopSpectator3\Patches\MissionStateOpenNewPatches.cs`

## Last experiment state
- A new patch was added for `MultiplayerClassDivisions.GetMPHeroClassForPeer(...)`
- It still did not beat vanilla TDM selection reliably in practice
- Do not continue investing in observer/tick SelectedTroopIndex hacks
