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
        private bool _hasLoggedFirstServerTick;

        public override MultiplayerGameType GetMissionType()
        {
            return MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(Mission?.SceneName)
                ? MultiplayerGameType.Battle
                : MultiplayerGameType.TeamDeathmatch;
        }

        public override bool IsGameModeUsingOpposingTeams => true;

        public override bool IsGameModeHidingAllAgentVisuals => false;

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            _hasInitialized = false;
            _hasLoggedFirstServerTick = false;
            CoopBattlePhaseRuntimeState.StartMission(Mission, "CoopBattle.OnBehaviorInitialize");
        }

        public override void AfterStart()
        {
            if (!GameNetwork.IsServer)
            {
                base.AfterStart();
                return;
            }

            try
            {
                ModLogger.Info("CoopBattle server: AfterStart ENTER.");
                EnsureOpposingTeamsReady("CoopBattle.AfterStart pre-base");
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("CoopBattle server: EnsureOpposingTeamsReady failed before base.AfterStart.", ex);
            }

            base.AfterStart();

            try
            {
                EnsureOpposingTeamsReady("CoopBattle.AfterStart post-base");
                ModLogger.Info("CoopBattle server: AfterStart EXIT.");
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("CoopBattle server: EnsureOpposingTeamsReady failed after base.AfterStart.", ex);
            }
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            Mission mission = Mission;
            if (mission?.Teams == null)
                return;

            if (!GameNetwork.IsServer)
                return;

            if (!_hasLoggedFirstServerTick)
            {
                _hasLoggedFirstServerTick = true;
                ModLogger.Info(
                    "CoopBattle server: first mission tick entered. " +
                    "Scene=" + (mission.SceneName ?? "null") +
                    " MissionType=" + GetMissionType() +
                    " Mode=" + mission.Mode);
            }

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

            EnsureOpposingTeamsReady("CoopBattle.InitializeTeams");
            ModLogger.Info("CoopBattle mission started (teams initialized; no agents yet).");
        }

        private void EnsureOpposingTeamsReady(string source)
        {
            Mission mission = Mission;
            if (mission == null)
                return;

            if (mission.Teams.Attacker == null)
                mission.Teams.Add(BattleSideEnum.Attacker, 0xFFCC2222u, 0xFF661111u, null, false, false, false);
            if (mission.Teams.Defender == null)
                mission.Teams.Add(BattleSideEnum.Defender, 0xFF2222CCu, 0xFF111166u, null, false, false, false);

            CoopBattlePhaseRuntimeState.AdvanceToAtLeast(CoopBattlePhase.SideSelection, source ?? "CoopBattle.EnsureOpposingTeamsReady", mission);
            ModLogger.Info(
                "CoopBattle server: ensured opposing teams exist. " +
                "Source=" + (source ?? "null") +
                " HasAttacker=" + (mission.Teams.Attacker != null) +
                " HasDefender=" + (mission.Teams.Defender != null));
        }
    }
}
