# Campaign to MP 1-to-1 Transfer Audit

Date: 2026-03-19

## Scope

This audit summarizes where Bannerlord stores campaign troop/hero/lord data in local game files and what that implies for eventual 1-to-1 transfer into the multiplayer runtime.

It is based on local installed game data, not web research.

## Key Conclusion

There is no single XML file that contains "the whole unit" for campaign entities.

Campaign identity is split across multiple layers:

- troop or template body/skills/equipment defaults
- hero records and family/faction ownership
- clan/faction metadata
- equipment set indirections
- skill template indirections
- runtime/save mutations

For 1-to-1 transfer we therefore need a composite extraction model, not a single `CharacterId -> MP troop` lookup.

## Data Sources

### 1. Regular campaign troops

Primary source:

- [spnpccharacters.xml](C:/Program%20Files%20(x86)/Steam/steamapps/common/Mount%20&%20Blade%20II%20Bannerlord/Modules/SandBoxCore/ModuleData/spnpccharacters.xml)

Example:

- `imperial_recruit` in [spnpccharacters.xml](C:/Program%20Files%20(x86)/Steam/steamapps/common/Mount%20&%20Blade%20II%20Bannerlord/Modules/SandBoxCore/ModuleData/spnpccharacters.xml)

What it contains:

- `id`
- `default_group`
- `level`
- `occupation`
- `culture`
- explicit combat skills
- upgrade targets
- one or more explicit `EquipmentRoster` blocks

Observed example:

- `imperial_recruit` is a low-tier empire infantry troop with direct item loadouts such as pitchfork or polearm, simple clothes, no horse.

Implication:

- regular troop entries are the easiest category for eventual 1-to-1 extraction because body, skills, culture and equipment defaults are all present in one place.

### 2. Bandits and looters

Primary source:

- [bandits.xml](C:/Program%20Files%20(x86)/Steam/steamapps/common/Mount%20&%20Blade%20II%20Bannerlord/Modules/SandBox/ModuleData/bandits.xml)

Example:

- `looter` in [bandits.xml](C:/Program%20Files%20(x86)/Steam/steamapps/common/Mount%20&%20Blade%20II%20Bannerlord/Modules/SandBox/ModuleData/bandits.xml)

What it contains:

- `occupation="Bandit"`
- `culture="Culture.looters"`
- direct skill values
- several `EquipmentRoster` variants
- upgrade target

Observed example:

- `looter` has multiple low-tier rosters, often sling or crude melee tools, with bandit clothing and no horse.

Implication:

- bandits are also relatively straightforward data-wise.
- their problem is not missing campaign data; their problem is MP runtime compatibility and lack of matching native MP classes.

### 3. Player hero and lords

Primary sources:

- [heroes.xml](C:/Program%20Files%20(x86)/Steam/steamapps/common/Mount%20&%20Blade%20II%20Bannerlord/Modules/SandBox/ModuleData/heroes.xml)
- [lords.xml](C:/Program%20Files%20(x86)/Steam/steamapps/common/Mount%20&%20Blade%20II%20Bannerlord/Modules/SandBox/ModuleData/lords.xml)
- [spclans.xml](C:/Program%20Files%20(x86)/Steam/steamapps/common/Mount%20&%20Blade%20II%20Bannerlord/Modules/SandBox/ModuleData/spclans.xml)

Observed split:

- `heroes.xml` contains hero records and relations:
  - `main_hero`
  - lord ids such as `lord_1_1`
  - spouse/father/mother/faction links
- `lords.xml` contains `NPCCharacter` definitions:
  - body properties
  - default skills
  - traits
  - default equipment
  - horse and horse harness
- `spclans.xml` contains faction/clan ownership and cultural context

Important observation:

- `main_hero` exists in both [heroes.xml](C:/Program%20Files%20(x86)/Steam/steamapps/common/Mount%20&%20Blade%20II%20Bannerlord/Modules/SandBox/ModuleData/heroes.xml) and [lords.xml](C:/Program%20Files%20(x86)/Steam/steamapps/common/Mount%20&%20Blade%20II%20Bannerlord/Modules/SandBox/ModuleData/lords.xml), but the XML values are only defaults.
- in a real campaign, the live player hero is runtime-mutated by the save:
  - name
  - appearance
  - culture in some cases
  - skills
  - traits
  - equipment
  - horse

