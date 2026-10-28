# Settlement menu top overlay — open presentation gap

## r67 user/native acceptance,2026-10-28

User confirms the settlement interface/tooltips work and moves priority to the
Send troops auto-battle window. Coherent b292 loaded in campaign74532, DS54020 and
MP22836. User manually confirms militia tooltip. Direct Computer Use observes
Walls/Level1 and Food216.51, full native terms and Expected Change+41.56; retained
PNG r67/evidence/client-zeonica-food-tooltip.png. Portrait click opens a local
Polon read-only context popup; closing it works. That is not proof of every hover
provider or every settlement/scenario. Initial automated hover was unreliable and
must not be counted as a game regression. Broad Goal/multi-peer gates remain open.
r67 CLOSED after user exited all roles; all7 config hashes match, only inspected
DS keep-map switch restored; Steam/saves preserved.

## b290 acceptance and b292 tooltip candidate,2026-10-28

The user confirms the top interface now works, with tooltips still absent. b290
native popup correction is accepted. r66 CLOSED after user exited all roles;
all7 config hashes match. Only the inspected dedicated map switch to
empire_castle_keep_a_l2_interior was restored to preimage empire_village_o.

b292 adds independent host-formatted settlement-panel tooltip interests. CMN11
adds a bounded ordered HintTokens map; protocol70/host bridge65/tooltip6 requires
coherent roles. SettlementControl13 preserves string hints with the stock hint
movie; SettlementInfo14 preserves generic native property rows; Hero15 preserves
native hero mode/rows. Existing mobile-party/settlement providers retain their
native route. No tooltip payload or hovered selection is broadcast from host UI.

Only current prepared native hint/portrait sources in the active overlay receive
opaque settlementui capabilities. Each lease binds context, overlay, session,
menu, key, native source and exact portrait target. Replaced/removed/hidden source,
save/menu/session change and failed capture revoke it. Successful publication,
peer lease, freshness, generation and current owner are independently rechecked
by relay, dedicated, client and host. Tokens never resolve arbitrary settlement
or hero IDs. Hidden/non-fortification statistic interests are not issued.

Host capture reads SettlementMenuOverlayVM._settlement and requires the exact
current settlement. It never constructs or Refreshes native overlays. Statistics
use the exact audited CampaignUIHelper methods and their missing-information
logic. Crime uses GetCrimeTooltip directly, omitting only the overlay wrapper's
inspection event. Walls uses its native string function. HintViewModel text is
already prepared; no client command invokes a host action. Shipyard has no
producer in the audited installed constructor; absent native sources remain
unavailable, with no invented replacement.

Fresh TooltipRefresherCollection-current.cs confirms portrait dispatch precedence:
MobileParty -> Settlement -> Hero(true). Hero uses PropertyBasedTooltipVM and
RefreshHeroTooltip, including native relation mode, hidden-information rules and
quest rows. Audited GetQuestStateOfHero reads tracked quests/issues and builds new
lists; GetCommaSeparatedText builds a new TextObject with scoped global variables.
GetHeroKingdomRank mutates copied HERO attributes on its text variation: explicit
copy-isolation checks cover faction rank0/1, mercenary and nobleman variants.
The existing owner-thread/reentry guard, MBTextManager transaction, language-thread
restoration and permanent cleanup-fault stop surround all provider execution.
No new hot-path diagnostics or native item registry mutation are introduced.

Detached hint VMs preserve parent bindings and local hover state; a changed source
ends the old interest before starting its replacement. Portrait interest tokens
are separate from encounter tokens and do not mutate the prepared portrait data.
Repeated values do not rebuild model images or restart stable hover. Destruction
ends interests. Management/talk/add-member commands remain inert.

Validation: b291 isolated client/dedicated builds and six compiled checks passed.
c193 had25 passes and one TEST compilation failure (request DTO is immutable);
test constructor usage corrected. c194 reached the old encounter source-wiring
assertion, which still named _state.TooltipId; updated to the separate _tooltipId
field and added a compiled encounter-hover regression check. Both b292 builds,
all26 c195 contracts and six compiled checks now pass. Installed inventories were
unchanged by isolated builds. r67 installed nine coherent files with seven fresh
config preimages; campaign74532 launched2026-10-28T03:20:29.7478561Z. Native hover/layout/Alt behavior is not yet
proved, and no full Goal completion follows.

