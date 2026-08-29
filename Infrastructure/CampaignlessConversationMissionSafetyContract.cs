namespace CoopSpectator.Infrastructure
{
    internal static class CampaignlessConversationMissionSafetyContract
    {
        public static bool ShouldReturnNull(
            bool hasCampaign,
            bool hasConversationManager)
        {
            return !hasCampaign || !hasConversationManager;
        }
    }
}
