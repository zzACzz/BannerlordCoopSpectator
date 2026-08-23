using CoopSpectator.Infrastructure;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.GameMode
{
    public sealed class MissionMultiplayerCoopSiegeAssaultWithDeploymentClient : MissionMultiplayerGameModeBaseClient
    {
        private bool _hasLoggedFirstMissionTick;

        public override void OnBehaviorInitialize()
        {
            _hasLoggedFirstMissionTick = false;
            ModLogger.Info("MissionMultiplayerCoopSiegeAssaultWithDeploymentClient OnBehaviorInitialize. Scene=" + (Mission?.SceneName ?? "null"));
            base.OnBehaviorInitialize();
            CoopBattlePhaseRuntimeState.StartMission(
                Mission,
                "CoopSiegeAssaultWithDeploymentClient.OnBehaviorInitialize");
        }

        public override void AfterStart()
        {
            ModLogger.Info(
                "MissionMultiplayerCoopSiegeAssaultWithDeploymentClient AfterStart ENTER. " +
                "Scene=" + (Mission?.SceneName ?? "null") +
                " GameType=" + GameType);
            base.AfterStart();
            ModLogger.Info(
                "MissionMultiplayerCoopSiegeAssaultWithDeploymentClient AfterStart EXIT. " +
                "Scene=" + (Mission?.SceneName ?? "null") +
                " GameType=" + GameType);
        }

        public override bool IsGameModeUsingGold => true;

        public override bool IsGameModeTactical => true;

        public override bool IsGameModeUsingRoundCountdown => true;

        public override MultiplayerGameType GameType => MultiplayerGameType.Siege;

        public override int GetGoldAmount() => 0;

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            if (_hasLoggedFirstMissionTick)
                return;

            _hasLoggedFirstMissionTick = true;
            ModLogger.Info(
                "MissionMultiplayerCoopSiegeAssaultWithDeploymentClient first mission tick entered. " +
                "Scene=" + (Mission?.SceneName ?? "null") +
                " GameType=" + GameType +
                " Mode=" + (Mission?.Mode.ToString() ?? "null"));
        }

        public override void OnGoldAmountChangedForRepresentative(MissionRepresentativeBase representative, int newAmount)
        {
        }
    }
}
