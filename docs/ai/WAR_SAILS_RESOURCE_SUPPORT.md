# War Sails resource support — approved implementation 2026-10-29

2026-10-30: current model handoff is HANDOFF_2026-10-30_NAVAL_CRASH.md. It records
the exact accepted base-game boundary, rejected Naval experiments, current
installed/source split and remaining gates. Older status sections below are
historical and do not establish a working Naval build.

CURRENT: r81/b16 FAILED on load with the same native TickVisuals null read.
Both failed experiments (background-focus veto, deferred reset) are removed.
r81 CLOSED/all12 preimages restored/all7 configs match; dumps retained, no game/
debugger roles running. Installed b9 remains known-unstable Naval. Source b17
rollback builds/all27 naval-c12/focused compiled check PASS, not installed.
No manual retry pending. Read WAR_SAILS_SCENE_LIFECYCLE_2026-10-29.md and r81/FAILED.md
before further native work. Partial constructor allocation/global scene lifetime
are research leads, not established causes. Goal remains ACTIVE and unfinished.

Latest r80/b15 FAILED and its background-rendering veto is rejected/removed.
b16 restores background movement and defers map AgentVisuals retirement via the
stock ResetNextFrame API. Offline ClrMD matches the failing native pointer to a
no-longer-owned visual; native fix still unproven. Both builds/all27 naval-c11/
twelve compiled checks PASS. r80 CLOSED/restored; r81 OPEN/INSTALLED with
campaign94992 launched, user repeating recruiting/leaving/background rendering.
Read WAR_SAILS_SCENE_LIFECYCLE_2026-10-29.md and r80/FAILED.md first. Goal ACTIVE.

Latest r79/b13 FAILED after window focus loss: same native TickVisuals null read
as r77. r79 CLOSED/all12 original files restored/all7 configs match; dumps retained.
b15 adds the actual MissionScreen.IsFocusLost boundary plus deferred latest-state
reconciliation, with both builds/all27 naval-c10/twelve compiled checks PASS.
r80 is now OPEN/INSTALLED and campaign89204 launched; manual focus/recovery native
acceptance pending. Read WAR_SAILS_SCENE_LIFECYCLE_2026-10-29.md before continuing.
No complete DLC fidelity/crash-fix claim; black water and Training Field still open.

Latest lifecycle follow-up: see WAR_SAILS_SCENE_LIFECYCLE_2026-10-29.md. A concrete
stale scene-readiness defect is corrected separately from the unproven crash
attribution. b13 both builds/twelve compiled checks pass; native safety remains
pending. The rejected b10 water initializer remains removed. Consult r79 evidence
for deployment/launch status. No new Training Field/water fidelity claim follows.

## LATEST: water candidate rejected and reverted

r78/naval-b10 caused an immediate new crash on replica load (MP51056). Native
worker thread0x17888 reads garbage0x485340CCCCCCCCCC, C0000005, different from
MP77780's main-thread TickVisuals null read. The new water initializer ran too early
relative to stock load/atmosphere/optimization and view/camera setup; exact causal
prerequisites remain unproven. Managed/native-IL checks did NOT establish runtime
safety. Do NOT reinstall that candidate or infer that water initialization alone
repairs the delayed crash.

Source helper/call and its now-inapplicable IL check are removed. naval-b11 both
isolated rollback builds PASS. All12 installed files restored to r78 preimages
(naval-b9 client966391D3...C3AB3D1C), all7 config hashes match, no owned game roles
remain; no new game launch. r78 CLOSED2026-10-29T19:53:32.0590337Z. Prior b9 is
base-accepted but has an unresolved delayed Naval crash, not a safe DLC release.
Both smaller WER dumps are copied/hash-verified under r78/evidence/preserved-wer;
the first original engine crash folder is missing on later inspection, with its
earlier debugger stack reports retained. No task tool deleted the original dump.

Read artifacts/runtime/naval-water-r78-20261029/REJECTED.md before continuing.
Next is offline native scene/view/water lifecycle and current-readiness analysis;
do not ask for another speculative manual run. Training Field dynamic model,
water fidelity, crash-free lifetime and DLC land battle gates remain incomplete.
Full Goal remains ACTIVE; no completion/archive/commit/push is claimed.

## Current Goal / native visual crash follow-up

The user explicitly resumed the Goal through its mechanism. It is now ACTIVE;
GOAL_MODE.md supersedes historical PAUSED notes and supplies in-scope authority.
r77 naval-b9 passes server startup and local Naval map/profile admission; user and
direct paired Computer Use observations confirm the map/parties render. Broader
battle/two-PC acceptance remains open.

