# Siege presentation playback investigation

## Latest native result: r44 / b231

The user reports correct, smooth siege firing and confirms that a repeated siege
and opening/closing menus also work. Campaign, dedicated and MP loaded identities
match b231; installed client/dedicated hashes match the staged candidate. This is
bounded user-observed native acceptance, not an independently recorded frame-time
measurement or exhaustive projectile/trail/impact parity. Pause/fast-forward,
castle coverage, assault return, remote-peer delivery and native hook removal
remain separately unverified. Full replica Goal acceptance remains open.

r44 retains seven configuration preimages and module backups. Cleanup is pending;
do not treat this report as authorization to stop user-launched server/MP roles.
Source protocol60/bridge55/catalog10; CSF2/CSP4/CSO10. b231 both isolated builds
and all26 c170 contracts passed. Earlier b217 failures below are historical.

## Latest orientation/scale research (2026-10-23)

Cleanup follow-up: Reset previously stopped before manager unpatch if missile
unpatch threw. It now revokes callbacks first, attempts both independent removals,
and retains the Harmony owner on a failure instead of claiming successful removal.
Fault-path tests pass for neither/either/both removals failing, abort throwing,
callback revocation, repeated reset and preserving the exact native exception.
These are substitute/delegate checks, not actual native unpatch validation.
The focused test emitted NU1900 because vulnerability metadata was unavailable;
execution exited successfully. b231 isolated client/dedicated compilation passed
with installed inventories unchanged. Aggregate c169 predates this cleanup change.
Final c170 passed all26 contracts including cleanup fault paths. Fresh r44
preparation script and acceptance checklist now exist locally, pinned to b231/c170.
They have NOT been executed to deploy or launch. Elevated inventory found no
game/server roles; installed b217 remains. The scoped launch must explicitly enable
the flight observer in the campaign; a plain manual launch would not test this path.

Verification follow-up: siege-flight-scale-c169-20261023 passed all 26 canonical
contract projects, including observer ownership/parallel substitute tests and
flight replay orientation/scale tests. Offline inspection of b230 against installed
SandBox.View MVID 4b56e072-e521-4518-8c8c-171ba8e3f483 again matched the unique
missile producer (936 original -> 941 prepared instructions). No patch was installed
by that check. Native hook installation, partial rollback/lifetime acceptance,
smoothness and projectile-trail/impact fidelity remain unproven. No game launched
or installed module changed by these checks.

Source now captures prepared prefab scale on the admitted owner thread, retains it
by shot identity and attaches a detached copy to each published flight. A typed
reflection boundary avoids adding a SandBox assembly dependency to the client.
CSF2 (81 bytes), CSP4/CSO10 and protocol60/bridge55 carry optional presentation
scale; missing scale preserves the earlier position-only path. Rendering uses
TryFrame with the observed projectile up axis only during the flight interval,
leaving actual pre-launch attachment frames unchanged. b229 failed due to the
direct SandBox type dependency; corrected b230 builds both roles with installed
inventories unchanged. Full map contracts passed including the additional replay
orientation, nonuniform-scale and attachment-immutability assertions; native appearance, lifetime and all26 regression remain
pending. Installed b217 is intentionally unchanged and incompatible with this
candidate. The earlier helper-only statements below describe previous stages.

Installed SandBox.CampaignMapSiegePrefabEntityCache.GetScaleForSiegeEngine
returns prepared scale vectors from the scene's machine prefabs, not from the
projectile's current attachment frame. Onager/FireOnager use attacker mangonel;
Catapult/FireCatapult use defender mangonel; Ballista/FireBallista select by side;
Trebuchet uses attacker and Bricole defender. Unknown types return zero. Capture
must read the admitted MapScreen cache on its owner thread and validate the result;
do not replace it with unit scale or instantiate additional native prefabs.

