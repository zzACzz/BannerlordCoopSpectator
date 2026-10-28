# Read-only aftermath screens

Latest2026-10-27 r63/b287: user accepts the previously missing main-hero pre-citadel
portrait and successful village battle/return. The detached unregistered resource
path is described in CAMPAIGN_CRAFTED_VISUAL_RESOURCES.md. This does not close the
cross-scenario/two-machine matrix. New manual findings: missing settlement top
overlay and network return deadlock after siege-ambush withdrawal; see the focused
documents linked from README. b287 remains installed until a fresh deployment.

2026-10-27 latest native r61: user confirms b281 equipment, row portraits and
independent prisoner inspection work; only the prebattle main-hero portrait remains
in that repetition. Campaign19124 and MP75056 loaded MVID
b6d1315a-36fb-4cf4-b38b-ba4e002f1f5a; DS26992 loaded
f1de1b13-0727-4cae-a453-d2dbb75c655d. Final logs retained. All roles exited, all7
configs restored/verified; Steam/saves preserved. This supersedes native-pending
b281 statements below, but does not accept the broad cross-scenario/two-PC matrix.
Source b283 carries only the bounded portrait-resource foundation; a client-only
native prototype was removed after numeric item-ID order risk was proven. See
CAMPAIGN_CRAFTED_VISUAL_RESOURCES.md. Installed b281 remains; no new test requested.

2026-10-27 newest native r60 result: user confirms siege/citadel prisoner screens
appear, citadel roster contains only the selected assault fighters, and citadel
Loot equipment looks correct. Supplied PNGs still show missing prebattle portrait,
naked prisoner model and missing hero row icons. All3 heroes have crafted weapons;
MP creates exact mirrors only when the citadel Battle opens. Raw logs/PNGs retained.
r60 CLOSED, all7 configuration hashes restored, Steam/saves preserved.

b281 source: full owned aftermath tree now uses exact crafted
aliases for equipment/row portraits. Independent prisoner selection is wired via
AftermathTroop replies, exact window/positive current-row admission, client-local
selection, suspension/removal/window revocation and a detached native preview.
No live PartyVM setter/transfer/recruit/upgrade/confirm command is invoked. Installed
PartyVM was freshly decompiled; preview mirrors its visual-only calculations while
omitting InitializeUpgrades, UpdateTalkable and live row cleanup callbacks.
Protocol67/bridge62, tooltip5/catalog10, CAF3/CML2. Both cmp-b281 builds, all26
cmp-c186 contracts and four detached checks pass. Independent prisoner rendering
and generic crafted alias visuals are NOT native-accepted yet. c185 failed a test
constructor compilation error; it was corrected and superseded by c186.

Prebattle crafted dependencies remain open; see
[resource investigation and next implementation](CAMPAIGN_CRAFTED_VISUAL_RESOURCES.md).
No resource creation/preload-before-battle fix or two-machine archive is claimed.
r61 has now staged nine coherent b281 files with seven configuration preimages at
`artifacts/runtime/map-prisoner-r61-20261027`. No game launched. Use its scoped
Start-Campaign-Replica.cmd, normal MP shortcut and a real citadel battle, then test
all hero row portraits/equipment and independent prisoner row clicks while the host
selects another row. Cold prebattle crafted portraits remain an acknowledged open
requirement; do not ask the user to treat that unchanged issue as a new regression.

2026-10-27 r59 CLOSED, all7 configs restored. Save-switch -> citadel -> map passed
on one machine with unchanged owned server PID. The prisoner failure is now proven
`aftermath-tree-invalid:byte-limit`. User also reports naked main-hero Loot preview
after citadel and full-army keep selection. Source b279 addresses these as follows:

- CAF3 uses canonical per-envelope string references, preserving every row/value
  under unchanged786432-byte/65536-value/1024-item/depth12 bounds. No truncation,
  decompression or enlarged native payload. Protocol66/bridge61 require coherent
  host/server/MP; tooltip5/catalog10 and CML2 remain unchanged.
- A600-row complete prisoner/loot fixture preserves the final row, all50 prepared
  properties and Unicode text below half the wire budget; undefined references,
  ordinary corruption, bounds and canonical roundtrip are checked.
- The saved roster proves main_hero has armor and crafted_item_0, mapped during the
  MP battle to cs_crafted_b2834eadf9bb7620. The old strict preview resolver cannot
  find the original crafted ID and therefore rejects the whole equipment code.
  Host detached loadout capture now reuses the existing battle fingerprint builder
  to project exact crafted aliases into its prepared mannequin/slot image data.
  It does not modify live inventory or create native items. Aliases are derived
  from the current item's design, never adopted by a reused save-local name.
- A validated LordsHall prebattle selector now requires its materialized cohort,
  waits if that cohort is not ready, and never merges the full campaign army.
  The same resolver is used by server request validation. Other scenario branches
  retain their existing behavior. This still needs native selection/possession proof.
- cmp-b279: both isolated builds pass and installed inventories were unchanged.
  cmp-c184: all26 contract projects pass; compiled mission-admission, detached loot
  session and aftermath-binding checks pass. These do not accept native rendering.

Installed is now b279 through r60: nine coherent files with seven config preimages
retained under `artifacts/runtime/map-save-reload-r60-20261027`. No game launched;
the prior user-started roles are closed. Use its Start-Campaign-Replica.cmd for the
scoped automatic-mission/capture flags; MP must use the user's normal shortcut.
Do not publish an archive
or claim the three reported issues fixed before the next native check. Independent
prisoner inspection and the older open fidelity gates below remain unfinished.

2026-10-27 override: manual r58 siege prisoner capture failed with repeated
`aftermath-tree-invalid`; loot did render. Its exact rejection remains unknown.
Save reload also exposed repeated expensive roster construction after owned-session
faulting. Source b278 implements reset/rebinding instead of permanent rejection;
both builds, all26 c181 contracts and detached checks pass, native acceptance pending.
See [save-reload investigation](CAMPAIGN_SAVE_RELOAD.md). r59 now stages coherent
b278 and campaign63908 is launched; user prepares the siege -> citadel repetition.
No native fix acceptance yet. This overrides the historical b274 entries below.