MP77780 then crashed C0000005 at TaleWorlds.Native+0x5B1276, null read0x70. Offline
WinDbg native/managed stacks trace the failing call through MBMapScene.TickVisuals
to replica OnMissionScreenTick. Native field identity is not known without private
symbols. !analyze produced a WRONG_SYMBOLS pseudo-bucket which is not diagnostic.
Original engine/WER dumps remain in place, hashed without large duplication.
Full evidence/limits: r77/evidence/CRASH-AND-VISUAL-FINDINGS.md and debugger logs.

Paired screenshots show black replica water versus normal host water. Installed
NavalMapSceneWrapper.InitializeMapWaterWake calls SetupWaterWake(128f,8f), which
ensures a water renderer then sets world size/damping(128f,0.994f) and offset8f.
Replica omitted this concrete prerequisite. naval-b10 adds those exact calls only
to the owned noncampaign Naval scene after Read and before preload/native ticks.
This is runtime-sensitive render-resource creation, not diagnostic code. It does
not suppress TickVisuals or create a Naval campaign simulation. Its relationship
to the crash is a hypothesis pending native repetition, not a proven root cause.

Both naval-b10 builds, all27 naval-c7 projects and twelve compiled checks PASS.
Test-NavalMapWaterPrerequisites.ps1 decodes actual installed and candidate IL,
compares three calls/order and exact native constants, and verifies placement.
It never creates/invokes a scene/renderer. Its first harness attempts needed the
Native-module dependency directory and corrected FE two-byte opcode decoding;
these were test-harness errors, not product runtime failures.
Client SHA256 DEDFA83416967FF162FC769E827816A47EF77F2D02BDF8DB23D32B6B87676406;
dedicated remains AE53B5A70A2AFE27ED64C6D57F6B18EC661123FE81874913F678479D6FF07334.

r77 CLOSED2026-10-29T19:42:28.0051527Z, all7 configurations match, no owned IDs
remain; all game roles already exited. Exact task-owned debugger helpers closed.
The test-only dedicated map selection was restored; dumps/saves/adapter/relocated
official link and Steam retained. Fresh r78 will install the water prerequisite
candidate and reuse the preserved Naval save. Native crash-free duration and
water appearance must be checked, then battle/return, not inferred from tests.
r78 now OPEN/INSTALLED with12 verified files/seven new preimages. Official Naval
campaign22936 started2026-10-29T19:44:03.8266493Z. User asked to load preserved save,
start/join and compare water, then observe approximately10 minutes. Do not interrupt
that manual test or interpret silence as success. Training Field is still pending.

Separate missing Training Field is confirmed: catalog entity
settlement:tutorial_training_field, custom kind5, LevelFlags0. Official StoryMode
prefab is instantiated dynamically by SettlementVisual.OnStartup/MapScene when
the scene root is absent. Replica supports only tagged static settlement roots.
Do not invent a wall-level flag or reveal hidden roots to approximate this model;
exact dynamic visual/resource/disclosure support is still required.

## Current correction: keep the official campaign module outside the server scanner

r76 passed map ownership/profile and prepared the adapter, but native server
attempts94616/96468 exited during module scanning: NavalDLC version is not matched
with base game. Actual server ModuleHelper scans physical/platform modules and
compares original NavalDLC.RequiredBaseVersion before active module selection.
The now-obsolete raw server/Modules/NavalDLC junction exposed the campaign module
despite a resource-adapter-only launch list. Native server build118999 and client
119303 differ; matching v1.4.8 Native descriptors do not prove binary compatibility.
Version checks and game files are not patched to suppress this error.

User approved removal of raw-link creation and reversible migration. Launcher
now shares only SandBox/SandBoxCore and uses CoopSpectatorNavalResources for DLC
assets. SharedServerModules relocates an old raw NavalDLC junction only when its
target and resolved source equal the current game's source AND a bounded ordinary
committed transaction journal proves initial creation (PreviousFolder=none,
Reused=False). It rechecks the link before moving it, retains the junction outside
Modules as LegacyNavalDLCLink, journals recovery paths and rolls back earlier
entries if preparation fails. Ordinary folders, foreign/unowned links and unsafe
backup roots are preserved/refused. This applies even to base launches because
the native scan sees inactive modules. No recursive removal or asset copying.