Native acceptance next: village militia/hearth and notable; town/castle walls,
garrison/militia/food/prosperity/loyalty/security/crime and hero/party cards; extension
key behavior, independent hover with host elsewhere, leave/reenter, settings,
save reload/reconnect and no stale tooltip or visible empty popup. Multi-peer
acceptance remains separate.

## r65 native comparison and b290 hidden-popup correction,2026-10-28

User accepts the siege-engine ambush return on b289. Paired direct window
inspection of campaign79944 and MP41736 at Morenia Castle confirms matching
top-panel values (walls2, garrison0(+1), militia92(+1), food624(+8), prosperity777,
loyalty56(-3), security55(+4)), but MP has an unsolicited large empty brown popup
and a Missing-labelled footer. Campaign has no such popup.

The installed ClanCardSelectionPopup.xml root binds IsVisible. Native
SettlementMenuOverlayVM constructs CardSelectionPopup and explicitly hides it;
the detached replica instead returned null. The missing child context leaves
the stock nested prefab's default visible state and unresolved footer label.
OverlayPopup has a separate IsContextMenuEnabled=false binding and is not the
matching large card-selection layout.

b290 supplies an owned hidden card-selection VM, empty items/text, disabled Done,
hidden input-key contexts and inert commands. No native popup constructor, action
delegate or campaign access is introduced. Its visibility has no setter, and
publication updates cannot open it. This shared prefab affects village, town and
castle; the compiled binding check now verifies explicit non-null hidden child
state, inert actions and retention across all three presentation variants. Its
previous null assertion was inadequate and allowed this native layout regression.
Both isolated b290 builds, all26 c192 contracts and six detached compiled checks
pass. r66 installed nine coherent files with seven fresh config preimages after
r65 closure/restoration. Native corrected appearance still requires the new run;
do not equate metadata/detached-object checks with actual widget acceptance.

## Source candidate b289

The user requested one combined native test for this panel and the ambush return
fix. b288 was build/test verified but NOT installed; b289 includes both changes.
Both isolated b289 builds, all26 cmp-c191 contracts and the compiled
Test-SettlementMenuBindings.ps1 check pass without a game/native overlay instance.
Native layout/transition acceptance remains pending.

`CoopCampaignSettlementMenuPresentation` contains a closed typed schema of native
display labels, integers and visibility/warning flags; owner banner; ordered
characters/parties; exact portrait-resource definitions. Missing/unknown scalar
keys, malformed portraits, unrelated encounter tooltip tokens, invalid resources,
invalid wall state and oversized lists are rejected. Deep snapshots preserve
native order and Unicode. CMN10/protocol69/host bridge64 delivers it atomically with
the menu; coherent host/server/client installation is mandatory.

Capture reads only the existing SettlementMenuOverlayVM on the current active
MenuViewContext. No constructor, Refresh, event subscription, tooltip producer or
simulation list rebuilding runs. Shared exact portrait projection uses the b287
unregistered visual-resource path. The client uses the stock SettlementOverlay
movie and CoopCampaignSettlementMenuVM; list pagination/toggle controls operate
locally through stock widgets. Content updates preserve list-item VMs. Absent
panel/list state clears previous content. Conversation/modal suspension and menu
release use the existing map menu lifetime; no host action delegates are present.

Remaining fidelity boundary: statistic and portrait hover tooltip interests are
not wired for this new panel yet. Non-null passive hint VMs preserve the stock
parent binding context for display values without invoking native tooltip
delegates. Add-companion/context actions are inert. Native appearance, list
pagination, village/town/castle differences, close/return and settings transitions
must be verified before declaring visible layout parity; full hover fidelity is
still an open Goal requirement, not covered by the compiled binding test.

## User evidence,2026-10-27 r63/b287

