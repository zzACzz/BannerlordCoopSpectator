# Campaign Map Resume — 2026-09-26

## Deferred reconciliation ordering correction (2026-09-28)

Source investigation after map-body-r1 found a concrete stale-write ordering:
ApplyCompletedCatalogDelta -> ApplyMergedReplica queues a merged full projection
in MissionView. A following ApplyCompletedDynamic immediately updates its live
replica to the cleared InSettlement bit. ApplyPendingVisibleEntities on the next
frame then used the older queued projection and overwrote that dynamic update.
While the host is paused, an unchanged party need not receive another delta.
The same ordering can resurrect a newly hidden object or discard a new visible
object, so the correction must reconcile membership, not only copy party flags.
This is a source-proven defect consistent with the recorded native failure.
The corrected candidate now passes the bounded native reproduction below;
this does not establish every possible reconciliation ordering as correct.

Source candidate: CoopCampaignMapReplicaStateProjection.BuildVisible resolves
the current admitted catalog and dynamic dictionaries at deferred consumption
time. ApplyPendingVisibleEntities uses that result for body upserts, absent-entity
removal and both overlays. Queued payload contents no longer replace newer state.
Merge retains the existing visibility, identity and settlement-range disclosure
rules. No protocol change, new diagnostic, native read or new authority exists.
The dictionaries are mission-local and cleared on release. This affects only the
map view; physical field/village/siege/sally/hideout/naval renderers are unchanged.
Battle return-to-map and broader visibility transitions still require regression.
Repeated settlement entry and native reappearance passed the bounded run below.

PartyBodyTests now has 656 assertions, including queued-inside then exit,
queued-outside then entry, visibility loss, later admission, catalog removal,
latest catalog data, mismatched identity and missing-input rejection, plus the
view consumption wiring. Its new wiring assertion failed before the view fix and
passed after. A direct whole-project invocation then failed in an unrelated test
because COOPSPECTATOR_REPOSITORY_ROOT was absent; use the canonical runner.
cmp-c19 passed all 24 projects and cmp-b19 compiled both roles with installed
inventories unchanged. Evidence is copied to artifacts/verification/cmp-{b,c}19.
Client SHA-256: 304EDE51EB0EC4EF45DAC3B2355DD1FFC69E9673A01AA89B4FB34FE9801281BF;
dedicated: EFBEC32BDF186ED1784F8134E906B5146CEBF4BC9E54909148C1624C679D51B6.
Native map-body-r2-20260928 coherently loaded cmp-b19 in campaign, menu-launched
dedicated and exact-shortcut multiplayer; all three loaded identities were
verified. User-assisted Login and AC_COOP join opened the map automatically.
Two Avalyps entry/exit cycles passed: mounted body/nameplate hide inside and
reappear outside; the host-lord occupant badge appears/clears correspondingly.
The pinned party panel clears on entry, while an unrelated pinned settlement
panel survives the repeat cycle. Manual camera detach and explicit return passed.
Evidence: artifacts/runtime/map-body-r2-20260928/RESULT.md, eleven screenshots
and detached catalog/dynamic/host-state files at five checkpoints.

Startup limitation: the first campaign launch failed Steam API initialization
after fresh Steam startup and exited. Its evidence was retained; one retry only
after confirmed termination succeeded. Nonfatal intermission/distance-cache/audio
diagnostics remain in the successful run logs; this is not an error-free-log claim.
All 24 exact-owned identities are absent, seven configuration preimages match,
no new configs or product processes remain, and test saves are preserved.
Independent postflight confirms installed cmp-b19 hashes retained (module
preimages were not restored). Full native hover, complete map/dialogue parity,
broader visibility transitions and battle-return regression remain open.

Documentation impact: this canonical section and short README/build/code/flow/
invariant links. Architecture is unchanged (existing accepted state, same owner).

## Mobile body admission correction (2026-09-28)

The cmp-b16 native run's 07/08 screenshots showed a mounted main party inside
Avalyps only in the replica. Installed MobilePartyVisual.RefreshPartyIcon resets
the icon, then branches on CurrentSettlement: that branch may create a settlement
banner, but never calls AddMobileIconComponents (the body/mount builder). The
existing replica instead kept the logical MainParty anchor and used successful
camera projection as permission to render its body. PartyNameplateVM already
hid the label using InSettlement; its UpdatePosition return value describes
projection, not body admission. Do not equate these decisions.

Source correction: CoopCampaignMapPartyBodyPolicy.ShouldRenderBody admits valid
MainParty/MobileParty states only when the existing PartyNameplateFlags lack
InSettlement. MissionView applies it before visual allocation/update and the
same shouldRenderParty decision hides existing bodies, generic markers and the
main-party fallback marker. Logical entity, position, camera anchor and settlement
occupant badge are preserved. Exiting the settlement re-admits the existing body;
native resource failure follows the pre-existing retry path. No wire/schema bump,
new host state, campaign command, physical agent or diagnostic was added.

The hidden mobile body is also excluded from custom hover/pin selection. Upserts
immediately invalidate matching displayed/pinned information even while UI hit
testing blocks ordinary hover updates. InfoVM now remembers only its displayed
entity ID and clears it on Hide; unrelated settlement/party information is kept.

Adjacent scenario audit: this view is instantiated only by the map prototype
mode. Field, village, siege, sally, hideout and naval physical mission renderers
do not consume the policy. Their return-to-map and settlement-entry transitions
still require native regression. Body admission is intentionally NOT nameplate
admission: AttachedToArmy, AtSea and InRaft do not independently hide a body in
this correction. Native naval/tent/battle visual variants and settlement 3D
banners remain incomplete and are not claimed fixed by hiding the extra rider.

Validation: intermediate cmp-b17 built both roles with installed inventories
unchanged; cmp-c17 passed all 24 projects. Focus-aware information invalidation
was added afterwards; final cmp-b18 built both roles and cmp-c18 passed all 24
projects (646 body assertions). Build-time installed inventories were unchanged.
Evidence is retained under artifacts/verification/{cmp-b17,cmp-c17,cmp-b18,cmp-c18}.
The fresh map-body-r1-20260928 native run has now installed cmp-b18 and verified
campaign, menu-owned dedicated and shortcut-launched MP loaded MVID/disk hashes.
Native entry hiding/pinned-panel invalidation passed, as did camera detach/return.
Exit reappearance FAILED: host screenshot 08 shows the mounted main party outside
Avalyps, but client screenshot 09 still lacks its body and nameplate. The captured
host dynamic file has main-party PartyNameplateFlags=0 and IsVisible=True; the
catalog retains MainParty and mounted visual kind. The settlement occupant badge
clears on both sides. This narrows but does not prove the cause: investigate
dynamic delivery, merged client state and re-admission before changing rendering.
Evidence: artifacts/runtime/map-body-r1-20260928/RESULT.md. All 22 exact-owned
identities exited; all seven config preimages match, no new configs/products,
test saves preserved, and all five installed module preimages (cmp-b16) restored.
Source remains cmp-b18; do not confuse source/contracts with installed acceptance.
New PartyBodyTests covers
all 64 flag combinations for both party kinds, nonparty/invalid rejection,
repeated entry/exit without anchor loss, and source-bound renderer/fallback/hover
invalidation wiring. These contracts do not prove native disappearance/reappearance.

Documentation impact: this canonical correction, README/build status, code map,
runtime flow and invariant links. Architecture is unchanged: the existing host
presentation bit governs client-owned objects, with no new authority or transport.

## Settlement occupant badge candidate (2026-09-28)

The host publisher now captures the admitted native settlement grid as detached
kind/bandit/banner descriptors. IsInRange gates enumeration; garrison, militia,
disguise and army rules follow the installed PartyMarkersVM audit below. The
list is read in its existing rendered order, preserving duplicate badges and
the native item's Visual.Id banner without faction recoloring. It is NOT rebuilt
or sorted from settlement.Parties: native MBBindingList.Sort skips already ordered
lists, so recomputation can change equal-kind ordering. The publisher reads the
existing SettlementNameplatesVM through the loaded Gauntlet view's _dataSource;
only reflection metadata is cached, not host view/VM instances. Missing or changed
private-field layout fails closed. No refresh, population or registration runs.
The client binds
these items to the existing stock settlement grid; it creates no campaign parties,
rosters, names, commands, physical agents or native host event subscriptions.

`SettlementPartyBadges` is a bounded catalog/full-visible field. Protocol 26,
bridge schema 22 and binary catalog schema 7 require coherent restarts. Full
host bridge, catalog bridge, binary codec, clones, catalog comparison and shared
projection preserve it. Range loss clears the projection even with a retained
catalog; hidden entities cannot project. Empty-list departures trigger a delta.
The UI replaces the list only on content/range change and finalizes old items.
Malformed/oversized captures fail closed to an empty list, never guessed heraldry.
Bounds: 128 badges, 512 characters per banner, 66,176 encoded characters total.
This is a resource ceiling, not acceptance of parity for larger/modded lists.
Banner records require complete ten-integer native layers; empty lord banners
retain native null behavior rather than an invented coat of arms.

Any nonempty badge catalog delta is routed to the existing reliable bounded
binary/chunk transfer before sending a direct-message header. Native single
entity messages explicitly reject nonempty badges rather than silently dropping
them or overflowing a packet. Empty clears remain supported by the direct path.
The old monolithic visible-entity broadcaster has no call sites. Binary catalog
allocation is checked after each entity as well as at completion against the
existing 16-MiB bound. No new diagnostic code or authority channel is introduced.

Validation: cmp-b12/cmp-c13 failed before compilation on sandbox access to the
user NuGet.Config, not source assertions. Permission-correct cmp-b13 built both
roles with installed inventories unchanged; cmp-c14 passed all 24 projects.
Tests cover all 128 admission combinations, one-hot native item kinds, duplicate
badges/order/null heraldry, malformed/oversized data, full/catalog bridge and
binary roundtrips, native chunk bounds, clones, range/hide/re-entry projection,
departures, nonsettlement rejection and source-bound direct-send/UI wiring.
The final incremental allocation guard and stronger large-roundtrip/source check
passed both cmp-b14 role builds and all 24 cmp-c15 projects. Build-time installed
inventories were unchanged and no product process launched. Evidence from all
attempts is retained under artifacts/verification/<run-id>. Subsequent actual-UI
capture exposed a missing client Core.ViewModelCollection reference in cmp-b15
(CS0012). The explicit client-only Private=false reference fixes it. Final cmp-b16
built both roles and cmp-c16 passed all 24 projects, including source contracts
for actual ordered UI capture/no native refresh. Final client SHA256
96DDB0D4175103328F41EA3787B70A0649635EF8BFD907502ACC3C803B0DB942;
dedicated 2E57F620A766301DB440860E24271BD7C090AC68DAF2FCC5DDE4492BAAFD27B5.
Native run map-badges-r1-20260928 coherently loaded cmp-b16 in campaign, dedicated
and multiplayer (role MVID plus installed SHA verification). Campaign menu started
the server; the exact desktop shortcut started MP, Safe Mode No selected. First
observed MP main screen was already Home; no agent Login action is claimed.
Automatic map readiness was 382 ms, initial catalog 397 entities / 105,971 logical
bytes / 12,355 wire bytes / 49 chunks. No console command was required.

Bounded badge acceptance: initial Avalyps ordinary glyph matches both frames and
the captured `2,0,` descriptor. After villagers departed, both views and the later
catalog cleared the badge. Entering Avalyps with the main party produced the same
brown/gold lord glyph in both views and a kind-0 native banner descriptor. These
observations identify the tested lower glyph as an occupant badge, not a quest
marker. Host movement and manual-camera detach/explicit return also passed.
Evidence 01-10 and initial/departed/entered bridge copies are in the run root.
Copies are adjacent reads, not an atomic cross-file snapshot. No mixed-kind/order,
128-badge, caravan/bandit, remote-client, dialogue or battle re-entry acceptance.

New observed parity gap: the main party's mounted scene visual remains visible
inside Avalyps in the replica while absent in the host settlement view (07/08).
Settlement-menu/HUD parity is also absent. Native tooltips remain a separate gap.
The existing guarded IntermissionVmCrashGuardPatch ArgumentNullException appeared
at join; there was no visible crash, but this is not a zero-error run.

Cleanup and independent postflight passed: all 22 recorded identities absent,
all seven config preimages match, no new config files/product processes, test saves
preserved. Test-owned Steam ended (baseline empty). Only inspected safely_exited
and dedicated-map config changes were restored; five module preimages retained.
Installed cmp-b16 remains and all deployment hashes match. Full goal remains open.

Documentation impact: architecture, runtime flow, code map, invariants and current
build/navigation entries updated for this new presentation field and transport
routing. Native research and previous clean run remain below; no test evidence
from the banner-color run is reused as badge acceptance.

## Outstanding hover fidelity

Outstanding hover fidelity: UpdateReplicaInformation currently uses the custom
CoopCampaignMapReplicaInfoVM and CoopCampaignMapReplicaInfo.xml panel, not native
campaign tooltips. Its limited name/type/owner/faction/party/army fields and layout
are not 1:1 acceptance. Native appearance, full host-disclosed content, and dynamic
updates remain part of the goal. Nameplate parity does not prove tooltip parity.

### Installed native tooltip audit (2026-09-28; live integration not implemented)

Research output: `artifacts/native/map-tooltips-20260928`. Native screenshot:
`artifacts/runtime/map-badges-r1-20260928/evidence/06-host-native-settlement-tooltip.png`.
Installed assemblies inspected with ilspycmd, SHA256:

- CampaignSystem.ViewModelCollection: `5D817A46456D07B5E4382A870795FD9F8D6F11B4707A91D8A8B5D5CD50E762A5`.
- Core.ViewModelCollection: `5A48A04FF42B1B448FB8DEF314608A402FD4F872174C3A4CD858D5E866F44B23`.
- SandBox.View: `C7360E71DA06799A0CB6C00C4A0CFAB5877621197BA25101433AC28654D169E2`.

Confirmed routing: MobilePartyVisual.OnHover first selects MapEvent, otherwise
requires a visible mobile party. An attached/leader army member selects its
leader's SiegeEvent or Army; other parties select their SiegeEvent or MobileParty.
Army/MobileParty arguments are `[entity, false, true]` (not menu context; enforce
map visibility). SettlementVisual.OnHover selects MapEvent first, then a visible
settlement's SiegeEvent or Settlement. Do NOT call OnHover for capture: it changes
the actual host tooltip. Do NOT force IsInspected or use menu arguments to obtain
more rows. Independent client hover must still use host disclosure, not client
camera proximity or arbitrary entity IDs as authority.

TooltipRefresherCollection implements native row construction:

- RefreshSettlementTooltip gates economic/defender/prisoner details using
  CampaignUIHelper.IsSettlementInformationHidden. Ordinary and extended layouts
  differ; hidden information stays absent or uses the native missing indicator.
- RefreshMobilePartyTooltip uses IsInspected/IsInfoHidden, native behavior/speed,
  approximate counts, troop grouping and inspected extended roster/health rows.
- RefreshArmyTooltip aggregates the leader and attached parties with distinct
  inspected/count/prisoner rules. It must not be replaced by a single-party card.
- RefreshMapEventTooltip / RefreshSiegeEventTooltip use mode War and side-specific
  property flags. Adjacent field, village raid, siege and naval event branches
  must keep their native routing, not fall through to a generic party tooltip.
- Ship details have their own inspected/menu/map-visibility gates. Capture rendered
  strings, not raw ship/character objects or roster delegates.

PropertyBasedTooltipVM retains an ordered TooltipPropertyList and Mode; rows
carry DefinitionLabel, ValueLabel, TextHeight, TextColor and PropertyModifier.
The stock Native/GUI/Prefabs/Information/PropertyBasedTooltip.xml already binds
those fields, including titles, separators, rich text, war columns and colors.
TooltipProperty.SerializeTo omits PropertyModifier: it is NOT a complete wire
contract for visual fidelity. Its copy constructor also omits
OnlyShowWhenNotExtended; snapshot already-selected rows instead of relying on it.
Native PropertyBasedTooltipVM refreshes row delegates at a two-second interval;
changing IsExtended clears/rebuilds rows only while active. TooltipBaseVM invokes
the registered provider by type and finalization drops its arguments. A detached
VM can be researched for capture, but provider registration, key-text getters,
GameTexts shared variables and event/roster lifetime still require bounded native
verification; no unsafe runtime probe or production diagnostic was added.

Implementation direction (not shipped): preserve the native row schema and use
the stock renderer with passive client data. Prefer a bounded read-only per-peer
hover-interest request, host-side disclosure/routing validation and a detached
row response, rather than regenerating both tooltip modes for every known object
each frame. Bind results to map session, target and request generation; invalidate
on target/hide/dialogue/mission change and refresh active targets. Reuse bounded
chunk transport principles, but catalog range requests are not a semantic hover
channel. The current controller has no such semantic request path. Never accept
client-supplied rows, provider types, file paths or campaign commands. Pending
acceptance: native normal/Alt comparison for settlement/party/army/event, hidden
and approximate information, dynamic updates, peer isolation and lifecycle cleanup.

Historical audit's separate mounted-visual gap: publisher CaptureVisibleEntities always
adds MainParty with isVisible=true, even in a settlement, while other parties
pass hasCurrentSettlement admission. This is a source-level explanation for the
07/08 mismatch, not a completed fix. Do not remove the logical main-party anchor
blindly: camera and overlay behavior need separate visual-admission validation.
The newer body-admission and reconciliation corrections at the top of this
document supersede that observation and include bounded native verification.

Documentation impact of this audit: canonical resume only; no production schema,
runtime implementation or build changed. Architecture/code-map entries remain
unchanged until the semantic tooltip channel is implemented.

### Tooltip provider isolation and detached payload foundation (2026-09-28)

Further installed-assembly audit resolves the registration and shared-text risk:

- SandBoxViewSubModule.RegisterTooltipTypes registers Settlement, MobileParty,
  Army, MapEvent and SiegeEvent to PropertyBasedTooltipVM and their corresponding
  TooltipRefresherCollection methods. Capture must verify the actual registered
  provider instead of assuming another mod has not replaced it.
- GauntletInformationView.Initialize installs MapClick/FollowModifier key getters
  and ExtendModifier. They read Game.Current.GameTextManager / hotkey text;
  keyboard extension uses `str_game_key_text/anyalt`, gamepad uses follow modifier.
  Do not register duplicate keys, invoke the global tooltip, or automate input.
- GameTextManager.TryGetText returns a CopyTextObject, but this is NOT a general
  deep-isolation guarantee: TextObject copies a nonempty attributes dictionary
  shallowly, shares nested values, and preserves a nonnull empty dictionary by
  reference. Audit actual provider writes instead of assuming template isolation.
  GameTexts.SetVariable forwards to MBTextManager.SetTextVariable.
  Its process-global TextContext owns a case-insensitive `_variables` dictionary;
  setters replace dictionary entries, and ClearAll removes ALL entries. No public
  scoped save/restore API was found. An independent provider call is therefore
  not proven side-effect-free. Do not use ClearAll as cleanup. Before host capture,
  design/test a main-thread bounded exact restoration strategy (including failure),
  or use a read-only existing-VM source with its more limited hover semantics.
- Provider row delegates capture live party/army/roster objects. Flatten strings
  synchronously and finalize the temporary VM in finally; never retain delegates
  or call periodic refresh after leaving the valid campaign-map context. Constructor
  immediately refreshes the default mode; setting IsExtended refreshes again only
  while active, so both executions need the same capture safety scope.

Additional decompilations are in artifacts/native/map-tooltips-20260928. SHA-256:
Localization D75A5C89C26A09E587FAB55C182B2962BB36708B4766A922BDF1F46A28212628;
Core 3E5CECE8CA42FAF3FDDE27632519ED2A6F86D1EC6D1A742867729F00001689BF;
Native GauntletUI 75B070E0B1347C3F6C7E4526EDC5370BBA18B4D4B4747C115669E23477AD44E2.

Source-only foundation: Infrastructure/CoopCampaignMapTooltipPayload.cs defines
detached ordered rows with both strings, native TextHeight/PropertyModifier and
four float color channels; Mode, route kind, extended state, session, target,
request generation and response sequence are preserved. Strict UTF-8, canonical
booleans, bounded prefixes/counts and total pre-allocation measurement reject
malformed/oversized data rather than truncating a native card. Limits are 512 rows,
4096 UTF-16 characters per label and 1 MiB total; oversize means unavailable, not
permission to expose an alternate summary. MatchesInterest is correlation only,
NOT peer authorization, current visibility, freshness or mission-lifetime proof.

This helper is compiled into both roles but has NO runtime caller. No active
protocol/bridge/schema version changed; installed cmp-b19 remains. Host capture,
per-peer request admission/rate limits, relay/chunking, invalidation and passive
stock rendering are still to be implemented. Empty rows are rejected; close/deny
requires a separately defined control response. Initial cmp-b20 compiled both
roles with installed inventories unchanged. cmp-c20 passed 23/24 projects and
exposed uncaught InvalidDataException on a malformed prefix; the decoder now
rejects this explicitly. Corrected cmp-b21 built both roles without installed
changes; cmp-c21 passed all 24 projects, including 907 tooltip payload assertions.
Evidence is retained under artifacts/verification/cmp-{b,c}{20,21}. No product
process was launched for these builds/tests and no tooltip native pass is claimed.
Documentation impact: this section, code-map and build/index/invariant links;
architecture/runtime-flow ownership remains unchanged until actual wiring.

### Host-native tooltip capture candidate (2026-09-28; unwired)

Campaign/CoopCampaignMapTooltipCapture.cs now provides a host-thread-bound native
row capture adapter. Its initial version was unwired; the new source host-relay
tick now reaches the publisher entry for an admitted local request, as described
below. There is still no native MP producer or native acceptance. It requires
a campaign MapState, no physical mission/dialogue, exact raw
target identity and current host disclosure before native reads. Its caller must
ALSO resolve that target from the currently admitted catalog; a live visible
PartyBase alone is not a peer's request authorization.

Routing preserves native precedence: MapEvent, then settlement/army-leader/party
SiegeEvent, then Settlement/Army/MobileParty. Army membership must actually include
the target; map arguments remain [entity, false, true]. Missing event leaders or
removing sieges fail closed. Physical field/village/siege/sally/hideout/naval
renderers are not modified, but map event/raid/naval tooltip branches and the
map-to-battle-to-map invalidation boundary remain future native gates.

