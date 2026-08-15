namespace CoopSpectator.Infrastructure
{
    internal enum CoopNativeAftermathContributionEventDecision
    {
        Ignore = 0,
        Apply = 1,
        SkipUnresolvedCharacter = 2,
        AbortUnresolvedWinnerParty = 3
    }

    internal static class CoopNativeAftermathContributionContract
    {
        public static CoopNativeAftermathContributionEventDecision EvaluateCombatEvent(
            bool eventClaimsWinnerSide,
            bool attackerPartyResolved,
            bool attackerPartyIsWinner,
            bool isTeamKill,
            bool attackerCharacterResolved,
            bool victimCharacterResolved)
        {
            if (!attackerPartyResolved)
            {
                return eventClaimsWinnerSide
                    ? CoopNativeAftermathContributionEventDecision.AbortUnresolvedWinnerParty
                    : CoopNativeAftermathContributionEventDecision.Ignore;
            }

            if (!attackerPartyIsWinner || isTeamKill)
                return CoopNativeAftermathContributionEventDecision.Ignore;

            if (!attackerCharacterResolved || !victimCharacterResolved)
                return CoopNativeAftermathContributionEventDecision.SkipUnresolvedCharacter;

            return CoopNativeAftermathContributionEventDecision.Apply;
        }
    }
}
