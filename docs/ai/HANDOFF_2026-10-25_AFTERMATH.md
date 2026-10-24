# Aftermath checkpoint and manual test handoff — 2026-10-25

## Current decision

The user requested a Git checkpoint and push, then will test all scenarios manually
on one machine. Prepare the two-machine test archive only after those results are
satisfactory. Do not restart their games, replace the installed build or continue
feature changes while that manual sweep is in progress unless asked.

This is an unfinished development checkpoint, not a release or full Goal acceptance.
The broad goal remains a visually faithful, host-disclosed campaign-map replica,
including dialogues, map menus and transitions. Current implementation details and
investigation history are in [the canonical aftermath document](CAMPAIGN_MAP_AFTERMATH_SCREENS.md).

## Installed baseline

- Candidate: b274; protocol65, host bridge60, tooltip5, catalog10, aftermath CAF2.
- Campaign and multiplayer client SHA256:
  `5735E8602B1C1BFC51B2B76759574B80C9184B8C43D24F7018678EEE0D21124B`.
- Dedicated DLL SHA256 (both client/server binary directories):
  `88324B207D9D8DCFC8F092A2C51E0B8581C2EC8F1D445A35DF98EF2E2A467101`.
- Client loaded MVID: `9171b656-4d62-4383-aebb-836633bfdfec`.
- Dedicated loaded MVID: `98121c47-9b7c-4202-8a67-d5e03af45713`.
- r57 staged nine coherent files: three DLL destinations, replica-info/menu/
  prisoners/loot prefabs, and the item-mirror XML in both module roots.
- No archive was created for this checkpoint. Module version remains0.3.2; identify
  this development build by hashes/protocol, not the unchanged display version.

## What changed

The host captures prepared bindings from native Loot-mode PartyVM/SPInventoryVM.
A bounded typed tree travels with the atomic menu snapshot; the client owns a
detached read-only binding model and campaign-layout prisoner/loot screens. Final
applied-result gates allow returning to the map-backed mission while the host is
still choosing prisoners or loot. Intermediate siege/hideout stages keep their
existing follow-up gates. The retained map anchor receives a fresh publication
timestamp after battle without replacing world data or identity.

Each client independently selects a loot item, hero and outfit. Exact window/row/
hero targets reuse the leased tooltip channel; host authority admits only current
disclosed objects. Owned host projections produce full item cards and hero/12-slot
visuals without calling live selection setters or transfer/equip/confirm commands.
Per-hero equipment versions preserve the mannequin across focus-only publications
while rejecting stale equipment replies. Current row reconciliation uses opaque
reference identities, so removal/reordering cannot retarget an old hover by index.

Three StoryMode Dragon Banner fragment aliases were appended to both item catalogs.
Multiplayer initializes the exact original zero-valued banner effect, only if absent,
after native defaults and before item XML. The first fragment uses the original
thumbnail pose selection locally while retaining its registered alias/cache identity.
Campaign initialization and existing effects are preserved.

## Verification and its limits

- Isolated build run `aftermath-story-banner-b274-20261025`: client and dedicated
  exit0; installed inventories unchanged during compilation.
- Canonical run `aftermath-story-banner-c179-20261025`: all26 contract projects pass.
- Installed-library detached binding and loot-session checks pass. These do not
  create a game and do not establish native rendering by themselves.
- r56: user reports the hover blink fixed. Bounded agent captures retain model,
  slots and equipped-item card; no continuous video was recorded.
- r57: user and agent see the first Dragon Banner fragment thumbnail. Its image
  orientation and full item-card text match the campaign. Local Iara remains
  independent of host Halgard; subsequent helmet inspection renders armor54 and
  weight3.23 with model/slots still visible.
- r57 is CLOSED: fresh inventory empty, all seven configuration hashes restored,
  Steam and saves preserved. Manual MP shutdown exitFFFFFFFF is not crash evidence.

Retained local manifests/logs are under `artifacts/runtime/map-aftermath-r57-20261025`
and `%TEMP%/CoopSpectator/Automation/<run-id>`. Raw runtime logs/builds are deliberately
not committed. Small native comparison screenshots accompany this checkpoint:
[MP thumbnail](evidence/aftermath-b274/banner-visible-b274.png),
[MP card](evidence/aftermath-b274/banner-card-b274.png),
[campaign card](evidence/aftermath-b274/campaign-banner-card-b274.png).

## Known incomplete work

- Independent prisoner/troop selection is UNWIRED. A strict troop-target foundation
  and owned preview helper exist but are not network/UI admitted. Shared prisoner
  lists and host changes were observed; do not claim independent prisoner inspection.
- Alternate weapon/comparison cycling, independent sorting/filter/search controls
  and some scroll behavior still need implementation/acceptance.
