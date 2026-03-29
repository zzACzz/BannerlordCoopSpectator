using CoopSpectator.Infrastructure;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Multiplayer;

namespace CoopSpectator.GameMode
{
    public sealed class MissionMultiplayerCoopBattleClient : MissionMultiplayerGameModeBaseClient
    {
        private bool _hasLoggedFirstMissionTick;

        public override void OnBehaviorInitialize()
        {
            _hasLoggedFirstMissionTick = false;
            ModLogger.Info("MissionMultiplayerCoopBattleClient OnBehaviorInitialize. Scene=" + (Mission?.SceneName ?? "null"));
            base.OnBehaviorInitialize();
        }

        public override void AfterStart()
        {
            ModLogger.Info("MissionMultiplayerCoopBattleClient AfterStart ENTER. Scene=" + (Mission?.SceneName ?? "null") + " GameType=" + GameType);
            base.AfterStart();
            ModLogger.Info("MissionMultiplayerCoopBattleClient AfterStart EXIT. Scene=" + (Mission?.SceneName ?? "null") + " GameType=" + GameType);
        }

        public override bool IsGameModeUsingRoundCountdown => false;

        public override MultiplayerGameType GameType
        {
            get
            {
                return MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(Mission?.SceneName)
                    ? MultiplayerGameType.Battle
                    : MultiplayerGameType.TeamDeathmatch;
            }
        }

        public override bool IsGameModeUsingGold => false;

        public override bool IsGameModeTactical => false;

        public override int GetGoldAmount() => 0;

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            if (_hasLoggedFirstMissionTick)
                return;

            _hasLoggedFirstMissionTick = true;
            ModLogger.Info(
                "MissionMultiplayerCoopBattleClient first mission tick entered. " +
                "Scene=" + (Mission?.SceneName ?? "null") +
                " GameType=" + GameType +
                " Mode=" + (Mission?.Mode.ToString() ?? "null"));
        }

        public override void OnGoldAmountChangedForRepresentative(MissionRepresentativeBase representative, int newAmount)
        {
        }
    }
}
