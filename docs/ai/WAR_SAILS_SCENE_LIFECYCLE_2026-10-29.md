# War Sails replica scene lifecycle — 2026-10-29

CURRENT 2026-10-30: [visual provenance investigation](WAR_SAILS_VISUAL_PROVENANCE_2026-10-30.md)
supersedes the unresolved-owner notes below. r82's traced failed-constructor native
allocation exactly matches the faulting rbx. The original northern_round_shield
is ready but has no ItemComponent, while its full exact mirror exists. b21 corrects
component admission and partial-construction cleanup; both builds/all27 c15/15
compiled checks pass. Fresh r83 native acceptance is pending, background rendering
preserved. Read actual r83 records; no native stability or full Goal completion yet.

2026-10-30 handoff: read HANDOFF_2026-10-30_NAVAL_CRASH.md for the authoritative
source/installed/acceptance boundaries and continuation instructions. Latest
saved-dump handle/finalization inspection is inconclusive; no creation ownership
or native field semantics are established. No product change/new native run.

## CURRENT: r81/b16 also failed; both experiments removed; no test pending

Read `artifacts/runtime/naval-retirement-r81-20261029/FAILED.md` first. The user
reports immediate crash after loading. It matches the same native+0x5B1276
TickVisuals null read, despite deferred actor reset. That experiment is therefore
reverted, together with the already rejected focus/background veto. Do not retain
either as a proven solution. Native visual allocation/ownership remains open.

r81's failing MBAgentVisuals wrapper has no incoming object references in a broad
saved-heap scan; GC handles/native ownership and omitted pages are not ruled out.
Installed AgentVisuals constructor allocates data.AgentVisuals before Refresh and
before returning the managed owner, exposing a possible partial-construction leak
path. No retained managed creation exception proves that path actually failed.
Next is stronger creation/cleanup/global-scene contract evidence, not another
blind release-mode/water retry. MissionScreen already has a PreDisplay route.

r81 CLOSED/RestoreModules at2026-10-29T21:53:02.6720410Z: all12 preimages restored,
all7 configs match, all game/debugger roles gone. Both new dumps retained/verified.
Installed client returns to b9 (still known-unstable for Naval). Current source
b17 rollback builds and all27 naval-c12 PASS; compiled readiness/no-focus-veto
check PASS. b17 is not installed or a new native fix. No next manual test/game
launch requested. No ZIP/commit/push. Full Goal remains ACTIVE and incomplete.

## Latest: b15 rejected, b16 restores background rendering and defers retirement

r80/b15 FAILED after recruiting/moving, and its OS-focus rendering veto caused
an unacceptable user-visible regression: no background host movement. That veto
is removed in source. Keep background fidelity as a requirement; do not redefine
the Goal around a stopped background scene. Read r80/FAILED.md before continuing.

The fourth crash matches native+0x5B1276. Saved ClrMD state shows a ready active
view with IsFocusLost=false, no pending reconciliation and zero static ticked
meshes. The faulting native address exactly matches an MBAgentVisuals wrapper
that is absent from the four visuals currently owned by map parties. This is
stronger lifetime evidence than a focus correlation, but the prior model owner
and native field semantics remain unknown. r80 data receipt continued normally.

b16 restores background updates and uses public AgentVisuals.ResetNextFrame for
retiring map actors, after managed ownership is detached and valid entities are
hidden. This matches the installed detached CharacterTableau API; it also covers
partial creation failure, visual replacement, hidden/removed parties, animation
recreation and release. No native offsets/global scene state/water creation is
changed. Crash causality and native survival remain unproven until r81 acceptance.

