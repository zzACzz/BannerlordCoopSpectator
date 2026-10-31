# Naval hero garment materials — investigation2026-10-30

FINAL r91: user accepts corrected appearances and a normal field battle with
correct water/Training Field; actual scene battle_terrain_001.33 creates/0throws.
Approximately439s observed owned-map time (not10min); user background/long-soak
qualification remains open. r91 CLOSED/all7 configs restored/all3 graceful/normal
trace-off BAT restored; b26 retained. Fresh r92 runs base-only campaign75628 for
manual base regression, separately journaled. Current runtime comes from r92.

ACCEPTED MATERIAL FIX b26/r91: user confirms correct map, loot and prisoners
appearance. Exact loaded client/dedicated identities verified. Post-init external
read maps all8 source templates to alias4, matching package module4, unlike the
pre-init module0 bindings. Visible worn boot material and textures retained in
immutable013020866 capture. Read r91/ACCEPTANCE.md. A fresh approximately10min
movement/background/window-switch/recruitment/land-battle return test is pending;
base/scenario/save-reload/two-PC gates remain open. Current r91 roles stay running.

NEWEST b26 / r90: offline r89 metadata exposes stale source-template tileset
bindings. Native26B9A0 writes material+1D0 using the owning package's module index
through19C610, falling back to19CCE0's Native default. Worn boots have the same
pointer as Native body_male_a, while tunic/cloak copies have a different pointer.
The matching1D8=100000001 is two enable flags, not a tileset identity. Both the
stock copy path and same-texture public Material.SetTexture invoke26B9A0.
b26 therefore validates the official TileSets/ValidMaterials.xml primary imports,
activates the alias, and reassigns each existing template's own diffuse texture
once before first UI root. No recoloring, copying, offsets, hotpath work or
global renderer reset is shipped. Applies to all battle/portrait/map paths.
Both isolated builds, all27 c19 projects and all16 compiled checks PASS. Native
resource-only pre-login verification is in r90: first read precedes the01:21:04
initializer and confirms all8 source templates' asset package module4 but binding
module0. The log then reports497/497 refreshed; user closed before the post-init
read due missing Steam login. r90 CLOSED/all7 configs match/normal BAT restored.
Fresh r91 campaign92084 is running for manual client launch and actual visual
acceptance, which remains OPEN.
r89 CLOSED/all7 hashes match/normal trace-off BAT restored; b26 installation is
journaled separately. Do not request a full battle/soak as proof of an unverified
material change. Current runtime state is governed by r91 records.

CURRENT r89 captures actual visible boot/cloth mesh assignments and selective
metadata for offline analysis. Read r89/RESULT.md and its immutable005655302
capture. Boot materials and texture names are correct across all six LODs; the
remaining sampling/streaming/binding cause is NOT proven. No b26 correction has
been applied. User was told windows may close after the capture. Use elevated
CIM/path/start checks for lifecycle: sandbox-only Get-Process gave false absence.
Actual cleanup records govern current r89 state. Goal ACTIVE; no archive.

LATEST r87/b25 is PARTIAL: user screenshots confirm black/textured main garments
but boots/sword/spear remain flat gold in map and aftermath. The native active-set
correction is real but insufficient. Read r87/PARTIAL.md; do not claim full visual
acceptance or request another full manual battle/soak before a concrete next fix.
All three roles were found exited; actual cleanup records govern status.
Second PC is unavailable now; no preview archive has been created.

r88 now compares source material templates in official SP, SP alias and pre-login
MP. All eight relevant source templates match (including virtual binding field);
see r88/RESULT.md. A naive global rebind/cloning fix is NOT justified and has not
been implemented. A bounded external actor-material reader is prepared to inspect
the actual worn meshes/materials in a live replica; no new runtime diagnostic hook
or renderer state change is shipped. Source remains b25.

## Reproduction