The adapter compares the registered tooltip type, movie, delegate Method and
Target against the installed native provider. Unknown overrides fail closed.
An unregistered private marker type constructs an inactive PropertyBasedTooltipVM;
IsExtended is set BEFORE the verified provider is called once. This avoids the
default-then-extended double invocation of an ordinary native VM constructor.
Missing native key labels fail closed. Only already-formatted strings and native
row layout/color fields are copied; no row delegates escape. Target/context and
payload validity are rechecked before publication. VM finalization is in finally.
Cleanup failure suppresses output and disables subsequent captures in that process.

Infrastructure/CoopCampaignMapTooltipTextTransaction.cs snapshots up to 8192 exact
global text-variable entries while retaining dictionary/comparer identity. It
admits only the owner thread and empty native evaluated/raw function stacks.
On exit it restores original entries/references and clears only the stacks proved
empty at entry. This specifically covers native TextProcessingContext.CallFunction,
which pushes then pops WITHOUT finally and can strand parameters on exceptions.
It does NOT deep-restore mutations inside referenced TextObjects, game-text template
attributes or language-processor internals; do not describe it as complete native
state isolation. Its intended audited writes replace dictionary entries only.

Further inspection found LanguageSpecificTextProcessor.Process temporarily replaces
thread-static _lowerMarkers, also without finally. The language-state isolation
follow-up below now covers the nine installed processors. Template CopyTextObject
remains shallow; the follow-up rejects the audited shared-empty-dictionary case.
Normal/extended native visual comparisons and actual campaign restoration checks
remain required. No global processor swapping, Harmony interception, live
diagnostic or host tooltip operation was added.

Validation: intermediate cmp-b22 both isolated builds passed. cmp-c22 failed its
new test's case-sensitive key assertion because Dictionary.KeyCollection.Contains
uses the dictionary's case-insensitive comparer; the assertion now explicitly
compares enumerated keys ordinally. Final cmp-b23 both isolated builds passed;
cmp-c23 passed all 24 projects, including 520 transaction assertions and the existing
907 payload assertions. Evidence: artifacts/verification/cmp-{b,c}{22,23}.
Installed cmp-b19 remains unchanged; no product process was launched.
Documentation impact: canonical candidate, code-map and index/build/invariant links.
Architecture and runtime flow are unchanged because no live endpoint calls this
adapter. Remaining work includes native isolation gates, per-peer request admission,
host catalog lookup, bounded relay, close/deny semantics and passive stock renderer.

### Tooltip language-state isolation (2026-09-28; source and managed probe)

`CoopCampaignMapTooltipLanguageState` identifies the installed active processor
by assembly, exact type/base and thread-static field-name set. The pure
`CoopCampaignMapTooltipThreadStateScope` clones only audited flat lists/dictionaries
of strings/value tuples and retains exact original references/scalars. It supports
at most eight fields and 4096 entries per collection, preserves comparers, preflights
all clones before writes, publishes its cleanup scope before reflection mutation,
and attempts every restoration even if one fails. Owner-thread restrictions apply.
Unknown layouts/types fail closed. Capture finally restores VM, grammar state and
text variables/stacks; cleanup failure suppresses publication and future captures.

Audited processors: Default/English (only base lower markers), French/Italian
(word groups/gender and French plural), German/Polish/Russian (word groups, gender,
no-tag groups, links and origin flag), Spanish (gender), Turkish (links). The base
lower-marker list is covered for every processor. Read-only grammar lookup tables
are not cloned; arbitrary mod overrides/nested object graphs are not covered.

CopyTextObject shares a nonnull EMPTY attribute dictionary, whereas nonempty
dictionaries are copied before direct assignment. The adapter refuses that shape
or a missing template for 17 audited writable IDs from the five map providers and
their troop/ship, party-size, duration and army-behavior helpers. The helper audit
includes army gathering/travel/raid/siege/defense branches; physical battle code
is unchanged. This gate is not a claim that arbitrary replacement models are safe.

cmp-b24 and cmp-b25 both role builds passed with installed inventories unchanged;
cmp-c24 and cmp-c25 passed all 24 projects, including 613 grammar-scope, 520 text
transaction and 907 payload assertions. Evidence: artifacts/verification/cmp-{b,c}{24,25}.
The isolated `Test-LanguageStateIsolation.ps1` loaded the installed localization
assembly and candidate, exercised actual ClearTemporaryData for all nine native
processors plus interrupted lower-marker replacement, and confirmed original
references/counts/scalars after restoration. Both language-isolation-cmp-b24.json
and language-isolation-cmp-b25.json are under artifacts/native/map-tooltips-20260928.
No game process/UI was involved; this is managed installed-library evidence, NOT
a native tooltip or visual acceptance run. Installed cmp-b19 remains unchanged.

### Tooltip request lifetime boundary (2026-09-28; candidate)

Network-facing CoopCampaignMapPrototypeState does not carry the campaign session;
the server separately retains _lastBridgeSessionId. A campaign session persists
across map -> battle -> map, so session/entity/generation alone cannot reject a
response from a previous map or connection when request counters restart. Future
transport must grant a fresh per-peer/map/host-session lease and bind both rows
and denial to it. The trusted grant adapter must reject stale mission/connection
messages; a lease token alone is not authentication. Reused peer indices must be
checked against the exact current NetworkCommunicator reference before unicast.

`CoopCampaignMapTooltipInterest` is an UNWIRED client policy and immutable semantic
request. Only target, extended state, session, lease and generation cross its
request boundary. It issues at most four requests per second, including after hover
clear, and assigns a new generation to each refresh. Same-hover refresh may retain
an unexpired card; target/extension change clears immediately, even when throttled.
One terminal row/deny response per request is admitted. Old generation, target,
extension, lease, session, duplicate/older sequence and expired responses fail.
Maximum age is two seconds from LOCAL REQUEST time, not arrival, so transit cannot
renew freshness. Payload storage/readback are detached copies. Clock reversal
revokes the grant; arithmetic avoids deadline-addition overflow.

The future UI adapter must check current disclosure on every read/tick, clear on
hover exit/dialogue/unavailable map, and reset on mission/connection/session loss.
The caller must authenticate each response and validate fresh grants against the
current mission/connection. The follow-up below adds an unwired host admitted-catalog
entry and semantic envelope codec; request relay, native chunk/network registration
and stock renderer are NOT implemented. Existing protocol/
bridge/catalog remain 26/22/7; no runtime caller. cmp-b26 both builds and cmp-c26
all 24 projects passed (3612 client-interest assertions). Final cmp-b27 both builds
and cmp-c27 all 24 projects passed, additionally including 2505 server-gate assertions.
Evidence: artifacts/verification/cmp-{b,c}{26,27}. Installed inventories were unchanged;
no product process launched. Existing compiler warnings remain (77 client/49 server).

`CoopCampaignMapTooltipPeerGate` now supplies an UNWIRED pure server-side policy:
fresh random lease per instance, exact peer object reference, immutable host
capture tickets, monotonic request generations/response sequences, four captures
per second maximum and two-second pending age. Every valid newer intent cancels
the previous capture even when hidden/throttled; replayed/foreign envelopes cannot.
Completion rechecks the caller's current host-admission result and the exact pending
ticket plus all payload correlation. Malformed completion is terminal. Revocation
and clock reversal are permanent for that gate instance. Client/server clocks are
independent local monotonic values, never compared across machines. Peer index,
name and a lease by themselves are not authentication; the future adapter must
supply the actual current NetworkCommunicator and fresh host facts. No live network
handler, file endpoint, native formatter invocation or packet format was added.

Next integration requirements: trusted grant delivery must be bound to the current
mission/connection (never blindly apply delayed grants), current host catalog lookup
must precede capture and publication, bounded chunk/relay envelopes must carry the
lease and full request/ticket correlation, and unavailable/denied/closed state must
clear presentation. The renderer should build/cache its detached VM only on a new
accepted card; avoid repeated full payload decode/clone every frame. The new
HasCurrentDisplay method supplies a cheap expiry/disclosure tick. Both pure policies remain single-
owner-thread components; the network/host adapter must enforce that scheduling.

Documentation impact: canonical sections above, code map, invariant, build and
index summaries. Architecture/runtime flow are unchanged because there is still
no live endpoint or ownership change. Actual campaign tooltip isolation, normal/
extended parity, unknown/hidden data, multiple peers and map/battle/map are open
integration/regression gates; no complete hover or overall Goal acceptance.

### Tooltip semantic envelopes and host lookup (2026-09-28; unwired)

`CoopCampaignMapTooltipWireCodec` v1 defines five bounded semantic records: Hello,
Grant, Request, Rows and Denied. Hello/Request are client-origin only; Grant/Rows/
Denied are server-origin only. The future native handlers MUST enforce those
directions and authenticate the current peer/server; decoding is not authorization.
Control records are at most 512 bytes, rows at most payload limit + 26 envelope
bytes. Strict UTF-8, bounded ushort text lengths, exact GUID blocks, canonical
booleans, known version/kind, exact row length and no trailing bytes are enforced.
Records reject irrelevant union fields rather than silently dropping them. No
native packet/message type, file format in active use or protocol version changed.

`CoopCampaignMapTooltipHandshake` regenerates a random client challenge on reset.
Only the matching challenge can install the first grant; later duplicates must
have the same session/lease. Reset both handshake AND interest on map/connection/
host-session loss. The challenge blocks delayed grants from previous client
lifetimes; it is not a password, authenticated peer proof or replacement for the
server's exact-reference admission. A grant change needs a new handshake, not a
silent replacement. cmp-b28 both builds/cmp-c28 all 24 projects passed, including
1974 wire/handshake assertions and the existing request-policy checks. No install.

The publisher now exposes `TryCapturePublishedTooltip` for a FUTURE trusted local
relay, not directly for client/network calls. It checks owner thread, exact current
campaign reference, successful publication session, monotonic publication age below
two seconds and live map/no-mission/no-dialogue context. A publication attempt
first invalidates the old proof; only all successful catalog/dynamic/state writes
restore it, preventing partially updated stores from inheriting an older stamp.
The pure `CoopCampaignMapTooltipTargetAdmission` requires exact session/entity IDs,
catalog/dynamic agreement, visibility, supported kind/prefix and body admission.
Hidden numeric detail is NOT grounds to omit the card: the native provider must
retain its ordinary unknown-number presentation. Current native target resolution
runs only after those checks, rejects duplicate raw IDs, and the capture adapter
rechecks real host visibility before/after formatting. No IsInspected mutation.

The local host relay below now calls this entry, but no native MP handler submits
requests yet. Its native formatting remains potentially
runtime-sensitive despite scoped restoration; a bounded real campaign probe is
required before claiming isolation/fidelity. Unknown native formatter/model
overrides, key text readiness and all visual branches remain acceptance gates.
Physical field/village/siege/sally/hideout/naval mission code is unchanged; map
event/raid/siege tooltip routes and map/battle/map re-entry still require native
regression. cmp-b29 both builds and cmp-c29 all 24 projects passed, including 549
target-admission assertions (kind/body/visibility matrix, exact IDs, hidden numeric
detail, missing proof and source-order guards). These source checks do not execute
native campaign collections/formatters. Evidence: artifacts/verification/cmp-{b,c}{28,29}.
Installed inventories unchanged, no product process launched; cmp-b19 remains.

The owned server-to-host relay is now in source below. Next: bounded round-robin
workload across peers in the native network adapter, lease-bound
chunk assembly/cancellation and passive stock tooltip renderer. Do not couple
tooltip availability solely to the default-off automatic-mission feature: the
original manual map-command flow must remain supported. The launcher ownership
hook is moddedOfficialStartInfo near the existing MissionHost Prepare/Attach;
its session is not the publisher's map SessionId. No local directory discovery or
client-chosen paths/providers/commands are acceptable.

Documentation impact: canonical section, index, code map, invariants and build
summary. Architecture/runtime-flow changes for the new host relay are recorded below.

### Launcher-owned tooltip relay (2026-09-28; source candidate)

`CoopCampaignMapTooltipRelay` defines a separate inherited run GUID and fixed
binding.bin/requests.bin/responses.bin below Temp/CoopSpectator/TooltipSessions.
Only validated GUIDs select a run directory; client messages contain no paths,
process identities, provider names or commands. The binding pins exact host and
dedicated PID/start-UTC/executable identities. This is same-user local process
coordination, not protection from a malicious process already controlling that
user's files. Native MP peer authentication/disclosure remains separate.

Strict binary frames admit at most two distinct peer leases per batch, bounded
UTF-8 executable paths, bounded semantic request/result blocks, exact schema/kind,
positive round/sequence/timestamps and complete consumption. Responses must echo
request identity and capture sequence; data/denial cannot be substituted across
requests. Atomic replacement and bounded reads reuse existing low-level channel
primitives, not automatic mission ownership/state. No directory enumeration or
in-place write fallback. Capture/transit time does not renew the two-second age.

`CoopCampaignMapTooltipHost.Prepare/Attach` is wired next to the modded official
launcher hooks, after exact child selection. Its feature gate is CampaignMapPrototype,
NOT AutomaticCampaignMissions; manual map-command startup remains eligible. The
campaign application tick polls after ordinary map publication, admits exact run/
process/freshness/monotonic round, verifies the owned dedicated process alive,
and invokes at most two published-target captures per 100ms pass. It processes
an admitted round once; failure disables this channel without stopping the game.
Campaign identity loss/unload resets it, including an unattached failed launch.
An independent one-second liveness check stops idle file polling after the owned
server exits; each admitted batch also rechecks liveness immediately. Old files are left in their unique run
directory rather than deleting unknown state; runtime test cleanup must track these
three newly owned files. Normal no-channel ticks do no IO/native formatting.

`CoopCampaignMapTooltipRelayClient` is the dedicated-side endpoint, not yet called
by the mission network controller. It opens ONLY the inherited run binding and
checks its own exact process plus the live host; no discovery/adoption of servers.
It detaches binding preimages, permits one outstanding two-ticket batch, uses the
monotonic timestamp as round ID across mission adapter recreation, and checks
process liveness, freshness, exact round/request/sequence/order before returning
a response once. Timeout clears the batch; clock/process loss revokes the endpoint.
The future consumer still rechecks current peer reference/ticket/host disclosure
before unicast. Network scheduling, chunk assembly and native renderer remain open.

cmp-b30 both isolated builds passed with installed inventories unchanged. The map
suite passed 908 relay assertions, including synthetic file request/response,
timeout, identity/replay/shape rejection and exact temporary-file cleanup. Full
cmp-c30 aggregate passed all 24 projects. Final cmp-b31 both builds and cmp-c31 all
24 projects passed; the idle-liveness/source cleanup follow-up brings relay checks
to 910. Evidence: artifacts/verification/cmp-{b,c}{30,31}. No game/native
formatter/process was launched for these checks; installed cmp-b19 is unchanged.
Source wiring is NOT native tooltip or local-process runtime acceptance. New native
capture is runtime-sensitive: actual restoration, latency, normal/extended visuals,
late join, multiple active hovers and adjacent map/battle/map gates remain open.

Documentation impact: this canonical section, index/code/build summaries, plus
architecture, runtime flow and invariants for the new launcher/process ownership.

The native-network candidate below now handles lease revocation when the host map
session changes while the MP mission stays alive, not only removal/disconnect.
Its sixth semantic record is lease-correlated Revoked, followed by a new challenge.
Native acceptance remains required. Preserve request/host age through chunk
transmission; never renew the card on completed assembly.

### Tooltip native-network candidate (2026-09-28; source only)

Protocol is now **27**, host bridge/catalog remain **22/7**. Three mod-native
message types carry client control, server control and server-only bounded chunks.
Direction is enforced during native packet read as well as semantic decode; Rows
are forbidden in control packets. Hello/Grant/Request/Denied retain semantic v1,
with newly admitted lease-only Revoked. Installed cmp-b19 still uses protocol 26;
do not mix its roles with this candidate. Coherent build/deploy/loaded identities
remain necessary before any live comparison.

The partial `CoopCampaignMapPrototypeNetworkController.Tooltips.cs` wires native
handlers, mission tick/initialize/finalize and exact-reference disconnect cleanup.
Client freshness derives from newly admitted map-state messages (two seconds),
not arbitrary native scene presence. It retries Hello at one-second intervals,
resets challenge on loss of availability even before Grant arrives, and accepts
only the current challenge. Revoked affects only the currently granted lease.
Changing the desired entity/extension clears the old card/assembly. Pending
requests get their whole two-second response window rather than being superseded
every 250ms; new refreshes start only after terminal response/expiry and the rate
gate. Disclosure/expiry are checked without decoding rows on each frame.

Server grants bind exact current NetworkCommunicator references and current host
map session. Host freshness/session loss revokes grants, pending captures, relay
batches and sends. Hello is bounded to once per 500ms per peer; current session,
catalog/dynamic/body visibility and dialogue exclusion precede host capture.
Newer intent cancels old sends even if throttled; stale requests cannot replace
the gate's pending ticket. A late disconnect for an old peer reference cannot
remove a newly connected peer sharing that index. Local tooltip transport faults
disable the subsystem with one failure log; they must not penalize otherwise
valid player requests or stop the mission.

The server relay endpoint is now called from the mission controller. Latest
per-peer tickets are selected round-robin, at most two per local batch, one batch
outstanding, polled at 100ms. Return admission rechecks the exact peer-state object,
ticket, current host disclosure and gate sequence/age before preparing a response.
Completed rows are compressed with the existing catalog codec and sent with an
aggregate budget of 16 chunks per tick, at most two per peer per tick, rotating
the starting peer. No broadcast tooltip rows. Send expiry remains anchored to the
server's original request admission, and client expiry to its own request time.
Multiple-active-hover throughput/fairness and native latency still require runtime
measurement; contract counts are not performance evidence.

`CoopCampaignMapTooltipChunks` bounds each packet to 512 bytes, each data part to
256, and wire/inflated payload to the semantic envelope maximum. Its assembler
checks current lease/generation/disclosure/age BEFORE allocation/decompression.
Foreign stale chunks leave a current valid assembly intact; changed metadata,
conflicting duplicates, hash/size mismatches and expired requests never publish.
Final semantic decode/gate admission is required; chunk integrity is not authority.
The shared catalog accumulator now stops decompression at its declared logical
length instead of the larger global ceiling. Valid catalog/conversation transfers
are unchanged; malformed over-expansion fails earlier. Full shared contracts ran.

The UI candidate below now supplies the hover producer and passive stock renderer.
Native relay/capture/UI acceptance is still open. No full Goal acceptance claimed.

cmp-b32 both isolated builds and cmp-c32 all 24 projects passed, including 345
chunk/network assertions (reordered/duplicate/foreign/stale data, byte bounds,
round-trip rows, revocation, over-expansion and source-handler/authority hooks).
Final non-penalizing local-fault handling passed cmp-b33 both builds and cmp-c33
all 24 projects.
No game process or installed mutation. Documentation impact: canonical section,
index/build/code, architecture/runtime flow and invariant summaries.

### Tooltip stock-presenter candidate (2026-09-28; not native-verified)

`UI/CoopCampaignMapTooltipPresenter.cs` registers a private marker type with the
installed PropertyBasedTooltipVM and PropertyBasedTooltip movie. Its callback
copies only detached rows, float colors, text height, modifier flags and mode.
It never registers a campaign entity provider or creates Campaign.Current in MP.
Same-hover updates replace rows in the existing VM without reloading the movie.
Normal/extended input mirrors the installed global view's keys 56/184/248 and
0.18-second gamepad threshold. A mode mismatch clears rather than displaying the
other mode's rows. No client-generated campaign commands or tooltip calculations.

MissionView supplies actual hover, not the selected/pinned entity. Hover exit,
foreign UI focus/hit, active dialogue, unavailable scene and finalization clear
the card/interest. Native hover replaces the old unpinned summary; explicit pinned
summary and camera-return controls remain. Existing world-projection/radius picking
is unchanged and is NOT proven equal to native campaign hit testing. Global show/
hide notifications track exact ownership: cleanup must not hide another menu's
tooltip, and active foreign cards cannot be replaced by a host response. Disposal
unsubscribes and unregisters only the exact owned mapping; replacement missions
dispose the previous owner first. Exceptions disable only this passive presenter.

Scope/risk: map-prototype client UI only; no physical battle UI registration.
All battle types still need map return/rejoin regression after shared transport
changes. The global tooltip service and host formatter are native-sensitive;
builds/test doubles do not establish safety or fidelity inside Bannerlord.
cmp-b34 compiled both roles without installed mutation. cmp-c34 passed all 24
projects, exercising the actual presenter with narrow event/VM/input substitutes,
not the native renderer (17 assertions). cmp-b35 both builds and cmp-c35 all 24
projects also passed, including unavailable-scene cleanup. Native preparation uses
cmp-b36 (both builds passed) and cmp-c36 (all 24 passed): added host capture result/
reason/row-count metadata under explicit VerboseDiagnostics, at most once per five
seconds, with no additional native state access. No default-on hot-path logging.
Fresh `artifacts/runtime/map-tooltip-r1-20260928` now staged cmp-b36 with five
cmp-b19 module preimages and seven config preimages. Test-owned Steam 59600 is
recorded; native acceptance and cleanup are PENDING. Do not infer runtime success.
The run now verified campaign 7052/menu-owned dedicated 33956/shortcut MP 39632
loaded identities and automatic StartMap command=1. Campaign paused on map;
MP awaits user Login click per Computer Use restriction (async question sent).
Do not relaunch roles or call tooltip acceptance passed: no MP hover yet. Exact
relay run 1dc28149f46043e5a1a71bf039c0d2bc is bound to recorded host/server identities.
Live run README owns current process/evidence/cleanup continuation details.
Next: coherent three-role deployment, host/MP normal+extended settlement/party
comparison, repeated hover/focus/dialogue lifecycle, config/file/process cleanup.

### Native scene picking candidate (2026-09-28, not runtime-verified)

**Final bounded result:** the section title and preparation statements below are
historical. cmp-b39 now passed ordinary party/settlement native-ray hover,
empty-point clearing, second zoom, camera detach/return, read-only Tristania
dialogue open/update/close and Avalyps entry/body-hide/exit/body-hover restoration.
967 requests, zero Denied controls; no picking/presentation-disable message or
observed crash. Host village gold hover ring is absent in replica. Army redirect,
prisoner/raft rules, naval/siege targets and battle-return/rejoin remain open.
All 22 owned identities are absent, seven configs restored, no new configs;
cmp-b39 retained. See [bounded result and evidence](../../artifacts/runtime/map-picking-r1-20260928/RESULT.md).

