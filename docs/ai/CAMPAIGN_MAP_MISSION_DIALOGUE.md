# Standalone campaign mission dialogue replication

## Host gaze candidate (2026-10-11, r12 in progress)

Status: r12 is now CLOSED; heading anchor retained for existing links.

Source now captures Agent.LookDirection for admitted standalone actors and sends
the world-space unit vector with each framed pose. Native MissionConversationCameraView
MakeAgentLookToSpeaker/MakeSpeakerLookToListener assign this property from the
normalized world-space eye-height position difference. View.AgentVisuals.SetLookDirection
forwards to native visuals; stock map tableau passes a world-space camera/eye
difference. This supports using the host target direction, not treating the
GetCurrentHeadLookDirection output as an equivalent target.

Only owned standalone visuals receive the transported target after their frame
update. Their former one-time camera look override is removed; map-tableau
camera look remains unchanged. Physical battle/hideout/settlement mission
admission, agents and camera controllers are untouched. No diagnostic probe was
added. The read-only native getter and visual setter are runtime-sensitive:
target replication does not prove identical head interpolation or facial motion.

Protocol37/bridge32/catalog8/tableau5/pose4 requires coherent three-role deployment.
Four framed actors now require at most901 bytes (48 additional direction bytes);
the derived bridge bound is1204 Base64 characters. The unframed map maximum
remains473 bytes and forbids gaze fields. Finite unit-vector validation, deep
copying, descriptor/revision/sequence binding, old-schema rejection and truncation
checks cover the new field. cmp-b91 passed both isolated builds with installed
inventories unchanged; cmp-c98 passed all24 contract projects, both terminal
exit0. Initial restricted cmp-c97 failed on CIM access before supplying test
evidence. Results reside under the standard Automation/cmp-{b91,c98} run roots.

Fresh r12 staged cmp-b91 coherently in all three roles, retaining cmp-b90
preimages. Loaded MVID and installed hash checks passed. User completed Login
and joined; automatic replica map entry and Tristania plus two visible guards
rendered. Paired evidence02/03 and04/05 proves greeting/options and subsequent
text synchronization. The MP trace at22:42:42.941 records three applied actors;
pose messages continue through22:47:18. This is bounded native rendering and
transport evidence, not exact gaze interpolation, facial motion, four-visible-
actor or speaker-change acceptance. User manually compared the fighters and
reported no visible motion difference between campaign and multiplayer in this
scene. This is bounded user visual/motion acceptance, not measured exact phase
parity. Agent completed the current Pendraic topic and farewell; MP automatically
returned to the map (06), wheel zoom detached the camera (07), and the return
button restored host-camera mode/framing (08). All three checks passed on cmp-b91.
Join again logged a swallowed IntermissionVmCrashGuardPatch ArgumentNullException;
do not call this an error-free join. Campaign pause recurred when switching
foreground windows, with cause unestablished. Evidence is in
`artifacts/runtime/mission-dialogue-r12-20261011`. Final cleanup confirms all22
recorded identities absent and all seven configurations matching preimages.
Only the inspected dedicated startup-map change required restoration. cmp-b91,
eight screenshots, final logs, test saves and prior module preimages are retained.

Documentation impact: this section owns the candidate/evidence; README and
BUILD_TEST_DEBUG point to its incompatible protocol boundary. No architecture or
physical battle flow changed. Native acceptance must compare speaker/guards and
speaker changes, then close/reopen and map recovery; existing battle regressions
remain required for overall Goal acceptance.

## r11 bodyguard regression (2026-10-11, closed)

Fresh `artifacts/runtime/mission-dialogue-r11-20261011` staged cmp-b90 after
cmp-c96 passed all24 contracts. Campaign8420, campaign-menu-owned dedicated25688
and exact-desktop-shortcut MP16100 loaded matching candidate identities. Safe
Mode No and automatic map entry passed. First campaign startup preceded Steam
readiness and exited with the Steam API warning; it is not feature evidence.

Agent-selected Tristania's party opened the forest dialogue. Paired03/04 show
speaker, both guards, matching greeting/options and background. Paired05/06
confirm text advancement; host farewell returned MP automatically to the map07.
The pose trace at22:19:04.100 lists THREE accepted actors. Thus this is a
three-visible-actor bodyguard regression, NOT four-visible-actor native acceptance.
Do not infer four captured actors merely from four agents spawned by native code:
capture filters AgentVisuals.GetVisible. Exact animation-phase parity remains open.

Unexpected campaign pause recurred after switching windows. User denied opening
it; focus-loss correlation was observed but cause is not established. Join logged
swallowed IntermissionVmCrashGuardPatch ArgumentNullException before map load;
this run is not error-free lobby acceptance.

Run closed: cleanup.json confirms all23 recorded identities absent and all seven
configuration hashes restored/matching. Initial cleanup stopped on an identity
check during exit; fresh elevated inspection found no recorded processes, then
the inspected dedicated startup-map change was restored. cmp-b90 modules,
preimages, seven screenshots, final logs and test saves retained. No system-wide
stability claim. Four-visible-actor acceptance and other scene/weather coverage
remain open.

## r10 photo-atmosphere observation (2026-10-11, run in progress)

This supersedes the not-deployed status of the candidate below. Fresh run
`artifacts/runtime/mission-dialogue-r10-20261011` staged cmp-b88 with seven module
and configuration preimages. Campaign15708, menu-owned dedicated6924 and shortcut-
launched MP7032 have verified loaded identities. Do not reuse these process IDs
without checking the retained path/start identities against current processes.
MP Safe Mode No was selected; user-assisted join reached the replica map.

Party/Iara/Talk rendered the companion greeting in both clients. Paired evidence
02/03 shows substantially closer lighting than r9, but does not establish exact
1:1 parity. Host text advancement and both response choices replicated (04/05).
An injected MP continuation click did not advance the host; unreliable injected
input means that negative observation alone is not read-only enforcement proof.

Host log4357 selects `TOD_photo_01_00_SemiCloudy`. MP log8862 records native-photo
selection with preset/live time2.23680329, stormFalse and sun direction
(-0.324979663,0.126564533,-0.9372138), for conversation_scene_green_valley.
Visual readiness completed at81 ms. These are named input/readiness observations,
not complete native render-state measurements or general performance acceptance.

The user completed manual comparison and reports no visible difference between
multiplayer and campaign in this Iara dialogue. Record this as bounded user visual
acceptance, not all-scene/weather or pixel-exact acceptance. Host selection of
the leave response, followed by continuation, returned MP to the replica map
with host-camera mode visible (06). Post-return wheel zoom detached the camera
(07), and the return button restored host-camera mode and framing (08).
Reopening Iara rendered a second greeting in both roles (09/10); host closure
again returned MP to the map (11). Retained final logs identify the repeat as
conversation_scene_forest, with the same photo preset/time and host sun direction
(0.167060778,-0.306139,-0.9372138). The pair is agent-observed evidence, not a
second user acceptance or all-weather claim. Final log line numbers differ from
the live observations above; locate by timestamps21:50:43 and21:57:22.

Run closed: cleanup.json confirms all23 recorded identities absent and all seven
configuration hashes match their preimages. The initial cleanup stopped safely
after process exit because engine_config was rewritten on exit; reviewed
physics_tick_rate20 to90 and dedicated startup-map changes were then restored.
cmp-b88, module preimages, logs, eleven screenshots and test saves are retained.
Physical
battle regressions, additional scenes/weather and full visual acceptance remain
open. This is not a system-stability or mobile-monitoring causal result.

## Post-r10 coverage audit: ordinary bodyguard dialogue (2026-10-11)

Post-r11 animation audit: mission capture already transmits the host's actual
`actor.ActionSet.GetName()` plus channel-0 action, progress and speed; replica
creation resolves that named action set explicitly. The monster-name difference
therefore does not establish use of the wrong default action set.

A separate source-verified fidelity gap remains: replica Create calls
`SetLookDirection(camera.Position - eyePoint)` once for every actor, in both map
and standalone contexts. Native MapConversationTableau does target its camera,
but ConversationMissionLogic.SpawnCharacter calls `SetLookAgent(Agent.Main)` for
guards/player. The current pose contract carries no look vector and cannot
reproduce subsequent host gaze changes. This is source evidence, not a measured
visible failure in r11. Do not replace native gaze with an inferred camera target.
Retained Agent exposes LookDirection, and MBAgentVisuals exposes
GetCurrentHeadLookDirection; these are not proven interchangeable coordinate or
animation semantics. Verify their native call sites before selecting a capture
source. Any implementation must preserve the map branch, bind samples to the
existing actor/descriptor identity, validate finite vectors, revise wire bounds
and schemas coherently, and apply only to owned passive visuals. Physical battle
agents and hideout/settlement admission must remain untouched. No runtime native
getter or behavior was added by this audit.

