using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;

namespace CoopSpectator.Campaign.Capture
{
    [HarmonyPatch(typeof(MapEvent), "CaptureDefeatedPartyMembers")]
    internal static class ExactCampaignHeroCaptureDistributionPatch
    {
        private static void Prefix(MapEvent __instance)
        {
            try
            {
                if (!ExactCampaignHeroCaptureRuntime.TryPrepareForNativeDistribution(
                        __instance,
                        out string diagnostics))
                {
                    return;
                }

                ModLogger.Info(
                    "ExactCampaignHeroCaptureDistributionPatch: evaluated exact campaign hero capture before " +
                    "native capture distribution. " + diagnostics + ".");
            }
            catch (System.Exception ex)
            {
                ModLogger.Info(
                    "ExactCampaignHeroCaptureDistributionPatch: failed to prepare exact campaign hero capture; " +
                    "native capture distribution will continue. Error=" + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(PlayerEncounter), "DoCaptureHeroes")]
    internal static class ExactCampaignHeroCaptureConversationPatch
    {
        private static void Prefix()
        {
            try
            {
                if (!ExactCampaignHeroCaptureRuntime.TryReconcileBeforeNativeConversation(
                        PlayerEncounter.Battle,
                        out string diagnostics))
                {
                    return;
                }

                ModLogger.Info(
                    "ExactCampaignHeroCaptureConversationPatch: evaluated exact campaign hero capture before " +
                    "native captured-lord conversation. " + diagnostics + ".");
            }
            catch (System.Exception ex)
            {
                ModLogger.Info(
                    "ExactCampaignHeroCaptureConversationPatch: failed to reconcile exact campaign hero capture; " +
                    "native captured-lord flow will continue. Error=" + ex.Message);
            }
        }
    }
}
