# Settlement event disclosure parity

## Evidence (2026-09-28)

cmp-b50 run `map-relation-r1-20260928`, paired images 08/09: extra cyan issue
marker at Ethemisa in the replica. The earlier enemy-village dynamic capture has
`settlement:castle_village_ES4_2`, IsVisible=True, IsSettlementInRange=False,
QuestMarkerFlags=0, SettlementEventFlags=66 (available issue + production),
SettlementRelation=2. This earlier capture is not a simultaneous host UI dump.

Independent source proof: installed SettlementNameplateVM.IsInRange registers
SettlementNameplateEventsVM only while inspected/spotted. UnloadEvents removes
every non-production event; constructor adds only village production. Stock
Small/Medium/Large prefabs bind EventsList directly; TrackQuests is separately
gated by IsInRange. Our publisher/replica previously exposed all event bits even
outside that range. This is not a diplomacy rule. Native decompilations retained
in artifacts/native/map-settlement-events-20260928 and map-settlement-nameplate-20260926.

## Candidate and shared-scenario audit

CoopCampaignMapSettlementEventPolicy bounds known bits and permits only Production
outside host range; no quest marker bits are disclosed there. Publisher filters
before transport; replica filters before populating stock lists, including range
loss. IsSettlementTracked remains unchanged: tracked settlement visibility is
distinct from knowledge of its available quests. No protocol changes, client
distance approximation, campaign writes or new diagnostics.

Town/castle/village use host IsInspected; hideouts use ISpottable.IsSpotted. This
map path is shared by initial join and returns from field/village/siege/hideout;
no battle adapters/renderers are changed. Shared code is not live regression proof.
Native event order, stale quest caching and quest tooltip fidelity remain open.

SettlementEventDisclosureTests exhausts 256 masks with both range states, the
captured 66 -> 64 case, reveal/clear, invalid bits and source wiring. Builds and
both isolated builds passed as cmp-b52 and all 24 contract projects passed as
cmp-c53 (1032 event disclosure assertions). Installed cmp-b51 remains unchanged and
belongs to the separate escape-menu run. Native correction NOT RUN.

## Native gates

Compare uninspected Ethemisa (no issue, production retained), inspection reveal
and loss, tournament, tracked quests and hideout spotting; verify join/rejoin and
dialogue/battle return. Manual client camera must never disclose more than host.
Full replica Goal remains open.

## Ordered marker representation (2026-09-29, not wired)

Historical foundation boundary; superseded by the source integration below.

Exact installed SettlementNameplateEventsVM investigation proves flags alone lose
presentation state. The constructor appends village production first; registration
then appends tournament and issue kinds, and later event callbacks append/remove
items without sorting. The same flags can therefore describe distinct visible
orders. TrackQuests also preserves tracked-object iteration order; its if/else-if
admits an ordinary marker after an already-present special marker even for another
special quest. Recomputing a mask from QuestManager is not the native list.
UnloadEvents removes non-production events but retains cached TrackQuests; native
IsInRange gates the latter's display. Never publish that stale cache out of range.

Read-only ILSpy inspection of the installed SettlementNameplateEventItemVM confirms
public Type and AdditionalParameters are the presentation properties; EventType is
the corresponding readonly enum. Production stores its icon in AdditionalParameters;
all other stock event constructors use an empty string. Read the actual properties
and existing lists, never RegisterEvents, Tick, RefreshQuestCounts or a new host VM.

New pure CoopCampaignMapSettlementMarkerOrder represents seven unique event types
and five unique one-hot quest flags as low-first three-bit digits (type index + 1).
It preserves order, rejects duplicates/holes/unknown types/overflow/flag mismatch,
and yields no partial output on invalid input. Decoding applies the existing host
range gate without reordering. Empty is zero; no absent-source fallback fabricates
an order. MaxEventOrder=2097151, MaxQuestOrder=32767. The runtime is NOT wired yet;
protocol31/bridge26/catalog8 and installed cmp-b62 remain unchanged.

SettlementMarkerOrderTests enumerates all 13700 ordered event subsets and all326
ordered quest subsets, verifies an independent mask/base-eight oracle, both range
states, production/issue and late-tournament ordering, tracked quest reversal and
malformed-input rejection. These are managed representation tests, not native
rendering evidence. cmp-c64 could not compile any project because sandbox access
to the user NuGet.Config was denied. cmp-b63 isolated client/dedicated builds pass
with installed inventories unchanged; permission-correct cmp-c65 passed all24
contract projects, including 126313 ordered-marker assertions. Result artifacts
for all three runs and the passing map-suite log are retained under
artifacts/verification. No game process was launched for this foundation.

### Required integration, not optional follow-up

1. Capture detached ordered Types and TrackQuests flags from the already loaded
   SettlementNameplatesVM obtained once per capture. Bound lists; require host
   visibility and range admission; copy production AdditionalParameters. Missing
   or invalid source fails closed; never mutate/register host presentation state.
2. Add SettlementEventOrder and SettlementQuestOrder to full/dynamic contracts,
   clones, merges, equality, hidden-state clearing, bridge codecs and both native
   message paths. Order-only changes must emit a dynamic delta. Preserve party
   quest semantics: party flags do not imply a settlement ordered quest list.
