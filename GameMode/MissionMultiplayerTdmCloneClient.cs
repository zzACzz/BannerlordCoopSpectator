using TaleWorlds.MountAndBlade; // GameNetwork
using TaleWorlds.MountAndBlade.Multiplayer; // MissionMultiplayerGameModeBaseClient, MissionRepresentativeBase, MultiplayerGameType
using CoopSpectator.Infrastructure;

namespace CoopSpectator.GameMode
{
    /// <summary>
    /// Клієнтська логіка TdmClone: клон TDM 1:1 — ті самі прапорці що й у CoopTdm (round countdown, gold, тощо).
    /// Єдина відмінність — ім'я режиму для узгодження з реєстрацією та конфігом дедика.
    /// </summary>
    public sealed class MissionMultiplayerTdmCloneClient : MissionMultiplayerGameModeBaseClient
    {
        public override bool IsGameModeUsingRoundCountdown => false;
        public override MultiplayerGameType GameType => MultiplayerGameType.TeamDeathmatch;
        public override bool IsGameModeUsingGold => false;
        public override bool IsGameModeTactical => false;
        public override int GetGoldAmount() => 0;

        public override void OnBehaviorInitialize()
        {
            ModLogger.Info("MissionMultiplayerTdmCloneClient OnBehaviorInitialize ENTER");
            base.OnBehaviorInitialize();
            ModLogger.Info("MissionMultiplayerTdmCloneClient OnBehaviorInitialize EXIT");
        }

        public override void AfterStart()
        {
            ModLogger.Info("MissionMultiplayerTdmCloneClient AfterStart ENTER");
            base.AfterStart();
            ModLogger.Info("MissionMultiplayerTdmCloneClient AfterStart EXIT");
        }

        public override void OnGoldAmountChangedForRepresentative(MissionRepresentativeBase representative, int newAmount)
        {
        }
    }
}
