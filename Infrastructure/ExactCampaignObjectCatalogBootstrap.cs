using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Xml;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Infrastructure
{
    public static class ExactCampaignObjectCatalogBootstrap
    {
        private const string CampaignCharacterTypeName = "TaleWorlds.CampaignSystem.CharacterObject";
        private const string CampaignCultureTypeName = "TaleWorlds.CampaignSystem.CultureObject";
        private const string CampaignAssemblyName = "TaleWorlds.CampaignSystem";

        private static readonly object Sync = new object();
        private static readonly MethodInfo LoadXmlWithGameTypeMethod =
            typeof(MBObjectManager).GetMethod(
                "LoadXML",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(string), typeof(bool), typeof(string), typeof(bool) },
                null);
        private static readonly MethodInfo RegisterTypeMethod =
            typeof(MBObjectManager).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(method =>
                    string.Equals(method.Name, "RegisterType", StringComparison.Ordinal) &&
                    method.IsGenericMethodDefinition &&
                    method.GetParameters().Length == 5);
        private static readonly FieldInfo ObjectTypeRecordsField =
            typeof(MBObjectManager).GetField("ObjectTypeRecords", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Type ObjectTypeRecordInterfaceType =
            typeof(MBObjectManager).GetNestedType("IObjectTypeRecord", BindingFlags.NonPublic);
        private static readonly PropertyInfo ObjectTypeRecordElementNameProperty =
            ObjectTypeRecordInterfaceType?.GetProperty("ElementName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly PropertyInfo ObjectTypeRecordElementListNameProperty =
            ObjectTypeRecordInterfaceType?.GetProperty("ElementListName", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly PropertyInfo ObjectTypeRecordObjectClassProperty =
            ObjectTypeRecordInterfaceType?.GetProperty("ObjectClass", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly string[] CampaignXmlCatalogs =
        {
            "NPCCharacters",
            "SPCultures"
        };

        private static readonly string[] SampleCharacterIds =
        {
            "imperial_recruit",
            "aserai_skirmisher",
            "aserai_mameluke_guard",
            "battanian_fian"
        };

        private static readonly string[] SampleItemIds =
        {
            "peasant_pitchfork_2_t1",
            "glen_ranger_bow",
            "noyans_shield",
            "wide_leaf_spear_t4",
            "battania_noble_armor"
        };

        private static readonly TimeSpan RetryCooldown = TimeSpan.FromMinutes(2);

        private static bool _loaded;
        private static bool _attempted;
        private static int _attemptCount;
        private static DateTime _nextRetryUtc = DateTime.MinValue;
        private static string _lastSummary = "not-attempted";

        public static string LastSummary => _lastSummary;

        public static bool EnsureLoaded(string source)
        {
            if (!ExperimentalFeatures.EnableExactCampaignObjectCatalogBootstrap)
            {
                _lastSummary = "feature-disabled";
                return false;
            }

            lock (Sync)
            {
                MBObjectManager objectManager = Game.Current?.ObjectManager ?? MBObjectManager.Instance;
                if (objectManager == null)
                {
                    _lastSummary = "object-manager-null";
                    return false;
                }

                bool hadCharacterSamplesBefore = HasResolvedCharacterSamples(objectManager, out string beforeCharacterSamples);
                bool hadItemSamplesBefore = HasResolvedItemSamples(objectManager, out string beforeItemSamples);
                int characterCountBefore = TryGetCharacterCount(objectManager);
                int itemCountBefore = TryGetItemCount(objectManager);
                Type campaignCharacterType = TryResolveCampaignType(CampaignCharacterTypeName);
                Type campaignCultureType = TryResolveCampaignType(CampaignCultureTypeName);
                bool canBootstrapCampaignCharacterCatalogs = campaignCharacterType != null && campaignCultureType != null;
                // Exact battle agents use the materialized roster snapshot and MP
                // surrogate characters. Campaign character samples are useful when
                // available, but repeatedly loading the same XML cannot make them
                // ready after the first completed bootstrap attempt. Treat the item
                // catalog as the runtime readiness contract so reinforcement
                // CreateAgent messages never trigger full catalog reloads mid-battle.
                bool alreadyResolved = hadItemSamplesBefore;

                if (_loaded && alreadyResolved)
                {
                    _lastSummary =
                        "already-loaded" +
                        " CharacterCount=" + characterCountBefore +
                        " ItemCount=" + itemCountBefore +
                        " CampaignCharacterType=" + (campaignCharacterType?.FullName ?? "unavailable") +
                        " CampaignCultureType=" + (campaignCultureType?.FullName ?? "unavailable") +
                        " CharacterSamples={" + beforeCharacterSamples + "}" +
                        " ItemSamples={" + beforeItemSamples + "}";
                    return true;
                }

                if (alreadyResolved)
                {
                    _loaded = true;
                    _lastSummary =
                        "resolved-by-existing-catalogs" +
                        " CharacterCount=" + characterCountBefore +
                        " ItemCount=" + itemCountBefore +
                        " CampaignCharacterType=" + (campaignCharacterType?.FullName ?? "unavailable") +
                        " CampaignCultureType=" + (campaignCultureType?.FullName ?? "unavailable") +
                        " CharacterSamples={" + beforeCharacterSamples + "}" +
                        " ItemSamples={" + beforeItemSamples + "}";
                    return true;
                }

                DateTime utcNow = DateTime.UtcNow;
                if (_attempted && utcNow < _nextRetryUtc)
                {
                    _lastSummary =
                        "retry-cooldown" +
                        " Loaded=" + _loaded +
                        " AttemptCount=" + _attemptCount +
                        " NextRetryUtc=" + _nextRetryUtc.ToString("O") +
                        " CharacterCount=" + characterCountBefore +
                        " ItemCount=" + itemCountBefore +
                        " CampaignCharacterType=" + (campaignCharacterType?.FullName ?? "unavailable") +
                        " CampaignCultureType=" + (campaignCultureType?.FullName ?? "unavailable") +
                        " CharacterSamples={" + beforeCharacterSamples + "}" +
                        " ItemSamples={" + beforeItemSamples + "}";
                    return false;
                }

                _attempted = true;
                _attemptCount++;
                _nextRetryUtc = utcNow + RetryCooldown;

                var results = new List<string>();
                ExactCampaignRuntimeItemRegistry.EnsureCraftingSupportLoadedForBootstrap("exact-object-catalog-bootstrap:" + (source ?? "unknown"));

                TryRegisterCampaignTypeIfMissing(
                    objectManager,
                    campaignCharacterType,
                    "NPCCharacter",
                    "NPCCharacters",
                    16u,
                    "NPCCharacter",
                    results);
                TryRegisterCampaignTypeIfMissing(
                    objectManager,
                    campaignCultureType,
                    "Culture",
                    "SPCultures",
                    17u,
                        "Culture",
                        results);

                if (hadItemSamplesBefore)
                {
                    results.Add("ItemCatalogs=skipped-already-resolved");
                }
                else
                {
                    TryLoadMissingItemsXml(objectManager, results);
                    TryLoadXml(objectManager, "EquipmentRosters", results);
                }

                if (canBootstrapCampaignCharacterCatalogs)
                {
                    if (hadCharacterSamplesBefore)
                    {
                        results.Add("CampaignCatalogs=skipped-already-resolved");
                    }
                    else
                    {
                        foreach (string xmlCatalog in CampaignXmlCatalogs)
                            TryLoadXml(objectManager, xmlCatalog, results);
                    }
                }
                else
                {
                    results.Add("CampaignCatalogs=skipped-campaign-types-unavailable");
                }

                TryUnregisterNonReadyObjects(objectManager, results);

                bool hasCharacterSamplesAfter = HasResolvedCharacterSamples(objectManager, out string afterCharacterSamples);
                bool hasItemSamplesAfter = HasResolvedItemSamples(objectManager, out string afterItemSamples);
                int characterCountAfter = TryGetCharacterCount(objectManager);
                int itemCountAfter = TryGetItemCount(objectManager);
                _loaded = hasItemSamplesAfter;
                _lastSummary =
                    "CharacterCountBefore=" + characterCountBefore +
                    " CharacterCountAfter=" + characterCountAfter +
                    " ItemCountBefore=" + itemCountBefore +
                    " ItemCountAfter=" + itemCountAfter +
                    " CampaignCharacterType=" + (campaignCharacterType?.FullName ?? "unavailable") +
                    " CampaignCultureType=" + (campaignCultureType?.FullName ?? "unavailable") +
                    " CharacterSamplesBefore={" + beforeCharacterSamples + "}" +
                    " CharacterSamplesAfter={" + afterCharacterSamples + "}" +
                    " ItemSamplesBefore={" + beforeItemSamples + "}" +
                    " ItemSamplesAfter={" + afterItemSamples + "}" +
                    " Results=[" + string.Join(", ", results) + "]";

                ModLogger.Info(
                    "ExactCampaignObjectCatalogBootstrap: ensured campaign object catalogs for exact runtime. " +
                    "Loaded=" + _loaded +
                    " Source=" + (source ?? "unknown") + " " +
                    _lastSummary);

                return _loaded;
            }
        }

        private static void TryRegisterCampaignTypeIfMissing(
            MBObjectManager objectManager,
            Type objectType,
            string objectNodeName,
            string objectTypeName,
            uint typeId,
            string label,
            List<string> results)
        {
            if (results == null)
                return;

            if (objectManager == null)
            {
                results.Add(label + "=object-manager-null");
                return;
            }

            if (objectType == null)
            {
                results.Add(label + "=campaign-type-unavailable");
                return;
            }

            try
            {
                IList records = GetObjectTypeRecords(objectManager);
                if (records == null)
                {
                    results.Add(label + "=object-type-records-unavailable");
                    return;
                }

                if (TryFindRecordIndex(records, objectNodeName, objectTypeName, objectType, out int exactRecordIndex))
                {
                    results.Add(label + "=already-registered-exact@index:" + exactRecordIndex);
                    return;
                }

                int insertionIndex = ResolveInsertionIndex(records, objectNodeName, objectTypeName);
                RegisterCampaignType(objectManager, objectType, objectNodeName, objectTypeName, typeId);
                ReorderLastRegisteredRecord(records, objectNodeName, objectTypeName, objectType, insertionIndex);

                records = GetObjectTypeRecords(objectManager);
                if (TryFindRecordIndex(records, objectNodeName, objectTypeName, objectType, out int registeredIndex))
                {
                    results.Add(
                        label +
                        "=registered-reflection@index:" + registeredIndex +
                        (insertionIndex >= 0 ? "/preferred-index:" + insertionIndex : string.Empty));
                    return;
                }

                results.Add(label + "=register-verify-failed");
            }
            catch (Exception ex)
            {
                results.Add(label + "=" + ex.GetType().Name);
            }
        }

        private static void RegisterCampaignType(
            MBObjectManager objectManager,
            Type objectType,
            string objectNodeName,
            string objectTypeName,
            uint typeId)
        {
            if (objectManager == null || objectType == null || RegisterTypeMethod == null)
                return;

            MethodInfo registerType = RegisterTypeMethod.MakeGenericMethod(objectType);
            registerType.Invoke(objectManager, new object[] { objectNodeName, objectTypeName, typeId, true, false });
        }

        private static Type TryResolveCampaignType(string fullTypeName)
        {
            if (string.IsNullOrWhiteSpace(fullTypeName))
                return null;

            Type directType = Type.GetType(fullTypeName + ", " + CampaignAssemblyName, throwOnError: false);
            if (directType != null)
                return directType;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly == null)
                    continue;

                AssemblyName assemblyName = assembly.GetName();
                if (!string.Equals(assemblyName?.Name, CampaignAssemblyName, StringComparison.Ordinal))
                    continue;

                Type resolvedType = assembly.GetType(fullTypeName, throwOnError: false, ignoreCase: false);
                if (resolvedType != null)
                    return resolvedType;
            }

            return null;
        }

        private static IList GetObjectTypeRecords(MBObjectManager objectManager)
        {
            if (objectManager == null || ObjectTypeRecordsField == null)
                return null;

            try
            {
                return ObjectTypeRecordsField.GetValue(objectManager) as IList;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryFindRecordIndex(
            IList records,
            string elementName,
            string elementListName,
            Type objectType,
            out int index)
        {
            index = -1;
            if (records == null)
                return false;

            for (int i = 0; i < records.Count; i++)
            {
                object record = records[i];
                if (record == null)
                    continue;

                if (string.Equals(GetRecordElementName(record), elementName, StringComparison.Ordinal) &&
                    string.Equals(GetRecordElementListName(record), elementListName, StringComparison.Ordinal) &&
                    GetRecordObjectClass(record) == objectType)
                {
                    index = i;
                    return true;
                }
            }

            return false;
        }

        private static int ResolveInsertionIndex(IList records, string elementName, string elementListName)
        {
            if (records == null)
                return -1;

            int firstElementNameIndex = FindFirstRecordIndex(
                records,
                record => string.Equals(GetRecordElementName(record), elementName, StringComparison.Ordinal));
            int firstElementListNameIndex = FindFirstRecordIndex(
                records,
                record => string.Equals(GetRecordElementListName(record), elementListName, StringComparison.Ordinal));

            if (firstElementNameIndex >= 0 && firstElementListNameIndex >= 0)
                return Math.Min(firstElementNameIndex, firstElementListNameIndex);

            return Math.Max(firstElementNameIndex, firstElementListNameIndex);
        }

        private static int FindFirstRecordIndex(IList records, Func<object, bool> predicate)
        {
            if (records == null || predicate == null)
                return -1;

            for (int i = 0; i < records.Count; i++)
            {
                object record = records[i];
                if (record != null && predicate(record))
                    return i;
            }

            return -1;
        }

        private static void ReorderLastRegisteredRecord(
            IList records,
            string elementName,
            string elementListName,
            Type objectType,
            int preferredIndex)
        {
            if (records == null || objectType == null)
                return;

            int registeredIndex = -1;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                object record = records[i];
                if (record == null)
                    continue;

                if (string.Equals(GetRecordElementName(record), elementName, StringComparison.Ordinal) &&
                    string.Equals(GetRecordElementListName(record), elementListName, StringComparison.Ordinal) &&
                    GetRecordObjectClass(record) == objectType)
                {
                    registeredIndex = i;
                    break;
                }
            }

            if (registeredIndex < 0 || preferredIndex < 0 || registeredIndex == preferredIndex)
                return;

            object recordToMove = records[registeredIndex];
            records.RemoveAt(registeredIndex);
            if (preferredIndex > registeredIndex)
                preferredIndex--;

            preferredIndex = Math.Max(0, Math.Min(preferredIndex, records.Count));
            records.Insert(preferredIndex, recordToMove);
        }

        private static string GetRecordElementName(object record)
        {
            try
            {
                return ObjectTypeRecordElementNameProperty?.GetValue(record, null) as string;
            }
            catch
            {
                return null;
            }
        }

        private static string GetRecordElementListName(object record)
        {
            try
            {
                return ObjectTypeRecordElementListNameProperty?.GetValue(record, null) as string;
            }
            catch
            {
                return null;
            }
        }

        private static Type GetRecordObjectClass(object record)
        {
            try
            {
                return ObjectTypeRecordObjectClassProperty?.GetValue(record, null) as Type;
            }
            catch
            {
                return null;
            }
        }

        private static void TryLoadXml(MBObjectManager objectManager, string xmlCatalog, List<string> results)
        {
            try
            {
                if (LoadXmlWithGameTypeMethod != null)
                {
                    LoadXmlWithGameTypeMethod.Invoke(objectManager, new object[] { xmlCatalog, false, "EditorGame", true });
                    results.Add(xmlCatalog + "=loaded-editor-filter-bypass");
                    return;
                }

                objectManager.LoadXML(xmlCatalog);
                results.Add(xmlCatalog + "=loaded-default");
            }
            catch (Exception ex)
            {
                results.Add(xmlCatalog + "=" + ex.GetType().Name);
            }
        }

        private static void TryLoadMissingItemsXml(MBObjectManager objectManager, List<string> results)
        {
            if (results == null)
                return;

            if (objectManager == null)
            {
                results.Add("Items=object-manager-null");
                return;
            }

            Dictionary<string, int> existingCraftedWeaponUsageCounts =
                CaptureExistingCraftedWeaponUsageCounts(objectManager);

            try
            {
                XmlDocument mergedItemsDocument = MBObjectManager.GetMergedXmlForManaged(
                    "Items",
                    skipValidation: false,
                    ignoreGameTypeInclusionCheck: true,
                    gameType: "EditorGame");
                ExactCampaignItemCatalogSelection selection =
                    ExactCampaignItemCatalogLoadPolicy.SelectMissingItems(
                        mergedItemsDocument,
                        itemId => TryResolveItem(objectManager, itemId) != null);

                if (selection.SelectedCount > 0)
                    objectManager.LoadXml(selection.Document);

                string craftedUsageInvariant =
                    BuildExistingCraftedWeaponUsageInvariantSummary(
                        objectManager,
                        existingCraftedWeaponUsageCounts);
                results.Add(
                    "Items=loaded-missing-only" +
                    ":Candidates=" + selection.CandidateCount +
                    ":Selected=" + selection.SelectedCount +
                    ":SkippedExisting=" + selection.SkippedExistingCount +
                    ":SkippedDuplicate=" + selection.SkippedDuplicateCount +
                    ":SkippedInvalid=" + selection.SkippedInvalidCount +
                    ":ExistingCraftedUsageCounts=" + craftedUsageInvariant);
            }
            catch (Exception ex)
            {
                // Never fall back to MBObjectManager.LoadXML("Items") here. That
                // deserializes already registered crafted items again and appends
                // duplicate weapon usages to their WeaponComponent.
                results.Add("Items=missing-only-load-failed:" + ex.GetType().Name);
            }
        }

        private static Dictionary<string, int> CaptureExistingCraftedWeaponUsageCounts(MBObjectManager objectManager)
        {
            var usageCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (objectManager == null)
                return usageCounts;

            try
            {
                foreach (ItemObject item in objectManager.GetObjectTypeList<ItemObject>())
                {
                    if (item == null ||
                        !item.IsCraftedWeapon ||
                        string.IsNullOrWhiteSpace(item.StringId))
                    {
                        continue;
                    }

                    usageCounts[item.StringId] = item.Weapons?.Count ?? 0;
                }
            }
            catch
            {
            }

            return usageCounts;
        }

        private static string BuildExistingCraftedWeaponUsageInvariantSummary(
            MBObjectManager objectManager,
            IReadOnlyDictionary<string, int> usageCountsBefore)
        {
            if (objectManager == null || usageCountsBefore == null || usageCountsBefore.Count == 0)
                return "not-applicable";

            var changedItems = new List<string>();
            foreach (KeyValuePair<string, int> usageCountBefore in usageCountsBefore)
            {
                ItemObject item = TryResolveItem(objectManager, usageCountBefore.Key);
                int usageCountAfter = item?.Weapons?.Count ?? -1;
                if (usageCountAfter == usageCountBefore.Value)
                    continue;

                changedItems.Add(
                    usageCountBefore.Key + "=" + usageCountBefore.Value + "->" + usageCountAfter);
                if (changedItems.Count >= 8)
                    break;
            }

            return changedItems.Count == 0
                ? "preserved:" + usageCountsBefore.Count
                : "changed:[" + string.Join(",", changedItems) + "]";
        }

        private static void TryUnregisterNonReadyObjects(MBObjectManager objectManager, List<string> results)
        {
            try
            {
                objectManager.UnregisterNonReadyObjects();
                results.Add("UnregisterNonReadyObjects=ok");
            }
            catch (Exception ex)
            {
                results.Add("UnregisterNonReadyObjects=" + ex.GetType().Name);
            }
        }

        private static bool HasResolvedCharacterSamples(MBObjectManager objectManager, out string samples)
        {
            var parts = new List<string>(SampleCharacterIds.Length);
            bool allResolved = SampleCharacterIds.Length > 0;
            foreach (string sampleCharacterId in SampleCharacterIds)
            {
                BasicCharacterObject character = TryResolveCharacter(objectManager, sampleCharacterId);
                bool resolved = character != null;
                allResolved &= resolved;
                parts.Add(sampleCharacterId + "=" + resolved);
            }

            samples = string.Join(", ", parts);
            return allResolved;
        }

        private static bool HasResolvedItemSamples(MBObjectManager objectManager, out string samples)
        {
            var parts = new List<string>(SampleItemIds.Length);
            bool allResolved = SampleItemIds.Length > 0;
            foreach (string sampleItemId in SampleItemIds)
            {
                ItemObject item = TryResolveItem(objectManager, sampleItemId);
                bool resolved = item != null;
                allResolved &= resolved;
                parts.Add(sampleItemId + "=" + resolved);
            }

            samples = string.Join(", ", parts);
            return allResolved;
        }

        private static int TryGetCharacterCount(MBObjectManager objectManager)
        {
            try
            {
                return objectManager.GetObjectTypeList<BasicCharacterObject>()?.Count ?? -1;
            }
            catch
            {
                return -1;
            }
        }

        private static int TryGetItemCount(MBObjectManager objectManager)
        {
            try
            {
                return objectManager.GetObjectTypeList<ItemObject>()?.Count ?? -1;
            }
            catch
            {
                return -1;
            }
        }

        private static BasicCharacterObject TryResolveCharacter(MBObjectManager objectManager, string characterId)
        {
            if (objectManager == null || string.IsNullOrWhiteSpace(characterId))
                return null;

            try
            {
                return objectManager.GetObject("NPCCharacter", characterId) as BasicCharacterObject ??
                       objectManager.GetObject<BasicCharacterObject>(characterId);
            }
            catch
            {
                return null;
            }
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
