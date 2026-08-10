using CoopSpectator.Infrastructure.Hideout;

namespace CoopSpectator.Infrastructure
{
    internal static class CampaignCasualtyPolicy
    {
        private const int BattleDeathDisabled = 0;
        private const int BattleDeathPlayerProtected = 1;

        public static bool SupportsScenario(
            bool isSiegeBattle,
            bool isExactLandBattle,
            string scenarioKind)
        {
            return isSiegeBattle ||
                   isExactLandBattle ||
                   CoopHideoutBossPhaseContract.IsHideoutScenario(scenarioKind) ||
                   CoopHideoutAmbushContract.IsHideoutAmbushScenario(scenarioKind);
        }

        public static bool AllowsHeroDeath(
            int battleDeathDifficulty,
            bool isPlayerCharacter,
            bool heroCanDieInBattle)
        {
            if (!heroCanDieInBattle || battleDeathDifficulty <= BattleDeathDisabled)
                return false;

            if (battleDeathDifficulty == BattleDeathPlayerProtected &&
                isPlayerCharacter)
            {
                return false;
            }

            return true;
        }
    }
}
