using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    public static class ExactCampaignArmyBootstrapPatch
    {
        private static string _lastInitSideOverrideKey;

        public static void Apply(Harmony harmony)
        {
            try
            {
                MethodInfo target = AccessTools.PropertyGetter(typeof(Team), nameof(Team.Side));
                MethodInfo postfix = AccessTools.Method(
                    typeof(ExactCampaignArmyBootstrapPatch),
                    nameof(Team_Side_Postfix));
                if (target == null || postfix == null)
                {
                    ModLogger.Info("ExactCampaignArmyBootstrapPatch: Team.Side getter not found. Skip.");
                    return;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                ModLogger.Info("ExactCampaignArmyBootstrapPatch: postfix applied to Team.Side.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("ExactCampaignArmyBootstrapPatch.Apply failed.", ex);
            }
        }

        private static void Team_Side_Postfix(Team __instance, ref BattleSideEnum __result)
        {
            if (__instance?.Mission == null)
                return;

            if (!ExactCampaignArmyBootstrap.TryGetSpawnLogicInitTeamSideOverride(
                    __instance.Mission,
                    __result,
                    out BattleSideEnum overrideSide))
            {
                return;
            }

            __result = overrideSide;

            string logKey =
                (__instance.Mission.SceneName ?? "null") + "|" +
                __instance.TeamIndex + "|" +
                overrideSide;
            if (string.Equals(_lastInitSideOverrideKey, logKey, StringComparison.Ordinal))
                return;

            _lastInitSideOverrideKey = logKey;
            ModLogger.Info(
                "ExactCampaignArmyBootstrapPatch: remapped Team.Side=None during MissionAgentSpawnLogic init for exact campaign bootstrap. " +
                "Scene=" + (__instance.Mission.SceneName ?? "null") +
                " TeamIndex=" + __instance.TeamIndex +
                " OverrideSide=" + overrideSide);
        }
    }
}
