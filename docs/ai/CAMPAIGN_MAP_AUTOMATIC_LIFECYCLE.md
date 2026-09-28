# Automatic Campaign Map / Battle Mission Lifecycle

Status: main-thread host/server adapters implemented in source, 2026-09-26.
First native attempt failed at opening-origin verification on 2026-09-27.
Full native acceptance remains open. The separate explicit environment gate
`COOPSPECTATOR_AUTOMATIC_CAMPAIGN_MISSIONS=1` AND the existing map prototype
gate are required. No default rollout or installed-module change is claimed.

Implementation boundary: the reconciler now has a production caller in
`DedicatedServer/CoopCampaignMissionServer.cs`. Both module ticks are wired.
The supported ModdedOfficial campaign-owned launch creates a fresh inherited
session and attaches the exact final launcher-owned PID/path/start time. Other
launch paths clear the inherited session and do not adopt existing servers.
`CoopCampaignMissionHost` publishes revisioned intent once per second; the
server checks it at four Hz. The bounded binary channel admits only fresh
same-session, exact-process, same-campaign input. Wire freshness is NOT trusted:
the receiver checks the local monotonic timestamp (15 seconds), exact live host
identity, pinned dedicated identity and increasing/immutable heartbeat pulses.
Input is limited to 16 KiB; roster capture is limited to 16 MiB. Invalid reads
cannot authorize commands. A pending transition times out after 120 seconds;
300 seconds without fresh host data is a terminal adapter failure. Current
missions are never forcibly cleaned up by this adapter on failure.

The campaign/client shared roster path remains unchanged. Host admission pins
accepted bytes to session-local `host_roster.json`; dedicated battle opening
revalidates SHA-256 and exact campaign/battle/instance/scene/mode, then copies
those bytes to its session-local active `battle_roster.json`. A later host stage
cannot overwrite the dedicated's active-stage roster. Existing scenario
selection/construction remains in the normal game modes; unsupported mode IDs
fail closed rather than selecting a generic fallback. Day/night hideout
scene/scenario/mode matching is explicitly rechecked.

Native dispatch uses the inspected public ListedServer StartMission/EndMission
methods on the main thread, retaining native LoadMission and the established
client topology gate. For topology-dependent battles, the pinned snapshot is
installed and its next wrapped wire-index contract is sent before StartMission.
The wrapping index is NOT ownership identity. StartMissionAux calls native
StartMultiplayerGame synchronously before its first await; the adapter requires
the exact new Mission object returned by OpenNew and expected scene/behavior from that invocation,
then waits for Continuing. If this native assumption changes, opening fails
closed; it never adopts a later same-kind mission.

### First native attempt and opening-origin correction (2026-09-27)

`artifacts/runtime/campaign-lifecycle-20260927` retains the exact cml-b5 loaded
campaign/server identities and logs. The campaign menu launched PID 58800 and
the server automatically opened CoopCampaignMapPrototype at 06:08:10, without a
console command. Immediately afterwards the adapter faulted; no MP client was
launched. All exact-owned product/support processes exited and all seven config
preimages match. The user-approved test save state remains. Steam is retained
for follow-up. This is a failed adapter acceptance, not an automatic-map pass.

Exact installed MissionState/Mission inspection found that OpenNew constructs
and returns the mission synchronously, but Mission.Current is assigned only in
Mission.Initialize, invoked by a later loading tick. Reading Current immediately
after StartMission therefore cannot prove the opening. The source correction
uses the existing OpenNew postfix return only during a scoped synchronous
dispatch on the same managed thread. Missing, duplicate, null or cross-thread
returns fail closed. The scope is cleared in finally; late missions cannot be
adopted. Scene/behavior checks and the subsequent exact-reference Continuing
observation remain mandatory. The hook observes managed identity only; it does
not initialize, mutate, end or scan native missions. It is inert outside the
default-off automatic adapter dispatch. The same defect affected field, village,
siege, sally-out, ambush, relief and hideout openings through this adapter.

Native decompilation evidence: `artifacts/native/lifecycle-opening-20260927`.
Eight pure capture assertions cover exact identity, missing/null/duplicate,
cross-thread, closed and fresh-dispatch rejection. Native rerun is still required.
Correction validation: cml-b6 compiled both roles with unchanged installed
inventories; cml-c5 passed all 24 contract projects. Client SHA-256
`43280C649753CE6A96A058A75EBD3265613F9306EC41A5423F6F632E82B06B86`, dedicated
`AE73361600BCF29681A6332D331F8E8A629077904B1F81FED2A7754CC23DF88A`.
Fresh run `artifacts/runtime/campaign-lifecycle-r2-20260927` staged this candidate;
campaign PID 29276 is owned and cleanup is pending. No acceptance claim yet.