Post-r11 resource audit: retained native ConversationMissionLogic uses
FaceGen.GetMonsterWithSuffix(race, "_settlement") in both SpawnOtherSide and
SpawnCharacter (therefore guards/player too). Retained MapConversationTableau
uses GetBaseMonsterFromRace. Current replica Create uses the latter for both
contexts. Installed Native/ModuleData/monsters.xml human_settlement inherits
human and overrides hit_points40, walking_speed_limit1.4 and flags; this evidence
does NOT establish a visible mesh/skeleton difference or explain an animation
defect. A future correction must branch only on the admitted MissionScene context,
preserve base-monster map behavior, and leave physical battle spawns untouched.
No runtime/source behavior changed during this audit. Do not report the model
selection difference as a reproduced visual bug. Four-actor native coverage must
likewise count visible actors, not merely agents spawned by native code.

Implementation follow-up: source now permits four actors only when MissionScene
is present; map capture, descriptor validation and unframed poses retain a
three-actor limit. Framed pose cap853 includes four maximum-length action names.
Protocol36/bridge31/tableau5/pose3 rejects mixed installations; catalog8 is unchanged.
The native message buffer follows the codec cap. The host bridge's stale hardcoded
684-character cap was also corrected to the codec-derived Base64 bound1140;
that stale limit could reject earlier legal three-actor mission poses as well.
Tests exercise maximum four-actor descriptor/pose/bridge round trips, fifth-actor
rejection, old schemas, frame binding, truncation and all missing-readiness masks.
The original map maximum remains473 bytes. Existing mounted/child/context guards
remain; this is not mounted or general conversation acceptance.

cmp-c95 passed all24 projects before the final bridge tests were added. Final
cmp-b90 passed both isolated builds with installed inventories unchanged.
cmp-c96 passed all24 projects on the final source, with terminal exit0.
cmp-b90 was subsequently deployed in r11 above; native four-visible-actor
verification remains open. No hot-path diagnostic was added.
Documentation impact: this section is canonical; README and BUILD_TEST_DEBUG
summarize the incompatible candidate and validation boundary. Physical battle
flows/ownership are unchanged, so their architecture documents are unchanged.

Pre-candidate source-verified gap, not a runtime reproduction: tableau MaxActors=3
also bounds mission capture, pose encoding/decoding and visual readiness.
Retained installed ConversationMissionLogic.SpawnOtherSide calls SpawnBodyguards
unless NoBodyguards or the speaker is a player companion. SpawnBodyguards creates
two guards when roster conditions permit. Together with SpawnPlayer and the
speaker, this requires four human descriptors when all are visible. Current
capture rejects the fourth visible actor and resets the entire visual descriptor;
the successful companion checks do not cover this branch.

Do not fix only MaxActors: mission pose maximum is101 header/camera/count bytes
plus188 bytes per actor (128-byte action identifier, four-byte text length,
progress/speed and48-byte frame). Four actors require853 bytes, exceeding the
current768-byte pose cap. Map pose without mission frames must retain its existing
three-actor bound. Audit the network and bridge bounds before changing versions.
Readiness masks currently derive from MaxActors, and must be tested for a missing
fourth actor as well as full readiness.

Adjacent gaps remain: mounted speakers/nonhuman agents and children are rejected;
host standalone actors use the race's _settlement monster while the replica
resolves its base monster. These are distinct from the lighting correction and
must not be claimed fixed by count changes. Field/village/siege/relief/sally-out,
hideout and settlement physical mission admission remains intentionally separate;
do not remove those guards to admit ordinary standalone bodyguards.

Original implementation gate (source candidate above now addresses it): bounded four-human standalone capture, descriptor and
pose round trips at maximum size, over-limit rejection, descriptor/pose binding,
four-actor readiness and unchanged map limits; then both isolated builds and a
fresh host/leader/two-bodyguard runtime pair. No new runtime launch or code change
was made during this coverage audit.

## Native photo-atmosphere candidate (2026-10-11, not deployed)

Historical pre-deployment checkpoint; r10 above is the current runtime status.

Source now uses `Scene.SetPhotoAtmosphereViaTod` only in the detached standalone
land-conversation branch. The original map tableau still calls
`SetAtmosphereWithName`; physical mission initialization is unchanged.
Host capture supplies initializer TimeInfo.TimeOfDay and the explicit
NauticalInfo.IsInsideStorm flag, separately from live scene TimeOfDay. Rain is
not a storm proxy. Low-altitude/naval selection and noncanonical storm values
are rejected rather than sent through the wrong resolver.

The native-selected sky/fog/color/exposure preset remains intact. The old
Sun/Ambient/Fog/PostPro weather-model projection is removed from capture,
transport and application. The admitted host scene's read-only GetSunDirection
supplies a finite unit direction, applied only to the owned replica scene.
Existing winter/dryness/temperature/humidity/rain/snow/wind projection remains.
Do not infer complete season/resource/render-state parity from this candidate.

Wire versions are protocol35/bridge30/catalog8/tableau4/pose2; all three roles
must be coherently rebuilt and staged. Old tableau3 payloads are rejected.
Tests cover every scene field, independent selection/live time, storm/calm
roundtrips, canonical storm bytes, nonfinite/degenerate directions, truncation,
map-branch preservation and removal of blanket lighting overrides.

Scenario audit: the existing exact Conversation mission/behavior/view and four
land-scene admission checks remain in force. Field/village/siege/relief/sally-out,
hideout boss and settlement-mission conversations cannot enter this adapter just
because their mode is Conversation. No battle constructor, native patch or host
scene setter is introduced. The extra getter and owned-scene preset application
remain runtime-sensitive; builds and source guards cannot establish their native
safety or visual parity. Default-off bounded trace now reports selection inputs,
not obsolete weather-model lighting. No additional diagnostic native reads.

Documentation impact: this document is canonical for the correction and evidence;
README and BUILD_TEST_DEBUG summarize versions and validation state. Architecture,
CODE_MAP and physical RUNTIME_FLOWS are unchanged because ownership and topology
are unchanged. Installed cmp-b87 remains until separate validated staging.
Validation: cmp-c94 passed all24 canonical contract projects (0 failed), including
4291 mission-scene and71 visual-lifetime checks. cmp-b88 passed both isolated
Release builds with installed module inventories unchanged; both commands exited0.
The initial restricted focused run stopped on temporary-directory access, not a
passing result; the full authorized run above supplies the acceptance evidence.
Results are under `%LOCALAPPDATA%/Temp/CoopSpectator/Automation/cmp-{c94,b88}`.
No game was launched or candidate deployed. Native lighting/background parity,
resource refresh, repeated dialogue and close-to-map regression remain unverified.

## r9 descriptor observation (2026-10-11, parity FAILED, run closed)

This supersedes earlier not-staged/not-native-verified cmp-b87 notes below.
Fresh `artifacts/runtime/mission-dialogue-r9-20261011` staged cmp-b87 after
cmp-c93 all24 contracts and both isolated Release builds. All three loaded
identities matched. Dedicated startup was campaign-menu driven; MP used the
prescribed shortcut and Safe Mode No. Login/server join were user-assisted.
Map rendered in host-camera mode, and Party/Iara/Talk rendered in both roles.
Paired screenshots02/03 still show darker/bluer MP lighting and different
background appearance. Greeting text matched; this is NOT visual 1:1 acceptance.
Host dialogue closure returned MP to the map (screenshot04); post-return input,
answers in MP, battles and reconnect were not verified in this run.

The bounded trace now proves receipt of scene `conversation_scene_forest`,
preset `semicloudy_field_battle`, time2.23680329, sun brightness3.06582475,
environment0.00462020375, sky1.10711265, Mie19.5596542, exposure-2.11840153..1.98151922
and middle gray0.1. Active host/dynamic bridge snapshots and the live MP log were
retained alongside images. These are descriptor inputs, not effective native
render-state measurements. The selected preset exists under
Native/Atmospheres/Interpolated; it contains time curves, not a single constant
lighting state. Public Scene wrappers inspected so far expose no whole
AtmosphereInfo application method. Do not assume SetAtmosphereWithName plus
individual setters reproduces InitializeMission's initializer semantics.

