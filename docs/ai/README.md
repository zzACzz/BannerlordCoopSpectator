# AI Knowledge Base

Start here after reboot/model handoff: [2026-10-09 checkpoint](CAMPAIGN_MAP_HANDOFF_2026-10-09.md)
and [agent test runbook](CAMPAIGN_MAP_TEST_RUNBOOK.md). They summarize the latest
evidence and supersede older chronological "current/running/pending" notes below.
No native run is assumed alive after shutdown. Full Goal acceptance remains open.
Fresh 2026-10-09 checkpoint checks: cmp-b74 both isolated builds and cmp-c79 all
24 contract projects passed, installed inventories unchanged, no product launch.

New mission-dialogue 3D source candidate uses protocol33/bridge28/catalog8 with
tableau2/pose2: bounded land scene, host camera/visible actors/climate and separate
frame/animation updates. Native capture is connected, but NOT deployed or native-
verified; full lighting, mounts/crowds and visual parity remain open. Final cmp-b73
passed both isolated builds; cmp-c78 passed all 24 contract projects. See
[candidate and limits](CAMPAIGN_MAP_MISSION_DIALOGUE.md).

Companion mission dialogue: cmp-b65 reproduced the known Party/Talk gap. Source
now binds an exact standalone land-conversation mission to a successful map
publication and reads its prepared native text/HUD. Failure/close clears the
mission dialogue; no client choice, battle-lifecycle or map-visual reads are added.
The subsequent bounded 3D source candidate is connected but not native-verified,
so this is NOT 1:1 acceptance. See [integration and next gates](CAMPAIGN_MAP_MISSION_DIALOGUE.md).
cmp-b70 passed both isolated builds with installed inventories unchanged;
cmp-c75 passed all24 projects (96 lease/integration checks). Nothing newly
deployed/native-verified; installed cmp-b65 remains.