The user accepts the prebattle main-hero portrait and village battle, but shows a
missing top settlement panel when entering a village, town or castle. Paired PNGs:
`artifacts/runtime/map-portrait-r63-20261027/evidence/user-settlement-overlay-host.png`
and `user-settlement-overlay-missing-client.png`. Avalyps host panel displays owner
banner/name, militia41, prosperity346(+1), ordered notable portraits and a small
secondary portrait strip; MP replicates the left menu but omits the entire panel.

## Source/native findings

This is not the map world SettlementNameplate movie and not EncounterOverlay.
Installed GauntletMenuOverlayBaseView owns _overlayDataSource separately from
GameMenuVM and loads **SettlementOverlay** for SettlementMenuOverlayVM. Existing
capture handles only EncounterMenuOverlayVM. No settlement-panel presentation,
codec or client VM is currently implemented.

Installed SettlementMenuOverlayVM has already-prepared display strings/flags:
settlement name, owner banner, walls, militia, garrison, food, prosperity, loyalty,
security, crime, shipyard, change amounts, warning states and ordered CharacterList
and PartyList. Party entries share GameMenuPartyItemVM portrait/banner/quest fields.
Never reconstruct these lists or amounts by querying hidden campaign simulation.

Do not construct, Refresh or tick a native settlement overlay for replication:
its constructor reads Settlement.CurrentSettlement, subscribes campaign events,
creates native tooltip delegates and sets up actions. GetCrimeTooltip also emits
an inspection event; arbitrary native tooltip invocation is not read-only.

Native SettlementOverlay.xml includes list-filter widgets, portrait item templates,
change-amount widgets and context/action popup. The root SettlementOverlayWidget
inherits OverlayBaseWidget; its update/command/property writes need audit before
using a detached client VM. Host commands such as talk, recruit, assign companion
and management must not be forwarded. Independent read-only list browsing belongs
to each peer; host mutations/current disclosure still determine content.

Decompiled primary sources retained in
`artifacts/native/settlement-menu-overlay-20261027`. The first guessed widget
namespace failed; the actual namespace is
TaleWorlds.MountAndBlade.GauntletUI.Widgets.Menu.Overlay.

## Next implementation and acceptance

### Fresh native tooltip audit2026-10-28 (no implementation yet)

Current installed CampaignUIHelper freshly decompiled to
`artifacts/native/settlement-menu-overlay-20261027/CampaignUIHelper-current.cs`.
Village militia and hearth/prosperity, town militia/food/loyalty/prosperity/security/
garrison all use the native GetSettlementPropertyTooltip family, which explicitly
preserves missing-information state via IsSettlementInformationHidden. Do not
replace its hidden value with simulation totals in a custom formatter.

SettlementMenuOverlayVM.GetCrimeTooltip emits
CrimeValueInspectedInSettlementOverlayEvent; its CampaignUIHelper.GetCrimeTooltip
callee builds the same rows without that event. The native host callback must
retain normal behavior, but remote capture must not invoke the event-producing
wrapper. Town walls returns a string, not property rows, and sets global text
variables newline/LEVEL. Accumulating-property helpers set EXTEND_KEY. Existing
MBTextManager transaction and language-processor restoration remain necessary.
Character/party filter and assign-member hints are already-prepared HintViewModel
texts; capture those texts instead of rebuilding permission logic on a peer.

The next tooltip path must issue opaque tokens bound to exact active overlay,
MenuContext, menu and successful current publication; revoke on session/save/menu/
owner change. Enforce source availability/visibility for each statistic before
native capture, revalidate afterwards, and keep peer generation/lease admission.
Do not reuse an unrelated encounter token for a notable or share host hover state.
Portrait tooltip dispatch must be audited separately from statistic rows.

Capture only the active native owner's prepared data atomically with the current
menu/session. Validate bounded labels, flags, counts, ordered parties/characters,
portraits and exact crafted dependencies. Render using detached stock bindings,
with local browsing and admitted tooltip payloads. Closing, leaving settlement,
encounter/raid transitions, conversation, modal settings, save reload and reconnect
must revoke stale panels. Verify village/town/castle separately, not village only.

The native findings above preceded b289. The candidate is now source/build/test
verified as described at the top; installation and native acceptance remain
pending until a fresh run record supersedes that status.
