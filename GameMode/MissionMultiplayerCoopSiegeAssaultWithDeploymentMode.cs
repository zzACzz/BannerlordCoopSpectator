using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.SiegeAmbush;
using CoopSpectator.MissionBehaviors;
using CoopSpectator.Network.Messages;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Missions.Handlers;
using TaleWorlds.MountAndBlade.Multiplayer;
using TaleWorlds.MountAndBlade.Source.Missions;

namespace CoopSpectator.GameMode
{
    internal static class MissionMultiplayerCoopSiegeAssaultWithDeploymentMode
    {
        internal static bool HasCoopSiegeRuntimeMarker(Mission mission)
        {
            if (mission == null)
                return false;

            return mission.GetMissionBehavior<MissionMultiplayerCoopSiegeAssaultWithDeployment>() != null ||
                   mission.GetMissionBehavior<MissionMultiplayerCoopSiegeAssaultWithDeploymentClient>() != null ||
                   mission.GetMissionBehavior<CoopMissionSpawnLogic>() != null ||
                   mission.GetMissionBehavior<CoopMissionNetworkBridge>() != null;
        }

        internal static IEnumerable<MissionBehavior> CreateBehaviorsForOfficialOpenNewBridge(Mission mission)
        {
            bool isServer = GameNetwork.IsServer;
            bool isDedicated = IsDedicatedServerProcess();
            List<MissionBehavior> list = isServer
                ? BuildServerMissionBehaviors(mission, isDedicated)
                : BuildClientMissionBehaviors(mission, isDedicated);

            if (isServer)
                ValidateServerStackSanity(list);
            else
                ValidateClientStackSanity(list);

            try
            {
                ModLogger.Info(
                    "CoopSiegeAssaultWithDeployment CreateBehaviorsForOfficialOpenNewBridge count=" + list.Count +
                    ", IsServer=" + isServer +
                    ", IsDedicated=" + isDedicated +
                    ", Scene=" + (mission?.SceneName ?? "null"));
                for (int i = 0; i < list.Count; i++)
                    ModLogger.Info("  [Siege " + i + "] " + list[i].GetType().FullName);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopSiegeAssaultWithDeployment behavior list log failed: " + ex.Message);
            }

            return list;
        }

        private static List<MissionBehavior> BuildServerMissionBehaviors(Mission mission, bool isDedicated)
        {
            var list = new List<MissionBehavior>
            {
                MissionLobbyComponent.CreateBehavior(),
                new MissionMultiplayerCoopSiegeAssaultWithDeployment(),
                new MultiplayerWarmupComponent(),
                new MissionMultiplayerCoopSiegeAssaultWithDeploymentClient(),
                new MultiplayerTimerComponent(),
            };

            AddOptional(
                list,
                MissionBehaviorHelpers.TryCreateBehaviorFromLoadedAssemblies(
                    "TaleWorlds.MountAndBlade.Multiplayer.Missions.MultiplayerBattleMissionAgentInteractionLogic"),
                "MultiplayerBattleMissionAgentInteractionLogic");
            ModLogger.Info(
                "CoopSiegeAssaultWithDeployment server: MultiplayerMissionAgentVisualSpawnComponent skipped " +
                "(client-only behavior). MissionLobbyEquipmentNetworkComponent retained for native siege spawning.");
            AddOptional(
                list,
                MissionBehaviorHelpers.TryCreateBehaviorFromLoadedAssemblies("TaleWorlds.MountAndBlade.ConsoleMatchStartEndHandler"),
                "ConsoleMatchStartEndHandler");
            list.Add(new MissionLobbyEquipmentNetworkComponent());
            AddRequired(list, MissionBehaviorHelpers.TryCreateSiegeSpawnComponent(), "SpawnComponent");
            list.Add(new MultiplayerTeamSelectComponent());
            AddRequired(list, MissionBehaviorHelpers.TryCreateHardBorderPlacer(), "MissionHardBorderPlacer");
            AddRequired(list, MissionBehaviorHelpers.TryCreateBoundaryPlacer(), "MissionBoundaryPlacer");
            AddRequired(list, MissionBehaviorHelpers.TryCreateBoundaryCrossingHandler(mission), "MissionBoundaryCrossingHandler");
            list.Add(new MultiplayerPollComponent());
            list.Add(new MultiplayerAdminComponent());
            if (!isDedicated)
                list.Add(new MultiplayerGameNotificationsComponent());
            AddOptional(list, MissionBehaviorHelpers.TryCreateMissionOptionsComponent(mission), "MissionOptionsComponent");
            AddOptional(list, MissionBehaviorHelpers.TryCreateSiegeMissionScoreboardComponent(), "MissionScoreboardComponent");
            AddOptional(
                list,
                MissionBehaviorHelpers.TryCreateBehaviorFromLoadedAssemblies("TaleWorlds.MountAndBlade.MissionAgentPanicHandler"),
                "MissionAgentPanicHandler");
            AddOptional(
                list,
                MissionBehaviorHelpers.TryCreateBehaviorFromLoadedAssemblies(
                    "TaleWorlds.MountAndBlade.Source.Missions.Handlers.Logic.AgentMoraleInteractionLogic"),
                "AgentMoraleInteractionLogic");
            AddOptional(
                list,
                MissionBehaviorHelpers.TryCreateBehaviorFromLoadedAssemblies("TaleWorlds.MountAndBlade.AgentHumanAILogic"),
                "AgentHumanAILogic");
            AddOptional(
                list,
                MissionBehaviorHelpers.TryCreateBehaviorFromLoadedAssemblies("TaleWorlds.MountAndBlade.EquipmentControllerLeaveLogic"),
                "EquipmentControllerLeaveLogic");
            AddOptional(
                list,
                MissionBehaviorHelpers.TryCreateBehaviorFromLoadedAssemblies("TaleWorlds.MountAndBlade.MultiplayerPreloadHelper"),
                "MultiplayerPreloadHelper");

            AppendSiegeAssaultRuntimeBehaviors(
                list,
                mission,
                includeScenePreparation: true,
                includeCampaignSiegeStateHandler: true);

            list.Add(new CoopMissionNetworkBridge());
            if (isDedicated)
            {
                ModLogger.Info(
                    "CoopSiegeAssaultWithDeployment server: injected CoopMissionNetworkBridge into the initial dedicated mission stack. " +
                    "CoopMissionSpawnLogic remains on deferred observer attach until native siege bootstrap stabilizes.");
            }
            else
            {
                list.Add(new MissionBehaviorDiagnostic());
                list.Add(new CoopMissionSpawnLogic());
            }

            return list;
        }

