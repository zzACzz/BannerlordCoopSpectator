using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;

namespace CoopSpectator.Campaign.SallyOut
{
    [HarmonyPatch(typeof(PlayerEncounter), "CheckIfBattleShouldContinueAfterBattleMission")]
    internal static class SallyOutEncounterContinuationPatch
    {
        private static bool Prefix(ref bool __result)
        {
            try
            {
                CampaignBattleResult campaignBattleResult = PlayerEncounter.CampaignBattleResult;
                if (campaignBattleResult?.BattleResolved != true)
                    return true;

                MapEvent battle = PlayerEncounter.Battle;
                if (!SallyOutCampaignBattleAdapter.TryConsumeFinalEncounterCompletion(
                        battle,
                        out string diagnostics))
                {
                    return true;
                }

                __result = false;
                ModLogger.Info(
                    "SallyOutEncounterContinuationPatch: consumed final encounter completion contract. " +
                    diagnostics +
                    " Action=force-terminal-native-aftermath.");
                return false;
            }
            catch (System.Exception ex)
            {
                ModLogger.Info(
                    "SallyOutEncounterContinuationPatch: failed to evaluate final encounter completion contract; " +
                    "native continuation check will run. Error=" + ex.Message);
                return true;
            }
        }
    }
}
