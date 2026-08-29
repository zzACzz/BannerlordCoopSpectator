using System;
using CoopSpectator.Infrastructure;

internal static class Program
{
    private const string SiegeShell = "SiegeMissionWithDeployment";
    private const string CastleScene = "empire_castle_004";

    private static int Main()
    {
        try
        {
            ValidateExactRemoteSiegeIsProtected();
            ValidateAnotherExactRemoteSiegeIsProtected();
            ValidateSuccessfulBattleTypesRemainUnchanged();
            ValidateServerAndListenServerRemainUnchanged();
            ValidateMissingOrMismatchedTopologyIsRejected();
            ValidateNonExactShellIsRejected();
            Console.WriteLine(
                "Remote siege occlusion safety contract tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void ValidateExactRemoteSiegeIsProtected()
    {
        CoopSiegeSceneOcclusionSafetyDecision decision = Resolve(
            isRemoteClient: true,
            hasMatchingPreMissionTopology: true,
            isSiegeBattle: true,
            missionShell: SiegeShell,
            runtimeScene: CastleScene,
            topologyScene: CastleScene);

        Assert(
            decision.DisableSceneOcclusion,
            "The reproduced remote SiegeMissionWithDeployment crash path must disable scene occlusion.");
    }

    private static void ValidateAnotherExactRemoteSiegeIsProtected()
    {
        CoopSiegeSceneOcclusionSafetyDecision decision = Resolve(
            isRemoteClient: true,
            hasMatchingPreMissionTopology: true,
            isSiegeBattle: true,
            missionShell: " siegemissionwithdeployment ",
            runtimeScene: "vlandia_castle_005",
            topologyScene: "VLANDIA_CASTLE_005");

        Assert(
            decision.DisableSceneOcclusion,
            "Other matching castle scenes using the same exact siege shell must receive the same protection.");
    }

    private static void ValidateSuccessfulBattleTypesRemainUnchanged()
    {
        AssertKeptEnabled(
            Resolve(true, true, false, "MultiplayerBattle", "sea_bandit_d_sv", "sea_bandit_d_sv"),
            "Day hideouts must remain unchanged.");
        AssertKeptEnabled(
            Resolve(true, true, false, "MultiplayerBattle", "battle_terrain_001", "battle_terrain_001"),
            "Field battles must remain unchanged.");
        AssertKeptEnabled(
            Resolve(true, true, false, "MultiplayerBattle", "battania_village_h", "battania_village_h"),
            "Village battles must remain unchanged.");
        AssertKeptEnabled(
            Resolve(true, true, true, "MultiplayerBattle", "battle_terrain_biome_030", "battle_terrain_biome_030"),
            "A siege-labelled battle using the successful MultiplayerBattle shell must remain unchanged.");
    }

    private static void ValidateServerAndListenServerRemainUnchanged()
    {
        AssertKeptEnabled(
            Resolve(false, true, true, SiegeShell, CastleScene, CastleScene),
            "The server and listen-server host path must remain unchanged.");
    }

    private static void ValidateMissingOrMismatchedTopologyIsRejected()
    {
        AssertKeptEnabled(
            Resolve(true, false, true, SiegeShell, CastleScene, CastleScene),
            "A missing active pre-mission topology must not change renderer state.");
        AssertKeptEnabled(
            Resolve(true, true, true, SiegeShell, CastleScene, "empire_castle_005"),
            "A topology for another scene must not change renderer state.");
        AssertKeptEnabled(
            Resolve(true, true, true, SiegeShell, string.Empty, CastleScene),
            "A missing runtime scene must not change renderer state.");
    }

    private static void ValidateNonExactShellIsRejected()
    {
        AssertKeptEnabled(
            Resolve(true, true, true, "SiegeMission", CastleScene, CastleScene),
            "A different siege shell must remain unchanged.");
        AssertKeptEnabled(
            Resolve(true, true, true, "SiegeMissionWithDeploymentExtra", CastleScene, CastleScene),
            "A partial shell-name match must remain unchanged.");
    }

    private static CoopSiegeSceneOcclusionSafetyDecision Resolve(
        bool isRemoteClient,
        bool hasMatchingPreMissionTopology,
        bool isSiegeBattle,
        string missionShell,
        string runtimeScene,
        string topologyScene)
    {
        return CoopSiegeSceneOcclusionSafetyContract.Resolve(
            isRemoteClient,
            hasMatchingPreMissionTopology,
            isSiegeBattle,
            missionShell,
            runtimeScene,
            topologyScene);
    }

    private static void AssertKeptEnabled(
        CoopSiegeSceneOcclusionSafetyDecision decision,
        string message)
    {
        Assert(!decision.DisableSceneOcclusion, message);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