The former UpdateReplicaInformation picked the nearest projected origin within
96 physical pixels. It could select empty space and misidentify adjacent objects;
native-looking tooltip rows did not imply native hit-region parity.

Installed binary audit (SandBox.View.dll, ilspycmd): MapScreen.HandleMouse uses
SceneView.TranslateMouse, Ray and Scene.SelectEntitiesCollidedWith with 128-entry
buffers. SandBoxViewVisualManager sorts components by Priority. MobilePartyVisualManager
has priority 10 and reverse-walks hits, overwriting hover (first mobile array hit
wins); SettlementVisualManager priority 40 reverse-walks and sets hover only when
null (last settlement array hit wins). MobilePartyVisual.InitializePartyCollider
creates a radius-0.5 sphere at the unscaled strategic origin with BodyFlags 144
(Moveable | OnlyCollideWithRaycast). SandBox.MapScene also uses CreateNewScene(false).
SettlementVisual maps the root and recursive child pointers to its settlement.
These facts were checked against installed assemblies, not inferred from docs.

Source candidate replaces the pixel-radius loop with the native ray/query in
UI/CoopCampaignMapPrototypeMissionView.Picking.cs. CoopCampaignMapPickResolver
retains the audited ordinary mobile/settlement hover ordering, bounds counts and
bindings, and rejects unknown/conflicting handles. Settlement bindings reference
only the owned map scene. Current disclosure and actual parent-inclusive geometry
visibility are required before returning an id. Parties receive separate owned
ray-only spheres only while body rendering is admitted; hidden/upsert/removal/scene
release paths remove them. Unchanged frames do not rewrite native physics state.
No native Campaign.Current or campaign visual managers are constructed in MP.
Query/creation exceptions disable this scene's picking once, without silently
falling back to approximate screen-radius selection. There is no per-frame logging.

Scope: only the prototype client map view. No physical field/village/siege/hideout
mission physics is changed. Nevertheless AddSphereAsBody/SelectEntitiesCollidedWith
are native-sensitive: managed tests and successful compilation do not prove live
safety, hit ordering, visibility or cleanup. Contract tests cover pure order,
admission, conflict, stale-handle/reset and count bounds, plus source wiring.
cmp-b38 both builds and cmp-c40 all 24 projects passed before final frame-cache/
upsert cleanup refinement. Final cmp-b39 both builds and cmp-c41 all 24 projects
passed; evidence is retained under artifacts/verification for each named run.
Fresh map-picking-r1-20260928 has now staged cmp-b39 with five module/seven config
preimages. All three loaded identities are verified; server automatically opened
the map and published 397 entities. MP awaits user Login. Native picking acceptance
and cleanup are pending; see the run README for exact continuation state.

Remaining acceptance: compare actual hover boundaries and overlapping targets at
multiple zooms with the host; verify hidden variants/settlement entry/exit, camera
detach/return, dialogue exclusion and battle-return/rejoin; audit attached-army,
prisoner-main-party and naval/raft-specific target semantics. Those require host
metadata/visual treatment not established by this ordinary-land candidate. The
read-only pinned information panel intentionally does not reproduce native movement
click commands. No full map/hover 1:1 claim is made.

Follow-up installed-manager audit retained both decompiled types and the concrete
missing metadata/admission rules in [picking audit](../../artifacts/native/map-picking-20260928/README.md).
InRaft/AtSea flags already cross the protocol but are not yet enforced by pick
admission; AttachedToArmy does not identify the hover parent, and prisoner-main
hover state is absent. No candidate source or installed binary changed during
this audit; cmp-b39 is still awaiting user Login for native verification.

### Tooltip first native result and flicker correction (2026-09-28)

**Final follow-up:** cmp-b37 / map-tooltip-r2-20260928 is CLOSED with bounded
flicker acceptance: the user confirmed it disappeared during normal/extended
party checks. Agent snapshots and successful host captures support normal Lavenia
retention for about 47 seconds, correct Tristania replacement and hover-exit
clearing. Final MP log has 305 requests, zero Denied controls and no tooltip
presentation-disable message. Both builds/all 24 cmp-c39 projects passed.
All three loaded identities were verified, automatic map opening and native join
worked. All 24 recorded owned identities are absent; seven configs match, no new
configs/products; cmp-b37 is retained. Exact three-file relay archived/removed.
Battle-return/rejoin, dialogue exclusion/return, camera regression and sustained
extended village verification were not rerun; full parity remains open. Existing
guarded intermission, distance-cache and FMOD diagnostics remain independent.
See [result and cleanup evidence](../../artifacts/runtime/map-tooltip-r2-20260928/RESULT.md).
Preparation statements below are historical.

The cmp-b36 run above is now CLOSED with stability FAILED. Normal Tristania and
Lavenia cards reached the stock movie and matched the observed host text/layout.
The user confirmed extended cards open but reported continual disappearance on
both entity kinds. Client log has interleaved Rows and Denied refreshes; host
capture succeeds. See [final result](../../artifacts/runtime/map-tooltip-r1-20260928/RESULT.md).
All 22 exact owned identities are absent, seven configs match, no new config or
product process remains, and all five cmp-b19 module preimages are restored.
The exact bound three-file tooltip channel was archived and removed.

Follow-up source changes only map tooltip interest/presentation and contracts:
accepted terminal receipt now starts an additional 250ms cooldown, avoiding
compressed server-arrival intervals from variable uplink latency. Server gates,
request-anchored two-second expiry, immediate hover/mode/disclosure invalidation,
and denial clearing remain unchanged. Cooldown survives Clear but not fresh lease
Reset. Subtraction comparisons avoid timestamp addition overflow.
Same-mode presenter refresh no longer toggles IsActive off. Identical displayed
rows/mode retain row objects and movie, while a fresh admitted payload still
renews the network's bounded display lifetime. Changed row styles/text update.
Installed TooltipWidget.RefreshState restarts its 0.2s fade when visibility
returns; PropertyBasedTooltipWidget adds two initial frames. No native patch or
reflection mutation is introduced by this correction.

Scope audit: only prototype controller and prototype client presenter use this
interest policy; battle UIs are not changed. Map return/rejoin after all battle
types remains a regression requirement. New tests exercise 240 variable-transit
round trips and same-card row/movie/visibility preservation. cmp-c37 was unable
to read NuGet.Config under the sandbox (no contracts executed); cmp-c38 passed
all 24 after authorized rerun, before final presentation changes. Final cmp-b37
both builds and all 24 cmp-c39 contracts passed. No native correction acceptance yet.
Fresh map-tooltip-r2-20260928 is live with campaign 34224, menu-owned dedicated
23764 and shortcut MP 28204, all loaded MVID/disk SHA identities verified.
Automatic map opening succeeded; MP is at Login pending user action. The first
campaign launch exited with Steam API initialization failure before Steam was
ready; its evidence and exited identity are retained separately. Seven config
and five module preimages remain available. Cleanup is pending; see the run README.

## Banner color preservation candidate (2026-09-28)

After the cleaned cmp-b10 native run, an installed-assembly audit found a concrete
nameplate mismatch: both replica UpdateBanner methods call Banner(code, color1,
color2), whose installed constructor deserializes then calls ChangePrimaryColor
and ChangeIconColors. Native PartyNameplateVM uses Party.Banner directly and
SettlementNameplateVM uses Settlement.Banner directly. Faction/cloth/nameplate
colors are not an instruction to repaint the host's serialized heraldry.

The candidate uses Banner(bannerCode) for supplied banners in both replica overlay
VMs. Existing absent/malformed fallback behavior is retained; this does not claim
fallback parity. The source contract checks both call sites and confinement of
recoloring to the existing missing-banner fallback. No wire/schema, campaign state,
physical battle scene, native diagnostic or authentication behavior changes.
Field/village/sally/siege/hideout battle renderers do not use these map overlay VMs;
map re-entry and adjacent lifecycle regression remain required.

Both cmp-b11 isolated role builds and all 24 cmp-c12 contracts passed. Build-time
installed inventories were unchanged. Client SHA256
955F2D0E6C60FAAB3061F8983F7E3E72EA492CD1EE1D79F7FDD0F014CAD039DA;
dedicated unchanged 00E81759C40BB191038B27966E24913900FF28836BC862CF34AAFE30A6A299FE.
Native map-banner-r1-20260928 passed the bounded comparison: party 30 brown/gold,
80 purple/gold and 22 black/white-striped nameplates match the host frame, as do
the observed settlement banners. Automatic map readiness was 465 ms. Manual zoom
detached the camera; explicit return restored host following. Seven screenshots
and final role logs are retained. All three loaded assembly identities verified.
Multiplayer used the exact desktop shortcut with Safe Mode No; its first observed
main screen was already Home, so this run contains no agent Login action.
The existing guarded IntermissionVmCrashGuardPatch ArgumentNullException remains;
no visible crash occurred. Dialogue, battle re-entry, remote clients, changing
factions and full visual parity were not retested/accepted in this narrow run.
Cleanup and independent postflight passed: all 22 recorded identities absent,
all seven configs match, no new config files/product processes; test-owned Steam
ended, test saves preserved, installed cmp-b11 hashes match. Two inspected config
changes (safely_exited and dedicated map selection) were restored. Five module
preimages remain available. This is not proof of the cause of every black banner
pixel (UI and 3D paths differ); fortification flags and the Avalyps lower icon remain open.

### Settlement occupant badge investigation (2026-09-28)

Do not classify the Avalyps lower book-like icon as a confirmed quest marker.
The retained cmp-b11 bridge snapshot (session 7af2ac50e6034b0890f20630448051e4,
dynamic revision 1121) has Avalyps QuestMarkerFlags=0, SettlementEventFlags=64
(production), IsSettlementInRange=True. Vostrum is 0/2/True; Lavenia 0/64/True;
Tevea 16/64/False. This is host publisher output, not an independent host VM dump.
The lower icon's precise rendered identity still needs native comparison.

Confirmed independent gap: replica SettlementParties.PartiesInSettlement is
always an empty placeholder, while the installed stock small/medium/large
nameplate prefab binds a lower party grid. Native source research is retained in
artifacts/native/map-settlement-events-20260928. Symbols:
SettlementNameplateEventsVM.RefreshQuestCounts, SettlementNameplatePartyMarkersVM
(PopulatePartyList/IsMobilePartyValid/RegisterEvents/UnloadEvents),
SettlementNameplatePartyMarkerItemVM constructor, and
SettlementNameplateWidget.UpdateNameplateTransparencyAndBrightness.

Host rules: SettlementNameplateVM.IsInRange registers/unregisters the party-list
events; leaving range clears the list. Ordinary settlements use IsInspected;
hideouts use spotting. Party admission excludes garrisons/militia, excludes a
disguised main party, and accepts army members only when the army leader is the
main party and the main hero is not disguised. Non-army parties otherwise pass.
Do not substitute world-map IsVisible or reveal every occupant of a visible town.
Items classify caravans first (sort 1), then lord parties with leaders (sort 0,
ActualClan.Banner with native null handling), then default parties (sort 2);
IsBandit chooses the default icon. Native grid uses five columns, 30x20 cells,
26x26 item widgets. The existing stock UI handles fading; no custom guessed icon
or local campaign simulation is needed.

Next implementation gate: a bounded host-admitted descriptor list (type/bandit/
serialized banner only, no rosters, hero identities or hidden party data), exact
ordering and range-clear semantics, wired through full/catalog/binary/network
paths with schema/version and packet-size review. Preserve supplied banner colors
and native null behavior; do not fabricate flags. Validate enter/leave, disguise,
army admission, duplicates, malformed/oversized input, hide/reappear, and map
re-entry after field/village/sally/siege/hideout battles. Physical battle renderers
do not consume these nameplate items, but map re-entry is shared across them.
No occupant descriptor transport or production implementation added in this audit.

Installed Widgets DLL SHA256:
3828067E5BC61521D399BB237CAC6488C7C999B045C2436E86906E1232BD9DBE.
Small prefab SHA256:
635A826BBFBC3D2F140FF06BA8ACFF35DAA8ED20EF9141F06E4EB072E23B1D83.
SandBox.ViewModelCollection hash is listed below. The failed exploratory type
name SettlementNameplatePartiesVM was corrected to the actual PartyMarkersVM;
no runtime was loaded or changed by the decompilation.

Read-only native research retained under artifacts/native/map-party-banner-20260928:
MobilePartyVisual and Banner decompilations. Installed SHA256:
- SandBox.View: C7360E71DA06799A0CB6C00C4A0CFAB5877621197BA25101433AC28654D169E2.
- TaleWorlds.Core: 3E5CECE8CA42FAF3FDDE27632519ED2A6F86D1EC6D1A742867729F00001689BF.
- SandBox.ViewModelCollection: 8C4B2CA448A3BC8789A6F8D7B4427E7A9832A31C45DD79E9034173535EBAEBF4.

Fortification flags are a separate, unimplemented presentation path. Installed
MobilePartyVisual.RefreshPartyIcon creates campaign_flag meshes for parties inside
a friendly fortification with a clan banner, using GetBannerPositionForParty and
party-count/army-dependent scale. Visibility/fading belongs to the party visual,
not simply settlement visibility. The current mobile-party filter excludes parties
inside settlements. Do not replace missing flags with the settlement owner's flag,
relax disclosure to reveal hidden occupants, or invoke GetBannerOfCharacter from
MP: it depends on MapScreen.Instance.CharacterBannerMaterialCache. The future path
needs bounded host-admitted rendered banner descriptors and owned client materials,
including exact native transform, lifecycle and callback ownership. No such
transport/material code has been added in this step.

Documentation impact: canonical evidence here; current navigation/build entries
in README and BUILD_TEST_DEBUG. Existing architecture/flow/ownership remain intact,
so no new subsystem or authority section is needed for the two-constructor fix.

## Wall and siege preparation candidate (2026-09-27)

cmp-b10 adds SettlementWallStates (bounded 32-character intact/breached section
sequence; empty unknown/not applicable) and SettlementPreparationState (unknown,
hidden, visible). Capture runs only after existing host disclosure admission and
reads the same authoritative wall-health and attacker preparation inputs as native
RefreshWallState/RefreshSiegePreparations; no local-client campaign simulation.
Nonfinite/oversized wall input is unknown, not guessed intact. Missing preparation
object is hidden as in stock code; capture exceptions are unknown. This does not
yet capture the actual post-refresh child state or override a changed game version.

Full state, catalog, clones, equality/delta, projection, both legacy messages and
binary catalog carry both fields. Protocol 25 / bridge 21 / catalog 6 reject old
layouts; malformed values/non-settlement payloads are rejected. The owned root is
hidden while level filtering runs, then walls follow native recursive breachable
root order and section-index modulo count, with direct solid/broken children toggled.
Preparation visibility overrides follow. Missing required wall state or unknown
preparation keeps geometry hidden. Unchanged valid states are not reapplied; level,
wall/preparation change and disclosure reappearance reapply before reveal. No physics,
campaign mutations or new diagnostics. Includes cmp-b9 authoring-helper cleanup.

Both cmp-b10 isolated builds passed, installed inventories unchanged/no product
launch. Client SHA256 B26F304461C09976F7062A01745B2B006EE9EEA091D7C7342D6A8AEEDE3886D3;
dedicated 00E81759C40BB191038B27966E24913900FF28836BC862CF34AAFE30A6A299FE.
cmp-c11 all 24 canonical projects passed: thresholds/bounds, transitions, ordered
apply/failure/reappearance, full/catalog/binary transport, clone/projection/deltas,
required fields/malformed values, version rejection and native source wiring.
Physical field/village/sally/siege/hideout paths are unchanged, but shared transport
and native child visibility require fresh runtime/battle regression evidence.

Native run `artifacts/runtime/map-wall-r1-20260927` completed at 20:55Z. All three
roles loaded the expected cmp-b10 MVIDs and installed hashes. Initial campaign
30288 exited after Steam API-not-ready warning; retry occurred only after verified
exit and Steam connection readiness. Campaign 56188 launched dedicated 35100 via
its menu; multiplayer 468 launched via the exact requested desktop shortcut,
Safe Mode No. The user completed Login. AC_COOP join opened the map automatically
without a console command; render readiness was 370 ms.

Seventeen screenshots and exact-role logs are retained. The observed Vostrum
footprint now agrees with the host in the paired initial/zoom frames: the earlier
extra dark/ladder-like structures are no longer observed. Avalyps/Lavenia retain
the matching selected building variants. This is a bounded visual improvement,
not pixel-perfect or all-settlement acceptance. The retained catalog (bridge 21,
revision 4, 398 entities) gives Vostrum level flags 9, walls "00", preparation 1;
Avalyps/Lavenia/Tevea flags 10, no wall sections, preparation 1. No active siege or
wall-damage transition was exercised. Independent cause attribution between the
helper-removal and wall/preparation changes is not claimed.

Host zoom propagated. Replica wheel input detached; explicit return restored host
framing. Tristania map-overlay dialogue opened with three actors (44 ms readiness),
ignored a replica answer click, updated text on the host's choice and closed back
to the current host-following map after campaign movement/daylight change. Actors
remained present across text changes/window switches. This run did not re-certify
continuous animation smoothness, mission-backed companion dialogue, full HUD,
multi-peer transport or map/battle/map regressions. Visible remaining discrepancies
include settlement/party banners and some marker/UI details. Existing guarded
intermission ArgumentNullException entries remain; no visible crash during checks.

Fresh descendant audit found no unrecorded child processes. Cleanup checked exact
path/start identities for all 23 recorded processes, preserving any mismatch. One
exiting Steam helper was skipped; independent inspection subsequently found every
recorded PID absent. All seven configuration hashes match preimages, with no new
configuration files or product processes. Only safely_exited and the dedicated
map selection changed, were inspected and restored. Test-owned Steam exited;
test saves and cmp-b10 modules remain, verified by independent postflight hashes.
The cleanup and standalone postflight both succeeded; a combined diagnostic call
returned exit 1 solely because its trailing Get-Process query found no recorded
PIDs. Standalone postflight was rerun successfully (20:55:38Z). No commit/staging.

Next: authoritative live fortification banners, unresolved map-marker/UI parity,
active wall/siege transitions and adjacent mission lifecycle regression. Goal
remains active and incomplete; do not treat this bounded pass as full acceptance.

## Fortification authoring helper candidate (2026-09-27)

Source cmp-b9 adds `RemoveFortificationAuthoringHelpers` immediately after circle
cleanup and before settlement bindings/layers. It snapshots only descendants of
owned-scene town/castle roots, deduplicates native root handles, rejects over 4096
descendants per root, and matches only `main_map_city_gate` and
`map_banner_placeholder`, the additional tags removed by installed native startup.
`CoopCampaignMapSettlementAuthoringPolicy.SelectRemovalRoots` resolves all ancestry
before mutation, deduplicates aliased native handles, suppresses nested removals,
and rejects cycles/invalid identities/depth overflow. Only planned topmost helpers
are hidden and removed with native reason 112. No campaign or physical mission
scene mutation, protocol change, per-tick logging or local simulation.

Installed XML audit found 252 fortification roots, 120 city-gate helpers and 798
banner placeholders; maximum descendant count 559. This proves bounded asset
coverage on this installation, not the exact cause of every visible Vostrum extra.
Live banner replacements, wall state, siege preparations/engines and alternate
scene coverage remain required. Removal/native lifetime is runtime-sensitive.
Field/village/sally/siege/hideout battle scenes do not invoke this owned-map method;
their regression is still required, not inferred from source isolation.

Both cmp-b9 isolated builds passed, no product launch, installed inventories
unchanged. Client SHA256 B76537D8783ECE423380CE20AB1F26AC6497013CD9CBD9D5B62CFF43EEAAD201;
dedicated SHA256 032B0ECBE1019628EAB2D45EE4C8126B9EF2DBCBBDBA82BF7F6F566FB174BAF7.
All 24 cmp-c10 canonical projects passed, including exact-tag exclusions,
alias/nested/cyclic/empty/invalid helper plans and source ordering/scope. Candidate
is NOT deployed or native-verified; installed cmp-b8 and its cleaned run remain
the runtime baseline. Next: host-authoritative mutable wall/preparation state,
then coherent native geometry comparison and map/battle lifecycle regression.

Documentation impact: canonical detail here; narrow status/navigation entries in
README/CODE_MAP/ARCHITECTURE/RUNTIME_FLOWS/INVARIANTS_AND_RISKS/BUILD_TEST_DEBUG.
No commit/staging or unrelated battle implementation change in this step.

## Host-selected settlement levels (2026-09-27, bounded native result)

Native run `artifacts/runtime/map-level-r1-20260927` completed at 20:13Z.
Campaign, menu-owned server and exact-shortcut multiplayer loaded coherent cmp-b8
identities. Safe Mode No, AC_COOP join and automatic map start without a console
command passed. Map render readiness was 421 ms. Host/replica screenshots show
the extra Avalyps/Lavenia building variants removed, but Vostrum retains extra
dark/ladder-like geometry: full geometry parity is NOT accepted. Wall/siege
overrides are the next investigation, not yet a proven explanation of that image.

Wheel input detached the local camera; explicit return restored host framing.
Tristania map-overlay dialogue opened in both clients (44 ms replica readiness),
retained actors through text changes and window switches for over 14 minutes,
ignored a replica answer click, updated on host choice, and closed back to the
host-following map. Sixteen screenshots, exact-role logs and final catalog retained.
Existing guarded intermission exceptions remain; no observed crash in this run.
No battle-cycle, remote-peer, hideout transition or full dialogue HUD parity claim.

All 22 recorded owned identities are absent, no product processes/new config files
remain, and all seven configuration hashes match preimages. Only engine safely_exited
and the dedicated map selection differed and were inspected/restored. Cleanup
skipped two identities after dependent processes exited; independent postflight
confirmed no products or exact-owned identities remain. Test-owned Steam exited;
test saves and installed cmp-b8 were retained, with independent hash verification.

Post-run read-only native/XML review is retained at
`artifacts/native/settlement-presentation-20260927/POST_CMP_B8_FINDINGS.md`.
It identifies unhandled fortification helper removal, wall solid/broken overrides,
six Vostrum siege-preparation meshes and separate engine frame anchors. Exact
pixel-to-node attribution remains unproven. Next work must preserve host authority,
apply overrides after level selection and avoid touching physical battle scenes.

