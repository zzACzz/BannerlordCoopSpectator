using System;
using CoopSpectator.Infrastructure;

internal static class Program
{
    private static int Main()
    {
        try
        {
            ValidateKnownUnsafeSiegeAssaultSceneIsDisabled();
            ValidateKnownSafeSiegeAssaultSceneRemainsEnabled();
            ValidateSiegeAmbushRemainsDisabled();
            ValidateNonRemoteAndNonSiegeContextsRemainEnabled();
            ValidateUnknownSiegeSubtypeRemainsEnabled();
            ValidateWrongMissionShellRemainsEnabled();
            ValidateMatchingIsNormalized();
            Console.WriteLine("Coop siege scene occlusion contract tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void ValidateKnownUnsafeSiegeAssaultSceneIsDisabled()
    {
        CoopSiegeSceneOcclusionSafetyDecision decision = Resolve(
            runtimeScene: "empire_castle_g",
            siegeSubtype: "SiegeAssault");

        Assert(
            decision.DisableSceneOcclusion,
            "The known unsafe external siege scene must disable software occlusion on a remote client.");
        AssertEqual(
            "known-unsafe-software-occlusion-scene",
            decision.Reason,
            "The unsafe scene decision must remain diagnosable.");
    }

    private static void ValidateKnownSafeSiegeAssaultSceneRemainsEnabled()
    {
        CoopSiegeSceneOcclusionSafetyDecision decision = Resolve(
            runtimeScene: "empire_town_d",
            siegeSubtype: "SiegeAssault");

        Assert(
            !decision.DisableSceneOcclusion,
            "Previously successful external siege scenes must keep occlusion enabled.");
        AssertEqual(
            "scene-occlusion-supported",
            decision.Reason,
            "A supported scene must report the non-mitigation reason.");
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
            "exact-siege-ambush",
            decision.Reason,
            "The siege ambush decision must keep its distinct reason.");
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

    private static void ValidateUnknownSiegeSubtypeRemainsEnabled()
    {
        CoopSiegeSceneOcclusionSafetyDecision decision = Resolve(
            runtimeScene: "empire_castle_g",
            siegeSubtype: "UnknownSiegeSubtype");

        Assert(
            !decision.DisableSceneOcclusion,
            "An unknown siege subtype must not activate the scene-specific workaround.");
    }

    private static void ValidateMatchingIsNormalized()
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
            "Contract matching must tolerate casing and transport whitespace differences.");
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
