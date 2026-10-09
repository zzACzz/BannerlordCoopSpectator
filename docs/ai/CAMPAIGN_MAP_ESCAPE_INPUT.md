# Replica map escape input

Latest bounded cmp-b57 run is CLOSED. After elapsed-deadline survival, native
Quit/rejoin and a field-victory map return, the agent opened native Options,
then opened, advanced and closed Phorys' conversation in the campaign. Options
remained present on host opening and after controlled closure; Cancel restored
the latest map without a stale dialogue. Wheel detach/zoom, native Lavenia tooltip
and host-camera return passed (evidence 27–33). No settings were changed.
This closes the previously unobserved close-after-options-open gate for one cycle,
not every modal combination. Escape opening still required user assistance.
All 23 recorded identities are absent, seven configs match, no new configs/products;
cmp-b57 and permitted test saves retained. See
[run result](../../artifacts/runtime/map-lifetime-r1-20260929/RESULT.md) and
[lifetime diagnosis](CAMPAIGN_MAP_LOBBY_LIFETIME.md).

The following cmp-b55/cmp-b51 entries are historical.

The newer cmp-b55 run FAILED stability: the dedicated crashed on native match
expiry after user-assisted rejoin. Bounded menu checks below remain valid, but
post-rejoin input acceptance did not finish. See
[dump diagnosis and map-lifetime candidate](CAMPAIGN_MAP_LOBBY_LIFETIME.md).

Latest native run `map-escape-r1-20260928` is CLOSED: cmp-b51 bounded menu
retention/wheel isolation, Quit cancellation/confirmation, actual agent-driven
rejoin, post-rejoin camera and main-party tooltip passed. Options FAILED and
dialogue/menu and full acceptance remain open. All 21 recorded identities are
absent; seven configs restored, no new configs/products, cmp-b51 retained with
module preimages. See [run result](../../artifacts/runtime/map-escape-r1-20260928/RESULT.md).
Earlier live-run wording below is historical.

## Native defect and source evidence (2026-09-28)

During cmp-b50 run `map-relation-r1-20260928`, both the automated Escape release
and the user's physical Escape failed to open the multiplayer menu after the
Osarios dialogue/map return. Rejoin acceptance was not completed. Do not classify
this observation solely as input-injection failure.

Installed assemblies inspected with ILSpy:
- `MissionScreen.HandleInputs` reads `ToggleEscapeMenu` from its SceneLayer.
- The replica info layer owns focus and blocks `InputUsageMask.All`; it previously
  neither registered the generic panel category nor forwarded the release.
- The mode already adds the stock multiplayer escape-menu behavior.
- `MissionGauntletEscapeMenuBase` opens a focused modal layer; its own tick closes
  it on `ToggleEscapeMenu`/`Exit`. Its native `OnEscape` waits for rendering.

## Correction and boundaries

The replica layer now registers `GenericPanelGameKeyCategory`. While render-ready
and admitted by `CanUseReplicaMapInput`, its release is queued through the pure
`CoopCampaignMapEscapeInputPolicy` and dispatched on the next owned frame to
`MissionScreen.OnEscape`. A frame boundary prevents the newly created native menu
from closing on the same release. Focus loss, dialogue ownership, or scene teardown
discards pending input. No global keyboard polling, menu clone, direct disconnect,
campaign mutation, transport change, or new hot-path diagnostics is introduced.

Shared-scenario audit: the map view exists only in the map prototype mode, not
field/village/siege/hideout battle stacks. Those stacks retain their existing menu
and selection handling. Returning from any battle creates the map input owner
again, so the map-side correction applies there too; this is not battle-regression
acceptance. Read-only dialogue currently owns a separate blocking layer; opening
the menu DURING dialogue is outside the installed cmp-b51 correction. The newer
source candidate described below extends this path, but remains native-unverified.

