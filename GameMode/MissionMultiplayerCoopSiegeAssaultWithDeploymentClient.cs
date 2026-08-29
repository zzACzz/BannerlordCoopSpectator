using System;
using CoopSpectator.Infrastructure;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.GameMode
{
    public sealed class MissionMultiplayerCoopSiegeAssaultWithDeploymentClient : MissionMultiplayerGameModeBaseClient
    {
        private readonly bool _disableSceneOcclusion;
        private readonly string _disableSceneOcclusionReason;
        private bool _hasLoggedFirstMissionTick;

        public MissionMultiplayerCoopSiegeAssaultWithDeploymentClient(
            bool disableSceneOcclusion = false,
            string disableSceneOcclusionReason = "not-requested")
        {
            _disableSceneOcclusion = disableSceneOcclusion;
            _disableSceneOcclusionReason =
                disableSceneOcclusionReason ?? string.Empty;
        }

        public override void OnBehaviorInitialize()
        {
            _hasLoggedFirstMissionTick = false;
            TryDisableSceneOcclusionBeforeRendererActivation();
            ModLogger.Info("MissionMultiplayerCoopSiegeAssaultWithDeploymentClient OnBehaviorInitialize. Scene=" + (Mission?.SceneName ?? "null"));
            base.OnBehaviorInitialize();
            CoopBattlePhaseRuntimeState.StartMission(
                Mission,
                "CoopSiegeAssaultWithDeploymentClient.OnBehaviorInitialize");
        }

        private void TryDisableSceneOcclusionBeforeRendererActivation()
        {
            if (!_disableSceneOcclusion || Mission?.Scene == null)
                return;

            try
            {
                Mission.Scene.SetOcclusionMode(false);
                ModLogger.Info(
                    "MissionMultiplayerCoopSiegeAssaultWithDeploymentClient: disabled scene occlusion before renderer activation. " +
                    "Scene=" + (Mission.SceneName ?? "null") +
                    " Reason=" + _disableSceneOcclusionReason + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Error(
                    "MissionMultiplayerCoopSiegeAssaultWithDeploymentClient: failed to disable scene occlusion before renderer activation. " +
                    "Scene=" + (Mission?.SceneName ?? "null") +
                    " Reason=" + _disableSceneOcclusionReason + ".",
                    ex);
            }
        }

        public override void AfterStart()
        {
            ModLogger.Info(
                "MissionMultiplayerCoopSiegeAssaultWithDeploymentClient AfterStart ENTER. " +
                "Scene=" + (Mission?.SceneName ?? "null") +
                " GameType=" + GameType);
            base.AfterStart();
            ModLogger.Info(
                "MissionMultiplayerCoopSiegeAssaultWithDeploymentClient AfterStart EXIT. " +
                "Scene=" + (Mission?.SceneName ?? "null") +
                " GameType=" + GameType);
        }

        public override bool IsGameModeUsingGold => true;

        public override bool IsGameModeTactical => true;

        public override bool IsGameModeUsingRoundCountdown => true;

        public override MultiplayerGameType GameType => MultiplayerGameType.Siege;

        public override int GetGoldAmount() => 0;

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);

            if (_hasLoggedFirstMissionTick)
                return;

            _hasLoggedFirstMissionTick = true;
            ModLogger.Info(
                "MissionMultiplayerCoopSiegeAssaultWithDeploymentClient first mission tick entered. " +
                "Scene=" + (Mission?.SceneName ?? "null") +
                " GameType=" + GameType +
                " Mode=" + (Mission?.Mode.ToString() ?? "null"));
        }

        public override void OnGoldAmountChangedForRepresentative(MissionRepresentativeBase representative, int newAmount)
        {
        }
    }
}
