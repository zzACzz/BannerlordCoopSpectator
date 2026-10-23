# Two-PC development preview — b131, 2026-10-17

This is an unreleased development snapshot, not a replacement public v0.3.2
release and not a confirmed fix for the reported deployment stall. Assembly
version metadata remains 0.3.2; use MANIFEST.json hashes to identify this build.
All three roles must use this package. Do not mix with the public-release DLLs.

This candidate corrects village scene names ending in `_village`, including
`battania_village`. Previously the host could replace that actual village scene
with a field map patch while retaining VillageBattle context, blocking exact
equipment readiness and leaving both clients at Loading battle data. Both roles
compiled and all 26 contract projects passed; this specific native fix still
requires two-PC acceptance. Map marker changes since b124 are also included but
are not yet visually accepted. Do not install while a test is in progress.

Priority reproduction: finish a field battle, then enter the same problematic
village/bandit encounter without restarting. Verify village scenery, side/troop
selection, deployment, combat and campaign results on both PCs. Repeat the village
battle. Retain same-attempt logs from all four processes if any stage fails.

## Install with all game/server processes closed

Back up saves and move the existing CoopSpectator and CoopSpectatorDedicated
module folders outside Modules before installing, so old files cannot remain.
Keep the backups for rollback. Do not overwrite the game's Native, SandBoxCore,
SandBox or Multiplayer module folders.

- BOTH PCs: copy Client/Modules/CoopSpectator into the Bannerlord game's Modules
  directory. Copy Client/run_mp_with_mod_from_game_root.bat into the GAME ROOT,
  alongside bin and Modules, not onto the desktop. A desktop shortcut may point
  to that BAT. The bundled shader-cache helper is retained.
- HOST only: copy Host/Modules/CoopSpectatorDedicated into the Modules directory
  of the Dedicated Server installation selected by your existing host setup.
  This is a HostLite-style module update, not a complete dedicated-server install.
  Keep the existing working base-scene/server installation and networking settings.
- Use matching Bannerlord game versions on both PCs and a compatible dedicated
  installation. Do not infer compatibility solely from the mod version label.
- Bundled Harmony is included. No separately enabled Harmony mod is required
  for this baseline test. Avoid other mods for the first reproduction.

## Baseline field-battle test

1. On the host PC, load a TEST campaign and start the dedicated server from its menu.
2. Start the modded multiplayer client via the supplied BAT on BOTH PCs.
3. Complete the game's Login screen and connect BOTH clients to the same server.
   The host must also join in multiplayer; the campaign battle window is not a
   live multiplayer battlefield. Do not fight the campaign copy independently.
4. Start a small field encounter from the campaign. Select the host/commander
   role in the host's multiplayer client and a troop on the second PC.
5. Complete deployment and the pre-battle hold in the HOST MULTIPLAYER window.
   The existing H shortcut advances the applicable host deployment/start stage;
   check the displayed stage after each press. Deploy on a regular troop may
   queue its entry until deployment completes; it does not start both armies.
6. Verify both PCs see movement/combat, then finish the battle and check campaign
   results. Repeat once; compare a night battle and a village battle separately.
7. If stuck, stop advancing the campaign independently. Retain logs from campaign,
   dedicated server AND both multiplayer clients from that same attempt, including
   rgl_log, rgl_log_errors and watchdog logs. Record the time and visible phase.

## Experimental features

The global-map replica and automatic map/battle lifecycle remain opt-in via
COOPSPECTATOR_CAMPAIGN_MAP_PROTOTYPE=1 and
COOPSPECTATOR_AUTOMATIC_CAMPAIGN_MISSIONS=1 inherited by the relevant processes.
Leave these unset for the initial public-flow deployment reproduction above.
Do not use a map test as proof that an ordinary-release battle path is fixed.

Leave COOPSPECTATOR_PORTRAIT_POSE_OBSERVER unset. New encounter portrait pose
capture/transport/rendering is included but remains default-off and has NOT been
visually accepted in Bannerlord. Map protocol42/bridge37/catalog9 requires matching
roles. The same installed package may later be used for an explicitly enabled map
test, without swapping individual DLLs.

Rollback: close all game/server processes, move the test module folders aside,
restore the backed-up module folders and original launcher. No automatic installer
or machine-wide setting changes are included in this archive.
