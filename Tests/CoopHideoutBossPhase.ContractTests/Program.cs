using System;
using CoopSpectator.Infrastructure.Hideout;

internal static class Program
{
    private static int Main()
    {
        try
        {
            ValidateCampaignBridgePolicy();
            ValidateScenePolicy();
            ValidatePreOpenMissionContractPolicy();
            ValidateNativeTimerStartupPolicy();
            ValidateTriggerPolicy();
            ValidatePhaseTransitions();
            ValidateHostChoiceAuthority();
            ValidateFallbackTransitions();
            Console.WriteLine("Coop hideout boss phase contract tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static void ValidateScenePolicy()
    {
        string[] supportedScenes =
        {
            "bandit_forest",
            "bandit_forest_sv",
            "desert_hideout_004_sv",
            "forest_hideout_003",
            "hideout_steppe_001",
            "mountain_hideout_004_sv",
            "sea_bandit_d_sv"
        };
        foreach (string scene in supportedScenes)
        {
            Assert(
                CoopHideoutBossPhaseContract.IsSupportedDayHideoutSceneName(scene),
                "Expected supported vanilla hideout scene: " + scene);
        }

        Assert(
            CoopHideoutBossPhaseContract.TryNormalizeDayHideoutSceneName(
                "  BANDIT_FOREST_SV  ",
                out string normalizedScene) &&
            normalizedScene == "BANDIT_FOREST_SV",
            "A supported hideout scene must be trimmed without changing its identifier casing.");

        string[] rejectedScenes =
        {
            null,
            string.Empty,
            "battle_terrain_n",
            "mp_battle_map_001",
            "village_battania_a",
            "bandit_forest_variant"
        };
        foreach (string scene in rejectedScenes)
        {
            Assert(
                !CoopHideoutBossPhaseContract.IsSupportedDayHideoutSceneName(scene),
                "Expected non-hideout scene rejection: " + (scene ?? "null"));
        }
    }

    private static void ValidatePreOpenMissionContractPolicy()
    {
        Assert(
            CoopHideoutBossPhaseContract.IsMatchingDayHideoutMissionContract(
                "bandit_forest_sv",
                "BANDIT_FOREST_SV",
                CoopHideoutBossPhaseContract.ScenarioKind),
            "A matching exact hideout scene and hideout scenario must be accepted.");
        Assert(
            !CoopHideoutBossPhaseContract.IsMatchingDayHideoutMissionContract(
                "bandit_forest_sv",
                "bandit_forest_sv",
                null),
            "A pre-open contract with a missing scenario kind must be rejected.");
        Assert(
            !CoopHideoutBossPhaseContract.IsMatchingDayHideoutMissionContract(
                "bandit_forest_sv",
                "battle_terrain_n",
                CoopHideoutBossPhaseContract.ScenarioKind),
            "A pre-open contract with a field battle scene must be rejected.");
        Assert(
            !CoopHideoutBossPhaseContract.IsMatchingDayHideoutMissionContract(
                "bandit_forest_sv",
                "forest_hideout_003",
                CoopHideoutBossPhaseContract.ScenarioKind),
            "A different supported hideout scene must be rejected.");
        Assert(
            !CoopHideoutBossPhaseContract.IsMatchingDayHideoutMissionContract(
                "bandit_forest_sv",
                "bandit_forest_sv",
                "FieldBattle"),
            "A non-hideout scenario kind must be rejected.");
    }

    private static void ValidateCampaignBridgePolicy()
    {
        Assert(
            CoopHideoutBossPhaseContract.CanEnterDayHideoutCampaignBridge(
                hasDayController: true,
                hasAmbushController: false,
                hasSelectedRosterContract: true),
            "A vanilla day assault must enter the isolated campaign bridge before scene entities finish loading.");
        Assert(
            !CoopHideoutBossPhaseContract.CanEnterDayHideoutCampaignBridge(
                hasDayController: false,
                hasAmbushController: false,
                hasSelectedRosterContract: true),
            "A mission without the vanilla day controller must remain unsupported.");
        Assert(
            !CoopHideoutBossPhaseContract.CanEnterDayHideoutCampaignBridge(
                hasDayController: true,
                hasAmbushController: true,
                hasSelectedRosterContract: true),
            "A night ambush must not enter the day-assault bridge.");
        Assert(
            !CoopHideoutBossPhaseContract.CanEnterDayHideoutCampaignBridge(
                hasDayController: true,
                hasAmbushController: false,
                hasSelectedRosterContract: false),
            "A day assault without the selected-roster contract must remain blocked.");
    }

    private static void ValidateNativeTimerStartupPolicy()
    {
        Assert(
            CoopHideoutBossPhaseContract.ShouldAllowNativeTimerStartup(
                hasHideoutDayRuntimeMarker: true,
                CoopHideoutBossPhaseContract.NativeTimerStartAsServerSource),
            "The isolated hideout runtime must allow the native server timer initialization call.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldAllowNativeTimerStartup(
                hasHideoutDayRuntimeMarker: true,
                CoopHideoutBossPhaseContract.NativeTimerStartAsClientSource),
            "The isolated hideout runtime must allow the native client timer initialization call.");
        Assert(
            !CoopHideoutBossPhaseContract.ShouldAllowNativeTimerStartup(
                hasHideoutDayRuntimeMarker: false,
                CoopHideoutBossPhaseContract.NativeTimerStartAsServerSource),
            "A non-hideout runtime must retain native server timer suppression.");
        Assert(
            !CoopHideoutBossPhaseContract.ShouldAllowNativeTimerStartup(
                hasHideoutDayRuntimeMarker: true,
                "MultiplayerWarmupComponent.AfterStart"),
            "The hideout timer exception must not allow another native battle shell method.");
        Assert(
            !CoopHideoutBossPhaseContract.ShouldAllowNativeTimerStartup(
                hasHideoutDayRuntimeMarker: true,
                null),
            "A missing native battle shell source must remain suppressed.");
    }

    private static void ValidateTriggerPolicy()
    {
        Assert(CoopHideoutBossPhaseContract.ResolveBossTriggerCount(0) == 0, "Empty enemy roster must not trigger a boss phase.");
        Assert(CoopHideoutBossPhaseContract.ResolveBossTriggerCount(1) == 1, "A single enemy must trigger at one.");
        Assert(CoopHideoutBossPhaseContract.ResolveBossTriggerCount(5) == 2, "Five enemies must trigger at the rounded-up quarter.");
        Assert(CoopHideoutBossPhaseContract.ResolveBossTriggerCount(20) == 5, "Twenty enemies must trigger at five.");
        Assert(CoopHideoutBossPhaseContract.ResolveBossTriggerCount(100) == 5, "The trigger must be capped at five.");
        Assert(
            CoopHideoutBossPhaseContract.ShouldPrepareBossPhase(20, 5, hostAgentActive: true, bossFightEntityAvailable: true),
            "A valid five-enemy boss threshold must start preparation.");
        Assert(
            !CoopHideoutBossPhaseContract.ShouldPrepareBossPhase(20, 6, hostAgentActive: true, bossFightEntityAvailable: true),
            "Preparation must not start above the threshold.");
        Assert(
            !CoopHideoutBossPhaseContract.ShouldPrepareBossPhase(20, 5, hostAgentActive: false, bossFightEntityAvailable: true),
            "Preparation must require an active host agent.");
        Assert(
            !CoopHideoutBossPhaseContract.ShouldPrepareBossPhase(20, 5, hostAgentActive: true, bossFightEntityAvailable: false),
            "Preparation must require the scene anchor.");
    }

    private static void ValidatePhaseTransitions()
    {
        CoopHideoutBossPhaseSession session = NewSession();
        Assert(
            !CoopHideoutBossPhaseContract.TryTransition(
                session,
                CoopHideoutBossPhase.Cinematic,
                DateTime.UtcNow,
                "skip",
                out string rejection) && rejection.StartsWith("transition_invalid", StringComparison.Ordinal),
            "The initial assault must not skip preparation.");

        int initialRevision = session.Revision;
        AssertTransition(session, CoopHideoutBossPhase.PreparingCinematic);
        Assert(session.Revision == initialRevision + 1, "A successful transition must advance the revision once.");
        AssertTransition(session, CoopHideoutBossPhase.Cinematic);
        AssertTransition(session, CoopHideoutBossPhase.AwaitingHostChoice);
    }

    private static void ValidateHostChoiceAuthority()
    {
        CoopHideoutBossPhaseSession session = NewSession();
        session.HostPeerIndex = 7;
        AssertTransition(session, CoopHideoutBossPhase.PreparingCinematic);
        AssertTransition(session, CoopHideoutBossPhase.Cinematic);
        AssertTransition(session, CoopHideoutBossPhase.AwaitingHostChoice);

        Assert(
            !CoopHideoutBossPhaseContract.TryAcceptHostChoice(
                session,
                senderPeerIndex: 8,
                expectedRevision: session.Revision,
                CoopHideoutBossClientCommandKind.ChooseDuel,
                out _,
                out string rejection) && rejection == "choice_sender_not_host",
            "A non-host peer must not choose the boss fight mode.");
        Assert(
            !CoopHideoutBossPhaseContract.TryAcceptHostChoice(
                session,
                senderPeerIndex: 7,
                expectedRevision: session.Revision - 1,
                CoopHideoutBossClientCommandKind.ChooseDuel,
                out _,
                out rejection) && rejection == "choice_revision_stale",
            "A stale host command must be rejected.");
        Assert(
            CoopHideoutBossPhaseContract.TryAcceptHostChoice(
                session,
                senderPeerIndex: 7,
                expectedRevision: session.Revision,
                CoopHideoutBossClientCommandKind.ChooseDuel,
                out CoopHideoutBossChoice choice,
                out rejection) && choice == CoopHideoutBossChoice.Duel,
            "The current host must be able to accept the duel.");
        Assert(
            !CoopHideoutBossPhaseContract.TryAcceptHostChoice(
                session,
                senderPeerIndex: 7,
                expectedRevision: session.Revision,
                CoopHideoutBossClientCommandKind.ChooseAllBattle,
                out _,
                out rejection) && rejection == "choice_already_committed",
            "A committed choice must be idempotent.");
        AssertTransition(session, CoopHideoutBossPhase.Duel);
    }

    private static void ValidateFallbackTransitions()
    {
        CoopHideoutBossPhaseSession duel = NewSession();
        AssertTransition(duel, CoopHideoutBossPhase.PreparingCinematic);
        AssertTransition(duel, CoopHideoutBossPhase.Cinematic);
        AssertTransition(duel, CoopHideoutBossPhase.AwaitingHostChoice);
        AssertTransition(duel, CoopHideoutBossPhase.Duel);
        AssertTransition(duel, CoopHideoutBossPhase.AllBattle);
        AssertTransition(duel, CoopHideoutBossPhase.Completed);

        CoopHideoutBossPhaseSession timeout = NewSession();
        AssertTransition(timeout, CoopHideoutBossPhase.PreparingCinematic);
        AssertTransition(timeout, CoopHideoutBossPhase.Cinematic);
        AssertTransition(timeout, CoopHideoutBossPhase.AwaitingHostChoice);
        AssertTransition(timeout, CoopHideoutBossPhase.AllBattle);
        AssertTransition(timeout, CoopHideoutBossPhase.Completed);
    }

    private static CoopHideoutBossPhaseSession NewSession()
    {
        return new CoopHideoutBossPhaseSession
        {
            BattleInstanceId = "hideout-contract-test",
            Revision = 1,
            Phase = CoopHideoutBossPhase.InitialAssault,
            Choice = CoopHideoutBossChoice.None
        };
    }

    private static void AssertTransition(
        CoopHideoutBossPhaseSession session,
        CoopHideoutBossPhase phase)
    {
        Assert(
            CoopHideoutBossPhaseContract.TryTransition(
                session,
                phase,
                DateTime.UtcNow,
                "test-" + phase,
                out string rejection),
            "Transition to " + phase + " failed: " + rejection);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