SettlementVisual points forward along position(t+0.1)-position(t), then calls
Mat3.Orthonormalize and applies the cached scale. The look-ahead may cross the
impact time, although displayed flight membership remains strict. The new pure
TryDirection helper implements that chord, not the instantaneous derivative.
Full map contracts passed after adding chord, rotated-world-axis, endpoint and
near-impact tests. TryFrame now composes the native cross-product basis and
independent local-axis scales from explicitly supplied native up/scale values.
Installed Mat3.Orthonormalize and ApplyScaleLocal were inspected directly;
full map contracts pass for the basis order, nonuniform scale, input isolation,
invalid scale/basis and impact boundary. Neither helper is used by the renderer
yet: exact scale capture/transport and native visual acceptance remain open.
Installed b217 and the closed r43 configuration state are unchanged.

Status: sampled translation playback candidate; full smoothness NOT runtime-accepted.
User r43 test: machine presentation looks normal, but projectile motion is jerky
and some shots disappear/are missing. Sampled b217 playback does not meet the
projectile fidelity requirement. This is bounded user evidence, not yet measured
per-shot transport/render loss.
Installed candidate is b217 in staged r43; the user is launching the manual test.
The full replica Goal remains open.
Final phase-integration b217 isolated client/dedicated builds both passed with
installed module inventories unchanged during compilation. Subsequent r43 staging
installed both roles; c168 passed all 26 contract projects. Native acceptance is pending.

## Follow-up impact and tail evidence (2026-10-23)

Rechecked native SettlementVisual missile tick against current capture/renderer:

- Native computes previous/current flight membership from campaign time and the
  missile CollisionTime/FlightDuration. Its transition out of flight emits a burst
  at the entity frame and selects a hit sound. TargetType selects either
  psys_game_ballista_destruction or psys_campaign_boulder_stone_coll.
- Native hides the projectile root at the end of that tick. Current capture skips
  hidden roots entirely, so the impact transition and its location/type are not
  represented in CSP2. The client deletes omitted roots instead of retaining them.
- Thus an empty visible set cannot distinguish collision from siege exit, modal
  hiding, loss of visibility or another lifecycle transition. Adding a burst on
  omission would create false impacts in those adjacent scenarios.
- Hidden native roots do not by themselves prove that residual particles continue
  rendering. A fixed retention timer would be a hypothesis, not a faithful fix.

Next implementation gate: obtain explicit host-observed impact evidence with a
stable event identity, location and effect selection before adding client bursts.
Check whether a narrowly scoped native presentation-event observation is needed
to retain transitions between 0.2-second publications. Never infer damage or hits
from disappearance or replay historical impacts on reconnect. This is research,
not an implemented event channel or an accepted cause of the missing trail.
Installed b217 remains unchanged during the user's test.

Machine follow-up b216/b217: capture binds visible models to the current managed
SiegeEngineConstructionProgress in the deployed side/ranged-or-melee slot, rechecks
that reference and assigns a bounded instance ID. CSM2/CSO8/protocol58/bridge53
carry InstanceId and LastBombardTime.ToHours (negative sentinel normalized to zero).
Zero instance IDs are untracked exact-render samples, never interpolated.
Renderer recreates an owned root when its instance changes even if prefab/slot
are unchanged. Shared playback now blends nondecreasing phases only for matching
instance, prefab, cycle and clip. Tests cover phase midpoint and replacement/cycle
boundaries; full map tests pass. b216 both isolated builds passed. Orientation,
missed short flights, effects and native performance/fidelity remain unverified.

b215 playback: `CoopCampaignMapSiegeProjectilePlayback` is called by the replica
menu tick. It buffers translation between admitted same-shot flight samples for
their host sample interval (at most 0.75 seconds), using local receipt time. It
never extrapolates. Pause/rate/epoch change, large gaps, attachment boundaries and
new shots snap to the authoritative endpoint; stale same-epoch timestamps are
ignored. Modal hide and teardown clear history. Full map tests and both isolated
b215 builds passed, installed inventories unchanged. This is linear translation
only, adding about one sample interval of delay. Orientation, machine phases,
unobserved short flights, residual tails, impact particles/sounds and native
visual/performance acceptance remain open. It is not a proven particle-trail fix.
The foundation/timing-only statements below describe their earlier stages.

