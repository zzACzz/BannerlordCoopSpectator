using System;
using CoopSpectator.Infrastructure;

internal static class Program
{
    private static int Main()
    {
        try
        {
            ValidateSingleStagePlan();
            ValidateMultiStageAggregation();
            ValidateExactRetryIsNoOp();
            ValidateExistingNativeStateIsPreserved();
            ValidateDecisiveSiegeAmbushPlan();
            ValidateHideoutCasualtyLedgerPlan();
            ValidateEffectiveHideoutPopulationPlan();
            ValidateTerminalAgentReconciliationPolicy();
            ValidateCampaignCasualtyScenarioPolicy();
            ValidateCampaignHeroDeathPolicy();
            ValidateInvalidInputsAreClamped();
            Console.WriteLine("Native aftermath contract tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static void ValidateSingleStagePlan()
    {
        ExactCasualtyLedgerDelta killed =
            ExactCasualtyLedgerMath.PlanMissingDelta(0, 0, 113, 0);
        ExactCasualtyLedgerDelta wounded =
            ExactCasualtyLedgerMath.PlanMissingDelta(0, 0, 492, 492);
        Assert(killed.NumberDelta == 113 && killed.WoundedDelta == 0,
            "Single-stage killed ledger delta is invalid.");
        Assert(wounded.NumberDelta == 492 && wounded.WoundedDelta == 492,
            "Single-stage wounded ledger delta is invalid.");
    }

    private static void ValidateMultiStageAggregation()
    {
        int killed = ExactCasualtyLedgerMath.CombineStageCounts(73, 40);
        int wounded = ExactCasualtyLedgerMath.CombineStageCounts(320, 172);
        Assert(killed == 113, "Killed casualties from two siege stages must be summed.");
        Assert(wounded == 492, "Wounded casualties from two siege stages must be summed.");
    }

    private static void ValidateExactRetryIsNoOp()
    {
        ExactCasualtyLedgerDelta killed =
            ExactCasualtyLedgerMath.PlanMissingDelta(113, 0, 113, 0);
        ExactCasualtyLedgerDelta wounded =
            ExactCasualtyLedgerMath.PlanMissingDelta(492, 492, 492, 492);
        Assert(killed.NumberDelta == 0 && killed.WoundedDelta == 0,
            "Repeated killed result must not duplicate the ledger.");
        Assert(wounded.NumberDelta == 0 && wounded.WoundedDelta == 0,
            "Repeated wounded result must not duplicate the ledger.");
    }

    private static void ValidateExistingNativeStateIsPreserved()
    {
        ExactCasualtyLedgerDelta partial =
            ExactCasualtyLedgerMath.PlanMissingDelta(13, 13, 20, 20);
        ExactCasualtyLedgerDelta surplus =
            ExactCasualtyLedgerMath.PlanMissingDelta(25, 25, 20, 20);
        Assert(partial.NumberDelta == 7 && partial.WoundedDelta == 7,
            "Only missing native casualty rows should be added.");
        Assert(surplus.NumberDelta == 0 && surplus.WoundedDelta == 0,
            "Existing native casualty rows must never be removed.");
    }

    private static void ValidateDecisiveSiegeAmbushPlan()
    {
        ExactCasualtyLedgerDelta killed =
            ExactCasualtyLedgerMath.PlanMissingDelta(2, 0, 7, 0);
        ExactCasualtyLedgerDelta wounded =
            ExactCasualtyLedgerMath.PlanMissingDelta(3, 3, 11, 11);
        Assert(killed.NumberDelta == 5 && killed.WoundedDelta == 0,
            "Siege-ambush loot must receive only missing killed casualties.");
        Assert(wounded.NumberDelta == 8 && wounded.WoundedDelta == 8,
            "Siege-ambush loot must receive only missing wounded casualties.");

        ExactCasualtyLedgerDelta retryKilled =
            ExactCasualtyLedgerMath.PlanMissingDelta(7, 0, 7, 0);
        ExactCasualtyLedgerDelta retryWounded =
            ExactCasualtyLedgerMath.PlanMissingDelta(11, 11, 11, 11);
        Assert(retryKilled.NumberDelta == 0 && retryKilled.WoundedDelta == 0,
            "Repeated siege-ambush killed casualties must not duplicate loot.");
        Assert(retryWounded.NumberDelta == 0 && retryWounded.WoundedDelta == 0,
            "Repeated siege-ambush wounded casualties must not duplicate loot.");
    }

        private static void ValidateHideoutCasualtyLedgerPlan()
    {
        ExactCasualtyLedgerDelta killed =
            ExactCasualtyLedgerMath.PlanMissingDelta(0, 0, 12, 0);
        ExactCasualtyLedgerDelta wounded =
            ExactCasualtyLedgerMath.PlanMissingDelta(0, 0, 7, 7);
        Assert(killed.NumberDelta == 12 && killed.WoundedDelta == 0,
            "Hideout killed defenders must populate the native died-in-battle ledger.");
        Assert(wounded.NumberDelta == 7 && wounded.WoundedDelta == 7,
            "Hideout unconscious defenders must populate the native wounded-in-battle ledger.");

        ExactCasualtyLedgerDelta retryKilled =
            ExactCasualtyLedgerMath.PlanMissingDelta(12, 0, 12, 0);
        ExactCasualtyLedgerDelta retryWounded =
            ExactCasualtyLedgerMath.PlanMissingDelta(7, 7, 7, 7);
        Assert(retryKilled.NumberDelta == 0 && retryKilled.WoundedDelta == 0,
            "Repeated hideout killed casualties must not duplicate equipment loot.");
            Assert(retryWounded.NumberDelta == 0 && retryWounded.WoundedDelta == 0,
                "Repeated hideout wounded casualties must not duplicate equipment loot.");
        }

        private static void ValidateEffectiveHideoutPopulationPlan()
        {
            int nightParticipants = ExactCasualtyLedgerMath.ResolveEffectiveParticipantCount(
                activeCount: 0,
                killedCount: 10,
                unconsciousCount: 28,
                routedCount: 6,
                otherRemovedCount: 0);
            int nightSurvivors = ExactCasualtyLedgerMath.ResolveEffectiveSurvivorCount(
                activeCount: 0,
                unconsciousCount: 28,
                routedCount: 6);
            Assert(nightParticipants == 44,
                "Night hideout aftermath must include every materialized boss guard in the effective population.");
            Assert(nightSurvivors == 34,
                "Only the ten killed fighters may be removed from the 44-fighter night hideout population.");

            int dayParticipants = ExactCasualtyLedgerMath.ResolveEffectiveParticipantCount(
                activeCount: 0,
                killedCount: 11,
                unconsciousCount: 18,
                routedCount: 0,
                otherRemovedCount: 0);
            int daySurvivors = ExactCasualtyLedgerMath.ResolveEffectiveSurvivorCount(
                activeCount: 0,
                unconsciousCount: 18,
                routedCount: 0);
            Assert(dayParticipants == 29 && daySurvivors == 18,
                "Day hideout aftermath must preserve the full 29-fighter population while removing only the killed fighters.");
        }

        private static void ValidateTerminalAgentReconciliationPolicy()
        {
            Assert(
                ExactCasualtyLedgerMath.ShouldSkipTerminalAgentReconciliation(
                    isActive: false,
                    wasTerminalRemovalRecorded: true),
                "An inactive agent with an authoritative terminal removal must not be registered twice.");
            Assert(
                !ExactCasualtyLedgerMath.ShouldSkipTerminalAgentReconciliation(
                    isActive: true,
                    wasTerminalRemovalRecorded: true),
                "An active agent that reused an index must still be reconciled.");
            Assert(
                !ExactCasualtyLedgerMath.ShouldSkipTerminalAgentReconciliation(
                    isActive: false,
                    wasTerminalRemovalRecorded: false),
                "An untracked inactive agent must remain eligible for final reconciliation.");
        }

    private static void ValidateCampaignCasualtyScenarioPolicy()
    {
        Assert(
            CampaignCasualtyPolicy.SupportsScenario(
                isSiegeBattle: true,
                isExactLandBattle: false,
                scenarioKind: "Siege"),
            "Siege battles must use campaign casualty rules.");
        Assert(
            CampaignCasualtyPolicy.SupportsScenario(
                isSiegeBattle: false,
                isExactLandBattle: true,
                scenarioKind: "FieldBattle"),
            "Exact land battles must use campaign casualty rules.");
        Assert(
            CampaignCasualtyPolicy.SupportsScenario(
                isSiegeBattle: false,
                isExactLandBattle: false,
                scenarioKind: "Hideout"),
            "Daytime hideouts must use campaign casualty rules.");
        Assert(
            CampaignCasualtyPolicy.SupportsScenario(
                isSiegeBattle: false,
                isExactLandBattle: false,
                scenarioKind: "HideoutAmbush"),
            "Nighttime hideout ambushes must use campaign casualty rules.");
        Assert(
            !CampaignCasualtyPolicy.SupportsScenario(
                isSiegeBattle: false,
                isExactLandBattle: false,
                scenarioKind: "HideoutAmbushExtra"),
            "An unrelated scenario must not enter campaign casualty rules by partial name.");
    }

    private static void ValidateCampaignHeroDeathPolicy()
    {
        Assert(
            !CampaignCasualtyPolicy.AllowsHeroDeath(
                battleDeathDifficulty: 0,
                isPlayerCharacter: false,
                heroCanDieInBattle: true),
            "Disabled battle death must protect every hero.");
        Assert(
            !CampaignCasualtyPolicy.AllowsHeroDeath(
                battleDeathDifficulty: 1,
                isPlayerCharacter: true,
                heroCanDieInBattle: true),
            "Player-protected battle death must protect the player character.");
        Assert(
            CampaignCasualtyPolicy.AllowsHeroDeath(
                battleDeathDifficulty: 1,
                isPlayerCharacter: false,
                heroCanDieInBattle: true),
            "Player-protected battle death may still allow a non-player hero to die.");
        Assert(
            CampaignCasualtyPolicy.AllowsHeroDeath(
                battleDeathDifficulty: 2,
                isPlayerCharacter: true,
                heroCanDieInBattle: true),
            "Realistic battle death may allow the player character to die.");
        Assert(
            !CampaignCasualtyPolicy.AllowsHeroDeath(
                battleDeathDifficulty: 2,
                isPlayerCharacter: false,
                heroCanDieInBattle: false),
            "Hero-specific campaign protection must override the difficulty setting.");
    }

    private static void ValidateInvalidInputsAreClamped()
    {
        Assert(ExactCasualtyLedgerMath.CombineStageCounts(-5, 7) == 7,
            "Negative stage totals must be treated as zero.");
        ExactCasualtyLedgerDelta delta =
            ExactCasualtyLedgerMath.PlanMissingDelta(-1, 10, 4, 9);
        Assert(delta.NumberDelta == 4 && delta.WoundedDelta == 4,
            "Invalid wounded totals must be clamped to the planned member total.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
