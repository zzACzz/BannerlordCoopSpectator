using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.GameMode
{
    internal sealed class ListedShellMissionLobbyClientComponent : MissionLobbyComponent
    {
        private const float ListedShellClientInactivityThresholdSeconds = 2f;
        private static readonly FieldInfo MbApiNetworkField = typeof(MBAPI).GetField(
            "IMBNetwork",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo ElapsedTimeSinceLastUdpPacketArrivedMethod = MbApiNetworkField?.FieldType.GetMethod(
            "ElapsedTimeSinceLastUdpPacketArrived",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private object _lobbyClient;
        private Timer _inactivityTimer;

        private bool _isServerEndedBeforeClientLoaded;

        public override void OnBehaviorInitialize()
        {
            Mission mission = Mission ?? Mission.Current;
            GameNetwork.AddNetworkHandler(this);
            _lobbyClient = ListedShellLobbyClientInteropRuntime.ResolveLobbyClient();
            if (!GameNetwork.IsServerOrRecorder && mission != null)
                _inactivityTimer = new Timer(mission.CurrentTime, ListedShellClientInactivityThresholdSeconds);
            ListedShellLobbyRuntime.InitializeListedShellLobbyState(mission);
        }

        public void SetServerEndingBeforeClientLoaded(bool isServerEndingBeforeClientLoaded)
        {
            _isServerEndedBeforeClientLoaded = isServerEndingBeforeClientLoaded;
        }

        public override void QuitMission()
        {
            base.QuitMission();
            Mission mission = Mission ?? Mission.Current;
            bool isEnding = ListedShellLobbyRuntime.IsMissionLobbyState(mission, MultiplayerGameState.Ending);
            ListedShellLobbyClientInteropRuntime.HandleQuitMissionInterop(
                _lobbyClient,
                GameNetwork.IsServer,
                isEnding,
                _isServerEndedBeforeClientLoaded);
        }

        public override void AfterStart()
        {
            (Mission ?? Mission.Current)?.DeploymentPlan.MakeDefaultDeploymentPlans();
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
            UpdateLobbyClientCriticalState();
            ListedShellLobbyRuntime.HandleListedShellMissionTick(this, dt);
        }

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState agentState, KillingBlow killingBlow)
        {
            ListedShellLobbyRuntime.HandleListedShellAgentRemoved(this, affectedAgent, affectorAgent, agentState, killingBlow);
        }

        protected override void HandleLateNewClientAfterLoadingFinished(NetworkCommunicator networkPeer)
        {
            ListedShellLobbyRuntime.HandleListedShellLateNewClientAfterLoadingFinished(this, networkPeer);
        }

        private void HandleListedShellMissionStateChange(GameNetworkMessage baseMessage)
        {
            ListedShellLobbyRuntime.TryApplyListedShellMissionStateChange(
                Mission,
                baseMessage,
                nameof(ListedShellMissionLobbyClientComponent));
        }

        private void HandleListedShellKillDeathCountChange(GameNetworkMessage baseMessage)
        {
            ListedShellLobbyRuntime.TryApplyListedShellKillDeathCountChange(
                baseMessage,
                Mission?.GetMissionBehavior<MissionScoreboardComponent>());
        }

        private void UpdateLobbyClientCriticalState()
        {
            try
            {
                if (!GameNetwork.IsClient)
                    return;

                Mission mission = Mission ?? Mission.Current;
                if (mission == null)
                    return;

                if (_inactivityTimer == null)
                    _inactivityTimer = new Timer(mission.CurrentTime, ListedShellClientInactivityThresholdSeconds);

                if (!_inactivityTimer.Check(mission.CurrentTime))
                    return;

                if (_lobbyClient == null)
                    _lobbyClient = ListedShellLobbyClientInteropRuntime.ResolveLobbyClient();

                double elapsedSeconds = ResolveElapsedSecondsSinceLastUdpPacketArrived();
                ListedShellLobbyClientInteropRuntime.TrySetCriticalState(
                    _lobbyClient,
                    elapsedSeconds > ListedShellClientInactivityThresholdSeconds);
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellMissionLobbyClientComponent: failed to update listed-shell inactivity critical-state locally: " + ex.Message);
            }
        }

        private static double ResolveElapsedSecondsSinceLastUdpPacketArrived()
        {
            try
            {
                object mbNetwork = MbApiNetworkField?.GetValue(null);
                object elapsedSeconds = ElapsedTimeSinceLastUdpPacketArrivedMethod?.Invoke(mbNetwork, null);
                return elapsedSeconds is double value ? value : 0d;
            }
            catch
            {
                return 0d;
            }
        }
    }
}