Timing update b214: `CoopCampaignMapSiegeTime` is captured by the overlay owner
and transported as an optional CSO7 suffix (protocol57/host-bridge52). It contains
a siege-reference/reversal epoch, monotonic Stopwatch sample seconds, campaign
hours and the existing admitted campaign time scale (zero means stopped). The
value is detached/deep-cloned and invalid/nonfinite fields fail admission. It is
not yet consumed for playback. Sample time is host-local, not synchronized UTC;
the client must map it to local receipt time rather than subtract unrelated clocks.
The full map-contract executable and both isolated b214 builds passed, installed
inventories unchanged. No native pause/smoothness acceptance is claimed.

Foundation update: `CoopCampaignMapSiegeShotIdentity<T>` now supplies bounded,
reference-based batch identities with atomic rejection, epoch replacement and
monotonic non-reuse across Clear. It is now connected to host capture and CSP2
transport. CSO6/protocol56/host-bridge51 reject mixed old/new components. Client
visual keys now use ShotId rather than list index. No interpolation is added yet.
Capture assigns IDs over both complete managed missile populations, including
hidden shots, after list/owner revalidation. It clears on observed inactive state
and campaign-time reversal and changes epoch when the siege object changes.
New tests cover value-equal distinct objects, reorder/compaction, departure,
reappearance, invalid batches, capacity, epoch changes and Clear. The full map
contract executable passed on 2026-10-23 after a sandbox file-access failure was
resolved by an elevated rerun. Final wire-validation tests also pass. b212 client
compilation failed because CampaignTime.NumTicks is internal; the candidate uses
public ToHours for reversal detection instead. No installed change/native acceptance.
The corrected b213 isolated client and dedicated builds both passed, with installed
module inventories unchanged. Native runtime identity and visual continuity remain
unverified; b211 remains installed.

## Evidence

- `CoopCampaignMapPrototypePublisherBehavior.PublishIntervalSeconds` is 0.2.
  Siege machine/projectile capture runs in the publication path.
- `MissionView.SiegeMachines` stops local skeleton playback and sets sampled phase.
  `MissionView.SiegeProjectiles` applies sampled world frames without interpolation.
- r42 host diagnostics find one particle component on child zero for ordinary
  mangonel, fire mangonel and ballista projectiles, in both flight visibility states.
  User reports a trail fragment and possibly jerky motion. Replica component
  counts were not captured after manual replacement of the failed-login client.
- Installed CampaignSystem decompilation retained in
  `artifacts/native/siege-playback-20261023/` shows `SiegeEvent.SiegeEngineMissile`
  is a class with readonly ShooterSlotIndex, ShooterSiegeEngineType, TargetType,
  TargetSlotIndex, CollisionTime and FireDecisionTime. It has no explicit shot ID.
- `BesiegerCamp.RemoveDeprecatedMissiles` uses RemoveAll on past CollisionTime.
  Thus surviving list entries shift index. Current side/index rendering keys cannot
  be used as persistent interpolation identity, even within one firing sequence.
- Native SettlementVisual uses the same teleportation=true frame setter as the
  replica. This flag alone is not an established cause. Native roots are retained
  and hidden; replica omission deletes them. Tail-particle lifetime differs.

## Implementation requirements

Source foundation added: `CoopCampaignMapSiegeFlight` evaluates only the strict
host-defined flight interval using detached launch origin, orthogonal unit axes,
speed, launch angle and gravity in campaign-hour units. It rejects invalid inputs
and out-of-map results. `SiegeFlightTests` covers analytic midpoint, boundaries,
short flights and invalid axes/time/gravity. Capture, transport, orientation and
client integration are NOT connected; installed b217 is untouched. The complete
map contract executable passed after rerunning with access to its temporary files
(the sandbox run passed the flight checks but later hit Access Denied).
This is not a runtime fix.

Parallel handoff foundation: `CoopCampaignMapSiegeFlightBuffer` admits at most 256
detached trajectories under a fresh reference token per owner-started batch.
Synchronous deep copies protect against subsequent DTO mutation. Duplicate IDs,
invalid values and overflow invalidate the entire batch; stale/closed tokens are
rejected. Finish transfers values after the native join; Clear revokes admission.
Tests exercise concurrent submissions, capacity, duplicate rejection, deep-copy
isolation and token lifecycle. Native observation/identity wiring is still absent.