Implication:

- for main hero and lords, XML is only a fallback/template layer.
- true 1-to-1 transfer must come from live campaign `Hero` state, not only from XML defaults.

### 4. Wanderers and companions

Primary sources:

- [spspecialcharacters.xml](C:/Program%20Files%20(x86)/Steam/steamapps/common/Mount%20&%20Blade%20II%20Bannerlord/Modules/SandBox/ModuleData/spspecialcharacters.xml)
- [sandbox_skill_sets.xml](C:/Program%20Files%20(x86)/Steam/steamapps/common/Mount%20&%20Blade%20II%20Bannerlord/Modules/SandBox/ModuleData/sandbox_skill_sets.xml)

Observed structure:

- wanderers are defined as `NPCCharacter` templates with:
  - `occupation="Wanderer"`
  - `is_template="true"`
  - culture
  - face template
  - skill template id such as `SkillSet.spc_wanderer_empire_0_skills`
  - equipment references via `EquipmentSet id="npc_companion_equipment_template_empire"`

Important observation:

- wanderer skill values are often indirect via skill template ids.
- wanderer equipment is often indirect via equipment set ids rather than explicit per-slot items in the same record.
- recruited companions in a live campaign are runtime hero instances derived from these templates.

Implication:

- companion transfer must resolve:
  - template identity
  - resolved skill template
  - resolved equipment set
  - live hero mutations after recruitment and leveling

### 5. Equipment and items

Primary sources:

- [weapons.xml](C:/Program%20Files%20(x86)/Steam/steamapps/common/Mount%20&%20Blade%20II%20Bannerlord/Modules/SandBoxCore/ModuleData/items/weapons.xml)
- [shields.xml](C:/Program%20Files%20(x86)/Steam/steamapps/common/Mount%20&%20Blade%20II%20Bannerlord/Modules/SandBoxCore/ModuleData/items/shields.xml)
- [horses_and_others.xml](C:/Program%20Files%20(x86)/Steam/steamapps/common/Mount%20&%20Blade%20II%20Bannerlord/Modules/SandBoxCore/ModuleData/items/horses_and_others.xml)
- [arm_armors.xml](C:/Program%20Files%20(x86)/Steam/steamapps/common/Mount%20&%20Blade%20II%20Bannerlord/Modules/SandBoxCore/ModuleData/items/arm_armors.xml)
- [body_armors.xml](C:/Program%20Files%20(x86)/Steam/steamapps/common/Mount%20&%20Blade%20II%20Bannerlord/Modules/SandBoxCore/ModuleData/items/body_armors.xml)
- [head_armors.xml](C:/Program%20Files%20(x86)/Steam/steamapps/common/Mount%20&%20Blade%20II%20Bannerlord/Modules/SandBoxCore/ModuleData/items/head_armors.xml)
- [leg_armors.xml](C:/Program%20Files%20(x86)/Steam/steamapps/common/Mount%20&%20Blade%20II%20Bannerlord/Modules/SandBoxCore/ModuleData/items/leg_armors.xml)
- [shoulder_armors.xml](C:/Program%20Files%20(x86)/Steam/steamapps/common/Mount%20&%20Blade%20II%20Bannerlord/Modules/SandBoxCore/ModuleData/items/shoulder_armors.xml)

Implication:

- exact 1-to-1 transfer of gear is feasible as data.
- the hard part is not discovering item ids; it is spawning and synchronizing them safely inside MP runtime.

### 6. Multiplayer runtime templates

Primary sources:

