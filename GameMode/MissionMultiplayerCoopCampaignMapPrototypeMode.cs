using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Multiplayer;

namespace CoopSpectator.GameMode
{
    public sealed class MissionMultiplayerCoopCampaignMapPrototypeMode :
        MissionBasedMultiplayerGameMode
    {
        public const string GameModeId =
            CoopCampaignMapPrototypeContract.GameModeId;

        public MissionMultiplayerCoopCampaignMapPrototypeMode(string name)
            : base(name)
        {
        }

        public override void StartMultiplayerGame(string scene)
        {
            if (!ExperimentalFeatures.EnableCampaignMapPrototype)
            {
                ModLogger.Info(
                    "CoopCampaignMapPrototype: mission start rejected because the explicit feature flag is disabled.");
                return;
            }

            ModLogger.Info(
                "CoopCampaignMapPrototype: opening isolated network mission. BootstrapScene=" +
                (scene ?? string.Empty) + ".");
            MissionInitializerRecord record = new MissionInitializerRecord(scene);
            MissionState.OpenNew(GameModeId, record, CreateBehaviorsForMission);
        }

        private static IEnumerable<MissionBehavior> CreateBehaviorsForMission(
            Mission mission)
        {
            List<MissionBehavior> list = new List<MissionBehavior>
            {
                MissionLobbyComponent.CreateBehavior()
            };
            if (GameNetwork.IsServer)
                list.Add(new MissionMultiplayerCoopCampaignMapPrototype());
            list.Add(new MissionMultiplayerCoopCampaignMapPrototypeClient());
            list.Add(new MultiplayerTimerComponent());
            list.Add(new MultiplayerTeamSelectComponent());
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateHardBorderPlacer());
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBoundaryPlacer());
            AddIfNotNull(
                list,
                MissionBehaviorHelpers.TryCreateBoundaryCrossingHandler(mission));
            list.Add(new MultiplayerPollComponent());
            list.Add(new MultiplayerAdminComponent());
            if (!GameNetwork.IsServer)
                list.Add(new MultiplayerGameNotificationsComponent());
            AddIfNotNull(
                list,
                MissionBehaviorHelpers.TryCreateMissionOptionsComponent(mission));
            if (GameNetwork.IsServer)
            {
                MissionBehavior scoreboard =
                    MissionBehaviorHelpers.TryCreateMissionScoreboardComponent();
                if (scoreboard != null)
                    list.Add(scoreboard);
                else
                    ModLogger.Error(
                        "CoopCampaignMapPrototype server: MissionScoreboardComponent could not be created.",
                        null);
            }
            else
            {
                AddIfNotNull(
                    list,
                    MissionBehaviorHelpers.TryCreateMissionMultiplayerEscapeMenu(
                        CoopGameModeIds.OfficialTeamDeathmatch));
            }

            list.Add(new CoopCampaignMapPrototypeNetworkController());
#if !COOPSPECTATOR_DEDICATED
            if (!GameNetwork.IsServer)
                list.Add(new CoopSpectator.UI.CoopCampaignMapPrototypeMissionView());
#endif
            return list;
        }

        private static void AddIfNotNull(
            List<MissionBehavior> list,
            MissionBehavior behavior)
        {
            if (behavior != null)
                list.Add(behavior);
        }
    }
}
