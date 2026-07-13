using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Settlements;

namespace CoopSpectator.Patches
{
    [HarmonyPatch(typeof(Settlement), nameof(Settlement.SetNextSiegeState))]
    internal static class ExactSiegeStageAdvancePatch
    {
        private static bool Prefix(Settlement __instance)
        {
            if (!ExactSiegeStageOutcomeRuntimeState.TryConsume(
                    __instance?.StringId,
                    out bool allowStageAdvance,
                    out string diagnostics))
            {
                return true;
            }

            ModLogger.Info(
                "ExactSiegeStageAdvancePatch: consumed authoritative wall-stage outcome. " +
                diagnostics +
                " Action=" + (allowStageAdvance
                    ? "allow-native-SetNextSiegeState"
                    : "block-native-SetNextSiegeState") + ".");
            return allowStageAdvance;
        }
    }
}
