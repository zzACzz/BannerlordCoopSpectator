using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using HarmonyLib;
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
        public static void Apply(Harmony harmony)
        {
            try
            {
                MethodInfo target = AccessTools.Method(
                    typeof(MultiplayerAgentStatCalculateModel),
                    "UpdateAgentStats",
                    new[] { typeof(Agent), typeof(AgentDrivenProperties) });

                if (target == null)
                {
                    ModLogger.Info("CampaignCombatProfileAgentStatsPatch: MultiplayerAgentStatCalculateModel.UpdateAgentStats not found. Skip.");
                    return;
                }

                MethodInfo postfix = typeof(CampaignCombatProfileAgentStatsPatch).GetMethod(
                    nameof(UpdateAgentStats_Postfix),
                    BindingFlags.Static | BindingFlags.NonPublic);

                if (postfix == null)
                {
                    ModLogger.Info("CampaignCombatProfileAgentStatsPatch: postfix method not found. Skip.");
                    return;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                ModLogger.Info("CampaignCombatProfileAgentStatsPatch: postfix applied to MultiplayerAgentStatCalculateModel.UpdateAgentStats.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("CampaignCombatProfileAgentStatsPatch.Apply failed.", ex);
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
    }
}
