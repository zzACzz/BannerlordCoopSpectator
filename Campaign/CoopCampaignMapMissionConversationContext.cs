using System;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;
using CoopSpectator.Infrastructure;

namespace CoopSpectator.Campaign
{
    // Host-only native adapter. A conversation mode alone is insufficient: the
    // hideout boss and settlement conversations have different lifecycles.
    internal static class CoopCampaignMapMissionConversationContext
    {
        internal static bool IsStandaloneOpening(Mission mission, string missionName)
        {
            if (mission == null || missionName != "Conversation" ||
                GameNetwork.IsClientOrReplay || GameNetwork.IsServer ||
                TaleWorlds.CampaignSystem.Campaign.Current == null ||
                !IsLandConversationScene(mission.SceneName)) return false;
            foreach (MissionBehavior behavior in mission.MissionBehaviors)
            {
                Type type = behavior?.GetType();
                if (type?.FullName == "SandBox.Conversation.MissionLogics.ConversationMissionLogic" &&
                    type.Assembly.GetName().Name == "SandBox") return true;
            }
            return false;
        }

        internal static bool IsLandConversationScene(string scene) =>
            CoopCampaignMapConversationMissionScene.IsAllowedScene(scene);

        internal static bool TryGetActiveView(Mission mission, object handler, out MissionView view)
        {
            view = null;
            MissionState state = MissionState.Current;
            if (state == null || !ReferenceEquals(state.CurrentMission, mission) ||
                !ReferenceEquals(Mission.Current, mission) ||
                !IsStandaloneOpening(mission, state.MissionName) ||
                mission.CurrentState != Mission.State.Continuing || mission.MissionEnded ||
                mission.Mode != MissionMode.Conversation) return false;
            var candidate = handler as MissionView;
            Type type = candidate?.GetType();
            if (type?.FullName != "SandBox.GauntletUI.Missions.MissionGauntletConversationView" ||
                type.Assembly.GetName().Name != "SandBox.GauntletUI" ||
                !ReferenceEquals(candidate.Mission, mission) || candidate.MissionScreen == null ||
                !candidate.MissionScreen.IsConversationMission) return false;
            view = candidate;
            return true;
        }
    }
}