        private static List<MissionBehavior> BuildClientMissionBehaviors(Mission mission, bool isDedicated)
        {
            BattleScenarioContextMessage scenarioContext = ResolveScenarioContext();
            bool isSiegeAmbushClient =
                SiegeAmbushScenarioContract.IsSiegeAmbushScenario(
                    scenarioContext);
            var list = new List<MissionBehavior>
            {
                MissionLobbyComponent.CreateBehavior(),
                new MultiplayerWarmupComponent(),
                new MissionMultiplayerCoopSiegeAssaultWithDeploymentClient(
                    disableSceneOcclusion: isSiegeAmbushClient),
                new MultiplayerTimerComponent(),
            };

            AddIfNotNull(list, MissionBehaviorHelpers.TryCreateBehaviorFromLoadedAssemblies("TaleWorlds.MountAndBlade.MultiplayerAchievementComponent"));
            AddOptional(
                list,
                MissionBehaviorHelpers.TryCreateBehaviorFromLoadedAssemblies(
                    "TaleWorlds.MountAndBlade.Multiplayer.Missions.MultiplayerBattleMissionAgentInteractionLogic"),
                "MultiplayerBattleMissionAgentInteractionLogic");
            AddRequired(list, MissionBehaviorHelpers.TryCreateMissionAgentVisualSpawnComponent(), "MultiplayerMissionAgentVisualSpawnComponent");
            AddRequired(
                list,
                MissionBehaviorHelpers.TryCreateBehaviorFromLoadedAssemblies("TaleWorlds.MountAndBlade.ConsoleMatchStartEndHandler"),
                "ConsoleMatchStartEndHandler");
            if (ExperimentalFeatures.EnableSiegeReplayLobbyEquipmentNetworkComponent)
            {
                list.Add(new MissionLobbyEquipmentNetworkComponent());
            }
            else
            {
                ModLogger.Info("CoopSiegeAssaultWithDeployment client: skipped MissionLobbyEquipmentNetworkComponent for siege replay client isolation.");
            }
            list.Add(new MultiplayerTeamSelectComponent());
            AddRequired(list, MissionBehaviorHelpers.TryCreateHardBorderPlacer(), "MissionHardBorderPlacer");
            AddRequired(list, MissionBehaviorHelpers.TryCreateBoundaryPlacer(), "MissionBoundaryPlacer");
            AddRequired(list, MissionBehaviorHelpers.TryCreateBoundaryCrossingHandler(mission), "MissionBoundaryCrossingHandler");
            list.Add(new MultiplayerPollComponent());
            list.Add(new MultiplayerAdminComponent());

            if (!isDedicated)
                list.Add(new MultiplayerGameNotificationsComponent());

            AddOptional(list, MissionBehaviorHelpers.TryCreateMissionOptionsComponent(mission), "MissionOptionsComponent");
            AddOptional(list, MissionBehaviorHelpers.TryCreateSiegeMissionScoreboardComponent(), "MissionScoreboardComponent");
            AddOptional(list, MissionBehaviorHelpers.TryCreateMissionMatchHistoryComponentIfConditionsAreMet(), "MissionMatchHistoryComponent");
            AddOptional(
                list,
                MissionBehaviorHelpers.TryCreateBehaviorFromLoadedAssemblies("TaleWorlds.MountAndBlade.EquipmentControllerLeaveLogic"),
                "EquipmentControllerLeaveLogic");
            AddOptional(
                list,
                MissionBehaviorHelpers.TryCreateBehaviorFromLoadedAssemblies("TaleWorlds.MountAndBlade.MissionRecentPlayersComponent"),
                "MissionRecentPlayersComponent");
            AddOptional(
                list,
                MissionBehaviorHelpers.TryCreateBehaviorFromLoadedAssemblies("TaleWorlds.MountAndBlade.MultiplayerPreloadHelper"),
                "MultiplayerPreloadHelper");

            AppendSiegeAssaultRuntimeBehaviors(
                list,
                mission,
                includeScenePreparation: true,
                includeCampaignSiegeStateHandler: false);
            AppendRemoteClientSiegeDeploymentBridgeBehaviors(list, mission);

            list.Add(new MissionBehaviorDiagnostic());
            list.Add(new CoopMissionNetworkBridge());
            AddOptional(list, MissionBehaviorHelpers.TryCreateMissionAgentLabelUiParityView(mission), "MissionAgentLabelUIHandler");
            AddOptional(
                list,
                MissionBehaviorHelpers.TryCreateBehaviorFromLoadedAssemblies("TaleWorlds.MountAndBlade.View.MissionViews.MissionFormationTargetSelectionHandler"),
                "MissionFormationTargetSelectionHandler");
            AddOptional(list, MissionBehaviorHelpers.TryCreateMissionMultiplayerEscapeMenu("Battle"), "MissionMultiplayerEscapeMenu");
            if (ExperimentalFeatures.EnableSiegeReplayFormationMarkerUi)
            {
                AddOptional(list, MissionBehaviorHelpers.TryCreateMissionFormationMarkerUiParityView(mission), "MissionFormationMarkerUIHandler");
            }
            else
            {
                ModLogger.Info("CoopSiegeAssaultWithDeployment client: skipped MissionGauntletFormationMarker for siege replay client isolation.");
            }
#if !COOPSPECTATOR_DEDICATED
            if (ExperimentalFeatures.EnableCustomCoopSelectionOverlay &&
                ExperimentalFeatures.EnableSiegeReplayCustomCoopSelectionOverlay)
            {
                ModLogger.Info("CoopSiegeAssaultWithDeployment client: adding CoopMissionSelectionView.");
                list.Add(new CoopSpectator.UI.CoopMissionSelectionView());
            }
            else
            {
                ModLogger.Info("CoopSiegeAssaultWithDeployment client: skipped CoopMissionSelectionView for siege replay client isolation.");
            }
#endif
            return list;
        }

