using System;

namespace CoopSpectator.Infrastructure
{
    internal static class CoopCampaignMapCustomSettlementPolicy
    {
        internal const string TrainingFieldId = "settlement:tutorial_training_field";
        internal const string TrainingFieldPrefab = "tutorial_training_field";

        internal static bool IsAdmitted(CoopCampaignMapPrototypeEntityState visible,
            CoopCampaignMapPrototypeCatalogEntityState catalog) =>
            visible != null && catalog != null &&
            visible.Kind == CoopCampaignMapPrototypeEntityKind.Settlement &&
            catalog.Kind == CoopCampaignMapPrototypeEntityKind.Settlement &&
            catalog.SettlementKind == CoopCampaignMapPrototypeSettlementKind.Special &&
            string.Equals(visible.EntityId, TrainingFieldId, StringComparison.Ordinal) &&
            string.Equals(catalog.EntityId, visible.EntityId, StringComparison.Ordinal) &&
            visible.SettlementLevelFlags == 0 && catalog.SettlementLevelFlags == 0;
    }
}
