# Campaign map movement-target presentation

## Status and authority (2026-09-28)

The source candidate now connects host capture, versioned state transport,
server/client disclosure admission and owned native rendering. cmp-b48 completed
map-movement-target-r1-20260928 with bounded point/village/fort, camera/dialogue
and field-victory map-return observations. None of 22 owned process identities
remain; seven configs match and no new configs/products remain. cmp-b48 is retained
with five recoverable module preimages. Full native acceptance remains open;
party-ring motion/alignment, reconnect and other battle types were not accepted.
Current source uses protocol 29 / bridge 24 / catalog binary 8. All three roles
must be replaced coherently before testing; mixed installed/source versions are
not valid evidence.
Local hover rings are a separate implemented feature; see
[hover implementation and native limits](CAMPAIGN_MAP_HOVER_DECALS.md).

The Goal requires host movement targets as part of map parity, not additional
player control. Only the host chooses the target. Clients may inspect the replica,
but must never use their hover, detached camera or input to change the host order.

## Installed-code audit

Selection, scale, palette and atlas behavior were checked against installed
`SandBox.View.Map.MapScreen.TickCircles` and `GetCircleIndex`, retained locally in
`artifacts/native/map-settlement-events-20260928`. Matrix orientation was checked
with ilspycmd against `TaleWorlds.Library.Mat3.CreateMat3WithForward`,
`RotateAboutUp`, and `GetScaleVector`. Installed Library DLL SHA-256:
`AB4FF23E94A6F4B6F293D0CB4333CAE731A0499063FE4A8B6C6A3770DE32C4E5`.
Do not publish decompiled game source.

Native selection order:

1. Point when move mode is 1, default behavior is neither 2 nor 0, no forced
   no-path mode, no AI interactable, no map event, and squared distance exceeds
   `0.01f`.
2. Otherwise visible pursuit party when move mode is 2. A party inside a non-hideout
   settlement redirects to that settlement; a hideout does not redirect.
3. Otherwise default behavior 2 selects its target settlement.

After selection, the replica policy requires the chosen entity to be disclosed.
Denial produces no target or suppression ID; it does not fall back to a different
object. Invisible/absent pursuit may take the native settlement fallback *before*
this disclosure check. Point targets do not suppress local entity hover.

Geometry is not the same as hover geometry:

- Land point: two rings, base scales 0.238846 / 0.278584, atlas indices 4 / 5,
  color 4293993473, multiplied by clamped `(hostCameraDistance + 80)^2 / 5000`.
- Sea point: one stock wind decal at inner scale plus 0.15. Orientation uses the
  host weather model's wind, forward basis and a positive quarter turn about up.
  The foundation rejects absent/zero wind instead of inventing an orientation.
- Party: host CircleLocalFrame side-axis length times 1.2 on land or 2.5 at sea,
  host diplomacy color, reflected atlas sequence 0,1,2,3,4,4,3,2,1,0 at 0.1 seconds.
- Non-fortification settlement: same side-axis length times 1.2, atlas index 5.
- Fortification: full authored global frame and stock fortification material;
  do not apply the unused native intermediate 1.3 scale to that frame.

Non-fortification origin is the target's native position, with event/army position
adders for mobile parties. It is NOT the hover frame's local origin. Native ground
basis replaces Identity.u with the terrain normal and then scales all axes; it
does not reconstruct an orthonormal basis. Detached layout reproduces that rule.

The native animation clock resets only when no entity target is selected; switching
between selected entities does not restart it. The native method samples the index
before initializing an unset clock on the first target tick. Host capture now reads
that existing private managed clock field without changing it; a missing field
clears party presentation rather than creating an independent reset clock. Client
animation advances by monotonic time since receipt; network transit delay is not
estimated. First-tick/frame-perfect phase remains a native comparison gate.
Float division is retained at atlas boundaries (double arithmetic changes .3).

## Source ownership