cmp-b8 reads the admitted host SettlementVisual's private uint _currentLevelMask
without refreshing/dirtying campaign visuals. Semantic level_1/2/3 and
civilian/looted/siege bits cross protocol 24 / bridge 20 / catalog 5; engine bit
positions do not cross the wire. Unknown masks, absent visuals or changed field
types produce unknown (zero), not a guessed civilian level. Hidden settlements
are rejected before capture. Catalog equality, clone, binary/legacy messages and
shared full/dynamic projection carry the choice, including late join and updates.

The owned replica root is hidden while the known local scene mask is applied to
its descendants using the installed native subset rule. Up to 4096 descendants
may be mutated per application; unchanged visible choices are not rewritten.
Unknown state/application failure hides the root, disclosure removal hides it,
and reappearance reapplies before reveal. No native physics or campaign manager
is enabled. Private-field compatibility and native resource visibility/lifetime
remain runtime-sensitive; bounded success above is not complete geometry parity.

Wall solid/broken overrides, siege preparations/engines, banners and dynamically
missing roots remain outside this level-only step. Physical field/village/sally/
siege/hideout mission paths do not invoke the map-scene adapter; unchanged paths
still require regression coverage. Mission-backed companion dialogue remains open.

Both cmp-b8 builds passed with installed inventories unchanged and no product
launch. Client SHA256 66E06EEE8FA3BF569B76673BDE38F247120A366C9FC62AF2AA3654F54DDA838F;
dedicated A4D4AC329D85B7B51A821AACDAEA90292974A10A5D2EA2E1D30635AE659787CC.
cmp-c7/cmp-b6 were blocked before meaningful validation by sandbox NuGet.Config
access. cmp-b7 exposed namespace qualification/enum cast compile errors, corrected
in cmp-b8. cmp-c8 passed 23/24; an overly broad source-test slice included a new
comment and was narrowed. Direct focused run passed geometry/transport but later
failed a separate test's missing runner environment. Canonical cmp-c9 passed all
24 projects, including semantic mask remapping, admission transitions, malformed
states, full/catalog/binary round trips, catalog delta and source wiring checks.

Documentation impact: this is the canonical implementation/evidence entry;
README/CODE_MAP/ARCHITECTURE/RUNTIME_FLOWS/INVARIANTS_AND_RISKS/BUILD_TEST_DEBUG
receive narrow status links. Build scripts and unrelated battle docs unchanged.

## Static settlement geometry admission (2026-09-27)

Source cmp-b5 adds `CoopCampaignMapSettlementGeometryAdmission<T>` bound only to
the MissionView-owned map scene. Initial root discovery uses stock town/castle/
village/wm_hideout/hideout tags and deduplicates native handles. Each root starts
hidden before layer construction. Full and incremental typed settlement projections
share UpsertEntityReplica admission; RemoveEntityReplica hides roots even when
overlay updates are suppressed. Release clears bindings before native destruction.
Ambiguous names stay hidden; the 2,048 unique binding cap fails setup rather than
exposing overflow. Unchanged visibility is not rewritten every frame. No protocol
or host disclosure gate change, campaign simulation, or hot-path diagnostic.

Read-only installed XML audit: 492 of 493 settlement IDs each resolve to one tagged
scene entity; retirement_retreat has no static match. This bounds asset coverage
to this installation, not arbitrary overridden maps. Dynamic/missing roots are not
invented. Root admission is not variant, wall/preparation, banner or topology parity.

cmp-c6 passed all 24 canonical projects, including pure admission/removal/reappear,
duplicate/case-collision, disposal/rebind, overflow and source-wiring tests. cmp-b5
compiled both roles without installed mutation/product launch. Client SHA256
`614DC6C0CAE9ABE9F9ABB41ECA35F16F931A2DE1A9D52B0ED760536E27499ABE`;
dedicated remains `2F63E54EC9A3B58AAA517D8F3F123450887C1C0E07ACF452C019F237B90812A9`.
Bounded native run `artifacts/runtime/map-geometry-r1-20260927` finished and cleaned
at 19:17Z. cmp-b5 remains installed. Automatic menu-server map, shortcut MP, Safe
Mode No, AC_COOP join, visible settlement geometry, absent authoring rings in the
observed views, wheel-detach/explicit-return and Tristania map-dialogue open/close
passed. Sixteen screenshots and exact-role logs retained; 433 ms map render ready.
All seven configurations match preimages, no new config/owned/product processes.
Existing guarded intermission exceptions remain in logs; no crash observed.

Full geometry parity explicitly fails: Avalyps/Lavenia show extra replica buildings
versus host because level/state variants are not applied. Hidden-hideout removal/
reappearance and remote-peer disclosure remain unverified. Separate negative test
Party > Iara the Healer > Talk opens native Conversation mission but client stays
on map: publisher excludes Mission.Current, capture only handles MapConversationView.
Mission-backed dialogue coverage remains open; not attributed to this geometry fix.
Native risks: invisible-resource loading on reveal, tag/name coverage and scene
lifetime. Physical battle paths unchanged; adjacent regression/multi-peer open.

Documentation impact: canonical evidence here; README, CODE_MAP, ARCHITECTURE,
RUNTIME_FLOWS, INVARIANTS_AND_RISKS and BUILD_TEST_DEBUG carry narrow navigation,
ownership and status entries. Build tooling unchanged.

## Settlement scene presentation investigation (2026-09-27)

After cleaned cmp-b3 native verification, inspect the actual installed managed
implementation rather than treating Main_map Scene.Read as campaign parity.
Decompiled evidence is retained in `artifacts/native/settlement-presentation-20260927`:
`SettlementVisual.OnStartup`, `RefreshPartyIcon`, `RefreshLevelMask`,
`SetSettlementLevelVisibility`, `RefreshWallState`, `RefreshSiegePreparations`,
`SettlementVisualManager` and `MapScene.Load`.
Installed SandBox.View.dll SHA256:
`C7360E71DA06799A0CB6C00C4A0CFAB5877621197BA25101433AC28654D169E2`;
SandBox.dll SHA256:
`587C6E94C425745C1478F632F3B66226FF8298666CB27E8E6D9A62488DF5956A`.

The stock settlement startup records the frame of `map_settlement_circle`, hides
it and removes it with native reason 112. This is an authoring helper, not the
runtime selection circle or siege-machine decal. The isolated replica skipped
that startup and left these helpers visible. Source now snapshots exact-tag
entities from its own `_mapScene`, hides/removes them once immediately after
Scene.Read, before any scene layer/render resource setup. No global Harmony patch,
campaign scene, physical mission, filesystem asset or runtime siege decal changes.
Native removal is runtime-sensitive; source assertions only verify ordering/scope,
not actual native resource lifetime or visual results.

Validation: cmp-c5 passed all 24 canonical projects, including new source-order,
single-call, exact-tag, owned-scene and enumeration-snapshot checks. cmp-b4 built
both roles with installed inventories unchanged and no product launch. Client
SHA256 `DB69A9289C9B6D7BC483CCC3E7F04DA43FA2D9CB7E2E421BDACD37903D703746`;
dedicated unchanged `2F63E54EC9A3B58AAA517D8F3F123450887C1C0E07ACF452C019F237B90812A9`.
Historical cmp-b4 was compile-only; its circle correction was subsequently included
in cmp-b5 and bounded native-verified above. Installed now cmp-b5.
Field/village/sally/siege/hideout physical scenes do not call this method; their
regression tests are still required for a full release, not inferred from builds.

### Geometry gap discovered before cmp-b5; variant work still required

Before cmp-b5, the settlement branch updated nameplates only. Static settlement
geometry was not bound to host admission or dynamic removal. Thus label/party
visibility tests do not establish host-only settlement geometry disclosure.
This is a correctness gap, especially for hidden hideouts, not merely cosmetics.
The source candidate above now addresses static root admission, not variants.

Native RefreshLevelMask chooses named `level_1/2/3` plus `civilian/looted/siege`:
village production uses hearth thresholds 200/600, fortification uses wall level,
hideout uses level_1. SetSettlementLevelVisibility checks child masks; subsequent
wall solid/broken and siege-preparation visibility override selected descendants.
Applying a guessed civilian level or wall mask alone is therefore insufficient.
Future capture/application must preserve host-visible root admission, the chosen
visual variants, wall/preparation overrides, hidden/removal state and transition
updates without sending hidden campaign internals. Capture must be bounded and
read-only; client mutation confined to its owned replica scene. Do not enable
campaign simulation/managers in multiplayer or infer unseen settlement state.
Reconcile entity identity and native scene topology before enabling geometry;
fail closed when a required binding/state is unavailable. Verify town, castle,
village/looted village, hideout visibility and siege visual transitions, plus
late join, removal/reappearance and remote peers. These remain full Goal gates.

Documentation impact: canonical evidence here; README/BUILD_TEST_DEBUG and
CODE_MAP/INVARIANTS carry short status/scope links. No wire/authority/default or
physical battle lifecycle change in the circle correction; architecture and
runtime-flow documents intentionally unchanged for this narrow source step.

## Host camera following and settlement presentation (2026-09-27)

User-approved behavior: each client initially follows the host; manual movement
detaches only that client. The explicit `До камери хоста` button resumes the current
host frame. Idle never reattaches. `CoopCampaignMapReplicaCameraPolicy` owns per-view
state; the map view snaps on first/explicit attachment and interpolates subsequent
valid frames. Missing frames retain the view. Manual fallback navigation before a
first host frame stays detached. Scene release resets ownership. Dialogue, inactive
layers, foreign focus and native Gauntlet hit-testing block underlying map input.
Camera changes neither expand host-visible entities nor send campaign commands.

`IsSettlementTracked` and `IsSettlementInRange` now cross full/dynamic bridge and
network messages, projection and native widget bindings. Installed native
SettlementNameplateVM semantics: tracker membership plus ordinary `IsInspected`,
or hideout `ISpottable.IsSpotted`. These are not camera-distance guesses. Capture
follows visibility admission; presentation-only changes invalidate dynamic deltas;
hidden removals clear both bits. Full non-settlement entities reject either bit.
Missing/malformed required booleans fail closed. Protocol 23 / bridge 19 / binary
catalog 4: all roles must match; catalog unchanged because fields are dynamic.

Validation: cmp-c1 failed before compilation on denied NuGet.Config access.
Permission-reviewed cmp-c2 passed all 24 projects and cmp-b1 built both roles for
the settlement stage. Final cmp-c3 passed all 24, including 1,117 camera assertions;
cmp-b2 built both roles (77/49 warnings, zero errors) with installed inventories
unchanged. This is not native visual acceptance or camera smoothness evidence.

Fresh `artifacts/runtime/map-follow-r1-20260927` explicitly installed cmp-b2 with
five module and seven config preimages. Native initial follow/host zoom/local
detach/current-host return and a repeated detach/return passed. All 19 owned
identities exited; seven configs match, no new configs/product processes and the
installed cmp-b2 hashes are independently verified. Steam was absent at baseline;
the test-owned Steam was removed. Screenshots 03–12 and final logs are retained.
Pan/rotation, dialogue input arbitration, multi-peer and adjacent battle gates
were not covered. No full camera or visual acceptance is claimed.

Native host/client comparison exposed missing tracked Tevea edge nameplate.
Installed SettlementNameplateVM.IsVisible returns true for tracked objects;
RefreshPosition retains their actual projected coordinates. Native
SettlementNameplateWidget.UpdatePosition uses those coordinates plus WSign to
clamp the label to the proper screen edge, including behind-camera positions.
The replica instead culled at both the mission prefilter and final visibility
policy, and replaced out-of-view coordinates with (-1000,-1000).

Follow-up source correction: ShouldShowSettlement accepts the transported
tracking bit after finite-value checks; the mission prefilter reuses this same
policy. The overlay preserves raw projected coordinates whenever the label is
visible, leaving edge placement to the unmodified native widget. This affects
only already host-admitted entities; disclosure/hidden removals remain unchanged.
Tests cover all three sizes, zoom boundaries, front/behind and inside/outside,
untracking, malformed numbers and both integration gates. cmp-b3 built both roles
without installed changes; cmp-c4 passed all 24 projects. Tutorial-only tracking
is a separate parity gap, not silently inferred from tracker membership.

Fresh native `artifacts/runtime/map-edge-r1-20260927` installed cmp-b3 and verified
the loaded campaign, menu-owned dedicated and shortcut multiplayer identities.
Automatic StartMap command=1 required no console input; user-assisted Login and
AC_COOP join reached map rendering at 21:19:09.127 local. Screenshots 05/06 and
07/08 confirm the tracked Tevea left-edge label initially and after host zoom.
Local wheel input detached the view; explicit return restored the current host
frame (09/10). The first injected return click did not change the mode; a fresh
observation and second click did. Do not infer the cause or one-click reliability.

Tristania's three-actor dialogue reached visual readiness in 44 ms at 21:25:46.309,
remained visible beyond two minutes, and replicated later farewell text (11–13).
A replica answer click left the host answer state unchanged. Wheel input during
dialogue did not detach the map: after host close at 21:28:59.700 the replica
returned to the new host location/frame with Camera Host still active (14/15).
This bounds the input-arbitration pass to wheel input in an attached view; native
pan/rotation, detached-dialogue preservation and remote peers remain unverified.

At 18:30:55Z independent postflight verified all seven configuration preimages,
zero new configurations, no remaining owned/product processes and all five
installed cmp-b3 hashes. The 22 exact-owned identities included Steam absent at
baseline; test-owned Steam was removed and test saves retained. Cleanup preserved
an identity mismatch/exit race for dedicated PID 16956 rather than force-killing
it; the independent product enumeration subsequently confirmed no server remained.
Only engine safely_exited and the dedicated Map/add_map_to_usable_maps settings
needed restoration, with inspected postimage hashes retained.

This was not an error-free startup: first campaign 54444 reported Steam API
initialization failure and exited before module load. The retry was admitted only
after process absence and Steam readiness; its exact failure cause is unproved.
The MP intermission ArgumentNullException remained swallowed by the existing guard.
Remaining visible parity differences include white settlement rings, event icons,
bandit banner colors, settlement-model presentation and campaign HUD. Dialogue
clan emblem, facial synchronization and full pixel parity are not accepted.

This stage changes only map presentation, not physical field/village/sally/siege/
hideout mission logic. Their runtime regression and remote multi-peer acceptance
remain open. No new hot-path diagnostics. Native camera writes target only the
owned map camera; tracker reads are read-only host state. Full map/HUD/dialogue
parity and automatic battle-cycle gates are not closed by these tests.

Documentation impact: this is the canonical implementation/evidence home;
README, CODE_MAP, ARCHITECTURE, RUNTIME_FLOWS, INVARIANTS_AND_RISKS and
BUILD_TEST_DEBUG carry narrow navigation/status updates. Build tooling unchanged.

## Scope and baseline

Resumed the active Goal from checkpoint `c2261d8` after the model handoff.
The current milestone is a stable host-visible campaign map replica and
display-only native dialogue synchronization. Voting, market/inventory actions,
hero progression and hero editing remain out of scope.

This report is in progress. It does not supersede the earlier partial runtime
results until the corresponding fresh checks below have completed.

## Candidate identity

Fresh CompileOnly run `cmr-b1` passed both products with unchanged installed
inventories. Source was clean apart from retained untracked runtime artifacts.

| Role | Selected SHA-256 |
|---|---|
| Client | `4977279F1B4DDBDF45AABD1348DD02E85487A776D9162EA29703DB665335CDEA` |
| Dedicated, both bins | `C6D19880A98FE659637CE9AD4F118E43CCF8D20D0EF60942D3532384DA2549FE` |

The run-specific `Prepare-LiveRun.ps1` acquired the canonical game/dedicated
installation locks, retained exact module preimages, installed the three DLL
copies and client PDB, verified selected hashes, and verified lock release.
It also retained 64 configuration/save preimages before campaign launch.

Evidence and transaction root:
`artifacts/runtime/campaign-map-resume-20260926`.

## Live checks

- No campaign, dedicated server, multiplayer client, or On-Screen Keyboard was
  running at preflight. Steam was already running and is not test-owned.
- A direct auxiliary managed-launcher attempt displayed `There is no target
  file! Please verify your files.` and was dismissed. It did not load a game.
- Campaign launched as PID `24572`, start time `2026-09-26T13:11:35.0023457+03:00`,
  through the installed `Bannerlord.exe` with the established singleplayer
  module list and process-local prototype flag. Shader warning was accepted.

- Campaign continued to the world map. The pause-menu `Coop Dedicated Server`
  panel launched the server as PID `2224` at `13:14:34` (local time).
  The campaign log confirms `AC_COOP`, port `7210`, and the host self-join marker.
- Multiplayer was launched through the user's exact existing desktop shortcut:
  `C:\Users\Admin\OneDrive\Desktop\run_mp_with_mod_from_game_root.bat - Shortcut.lnk`.
  Shortcut command shell PID `17996` started at `13:15:11`; multiplayer
  `Bannerlord.exe` PID `25416` started at `13:15:12`. The Safe Mode dialog was
  answered `No`, and the native login button was clicked. No server join has
  been verified in this fresh run yet.
- All three product processes were responding at the latest process check.
  The dedicated log continues successful service heartbeat messages. This is
  server liveness evidence, not a map-mission pass.
- Screenshots `01` through `06` under the evidence directory retain the shader
  warning, campaign menu, loaded campaign, server panel before/after launch,
  and multiplayer Safe Mode prompt.
- GUI continuation initially needed the multiplayer window restored: state
  capture reports `window is minimized`, while activation reports
  `user input was detected in this window`. No stale screenshot coordinates
  were used. The user was asked to restore multiplayer and leave it active.
  A subsequent activation succeeded and exposed the login failure below.

## Steam login failure, before server join

The user reported `Steam is not running`; screenshot
`07-multiplayer-steam-login-failed.png` independently confirms the native
`Login Failed` dialog with that exact message. Any earlier conversational
description of successful login was premature and is retracted. A lobby-screen
class name and a successful transport connection do not prove authentication.

Read-only inspection of the exact installed
`TaleWorlds.Diamond.AccessProvider.Steam.dll` using ILSpy locates the message in
`SteamLoginAccessProvider.ILoginAccessProvider.CreateAccessObject()`. That branch
returns failure when `SteamAPI.IsSteamRunning()` is false, before requesting an
auth-session ticket. No login bypass or mod change was made.

Observed facts:

- Steam PID `13984`, started `2026-09-26T12:56:48.4092905+03:00`, is running in
  interactive session 1. Its log records a successful Steam account logon and
  tracks multiplayer PID `25416` as AppID `261550` at `13:15:13`.
- Both game processes loaded the installed `steam_api64.dll`, `steamclient64.dll`
  and `gameoverlayrenderer64.dll`. Loading those modules is not by itself proof
  that a subsequent availability check succeeds.
- Read-only `OpenProcessToken(TOKEN_QUERY)` inspection confirms Steam, campaign
  and multiplayer have the same Windows user, medium integrity (RID 8192), no
  elevation, no restricted token and no app-container token. A privilege-level
  mismatch is not present.
- In the real user's registry, `HKCU\Software\Valve\Steam\ActiveProcess\pid`
  is `21152`, but process `21152` does not exist. The only running `steam.exe`
  is `13984`. This is a concrete stale registration and a candidate explanation,
  not yet a proven exclusive root cause.
- The exact shortcut has no arguments or RunAsAdmin flag and points to the
  requested game-root BAT with the correct working directory. The game's
  existing `steam_appid.txt` contains `261550`.

Steam predated this test. The user explicitly approved closing the test roles,
normally restarting Steam and repeating the test. Multiplayer PID `25416`
exited through its native Exit/Yes UI. Campaign activation still failed, so the
exact path/start-time-checked campaign `24572`, dedicated `2224`, and shortcut
shell `17996` were terminated. No product process remained. Their logs were
copied before cleanup, and all 64 configuration/save preimages still matched.

Steam received its normal `-shutdown` request and exited. It was restarted as
PID `2740` at `2026-09-26T13:36:49.140198+03:00`. The registered PID then matched
the only live Steam process, and its connection log reported successful logon at
`13:36:58`. No registry value was manually edited. This removes the observed
registration discrepancy; successful Bannerlord login must still be checked.

The repeat campaign is PID `6240`, started at
`2026-09-26T13:37:27.694364+03:00`, using the same installed candidate and
singleplayer module list. This authentication gate is common to every
multiplayer battle type; it is not evidence of a campaign-map mission crash.

## Approved Steam restart and repeat join

The repeated campaign pause-menu server button launched dedicated PID `5848`
at `2026-09-26T13:39:27.0738964+03:00`. Campaign logs confirm `AC_COOP`, port
`7210`, and a persisted host self-join marker. The exact desktop shortcut then
launched command shell PID `12768` at `13:39:57.0497116` and multiplayer PID
`3188` at `13:39:57.589169` (same local date/time zone as above). Safe Mode was
declined again.

Native multiplayer login succeeded: the profile/Home screen appeared at
approximately `13:41:08`, retained as `12-repeat-multiplayer-login-success.png`.
This resolves the observed authentication gate without changing mod code,
registry values or credentials. The restart and corrected registration correlate
with success; the stale registered PID is not claimed as an exclusive proven
cause.

The native custom-server list selected the exact `AC_COOP` entry and Join.
At `13:47:14`, the multiplayer log reports `success=True`, `response=Success`,
`armedSelfJoin=True`, redirects the owned server connection to loopback, and
records `Join game successful`. The screen reached `Awaiting Server`.
Evidence `13-repeat-AC_COOP-selected.png` and
`14-repeat-awaiting-server.png` captures both states. A transient list-scroll
animation first selected an unrelated row; no Join was clicked until the exact
`AC_COOP` selection was visually confirmed.

Campaign is active again, its server panel is closed, and its pause menu remains
open. Supported `Alt_L+grave` and `Alt_L+Shift_L+grave` input did not visibly
open the RGL console. The name `asciitilde` was rejected by the automation tool
without dispatch. The user was asked to open the campaign console manually;
no equivalent post-parser request has been substituted in this repeat run.

## Literal command and baseline mission

The user opened the console and entered literal `coop.map_prototype_start` at
13:51:27 after automated key/text input failed. Screenshot
`15-literal-command-user-entered.png` records the accepted request. This is a
user-assisted console pass, not autonomous console-input evidence. The user
also closed the on-screen keyboard and console. Keyboard automation was
unreliable while screenshot-backed mouse actions generally worked; no Windows
integrity or security setting was changed to bypass that limitation.

