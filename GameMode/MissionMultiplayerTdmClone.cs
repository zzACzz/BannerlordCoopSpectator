using CoopSpectator.Infrastructure; // ModLogger
using TaleWorlds.Core; // BattleSideEnum
using TaleWorlds.MountAndBlade; // Mission, GameNetwork, Team, MissionLogic
using TaleWorlds.MountAndBlade.Multiplayer; // MissionMultiplayerGameModeBase, MultiplayerGameType

namespace CoopSpectator.GameMode
{
    /// <summary>
    /// Серверна логіка TdmClone: клон TDM 1:1 — ті самі команди Attacker/Defender і мінімальний спавн що й CoopTdm.
    /// Єдина відмінність від CoopTdm — ім'я режиму (GameModeId) для перевірки узгодження ID у трьох місцях.
    /// </summary>
    public sealed class MissionMultiplayerTdmClone : MissionMultiplayerGameModeBase
    {
        private bool _hasInitialized;

        public override MultiplayerGameType GetMissionType()
        {
            return MultiplayerGameType.TeamDeathmatch;
        }

        public override bool IsGameModeUsingOpposingTeams => true;
        public override bool IsGameModeHidingAllAgentVisuals => false;

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            _hasInitialized = false;
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            if (_hasInitialized || Mission?.Teams == null)
                return;

            _hasInitialized = true;

            if (!GameNetwork.IsServer)
                return;

            try
            {
                InitializeTeamsAndMinimalSpawn();
            }
            catch (System.Exception ex)
            {
                ModLogger.Error("TdmClone server: InitializeTeamsAndMinimalSpawn failed.", ex);
            }
        }

        private void InitializeTeamsAndMinimalSpawn()
        {
            Mission mission = Mission;
            if (mission == null) return;

            if (mission.Teams.Attacker == null)
                mission.Teams.Add(BattleSideEnum.Attacker, 0xFFCC2222u, 0xFF661111u, null, false, false, false);
            if (mission.Teams.Defender == null)
                mission.Teams.Add(BattleSideEnum.Defender, 0xFF2222CCu, 0xFF111166u, null, false, false, false);

            ModLogger.Info("TdmClone mission started (teams initialized; no agents yet).");
        }
    }
}
