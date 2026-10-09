# Replica map lobby lifetime

## Confirmed failure (2026-09-29 analysis of 2026-09-28 crash)

Run `map-options-r1-20260928`, cmp-b55, failed stability. Dedicated PID 43376
crashed at 20:08:41 local after the user-assisted rejoin at 20:07:41. The client
later returned to the lobby at 20:10:41. No agent stop caused the server exit.
Do not classify this as a proven reconnect defect merely from temporal order.

The local watchdog dump was copied and SHA-256 verified:
`artifacts/runtime/map-options-r1-20260928/evidence/dedicated-crash-20260928-200841/`.
Dump: 292117085 bytes, SHA-256
`031F4EAC453E833DE72A7AA530B5BE10CF68371EA251127A9F064F4508463CDB`.
It contains private process memory and stays local, outside publication.

`dotnet-dump` maps watchdog instruction 0x7ffc667ed1a8 to
`MissionLobbyComponent.OnMissionTick(float)+0x2d8`. OS thread 0x8b54 holds
`System.NullReferenceException`, with the same instruction in the managed stack.
The invoked MissionCustomGameServerComponent has state Playing and non-null
game-mode/timer references. Its game mode is precisely
MissionMultiplayerCoopCampaignMapPrototype; SpawnComponent and RoundController
are null. The running MissionTimer has duration 1800 seconds.

Installed 1.4.8 native managed code explains the fault: Playing, no round
controller, and `(timer passed || CheckForMatchEnd())` calls GetWinnerTeam then
unconditionally dereferences SpawnComponent.SpawningBehavior. The map inherits
CheckForMatchEnd=false and GetWinnerTeam=null; its construction intentionally
has neither spawn nor round components. Thus the stock match-timeout route,
not a client map input, acquires an invalid battle-end dependency. Wall-clock
duration is not mission-clock duration; do not infer a 40-minute timer.

## Source candidate

`CoopCampaignMapLobbyLifetimePatch` binds the exact declared native tick before
opening the server map. Failure to resolve or patch prevents opening. A pure
policy bypasses only the server's Playing tick when the invoked lobby's own
Mission contains the concrete replica-map game-mode behavior. No global current
mission lookup, option rewriting, huge timer, dummy spawner, exception finalizer,
or periodic diagnostic is used.

WaitingFirstPlayers is unchanged, including native state/timer initialization
and replication. Client inactivity handling remains native. Explicit
SetStateEndingAsServer and OnUdpNetworkHandlerTick are not patched, preserving
the existing ListedServer EndMission route used by campaign map-to-battle
transitions. The guard also covers manually opened replica maps; it does not
depend on the automatic lifecycle flag. A map is passive, not a timed match.

Risk: a Harmony prefix affects a shared native method, although its managed
gate is map/server/Playing-only. Therefore verify actual binding, expiration,
native explicit ending, rejoin, and the next battle in game. Contract checks
and builds alone cannot prove native lifecycle compatibility.

## Adjacent-scenario audit

- Field/village and isolated day/night hideout builders use CoopBattle behavior,
  not the concrete map behavior. Their existing battle-shell timer suppression
  and result logic are untouched. They cannot match this new gate.
- Siege construction includes its required siege SpawnComponent and remains on
  the existing official/native route. No siege timer or ending hook was changed.
- The direct CoopTdm, full TdmClone, and hero-creator stacks also lack a native
  SpawnComponent/round controller in their inspected builders. They have a
  potential analogous timeout dependency; no runtime failure is claimed here,
  and this map-only correction does not silently change their semantics.
- Returning from any battle creates a fresh map; the static patch is idempotent
  and the gate reads that lobby's actual mission. No per-peer lifetime state exists.

## Validation and open gates

cmp-b57 passed both isolated builds with installed inventories unchanged;
cmp-c58 passed all 24 contract projects (10013 lifetime checks). Evidence is
retained in artifacts/verification. cmp-c57/cmp-b56 failed on sandbox NuGet.Config
access before useful validation. Fresh native run map-lifetime-r1-20260929 has
staged cmp-b57 with five module/seven configuration preimages; acceptance pending.

LobbyLifetimeTests exhaust all eight gate combinations, repeated active-map
ticks, native Ending/next-battle admission and adapter/source wiring. This is
managed/source evidence, not an elapsed native timer test.

