using System;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using System.Reflection;

namespace CoopSpectator.Patches
{
    internal sealed class ListedShellPassiveSpawningBehavior : SpawningBehaviorBase
    {
        private static string _lastInitializedMissionKey = string.Empty;

        public override void Initialize(SpawnComponent spawnComponent)
        {
            SpawnComponent = spawnComponent;
            GameMode = Mission.GetMissionBehavior<MissionMultiplayerGameModeBase>();
            MissionLobbyComponent = Mission.GetMissionBehavior<MissionLobbyComponent>();
            SpawnCheckTimer = new Timer(Mission.Current.CurrentTime, 0.2f);

            string missionKey =
                (Mission?.SceneName ?? Mission.Current?.SceneName ?? "unknown") +
                "|" +
                (GameNetwork.IsServer ? "server" : "client");
            if (!string.Equals(_lastInitializedMissionKey, missionKey, StringComparison.Ordinal))
            {
                _lastInitializedMissionKey = missionKey;
                ModLogger.Info(
                    "ListedShellPassiveSpawningBehavior: installed passive listed-shell SpawnComponent contract; active direct spawn authority moved to CoopMissionSpawnLogic. " +
                    "Mission=" + (Mission?.SceneName ?? Mission.Current?.SceneName ?? "unknown") +
                    " Side=" + (GameNetwork.IsServer ? "server" : "client") + ".");
            }

            TryInstallPassiveVisualCleanupShim();

            if (GameMode?.WarmupComponent == null)
                RequestStartSpawnSession();
        }

        public override void Clear()
        {
        }

        public override void OnTick(float dt)
        {
        }

        protected override void SpawnAgents()
        {
        }

        public override bool CanUpdateSpawnEquipment(MissionPeer missionPeer)
        {
            return false;
        }

        public override bool AllowEarlyAgentVisualsDespawning(MissionPeer lobbyPeer)
        {
            return false;
        }

        public override int GetMaximumReSpawnPeriodForPeer(MissionPeer peer)
        {
            if (GameMode?.WarmupComponent != null && GameMode.WarmupComponent.IsInWarmup)
                return 3;

            if (peer?.Team != null)
            {
                if (peer.Team.Side == BattleSideEnum.Attacker)
                    return MultiplayerOptions.OptionType.RespawnPeriodTeam2.GetIntValue();

                if (peer.Team.Side == BattleSideEnum.Defender)
                    return MultiplayerOptions.OptionType.RespawnPeriodTeam1.GetIntValue();
            }

            return -1;
        }

        protected override bool IsRoundInProgress()
        {
            return Mission.Current.CurrentState == Mission.State.Continuing;
        }

        private void TryInstallPassiveVisualCleanupShim()
        {
            try
            {
                FieldInfo agentVisualField = typeof(SpawningBehaviorBase).GetField(
                    "<AgentVisualSpawnComponent>k__BackingField",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (agentVisualField == null || agentVisualField.GetValue(this) != null)
                    return;

                var visualComponent = new MultiplayerMissionAgentVisualSpawnComponent();
                visualComponent.OnPreMissionTick(0f);
                agentVisualField.SetValue(this, visualComponent);
            }
            catch (Exception ex)
            {
                ModLogger.Info("ListedShellPassiveSpawningBehavior: passive visual cleanup shim failed open: " + ex.Message);
            }
        }
    }
}
