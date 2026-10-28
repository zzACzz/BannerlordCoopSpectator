using System;

namespace CoopSpectator.Infrastructure
{
    public static class CoopCampaignMapTooltipTargetAdmission
    {
        public static bool ContainsSettlementHint(CoopCampaignMapMenuPresentation menu, string token) =>
            menu != null && !menu.IsEncounterMenu && menu.Aftermath == null && menu.SettlementOverlay != null &&
            menu.SettlementOverlay.IsValid() && CoopCampaignSettlementMenuPresentation.IsHintToken(token) &&
            System.Linq.Enumerable.Count(menu.SettlementOverlay.HintTokens.Values, value => value == token) == 1;
        public static bool IsSettlementHintAdmitted(string session, CoopCampaignMapTooltipRequest request,
            CoopCampaignMapMenuPresentation menu) => request != null && request.LeaseId != Guid.Empty &&
            string.Equals(session, request.SessionId, StringComparison.Ordinal) &&
            CoopCampaignMapTooltipPayloadCodec.IsValidCorrelation(request.SessionId, request.EntityId, request.Generation, 1) &&
            ContainsSettlementHint(menu, request.EntityId);
        public static bool IsAftermathAdmitted(string session, CoopCampaignMapTooltipRequest request,
            CoopCampaignMapMenuPresentation menu) => request != null && request.LeaseId != Guid.Empty &&
            string.Equals(session, request.SessionId, StringComparison.Ordinal) &&
            CoopCampaignMapTooltipPayloadCodec.IsValidCorrelation(request.SessionId, request.EntityId, request.Generation, 1) &&
            (CoopCampaignAftermathInspectionTarget.IsDisclosed(menu?.Aftermath, request.EntityId) ||
             CoopCampaignAftermathLoadoutTarget.IsDisclosed(menu?.Aftermath, request.EntityId) ||
             (!request.IsExtended && CoopCampaignAftermathTroopTarget.IsDisclosed(menu?.Aftermath, request.EntityId)));
        public static bool ContainsBarHint(CoopCampaignMapBarPresentation bar, string token)
        {
            if (bar == null || !bar.IsEnabled || !bar.IsValid() ||
                !CoopCampaignMapBarHintIdentity<object>.IsToken(token)) return false;
            int matches = 0;
            bool available = false;
            foreach (var item in bar.PrimaryInfoItems) if (item.HintToken == token)
            { matches++; available = bar.IsInfoBarEnabled; }
            foreach (var item in bar.SecondaryInfoItems) if (item.HintToken == token)
            { matches++; available = bar.IsInfoBarEnabled; }
            foreach (var item in bar.ControlHints) if (item.HintToken == token)
            { matches++; available = item.IsAvailable; }
            return matches == 1 && available;
        }
        public static bool IsBarHintAdmitted(string session, CoopCampaignMapTooltipRequest request,
            CoopCampaignMapBarPresentation bar) => request != null && request.LeaseId != Guid.Empty &&
            string.Equals(session, request.SessionId, StringComparison.Ordinal) &&
            CoopCampaignMapTooltipPayloadCodec.IsValidCorrelation(request.SessionId, request.EntityId, request.Generation, 1) &&
            ContainsBarHint(bar, request.EntityId);

        public static bool ContainsEncounter(CoopCampaignMapMenuPresentation menu, string id)
        {
            if (!CoopCampaignMapEncounterParty.IsTooltipId(id) || menu?.IsEncounterMenu != true ||
                menu.Encounter == null || !menu.Encounter.IsValid()) return false;
            int matches = 0;
            foreach (var entry in menu.Encounter.Attackers)
                if (string.Equals(entry.TooltipId, id, StringComparison.Ordinal)) matches++;
            foreach (var entry in menu.Encounter.Defenders)
                if (string.Equals(entry.TooltipId, id, StringComparison.Ordinal)) matches++;
            return matches == 1;
        }

        public static bool IsEncounterAdmitted(string session, CoopCampaignMapTooltipRequest request,
            CoopCampaignMapMenuPresentation menu) => request != null && request.LeaseId != Guid.Empty &&
            string.Equals(session, request.SessionId, StringComparison.Ordinal) &&
            CoopCampaignMapTooltipPayloadCodec.IsValidCorrelation(request.SessionId, request.EntityId, request.Generation, 1) &&
            ContainsEncounter(menu, request.EntityId);

        public static bool ContainsPlunder(CoopCampaignMapMenuPresentation menu, string id)
        {
            if (string.IsNullOrEmpty(id) || !CoopCampaignMapMenuPlunder.IsTooltipId(id) ||
                menu?.PlunderItems == null || menu.PlunderItems.Count > CoopCampaignMapMenuPresentation.MaxPlunderItems) return false;
            int matches = 0;
            foreach (var item in menu.PlunderItems)
                if (item != null && string.Equals(item.TooltipId, id, StringComparison.Ordinal)) matches++;
            return matches == 1;
        }

        public static bool IsPlunderAdmitted(string session, CoopCampaignMapTooltipRequest request,
            CoopCampaignMapMenuPresentation menu) => request != null && request.LeaseId != Guid.Empty &&
            string.Equals(session, request.SessionId, StringComparison.Ordinal) &&
            CoopCampaignMapTooltipPayloadCodec.IsValidCorrelation(request.SessionId, request.EntityId, request.Generation, 1) &&
            ContainsPlunder(menu, request.EntityId);

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
            else if (CoopCampaignMapPartyPickPolicy.ShouldAllowPick(catalog.Kind, dynamicState.PartyNameplateFlags)) prefix = "party:";
            else return false;
            return request.EntityId.StartsWith(prefix, StringComparison.Ordinal) && request.EntityId.Length > prefix.Length;
        }
    }
}
