namespace CoopSpectator.Infrastructure
{
    internal readonly struct ExactCampaignSiegePeerPerkSafetyInput
    {
        public ExactCampaignSiegePeerPerkSafetyInput(
            bool isExactCampaignSiegeAssault,
            bool isDedicatedServer,
            bool hasTeam,
            bool hasControlledAgent,
            bool hasCulture,
            int selectedTroopIndex,
            int perkStorageCount,
            int cultureClassCount)
        {
            IsExactCampaignSiegeAssault = isExactCampaignSiegeAssault;
            IsDedicatedServer = isDedicatedServer;
            HasTeam = hasTeam;
            HasControlledAgent = hasControlledAgent;
            HasCulture = hasCulture;
            SelectedTroopIndex = selectedTroopIndex;
            PerkStorageCount = perkStorageCount;
            CultureClassCount = cultureClassCount;
        }

        public bool IsExactCampaignSiegeAssault { get; }
        public bool IsDedicatedServer { get; }
        public bool HasTeam { get; }
        public bool HasControlledAgent { get; }
        public bool HasCulture { get; }
        public int SelectedTroopIndex { get; }
        public int PerkStorageCount { get; }
        public int CultureClassCount { get; }
    }

    internal static class ExactCampaignSiegePeerPerkSafetyContract
    {
        public static bool ShouldSuppressNativePerkResolution(
            ExactCampaignSiegePeerPerkSafetyInput input)
        {
            if (!input.IsExactCampaignSiegeAssault ||
                !input.IsDedicatedServer ||
                !input.HasTeam ||
                input.SelectedTroopIndex < 0)
            {
                return false;
            }

            if (input.PerkStorageCount <= 0 ||
                input.SelectedTroopIndex >= input.PerkStorageCount)
            {
                return true;
            }

            if (input.HasControlledAgent)
                return false;

            return !input.HasCulture ||
                   input.CultureClassCount <= 0 ||
                   input.SelectedTroopIndex >= input.CultureClassCount;
        }
    }
}