Current consolidated status and next manual test:
[2026-10-25 handoff](HANDOFF_2026-10-25_AFTERMATH.md). The dated entries below retain
investigation history; older candidates and pending statuses are superseded by the
latest closed run. The obsolete host-hover tooltip candidate near the end was
abandoned in favor of independent client inspection and is not a deployment input.

## Authoritative clarification — 2026-10-24

### Current checkpoint and native inspection boundary

2026-10-25 r57 CLOSED after user manual shutdown; fresh product inventory empty,
all7 config hashes restored/verified, final role logs retained. b274 remains installed.
First-fragment native acceptance below stands; broad objective remains incomplete.

2026-10-25 r57 NATIVE FIRST-FRAGMENT PASS. All26 c179 and detached checks passed;
nine coherent b274 files installed with seven config preimages. Campaign65360,
manual DS8848 and MP31584 loaded matching build MVIDs. MP shows actual first-fragment
thumbnail and full card: weight0.20, classBanner, effectNone, donationXP150, value2.
Host comparison matches orientation and card text. Iara remains selected locally
while host uses Halgard; subsequent equipped-helmet hover shows armor54/weight3.23
without observed model/slot disappearance. Bounded screenshots, not continuous
video. Evidence: r57/evidence/banner-visible-b274.png, banner-card-b274.png,
campaign-banner-card-b274.png. Other2 fragment native visuals remain unverified.
User elects manual shutdown; cleanup still pending fresh inventory/config comparison.
The old spinner is resolved for the observed first fragment only. Full aftermath
acceptance and broad Goal remain open: independent prisoner selection is unwired,
alternate item comparison/sorting/filtering and prior terrain/crash findings remain.

Read-only native PartyVM inspection reconfirms CurrentCharacter setter calls
RefreshCurrentCharacterInformation and ExecuteRemoveZeroCounts. The refresh also
mutates live rows through InitializeUpgrades/UpdateTalkable. Independent prisoner
preview must use owned HeroViewModel construction plus detached row presentation,
never call that setter/refresh or the live row initialization methods. Shared final
Loot state covers actual/simulated field, village, hideout and siege aftermath.

2026-10-25 r56 CLOSED after user shutdown. Fresh inventory contains no product
roles; seven config hashes match preimages. MP12812 retained handle exitFFFFFFFF
coincides with announced shutdown, not proof of a crash. User accepts b273 hover
continuity; agent point captures retain mannequin/slots and equipped Warlord Helmet
card (r56/evidence/stable-equipped-helmet-b273.png). Continuous video not recorded.
Dragon Banner First Piece still spins; stale Plains Battle hint remains.

b274 isolated source (native acceptance pending): append all3 StoryMode fragment
items to both mirrors without reordering existing entries. Exact source is installed
StoryMode/ModuleData/story_mode_items.xml. Original IDs dragon_banner_center,
dragon_banner_dragonhead, dragon_banner_handle map to cs_mirror aliases ending
a116ad33,57179aef,8d58eae8. Preserve banner component/flags/prefab/mesh; set aliases
and multiplayer_item only. StoryModeBannerEffects IL defines dragon_banner_effect
with all3 bonuses zero and increment type -1. Register this exact effect only if
absent, per MultiplayerGame, in InitializeSubModuleGameObjects after native defaults
and before item XML. Both client and dedicated use the hook; campaign is untouched.
Native Game/MultiplayerGame/MBGameManager call order inspected. Registration order
is runtime-sensitive; native launch/load and matching roles are required gates.
First fragment's original StringId controls native thumbnail rotation180; extend
the existing local pose resolver only, retaining registered object ID/cache key.
Managed substitute tests cover no campaign mutation, repeated/per-game initialization
and preserving existing effects; XML/pose contracts cover3 fragments. Both builds
and detached sessions/bindings pass; canonical all26 c179 running. Installed b273.
This does not fix multiplayer terrain or establish a cause for old silent exits.

Unused CoopCampaignAftermathTroopTarget now validates canonical current-window/
row tokens against exactly one positive-count row in the4 disclosed rosters.
Malformed/removed/duplicate/zero-count/wrong-kind cases pass contracts. Network,
native provider and client prisoner selection remain UNWIRED; not a delivered feature.

2026-10-25 all26 c178 passed after correcting the stale settlement transport version
fixture; r56 staged nine coherent b273 files with seven config preimages and campaign
launched. Native hover continuity still pending. Installed b273 protocol65/bridge60
supersedes older b271 installed statements below. No new crash/terrain fix claimed.

2026-10-25 r55 CLOSED all7 config hashes match. Native b271 equipped helmet card,
Cheese weight10/Trade Rumors layout and independent Yorig mannequin/slots/armor/
weight45.5 observed. User still sees brief model/slot disappearance on hover.
Bridge A/C diff isolates CurrentFocusedItem, IsFocusedOnItemList, item.IsFocused;
whole-root content comparison incorrectly treats those as equipment mutation.

b273 source adds ReplicaLoadoutVersion on each disclosed hero row and loadout
response, computed from3equipment12slot item/modifier codes, faction colors and
FormFittingArmor perk. Capture/provider revalidate; client invalidates only selected
hero version changes, checks response version, rejects late stale equipment. Native
equipment string-builder/getters inspected; no tableau reads or new diagnostics.
Same hero/mode echoes no longer revoke preview. Protocol65/bridge60 requires coherent
roles; tooltip5/catalog10/CAF2 unchanged. Both isolated builds and detached checks
pass. Focus-only changes stay visible and actual version changes hide/re-request in
tests; native acceptance pending. all26 c177 running, installed b271 unchanged.

2026-10-25 all26 c176 passed; r55 staged nine coherent b271 files and seven config
preimages, campaign launched. Native validation of blink/layout/gear targets is
pending. Installed b271 supersedes older installed-b268 statements below.

