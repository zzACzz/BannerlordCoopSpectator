# Code Map

b302 source-only reward details: CoopCampaignAutoBattleRewardCapture inspects
audited exact native captured ExplainedNumber fields, scopes cache to the current
scoreboard, and appends prepared CAS1 rows. CoopCampaignMapTooltipCapture.AutoBattle
reuses text/language isolation for native formatting. CoopCampaignAutoBattleHints
supports bounded local property rows as well as plain hints; no simulation owner.
Test-AutoBattleRewardBindings checks installed native closure IL/typed row capture
and detached UI lifetimes without constructing a Game or running a delegate.

b299 default rollout: ExperimentalFeatures uses default-on map/automatic/functional
siege-flight gates with explicit0 opt-out. CoopSiegeFlightObservationPatch retains
its exact original-instruction validation and owner-scoped teardown. All diagnostic
and automation gates remain opt-in. Test-ReplicaDefaults.ps1 checks compiled client
and server flags in fresh processes. CreateTwoPcPreviewPackage.ps1 validates these
defaults, embeds verification and excludes the unused tooltip prefab. No launcher
environment injection is required for the map on either PC.

2026-10-28 camera UI candidate b298: CoopCampaignMapReplicaInfoVM retains only
English camera mode/return/control text. CoopCampaignMapReplicaInfo.xml removes
the custom pinned card. UpdateReplicaInformation performs admitted native hover
only; no local click pin or campaign order. Old pure panel-position utility remains
unused for compatibility. Native hover/Alt and body/disclosure revocation persist.
Auto-battle plain hints use CoopCampaignAutoBattleHints and exact HintViewModel
field capture under the explicit scoreboard policy; no provider/simulator commands.

Auto-battle candidate: `Campaign/CoopCampaignAutoBattleCapture.cs` reads the exact
MapScreen-owned current native scoreboard; `CoopCampaignAutoBattlePresentation.cs`
owns CAS1 and uses the bounded shared prepared-value codec. CMN12 publishes it
exclusively; `CoopCampaignMapPrototypeMissionView.AutoBattle.cs` binds the stock
SPScoreboard through detached VMs. `AutoBattlePresentationTests` and
`scripts/Test-AutoBattleBindings.ps1` cover transport/progress and local command
isolation. See CAMPAIGN_AUTO_BATTLE_WINDOW.md; native acceptance pending.

2026-10-28 settlement tooltip candidate: `CoopCampaignMapMenuCapture.SettlementHints.cs`
owns current prepared source leases; `CoopCampaignSettlementHintIdentity` lives
with the pure settlement presentation. `CoopCampaignMapTooltipCapture.Settlement.cs`
captures audited native rows/string hints with existing text/language isolation.
`CoopSettlementHintVM` and separate portrait interests reuse the existing leased
tooltip relay/presenter. CMN11/protocol70/bridge65/tooltip6. Native hover acceptance
pending; see CAMPAIGN_SETTLEMENT_MENU_OVERLAY.md.

## Settlement top panel and native-ended close (2026-10-27 b289 candidate)

`CoopCampaignSettlementMenuPresentation` and CMN10 carry prepared native settlement
labels/flags/numbers, owner banner and ordered people/party portraits. Host capture
reads the current SettlementMenuOverlayVM; client `CoopCampaignSettlementMenuVM`
binds the stock SettlementOverlay movie. Compiled binding/metadata check:
`scripts/Test-SettlementMenuBindings.ps1`. Native and hover fidelity remain open;
see [canonical panel boundary](CAMPAIGN_SETTLEMENT_MENU_OVERLAY.md).

`CoopCampaignMissionClosePolicy` in the lifecycle file selects the owned native
close route; `CoopCampaignMissionServer.CloseOwnedMission` dispatches it for normal
applied-result completion or explicitly admitted save reload. Never confuse
MissionEnded with completed multiplayer teardown. See
[ambush failure and native audit](CAMPAIGN_MISSION_NATIVE_ENDED_CLOSE.md).

## Detached encounter portrait resources (2026-10-27 candidate)

`Infrastructure/CoopCampaignCraftedVisualWeapon.cs` and
`Campaign/CoopCampaignCraftedVisualCapture.cs` own bounded exact dependency data.
`Infrastructure/CoopDetachedPortraitResources.cs` owns client-mission unregistered
preview items, prepared outside rendering. `CoopItemRegistryCounter.cs` observes
managed registration metadata. `UI/CoopDetachedPortraitTextureProvider.cs` owns
local binding/request lifetimes; `Patches/CoopDetachedPortraitEquipmentPatch.cs`
limits equipment substitution to the exact own thumbnail request/code reference.
`scripts/Test-DetachedPortraitScope.ps1` verifies compiled scope/lifetime and the
installed managed counter layout without a native game. See
[canonical boundaries and acceptance](CAMPAIGN_CRAFTED_VISUAL_RESOURCES.md).

Current aftermath entry points and acceptance boundaries:
[2026-10-25 handoff](HANDOFF_2026-10-25_AFTERMATH.md). Earlier dated entries describe
their then-current candidates, including now-superseded format versions.

Aftermath independent inspection follow-up (2026-10-24):
`Campaign/CoopCampaignMapTooltipCapture.Prisoners.cs` is an unwired host preview
candidate, with exact active-screen/four-list membership resolution in the capture
adapter. Owned HeroViewModel and prepared labels replace unsafe live selection
commands. b258 builds; native safety/rendering and client delivery are unverified.
Keep it separate from the immutable b256 feedback-correction runtime candidate.
`scripts/Test-LootInspectionSession.ps1` exercises the client session with injected
channel delegates, without native network/game initialization. Source b255 adds
that test seam only; installed b254 and the coordinated r49 repeat remain separate.
`UI/CoopCampaignLootInspection.cs` connects local hover/hero/mode handlers to
per-peer card interests and applies only matching responses to a detached ItemMenu.
`CoopCampaignAftermathVM` supports isolated local overrides and explicitly bound
inspection handlers; default commands/setters remain inert. The map presenter
suspends its own movie without stealing inventory interest. b253 is build/binding
verified, not native-accepted. Hero mannequin, cycling controls and prisoner
inspection remain open; canonical aftermath documentation lists exact limits.
`Campaign/CoopCampaignMapTooltipCapture.Aftermath.cs` is an isolated
host-side ItemMenu projection candidate connected through the publisher's admitted
per-peer tooltip relay. It excludes SetItem's inspection event,
uses existing language restoration, and revalidates live Loot row identity.
Explicit admitted-hero equipment comparisons are now source candidates, with native
comparability/ordering and pure bounds tests; native validation and delivery remain
incomplete. `CoopCampaignAftermathInspectionTarget` and tooltip format4 carry exact
view interests/full CAF2 cards (protocol63/bridge58). No working independent hover
is claimed; the client producer/consumer remains pending.
`Campaign/CoopCampaignAftermathCapture.cs` publishes prepared bindings and opaque
window-local ReplicaRowId values for exact SPItemVM/PartyCharacterVM rows.
`Infrastructure/CoopCampaignAftermathPresentation.cs` owns the reference-identity
registry; successful captures revoke removed rows, reset revokes the whole window.
Row identity tests cover reordering, removal/reintroduction and screen replacement.
`UI/CoopCampaignAftermathVM.cs` reconciles identified rows by token, preserving VM
references across reorder and finalizing revoked rows; ordinary property lists
remain index-based. Installed-library tests exercise both behaviors without startup.
The b240 host-hover movie is superseded and no longer loaded. Per-client inspection
requests, isolated full-card generation and local selection rendering remain open.
No native inspection or terrain fix is claimed. See the
[canonical aftermath state](CAMPAIGN_MAP_AFTERMATH_SCREENS.md).

Siege playback foundation (2026-10-23):
`Infrastructure/CoopCampaignMapSiegeShotIdentity.cs` assigns bounded reference-based
identities to complete managed missile populations, independent of list indices.
`Tests/CoopCampaignMapPrototype.ContractTests/SiegeShotIdentityTests.cs` verifies
compaction, epoch and atomic-rejection behavior; full map contracts pass.
Connected in source to host capture, CSP2/CSO6 transport (protocol56/bridge51)
and client ShotId visual keys; smooth playback/native acceptance remain open. See
[playback investigation](SIEGE_PRESENTATION_PLAYBACK_2026-10-23.md) for remaining gates.

Timing follow-up b214 adds `CoopCampaignMapSiegeTime.cs`, owner-thread overlay
capture, deep-copy validation and optional CSO7 time suffix (protocol57/bridge52).
Full map tests and both isolated builds pass. Follow-up b215 consumes timing in
`CoopCampaignMapSiegeProjectilePlayback.cs` for bounded same-shot translation from
the menu tick. Full map tests and both b215 builds pass. Orientation/machine phases,
short-flight coverage and native acceptance remain open; installed b211 unchanged.

Machine continuity follow-up: managed construction-object identity and bombardment
cycle are captured in CSM2/CSO8 (58/53); renderer replaces same-slot changed instances.
The shared sampled playback now blends matching machine/clip/cycle phases. Full
map tests pass; native smoothness and orientation are not accepted yet.

## Raid plunder tooltip boundary (2026-10-21, source research)

Current CoopCampaignMapMenuPlunderVM has inert ExecuteBeginTooltip/EndTooltip;
host capture and menu transport carry only ItemId/Amount, not EquipmentElement
modifiers or tooltip rows. This remains a fidelity gap, not a completed feature.
Installed TaleWorlds.CampaignSystem.ViewModelCollection.GameMenu.GameMenuPlunderItemVM
calls InformationManager.ShowTooltip(typeof(ItemObject), Item), passing its
actual EquipmentElement. Installed TooltipRefresherCollection.RefreshItemTooltip
produces PropertyBasedTooltipVM rows (mode1): name/type/weight, component-specific
properties, and equipment-modified values. Weapon extension selects a different
usage; armor/weapon tier is conditional on Campaign.Current. Banner handling
mutates shared text variables, so invoking it casually during replication is
unsafe. Merely showing the mirrored item's name or invoking the provider in MP
would not establish host-equivalent presentation.

Installed SandBox.View.SandBoxViewSubModule.RegisterTooltipTypes was decompiled
on 2026-10-21: ItemObject is registered with PropertyBasedTooltipVM,
TooltipRefresherCollection.RefreshItemTooltip and movie PropertyBasedTooltip.
This establishes the stock registration, not the live registration after mods;
retain the existing runtime delegate/type/movie compatibility check.

Next design gate: extend the existing
host-main-thread tooltip text/language isolation, and admit requests only against
the currently published menu's visible plunder entries. Preserve full equipment
identity on the host; send bounded detached rows, never native delegates or
campaign objects. Bind response lifetime to session/menu-instance/item identity,
clear on menu close/change/modal entry, and reject stale or arbitrary item IDs.
Compare ordinary goods/food, livestock and modifier-bearing items if admitted by
native plunder, without changing battle adapters. Inspect shared tooltip ownership
and protocol compatibility before implementation. No new runtime code, build,
deployment, or native plunder-tooltip acceptance is claimed by this research.

### Publication and isolation constraints

Source inspection of NetworkController.Menu.UpdateServerMenu shows that the
server increments its transport revision for every serialized menu change,
including raid progress/count changes. That revision is server-owned, not a
host-published menu lifetime. Do not use it as the tooltip identity: continuous
progress can invalidate every asynchronous response. Introduce a host-owned
menu-instance token plus entry identity, stable across progress/count changes
but invalidated on close/reopen, source-owner replacement, or equipment change.
Admission must recheck current host visibility and the full EquipmentElement;
ItemId alone does not distinguish modifiers. Client and server must admit only
entries in their current published menu, not arbitrary item registry lookups.
Failed/hidden menu capture must invalidate the host capability, not keep an old
source VM alive. Test same-ID close/reopen, reordered/replaced entries, delayed
responses and continuous progress updates separately.

CoopCampaignMapTooltipCapture already checks main-thread ownership, native
registration, non-reentrancy, text context restoration and language thread state.
However, CoopCampaignMapTooltipLanguageState.AreWritableTemplatesIsolated audits
only the five map providers' templates. Its current successful result is NOT
evidence that RefreshItemTooltip is isolated. Audit item provider helpers and
mutable GameTexts templates before adding the item route; retain fail-closed
cleanup behavior. This applies to every native plunder-bearing menu, not only
Lavenia or a particular battle adapter. No additional diagnostic hooks into
native state are needed for this design.

Source foundation added after that audit: AreItemWritableTemplatesIsolated
checks str_STR1_space_STR2 and str_NUMBER_percent independently of the map
inventory, through the same missing/shared-empty-attributes rejection. The
installed ItemHelper.GetMissileDamageText/GetThrustDamageText delegate to
GetDamageDescription, which allocates a fresh TextObject before assigning
DAMAGE/DAMAGE_TYPE; it does not assign attributes on str_damage_types.
Global banner RANK/NUMBER writes still require the existing text transaction.
This helper is intentionally not yet called: item admission/transport/capture
integration is pending. The focused map contract executable passed (exit0,
522 text-transaction checks) after an initial sandbox file-access failure.
The two new checks are source-wiring guards, not execution of the game-backed
template check. Production compilation and native verification remain pending;
installed b138 and closed game processes are unchanged.

Identity foundation: Infrastructure/CoopCampaignMapPlunderIdentity<T> now owns
opaque per-slot tokens for one host menu owner. Equal equipment in the same
slot retains its token across repeated captures; duplicate slots do not alias.
Owner/menu changes, Clear, invalid captures, removed slots and changed equipment
invalidate their tokens. Returned token arrays are detached. The native adapter
must use complete EquipmentElement equality and clear on hidden/failed capture
and session reset; the helper does not discover those events itself. It is NOT
yet wired into menu publication, transport, request admission or tooltip UI.
PlunderIdentityTests passed in the focused map contract executable (exit0),
including 100 stable captures, modifier-key replacement, duplicate slots,
reordering, removal, close/reopen and invalid capture. Test equipment is a string
surrogate; actual EquipmentElement equality and native lifecycle remain gates.
No production build, deployment or new game run was performed for this helper.

Transport candidate now carries optional TooltipId on each plunder entry in
CMN5 (protocol43/host bridge38/catalog9). Empty means no admitted tooltip;
nonempty IDs must be canonical `plunder:` plus32 lowercase hex characters and
unique within the menu. Clone and binary round-trip preserve IDs atomically with
their entry; old CMN4 bytes are rejected. Campaign capture still emits empty IDs,
so this does not enable item tooltips yet. All three roles must eventually be
rebuilt/deployed together; retained b138 must not be mixed with this candidate.
Focused map contracts passed (exit0,3327 menu checks), including new identity
round-trip/malformed/duplicate/old-format cases. The initial run failed on the
old expected protocol constants; those expectations now match43/38/9. Host
capture, relay admission, item-provider execution and UI wiring remain pending.
Production compilation and native verification have not yet been performed.

Subsequent host integration: CoopCampaignMapMenuCapture now issues the opaque
IDs from the visible native GameMenuVM, scoped to session and MenuContext.
An explicit equipment comparer includes Item, ItemModifier, CosmeticItem and
IsQuestItem: installed EquipmentElement.Equals only compares Item/ItemModifier.
Null/failed/hidden captures revoke identities in finally; publisher reset also
revokes. TryResolveCurrentPlunder re-reads the native menu before resolving a
token, but is not yet called by the relay. Successful-publication and peer
admission must still precede it. Client UI/provider integration remains open.
plunder-identity-b139-20261021 passed both isolated production builds, with
installed module inventories unchanged. Focused map contracts also passed
(exit0). No native menu-lifetime or item-tooltip acceptance is claimed; b138
remains installed and no game processes were started.

Host provider candidate now adds PlunderItem(kind6) and shares the guarded
capture core with map providers. It verifies the live native ItemObject mapping,
uses the item template inventory, restores text/language state and rechecks
current equipment before/after the native call. Publisher retains a detached
menu only after successful map publication and clears it before each new attempt;
item requests require matching session, nonempty lease and a token in that menu.
The existing server/client target admission still rejects plunder IDs, so no
end-to-end item requests are enabled yet. Peer admission and UI remain next gates.
plunder-capture-b140 failed compilation on a Guid/int lease comparison; corrected
to Guid.Empty. Fresh b141 passed both isolated builds with installed inventories
unchanged. Focused map contracts passed after updating the unknown-kind fixture
from6 to7. These are compilation/existing contract results, not native item
capture verification or full regression of the new request route.

Admission integration now uses ContainsPlunder/IsPlunderAdmitted on client,
dedicated and publisher. Dedicated keeps a detached current server menu and
clears it on reset; existing freshness, conversation, peer-lease, generation,
throttle and response-time revalidation remain in effect. An ID must occur
exactly once; arbitrary item IDs, duplicate tokens, closed menus and wrong
sessions/empty leases are rejected by the focused contracts. UI hover still
does not request plunder tooltips. plunder-admission-b142-20261021 passed both
isolated builds without installed inventory changes; focused map contracts
passed. Native multi-peer/menu-transition acceptance remains unverified.

2026-10-22 UI candidate: plunder Begin/EndTooltip callbacks now forward opaque
IDs to the mission view; the existing detached-row presenter takes precedence
over world hover while the pointer hits the campaign menu. Replacing an entry
updates its hovered identity, removal/finalization ends it, and hidden/modal menu
suspension clears hover. No native ItemObject tooltip provider runs in MP.
plunder-hover-b143-20261022 passed both isolated builds with unchanged installed
inventories. Focused map contracts passed with3330 menu assertions after fixing
a duplicate local name in the new test. New UI checks are source-wiring checks,
not native event delivery tests. End-to-end hover, native text parity, long-hover
flicker, modal recovery and multiple-peer coverage remain unverified. Retained
b138 is still installed; no game run was started for this candidate.

Subsequent r27 preparation: c137 passed all26 contract projects. Fresh
plunder-hover-r27-20261022 staged b143's seven files and retained seven current
configuration preimages. b143 now supersedes b138 on disk; no role has yet been
launched or verified loaded in this run. Native acceptance remains pending.

## Encounter portrait hover crash (2026-10-21, r25)

### Encounter tooltip routing research (2026-10-22)

Installed TaleWorlds.CampaignSystem.ViewModelCollection.dll was inspected with
ilspycmd: GameMenu.Overlay.GameMenuPartyItemVM.ExecuteOpenTooltip chooses
MobileParty with arguments [Party.MobileParty, true, false], otherwise Settlement
with [Settlement], otherwise Hero with [Character.HeroObject, true]. This differs
from map-hover routing: CoopCampaignMapTooltipCapture.TryRoute first selects
MapEvent, SiegeEvent or Army and uses [MobileParty, false, true] for ordinary
parties. Reusing a world entity ID and TryRoute for encounter portraits would
therefore show the wrong provider and potentially different troop/prisoner rows.
RefreshMobilePartyTooltip uses these flags in troop/prisoner disclosure and ship
rows; preserve native IsInfoHidden behavior rather than reconstructing rosters.

EncounterMenuOverlayVM constructs party-backed entries for mobile participants
and settlement-backed leading entries for settlement defenses/sieges. Its current
construction sites do not use the CharacterObject overload. This observation
covers the shared field/village/siege encounter surface, not every other menu
using GameMenuPartyItemVM. The generic Hero fallback must not be silently routed
to a party provider; any future admitted character entry needs a separate audit.

Implementation plan: add opaque encounter-entry capabilities bound to the actual
visible overlay owner, menu context, side/slot and exact Party/Settlement source.
Revalidate against the current successful menu publication and peer lease before
capture and response delivery, independently of world-label catalog membership.
Keep native arguments host-selected; retain text/language isolation, main-thread
capture and detached-row transport. Wire Begin/End hover into the shared presenter
with modal/dialogue/menu replacement cleanup, without enabling encyclopedia or
campaign actions. A changed side/slot/owner/source must revoke the old token;
count/progress-only changes must not repeatedly invalidate it.

Affected files will include menu presentation/codec, menu capture/publisher,
tooltip target admission/controller, capture adapter, encounter VM and mission
view. Test malformed/duplicate/arbitrary tokens, close/reopen, replacement,
delayed responses, exact native route arguments and unchanged ordinary map hover.
Use coordinated wire-version staging only after builds/contracts pass. Native
provider execution remains runtime-sensitive; no new hot-path diagnostic or
native scene hook is justified. This section records research/design only: no
encounter tooltip implementation, build or native acceptance is claimed.
Documentation impact: CODE_MAP owns this boundary; update the runtime test report
when verified. Existing authority and deployment mechanisms remain unchanged.

Identity foundation implemented: CoopCampaignMapEncounterIdentity retains opaque
per-side/per-slot tokens for exact context, overlay, menu and native source
references. It detaches caller arrays, revokes removed/replaced entries and clears
on invalid updates. The host adapter still must supply visible native sources,
clear at session/menu lifetime boundaries and enforce publication/peer admission;
this helper is not authorization on its own. EncounterIdentityTests exercises
stable repeated capture, duplicates, side/source replacement, owner mismatch,
close/reopen, aliasing, invalid/oversized lists and empty lists. Focused map
contracts passed (exit0) with the initial helper. Transport/capture/UI integration
and production build/native acceptance remain pending; installed b143 unchanged.

Transport follow-up: encounter entries now carry canonical optional TooltipId
(`encounter:` plus32 lowercase hex characters), unique across both sides. CMN6
preserves it with protocol44/host bridge39/catalog9; CMN5 is rejected. Focused map
contracts passed after correcting the remaining settlement-marker schema guard;
3362 menu checks passed. Native capture still emits empty tokens, so this is not
an enabled tooltip feature. No deployment occurred; installed b143 uses43/38/9
and must not be mixed with subsequently built44/39/9 roles.

End-to-end source candidate now wires encounter tokens from native overlay
capture through successful-publication/peer admission into the existing tooltip
relay. Host capture re-resolves the exact current source before and after the
provider call, using MobileParty(true,false) or Settlement directly instead of
world MapEvent/Army routing. Existing provider compatibility, text/language scope
and detached row validation remain. Character-only sources are not admitted.
Client portrait Begin/End callbacks forward only opaque interest; replacement,
finalization and modal suspension clear it. Encyclopedia/context actions remain
inert. Both isolated builds passed in encounter-identity-b144-20261022 and
encounter-tooltip-b145-20261022, installed inventories unchanged. Focused map
contracts passed (3361 menu checks) after updating obsolete no-op hover/source
signature guards. These UI guards inspect source wiring, not native events.
Full c138 suite (`encounter-tooltip-c138-20261022`) completed successfully: all26
contract projects passed (observed terminal exit0). Live portrait/settlement
comparison, delayed hover lifecycle, multi-peer and siege regression remain
pending. b145 is not deployed; b143 retained.

### Whole-screen fidelity priority audit (2026-10-22)

The user explicitly highlighted missing campaign UI and untested sieges. Do not
equate tooltip completion with completion of the global-map replica objective.
Source inspection of CoopCampaignMapPrototypeMissionView and its Menu/BattleMarkers
partials finds nameplate, replica-info, campaign-menu, encounter-overlay and
map-event-visual layers. CoopCampaignMapReplicaInfo.xml instead supplies custom
camera-follow controls/instructions and an info panel; it does not reproduce the
campaign bottom navigation, date/time controls or host-party statistics. This
matches the paired r27 screenshots and is a major remaining visual gap.

Priority acceptance inventory:

| Surface | Current evidence | Remaining work |
| --- | --- | --- |
| Campaign bottom bar/time/party statistics | Missing from inspected replica layers and r27 screenshots | Audit installed native VM/prefabs, capture host-visible presentation, read-only replica and paired verification |
| Raid/menu/encounter panels | r27 bounded raid/dialogue/encounter replacement passes | Changing-count stationary hover, wider menu states and disabled hints |
| Encounter portrait tooltips | End-to-end source candidate, both b145 builds and c138 all26 contracts pass | Paired native verification, delayed lifecycle and adjacent encounter types |
| Portrait appearance | r27 poses differ | Complete gated pose parity work and verify lifecycle safety |
| Siege preparation UI | IsSiege flags and encounter wall/count data exist | Inventory native camp/machine/progress panels; flags alone do not establish replication |
| Siege map-to-battle-to-map | Historical battle subsystem evidence is not current replica evidence | Test assault and applicable siege variants, aftermath and rejoin on a coherent candidate |
| Map entities/markers/camera | Earlier bounded tests and user feedback | Whole-screen paired pass across visibility, motion, scale and relevant states |
| Multiple peers/reconnect | Not covered by r27 | Verify independent camera/hover and no stale/private host data |

Next broad implementation target after coherent encounter-tooltip integration is
the missing campaign bottom interface, alongside planning a real siege fixture.
Do not enable campaign control actions while reproducing their visual states.
Inventory is source/screenshot-backed, not an exhaustive native screen audit or
a completion percentage. No game processes were launched for this audit.

### Native bottom-bar implementation boundary (2026-10-22 research)

Installed `SandBox.GauntletUI.dll` decompilation locates the actual ownership:
`GauntletMapBarView._mapBarGlobalLayer` owns
`GauntletMapBarGlobalLayer._dataSource` (`MapBarVM`). The layer loads movie
`MapBar`, sprite category `ui_mapbar`, at native layer order202. Finalization
disposes the VM and unloads that category. Capture must inspect the current
map-view instance and its existing VM; it must never create/initialize a native
MapBarVM in multiplayer or invoke its Tick/Refresh/command methods to obtain data.
Both private fields require explicit compatibility failure, not guessed fallback.

Verified installed prefab: `Modules/SandBox/GUI/Prefabs/Map/MapBar.xml`.
Its presentation inventory is broader than a date label:

| Native source | Detached presentation needed | Authority constraint |
| --- | --- | --- |
| MapBarVM | IsEnabled, army-button visibility/enabled state | No ExecuteArmyManagement on replica |
| MapTimeControlVM | Date, Time, PausedText, TimeFlowState, IsCenterPanelEnabled | No ExecuteTimeControlChange; camera reset may target only the local replica camera |
| MapInfoVM | IsInfoBarEnabled/Extended, ordered PrimaryInfoItems and SecondaryInfoItems | Capture prepared lists, not recomputed campaign statistics |
| MapInfoItemVM bindings | VisualId, Value, IntValue, HasWarning | Preserve host formatting and warning state; native tooltip callbacks are not transport data |
| MapNavigationItemVM | Ordered ItemId, IsEnabled, IsActive, HasAlert, AlertText | ExecuteOpen/ExecuteGoToLink must not open or mutate a local campaign |
| Native hints | Time/navigation/army/extension and statistic explanations | Audit source callbacks before exposing detached, host-authorized hints |

MapTimeControlVM construction subscribes to campaign save events and the global
input-device event; Refresh reads CampaignTime, and Tick reads Campaign.Current
and MobileParty.MainParty. Its command changes campaign speed. This establishes
why reusing the live native VM in MP is unsafe, even though reusing the stock
movie with detached read-only bindings is the intended visual path.
MapInfoVM also reads main-hero finances/health, party food/morale/speed/wages and
army speed. It dynamically changes visual IDs (e.g. speed_at_sea) and warnings;
hardcoded numeric approximations would not reproduce the host presentation.

Implementation sequence and risk boundaries:

1. Add bounded detached presentation/list validation and clone/codec tests;
   carry exact host-formatted strings, ordered items and native visibility.
2. Capture only the current host map view's prepared data on its owning thread,
   with active session and screen/lifetime checks. No native writes, constructor,
   reflection invocation of callbacks, or new hot-path diagnostics.
3. Carry the bar atomically with a fresh session/revision-bound map publication;
   hidden, failed, closed and replaced sources clear it, including rejoin.
   Update protocol coherently for all roles rather than mixing b143 and b145.
