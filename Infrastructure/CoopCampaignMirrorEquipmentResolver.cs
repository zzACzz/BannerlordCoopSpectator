using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Infrastructure
{
    public sealed class CoopCampaignMirrorEquipmentResult
    {
        public Equipment SpawnEquipment { get; set; }
        public MissionEquipment MissionEquipment { get; set; }
        public string Summary { get; set; }
        public bool HasDeferredCraftedWeapons { get; set; }
        public string DeferredCraftedWeaponSummary { get; set; }
    }

    public enum CoopCampaignMirrorCraftedWeaponPolicy
    {
        UseMirrors = 0,
        StripCreateTimeWeapons = 1
    }

    public static class CoopCampaignMirrorEquipmentResolver
    {
        public static bool TryBuild(
            RosterEntryState entryState,
            Banner banner,
            string source,
            out CoopCampaignMirrorEquipmentResult result,
            out string failureSummary,
            bool includeWeapons = true,
            bool includeArmorVisuals = true,
            bool includeCape = true,
            bool includeMountVisuals = true,
            CoopCampaignMirrorCraftedWeaponPolicy craftedWeaponPolicy = CoopCampaignMirrorCraftedWeaponPolicy.UseMirrors)
        {
            result = null;
            failureSummary = null;
            if (entryState == null)
            {
                failureSummary = "entry-null";
                return false;
            }

            MBObjectManager objectManager = Game.Current?.ObjectManager ?? MBObjectManager.Instance;
            if (objectManager == null)
            {
                failureSummary = "object-manager-null";
                return false;
            }

            var equipment = new Equipment();
            var appliedSlots = new List<string>();
            var missingSlots = new List<string>();
            var deferredCraftedWeaponSlots = new List<string>();

            if (includeWeapons)
            {
                ApplyMirrorSlot(objectManager, equipment, EquipmentIndex.Weapon0, "Item0", entryState.CombatItem0Id, entryState.CombatItem0CraftedWeaponKey, entryState.CombatItem0ModifierId, appliedSlots, missingSlots, deferredCraftedWeaponSlots, craftedWeaponPolicy);
                ApplyMirrorSlot(objectManager, equipment, EquipmentIndex.Weapon1, "Item1", entryState.CombatItem1Id, entryState.CombatItem1CraftedWeaponKey, entryState.CombatItem1ModifierId, appliedSlots, missingSlots, deferredCraftedWeaponSlots, craftedWeaponPolicy);
                ApplyMirrorSlot(objectManager, equipment, EquipmentIndex.Weapon2, "Item2", entryState.CombatItem2Id, entryState.CombatItem2CraftedWeaponKey, entryState.CombatItem2ModifierId, appliedSlots, missingSlots, deferredCraftedWeaponSlots, craftedWeaponPolicy);
                ApplyMirrorSlot(objectManager, equipment, EquipmentIndex.Weapon3, "Item3", entryState.CombatItem3Id, entryState.CombatItem3CraftedWeaponKey, entryState.CombatItem3ModifierId, appliedSlots, missingSlots, deferredCraftedWeaponSlots, craftedWeaponPolicy);
            }

            if (includeArmorVisuals)
            {
                ApplyMirrorSlot(objectManager, equipment, EquipmentIndex.Head, "Head", entryState.CombatHeadId, null, null, appliedSlots, missingSlots, deferredCraftedWeaponSlots, craftedWeaponPolicy);
                ApplyMirrorSlot(objectManager, equipment, EquipmentIndex.Body, "Body", entryState.CombatBodyId, null, null, appliedSlots, missingSlots, deferredCraftedWeaponSlots, craftedWeaponPolicy);
                ApplyMirrorSlot(objectManager, equipment, EquipmentIndex.Leg, "Leg", entryState.CombatLegId, null, null, appliedSlots, missingSlots, deferredCraftedWeaponSlots, craftedWeaponPolicy);
                ApplyMirrorSlot(objectManager, equipment, EquipmentIndex.Gloves, "Gloves", entryState.CombatGlovesId, null, null, appliedSlots, missingSlots, deferredCraftedWeaponSlots, craftedWeaponPolicy);
            }

            if (includeCape)
                ApplyMirrorSlot(objectManager, equipment, EquipmentIndex.Cape, "Cape", entryState.CombatCapeId, null, null, appliedSlots, missingSlots, deferredCraftedWeaponSlots, craftedWeaponPolicy);

            if (includeMountVisuals)
            {
                ApplyMirrorSlot(objectManager, equipment, EquipmentIndex.Horse, "Horse", entryState.CombatHorseId, null, null, appliedSlots, missingSlots, deferredCraftedWeaponSlots, craftedWeaponPolicy);
                ApplyMirrorSlot(objectManager, equipment, EquipmentIndex.HorseHarness, "HorseHarness", entryState.CombatHorseHarnessId, null, null, appliedSlots, missingSlots, deferredCraftedWeaponSlots, craftedWeaponPolicy);
            }

            if (missingSlots.Count > 0)
            {
                failureSummary =
                    "entry=" + (entryState.EntryId ?? "null") +
                    " missing=[" + string.Join("; ", missingSlots.OrderBy(slot => slot, StringComparer.Ordinal)) + "]";
                return false;
            }

            MissionEquipment missionEquipment;
            try
            {
                missionEquipment = new MissionEquipment(equipment, banner);
            }
            catch (Exception ex)
            {
                failureSummary =
                    "entry=" + (entryState.EntryId ?? "null") +
                    " mission-equipment-failed:" + ex.GetType().Name + ":" + ex.Message;
                return false;
            }

            result = new CoopCampaignMirrorEquipmentResult
            {
                SpawnEquipment = equipment,
                MissionEquipment = missionEquipment,
                Summary = BuildSummary(appliedSlots, deferredCraftedWeaponSlots),
                HasDeferredCraftedWeapons = deferredCraftedWeaponSlots.Count > 0,
                DeferredCraftedWeaponSummary = deferredCraftedWeaponSlots.Count > 0
                    ? string.Join(", ", deferredCraftedWeaponSlots.OrderBy(slot => slot, StringComparer.Ordinal))
                    : "(none)"
            };
            return true;
        }

        private static void ApplyMirrorSlot(
            MBObjectManager objectManager,
            Equipment equipment,
            EquipmentIndex slot,
            string slotLabel,
            string originalItemId,
            string craftedWeaponKey,
            string modifierId,
            List<string> appliedSlots,
            List<string> missingSlots,
            List<string> deferredCraftedWeaponSlots,
            CoopCampaignMirrorCraftedWeaponPolicy craftedWeaponPolicy)
        {
            if (equipment == null ||
                (string.IsNullOrWhiteSpace(originalItemId) && string.IsNullOrWhiteSpace(craftedWeaponKey)))
                return;

            bool useCraftedMirror = !string.IsNullOrWhiteSpace(craftedWeaponKey);
            string itemLabel = !string.IsNullOrWhiteSpace(originalItemId) ? originalItemId : craftedWeaponKey;
            string mirrorItemId;
            if (useCraftedMirror)
            {
                bool craftedMirrorResolved = ExactCampaignRuntimeItemRegistry.TryResolveCraftedMirrorItem(
                        craftedWeaponKey,
                        out mirrorItemId,
                        out string craftedMirrorFailure);
                if (ExactCampaignCraftingRuntimeSafetyContract.ShouldUseSafeWeaponSlotFallback(craftedMirrorResolved))
                {
                    deferredCraftedWeaponSlots?.Add(
                        slotLabel + "=" + itemLabel +
                        "/crafted:" + craftedWeaponKey +
                        "/safe-empty-slot-fallback:" + (craftedMirrorFailure ?? "unknown") +
                        (!string.IsNullOrWhiteSpace(modifierId) ? "/modifier:" + modifierId : string.Empty));
                    return;
                }

                if (craftedWeaponPolicy == CoopCampaignMirrorCraftedWeaponPolicy.StripCreateTimeWeapons)
                {
                    deferredCraftedWeaponSlots?.Add(
                        slotLabel + "=" + itemLabel + "->" + mirrorItemId +
                        "/crafted:" + craftedWeaponKey +
                        "/stripped-create-time" +
                        (!string.IsNullOrWhiteSpace(modifierId) ? "/modifier:" + modifierId : string.Empty));
                    return;
                }
            }
            else
            {
                if (!ExactCampaignRuntimeItemRegistry.TryResolvePreloadedMirrorItem(
                        originalItemId,
                        out mirrorItemId,
                        out string mirrorFailure))
                {
                    missingSlots?.Add(slotLabel + "=" + originalItemId + " static-mirror-failed:" + (mirrorFailure ?? "unknown"));
                    return;
                }
            }

            ItemObject item = TryResolveItem(objectManager, mirrorItemId);
            if (item == null)
            {
                missingSlots?.Add(slotLabel + "=" + itemLabel + " mirror-unresolved:" + mirrorItemId);
                return;
            }

            ItemModifier itemModifier = null;
            if (!ExactCampaignRuntimeItemRegistry.TryResolveItemModifier(modifierId, out itemModifier, out string modifierFailure))
            {
                missingSlots?.Add(slotLabel + "=" + itemLabel + " modifier-unresolved:" + (modifierFailure ?? "unknown"));
                return;
            }

            try
            {
                equipment[slot] = new EquipmentElement(item, itemModifier, null, false);
                appliedSlots?.Add(
                    slotLabel + "=" + itemLabel + "->" + mirrorItemId +
                    (useCraftedMirror ? "/crafted:" + craftedWeaponKey : string.Empty) +
                    (!string.IsNullOrWhiteSpace(modifierId) ? "/modifier:" + modifierId : string.Empty));
            }
            catch (Exception ex)
            {
                missingSlots?.Add(slotLabel + "=" + itemLabel + " apply-failed:" + ex.GetType().Name);
            }
        }

        private static string BuildSummary(List<string> appliedSlots, List<string> deferredCraftedWeaponSlots)
        {
            string appliedSummary = appliedSlots.Count > 0
                ? string.Join(", ", appliedSlots)
                : "(empty)";

            if (deferredCraftedWeaponSlots == null || deferredCraftedWeaponSlots.Count == 0)
                return appliedSummary;

            return appliedSummary +
                   " DeferredCraftedCreateTimeWeapons=[" +
                   string.Join(", ", deferredCraftedWeaponSlots.OrderBy(slot => slot, StringComparer.Ordinal)) +
                   "]";
        }

        private static ItemObject TryResolveItem(MBObjectManager objectManager, string itemId)
        {
            if (objectManager == null || string.IsNullOrWhiteSpace(itemId))
                return null;

            try
            {
                return objectManager.GetObject<ItemObject>(itemId);
            }
            catch
            {
                return null;
            }
        }
    }
}
