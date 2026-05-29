using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.GameMode
{
    internal sealed class ListedShellMissionLobbyServerComponent : MissionLobbyComponent
    {
        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            ListedShellLobbyRuntime.InitializeListedShellLobbyState(Mission ?? Mission.Current);
        }

        public override void AfterStart()
        {
            (Mission ?? Mission.Current)?.DeploymentPlan.MakeDefaultDeploymentPlans();
        }

        protected override void AddRemoveMessageHandlers(GameNetwork.NetworkMessageHandlerRegistererContainer registerer)
        {
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

        protected override void OnUdpNetworkHandlerTick()
        {
            if (ListedShellLobbyRuntime.ShouldCallNativeOnUdpNetworkHandlerTick(this))
                base.OnUdpNetworkHandlerTick();
        }

        public override void SetStateEndingAsServer()
        {
            ListedShellLobbyRuntime.SetListedShellStateEndingAsServer(this);
        }

        protected override void EndGameAsServer()
        {
            ListedShellLobbyRuntime.EndListedShellMissionAsServer();
        }
    }
}