2026-10-25 r54 CLOSED, all7 config hashes restored. Native b268 now renders client
Iara model independently while host retains Halgard; Cheese card appears. Native
fidelity fails: brief mannequin/equipment disappearance on hover, missing Weight10
and narrow Trade Rumors wrapping, absent equipment-slot cards. Comparison PNGs in
r54/evidence. Dragon Banner spinner/stale Plains Battle hint remain. No new crash.

Source b271 (not installed): compare validated snapshot contents rather than object
identity before invalidating local preview; identical re-publication retains it,
actual shared-state mutation still revokes. Different item hover constructs a fresh
owned ItemMenu projection so native property widgets do not inherit another row
shape's size policies. This is a candidate layout fix, not native proof. Also sends
selected hero armor5/encumbrance and native faction/stealth colors using installed
SPInventoryVM read-only formulas; no live selection setter is called.

Additive `gear:<window>:<hero>:<mode>:<slot>:<usage>:<offset>` targets use existing
AftermathItem envelope (protocol64/bridge59/tooltip5 unchanged). Slot0..11/canonical
numbers/current Loot window and unique hero are mandatory. Host reconstructs owned
equipment SPItemVMs for that exact hero/mode, rejects empty slots, revalidates owner,
hero/equipment reference and all12 elements. Never borrows host-selected slot row
or authorizes mutation. Native independent equipment cards pending. Both isolated
b271 builds and detached binding/session checks pass; map contracts pass. All26
c176 running. Installed remains b268; no product roles remain.

2026-10-25 r53 CLOSED with all7 config hashes restored after user shutdown. Native
b266 changed independent equipment AC ->Niasen but mannequin/card stayed absent;
diagnostics show matching-card, so delivery is proven while rendering failed.
b268 applies visibility within MainCharacter and ItemMenu child contexts rather
than relying on parent VM property lookup. Both isolated builds, detached binding/
session tests pass; new assertions fail b266. r54 staged nine files/seven preimages,
native acceptance pending. Protocol64/bridge59/tooltip5 unchanged. No silent-exit or
terrain fix claimed. Independent slots, prisoner preview and weight remain open.

2026-10-25 b266 connects independent hero visual transport and client application.
AftermathLoadout=11, tooltip codec5, protocol64/bridge59, shared protocol compression
maximum127 (7 bits). Client/server/campaign must be installed coherently. Catalog10
and CAF2 unchanged. Publisher admits hero-only loadout via existing fresh session/
publication/lease target gate, builds owned model/12 slots and correlates exact
window/hero/mode. Codec rejects mismatched metadata, extended aliases and item-kind
impersonation. Client retains local hero choice; pending/replaced visuals are hidden,
late prior-hero replies rejected, local overrides prevent host-selected model from
overwriting independent preview. New atomic host snapshots revoke cached equipment
and renew request generation; modal/hero/mode changes revoke the projection too.
No client campaign mutation is introduced. Native helper/rendering acceptance OPEN.

b266 both builds, full map contracts and detached binding/session tests passed.
Tests include empty loot lists, wrong/removed/duplicate heroes, changed screen,
response correlation, name/model separation, host updates, prior-hero reply and
modal suspension. Initial full map checks caught explicit version fixtures and
the exhausted6-bit protocol range; all readers/writers now share7-bit range.
No silent-exit fix or native card acceptance claimed. Independent equipment-slot
tooltip admission and selected-hero weight/stats remain open; prisoner preview is
still unwired. r53 prepared for coherent staging after all26 c175; installed b262
unchanged and r52 closed. Corrected copied BuildRunId labels in r51/r52 to b262;
source paths/SHA256 always identified the actual staged b262 files.

b264 extends the unwired hero visual provider with12 ordered equipment-slot VMs
(weapons0-3,banner4,head5,body6,legs7,hands8,cape9,horse10,harness11). Uses owned
SPItemVM instances with current admitted inventory logic for prepared native labels/
images/costs, default SPItemVM for empty slots. CapturePrepared(registerRows:false)
recursively suppresses live row registration; each detached slot carries only its
ReplicaSlotIndex, never a transferable host-row identity. The normal screen capture
still registers real rows. Owned VMs finalize and existing restoration scopes run.
Native constructor/helper transitive safety remains unaccepted, no runtime entry
is connected. Network/UI projection, selected-hero weight/stats and slot-interest
admission remain open. Both isolated builds and detached binding/session tests pass;
new installed-library test captures an empty slot without any active row window and
proves no ReplicaRowId is allocated. Installed r52 immutable b262 unchanged.

Independent hero foundation b263 (source only): a separate canonical loadout
target carries window/hero/mode without requiring an item row. Strict parse and
disclosure tests cover empty loot lists, duplicate/removed heroes, replaced windows,
wrong screen kind and noncanonical modes/identities. Not network-admitted yet;
protocol/bridge remain63/58 and installed r52 is immutable b262.

Unwired TryPrepareLootHeroVisual builds an owned HeroViewModel with FillFrom(hero)
and SetEquipment for independently selected battle/civilian/stealth equipment,
within existing owner-thread/reentry/text/language scopes. It revalidates the exact
live screen/owner/hero row, equipment reference and every slot before returning
MainCharacter/hero-row/mode. No live SPInventoryVM selection or transfer command is
called. Native/transitive helper safety and visual fidelity remain unverified.
Equipment slot projections and per-client request/response/UI lifetime still must
be connected; this is not a completed independent loadout feature.

r51 native partial result: manual client28540 connected after automatic39236 failed
Steam login. Equipped Prisoners mannequin/portraits and most Loot images now render
correctly; Dragon Banner First Piece still spins. Independent item cards still do
not appear under agent or user input. User and agent confirm local hero arrows
change only name (Yorig -> Iara), while model/equipment remain host-selected.
Stale Plains Battle hint overlays aftermath. These are open fidelity requirements,
not accepted behavior. No diagnostic trace available on manual launch; do not infer
where hover fails. Run remains live, observer session46811 tracks manual client.

r51 installed b262: nine coherent files staged and seven config preimages saved.
b262 adds per-owned-node cache keyed by raw value/provider to b261 resource
projection; unchanged snapshots no longer repeat object/portrait helper calls.
Both builds and detached binding/session checks passed, all26 c174 contracts passed.
Native acceptance remains pending. No declaration that missing cards or crashes
are fixed. Scoped MP diagnostic launch must be verified by session-created log.