`EscapeInputTests` exercises 100 repeated release/dispatch/cancellation/reset
cycles plus source wiring checks. Managed results cannot establish native focus,
stock menu rendering or actual disconnect/rejoin success.

## Acceptance gates

cmp-b51 both isolated builds passed without installed mutation; cmp-c52 all 24
projects passed, including 1003 escape-input assertions. Results retained under
artifacts/verification/cmp-b51 and cmp-c52. Fresh run
`map-escape-r1-20260928` installed cmp-b51 with recoverable module/config preimages
and verified all three loaded identities. Initial map rendered after join;
automated Escape had no visible effect. The user confirmed that the first physical
Escape stopped Computer Use and the second opened the in-game menu. This is
bounded user-confirmed opening only, not full menu/rejoin acceptance. After the
user resumed control, the menu remained open and wheel input did not zoom the
underlying map (evidence 03/04). Clicking Options closed the menu but displayed no
options UI (evidence 05). Another automated Escape did not reopen it; physical
reopening was requested. The run remains live with cleanup pending.
Native correction NOT ACCEPTED. Verify physical/automated Escape, menu retention,
Escape close and Return button, options roundtrip, no map input through modal,
dialogue/map return and Quit confirmation followed by actual rejoin. Preserve
camera, tooltip and relation behavior; battle-return remains a separate gate.

## Dialogue/modal follow-up (source candidate, 2026-09-28)

Installed native `MissionEscapeMenuView.IsActive` is public. The stock menu owns
its own focused layer and removes it on close. `MissionScreen.OnEscape` dispatches
through mission views and does not reject conversation state; in multiplayer,
`SetConversationActive` itself returns without changing that flag.

`CoopCampaignMapConversationView` now has a separate instance of the same deferred
Escape policy, reads only its owned active/focused conversation layer, and forwards
to the stock dispatcher without changing host answers or closing the dialogue.
Close/replacement/finalize reset the pending release.

Native `ScreenBase.SetLayerCategoriesStateAndDeactivateOthers` deactivates every
unlisted active layer. Previously any incoming host dialogue revision could invoke
that operation while the stock menu was open, hiding the menu and stealing focus.
The candidate retains the latest queued host revision while the stock escape menu,
an inquiry, or another top screen owns presentation. It applies that revision after
the local modal ends. Network capture/authority continue unchanged. A host close
is likewise applied after the modal; no stale answer becomes actionable.

Shared-scenario audit: only the map prototype mode instantiates this dialogue view.
Field/village/siege and hideout cinematics are separate views and are not changed.
Map return/rejoin create fresh policy instances. No new runtime diagnostics or
native actor mutation is introduced. Native gates now also include opening and
closing Escape during dialogue, host text change/close while menu/options is open,
and restoration to the latest dialogue or map without losing modal focus.

Escape tests include separate map/dialogue owners and source ordering/reset
contracts (1013 assertions total); these do not simulate native focus or prove
dialogue/menu rendering. cmp-b54 both isolated builds passed with installed
inventories unchanged; cmp-c55 passed all 24 contract projects, including those
1013 assertions. Earlier cmp-b53/cmp-c54 failed before compilation because sandbox
access to NuGet.Config was denied, not due to source assertions. Evidence is
retained under artifacts/verification; native decompilation is retained under
artifacts/native/map-modal-input-20260928. Installed/live cmp-b51 is unchanged.
This source also includes the separately tested event-disclosure candidate from
cmp-b52; neither follow-up has been deployed or native-accepted yet.

## Native options subscriber correction (source candidate, 2026-09-28)

The cmp-b51 Options no-op has an installed-binary explanation:
`MissionGauntletMultiplayerEscapeMenu` closes its menu and invokes
`MissionOptionsComponent.OnAddOptionsUIHandler`; this raises `OnOptionsAdded` only
when subscribed. Native `MissionGauntletOptionsUIHandler` subscribes on screen
initialization and unsubscribes on finalization. The isolated map mode supplied
the component but omitted the view. Unlike stock MultiplayerBattle/TeamDeathmatch,
its custom mission name has no native view-factory registration.