Run closed at21:20 local: all22 recorded identities absent and all seven
configuration hashes match preimages (`cleanup.json`). Only inspected changes
to safely_exited and dedicated startup map entries required restoration.
cmp-b87 remains installed with module preimages retained; test saves preserved.
No system-stability or mobile-monitoring causal claim is made. Next work must
resolve native atmosphere/resource or render-target semantics rather than
repeat rejected preload, waiting or bare post-effect-profile experiments.

### Post-r9 static investigation: color grading remains a hypothesis

**Superseded as the leading hypothesis by the concrete preset-path evidence
below.** Color grading is one preset component, not an independently proven cause.

Installed `DefaultMapWeatherModel.GetAtmosphereModel` populates
`InterpolatedAtmosphereName` and numeric weather fields, but not `AtmosphereName`.
`AtmosphereInfo.IsValid` checks only the latter. This managed serialization guard
does not establish how native `InitializeMission` consumes the initializer;
do not infer that the host ignores its weather or replace native initialization.
The replica currently collapses the two names into one `SetAtmosphereWithName`
argument, then applies numeric climate setters.

The selected interpolated XML includes a time-varying `colorgrade_texture` curve
(including `cg_blue_20`), while the forest scene XML names `color_grade`.
Neither asset value proves the active host/client color grading. The exact curve
time mapping and native selection/blending semantics are unverified.
Installed `PostProcessInformation` has only MinExposure, MaxExposure,
BrightpassThreshold and MiddleGray: no target exposure or color-grade field.
Scene wrappers expose color-grade index/names/blend APIs near photo-mode methods,
but their ability to observe normal mission interpolation is unestablished.
Do not transport a guessed index or apply a hand-picked warmer texture as a fix.

These findings are read-only source/installed-library evidence, not a new build,
runtime measurement, lighting correction or system-stability result. Any next
native diagnostic must be separately bounded/default-off and account for the
previous desktop failures. No physical battle initialization is changed.

### Native preset selection resolved (2026-10-11, correction not implemented)

The retained host log gives a stronger observation than the initializer:
`artifacts/runtime/mission-dialogue-r9-20261011/evidence/final-rgl_log_11128.txt:3559`
records `Chosen photo atmosphere: TOD_photo_01_00_SemiCloudy` at21:15:47.422,
immediately after the forest scene loads. Replica descriptor line5797 names
`semicloudy_field_battle`. Therefore the previous assumption that the initializer's
interpolated name identifies the actually selected host preset is false for r9.

Offline inspection of installed `TaleWorlds.Native.dll`, SHA256
`2A5E0E0B15513EBB7052D747A621B82C50EE834553EBA283B33BBBC33766E12A`, links
managed engine enum1033 to RVA0x4DE600 (`SetAtmosphereWithName`) and enum1093
to RVA0x4DF130 (`SetPhotoAtmosphereViaTod`). These mappings come from native
registration at0x395F13 and0x3963C3, respectively, not guessed function names.

- Named setter calls name lookup0x1B30A0 over the ordinary atmosphere registry
  (record stride0x358); a missing name returns null. It then calls0x1BE1C0,
  whose null branch skips copying a new preset. It does not load an interpolated
  XML file here. Runtime registry contents are not directly measured.
- Photo setter truncates the supplied time to an integer and calls0x1B3180,
  then copies/applies that result. Selector0x1B3180 mirrors hours above12 via
  `24-hour`, enumerates eligible photo presets via0x1B3E00, and selects nearest
  preset time. This is not continuous interpolation of the field-battle XML.
- Native atmosphere-construction function0x1B34F0 uses the same selector for
  the non-low-altitude branch and emits the exact `Chosen photo atmosphere`
  message observed in the host log. Marshaled AtmosphereInfo offsets corroborate
  selection inputs: +0xCC TimeInfo.TimeOfDay, +0xC4 NauticalInfo.IsInsideStorm,
  +0xB8 NauticalInfo.CanUseLowAltitudeAtmosphere. It applies a limited set of
  overrides, not the replica's blanket Sun/Ambient/Fog/PostPro projection.
- Enum1125 maps to0x4DA820: `SetTimeOfDay` writes one field and increments a
  revision; it does not itself perform the photo-preset selection.

The chosen photo asset contains middle_gray0.183, sky_brightness10,
min/max_exposure-7.232/-3 and target_exposure-4, unlike r9's transmitted model
values0.1/1.107/-2.118/1.982. Those asset comparisons explain why blindly applying
weather-model numbers cannot claim host parity; they are not measurements of
all effective host values.

Next correction should reproduce the native photo selection only for the admitted
standalone land Conversation scene, transport its actual initializer selection
inputs (do not infer storm from rain), and preserve only verified native overrides
or read-only host scene values. `SetPhotoAtmosphereViaTod` is now evidence-backed
as a candidate, superseding the earlier speculative rejection; it is not yet a
verified complete replacement. Check sun direction, season, climate and resource
refresh semantics before calling parity achieved. No native addresses may enter
production code, and no host or physical battle scene may be mutated.

Read-only inspection tooling is local under `artifacts/tools/inspect-native.py`
with pefile2024.8.26/capstone5.0.9 in a task-local directory. It parses file bytes,
never loads the inspected DLL into a process. Immediate-byte matches are only
candidates until decoded at a confirmed boundary. No game launch, new build,
deployment, driver change or system-stability test occurred in this investigation.

## r8 standalone preload result (2026-10-11, parity FAILED)

Post-r8 source removes the one-time PreloadForRendering experiment and replaces
its presence guard with an absence guard. r8 proves execution but not a parity
benefit; removal is not a black-screen fix or proof of causality. Existing bounded
lighting transport, actor/target readiness and default-off probe remain unchanged.
No field/village/siege/relief/sally-out/hideout physical mission path is changed.
Installed cmp-b86 still contains the experiment until a separately validated
candidate is deployed. Source removal now passes focused contracts and both
isolated builds below; native gates remain unverified.

2026-10-11 resumed validation: the focused
`Tests/CoopCampaignMapPrototype.ContractTests` executable passed with
`COOPSPECTATOR_REPOSITORY_ROOT` explicitly set to the repository directory
(`dotnet run --no-restore --project
Tests/CoopCampaignMapPrototype.ContractTests/CoopCampaignMapPrototype.ContractTests.csproj`).
The restricted attempt reached tooltip target admission and failed on access;
the approved elevated retry exited 0, including 69 visual-lifetime and 4748
mission-scene checks. This verifies the preload-removal source guard, not visual
parity, both product builds, or the complete 24-project inventory. No product
launch or deployment occurred during this validation.

Follow-up isolated compile root `artifacts/compile/resume-20261011-a` passed both
Debug projects with `CoopCompileOnly=true`, explicit absolute output root and
single-worker `-m:1`: client77 warnings/0 errors, dedicated49 warnings/0 errors.
The initial restricted client attempt failed NuGet DNS resolution (NU1301);
approved retry and dedicated compilation exited0. No staging occurred. Installed
client SHA256 remains08AC807D6FA94F0A75BDC031BD2C93714CDC5169331F7F3A3A61353CA9990CB6
(cmp-b86). This direct build is not a complete installed-inventory audit or a
full24-project run. Product rendering correctness remains unverified.

Additional system incident evidence: Application Error events record git/conhost
failures at19:44:03, dwm.exe in Intel igd10um64xe.DLL31.0.101.4146 at19:44:06,
and EventLog/Git failures around19:50, followed by boot19:53:31 (local time).
The user supplied a ChatGPT.exe exception0xe0000008 photograph from the black
screen. Root cause remains unknown. Current automatic pagefile management and
15GiB pagefile after reboot do not establish memory availability during failure.
User authorized resuming goal work with mobile monitoring disabled; this is not
a controlled causal exclusion of the mobile application. No driver/settings
change or intentional black-screen reproduction is authorized by this note.

Post-run safety note: user reported a black/unresponsive desktop after game
closure. Windows events show multiple application failures from19:25:59 local,
including dwm.exe/CoreMessaging.dll at19:26:08 and NVIDIA Overlay at19:26:38;
reboot19:29 follows verified cleanup19:25:09. Root cause is unknown; do not infer
mod or automation innocence/causality, or claim system-stability acceptance.
Private r8 README records the bounded event audit. Native reruns are deferred
while source-only investigation continues.