Native gates: elapsed configured match-time expiry while map stays alive; map input
and dialogue afterward; native Quit/rejoin; explicit map-to-battle-to-map;
unchanged battle ending/result writeback. Full visual and multi-scenario acceptance
remains open. cmp-b55 is not accepted as stable and must not be blindly rerun.

### cmp-b57 bounded native run, closed (2026-09-29)

`map-lifetime-r1-20260929` verified loaded campaign/dedicated/MP identities,
campaign-menu server launch, automatic map opening and native AC_COOP join.
Initial map rendering, party tooltip and manual camera detach/return passed
bounded observation. A stalled live dump collection caused a log gap and client
timeout; it was cancelled. This contaminated disconnect is not ordinary candidate
stability evidence and must not be counted as native Quit acceptance.

The agent rejoined at 05:04:15 into the same dedicated mission opened at
04:47:28.265. A later Avalyps/Losys dialogue rendered matching greeting text,
response labels, speaker and background (paired evidence 08/09). Exact animation
timing and full dialogue/menu lifecycle remain unverified in this run.

Native `SynchronizeMissionTimeTracker` messages, not process uptime, are the
expiry oracle. The clock barely advanced without a connected client. A separate
read-only observer records exact role identities, original mission opening,
fresh map-state receipts and mission seconds; it performs no debugger attachment
or runtime changes. Its 1830-second target exceeds the previous dump-proven
1800-second duration and the initial Playing transition offset. For this fresh
run the 30-minute duration is inferred from installed native defaults and startup
configuration, not measured in live memory.

At 05:29:45 the observer passed at 1839.988 mission seconds, retaining all three
exact role identities, the original single mission opening and a map receipt
only 0.042 seconds old. Paired evidence 12/13 then shows a new host-selected Losys
line reaching MP. Host conversation closure restored the map; wheel detachment,
settlement picking/native hover and explicit host-camera return worked afterward
(evidence 14/15). This is bounded deadline-survival and post-deadline interaction
evidence, not full stability acceptance. Automated Escape again failed to open
the MP menu; the campaign pause menu was observed afterward, so keyboard-target
behavior remains ambiguous. After the user opened the MP menu and explicitly
resumed control, native Quit returned to the lobby at 05:43:51.392 (evidence
16/17). The agent selected AC_COOP by name (its ping was unavailable) and rejoined
at 05:51:49.240. Evidence 18/19 shows the restored host-following map and native
main-party tooltip. Dedicated 49340 retained its exact identity and original
single 04:47:28.265 mission opening; MP clock reached 2774.018 at 05:53:01.518.
This closes the bounded native Quit/rejoin gate after the previous crash deadline.
Explicit native map ending and next field-battle opening also passed: EndMap
command=2 at 05:59:38.282, StartBattle command=3 at 05:59:55.402 in the same
dedicated process. MP reached 29-versus-11 team selection, commander deployment
and Attacker Ready. User-assisted H started BattleActive at 06:02:45.901.
Authoritative victory at 06:03:20.374 (29/0 active), host native aftermath/writeback
at 06:03:21 and automatic StartMap command=4 at 06:05:30.785 closed one
field-victory cycle. Returned-map native castle tooltip, wheel detach and
host-camera return worked (evidence 24–26). Retained writeback audit has two
unresolved defeated-Looter references; no blanket exception-free claim is made.
Controlled modal lifecycle also passed: after the field return, Options was open
before the agent opened, advanced and closed Phorys' host conversation. Options
retained presentation, and Cancel returned to the latest map without stale dialogue.
Wheel zoom/detach, native village tooltip and explicit camera return worked
(evidence 27–33). This does not prove every dialogue/Escape/keybind combination.

Cleanup completed at 06:21 local. Independent postflight confirms all 23 recorded
identities absent, all seven config preimages matching, no new configs or product
processes, and retained installed cmp-b57 hashes. Only inspected safely_exited and
test-autosave-pointer changes were restored; physics_tick_rate=20 and test saves
were preserved. Three exact session-owned tooltip files were backed up then removed.
See [bounded run result](../../artifacts/runtime/map-lifetime-r1-20260929/RESULT.md).
See the run-local README and evidence for diagnostic limitations and ownership.

## Documentation impact

Read README, GOAL_MODE, BUILD_TEST_DEBUG low-level/build guidance, map lifecycle,
escape/modal evidence and relevant code/risk/architecture entries. Update this
canonical failure/candidate document, the index, code map, ownership/risk/flow
summaries and the failed run record. Historical battle reports remain unchanged;
no network protocol, dialogue, camera, or tooltip algorithm changes were made.