Corrected native rerun: exact cml-b6 campaign 29276, dedicated 34872 and MP 15376
loaded identities match. Menu-started dedicated dispatched StartMap command=1
at 06:17:25.818, then accepted the authoritative 397-entity catalog. No adapter
fault appeared beyond the 120-second opening deadline. MP launched through the
required desktop shortcut and Safe Mode No was selected. After user-assisted
Login it joined AC_COOP: mission opening 06:22:57.797, Main_map read 06:23:00.223.
Evidence 03-05 in the run root proves rendered automatic startup and replicated
movement/daylight, not full visual parity. At 06:28:16, independent dialogue
scene loading reproduced the pbr_terrain/pbr_terrain_gbuffer X3129/X4008 errors.
The same connected client then transitioned to battle_terrain_biome_093b:
EndMap command=2 at 06:31:37.198, StartBattle command=3 at 06:31:55.845,
30 attackers / 22 defenders, followed by Yorig commander deployment.
Attacker Ready was accepted at 06:36:47.911. User-assisted H at 06:38:30.535
completed opposing deployment; second H reached BattleActive at 06:40:52.454.
The user ordered attack. Server victory/result publication occurred at
06:41:58, campaign writeback was consumed at 06:41:59.008, and after closing
the host's prisoner/loot screens StartMap command=4 dispatched at 06:44:13.834.
The same MP client rendered the returned map at 06:44:21.430 (evidence 14/15).
This closes one normal field-victory cycle only. Repeat, retreat, adjacent
scenarios and full dialogue rendering remain open. Native MP end UI displayed
Draw despite authoritative Attacker victory; this presentation defect remains
uninvestigated. Exact-owned cleanup completed at 06:45:51: all seven config
preimages match, no owned roles remain, test saves and installed cml-b6 remain.

Existing campaign start callers now check roster-write/admission success before
marking a battle dispatched. The central legacy command path, map console path
and hero-creator start cannot race a bound automatic session. Authoritative
successful non-suppressed result publication supplies a mission-reference and
exact battle identity callback. Existing battle logic still owns battle ending;
the lifecycle never issues EndMission for a battle. The campaign result journal
success/already-applied boundary supplies matching aftermath proof. Non-final
stages block map return; a subsequent stage can proceed only after prior
aftermath. Actual native siege/hideout/abort/reconnect acceptance remains open.

Source validation: initial integration `cml-b4` compiled both roles without
installed changes. `cml-c3` passed 23/24; the new channel rejection harness failed
because InvalidDataException is not IOException. Both the harness and channel
reader now handle it explicitly. Focused `cml-f1` passed 305 channel assertions
plus 4,305 lifecycle assertions and the existing map contracts. Final `cml-b5`
compiled both modules and retained unchanged installed inventories; `cml-c4`
passed all 24 projects, including those channel/lifecycle checks. These are
not native adapter/scenario tests. Client SHA-256:
`A9A38C59986297915DAF4039B3F0096C7ECD50B40ACEC7AFD72040E09B9F512C`;
dedicated SHA-256:
`B1383AFDE3EC540B73F40CF363A4CBD3F9DDD0BAE824B659D6FF111EE2F810CB`.
No game was launched during this source validation. Build/test evidence is under
`%TEMP%/CoopSpectator/Automation/cml-b5` and `cml-c4`.

The reconciler binds a GUID session, immutable revisioned intent and increasing
native observation sequences. Emitted commands become pending immediately;
only the expected native mission identity or actual intermission clears them.
It never stops battle/foreign missions. A final matching completion and matching
applied aftermath are required before map return; explicit next-stage battle
intent may proceed without an intermediate map only after the prior stage's
aftermath is applied. Applied-result proof is retained for that exact battle
identity, not inferred again from mission absence. It retains up to 4096 issued
battle identities without eviction to reject replay, then fails closed. Inputs
are bounded to 256-character identity keys. Timeouts/rejections are terminal for
that reconciler, not automatic retry authorization. No engine or I/O calls exist
in this class. Safe adapter integration and recovery remain separate work.