        private static void ValidateServerStackSanity(List<MissionBehavior> list)
        {
            if (list == null)
                return;

            string[] clientOnlyNames =
            {
                "MultiplayerMissionAgentVisualSpawnComponent"
            };

            int removed = 0;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                MissionBehavior behavior = list[i];
                if (behavior == null)
                    continue;

                string typeName = behavior.GetType().Name;
                for (int j = 0; j < clientOnlyNames.Length; j++)
                {
                    if (!string.Equals(typeName, clientOnlyNames[j], StringComparison.Ordinal))
                        continue;

                    list.RemoveAt(i);
                    removed++;
                    ModLogger.Info("CoopSiegeAssaultWithDeployment server validation: removed client-only behavior " + typeName + ".");
                    break;
                }
            }

            if (removed == 0)
            {
                ModLogger.Info(
                    "CoopSiegeAssaultWithDeployment server validation passed. HasSiegeClientCompanion=" +
                    MissionBehaviorHelpers.ListContainsBehaviorType(list, nameof(MissionMultiplayerCoopSiegeAssaultWithDeploymentClient)) + ".");
            }
        }

        private static void ValidateClientStackSanity(List<MissionBehavior> list)
        {
            if (list == null)
                return;

            bool hasVisualSpawn = MissionBehaviorHelpers.ListContainsBehaviorType(list, "MultiplayerMissionAgentVisualSpawnComponent");
            int removed = 0;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                MissionBehavior behavior = list[i];
                if (behavior == null)
                    continue;

                if (!string.Equals(behavior.GetType().Name, nameof(MissionLobbyEquipmentNetworkComponent), StringComparison.Ordinal))
                    continue;

                if (hasVisualSpawn)
                    continue;

                list.RemoveAt(i);
                removed++;
                ModLogger.Info(
                    "CoopSiegeAssaultWithDeployment client validation: removed MissionLobbyEquipmentNetworkComponent because " +
                    "MultiplayerMissionAgentVisualSpawnComponent is missing.");
            }

