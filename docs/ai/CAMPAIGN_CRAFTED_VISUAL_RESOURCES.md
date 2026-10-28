# Crafted visual dependencies before battle — investigation2026-10-27

## Latest result and critical identity constraint

Native update r63/b287: user confirms the main-hero portrait now works before
citadel combat and village battle/return succeeds. Campaign75556/MP78148 loaded
client MVID91cd97a5-4615-4698-8baa-3de6a708614f; installed nine hashes match b287.
This is scoped user acceptance, not broad multi-peer/resource-history acceptance.
r62 was closed after reboot with all7 hashes matching; r63 owns seven fresh
configuration preimages. No success is claimed for the later ambush return bug;
see CAMPAIGN_MISSION_NATIVE_ENDED_CLOSE.md. User-supplied settlement top-overlay
comparison is a separate missing presentation feature.

### Detached portrait candidate b287 (native acceptance pending)

The audit found a narrower alternative to registered resource ledgers: the stock
smithy creates an owned **unregistered** ItemObject through
Crafting.CreatePreCraftedWeaponOnDeserialize. Crafting.GenerateItem and its nested
helper do not register objects. AgentVisuals builds weapon meshes from actual
equipment objects; CharacterSpawner obtains them through CalculateEquipment.
Decompilations: `artifacts/native/portrait-resource-identity-20261027`.

The candidate uses this path for encounter thumbnails only. It does not register
an item, mutate a registered item, repair an ID or inject these objects into battle
equipment. Protocol68/bridge63/CMN9 carries bounded exact definitions atomically
with their current disclosed encounter. Battle materialization remains unchanged.

- `CoopDetachedPortraitResources` owns resources for one client map-mission
  lifetime. Fresh current dependencies are prepared on mission ticks, at most4 per
  attempt. Retries are rate-limited; elapsed time never acknowledges readiness.
  Stale host/conversation gates suppress preparation. Missing dependencies hide
  only the portrait; complete readiness refreshes its cached unavailable image.
- `TryCreateDetachedPortraitWeapon` validates preloaded templates/pieces and
  generates a new owned ID-zero item. `CoopItemRegistryCounter` reads the managed
  ItemObject registration counter before/after; changed/unreadable post-state
  permanently disables this preview path. It never repairs counters or IDs.
- `CoopDetachedPortraitTextureProvider` accepts an opaque local token paired with
  exact character code. VM finalization revokes future lookup; in-flight requests
  retain their Equipment and resources. Tokens do not fragment the render cache.
- `CoopDetachedPortraitEquipmentPatch` substitutes an equipment clone only inside
  the exact own thumbnail request and CharacterCode reference. Nested stock
  requests mask scope; finalizers restore it on exceptions. Optional host pose
  override is also exact-request/cache scoped. All other callers pass through.
  This feature does not depend on the optional portrait-observer diagnostic flag.

These operations are runtime-sensitive even though metadata observation is managed
and read-only. Both cmp-b287 isolated builds and all26 cmp-c189 contracts pass;
native mesh/lifetime/rendering acceptance is pending. Installed b281 remains the
baseline until a fresh deployment record supersedes it. b282 is still prohibited.

Next native gates: cold first join before any battle, citadel header, actual battle
and aftermath, same-campaign rewind and another save with the server retained.
Field/village/siege/hideout headers, late join and repeated joins remain adjacent
regression gates. A counter mismatch fails closed; never repair native identity.
The registered-resource ledger alternative below applies only if registered item
creation is used, not merely to hold unregistered render-only Equipment objects.

User r61 accepts b281 prisoner model equipment, hero row portraits and independent
selection; only the main-hero encounter portrait before the citadel battle remains
in that tested sequence. r61 CLOSED, fresh product inventory empty, all7 config
hashes restored. Campaign19124/DS26992/MP75056 logs retained under its evidence dir.

**Do not install cmp-b282.** It was an isolated, never-installed client-only native
preparation prototype. The low-level audit below proved that approach could change
the normal ItemObject ID allocation order. Its native registry entry point and all
mission/UI hooks were removed. Source b283 retains only host dependency capture,
bounded data transport and pure readiness bookkeeping; native preparation is
UNWIRED. Both cmp-b283 builds and all26 cmp-c188 contract projects pass, installed
inventories unchanged. c187 failed a test definite-assignment compilation error;
c188 supersedes it. Installed b281 is unchanged; no next native test is ready.

Fresh decompilation under `artifacts/native/portrait-resource-identity-20261027`:

- `MBObjectManager.ObjectTypeRecord<T>.GetNewId()` increments `_objCount` per type;
  RegisterObject assigns that ID. Creation order is part of native object identity.
- `GameNetworkMessage.WriteObjectReferenceToPacket` writes `Id.InternalValue`.
  ReadObjectReferenceFromPacket looks up the received MBGUID in the local manager.
  `ModuleNetworkData.WriteItemReferenceToPacket` uses those exact object-reference
  methods for equipment. This is direct equipment serialization evidence, not an
  inference from unrelated character IDs.
- `ItemObject` is sealed. A separate portrait-only ItemObject subclass/type table
  is unavailable; do not repeat that proposed workaround or patch generic object
  lookup blindly.
- Native CharacterThumbnailCache calls CharacterSpawner with the full equipment;
  CharacterSpawner constructs AgentVisualsData from CalculateEquipment. Silently
  dropping unavailable weapon slots is not an accepted exact-rendering solution.

### Source b283 foundation

