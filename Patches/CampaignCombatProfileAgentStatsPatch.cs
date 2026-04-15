using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Applies campaign-derived combat-profile skill deltas after vanilla MP agent stat calculation.
    /// This keeps the vanilla runtime shell intact and only overlays driven-property changes
    /// for materialized agents that were registered from a live battle snapshot.
    /// </summary>
    public static class CampaignCombatProfileAgentStatsPatch
    {
        [ThreadStatic]
        private static int _suppressWeaponDamagePostfixDepth;

        public static void Apply(Harmony harmony)
        {
            try
            {
                ApplyUpdateAgentStatsPatch(harmony);
                ApplyWeaponDamageMultiplierPatch(harmony);
            }
            catch (Exception ex)
            {
                ModLogger.Error("CampaignCombatProfileAgentStatsPatch.Apply failed.", ex);
            }
        }

        public static bool ApplyWeaponDamageOnly(Harmony harmony)
        {
            try
            {
                return ApplyWeaponDamageMultiplierPatch(harmony);
            }
            catch (Exception ex)
            {
                ModLogger.Error("CampaignCombatProfileAgentStatsPatch.ApplyWeaponDamageOnly failed.", ex);
                return false;
            }
        }

        private static void ApplyUpdateAgentStatsPatch(Harmony harmony)
        {
            MethodInfo updateAgentStatsTarget = AccessTools.Method(
                typeof(MultiplayerAgentStatCalculateModel),
                "UpdateAgentStats",
                new[] { typeof(Agent), typeof(AgentDrivenProperties) });

            if (updateAgentStatsTarget == null)
            {
                ModLogger.Info("CampaignCombatProfileAgentStatsPatch: MultiplayerAgentStatCalculateModel.UpdateAgentStats not found. Skip.");
                return;
            }

            MethodInfo updateAgentStatsPostfix = typeof(CampaignCombatProfileAgentStatsPatch).GetMethod(
                nameof(UpdateAgentStats_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);

            if (updateAgentStatsPostfix == null)
            {
                ModLogger.Info("CampaignCombatProfileAgentStatsPatch: UpdateAgentStats postfix method not found. Skip.");
                return;
            }

            harmony.Patch(updateAgentStatsTarget, postfix: new HarmonyMethod(updateAgentStatsPostfix));
            ModLogger.Info("CampaignCombatProfileAgentStatsPatch: postfix applied to MultiplayerAgentStatCalculateModel.UpdateAgentStats.");
        }

        private static bool ApplyWeaponDamageMultiplierPatch(Harmony harmony)
        {
            MethodInfo weaponDamageTarget = AccessTools.Method(
                typeof(MultiplayerAgentStatCalculateModel),
                "GetWeaponDamageMultiplier",
                new[] { typeof(Agent), typeof(WeaponComponentData) });

            if (weaponDamageTarget == null)
            {
                ModLogger.Info("CampaignCombatProfileAgentStatsPatch: MultiplayerAgentStatCalculateModel.GetWeaponDamageMultiplier not found. Skip.");
                return false;
            }

            MethodInfo weaponDamagePostfix = typeof(CampaignCombatProfileAgentStatsPatch).GetMethod(
                nameof(GetWeaponDamageMultiplier_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);

            if (weaponDamagePostfix == null)
            {
                ModLogger.Info("CampaignCombatProfileAgentStatsPatch: GetWeaponDamageMultiplier postfix method not found. Skip.");
                return false;
            }

            harmony.Patch(weaponDamageTarget, postfix: new HarmonyMethod(weaponDamagePostfix));
            ModLogger.Info("CampaignCombatProfileAgentStatsPatch: postfix applied to MultiplayerAgentStatCalculateModel.GetWeaponDamageMultiplier.");
            return true;
        }

        internal static float InvokeWithoutWeaponDamagePostfix(Func<float> callback)
        {
            if (callback == null)
                return 0f;

            _suppressWeaponDamagePostfixDepth++;
            try
            {
                return callback();
            }
            finally
            {
                _suppressWeaponDamagePostfixDepth--;
            }
        }

        private static void UpdateAgentStats_Postfix(Agent agent, AgentDrivenProperties agentDrivenProperties)
        {
            try
            {
                CoopMissionSpawnLogic.TryApplyDrivenSkillCombatProfile(agent, agentDrivenProperties);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CampaignCombatProfileAgentStatsPatch: postfix failed: " + ex.Message);
            }
        }

        private static void GetWeaponDamageMultiplier_Postfix(Agent agent, WeaponComponentData weapon, ref float __result)
        {
            try
            {
                if (_suppressWeaponDamagePostfixDepth > 0)
                    return;

                CoopMissionSpawnLogic.TryApplyWeaponDamageMultiplierCombatProfile(agent, weapon, ref __result);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CampaignCombatProfileAgentStatsPatch: GetWeaponDamageMultiplier postfix failed: " + ex.Message);
            }
        }
    }
}