The dedicated mission started, and client `Main_map` scene loading completed
at 13:51:33. The mission remained live until deliberate client exit at 14:18:12.
No post-start `host-state-unavailable` / `missing-file` transition was found in
the inspected logs. There was no observed crash, and the Application event-log
query for IDs 1000/1001/1002 since 13:37 found no Bannerlord/dedicated event.
This does not erase startup dependency warnings or the earlier pre-mission
intermission exception. Campaign shutdown also logged native resource warning
`ERC3577`; no crash is inferred from that shutdown warning alone.

Evidence `16`–`24` retains map, selection, movement and dialogue comparisons:

- Host-visible settlements and party counts were rendered. Wheel zoom worked.
- Campaign travel toward Tristania moved replicated parties; dynamic revision
  reached 20. Camera framing differed after manual client zoom.
- Clicking Lavenia's model selected/pinned its information. Earlier clicks
  near labels did not consistently select the intended object; label hit areas
  remain unverified. Do not classify all selection as broken.
- Information appeared at the upper-left regardless of its intended position.
- Party label colors differed (for example neutral 80 and looters 22), and a
  white settlement circle was visible. Exact map visual parity remains open.
- Camera keyboard pan/rotation was not meaningfully verified because injected
  key input was unreliable.

## Baseline dialogue open / update / close

The host opened a friendly Tristania conversation around 14:06. The client
displayed matching speaker/text/options using the native `SPConversation`
layout. Clicking a client answer did not advance the host. Host selection
updated the client text/continue state; clicking client Continue likewise did
not advance the host. Host farewell/continue closed both dialogue displays,
and the client's map/nameplates returned. Logs retain active revisions 4/5 and
inactive revision 6 at 14:10:11.700.

This is synchronous read-only lifecycle evidence, **not full 1:1 parity**:

- The client background remained the map; host speaker/guards/tableau were
  absent, and the speaker banner was missing.
- Ordinary answers showed spurious persuasion blocking/progress icons.
- The invented host-only option hint remained after replacement/close.
- Actual persuasion state, relation and visual character/scene data are not
  transported by the current dialogue contract.

## Map-only UI corrections after the baseline run

Exact installed native assembly/prefab inspection established that generic
Gauntlet `Widget` has no bound `Position` property. The information panel now
binds scalar `ScaledPositionXOffset` / `ScaledPositionYOffset`; pure placement
uses the actual layer `CustomScale`, shared panel dimensions and clamped
screen bounds. Boundary tests cover normal/edge/pinned/negative/tiny/invalid
inputs. No focus or label-hit-test change was guessed from the failed clicks.

Native `ConversationItem` binds persuasion child visibility even for ordinary
answers. Native regular answers supply a non-null default persuasion VM;
the replica's previous null left default widgets visible. The map projection
now supplies a non-campaign inert VM with false blocking/progress flags and
zero chances, removes the invented host-only hint, and clears information
hints when replacing/closing the movie. This is a non-persuasion baseline,
not implementation of actual persuasion parity.

Adjacent review found the null persuasion property in hideout dialogue and
the unsupported generic `Position` binding in battle-control/hideout-stealth
prefabs. These are recorded latent risks, not automatically modified: each
needs its own evidence and scenario validation. No authority, spawn, battle
network behavior, or production diagnostics were changed.

Validation:

- Focused campaign-map contracts passed.
- `cmr-c2`: all 24 contract projects passed.
- `cmr-b2`: both CompileOnly products passed for the panel-only change.
- `cmr-b3`: both final CompileOnly products passed after dialogue corrections;
  installed inventories remained unchanged. Client SHA-256
  `19B039C0AF5B34CF444E54927A51D216969ECB7A2BAA4DEC59540C173B315CE8`;
  dedicated SHA-256
  `B41BFFF5365936E134CC46A0A161789728F07D2F7BCA72C2B302167072458A56`.
- These corrections are **not yet live-verified**. All screenshots above are
  from installed baseline `cmr-b1`, not `cmr-b3`.

## Baseline cleanup

Campaign 6240 and client 3188 were closed through their observed window
titlebar buttons. MP shader helper 25748 exited before remaining cleanup.
Dedicated 5848, shortcut shell 12768 and surviving owned helpers were stopped
only after exact PID/path/start-time checks. No name-wide termination occurred.
An initial Windows PowerShell 5.1 JSON-array validation mistake stopped the
postflight before any restore; independent file-by-file inspection confirmed
intact preimages. A later reused PID was preserved rather than terminated.
The corrected audit completed at 14:22:50 local time.

`cleanup.json` records all 64 preimages matching, zero new configuration/save
files, no remaining owned processes, and unchanged Steam process 2740. No
configuration restoration was needed after normal game exit; saves were not
changed. UDP 7210 was free. Exact role logs are retained as `evidence/final-*`.
No private save or raw authentication log is intended for publication.

## Remaining gates

Install and live-check `cmr-b3` (including the changed XML prefab), then address
the recorded visual/interaction differences using native read-only host data.
The full Goal remains active and incomplete. Voting, progression, trade and
per-player inventory remain outside this milestone.

## Follow-up UI candidate run (in progress)

`campaign-map-ui1-20260926` installed five exact files from `cmr-b3`, including
the changed client replica-info XML, after retaining module and 64 config/save
preimages under the new run root. Campaign 1436 started at 14:26:42.2019489,
continued the save, and the campaign menu launched dedicated 18552 at
14:29:14.2760451. The exact desktop shortcut started shell 12692 at
14:29:31.5028778 and MP 21920 at 14:29:32.0960677. All times are local +03:00.
Safe Mode was declined and native login succeeded again. The native server
list selected `AC_COOP`; self-join succeeded at 14:37:17/18 and the client
reached Awaiting Server. Evidence `06-AC_COOP-selected.png` and
`07-awaiting-server.png` records the selection and result. The three corrected
UI checks are still pending. `owned-processes.json` and
`owned-mp-helpers.json` retain precise .NET process timestamps for cleanup.

Automated `Alt_L+grave` again did not visibly open the campaign console.
The explicit Computer Use launch of the existing `osk.exe` created process
5996 at `2026-09-26T14:38:03.9265148+03:00` (window 1313310). Its accessibility
state reported higher integrity than the helper. Normal screenshot-backed
Alt and close-button clicks returned but produced no visible effect. No
security/integrity change or alternate input injection was used. A single
user request asks for keyboard close, literal command entry, console close
and Return to the Game. Until that happens, this run proves connection, not
mission startup or corrected-UI behavior. OSK is test-owned and still requires
close/identity verification; a non-elevated process query could not read its
executable path.

A later exact two-PID metadata check retained additional shader-helper child
13740 (parent 23280, PowerShell path and precise start time in
`owned-extra-helper.json`). Allow it and its parent to finish after MP exit.
Even the elevated read could not obtain OSK 5996's executable path; do not
invent a path-based cleanup proof or use a name-wide stop for it.

### Next dialogue visual investigation (read-only)

Exact installed `SandBox.GauntletUI.dll`/`SandBox.View.dll` inspection separates
the native outer `MapConversation` movie/tableau from its nested
`SPConversation` dialog controller. `GauntletMapConversationView` builds
`MapConversationTableauData`; the texture provider delegates to
`MapConversationTableau`. The latter directly reaches MapScreen conversation
mission and Campaign.Current animation state (SetData, OnTick and animation
methods), so instantiating it unchanged in multiplayer is unsafe. Hideout
uses already existing mission agents and a conversation camera, a different
visual path. No fake Campaign/Character/Party objects or blanket null guards
have been introduced to force the map tableau into multiplayer.

Generated local decompilation evidence is under
`artifacts/native/map-conversation-tableau-20260926` and
`artifacts/native/map-conversation-provider-20260926`. A future renderer should
consume bounded already-resolved host-visible visual data (scene/atmosphere,
equipment/body/frame/colors/animation and camera), with separate lifetime and
native-scene verification. This investigation is not implementation evidence.

Further native inspection found an existing read-only capture entry point:
`MapConversationView.ConversationMission.ConversationTableau` exposes the
host's already-created tableau. Its private `_agentVisuals`, `_cachedAtmosphereName`
and `_continuousRenderCamera` hold the resolved actors, environment and camera
after `_initialized`. The existing publisher already uses
`AgentVisuals.GetCopyAgentVisualsData` for map parties. A future conversation
capture can project those real resolved visuals rather than independently
guessing random guards/equipment; native-sensitive read/lifetime safety and
animation synchronization still require explicit implementation and checks.
No tableau capture or renderer has been installed or implemented in this run.

## Independent party nameplate color correction (source candidate)

Exact installed `SandBox.ViewModelCollection.Nameplate.PartyNameplateVM`
inspection confirms that party text colors are relationship indicators, not
the faction banner/cloth color. Native precedence is main party or co-attached
army member, war, same non-eliminated faction, alliance, then neutral; each
has a separate army-leader palette. `SettlementNameplateVM` instead uses the
faction color. This distinction explains the baseline neutral purple `80` and
gray looter `22`. Generated evidence is local under
`artifacts/native/map-party-nameplate-20260926` and
`artifacts/native/map-settlement-nameplate-20260926`.

The new `CoopCampaignMapPartyLabelPolicy` pins the ten inspected native color
values and precedence. The campaign publisher evaluates the real host's
diplomacy/army membership after visibility admission. `NameplateColor` travels
independently through visible state, catalog clone/delta, both bridge codecs,
binary catalog, legacy visible-entity wire message, replica merge and party
label VM. Banner/cloth colors and settlement rendering are unchanged. No
client campaign objects, authority mutation, spawn changes or hot-path
diagnostics were introduced. Cross-scenario UI search found the wrong party
color binding only in the campaign-map overlay; battle/hideout logic is not
changed.

Wire protocol is now 17, host bridge schema 13, binary catalog schema 3.
Fresh deployments require the coherent host/client/dedicated candidate;
do not replace files under the currently running schema-12 `cmr-b3` roles.
Tests cover all 32 boolean combinations of palette/precedence, independent
cloning and diplomacy-only catalog delta, both bridge round trips, missing
color rejection, binary round trip/old-schema rejection and client/wire
source bindings. The focused project passed. `cmr-b4` passed both CompileOnly
builds and unchanged installed inventories:

- Client `47BC6705E3A4A5507ABBF3C713532C2E5D2B9979AD832316B9D460BA4C9D2416`.
- Dedicated `8998A6E96442B1A21B58C8278EA119712B220B442BD855191FFB3D53B0B3AAEC`.

`cmr-c3` passed all 24 canonical contract projects at 14:46:41 local time.
No color candidate has been staged or live-verified. Native
hidden-count/healthy-count/army-total formatting,
nameplate status flags and the main-party-specific template remain separate
parity work; the color fix does not claim to solve those.

## Host-hidden party numerical information (subsequent source candidate)

The next inspection found a direct objective violation: native
`PartyNameplateVM.RefreshDynamicProperties` displays `?` when `Party.IsInfoHidden`,
whereas the replica published `NumberOfAllMembers` and displayed it literally.
The new `IsPartyInformationHidden` bit is captured only for admitted visible
mobile parties, redacts `PartySize` to zero on the host, propagates through
visible/dynamic snapshots, both text bridge paths, legacy entity and dynamic
batch wire messages, clone/delta, replica merge and the two client displays.
The dynamic projection also suppresses army numerical details for such a party
(party count, total size and cohesion). Contract admission rejects nonzero
hidden numerical payloads rather than allowing a client-only cosmetic mask.
Settlement states cannot carry the party-hidden bit. Known counts remain
unchanged; hide/reveal transitions are detected even if the numeric value is zero.

Cross-scenario scope: no existing map publisher/UI path checked `IsInfoHidden`.
Only the campaign-map projection is changed. Full battle snapshots, troop
materialization, battle-type adapters and authority are untouched.

This is a targeted numerical-redaction correction, not a claim of complete
host-visible information parity. Native army tooltip authorization, retained
catalog information and exact healthy/wounded/prisoner formatting still need
separate verification. The current ordinary count remains total-members;
native label healthy-count/attached-army aggregation was inspected in
`artifacts/native/map-ui-helper-20260926` but is not yet implemented.

Current source now requires wire protocol **18**, host bridge schema **14**;
binary catalog remains **3**. This supersedes the earlier `cmr-b4` source
candidate but does not mutate installed/running `cmr-b3`. Tests cover bounded
redaction, visible/dynamic clone and codec round trips, nonzero-hidden rejection,
missing-bit rejection, both hide/reveal deltas, source wiring and client `?`
display. Focused contracts passed. `cmr-b5` passed both CompileOnly products
with installed inventories unchanged:

- Client `7C87395646DC89CCC1F845AF8E3A3C47FD5B5E317E3AA35FD5ECD6338395CB57`.
- Dedicated `5176FD9C81F0806E8D42FCAC9C245220D5D8ABC639061F411C168E36FB54A1A0`.

Full `cmr-c4` passed all 24 canonical projects at 14:55:06; no deployment or native test of this
candidate is claimed. The original fresh UI run still awaits the literal
console command from the user; no equivalent post-parser command was substituted.

## Shared replica merge correction (subsequent source audit)

The end-to-end source audit found that the earlier color/redaction descriptions
overstated merge coverage. `cmr-b4`/`cmr-b5` compiled and their recorded tests
passed, but the network controller's full merge omitted `NameplateColor` and
`IsPartyInformationHidden`. Conversely, the mission view's incremental merge
omitted quest markers, settlement events and the production icon. The earlier
source-string tests checked individual copies rather than both consumers.
Neither candidate was installed; this is a source finding, not a new live failure.

`CoopCampaignMapReplicaStateProjection.Merge` now supplies both consumers from
one pure implementation. It preserves all visible-entity fields, deep-clones
each visual/equipment descriptor and rejects null, empty/mismatched identities
or non-visible dynamic input. Transport admission remains upstream; no native
reads, per-tick diagnostics or new authority are introduced. Field, village,
siege and hideout adapters do not consume this map-only merge and are unchanged.
Wire 18/bridge 14/catalog 3 remain unchanged from the preceding candidate.

Executable tests compare every projected property and every visual property
against its declared source, verify equipment-array independence, marker removal,
color update, hidden/revealed zero counts, visibility removal and identity guards.
Consumer source checks require both full and incremental paths to use the tested
projection. Focused contracts passed; aggregate `cmr-c5` passed 23/24, including
the map suite. The separate runner-contract suite refused its worker cleanup
because exact PID/path/start-time matching failed; the underlying mismatch is
not established, and no identity guard was relaxed. A read-only process check
found no remaining worker from the failed test (excluding the inspection shell).
The sequential `cmr-c6` retry passed all 24 projects at 15:11:00 local without
changing source or guards. This does not establish why the earlier worker
identity differed. Isolated `cmr-b6` built both roles with installed inventories unchanged:

- Client `85A2274C45ECEC34ACE94AA5E56C217180D421626C1CC5FF89FBCC37F8F722AC`.
- Dedicated `70823B6680ADC04B9059D986DE88EC9B0418DC465E0A284DA25339F69D0B3E8A`.

No new files were deployed to the live `cmr-b3` roles.

At 15:04 local, Computer Use recovered campaign PID 1436's pause menu: no console
was open. OSK remained listed as a separate window. The fresh run still has no
confirmed literal map-start command or mission result; this observation is not
evidence that the new source candidate has run in Bannerlord.

The fresh multiplayer PID 21920 was separately activated and still displayed
`Awaiting Server`; screenshot `evidence/08-awaiting-server-after-source-audit.png`
is retained in `artifacts/runtime/campaign-map-ui1-20260926`. Campaign 1436,
multiplayer 21920, dedicated 18552 and pre-existing Steam 2740 remain running.
This source-validation stage did not claim or perform live-run cleanup.

## Native healthy/wounded/prisoner nameplate text (subsequent source candidate)

The installed `PartyNameplateVM.RefreshDynamicProperties` and `SandBoxUIHelper`
show that native `Count` is the healthy count, with only attached parties added
for an army leader. Wounded/prisoner suffixes use native localized TextObjects;
zero suffixes are empty, and each nonzero hidden suffix becomes `?`. The native
`PartyNameplateItem.xml` binds `Count` and `ExtraInfoText` separately.

The publisher now calls the installed public `SandBoxUIHelper` methods after
host-visible party admission and formats the hidden/zero composition through
the pure `CoopCampaignMapPartyLabelPolicy`. It sends `PartyNameplateCountText`
and `PartyNameplateExtraInfoText` through visible/dynamic clones, both bridge
formats, both wire paths, dynamic comparison and the shared merge. The client
binds those already host-localized strings; it does not recompute campaign
rosters or translate the host's text. The separate `PartySize` detail field
remains total members and retains its existing hidden-number redaction.

The client/campaign project references installed `SandBox.ViewModelCollection`
with `Private=false` in both reference profiles. No native VM is instantiated,
no campaign object is created in multiplayer, and the dedicated project does
not acquire this dependency. Battle adapters and battle snapshots are unchanged.

Source protocol is now **19**, bridge schema **15**, binary catalog **3**.
Count strings are bounded to ten ASCII digits; hidden count is empty or `?`.
Extra text is limited to 128 UTF-8 bytes with no controls; hidden extra text
is limited to empty, `?` or `??`, matching native presence-only disclosure.
Out-of-bound localized text fails admission rather than silently being truncated.
Dynamic batches are reduced from eight to two entities to reserve room for
the new variable-length strings. For the historical 1,180-entity snapshot this
means 590 batches rather than 148; initial transfer cost and native packet-writer
behavior require live measurement. The source limit is not native packet proof.

Focused contracts passed: all hidden/zero suffix combinations, host-localized
round trips, missing-field rejection, UTF-8/control/count limits, clone/merge
fidelity, count-only and suffix-removal deltas, native helper wiring and both
wire directions. `cmr-b7` compiled both roles with installed inventories unchanged:

- Client `C4E97A5D284077F90191B99D6442528DA5769681CFF63EA435C0E050FE309B0E`.
- Dedicated `32ACD15CABB7A3CABBA9F51E0621DF8D1F42B7D29364085FF2FE870998C211EB`.

Full `cmr-c7` passed all 24 projects at 15:21:06 local. The candidate is not installed/live-verified;
the live roles remain `cmr-b3`. Army/status/name/speed presentation, native
tooltip visibility and the dialogue tableau are still incomplete. This change
does not establish full map or dialogue visual parity.

A bounded Computer Use retry using explicit `Alt_L+Shift_L+grave` left the
campaign pause menu unchanged; the console did not open. No command was entered,
no security setting changed, and no native-start success is inferred from this
input attempt. The pending user-assisted literal-command gate remains open.

## Native party caption, movement speed and presentation states

The installed `PartyNameplateVM` uses army name for the leader/attached group,
otherwise leader name (or party name when leaderless); speed is host-formatted
`F1`. It sets army-leader, attached-army, settlement, disorganization and sea
flags, and suppresses individual labels for attached parties, settlement parties
and non-main raft parties. The replica previously hard-coded these properties
to false and its speed to empty. The native prefab consumes them directly.

Current source captures the native caption separately from detail `DisplayName`
as catalog `PartyNameplateNameText` (bounded to 256 characters), plus dynamic
`PartyMovementSpeedText` (32 UTF-8 bytes) and six-bit `PartyNameplateFlags`.
The shared merge carries all three into the overlay, whose property notifications
now update the native widget flags. The client applies the native label-only
suppression separately from entity/model visibility. No hidden party is newly
admitted and no campaign action is granted to clients. Field/village/siege/hideout
adapters do not use this projection; no battle snapshot/spawn logic is changed.

Wire **20**, bridge **16**, binary catalog **4** supersede `cmr-b7`. Count/extra
text bounds and the conservative two-entity dynamic batches remain. All roles
need the matching candidate; none of these source changes has been installed.
Focused tests passed all 64 flag combinations, main/non-main visibility decisions,
caption precedence, independent caption/speed/flag deltas, text and binary
round trips, missing-field and bound rejection, clone/shared-merge field fidelity
and client/wire binding checks. `cmr-b8` passed both isolated builds and unchanged
installed inventories:

- Client `5789AE7ACDDABCF64DB0BAFA251F8848332D6A62EC04241B65EF90771384AFDF`.
- Dedicated `BA5AB2237B6EF0F3590E915A806E07A6E27E7BBA4672B54FBC9A5B8A2473038A`.

Aggregate `cmr-c8` passed all 24 projects. Final call-site review then found that
the overlay's boolean position-update result was also used to gate 3D models.
The final source correction keeps this return value as model projection eligibility
while applying status-specific suppression only to the label property. Focused
contracts passed afterward. Final `cmr-b9` compiled both roles with installed
inventories unchanged and no product process launched:

- Client `86AF5D4661CF1E52597CC51942208AA28E46B18DA9C248A20532C93FC04BBA5F`.
- Dedicated `BA5AB2237B6EF0F3590E915A806E07A6E27E7BBA4672B54FBC9A5B8A2473038A`.

Final `cmr-c9 -All` passed all 24 contract projects, zero failures, completed
at 2026-09-26T12:37:26.7794342Z (15:37:26 local). Authoritative results are
`cmr-b9/artifacts/results/compile-only.json` and
`cmr-c9/artifacts/results/contracts.json` under the automation temporary root.
The final candidate is build/test-verified, not installed or runtime-verified.
Post-verification Computer Use observations still show campaign PID 1436 paused
with no console, multiplayer PID 21920 on `Awaiting Server`, and OSK listed as
a separate window. Evidence `09-campaign-paused-after-final-contracts.jpg` and
`10-multiplayer-awaiting-after-final-contracts.jpg` is retained in the fresh
UI run's evidence directory. No input command, installation or cleanup occurred
during this final observation; the pending literal-command gate remains open.
No native icon/name/speed/performance result is
claimed. The exact native `PartyPlayerNameplateVM`/`PartyPlayerNameplateItem.xml`
inspection confirmed a remaining separate main-party template: portrait,
prisoner indication and a camera-navigation arrow with a 110-world-unit distance
threshold. The replica still substitutes an ordinary party template. Generated
inspection evidence is under `artifacts/native/map-player-nameplate-20260926`.
This is the next visual gap, not a completed main-party implementation.

### Documentation impact

