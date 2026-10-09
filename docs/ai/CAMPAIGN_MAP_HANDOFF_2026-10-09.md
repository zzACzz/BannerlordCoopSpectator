# Campaign map checkpoint and next-model handoff — 2026-10-09

## Read this first

This is a development checkpoint, NOT a release or full Goal acceptance. The user
requested documentation and publication after a shutdown/move. Do not assume any
old process, window, session, port, installed binary or temporary artifact survived.
No new native test was performed for this documentation checkpoint.

Read AGENTS.md, README.md and (when an unfinished Goal is active) GOAL_MODE.md,
then [the test runbook](CAMPAIGN_MAP_TEST_RUNBOOK.md). Resume the companion-dialogue
gate in [MISSION_DIALOGUE](CAMPAIGN_MAP_MISSION_DIALOGUE.md), not an unrelated feature.
Earlier chronological notes saying "current", "pending" or "running" describe
their recorded revision; this checkpoint summarizes the latest retained evidence.

## Agreed product boundary

- Players are members of the host party; the host owns campaign decisions.
- Replica map shows only what the host can see, including motion, settlement/party
  presentation, markers and native-style tooltips. Target is visual 1:1, not a
  simplified strategic map.
- Follow host camera by default; manual inspection detaches locally, explicit
  return restores host camera.
- Map mission starts automatically while the host is on the global map; a battle
  replaces it and the map returns after authoritative campaign aftermath.
- Dialogue is synchronized read-only 1:1, including presentation, without voting.
- Trading and hero progression are OUT of this increment. Future inventory is
  shared, with item reservation; editing a hero takes an exclusive lock. Do not
  introduce separate player inventories or implement those future systems now.

## Changes since checkpoint c85c586

| Area | Implementation / canonical document | Evidence boundary |
|---|---|---|
| Picking | Non-main raft exclusion in local picking, tooltip admission and host capture; RESUME_2026-09-26 | Source/contracts; native raft case not accepted |
| Hover | Host frames/diplomatic colors and owned hover decals; HOVER_DECALS | cmp-b43 bounded town/village/party comparison |
| Movement | Detached host target recipe, transport and owned target decals; MOVEMENT_TARGETS | cmp-b48 bounded point/settlement and field battle return |
| Relations | Host diplomacy to settlement-nameplate relation; SETTLEMENT_RELATION | cmp-b50 bounded hostile settlement comparison |
| Menus | Escape dispatch, native Options view, dialogue deferred under local modals; ESCAPE_INPUT | Later cmp-b62 Options/dialogue open/update/close and recovery passed |
| Lifetime | Map-only stock match expiry guard; LOBBY_LIFETIME | cmp-b57 original mission survived 1839.988 seconds, rejoin and field return |
| Dialogue HUD | Host-prepared portraits, power/counts, banner, quest markers; CONVERSATION_HUD | cmp-b62 both portraits, read-only dialogue, modals, rejoin and village return |
| Shared resources | Narrow dragon_banner mirror item supplement on client and dedicated | cmp-b62 portrait resource correction; keep catalogs identical |
| Settlement markers | Host-native ordered events, inspection visibility, incremental transport/removal; EVENT_DISCLOSURE | cmp-b65 paired initial markers and Ethemisa reveal/order |
| Companion mission | Exact publication lease, native mission VM text/HUD, bounded scene/camera/actors and framed motion; MISSION_DIALOGUE | cmp-b73 both builds, cmp-c78 all 24 projects; NOT deployed/native-verified |

Names above refer to `CAMPAIGN_MAP_*.md` in this directory. CODE_MAP links the
implementation types and tests. Source, transport, UI, tests and documents are
preserved together because these accumulated changes share versioned contracts.
No new gameplay fix was introduced during checkpoint documentation.

## Latest validation and installation boundary

- Fresh checkpoint validation on 2026-10-09: `cmp-b74` PASSED both isolated builds
  with installed inventories unchanged; `cmp-c79` PASSED all 24 contract projects.
  No product process was launched and no new game behavior was tested.
- `cmp-b73`: client and dedicated isolated compilation PASSED; installed module
  inventories unchanged. `cmp-c78`: all 24 contract projects PASSED. Completion
  was observed directly from the retained runner sessions, not inferred from launch.
- Source: protocol 33 / bridge 28 / catalog 8 / tableau 2 / pose 2. Stage coherent
  roles; old and new binaries are not interchangeable.
- Last recorded installed/native candidate: cmp-b65, protocol 32 / bridge 27 /
  catalog 8. This is historical installation evidence, NOT a current machine probe.
- Last native run `map-markers-r1-20260929` is CLOSED: seven configs matched,
  exact-owned processes were gone, no new configs remained, seven verified
  candidate files retained. Test campaign progress was allowed to remain.
- That run FAILED companion Party -> Iara the Healer -> Talk replication: host
  displayed the mission dialogue, MP stayed on the map. Camera return passed.
  The new source addresses this missing path but its native acceptance is open.

Local-only evidence roots: `artifacts/runtime/map-markers-r1-20260929` and
`%TEMP%/CoopSpectator/Automation/{cmp-b73,cmp-c78,cmp-b74,cmp-c79}`. These are not distributed in
Git. Missing evidence on another machine requires a fresh run, not invented passes.
Do not publish raw logs, bridge contents, account identifiers, dumps, native DLLs,
decompiled game code or configuration/save backups.

## Next work, in order

1. Re-inventory source, tools, installed game/dependencies and processes after the
   reboot; inspect differences before adopting any runtime state.
2. Build/test fresh if source or dependencies differ, then safely stage a coherent
   candidate with module/config preimages and exact ownership records.
3. Native-test Party -> companion -> Talk: paired camera, scene, visible actors,
   animation, text/options/HUD; update and close; repeat. Text alone is not a pass.
4. Test ordinary map dialogue and modals, map input/camera/tooltips, reconnect,
   late join and map -> battle -> map; audit affected adjacent battle types.
5. Resolve actual visual failures from exact installed-code/log evidence. Current
   3D bounds reject visible mounts, mounted humans, children and >3 visible actors
   (also >32 active actors). Full lighting, facial/voice, first-person visibility,
   terrain readiness and native packet sizing remain unaccepted.

## Completion audit

Documentation impact: added this dated handoff and the reusable test runbook;
updated README/BUILD_TEST_DEBUG routing and ARCHITECTURE/CODE_MAP/RUNTIME_FLOWS/
INVARIANTS_AND_RISKS to remove stale claims that mission 3D is unwired. Updated
MISSION_DIALOGUE with final retained validation. Historical feature reports keep
their original native evidence dates; no new runtime acceptance was invented.

Documentation/handoff: captured with source and historical evidence separation.
Overall Goal: PARTIALLY SATISFIED. Broad 1:1 and multi-peer/adjacent-scenario
acceptance remain open; never convert isolated builds/contracts into a native pass.
This checkpoint does not authorize replacing the active Goal or declaring it done.
