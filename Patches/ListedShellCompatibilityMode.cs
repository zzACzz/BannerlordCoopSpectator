using System;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Missions.Multiplayer;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Patches
{
    internal sealed class ListedShellCompatibilityMode : MissionMultiplayerGameModeBase
    {
        private static string _lastInitializedMissionKey = string.Empty;

        public override bool IsGameModeHidingAllAgentVisuals => true;

        public override bool IsGameModeUsingOpposingTeams => true;

        public override MultiplayerGameType GetMissionType()
        {
            return MultiplayerGameType.TeamDeathmatch;
        }

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
                "ListedShellCompatibilityMode: installed listed-shell server compatibility mode without TDM score/gold authority. " +
                "Mission=" + (Mission?.SceneName ?? Mission.Current?.SceneName ?? "unknown") +
                " Side=" + (GameNetwork.IsServer ? "server" : "client") + ".");
        }

        public override void AfterStart()
        {
            string attackerCultureId = MultiplayerOptions.OptionType.CultureTeam1.GetStrValue();
            string defenderCultureId = MultiplayerOptions.OptionType.CultureTeam2.GetStrValue();
            BasicCultureObject attackerCulture = MBObjectManager.Instance.GetObject<BasicCultureObject>(attackerCultureId);
            BasicCultureObject defenderCulture = MBObjectManager.Instance.GetObject<BasicCultureObject>(defenderCultureId);
            MultiplayerBattleColors battleColors = MultiplayerBattleColors.CreateWith(attackerCulture, defenderCulture);
            Banner attackerBanner = new Banner(
                attackerCulture.Banner,
                battleColors.AttackerColors.BannerBackgroundColorUint,
                battleColors.AttackerColors.BannerForegroundColorUint);
            Banner defenderBanner = new Banner(
                defenderCulture.Banner,
                battleColors.DefenderColors.BannerBackgroundColorUint,
                battleColors.DefenderColors.BannerForegroundColorUint);

            if (Mission?.Teams?.Attacker == null)
            {
                Mission.Teams.Add(
                    BattleSideEnum.Attacker,
                    battleColors.AttackerColors.BannerBackgroundColorUint,
                    battleColors.AttackerColors.BannerForegroundColorUint,
                    attackerBanner);
            }

            if (Mission?.Teams?.Defender == null)
            {
                Mission.Teams.Add(
                    BattleSideEnum.Defender,
                    battleColors.DefenderColors.BannerBackgroundColorUint,
                    battleColors.DefenderColors.BannerForegroundColorUint,
                    defenderBanner);
            }
        }

        protected override void HandleEarlyNewClientAfterLoadingFinished(NetworkCommunicator networkPeer)
        {
            networkPeer?.AddComponent<ListedShellMissionRepresentative>();
        }

        protected override void HandleNewClientAfterSynchronized(NetworkCommunicator networkPeer)
        {
            MissionPeer missionPeer = networkPeer?.GetComponent<MissionPeer>();
            if (missionPeer == null)
                return;

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
