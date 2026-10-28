# Host auto-battle window replica

## Source candidate b302; installed/tester baseline remains b299

2026-10-28: b302 is isolated ONLY, never installed or packaged. Both builds,
all26 c203 projects and eight compiled checks pass; AutoBattleRewardBindings also
runs the original plain scoreboard binding checks. Logs retained under
artifacts/runtime/autobattle-rewards-b302-managed-checks. Installed inventories
remain unchanged. Immutable b299 tester ZIP still hashes to
E672ADB1393F8F8B832F047EE2EFA51C7114895CB7F0CBDFA58E763F8DF3F4FD.

Reward detail audit: exact native GetBattleRewards closures for renown, influence
and morale use12-byte IL: ldarg.0/ldflda/call GetExplainedNumberTooltip/ret.
CoopCampaignAutoBattleRewardCapture admits that exact shape, field type/name,
native owner type/assembly and single delegate target, then reads a boxed value
copy. It NEVER executes the native callback or recalculates rewards. Unknown,
multicast, loot and figurehead providers are rejected. GetChangeValueString writes
the global NUMBER variable; formatting therefore runs inside the existing
main-thread text transaction/language scope, with str_plus_with_number template
admission. Cleanup failure fails the shared capture gate closed.

Only the current visible simulation's terminal BattleResults are inspected. The
ordinary owner/campaign/view/source post-check still fences publication. Prepared
rows are cached by native hint identity for that window; a temporarily busy scope
does not permanently cache failure. Owner/session/hidden/close invalidation clears
the cache. Structured ReplicaTooltipRows use existing bounded CAS1 value encoding;
no delegate/model/reference crosses the wire and protocol71/bridge66 stay unchanged.

Client CoopCampaignAutoBattleHints reconstructs only bounded typed native tooltip
rows; unchanged content keeps the current hint, host replacement updates it,
malformed/removal/modal/disposal revoke it. Commands remain read-only. Tests prove
actual installed native closure admission without Game/simulator/callback execution,
actual native row capture, typed local dispatch, no unchanged-content flicker and
malformed/removal cleanup. Pure CAS1 tests cover row transport/deep-copy isolation.

Not proved: live formatter text/language restoration in a running campaign, native
reward card dimensions/placement/values, result-to-prisoners/loot transitions on
this candidate, loot-share/figurehead/ship detail parity, other-mod providers and
remote-peer behavior. Keep b299 for the current tester run. Do not replace it
until that manual test has ended and a separate native transaction is prepared.

Primary native decompilations/metadata in artifacts/native/map-battle-simulation-20261028:
BattleResultVM, BasicTooltipViewModel, SandBoxUIHelper, ExplainedNumber and
reward-metadata.txt. These are local diagnostic evidence, not package assets.

## Request and native source,2026-10-28

After accepting b292 settlement top-panel/tooltips, the user requests the window
shown while the host chooses Send troops and does not enter a mission personally.
Replicate the live native progress and final result. The host retains simulation
control; clients must not create, advance, pause, accelerate, end or apply another
simulation. This is distinct from the already-replicated prisoners and loot screens.

Primary decompilations retained in artifacts/native/map-battle-simulation-20261028:
GauntletMapBattleSimulationView, SPScoreboardVM, ScoreboardBaseVM, side/party/unit/
stats/skill/ship/sort VMs and ScoreboardScreenWidget. Installed native view owns
SPScoreboardVM and loads SPScoreboard. Its constructor/Initialize/Tick/commands
bind to BattleSimulation and must never run on the replica.

MapScreen retains the actual owner in private _battleSimulationView (MapView).
The Gauntlet override derives from MapView, not the BattleSimulationMapView
placeholder. A typed placeholder lookup can miss the actual UI; use that exact
retained owner and recheck its source/layer/current screen after capture. The
compiled metadata check proves the installed field type without a native instance.

ScoreboardScreenWidget only changes native widget visibility, scrolling gradients
and result animations. No campaign/mission simulation calls occur in its update.
SPScoreboardUnitVM exposes prepared Score/GainedSkills/hero flags, not a hero model
equipment getter. Ship VMs expose prepared health/status/type; their native static
tooltip callback is never evaluated. Sort comparer models are not serialized.

## Candidate b296

Infrastructure/CoopCampaignAutoBattlePresentation.cs defines CAS1: opaque nonempty
window identity plus a bounded detached value tree. It shares the existing checked
value writer/reader with CAF3, but has its own framing and root admission. Require
IsSimulation/ShowScoreboard, IsOver and both prepared side nodes. Reject malformed,
truncated/trailing/cyclic/oversized values and invalid lifetimes. It is an exclusive
AutoBattle member of the atomic menu, not a fake prisoners/loot envelope.

CMN12/protocol71/bridge66 requires coherent campaign, dedicated and client. Existing
tooltip6/catalog10/CAF3/CML2 unchanged. A simulation cannot overlap encounter,
settlement/siege menu, options/plunder/progress or aftermath. Clone preserves owned
progress snapshots. Explicit menu replacement removes the auto-battle screen.

