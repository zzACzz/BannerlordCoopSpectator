# Campaign map hover decal implementation boundary

## Current evidence (2026-09-28)

cmp-b39 live screenshots show a host village hover ring absent from the replica.
The renderer currently removes map_settlement_circle authoring helpers, correctly
matching SettlementVisual.OnStartup; those helpers are NOT runtime hover decals.
Do not restore the visible helper meshes as a substitute for the missing feature.

Installed SandBox.View.dll decompilation is retained locally under
artifacts/native/map-settlement-events-20260928 (MapScreen),
artifacts/native/settlement-presentation-20260927 (SettlementVisual), and
artifacts/native/map-party-banner-20260928 (MobilePartyVisual). These are the
implementation evidence below; decompiled game code is not published in Git.

## Stock behavior to preserve

MapScreen creates separate owned decal entities with materials map_circle_decal
for party/non-fortification hover and decal_city_circle_a for fortifications.
Creation uses a scene-owned GameEntity, Decal.CreateDecal, Material.GetFromResource,
Scene.AddDecalInstance and entity AddComponent. It is not an outline of the picked
mesh and must not add collision bodies or alter the battle scene.

TickCircles distinguishes the host's movement target from CurrentVisualOfTooltip.
The hover decal is hidden if the same object already has a target indicator.
Fortification hover takes CircleLocalFrame as a global frame. Other hover takes
GetVisualPosition plus CircleLocalFrame.origin, retaining CircleLocalFrame.rotation.
For land objects its height comes from terrain; the sea branch preserves visual
height and scales by 2.5. The circle material receives vector arguments
(0.166, 1, 0.83, 0). These are different from the animated movement-target rings.

SettlementVisual stores an authored map_settlement_circle global frame before
removing the helper. Without an authored frame it uses identity with local scale
1.75 for village/other, 5.75 for town, 2.75 for castle. MobilePartyVisual uses
scale 0.4625 for mounted visual leader/caravan, otherwise 0.3725. Prefer the host's
actual native CircleLocalFrame over re-deriving it from incomplete client state.

GetDecalColorForParty evaluates the target faction against Hero.MainHero.MapFaction
in this order: war 4292093218, same non-eliminated faction 4284183827, alliance
4279386828, otherwise 4291596077. These values differ from nameplate palettes.
Client BannerCode, PrimaryColor or NameplateColor cannot substitute for diplomacy.

## Source candidate and verification boundary

The post-c85c586 candidate now captures the native CircleLocalFrame, the
fortification selector and host-evaluated diplomatic color for disclosed objects.
`CoopCampaignMapHoverDecal` validates a detached 53-byte record (72 base64 chars):
shape, four admitted stock colors, twelve finite bounded floats and a nondegenerate
basis. Empty means unavailable presentation, not a guessed default.

Protocol 28 / host bridge 23 / binary catalog 8 carry the descriptor through
visible and catalog clones, text/binary codecs, projection and delta comparison.
Catalog deltas with a descriptor use the bounded chunked catalog path rather than
adding frame bytes to an already large direct message. Changes to diplomacy or
native frames refresh the catalog; dynamic admission still controls visibility.

`CoopCampaignMapPrototypeMissionView.HoverDecals.cs` owns two lazily created native
decals in the replica scene only. It updates from current admitted local hover
after party interpolation, selects stock materials and terrain/sea placement,
hides on input loss and clears before scene teardown. No battle-scene objects or
campaign native handles are borrowed. A first adapter exception disables this
presentation for that scene and logs the exception type once; no per-frame
diagnostics were added.

cmp-b41 failed the client compile on a C# 7.3 `ref`/`in` mismatch; corrected to the
installed SetGlobalFrame `in` signature. cmp-b42 built both roles with installed
inventories unchanged. All 24 cmp-c43 projects passed, including 94 hover checks.
Final cmp-b43 builds passed with unchanged installed inventories; cmp-c44 passed
all 24 projects, including 162 hover checks after adding hostile-wire and
independent color/frame-only delta tests. Evidence is retained under
artifacts/verification. These results do not prove native rendering or parity.
Native run map-hover-ring-r1-20260928 passed bounded village/town/main-party/enemy
hover, empty clear, second zoom, camera return and dialogue/map restoration checks.
Host/replica village and authored town rings visually agree. Small party indicators
are partly obscured by cursor/nameplates, so exact dimensions are not established.
No crash or hover-adapter failure was observed; pre-existing guarded intermission
exceptions remain. All 24 owned identities are absent, seven configurations match,
no new configuration files; cmp-b43 retained with five module preimages. Exact
tooltip channel archived and removed. See local artifacts/runtime/map-hover-ring-r1-20260928/RESULT.md.
This is NOT full native acceptance: no battle-return/rejoin, allied/sea/full shape
matrix or continuous stability measurement. Dialogue regression screenshots show
a missing MP speaker-banner icon; full 1:1 dialogue parity remains open.

The newer [movement-target source candidate](CAMPAIGN_MAP_MOVEMENT_TARGETS.md)
connects target capture/transport/rendering and same-target hover suppression at
29/24/8. It is not yet installed or native-accepted. The cmp-b43 observations above
remain hover-only evidence, not full campaign-map replica acceptance.

## Full implementation and acceptance requirements

1. Capture bounded, detached hover presentation from admitted host native visuals:
   frame, material/shape selector, host-evaluated diplomatic color, and target
   semantics needed to distinguish movement indicators from local hover.
   Missing native presentation must be explicit, not a fabricated yellow ring.
2. Transport and validate it through the authoritative snapshot/catalog path,
   including clone/equality, text and binary codecs, schema compatibility, catalog
   chunking, incremental updates and projection. Do not tunnel it through unrelated
   tooltip text, nameplate fields or banner colors. Static frame data and changing
   relationships need explicit update/lifetime ownership.
3. Create/reuse the two decals only in the replica's owned map scene. Select from
   current admitted hover, update frame after party interpolation, and hide on
   empty hover, removal, disclosure loss, dialogue, input/focus loss and finalization.
   Release before scene teardown; never reuse native handles across missions.
4. Preserve the distinction between read-only local exploration, host movement
   target display and hover. Attached-army redirect and prisoner-main-party rules
   remain separate prerequisites for exact target semantics.
5. Verify host/replica village, town/castle and mounted/unmounted party indicators,
   enemy/same-faction/allied/neutral colors, authored frames, two zooms, state changes,
   dialogue and map/battle/map/rejoin. Managed tests cannot prove decal rendering.

## Scope and risk

The map scene already enables decals. Native decal creation/material binding and
frame updates are runtime-sensitive and require a fresh coherent three-role build
with recoverable module/configuration preimages. Do not add per-frame diagnostics.
The adapter must remain separate from field, village, siege and hideout scene
rendering; all can still exercise its map-return lifecycle. This is not a marine
DLC implementation. Do not advertise full hover parity until actual native runs
cover the corresponding cases.