## Exact native inspection for adapter integration

Installed dedicated `TaleWorlds.MountAndBlade.ListedServer.dll` was inspected
with ILSpy on 2026-09-26. `ServerSideIntermissionManager.IsNewTaskAssignable`
returns `_currentTask == null`. `StartMissionAux` additionally requires registered
and playing listed-server state plus active `IIntermissionState`. It sends
`LoadMission` to clients before `Module.CurrentModule.StartMultiplayerGame`, then
waits until `Mission.Current.CurrentState == Continuing`. Its battle index wraps
from 10 to 0, so that index cannot serve as unique mission ownership.

`EndMissionAux` requires registered/playing state, active `MissionState`, a
non-null non-ended mission, then calls `MissionLobbyComponent.SetStateEndingAsServer`
and waits for `Mission.Current == null`. Therefore CanStart/CanEnd adapters must
check the complete native prerequisites and cannot treat a command call or
HTTP acknowledgement as completed teardown. Preserve the existing pre-mission
topology publication before native LoadMission; no direct StartMultiplayerGame
shortcut is planned. The test-only control bridge's narrower readiness checks
are evidence to inspect, not a production adapter to enable wholesale.

Campaign `BattleDetector.TryConsumeBattleResultWritebackAudit` calls
`TryMarkConsumedAfterSuccess` only after applying writeback and determining
pending lords-hall state, then completes the application lease. The AlreadyApplied
branch validates the result journal separately. The host adapter must bind those
facts back to the exact requested battle key; merely reading any old result file
or observing Mission.Current == null is insufficient.

Inspected assembly SHA-256:
`C7D27584FCE431B2D3734EB88C8DF52EF3B1BC8C5729F7FCE690CC277DA577E3`.

## Foundation validation

`cml-c2` passed all 24 canonical projects, including 4,305 lifecycle assertions.
They exercise initial readiness, command origin/mission identity, one-shot
dispatch, delayed opening/teardown, stale sessions/revisions, immutable intent,
authoritative completion versus disappearance, aftermath gating for map and
next-stage battle, no premature intermediate map, replay of recent/old battles,
4096-entry history capacity, foreign missions, rejection, timeout and clock errors.
The nine scenario labels use the same pure transition trace; they are NOT native
field/village/siege/hideout regression tests and do not prove adapter routing.

`cml-b3` compiled both modules with unchanged installed inventories, no product
launch and no warning against the new lifecycle class. Client SHA-256:
`21486B60A5A4F6731227177D45C9E742DC759174AE1B716E1BFA879DFCC4E640`;
dedicated SHA-256:
`527683E618234008D33A180617B1DBF08BE0E724637B74FA2EB7BF4C9A7C295A`.
Both artifacts remain source-only; installed cmt-b7 is unchanged and no native
role runs. Intermediate cml-b1/b2 and cml-c1 are superseded by these final runs.

Historical foundation boundary: the subsequent adapter section above supersedes
the former no-runtime-caller status. Automatic startup still requires native
proof. Default-off dialogue diagnostics from cmt-b9 also need native verification.

## User-visible contract

Starting the campaign-owned dedicated server while the host is on the global
map must automatically open the replica mission once the server is ready.
Connected players stay in the same server session. A cooperative battle replaces
the map mission with the existing scenario-specific battle mission. Once the
final battle stage is finished, its result applied, and the host has returned to
the global map, the server reopens the replica mission automatically.

Read-only map dialogue remains part of the map presentation; it is not a battle
transition. There is no voting, trading, progression or separate inventory in
this increment. Unsupported encounters must not silently start a fallback battle.

## Source-verified integration points

- `DedicatedHelperLauncher.BuildStartupConfigContent`: start_game, no automatic
  start_mission. Preserve the normal menu-owned server process and exact ownership.
- `CoopCampaignMapPrototypePublisherBehavior.PumpApplicationTick`: host map
  snapshot publisher, currently requires no Mission.Current; not a mission owner.
- `DedicatedServerCommands.SendStartCampaignMapPrototypeMission`: selects map
  options and sends start_mission. HTTP acceptance is not native completion.
- `BattleDetector.TrySendBattleStart` and
  `TryStartDedicatedMissionForCampaignHost`: scenario/roster preparation and
  existing battle start. They must participate in the same transition owner.
