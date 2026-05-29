using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.GameMode
{
    internal sealed class ListedShellMissionLobbyClientComponent : MissionCustomGameClientComponent
    {
        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            ListedShellLobbyRuntime.InitializeListedShellLobbyState(Mission ?? Mission.Current);
        }

        public override void AfterStart()
        {
            base.AfterStart();
            ListedShellLobbyRuntime.AfterListedShellLobbyStart(Mission ?? Mission.Current, this);
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
    }
}