Next correction direction after r43: capture the host's prepared launch timing
and trajectory, not merely visible positions. Native
CalculateDataAndDurationsForSiegeMachine supplies LaunchGlobalPosition,
TargetAlignedShooterGlobalFrame, MissileSpeed, Gravity, LaunchAngle and
FlightDuration; CollisionTime bounds the observed shot. Investigate a narrowly
scoped observation of that native calculation (including thread/caller ownership)
instead of duplicating its target-selection/height/animation calculations.
Preserve unseen-host visibility restrictions and bounded session lifecycle.
Publication cadence is 0.2 seconds; current playback requires two visible flight
samples and immediately removes omitted shots. Therefore smoothing cannot recover
a flight absent from those samples, and omission can cut a buffered segment short.
Increasing publication frequency alone cannot guarantee full-flight coverage.
Native hook/transport/render integration and tests are not implemented yet.

1. Establish a bounded host-assigned shot identity within a siege/session epoch,
   tracking actual managed missile identity and pruning departed objects. Never
   expose native pointers, infer impacts from list disappearance, or rely on
   side/list-index. Detect siege change, save reload and clock reversal.
2. Carry sample campaign time and presentation time, plus explicit paused state.
   Align machines and projectiles on one playback timeline. Do not independently
   run local clip speed through a paused host or infer elapsed time from time of day.
3. Smooth only admitted samples of the same shot/animation segment. A short
   buffered timeline must not interpolate across reload/fire clip changes, index
   reuse, missing state, session change, or machine replacement. Bound buffering
   and staleness. Preserve exact authoritative endpoints; do not simulate damage.
4. Very short flights can occur wholly between samples. Investigate native
   prepared bombardment trajectory/timing capture so fidelity is not limited to
   interpolating two points that may never both be observed. Never use client
   campaign gameplay state to reconstruct a different battle outcome.
5. Verify root/child effect initialization separately from smoothness. Preserve
   authored particles without blindly enabling callbacks/physics or duplicating
   an existing emitter. Tail persistence, impact bursts and sound remain separate
   fidelity requirements; current code does not implement all of them.

## Change surface and risks

Client trajectory playback candidate: retained flights are evaluated against the
buffered host campaign-hour interval, including a flight entirely between two
publications. Ended/future trajectory-only roots are omitted; first join uses
current host time rather than replaying delivery history. Pause stays at fixed
campaign time. CSP3 presence flags are 0=no flight, 1=current native root+flight,
3=trajectory-only retained root; other values reject. Current pre-launch attached
roots remain available, while retained hidden roots cannot appear before launch.
Full map contracts pass for the between-samples flight, analytic position, end
removal, late join and pause. Native scale/orientation still use sampled root
metadata and need correction/verification; trail, impacts and performance remain
open. b228 both isolated builds passed with installed inventories unchanged;
do not claim native smoothness.

Publication source integration now attaches journal trajectories to visible shots
and appends retained completed shots with detached prefab/frame metadata. Metadata
is sampled on the owner thread from native projectile roots (including prepared
hidden roots of the admitted visible siege); stale root indices beyond the current
managed list are skipped, as native ticking hides those retained roots. Historical
shots with Flight may share side/index but never ShotId; untracked current roots
still reject duplicate positions. Metadata capacity is bounded by active/journal
populations and the final projectile-set cap. Client must NOT display historical
roots until trajectory-time admission is implemented: renderer still ignores Flight.
Do not deploy/enable this intermediate state. b226 built before the stale-root
correction; final b227 both isolated builds passed with installed inventories
unchanged, and full map contracts passed. Client rendering remains incomplete.

Trajectory wire integration: projectile DTO has optional deep-cloned Flight;
CSP3 adds a strict presence byte and fixed CSF1 payload. CSO9 and protocol59 /
host-bridge54 require coordinated roles; catalog10 is unchanged. Dedicated build
includes the detached flight model/codec. Full map contracts pass after updating
the intentional version assertions. Tests cover optional trajectory roundtrip
inside a projectile and malformed/truncated sets. Capture still does not attach
the journal to outgoing items; renderer does not consume Flight yet. b225 both
isolated builds passed with installed inventories unchanged; installed b217
remains on58/53 and is untouched.