Fresh mission-dialogue-r8-20261011 staged cmp-b86/cmp-c92 and verified all three
loaded identities. Campaign-menu server, prescribed MP shortcut and Safe Mode No
were agent-executed; Login/join were user-assisted. Automatic map rendered.
Party/Iara/Talk greeting and options replicated, but paired screenshots02/03 and
04/05 show MP still darker/bluer. The current camera faces rock/grass, so r7's
forest-gap framing is not a controlled before/after terrain comparison.

Preload begins at77ms and completes at78ms, both19:21:59.548; readiness completes
at160ms and later terrain bake entries remain. The call alone is not a parity fix
or full-render readiness fence. Host text/options/close and returned map zoom/
manual detach passed. Guest answer click did not advance host, with the existing
automation-input reliability caveat. No battle/rejoin/full regression claim.

Run closed16:25:09UTC: seven configs restored, no extra configs or owned/product
processes, cmp-b86 retained with recoverable module preimages; test saves preserved.
Exact post-shutdown physics tick/dedicated map changes were inspected and admitted.
A reused PID with a different identity was preserved. Actual stop evidence remains
in cleanup-process-progress.json; final AlreadyExited proves absence only.
Full lighting/terrain acceptance remains open. Do not repeat preload or increase
waits as an established fix; investigate native atmosphere/render-target semantics.

### Post-reboot managed rendering audit (2026-10-11)

Follow-up diagnostic candidate: TraceLightingDescriptor records the admitted
standalone scene/preset and detached time/sun/environment/sky/Mie/exposure values
immediately before SetAtmosphereWithName. It requires the existing explicit
VerboseDiagnostics flag, is capped at12 entries per process and formats values
invariantly. No additional native getter, host mutation, per-frame work or
physical mission change is added. The log proves supplied descriptor values,
NOT effective renderer state; Rayleigh/night values are not invented.
Justification: r8 retained screenshots/logs but no active dialogue bridge
snapshot, so the selected atmosphere and its input values cannot be recovered
from that run's retained evidence. Focused contracts pass (71 visual-lifetime
checks); isolated client rebuild passes77 warnings/0 errors. Not deployed or
native-verified. Remove this investigation-only trace when the ambiguity is
resolved; no production-default logging is introduced.

Fresh canonical validation after this trace: cmp-c93 completed all24 projects
(24 passed,0 failed, full inventory); cmp-b87 completed client and dedicated
Release builds with InstalledInventoriesUnchanged confirmed by the runner.
Both exact runner sessions exited0. This supersedes the focused-only validation
boundary for the candidate, not its missing native acceptance. Windows Computer
Use initialization/list_windows succeeded after reboot; no product window was
present in that observation. Nothing has yet been staged or launched for r9.

Read-only comparison of the retained installed-game decompilations confirms that
`MapConversationTableau.CharacterTableauContinuousRenderFunction` supplies the
replica's `0xFFFFFBFF` post-effect mask. This is the map-tableau profile, not proof
of equivalence to standalone `MissionScreen` rendering. The inspected
`MissionConversationCameraView` changes camera framing and actor look direction;
it contains no atmosphere/exposure correction to copy. `SceneLayer` merely
forwards its explicit post-effect methods to `SceneView`; its inspected
constructor/activation do not establish an equivalent explicit mask.

The current replica widget draws its target with white color, color factor1,
zero hue/saturation/value offsets and ContextAlpha. Source does not show a
hardcoded blue tint; actual compositing and native exposure equivalence remain
unverified. No brightness compensation or guessed native call was introduced.
The known missing initializer fields (NightTimeFactor, RayleighConstant and
SunInfo.MaxBrightness) still cannot be considered transported via the inspected
Scene public setters. Their visual relevance is not established by this audit.
Next investigation must distinguish native atmosphere application from target
post-processing/compositing; do not re-run the rejected preload or bare
SetPostfxFromConfig experiment as if it were a new fix. No product was launched
and no physical battle path was modified for this audit.

Further installed-library inspection: `MBMapScene.LoadAtmosphereData` and
`SetFrameForAtmosphere` are map-scene native wrappers, not an exposed setter for
a supplied AtmosphereInfo. `IMBMission` exposes InitializeMission but no separate
whole-atmosphere setter in the inspected interface. Do not initialize a second
physical mission or call map-specific APIs on the detached conversation scene
merely to obtain lighting. `MBAgentRendererSceneController` is also a native
wrapper, with no managed atmosphere correction to reuse.

Installed SandBox/SceneObj/conversation_scene_forest/atmosphere.xml explicitly
contains rayleigh_constant10, time_of_day9 and daytime sky resources.
DefaultMapWeatherModel.GetRayleighConstant clamps its campaign result to
0.65..0.99. The replica does not explicitly project this field. This is a
concrete initialization difference to investigate, NOT the observed effective
replica value: SetAtmosphereWithName may replace the scene-file default, and
native interpretation/units are not proven equivalent. Never change the value
to10 or an arbitrary campaign approximation based solely on these sources.
SceneInitializationData(true) was freshly inspected: flora nodes and terrain
mesh blending already default true; enabling them again is not a new fix.

## Native investigation (2026-10-09, in progress)

Fresh `mission-dialogue-r1-20261009` staged cmp-b74 from checkpoint0ed6c90;
campaign, dedicated and MP loaded identities matched. Campaign-menu server,
prescribed MP shortcut, Safe Mode No, AC_COOP join and automatic rendered map
succeeded without a console start command.

Party/Talk with Iara opened `conversation_scene_forest`. Paired evidence confirms
greeting, text/options update and HUD replication, but NO 3D scene on MP: its
background remains the map. The host bridge has empty ConversationTableau and
ConversationPose while text is active, locating failure before network/render.
An MP answer click did not advance the host. Host close cleared the replica;
return to map and MP zoom/detach passed. This is partial progress, NOT 1:1 acceptance.
Local screenshots/bridge evidence remain private. r1 cleanup is complete.

A temporary host capture probe preserved admission/scene/actor/battle boundaries.
Both `COOPSPECTATOR_VERBOSE_DIAGNOSTICS=1` and
`COOPSPECTATOR_MISSION_CONVERSATION_CAPTURE_DIAGNOSTICS=1` are required before
class initialization; at most32 distinct outcomes/process reach the host rgl log.
Formatting and detached-sample checks occur only after deduplication; no extra
native reads, scans or host mutations. Existing capture remains native-sensitive.
The probe was removed from source after r3 diagnosis. Field/village/siege/sally-out/relief/hideout
paths remain excluded by exact standalone Conversation behavior/view/scene admission.
cmp-b75/cmp-c80 failed NuGet.Config access, not contract assertions. Reviewed
cmp-b76 passed both isolated builds with installed inventories unchanged;
cmp-c81 passed all24 projects. r1 was closed with seven configurations restored,
no owned/product processes and cmp-b74 retained before fresh r2 staging.

r2 loaded cmp-b76 in campaign32592 (MVIDb80f9f46-aeff-452a-925f-615ff2949249).
The first launch26304 preceded Steam readiness, reported Steam API unavailable and
exited; it is not a mission crash. Party/Talk at12:02:54 local reproduced
`atmosphere-invalid` in a campaign-only diagnosis; no dedicated/MP was needed
because the existing local publisher runs independently. This is not MP acceptance.

Installed-library audit found the semantic mismatch: `AtmosphereInfo.IsValid`
tests only AtmosphereName, but DefaultMapWeatherModel.GetAtmosphereModel populates
InterpolatedAtmosphereName and climate/lighting fields, not AtmosphereName. Do not
call that weather method from capture: it mutates weather state. Relevant DLL
SHA256: Library AB4FF23E94A6F4B6F293D0CB4333CAE731A0499063FE4A8B6C6A3770DE32C4E5;
CampaignSystem 1F8E33E2ED73E6EC653D7629180AFB70649DDC6E5BD1657A802A264EFDA1C3AE.
Private decompilation remains under artifacts/native/mission-dialogue-r2-20261009.

The narrow candidate resolves the already captured named preset, or the existing
interpolated preset when primary is absent; malformed/absent names still reject.
No weather recomputation, battle initializer or lifecycle mutation is added.
Existing scene/climate/actor validation remains. Pure tests cover absent, malformed,
oversized and precedence cases. This only removes the incorrect admission guard;
full interpolated sun/fog/exposure parity remains open. cmp-b77 both isolated builds
passed with unchanged installed inventories; cmp-c82 passed all24 projects.

