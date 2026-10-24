# Campaign map replica: agent test runbook

## Scope and prerequisites

Read [current handoff](CAMPAIGN_MAP_HANDOFF_2026-10-09.md),
[build safety](BUILD_TEST_DEBUG.md), [automatic lifecycle](CAMPAIGN_MAP_AUTOMATIC_LIFECYCLE.md)
and the focused feature document before running. This is the user's real campaign
menu -> dedicated -> modded MP workflow, not a substitute connection-only test.
Read the currently available Computer Use skill completely before UI interaction.
Inspect actual capabilities; a browser-only tool cannot control Windows games.
Never bypass a safety stop, integrity boundary or authentication restriction.

## Fresh-run preparation

### Current user test — 2026-10-25

Use [the b274 handoff and single-machine matrix](HANDOFF_2026-10-25_AFTERMATH.md).
User is conducting the next manual sweep; wait for results before producing a
two-machine archive. The older candidate procedures below are historical and do
not describe the current installed version or accepted loot inspection scope.

### Independent loot inspection candidate (2026-10-24)

Current b254 is protocol63/bridge58/tooltip4/CAF2; installed b240 is older and
incompatible. Fresh c172 passed all26 contracts; b254 both isolated builds and
installed-library base-command dispatch checks pass. Stage coherent roles and the
current CoopCampaignLoot/CoopCampaignPrisoners prefabs with exact preimages. The
unused CoopCampaignLootTooltip host-hover prefab is NOT a candidate dependency.

After a final battle, leave loot open. Hover different items in MP while keeping
host hover elsewhere or absent: local card must name the local item and preserve
all property rows/modifier colors, not the host hover. Check food/trade rumors,
armor and multi-use weapons. Host transfers must update counts and revoke a removed
hover target without retargeting it to the replacement list index. Verify hover
exit, reentry, modal suspension, screen close/reopen and return to map. Selecting
a hero/mode is local; independent mannequin/slot display and comparison cycling
remain open implementation work, so do not accept them from partial card behavior.
Prisoner independent inspection and the MP-only dark-ground report remain open.
Do not treat these candidate checks as full Goal acceptance.

### Aftermath candidate (2026-10-23, native test pending)

Use coherent protocol61/bridge56 modules and additionally stage the new
CoopCampaignPrisoners.xml and CoopCampaignLoot.xml prefabs, preserving prior absence
or exact bytes. Source/binding contracts are not visual acceptance. After a final
field battle, leave host prisoner selection open and compare all four lists and
host changes; proceed to loot, compare items/counts/equipment and close to map.
Remote clicks/hotkeys must not transfer/confirm host resources. Repeat with local
settings, reconnect and final village/hideout/siege outcomes; verify no map return
for intermediate stages. See CAMPAIGN_MAP_AFTERMATH_SCREENS.md for open parity gates.

### Party animation candidate acceptance (2026-10-23, not yet run)

Current source b184 uses protocol50/bridge45/catalog9 and CMP2 animation tuples.
Installed b173 remains unchanged. Use a fresh coherent three-role installation
with configuration preimages and loaded hashes before accepting any visual result.
Do not mix old roles with this candidate. Source tests/builds are not native proof.

- Compare paired host/MP moving, stationary, raiding and fighting figures; include
  the host and another disclosed party, mounted/unmounted and caravan if available.
- Compare action choice, weapon/mount synchrony and continuity over several loops.
  Do not accept a screenshot alone as proof that motions are smooth.
- Pause, resume and fast-forward in the campaign, including stoppable waiting.
  Verify pose progression follows the host clock rather than MP wall-clock time.
- End/cancel a raid and resume travel. No attack loop may remain on an idle/travel
  figure; test host dialogue open/close, settings and map->battle->map similarly.
- Hide/reveal a party through host disclosure, change appearance when feasible,
  and reconnect: no obsolete tuple, resource reuse or undisclosed model motion.
- Check frame time and send volume with increasing disclosed party counts. The
  current64-sample rotating publication budget has an unresolved dense-party
  refresh limit; normal small-count success does not close that requirement.
- Siege participants may be tents rather than figures in native campaign. This
  renderer does not implement those tents or complete siege UI/engine presentation;
  record that as a remaining feature, not a passing animation fallback.