Source b261 resource projection: detached aftermath VM resolves EquipmentCode via
the strict12-slot equipment grammar; ItemImageTextureProvider IDs use existing
native items or verified preloaded mirror aliases; CharacterImageTextureProvider
codes reuse the existing conversation portrait validation/resolution. Only client
presentation values change; received DTOs/host state and row identities remain
unchanged. Missing resources fail closed, never synthesize a partial equipped code.
This applies to prisoners, loot and detached item-card subtrees across simulated/
actual field/village/hideout/final siege aftermath. No mission/battle policy changes.
Both isolated builds, full map contracts and detached binding/session tests pass;
not installed/native accepted. Renderer/transitive native portrait helper behavior
still requires game verification; no extra runtime diagnostic native reads exist.
The underlying equipment resolver was extracted from the existing portrait codec,
with additional strict standalone slot/missing-resource/duplicate-slot tests.

r50 CLOSED 2026-10-24 08:26 UTC after user announced game shutdown. All seven
configuration hashes restored, no product roles found, Steam/saves preserved.
Installed b256 retained. Native simulated-battle Prisoners -> Loot transitions
worked, but manual hover/click also failed to show independent item cards. Most
item images remained spinners and equipped portrait/mannequin visuals lacked
equipment. A stale Plains Battle tooltip overlaid both aftermath screens.
This is partial evidence, NOT crash-fix acceptance. Exact client observer recorded
exitFFFFFFFF during announced user shutdown; do not call it a reproduced crash.

Source b260 adds COOPSPECTATOR_AFTERMATH_INSPECTION_DIAGNOSTICS=1 opt-in to the
local inspection session only: at most32 transition logs per session, no native
reads or identifiers/values, disabled by default. Logs distinguish session creation,
missing hover row/hero, disclosure rejection, pending/unmatched/matching card.
Both isolated builds and detached binding/session checks pass; not deployed.
Initial b259 runner attempt failed CIM access before compilation; b260 ran elevated.

Installed native ItemImageIdentifier puts ItemObject.StringId directly in Id;
HeroViewModel emits Equipment.CalculateEquipmentCode. Existing map/menu/conversation
paths resolve campaign-only IDs to preloaded cs_mirror aliases, whereas generic
aftermath binding projection currently does not. This is a source-backed resource
mapping gap to fix/test for both screens and item cards, not proof of the crash cause.

Runtime update 2026-10-24 r50: r49 is closed, seven configuration hashes restored
and no owned processes remain. Nine coherent b256 files are now installed, seven
new configuration preimages retained. Campaign17596 loaded b256 MVID
2184223e-4c27-4dba-b9d9-284bbed1135d, SHA256
305473E2531A18F99D141EB222227190CF8F10A9DEBD88FBF301A70C9B43DFE5.
User connected MP29524; an exact-identity process handle was retained before exit
for exit-code observation. Native reproduction/acceptance is pending. These facts
supersede the earlier installed-b254/server39208 preparation-only notes below.

Prisoner preview source-only b258: TryResolvePrisonerRow checks exact active Loot
PartyState/top screen/owner token, positive count, native character and unique live
membership across all four party lists. An unwired
CoopCampaignMapTooltipCapture.Prisoners.cs provider builds an owned HeroViewModel,
prepared current-row clone, level/wage/tier and formation/wage enablement. It does
not call the live PartyVM selection setter, InitializeUpgrades, UpdateTalkable,
ExecuteRemoveZeroCounts, recruit/execute/transfer commands or static row callbacks.
It revalidates current ownership/row and uses existing owner-thread/non-reentrancy
and text/language restoration scopes. Body/equipment/banner/color setup matches
the audited native preview sequence. Native/transitive helper safety, rendering,
hidden-character behavior and independent client delivery remain unverified.

b257 failed because native tier data is StringItemWithHintVM rather than text;
b258 captures that typed VM correctly and both isolated builds pass. Detached
binding and loot-session tests also pass; they do not execute this provider.
No native entry point is connected, no protocol or installed module changed.
The prepared crash-feedback test candidate remains immutable b256, not b258.

Independent prisoner inspection research (installed native source, 2026-10-24):
PartyCharacterVM.ExecuteSetSelected calls the static SetSelected delegate wired to
the live PartyVM. PartyVM.CurrentCharacter's setter calls both
RefreshCurrentCharacterInformation and ExecuteRemoveZeroCounts. The refresh also
invokes InitializeUpgrades and UpdateTalkable on the live row. Therefore none of
these methods is an isolated read-only preview API; do not call them for a client.

The native center preview is a separate HeroViewModel: FillFrom(hero) for heroes,
or FillFrom(character, deterministic StringId hash, owner banner) for regular
troops, followed by SetEquipment(character.Equipment). Owner faction armor colors
apply only to non-prisoners. Level/wage/tier/formation enablement are separate root
PartyVM properties, not all contained in the row. A future per-client projection
must build owned preview data and those exact labels using admitted row/side/type,
without switching the host selection or invoking the live row refresh. Four-list
membership, zero-count removals and screen replacement must revoke it. This applies
after simulated/actual field/village/hideout/final siege outcomes alike.
No prisoner preview implementation or new native acceptance is claimed here.

Second r49 failure: user selected Send troops rather than entering a battlefield.
MP58416 still exited silently around10:31:55 during menu/tooltip chunk receipt;
watchdog again records process exit without exception. Thus a battlefield mission
is not necessary for reproduction. User previously confirmed prisoner display.
Exact hover timing/cause remain unknown; do not call this a proven topology or
scene-load regression. Logs are retained under local r49 evidence.