r2 cleanup restored all seven configuration preimages and left no owned/product
processes. A reused PID belonging to a different identity was preserved.
r3 staged cmp-b77 and waited for Steam readiness before campaign54960 launch.
Loaded client MVID76e42ec0-aba0-4055-82b1-8ce8f650884c matches the candidate.
Party/Talk with Iara now publishes nonempty ConversationTableau (1868 encoded
characters) and ConversationPose (372), without the previous rejection. Evidence
is retained in mission-dialogue-r3-20261009. Campaign-menu dedicated10576 and
prescribed-shortcut MP21972 loaded the verified candidate. Safe Mode No, native
AC_COOP join and automatic rendered map passed. MP was already at Home when
observed: do not claim an agent Login click.

Networked Party/Iara/Talk rendered the actual forest scene and equipped companion
in MP, with readiness at218ms and matching greeting/text/options. A guest Continue
click did not advance the host. Host close cleared the overlay; map zoom/detach
worked. Paired screenshots03/04 and05/06 show significantly brighter MP lighting
than the host, so visual 1:1 FAILED. This is not a complete animation/terrain claim.
Local evidence07/08 covers map recovery. r3 closed at09:27:42UTC: all seven configs
match, no new configs or owned/product processes, cmp-b77 retained. Cleanup first
stopped exact-owned roles, then required inspected hashes for engine physics tick
and generated dedicated map changes. Original stop audit is retained separately
in cleanup-process-progress.json; later AlreadyExited entries are not proof of
graceful shutdown. No test saves were deleted.

## Lighting projection follow-up (2026-10-09, source candidate)

The preset-only renderer drops the prepared initializer's sun, ambient multiplier,
fog and exposure values. Installed DefaultMapWeatherModel explicitly supplies
these alongside InterpolatedAtmosphereName. Source now transports23 additional
finite, range-checked scalar lighting fields in the existing detached scene
descriptor, including ordered exposure bounds. The owned scene applies exact
public Scene setters after the named resource preset; it never modifies host,
map or battle scenes. No new native host read or weather-model invocation is added.
The temporary capture probe is removed. Protocol34/bridge29/tableau3 rejects
obsolete data; catalog8 and pose2 are unchanged, including pose payload bounds.

Affected methods: MissionConversationVisualCapture.TryCapture, MissionScene
validation/read/write, and ConversationTableauView.ApplyMissionClimate. Tests
cover round-trip/nonfinite/bounds/clone/resource identity, old schema rejection,
unchanged map branch and no weather recomputation. Both isolated builds and all
24 contract projects passed as cmp-b78/cmp-c83. Final cmp-c84 also passed all24
after adding direct equality checks for every round-tripped scene property.
Fresh r4 staged cmp-b78 with seven module/config preimages and all three loaded
identities verified. User completed Login; the agent joined AC_COOP through the
native list. Party/Iara/Talk renders the forest scene at night instead of r3's
overbright preset, but paired greeting/options evidence01-04 shows MP remains
darker/bluer than campaign. Lighting 1:1 remains FAILED, not accepted. Guest
Continue did not advance the host; host text/options/close replicated. Evidence05-06
confirms map return, zoom/detach and the main-party tooltip. No battle/rejoin or
complete animation/terrain regression is claimed from this bounded run.

r4 closed at09:51:37UTC: independent postflight confirms seven matching configs,
no new configs, no owned/product processes, cmp-b78 still installed and test saves
preserved. Original cleanup-process-progress.json records actual close/termination;
cleanup.json's later AlreadyExited entries only prove absence. Engine config was
re-inspected after process exit (physics_tick_rate20 to90), and the generated
dedicated map changed battle_terrain_029 to empire_village_o; both exact postimages
were preserved and restored. Do not reuse closed process identities.

Risk/adjacent audit: exact standalone Conversation/view/handler/land-scene guards
remain; field/village/siege/relief/sally-out/hideout admission, physical spawning,
battle initialization and lifecycle are unchanged. Renderer setters are native-
sensitive but touch only an owned scene after descriptor validation. Named-preset
resources remain necessary; unavailable Rayleigh/max-sun and other engine-specific
overrides are not guessed. Complete lighting/terrain/facial/voice parity is still
unaccepted until fresh paired native evidence. No universal atmosphere parity claim.

Documentation impact: this focused record and README updated. Existing test runbook,
build procedure and architecture boundaries are reused unchanged; no new subsystem.

### Post-r4 effect-profile hypothesis (native parity failed)

Installed MapConversationTableau explicitly sets mask0xFFFFFBFF; MissionScreen
instead creates a normal SceneLayer/SceneView. The replica had unconditionally
copied the map mask into standalone mission targets. The next narrow candidate
retains that mask for map dialogues and calls the public SetPostfxFromConfig only
for the owned standalone target. It does not change global settings, the host,
physical missions, wire formats, or light intensities. This is a controlled
hypothesis, NOT a proven cause or accepted lighting fix. Installed View.PostfxConfig
identifies the removed0x400 bit as temporal_filter; the old mask enables all other
bits, not a simple exposure switch. Native InitializeMission's atmosphere application
is not established by managed wrappers. NightTimeFactor/Rayleigh/max-sun remain
unprojected; do not guess setters
or claim this effect-profile change covers them. Source guard checks both branches;
fresh builds/contracts and paired native comparison are required.

cmp-b79 built successfully but cmp-c85 failed a duplicated local declaration in
the new source guard; duplicated view/test additions were removed before restaging.
Fresh final cmp-b80 passed both isolated builds with installed inventories unchanged;
cmp-c86 passed all24 projects. Fresh r5 verified all three loaded identities,
user Login, native AC_COOP join and automatic map entry. Party/Iara/Talk rendered
the native companion scene and greeting in MP, but paired evidence02/03 shows
MP substantially brighter than the host night scene. This hypothesis FAILED
lighting parity. This run did not recheck read-only choices, text transitions or
close/map input recovery; earlier r4 checks are not b80 regression evidence.
Private evidence and bridge copies: artifacts/runtime/mission-dialogue-r5-20261009.
Run closed at10:12:07 UTC: seven configs match, no extra configs, no owned/product
processes. Exact inspected engine physics_tick_rate and dedicated map changes
were restored; original cleanup-process-progress retained. Installed b80 remains
an unaccepted candidate with recoverable module preimages. Next inspect native
atmosphere application and post-effect initialization ordering; do not tune an
arbitrary brightness multiplier or report full 1:1 acceptance.

Post-r5 source removes the rejected SetPostfxFromConfig branch and its
hypothesis-specific source assertion; bounded lighting transport remains.
Installed b80 differs from source until the next staged candidate. No physical
battle, hideout or global effect configuration is changed. Further installed-
wrapper inspection found no whole-AtmosphereInfo application entry in Scene or
Utilities. EnsurePostfxSystem/exposure setters forward to native interfaces;
their reset/order semantics remain unproven. Reordering is not an established
fix. cmp-b81 passed both isolated builds with installed inventories unchanged;
cmp-c87 passed all24 contract projects after removal. No new native acceptance.

### Owned standalone view-shadow admission (2026-10-09, candidate)

Fresh installed MissionScreen.InitializeMissionView explicitly calls
SceneView.SetSceneUsesShadows(true) after SetScene. View assembly SHA256:
FFAAC5ED867CD299F8C14F58FFEE64F86E50D8B197DB5F966759524C3E574BA1.
Private decompilation: artifacts/native/mission-shadow-20261009. The replica
had scene shadow flags but no explicit view-shadow admission. ConfigureTableau
now mirrors this call only for a standalone mission scene; the map-only focused
shadow profile remains unchanged. Native defaults and contribution to the
lighting mismatch remain unproven; this is not an accepted parity fix.

