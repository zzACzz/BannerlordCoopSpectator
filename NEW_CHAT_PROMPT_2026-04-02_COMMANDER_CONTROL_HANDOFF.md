Продовжуємо роботу в `C:\dev\projects\BannerlordCoopSpectator3`.

Спершу прочитай:
- `C:\dev\projects\BannerlordCoopSpectator3\docs\README.md`
- `C:\dev\projects\BannerlordCoopSpectator3\PROJECT_CONTEXT.md`
- `C:\dev\projects\BannerlordCoopSpectator3\docs\EXACT_CAMPAIGN_COMMANDER_CONTROL_HANDOFF_2026-04-02.md`
- `C:\dev\projects\BannerlordCoopSpectator3\docs\EXACT_CAMPAIGN_ARMY_SPAWN_AND_SPAWN_ZONE_ANALYSIS_2026-04-01.md`
- `C:\dev\projects\BannerlordCoopSpectator3\docs\EXACT_CAMPAIGN_POST_SPAWN_ARMY_BOOTSTRAP_ANALYSIS_2026-04-01.md`
- `C:\dev\projects\BannerlordCoopSpectator3\BUILD_RUNBOOK.md`

Поточний validated state:
- exact `1:1` campaign scene transfer already works on `battle_terrain_*`
- dedicated starts exact campaign scenes
- client loads the real campaign battlefield
- two armies spawn visually in campaign-like zones
- player spawn / possession works
- battle start, active battle, victory, return to campaign work
- prisoners, troop losses, and hero HP writeback already work

Поточний commander-control blocker:
- server-side commander/general control is mostly working
- client-side commander order/control handoff is only partial
- small battle can partially issue movement orders
- command menu still does not behave like campaign
- large battle previously failed to reach stable local general-control state

Найсвіжіші logs для commander issue:
- client/host large battle: `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_106040.txt`
- client/host mixed run: `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_98836.txt`
- dedicated: `C:\Users\Admin\AppData\Local\Temp\CoopSpectatorDedicated_logs\logs\rgl_log_106388.txt`

Ключові architectural facts:
- `OrderController` itself supports full order set, not only movement
- server already receives multi-formation movement orders in successful small-battle runs
- `MissionGauntletMultiplayerOrderUIHandler` still builds `MissionOrderVM(..., false, true)`
- singleplayer order UI builds `MissionOrderVM(..., IsDeployment, false)`
- this means the remaining problem is split into:
  1. functional client commander handoff
  2. separate UI migration from MP-style order handler to campaign-like commander UI

Найсвіжіші code changes already built and deployed:
- in `C:\dev\projects\BannerlordCoopSpectator3\Patches\BattleMapSpawnHandoffPatch.cs`
- local commander promotion now falls back to `controlledAgent.Formation` if `MissionPeer.ControlledFormation` is still null
- auto-select fallback can now call real `PlayerOrderController.ClearSelectedFormations()` + `SelectAllFormations(false)` if troop VM method is insufficient

Що робити далі:
1. Validate the latest commander handoff patch on both a large battle and a small battle.
2. In client logs, confirm:
   - `promoted local exact-scene commander to general control after BotsControlledChange`
   - `finalized local exact-scene commander order control after Agent.Main attach`
   - `maintained local exact-scene commander order control ... FormationsWithUnits=... OwnedFormationsWithUnits=... AutoSelectAllInvoked=...`
3. If large battle still does not get full control, inspect exact client-side `MissionOrderVM` / `MissionOrderTroopControllerVM` state instead of touching server spawn logic again.
4. If functional control becomes correct, move to the next explicit task:
   migrate commander UI/UX from multiplayer-style order handler toward campaign-style command behavior.

Не повертайся до old solved areas без нових log-backed причин:
- exact scene transfer
- army spawn zones
- player possession
- battle completion
- prisoner/casualty writeback

Фокус цього handoff саме commander/general control on exact campaign scenes.