The map client now explicitly adds `ViewCreator.CreateOptionsUIHandler()` under
the non-dedicated compilation guard. It uses native options, cancellation and
keybind handling, with no custom settings or disconnect implementation. Stock
options are a focused `MissionOptions` layer on the SAME MissionScreen. The
dialogue tick also checks that active, non-finalized layer before consuming a
pending host revision; top-screen and escape-menu guards alone are insufficient.
The check retains options/keybind focus without a new Gauntlet assembly reference.

Adjacent-scenario audit: CoopBattle field/village paths choose MultiplayerBattle
or MultiplayerTeamDeathmatch; exact siege retains the official mission opening
bridge; day/night hideouts use MultiplayerBattle. Their native view factory
already includes the options handler, so no global injection was added. The
separate custom hero-creator mode has the same omission but is outside this
increment; record it as a source finding, not a tested defect or a changed path.

`EscapeInputTests` adds source checks for exactly one client-only options view and
the same-screen layer guard (1015 assertions total). Native acceptance requires
an options open/cancel roundtrip, input isolation, host dialogue update/close
while options are open, and actual quit/rejoin. Existing cmp-b51 live evidence
cannot validate these changes. No diagnostics or native actor changes were added.

Verification: cmp-b55 compiled client and dedicated successfully with installed
inventories unchanged; cmp-c56 passed all 24 contract projects. Evidence retained
under artifacts/verification/cmp-b55 and cmp-c56.

Live follow-up: `map-options-r1-20260928` deployed cmp-b55 and verified all three
loaded identities. After user-assisted Escape opening, the agent clicked Options
and observed the native settings UI (evidence 04-native-options-open.png). This
confirms the previous Options no-op is resolved in this bounded observation.
The user opened a Phorys of Lavenia greeting in the host campaign while options
remained open in MP. The agent observed options retained, clicked Cancel without
settings changes, then observed the replicated greeting and three answer labels
(evidence 05/06, dialogue-under-options bridge copies). This passes a bounded
options open/cancel roundtrip with incoming conversation opening. Later text
revisions/host close under an existing dialogue modal and cmp-b55 quit/rejoin
remain unverified. Automated dialogue Escape had no visible effect; physical
verification was requested. Live run and cleanup are incomplete.

The user then opened Escape during the replicated conversation. Agent-observed
evidence 07 confirms stock menu over dialogue. Agent advanced the host greeting
to Phorys' introduction (08), observed the client menu still present with the
previous underlying greeting (09), then clicked Return and observed the latest
introduction/continue text (10). One text-revision/menu-retention roundtrip passed.
Host close under options, dialogue Escape-close and cmp-b55 quit/rejoin remain
unverified; this is not blanket modal/native or full map acceptance.

Further bounded close recovery: after another user-assisted menu opening and
control resumption, MP retained the old Phorys dialogue under its menu. Agent
opened Options and found the host already back at paused Lavenia settlement.
The close was not an observed agent action; timing relative to Options opening
is unknown. Host bridge revision 5 was inactive. Options remained visible (12),
and Cancel returned to map without stale dialogue (13). This passes pending-close
recovery across menu/options, but not a controlled close-after-options-open test.
Wheel zoom worked afterward.

cmp-b55 continuation: agent exercised Quit -> No -> Quit -> Yes and observed
native Home in the same client (evidence 14); dedicated recorded disconnect at
20:03:08. While the agent searched the server list, manual input intervened.
Subsequent observation and 20:07:41 connection logs prove user-assisted rejoin
with replica rendering and host-camera mode (15). The agent did not perform the
second Join. Existing intermission crash-guard swallowed ArgumentNullException
entries recur; do not describe the roundtrip as exception-free. Post-rejoin input
and tooltip regression, controlled host-close under already-open options, and
cleanup remain pending. Full map/native acceptance remains open.