3. Bump protocol to32 and bridge to27; catalog8 need not change for dynamic-only
   fields. All map message protocol compressors (including conversation/tooltip)
   currently cap31 and must coherently support32. Audit packet bounds (two dynamic
   entries per batch; the two fields add36 bits/entry), rejection and mixed-version
   behavior; no mixed installed roles may be launched for validation.
4. Reconcile client lists on order/content/range change, not just bitmask changes.
   Preserve host order and production icon. Retain disclosure filtering and avoid
   rebuilding unchanged lists every frame. Add transport, clear, delta-only-order,
   malformed, nonsettlement and source-wiring tests before native deployment.
5. Compare host/replica production plus issue, quest ordering, range loss/reveal,
   rejoin and battle/dialogue return. The map capture/view is shared after field,
   village, siege and hideout missions; no physical battle adapter is to change.

Documentation impact: this canonical document, README and CODE_MAP. No new hot-path
diagnostics, native-state inspection, protocol or behavior changes are present in
this foundation. Full goal remains open; the representation alone fixes no pixels.

## Ordered marker integration (2026-09-29, source candidate)

Publisher CaptureSettlementMarkers now copies Types/AdditionalParameters and
TrackQuests flags from the already loaded SettlementNameplatesVM. The source is
obtained once per capture, not constructed or refreshed by the replica publisher.
Host model inspection/spotting and existing nameplate IsInRange are intersected;
cached native quest rows never publish outside that range. Production remains
admitted outside range, with its exact bounded icon. Invalid/missing lists yield
no invented order. Party quest capture is unchanged.

SettlementEventOrder/SettlementQuestOrder now cross full/dynamic records, clones,
projection, bridge codecs and both native entity messages. Equality includes
order, so an order-only change emits a delta. Publisher-hidden state and removal
tombstones clear both masks and both orders. Client decoding preserves order and
range filtering; unchanged order/masks/icon/range retains existing UI lists.

Protocol32 / bridge27 / catalog8: all15 native map readers use the shared six-bit
protocol range0..63 and reject a mismatching version before payload parsing. This
changes the old five-bit header and is NOT a mixed-version compatibility promise.
Campaign, dedicated and multiplayer must restart on one coherent candidate.
Full bridge parsing remains syntactic: BridgeFile subsequently rejects old schema
at TryValidateHostSnapshot before publishing the snapshot. Dynamic parsing performs
its schema admission directly. Missing new order fields reject both representations.

Packet audit: each entity adds fixed21+15 bits. The unchanged maximum two-entry
dynamic batch adds73 bits including the protocol-header growth (at most10 additional
bytes). No new variable-length string is introduced. This bounds incremental cost,
not total native-packet acceptance; fresh in-game transport remains mandatory.

Verification: cmp-b64 compiled the initial integration; cmp-b65 compiled both roles
after early version guards and removal clearing, with installed inventories unchanged.
cmp-c66 passed23/24 projects: the new test incorrectly required the full syntactic
parser itself to reject schema26. Inspection confirms BridgeFile applies the real
schema/freshness admission after parsing; the corrected test exercises that boundary
without changing production policy. cmp-c67 then exposed a new fixture selector
requesting MobileParty although the shared fixture contains MainParty; cmp-c68
exposed a source regex omitting the equivalent named maximumValueGiven argument.
Both test defects were corrected without changing production behavior. cmp-c69
passed all24 projects, including126313 order and87 transport assertions. New tests cover exact
codec roundtrips, required fields, corrupt orders, deep clones, order-only/clear
deltas, hidden removal, projection and non-settlement marker rejection. Source
wiring checks are not actual native message serialization/rendering proof.

Shared-scenario audit: only campaign-map capture/transport/presentation changes.
Initial joins and returns from field, village, siege and hideout reuse this path;
no battle adapter, spawner or result application changes. No new diagnostics or
native state mutation. Those builds/tests did not launch a game or alter installed
cmp-b62. Fresh map-markers-r1-20260929 now staged allseven cmp-b65 files with exact
preimages and launched campaign59184 after Steam readiness. Loaded campaign MVID
and disk SHA match the build; native acceptance and cleanup are pending.
Native ordered-marker acceptance, inspection loss/reveal,
quest/tournament combinations, rejoin and dialogue/battle-return gates remain open.

### Bounded native acceptance in progress

map-markers-r1-20260929 subsequently verified all three loaded cmp-b65 identities,
joined AC_COOP after user Login and automatically entered the map mission. Paired
evidence02-05 confirms initial production/issue markers and Ethemisa production
without an out-of-inspection issue. Movement toward Ethemisa revealed its issue;
evidence06-07 confirms production then issue in both campaign and replica. Source
bridge copies are retained locally. This closes the bounded production+issue
order/reveal example only; other quest/tournament combinations, range loss,
candidate-specific rejoin/dialogue/battle return remained open at that checkpoint.

Final run closure07:04Z: user-assisted camera return independently observed;
companion Iara's mission dialogue is absent (evidence09-11), repeating the known
cmp-b5 Mission.Current/MapConversationView-only gap, not a proven marker regression.
All7 config preimages match; no new config/owned/product processes; installed
cmp-b65 retained and hash-verified. Reused PID31700 was preserved. Range loss,
quest/tournament combinations and this candidate's rejoin/battle return remain open.