naval-b9 both builds, naval-c6 all27 contracts and eleven compiled checks PASS.
Tests include real link relocation and unchanged source, idempotence/no-recreation,
ordinary/unowned/foreign refusal and migration rollback. Client SHA256:
966391D37725BECC1D55664D9E119A5B193C50694B2832257FCA8C6EC3AB3D1C.
Dedicated binary is unchanged from naval-b8:
AE53B5A70A2AFE27ED64C6D57F6B18EC661123FE81874913F678479D6FF07334.

r76 CLOSED2026-10-29T19:07:03.7885407Z: all7 configurations match, no owned IDs
remain; expected dedicated map selection restored. Game roles already exited.
Exact failed server logs are retained in r76/evidence. Saves/Steam remain.

Fresh artifacts/runtime/naval-isolation-r77-20261029 is OPEN:12 coherent files,
seven fresh preimages. Actual compiled-helper migration at19:08:43.9423369Z moved
the recorded raw link to the server's CoopSpectator.SharedModuleBackups/
20261029T190843176-5b564b2c67ce41f59622b4253fe7d099/LegacyNavalDLCLink. Its target
remains the game's original NavalDLC. Exact resource profile before/after is
naval-v1:8e62ede2649adc90408d4782b2bcc55d331d0ad520bb5f849e452d7b72abe613;
resource adapter unchanged, second preparation unchanged. This is actual filesystem
proof, not native server/map/battle acceptance. Do NOT restore the obsolete link
inside Modules for a new test. User must load the preserved Naval save and retry
server, then the working multiplayer shortcut. Native gates remain open.
Campaign88284 started2026-10-29T19:09:45.0385304Z with official NavalDLC active.

## Current correction: native map ownership and recoverable readiness

User approved the correction after r75 failed before server launch. The official
campaign log loads NavalDLC/SceneObj/Main_map/scene.xscene successfully, while the
new guard returned unknown via engine per-module scene-name enumeration. Current
installed SandBox.MapScene.GetMainMapModule instead uses ModuleHelper.GetActiveModules,
IsActive, actual FolderPath and File.Exists(SceneObj/Main_map/scene.xscene), choosing
the last match. The correction adopts that exact rule for campaign validation and
replica Scene.Read admission. Naval activation also uses the same managed registry.
No path is guessed from module installation or drive C; platform DLC paths/order
remain authoritative. Foreign late map overrides remain exposed/rejected.

ModulePathHelper reads only public managed registry metadata. New pure
CoopCampaignMapResourceOwner shares the file/order rule. New
CoopCampaignContentIdentityCache retries failed capture after two seconds rather
than poisoning the entire Game owner. Successful identities stay cached, and a
new Game owner resets both result and retry delay. No hot-path diagnostics,
registration-counter changes or native game construction are introduced.

naval-b8 both isolated builds PASS with unchanged installed inventories; naval-c5
all27 contracts PASS, including actual file ownership/order, inactive/foreign
overrides, missing files and same-owner early-failure recovery. Eleven compiled
net472 checks PASS; Test-ContentOwnerReadiness.ps1 additionally validates current
installed registry/property signatures without invoking/initializing the native
registry. Client SHA256:
1C5003FCF4871BB1D4550A4FE02ED1E4675C5D49A04F1212CD93786ACEC7F2DE;
dedicated SHA256:
AE53B5A70A2AFE27ED64C6D57F6B18EC661123FE81874913F678479D6FF07334.

r75 CLOSED at2026-10-29T18:49:52.7310371Z: all7 configuration hashes match, no
owned processes remain. User closed the campaign. Only the inspected test save
pointer was restored; actual save Тест доповнення is preserved. No server had
launched. r76 is the fresh staging run for this correction; native success remains
pending. Explicitly load the new Naval save, not Continue's restored base pointer.

r76 now OPEN/INSTALLED: all12 file hashes match naval-b8/source assets, seven fresh
configuration preimages retained. Official Naval campaign64448 started
2026-10-29T18:51:48.8566425Z. User asked to load Тест доповнення, retry Start Server,
then connect MP via working shortcut, initially without entering a battle. No
native corrected server/map success is claimed yet. No new archive/commit/push.

## Authority and baselines

The user approved this implementation after an additional read-only comparison.
Goal remains PAUSED; normal-mode task approval supplies authority. The previous
aftermath fix is user-accepted: Prisoners/Loot and map input now work. r73 CLOSED
at2026-10-29T01:06:34.0094005Z: all seven configuration hashes match, no owned IDs
remain and no process was terminated. Steam/saves and shared links/backups remain.
Prior installed baseline af-b2 is retained in fresh r74 module preimages.
The distributed b299 ZIP remains unchanged. No archive/commit/push is requested.