Source investigation found a separate demonstrable binding-feedback defect:
rejected SetPropertyValue writes always re-notified, permitting synchronous
coercing widgets to reenter the setter indefinitely. Stock GauntletView forwards
widget property changes to IViewModel.SetPropertyValue; InventoryTooltipInnerContent
uses RadioContainerWidget.SelectedIndex <-> AlternativeUsageIndex, and that widget
also synchronizes Container.IntValue. A bounded synthetic coercion test fails on
b255 (capped at10 notifications, no deliberate stack overflow). b256 adds a
per-property reentry guard and suppresses equal-value echoes; the test passes with
at most one notification while preserving the received value. This is NOT yet
proof of the silent-exit root cause. Both b256 isolated builds and detached binding/
session tests pass; installed b254 is unchanged. The shared VM correction applies
to both aftermath screens after simulated and actual field/village/hideout/final
siege outcomes. Do not request more blind user repeats of b254.
Follow-up binding checks also pass for equal-value silence, guard release after
each rejected write and subsequent authoritative host-value propagation. No native
module changes were made for these additional checks. Fresh exact process lookup
still finds user server39208; r50 remains preparation-only pending shutdown consent.

r49 latest bounded result: user confirms prisoner screen displayed, but MP56440
exited during later menu/tooltip receipt. Watchdog has no exception event and no
new dump/Windows crash record was found; hover timing is uncertain. Cause is UNKNOWN,
not a proven renderer/provider crash. User confirms closing campaign manually.
See r49 retained logs; cleanup remains pending during coordinated repeat testing.

Source-only b255 isolates client session channel dependencies for testing.
Test-LootInspectionSession.ps1 passes actual local command -> interest -> matching
card display, stable hover, prior-item late response rejection and host-removal
revocation. It constructs no GameNetwork, mission or native campaign. Both b255
isolated builds pass; installed b254 remains unchanged. These tests do not prove
native card generation/rendering or explain the unexpected client exit.

r49 IN PROGRESS on coherent installed b254: nine files staged, seven configuration
preimages retained. Campaign56904 loaded3fa222d3-d3a0-4dac-9a45-902fad3b5875 and
Continue Campaign reached the paused map near Vostrum/Lavenia. Injected Escape did
not open pause; server/MP are not launched and loot native acceptance is pending.
No Steam/save changes. Cleanup responsibility remains open in local r49 artifacts.
All earlier installed-b240 statements below are historical.

Native XML command dispatch correction b254: installed GauntletView.OnCommand
converts Widget arguments to their bound VM, then casts the target to ViewModel
and calls its nonvirtual ExecuteCommand. The earlier new/IViewModel-only dispatcher
was therefore insufficient for native XML commands. CoopCampaignAftermathVM now
exposes explicit methods only for hover/reset, character previous/next and equipment
mode selection. There are still no transfer/equip/buy/sell/Done endpoints.
Installed-library tests invoke ViewModel.ExecuteCommand directly and prove exact
detached-row delivery and no transfer endpoint. Both isolated b254 builds and the
binding harness pass without installed changes or game startup. Native visual
acceptance remains open. Computer Use was initialized and returned no game windows;
no app input or product launch was issued in this investigation.
Fresh aftermath-dispatch-c172-20261024 passed all26 canonical contract projects.
This confirms regression contracts, not native hover/render/provider acceptance.

Client wiring candidate b253 (supersedes the producer/consumer-pending statements):
CoopCampaignLootInspection owns local hero token, equipment mode, hovered item and
detached ItemMenu. Whitelisted inspection commands/setters never invoke campaign
actions. Host shared-list publications still apply to the received VM tree; local
overrides survive them. Current token membership is rechecked every tick, and only
an exact matching AftermathItem response opens the stock-positioned tooltip.
Modal suspension, hover exit and teardown clear interest and hide the card.
Map tooltip presentation now suspends without clearing the inventory-owned interest.
CoopCampaignLoot.xml binds visibility to locally received card readiness rather
than the host hover. The older host-hover prefab remains unused/unreferenced.

b252/b253 both isolated builds pass with installed inventories unchanged. Detached
binding tests pass for local override isolation/release, existing row identity and
typed native tooltip property capture. No native hover dispatch has been exercised:
InventoryScreenWidget command argument conversion to detached row must be confirmed
in-game; unknown arguments fail closed. Independent hero mannequin/equipment panel,
alternate usage/comparison cycling UI, filtering/sorting/search, prisoner inspection
and broad native safety/fidelity remain open. Do not claim complete independent
inspection or stage this as an accepted finished feature. Installed b240 unchanged.

Per-peer transport source integration (supersedes earlier unwired-provider notes):
CoopCampaignAftermathInspectionTarget canonically packs three opaque GUID identities
and view parameters into the existing 96-character interest field without relaxing
the bound. Dedicated/client admission requires current Loot item and hero membership
in the published tree. Publisher repeats fresh session/lease admission before live
host resolution and invokes the detached provider. Tooltip format4 adds AftermathItem
with a bounded CAF2 card; response window must match the interest window, and other
payload kinds cannot carry a card. Existing per-peer challenge/lease/generation/
sequence/chunk handling remains in force. Protocol63/bridge58 require all roles rebuilt.

Full map contracts pass, including canonical token roundtrip, malformed tokens,
current membership, host removal, full card roundtrip and wrong-window rejection.
b251 both isolated builds pass and installed inventories are unchanged. This has no
client hover producer or card consumer yet and is NOT working native inspection.
Native provider execution, full two-peer behavior and visual comparison remain open.

Comparison candidate follow-up (supersedes the base-card-only limitation below):
the unwired host provider now accepts explicit equipment mode, alternate weapon
usage and comparison offset. It reads only the admitted hero's civilian/battle/
stealth equipment, builds owned SPItemVM slot projections and passes them to the
detached ItemMenuVM. Armor uses its corresponding slot; weapon candidates use the
native ItemHelper comparability check and audited stable native precedence: exact
lock identity, same item type, alternate weapon-class match, other compatible slots.
Equipment-side targets suppress comparison as native ProcessCompareItem does.
The host ProcessCompareItem and character-selection commands are never invoked.
Before return, provider revalidates hero/item membership, equipment reference and
all captured slot elements. Owned slot VMs are finalized inside the existing text/
language restoration transaction. No callbacks are invoked to equip or transfer.