If native action validation fails, preserve the log/dump and current source identity;
do not replace the host action with an invented generic attack to make the test pass.

### Bottom-bar candidate acceptance (2026-10-22, not yet run)

Candidate b160 uses protocol46/bridge41/catalog9 and stock MapBar with detached
bindings plus statistic/gold hint transport. Do not mix with installed b150.
Both isolated builds passed and c141 passed all26 contract projects; native
provider execution and visual parity are still unverified. Use fresh-run backup/hash
and exact-process ownership procedure below; compilation is not visual acceptance.

- Compare full paired screens: date/time image, pause label and selected speed,
  ordered navigation icons/alerts, primary/expanded statistics and army button.
- Change campaign time and visible statistics, then pause. Verify updates reach
  the replica without resetting local expansion or flickering the menu/raid bar.
- Click replica time/navigation/army controls: no campaign mutation or unwanted
  local campaign screens. The central camera control must return only the local
  replica camera to the host. Test world input immediately above/outside the bar.
- Check camera/help overlays and the native bar at both normal and smaller window
  sizes. Confirm no overlap with raid/encounter, pause text and item tooltip.
- Open/close local settings, a host dialogue, raid and encounter; perform battle
  return and rejoin. No stale bar after host disappearance or old mission teardown.
- Check siege preparation separately; stock bottom bar is not siege UI coverage.
- Compare host/replica normal and Alt-expanded gold breakdowns: title, category,
  signed/formatted amounts, row order, total and extension text. Ensure hovering
  never changes campaign gold. A rejected custom finance model is not a parity pass.
- Compare influence, health, troops, food, morale, view distance, wages and speed
  hints. Test moving between icons, value changes, expansion, mouse leave and local
  settings; old targets must not retain rows or reopen over another tooltip.
- Compare hardwood/material thumbnails in raid loot. Confirm the mirror-only
  native pose patch was admitted; build/substitute tests do not prove native IL
  admission or correct framing. Preserve ordinary campaign rendering.
- Simple/navigation/time hints remain unimplemented; explicitly record that gap.
  Statistics/gold hints are source candidates, not full visual acceptance.

### Pending map-event acceptance (2026-10-17)

The user is manually testing; do not deploy or interrupt their processes without
fresh coordination. b131 VillageFix is an immutable two-PC candidate; b132/b133
source changes are not in that archive. Record loaded hashes, not only 0.3.2.

For the next coordinated map-event run, retain paired host/replica evidence for:

- A disclosed non-settlement battle icon, host-follow camera, manual camera pan
  and return; the icon must stay anchored to the event, with native zoom cutoff.
- Loss of host disclosure and battle completion: no persistent or resurrected
  old icon. Moving the client camera must not reveal undisclosed events.
- A village raid and siege/sally/rebellion settlement marker when obtainable;
  compare the host's actual nameplate, not inferred campaign conditions. Mark
  unavailable event types untested, not passed. Avoid duplicate world icons.
- Dialogue open/close, map -> battle -> map and disconnect/rejoin; verify fresh
  marker data, usable camera and no retained markers from the prior mission.
- Many changing visible events and multiple peers: record server frame time and
  transfer latency. b133 sends at most 33 chunks / 8,204 payload bytes per changed
  list per peer; aggregate bandwidth and native delivery are not test-verified.

Pure codec/source tests and successful compilation cannot satisfy these visual,
disclosure or network-runtime gates. Preserve the no-mutation manual-test boundary.

Important manual-test caveat (2026-10-13): installing candidate DLLs does not
persist the automatic lifecycle gate. A campaign launched by an agent with
process-local flags can work while the same DLLs launched normally remain in
manual mode. Both map and automatic flags must be present when the campaign
starts; the menu-owned server inherits them and its new session identity.
`coop.map_prototype_start` starts only a manual map, not the automatic lifecycle.
Never present this command as enabling map/battle/map transitions. A user-led
test must distinguish those modes before interpreting a missing battle switch.

1. Inspect Git branch/status and installed game versions. Preserve other changes.
   Use a fresh, short run ID; never reuse a closed run or old PID after reboot.
2. Inventory current Steam/game/server/helpers and ports. Record baseline identities
   using PID + executable path + start time, plus ancestry for new test helpers.
   Existing user processes are not automatically test-owned.
