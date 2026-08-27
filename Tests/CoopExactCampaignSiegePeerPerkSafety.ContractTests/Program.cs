using System;
using CoopSpectator.Infrastructure;

internal static class Program
{
    private static int Main()
    {
        try
        {
            ValidateDedicatedServerHeroClassMismatchIsCanonicalized();
            ValidateClientHeroClassMismatchIsCanonicalized();
            ValidateValidHeroClassStatesRemainNative();
            ValidateHeroClassContextGatesRemainNative();
            ValidateOrdinaryBattleCampaignCultureMismatchIsSuppressed();
            ValidateHideoutCampaignCultureMismatchIsSuppressed();
            ValidateDetachedInvalidPeerStateIsSuppressed();
            ValidatePerkStorageBoundsAreProtected();
            ValidateValidNativeStatesRemainNative();
            ValidateContextGatesRemainNative();
            ValidateRepeatedDecisionIsIdempotent();
            Console.WriteLine("Dedicated coop peer perk and hero-class safety contract tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void ValidateDedicatedServerHeroClassMismatchIsCanonicalized()
    {
        AssertCanonicalHeroClass(
            CreateHeroClassInput(hasNativeCultureClassIndex: false),
            "A detached dedicated-server coop peer with a campaign-culture identity mismatch must use canonical hero-class resolution.");
    }

    private static void ValidateClientHeroClassMismatchIsCanonicalized()
    {
        AssertCanonicalHeroClass(
            CreateHeroClassInput(
                isClient: true,
                isServer: false,
                isDedicatedServer: false,
                hasNativeCultureClassIndex: false),
            "The existing detached-client mismatch path must continue to use canonical hero-class resolution.");
    }

    private static void ValidateValidHeroClassStatesRemainNative()
    {
        AssertNativeHeroClass(
            CreateHeroClassInput(hasNativeCultureClassIndex: true),
            "A peer with a valid native culture class index must remain native.");
        AssertNativeHeroClass(
            CreateHeroClassInput(
                hasControlledAgent: true,
                hasNativeCultureClassIndex: false),
            "A peer with a controlled agent must remain on native character-based resolution.");
    }

    private static void ValidateHeroClassContextGatesRemainNative()
    {
        AssertNativeHeroClass(
            CreateHeroClassInput(
                isCoopMission: false,
                hasNativeCultureClassIndex: false),
            "A non-coop mission must not enter canonical hero-class resolution.");
        AssertNativeHeroClass(
            CreateHeroClassInput(
                isClient: false,
                isServer: true,
                isDedicatedServer: false,
                hasNativeCultureClassIndex: false),
            "A non-dedicated server must not enter the new server branch.");
        AssertNativeHeroClass(
            CreateHeroClassInput(
                hasTeam: false,
                hasActiveTeamSide: false,
                hasNativeCultureClassIndex: false),
            "A peer without an active team must remain behind the native team gate.");
        AssertNativeHeroClass(
            CreateHeroClassInput(
                selectedTroopIndex: -1,
                hasNativeCultureClassIndex: false),
            "A negative selected troop index must remain behind the native index gate.");
        AssertCanonicalHeroClass(
            CreateHeroClassInput(
                hasTeam: false,
                hasActiveTeamSide: false,
                skipTeamCheck: true,
                hasNativeCultureClassIndex: false),
            "An explicit skipped team check must still allow safe canonical resolution.");
    }

    private static void ValidateOrdinaryBattleCampaignCultureMismatchIsSuppressed()
    {
        AssertSuppress(
            CreateInput(
                hasControlledAgent: false,
                selectedTroopIndex: 0,
                perkStorageCount: 16,
                cultureClassCount: 0),
            "An ordinary battle peer with a campaign-culture identity mismatch must not enter native perk resolution.");
    }

    private static void ValidateHideoutCampaignCultureMismatchIsSuppressed()
    {
        AssertSuppress(
            CreateInput(
                hasControlledAgent: false,
                selectedTroopIndex: 0,
                perkStorageCount: 16,
                cultureClassCount: 0),
            "A hideout peer with a campaign-culture identity mismatch must not enter native perk resolution.");
    }

    private static void ValidateDetachedInvalidPeerStateIsSuppressed()
    {
        AssertSuppress(
            CreateInput(hasControlledAgent: false, hasCulture: false, cultureClassCount: 0),
            "A detached coop peer without culture must not enter native perk resolution.");
        AssertSuppress(
            CreateInput(hasControlledAgent: false, hasCulture: true, cultureClassCount: 8, selectedTroopIndex: 8),
            "A detached peer index outside its culture class list must not enter native perk resolution.");
        AssertSuppress(
            CreateInput(hasControlledAgent: false, hasCulture: true, cultureClassCount: 0),
            "A detached peer with an empty culture class list must not enter native perk resolution.");
    }

    private static void ValidatePerkStorageBoundsAreProtected()
    {
        AssertSuppress(
            CreateInput(selectedTroopIndex: 16, perkStorageCount: 16),
            "An index outside the native perk storage must be suppressed.");
        AssertSuppress(
            CreateInput(hasControlledAgent: true, selectedTroopIndex: 16, perkStorageCount: 16),
            "A controlled agent cannot make an out-of-range native perk-storage index safe.");
        AssertSuppress(
            CreateInput(perkStorageCount: 0),
            "Missing native perk storage must fail closed in a dedicated coop mission.");
    }

    private static void ValidateValidNativeStatesRemainNative()
    {
        AssertNative(
            CreateInput(hasControlledAgent: false, hasCulture: true, selectedTroopIndex: 8, cultureClassCount: 9),
            "A detached peer with a valid culture index must use native perk resolution.");
        AssertNative(
            CreateInput(hasControlledAgent: true, hasCulture: false, selectedTroopIndex: 8),
            "A live controlled agent with a valid perk index must resolve its class natively.");
    }

    private static void ValidateContextGatesRemainNative()
    {
        AssertNative(
            CreateInput(isCoopMission: false, hasControlledAgent: false, hasCulture: false),
            "A non-coop mission must remain native.");
        AssertNative(
            CreateInput(isDedicatedServer: false, hasControlledAgent: false, hasCulture: false),
            "A client or listen server must not enter the new dedicated-server branch.");
        AssertNative(
            CreateInput(hasTeam: false, hasControlledAgent: false, hasCulture: false),
            "A peer without a team is already rejected by the native SelectedPerks gate.");
        AssertNative(
            CreateInput(selectedTroopIndex: -1, hasControlledAgent: false, hasCulture: false),
            "A negative selected troop index is already rejected by the native SelectedPerks gate.");
    }

    private static void ValidateRepeatedDecisionIsIdempotent()
    {
        ExactCampaignSiegePeerPerkSafetyInput input =
            CreateInput(hasControlledAgent: false, hasCulture: false);
        bool first = ExactCampaignSiegePeerPerkSafetyContract
            .ShouldSuppressNativePerkResolution(input);
        bool second = ExactCampaignSiegePeerPerkSafetyContract
            .ShouldSuppressNativePerkResolution(input);
        Assert(first && second, "Repeated invalid-state evaluation must remain safely suppressed.");
    }

    private static ExactCampaignSiegePeerPerkSafetyInput CreateInput(
        bool isCoopMission = true,
        bool isDedicatedServer = true,
        bool hasTeam = true,
        bool hasControlledAgent = false,
        bool hasCulture = true,
        int selectedTroopIndex = 8,
        int perkStorageCount = 16,
        int cultureClassCount = 9)
    {
        return new ExactCampaignSiegePeerPerkSafetyInput(
            isCoopMission,
            isDedicatedServer,
            hasTeam,
            hasControlledAgent,
            hasCulture,
            selectedTroopIndex,
            perkStorageCount,
            cultureClassCount);
    }

    private static CoopPeerHeroClassSafetyInput CreateHeroClassInput(
        bool isCoopMission = true,
        bool isClient = false,
        bool isServer = true,
        bool isDedicatedServer = true,
        bool hasControlledAgent = false,
        bool hasTeam = true,
        bool hasActiveTeamSide = true,
        bool skipTeamCheck = false,
        int selectedTroopIndex = 0,
        bool hasNativeCultureClassIndex = false)
    {
        return new CoopPeerHeroClassSafetyInput(
            isCoopMission,
            isClient,
            isServer,
            isDedicatedServer,
            hasControlledAgent,
            hasTeam,
            hasActiveTeamSide,
            skipTeamCheck,
            selectedTroopIndex,
            hasNativeCultureClassIndex);
    }

    private static void AssertCanonicalHeroClass(
        CoopPeerHeroClassSafetyInput input,
        string message)
    {
        Assert(
            CoopPeerHeroClassSafetyContract
                .ShouldResolveCanonicalHeroClass(input),
            message);
    }

    private static void AssertNativeHeroClass(
        CoopPeerHeroClassSafetyInput input,
        string message)
    {
        Assert(
            !CoopPeerHeroClassSafetyContract
                .ShouldResolveCanonicalHeroClass(input),
            message);
    }

    private static void AssertSuppress(
        ExactCampaignSiegePeerPerkSafetyInput input,
        string message)
    {
        Assert(
            ExactCampaignSiegePeerPerkSafetyContract
                .ShouldSuppressNativePerkResolution(input),
            message);
    }

    private static void AssertNative(
        ExactCampaignSiegePeerPerkSafetyInput input,
        string message)
    {
        Assert(
            !ExactCampaignSiegePeerPerkSafetyContract
                .ShouldSuppressNativePerkResolution(input),
            message);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
