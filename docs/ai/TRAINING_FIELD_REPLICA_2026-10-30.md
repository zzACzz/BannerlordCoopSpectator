# Training Field replica visual — 2026-10-30

ACCEPTED r85/b24: the user confirms the3D Training Field now renders. Native log
also confirms creation on initial map and battle return. Read r85/ACCEPTANCE.md
for exact boundaries. No crash through repeat prisoners/loot opening, but hero
garment materials remain defective across MP map/aftermath. That separate issue
is tracked in NAVAL_MATERIALS_2026-10-30.md. b24 remains installed; r85 CLOSED,
all7 configs restored and temporary trace launcher returned to default-off.

## r84 result and resource correction

r84/b23 confirms `PrefabExists(tutorial_training_field)=false` in multiplayer;
the user sees no model. See r84/FAILED.md. StoryMode's prefab directory is not
loaded by the MP module list. A new file-only CoopMapVisualResourceAdapter
projects the one exact official root into CoopSpectatorMapResources/Prefabs,
with no gameplay submodules, catalogs, binaries, scenes or heavy asset copies.
It keeps all authored descendants and rotation, rejects scripts, verifies hashes,
reuses an intact adapter and preserves old adapters before source updates. Foreign
or edited adapters are rejected without overwrite. Both base/Naval content hashes
now include the official prefab source so mixed peers reject coherently.
The helper prepares this small module for MP and campaign-owned server launch;
both explicit module lists activate it. Original StoryMode files remain unchanged.
b24 both builds, all27 c17 contracts and15 compiled checks pass. Projection of
the actual installed official XML preserves all33 descendants and exact rotation;
source SHA2568F2EC79108321FBB6AE60A0D1D8C20CFB70832F0CE6C2A6911EF9AF41C836A9D
is unchanged. Evidence is r85/official-projection-check/validation.json. r85 stages
b24 for native acceptance; do not claim the model is visible yet.

## Exact installed source contract

The user confirms water is correct in b21 and only the Training Field's model is
missing. Read WAR_SAILS_VISUAL_PROVENANCE_2026-10-30.md for the fixed construction
failure and r83/ACCEPTANCE.md for the precise local native acceptance boundary.

Installed SettlementVisual.OnStartup resolves the settlement's entity name; if
absent, MapScene.AddNewEntityToMapScene instantiates a prefab with that same name
and changes only its position. CampaignVec2.AsVec3 gets the local scene height.
It does not synthesize a town wall level or replace the prefab's authored frame.

The official prefab `tutorial_training_field` is in
StoryMode/Prefabs/storymode_prefabs.xml. Its root rotation is0,0,-2.525, with33
descendants, meshes/decals and no script nodes. The StoryMode settlement data's
map_icon is not the prefab identity. Copying the map_icon alone would be wrong.

## b23 source behavior

- Match a visible settlement record and the current catalog's Special kind,
  exact official identity and level0 data. Catalog presence alone grants no
  visibility. No new network field, arbitrary-prefab admission or host authority
  change is introduced.
- On a ready owning replica scene, check the known prefab once. Resolve an
  existing exact named root or instantiate the official prefab into that scene,
  with script callbacks and physical simulation creation disabled for this visual.
- Keep the native prefab rotation, scale, children and child visibility. Change
  only position, then request its resources and register existing local picking.
- A separate level-free geometry binding controls only root visibility. Ordinary
  town/castle/village/hideout wall-level admission remains unchanged. Revocation
  hides the root; re-admission reuses it. Scene teardown detaches all managed
  bindings before the owning native scene is cleared.
- Missing resource/failed creation is reported once per scene with no unbounded
  native retry. Actual native prefab availability must be verified in r84.

This applies to base and Naval replicas and all battle-to-map returns because
they use the same mission view. Native creation is scene-sensitive; managed
tests cannot prove it renders or remains safe. The b21 partial-creation fix and
background-update behavior are retained. No water experiment is introduced.

## Validation and pending runtime gate

b23: both isolated builds pass with installed inventories unchanged. c16: all27
contract projects pass. The added custom-settlement tests cover host disclosure,
identity/kind mismatch, level0 isolation, child visibility and stale-scene teardown.
All15 compiled checks pass in artifacts/runtime/naval-b23-managed-checks.

Fresh r84: artifacts/runtime/naval-training-r84-20261030. Twelve coherent files
installed, seven new configuration preimages; campaign86364 launched. User was
asked to inspect the actual3D training model. Read the actual loaded identities,
creation logs and user observation before claiming native success. Prolonged
stability, base regression, DLC land battles/aftermath/save reload and two-PC
acceptance remain outstanding. No archive/commit/push has been performed.