4. Bind detached VMs to stock MapBar and explicitly own sprite/movie lifetime.
   Keep visual enabled/selected states separate from command authority: simply
   disabling every button would alter native appearance. Commands remain inert
   except explicit local camera/inspection actions. Audit stock custom widgets
   for callbacks, two-way writes and campaign dependencies before loading.
5. Resolve overlap with existing replica camera controls/instructions; preserve
   manual detach/return without covering native information or leaking clicks
   into world picking. Suspend interaction under local settings/dialogues.
6. Compare full paired screens, not just values: normal map, pause/speed/date
   changes, expanded statistics, town/raid/encounter, dialogue, battle return,
   siege preparation and reconnect. Siege panels/machines are a separate required
   surface and are not implemented by adding MapBar.

This is native-source/prefab research and an implementation boundary, NOT an
implemented bar, native acceptance, or full-screen parity claim. No game or
configuration changes occurred. Documentation impact: CODE_MAP owns this evidence;
update architecture/runtime-flow documents when transport/ownership is wired,
and the runbook when an executable acceptance candidate exists.

Bottom-bar value foundation follow-up: `CoopCampaignMapBarPresentation` now
contains detached native-shaped time/visibility, ordered statistic rows and
navigation alert states, with bounded text/icon keys/lists and deep copies.
`CampaignTime.HoursInDay` is configured by the campaign model, so the presentation
retains that denominator rather than assuming a24-hour custom campaign. Native
CampaignTimeControlMode values0..6 were verified against the installed assembly.
No callbacks, native objects, campaign commands or transport admission are in
the value object. No host capture, codec, publication or UI consumer is wired
yet; hints remain a separate required integration, not an omitted acceptance gate.

`MapBarPresentationTests` covers native-shaped values, independent deep clones,
invalid/nonfinite time, calendar bounds, unknown speed, null/oversized lists,
duplicate navigation identities, invalid icon paths, string bounds/NUL and signed
native values. The full map contract executable passed on2026-10-22 including
these checks. Initial direct invocations failed on missing repository-root env
and sandbox file access respectively; rerun with the repository-root variable
and approved elevation passed exit0. No production build/deployment/native
acceptance was performed for this value foundation; installed b143 is unchanged.

Host adapter follow-up: `Campaign/CoopCampaignMapBarCapture.cs` now resolves the
current concrete GauntletMapBarView through MapScreen.GetMapView, validates the
two native field types, reads the existing MapBarVM children, bounds list counts
before copying and rechecks screen/campaign/view/layer/VM identity before success.
Hidden/native-unavailable/invalid capture leaves the output null. The caller must
run on the owning UI thread and publish this null as removal; the adapter is NOT
yet called by the publisher. Session membership, publication ordering, network
codec and detached stock-movie binding remain open. Hints are not captured yet.
No native Refresh/Tick, construction, tooltip callback or action is invoked.
`map-bar-capture-b146-20261022` passed both isolated builds with installed module
inventories unchanged. Source guards cover prepared submodels, replacement/hidden
checks and forbidden command calls; they do not establish native capture success.
No new runtime acceptance or deployment is claimed; b143 remains installed.

Host transport follow-up: CMB1 `CoopCampaignMapBarCodec` carries an explicit
null-close or bounded presentation with an exact positive revision, strict UTF8,
canonical booleans and no trailing bytes. Tests reject every truncation of a
valid sample, wrong schema/revision, malformed boolean and extra bytes. The
first test run exposed uncaught InvalidDataException; both codec directions now
handle it explicitly and the focused map contracts subsequently passed.

Host snapshot now deep-clones MapBar, validates it and excludes it during active
dialogue. Publisher captures it each normal publication; failures publish null.
Required canonical Base64 MapBarPresentation binds CMB1 to the surrounding
snapshot revision. Host bridge schema is40 (protocol44/catalog9 unchanged until
client-network integration); old bridge39 is intentionally incompatible.
Dedicated links both new value/codec files. b147 isolated client/dedicated builds
passed with installed inventories unchanged. `map-bar-bridge-c139-20261022`
completed exit0: all26 contract projects passed, including host bridge roundtrip,
required-field rejection, revision mismatch and explicit close. Server-to-client
delivery, UI, hints and native verification remain open.

Atomic interface transport follow-up: protocol45/bridge40/catalog9 now sends
CMI1 (nested menu CMN6 plus bar CMB1) on the existing reliable menu-chunk message.
Both nested revisions must match the channel revision; decoded objects become
available together only after hash-checked assembly. CurrentClientMapBar resets
with menu/mission state. Fixed-revision content comparison avoids resending on
unrelated map movement. Ordinary value changes finish in-flight sends before the
latest payload; bar removal/menu identity replacement preempts. Existing peer
identity, synchronization, per-tick budget and join gates remain. The combined
payload retains the existing1MiB cap; overflow explicitly closes both surfaces
rather than publishing a partial interface. Large-menu acceptance remains open.

map-interface-b148-20261022 passed both isolated builds with installed inventories
unchanged. Focused map contracts passed including nested revision matching,
every truncated combined sample, chunk completion, replay rejection and explicit
close. These are not live networking tests. Stock MapBar rendering/hints and
freshness/modal input binding remain pending; installed b143 is unchanged.

Stock bar UI candidate: `CoopCampaignMapBarVM` supplies detached MapBar bindings;
the mission's MapBar partial owns `ui_mapbar`, stock movie MapBar and a mouse-only
nonfocus layer. Display requires ready replica, fresh host state, visible bar and
no conversation/local modal. World picking/camera input and unrelated world
tooltips are suppressed over the bar. Teardown releases movie, VM and sprites.
Campaign time/navigation/army commands remain empty; only reset-camera calls the
existing local FollowHostCamera. Local info expansion is retained across ordinary
updates, with host expansion changes authoritative. Navigation/info child VMs are
updated in place, not rebuilt per time tick. Hint commands deliberately remain
empty: this is an outstanding fidelity requirement, not final acceptance.

Native widget audit: MapCurrentTimeVisualWidget selects button states without
campaign calls; MapInfoBarWidget toggles local expansion; MapBarGatherArmyBrushWidget
subscribes to that widget's expansion and changes visual states; MapTimeImageBrushWidget
renders using native24-hour offsets. The replica passes native Time unchanged,
including on nonstandard campaign calendars, to preserve the same rendering.
This inspected-widget evidence does not prove all stock-movie dependencies or
binding/runtime safety. Custom camera controls moved above native statistics and
the help text above the central bar; actual viewport overlap requires native QA.

b149 and final map-bar-ui-b150-20261022 both isolated builds passed without installed
inventory changes. map-bar-ui-c140-20261022 passed all26 contract projects. Source
guards cover stock resources, visibility gates, release and inert commands, not
actual Gauntlet rendering. No deployment or native visual
acceptance yet. Remaining gates include hints, first/repeated load, dynamic values,
pause/expanded states, settings/dialogue/raid, battle return, rejoin and siege.

r27 live follow-up (2026-10-22): b143 loaded client/dedicated MVIDs matched the
staged candidate. Fish and Hides plunder cards matched the observed native rows;
switch/off-panel clearing and Options hide/Cancel/hover recovery passed bounded
checks. Raid interception by Tristania removed plunder UI during dialogue and
returned to a matching village-defense encounter menu without stale item rows.
Stationary hover with changing counts, modifier equipment and multi-peer gates
remain open. Portrait tooltip callbacks are still empty, and portrait pose parity
is not established. All three explicitly authorized roles exited gracefully;
all seven configuration hashes matched preimages, Steam/saves preserved, b143
retained. See artifacts/runtime/plunder-hover-r27-20261022/OBSERVATION.md and
cleanup.json for scope and exact evidence. No broad completion claim is implied.

b137 restored native menu hints, but r25 subsequently crashed. The dump proves
ArgumentException/80070057 in EventManager.Update -> Enum.Parse on the
CoopCampaignMenu layer. Its hovered GameMenuPartyItemButtonWidget had an empty
HoveredCursorState. Installed SandBox GameMenuPartyItem.xml binds that property
to EncyclopediaCursorEffect; CoopCampaignMapEncounterPartyVM returned string.Empty.
The source candidate now returns the valid Default cursor while keeping all
portrait actions inert. This shared binding affects field/village/siege and other
encounter presentations, not battle-specific adapters. A source-wiring guard is
added; fresh builds and native portrait-hover/modal regression remain pending.
Do not classify b137 as stable or attribute the crash to resource pressure.
See [r25 evidence](../../artifacts/runtime/menu-input-r25-20261021/OBSERVATION.md).

menu-cursor-b138-20261021 passed both isolated production builds with installed
inventories unchanged. The candidate is not deployed or native-verified. r25 is
closed with all seven configuration hashes matching and owned processes absent.
menu-cursor-c136-20261021 passed all26 canonical contract projects (exit0).
The cursor assertion verifies source wiring, not native portrait hover safety.

Subsequent r26 deployed b138. Campaign8296 and user-launched MP45948 report the
candidate client MVID10249a11-1869-4e40-8d71-f44d7ff17ce9; dedicated17732 reports
MVID456020e0-b0cb-46dd-a240-0078a7a68491, matching the isolated dedicated output.
Both installed assembly SHA256 values match the deployment manifest. This is
loaded-MVID plus on-disk hash evidence, not an in-memory hash.
Native deserter encounter regression passed bounded portrait pointer/click
checks on both sides without the r25 crash. Disabled-retreat and simulation
hints still render; replica clicks remain inert. All three processes were
responsive on the subsequent identity audit. Modal/re-entry/other-scenario
coverage and cleanup remain pending; r26 is IN PROGRESS.
See [r26 observation](../../artifacts/runtime/menu-cursor-r26-20261021/OBSERVATION.md).

r26 subsequently passed the Options/Cancel round trip and another host portrait
pointer check with the same stationary encounter. It is now CLOSED: user-approved
cleanup gracefully exited all three exact roles; seven configurations already
matched preimages, no restore overwrite was necessary, and no recorded roles
remain. b138 is retained. Changing-host-modal and other-encounter coverage remain
open; this bounded regression does not establish full visual parity.

## Lavenia raid completion observation (2026-10-21)

On deployed b135, r24 rendered advancing raid progress and matching four-item
plunder images/counts in campaign and replica. After the user strengthened the
party, both displayed successful raid completion and cleared progress/plunder
and the active raid marker. Host Continue closed both panels; multiplayer map
zoom then responded. This is bounded native acceptance for Lavenia, not all-goods
or multi-peer coverage. The user-restarted client has unverified diagnostic
environment inheritance, so missing hint logs cannot resolve the separate hint
investigation. Cleanup remains pending; see
[r24 observation](../../artifacts/runtime/menu-hint-r24-20261021/OBSERVATION.md).

## Encounter disabled-option hint investigation (2026-10-21)

Post-r24 source finding: the replica menu layer never configured InputRestrictions.
Installed ScreenLayer initializes this mask to Invalid (0); GauntletLayer does not
override it. ScreenManager grants IsHitThisFrame on mouse-button/wheel admission,
and GauntletLayer.LateUpdate passes that flag to UIContext.SetIsMouseEnabled.
Thus the menu layer could render while lacking native pointer/hover delivery.
The candidate now admits Mouse only, preserves IsFocusLayer=false and inert menu
commands. Shared raid/field/village/siege menus are affected; no battle adapter or
host authority changes. Source-wiring guards cover admission and inert actions,
not actual hover rendering. Native verification, disabled-option hints, panel
collapse, modal suspension, and returned-map input regressions remain required.
No deployment occurred; the bounded default-off probe is retained for verification.
The focused map Release contract executable passed (including3322 menu checks)
after supplying COOPSPECTATOR_REPOSITORY_ROOT and rerunning outside the sandbox
that denied file access. The first invocation lacked that variable; neither failed
attempt is a pass. This compiles the contract project, not the production view;
production isolated builds and native acceptance remain pending.
Follow-up: menu-input-b137-20261021 passed both isolated production builds and
verified installed inventories unchanged. Client SHA256 is
25C972010D18951C4AC0A14BC45AD047B23748A30F9878A4EAC3998D2CE0140B;
dedicated output remains byte-identical to b135. The preceding b136 attempt failed
on sandbox CIM access and is not a build pass. No candidate was deployed; r24
games remain running and native input/hint acceptance is still pending.
Full regression run menu-input-c135-20261021 passed all26 canonical contract
projects (terminal Pass, exit0). This does not replace native hover/input testing.

Candidate b135 adds a bounded managed-only probe to the shared hint VM, opted in
only by CoopCampaignMapMenuOptionVM. Both COOPSPECTATOR_VERBOSE_DIAGNOSTICS=1 and
COOPSPECTATOR_MENU_HINT_DIAGNOSTICS=1 are required; default execution emits nothing.
At most64 records per client process report created/begin/end, hasText and whether
the global string tooltip registration exists. No host text, scene reads, native
getters or behavior overrides are added. Other conversation hints do not opt in.
Isolated menu-hint-b135-20261021 compiled both roles and verified installed module
inventories unchanged. Not deployed or runtime-tested. Use a fresh bounded run to
separate missing client text, missing hover events and missing registration; remove
the temporary probe after diagnosis. This is not a hint fix.

Follow-up: c134 passed all26 contracts. Fresh menu-hint-r24-20261021 staged b135
with seven verified files and seven configuration preimages. No game launched
yet; native observation and cleanup remain pending. This supersedes the
not-deployed status above, not the unverified hint behavior.

r23 native comparison showed the campaign's disabled retreat hint, but no hint
in MP after pointer placement and a later capture. After graceful closure, the
retained host state (revision5824, encounter, written01:38:07 local) decoded to
Try to get away / IsEnabled=false / "You don't have enough men!". Send troops
also retained its multiline Plains Battle hint. This proves host capture and
host-file serialization for that final sample, not client receipt at hover time.
The menu codec and bridge roundtrip tests now explicitly assert disabled status
and hint text. The focused map Release suite passed, including3320 menu checks.
No runtime fix or deployment was made; native hint acceptance remains open.
MBInformationManager.ShowHint uses the global string tooltip registration. The
existing replica presenter relinquishes ownership on foreign tooltip display;
there is not yet evidence that it clears this hint. Next inspect client payload,
native hover event delivery and string-tooltip registration before changing UI.
Installed native inspection follow-up: GauntletUISubModule registers
string/HintVM/HintTooltip alongside the general tooltip view, not in a
campaign-only provider. HintWidget.OnConnectedToRoot subscribes to its parent's
EventFire and forwards HoverBegin/HoverEnd while visible, even though the stock
prefab sets the hint widget IsEnabled=false. Therefore that flag alone is not a
defect and must not be flipped speculatively. This is static initialization/event
evidence, not proof of the live registration or callback in r23. The retained
client log contains only menu revisions1/2 (last completed at01:27:32), so repeated
menu-rebinding on every map tick is not supported by this run's evidence either.
See [r23 evidence and cleanup](../../artifacts/runtime/raid-goods-r23-20261021/OBSERVATION.md).

## Battle marker disclosure regression (2026-10-21)

Offline review verified capture requires both the prepared native disclosure flag
and MapEvent.IsVisible; settlement events use their separate nameplate path.
BattleMarkerTests now exercises a nonempty completed list followed by a newer
multi-chunk list: the first newer chunk revokes display, delayed older data cannot
restore it, and only complete replacement permits display again. The focused
CoopCampaignMapPrototype.ContractTests Release run passed (exit0), including this
sequence and3320 menu checks. No runtime code, installed files or game processes
were changed. This tests assembler semantics, not native visual/network timing.

## Read-only campaign menu candidate (2026-10-13)

2026-10-22 r28 thumbnail investigation (games closed): decompilation of the
installed Native `TaleWorlds.MountAndBlade.View.dll`,
`ItemThumbnailCache.GetItemPoseAndCamera`, establishes an identifier-sensitive
craft-material branch. Exact IDs iron, hardwood, charcoal and ironIngot1..6 use
`craftmat_cam` / `craftmat_frame`; the mirror hardwood ID
`cs_mirror_hardwood_e17ff4f1` does not match. Its mesh is nevertheless the correct
`hardwood`, matching installed `CampaignSystem.DefaultItems` initialization.
`ItemImageIdentifierVM` delegates to `ItemImageIdentifier`, which uses the item's
StringId, and the thumbnail cache also keys by that ID. This explains why a
correct mirrored mesh can render with the wrong framing; do not replace the mesh
or mutate registered item IDs to compensate. The same risk applies to all nine
listed craft materials regardless of field/village/siege/hideout adapter.
Next implementation gate: a replica-scoped pose correction preserving registered
identity, cache identity, ordinary thumbnails and native resource lifetime.
No correction is implemented or visually accepted yet. Separately, MapBar hint
callbacks in `UI/CoopCampaignMapBarVM.cs` remain explicit empty placeholders;
visible bar values alone do not establish full native UI parity.

Follow-up source candidate: `Patches/CoopMirrorItemPosePatch.cs` now normalizes
only local StringId results inside native GetItemPoseAndCamera, using admitted
reverse mirror mappings and a nine-material allowlist in multiplayer. It does
not modify registered objects, cache keys, camera creation, mesh loading or
resource disposal. The transpiler requires ten native getter calls (nine
materials plus the unchanged dragon-banner comparison), otherwise installation
fails and removes its owner hooks. SubModule installs it only with the map
prototype enabled. All other item IDs retain native behavior.
`mirror-pose-b151a-20261022` isolated CompileOnly passed both roles with unchanged
installed inventories. PortraitObservation.ContractTests Release passed its new
patched substitute selector tests (all nine materials, identity preservation,
campaign isolation, unknown/non-material IDs and changed-shape rejection), plus
29 existing portrait checks. NuGet vulnerability lookup was unavailable (NU1900),
not a failed compilation. These substitute tests do not prove installed native
patch admission or rendered parity. Candidate remains undeployed; b150 retained.

### Map bar hint routing investigation (2026-10-22)

Installed CampaignSystem.ViewModelCollection decompilation establishes that
MapInfoItemVM has a stable ItemId distinct from its overridable VisualId. The
current detached bar carries only VisualId; never use that icon key as provider
authorization. Introduce stable, current-publication-bound targets before routing
remote hover requests, and revoke them on bar removal/dialogue/battle/session reset.

Native CreateItems routes gold through CampaignUIHelper.GetDenarTooltip, returning
TooltipTriggerVM for ExplainedNumber/RundownTooltipVM with normal and expanded
finance delegates. This is not an ordinary List<TooltipProperty>; flattening it
would lose native layout/expansion. Influence, hit_points, troops, food, morale,
speed, view_distance and troop_wage use BasicTooltipViewModel list delegates.
BasicTooltipViewModel.ExecuteBeginHint and TooltipTriggerVM.ExecuteBeginHint call
global InformationManager.ShowTooltip; do not invoke them for remote requests.

Important side effect: CampaignUIHelper.GetPartySpeedTooltip starts by triggering
PlayerInspectedPartySpeedEvent. Calling it per remote hover is not observational.
Before implementation, isolate only that event within the exact capture scope or
reuse its audited presentation calculation without emitting it; preserve normal
host hover events. Also audit finance delegates/model calls and writable text
templates before treating them as read-only. Native time controls use basic
tooltip callbacks; TimeOfDayHint calls GetTimeOfDayAndResetCameraTooltip.

Implementation sequence: stable target identity and admission tests; separate
basic/rundown/hint detached representations and bounded transport; owner-thread
capture with side-effect/text-state isolation; native client layout without host
commands; clearing/rejoin/settings tests; paired runtime visual acceptance.
No bar hint callbacks are implemented yet. This finding changes the approach:
do not blindly reflect/invoke the native private delegate for every info item.

Identity foundation added: `CoopCampaignMapBarHintIdentity<T>` binds opaque
barhint tokens to an owner reference, session, stable key and exact prepared
source reference. Reordering retains valid targets; replacement/removal/hidden
or invalid input revokes old targets. Duplicate/malformed keys and null sources
fail closed. Returned token arrays do not alias internal state. New focused
identity tests and the entire map-prototype contract executable passed (Release,
repository-root environment set; elevated rerun required for existing relay
filesystem checks). This helper is not yet wired to capture/transport/hover;
it does not by itself enable any client hints or authorize arbitrary providers.

Capture/transport follow-up: native MapInfoItemVM.ItemId and opaque HintToken now
travel in CMB2 (protocol46/hostbridge41/catalog9). Host capture binds primary and
secondary sources separately, verifies unchanged owner/layer/source, clears leases
on null/failed capture and publisher reset. TryResolveCurrentInfoHint recaptures
current visibility and rejects disabled info bars before resolving a source.
It is an owner-thread API, not a network-thread callback. CMB1 is rejected;
tokens/ItemId survive combined interface and host-file serialization. Basic and
rundown payload capture, request admission and client hover remain unconnected.
Focused map contracts passed, including schema rejection and icon-independent
identity roundtrip. Isolated b153 both-role builds passed with installed inventory
unchanged; b152's missing dedicated compile include was corrected. No live game
verification or deployment: installed b150 remains protocol45/bridge40.

Rundown foundation: installed RundownTooltipVM/RundownLineVM and native
RundownTooltip.xml were inspected. The stock movie needs IsActive, IsExtended,
ValueCategorizationAsInt, TitleText, ExpectedChangeText, ExtendText and ordered
Lines(Name, Value, ValueAsString). RefreshExplainedNumberTooltip uses the native
ExplainedNumber.ResultNumber for CurrentExpectedChange, not a sum of display rows.
RundownTooltipVM mutates text templates and global EXTEND_KEY during refresh;
capture must use the existing text-state isolation discipline. Native default
finance CalculateClanGoldChange defaults applyWithdrawals=false; retain that
explicitly, and audit non-default model overrides before invoking callbacks.

`CoopCampaignMapRundownPresentation` now preserves native strings/numeric values
and order, bounded to512 lines/4096-char fields, finite floats, categories0..2,
with deep cloning. `CoopCampaignMapRundownVM` supplies the exact stock bindings
without native finance delegates, timers or registration; it retains native
ValueAsString rather than reformatting on the client. New presentation tests and
the focused map suite passed; isolated b154 compiled both roles without installed
changes. This remains unwired: no serializer, host capture, response correlation,
hover presenter or runtime acceptance yet. Do not claim functioning gold hints.

Rundown serialization follow-up: CMR1 inner presentation codec is implemented
with1MiB total bound, strict UTF-8/boolean/length/category/finite-number validation,
ordered native strings/floats and atomic decode (no partial object on rejection).
It is included in both runtime projects. Focused map contracts passed including
every truncation, trailing data, wrong schema, noncanonical boolean, NaN,
unpaired UTF-16 input and empty inactive presentation. This codec deliberately
does not carry authorization or correlation: enclosing response integration,
host capture, hover routing and native display acceptance remain required.

Response integration: tooltip payload codec v2 admits BarRundown(kind7) only
with a syntactically valid barhint target, zero property rows/mode0, active
CMR1 presentation and matching inner/outer IsExtended. Ordinary kinds reject
attached rundown data. Existing session/generation/sequence/target matching now
also applies to this response variant; v1 payloads are rejected. Focused map
tests passed including full envelope truncation and stale generation/extension/
sequence rejection. Host authorization and publication freshness still belong
to request admission; syntactic token validation alone is not authority. No
host emits this variant yet and the property-row presenter is not its renderer.

Client renderer follow-up: presenter now owns a second private registration for
the stock RundownTooltip movie with detached CoopCampaignMapRundownVM derived
from TooltipBaseVM. It switches movies on response kind changes, updates stable
presentations without reopening, clears only its own tooltip and unregisters only
its own callbacks. Native extension transitions hide stale-mode data until the
matching response arrives. b156 both-role compilation passed unchanged installed
inventories; real presenter/VM with substitute native lifecycle passed123 checks
plus the focused map suite (finance display, stable refresh, exit and return to
property rows). This is not native rendered acceptance. Host finance capture,
bar-hover input and request admission are still not connected.

Hover integration follow-up: info VMs now emit token begin/end callbacks; value
updates retain hover, token replacement/finalization clear it. MapBar suspension
and release clear pending hover. Mission tooltip selection now uses the bar
target while over the stock bar, with existing modal/conversation gates. Client
and server admit exactly one token in the enabled current info bar; server keeps
the same cloned bar it publishes and clears it on reset. Tests exercise actual
info VM callbacks plus disabled/ambiguous target rejection; focused map suite
passed. Host reply capture is still missing, so no functioning info hint is
claimed. Navigation/time/simple hints remain placeholders as well.

Gold capture connected in source: publisher retains its successfully published
bar, admits its token and resolves the exact current native source on owner thread.
TryCaptureBarGold requires native MapInfoItemVM gold, native GetDenarTooltip delegate
shape and exact DefaultClanFinanceModel. It does not invoke opaque delegates:
both normal/detail calculations explicitly set applyWithdrawals=false. A local
unregistered RundownTooltipVM captures native rows/formatting inside existing
text and language scopes; current context/source are checked again afterward.
Cleanup uncertainty permanently disables capture in that process. Audited mutable
templates include finance trade-agreement income in addition to rundown summary
and extension text. Non-native finance overrides are currently rejected, not
silently approximated; wider mod compatibility remains a separate validation gate.
b158 both-role builds and the existing focused suite passed. The suite tests
transport/admission/presenter, not execution of this native finance adapter.
No deployment/runtime acceptance. Other info providers and simple bar hints are
still absent. Protocol46/bridge41 must be deployed coherently for the next test.

Info capture connected: BarInfo(kind8) uses native generic property-tooltip rows
for influence, hit_points, troops, food, morale, view_distance and troop_wage.
Speed reproduces the installed GetPartySpeedTooltip(true) presentation branch
(prisoner message or army-leader/main-party SpeedExplained and native formatting),
omitting only PlayerInspectedPartySpeedEvent. It does not patch ordinary host hover.
Capture runs inside existing text/language scopes and checks exact current source
before/after; native generic provider registration must match. Troop capacity's
str_LEFT_over_RIGHT_no_space is included in writable-template admission. BarInfo
requires a barhint token and mode0; native generic filtering handles extended rows.
Focused map suite passed (910 payload checks). b159 found a missing TooltipProperty
namespace import; corrected b160 compiled both roles with unchanged installed
inventories. Native provider execution, visual parity, remote peers and nonstandard
model behavior remain unverified. Simple/navigation/time hints remain unimplemented.
Installed b150 is still unchanged.

Final aggregate validation: `bar-hints-c141-20261022` completed with all26 canonical
contract projects passing. This supersedes the focused-only validation above,
not the native execution/visual limitations. No game was launched or candidate
deployed by that validation; b150 remains installed and r28 remains closed.

2026-10-21 plunder image source candidate: installed CampaignSystem.DefaultItems
creates16 goods programmatically (grain, meat, planks, felt, hides, tools, iron,
hardwood, charcoal, six iron ingots, trash). They are absent from the campaign
item XML and the previous mirror catalog. Existing XML goods such as fish were
present, explaining selective rather than universal missing icons in r21.
An append-only supplement now provides all16 stable mirror IDs with native mesh,
localized name, value, weight, food flag and civilian trade component. Existing
catalog order/IDs remain unchanged; no renderer-time registration is introduced.
This shared catalog affects any menu containing these goods, independent of
field/village/siege/hideout battle adapters. The focused map suite passed with3304
menu checks after elevated rerun of the same executable (sandbox file access
denied). XML-to-native loading and rendered image parity remain unverified;
the supplement was subsequently deployed in r22 (see below). Preserve both StoryMode and DefaultItems
supplements when regenerating the catalog.