Adjacent-scenario audit: exact Conversation/land-scene/SandBox logic/current-view
gates are unchanged. No physical field, village, siege, relief, sally-out or
hideout mission is modified. Only the owned detached view changes; no diagnostics
or global settings. Risk: additional shadow preparation/render cost. Require
paired lighting, retention, read-only dialogue lifecycle and map recovery checks.
Source guard checks the scoped call and retained map profile. cmp-b82 passed
both isolated builds with installed inventories unchanged; cmp-c88 passed all24
contract projects. Fresh mission-dialogue-r6-20261009 staged cmp-b82 with seven
module/config preimages. Campaign-menu server startup and all three loaded
identities are verified. MP launched through the requested desktop shortcut;
Safe Mode No was selected; user completed Login. Native AC_COOP join and automatic
map startup passed without a console command. Paired companion greeting/options
screenshots show FAILED lighting parity: MP remains darker/bluer, and background
gaps differ. This observation does not identify the underlying cause. The shadow
call alone is not a sufficient parity fix. Host-driven text/options and dialogue
closure replicated; map zoom then switched to manual inspection. An MP continue
click did not advance the host, but input reliability limits this read-only check.
Private evidence: mission-dialogue-r6-20261009/evidence, screenshots 01 through 07.
Cleanup/postflight completed at 2026-10-09T10:36:31Z: seven configurations match,
no new config files, no owned/product processes; test saves preserved. cmp-b82
remains installed as an unaccepted candidate. The first cleanup stopped processes
but halted at the post-shutdown config hash gate; inspected physics_tick_rate
20-to-90 and dedicated map changes were then admitted and restored. Preserve
cleanup-process-progress.json; final AlreadyExited entries do not prove graceful
exit. No full Goal completion or lighting acceptance is claimed.
Documentation impact: this record and README;
runbook and architecture remain unchanged.

### Post-r6 terrain/readiness audit (2026-10-09, read-only evidence)

Installed SandBox/SceneObj/conversation_scene_forest/scene.xscene has terrain
enabled, a 3-by-3 node grid, 100-unit nodes, terrain mesh/grass layers, terrain.bin
and flora.bin. Scene XML SHA256:
BC822DB9984686E62904C7523907F2603EDC7BB9412FB1F67D10BBFBF273A75C.
The renderer's old comment saying the stock tableau has terrain disabled applies
to the map-tableau asset, not this standalone mission scene. Do not infer missing
terrain merely from the screenshot or that comment: r6 MP log27720 proves the
forest scene/atmosphere were read, and terrain bake entries follow scene reading.

The existing target readiness predicate covers configuration, texture submission,
actor skeletons/resources; it does not establish terrain or complete scene readiness.
In r6 it logged readiness-complete at13:31:35.086, while later Bake Terrain entries
occurred at13:31:35.091 and13:31:35.231/237. This proves the readiness message is not
full-scene completion, not that asynchronous loading caused the persistent mismatch.
The scene remains visible until13:33:03.702, so an early sample alone cannot explain
all observed differences. Keep native visual acceptance separate from this predicate.

Managed wrappers expose ContainsTerrain, HasTerrainHeightmap, IsLoadingFinished
and GetLoadingStateName. Their native safety and semantics must be reviewed before
any opt-in bounded probe; do not introduce a hot-path poll or synchronous
ForceLoadResources/CheckResources loop. Scene initialization already uses default
InitFloraNodes/UseTerrainMeshBlending, with physics/navmesh intentionally disabled.
CreateNewScene's first argument controls physics, not terrain. No evidence supports
turning physical simulation on as a rendering fix. No source/runtime mutation or
new game run was made for this audit. Lighting/terrain parity remains open.

### Bounded scene-loading probe (2026-10-11, not native-verified)

Source adds an opt-in observation of the owned standalone scene only, requiring
both COOPSPECTATOR_VERBOSE_DIAGNOSTICS=1 and
COOPSPECTATOR_MISSION_SCENE_LOADING_DIAGNOSTICS=1 before the visual attempt.
It reads ContainsTerrain, HasTerrainHeightmap and IsLoadingFinished on the owning
tick at approximately 0, 2 and 10 seconds, at most three samples per attempt and
twelve per process. Counters advance before native reads; a managed exception
disables that attempt's probe. Cleanup disables it before releasing scene handles.
No loading-state string, resource scan, forced loading, host query or physical
mission mutation is added. It is not a new readiness gate or rendering fix.
Native getters remain potentially runtime-sensitive; managed exception handling
does not protect against a native crash. Enable only for the exact test child,
remove the temporary probe after diagnosis, and retain paired visual evidence.
Existing field/village/siege/relief/sally-out/hideout admission remains unchanged.

cmp-b83/cmp-c89 were interrupted: on 2026-10-11 their manifests had no terminal
outcome or completion time, result directories were empty and runner PIDs absent.
Do not call them passes or reuse their run roots. Fresh cmp-b84 passed both
isolated builds with installed module inventories unchanged; cmp-c90 passed all
24 contract projects, including 4748 mission-scene checks. No new deployment or
native acceptance is claimed. The next native run must scope the two probe flags
to its owned MP child and preserve exact module/config preimages.
Documentation impact: this record and BUILD_TEST_DEBUG; architecture/runbook
unchanged because this is a temporary, default-off observation, not a subsystem.

### r7 bounded loading observation (2026-10-11, visual parity FAILED)

Fresh mission-dialogue-r7-20261011 deployed cmp-b84 with seven configuration
preimages and verified all three loaded identities. Campaign-menu dedicated start,
prescribed MP shortcut and Safe Mode No were agent-executed; Login and AC_COOP
join were user-assisted. The agent observed the replica map, then opened campaign
Party -> Iara -> Talk. Local evidence01 records map entry, evidence02/03 are paired
host/MP greeting screenshots, and evidence04 retains the private MP log.

MP48760 reports ContainsTerrain=True, HasTerrainHeightmap=True and
IsLoadingFinished=True at121,2020,10009 ms. Visual readiness completed at215 ms.
The greeting and actor render, but MP remains darker/bluer and background gaps
differ. These getters establish neither correct terrain drawing nor 1:1 parity;
simply increasing the loading delay is not an evidence-backed fix.

Read-only follow-up inspected installed MBAgentRendererSceneController and
TableauView wrappers plus retained SceneLayer/View/SceneView decompilation.
The controller delegates to native creation; TableauView derives from SceneView
and creates a tableau texture with render-on-demand disabled. ViewRenderOptions
contains only ClearColor/ClearDepth, not an explicit terrain switch. Native
creation defaults and the full scene atmosphere/rendering contract remain open;
do not infer terrain absence or change brightness arbitrarily from this audit.
No new runtime fix, build, restart or broad battle-regression claim follows from
these observations. r7 remains LIVE with dialogue open; cleanup is pending and
owned-processes.json remains the exact cleanup authority.

Follow-up source/native audit: the replica already calls Scene.Tick after target
readiness (dt capped at0.1); missing scene ticking is not supported by source.
r7's first LoadingFinished=True sample at19:00:32.468 precedes terrain bake
entries at .553/.581/.684/.797, so that getter specifically is not a completed
GPU-terrain preparation fence. Later true samples and persistent visual mismatch
still do not identify which rendering operation is wrong.

Installed MissionScreen.OnActivate calls ActivateLoadingScreen, which obtains
its attached scene and calls PreloadForRendering. The replica omits this call.
This is a concrete initialization difference, not yet a cause. Historical
CAMPAIGN_MAP_RESUME_2026-09-26.md, section "Terrain-disabled tableau investigation",
records removal of PreloadForRendering alongside CheckSceneReadyToRender after
shader failures; it explicitly establishes timing correlation, not causality.
That old experiment concerned the terrain-disabled map tableau, whereas this
candidate loads a standalone terrain-enabled conversation scene. Any controlled
reintroduction must be standalone-only, outside native rendering callbacks and
per-frame polling, with exact cleanup and fresh shader/paired-image evidence.
It must not reintroduce the old map-tableau calls or alter physical battle paths.
No such reintroduction is implemented or accepted by this audit.

### Standalone preload candidate (2026-10-11, NOT native-verified)

Source now calls PreloadForRendering once in Create, after attaching the owned
texture layer, only when MissionScene is present. It is absent from target resize,
render callbacks and tick/readiness polling. Map-tableau behavior and physical
field/village/siege/relief/sally-out/hideout mission paths remain unchanged.
Source guards verify the standalone condition, attachment order and unique call.
This native-sensitive experiment is not a proven fix and may reproduce historical
shader failures; existing catch/cleanup cannot prevent a native crash or bound a
blocking engine call. Fresh paired native evidence is mandatory before acceptance.
cmp-c91/cmp-b85 failed in runner process inspection because CIM access was denied;
neither is a validation pass. Elevated isolated cmp-c92 passed all24 projects;
cmp-b86 passed both client/dedicated builds with installed inventories unchanged.
r7 still runs installed cmp-b84; no hot replacement or new deployment occurred.
Documentation impact: this focused record; existing runbook/architecture unchanged.

