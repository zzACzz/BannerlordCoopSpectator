using System;

namespace CoopSpectator.Infrastructure
{
    public enum CoopSiegeLadderInteractionPointRole
    {
        Invalid = 0,
        LeftRear = 1,
        LeftFront = 2,
        RightRear = 3,
        RightFront = 4
    }

    internal enum CoopSiegeLadderInteractionObjectKind
    {
        Other = 0,
        SiegeLadder = 1,
        AttackerStandingPoint = 2
    }

    internal readonly struct CoopSiegeLadderInteractionDecisionInput
    {
        public CoopSiegeLadderInteractionDecisionInput(
            bool isExactCampaignSiegeAssault,
            bool isRemoteClient,
            bool isPlayerControlled,
            bool isLocalAttacker,
            CoopSiegeLadderInteractionObjectKind objectKind,
            int authoritativeIdentityCount,
            int localLadderCount,
            int localPointCount,
            bool isBijectiveMapping,
            int ladderState,
            bool rootDisabled,
            bool rootDestroyed,
            bool rootDeactivated,
            bool rootVisible,
            bool authoritativePointDeactivated,
            bool authoritativePointDisabledForPlayers,
            bool authoritativePointHasUser,
            bool localPointDeactivated,
            bool localPointDisabledForPlayers)
        {
            IsExactCampaignSiegeAssault = isExactCampaignSiegeAssault;
            IsRemoteClient = isRemoteClient;
            IsPlayerControlled = isPlayerControlled;
            IsLocalAttacker = isLocalAttacker;
            ObjectKind = objectKind;
            AuthoritativeIdentityCount = authoritativeIdentityCount;
            LocalLadderCount = localLadderCount;
            LocalPointCount = localPointCount;
            IsBijectiveMapping = isBijectiveMapping;
            LadderState = ladderState;
            RootDisabled = rootDisabled;
            RootDestroyed = rootDestroyed;
            RootDeactivated = rootDeactivated;
            RootVisible = rootVisible;
            AuthoritativePointDeactivated = authoritativePointDeactivated;
            AuthoritativePointDisabledForPlayers = authoritativePointDisabledForPlayers;
            AuthoritativePointHasUser = authoritativePointHasUser;
            LocalPointDeactivated = localPointDeactivated;
            LocalPointDisabledForPlayers = localPointDisabledForPlayers;
        }

        public bool IsExactCampaignSiegeAssault { get; }
        public bool IsRemoteClient { get; }
        public bool IsPlayerControlled { get; }
        public bool IsLocalAttacker { get; }
        public CoopSiegeLadderInteractionObjectKind ObjectKind { get; }
        public int AuthoritativeIdentityCount { get; }
        public int LocalLadderCount { get; }
        public int LocalPointCount { get; }
        public bool IsBijectiveMapping { get; }
        public int LadderState { get; }
        public bool RootDisabled { get; }
        public bool RootDestroyed { get; }
        public bool RootDeactivated { get; }
        public bool RootVisible { get; }
        public bool AuthoritativePointDeactivated { get; }
        public bool AuthoritativePointDisabledForPlayers { get; }
        public bool AuthoritativePointHasUser { get; }
        public bool LocalPointDeactivated { get; }
        public bool LocalPointDisabledForPlayers { get; }
    }

    internal readonly struct CoopSiegeLadderInteractionDecision
    {
        public CoopSiegeLadderInteractionDecision(
            bool shouldMutate,
            bool desiredDeactivated,
            bool desiredDisabledForPlayers)
        {
            ShouldMutate = shouldMutate;
            DesiredDeactivated = desiredDeactivated;
            DesiredDisabledForPlayers = desiredDisabledForPlayers;
        }

        public bool ShouldMutate { get; }
        public bool DesiredDeactivated { get; }
        public bool DesiredDisabledForPlayers { get; }
    }

    internal static class CoopSiegeLadderInteractionContract
    {
        public const int OnLandState = 0;

        public static bool TryResolveAttackerPointRole(
            bool hasAttackerTag,
            bool hasDefenderTag,
            bool isStandingPointWithWeaponRequirement,
            bool hasAmmoPickupTag,
            bool hasRightTag,
            bool hasFrontTag,
            out CoopSiegeLadderInteractionPointRole role)
        {
            role = CoopSiegeLadderInteractionPointRole.Invalid;
            if (!hasAttackerTag ||
                hasDefenderTag ||
                isStandingPointWithWeaponRequirement ||
                hasAmmoPickupTag)
            {
                return false;
            }

            if (hasRightTag)
            {
                role = hasFrontTag
                    ? CoopSiegeLadderInteractionPointRole.RightFront
                    : CoopSiegeLadderInteractionPointRole.RightRear;
            }
            else
            {
                role = hasFrontTag
                    ? CoopSiegeLadderInteractionPointRole.LeftFront
                    : CoopSiegeLadderInteractionPointRole.LeftRear;
            }

            return true;
        }

        public static bool IsSupportedObjectKind(CoopSiegeLadderInteractionObjectKind objectKind)
        {
            return objectKind == CoopSiegeLadderInteractionObjectKind.SiegeLadder ||
                   objectKind == CoopSiegeLadderInteractionObjectKind.AttackerStandingPoint;
        }

        public static CoopSiegeLadderInteractionDecision Decide(
            CoopSiegeLadderInteractionDecisionInput input)
        {
            if (!input.IsExactCampaignSiegeAssault ||
                !input.IsRemoteClient ||
                !input.IsPlayerControlled ||
                !input.IsLocalAttacker ||
                input.ObjectKind != CoopSiegeLadderInteractionObjectKind.AttackerStandingPoint ||
                input.AuthoritativeIdentityCount != 1 ||
                input.LocalLadderCount != 1 ||
                input.LocalPointCount != 1 ||
                !input.IsBijectiveMapping)
            {
                return default;
            }

            if (input.AuthoritativePointHasUser)
                return default;

            bool rootAllowsActivation =
                IsAttackerLiftStatePotentiallyUsable(input.LadderState) &&
                !input.RootDisabled &&
                !input.RootDestroyed &&
                !input.RootDeactivated &&
                input.RootVisible;
            bool desiredDeactivated =
                input.AuthoritativePointDeactivated ||
                !rootAllowsActivation;
            bool desiredDisabledForPlayers =
                input.AuthoritativePointDisabledForPlayers;
            bool shouldMutate =
                input.LocalPointDeactivated != desiredDeactivated ||
                input.LocalPointDisabledForPlayers != desiredDisabledForPlayers;

            return new CoopSiegeLadderInteractionDecision(
                shouldMutate,
                desiredDeactivated,
                desiredDisabledForPlayers);
        }

        public static bool IsAttackerLiftStatePotentiallyUsable(int ladderState)
        {
            switch (ladderState)
            {
                case 0: // OnLand
                case 1: // FallToLand
                case 2: // BeingRaised; the authoritative point flag covers the terminal frame.
                case 3: // BeingRaisedStartFromGround
                case 4: // BeingRaisedStopped
                case 7: // BeingPushedBack; the authoritative point flag covers animation state.
                    return true;
                default:
                    return false;
            }
        }
    }
}