Reviewed the map sections of README, CODE_MAP, INVARIANTS_AND_RISKS, ARCHITECTURE,
RUNTIME_FLOWS and BUILD_TEST_DEBUG. This report owns candidate hashes and evidence;
README/CODE_MAP/RUNTIME_FLOWS/INVARIANTS_AND_RISKS summarize the changed protocol
and rendering boundary. ARCHITECTURE and BUILD_TEST_DEBUG retain the preceding
unchanged host-only helper dependency/compile-only policy. Historical reports,
release packaging and battle-automation specifications are intentionally unchanged.

## Native label geometry and coherent candidate restart

Installed `PartyNameplateVM.RefreshPosition` computes `DistanceToCamera` with
`Vec3.Distance(camera.Position)` and projects a head anchor 0.8 world units above
the party. The replica instead bound projected depth as distance and multiplied
the head offset by map-bound-derived marker scale. The party overlay now uses
spatial distance; the mission view uses a fixed `PartyNameplateHeadHeight=0.8f`.
Settlement labels already use spatial distance and are unchanged. Field/village/
siege/hideout adapters do not consume this map overlay. The protocol stays
20/16/4. Source-wiring tests guard both call sites; they do not constitute an
in-engine rendered-size/position comparison. Event-position and main-player
template parity remain separate incomplete work.

`cmr-b10` built both roles without installed mutation; `cmr-c10 -All` passed all
24 projects at 2026-09-26T12:45:49.0672377Z:

- Client `8CF2126858913B7409E486A393F58BBBF48BD6981739272551D6A9C5EE7C12FF`.
- Dedicated `BA5AB2237B6EF0F3590E915A806E07A6E27E7BBA4672B54FBC9A5B8A2473038A`.

The previous `campaign-map-ui1-20260926` run was ended before installing the new
candidate. Both game roles closed via observed titlebar buttons, shader helpers
exited naturally, and remaining exact-owned server/shell processes were stopped.
The first cleanup invocation refused a nested JSON ownership array before any
mutation; direct array assignment fixed the PowerShell 5.1 enumeration issue.
The final `cleanup.json` records all 64 preimages matching, no new config/save
files, no remaining exact-owned listed roles/helpers, and Steam PID 2740 kept.
PID 3208 no longer matched the old identity and was preserved. OSK PID 5996 is
still outside completed cleanup: executable identity is unavailable, so it was
not force-stopped. Do not claim every test helper has been removed.

New run root: `artifacts/runtime/campaign-map-candidate1-20260926`.
`Prepare-LiveRun.ps1` installed five exact files from `cmr-b10` under canonical
installation locks, verified hashes and retained module preimages plus 64
configuration/save preimages. Campaign PID 33364 started at
2026-09-26T15:47:02.6024755+03:00. The candidate is now installed, but mission
startup and visual behavior are not yet verified.

### Coherent candidate connection and current input gate

The campaign's observed server menu launched dedicated PID 49132 at
2026-09-26T15:49:42.0905227+03:00. The exact desktop shortcut launched multiplayer
PID 35052 at 15:50:11.8734402+03:00; Safe Mode was answered No. Native login and
AC_COOP selection succeeded, with self-join recorded at 15:54:36. The client
shows `Awaiting Server`, not a running map mission. Screenshots 01 through 08
in the candidate run's `evidence` directory retain these stages.

`loaded-identity.json` verifies installed SHA-256 against build outputs and
loaded MVIDs: campaign/multiplayer `b384afe6-e633-4547-9b33-b09c1cd8c4ac`,
dedicated `ed81d7db-45ab-42bb-8daa-f41d74233aef`. This is not an in-memory hash.
The evidence script explicitly converts matched log lines to plain strings
before JSON serialization to exclude PowerShell provider metadata.

A fresh Computer Use observation confirmed the campaign pause menu with the
console closed. `Alt_L+grave` did not open it. The existing OSK was minimized;
after activation, the helper explicitly reported higher Windows integrity and
limited accessibility. Observed close-button clicks did not remove its window.
No integrity/security setting was changed and no alternate input injection was
used. User assistance was requested to close OSK and execute the literal
`coop.map_prototype_start` in this new campaign. Earlier manual execution does
not count for these new processes. Product roles are deliberately left running
for that step; current-run cleanup/restoration is not yet complete.

### Coherent candidate partial live pass and cleanup

The preceding input/cleanup gate is now superseded. The user executed the
literal command in the fresh campaign at approximately 16:01:44 local and
later closed OSK and the campaign console. Screenshot 09 records the command
and its acknowledged mission-start request. Dedicated opened the isolated
mission at 16:01:44; client Main_map Scene.Read completed at 16:01:50.083 and
render readiness followed at 16:01:50.810. The first client state arrived at
16:01:51.940. Initial catalog: 397 entities, 101,757 logical bytes, 11,802 wire
bytes, 47 chunks. This is a successful transfer for this snapshot, not a
worst-case packet/performance bound or a 1,180-entity measurement.

Screenshots 10-15 retain mission rendering, Vostrum selection with the corrected
top-right detail panel, client zoom, host movement and the corresponding later
replica positions/lighting. The party with turquoise count 30 is the main party;
Tristania's nearby white count 80 is not the player. Client camera remained at
its locally selected zoom/offset; no automatic return-to-host-camera claim.

Screenshots 16-22 retain native Tristania dialogue and its replica: open with
matching text/three replies, a client reply click that did not advance the host,
host reply selection and synchronized later text, then host farewell/close and
both maps restored. Map labels/detail panel hide during dialogue and return
afterward. No obsolete persuasion panel was observed. Client still displays
the native click-to-continue hint; that hint's interaction was not tested.
The replica lacks the host's 3D speaker/guards tableau and speaker banner, so
dialogue visual 1:1 is NOT satisfied. Other observed map differences include
an extra white ring at Vostrum, black-versus-light party banner appearance,
missing native settlement book/eye indicators and incomplete main-party UI.

`Capture-MapEvidence.ps1` retains selected existing map log lines and exact
process identity/responding checks in `mission-initial.json`, `movement.json`
and `dialogue-complete.json`. At 16:09:13.686 the client had processed 2,163
state messages through revision 2176 (entities/dynamic 38, catalog 2); all three
roles responded and selected map error counts were zero. No crash was observed
during the approximately eight-minute mission. These observations are NOT a
complete crash audit, network-load benchmark, privacy audit, multi-remote-client
test or battle regression pass. Raw retained logs are private evidence and may
contain session/network details; do not print or publish them indiscriminately.

Both game windows closed via their observed titlebar buttons without saving
the in-memory campaign actions. Shader helpers exited naturally. The exact
identity-guarded `Complete-LiveRun.ps1` then removed only remaining owned server
and shortcut-shell processes. `cleanup.json` at 2026-09-26T13:12:58.1731496Z
records all 64 config/save preimages matching, no new config/save files, no
remaining exact-owned processes and Steam PID 2740 preserved. No file needed
restoration. OSK PID 5996 was absent after the user closed it. Five candidate
module files remain installed intentionally; their preimages are retained.

Current acceptance: startup, selection/zoom, movement and read-only dialogue
lifecycle are partially runtime-verified; full host-visible map/dialogue visual
parity remains incomplete. Next implementation investigation is the isolated
campaignless 3D conversation tableau, not another claim of full completion.

### Next implementation boundary: campaignless dialogue tableau (planned)

Low-level inspection of the installed `MapConversationTableau`,
`DefaultMapConversationDataProvider` and `ThumbnailCacheManager` identifies
the native boundary. The native tableau obtains a shared cached scene named
`scn_conversation_tableau`, selects an atmosphere/tag group, creates the actual
speaker and up to two guards, and configures the tagged infantry camera.
`SetData`/`OnTick` call MapScreen/ConversationMission and the data provider
consumes campaign conversation/settlement state. Do NOT instantiate this native
class in multiplayer or manufacture Campaign.Current to satisfy it.
The native render callback also contains an unbounded CheckResources loop;
do NOT copy that wait into the replica.

The intended narrow implementation is:

1. On the campaign host, capture only the already-visible initialized tableau:
   exact rendered actor body/equipment/cosmetic items, colors/banner, transform,
   camera, atmosphere/weather and supported presentation actions. Prefer the
   existing native AgentVisuals data over rebuilding a hidden party roster.
   Read-only native object capture remains runtime-sensitive; guard readiness,
   lifetime and absence explicitly, avoid diagnostic scans and repeated reflection.
2. Define a bounded detached descriptor with clone/validation/equality and
   explicit visual identity. Transport it with the matching dialogue revision,
   using bounded chunks and pacing rather than inflating the existing text
   header. Validate total sizes/counts, actor limits, frames, equipment IDs,
   duplicate/mixed/stale revisions and full lifecycle clearing before rendering.
3. In the map-only client view, own a separate scene and agent-renderer controller,
   not ThumbnailCacheManager's global cache. Load only the known native scene;
   allow no remote arbitrary scene paths. Reconstruct visual-only actors from
   the accepted descriptor, not physical mission agents. Use bounded readiness
   over ticks, not blocking resource loops. Keep SPConversation read-only.
4. Preserve the scene across text-only revisions; replace it only when the
   visual identity changes. Release exact owned visuals/camera/controller/layer
   on replacement, close, invalidation and mission shutdown, and restore map
   focus. Fail safely to the existing text-only dialogue if visuals are unavailable.
5. Add pure round-trip/revision/lifecycle tests, both isolated builds and the
   complete contract inventory, then run fresh coherent host/server/client
   verification for dialogue opening, update, close and reopen. Rendered parity
   (speaker/guards/equipment/camera/weather), input inactivity, no map-layer leaks
   and bounded cleanup require live proof; compile success is insufficient.

Likely source owners: `Campaign/CoopCampaignMapConversationCapture.cs`, a new
bounded infrastructure descriptor/codec, bridge codec and conversation network
messages/controller, `UI/CoopCampaignMapConversationView.cs`, a new map-only
visual owner, and the map contract tests. Wire/bridge versions must advance if
the descriptor is integrated. No source implementation for this tableau was
added during this investigation; `cmr-b10` remains the installed live-tested code.

Adjacent-scenario review: `CoopHideoutBossCinematicView` uses existing mission
agents/camera and its own conversation view model, so this scene-owner addition
must remain map-only. Field/village/siege paths must neither load the tableau nor
acquire a second physical spawner. Do not alter the shared campaignless getter
safety patch or battle adapters for this visual feature.

Documentation impact: this report owns the native investigation and plan;
README, CODE_MAP, RUNTIME_FLOWS and INVARIANTS_AND_RISKS summarize current live
evidence. ARCHITECTURE must be updated when the new scene ownership/descriptor
actually exists; BUILD_TEST_DEBUG needs no change while build/deploy commands
are unchanged. Native inspection output is retained under
`artifacts/native/map-conversation-{tableau,provider,cache}-20260926`.

## Detached dialogue tableau foundation (source implementation)

The prior plan now has its first implemented layer, not a live 3D scene:

- `CoopCampaignMapConversationTableauState.cs` holds a detached scene descriptor,
  actor descriptors and deep clones. It carries the host camera frame/base FOV,
  atmosphere/weather and one speaker plus up to two visible guards, including
  body/equipment/cosmetic item IDs, colors/banner, frame/scale and initial action
  set/action name. It has no campaign object, scene path or client command.
- The binary codec uses schema 1, strict UTF-8, pre-allocation string limits,
  canonical booleans, fixed frame vectors, exact end-of-stream and a 64-KiB cap.
  Validation rejects non-finite/scaled/skewed/reflected coordinate frames,
  invalid actor/equipment counts, path-like identifiers and inconsistent banners.
  Scale is transported separately. Semantic installed-resource resolution and
  native body/banner parsing still belong to the future renderer.
- `CoopCampaignMapConversationTableauAssembler` uses the existing integrity-checked
  chunk codec but applies the smaller visual payload limits before allocating
  or decompressing. It rejects mixed metadata, stale revisions and conflicting
  duplicates; authoritative close and mission reset have explicit barriers.
  This is pure assembly logic, NOT an integrated native packet transport.
- `CoopCampaignMapConversationTableauCapture` reads cached exact native field
  metadata only from a host campaign's active initialized/enabled tableau. It
  detaches actual AgentVisuals data and rechecks view/tableau ownership. It does
  not create native scenes, tick visuals, enumerate a party roster or select a
  reply. It is compiled but NOT called by the publisher yet. Its native reads
  require live verification once integrated; exception handling is not a
  guarantee against native access violations.

Initial action identity does not synchronize ongoing face animations, reaction
timing, voice/audio or dialogue HUD metadata. These remain explicit parity gaps,
along with bridge/wire integration and the client-owned renderer. No source
wire/bridge/catalog version was changed: live protocol remains 20/16/4 and the
installed candidate is still `cmr-b10`. No game launched or module deployed.
Hideout cinematic and field/village/siege adapters are unchanged.

Validation: initial `cmt-b1`/`cmt-c1` stopped before compilation because the sandbox
could not read existing NuGet.Config; no source-failure inference from these runs.
With appropriate access, `cmt-b2` built both roles. `cmt-c2` passed 23 projects
and found a malformed-string-length exception escaping the new decoder; explicit
InvalidDataException handling fixed it. A first standalone focused invocation
omitted COOPSPECTATOR_REPOSITORY_ROOT and failed only source-root discovery;
`cmt-f2` then passed the full map contract executable, including 2,742 tableau
assertions. Tests cover reflected public-property clone/round-trip completeness,
all byte truncation boundaries, strict text/boolean limits, malformed fields,
1,000 deterministic byte mutations, chunk reassembly, supersession, close/reset,
mixed metadata and integrity rejection. This is not native packet/render proof.

Final frozen-source `cmt-b3` built both roles with installed inventories unchanged
and no product process launched:

- Client `FE5A9D778DD6EB04150ADCF4D57FB9F256E9895CC7E2F0A3AFC9A8095B3509F6`.
- Dedicated `8CA2499526C0303BA09C7FF00431F7506665AA6569C573142DCDC1128B2C3977`.

Aggregate `cmt-c3 -All` completed successfully: all 24 contract projects passed,
including the map suite's 2,742 tableau assertions. Next implementation is
the descriptor's bounded revision-bound wire integration and isolated client
scene lifecycle, followed by a fresh coherent native test. Do not deploy this
foundation alone as though it supplied visible 3D dialogue.

Documentation impact: this report owns the implementation/verification evidence;
CODE_MAP lists new owners, ARCHITECTURE and INVARIANTS_AND_RISKS record the unwired
authority/lifetime boundary. RUNTIME_FLOWS remains unchanged because production
publication/messages/rendering did not change. BUILD_TEST_DEBUG remains unchanged
because the canonical isolated build/test commands and deployment policy did not
change. README distinguishes installed live-tested code from this source foundation.

## Atomic dialogue and tableau transport (source integration)

The descriptor is now wired from `CoopCampaignMapConversationCapture` through
the local bridge to the dedicated process and complete client conversation
state. Optional visual-capture failure retains the valid native text. Scene
rendering is still absent: do not confuse received descriptors with visible
3D characters. Wire **21**, bridge **17**, catalog binary **4** require a coherent
restart of all three roles. Installed/live-tested `cmr-b10` remains **20/16/4**.

The previous header containing the whole dialog line and separate option wire
messages were replaced by `CoopCampaignMapConversationChunkMessage`. One logical
payload contains revision, active/text/options and the optional 64-KiB tableau.
`CoopCampaignMapConversationPayloadCodec` caps that complete payload at 1 MiB,
validates strict text/boolean/count/length bounds and rejects trailing bytes.
Native messages carry at most 256 binary payload bytes plus bounded metadata;
the payload hash, compressed and logical sizes are checked before decoding.
Full text and visuals are applied atomically only after matching embedded and
envelope revision validation. No client-to-server choice action was introduced.

The dedicated prepares/compresses once per dialogue revision and queues at most
two chunks per peer per tick. Peer reference plus connection/synchronization
checks prevent a reused peer index inheriting another peer's send. New revisions
replace pending queues; disconnect, host-session replacement and mission
finalization clear owned state. Late join gets the current payload. An unavailable
fresh host bridge closes active dialogue at a newer network revision. Encoding
failure projects inactive rather than silently retaining an older active state.
Per-peer send exceptions terminate that send and log once; delivery/reconnect
under native error conditions is not yet runtime-verified.

Installed GameNetwork decompilation confirms that the selected
BeginModuleEventAsServer/EndModuleEventAsServer pair invokes native peer events
with `isReliable: true`. The inspection is retained under
`artifacts/native/conversation-network-20260926`. This supports channel selection,
not native packet capacity, timing, throughput or reconnect proof. The pure tests
exercise a >400-KiB maximum-length localized dialogue, atomic tableau/text round
trip, malformed lengths/truncations, revision mismatch, close supersession,
bridge required-field/corruption rejection and source wiring/pacing/lifetime.

`cmt-f3` passed the focused suite (2,742 descriptor + 903 payload assertions).
`cmt-b4` compiled both roles with installed inventories unchanged:

- Client `C000853D73F2CC6A7A4F6B3115E15BB66EA06BABCD3DF874524FF6CD478777C2`.
- Dedicated `AE45FDB362B48B085618C8E4F8760AE238EECF2A5C86E1711A2EF5CF1764C036`.

`cmt-c4 -All` passed all 24 projects. `cmt-f4` repeated the focused suite after
updating only the protocol assertion's explanatory message; production source
is unchanged from `cmt-b4`. No game launched and no deployment occurred.

The next implementation gate is an independent client scene owner consuming the
accepted descriptor and preserving it across text-only updates, with bounded
resource readiness and cleanup on close/replacement/mission end. Initial action
identity is not continuous animation, face, audio or HUD parity. Follow the
prior native-scene investigation; do not instantiate MapConversationTableau in
multiplayer or use its shared campaign cache. Hideout and other battle adapters
remain untouched. README/CODE_MAP/ARCHITECTURE/RUNTIME_FLOWS/INVARIANTS_AND_RISKS
now describe this source boundary; build/deploy policy remains unchanged.

## Independent client dialogue scene (source implementation)

`UI/CoopCampaignMapConversationTableauView.cs` now consumes the accepted descriptor
as a map-dialogue-owned visual scene. It resolves only the known locally owned
`scn_conversation_tableau` asset, reconstructs visual-only actors from the host's
body/equipment/cosmetic IDs, banner/colors, action set/initial action, frame/scale,
and reproduces host camera/atmosphere/weather. Equipment resolution reuses the
existing party visual resolver (visibility changed private to internal; resolver
behavior unchanged). No physical Agent, mission troop roster, Campaign.Current
or native MapConversationTableau/cache instance is constructed on the client.

Installed SceneLayer/ScreenLayer/ScreenBase inspection establishes that scene
layers use local order -100 with creation-ID tie-breaking, below the existing
dialogue Gauntlet layer; removal invokes finalization. The new passive layer
does not accept focus/input and has automatic scene deletion disabled because
the dedicated owner detaches rendering, resets visuals, releases camera and
renderer controller, removes its layer, then clears its own scene. Cleanup
continues across individual managed exceptions and emits one failed-operation
count if any occur; this is not a guarantee against native access violations.
WorldMap virtual texture tiles are loaded as in the native initializer and are
not globally unloaded while the map scene still uses them.

The native asset's only scripts are camera_instance and CharacterSpawner. The
installed CharacterSpawner creates previews from OnEditorTick, not runtime
OnInit; runtime initialization only sets colors. Outdoor conversation background
names (e.g. conv_plains_noon_0) exist as atmosphere resources, not scene entity
tags. The initial candidate incorrectly required a matching tag group; asset
inspection corrected that before deployment. Optional interior tag groups are
shown as native does. The standard infantry camera script supplies native lens
defaults before the host frame/FOV projection is applied. Retained inspections:
`artifacts/native/conversation-{scene-layer,screen-layer,screen-base,character-spawner}-20260926`.

The pure `CoopCampaignMapConversationVisualLifetime` preserves a scene across
equal visual descriptors/text-only revisions, replaces it on visual changes,
clears it on invalidation/close, and suppresses same-state retry after failure.
Reopen after a close or a new visual descriptor may retry. Resource readiness
is polled across frames using a monotonic wall clock with a 15-second limit;
there is no ForceLoadResources call or unbounded resource loop. Native Scene.Read
and individual native calls remain synchronous and are not preempted by that
deadline. Missing resources or readiness failure leave the existing text dialogue.
The scene/visuals tick only after readiness. Close and mission-screen finalization
dispose the owner; existing map-layer/focus restoration remains in the text view.

`cmt-b5` found two compile-only API issues (ambiguous FaceGen type and the installed
ActionCode in-parameter versus decompiled ref syntax); both were corrected without
changing language version. `cmt-f5` passed focused contracts. `cmt-b6` and `cmt-c6`
passed both builds/all 24 projects for the intermediate candidate. Final `cmt-b7`
also passed both roles, installed inventories unchanged and no game launched:

- Client `787717C014A7E6B1154BD98C75F6C1C8B5BA711E4F48D04F6F05049F854BD7E7`.
- Dedicated `AE45FDB362B48B085618C8E4F8760AE238EECF2A5C86E1711A2EF5CF1764C036`.

Final aggregate `cmt-c7 -All` passed all 24 projects. Its focused suite
includes 2,742 descriptor, 903 payload and 24 visual-lifetime/source-wiring assertions.
These checks do not execute a native renderer or prove cleanup/memory/frame-rate
behavior. The next gate is coherent staging of `cmt-b7` (protocol 21/bridge 17)
and the prescribed campaign-menu/server/desktop-shortcut/literal-command run,
checking first dialogue, text update without scene recreation, close, reopen,
camera/weather/equipment parity and return to map. Do not use the earlier
`cmr-b10` runtime evidence as proof of this renderer. `cmr-b10` is still installed.

Continuous reaction/face/voice synchronization and remaining native dialogue HUD
metadata are not implemented by the initial-action descriptor. Main-party/map
marker gaps also remain. The full goal is not complete. Hideout, field, village
and siege code paths are untouched; their native regression is not claimed.

Documentation impact: CODE_MAP lists the scene owner/lifetime policy; ARCHITECTURE
and RUNTIME_FLOWS describe its ownership and open/update/close flow; risk guidance
records native-call/readiness/cleanup boundaries. README names the source candidate
separately from installed/runtime evidence. Build/deploy procedures are unchanged.

## Dialogue candidate native run started (2026-09-26)

Run root: `artifacts/runtime/campaign-dialogue-candidate1-20260926`.
The hash-checked cmt-b7 candidate is now installed in the five exact module
targets under the canonical installation locks. All 64 configuration/save
preimages and five module preimages were retained before launch. This supersedes
the earlier statement that cmr-b10 remains installed; cmt-b7 is the current
installed candidate, not yet mission-verified.

