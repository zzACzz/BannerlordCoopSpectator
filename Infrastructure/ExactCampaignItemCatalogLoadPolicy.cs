using System;
using System.Collections.Generic;
using System.Xml;

namespace CoopSpectator.Infrastructure
{
    public enum ExactCampaignCraftingRuntimeDecision
    {
        SkipStandaloneCampaign = 0,
        UsePreloadedCatalog = 1,
        RejectUnavailableCatalog = 2
    }

    public static class ExactCampaignCraftingRuntimeSafetyContract
    {
        public static ExactCampaignCraftingRuntimeDecision Evaluate(
            bool isCampaignRuntime,
            bool isNetworkSessionActive,
            bool isPreloadedCatalogReady)
        {
            if (isCampaignRuntime && !isNetworkSessionActive)
                return ExactCampaignCraftingRuntimeDecision.SkipStandaloneCampaign;

            return isPreloadedCatalogReady
                ? ExactCampaignCraftingRuntimeDecision.UsePreloadedCatalog
                : ExactCampaignCraftingRuntimeDecision.RejectUnavailableCatalog;
        }

        public static bool IsCanonicalCraftingPiece(
            bool belongsToTemplate,
            bool resolvesToSameObject,
            bool isReady,
            bool isValid)
        {
            return belongsToTemplate && resolvesToSameObject && isReady && isValid;
        }

        public static bool ShouldAllowCampaignSmithyPieceSelection(
            bool isCampaignSmithy,
            bool belongsToTemplate,
            bool resolvesToSameObject,
            bool isReady,
            bool isValid)
        {
            return !isCampaignSmithy || IsCanonicalCraftingPiece(
                belongsToTemplate,
                resolvesToSameObject,
                isReady,
                isValid);
        }

        public static bool ShouldRetryRejectedCraftedMirror(bool wasRejected)
        {
            return !wasRejected;
        }

        public static bool ShouldUseSafeWeaponSlotFallback(bool craftedMirrorResolved)
        {
            return !craftedMirrorResolved;
        }
    }

    public sealed class ExactCampaignItemCatalogSelection
    {
        public ExactCampaignItemCatalogSelection(
            XmlDocument document,
            int candidateCount,
            int selectedCount,
            int skippedExistingCount,
            int skippedDuplicateCount,
            int skippedInvalidCount)
        {
            Document = document;
            CandidateCount = candidateCount;
            SelectedCount = selectedCount;
            SkippedExistingCount = skippedExistingCount;
            SkippedDuplicateCount = skippedDuplicateCount;
            SkippedInvalidCount = skippedInvalidCount;
        }

        public XmlDocument Document { get; }

        public int CandidateCount { get; }

        public int SelectedCount { get; }

        public int SkippedExistingCount { get; }

        public int SkippedDuplicateCount { get; }

        public int SkippedInvalidCount { get; }
    }

    public static class ExactCampaignItemCatalogLoadPolicy
    {
        public static ExactCampaignItemCatalogSelection SelectMissingItems(
            XmlDocument mergedItemsDocument,
            Func<string, bool> isItemAlreadyLoaded)
        {
            var selectedDocument = new XmlDocument();
            XmlElement selectedRoot = selectedDocument.CreateElement("Items");
            selectedDocument.AppendChild(selectedRoot);

            int candidateCount = 0;
            int selectedCount = 0;
            int skippedExistingCount = 0;
            int skippedDuplicateCount = 0;
            int skippedInvalidCount = 0;
            var observedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            XmlElement sourceRoot = mergedItemsDocument?.DocumentElement;
            if (sourceRoot == null ||
                !string.Equals(sourceRoot.Name, "Items", StringComparison.Ordinal))
            {
                return new ExactCampaignItemCatalogSelection(
                    selectedDocument,
                    candidateCount,
                    selectedCount,
                    skippedExistingCount,
                    skippedDuplicateCount,
                    skippedInvalidCount);
            }

            foreach (XmlNode sourceNode in sourceRoot.ChildNodes)
            {
                if (!(sourceNode is XmlElement sourceElement) ||
                    (!string.Equals(sourceElement.Name, "Item", StringComparison.Ordinal) &&
                     !string.Equals(sourceElement.Name, "CraftedItem", StringComparison.Ordinal)))
                {
                    continue;
                }

                candidateCount++;
                string itemId = sourceElement.GetAttribute("id")?.Trim();
                if (string.IsNullOrWhiteSpace(itemId))
                {
                    skippedInvalidCount++;
                    continue;
                }

                if (!observedIds.Add(itemId))
                {
                    skippedDuplicateCount++;
                    continue;
                }

                if (isItemAlreadyLoaded != null && isItemAlreadyLoaded(itemId))
                {
                    skippedExistingCount++;
                    continue;
                }

                selectedRoot.AppendChild(selectedDocument.ImportNode(sourceElement, deep: true));
                selectedCount++;
            }

            return new ExactCampaignItemCatalogSelection(
                selectedDocument,
                candidateCount,
                selectedCount,
                skippedExistingCount,
                skippedDuplicateCount,
                skippedInvalidCount);
        }
    }
}
