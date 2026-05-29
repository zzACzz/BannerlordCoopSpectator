using CoopSpectator.Patches;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.GameMode
{
    internal sealed class ListedShellMissionLobbyClientComponent : MissionCustomGameClientComponent
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
            base.AddRemoveMessageHandlers(registerer);
            MissionLobbySpawnContractPatch.PruneListedShellLobbyMessageRegistrations(this, registerer);
        }
    }
}