Both b16 builds/all27 naval-c11/twelve b16 compiled checks PASS. Client SHA256
A54E46816ABC17949297A7ED7EDD26C1B1F14B7182C173579CE623428378490C,
MVIDb41570f0-a3ee-4a8b-9728-f9574ea396de. Dedicated unchanged.
r80 CLOSED/all12 preimages restored/all7 configs match/no owned IDs remain.
Fresh r81 `artifacts/runtime/naval-retirement-r81-20261029` OPEN/INSTALLED12 files,
seven new config preimages. Campaign94992 started2026-10-29T21:33:56.6826378Z.
User asked to repeat recruiting/leaving and verify background movement. Do not
infer native success from code checks or a launched campaign. Water, Training
Field, prolonged stability and DLC land battle gates remain open. Goal ACTIVE.

## Latest: r79 failed; native-window focus boundary added in b15

r79 b13 loaded/rendered Main_map but MP23464 crashed after the user switched to
the chat. OnGameWindowFocusChange:False is logged at22:31:34.881 local. The new
dump matches r77's native+0x5B1276 TickVisuals null read, not r78's worker crash.
Current view/loading checks alone are therefore insufficient. Read
`artifacts/runtime/naval-lifecycle-r79-20261029/FAILED.md` for full evidence,
retention hashes, debugger limitations and cleanup. r79 CLOSED/all7 configs match;
all12 original files restored. No game/debugger roles remain from that run.

Installed MissionScreen tracks OS focus separately through public IsFocusLost.
b15 uses this signal before scene startup, menu/render/input work, clears owned
pending tick entities while unfocused, defers network-driven native model changes,
and reconciles latest catalog/dynamic stores after return without requiring a new
visible-entity revision. Base/Naval and all battle-to-map returns use this shared
path. State reception continues; normal foreground visuals resume. This is a
concrete missing lifecycle boundary, not proof of the native field's identity or
the complete crash cause.

Both final b15 builds, all27 naval-c10 contracts and twelve b15 compiled checks
PASS. The compiled real-view test confirms the installed focus-loss barrier with
no game/scene construction. Native survival and resumption remain open. Client
SHA25670948286201CFBA1D3826C6D2216225940AC27281AE476E1EF4262260F3BA3B9,
MVID7cb76d2a-c2e0-40a7-ac52-cc38aa927a3d; dedicated unchanged.

Fresh r80 `artifacts/runtime/naval-focus-r80-20261029` is OPEN/INSTALLED: twelve
files verified and seven new config preimages saved. Campaign89204 launched
2026-10-29T20:51:49.7385473Z. User asked to load Naval save, start/join through the
working shortcut, switch between MP/chat and check camera/models on return.
Do not infer acceptance from a running campaign or a pending question. Read actual
r80 loaded identity/cleanup records before further action. Black water, Training
Field, long lifetime and broader DLC gates remain incomplete. Goal ACTIVE.

## Scope and evidence limits

The active Goal remains unfinished. r78/naval-b10 is rejected and rolled back;
never reinstall its water initializer. It introduced an immediate native-worker
crash while loading the replica. b9 can load the Naval map but has a separate
delayed native crash and is not a stable DLC release. Neither black water nor the
missing Training Field model has been repaired by this lifecycle correction.

Two retained, hash-verified WER dumps are in
`artifacts/runtime/naval-water-r78-20261029/evidence/preserved-wer`. Existing r77
and r78 reports retain the exact fault/stack/module identities. r79 offline
debugger reports confirm the first exception's function bounds and managed call
chain, but the WER minidump lacks the relevant native object and managed view
heap pages. Reading them returns absent memory / invalid object. Do not infer
the native field at +0x8B8 is a camera, water renderer or mesh from its offset.
No private native symbols identify it. No live process was attached or dumped.
The two exact task-owned debugger shells and their engine children were closed.

## Concrete defect found independently of crash attribution

`UI/CoopCampaignMapPrototypeMissionView.UpdateRenderReadiness` previously latched
`_mapSceneReadyToRender=true` permanently after any successful view/scene probe.
A later failed/unready probe, inactive layer or another top screen did not revoke
that historical permission. The replica also lacked the stock loading-work route
before `MBMapScene.TickVisuals`.

