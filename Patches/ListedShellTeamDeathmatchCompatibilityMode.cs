using System;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.MissionRepresentatives;

namespace CoopSpectator.Patches
{
    internal sealed class ListedShellTeamDeathmatchCompatibilityMode : MissionMultiplayerTeamDeathmatch
    {
        private static string _lastInitializedMissionKey = string.Empty;

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
                "ListedShellTeamDeathmatchCompatibilityMode: installed listed-shell TDM compatibility mode without score/gold authority. " +
                "Mission=" + (Mission?.SceneName ?? Mission.Current?.SceneName ?? "unknown") +
                " Side=" + (GameNetwork.IsServer ? "server" : "client") + ".");
        }

        protected override void HandleEarlyNewClientAfterLoadingFinished(NetworkCommunicator networkPeer)
        {
            networkPeer?.AddComponent<TeamDeathmatchMissionRepresentative>();
        }

        protected override void HandleNewClientAfterSynchronized(NetworkCommunicator networkPeer)
        {
            MissionPeer missionPeer = networkPeer?.GetComponent<MissionPeer>();
            if (missionPeer == null)
                return;

            // Keep the representative graph stable for native clients without reviving TDM gold economy.
            ChangeCurrentGoldForPeer(missionPeer, 0);
        }

        public override void OnPeerChangedTeam(NetworkCommunicator peer, Team oldTeam, Team newTeam)
        {
        }

        public override void OnAgentRemoved(Agent affectedAgent, Agent affectorAgent, AgentState agentState, KillingBlow blow)
        {
        }

        public override bool CheckForMatchEnd()
        {
            return false;
        }

        public override Team GetWinnerTeam()
        {
            return null;
        }
    }
}
