# Naval aftermath portrait failure — r84

Read `artifacts/runtime/naval-training-r84-20261030/FAILED.md` for exact dump,
stack, equipment evidence and cleanup. The source cause is the same incomplete
original `northern_round_shield` object seen in r82, reached through ordinary
UI thumbnail generation on the prisoners screen after a land battle. This is
a managed NullReferenceException, not the earlier native TickVisuals failure.

## Shared fix and scenario review

SafePortrait, aftermath EquipmentCode and item-image selection now use the
same preloaded-item resolver as map/dialogue visuals. Slot-aware codec methods
preserve host body, colors, formation, equipment order and modifiers, replacing
only unusable original IDs with the already loaded complete exact mirror.
Weapons need PrimaryWeapon, mounts need HorseComponent.Monster, armor/harness
need ArmorComponent; generic item images require a complete applicable component.
Unavailable full equipment yields an empty safe image instead of a partial one.

Prisoners, loot, settlement plunder, equipment inspection and conversation use
these shared presentation paths. The risk is not restricted to field battles;
siege/sally, village, hideout, naval and automatic aftermath can show the same
item. No battle-type exception or new gameplay/object registration is introduced.
The b21 constructor cleanup and normal background map updates remain in place.

## Validation boundary

The new managed checks exercise slot-preserving portrait rewriting and the
actual compiled aftermath VM with the saved-state-shaped broken shield plus
complete mirror. b24 client/dedicated isolated builds PASS, installed inventories
unchanged. c17 all27 contract projects PASS. All15 compiled checks PASS under
artifacts/runtime/naval-b24-managed-checks. The first extended test fixture had
an uninitialized Properties dictionary; corrected to use the real Node factory,
then passed. No production change was needed for that test fixture failure.

Resource-only Training Field projection is tracked separately in
TRAINING_FIELD_REPLICA_2026-10-30.md. The actual official subtree was also checked:
33 descendants and authored rotation preserved exactly, official source SHA256
8F2EC79108321FBB6AE60A0D1D8C20CFB70832F0CE6C2A6911EF9AF41C836A9D unchanged.

Fresh native run r85 is artifacts/runtime/naval-aftermath-r85-20261030. Twelve
coherent deployment files installed, seven configuration preimages retained.
Client SHA2563A9DDB4F1301A47B325FDA6792CB54094AFFA16C18BDA128A1230BC143B28A60;
dedicated B6EB1AEDCE1BA55FE76CEEC5987EB1C7606AD9BCF22D6D0D8A9BB50E712D4CE4.
Actual launch/loaded records determine current live state. Native acceptance,
longer soak, base regression and two-PC scenarios remain OPEN. No archive/commit/push.
