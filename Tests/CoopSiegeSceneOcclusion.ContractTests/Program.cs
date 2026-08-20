using System;
using CoopSpectator.Infrastructure;

internal static class Program
{
    private static int Main()
    {
        try
        {
            ValidateAllRemoteSiegeAssaultScenesAreDisabled();
            ValidateSiegeAmbushRemainsDisabled();
            ValidateNonRemoteAndNonSiegeContextsRemainEnabled();
            ValidateUnknownSiegeSubtypeIsDisabled();
            ValidateWrongMissionShellRemainsEnabled();
            ValidateMissionShellMatchingIsNormalized();
            Console.WriteLine("Coop siege scene occlusion contract tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void ValidateAllRemoteSiegeAssaultScenesAreDisabled()
    {
        string[] scenes =
        {
            "empire_castle_g",
            "empire_siege_001",
            "empire_town_d",
            "future_campaign_siege_scene"
        };

        foreach (string scene in scenes)
        {
            CoopSiegeSceneOcclusionSafetyDecision decision = Resolve(
                runtimeScene: scene,
                siegeSubtype: "SiegeAssault");

            Assert(
                decision.DisableSceneOcclusion,
                "Every remote-client siege assault scene must disable software occlusion. Scene=" + scene + ".");
            AssertEqual(
                "remote-client-siege-software-occlusion-safety",
                decision.Reason,
                "The all-scene siege safety decision must remain diagnosable. Scene=" + scene + ".");
        }
    }

    private static void ValidateSiegeAmbushRemainsDisabled()
    {
        CoopSiegeSceneOcclusionSafetyDecision decision = Resolve(
            runtimeScene: "empire_siege_001",
            siegeSubtype: "SiegeAmbush");

        Assert(
            decision.DisableSceneOcclusion,
            "The existing exact siege ambush safety policy must remain active.");
        AssertEqual(
            "remote-client-siege-software-occlusion-safety",
            decision.Reason,
            "The siege ambush decision must report the shared remote-client siege safety reason.");
    }

    private static void ValidateNonRemoteAndNonSiegeContextsRemainEnabled()
    {
        CoopSiegeSceneOcclusionSafetyDecision hostDecision =
            CoopSiegeSceneOcclusionSafetyContract.Resolve(
                isRemoteClient: false,
                isSiegeBattle: true,
                missionShell: "SiegeMissionWithDeployment",
                siegeSubtype: "SiegeAssault",
                runtimeScene: "empire_castle_g");
        Assert(
            !hostDecision.DisableSceneOcclusion,
            "The campaign host and dedicated server must not receive the client-only workaround.");

        CoopSiegeSceneOcclusionSafetyDecision fieldBattleDecision =
            CoopSiegeSceneOcclusionSafetyContract.Resolve(
                isRemoteClient: true,
                isSiegeBattle: false,
                missionShell: "SiegeMissionWithDeployment",
                siegeSubtype: "SiegeAssault",
                runtimeScene: "empire_castle_g");
        Assert(
            !fieldBattleDecision.DisableSceneOcclusion,
            "Non-siege battles must never receive the siege rendering workaround.");
    }

    private static void ValidateWrongMissionShellRemainsEnabled()
    {
        CoopSiegeSceneOcclusionSafetyDecision decision =
            CoopSiegeSceneOcclusionSafetyContract.Resolve(
                isRemoteClient: true,
                isSiegeBattle: true,
                missionShell: "MultiplayerBattle",
                siegeSubtype: "SiegeAssault",
                runtimeScene: "empire_castle_g");

        Assert(
            !decision.DisableSceneOcclusion,
            "A non-deployment mission shell must fail closed and preserve occlusion.");
    }

    private static void ValidateUnknownSiegeSubtypeIsDisabled()
    {
        CoopSiegeSceneOcclusionSafetyDecision decision = Resolve(
            runtimeScene: "empire_castle_g",
            siegeSubtype: "UnknownSiegeSubtype");

        Assert(
            decision.DisableSceneOcclusion,
            "An unknown future siege subtype must remain protected by the mission-shell safety policy.");
        AssertEqual(
            "remote-client-siege-software-occlusion-safety",
            decision.Reason,
            "An unknown future siege subtype must report the shared safety reason.");
    }

    private static void ValidateMissionShellMatchingIsNormalized()
    {
        CoopSiegeSceneOcclusionSafetyDecision decision =
            CoopSiegeSceneOcclusionSafetyContract.Resolve(
                isRemoteClient: true,
                isSiegeBattle: true,
                missionShell: " siegemissionwithdeployment ",
                siegeSubtype: " siegeassault ",
                runtimeScene: " EMPIRE_CASTLE_G ");

        Assert(
            decision.DisableSceneOcclusion,
            "Mission-shell matching must tolerate casing and transport whitespace differences.");
    }

    private static CoopSiegeSceneOcclusionSafetyDecision Resolve(
        string runtimeScene,
        string siegeSubtype)
    {
        return CoopSiegeSceneOcclusionSafetyContract.Resolve(
            isRemoteClient: true,
            isSiegeBattle: true,
            missionShell: "SiegeMissionWithDeployment",
            siegeSubtype: siegeSubtype,
            runtimeScene: runtimeScene);
    }

    private static void AssertEqual(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                message + " Expected=" + expected + " Actual=" + actual + ".");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
