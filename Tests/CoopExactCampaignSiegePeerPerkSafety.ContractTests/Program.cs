using System;
using CoopSpectator.Infrastructure;

internal static class Program
{
    private static int Main()
    {
        try
        {
            ValidateDetachedInvalidPeerStateIsSuppressed();
            ValidatePerkStorageBoundsAreProtected();
            ValidateValidNativeStatesRemainNative();
            ValidateContextGatesRemainNative();
            ValidateRepeatedDecisionIsIdempotent();
            Console.WriteLine("Exact campaign siege peer perk safety contract tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void ValidateDetachedInvalidPeerStateIsSuppressed()
    {
        AssertSuppress(
            CreateInput(hasControlledAgent: false, hasCulture: false, cultureClassCount: 0),
            "A detached exact-siege peer without culture must not enter native perk resolution.");
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
            "Missing native perk storage must fail closed in the exact dedicated siege.");
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
            CreateInput(isExactCampaignSiegeAssault: false, hasControlledAgent: false, hasCulture: false),
            "A non-exact campaign siege must remain native.");
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
        bool isExactCampaignSiegeAssault = true,
        bool isDedicatedServer = true,
        bool hasTeam = true,
        bool hasControlledAgent = false,
        bool hasCulture = true,
        int selectedTroopIndex = 8,
        int perkStorageCount = 16,
        int cultureClassCount = 9)
    {
        return new ExactCampaignSiegePeerPerkSafetyInput(
            isExactCampaignSiegeAssault,
            isDedicatedServer,
            hasTeam,
            hasControlledAgent,
            hasCulture,
            selectedTroopIndex,
            perkStorageCount,
            cultureClassCount);
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