Campaign PID 52136 (17:04:40 +03:00) loaded Continue Campaign. The campaign
Coop Dedicated Server / Start Server menu launched dedicated PID 33596
(17:07:24 +03:00). The exact user-prescribed desktop shortcut launched the
multiplayer chain at 17:07:48 and multiplayer PID 43308. Safe Mode No was
selected. Screenshots 01-04 retain menu, loaded campaign, server menu and
Safe Mode prompt. Exact path/start-time/parent ownership records are retained.
Steam PID 2740 is preserved. NVIDIA update children are not owned for cleanup.

`loaded-identity.json` confirms installed SHA-256 plus matching runtime MVID
in each role log: campaign/client faa61d48-c4b7-4682-b30a-a65baab7ca0f,
dedicated 13d22b8b-0a27-4e6c-b314-4e95ca2b1f1d. This is not an in-memory hash.
The user completed native Login. The agent selected AC_COOP in the custom-server
list and clicked Join; the client log records successful join at 17:16:51 and
the screen shows Awaiting Server. Evidence 05/06 retains selection and waiting.
The native intermission VM logged ArgumentNullException (key), caught by the
existing IntermissionVmCrashGuardPatch. This also occurred before the previous
successful mission and is not itself proof of a failed connection or map crash.
Source confirms startup issues start_game, not start_mission; waiting here is
consistent with the prescribed later campaign console command.

The agent closed the campaign server panel. Alt_L+grave did not open the console.
At 17:22:45 the agent launched OSK PID 49028 (window 200148); its Alt/key clicks
did not visibly open the console and its close-button click left it open. No
security or integrity bypass was attempted; no explicit protection error was
returned in this attempt. The user was asked to close OSK and enter the literal
command. OSK path was not exposed by Get-Process, so it is not admitted to the
automatic identity-checked termination list. No literal map command or new
dialogue-scene rendering is claimed yet.

Continuation revalidation: campaign 52136, dedicated 33596, client 43308 and
OSK 49028 still have the recorded start times and Responding=True. No map-start
event was present in the selected current campaign/client logs. A subsequent
accessibility-only OSK observation explicitly reports that the window has higher
Windows integrity than the Computer Use helper and exposes only window/title
elements. Stop OSK input attempts; do not bypass that protection. Await the
already-requested user console action, keeping the frozen cmt-b7 native candidate
separate from source-only cmt-b8. No restart, new build or installed mutation was
performed during this revalidation.

Login classification was checked against the installed multiplayer view-model
assembly using ILSpy: MPLobbyVM.Login is MPAuthenticationVM, and its
ExecuteLogin awaits `_lobbyState.TryLogin()`. Thus absence of a password field
does not establish that this is merely an acknowledgement button. Current
Computer Use guidance prohibits authentication-dialog automation; do not promise
automatic clicking on the basis of the label or prior successful clicks alone.
This inspection does not establish all downstream authentication mechanics.

The three product roles remain alive for continuation. `Complete-LiveRun.ps1`
is prepared with this run's exact IDs but NOT EXECUTED; first close both game
windows gracefully and allow shader cleanup helpers to finish. Then inspect
and run the cleanup to verify original config/save bytes and preserve Steam.

### Resolved actor scale correction, source-only while cmt-b7 runs

Installed AgentVisuals.Refresh sets its private `_scale` from native body
properties when ScaleData is exactly zero; GetScale returns that resolved field.
Native MapConversationTableau leaves ScaleData at zero for its actors. The first
capture incorrectly converted zero to 1, flattening body-derived height.
CoopCampaignMapConversationTableauCapture now captures actor.GetScale directly.
The existing positive bounded wire scale and client's explicit Scale application
remain unchanged. No new native read, reflection field, diagnostic or protocol
change is required. Source search found this fallback only in the new capture;
map party visuals use their deliberate miniature scale, and battle/hideout
adapters are untouched. Their runtime regression is not claimed.

`cmt-b8` compiled both roles, installed inventories unchanged:
client SHA-256 `628AAB73AE5A02F04BBE66C07E68E2DC1CF2FC03A15BB18FE9253068F594C51F`;
dedicated unchanged `AE45FDB362B48B085618C8E4F8760AE238EECF2A5C86E1711A2EF5CF1764C036`.
`cmt-c8` passed all 24 projects: 2,745 descriptor assertions, 903 payload,
25 visual lifetime/source wiring. Three non-unit scale round trips and one
resolved-capture/client-application source guard were added. These do not execute
native actor rendering. cmt-b8 is NOT deployed; cmt-b7 remains the live candidate.
Documentation impact is this canonical evidence entry and the README source note;
the architecture, wire format and public behavior contract did not change.

### cmt-b7 native dialogue failure and restoration

The user executed the literal campaign command at 17:31:58.495 on 2026-09-26.
The frozen cmt-b7 client and dedicated identities recorded above remained in use;
cmt-b8 was not deployed. Evidence root:
`artifacts/runtime/campaign-dialogue-candidate1-20260926`.

Observed results:

- 07-map-mission-running: actual multiplayer map, not merely an accepted command.
- The host moved into an encounter with friendly Tristania (80 troops).
- First host conversation opened at approximately 17:39:21; the client loaded
  `scn_conversation_tableau` at 17:39:21.947.
- Two client shader compiler dialogs named `pbr_terrain_gbuffer.rs` and
  `pbr_terrain.rs`, PIXEL_SHADER input layout 9. Both reported
  `terrain_pixel_functions.rsh(818,29-63)`, X3129 (warning treated as error) and
  X4008 (floating point division by zero). The agent chose Cancel, not Retry.
- The renderer released its scene and logged `readiness-timeout` at
  17:40:39.163. This elapsed interval includes blocking native shader dialogs;
  it is not proof of a 15-second asynchronous readiness-loop defect.
- Host/client text updates matched. Clicking the client's native-style Continue
  did not advance either dialogue; the host retained control. Host farewell and
  close restored the client map (08-13 evidence).
- Reopening the same conversation at 17:58:38 reproduced both shader dialogs.
  After Cancel, a second readiness timeout was logged at 17:59:56.413. The
  client retained the correct read-only text over its map. Evidence 14-16
  retains host reopen, shader error, and fallback. No successful 3D rendering
  or full dialogue 1:1 pass is claimed. Root cause remains under investigation.
- Installed shader source line 818 normalizes the accumulated terrain normal;
  the scene XML explicitly has terrain disabled. These facts alone do not
  identify which native resource/render initialization caused that permutation.

The two game windows were closed by their observed titlebar controls. Multiplayer
shader cleanup completed naturally. `Complete-LiveRun.ps1` then removed only
identity-matched owned support/server processes. Its first execution stopped at
an unexpected BannerlordConfig change after process cleanup; the config diff and
campaign log established a test-triggered autosave (`saveauto3`, 17:39:09.522).
The exact inspected postimages of BannerlordConfig and saveauto3 were retained
privately before verified preimages were restored. The engine_config safe-exit
change was restored by the first pass. OSK 49028 had been launched by the agent
and closed by the user; absence is verified, not attributed to agent cleanup.

Final `cleanup.json` at 18:02:27 local time: all 64 preimages match, no new
configuration/save files, zero remaining owned identities, Steam 2740 preserved.
The final second-pass process list says AlreadyExited because the first pass
performed cleanup; do not interpret that as proof all support processes exited
naturally. cmt-b7 remains installed under development-owned policy. Logs and
screenshots are private local artifacts and are not automatically committed.

### Newly requested automatic server mission lifecycle

The user now requires automatic map mission startup when the campaign-owned
server appears, map-to-battle switching on a host battle, and return to the map
after the battle. The literal console command must cease being a normal-play
requirement; it may remain a diagnostic control.

This requirement is accepted but NOT implemented by cmt-b7/cmt-b8. The current
launcher writes start_game only; the existing map publisher does not own mission
transitions. See the [lifecycle implementation plan](CAMPAIGN_MAP_AUTOMATIC_LIFECYCLE.md)
for state/ownership boundaries and acceptance gates. Do not implement a timer
that blindly issues start_mission whenever Mission.Current becomes null.

### Post-run readiness investigation, source-only

The installed SceneView wrapper exposes separate native ReadyToRender and
CheckSceneReadyToRender entry points. The replica map already evaluates both
independently, but the new conversation renderer short-circuited the second call
while the first was false. The source now evaluates both, then all actor resource
checks, before combining the results. This removes a readiness-progress hazard;
it is NOT established as the shader-error root cause or a verified native fix.

Default-off verbose load-stage/cached-readiness diagnostics were added to the
same renderer to distinguish synchronous creation/preload from tick readiness.
No extra native probe is performed on failure. The implementation retains the
same timeout, visual-only scene ownership, text fallback and same-state retry
suppression. Three source guards cover independent calls, explicit verbose
activation and absence of failure-path native reads. They do not execute native
rendering. The renderer is used only by the opt-in map conversation view;
field/village/siege/hideout mission renderers are not changed. Their runtime
regression is not claimed.

Documentation impact: this report, README current evidence, and BUILD_TEST_DEBUG
diagnostic activation/cost. The separate lifecycle design is documented but not
implemented. No authority, wire format, project inclusion or scene asset changed.

Validation: `cmt-c9` passed all 24 canonical projects (2,745 descriptor,
903 payload and 28 visual-lifetime/source assertions). `cmt-b9` compiled both
roles with installed inventories unchanged and no product process launched.
Client SHA-256 `4E287FB62D32A5497A121D3EDFB13C12473EC2D8115F6E19EA234DF26598BF52`;
dedicated remains `AE45FDB362B48B085618C8E4F8760AE238EECF2A5C86E1711A2EF5CF1764C036`.
Artifacts are below `%TEMP%/CoopSpectator/Automation/cmt-{b,c}9`.
No cmt-b9 deployment or native run occurred. All game roles remain closed and
cmt-b7 stays installed. Git diff whitespace checks passed; no commit was made.

Next actionable work: implement the session-bound automatic lifecycle from the
linked plan after completing native readiness/aftermath boundary inspection;
prepare a coherent candidate for a bounded diagnostic dialogue run. Do not
reuse the ended process IDs or interpret a readiness source change as proof that
the terrain-shader failure has been fixed.

### Terrain-disabled tableau investigation (2026-09-27)

The later `campaign-lifecycle-r2-20260927` run proved one automatic map / field
battle / map cycle, but reproduced the dialogue shader failure on cml-b6.
Its retained MP log records scene read at 06:28:16.289, actor completion and
preload at .300, terrain shader generation at .313, resource request completion
at .337, first readiness at .357, and Bake Terrain at .364. This timing is
correlation, not proof of the responsible native call.

Native comparison: the installed thumbnail cache creates the terrain-disabled
`scn_conversation_tableau` without PreloadForRendering or CheckSceneReadyToRender.
The stock MapConversationTableau uses TableauView, synchronous ForceLoadResources
and a busy actor-resource loop. The custom renderer still uses SceneLayer; it
does not copy the blocking stock loop. Scene wrappers were decompiled under
`artifacts/native/conversation-resource-load-20260927`; the inspected engine DLL
SHA-256 is BFD5DB82D92F75EF15A5EB9A9F0C9391412566933193122DB1684019ED704D68.
Managed wrappers forward to native functions and do not establish their C++
internals or prove a shader root cause.

Candidate experiment: remove PreloadForRendering and CheckSceneReadyToRender
only from CoopCampaignMapConversationTableauView. Retain asynchronous visible
resource requests, view readiness plus every actor's resource readiness, the
15-second managed deadline, terminal same-state failure, text fallback and exact
scene cleanup. Main-map readiness and field/village/siege/hideout renderers are
unchanged. Individual native calls can still block beyond the managed deadline.
Source guards cover the removal and preservation of map/actor checks; they do
not prove rendered parity.

Validation: cmt-b10 and cmt-c10 failed before compilation because the sandbox
denied the existing user NuGet.Config. Retried with approved dependency access,
cmt-b11 compiled both roles with installed inventories unchanged. Client SHA-256
776EF77FEBC9E4673452DA86CFE3046D91AA69DF2EF545768F703814AA88B249;
dedicated SHA-256 AE73361600BCF29681A6332D331F8E8A629077904B1F81FED2A7754CC23DF88A.
All 24 cmt-c11 contract projects passed. Native dialogue rendering FAILED.
Fresh run `artifacts/runtime/dialogue-terrain-r1-20260927` coherently installed
and loaded cmt-b11 in campaign 32428, menu-owned dedicated 59928 and shortcut-owned
MP 31212. Safe Mode No was selected. Server dispatched automatic StartMap at
07:02:15.383. After user Login, MP joined AC_COOP at 07:06:37.945 and map rendering
was ready at 07:06:42.032. Host movement led to a Deserters conversation at
07:09:25. Scene/actors completed in 10 ms; terrain shader generation appeared
after tableau-resources-begin, followed by Bake Terrain after readiness-begin.
Both pbr_terrain and pbr_terrain_gbuffer raised X3129/X4008 at
terrain_pixel_functions.rsh line 818. The two removed calls are therefore not
necessary for reproduction; the experiment is NOT a shader fix. View=False,
ActorMask=7 and Actors=3 were cached at failure after 89,885 ms, including the
blocking native error dialogs. No extra native probe was used on failure.
Both dialogs were cancelled without Retry. The client survived; screenshots
05-08 prove text fallback, host-native actors, synchronized farewell text, and
dialogue close restoring map labels. No new battle was started in this run.

Cleanup completed at 04:15:39Z: all eleven exact-owned identities exited, all
seven configuration preimages match. Only inspected safely_exited and dedicated
map-selection changes required restoration. Steam 11956 with its original start
time remains; test saves remain and cmt-b11 stays installed as an unverified
development candidate. Private logs/screenshots and cleanup.json are in the run
directory. Do not reuse its ended PIDs. Next investigation must distinguish the
custom SceneLayer/resource path from the native texture/TableauView path; the
managed wrapper comparison does not yet prove the underlying engine cause.

Documentation impact: this report, README candidate status and BUILD_TEST_DEBUG
diagnostic fields. No authority, wire format, scene asset, ownership boundary or
new subsystem changed, so architecture/code-map invariants remain unchanged.
No commit has been created.

### Off-screen dialogue target candidate (2026-09-27)

After the failed cmt-b11 experiment and complete cleanup, the installed native
MapConversationTableau/MapConversationTextureProvider, TableauView, Texture,
RenderTargetComponent, GauntletLayer and Widget implementations were inspected.
Native dialogue uses a texture target rather than a second world SceneLayer.
Texture.Release clears PaintNeeded handlers; the native owner disables/clears
the view before releasing the texture. This is architectural evidence for the
next experiment, not proof that SceneLayer caused the terrain compiler failure.

Source changes in CoopCampaignMapConversationTableauView replace the passive
world layer with TableauView.AddTableau and a private passive Gauntlet layer
with an instance-local drawing widget. No global texture-provider registration,
prefab override, campaign cache, physical agents or network authority change.
The visual layer sits below SPConversation and survives text-only refreshes.
The target is drawn while readiness is pending so native rendering can progress;
the owner still polls view and all actor resources under a bounded deadline.
No ForceLoadResources or busy loop is copied from the stock callback.

Resize recreates only the render target and invalidates readiness without
retrying failed scenes. Delayed callbacks reject a different target; callback
and draw exceptions defer cleanup to the owning tick. Cleanup detaches the draw
consumer and native view/target before actors, camera, controller and scene.
Engine resource operations remain runtime-sensitive, including allocation,
render callback ordering and release. Other battle types do not instantiate
this class; map terrain readiness and battle renderers remain unchanged.

Initial cmt-b12 client compilation found the exact Widget API is ParentWidget,
not Parent; dedicated passed. cmt-c12 passed 24/24 before final resize tests.
cmt-b13 compiled both roles with unchanged installed inventories. Final candidate
validation cmt-b14/cmt-c13 passed: both roles compiled, installed inventories
unchanged during build and all 24 projects passed. Client SHA-256
C96386ADA0851CC27B0017367B23F168C637A6265ABBB81BFD2C626A01DEE2D3;
dedicated AE73361600BCF29681A6332D331F8E8A629077904B1F81FED2A7754CC23DF88A.
The candidate was then coherently installed for a fresh native run under
`artifacts/runtime/dialogue-texture-r1-20260927`. Campaign 14236, menu-owned
dedicated 29340 and shortcut-owned MP 41156 loaded expected identities. Safe Mode
No selected; MP already logged in when inspected, then agent joined AC_COOP.
Automatic map displayed without console input. Host movement triggered dialogue
at 07:39:28: scene read 15 ms, actors 23 ms, resources requested 76 ms, readiness
began 100 ms. BOTH terrain shader dialogs reproduced at line 818 with X3129/X4008.
The off-screen target briefly showed an incomplete actor, not visual parity.
View=False/ActorMask=7/Actors=3 at 62,168 ms includes native modal blocking.
Both dialogs cancelled; text fallback, farewell update and dialogue close passed.
No battle started. The texture target is NOT a shader fix; inspect terrain/scene
resource initialization before repeating the native run.

Cleanup at 04:42:44Z completed: eleven exact-owned identities exited, seven
configuration preimages match, no new config files or product processes remain.
Original Steam 11956/start identity remains. Test saves and installed cmt-b14
remain; all PIDs above are historical. Evidence/screenshots 01-07 and final PID
logs are in the run directory. No native 3D pass or commit is claimed.

Documentation impact: this report, README, CODE_MAP, RUNTIME_FLOWS and
BUILD_TEST_DEBUG. Existing scene/texture authority remains local to the view;
ARCHITECTURE and INVARIANTS_AND_RISKS retain their ownership/bounds rules.

### Scene-wide resource-request isolation (2026-09-27)

Read-only installed shader inspection locates line 818 at
normalize(final_world_space_normal). The accumulator begins at zero (line 613),
collects enabled terrain layers (651-809), and has an optional vista contribution
(817). A compile-time empty layer set without vista can therefore fold to a
zero-vector normalization; the actual native generated header has not been
captured, so this is a hypothesis, not a proven C++ cause. The admitted stock
scn_conversation_tableau/scene.xscene explicitly has terrain enabled=false (609).
Its stock campaign scene is loaded before world-map terrain preparation, whereas
the custom MP scene is loaded during the running map. Ordering may matter.

The native wrapper confirms CheckResources(bool) takes checkInvisibleEntities,
not an asynchronous-mode flag. Both CheckResources and ForceLoadResources forward
to separate native functions; neither wrapper proves nonblocking behavior.
The cmt-b14 log places terrain-generator clear inside the interval between the
explicit scene-resource request markers. The next single-variable experiment
removes only this Scene.CheckResources call. The native texture consumer draws
the scene; view readiness and all actor resource checks remain required within
the existing deadline. No ForceLoadResources, shader edit, scene asset edit,
global cache manipulation or extra runtime probe is introduced. Risk: background
resources may never become ready. Main-map CheckResources/readiness and all
battle renderers are untouched. Only the map mode instantiates this dialogue view.

Changed source: CoopCampaignMapConversationTableauView.Create and its lifetime
source guard. cmt-b15 compiled both roles; final cmt-b16 captures a comment-only
clarification and also compiled both with installed inventories unchanged. All
24 cmt-c14 contract projects passed. A fresh run under
`artifacts/runtime/dialogue-request-r1-20260927` installed cmt-b16 and started
campaign 23624 with loaded MVID a0cd0dbe-db72-4c7e-8215-fdd30d1ac277 and client SHA
8E9DC3DB948459AAFF117DD5ED9A1E710422B95409C46FA409D5207902AB18FF.
The following initial handoff is historical; see final result below.
Menu-owned dedicated 24072 dispatched automatic StartMap command=1 at 07:57:51.269;
the prescribed shortcut created MP 31580, Safe Mode No was selected, and all three
loaded identities match. Eleven role/support identities are recorded in the run.
At 08:00 local MP awaits user Login at the Computer Use authentication boundary;
the roles remain live for handoff. No new join or 3D acceptance is claimed.
Documentation
impact: this report, README status, RUNTIME_FLOWS and BUILD_TEST_DEBUG. CODE_MAP,
ARCHITECTURE and INVARIANTS_AND_RISKS retain their unchanged ownership boundaries.

#### cmt-b16 native result and target-readiness correction

Run `dialogue-request-r1-20260927` joined AC_COOP after user Login. Both dialogue
openings timed out with View=False and all three actor resources ready (mask 7):
08:03:51 at 15,036 ms and 08:09:44 at 15,006 ms. No terrain shader error/modal was
observed. On opening two MP was foregrounded during loading and visibly rendered
three actors and outdoor background before timeout; screenshots 08/09 compare MP
and host, 10 shows fallback. Framing differs and host battle balance/portraits are
missing: NOT visual parity. Focus did not remove the timeout; resource warming
confounds comparisons. Map, movement, text update and peaceful close worked.

Cleanup 05:11:40Z: eleven owned identities gone, seven configs restored, zero new
config files/product processes, original Steam retained. Test saves and installed
cmt-b16 remain. Final per-PID logs and cleanup audit are in the run directory.

Installed-library decompilation: stock MapConversationTableau's rendering callback
does not use SceneView.ReadyToRender. That method only forwards to a native flag;
its internal semantics are unproven, but the run demonstrates false while pixels
are drawn. Texture.AddTableau attaches PaintNeeded to RenderTargetComponent and
EngineTexture.IsLoaded forwards to Texture.IsLoaded. Source correction admits
bounded scene/actor ticking when the exact target callback has configured the
view, the widget has submitted a valid loaded texture, and every actor's skeleton/
resources is ready. This is operational admission, NOT proof of completed pixels
or 1:1 parity. No global resource load or busy wait; 15-second fallback retained.
Resize/replacement resets callback/submission readiness; stale callbacks remain
rejected. Cached failure diagnostics stay verbose-only. The added IsLoaded read
is native-sensitive, so a fresh native run is mandatory. cmt-b17/cmt-c15 failed
before compilation because sandbox access to NuGet.Config was denied. Corrected
permission runs cmt-b18 and cmt-c16 passed both builds and all 24 projects (63
visual-lifetime assertions). Installed inventories unchanged during build.
Client SHA 54372A394D1318E8B324571BE92FEB62BB5682DC8606BF8B075714E761FA284E.
Then explicitly installed with backups for `dialogue-ready-r1-20260927`; campaign
17180/start 08:20:35.5788022+03:00 launched. Native acceptance and cleanup pending.