Full map contracts pass including comparison priority/selection bounds. b249 both
isolated builds pass; b250 verifies final policy integration and equipment identity
revalidation. Native SPItemVM construction, localization helper transitive effects,
comparison cycling and actual card fidelity still require controlled runtime
validation before this provider can be claimed safe/accepted. Per-client networking,
hover, selection and card application remain unconnected; installed b240 unchanged.

Hero admission follow-up: capture now gives InventoryCharacterSelectorItemVM nodes
opaque row tokens. TryResolveLootHero requires the exact current screen owner,
window and current CharacterList.ItemList membership with a living hero. The
unwired provider requires an explicit hero token and sets only the detached card's
character context, never the host selector. Item resolution additionally checks live
Left/RightItemListVM or exact equipment-slot reference and a nonempty positive
count, closing the gap between last publication and a subsequent host transfer.
Both hero and item membership are revalidated after generation. b247 both builds
pass for the admission changes; b248 checks the explicit hero context. Equipment
comparison/slot projection, native validation and network delivery remain pending.
This common Loot-state admission applies to all supported final aftermath types.

Unwired host projection candidate:
`CoopCampaignMapTooltipCapture.Aftermath.cs` provides TryPrepareLootCard, on the
existing capture owner thread/non-reentrancy gate with text and language restoration.
It resolves an exact current Loot screen/window/SPItemVM reference, constructs a
separate ItemMenuVM, and invokes only RefreshItemTooltips after assigning detached
target metadata. It never calls ProcessItemTooltip/SetItem and thus does not invoke
their selection/transaction/slaughter/event side effects. Native usage flags come
from the admitted screen's existing provider, not fabricated zero flags. Reflection
shape mismatch fails closed. Screen and row identity are revalidated after capture.
No remote/runtime caller is connected. This currently prepares only a base card;
independent hero/equipment comparisons, alternate usage, per-peer admission and
delivery remain incomplete. It must NOT be deployed as full tooltip parity.
b245 compiled the initial projection; b246 compiled both roles with the real usage
provider binding and proved installed inventories unchanged. An attempted off-game
provider invocation first failed dependency loading, then the existing capture
type initializer; it is NOT a passing projection test. That probe was removed from
the detached harness rather than initializing engine state to force a pass.
Detached binding tests remain separate. The admitted provider has not executed;
no game startup occurred. Native provider validation remains mandatory.

Source follow-up adds opaque ReplicaRowId bindings to exact SPItemVM and
PartyCharacterVM nodes. A window-local reference registry preserves identities
through reordering and invalidates removed rows and replaced/closed windows.
Removed objects reintroduced later receive fresh tokens. This does not yet expose
an inspection endpoint, invoke native providers, or enable client commands.
Full map contracts pass, including the new identity lifetime cases; the initial
restricted run reached the new tests but failed a later local-channel access check,
and the elevated rerun passed the complete executable. Native acceptance is pending.
Isolated aftermath-rows-b243-20261024 compiled both roles and verified installed
module inventories unchanged. Candidate remains source/build/test-only, not deployed.

Client follow-up b244 reconciles identified lists by ReplicaRowId rather than index.
Surviving detached row VMs retain reference identity across reorder; host changes
update those same objects, removed rows finalize before replacement, and old held
references cannot silently become a different item. Duplicate/mixed identities
fail closed before list reconciliation. Ordinary unkeyed tooltip-property lists
retain index reconciliation. Both isolated builds pass without installed mutation;
installed-library detached tests verify reordered object references, updated values,
removal, reintroduction, invalid identities and empty-list revocation without game
startup. This is selection-identity infrastructure, not working local hover/hero
inspection or native UI acceptance. The same detached list path serves all admitted
aftermath scenarios; no battle-type branch was introduced.

r48 is CLOSED. Its cleanup.json timestamp 2026-10-24T01:37:11.5055914Z records
AllConfigurationsMatch=true and no remaining owned identities; seven configuration
preimages were restored/verified. Installed b240 is retained; source b242 was not
deployed. Both isolated b242 builds pass after unused GauntletUI.Data references
were removed. This is not native independent-inspection acceptance.

Fresh installed-assembly decompilation of SPInventoryVM and ItemMenuVM confirms:

- ProcessItemTooltip changes the host's _selectedItem, compares against the host's
  current character, refreshes transaction cost, and updates slaughter eligibility.
  It is not a read-only remote inspection API and must not serve client requests.
- ItemMenuVM.SetItem builds prepared property rows but also triggers
  InventoryItemInspectedEvent through Game.Current.EventManager. Calling it on a
  separate VM does not by itself isolate campaign side effects.
- Its constructor requires Campaign.Current's trade-rumor behavior; property
  generation uses InventoryLogic, settlement/market data, language variables,
  character and comparison equipment. Constructing it on an MP client is invalid.
- Alternative usage changes can call the supplied reset-compared-items delegate.
  No delegate may route remote inspection back into the live host screen.

The next implementation must isolate host-side presentation generation, including
event/language side effects and exact row/hero admission, rather than replay native
host hover commands. Preserve all prepared property rows, modifiers, trade rumors
and comparisons; do not substitute a smaller generic item tooltip as visual parity.
Apply the same window/session invalidation after field, village, hideout and final
siege aftermath; no new battle-type-specific exception is justified by this finding.

The user explicitly requires independent client inspection of loot and prisoners.
Host transfers and shared-list changes must replicate to everyone, but host hover,
selection and tooltip visibility must NOT control other players' inspection.
The b240 host-hover movie was a misinterpretation, not the desired design.
Source now removes host widget visibility/position capture and that movie's runtime
loading. The old unreferenced prefab is retained temporarily; do not stage or use it.
Independent local inspection is NOT implemented yet: current detached commands
remain inert, and native local hover must not display the host's last ItemMenu as
if it belonged to a different item. Do not declare this requirement complete.

