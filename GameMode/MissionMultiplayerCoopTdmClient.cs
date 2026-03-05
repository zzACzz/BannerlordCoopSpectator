using TaleWorlds.MountAndBlade; // GameNetwork
using TaleWorlds.MountAndBlade.Multiplayer; // MissionMultiplayerGameModeBaseClient, MissionRepresentativeBase, MultiplayerGameType

namespace CoopSpectator.GameMode
{
    /// <summary>
    /// Клієнтська логіка CoopTdm: клон TDM 1:1 — ті самі прапорці що й у ванільному TDM (round countdown, gold, тощо можна змінити пізніше).
    /// </summary>
    public sealed class MissionMultiplayerCoopTdmClient : MissionMultiplayerGameModeBaseClient
    {
        public override bool IsGameModeUsingRoundCountdown => false;
        public override MultiplayerGameType GameType => MultiplayerGameType.TeamDeathmatch;
        public override bool IsGameModeUsingGold => false;
        public override bool IsGameModeTactical => false;
        public override int GetGoldAmount() => 0;

        public override void OnGoldAmountChangedForRepresentative(MissionRepresentativeBase representative, int newAmount)
        {
        }
    }
}
