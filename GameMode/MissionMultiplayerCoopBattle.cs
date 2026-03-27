using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Multiplayer;

namespace CoopSpectator.GameMode
{
    public sealed class MissionMultiplayerCoopBattle : MissionMultiplayerGameModeBase
    {
        private bool _hasInitialized;

        public override MultiplayerGameType GetMissionType()
        {
            return MultiplayerGameType.TeamDeathmatch;
        }

        public override bool IsGameModeUsingOpposingTeams => true;

        public override bool IsGameModeHidingAllAgentVisuals => false;

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            _hasInitialized = false;
            CoopBattlePhaseRuntimeState.StartMission(Mission, "CoopBattle.OnBehaviorInitialize");
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            Mission mission = Mission;
            if (mission?.Teams == null)
                return;

            if (!GameNetwork.IsServer)
                return;

            if (!_hasInitialized)
            {
                _hasInitialized = true;
                try
                {
                    InitializeTeamsAndMinimalSpawn();
                    CoopMissionSpawnLogic.RunCoopBattleSpawnOwnerTick(mission, "CoopBattle.OnMissionTick initialize");
                    CoopMissionSpawnLogic.RunCoopBattlePhaseOwnerTick(mission, "CoopBattle.OnMissionTick initialize");
                }
                catch (System.Exception ex)
                {
                    ModLogger.Error("CoopBattle server: InitializeTeamsAndMinimalSpawn failed.", ex);
                }

                return;
            }

            try
            {
                CoopMissionSpawnLogic.RunCoopBattleSpawnOwnerTick(mission, "CoopBattle.OnMissionTick");
                CoopMissionSpawnLogic.RunCoopBattlePhaseOwnerTick(mission, "CoopBattle.OnMissionTick");
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("CoopBattle server: phase owner tick failed.", ex);
            }
        }

        private void InitializeTeamsAndMinimalSpawn()
        {
            Mission mission = Mission;
            if (mission == null)
                return;

            if (mission.Teams.Attacker == null)
                mission.Teams.Add(BattleSideEnum.Attacker, 0xFFCC2222u, 0xFF661111u, null, false, false, false);
            if (mission.Teams.Defender == null)
                mission.Teams.Add(BattleSideEnum.Defender, 0xFF2222CCu, 0xFF111166u, null, false, false, false);

            CoopBattlePhaseRuntimeState.AdvanceToAtLeast(CoopBattlePhase.SideSelection, "CoopBattle.InitializeTeams", mission);
            ModLogger.Info("CoopBattle mission started (teams initialized; no agents yet).");
        }
    }
}