User paired screenshots proved missing tooltip rows. Offline live decoding showed
eight TargetItemProperties nodes with only modifier/hint fields. Installed native
ItemMenuTooltipPropertyVM derives from TooltipProperty, whose prepared labels,
height/color and layout flags have no DataSourceProperty attributes. Source now
explicitly reads those backing-value getters without RefreshValue/RefreshDefinition.
CAF2 adds typed packed UI color; protocol62/bridge57 require coherent new roles.
b241 isolated builds and full map contracts pass. An installed-library test creates
only a detached native tooltip row, confirms base labels/layout/color capture and
typed-color client reconstruction; no game/engine/screen starts. Native independent
inspection and terrain regression remain open; installed b240/r48 is unchanged.

Next implementation must use window/row-bound identities and per-client inspection
state. Requests may inspect only rows in the current authoritative screen and must
never invoke transfer, equip, sell, buy, Done or campaign-edit commands. Comparison
data must correspond to the client's inspected row/hero, not the host's hover.
The shared host screen continues to own all mutations. Earlier host-hover candidate
sections below are historical experiments superseded by this clarification.

## Scope and native evidence (2026-10-23)

The user requested campaign prisoner/troop disposition and loot screens on all
replica clients. Host decisions remain authoritative. Trading, remote transfers,
inventory reservations and hero editing are outside this increment.

Installed SandBox.GauntletUI maps PartyState to GauntletPartyScreen and InventoryState
to GauntletInventoryScreen. Their existing _dataSource fields own PartyVM and
SPInventoryVM, loaded into PartyScreen and Inventory movies. Both are full game
states rather than GameMenuVM map menus. Public PartyState.PartyScreenMode and
InventoryState.InventoryMode distinguish Loot from normal/trade/garrison/ransom.
Both Loot enum values exist in Helpers in CampaignSystem. Read the existing VM
bindings; never construct native campaign VMs or invoke their commands on a client.

## Current implementation candidate

- CoopCampaignAftermathCapture admits only the exact Loot state, exact top screen,
  exact native VM type, and revalidates screen/state references after capture.
  It copies primitive annotated binding properties and bounded nested VM/list data.
  Unsupported objects, back references and non-binding campaign models are omitted.
  Tooltip delegates are never invoked. A fresh VM owner gets a fresh window token.
- CoopCampaignAftermathPresentation/Codec carry a typed, detached tree with explicit
  depth/value/list/text/byte limits, canonical sorted property names and finite
  numeric values. No reflected CLR type names, methods or delegates cross transport.
  CAF1 sits inside CMN8 using existing atomic menu chunking, freshness and revision
  handling. Protocol61/host bridge56 require coherent rebuilt roles.
- Publisher anchors to a successfully published map from the same campaign/session.
  While Loot states are active it publishes the anchor plus live aftermath bindings,
  clears world motion/markers and bar, and preserves current publication freshness.
- After final result application, automatic host intent treats admitted Loot states
  as map-backed presentation so clients return before the host dismisses the screens.
  Applied battle key/final-stage gates remain in force for siege/hideout stages.
- CoopCampaignAftermathVM supplies detached IViewModel bindings to copied stock
  PartyScreen/Inventory XML (CoopCampaignPrisoners/CoopCampaignLoot names force XML
  fallback rather than native generated bindings). No campaign command targets exist.
  Host selection and list changes update child nodes in place. Client setter requests
  are ignored and rebound to the received value. Local modal menus suspend the layer.
- The fullscreen presentation blocks map camera/picking underneath. Close, stale
  host state, conversation and mission teardown release the movie/VM/sprites.

## Validation and open gates

Map contracts passed for localized tree roundtrip, nested copy, existing interface
envelope, mixed-screen rejection, identity, truncation/trailing bytes, unknown kind,
binding-path injection, cycles, fractional integers and oversized lists. Isolated
b232 failed on a missing InputUsageMask import; corrected b233 compiled both roles
with installed inventories unchanged. Installed-library detached binding checks
pass for IViewModel dispatch, nested list paths, row updates, ignored setter and
command requests, removal and repeated teardown. The original Add-Type harness
failed reference-profile resolution and was replaced with direct reflection.
No candidate deployed or native visual acceptance.

Subsequent r45 preparation staged nine coherent b233 files and retained seven
configuration preimages. Campaign PID41040 loaded client MVID
813900c2-f526-48a8-8f1b-8f7796a2b55d. Agent loaded saveauto3 and closed the raid
tutorial; injected Escape did not open pause, then the helper reported manual
input. Input is handed back to the user for server/join/battle preparation.
No prisoner or loot screen has been observed yet; full Goal remains open.

Subsequent user field victory reached GauntletPartyScreen in the host log, but
b233 publication failed with the generic aftermath-screen-unavailable reason.
Physical Escape stopped Computer Use before a prisoner screenshot was obtained.
The source follow-up uses the native public state modes and distinguishes anchor,
screen, source, capture, identity and validation failures through the publisher's
existing throttled failure report. Exception reporting contains only stage/type,
not values or native-object inspection. This is not an established root cause or
a native-accepted fix; current installed b233/r45 remains unchanged.

Isolated b234 did not reach compilation because sandbox CIM access failed.
Elevated isolated b235 compiled client and dedicated successfully and verified
installed module inventories unchanged. No deployment, restart or native acceptance
was performed for this follow-up. It changes neither the wire schema nor DTO bounds.

Later r45 closed after user shutdown: no game roles remained and all seven
configuration hashes matched after inspected physics-rate/server-map restoration.
r46 then staged nine coherent b235 files, retained seven configuration preimages
and launched campaign PID37500. Loaded identity and native screen acceptance
remain pending. Steam/saves preserved; r46 cleanup is open.

r46 follow-up: campaign loaded MVID6223fc73-34ee-4552-bc40-bdbeb62a0391.
User completed a field victory. Computer Use confirmed the native Party screen
with 3 rescued troops, 16 wounded prisoners and the host party; multiplayer showed
only the map with no aftermath panel. Host bridge carried a live CMN8 aftermath:1
tree, so capture succeeded. Dedicated log at 18:49:32.429 reported
`host state dynamic-unavailable:stale`: the new map controller requires a fresh
dynamic catalog, but the aftermath anchor retained its pre-battle timestamp.
This independently blocks menu forwarding before client movie creation.

