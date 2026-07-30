using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;

namespace CoopSpectator.Campaign.LandBattle
{
    [HarmonyPatch(typeof(MapEvent), "CaptureDefeatedPartyMembers")]
    internal static class ExactLandBattleHeroCaptureDistributionPatch
    {
        private static void Prefix(MapEvent __instance)
        {
            try
            {
                if (!ExactLandBattleCampaignBattleAdapter.TryPrepareFinalFieldBattleHeroCapturesForNativeDistribution(
                        __instance,
                        out string diagnostics))
                {
                    return;
                }

                ModLogger.Info(
                    "ExactLandBattleHeroCaptureDistributionPatch: evaluated final field-battle hero capture before " +
                    "native capture distribution. " + diagnostics + ".");
            }
            catch (System.Exception ex)
            {
                ModLogger.Info(
                    "ExactLandBattleHeroCaptureDistributionPatch: failed to prepare final field-battle hero capture; " +
                    "native capture distribution will continue. Error=" + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(PlayerEncounter), "DoCaptureHeroes")]
    internal static class ExactLandBattleHeroCapturePatch
    {
        private static void Prefix()
        {
            try
            {
                if (!ExactLandBattleCampaignBattleAdapter.TryReconcileFinalFieldBattleHeroCaptures(
                        PlayerEncounter.Battle,
                        out string diagnostics))
                {
                    return;
                }

                ModLogger.Info(
                    "ExactLandBattleHeroCapturePatch: evaluated final field-battle hero capture contract. " +
                    diagnostics + ".");
            }
            catch (System.Exception ex)
            {
                ModLogger.Info(
                    "ExactLandBattleHeroCapturePatch: failed to reconcile final field-battle hero capture; " +
                    "native captured-lord flow will continue. Error=" + ex.Message);
            }
        }
    }
}