            if (removed == 0)
                ModLogger.Info("CoopSiegeAssaultWithDeployment client validation passed.");
        }

        private static void AppendSiegeAssaultRuntimeBehaviors(
            List<MissionBehavior> list,
            Mission mission,
            bool includeScenePreparation,
            bool includeCampaignSiegeStateHandler)
        {
            BattleScenarioContextMessage scenarioContext = ResolveScenarioContext();
            bool isPlayerAttacker = ResolvePlayerAttackerSide();
            bool isSiegeAmbush =
                SiegeAmbushScenarioContract.IsSiegeAmbushScenario(
                    scenarioContext);
            if (includeScenePreparation)
            {
                float[] wallHitPointRatios =
                    ExactCampaignSiegeAssaultWithDeploymentRuntime.ResolveIntactWallHitPointRatiosForScenePreparation(
                        mission,
                        scenarioContext,
                        out string wallRatioDiagnostics);
                bool hasAnySiegeTower = scenarioContext?.SiegeContext?.HasAnySiegeTower == true;
                ModLogger.Info(
                    "CoopSiegeAssaultWithDeployment: resolved siege wall scene-preparation ratios. " +
                    "HasAnySiegeTower=" + hasAnySiegeTower +
                    " Diagnostics=" + wallRatioDiagnostics);

                AddIfMissing(
                    list,
                    mission,
                    () => new SiegeMissionPreparationHandler(
                        isSallyOut: isSiegeAmbush,
                        isReliefForceAttack: false,
                        wallHitPointRatios,
                        hasAnySiegeTower),
                    "SiegeMissionPreparationHandler",
                    required: false);

                if (includeCampaignSiegeStateHandler)
                {
                    AddIfMissing(
                        list,
                        mission,
                        () => MissionBehaviorHelpers.TryCreateBehaviorFromLoadedAssemblies(
                            "SandBox.Missions.MissionLogics.CampaignSiegeStateHandler"),
                        "CampaignSiegeStateHandler",
                        required: false);
                }
                else
                {
                    ModLogger.Info(
                        "CoopSiegeAssaultWithDeployment: skipped CampaignSiegeStateHandler while preserving client siege scene preparation parity. " +
                        "Scene=" + (mission?.SceneName ?? "null") + ".");
                }
            }
            else
            {
                ModLogger.Info(
                    "CoopSiegeAssaultWithDeployment: skipped siege scene preparation. " +
                    "Scene=" + (mission?.SceneName ?? "null") + ".");
            }

            AddIfMissing(
                list,
                mission,
                () => new SiegeSceneObjectParityProbeBehavior(),
                "SiegeSceneObjectParityProbeBehavior",
                required: false);
            AddIfMissing(
                list,
                mission,
                () =>
                {
                    if (!ExactCampaignSiegeAssaultWithDeploymentRuntime.TryCreateMissionSiegeEnginesLogicBehavior(
                            scenarioContext,
                            out MissionSiegeEnginesLogic behavior,
                            out string diagnostics))
                    {
                        ModLogger.Info(
                            "CoopSiegeAssaultWithDeployment: MissionSiegeEnginesLogic factory failed. " +
                            "Scene=" + (mission?.SceneName ?? "null") +
                            " Diagnostics=" + diagnostics);
                        return null;
                    }

                    ModLogger.Info(
                        "CoopSiegeAssaultWithDeployment: MissionSiegeEnginesLogic factory succeeded. " +
                        "Scene=" + (mission?.SceneName ?? "null") +
                        " Diagnostics=" + diagnostics);
                    return behavior;
                },
                "MissionSiegeEnginesLogic",
                required: true);
            AddIfMissing(
                list,
                mission,
                () => new BannerBearerLogic(),
                "BannerBearerLogic",
                required: false);
            if (GameNetwork.IsServer)
            {
                AddIfMissing(
                    list,
                    mission,
                    () => new CasualtyHandler(),
                    "CasualtyHandler",
                    required: true);
                AddIfMissing(
                    list,
                    mission,
                    () => new BattlePowerCalculationLogic(),
                    "BattlePowerCalculationLogic",
                    required: true);
            }

            BattleSideEnum playerSide = isPlayerAttacker
                ? BattleSideEnum.Attacker
                : BattleSideEnum.Defender;
            bool shouldMountLiveDeploymentControllers =
                ExactCampaignSiegeAssaultWithDeploymentRuntime.ShouldMountLiveDeploymentControllers(
                    mission,
                    out string deploymentPolicy);
            if (!shouldMountLiveDeploymentControllers)
            {
                ModLogger.Info(
                    "CoopSiegeAssaultWithDeployment: live deployment controllers suppressed. " +
                    "Scene=" + (mission?.SceneName ?? "null") +
                    " Policy=" + (deploymentPolicy ?? "unknown"));
                TryAppendInitialNativeSpawnLogicBootstrap(list, mission, playerSide);
                return;
            }

            AddIfMissing(
                list,
                mission,
                () => ExactCampaignSiegeAssaultWithDeploymentRuntime.CreateSiegeDeploymentHandler(isPlayerAttacker),
                "SiegeDeploymentHandler",
                required: true);
            AddIfMissing(
                list,
                mission,
                () => new SiegeDeploymentMissionController(isPlayerAttacker),
                "SiegeDeploymentMissionController",
                required: true);
            LogDeploymentControllerDependencySnapshot(list, mission, isPlayerAttacker, deploymentPolicy);
            TryAppendInitialNativeSpawnLogicBootstrap(list, mission, playerSide);
        }

        private static void AppendRemoteClientSiegeDeploymentBridgeBehaviors(List<MissionBehavior> list, Mission mission)
        {
            BattleScenarioContextMessage scenarioContext = ResolveScenarioContext();
            if (!ExactCampaignSiegeAssaultWithDeploymentRuntime.ShouldInjectWrappedBattleClientDeploymentBehaviors(
                    mission,
                    scenarioContext,
                    out string deploymentBridgePolicy))
            {
                ModLogger.Info(
                    "CoopSiegeAssaultWithDeployment client: deployment bridge suppressed. " +
                    "Scene=" + (mission?.SceneName ?? "null") +
                    " Policy=" + (deploymentBridgePolicy ?? "unknown"));
                return;
            }

            bool isPlayerAttacker = ResolvePlayerAttackerSide();
            AddIfMissing(
                list,
                mission,
                () => ExactCampaignSiegeAssaultWithDeploymentRuntime.CreateSiegeDeploymentHandler(isPlayerAttacker),
                "SiegeDeploymentHandler",
                required: true);

            bool hasMissionAgentSpawnLogic =
                MissionBehaviorHelpers.ListContainsBehaviorType(list, "MissionAgentSpawnLogic") ||
                MissionBehaviorHelpers.ListContainsBehaviorType(list, "DefaultBattleMissionAgentSpawnLogic") ||
                GetExistingBehaviorByTypeName(mission, "MissionAgentSpawnLogic") != null ||
                GetExistingBehaviorByTypeName(mission, "DefaultBattleMissionAgentSpawnLogic") != null;
            bool addedDeploymentController = false;
            if (hasMissionAgentSpawnLogic)
            {
                AddIfMissing(
                    list,
                    mission,
                    () => new SiegeDeploymentMissionController(isPlayerAttacker),
                    "SiegeDeploymentMissionController",
                    required: true);
                addedDeploymentController = true;
            }

            ModLogger.Info(
                "CoopSiegeAssaultWithDeployment client: deployment bridge enabled. " +
                "Scene=" + (mission?.SceneName ?? "null") +
                " PlayerAttacker=" + isPlayerAttacker +
                " Policy=" + (deploymentBridgePolicy ?? "unknown") +
                " HasMissionAgentSpawnLogic=" + hasMissionAgentSpawnLogic +
                " AddedSiegeDeploymentHandler=True" +
                " AddedSiegeDeploymentController=" + addedDeploymentController + ".");
        }

        private static void LogDeploymentControllerDependencySnapshot(
            List<MissionBehavior> list,
            Mission mission,
            bool isPlayerAttacker,
            string deploymentPolicy)
        {
            try
            {
                bool listHasSpawnComponent = MissionBehaviorHelpers.ListContainsBehaviorType(list, "SpawnComponent");
                bool listHasDefaultBattleSpawnLogic = MissionBehaviorHelpers.ListContainsBehaviorType(list, "DefaultBattleMissionAgentSpawnLogic");
                bool missionHasDefaultBattleSpawnLogic = GetExistingBehaviorByTypeName(mission, "DefaultBattleMissionAgentSpawnLogic") != null;
                bool missionHasSiegeDeploymentHandler = GetExistingBehaviorByTypeName(mission, "SiegeDeploymentHandler") != null ||
                                                        MissionBehaviorHelpers.ListContainsBehaviorType(list, "SiegeDeploymentHandler");

                ModLogger.Info(
                    "CoopSiegeAssaultWithDeployment: deployment controller dependency snapshot. " +
                    "Scene=" + (mission?.SceneName ?? "null") +
                    " IsPlayerAttacker=" + isPlayerAttacker +
                    " Policy=" + (deploymentPolicy ?? "unknown") +
                    " ListHasSpawnComponent=" + listHasSpawnComponent +
                    " ListHasDefaultBattleMissionAgentSpawnLogic=" + listHasDefaultBattleSpawnLogic +
                    " MissionHasDefaultBattleMissionAgentSpawnLogic=" + missionHasDefaultBattleSpawnLogic +
                    " HasSiegeDeploymentHandler=" + missionHasSiegeDeploymentHandler + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopSiegeAssaultWithDeployment: deployment controller dependency snapshot failed: " + ex.Message);
            }
        }

        private static void TryAppendInitialNativeSpawnLogicBootstrap(
            List<MissionBehavior> list,
            Mission mission,
            BattleSideEnum playerSide)
        {
            if (!GameNetwork.IsServer || list == null || mission == null)
                return;

            if (ShouldUseFieldMaterializedArmyRuntime(mission))
            {
                ModLogger.Info(
                    "CoopSiegeAssaultWithDeployment: retaining initial native spawn logic bootstrap as a dependency-only contract for field materialized siege runtime. " +
                    "Scene=" + (mission.SceneName ?? "null") +
                    " PlayerSide=" + playerSide);
            }

            if (!ExperimentalFeatures.EnableSiegeReplayInitialNativeSpawnLogicBootstrap)
                return;

            if (MissionBehaviorHelpers.ListContainsBehaviorType(list, "DefaultBattleMissionAgentSpawnLogic") ||
                GetExistingBehaviorByTypeName(mission, "DefaultBattleMissionAgentSpawnLogic") != null)
            {
                list.Add(new SiegeReplayNativeSpawnContractBootstrapBehavior(playerSide));
                TryAppendInitialSiegeAmbushController(list, mission);
                ModLogger.Info(
                    "CoopSiegeAssaultWithDeployment: appended siege replay native spawn contract bootstrap after existing DefaultBattleMissionAgentSpawnLogic.");
                return;
            }

            if (!ExactCampaignArmyBootstrap.TryCreateInitialSiegeAssaultWithDeploymentSpawnLogic(
                    mission,
                    playerSide,
                    "CoopSiegeAssaultWithDeployment.initial-stack",
                    out BattleSpawnLogic battleSpawnLogic,
                    out DefaultBattleMissionAgentSpawnLogic spawnLogic,
                    out BattleReinforcementsSpawnController battleReinforcementsSpawnController,
                    out string diagnostics))
            {
                ModLogger.Info(
                    "CoopSiegeAssaultWithDeployment: initial DefaultBattleMissionAgentSpawnLogic bootstrap skipped. " +
                    "Scene=" + (mission.SceneName ?? "null") +
                    " PlayerSide=" + playerSide +
                    " Diagnostics=" + (diagnostics ?? "none"));
                return;
            }

            list.Add(battleSpawnLogic);
            list.Add(spawnLogic);
            list.Add(battleReinforcementsSpawnController);
            list.Add(new SiegeReplayNativeSpawnContractBootstrapBehavior(playerSide));
            TryAppendInitialSiegeAmbushController(list, mission);
            ModLogger.Info(
                "CoopSiegeAssaultWithDeployment: appended initial BattleSpawnLogic, DefaultBattleMissionAgentSpawnLogic, BattleReinforcementsSpawnController and spawn contract bootstrap. " +
                "Scene=" + (mission.SceneName ?? "null") +
                " PlayerSide=" + playerSide +
                " Diagnostics=" + (diagnostics ?? "none"));
        }

        private static void TryAppendInitialSiegeAmbushController(
            List<MissionBehavior> list,
            Mission mission)
        {
            if (list == null || mission == null)
                return;

            BattleScenarioContextMessage scenarioContext = ResolveScenarioContext();
            if (!SiegeAmbushScenarioContract.IsSiegeAmbushScenario(scenarioContext))
                return;

            if (MissionBehaviorHelpers.ListContainsBehaviorType(
                    list,
                    nameof(CoopExactCampaignSiegeAmbushMissionController)) ||
                GetExistingBehaviorByTypeName(
                    mission,
                    nameof(CoopExactCampaignSiegeAmbushMissionController)) != null)
            {
                return;
            }

            list.Add(
                new CoopExactCampaignSiegeAmbushMissionController(
                    defenderTotalTroopCount: 0,
                    attackerTotalTroopCount: 0,
                    isSallyOutAmbush: true));
            ModLogger.Info(
                "CoopSiegeAssaultWithDeployment: appended initial CoopExactCampaignSiegeAmbushMissionController after native spawn contract bootstrap. " +
                "Scene=" + (mission.SceneName ?? "null") + ".");
        }

        private static void AddIfMissing(
            List<MissionBehavior> list,
            Mission mission,
            Func<MissionBehavior> factory,
            string shortTypeName,
            bool required)
        {
            if (MissionBehaviorHelpers.ListContainsBehaviorType(list, shortTypeName))
                return;

            MissionBehavior existingBehavior = GetExistingBehaviorByTypeName(mission, shortTypeName);
            if (existingBehavior != null)
                return;

            MissionBehavior created = factory?.Invoke();
            if (required)
                AddRequired(list, created, shortTypeName);
            else
                AddOptional(list, created, shortTypeName);
        }

        private static MissionBehavior GetExistingBehaviorByTypeName(Mission mission, string shortTypeName)
        {
            if (mission?.MissionBehaviors == null || string.IsNullOrWhiteSpace(shortTypeName))
                return null;

            for (int i = 0; i < mission.MissionBehaviors.Count; i++)
            {
                MissionBehavior behavior = mission.MissionBehaviors[i];
                if (MissionBehaviorHelpers.IsBehaviorTypeOrBaseType(behavior, shortTypeName))
                {
                    return behavior;
                }
            }

            return null;
        }

        private static BattleScenarioContextMessage ResolveScenarioContext()
        {
            return BattleSnapshotRuntimeState.GetScenarioContext()
                   ?? BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext
                   ?? BattleSnapshotRuntimeState.GetState()?.ScenarioContext
                   ?? CoopPreMissionTopologyRuntimeState.GetActiveScenarioContext();
        }

        private static bool ShouldUseFieldMaterializedArmyRuntime(Mission mission)
        {
            return CoopMissionSpawnLogic.ShouldUseFieldMaterializedSiegeReplayRuntime(mission);
        }

        private static bool ResolvePlayerAttackerSide()
        {
            BattleSideState runtimePlayerSide = BattleSnapshotRuntimeState.GetState()?.Sides?
                .FirstOrDefault(side => side != null && side.IsPlayerSide);
            if (runtimePlayerSide != null)
                return IsAttackerSideKey(runtimePlayerSide.CanonicalSideKey ?? runtimePlayerSide.SideId);

            BattleSideSnapshotMessage snapshotPlayerSide = BattleSnapshotRuntimeState.GetCurrent()?.Sides?
                .FirstOrDefault(side => side != null && side.IsPlayerSide);
            if (snapshotPlayerSide != null)
                return IsAttackerSideKey(snapshotPlayerSide.SideText ?? snapshotPlayerSide.SideId);

            string preMissionPlayerSide =
                CoopPreMissionTopologyRuntimeState.GetActivePlayerSide();
            if (!string.IsNullOrWhiteSpace(preMissionPlayerSide))
                return IsAttackerSideKey(preMissionPlayerSide);

            return true;
        }

        private static bool IsAttackerSideKey(string sideKey)
        {
            if (string.IsNullOrWhiteSpace(sideKey))
                return true;

            return string.Equals(sideKey, BattleSideEnum.Attacker.ToString(), StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(sideKey, "attacker", StringComparison.OrdinalIgnoreCase);
        }

        private static void AddOptional(List<MissionBehavior> list, MissionBehavior behavior, string name)
        {
            if (behavior == null)
            {
                ModLogger.Info("CoopSiegeAssaultWithDeployment: " + name + " skipped with warning (optional).");
                return;
            }

            list.Add(behavior);
        }

        private static void AddRequired(List<MissionBehavior> list, MissionBehavior behavior, string name)
        {
            if (behavior == null)
            {
                ModLogger.Info("CoopSiegeAssaultWithDeployment: Required mission behavior '" + name + "' could not be created. Aborting mission open.");
                throw new InvalidOperationException("Required mission behavior '" + name + "' could not be created. Check logs for assembly/type resolution.");
            }

            list.Add(behavior);
        }

        private static void AddIfNotNull(List<MissionBehavior> list, MissionBehavior behavior)
        {
            if (behavior != null)
                list.Add(behavior);
        }

        private static bool IsDedicatedServerProcess()
        {
            try
            {
                string name = System.Diagnostics.Process.GetCurrentProcess().ProcessName ?? string.Empty;
                return name.IndexOf("Dedicated", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private sealed class SiegeSceneObjectParityProbeBehavior : MissionLogic
        {
            private int _remainingTickProbes = 3;
            private float _elapsedSeconds;
            private float _nextProbeSeconds = 0.5f;

            public override void AfterStart()
            {
                Log("AfterStart");
            }

            public override void OnMissionTick(float dt)
            {
                if (!ExperimentalFeatures.EnableBattleMapFullContractDiagnostics)
                    return;

                if (_remainingTickProbes <= 0)
                    return;

                _elapsedSeconds += dt;
                if (_elapsedSeconds < _nextProbeSeconds)
                    return;

                Log("Tick" + (4 - _remainingTickProbes));
                _remainingTickProbes--;
                _nextProbeSeconds = _elapsedSeconds + 1f;
            }

            private void Log(string phase)
            {
                Mission mission = Mission;
                if (mission == null)
                    return;

                string side = GameNetwork.IsServer ? "server" : "client";
                BattleMapContractDiagnostics.LogSiegeSceneObjectParity(
                    mission,
                    "SiegeSceneObjectParityProbeBehavior." + phase + "." + side);
            }
        }

        private sealed class SiegeReplayNativeSpawnContractBootstrapBehavior : MissionLogic
        {
            private readonly BattleSideEnum _playerSide;
            private bool _initialized;

            public SiegeReplayNativeSpawnContractBootstrapBehavior(BattleSideEnum playerSide)
            {
                _playerSide = playerSide;
            }

            public override void AfterStart()
            {
                if (_initialized)
                    return;

                _initialized = true;
                Mission mission = Mission;
                if (!GameNetwork.IsServer || mission == null)
                    return;

                if (!HasCoopSiegeRuntimeMarker(mission))
                    return;

                if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(mission.SceneName ?? string.Empty))
                    return;

                if (ShouldUseFieldMaterializedArmyRuntime(mission))
                {
                    ModLogger.Info(
                        "CoopSiegeAssaultWithDeployment: initializing native spawn contract bootstrap as dependency-only for field materialized siege runtime. " +
                        "Scene=" + (mission.SceneName ?? "null") +
                        " PlayerSide=" + _playerSide + ".");
                }

                if (!ExactCampaignArmyBootstrap.TryInitialize(
                        mission,
                        _playerSide,
                        "SiegeReplayNativeSpawnContractBootstrapBehavior.AfterStart",
                        out string reason))
                {
                    ModLogger.Info(
                        "CoopSiegeAssaultWithDeployment: initial native spawn contract bootstrap deferred or failed. " +
                        "Scene=" + (mission.SceneName ?? "null") +
                        " PlayerSide=" + _playerSide +
                        " Reason=" + (reason ?? "none"));
                    return;
                }

                ModLogger.Info(
                    "CoopSiegeAssaultWithDeployment: initial native spawn contract bootstrap initialized. " +
                    "Scene=" + (mission.SceneName ?? "null") +
                    " PlayerSide=" + _playerSide + ".");
            }
        }
    }
}