Campaign/CoopCampaignAutoBattleCapture.cs reads only the active exact native
GauntletMapBattleSimulationView._dataSource. Require current MapState, current
MapScreen/top screen, no mission/conversation, the real owner and existing layer.
Capture prepared DataSourceProperty values through a separate exact scoreboard
type policy; no native constructors, Initialize, Tick, Refresh, events, commands,
battle/model traversal, result application or item registry changes. Recheck owner,
campaign and source after capture. Reset window identity on owner/session changes,
hidden state or failure. Preserve the original two-argument CapturePrepared API
for existing aftermath callers/reflection tests; the scoreboard uses the separate
CapturePreparedWithPolicy entry point.

CoopCampaignMapPrototypeMissionView.AutoBattle.cs binds a detached generic VM to
stock SPScoreboard, preserving native layout/result animation and updating existing
children in place. Commands/setters have no host targets. Mouse scrolling remains
local; keyboard focus remains with the replica so local Escape/settings can work.
Auto-battle blocks map hover/camera input, hides campaign menu overlays and
suspends the ordinary tooltip presenter. Conversation/settings/freshness/mission
release close or suspend it. No new hot-path diagnostics are introduced.

Known fidelity boundaries before native acceptance: dynamic reward/ship
tooltip delegates are intentionally not invoked by this first window adapter;
their detached local/host-admitted inspection path remains open. Sort/expand and
control commands are inert; screen content follows the host. No new sprite-category
claim is made from compilation: native resource/layout acceptance must verify the
stock movie on the multiplayer map mission. Do not declare full Goal parity.

## Evidence and remaining checks

b293 client/dedicated builds passed; never installed. b294 corrects the actual
MapScreen owner lookup and builds passed. Its compiled metadata/scoreboard check
passed after loading explicit InputSystem/Native.View dependencies, but the
existing aftermath binding check exposed a changed helper parameter count.
b295 restores the old helper API and uses a new policy entry point. b296 also
rejects noncanonical lifetime IDs and null required root values. Both b296 builds
pass with installed inventories unchanged. All seven compiled checks pass:
AutoBattleBindings, SettlementMenuBindings, DetachedPortraitScope,
MissionStartAdmission, LootInspectionSession, AftermathBindings and
PrisonerInspectionSession. The aftermath script's private recursive Capture call
now supplies a sixth null policy argument; the public helper signature is unchanged.
Logs: artifacts/runtime/autobattle-b296-managed-checks. c196 had25 passes/one new test failure: NUL is
allowed by the existing shared native-text schema, so malformed UTF16 is the valid
encoding rejection test. Corrected c197 and final c198 pass all26 contract projects, including
CAS1/menu transport and progress/terminal/close fixtures for field, village, siege,
sally-out, ambush, relief, citadel, hideout and naval labels. These are shared
presentation scenarios, not proof all native encounter types offer Send troops.

Native test next: user launches campaign/server/MP, chooses Send troops, leaves
scoreboard visible. Compare both sides, unit losses, remaining/wounded/dead/kills,
power bars, time, pause/fast-forward and result/renown rewards. Verify host Done
-> prisoners -> loot -> map; a second simulation, settings, save reload/rejoin
must not retain a previous scoreboard. Ensure replica controls cannot change host
simulation. Verify actual supported field/village/siege/citadel scenarios, not
only a synthetic fixture. Run records must include role identities, loaded module
identity, screenshots, seven config preimages/restoration and exact process cleanup.

## Native r68 closed; next candidate b298

b296 installed coherently with nine verified files and seven fresh config preimages.
Campaign85980 / user-started Dedicated3780 / Multiplayer78392 have matching loaded
candidate identities. Direct native comparison proves the paused village simulation
window and visible unit/party counts match. Host Play advanced to Victory with
6.4 renown and3.7 morale. Client terminal/rewards and post-simulation transitions
are not yet directly verified. User confirmed live numeric progression matches
campaign exactly. r68 CLOSED: all roles exited and seven config hashes match.
Only inspected DS map settings were restored. See the run OBSERVATION.md.

b298 adds plain prepared HintViewModel field replication and independent local
hover. No native hover methods or dynamic tooltip delegates are invoked.
CoopCampaignAutoBattleHints owns detached text, refreshes changed text without
restarting unchanged hovers, and revokes on node removal, modal or disposal.
Native base command lookup requires explicit ExecuteBeginHint/EndHint forwarders
on the generic adapter; simulation commands remain inert. The compiled test
captures the real prepared hint field and checks native dispatch, stable/changed
text, unrelated hover end, modal/removal/disposal and inert combat commands using
injected display callbacks. Both b298 builds and seven compiled checks pass.
c199/c200 found obsolete source assertions for the removed custom summary and
Ukrainian camera labels. Corrected targeted map contracts and final all26 c201 pass.
b298 installed coherently in r69, campaign85732 launched. Native acceptance pending.

User also requests English replica-only camera controls and removal of the custom
click-pinned summary. Native hover/Alt/disclosure remain unchanged. See CODE_MAP.md.
Actual plain hints and English layout still require native acceptance.

2026-10-28 update: user accepts the b298 English controls and removal of the custom
card; no separately observed auto-battle plain-hint acceptance is claimed. r69
closed after all roles exited, seven config hashes match. b299 defaults map and
owned lifecycle ON and is packaged for two-PC testing; see RELEASE_PACKAGING.md.