Native loader follow-up: ItemObject.Deserialize accepts IsFood and a Trade child;
TradeItemComponent's native default morale bonus is0, now explicit in XML.
DefaultItemCategories.Wood registers as `hardwood`, not `wood`; both hardwood and
charcoal now reference that exact ID. All16 category IDs and zero morale are
covered by tests. The rebuilt focused map suite passes3320 menu checks on2026-10-21.
Catalog diff is66 appended lines with no prior item reordering or line-ending churn.
Full canonical regression run `goods-c133` subsequently passed all26 contract
projects (terminal Pass, exit0); results are under
`%LOCALAPPDATA%/Temp/CoopSpectator/Automation/goods-c133`.
This does not establish native XML loading or image rendering acceptance.

2026-10-21 r22 is CLOSED. The supplement was deployed with retained b134 DLLs;
campaign46992 loaded and dedicated18676 automatically opened
CoopCampaignMapPrototype. Retained server log records OpenNew returning at
01:09:38.837, mission running at01:09:39.876 and a397-entity catalog prepared at
01:09:39.982. This proves server map startup, not per-item native admission or
client image parity: multiplayer was never launched. The log also contains
assembly/dependency warnings, so this is not a claim of an error-free log.
Cleanup verified all five recorded identities absent and all seven configuration
hashes unchanged; test saves and installed candidate were preserved. See
[r22 observation](../../artifacts/runtime/raid-goods-r22-20261021/OBSERVATION.md).
Keep game processes closed during code work and close test-owned roles after
each live test, per user preference. Next native gate: compare grain/hides/etc.
plunder images and counts against the host, then full raid completion and input
isolation. Existing catalog tests require all16 supplement entries; they guard
against accidental removal during regeneration, not runtime render correctness.

2026-10-21 r20: paired user evidence shows raid progress advancing in campaign
but remaining empty in MP. Installed SliderWidget.ValueFloat explicitly ignores
updates while Locked is true; the replica prefab incorrectly used that flag for
read-only input, and its old source test enforced the mistake. Candidate changes
the shared progress slider to Locked=false, IsEnabled=false, DoNotAcceptEvents=true
and UpdateValueOnScroll=false. This applies to every replicated wait-menu progress
item, including progress-only and progress-with-hours presentations, not only raid.
No protocol or campaign authority changes. The focused map contract project passed
on2026-10-21, including3256 menu checks. Initial sandbox execution stopped at a
file-access denial; the same built test executable passed with elevated access.
Fresh r21 staged the corrected prefab (SHA256
B57F463C3AB23090671475EEB1C9FFA5C9FE1906C7491FCB4A329F98A8FACB70).
Campaign30324 and user-started MP42792 showed progress increasing from a small
nonzero fill to approximately one eighth, with matching plunder counts changing
from7/1 to19/1/2/1/1. MP loaded the retained b134 client MVID. This establishes
bounded native progress advancement, not full raid completion or input isolation.
Some plunder images remain blank despite matching counts. See
[r21 observation](../../artifacts/runtime/raid-progress-r21-20261021/OBSERVATION.md).

This is an unfinished source candidate, not native acceptance of raid/pre-battle UI.
Bounded r17 evidence on b107: native BattleSize=-1 is now admitted (values below
-1 remain invalid); c103 passed all25 projects, including284 menu checks. The
host published Lavenia's six-option menu and MP rendered its context, illustration,
texts and icons. After user-released control, client Leave and Attack clicks had
no observed campaign effect. Host hostile submenu and village-defense context,
Attack / Send troops replicated; host Attack automatically opened empire_village_o.
User-assisted H reached BattleActive. Victory and automatic return to the raid
panel passed; a hostile dialogue subsequently replaced that panel. Progress
advancement remains unverified. Two plunder images were blank in MP. The source
now falls back to an already-loaded static item mirror, as portrait equipment
does; no new native objects are registered. Isolated b109 builds pass, but this
image correction is not deployed or visually accepted.
The encounter upper army/power strip and village raid symbol are absent in the
paired pre-battle MP image: full parity is not accepted. The settlement/notable top strip is not
replicated; the camera control overlaps the menu bottom. Source confirms the
camera control was fixed left/bottom in CoopCampaignMapReplicaInfo.xml.
The source now anchors it bottom-right with a20-unit right margin, retaining the
same action and dimensions. c106 passes all25 projects including a layout-wiring
assertion; not deployed or visually accepted at different resolutions yet. See
[r17 evidence](../../artifacts/runtime/menu-sentinel-r17-20261013/OBSERVATION.md).

### Native encounter overlay investigation (2026-10-13)

Local installed assemblies were inspected with ilspycmd, not inferred from the
menu option list. `SandBox.GauntletUI.Menu.GauntletMenuOverlayBaseView` owns private
`_overlayDataSource` separately from GameMenuVM; its existing EncounterMenuOverlayVM
loads the stock `EncounterOverlay` movie on layer202. Settlement uses the separate
SettlementOverlay movie. Capture must read the already-active source through the
existing MenuViewContext, never construct/Refresh/tick a native overlay on clients.

The stock encounter view binds title, attacker/defender party lists, banners,
count labels, PowerComparer, siege food/morale/wall health, and naval ship counts.
The native source distinguishes MapEvent from SiegeEvent and IsNaval from IsSiege.
Therefore replicating only field troop counts is insufficient: any implementation
must preserve native visibility and presentation for field, village, siege and
naval encounters, with no client campaign queries or actionable encyclopedia links.
Detached encounter/party models now exist in CoopCampaignMapMenuPresentation.cs,
including native display labels, ordered parties/quest markers, portrait/banner
codes, current/initial power, colors, siege and naval values. Validation rejects
invalid lists/numbers/resources without truncation; snapshots deep-copy nested
lists. The encounter is now optional in the menu envelope and encoded atomically
with options under CMN3 / protocol39 / host bridge34. A non-encounter menu cannot
carry an encounter. Old menu formats and incomplete envelopes are rejected;
host capture and detached client UI are now connected in source. Capture reads
the existing GauntletMenuOverlayBaseView._overlayDataSource on the campaign UI
thread, only for encounter menus, and copies existing VM values without invoking
constructors, Refresh, tooltip delegates or strength calculations. Invalid/missing
overlay capture fails closed. Native HeroVM.GetRelation returns -101 for no hero and101 for
the player's hero; both sentinels are admitted and tested (not clamped to100).
Initial b110 isolated builds/c107 all25 contracts passed; c108 also passed all25
projects with the final sentinel refinement. b111 isolated client/dedicated builds
pass with installed inventories unchanged. c109 failed the old protocol38/bridge33
assertion in one project; expected versions were updated to39/34 (catalog9 remains
unchanged). Fresh c110 passed all25 contract projects; b112 host-capture builds
passed for both roles without installed mutation. This is not live capture evidence.
CoopCampaignMapEncounterVM supplies the stock EncounterOverlay movie with detached
party/image/power bindings. It shares the campaign menu layer, hence dialogue/modal/
freshness suspension, and releases the movie on encounter removal and menu cleanup.
Party portrait/banner providers are retained when their codes do not change.
Encyclopedia/context-menu/native tooltip commands are no-ops. Stock disabled hint
bindings are explicitly null; synchronized hover payloads remain unimplemented.
b113 and final b114 compiled both roles with installed inventories unchanged.
c111 passed all25 projects, including1712 menu assertions. These assertions check
source wiring/data contracts, not native behavior.
Native acceptance remains open: paired screenshots, update/close/modal/rejoin,
field/village/siege/naval visual comparisons. Hover tooltip payloads also remain open;
do not invoke native tooltip delegates on the multiplayer client.

r18 b114 native field encounter renders the four-option menu and upper party/power
strip, with22/8 counts and matching names. Full visual parity fails: cropped side
banners, different host portrait equipment/framing, absent crossed-swords world
marker. Installed EncounterMenuOverlayVM constructs both banners with nineGrid:true;
the detached root omitted that flag. Source now enables it for both sides, shared
by field/village/siege/naval presentations; no campaign logic or protocol changes.
Correction b116 passed both isolated builds with installed inventories unchanged;
c112 passed all25 contract projects. Initial b115 could not acquire a sandboxed
CIM resource and is not a build pass. Native correction acceptance remains open;
live b114 is unchanged. Paired images are retained in the r18 artifact directory.
Documentation impact: this section owns the diagnosis; README and r18 observation
index native evidence. Authority/lifecycle documents remain unchanged.

Portrait follow-up (installed native assembly inspection,2026-10-13):
CharacterThumbnailCache.GetPoseParamsFromCharacterCode chooses hero pose as
`lord_` + (MBRandom.NondeterministicRandomInt % 8). CreateCharacterBaseEntity uses
that pose and its matching camera tag; FillEntityWithPose calls the selector again
but discards its outputs. CharacterThumbnailCreationData.RenderId includes the
character code, size and optional dimensions, not the selected random pose.
Thus transmitting CharacterImageIdentifierVM.Id cannot guarantee identical hero
portrait pose/camera across processes. This establishes a framing divergence
mechanism, not proof of incorrect equipment. Do not normalize host equipment or
globally reseed random state to hide it. Exact parity needs host-selected pose
capture/transport and narrowly owned client rendering, including cached portraits
created before an encounter. That implementation remains open; no patch installed.

Native cache control-flow confirmation: OnCreateTexture first queries the exact
RenderId and returns the existing texture (or attaches to pending callbacks) on
a hit. Only a miss reaches CreateCharacterBaseEntity and random pose selection.
Consequently, a menu-open observer alone cannot recover an already-cached pose.

Implementation gates (observer source below; publication/rendering still open):
- Observe native host pose selection from the first thumbnail creation, scoped
  to the actual CreateCharacterBaseEntity call, not the second discarded selector
  call in FillEntityWithPose. Preserve native RNG calls and outputs on the host.
- Associate pose with native cache instance/generation, exact normalized render
  key and size; commit only for the corresponding successful texture request.
  Cache replacement/finalization and failed creation must invalidate evidence.
  Do not infer a pose from hero identity or overwrite it on every menu tick.
- Publish only the pose attached to a currently host-visible portrait. Internal
  observation of other thumbnails must never disclose hidden campaign heroes.
- Preserve pose across item-ID mirror resolution. Client texture identity must
  include the received pose, otherwise two host portraits with the same character
  code can incorrectly reuse an earlier texture. Scope overrides to replica-owned
  rendering requests; never alter unrelated battle/lobby thumbnails or global RNG.
- Validate the finite native hero-pose set and size modes. Unknown cache evidence
  remains explicitly unverified; a deterministic client-only pose is not1:1 parity.
- Cover cache hit/miss, repeated encounter, changed gear, size separation, two
  simultaneous portraits, cache eviction/finalization, cancelled requests and
  field/village/siege/naval encounters. Native rendering remains runtime-sensitive;
  pure contracts cannot close its acceptance gate.

r18 is now closed. All12 recorded identities are absent; the three game roles
exited gracefully, one exact-owned console helper required termination. Seven
configuration preimage hashes match, test saves/Steam preserved. b114 retained;
b116 correction has not been installed. See the r18 observation and cleanup.json.

Portrait evidence foundation (2026-10-13, not wired to runtime):
Infrastructure/CoopPortraitPoseEvidence.cs provides a bounded per-cache registry
of exact render keys, monotonically increasing creation tickets and completed
hero poses0..7. Pending requests are not evidence; replacement, cancellation,
clear and finalization reject old callbacks. Identical duplicate completion is
idempotent; a different second pose cannot rewrite a completed observation.
Default capacity512 and key cap8192 bound retained strings; capacity exhaustion
rejects new observations without evicting valid entries. It owns no game objects,
network publication or random state. PortraitPoseEvidenceTests covers admission,
size-separated keys, replacement, late callbacks, limits and finalization.
b117 both isolated builds passed with installed inventories unchanged. Contract
run c113 passed all25 projects; no native observer, transport or rendering override is yet
connected, so this is not a portrait parity fix.

Additional installed-native inspection: ThumbnailCache<T>.OnThumbnailRenderCompleted
dispatches callbacks only for a registered renderId and returns whether it found
them. Clear and RemoveThumbnailCacheNode invoke OnRequestCancelled(key);
Destroy invokes OnFinalize. These are candidate lifecycle observation boundaries,
not installed hooks. Generic method patch sharing must be audited before use;
callbacks must remain restricted to the concrete character cache instance.

### Gated portrait observation candidate (2026-10-13)

`Patches/CoopPortraitPoseObservationPatch.cs` now connects the evidence registry
to character-cache creation, the first effective pose selection, successful
non-null texture completion, eviction, clear and destruction. SubModule calls
Apply, but installation requires both the map prototype flag and
`COOPSPECTATOR_PORTRAIT_POSE_OBSERVER=1`. Default behavior remains unchanged.
Observation is campaign-only, hero-only and concrete-character-cache-only; it
does not alter selector outputs, native random calls, texture callbacks or
exceptions. Pending frames are thread-local; cache ownership uses weak keys.
There is no host-visible portrait lookup integration, network publication or
replica pose override yet. Earlier foundation-only statements above are historical.

`Tests/PortraitObservation.ContractTests` executes actual Harmony patches on
minimal game API substitutes: 14 checks cover first-vs-discarded selection,
completion, cache hits, eviction, clear/late completion, original exceptions,
null textures, multiplayer exclusion, recovery, destruction and other caches.
c115 failed because an inherited reflected target was rejected by Harmony.
Targets now resolve to their declaring implementation before patching; c116
passed all26 projects. These are substitute-method tests, not installed-native
rendering or generic-code-sharing acceptance. b120 stopped at restricted CIM
access before compilation; fresh b121 compiled both roles and confirmed unchanged
installed module inventories. Neither observer nor banner fix was deployed;
b114 remains installed and r18 remains closed.

### Visible encounter portrait pose transport (2026-10-13, candidate)

The current source supersedes the preceding no-publication boundary for encounter
parties only. `CoopCampaignMapMenuCapture.CapturePortraitPose` reads an already
visible party VM code, creates detached managed CharacterThumbnailCreationData
in native small/no-custom-size mode to obtain the exact normalized key, then
looks up completed evidence in the current manager's sole registered character
cache. It never queues a render or mutates the source VM. Missing evidence,
non-hero portraits or ambiguous caches yield -1, not a guessed pose. Native
constructor normalization was rechecked against installed View.dll on2026-10-13;
GameMenuPartyItem.xml has no IsBig or custom-size overrides.

`CoopCampaignMapEncounterParty.PortraitPose` admits -1 (unverified) or0..7; a hidden
portrait cannot carry a pose. Clone and the atomic menu codec preserve it. The
menu format is now CMN4, protocol40/bridge35/catalog9, requiring coherent roles.
This travels through the existing menu bridge and mission transport, with no new
hero discovery or battle adapter. Capture remains behind the explicit observer
gate. Field/village/siege/naval contract cases cover pose roundtrip alongside
the encounter; the renderer still ignores the new value and parity is NOT fixed.
b122 passed both isolated builds with installed inventories unchanged. c117
passed all26 contract projects, including17 patched-substitute observer checks.
Native capture and visual acceptance remain pending; installed b114 is unchanged.

Next rendering boundary: the native ImageIdentifierTextureProvider owns request
release/callback lifecycle; ThumbnailCreationData.RenderId has a protected setter.
A replica-owned derived request can therefore separate pose-specific cache keys
without changing host/global keys. Such a provider and narrowly scoped pose
override are not implemented yet; registration and native lifecycle need review.

### Replica portrait rendering candidate (2026-10-17)

The rendering boundary above is now implemented in source, not native-accepted.
`CoopReplicaPortraitTextureProvider` derives from the stock
ImageIdentifierTextureProvider; its base retains callback/refcount/release/disposal
ownership. Installed TextureProviderFactory.RefreshProviderTypes discovers
non-abstract providers from loaded assemblies by type name. The client references
the installed Native GauntletUI assembly with Private=false; it is not packaged.
`CoopReplicaPortraitCreationData` is a CharacterThumbnailCreationData subclass
with the received finite pose appended to the native normalized render key.
Installed ThumbnailCache<T> accepts assignable requests for create and release.
This separates ordinary thumbnails, distinct poses, equipment and sizes.

`CoopReplicaPortraitPosePatch` has its own Harmony owner and the same explicit
default-off observer gate. Only multiplayer creation of the internal replica
request enables a thread-local frame; only its matching character/cache inside
CreateCharacterBaseEntity may replace the pose output. Native selector calls
still execute; the later discarded selection, ordinary thumbnails and campaign
outputs stay unchanged. Finalizers restore the outer scope and propagate original
exceptions. Installation failure removes only this patch's hooks; the detached
VM retains the stock provider if hooks are unavailable or pose is unknown.
Encounter VM invalidates the portrait on code OR pose change. Other HUD portrait
consumers are unchanged and still require separate pose transport integration.

Native risk/acceptance: provider discovery, release/rejoin, simultaneous portraits,
mirrored equipment, small/large size, and paired field/village/siege/naval panels
remain open. Substitute tests are not native rendering proof. b123 failed an
ambiguous FaceGen type reference; qualification to TaleWorlds.Core corrected it.
b124 passed both isolated builds with installed inventories unchanged. c118
passed all26 contracts (23 observer/override checks); final c119 also passed all26
with the strengthened exception-scope assertion before packaging. No deployment occurred.

Follow-up `portrait-nested-c120-20261017` terminated successfully: all26 contract
projects passed, including29 patched-substitute portrait checks. Six additional
checks cover nested replica requests, nested ordinary requests, caught inner
failure, cross-thread isolation, cache-hit selector suppression and scope cleanup
after a hit. These are managed substitute tests, not native renderer acceptance.
The delivered b124 r4 archive is unchanged; no installed files were modified.

### World battle marker native boundary (2026-10-17 research)

Latest validation: settlement-invalid-c131-20261017 passed all 26 projects.
Settlement map-event transport now has rejection tests for absent fields,
negative/out-of-range/overflow/non-numeric/empty values on both full and dynamic
bridge paths for every valid event type. This proves decoder rejection, not
immediate removal of previously displayed settlement icons after corruption;
native stale-display duration still needs paired observation. No production
changes after b133; b131 package remains immutable and installed state untouched.

Subsequent b134 source candidate adds settlement-event-only freshness gating:
nameplates retain the host event type separately and expose zero when hidden.
The view applies the existing <2-second map-state receipt plus completed catalog/
dynamic revision predicate after each tick, and hides events during dialogue or
before render readiness. Labels and other nameplate fields are unchanged. Fresh
coherent state restores the retained event. Both isolated builds passed with no
installed mutation; native stale/recovery and flicker checks remain open. This
candidate is not in b131 and supersedes the prior ungated-settlement limitation.

Post-b131 camera-order correction: `OnMissionScreenTick` projects battle markers
in a finally block after map/camera updates. This removes the previous-frame
camera projection while preserving readiness/freshness hiding on early returns.
`marker-camera-b132-20261017` compiled both roles with installed inventories
unchanged. Native visual acceptance remains open. This source-only change is not
in the immutable b131 VillageFix preview; protocol/layout are unchanged.
`marker-camera-c129-20261017` passed all 26 contract projects, including source
guards for final-camera ordering, one marker update per frame, dialogue/freshness
hiding and scene release. These guards do not prove native pixel parity.

Transport scheduling audit: mission finalization calls ResetServerBattleMarkers
and client ResetClientConversation -> ResetClientBattleMarkers. Synchronized
peer admission calls SendBattleMarkers after state/catalog/conversation/menu.
Host-session changes force a new monotonically increasing marker revision rather
than reusing revision 1 inside the same mission. Frequent-change stress remains
open: a maximum uncompressed 8,204-byte list needs 33 chunks / 17 server ticks at
two chunks per tick. Host publication is every 0.2 seconds, server polling every
0.1 seconds, and each content change cancels pending sends. Thus a changing large
list can starve completion on a slow server. Do not treat pure assembler tests
as scheduler liveness proof. Any correction must preserve immediate disclosure
revocation; finishing obsolete lists blindly could reveal removed events.

Follow-up source candidate removes that two-chunk scheduling starvation: each
queued list is sent within one tick, capped by ceil(8204/256)=33 chunks per peer.
The send tick still precedes the bridge poll; any newly observed disclosure
change cancels queued old lists and the client retains newer-envelope revocation.
This is up to 8,204 payload bytes per peer per changed list (plus message overhead),
not a bound on total traffic across peers. No wire format change. b133 both
isolated builds passed with installed modules unchanged; marker-send-c130-20261017
passed all 26 contract projects. Native many-peer bandwidth/latency acceptance remains open. The b131
archive does not contain this candidate.

Asset audit against installed 1.4.8 files: SandBox/GUI/Prefabs/Map/MapEventVisuals.xml
binds MapEvents, Position, IsVisibleOnMap and EventType to the same stock widgets
used by the detached VM. Native/GUI/NativeSpriteData.xml declares
SPGeneral/MapEvents/battle_icon (backslash separators in XML), 144x106, sheet2,
category ui_group1; SandBox Nameplates.xml references that sprite in the event
brush. Decompiled GauntletMapEventVisualsView loads the stock movie on the map
nameplate layer and does not explicitly load a sprite category itself. This proves
asset/binding availability on disk, NOT that ui_group1 is resident in every MP
entry path. Do not add global sprite unload/reload speculatively; native rendering
and reconnect acceptance must establish residency/ownership first.

Read-only inspection of installed `SandBox.ViewModelCollection.dll` types
`SandBox.ViewModelCollection.Map.MapEventVisualsVM`, `MapEventVisualItemVM`,
`SandBoxUIHelper.GetMapEventVisualTypeFromMapEvent`, and SandBox's
`GUI/Prefabs/Map/MapEventVisuals.xml` establishes:

- The standalone event list excludes every event with non-null
  `MapEventSettlement`, and removes an existing entry if it becomes settlement-related.
  Do not generate one crossed-swords marker per fighting party: that duplicates
  shared events and bypasses the settlement-event presentation boundary.
- Native type selection returns Battle for non-settlement events; settlement
  events select Siege for siege assault/outside, SallyOut for sally/blockade
  sally-out, otherwise Raid. This mapping is not proof that the standalone movie
  renders settlement events; its list explicitly excludes them.
- Visibility combines host event disclosure (`MapEvent.IsVisible`, cached by
  UpdateProperties) with projection W > 0 and camera Z < 200. Position is projected
  from event campaign position converted to Vec3 plus a 1.5-unit vertical offset,
  through WorldToScreenInsideUsableArea.
- The stock movie binds MapEvents / Position / IsVisibleOnMap / EventType to
  MapEventVisualItemWidget and MapEventVisualBrushWidget, using
  MapEvent.Visual.Brush at 80x59 with centered/top alignment and no input events.

Implementation remains open. Capture must retain host disclosure independently
of viewport culling, transport bounded detached world positions/types, and apply
the replica camera's projection for manual exploration. Never instantiate native
campaign event models in multiplayer or enumerate undisclosed events. Settlement
raid/siege markers require their separate nameplate path audit. Required gates:
event deduplication, hidden/revealed/end removal, field versus settlement/naval
classification, zoom cutoff, detached/host camera, dialogue suspension and rejoin.

Source foundation `Infrastructure/CoopCampaignMapBattleMarkers.cs` now provides
immutable detached event coordinates, host-disclosure/non-settlement admission,
the native camera cutoff and a bounded atomic full-list snapshot validator.
The 512-event transport safety cap is a proposed bound, not a measured native
maximum; integration must fail closed and expose overflow, never silently truncate.
`BattleMarkerTests` covers disclosure combinations, projection boundaries,
nonfinite coordinates, duplicate IDs, detached list ownership, empty clearing and
capacity. Capture, transport, event-ID lifetime and stock movie rendering remain
unimplemented; this foundation has no runtime callers and changes no protocol.
Validation run: `map-markers-c121-20261017` passed all26 contract projects.

`Campaign/CoopCampaignMapBattleMarkerCapture.cs` adds an unwired campaign/UI-thread
capture adapter. It reads only the existing GauntletMapEventVisualsView data source,
checks prepared and current host disclosure, excludes settlement events, deduplicates
event objects and copies finite world coordinates. Missing metadata/view or overflow
returns failure with an empty output, never a retained old list. Outside the map or
during conversation it returns an explicit empty snapshot. Metadata is cached but
no campaign view/event survives the call. IDs are snapshot-local: the eventual
consumer must replace lists atomically, not interpolate across snapshots by ID.
No capture caller, wire format or UI renderer is connected yet. Isolated compilation
`map-markers-b125-20261017` passed both roles, with installed module inventories
unchanged; no deployment or native acceptance.

`CoopCampaignMapBattleMarkerCodec` now defines the unwired CBM1 full-list payload:
12-byte magic/revision/count header and 16 bytes per immutable ID/XYZ entry,
maximum8204 bytes. Exact expected revision, size, count, finite coordinates and
unique positive IDs are validated before publication. Empty lists explicitly clear
presentation. The outer transport still needs session authentication, revision
ordering, freshness and bounded fragmentation; this payload must NOT be appended
unbounded to the existing single state packet. No current protocol change or
network caller exists. Tests include all one-entry truncations, trailing data,
wrong magic/revision, nonfinite wire coordinates, count overflow, empty and maximum
roundtrips. `map-markers-c122-20261017` is running; no pass is claimed yet.

Correction: c122 terminated with one failed project: the settlement marker test
still asserted bridge35 while source had advanced to36. This exposed that a live
aggregate run must not be assumed to freeze every later source read. Its failure
is retained, not a pass. The stale assertion is corrected; final validation must
use a fresh run after edits finish.

`CoopCampaignMapBattleMarkerAssembler` adds bounded one-list chunk assembly using
the existing hash-verified accumulator. It enforces the marker-specific logical
and wire limits, accepts out-of-order chunks, publishes only a decoded complete
list, rejects completed/older revisions, and exposes Reset for mission/session
ownership. Tests cover reverse order, no partial publication and late old data
after an empty clear. Native message/controller wiring remains open. These latest
assembler edits are not covered by a completed validation run yet.

Native network candidate now registers a server-only marker chunk message and
controller partial, queues the latest full list for synchronized peers/rejoin,
clears on host-source loss, resets on mission/client-conversation boundary, and
uses separate monotonic transfer revisions. Empty lists/session changes preempt
pending sends; other updates finish the current transfer then send the latest.
Client publication is complete-list only; the future renderer must enforce host
freshness and dialogue suppression. Protocol41/bridge36 now require coherent roles.
No rendering is connected yet. c123 passed26 projects before this network edit;
c124 is still running and cannot establish final-network-source acceptance.

Rendering follow-up: b127 passed both isolated network builds without installed
changes. c124 failed the protocol-version assertion during changing-source work;
it is not acceptance. New detached `CoopCampaignMapBattleMarkersVM` exposes the
stock MapEventVisuals bindings and native Battle enum, projects host world positions
through the replica camera with the native1.5 offset/200 height cutoff, and owns
no campaign objects. `CoopCampaignMapPrototypeMissionView.BattleMarkers.cs` loads
the stock movie in a nonfocus layer, hides it on missing/stale data or dialogue,
and releases its movie/list/layer with scene teardown. Native availability, visual
parity, input, event removal, rejoin and battle return are NOT verified.
Fresh b128 compilation and c125 full contracts are running after source edits.

b128 terminated successfully for both roles with installed inventories unchanged.
c125 remains live; keep its source stable until terminal completion. Read-only
review found an outstanding disclosure gap: CurrentClientBattleMarkers retains
the preceding complete list during a newer partial transfer. Before native
acceptance, invalidate that presentation on first accepted newer revision, retain
the latest-revision tombstone after corrupt completion, and reveal only a complete
valid list. Add regression coverage for newer partial -> corrupt completion ->
old replay -> valid retry. Server update pacing/removal preemption also needs
review; generic menu progress policy is not sufficient proof for event disclosure.

