using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Multiplayer;

namespace CoopSpectator.GameMode
{
    public sealed class MissionMultiplayerCoopCampaignMapPrototype :
        MissionMultiplayerGameModeBase
    {
        public override MultiplayerGameType GetMissionType() =>
            MultiplayerGameType.TeamDeathmatch;

        public override bool IsGameModeUsingOpposingTeams => true;

        public override bool IsGameModeHidingAllAgentVisuals => true;

        public override void AfterStart()
        {
            base.AfterStart();
            if (GameNetwork.IsServer)
                EnsureTeams();
        }

        private void EnsureTeams()
        {
            if (Mission.Teams.Attacker == null)
            {
                Mission.Teams.Add(
                    BattleSideEnum.Attacker,
                    0xFF664422u,
                    0xFF332211u,
                    null,
                    false,
                    false,
                    false);
            }

            if (Mission.Teams.Defender == null)
            {
                Mission.Teams.Add(
                    BattleSideEnum.Defender,
                    0xFF223344u,
                    0xFF111A22u,
                    null,
                    false,
                    false,
                    false);
            }
        }
    }
}