r7 subsequently CLOSED at16:11 UTC. Independent postflight proves seven restored
configs, no new config files and no owned/product processes. cmp-b84 remains
installed; test saves retained. Cleanup first stopped exact-owned processes and
then rejected the shutdown-written engine config; inspected physics_tick_rate
20->90 was hash-admitted and restored on retry. Preserve cleanup-process-progress
for actual exits; final AlreadyExited does not mean a new graceful shutdown.
Next step is a fresh, preimage-protected native run of cmp-b86, not a claim that
the preload experiment already solved visual parity.

## Status (2026-09-29)

Source candidate now connects the publication lease, prepared native mission
text/HUD and bounded land-mission 3D capture/transport/rendering. NOT deployed or
native-verified. Full lighting, mounted/crowded conversations and complete visual
parity remain open. Installed cmp-b65 still omits party-menu companion conversations.
Do not claim that text-only replication or the existing map tableau achieves
the requested 1:1 companion dialogue. Current source format is protocol33 /
bridge28 / catalog8 / tableau2 / pose2. All roles require coherent new binaries;
no installed module or battle adapter changed in this offline work.

## Reproduction and evidence

Closed run `artifacts/runtime/map-markers-r1-20260929` used cmp-b65 (32/27/8).
The user clicked the MP host-camera-return button and opened Party -> Iara the
Healer -> Talk in campaign. Evidence09-11 shows host-camera mode in MP but no
dialogue; the host displays Iara's greeting. The `companion-missing` bridge
copies still have ConversationRevision0/IsActiveFalse. Campaign log59184 records
MissionState.OpenNew/Conversation at09:58:00.345, scene
`conversation_scene_green_valley`, and ConversationStart at09:58:01.990.
This reproduces the known cmp-b5 limitation, not an established marker regression.

The same bounded run passed initial markers, Ethemisa issue reveal/order and
user-assisted camera return. It is CLOSED: seven configuration preimages match,
all exact test-owned processes are absent, no new config files/product processes
remain, and all seven installed cmp-b65 candidate files were independently
verified. Cleanup/postflight evidence is in that run. Do not reuse its closed
ownership records to authorize a new launch or cleanup.

## Exact installed-code investigation

Research output: `artifacts/native/mission-dialogue-20260929` (local only; do not
publish proprietary decompilation). DLLs below are in the game's
`Modules/SandBox/bin/Win64_Shipping_Client` directory.

| DLL | SHA-256 |
|---|---|
| SandBox.dll | 587C6E94C425745C1478F632F3B66226FF8298666CB27E8E6D9A62488DF5956A |
| SandBox.GauntletUI.dll | 69F2A87C3C3883D04ECE46200C5CDDF39C188F0EE7C20E26C47FCF64C0F97ED3 |
| SandBox.View.dll | C7360E71DA06799A0CB6C00C4A0CFAB5877621197BA25101433AC28654D169E2 |

Additional exact dependencies (game-relative paths):

| DLL | SHA-256 |
|---|---|
| Modules/Native/bin/Win64_Shipping_Client/TaleWorlds.MountAndBlade.View.dll | FFAAC5ED867CD299F8C14F58FFEE64F86E50D8B197DB5F966759524C3E574BA1 |
| bin/Win64_Shipping_Client/TaleWorlds.MountAndBlade.dll | 19387F31557FF840D14F378F6BBDF1D58FCFF406AB9FF2294DFBB3E49A50B87E |
| bin/Win64_Shipping_Client/TaleWorlds.Core.dll | 3E5CECE8CA42FAF3FDDE27632519ED2A6F86D1EC6D1A742867729F00001689BF |

Confirmed source boundaries:

- The cmp-b65 publisher `PumpApplicationTick` and `TryCaptureSnapshot` reject non-null
  Mission.Current. Its `CoopCampaignMapConversationCapture` admits only an active
  MapConversationView and reads MapConversationVM.DialogController.
- Native `MissionGauntletConversationView` owns a **MissionConversationVM** in
  its private `_dataSource`, uses the same SPConversation movie, and installs
  itself as ConversationManager.Handler. Finalization/uninstall destroys that
  VM. Never instantiate, tick, refresh, continue or finalize the host's VM.
- `ConversationMissionLogic` is the standalone actor/scene owner;
  **MissionConversationLogic** is a different, generic conversation behavior.
  The latter or MissionMode.Conversation alone is NOT an admission proof.
- Native standalone conversation actors are physical agents in the actual
  mission scene, not the map's MapConversationTableau visual collection.
- `MissionConversationCameraView` can set MissionScreen.CustomCamera and
  manipulate agent looks, visibility, bone frames and positions. Its update
  methods are mutating and MUST NOT be invoked by a capture helper. Exact active
  camera and read-only actor visibility/frame sampling still require investigation.
- Existing replica tableau schema1 fixes its scene to scn_conversation_tableau,
  requires conv_* atmosphere and has a three-actor cap. Loading that scene with
  green-valley world coordinates cannot produce faithful mission replication.

## Adjacent-scenario audit

`CoopCampaignMissionHost` publishes Hold while the campaign is in a non-battle
conversation mission. `CoopCampaignMissionLifecycle` retains an already owned
map under Hold; only an admitted Battle intent may end it. No lifecycle change
is needed merely to overlay a dialogue on that map. Do not falsely mark the
campaign as on-map or relax aftermath/follow-up barriers.

Field/village/siege assault/relief/sally-out and hideout missions also make
Mission.Current non-null. Never remove the publisher guard globally or capture
map-native visuals inside those missions. Hideout boss dialogue retains its
independent choice/duel authority. Settlement, naval and multi-agent dialogue
paths need explicit separate admission/presentation coverage; they are not
accepted by this investigation.

## Publication lease and current source integration

`Campaign/CoopCampaignMapConversationPublicationLease.cs` is a pure helper with
no native dependencies, file/network writes or logging:

1. Remember only a complete successfully published, valid active map snapshot
   with camera and catalog/dynamic revisions. Clone it and bind exact campaign
   reference/session. Failed replacement revokes the prior anchor.
2. A separately proven standalone mission must enter within two monotonic
   seconds of that publication. Bind its exact object identity.
3. The same admitted mission can hold the anchor through a long conversation.
   Campaign/session change, another mission, unsupported context, clock reversal
   or explicit reset revokes it irreversibly until a new real map publication.
4. Return detached clones without changing revision/timestamp. An anchor is NOT
   a fresh observation or permission to replay stale dialogue as a heartbeat.

`ConversationPublicationLeaseTests` covers lifetime boundaries, ownership loss,
invalid publications, defensive clones, unchanged freshness and return-after-
battle re-entry. Native adapters must validate eligibility independently; the
helper cannot prove that a caller supplied truthful native facts.

The client branch of the existing MissionStateOpenNewPatches postfix now calls
publisher.ObserveNativeOpening. Native OpenNew returns a constructed mission
with initialized behavior lists BEFORE TickLoading loads the scene. This binds
the two-second entry lease before loading, rather than wrongly measuring a slow
load as stale map data. Exceptions revoke the optional lease and cannot prevent
native mission opening. No new Harmony patch or behavior injection is added.

`CoopCampaignMapMissionConversationContext` requires campaign/non-MP/non-replay,
exact mission name Conversation, one of four installed land conversation scenes,
and the exact SandBox ConversationMissionLogic type/assembly. Actual capture also
requires the same MissionState/Current mission, Continuing/non-ended/Conversation
mode and the manager's existing MissionGauntletConversationView belonging to that
mission with IsConversationMission set. Barter is inactive. No campaign-only
assembly reference or native conversation constructor is added to MP/dedicated.

`PublisherBehavior.MissionConversation` uses a detached, successful map anchor
and captures only conversation data; it does not read map borders/visuals/camera
inside the mission. It preserves catalog/dynamic revisions, freezes IsMoving,
clears the movement target, and advances publication metadata only for a live
admitted mission. Scene loading does not publish a fabricated dialogue heartbeat.
The publisher's successful-map stamp is not set by an overlay, keeping tooltip
admission map-only. Any unsupported mission/replacement/reset revokes the lease.
New mission capture/loading failure diagnostics require explicit VerboseDiagnostics;
the existing map-only failure logging policy is unchanged.

