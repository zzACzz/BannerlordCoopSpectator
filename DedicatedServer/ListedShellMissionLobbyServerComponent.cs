using CoopSpectator.Patches;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.DedicatedCustomServer;

namespace CoopSpectator.GameMode
{
    internal sealed class ListedShellMissionLobbyServerComponent : MissionCustomGameServerComponent
    {
        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            MissionLobbySpawnContractPatch.InitializeListedShellLobbyState(Mission ?? Mission.Current);
        }

        public override void AfterStart()
        {
            base.AfterStart();
            MissionLobbySpawnContractPatch.AfterListedShellLobbyStart(Mission ?? Mission.Current, this);
        }

        protected override void AddRemoveMessageHandlers(GameNetwork.NetworkMessageHandlerRegistererContainer registerer)
        {
        }

        public override void OnMissionTick(float dt)
        {
            if (MissionLobbySpawnContractPatch.ShouldCallNativeOnMissionTick(this, dt))
                base.OnMissionTick(dt);
        }

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState agentState, KillingBlow killingBlow)
        {
            if (MissionLobbySpawnContractPatch.ShouldCallNativeOnAgentRemoved(this, affectedAgent, affectorAgent, agentState, killingBlow))
                base.OnAgentRemoved(affectedAgent, affectorAgent, agentState, killingBlow);
        }

        protected override void HandleLateNewClientAfterLoadingFinished(NetworkCommunicator networkPeer)
        {
            if (MissionLobbySpawnContractPatch.ShouldCallNativeHandleLateNewClientAfterLoadingFinished(this, networkPeer))
                base.HandleLateNewClientAfterLoadingFinished(networkPeer);
        }
    }
}
