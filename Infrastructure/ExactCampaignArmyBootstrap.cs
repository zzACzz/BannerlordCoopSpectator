using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure.LordsHall;
using CoopSpectator.Infrastructure.Relief;
using CoopSpectator.Infrastructure.SallyOut;
using CoopSpectator.Infrastructure.SiegeAmbush;
using CoopSpectator.MissionBehaviors;
using CoopSpectator.MissionBehaviors.LordsHall;
using CoopSpectator.Network.Messages;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Source.Missions;
using TaleWorlds.ObjectSystem;
using NativeMissionAgentSpawnLogic = TaleWorlds.MountAndBlade.DefaultBattleMissionAgentSpawnLogic;

namespace CoopSpectator.Infrastructure
{
    public static class ExactCampaignArmyBootstrap
    {
        private enum ActiveBootstrapMode
        {
            None = 0,
            NativeSpawnLogic = 1,
            LordsHallController = 2,
            SiegeAssaultNoDeployment = 3,
            SiegeAssaultWithDeployment = 4
        }

        private static Mission _activeMission;
        private static NativeMissionAgentSpawnLogic _activeSpawnLogic;
        private static IMissionTroopSupplier[] _activeSuppliers;
        private static ActiveBootstrapMode _activeMode;
        private static BattleSideEnum _activePlayerSide = BattleSideEnum.None;
        private static Team _activePlayerTeam;
        private static Team _activePlayerEnemyTeam;
        private static bool _reinforcementsEnabled;
        private static Mission _nativeInitialSpawnersStartedMission;
        private static Mission _initialStackSpawnLogicMission;
        private static NativeMissionAgentSpawnLogic _initialStackSpawnLogic;
        private static IMissionTroopSupplier[] _initialStackSuppliers;
        private static int _initialStackDefenderTotal;
        private static int _initialStackAttackerTotal;
        private static string _initialStackSupplierDiagnostics = string.Empty;
        private static DateTime _nextDeferredLogUtc = DateTime.MinValue;
        private static DateTime _nextRuntimeDiagnosticsLogUtc = DateTime.MinValue;
        private static string _lastRuntimeDiagnosticsSummary = string.Empty;
        private static Mission _spawnLogicInitSideOverrideMission;
        private static BattleSideEnum _spawnLogicInitSideOverride = BattleSideEnum.None;
        private static int _spawnLogicInitSideOverrideDepth;
        private static readonly HashSet<Team> SpawnLogicInitTemporaryNonBattleTeams = new HashSet<Team>();
        private static Mission _lastLoggedTeamAiActivationStateMission;
        private static string _lastLoggedTeamAiActivationStateKey = string.Empty;
        private static readonly HashSet<TeamAIComponent> TeamAiDeploymentFinishedNotifications =
            new HashSet<TeamAIComponent>();
        private static readonly HashSet<Mission> SiegeAssaultTeamAiLifecycleTacticRepairs =
            new HashSet<Mission>();
        private static readonly FieldInfo TeamSideBackingField =
            typeof(Team).GetField("<Side>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo TeamAiBackingField =
            typeof(Team).GetField("<TeamAI>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly Type TeamAiSiegeComponentRuntimeType =
            ResolveRuntimeType("TaleWorlds.MountAndBlade.TeamAISiegeComponent", "TaleWorlds.MountAndBlade");
        private static readonly Type SiegeLaneRuntimeType =
            ResolveRuntimeType("TaleWorlds.MountAndBlade.SiegeLane", "TaleWorlds.MountAndBlade");
        private static readonly FieldInfo TeamAiSiegeComponentSiegeLanesField =
            TeamAiSiegeComponentRuntimeType?.GetField("SiegeLanes", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly PropertyInfo TeamAiSiegeComponentSiegeLanesProperty =
            TeamAiSiegeComponentRuntimeType?.GetProperty("SiegeLanes", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo SiegeLaneQuerySystemField =
            SiegeLaneRuntimeType?.GetField("_siegeQuerySystem", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DefaultMissionDeploymentPlanTeamDeploymentPlansField =
            typeof(DefaultMissionDeploymentPlan).GetField("_teamDeploymentPlans", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo MissionInitializerRecordProperty =
            typeof(Mission).GetProperty("InitializerRecord", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo MissionAgentSpawnLogicMissionSidesField =
            typeof(NativeMissionAgentSpawnLogic).GetField("_battleSideSpawnContexts", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo MissionAgentSpawnLogicPhasesField =
            typeof(NativeMissionAgentSpawnLogic).GetField("_phases", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo MissionAgentSpawnLogicNumberOfTroopsInTotalField =
            typeof(NativeMissionAgentSpawnLogic).GetField("_numberOfTroopsInTotal", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo MissionAgentSpawnLogicBattleSizeField =
            typeof(NativeMissionAgentSpawnLogic).GetField("_battleSize", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo MissionAgentSpawnLogicDeploymentPlanField =
            typeof(NativeMissionAgentSpawnLogic).GetField("_deploymentPlan", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DefaultMissionDeploymentPlanFormationSceneSpawnEntriesField =
            typeof(DefaultMissionDeploymentPlan).GetField("_formationSceneSpawnEntries", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo MissionSideTroopSupplierField =
            typeof(MissionBattleSideSpawnContext).GetField("_troopSupplier", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly struct TeamSideOverrideState
        {
            public TeamSideOverrideState(
                Team team,
                BattleSideEnum originalSide,
                bool addedTemporaryDeploymentPlan,
                int temporaryDeploymentPlanIndex)
            {
                Team = team;
                OriginalSide = originalSide;
                AddedTemporaryDeploymentPlan = addedTemporaryDeploymentPlan;
                TemporaryDeploymentPlanIndex = temporaryDeploymentPlanIndex;
            }

            public Team Team { get; }

            public BattleSideEnum OriginalSide { get; }

            public bool AddedTemporaryDeploymentPlan { get; }

            public int TemporaryDeploymentPlanIndex { get; }
        }

        public static bool IsActive(Mission mission)
        {
            return mission != null &&
                   ReferenceEquals(_activeMission, mission) &&
                   _activeMode != ActiveBootstrapMode.None;
        }

        public static bool IsInitialSpawnMaterializationComplete(
            Mission mission,
            out string diagnostics)
        {
            diagnostics = "bootstrap-inactive";
            if (!IsActive(mission) || _activeSpawnLogic == null)
                return false;

            try
            {
                bool initialSpawnOver = _activeSpawnLogic.IsInitialSpawnOver;
                diagnostics =
                    "InitialSpawnOver=" + initialSpawnOver +
                    " ActiveDefender=" + _activeSpawnLogic.NumberOfActiveDefenderTroops +
                    " ActiveAttacker=" + _activeSpawnLogic.NumberOfActiveAttackerTroops +
                    " NumberOfAgents=" + _activeSpawnLogic.NumberOfAgents;
                return initialSpawnOver;
            }
            catch (Exception ex)
            {
                diagnostics = "initial-spawn-read-failed:" + ex.GetType().Name;
                return false;
            }
        }

        public static bool TryGetSpawnHorses(Mission mission, BattleSideEnum side, out bool spawnHorses)
        {
            spawnHorses = false;
            if (mission == null ||
                side == BattleSideEnum.None ||
                !ReferenceEquals(_activeMission, mission) ||
                !UsesSpawnLogicRuntimeMode(_activeMode) ||
                _activeSpawnLogic == null)
            {
                return false;
            }

            try
            {
                spawnHorses = _activeSpawnLogic.GetSpawnHorses(side);
                return true;
            }
            catch
            {
                spawnHorses = false;
                return false;
            }
        }

        public static bool IsSiegeAssaultWithDeploymentActive(Mission mission)
        {
            return mission != null &&
                   ReferenceEquals(_activeMission, mission) &&
                   _activeMode == ActiveBootstrapMode.SiegeAssaultWithDeployment;
        }

        private static bool UsesSpawnLogicRuntimeMode(ActiveBootstrapMode mode)
        {
            return mode == ActiveBootstrapMode.NativeSpawnLogic ||
                   mode == ActiveBootstrapMode.SiegeAssaultNoDeployment ||
                   mode == ActiveBootstrapMode.SiegeAssaultWithDeployment;
        }

        public static bool TryGetSpawnLogicInitTeamSideOverride(
            Team team,
            BattleSideEnum currentSide,
            out BattleSideEnum overrideSide)
        {
            if (team?.Mission != null &&
                SpawnLogicInitTemporaryNonBattleTeams.Contains(team) &&
                TryGetSpawnLogicInitTeamSideOverride(team.Mission, currentSide, out overrideSide))
            {
                return true;
            }

            overrideSide = BattleSideEnum.None;
            return false;
        }

        public static bool TryGetSpawnLogicInitTeamSideOverride(
            Mission mission,
            BattleSideEnum currentSide,
            out BattleSideEnum overrideSide)
        {
            if (_spawnLogicInitSideOverrideDepth > 0 &&
                mission != null &&
                ReferenceEquals(_spawnLogicInitSideOverrideMission, mission) &&
                currentSide == BattleSideEnum.None &&
                _spawnLogicInitSideOverride != BattleSideEnum.None)
            {
                overrideSide = _spawnLogicInitSideOverride;
                return true;
            }

            overrideSide = BattleSideEnum.None;
            return false;
        }

        public static bool IsSpawnLogicInitTemporaryNonBattleTeam(Mission mission, Team team)
        {
            return mission != null &&
                   team != null &&
                   ReferenceEquals(_spawnLogicInitSideOverrideMission, mission) &&
                   _spawnLogicInitSideOverrideDepth > 0 &&
                   SpawnLogicInitTemporaryNonBattleTeams.Contains(team);
        }

        public static void ResetForMission(Mission mission)
        {
            if (ReferenceEquals(_activeMission, mission))
                return;

            if (_activeMission != null)
                ExactCampaignSiegeAssaultWithDeploymentRuntime.ResetRuntimeState(
                    _activeMission,
                    "ExactCampaignArmyBootstrap.ResetForMission");

            if (_activeMission != null)
                ExactCampaignCommanderDeploymentRuntime.ResetRuntimeState(
                    _activeMission,
                    "ExactCampaignArmyBootstrap.ResetForMission");

            if (_activeMission != null)
                _activeMission.OnBeforeAgentRemoved -= OnMissionBeforeAgentRemoved;

            if (_activeSpawnLogic != null)
                _activeSpawnLogic.OnReinforcementsSpawned -= OnNativeReinforcementsSpawned;

            _activeMission = mission;
            _activeSpawnLogic = null;
            _activeSuppliers = null;
            _activeMode = ActiveBootstrapMode.None;
            _activePlayerSide = BattleSideEnum.None;
            _activePlayerTeam = null;
            _activePlayerEnemyTeam = null;
            _reinforcementsEnabled = false;
            _nativeInitialSpawnersStartedMission = null;
            SpawnLogicInitTemporaryNonBattleTeams.Clear();
            if (!ReferenceEquals(_initialStackSpawnLogicMission, mission))
            {
                _initialStackSpawnLogicMission = null;
                _initialStackSpawnLogic = null;
                _initialStackSuppliers = null;
                _initialStackDefenderTotal = 0;
                _initialStackAttackerTotal = 0;
                _initialStackSupplierDiagnostics = string.Empty;
            }
            _nextDeferredLogUtc = DateTime.MinValue;
            _nextRuntimeDiagnosticsLogUtc = DateTime.MinValue;
            _lastRuntimeDiagnosticsSummary = string.Empty;
            _lastLoggedTeamAiActivationStateMission = null;
            _lastLoggedTeamAiActivationStateKey = string.Empty;
            TeamAiDeploymentFinishedNotifications.Clear();
            SiegeAssaultTeamAiLifecycleTacticRepairs.Clear();
        }

        public static bool TryCreateInitialSiegeAssaultWithDeploymentSpawnLogic(
            Mission mission,
            BattleSideEnum playerSide,
            string source,
            out BattleSpawnLogic battleSpawnLogic,
            out NativeMissionAgentSpawnLogic spawnLogic,
            out BattleReinforcementsSpawnController battleReinforcementsSpawnController,
            out string diagnostics)
        {
            battleSpawnLogic = null;
            spawnLogic = null;
            battleReinforcementsSpawnController = null;
            diagnostics = "mission-null";

            try
            {
                if (!ExperimentalFeatures.EnableSiegeReplayInitialNativeSpawnLogicBootstrap)
                {
                    diagnostics = "feature-disabled";
                    return false;
                }

                if (!GameNetwork.IsServer)
                {
                    diagnostics = "not-server";
                    return false;
                }

                if (mission == null)
                    return false;

                if (playerSide == BattleSideEnum.None)
                {
                    diagnostics = "player-side-none";
                    return false;
                }

                string sceneName = mission.SceneName ?? string.Empty;
                if (!SceneRuntimeClassifier.IsExactCampaignBattleScene(sceneName))
                {
                    diagnostics = "scene-not-exact-campaign Scene=" + sceneName;
                    return false;
                }

                BattleScenarioContextMessage scenarioContext = ResolveScenarioContextForMission(mission);
                if (!ExactCampaignSiegeAssaultWithDeploymentRuntime.IsExactSiegeWithDeploymentScenario(scenarioContext))
                {
                    diagnostics =
                        "not-siege-assault-with-deployment " +
                        "ScenarioKind=" + (scenarioContext?.ScenarioKind ?? "null") +
                        " SiegeSubtype=" + (scenarioContext?.SiegeContext?.SiegeSubtype ?? "null") +
                        " MissionShell=" + (scenarioContext?.SiegeContext?.MissionShell ?? "null");
                    return false;
                }

                if (!TryResolveBootstrapScenarioContract(
                        scenarioContext,
                        sceneName,
                        out string battleSpawnTag,
                        out Mission.BattleSizeType battleSizeType,
                        out string scenarioContractReason))
                {
                    diagnostics = scenarioContractReason ?? "scenario-contract-rejected";
                    return false;
                }

                if (!TryBuildSuppliers(
                        playerSide,
                        scenarioContext,
                        out IMissionTroopSupplier[] suppliers,
                        out int defenderTotal,
                        out int attackerTotal,
                        out string supplierDiagnostics))
                {
                    diagnostics = supplierDiagnostics ?? "supplier-build-failed";
                    return false;
                }

                battleSpawnLogic = new BattleSpawnLogic(battleSpawnTag);
                spawnLogic = new NativeMissionAgentSpawnLogic(suppliers, playerSide, battleSizeType);
                battleReinforcementsSpawnController = new BattleReinforcementsSpawnController();
                _initialStackSpawnLogicMission = mission;
                _initialStackSpawnLogic = spawnLogic;
                _initialStackSuppliers = suppliers;
                _initialStackDefenderTotal = defenderTotal;
                _initialStackAttackerTotal = attackerTotal;
                _initialStackSupplierDiagnostics = supplierDiagnostics ?? string.Empty;

                diagnostics =
                    "Created=True" +
                    " Scene=" + sceneName +
                    " PlayerSide=" + playerSide +
                    " BattleSpawnTag=" + battleSpawnTag +
                    " BattleSizeType=" + battleSizeType +
                    " DefenderTotal=" + defenderTotal +
                    " AttackerTotal=" + attackerTotal +
                    " Suppliers={" + (supplierDiagnostics ?? "none") + "}" +
                    " Source=" + (source ?? "unknown");
                return true;
            }
            catch (Exception ex)
            {
                diagnostics = "faulted " + ex.GetType().Name + ":" + ex.Message;
                battleSpawnLogic = null;
                spawnLogic = null;
                battleReinforcementsSpawnController = null;
                return false;
            }
        }

        public static bool TryInitialize(
            Mission mission,
            BattleSideEnum playerSide,
            string source,
            out string reason)
        {
            string initializationStep = "enter";
            reason = string.Empty;
            try
            {
                initializationStep = "validate-mission";
                if (mission == null)
                {
                    reason = "mission-null";
                    return false;
                }

                initializationStep = "reset-runtime";
                ResetForMission(mission);
                if (IsActive(mission))
                    return true;

                initializationStep = "validate-feature";
                if (!ExperimentalFeatures.EnableExactCampaignNativeArmyBootstrap)
                {
                    reason = "feature-disabled";
                    return false;
                }

                initializationStep = "validate-scene";
                string sceneName = mission.SceneName ?? string.Empty;
                if (!SceneRuntimeClassifier.IsExactCampaignBattleScene(sceneName))
                {
                    reason = "scene-not-exact-campaign";
                    return false;
                }

                initializationStep = "validate-player-side";
                if (playerSide == BattleSideEnum.None)
                {
                    reason = "player-side-none";
                    return false;
                }

                initializationStep = "validate-player-teams";
                if (mission.PlayerTeam == null || mission.PlayerEnemyTeam == null)
                {
                    reason = "player-teams-not-ready";
                    return false;
                }

                initializationStep = "validate-player-team-side";
                if (mission.PlayerTeam.Side != playerSide)
                {
                    reason =
                        "player-team-side-mismatch MissionPlayerTeam=" + mission.PlayerTeam.Side +
                        " RequestedPlayerSide=" + playerSide;
                    return false;
                }

                initializationStep = "resolve-scenario-contract";
                BattleScenarioContextMessage scenarioContext = ResolveScenarioContextForMission(mission);
                if (!TryResolveBootstrapScenarioContract(
                        scenarioContext,
                        sceneName,
                        out string battleSpawnTag,
                        out Mission.BattleSizeType battleSizeType,
                        out string scenarioContractReason))
                {
                    reason = scenarioContractReason ?? "scenario-contract-rejected";
                    return false;
                }
                bool useSiegeAmbushController = RequiresSiegeAmbushController(scenarioContext);
                bool useReliefController =
                    ExactReliefScenarioContract.IsReliefScenario(
                        scenarioContext);
                bool useLordsHallController = IsLordsHallSiegeSubtype(scenarioContext);
                bool isLandSallyOutScenario = SallyOutScenarioContract.IsSallyOutScenario(scenarioContext);
                bool isSiegeAmbushScenario =
                    SiegeAmbushScenarioContract.IsSiegeAmbushScenario(scenarioContext);
                bool isSallyOutSubtype = IsSallyOutSiegeSubtype(scenarioContext);
                bool isReliefForceAttack = IsReliefSiegeSubtype(scenarioContext);
                bool isSiegeAssaultWithDeploymentSubtype = ExactCampaignSiegeAssaultWithDeploymentRuntime.IsSiegeAssaultScenario(scenarioContext);
                bool isExactSiegeWithDeploymentSubtype =
                    ExactCampaignSiegeAssaultWithDeploymentRuntime.IsExactSiegeWithDeploymentScenario(scenarioContext);
                bool isSiegeAssaultNoDeploymentSubtype = ExactCampaignSiegeAssaultNoDeploymentRuntime.IsSiegeAssaultScenario(scenarioContext);
                bool requiresCampaignSiegeStateHandler =
                    isSiegeAssaultWithDeploymentSubtype ||
                    isSiegeAssaultNoDeploymentSubtype ||
                    useReliefController;
                Mission.MissionTeamAITypeEnum missionTeamAiType = ResolveMissionTeamAiType(scenarioContext);
                bool deferMissionTeamAiActivationUntilBattleActive =
                    ShouldDeferMissionTeamAiActivationUntilBattleActive(
                        missionTeamAiType,
                        scenarioContext);
                string teamAiDiagnostics = "team-ai-not-applied";

                initializationStep = "ensure-campaign-object-catalogs";
                ExactCampaignObjectCatalogBootstrap.EnsureLoaded("exact-native-bootstrap:" + (source ?? "unknown"));

                initializationStep = "seed-formation-banner-codes";
                TrySeedFormationBannerCodes(mission, playerSide, source, out string formationBannerDiagnostics);

                initializationStep = "build-suppliers";
                if (!TryBuildSuppliers(
                        playerSide,
                        scenarioContext,
                        out IMissionTroopSupplier[] suppliers,
                        out int defenderTotal,
                        out int attackerTotal,
                        out string supplierDiagnostics))
                {
                    reason = supplierDiagnostics ?? "supplier-build-failed";
                    return false;
                }
                MissionBehavior initialStackSpawnLogicBehavior = mission.GetMissionBehavior<NativeMissionAgentSpawnLogic>();
                if (ReferenceEquals(_initialStackSpawnLogicMission, mission) &&
                    ReferenceEquals(_initialStackSpawnLogic, initialStackSpawnLogicBehavior) &&
                    _initialStackSuppliers != null)
                {
                    suppliers = _initialStackSuppliers;
                    defenderTotal = _initialStackDefenderTotal;
                    attackerTotal = _initialStackAttackerTotal;
                    supplierDiagnostics =
                        "InitialStackReuse={" + (_initialStackSupplierDiagnostics ?? string.Empty) + "}";
                }

                string siegePreparationDiagnostics = "not-required";
                initializationStep = "resolve-siege-scene-preparation";
                if (scenarioContext?.IsSiegeBattle == true &&
                    !useLordsHallController &&
                    !isLandSallyOutScenario &&
                    !isSiegeAssaultNoDeploymentSubtype)
                {
                    if (!TryEnsureSiegeScenePreparationBehavior(
                            mission,
                            scenarioContext,
                            isSallyOutSubtype,
                            isReliefForceAttack,
                            isExactSiegeWithDeploymentSubtype,
                            out siegePreparationDiagnostics))
                    {
                        reason = siegePreparationDiagnostics ?? "siege-scene-preparation-failed";
                        return false;
                    }
                }
                else if (isSiegeAssaultNoDeploymentSubtype)
                {
                    siegePreparationDiagnostics = "skipped-native-no-deployment";
                }

                string siegeStateHandlerDiagnostics = "not-required";
                if (requiresCampaignSiegeStateHandler)
                {
                    initializationStep = "ensure-siege-assault-state-handler";
                    bool requireSiegeStateHandler = !IsDedicatedServerProcess();
                    bool hasInitialSiegeStateHandler = GetMissionBehaviorByFullName(
                        mission,
                        "SandBox.Missions.MissionLogics.CampaignSiegeStateHandler") != null;
                    if (isExactSiegeWithDeploymentSubtype)
                    {
                        siegeStateHandlerDiagnostics = hasInitialSiegeStateHandler
                            ? "Existing=True Created=False RuntimeType=CampaignSiegeStateHandler"
                            : "Existing=False Created=False Reason=missing-from-initial-stack-for-siege-with-deployment";
                        if (!hasInitialSiegeStateHandler)
                        {
                            if (requireSiegeStateHandler)
                            {
                                reason = siegeStateHandlerDiagnostics ?? "siege-assault-state-handler-missing-from-initial-stack";
                                return false;
                            }

                            ModLogger.Info(
                                "ExactCampaignArmyBootstrap: continuing siege assault bootstrap without optional CampaignSiegeStateHandler on dedicated server. " +
                                "Scene=" + (sceneName ?? "null") +
                                " Diagnostics=" + (siegeStateHandlerDiagnostics ?? "unknown") +
                                " Source=" + (source ?? "unknown"));
                            siegeStateHandlerDiagnostics =
                                "OptionalDedicatedSkip={" + (siegeStateHandlerDiagnostics ?? "unknown") + "}";
                        }
                    }
                    else if (!TryEnsureMissionBehaviorAvailableByTypeName(
                            mission,
                            "SandBox.Missions.MissionLogics.CampaignSiegeStateHandler",
                            "CampaignSiegeStateHandler",
                            out siegeStateHandlerDiagnostics))
                    {
                        if (requireSiegeStateHandler)
                        {
                            reason = siegeStateHandlerDiagnostics ?? "siege-assault-state-handler-failed";
                            return false;
                        }

                        ModLogger.Info(
                            "ExactCampaignArmyBootstrap: continuing siege assault bootstrap without optional CampaignSiegeStateHandler on dedicated server. " +
                            "Scene=" + (sceneName ?? "null") +
                            " Diagnostics=" + (siegeStateHandlerDiagnostics ?? "unknown") +
                            " Source=" + (source ?? "unknown"));
                        siegeStateHandlerDiagnostics =
                            "OptionalDedicatedSkip={" + (siegeStateHandlerDiagnostics ?? "unknown") + "}";
                    }
                }

                string siegeAssaultDeploymentDiagnostics = "not-required";

                initializationStep = "apply-mission-team-ai-type";
                mission.MissionTeamAIType = missionTeamAiType;

                initializationStep = "ensure-mission-team-ai-contract";
                if (!TryEnsureMissionTeamAiContract(
                        mission,
                        missionTeamAiType,
                        !deferMissionTeamAiActivationUntilBattleActive,
                        isExactSiegeWithDeploymentSubtype,
                        source,
                        out teamAiDiagnostics))
                {
                    reason = teamAiDiagnostics ?? "mission-team-ai-contract-failed";
                    return false;
                }

                if (useLordsHallController)
                {
                    initializationStep = "prepare-lords-hall-runtime";
                    if (!LordsHallMissionRuntime.TryPrepare(
                            mission,
                            scenarioContext,
                            out string lordsHallRuntimeDiagnostics))
                    {
                        reason = lordsHallRuntimeDiagnostics ?? "lords-hall-runtime-contract-failed";
                        return false;
                    }

                    initializationStep = "prepare-lords-hall-deployment-plan";
                    if (!TryPrepareLordsHallDeploymentPlanContract(
                            mission,
                            source,
                            out string lordsHallDeploymentPlanDiagnostics))
                    {
                        reason = lordsHallDeploymentPlanDiagnostics ?? "lords-hall-deployment-plan-failed";
                        return false;
                    }

                    initializationStep = "validate-lords-hall-contract";
                    if (mission.GetMissionBehavior<BattleSpawnLogic>() != null)
                    {
                        reason = "lords-hall-unexpected-battle-spawn-logic";
                        return false;
                    }

                    if (mission.GetMissionBehavior<NativeMissionAgentSpawnLogic>() != null)
                    {
                        reason = "lords-hall-unexpected-native-spawn-logic";
                        return false;
                    }

                    initializationStep = "log-bootstrap-contract";
                    LogBootstrapContractSnapshot(
                        mission,
                        null,
                        playerSide,
                        supplierDiagnostics +
                        " FormationBannerSeed={" + formationBannerDiagnostics + "}" +
                        " DeploymentPlan={" + lordsHallDeploymentPlanDiagnostics + "}" +
                        " RuntimeContract={LordsHall MissionReadyOnly=true}",
                        "pre-init-lords-hall-controller",
                        source);

                    initializationStep = "init-lords-hall-controller";
                    if (!TryEnsureLordsHallControllerInitialized(
                            mission,
                            suppliers,
                            playerSide,
                            scenarioContext,
                            defenderTotal,
                            attackerTotal,
                            out string lordsHallDiagnostics))
                    {
                        reason = lordsHallDiagnostics ?? "lords-hall-controller-failed";
                        return false;
                    }

                    initializationStep = "subscribe-agent-removal-events";
                    mission.OnBeforeAgentRemoved -= OnMissionBeforeAgentRemoved;
                    mission.OnBeforeAgentRemoved += OnMissionBeforeAgentRemoved;

                    initializationStep = "activate-runtime";
                    _activeMission = mission;
                    _activeSpawnLogic = null;
                    _activeSuppliers = suppliers;
                    _activeMode = ActiveBootstrapMode.LordsHallController;
                    _activePlayerSide = playerSide;
                    _activePlayerTeam = mission.PlayerTeam;
                    _activePlayerEnemyTeam = mission.PlayerEnemyTeam;
                    _reinforcementsEnabled = false;
                    reason = "initialized";

                    ModLogger.Info(
                        "ExactCampaignArmyBootstrap: initialized lords-hall army bootstrap on exact campaign scene. " +
                        "Scene=" + sceneName +
                        " PlayerSide=" + playerSide +
                        " ScenarioKind=" + (scenarioContext?.ScenarioKind ?? "Unknown") +
                        " SiegeSubtype=" + (scenarioContext?.SiegeContext?.SiegeSubtype ?? "None") +
                        " DefenderTotal=" + defenderTotal +
                        " AttackerTotal=" + attackerTotal +
                        " FormationBannerSeed={" + formationBannerDiagnostics + "}" +
                        " ObjectCatalog={" + ExactCampaignObjectCatalogBootstrap.LastSummary + "}" +
                        " SupplierDiagnostics=" + supplierDiagnostics +
                        " RuntimeDiagnostics={" + lordsHallRuntimeDiagnostics + "}" +
                        " TeamAIDiagnostics={" + teamAiDiagnostics + "}" +
                        " DeploymentPlanDiagnostics={" + lordsHallDeploymentPlanDiagnostics + "}" +
                        " ControllerDiagnostics=" + lordsHallDiagnostics +
                        " Source=" + (source ?? "unknown"));
                    return true;
                }

                initializationStep = "resolve-battle-spawn-logic";
                MissionBehavior existingBattleSpawnLogic = mission.GetMissionBehavior<BattleSpawnLogic>();
                if (existingBattleSpawnLogic == null)
                {
                    if (isExactSiegeWithDeploymentSubtype)
                    {
                        reason = "battle-spawn-logic-missing-from-initial-stack";
                        return false;
                    }

                    initializationStep = "create-battle-spawn-logic";
                    var battleSpawnLogic = new BattleSpawnLogic(battleSpawnTag);
                    mission.AddMissionBehavior(battleSpawnLogic);
                    initializationStep = "battle-spawn-logic-onbehaviorinitialize";
                    battleSpawnLogic.OnBehaviorInitialize();
                    initializationStep = "battle-spawn-logic-afterstart";
                    battleSpawnLogic.AfterStart();
                }

                initializationStep = "repair-live-contract-after-battle-spawn-logic";
                CampaignMapPatchMissionInit.TryRepairLiveMissionContract(
                    mission,
                    (source ?? "unknown") + " exact-native-bootstrap-post-battle-spawn");
                string siegeAssaultScenePreparationDiagnostics = "not-applicable";
                if (isSiegeAssaultNoDeploymentSubtype)
                {
                    initializationStep = "prepare-siege-assault-no-deployment-scene";
                    if (!ExactCampaignSiegeAssaultNoDeploymentRuntime.TryPrepareLateBattleSpawnLogic(
                            mission,
                            out siegeAssaultScenePreparationDiagnostics))
                    {
                        reason = siegeAssaultScenePreparationDiagnostics ?? "siege-assault-scene-preparation-failed";
                        return false;
                    }
                }

                initializationStep = "resolve-agent-spawn-logic";
                NativeMissionAgentSpawnLogic spawnLogic = mission.GetMissionBehavior<NativeMissionAgentSpawnLogic>();
                if (spawnLogic == null)
                {
                    if (isExactSiegeWithDeploymentSubtype)
                    {
                        reason = "agent-spawn-logic-missing-from-initial-stack";
                        return false;
                    }

                    initializationStep = "create-agent-spawn-logic";
                    spawnLogic = new NativeMissionAgentSpawnLogic(suppliers, playerSide, battleSizeType);
                    initializationStep = "add-agent-spawn-logic";
                    mission.AddMissionBehavior(spawnLogic);
                    initializationStep = "agent-spawn-logic-onbehaviorinitialize";
                    spawnLogic.OnBehaviorInitialize();
                    initializationStep = "agent-spawn-logic-afterstart";
                    spawnLogic.AfterStart();
                }

                initializationStep = "resolve-battle-reinforcements-controller";
                MissionBehavior existingBattleReinforcementsSpawnController = mission.GetMissionBehavior<BattleReinforcementsSpawnController>();
                if (existingBattleReinforcementsSpawnController == null)
                {
                    if (isExactSiegeWithDeploymentSubtype)
                    {
                        reason = "battle-reinforcements-controller-missing-from-initial-stack";
                        return false;
                    }

                    initializationStep = "create-battle-reinforcements-controller";
                    var battleReinforcementsSpawnController = new BattleReinforcementsSpawnController();
                    mission.AddMissionBehavior(battleReinforcementsSpawnController);
                    initializationStep = "battle-reinforcements-controller-onbehaviorinitialize";
                    battleReinforcementsSpawnController.OnBehaviorInitialize();
                    initializationStep = "battle-reinforcements-controller-afterstart";
                    battleReinforcementsSpawnController.AfterStart();
                }

                if (isExactSiegeWithDeploymentSubtype)
                {
                    initializationStep = "ensure-exact-siege-with-deployment-behaviors";
                    if (!ExactCampaignSiegeAssaultWithDeploymentRuntime.TryEnsureMissionBehaviorContract(
                            mission,
                            scenarioContext,
                            playerSide,
                            out siegeAssaultDeploymentDiagnostics))
                    {
                        reason = siegeAssaultDeploymentDiagnostics ?? "exact-siege-with-deployment-contract-failed";
                        return false;
                    }
                }

                string siegeAssaultBattlePowerDiagnostics = "not-required";
                if (isSiegeAssaultNoDeploymentSubtype)
                {
                    initializationStep = "ensure-siege-assault-battle-power";
                    if (!TryEnsureMissionBehaviorAvailable(
                            mission,
                            mission.GetMissionBehavior<BattlePowerCalculationLogic>(),
                            () => new BattlePowerCalculationLogic(),
                            "BattlePowerCalculationLogic",
                            out siegeAssaultBattlePowerDiagnostics))
                    {
                        reason = siegeAssaultBattlePowerDiagnostics ?? "siege-assault-battle-power-failed";
                        return false;
                    }
                }

                initializationStep = "build-native-wave-spawn-settings";
                MissionSpawnSettings spawnSettings = CreateNativeCampaignBattleWaveSpawnSettings();
                ComputeInitialSpawnCounts(
                    mission,
                    defenderTotal,
                    attackerTotal,
                    out int defenderInitial,
                    out int attackerInitial,
                    out int battleSizeBudget);
                int reinforcementWaveCount = GetResolvedReinforcementWaveCount();

                initializationStep = "ensure-deployment-team-plans";
                if (!TryEnsureDeploymentPlanTeamPlans(mission, source, out string deploymentPlanDiagnostics))
                {
                    reason = deploymentPlanDiagnostics ?? "deployment-team-plan-bridge-failed";
                    return false;
                }

                initializationStep = "override-native-battle-size";
                int nativeBattleSizeBeforeOverride = GetNativeBattleSize(spawnLogic);
                if (!TryOverrideNativeBattleSize(spawnLogic, battleSizeBudget, out string battleSizeOverrideDiagnostics))
                {
                    reason = battleSizeOverrideDiagnostics ?? "battle-size-override-failed";
                    return false;
                }
                int nativeBattleSizeAfterOverride = GetNativeBattleSize(spawnLogic);

                if (useSiegeAmbushController)
                {
                    initializationStep = "init-siege-ambush-controller";
                    PushSpawnLogicInitTeamSideOverride(mission, playerSide);
                    List<TeamSideOverrideState> temporaryTeamSideOverrides =
                        PushInitTeamSideSanitization(mission, playerSide, source);
                    try
                    {
                        initializationStep = "ensure-deployment-team-plans-post-sanitization";
                        if (!TryEnsureDeploymentPlanTeamPlans(mission, source, out string postSanitizationDeploymentPlanDiagnostics))
                        {
                            reason = postSanitizationDeploymentPlanDiagnostics ?? "deployment-team-plan-bridge-post-sanitization-failed";
                            return false;
                        }

                        string combinedDeploymentPlanDiagnostics = deploymentPlanDiagnostics;
                        if (!string.Equals(postSanitizationDeploymentPlanDiagnostics, deploymentPlanDiagnostics, StringComparison.Ordinal))
                        {
                            combinedDeploymentPlanDiagnostics +=
                                " PostSanitization={" + (postSanitizationDeploymentPlanDiagnostics ?? string.Empty) + "}";
                        }

                        initializationStep = "log-bootstrap-contract";
                        LogBootstrapContractSnapshot(
                            mission,
                            spawnLogic,
                            playerSide,
                            supplierDiagnostics +
                            " FormationBannerSeed={" + formationBannerDiagnostics + "}" +
                            " DeploymentPlanBridge={" + combinedDeploymentPlanDiagnostics + "}" +
                            " MissionTeamAI={" + teamAiDiagnostics + "}",
                            "pre-init-siege-ambush-controller",
                            source);

                        initializationStep = "ensure-siege-ambush-controller";
                        if (!TryEnsureSiegeAmbushControllerInitialized(
                                mission,
                                defenderTotal,
                                attackerTotal,
                                isSiegeAmbushScenario,
                                out string siegeAmbushDiagnostics))
                        {
                            reason = siegeAmbushDiagnostics ?? "siege-ambush-controller-failed";
                            return false;
                        }

                        initializationStep = "prepare-siege-ambush-deployment-plan";
                        if (!ExactCampaignSiegeAssaultWithDeploymentRuntime.TryPrepareDeploymentPlanContract(
                                mission,
                                suppliers,
                                playerSide,
                                out string siegeAmbushDeploymentPlanDiagnostics))
                        {
                            reason =
                                siegeAmbushDeploymentPlanDiagnostics ??
                                "siege-ambush-deployment-plan-failed";
                            return false;
                        }

                        initializationStep = "adopt-siege-ambush-spawn-contract";
                        if (!ExactCampaignSiegeAssaultWithDeploymentRuntime.TryAdoptInitializedSallyOutSpawnContract(
                                mission,
                                spawnLogic,
                                out string siegeAmbushSpawnContractDiagnostics))
                        {
                            reason =
                                siegeAmbushSpawnContractDiagnostics ??
                                "siege-ambush-spawn-contract-adopt-failed";
                            return false;
                        }

                        LogBootstrapContractSnapshot(
                            mission,
                            spawnLogic,
                            playerSide,
                            supplierDiagnostics +
                            " FormationBannerSeed={" + formationBannerDiagnostics + "}" +
                            " DeploymentPlanBridge={" + combinedDeploymentPlanDiagnostics + "}" +
                            " MissionTeamAI={" + teamAiDiagnostics + "}" +
                            " SiegeAmbush={" + siegeAmbushDiagnostics + "}" +
                            " SiegeAmbushDeploymentPlan={" + siegeAmbushDeploymentPlanDiagnostics + "}" +
                            " SiegeAmbushSpawnContract={" + siegeAmbushSpawnContractDiagnostics + "}" +
                            " RuntimeContract={SiegeAmbushWithDeployment}",
                            "post-init-siege-ambush-controller",
                            source);
                    }
                    finally
                    {
                        PopInitTeamSideSanitization(temporaryTeamSideOverrides, source);
                        PopSpawnLogicInitTeamSideOverride(mission);
                    }
                }
                else
                {
                    PushSpawnLogicInitTeamSideOverride(mission, playerSide);
                    List<TeamSideOverrideState> temporaryTeamSideOverrides =
                        PushInitTeamSideSanitization(mission, playerSide, source);
                    try
                    {
                        initializationStep = "ensure-deployment-team-plans-post-sanitization";
                        if (!TryEnsureDeploymentPlanTeamPlans(mission, source, out string postSanitizationDeploymentPlanDiagnostics))
                        {
                            reason = postSanitizationDeploymentPlanDiagnostics ?? "deployment-team-plan-bridge-post-sanitization-failed";
                            return false;
                        }

                        string combinedDeploymentPlanDiagnostics = deploymentPlanDiagnostics;
                        if (!string.Equals(postSanitizationDeploymentPlanDiagnostics, deploymentPlanDiagnostics, StringComparison.Ordinal))
                        {
                            combinedDeploymentPlanDiagnostics +=
                                " PostSanitization={" + (postSanitizationDeploymentPlanDiagnostics ?? string.Empty) + "}";
                        }

                        string reliefControllerDiagnostics = "not-required";
                        if (useReliefController)
                        {
                            initializationStep = "ensure-relief-controller";
                            if (!TryEnsureReliefControllerInitialized(
                                    mission,
                                    defenderTotal,
                                    attackerTotal,
                                    out reliefControllerDiagnostics))
                            {
                                reason =
                                    reliefControllerDiagnostics ??
                                    "relief-controller-failed";
                                return false;
                            }
                        }

                        if (isSiegeAssaultWithDeploymentSubtype)
                        {
                            initializationStep = "prepare-siege-assault-with-deployment-plan";
                            if (!ExactCampaignSiegeAssaultWithDeploymentRuntime.TryPrepareDeploymentPlanContract(
                                    mission,
                                    suppliers,
                                    playerSide,
                                    out string siegeAssaultWithDeploymentPlanDiagnostics))
                            {
                                reason = siegeAssaultWithDeploymentPlanDiagnostics ?? "siege-assault-with-deployment-plan-failed";
                                return false;
                            }

                            initializationStep = "log-siege-assault-with-deployment-contract";
                            LogBootstrapContractSnapshot(
                                mission,
                                spawnLogic,
                                playerSide,
                                supplierDiagnostics +
                                " FormationBannerSeed={" + formationBannerDiagnostics + "}" +
                                " DeploymentPlanBridge={" + combinedDeploymentPlanDiagnostics + "}" +
                                " MissionTeamAI={" + teamAiDiagnostics + "}" +
                                " SiegeScenePrep={" + siegePreparationDiagnostics + "}" +
                                " SiegeStateHandler={" + siegeStateHandlerDiagnostics + "}" +
                                " SiegeAssaultDeployment={" + siegeAssaultDeploymentDiagnostics + "}" +
                                " SiegeAssaultDeploymentPlan={" + siegeAssaultWithDeploymentPlanDiagnostics + "}" +
                                " RuntimeContract={SiegeAssaultWithDeployment}",
                                "pre-init-siege-assault-with-deployment",
                                source);
                            CampaignMapPatchMissionInit.TryRepairLiveMissionContract(
                                mission,
                                (source ?? "unknown") + " exact-native-bootstrap-post-deployment-plan");
                            initializationStep = "init-siege-assault-with-deployment";
                            if (!ExactCampaignSiegeAssaultWithDeploymentRuntime.TryApplyNativeLikeSpawnHandlerContract(
                                    mission,
                                    spawnLogic,
                                    defenderTotal,
                                    attackerTotal,
                                    defenderInitial,
                                    attackerInitial,
                                    in spawnSettings,
                                    out string siegeAssaultWithDeploymentSpawnDiagnostics))
                            {
                                reason = siegeAssaultWithDeploymentSpawnDiagnostics ?? "siege-assault-with-deployment-init-failed";
                                return false;
                            }

                            CampaignMapPatchMissionInit.TryRepairLiveMissionContract(
                                mission,
                                (source ?? "unknown") + " exact-native-bootstrap-post-deployment-init");
                        }
                        else if (isSiegeAssaultNoDeploymentSubtype)
                        {
                            initializationStep = "log-siege-assault-no-deployment-contract";
                            LogBootstrapContractSnapshot(
                                mission,
                                spawnLogic,
                                playerSide,
                                supplierDiagnostics +
                                " FormationBannerSeed={" + formationBannerDiagnostics + "}" +
                                " DeploymentPlanBridge={" + combinedDeploymentPlanDiagnostics + "}" +
                                " MissionTeamAI={" + teamAiDiagnostics + "}" +
                                " SiegeScenePrep={" + siegePreparationDiagnostics + "}" +
                                " SiegeStateHandler={" + siegeStateHandlerDiagnostics + "}" +
                                " SiegeAssaultScenePrep={" + siegeAssaultScenePreparationDiagnostics + "}" +
                                " BattlePower={" + siegeAssaultBattlePowerDiagnostics + "}" +
                                " RuntimeContract={SiegeAssaultNoDeployment}",
                                "pre-init-siege-assault-no-deployment",
                                source);
                            initializationStep = "init-siege-assault-no-deployment";
                            if (!ExactCampaignSiegeAssaultNoDeploymentRuntime.TryApplyNativeLikeSpawnHandlerContract(
                                    spawnLogic,
                                    defenderTotal,
                                    attackerTotal,
                                    defenderInitial,
                                    attackerInitial,
                                    in spawnSettings,
                                    out string siegeAssaultSpawnDiagnostics))
                            {
                                reason = siegeAssaultSpawnDiagnostics ?? "siege-assault-no-deployment-init-failed";
                                return false;
                            }
                        }
                        else
                        {
                            initializationStep = "configure-spawn-horses";
                            bool spawnDefenderHorses = SideHasMountedTroops(suppliers, BattleSideEnum.Defender);
                            bool spawnAttackerHorses = SideHasMountedTroops(suppliers, BattleSideEnum.Attacker);
                            spawnLogic.SetSpawnHorses(BattleSideEnum.Defender, spawnDefenderHorses);
                            spawnLogic.SetSpawnHorses(BattleSideEnum.Attacker, spawnAttackerHorses);

                            initializationStep = "log-bootstrap-contract";
                            LogBootstrapContractSnapshot(
                                mission,
                                spawnLogic,
                                playerSide,
                                supplierDiagnostics +
                                 " FormationBannerSeed={" + formationBannerDiagnostics + "}" +
                                 " DeploymentPlanBridge={" + combinedDeploymentPlanDiagnostics + "}" +
                                 " MissionTeamAI={" + teamAiDiagnostics + "}" +
                                 " SpawnHorses={Defender=" + spawnDefenderHorses + " Attacker=" + spawnAttackerHorses + "}" +
                                 (useReliefController
                                     ? " ReliefController={" + reliefControllerDiagnostics + "}" +
                                       " RuntimeContract={ReliefFieldCore}"
                                     : string.Empty),
                                 "pre-init-with-single-phase",
                                source);
                            initializationStep = "init-with-single-phase";
                            spawnLogic.InitWithSinglePhase(
                                defenderTotal,
                                attackerTotal,
                                defenderInitial,
                                attackerInitial,
                                spawnDefenders: defenderTotal > 0,
                                spawnAttackers: attackerTotal > 0,
                                in spawnSettings);
                        }
                    }
                    finally
                    {
                        PopInitTeamSideSanitization(temporaryTeamSideOverrides, source);
                        PopSpawnLogicInitTeamSideOverride(mission);
                    }
                }
                initializationStep = "subscribe-agent-removal-events";
                mission.OnBeforeAgentRemoved -= OnMissionBeforeAgentRemoved;
                mission.OnBeforeAgentRemoved += OnMissionBeforeAgentRemoved;

                initializationStep = "disable-reinforcements";
                spawnLogic.SetReinforcementsSpawnEnabled(false);
                spawnLogic.OnReinforcementsSpawned -= OnNativeReinforcementsSpawned;
                spawnLogic.OnReinforcementsSpawned += OnNativeReinforcementsSpawned;

                initializationStep = "activate-runtime";
                _activeMission = mission;
                _activeSpawnLogic = spawnLogic;
                _activeSuppliers = suppliers;
                _activeMode = isExactSiegeWithDeploymentSubtype
                    ? ActiveBootstrapMode.SiegeAssaultWithDeployment
                    : isSiegeAssaultNoDeploymentSubtype
                        ? ActiveBootstrapMode.SiegeAssaultNoDeployment
                        : ActiveBootstrapMode.NativeSpawnLogic;
                _activePlayerSide = playerSide;
                _activePlayerTeam = mission.PlayerTeam;
                _activePlayerEnemyTeam = mission.PlayerEnemyTeam;
                _reinforcementsEnabled = false;
                reason = "initialized";

                ModLogger.Info(
                    "ExactCampaignArmyBootstrap: initialized native-like army bootstrap on exact campaign scene. " +
                    "Scene=" + sceneName +
                    " PlayerSide=" + playerSide +
                    " ScenarioKind=" + (scenarioContext?.ScenarioKind ?? "Unknown") +
                    " SiegeSubtype=" + (scenarioContext?.SiegeContext?.SiegeSubtype ?? "None") +
                    " BattleSpawnTag=" + battleSpawnTag +
                    " BattleSizeType=" + battleSizeType +
                    " DefenderTotal=" + defenderTotal +
                    " AttackerTotal=" + attackerTotal +
                    " DefenderInitialInput=" + defenderInitial +
                    " AttackerInitialInput=" + attackerInitial +
                    " BattleSizeBudget=" + battleSizeBudget +
                    " ReinforcementWaveCount=" + reinforcementWaveCount +
                    " SpawnSettings=BattleSizeAllocating/Wave" +
                    " NativeBattleSizeBeforeOverride=" + nativeBattleSizeBeforeOverride +
                    " NativeBattleSizeAfterOverride=" + nativeBattleSizeAfterOverride +
                    " DefenderSpawnHorses=" + spawnLogic.GetSpawnHorses(BattleSideEnum.Defender) +
                    " AttackerSpawnHorses=" + spawnLogic.GetSpawnHorses(BattleSideEnum.Attacker) +
                    " BootstrapMode=" + _activeMode +
                    " FormationBannerSeed={" + formationBannerDiagnostics + "}" +
                    " ObjectCatalog={" + ExactCampaignObjectCatalogBootstrap.LastSummary + "}" +
                    " SupplierDiagnostics=" + supplierDiagnostics +
                    " Source=" + (source ?? "unknown"));
                return true;
            }
            catch (Exception ex)
            {
                string playerTeamText =
                    mission?.PlayerTeam == null
                        ? "null"
                        : mission.PlayerTeam.Side + "#" + mission.PlayerTeam.TeamIndex;
                string playerEnemyTeamText =
                    mission?.PlayerEnemyTeam == null
                        ? "null"
                        : mission.PlayerEnemyTeam.Side + "#" + mission.PlayerEnemyTeam.TeamIndex;
                reason = "exception@" + initializationStep + ":" + ex.GetType().Name + ":" + ex.Message;
                LogBootstrapContractSnapshot(
                    mission,
                    mission?.GetMissionBehavior<NativeMissionAgentSpawnLogic>(),
                    playerSide,
                    reason,
                    "exception-" + initializationStep,
                    source);
                ModLogger.Info(
                    "ExactCampaignArmyBootstrap: initialization failed with exception. " +
                    "Scene=" + (mission?.SceneName ?? "null") +
                    " PlayerSide=" + playerSide +
                    " Step=" + initializationStep +
                    " PlayerTeam=" + playerTeamText +
                    " PlayerEnemyTeam=" + playerEnemyTeamText +
                    " Error=" + ex);
                return false;
            }
        }

        private static BattleScenarioContextMessage ResolveScenarioContextForMission(Mission mission)
        {
            if (mission == null)
                return null;

            try
            {
                BattleScenarioContextMessage scenarioContext = BattleSnapshotRuntimeState.GetScenarioContext();
                if (scenarioContext != null)
                    return scenarioContext;
            }
            catch
            {
            }

            try
            {
                return BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryResolveBootstrapScenarioContract(
            BattleScenarioContextMessage scenarioContext,
            string runtimeScene,
            out string battleSpawnTag,
            out Mission.BattleSizeType battleSizeType,
            out string reason)
        {
            battleSpawnTag = BattleSpawnLogic.BattleTag;
            battleSizeType = Mission.BattleSizeType.Battle;
            reason = string.Empty;

            if (scenarioContext?.IsSiegeBattle != true)
                return true;

            string siegeSubtype = scenarioContext.SiegeContext?.SiegeSubtype ?? string.Empty;
            if (string.IsNullOrWhiteSpace(siegeSubtype))
            {
                reason = "siege-scenario-missing-subtype";
                return false;
            }

            if (SallyOutScenarioContract.IsSallyOutScenario(scenarioContext))
            {
                if (!SallyOutScenarioContract.IsValidatedScenario(
                        scenarioContext,
                        runtimeScene,
                        out string sallyOutDiagnostics))
                {
                    reason = "sally-out-contract-invalid:" + sallyOutDiagnostics;
                    return false;
                }

                battleSpawnTag = BattleSpawnLogic.BattleTag;
                battleSizeType = Mission.BattleSizeType.Battle;
                return true;
            }

            if (SiegeAmbushScenarioContract.IsSiegeAmbushScenario(scenarioContext))
            {
                if (!SiegeAmbushScenarioContract.IsValidatedScenario(
                        scenarioContext,
                        runtimeScene,
                        out string siegeAmbushDiagnostics))
                {
                    reason =
                        "siege-ambush-contract-invalid:" +
                        siegeAmbushDiagnostics;
                    return false;
                }

                battleSpawnTag = "sally_out_set";
                battleSizeType = Mission.BattleSizeType.SallyOut;
                return true;
            }

            if (string.Equals(siegeSubtype, "SiegeAssault", StringComparison.OrdinalIgnoreCase))
            {
                battleSpawnTag = BattleSpawnLogic.BattleTag;
                battleSizeType = Mission.BattleSizeType.Siege;
                return true;
            }

            if (string.Equals(siegeSubtype, "BlockadeSallyOut", StringComparison.OrdinalIgnoreCase))
            {
                reason = "siege-subtype-guarded:BlockadeSallyOut:no-land-mission-runtime-contract";
                return false;
            }

            if (string.Equals(siegeSubtype, "Relief", StringComparison.OrdinalIgnoreCase))
            {
                if (!ExactReliefScenarioContract.IsValidatedScenario(
                        scenarioContext,
                        runtimeScene,
                        out string reliefDiagnostics))
                {
                    reason =
                        "relief-contract-invalid:" +
                        reliefDiagnostics;
                    return false;
                }

                battleSpawnTag = BattleSpawnLogic.ReliefForceAttackTag;
                battleSizeType = Mission.BattleSizeType.Siege;
                return true;
            }

            if (string.Equals(siegeSubtype, LordsHallScenarioContract.SiegeSubtype, StringComparison.OrdinalIgnoreCase))
            {
                battleSpawnTag = BattleSpawnLogic.BattleTag;
                battleSizeType = Mission.BattleSizeType.Siege;
                return true;
            }

            if (string.Equals(siegeSubtype, "Blockade", StringComparison.OrdinalIgnoreCase))
            {
                reason = "siege-subtype-guarded:Blockade:no-mission-runtime-contract";
                return false;
            }

            reason = "siege-subtype-guarded:" + siegeSubtype;
            return false;
        }

        private static bool RequiresSiegeAmbushController(BattleScenarioContextMessage scenarioContext)
        {
            return SiegeAmbushScenarioContract.IsSiegeAmbushScenario(
                scenarioContext);
        }

        private static bool IsLordsHallSiegeSubtype(BattleScenarioContextMessage scenarioContext)
        {
            return LordsHallScenarioContract.IsLordsHallScenario(scenarioContext);
        }

        private static bool IsSallyOutSiegeSubtype(BattleScenarioContextMessage scenarioContext)
        {
            string siegeSubtype = scenarioContext?.SiegeContext?.SiegeSubtype ?? string.Empty;
            return string.Equals(
                       siegeSubtype,
                       "BlockadeSallyOut",
                       StringComparison.OrdinalIgnoreCase) ||
                   SiegeAmbushScenarioContract.IsSiegeAmbushScenario(
                       scenarioContext);
        }

        private static bool IsReliefSiegeSubtype(BattleScenarioContextMessage scenarioContext)
        {
            return ExactReliefScenarioContract.IsReliefScenario(
                scenarioContext);
        }

        private static bool IsSiegeAssaultSubtype(BattleScenarioContextMessage scenarioContext)
        {
            return ExactCampaignSiegeAssaultWithDeploymentRuntime.IsSiegeAssaultScenario(scenarioContext) ||
                   ExactCampaignSiegeAssaultNoDeploymentRuntime.IsSiegeAssaultScenario(scenarioContext);
        }

        private static Mission.MissionTeamAITypeEnum ResolveMissionTeamAiType(BattleScenarioContextMessage scenarioContext)
        {
            if (scenarioContext?.IsSiegeBattle != true)
                return Mission.MissionTeamAITypeEnum.FieldBattle;

            if (SallyOutScenarioContract.IsSallyOutScenario(scenarioContext))
                return Mission.MissionTeamAITypeEnum.FieldBattle;

            if (IsSallyOutSiegeSubtype(scenarioContext))
                return Mission.MissionTeamAITypeEnum.SallyOut;

            if (ExactCampaignSiegeAssaultWithDeploymentRuntime.IsSiegeAssaultScenario(scenarioContext))
                return Mission.MissionTeamAITypeEnum.Siege;

            // Native OpenSiegeMissionNoDeployment currently seeds assault missions through
            // MissionCombatantsLogic(FieldBattle), so the coop exact-runtime must not
            // force Siege TeamAI onto the hybrid MultiplayerBattle shell.
            if (ExactCampaignSiegeAssaultNoDeploymentRuntime.IsSiegeAssaultScenario(scenarioContext))
                return Mission.MissionTeamAITypeEnum.FieldBattle;

            if (LordsHallScenarioContract.IsLordsHallScenario(scenarioContext))
                return Mission.MissionTeamAITypeEnum.NoTeamAI;

            if (string.Equals(scenarioContext.SiegeContext?.SiegeSubtype, "Blockade", StringComparison.OrdinalIgnoreCase))
                return Mission.MissionTeamAITypeEnum.NoTeamAI;

            return Mission.MissionTeamAITypeEnum.Siege;
        }

        private static bool ShouldDeferMissionTeamAiActivationUntilBattleActive(
            Mission.MissionTeamAITypeEnum missionTeamAiType,
            BattleScenarioContextMessage scenarioContext)
        {
            if (ExactCampaignSiegeAssaultWithDeploymentRuntime.IsExactSiegeWithDeploymentScenario(scenarioContext))
                return false;

            return missionTeamAiType == Mission.MissionTeamAITypeEnum.Siege ||
                   missionTeamAiType == Mission.MissionTeamAITypeEnum.SallyOut;
        }

        private static bool TryEnsureSiegeScenePreparationBehavior(
            Mission mission,
            BattleScenarioContextMessage scenarioContext,
            bool isSallyOutSubtype,
            bool isReliefForceAttack,
            bool requireInitialStack,
            out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            MissionBehavior existingSiegePreparationHandler = mission.GetMissionBehavior<SiegeMissionPreparationHandler>();
            if (existingSiegePreparationHandler != null)
            {
                diagnostics = "existing";
                return true;
            }

            if (requireInitialStack)
            {
                diagnostics = "missing-from-initial-stack-for-siege-with-deployment";
                return false;
            }

            float[] nativeSafeWallHitPointRatios = ResolveNativeSafeWallHitPointRatios(
                mission,
                scenarioContext,
                out string wallRatioDiagnostics);
            var siegePreparationHandler = new SiegeMissionPreparationHandler(
                isSallyOutSubtype,
                isReliefForceAttack,
                nativeSafeWallHitPointRatios,
                scenarioContext?.SiegeContext?.HasAnySiegeTower == true);
            mission.AddMissionBehavior(siegePreparationHandler);
            try
            {
                siegePreparationHandler.OnBehaviorInitialize();
                siegePreparationHandler.AfterStart();
                diagnostics =
                    "created IsSallyOut=" + isSallyOutSubtype +
                    " IsReliefForceAttack=" + isReliefForceAttack +
                    " WallRatioSanitization=" + wallRatioDiagnostics;
            }
            catch (Exception ex)
            {
                bool allowBestEffortAssaultContinuation = !isSallyOutSubtype && !isReliefForceAttack;
                diagnostics =
                    "created-faulted " +
                    ex.GetType().Name + ":" + ex.Message +
                    " IsSallyOut=" + isSallyOutSubtype +
                    " IsReliefForceAttack=" + isReliefForceAttack +
                    " WallRatioSanitization=" + wallRatioDiagnostics +
                    " BestEffort=" + allowBestEffortAssaultContinuation;
                if (!allowBestEffortAssaultContinuation)
                    return false;

                ModLogger.Info(
                    "ExactCampaignArmyBootstrap: siege scene preparation faulted during assault bootstrap; " +
                    "continuing in best-effort mode because late exact runtime only needs spawn/runtime " +
                    "contracts at this stage. " +
                    "Scene=" + (mission.SceneName ?? "null") +
                    " Diagnostics=" + diagnostics);
            }
            return true;
        }

        private static float[] ResolveNativeSafeWallHitPointRatios(
            Mission mission,
            BattleScenarioContextMessage scenarioContext,
            out string diagnostics)
        {
            diagnostics = "empty";
            List<float> rawRatios = scenarioContext?.SiegeContext?.WallHitPointRatios?
                .Where(value => !float.IsNaN(value) && !float.IsInfinity(value))
                .Select(value => value < 0f ? 0f : (value > 1f ? 1f : value))
                .ToList() ?? new List<float>();
            if (rawRatios.Count <= 0)
                return Array.Empty<float>();

            int breakableWallCount = CountBreakableSiegeWallSegments(mission);
            if (breakableWallCount <= 0)
            {
                diagnostics =
                    "cleared-for-native-safety RawCount=" + rawRatios.Count +
                    " BreakableWallCount=0";
                return Array.Empty<float>();
            }

            if (breakableWallCount > 2)
            {
                diagnostics =
                    "cleared-for-native-safety RawCount=" + rawRatios.Count +
                    " BreakableWallCount=" + breakableWallCount +
                    " NativeLimit=2";
                return Array.Empty<float>();
            }

            int safeCount = Math.Min(rawRatios.Count, breakableWallCount);
            if (safeCount <= 0)
            {
                diagnostics =
                    "cleared-for-native-safety RawCount=" + rawRatios.Count +
                    " BreakableWallCount=" + breakableWallCount;
                return Array.Empty<float>();
            }

            if (safeCount == rawRatios.Count)
            {
                diagnostics =
                    "unchanged Count=" + rawRatios.Count +
                    " BreakableWallCount=" + breakableWallCount;
                return rawRatios.ToArray();
            }

            diagnostics =
                "trimmed-for-native-safety RawCount=" + rawRatios.Count +
                " SafeCount=" + safeCount +
                " BreakableWallCount=" + breakableWallCount;
            return rawRatios.Take(safeCount).ToArray();
        }

        private static int CountBreakableSiegeWallSegments(Mission mission)
        {
            if (mission?.ActiveMissionObjects == null)
                return 0;

            try
            {
                return mission.ActiveMissionObjects
                    .FindAllWithType<WallSegment>()
                    .Count(wallSegment =>
                        wallSegment != null &&
                        wallSegment.DefenseSide != FormationAI.BehaviorSide.BehaviorSideNotSet &&
                        wallSegment.GameEntity != null &&
                        wallSegment.GameEntity.GetChildren().Any(child => child != null && child.HasTag("broken_child")));
            }
            catch
            {
                return 0;
            }
        }

        private static bool TryEnsureMissionTeamAiContract(
            Mission mission,
            Mission.MissionTeamAITypeEnum missionTeamAiType,
            bool shouldActivateTeamAi,
            bool requireInitialStackPrerequisites,
            string source,
            out string diagnostics)
        {
            diagnostics = "team-ai-not-required";
            if (mission == null)
                return false;

            if (missionTeamAiType == Mission.MissionTeamAITypeEnum.NoTeamAI)
                return TrySuppressMissionTeamAiContract(mission, missionTeamAiType, out diagnostics);

            if (missionTeamAiType != Mission.MissionTeamAITypeEnum.Siege &&
                missionTeamAiType != Mission.MissionTeamAITypeEnum.SallyOut)
            {
                return true;
            }

            if (!shouldActivateTeamAi)
                return TrySuppressMissionTeamAiContract(mission, missionTeamAiType, out diagnostics);

            if (!TryEnsureMissionTeamAiRuntimePrerequisites(
                    mission,
                    missionTeamAiType,
                    requireInitialStackPrerequisites,
                    out string prerequisiteDiagnostics))
            {
                diagnostics =
                    "prerequisites={" + prerequisiteDiagnostics + "} " +
                    "Source=" + (source ?? "unknown");
                return false;
            }

            if (missionTeamAiType == Mission.MissionTeamAITypeEnum.Siege &&
                IsSiegeAssaultWithDeploymentScenario(mission))
            {
                return TryEnsureSiegeAssaultWithDeploymentMissionTeamAiContract(
                    mission,
                    source,
                    out diagnostics);
            }

            Team firstTeam;
            Team secondTeam;
            string firstLabel;
            string secondLabel;
            ResolveMissionTeamAiInitializationOrder(
                mission,
                missionTeamAiType,
                out firstTeam,
                out secondTeam,
                out firstLabel,
                out secondLabel);

            bool firstReady = TryEnsureMissionTeamAiForTeam(
                mission,
                firstTeam,
                missionTeamAiType,
                out string firstDiagnostics);
            bool secondReady = TryEnsureMissionTeamAiForTeam(
                mission,
                secondTeam,
                missionTeamAiType,
                out string secondDiagnostics);
            diagnostics =
                "TeamAIType=" + missionTeamAiType +
                " " + firstLabel + "={" + firstDiagnostics + "}" +
                " " + secondLabel + "={" + secondDiagnostics + "}" +
                " Source=" + (source ?? "unknown");
            return firstReady && secondReady;
        }

        private static bool TryPrepareLordsHallDeploymentPlanContract(
            Mission mission,
            string source,
            out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            if (!TryEnsureDeploymentPlanTeamPlans(
                    mission,
                    source,
                    out string teamPlanDiagnostics))
            {
                diagnostics = "TeamPlans={" + (teamPlanDiagnostics ?? "unknown") + "}";
                return false;
            }

            try
            {
                mission.DeploymentPlan.MakeDefaultDeploymentPlans();
            }
            catch (Exception ex)
            {
                diagnostics =
                    "TeamPlans={" + (teamPlanDiagnostics ?? "unknown") + "}" +
                    " MakeDefaultFailed=" + ex.GetType().Name + ":" + ex.Message;
                return false;
            }

            Team attackerTeam = mission.AttackerTeam;
            Team defenderTeam = mission.DefenderTeam;
            bool attackerPlanMade =
                attackerTeam != null && mission.DeploymentPlan.IsPlanMade(attackerTeam);
            bool defenderPlanMade =
                defenderTeam != null && mission.DeploymentPlan.IsPlanMade(defenderTeam);

            diagnostics =
                "TeamPlans={" + (teamPlanDiagnostics ?? "unknown") + "}" +
                " AttackerTeam=" + (attackerTeam == null ? "null" : "#" + attackerTeam.TeamIndex) +
                " AttackerPlanMade=" + attackerPlanMade +
                " DefenderTeam=" + (defenderTeam == null ? "null" : "#" + defenderTeam.TeamIndex) +
                " DefenderPlanMade=" + defenderPlanMade +
                " Source=" + (source ?? "unknown");
            return attackerPlanMade && defenderPlanMade;
        }

        private static bool IsSiegeAssaultWithDeploymentScenario(Mission mission)
        {
            return ExactCampaignSiegeAssaultWithDeploymentRuntime.IsSiegeAssaultScenario(
                ResolveScenarioContextForMission(mission));
        }

        private static bool TryEnsureSiegeAssaultWithDeploymentMissionTeamAiContract(
            Mission mission,
            string source,
            out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            Team firstTeam;
            Team secondTeam;
            string firstLabel;
            string secondLabel;
            ResolveMissionTeamAiInitializationOrder(
                mission,
                Mission.MissionTeamAITypeEnum.Siege,
                out firstTeam,
                out secondTeam,
                out firstLabel,
                out secondLabel);

            bool firstInstanceReady = TryEnsureMissionTeamAiInstanceForTeamSafely(
                mission,
                firstTeam,
                Mission.MissionTeamAITypeEnum.Siege,
                firstLabel,
                out TeamAIComponent firstTeamAi,
                out bool firstChanged,
                out string firstInstanceDiagnostics);
            bool secondInstanceReady = TryEnsureMissionTeamAiInstanceForTeamSafely(
                mission,
                secondTeam,
                Mission.MissionTeamAITypeEnum.Siege,
                secondLabel,
                out TeamAIComponent secondTeamAi,
                out bool secondChanged,
                out string secondInstanceDiagnostics);

            bool firstContractReady = false;
            string firstContractDiagnostics = "skipped-instance-not-ready";
            if (firstInstanceReady)
            {
                firstContractReady = TryFinalizeMissionTeamAiForTeamSafely(
                    mission,
                    firstTeam,
                    firstTeamAi,
                    Mission.MissionTeamAITypeEnum.Siege,
                    firstChanged,
                    firstLabel,
                    out firstContractDiagnostics);
            }

            bool secondContractReady = false;
            string secondContractDiagnostics = "skipped-instance-not-ready";
            if (secondInstanceReady)
            {
                secondContractReady = TryFinalizeMissionTeamAiForTeamSafely(
                    mission,
                    secondTeam,
                    secondTeamAi,
                    Mission.MissionTeamAITypeEnum.Siege,
                    secondChanged,
                    secondLabel,
                    out secondContractDiagnostics);
            }

            diagnostics =
                "TeamAIType=Siege TwoPhase=True" +
                " " + firstLabel + "={Instance={" + firstInstanceDiagnostics + "} Contract={" + firstContractDiagnostics + "}}" +
                " " + secondLabel + "={Instance={" + secondInstanceDiagnostics + "} Contract={" + secondContractDiagnostics + "}}" +
                " Source=" + (source ?? "unknown");
            return firstInstanceReady &&
                   secondInstanceReady &&
                   firstContractReady &&
                   secondContractReady;
        }

        private static bool TryEnsureMissionTeamAiRuntimePrerequisites(
            Mission mission,
            Mission.MissionTeamAITypeEnum missionTeamAiType,
            bool requireInitialStackPrerequisites,
            out string diagnostics)
        {
            diagnostics = "not-required";
            if (mission == null)
            {
                diagnostics = "mission-null";
                return false;
            }

            if (missionTeamAiType != Mission.MissionTeamAITypeEnum.Siege &&
                missionTeamAiType != Mission.MissionTeamAITypeEnum.SallyOut)
            {
                return true;
            }

            if (!TryEnsureMissionBehaviorAvailable(
                    mission,
                    mission.GetMissionBehavior<CasualtyHandler>(),
                    () => new CasualtyHandler(),
                    "CasualtyHandler",
                    out string casualtyDiagnostics,
                    !requireInitialStackPrerequisites))
            {
                diagnostics = "CasualtyHandler={" + casualtyDiagnostics + "}";
                return false;
            }

            if (!TryEnsureMissionBehaviorAvailable(
                    mission,
                    mission.GetMissionBehavior<BattlePowerCalculationLogic>(),
                    () => new BattlePowerCalculationLogic(),
                    "BattlePowerCalculationLogic",
                    out string battlePowerDiagnostics,
                    !requireInitialStackPrerequisites))
            {
                diagnostics =
                    "CasualtyHandler={" + casualtyDiagnostics + "} " +
                    "BattlePowerCalculationLogic={" + battlePowerDiagnostics + "}";
                return false;
            }

            diagnostics =
                "CasualtyHandler={" + casualtyDiagnostics + "} " +
                "BattlePowerCalculationLogic={" + battlePowerDiagnostics + "}";
            return true;
        }

        private static bool TryEnsureMissionBehaviorAvailable<TBehavior>(
            Mission mission,
            TBehavior existingBehavior,
            Func<TBehavior> behaviorFactory,
            string behaviorName,
            out string diagnostics,
            bool allowCreation = true)
            where TBehavior : MissionBehavior
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            if (existingBehavior != null)
            {
                diagnostics = "Existing=True Created=False";
                return true;
            }

            if (!allowCreation)
            {
                diagnostics = "Existing=False Created=False Reason=missing-from-initial-stack BehaviorName=" + behaviorName;
                return false;
            }

            if (behaviorFactory == null)
            {
                diagnostics = "Existing=False Created=False Reason=factory-null";
                return false;
            }

            try
            {
                TBehavior behavior = behaviorFactory();
                if (behavior == null)
                {
                    diagnostics = "Existing=False Created=False Reason=factory-returned-null";
                    return false;
                }

                mission.AddMissionBehavior(behavior);
                behavior.OnBehaviorInitialize();
                behavior.AfterStart();
                diagnostics = "Existing=False Created=True RuntimeType=" + behavior.GetType().Name;
                return true;
            }
            catch (Exception ex)
            {
                diagnostics =
                    "Existing=False Created=False Reason=" +
                    DescribeBehaviorAvailabilityException(ex);
                return false;
            }
        }

        private static bool TryEnsureMissionBehaviorAvailableByTypeName(
            Mission mission,
            string behaviorTypeFullName,
            string behaviorName,
            out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            if (string.IsNullOrWhiteSpace(behaviorTypeFullName))
            {
                diagnostics = "Existing=False Created=False Reason=type-name-empty";
                return false;
            }

            MissionBehavior existingBehavior = GetMissionBehaviorByFullName(mission, behaviorTypeFullName);
            if (existingBehavior != null)
            {
                diagnostics = "Existing=True Created=False RuntimeType=" + existingBehavior.GetType().Name;
                return true;
            }

            try
            {
                Type behaviorType = ResolveRuntimeType(behaviorTypeFullName, "SandBox");
                if (behaviorType == null)
                {
                    diagnostics = "Existing=False Created=False Reason=type-not-found FullName=" + behaviorTypeFullName;
                    return false;
                }

                if (!typeof(MissionBehavior).IsAssignableFrom(behaviorType))
                {
                    diagnostics = "Existing=False Created=False Reason=type-not-mission-behavior RuntimeType=" + behaviorType.FullName;
                    return false;
                }

                var behavior = Activator.CreateInstance(behaviorType) as MissionBehavior;
                if (behavior == null)
                {
                    diagnostics = "Existing=False Created=False Reason=activator-returned-null RuntimeType=" + behaviorType.FullName;
                    return false;
                }

                mission.AddMissionBehavior(behavior);
                behavior.OnBehaviorInitialize();
                behavior.AfterStart();
                diagnostics = "Existing=False Created=True RuntimeType=" + behavior.GetType().Name;
                return true;
            }
            catch (Exception ex)
            {
                diagnostics =
                    "Existing=False Created=False Reason=" +
                    DescribeBehaviorAvailabilityException(ex) +
                    " BehaviorName=" + behaviorName;
                return false;
            }
        }

        private static MissionBehavior GetMissionBehaviorByFullName(Mission mission, string behaviorTypeFullName)
        {
            if (mission?.MissionBehaviors == null || string.IsNullOrWhiteSpace(behaviorTypeFullName))
                return null;

            for (int i = 0; i < mission.MissionBehaviors.Count; i++)
            {
                MissionBehavior behavior = mission.MissionBehaviors[i];
                if (string.Equals(
                        behavior?.GetType().FullName,
                        behaviorTypeFullName,
                        StringComparison.Ordinal))
                {
                    return behavior;
                }
            }

            return null;
        }

        private static bool IsDedicatedServerProcess()
        {
            try
            {
                string processName = System.Diagnostics.Process.GetCurrentProcess().ProcessName ?? string.Empty;
                return processName.IndexOf("Dedicated", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static string DescribeBehaviorAvailabilityException(Exception ex)
        {
            if (ex == null)
                return "unknown-exception";

            Exception actual = ex is TargetInvocationException invocationException &&
                               invocationException.InnerException != null
                ? invocationException.InnerException
                : ex;

            var diagnostics = new StringBuilder();
            if (!ReferenceEquals(actual, ex))
            {
                diagnostics.Append("Wrapper=")
                    .Append(ex.GetType().Name)
                    .Append(":")
                    .Append(ex.Message)
                    .Append(' ');
            }

            diagnostics.Append("Root=")
                .Append(actual.GetType().Name)
                .Append(":")
                .Append(actual.Message);

            if (actual.InnerException != null)
            {
                diagnostics.Append(" Inner=")
                    .Append(actual.InnerException.GetType().Name)
                    .Append(":")
                    .Append(actual.InnerException.Message);
            }

            return diagnostics.ToString();
        }

        private static Type ResolveRuntimeType(string typeFullName, params string[] preferredAssemblyNames)
        {
            if (string.IsNullOrWhiteSpace(typeFullName))
                return null;

            if (preferredAssemblyNames != null)
            {
                foreach (string assemblyName in preferredAssemblyNames)
                {
                    if (string.IsNullOrWhiteSpace(assemblyName))
                        continue;

                    Type resolvedType = Type.GetType(typeFullName + ", " + assemblyName, throwOnError: false);
                    if (resolvedType != null)
                        return resolvedType;
                }
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type resolvedType = assembly?.GetType(typeFullName, throwOnError: false, ignoreCase: false);
                if (resolvedType != null)
                    return resolvedType;
            }

            return null;
        }

        private static bool TrySuppressMissionTeamAiContract(
            Mission mission,
            Mission.MissionTeamAITypeEnum missionTeamAiType,
            out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            var teamDiagnostics = new List<string>();
            bool changed = false;
            foreach (Team team in new[] { mission.AttackerTeam, mission.DefenderTeam })
            {
                if (team == null || team.Side == BattleSideEnum.None)
                    continue;

                TeamAIComponent existingTeamAi = team.TeamAI;
                string existingTypeName = existingTeamAi?.GetType().Name ?? "null";
                if (existingTeamAi == null)
                {
                    teamDiagnostics.Add("Side=" + team.Side + " ExistingType=null Cleared=False");
                    continue;
                }

                if (TeamAiBackingField == null)
                {
                    diagnostics =
                        "team-ai-backing-field-missing Type=" + missionTeamAiType +
                        " Side=" + team.Side;
                    return false;
                }

                try
                {
                    if (team.HasTeamAi)
                        team.ResetTactic();
                }
                catch
                {
                }

                try
                {
                    TeamAiBackingField.SetValue(team, null);
                    team.QuerySystem?.Expire();
                    changed = true;
                    teamDiagnostics.Add("Side=" + team.Side + " ExistingType=" + existingTypeName + " Cleared=True");
                }
                catch (Exception ex)
                {
                    diagnostics =
                        "team-ai-clear-failed Type=" + missionTeamAiType +
                        " Side=" + team.Side +
                        " Error=" + ex.GetType().Name + ":" + ex.Message;
                    return false;
                }
            }

            diagnostics =
                "DeferredUntilBattleActive=True" +
                " Changed=" + changed +
                " Teams=[" + string.Join("; ", teamDiagnostics) + "]";
            return true;
        }

        private static bool HasExpectedMissionTeamAiContract(
            Mission mission,
            Mission.MissionTeamAITypeEnum missionTeamAiType)
        {
            if (mission == null)
                return false;

            switch (missionTeamAiType)
            {
                case Mission.MissionTeamAITypeEnum.FieldBattle:
                    return mission.AttackerTeam?.TeamAI is TeamAIGeneral &&
                           mission.DefenderTeam?.TeamAI is TeamAIGeneral;

                case Mission.MissionTeamAITypeEnum.Siege:
                    return mission.AttackerTeam?.TeamAI is TeamAISiegeAttacker &&
                           mission.DefenderTeam?.TeamAI is TeamAISiegeDefender;

                case Mission.MissionTeamAITypeEnum.SallyOut:
                    return mission.AttackerTeam?.TeamAI is TeamAISallyOutDefender &&
                           mission.DefenderTeam?.TeamAI is TeamAISallyOutAttacker;

                default:
                    return true;
            }
        }

        private static bool IsMissionTeamAiSuppressed(Mission mission)
        {
            if (mission == null)
                return false;

            return mission.AttackerTeam?.TeamAI == null &&
                   mission.DefenderTeam?.TeamAI == null;
        }

        private static void LogMissionTeamAiActivationState(
            Mission mission,
            CoopBattlePhase currentPhase,
            Mission.MissionTeamAITypeEnum missionTeamAiType,
            bool shouldActivateTeamAi,
            bool success,
            string diagnostics,
            string source)
        {
            string attackerType = mission?.AttackerTeam?.TeamAI?.GetType().Name ?? "null";
            string defenderType = mission?.DefenderTeam?.TeamAI?.GetType().Name ?? "null";
            string logKey =
                currentPhase + "|" +
                missionTeamAiType + "|" +
                shouldActivateTeamAi + "|" +
                success + "|" +
                attackerType + "|" +
                defenderType + "|" +
                (diagnostics ?? string.Empty);
            if (ReferenceEquals(_lastLoggedTeamAiActivationStateMission, mission) &&
                string.Equals(_lastLoggedTeamAiActivationStateKey, logKey, StringComparison.Ordinal))
            {
                return;
            }

            _lastLoggedTeamAiActivationStateMission = mission;
            _lastLoggedTeamAiActivationStateKey = logKey;
            ModLogger.Info(
                "ExactCampaignArmyBootstrap: synced deferred mission team AI activation state. " +
                "Scene=" + (mission?.SceneName ?? "null") +
                " Phase=" + currentPhase +
                " TeamAIType=" + missionTeamAiType +
                " Activate=" + shouldActivateTeamAi +
                " Success=" + success +
                " AttackerAI=" + attackerType +
                " DefenderAI=" + defenderType +
                " Diagnostics={" + (diagnostics ?? string.Empty) + "}" +
                " Source=" + (source ?? "unknown"));
        }

        public static void TrySyncMissionTeamAiActivationState(
            Mission mission,
            CoopBattlePhase currentPhase,
            string source)
        {
            if (!IsActive(mission))
                return;

            BattleScenarioContextMessage scenarioContext = ResolveScenarioContextForMission(mission);
            Mission.MissionTeamAITypeEnum missionTeamAiType = ResolveMissionTeamAiType(scenarioContext);
            if (!ShouldDeferMissionTeamAiActivationUntilBattleActive(missionTeamAiType, scenarioContext))
                return;

            bool shouldActivateTeamAi =
                currentPhase >= CoopBattlePhase.BattleActive &&
                currentPhase < CoopBattlePhase.BattleEnded;
            if (shouldActivateTeamAi)
            {
                if (HasExpectedMissionTeamAiContract(mission, missionTeamAiType))
                {
                    LogMissionTeamAiActivationState(
                        mission,
                        currentPhase,
                        missionTeamAiType,
                        shouldActivateTeamAi,
                        success: true,
                        diagnostics: "already-active",
                        source: source);
                    return;
                }
            }
            else
            {
                if (IsMissionTeamAiSuppressed(mission))
                {
                    LogMissionTeamAiActivationState(
                        mission,
                        currentPhase,
                        missionTeamAiType,
                        shouldActivateTeamAi,
                        success: true,
                        diagnostics: "already-suppressed",
                        source: source);
                    return;
                }
            }

            bool success = TryEnsureMissionTeamAiContract(
                mission,
                missionTeamAiType,
                shouldActivateTeamAi,
                false,
                source,
                out string diagnostics);
            LogMissionTeamAiActivationState(
                mission,
                currentPhase,
                missionTeamAiType,
                shouldActivateTeamAi,
                success,
                diagnostics,
                source);
        }

        private static void ResolveMissionTeamAiInitializationOrder(
            Mission mission,
            Mission.MissionTeamAITypeEnum missionTeamAiType,
            out Team firstTeam,
            out Team secondTeam,
            out string firstLabel,
            out string secondLabel)
        {
            // Mirror the native creation order because siege AI constructors depend on
            // QuerySystem being initialized by the complementary side first.
            if (missionTeamAiType == Mission.MissionTeamAITypeEnum.SallyOut)
            {
                firstTeam = mission?.AttackerTeam;
                secondTeam = mission?.DefenderTeam;
                firstLabel = "First";
                secondLabel = "Second";
                return;
            }

            firstTeam = mission?.DefenderTeam;
            secondTeam = mission?.AttackerTeam;
            firstLabel = "First";
            secondLabel = "Second";
        }

        private static bool TryEnsureMissionTeamAiForTeam(
            Mission mission,
            Team team,
            Mission.MissionTeamAITypeEnum missionTeamAiType,
            out string diagnostics)
        {
            if (!TryEnsureMissionTeamAiInstanceForTeam(
                    mission,
                    team,
                    missionTeamAiType,
                    out TeamAIComponent activeTeamAi,
                    out bool changed,
                    out string instanceDiagnostics))
            {
                diagnostics = instanceDiagnostics;
                return false;
            }

            if (!TryFinalizeMissionTeamAiForTeam(
                    mission,
                    team,
                    activeTeamAi,
                    missionTeamAiType,
                    changed,
                    out string contractDiagnostics))
            {
                diagnostics =
                    instanceDiagnostics +
                    " Contract={" + contractDiagnostics + "}";
                return false;
            }

            diagnostics =
                instanceDiagnostics +
                " Contract={" + contractDiagnostics + "}";
            return true;
        }

        private static bool TryEnsureMissionTeamAiInstanceForTeamSafely(
            Mission mission,
            Team team,
            Mission.MissionTeamAITypeEnum missionTeamAiType,
            string label,
            out TeamAIComponent activeTeamAi,
            out bool changed,
            out string diagnostics)
        {
            try
            {
                return TryEnsureMissionTeamAiInstanceForTeam(
                    mission,
                    team,
                    missionTeamAiType,
                    out activeTeamAi,
                    out changed,
                    out diagnostics);
            }
            catch (Exception ex)
            {
                activeTeamAi = null;
                changed = false;
                diagnostics =
                    "exception Label=" + (label ?? "unknown") +
                    " Side=" + (team?.Side.ToString() ?? "null") +
                    " Type=" + missionTeamAiType +
                    " Error=" + ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        private static bool TryFinalizeMissionTeamAiForTeamSafely(
            Mission mission,
            Team team,
            TeamAIComponent activeTeamAi,
            Mission.MissionTeamAITypeEnum missionTeamAiType,
            bool teamAiWasChanged,
            string label,
            out string diagnostics)
        {
            try
            {
                return TryFinalizeMissionTeamAiForTeam(
                    mission,
                    team,
                    activeTeamAi,
                    missionTeamAiType,
                    teamAiWasChanged,
                    out diagnostics);
            }
            catch (Exception ex)
            {
                diagnostics =
                    "exception Label=" + (label ?? "unknown") +
                    " Side=" + (team?.Side.ToString() ?? "null") +
                    " Type=" + missionTeamAiType +
                    " Error=" + ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        private static bool TryEnsureMissionTeamAiInstanceForTeam(
            Mission mission,
            Team team,
            Mission.MissionTeamAITypeEnum missionTeamAiType,
            out TeamAIComponent activeTeamAi,
            out bool changed,
            out string diagnostics)
        {
            activeTeamAi = null;
            changed = false;
            diagnostics = "team-null";
            if (mission == null || team == null)
                return false;

            Type desiredTeamAiType = ResolveMissionTeamAiImplementationType(team, missionTeamAiType);
            if (desiredTeamAiType == null)
            {
                diagnostics = "team-ai-unavailable Type=" + missionTeamAiType + " Side=" + team.Side;
                return false;
            }

            TeamAIComponent existingTeamAi = team.TeamAI;
            string desiredTypeName = desiredTeamAiType.Name;
            string existingTypeName = existingTeamAi?.GetType().Name ?? "null";
            changed = existingTeamAi == null || !desiredTeamAiType.IsInstanceOfType(existingTeamAi);
            TeamAIComponent targetTeamAi = changed
                ? CreateDesiredTeamAi(mission, team, missionTeamAiType)
                : existingTeamAi;
            if (targetTeamAi == null)
            {
                diagnostics =
                    "team-ai-create-failed Type=" + missionTeamAiType +
                    " Side=" + team.Side +
                    " ExistingType=" + existingTypeName +
                    " DesiredType=" + desiredTypeName +
                    " Changed=" + changed;
                return false;
            }

            if (changed)
                team.AddTeamAI(targetTeamAi);

            activeTeamAi = team.TeamAI ?? targetTeamAi;
            diagnostics =
                "Side=" + team.Side +
                " ExistingType=" + existingTypeName +
                " DesiredType=" + desiredTypeName +
                " Changed=" + changed +
                " HasTeamAI=" + team.HasTeamAi;
            return team.HasTeamAi && activeTeamAi != null;
        }

        private static bool TryFinalizeMissionTeamAiForTeam(
            Mission mission,
            Team team,
            TeamAIComponent activeTeamAi,
            Mission.MissionTeamAITypeEnum missionTeamAiType,
            bool teamAiWasChanged,
            out string diagnostics)
        {
            diagnostics = "team-ai-null";
            if (mission == null || team == null || activeTeamAi == null)
                return false;

            if (!TryPrepareMissionTeamAiTactics(
                    team,
                    activeTeamAi,
                    missionTeamAiType,
                    out string tacticsDiagnostics))
            {
                diagnostics =
                    "Side=" + team.Side +
                    " Tactics={" + tacticsDiagnostics + "}";
                return false;
            }

            if (!TryNotifyMissionTeamAiDeploymentFinishedIfNeeded(
                    mission,
                    team,
                    activeTeamAi,
                    missionTeamAiType,
                    teamAiWasChanged,
                    out string deploymentFinishedNotificationDiagnostics))
            {
                diagnostics =
                    "Side=" + team.Side +
                    " Tactics={" + tacticsDiagnostics + "}" +
                    " DeploymentFinishedNotification={" + deploymentFinishedNotificationDiagnostics + "}";
                return false;
            }

            team.QuerySystem?.Expire();
            team.ResetTactic();

            diagnostics =
                "Side=" + team.Side +
                " Tactics={" + tacticsDiagnostics + "}" +
                " DeploymentFinishedNotification={" + deploymentFinishedNotificationDiagnostics + "}" +
                " HasTeamAI=" + team.HasTeamAi;
            return team.HasTeamAi;
        }

        private static Type ResolveMissionTeamAiImplementationType(
            Team team,
            Mission.MissionTeamAITypeEnum missionTeamAiType)
        {
            if (team == null)
                return null;

            switch (missionTeamAiType)
            {
                case Mission.MissionTeamAITypeEnum.Siege:
                    return team.Side == BattleSideEnum.Attacker
                        ? typeof(TeamAISiegeAttacker)
                        : team.Side == BattleSideEnum.Defender
                            ? typeof(TeamAISiegeDefender)
                            : null;

                case Mission.MissionTeamAITypeEnum.SallyOut:
                    return team.Side == BattleSideEnum.Attacker
                        ? typeof(TeamAISallyOutDefender)
                        : team.Side == BattleSideEnum.Defender
                            ? typeof(TeamAISallyOutAttacker)
                            : null;

                case Mission.MissionTeamAITypeEnum.FieldBattle:
                    return typeof(TeamAIGeneral);

                default:
                    return null;
            }
        }

        private static bool TryNotifyMissionTeamAiDeploymentFinishedIfNeeded(
            Mission mission,
            Team team,
            TeamAIComponent teamAi,
            Mission.MissionTeamAITypeEnum missionTeamAiType,
            bool teamAiWasChanged,
            out string diagnostics)
        {
            diagnostics = "not-required";
            if (mission == null || team == null || teamAi == null)
            {
                diagnostics = "not-required-null-context";
                return true;
            }

            if (missionTeamAiType != Mission.MissionTeamAITypeEnum.Siege)
            {
                diagnostics = "not-required Type=" + missionTeamAiType;
                return true;
            }

            if (!teamAiWasChanged)
            {
                diagnostics = "not-required-existing-team-ai";
                return true;
            }

            bool deploymentFinished;
            try
            {
                deploymentFinished = mission.IsDeploymentFinished;
            }
            catch (Exception ex)
            {
                diagnostics =
                    "deployment-state-unavailable " +
                    ex.GetType().Name + ":" + ex.Message;
                return true;
            }

            if (!deploymentFinished)
            {
                diagnostics = "not-required-deployment-open";
                return true;
            }

            if (TeamAiDeploymentFinishedNotifications.Contains(teamAi))
            {
                diagnostics =
                    "already-notified Side=" + team.Side +
                    " Type=" + teamAi.GetType().Name;
                return true;
            }

            try
            {
                teamAi.OnDeploymentFinished();
                TeamAiDeploymentFinishedNotifications.Add(teamAi);
                diagnostics =
                    "notified Side=" + team.Side +
                    " Type=" + teamAi.GetType().Name;
                return true;
            }
            catch (Exception ex)
            {
                diagnostics =
                    "notify-failed Side=" + team.Side +
                    " Type=" + teamAi.GetType().Name +
                    " Error=" + ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        public static void TryRepairSiegeAssaultWithDeploymentTeamAiLifecycle(
            Mission mission,
            CoopBattlePhase currentPhase,
            string source)
        {
            if (mission == null ||
                !IsSiegeAssaultWithDeploymentScenario(mission) ||
                currentPhase < CoopBattlePhase.BattleActive ||
                currentPhase >= CoopBattlePhase.BattleEnded)
            {
                return;
            }

            if (!(mission.AttackerTeam?.TeamAI is TeamAISiegeAttacker) ||
                !(mission.DefenderTeam?.TeamAI is TeamAISiegeDefender))
            {
                return;
            }

            bool lanesReadBefore = TryGetSiegeLaneQuerySystemReadiness(
                out bool lanesReadyBefore,
                out int laneCountBefore,
                out int missingQuerySystemCountBefore,
                out string laneDiagnosticsBefore);
            bool laneQuerySystemNeedsRepair = lanesReadBefore && !lanesReadyBefore;
            bool tacticsAlreadyRebuilt = SiegeAssaultTeamAiLifecycleTacticRepairs.Contains(mission);
            bool attackerDeploymentFinishedNotificationNeeded =
                !TeamAiDeploymentFinishedNotifications.Contains(mission.AttackerTeam.TeamAI);
            bool defenderDeploymentFinishedNotificationNeeded =
                !TeamAiDeploymentFinishedNotifications.Contains(mission.DefenderTeam.TeamAI);
            bool deploymentFinishedNotificationNeeded =
                attackerDeploymentFinishedNotificationNeeded ||
                defenderDeploymentFinishedNotificationNeeded;

            if (tacticsAlreadyRebuilt &&
                !laneQuerySystemNeedsRepair &&
                !deploymentFinishedNotificationNeeded)
            {
                return;
            }

            bool attackerNotified = true;
            bool defenderNotified = true;
            string attackerNotificationDiagnostics = "not-required-already-notified";
            string defenderNotificationDiagnostics = "not-required-already-notified";
            if (deploymentFinishedNotificationNeeded || laneQuerySystemNeedsRepair)
            {
                attackerNotified = TryNotifyExistingMissionTeamAiDeploymentFinished(
                    mission.AttackerTeam,
                    out attackerNotificationDiagnostics);
                defenderNotified = TryNotifyExistingMissionTeamAiDeploymentFinished(
                    mission.DefenderTeam,
                    out defenderNotificationDiagnostics);
            }

            bool attackerTacticsRebuilt = TryPrepareMissionTeamAiTactics(
                mission.AttackerTeam,
                mission.AttackerTeam.TeamAI,
                Mission.MissionTeamAITypeEnum.Siege,
                out string attackerTacticsDiagnostics);
            bool defenderTacticsRebuilt = TryPrepareMissionTeamAiTactics(
                mission.DefenderTeam,
                mission.DefenderTeam.TeamAI,
                Mission.MissionTeamAITypeEnum.Siege,
                out string defenderTacticsDiagnostics);
            if (attackerTacticsRebuilt)
            {
                mission.AttackerTeam.QuerySystem?.Expire();
                mission.AttackerTeam.ResetTactic();
            }

            if (defenderTacticsRebuilt)
            {
                mission.DefenderTeam.QuerySystem?.Expire();
                mission.DefenderTeam.ResetTactic();
            }

            SiegeAssaultTeamAiLifecycleTacticRepairs.Add(mission);

            CoopMissionNetworkBridge.ReapplyDelegatedFormationOwnership(
                mission,
                mission.AttackerTeam,
                "siege-team-ai-lifecycle-repair-attacker");
            CoopMissionNetworkBridge.ReapplyDelegatedFormationOwnership(
                mission,
                mission.DefenderTeam,
                "siege-team-ai-lifecycle-repair-defender");

            TryGetSiegeLaneQuerySystemReadiness(
                out bool lanesReadyAfter,
                out int laneCountAfter,
                out int missingQuerySystemCountAfter,
                out string laneDiagnosticsAfter);

            ModLogger.Info(
                "ExactCampaignArmyBootstrap: repaired siege assault deployment team AI lifecycle. " +
                "Scene=" + (mission.SceneName ?? "null") +
                " Phase=" + currentPhase +
                " TacticsAlreadyRebuilt=" + tacticsAlreadyRebuilt +
                " LaneQuerySystemNeedsRepair=" + laneQuerySystemNeedsRepair +
                " DeploymentFinishedNotificationNeeded=" + deploymentFinishedNotificationNeeded +
                " LanesBefore={Readable=" + lanesReadBefore +
                " Ready=" + lanesReadyBefore +
                " Count=" + laneCountBefore +
                " MissingQuerySystems=" + missingQuerySystemCountBefore +
                " Diagnostics=" + laneDiagnosticsBefore + "}" +
                " LanesAfter={Ready=" + lanesReadyAfter +
                " Count=" + laneCountAfter +
                " MissingQuerySystems=" + missingQuerySystemCountAfter +
                " Diagnostics=" + laneDiagnosticsAfter + "}" +
                " AttackerNotification={" + attackerNotificationDiagnostics + "}" +
                " DefenderNotification={" + defenderNotificationDiagnostics + "}" +
                " AttackerTactics={" + attackerTacticsDiagnostics + "}" +
                " DefenderTactics={" + defenderTacticsDiagnostics + "}" +
                " AttackerSuccess=" + attackerNotified +
                " DefenderSuccess=" + defenderNotified +
                " AttackerTacticsRebuilt=" + attackerTacticsRebuilt +
                " DefenderTacticsRebuilt=" + defenderTacticsRebuilt +
                " Source=" + (source ?? "unknown"));
        }

        private static bool TryNotifyExistingMissionTeamAiDeploymentFinished(
            Team team,
            out string diagnostics)
        {
            diagnostics = "team-null";
            if (team == null)
                return false;

            TeamAIComponent teamAi = team.TeamAI;
            if (teamAi == null)
            {
                diagnostics = "team-ai-null Side=" + team.Side;
                return false;
            }

            if (TeamAiDeploymentFinishedNotifications.Contains(teamAi))
            {
                diagnostics =
                    "already-notified Side=" + team.Side +
                    " Type=" + teamAi.GetType().Name;
                return true;
            }

            try
            {
                teamAi.OnDeploymentFinished();
                TeamAiDeploymentFinishedNotifications.Add(teamAi);
                diagnostics =
                    "notified Side=" + team.Side +
                    " Type=" + teamAi.GetType().Name;
                return true;
            }
            catch (Exception ex)
            {
                diagnostics =
                    "notify-failed Side=" + team.Side +
                    " Type=" + teamAi.GetType().Name +
                    " Error=" + ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        private static bool TryGetSiegeLaneQuerySystemReadiness(
            out bool lanesReady,
            out int laneCount,
            out int missingQuerySystemCount,
            out string diagnostics)
        {
            lanesReady = false;
            laneCount = 0;
            missingQuerySystemCount = 0;
            diagnostics = "reflection-unavailable";

            if (SiegeLaneQuerySystemField == null)
            {
                diagnostics = "siege-lane-query-system-field-missing";
                return false;
            }

            object siegeLanes = null;
            try
            {
                if (TeamAiSiegeComponentSiegeLanesProperty != null)
                    siegeLanes = TeamAiSiegeComponentSiegeLanesProperty.GetValue(null, null);
                else if (TeamAiSiegeComponentSiegeLanesField != null)
                    siegeLanes = TeamAiSiegeComponentSiegeLanesField.GetValue(null);
            }
            catch (Exception ex)
            {
                diagnostics =
                    "siege-lanes-read-failed " +
                    ex.GetType().Name + ":" + ex.Message;
                return false;
            }

            var enumerable = siegeLanes as System.Collections.IEnumerable;
            if (enumerable == null)
            {
                diagnostics = "siege-lanes-unavailable";
                return false;
            }

            foreach (object lane in enumerable)
            {
                if (lane == null)
                    continue;

                laneCount++;
                object querySystem = null;
                try
                {
                    querySystem = SiegeLaneQuerySystemField.GetValue(lane);
                }
                catch (Exception ex)
                {
                    diagnostics =
                        "siege-lane-query-system-read-failed " +
                        ex.GetType().Name + ":" + ex.Message;
                    return false;
                }

                if (querySystem == null)
                    missingQuerySystemCount++;
            }

            lanesReady = laneCount > 0 && missingQuerySystemCount == 0;
            diagnostics =
                "LaneCount=" + laneCount +
                " MissingQuerySystems=" + missingQuerySystemCount;
            return true;
        }

        private static TeamAIComponent CreateDesiredTeamAi(
            Mission mission,
            Team team,
            Mission.MissionTeamAITypeEnum missionTeamAiType)
        {
            if (mission == null || team == null)
                return null;

            switch (missionTeamAiType)
            {
                case Mission.MissionTeamAITypeEnum.Siege:
                    return team.Side == BattleSideEnum.Attacker
                        ? (TeamAIComponent)new TeamAISiegeAttacker(mission, team, 5f, 1f)
                        : new TeamAISiegeDefender(mission, team, 5f, 1f);

                case Mission.MissionTeamAITypeEnum.SallyOut:
                    return team.Side == BattleSideEnum.Attacker
                        ? (TeamAIComponent)new TeamAISallyOutDefender(mission, team, 5f, 1f)
                        : new TeamAISallyOutAttacker(mission, team, 5f, 1f);

                case Mission.MissionTeamAITypeEnum.FieldBattle:
                    return new TeamAIGeneral(mission, team);

                default:
                    return null;
            }
        }

        private static bool TryPrepareMissionTeamAiTactics(
            Team team,
            TeamAIComponent targetTeamAi,
            Mission.MissionTeamAITypeEnum missionTeamAiType,
            out string diagnostics)
        {
            diagnostics = "team-ai-null";
            if (team == null || targetTeamAi == null)
                return false;

            TeamAIComponent originalTeamAi = team.TeamAI;
            bool temporarilyAssignedTeamAi = false;
            try
            {
                if (!ReferenceEquals(originalTeamAi, targetTeamAi))
                {
                    if (TeamAiBackingField == null)
                    {
                        diagnostics = "team-ai-backing-field-missing";
                        return false;
                    }

                    TeamAiBackingField.SetValue(team, targetTeamAi);
                    temporarilyAssignedTeamAi = true;
                }

                targetTeamAi.ClearTacticOptions();
                AddMissionTeamAiTactics(targetTeamAi, team, missionTeamAiType);
                diagnostics =
                    "PreparedForType=" + missionTeamAiType +
                    " Side=" + team.Side +
                    " TemporaryAssign=" + temporarilyAssignedTeamAi;
                return true;
            }
            finally
            {
                if (temporarilyAssignedTeamAi && TeamAiBackingField != null)
                    TeamAiBackingField.SetValue(team, originalTeamAi);
            }
        }

        private static void AddMissionTeamAiTactics(
            TeamAIComponent teamAi,
            Team team,
            Mission.MissionTeamAITypeEnum missionTeamAiType)
        {
            if (team == null || teamAi == null)
                return;

            switch (missionTeamAiType)
            {
                case Mission.MissionTeamAITypeEnum.FieldBattle:
                    teamAi.AddTacticOption(new TacticCharge(team));
                    break;

                case Mission.MissionTeamAITypeEnum.Siege:
                    if (team.Side == BattleSideEnum.Attacker)
                        teamAi.AddTacticOption(new TacticBreachWalls(team));
                    else if (team.Side == BattleSideEnum.Defender)
                        teamAi.AddTacticOption(new TacticDefendCastle(team));
                    break;

                case Mission.MissionTeamAITypeEnum.SallyOut:
                    if (team.Side == BattleSideEnum.Defender)
                        teamAi.AddTacticOption(new TacticSallyOutHitAndRun(team));
                    if (team.Side == BattleSideEnum.Attacker)
                        teamAi.AddTacticOption(new TacticSallyOutDefense(team));
                    teamAi.AddTacticOption(new TacticCharge(team));
                    break;
            }
        }

        private static bool TryEnsureSiegeAmbushControllerInitialized(
            Mission mission,
            int defenderTotal,
            int attackerTotal,
            bool isSallyOutAmbush,
            out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            CoopExactCampaignSiegeAmbushMissionController controller =
                mission.GetMissionBehavior<CoopExactCampaignSiegeAmbushMissionController>();
            bool created = false;
            if (controller == null)
            {
                if (isSallyOutAmbush)
                {
                    diagnostics =
                        "siege-ambush-controller-missing-from-initial-stack";
                    return false;
                }

                controller = new CoopExactCampaignSiegeAmbushMissionController(
                    defenderTotal,
                    attackerTotal,
                    isSallyOutAmbush);
                mission.AddMissionBehavior(controller);
                created = true;
            }
            else if (controller.IsSallyOutAmbush != isSallyOutAmbush)
            {
                diagnostics =
                    "existing-controller-mode-mismatch Existing=" +
                    controller.IsSallyOutAmbush +
                    " Requested=" + isSallyOutAmbush;
                return false;
            }
            else
            {
                controller.UpdateTroopCounts(defenderTotal, attackerTotal);
            }

            controller.EnsureInitializedAndStarted();
            diagnostics =
                "Created=" + created +
                " Started=" + controller.HasStarted +
                " IsSallyOutAmbush=" + controller.IsSallyOutAmbush +
                " DefenderTotal=" + defenderTotal +
                " AttackerTotal=" + attackerTotal;
            return controller.HasStarted;
        }

        private static bool TryEnsureReliefControllerInitialized(
            Mission mission,
            int defenderTotal,
            int attackerTotal,
            out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            CoopExactCampaignReliefMissionController controller =
                mission.GetMissionBehavior<
                    CoopExactCampaignReliefMissionController>();
            bool created = false;
            if (controller == null)
            {
                controller =
                    new CoopExactCampaignReliefMissionController(
                        defenderTotal,
                        attackerTotal);
                mission.AddMissionBehavior(controller);
                created = true;
            }
            else
            {
                controller.UpdateTroopCounts(
                    defenderTotal,
                    attackerTotal);
            }

            controller.EnsureInitializedAndStarted();
            diagnostics =
                "Created=" + created +
                " Started=" + controller.HasStarted +
                " DefenderTotal=" + defenderTotal +
                " AttackerTotal=" + attackerTotal;
            return controller.HasStarted;
        }

        private static bool TryEnsureLordsHallControllerInitialized(
            Mission mission,
            IMissionTroopSupplier[] suppliers,
            BattleSideEnum playerSide,
            BattleScenarioContextMessage scenarioContext,
            int defenderTotal,
            int attackerTotal,
            out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            CoopExactCampaignLordsHallMissionController controller =
                mission.GetMissionBehavior<CoopExactCampaignLordsHallMissionController>();
            bool created = false;
            if (controller == null)
            {
                BattleSiegeContextMessage siegeContext = scenarioContext?.SiegeContext;
                float areaLostRatio = siegeContext?.LordsHallAreaLostRatio > 0f
                    ? siegeContext.LordsHallAreaLostRatio
                    : 3f;
                float attackerDefenderRatio = siegeContext?.LordsHallAttackerDefenderTroopCountRatio > 0f
                    ? siegeContext.LordsHallAttackerDefenderTroopCountRatio
                    : 0.7f;
                int maxAttackerCount = siegeContext?.LordsHallMaxAttackerSideTroopCount > 0
                    ? siegeContext.LordsHallMaxAttackerSideTroopCount
                    : 19;
                int maxDefenderCount = siegeContext?.LordsHallMaxDefenderSideTroopCount > 0
                    ? siegeContext.LordsHallMaxDefenderSideTroopCount
                    : 27;
                controller = new CoopExactCampaignLordsHallMissionController(
                    suppliers,
                    areaLostRatio,
                    attackerDefenderRatio,
                    Math.Min(maxAttackerCount, Math.Max(0, attackerTotal)),
                    Math.Min(maxDefenderCount, Math.Max(0, defenderTotal)),
                    playerSide);
                mission.AddMissionBehavior(controller);
                created = true;
            }

            controller.SetReinforcementsEnabled(false);
            controller.EnsureInitializedAndStarted();
            diagnostics =
                "Created=" + created +
                " Started=" + controller.HasStarted +
                " PlayerSide=" + playerSide +
                " DefenderTotal=" + defenderTotal +
                " AttackerTotal=" + attackerTotal;
            return controller.HasStarted;
        }

        private static void LogBootstrapContractSnapshot(
            Mission mission,
            NativeMissionAgentSpawnLogic spawnLogic,
            BattleSideEnum playerSide,
            string details,
            string stage,
            string source)
        {
            if (mission == null)
                return;

            string playerTeamText =
                mission.PlayerTeam == null
                    ? "null"
                    : mission.PlayerTeam.Side + "#" + mission.PlayerTeam.TeamIndex;
            string playerEnemyTeamText =
                mission.PlayerEnemyTeam == null
                    ? "null"
                    : mission.PlayerEnemyTeam.Side + "#" + mission.PlayerEnemyTeam.TeamIndex;

            ModLogger.Info(
                "ExactCampaignArmyBootstrap: contract snapshot. " +
                "Stage=" + stage +
                " Scene=" + (mission.SceneName ?? "null") +
                " PlayerSide=" + playerSide +
                " MissionMode=" + mission.Mode +
                " MissionTeamAIType=" + mission.MissionTeamAIType +
                " HasSpawnPath=" + mission.HasSpawnPath +
                " LiveHasSceneMapPatch=" + SafeHasSceneMapPatch(mission) +
                " ReflectedInitializerRecord={" + BuildReflectedInitializerRecordSummary(mission) + "}" +
                " PlayerTeam=" + playerTeamText +
                " PlayerEnemyTeam=" + playerEnemyTeamText +
                " BattleSize=" + NativeMissionAgentSpawnLogic.MaxNumberOfAgentsForMission +
                " Source=" + (source ?? "unknown") +
                " Details=" + (details ?? string.Empty));

            ModLogger.Info(
                "ExactCampaignArmyBootstrap: mission teams snapshot. " +
                "Stage=" + stage +
                " Teams=[" + BuildMissionTeamsSummary(mission) + "] " +
                " Source=" + (source ?? "unknown"));

            ModLogger.Info(
                "ExactCampaignArmyBootstrap: deployment plan snapshot. " +
                "Stage=" + stage +
                " " + BuildDeploymentPlanSummary(mission) +
                " Source=" + (source ?? "unknown"));

            if (spawnLogic != null)
            {
                ModLogger.Info(
                    "ExactCampaignArmyBootstrap: spawn logic snapshot. " +
                    "Stage=" + stage +
                    " " + BuildSpawnLogicSummary(spawnLogic) +
                    " Source=" + (source ?? "unknown"));
            }

            string behaviorSummary =
                mission.MissionBehaviors == null
                    ? "null"
                    : string.Join(", ", mission.MissionBehaviors.Select(behavior => behavior?.GetType().Name ?? "null"));
            ModLogger.Info(
                "ExactCampaignArmyBootstrap: mission behaviors snapshot. " +
                "Stage=" + stage +
                " Behaviors=[" + behaviorSummary + "] " +
                " Source=" + (source ?? "unknown"));
        }

        private static string BuildMissionTeamsSummary(Mission mission)
        {
            if (mission?.Teams == null)
                return "null";

            return string.Join(
                "; ",
                mission.Teams.Select(team =>
                {
                    if (team == null)
                        return "null-team";

                    return
                        "#" + team.TeamIndex +
                        " Side=" + team.Side +
                        " IsPlayerTeam=" + team.IsPlayerTeam +
                        " IsPlayerAlly=" + team.IsPlayerAlly +
                        " HasAI=" + (team.TeamAI != null) +
                        " Formations=" + (team.FormationsIncludingSpecialAndEmpty?.Count ?? -1) +
                        " ActiveAgents=" + (team.ActiveAgents?.Count ?? -1) +
                        " QueryReady=" + (team.QuerySystem != null);
                }));
        }

        private static string BuildDeploymentPlanSummary(Mission mission)
        {
            if (mission == null)
                return "Mission=null";

            if (!mission.GetDeploymentPlan<DefaultMissionDeploymentPlan>(out DefaultMissionDeploymentPlan deploymentPlan) ||
                deploymentPlan == null)
            {
                return "DeploymentPlan=null";
            }

            var builder = new StringBuilder();
            builder.Append("DeploymentPlanType=");
            builder.Append(deploymentPlan.GetType().Name);
            builder.Append(" TeamPlans=[");
            builder.Append(BuildTeamPlanCollectionSummary(deploymentPlan));
            builder.Append("]");

            Array spawnEntries =
                DefaultMissionDeploymentPlanFormationSceneSpawnEntriesField?.GetValue(deploymentPlan) as Array;
            builder.Append(" FormationSceneSpawnEntries=");
            builder.Append(spawnEntries == null ? "null" : spawnEntries.Length.ToString());

            builder.Append(" Boundaries=");
            builder.Append(mission.Boundaries == null ? "null" : mission.Boundaries.Count.ToString());
            return builder.ToString();
        }

        private static bool SafeHasSceneMapPatch(Mission mission)
        {
            try
            {
                return mission != null && mission.HasSceneMapPatch();
            }
            catch
            {
                return false;
            }
        }

        private static string BuildReflectedInitializerRecordSummary(Mission mission)
        {
            if (mission == null || MissionInitializerRecordProperty == null)
                return "unavailable";

            try
            {
                object record = MissionInitializerRecordProperty.GetValue(mission, null);
                if (record == null)
                    return "null";

                return
                    "SceneName=" + FormatMemberValue(TryReadMember(record, "SceneName")) +
                    " SceneLevels=" + FormatMemberValue(TryReadMember(record, "SceneLevels")) +
                    " PlayingInCampaignMode=" + FormatMemberValue(TryReadMember(record, "PlayingInCampaignMode")) +
                    " SceneHasMapPatch=" + FormatMemberValue(TryReadMember(record, "SceneHasMapPatch")) +
                    " PatchCoordinates=" + FormatMemberValue(TryReadMember(record, "PatchCoordinates")) +
                    " PatchEncounterDir=" + FormatMemberValue(TryReadMember(record, "PatchEncounterDir"));
            }
            catch (Exception ex)
            {
                return "reflection-failed:" + ex.Message;
            }
        }

        private static object TryReadMember(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
                return null;

            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = instance.GetType();

            PropertyInfo property = type.GetProperty(memberName, flags);
            if (property != null)
                return property.GetValue(instance, null);

            FieldInfo field = type.GetField(memberName, flags);
            if (field != null)
                return field.GetValue(instance);

            return null;
        }

        private static string FormatMemberValue(object value)
        {
            return value?.ToString() ?? "null";
        }

        private static string BuildTeamPlanCollectionSummary(DefaultMissionDeploymentPlan deploymentPlan)
        {
            object teamPlans = DefaultMissionDeploymentPlanTeamDeploymentPlansField?.GetValue(deploymentPlan);
            if (teamPlans == null)
                return "null";

            var enumerable = teamPlans as System.Collections.IEnumerable;
            if (enumerable == null)
                return "not-enumerable";

            var entries = new List<string>();
            foreach (object entry in enumerable)
            {
                if (entry == null)
                {
                    entries.Add("null");
                    continue;
                }

                Type entryType = entry.GetType();
                Team team = TryReadMember(entry, "team") as Team ?? TryReadMember(entry, "Item1") as Team;
                object plan = TryReadMember(entry, "plan") ?? TryReadMember(entry, "Item2");
                entries.Add(
                    "Team=" + (team == null ? "null" : "#" + team.TeamIndex + "/" + team.Side) +
                    " Plan=" + (plan == null ? "null" : plan.GetType().Name));
            }

            return string.Join(", ", entries);
        }

        private static bool TryEnsureDeploymentPlanTeamPlans(
            Mission mission,
            string source,
            out string diagnostics)
        {
            diagnostics = string.Empty;
            if (mission == null)
            {
                diagnostics = "mission-null";
                return false;
            }

            if (!mission.GetDeploymentPlan<DefaultMissionDeploymentPlan>(out DefaultMissionDeploymentPlan deploymentPlan) ||
                deploymentPlan == null)
            {
                diagnostics = "deployment-plan-null";
                return false;
            }

            if (DefaultMissionDeploymentPlanTeamDeploymentPlansField == null)
            {
                diagnostics = "team-deployment-plan-field-missing";
                return false;
            }

            object teamPlans = DefaultMissionDeploymentPlanTeamDeploymentPlansField.GetValue(deploymentPlan);
            if (!(teamPlans is System.Collections.IEnumerable enumerable))
            {
                diagnostics = "team-deployment-plans-not-enumerable";
                return false;
            }

            var currentTeams = enumerable.Cast<object>().ToList();
            var existingTeams = new HashSet<Team>();
            foreach (object entry in currentTeams)
            {
                Team existingTeam = TryReadMember(entry, "team") as Team ?? TryReadMember(entry, "Item1") as Team;
                if (existingTeam != null)
                    existingTeams.Add(existingTeam);
            }

            List<Team> battleTeams = mission.Teams?
                .Where(team => team != null &&
                               team.Side != BattleSideEnum.None &&
                               !IsSpawnLogicInitTemporaryNonBattleTeam(mission, team))
                .ToList() ?? new List<Team>();

            List<Team> missingTeams = battleTeams
                .Where(team => !existingTeams.Contains(team))
                .ToList();

            if (missingTeams.Count <= 0)
            {
                diagnostics =
                    "already-ready Existing=" + existingTeams.Count +
                    " BattleTeams=" + battleTeams.Count;
                return true;
            }

            MethodInfo addMethod = teamPlans.GetType().GetMethod("Add", BindingFlags.Instance | BindingFlags.Public);
            if (addMethod == null)
            {
                diagnostics = "team-deployment-plan-add-method-missing";
                return false;
            }

            foreach (Team missingTeam in missingTeams)
            {
                addMethod.Invoke(
                    teamPlans,
                    new object[] { (missingTeam, new DefaultTeamDeploymentPlan(mission, missingTeam)) });
            }

            diagnostics =
                "added-missing-team-plans MissingTeams=[" +
                string.Join(", ", missingTeams.Select(team => "#" + team.TeamIndex + "/" + team.Side)) +
                "] ExistingBefore=" + existingTeams.Count +
                " ExistingAfter=" + (existingTeams.Count + missingTeams.Count);

            ModLogger.Info(
                "ExactCampaignArmyBootstrap: ensured deployment plan team plans before native bootstrap init. " +
                "Scene=" + (mission.SceneName ?? "null") +
                " " + diagnostics +
                " Source=" + (source ?? "unknown"));
            return true;
        }

        private static string BuildSpawnLogicSummary(NativeMissionAgentSpawnLogic spawnLogic)
        {
            if (spawnLogic == null)
                return "SpawnLogic=null";

            var builder = new StringBuilder();
            builder.Append("SpawnLogicType=");
            builder.Append(spawnLogic.GetType().Name);

            builder.Append(" NativePlayerSide=");
            builder.Append(spawnLogic.PlayerSide);

            object deploymentPlan = MissionAgentSpawnLogicDeploymentPlanField?.GetValue(spawnLogic);
            builder.Append(" NativeDeploymentPlan=");
            builder.Append(deploymentPlan == null ? "null" : deploymentPlan.GetType().Name);

            int[] troopTotals = MissionAgentSpawnLogicNumberOfTroopsInTotalField?.GetValue(spawnLogic) as int[];
            builder.Append(" TroopTotals=");
            builder.Append(
                troopTotals == null
                    ? "null"
                    : "[" + string.Join(", ", troopTotals.Select((value, index) => ((BattleSideEnum)index) + "=" + value)) + "]");

            Array phases = MissionAgentSpawnLogicPhasesField?.GetValue(spawnLogic) as Array;
            builder.Append(" PhaseCounts=");
            if (phases == null)
            {
                builder.Append("null");
            }
            else
            {
                var phaseEntries = new List<string>();
                for (int i = 0; i < phases.Length; i++)
                {
                    object phaseList = phases.GetValue(i);
                    int count = (int)(phaseList?.GetType().GetProperty("Count")?.GetValue(phaseList) ?? -1);
                    phaseEntries.Add(((BattleSideEnum)i) + "=" + count);
                }
                builder.Append("[" + string.Join(", ", phaseEntries) + "]");
            }

            Array missionSides = MissionAgentSpawnLogicMissionSidesField?.GetValue(spawnLogic) as Array;
            builder.Append(" MissionSides=");
            if (missionSides == null)
            {
                builder.Append("null");
            }
            else
            {
                var sideEntries = new List<string>();
                for (int i = 0; i < missionSides.Length; i++)
                {
                    object sideState = missionSides.GetValue(i);
                    sideEntries.Add(((BattleSideEnum)i) + "=" + (sideState == null ? "null" : sideState.GetType().Name));
                }
                builder.Append("[" + string.Join(", ", sideEntries) + "]");
            }

            return builder.ToString();
        }

        private static string BuildDetailedRuntimeSummary(NativeMissionAgentSpawnLogic spawnLogic)
        {
            if (spawnLogic == null)
                return "SpawnLogic=null";

            var builder = new StringBuilder();
            builder.Append("BattleSize=").Append(spawnLogic.BattleSize);
            builder.Append(" NumberOfAgents=").Append(spawnLogic.NumberOfAgents);
            builder.Append(" RemainingTroops=").Append(spawnLogic.NumberOfRemainingTroops);
            builder.Append(" ActiveTroops=[Defender=").Append(spawnLogic.NumberOfActiveDefenderTroops);
            builder.Append(", Attacker=").Append(spawnLogic.NumberOfActiveAttackerTroops).Append("]");
            builder.Append(" RemovedBySide=[Defender=").Append(GetMissionSideSupplierPropertyValue<int>(spawnLogic, BattleSideEnum.Defender, "NumRemovedTroops"));
            builder.Append(", Attacker=").Append(GetMissionSideSupplierPropertyValue<int>(spawnLogic, BattleSideEnum.Attacker, "NumRemovedTroops")).Append("]");
            builder.Append(" RemainingBySide=[Defender=").Append(spawnLogic.NumberOfRemainingDefenderTroops);
            builder.Append(", Attacker=").Append(spawnLogic.NumberOfRemainingAttackerTroops).Append("]");
            builder.Append(" UnsuppliedBySide=[Defender=").Append(GetMissionSideSupplierPropertyValue<int>(spawnLogic, BattleSideEnum.Defender, "NumTroopsNotSupplied"));
            builder.Append(", Attacker=").Append(GetMissionSideSupplierPropertyValue<int>(spawnLogic, BattleSideEnum.Attacker, "NumTroopsNotSupplied")).Append("]");
            builder.Append(" IsSideDepleted=[Defender=").Append(SafeIsSideDepleted(spawnLogic, BattleSideEnum.Defender));
            builder.Append(", Attacker=").Append(SafeIsSideDepleted(spawnLogic, BattleSideEnum.Attacker)).Append("]");
            builder.Append(" PhaseState=[");
            builder.Append(BuildPhaseRuntimeSummary(spawnLogic, BattleSideEnum.Defender));
            builder.Append("; ");
            builder.Append(BuildPhaseRuntimeSummary(spawnLogic, BattleSideEnum.Attacker));
            builder.Append("]");
            builder.Append(" MissionSideState=[");
            builder.Append(BuildMissionSideRuntimeSummary(spawnLogic, BattleSideEnum.Defender));
            builder.Append("; ");
            builder.Append(BuildMissionSideRuntimeSummary(spawnLogic, BattleSideEnum.Attacker));
            builder.Append("]");
            return builder.ToString();
        }

        private static string BuildPhaseRuntimeSummary(NativeMissionAgentSpawnLogic spawnLogic, BattleSideEnum side)
        {
            object phase = GetActivePhaseObject(spawnLogic, side);
            if (phase == null)
                return side + "=null";

            return side +
                   "{Total=" + GetIntFieldValue(phase, "TotalSpawnNumber") +
                   ",InitialPending=" + GetIntFieldValue(phase, "InitialSpawnNumber") +
                   ",InitialSpawned=" + GetIntFieldValue(phase, "InitialSpawnedNumber") +
                   ",Remaining=" + GetIntFieldValue(phase, "RemainingSpawnNumber") +
                   ",Active=" + GetIntFieldValue(phase, "NumberActiveTroops") +
                   "}";
        }

        private static string BuildMissionSideRuntimeSummary(NativeMissionAgentSpawnLogic spawnLogic, BattleSideEnum side)
        {
            object missionSide = GetMissionSideObject(spawnLogic, side);
            if (missionSide == null)
                return side + "=null";

            return side +
                   "{SpawnActive=" + GetPropertyValue<bool>(missionSide, "TroopSpawnActive") +
                   ",ReinforcementActive=" + GetPropertyValue<bool>(missionSide, "ReinforcementSpawnActive") +
                   ",HasSpawnable=" + GetPropertyValue<bool>(missionSide, "HasSpawnableReinforcements") +
                   ",HasReserved=" + GetPropertyValue<bool>(missionSide, "HasReservedTroops") +
                   ",Reserved=" + GetPropertyValue<int>(missionSide, "ReservedTroopsCount") +
                   ",BatchSize=" + GetPropertyValue<float>(missionSide, "ReinforcementBatchSize").ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                   ",SpawnedLastBatch=" + GetPropertyValue<int>(missionSide, "ReinforcementsSpawnedInLastBatch") +
                   ",Quota=" + GetPropertyValue<int>(missionSide, "ReinforcementQuotaRequirement") +
                   ",Priority=" + GetPropertyValue<float>(missionSide, "ReinforcementBatchPriority").ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                   ",ActiveTroops=" + GetPropertyValue<int>(missionSide, "NumberOfActiveTroops") +
                   "}";
        }

        private static object GetActivePhaseObject(NativeMissionAgentSpawnLogic spawnLogic, BattleSideEnum side)
        {
            if (spawnLogic == null || MissionAgentSpawnLogicPhasesField == null)
                return null;

            Array phases = MissionAgentSpawnLogicPhasesField.GetValue(spawnLogic) as Array;
            int sideIndex = (int)side;
            if (phases == null || sideIndex < 0 || sideIndex >= phases.Length)
                return null;

            object phaseList = phases.GetValue(sideIndex);
            object countValue = phaseList?.GetType().GetProperty("Count")?.GetValue(phaseList);
            if (!(countValue is int count) || count <= 0)
                return null;

            MethodInfo indexer = phaseList.GetType().GetMethod("get_Item", BindingFlags.Instance | BindingFlags.Public);
            return indexer?.Invoke(phaseList, new object[] { 0 });
        }

        private static object GetMissionSideObject(NativeMissionAgentSpawnLogic spawnLogic, BattleSideEnum side)
        {
            if (spawnLogic == null || MissionAgentSpawnLogicMissionSidesField == null)
                return null;

            Array missionSides = MissionAgentSpawnLogicMissionSidesField.GetValue(spawnLogic) as Array;
            int sideIndex = (int)side;
            if (missionSides == null || sideIndex < 0 || sideIndex >= missionSides.Length)
                return null;

            return missionSides.GetValue(sideIndex);
        }

        private static T GetMissionSideSupplierPropertyValue<T>(
            NativeMissionAgentSpawnLogic spawnLogic,
            BattleSideEnum side,
            string propertyName)
        {
            object missionSide = GetMissionSideObject(spawnLogic, side);
            if (missionSide == null || MissionSideTroopSupplierField == null)
                return default(T);

            object supplier = MissionSideTroopSupplierField.GetValue(missionSide);
            return GetPropertyValue<T>(supplier, propertyName);
        }

        private static int GetIntFieldValue(object instance, string fieldName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(fieldName))
                return 0;

            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object value = field?.GetValue(instance);
            return value is int intValue ? intValue : 0;
        }

        private static T GetPropertyValue<T>(object instance, string propertyName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(propertyName))
                return default;

            PropertyInfo property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object value = property?.GetValue(instance);
            if (value is T typedValue)
                return typedValue;

            return default;
        }

        private static bool SafeIsSideDepleted(NativeMissionAgentSpawnLogic spawnLogic, BattleSideEnum side)
        {
            try
            {
                return spawnLogic?.IsSideDepleted(side) == true;
            }
            catch
            {
                return false;
            }
        }

        private static void OnNativeReinforcementsSpawned(BattleSideEnum side, int spawnedCount)
        {
            Mission mission = _activeMission;
            if (!UsesSpawnLogicRuntimeMode(_activeMode) || _activeSpawnLogic == null || mission == null)
                return;

            if (ExperimentalFeatures.EnableExactCampaignArmyRuntimeDiagnostics)
            {
                ModLogger.Info(
                    "ExactCampaignArmyBootstrap: native reinforcement batch spawned. " +
                    "Scene=" + (mission.SceneName ?? "null") +
                    " Side=" + side +
                    " SpawnedCount=" + spawnedCount +
                    " PlayerSide=" + _activePlayerSide);
                TryLogRuntimeDiagnostics(mission, "native-reinforcements-spawned", force: true);
            }
        }

        private static void OnMissionBeforeAgentRemoved(
            Agent affectedAgent,
            Agent affectorAgent,
            AgentState agentState,
            KillingBlow killingBlow)
        {
            Mission mission = affectedAgent?.Mission ?? affectorAgent?.Mission ?? _activeMission;
            bool exactSiegeClientTrackedAgentHandled =
                affectedAgent != null &&
                CoopSpectator.Patches.BattleMapSpawnHandoffPatch.TryReleaseTrackedExactSiegeClientAgentBeforeRemoval(
                    mission,
                    affectedAgent,
                    "mission-onbefore-agent-removed",
                    out _);

            if (!exactSiegeClientTrackedAgentHandled &&
                IsSiegeAssaultWithDeploymentActive(mission) &&
                affectedAgent != null &&
                CoopSiegeMachineDeploymentController.TryReleaseAgentFromSiegeMachineBeforeRemoval(
                    mission,
                    affectedAgent,
                    "mission-onbefore-agent-removed",
                    out string siegeMachineReleaseDiagnostics))
            {
                ModLogger.Info(
                    "ExactCampaignArmyBootstrap: processed removed agent siege machine cleanup before native removal. " +
                    "Scene=" + (mission?.SceneName ?? "null") +
                    " " + siegeMachineReleaseDiagnostics);
            }

            TrySyncAgentOriginRemoval(
                mission,
                affectedAgent,
                affectorAgent,
                agentState,
                "mission-onbefore-agent-removed");
        }

        public static void TrySyncAgentOriginRemoval(
            Mission mission,
            Agent affectedAgent,
            Agent affectorAgent,
            AgentState agentState,
            string source = null)
        {
            if (!IsActive(mission) ||
                affectedAgent == null ||
                affectedAgent.IsMount ||
                !(affectedAgent.Origin is ExactCampaignSnapshotAgentOrigin exactOrigin))
            {
                return;
            }

            bool diagnosticsEnabled = ExperimentalFeatures.EnableExactCampaignArmyRuntimeDiagnostics;
            int defenderRemovedBefore = diagnosticsEnabled
                ? GetActiveRemovedTroopCount(BattleSideEnum.Defender)
                : 0;
            int attackerRemovedBefore = diagnosticsEnabled
                ? GetActiveRemovedTroopCount(BattleSideEnum.Attacker)
                : 0;

            switch (agentState)
            {
                case AgentState.Unconscious:
                    affectedAgent.Origin.SetWounded();
                    break;
                case AgentState.Killed:
                    affectedAgent.Origin.SetKilled();
                    break;
                default:
                    affectedAgent.Origin.SetRouted(isOrderRetreat: false);
                    break;
            }

            if (!diagnosticsEnabled)
                return;

            int defenderRemovedAfter = GetActiveRemovedTroopCount(BattleSideEnum.Defender);
            int attackerRemovedAfter = GetActiveRemovedTroopCount(BattleSideEnum.Attacker);
            if (defenderRemovedBefore == defenderRemovedAfter &&
                attackerRemovedBefore == attackerRemovedAfter)
            {
                return;
            }

            ModLogger.Info(
                "ExactCampaignArmyBootstrap: synced exact origin removal. " +
                "Scene=" + (mission?.SceneName ?? "null") +
                " Source=" + (source ?? "unknown") +
                " Side=" + exactOrigin.Side +
                " AgentState=" + agentState +
                " AgentIndex=" + affectedAgent.Index +
                " EntryId=" + exactOrigin.EntryId +
                " TroopId=" + exactOrigin.TroopId +
                " RemovedBySideBefore=[Defender=" + defenderRemovedBefore +
                ",Attacker=" + attackerRemovedBefore + "]" +
                " RemovedBySideAfter=[Defender=" + defenderRemovedAfter +
                ",Attacker=" + attackerRemovedAfter + "]" +
                " ActiveTroopsAfter=[Defender=" + CountActiveMissionSideAgents(mission, BattleSideEnum.Defender) +
                ",Attacker=" + CountActiveMissionSideAgents(mission, BattleSideEnum.Attacker) + "]" +
                " PlayerSide=" + _activePlayerSide);
            TryLogRuntimeDiagnostics(
                mission,
                (source ?? "unknown") + " exact-origin-removal",
                force: true);
        }

        public static void TrySyncReinforcementState(Mission mission, bool enabled, string source)
        {
            if (!IsActive(mission) || _reinforcementsEnabled == enabled)
                return;

            if (UsesSpawnLogicRuntimeMode(_activeMode))
            {
                _activeSpawnLogic?.SetReinforcementsSpawnEnabled(enabled);
            }
            else if (_activeMode == ActiveBootstrapMode.LordsHallController)
            {
                mission?.GetMissionBehavior<CoopExactCampaignLordsHallMissionController>()?.SetReinforcementsEnabled(enabled);
            }

            _reinforcementsEnabled = enabled;
            ModLogger.Info(
                "ExactCampaignArmyBootstrap: reinforcement gate updated. " +
                "Scene=" + (mission?.SceneName ?? "null") +
                " Enabled=" + enabled +
                " Mode=" + _activeMode +
                " PlayerSide=" + _activePlayerSide +
                " Source=" + (source ?? "unknown"));
            TryLogRuntimeDiagnostics(mission, source + " gate-change", force: true);
        }

        public static bool TryStartNativeInitialSpawners(
            Mission mission,
            string source,
            out string diagnostics)
        {
            diagnostics = "bootstrap-inactive";
            if (!IsActive(mission) ||
                _activeSpawnLogic == null ||
                _activeMode != ActiveBootstrapMode.SiegeAssaultWithDeployment)
            {
                return false;
            }

            try
            {
                if (_activeSpawnLogic.IsInitialSpawnOver)
                {
                    _nativeInitialSpawnersStartedMission = mission;
                    diagnostics =
                        "already-complete ActiveDefender=" + _activeSpawnLogic.NumberOfActiveDefenderTroops +
                        " ActiveAttacker=" + _activeSpawnLogic.NumberOfActiveAttackerTroops;
                    return true;
                }

                if (ReferenceEquals(_nativeInitialSpawnersStartedMission, mission))
                {
                    diagnostics =
                        "already-started DefenderEnabled=" + _activeSpawnLogic.IsSideSpawnEnabled(BattleSideEnum.Defender) +
                        " AttackerEnabled=" + _activeSpawnLogic.IsSideSpawnEnabled(BattleSideEnum.Attacker);
                    return true;
                }

                _activeSpawnLogic.StartSpawner(BattleSideEnum.Defender);
                _activeSpawnLogic.StartSpawner(BattleSideEnum.Attacker);
                _nativeInitialSpawnersStartedMission = mission;
                diagnostics = "started-both-sides";
                ModLogger.Info(
                    "ExactCampaignArmyBootstrap: started native initial army spawners. " +
                    "Scene=" + (mission.SceneName ?? "null") +
                    " Mode=" + _activeMode +
                    " PlayerSide=" + _activePlayerSide +
                    " Source=" + (source ?? "unknown"));
                return true;
            }
            catch (Exception ex)
            {
                diagnostics = "start-failed:" + ex.GetType().Name + ":" + ex.Message;
                ModLogger.Info(
                    "ExactCampaignArmyBootstrap: failed to start native initial army spawners. " +
                    "Scene=" + (mission?.SceneName ?? "null") +
                    " Mode=" + _activeMode +
                    " PlayerSide=" + _activePlayerSide +
                    " Source=" + (source ?? "unknown") +
                    " Error=" + ex.GetType().Name + ":" + ex.Message);
                return false;
            }
        }

        public static void TryStopNativeReinforcementSpawnersAtBattleEnd(
            Mission mission,
            string source)
        {
            if (!IsActive(mission) ||
                !ReferenceEquals(_activeMission, mission) ||
                !UsesSpawnLogicRuntimeMode(_activeMode) ||
                _activeSpawnLogic == null)
            {
                return;
            }

            bool reinforcementGateWasEnabled = _reinforcementsEnabled;
            bool defenderSpawnerWasEnabled = false;
            bool attackerSpawnerWasEnabled = false;
            try
            {
                defenderSpawnerWasEnabled =
                    _activeSpawnLogic.IsSideSpawnEnabled(BattleSideEnum.Defender);
            }
            catch
            {
            }

            try
            {
                attackerSpawnerWasEnabled =
                    _activeSpawnLogic.IsSideSpawnEnabled(BattleSideEnum.Attacker);
            }
            catch
            {
            }

            if (!reinforcementGateWasEnabled &&
                !defenderSpawnerWasEnabled &&
                !attackerSpawnerWasEnabled)
            {
                return;
            }

            try
            {
                _activeSpawnLogic.SetReinforcementsSpawnEnabled(false);
                _activeSpawnLogic.StopSpawner(BattleSideEnum.Defender);
                _activeSpawnLogic.StopSpawner(BattleSideEnum.Attacker);
                _reinforcementsEnabled = false;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "ExactCampaignArmyBootstrap: failed to stop native reinforcement spawners at battle end. " +
                    "Scene=" + (mission.SceneName ?? "null") +
                    " Mode=" + _activeMode +
                    " PlayerSide=" + _activePlayerSide +
                    " Source=" + (source ?? "unknown") +
                    " Error=" + ex.GetType().Name + ":" + ex.Message);
                return;
            }

            ModLogger.Info(
                "ExactCampaignArmyBootstrap: stopped native reinforcement spawners at battle end. " +
                "Scene=" + (mission.SceneName ?? "null") +
                " ReinforcementGateWasEnabled=" + reinforcementGateWasEnabled +
                " DefenderSpawnerWasEnabled=" + defenderSpawnerWasEnabled +
                " AttackerSpawnerWasEnabled=" + attackerSpawnerWasEnabled +
                " Mode=" + _activeMode +
                " PlayerSide=" + _activePlayerSide +
                " Source=" + (source ?? "unknown"));
        }

        public static void TrySuppressNativeReinforcementSpawnersForMaterializedSiege(Mission mission, string source)
        {
            if (!IsActive(mission) ||
                !ReferenceEquals(_activeMission, mission) ||
                _activeMode != ActiveBootstrapMode.SiegeAssaultWithDeployment ||
                _activeSpawnLogic == null)
            {
                return;
            }

            bool defenderWasEnabled = false;
            bool attackerWasEnabled = false;
            try
            {
                defenderWasEnabled = _activeSpawnLogic.IsSideSpawnEnabled(BattleSideEnum.Defender);
            }
            catch
            {
            }

            try
            {
                attackerWasEnabled = _activeSpawnLogic.IsSideSpawnEnabled(BattleSideEnum.Attacker);
            }
            catch
            {
            }

            try
            {
                _activeSpawnLogic.SetReinforcementsSpawnEnabled(false);
                _activeSpawnLogic.StopSpawner(BattleSideEnum.Defender);
                _activeSpawnLogic.StopSpawner(BattleSideEnum.Attacker);
                _reinforcementsEnabled = false;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "ExactCampaignArmyBootstrap: failed to suppress native siege reinforcement spawners. " +
                    "Scene=" + (mission.SceneName ?? "null") +
                    " Mode=" + _activeMode +
                    " PlayerSide=" + _activePlayerSide +
                    " Source=" + (source ?? "unknown") +
                    " Error=" + ex.GetType().Name + ":" + ex.Message);
                return;
            }

            if (!defenderWasEnabled && !attackerWasEnabled)
                return;

            ModLogger.Info(
                "ExactCampaignArmyBootstrap: suppressed native siege reinforcement spawners for materialized siege runtime. " +
                "Scene=" + (mission.SceneName ?? "null") +
                " DefenderWasEnabled=" + defenderWasEnabled +
                " AttackerWasEnabled=" + attackerWasEnabled +
                " Mode=" + _activeMode +
                " PlayerSide=" + _activePlayerSide +
                " Source=" + (source ?? "unknown"));
        }

        public static void TryMaintainMissionPlayerTeamContract(Mission mission, string source)
        {
            if (!IsActive(mission))
                return;

            if (ReferenceEquals(mission.PlayerTeam, _activePlayerTeam) &&
                ReferenceEquals(mission.PlayerEnemyTeam, _activePlayerEnemyTeam))
            {
                return;
            }

            Team previousPlayerTeam = mission.PlayerTeam;
            Team previousPlayerEnemyTeam = mission.PlayerEnemyTeam;
            MissionMultiplayerCoopBattle.TryRefreshMissionPlayerTeamRelationView(
                mission,
                _activePlayerTeam,
                source + " restore",
                out _);

            string previousPlayerTeamText =
                previousPlayerTeam == null
                    ? "null"
                    : previousPlayerTeam.Side + "#" + previousPlayerTeam.TeamIndex;
            string previousPlayerEnemyTeamText =
                previousPlayerEnemyTeam == null
                    ? "null"
                    : previousPlayerEnemyTeam.Side + "#" + previousPlayerEnemyTeam.TeamIndex;
            string appliedPlayerTeamText =
                mission.PlayerTeam == null
                    ? "null"
                    : mission.PlayerTeam.Side + "#" + mission.PlayerTeam.TeamIndex;
            string appliedPlayerEnemyTeamText =
                mission.PlayerEnemyTeam == null
                    ? "null"
                    : mission.PlayerEnemyTeam.Side + "#" + mission.PlayerEnemyTeam.TeamIndex;

            ModLogger.Info(
                "ExactCampaignArmyBootstrap: restored native player team contract after runtime drift. " +
                "Scene=" + (mission.SceneName ?? "null") +
                " ActivePlayerSide=" + _activePlayerSide +
                " PreviousPlayerTeam=" + previousPlayerTeamText +
                " PreviousPlayerEnemyTeam=" + previousPlayerEnemyTeamText +
                " AppliedPlayerTeam=" + appliedPlayerTeamText +
                " AppliedPlayerEnemyTeam=" + appliedPlayerEnemyTeamText +
                " Source=" + (source ?? "unknown"));
        }

        public static bool TryGetRemainingTroopCounts(
            Mission mission,
            out int attackerRemaining,
            out int defenderRemaining)
        {
            attackerRemaining = 0;
            defenderRemaining = 0;
            if (!IsActive(mission))
                return false;

            if (UsesSpawnLogicRuntimeMode(_activeMode) && _activeSpawnLogic != null)
            {
                attackerRemaining = Math.Max(0, _activeSpawnLogic.NumberOfRemainingAttackerTroops);
                defenderRemaining = Math.Max(0, _activeSpawnLogic.NumberOfRemainingDefenderTroops);
                return true;
            }

            if (_activeMode == ActiveBootstrapMode.LordsHallController)
            {
                attackerRemaining = GetActiveRemainingTroopCount(BattleSideEnum.Attacker);
                defenderRemaining = GetActiveRemainingTroopCount(BattleSideEnum.Defender);
                return true;
            }

            return false;
        }

        public static void TryLogRuntimeDiagnostics(Mission mission, string source, bool force = false)
        {
            if (!ExperimentalFeatures.EnableExactCampaignArmyRuntimeDiagnostics || !IsActive(mission))
                return;

            DateTime nowUtc = DateTime.UtcNow;
            if (!force && nowUtc < _nextRuntimeDiagnosticsLogUtc)
                return;

            string summary;
            if (UsesSpawnLogicRuntimeMode(_activeMode))
            {
                if (_activeSpawnLogic == null)
                    return;

                summary = BuildDetailedRuntimeSummary(_activeSpawnLogic);
            }
            else if (_activeMode == ActiveBootstrapMode.LordsHallController)
            {
                CoopExactCampaignLordsHallMissionController controller =
                    mission.GetMissionBehavior<CoopExactCampaignLordsHallMissionController>();
                summary = controller?.BuildRuntimeSummary() ??
                          "Mode=LordsHall Started=false Controller=missing";
            }
            else
            {
                return;
            }

            if (!force && string.Equals(summary, _lastRuntimeDiagnosticsSummary, StringComparison.Ordinal))
            {
                _nextRuntimeDiagnosticsLogUtc = nowUtc.AddSeconds(2);
                return;
            }

            _lastRuntimeDiagnosticsSummary = summary;
            _nextRuntimeDiagnosticsLogUtc = nowUtc.AddSeconds(force ? 1 : 2);
            ModLogger.Info(
                "ExactCampaignArmyBootstrap: runtime state. " +
                "Scene=" + (mission?.SceneName ?? "null") +
                " Mode=" + _activeMode +
                " ReinforcementsEnabled=" + _reinforcementsEnabled +
                " PlayerSide=" + _activePlayerSide +
                " Source=" + (source ?? "unknown") +
                " " + summary);
        }

        public static bool TryGetEntryId(Agent agent, out string entryId)
        {
            if (agent?.Origin is ExactCampaignSnapshotAgentOrigin origin &&
                !string.IsNullOrWhiteSpace(origin.EntryId))
            {
                entryId = origin.EntryId;
                return true;
            }

            entryId = null;
            return false;
        }

        public static bool TryGetSide(Agent agent, out BattleSideEnum side)
        {
            if (agent?.Origin is ExactCampaignSnapshotAgentOrigin origin)
            {
                side = origin.Side;
                return side != BattleSideEnum.None;
            }

            side = BattleSideEnum.None;
            return false;
        }

        public static void LogInitializationDeferred(Mission mission, string reason, string source)
        {
            DateTime nowUtc = DateTime.UtcNow;
            if (nowUtc < _nextDeferredLogUtc)
                return;

            _nextDeferredLogUtc = nowUtc.AddSeconds(2);
            ModLogger.Info(
                "ExactCampaignArmyBootstrap: deferred native-like army bootstrap initialization. " +
                "Scene=" + (mission?.SceneName ?? "null") +
                " Reason=" + (reason ?? "unknown") +
                " Source=" + (source ?? "unknown"));
        }

        private static void ComputeInitialSpawnCounts(
            Mission mission,
            int defenderTotal,
            int attackerTotal,
            out int defenderInitial,
            out int attackerInitial,
            out int battleSizeBudget)
        {
            defenderTotal = Math.Max(0, defenderTotal);
            attackerTotal = Math.Max(0, attackerTotal);
            int total = defenderTotal + attackerTotal;

            battleSizeBudget = BattleAgentCapacityPolicy.GetResolvedBattleSize(
                mission,
                "ExactCampaignArmyBootstrap.ComputeInitialSpawnCounts");
            if (battleSizeBudget <= 0)
                battleSizeBudget = BattleSnapshotRuntimeState.GetState()?.BattleSizeBudget ?? 0;
            if (battleSizeBudget <= 0)
                battleSizeBudget = total;

            if (total <= 0 || battleSizeBudget >= total)
            {
                defenderInitial = defenderTotal;
                attackerInitial = attackerTotal;
                return;
            }

            battleSizeBudget = Math.Max(1, battleSizeBudget);
            BattleAgentCapacityPolicy.AllocateInitialTroops(
                defenderTotal,
                attackerTotal,
                battleSizeBudget,
                out defenderInitial,
                out attackerInitial);
        }

        private static MissionSpawnSettings CreateNativeCampaignBattleWaveSpawnSettings()
        {
            return new MissionSpawnSettings(
                MissionSpawnSettings.InitialSpawnMethod.BattleSizeAllocating,
                MissionSpawnSettings.ReinforcementTimingMethod.GlobalTimer,
                MissionSpawnSettings.ReinforcementSpawnMethod.Wave,
                globalReinforcementInterval: 3f,
                reinforcementBatchPercentage: 0f,
                desiredReinforcementPercentage: 0f,
                reinforcementWavePercentage: 0.5f,
                maximumReinforcementWaveCount: GetResolvedReinforcementWaveCount(),
                defenderReinforcementBatchPercentage: 0f,
                attackerReinforcementBatchPercentage: 0f,
                defenderAdvantageFactor: 1f,
                maximumBattleSizeRatio: 0.75f);
        }

        private static int GetResolvedReinforcementWaveCount()
        {
            int reinforcementWaveCount = BattleSnapshotRuntimeState.GetState()?.ReinforcementWaveCount ?? 0;
            if (reinforcementWaveCount <= 0)
            {
                reinforcementWaveCount = BannerlordConfig.GetReinforcementWaveCount();
            }

            return Math.Max(0, reinforcementWaveCount);
        }

        private static bool TryOverrideNativeBattleSize(
            NativeMissionAgentSpawnLogic spawnLogic,
            int battleSizeBudget,
            out string diagnostics)
        {
            if (spawnLogic == null)
            {
                diagnostics = "spawn-logic-null";
                return false;
            }

            if (battleSizeBudget <= 0)
            {
                diagnostics = "battle-size-budget-invalid";
                return false;
            }

            if (MissionAgentSpawnLogicBattleSizeField == null)
            {
                diagnostics = "battle-size-field-metadata-missing";
                return false;
            }

            try
            {
                int safeBattleSize = Math.Min(
                    battleSizeBudget,
                    BattleAgentCapacityPolicy.GetMaximumPhysicalAgentCount());
                MissionAgentSpawnLogicBattleSizeField.SetValue(spawnLogic, safeBattleSize);
                diagnostics = "ok";
                return true;
            }
            catch (Exception ex)
            {
                diagnostics = ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        private static int GetNativeBattleSize(NativeMissionAgentSpawnLogic spawnLogic)
        {
            if (spawnLogic == null || MissionAgentSpawnLogicBattleSizeField == null)
                return -1;

            object value = MissionAgentSpawnLogicBattleSizeField.GetValue(spawnLogic);
            return value is int battleSize ? battleSize : -1;
        }

        private static bool SideHasMountedTroops(IMissionTroopSupplier[] suppliers, BattleSideEnum side)
        {
            if (suppliers == null)
                return false;

            int sideIndex = (int)side;
            if (sideIndex < 0 || sideIndex >= suppliers.Length)
                return false;

            IMissionTroopSupplier supplier = suppliers[sideIndex];
            if (supplier == null)
                return false;

            IEnumerable<IAgentOriginBase> troops = supplier.GetAllTroops();
            if (troops == null)
                return false;

            foreach (IAgentOriginBase troop in troops)
            {
                if (troop?.Troop?.IsMounted == true)
                    return true;
            }

            return false;
        }

        private static bool TryBuildSuppliers(
            BattleSideEnum playerSide,
            BattleScenarioContextMessage scenarioContext,
            out IMissionTroopSupplier[] suppliers,
            out int defenderTotal,
            out int attackerTotal,
            out string diagnostics)
        {
            suppliers = null;
            defenderTotal = 0;
            attackerTotal = 0;
            diagnostics = string.Empty;

            BattleRuntimeState runtimeState = BattleSnapshotRuntimeState.GetState();
            if (runtimeState?.Sides == null || runtimeState.Sides.Count <= 0)
            {
                diagnostics = "runtime-state-missing";
                return false;
            }

            bool useMissionReadyOnly = IsLordsHallSiegeSubtype(scenarioContext);
            int maxDefenderEntries = scenarioContext?.SiegeContext?.LordsHallMaxDefenderSideTroopCount > 0
                ? scenarioContext.SiegeContext.LordsHallMaxDefenderSideTroopCount
                : 27;
            int maxAttackerEntries = scenarioContext?.SiegeContext?.LordsHallMaxAttackerSideTroopCount > 0
                ? scenarioContext.SiegeContext.LordsHallMaxAttackerSideTroopCount
                : 19;
            string defenderDiagnostics;
            string attackerDiagnostics;
            ExactCampaignSnapshotTroopSupplier defenderSupplier = useMissionReadyOnly
                ? BuildMissionReadyOnlySupplier(
                    runtimeState,
                    BattleSideEnum.Defender,
                    playerSide,
                    maxEntries: maxDefenderEntries,
                    out defenderTotal,
                    out defenderDiagnostics)
                : BuildSupplier(runtimeState, BattleSideEnum.Defender, playerSide, out defenderTotal, out defenderDiagnostics);
            ExactCampaignSnapshotTroopSupplier attackerSupplier = useMissionReadyOnly
                ? BuildMissionReadyOnlySupplier(
                    runtimeState,
                    BattleSideEnum.Attacker,
                    playerSide,
                    maxEntries: maxAttackerEntries,
                    out attackerTotal,
                    out attackerDiagnostics)
                : BuildSupplier(runtimeState, BattleSideEnum.Attacker, playerSide, out attackerTotal, out attackerDiagnostics);
            suppliers = new IMissionTroopSupplier[2]
            {
                defenderSupplier,
                attackerSupplier
            };

            diagnostics =
                "Mode=" + (useMissionReadyOnly ? "MissionReadyOnly" : "FullBattleRoster") + " " +
                "Defender=" + defenderTotal + "(" + defenderDiagnostics + ")" +
                " Attacker=" + attackerTotal + "(" + attackerDiagnostics + ")";

            if (ExactLandBattleScenarioContract.IsLandBattleScenario(scenarioContext) &&
                (defenderTotal <= 0 || attackerTotal <= 0))
            {
                diagnostics += " Rejected=terminal-land-battle-side-empty";
                return false;
            }

            return defenderTotal > 0 || attackerTotal > 0;
        }

        private static ExactCampaignSnapshotTroopSupplier BuildMissionReadyOnlySupplier(
            BattleRuntimeState runtimeState,
            BattleSideEnum side,
            BattleSideEnum playerSide,
            int maxEntries,
            out int totalHealthyCount,
            out string diagnostics)
        {
            totalHealthyCount = 0;
            diagnostics = "side-state-missing";

            BattleSideState sideState = runtimeState?.Sides?.FirstOrDefault(candidate => ResolveBattleSide(candidate) == side);
            if (sideState?.Entries == null || sideState.Entries.Count <= 0)
                return new ExactCampaignSnapshotTroopSupplier(side, side == playerSide);

            List<string> missionReadyEntryOrder = sideState.MissionReadyEntryOrder?
                .Where(entryId => !string.IsNullOrWhiteSpace(entryId))
                .ToList();
            if (missionReadyEntryOrder == null || missionReadyEntryOrder.Count <= 0)
            {
                diagnostics = "mission-ready-order-missing";
                return new ExactCampaignSnapshotTroopSupplier(side, side == playerSide);
            }

            var supplier = new ExactCampaignSnapshotTroopSupplier(side, side == playerSide);
            var origins = new List<ExactCampaignSnapshotAgentOrigin>();
            ResolveOriginAppearance(sideState, side, out uint factionColor, out uint factionColor2, out Banner banner);
            Dictionary<string, RosterEntryState> entriesById = sideState.Entries
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.EntryId))
                .GroupBy(entry => entry.EntryId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var remainingHealthyByEntryId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int seed = 1;
            int missingEntries = 0;
            int exhaustedEntries = 0;
            int unresolvedEntries = 0;

            foreach (RosterEntryState entryState in sideState.Entries)
            {
                if (entryState == null || string.IsNullOrWhiteSpace(entryState.EntryId))
                    continue;

                int availableCount = Math.Max(0, entryState.Count - entryState.WoundedCount);
                if (availableCount > 0)
                    remainingHealthyByEntryId[entryState.EntryId] = availableCount;
            }

            RosterEntryState commanderEntryState = BattleCommanderResolver.ResolveCommanderEntry(runtimeState, side, sideState.Entries);
            BasicCharacterObject generalCharacter = TryResolveEntryCharacter(commanderEntryState);

            foreach (string entryId in missionReadyEntryOrder)
            {
                if (totalHealthyCount >= maxEntries)
                    break;

                if (!entriesById.TryGetValue(entryId, out RosterEntryState entryState) || entryState == null)
                {
                    missingEntries++;
                    continue;
                }

                if (!remainingHealthyByEntryId.TryGetValue(entryId, out int remainingHealthyCount) || remainingHealthyCount <= 0)
                {
                    exhaustedEntries++;
                    continue;
                }

                BasicCharacterObject troop = TryResolveEntryCharacter(entryState);
                if (troop == null)
                {
                    unresolvedEntries++;
                    continue;
                }

                if (generalCharacter == null &&
                    (entryState.IsHero || !string.IsNullOrWhiteSpace(entryState.HeroRole)))
                {
                    generalCharacter = troop;
                }

                AppendOriginForEntry(origins, supplier, entryState, troop, side, playerSide, factionColor, factionColor2, banner, ref seed);
                remainingHealthyByEntryId[entryId] = remainingHealthyCount - 1;
                totalHealthyCount++;
            }

            if (generalCharacter == null)
                generalCharacter = origins.FirstOrDefault()?.Troop;

            supplier.Initialize(origins, generalCharacter);
            diagnostics =
                "MissionReadyOrder=" + missionReadyEntryOrder.Count +
                " Selected=" + totalHealthyCount +
                " MaxEntries=" + maxEntries +
                " MissingEntries=" + missingEntries +
                " ExhaustedEntries=" + exhaustedEntries +
                " UnresolvedEntries=" + unresolvedEntries +
                " GeneralCharacter=" + (generalCharacter?.StringId ?? "null");
            return supplier;
        }

        private static bool TrySeedFormationBannerCodes(
            Mission mission,
            BattleSideEnum playerSide,
            string source,
            out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            bool anySeeded =
                TrySeedFormationBannerCodesForTeam(mission.AttackerTeam, BattleSideEnum.Attacker, playerSide, out string attackerDiagnostics) |
                TrySeedFormationBannerCodesForTeam(mission.DefenderTeam, BattleSideEnum.Defender, playerSide, out string defenderDiagnostics);

            diagnostics =
                "Attacker={" + attackerDiagnostics + "} " +
                "Defender={" + defenderDiagnostics + "}";

            ModLogger.Info(
                "ExactCampaignArmyBootstrap: formation banner-code seed for exact runtime. " +
                "Scene=" + (mission.SceneName ?? "null") +
                " PlayerSide=" + playerSide +
                " AnySeeded=" + anySeeded +
                " Details=" + diagnostics +
                " Source=" + (source ?? "unknown"));

            return anySeeded;
        }

        private static bool TrySeedFormationBannerCodesForTeam(
            Team team,
            BattleSideEnum side,
            BattleSideEnum playerSide,
            out string diagnostics)
        {
            diagnostics = "team-null";
            if (team == null)
                return false;

            string bannerCode = ResolvePreferredFormationBannerCodeForTeam(team, side, playerSide, out string bannerSource);
            if (string.IsNullOrWhiteSpace(bannerCode))
            {
                diagnostics =
                    "TeamIndex=" + team.TeamIndex +
                    " Source=" + bannerSource +
                    " BannerCode=empty";
                return false;
            }

            int changed = 0;
            int unchanged = 0;
            int formationCount = 0;
            foreach (Formation formation in team.FormationsIncludingEmpty)
            {
                if (formation == null || !ReferenceEquals(formation.Team, team))
                    continue;

                formationCount++;
                if (string.Equals(formation.BannerCode ?? string.Empty, bannerCode, StringComparison.Ordinal))
                {
                    unchanged++;
                    continue;
                }

                formation.BannerCode = bannerCode;
                changed++;
            }

            diagnostics =
                "TeamIndex=" + team.TeamIndex +
                " TeamSide=" + team.Side +
                " Source=" + bannerSource +
                " FormationCount=" + formationCount +
                " Changed=" + changed +
                " Unchanged=" + unchanged +
                " BannerCodeLength=" + bannerCode.Length;
            return changed > 0;
        }

        private static string ResolvePreferredFormationBannerCodeForTeam(
            Team team,
            BattleSideEnum side,
            BattleSideEnum playerSide,
            out string source)
        {
            source = "none";
            if (team == null)
                return null;

            string runtimeSideBannerCode = BattleSnapshotRuntimeState.ResolveSideBannerCode(side, null);
            if (!string.IsNullOrWhiteSpace(runtimeSideBannerCode))
            {
                source = "battle-snapshot-side";
                return runtimeSideBannerCode;
            }

            string teamBannerCode = team.Banner?.BannerCode;
            if (!string.IsNullOrWhiteSpace(teamBannerCode))
            {
                source = "team-banner";
                return teamBannerCode;
            }

            string assignedPeerBannerCode = TryResolveAssignedMissionPeerBannerCode(team);
            if (!string.IsNullOrWhiteSpace(assignedPeerBannerCode))
            {
                source = "assigned-peer";
                return assignedPeerBannerCode;
            }

            if (side == playerSide)
            {
                string singleActivePeerBannerCode = TryResolveSingleActivePlayerPeerBannerCode();
                if (!string.IsNullOrWhiteSpace(singleActivePeerBannerCode))
                {
                    source = "single-active-peer";
                    return singleActivePeerBannerCode;
                }
            }

            return null;
        }

        private static string TryResolveAssignedMissionPeerBannerCode(Team team)
        {
            if (team == null || GameNetwork.NetworkPeers == null)
                return null;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive)
                    continue;

                MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
                if (missionPeer == null || !ReferenceEquals(missionPeer.Team, team))
                    continue;

                string bannerCode = missionPeer.Peer?.BannerCode;
                if (!string.IsNullOrWhiteSpace(bannerCode))
                    return bannerCode;
            }

            return null;
        }

        private static string TryResolveSingleActivePlayerPeerBannerCode()
        {
            if (GameNetwork.NetworkPeers == null)
                return null;

            string resolvedBannerCode = null;
            int candidateCount = 0;
            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive)
                    continue;

                MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
                string bannerCode = missionPeer?.Peer?.BannerCode;
                if (string.IsNullOrWhiteSpace(bannerCode))
                    continue;

                candidateCount++;
                if (candidateCount > 1)
                    return null;

                resolvedBannerCode = bannerCode;
            }

            return resolvedBannerCode;
        }

        private static void PushSpawnLogicInitTeamSideOverride(Mission mission, BattleSideEnum playerSide)
        {
            if (mission == null || playerSide == BattleSideEnum.None)
                return;

            if (_spawnLogicInitSideOverrideDepth == 0 || !ReferenceEquals(_spawnLogicInitSideOverrideMission, mission))
            {
                _spawnLogicInitSideOverrideMission = mission;
                _spawnLogicInitSideOverride = playerSide;
                _spawnLogicInitSideOverrideDepth = 1;
                return;
            }

            _spawnLogicInitSideOverrideDepth++;
        }

        private static void PopSpawnLogicInitTeamSideOverride(Mission mission)
        {
            if (_spawnLogicInitSideOverrideDepth <= 0 || !ReferenceEquals(_spawnLogicInitSideOverrideMission, mission))
                return;

            _spawnLogicInitSideOverrideDepth--;
            if (_spawnLogicInitSideOverrideDepth > 0)
                return;

            _spawnLogicInitSideOverrideMission = null;
            _spawnLogicInitSideOverride = BattleSideEnum.None;
            SpawnLogicInitTemporaryNonBattleTeams.Clear();
        }

        private static List<TeamSideOverrideState> PushInitTeamSideSanitization(
            Mission mission,
            BattleSideEnum playerSide,
            string source)
        {
            var overrides = new List<TeamSideOverrideState>();
            if (mission?.Teams == null || playerSide == BattleSideEnum.None)
                return overrides;

            if (TeamSideBackingField == null)
            {
                ModLogger.Info(
                    "ExactCampaignArmyBootstrap: could not sanitize Team.Side=None during native bootstrap init because Team backing field was not found. " +
                    "Scene=" + (mission.SceneName ?? "null") +
                    " PlayerSide=" + playerSide +
                    " Source=" + (source ?? "unknown"));
                return overrides;
            }

            foreach (Team team in mission.Teams)
            {
                if (team == null || team.Side != BattleSideEnum.None)
                    continue;

                try
                {
                    SpawnLogicInitTemporaryNonBattleTeams.Add(team);
                    TeamSideBackingField.SetValue(team, playerSide);
                    bool addedTemporaryDeploymentPlan =
                        TryAddTemporaryDeploymentPlanForRemappedTeam(
                            mission,
                            team,
                            source,
                            out int temporaryDeploymentPlanIndex);
                    overrides.Add(
                        new TeamSideOverrideState(
                            team,
                            BattleSideEnum.None,
                            addedTemporaryDeploymentPlan,
                            temporaryDeploymentPlanIndex));
                }
                catch (Exception ex)
                {
                    SpawnLogicInitTemporaryNonBattleTeams.Remove(team);
                    ModLogger.Info(
                        "ExactCampaignArmyBootstrap: failed to temporarily remap Team.Side=None during native bootstrap init. " +
                        "Scene=" + (mission.SceneName ?? "null") +
                        " TeamIndex=" + team.TeamIndex +
                        " RequestedSide=" + playerSide +
                        " Error=" + ex.GetType().Name + ": " + ex.Message +
                        " Source=" + (source ?? "unknown"));
                }
            }

            if (overrides.Count > 0)
            {
                string overrideSummary = string.Join(
                    ", ",
                    overrides.Select(state => "#" + state.Team.TeamIndex + ":" + state.OriginalSide + "->" + playerSide));
                ModLogger.Info(
                    "ExactCampaignArmyBootstrap: temporarily remapped non-battle teams during native bootstrap init. " +
                    "Scene=" + (mission.SceneName ?? "null") +
                    " PlayerSide=" + playerSide +
                    " Teams=[" + overrideSummary + "] " +
                    " DeploymentPlanBridge=excluded-from-battle-team-plans " +
                    " Source=" + (source ?? "unknown"));
            }

            return overrides;
        }

        private static void PopInitTeamSideSanitization(
            List<TeamSideOverrideState> overrides,
            string source)
        {
            if (overrides == null || overrides.Count == 0 || TeamSideBackingField == null)
                return;

            foreach (TeamSideOverrideState state in overrides)
            {
                if (state.Team == null)
                    continue;

                if (state.AddedTemporaryDeploymentPlan)
                {
                    TryRemoveTemporaryDeploymentPlanForRemappedTeam(state.Team.Mission, state.TemporaryDeploymentPlanIndex, source);
                }

                try
                {
                    TeamSideBackingField.SetValue(state.Team, state.OriginalSide);
                    SpawnLogicInitTemporaryNonBattleTeams.Remove(state.Team);
                }
                catch (Exception ex)
                {
                    SpawnLogicInitTemporaryNonBattleTeams.Remove(state.Team);
                    ModLogger.Info(
                        "ExactCampaignArmyBootstrap: failed to restore temporary Team.Side remap after native bootstrap init. " +
                        "Scene=" + (state.Team.Mission?.SceneName ?? "null") +
                        " TeamIndex=" + state.Team.TeamIndex +
                        " RestoreSide=" + state.OriginalSide +
                        " Error=" + ex.GetType().Name + ": " + ex.Message +
                        " Source=" + (source ?? "unknown"));
                }
            }
        }

        private static bool TryAddTemporaryDeploymentPlanForRemappedTeam(
            Mission mission,
            Team team,
            string source,
            out int addedIndex)
        {
            addedIndex = -1;
            if (mission == null || team == null)
                return false;

            if (!mission.GetDeploymentPlan<DefaultMissionDeploymentPlan>(out DefaultMissionDeploymentPlan deploymentPlan) ||
                deploymentPlan == null)
            {
                return false;
            }

            if (DefaultMissionDeploymentPlanTeamDeploymentPlansField == null)
            {
                ModLogger.Info(
                    "ExactCampaignArmyBootstrap: could not bridge deployment plan for remapped non-battle team because DefaultMissionDeploymentPlan field metadata was not found. " +
                    "Scene=" + (mission.SceneName ?? "null") +
                    " TeamIndex=" + team.TeamIndex +
                    " Source=" + (source ?? "unknown"));
                return false;
            }

            object teamPlans = DefaultMissionDeploymentPlanTeamDeploymentPlansField.GetValue(deploymentPlan);
            if (teamPlans == null)
                return false;

            PropertyInfo countProperty = teamPlans.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo addMethod = teamPlans.GetType().GetMethod("Add", BindingFlags.Instance | BindingFlags.Public);
            if (countProperty == null || addMethod == null)
                return false;

            addedIndex = (int)countProperty.GetValue(teamPlans);
            var teamPlanTuple = (team, new DefaultTeamDeploymentPlan(mission, team));
            addMethod.Invoke(teamPlans, new object[] { teamPlanTuple });
            ModLogger.Info(
                "ExactCampaignArmyBootstrap: added temporary deployment plan for remapped non-battle team during native bootstrap init. " +
                "Scene=" + (mission.SceneName ?? "null") +
                " TeamIndex=" + team.TeamIndex +
                " TeamSide=" + team.Side +
                " AddedIndex=" + addedIndex +
                " Source=" + (source ?? "unknown"));
            return true;
        }

        private static void TryRemoveTemporaryDeploymentPlanForRemappedTeam(
            Mission mission,
            int addedIndex,
            string source)
        {
            if (mission == null || addedIndex < 0)
                return;

            if (!mission.GetDeploymentPlan<DefaultMissionDeploymentPlan>(out DefaultMissionDeploymentPlan deploymentPlan) ||
                deploymentPlan == null ||
                DefaultMissionDeploymentPlanTeamDeploymentPlansField == null)
            {
                return;
            }

            object teamPlans = DefaultMissionDeploymentPlanTeamDeploymentPlansField.GetValue(deploymentPlan);
            if (teamPlans == null)
                return;

            PropertyInfo countProperty = teamPlans.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
            MethodInfo removeAtMethod = teamPlans.GetType().GetMethod("RemoveAt", BindingFlags.Instance | BindingFlags.Public);
            if (countProperty == null || removeAtMethod == null)
                return;

            int count = (int)countProperty.GetValue(teamPlans);
            if (addedIndex < 0 || addedIndex >= count)
                return;

            removeAtMethod.Invoke(teamPlans, new object[] { addedIndex });
            ModLogger.Info(
                "ExactCampaignArmyBootstrap: removed temporary deployment plan for remapped non-battle team after native bootstrap init. " +
                "Scene=" + (mission.SceneName ?? "null") +
                " RemovedIndex=" + addedIndex +
                " Source=" + (source ?? "unknown"));
        }

        private static ExactCampaignSnapshotTroopSupplier BuildSupplier(
            BattleRuntimeState runtimeState,
            BattleSideEnum side,
            BattleSideEnum playerSide,
            out int totalHealthyCount,
            out string diagnostics)
        {
            totalHealthyCount = 0;
            diagnostics = "side-state-missing";

            BattleSideState sideState = runtimeState?.Sides?.FirstOrDefault(candidate => ResolveBattleSide(candidate) == side);
            if (sideState?.Entries == null || sideState.Entries.Count <= 0)
                return new ExactCampaignSnapshotTroopSupplier(side, side == playerSide);

            var supplier = new ExactCampaignSnapshotTroopSupplier(side, side == playerSide);
            var origins = new List<ExactCampaignSnapshotAgentOrigin>();
            RosterEntryState commanderEntryState = BattleCommanderResolver.ResolveCommanderEntry(runtimeState, side, sideState.Entries);
            string commanderEntryId = commanderEntryState?.EntryId;
            IEnumerable<RosterEntryState> orderedEntries = sideState.Entries;
            List<string> missionReadyEntryOrder = sideState.MissionReadyEntryOrder?
                .Where(entryId => !string.IsNullOrWhiteSpace(entryId))
                .ToList();
            if ((missionReadyEntryOrder?.Count ?? 0) <= 0 &&
                !string.IsNullOrWhiteSpace(commanderEntryId))
            {
                orderedEntries = sideState.Entries
                    .OrderByDescending(entry => string.Equals(entry?.EntryId, commanderEntryId, StringComparison.Ordinal));
            }

            BasicCharacterObject commanderCharacter = TryResolveEntryCharacter(commanderEntryState);
            BasicCharacterObject generalCharacter = commanderCharacter;
            int unresolvedEntries = 0;
            int skippedWoundedOnlyEntries = 0;
            int totalRawCount = 0;
            int aggregateWoundedCount = 0;
            int missionReadyMatched = 0;
            int missionReadyMissingEntries = 0;
            int missionReadyExhaustedEntries = 0;
            int missionReadyUnresolvedEntries = 0;
            int seed = 1;
            ResolveOriginAppearance(sideState, side, out uint factionColor, out uint factionColor2, out Banner banner);
            var remainingHealthyByEntryId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (RosterEntryState entryState in sideState.Entries)
            {
                if (entryState == null)
                    continue;

                totalRawCount += Math.Max(0, entryState.Count);
                aggregateWoundedCount += Math.Max(0, entryState.WoundedCount);
                int availableCount = Math.Max(0, entryState.Count - entryState.WoundedCount);
                if (availableCount <= 0)
                {
                    skippedWoundedOnlyEntries++;
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(entryState.EntryId))
                    remainingHealthyByEntryId[entryState.EntryId] = availableCount;
            }

            HashSet<string> unresolvedEntryIds = null;
            if (missionReadyEntryOrder?.Count > 0)
            {
                Dictionary<string, RosterEntryState> entriesById = sideState.Entries
                    .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.EntryId))
                    .GroupBy(entry => entry.EntryId, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
                unresolvedEntryIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (string entryId in missionReadyEntryOrder)
                {
                    if (!entriesById.TryGetValue(entryId, out RosterEntryState entryState) || entryState == null)
                    {
                        missionReadyMissingEntries++;
                        continue;
                    }

                    if (!remainingHealthyByEntryId.TryGetValue(entryId, out int remainingHealthyCount) || remainingHealthyCount <= 0)
                    {
                        missionReadyExhaustedEntries++;
                        continue;
                    }

                    BasicCharacterObject troop = TryResolveEntryCharacter(entryState);
                    if (troop == null)
                    {
                        unresolvedEntries++;
                        missionReadyUnresolvedEntries++;
                        unresolvedEntryIds.Add(entryId);
                        continue;
                    }

                    AppendOriginForEntry(origins, supplier, entryState, troop, side, playerSide, factionColor, factionColor2, banner, ref seed);
                    remainingHealthyByEntryId[entryId] = remainingHealthyCount - 1;
                    totalHealthyCount++;
                    missionReadyMatched++;

                    if (generalCharacter == null &&
                        (entryState.IsHero || !string.IsNullOrWhiteSpace(entryState.HeroRole)))
                    {
                        generalCharacter = troop;
                    }
                }
            }

            foreach (RosterEntryState entryState in orderedEntries)
            {
                if (entryState == null)
                    continue;

                if (unresolvedEntryIds != null &&
                    !string.IsNullOrWhiteSpace(entryState.EntryId) &&
                    unresolvedEntryIds.Contains(entryState.EntryId))
                {
                    continue;
                }

                int availableCount = !string.IsNullOrWhiteSpace(entryState.EntryId) &&
                                     remainingHealthyByEntryId.TryGetValue(entryState.EntryId, out int remainingHealthyCount)
                    ? remainingHealthyCount
                    : Math.Max(0, entryState.Count - entryState.WoundedCount);
                if (availableCount <= 0)
                    continue;

                BasicCharacterObject troop = TryResolveEntryCharacter(entryState);
                if (troop == null)
                {
                    unresolvedEntries++;
                    continue;
                }

                if (generalCharacter == null &&
                    (entryState.IsHero || !string.IsNullOrWhiteSpace(entryState.HeroRole)))
                {
                    generalCharacter = troop;
                }

                for (int i = 0; i < availableCount; i++)
                {
                    AppendOriginForEntry(origins, supplier, entryState, troop, side, playerSide, factionColor, factionColor2, banner, ref seed);
                    totalHealthyCount++;
                }

                if (!string.IsNullOrWhiteSpace(entryState.EntryId))
                    remainingHealthyByEntryId[entryState.EntryId] = 0;
            }

            if (generalCharacter == null)
                generalCharacter = origins.FirstOrDefault()?.Troop;

            supplier.Initialize(origins, generalCharacter);
            diagnostics =
                "Entries=" + sideState.Entries.Count +
                " RawTotal=" + totalRawCount +
                " Healthy=" + totalHealthyCount +
                " AggregateWounded=" + aggregateWoundedCount +
                " UnresolvedEntries=" + unresolvedEntries +
                " WoundedOnlyEntries=" + skippedWoundedOnlyEntries +
                " CommanderEntryId=" + (commanderEntryId ?? "none") +
                " MissionReadyOrder=" + (missionReadyEntryOrder?.Count ?? 0) +
                " MissionReadyMatched=" + missionReadyMatched +
                " MissionReadyMissing=" + missionReadyMissingEntries +
                " MissionReadyExhausted=" + missionReadyExhaustedEntries +
                " MissionReadyUnresolved=" + missionReadyUnresolvedEntries +
                " GeneralCharacter=" + (generalCharacter?.StringId ?? "null");
            return supplier;
        }

        private static int GetActiveRemovedTroopCount(BattleSideEnum side)
        {
            IMissionTroopSupplier supplier = GetActiveSupplier(side);
            return supplier?.NumRemovedTroops ?? 0;
        }

        private static int GetActiveRemainingTroopCount(BattleSideEnum side)
        {
            IMissionTroopSupplier supplier = GetActiveSupplier(side);
            return Math.Max(0, supplier?.NumTroopsNotSupplied ?? 0);
        }

        private static IMissionTroopSupplier GetActiveSupplier(BattleSideEnum side)
        {
            if (_activeSuppliers == null)
                return null;

            int sideIndex = (int)side;
            return sideIndex >= 0 && sideIndex < _activeSuppliers.Length
                ? _activeSuppliers[sideIndex]
                : null;
        }

        private static int CountActiveMissionSideAgents(Mission mission, BattleSideEnum side)
        {
            if (mission == null)
                return 0;

            Team team =
                side == BattleSideEnum.Attacker
                    ? mission.AttackerTeam
                    : side == BattleSideEnum.Defender
                        ? mission.DefenderTeam
                        : null;
            return team?.ActiveAgents?.Count ?? 0;
        }

        private static void ResolveOriginAppearance(
            BattleSideState sideState,
            BattleSideEnum side,
            out uint factionColor,
            out uint factionColor2,
            out Banner banner)
        {
            const uint fallbackAttackerColor = 0xFFCC2222u;
            const uint fallbackAttackerColor2 = 0xFF661111u;
            const uint fallbackDefenderColor = 0xFF2222CCu;
            const uint fallbackDefenderColor2 = 0xFF111166u;
            const string fallbackAttackerCultureId = "empire";
            const string fallbackDefenderCultureId = "vlandia";

            uint fallbackColor = side == BattleSideEnum.Attacker ? fallbackAttackerColor : fallbackDefenderColor;
            uint fallbackColor2 = side == BattleSideEnum.Attacker ? fallbackAttackerColor2 : fallbackDefenderColor2;
            string fallbackCultureId = side == BattleSideEnum.Attacker ? fallbackAttackerCultureId : fallbackDefenderCultureId;

            string cultureId = !string.IsNullOrWhiteSpace(sideState?.CultureId)
                ? sideState.CultureId
                : fallbackCultureId;
            factionColor = sideState?.Color ?? 0u;
            factionColor2 = sideState?.Color2 ?? 0u;
            string bannerCode = sideState?.BannerCode;

            BasicCultureObject culture = !string.IsNullOrWhiteSpace(cultureId)
                ? MBObjectManager.Instance?.GetObject<BasicCultureObject>(cultureId)
                : null;
            if (culture != null)
            {
                if (factionColor == 0u)
                    factionColor = culture.Color;
                if (factionColor2 == 0u)
                    factionColor2 = culture.Color2;
                if (string.IsNullOrWhiteSpace(bannerCode))
                    bannerCode = culture.Banner?.BannerCode;
            }

            if (factionColor == 0u)
                factionColor = fallbackColor;
            if (factionColor2 == 0u)
                factionColor2 = fallbackColor2;

            banner = null;
            if (string.IsNullOrWhiteSpace(bannerCode))
                return;

            try
            {
                banner = new Banner(bannerCode, factionColor, factionColor2);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "ExactCampaignArmyBootstrap: failed to resolve exact origin banner for side. " +
                    "Side=" + side +
                    " Culture=" + (cultureId ?? "null") +
                    " BannerCodeLength=" + bannerCode.Length +
                    " Error=" + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void AppendOriginForEntry(
            List<ExactCampaignSnapshotAgentOrigin> origins,
            ExactCampaignSnapshotTroopSupplier supplier,
            RosterEntryState entryState,
            BasicCharacterObject troop,
            BattleSideEnum side,
            BattleSideEnum playerSide,
            uint factionColor,
            uint factionColor2,
            Banner banner,
            ref int seed)
        {
            if (origins == null || supplier == null || entryState == null || troop == null)
                return;

            origins.Add(new ExactCampaignSnapshotAgentOrigin(
                supplier,
                troop,
                entryState.EntryId,
                troop.StringId,
                side,
                side == playerSide,
                factionColor,
                factionColor2,
                banner,
                seed++));
        }

        private static BasicCharacterObject TryResolveEntryCharacter(RosterEntryState entryState)
        {
            if (entryState == null)
                return null;

            if (!string.IsNullOrWhiteSpace(entryState.EntryId))
            {
                BasicCharacterObject runtimeCharacter = BattleSnapshotRuntimeState.TryResolveCharacterObject(entryState.EntryId);
                if (runtimeCharacter != null)
                    return runtimeCharacter;
            }

            string[] candidateIds =
            {
                entryState.SpawnTemplateId,
                entryState.OriginalCharacterId,
                entryState.CharacterId,
                entryState.HeroTemplateId
            };

            foreach (string candidateId in candidateIds)
            {
                if (string.IsNullOrWhiteSpace(candidateId))
                    continue;

                try
                {
                    BasicCharacterObject candidate = MBObjectManager.Instance.GetObject<BasicCharacterObject>(candidateId);
                    if (candidate != null)
                        return candidate;
                }
                catch
                {
                }
            }

            string missionSafeFallbackCharacterId =
                BattleSnapshotRuntimeState.TryResolveMissionSafeFallbackCharacterId(
                    entryState,
                    entryState.SpawnTemplateId ?? entryState.CharacterId);
            if (!string.IsNullOrWhiteSpace(missionSafeFallbackCharacterId))
            {
                try
                {
                    BasicCharacterObject fallbackCharacter = MBObjectManager.Instance.GetObject<BasicCharacterObject>(missionSafeFallbackCharacterId);
                    if (fallbackCharacter != null)
                        return fallbackCharacter;
                }
                catch
                {
                }
            }

            return null;
        }

        private static BattleSideEnum ResolveBattleSide(BattleSideState sideState)
        {
            if (sideState == null)
                return BattleSideEnum.None;

            string raw =
                !string.IsNullOrWhiteSpace(sideState.CanonicalSideKey)
                    ? sideState.CanonicalSideKey
                    : sideState.SideId;
            if (string.Equals(raw, "attacker", StringComparison.OrdinalIgnoreCase))
                return BattleSideEnum.Attacker;
            if (string.Equals(raw, "defender", StringComparison.OrdinalIgnoreCase))
                return BattleSideEnum.Defender;
            return BattleSideEnum.None;
        }
    }

    public sealed class ExactCampaignSnapshotTroopSupplier : IMissionTroopSupplier
    {
        private readonly bool _isPlayerSide;
        private List<ExactCampaignSnapshotAgentOrigin> _troops = new List<ExactCampaignSnapshotAgentOrigin>();
        private BasicCharacterObject _generalCharacter;
        private int _allocatedCount;
        private int _numWounded;
        private int _numKilled;
        private int _numRouted;

        public BattleSideEnum Side { get; }

        public int NumRemovedTroops => _numWounded + _numKilled + _numRouted;

        public int NumTroopsNotSupplied => Math.Max(0, _troops.Count - _allocatedCount);

        public bool AnyTroopRemainsToBeSupplied => _allocatedCount < _troops.Count;

        public ExactCampaignSnapshotTroopSupplier(BattleSideEnum side, bool isPlayerSide)
        {
            Side = side;
            _isPlayerSide = isPlayerSide;
        }

        public void Initialize(List<ExactCampaignSnapshotAgentOrigin> troops, BasicCharacterObject generalCharacter)
        {
            _troops = troops ?? new List<ExactCampaignSnapshotAgentOrigin>();
            _generalCharacter = generalCharacter;
            _allocatedCount = 0;
            _numWounded = 0;
            _numKilled = 0;
            _numRouted = 0;
        }

        public IEnumerable<IAgentOriginBase> SupplyTroops(int numberToAllocate)
        {
            if (numberToAllocate <= 0 || _allocatedCount >= _troops.Count)
                return Array.Empty<IAgentOriginBase>();

            int takeCount = Math.Min(numberToAllocate, _troops.Count - _allocatedCount);
            var supplied = new List<IAgentOriginBase>(takeCount);
            for (int i = 0; i < takeCount; i++)
            {
                supplied.Add(_troops[_allocatedCount]);
                _allocatedCount++;
            }

            return supplied;
        }

        public IAgentOriginBase SupplyOneTroop()
        {
            if (_allocatedCount >= _troops.Count)
                return null;

            ExactCampaignSnapshotAgentOrigin troop = _troops[_allocatedCount];
            _allocatedCount++;
            return troop;
        }

        public IEnumerable<IAgentOriginBase> GetAllTroops()
        {
            return _troops;
        }

        public BasicCharacterObject GetGeneralCharacter()
        {
            return _generalCharacter;
        }

        public int GetNumberOfPlayerControllableTroops()
        {
            return _isPlayerSide ? _troops.Count : 0;
        }

        internal void OnOriginWounded(ExactCampaignSnapshotAgentOrigin origin)
        {
            _numWounded++;
        }

        internal void OnOriginKilled(ExactCampaignSnapshotAgentOrigin origin)
        {
            _numKilled++;
        }

        internal void OnOriginRouted(ExactCampaignSnapshotAgentOrigin origin)
        {
            _numRouted++;
        }
    }

    public sealed class ExactCampaignSnapshotAgentOrigin : IAgentOriginBase
    {
        private readonly ExactCampaignSnapshotTroopSupplier _supplier;
        private readonly BasicCharacterObject _troop;
        private readonly bool _isUnderPlayersCommand;
        private readonly int _seed;
        private readonly bool _hasThrownWeapon;
        private readonly bool _hasHeavyArmor;
        private readonly bool _hasShield;
        private readonly bool _hasSpear;
        private readonly uint _factionColor;
        private readonly uint _factionColor2;
        private Banner _banner;
        private OriginRemovalState _removalState;

        private enum OriginRemovalState
        {
            Alive = 0,
            Wounded = 1,
            Killed = 2,
            Routed = 3
        }

        public string EntryId { get; }

        public string TroopId { get; }

        public BattleSideEnum Side { get; }

        public BasicCharacterObject Troop => _troop;

        private static int NormalizeNetworkSafeOriginSeed(int seed)
        {
            const int maxInclusive = 2000;
            int normalized = seed % (maxInclusive + 1);
            return normalized < 0 ? -normalized : normalized;
        }

        bool IAgentOriginBase.IsUnderPlayersCommand => _isUnderPlayersCommand;

        bool IAgentOriginBase.IsInSameArmyAsPlayer => _isUnderPlayersCommand;

        uint IAgentOriginBase.FactionColor => _factionColor;

        uint IAgentOriginBase.FactionColor2 => _factionColor2;

        IBattleCombatant IAgentOriginBase.BattleCombatant => null;

        int IAgentOriginBase.UniqueSeed => _seed;

        int IAgentOriginBase.Seed => _seed;

        Banner IAgentOriginBase.Banner => _banner;

        BasicCharacterObject IAgentOriginBase.Troop => _troop;

        bool IAgentOriginBase.HasThrownWeapon => _hasThrownWeapon;

        bool IAgentOriginBase.HasHeavyArmor => _hasHeavyArmor;

        bool IAgentOriginBase.HasShield => _hasShield;

        bool IAgentOriginBase.HasSpear => _hasSpear;

        public ExactCampaignSnapshotAgentOrigin(
            ExactCampaignSnapshotTroopSupplier supplier,
            BasicCharacterObject troop,
            string entryId,
            string troopId,
            BattleSideEnum side,
            bool isUnderPlayersCommand,
            uint factionColor,
            uint factionColor2,
            Banner banner,
            int seed)
        {
            _supplier = supplier;
            _troop = troop;
            EntryId = entryId ?? string.Empty;
            TroopId = troopId ?? troop?.StringId ?? string.Empty;
            Side = side;
            _isUnderPlayersCommand = isUnderPlayersCommand;
            _factionColor = factionColor;
            _factionColor2 = factionColor2;
            _banner = banner;
            _seed = NormalizeNetworkSafeOriginSeed(seed);
            AgentOriginUtilities.GetDefaultTroopTraits(_troop, out _hasThrownWeapon, out _hasSpear, out _hasShield, out _hasHeavyArmor);
        }

        void IAgentOriginBase.SetWounded()
        {
            if (!TryMarkRemoved(OriginRemovalState.Wounded))
                return;

            _supplier?.OnOriginWounded(this);
        }

        void IAgentOriginBase.SetKilled()
        {
            if (!TryMarkRemoved(OriginRemovalState.Killed))
                return;

            _supplier?.OnOriginKilled(this);
        }

        void IAgentOriginBase.SetRouted(bool isOrderRetreat)
        {
            if (!TryMarkRemoved(OriginRemovalState.Routed))
                return;

            _supplier?.OnOriginRouted(this);
        }

        void IAgentOriginBase.OnAgentRemoved(float agentHealth)
        {
        }

        void IAgentOriginBase.OnScoreHit(BasicCharacterObject victim, BasicCharacterObject formationCaptain, int damage, bool isFatal, bool isTeamKill, WeaponComponentData attackerWeapon)
        {
        }

        void IAgentOriginBase.SetBanner(Banner banner)
        {
            if (banner != null)
                _banner = banner;
        }

        TroopTraitsMask IAgentOriginBase.GetTraitsMask()
        {
            return AgentOriginUtilities.GetDefaultTraitsMask(this);
        }

        private bool TryMarkRemoved(OriginRemovalState targetState)
        {
            if (_removalState != OriginRemovalState.Alive)
                return false;

            _removalState = targetState;
            return true;
        }
    }
}