Initial naval-b1 compilation found API/namespace errors; corrected builds pass.
Final naval-b7 both builds pass with installed inventories unchanged; naval-c4
all27 projects and ten compiled net472 checks pass. r74 installed12 coherent
files with seven fresh configuration preimages. Native acceptance remains open.
Do not treat this source note as proof that a DLC campaign has run successfully.

## Read-only findings

Installed Sandbox1.4.8 has493 settlement definitions; NavalDLC1.2.8 has540.
All493 common settlement coordinates differ;47 IDs are added. Main_map uses the
same scene format2, but different scene/terrain/navmesh and map bounds. This is
not a uniform label-offset correction. Current replica resolves the last active
owner of Main_map, so campaign and MP must load the same resource selection.

Naval items.xml has131 Item entries, naval_weapons.xml has67 (66 CraftedItem),
banners.xml has6;204 IDs are unique across those files. naval_crafting_pieces.xml
adds150 definitions. The original descriptor excludes MultiplayerGame for these
catalogs; two XSLT files also extend Native crafting templates/weapon descriptions.
All nine named unavailable fixed items in tester8560/17644 exist here. The tenth
missing name, crafted_item_77, needs its transmitted crafted definition and the
confirmed unresolved naval_axe_head_08. A1240-piece count alone reported Ready=True
while that exact dependency was absent.

Tester host2592 loaded NavalDLC while both MP clients and DS omitted it. Both peers
connected and agent creation partially worked, but strict equipment materialization
failed. Missing parties/stale map publication and engine build mismatch remain
separate compatibility variables; no single-cause guarantee is claimed.

Additional map scripts include PirateSpawnPoint (empty OnInit), BlockadePositionScript
(stock SandBox.View editor helpers) and CampaignMapAmbientOccluder (NavalDLC).
The latter initializes a bone buffer from NavalDLCManager.GameModels.MapStormModel
and ticks StormManager.SpawnedStorms. StormManager is created only for Campaign.
Simply activating official Naval submodules in MP is not the chosen solution.

## Implementation structure

- CoopCampaignContentProfile is pure file metadata: base/naval selection plus a
  canonical SHA256 identity of descriptors, relevant XML/XSLT, selected map files,
  Naval script assemblies and the three actual private Warhost item/piece catalogs.
  Private catalogs use role-independent labels so coherent client/DS copies match.
  Host/MP/DS native adapters cache per Game owner, not every frame. No drive scan,
  hot-path hashing or untrusted remote file access. Missing/changed inputs fail.
- SharedServerModules shares only SandBox/SandBoxCore. Original NavalDLC must not
  be exposed inside server Modules; the resource adapter links directly to game
  resources. Earlier optional raw-link behavior is superseded by the correction above.
- CoopNavalResourceAdapter creates CoopSpectatorNavalResources on each installation.
  Heavy asset/scene directories link to that machine's game NavalDLC. ModuleData
  is an ordinary local directory:40 top metadata files total2,241,097 bytes on the
  audited installation; metadata subdirectories link to source. Small private
  ModuleData/WarhostPrepared XML/XSLT files use plain native relative paths and
  admit Items/CraftingPieces/Templates/Descriptions/
  cultures to MultiplayerGame/EditorGame, adding multiplayer_item only to detached
  item copies. Original files and official SubModule.xml/bin remain untouched.
  The adapter has NO managed gameplay submodules. Exact source/profile/file/link
  ownership is checked on reuse; changed profiles preserve the old adapter outside
  Modules. Failures leave diagnostic staging outside Modules; never recurse-delete
  shared resources. Existing foreign/modified module data is rejected.
- Campaign-owned launch prepares the server profile and records local host process
  identity/mode for the working MP launcher. Auto mode trusts only a live matching
  Bannerlord PID/start time. With no local host and DLC installed it asks Base/War
  Sails; --base/--naval choose explicitly. Never infer activation from installation
  alone. The launcher preserves its normal Steam/shader-cache process path.
- Map protocol72/bridge67 carries ContentProfileId, cloned with retained aftermath
  frames. Map scene load requires an exact local profile and expected scene owner.
  Battle ScenarioContext carries the same ID; topology schema3/hash includes it.
  Binary battle snapshot schema20 writes/reads the ID. Schema19 stays readable but
  explicitly unbound, never guessed Base. The real serializer is tested for Field,
  Village, Hideout, Siege, LordsHall, SiegeAmbush and Relief contexts with an exact
  Naval crafted dependency, plus an actual old-serializer fixture.
  The native network adapter aborts mismatched content before opening the battle,
  rather than disabling equipment safety or waiting for an unrelated timeout.
