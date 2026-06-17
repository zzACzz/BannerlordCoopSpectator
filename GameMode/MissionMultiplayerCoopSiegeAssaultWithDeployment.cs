using System;
using CoopSpectator.Infrastructure;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.GameMode
{
    public sealed class MissionMultiplayerCoopSiegeAssaultWithDeployment : MissionMultiplayerGameModeBase
    {
        private bool _hasLoggedFirstServerTick;

        public override MultiplayerGameType GetMissionType() => MultiplayerGameType.Siege;

        public override bool IsGameModeUsingOpposingTeams => true;

        public override bool IsGameModeHidingAllAgentVisuals => true;

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            _hasLoggedFirstServerTick = false;
            CoopBattlePhaseRuntimeState.StartMission(Mission, "CoopSiegeAssaultWithDeployment.OnBehaviorInitialize");
        }

        public override void AfterStart()
        {
            base.AfterStart();

            if (!GameNetwork.IsServer)
                return;

            try
            {
                const string source = "CoopSiegeAssaultWithDeployment.AfterStart";
                MissionMultiplayerCoopBattle.TryApplyAuthoritativeBattleCultureOptionsFromRuntimeState(source);
                if (ExperimentalFeatures.EnableSiegeReplayServerTeamBootstrap)
                {
                    MissionMultiplayerCoopBattle.EnsureOpposingTeamsReadyForMission(Mission, source);
                }
                else
                {
                    ModLogger.Info(
                        "CoopSiegeAssaultWithDeployment server: skipped battle-flow team bootstrap for siege replay server isolation. " +
                        "Scene=" + (Mission?.SceneName ?? "null") +
                        " HasAttacker=" + (Mission?.Teams?.Attacker != null) +
                        " HasDefender=" + (Mission?.Teams?.Defender != null));
                }
                ModLogger.Info(
                    "CoopSiegeAssaultWithDeployment server: AfterStart completed. " +
                    "Scene=" + (Mission?.SceneName ?? "null") +
                    " Mode=" + (Mission?.Mode.ToString() ?? "null") +
                    " HasAttacker=" + (Mission?.Teams?.Attacker != null) +
                    " HasDefender=" + (Mission?.Teams?.Defender != null));
            }
            catch (Exception ex)
            {
                ModLogger.Error("CoopSiegeAssaultWithDeployment server: AfterStart failed.", ex);
            }
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            if (!GameNetwork.IsServer || _hasLoggedFirstServerTick)
                return;

            _hasLoggedFirstServerTick = true;
            ModLogger.Info(
                "CoopSiegeAssaultWithDeployment server: first mission tick entered. " +
                "Scene=" + (Mission?.SceneName ?? "null") +
                " Mode=" + (Mission?.Mode.ToString() ?? "null") +
                " MissionType=" + GetMissionType());
        }
    }
}