Map publication remembers a new anchor only AFTER catalog/dynamic/state writes
succeed; failure revokes the prior anchor. Mission capture failure publishes
inactive rather than retaining the last line; this clearing rule persists through
the first successful map return. Existing map-only transient-retention behavior
is otherwise unchanged. The initial text-only integration had no fabricated map
tableau. The later bounded 3D source candidate below now supplies the actual scene
descriptor when admitted; neither stage has native 1:1 acceptance.

## Next implementation gates (planned)

1. Native-verify the new exact mission/view/handler association and lease/capture
   integration described above; source guards and builds cannot prove UI behavior.
2. Verify loading, failure, barter/close, campaign/session replacement, tooltip
   exclusion and first map-return clearing in the real host/client processes.
3. Native-verify the bounded mission descriptor/rendering candidate below, then
   extend full interpolated lighting and unsupported visible actors. Do not claim
   that named atmosphere plus climate setters reproduces all native lighting.
4. Verify owned terrain/render-target readiness and native packet sizing, scene
   replacement/close and late-join barriers. No physical agent spawning,
   campaign constructors, stock host-camera updates or host animation mutation.
5. Run both isolated builds/all contract projects, then a FRESH native run:
   companion open/update/read-only answers/close, repeated companion, ordinary
   map dialogue, host and MP modals, camera/tooltip recovery, reconnect and
   map/battle/map. Record paired scene/actors/camera and animation proof.
   Missing visual parity is a failed gate, not a text-only success.

## Validation and documentation impact

cmp-c70/cmp-b66 did not validate source: NuGet.Config access was denied by the
sandbox. Fresh permitted cmp-c71 passed all 24 contract projects, including 86
publication-lease checks. cmp-b67 compiled both client and dedicated under the
isolated run root and verified installed module inventories unchanged. Logs and
structured results are under the corresponding temporary Automation run roots.
No new native run or deployment was performed; installed cmp-b65 remains.

Follow-up source integration: cmp-b68 client compilation failed because the
MissionMode namespace import was missing (corrected). cmp-c72/c73 each failed
the old source assertion requiring unconditional retention of failed dialogue
capture. It now explicitly checks map-only retention plus mission/return clearing.
cmp-c74 passed all24 projects; cmp-b69 passed both isolated builds. Final cmp-b70
also passed both builds with installed inventories unchanged after gating new
mission diagnostics. Fresh cmp-c75 passed all24 projects, including 96 lease/
integration checks with the explicit diagnostic gate assertion. Do not count
the unwired cmp-c71/b67 foundation results as verification
of the new publisher integration.

Additional installed-code investigation: MissionScreen sends CombatCamera to
SceneView both on ordinary updates and after copying a custom camera. Capture
that already computed camera, not a recomputed framing. MissionInitializerRecord
owns AtmosphereOnCampaign and scene identity/levels; mission actors expose native
MBAgentVisuals, unlike the map's managed AgentVisuals wrapper. The existing map
actor capture cannot simply cast them. Actual mission-scene atmosphere/actor
capture and client terrain readiness remain the next implementation work.
Exact `TaleWorlds.MountAndBlade.MBAgentVisuals` exposes GetVisible, GetGlobalFrame,
GetSkeleton and GetEntity as read accessors. Native Agent exposes BodyPropertiesValue,
SpawnEquipment, AgentScale, ActionSet, ClothingColor1/2 and wielded indices.
Their existence is not proof of safe capture after agent removal or correct
first-person body visibility; bind live mission/actor identity and test it.

Read: README, GOAL_MODE, CONVERSATION_HUD, focused previous companion result,
AUTOMATIC_LIFECYCLE, BUILD_TEST_DEBUG, architecture/flow/risk/code-map entries.
This is the canonical investigation/integration record. README, CODE_MAP,
ARCHITECTURE, RUNTIME_FLOWS and INVARIANTS_AND_RISKS link the new source boundary.
BUILD_TEST_DEBUG remains unchanged: isolated validation procedures are reused.

Goal audit: bounded cmp-b65 marker/camera evidence retained; companion capture
and 1:1 presentation NOT satisfied; source text integration build/test verified
(cmp-b70/cmp-c75), but not deployed/native-verified; full native visual and adjacent-
regression acceptance remains open.

## Land-mission 3D source candidate (2026-09-29)

`CoopCampaignMapConversationMissionScene` explicitly selects one of four installed
land scenes and carries native vertical FOV/near/far plus bounded time, winter,
dryness, precipitation, temperature, humidity and wind. Optional presence is in
tableau schema2; null preserves the old fixed map-tableau behavior and conv_*
atmosphere restriction. Mission atmosphere identifiers remain bounded/no-path.
Deep clones and canonical decoding reject malformed presence, unknown scene,
nonfinite/bounded values, obsolete schema and partial payloads. The existing atomic
text/tableau payload and host bridge carry this descriptor unchanged.

Pose schema2 adds optional camera/actor frames. Their presence is mandatory for
mission descriptors and forbidden for map tableaux, including receiver admission.
Matrices are finite, bounded, unit/right-handed. The three-actor worst case with
128-character action names is665 bytes (map473); cap768. Host hash/revision/sequence
binding and client coalescing/expiration remain. Native packet acceptance is NOT
proven by the pure 665-byte test. The client applies owned scaled actor frames and
camera frame without scene/actor allocation; body playback still uses bounded
same-action rate correction rather than repeated phase seeking.

`CoopCampaignMapMissionConversationVisualCapture` reads the existing admitted
MissionView.CombatCamera and exact mission initializer; random terrain, map patch,
nonempty scene levels or invalid campaign atmosphere are not guessed. It samples
only active actors whose valid MBAgentVisuals reports visible, with stable agent
reference/order, then reads body/equipment/colors/action set, normalized detached
world frame, scale and skeleton body motion. It never updates native host state.
Native role/view association is rechecked; failures clear the optional visual cache.
No campaign-roster traversal is used. Initial descriptor identity stays stable
across pose/camera/action changes when actor identities/resources are unchanged.

Current explicit bounds: three visible adult unmounted humans and at most32 active
mission actors. Visible mounts, mounted humans, children or a fourth visible actor
reject the optional 3D capture instead of silently omitting them. This is a remaining
Goal gap, not successful support of those conversations. Facial/voice and exact
first-person mesh visibility/appearance are not accepted. Shader/resource/native
readiness and the campaign initializer's atmosphere availability need fresh proof.

`CoopCampaignMapConversationTableauView` selects the supplied allowlisted scene's
local module owner. Mission scene creation has no physics/navmesh/flora physics;
it does not open a new physical mission or spawn agents. It uses host vertical
FOV/clip distances rather than the map tableau's fixed camera tag/200-unit far
plane; full-scene shadows replace its portrait-focused shadow region. Named
atmosphere and climate apply only to the owned scene. Original map setup parameters
are retained under the null MissionScene branch. Descriptor and scene release clear
the mission profile. Terrain readiness still needs native validation; existing
offscreen actor/target readiness must not be cited as complete terrain proof.

Installed scene XML inventory: green_valley has CharacterSpawner and native
mesh_seasonal_material; forest also has AmbientSoundEmitter; steppe/desert have
CharacterSpawner. Exact `TaleWorlds.MountAndBlade.View.Scripts.CharacterSpawner`
OnInit sets colors only; spawning is in editor tick or explicit SpawnCharacter,
neither called by this renderer. This is source evidence, not blanket safety
acceptance for every scene script/version. Ambient sound duplication and terrain
seasonal material behavior remain native QA items.

Intermediate validation: cmp-b71 and cmp-b72 both isolated builds passed. cmp-c76
failed the old source assertion for zero-argument ResolveSceneOwner; the guard now
checks the explicit selected scene and scene-list tests reject non-allowlisted names.
cmp-c77 passed all24 projects before the native capture adapter was added. Final
cmp-b73 passed both isolated builds with installed inventories unchanged; cmp-c78
passed all 24 contract projects, including the connected capture adapter guards.
Their terminal runner results were observed directly. No new native run/deployment
occurred. cmp-b65 is the last recorded installation, to be re-inventoried after
shutdown. See the [handoff](CAMPAIGN_MAP_HANDOFF_2026-10-09.md) and
[fresh test procedure](CAMPAIGN_MAP_TEST_RUNBOOK.md).