- Result application, mission-exit guards and pending lords-hall/hideout stages
  must block a premature map return. Mission.Current == null alone is insufficient.
- The test-only dedicated control bridge has native ListedServer intermission
  and IsNewTaskAssignable checks. It must not be enabled as a production shortcut.

## Required design

One serialized transition owner must reconcile **desired host state** with
**observed dedicated state**. Suggested logical states: WaitingForServer,
OpeningMap, MapActive, ClosingMap, OpeningBattle, BattleActive,
AwaitingAftermath, ReturningToMap, Faulted. These are a design outline, not names
of implemented classes.

Bind requests and observations to a fresh campaign/server session, exact owned
server identity, monotonic request revision and the existing exact battle/scene
identity. Reject stale data from a previous process, save, battle or restart.
No command success, HTTP timeout, fixed delay, client join or map snapshot may
substitute for a native mission-ready/closed observation.

Battle intent takes priority over map intent. Stop only the owned map mission,
wait for its actual teardown and native intermission readiness, then start the
already validated scenario-specific battle once. Never end an unknown, manual,
hero-creator or unrelated battle mission. Never overwrite its options first.
Map return requires final authoritative battle completion, existing campaign
writeback completion, no pending follow-up stage, and confirmed global-map state.

Disconnecting a player does not command mission replacement. A server failure
must surface a bounded error, not an unbounded restart loop. Session shutdown
invalidates pending work. The existing opt-in map feature boundary remains until
the lifecycle has native acceptance evidence; rollout/default changes are separate.

## Implementation and validation order

1. Inspect exact native server readiness/lifecycle and every affected scenario.
2. Implement a pure transition decision contract with one-shot request handling,
   stale identity rejection and explicit failure/recovery outcomes.
3. Wire host intent and dedicated observations without hot-path blocking HTTP,
   unsafe native object sharing, or independent competing start/end callers.
4. Add transition contracts plus focused adjacent-scenario source guards.
5. Run all 24 existing contract projects and both isolated CompileOnly builds.
6. Deploy a coherent candidate with exact process/config ownership and retain
   evidence for startup without console, map -> field -> map, repeated battle,
   late join/reconnect, abort/retreat and multi-stage scenarios below.

## Acceptance checklist

| Requirement | Status |
|---|---|
| Map starts after server readiness without a console command | Satisfied for cml-b6 first startup and later client join; repeat/scenario coverage remains open |
| No duplicate starts during slow load/repeated state observations | Not Satisfied |
| Connected clients transition map -> exact battle -> map | Satisfied for one cml-b6 normal field victory; repeat and other scenarios remain open |
| Return waits for result application, including retreat/abort | Partially Satisfied: normal field result applied before return; retreat/abort unverified |
| Field, village, sally-out, ambush and relief preserve existing routing | Not Verifiable |
| Siege assault -> interior/lords hall does not insert a map prematurely | Not Verifiable |
| Day/night hideout and boss stages preserve existing routing | Not Verifiable |
| Stale server/save/battle requests and unknown missions fail closed | Not Satisfied |
| Join/reconnect, shutdown and repeated battles have bounded recovery | Not Satisfied |
| Exact-owned cleanup and config/save restoration for the new run | Satisfied for r2: eleven recorded identities exited, seven configs match; permitted test saves retained |

Test-save policy update: the user explicitly classifies the active campaign as
test-only and permits test progress/autosaves to remain. Future runs need not
roll those saves back. Preserve unrelated campaigns; still restore temporary
game configuration and remove only exact-owned test processes. The completed
cmt-b7 restoration is historical evidence, not a future save-restoration rule.

## Documentation impact

Read: README, GOAL_MODE, RUNTIME_FLOWS map/battle/completion sections,
INVARIANTS_AND_RISKS, BUILD_TEST_DEBUG. Update RUNTIME_FLOWS for transitions,
CODE_MAP for new coordinator/contract, ARCHITECTURE for authority/ownership,
INVARIANTS_AND_RISKS for teardown/aftermath gates, and this checklist with exact
implementation/test/native evidence. README links the current evidence. Existing
historical battle reports remain unchanged; they do not verify this new lifecycle.

Source adapter integration updates CODE_MAP, README, ARCHITECTURE,
RUNTIME_FLOWS and INVARIANTS_AND_RISKS. These describe source ownership, not
native acceptance. The acceptance checklist remains open until measured in game.