CSF1 trajectory codec foundation: fixed 68 bytes (magic, two double campaign times,
three float vectors, speed/angle/gravity). Flight model now exposes public properties
for eventual host-file serialization. Exact length, version, finite/orthogonal
geometry and ordered times are validated. This codec is not attached to CSP2/CSO8
yet; no protocol bump or deployed wire change. Tests cover roundtrip position,
all truncated lengths, trailing bytes, wrong magic and NaN time.

Delivery-history source: CoopCampaignMapSiegeFlightJournal retains at most 256
deep-copied trajectories for two monotonic seconds after their last observation.
This is a bounded delivery window, NOT an imposed visual delay or collision timer.
It rejects invalid/overflow batches and clock reversal; epoch mismatch cannot
read another siege. Owner capture merges after the native join and clears on
failed admission, modal/visibility/siege departure, reversal and release. It still
does not transmit these records or replay them on clients. Client join-baseline
and time admission remain mandatory to avoid historical-shot replay.
Full map contracts pass for copy isolation, short-shot retention, expiration,
epoch mismatch, reversal and Clear. b224 both isolated builds passed with installed
module inventories unchanged. Native retention/visibility acceptance is pending.

Owner wiring source now exists in ProjectileCapture.Flights: capture construction
attempts the explicitly gated hook; manager prefix admits the current visible
player siege on the construction thread, assigns IDs using the same registry as
visible projectile publication, and snapshots managed missiles. Postfix revalidates
map/siege/screen/visual state after the parallel join and retains detached values.
Publisher replacement/reset releases hooks and references. Shared campaign-time
reversal tracking prevents observer/publication IDs diverging. Detached flights
are not attached to the wire model yet; retaining every short shot across slower
publication and rendering its full interval remains unimplemented.
b222 builds passed before the final shared-clock adjustment; final b223 both
isolated builds passed with installed inventories unchanged. Substitute coordinator tests pass after managed-only Clear was made safe
for worker-triggered abort. No installed module changes or native acceptance.

Explicit installation adapter `CoopSiegeFlightObservationPatch` is source-only.
It requires both the map feature and COOPSPECTATOR_SIEGE_FLIGHT_OBSERVER=1,
validates original missile-call shape, and prepares manager prefix/postfix/finalizer
plus the missile-only insertion. Callbacks are supplied by an owner; no startup
caller exists yet. Native exceptions are returned unchanged by the finalizer;
observer callback failures trigger abort rather than escaping through native Tick.
Owner lifecycle, partial-install rollback fault tests and real native installation
remain unverified. b221 both isolated builds passed with installed inventories
unchanged; installed b217 remains untouched.

Observation coordinator source: `CoopCampaignMapSiegeFlightObservation` accepts
an exact visual, managed missile population and caller-assigned shared shot IDs
on its construction thread. Collision times are copied before worker execution.
Workers perform reference lookup and value decoding only; no Campaign.Current,
native visibility queries or missile-field traversal. Finish/Clear revoke the
batch token. Caller MUST join workers, revalidate siege/visibility/session and
share the publication identity registry. This coordinator is not wired to native
manager prefixes/postfixes or publisher. b220 isolated client/dedicated builds
passed with installed inventories unchanged. Coordinator runtime behavior is
not yet accepted.

Coordinator substitute tests now pass in PortraitObservation.ContractTests:
worker-thread admission is refused; a worker may submit only the exact admitted
visual/missile reference pair; normalized prepared axes and collision-minus-duration
produce the expected time interval; Finish cannot repeat; malformed boxed data
invalidates the batch; Clear revokes observations and duplicate missile references
are rejected. Tests execute callbacks on Task.Run and join before Finish, but do
not prove the actual manager hook's native lifetime/visibility behavior. Latest
offline matcher also passes against b220 (936 -> 941 instructions), including the
added signature checks.

Source-only `Patches/CoopSiegeFlightCallSite.cs` now validates the two native
calculation calls and finds a unique missile producer sequence by field names,
field/parameter types, local types and side getter. It prepares a copied IL
sequence inserting a detached observer after that calculation; it never installs
a patch. Unknown or ambiguous shapes are rejected. b219 both isolated builds passed
with installed inventories unchanged;
synthetic rejection tests and native IL matcher execution remain required before
installation. A no-throw observer, owner admission and parallel join lifecycle
are still unimplemented, so this is not enabled at startup.

