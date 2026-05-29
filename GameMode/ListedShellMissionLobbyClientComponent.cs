using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.GameMode
{
    internal sealed class ListedShellMissionLobbyClientComponent : MissionLobbyComponent
    {
        private object _lobbyClient;

        private bool _isServerEndedBeforeClientLoaded;

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            _lobbyClient = ResolveLobbyClient();
            ListedShellLobbyRuntime.InitializeListedShellLobbyState(Mission ?? Mission.Current);
        }

        public void SetServerEndingBeforeClientLoaded(bool isServerEndingBeforeClientLoaded)
        {
            _isServerEndedBeforeClientLoaded = isServerEndingBeforeClientLoaded;
        }

        public override void QuitMission()
        {
            base.QuitMission();
            if (_lobbyClient == null)
                return;

            if (GameNetwork.IsServer)
            {
                if (CurrentMultiplayerState != MultiplayerGameState.Ending &&
                    IsLobbyClientLoggedIn(_lobbyClient) &&
                    ResolveLobbyClientState(_lobbyClient) == 14)
                {
                    TryInvokeLobbyClientMethod(_lobbyClient, "EndCustomGame");
                }

                return;
            }

            if (!_isServerEndedBeforeClientLoaded &&
                CurrentMultiplayerState != MultiplayerGameState.Ending &&
                IsLobbyClientLoggedIn(_lobbyClient) &&
                ResolveLobbyClientState(_lobbyClient) == 16)
            {
                TryInvokeLobbyClientMethod(_lobbyClient, "QuitFromCustomGame");
            }
        }

        public override void AfterStart()
        {
            base.AfterStart();
            ListedShellLobbyRuntime.AfterListedShellLobbyStart(Mission ?? Mission.Current, this);
        }

        protected override void AddRemoveMessageHandlers(GameNetwork.NetworkMessageHandlerRegistererContainer registerer)
        {
            if (!GameNetwork.IsClient || registerer == null)
                return;

            registerer.RegisterBaseHandler<NetworkMessages.FromServer.KillDeathCountChange>(
                HandleListedShellKillDeathCountChange);
            if (ListedShellLobbyRuntime.TryRegisterListedShellMissionStateHandler(
                registerer,
                HandleListedShellMissionStateChange))
            {
                ModLogger.Info(
                    "ListedShellMissionLobbyClientComponent: registered coop-owned listed-shell MissionStateChange handler inside explicit lobby shell.");
            }

            ModLogger.Info(
                "ListedShellMissionLobbyClientComponent: registered coop-owned listed-shell KillDeathCountChange handler inside explicit lobby shell.");
        }

        public override void OnMissionTick(float dt)
        {
            if (ListedShellLobbyRuntime.ShouldCallNativeOnMissionTick(this, dt))
                base.OnMissionTick(dt);
        }

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState agentState, KillingBlow killingBlow)
        {
            if (ListedShellLobbyRuntime.ShouldCallNativeOnAgentRemoved(this, affectedAgent, affectorAgent, agentState, killingBlow))
                base.OnAgentRemoved(affectedAgent, affectorAgent, agentState, killingBlow);
        }

        protected override void HandleLateNewClientAfterLoadingFinished(NetworkCommunicator networkPeer)
        {
            if (ListedShellLobbyRuntime.ShouldCallNativeHandleLateNewClientAfterLoadingFinished(this, networkPeer))
                base.HandleLateNewClientAfterLoadingFinished(networkPeer);
        }

        private void HandleListedShellMissionStateChange(GameNetworkMessage baseMessage)
        {
            ListedShellLobbyRuntime.TryApplyListedShellMissionStateChange(
                Mission,
                this,
                baseMessage,
                nameof(ListedShellMissionLobbyClientComponent));
        }

        private void HandleListedShellKillDeathCountChange(GameNetworkMessage baseMessage)
        {
            ListedShellLobbyRuntime.TryApplyListedShellKillDeathCountChange(
                baseMessage,
                Mission?.GetMissionBehavior<MissionScoreboardComponent>());
        }

        private static object ResolveLobbyClient()
        {
            try
            {
                return typeof(NetworkMain)
                    .GetProperty("GameClient", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?
                    .GetValue(null);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsLobbyClientLoggedIn(object lobbyClient)
        {
            try
            {
                object loggedInValue = lobbyClient?.GetType()
                    .GetProperty("LoggedIn", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                    .GetValue(lobbyClient);
                return loggedInValue is bool loggedIn && loggedIn;
            }
            catch
            {
                return false;
            }
        }

        private static int ResolveLobbyClientState(object lobbyClient)
        {
            try
            {
                object currentState = lobbyClient?.GetType()
                    .GetProperty("CurrentState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                    .GetValue(lobbyClient);
                return currentState != null ? Convert.ToInt32(currentState) : -1;
            }
            catch
            {
                return -1;
            }
        }

        private static void TryInvokeLobbyClientMethod(object lobbyClient, string methodName)
        {
            try
            {
                lobbyClient?.GetType()
                    .GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?
                    .Invoke(lobbyClient, null);
            }
            catch
            {
            }
        }
    }
}