c125 terminated Pass for all26 projects on the stable b128-era source. Follow-up
now gates rendering on assembler HasCompletedLatest: admission of a newer envelope
revokes the preceding list, including after corrupt completion; valid retry of the
latest revision restores eligibility. Server changes preempt pending old marker
transfers. Tests cover reset/partial/corrupt-newer/valid-retry visibility. c126 is
running for this correction; new build/native acceptance remain pending. Frequent
change starvation and bounded network latency still require live verification.

b129 passed both isolated builds for the latest-display correction; installed
inventories remain unchanged. c126 remains live, not yet a pass.

Adjacent settlement gap confirmed by read-only source/native inspection:
CoopCampaignMapPrototypeSettlementOverlayVM.MapEventVisualType is hardcoded0.
Installed SettlementNameplateVM instead receives native event callbacks: siege
start sets2, map-event start uses SandBoxUIHelper's event mapping, map-event end
sets0, rebellion formation sets4, and the final rebellion removal retains2 if
still besieged or clears0. The stock small/medium/large nameplate prefabs all bind
this value through MapEventVisualBrushWidget. Nameplates.xml maps native styles
to SPGeneral/MapEvents raid/siege/battle/rebellion/sally_out sprites. This is a
separate missing replication field, not fixed by standalone field battle markers.
Capture should copy the existing host nameplate's prepared value and preserve its
visibility policy, rather than reimplement campaign event callbacks on clients.
Settlement visibility/refresh initialization and resource availability still need
inspection before integration. No settlement event implementation is claimed.

c126 completed Pass for all26 contract projects; b129 both-role builds already
passed. This is source/build/contract evidence only, no native marker acceptance.
Further native inspection: SettlementNameplatesVM registers siege/rebellion
callbacks for existing admitted nameplates and initializes siege/rebellion values
when binding. MapEventVisualType is not the quest/production EventsList and is
not cleared by IsInRange's event unloading. Therefore do not apply quest-range
filtering to this field or infer it from generic raid state. Copy the prepared
native value from the existing host nameplate, admit only disclosed settlements,
and retain normal nameplate camera visibility on the client. Native event codes
are None0/Raid1/Siege2/Battle3/Rebellion4/SallyOut5. Settlement event-end and siege-end
callbacks can clear or replace the value; dynamic transport must include zero
changes and hidden-state clearing, not only nonzero catalog additions.

Settlement event source integration now adds SettlementMapEventType (0..5) to
visible/dynamic state, clones, delta equality/removal, host text serialization,
both native packet layouts and projection. Publisher copies the existing host
nameplate's value only on the admitted settlement path; missing/invalid values
clear to0. The client stock nameplate binding now changes with that field instead
of always returning0. Hidden/nonsettlement dynamic injection is rejected. No
campaign callback is invoked or reproduced on clients. Protocol42/bridge37,
catalog9; coherent replacement required. SettlementMapEventTests covers all six
types, all36 transitions, full/dynamic roundtrip, clone/projection/removal and
invalid/hidden/party injection. b130/c127 are running; no native acceptance yet.

Host bridge integration follow-up: publisher now captures the list on map publication,
clears it during conversation, and defaults non-map snapshots to empty. Host snapshot
clone isolates the array; validation rejects invalid lists and active-dialogue markers.
Bridge schema36 requires a canonical Base64 CBM1 field bound to the host revision.
The dedicated project includes both pure marker classes. Network protocol remains40:
server-to-client marker transfer/rendering is still absent. All installed roles must
be rebuilt coherently for this source candidate; b124 r4 remains unchanged.
`map-markers-b126-20261017` passed both isolated builds with installed inventories
unchanged. Fresh `map-markers-c123-20261017` is running for the bridge tests;
c122 started before this integration and cannot validate it.

`Campaign/CoopCampaignMapMenuCapture.cs` reads the existing active MapScreen
MenuViewContext/GameMenuVM on the host. It never constructs or refreshes native
campaign menu models or invokes their option conditions/actions. Missing or
invalid capture becomes an explicit close; dialogue and mission contexts suppress
this map menu. The publisher places the detached result in the host snapshot.

Input follow-up: `Module/CoopSpectator/GUI/Prefabs/CoopCampaignMenu.xml` retains
the installed stock GameMenu layout with its progress slider locked. The view loads
this movie and excludes pointer-over-menu from map picking/free-camera input without
disabling the existing Escape path. This new prefab MUST be staged with the candidate;
the historical seven-file staging inventory is insufficient. `menu-input-b106-20261013`
passed both isolated builds, and the map suite passed281 menu checks including XML
and source-wiring checks. This does not prove native hit-testing or visual parity.

`Infrastructure/CoopCampaignMapMenuPresentation.cs` owns bounded ordered options,
disabled appearance/hints, percentage progress and plunder item IDs/counts.
`CoopCampaignMapMenuCodec` uses strict UTF-8, canonical booleans, bounded counts,
revision binding and exact payload consumption. Null means an explicit close.
Host bridge schema33 and network protocol38 are required for this candidate;
catalog schema9 additionally carries settlement raid state. Do not mix candidate and previous roles.
`CoopCampaignMapMenuAssembler` publishes only complete digest-verified payloads,
rejecting old revisions and preserving an explicit close over late old packets.

`Network/Messages/CoopCampaignMapMenuNetworkMessages.cs` and
`Mission/CoopCampaignMapPrototypeNetworkController.Menu.cs` carry the server-owned
menu on the existing synchronized-peer reliable channel. Unchanged payloads are
not resent; late joins receive the latest payload. Same-window updates preserve
each in-flight snapshot, then enqueue only the latest revision on completion;
close, changed MenuId and forced session boundaries preempt immediately.
`CoopCampaignMapMenuSendPolicy` supplies the tested preemption rule. Slow-link
native validation remains open. Client state resets with map/conversation
teardown. Two-second host-state freshness gates presentation.

`UI/CoopCampaignMapMenuVM.cs` binds the stock-derived CoopCampaignMenu movie using detached models;
all action/link callbacks are no-ops. Plunder icons resolve local ItemObject IDs,
not paths or remote providers. Item tooltip callbacks intentionally remain no-ops.
`CoopCampaignMapPrototypeMissionView.Menu.cs` owns the client layer, suspends it for
conversation/local modal UI and releases it with the scene. Input isolation, native
movie resource availability and progress-widget interaction remain unverified.
Quest/shortcut indicators and encounter party/power overlays are not yet captured.

Raid world effects are connected in source, NOT native-verified. Installed SettlementVisual.RefreshPartyIcon
distinguishes active village MapEvent.IsRaid (`psys_fire_smoke_env_point`) from
completed Settlement.IsRaided (`map_icon_village_plunder_fx`), with active raid
precedence. Evidence: retained decompilation under
`artifacts/native/settlement-presentation-20260927`. Do not equate either effect to
quest/event nameplate flags or invoke RefreshPartyIcon to obtain a snapshot.
`CoopCampaignMapRaidPresentationPolicy` now defines unknown/clear/active/raided
states and the two fixed particle IDs. Publisher capture runs only for admitted
settlements; both bridge forms, both network entity messages, catalog schema9,
clones, catalog-delta comparison and visible projection carry SettlementRaidState.
Validation rejects unknown values and nonzero party states. The focused geometry
tests cover all16 visibility/village/active/raided combinations and clone/delta
preservation. The map suite and isolated `raid-transport-b103-20261013` both-role
build passed without installed changes. The geometry admission now invokes the
owned-root ApplySettlementRaid adapter only for changed state after successful
geometry admission. Registration clears authored emitters; disclosure removal and
failed geometry admission clear prior effects. A failed effect hides the root and
is retried on a subsequent update. Unchanged states do not restart emitters.
Scene release discards bindings before destruction of the owned scene; callbacks
never dereference released roots. Tests cover these transitions, not native particle
lifetime. The adapter uses only fixed particle IDs, clears root particle systems,
removes native predisplay status for active effects and checks effect resources.
Compile attempt `raid-effects-b104-20261013` failed on the signed complement cast
to EntityFlags; source now uses the correct unsigned mask. Fresh
`raid-effects-b105-20261013` passed both isolated builds with installed inventories
unchanged. Native visual placement,
resource availability and ending/recovery parity still need paired observation.

Shared scenario audit: map GameMenu presentation is not specific to field battles;
village, siege, sally-out, relief and hideout entry menus can also use it. No battle
adapter, result processing or decision authority is changed. Required native gates:
raid start/progress/loot/interruption/end; encounter enabled/disabled options;
dialogue and local Options overlap; map/battle/map; late join/rejoin and host loss.
Also prove client clicks do not affect campaign decisions or underlying map input.

Verification: `MenuPresentationTests` in the map contract project passed279 checks
including deep copy, malformed/truncated payloads, Unicode, reordered chunks,
corrupt digest, conflicting metadata, close/replay, size limits and send preemption. The full map
contract executable passed. Isolated `menu-plunder-b101-20261013` compiled both
roles with installed inventories unchanged. Subsequent `menu-send-b102-20261013`
also compiled both roles after the send-preemption correction, again without
installed mutation. No deployment or game launch occurred
for this candidate. Subsequent `raid-menu-c101-20261013` passed all25 canonical
contract projects after raid rendering integration. Windows Computer Use capability
was revalidated read-only; no Bannerlord window was returned. Full Goal and approved
menu/raid requirements remain open: paired runtime acceptance is still required.