r85/b24 fixes the previously crashing prisoners portrait path and makes Training
Field render (user acceptance). However the user reports gold/flat main-hero clothes
in prisoners, loot and the replica map. Campaign clothes remain correctly black,
matching the clan banner. Read r85/ACCEPTANCE.md and its retained screenshot/host
snapshots. This shared resource defect can affect all battle/aftermath types and
map/dialogue visuals; do not restrict a correction to prisoners.

The actual b24 codec decodes dark ArmorColor1=0xFF2F322B. A palette replacement
would not be evidence-based. Native logs show Naval package and ValidMaterials.xml
loading through the resource adapter. Official nord_casual_tunic/cloak use team
color; boots do not. Both textured material data and virtual texture pages matter.

## Lower-level texture selection

Actual Engine Utilities.LoadVirtualTextureTileset forwards to IUtil. The saved r84
delegate gives native RVA0x528AA0 (same installed native image), which resolves a
module name to index, inserts it into the manager's active set and calls the
tileset loader. Disassembly RVA0x19BE50 scans each module's TileSets/*.gts but
RVA0x19C240–0x19C27F skips modules absent from that active set. This separates
package/model registration from activation of texture pages. The native module
path lookup is0x19AEB0 and module-name lookup0x166340. Evidence under r85/evidence/
hero-materials; no production offsets or memory writes were introduced.

r84 engine dump lacks the required native heap pages. The attempted saved-state
reader fails with missing memory; it cannot prove the r84 active set. Do not
interpret that failure as an empty set or alter production code on that basis.

r86 is a fresh read-only inspection of b24 at main menu, with seven config
preimages and exact process identities. The external ClrMD reader opens process
memory without suspension, hooks, extra engine calls or writes; its only live
mode reads bounded module names/paths and the proven active-set layout. It uses
the exact installed native RVAs in a private artifact, never shipping code.
Campaign94680 loaded exact b24 MVID. Read result campaign-tileset-state.txt:
Native index0 and official NavalDLC index5 are active. Manual MP98368 loads the
resource alias at index4, but only Native index0 is active. The adapter's texture
pages are demonstrably absent from the active set, despite its package/material
registration. Both exact identities were captured live; no authentication or
server was needed. r86 CLOSED2026-10-30T00:06:39.5905844Z, both graceful exits,
all7 configs match, no remaining owned IDs. b24 installation retained unchanged.

## b25 correction and verification

Client SubModule.OnBeforeInitialModuleScreenSetAsRoot now calls
CoopNavalVisualResources.EnsureTextureTilesetLoaded. The pure
CoopNavalTextureAdmission gate admits only the active Naval resource alias in a
rendered non-campaign context, refuses dedicated/base/official-campaign/mixed
contexts and reentrant calls, and remembers success process-wide. It invokes the
stock Utilities.LoadVirtualTextureTileset(CoopSpectatorNavalResources) once before
affected render objects. Failure does not consume the gate. Host palettes and
official DLC files/licensing are unchanged; no scene tick/global allocation
setter or frame-loop reload was added. This shared activation applies to map,
dialogue, thumbnails, all battle types and aftermath, rather than recoloring a
single screen. It is production resource initialization justified by the measured
missing activation; private inspection code is not shipped.

b25 both isolated builds PASS (installed inventories unchanged), all16 compiled
checks PASS, including actual early-entry/public API wiring and16 role/profile
combinations without executing a native call. All27 aggregate c18 projects PASS.
Fresh r87 stages12 coherent b25 files/seven config preimages. Client SHA256
8FE1EF165F0211E66D04E638FEFA5BC3AC346A187362815A06E1CF23201D444D; dedicated
is byte-identical to b24 B6EB1AEDCE1BA55FE76CEEC5987EB1C7606AD9BCF22D6D0D8A9BB50E712D4CE4.
In r87 all three exact roles/MVIDs are verified. At00:11:36.5999828Z a bounded
external read of MP88724 proves Native0 AND the Naval alias4 active, unlike b24.
See r87/texture-admission.json and raw evidence/multiplayer-tileset-state.txt.
User garment rendering/soak acceptance is pending. Do not call the visual defect fixed
until both the new active index and the user's actual garment rendering agree.
