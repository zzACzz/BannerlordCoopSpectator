# Warhost campaign-map replica — development testing instructions

Source update2026-10-31 (b34; two-PC acceptance still pending):
a newly built candidate prepares server SandBox/SandBoxCore directory links to
the HOST's running game before starting the dedicated process. No manual resource
copies are needed for that candidate.
Existing folders are retained outside Modules under CoopSpectator.SharedModuleBackups
with RESTORE.txt. Close the server before migration; keep the game drive available
while it runs. Write access to the dedicated installation is required. An explicit
BANNERLORD_DEDICATED_SERVER_PATH may select a server in another location; normal
discovery also checks configured Steam libraries. Base and official War Sails
campaign resources are supported by explicit matching content profiles. Unrelated
overhaul resources are not admitted. The previously delivered b299 archive is
unchanged and does not contain these new source features; check MANIFEST.json.
Do not treat an old archive or installed b9 as a stable War Sails candidate.

Latest local evidence: b34 preserves the original failed-visual/portrait fixes and
Training Field model. Canonical Naval graphics admission, guarded water setup and
paired material cleanup now keep water correct initially, after VillageBattle
empire_village_002 through prisoners/loot and after connected save reload. The
user also confirms hero appearance, Training Field and background movement. The
same host/server/session and new reload epoch/acknowledgement were verified.
Measured map ownership738.428s spans three scene lifetimes and includes idle/
diagnostic time; it is not a continuous10-minute movement test. This is bounded
one-PC evidence, not all native battle scenarios or remote acceptance. Earlier
Naval field/base night-hideout acceptance was on b26. Identify the current archive
by MANIFEST.json hashes; v0.3.2 labels alone do not identify it.

Additional local base-game evidence (2026-10-31): b34 completed the campaign siege
on empire_town_d with2622 healthy roster troops and2000 initial foot agents
(1500 attacker/500 defender). Commander deployment, active combat, subsequent
native agent creation, final victory/writeback and connected map return passed.
Only one multiplayer peer was connected, on the attacker side. This does not
establish two-PC acceptance or resolve the tester's older b299 freeze on
khuzait_city_b while selecting/controlling defenders.

For War Sails, BOTH game installations must contain the same official DLC version.
On each game PC, the launcher selects the official canonical NavalDLC graphics
plus a small XML-only CoopSpectatorNavalCatalog generated from that installation.
The four known Naval campaign gameplay plugins remain inactive in multiplayer;
official files are not edited. Use this archive's BAT/profile helper rather than
an older alias-only launch list. The dedicated host still prepares a separate
resource-only Naval adapter with machine-local resource links; it must not scan
the raw official Naval module. No heavy resources or directory links are archived.
For either content profile, the helper also projects the exact official Training
Field prefab from local StoryMode metadata into a small map-resource module.
It preserves the authored model/rotation and does not enable StoryMode gameplay.
Existing verified adapters are reused; updated adapters are retained outside
Modules before replacement. Modified or foreign folders are refused and preserved.

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
   load a TEST save with the intended base/War Sails content. Enable official
   NavalDLC in the campaign for a War Sails test. Dismiss the shader notice if shown. Choose No if asked to
   enter Safe Mode, so the mod remains enabled.
2. Start the dedicated server using the campaign's Coop Dedicated Server menu.
   Leave the campaign open. Once the server is ready and the host is on the
   global map, the server automatically starts the map-replica mission.
3. Start modded multiplayer using the supplied BAT on BOTH PCs. Steam must be
   running normally under the same Windows user as the game. The same-PC host
   launcher can detect its running campaign profile. On the REMOTE PC, choose
   the SAME profile when prompted:1=Base,2=War Sails. The BAT also accepts
   --base or --naval. A content-profile mismatch is a failed setup; correct it
   before testing battles. Never suppress the mismatch or mix individual files.
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
- Keep each replica in the background while the host moves; the movement must
  remain visible. Switch windows repeatedly, recruit and leave settlements,
  and observe at least10 minutes of actual map use. Record the elapsed time.
- Compare the Training Field's full3D model and water with the campaign. A label
  alone does not establish model rendering. Check the model again after returning
  from battle and after save reload. Hidden settlements must remain undisclosed.
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
  On BOTH clients, inspect prisoners and equipment, then loot including shields
  and modified items; check appearance, counts and tooltips. Complete the host's
  screens and verify camera movement/detach/return after the map comes back.
- Repeat a second battle without restarting. Then test village, siege assault,
  citadel (selected cohort only), siege ambush withdrawal and save reload while
  server/clients remain connected. Report exactly which scenario failed.
- For the reported siege freeze, use khuzait_city_b if available, approximately
  1680 attackers/750 defenders and a2000 concurrent-troop limit, with both
  multiplayer clients connected. Exercise defender selection and deployment;
  record each PC's phase and time. A freeze after all2000 agents are ready is a
  distinct boundary from scene load. If frozen, retain the process long enough
  to capture a full hang dump before closing it. Do not treat this preview as a
  proven fix for that report.

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
