using System;
using CoopSpectator.Infrastructure.Hideout;
using CoopSpectator.Infrastructure.LordsHall;
using CoopSpectator.Infrastructure.SallyOut;
using CoopSpectator.Network.Messages;

namespace CoopSpectator.Infrastructure
{
    /// <summary>
    /// Shared scene-name classification helpers that are safe to use from both
    /// client/campaign and dedicated-only builds.
    /// </summary>
    public static class SceneRuntimeClassifier
    {
        private const string OfficialBattleMapScenePrefix = "mp_battle_map_";
        private const string CampaignBattleScenePrefix = "battle_terrain_";
        private const string VillageBattleScenePrefix = "village_";
        private const string VillageBattleSceneMarker = "_village_";

        public static bool IsOfficialMultiplayerBattleScene(string sceneName)
        {
            return !string.IsNullOrWhiteSpace(sceneName)
                && sceneName.StartsWith(OfficialBattleMapScenePrefix, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsCampaignBattleScene(string sceneName)
        {
            return !string.IsNullOrWhiteSpace(sceneName)
                && sceneName.StartsWith(CampaignBattleScenePrefix, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsVillageBattleScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                return false;

            return sceneName.StartsWith(VillageBattleScenePrefix, StringComparison.OrdinalIgnoreCase)
                || sceneName.IndexOf(VillageBattleSceneMarker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsExactCampaignBattleScene(string sceneName)
        {
            return IsCampaignBattleScene(sceneName)
                || IsVillageBattleScene(sceneName)
                || IsCurrentSiegeScenarioScene(sceneName);
        }

        public static bool IsCampaignOrCurrentSiegeScene(string sceneName)
        {
            return IsCampaignBattleScene(sceneName)
                || IsCurrentSiegeScenarioScene(sceneName);
        }

        public static bool RequiresLandRaidSceneLevel(string sceneName)
        {
            return IsVillageBattleScene(sceneName);
        }

        public static bool RequiresDedicatedSceneRegistration(string sceneName)
        {
            return IsSceneAwareBattleRuntimeScene(sceneName)
                && !IsOfficialMultiplayerBattleScene(sceneName);
        }

        public static bool IsSceneAwareBattleRuntimeScene(string sceneName)
        {
            return IsOfficialMultiplayerBattleScene(sceneName)
                || IsExactCampaignBattleScene(sceneName)
                || CoopHideoutBossPhaseContract.IsSupportedDayHideoutSceneName(sceneName);
        }

        public static bool IsValidatedDayHideoutScenarioScene(string sceneName)
        {
            if (!CoopHideoutBossPhaseContract.IsSupportedDayHideoutSceneName(sceneName))
                return false;

            try
            {
                BattleSnapshotMessage snapshot = BattleSnapshotRuntimeState.GetCurrent();
                string snapshotScene = !string.IsNullOrWhiteSpace(snapshot?.MultiplayerScene)
                    ? snapshot.MultiplayerScene
                    : snapshot?.MapScene;
                if (CoopHideoutBossPhaseContract.IsMatchingDayHideoutMissionContract(
                        sceneName,
                        snapshotScene,
                        snapshot?.ScenarioContext?.ScenarioKind))
                {
                    return true;
                }

                if (CoopHideoutAmbushContract.IsMatchingNightHideoutMissionContract(
                        sceneName,
                        snapshotScene,
                        snapshot?.ScenarioContext?.ScenarioKind))
                {
                    return true;
                }

                if (CoopPreMissionTopologyRuntimeState.TryGetActive(
                        sceneName,
                        out CoopPreMissionTopologyContract topology,
                        out _))
                {
                    return
                        CoopHideoutBossPhaseContract.IsMatchingDayHideoutMissionContract(
                            sceneName,
                            topology?.RuntimeScene,
                            topology?.ScenarioContext?.ScenarioKind) ||
                        CoopHideoutAmbushContract.IsMatchingNightHideoutMissionContract(
                            sceneName,
                            topology?.RuntimeScene,
                            topology?.ScenarioContext?.ScenarioKind);
                }
            }
            catch
            {
            }

            return false;
        }

        public static bool IsExactCommanderOrderControlScene(string sceneName)
        {
            return IsExactCampaignBattleScene(sceneName) ||
                   IsValidatedDayHideoutScenarioScene(sceneName);
        }

        public static bool IsExactSiegeAssaultWithDeploymentScene(string sceneName)
        {
            return IsExactSiegeWithDeploymentScene(sceneName);
        }

        public static bool IsExactRangedPossessionSynchronizationScene(string sceneName)
        {
            if (IsExactSiegeAssaultWithDeploymentScene(sceneName))
                return true;

            if (!IsExactCampaignBattleScene(sceneName))
                return false;

            try
            {
                BattleScenarioContextMessage scenarioContext =
                    BattleSnapshotRuntimeState.GetScenarioContext() ??
                    BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                    BattleSnapshotRuntimeState.GetState()?.ScenarioContext ??
                    CoopPreMissionTopologyRuntimeState.GetActiveScenarioContext();
                return SallyOutScenarioContract.IsValidatedScenario(
                    scenarioContext,
                    sceneName,
                    out _);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsExactSiegeWithDeploymentScene(string sceneName)
        {
            if (!IsExactCampaignBattleScene(sceneName))
                return false;

            BattleScenarioContextMessage scenarioContext = null;
            try
            {
                scenarioContext = BattleSnapshotRuntimeState.GetScenarioContext() ??
                                  BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                                  BattleSnapshotRuntimeState.GetState()?.ScenarioContext ??
                                  CoopPreMissionTopologyRuntimeState.GetActiveScenarioContext();
            }
            catch
            {
            }

            return ExactCampaignSiegeAssaultWithDeploymentRuntime
                .IsExactSiegeWithDeploymentScenario(scenarioContext);
        }

        public static bool IsValidatedLordsHallScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                return false;

            try
            {
                BattleScenarioContextMessage scenarioContext =
                    BattleSnapshotRuntimeState.GetScenarioContext() ??
                    BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                    BattleSnapshotRuntimeState.GetState()?.ScenarioContext ??
                    CoopPreMissionTopologyRuntimeState.GetActiveScenarioContext();
                return LordsHallScenarioContract.IsValidatedScenario(
                    scenarioContext,
                    sceneName,
                    out _);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsExactCampaignArmyMaterializationScene(string sceneName)
        {
            return IsExactSiegeAssaultWithDeploymentScene(sceneName) ||
                   IsValidatedLordsHallScene(sceneName);
        }

        private static bool IsCurrentSiegeScenarioScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                return false;

            try
            {
                BattleScenarioContextMessage scenarioContext = BattleSnapshotRuntimeState.GetScenarioContext();
                if (scenarioContext == null)
                    scenarioContext = CoopPreMissionTopologyRuntimeState.GetActiveScenarioContext();
                if (scenarioContext?.IsSiegeBattle != true)
                    return false;

                if (LordsHallScenarioContract.IsLordsHallScenario(scenarioContext) &&
                    !LordsHallScenarioContract.IsValidatedScenario(
                        scenarioContext,
                        sceneName,
                        out _))
                {
                    return false;
                }

                BattleSnapshotMessage snapshot = BattleSnapshotRuntimeState.GetCurrent();
                if (SceneNamesMatch(sceneName, snapshot?.MapScene) ||
                    SceneNamesMatch(sceneName, snapshot?.MultiplayerScene))
                {
                    return true;
                }

                return CoopPreMissionTopologyRuntimeState.TryGetActive(
                    sceneName,
                    out _,
                    out _);
            }
            catch
            {
                return false;
            }
        }

        private static bool SceneNamesMatch(string sceneName, string candidate)
        {
            return !string.IsNullOrWhiteSpace(sceneName) &&
                   !string.IsNullOrWhiteSpace(candidate) &&
                   string.Equals(sceneName, candidate, StringComparison.OrdinalIgnoreCase);
        }
    }
}