Source correction renews only the retained dynamic snapshot's publication time,
checks exact session/dynamic revision/catalog revision against the map anchor,
and marks it for writing before the main snapshot. It does not recapture unseen
world entities or weaken freshness admission. This path is shared by prisoner
and loot screens after final field/village/hideout/siege results. b236 failed on
an invalid CatalogRevision member on the dynamic DTO; corrected to check the
cached catalog separately. Build and native acceptance of this correction pending.

Corrected b237 compiled both roles with installed inventories unchanged. Full map
contracts passed, including an isolated bridge regression: a five-minute-old
dynamic snapshot is rejected, renewal admits the same session/revision/entity data,
and expiry still rejects it three seconds after publication stops. This verifies
bridge freshness behavior, not the native publisher branch or rendered UI. The
first restricted test run was denied local-channel access; elevated isolated tests
passed. Installed b235/r46 and its running roles remain unchanged pending user
restart permission; no native acceptance is claimed for b237.

Offline live-bridge decoding of r46 passed: revision3765, aftermath:1, 94 root
properties, nine prisoner rows and thirteen host troop rows. The interface encodes
to 90,839 bytes, within bounds. SelectedCharacter was null although the native
screen visibly displayed Yorig. Installed metadata confirms HeroViewModel belongs
to the parent CampaignSystem.ViewModelCollection namespace, previously excluded by
the capture filter. The source follow-up explicitly admits only that type and
Input.InputKeyItemVM; it still reads annotated presentation getters only and never
copies native models, commands or delegates. Native character fidelity is pending.
Both isolated b238 builds passed with installed inventories unchanged. b238
supersedes b237 as the next candidate; installed b235/r46 remains live and unchanged.

Subsequent authorized restart closed r46: all three exact game roles exited and
seven configuration hashes matched after inspected physics/server-map restoration.
r47 staged nine coherent b238 files and retained seven configuration preimages;
campaign PID54000 launched2026-10-23T16:11:36.4882044Z. b238 is now installed;
loaded identity and native screen acceptance are pending. Steam/saves preserved.

## r47 bounded native observation

b238 campaign loaded identity matches09e5e23a-ea89-4da5-bd04-abc21676bd50.
User completed a field battle and retained prisoners. Server accepted authoritative
state after return; MP now renders the same visible prisoner rows, counts, portraits
and selected hero as campaign. Client Done did not advance the screen. Host Done
opened a leave-prisoners confirmation; user confirmed manually after UI coordinate
auto-review rejected automation. The confirmation itself is not replicated.

Loot opened in both roles with matching visible names/quantities/prices, capacity,
gold, equipment and XP text. Two MP banner images remained loading in the observed
frame; host comparison tooltips were absent on MP. Neither feature is full1:1
accepted. Host transfers, filtering, scroll, modal/settings transitions, reconnect,
repeat and non-field scenarios still require testing. r47 cleanup remains open.

Separate field renderer regression reported: MP ground appeared black/dark-blue in
battle_terrain_a (patch220). User screenshot/log retained in r47 evidence. No cause
or fix established; do not conflate this with the now-working aftermath forwarding.
User confirms campaign ground was normal. r47 subsequently CLOSED after user
shutdown: no game roles remain and all seven configuration hashes match after
restoring the inspected generated dedicated-map config. Steam/saves preserved;
b238 retained. Full native fidelity and broader regression gates remain open.

## Prepared loot-tooltip candidate

Native InventoryTooltip.xml binds ItemMenu but defaults IsHidden=true. Installed
InventoryScreenWidget changes its visibility/offsets directly on hover, outside
the annotated VM graph. The copied root therefore carried tooltip text but could
not mirror host visibility. Source follow-up reads the native inventory movie's
prepared managed InventoryTooltip widget and adds ReplicaTooltipHidden/X/Y values
to the detached tree. No hover commands or item-action delegates are invoked.

CoopCampaignLootTooltip is a distinct copied stock tooltip movie with visibility
and UI-unit offsets bound to those values. It shares the detached root, suspends
with the aftermath layer and releases before its owner. The original local-hover
tooltip remains structurally present for InventoryScreenWidget references but is
permanently invisible, preventing a local hover from showing stale host content.
GauntletUI.Data is an explicit non-copying client reference for IGauntletMovie.
No wire schema change: CAF1 already carries these bounded presentation scalars.

Initial b239 compilation required the explicit GauntletUI.Data reference; b240
compiled both roles with installed inventories unchanged. Full map contracts pass,
including tooltip scalar roundtrip. Both XML files parse, the custom tooltip root
exists and the local tooltip is explicitly invisible. These are not native tests.
Native visibility, comparison left/right ordering, different resolutions/UI scale,
closing/reopening and absence of local-hover duplicates remain acceptance gates.
This candidate does not fix the separate terrain regression. r47 is closed and
installed b238 remains unchanged. Deployment must include the new tooltip prefab;
old nine-file staging scripts are insufficient for this candidate.

Subsequent r48 staged ten coherent b240 files after fresh process inventory found
no game roles. Seven config preimages retained; campaign PID46252 started
2026-10-23T16:49:42.3586960Z. Native tooltip checks/loaded identity and cleanup
remain open. The dark-terrain issue has no fix in this candidate.

Required native sequence: map -> final field battle -> prisoner Loot state ->
inventory Loot state -> map. Compare both lists, quantities, selected character,
equipment/portraits, host transfers, filters and closing. Repeat for village,
hideout final stage and siege final stage; intermediate stages must not open map.
Check client clicks/hotkeys cannot transfer or confirm host items/troops, local
settings, reconnect while each screen is open, stale/closed state and repeat battle.
Scroll positions and hover tooltip presentation need explicit visual verification;
primitive binding transmission alone is not proof of 1:1 parity.

Documentation impact: CODE_MAP, ARCHITECTURE, RUNTIME_FLOWS, INVARIANTS_AND_RISKS,
README and test runbook describe this source candidate. Siege flight acceptance
and the previous b231 installation remain unchanged until a coordinated fresh run.
