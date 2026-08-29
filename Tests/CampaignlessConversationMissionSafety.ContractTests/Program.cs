using System;
using CoopSpectator.Infrastructure;

internal static class Program
{
    private static int Main()
    {
        try
        {
            ValidateRemoteClientWithoutCampaignReturnsNull();
            ValidateDedicatedServerWithoutCampaignReturnsNull();
            ValidateMissingConversationManagerReturnsNull();
            ValidateCampaignConversationBehaviorIsPreserved();
            Console.WriteLine(
                "Campaignless conversation mission safety contract tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void ValidateRemoteClientWithoutCampaignReturnsNull()
    {
        Assert(
            CampaignlessConversationMissionSafetyContract.ShouldReturnNull(
                hasCampaign: false,
                hasConversationManager: false),
            "A remote multiplayer client without Campaign.Current must bypass the unsafe getter.");
    }

    private static void ValidateDedicatedServerWithoutCampaignReturnsNull()
    {
        Assert(
            CampaignlessConversationMissionSafetyContract.ShouldReturnNull(
                hasCampaign: false,
                hasConversationManager: false),
            "A dedicated server without campaign assemblies must bypass the unsafe getter.");
    }

    private static void ValidateMissingConversationManagerReturnsNull()
    {
        Assert(
            CampaignlessConversationMissionSafetyContract.ShouldReturnNull(
                hasCampaign: true,
                hasConversationManager: false),
            "A transitional campaign state without ConversationManager must bypass the unsafe getter.");
    }

    private static void ValidateCampaignConversationBehaviorIsPreserved()
    {
        Assert(
            !CampaignlessConversationMissionSafetyContract.ShouldReturnNull(
                hasCampaign: true,
                hasConversationManager: true),
            "An active campaign conversation manager must preserve the original getter behavior.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
