using System;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.Patches
{
    internal sealed class ListedShellCompatibilityModeClient : MissionMultiplayerGameModeBaseClient
    {
        private static string _lastInitializedMissionKey = string.Empty;
        private MissionRepresentativeBase _myRepresentative;

        public override bool IsGameModeUsingGold => false;

        public override bool IsGameModeTactical => false;

        public override bool IsGameModeUsingRoundCountdown => true;

        public override MultiplayerGameType GameType => MultiplayerGameType.TeamDeathmatch;

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            MissionNetworkComponent.OnMyClientSynchronized += OnMyClientSynchronized;

            string missionKey =
                (Mission?.SceneName ?? Mission.Current?.SceneName ?? "unknown") +
                "|" +
                (GameNetwork.IsServer ? "server" : "client");
            if (string.Equals(_lastInitializedMissionKey, missionKey, StringComparison.Ordinal))
                return;

            _lastInitializedMissionKey = missionKey;
            ModLogger.Info(
                "ListedShellCompatibilityModeClient: installed listed-shell client compatibility mode without TDM gold/sound authority. " +
                "Mission=" + (Mission?.SceneName ?? Mission.Current?.SceneName ?? "unknown") +
                " Side=" + (GameNetwork.IsServer ? "server" : "client") + ".");
        }

        protected override void AddRemoveMessageHandlers(GameNetwork.NetworkMessageHandlerRegistererContainer registerer)
        {
            base.AddRemoveMessageHandlers(registerer);
        }

        public override void AfterStart()
        {
            Mission.SetMissionMode(MissionMode.Battle, atStart: true);
        }

        public override void OnGoldAmountChangedForRepresentative(MissionRepresentativeBase representative, int goldAmount)
        {
            if (representative != null &&
                !ListedShellLobbyRuntime.IsMissionLobbyState(
                    Mission,
                    MissionLobbyComponent.MultiplayerGameState.Ending))
            {
                representative.UpdateGold(goldAmount);
                ListedShellMissionScoreboardComponent.NotifyListedShellPlayerPropertiesChanged(
                    ScoreboardComponent,
                    representative.MissionPeer);
            }
        }

        public override int GetGoldAmount()
        {
            return _myRepresentative?.Gold ?? 0;
        }

        public override void OnRemoveBehavior()
        {
            if (MissionNetworkComponent != null)
                MissionNetworkComponent.OnMyClientSynchronized -= OnMyClientSynchronized;

            base.OnRemoveBehavior();
        }

        private void OnMyClientSynchronized()
        {
            _myRepresentative = GameNetwork.MyPeer?.GetComponent<MissionRepresentativeBase>();
        }
    }
}