Ordered settlement markers are now integrated in source (protocol32/bridge27/catalog8):
existing host UI lists, full/dynamic transport, order-only deltas, removal clearing
and ordered client lists. cmp-b65 both isolated builds and cmp-c69 all24 contract
projects pass (126313 order /87 transport assertions); earlier test defects are documented.
Fresh map-markers-r1-20260929 staged cmp-b65 (32/27/8), preserving seven module
and seven configuration preimages. All three loaded identities match; native AC_COOP
join and automatic map entry passed. Paired initial markers and Ethemisa production-
then-issue reveal passed (evidence02-07), as did user-assisted camera return.
Companion mission dialogue remains absent (known gap). Run closed: seven configs
match, no owned/product processes/new configs, installed cmp-b65 retained.
Other native regression gates remain open.
See [integration and native gates](CAMPAIGN_MAP_EVENT_DISCLOSURE.md#ordered-marker-integration-2026-09-29-source-candidate).

Dialogue HUD candidate adds host-prepared upper powers/portraits/party counts,
quest markers and speaker banner (31/26/8). cmp-b60 both isolated builds and
cmp-c61 all24 contract projects passed. Native r2 restores the defender portrait,
but the host remains a silhouette: full parity is NOT accepted. All three HUD runs
are closed; seven configs and five cmp-b57 module preimages restored, no owned
products remain. cmp-b61/r3 confirms rejection of missing dragon_banner; the narrow
static StoryMode supplement passed cmp-b62 both builds/cmp-c63 all24 projects.
Fresh r4 passed bounded gates on cmp-b62 with identical client/dedicated mirror
catalogs and seven module/config preimages. Both portraits now render in the paired
hostile greeting (29/22); read-only answer, text update, dialogue close and map
zoom/camera-return checks passed. Host dialogue open/close under MP Options and
map/input recovery also passed. Native Quit/rejoin and post-rejoin map input passed;
automatic village battle/victory/map return and post-return zoom passed. User-assisted
host-camera return passed. r4 is closed: seven config preimages match, no owned
processes remain, and cmp-b62 modules are retained. Full parity remains open. See
[canonical source, native investigation and verification gates](CAMPAIGN_MAP_CONVERSATION_HUD.md).

cmp-b55 dedicated stability FAILED after rejoin: recovered dump proves the
stock Playing match-timeout path dereferences the map's absent SpawnComponent.
The map-only lifetime candidate passed cmp-b57 both builds/cmp-c58 all 24 contract
projects. Its fresh native run survived 1839.988 mission seconds in the original
mission, with post-threshold dialogue/map/camera/tooltip checks. Native Quit/rejoin
also passed in the same original mission (05:51:49 rejoin, post-rejoin tooltip).
One native field-victory map/battle/map cycle also passed with post-return input.
Controlled host dialogue open/close under Options and map/input recovery also passed.
The run is closed: all 23 recorded identities absent, seven configs match, no new
configs/products; cmp-b57 and permitted test saves retained. Full acceptance is open. See
[dump diagnosis, scoped correction and native gates](CAMPAIGN_MAP_LOBBY_LIFETIME.md).
The prior live/menu progress below is historical and is not stability acceptance.

Settlement event disclosure candidate filters uninspected issue/tournament icons
before publication/display. [Native rules and gates](CAMPAIGN_MAP_EVENT_DISCLOSURE.md).
Not yet native-verified.

cmp-b51 user confirmed physical Escape opens the replica map menu; retention and
wheel isolation passed, but Options closed the menu without opening settings.
Newer source adds the missing native options view, forwards dialogue-layer Escape,
and defers host dialogue reconstruction during local modals, including same-screen
options. cmp-b51 run is closed with actual Quit/rejoin, camera return and main-party
tooltip rendering verified; seven configs restored and owned products absent.
cmp-b55/cmp-c56 passed both builds/all 24 projects, and a fresh options/modal run
is in progress on staged cmp-b55. Full menu/dialogue acceptance remains open. See
[input ownership and evidence](CAMPAIGN_MAP_ESCAPE_INPUT.md).

Settlement-nameplate relation correction is source/build/contract verified:
cmp-b50 both isolated builds, cmp-c51 all 24 projects (164 relation assertions).
Protocol 30 / bridge 25 / catalog 8 transports host-owned diplomacy through dynamic
state and stock Relation binding. cmp-b50 bounded enemy village/castle, dialogue
return and camera checks passed; run closed with configs restored and 24 owned
identities absent. Rejoin failed to start because Escape was unresponsive; full
native acceptance remains pending. See
[evidence, shared-scenario scope and native gates](CAMPAIGN_MAP_SETTLEMENT_RELATION.md).

Movement-target source candidate now connects host capture, state transport,
disclosure/readiness gates and owned replica decals (29/24/8). cmp-b48 passed both
isolated builds; cmp-c49 passed all 24 projects (237 layout/408 transport checks).
Native run map-movement-target-r1-20260928 closed with bounded point/village/fort,
camera/dialogue and automatic field-victory map-return evidence. User confirmed
post-return tooltips. None of 22 owned identities remain; seven configs match,
no new configs/products; cmp-b48 retained with module preimages. Enemy settlement
nameplate backgrounds remain neutral (Relation is hardcoded zero); pursuit-ring
alignment, reconnect and other battle types remain unverified. Full acceptance
is open. See local [run result](../../artifacts/runtime/map-movement-target-r1-20260928/RESULT.md) and
[native rules, tests and integration gates](CAMPAIGN_MAP_MOVEMENT_TARGETS.md).

Hover-ring source candidate now transports host-native frame/diplomatic color
and renders owned stock decals (protocol 28/bridge 23/catalog 8). cmp-b42 both
isolated builds and cmp-c43 all 24 projects passed. Final cmp-b43/cmp-c44 also
passed. cmp-b43 bounded native village/town/party hover, second zoom, camera return
and dialogue/map restoration checks passed. All 24 owned identities are absent,
seven configs match; cmp-b43 retained with recoverable module preimages. Full
native acceptance, battle-return/rejoin and host movement-target indicators remain
open. Dialogue speaker-banner parity also remains open. See
[implementation and required verification](CAMPAIGN_MAP_HOVER_DECALS.md).

Post-checkpoint ordinary picking refinement: non-main raft exclusion now applies
to client hover/pinned info, shared tooltip target admission and live host capture.
State invalidation clears stale hover/payload. cmp-b40 both isolated builds and
cmp-c42 all 24 projects passed; installed cmp-b39 unchanged. Native raft acceptance
NOT RUN. See [refinement](CAMPAIGN_MAP_RESUME_2026-09-26.md#raft-hover-admission-refinement-2026-09-28-source-only).

Published checkpoint overview: [2026-09-28 scope, verification and remaining work](CAMPAIGN_MAP_CHECKPOINT_2026-09-28.md).
Raw artifacts/ evidence is local-only and is intentionally not included in Git.

Native scene picking cmp-b39 passed bounded ordinary-land hover, second zoom,
camera return, read-only dialogue lifecycle and settlement entry/exit checks.
Both builds/all 24 cmp-c41 projects passed. All 22 test-owned identities are absent,
seven configs match, no new configs; cmp-b39 retained with module preimages.
Hover gold ring, army/prisoner/raft semantics and battle-return/rejoin remain open.
See [result and limits](../../artifacts/runtime/map-picking-r1-20260928/RESULT.md).
The cmp-b37 tooltip result below is historical evidence, not cmp-b39 acceptance.

Latest tooltip correction: cmp-b37 / map-tooltip-r2-20260928 received user
confirmation that flicker is gone. Bounded village retention, party switch and
hover-exit clearing passed; 305 requests, zero Denied control messages. Both
builds/all 24 cmp-c39 projects passed. All owned/product processes are absent,
seven configs match, no new configs; cmp-b37 retained. See [result and limits](../../artifacts/runtime/map-tooltip-r2-20260928/RESULT.md).
Battle-return/rejoin, dialogue/camera regression and full parity remain open.
Earlier live/preparation statements below are historical.

Latest tooltip native result: cmp-b36 / map-tooltip-r1-20260928 matched the observed
normal party/village native cards and user-confirmed Alt expansion, but FAILED
stability (continuous flicker). All 22 owned identities are absent, seven configs
match and five cmp-b19 module preimages are restored. See [result](../../artifacts/runtime/map-tooltip-r1-20260928/RESULT.md).
Source follow-up adds terminal-receipt request pacing and non-disruptive native
card refresh; cmp-b37 both builds and all 24 cmp-c39 contracts passed. The fresh
map-tooltip-r2-20260928 run is live with all three loaded identities verified;
MP awaits user Login. Native correction acceptance and cleanup are pending.
Earlier IN PROGRESS notes below are historical.

Tooltip native run map-tooltip-r1-20260928 is now IN PROGRESS on staged cmp-b36,
with five cmp-b19 module preimages and seven config preimages. Both final builds
and all 24 cmp-c36 projects passed. Native acceptance/cleanup are pending; this
supersedes installed-cmp-b19 statements below. See [live preparation](../../artifacts/runtime/map-tooltip-r1-20260928/README.md).

Latest tooltip source connects native peer messages, launcher-owned relay,
host capture and the stock tooltip presenter (27/22/7). cmp-b33/b34 both builds and
cmp-c33/c34 all 24 projects passed. Installed cmp-b19 is unchanged; native tooltip
acceptance NOT RUN. Earlier foundation entries below describe historical
integration boundaries, not current wiring. See [network](CAMPAIGN_MAP_RESUME_2026-09-26.md#tooltip-native-network-candidate-2026-09-28-source-only)
and [stock presenter](CAMPAIGN_MAP_RESUME_2026-09-26.md#tooltip-stock-presenter-candidate-2026-09-28-not-native-verified).

Tooltip local relay now has source launcher ownership hooks and a host tick; its
dedicated endpoint has no native MP producer yet. Final cmp-b31 both builds and
cmp-c31 all 24 projects passed (910 relay assertions). Native tooltip/UI/relay acceptance NOT
RUN, installed cmp-b19 unchanged. See [candidate and remaining work](CAMPAIGN_MAP_RESUME_2026-09-26.md#launcher-owned-tooltip-relay-2026-09-28-source-candidate).

Tooltip continuation adds challenge-bound semantic envelopes and a host-only
published-target lookup entry; still NO live relay or native packet/UI integration.
cmp-b28 both builds/cmp-c28 all 24 projects passed (1974 wire/handshake assertions).
Host-entry cmp-b29 both builds/cmp-c29 all 24 projects passed (549 admission assertions). Installed cmp-b19 is
unchanged. See [candidate and next integration gate](CAMPAIGN_MAP_RESUME_2026-09-26.md#tooltip-semantic-envelopes-and-host-lookup-2026-09-28-unwired).

Tooltip source follow-up now includes bounded language-state restoration, a
writable-template guard, and unwired client-interest/per-peer request policies.
cmp-b25 both builds/cmp-c25 all 24 projects passed; isolated installed-library
restoration passed for all nine processors. Final cmp-b27 both builds/cmp-c27 all
24 projects passed (3612 client-interest/2505 server-gate assertions).
Native capture/relay/stock rendering are NOT integrated or accepted;
installed cmp-b19 is unchanged. See [isolation](CAMPAIGN_MAP_RESUME_2026-09-26.md#tooltip-language-state-isolation-2026-09-28-source-and-managed-probe)
and [request lifetime](CAMPAIGN_MAP_RESUME_2026-09-26.md#tooltip-request-lifetime-boundary-2026-09-28-candidate).

Tooltip follow-up is source-only: a detached native-row payload/strict bounded
codec exists, but has no runtime callers. Host capture, semantic per-peer
transport and stock rendering remain open. cmp-b21 both builds and cmp-c21 all
24 contract projects pass (907 tooltip assertions). Installed cmp-b19 is unchanged. See
[foundation and provider-isolation audit](CAMPAIGN_MAP_RESUME_2026-09-26.md#tooltip-provider-isolation-and-detached-payload-foundation-2026-09-28).

Current source corrects a stale deferred map reconciliation: a queued catalog
projection could overwrite a newer dynamic state on the next frame. The drain
now projects the latest admitted stores, including visibility and membership.
All 656 body assertions pass; cmp-b19 both builds and cmp-c19 all 24 projects pass.
Native map-body-r2 passed two settlement entry/exit cycles, body reappearance,
pinned-party invalidation and camera detach/return. All 24 owned identities are
absent, seven configs match, no new configs/products; cmp-b19 is retained. Full
hover fidelity and battle-return regression remain open. See
[ordering correction](CAMPAIGN_MAP_RESUME_2026-09-26.md#deferred-reconciliation-ordering-correction-2026-09-28).

Current source corrects the extra mobile body inside settlements while retaining
the logical host/camera anchor and occupant badges. Intermediate cmp-b17/cmp-c17
passed; final focus-aware panel invalidation passed cmp-b18 both builds and
cmp-c18 all 24 projects. Fresh map-body-r1-20260928 loaded cmp-b18 in all three
roles. Entry hiding/pinned-panel invalidation and camera detach/return passed,
but the body did NOT reappear after exit: native acceptance FAILED. All 22 owned
identities exited, seven configs match, no new configs/products, and all five
module preimages (cmp-b16) were restored. See
[body admission and remaining gates](CAMPAIGN_MAP_RESUME_2026-09-26.md#mobile-body-admission-correction-2026-09-28).

Current cmp-b16 (26/22/7) passed bounded native settlement occupant badge checks:
ordinary occupant, departure clear, and the host lord banner on entry to Avalyps.
Both builds/all 24 cmp-c16 contracts passed; all three loaded identities verified.
Camera detach/return passed. All 22 owned identities absent, seven configs match,
no new configs/products; cmp-b16 retained. Mixed ordering, dialogue/battle re-entry
and full parity remain open. A mounted party visual remains incorrectly visible
inside the village; native hover tooltips also remain unimplemented.
See [candidate and remaining gates](CAMPAIGN_MAP_RESUME_2026-09-26.md#settlement-occupant-badge-candidate-2026-09-28).

Current source follow-up preserves supplied host banner colors in party/settlement
nameplates instead of overriding them with faction colors. Both cmp-b11 builds
and all 24 cmp-c12 contracts passed. Native map-banner-r1-20260928 passed bounded
party/settlement color comparison and camera detach/return. All 22 owned identities
are absent, seven configs match, no new configs/products; cmp-b11 retained.
Separate 3D fortification flags
remain unimplemented. See [banner audit and scope](CAMPAIGN_MAP_RESUME_2026-09-26.md#banner-color-preservation-candidate-2026-09-28).
The previously empty settlement occupant badge list now has the source candidate
above; host admission and native verification gates are recorded canonically.

Current cmp-b10 wall/preparation transport and owned visibility overrides use
25/21/6. Both builds/all 24 cmp-c11 tests passed. Native map-wall-r1-20260927
passed bounded Vostrum geometry, host zoom, camera detach/return and read-only
dialogue update/close. All 23 recorded PIDs absent, seven configs restored, no new
configs/products; cmp-b10 retained. Full parity/siege/battle regressions remain open.
See [implementation and live-run boundary](CAMPAIGN_MAP_RESUME_2026-09-26.md#wall-and-siege-preparation-candidate-2026-09-27).

Historical source cmp-b9 removes stock fortification gate/banner authoring helpers from
the owned replica scene before binding/rendering. Both builds/all 24 cmp-c10
contracts pass; no deployment/native verification. Installed cmp-b8 remains.
Wall/preparation/live banner and full parity work remain open.
See [candidate and next gates](CAMPAIGN_MAP_RESUME_2026-09-26.md#fortification-authoring-helper-candidate-2026-09-27).

Latest source candidate cmp-b8: host-selected settlement level/state variants cross
protocol 24/bridge 20/catalog 5 and apply to owned scene descendants before reveal.
Both builds/all 24 cmp-c9 projects passed. Native Avalyps/Lavenia variants now match
the observed host footprint; Vostrum extras/full parity remain open. Camera detach/
return and read-only dialogue/update/close passed. All 22 owned identities absent,
seven configs match, no new configs/products; installed cmp-b8 retained.
See [implementation and remaining geometry gaps](CAMPAIGN_MAP_RESUME_2026-09-26.md#host-selected-settlement-levels-2026-09-27-bounded-native-result).

Latest cmp-b5 bounded native result: automatic map, detach/explicit return and
map-overlay dialogue passed; extra settlement building variants remain visible.
Party-menu companion Conversation is a separate unsupported dialogue path.
Both builds/all 24 cmp-c6 projects passed. All test-owned product processes exited,
seven configs were restored, no new configs remain; installed cmp-b5 is retained.
Root hide/reappearance transitions and full visual parity are not accepted.
See [candidate and limits](CAMPAIGN_MAP_RESUME_2026-09-26.md#static-settlement-geometry-admission-2026-09-27).

Historical source step (2026-09-27): cmp-b4 removes native authoring settlement circles
only from the owned replica scene. Both builds and all 24 cmp-c5 contracts passed,
installed cmp-b3 unchanged; correction not deployed/native-verified. Investigation
also confirms missing host binding for settlement geometry/visual variants,
including hidden-root admission: nameplate parity is not geometry disclosure proof.
See [native investigation and next gates](CAMPAIGN_MAP_RESUME_2026-09-26.md#settlement-scene-presentation-investigation-2026-09-27).

Historical map stage (2026-09-27): installed cmp-b3 passed bounded tracked Tevea
left-edge/host-zoom comparison, local detach/explicit return and attached-camera
preservation after dialogue wheel input/close. Read-only Tristania dialogue and
text update passed; visual readiness 44 ms. All 24 cmp-c4 projects and both cmp-b3
builds passed before deployment. Protocol 23/bridge 19/catalog 4.
Run `map-edge-r1-20260927` postflight confirms no owned/product processes, seven
configs restored, no new configs and retained cmp-b3 hashes. Test-owned Steam was
removed; test saves remain. Full parity and broader regression remain open.
See [behavior and acceptance boundary](CAMPAIGN_MAP_RESUME_2026-09-26.md#host-camera-following-and-settlement-presentation-2026-09-27).

Current correction (2026-09-27): same-action body poses now steer bounded playback
rate instead of repeatedly seeking phase. cmt-b27 built both roles without installed
changes; all 24 cmt-c25 projects passed (4,331 pose/playback assertions). The user
confirmed smooth body motion like campaign on cmt-b27. First/repeated readiness
46/53 ms, retention, read-only answers, text update and both closes passed.
All twelve owned identities are gone; seven configs match, no new configs/product
processes, original Steam/test saves preserved. Installed cmt-b27 retained and
independently hash-verified. No full facial/HUD/map 1:1 or battle regression claim.
See [native result and remaining gates](CAMPAIGN_MAP_RESUME_2026-09-26.md#cmt-b27-native-rate-steering-result).

Historical native result (2026-09-27): cmt-b25 body-pose transport/first dialogue/text
transition/close passed, but the user observed jerking actors versus smooth host
motion. Animation acceptance FAILED; repeat opening was not completed. All twelve
owned identities exited, seven configs match, no new config/product processes,
original Steam preserved, and all five cmt-b22 module preimages restored. Source
at that point contained b25; the newer b27 result above supersedes that state. Evidence:
artifacts/runtime/dialogue-pose-r1-20260927. See the canonical candidate below.

Historical candidate preparation (2026-09-27): separate live body-pose transport is wired
from initialized host tableau to ready owned client actors. Protocol 22/bridge 18;
cmt-b25 is now installed for a fresh native run. Pose-only changes preserve text/UI
and scene identity, with exact descriptor/revision/sequence binding. cmt-b23 builds
passed; cmt-c21 exposed a corrupt-input decoder exception (23/24), now corrected.
After the decoder correction, cmt-b24 built successfully; cmt-c22 exposed an
overbroad source assertion matching ActionIndexCache.Create as scene creation.
That assertion now targets actual scene/actor allocation. Final cmt-b25 builds
and all 24 cmt-c23 projects passed (719 pose assertions). Campaign/menu-owned
server/shortcut MP loaded identities are verified; map start was automatic.
The native failure and cleanup above supersede this preparation status.
See [pose candidate and limits](CAMPAIGN_MAP_RESUME_2026-09-26.md#live-body-pose-channel-candidate-2026-09-27).

Latest native result (2026-09-27): cmt-b22 restored visible scar/age face details
in first/repeated dialogue; readiness 42/51 ms and retention beyond one minute
each passed, as did read-only answers, text updates and both closes to map.
Both builds/all 24 cmt-c20 projects passed. All eleven exact-owned identities
exited; seven configs match, no new configs/product processes, original Steam
preserved. Test saves and installed cmt-b22 retained. Facial animation, poses,
top HUD and map parity remain open; no 1:1 acceptance. Evidence:
artifacts/runtime/dialogue-face-r1-20260927. See
[candidate](CAMPAIGN_MAP_RESUME_2026-09-26.md#bounded-face-preparation-candidate).

Current native result (2026-09-27): cmt-b20 removes gross dialogue-image scaling
and displacement; background framing matches the host. First/repeated readiness
48/49 ms, three actors retained beyond one minute each, read-only answers/text
update/both closes passed. Faces/animation/top HUD and map camera still differ;
no 1:1 acceptance. Both builds/all 24 cmt-c18 projects passed. All eleven owned
identities exited, seven configs match, zero new configs/product processes,
original Steam preserved; test saves/installed cmt-b20 retained. See
[viewport native result](CAMPAIGN_MAP_RESUME_2026-09-26.md#cmt-b20-native-viewport-result).

Previous native result (2026-09-27): cmt-b19 passed first/repeated dialogue operational
readiness (63/50 ms), stayed visible beyond 50 seconds each, and returned to the
map on both closes. Read-only client answer handling was verified. Framing,
appearance and HUD still differ; no 1:1 acceptance. Both builds and all 24 cmt-c17
contracts passed. All eleven owned identities exited, seven configs match, no new
configs/product processes remain, original Steam preserved; installed cmt-b19 and
test saves retained. Evidence: `artifacts/runtime/dialogue-configured-r1-20260927`.
See [current dialogue result](CAMPAIGN_MAP_RESUME_2026-09-26.md#cmt-b19-native-result).

Historical follow-up (2026-09-27): cmt-b18 also timed out twice, including after
visible actors/background. TargetConfigured=False while loaded submission and
all three actors were ready. All eleven owned identities exited, seven configs
match, no new configs/product processes remain, Steam is preserved. Evidence:
`artifacts/runtime/dialogue-ready-r1-20260927`. Source now admits successful
synchronous owned-target configuration rather than awaiting PaintNeeded, with
epoch-bound stale callback rejection. The newer verification is recorded above.
No 1:1 dialogue or overall goal acceptance is claimed.

Earlier source correction (2026-09-27): tableau readiness now uses the exact native
target callback, loaded-target draw submission and all actor resources, not the
world-view flag. cmt-b18 built both roles and cmt-c16 passed all 24 projects;
installed for a fresh `dialogue-ready-r1-20260927` run (native acceptance/cleanup
PENDING). cmt-b16 displayed three dialogue actors/background on the second opening without
terrain shader errors, but the false view-ready flag removed them after 15 seconds.
Framing differs: no 1:1 pass. Map/text worked. All eleven owned identities exited,
seven configs match, no new configs/product processes remain, Steam is preserved.
Evidence: `artifacts/runtime/dialogue-request-r1-20260927`. Test saves remain.
See [resource isolation](CAMPAIGN_MAP_RESUME_2026-09-26.md#scene-wide-resource-request-isolation-2026-09-27).

Historical candidate (2026-09-27): cmt-b14 replaces the independent
dialogue world SceneLayer with an off-screen TableauView texture beneath the
read-only SPConversation UI. Both isolated builds and all 24 cmt-c13 contracts
passed. Native 3D dialogue FAILED with the same terrain shader errors; the
texture target does not eliminate reproduction. Map join, text fallback/update/
close worked. All eleven owned identities exited, seven config preimages match,
no new configs/product processes remain and original Steam is preserved.
Evidence: `artifacts/runtime/dialogue-texture-r1-20260927`. Test saves and installed
cmt-b14 remain; do not reuse ended PIDs or infer shader acceptance from contracts.
See [texture candidate](CAMPAIGN_MAP_RESUME_2026-09-26.md#off-screen-dialogue-target-candidate-2026-09-27).

Latest dialogue run (2026-09-27): cmt-b11 reproduced both terrain shader errors
despite removing full-scene preload/readiness calls. It is NOT a native fix.
Both role builds and all 24 cmt-c11 contracts passed. Native map join and dialogue
text fallback/update/close worked; independent 3D rendering failed. All eleven
owned identities exited, seven configuration preimages match, no new config
files exist and Steam is preserved. Test saves and installed cmt-b11 remain.
Evidence: `artifacts/runtime/dialogue-terrain-r1-20260927`. See
[candidate evidence](CAMPAIGN_MAP_RESUME_2026-09-26.md#terrain-disabled-tableau-investigation-2026-09-27).

Previous lifecycle run (2026-09-27): cml-b6 passed one automatic map -> field battle
-> map cycle with the same connected MP client and no console command. Host
movement/daylight and read-only dialogue text replicated. User assisted Login,
the H hotkeys and the attack order. The server's Attacker victory was applied
to the campaign before StartMap command=4; returned MP rendering was ready at
06:44:21.430. The dialogue terrain-shader failure reproduced and visual parity,
repeat/adjacent scenarios and the native MP Draw end-screen mismatch remain open.
All exact-owned product/support processes exited; all seven configuration
preimages match, no new config files were found, test saves remain. Steam and
installed cml-b6 are retained. Evidence: `artifacts/runtime/campaign-lifecycle-r2-20260927`.
cml-c5 passed all 24 projects. See [lifecycle acceptance boundary](CAMPAIGN_MAP_AUTOMATIC_LIFECYCLE.md).

Native lifecycle attempt (2026-09-27): coherent cml-b5 campaign and menu-owned
server loaded; automatic map opening was dispatched without a console command,
but the adapter faulted because it read Mission.Current before native Initialize.
No multiplayer was launched. Exact-owned product/support processes are gone and
all seven configuration preimages match; test saves remain. Steam is retained for
the corrected follow-up run. Source correction captures the exact OpenNew return
inside the synchronous dispatch, without adopting a later same-kind mission.
See [lifecycle correction and acceptance boundary](CAMPAIGN_MAP_AUTOMATIC_LIFECYCLE.md)
and `artifacts/runtime/campaign-lifecycle-20260927/README.md`.

Automatic mission lifecycle (2026-09-26): host/server main-thread adapters,
session-bound binary intent, pinned battle roster, exact native opening and
applied-result gates are now wired in source behind the separate default-off
`COOPSPECTATOR_AUTOMATIC_CAMPAIGN_MISSIONS` gate. Native map -> battle -> map
acceptance remains NOT RUN; installed cmt-b7 is unchanged. Initial cml-b4 compiled
both roles; final cml-b5 built both with installed inventories unchanged and
cml-c4 passed all 24 projects (305 channel / 4,305 lifecycle assertions).
See [current implementation and native acceptance boundary](CAMPAIGN_MAP_AUTOMATIC_LIFECYCLE.md).
The active campaign is user-designated test-only: future test autosaves may remain;
continue preserving unrelated saves and restoring temporary configuration.

Latest native evidence (2026-09-26, `cmt-b7`): **map mission ran; read-only dialogue text/open/update/close passed, but independent 3D dialogue failed twice.** Both attempts raised `pbr_terrain`/`pbr_terrain_gbuffer` shader compiler errors (X3129/X4008) and ended in visual readiness timeout; no rendered-actor parity is claimed. All exact-owned test processes are now gone, all 64 configuration/save preimages match, and Steam is preserved. This supersedes the earlier awaiting-command/live-process notes below. See [native failure and restoration](CAMPAIGN_MAP_RESUME_2026-09-26.md#cmt-b7-native-dialogue-failure-and-restoration).

Latest source-only follow-up: `cmt-b9` includes the resolved actor-scale correction, independent native readiness checks, and default-off load-stage diagnostics. Both isolated builds and all 24 `cmt-c9` contracts passed; it is not deployed or native-verified. The installed candidate remains `cmt-b7`, with no test roles running. See [source investigation](CAMPAIGN_MAP_RESUME_2026-09-26.md#post-run-readiness-investigation-source-only). Automatic map -> battle -> map is now a requested but [not-yet-implemented lifecycle](CAMPAIGN_MAP_AUTOMATIC_LIFECYCLE.md).

Latest native continuation (2026-09-26): **cmt-b7 is coherently installed and loaded in campaign, dedicated and multiplayer; dialogue rendering is still unverified.** The campaign menu started the server, the prescribed desktop shortcut started multiplayer, and Safe Mode No was selected. User-assisted Login succeeded; AC_COOP join succeeded at 17:16:51 and the client awaits mission start. Literal console input still needs user assistance after direct and on-screen-keyboard attempts. Three owned roles remain alive; cleanup is not executed. See [live continuation](CAMPAIGN_MAP_RESUME_2026-09-26.md#dialogue-candidate-native-run-started-2026-09-26). This supersedes earlier installed-candidate statements below, not their historical evidence.

Current source follow-up: **2026-09-26, independent 3D dialogue scene and atomic transport are implemented in source, not deployed or live-verified.** Protocol 21/bridge 17 carries the host-visible actor/camera/atmosphere descriptor; the client owns a separate passive scene, preserves it across text updates and explicitly cleans it up on close. `cmt-b7` compiled both roles without installed changes; `cmt-c7` passed all 24 contracts, including descriptor, payload and visual-lifetime checks. Installed/live behavior remains `cmr-b10` (20/16/4) below. Fresh native rendering/cleanup proof and animation/HUD parity remain pending. See [implementation boundary](CAMPAIGN_MAP_RESUME_2026-09-26.md#independent-client-dialogue-scene-source-implementation).

Current campaign-map candidate: **2026-09-26, `cmr-b10` partial live pass after both isolated builds and 24/24 `cmr-c10` contract projects passed.** Wire 20/bridge 16/catalog 4 is coherently installed and its loaded identities verified across campaign, dedicated and multiplayer. The user-assisted literal campaign command opened the replica mission; city selection, zoom, later host movement and read-only dialogue open/update/close were observed without a crash during this approximately eight-minute mission. A client answer click did not advance the host dialogue. The initial 397-entity catalog transferred, but comprehensive native packet/performance and visual 1:1 acceptance remain open. All exact-owned test processes were removed, all 64 configuration/save preimages match, no new config/save files exist, and Steam was preserved. The user closed OSK. The candidate remains installed intentionally. Main-party portrait/navigation, dialogue 3D tableau, settlement icons and banner/marker differences remain incomplete. See [resume evidence and remaining gates](CAMPAIGN_MAP_RESUME_2026-09-26.md).

Latest campaign-map prototype runtime verification: **2026-09-25, partial live pass.** The corrected host/dedicated/client run loaded the isolated `Main_map` scene without a crash, preserved host-visible marker parity, synchronized later party movement, passed all 24 contracts and both CompileOnly builds, and completed exact test-process/configuration cleanup. Literal RGL console input and client camera/selection interaction remain open. See [runtime evidence and next gate](CAMPAIGN_MAP_PROTOTYPE_RUNTIME_2026-09-25.md).

Current field automation stage: **Milestone 3 complete; Milestone 4 remains open. Published `438b434` passed clean 24/24 contracts (including 432 focused assertions and exact clock-IL inspection) and both CompileOnly builds. The fresh native child loaded the exact candidate but returned `Timeout/NoHeartbeat` while waiting for dedicated control readiness; no bootstrap or mission opened, and attempt two was Not Run.** Automatic timeout evidence/cleanup and full temporary-install restoration passed. The retained final role status contains a recent heartbeat, so missing/read-failed versus stale data at the original decision remains unresolved. See [live evidence and next boundary](BATTLE_TEST_AUTOMATION_M4_FIELD_SPAWN_SMOKE.md#26-clean-zero-client-live-attempt-stops-before-bootstrap-2026-09-10). The mode/clock/initial-peer corrections remain unverified inside a Bannerlord mission; no L2 pass is claimed.

Latest M4 scoreboard dependency correction: **2026-09-09**, published `a6fe74f`. A pure decision contract, minimal server bridge, ordered insertion helper and fail-closed invariant cover direct CoopBattle plus full TdmClone without Harmony suppression or default-on hot-path diagnostics. Focused contracts passed; `m4sd-c1` passed 24/24; `m4sd-b1` built client `A517CED3...80C371` and dedicated `74B03C00...2BD4B7` with 77/49 warnings, zero errors, no product launch and unchanged installed inventories. No module was staged and no native process ran. [Source/L1 closure and next live gate](BATTLE_TEST_AUTOMATION_M4_FIELD_SPAWN_SMOKE.md#19-scoreboard-server-dependency-source-and-contract-closure-2026-09-09).

Latest M4 dump diagnosis: **2026-09-09**. `C:\dumps\DedicatedCustomServer.Starter.exe.15968.dmp` is the 1,157,623,635-byte full dump for the previously documented PID/time and has SHA-256 `E5348288...F0E5CF124`. `dotnet-dump` places the sole meaningful exception on OS thread `0x20f0` in the Harmony wrapper for `MissionScoreboardComponent.AfterStart`; the live object has non-null mission/lobby/network/scoreboard fields but null `_mpGameModeBase`. The exact engine method dereferences that field's `RoundComponent` on a server. Field/land/village/sally-out paths share the CoopBattle risk, and the full TdmClone server path has the latent same defect; hideout, siege, CoopTdm, hero-creator and campaign-map paths already supply or avoid the dependency. No source, installed module, registry or game process changed. [Recovered-dump diagnosis and next fix gate](BATTLE_TEST_AUTOMATION_M4_FIELD_SPAWN_SMOKE.md#18-recovered-full-dump-and-exact-managed-failure-diagnosis-2026-09-09).

Latest M4 failure-finalization correction: **2026-09-09**, published `c100cb8`. All full `Win32_Process` acquisition used by finalization now runs in an exact provisionally owned child process with 5-second, 256-MiB, 4-MiB-output and 4,096-record limits; only five lightweight fields cross the boundary. Timeout, high-memory and collector failure are structured and cannot prevent primary crash/hang artifact publication; raw `CimInstance` records are not serialized. WER/CrashUploader correlation and non-fatal Watchdog policy remain unchanged. Focused tests passed in Windows PowerShell 5.1 and PowerShell 7; `m4fe-c3` passed 24/24 and `m4fe-b2` built both modules without product launch or installed mutation. Runtime correction remains Not Run. [Source/contract closure and next live gate](BATTLE_TEST_AUTOMATION_M4_FIELD_SPAWN_SMOKE.md#17-bounded-failure-evidence-source-and-contract-closure-2026-09-09).

Latest liveness-corrected live attempt: **2026-09-09**, `m4pl-p1-l1` from clean published `6d38780`. Child 1 loaded dedicated SHA-256 `0F7FA25...5382`, remained admitted by the corrected parent-liveness path, reached control readiness, completed all seven bootstrap acknowledgements, confirmed `start_game`, issued one `start_mission`, and reached `MissionOpening`. The dedicated process then produced WER APPCRASH `0xc0000005`; the report names no fault module and contains no dump, so the native root cause is unknown. Shared failure finalization started but did not complete, reached approximately 6.0 GiB private memory, and required exact identity-checked manual Runner termination after fail-closed cancellation refused stale ownership. Parent exit 40, child terminal evidence incomplete, child 2 Not Run, no L2 pass. The one-use transaction and independent postflight restored all 218 dedicated and preserved all 32 client files, original DLL hashes, protected result, ports, processes and 8/8 locks. [Canonical audit and next boundary](BATTLE_TEST_AUTOMATION_M4_FIELD_SPAWN_SMOKE.md#16-clean-published-liveness-corrected-rerun-native-mission-opening-crash-and-failure-evidence-stall-2026-09-09).

Previous field live attempt: **2026-09-09**, m4b-local-live-20260909-01 from clean ebf9cd3: exact dedicated hash and seven bootstrap acknowledgements confirmed; parent Timeout, incorrect readiness interface name and stalled failure handling found. Manual exact cleanup completed; original 218-file dedicated and 32-file client inventories verified, ports/locks clear. Second attempt Not Run; no L2 pass. Fresh contracts passed 24/24 and both builds passed. [Evidence, restoration and remaining work](BATTLE_TEST_AUTOMATION_M4_FIELD_SPAWN_SMOKE.md#12-first-clean-local-only-live-attempt-2026-09-09).

Latest M4 correction verification: **2026-09-09**, bounded synthetic failure diagnostics passed under PowerShell 7.6.5 and Windows PowerShell 5.1; the prior live stall/high-memory observation was not reproduced. Exact ListedServer readiness, fatal reporter versus watchdog semantics, support cleanup grace, PowerShell 5.1 hash initialization, and failure-phase markers are implemented. `m4b-readiness-runner-contracts-20260909-01` passed 24/24; `m4b-readiness-runner-compile-20260909-01` built both modules without product launch or installed mutation. [Correction evidence and limits](BATTLE_TEST_AUTOMATION_M4_FIELD_SPAWN_SMOKE.md#13-bounded-failure-diagnostic-and-sourcecontract-correction-2026-09-09).

Previous clean M4 rerun: **2026-09-09**, `m4r27-live-01` from exact published `c3ec15a`. Child 1 loaded corrected dedicated SHA-256 `0F7FA25...5382` but cancelled before readiness when the combined 10-second parent-liveness admission rejected the still-running parent. The failed subcondition was not recorded; heartbeat staleness versus a transient shared read remains unresolved. Automatic exact cleanup, non-fatal watchdog handling, local result preservation, full 218/32-file restoration, free ports, and eight released-lock probes passed. Child 2 was Not Run; no L2 pass. [Exact audit and next boundary](BATTLE_TEST_AUTOMATION_M4_FIELD_SPAWN_SMOKE.md#14-clean-m4-l2-rerun-parentliveness-cancellation-2026-09-09).

Latest M4 parent-liveness correction: **2026-09-09**, exact admission facts, a pure bounded read-fallback decision and one failure-only rejection artifact added; unreadable shared snapshots receive only an existing-window grace with a still-live cached exact parent; immutable, stale and process-loss facts still fail closed. Output capture is limited to 100 ms per stream per poll without dropping completion output. The focused dual-shell harness directly passed allowed/expired/process-loss fallback cases and retained 8,000 burst lines; final short-path run `m4pl-c3-01` passed 24/24, and both `m4pl-b3-01` builds passed with installed inventories unchanged. No native rerun or L2 claim. [Source, contracts, hashes and limits](BATTLE_TEST_AUTOMATION_M4_FIELD_SPAWN_SMOKE.md#15-parentliveness-source-and-contract-correction-2026-09-09).

Latest local-isolation verification: **2026-09-09** (m4-local-contracts-20260909-01, 24/24; m4-local-compile-20260909-01, both modules compiled without installed changes). Field-profile bridges use local run storage; the personal result is not accessed. See [local ownership, evidence and audit](BATTLE_TEST_AUTOMATION_M4_FIELD_SPAWN_SMOKE.md#11-local-only-file-isolation-2026-09-09).

Last source verification: **2026-09-09**
Last bounded runtime-feasibility verification: **2026-09-02** (`m2b3a-live-r2-01`, exact clean revision `c6507e9`; exact dedicated/client loaded hashes, live schema-2 heartbeat/progress, complete bootstrap, native login, terminal `Connected`, six independently acquired/released resources, graceful cleanup, protected-state preservation, and no crash/hang artifact confirmed)
Last automation-control source/contract verification: **2026-09-09** (`m4b-readiness-runner-contracts-20260909-01`; fatal-helper ownership/classification, role-specific cleanup grace, Windows PowerShell 5.1 hash initialization, and failure-phase markers passed in both supported shells and the full 24/24 inventory)
Last field automation source/contract verification: **2026-09-09** (`a6fe74f`; focused startup contracts passed; `m4sd-c1`, 24/24; `m4sd-b1`, both modules compiled with installed inventories unchanged; scoreboard dependency correction present, native field L2 pending)
Last automation-foundation verification: **2026-08-31** (`m2a-contracts-20260831-07` and `m2a-compile-only-20260831-03`, no product process launched)
Last controlled on-disk staging verification: **2026-09-02** (`m2b3a-pub-b-01` and `m2b3a-stage-01`, clean pushed revision `5d6a6c6`; complete 32-file client and 218-file dedicated pre-images retained; client `A363B19B...C91C0075` and dedicated `2E1494BC...C0CA1414` installed; protected result unchanged; no product process launched)
Last Milestone 2B.1 source/contract/compile verification: **2026-08-31** (`m2b1-final-contracts-20260831-03`, 21/21, and `m2b1-final-compile-only-20260831-02`; no product process launched; installed modules unchanged)
Last live-runner correction verification: **2026-08-31** (aggregate correction `8f68d43` and client handoff correction `711f2ca` published; focused Windows PowerShell 5.1/7 coverage passed; `m2b2c-client-handoff-contracts-20260831-01` passed 22/22; `m2b2c-client-handoff-compile-20260831-01` passed; installed modules unchanged)
Last clean live validation: **2026-09-02** (`m2b3a-live-r2-01`; client `A363B19B...C91C0075` and dedicated `2E1494BC...C0CA1414` loaded; live role health, exact server selection, terminal `Connected`, six independent shared locks, protected-state preservation, exact automatic cleanup, and passing-path crash/hang absence confirmed)
Repository: `BannerlordCoopSpectator3`
Verification scope: static review of source, project files, module descriptors, tests, scripts, existing documentation, selected installed game assemblies, and ILSpy output, plus the separately documented Milestone 1 installed-runtime probe, post-M1 client-control validation, Milestone 2A contracts/compile-only proof, controlled `0.3.2` staging, Milestone 2B.1 runtime-safety source/contracts, published ownership/handoff corrections, Milestone 2B.2D dedicated control, and live runs through `m2b3a-live-r2-01`. The latest clean live run reconfirmed both loaded identities, retained the full bootstrap, completed native login and exact run-owned server selection, reached terminal `Connected`, published live role heartbeat/progress, acquired six expected resources through six distinct lock paths, cleaned both exact roles gracefully, verified all six releases, released both ports, preserved the protected result, and created no crash/hang artifact. Milestone 2B is complete. Campaign and battle behavior remain unverified by this connection-only run.

## Purpose

This directory is the canonical living documentation for future Codex work. It is intentionally organized by engineering question rather than development date. Dated files in the parent `docs/` directory remain valuable evidence, but many describe intermediate states, experiments, or planned changes.

The project converts a single-player Bannerlord campaign encounter into a synchronized multiplayer mission, runs the authoritative battle on a dedicated server, and writes the result back into the host campaign. The difficult part is preserving exact campaign identity, equipment, armies, scene topology, deployment, casualties, and aftermath while using Bannerlord's multiplayer networking and mission runtime safely.

## Document map

| Document | Use it for |
|---|---|
| [ARCHITECTURE.md](ARCHITECTURE.md) | System boundaries, authority, data ownership, major components, and active feature flags |
| [CODE_MAP.md](CODE_MAP.md) | Finding the correct file, class, subsystem, adapter, patch, UI, bridge, or test |
| [RUNTIME_FLOWS.md](RUNTIME_FLOWS.md) | Campaign-to-mission startup, topology handshake, snapshot transfer, spawn/materialization, phases, reconnect, completion, and scenario routing |
| [BUILD_TEST_DEBUG.md](BUILD_TEST_DEBUG.md) | Reference profiles, side-effecting builds, contract tests, scripts, logs, diagnostics, and safe validation |
| [GOAL_MODE.md](GOAL_MODE.md) | Autonomous Goal-mode authorization, precedence, allowed actions, boundaries, and stopping conditions |
| [RELEASE_PACKAGING.md](RELEASE_PACKAGING.md) | Canonical GitHub/Nexus archive layouts, packaging commands, exclusions, validation, and publication boundaries |
| [BATTLE_TEST_AUTOMATION_AUDIT.md](BATTLE_TEST_AUTOMATION_AUDIT.md) | Source-verified feasibility findings, unsafe assumptions in the supplied proposal, and the required evidence ladder |
| [BATTLE_TEST_AUTOMATION_SPEC.md](BATTLE_TEST_AUTOMATION_SPEC.md) | Canonical staged requirements and acceptance criteria for battle-test automation |
| [BATTLE_TEST_AUTOMATION_M1_FEASIBILITY.md](BATTLE_TEST_AUTOMATION_M1_FEASIBILITY.md) | Named-machine runtime-feasibility evidence, capability decisions, blockers, and the revised pre-runtime gate plan |
| [BATTLE_TEST_AUTOMATION_CLIENT_JOIN_IMPLEMENTATION.md](BATTLE_TEST_AUTOMATION_CLIENT_JOIN_IMPLEMENTATION.md) | Post-M1 run-scoped launcher/lobby-control implementation, validation evidence, requirement audit, and remaining runtime gates |
| [BATTLE_TEST_AUTOMATION_M2A_IMPLEMENTATION.md](BATTLE_TEST_AUTOMATION_M2A_IMPLEMENTATION.md) | Completed non-runtime runner/protocol/compile-only implementation, authoritative L0/L1 evidence, requirement audit, and Milestone 2B boundary |
| [BATTLE_TEST_AUTOMATION_M2B_STAGING.md](BATTLE_TEST_AUTOMATION_M2B_STAGING.md) | Controlled `0.3.2` on-disk installation identity, retained `0.3.1` pre-images, post-install doctor evidence, and the remaining loaded-hash gate |
| [BATTLE_TEST_AUTOMATION_M2B1_RUNTIME_FOUNDATION.md](BATTLE_TEST_AUTOMATION_M2B1_RUNTIME_FOUNDATION.md) | Fail-closed runtime identity/result-isolation source, exact owned-host/cleanup/recovery control, native bootstrap correction, contract evidence, and live-run boundary |
| [BATTLE_TEST_AUTOMATION_M2B2_STAGING.md](BATTLE_TEST_AUTOMATION_M2B2_STAGING.md) | Current committed-source staging identity, retained prior `0.3.2` trees, clean-build hash chain, post-stage doctor evidence, and the connection-only live gate |
| [BATTLE_TEST_AUTOMATION_M2B2_FEASIBILITY.md](BATTLE_TEST_AUTOMATION_M2B2_FEASIBILITY.md) | Live feasibility evidence, exact dedicated/client identities, bounded recovery, runner corrections, corrected resolver proof, and native platform-login boundary |
| [BATTLE_TEST_AUTOMATION_M2B2C_RUNNER_CORRECTION.md](BATTLE_TEST_AUTOMATION_M2B2C_RUNNER_CORRECTION.md) | Ownership corrections, clean live ownership/cleanup proof, disproved redirected-console assumption, exact PID-log evidence, and the next dedicated-control gate |
| [BATTLE_TEST_AUTOMATION_M2B2D_DEDICATED_CONTROL.md](BATTLE_TEST_AUTOMATION_M2B2D_DEDICATED_CONTROL.md) | Structured dedicated lifecycle/request/seven-acknowledgement implementation, native decompilation basis, contract/compile evidence, and staging/runtime boundary |
| [BATTLE_TEST_AUTOMATION_M3_FIELD_FIXTURE.md](BATTLE_TEST_AUTOMATION_M3_FIELD_FIXTURE.md) | Default-off exact post-write campaign-roster capture contract, first-field admission, integrity/privacy boundaries, source/contract evidence, and remaining real-capture/oracle/reproduction gates |
| [INVARIANTS_AND_RISKS.md](INVARIANTS_AND_RISKS.md) | Protected contracts, known limitations, high-risk files, regression matrix, and change checklists |

The repository-root [AGENTS.md](../../AGENTS.md) is the concise instruction and routing entry point.

## Sixty-second mental model

```text
Campaign host
  detects a native campaign encounter
  builds BattleStartMessage + full BattleSnapshotMessage
  writes battle_roster.json and starts/notifies the dedicated mission
            |
            v
Dedicated server
  opens the exact campaign scene through a multiplayer mission shell
  sends a compact pre-mission topology contract before client scene load
  owns native physical agent creation and authoritative mission state
            |
            v
Remote client
  refuses unsafe scene load until the topology contract matches
  opens the mission, receives the chunked full snapshot, and acknowledges it
  paces initial native CreateAgent replay for supported large-battle scenarios
  selects a side/entry, controls an authoritative agent, and renders UI
            |
            v
Battle end
  dedicated writes battle_result.json with authoritative entry outcomes
  campaign host validates campaign/result identity, applies aftermath once,
  journals the writeback, and exits/continues the native campaign flow
```

There are two distinct communication layers:

1. `NetworkManager` and its TCP line protocol handle the older campaign-host coordination path, including host state and `BATTLE_START` notifications.
2. Bannerlord mission networking handles the pre-mission topology contract and all in-mission authoritative state. `CoopMissionNetworkBridge` is the central in-mission transport.

Local bridge files are a third mechanism, but they coordinate processes, commands, and tooling on a machine. They are not a replacement for authoritative mission networking.

## Current verified runtime snapshot

### Builds and module identity

- Client project: `CoopSpectator.csproj`, `net472`, `x64`, assembly version `0.3.2`.
- Dedicated project: `DedicatedServer/CoopSpectatorDedicated.csproj`, `net472`, `x64`, constant `COOPSPECTATOR_DEDICATED`.
- Both outputs use assembly name `CoopSpectator`, but compile different `SubModule` implementations and different source/reference profiles.
- Client module ID: `CoopSpectator`.
- Dedicated module ID: `CoopSpectatorDedicated`.
- The client build conditionally compiles `GameMode/` when a compatible `TaleWorlds.MountAndBlade.Multiplayer.dll` is available.
- The normal client build triggers client deployment and, by default, a dedicated build/deployment. See [BUILD_TEST_DEBUG.md](BUILD_TEST_DEBUG.md) before building.

### Registered game modes

- `CoopBattle`: primary campaign battle mode; also replaces the official `Battle` registration.
- `CoopTdm`: cooperative TDM-derived mode.
- `CoopHeroCreator`: multiplayer hero-creation mission.
- `CoopHideoutDay` and `CoopHideoutNight`: isolated hideout paths.
- `CoopCampaignMapPrototype`: disabled unless `COOPSPECTATOR_CAMPAIGN_MAP_PROTOTYPE=1`.
- `TdmClone`: compiled support exists but its experiment flag is disabled.
- Official `TeamDeathmatch` remains the stable listed baseline; passive diagnostics injection is enabled.

### Scenario support

Supported when their scenario contracts validate:

- ordinary field battle;
- village battle;
- siege assault with deployment;
- sally out;
- siege ambush;
- relief battle;
- lords hall stage;
- day hideout assault;
- night hideout ambush.

Explicitly rejected:

- blockade;
- blockade sally out;
- any supported scenario whose active mission does not satisfy its required contract.

Do not interpret the presence of an adapter as unconditional support. `BattleDetector.TryGetUnsupportedCoopMissionReason` validates live mission facts and fails closed for invalid contracts.

### Active materialization strategy

- The native server mission stack is the only physical spawner.
- The full campaign snapshot provides exact logical identity and loadout data.
- Multiplayer-safe surrogate characters remain the network identity mechanism.
- `ExactCampaignPreSpawnLoadoutPatch` injects snapshot body/equipment before native spawn.
- Client initial `CreateAgent` replay is paced for field, village, siege assault, and siege ambush paths.
- Native reinforcements remain active; custom staged siege reinforcement materialization is disabled.
- The runtime object registry experiment is disabled.
- Full snapshot transport V2 is active with schema version 1, manifests, chunk windows, range acknowledgements, retries, completion acknowledgement, and abort handling.

### Battle-test client control

- A default-off, run-scoped multiplayer-client launcher and normal-lobby join controller now exist in source.
- The request is bound to a `RunId`, token hash, expected loaded client hash, exact server name/port, and a run-scoped owned-host proof tied to a live PID/path/start time and active UDP port.
- The path passed isolated contract/source compilation and non-launching environment validation on 2026-08-31.
- Current transaction `m2b3a-stage-01` installed clean-published Revision 17 client SHA-256 `A363B19B...C91C0075` and dedicated SHA-256 `2E1494BC...C0CA1414`, retained complete 32-file client and 218-file dedicated pre-images, and preserved the protected result. Run `m2b3a-live-r1-01` promoted both identities to `ConfirmedLoadedHash`. The subsequent shared-lock correction is runner-only and requires no restaging.
- The launcher and client module do not issue `start_game` or automate battle phases/results. The Milestone 2B `Feasibility` runner may authorize only the minimum vanilla `TeamDeathmatch` bootstrap after loaded-hash, suppression, and exact owned-role readiness. The default-off run/token/hash/process-bound dedicated lifecycle/request/acknowledgement channel is runtime-verified through all seven steps and UDP ownership. Native platform login, exact server selection, native join, post-rewrite handoff, and terminal `Connected` are now runtime-confirmed without UI automation, credentials, bypass, or blind retry. The bootstrap is not campaign or L2/L3 battle evidence.

### Battle-test non-runtime foundation

- `scripts/Invoke-CoopTest.ps1` owns fresh run roots and exposes non-runtime `Doctor`, `Contracts`, and `CompileOnly`, bounded connectivity `Feasibility`, private campaign `Record`, read-only `Inspect`, fail-closed exact-run `Cancel`, and opt-in exact `Recover` control. Protocol 1.1 capability gates cover role health, cancellation, recovery, and failure evidence. Runtime commands acquire canonical shared-resource locks before product launch, classify `NoHeartbeat` separately from `NoProgress`, and correlate owned helpers without name-only matching. Exact `CrashUploader.exe`/`WerFault.exe` paths are fatal signals; an owned `Watchdog.exe` is cleanup support, not crash evidence by itself. `Record` additionally requires clean local/upstream source and an explicit installed client hash, uses exact installed-1.4.8 singleplayer arguments, validates a runner-owned campaign handoff and exact fixture bytes, and preserves private/non-oracle/non-L2 boundaries. `scripts/CoopAutomationRunner.Core.ps1` supplies strict request/status, bounded process observation, provisional/verified identity, PID-reuse-resistant cleanup, one-snapshot descendant primitives, centralized fatal-helper policy, and role-specific cleanup grace.
- `Tests/contract-tests.manifest.json` is the canonical 24-project inventory. Latest run `m4b-readiness-runner-contracts-20260909-01` passed all 24; `m4b-readiness-runner-compile-20260909-01` passed both builds with installed inventories unchanged and no product launch. Milestone 3B run `m3b-contracts-01` passed all 23 and `m3b-compile-01` passed both builds without installed mutation; this remains local source/contract evidence, not a clean-published or runtime fixture claim.
- `CoopCompileOnly=true` redirects all build/package state below the run root and disables installation deployment; clean pushed run `m2e-pub-compile-r1` compiled both version `0.3.2` assemblies and proved installed module inventories unchanged, producing the exact client later installed by `m2e-client-stage-r1`.
- Protocol 1.0 covers run/nonce/role identity, leases, ordered events, stable outcomes/reasons, known-issue annotations, atomic/append file behavior, recovery classification, and fault injection.
- Later live runs supplied explicit installed hashes and confirmed both loaded identities, native login, exact server selection, native join, formal handoff, terminal connection, role heartbeat/progress, graceful exact cleanup, and passing-path crash/hang absence. The Milestone 1 capability matrix is fully `Confirmed`. Milestone 2B source/contracts cover shared machine locks, aggregate cancellation, recovery, and structured crash/modal/hang evidence; `m2b3a-live-r2-01` closes its final live gap by acquiring and releasing all six shared resources independently.
- Milestone 3A adds a default-off exact post-write `battle_roster.json` recorder and integrity/admission contracts. Milestone 3B adds `Start-CoopFieldFixtureCapture.ps1` plus aggregate `Record`, exact campaign ownership/cleanup, bounded `Skipped`/`Failed`/`Recorded` handling, protected-result verification, and a credential-free reproduction descriptor. `m3b-dirty-block-01` rejected dirty source without launching Bannerlord. Milestone 3 is complete: `m3b-live-capture-02` supplied the private exact capture and Milestone 3C supplied the reviewed derivative, independent oracle and hash-gated replay. Milestone 4 now has field-only source/contracts, local isolation, exact readiness, and corrected shared failure semantics; native mission, automatic live-failure cleanup, full battle, and live L2/L3 proof remain pending.

### Runtime evidence retained from dated reports

The most relevant recorded manual evidence is summarized in [CURRENT_EXACT_BATTLE_MATERIALIZATION_RUNTIME_2026-07-27.md](../CURRENT_EXACT_BATTLE_MATERIALIZATION_RUNTIME_2026-07-27.md):

- field battle materialization and deployment boundaries were validated on one and two machines;
- village materialization and boundaries were validated on one and two machines;
- external siege initial materialization, native reinforcements, sequential battles, final completion, and result writeback were exercised successfully;
- active-battle external-siege reconnect was still unverified in that report;
- exact final ownership of every captured lord was not separately verified;
- the custom staged siege reinforcement path remained frozen.

Treat those results as evidence for the recorded revision, not as an automatic guarantee after later code changes.

## Evidence taxonomy

Use these labels when maintaining this knowledge base:

- **Source-verified**: directly confirmed in current source/configuration.
- **Build-verified**: the named project/configuration compiled successfully; this does not prove runtime behavior.
- **Test-verified**: the named automated or contract tests passed for the recorded revision.
- **Runtime-verified**: backed by a named log, dump, or recorded manual run for a known revision.
- **Regression-verified**: the stated affected and adjacent scenarios/roles were rerun successfully.
- **Planned**: described in a technical specification but not confirmed in current source.
- **Historical**: useful explanation of an earlier architecture, failure, or experiment.
- **Unknown**: requires a fresh source or runtime investigation.

Important current example: [EXACT_SIEGE_UNUSED_MACHINE_FINALIZATION_FIX_TZ_2026-08-21_V3.md](../EXACT_SIEGE_UNUSED_MACHINE_FINALIZATION_FIX_TZ_2026-08-21_V3.md) is a detailed **planned** design. During the 2026-08-28 source review, the named two-phase unused-machine finalization operation was not found in `CoopSiegeMachineDeploymentController` or `CoopMissionNetworkBridge`. Do not cite that specification as implemented behavior without re-checking the source.

## Requirement and completion audit

The mandatory completion protocol is defined in [AGENTS.md](../../AGENTS.md). Before a task governed by a technical specification or approved plan is called complete:

1. Convert every mandatory requirement and acceptance criterion into an auditable checklist item.
2. Link each item to implementation evidence, validation evidence, affected scenarios/roles, and documentation impact.
3. Mark each item `Satisfied`, `Partially Satisfied`, `Not Satisfied`, or `Not Verifiable`.
4. Distinguish source inspection, implementation, build, automated tests, Bannerlord runtime verification, and regression verification.
5. Do not report 100% completion while a mandatory item is partial, unsatisfied, or lacks verification required by its acceptance criteria.

If runtime validation was not performed, use precise language such as “implemented and source-verified, but not runtime-verified in Bannerlord.”

## How to start a task

| Task | Read first | Then inspect |
|---|---|---|
| Campaign encounter detection or aftermath | `RUNTIME_FLOWS.md` | `Campaign/BattleDetector.cs`, `Campaign/*Adapter.cs`, result contracts |
| Mission startup or scene mismatch | `ARCHITECTURE.md`, `RUNTIME_FLOWS.md` | topology component/state, mission mode, open patches |
| Spawn, equipment, mount, hero, or visual parity | `INVARIANTS_AND_RISKS.md` | snapshot model, exact transfer contracts, pre-spawn patch, spawn handoff patch |
| Battle phase, readiness, selection, respawn | `RUNTIME_FLOWS.md` | mission behaviors, network bridge, authority/session/runtime state classes |
| Siege deployment, machines, ladders, or AI | all three core maps | siege mode, siege infrastructure, network bridge, siege patches, latest focused reports |
| UI or player commands | `CODE_MAP.md` | `UI/`, `Commands/`, matching GUI prefab |
| Build/runtime mismatch | `BUILD_TEST_DEBUG.md` | both project files, module descriptors, actual DLL stamps and logs |
| Release packaging or archive layout | `RELEASE_PACKAGING.md`, `BUILD_TEST_DEBUG.md` | `scripts/CreateReleasePackage.ps1`, release documents, module descriptors, generated archives |
| Battle-test automation | `BATTLE_TEST_AUTOMATION_AUDIT.md`, `BATTLE_TEST_AUTOMATION_SPEC.md`, `BATTLE_TEST_AUTOMATION_M1_FEASIBILITY.md`, `BATTLE_TEST_AUTOMATION_CLIENT_JOIN_IMPLEMENTATION.md`, `BATTLE_TEST_AUTOMATION_M2A_IMPLEMENTATION.md`, `BATTLE_TEST_AUTOMATION_M2B_STAGING.md`, `BATTLE_TEST_AUTOMATION_M2B1_RUNTIME_FOUNDATION.md`, `BATTLE_TEST_AUTOMATION_M2B2_STAGING.md`, `BATTLE_TEST_AUTOMATION_M2B2_FEASIBILITY.md`, `BATTLE_TEST_AUTOMATION_M2B2C_RUNNER_CORRECTION.md`, `BATTLE_TEST_AUTOMATION_M2B2D_DEDICATED_CONTROL.md`, `BATTLE_TEST_AUTOMATION_M2B3_SAFETY_CLOSURE.md`, `BATTLE_TEST_AUTOMATION_M3_FIELD_FIXTURE.md` | compile-only targets, aggregate runner/protocol, exact fixture admission/recording/integrity, historical and current-source controlled staging evidence, live feasibility evidence and runner correction, runtime identity/result isolation, role health, shared runtime locks, cancellation/recovery, exact failure evidence, run-scoped launcher/control files, dedicated lifecycle/request/acknowledgement bridge, mission readiness, result publication/writeback, scenario adapters, installed-runtime capability evidence |
| New diagnostics | `INVARIANTS_AND_RISKS.md`, `BUILD_TEST_DEBUG.md` | `ExperimentalFeatures.cs`, `CoopDebugConfig.cs`, target hot path |

## Maintenance workflow

Every implementation plan must include a `Documentation Impact` section. When an approved change alters behavior:

1. List the living documents read, documents to update, documents to create, and relevant documents intentionally unchanged with a reason.
2. Re-read the affected implementation and adjacent scenario adapters.
3. Update the narrowest relevant living document in the same approved change.
4. Keep each technical fact in one canonical home; link or summarize from other documents instead of duplicating the full statement.
5. Update cross-document links or tables only where the relationship changed.
6. Record a new verification date only for sections whose affected source facts were rechecked.
7. Add runtime evidence only after a real run; distinguish it from build or source verification.
8. Preserve dated reports. If a report is obsolete, label it historical from the living index rather than rewriting it.
9. If the change creates a new lasting subsystem, add it to `CODE_MAP.md`; if it changes authority or ownership, also update `ARCHITECTURE.md` and `INVARIANTS_AND_RISKS.md`.
10. Include required documentation updates in the final requirement-compliance audit.

### Change-to-document matrix

| Change type | Required living documentation |
|---|---|
| New directory, subsystem, major type, command, or test | `CODE_MAP.md` |
| Authority, ownership, dependency, module, or feature default | `ARCHITECTURE.md` |
| Startup sequence, messages, state transition, readiness, reconnect, result flow | `RUNTIME_FLOWS.md` |
| Build target, deployment behavior, script, environment variable, log path, test command | `BUILD_TEST_DEBUG.md` |
| Guardrail, unsafe pattern, known limitation, regression surface | `INVARIANTS_AND_RISKS.md` |
| Any of the above | this index if navigation or the current snapshot changes |

## Known documentation hazards

- Root and dated documents span many revisions from March through August 2026. Their dates matter.
- `PROJECT_CONTEXT.md` and `BUILD_RUNBOOK.md` are useful but predate several exact-scene and materialization changes.
- Some dated files are implementation plans, not completion reports.
- The client project comment recommends Bannerlord `1.3.14`, while dedicated staging explicitly refers to required Bannerlord `1.4.8` SandBox runtime files. Treat the supported game version as unresolved until installation metadata, assembly versions, and a runtime matrix are checked together.
- Generated packages and copied DLLs can outlive the source that produced them. Never infer source state from `dist/`, module `bin/`, root DLL copies, or ZIP files alone.
