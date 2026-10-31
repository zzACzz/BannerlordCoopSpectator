namespace CoopSpectator.Infrastructure
{
    internal static class CoopCampaignMapInputFocusPolicy
    {
        internal static bool CanRestore(bool topMissionScreen, bool replicaLayerActive, bool mapReady,
            bool conversationActive, bool escapeMenuActive, bool optionsActive, bool inquiryActive,
            bool unclaimedOrMissionSceneFocus)
        {
            return topMissionScreen && replicaLayerActive && mapReady &&
                !conversationActive && !escapeMenuActive && !optionsActive && !inquiryActive &&
                unclaimedOrMissionSceneFocus;
        }
    }
}
