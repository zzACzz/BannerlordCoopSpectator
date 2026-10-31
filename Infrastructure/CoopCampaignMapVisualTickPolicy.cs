namespace CoopSpectator.Infrastructure
{
    internal enum CoopCampaignMapVisualTickAction
    {
        None,
        AdvanceLoading,
        ClearPending,
        UpdateVisuals
    }

    internal static class CoopCampaignMapVisualTickPolicy
    {
        // Current-frame admission, deliberately independent of any past ready
        // frame. Scene loading can restart after the initial presentation.
        internal static CoopCampaignMapVisualTickAction Select(
            bool ownedSceneAvailable, bool activeTopView, bool sceneLoaded,
            bool renderReady, bool probeFailed)
        {
            if (!ownedSceneAvailable || probeFailed)
                return CoopCampaignMapVisualTickAction.None;
            if (!activeTopView)
                return CoopCampaignMapVisualTickAction.ClearPending;
            if (!sceneLoaded)
                return CoopCampaignMapVisualTickAction.AdvanceLoading;
            return renderReady
                ? CoopCampaignMapVisualTickAction.UpdateVisuals
                : CoopCampaignMapVisualTickAction.ClearPending;
        }
    }
}