Follow-up substitute test passed: PortraitObservation.ContractTests now includes
SiegeFlightCallSiteTests, which patches an isolated two-call fixture and confirms
both native calculations/results remain unchanged and exactly one missile is
observed. Missing/extra calculation calls and a changed field producer reject;
original instructions are not mutated. This is not installed-native execution.
NuGet vulnerability metadata was unavailable (NU1900); test process exited 0.

Offline native matcher verification passed using isolated b219 and
`scripts/Test-SiegeFlightNativeCallSite.ps1`: installed SandBox.View MVID
4b56e072-e521-4518-8c8c-171ba8e3f483, 936 original instructions, 941 prepared
instructions. No patch was installed and no game was started. Script loads the
candidate Harmony before inspection to satisfy JIT dependency resolution.
This validates b219's native shape matcher, not emitted native execution, the
later extra signature guards, thread lifecycle, visibility admission or transport.

Installed SandBox.View IL inspection confirms two calculation calls inside Tick:
the missile call at IL_00e2 consumes managed SiegeEngineMissile local9 and writes
SiegeBombardmentData local11; the machine-animation call at IL_0782 writes local43.
These offsets/local numbers are evidence for this binary, not a portable matcher.
Any patch must validate argument producer types/fields and unique call-site shape.
`Campaign/CoopCampaignMapSiegeFlightReader.cs` now decodes only the exact private
boxed value type and typed fields into detached numbers, using CollisionTime minus
FlightDuration and normalized axes as native rendering does. It does not call the
native calculation, access campaign globals or retain wrappers. Not hooked or
transported; b218 isolated client/dedicated compilation passed with installed
module inventories unchanged. Native capture/visual acceptance remains pending.

Threading gate discovered on 2026-10-23: retained native
`SettlementVisualManager.OnTick` dispatches `SettlementVisual.Tick` through
`TWParallel.For(..., 16)`, then validates dirty visuals after the join. Consequently
a postfix on CalculateDataAndDurationsForSiegeMachine must NOT assume the publisher's
owner thread. It also runs for machine animation, not only missiles; matching only
side/slot/type would conflate calculations and shots. Its output is a private
SiegeBombardmentData value struct, not a public accessible capture DTO.

Required capture boundary before integration: establish an owner-thread admission
token for the exact current siege/visual before parallel ticking; in the native
call path copy only prepared value fields under that token, associate the actual
managed missile identity at the missile call site (not the machine-animation call),
and publish through a bounded synchronized queue. Revalidate token/session after
the native parallel join. No worker-thread global campaign traversal or retention
of GameEntity/Skeleton/Scene wrappers. On unsupported call-site shape disable this
capture without changing original native behavior. Native patching is not implemented
or authorized as a global Scene.CreateBurstParticle interception by this design.
Adjacent gates include other settlements ticked in the same parallel batch, hidden
settlements, both siege sides, modal/assault transitions and save replacement.

Expected changes: host siege capture, detached bounded DTO/codecs, protocol/schema
versions, menu transport and client visual tick/lifecycle, with pure tests first.
Native capture/render modifications are lifetime-sensitive. Do not retain native
wrappers outside the owner-thread visual lifecycle. No new default-on diagnostics.
No installed module replacement while user tests. Use isolated CompileOnly builds.

Documentation impact: update CODE_MAP for actual implementation, INVARIANTS_AND_RISKS
for changed identity/time ownership, and the test runbook for native acceptance.
This design does not itself authorize a completion claim for any of these gates.

## Required validation

Pure tests: reordered/compacted entries, same-slot consecutive shots, simultaneous
opposite-side shots, new siege/session, duplicate/out-of-order samples, pause,
time acceleration, clock reversal, sparse/lost samples and bounded resource use.
Native: town and castle, both sides, ordinary/fire projectiles, pause/resume and
fast-forward, reserve/redeploy/destruction, dialogue/menu transitions, assault/sally
return, siege exit and reconnect. Field/village battles must remain unaffected;
the playback belongs exclusively to the detached campaign-map presentation.