The installed `SandBox.View.Map.MapScreen.TickVisuals` first tests
`Scene.IsLoadingFinished`, advances pending entities and returns while loading,
and clears pending work when visual ticks are disabled. `AfterTick` invokes it
only while that map screen is the top screen. This is a concrete current-state
contract; the earlier replica's one-time readiness latch does not implement it.

The correction applies to both base and Naval map scenes, independent of the
battle type that precedes or follows the replica:

- Revoke readiness before every probe/early return; require current view/scene
  readiness, active non-finalized layer, the owning top mission screen and
  finished scene loading.
- Select one current-frame action: no work without ownership or after probe
  failure; advance loading on the active owning view; clear pending work on
  inactive/unready presentation; update visuals only with current admission.
- A later recovery resumes normal visual updates. This does not permanently
  disable rendering or hide incomplete DLC content.
- The ready log follows actual combined admission. Detailed native terrain/camera
  diagnostic reads now require explicit verbose mode **and** current readiness.
  Ordinary startup logs use already-known flags and elapsed time only.
- No water renderer is created, no additional `Scene.Tick` is added, and no
  global allocation scene, native version check or DLC gameplay manager changes.

`Infrastructure/CoopCampaignMapVisualTickPolicy.cs` contains the detached action
selection. `VisualTickReadinessTests.cs` covers first loading, resource reload,
another top screen, recovery, failed probing and teardown, plus all 32 admission
combinations. This deliberately checks loading progress and resumption as well
as rejection; suppressing every update would not satisfy it.

## Validation and native gate

naval-b12 built both roles; all27 naval-c8 contracts passed. The final b13 also
guards readiness-stage logging and native diagnostic reads. Its isolated client
and dedicated builds pass; installed inventories were unchanged by compilation.
All twelve compiled checks pass under `artifacts/runtime/naval-b13-managed-checks`,
including `Test-MapVisualReadiness.ps1`. That check invokes the real compiled
view's null-layer early return after setting historical readiness true, verifies
revocation, and exercises the compiled detached policy. It constructs no scene,
camera, renderer or game. The first check-script attempt had a PowerShell array
expression parsing error; corrected parenthesized Boolean arguments pass.

Final client SHA256:
`430AD8CBA4C9220CC75A34F57CE9E2599B7696EF977667AA2A589A51F88AE84D`.
Dedicated remains:
`AE53B5A70A2AFE27ED64C6D57F6B18EC661123FE81874913F678479D6FF07334`.

Final all27 naval-c9 contracts also PASS after the diagnostic-read guard change;
`git diff --check` is clean. No line-ending-only rewrite was introduced.

None of these results proves either native crash repaired. The next bounded
native run must first verify successful map loading, at least ten minutes of
normal visual updates, and recovery after options/window changes. Stop on a
crash and preserve evidence; do not ask for repeated speculative water retries.
Base map/battle return, aftermath and DLC land battles remain separate gates.
Do not mark the full Goal complete or distribute a tester archive from this stage.

r79 staging helpers are private evidence under
`artifacts/runtime/naval-lifecycle-r79-20261029`; preparing those scripts alone
does not install or start the product. Read its deployment/launch/cleanup records
to determine the actual runtime state rather than assuming a launch occurred.

r79 is now OPEN/INSTALLED: twelve files verified and seven fresh configuration
preimages retained. Campaign92628 started at2026-10-29T20:26:30.6024481Z. Its actual
loaded MVID is70679cd1-7c5f-44c6-86df-599dc45df7f8 and installed SHA matches b13.
The user was asked to load the preserved Naval save and start/join via the working
shortcut. The campaign reached the main menu; no client/scene acceptance follows
from that. Water/Training Field remain visibly incomplete. Do not overwrite this
run's fresh preimages with earlier test baselines. Preserve Steam and saves.
