using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    internal static class CampaignlessConversationMissionSafetyPatch
    {
        private const string ConversationMissionTypeName =
            "SandBox.Conversation.ConversationMission";
        private const string OneToOneConversationAgentPropertyName =
            "OneToOneConversationAgent";

        private static bool _isApplied;

        internal static void Apply(Harmony harmony)
        {
            if (harmony == null || _isApplied)
                return;

            try
            {
                Type conversationMissionType =
                    AccessTools.TypeByName(ConversationMissionTypeName);
                if (conversationMissionType == null)
                {
                    throw new TypeLoadException(
                        ConversationMissionTypeName + " was not found.");
                }

                MethodInfo target = AccessTools.PropertyGetter(
                    conversationMissionType,
                    OneToOneConversationAgentPropertyName);
                MethodInfo prefix = AccessTools.Method(
                    typeof(CampaignlessConversationMissionSafetyPatch),
                    nameof(OneToOneConversationAgentPrefix));
                if (target == null || prefix == null)
                {
                    throw new MissingMethodException(
                        ConversationMissionTypeName + ".get_" +
                        OneToOneConversationAgentPropertyName);
                }

                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                _isApplied = true;
                ModLogger.Info(
                    "CampaignlessConversationMissionSafetyPatch: protected " +
                    "ConversationMission.OneToOneConversationAgent when campaign state is unavailable.");
            }
            catch (Exception ex)
            {
                ModLogger.Error(
                    "CampaignlessConversationMissionSafetyPatch: failed to apply conversation mission safety guard.",
                    ex);
            }
        }

        private static bool OneToOneConversationAgentPrefix(ref Agent __result)
        {
#if COOPSPECTATOR_DEDICATED
            const bool hasCampaign = false;
            const bool hasConversationManager = false;
#else
            TaleWorlds.CampaignSystem.Campaign campaign =
                TaleWorlds.CampaignSystem.Campaign.Current;
            bool hasCampaign = campaign != null;
            bool hasConversationManager = campaign?.ConversationManager != null;
#endif
            if (!CampaignlessConversationMissionSafetyContract.ShouldReturnNull(
                    hasCampaign,
                    hasConversationManager))
            {
                return true;
            }

            __result = null;
            return false;
        }
    }
}