Intermission culture correction: `Patches/IntermissionVmCrashGuardPatch.cs` contains
the exact-shape two-lookup rewrite and read-only current-option resolver;
`Tests/IntermissionCulture.ContractTests/` tests shape admission and option semantics
with stubs. See [candidate and native gates](BUILD_TEST_DEBUG.md#intermission-culture-correction-candidate-2026-10-11-not-deployed).

`CoopCampaignMapConversationMissionScene` extends the tableau descriptor with
allowlisted land-scene/camera/climate data. `CoopCampaignMapMissionConversationVisualCapture`
reads the admitted native camera/visible actors; tableau and pose schema2 carry
resource identity and separate motion. `CoopCampaignMapConversationTableauView`
owns the terrain-scene branch. `ConversationMissionSceneTests` covers malformed
data, bounded framed poses and source wiring. Full lighting/mount/crowd/native
acceptance is open; see [current limits](CAMPAIGN_MAP_MISSION_DIALOGUE.md).

`Campaign/CoopCampaignMapConversationPublicationLease.cs` is a pure
map-anchor lifetime boundary for standalone companion dialogue. It binds exact
campaign/session/mission identities, revokes on context loss and never refreshes
snapshot timestamps. `ConversationPublicationLeaseTests` covers its boundaries.
The existing OpenNew postfix now binds it through PublisherBehavior.MissionConversation;
MissionConversationContext validates the native role/type/view and ConversationCapture
reads prepared mission text/HUD. Bounded mission 3D rendering is connected in
source through the types above, but not native-verified; see
[native investigation and next gates](CAMPAIGN_MAP_MISSION_DIALOGUE.md).

`GameMode/CoopCampaignMapLobbyLifetimePatch.cs` binds before server map opening;
`Infrastructure/CoopCampaignMapLobbyLifetimePolicy.cs` excludes only that map's
Playing lobby tick from stock match expiry. `LobbyLifetimeTests` checks gating
and wiring. See [failure and native gates](CAMPAIGN_MAP_LOBBY_LIFETIME.md).

`CoopCampaignMapSettlementEventPolicy` bounds event/quest bits by host inspection
or spotting; publisher and settlement overlay consume it. Exhaustive masks and
wiring: `SettlementEventDisclosureTests`. See [disclosure](CAMPAIGN_MAP_EVENT_DISCLOSURE.md).

`Infrastructure/CoopCampaignMapSettlementMarkerOrder.cs` is the pure bounded
ordered-event/quest representation; `SettlementMarkerOrderTests` enumerates every
ordered subset and malformed-input/disclosure cases. The publisher now reads the
existing host UI lists; full/dynamic contracts and message codecs carry their order,
and the settlement overlay preserves it without rebuilding unchanged lists.
`SettlementMarkerTransportTests` covers clones, codecs, order-only deltas, clearing,
party isolation and native message source wiring. Source format is32/27/8; installed
candidate is now cmp-b65 (32/27/8), staged for a fresh native run; acceptance is
pending. See integration/native gates in the same document.

`CoopCampaignMapEscapeInputPolicy` queues one owned-layer release for next-frame
native menu dispatch. Map and conversation views each own a policy instance;
conversation presentation defers queued host revisions while local modals own the
screen, including the native same-screen MissionOptions layer. The isolated map
mode explicitly adds the stock options view only on the client. `EscapeInputTests`
covers dispatch/cancellation and options/modal source wiring. See
[source correction and native gates](CAMPAIGN_MAP_ESCAPE_INPUT.md).

`Infrastructure/CoopCampaignMapMovementTarget.cs` defines detached host target
selection, hover suppression and decal layout; its Codec defines bounded wire,
disclosure and readiness rules. PublisherBehavior.MovementTarget captures host
state; state message/controller transport it; MissionView.MovementTargets owns
replica decals and teardown. MovementTargetTests and MovementTargetTransportTests
cover the managed rules. See [scope and native gates](CAMPAIGN_MAP_MOVEMENT_TARGETS.md).

`Infrastructure/CoopCampaignMapHoverDecal.cs` defines the bounded detached native
hover-frame/color descriptor. Host publisher captures it; snapshot/catalog codecs
and projection carry it; `UI/CoopCampaignMapPrototypeMissionView.HoverDecals.cs`
owns scene-local rendering and teardown. `HoverDecalTests.cs` covers wire validity,
transport and delta propagation. See [scope and native gates](CAMPAIGN_MAP_HOVER_DECALS.md).

`CoopCampaignMapPartyPickPolicy` in the prototype contract separates ordinary
hover admission from visible bodies: non-main rafts cannot be hovered. Picking,
pinned info and shared tooltip target admission consume it; host capture also
checks the current native raft flag. See the dated source-only refinement.

Native picking candidate: `UI/CoopCampaignMapPrototypeMissionView.Picking.cs`
owns map-scene ray hits, referenced settlement geometry and owned party spheres;
`Infrastructure/CoopCampaignMapPickResolver.cs` resolves bounded admitted hits in
audited native order. `PickResolverTests.cs` covers pure decisions and wiring.
Source/build checks are not native acceptance. See [scope and gaps](CAMPAIGN_MAP_RESUME_2026-09-26.md#native-scene-picking-candidate-2026-09-28-not-runtime-verified).

Current tooltip integration: controller partial .Tooltips owns exact-peer grants,
relay scheduling and unicast chunks; CoopCampaignMapTooltipNetworkMessages defines
three native messages; CoopCampaignMapTooltipChunks owns bounded assembly.
UI/CoopCampaignMapTooltipPresenter.cs uses the stock movie for detached rows;
MissionView supplies hover and lifecycle cleanup. TooltipPresenterTests compiles
the real presenter with isolated test doubles. Earlier "unwired" entries below
are historical. See [current implementation and gates](CAMPAIGN_MAP_RESUME_2026-09-26.md#tooltip-stock-presenter-candidate-2026-09-28-not-native-verified).

`CoopCampaignMapTooltipRelay` defines the fixed local binary channel;
`CoopCampaignMapTooltipRelayClient` is the not-yet-network-connected dedicated
endpoint. `Campaign/CoopCampaignMapTooltipHost.cs` owns launcher Prepare/Attach
and host tick capture. See [relay candidate](CAMPAIGN_MAP_RESUME_2026-09-26.md#launcher-owned-tooltip-relay-2026-09-28-source-candidate).

`CoopCampaignMapTooltipWireCodec`/`CoopCampaignMapTooltipHandshake` define bounded
semantic messages and reject grants from a previous client challenge. Publisher
`TryCapturePublishedTooltip` applies `CoopCampaignMapTooltipTargetAdmission` before
native target lookup/capture. These have no live relay/network/UI caller. See
[envelope and host-entry candidate](CAMPAIGN_MAP_RESUME_2026-09-26.md#tooltip-semantic-envelopes-and-host-lookup-2026-09-28-unwired).

`Campaign/CoopCampaignMapTooltipCapture.cs` is a host-only native-provider
adapter reached by the new source host relay, with disclosure/routing checks and
detached row capture; native MP submission/acceptance is still open. The pure
`CoopCampaignMapTooltipTextTransaction<T>` restores variable entries and idle
parser stacks. `CoopCampaignMapTooltipLanguageState` plus the pure thread-state
scope isolate audited grammar fields and reject shared-empty writable templates.
This is not arbitrary nested-state isolation; native gates precede live wiring. See
[candidate and safety boundary](CAMPAIGN_MAP_RESUME_2026-09-26.md#host-native-tooltip-capture-candidate-2026-09-28-unwired).

`Infrastructure/CoopCampaignMapTooltipInterest.cs` owns pure client correlation,
request-age expiry and detached display; `CoopCampaignMapTooltipPeerGate.cs` owns
one exact peer/map/session grant and bounded pending capture. Both are unwired;
their dedicated contract tests are not network/native acceptance. See
[lifetime boundary](CAMPAIGN_MAP_RESUME_2026-09-26.md#tooltip-request-lifetime-boundary-2026-09-28-candidate).

`Infrastructure/CoopCampaignMapTooltipPayload.cs` is an unwired pure detached-row
format and bounded UTF-8 codec; `TooltipPayloadTests` exercises presentation
preservation, request correlation and hostile input. No host capture, transport
or UI integration exists yet. See
[foundation and native-provider risks](CAMPAIGN_MAP_RESUME_2026-09-26.md#tooltip-provider-isolation-and-detached-payload-foundation-2026-09-28).

`CoopCampaignMapReplicaStateProjection.BuildVisible` projects latest admitted
catalog/dynamic stores when MissionView drains deferred reconciliation, so queued
clones cannot overwrite newer visibility/entry/exit/membership. PartyBodyTests
covers temporal projection and consumer wiring. See
[ordering correction](CAMPAIGN_MAP_RESUME_2026-09-26.md#deferred-reconciliation-ordering-correction-2026-09-28).

`CoopCampaignMapPartyBodyPolicy` (prototype contract) separately gates mobile
bodies using InSettlement. MissionView applies it to allocation/render/fallbacks,
hover and upsert-time panel invalidation; InfoVM tracks DisplayedEntityId.
`PartyBodyTests` covers admission transitions and source wiring. See
[correction and validation](CAMPAIGN_MAP_RESUME_2026-09-26.md#mobile-body-admission-correction-2026-09-28).

cmp-b10: Publisher.CaptureSettlementPresentation reads disclosed host wall health
and siege preparation inputs. SettlementPresentationPolicy bounds/codifies them;
MissionView.ApplySettlementPresentation applies wall then preparation overrides
after level filtering, before owned-root reveal. Full/catalog/binary/native paths
carry both fields (25/21/6). See [implementation and bounded native result](CAMPAIGN_MAP_RESUME_2026-09-26.md#wall-and-siege-preparation-candidate-2026-09-27).

MissionView.RemoveFortificationAuthoringHelpers snapshots exact startup helper tags
under owned town/castle roots. SettlementAuthoringPolicy selects topmost native
removal roots before mutation. See [cmp-b9](CAMPAIGN_MAP_RESUME_2026-09-26.md#fortification-authoring-helper-candidate-2026-09-27).

Settlement levels: SettlementLevelPolicy (in the geometry admission file) maps
semantic/native masks; Publisher.CaptureSettlementLevels reads admitted host
visuals; MissionView.ApplySettlementLevels changes only owned scene children.
Protocol 24/bridge 20/catalog 5 carry SettlementLevelFlags through existing paths.
See [implementation and bounded result](CAMPAIGN_MAP_RESUME_2026-09-26.md#host-selected-settlement-levels-2026-09-27-bounded-native-result).

CoopCampaignMapSettlementGeometryAdmission<T> owns pure per-scene root admission;
SettlementGeometryTests covers transitions/wiring. MissionView discovers hidden
roots, reveals typed settlement upserts, hides removals and clears before disposal.
See [candidate and limits](CAMPAIGN_MAP_RESUME_2026-09-26.md#static-settlement-geometry-admission-2026-09-27).

Map scene helpers (2026-09-27): MissionView.RemoveSettlementAuthoringCircles
snapshots/removes exact native helper-tag entities once after isolated Scene.Read.
It does not update settlement variants; root admission is now handled separately
as above. The variant gap is recorded
in the [native investigation](CAMPAIGN_MAP_RESUME_2026-09-26.md#settlement-scene-presentation-investigation-2026-09-27).

Map camera (2026-09-27): `CoopCampaignMapReplicaCameraPolicy` owns local follow/
detach/reset; `ReplicaCameraTests` covers policy and bindings. Mission view, info VM
and prefab implement the return button/input guards. Settlement tracked/inspected
bits cross publisher, full/dynamic contracts/codecs/messages and settlement overlay.
Both settlement culling gates share `ShouldShowSettlement`; tracked admitted
entities retain their projection for the native widget's screen-edge placement.
See [candidate and verification](CAMPAIGN_MAP_RESUME_2026-09-26.md#host-camera-following-and-settlement-presentation-2026-09-27).

Campaign mission lifecycle foundation (2026-09-26):
`Infrastructure/CoopCampaignMissionLifecycle.cs` is the pure one-shot map/battle
transition reconciler; `Tests/CoopCampaignMapPrototype.ContractTests/MissionLifecycleTests.cs`
exercises its transition, identity, stale-input and aftermath gates.
`Infrastructure/CoopCampaignMissionChannel.cs` provides bounded atomic transport
and process/freshness admission, tested by `MissionChannelTests.cs`.
`Campaign/CoopCampaignMissionHost.cs` connects the supported dedicated launcher,
battle roster admission and applied-result journal; `DedicatedServer/CoopCampaignMissionServer.cs`
connects the reconciler to native readiness/opening/result observations and commands.
`Infrastructure/CoopCampaignMissionOpening.cs` scopes the exact OpenNew return
capture to one native dispatch/thread, with `MissionOpeningTests.cs` rejection
coverage. The existing `MissionStateOpenNewPatches` postfix observes the return;
it does not initialize or mutate missions. See the 2026-09-27 native failure in
the lifecycle document for why immediate Mission.Current is not sufficient.
Both SubModule ticks call their adapters. `CoopPreMissionTopologyNetworkComponent.TryPublishPreparedMission`
supplies the pre-open scene contract. Separate default-off automatic-mission gate;
see [source implementation and native acceptance boundary](CAMPAIGN_MAP_AUTOMATIC_LIFECYCLE.md).

Local-isolation source map (2026-09-09): CoopAutomationRuntimeContract owns profile/path constants, ResolveCoopFolderPath, TryResolveContainedPath and LocalResultSentinelText; CoopAutomationRuntimeBridge supplies process configuration and the lazy production provider. Shared path factories route through it. CoopAutomationSpawnSmokeBridge admits/checks the local sentinel; the dedicated observer/control contract emit/validate its scope. CoopAutomationRunner.Core.ps1 contains the matching local-result helpers and v2 validators; Invoke-CoopTest.ps1 uses them only for DedicatedSpawnSmoke. Runtime, spawn-smoke and campaign-result guard suites cover this boundary; RuntimeCompileStubs.cs belongs only to the spawn-smoke test project. [Exact scope and evidence](BATTLE_TEST_AUTOMATION_M4_FIELD_SPAWN_SMOKE.md#11-local-only-file-isolation-2026-09-09).

Milestone 4A adds CoopAutomationSpawnSmokeContract, CoopAutomationSpawnSmokeBridge, CoopAutomationDedicatedSpawnSmokeObserver, the DedicatedSpawnSmoke command and CoopAutomationSpawnSmoke.ContractTests. The [M4 source map](BATTLE_TEST_AUTOMATION_M4_FIELD_SPAWN_SMOKE.md#2-ownership-and-source-map) lists exact integration methods. The canonical inventory has 24 projects.

Last source verification: **2026-08-28**
Last automation-control source verification: **2026-09-02**

## Repository map

| Path | Responsibility |
|---|---|
| `SubModule.cs` | Client/campaign startup, Harmony grouping, game-mode registration, campaign behavior/model registration |
| `CoopRuntime.cs` | Process-level access to `NetworkManager` |
| `CoopSpectator.csproj` | Client/campaign compilation, conditional game-mode inclusion, client deployment, default chained dedicated build |
| `DedicatedServer/CoopSpectatorDedicated.csproj` | Explicit dedicated source graph, dedicated references, runtime staging, server deployment |
| `Campaign/` | Encounter capture, snapshot creation, dedicated launch, campaign adapters, result validation/writeback, hero creation |
| `GameMode/` | Multiplayer mode registration and native/cooperative mission behavior composition |
| `Mission/` | Live mission state owners, spawn/phase lifecycle, network transport, battle power, scenario controllers |
| `MissionModels/` | Campaign-derived combat, damage, difficulty, and morale models used in multiplayer mission runtime |
| `Infrastructure/` | Snapshot/runtime state, contracts, bridges, scenario adapters, feature flags, exact-transfer utilities, and run-scoped automation contracts |
| `Network/` | TCP prototype layer, pre-mission topology component, and network message schemas |
| `Patches/` | Harmony boundaries around native lobby, mission load, spawn, control, UI, siege, and compatibility code |
| `UI/` | Gauntlet views/view-models for selection, deployment, hero creator, hideout, battle power, and map prototype |
| `Commands/` | `coop.*` console commands and synthetic test controls |
| `DedicatedHelper/` | Dedicated launch settings/commands, hosting mode, helper launcher, web-panel authentication |
| `Module/` | Client and dedicated module descriptors, XML game data, GUI prefabs, shader-cache helper |
| `Tests/` | Standalone `net8.0` contract-test executables linked to narrow production contracts |
| `scripts/` | Development loop, release packaging, installed-DLL inventory audit, repository hygiene, and battle-test client launcher core |
| `docs/` | Dated investigations, status reports, specifications, and the living `docs/ai/` index |

Generated and investigative areas such as `bin/`, `obj/`, `.buildcheck/`, `.codex_tmp*/`, `dist/`, `work/`, copied root DLLs, ZIP packages, and decompilation scratch files are not primary source.

## Primary entry points

### `SubModule.cs`

Start here for client/campaign startup questions.

Key responsibilities and methods:

- `OnSubModuleLoad`: reset state, initialize runtime/networking, apply patches, register modes.
- `TryApplyHarmonyPatches`: client patch groups and isolation flags.
- assembly-load callback: reapplies patches whose target assembly arrived late.
- `OnGameStart`: campaign behaviors or non-campaign mission-model wrappers.
- application tick: network dispatcher, campaign map prototype, hero creation, default-off lobby automation controller, and `BattleDetector.Tick`.
- `OnSubModuleUnloaded`: shutdown and event cleanup.

Important local constants at the top of the file define which manual patch groups are active. Do not infer them from patch class existence.

### `DedicatedServer/SubModule.cs`

Start here for dedicated startup, registration, observer, or web-panel questions.

Key responsibilities:

- module-load proof logging and exact scene-script registration;
- dedicated Harmony compatibility and patch registration;
- cooperative game-mode registration and official mode overrides;
- application-tick mission classification and stable-start observation;
- delayed attachment of `CoopMissionNetworkBridge` and `CoopMissionSpawnLogic`;
- dedicated knockout outcome and shared mission-model registration;
- web-panel hooks and server lifecycle cleanup.

### `Campaign/BattleDetector.cs`

The campaign orchestration hotspot. It owns far more than detection:

- decides when a campaign encounter is eligible;
- validates supported scenario contracts;
- resolves scenario kind, siege subtype, scene, map patch, mission shell, and atmosphere;
- builds `BattleStartMessage` and the complete `BattleSnapshotMessage`;
- writes `battle_roster.json`;
- starts the helper/dedicated mission or sends a start notification;
- monitors campaign mission exit and authoritative result availability;
- validates campaign/result identity and applies casualties, hero outcomes, loot, prisoners, XP, and scenario-specific aftermath;
- prevents duplicate writeback through stable IDs and journaling.

Search by method before scanning this very large file. Useful anchors include:

- `Tick`
- `TryGetUnsupportedCoopMissionReason`
- `TrySendBattleStart`
- `TryStartDedicatedMissionForCampaignHost`
- `BuildBattleStartPayload`
- `ResolveScenarioKind`
- `ResolveSiegeSubtype`
- `TryWriteBattleRosterFile`
- result/writeback methods containing `BattleResult`, `Aftermath`, `Casualty`, `Prisoner`, or `Journal`.

### `Mission/CoopMissionBehaviors.cs`

This file contains two large top-level behavior owners:

- `CoopMissionClientLogic`: client mission lifecycle, local presentation/observer behavior, result reception, cleanup.
- `CoopMissionSpawnLogic`: authoritative battle runtime, roster entry tracking, teams, phases, selection/spawn, agent ledger, native reinforcements, battle completion, and result production.

Dedicated runtime also calls static bootstrap/observer entry points on `CoopMissionSpawnLogic`. Because this file owns many static dictionaries and scenario gates, any modification requires a full scenario and sequential-mission reset review.

Useful search anchors:

- `TryRunDedicatedMissionNetworkBootstrap`
- `TryRunDedicatedMissionObserver`
- `RunCoopBattleSpawnOwnerTick`
- `RunCoopBattlePhaseOwnerTick`
- `TryCompleteBattleIfResolved`
- `TryResolveAuthoritativeTrackedEntryId`
- `MarkInitialClientMaterializationComplete`
- methods containing `InitialMaterialization`, `Reinforcement`, `Result`, `Reset`, or `BattleEnded`.

### `Mission/CoopMissionNetworkBridge.cs`

The in-mission network and deployment hotspot. It owns:

- full battle snapshot transport V2;
- client/server message registration;
- entry-status and materialized-agent mapping transfers;
- selection, spawn, spectator, control, reconnect, and phase messages;
- initial materialization readiness for multiple scenarios;
- field/village/sally-out deployment boundary state;
- commander formation state and siege-machine state;
- siege mission-object ID maps and ladder interaction state;
- server/client readiness barriers and retry timers.

Use symbol search aggressively. Relevant method groups are clustered by message handler or scenario name, but this is not a small single-purpose class.

## Campaign adapters and aftermath

| Scenario | Campaign capture/validation | Result/aftermath |
|---|---|---|
| Field/general land | `Campaign/LandBattle/ExactLandBattleCampaignBattleAdapter.cs` | `ExactLandBattleNativeAftermathBridge.cs`, `ExactLandBattleEncounterContinuationPatch.cs` |
| Village | `Campaign/VillageBattle/ExactVillageBattleCampaignBattleAdapter.cs` | general land/native aftermath plus scenario data |
| Siege assault | `Campaign/SiegeAssault/ExactSiegeAssaultCampaignBattleAdapter.cs` | `ExactSiegeAssaultNativeAftermathRuntime.cs`, `ExactSiegeAssaultNativeAftermathCommitPatch.cs` |
| Sally out | `Campaign/SallyOut/SallyOutCampaignBattleAdapter.cs` | general result pipeline with sally-out contract data |
| Siege ambush | `Campaign/SiegeAmbush/SiegeAmbushCampaignBattleAdapter.cs` | `ExactSiegeAmbushNativeAftermathRuntime.cs` |
| Relief | `Campaign/Relief/ExactReliefCampaignBattleAdapter.cs` | scenario controller plus general writeback |
| Lords hall | `Campaign/LordsHall/LordsHallCampaignBattleAdapter.cs` | `LordsHallResultBridge.cs` |
| Day hideout | `Campaign/Hideout/HideoutCampaignBattleAdapter.cs` | `ExactHideoutNativeAftermathRuntime.cs` |
| Night hideout | `Campaign/Hideout/HideoutAmbushCampaignBattleAdapter.cs` | hideout aftermath plus ambush contract |
| Hero capture | n/a | `Campaign/Capture/ExactCampaignHeroCaptureRuntime.cs`, patch |

`BattleResultWritebackJournalBehavior` and `Infrastructure/CoopBattleResultCampaignGuardContract.cs` are cross-scenario idempotency/identity boundaries.

## Game modes and mission composition

| File | Role |
|---|---|
| `GameMode/MissionMultiplayerCoopBattleMode.cs` | General cooperative campaign battle mission opener and behavior stack |
| `GameMode/MissionMultiplayerCoopSiegeAssaultWithDeploymentMode.cs` | Native siege-with-deployment wrapper and cooperative siege stack |
| `GameMode/MissionMultiplayerCoopHideoutDayMode.cs` | Day hideout mission stack |
| `GameMode/MissionMultiplayerCoopHideoutNightMode.cs` | Night ambush mission stack |
| `GameMode/MissionMultiplayerCoopHeroCreatorMode.cs` | Hero-creation mission stack |
| `GameMode/MissionMultiplayerCoopCampaignMapPrototypeMode.cs` | Opt-in campaign map replica/prototype |
| `GameMode/MissionMultiplayerCoopTdmMode.cs` | Cooperative TDM-derived mode |
| `GameMode/MissionMultiplayerTdmCloneMode.cs` | Disabled custom TDM experiment |
| `GameMode/MissionBehaviorHelpers.cs` | Reflection/compatibility helpers for optional native behaviors |
| `GameMode/MissionBehaviorDiagnostic.cs` | Mission stack diagnostics |

Each mode usually has server/client marker classes beside its `*Mode.cs` file. The mode class builds the behaviors; the marker classes help Bannerlord registration and side-specific identification.

### Campaign map prototype

Settlement occupant badge source (26/22/7): `CoopCampaignMapSettlementPartyBadgePolicy`
in the map contract owns admission/codec bounds; publisher
`CaptureSettlementPartyBadges` reads admitted host parties; catalog/full bridge,
binary codec and shared projection carry the field. `SendCatalogDelta` routes
nonempty lists to chunks. Settlement overlay `SettlementParties.Synchronize`
owns the passive stock-grid items. `SettlementPartyBadgeTests.cs` covers detached
contracts; native acceptance pending in the canonical resume report.

| Concern | Files |
|---|---|
| Host-visible map and dialogue publication | `Campaign/CoopCampaignMapPrototypePublisherBehavior.cs`, `Campaign/CoopCampaignMapConversationCapture.cs` |
| Bounded host/dedicated contract and bridge codec | `Infrastructure/CoopCampaignMapPrototypeContract.cs`, `Infrastructure/CoopCampaignMapPrototypeBridgeCodec.cs` |
| Static catalog payload | `Infrastructure/CoopCampaignMapCatalogBinarySerializer.cs` |
| Dedicated authority and client assembly | `Mission/CoopCampaignMapPrototypeNetworkController.cs` |
| Map and dialogue wire messages | `Network/Messages/CoopCampaignMapPrototypeNetworkMessages.cs`, `Network/Messages/CoopCampaignMapReplicaNetworkMessages.cs`, `Network/Messages/CoopCampaignMapConversationNetworkMessages.cs` |
| Client map rendering and interaction | `UI/CoopCampaignMapPrototypeMissionView.cs`, map overlay and replica-info view-models |
| Client synchronous dialogue | `UI/CoopCampaignMapConversationView.cs`, `UI/CoopCampaignMapConversationVM.cs` |
| Host-owned dialogue upper HUD/banner | `Infrastructure/CoopCampaignMapConversationHud.cs`, `Campaign/CoopCampaignMapConversationCapture.cs`, `UI/CoopCampaignMapConversationHudVM.cs`, `ConversationHudTests.cs`; [scope and verification](CAMPAIGN_MAP_CONVERSATION_HUD.md) |
| Client dialogue scene ownership | `UI/CoopCampaignMapConversationTableauView.cs`, `Infrastructure/CoopCampaignMapConversationVisualLifetime.cs`, `ConversationVisualLifetimeTests.cs` in the map contract project |
| Live dialogue body motion | `Infrastructure/CoopCampaignMapConversationPose.cs` (codec, exact receiver, pure `CoopConversationPosePlayback` rate steering), map contract `ConversationPoseTests.cs`, host capture, message/controller/view; protocol 22/bridge 18. cmt-b27 first-dialogue smoothness user-confirmed; first/repeat retention, read-only/text update and both closes passed. Installed b27 retained after exact cleanup; no full visual/battle-regression claim. |
| 3D dialogue descriptor and atomic transport | `Campaign/CoopCampaignMapConversationTableauCapture.cs`, `Infrastructure/CoopCampaignMapConversationTableauState.cs`, `Infrastructure/CoopCampaignMapConversationPayloadCodec.cs`, map contract `ConversationTableauTests.cs` and `ConversationPayloadTests.cs` |
| Contract coverage | `Tests/CoopCampaignMapPrototype.ContractTests/Program.cs` |

The dialogue projection is server-to-client only. The client uses the native `SPConversation` movie but its answer/continue commands are no-op; no campaign object or choice authority is created in the multiplayer process.

The 2026-09-26 tableau descriptor defines detached camera/atmosphere/weather and up to three native visible actors with a strict 64-KiB binary codec. Host dialogue capture now reads an already initialized native tableau and transports it via bridge schema 17. The whole dialogue uses a bounded 1-MiB atomic payload with 256-byte reliable chunks, two per peer per tick; protocol 21 replaces the old text header/option messages. The complete-payload assembler rejects mixed/stale/incomplete revisions. It preserves initial action identity, not continuous facial/voice/animation synchronization. The independent client scene owner now consumes the descriptor with bounded readiness and explicit cleanup; this candidate is not deployed or runtime-verified. Do not call this implemented live 3D dialogue. See the [implementation boundary and evidence](CAMPAIGN_MAP_RESUME_2026-09-26.md).

`CoopCampaignMapReplicaStateProjection.Merge` in the map contract is the single full/incremental visible-entity projection used by both the network controller and mission view. It preserves all marker, production-icon, label-color and hidden-information fields, with detached visual descriptors. The 2026-09-26 follow-up audit found and removed divergent hand-written merges; the earlier `cmr-b4`/`cmr-b5` candidates had incomplete consumer coverage despite successful tests. `cmr-b6` builds both roles without installed mutation; see the resume report for verification boundaries.

`CoopCampaignMapPartyLabelPolicy` in the map contract holds the inspected native relationship palette, caption precedence, status/label visibility, hidden-count redaction and text composition. The publisher resolves real host diplomacy and uses installed `SandBoxUIHelper` for healthy/wounded/prisoner attached-army aggregation and localization. `NameplateColor` is independent of banner/cloth colors. `IsPartyInformationHidden` zeroes transmitted detail numbers and restricts count/extra strings to native question-mark disclosure. `PartyNameplateNameText` is catalog-owned and separate from detail `DisplayName`; count/extra/speed strings and six presentation flags are dynamic. The shared merge feeds the overlay's native widget properties. Label suppression is separate from the return value used to gate 3D models. The party overlay uses spatial camera distance; the mission view projects a fixed 0.8-unit head anchor. Detail `PartySize` remains total members. Current source requires wire 22/bridge 18/catalog 4 and two-entity dynamic batches (the historical live `cmr-b10` used 20/16/4). Coherent `cmr-b10` passed bounded live mission startup, city selection, zoom, movement and read-only dialogue open/update/close; exact-owned cleanup and 64-file preservation passed. The candidate remains installed. Main-party-specific template, portrait, navigator and dialogue 3D tableau remain pending, as do broader visual and transfer-cost checks. See the resume report for exact build/test evidence and remaining parity gaps.

The replica information panel binds scalar scaled-position offsets; `CoopCampaignMapPrototypeInteractionPolicy.ResolveInformationPanelPosition` clamps shared panel dimensions using the actual layer UI scale. Ordinary dialogue answers supply an inert non-null persuasion VM to suppress default-visible native child widgets; this does not transport actual persuasion state. Movie replacement/close clears information hints. Source/contracts and both builds passed on 2026-09-26; live validation of these corrections remains pending in [the resume report](CAMPAIGN_MAP_RESUME_2026-09-26.md).

## Snapshot, identity, and exact transfer

### Bottom-bar remaining hint boundary (2026-10-22 research)

2026-10-23 follow-up: user manually reports current b173 bottom-bar data visually
matches campaign and requests siege/world-party animation work next. This is
bounded user evidence, not every provider/modal gate. r31 cleanup is still pending.

### Map-party event animation and siege audit (2026-10-23)

Projectile particle probe candidate: CoopSiegeProjectileParticleProbe is disabled
unless both verbose diagnostics and COOPSPECTATOR_SIEGE_PROJECTILE_DIAGNOSTICS=1
are explicitly present at construction. Owner-thread only, at most16 keys per
instance, once per role/prefab/flight-state. Reads root and child0 particle counts
and child counts, not native pointers/effect mutation. Campaign capture and replica
apply call it only for admitted projectiles. It distinguishes missing components
from later emission issues but does not inspect nested grandchildren or prove
activity. Native-sensitive read risk remains; b211 passed both isolated builds
with installed inventories unchanged. Not installed or native-exercised.

Projectile trail research (2026-10-23): installed Native/ModuleData/
particle_systems_general.xml defines map_psys_game_ballista_trail (near8707) and
map_psys_game_mangonel_trail (near9039). Ballista trail explicitly enables
emit_while_moving and disables dont_emit_while_moving. Native SettlementVisual
enables missile child0 only in flight. Replica preserves that flag but creates
prefabs without callbacks/physics and moves roots only on admitted samples.
These are hypotheses for missing trails, not a diagnosed cause. Both native and
replica SetGlobalFrame use isTeleportation=true (the default), so changing that
flag alone is not supported by the comparison. MBMapScene.TickVisuals is already
called; SceneLayer source contains no managed tick/particle override. Next inspect
actual prefab components and particle initialization before adding/forcing effects.
Do not blindly enable all prefab scripts or duplicate authored particle systems.

Fresh installed TaleWorlds.Engine.ParticleSystem decompilation confirms
SetEnable, Restart, SetRuntimeEmissionRateMultiplier, GetLocalFrame,
SetPreviousGlobalFrame and HasAliveParticles; there is no exposed effect-name or
enabled-state getter in this class. GameEntity provides typed component counts
and GetComponentAtIndex; a bounded explicit diagnostic can distinguish missing
authored particle components from present-but-inactive ones. Such inspection is
native-sensitive and must remain off by default, owner-thread scoped and bounded,
not an unconditional per-frame probe. A raw name scan of Native AssetPackages
found no plaintext mangonel_mapicon_projectile entry; it does not prove absence
because package serialization/compression is opaque. SandBox.MapScene.Tick(0.1)
is initial setup, not evidence that the replica needs an extra per-frame Scene.Tick.

Projectile boundary follow-up: expanded tests reject hostile count/name lengths,
non-ASCII resources, wrong magic, invalid sides/indexes/null lists and oversized
sets; maximum128 entries roundtrip and opposite-side same-index identity pass.
Full map-contract executable passed. These are test-only additions after b210
staging; no installed binaries changed and no native flight claim follows.

Projectile rendering candidate (2026-10-23): MissionView.SiegeProjectiles owns
closed-admission prefabs, applies captured world frames and child-zero flight
visibility, and removes missing/replaced roots. Null/invalid/hidden menu and menu
release clear ownership before scene teardown. Creation disables gameplay callbacks
and physics; ownership is recorded before later native operations. This is sampled
presentation, not smooth interpolation or impact/sound event replay. All native
appearance, particle initialization and lifecycle acceptance remains pending.
Isolated b210 builds passed both roles with installed inventories unchanged;
c166 passed all 26 contract projects. Installed b207 unchanged. User closed games;
r40 cleanup restored the inspected generated map change and verified seven
configuration preimages. Candidate staging/native projectile acceptance is next.

Projectile integration (2026-10-23): publisher now attaches prepared projectile
samples to fresh siege overlays; failed/absent capture clears that optional set.
Overlay validates/deep-copies it and CSO5 appends bounded CSP1. Protocol55 and
host bridge50 reject prior incompatible envelopes (catalog10 unchanged).
Dedicated explicitly includes projectile model/codec. Full map-contract executable
passed including nested nonempty projectile roundtrip and truncation checks.
Isolated b209 builds passed both roles with installed module inventories unchanged;
deployment and rendering remain pending.

Projectile foundation (2026-10-23): CoopCampaignMapSiegeProjectiles contains only
visible prepared roots with side/list-index, exact admitted resource, world frame
and independent first-child flight visibility. Index is NOT persistent shot
identity: impacts and interpolation across reused entries require further event
identity. CSP1 validates bounded lengths/counts, strict flags, frames/resources,
duplicate entries and complete consumption, and deep-copies replacement sets.
SiegeProjectileTests registers four resource roundtrips, detached frames, flight
visibility, truncation, invalid flags, trailing data, duplicate entries and invalid
frame/resource rejection. Capture, enclosing versioned transport, renderer and
event effects are NOT integrated yet. Installed b207 is untouched.

The complete map-contract executable passed after setting its required
COOPSPECTATOR_REPOSITORY_ROOT and rerunning with temporary-file access. Initial
direct attempts failed on missing root and then sandbox access, not projectile
assertions. A source-only CoopCampaignMapSiegeProjectileCapture now reads the
typed native missile list on its owner thread, omits hidden roots and preserves
child-zero visibility/world frame. It rechecks scene owners and list identity;
native calls remain potentially lifetime-sensitive. Not compiled, invoked or
runtime-accepted yet. No publication, deployment or native safety claim.

Follow-up b208 CompileOnly completed successfully for both roles with installed
module inventories unchanged. The client capture now checks ChildCount before
GetChild(0). This proves API/build compatibility only; the capture still has no
publisher caller, and dedicated transport has not yet included the new payload.
Native runtime acceptance and projectile rendering remain pending.

Machine renderer candidate (2026-10-23): MissionView.SiegeMachines creates owned
admitted prefabs with gameplay callbacks/physics disabled, applies captured world
frames without double scaling, finds the unique tagged animated child and applies
host clip/phase. Local speed is zero: this is sampled phase fidelity, not yet smooth
host-clock interpolation. The snapshot identity skips unchanged admitted instances;
replacement prefab/removed slots/null/hidden menu clear owned roots before scene
teardown. Native failures enter existing menu cleanup. b207 isolated builds passed
both roles, installed inventories unchanged. All-project c165 was started; outcome
must be checked from its live session/terminal evidence. No deployment or native
machine acceptance. Projectile visuals/effects and smooth animation remain open.

Machine publication/transport integration (2026-10-23): publisher attaches prepared
machine capture to fresh siege overlays; capture failure/null clears that optional
component rather than retaining an older set. Overlay validation and snapshots own
machines. CSO4 appends length-framed CSM1 after decals; absent component is length0.
Protocol54/host bridge49/catalog10 requires coherent rebuilt roles. Dedicated
explicit sources include model, animation, resource admission and codec. The full
map-contract executable passed, including nonempty nested machine roundtrip;
siege-machines-b206-20261023 compiled both roles with installed inventories unchanged.
This supersedes earlier capture/transport-unwired notes below. Renderer and native
acceptance remain open; installed b204 is still53/48/10 after closed r39.

Machine transport foundation (2026-10-23): CoopCampaignMapSiegeMachineCodec uses
CSM1 with bounded count, length-prefixed ASCII resource names, exact float frame,
strict 0/1 animation presence and optional clip/phase/speed. Decode rejects oversized
or truncated data, unknown version, trailing bytes, invalid names and malformed
complete sets without partial output. Registered mixed-machine tests cover exact
roundtrip, every truncated prefix and hostile count/name length. This inner codec
is not yet integrated into the admitted menu/siege transport or native renderer;
it does not provide session/freshness authorization on its own.

Siege machine capture candidate (2026-10-23):
Campaign/CoopCampaignMapSiegeMachineCapture.cs reads the typed native ranged/melee
SettlementVisual lists on its construction thread under current player-siege/map/
manager/visible-root admission. It bounds inspected entries, omits hidden roots,
reads actual GetPrefabName/global frame and ranged skeleton clip/phase/speed,
validates the detached set, then rechecks owners/list identities/counts. It never
calls campaign update, creation or mutation methods and logs nothing. Unknown
layout/resource or invalid native sample fails closed. b205 isolated compilation
passed both roles with installed inventories unchanged. Capture is NOT called by
the publisher yet; native timing, visibility and safe lifetime remain unverified.
Transport, rendering and projectile presentation remain open. Installed b204 is
retained after closed r39. Do not confuse compile success with in-game acceptance.

Detached siege machine model (2026-10-23): CoopCampaignMapSiegeMachines is a bounded
complete-replacement set keyed by side/ranged-or-melee/slot. It preserves the actual
admitted prefab, finite nonsingular world frame and matching optional clip sample.
Defender melee entries, duplicate logical slots (including different prefabs),
invalid resources/transforms and oversized/null sets fail atomically. Snapshot owns
list/entries/frames/animations. Registered SiegeMachineTests exercise these boundaries.
This is not wired to capture, transport or rendering. Enclosing integration still
requires settlement/session/revision/host-clock binding and visibility/lifetime
admission for town/castle, construction/reserve/destruction and battle return.

Siege resource admission foundation (2026-10-23): fresh installed
TaleWorlds.CampaignSystem.GameComponents.DefaultSiegeEventModel decompilation
confirms ten ranged prefab names, four melee names (ram and three wall-level
towers), and four projectile names. CoopCampaignMapSiegeResources admits those
exact names and pairs ranged prefabs only with their own _reload/_fire clips;
melee samples have no ranged animation. Tests cover all entries, unrelated clips,
unknown/case/path names and machine/projectile separation; the complete map-contract
executable passed. This is not runtime integration or resource-existence proof.
The capture must use the actual host-selected resource, never substitute a guessed
side/wall variant. Unknown modded resources require an explicit compatible catalog
extension, not arbitrary native instantiation. Default model returns no prefab for
ImprovedRam despite SettlementVisual considering that type; do not fabricate one.
Installed b204 remains unchanged while the user connects for r39.

Siege animation sample foundation (2026-10-23):
Infrastructure/CoopCampaignMapSiegeAnimation.cs separates prefab clip identity
from agent action-set identity, validates bounded resource-name syntax, phase0..1
and speed0..16, and returns detached snapshots. Registered SiegeAnimationTests
cover paused/completed samples, copy isolation, name bounds/path/control characters
and nonfinite/out-of-range phase/speed. The complete map-contract executable passed
after rerunning with filesystem access; its first sandboxed run passed these new
checks but later failed with access denied. This is validation-only, with no native
capture, transport or rendering callers and no deployment. Resource syntax alone
must never authorize arbitrary native loading; future integration needs disclosed
engine identity, compatible resource admission and host clock/expiry semantics.
Installed/running b204 is unchanged by this source-only addition.

Skeleton sampling boundary (2026-10-23): fresh installed TaleWorlds.Engine.Skeleton
decompilation exposes GetAnimationAtChannel(int), GetAnimationParameterAtChannel(int)
and GetAnimationSpeedAtChannel(int). Existing party capture uses the MB extension
GetActionAtChannel and an agent action set; that identity scheme must NOT be reused
for siege prefabs, whose native writer calls SetAnimationAtChannel by animation
resource name. Read the actual clip name and phase from the prepared machine skeleton.
Do not infer clip identity from a local campaign timer or transmit native pointers/
animation indices across installations. These are native-sensitive reads requiring
owner-thread lifetime checks; no new live native sampling has been performed.
Frame-based playback also needs explicit host pause/clock handling because native
bombardment repeatedly supplies campaign-derived phase rather than relying solely
on the skeleton's speed value. Existing party rate steering is not proof of siege
pause or fast-forward correctness.

Bombardment presentation audit (2026-10-23, retained native decompilation lines
725-901): missile root visibility is not equivalent to its first child's flight
visibility. During aiming/firing a visible projectile may be attached to the
machine's projectile bone; only the flight interval enables the child and updates
the ballistic global frame. Flight boundaries also emit sound and impact particles.
A future snapshot must preserve root/child visibility separately and must not replay
impact effects on every repeated sample or late join. Machine roots rotate toward
previous/current targets; channel0 is explicitly driven through reload, aim and
fire phases using campaign time and ends at phase1. Therefore free-running local
animation or frame-only static prefabs cannot satisfy fidelity. Capture prepared
graphics on the owner thread after native updates; do not run campaign bombardment
simulation in multiplayer. Confirm ordering and safe skeleton sampling before
implementation. This research does not claim any new native test or implementation.

Latest decal verification (2026-10-23): terminal runner records for
siege-decals-b204-20261023 confirm both isolated builds passed and installed module
inventories were unchanged. siege-decals-c164-20261023 completed all 26 contract
projects successfully. Capture, transport and renderer are now connected; earlier
foundation-only statements below are historical. b204 avoids reapplying the same
immutable admitted decal-set instance; clearing resets that identity. This does
not skip later replacement samples or implement interpolation of native pulsing.
Installed b199 remains unchanged; native decal acceptance has not been performed.
Required next checks include town/castle siege slots, host color changes, pause,
menu/modal hiding, departure, reconnect and battle/map return. Siege engine models,
projectiles and flag-fold/lighting fidelity remain separate unfinished work.

Siege decal rendering candidate (2026-10-23): MissionView.SiegeDecals owns one
GameEntity/Decal pair per group/index. It uses the closed native material mapping,
exact captured frame and raw SetFactor1 color; no campaign command is available.
Menu tick applies the complete replacement set after freshness/conversation/modal
admission. Missing/invalid sets and hidden menus remove owned entities; menu release
clears them before map-scene teardown. Native resource creation is ownership-recorded
before subsequent native calls so the existing menu exception cleanup can find it.
b202 failed on a ref/in language compatibility error; corrected call is in b203.
Native acceptance, allocation/rate optimization and broad lifecycle regressions
remain open. This does not add siege engine/projectile models or production choices.

Siege decal transport candidate (2026-10-23): publisher now attaches prepared decal
capture to the fresh siege overlay, with null on capture failure. CSO3 carries a
bounded length-prefixed CSD1 set after points; zero length clears/omits decals.
Overlay snapshots deep-copy decals, and validation rejects malformed nested sets.
Existing menu/interface transport owns this atomically with overlay state. Protocol53,
host bridge48, catalog10 requires matching unreleased roles; installed b199 remains
52/47/10. Dedicated explicit compilation includes both decal shared files. Full
map-contract executable passed with a nonempty nested decal roundtrip; isolated
b201 compiled both roles with installed inventories unchanged. Renderer integration
and native visibility/lifetime acceptance remain open. Patch application reported a
partial/retry failure; resulting duplicate model members were removed and source
was compiled successfully before recording this candidate.

Siege decal capture candidate (2026-10-23): Campaign/CoopCampaignMapSiegeDecalCapture.cs
reads the four prepared SettlementVisualManager arrays on its construction thread.
Admission requires campaign/map state, current player siege, no mission/conversation,
the native added flag and visible settlement graphics. It bounds all inspected slots,
omits hidden entities, reads world frame and raw Decal.GetFactor1, and requires the
native material to match the closed group mapping. It rechecks owner and array identity
before returning a detached validated snapshot. Unknown layouts/errors fail closed;
no native update, tooltip, mutation or diagnostic logging is invoked. This is a
native-sensitive read-only candidate, NOT yet connected to publication or accepted
in game. b200 CompileOnly passed client and dedicated with installed inventories
unchanged. b199 remains installed. Next: atomic menu/overlay transport and owned decal
rendering, then host timing/visibility/lifecycle acceptance across siege scenarios.

Siege decal binary foundation (2026-10-23): CoopCampaignMapSiegeDecalCodec uses
CSD1, an8-byte header and60-byte fixed records (group/index/raw color/12 floats),
bounded to128 entries. Decode checks exact length/count before allocation and
validates the complete set atomically; resource names are not transmitted.
Encode owns a deep validated snapshot. Registered tests cover exact byte roundtrip,
every truncated prefix, trailing byte, wrong magic, hostile count and NaN frame.
The full map-contract executable passed after correcting a test-local name clash.
No runtime wiring, native capture/rendering or deployment yet; this inner format
must be enclosed in the admitted siege/session presentation before use.

Siege decal foundation (2026-10-23): Infrastructure/CoopCampaignMapSiegeDecals.cs
defines a bounded complete-replacement set with four native groups, slot index,
12-float world frame and raw decal color. Materials are a closed group mapping,
not network-provided resource names. Validation rejects unknown groups, duplicate
group/index identities, nulls, oversized lists and invalid/singular transforms;
snapshots own their lists, entries and frame arrays. SiegeDecalTests is registered
in the map-contract executable. The complete executable passed with
COOPSPECTATOR_REPOSITORY_ROOT set; the first invocation failed later in unrelated
source-wiring tests because that required variable was missing. No game build,
deployment, native capture, transport or renderer integration is claimed. b199
remains installed. Future integration must bind the set to the enclosing siege
and session and distinguish hidden/expired data from a retained prior set.

Post-r38 native investigation (2026-10-23, read-only): the missing blue slot
outlines are NOT MapSiegeOverlay widgets. Fresh installed SandBox.View.dll
decompilation of SettlementVisualManager confirms four owned GameEntity arrays:
_defenderMachinesCircleEntities, _attackerRamMachinesCircleEntities,
_attackerTowerMachinesCircleEntities and _attackerRangedMachinesCircleEntities,
guarded by _playerSiegeMachineSlotMeshesAdded. Their Decal components use
decal_defender_ranged_siege, decal_siege_ram, decal_siege_tower and
decal_siege_ranged. TickSiegeMachineCircles updates prepared material/color;
GetDesiredDecalColor varies by enemy, occupancy, hover and player leadership,
with a time-dependent PingPong interpolation for empty leader-owned positions.
Therefore a fixed blue material/color is not 1:1. Capture the prepared visible
entity frame/material/factor under current manager/siege identity, rather than
re-evaluating campaign authority on clients. RemoveSiegeCircleVisuals removes the
four arrays independently of the Gauntlet movie. Own replica decals separately,
clear on departure/expiry/mission transition, and never call host update methods.

Retained SettlementVisual decompilation separately shows that AddSiegeIconComponents
creates only active deployed engines (attacker ranged/melee, defender ranged).
AddSiegeMachine resolves GetSiegeEngineMapPrefabName with wall level and side,
composes the prefab's authored local transform with the slot global frame, and
retains a siege_machine_mapicon_skeleton child. Bombardment updates rotate the
root and drive fire/reload animation phases; missiles have a separate lifetime.
RemoveSiege removes all three machine/missile lists. Engine rendering must not be
inferred from HasItem in the UI: construction entries can exist before an active
model. Plan a detached visible-object descriptor/pose stream with deletion,
settlement/session identity and bounds; static prefabs alone are incomplete.
Native-sensitive getters/rendering require explicit validation before integration.
Adjacent gates: town/castle wall levels, attacker/defender, reserve/redeploy,
destruction, siege abandonment, assault/sally transition and return, late join,
conversation suspension and hidden-settlement disclosure. No implementation or
new runtime pass is claimed by this investigation; b199 remains installed.

Siege overlay rendering connected in source (2026-10-23): b199 loads stock
MapSiegeOverlay with the detached VM on the existing campaign-menu layer.
Apply/close/teardown own its movie/VM; existing menu freshness, conversation and
modal suspension gates apply. Host capture now includes MapCameraView.CameraDistance
in CSO2 (minimum21 bytes); following uses that native distance against threshold20.
Detached inspection retains its initial distance and adjusts it by local zoom
steps; panning/rotation do not reset it. Native parity near terrain-clamp/camera
transitions needs testing. CSO1 is rejected. Outer52/47/10 remains the coherent
unreleased candidate. Complete map-contract executable passed and b199 both
isolated builds passed with installed inventories unchanged. Installed b194 remains.
This is not native visual acceptance. Tooltips, production choices/host selection,
ground outlines and siege engine/projectile presentation remain fidelity gaps.

Siege UI ordering correction (2026-10-23): current native MapSiegeVM.Update sorts
PointsOfInterest by descending LatestW every frame. Replica Apply now matches
existing point VMs by (Type,MachineIndex), independently of incoming list order,
updates retained objects and removes only departed identities. Replica projection
sorts by its own descending depth with deterministic identity ties. This avoids
recreating all hammer widgets when host depth order changes. b198 isolated builds
passed both roles; installed inventories unchanged. Native animation retention
and UI rendering are still unverified; movie loading/range admission remains open.

Siege UI binding candidate (2026-10-23): UI/CoopCampaignMapSiegeOverlayVM.cs
provides detached stock MapSiegeOverlay bindings, inert commands and a disabled
production-controller binding. Point objects are retained for stable ordered
identities so value updates do not recreate hammer animations. World anchors
project through the replica camera with finite/behind-camera rejection.
b197 isolated client/dedicated builds passed; installed files unchanged. This VM
is NOT loaded by the mission view yet. It currently intentionally omits production
choices/tooltips and host selection; those remain fidelity gaps, not completed
read-only parity. Depth sorting and exact visible-range admission remain open.
Native widget inspection confirms IsPOISelected calls ConstructionControllerWidget
without a null guard, so even a passive template needs that referenced widget.
Native MapCameraView.CameraDistance is not camera height: ComputeMapCamera offsets
the target by cameraDistance+2 along rotation.u; an adjustment branch uses distance
to IdealCameraTarget minus2. Do not substitute camera.Position.z for the native20
range threshold. Resolve host-follow and detached-camera range before integration.

Siege overlay transport integration (2026-10-23): protocol52/bridge47/catalog10,
menu CMN7. Publisher now captures the overlay only for a present siege-mode menu
outside conversation. Failed capture attaches null rather than stale values.
Menu validation requires siege mode for nonnull overlay; deep cloning preserves
isolation. CMN7 embeds a bounded CSO1 payload and rejects invalid/trailing data.
The existing host MenuPresentation bridge and atomic menu/bar interface packet
carry this field without a separate independently completing overlay stream.
Menu close/dialogue/mission publication clears it with the enclosing menu.
Dedicated shared-file inventory includes both overlay files. Map-contract tests
passed menu roundtrip, interface roundtrip, deep copy, close and non-siege rejection;
siege-overlay-b196-20261023 passed both isolated builds with installed inventories
unchanged. Installed b194 remains51/46/10; do not mix roles. No rendering consumer,
native post-update timing acceptance or siege visual pass is claimed yet.

Siege overlay capture candidate (2026-10-23):
Campaign/CoopCampaignMapSiegeOverlayCapture.cs reads the existing
GauntletMapSiegeOverlayView._dataSource as MapSiegeVM, on its construction thread.
It requires current campaign/MapScreen/MapState, no mission/conversation and a
PlayerSiege event. No view yields no overlay; unavailable layout/source fails
closed. It copies prepared title/progress and bounded POI bindings, including
world origins, colors, native -1 queue sentinel and unchanged HP/progress pairs.
It rechecks campaign, screen, siege, view and VM ownership before publishing a
deep snapshot. It never calls native Update/Refresh, Machine, selection or tooltip
producers, and adds no hot-path logging. It is NOT wired to the publisher yet;
post-native-update timing and native visibility acceptance remain required.
siege-overlay-b195-20261023 CompileOnly passed both roles and verified installed
inventories unchanged. Installed b194 remains; no new game launch occurred.
Next integrate into the atomic interface envelope with explicit clearing on
departure/dialogue, correlated schema revision and owned read-only UI projection.

Siege overlay foundation (2026-10-23): Infrastructure/CoopCampaignMapSiegeOverlay.cs
and CoopCampaignMapSiegeOverlayCodec.cs contain detached prepared UI values and
strict CSO1 framing. They preserve preparation title/progress/completion and up
to128 points keyed by native type plus machine index. Points retain world anchors,
colors, icon, queue index (including native -1), HP/progress pair and presentation
flags. They do not carry campaign objects, commands or host screen coordinates.
The49-byte point layout and bounded strict UTF8 title reject truncation, extra
bytes, unknown magic, hostile counts/lengths and noncanonical booleans. Validation
rejects nonfinite numbers, duplicate identities and unknown native point/icon enums;
deep snapshots isolate lists and entries. SiegeOverlayTests passed in the complete
CoopCampaignMapPrototype.ContractTests executable, including empty/completed camp,
construction and broken wall cases. Initial sandbox execution stopped later in
existing temporary-file tests; the authorized rerun passed. This is foundation
only: host capture, correlated transport, UI binding and native acceptance remain
unimplemented. Protocol51/bridge46/catalog10 and installed b194 are unchanged.
No production diagnostic or host mutation is introduced. Other battle adapters
and lifecycle code are unchanged; future integration must verify siege departure,
assault/sally return, dialogue/menu suspension, late join and detached camera.

r35 follow-up: native siege renders the tent but not its flag. The host catalog
contains CST1 with banner length0. Native AddTentEntityForParty stores/reuses
component type3 (ClothSimulator), whereas the sampler enumerated only MetaMesh.
CoopCampaignMapSiegeTentCapture now additionally reads at most one cloth component
through GetFirstMetaMesh, requires campaign_flag, and rejects duplicate/unknown
flag geometry. This is a read-only native getter under existing ownership and
disclosure gates, not a new diagnostic. Ordinary party capture is unchanged.
b193 isolated client/dedicated builds passed; native acceptance is pending and
b191 remains installed. Required regression includes absent flags, cached flag
reuse, siege departure/reconnect and ordinary encounter/travel visuals.

r36 b193 observation confirms banner transfer (172-byte CST1,68 banner bytes)
and a visible pole, but not cloth visual parity. Native ApplyWindEffect explicitly
calls SetForcedWind(-StrategicEntity.GetGlobalFrame().rotation.f,false) on the
cached cloth component. Replica creation previously omitted this. The next source
candidate retains only its owned flag cloth component, applies the native wind
rule when applying the world frame, and clears the reference before root removal.
It rejects unexpected cloth component counts rather than mutating another object.
This is runtime-sensitive owned rendering, not host mutation or diagnostics.
Installed b193 remains unchanged; native cloth motion/parity is not yet accepted.

Latest correction candidate: r32 user reports bandit encounter animation
but stale attack during travel after selecting Surrender; no battlefield mission
took place. Do not classify this as a completed battle test. Host revision2391 carries
act_none. PartyVisual.Poses previously treated that native release sentinel as a
playable clip requiring a nonnegative index and positive duration, and skipped
restoring a negative-index original sentinel. The candidate admits act_none before
clip lookup, applies it without phase steering, clears channel playback, and permits
sentinel restoration on release. Full human/mount/caravan validation still precedes
mutation. It is shared by field/village/raid/sally/siege-outside visuals, not a
bandit-only special case. Native post-event travel, mounted transitions, expiry and
hide/reveal acceptance remain required. b185 was blocked by sandbox CIM access;
b186 compiled both roles with installed inventories unchanged. No correction
deployment or native fix acceptance yet; r32 remains installed b184.

Subsequent r33 b186 native acceptance FAILED after Surrender without battle.
Verified loaded identities rule out stale installed binaries. Host continues to
publish act_none while replica attack motion persists. Installed-engine inspection
of MBSkeletonExtensions confirms SetAgentActionChannel directly forwards an action
index; it does not expose a separate managed clear/reset operation. Skeleton exposes
GetAnimationAtChannel separately from GetActionAtChannel, so an action sentinel alone
does not establish that the rendered clip was cleared. Retained MobilePartyVisual
refresh explicitly calls ResetPartyIcon after IsVisualDirty admission and recreates
its actors. Next investigation must distinguish receipt loss from retained native
animation state, and evaluate one-time owned replica reconstruction at the release
boundary rather than assuming the sentinel setter reproduces host lifecycle.
No second fix or native acceptance is claimed by this research.

Next source candidate adds RequiresVisualRecreation: an applied explicit action
followed by act_none or missing/revoked pose requests one owner-screen actor
reconstruction. MissionView checks it before TryEnsurePartyVisual and rebuilds the
human/mount/caravan tuple from the current disclosed catalog at the current frame.
Hidden release retains a pending reconstruction bit until reveal; Reset clears it.
Fresh/locomotion actors and repeated explicit actions do not request recreation.
This avoids relying solely on a native sentinel setter. Pure transition tests cover
raid/foot/mount actions, revoked data and no repeated reset. Native lifecycle,
flicker and dense-stream lease churn remain unverified; b186 stays installed.

Verification follow-up: b187 both isolated builds and c159 all26 contracts passed.
r34 installed b187; campaign40808, MP48888 and dedicated21324 loaded matching role
MVIDs. User manually repeated Surrender without a battlefield mission and confirms
the lingering animation is fixed. This supersedes b186 as installed candidate and
is bounded manual acceptance only. Raid/mounted/field-return and dense-party lease
capacity remain open. User continues manual testing; r34 cleanup is pending.
Subsequent user acceptance also covers an actual bandit battle and correct
post-battle animation. Raid/mounted/siege and dense-party lease tests remain open;
do not retain ordinary bandit battle return as an entirely untested gate.

Siege presentation follow-up (read-only research,2026-10-23): native
IsPartOfBesiegerCamp requires BesiegedSettlement.SiegeEvent and
BesiegerCamp.HasInvolvedPartyForEventType(party, Siege=5). That branch replaces
character creation with AddTentEntityForParty, not a generic attack action.
The tent uses map_icon_siege_camp_tent at local scale1.2. Optional campaign_flag
uses the leader clan banner, height0.2 for army leader/0.15 otherwise, and native
MBMath.Map strength-based scaling (including the non-army-leader factor0.8).
Do not infer clipping or strength disclosure from the formula; prefer capture
of already visible presentation/frames under existing host disclosure gates.

Current ResolvePartyVisualState fills absent HumanAgentVisuals from
CaptureCharacterVisual(visualLeader), and similarly fills a mount. Thus native
absence in a siege camp can become an invented foot/mounted figure, not a tent.
PartyVisualKind has only None/Foot/Mounted/Caravan; neither transport nor renderer
represents a tent. Implementation needs explicit host-owned tent presentation,
no character fallback for that native case, bounded frame/banner transport with
catalog serialization/clones/revisions, owned client geometry, and camp enter/
leave/reconnect/disclosure tests. Settlement wall/preparation visibility does not
cover party tents or siege engines/UI. This is research, not implemented support.
Installed b187 and live r34 remain unchanged.

Tent foundation follow-up: `CoopCampaignMapSiegeTent` now holds detached local
tent/optional flag frames and the host banner code. Mesh names are fixed native
identifiers, not remotely supplied resource paths. It validates finite nonsingular
bounded transforms and the complete flag/banner tuple, and clones both arrays.
`SiegeTentTests` covers absent/present flags, clone isolation, malformed banners,
invalid numbers and singular/wrong-size frames. The complete map-contract project
passed on2026-10-23 after an initial sandbox filesystem denial; this is one project,
not all26 or product compilation. No capture, catalog field or renderer is
connected yet; protocol50/bridge45/catalog9 and installed b187 remain unchanged.

Next source step adds `CoopCampaignMapSiegeTentCodec`, inner CST1 framing with
56-byte minimum and616-byte maximum. A zero banner length requires exactly the
tent frame; a nonzero bounded ASCII banner requires exactly one flag frame.
Strict UTF8, total-length equality and semantic validation reject malformed input
before publication. Extended SiegeTentTests passed round trips with/without flag,
every-prefix truncation, trailing bytes, hostile lengths, wire NaN, unknown magic
and malformed UTF8 in the full map-contract project. This codec is not yet called
by catalog/file/network transport; decoding does not grant disclosure authority.

Native sampler source candidate: `Campaign/CoopCampaignMapSiegeTentCapture.cs`
reads existing visible strategic children and named tent/flag MetaMeshes on its
construction thread. Admission requires current MapState/campaign/visual-manager
identity, no conversation/mission, current visible non-dirty party, no human/mount/
caravan visuals, and native BesiegerCamp Siege membership. Child/component scans
are bounded to16 each; duplicate/unknown tent geometry is rejected. It copies the
child local frame and flag mesh frame, without calling creation, strength or
mutation APIs. The caller must apply current body disclosure before invocation.
Native getters remain runtime-sensitive; the helper is NOT connected to the
publisher and has no runtime acceptance. b188 CompileOnly passed both modules with
installed inventories unchanged. b187/r34 remains the installed test candidate.
Before integration, verify captured flag name/banner and exact local-frame parity
in a real siege, then wire explicit tent kind, cache invalidation and catalog
transport before allowing replica rendering. Do not use capture failure as a
reason to synthesize the absent campaign hero.

Catalog integration candidate: protocol51/bridge46/catalog10 introduces explicit
SiegeTent=4 and canonical Base64 SiegeTentPresentation in full/catalog entities.
Clone, catalog delta equality, dynamic merge and publisher catalog projection
preserve it. Both host-file layouts and binary catalog carry the required field.
Admission rejects tent-with-actors, settlement tents, absent/malformed tent data,
and tent payload on another visual kind. A complete tent does not require a human
or culture fallback. Focused map-contract tests pass transport/clone/delta and
malformed-kind cases; the first run caught an old version assertion, now updated.
b189 product build failed because the dedicated explicit Compile inventory omitted
the new shared files; those references are now added. No publisher calls the native
sampler yet and no tent renderer exists. Do not deploy this intermediate candidate
or mix it with installed b187 protocol50/bridge45/catalog9.
Follow-up b190 passed both isolated product builds and confirmed installed module
inventories unchanged. This is compile evidence, not a full26-contract or native pass.

End-to-end source candidate b191: publisher now excludes camp participants from
ordinary character fallback/cache reuse, captures the tent after body disclosure,
and publishes None on capture failure rather than an invented hero. Camp values
are freshly sampled; tent-to-travel rejects cached tent actor state. The client
PartyVisual.Tent creates only an owned root/child in the replica scene, copies
native tent/flag meshes and local frames, and uses private banner materials with
the stock banner texture provider (no campaign MapScreen cache). Texture callbacks
check owner lifetime; reset clears the root before removal. Tent values participate
in the visual attempt key, causing replacement on presentation changes. The tent
branch returns directly and never falls through to character creation. b191 both
isolated product builds passed with installed modules unchanged. Native mesh-name,
banner appearance, camp enter/leave, hidden/reveal, reconnect, siege assault/sally
return and teardown safety still require verification; no native siege pass.
Full regression contract run map-siege-render-c160-20261023 passed all26 projects.
This supplements b191 compilation only; no staging or live siege test has occurred.

Siege-engine/overlay research (2026-10-23, installed SandBox.View/GauntletUI):
SettlementVisual.AddSiegeIconComponents creates only active deployed ranged/melee
engines, with attacker/defender and slot identity; reserve/construction entries
are not equivalent to completed scene machines. Root prefabs come from the host
SiegeEventModel with wall level and side, and their authored local frames are
composed with slot global frames. A tagged siege_machine_mapicon_skeleton child
has separate reload/fire animation. Existing private lists retain root, side,
slot, placement frame and animated child; projectile lists are separate and
initially invisible. Per-frame code rotates machines, advances animations and
positions projectiles from host campaign time. Therefore static mesh replication
or copying slot coordinates alone is insufficient for1:1 bombardment fidelity.
Preferred capture boundary is these already prepared visible entities, not client
re-execution of SiegeEventModel/campaign targets. Need bounded descriptor plus
dynamic pose/projectile visibility, deletion and session/disclosure admission.

GauntletMapSiegeOverlayView constructs MapSiegeVM from current player siege and
five native slot/wall frame groups, loads MapSiegeOverlay on the nameplate layer,
updates it with camera distance and suspends it during conversations. Its selection
and production-controller commands mutate campaign state. Replica must project a
detached read-only VM, not instantiate the native campaign VM or forward those
commands. Host-screen coordinates are unsuitable when client camera is detached;
capture world anchors and host-visible values, then project with client camera.
This is research only; engine/projectile/overlay implementation remains open.

Installed MapSiegeVM/MapSiegePOIVM inspection (2026-10-23) refines that boundary:
capture PreparationProgress, PreparationTitleText, IsPreparationsCompleted and
the prepared PointsOfInterest list. Each item exposes Type, MachineIndex,
MapSceneLocationFrame, colors, MachineType, CurrentHitpoints, MaxHitpoints,
QueueIndex, HasItem, IsConstructing, IsPlayerSidePOI, IsFireVersion and IsSelected.
Do not call UpdateProperties/RefreshValues on host during capture: they derive
campaign state; copy the already published binding values after native update.
The same HP bindings represent actual HP for active engines, redeployment
progress for redeploying engines, and construction progress otherwise (max1).
Preserving the prepared pair avoids replacing progress with guessed health.
Walls use per-section HP and switch machine type to BrokenWall at zero.
Tower indexes are offset by batteringRamFrames.Length; identity must include
POI type plus index, not index alone. Native visible-range threshold is camera
distance20 and projection uses WorldToScreenInsideUsableArea with finite-value
fallback and100px bounds margin, then depth sorting. Client must reproject world
anchors for its own camera while retaining host disclosure. Read-only replicas
must not invoke ExecuteSelection or native tooltip producers that query Machine.

Cache integration gate: `CanReusePartyVisualState` currently keys reuse on the
human/mount/caravan native object identities plus dirty/character checks. Tent
visuals have no human actor; flag-frame/banner changes must participate in cache
identity/revision rather than relying on those null references. Capture must read
the already prepared native child/mesh local frames, never recompute undisclosed
strength. Validate camp entry/leave, army leader changes, visibility revocation,
late join and siege assault/sally/map return independently. Siege engines and
siege UI remain separate unimplemented parity gates.

Current CoopCampaignMapPrototypePartyVisual.Update only ticks human/mount/caravan
visuals with movement and speed, then applies the frame. It does not assign map
battle/raid action channels. Retained native MobilePartyVisual decompilation in
artifacts/native/map-party-banner-20260928 selects leader and mount actions in
GetPartyBattleAnimation before AddCharacterToPartyIcon. Selection depends on
weapon, mount species and map event; settlement-associated unmounted leaders can
use act_map_raid. Event admission also considers an attached army's leader event.
The native creation branch replaces besieger-camp participant figures with tent
entities. These are separate missing fidelity paths, not merely movement bearing.

Before implementation, validate retained native code against installed assemblies,
resolve numeric event values to current enum names, inspect action update/lifetime
and party disclosure. Transfer host-selected presentation without invoking host
visual creation, randomization or campaign commands. Check field/village/raid,
mounted/unmounted/army and siege-tent transitions, plus return to ordinary travel.
Siege wall/preparation state already has publisher and renderer plumbing; this is
not proof of complete camp/engine/UI fidelity. No new animation implementation or
runtime acceptance is claimed by this audit.

Fresh installed-assembly verification on2026-10-23 used ilspycmd against
Modules/SandBox/bin/Win64_Shipping_Client/SandBox.View.dll, SHA256
C7360E71DA06799A0CB6C00C4A0CFAB5877621197BA25101433AC28654D169E2.
MobilePartyVisual still has the audited battle-action and tent branches. Current
TaleWorlds.CampaignSystem.MapEvents.MapEvent.BattleTypes resolves1=FieldBattle,
2=Raid,7=SallyOut,8=SiegeOutside and5=Siege. Thus visual attack admission is not
equivalent to every active MapEvent; hideout/forced recruitment/supplies and naval
events must not be assigned these actions merely because an event exists.
Publisher already receives the exact MobilePartyVisual and tracks human/mount/
caravan visual identities for appearance caching. Prefer a separate dynamic pose
sample from these disclosed visuals: caching animation with appearance would
freeze it until recreation. Current dynamic entity state carries movement but no
action/phase/speed. Existing mission-dialogue capture demonstrates skeleton
GetActionAtChannel/GetAnimationParameterAtChannel/GetAnimationSpeedAtChannel;
map-specific ownership, lifetime and validity guards remain necessary. Native
AgentVisuals.SetAction assigns channel0, while Tick delegates to native animation;
verify transition handling before combining these on the replica. No host
SetAction or native visual creation is authorized by the capture design.

Source foundation now adds CoopCampaignMapPartyAnimation: a detached channel-zero
action identifier/phase/speed value with ASCII identifier bounds, finite normalized
phase and bounded nonnegative playback speed, plus independent cloning. It does
not yet alter entity contracts, protocols, capture or rendering. The numeric
admission range must be checked against native samples before integration; rejected
samples must never be clamped into a different host animation. PartyAnimationTests
covers valid raid/foot/mount identifiers, clone isolation and malformed numbers/
identifiers. The focused CoopCampaignMapPrototype.ContractTests executable passed
on2026-10-23 with COOPSPECTATOR_REPOSITORY_ROOT set and normal host filesystem
access. Earlier direct attempts stopped on missing root and sandbox file access,
not a passing full run. This is one project, not all26 projects, product build,
deployment or native animation acceptance. Installed b173 remains unchanged.

CoopCampaignMapPartyAnimationCodec now adds bounded CMA1 inner sample framing
(maximum144 bytes), exact length consumption and strict UTF8 decoding. Focused
tests passed round-trip, every-prefix truncation, trailing data, wrong magic,
invalid UTF8 and wire NaN cases. The entire map-contract executable passed after
this change. No entity/session/revision envelope or native producer/consumer is
connected yet; successful decoding is not authorization. No protocol bump or
installed-module change has occurred at this foundation stage.

CoopCampaignMapPartyAnimationCapture adds an unwired native read helper bound to
its construction thread. After caller disclosure admission it samples human,
mount and caravan channel0 action/phase/speed, rejects missing human/invalid
samples and verifies visual object identities before returning the tuple. It
does not invoke SetAction, recreate visuals or infer a human when native shows a
tent. Caller integration must additionally prove current map/party ownership and
bind the tuple to session/entity/appearance revision; passing hostDisclosed alone
is not sufficient authority. Native read lifetime remains runtime-sensitive.
map-party-animation-b174-20261023 compiled both product roles successfully with
installed module inventories unchanged. No capture call or native acceptance yet.

CoopCampaignMapPartyPose and its CMP1 inner codec now group human/mount/caravan
samples atomically with an appearance revision. Admission requires a valid human,
exact revision and matching optional mount/caravan presence; clones detach every
sample. Decoder bounds the complete frame to452 bytes and each nested CMA1 sample,
rejecting truncation and trailing bytes. PartyAnimationTests covers all four
optional-animal shapes, revision/shape mismatch and nested clone isolation. This
is still an unwired value/codec, not entity/session admission or live replication.

Capture ownership follow-up checks active MapState, no conversation/mission,
current campaign/visual-manager identities and exact party-to-visual mapping
before reading, then rechecks campaign/manager/mapping/visual identities afterward.
b175 failed client compilation due to a missing Party namespace import; corrected
before b176. Dynamic batches currently contain up to2 entities; maximum CMP1 is
452 bytes per party (604 Base64 characters), so inserting raw Base64 into every
existing dynamic batch requires a packet-size audit first. Session and revision
transport integration remains open; do not claim that the new source sends poses.

Network audit: installed TaleWorlds.MountAndBlade.dll SHA256
19387F31557FF840D14F378F6BBDF1D58FCFF406AB9FF2294DFBB3E49A50B87E was inspected
with ilspycmd. GameNetworkMessage.ReadStringFromPacket allocates1024 bytes, while
WriteStringToPacket encodes UTF8 and forwards a byte array. This establishes a
per-string read bound, NOT the total native packet capacity. Do not conflate the
two. Existing conversation poses use a separate bounded binary byte-array message
(current codec limit901), whereas map dynamic batches combine up to2 entities.
Prefer a separate bounded party-pose message with explicit session/entity/sequence
and appearance correlation over enlarging both movement records with Base64.
Keep movement publication independent and revoke poses on disclosure loss, source
absence, appearance replacement and session teardown; no stale pose replay on
rejoin. Bound total sample publication, not just individual payload size. This
decision still requires producer, receiver, replay/freshness tests and native
validation. b176 passed both isolated builds with installed inventories unchanged.

CoopCampaignMapPartyPoseEnvelope adds CPE1 framing (maximum868 bytes) for a
nonempty GUID session, bounded UTF8 entity ID, monotonic nonnegative sequence and
CMP1 pose. Matches checks current disclosure, session/entity, increasing sequence
and exact appearance revision/body shape. It is not a stateful replay cache: the
consumer must retain lastSequence and clear state at the correct lifetime edges.
Focused map contracts passed envelope round-trip, every-prefix truncation, wrong
session/entity, replay and hidden-target rejection after correcting an initially
inaccessible private validation helper reference. No network message registration,
host bridge writer, receiver or rendering integration is present yet.

CoopCampaignMapPartyPoseMessage now declares a server-to-client bounded binary
message using CPE1 validation and detached constructor copying; dedicated project
explicitly links it and all five animation/pose/envelope infrastructure files.
There is still no send call or receive-handler registration. Adding a message
type changes the module message inventory: do not deploy this intermediate source
against retained b173; complete integration and bump/check protocol compatibility
before any live run. Installed modules remain b173.

Protocol is now49 (host bridge remains43) because the state network message now
also carries the host session GUID in normalized N form. Previously the client
state had no campaign session identity, so accepting a pose's self-declared session
would not prove current-session correlation. ToNetworkState and Clone preserve it;
message decoding rejects empty/malformed session IDs. b178 compiled both roles
without installed changes. Focused tests include session projection and cloning.
This establishes the prerequisite for receiver admission; at b178 no pose handler,
send loop or render consumer was connected. Never mix this candidate with b173.

2026-10-23 integration follow-up: b179's terminal artifact confirms both builds
passed. The client now registers the pose handler, checks current map/disclosure,
body shape, session, completed catalog revision and monotonic receipt sequence,
revokes inadmissible receipts and expires them after two seconds. Reset clears
the receipt store. There is still no renderer consumer.

Source protocol49/bridge44/catalog9 adds PartyPoses to the host snapshot and its
deep clone. The bounded host-file batch contains at most64 individual CPE1 frames,
requires exact publication/session/catalog binding and unique case-insensitive
entity IDs, and rejects noncanonical Base64, malformed frames and mixed revisions.
Active dialogues require an empty batch, including standalone mission publication.
Publisher CapturePartyPoses reads currently disclosed, body-admitted parties only,
checks native visual identity against the published appearance resources, rejects
dirty/replaced visuals and rotates through candidates with at most64 native samples
per publication. Capture never invokes native action setters or creates host visuals.
The server rechecks body/disclosure/shape, translates host catalog/sequence into
network revisions, and forwards individual bounded frames only to synchronized
peers whose catalog transport is complete at the current revision. No replay queue
or cached join pose is sent. Client rendering, native safety/smoothness, packet-load
and dense-party refresh-capacity acceptance remain open. At2048 eligible parties,
64 per0.2s publication can exceed the two-second receipt lease: this candidate is
NOT full-density acceptance and needs scheduling/freshness validation before release.

b180 (host capture/file transport) and b181 (server forwarding) passed both isolated
builds with installed inventories unchanged. Focused map contracts passed batch
round-trip, clone isolation, duplicate/mixed revision/dialogue rejection and corrupt
framing tests. map-party-animation-c155-20261023 passed all26 contract projects. Installed
b173 remains unchanged; no game was launched and no animation visual pass is claimed.
Optional native animation capture failures return an empty batch without suppressing
the main map publication; final b182 passed both isolated builds with installed
inventories unchanged after this guard. Renderer integration must not blindly reuse the dialogue
playback helper: its speed domain is0..10, whereas the provisional map sample bound
is0..16. Validate actual native ranges and implement consistent playback admission
rather than clamping host animation speed or throwing during a map render tick.

Renderer continuation (2026-10-23): PartyVisual.Poses now consumes admitted receipts
on the mission-screen tick, only for exact catalog-built actors. It validates all
human/mount/caravan actions, action sets and durations before applying a tuple.
Action changes seed phase once; same-action samples steer playback rate without
repeated seeks. Hide/expiry releases the override and restores the original native
action only if it still owns the current channel; reset discards actor-bound state.
Native exceptions follow the existing outer visual-reset/retry path. This does not
make native animation/render lifetime validated. b183 both builds and c156 all26
contracts passed before the clock correction below.

Fresh installed Campaign.TickMapTime and MapScreen/MobilePartyVisualManager reads
show that native AgentVisuals.Tick receives CampaignDt, not real frame dt. Campaign
uses0.25 times real dt for play, multiplied by SpeedUpMultiplier for fast-forward,
and zero for Stop/FastForwardStop or stoppable modes while waiting. No native
ComputeIsWaiting call is made by capture: it reads the prepared IsMainPartyWaiting.
Source now transfers this clock scale in CMP2 (max456 bytes; CPE1 max872), protocol50/
bridge45/catalog9, and advances owned visual animation/playback in that domain.
Unknown/invalid time modes fail closed without clamping. The reusable playback
helper retains its default0..10 dialogue domain; map instances explicitly use0..16.
Tests cover every installed time mode/wait combination, custom fast multiplier,
clock serialization/cloning, invalid clock payload and unchanged dialogue limits.
All animation, pause/fast-forward, transition and load acceptance remains native-
unverified. Dense-party scheduling remains open; no deployment of this source yet.
Final map-party-animation-b184-20261023 passed both isolated product builds with
installed inventories unchanged; map-party-animation-c157-20261023 passed the full
26-project canonical inventory. Native acceptance steps are in the test runbook's
party-animation section. No product process was launched in this source stage.

Installed MapBar.MapTimeControlVM uses BasicTooltipViewModel string callbacks
for Pause/Play/FastForward. Keyboard variants write TEXT/HOTKEY variables and
format str_hotkey_with_hint using prepared map-bar shortcuts; gamepad variants
omit hotkeys. TimeOfDayHint instead returns TooltipProperty rows from
CampaignUIHelper.GetTimeOfDayAndResetCameraTooltip: host CampaignTime hour selects
Morning/Noon/Afternoon/Evening/Night and a second row says Click to Reset Camera.
Do not replace that structured tooltip with a plain local-time string.

MapNavigationItemVM.GetTooltip selects NavigationElement.Tooltip only when
Permission.IsAuthorized or IsActive; otherwise it returns Permission.ReasonString.
AlertTooltip is separate. BasicTooltipViewModel.ExecuteBeginHint may invoke
string, structured-list, or arbitrary prebuilt callbacks and writes global UI
state; ExecuteEndHint hides global information. Do not invoke either host action
as a capture getter. Audit known read-only providers and isolate text variables,
retain exact prepared source/session admission, and transport presentation only.
Current CoopCampaignMapBarHintVM remains inert; implementation and native gates
for these controls are still open. No battle lifecycle changes are needed.

CoopCampaignMapControlHint now provides a detached, not-yet-wired presentation
value: exactly one nonempty plain-text shape or a bounded structured-row shape,
32-row/4096-character bounds, finite color/flag/height checks, valid UTF-16 and
deep row cloning. ControlHintTests exercises both shapes and malformed inputs.
This does not change existing network formats or enable any displayed hint.
Host capture, lifetime-bound target admission, transport and client ownership
remain to be implemented before this foundation can be called a working feature.
map-control-hints-c143-20261022 passed all26 contract projects. No new product
build, deployment or native acceptance is claimed for the control-hint value.

CoopCampaignMapControlHintCodec adds the CMH1 inner presentation format with
strict UTF-8, 1-MiB framing bound, per-string/row limits, exact stream consumption
and fail-closed decoding. It preserves all structured row fields, including
transparent colors and height/flags. Round-trip, every-prefix truncation, extra
bytes, wrong magic and oversized input are covered in ControlHintTests. It is
not yet inserted into the session/generation-bound tooltip transport; decoding
alone never authorizes a hint or selects a host provider.
Initial c144 passed25/26 projects and exposed an uncaught InvalidDataException
in malformed-frame decoding. Explicit invalid-data handling was added to both
codec directions; c144 is not a passing validation record. Follow-up
map-control-hints-c145-20261022 passed all26 contract projects, and b163 compiled
both product roles with installed inventories unchanged. These are foundation
tests/builds only, not connected hint behavior or native acceptance.

Integration must extend all existing bar-hint boundaries together: published
CoopCampaignMapBarPresentation and its codec; ContainsBarHint admission on both
server/client; publisher current-source resolution; tooltip payload kind/codec;
and UI hover plus ownership-aware presentation. Reusing only the token syntax
would not authorize the new targets. Existing statistic tokens must retain their
current source identity/revocation behavior. Installed b162 remains unchanged.

Source follow-up introduces CMB3 control target entries (key, opaque token,
availability), with protocol47/host bridge42/catalog9. The entries are bounded,
deep-cloned and validated for unique presentation keys. Shared target admission
counts collisions across statistics and controls, rejects unavailable controls,
and does not incorrectly tie control hints to statistics-panel visibility.
MapBarPresentationTests now covers these rules, target round-trip, CMB2 rejection
and canonical control booleans; existing truncation tests include the new fields.
This is still incomplete integration: no host control producers, response payload
or client hint binding are connected. Do not deploy or mix this source with b162.
c146 passed25/26 projects, stopping at the stale protocol46/bridge41 expectation
in SettlementMarkerTransportTests before later map checks. That expectation now
matches47/42. c147 passed all26 projects, including control-count bounds,
opaque-token and path-key rejection checks. c145/b163 do not cover this follow-up.

Subsequent source adds tooltip payload version3 and BarControl kind9 carrying
the bounded CMH1 presentation inside the existing session/target/generation/
sequence envelope. Mixed outer rows/rundown/control shapes are rejected; control
targets require opaque bar identities. Dedicated compilation explicitly includes
both control presentation files. ControlHintTests covers plain/structured outer
round-trips, stale generation, every-prefix truncation and mixed shapes. c148 and
b164 validate this later change, not covered by c147: b164 compiled both roles
with installed inventories unchanged; c148 passed all26 projects. Capture
and hover bindings are still unwired; installed b162 is unchanged.

The next presenter candidate registers a private HintVM/HintTooltip mapping for
plain controls and uses the existing PropertyBasedTooltip mapping for structured
control rows. Installed MBInformationManager.ShowHint delegates to the same
InformationManager.ShowTooltip system; HintVM.Text and the stock HintTooltip.xml
are the native text presentation. Private registrations preserve source ownership
without registering/replacing the global string provider. Shape changes release
only the owned movie; same-shape refresh updates in place. Tests model native
event ordering and cover text refresh, text-to-rows switching, foreign-tooltip
preservation and replacement-owner disposal. b165 compiled both roles with
installed inventories unchanged; c149 passed all26 projects. This is not
native acceptance and has no host producer or control-hover binding yet.

Next source follow-up binds distinct read-only control hover models to published
keys: TutorialNotification/GatherArmyHint/ExtendHint/TimeOfDayHint/PauseHint/
PlayHint/FastForwardHint and Navigation_/Alert_ plus native item ID. Missing,
hidden or unavailable targets end interest; unchanged publication preserves it.
Navigation alerts additionally require HasAlert. Modal clearing and finalization
release each hint. Campaign commands remain inert. MapBarPresentationTests adds
hover routing, unchanged refresh, unavailability, modal clearing and disposal
coverage. b166 compiled both roles with installed inventories unchanged; c150
passed all26 projects. Host follow-up now publishes the four time-control targets
with stable MapTimeControlVM ownership (not the per-tick TimeOfDayHint wrapper).
Current-source resolution recaptures visibility before and after content capture.
CoopCampaignMapTooltipCapture.Controls.cs uses the audited read-only time-of-day
provider and prepared shortcut fields/native localized time-control labels inside
the existing text/language isolation scopes. It invokes no host hover or command.
Publisher routes admitted control tokens separately from statistic tokens. b167
compiled both roles with installed inventories unchanged; navigation/army/extend/tutorial host producers and native acceptance
remain open. The installed b162 is unchanged; source remains protocol47/bridge42.

Navigation provider audit: MapNavigationItemVM.GetTooltip re-evaluates Permission,
uses NavigationElement.Tooltip when authorized or active, otherwise ReasonString.
AlertTooltip is a separate getter. MapNavigationElementBase delegates these to
derived providers, not stored strings. Installed Party/Inventory providers read
navigation/mission/raft/prisoner permissions, then assign local TEXT/HOTKEY on
str_hotkey_with_hint; party alerts call the view-data tracker. Never invoke
OpenView/GoToLink/RefreshStates. Before enabling these routes, audit the remaining
derived providers and their alert helpers. AreControlWritableTemplatesIsolated
now guards the shared hotkey template copy shape; capture also retains global
text and language restoration. This later guard is not covered by b167.

Further native audit found ClanNavigationElement.GetPermission clears its cached
permission and dispatches ClanScreenPermissionEvent. Never call that getter or
its Tooltip getter from replica requests. Clan requires an observed/prepared
permission path, not synthetic re-dispatch. Other audited native navigation
types (party/inventory/character/kingdom/quests/escape) now have exact-type-gated
normal hint publication and capture. Capture preserves native authorized-or-active
vs ReasonString selection, inside text/language scopes; no navigation commands
or alert providers are invoked. Removed navigation sources are pruned and tokens
revoked. b168 compiled both roles with installed inventories unchanged. c151
passed all26 projects before this navigation follow-up (not coverage of it).
Clan, all alert getters, gather-army, extend and tutorial
remain incomplete; this is not native acceptance of the new hints.

Alert follow-up audits ViewDataTrackerCampaignBehavior.GetPartyNotificationText,
GetCharacterNotificationText and GetQuestNotificationText: each copies its text
template and sets NUMBER from the prepared count; notification clearing/update
commands are separate and are never invoked. Visible native alerts now publish
separate Alert_ targets. Capture requires the exact audited tracker type and
rejects templates with the unsafe shared-empty-attributes shape, verifies both
prepared and current native HasAlert, then reads AlertTooltip in isolation.
MapBarPresentationTests exercises alert disappearance even with a stale available
token. b169 compiled both roles with installed inventories unchanged; c152 passed
all26 projects. This predates the prepared-hint follow-up below.

Prepared hint follow-up: GatherArmyHint is native HintViewModel.HintText populated
by MapBarVM.UpdateCanGatherArmyAndReason; ExtendHint holds str_map_extend_bar_hint.
The replica now publishes these exact prepared objects and reads HintText inside
existing isolation, without calling the army model or ExecuteBeginHint. Gather
requires IsGatherArmyVisible; extend requires IsInfoBarEnabled. Identity pruning
occurs after all sources are collected so stable prepared hints do not churn.
b170 compiled both roles with installed inventories unchanged. TutorialNotification was confirmed to be ElementNotificationVM
with ElementID (tutorial highlighting), not a textual hint; its visual replication
is a separate remaining task. Clan hint and native acceptance remain open.

Tutorial source follow-up carries TutorialElementId in CMB4, protocol48/bridge43/
catalog9, reads the prepared host ElementID and binds a detached tutorial VM to
stock MapBar.xml ElementNotificationWidget. Empty IDs clear highlighting; IDs are
bounded non-path keys and finalization clears them. No tutorial events or native
notification VM are created on the client. Contract cases cover transfer, binding,
finalization and rejection of CMB3. b171 compiled both roles without installed
changes; c153 passed all26 projects. This is source wiring,
not proof that native tutorial frames render correctly. Installed b162 unchanged.

Clan hint candidate uses the prepared MapNavigationItemVM enabled/active state
and exact ClanNavigationElement._mostRecentClanScreenPermission nullable field.
It never calls that element's Permission or Tooltip getters (event dispatch).
Enabled/active label uses the audited str_clan/native key41 formatting; disabled
label requires a cached denial and uses its ReasonString. Inconsistent/missing
denials fail closed. This is prepared-state semantics, not proof of fresh-event
parity: native acceptance must cover enabling/disabling and reason replacement,
including outer navigation gates. b172 compiled both roles with installed
inventories unchanged. No new deployment performed.

Clan outer-gate correction: audited MapNavigationHelper.IsNavigationBarEnabled
checks hero/death, saving, navigation lock, battle simulation, map modal windows,
escape and locking elements without requesting clan permission. Prepared clan
capture now applies this gate before reading the cached event denial, preventing
a stale reason from leaking through a newer outer block. b173 compiled both roles
with installed inventories unchanged; c154 passed all26 projects;
source-wiring checks explicitly guard the outer gate and absence of event/command
dispatch. These are not native transition tests.

Fresh map-control-hints-r31-20261022 staged b173: seven deployed files verified,
seven configuration preimages retained and prior b162 module copies preserved.
No game launch/native acceptance yet. See the local r31 OBSERVATION.md and
deployment.json. Installed state is now b173 (48/43/9), superseding earlier b162
notes for this run only; never mix role versions.

### Party label expansion verification

Installed native GauntletMapPartyNameplateView.OnMapScreenUpdate reads scene
input game key5 and assigns ShouldShowFullName for ordinary and player labels.
The replica previously reset that property to false for parties in UpdateIdentity,
despite already receiving caption, speed and wounded/prisoner strings. The
candidate registers native Generic/MapHotKeyCategory input and applies key5
after each mission tick's updates, gated by render readiness and map input
ownership. Modal/dialogue loss collapses the labels. It does not change payloads,
host disclosure, battle lifecycle or campaign commands. Main-party-specific
template/portrait parity remains separate work.

map-labels-b162-20261022 compiled both roles with installed inventories unchanged.
map-labels-c142-20261022 passed all26 contract projects, including an explicit
source-wiring guard for key5 and identity-update reset removal; this guard is
not a native input/widget behavior test.
b161 failed before compilation because sandbox CIM access was unavailable;
it is not a code failure or passing build. Native held/released Alt, updates
while held, modal return, hidden parties and post-battle/rejoin checks remain
open. r29 closed on b160 with all seven configuration preimages matching;
this candidate is not deployed.

Subsequent r30 deployed b162 coherently. MP39956 independently renders expanded
Yorig/Deserters name, speed and wounded/prisoner labels during user-held Alt;
the user confirms normal collapse on release. This is bounded raid-map acceptance,
not all-party/modal/battle/rejoin coverage. See r30 OBSERVATION.md for current
process identities and evidence limits; the run remains in progress.

| Concern | Files |
|---|---|
| Wire/domain snapshot model | `Network/Messages/BattleStartMessage.cs` |
| Snapshot line codec | `Network/Messages/BattleStartMessageCodec.cs` |
| Compact binary snapshot payload | `Infrastructure/BattleSnapshotBinarySerializer.cs` |
| Current normalized runtime projection | `Infrastructure/BattleSnapshotRuntimeState.cs` |
| Exact contract definition | `Infrastructure/ExactTransferSpawnContract.cs` |
| Contract construction | `Infrastructure/ExactTransferContractBuilder.cs` |
| Contract validation | `Infrastructure/ExactTransferContractValidator.cs` |
| Per-entry cache | `Infrastructure/ExactTransferContractRuntimeCache.cs` |
| Transfer stage state/machine | `Infrastructure/ExactTransferRuntimeState.cs`, `ExactTransferStageMachine.cs` |
| Server pre-spawn resolution | `Infrastructure/ExactCreateAgentServerPreSpawnContractResolver.cs` |
| Pre-spawn body/equipment injection | `Patches/ExactCampaignPreSpawnLoadoutPatch.cs` |
| Native/client create-agent corridor | `Patches/BattleMapSpawnHandoffPatch.cs` |
| Item/object catalogs | `ExactCampaignObjectCatalogBootstrap.cs`, `ExactCampaignRuntimeItemRegistry.cs`, `ExactCampaignRuntimeObjectRegistry.cs` |
| MP compatibility mapping | `CampaignMultiplayerHeroClassResolver.cs`, class fallback/override patches |
| Equipment/mirror policy | `CoopCampaignMirrorEquipmentResolver.cs`, `ExactWeaponSlotMaterializationPolicy.cs` |
| Mounted identity/link contract | `CoopMountedHeroMountLinkContract.cs` |

The runtime object registry file exists but its feature is disabled. Do not treat its types as the active path.

## Scenario infrastructure

### Commander deployment

`Infrastructure/CommanderDeployment/` contains shared contracts plus separate runtimes for:

- generic campaign deployment;
- ordinary field battle;
- generic land battle;
- sally out;
- village battle.

The readiness, facing-order, and commander-death handoff contracts are intentionally isolated and contract-testable.

### Field/general land

- `Infrastructure/LandBattle/ExactLandBattleScenarioContract.cs`
- `Infrastructure/LandBattle/ExactFieldBattleInitialMaterializationRuntime.cs`
- field and land commander-deployment runtimes
- field boundary calculation currently lives primarily in `CoopMissionNetworkBridge`.

### Village battle

- `ExactVillageBattleScenarioContract.cs`
- `ExactVillageBattleInitialMaterializationRuntime.cs`
- `ExactVillageBattleDeploymentBoundaryRuntime.cs`
- `ExactVillageBattleCommanderDeploymentRuntime.cs`

Village boundary data is versioned, hashed, transmitted, applied, and acknowledged separately from general materialization readiness.

### Siege assault

`Infrastructure/SiegeAssault/` contains several distinct concerns:

- `SiegeAssaultMissionOpenBridge`: selects/captures the native mission-opening profile.
- `CoopPreMissionTopologyRuntimeState`: pre-open scene/mode contract state.
- `ExactCampaignSiegeAssaultWithDeploymentRuntime`: deployment lifecycle.
- `ExactCampaignSiegeAssaultNoDeploymentRuntime`: isolated no-deployment path.
- `ExactSiegeAssaultInitialMaterializationRuntime`: client pacing/readiness for initial foot army.
- `CoopSiegeMachineDeploymentController`: machine selection, auto-deploy, native controller synchronization, detachments, and visual normalization.
- `SiegeMissionObjectIdBridge` / `SiegeMissionObjectIdMapRuntime`: stable synchronized object mapping.
- ladder interaction and merlon parity contracts/runtimes.
- peer/perk and formation-membership safety contracts.
- `CoopSiegeSceneOcclusionSafetyContract`: narrow remote-client decision for exact `SiegeMissionWithDeployment` scene occlusion.

Do not combine machine selection, unused-machine finalization, ladder state, scene-object ID mapping, initial army materialization, and reinforcement ownership into one conceptual fix. They touch different owners.

### Siege ambush

- `Infrastructure/SiegeAmbush/SiegeAmbushScenarioContract.cs`
- `ExactSiegeAmbushInitialMaterializationRuntime.cs`
- `Mission/CoopExactCampaignSiegeAmbushMissionController.cs`
- `Network/Messages/CoopSiegeAmbushOrderNetworkMessages.cs`
- `Patches/ExactSiegeAmbushDeploymentControllerPatch.cs`

Mounted rider/mount grouping is valid here; external siege assault is foot-only.

### Relief, sally out, and lords hall

- contracts: `Infrastructure/Relief/`, `Infrastructure/SallyOut/`, `Infrastructure/LordsHall/`;
- mission controllers: `CoopExactCampaignReliefMissionController`, `CoopExactCampaignLordsHallMissionController`, and `Mission/LordsHall/LordsHallMissionRuntime.cs`;
- campaign adapters are listed above.

### Hideouts

- `Infrastructure/Hideout/`: day/night scenario contracts, boss-phase/ambush rules, scene manifest, mannequin isolation.
- `Mission/`: day/night mission controllers, boss phase, ambush network controller, runtime scene manifest.
- `UI/`: stealth view, call-troops cinematic, boss cinematic/conversation, objectives, markers.
- `Patches/HideoutAmbushArrowBarrelPatch.cs`: scenario interaction patch.

## Runtime state and local bridges

### Battle/session state

- `CoopBattlePhaseRuntimeState`
- `CoopBattleAuthorityState`
- `CoopBattleSelectionIntentState` and `CoopBattleSelectionRequestState`
- `CoopBattleSpawnIntentState`, `CoopBattleSpawnRequestState`, `CoopBattleSpawnRuntimeState`
- `CoopBattlePeerLifecycleRuntimeState`
- `CoopBattlePeerReconnectState`
- `CoopBattlePeerSessionState`
- `CoopBattleAgentControlRuntimeState`

These types overlap deliberately: intent, authoritative request, spawn result, lifecycle, reconnect, and composed UI/session state are different layers. Before adding another flag, locate the existing owner.

### Bridge files

- roster: `Campaign/BattleRosterFile.cs`
- result: `Infrastructure/CoopBattleResultBridgeFile.cs`
- phase: `CoopBattlePhaseBridgeFile.cs`
- entry status: `CoopBattleEntryStatusBridgeFile.cs`
- selection: `CoopBattleSelectionBridgeFile.cs`
- spawn requests: `CoopBattleSpawnBridgeFile.cs`
- hero creation: `CoopHeroCreationBridgeFile.cs`
- campaign map prototype: `CoopCampaignMapPrototypeBridgeFile.cs`
- synthetic role matrix: `CoopRoleMatrixStreamBridgeFile.cs`
- exact diagnostic artifacts: `ExactBattleAgentSpawnTraceBridgeFile.cs`, `ExactBattleEntryCompatibilityBridgeFile.cs`, `ExactBattleRuntimeBundleBridgeFile.cs`
- shared I/O: `AtomicBridgeFileIO.cs`
- temporary local-host redirect state: `HostSelfJoinRedirectState.cs`

### Battle-test client join

- `Infrastructure/Automation/CoopAutomationJoinContract.cs`: schema 3, request validation, SHA-256 binding, exact server selection, optional native serialized `UniqueMapId` matching distinct from scene `Map`, and terminal-state rules.
- `Infrastructure/Automation/CoopAutomationJoinBridge.cs`: complete environment/profile validation, loaded-assembly hash, run-scoped request read, and strict atomic status publication including native platform-login attempt/task/outcome evidence.
- `Multiplayer/Automation/CoopLobbyAutomationDriver.cs`: narrow reflection adapter that resolves `TaleWorlds.MountAndBlade.NetworkMain` and the exact installed `TaleWorlds.MountAndBlade.LobbyState` from their defining assemblies. It validates `Game.Current.GameStateManager.ActiveState`, exact state identity, lobby-client reference equality, native login state/privilege, invokes public `TryLogin()` once, and then performs custom-server discovery plus custom-game join. Structural and state failures are published distinctly.
- `Multiplayer/Automation/CoopLobbyAutomationController.cs`: main-thread polling, one retained native platform-login task with no blind retry or false cancellation, explicit login outcomes, exact local-server ownership gate, native join lifecycle, GameNetwork handoff observation, terminal connected acknowledgement, and safe cancellation boundary.
- `Commands/CoopAutomationConsoleCommands.cs`: `coop.automation_join` status/start/cancel surface.
- `Patches/LocalJoinAddressPatch.cs`: authoritative patch on actual `GameNetwork.StartMultiplayerOnClient(...)`; applies the one-shot same-machine loopback rewrite, then notifies the run-scoped controller with the final address and native handoff arguments.
- `Patches/LobbyCustomGameLocalJoinPatch.cs`: optional historical reflection-based lobby rewrite for version compatibility; it is not an authoritative handoff observer and emits no duplicate notification.

This path is disabled by default, is independent of battle type, and stops at network connection evidence. It does not open a mission or own battle readiness/result state.

### Battle-test automation foundation

- `Directory.Build.props`: shared default-false compile-only output/intermediate/package routing.
- `Infrastructure/Automation/CoopAutomationRunContract.cs`: manifest, role instance, port/fixture identity, lease, envelope, event, stable outcome/reason, recovery, and known-issue contracts.
- `Infrastructure/Automation/CoopAutomationProtocolFileIO.cs`: strict atomic JSON, bounded shared reads, append-safe JSONL, and same-volume inbox processing.
- `Infrastructure/Automation/CoopAutomationRuntimeContract.cs`: strict runtime profile, loaded-role identity, `Suppress` result policy, and run-scoped owned-host validation.
- `Infrastructure/Automation/CoopAutomationRuntimeBridge.cs`: loaded assembly hashing, role status publication, result-policy resolution, and exact live owned-host confirmation.
- `Infrastructure/Automation/CoopAutomationFixtureContract.cs`: schema-1 exact campaign-roster fixture metadata, ordinary SCN-001 admission, immutable byte/hash/provenance validation, stable rejection reasons, and run-root path containment.
- `Infrastructure/Automation/CoopAutomationFixtureRecorder.cs`: separately opted-in post-write `battle_roster.json` byte capture, loaded-module identity check, immutable run-scoped payload/metadata/status publication, and disabled-mode no-I/O boundary.
- `DedicatedServer/Automation/CoopAutomationDedicatedControlContract.cs`: fixed schema-1 dedicated ready/request/status identities, bounded `ConnectionFeasibilityV1` profile, request validation, and exact seven-acknowledgement terminal validation.
- `DedicatedServer/Automation/CoopAutomationDedicatedControlBridge.cs`: default-off binding to `InitialListedGameServerState.OnActivated`, atomic request claim/status publication, main-tick native command dispatch, and native option/map/listed-server verification.
- `DedicatedServer/SubModule.cs`: initializes and shuts down the dedicated control bridge after runtime-profile validation and pumps it before other early-return paths in `OnApplicationTick`.
- `scripts/Invoke-CoopTest.ps1`: `Doctor`, full `Contracts`, client/dedicated `CompileOnly`, runtime `Feasibility`, private campaign `Record`, existing-run `Inspect`, fail-closed exact `Cancel`, and opt-in exact `Recover`. It owns the run root, runner lock, nonce fingerprint, lease, schema-2 runner status, events, assertions, provisional-to-verified process identities, cleanup, logs, and reports. Protocol 1.1 adds `RoleHealthV1`, `CancellationV1`, `RecoveryV2`, and `FailureEvidenceV1`; runtime commands acquire canonical game/dedicated/bridge/profile/port locks before product launch and verify release. `Feasibility` waits for structured dedicated readiness and all seven acknowledgements before UDP/client work, then validates and registers the complete client handoff before fallible live revalidation. `Record` requires clean local/upstream identity and an explicit installed client hash, validates the aggregate-owned campaign handoff, waits for an exact ordinary field fixture, independently rechecks byte/provenance/qualification evidence, retains private/non-oracle/non-L2 boundaries, discovers exact descendants, and proves protected-result/cleanup isolation. The connectivity bootstrap still omits optional `UniqueMapId` because no authoritative serialized native value is available. Terminal/non-terminal client and fixture status survive bounded failure without outcome promotion; PID logs and exact-correlated crash/hang evidence remain run-scoped.
- `scripts/CoopAutomationRunner.Core.ps1`: shared process-identity, descendant, output-capture, dedicated-control, role-health, cancellation-signal, shared-lock, exact-stop, and failure-helper-correlation primitives. `Get-CoopSharedRuntimeResourceIdsCore` constructs and cardinality-checks independent bridge/game/dedicated/profile/port resource ids without PowerShell argument-expression concatenation. Its console handler only sets an atomic cancellation flag; cleanup and artifact I/O stay on the main runner thread.
- `Infrastructure/Automation/CoopAutomationRuntimeBridge.cs`: default-off common runtime role-health publisher. It is pumped by the existing client controller and dedicated control tick, writes at most once per second unless state changes, and uses monotonic elapsed values.
- `scripts/CoopAutomationRunner.Core.ps1`: fixed dedicated bootstrap request construction, strict ready/status validation, type-safe UTC normalization, schema-v4 client launch validation, module-independent .NET SHA-256 hashing, required/optional PID-log descriptors, bounded process observation with validated `Win32_Process` path fallback, provisional/verified identity matching, PID-reuse-resistant exact cleanup, supplementary stdout/stderr capture, and pure in-memory descendant discovery from one bounded process snapshot.
- `scripts/Start-CoopBattleTestClient.ps1`: standalone validation plus aggregate-owned live launch through `-UseExistingRunContract`; inherits the runner token/root/hash/result policy, records immediate provisional PID/path/parent/window ownership, performs bounded exact identity observation, publishes the schema-v4 complete verified-process handoff, and exactly cleans pre-handoff failures.
- `scripts/Start-CoopFieldFixtureCapture.ps1`: validation-only standalone campaign preflight plus aggregate-owned live launch through `-UseExistingRunContract`; verifies the exact run/root/token fingerprint/hash/revision/game version, uses the installed-1.4.8 singleplayer module list, supplies fixture flags only through the child environment, removes the server-password variable, publishes provisional then exact campaign ownership, and cleans exact pre-handoff failures without UI automation or save mutation.
- `scripts/New-CoopSanitizedFieldFixture.ps1`: offline deterministic sanitizer pinned to the independently reviewed private field-payload SHA-256; refuses source overwrite, nonempty output, and implicit repository writes; replaces campaign-derived identities while preserving static replay content; emits only linked sanitized payload/metadata bytes that are identical in Windows PowerShell 5.1 and PowerShell 7.
- `Tests/Fixtures/Automation/field-current`: three-file shareable fixture authority containing the sanitized campaign roster, provenance metadata linking private and derivative hashes, and a separately authored critical-value oracle. No raw capture, save, or run log belongs in this directory.
- `Tests/contract-tests.manifest.json`: exact reviewed inventory consumed by the aggregate runner.

This layer remains non-authoritative for campaign and battle state. Milestone 2A stays non-runtime. Milestone 2B.1 adds process/role ownership, and Milestone 2B.2D adds the bounded vanilla `TeamDeathmatch` connectivity bootstrap after fail-closed result isolation. Run `m2b2d-live-feasibility-20260901-01` confirms the exact dedicated binary, control acknowledgements, UDP ownership, real client launch, and client loaded hash. Revision 12 corrects the aggregate handoff; `m2d-live-r2-01` confirms that handoff and automatic cleanup, then exposes the exact-assembly `NetworkMain` resolver defect. Corrected client `1C500...79250` was transactionally staged and live-confirmed by `m2d-live-r3-01`: the resolver works, but the stock client remains `Not Logged In` with `LobbyState=Idle`. Revision 13 implements and contract-verifies one bounded native `LobbyState.TryLogin()` attempt before server discovery plus terminal client evidence retention. Final client candidate `6DE0B8...E8551` is compile-only evidence and is not staged or runtime-verified. This layer does not load a campaign fixture, advance cooperative battle phases, publish campaign-consumable results, or claim L2/L3 evidence.

## Network files

### Transport components

- `Network/NetworkManager.cs`: TCP server/client role and message dispatch.
- `Network/TcpClientConnection.cs`: connection lifecycle and line receive loop.
- `Network/TcpLineProtocol.cs`: framing/encoding.
- `Network/CoopPreMissionTopologyNetworkComponent.cs`: global pre-mission Bannerlord component.

### Message groups

| File | Messages |
|---|---|
| `BattleStartMessage.cs` | Complete campaign battle domain model |
| `BattleStartMessageCodec.cs` | TCP/start payload codec |
| `CoopPreMissionTopologyNetworkMessages.cs` | Pre-open compact scene/mode/scenario contract |
| `CoopBattleSelectionNetworkMessages.cs` | Snapshot chunks/control, entry status, selection, phase, materialization, deployment, siege state |
| `CoopBattleAgentControlNetworkMessages.cs` | Controlled-agent/AI authority state |
| `CoopBattlePowerNetworkMessages.cs` | Side power/casualty comparison |
| `CoopSiegeAssaultOrderNetworkMessages.cs` | Siege assault formation/player orders |
| `CoopSiegeAmbushOrderNetworkMessages.cs` | Siege ambush orders |
| `CoopHideoutBossPhaseNetworkMessages.cs` | Hideout boss-stage state |
| `CoopHideoutAmbushNetworkMessages.cs` | Night ambush state/interactions |
| `CoopHeroCreationNetworkMessages.cs` | Hero draft/progress/result transport |
| `CoopCampaignMapPrototypeNetworkMessages.cs` | Prototype host state, settlement events, and quest-marker flags |
| `CoopCampaignMapReplicaNetworkMessages.cs` | Replica/catalog state including the host-visible marker projection |
| `HostGameState.cs` / codec | TCP host state |
| `NetworkMessagePrefixes.cs` | Stable message prefixes/constants |

`CoopBattleSelectionNetworkMessages.cs` is broad and should eventually be split by protocol concern, but that refactor would affect registration and wire compatibility and must not be incidental.

## Harmony patch map

### Mission open and topology

- `PreMissionTopologyContractPatch`
- `MissionStateOpenNewPatches`
- `BattleShellSuppressionPatch`
- `SiegeMissionGameTypeAliasPatch`
- `FinishedLoadingMissionReadyGatePatch`
- `LateJoinPeerBootstrapGatePatch` (code present; dangerous hooks disabled by startup flags)

### Exact spawn and network compatibility

- `ExactCampaignArmyBootstrapPatch`
- `ExactCampaignNetworkObjectBootstrapPatch`
- `ExactCampaignPreSpawnLoadoutPatch`
- `BattleMapSpawnHandoffPatch`
- `ClientChangeCultureCanonicalizationPatch`
- `ServerChangeCultureCanonicalizationPatch`
- `MultiplayerCharacterClassFallbackPatch`
- `MultiplayerHeroClassOverridePatch`
- `CampaignCombatProfileAgentStatsPatch`
- `CoopBattleDisplayNameConsumerPatch`

### Mission safety and control

- `CampaignlessConversationMissionSafetyPatch`
- `MissionItemUsageSetFlagsGuardPatch`
- `CoopNetworkSafeAgentBlowPatch`
- `CoopBotsControlledCountPatch`
- `CoopMissionLobbySpawnPeriodGuardPatch`
- `CommanderDeploymentMissionNetworkComponentPatch`
- `CoopBattleEscapeMenuAiControlPatch`

`CampaignlessConversationMissionSafetyPatch` targets the native `ConversationMission.OneToOneConversationAgent` getter. Its pure `CampaignlessConversationMissionSafetyContract` allows the original getter when both campaign and conversation manager exist and returns a null result only when that campaign state is unavailable.

### Siege-specific

- `CoopSiegeFormationMembershipSafetyPatch`
- `CoopSiegeLadderInteractionPatch`
- `ExactSiegeLadderMerlonVisualParityPatch`
- `ExactSiegeAmbushDeploymentControllerPatch`
- `ExactSiegeStageAdvancePatch`
- `OrderOfBattleSiegeProjectedCountsPatch`

### Lobby/UI/diagnostics

- game-mode override and lobby join patches;
- entry/HUD suppression patches;
- mission camera preview patch;
- optional character tableau/visual diagnostics patches;
- campaign map prototype scene-load patch.

When diagnosing a patch, inspect both `Apply` targeting and the startup flag/call site. A patch class can exist but be inactive, partially applied, or reapplied after assembly load.

## UI and assets

Primary views/view-models:

- `CoopMissionSelectionView` and `CoopSelectionShellViewModels`: side/unit/spawn/deployment shell;
- `CoopSiegeMachineDeploymentVM` and `CoopSiegeOrderOfBattleVM`: siege deployment and projected counts;
- `CoopBattlePowerScoreView`, `CoopBattleAiControlHintVM`: battle HUD additions;
- hero creator view/model/culture selection;
- day/night hideout objective, stealth, cinematic, conversation, and marker views;
- `CoopCampaignMapPrototypeMissionView`, party visual, overlays, and replica info. The mission view loads the native `Main_map` atmosphere data after `Scene.Read` and before optimization, collects meshes tagged `ticked_map_entity`, calls `MBMapScene.TickVisuals` while the map scene is render-ready, and clears those cached arrays during scene release. The atmosphere load is required because the visual tick reaches the native map color-grade/atmosphere path even though this is an isolated client scene layer. Publisher-to-client marker state uses explicit bit flags for available/active/tracked issue and story-quest semantics plus settlement tournament/production events; hidden campaign objects clear these fields instead of leaking markers.
- `CoopCampaignMapConversationView` and `CoopCampaignMapConversationVM`: synchronized display-only projection of the host's already formatted native map dialogue into the `SPConversation` movie. It restores mission-layer activation/focus on close and exposes no client choice transport.
- `CoopCampaignMapConversationTableauView`: owns the independent scene, up to three visual actors and an off-screen native TableauView texture. Its private passive Gauntlet layer/instance-local widget draws below the native conversation without global provider registration. Texture callback failures are consumed by the mission tick; resize invalidates readiness while preserving actors. cmt-b20 corrected gross image scaling/displacement using stock movie-style physical-pixel sizing. cmt-b22 restored visible scar/age detail with bounded immediate actor preparation; repeated readiness/retention/close passed. Animation, expressions and top HUD still differ; this is not visual 1:1 acceptance. See the canonical cmt-b22 native face-detail result in the resume report.

Gauntlet prefabs live in `Module/CoopSpectator/GUI/Prefabs/`. When changing a view-model property or command, search the matching XML binding. `CoopSelection.xml` and `CoopCommanderDeployment.xml` are primary selection/deployment assets.

Game data under `Module/CoopSpectator/ModuleData/` defines custom/mirror items, crafting pieces, multiplayer characters/classes, and strings. Exact equipment resolution may depend on both snapshot data and these stable XML registrations.

## Console commands

All commands use the `coop` prefix.

### Connectivity and launch

From `CoopConsoleCommands`:

- `coop.host`
- `coop.join`
- `coop.send`
- `coop.status`
- `coop.test_mp_launch`
- `coop.test_mp_server`
- `coop.test_mp_mission`
- `coop.test_mp_join`
- `coop.dedicated_start`
- `coop.dedicated_start_vpn`
- `coop.dedicated_open_tokens`
- `coop.test_mp_team`
- `coop.automation_join status`
- `coop.automation_join start <RunId>`
- `coop.automation_join cancel`

### Selection and spawn

From `CoopBattleSelectionConsoleCommands`:

- `coop.select_side`
- `coop.select_troop`
- `coop.spawn_now`
- `coop.force_respawnable`
- `coop.entry_status`
- `coop.side_options`
- `coop.select_side_index`
- `coop.troop_options`
- `coop.select_troop_index`
- side-scoped troop variants and `coop.entry_menu`.

### Phase and synthetic scenarios

- `coop.phase_status`
- `coop.start_battle`
- `coop.test_campaign_roster`
- `coop.test_hero_roster`
- `coop.test_battle`
- campaign map prototype console command(s) in `CoopCampaignMapPrototypeConsoleCommands`.

Console commands often write bridge requests that the authoritative mission consumes. They do not bypass mission authority.

## Contract tests

Each project is a small `net8.0` executable. Most link a narrow production contract directly and avoid loading Bannerlord runtime.

| Test project | Main contract area |
|---|---|
| `CampaignlessConversationMissionSafety.ContractTests` | Campaign-null conversation safety |
| `CoopBattlePower.ContractTests` | Power and multiplayer HUD calculations |
| `CoopBattleResultCampaignGuard.ContractTests` | Campaign/result ID validation and journaling |
| `CoopBattleResultReadCache.ContractTests` | Stable file-stamp cache semantics |
| `CoopBattleStartup.ContractTests` | Startup, exact hero/equipment, commander, mount, relief, and safety contracts |
| `CoopCampaignMapPrototype.ContractTests` | Map codec, chunk/catalog, bounds and scene rules |
| `CoopExactCampaignSiegePeerPerkSafety.ContractTests` | Hero-class and perk safety on dedicated siege |
| `CoopExactSiegeLadderMerlonVisualParity.ContractTests` | Remote ladder/merlon visibility decisions |
| `CoopHeroBattleProgression.ContractTests` | Weapon identity, XP, ammo/perk, fatal-event matching |
| `CoopHeroCreation.ContractTests` | Draft budgets, chunks, progress, and state machine |
| `CoopHideoutBossPhase.ContractTests` | Day/night hideout and boss/ambush rules |
| `CoopRemoteSiegeOcclusionSafety.ContractTests` | Remote client occlusion safety |
| `CoopShaderCacheModeSwitch.ContractTests` | Module PowerShell wrapper state changes |
| `CoopSiegeFormationMembershipSafety.ContractTests` | Stored formation membership/coordinates |
| `CoopSiegeLadderInteraction.ContractTests` | Ladder interaction mutation guards |
| `CoopSiegeSceneScriptRegistration.ContractTests` | SandBox managed scene-script registration |
| `CoopAutomationJoin.ContractTests` | Run/token/hash request validation, exact server selection, atomic status, and automation source-graph compilation |
| `CoopAutomationFixture.ContractTests` | Exact post-write byte retention, disabled-mode no-I/O, first-field admission, other-scenario rejection, immutable metadata, corruption/schema/path rejection, plus committed sanitized-fixture allowlist/privacy/hash/oracle/reference/composition/equipment checks and production-schema contract replay |
| `CoopAutomationProtocol.ContractTests` | Protocol compatibility, role/run/nonce ordering, lease/recovery, stable outcomes/reasons, known issues, atomic/append file faults, locks, and repeat reads |
| `CoopAutomationRuntime.ContractTests` | Strict runtime profile, loaded-role hash status, fail-closed result suppression, and exact live owned-host validation |
| `CoopCompileOnly.ContractTests` | Shared compile-only property, output routing, and deployment-target guards in both main project files |
| `NativeAftermath.ContractTests` | Casualty math, staged siege totals, hideout/final siege aftermath |

These tests prove contract logic only. They do not prove native mission loading, synchronized network ordering, or visual/runtime stability.

## Scripts

- `scripts/CoopDevLoop.ps1`: optional client/dedicated builds, process restart/launch, DLL timestamp checks, and log-marker scanning. With no action switches it builds both and checks logs. Its default `ProjectRoot` points to `C:\dev\projects\BannerlordCoopSpectator3`, not necessarily the active Codex worktree; pass `-ProjectRoot` explicitly.
- `scripts/CreateReleasePackage.ps1`: Release builds unless `-SkipBuild`, then recreates selected `dist/` artifacts. `-GitHubAssetsOnly` creates full GitHub client/host archives, `-NexusAssetsOnly` derives validated Nexus client/HostLite archives from existing GitHub archives, and `-ReleaseAssetsOnly` creates both sets. Nexus validation excludes BAT/PS1/PDB files and compares every retained payload entry and embedded release document by SHA-256. See [RELEASE_PACKAGING.md](RELEASE_PACKAGING.md) for the canonical layouts and workflow.
- `scripts/DllInventoryAudit.ps1`: scans installed client/dedicated trees and writes repository reports/CSV. Its historical conclusion may not match the current installed version.
- `scripts/Test-RepositoryHygiene.ps1`: read-only validation of repository-local Git EOL configuration, tracked text EOL state, mixed endings, and optional final working-tree cleanliness.
- `scripts/Invoke-CoopTest.ps1`: canonical fresh-root runner for L0/L1 checks, controlled connectivity, and private exact field-fixture capture. `Record` is capture-only and never implies mission/full-battle or L2/L3 success.
- `scripts/Start-CoopBattleTestClient.ps1`: validates Steam, exact installed client hash, and run identity; `-ValidateOnly` performs no launch/run-root write; an aggregate-owned live call writes only the selected temporary run root, provisionally owns and verifies the exact multiplayer executable, and cleans any failure before final handoff.
- `scripts/Start-CoopFieldFixtureCapture.ps1`: validates the exact singleplayer executable/module/version/arguments; `-ValidateOnly` performs no launch/run-root write; an aggregate-owned live call publishes immutable launch evidence and starts only the exact campaign process.
- `run_battle_test_client.bat`: historical standalone wrapper for the PowerShell client launcher. Current source is validation-only for this route; live automation must inherit exact server ownership from aggregate `Feasibility`. The password remains environment-only and is not a positional argument.

## High-risk hotspots

These are high risk because of responsibility breadth and native/runtime sensitivity, not merely file length:

1. `Mission/CoopMissionBehaviors.cs`: mission lifecycle, static state, spawn, phases, result.
2. `Patches/BattleMapSpawnHandoffPatch.cs`: native `CreateAgent` interception and replay.
3. `Mission/CoopMissionNetworkBridge.cs`: protocol, retries, acknowledgements, deployment, siege objects.
4. `Campaign/BattleDetector.cs`: capture plus all scenario routing and campaign aftermath.
5. `Infrastructure/ExactCampaignArmyBootstrap.cs`: native supplier/bootstrap boundary.
6. `Infrastructure/SiegeAssault/CoopSiegeMachineDeploymentController.cs`: native siege controller state, detachments, visibility, and AI-sensitive machines.
7. `GameMode/MissionMultiplayerCoop*Mode.cs`: exact native behavior-stack composition and ordering.
8. `SubModule.cs` and `DedicatedServer/SubModule.cs`: patch registration differences and late-load behavior.
9. `UI/CoopMissionSelectionView.cs`: broad player-facing selection/deployment state integration.

## Search recipes

Run from repository root:

```powershell
# Find a type or method without searching generated output.
rg -n --glob '*.cs' --glob '!bin/**' --glob '!obj/**' --glob '!dist/**' 'SymbolName'

# Find every runtime feature flag and its use sites.
rg -n --glob '*.cs' 'ExperimentalFeatures\.|COOPSPECTATOR_'

# Find a network message definition and all handlers/senders.
rg -n --glob '*.cs' 'CoopSomeMessage'

# Find mission-state cleanup for a subsystem.
rg -n --glob '*.cs' 'Reset|Clear|OnEndMission|OnRemoveBehavior' Mission Infrastructure Patches

# Find scenario routing rather than guessing from file names.
rg -n --glob '*.cs' 'ScenarioKind|SiegeSubtype|IsCampaignBattle|TryValidateActiveMission'

# Find console command names.
rg -n --glob '*.cs' 'CommandLineArgumentFunction' Commands
```

Prefer symbol-first reading over opening the largest files at line 1.
# Siege flight mathematical foundation (2026-10-23)

`Infrastructure/CoopCampaignMapSiegeFlightCodec.cs` defines fixed-size CSF1 detached
trajectory encoding (68 bytes). Shot identity/resource/session envelope integration
is pending; it does not independently authorize rendering or change current wire.

`Infrastructure/CoopCampaignMapSiegeFlightJournal.cs` retains bounded detached
observations between native ticks and publication (256 records, two-second delivery
window). Capture merges after join; invalid/hidden lifecycle clears it. This is
not a client playback delay, and transport/join-baseline admission is still absent.

Latest owner wiring: `Campaign/CoopCampaignMapSiegeProjectileCapture.Flights.cs`
attempts explicitly gated observation on construction and revalidates visible player
siege before/after parallel ticking. Publisher replacement/reset releases it.
Shared shot registry/clock binds observations to publication IDs. Detached values
are collected only; wire integration/full-flight retention remain pending. b223
isolated builds passed; installed b217 remains unchanged.

`Patches/CoopSiegeFlightObservationPatch.cs` supplies explicit gated manager-tick
and missile-call observation hooks. No startup caller is connected yet; both
map opt-in and COOPSPECTATOR_SIEGE_FLIGHT_OBSERVER=1 are required. Native
installation/lifecycle acceptance is pending; it does not publish trajectories.

`Campaign/CoopCampaignMapSiegeFlightObservation.cs` coordinates owner-admitted
managed shot references and detached worker observations. It requires shared shot
IDs, exact visual admission and a native parallel join; it is not wired at startup.
Substitute reader/coordinator/thread tests are in
`Tests/PortraitObservation.ContractTests/SiegeFlightObservationTests.cs`; this
existing Harmony test harness avoids loading game-native libraries in unit tests.

`Patches/CoopSiegeFlightCallSite.cs` prepares a uniquely shape-validated missile
observation insertion; it does not install a patch and awaits matcher tests and
owner/thread-safe observation wiring.

`Infrastructure/CoopCampaignMapSiegeFlight.cs` evaluates detached host-prepared
flight positions within strict campaign-time bounds. It does not select targets,
apply damage, capture native data or render effects. Tests reside in
`Tests/CoopCampaignMapPrototype.ContractTests/SiegeFlightTests.cs`.
`Infrastructure/CoopCampaignMapSiegeFlightBuffer.cs` is the bounded synchronized
value-only handoff foundation; tokens revoke stale worker submissions and invalid
batches are rejected atomically. It is not connected to native ticking yet.
Integration and native verification remain open; see
`SIEGE_PRESENTATION_PLAYBACK_2026-10-23.md`.
`Campaign/CoopCampaignMapSiegeFlightReader.cs` decodes the exact private native
trajectory value type into that detached model. It is not installed as a hook;
caller/shot identity, parallel admission and transport remain required.
## Post-battle prisoner and loot presentation (2026-10-23 candidate)

`Campaign/CoopCampaignAftermathCapture.cs` reads the exact active native Loot
PartyVM/SPInventoryVM. `Infrastructure/CoopCampaignAftermathPresentation.cs` owns
the detached CAF1 tree. CMN8/protocol61/bridge56 carries it through existing menu
transport. `UI/CoopCampaignAftermathVM.cs` provides read-only IViewModel bindings;
`CoopCampaignMapPrototypeMissionView.Aftermath.cs` owns the fullscreen layer and
stock-layout CoopCampaignPrisoners/CoopCampaignLoot prefabs. Publisher map anchors
and CoopCampaignMissionHost final-result gates permit presentation before host
aftermath screens close. See [implementation and gates](CAMPAIGN_MAP_AFTERMATH_SCREENS.md).
# 2026-10-25 independent aftermath hero integration

`CoopCampaignAftermathLoadoutTarget` handles canonical hero-only interests and
current disclosed hero membership. `CoopCampaignMapTooltipPayload` kind11 carries
the bounded visual tree with window/hero/mode correlation. Publisher dispatches to
`TryPrepareLootHeroVisual`; `CoopCampaignLootInspection.TickVisual` applies local
model/slots, revokes on host atomic snapshot/modal/selection changes. Source b266
protocol64/bridge59/tooltip5; native acceptance pending. See canonical aftermath doc.
