using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Multiplayer;

namespace CoopSpectator.GameMode
{
    internal sealed class MissionMultiplayerScoreboardServerBridge :
        MissionMultiplayerGameModeBaseClient
    {
        private readonly MultiplayerGameType _gameType;

        internal MissionMultiplayerScoreboardServerBridge(MultiplayerGameType gameType)
        {
            _gameType = gameType;
        }

        public override bool IsGameModeUsingRoundCountdown => false;

        public override MultiplayerGameType GameType => _gameType;

        public override bool IsGameModeUsingGold => false;

        public override bool IsGameModeTactical => false;

        public override int GetGoldAmount() => 0;

        public override void OnGoldAmountChangedForRepresentative(
            MissionRepresentativeBase representative,
            int newAmount)
        {
        }
    }
}