- Naval item indexing follows original Items declarations including root-level
  files; it does not merely add NavalDLC to the old items-directory scan. Crafting
  startup admission checks all204 ItemObject and150 CraftingPiece IDs and their
  public IsReady state. Piece indexing also checks the Naval IDs, never late-loads pieces to bypass
  a failed pre-spawn contract or repairs object registration counters.
- Client map preparation loads the two local Naval assemblies as type resources,
  registers exactly the two additional map script types without constructing
  official submodules, and patches only the storm component. Only instances first
  initialized inside the owned Scene.Read scope receive an empty16-bone visual
  buffer and skip campaign StormManager ticks, tracked with weak keys. Campaign
  instances/unrelated scenes pass through. This is runtime-sensitive visual code,
  not diagnostics. Native guard/lifetime/rendering must be tested explicitly.

## Boundaries and remaining gates

Empty storm initialization prevents invoking an absent campaign simulation; it
does NOT replicate live host storm positions, blockades, ships or naval battles.
Those visual/gameplay features need separate capture/transport/mission work. Do
not advertise complete marine support from a successful land battle.

## Final candidate and first native run

naval-b7 client SHA256:
CA0A3AA08E48D31E14ACA1E24B7A6F76B6628893366A45FF8583629CB68F31B9;
dedicated SHA256:
43F0962E5F424721AF7DFB1B055ADDA33FA8D11697E567A9DAE856758D6CAC3B.
naval-c4 all27 projects pass. All ten detached compiled checks pass in
artifacts/runtime/naval-b7-managed-checks. Actual installed resources produce
base-v1:19b10c5e8416692573aaca35def057fb165b8ca0a44dfd54622e0a8775b4325d
and naval-v1:8e62ede2649adc90408d4782b2bcc55d331d0ad520bb5f849e452d7b72abe613;
204 items and150 pieces confirmed by read-only inventory, not native registration.

artifacts/runtime/naval-resources-r74-20261029 is CLOSED. Twelve installed file
hashes match: client, both DS copies, existing presentation/catalog assets plus
the new content helper and both game-root BAT launchers. Seven fresh configuration
preimages and previous module/launcher files remain. No DLC adapter has yet been
created or native-tested. Base campaign72204 started2026-10-29T18:19:24.7079386Z;
Steam62800 was already user-owned and must not be terminated. User is asked to
load a BASE save, start server via campaign and MP via working shortcut, then
check map/battle/Prisoners/Loot/map camera. User offers to create a new Naval
campaign after this regression passes. Do not treat the offer as DLC acceptance.

User now reports the complete requested BASE regression works. r74 cleanup at
2026-10-29T18:26:43.4196855Z confirms all7 configurations match and no owned IDs
remain; no process terminated. Only the inspected dedicated map selection was
restored. Existing user Steam62800 and saves remain untouched.

Fresh artifacts/runtime/naval-resources-r75-20261029 is OPEN, with the same12
coherent files and seven fresh preimages. Its campaign launch includes official
NavalDLC after StoryMode and before CoopSpectator. User offered to create a new
War Sails campaign. Exact campaign87648 started2026-10-29T18:27:54.8175063Z;
its log loads NavalDLC/SubModule.xml and candidate MVID
1e6d2d90-5903-46ad-91ef-36e84022b71a. This is startup evidence only.
First native gates are map scene/settlement positions/parties,
Naval resource admission and a short dry-land battle with aftermath. Naval runtime
resource projection, storm adaptation and native catalog readiness are not yet
accepted. Multiplayer must still be launched by the user's working shortcut;
do not create a duplicate client or launch official Naval gameplay submodules
inside MP/DS.

Run contracts on both modes, different install roots, missing/mutated source,
foreign links, exact crafted dependencies, codec/clone/hash identity and no-copy
packaging. Run Test-SharedServerModules.ps1 and Test-NavalResourceAdapter.ps1 on
the final actual net472 binary, plus existing detached UI checks. Never execute
the fixture DLL bytes or construct a native game in filesystem tests.

Before native deployment reconcile/close r73, retain fresh config/module/launcher
preimages, and install coherent client/DS/launcher/helper files. Keep the previous
working binaries recoverable. First test base profile unchanged; then a real
War Sails campaign with both peers/DS on Naval resources. Verify map geometry,
party positions, a known Nordic item and crafted axe, cold portraits, field and
other supported land scenarios, aftermath, save reload and map return. Actual
two-PC acceptance and source/installed identity evidence remain separate gates.