3. Validate without deployment (replace example IDs if already present):

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-CoopTest.ps1 -Command Contracts -RunId cmp-next-c1 -All
   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Invoke-CoopTest.ps1 -Command CompileOnly -RunId cmp-next-b1
   ```

   Wait for terminal results. Continue observing the same runner after a tool
   timeout; do not start duplicate builds. Ordinary project builds deploy by
   default. CompileOnly does not install or launch Bannerlord. Request permitted
   filesystem access if NuGet.Config is denied; do not weaken security settings.
4. Before staging, retain byte/hash preimages for every changed module/config
   and list previously absent paths. Acquire installation locks. Verify full
   candidate consistency, not just one DLL. Last run staged seven files: client
   DLL/PDB, dedicated DLL in both client/server bin directories, replica-info
   prefab and both mirror-item catalogs. Recompute this list for current source.
   The menu/raid candidate additionally requires
   `GUI/Prefabs/CoopCampaignMenu.xml` from the client module. Preserve its preimage
   or prior absence and stage it with the matching DLL; otherwise menu loading fails.
5. Preserve all configuration preimages (including dedicated test config). The
   user permits progress in the test campaign to remain; unrelated saves remain
   protected. Keep diagnostics default-off unless a bounded explicit probe needs them.
6. Review `COOPSPECTATOR_CAMPAIGN_MAP_PROTOTYPE=1` and the separate
   `COOPSPECTATOR_AUTOMATIC_CAMPAIGN_MISSIONS=1` gate and ensure required roles
   receive the intended run-scoped configuration. Do not permanently change
   global environment or silently mix manual and automatic lifecycle modes.

Local historical Prepare-LiveRun/Record-Process/Verify-LoadedIdentity/cleanup
helpers under `artifacts/runtime/map-markers-r1-20260929` show prior implementation,
but pin OLD run IDs and binaries. They are NOT reusable launch commands and are
not present in a clean clone. Audit/adapt into a fresh scoped transaction; do not
execute an old script unchanged or guess unavailable helper commands.

## Required launch sequence

1. Confirm Steam is running and usable. Preserve the user's session; request fresh
   approval before restarting user-owned Steam if necessary. Never handle credentials.
2. Launch the modded campaign, acknowledge the known startup shader warning if
   present, choose **No** for Safe Mode, and select **Continue**. Test-save choice
   is not critical. New/repeated rendering errors during a mission are failures,
   not warnings to dismiss indefinitely.
3. In the campaign pause menu use **Coop Dedicated Server** and its **Start Server**
   button. Prove the new dedicated process belongs to this campaign launch.
4. Start MP using the user's desktop shortcut **run_mp_with_mod_from_game_root.bat - Shortcut**.
   Last known path:
   `C:\Users\Admin\OneDrive\Desktop\run_mp_with_mod_from_game_root.bat - Shortcut.lnk`.
   Resolve the current shortcut target/working directory and use it; do not replace
   it with a guessed executable command or an unrelated Steam launch path.
5. In MP acknowledge the known startup warning and select **No** for Safe Mode.
   The user authorizes the password-free in-game **Login** button. Click it when
   current tool/skill rules permit; if they prohibit login interaction, explain
   that specific boundary and ask for the click. Do not request or enter credentials.
6. Join the exact menu-owned **AC_COOP** server through the native server browser
   as the host. Verify actual connection and loaded campaign/client/server binary
   identities against staged hashes/MVIDs, not just window titles or on-disk files.
7. With automatic lifecycle enabled, the map should start without a console command.
   Record this. Do not send manual start commands to hide an automatic-start failure.
   For an explicitly isolated legacy/manual reproduction only: AFTER host join,
   open the CAMPAIGN console (Alt+~), run `coop.map_prototype_start`, retain the
   response, close the console and return to the game. A "start requested" response
   is not proof that the server/client mission loaded.

## Input lessons (verify, never assume)

- Login, H, Alt+~, Escape and some clicks were not reliably injected in prior runs.
  Observe the actual state change after each action; a successful tool call is not
  evidence that Bannerlord received it. Inspect focus, modal state and tool result.
- On-screen keyboard may be tried through supported Computer Use when allowed,
  but prior instances had higher integrity and rejected automation. Do not elevate
  tools or bypass that boundary. Ask for the smallest necessary physical action.
- A physical Escape may first stop automation, then a second press opens the game
  menu. Respect the stop and wait for explicit permission to resume control.
- Native **Ready** may ready only the host army. H was used once to deploy the other
  army and again to release pre-battle pause. Wait for each corresponding state;
  never infer battle start from injected key events alone.
- If the user is testing manually, do not compete for control. Record their report
  separately from independently observed screenshots/logs.

## Evidence and acceptance checklist

Party-action release regression (r32 follow-up): test encounter -> Surrender ->
captivity/travel separately from an actual battlefield mission. Also test leaving
an encounter without fighting, completed field/village battles, stopped/completed
raids and applicable sally/siege-outside events. Compare host and replica before
and after the transition, for foot and mounted actors. act_none is a channel
release sentinel, not a missing animation clip; no attack loop may survive it.
Repeat during pause/resume and after hide/reveal or pose expiry. Codec tests and
successful compilation do not verify native channel release or smooth movement.

For every stage record candidate/run identity, timestamp, expected/actual state,
pass/fail/not-run and user assistance. Keep paired host/MP screenshots locally.

- Campaign, menu-owned dedicated, prescribed MP launch, Safe Mode choice, host join.
- Automatic map, host-visible entities/markers/order and no hidden-information leak.
- Movement/daylight, settlement entry/exit, normal and Alt-expanded tooltips without
  flicker, hover/target indicators, diplomatic colors.
- Host-camera follow, manual detach, explicit return, restoration after dialogue/battle.
- Ordinary map dialogue AND Party -> companion -> Talk (different native paths):
  paired scene/actors/camera/HUD/text/options, smooth motion, read-only answers,
  update/close/repeat. Missing 3D or text-only fallback fails 1:1 acceptance.
- MP Options/menu during host dialogue open/update/close; recovery of input afterward.
- Native Quit/rejoin, late join/multiple peers as available; mark unavailable cases.
- Map -> battle -> authoritative result/aftermath -> automatic map, with post-return
  camera/tooltips/menu checks. A prior field/village pass does not prove every siege,
  hideout or naval path. Map longevity beyond the old ~1800-second expiry when relevant.
- On crash retain exact role logs/dump provenance, stop blind retries, inspect the
  lowest useful native/managed boundary and audit other battle types before fixing.

## Transition timing evidence

For automatic map/battle transitions, capture separate timestamps for:

1. Server EndMap dispatch and StartBattle dispatch.
2. Server result publication, campaign consumed-result audit, and EndBattle dispatch.
3. Actual host return to global-map state, StartMap dispatch, and client render readiness.
4. A real post-return camera/tooltip/menu interaction.

Do not treat dispatch as client loading completion. Do not attribute time spent
in campaign aftermath menus to a server timer. A published result is not proof
of campaign application, and a consumed-result audit is not proof of a disk save.
The r15 candidate retains exact-result acknowledgement, actual native teardown
and start readiness; no elapsed timeout authorizes discarding pending results.
Record a missing measurement as unmeasured rather than inferring it from nearby
log entries. Repeat with adjacent battle types; field success alone is insufficient.

When the user takes over, stop UI input and avoid deployment/restart. Passive
log evidence may continue. If recorded processes disappear, reconcile replacement
identities and user activity before doing cleanup; never claim restoration from
process absence alone or restart solely because an observation timed out.

## Cleanup and final report

Stop only proven test-owned identities, preferably gracefully; recheck path/start
before termination to prevent PID-reuse mistakes. Preserve original Steam and
unrelated helpers. Restore inspected run-owned configuration changes from preimages;
do not overwrite concurrent user edits. Remove only proven newly created test files.
Verify restored hashes, newly created paths, ports and exact-owned process absence.
State explicitly whether candidate modules were retained or restored, and preserve
their backups. Never recursively remove the workspace, Documents or game install.

Report source/build/contracts/native/regression evidence separately. Raw logs,
private bridge payloads, account data, dumps, game binaries/decompilation and backup
files stay local; publish only sanitized summaries. If a required stage was manual,
failed or not run, say so. Update the canonical feature document and handoff.
