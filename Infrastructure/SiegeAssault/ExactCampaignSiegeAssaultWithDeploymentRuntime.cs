using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using CoopSpectator.MissionBehaviors;
using CoopSpectator.Network.Messages;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Missions;
using TaleWorlds.MountAndBlade.Missions.Handlers;
using TaleWorlds.MountAndBlade.Objects.Siege;
using TaleWorlds.MountAndBlade.Source.Missions;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Infrastructure
{
    internal static class ExactCampaignSiegeAssaultWithDeploymentRuntime
    {
        private static readonly object Sync = new object();
        private static readonly FieldInfo DefaultMissionDeploymentPlanTeamDeploymentPlansField =
            typeof(DefaultMissionDeploymentPlan).GetField("_teamDeploymentPlans", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DeploymentPointWeaponsField =
            typeof(DeploymentPoint).GetField("_weapons", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DeploymentPointOnDeploymentStateChangedField =
            typeof(DeploymentPoint).GetField(
                "OnDeploymentStateChanged",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo DeploymentPointDetermineTypeMethod =
            typeof(DeploymentPoint).GetMethod("DetermineDeploymentPointType", BindingFlags.Instance | BindingFlags.NonPublic);
        private static Mission _activeMission;
        private static BattleSideEnum _activePlayerSide = BattleSideEnum.None;
        private static bool _deploymentPlanPrepared;
        private static bool _nativeSpawnContractApplied;
        private static bool _fieldMaterializedDeploymentLifecycleFinished;

        private sealed class CoopIdempotentSiegeDeploymentHandler : SiegeDeploymentHandler
        {
            private bool _afterStartApplied;

            public CoopIdempotentSiegeDeploymentHandler(bool isPlayerAttacker)
                : base(isPlayerAttacker)
            {
            }

            public override void AfterStart()
            {
                if (_afterStartApplied)
                    return;

                _afterStartApplied = true;
                base.AfterStart();
                ReplaceNativeDeploymentStateSubscribers();
            }

            public override void FinishDeployment()
            {
                RemoveCoopDeploymentStateSubscribers();
                base.FinishDeployment();
            }

            public override void OnRemoveBehavior()
            {
                RemoveCoopDeploymentStateSubscribers();
                base.OnRemoveBehavior();
            }

            private void ReplaceNativeDeploymentStateSubscribers()
            {
                if (DeploymentPointOnDeploymentStateChangedField == null ||
                    AllDeploymentPoints == null)
                {
                    return;
                }

                foreach (DeploymentPoint deploymentPoint in AllDeploymentPoints)
                {
                    if (deploymentPoint == null)
                        continue;

                    try
                    {
                        Delegate current = DeploymentPointOnDeploymentStateChangedField.GetValue(deploymentPoint) as Delegate;
                        if (current != null)
                        {
                            foreach (Delegate subscriber in current.GetInvocationList())
                            {
                                if (ReferenceEquals(subscriber.Target, this) &&
                                    subscriber.Method.Name == "OnDeploymentStateChange")
                                {
                                    current = Delegate.Remove(current, subscriber);
                                }
                            }
                        }

                        Action<DeploymentPoint, SynchedMissionObject> coopSubscriber = OnCoopDeploymentStateChange;
                        current = Delegate.Combine(current, coopSubscriber);
                        DeploymentPointOnDeploymentStateChangedField.SetValue(deploymentPoint, current);
                    }
                    catch
                    {
                    }
                }
            }

            private void RemoveCoopDeploymentStateSubscribers()
            {
                if (DeploymentPointOnDeploymentStateChangedField == null ||
                    AllDeploymentPoints == null)
                {
                    return;
                }

                foreach (DeploymentPoint deploymentPoint in AllDeploymentPoints)
                {
                    if (deploymentPoint == null)
                        continue;

                    try
                    {
                        Delegate current = DeploymentPointOnDeploymentStateChangedField.GetValue(deploymentPoint) as Delegate;
                        if (current == null)
                            continue;

                        Action<DeploymentPoint, SynchedMissionObject> coopSubscriber = OnCoopDeploymentStateChange;
                        current = Delegate.Remove(current, coopSubscriber);
                        DeploymentPointOnDeploymentStateChangedField.SetValue(deploymentPoint, current);
                    }
                    catch
                    {
                    }
                }
            }

            private void OnCoopDeploymentStateChange(
                DeploymentPoint deploymentPoint,
                SynchedMissionObject targetObject)
            {
                if (deploymentPoint == null)
                    return;

                TryCleanupDisbandedDetachment(deploymentPoint);

                if (targetObject is SiegeWeapon missionWeapon)
                    TrySyncSiegeWeaponController(deploymentPoint, missionWeapon);
            }

            private void TryCleanupDisbandedDetachment(DeploymentPoint deploymentPoint)
            {
                if (deploymentPoint == null || deploymentPoint.IsDeployed)
                    return;

                try
                {
                    Team team = GetTeamForSide(deploymentPoint.Side);
                    IDetachment disbandedDetachment = deploymentPoint.DisbandedWeapon as IDetachment;
                    if (team?.DetachmentManager != null &&
                        disbandedDetachment != null &&
                        team.DetachmentManager.ContainsDetachment(disbandedDetachment))
                    {
                        team.DetachmentManager.DestroyDetachment(disbandedDetachment);
                    }
                }
                catch
                {
                }
            }

            private void TrySyncSiegeWeaponController(DeploymentPoint deploymentPoint, SiegeWeapon missionWeapon)
            {
                if (deploymentPoint == null || missionWeapon == null)
                    return;

                try
                {
                    IMissionSiegeWeaponsController weaponsController =
                        Mission?.GetMissionBehavior<MissionSiegeEnginesLogic>()
                            ?.GetSiegeWeaponsController(deploymentPoint.Side);
                    if (weaponsController == null)
                        return;

                    if (deploymentPoint.IsDeployed)
                    {
                        weaponsController.OnWeaponDeployed(missionWeapon);
                    }
                    else
                    {
                        weaponsController.OnWeaponUndeployed(missionWeapon);
                    }
                }
                catch
                {
                }
            }

            private Team GetTeamForSide(BattleSideEnum side)
            {
                if (Mission == null)
                    return null;

                return side == BattleSideEnum.Defender
                    ? Mission.DefenderTeam
                    : side == BattleSideEnum.Attacker
                        ? Mission.AttackerTeam
                        : null;
            }
        }

        internal static SiegeDeploymentHandler CreateSiegeDeploymentHandler(bool isPlayerAttacker)
        {
            return new CoopIdempotentSiegeDeploymentHandler(isPlayerAttacker);
        }

        public static bool IsSiegeAssaultScenario(BattleScenarioContextMessage scenarioContext)
        {
            string siegeSubtype = scenarioContext?.SiegeContext?.SiegeSubtype ?? string.Empty;
            string missionShell = scenarioContext?.SiegeContext?.MissionShell ?? string.Empty;
            return scenarioContext?.IsSiegeBattle == true &&
                   string.Equals(siegeSubtype, "SiegeAssault", StringComparison.OrdinalIgnoreCase) &&
                   CampaignMissionShellRuntimeState.IsWithDeploymentMissionShell(missionShell);
        }

        public static float[] ResolveIntactWallHitPointRatiosForScenePreparation(
            Mission mission,
            BattleScenarioContextMessage scenarioContext,
            out string diagnostics)
        {
            List<float> rawRatios = scenarioContext?.SiegeContext?.WallHitPointRatios?
                .Where(value => !float.IsNaN(value) && !float.IsInfinity(value))
                .ToList() ?? new List<float>();

            int activeMissionObjectCount = CountActiveMissionObjects(mission);
            int breakableWallCount = CountBreakableSiegeWallSegments(mission);
            if (breakableWallCount > 0 && breakableWallCount <= 2)
            {
                float[] outputRatios = CreateSceneAlignedWallHitPointRatios(rawRatios, breakableWallCount);
                diagnostics =
                    "campaign-ratios Source=Scene" +
                    " RawCount=" + rawRatios.Count +
                    " ActiveMissionObjects=" + activeMissionObjectCount +
                    " BreakableWallCount=" + breakableWallCount +
                    " OutputCount=" + outputRatios.Length +
                    " RawValues=[" + FormatRatioList(rawRatios) + "]" +
                    " OutputValues=[" + FormatRatioList(outputRatios) + "]";
                return outputRatios;
            }

            if (breakableWallCount > 2)
            {
                diagnostics =
                    "cleared-for-native-safety Source=Scene" +
                    " RawCount=" + rawRatios.Count +
                    " ActiveMissionObjects=" + activeMissionObjectCount +
                    " BreakableWallCount=" + breakableWallCount +
                    " NativeLimit=2";
                return Array.Empty<float>();
            }

            if (activeMissionObjectCount <= 0 && rawRatios.Count > 0 && rawRatios.Count <= 2)
            {
                float[] outputRatios = CreateWallHitPointRatios(rawRatios);
                diagnostics =
                    "campaign-ratios Source=SnapshotFallback" +
                    " RawCount=" + rawRatios.Count +
                    " ActiveMissionObjects=" + activeMissionObjectCount +
                    " BreakableWallCount=" + breakableWallCount +
                    " OutputCount=" + outputRatios.Length +
                    " RawValues=[" + FormatRatioList(rawRatios) + "]" +
                    " OutputValues=[" + FormatRatioList(outputRatios) + "]";
                return outputRatios;
            }

            diagnostics =
                "empty-for-native-safety" +
                " RawCount=" + rawRatios.Count +
                " ActiveMissionObjects=" + activeMissionObjectCount +
                " BreakableWallCount=" + breakableWallCount +
                " RawValues=[" + FormatRatioList(rawRatios) + "]";
            return Array.Empty<float>();
        }

        private static float[] CreateSceneAlignedWallHitPointRatios(List<float> rawRatios, int count)
        {
            if (count <= 0)
                return Array.Empty<float>();

            var ratios = new float[count];
            for (int i = 0; i < ratios.Length; i++)
            {
                ratios[i] = i < rawRatios.Count
                    ? ClampWallHitPointRatio(rawRatios[i])
                    : 1f;
            }

            return ratios;
        }

        private static float[] CreateWallHitPointRatios(List<float> rawRatios)
        {
            if (rawRatios == null || rawRatios.Count <= 0)
                return Array.Empty<float>();

            var ratios = new float[rawRatios.Count];
            for (int i = 0; i < ratios.Length; i++)
                ratios[i] = ClampWallHitPointRatio(rawRatios[i]);

            return ratios;
        }

        private static float ClampWallHitPointRatio(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 1f;

            if (value < 0f)
                return 0f;

            if (value > 1f)
                return 1f;

            return value;
        }

        private static string FormatRatioList(IEnumerable<float> ratios)
        {
            if (ratios == null)
                return string.Empty;

            return string.Join(
                ",",
                ratios.Select(value => ClampWallHitPointRatio(value).ToString("0.###", CultureInfo.InvariantCulture)));
        }

        private static int CountActiveMissionObjects(Mission mission)
        {
            if (mission?.ActiveMissionObjects == null)
                return 0;

            try
            {
                return mission.ActiveMissionObjects.Count;
            }
            catch
            {
                return 0;
            }
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

        public static bool ShouldInjectWrappedBattleClientDeploymentBehaviors(
            Mission mission,
            BattleScenarioContextMessage scenarioContext,
            out string diagnostics)
        {
            if (mission == null)
            {
                diagnostics = "mission-null";
                return false;
            }

            if (!IsSiegeAssaultScenario(scenarioContext))
            {
                diagnostics = "not-siege-assault-with-deployment";
                return false;
            }

            if (GameNetwork.IsDedicatedServer)
            {
                diagnostics = "disabled-on-dedicated";
                return false;
            }

            if (!GameNetwork.IsClient || GameNetwork.IsServer)
            {
                diagnostics = "disabled-outside-remote-client-runtime";
                return false;
            }

            if (!SiegeAssaultMissionOpenBridge.ShouldAllowWrappedBattleDeploymentBridge(
                    mission,
                    scenarioContext,
                    out string bridgeDiagnostics))
            {
                diagnostics = bridgeDiagnostics;
                return false;
            }

            diagnostics = "enabled-for-exact-siege-assault-with-deployment-remote-client " + bridgeDiagnostics;
            return true;
        }

        public static bool ShouldMountLiveDeploymentControllers(
            Mission mission,
            out string diagnostics)
        {
            if (mission == null)
            {
                diagnostics = "mission-null";
                return false;
            }

            if (GameNetwork.IsClient && !GameNetwork.IsServer)
            {
                diagnostics = "suppressed-remote-client-pre-commander-selection";
                return false;
            }

            if (ExperimentalFeatures.EnableSiegeReplayFieldMaterializedArmyRuntime &&
                SceneRuntimeClassifier.IsExactSiegeAssaultWithDeploymentScene(mission.SceneName ?? string.Empty))
            {
                diagnostics = "suppressed-field-materialized-runtime-no-initial-player-agent";
                return false;
            }

            if (!SiegeAssaultMissionOpenBridge.ShouldAllowLiveDeploymentControllers(
                    mission,
                    out string bridgeDiagnostics))
            {
                diagnostics = bridgeDiagnostics;
                return false;
            }

            diagnostics = "enabled " + bridgeDiagnostics;
            return true;
        }

        public static bool TryEnsureMissionBehaviorContract(
            Mission mission,
            BattleScenarioContextMessage scenarioContext,
            BattleSideEnum playerSide,
            out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            bool isPlayerAttacker = playerSide == BattleSideEnum.Attacker;
            bool allowMissionBehaviorCreation = !IsMissionBehaviorMutationUnsafe(mission);
            bool shouldMountLiveDeploymentControllers =
                ShouldMountLiveDeploymentControllers(mission, out string liveDeploymentControllerPolicy);

            if (!TryEnsureMissionSiegeEnginesLogicBehavior(
                    mission,
                    scenarioContext,
                    out string siegeEnginesDiagnostics,
                    allowMissionBehaviorCreation))
            {
                diagnostics = "MissionSiegeEnginesLogic={" + siegeEnginesDiagnostics + "}";
                return false;
            }

            if (!shouldMountLiveDeploymentControllers)
            {
                diagnostics =
                    "MissionSiegeEnginesLogic={" + siegeEnginesDiagnostics + "} " +
                    "SiegeDeploymentHandler={" +
                    BuildSuppressedMissionBehaviorDiagnostics(
                        mission.GetMissionBehavior<SiegeDeploymentHandler>(),
                        liveDeploymentControllerPolicy) +
                    "} " +
                    "SiegeDeploymentMissionController={" +
                    BuildSuppressedMissionBehaviorDiagnostics(
                        mission.GetMissionBehavior<SiegeDeploymentMissionController>(),
                        liveDeploymentControllerPolicy) +
                    "}";
                return true;
            }

            if (!TryEnsureMissionBehaviorAvailable(
                    mission,
                    mission.GetMissionBehavior<SiegeDeploymentHandler>(),
                    () => CreateSiegeDeploymentHandler(isPlayerAttacker),
                    "SiegeDeploymentHandler",
                    out string deploymentHandlerDiagnostics,
                    allowMissionBehaviorCreation))
            {
                diagnostics =
                    "MissionSiegeEnginesLogic={" + siegeEnginesDiagnostics + "} " +
                    "SiegeDeploymentHandler={" + deploymentHandlerDiagnostics + "}";
                return false;
            }

            if (!TryEnsureMissionBehaviorAvailable(
                    mission,
                    mission.GetMissionBehavior<SiegeDeploymentMissionController>(),
                    () => new SiegeDeploymentMissionController(isPlayerAttacker),
                    "SiegeDeploymentMissionController",
                    out string deploymentControllerDiagnostics,
                    allowMissionBehaviorCreation))
            {
                diagnostics =
                    "MissionSiegeEnginesLogic={" + siegeEnginesDiagnostics + "} " +
                    "SiegeDeploymentHandler={" + deploymentHandlerDiagnostics + "} " +
                    "SiegeDeploymentMissionController={" + deploymentControllerDiagnostics + "}";
                return false;
            }

            diagnostics =
                "MissionSiegeEnginesLogic={" + siegeEnginesDiagnostics + "} " +
                "SiegeDeploymentHandler={" + deploymentHandlerDiagnostics + "} " +
                "SiegeDeploymentMissionController={" + deploymentControllerDiagnostics + "}";
            return true;
        }

        public static bool TryEnsureCommanderDeploymentUiContract(
            Mission mission,
            BattleScenarioContextMessage scenarioContext,
            BattleSideEnum commanderSide,
            out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            if (commanderSide == BattleSideEnum.None)
            {
                diagnostics = "commander-side-none";
                return false;
            }

            if (!IsSiegeAssaultScenario(scenarioContext))
            {
                diagnostics = "not-siege-assault-with-deployment";
                return false;
            }

            bool isCommanderAttacker = commanderSide == BattleSideEnum.Attacker;
            if (GameNetwork.IsClient && !GameNetwork.IsServer)
            {
                bool hasSiegeEnginesLogic = mission.GetMissionBehavior<MissionSiegeEnginesLogic>() != null;
                bool preparedDeploymentPoints = TryPrepareClientDeploymentPointsForCommanderUi(
                    mission,
                    commanderSide,
                    out string remoteDeploymentPointDiagnostics);
                diagnostics =
                    "MissionSiegeEnginesLogic={Existing=" + hasSiegeEnginesLogic + " Created=False Reason=remote-client-ui-readonly} " +
                    "SiegeDeploymentHandler={Skipped=True Reason=remote-client-ui-readonly} " +
                    "SiegeDeploymentMissionController={Skipped=True Reason=ui-handler-only} " +
                    "DeploymentPoints={" + remoteDeploymentPointDiagnostics + "}";
                return hasSiegeEnginesLogic && preparedDeploymentPoints;
            }

            bool allowMissionBehaviorCreation = !IsMissionBehaviorMutationUnsafe(mission);
            if (!TryEnsureMissionSiegeEnginesLogicBehavior(
                    mission,
                    scenarioContext,
                    out string siegeEnginesDiagnostics,
                    allowMissionBehaviorCreation))
            {
                diagnostics = "MissionSiegeEnginesLogic={" + siegeEnginesDiagnostics + "}";
                return false;
            }

            bool ensuredDeploymentHandler = TryEnsureMissionBehaviorAvailable(
                mission,
                mission.GetMissionBehavior<SiegeDeploymentHandler>(),
                () => CreateSiegeDeploymentHandler(isCommanderAttacker),
                "SiegeDeploymentHandler",
                out string deploymentHandlerDiagnostics,
                allowMissionBehaviorCreation);

            TryPrepareClientDeploymentPointsForCommanderUi(
                mission,
                commanderSide,
                out string deploymentPointDiagnostics);

            diagnostics =
                "MissionSiegeEnginesLogic={" + siegeEnginesDiagnostics + "} " +
                "SiegeDeploymentHandler={" + deploymentHandlerDiagnostics + "} " +
                "SiegeDeploymentMissionController={Skipped=True Reason=ui-handler-only} " +
                "DeploymentPoints={" + deploymentPointDiagnostics + "}";
            return ensuredDeploymentHandler;
        }

        public static bool TryCreateMissionSiegeEnginesLogicBehavior(
            BattleScenarioContextMessage scenarioContext,
            out MissionSiegeEnginesLogic behavior,
            out string diagnostics)
        {
            behavior = null;
            if (!TryBuildMissionSiegeWeaponLists(
                    scenarioContext,
                    out List<MissionSiegeWeapon> attackerSiegeWeapons,
                    out List<MissionSiegeWeapon> defenderSiegeWeapons,
                    out string siegeWeaponDiagnostics))
            {
                diagnostics = siegeWeaponDiagnostics ?? "siege-weapon-list-build-failed";
                return false;
            }

            try
            {
                behavior = new MissionSiegeEnginesLogic(defenderSiegeWeapons, attackerSiegeWeapons);
                diagnostics =
                    "Created=True " +
                    "AttackerWeapons=" + attackerSiegeWeapons.Count +
                    " DefenderWeapons=" + defenderSiegeWeapons.Count +
                    " Source={" + siegeWeaponDiagnostics + "}";
                return true;
            }
            catch (Exception ex)
            {
                diagnostics =
                    "mission-siege-engines-logic-create-faulted " +
                    ex.GetType().Name + ":" + ex.Message +
                    " Source={" + siegeWeaponDiagnostics + "}";
                behavior = null;
                return false;
            }
        }

        public static void ResetRuntimeState(Mission mission, string source)
        {
            lock (Sync)
            {
                if (mission != null &&
                    _activeMission != null &&
                    !ReferenceEquals(_activeMission, mission))
                {
                    return;
                }

                string clearedMissionScene = _activeMission?.SceneName ?? "null";
                BattleSideEnum clearedPlayerSide = _activePlayerSide;
                bool hadPreparedPlan = _deploymentPlanPrepared;
                bool hadAppliedSpawnContract = _nativeSpawnContractApplied;

                _activeMission = null;
                _activePlayerSide = BattleSideEnum.None;
                _deploymentPlanPrepared = false;
                _nativeSpawnContractApplied = false;
                _fieldMaterializedDeploymentLifecycleFinished = false;

                if (hadPreparedPlan || hadAppliedSpawnContract)
                {
                    ModLogger.Info(
                        "ExactCampaignSiegeAssaultWithDeploymentRuntime: cleared deployment runtime state. " +
                        "Scene=" + clearedMissionScene +
                        " PlayerSide=" + clearedPlayerSide +
                        " HadPreparedPlan=" + hadPreparedPlan +
                        " HadAppliedSpawnContract=" + hadAppliedSpawnContract +
                        " Source=" + (source ?? "unknown") + ".");
                }
            }
        }

        public static bool IsDeploymentRuntimeActive(Mission mission)
        {
            if (mission == null)
                return false;

            lock (Sync)
            {
                return ReferenceEquals(_activeMission, mission) &&
                       _deploymentPlanPrepared &&
                       _nativeSpawnContractApplied;
            }
        }

        public static bool IsDeploymentPhaseBlockingBattleStart(Mission mission)
        {
            return mission != null &&
                   IsDeploymentRuntimeActive(mission) &&
                   !HasDeploymentLifecycleFinished(mission);
        }

        public static bool HasDeploymentLifecycleFinished(Mission mission)
        {
            if (mission == null || !IsDeploymentRuntimeActive(mission))
                return false;

            lock (Sync)
            {
                if (ReferenceEquals(_activeMission, mission) &&
                    _fieldMaterializedDeploymentLifecycleFinished)
                {
                    return true;
                }
            }

            try
            {
                return mission.IsDeploymentFinished;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsCoopCommanderDeploymentAutoDeployWindowActive(
            Mission mission,
            out string diagnostics)
        {
            diagnostics = "commander-window-inactive";
            if (mission == null)
            {
                diagnostics = "commander-window-mission-null";
                return false;
            }

            if (!IsDeploymentRuntimeActive(mission))
            {
                diagnostics = "commander-window-runtime-inactive";
                return false;
            }

            CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();
            if (currentPhase < CoopBattlePhase.SideSelection)
            {
                diagnostics = "commander-window-phase-too-early Phase=" + currentPhase;
                return false;
            }

            if (currentPhase >= CoopBattlePhase.BattleActive)
            {
                diagnostics = "commander-window-battle-active Phase=" + currentPhase;
                return false;
            }

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetScenarioContext() ??
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            if (!IsSiegeAssaultScenario(scenarioContext))
            {
                diagnostics = "commander-window-not-siege-assault Phase=" + currentPhase;
                return false;
            }

            bool hasSiegeDeploymentHandler = false;
            bool hasDeploymentHandler = false;
            bool hasSiegeEnginesLogic = false;
            try
            {
                hasSiegeDeploymentHandler = mission.GetMissionBehavior<SiegeDeploymentHandler>() != null;
                hasDeploymentHandler = mission.GetMissionBehavior<DeploymentHandler>() != null;
                hasSiegeEnginesLogic = mission.GetMissionBehavior<MissionSiegeEnginesLogic>() != null;
            }
            catch (Exception ex)
            {
                diagnostics =
                    "commander-window-handler-check-failed " +
                    ex.GetType().Name + ":" + ex.Message +
                    " Phase=" + currentPhase;
                return false;
            }

            if (!hasSiegeDeploymentHandler && !hasDeploymentHandler && !hasSiegeEnginesLogic)
            {
                diagnostics =
                    "commander-window-deployment-runtime-pieces-missing" +
                    " Phase=" + currentPhase +
                    " HasSiegeDeploymentHandler=" + hasSiegeDeploymentHandler +
                    " HasDeploymentHandler=" + hasDeploymentHandler +
                    " HasSiegeEnginesLogic=" + hasSiegeEnginesLogic;
                return false;
            }

            diagnostics =
                "native-finished-but-coop-commander-window-active" +
                " Phase=" + currentPhase +
                " HasSiegeDeploymentHandler=" + hasSiegeDeploymentHandler +
                " HasDeploymentHandler=" + hasDeploymentHandler +
                " HasSiegeEnginesLogic=" + hasSiegeEnginesLogic;
            return true;
        }

        public static bool ShouldTreatAllowedPrebattleSelectableSourceAsReady(
            Mission mission,
            BattleSideEnum side,
            CoopBattlePhase currentPhase,
            string selectableSource)
        {
            if (mission == null ||
                side == BattleSideEnum.None ||
                currentPhase < CoopBattlePhase.SideSelection ||
                currentPhase >= CoopBattlePhase.BattleActive ||
                string.IsNullOrWhiteSpace(selectableSource) ||
                !selectableSource.StartsWith("allowed-prebattle", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return IsDeploymentRuntimeActive(mission);
        }

        public static bool ShouldBlockPeerRespawnUntilBattleActive(
            Mission mission,
            CoopBattlePhase currentPhase)
        {
            return mission != null &&
                   currentPhase < CoopBattlePhase.BattleActive &&
                   IsDeploymentRuntimeActive(mission) &&
                   !HasDeploymentLifecycleFinished(mission);
        }

        public static bool TryForceAutoDeployAndFinishDeployment(
            Mission mission,
            out string diagnostics)
        {
            return TryAutoDeployDeployment(
                mission,
                finishDeployment: true,
                out diagnostics);
        }

        public static bool TryAutoDeployDeploymentOnly(
            Mission mission,
            out string diagnostics)
        {
            return TryAutoDeployDeployment(
                mission,
                finishDeployment: false,
                out diagnostics);
        }

        public static bool TryAutoDeployDeploymentOnly(
            Mission mission,
            BattleSideEnum side,
            out string diagnostics)
        {
            return TryAutoDeployDeploymentForSide(
                mission,
                side,
                treatSideAsPlayerSide: true,
                out diagnostics);
        }

        private static bool TryAutoDeployDeploymentForSide(
            Mission mission,
            BattleSideEnum side,
            bool treatSideAsPlayerSide,
            out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            if (side == BattleSideEnum.None)
            {
                diagnostics = "side-none";
                return false;
            }

            if (!IsDeploymentRuntimeActive(mission))
            {
                diagnostics = "deployment-runtime-inactive";
                return false;
            }

            string lifecycleDiagnostics = "deployment-lifecycle-open";
            if (HasDeploymentLifecycleFinished(mission))
            {
                if (!IsCoopCommanderDeploymentAutoDeployWindowActive(
                        mission,
                        out lifecycleDiagnostics))
                {
                    diagnostics = "deployment-already-finished " + lifecycleDiagnostics;
                    return false;
                }
            }

            Team battleTeam = mission.Teams?
                .FirstOrDefault(team => IsBattleDeploymentTeam(mission, team) && team.Side == side);
            if (battleTeam == null)
            {
                diagnostics = "battle-team-missing Side=" + side;
                return false;
            }

            string deploymentPlanDiagnostics = "plan-not-remade";
            if (mission.GetDeploymentPlan<IMissionDeploymentPlan>(out IMissionDeploymentPlan deploymentPlan) &&
                deploymentPlan != null)
            {
                try
                {
                    deploymentPlan.RemakeDeploymentPlan(battleTeam);
                    deploymentPlanDiagnostics = "remade " + DescribeDeploymentTeam(battleTeam);
                }
                catch (Exception ex)
                {
                    deploymentPlanDiagnostics =
                        "faulted " +
                        DescribeDeploymentTeam(battleTeam) +
                        " " +
                        ex.GetType().Name + ":" + ex.Message;
                }
            }
            else
            {
                deploymentPlanDiagnostics = "plan-missing";
            }

            SiegeDeploymentHandler siegeDeploymentHandler = mission.GetMissionBehavior<SiegeDeploymentHandler>();
            DeploymentHandler deploymentHandler = mission.GetMissionBehavior<DeploymentHandler>();

            bool autoDeployedTeam = false;
            bool forceUpdatedUnits = false;
            bool siegeMachinesDeployed = false;
            string siegeMachineDiagnostics = string.Empty;
            bool siegeMachineStatePublished = false;
            string siegeMachineStatePublishDiagnostics = string.Empty;
            string teamDeploymentDiagnostics = string.Empty;
            try
            {
                siegeMachinesDeployed = CoopSiegeMachineDeploymentController.TryAutoDeploySide(
                    mission,
                    battleTeam,
                    siegeDeploymentHandler,
                    treatSideAsPlayerSide,
                    CoopDebugConfig.OrderOfBattleDiagnostics,
                    out siegeMachineDiagnostics);
                if (siegeMachinesDeployed)
                {
                    siegeMachineStatePublished = CoopMissionNetworkBridge.TryBroadcastCommanderDeploymentSiegeMachineState(
                        mission,
                        battleTeam.Side,
                        out siegeMachineStatePublishDiagnostics,
                        "ExactCampaignSiegeAssaultWithDeploymentRuntime.TryAutoDeployDeploymentForSide");
                }

                if (siegeDeploymentHandler != null)
                {
                    siegeDeploymentHandler.AutoDeployTeamUsingTeamAI(battleTeam);
                    autoDeployedTeam = true;
                    ForceUpdateDeploymentTeamUnits(battleTeam);
                    forceUpdatedUnits = true;
                    teamDeploymentDiagnostics = "siege-team-ai";
                }
                else if (deploymentHandler != null)
                {
                    deploymentHandler.AutoDeployTeamUsingDeploymentPlan(battleTeam);
                    autoDeployedTeam = true;
                    ForceUpdateDeploymentTeamUnits(battleTeam);
                    forceUpdatedUnits = true;
                    teamDeploymentDiagnostics = "deployment-plan";
                }
                else
                {
                    if (siegeMachinesDeployed &&
                        !ShouldUseDedicatedFieldMaterializedSiegeMachineStateOnly(mission))
                    {
                        ForceUpdateDeploymentTeamUnits(battleTeam);
                        forceUpdatedUnits = true;
                    }

                    teamDeploymentDiagnostics = "deployment-handler-missing-machine-only";
                }
            }
            catch (Exception ex)
            {
                diagnostics =
                    "auto-deploy-side-faulted " +
                    ex.GetType().Name + ":" + ex.Message +
                    " Side=" + side +
                    " Team=" + DescribeDeploymentTeam(battleTeam) +
                    " Lifecycle={" + lifecycleDiagnostics + "}" +
                    " Plan={" + deploymentPlanDiagnostics + "}" +
                    " SiegeMachines={" + siegeMachineDiagnostics + "}" +
                    " SiegeMachineStatePublished=" + siegeMachineStatePublished +
                    " SiegeMachineStatePublish={" + siegeMachineStatePublishDiagnostics + "}" +
                    " TeamDeployment={" + teamDeploymentDiagnostics + "}";
                return false;
            }

            if (!siegeMachinesDeployed)
            {
                diagnostics =
                    "auto-deploy-side-siege-machines-failed " +
                    "Side=" + side +
                    " Team=" + DescribeDeploymentTeam(battleTeam) +
                    " TreatSideAsPlayerSide=" + treatSideAsPlayerSide +
                    " Lifecycle={" + lifecycleDiagnostics + "}" +
                    " Plan={" + deploymentPlanDiagnostics + "}" +
                    " SiegeMachines={" + siegeMachineDiagnostics + "}" +
                    " HasSiegeDeploymentHandler=" + (siegeDeploymentHandler != null) +
                    " HasDeploymentHandler=" + (deploymentHandler != null) +
                    " TeamDeployment={" + teamDeploymentDiagnostics + "}" +
                    " AutoDeployedTeam=" + autoDeployedTeam;
                return false;
            }

            diagnostics =
                "Side=" + side +
                " Team=" + DescribeDeploymentTeam(battleTeam) +
                " TreatSideAsPlayerSide=" + treatSideAsPlayerSide +
                " Lifecycle={" + lifecycleDiagnostics + "}" +
                " Plan={" + deploymentPlanDiagnostics + "}" +
                " SiegeMachinesDeployed=" + siegeMachinesDeployed +
                " SiegeMachines={" + siegeMachineDiagnostics + "}" +
                " SiegeMachineStatePublished=" + siegeMachineStatePublished +
                " SiegeMachineStatePublish={" + siegeMachineStatePublishDiagnostics + "}" +
                " AutoDeployedTeam=" + autoDeployedTeam +
                " HasSiegeDeploymentHandler=" + (siegeDeploymentHandler != null) +
                " HasDeploymentHandler=" + (deploymentHandler != null) +
                " TeamDeployment={" + teamDeploymentDiagnostics + "}" +
                " ForceUpdatedUnits=" + forceUpdatedUnits;
            return autoDeployedTeam || siegeMachinesDeployed;
        }

        private static bool ShouldUseDedicatedFieldMaterializedSiegeMachineStateOnly(Mission mission)
        {
            // Siege machines need the controlled native deployment steps to become usable.
            return false;
        }

        public static bool TryEnsureAutoDeployedSiegeMachinesBeforeBattleStart(
            Mission mission,
            out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            if (!IsDeploymentRuntimeActive(mission))
            {
                diagnostics = "not-required-runtime-inactive";
                return true;
            }

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetScenarioContext() ??
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            if (!IsSiegeAssaultScenario(scenarioContext))
            {
                diagnostics = "not-required-not-siege-assault-with-deployment";
                return true;
            }

            List<Team> battleTeams = CollectBattleDeploymentTeams(mission);
            if (battleTeams.Count <= 0)
            {
                diagnostics = "battle-teams-missing";
                return false;
            }

            return TryAutoDeploySiegeMachinesForTeams(
                mission,
                battleTeams,
                mission.GetMissionBehavior<SiegeDeploymentHandler>(),
                out diagnostics);
        }

        private static bool TryAutoDeploySiegeMachinesForTeams(
            Mission mission,
            List<Team> battleTeams,
            SiegeDeploymentHandler siegeDeploymentHandler,
            out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            if (battleTeams == null || battleTeams.Count <= 0)
            {
                diagnostics = "battle-teams-missing";
                return false;
            }

            var details = new List<string>();
            int attemptedCount = 0;
            int succeededCount = 0;
            int failedCount = 0;
            foreach (Team battleTeam in battleTeams)
            {
                if (battleTeam == null || battleTeam.Side == BattleSideEnum.None)
                    continue;

                attemptedCount++;
                bool treatSideAsPlayerSide =
                    mission.PlayerTeam != null &&
                    battleTeam.Side == mission.PlayerTeam.Side;
                bool deployed = CoopSiegeMachineDeploymentController.TryAutoDeploySide(
                    mission,
                    battleTeam,
                    siegeDeploymentHandler,
                    treatSideAsPlayerSide,
                    CoopDebugConfig.OrderOfBattleDiagnostics,
                    out string teamDiagnostics);
                bool statePublished = false;
                string statePublishDiagnostics = string.Empty;
                if (deployed)
                {
                    statePublished = CoopMissionNetworkBridge.TryBroadcastCommanderDeploymentSiegeMachineState(
                        mission,
                        battleTeam.Side,
                        out statePublishDiagnostics,
                        "ExactCampaignSiegeAssaultWithDeploymentRuntime.TryAutoDeploySiegeMachinesForTeams");
                }

                if (deployed)
                    succeededCount++;
                else
                    failedCount++;

                details.Add(
                    DescribeDeploymentTeam(battleTeam) +
                    ":TreatSideAsPlayerSide=" + treatSideAsPlayerSide +
                    ":Deployed=" + deployed +
                    ":StatePublished=" + statePublished +
                    ":StatePublish={" + statePublishDiagnostics + "}" +
                    ":{" + (teamDiagnostics ?? string.Empty) + "}");
            }

            diagnostics =
                "Attempted=" + attemptedCount +
                " Succeeded=" + succeededCount +
                " Failed=" + failedCount +
                " HasSiegeDeploymentHandler=" + (siegeDeploymentHandler != null) +
                " Details=[" + string.Join("; ", details.ToArray()) + "]";
            return attemptedCount > 0 && failedCount == 0;
        }

        private static List<Team> CollectBattleDeploymentTeams(Mission mission)
        {
            Team playerTeam = mission?.PlayerTeam;
            return mission?.Teams?
                .Where(team => IsBattleDeploymentTeam(mission, team))
                .OrderBy(team => playerTeam != null && ReferenceEquals(team, playerTeam) ? 1 : 0)
                .ThenBy(team => team.TeamIndex)
                .ToList() ?? new List<Team>();
        }

        private static bool TryAutoDeployDeployment(
            Mission mission,
            bool finishDeployment,
            out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            if (!IsDeploymentRuntimeActive(mission))
            {
                diagnostics = "deployment-runtime-inactive";
                return false;
            }

            if (HasDeploymentLifecycleFinished(mission))
            {
                bool siegeMachinesReady = TryEnsureAutoDeployedSiegeMachinesBeforeBattleStart(
                    mission,
                    out string alreadyFinishedSiegeMachineDiagnostics);
                diagnostics =
                    "deployment-already-finished" +
                    " SiegeMachinesReady=" + siegeMachinesReady +
                    " SiegeMachines={" + alreadyFinishedSiegeMachineDiagnostics + "}";
                return finishDeployment && siegeMachinesReady;
            }

            Team playerTeam = mission.PlayerTeam;
            if (playerTeam == null || playerTeam.Side == BattleSideEnum.None)
            {
                diagnostics = "player-team-missing";
                return false;
            }

            List<Team> battleTeams = CollectBattleDeploymentTeams(mission);
            if (battleTeams.Count <= 0)
            {
                diagnostics = "battle-teams-missing PlayerTeamSide=" + playerTeam.Side;
                return false;
            }

            string deploymentPlanDiagnostics = "plan-not-remade";
            if (mission.GetDeploymentPlan<IMissionDeploymentPlan>(out IMissionDeploymentPlan deploymentPlan) &&
                deploymentPlan != null)
            {
                List<string> planResults = new List<string>();
                foreach (Team battleTeam in battleTeams)
                {
                    try
                    {
                        deploymentPlan.RemakeDeploymentPlan(battleTeam);
                        planResults.Add(DescribeDeploymentTeam(battleTeam) + ":remade");
                    }
                    catch (Exception ex)
                    {
                        planResults.Add(
                            DescribeDeploymentTeam(battleTeam) +
                            ":faulted " +
                            ex.GetType().Name + ":" + ex.Message);
                    }
                }

                deploymentPlanDiagnostics = "plans={" + string.Join(", ", planResults) + "}";
            }
            else
            {
                deploymentPlanDiagnostics = "plan-missing";
            }

            SiegeDeploymentHandler siegeDeploymentHandler = mission.GetMissionBehavior<SiegeDeploymentHandler>();
            DeploymentHandler deploymentHandler = mission.GetMissionBehavior<DeploymentHandler>();
            bool stateOnlyFieldMaterializedDeployment =
                ShouldUseDedicatedFieldMaterializedSiegeMachineStateOnly(mission);

            int autoDeployedTeamCount = 0;
            bool forceUpdatedUnits = false;
            bool finishedDeployment = false;
            bool playerSiegeWeaponsDeployed = false;
            bool aiSiegeWeaponsDeployed = false;
            bool controlledSiegeMachinesDeployed = false;
            string controlledSiegeMachineDiagnostics = string.Empty;
            string deploymentDiagnostics = string.Empty;
            List<string> autoDeployedTeams = new List<string>();
            List<string> nonFatalDeploymentFaults = new List<string>();

            try
            {
                if (siegeDeploymentHandler != null)
                {
                    try
                    {
                        siegeDeploymentHandler.DeployAllSiegeWeaponsOfPlayer();
                        playerSiegeWeaponsDeployed = true;
                    }
                    catch (Exception ex)
                    {
                        nonFatalDeploymentFaults.Add(
                            "player-siege-weapons " +
                            ex.GetType().Name + ":" + ex.Message);
                    }

                    try
                    {
                        siegeDeploymentHandler.DeployAllSiegeWeaponsOfAi();
                        aiSiegeWeaponsDeployed = true;
                    }
                    catch (Exception ex)
                    {
                        nonFatalDeploymentFaults.Add(
                            "ai-siege-weapons " +
                            ex.GetType().Name + ":" + ex.Message);
                    }

                    foreach (Team battleTeam in battleTeams)
                    {
                        siegeDeploymentHandler.AutoDeployTeamUsingTeamAI(battleTeam);
                        autoDeployedTeams.Add(DescribeDeploymentTeam(battleTeam));
                        autoDeployedTeamCount++;
                    }

                    siegeDeploymentHandler.ForceUpdateAllUnits();
                    if (finishDeployment)
                        siegeDeploymentHandler.FinishDeployment();
                    forceUpdatedUnits = true;
                    finishedDeployment = finishDeployment && HasDeploymentLifecycleFinished(mission);
                    deploymentDiagnostics = "siege-handler-auto-deployed-all-battle-teams";
                }
                else if (deploymentHandler != null)
                {
                    foreach (Team battleTeam in battleTeams)
                    {
                        deploymentHandler.AutoDeployTeamUsingDeploymentPlan(battleTeam);
                        autoDeployedTeams.Add(DescribeDeploymentTeam(battleTeam));
                        autoDeployedTeamCount++;
                    }

                    deploymentHandler.ForceUpdateAllUnits();
                    if (finishDeployment)
                        deploymentHandler.FinishDeployment();
                    forceUpdatedUnits = true;
                    finishedDeployment = finishDeployment && HasDeploymentLifecycleFinished(mission);
                    deploymentDiagnostics = "deployment-handler-auto-deployed-all-battle-teams";
                }
                else
                {
                    controlledSiegeMachinesDeployed = TryAutoDeploySiegeMachinesForTeams(
                        mission,
                        battleTeams,
                        siegeDeploymentHandler,
                        out controlledSiegeMachineDiagnostics);
                    if (!controlledSiegeMachinesDeployed &&
                        !stateOnlyFieldMaterializedDeployment)
                    {
                        diagnostics =
                            "deployment-handler-missing-controlled-siege-machine-auto-deploy-failed " +
                            "Plan={" + deploymentPlanDiagnostics + "} " +
                            "SiegeMachines={" + controlledSiegeMachineDiagnostics + "}";
                        return false;
                    }

                    if (!stateOnlyFieldMaterializedDeployment)
                    {
                        foreach (Team battleTeam in battleTeams)
                        {
                            ForceUpdateDeploymentTeamUnits(battleTeam);
                        }

                        forceUpdatedUnits = true;
                    }

                    if (finishDeployment &&
                        (stateOnlyFieldMaterializedDeployment || controlledSiegeMachinesDeployed))
                    {
                        MarkFieldMaterializedDeploymentLifecycleFinished(mission);
                    }

                    finishedDeployment = finishDeployment && HasDeploymentLifecycleFinished(mission);
                    deploymentDiagnostics =
                        "deployment-handler-missing-controlled-siege-machines-only" +
                        " StateOnlyFieldMaterialized=" + stateOnlyFieldMaterializedDeployment;
                }
            }
            catch (Exception ex)
            {
                diagnostics =
                    "auto-deploy-faulted " +
                    ex.GetType().Name + ":" + ex.Message +
                    " Plan={" + deploymentPlanDiagnostics + "}" +
                    " Deployment={" + deploymentDiagnostics + "}" +
                    " ControlledSiegeMachines={" + controlledSiegeMachineDiagnostics + "}" +
                    " AutoDeployedTeams=[" + string.Join(", ", autoDeployedTeams) + "]" +
                    " NonFatalFaults=[" + string.Join("; ", nonFatalDeploymentFaults) + "]";
                return false;
            }

            bool deploymentFinished = HasDeploymentLifecycleFinished(mission);
            diagnostics =
                "PlayerTeamSide=" + playerTeam.Side +
                " BattleTeams=[" + string.Join(", ", battleTeams.Select(DescribeDeploymentTeam)) + "]" +
                " Plan={" + deploymentPlanDiagnostics + "}" +
                " Deployment={" + deploymentDiagnostics + "}" +
                " ControlledSiegeMachinesDeployed=" + controlledSiegeMachinesDeployed +
                " ControlledSiegeMachines={" + controlledSiegeMachineDiagnostics + "}" +
                " AutoDeployedTeamCount=" + autoDeployedTeamCount +
                " AutoDeployedTeams=[" + string.Join(", ", autoDeployedTeams) + "]" +
                " PlayerSiegeWeaponsDeployed=" + playerSiegeWeaponsDeployed +
                " AiSiegeWeaponsDeployed=" + aiSiegeWeaponsDeployed +
                " ForceUpdatedUnits=" + forceUpdatedUnits +
                " RequestedFinishDeployment=" + finishDeployment +
                " FinishedDeploymentCall=" + finishedDeployment +
                " NonFatalFaults=[" + string.Join("; ", nonFatalDeploymentFaults) + "]" +
                " DeploymentFinished=" + deploymentFinished;
            return finishDeployment
                ? deploymentFinished && (autoDeployedTeamCount > 0 || controlledSiegeMachinesDeployed || stateOnlyFieldMaterializedDeployment)
                : autoDeployedTeamCount > 0 || controlledSiegeMachinesDeployed;
        }

        private static void MarkFieldMaterializedDeploymentLifecycleFinished(Mission mission)
        {
            if (mission == null)
                return;

            lock (Sync)
            {
                if (ReferenceEquals(_activeMission, mission))
                    _fieldMaterializedDeploymentLifecycleFinished = true;
            }
        }

        public static bool TryPrepareDeploymentPlanContract(
            Mission mission,
            IMissionTroopSupplier[] suppliers,
            BattleSideEnum playerSide,
            out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            if (!mission.GetDeploymentPlan<DefaultMissionDeploymentPlan>(out DefaultMissionDeploymentPlan deploymentPlan) ||
                deploymentPlan == null)
            {
                diagnostics = "deployment-plan-null";
                return false;
            }

            if (!TryEnsureTeamDeploymentPlans(mission, deploymentPlan, out string teamPlanDiagnostics))
            {
                diagnostics = "team-plans={" + teamPlanDiagnostics + "}";
                return false;
            }

            List<Team> battleTeams = mission.Teams?
                .Where(team => IsBattleDeploymentTeam(mission, team))
                .ToList() ?? new List<Team>();
            if (battleTeams.Count <= 0)
            {
                diagnostics = "team-plans={" + teamPlanDiagnostics + "} BattleTeams=0";
                return false;
            }

            var troopCountsByTeam = battleTeams.ToDictionary(
                team => team,
                _ => new Dictionary<FormationClass, (int Foot, int Mounted)>());

            int totalTroops = 0;
            int unresolvedTeamAssignments = 0;
            int fallbackTeamAssignments = 0;
            int skippedOrigins = 0;

            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                BattleSideEnum side = (BattleSideEnum)sideIndex;
                IMissionTroopSupplier supplier =
                    suppliers != null && sideIndex >= 0 && sideIndex < suppliers.Length
                        ? suppliers[sideIndex]
                        : null;
                if (supplier == null)
                    continue;

                Team fallbackTeam = battleTeams.FirstOrDefault(team => team.Side == side);
                bool isPlayerSide = side == playerSide;
                IEnumerable<IAgentOriginBase> troops;
                try
                {
                    troops = supplier.GetAllTroops() ?? Array.Empty<IAgentOriginBase>();
                }
                catch (Exception ex)
                {
                    diagnostics =
                        "team-plans={" + teamPlanDiagnostics + "} " +
                        "supplier-read-failed Side=" + side +
                        " Message=" + ex.GetType().Name + ":" + ex.Message;
                    return false;
                }

                foreach (IAgentOriginBase troopOrigin in troops)
                {
                    BasicCharacterObject troop = troopOrigin?.Troop;
                    if (troop == null)
                    {
                        skippedOrigins++;
                        continue;
                    }

                    Team troopTeam = null;
                    try
                    {
                        troopTeam = Mission.GetAgentTeam(troopOrigin, isPlayerSide);
                    }
                    catch
                    {
                    }

                    if (troopTeam == null || troopTeam.Side != side)
                    {
                        troopTeam = fallbackTeam;
                        if (troopTeam != null)
                        {
                            fallbackTeamAssignments++;
                        }
                        else
                        {
                            unresolvedTeamAssignments++;
                            continue;
                        }
                    }

                    FormationClass formationClass = ResolveDeploymentFormationClass(mission, side, troop);
                    bool forceDismountedDeploymentProjection = ShouldUseDismountedDeploymentProjection(mission, side);
                    if (!troopCountsByTeam.TryGetValue(troopTeam, out Dictionary<FormationClass, (int Foot, int Mounted)> formationCounts))
                    {
                        formationCounts = new Dictionary<FormationClass, (int Foot, int Mounted)>();
                        troopCountsByTeam[troopTeam] = formationCounts;
                    }

                    formationCounts.TryGetValue(formationClass, out (int Foot, int Mounted) currentCount);
                    if (!forceDismountedDeploymentProjection && troop.HasMount())
                        formationCounts[formationClass] = (currentCount.Foot, currentCount.Mounted + 1);
                    else
                        formationCounts[formationClass] = (currentCount.Foot + 1, currentCount.Mounted);

                    totalTroops++;
                }
            }

            try
            {
                deploymentPlan.ClearAll();
            }
            catch (Exception ex)
            {
                diagnostics =
                    "team-plans={" + teamPlanDiagnostics + "} " +
                    "clear-all-failed Message=" + ex.GetType().Name + ":" + ex.Message;
                return false;
            }

            int plansMade = 0;
            foreach (Team team in battleTeams)
            {
                try
                {
                    deploymentPlan.SetSpawnWithHorses(team, false);
                    if (troopCountsByTeam.TryGetValue(team, out Dictionary<FormationClass, (int Foot, int Mounted)> formationCounts))
                    {
                        foreach (KeyValuePair<FormationClass, (int Foot, int Mounted)> formationCount in formationCounts.OrderBy(pair => (int)pair.Key))
                        {
                            if (formationCount.Value.Foot <= 0 && formationCount.Value.Mounted <= 0)
                                continue;

                            deploymentPlan.AddTroops(team, formationCount.Key, formationCount.Value.Foot, formationCount.Value.Mounted);
                            deploymentPlan.AddTroops(team, formationCount.Key, formationCount.Value.Foot, formationCount.Value.Mounted, isReinforcement: true);
                        }
                    }

                    deploymentPlan.MakeDeploymentPlan(team);
                    deploymentPlan.MakeReinforcementDeploymentPlan(team);
                    plansMade++;
                }
                catch (Exception ex)
                {
                    diagnostics =
                        "team-plans={" + teamPlanDiagnostics + "} " +
                        "team-plan-build-failed Team=#" + team.TeamIndex + "/" + team.Side +
                        " Message=" + ex.GetType().Name + ":" + ex.Message;
                    return false;
                }
            }

            diagnostics =
                "TeamPlans={" + teamPlanDiagnostics + "} " +
                "BattleTeams=" + battleTeams.Count +
                " PlansMade=" + plansMade +
                " TotalTroops=" + totalTroops +
                " FallbackTeamAssignments=" + fallbackTeamAssignments +
                " UnresolvedTeamAssignments=" + unresolvedTeamAssignments +
                " SkippedOrigins=" + skippedOrigins +
                " TeamCounts=[" + BuildTeamCountSummary(troopCountsByTeam) + "]";
            RememberPreparedDeploymentPlan(mission, playerSide);
            return true;
        }

        public static bool TryApplyNativeLikeSpawnHandlerContract(
            Mission mission,
            DefaultBattleMissionAgentSpawnLogic spawnLogic,
            int defenderTotal,
            int attackerTotal,
            int defenderInitial,
            int attackerInitial,
            in MissionSpawnSettings spawnSettings,
            out string diagnostics)
        {
            diagnostics = "spawn-logic-null";
            if (spawnLogic == null)
                return false;

            try
            {
                bool shouldMountLiveDeploymentControllers =
                    ShouldMountLiveDeploymentControllers(mission, out string liveDeploymentControllerPolicy);
                bool useFieldMaterializedSiegeRuntime =
                    ExperimentalFeatures.EnableSiegeReplayFieldMaterializedArmyRuntime &&
                    mission != null &&
                    SceneRuntimeClassifier.IsExactSiegeAssaultWithDeploymentScene(mission.SceneName ?? string.Empty);
                int effectiveDefenderTotal = defenderTotal;
                int effectiveAttackerTotal = attackerTotal;
                if (useFieldMaterializedSiegeRuntime)
                {
                    effectiveDefenderTotal = Math.Max(0, effectiveDefenderTotal);
                    effectiveAttackerTotal = Math.Max(0, effectiveAttackerTotal);
                    if ((long)effectiveDefenderTotal + effectiveAttackerTotal <= 0L)
                    {
                        effectiveDefenderTotal = 1;
                        effectiveAttackerTotal = 1;
                    }
                }
                int effectiveDefenderInitial = useFieldMaterializedSiegeRuntime ? 0 : defenderInitial;
                int effectiveAttackerInitial = useFieldMaterializedSiegeRuntime ? 0 : attackerInitial;
                spawnLogic.SetSpawnHorses(BattleSideEnum.Defender, false);
                spawnLogic.SetSpawnHorses(BattleSideEnum.Attacker, false);
                spawnLogic.InitWithSinglePhase(
                    effectiveDefenderTotal,
                    effectiveAttackerTotal,
                    effectiveDefenderInitial,
                    effectiveAttackerInitial,
                    spawnDefenders: false,
                    spawnAttackers: false,
                    in spawnSettings);
                RememberAppliedNativeSpawnContract(mission);
                diagnostics =
                    "SpawnHorses={Defender=False Attacker=False} " +
                    "SinglePhaseInitialized=True " +
                    "SpawnMode=" + (useFieldMaterializedSiegeRuntime ? "FieldMaterializedDependencyOnlyZeroInitial" : "NativeWithDeploymentFalseFalse") + " " +
                    "FieldMaterializedSiegeRuntime=" + useFieldMaterializedSiegeRuntime +
                    " " +
                    "LiveDeploymentControllers=" + (shouldMountLiveDeploymentControllers ? "Enabled" : "Suppressed") +
                    " LiveDeploymentControllerPolicy=" + liveDeploymentControllerPolicy +
                    " " +
                    "SpawnDefenders=False" +
                    " SpawnAttackers=False" +
                    "DefenderTotal=" + defenderTotal +
                    " AttackerTotal=" + attackerTotal +
                    " DefenderInitial=" + defenderInitial +
                    " AttackerInitial=" + attackerInitial +
                    " EffectiveDefenderTotal=" + effectiveDefenderTotal +
                    " EffectiveAttackerTotal=" + effectiveAttackerTotal +
                    " EffectiveDefenderInitial=" + effectiveDefenderInitial +
                    " EffectiveAttackerInitial=" + effectiveAttackerInitial;
                return true;
            }
            catch (Exception ex)
            {
                diagnostics =
                    "spawn-handler-faulted " +
                    ex.GetType().Name + ":" + ex.Message +
                    " DefenderTotal=" + defenderTotal +
                    " AttackerTotal=" + attackerTotal +
                    " DefenderInitial=" + defenderInitial +
                    " AttackerInitial=" + attackerInitial;
                return false;
            }
        }

        private static void RememberPreparedDeploymentPlan(Mission mission, BattleSideEnum playerSide)
        {
            if (mission == null)
                return;

            lock (Sync)
            {
                if (!ReferenceEquals(_activeMission, mission))
                    _fieldMaterializedDeploymentLifecycleFinished = false;

                _activeMission = mission;
                _activePlayerSide = playerSide;
                _deploymentPlanPrepared = true;
            }
        }

        private static void RememberAppliedNativeSpawnContract(Mission mission)
        {
            if (mission == null)
                return;

            lock (Sync)
            {
                if (!ReferenceEquals(_activeMission, mission))
                {
                    _activeMission = mission;
                    _fieldMaterializedDeploymentLifecycleFinished = false;
                }

                _nativeSpawnContractApplied = true;
            }
        }

        private static bool TryEnsureMissionSiegeEnginesLogicBehavior(
            Mission mission,
            BattleScenarioContextMessage scenarioContext,
            out string diagnostics,
            bool allowCreation = true)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            MissionSiegeEnginesLogic existingBehavior = mission.GetMissionBehavior<MissionSiegeEnginesLogic>();
            if (existingBehavior != null)
            {
                diagnostics = "Existing=True Created=False";
                return true;
            }

            if (!allowCreation)
            {
                diagnostics = "Existing=False Created=False Reason=missing-from-initial-stack";
                return false;
            }

            if (!TryCreateMissionSiegeEnginesLogicBehavior(
                    scenarioContext,
                    out MissionSiegeEnginesLogic behavior,
                    out string behaviorDiagnostics))
            {
                diagnostics = "Existing=False Created=False Reason=" + behaviorDiagnostics;
                return false;
            }

            try
            {
                mission.AddMissionBehavior(behavior);
                behavior.OnBehaviorInitialize();
                behavior.AfterStart();
                diagnostics = "Existing=False Created=True " + behaviorDiagnostics;
                return true;
            }
            catch (Exception ex)
            {
                diagnostics =
                    "Existing=False Created=False Reason=" +
                    ex.GetType().Name + ":" + ex.Message +
                    " Factory={" + behaviorDiagnostics + "}";
                return false;
            }
        }

        private static bool TryPrepareClientDeploymentPointsForCommanderUi(
            Mission mission,
            BattleSideEnum commanderSide,
            out string diagnostics)
        {
            diagnostics = "skipped";
            if (mission?.ActiveMissionObjects == null)
                return false;

            if (!GameNetwork.IsClient || GameNetwork.IsServer)
            {
                diagnostics = "skipped-non-remote-client";
                return true;
            }

            if (DeploymentPointWeaponsField == null || DeploymentPointDetermineTypeMethod == null)
            {
                diagnostics =
                    "reflection-unavailable WeaponsField=" + (DeploymentPointWeaponsField != null) +
                    " DetermineTypeMethod=" + (DeploymentPointDetermineTypeMethod != null);
                return false;
            }

            int pointCount = 0;
            int pointCountForSide = 0;
            int pointsWithWeapons = 0;
            int weaponCount = 0;
            int hiddenPointCount = 0;
            int shownPointCount = 0;
            int unavailablePointCount = 0;
            int failedCount = 0;
            try
            {
                var preparedPoints = new List<DeploymentPoint>();
                foreach (DeploymentPoint deploymentPoint in mission.ActiveMissionObjects.FindAllWithType<DeploymentPoint>())
                {
                    if (deploymentPoint == null || deploymentPoint.IsDisabled)
                        continue;

                    pointCount++;
                    try
                    {
                        var weapons = deploymentPoint.GetWeaponsUnder();
                        DeploymentPointWeaponsField.SetValue(deploymentPoint, weapons);
                        int currentWeaponCount = weapons?.Count ?? 0;
                        weaponCount += currentWeaponCount;
                        if (currentWeaponCount > 0)
                        {
                            pointsWithWeapons++;
                            DeploymentPointDetermineTypeMethod.Invoke(deploymentPoint, null);
                        }

                        deploymentPoint.Hide();
                        hiddenPointCount++;
                        preparedPoints.Add(deploymentPoint);
                    }
                    catch
                    {
                        failedCount++;
                    }
                }

                foreach (DeploymentPoint deploymentPoint in preparedPoints)
                {
                    if (commanderSide != BattleSideEnum.None && deploymentPoint.Side != commanderSide)
                        continue;

                    pointCountForSide++;
                    if (!HasCommanderDeployableSiegeWeapon(mission, commanderSide, deploymentPoint))
                    {
                        unavailablePointCount++;
                        continue;
                    }

                    try
                    {
                        deploymentPoint.Show();
                        shownPointCount++;
                    }
                    catch
                    {
                        failedCount++;
                    }
                }

                diagnostics =
                    "RemoteClientPrepared=True" +
                    " Points=" + pointCount +
                    " SidePoints=" + pointCountForSide +
                    " PointsWithWeapons=" + pointsWithWeapons +
                    " Weapons=" + weaponCount +
                    " HiddenPoints=" + hiddenPointCount +
                    " ShownSidePoints=" + shownPointCount +
                    " UnavailableSidePoints=" + unavailablePointCount +
                    " Failed=" + failedCount;
                return failedCount == 0;
            }
            catch (Exception ex)
            {
                diagnostics =
                    "RemoteClientPrepared=False Reason=" +
                    ex.GetType().Name + ":" + ex.Message +
                    " Points=" + pointCount +
                    " SidePoints=" + pointCountForSide +
                    " PointsWithWeapons=" + pointsWithWeapons +
                    " Weapons=" + weaponCount +
                    " HiddenPoints=" + hiddenPointCount +
                    " ShownSidePoints=" + shownPointCount +
                    " UnavailableSidePoints=" + unavailablePointCount +
                    " Failed=" + failedCount;
                return false;
            }
        }

        public static bool TryApplyCommanderDeploymentSiegeMachineSelectionLocally(
            Mission mission,
            BattleSideEnum commanderSide,
            DeploymentPoint deploymentPoint,
            SiegeWeapon siegeWeapon,
            bool clearSelection,
            out string diagnostics)
        {
            return TryApplyCommanderDeploymentSiegeMachineSelectionLocally(
                mission,
                commanderSide,
                deploymentPoint,
                siegeWeapon,
                clearSelection,
                prepareCommanderUi: false,
                out diagnostics);
        }

        public static bool TryApplyCommanderDeploymentSiegeMachineStateLocally(
            Mission mission,
            BattleSideEnum commanderSide,
            DeploymentPoint deploymentPoint,
            SiegeWeapon siegeWeapon,
            bool clearSelection,
            out string diagnostics)
        {
            return TryApplyCommanderDeploymentSiegeMachineSelectionLocally(
                mission,
                commanderSide,
                deploymentPoint,
                siegeWeapon,
                clearSelection,
                prepareCommanderUi: false,
                out diagnostics);
        }

        private static bool TryApplyCommanderDeploymentSiegeMachineSelectionLocally(
            Mission mission,
            BattleSideEnum commanderSide,
            DeploymentPoint deploymentPoint,
            SiegeWeapon siegeWeapon,
            bool clearSelection,
            bool prepareCommanderUi,
            out string diagnostics)
        {
            diagnostics = "skipped";
            if (mission == null ||
                commanderSide == BattleSideEnum.None ||
                deploymentPoint == null ||
                deploymentPoint.Side != commanderSide ||
                (!clearSelection && siegeWeapon == null))
            {
                diagnostics = "invalid-context";
                return false;
            }

            if (!GameNetwork.IsClient || GameNetwork.IsServer)
            {
                diagnostics = "skipped-non-remote-client";
                return false;
            }

            string preparationDiagnostics;
            if (prepareCommanderUi)
            {
                TryPrepareClientDeploymentPointsForCommanderUi(
                    mission,
                    commanderSide,
                    out preparationDiagnostics);
            }
            else
            {
                TryRefreshClientDeploymentPointWeaponCacheForStateApply(
                    mission,
                    commanderSide,
                    out preparationDiagnostics);
            }

            Type selectedWeaponType = clearSelection
                ? null
                : ResolveCommanderDeploymentSiegeWeaponType(siegeWeapon);
            if (!clearSelection && selectedWeaponType == null)
            {
                diagnostics =
                    "unresolved-weapon-type " +
                    "Preparation={" + preparationDiagnostics + "}";
                return false;
            }

            int disbandedOtherPoints = 0;
            bool disbandedTargetPoint = false;
            bool deployedTargetPoint = false;
            var visualDiagnostics = new List<string>();
            try
            {
                if (!clearSelection)
                {
                    foreach (DeploymentPoint otherPoint in mission.ActiveMissionObjects.FindAllWithType<DeploymentPoint>())
                    {
                        if (otherPoint == null ||
                            ReferenceEquals(otherPoint, deploymentPoint) ||
                            otherPoint.Side != commanderSide ||
                            !otherPoint.IsDeployed ||
                            otherPoint.DeployedWeapon == null)
                        {
                            continue;
                        }

                        if (ReferenceEquals(otherPoint.DeployedWeapon, siegeWeapon))
                        {
                            SiegeWeapon movedWeapon = otherPoint.DeployedWeapon as SiegeWeapon;
                            otherPoint.Disband();
                            disbandedOtherPoints++;
                            visualDiagnostics.Add("OtherDisband={" +
                                                  CoopSiegeMachineDeploymentController.NormalizeLocalDeployedSiegeWeaponVisualTree(
                                                      otherPoint,
                                                      movedWeapon,
                                                      false) +
                                                  "}");
                        }
                    }
                }

                if (deploymentPoint.IsDeployed &&
                    (clearSelection || !ReferenceEquals(deploymentPoint.DeployedWeapon, siegeWeapon)))
                {
                    SiegeWeapon previousWeapon = deploymentPoint.DeployedWeapon as SiegeWeapon;
                    deploymentPoint.Disband();
                    disbandedTargetPoint = true;
                    visualDiagnostics.Add("TargetDisband={" +
                                          CoopSiegeMachineDeploymentController.NormalizeLocalDeployedSiegeWeaponVisualTree(
                                              deploymentPoint,
                                              previousWeapon,
                                              false) +
                                          "}");
                }

                if (!clearSelection &&
                    siegeWeapon != null &&
                    !ReferenceEquals(deploymentPoint.DeployedWeapon, siegeWeapon))
                {
                    deploymentPoint.Deploy(siegeWeapon);
                    deployedTargetPoint = true;
                }

                if (!clearSelection && siegeWeapon != null)
                {
                    visualDiagnostics.Add("TargetDeploy={" +
                                          CoopSiegeMachineDeploymentController.NormalizeLocalDeployedSiegeWeaponVisualTree(
                                              deploymentPoint,
                                              siegeWeapon,
                                              true) +
                                          "}");
                }

                diagnostics =
                    "Applied=True" +
                    " Clear=" + clearSelection +
                    " PrepareCommanderUi=" + prepareCommanderUi +
                    " WeaponType=" + (selectedWeaponType?.Name ?? "<null>") +
                    " DisbandedOtherPoints=" + disbandedOtherPoints +
                    " DisbandedTargetPoint=" + disbandedTargetPoint +
                    " DeployedTargetPoint=" + deployedTargetPoint +
                    " Visual={" + string.Join(" ", visualDiagnostics.ToArray()) + "}" +
                    " Preparation={" + preparationDiagnostics + "}";
                return true;
            }
            catch (Exception ex)
            {
                diagnostics =
                    "Applied=False Reason=" +
                    ex.GetType().Name + ":" + ex.Message +
                    " Clear=" + clearSelection +
                    " PrepareCommanderUi=" + prepareCommanderUi +
                    " WeaponType=" + (selectedWeaponType?.Name ?? "<null>") +
                    " DisbandedOtherPoints=" + disbandedOtherPoints +
                    " DisbandedTargetPoint=" + disbandedTargetPoint +
                    " DeployedTargetPoint=" + deployedTargetPoint +
                    " Visual={" + string.Join(" ", visualDiagnostics.ToArray()) + "}" +
                    " Preparation={" + preparationDiagnostics + "}";
                return false;
            }
        }

        private static bool TryRefreshClientDeploymentPointWeaponCacheForStateApply(
            Mission mission,
            BattleSideEnum commanderSide,
            out string diagnostics)
        {
            diagnostics = "skipped";
            if (mission?.ActiveMissionObjects == null)
                return false;

            if (!GameNetwork.IsClient || GameNetwork.IsServer)
            {
                diagnostics = "skipped-non-remote-client";
                return true;
            }

            if (DeploymentPointWeaponsField == null || DeploymentPointDetermineTypeMethod == null)
            {
                diagnostics =
                    "reflection-unavailable WeaponsField=" + (DeploymentPointWeaponsField != null) +
                    " DetermineTypeMethod=" + (DeploymentPointDetermineTypeMethod != null);
                return false;
            }

            int pointCount = 0;
            int pointCountForSide = 0;
            int pointsWithWeapons = 0;
            int weaponCount = 0;
            int determinedPointCount = 0;
            int failedCount = 0;
            try
            {
                foreach (DeploymentPoint deploymentPoint in mission.ActiveMissionObjects.FindAllWithType<DeploymentPoint>())
                {
                    if (deploymentPoint == null)
                        continue;

                    pointCount++;
                    if (commanderSide != BattleSideEnum.None && deploymentPoint.Side == commanderSide)
                        pointCountForSide++;

                    try
                    {
                        var weapons = deploymentPoint.GetWeaponsUnder();
                        DeploymentPointWeaponsField.SetValue(deploymentPoint, weapons);
                        int currentWeaponCount = weapons?.Count ?? 0;
                        weaponCount += currentWeaponCount;
                        if (currentWeaponCount > 0)
                        {
                            pointsWithWeapons++;
                            DeploymentPointDetermineTypeMethod.Invoke(deploymentPoint, null);
                            determinedPointCount++;
                        }
                    }
                    catch
                    {
                        failedCount++;
                    }
                }

                diagnostics =
                    "RemoteClientCacheRefreshed=True" +
                    " Points=" + pointCount +
                    " SidePoints=" + pointCountForSide +
                    " PointsWithWeapons=" + pointsWithWeapons +
                    " Weapons=" + weaponCount +
                    " DeterminedPoints=" + determinedPointCount +
                    " Failed=" + failedCount;
                return failedCount == 0;
            }
            catch (Exception ex)
            {
                diagnostics =
                    "RemoteClientCacheRefreshed=False Reason=" +
                    ex.GetType().Name + ":" + ex.Message +
                    " Points=" + pointCount +
                    " SidePoints=" + pointCountForSide +
                    " PointsWithWeapons=" + pointsWithWeapons +
                    " Weapons=" + weaponCount +
                    " DeterminedPoints=" + determinedPointCount +
                    " Failed=" + failedCount;
                return false;
            }
        }

        private static Type ResolveCommanderDeploymentSiegeWeaponType(SiegeWeapon siegeWeapon)
        {
            if (siegeWeapon == null)
                return null;

            try
            {
                return MissionSiegeWeaponsController.GetWeaponType(siegeWeapon);
            }
            catch
            {
                return siegeWeapon.GetType();
            }
        }

        private static bool HasCommanderDeployableSiegeWeapon(
            Mission mission,
            BattleSideEnum commanderSide,
            DeploymentPoint deploymentPoint)
        {
            if (mission == null ||
                commanderSide == BattleSideEnum.None ||
                deploymentPoint == null ||
                deploymentPoint.Side != commanderSide)
            {
                return false;
            }

            try
            {
                MissionSiegeEnginesLogic siegeEnginesLogic = mission.GetMissionBehavior<MissionSiegeEnginesLogic>();
                IMissionSiegeWeaponsController weaponsController = siegeEnginesLogic?.GetSiegeWeaponsController(commanderSide);
                foreach (SynchedMissionObject deployableWeapon in deploymentPoint.DeployableWeapons)
                {
                    if (!(deployableWeapon is SiegeWeapon siegeWeapon) ||
                        siegeWeapon.IsDisabled ||
                        siegeWeapon.Side != commanderSide)
                    {
                        continue;
                    }

                    Type weaponType = MissionSiegeWeaponsController.GetWeaponType(siegeWeapon);
                    if (weaponType == null)
                        continue;

                    if (weaponsController == null || weaponsController.GetMaxDeployableWeaponCount(weaponType) > 0)
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool TryBuildMissionSiegeWeaponLists(
            BattleScenarioContextMessage scenarioContext,
            out List<MissionSiegeWeapon> attackerSiegeWeapons,
            out List<MissionSiegeWeapon> defenderSiegeWeapons,
            out string diagnostics)
        {
            attackerSiegeWeapons = new List<MissionSiegeWeapon>();
            defenderSiegeWeapons = new List<MissionSiegeWeapon>();

            BattleSiegeContextMessage siegeContext = scenarioContext?.SiegeContext;
            if (siegeContext == null)
            {
                diagnostics = "siege-context-null";
                return false;
            }

            attackerSiegeWeapons = BuildMissionSiegeWeaponsForSide(
                siegeContext.AttackerSiegeEngines,
                siegeContext.AttackerSiegeEngineTypeIds,
                "Attacker",
                out string attackerDiagnostics);
            defenderSiegeWeapons = BuildMissionSiegeWeaponsForSide(
                siegeContext.DefenderSiegeEngines,
                siegeContext.DefenderSiegeEngineTypeIds,
                "Defender",
                out string defenderDiagnostics);

            diagnostics =
                "Attacker={" + attackerDiagnostics + "} " +
                "Defender={" + defenderDiagnostics + "}";
            return true;
        }

        private static List<MissionSiegeWeapon> BuildMissionSiegeWeaponsForSide(
            List<BattleSiegeEngineSnapshotMessage> engineSnapshots,
            List<string> legacyTypeIds,
            string sideName,
            out string diagnostics)
        {
            var missionSiegeWeapons = new List<MissionSiegeWeapon>();
            int createdFromSnapshot = 0;
            int createdFromLegacyTypeId = 0;
            int skipped = 0;

            List<BattleSiegeEngineSnapshotMessage> orderedSnapshots = engineSnapshots?
                .Where(snapshot => snapshot != null)
                .OrderBy(snapshot => snapshot.Index)
                .ToList() ?? new List<BattleSiegeEngineSnapshotMessage>();

            if (orderedSnapshots.Count > 0)
            {
                for (int i = 0; i < orderedSnapshots.Count; i++)
                {
                    BattleSiegeEngineSnapshotMessage engineSnapshot = orderedSnapshots[i];
                    if (TryCreateMissionSiegeWeapon(engineSnapshot, allowLegacyDefaultCreation: false, out MissionSiegeWeapon missionSiegeWeapon))
                    {
                        missionSiegeWeapons.Add(missionSiegeWeapon);
                        createdFromSnapshot++;
                    }
                    else
                    {
                        skipped++;
                    }
                }

                diagnostics =
                    "Side=" + sideName +
                    " Source=Snapshot" +
                    " Input=" + orderedSnapshots.Count +
                    " Created=" + createdFromSnapshot +
                    " Skipped=" + skipped;
                return missionSiegeWeapons;
            }

            List<string> safeLegacyTypeIds = legacyTypeIds?
                .Where(typeId => !string.IsNullOrWhiteSpace(typeId))
                .ToList() ?? new List<string>();
            for (int i = 0; i < safeLegacyTypeIds.Count; i++)
            {
                var legacySnapshot = new BattleSiegeEngineSnapshotMessage
                {
                    EngineTypeId = safeLegacyTypeIds[i],
                    Index = -1
                };

                if (TryCreateMissionSiegeWeapon(legacySnapshot, allowLegacyDefaultCreation: true, out MissionSiegeWeapon missionSiegeWeapon))
                {
                    missionSiegeWeapons.Add(missionSiegeWeapon);
                    createdFromLegacyTypeId++;
                }
                else
                {
                    skipped++;
                }
            }

            diagnostics =
                "Side=" + sideName +
                " Source=" + (safeLegacyTypeIds.Count > 0 ? "LegacyTypeIds" : "None") +
                " Input=" + safeLegacyTypeIds.Count +
                " Created=" + createdFromLegacyTypeId +
                " Skipped=" + skipped;
            return missionSiegeWeapons;
        }

        private static bool TryCreateMissionSiegeWeapon(
            BattleSiegeEngineSnapshotMessage engineSnapshot,
            bool allowLegacyDefaultCreation,
            out MissionSiegeWeapon missionSiegeWeapon)
        {
            missionSiegeWeapon = null;
            string engineTypeId = engineSnapshot?.EngineTypeId;
            if (string.IsNullOrWhiteSpace(engineTypeId))
                return false;

            SiegeEngineType engineType;
            try
            {
                engineType = MBObjectManager.Instance.GetObject<SiegeEngineType>(engineTypeId);
            }
            catch
            {
                return false;
            }

            if (engineType == null)
                return false;

            float defaultMaxHealth = engineType.BaseHitPoints > 0 ? engineType.BaseHitPoints : 1f;
            float maxHealth = SanitizeFiniteFloat(engineSnapshot?.MaxHealth, defaultMaxHealth, 0f, float.MaxValue);
            if (maxHealth <= 0f)
                maxHealth = defaultMaxHealth;

            float fallbackInitialHealth = SanitizeFiniteFloat(engineSnapshot?.Health, maxHealth, 0f, maxHealth);
            float initialHealth = SanitizeFiniteFloat(engineSnapshot?.InitialHealth, fallbackInitialHealth, 0f, maxHealth);
            float currentHealth = SanitizeFiniteFloat(engineSnapshot?.Health, initialHealth, 0f, maxHealth);

            bool hasExplicitCampaignState =
                engineSnapshot != null &&
                (engineSnapshot.Index >= 0 ||
                 engineSnapshot.Health > 0f ||
                 engineSnapshot.InitialHealth > 0f ||
                 engineSnapshot.MaxHealth > 0f);

            if (!hasExplicitCampaignState && allowLegacyDefaultCreation)
            {
                missionSiegeWeapon = MissionSiegeWeapon.CreateDefaultWeapon(engineType);
                return missionSiegeWeapon != null;
            }

            missionSiegeWeapon = MissionSiegeWeapon.CreateCampaignWeapon(
                engineType,
                engineSnapshot?.Index ?? -1,
                initialHealth,
                maxHealth);
            if (missionSiegeWeapon == null)
                return false;

            if (Math.Abs(currentHealth - initialHealth) > 0.001f)
                missionSiegeWeapon.SetHealth(currentHealth);

            return true;
        }

        private static float SanitizeFiniteFloat(float? value, float fallback, float min, float max)
        {
            if (!value.HasValue || float.IsNaN(value.Value) || float.IsInfinity(value.Value))
                return fallback;

            float safeValue = value.Value;
            if (safeValue < min)
                return min;

            if (safeValue > max)
                return max;

            return safeValue;
        }

        private static bool TryEnsureTeamDeploymentPlans(
            Mission mission,
            DefaultMissionDeploymentPlan deploymentPlan,
            out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null || deploymentPlan == null)
                return false;

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

            var existingTeams = new HashSet<Team>();
            foreach (object entry in enumerable)
            {
                Team existingTeam = TryReadMember(entry, "team") as Team ?? TryReadMember(entry, "Item1") as Team;
                if (existingTeam != null)
                    existingTeams.Add(existingTeam);
            }

            List<Team> battleTeams = mission.Teams?
                .Where(team => IsBattleDeploymentTeam(mission, team))
                .ToList() ?? new List<Team>();
            List<Team> missingTeams = battleTeams
                .Where(team => !existingTeams.Contains(team))
                .ToList();

            if (missingTeams.Count <= 0)
            {
                diagnostics = "already-ready Existing=" + existingTeams.Count + " BattleTeams=" + battleTeams.Count;
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
            return true;
        }

        private static string DescribeDeploymentTeam(Team team)
        {
            return team == null
                ? "null"
                : "#" + team.TeamIndex + "/" + team.Side;
        }

        private static void ForceUpdateDeploymentTeamUnits(Team team)
        {
            if (team == null)
                return;

            try
            {
                foreach (Formation formation in team.FormationsIncludingEmpty)
                {
                    if (formation == null)
                        continue;

                    formation.ApplyActionOnEachUnit(agent =>
                    {
                        if (agent == null)
                            return;

                        agent.ForceUpdateCachedAndFormationValues(
                            updateOnlyMovement: false,
                            arrangementChangeAllowed: false);
                    });
                    formation.SetHasPendingUnitPositions(hasPendingUnitPositions: false);
                }
            }
            catch
            {
            }
        }

        private static bool IsBattleDeploymentTeam(Mission mission, Team team)
        {
            return mission != null &&
                   team != null &&
                   team.Side != BattleSideEnum.None &&
                   !ExactCampaignArmyBootstrap.IsSpawnLogicInitTemporaryNonBattleTeam(mission, team);
        }

        private static FormationClass ResolveDeploymentFormationClass(
            Mission mission,
            BattleSideEnum side,
            BasicCharacterObject troop)
        {
            FormationClass formationClass = troop?.DefaultFormationClass ?? FormationClass.Infantry;
            try
            {
                if (mission != null && troop != null)
                    formationClass = mission.GetAgentTroopClass(side, troop);
            }
            catch
            {
            }

            if (formationClass == FormationClass.NumberOfRegularFormations ||
                formationClass == FormationClass.NumberOfAllFormations)
            {
                formationClass = troop?.DefaultFormationClass ?? FormationClass.Infantry;
            }

            formationClass = formationClass.FallbackClass();
            if (ShouldUseDismountedDeploymentProjection(mission, side))
                formationClass = DismountSiegeFormationClass(formationClass);

            int formationIndex = (int)formationClass;
            if (formationIndex < 0 || formationIndex >= 11)
                return FormationClass.Infantry;

            return formationClass;
        }

        private static bool ShouldUseDismountedDeploymentProjection(Mission mission, BattleSideEnum side)
        {
            if (mission == null || side == BattleSideEnum.None)
                return false;

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetScenarioContext() ??
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            if (!IsSiegeAssaultScenario(scenarioContext))
                return false;

            if (ExactCampaignArmyBootstrap.TryGetSpawnHorses(mission, side, out bool spawnHorses))
                return !spawnHorses;

            try
            {
                return mission.IsSiegeBattle;
            }
            catch
            {
                return true;
            }
        }

        private static FormationClass DismountSiegeFormationClass(FormationClass formationClass)
        {
            if (formationClass == FormationClass.Cavalry)
                return FormationClass.Infantry;

            if (formationClass == FormationClass.HorseArcher)
                return FormationClass.Ranged;

            return formationClass;
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

        private static string BuildTeamCountSummary(
            IDictionary<Team, Dictionary<FormationClass, (int Foot, int Mounted)>> troopCountsByTeam)
        {
            if (troopCountsByTeam == null || troopCountsByTeam.Count <= 0)
                return "none";

            return string.Join(
                "; ",
                troopCountsByTeam.Select(teamEntry =>
                {
                    int teamTotal = teamEntry.Value?.Sum(pair => pair.Value.Foot + pair.Value.Mounted) ?? 0;
                    string formationSummary = teamEntry.Value == null || teamEntry.Value.Count <= 0
                        ? "none"
                        : string.Join(
                            ", ",
                            teamEntry.Value
                                .OrderBy(pair => (int)pair.Key)
                                .Select(pair => pair.Key + "=" + (pair.Value.Foot + pair.Value.Mounted) + "(F" + pair.Value.Foot + "/M" + pair.Value.Mounted + ")"));

                    return
                        "#" + teamEntry.Key.TeamIndex + "/" + teamEntry.Key.Side +
                        ":Total=" + teamTotal +
                        " Formations={" + formationSummary + "}";
                }));
        }

        private static string BuildSuppressedMissionBehaviorDiagnostics(
            MissionBehavior existingBehavior,
            string policy)
        {
            return
                "Existing=" + (existingBehavior != null) +
                " Created=False" +
                " RuntimeType=" + (existingBehavior?.GetType().Name ?? "null") +
                " Policy=" + (policy ?? "none");
        }

        private static bool IsMissionBehaviorMutationUnsafe(Mission mission)
        {
            if (mission == null)
                return true;

            try
            {
                return mission.CurrentState == Mission.State.Initializing;
            }
            catch
            {
                return true;
            }
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
                diagnostics = "Existing=False Created=False Reason=missing-from-initial-stack RuntimeType=" + behaviorName;
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
                diagnostics = "Existing=False Created=True RuntimeType=" + behaviorName;
                return true;
            }
            catch (Exception ex)
            {
                diagnostics =
                    "Existing=False Created=False Reason=" +
                    ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }
    }
}
