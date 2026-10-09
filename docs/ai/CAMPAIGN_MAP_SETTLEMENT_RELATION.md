# Settlement nameplate relation parity

## Evidence and scope (2026-09-28)

Closed native run map-movement-target-r1-20260928 (cmp-b48) completed a normal
field victory after the host declared war on Southern Empire. Paired screenshots
19-host-battle-return.png and 20-mp-battle-return.png show red enemy settlement
nameplates in the campaign and neutral backgrounds in the replica. User manually
confirmed post-return tooltips work; automated unsuccessful hovering is not a
second confirmed defect. See [run result](../../artifacts/runtime/map-movement-target-r1-20260928/RESULT.md).

Root cause: CoopCampaignMapPrototypeSettlementNameplateVM.Relation was fixed at
zero. This is a shared map-presentation omission, not a field-battle completion
failure. Initial joins, diplomacy changes, settlement ownership changes and
return from field, village, siege or hideout can all expose it. No battle adapter
or campaign diplomacy state should be changed to correct the display.

## Installed behavior

Installed SettlementNameplateVM.RefreshRelationStatus was inspected with ILSpy;
local evidence: artifacts/native/map-settlement-nameplate-20260926. All three
installed SandBox GUI settlement nameplate templates (Small/Medium/Large) bind
SettlementNameplateWidget.RelationType to @Relation. Preserve those stock assets.

Precedence, only if Settlement.OwnerClan exists:

1. FactionManager.IsAtWarAgainstFaction(settlement.MapFaction, Hero.MainHero.MapFaction): 2.
2. DiplomacyHelper.IsSameFactionAndNotEliminated: 1.
3. DiplomacyHelper.HasAllianceWithFaction: 3.
4. Otherwise 0. A settlement without an owner clan is also 0.

These values are presentation categories, not numerical hero relations. Do not
derive them from banner/faction colors or the local multiplayer player's faction.
The campaign host supplies the authority for all connected replica viewers.

## Source candidate

Protocol 30 / bridge 25 / binary catalog 8. Coherent campaign/client/dedicated
replacement is mandatory. The binary catalog layout is unchanged because this
field is in dynamic state, not static appearance.

- CoopCampaignMapSettlementRelationPolicy in the shared prototype contract:
  pure installed precedence, bounded integer 0..3.
- Campaign publisher captures live diplomacy on its existing published-visible
  settlement path. Copies SettlementRelation into dynamic state and explicitly
  clears it when disclosure is lost. No native mutation or new hot-path diagnostic.
- Full visible-state and dynamic clones, text codecs, validators, ordered dynamic
  equality/deltas and current-state projection retain the field. Missing/invalid
  field or old bridge schema is rejected. Nonzero dynamic relation requires a
  visible settlement-prefixed identity; nonsettlement projection is rejected.
- Both native message paths carry the two-bit field in matching read/write order.
  Existing two-entity dynamic batch limit is unchanged (four extra payload bits
  per full batch); compile/source tests do not establish actual packet capacity.
- Stock settlement VM updates its Relation property with change notification.
  Neutral is an explicit update, so peace does not retain the old red state.
  Existing entity admission/removal still owns nameplate visibility.

SettlementRelationTests covers the 16-case precedence table, every category and
16 category transitions, visible/dynamic clones and text roundtrips, delta-only
updates, current-state reconciliation, removal, invalid/missing values, old schema,
hidden/nonsettlement injection, and source wiring for native messages/UI updates.
Native message packing and stock widget rendering still require a live run.

## Validation status

cmp-b49 both isolated builds passed with unchanged installed module inventories
and no product launch. cmp-c50 passed 23/24 projects; the map suite's exhaustive
projection-source inventory rejected the new undeclared field before reaching
the new relation suite. The test inventory and equality oracle were completed;
the production transport was not changed to bypass this assertion.

Final cmp-b50 both isolated builds passed with unchanged installed inventories;
cmp-c51 passed all 24 projects, including 164 relation assertions. Exact source
subset and results are retained under artifacts/verification/cmp-b50 (and cmp-c51).
Client SHA-256 418A6C7BFAB7851CF2C2ACEA6013644DD50F2DAE200C89E77EECDB78F4110491;
dedicated F0BF38D616948E45E4B407740232BED4044D2D36219035DBC9009DF1483F4462.
Native run artifacts/runtime/map-relation-r1-20260928 is CLOSED. All three loaded
MVIDs/hashes verified; automatic map start, requested MP shortcut, Safe Mode No,
user Login and AC_COOP join observed. Bounded neutral-to-enemy village transition
passed without reconnect (04/05). Optional village battle declined. Read-only
Osarios dialogue (06/07), map return with red village/castle and restored host
body (08/09), camera detach/return (10/11) passed. Rejoin was not exercised because
physical Escape also failed; see CAMPAIGN_MAP_ESCAPE_INPUT.md. Other categories,
reverse transition and battle return remain unverified for cmp-b50.
All 24 owned identities absent; seven configs match, no new configs/products;
cmp-b50 retained with five module preimages, exact tooltip channel archived/removed.
See the run RESULT.md and independent postflight. No full native acceptance.

## Native acceptance still required

Compare campaign/replica ordinary town, castle and village backgrounds after
loading the coherent candidate. Exercise an enemy/neutral transition without
rejoining and check same-faction/allied categories when available. Verify new
join/rejoin, map-dialogue-map and battle-return do not restore cached relation.
Confirm hidden entities stay hidden and colors never change host authority.
Retain exact process/config/module preimages and independently verify cleanup.
This isolated correction does not close the broader replica Goal.