- The second and third banner fragments have catalog/pose contracts, but no native
  visual acceptance from the first-fragment test.
- MP-only dark/blue terrain remains unfixed. Original silent exits after loot have
  no established root cause; stable recent tests do not prove those exits fixed.
- A stale `Plains Battle` hint was observed earlier. Broad modal/reconnect/repeat
  acceptance and the complete battle-type matrix remain open.
- `CoopCampaignLootTooltip.xml` in the local worktree is an unused abandoned
  host-hover prototype. It is excluded from this checkpoint and deployment inputs.

## Launch the single-machine manual test

1. Keep the coherent installed b274 modules. Start the campaign with map prototype
   and automatic campaign missions enabled using the documented test launcher.
   For agent reruns, create a fresh runtime directory with config preimages and
   exact process ownership; never reuse a closed launch script.
2. Accept the known shader warning. If Safe Mode is offered after a crash, choose No.
3. Load the chosen test save and start the dedicated server from the campaign menu.
4. Launch the separate multiplayer client through the user's desktop shortcut
   `run_mp_with_mod_from_game_root.bat - Shortcut`. Agent shell launches repeatedly
   failed Steam detection while the user's manual launch worked; cause is unproven.
5. Complete Login manually and connect that multiplayer client to AC_COOP as host.
   The campaign process is not itself the multiplayer host peer. Every remote tester
   also needs its own multiplayer client; never create a duplicate local client.
6. With automatic missions enabled, the map should open without a console command.
   If it does not, record campaign/server logs and feature flags before using
   `coop.map_prototype_start` in the campaign as the diagnostic fallback. Do not
   mistake a manually started campaign lacking test flags for an automatic-start pass.
7. The host multiplayer peer must complete deployment and pre-battle pause. Where
   required, H first deploys the other army and H again starts the battle. A remote
   client waiting for the absent host peer is not evidence of a sync defect.

### Scenario matrix

Record battle type, save, actual battle versus Send troops, expected screen,
campaign/server/client log IDs and the first divergence or crash time.

| Scenario | Observe |
| --- | --- |
| Field battle and Send troops | Final result -> prisoners -> loot -> map; repeat a second battle without reconnecting. |
| Village battle | Same final sequence and result application; no infinite Loading data. |
| Hideout | Correct intermediate/final transitions; no premature map before the final stage. |
| Siege | Correct preparation/machines/shots; final battle -> aftermath -> map; no premature intermediate return. |
| Prisoners | All four lists/counts and host transfers/removals match; remote actions cannot transfer or confirm. Independent selection is a known open feature. |
| Loot | Independent hero/name/model/12 slots/outfit; item and equipped-slot cards, food/trade rumors, weight/armor; stable model across repeated hover. |
| Shared loot changes | Host transfers/equips update the client; removed hover targets disappear without retargeting another list row. |
| Menus and reconnect | Open/close settings during each screen; host advances while client is in settings; disconnect/rejoin while prisoners or loot remain open. |
| Return to map | Camera follow/detach/return, native tooltips/Alt labels, movement/facing, battle/raid animations and siege presentation still work. |
| Failure cases | Record terrain discoloration, stale hints, missing visuals, unexpected exit and whether it followed item hover. Preserve logs before rerunning. |

Close the three game roles after the test to avoid idle laptop load. Preserve Steam,
saves and exact config restoration evidence. Do not publish a two-machine archive
until the user confirms these results and asks to proceed.

## Useful implementation entry points

- `Campaign/CoopCampaignAftermathCapture.cs`: native Loot state admission, prepared
  binding capture, row identity and per-hero visual version.
- `Campaign/CoopCampaignMapTooltipCapture.Aftermath.cs`: owned loot cards/visuals.
- `Infrastructure/CoopCampaignAftermathPresentation.cs`: canonical targets, bounds,
  typed codec and row registry; troop target is foundation only.
- `UI/CoopCampaignAftermathVM.cs`, `UI/CoopCampaignLootInspection.cs`: detached local
  selection, resource resolution and lifetime; no campaign mutation targets.
- `UI/CoopCampaignMapPrototypeMissionView.Aftermath.cs`: fullscreen layer ownership.
- `Infrastructure/CoopStoryBannerResources.cs`, `Patches/CoopMirrorItemPosePatch.cs`:
  fragment effect initialization and local thumbnail pose normalization.

For future source changes, use isolated builds:
`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Invoke-CoopTest.ps1 -Command CompileOnly -RunId <fresh-id>`.
Canonical contracts: same entry point with `-Command Contracts -All`.
Detached checks use Windows PowerShell5 and `-CandidateDirectory <isolated-client-bin>`
with `scripts/Test-AftermathBindings.ps1` and `scripts/Test-LootInspectionSession.ps1`.