`Infrastructure/CoopCampaignMapMovementTarget.cs` contains:

- selection inputs/policy and exact selected-ID hover suppression;
- finite bounded scale and animation-index calculations;
- detached presentation inputs and material/frame/texture layout outputs.

Frames returned by layout own their arrays; they do not borrow input frame/origin
storage. There are no native handles, I/O, global state or hot-path diagnostics.
The file is compiled into both production projects and the existing map contract
test project. The following source adapters now call these rules.

`Campaign/CoopCampaignMapPrototypePublisherBehavior.MovementTarget.cs` captures on
the campaign publisher thread only while the map is ready and no mission is active.
It checks current published membership and live native visibility/spottability,
reads stock frame/diplomacy, origin/normal/wind/camera distance and animation time.
Dialogue, missing/invalid visuals or capture exceptions publish explicit absence.
No new native mutations or hot-path diagnostics are introduced on the host.

`Infrastructure/CoopCampaignMapMovementTargetCodec.cs` defines recipe version 1:
bounded canonical base64 (maximum 768 characters), kind/flags, byte-length-prefixed
strict UTF-8 ID, color, tagged fixed-size vectors and bounded time/scale inputs.
The ID length is checked before decoding; no unconstrained array/string length
from the payload is allocated. Unknown versions, tags, shape/type mismatches,
unsafe values, trailing bytes and noncanonical representations are rejected.
Empty string is an explicit clear; null/missing is invalid. Point IDs must be
absent, entity IDs use their exact party/settlement prefixes. Non-point targets
must match current visible dynamic and catalog identities/kinds, including fort
shape. A catalog entry alone never authorizes display.

`MovementTarget` is an immutable encoded field of the host snapshot and network
state. Host text serialization/parsing, validation, clones, ToNetworkState and
CoopCampaignMapPrototypeStateMessage carry it. It shares state ordering instead
of introducing an independently ordered channel. Server admission runs after
catalog/dynamic updates and clears a rejected target or active-dialogue target.
Late join uses the existing state/full-catalog/dynamic synchronization path.

`UI/CoopCampaignMapPrototypeMissionView.MovementTargets.cs` owns up to four lazy
decal entities (two circle slots, one fort, one wind), exclusively in the replica
map scene. It waits for the state's catalog/dynamic revisions, repeats disclosure
admission, and hides after two seconds without a fresh state, on input/dialogue
loss or render unreadiness. Explicit cancellation is carried by a newer empty
state; older target revisions cannot revive it. Same-target local hover is
suppressed only after successful target rendering. A native exception disables
the adapter for that scene and logs its type once. Cleanup releases these entities
before scene teardown and resets cached presentation; no battle scene is touched.

Moving party rings use the rendered replica party position plus the captured host
offset between native target origin and native visual position. This avoids a
0.2-second marker stream outrunning the 0.5-second party stream and retains the
native event/army offset. Point/village origins and fort authored frames remain
host-owned. Detached client camera never changes the host-selected point scale.

`MovementTargetTests.cs` covers selection priority, threshold/flags, disclosure
denial, settlement redirects, exact hover identity, frame timing, camera scaling,
ground and sea layouts, authored fort frames, copy independence and invalid input.
The wind oracle reconstructs the audited basis/quarter-turn operations separately.
`MovementTargetTransportTests.cs` exercises canonical/host-text roundtrips, cloned
state, every truncated payload prefix, invalid headers/IDs/offsets, bounded UTF-8,
disclosure/type/shape, newer cancellation/old-state rejection and freshness plus
catalog/dynamic readiness. Native message packing and rendering still require
an actual game run; compile success is not packet or visual acceptance.

## Verification

- cmp-c45 and cmp-b44 could not compile because the sandbox denied reading the
  user's NuGet.Config. These are environment failures, not executed assertions.
