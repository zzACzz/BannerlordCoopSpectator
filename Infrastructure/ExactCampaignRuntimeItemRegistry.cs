using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Infrastructure
{
    public static class ExactCampaignRuntimeItemRegistry
    {
        private sealed class ExactItemDefinition
        {
            public string ItemId { get; set; }
            public string NodeName { get; set; }
            public string XmlPath { get; set; }
            public string ModuleId { get; set; }
        }

        private sealed class CraftedItemDependencySet
        {
            public string TemplateId { get; set; }
            public string ModifierGroupId { get; set; }
            public List<string> PieceIds { get; } = new List<string>();
        }

        private static readonly object Sync = new object();
        private static readonly string[] SourceModuleIds =
        {
            "SandBoxCore",
            "SandBox",
            "Native",
            "StoryMode"
        };

        private static readonly Dictionary<string, ExactItemDefinition> DefinitionsById =
            new Dictionary<string, ExactItemDefinition>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, XmlDocument> XmlDocumentsByPath =
            new Dictionary<string, XmlDocument>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Dictionary<string, List<string>>> WeaponDescriptionAvailablePieceIdsByDescriptionId =
            new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> LoadedExactItemIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static bool _indexBuilt;
        private static string _indexSummary = "not-built";
        private static bool _craftingSupportLoaded;
        private static string _craftingSupportSummary = "not-loaded";

        public static void EnsureLoadedFromState(BattleRuntimeState runtimeState, string source)
        {
            if (!ExperimentalFeatures.EnableExactCampaignRuntimeItemRegistry || runtimeState == null)
                return;

            MBObjectManager objectManager = Game.Current?.ObjectManager ?? MBObjectManager.Instance;
            if (objectManager == null)
                return;

            lock (Sync)
            {
                EnsureIndexBuilt();

                List<string> requestedItemIds = CollectHeroEquipmentItemIds(runtimeState)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(itemId => itemId, StringComparer.Ordinal)
                    .ToList();
                if (requestedItemIds.Count == 0)
                    return;

                int itemCountBefore = TryGetItemCount(objectManager);
                List<string> missingBefore = requestedItemIds
                    .Where(itemId => TryResolveItem(objectManager, itemId) == null)
                    .ToList();

                var loadedThisPass = new List<string>();
                var unresolvedThisPass = new List<string>();
                foreach (string itemId in missingBefore)
                {
                    if (!DefinitionsById.TryGetValue(itemId, out ExactItemDefinition definition))
                    {
                        unresolvedThisPass.Add(itemId + "=definition-missing");
                        continue;
                    }

                    if (TryRegisterExactItem(objectManager, definition, out string successSummary, out string failureSummary))
                    {
                        if (!string.IsNullOrWhiteSpace(successSummary))
                            loadedThisPass.Add(successSummary);
                    }
                    else if (!string.IsNullOrWhiteSpace(failureSummary))
                    {
                        unresolvedThisPass.Add(failureSummary);
                    }
                }

                TryUnregisterNonReadyObjects(objectManager);

                int itemCountAfter = TryGetItemCount(objectManager);
                List<string> missingAfter = requestedItemIds
                    .Where(itemId => TryResolveItem(objectManager, itemId) == null)
                    .ToList();

                ModLogger.Info(
                    "ExactCampaignRuntimeItemRegistry: ensured exact campaign item availability for hero equipment. " +
                    "Source=" + (source ?? "unknown") +
                    " Requested=" + requestedItemIds.Count +
                    " MissingBefore=" + missingBefore.Count +
                    " LoadedThisPass=" + loadedThisPass.Count +
                    " MissingAfter=" + missingAfter.Count +
                    " ItemCountBefore=" + itemCountBefore +
                    " ItemCountAfter=" + itemCountAfter +
                    " IndexSummary={" + _indexSummary + "}");

                if (loadedThisPass.Count > 0)
                {
                    ModLogger.Info(
                        "ExactCampaignRuntimeItemRegistry: loaded exact hero equipment item ids = [" +
                        string.Join(", ", loadedThisPass) +
                        "].");
                }

                if (unresolvedThisPass.Count > 0 || missingAfter.Count > 0)
                {
                    var unresolvedSummary = new List<string>(unresolvedThisPass);
                    foreach (string unresolvedItemId in missingAfter)
                    {
                        if (!unresolvedSummary.Any(entry => entry.StartsWith(unresolvedItemId + "=", StringComparison.OrdinalIgnoreCase)))
                            unresolvedSummary.Add(unresolvedItemId + "=still-unresolved-after-load");
                    }

                    ModLogger.Info(
                        "ExactCampaignRuntimeItemRegistry: unresolved exact hero equipment item ids = [" +
                        string.Join(", ", unresolvedSummary.OrderBy(entry => entry, StringComparer.Ordinal)) +
                        "].");
                }
            }
        }

        public static void Reset(string reason)
        {
            lock (Sync)
            {
                ModLogger.Info(
                    "ExactCampaignRuntimeItemRegistry: preserving loaded exact items across snapshot clear. " +
                    "Reason=" + (reason ?? "unknown") +
                    " LoadedExactItems=" + LoadedExactItemIds.Count);
            }
        }

        private static void EnsureIndexBuilt()
        {
            if (_indexBuilt)
                return;

            var scannedFiles = new List<string>();
            int duplicateCount = 0;

            foreach (string moduleId in SourceModuleIds)
            {
                string itemsDirectory = ModulePathHelper.GetSiblingModuleDataFilePath(moduleId, "items");
                if (string.IsNullOrWhiteSpace(itemsDirectory) || !Directory.Exists(itemsDirectory))
                    continue;

                foreach (string xmlPath in Directory.GetFiles(itemsDirectory, "*.xml", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    int indexedInFile = IndexDefinitionsFromXml(moduleId, xmlPath, out int duplicatesInFile);
                    scannedFiles.Add(Path.GetFileName(xmlPath) + "=" + indexedInFile);
                    duplicateCount += duplicatesInFile;
                }
            }

            _indexBuilt = true;
            _indexSummary =
                "IndexedItems=" + DefinitionsById.Count +
                " DuplicateIdsIgnored=" + duplicateCount +
                " Files=[" + string.Join(", ", scannedFiles) + "]";

            ModLogger.Info(
                "ExactCampaignRuntimeItemRegistry: built exact campaign item index. " +
                _indexSummary);
        }

        private static int IndexDefinitionsFromXml(string moduleId, string xmlPath, out int duplicatesInFile)
        {
            duplicatesInFile = 0;
            XmlDocument xmlDocument = GetOrLoadXmlDocument(xmlPath);
            if (xmlDocument?.DocumentElement == null)
                return 0;

            int indexedInFile = 0;
            XmlNodeList nodes = xmlDocument.DocumentElement.ChildNodes;
            if (nodes == null)
                return 0;

            foreach (XmlNode node in nodes)
            {
                if (node?.NodeType != XmlNodeType.Element || node.Attributes == null)
                    continue;

                XmlAttribute idAttribute = node.Attributes["id"];
                if (idAttribute == null || string.IsNullOrWhiteSpace(idAttribute.Value))
                    continue;

                if (!string.Equals(node.Name, "Item", StringComparison.Ordinal) &&
                    !string.Equals(node.Name, "CraftedItem", StringComparison.Ordinal))
                {
                    continue;
                }

                if (DefinitionsById.ContainsKey(idAttribute.Value))
                {
                    duplicatesInFile++;
                    continue;
                }

                DefinitionsById[idAttribute.Value] = new ExactItemDefinition
                {
                    ItemId = idAttribute.Value,
                    NodeName = node.Name,
                    XmlPath = xmlPath,
                    ModuleId = moduleId
                };
                indexedInFile++;
            }

            return indexedInFile;
        }

        private static XmlDocument GetOrLoadXmlDocument(string xmlPath)
        {
            if (string.IsNullOrWhiteSpace(xmlPath))
                return null;

            if (XmlDocumentsByPath.TryGetValue(xmlPath, out XmlDocument existingDocument))
                return existingDocument;

            try
            {
                var xmlDocument = new XmlDocument();
                xmlDocument.Load(xmlPath);
                XmlDocumentsByPath[xmlPath] = xmlDocument;
                return xmlDocument;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "ExactCampaignRuntimeItemRegistry: failed to load source xml document. " +
                    "XmlPath=" + xmlPath +
                    " Error=" + ex.GetType().Name + ":" + ex.Message);
                XmlDocumentsByPath[xmlPath] = null;
                return null;
            }
        }

        private static bool TryRegisterExactItem(
            MBObjectManager objectManager,
            ExactItemDefinition definition,
            out string successSummary,
            out string failureSummary)
        {
            successSummary = null;
            failureSummary = null;
            if (objectManager == null || definition == null || string.IsNullOrWhiteSpace(definition.ItemId))
            {
                failureSummary = (definition?.ItemId ?? "null") + "=invalid-definition";
                return false;
            }

            ItemObject directItem = TryResolveItem(objectManager, definition.ItemId);
            if (directItem != null)
            {
                LoadedExactItemIds.Add(definition.ItemId);
                successSummary =
                    definition.ItemId + "=" + directItem.StringId +
                    "(already-direct,node:" + definition.NodeName +
                    ",file:" + Path.GetFileName(definition.XmlPath) + ")";
                return true;
            }

            XmlDocument sourceDocument = GetOrLoadXmlDocument(definition.XmlPath);
            if (sourceDocument?.DocumentElement == null)
            {
                failureSummary = definition.ItemId + "=source-document-missing";
                return false;
            }

            XmlNode sourceNode = FindItemNodeById(sourceDocument, definition.ItemId);
            if (sourceNode == null)
            {
                failureSummary =
                    definition.ItemId + "=node-missing@" + Path.GetFileName(definition.XmlPath);
                return false;
            }

            CraftedItemDependencySet craftedDependencies = null;
            bool isCraftedItemNode = string.Equals(sourceNode.Name, "CraftedItem", StringComparison.Ordinal);
            if (isCraftedItemNode)
            {
                EnsureCraftingSupportLoaded(objectManager);
                craftedDependencies = TryCollectCraftedItemDependencies(sourceNode);
            }

            try
            {
                XmlDocument singleItemDocument = BuildSingleItemDocument(sourceNode);
                objectManager.LoadXml(singleItemDocument);
            }
            catch (Exception ex)
            {
                failureSummary =
                    definition.ItemId + "=load-failed@" + Path.GetFileName(definition.XmlPath) +
                    ":" + ex.GetType().Name +
                    BuildCraftedDependencyStatusSuffix(objectManager, craftedDependencies);
                return false;
            }

            ItemObject exactItem = TryResolveItem(objectManager, definition.ItemId);
            bool manualCraftedFallbackUsed = false;
            string manualSummary = null;
            string manualFailure = null;
            if (exactItem == null)
            {
                if (isCraftedItemNode &&
                    TryRegisterCraftedItemManually(objectManager, definition, sourceNode, craftedDependencies, out manualSummary, out manualFailure))
                {
                    exactItem = TryResolveItem(objectManager, definition.ItemId);
                    manualCraftedFallbackUsed = exactItem != null;
                    if (exactItem == null)
                    {
                        failureSummary =
                            definition.ItemId + "=manual-crafted-registered-but-not-direct@" + Path.GetFileName(definition.XmlPath) +
                            "/node:" + definition.NodeName +
                            "/manual:" + (manualSummary ?? "none") +
                            BuildCraftedDependencyStatusSuffix(objectManager, craftedDependencies);
                        return false;
                    }
                }
                else
                {
                    failureSummary =
                        definition.ItemId + "=load-no-direct-item@" + Path.GetFileName(definition.XmlPath) +
                        "/node:" + definition.NodeName +
                        (!string.IsNullOrWhiteSpace(manualFailure) ? "/manual:" + manualFailure : string.Empty) +
                        BuildCraftedDependencyStatusSuffix(objectManager, craftedDependencies);
                    return false;
                }
            }

            LoadedExactItemIds.Add(definition.ItemId);
            if (manualCraftedFallbackUsed)
            {
                successSummary =
                    definition.ItemId + "=" + exactItem.StringId +
                    "(manual-crafted,module:" + definition.ModuleId +
                    ",file:" + Path.GetFileName(definition.XmlPath) + ")";
            }
            else
            {
                successSummary =
                    definition.ItemId + "=" + exactItem.StringId +
                "(node:" + definition.NodeName +
                ",module:" + definition.ModuleId +
                ",file:" + Path.GetFileName(definition.XmlPath) + ")";
            }
            return true;
        }

        private static bool TryRegisterCraftedItemManually(
            MBObjectManager objectManager,
            ExactItemDefinition definition,
            XmlNode sourceNode,
            CraftedItemDependencySet dependencySet,
            out string successSummary,
            out string failureSummary)
        {
            successSummary = null;
            failureSummary = null;
            if (objectManager == null || definition == null || sourceNode == null)
            {
                failureSummary = "invalid-manual-crafted-input";
                return false;
            }

            string templateId = dependencySet?.TemplateId ?? sourceNode.Attributes?["crafting_template"]?.Value;
            if (string.IsNullOrWhiteSpace(templateId))
            {
                failureSummary = "crafted-template-missing";
                return false;
            }

            CraftingTemplate craftingTemplate = TryResolveObject<CraftingTemplate>(objectManager, templateId);
            if (craftingTemplate == null)
            {
                failureSummary = "crafted-template-unresolved:" + templateId;
                return false;
            }

            string modifierGroupId = dependencySet?.ModifierGroupId ?? sourceNode.Attributes?["modifier_group"]?.Value;
            ItemModifierGroup itemModifierGroup = null;
            if (!string.IsNullOrWhiteSpace(modifierGroupId))
            {
                itemModifierGroup = TryResolveObject<ItemModifierGroup>(objectManager, modifierGroupId);
                if (itemModifierGroup == null)
                {
                    failureSummary = "crafted-modifier-group-unresolved:" + modifierGroupId;
                    return false;
                }
            }
            else
            {
                itemModifierGroup = craftingTemplate.ItemModifierGroup;
            }

            WeaponDesignElement[] usedPieces = BuildCraftedWeaponDesignElements(objectManager, craftingTemplate, sourceNode, out string pieceFailure);
            if (usedPieces == null)
            {
                failureSummary = pieceFailure ?? "crafted-piece-build-failed";
                return false;
            }

            string weaponDescriptionCanonicalizationSummary =
                CanonicalizeCraftingTemplateWeaponDescriptionAvailablePieces(objectManager, craftingTemplate, usedPieces);

            ItemObject createdItem = null;
            try
            {
                createdItem = objectManager.CreateObject<ItemObject>(definition.ItemId);
                TextObject craftedWeaponName = CreateCraftedWeaponName(sourceNode, definition.ItemId);
                ItemObject generatedItem = Crafting.CreatePreCraftedWeaponOnDeserialize(
                    createdItem,
                    usedPieces,
                    templateId,
                    craftedWeaponName,
                    itemModifierGroup);

                if (generatedItem?.WeaponComponent == null)
                {
                    objectManager.UnregisterObject(createdItem);
                    failureSummary =
                        "crafted-generation-returned-no-weapon-component" +
                        BuildCraftedGenerationCompatibilitySuffix(objectManager, craftingTemplate, usedPieces) +
                        "/compat:canonicalization=" + weaponDescriptionCanonicalizationSummary;
                    return false;
                }

                ApplyCraftedItemMetadata(objectManager, generatedItem, sourceNode);
                successSummary =
                    definition.ItemId + "=" + generatedItem.StringId +
                    "(manual-crafted/template:" + templateId +
                    "/modifier:" + (modifierGroupId ?? "(template-default)") +
                    "/canonicalization:" + weaponDescriptionCanonicalizationSummary + ")";
                return true;
            }
            catch (Exception ex)
            {
                if (createdItem != null)
                {
                    try
                    {
                        objectManager.UnregisterObject(createdItem);
                    }
                    catch
                    {
                    }
                }

                failureSummary = "manual-crafted-failed:" + ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        private static string BuildCraftedGenerationCompatibilitySuffix(
            MBObjectManager objectManager,
            CraftingTemplate craftingTemplate,
            WeaponDesignElement[] usedPieces)
        {
            if (objectManager == null || craftingTemplate == null || usedPieces == null)
                return string.Empty;

            var pieceState = new List<string>();
            foreach (WeaponDesignElement usedPiece in usedPieces)
            {
                if (usedPiece == null)
                {
                    pieceState.Add("null-piece");
                    continue;
                }

                string pieceId = usedPiece.CraftingPiece?.StringId ?? "(null)";
                bool inTemplate = craftingTemplate.Pieces != null &&
                    craftingTemplate.Pieces.Contains(usedPiece.CraftingPiece);
                pieceState.Add(pieceId + "=" + (inTemplate ? "template-piece" : "non-template-instance"));
            }

            var matchingWeaponDescriptions = new List<string>();
            var stringOnlyMatchingWeaponDescriptions = new List<string>();
            var xmlCompatibleWeaponDescriptions = new List<string>();
            if (craftingTemplate.WeaponDescriptions != null)
            {
                HashSet<string> xmlCompatibleWeaponDescriptionIds =
                    GetXmlCompatibleWeaponDescriptionIds(objectManager, craftingTemplate, usedPieces);

                foreach (WeaponDescription weaponDescription in craftingTemplate.WeaponDescriptions)
                {
                    if (weaponDescription?.AvailablePieces == null)
                        continue;

                    int remainingReferenceMatches = usedPieces.Count(usedPiece => usedPiece != null && usedPiece.IsValid);
                    int remainingStringMatches = remainingReferenceMatches;
                    foreach (CraftingPiece availablePiece in weaponDescription.AvailablePieces)
                    {
                        if (availablePiece == null)
                            continue;

                        int pieceTypeIndex = (int)availablePiece.PieceType;
                        if (pieceTypeIndex < 0 || pieceTypeIndex >= usedPieces.Length)
                            continue;

                        WeaponDesignElement usedPiece = usedPieces[pieceTypeIndex];
                        if (usedPiece?.CraftingPiece == availablePiece)
                            remainingReferenceMatches--;

                        if (usedPiece?.CraftingPiece != null &&
                            string.Equals(usedPiece.CraftingPiece.StringId, availablePiece.StringId, StringComparison.OrdinalIgnoreCase))
                        {
                            remainingStringMatches--;
                        }

                        if (remainingReferenceMatches <= 0 && remainingStringMatches <= 0)
                            break;
                    }

                    if (remainingReferenceMatches <= 0)
                        matchingWeaponDescriptions.Add(weaponDescription.StringId);

                    if (remainingReferenceMatches > 0 && remainingStringMatches <= 0)
                        stringOnlyMatchingWeaponDescriptions.Add(weaponDescription.StringId);

                    if (!string.IsNullOrWhiteSpace(weaponDescription.StringId) &&
                        xmlCompatibleWeaponDescriptionIds.Contains(weaponDescription.StringId))
                    {
                        xmlCompatibleWeaponDescriptions.Add(weaponDescription.StringId);
                    }
                }
            }

            return
                "/compat:pieces=[" + string.Join(", ", pieceState) + "]" +
                "/compat:weaponDescriptions=[" + string.Join(", ", matchingWeaponDescriptions) + "]" +
                "/compat:stringOnlyWeaponDescriptions=[" + string.Join(", ", stringOnlyMatchingWeaponDescriptions) + "]" +
                "/compat:xmlWeaponDescriptions=[" + string.Join(", ", xmlCompatibleWeaponDescriptions) + "]";
        }

        private static string CanonicalizeCraftingTemplateWeaponDescriptionAvailablePieces(
            MBObjectManager objectManager,
            CraftingTemplate craftingTemplate,
            WeaponDesignElement[] usedPieces)
        {
            if (objectManager == null ||
                craftingTemplate?.WeaponDescriptions == null ||
                craftingTemplate.Pieces == null ||
                craftingTemplate.Pieces.Count == 0 ||
                usedPieces == null)
            {
                return "skipped";
            }

            FieldInfo availablePiecesField = typeof(WeaponDescription).GetField(
                "_availablePieces",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (availablePiecesField == null)
                return "field-missing";

            var templatePiecesById = craftingTemplate.Pieces
                .Where(piece => piece != null && !string.IsNullOrWhiteSpace(piece.StringId))
                .GroupBy(piece => piece.StringId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            int descriptionCount = 0;
            int rewrittenDescriptions = 0;
            int replacedPieceRefs = 0;
            int appendedPieceRefs = 0;
            HashSet<string> xmlCompatibleWeaponDescriptionIds =
                GetXmlCompatibleWeaponDescriptionIds(objectManager, craftingTemplate, usedPieces);

            foreach (WeaponDescription weaponDescription in craftingTemplate.WeaponDescriptions)
            {
                if (weaponDescription == null)
                    continue;

                descriptionCount++;
                var availablePieces = availablePiecesField.GetValue(weaponDescription) as MBList<CraftingPiece>;
                if (availablePieces == null || availablePieces.Count == 0)
                {
                    availablePieces = new MBList<CraftingPiece>();
                }

                bool rewritten = false;
                var canonicalPieces = new MBList<CraftingPiece>();
                foreach (CraftingPiece availablePiece in availablePieces)
                {
                    if (availablePiece == null)
                        continue;

                    if (templatePiecesById.TryGetValue(availablePiece.StringId, out CraftingPiece templatePiece) &&
                        !ReferenceEquals(templatePiece, availablePiece))
                    {
                        canonicalPieces.Add(templatePiece);
                        rewritten = true;
                        replacedPieceRefs++;
                    }
                    else
                    {
                        canonicalPieces.Add(availablePiece);
                    }
                }

                if (!string.IsNullOrWhiteSpace(weaponDescription.StringId) &&
                    xmlCompatibleWeaponDescriptionIds.Contains(weaponDescription.StringId))
                {
                    foreach (WeaponDesignElement usedPiece in usedPieces)
                    {
                        CraftingPiece templatePiece = usedPiece?.CraftingPiece;
                        if (templatePiece == null || string.IsNullOrWhiteSpace(templatePiece.StringId))
                            continue;

                        bool alreadyPresent = canonicalPieces.Any(piece =>
                            piece != null &&
                            string.Equals(piece.StringId, templatePiece.StringId, StringComparison.OrdinalIgnoreCase));
                        if (!alreadyPresent)
                        {
                            canonicalPieces.Add(templatePiece);
                            rewritten = true;
                            appendedPieceRefs++;
                        }
                    }
                }

                if (rewritten)
                {
                    availablePiecesField.SetValue(weaponDescription, canonicalPieces);
                    rewrittenDescriptions++;
                }
            }

            return
                "descriptions=" + descriptionCount +
                "/rewritten=" + rewrittenDescriptions +
                "/pieceRefs=" + replacedPieceRefs +
                "/appendedPieceRefs=" + appendedPieceRefs +
                "/xmlCompatibleDescriptions=" + xmlCompatibleWeaponDescriptionIds.Count;
        }

        private static HashSet<string> GetXmlCompatibleWeaponDescriptionIds(
            MBObjectManager objectManager,
            CraftingTemplate craftingTemplate,
            WeaponDesignElement[] usedPieces)
        {
            var compatibleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (objectManager == null || craftingTemplate?.WeaponDescriptions == null || usedPieces == null)
                return compatibleIds;

            Dictionary<string, List<string>> availablePieceIdsByDescriptionId =
                GetWeaponDescriptionAvailablePieceIdsByDescriptionId();
            if (availablePieceIdsByDescriptionId == null || availablePieceIdsByDescriptionId.Count == 0)
                return compatibleIds;

            var usedPieceIdsByType = new Dictionary<CraftingPiece.PieceTypes, string>();
            foreach (WeaponDesignElement usedPiece in usedPieces)
            {
                CraftingPiece templatePiece = usedPiece?.CraftingPiece;
                if (templatePiece == null || string.IsNullOrWhiteSpace(templatePiece.StringId))
                    continue;

                usedPieceIdsByType[templatePiece.PieceType] = templatePiece.StringId;
            }

            foreach (WeaponDescription weaponDescription in craftingTemplate.WeaponDescriptions)
            {
                if (weaponDescription == null ||
                    string.IsNullOrWhiteSpace(weaponDescription.StringId) ||
                    !availablePieceIdsByDescriptionId.TryGetValue(weaponDescription.StringId, out List<string> availablePieceIds))
                {
                    continue;
                }

                var availablePieceIdsByType = new Dictionary<CraftingPiece.PieceTypes, HashSet<string>>();
                foreach (string availablePieceId in availablePieceIds)
                {
                    CraftingPiece availablePiece = TryResolveObject<CraftingPiece>(objectManager, availablePieceId);
                    if (availablePiece == null || string.IsNullOrWhiteSpace(availablePiece.StringId))
                        continue;

                    if (!availablePieceIdsByType.TryGetValue(availablePiece.PieceType, out HashSet<string> pieceIdsForType))
                    {
                        pieceIdsForType = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        availablePieceIdsByType[availablePiece.PieceType] = pieceIdsForType;
                    }

                    pieceIdsForType.Add(availablePiece.StringId);
                }

                bool allUsedPiecesCompatible = true;
                foreach (KeyValuePair<CraftingPiece.PieceTypes, string> usedPieceEntry in usedPieceIdsByType)
                {
                    CraftingPiece.PieceTypes pieceType = usedPieceEntry.Key;
                    string usedPieceId = usedPieceEntry.Value;
                    if (!availablePieceIdsByType.TryGetValue(pieceType, out HashSet<string> pieceIdsForType) ||
                        !pieceIdsForType.Contains(usedPieceId))
                    {
                        allUsedPiecesCompatible = false;
                        break;
                    }
                }

                if (allUsedPiecesCompatible)
                    compatibleIds.Add(weaponDescription.StringId);
            }

            return compatibleIds;
        }

        private static Dictionary<string, List<string>> GetWeaponDescriptionAvailablePieceIdsByDescriptionId()
        {
            const string cacheKey = "Native/weapon_descriptions.xml";
            if (WeaponDescriptionAvailablePieceIdsByDescriptionId.TryGetValue(cacheKey, out Dictionary<string, List<string>> cached))
                return cached;

            string xmlPath = ModulePathHelper.GetSiblingModuleDataFilePath("Native", "weapon_descriptions.xml");
            XmlDocument xmlDocument = GetOrLoadXmlDocument(xmlPath);
            if (xmlDocument?.DocumentElement == null)
                return null;

            var descriptionMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (XmlNode node in xmlDocument.DocumentElement.ChildNodes)
            {
                if (!string.Equals(node?.Name, "WeaponDescription", StringComparison.Ordinal) || node.Attributes == null)
                    continue;

                string descriptionId = node.Attributes["id"]?.Value;
                if (string.IsNullOrWhiteSpace(descriptionId))
                    continue;

                XmlNode availablePiecesNode = null;
                foreach (XmlNode childNode in node.ChildNodes)
                {
                    if (string.Equals(childNode?.Name, "AvailablePieces", StringComparison.Ordinal))
                    {
                        availablePiecesNode = childNode;
                        break;
                    }
                }

                if (availablePiecesNode == null)
                    continue;

                var availablePieceIds = new List<string>();
                foreach (XmlNode availablePieceNode in availablePiecesNode.ChildNodes)
                {
                    if (!string.Equals(availablePieceNode?.Name, "AvailablePiece", StringComparison.Ordinal) ||
                        availablePieceNode.Attributes == null)
                    {
                        continue;
                    }

                    string pieceId = availablePieceNode.Attributes["id"]?.Value;
                    if (!string.IsNullOrWhiteSpace(pieceId))
                        availablePieceIds.Add(pieceId);
                }

                descriptionMap[descriptionId] = availablePieceIds;
            }

            WeaponDescriptionAvailablePieceIdsByDescriptionId[cacheKey] = descriptionMap;
            return descriptionMap;
        }

        private static WeaponDesignElement[] BuildCraftedWeaponDesignElements(
            MBObjectManager objectManager,
            CraftingTemplate craftingTemplate,
            XmlNode sourceNode,
            out string failureSummary)
        {
            failureSummary = null;
            if (objectManager == null || sourceNode == null)
            {
                failureSummary = "crafted-pieces-input-invalid";
                return null;
            }

            var usedPieces = new WeaponDesignElement[4];
            XmlNode piecesNode = null;
            foreach (XmlNode childNode in sourceNode.ChildNodes)
            {
                if (string.Equals(childNode?.Name, "Pieces", StringComparison.Ordinal))
                {
                    piecesNode = childNode;
                    break;
                }
            }

            if (piecesNode == null)
            {
                failureSummary = "crafted-pieces-node-missing";
                return null;
            }

            foreach (XmlNode pieceNode in piecesNode.ChildNodes)
            {
                if (!string.Equals(pieceNode?.Name, "Piece", StringComparison.Ordinal) || pieceNode.Attributes == null)
                    continue;

                string pieceId = pieceNode.Attributes["id"]?.Value;
                string pieceTypeName = pieceNode.Attributes["Type"]?.Value;
                if (string.IsNullOrWhiteSpace(pieceId) || string.IsNullOrWhiteSpace(pieceTypeName))
                {
                    failureSummary = "crafted-piece-attr-missing";
                    return null;
                }

                CraftingPiece craftingPiece = TryResolveCraftingPieceForTemplate(objectManager, craftingTemplate, pieceId);
                if (craftingPiece == null)
                {
                    failureSummary = "crafted-piece-unresolved:" + pieceId;
                    return null;
                }

                if (!Enum.TryParse(pieceTypeName, out CraftingPiece.PieceTypes pieceType))
                {
                    failureSummary = "crafted-piece-type-unresolved:" + pieceTypeName;
                    return null;
                }

                WeaponDesignElement designElement = WeaponDesignElement.CreateUsablePiece(craftingPiece);
                string scaleFactorText = pieceNode.Attributes["scale_factor"]?.Value;
                if (!string.IsNullOrWhiteSpace(scaleFactorText) && int.TryParse(scaleFactorText, out int scaleFactor))
                    designElement.SetScale(scaleFactor);

                usedPieces[(int)pieceType] = designElement;
            }

            return usedPieces;
        }

        private static CraftingPiece TryResolveCraftingPieceForTemplate(
            MBObjectManager objectManager,
            CraftingTemplate craftingTemplate,
            string pieceId)
        {
            if (string.IsNullOrWhiteSpace(pieceId))
                return null;

            if (craftingTemplate?.Pieces != null)
            {
                CraftingPiece templatePiece = craftingTemplate.Pieces
                    .FirstOrDefault(piece =>
                        piece != null &&
                        string.Equals(piece.StringId, pieceId, StringComparison.OrdinalIgnoreCase));
                if (templatePiece != null)
                    return templatePiece;
            }

            return TryResolveObject<CraftingPiece>(objectManager, pieceId);
        }

        private static TextObject CreateCraftedWeaponName(XmlNode sourceNode, string fallbackId)
        {
            string rawName = sourceNode?.Attributes?["name"]?.InnerText;
            if (!string.IsNullOrWhiteSpace(rawName))
                return new TextObject(rawName);

            return new TextObject("{=!}" + (fallbackId ?? "crafted_weapon"));
        }

        private static void ApplyCraftedItemMetadata(
            MBObjectManager objectManager,
            ItemObject itemObject,
            XmlNode sourceNode)
        {
            if (objectManager == null || itemObject == null || sourceNode?.Attributes == null)
                return;

            try
            {
                BasicCultureObject culture = objectManager.ReadObjectReferenceFromXml("culture", typeof(BasicCultureObject), sourceNode) as BasicCultureObject;
                if (culture != null)
                    SetPropertyValue(itemObject, "Culture", culture);
            }
            catch
            {
            }

            try
            {
                MethodInfo calculateEffectiveness = typeof(ItemObject).GetMethod("CalculateEffectiveness", BindingFlags.Instance | BindingFlags.NonPublic);
                if (calculateEffectiveness != null)
                {
                    object effectiveness = calculateEffectiveness.Invoke(itemObject, Array.Empty<object>());
                    if (effectiveness is float effectivenessValue)
                        SetPropertyValue(itemObject, "Effectiveness", effectivenessValue);
                }
            }
            catch
            {
            }

            try
            {
                if (sourceNode.Attributes["value"] != null && int.TryParse(sourceNode.Attributes["value"].Value, out int explicitValue))
                {
                    SetPropertyValue(itemObject, "Value", explicitValue);
                }
                else
                {
                    MethodInfo determineValue = typeof(ItemObject).GetMethod("DetermineValue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    determineValue?.Invoke(itemObject, Array.Empty<object>());
                }
            }
            catch
            {
            }

            try
            {
                MethodInfo determineCategory = typeof(ItemObject).GetMethod("DetermineItemCategoryForItem", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                determineCategory?.Invoke(itemObject, Array.Empty<object>());
            }
            catch
            {
            }
        }

        private static void SetPropertyValue(object target, string propertyName, object value)
        {
            if (target == null || string.IsNullOrWhiteSpace(propertyName))
                return;

            PropertyInfo propertyInfo = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            propertyInfo?.SetValue(target, value);
        }

        private static void EnsureCraftingSupportLoaded(MBObjectManager objectManager)
        {
            if (_craftingSupportLoaded || objectManager == null)
                return;

            int craftingPieceCountBefore = TryGetObjectTypeCount<CraftingPiece>(objectManager);
            int craftingTemplateCountBefore = TryGetObjectTypeCount<CraftingTemplate>(objectManager);
            int weaponDescriptionCountBefore = TryGetObjectTypeCount<WeaponDescription>(objectManager);

            var loadedSources = new List<string>();
            LoadSupportXmlDocument(objectManager, "Native", "weapon_descriptions.xml", loadedSources);
            LoadSupportXmlDocument(objectManager, "Native", "crafting_pieces.xml", loadedSources);
            LoadSupportXmlDocument(objectManager, "Native", "mp_crafting_pieces.xml", loadedSources);
            LoadSupportXmlDocument(objectManager, "CoopSpectator", "coopspectator_crafting_pieces.xml", loadedSources);
            LoadSupportXmlDocument(objectManager, "Native", "item_modifiers_groups.xml", loadedSources);
            LoadSupportXmlDocument(objectManager, "Native", "item_modifiers.xml", loadedSources);
            LoadSupportXmlDocument(objectManager, "Native", "crafting_templates.xml", loadedSources);

            TryUnregisterNonReadyObjects(objectManager);

            int craftingPieceCountAfter = TryGetObjectTypeCount<CraftingPiece>(objectManager);
            int craftingTemplateCountAfter = TryGetObjectTypeCount<CraftingTemplate>(objectManager);
            int weaponDescriptionCountAfter = TryGetObjectTypeCount<WeaponDescription>(objectManager);

            _craftingSupportLoaded = true;
            _craftingSupportSummary =
                "LoadedSources=[" + string.Join(", ", loadedSources) + "]" +
                " CraftingPieces=" + craftingPieceCountBefore + "->" + craftingPieceCountAfter +
                " CraftingTemplates=" + craftingTemplateCountBefore + "->" + craftingTemplateCountAfter +
                " WeaponDescriptions=" + weaponDescriptionCountBefore + "->" + weaponDescriptionCountAfter;

            ModLogger.Info(
                "ExactCampaignRuntimeItemRegistry: ensured crafted-item runtime support catalogs. " +
                _craftingSupportSummary);
        }

        private static void LoadSupportXmlDocument(
            MBObjectManager objectManager,
            string moduleId,
            string relativeModuleDataPath,
            List<string> loadedSources)
        {
            if (objectManager == null || string.IsNullOrWhiteSpace(relativeModuleDataPath))
                return;

            string xmlPath = ModulePathHelper.GetSiblingModuleDataFilePath(moduleId, relativeModuleDataPath);
            if (string.IsNullOrWhiteSpace(xmlPath) || !File.Exists(xmlPath))
                return;

            XmlDocument xmlDocument = GetOrLoadXmlDocument(xmlPath);
            if (xmlDocument?.DocumentElement == null)
                return;

            try
            {
                objectManager.LoadXml(xmlDocument);
                loadedSources.Add(moduleId + "/" + relativeModuleDataPath);
            }
            catch (Exception ex)
            {
                loadedSources.Add(moduleId + "/" + relativeModuleDataPath + "=failed:" + ex.GetType().Name);
                ModLogger.Info(
                    "ExactCampaignRuntimeItemRegistry: crafted-item support load failed. " +
                    "Module=" + moduleId +
                    " RelativePath=" + relativeModuleDataPath +
                    " Error=" + ex.GetType().Name + ":" + ex.Message);
            }
        }

        private static XmlDocument BuildSingleItemDocument(XmlNode sourceNode)
        {
            var singleItemDocument = new XmlDocument();
            XmlElement root = singleItemDocument.CreateElement("Items");
            singleItemDocument.AppendChild(root);
            XmlNode importedNode = singleItemDocument.ImportNode(sourceNode, deep: true);
            root.AppendChild(importedNode);
            return singleItemDocument;
        }

        private static XmlNode FindItemNodeById(XmlDocument xmlDocument, string itemId)
        {
            if (xmlDocument?.DocumentElement == null || string.IsNullOrWhiteSpace(itemId))
                return null;

            foreach (XmlNode node in xmlDocument.DocumentElement.ChildNodes)
            {
                if (node?.NodeType != XmlNodeType.Element || node.Attributes == null)
                    continue;

                XmlAttribute idAttribute = node.Attributes["id"];
                if (idAttribute == null)
                    continue;

                if (string.Equals(idAttribute.Value, itemId, StringComparison.OrdinalIgnoreCase))
                    return node;
            }

            return null;
        }

        private static CraftedItemDependencySet TryCollectCraftedItemDependencies(XmlNode sourceNode)
        {
            if (sourceNode?.Attributes == null || !string.Equals(sourceNode.Name, "CraftedItem", StringComparison.Ordinal))
                return null;

            var dependencySet = new CraftedItemDependencySet
            {
                TemplateId = sourceNode.Attributes["crafting_template"]?.Value,
                ModifierGroupId = sourceNode.Attributes["modifier_group"]?.Value
            };

            foreach (XmlNode childNode in sourceNode.ChildNodes)
            {
                if (!string.Equals(childNode?.Name, "Pieces", StringComparison.Ordinal))
                    continue;

                foreach (XmlNode pieceNode in childNode.ChildNodes)
                {
                    if (!string.Equals(pieceNode?.Name, "Piece", StringComparison.Ordinal) || pieceNode.Attributes == null)
                        continue;

                    string pieceId = pieceNode.Attributes["id"]?.Value;
                    if (!string.IsNullOrWhiteSpace(pieceId) &&
                        !dependencySet.PieceIds.Contains(pieceId, StringComparer.OrdinalIgnoreCase))
                    {
                        dependencySet.PieceIds.Add(pieceId);
                    }
                }
            }

            return dependencySet;
        }

        private static string BuildCraftedDependencyStatusSuffix(
            MBObjectManager objectManager,
            CraftedItemDependencySet dependencySet)
        {
            if (objectManager == null || dependencySet == null)
                return string.Empty;

            bool templatePresent = TryResolveObject<CraftingTemplate>(objectManager, dependencySet.TemplateId) != null;
            bool modifierPresent = string.IsNullOrWhiteSpace(dependencySet.ModifierGroupId) ||
                TryResolveObject<ItemModifierGroup>(objectManager, dependencySet.ModifierGroupId) != null;
            var pieceStates = new List<string>();
            foreach (string pieceId in dependencySet.PieceIds.OrderBy(id => id, StringComparer.Ordinal))
            {
                bool piecePresent = TryResolveObject<CraftingPiece>(objectManager, pieceId) != null;
                pieceStates.Add(pieceId + "=" + (piecePresent ? "present" : "missing"));
            }

            return
                "/craftingSupport={" + _craftingSupportSummary + "}" +
                "/template:" + (dependencySet.TemplateId ?? "(none)") + "=" + (templatePresent ? "present" : "missing") +
                "/modifier:" + (dependencySet.ModifierGroupId ?? "(none)") + "=" + (modifierPresent ? "present" : "missing") +
                "/pieces:[" + string.Join(", ", pieceStates) + "]";
        }

        private static IEnumerable<string> CollectHeroEquipmentItemIds(BattleRuntimeState runtimeState)
        {
            if (runtimeState?.EntriesById == null)
                yield break;

            foreach (RosterEntryState heroEntry in runtimeState.EntriesById.Values
                .Where(entry => entry != null && entry.IsHero)
                .OrderBy(entry => entry.EntryId, StringComparer.Ordinal))
            {
                foreach (string itemId in EnumerateHeroEquipmentSlots(heroEntry))
                {
                    if (!string.IsNullOrWhiteSpace(itemId))
                        yield return itemId;
                }
            }
        }

        private static IEnumerable<string> EnumerateHeroEquipmentSlots(RosterEntryState entryState)
        {
            if (entryState == null)
                yield break;

            yield return entryState.CombatItem0Id;
            yield return entryState.CombatItem1Id;
            yield return entryState.CombatItem2Id;
            yield return entryState.CombatItem3Id;
            yield return entryState.CombatHeadId;
            yield return entryState.CombatBodyId;
            yield return entryState.CombatLegId;
            yield return entryState.CombatGlovesId;
            yield return entryState.CombatCapeId;
            yield return entryState.CombatHorseId;
            yield return entryState.CombatHorseHarnessId;
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

        private static void TryUnregisterNonReadyObjects(MBObjectManager objectManager)
        {
            if (objectManager == null)
                return;

            try
            {
                objectManager.UnregisterNonReadyObjects();
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "ExactCampaignRuntimeItemRegistry: UnregisterNonReadyObjects failed: " +
                    ex.Message);
            }
        }

        private static T TryResolveObject<T>(MBObjectManager objectManager, string objectId)
            where T : MBObjectBase
        {
            if (objectManager == null || string.IsNullOrWhiteSpace(objectId))
                return null;

            try
            {
                return objectManager.GetObject<T>(objectId);
            }
            catch
            {
                return null;
            }
        }

        private static int TryGetItemCount(MBObjectManager objectManager)
        {
            if (objectManager == null)
                return -1;

            try
            {
                return objectManager.GetObjectTypeList<ItemObject>()?.Count ?? -1;
            }
            catch
            {
                return -1;
            }
        }

        private static int TryGetObjectTypeCount<T>(MBObjectManager objectManager)
            where T : MBObjectBase
        {
            if (objectManager == null)
                return -1;

            try
            {
                MBReadOnlyList<T> objects = objectManager.GetObjectTypeList<T>();
                return objects?.Count ?? -1;
            }
            catch
            {
                return -1;
            }
        }
    }
}
