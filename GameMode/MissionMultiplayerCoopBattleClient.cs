using TaleWorlds.MountAndBlade; // Підключаємо GameNetwork
using TaleWorlds.MountAndBlade.Multiplayer; // Підключаємо MissionMultiplayerGameModeBaseClient, MissionRepresentativeBase, MultiplayerGameType

namespace CoopSpectator.GameMode
{
    /// <summary>
    /// Клієнтська логіка режиму CoopBattle: обробка мережевих повідомлень і синхронізація з сервером.
    /// Зараз мінімальний — реалізовані лише обов'язкові abstract-члени; далі додамо хендлери.
    /// </summary>
    public sealed class MissionMultiplayerCoopBattleClient : MissionMultiplayerGameModeBaseClient
    {
        /// <summary>Режим не використовує round countdown (простий бій до кінця).</summary>
        public override bool IsGameModeUsingRoundCountdown => false;

        /// <summary>Тип гри — Custom (наш CoopBattle).</summary>
        public override MultiplayerGameType GameType => MultiplayerGameType.TeamDeathmatch; // Відповідає серверному GetMissionType()

        /// <summary>Золото в цьому режимі не використовується.</summary>
        public override bool IsGameModeUsingGold => false;

        /// <summary>Режим не тактичний (звичайний бій, не командирський).</summary>
        public override bool IsGameModeTactical => false;

        /// <summary>Повертає поточну кількість золота (у нас не використовується — 0).</summary>
        public override int GetGoldAmount() => 0;

        /// <summary>Викликається при зміні золота у представника (у нашому режимі нічого не робимо).</summary>
        public override void OnGoldAmountChangedForRepresentative(MissionRepresentativeBase representative, int newAmount)
        {
        }
    }
}