- Fresh cmp-c46 passed all 24 contract projects, including 237 movement-target
  assertions. Both cmp-b45 isolated builds passed with byte-identical installed
  module inventories and no product launch. Evidence from successful and failed
  runs is retained locally under `artifacts/verification/<run-id>`.
- cmp-b45 client SHA-256:
  `AB994A14EC3DDB141FD66C18A1D51872F5AE152D29AF4B5D29A25B9181B63A8B`;
  dedicated SHA-256:
  `445D02E5FE905AF2EFEE9AE14C3348D9110AD424A5901A69A4A68C139B70D218`.
- No new deployment, product launch, native acceptance or lifecycle verification.

Integration verification: cmp-c47 passed all 24 projects; cmp-b46 failed only the
client compile on the missing ISpottable namespace import, then corrected to the
installed `TaleWorlds.CampaignSystem.Settlements` type. Intermediate cmp-b47 passed
both builds (77/49 warnings, zero errors, unchanged installed inventories).
cmp-c48 passed all 24 projects for the first integrated candidate. Final cmp-b48
passed both builds with unchanged installed inventories and no product launch;
cmp-c49 passed all 24 projects, including 237 layout and 408 transport assertions.
These final runs include the rendered-party offset correction. The exact source
subset is retained under artifacts/verification/cmp-b48/source. Final output hashes:

- Client: `539BD1A00E507E706CB3123C735736895417F8F1D7A88ACCD92C443D5158DD92`.
- Dedicated: `4047E450881829BED7AE3DE35E1CAA32AD2C07662F3EB2AF9F1386B4B0A8669D`.

Foundation selection/layout: Satisfied for the bounded managed rules. Host capture,
wire transport, renderer and lifecycle: source/build-verified, Partially Satisfied
pending native evidence. Native/regression acceptance: Not
Satisfied. Do not mark the Goal complete based on managed tests.

## Remaining implementation and acceptance

Native evidence update: [closed cmp-b48 run](../../artifacts/runtime/map-movement-target-r1-20260928/RESULT.md).
The host and same connected MP client completed normal field victory and automatic
map return after the native prisoner/loot screens. Post-return tooltip operation
was user-confirmed. Pursuit recipes alone do not prove party-ring placement.
The paired post-war screenshots reveal a separate settlement-nameplate relation
defect: replica Relation is hardcoded 0. Fix its shared map data/presentation path,
not field-battle completion. Adjacent battle regression remains mandatory.

1. Source/build/contract review is complete for cmp-b48/cmp-c49. Preserve host-only
   capture, current disclosure, bounded recipe validation and ordered explicit
   clears while completing native acceptance; passing tests are not full parity.
2. Verify native message capacity/packing for maximum recipe/ID values and actual
   receipt behavior while catalog synchronization or state replacement is active.
3. Verify party ring alignment and atlas phase during motion, frame readiness,
   target switches, pause and different camera scales. No managed test proves this.
4. Verify stale presentation and native ownership on dialogue/map hiding, session
   change, target cancellation/removal/disclosure loss and finalization; reacquire
   only fresh authoritative state after every battle return/rejoin.
5. Perform a coherent three-role native run with exact identities and recovery
   preimages: point, pursuit, village, town/castle, target changes/arrival, camera
   follow/detach, hover suppression, dialogue, map/battle/map and rejoin.

Field, village, siege and hideout battles share the map-return surface. No battle
scene or battle adapter was changed; their transition regression is still required
for the connected map adapter. Sea branches mirror installed behavior and
are not a separate marine expansion feature or proof of DLC compatibility.

## Documentation impact

Canonical implementation boundary: this document. README and CODE_MAP link here.
Hover documentation links the follow-up. Architecture/risk/runtime-flow summaries
now identify the connected source candidate without claiming native acceptance.
BUILD_TEST_DEBUG remains unchanged: deployment defaults and runner commands did
not change; all builds use the reviewed isolated CompileOnly runner.