`CoopCampaignCraftedVisualWeapon` reproduces the existing battle FNV fingerprint
and validates identity, template, pieces, scales, sizes and duplicate roles. The
actual r60 mace pins compatibility to cw_b2834eadf9bb7620. Definitions must be bound
to crafted aliases in the current disclosed encounter portraits; unrelated asset
creation requests are rejected. Host capture owns a per-campaign/current-object
cache, rewrites only detached portrait codes and sends definitions atomically in
CMN9. Protocol68/bridge63, tooltip5/catalog10 and CAF3/CML2 otherwise unchanged.
The pure preparation tests cover explicit success, no implicit timed readiness,
idempotence, same-name/different-design save loads and incompatible key reuse.
They do NOT prove native ID parity or supply a server/client asset ledger.

### Registered-resource alternative: server/client native identity agreement

Preparation must preserve identical equipment IDs between dedicated and clients,
including late join after previous battles and repeated save changes. Merely
creating the current portrait's items on both roles is insufficient if histories
differ. Capture the actual native crafted registration history from the existing
battle registry too, not just resources first requested by a map portrait.

The next design should carry a server-owned ordered resource ledger and its actual
native IDs through the interface/pre-mission preparation boundary. Clients must
verify their existing prefix, prepare missing definitions in the admitted order,
and verify the resulting IDs before acknowledging readiness or allowing native
item packets. Never overwrite/reassign an existing item's ID or unregister objects
still used by actors/views to force agreement. The host's capture must not supply
authoritative native IDs. Keep asset-history metadata separate from current-world
entity disclosure/rendering; no prior-world characters, positions or ownership are
needed to synchronize item resources. A real conflicting history must fail closed
and be investigated against native game/object-manager connection lifetime rather
than silently aliasing the wrong equipment.

Required tests include cold first join, late join after another crafted battle,
same-campaign rewind, different save/campaign, rapid menu->battle while preparation
is incomplete, multiple peers, reconnect, invalid IDs/definitions and failed native
preparation. Existing fixed waits must not stand in for readiness acknowledgements.

## Open requirement

The replica must display the host-visible hero encounter portrait before a battle,
including immediately after loading another save. It must also display equipped
prisoner previews and hero row portraits. Do not strip weapons, replace equipment,
reuse an arbitrary old crafted_item_N mapping or substitute a different image.

## Evidence and current partial fixes

User r60 ran coherent b279. Prisoner screens now appear after siege and citadel;
the user confirms keep selection is restricted to its actual assault cohort, and
Loot previews now work. Two supplied PNGs show a missing encounter leader portrait,
a naked prisoner preview and silhouettes for Yorig, Iara and Halgard.

Retained roster proves all three heroes carry crafted weapons: main_hero uses
crafted_item_0, CharacterObject_1653 uses crafted_item_2, CharacterObject_1660 uses
crafted_item_4 and crafted_item_1. The MP log first registers their exact cs_crafted
items at09:53:44 during the citadel Battle OpenNew pre-classification. The preceding
encounter portrait therefore cannot rely on battle materialization having happened.
See `artifacts/runtime/map-save-reload-r60-20261027/evidence` for logs, roster and PNGs.

Strict `CoopConversationHudCodec.ResolveEquipmentCode` rejects the entire code if
one original item cannot resolve. `CoopConversationHudImageVM.SafePortrait` likewise
rejects its portrait. The aftermath VM initially resolved only static preloaded
mirrors. b279 fixed detached Loot mannequin/slot images using exact battle-derived
crafted aliases; the generic prisoner tree and encounter header were not covered.

Source b281 extends owned aftermath capture to project current crafted aliases
into its prepared equipment and CharacterImageTextureProvider codes. Its cache is
owned by the exact screen and cleared on reset/replacement; aliases are derived
from the current native item's design using the existing battle fingerprint code.
It does not construct/register items or modify live inventory. Independent prisoner
selection is also source-wired, using exact disclosed window/row admission and
owned detached HeroViewModel preparation. Native rendering remains pending.

## Required next implementation

Identity rewriting alone cannot fix a portrait before its crafted dependencies
exist in MP. Deliver bounded, canonical definitions for host-disclosed visual item
dependencies before rendering the corresponding menu/portrait. Reuse the battle
crafting fingerprint and existing validated/preloaded crafting-piece support.

1. Capture only dependencies referenced by a currently disclosed portrait/model,
   from current campaign item objects. A save-local original name is insufficient
   identity; include its exact design fingerprint and declared mirror identity.
2. Transport definitions atomically with their presentation/session/revision.
   Validate counts, sizes, piece IDs/scales, duplicate definitions, deterministic
   fingerprints and mirror IDs. No arbitrary XML, class names, paths or native
   pointers from peers. Preserve full fidelity and existing bounded envelopes.
3. **Runtime-sensitive step:** native item creation belongs at an admitted MP
   mission/main-thread preparation boundary, before its UI provider consumes the
   items. Never register resources from a property getter, hover callback or
   native rendering callback. Inspect the existing registry's crafting-support
   policy and canonical piece validation before exposing a new entry point.
4. Preserve existing fingerprinted objects through native teardown; never replace
   or unregister objects while old portraits/actors still reference them. Reusing
   an already verified exact fingerprint must be idempotent.
5. Gate/refresh portrait construction on completed resource preparation. A cached
   unavailable image created before dependencies arrive must not remain permanent.
   A missing/invalid resource must not render unrelated equipment from another save.
6. Cover cold client join before any battle, save switch, reconnect, existing
   resource reuse, native catalog unavailable, ordinary noncrafted heroes, field/
   village/siege/citadel/hideout headers, all prisoner lists and independent Loot
   outfit changes. Do not use success after a Battle preload to accept cold join.

This is an implementation plan, not delivered resource transport/preloading.
Installed b281 (r61) does not yet accept the prebattle portrait requirement.
