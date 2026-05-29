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

        protected override void OnUdpNetworkHandlerTick()
        {
            ListedShellLobbyRuntime.HandleListedShellUdpNetworkHandlerTick(this);
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
