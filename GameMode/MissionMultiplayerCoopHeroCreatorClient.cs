using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Multiplayer;

namespace CoopSpectator.GameMode
{
    public sealed class MissionMultiplayerCoopHeroCreatorClient : MissionMultiplayerGameModeBaseClient
    {
        public override bool IsGameModeUsingRoundCountdown => false;
        public override MultiplayerGameType GameType => MultiplayerGameType.TeamDeathmatch;
        public override bool IsGameModeUsingGold => false;
        public override bool IsGameModeTactical => false;
        public override int GetGoldAmount() => 0;
        public override void OnGoldAmountChangedForRepresentative(MissionRepresentativeBase representative, int newAmount) { }
    }
}
