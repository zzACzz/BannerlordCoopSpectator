# Automatic Campaign Map / Battle Mission Lifecycle

## Applied-result close candidate, 2026-10-13 (r15 native verification in progress)

Current r15 supersedes older not-deployed statements below: b96 was installed
and loaded in campaign8772, dedicated32460 and multiplayer30496, with exact
role identities retained in artifacts/runtime/transition-ack-r15-20261013.
User-assisted join rendered the automatic map. A campaign UI field encounter
with22 deserters automatically transitioned the same connected MP client to
battle/team selection and commander deployment. Dedicated EndMap command2
at15:46:11.090 -> StartBattle command3 at15:46:13.301 is2.211 seconds,
compared with the prior r14 server dispatch interval15.251 seconds. This is
not total client loading time. Full cross-scenario fix acceptance remains open.
The first field battle subsequently completed: result publication15:49:10.234,
campaign consumed audit15:49:11.201, EndBattle15:49:11.908, client unload
15:49:12.063. This bounds publication-to-close at1.674s with the consumed audit
before close. StartMap15:49:47.990 and client render-ready15:49:51.590 prove
automatic return, but aftermath UI occupied part of the36.082s between close
and map dispatch; do not label that interval a fixed timer or claim measured
host-map-readiness latency. User advanced aftermath UI. Returned-map interaction
and adjacent scenarios are not yet verified. Cleanup/configuration restoration
also remain pending.

User-controlled repeat entry: EndMap command6 at15:50:51.329 -> StartBattle
command7 at15:50:51.662 (0.333s); client unload/load messages at15:50:51.461
and15:50:51.794. Second completion was then observed at15:52:38.389,
campaign consumed audit15:52:39.179, EndBattle command8 at15:52:39.490 and
client unload15:52:39.639. StartMap command9 at15:52:56.678 was followed by
client render-ready15:53:00.630. Both observed cycles preserve audit-before-close;
second audit-to-close is0.311s. User controls aftermath UI, so return intervals
are not isolated host-readiness measurements. These bounded observations do not
establish adjacent siege/hideout or multi-client coverage.

A third user-controlled cycle is recorded: EndMap15:53:47.772 ->
StartBattle15:53:48.112 (0.340s), consumed audit15:55:47.741 ->
EndBattle15:55:48.064 (0.323s), StartMap15:55:48.625 -> client render-ready
15:55:51.864 (3.239s). The result reports WinnerSide=Defender; scenario type
and post-return interaction are not established by these timing entries.
Later Get-Process and Win32_Process checks both found the three recorded IDs
absent. No agent stop/restart was issued during passive observation. Exit cause,
replacement sessions and configuration restoration remain unverified.

Latest source: b96 passed both isolated builds with installed inventories unchanged.
The automatic-session adapter now installs three dedicated-only Harmony guards:
owned completed-battle warmup ticks are suppressed; owned battle lobby-ending
requests are admitted only inside the reconciler's synchronous close dispatch;
the Ending timer is zeroed only for that exact mission inside that dispatch.
No global duration constant changes. Native Ending broadcast, UDP end callback,
UnloadMission, unsynchronization and actual teardown remain in their normal path.
Start still requires native intermission readiness. Bounded client timing is
recorded above; adjacent scenarios remain UNVERIFIED. This is a test candidate,
not a generally accepted release.

Installed DedicatedCustomServer.MissionCustomGameServerComponent.EndGameAsServer
broadcasts UnloadMission, calls UnSynchronizeEveryone and
BannerlordNetwork.EndMultiplayerLobbyMission. The candidate retains this path.
Native-sensitive risk: ordering now has no presentation grace period; verify
connected client unload/load, late join and multi-stage results in game.
The native client still initially derives its Ending display timer as15 seconds;
early UnloadMission must clear that presentation. No claim of UI verification.
Full contract run transition-ack-c101-20261013 passed all25 projects.
Runtime helpers and exact deployment/loaded-identity evidence are retained under
artifacts/runtime/transition-ack-r15-20261013. The user retained control for
testing, but these recorded processes are no longer live; reconcile current
user activity before deployment, relaunch or configuration restoration.

User accepted the bearing correction and reported two successful field battles
with map return, but requested shorter, evidence-driven transitions.
Live r14 server log22944 shows EndMap command5 at10:56:09.404 and StartBattle
command6 at10:56:24.655. Installed MissionLobbyComponent sets
PostMatchWaitDuration=15 and starts that timer in SetStateEndingAsServer.
Second result publication occurred at10:59:02.407; lobby Ending only at
11:00:02.414, then StartMap command7 at11:00:17.652.
During the intervening minute, native warmup resumed after BattleEnded:
timer activity at10:59:02 and10:59:32. Installed MultiplayerWarmupComponent
includes a separate 30-second EndWarmupProgress timer. This is not evidence
that result persistence needs 60 seconds.

