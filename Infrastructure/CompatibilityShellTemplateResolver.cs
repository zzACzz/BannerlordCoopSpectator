using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Infrastructure
{
    public static class CompatibilityShellTemplateResolver
    {
        public sealed class ShellProfile
        {
            public string TroopTemplateId { get; set; }

            public bool IsMounted { get; set; }

            public bool HasShield { get; set; }

            public bool HasThrown { get; set; }

            public RangedFamily Ranged { get; set; }

            public MeleeFamily Melee { get; set; }

            public bool IsRanged => Ranged != RangedFamily.None;
        }

        public enum RangedFamily
        {
            None = 0,
            Bow,
            Crossbow,
            Thrown
        }

        public enum MeleeFamily
        {
            None = 0,
            OneHanded,
            Polearm,
            TwoHanded
        }

        private static readonly object Sync = new object();
        private static readonly HashSet<string> LoggedFallbackProfileKeys = new HashSet<string>(StringComparer.Ordinal);

        public static bool IsCompatibilityShellTemplateId(string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                return false;

            return candidate.StartsWith("mp_coop_foot_", StringComparison.Ordinal) ||
                   candidate.StartsWith("mp_coop_mounted_", StringComparison.Ordinal);
        }

        public static string TryConvertTroopTemplateToHeroTemplateId(string troopTemplateId)
        {
            if (string.IsNullOrWhiteSpace(troopTemplateId))
                return null;

            if (!troopTemplateId.EndsWith("_troop", StringComparison.Ordinal))
                return troopTemplateId;

            return troopTemplateId.Substring(0, troopTemplateId.Length - "_troop".Length) + "_hero";
        }

        public static string ResolveTroopTemplateId(BasicCharacterObject character)
        {
            return ResolveProfile(character)?.TroopTemplateId;
        }

        public static ShellProfile ResolveProfile(BasicCharacterObject character)
        {
            if (character == null)
                return null;

            bool mountedHint = ResolveMountedHint(character);
            foreach (Equipment equipment in EnumerateBattleEquipments(character))
            {
                ShellProfile profile = ResolveProfile(equipment, mountedHint);
                if (profile != null)
                    return profile;
            }

            return null;
        }

        public static ShellProfile ResolveProfile(
            string combatItem0Id,
            string combatItem1Id,
            string combatItem2Id,
            string combatItem3Id,
            string combatHorseId,
            bool mountedHint = false)
        {
            return ResolveProfile(
                new[]
                {
                    combatItem0Id,
                    combatItem1Id,
                    combatItem2Id,
                    combatItem3Id
                },
                combatHorseId,
                mountedHint);
        }

        public static ShellProfile ResolveProfile(IEnumerable<string> weaponItemIds, string horseItemId, bool mountedHint = false)
        {
            List<ItemObject> items = (weaponItemIds ?? Array.Empty<string>())
                .Select(TryResolveItem)
                .Where(item => item != null)
                .ToList();

            return ResolveProfile(items, !string.IsNullOrWhiteSpace(NormalizeObjectId(horseItemId)) || mountedHint);
        }

        private static IEnumerable<Equipment> EnumerateBattleEquipments(BasicCharacterObject character)
        {
            if (character == null)
                yield break;

            var yielded = new HashSet<Equipment>();

            if (character.FirstBattleEquipment != null && yielded.Add(character.FirstBattleEquipment))
                yield return character.FirstBattleEquipment;

            if (character.RandomBattleEquipment != null && yielded.Add(character.RandomBattleEquipment))
                yield return character.RandomBattleEquipment;

            IEnumerable<Equipment> battleEquipments = null;
            try
            {
                battleEquipments = character.BattleEquipments;
            }
            catch
            {
                battleEquipments = null;
            }

            if (battleEquipments == null)
                yield break;

            foreach (Equipment equipment in battleEquipments)
            {
                if (equipment != null && yielded.Add(equipment))
                    yield return equipment;
            }
        }

        private static bool ResolveMountedHint(BasicCharacterObject character)
        {
            if (character == null)
                return false;

            if (TryGetBoolProperty(character, "IsMounted"))
                return true;

            FormationClass formationClass = character.DefaultFormationClass;
            return formationClass == FormationClass.Cavalry || formationClass == FormationClass.HorseArcher;
        }

        private static ShellProfile ResolveProfile(Equipment equipment, bool mountedHint)
        {
            if (equipment == null)
                return null;

            var items = new List<ItemObject>();
            bool hasShield = false;
            bool mounted = mountedHint || equipment[EquipmentIndex.Horse].Item != null;

            for (EquipmentIndex slot = EquipmentIndex.Weapon0; slot <= EquipmentIndex.Weapon3; slot++)
            {
                ItemObject item = equipment[slot].Item;
                if (item == null)
                    continue;

                items.Add(item);
                if (IsShield(item))
                    hasShield = true;
            }

            return ResolveProfile(items, mounted, hasShield);
        }

        private static ShellProfile ResolveProfile(List<ItemObject> items, bool mounted)
        {
            bool hasShield = items.Any(IsShield);
            return ResolveProfile(items, mounted, hasShield);
        }

        private static ShellProfile ResolveProfile(List<ItemObject> items, bool mounted, bool hasShield)
        {
            if (items == null)
                return null;

            RangedFamily ranged = ResolveRangedFamily(items);
            MeleeFamily melee = ResolveMeleeFamily(items);

            string troopTemplateId = ResolveTroopTemplateId(mounted, ranged, melee, hasShield);
            if (string.IsNullOrWhiteSpace(troopTemplateId))
                return null;

            return new ShellProfile
            {
                TroopTemplateId = troopTemplateId,
                IsMounted = mounted,
                HasShield = hasShield,
                HasThrown = ranged == RangedFamily.Thrown,
                Ranged = ranged,
                Melee = melee
            };
        }

        private static string ResolveTroopTemplateId(bool mounted, RangedFamily ranged, MeleeFamily melee, bool hasShield)
        {
            if (mounted)
            {
                switch (ranged)
                {
                    case RangedFamily.Bow:
                        if (melee == MeleeFamily.Polearm && !hasShield)
                            return "mp_coop_mounted_bow_polearm_no_shield_troop";
                        if (hasShield)
                            return "mp_coop_mounted_bow_1h_shield_troop";
                        return "mp_coop_mounted_bow_1h_no_shield_troop";

                    case RangedFamily.Thrown:
                        if (hasShield)
                            return "mp_coop_mounted_thrown_polearm_shield_troop";
                        return "mp_coop_mounted_thrown_polearm_no_shield_troop";

                    case RangedFamily.None:
                        if (melee == MeleeFamily.TwoHanded)
                        {
                            return hasShield
                                ? "mp_coop_mounted_melee_2h_shield_troop"
                                : "mp_coop_mounted_melee_2h_no_shield_troop";
                        }

                        if (hasShield)
                            return "mp_coop_mounted_melee_polearm_shield_troop";
                        return "mp_coop_mounted_melee_polearm_no_shield_troop";

                    default:
                        return LogAndReturnFallback(
                            "mounted|" + ranged + "|" + melee + "|shield=" + hasShield,
                            hasShield
                                ? "mp_coop_mounted_melee_polearm_shield_troop"
                                : "mp_coop_mounted_melee_polearm_no_shield_troop");
                }
            }

            switch (ranged)
            {
                case RangedFamily.Bow:
                    switch (melee)
                    {
                        case MeleeFamily.Polearm:
                            return "mp_coop_foot_bow_polearm_no_shield_troop";
                        case MeleeFamily.TwoHanded:
                            return "mp_coop_foot_bow_2h_no_shield_troop";
                        case MeleeFamily.None:
                            if (hasShield)
                            {
                                return LogAndReturnFallback(
                                    "foot|bow|none|shield=true",
                                    "mp_coop_foot_bow_1h_shield_troop");
                            }

                            return "mp_coop_foot_bow_unarmed_no_shield_troop";
                        default:
                            return hasShield
                                ? "mp_coop_foot_bow_1h_shield_troop"
                                : "mp_coop_foot_bow_1h_no_shield_troop";
                    }

                case RangedFamily.Crossbow:
                    if (hasShield)
                    {
                        if (melee == MeleeFamily.Polearm)
                            return "mp_coop_foot_crossbow_polearm_shield_troop";

                        return "mp_coop_foot_crossbow_1h_shield_troop";
                    }

                    if (melee == MeleeFamily.None)
                        return "mp_coop_foot_crossbow_unarmed_no_shield_troop";

                    return "mp_coop_foot_crossbow_1h_no_shield_troop";

                case RangedFamily.Thrown:
                    if (hasShield)
                    {
                        if (melee == MeleeFamily.Polearm)
                            return "mp_coop_foot_thrown_polearm_shield_troop";
                        if (melee == MeleeFamily.TwoHanded)
                            return "mp_coop_foot_thrown_2h_shield_troop";
                        return "mp_coop_foot_thrown_1h_shield_troop";
                    }

                    if (melee == MeleeFamily.Polearm)
                        return "mp_coop_foot_thrown_polearm_no_shield_troop";
                    if (melee == MeleeFamily.TwoHanded)
                        return "mp_coop_foot_thrown_2h_no_shield_troop";
                    return "mp_coop_foot_thrown_1h_no_shield_troop";

                case RangedFamily.None:
                    switch (melee)
                    {
                        case MeleeFamily.Polearm:
                            return hasShield
                                ? "mp_coop_foot_melee_polearm_shield_troop"
                                : "mp_coop_foot_melee_polearm_no_shield_troop";
                        case MeleeFamily.TwoHanded:
                            return "mp_coop_foot_melee_2h_no_shield_troop";
                        case MeleeFamily.OneHanded:
                            return hasShield
                                ? "mp_coop_foot_melee_1h_shield_troop"
                                : "mp_coop_foot_melee_1h_no_shield_troop";
                        case MeleeFamily.None:
                            return "mp_coop_foot_melee_unarmed_no_shield_troop";
                        default:
                            return LogAndReturnFallback(
                                "foot|melee|" + melee + "|shield=" + hasShield,
                                hasShield
                                    ? "mp_coop_foot_melee_1h_shield_troop"
                                    : "mp_coop_foot_melee_1h_no_shield_troop");
                    }

                default:
                    return LogAndReturnFallback(
                        "foot|unknown|" + melee + "|shield=" + hasShield,
                        hasShield
                            ? "mp_coop_foot_melee_1h_shield_troop"
                            : "mp_coop_foot_melee_1h_no_shield_troop");
            }
        }

        private static string LogAndReturnFallback(string key, string fallbackTroopTemplateId)
        {
            if (string.IsNullOrWhiteSpace(fallbackTroopTemplateId))
                return fallbackTroopTemplateId;

            bool shouldLog;
            lock (Sync)
            {
                shouldLog = LoggedFallbackProfileKeys.Add(key ?? string.Empty);
            }

            if (shouldLog)
            {
                ModLogger.Info(
                    "CompatibilityShellTemplateResolver: fell back to nearest compatibility shell. " +
                    "Profile=" + (key ?? "null") +
                    " FallbackTemplate=" + fallbackTroopTemplateId + ".");
            }

            return fallbackTroopTemplateId;
        }

        private static RangedFamily ResolveRangedFamily(IEnumerable<ItemObject> items)
        {
            bool sawBow = false;
            bool sawCrossbow = false;
            bool sawThrown = false;

            foreach (ItemObject item in items)
            {
                if (item == null)
                    continue;

                switch (ResolveItemRole(item))
                {
                    case ItemRole.Crossbow:
                        sawCrossbow = true;
                        break;
                    case ItemRole.Bow:
                        sawBow = true;
                        break;
                    case ItemRole.Thrown:
                        sawThrown = true;
                        break;
                }
            }

            if (sawCrossbow)
                return RangedFamily.Crossbow;
            if (sawBow)
                return RangedFamily.Bow;
            if (sawThrown)
                return RangedFamily.Thrown;

            return RangedFamily.None;
        }

        private static MeleeFamily ResolveMeleeFamily(IEnumerable<ItemObject> items)
        {
            bool sawPolearm = false;
            bool sawTwoHanded = false;
            bool sawOneHanded = false;

            foreach (ItemObject item in items)
            {
                if (item == null || IsShield(item) || IsAmmo(item))
                    continue;

                WeaponComponentData primaryWeapon = item.PrimaryWeapon;
                if (primaryWeapon != null)
                {
                    if (primaryWeapon.IsPolearm)
                    {
                        sawPolearm = true;
                        continue;
                    }

                    if (primaryWeapon.IsTwoHanded)
                    {
                        sawTwoHanded = true;
                        continue;
                    }

                    if (primaryWeapon.IsOneHanded || primaryWeapon.IsMeleeWeapon)
                    {
                        sawOneHanded = true;
                        continue;
                    }
                }

                switch (item.ItemType)
                {
                    case ItemObject.ItemTypeEnum.Polearm:
                        sawPolearm = true;
                        break;
                    case ItemObject.ItemTypeEnum.TwoHandedWeapon:
                        sawTwoHanded = true;
                        break;
                    case ItemObject.ItemTypeEnum.OneHandedWeapon:
                        sawOneHanded = true;
                        break;
                }
            }

            if (sawPolearm)
                return MeleeFamily.Polearm;
            if (sawTwoHanded)
                return MeleeFamily.TwoHanded;
            if (sawOneHanded)
                return MeleeFamily.OneHanded;

            return MeleeFamily.None;
        }

        private static bool IsShield(ItemObject item)
        {
            return item?.ItemType == ItemObject.ItemTypeEnum.Shield;
        }

        private static bool IsAmmo(ItemObject item)
        {
            if (item == null)
                return false;

            switch (item.ItemType)
            {
                case ItemObject.ItemTypeEnum.Arrows:
                case ItemObject.ItemTypeEnum.Bolts:
                case ItemObject.ItemTypeEnum.SlingStones:
                    return true;
                default:
                    return false;
            }
        }

        private enum ItemRole
        {
            None = 0,
            Bow,
            Crossbow,
            Thrown
        }

        private static ItemRole ResolveItemRole(ItemObject item)
        {
            if (item == null)
                return ItemRole.None;

            WeaponComponentData primaryWeapon = item.PrimaryWeapon;
            if (primaryWeapon != null)
            {
                if (primaryWeapon.WeaponClass == WeaponClass.Crossbow ||
                    item.ItemType == ItemObject.ItemTypeEnum.Crossbow ||
                    item.ItemType == ItemObject.ItemTypeEnum.Bolts)
                {
                    return ItemRole.Crossbow;
                }

                if (primaryWeapon.WeaponClass == WeaponClass.Bow ||
                    item.ItemType == ItemObject.ItemTypeEnum.Bow ||
                    item.ItemType == ItemObject.ItemTypeEnum.Arrows)
                {
                    return ItemRole.Bow;
                }

                if (primaryWeapon.IsRangedWeapon ||
                    primaryWeapon.WeaponClass == WeaponClass.Javelin ||
                    primaryWeapon.WeaponClass == WeaponClass.ThrowingAxe ||
                    primaryWeapon.WeaponClass == WeaponClass.ThrowingKnife ||
                    primaryWeapon.WeaponClass == WeaponClass.Stone ||
                    primaryWeapon.WeaponClass == WeaponClass.SlingStone ||
                    item.ItemType == ItemObject.ItemTypeEnum.Thrown ||
                    item.ItemType == ItemObject.ItemTypeEnum.Sling ||
                    item.ItemType == ItemObject.ItemTypeEnum.SlingStones)
                {
                    return ItemRole.Thrown;
                }
            }

            switch (item.ItemType)
            {
                case ItemObject.ItemTypeEnum.Crossbow:
                case ItemObject.ItemTypeEnum.Bolts:
                    return ItemRole.Crossbow;
                case ItemObject.ItemTypeEnum.Bow:
                case ItemObject.ItemTypeEnum.Arrows:
                    return ItemRole.Bow;
                case ItemObject.ItemTypeEnum.Thrown:
                case ItemObject.ItemTypeEnum.Sling:
                case ItemObject.ItemTypeEnum.SlingStones:
                    return ItemRole.Thrown;
                default:
                    return ItemRole.None;
            }
        }

        private static ItemObject TryResolveItem(string itemId)
        {
            string normalizedItemId = NormalizeObjectId(itemId);
            if (string.IsNullOrWhiteSpace(normalizedItemId))
                return null;

            try
            {
                MBObjectManager objectManager = Game.Current?.ObjectManager ?? MBObjectManager.Instance;
                return objectManager?.GetObject<ItemObject>(normalizedItemId);
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeObjectId(string objectId)
        {
            if (string.IsNullOrWhiteSpace(objectId))
                return null;

            string trimmed = objectId.Trim();
            if (trimmed.StartsWith("Item.", StringComparison.OrdinalIgnoreCase))
                return trimmed.Substring("Item.".Length);

            return trimmed;
        }

        private static bool TryGetBoolProperty(object instance, string propertyName)
        {
            try
            {
                var property = instance?.GetType().GetProperty(propertyName);
                if (property == null || property.PropertyType != typeof(bool))
                    return false;

                return (bool)property.GetValue(instance, null);
            }
            catch
            {
                return false;
            }
        }
    }
}
