# Native-ended combat and network mission teardown

## Proven failure,2026-10-27 r63/b287

User reports no automatic return to the map after siege-engine ambush withdrawal
behind the castle gates. Exact roles: campaign75556, dedicated67468, MP78148.
Retained evidence lives under `artifacts/runtime/map-portrait-r63-20261027/evidence`.

-22:12:44.528 dedicated converted manual Retreat into castle withdrawal.
-22:13:02.592 controller reported withdrawal completed.
-22:13:03.279 native log says `CheckMissionEnded::ended`.
-22:13:03.318 dedicated wrote result; normal result-publication callback follows.
-22:13:03.457 campaign applied ambush casualties/combat progression and requested
  native mission exit;22:13:13.756 consumed-result journal audit confirms success.
- Terminal intent.bin has Kind=Map, HostOnGlobalMap=true, AftermathApplied=true,
  AppliedBattleKey equal to BattleKey, FollowupStagePending=false. Result is
  SiegeAmbush, IsFinalStage=true, DefenderPushedBack=false, Defender winner.
- Dedicated never dispatched EndBattle after this result. Previous village and
  other completed-stage sequences did dispatch EndBattle/StartMap in the same PID.

## Low-level cause

Mission.CheckMissionEnded sets MissionEnded=true **before** OnMissionResultReady.
This indicates combat completion; the native mission may remain Continuing and
its multiplayer lobby may remain Playing. Existing owned-session close guards
correctly prevent independent lobby ending before result application, but the
adapter incorrectly required !MissionEnded to expose CanEnd.

Removing that adapter check alone is insufficient: installed listed-server
ServerSideIntermissionManager.EndMissionAux has the same !MissionEnded guard and
would silently skip its lobby-ending call. Decompilations are retained under
`artifacts/native/settlement-menu-overlay-20261027` and
`artifacts/native/lifecycle-opening-20260927`.

Native MissionLobbyComponent.SetStateEndingAsServer starts its post-match timer,
broadcasts MissionStateChange(Ending) and invokes OnPostMatchEnded. Its UDP timer
calls the server component EndGameAsServer, which broadcasts UnloadMission,
unsynchronizes peers and ends the multiplayer lobby mission. No MissionEnded reset
or direct native unload is needed.

## Source candidate b288

CoopCampaignMissionClosePolicy distinguishes normal listed-manager closure from
the owned, native-ended-but-Continuing lobby path. Both require manager/listed
readiness, active MissionState, exact owned mission, lobby availability and no
already-Ending lobby. Ended missions in teardown are not re-closed.

CoopCampaignMissionServer rechecks these conditions at dispatch and calls the
native lobby Ending method under its existing exact _dispatchClosing scope only
for the native-ended route. The original manager route remains for normal active
missions. The result publication + applied journal + exact battle key lifecycle
gate is unchanged. Explicit save reload uses the same dispatch helper after its
separate abandonment authorization. No timer substitutes for persistence.

Runtime-sensitive change: preserve native Ending broadcast, zero presentation
timer, UDP processing, UnloadMission and teardown observation. Never clear the
MissionEnded flag, replay results, bypass missing acknowledgements or close an
unowned/replaced mission. No new hot-path diagnostic output is enabled.

## Verification and remaining gates

### Native first repeat r65/b289,2026-10-28

Authoritative live roles: campaign79944, dedicated9636, MP41736. Client MVID
c4acdcb5-ee18-4209-b136-125e3885bd20; dedicated MVID
780c619a-bd33-4909-bab2-330fc3e21e02. All are the coherent installed b289 outputs.
Passive observation only; user controlled the battle.

Dedicated native combat ended04:18:40.065. Host journal consumption completed
04:18:48.158. Dedicated Ending broadcast and EndBattle command4 occurred
04:18:48.381, EndGameAsServer04:18:48.392, StartMap command5 at04:18:48.679.
Thus network return started0.521s after persisted application acknowledgement,
not a fixed wait and not before application. The preceding8.093s belong to the
native campaign result/exit/application sequence; do not falsely claim zero time
from combat result detection. Raw logs and the focused timeline are retained in
r65/evidence/ambush-return-*.

Same dedicated PID then handled save reload04:20:05.948, acknowledged cleared
state04:20:06.221, StartMap04:20:07.030 and next StartBattle04:20:28.102. This proves
one native-ended return plus subsequent reload/start sequence, not every scenario
or every subsequent return. The user now explicitly reports that the siege-engine
ambush battle passed successfully. This is bounded native/user acceptance of the
reported scenario; no Goal completion or multi-peer acceptance follows.

Both isolated cmp-b288 builds and all26 cmp-c190 contracts pass. b288 was not
installed: the user requested a combined test with the settlement top panel.
Combined b289 also passes both builds and all26 cmp-c191 contract projects.
Native ambush-return acceptance is recorded above for r65/b289.

Pure contract scenarios cover field, village, siege, sally-out, ambush, relief,
citadel, day/night hideout through exact publication/application admission,
one-shot close and teardown-before-map. Close-policy negatives include missing
readiness/active state/owner/lobby, non-Continuing ended state and existing Ending.
Build and contract run results must be recorded when terminal. Native ambush
withdrawal -> map, another battle, and save reload while the ended mission is
still present remain required acceptance tests. Do not infer native completion
from pure state-machine tests.

Documentation impact: README, CODE_MAP, RUNTIME_FLOWS and INVARIANTS_AND_RISKS link
this canonical cause and adapter boundary. Settlement overlay is an independent
presentation gap; it does not explain the ambush lifecycle failure.
