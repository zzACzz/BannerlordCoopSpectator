using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    internal enum ExactWeaponSlotRole
    {
        Other = 0,
        Melee = 1,
        Polearm = 2,
        Shield = 3,
        Ranged = 4,
        Thrown = 5,
        Ammo = 6
    }

    internal sealed class ExactWeaponSlotResolution
    {
        public EquipmentIndex MainHandSlot { get; set; } = EquipmentIndex.None;
        public EquipmentIndex OffHandSlot { get; set; } = EquipmentIndex.None;
        public EquipmentIndex CompatibleAmmoSlot { get; set; } = EquipmentIndex.None;
        public int MainHandUsageIndex { get; set; } = -1;
        public ExactWeaponSlotRole MainHandRole { get; set; } = ExactWeaponSlotRole.Other;
        public bool MainHandNotUsableWithOneHand { get; set; }
        public bool MainHandRequiresAmmo { get; set; }
        public bool HasCompatibleAmmo { get; set; }
        public string MainHandItemId { get; set; }
        public string Summary { get; set; }
    }

    internal static class ExactWeaponSlotMaterializationPolicy
    {
        private static readonly EquipmentIndex[] WeaponSlots =
        {
            EquipmentIndex.Weapon0,
            EquipmentIndex.Weapon1,
            EquipmentIndex.Weapon2,
            EquipmentIndex.Weapon3
        };

        private sealed class SlotItem
        {
            public EquipmentIndex Slot { get; set; }
            public ItemObject Item { get; set; }
        }

        private sealed class MainHandCandidate
        {
            public EquipmentIndex Slot { get; set; }
            public ItemObject Item { get; set; }
            public WeaponComponentData Usage { get; set; }
            public int UsageIndex { get; set; }
            public ExactWeaponSlotRole Role { get; set; }
            public bool RequiresAmmo { get; set; }
            public bool HasCompatibleAmmo { get; set; }
            public EquipmentIndex CompatibleAmmoSlot { get; set; }
            public int Score { get; set; }
        }

        public static bool TryResolveInitialWield(
            Equipment equipment,
            RosterEntryState entryState,
            out ExactWeaponSlotResolution resolution)
        {
            return TryResolveInitialWield(CollectSlotItems(equipment), entryState, out resolution);
        }

        public static bool TryResolveInitialWield(
            MissionEquipment equipment,
            RosterEntryState entryState,
            out ExactWeaponSlotResolution resolution)
        {
            return TryResolveInitialWield(CollectSlotItems(equipment), entryState, out resolution);
        }

        public static bool TryApplyPreSpawnUsage(
            MissionEquipment missionEquipment,
            ExactWeaponSlotResolution resolution,
            out string summary)
        {
            summary = "usage-projection-unavailable";
            if (missionEquipment == null ||
                resolution == null)
            {
                return false;
            }

            if (!IsWeaponSlot(resolution.MainHandSlot))
            {
                summary = IsWeaponSlot(resolution.OffHandSlot)
                    ? "offhand-only-no-main-usage"
                    : "usage-projection-main-slot-missing";
                return IsWeaponSlot(resolution.OffHandSlot);
            }

            if (resolution.MainHandUsageIndex < 0)
                return false;

            MissionWeapon missionWeapon = missionEquipment[resolution.MainHandSlot];
            if (missionWeapon.IsEmpty ||
                missionWeapon.Item == null ||
                !string.Equals(
                    missionWeapon.Item.StringId,
                    resolution.MainHandItemId,
                    StringComparison.Ordinal))
            {
                summary = "usage-projection-main-slot-mismatch";
                return false;
            }

            if (resolution.MainHandUsageIndex >= missionWeapon.WeaponsCount)
            {
                summary = "usage-projection-index-out-of-range";
                return false;
            }

            missionEquipment.SetUsageIndexOfSlot(
                resolution.MainHandSlot,
                resolution.MainHandUsageIndex);
            summary =
                "main=" + resolution.MainHandSlot +
                ",usage=" + resolution.MainHandUsageIndex +
                ",item=" + (resolution.MainHandItemId ?? "none");
            return true;
        }

        public static bool TryWieldResolvedInitialSlots(
            Agent agent,
            RosterEntryState entryState,
            Agent.WeaponWieldActionType wieldActionType,
            out ExactWeaponSlotResolution resolution,
            out string failureReason)
        {
            resolution = null;
            failureReason = "agent-or-live-equipment-unavailable";
            if (agent == null || agent.IsMount || agent.Equipment == null)
                return false;

            if (GameNetwork.IsClientOrReplay)
            {
                failureReason = "client-live-wield-is-server-authoritative";
                return false;
            }

            if (!TryResolveInitialWield(agent.Equipment, entryState, out resolution))
            {
                failureReason = "initial-wield-resolution-failed";
                return false;
            }

            if (IsWeaponSlot(resolution.MainHandSlot))
            {
                MissionWeapon mainHandWeapon = agent.Equipment[resolution.MainHandSlot];
                if (mainHandWeapon.IsEmpty ||
                    mainHandWeapon.CurrentUsageIndex != resolution.MainHandUsageIndex)
                {
                    failureReason =
                        "pre-spawn-usage-not-materialized:expected=" + resolution.MainHandUsageIndex +
                        ",actual=" + (mainHandWeapon.IsEmpty ? -1 : mainHandWeapon.CurrentUsageIndex);
                    return false;
                }
            }

            if (IsWeaponSlot(resolution.OffHandSlot))
            {
                agent.TryToWieldWeaponInSlot(
                    resolution.OffHandSlot,
                    wieldActionType,
                    isWieldedOnSpawn: true);
            }

            if (IsWeaponSlot(resolution.MainHandSlot))
            {
                agent.TryToWieldWeaponInSlot(
                    resolution.MainHandSlot,
                    wieldActionType,
                    isWieldedOnSpawn: true);
            }

            failureReason = null;
            return true;
        }

        public static ExactWeaponSlotRole ResolveRole(ItemObject item)
        {
            if (item == null)
                return ExactWeaponSlotRole.Other;

            if (item.Weapons != null)
            {
                bool hasMelee = false;
                bool hasPolearm = false;
                bool hasRanged = false;
                bool hasThrown = false;
                for (int usageIndex = 0; usageIndex < item.Weapons.Count; usageIndex++)
                {
                    ExactWeaponSlotRole usageRole = ResolveUsageRole(item.Weapons[usageIndex]);
                    if (usageRole == ExactWeaponSlotRole.Shield)
                        return ExactWeaponSlotRole.Shield;
                    if (usageRole == ExactWeaponSlotRole.Ammo)
                        return ExactWeaponSlotRole.Ammo;
                    if (usageRole == ExactWeaponSlotRole.Thrown)
                        hasThrown = true;
                    else if (usageRole == ExactWeaponSlotRole.Ranged)
                        hasRanged = true;
                    else if (usageRole == ExactWeaponSlotRole.Polearm)
                        hasPolearm = true;
                    else if (usageRole == ExactWeaponSlotRole.Melee)
                        hasMelee = true;
                }

                if (hasRanged)
                    return ExactWeaponSlotRole.Ranged;
                if (hasThrown)
                    return ExactWeaponSlotRole.Thrown;
                if (hasPolearm)
                    return ExactWeaponSlotRole.Polearm;
                if (hasMelee)
                    return ExactWeaponSlotRole.Melee;
            }

            switch (item.ItemType)
            {
                case ItemObject.ItemTypeEnum.Shield:
                    return ExactWeaponSlotRole.Shield;
                case ItemObject.ItemTypeEnum.Arrows:
                case ItemObject.ItemTypeEnum.Bolts:
                case ItemObject.ItemTypeEnum.SlingStones:
                case ItemObject.ItemTypeEnum.Bullets:
                    return ExactWeaponSlotRole.Ammo;
                case ItemObject.ItemTypeEnum.Bow:
                case ItemObject.ItemTypeEnum.Crossbow:
                case ItemObject.ItemTypeEnum.Sling:
                case ItemObject.ItemTypeEnum.Pistol:
                case ItemObject.ItemTypeEnum.Musket:
                    return ExactWeaponSlotRole.Ranged;
                case ItemObject.ItemTypeEnum.Thrown:
                    return ExactWeaponSlotRole.Thrown;
                case ItemObject.ItemTypeEnum.Polearm:
                    return ExactWeaponSlotRole.Polearm;
                case ItemObject.ItemTypeEnum.OneHandedWeapon:
                case ItemObject.ItemTypeEnum.TwoHandedWeapon:
                    return ExactWeaponSlotRole.Melee;
                default:
                    return ExactWeaponSlotRole.Other;
            }
        }

        private static bool TryResolveInitialWield(
            List<SlotItem> slotItems,
            RosterEntryState entryState,
            out ExactWeaponSlotResolution resolution)
        {
            resolution = null;
            if (slotItems == null || slotItems.Count == 0)
                return false;

            bool preferRanged = ShouldPreferRanged(entryState);
            bool mounted = entryState?.IsMounted == true;
            MainHandCandidate bestCandidate = null;
            for (int slotItemIndex = 0; slotItemIndex < slotItems.Count; slotItemIndex++)
            {
                SlotItem slotItem = slotItems[slotItemIndex];
                if (slotItem?.Item?.Weapons == null ||
                    slotItem.Item.ItemFlags.HasAnyFlag(ItemFlags.HeldInOffHand))
                {
                    continue;
                }

                for (int usageIndex = 0; usageIndex < slotItem.Item.Weapons.Count; usageIndex++)
                {
                    WeaponComponentData usage = slotItem.Item.Weapons[usageIndex];
                    ExactWeaponSlotRole role = ResolveUsageRole(usage);
                    if (!IsMainHandRole(role))
                        continue;

                    bool requiresAmmo =
                        role == ExactWeaponSlotRole.Ranged &&
                        usage.AmmoClass != WeaponClass.Undefined;
                    EquipmentIndex compatibleAmmoSlot = requiresAmmo
                        ? FindCompatibleAmmoSlot(slotItems, usage.AmmoClass)
                        : EquipmentIndex.None;
                    bool hasCompatibleAmmo = !requiresAmmo || compatibleAmmoSlot != EquipmentIndex.None;
                    var candidate = new MainHandCandidate
                    {
                        Slot = slotItem.Slot,
                        Item = slotItem.Item,
                        Usage = usage,
                        UsageIndex = usageIndex,
                        Role = role,
                        RequiresAmmo = requiresAmmo,
                        HasCompatibleAmmo = hasCompatibleAmmo,
                        CompatibleAmmoSlot = compatibleAmmoSlot
                    };
                    candidate.Score = ScoreCandidate(candidate, preferRanged, mounted);
                    if (IsBetterCandidate(candidate, bestCandidate))
                        bestCandidate = candidate;
                }
            }

            if (bestCandidate == null)
            {
                EquipmentIndex shieldOnlySlot = FindShieldSlot(slotItems);
                if (!IsWeaponSlot(shieldOnlySlot))
                    return false;

                resolution = new ExactWeaponSlotResolution
                {
                    OffHandSlot = shieldOnlySlot,
                    HasCompatibleAmmo = true,
                    Summary =
                        "Main=None Off=" + shieldOnlySlot +
                        " Role=ShieldOnly PreferRanged=" + preferRanged +
                        " Mounted=" + mounted
                };
                return true;
            }

            EquipmentIndex offHandSlot = EquipmentIndex.None;
            bool notUsableWithOneHand =
                bestCandidate.Usage.WeaponFlags.HasAnyFlag(WeaponFlags.NotUsableWithOneHand);
            if (!notUsableWithOneHand)
                offHandSlot = FindShieldSlot(slotItems);

            resolution = new ExactWeaponSlotResolution
            {
                MainHandSlot = bestCandidate.Slot,
                OffHandSlot = offHandSlot,
                CompatibleAmmoSlot = bestCandidate.CompatibleAmmoSlot,
                MainHandUsageIndex = bestCandidate.UsageIndex,
                MainHandRole = bestCandidate.Role,
                MainHandNotUsableWithOneHand = notUsableWithOneHand,
                MainHandRequiresAmmo = bestCandidate.RequiresAmmo,
                HasCompatibleAmmo = bestCandidate.HasCompatibleAmmo,
                MainHandItemId = bestCandidate.Item.StringId
            };
            resolution.Summary = BuildSummary(resolution, preferRanged, mounted);
            return true;
        }

        private static List<SlotItem> CollectSlotItems(Equipment equipment)
        {
            var result = new List<SlotItem>(4);
            if (equipment == null)
                return result;

            for (int i = 0; i < WeaponSlots.Length; i++)
            {
                EquipmentIndex slot = WeaponSlots[i];
                ItemObject item = equipment[slot].Item;
                if (item != null)
                    result.Add(new SlotItem { Slot = slot, Item = item });
            }

            return result;
        }

        private static List<SlotItem> CollectSlotItems(MissionEquipment equipment)
        {
            var result = new List<SlotItem>(4);
            if (equipment == null)
                return result;

            for (int i = 0; i < WeaponSlots.Length; i++)
            {
                EquipmentIndex slot = WeaponSlots[i];
                MissionWeapon weapon = equipment[slot];
                if (!weapon.IsEmpty && weapon.Item != null)
                    result.Add(new SlotItem { Slot = slot, Item = weapon.Item });
            }

            return result;
        }

        private static ExactWeaponSlotRole ResolveUsageRole(WeaponComponentData usage)
        {
            if (usage == null)
                return ExactWeaponSlotRole.Other;
            if (usage.IsShield)
                return ExactWeaponSlotRole.Shield;
            if (usage.IsAmmo)
                return ExactWeaponSlotRole.Ammo;
            if (IsThrownUsage(usage))
                return ExactWeaponSlotRole.Thrown;
            if (usage.IsRangedWeapon)
                return ExactWeaponSlotRole.Ranged;
            if (usage.IsPolearm)
                return ExactWeaponSlotRole.Polearm;
            if (usage.IsMeleeWeapon || usage.IsOneHanded || usage.IsTwoHanded)
                return ExactWeaponSlotRole.Melee;
            return ExactWeaponSlotRole.Other;
        }

        private static bool IsThrownUsage(WeaponComponentData usage)
        {
            return usage != null &&
                   (usage.RelevantSkill == DefaultSkills.Throwing ||
                    usage.WeaponClass == WeaponClass.Javelin ||
                    usage.WeaponClass == WeaponClass.ThrowingAxe ||
                    usage.WeaponClass == WeaponClass.ThrowingKnife ||
                    usage.WeaponClass == WeaponClass.Stone ||
                    usage.WeaponClass == WeaponClass.SlingStone);
        }

        private static bool IsMainHandRole(ExactWeaponSlotRole role)
        {
            return role == ExactWeaponSlotRole.Melee ||
                   role == ExactWeaponSlotRole.Polearm ||
                   role == ExactWeaponSlotRole.Ranged ||
                   role == ExactWeaponSlotRole.Thrown;
        }

        private static int ScoreCandidate(MainHandCandidate candidate, bool preferRanged, bool mounted)
        {
            int score;
            if (preferRanged)
            {
                switch (candidate.Role)
                {
                    case ExactWeaponSlotRole.Ranged:
                        score = 500;
                        break;
                    case ExactWeaponSlotRole.Thrown:
                        score = 480;
                        break;
                    case ExactWeaponSlotRole.Polearm:
                        score = 320;
                        break;
                    default:
                        score = 300;
                        break;
                }
            }
            else if (mounted)
            {
                switch (candidate.Role)
                {
                    case ExactWeaponSlotRole.Polearm:
                        score = 500;
                        break;
                    case ExactWeaponSlotRole.Melee:
                        score = 480;
                        break;
                    case ExactWeaponSlotRole.Thrown:
                        score = 400;
                        break;
                    default:
                        score = 360;
                        break;
                }
            }
            else
            {
                switch (candidate.Role)
                {
                    case ExactWeaponSlotRole.Melee:
                        score = 500;
                        break;
                    case ExactWeaponSlotRole.Polearm:
                        score = 490;
                        break;
                    case ExactWeaponSlotRole.Thrown:
                        score = 420;
                        break;
                    default:
                        score = 380;
                        break;
                }
            }

            if (candidate.RequiresAmmo)
                score += candidate.HasCompatibleAmmo ? 40 : -240;
            return score;
        }

        private static bool IsBetterCandidate(MainHandCandidate candidate, MainHandCandidate current)
        {
            if (candidate == null)
                return false;
            if (current == null || candidate.Score != current.Score)
                return current == null || candidate.Score > current.Score;

            int itemComparison = string.Compare(
                candidate.Item?.StringId,
                current.Item?.StringId,
                StringComparison.Ordinal);
            if (itemComparison != 0)
                return itemComparison < 0;
            if (candidate.UsageIndex != current.UsageIndex)
                return candidate.UsageIndex < current.UsageIndex;
            return candidate.Slot < current.Slot;
        }

        private static EquipmentIndex FindCompatibleAmmoSlot(
            List<SlotItem> slotItems,
            WeaponClass ammoClass)
        {
            if (slotItems == null || ammoClass == WeaponClass.Undefined)
                return EquipmentIndex.None;

            EquipmentIndex bestSlot = EquipmentIndex.None;
            string bestItemId = null;
            for (int slotIndex = 0; slotIndex < slotItems.Count; slotIndex++)
            {
                SlotItem slotItem = slotItems[slotIndex];
                if (slotItem?.Item?.Weapons == null)
                    continue;

                bool matches = false;
                for (int usageIndex = 0; usageIndex < slotItem.Item.Weapons.Count; usageIndex++)
                {
                    WeaponComponentData usage = slotItem.Item.Weapons[usageIndex];
                    if (usage != null && usage.IsAmmo && usage.WeaponClass == ammoClass)
                    {
                        matches = true;
                        break;
                    }
                }

                if (!matches)
                    continue;

                string itemId = slotItem.Item.StringId ?? string.Empty;
                if (bestSlot == EquipmentIndex.None ||
                    string.Compare(itemId, bestItemId, StringComparison.Ordinal) < 0 ||
                    (string.Equals(itemId, bestItemId, StringComparison.Ordinal) && slotItem.Slot < bestSlot))
                {
                    bestSlot = slotItem.Slot;
                    bestItemId = itemId;
                }
            }

            return bestSlot;
        }

        private static EquipmentIndex FindShieldSlot(List<SlotItem> slotItems)
        {
            EquipmentIndex bestSlot = EquipmentIndex.None;
            string bestItemId = null;
            for (int slotIndex = 0; slotIndex < slotItems.Count; slotIndex++)
            {
                SlotItem slotItem = slotItems[slotIndex];
                if (slotItem?.Item == null || ResolveRole(slotItem.Item) != ExactWeaponSlotRole.Shield)
                    continue;

                string itemId = slotItem.Item.StringId ?? string.Empty;
                if (bestSlot == EquipmentIndex.None ||
                    string.Compare(itemId, bestItemId, StringComparison.Ordinal) < 0 ||
                    (string.Equals(itemId, bestItemId, StringComparison.Ordinal) && slotItem.Slot < bestSlot))
                {
                    bestSlot = slotItem.Slot;
                    bestItemId = itemId;
                }
            }

            return bestSlot;
        }

        private static bool ShouldPreferRanged(RosterEntryState entryState)
        {
            if (entryState?.IsRanged == true)
                return true;

            string formationClass = entryState?.CampaignFormationClass;
            return string.Equals(formationClass, "Ranged", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(formationClass, "HorseArcher", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(formationClass, "Horse Archer", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildSummary(
            ExactWeaponSlotResolution resolution,
            bool preferRanged,
            bool mounted)
        {
            return
                "Main=" + resolution.MainHandSlot +
                ":" + (resolution.MainHandItemId ?? "none") +
                ":usage=" + resolution.MainHandUsageIndex +
                ":role=" + resolution.MainHandRole +
                " Off=" + resolution.OffHandSlot +
                " Ammo=" + resolution.CompatibleAmmoSlot +
                " RequiresAmmo=" + resolution.MainHandRequiresAmmo +
                " HasCompatibleAmmo=" + resolution.HasCompatibleAmmo +
                " PreferRanged=" + preferRanged +
                " Mounted=" + mounted;
        }

        private static bool IsWeaponSlot(EquipmentIndex slot)
        {
            return slot >= EquipmentIndex.Weapon0 && slot <= EquipmentIndex.Weapon3;
        }
    }
}
