using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Multiplayer;

namespace CoopSpectator.GameMode
{
    public sealed class MissionMultiplayerCoopHeroCreatorMode : MissionBasedMultiplayerGameMode
    {
        public const string GameModeId = "CoopHeroCreator";

        public MissionMultiplayerCoopHeroCreatorMode(string name) : base(name) { }

        public override void StartMultiplayerGame(string scene)
        {
            ModLogger.Info("CoopHeroCreator: opening isolated creator mission. Scene=" + (scene ?? string.Empty) + ".");
            MissionInitializerRecord record = new MissionInitializerRecord(scene);
            MissionState.OpenNew(GameModeId, record, CreateBehaviorsForMission);
        }

        private static IEnumerable<MissionBehavior> CreateBehaviorsForMission(Mission mission)
        {
            List<MissionBehavior> list = new List<MissionBehavior>
            {
                MissionLobbyComponent.CreateBehavior()
            };
            if (GameNetwork.IsServer)
                list.Add(new MissionMultiplayerCoopHeroCreator());
            list.Add(new MissionMultiplayerCoopHeroCreatorClient());

            list.Add(new MultiplayerTimerComponent());
            MissionBehavior visualSpawn = null;
            if (!GameNetwork.IsServer)
                visualSpawn = MissionBehaviorHelpers.TryCreateMissionAgentVisualSpawnComponent();
            if (visualSpawn != null)
            {
                list.Add(visualSpawn);
                list.Add(new MissionLobbyEquipmentNetworkComponent());
            }
            list.Add(new MultiplayerTeamSelectComponent());
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateHardBorderPlacer());
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBoundaryPlacer());
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBoundaryCrossingHandler(mission));
            list.Add(new MultiplayerPollComponent());
            list.Add(new MultiplayerAdminComponent());
            if (!GameNetwork.IsServer)
                list.Add(new MultiplayerGameNotificationsComponent());
            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateMissionOptionsComponent(mission));
            if (GameNetwork.IsServer)
            {
                MissionBehavior scoreboard = MissionBehaviorHelpers.TryCreateMissionScoreboardComponent();
                if (scoreboard != null)
                    list.Add(scoreboard);
                else
                    ModLogger.Error(
                        "CoopHeroCreator server: MissionScoreboardComponent is required by MissionCustomGameServerComponent.AfterStart but could not be created.",
                        null);
            }
            else
            {
                AddIfNotNull(
                    list,
                    MissionBehaviorHelpers.TryCreateMissionMultiplayerEscapeMenu(
                        CoopGameModeIds.OfficialTeamDeathmatch));
            }
            list.Add(new CoopHeroCreationMissionNetwork());
#if !COOPSPECTATOR_DEDICATED
            if (!GameNetwork.IsServer)
                list.Add(new CoopSpectator.UI.CoopHeroCreatorMissionView());
#endif
            return list;
        }

        private static void AddIfNotNull(List<MissionBehavior> list, MissionBehavior behavior)
        {
            if (behavior != null) list.Add(behavior);
        }
    }
}
