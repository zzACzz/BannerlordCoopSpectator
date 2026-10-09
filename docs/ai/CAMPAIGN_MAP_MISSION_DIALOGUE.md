# Standalone campaign mission dialogue replication

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