- [mpcharacters.xml](C:/Program%20Files%20(x86)/Steam/steamapps/common/Mount%20&%20Blade%20II%20Bannerlord/Modules/Native/ModuleData/mpcharacters.xml)
- [mpclassdivisions.xml](C:/Program%20Files%20(x86)/Steam/steamapps/common/Mount%20&%20Blade%20II%20Bannerlord/Modules/Native/ModuleData/mpclassdivisions.xml)
- [coopspectator_mpcharacters.xml](C:/dev/projects/BannerlordCoopSpectator3/Module/CoopSpectator/ModuleData/coopspectator_mpcharacters.xml)
- [coopspectator_mpclassdivisions.xml](C:/dev/projects/BannerlordCoopSpectator3/Module/CoopSpectator/ModuleData/coopspectator_mpclassdivisions.xml)

Observed structure:

- MP troops are defined as `NPCCharacter` records with MP-safe body/equipment
- MP class divisions add:
  - troop/hero pairing
  - idle animations
  - AI values
  - armor/movement summaries
  - perk/on-spawn behavior

Implication:

- even if we can build an exact campaign-equipment body, vanilla MP still expects a compatible class/runtime shell around it.
- that is why surrogates currently work more reliably than direct campaign bodies.

## What This Means for 1-to-1 Transfer

### We already have the right direction

The current `OriginalCharacterId -> SpawnTemplateId` split is the correct foundation.

Why:

- `OriginalCharacterId` preserves campaign identity
- `SpawnTemplateId` preserves MP runtime safety

This should be expanded, not replaced.

### True 1-to-1 transfer is a layered problem

We should think in layers:

1. Identity layer
- original troop/hero/wanderer/lord id
- source party
- side
- entry count
- hero flag

2. Appearance and equipment layer
- body properties
- exact equipment slots
- horse and harness
- civilian/combat distinction where relevant

3. Stat layer
- resolved combat skills
- traits
- perks/feat-like effects
- mounted/ranged/shield/thrown role metadata

4. Runtime spawn layer
- safe MP character/template/class shell
- equipment overrides where possible
- possession/spawn compatibility

### Category difficulty

From easiest to hardest:

1. Regular troops
- easiest because XML already contains most defaults directly

2. Bandits
- similar to troops, but they lack strong native MP analogues

3. Wanderers / companions
- harder because they use skill templates, equipment sets, and later runtime hero mutation

4. Lords and main hero
- hardest because their live campaign state diverges most from XML defaults

## Practical Constraints We Must Respect

### 1. XML defaults are not enough for live heroes

For:

- main hero
- companions
- lords

the save/runtime state matters more than static XML.

So any future 1-to-1 transfer for these categories must read live campaign objects first and use XML only as fallback/template context.

### 2. EquipmentSet indirection must be resolved

Companions and some special characters do not always list their final item slots directly.

That means the extractor must resolve:

- `EquipmentRoster`
- `EquipmentSet`
- possibly civilian/combat variants

instead of assuming one direct roster per character.

### 3. MP runtime safety still matters

Even if we can reconstruct exact campaign body/equipment/stats, vanilla MP still expects:

- valid spawn lifecycle
- compatible control ownership
- class/state expectations
- network-safe equipment handling

So the realistic path is:

- preserve exact campaign data first
- improve surrogate selection
- then start introducing exact equipment overrides
- only then evaluate how much of the body/stat identity can be made exact without destabilizing MP runtime

## Recommended Next Technical Steps

1. Extend snapshot/runtime contract with exact equipment slot data.
- not only role metadata
- store explicit combat equipment ids per entry where available

2. Add a resolved hero/live-character extraction path.
- for main hero, companions, lords:
  - read live campaign object state first
  - fallback to XML template only when needed

3. Introduce an equipment-override layer on top of safe MP templates.
- keep `SpawnTemplateId`
- override shield/weapon/armor/horse slots toward the original campaign loadout

4. Keep hero/lord transfer separate from regular troop transfer.
- troop fidelity can move faster
- hero fidelity needs save-aware extraction and stricter runtime validation

## Bottom Line

1-to-1 transfer is possible as a long-term architecture, but it will not come from a single XML lookup.

The reliable model is:

- exact campaign identity from live campaign state
- resolved skills/equipment from XML templates plus runtime mutations
- MP-safe spawn shell for runtime stability
- progressively more exact equipment/stat overrides as compatibility is proven

This is why the current surrogate path is not a dead end. It is the runtime-safety layer of a broader 1-to-1 transfer pipeline.
