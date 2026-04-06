using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Infrastructure
{
    public static class ExactCampaignObjectCatalogBootstrap
    {
        private static readonly object Sync = new object();
        private static readonly MethodInfo LoadXmlWithGameTypeMethod =
            typeof(MBObjectManager).GetMethod(
                "LoadXML",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(string), typeof(bool), typeof(string), typeof(bool) },
                null);
        private static readonly string[] XmlCatalogs =
        {
            "Items",
            "EquipmentRosters",
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

        private static bool _loaded;
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
                if (_loaded && hadItemSamplesBefore)
                {
                    _lastSummary =
                        "already-loaded" +
                        " CharacterCount=" + characterCountBefore +
                        " ItemCount=" + itemCountBefore +
                        " CharacterSamples={" + beforeCharacterSamples + "}" +
                        " ItemSamples={" + beforeItemSamples + "}";
                    return true;
                }

                var results = new List<string>();
                TryRegisterTypeIfMissing<BasicCharacterObject>(objectManager, "NPCCharacter", "NPCCharacters", 43u, "NPCCharacter", results);
                TryRegisterTypeIfMissing<BasicCultureObject>(objectManager, "Culture", "SPCultures", 17u, "Culture", results);

                foreach (string xmlCatalog in XmlCatalogs)
                    TryLoadXml(objectManager, xmlCatalog, results);

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

        private static void TryRegisterTypeIfMissing<TObject>(
            MBObjectManager objectManager,
            string objectNodeName,
            string objectTypeName,
            uint typeId,
            string label,
            List<string> results)
            where TObject : MBObjectBase
        {
            try
            {
                if (IsTypeRegistered<TObject>(objectManager))
                {
                    results.Add(label + "=already-registered");
                    return;
                }

                objectManager.RegisterType<TObject>(objectNodeName, objectTypeName, typeId);
                results.Add(label + "=registered");
            }
            catch (Exception ex)
            {
                results.Add(label + "=" + ex.GetType().Name);
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
            bool anyResolved = false;
            foreach (string sampleCharacterId in SampleCharacterIds)
            {
                BasicCharacterObject character = TryResolveCharacter(objectManager, sampleCharacterId);
                bool resolved = character != null;
                anyResolved |= resolved;
                parts.Add(sampleCharacterId + "=" + resolved);
            }

            samples = string.Join(", ", parts);
            return anyResolved;
        }

        private static bool HasResolvedItemSamples(MBObjectManager objectManager, out string samples)
        {
            var parts = new List<string>(SampleItemIds.Length);
            bool anyResolved = false;
            foreach (string sampleItemId in SampleItemIds)
            {
                ItemObject item = TryResolveItem(objectManager, sampleItemId);
                bool resolved = item != null;
                anyResolved |= resolved;
                parts.Add(sampleItemId + "=" + resolved);
            }

            samples = string.Join(", ", parts);
            return anyResolved;
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

        private static bool IsTypeRegistered<TObject>(MBObjectManager objectManager)
            where TObject : MBObjectBase
        {
            if (objectManager == null)
                return false;

            try
            {
                return objectManager.GetObjectTypeList<TObject>() != null;
            }
            catch
            {
                return false;
            }
        }

        private static BasicCharacterObject TryResolveCharacter(MBObjectManager objectManager, string characterId)
        {
            if (objectManager == null || string.IsNullOrWhiteSpace(characterId))
                return null;

            try
            {
                return objectManager.GetObject<BasicCharacterObject>(characterId);
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
