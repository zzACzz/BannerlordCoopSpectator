# Warhost campaign-map replica — two-PC testing preview b299 (2026-10-28)

This is a development test package, not a new public release. Module/assembly
version labels remain v0.3.2; identify this exact preview by MANIFEST.json and
its SHA-256 hashes. All roles must use this same archive. Do not mix public
v0.3.2, an older preview, or individual DLLs from different packages.

Built against the locally tested Bannerlord Native v1.4.8 installation. Use the
same game version on both PCs and a compatible, already working dedicated-server
installation. Other game versions and other mods are not validated by this archive.

## Install with campaign, dedicated server and multiplayer closed

Keep backups of saves and the existing mod folders/launcher for rollback. Move
old CoopSpectator and CoopSpectatorDedicated folders outside Modules before
copying the new folders, so obsolete files cannot remain. Do not replace the
base game's Native, SandBoxCore, SandBox or Multiplayer folders.

- BOTH PCs: install Client/Modules/CoopSpectator into the Bannerlord GAME Modules
  folder. Copy Client/run_mp_with_mod_from_game_root.bat into the GAME ROOT next
  to bin and Modules. A desktop shortcut may point to that BAT; the BAT itself
  must remain in the game root. The shader-cache helper is included.
- HOST PC only: install Host/Modules/CoopSpectatorDedicated into Modules of the
  Dedicated Server installation used by the host. This is a module update, not
  a complete server/base-scene/network installation. Keep the working server
  configuration, credentials and Tailscale/LAN setup; none are in this package.
- Bundled Harmony is included with its license. A separately enabled Harmony
  mod is not required for the first test. Start without unrelated mods.

## Normal startup — no console command or environment setup required

1. On the HOST PC, launch Bannerlord campaign with CoopSpectator enabled and
   load a TEST save. Dismiss the shader notice if shown. Choose No if asked to
   enter Safe Mode, so the mod remains enabled.
2. Start the dedicated server using the campaign's Coop Dedicated Server menu.
   Leave the campaign open. Once the server is ready and the host is on the
   global map, the server automatically starts the map-replica mission.
3. Start modded multiplayer using the supplied BAT on BOTH PCs. Steam must be
   running normally under the same Windows user as the game.
4. Complete the game's Login screen and connect BOTH multiplayer clients to the
   same server (for example AC_COOP). The HOST must join in multiplayer too:
   the campaign window is not the host's multiplayer participant.
5. The map replica should appear without coop.map_prototype_start. It follows
   the host camera by default. WASD/arrows, wheel or right-mouse camera movement
   temporarily detaches it; Return to host camera resumes following.
6. Players can inspect native hover tooltips and Alt expansion independently.
   Clicking the map does not move the host's party or open the old custom card.
   Campaign menus and dialogues follow the host; players do not vote, trade,
   transfer prisoners/loot or edit heroes through this preview.

Replica, automatic map/battle/map transitions and siege-projectile capture are
ENABLED BY DEFAULT in the compiled mod. No special campaign launcher is needed.
Diagnostics and test automation remain off. Existing explicit environment values
of COOPSPECTATOR_CAMPAIGN_MAP_PROTOTYPE=0,
COOPSPECTATOR_AUTOMATIC_CAMPAIGN_MISSIONS=0, or
COOPSPECTATOR_SIEGE_FLIGHT_OBSERVER=0 intentionally disable the corresponding
feature; remove old overrides from your own launch setup if present.
Standalone servers started outside the campaign are not adopted automatically.

## Test checklist

- Map: compare visible parties/settlements, movement/heading, camera follow and
  detach/return, normal/Alt tooltips and the English camera-control labels.
- Settlement/raid/siege: compare top panels, progress, markers, engine animations
  and projectile flights. Replica clients observe the host's choices.
- Dialogue: compare text/options and character appearance/motion. Answer choices
  remain controlled by the host; voting is not included.
- Send troops: compare changing auto-battle counts, result and rewards. Check
  plain column hints locally. Client controls must not advance the simulation.
- Manual battle: start a small field encounter in the campaign. In the HOST
  MULTIPLAYER window select the commander/host role and finish deployment, then
  unpause the pre-battle hold. The H shortcut advances the applicable stage;
  check the stage after each press. Pressing H in campaign does not start the
  multiplayer armies. Other players select their own troops in multiplayer.
- Finish the multiplayer battle and let the campaign apply its results. The
  server should return automatically to the replica, including prisoners and
  loot where applicable. Players inspect independently and see host changes.
- Repeat a second battle without restarting. Then test village, siege assault,
  citadel (selected cohort only), siege ambush withdrawal and save reload while
  server/clients remain connected. Report exactly which scenario failed.

Local acceptance does not establish two-PC/network acceptance. This archive is
provided for that next test. Not all combinations, packet delays, reconnection
cases or dynamic reward/ship-tooltip details have been validated. Do not use
irreplaceable campaign saves for the first tests.

## If something fails

Do not continue fighting a separate campaign copy while multiplayer is waiting.
Record the time, map/encounter, visible phase and actions on each PC. Retain logs
from the same attempt: HOST campaign + dedicated server + HOST multiplayer, and
REMOTE multiplayer. Include rgl_log, rgl_log_errors and watchdog logs and a
screenshot of the symptom. Include the preview name/hash; v0.3.2 alone is ambiguous.
Do not send authentication tokens, server passwords or private configuration files.

Rollback: close all game/server processes, move this preview's module folders
aside, and restore the backed-up module folders and launcher. This archive has no
automatic installer and changes no machine-wide settings.
