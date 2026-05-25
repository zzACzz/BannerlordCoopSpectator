using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using Newtonsoft.Json;
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

        public sealed class ShellAuditDescriptor
        {
            public string TroopTemplateId { get; set; }

            public bool IsMounted { get; set; }

            public bool HasShield { get; set; }

            public bool HasThrown { get; set; }

            public RangedFamily Ranged { get; set; }

            public MeleeFamily Melee { get; set; }

            public string TwoHandedSubtype { get; set; }

            public string PrimaryWeaponClass { get; set; }

            public string PrimaryRangedWeaponClass { get; set; }

            public string PrimaryMeleeWeaponClass { get; set; }

            public string SecondaryMeleeWeaponClass { get; set; }

            public string RuntimeSignatureKey { get; set; }
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

        private enum TwoHandedSubtype
        {
            None = 0,
            Generic,
            Axe
        }

        private static readonly (string ModuleId, string RelativeModuleDataPath)[] RuntimeShellXmlSources =
        {
            ("CoopSpectator", "coopspectator_mpcharacters.xml"),
            ("CoopSpectator", "coopspectator_generated_runtime_mpcharacters.xml"),
            ("CoopSpectator", "coopspectator_startup_mpcharacters.xml")
        };

        private const string GeneratedRuntimeShellManifestPath = "coopspectator_generated_runtime_shell_manifest.json";

        private sealed class GeneratedRuntimeShellManifestEntry
        {
            public string VariantSignature { get; set; }
            public string RuntimeSignatureKey { get; set; }
            public string TroopTemplateId { get; set; }
            public string HeroTemplateId { get; set; }
            public bool IsMounted { get; set; }
            public bool HasShield { get; set; }
            public bool HasThrown { get; set; }
            public string RangedFamily { get; set; }
            public string MeleeFamily { get; set; }
        }

        private static readonly object Sync = new object();
        private static readonly HashSet<string> LoggedFallbackProfileKeys = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> KnownCompatibilityShellTemplateIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, GeneratedRuntimeShellManifestEntry> GeneratedRuntimeShellManifestEntriesByVariantSignature =
            new Dictionary<string, GeneratedRuntimeShellManifestEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, GeneratedRuntimeShellManifestEntry> GeneratedRuntimeShellManifestEntriesByRuntimeSignatureKey =
            new Dictionary<string, GeneratedRuntimeShellManifestEntry>(StringComparer.OrdinalIgnoreCase);
        private static bool _knownCompatibilityShellTemplateIdsLoaded;
        private static string _knownCompatibilityShellTemplateIdsSummary = "not-loaded";
        private static bool _generatedRuntimeShellManifestLoaded;
        private static string _generatedRuntimeShellManifestSummary = "not-loaded";

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

        public static ShellProfile TryResolveProfileFromVariantSignature(string variantSignature)
        {
            if (string.IsNullOrWhiteSpace(variantSignature))
                return null;

            EnsureGeneratedRuntimeShellManifestLoaded();
            lock (Sync)
            {
                if (!GeneratedRuntimeShellManifestEntriesByVariantSignature.TryGetValue(
                        variantSignature.Trim(),
                        out GeneratedRuntimeShellManifestEntry manifestEntry) ||
                    manifestEntry == null)
                {
                    return null;
                }

                return BuildShellProfileFromManifestEntry(manifestEntry);
            }
        }

        public static ShellProfile TryResolveProfileFromVariantData(
            string combatItem0Id,
            int? combatItem0Amount,
            string combatItem1Id,
            int? combatItem1Amount,
            string combatItem2Id,
            int? combatItem2Amount,
            string combatItem3Id,
            int? combatItem3Amount,
            string combatHeadId,
            string combatBodyId,
            string combatLegId,
            string combatGlovesId,
            string combatCapeId,
            string combatHorseId,
            string combatHorseHarnessId)
        {
            string variantSignature = BuildVariantSignature(
                combatItem0Id,
                combatItem0Amount,
                combatItem1Id,
                combatItem1Amount,
                combatItem2Id,
                combatItem2Amount,
                combatItem3Id,
                combatItem3Amount,
                combatHeadId,
                combatBodyId,
                combatLegId,
                combatGlovesId,
                combatCapeId,
                combatHorseId,
                combatHorseHarnessId);
            return TryResolveProfileFromVariantSignature(variantSignature);
        }

        public static ShellProfile TryResolveProfileFromRuntimeSignature(
            string combatItem0Id,
            string combatItem1Id,
            string combatItem2Id,
            string combatItem3Id,
            string combatHorseId,
            bool mountedHint = false)
        {
            return TryResolveProfileFromRuntimeSignature(
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

        public static ShellProfile TryResolveProfileFromRuntimeSignature(
            IEnumerable<string> weaponItemIds,
            string horseItemId,
            bool mountedHint = false)
        {
            List<ItemObject> items = (weaponItemIds ?? Array.Empty<string>())
                .Select(TryResolveItem)
                .Where(item => item != null)
                .ToList();

            string normalizedHorseItemId = NormalizeObjectId(horseItemId);
            return TryResolveProfileFromRuntimeSignature(
                items,
                !string.IsNullOrWhiteSpace(normalizedHorseItemId) || mountedHint,
                normalizedHorseItemId);
        }

        public static ShellAuditDescriptor ResolveAuditDescriptor(BasicCharacterObject character)
        {
            if (character == null)
                return null;

            bool mountedHint = ResolveMountedHint(character);
            foreach (Equipment equipment in EnumerateBattleEquipments(character))
            {
                ShellAuditDescriptor descriptor = ResolveAuditDescriptor(equipment, mountedHint);
                if (descriptor != null)
                    return descriptor;
            }

            return null;
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

            string normalizedHorseItemId = NormalizeObjectId(horseItemId);
            return ResolveProfile(
                items,
                !string.IsNullOrWhiteSpace(normalizedHorseItemId) || mountedHint,
                normalizedHorseItemId);
        }

        public static ShellAuditDescriptor ResolveAuditDescriptor(
            string combatItem0Id,
            string combatItem1Id,
            string combatItem2Id,
            string combatItem3Id,
            string combatHorseId,
            bool mountedHint = false)
        {
            return ResolveAuditDescriptor(
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

        public static ShellAuditDescriptor ResolveAuditDescriptor(IEnumerable<string> weaponItemIds, string horseItemId, bool mountedHint = false)
        {
            List<ItemObject> items = (weaponItemIds ?? Array.Empty<string>())
                .Select(TryResolveItem)
                .Where(item => item != null)
                .ToList();

            return ResolveAuditDescriptor(
                items,
                !string.IsNullOrWhiteSpace(NormalizeObjectId(horseItemId)) || mountedHint,
                NormalizeObjectId(horseItemId));
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
            string horseItemId = NormalizeObjectId(equipment[EquipmentIndex.Horse].Item?.StringId);
            bool mounted = mountedHint || !string.IsNullOrWhiteSpace(horseItemId);

            for (EquipmentIndex slot = EquipmentIndex.Weapon0; slot <= EquipmentIndex.Weapon3; slot++)
            {
                ItemObject item = equipment[slot].Item;
                if (item == null)
                    continue;

                items.Add(item);
                if (IsShield(item))
                    hasShield = true;
            }

            return ResolveProfile(items, mounted, hasShield, horseItemId);
        }

        private static ShellAuditDescriptor ResolveAuditDescriptor(Equipment equipment, bool mountedHint)
        {
            if (equipment == null)
                return null;

            var items = new List<ItemObject>();
            bool hasShield = false;
            string horseItemId = NormalizeObjectId(equipment[EquipmentIndex.Horse].Item?.StringId);
            bool mounted = mountedHint || !string.IsNullOrWhiteSpace(horseItemId);

            for (EquipmentIndex slot = EquipmentIndex.Weapon0; slot <= EquipmentIndex.Weapon3; slot++)
            {
                ItemObject item = equipment[slot].Item;
                if (item == null)
                    continue;

                items.Add(item);
                if (IsShield(item))
                    hasShield = true;
            }

            return ResolveAuditDescriptor(items, mounted, horseItemId, hasShield);
        }

        private static ShellProfile ResolveProfile(List<ItemObject> items, bool mounted, string horseItemId = null)
        {
            bool hasShield = items.Any(IsShield);
            return ResolveProfile(items, mounted, hasShield, horseItemId);
        }

        private static ShellProfile ResolveProfile(List<ItemObject> items, bool mounted, bool hasShield, string horseItemId = null)
        {
            if (items == null)
                return null;

            RangedFamily ranged = ResolveRangedFamily(items);
            MeleeFamily melee = ResolveMeleeFamily(items);
            TwoHandedSubtype twoHandedSubtype = melee == MeleeFamily.TwoHanded
                ? ResolveTwoHandedSubtype(items)
                : TwoHandedSubtype.None;

            string troopTemplateId = ResolveGeneratedTroopTemplateId(items, mounted, ranged, melee, hasShield, horseItemId) ??
                                     ResolveDetailedTroopTemplateId(items, mounted, ranged, melee, hasShield, twoHandedSubtype) ??
                                     ResolveTroopTemplateId(mounted, ranged, melee, hasShield, twoHandedSubtype);
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

        private static ShellAuditDescriptor ResolveAuditDescriptor(List<ItemObject> items, bool mounted, string horseItemId, bool? hasShieldOverride = null)
        {
            if (items == null)
                return null;

            bool hasShield = hasShieldOverride ?? items.Any(IsShield);
            RangedFamily ranged = ResolveRangedFamily(items);
            MeleeFamily melee = ResolveMeleeFamily(items);
            TwoHandedSubtype twoHandedSubtype = melee == MeleeFamily.TwoHanded
                ? ResolveTwoHandedSubtype(items)
                : TwoHandedSubtype.None;

            string troopTemplateId = ResolveGeneratedTroopTemplateId(items, mounted, ranged, melee, hasShield, horseItemId) ??
                                     ResolveDetailedTroopTemplateId(items, mounted, ranged, melee, hasShield, twoHandedSubtype) ??
                                     ResolveTroopTemplateId(mounted, ranged, melee, hasShield, twoHandedSubtype);
            if (string.IsNullOrWhiteSpace(troopTemplateId))
                return null;

            ItemObject primaryWeapon = ResolvePrimaryCombatItem(items);
            ItemObject primaryRangedWeapon = ResolvePrimaryRangedCombatItem(items);
            List<ItemObject> meleeWeapons = ResolveMeleeCombatItems(items);
            ItemObject primaryMeleeWeapon = meleeWeapons.Count > 0 ? meleeWeapons[0] : null;
            ItemObject secondaryMeleeWeapon = meleeWeapons.Count > 1 ? meleeWeapons[1] : null;

            string primaryWeaponClass = ResolveWeaponClassToken(primaryWeapon);
            string primaryRangedWeaponClass = ResolveWeaponClassToken(primaryRangedWeapon);
            string primaryMeleeWeaponClass = ResolveWeaponClassToken(primaryMeleeWeapon);
            string secondaryMeleeWeaponClass = ResolveWeaponClassToken(secondaryMeleeWeapon);
            string twoHandedSubtypeToken = twoHandedSubtype == TwoHandedSubtype.None
                ? "None"
                : twoHandedSubtype.ToString();

            return new ShellAuditDescriptor
            {
                TroopTemplateId = troopTemplateId,
                IsMounted = mounted,
                HasShield = hasShield,
                HasThrown = ranged == RangedFamily.Thrown,
                Ranged = ranged,
                Melee = melee,
                TwoHandedSubtype = twoHandedSubtypeToken,
                PrimaryWeaponClass = primaryWeaponClass,
                PrimaryRangedWeaponClass = primaryRangedWeaponClass,
                PrimaryMeleeWeaponClass = primaryMeleeWeaponClass,
                SecondaryMeleeWeaponClass = secondaryMeleeWeaponClass,
                RuntimeSignatureKey = BuildRuntimeSignatureKey(
                    mounted,
                    hasShield,
                    ranged,
                    melee,
                    twoHandedSubtypeToken,
                    primaryWeaponClass,
                    primaryRangedWeaponClass,
                    primaryMeleeWeaponClass,
                    secondaryMeleeWeaponClass,
                    horseItemId)
            };
        }

        private static string ResolveDetailedTroopTemplateId(
            List<ItemObject> items,
            bool mounted,
            RangedFamily ranged,
            MeleeFamily melee,
            bool hasShield,
            TwoHandedSubtype twoHandedSubtype)
        {
            if (items == null || items.Count == 0)
                return null;

            string primaryRangedWeaponToken = ResolvePrimaryRangedWeaponToken(items);
            string primaryMeleeWeaponToken = ResolvePrimaryMeleeWeaponToken(items);
            string secondaryMeleeWeaponToken = ResolveSecondaryMeleeWeaponToken(items);

            if (mounted)
            {
                if (ranged == RangedFamily.None && melee != MeleeFamily.TwoHanded)
                {
                    if (hasShield)
                    {
                        if (primaryMeleeWeaponToken == "1h" && secondaryMeleeWeaponToken == "polearm")
                            return ResolveExistingDetailedTroopTemplateId("mp_coop_mounted_melee_1h_polearm_shield_troop");
                        if (primaryMeleeWeaponToken == "polearm" && secondaryMeleeWeaponToken == "1h")
                            return ResolveExistingDetailedTroopTemplateId("mp_coop_mounted_melee_polearm_1h_shield_troop");
                        if (primaryMeleeWeaponToken == "1h" && string.IsNullOrWhiteSpace(secondaryMeleeWeaponToken))
                            return ResolveExistingDetailedTroopTemplateId("mp_coop_mounted_melee_1h_shield_troop");
                    }
                    else
                    {
                        if (primaryMeleeWeaponToken == "1h" && string.IsNullOrWhiteSpace(secondaryMeleeWeaponToken))
                            return ResolveExistingDetailedTroopTemplateId("mp_coop_mounted_melee_1h_no_shield_troop");
                    }
                }

                if (ranged == RangedFamily.Bow && !hasShield)
                {
                    if (primaryMeleeWeaponToken == "1h" && secondaryMeleeWeaponToken == "polearm")
                        return ResolveExistingDetailedTroopTemplateId("mp_coop_mounted_bow_1h_polearm_no_shield_troop");
                    if (primaryMeleeWeaponToken == "polearm" && secondaryMeleeWeaponToken == "1h")
                        return ResolveExistingDetailedTroopTemplateId("mp_coop_mounted_bow_polearm_1h_no_shield_troop");
                    if (primaryMeleeWeaponToken == "1h" && secondaryMeleeWeaponToken == "2haxe")
                        return ResolveExistingDetailedTroopTemplateId("mp_coop_mounted_bow_1h_2haxe_no_shield_troop");
                }
            }
            else
            {
                if (ranged == RangedFamily.None && melee == MeleeFamily.Polearm)
                {
                    if (hasShield)
                    {
                        if (primaryMeleeWeaponToken == "1h" && secondaryMeleeWeaponToken == "polearm")
                            return ResolveExistingDetailedTroopTemplateId("mp_coop_foot_melee_1h_polearm_shield_troop");
                        if (primaryMeleeWeaponToken == "polearm" && secondaryMeleeWeaponToken == "1h")
                            return ResolveExistingDetailedTroopTemplateId("mp_coop_foot_melee_polearm_1h_shield_troop");
                        if (primaryMeleeWeaponToken == "polearm" && string.IsNullOrWhiteSpace(secondaryMeleeWeaponToken))
                            return ResolveExistingDetailedTroopTemplateId("mp_coop_foot_melee_polearm_only_shield_troop");
                    }
                }

                if (ranged == RangedFamily.Crossbow && !hasShield)
                {
                    if (primaryMeleeWeaponToken == "polearm")
                        return ResolveExistingDetailedTroopTemplateId("mp_coop_foot_crossbow_polearm_no_shield_troop");
                    if (primaryMeleeWeaponToken == "2haxe" || secondaryMeleeWeaponToken == "2haxe")
                        return ResolveExistingDetailedTroopTemplateId("mp_coop_foot_crossbow_2haxe_no_shield_troop");
                }
            }

            return null;
        }

        private static string ResolveGeneratedTroopTemplateId(
            List<ItemObject> items,
            bool mounted,
            RangedFamily ranged,
            MeleeFamily melee,
            bool hasShield,
            string horseItemId)
        {
            if (items == null || items.Count == 0)
                return null;

            ItemObject primaryRangedWeapon = ResolvePrimaryRangedCombatItem(items);
            List<ItemObject> meleeWeapons = ResolveMeleeCombatItems(items);
            ItemObject primaryMeleeWeapon = meleeWeapons.Count > 0 ? meleeWeapons[0] : null;
            ItemObject secondaryMeleeWeapon = meleeWeapons.Count > 1 ? meleeWeapons[1] : null;

            string primaryRangedWeaponToken = NormalizeGeneratedToken(
                ResolveWeaponClassToken(primaryRangedWeapon),
                ranged == RangedFamily.Thrown ? "javelin" : null);
            string primaryMeleeToken = NormalizeGeneratedToken(
                ResolveWeaponClassToken(primaryMeleeWeapon),
                "unarmed");
            string secondaryMeleeToken = NormalizeGeneratedToken(
                ResolveWeaponClassToken(secondaryMeleeWeapon),
                null);
            string horseToken = mounted
                ? NormalizeGeneratedToken(NormalizeObjectId(horseItemId), "horse")
                : null;

            string shellBaseId = BuildGeneratedShellBaseId(
                mounted,
                ranged,
                primaryRangedWeaponToken,
                primaryMeleeToken,
                secondaryMeleeToken,
                horseToken);

            string runtimeSignatureKey = BuildRuntimeSignatureKey(
                mounted,
                hasShield,
                ranged,
                melee,
                melee == MeleeFamily.TwoHanded ? ResolveTwoHandedSubtype(items).ToString() : "None",
                ResolveWeaponClassToken(ResolvePrimaryCombatItem(items)),
                ResolveWeaponClassToken(primaryRangedWeapon),
                ResolveWeaponClassToken(primaryMeleeWeapon),
                ResolveWeaponClassToken(secondaryMeleeWeapon),
                NormalizeObjectId(horseItemId));

            string signatureHash = ComputeShortSignatureHash(runtimeSignatureKey);
            string troopTemplateId = shellBaseId + "_" + signatureHash + "_" + (hasShield ? "shield" : "no_shield") + "_troop";

            return ResolveExistingDetailedTroopTemplateId(troopTemplateId);
        }

        private static string ResolveExistingDetailedTroopTemplateId(string troopTemplateId)
        {
            if (string.IsNullOrWhiteSpace(troopTemplateId))
                return null;

            try
            {
                MBObjectManager objectManager = Game.Current?.ObjectManager ?? MBObjectManager.Instance;
                if (objectManager?.GetObject<BasicCharacterObject>(troopTemplateId) != null)
                    return troopTemplateId;
            }
            catch
            {
            }

            return IsKnownCompatibilityShellTemplateId(troopTemplateId)
                ? troopTemplateId
                : null;
        }

        private static bool IsKnownCompatibilityShellTemplateId(string troopTemplateId)
        {
            if (string.IsNullOrWhiteSpace(troopTemplateId))
                return false;

            EnsureKnownCompatibilityShellTemplateIdsLoaded();
            lock (Sync)
            {
                return KnownCompatibilityShellTemplateIds.Contains(troopTemplateId);
            }
        }

        private static void EnsureKnownCompatibilityShellTemplateIdsLoaded()
        {
            lock (Sync)
            {
                if (_knownCompatibilityShellTemplateIdsLoaded)
                    return;

                KnownCompatibilityShellTemplateIds.Clear();
                var loadedSources = new List<string>();

                foreach ((string moduleId, string relativeModuleDataPath) in RuntimeShellXmlSources)
                {
                    string xmlPath = ModulePathHelper.GetSiblingModuleDataFilePath(moduleId, relativeModuleDataPath);
                    if (string.IsNullOrWhiteSpace(xmlPath))
                    {
                        loadedSources.Add(moduleId + "/" + relativeModuleDataPath + "=path-null");
                        continue;
                    }

                    try
                    {
                        var xmlDocument = new XmlDocument();
                        xmlDocument.Load(xmlPath);
                        XmlElement documentElement = xmlDocument.DocumentElement;
                        if (documentElement == null)
                        {
                            loadedSources.Add(moduleId + "/" + relativeModuleDataPath + "=empty");
                            continue;
                        }

                        int addedCountBefore = KnownCompatibilityShellTemplateIds.Count;
                        foreach (XmlNode node in documentElement.ChildNodes)
                        {
                            if (node?.NodeType != XmlNodeType.Element)
                                continue;

                            XmlAttribute idAttribute = node.Attributes?["id"];
                            if (idAttribute == null || string.IsNullOrWhiteSpace(idAttribute.Value))
                                continue;

                            KnownCompatibilityShellTemplateIds.Add(idAttribute.Value.Trim());
                        }

                        loadedSources.Add(
                            moduleId + "/" + relativeModuleDataPath +
                            "=loaded:" + (KnownCompatibilityShellTemplateIds.Count - addedCountBefore));
                    }
                    catch (Exception ex)
                    {
                        loadedSources.Add(moduleId + "/" + relativeModuleDataPath + "=failed:" + ex.GetType().Name);
                    }
                }

                _knownCompatibilityShellTemplateIdsLoaded = true;
                _knownCompatibilityShellTemplateIdsSummary =
                    "KnownShellIds=" + KnownCompatibilityShellTemplateIds.Count +
                    " Sources=[" + string.Join(", ", loadedSources) + "]";
                ModLogger.Info(
                    "CompatibilityShellTemplateResolver: indexed compatibility shell template ids from module XML. " +
                    _knownCompatibilityShellTemplateIdsSummary);
            }
        }

        private static void EnsureGeneratedRuntimeShellManifestLoaded()
        {
            lock (Sync)
            {
                if (_generatedRuntimeShellManifestLoaded)
                    return;

                GeneratedRuntimeShellManifestEntriesByVariantSignature.Clear();
                GeneratedRuntimeShellManifestEntriesByRuntimeSignatureKey.Clear();
                string manifestPath = ModulePathHelper.GetSiblingModuleDataFilePath(
                    "CoopSpectator",
                    GeneratedRuntimeShellManifestPath);
                if (string.IsNullOrWhiteSpace(manifestPath))
                {
                    _generatedRuntimeShellManifestLoaded = true;
                    _generatedRuntimeShellManifestSummary = "manifest-path-null";
                    ModLogger.Info(
                        "CompatibilityShellTemplateResolver: generated runtime shell manifest path could not be resolved.");
                    return;
                }

                try
                {
                    string json = System.IO.File.ReadAllText(manifestPath);
                    var entries = JsonConvert.DeserializeObject<List<GeneratedRuntimeShellManifestEntry>>(json) ??
                                  new List<GeneratedRuntimeShellManifestEntry>();
                    foreach (GeneratedRuntimeShellManifestEntry entry in entries)
                    {
                        if (entry == null || string.IsNullOrWhiteSpace(entry.VariantSignature))
                            continue;

                        GeneratedRuntimeShellManifestEntriesByVariantSignature[entry.VariantSignature.Trim()] = entry;
                        if (!string.IsNullOrWhiteSpace(entry.RuntimeSignatureKey))
                            GeneratedRuntimeShellManifestEntriesByRuntimeSignatureKey[entry.RuntimeSignatureKey.Trim()] = entry;
                    }

                    _generatedRuntimeShellManifestSummary =
                        "VariantEntries=" + GeneratedRuntimeShellManifestEntriesByVariantSignature.Count +
                        " RuntimeKeys=" + GeneratedRuntimeShellManifestEntriesByRuntimeSignatureKey.Count +
                        " Path=" + manifestPath;
                }
                catch (Exception ex)
                {
                    _generatedRuntimeShellManifestSummary =
                        "failed:" + ex.GetType().Name + " Path=" + manifestPath;
                }

                _generatedRuntimeShellManifestLoaded = true;
                ModLogger.Info(
                    "CompatibilityShellTemplateResolver: indexed generated runtime shell manifest. " +
                    _generatedRuntimeShellManifestSummary);
            }
        }

        private static RangedFamily TryParseRangedFamily(string value)
        {
            return Enum.TryParse(value, ignoreCase: true, out RangedFamily parsed)
                ? parsed
                : RangedFamily.None;
        }

        private static MeleeFamily TryParseMeleeFamily(string value)
        {
            return Enum.TryParse(value, ignoreCase: true, out MeleeFamily parsed)
                ? parsed
                : MeleeFamily.None;
        }

        private static string NormalizeGeneratedToken(string token, string fallbackToken)
        {
            if (string.IsNullOrWhiteSpace(token))
                return fallbackToken;

            switch (token)
            {
                case "1h":
                    return "1hsword";
                case "1haxe":
                    return "1haxe";
                case "mace":
                    return "mace";
                case "polearm":
                    return "polearm";
                case "2haxe":
                    return "2haxe";
                case "2hsword":
                    return "2hsword";
                case "2hpolearm":
                    return "2hpolearm";
                case "2hmace":
                    return "2hmace";
                case "dagger":
                    return "dagger";
                case "bow":
                    return "bow";
                case "crossbow":
                    return "crossbow";
                case "javelin":
                    return "javelin";
                case "throwingaxe":
                    return "throwingaxe";
                case "throwingknife":
                    return "throwingknife";
                case "stone":
                    return "stone";
                case "sling":
                    return "sling";
                default:
                    return token.ToLowerInvariant().Replace(" ", string.Empty);
            }
        }

        private static string BuildGeneratedShellBaseId(
            bool mounted,
            RangedFamily ranged,
            string primaryRangedWeaponToken,
            string primaryMeleeToken,
            string secondaryMeleeToken,
            string horseToken)
        {
            string combatPrefixToken;
            switch (ranged)
            {
                case RangedFamily.Bow:
                    combatPrefixToken = "bow";
                    break;
                case RangedFamily.Crossbow:
                    combatPrefixToken = "crossbow";
                    break;
                case RangedFamily.Thrown:
                    combatPrefixToken = "thrown_" + NormalizeGeneratedToken(primaryRangedWeaponToken, "javelin");
                    break;
                default:
                    combatPrefixToken = "melee";
                    break;
            }

            string mountToken = mounted ? "mounted" : "foot";
            string meleePrimaryToken = NormalizeGeneratedToken(primaryMeleeToken, "unarmed");
            string meleeSecondaryToken = NormalizeGeneratedToken(secondaryMeleeToken, null);
            string normalizedHorseToken = mounted
                ? NormalizeGeneratedToken(horseToken, "horse")
                : null;

            string shellBaseId = "mp_coop_" + mountToken + "_" + combatPrefixToken + "_" + meleePrimaryToken;
            if (!string.IsNullOrWhiteSpace(meleeSecondaryToken))
                shellBaseId += "_" + meleeSecondaryToken;
            if (!string.IsNullOrWhiteSpace(normalizedHorseToken))
                shellBaseId += "_" + normalizedHorseToken;

            return shellBaseId;
        }

        private static ShellProfile TryResolveProfileFromRuntimeSignature(
            List<ItemObject> items,
            bool mounted,
            string horseItemId = null)
        {
            if (items == null || items.Count == 0)
                return null;

            string runtimeSignatureKey = BuildRuntimeSignatureKeyForItems(items, mounted, horseItemId);
            if (string.IsNullOrWhiteSpace(runtimeSignatureKey))
                return null;

            EnsureGeneratedRuntimeShellManifestLoaded();
            lock (Sync)
            {
                if (!GeneratedRuntimeShellManifestEntriesByRuntimeSignatureKey.TryGetValue(
                        runtimeSignatureKey.Trim(),
                        out GeneratedRuntimeShellManifestEntry manifestEntry) ||
                    manifestEntry == null)
                {
                    return null;
                }

                return BuildShellProfileFromManifestEntry(manifestEntry);
            }
        }

        private static ShellProfile BuildShellProfileFromManifestEntry(GeneratedRuntimeShellManifestEntry manifestEntry)
        {
            if (manifestEntry == null)
                return null;

            string troopTemplateId = ResolveExistingDetailedTroopTemplateId(manifestEntry.TroopTemplateId);
            if (string.IsNullOrWhiteSpace(troopTemplateId))
                return null;

            return new ShellProfile
            {
                TroopTemplateId = troopTemplateId,
                IsMounted = manifestEntry.IsMounted,
                HasShield = manifestEntry.HasShield,
                HasThrown = manifestEntry.HasThrown,
                Ranged = TryParseRangedFamily(manifestEntry.RangedFamily),
                Melee = TryParseMeleeFamily(manifestEntry.MeleeFamily)
            };
        }

        private static string BuildVariantSignature(
            string combatItem0Id,
            int? combatItem0Amount,
            string combatItem1Id,
            int? combatItem1Amount,
            string combatItem2Id,
            int? combatItem2Amount,
            string combatItem3Id,
            int? combatItem3Amount,
            string combatHeadId,
            string combatBodyId,
            string combatLegId,
            string combatGlovesId,
            string combatCapeId,
            string combatHorseId,
            string combatHorseHarnessId)
        {
            return string.Join("|", new[]
            {
                combatItem0Id ?? string.Empty,
                combatItem0Amount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                combatItem1Id ?? string.Empty,
                combatItem1Amount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                combatItem2Id ?? string.Empty,
                combatItem2Amount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                combatItem3Id ?? string.Empty,
                combatItem3Amount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                combatHeadId ?? string.Empty,
                combatBodyId ?? string.Empty,
                combatLegId ?? string.Empty,
                combatGlovesId ?? string.Empty,
                combatCapeId ?? string.Empty,
                combatHorseId ?? string.Empty,
                combatHorseHarnessId ?? string.Empty
            });
        }

        private static string BuildRuntimeSignatureKeyForItems(
            List<ItemObject> items,
            bool mounted,
            string horseItemId = null)
        {
            if (items == null)
                return null;

            bool hasShield = items.Any(IsShield);
            RangedFamily ranged = ResolveRangedFamily(items);
            MeleeFamily melee = ResolveMeleeFamily(items);
            TwoHandedSubtype twoHandedSubtype = melee == MeleeFamily.TwoHanded
                ? ResolveTwoHandedSubtype(items)
                : TwoHandedSubtype.None;
            ItemObject primaryWeapon = ResolvePrimaryCombatItem(items);
            ItemObject primaryRangedWeapon = ResolvePrimaryRangedCombatItem(items);
            List<ItemObject> meleeWeapons = ResolveMeleeCombatItems(items);
            ItemObject primaryMeleeWeapon = meleeWeapons.Count > 0 ? meleeWeapons[0] : null;
            ItemObject secondaryMeleeWeapon = meleeWeapons.Count > 1 ? meleeWeapons[1] : null;

            return BuildRuntimeSignatureKey(
                mounted,
                hasShield,
                ranged,
                melee,
                twoHandedSubtype == TwoHandedSubtype.None ? "None" : twoHandedSubtype.ToString(),
                ResolveWeaponClassToken(primaryWeapon),
                ResolveWeaponClassToken(primaryRangedWeapon),
                ResolveWeaponClassToken(primaryMeleeWeapon),
                ResolveWeaponClassToken(secondaryMeleeWeapon),
                NormalizeObjectId(horseItemId));
        }

        private static string BuildRuntimeSignatureKey(
            bool mounted,
            bool hasShield,
            RangedFamily ranged,
            MeleeFamily melee,
            string twoHandedSubtypeToken,
            string primaryWeaponClass,
            string primaryRangedWeaponClass,
            string primaryMeleeWeaponClass,
            string secondaryMeleeWeaponClass,
            string horseItemId)
        {
            return string.Join("|", new[]
            {
                "mounted=" + mounted,
                "shield=" + hasShield,
                "ranged=" + ranged,
                "melee=" + melee,
                "two_handed_subtype=" + (twoHandedSubtypeToken ?? "None"),
                "primary_weapon=" + (primaryWeaponClass ?? "None"),
                "primary_ranged=" + (primaryRangedWeaponClass ?? "None"),
                "primary_melee=" + (primaryMeleeWeaponClass ?? "None"),
                "secondary_melee=" + (secondaryMeleeWeaponClass ?? "None"),
                "horse=" + (horseItemId ?? "None")
            });
        }

        private static string ComputeShortSignatureHash(string runtimeSignatureKey)
        {
            if (string.IsNullOrWhiteSpace(runtimeSignatureKey))
                return "nosig";

            using (var sha1 = SHA1.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(runtimeSignatureKey);
                byte[] hashBytes = sha1.ComputeHash(bytes);
                var builder = new StringBuilder(8);
                for (int i = 0; i < 4 && i < hashBytes.Length; i++)
                    builder.Append(hashBytes[i].ToString("x2"));
                return builder.ToString();
            }
        }

        private static string ResolveTroopTemplateId(
            bool mounted,
            RangedFamily ranged,
            MeleeFamily melee,
            bool hasShield,
            TwoHandedSubtype twoHandedSubtype)
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
                    {
                        return twoHandedSubtype == TwoHandedSubtype.Axe
                            ? "mp_coop_foot_thrown_2haxe_no_shield_troop"
                            : "mp_coop_foot_thrown_2h_no_shield_troop";
                    }
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

        private static TwoHandedSubtype ResolveTwoHandedSubtype(IEnumerable<ItemObject> items)
        {
            foreach (ItemObject item in items)
            {
                if (item == null || IsShield(item) || IsAmmo(item))
                    continue;

                if (ResolveItemRole(item) != ItemRole.None)
                    continue;

                WeaponComponentData primaryWeapon = item.PrimaryWeapon;
                if (primaryWeapon == null)
                    continue;

                if (primaryWeapon.IsPolearm || !primaryWeapon.IsTwoHanded)
                    continue;

                if (primaryWeapon.WeaponClass == WeaponClass.TwoHandedAxe)
                    return TwoHandedSubtype.Axe;

                return TwoHandedSubtype.Generic;
            }

            return TwoHandedSubtype.Generic;
        }

        private static ItemObject ResolvePrimaryCombatItem(IEnumerable<ItemObject> items)
        {
            foreach (ItemObject item in items ?? Enumerable.Empty<ItemObject>())
            {
                if (item == null || IsShield(item) || IsAmmo(item))
                    continue;

                return item;
            }

            return null;
        }

        private static ItemObject ResolvePrimaryRangedCombatItem(IEnumerable<ItemObject> items)
        {
            foreach (ItemObject item in items ?? Enumerable.Empty<ItemObject>())
            {
                if (item == null || IsShield(item) || IsAmmo(item))
                    continue;

                if (ResolveItemRole(item) != ItemRole.None)
                    return item;
            }

            return null;
        }

        private static List<ItemObject> ResolveMeleeCombatItems(IEnumerable<ItemObject> items)
        {
            return (items ?? Enumerable.Empty<ItemObject>())
                .Where(item => item != null && !IsShield(item) && !IsAmmo(item) && ResolveItemRole(item) == ItemRole.None)
                .ToList();
        }

        private static string ResolvePrimaryRangedWeaponToken(IEnumerable<ItemObject> items)
        {
            return NormalizeRangedWeaponToken(ResolvePrimaryRangedCombatItem(items));
        }

        private static string ResolvePrimaryMeleeWeaponToken(IEnumerable<ItemObject> items)
        {
            List<ItemObject> meleeWeapons = ResolveMeleeCombatItems(items);
            return NormalizeMeleeWeaponToken(meleeWeapons.Count > 0 ? meleeWeapons[0] : null);
        }

        private static string ResolveSecondaryMeleeWeaponToken(IEnumerable<ItemObject> items)
        {
            List<ItemObject> meleeWeapons = ResolveMeleeCombatItems(items);
            return NormalizeMeleeWeaponToken(meleeWeapons.Count > 1 ? meleeWeapons[1] : null);
        }

        private static string NormalizeRangedWeaponToken(ItemObject item)
        {
            if (item == null)
                return null;

            WeaponClass? weaponClass = item.PrimaryWeapon?.WeaponClass;
            if (weaponClass == WeaponClass.Crossbow)
                return "crossbow";
            if (weaponClass == WeaponClass.Bow)
                return "bow";
            if (weaponClass == WeaponClass.Stone || weaponClass == WeaponClass.SlingStone)
                return "stone";
            if (weaponClass == WeaponClass.Javelin ||
                weaponClass == WeaponClass.ThrowingAxe ||
                weaponClass == WeaponClass.ThrowingKnife)
            {
                return "thrown";
            }

            switch (item.ItemType)
            {
                case ItemObject.ItemTypeEnum.Bow:
                case ItemObject.ItemTypeEnum.Arrows:
                    return "bow";
                case ItemObject.ItemTypeEnum.Crossbow:
                case ItemObject.ItemTypeEnum.Bolts:
                    return "crossbow";
                case ItemObject.ItemTypeEnum.Sling:
                case ItemObject.ItemTypeEnum.SlingStones:
                    return "stone";
                case ItemObject.ItemTypeEnum.Thrown:
                    return "thrown";
                default:
                    return ResolveItemRole(item) == ItemRole.Thrown ? "thrown" : null;
            }
        }

        private static string NormalizeMeleeWeaponToken(ItemObject item)
        {
            if (item == null)
                return null;

            WeaponComponentData primaryWeapon = item.PrimaryWeapon;
            WeaponClass? weaponClass = primaryWeapon?.WeaponClass;
            if (weaponClass == WeaponClass.OneHandedPolearm || weaponClass == WeaponClass.TwoHandedPolearm)
                return "polearm";
            if (weaponClass == WeaponClass.TwoHandedAxe)
                return "2haxe";
            if (weaponClass == WeaponClass.TwoHandedMace)
                return "2hmace";
            if (weaponClass == WeaponClass.TwoHandedSword)
                return "2h";
            if (weaponClass == WeaponClass.OneHandedSword ||
                weaponClass == WeaponClass.OneHandedAxe ||
                weaponClass == WeaponClass.Mace ||
                weaponClass == WeaponClass.Dagger)
            {
                return "1h";
            }

            if (primaryWeapon != null)
            {
                if (primaryWeapon.IsPolearm)
                    return "polearm";
                if (primaryWeapon.IsTwoHanded)
                    return "2h";
                if (primaryWeapon.IsOneHanded)
                    return "1h";
            }

            switch (item.ItemType)
            {
                case ItemObject.ItemTypeEnum.Polearm:
                    return "polearm";
                case ItemObject.ItemTypeEnum.TwoHandedWeapon:
                    return "2h";
                case ItemObject.ItemTypeEnum.OneHandedWeapon:
                    return "1h";
                default:
                    return null;
            }
        }

        private static string ResolveWeaponClassToken(ItemObject item)
        {
            if (item == null)
                return null;

            WeaponComponentData primaryWeapon = item.PrimaryWeapon;
            if (primaryWeapon != null)
                return primaryWeapon.WeaponClass.ToString();

            return item.ItemType.ToString();
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