Adjacent-scenario review: only MissionMultiplayerCoopCampaignMapPrototypeMode
instantiates this conversation view. Field/village/sally-out/siege/hideout battle
renderers and main-map terrain readiness are unchanged; no regression pass implied.
Camera/framing remains a separate open investigation. Documentation impact:
README, this report, RUNTIME_FLOWS, BUILD_TEST_DEBUG. No new subsystem or ownership
boundary; CODE_MAP/ARCHITECTURE/INVARIANTS_AND_RISKS intentionally unchanged.

#### cmt-b18 result and synchronous configuration correction

Both first and repeated dialogues failed the callback-dependent gate at 15 seconds
in dialogue-ready-r1-20260927. The second visibly rendered three actors/background
before fallback. Cached flags: TargetConfigured=False, LoadedTargetSubmitted=True,
ActorMask=7, Actors=3. No shader modal was observed. Text updates and map movement
worked. All eleven owned identities exited, seven config preimages match, zero new
configs/product processes remain, original Steam preserved. Installed cmt-b18 and
test saves retained. See run README, screenshots 03-07 and final per-PID logs.

Decompiled Texture.CreateTableauTexture registers PaintNeeded, and
RenderTargetComponent.OnPaintNeeded supplies a WeakNativeObjectReference-derived
Texture wrapper; this is neither a guaranteed completion callback nor a managed
reference-identity contract. The existing synchronous ConfigureTableau already
succeeded on the exact owned target. Source now records that success immediately,
retaining loaded draw submission/all actor checks and the 15-second bound. Optional
callbacks use an instance-local registration epoch invalidated before release.
Callback absence versus wrapper mismatch was not separately instrumented; do not
claim the observed false flag proves which case occurred. No extra native probe,
force-load, polling loop, shader mutation or battle logic change was introduced.
Fresh runtime validation remains necessary for native scene ticking/callbacks.

cmt-b19 compiled both roles without installed mutation; cmt-c17 passed all 24
projects including 64 visual-lifetime assertions. Explicit deployment retained
five module preimages and seven configs for dialogue-configured-r1-20260927.
Campaign 32616 launched at 08:41:32.8668656+03:00. Results follow.

#### cmt-b19 native result

Run: `artifacts/runtime/dialogue-configured-r1-20260927`. Campaign menu created
dedicated 43880; automatic StartMap command 1 ran at 08:46:22.436 without console
input. Multiplayer 17476 launched from the user-prescribed desktop shortcut;
Safe Mode No was selected. Native lobby was already entered at observation, so
this is not evidence of automated Login. Own AC_COOP join and map rendering passed.
Installed client/dedicated SHA and executing MVID were verified for all three roles.

Dialogue openings at 08:52:54.679 and 08:56:08.082 reached operational readiness
in 63 and 50 ms. Three actors/background remained visible beyond 50 seconds on
each opening; no former 15-second fallback, shader modal or crash was observed.
First farewell text update preserved the visual scene. Both host closes restored
the map. A client answer click did not advance either the replica or host dialogue.
Evidence: screenshots 03-08 and final-rgl_log_17476.txt readiness-complete entries.

Acceptance remains PARTIAL: visual framing is enlarged/displaced, appearance and
host portraits/balance HUD differ, and map camera does not follow host movement.
No battle or broad shader regression proof belongs to this run. Next investigate
the exact widget draw transform versus stock native texture drawing before changing
camera/actor scale; double UI scaling is only a hypothesis at this point.

Cleanup at 06:01:12 UTC terminated only exact recorded identities; all eleven are
gone. Seven configuration preimages match; only engine safely_exited and the
menu-written dedicated map were restored after inspected postimage admission.
Independent postflight found no new config files or product processes and preserved
Steam 11956 with its original path/start time. Test saves and installed cmt-b19 remain.

#### Physical-pixel dialogue viewport correction

Installed GauntletUI decompilation identifies a concrete sizing mismatch:
UIContext initializes/updates Root.SuggestedWidth/Height with viewport pixels,
but Widget's Fixed measure multiplies those values by UI scale. Stock GauntletMovie
inserts a Fixed child and sets ScaledSuggestedWidth/Height to viewport dimensions
on creation and every Update. Our direct StretchToParent child bypassed that
physical-pixel sizing and inherited the oversized root. Stock TextureWidget uses
the same ImageDrawObject.Scale and full UVs as our drawing; changing those or the
camera to compensate would address the wrong layer. Native evidence retained in
`artifacts/native/dialogue-viewport-20260927`.

The map-only texture widget now uses Fixed sizing and refreshes physical dimensions
through ScaledSuggestedWidth/Height on creation and OnUpdate, preserving resize and
UI-scale changes. Three source guards cover that contract. No camera, actor scale,
shader, native resource probe, battle renderer or authority change. Field/village/
sally/siege/hideout mission views do not instantiate this map-only tableau.
Documentation impact: this canonical report, README verification snapshot and
CODE_MAP view summary; runtime ownership/defaults and architecture remain unchanged.
cmt-b20 compiled both roles without installed mutation; all 24 cmt-c18 projects
passed, including 67 visual-lifetime/source assertions. Explicit staging retained
five prior module files and seven configs in dialogue-viewport-r1-20260927.
Campaign 46320 loaded client SHA 2A82E7315C602DCD7B66346AE99A3C09182FBC666F270AA808C12FB7D2E039B8,
MVID f860fd3c-1dce-418f-a829-409e3a325a16. Native results follow.

#### cmt-b20 native viewport result

Run `artifacts/runtime/dialogue-viewport-r1-20260927` verified coherent loaded
identities in all three roles, campaign-menu-owned dedicated server, automatic
StartMap at 09:14:03.408 and prescribed-shortcut multiplayer joining AC_COOP.
Safe Mode No selected; lobby was already entered when observed, not automated Login.
First/repeated readiness completed at 09:22:34.565 / 09:25:59.727 in 48 / 49 ms.
Three actors/background remained visible beyond one minute per opening (04,08).
No former 15-second fallback, crash or shader modal observed in this bounded run.
Client answer click preserved the host's original two answers (05). Farewell text
updated without losing 3D (06); both host closes restored the map (07,09).

Physical viewport correction is runtime-supported: background landmarks match
host framing and gross full-image enlargement/displacement is removed. Remaining
face age/scars, facial expressions/animation phase, party portraits and balance
HUD differ. Do not claim pixel parity or 1:1 dialogue acceptance. Map camera still
does not follow the host. Resize/other resolutions, battles and broad shader
regression were not exercised here. Next investigate exact stock actor/facial
construction and immutable host-visible descriptor before changing scale/camera.

Cleanup 06:29:09 UTC: all eleven exact recorded identities exited. Seven config
preimages match after inspected safely_exited/dedicated-map restoration. Independent
postflight found zero new configs/product processes and original Steam 11956
unchanged. Test saves and installed cmt-b20 intentionally retained.

#### Bounded face preparation candidate

Post-b20 native-code inspection: installed MapConversationTableau uses
PrepareImmediately(true) for both leader and bodyguards; our replica used false.
AgentVisuals.AddSkinMeshesToEntity forwards this flag into SkinGenerationParams
before the native AddSkinMeshes call. BodyProperties capture/parse preserves all
eight static keys and dynamic age/weight/build; no managed multiplayer clamp was
found in that path. Both recorded processes loaded Native/ModuleData/skins.xml.
These facts do not yet prove the missing face-detail root cause.

Candidate cmt-b22 changes only owned dialogue actor face preparation to true,
bounded by the existing maximum of three actors. No scene-wide request, resource
force-load, global face-cache mutation or battle Agent creation is introduced.
Native face generation can synchronously stall/fail; readiness timeout cannot
preempt such an engine call. A fresh bounded native run must compare faces and
retained readiness/reopen, with exact process/config cleanup. Until then this is
an unverified experiment, not a visual fix. Facial animation/phase and top party
HUD remain independently incomplete.

Adjacent scenarios: field/village/sally/siege/hideout do not instantiate this
map-only view; no shared AgentVisuals implementation or battle adapter changes.
Documentation impact: this canonical report and verification index; CODE_MAP
retains owned-scene responsibility, architecture/authority/defaults unchanged.
One source guard checks stock face preparation while existing guards prohibit
scene-wide force loads. Initial cmt-b21/cmt-c19 failed before compilation because
the sandbox could not read NuGet.Config; cmt-b22 compiled both roles with required
filesystem access and confirmed unchanged installed inventories. All 24 cmt-c20
projects passed (68 visual-lifetime/source assertions). Fresh evidence root:
dialogue-face-r1-20260927, with five module and seven configuration preimages.
Campaign 10496, menu-owned dedicated 40724 and shortcut MP 41480 loaded identities
verified. StartMap command 1 dispatched at 09:49:15.165, 397 entities. Safe Mode No
selected, user-assisted Login completed. All eleven owned identities recorded;
the run is now closed with the bounded native result below. Do not reuse ended IDs.

#### cmt-b22 native face-detail result

AC_COOP join completed with map readiness at 10:00:22.829. First/repeated dialogue
readiness completed at 10:02:14.487 / 10:05:54.716 in 42 / 51 ms. The left guard's
scar and central actor's age/face detail are now visible, unlike the b20 images.
All three actors remained visible beyond one minute on both openings. Host/client
comparison evidence is 03/04 and 09/10; 05 proves first retention, 06 unchanged
host answers after client click, 07 farewell text without losing 3D, and 08/11
both close-to-map results. This supports the bounded immediate-preparation fix,
not exact facial/animation or full dialogue 1:1 acceptance. Body animation phase,
expressions, party portraits/balance HUD and host-following map camera remain open.
No battle, other resolution or broad shader regression was exercised.

No shader modal or crash was observed. Retained logs have no X3129/X4008; they
are NOT error-free: FMOD warnings, guarded intermission ArgumentNullException
messages and dedicated optional-assembly load errors remain in the record.
These are not evidence that the face change caused a new regression.

Cleanup at 07:11:30 UTC stopped only exact PID/path/start identities. Independent
postflight at 07:12:47 UTC confirms all eleven gone, seven configuration hashes
matching, no new config files or product processes, original Steam 11956 unchanged,
and installed cmt-b22 retained. Test saves remain. The first independent verifier
miscounted the JSON array under Windows PowerShell; explicit pipeline enumeration
corrected that audit-only bug, without further game/config mutation.

#### Animation synchronization investigation after cmt-b22

Read-only installed-code/source findings (no new runtime candidate):

- CaptureActor copies AgentVisualsData.ActionCodeData, the construction action,
  not the live skeleton's current action/phase. Installed AgentVisuals' own
  refresh path reads MBSkeletonExtensions.GetActionAtChannel(skeleton, 0) and
  Skeleton.GetAnimationParameterAtChannel(0), demonstrating the live body path.
- Stock MapConversationTableau sets default facial animation on channel 1 with
  loop=true and later reactions with loop=false. Our view never sets that channel.
  GetSkeletonFaceAnimationName/Time expose no channel argument; do NOT assume
  they represent channel 1 or reconstruct loop semantics from their names.
- Stock OnTick ticks only the leader's action channels, with Scene.Tick(3f) once
  at initialization. Our view additionally calls Scene.Tick every frame and ticks
  all actors. Native render-loop timing is not yet established; double ticking
  is a hypothesis, not a proven root cause. Do not remove ticking blindly.
- Publisher captures every 0.2 s. Adding phase to the existing immutable tableau
  equivalence would change every conversation revision. BroadcastConversation
  cancels unfinished transfers on every new revision, ShowConversation rebuilds
  the UI, and VisualLifetime replaces the whole scene when descriptor bytes differ.
  A naive phase-field patch therefore risks transport starvation and scene churn.

Next implementation plan: keep immutable actor/scene resources distinct from a
bounded, monotonic live-pose projection. Bind updates to the exact dialogue and
actor descriptor, coalesce motion without starving text/close delivery, apply only
to owned ready actors, and preserve text UI/native scene across pose-only updates.
Resolve facial-channel semantics before claiming expression synchronization.
Tests must cover stale updates after close/reopen, reordered/partial delivery,
actor replacement, invalid native identifiers/finite phase bounds, transfer budgets
and failure without a same-state recreation loop. Runtime gates remain repeat
dialogue, retention, text transition, close, loaded identity and exact cleanup.
Potential native-sensitive operations are read-only skeleton queries on the host
and action/facial setters on owned client visuals; do not touch physical battle
agents. Adjacent field/village/sally/siege/hideout dialogue paths were source-checked:
only the map prototype registers this view. Their runtime regression remains open.
Documentation impact: this report, CODE_MAP, RUNTIME_FLOWS, INVARIANTS_AND_RISKS,
BUILD_TEST_DEBUG and README if transport/schema or runtime behavior changes;
architecture/authority remain unchanged unless the eventual design changes them.

#### Live body-pose channel candidate (2026-09-27)

Source implementation, native verification pending. `CoopCampaignMapConversationPose`
is separate from immutable tableau/text equality. The host reads only initialized
visible skeleton channel 0: current action, normalized phase and speed. Each sample
is bound to conversation revision, monotonic host publication sequence, SHA-256 of
the exact ordered tableau descriptor and actor count (1-3). It never queries hidden
rosters or changes host animation. Invalid/unavailable motion is optional.

Bridge 18 includes a separate bounded pose field; protocol 22 adds a server-only
single-packet pose message. The strict binary payload is at most 472 bytes for three
128-character action names (512-byte hard allocation limit), with finite phase
0..1 and speed 0..10. One latest pose is considered per accepted 0.2-second host
publication. The server maps host revision/sequence into its monotonic network
revisions and skips motion for peers with unfinished text/descriptor transfer.
It does not replace or restart those transfers. The theoretical maximum pose-body
budget is 2,360 bytes/second/peer, excluding native framing/reliability overhead;
actual bandwidth and low-frame-rate behavior still need native measurement.

The client rejects old/duplicate/future/unmatched samples without buffering them
across dialogues. Close and a new descriptor revision are barriers; mission reset
clears reception state. Only the owning mission-screen tick can apply motion to
ready owned actors; one pending sample expires after 0.5 seconds and is coalesced.
All local action resources are resolved before modifying any actor. Motion does
not create scenes, actors, textures or text UI. Native failure retains existing
terminal-per-scene behavior and text fallback rather than a retry loop.

This candidate covers body action/phase/speed, not facial channel 1, voice/lip sync,
network-latency prediction or full 1:1 animation. Native tick ownership is unchanged
pending evidence. Tests cover binary limits/corruption, clone isolation, exact
binding, stale/duplicate/reordered samples, close/reopen, replacement and reset.
Both cmt-b23 role builds passed. cmt-c21 returned 23/24: a corrupt-input test found
an uncaught InvalidDataException in the new decoder, corrected explicitly.
cmt-b24 then built both roles; cmt-c22 found an overbroad source assertion matching
ActionIndexCache.Create as scene creation. The assertion now targets actual
scene/actor allocation. The final cmt-b25 built both roles with installed inventories
unchanged, and cmt-c23 passed all 24 projects (719 pose assertions). First applied
pose per dialogue revision is logged only with explicit verbose enabled, using
already-admitted managed values and no extra native probes.

Fresh native run dialogue-pose-r1-20260927 now has cmt-b25 installed with cmt-b22
module preimages retained. Campaign 23864, menu-owned dedicated 15992 and
prescribed-shortcut MP 39088 have verified loaded MVID/on-disk SHA identities.
Server dispatched StartMap command=1 at 10:42:07.390 automatically. Safe Mode No
was selected and multiplayer Home is visible. Eleven exact process identities are
recorded. Join, native body-pose acceptance and exact cleanup remain pending.
Use Windows PowerShell for the retained scripts: direct PowerShell 7 invocation
auto-converted JSON dates and failed the parent identity parse before mutation.
The guard was preserved; execution under the intended host passed.

#### cmt-b25 native body-pose result: smoothness failed

The completed run recorded twelve exact identities (not the preliminary eleven).
AC_COOP map rendering was ready at 10:48:44.775. First dialogue readiness completed
at 10:49:58.019 in 44 ms, with three poses applied at 10:49:58.100, revision 2,
sequence 401. All three actors and face/scar detail remained visible beyond five
minutes. A client answer click did not advance the host. Host farewell synchronized,
with revision 3 pose applied at 10:55:36.643 without another scene-read initialization.
Closing the first dialogue returned MP to the map (images 01-09 in the run root).

The user observed that replica actors jerk while campaign actors move smoothly.
This FAIL overrides any implication that successful pose transport proves animation
quality. Repeat opening was not completed; no battle regression was performed.
Source confirms each 0.2-second sample directly overwrites channel-0 phase while
the client independently ticks its scene/actions. Hard correction of delayed samples
is a concrete discontinuity mechanism consistent with the observation; native double
ticking remains an unproven additional hypothesis. Stock AgentVisuals.TickVisuals
calls TickActionChannels, while stock MapConversationTableau.OnTick invokes it only
for the leader and does not call Scene.Tick every frame. Do not remove ticking or
claim a unique native root cause without testing. No facial, HUD or 1:1 acceptance.

Cleanup stopped all twelve exact-owned identities and restored seven configuration
preimages, with zero new config files/product processes and original Steam retained.
All five previous cmt-b22 module files were restored; independent postflight passed
at 08:01:14 UTC. Test saves remain. The first workspace-sandbox cleanup stopped roles
and copied logs but config restoration was denied; permission-reviewed rerun completed
restoration. Its AlreadyExited outcomes refer to first-call terminations. Source
remains the b25 candidate; installed runtime is b22. Fix smoothness before restaging.

#### Rate-steering correction after the jerking report (source candidate)

Installed MBActionSet decompilation confirms GetActionAnimationDuration and
GetActionAnimationFlags; AnimFlags.anf_cyclic is the actual cyclic flag. Do not infer
looping from action-name suffixes. The new pure CoopConversationPosePlayback uses
these facts to project a received phase by local receipt age and steer the existing
native playback rate. Same-action samples never overwrite skeleton phase. Only first
pose or action replacement seeds phase; new text revisions preserve playback state.
Correction is capped at 15 percent of nominal speed and per-frame speed change is
bounded. Cyclic error uses the shortest wrap, noncyclic error never wraps. After
0.5 seconds without a sample, correction expires and rate returns to nominal; no
network-latency estimate or exact temporal parity is claimed. Resource release clears
all playback state. Per-frame Scene.Tick/TickVisuals behavior is deliberately unchanged
to isolate hard phase correction rather than simultaneously changing native timing.

New native calls are limited to owned ready dialogue visuals and action metadata;
no physical battle agents or host setters are touched, no default-on diagnostics
are added. Registration remains map-prototype-only; field/village/sally/siege/hideout
physical mission paths are not modified. Their native regression is not claimed.
Deterministic tests cover jitter/gaps, cyclic/noncyclic error, bounded rate slew,
expiry, zero speed, long frames, invalid values and fresh replacement state.
cmt-c24 and cmt-b26 could not compile due to sandbox-denied NuGet.Config reads;
these are environment failures, not executed-test failures. Permission-reviewed
cmt-c25 passed all 24 projects, including 4,331 pose/playback assertions; cmt-b27
built both roles and verified installed inventories unchanged. Client SHA-256
6CCCE13F96B2913ECCB1709F67CAEE26AA3287E343528F9E9AABD9EA9E5809E9;
dedicated 9C52DC90794DA26B7F922C42FD0345720C1760F74CDFC30808547ED27762B315.
Fresh dialogue-smooth-r1-20260927 staged cmt-b27 with five b22 module and seven
config preimages. Campaign 22584/start 11:12:11.8320130+03 and watchdog 56212 are
owned. Loaded client MVID 7c165645-515f-4728-8b5a-e903ce5a5e81/on-disk SHA verified.
Campaign loaded; the completed native result below supersedes this preparation.

#### cmt-b27 native rate-steering result

Evidence: `artifacts/runtime/dialogue-smooth-r1-20260927/README.md`, images 01-12,
loaded identity records, retained final role logs, cleanup.json and postflight.json.
Campaign 22584, menu-owned dedicated 30312 and prescribed-shortcut MP 45436 loaded
the candidate MVID/on-disk SHA identities. Safe Mode No was selected. StartMap
command=1 was automatic at 11:16:01.159; AC_COOP join and map rendering passed.

First peaceful Tristania dialogue reached readiness at 11:26:29.520 in 46 ms.
Leader confident2_loop and two normal_loop escorts received revision 2/sequence
490 poses. The user explicitly compared motion with campaign and reported it smooth
and correct. This closes the reported body-jerking defect for that inspected scene;
it is not a screenshot-derived smoothness judgment or exact network phase claim.
A physical Escape interrupted UI control; no further input occurred in that turn.
After user confirmation, host reinspection verified that an earlier MP answer click
did not advance host dialogue. Text changes synchronized, with the first scene
retained across revisions 3-5 beyond eight minutes. First close returned MP to map.

The second peaceful Tristania opening reached readiness at 11:36:57.279 in 53 ms,
and admitted revision 7/sequence 3571 poses. Three actors, matching text and face
details remained visible beyond one minute. Revisions 8-9 did not recreate the
scene (elapsed 129859/144962 ms); second close returned MP to map. Exactly two
scene-read starts/readiness completions match two openings. Repeat retention is
verified, not a second user smoothness assessment. No X3129/X4008 or dialogue
readiness timeout was found in the retained MP log; guarded intermission
ArgumentNullException messages remain, so the run is not classified error-free.

All twelve exact-owned identities are gone. Two inspected config changes were
restored; all seven preimages match, no new config files/product processes remain,
original Steam and test saves are preserved. One support PID identity failed during
parent shutdown and was preserved, then absent on a read-only follow-up; do not
infer PID reuse versus exit race. Independent postflight passed 08:40:39 UTC.
Installed cmt-b27 is intentionally retained; all five candidate file hashes match.
No rollback to retained b22 preimages was needed. No commit was created.

Scope: body smoothness and bounded first/repeat operational dialogue checks pass;
facial animation/lip synchronization/voice, complete HUD/map visual parity, repeated
map-battle-map and adjacent battle regression remain open. The overall Goal is not
complete. Documentation impact: this canonical result plus narrow status updates
in README, CODE_MAP, RUNTIME_FLOWS, INVARIANTS_AND_RISKS and BUILD_TEST_DEBUG;
ARCHITECTURE unchanged because ownership/authority did not change in verification.
