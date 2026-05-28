using System;
using CoopSpectator.Infrastructure;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    internal sealed class ListedShellTeamDeathmatchClientCompatibilityMode : MissionMultiplayerTeamDeathmatchClient
    {
        private static string _lastInitializedMissionKey = string.Empty;

        public override bool IsGameModeUsingGold => false;

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();

            string missionKey =
                (Mission?.SceneName ?? Mission.Current?.SceneName ?? "unknown") +
                "|" +
                (GameNetwork.IsServer ? "server" : "client");
            if (string.Equals(_lastInitializedMissionKey, missionKey, StringComparison.Ordinal))
                return;

            _lastInitializedMissionKey = missionKey;
            ModLogger.Info(
                "ListedShellTeamDeathmatchClientCompatibilityMode: installed listed-shell TDM client compatibility mode without gold/sound authority. " +
                "Mission=" + (Mission?.SceneName ?? Mission.Current?.SceneName ?? "unknown") +
                " Side=" + (GameNetwork.IsServer ? "server" : "client") + ".");
        }

        protected override void AddRemoveMessageHandlers(GameNetwork.NetworkMessageHandlerRegistererContainer registerer)
        {
            // Wrapped listed shell no longer consumes native TDM gold-gain / gold-sync message flow.
        }

        public override void OnGoldAmountChangedForRepresentative(MissionRepresentativeBase representative, int goldAmount)
        {
        }

        public override int GetGoldAmount()
        {
            return 0;
        }
    }
}
