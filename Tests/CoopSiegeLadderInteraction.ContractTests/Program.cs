using System;
using CoopSpectator.Infrastructure;

internal static class Program
{
    private static int Main()
    {
        try
        {
            ValidateRoleResolutionIsLadderSpecific();
            ValidateContextGates();
            ValidateObjectTypeGate();
            ValidateMappingCardinalityGates();
            ValidateUnsafeRootNeverActivates();
            ValidateReadyAttackerPointIsRestored();
            ValidateDefenderRemainsNoOp();
            ValidateRepeatedApplicationIsNoOp();
            ValidateMultipleLaddersRemainSeparated();
            ValidateLateJoinReplay();
            Console.WriteLine("Coop siege ladder interaction contract tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void ValidateRoleResolutionIsLadderSpecific()
    {
        Assert(
            CoopSiegeLadderInteractionContract.TryResolveAttackerPointRole(
                hasAttackerTag: true,
                hasDefenderTag: false,
                isStandingPointWithWeaponRequirement: false,
                hasAmmoPickupTag: false,
                hasRightTag: false,
                hasFrontTag: true,
                out CoopSiegeLadderInteractionPointRole role) &&
            role == CoopSiegeLadderInteractionPointRole.LeftFront,
            "An exact attacker ladder point must resolve to its semantic role.");
        Assert(
            !CoopSiegeLadderInteractionContract.TryResolveAttackerPointRole(
                true, false, true, false, false, false, out _),
            "A fork standing point must not enter the ladder lifting branch.");
        Assert(
            !CoopSiegeLadderInteractionContract.TryResolveAttackerPointRole(
                false, true, false, false, false, false, out _),
            "A defender standing point must not enter the ladder lifting branch.");
        Assert(
            !CoopSiegeLadderInteractionContract.TryResolveAttackerPointRole(
                true, false, false, true, false, false, out _),
            "An ammo or fork pickup point must not enter the ladder lifting branch.");
        Assert(
            CoopSiegeLadderInteractionContract.TryResolveAttackerPointRole(
                true, false, false, false, true, true, out role) &&
            role == CoopSiegeLadderInteractionPointRole.RightFront,
            "The native right/front tags must resolve the matching lift animation role.");
    }

    private static void ValidateContextGates()
    {
        AssertNoMutation(
            CreateInput(isExactCampaignSiegeAssault: false),
            "A non-campaign siege must remain a no-op.");
        AssertNoMutation(
            CreateInput(isRemoteClient: false),
            "The server and a listen-server host must remain a no-op.");
        AssertNoMutation(
            CreateInput(isPlayerControlled: false),
            "An AI-observed client must remain a no-op.");
    }

    private static void ValidateObjectTypeGate()
    {
        string[] unrelatedObjects =
        {
            "gate",
            "spawned pilum",
            "ordinary item",
            "fork",
            "fire pot",
            "catapult",
            "stone pile"
        };
        foreach (string unrelatedObject in unrelatedObjects)
        {
            AssertNoMutation(
                CreateInput(objectKind: CoopSiegeLadderInteractionObjectKind.Other),
                "The unrelated object must not enter the branch: " + unrelatedObject + ".");
        }

        Assert(
            CoopSiegeLadderInteractionContract.IsSupportedObjectKind(
                CoopSiegeLadderInteractionObjectKind.SiegeLadder),
            "A SiegeLadder root is eligible for explicit ID translation.");
        Assert(
            !CoopSiegeLadderInteractionContract.IsSupportedObjectKind(
                CoopSiegeLadderInteractionObjectKind.Other),
            "An unrelated mission object is never eligible for translation.");
    }

    private static void ValidateMappingCardinalityGates()
    {
        AssertNoMutation(
            CreateInput(authoritativeIdentityCount: 0),
            "A missing authoritative identity must remain a no-op.");
        AssertNoMutation(
            CreateInput(authoritativeIdentityCount: 2),
            "An ambiguous authoritative identity must remain a no-op.");
        AssertNoMutation(
            CreateInput(localLadderCount: 0),
            "A missing local ladder must remain a no-op.");
        AssertNoMutation(
            CreateInput(localLadderCount: 2),
            "Ambiguous local ladders must remain a no-op.");
        AssertNoMutation(
            CreateInput(localPointCount: 0),
            "A missing local standing point must remain a no-op.");
        AssertNoMutation(
            CreateInput(localPointCount: 2),
            "Ambiguous local standing points must remain a no-op.");
        AssertNoMutation(
            CreateInput(isBijectiveMapping: false),
            "A conflicting one-to-one mapping must remain a no-op.");
    }

    private static void ValidateUnsafeRootNeverActivates()
    {
        AssertDeactivated(
            CreateInput(rootDisabled: true, localPointDeactivated: false),
            "A server-disabled ladder must not activate its point.");
        AssertDeactivated(
            CreateInput(rootDestroyed: true, localPointDeactivated: false),
            "A destroyed ladder must not activate its point.");
        AssertDeactivated(
            CreateInput(rootDeactivated: true, localPointDeactivated: false),
            "A server-deactivated ladder must not activate its point.");
        AssertDeactivated(
            CreateInput(rootVisible: false, localPointDeactivated: false),
            "A hidden ladder must not activate its point.");
        AssertDeactivated(
            CreateInput(ladderState: 5, localPointDeactivated: false),
            "A ladder that is not OnLand must not expose the lift action.");
        AssertDeactivated(
            CreateInput(
                authoritativePointDeactivated: true,
                localPointDeactivated: false),
            "An explicitly deactivated server point must stay deactivated.");
        AssertNoMutation(
            CreateInput(
                authoritativePointHasUser: true,
                localPointDeactivated: false),
            "An occupied server point must retain its native user state without flag mutation.");
    }

    private static void ValidateReadyAttackerPointIsRestored()
    {
        CoopSiegeLadderInteractionDecision decision =
            CoopSiegeLadderInteractionContract.Decide(CreateInput());
        Assert(decision.ShouldMutate, "A ready deployed attacker ladder point must be restored.");
        Assert(!decision.DesiredDeactivated, "The restored attacker point must be active.");
        Assert(
            !decision.DesiredDisabledForPlayers,
            "The restored attacker point must remain enabled for players.");

        decision = CoopSiegeLadderInteractionContract.Decide(
            CreateInput(ladderState: 2));
        Assert(
            decision.ShouldMutate && !decision.DesiredDeactivated,
            "A server-available point may remain joinable while the ladder is being raised.");

        decision = CoopSiegeLadderInteractionContract.Decide(
            CreateInput(authoritativePointDisabledForPlayers: true));
        Assert(
            decision.ShouldMutate && decision.DesiredDisabledForPlayers,
            "The server's player-disabled flag must be mirrored instead of cleared.");
    }

    private static void ValidateDefenderRemainsNoOp()
    {
        AssertNoMutation(
            CreateInput(isLocalAttacker: false),
            "A defender client must not receive attacker ladder interaction.");
    }

    private static void ValidateRepeatedApplicationIsNoOp()
    {
        AssertNoMutation(
            CreateInput(localPointDeactivated: false),
            "Repeated application to an already active point must be idempotent.");
        AssertNoMutation(
            CreateInput(
                authoritativePointDisabledForPlayers: true,
                localPointDeactivated: false,
                localPointDisabledForPlayers: true),
            "Repeated application of the disabled-for-players flag must be idempotent.");
    }

    private static void ValidateMultipleLaddersRemainSeparated()
    {
        AssertNoMutation(
            CreateInput(authoritativeIdentityCount: 2),
            "Two ladders sharing one identity must never be mixed.");
        CoopSiegeLadderInteractionDecision firstLadder =
            CoopSiegeLadderInteractionContract.Decide(CreateInput());
            CoopSiegeLadderInteractionDecision secondLadder =
            CoopSiegeLadderInteractionContract.Decide(
                CreateInput(
                    authoritativePointDeactivated: true,
                    localPointDeactivated: false));
        Assert(
            !firstLadder.DesiredDeactivated && secondLadder.DesiredDeactivated,
            "Distinct unique ladder identities may retain different authoritative states.");
    }

    private static void ValidateLateJoinReplay()
    {
        CoopSiegeLadderInteractionDecision initialReplay =
            CoopSiegeLadderInteractionContract.Decide(CreateInput());
        Assert(
            initialReplay.ShouldMutate && !initialReplay.DesiredDeactivated,
            "A late join snapshot must restore the current ready state.");
        AssertNoMutation(
            CreateInput(localPointDeactivated: initialReplay.DesiredDeactivated),
            "Replaying the same late join snapshot must be a no-op.");
    }

    private static CoopSiegeLadderInteractionDecisionInput CreateInput(
        bool isExactCampaignSiegeAssault = true,
        bool isRemoteClient = true,
        bool isPlayerControlled = true,
        bool isLocalAttacker = true,
        CoopSiegeLadderInteractionObjectKind objectKind =
            CoopSiegeLadderInteractionObjectKind.AttackerStandingPoint,
        int authoritativeIdentityCount = 1,
        int localLadderCount = 1,
        int localPointCount = 1,
        bool isBijectiveMapping = true,
        int ladderState = CoopSiegeLadderInteractionContract.OnLandState,
        bool rootDisabled = false,
        bool rootDestroyed = false,
        bool rootDeactivated = false,
        bool rootVisible = true,
        bool authoritativePointDeactivated = false,
        bool authoritativePointDisabledForPlayers = false,
        bool authoritativePointHasUser = false,
        bool localPointDeactivated = true,
        bool localPointDisabledForPlayers = false)
    {
        return new CoopSiegeLadderInteractionDecisionInput(
            isExactCampaignSiegeAssault,
            isRemoteClient,
            isPlayerControlled,
            isLocalAttacker,
            objectKind,
            authoritativeIdentityCount,
            localLadderCount,
            localPointCount,
            isBijectiveMapping,
            ladderState,
            rootDisabled,
            rootDestroyed,
            rootDeactivated,
            rootVisible,
            authoritativePointDeactivated,
            authoritativePointDisabledForPlayers,
            authoritativePointHasUser,
            localPointDeactivated,
            localPointDisabledForPlayers);
    }

    private static void AssertDeactivated(
        CoopSiegeLadderInteractionDecisionInput input,
        string message)
    {
        CoopSiegeLadderInteractionDecision decision =
            CoopSiegeLadderInteractionContract.Decide(input);
        Assert(
            decision.ShouldMutate && decision.DesiredDeactivated,
            message);
    }

    private static void AssertNoMutation(
        CoopSiegeLadderInteractionDecisionInput input,
        string message)
    {
        Assert(
            !CoopSiegeLadderInteractionContract.Decide(input).ShouldMutate,
            message);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
