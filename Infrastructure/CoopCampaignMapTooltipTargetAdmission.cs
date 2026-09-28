using System;

namespace CoopSpectator.Infrastructure
{
    public static class CoopCampaignMapTooltipTargetAdmission
    {
        // Catalog membership is necessary, not sufficient. Caller must still
        // prove a fresh successful publication, same live campaign/map context,
        // exact native identity and current native visibility before capture.
        public static bool IsAdmitted(string publishedSession, CoopCampaignMapTooltipRequest request,
            CoopCampaignMapPrototypeCatalogEntityState catalog, CoopCampaignMapPrototypeDynamicEntityState dynamicState)
        {
            if (request == null || request.LeaseId == Guid.Empty || catalog == null || dynamicState == null ||
                !CoopCampaignMapTooltipPayloadCodec.IsValidCorrelation(request.SessionId, request.EntityId, request.Generation, 1) ||
                !string.Equals(publishedSession, request.SessionId, StringComparison.Ordinal) ||
                !string.Equals(catalog.EntityId, request.EntityId, StringComparison.Ordinal) ||
                !string.Equals(dynamicState.EntityId, request.EntityId, StringComparison.Ordinal) || !dynamicState.IsVisible) return false;
            string prefix;
            if (catalog.Kind == CoopCampaignMapPrototypeEntityKind.Settlement) prefix = "settlement:";
            else if (CoopCampaignMapPartyBodyPolicy.ShouldRenderBody(catalog.Kind, dynamicState.PartyNameplateFlags)) prefix = "party:";
            else return false;
            return request.EntityId.StartsWith(prefix, StringComparison.Ordinal) && request.EntityId.Length > prefix.Length;
        }
    }
}
