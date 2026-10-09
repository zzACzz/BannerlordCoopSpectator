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