Source now emits EndBattle once only when the exact owned battle has both
server publication proof and a fresh matching host applied-journal acknowledgement,
plus native CanEnd. The adapter rechecks mission and result identity and uses
ListedServer.EndMission. It still waits for actual teardown and native readiness;
pending followup blocks map return, not closing an already applied stage.
This supersedes the historical prohibition on adapter-issued battle ending below
only for this explicitly acknowledged completed-battle boundary.

Focused map contracts passed, including 4,327 lifecycle assertions. New checks
cover publication-only, wrong/stale acknowledgement, elapsed-time non-authority,
native readiness, one-shot close, teardown, replacement/wrong completion rejection,
pending-close timeout and followup map suppression.
These are pure contracts, NOT native siege/hideout coverage or deployment proof.
The shared adapter affects field, village, siege, sally, ambush, relief and
hideout stages; scenario construction and result-application gates are unchanged.

Remaining: native adjacent-scenario and multi-stage verification, late join,
returned-map interaction and final exact cleanup. The scoped timer removal and
warmup/ending guards are implemented in b96 and exercised by the bounded field
observations above. Do not infer broad native coverage from pure contracts.

### Historical interrupted preparation and r14 recovery

Recovery audit after user-reported reboot: transition-ack-b94-20261013 has a
terminal AssertionFailed runner status, not an unknown running build. Both
project logs report NU1301 resolving api.nuget.org; compilation was not validated.
No Bannerlord/dotnet/MSBuild processes were observed in the recovery inventory.
Five of seven r14 config preimages match; engine_config.txt and the dedicated
test config differ and require reviewed restoration. No restoration claimed.

Subsequent recovery completed: the only reviewed changes were physics_tick_rate
90.0000 -> 58.3911 and dedicated map empire_village_o -> battle_terrain_biome_093b
(including its usable-map entry). Exact postimage hashes were admitted; the
cleanup helper preserved postimages and restored both. cleanup.json proves all
seven configurations match, recorded campaign already exited, no owned IDs remain;
test saves and installed modules are retained. Fresh transition-ack-b95-20261013
passed both isolated builds with installed module inventories unchanged. This
validates the applied-result close candidate only, not the remaining native
warmup/presentation-wait changes or a native timing improvement.

## Original adapter design and historical verification

Status: main-thread host/server adapters implemented in source, 2026-09-26.
First native attempt failed at opening-origin verification on 2026-09-27.
Full native acceptance remains open. The separate explicit environment gate
`COOPSPECTATOR_AUTOMATIC_CAMPAIGN_MISSIONS=1` AND the existing map prototype
gate are required. No default rollout is claimed. Current test installation and
verification are recorded in the 2026-10-13 section above.

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

Additional bounded native coverage (2026-09-29): cmp-b57 run
map-lifetime-r1-20260929 survived the original map's former timeout boundary,
passed native Quit/rejoin, then completed one 29-versus-11 field victory on
battle_terrain_biome_093b. EndMap command 2 / StartBattle command 3 preserved
native ending; result writeback preceded StartMap command 4 at 06:05:30.785.
The same MP connection rendered the returned map, native tooltip and camera;
controlled host dialogue open/close under Options also passed. All 23 recorded
identities are absent, seven configs match, no new configs/products; cmp-b57
retained. Defeated-Looter missing-reference audit caveats remain recorded.
This does not close adjacent battle, retreat/abort, multi-peer or full parity
gates. See [result](../../artifacts/runtime/map-lifetime-r1-20260929/RESULT.md).

Additional bounded native coverage (2026-09-28): cmp-b48 run
map-movement-target-r1-20260928 repeated automatic startup and one normal field
victory on battle_terrain_029, 30 vs 15. Authoritative victory at 17:03:50.506,
campaign final aftermath committed at 17:03:51.212, StartMap command 4 at
17:07:34.862 after prisoner/loot closure. Same MP connection rendered the returned
map with 29 members and no defeated patrol; user confirmed working tooltips.
All 22 exact-owned identities absent, seven configs match, no new configs/products;
cmp-b48 retained. This is a separate fresh run, not consecutive-battle/rejoin or
siege/hideout/retreat acceptance. Historical Draw UI was not retested. See
[full local result and limits](../../artifacts/runtime/map-movement-target-r1-20260928/RESULT.md).

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
# Queued-stage teardown regression (2026-10-13)

`MissionLifecycleTests.QueuedStageDuringClose` adds 16 pure assertions for a
next-stage host intent arriving either before or during acknowledged predecessor
closure. The close remains bound to the previous battle/native instance; the
next stage waits for actual teardown, native start readiness and fresh host
authorization, then starts once without an intervening map.

Focused validation: `CoopCampaignMapPrototype.ContractTests` Release passed,
including 4343 lifecycle assertions. Set `COOPSPECTATOR_REPOSITORY_ROOT` to the
repository before invoking this project directly. The first direct attempt
failed without that variable; the next was denied access to synthetic test
files under sandbox restrictions; the permission-approved rerun passed.
No installed modules, game processes or live configurations were changed.
This is ordering coverage relevant to multi-stage battles, not native siege,
hideout or multi-client acceptance. The user retains control of the live test.
