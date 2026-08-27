namespace CoopSpectator.Infrastructure
{
    internal readonly struct CoopPeerHeroClassSafetyInput
    {
        public CoopPeerHeroClassSafetyInput(
            bool isCoopMission,
            bool isClient,
            bool isServer,
            bool isDedicatedServer,
            bool hasControlledAgent,
            bool hasTeam,
            bool hasActiveTeamSide,
            bool skipTeamCheck,
            int selectedTroopIndex,
            bool hasNativeCultureClassIndex)
        {
            IsCoopMission = isCoopMission;
            IsClient = isClient;
            IsServer = isServer;
            IsDedicatedServer = isDedicatedServer;
            HasControlledAgent = hasControlledAgent;
            HasTeam = hasTeam;
            HasActiveTeamSide = hasActiveTeamSide;
            SkipTeamCheck = skipTeamCheck;
            SelectedTroopIndex = selectedTroopIndex;
            HasNativeCultureClassIndex = hasNativeCultureClassIndex;
        }

        public bool IsCoopMission { get; }
        public bool IsClient { get; }
        public bool IsServer { get; }
        public bool IsDedicatedServer { get; }
        public bool HasControlledAgent { get; }
        public bool HasTeam { get; }
        public bool HasActiveTeamSide { get; }
        public bool SkipTeamCheck { get; }
        public int SelectedTroopIndex { get; }
        public bool HasNativeCultureClassIndex { get; }
    }

    internal static class CoopPeerHeroClassSafetyContract
    {
        public static bool ShouldResolveCanonicalHeroClass(
            CoopPeerHeroClassSafetyInput input)
        {
            bool isGuardedNetworkContext =
                input.IsClient ||
                (input.IsServer && input.IsDedicatedServer);

            if (!input.IsCoopMission ||
                !isGuardedNetworkContext ||
                input.HasControlledAgent ||
                input.SelectedTroopIndex < 0)
            {
                return false;
            }

            if (!input.SkipTeamCheck &&
                (!input.HasTeam || !input.HasActiveTeamSide))
            {
                return false;
            }

            return !input.HasNativeCultureClassIndex;
        }
    }

    internal readonly struct ExactCampaignSiegePeerPerkSafetyInput
    {
        public ExactCampaignSiegePeerPerkSafetyInput(
            bool isCoopMission,
            bool isDedicatedServer,
            bool hasTeam,
            bool hasControlledAgent,
            bool hasCulture,
            int selectedTroopIndex,
            int perkStorageCount,
            int cultureClassCount)
        {
            IsCoopMission = isCoopMission;
            IsDedicatedServer = isDedicatedServer;
            HasTeam = hasTeam;
            HasControlledAgent = hasControlledAgent;
            HasCulture = hasCulture;
            SelectedTroopIndex = selectedTroopIndex;
            PerkStorageCount = perkStorageCount;
            CultureClassCount = cultureClassCount;
        }

        public bool IsCoopMission { get; }
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
            if (!input.IsCoopMission ||
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
