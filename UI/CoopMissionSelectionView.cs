using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.SiegeAmbush;
using CoopSpectator.Infrastructure.VillageBattle;
using CoopSpectator.MissionBehaviors;
using CoopSpectator.Network.Messages;
using CoopSpectator.Patches;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Missions;
using TaleWorlds.MountAndBlade.Missions.Handlers;
using TaleWorlds.MountAndBlade.View.VisualOrders.Orders;
using TaleWorlds.MountAndBlade.View.VisualOrders.Orders.ToggleOrders;
using TaleWorlds.MountAndBlade.View.VisualOrders.OrderSets;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.MountAndBlade.ViewModelCollection.Order;
using TaleWorlds.MountAndBlade.ViewModelCollection.Order.Visual;
using TaleWorlds.MountAndBlade.ViewModelCollection.Order.Visual.Default.Orders.FormOrders;
using TaleWorlds.MountAndBlade.ViewModelCollection.Order.Visual.Default.Orders.MovementOrders;
using TaleWorlds.MountAndBlade.ViewModelCollection.Order.Visual.Default.Orders.ToggleOrders;
using TaleWorlds.MountAndBlade.ViewModelCollection.OrderOfBattle;
using TaleWorlds.Localization;
using TaleWorlds.ScreenSystem;
using TaleWorlds.TwoDimension;

namespace CoopSpectator.UI
{
    public sealed class CoopMissionSelectionView : MissionView
    {
        private const string TeamMovieName = "CoopTeamSelection";
        private const string ClassMovieName = "CoopClassLoadout";
        private const string CommanderDeploymentMovieName = "OrderOfBattle";
        private const string CommanderDeploymentOrderMovieName = "OrderRadial";
        private const string CommanderSiegeMachineDeploymentMovieName = "Siege";
        private const string AiControlHintMovieName = "CoopBattleAiControlHint";
        private static readonly bool EnableManualSiegeCommanderDeployment = true;
        private const float RefreshIntervalSeconds = 0.15f;
        private const float InitialOverlayDelaySeconds = 0.75f;
        private const float StartBattleHotkeyCooldownSeconds = 0.2f;
        private const float ReopenSelectionHotkeyCooldownSeconds = 0.2f;
        private const float CommanderDeploymentFreeCameraMoveSpeed = 18f;
        private const float CommanderDeploymentFreeCameraFastMoveMultiplier = 3f;
        private const float CommanderDeploymentFreeCameraLookSensitivity = 0.0035f;
        private const float CommanderDeploymentFreeCameraMinPitch = -1.3659099f;
        private const float CommanderDeploymentFreeCameraMaxPitch = 1.1219974f;
        private const float CommanderSiegeMachineDeploymentRetryIntervalSeconds = 0.5f;
        private static readonly InputUsageMask CommanderDeploymentInputMask = (InputUsageMask)7;
        private static readonly TimeSpan LocalSpawnOverlaySuppressionDuration = TimeSpan.FromSeconds(2.5);
        private static readonly TimeSpan LocalSpawnPendingTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan LocalSpawnPendingResendInterval = TimeSpan.FromSeconds(1.5);
        private static readonly TimeSpan AgentControlRequestTimeout = TimeSpan.FromSeconds(5);
        private const int LocalSpawnPendingMaxRequestAttempts = 8;
        private static int _activeCameraPreviewAgentIndex = -1;
        private static string _activeCameraPreviewEntryId = string.Empty;
        private static int _activeAiObservationAgentIndex = -1;
        private static bool _commanderDeploymentOrderOfBattleActive;
        private static bool _commanderDeploymentPlacementInputActive;
        private static bool _commanderBattleOrderActive;
        private float _commanderDeploymentBoundaryRefreshTimer;
        private float _commanderSiegeMachineDeploymentRetryTimer;
        private bool _commanderSiegeMachineDeploymentNoTargetsLogged;
        private bool _commanderSiegeMachineDeploymentFailureLogged;

        private GauntletLayer _gauntletLayer;
        private GauntletLayer _aiControlHintLayer;
        private GauntletMovieIdentifier _movie;
        private GauntletMovieIdentifier _aiControlHintMovie;
        private GauntletMovieIdentifier _commanderDeploymentOrderMovie;
        private GauntletMovieIdentifier _commanderBattleOrderMovie;
        private GauntletMovieIdentifier _commanderSiegeMachineDeploymentMovie;
        private ViewModel _viewModel;
        private CoopBattleAiControlHintVM _aiControlHintVm;
        private OrderOfBattleVM _commanderDeploymentViewModel;
        private MissionOrderVM _commanderDeploymentOrderVm;
        private MissionOrderVM _commanderBattleOrderVm;
        private CoopSiegeMachineDeploymentVM _commanderSiegeMachineDeploymentVm;
        private SpriteCategory _commanderDeploymentOrderOfBattleSpriteCategory;
        private SpriteCategory _commanderDeploymentOrderSpriteCategory;
        private SpriteCategory _commanderBattleOrderSpriteCategory;
        private object _commanderDeploymentOrderTroopPlacer;
        private Action _commanderDeploymentOnUnitDeployedHandler;
        private bool _commanderDeploymentOrderVmInitialized;
        private bool _landBattleManualFormationPlacementActive;
        private Camera _commanderDeploymentFreeCamera;
        private bool _commanderDeploymentFreeCameraActive;
        private float _commanderDeploymentFreeCameraYaw;
        private float _commanderDeploymentFreeCameraPitch;
        private CoopCommanderDeploymentVisualOrderProvider _commanderDeploymentVisualOrderProvider;
        private bool _commanderDeploymentVisualOrderProviderRegistered;
        private bool _commanderBattleOrderVmInitialized;
        private MissionFormationTargetSelectionHandler _commanderBattleFormationTargetHandler;
        private MBReadOnlyList<Formation> _commanderBattleFocusedFormationsCache;
        private ICoopSelectionScreenViewModel _screenViewModel;
        private CoopSelectionScreen _currentScreen;
        private CoopSelectionScreen _requestedScreen = CoopSelectionScreen.TeamSelection;
        private BattleSideEnum _selectedSideOverride = BattleSideEnum.None;
        private string _selectedEntryIdOverride;
        private float _refreshTimer;
        private float _overlayStartupDelay = InitialOverlayDelaySeconds;
        private float _startBattleHotkeyCooldown;
        private bool _overlayLoadFailed;
        private bool _inputCaptured;
        private bool _inputCapturedCommanderDeploymentMode;
        private bool _hadLocalControlledAgent;
        private bool _startBattleInstructionShown;
        private bool _autoDeployInstructionShown;
        private bool _spectatorOverlayHidden;
        private DateTime _overlaySuppressedUntilUtc = DateTime.MinValue;
        private float _reopenSelectionHotkeyCooldown;
        private float _agentControlHotkeyCooldown;
        private bool _wasAiObservationActive;
        private string _lastAppliedRefreshKey = string.Empty;
        private bool _localSpawnPending;
        private bool _localSpawnPendingWaitsForDeployment;
        private DateTime _localSpawnPendingStartedUtc = DateTime.MinValue;
        private string _localSpawnPendingEntryId;
        private BattleSideEnum _localSpawnPendingSide = BattleSideEnum.None;
        private DateTime _localSpawnPendingLastRequestUtc = DateTime.MinValue;
        private int _localSpawnPendingRequestAttemptCount;
        private string _lastCameraPreviewLogKey = string.Empty;
        private bool _reconnectSelectionContractActive;
        private string _lastReconnectSelectionContractLogKey = string.Empty;
        private DateTime _missionScreenInitializedUtc = DateTime.MinValue;
        private string _lastIgnoredEntryStatusLogKey = string.Empty;
        private string _lastCommanderBattleOrderBridgeContextKey = string.Empty;
        private string _lastCommanderBattleOrderEmptySetGuardKey = string.Empty;
        private string _lastCommanderBattleOrderVisualAuditKey = string.Empty;

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();

            if (GameNetwork.IsClient && ExperimentalFeatures.EnableCustomCoopSelectionOverlay)
                ModLogger.Info("CoopMissionSelectionView: OnBehaviorInitialize.");
        }

        public override void OnMissionScreenInitialize()
        {
            base.OnMissionScreenInitialize();

            if (!GameNetwork.IsClient || !ExperimentalFeatures.EnableCustomCoopSelectionOverlay)
                return;

            ViewOrderPriority = 25;
            _missionScreenInitializedUtc = DateTime.UtcNow;
            _overlayStartupDelay = InitialOverlayDelaySeconds;
            _hadLocalControlledAgent = HasLocalControlledAgent();
            _startBattleInstructionShown = false;
            _autoDeployInstructionShown = false;
            _reconnectSelectionContractActive = false;
            _lastReconnectSelectionContractLogKey = string.Empty;
            _lastIgnoredEntryStatusLogKey = string.Empty;
            _lastCommanderBattleOrderBridgeContextKey = string.Empty;
            _lastCommanderBattleOrderEmptySetGuardKey = string.Empty;
            _lastCommanderBattleOrderVisualAuditKey = string.Empty;
            _commanderBattleFocusedFormationsCache = null;
            _commanderDeploymentPlacementInputActive = false;
            _commanderBattleOrderActive = false;
            _commanderSiegeMachineDeploymentRetryTimer = 0f;
            _commanderSiegeMachineDeploymentNoTargetsLogged = false;
            _commanderSiegeMachineDeploymentFailureLogged = false;
            _activeAiObservationAgentIndex = -1;
            _wasAiObservationActive = false;
            _agentControlHotkeyCooldown = 0f;
            ClearLocalSpawnPending("mission-screen-initialize");
            ResetSelectionFlow("mission-screen-initialize");
            ModLogger.Info("CoopMissionSelectionView: OnMissionScreenInitialize, coop selection shell init deferred.");
        }

        public override void OnMissionScreenTick(float dt)
        {
            base.OnMissionScreenTick(dt);

            if (!GameNetwork.IsClient || !ExperimentalFeatures.EnableCustomCoopSelectionOverlay)
                return;

            CoopBattleAgentControlRuntimeState.ExpirePendingClientRequest(AgentControlRequestTimeout);
            bool hasLocalControlledAgent = HasLocalControlledAgent();
            bool aiControlHotkeyConsumed = TryHandleAiControlHotkey(dt);
            bool suppressAgentLossFlow = CoopBattleAgentControlRuntimeState.IsClientAiObservationOrTransitionActive();
            TryTickAiControlObservationPresentation();
            if (_hadLocalControlledAgent && !hasLocalControlledAgent && !suppressAgentLossFlow)
            {
                ClearLocalSpawnPending("lost-local-agent");
                _overlaySuppressedUntilUtc = DateTime.MinValue;
                HandleLostLocalAgentSelectionFlow();
            }
            else if (!_hadLocalControlledAgent && hasLocalControlledAgent)
            {
                ClearLocalSpawnPending("gained-local-agent");
                _selectedEntryIdOverride = null;
            }

            _hadLocalControlledAgent = hasLocalControlledAgent;
            if (!aiControlHotkeyConsumed)
                TryHandleStartBattleHotkey(dt, hasLocalControlledAgent);
            TryShowStartBattleInstruction(hasLocalControlledAgent);
            if (!aiControlHotkeyConsumed)
                TryHandleReopenSelectionHotkey(dt, hasLocalControlledAgent);
            TryTickCommanderDeploymentViewModel();
            TryTickCommanderSiegeMachineDeploymentOverlay(dt);
            TryCompleteAutoDeployCaptainAssignmentRestoration();
            TryTickCommanderDeploymentBoundaries(dt);
            TryTickCommanderDeploymentFreeCamera(dt);

            if (_gauntletLayer == null)
            {
                if (_overlayLoadFailed)
                    return;

                _overlayStartupDelay -= dt;
                if (_overlayStartupDelay <= 0f)
                    TryEnsureLayer();
                return;
            }

            TryTickCommanderBattleOrderBridge();

            _refreshTimer -= dt;
            if (_refreshTimer > 0f)
                return;

            _refreshTimer = RefreshIntervalSeconds;
            RefreshOverlay(force: false, hasLocalControlledAgent);
        }

        public override void OnMissionScreenFinalize()
        {
            try
            {
                ReleaseOverlayInput();
                ReleaseCommanderBattleOrderBridge("mission-screen-finalize");
                ReleaseCurrentMovie();
                ReleaseAiControlHintLayer();
                CoopSiegeDeploymentBoundaryRuntime.TryRemoveVisibleDeploymentBoundaryMarkers(
                    Mission,
                    MissionScreen,
                    "mission-screen-finalize");
                TryDeactivateCommanderDeploymentFreeCamera(MissionScreen, "mission-screen-finalize");
                _commanderDeploymentOrderOfBattleActive = false;
                _commanderDeploymentPlacementInputActive = false;
                _commanderBattleOrderActive = false;

                if (_gauntletLayer != null)
                {
                    MissionScreen?.RemoveLayer(_gauntletLayer);
                    _gauntletLayer = null;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: finalize failed: " + ex.Message);
            }

            _currentScreen = CoopSelectionScreen.None;
            _activeAiObservationAgentIndex = -1;
            _wasAiObservationActive = false;
            _reconnectSelectionContractActive = false;
            _lastReconnectSelectionContractLogKey = string.Empty;
            _lastCommanderBattleOrderBridgeContextKey = string.Empty;
            _lastCommanderBattleOrderEmptySetGuardKey = string.Empty;
            base.OnMissionScreenFinalize();
        }

        public override bool OnEscape()
        {
            if (GameNetwork.IsClient && ExperimentalFeatures.EnableCustomCoopSelectionOverlay)
            {
                if (TryCloseCommanderBattleOrderMenu("escape"))
                    return true;

                if (_currentScreen == CoopSelectionScreen.CommanderDeployment)
                {
                    TryDeactivateNativeCommanderDeploymentPlacement(MissionScreen);
                    return base.OnEscape();
                }

                if (_currentScreen != CoopSelectionScreen.None || _inputCaptured)
                {
                    _spectatorOverlayHidden = true;
                    ReleaseOverlayInput();
                    ReleaseCurrentMovie();
                    UpdateOverlayInputState(false);
                    return false;
                }
            }

            return base.OnEscape();
        }

        private void TryEnsureLayer()
        {
            if (_gauntletLayer != null || !GameNetwork.IsClient || !ExperimentalFeatures.EnableCustomCoopSelectionOverlay)
                return;

            try
            {
                ScreenBase missionScreen = MissionScreen;
                string missionScreenName = missionScreen?.GetType().FullName ?? "<null>";
                if (missionScreen == null)
                {
                    ModLogger.Info("CoopMissionSelectionView: mission screen is null, delaying coop selection shell init.");
                    return;
                }

                if (missionScreenName.IndexOf("MissionScreen", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    ModLogger.Info("CoopMissionSelectionView: screen is not MissionScreen yet (" + missionScreenName + "), delaying coop selection shell init.");
                    return;
                }

                _gauntletLayer = new GauntletLayer("CoopSelectionLayer", ViewOrderPriority, false);
                _gauntletLayer.IsFocusLayer = true;
                missionScreen.AddLayer(_gauntletLayer);
                ModLogger.Info("CoopMissionSelectionView: coop selection layer added.");

                if (!ExperimentalFeatures.EnableCustomCoopSelectionMovieLoad)
                {
                    ModLogger.Info("CoopMissionSelectionView: movie load disabled by feature flag; coop selection layer kept empty.");
                    return;
                }

                RefreshOverlay(force: true, HasLocalControlledAgent());
            }
            catch (Exception ex)
            {
                _overlayLoadFailed = true;
                ModLogger.Error("CoopMissionSelectionView: coop selection shell init failed.", ex);
                CleanupLayerState();
            }
        }

        private void RefreshOverlay(bool force, bool hasLocalControlledAgent)
        {
            if (_gauntletLayer == null)
                return;

            if (CoopBattleAgentControlRuntimeState.IsClientAiObservationOrTransitionActive())
            {
                ReleaseOverlayInput();
                ReleaseCurrentMovie();
                UpdateOverlayInputState(false);
                return;
            }

            CoopSelectionUiSnapshot snapshot = BuildCurrentSnapshot(hasLocalControlledAgent);
            CoopSelectionScreen desiredScreen = DetermineDesiredScreen(snapshot);
            if (desiredScreen == CoopSelectionScreen.None)
            {
                ReleaseCurrentMovie();
                ClearCameraPreviewTarget("overlay-hidden");
                UpdateOverlayInputState(false);
                return;
            }

            bool loadedNewScreen;
            try
            {
                loadedNewScreen = EnsureScreenLoaded(snapshot, desiredScreen);
            }
            catch (Exception ex)
            {
                ModLogger.Error("CoopMissionSelectionView: failed to load coop selection screen.", ex);
                _requestedScreen = CoopSelectionScreen.ClassLoadout;
                ReleaseOverlayInput();
                ReleaseCurrentMovie();
                return;
            }
            string refreshKey = GetRefreshKey(snapshot, desiredScreen);
            bool needsRefresh = force || loadedNewScreen || !string.Equals(_lastAppliedRefreshKey, refreshKey, StringComparison.Ordinal);
            if (needsRefresh && !loadedNewScreen)
                _screenViewModel?.Refresh(snapshot, force);

            if (needsRefresh)
                _lastAppliedRefreshKey = refreshKey;

            UpdateCameraPreviewTarget(snapshot, desiredScreen, hasLocalControlledAgent);
            UpdateOverlayInputState(true);
        }

        private CoopSelectionUiSnapshot BuildCurrentSnapshot(bool hasLocalControlledAgent)
        {
            UpdateReconnectSelectionContractState(hasLocalControlledAgent);
            return CoopSelectionUiHelpers.BuildSnapshot(
                _selectedSideOverride,
                _selectedEntryIdOverride,
                hasLocalControlledAgent,
                _reconnectSelectionContractActive,
                Mission?.SceneName ?? string.Empty,
                _missionScreenInitializedUtc);
        }

        private void UpdateReconnectSelectionContractState(bool hasLocalControlledAgent)
        {
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status = ReadCurrentMissionEntryStatus();
            bool nextState = false;
            string reason = "inactive";
            if (!hasLocalControlledAgent && status != null)
            {
                if (IsReconnectFinalizeStage(status))
                {
                    nextState = true;
                    reason = "reconnect-finalize";
                }
                else if (_reconnectSelectionContractActive && ShouldContinueReconnectSelectionContract(status))
                {
                    nextState = true;
                    reason = "reconnect-selection";
                }
            }

            string logKey = string.Join("|", new[]
            {
                nextState.ToString(),
                reason,
                status?.BattleDataReadinessStage ?? string.Empty,
                status?.BattleDataReady.ToString() ?? bool.FalseString,
                status?.AssignedSide ?? string.Empty,
                status?.HasAgent.ToString() ?? bool.FalseString,
                status?.LifecycleState ?? string.Empty
            });
            if (!string.Equals(_lastReconnectSelectionContractLogKey, logKey, StringComparison.Ordinal))
            {
                _lastReconnectSelectionContractLogKey = logKey;
                ModLogger.Info(
                    "CoopMissionSelectionView: reconnect selection contract state. " +
                    "Active=" + nextState +
                    " Reason=" + reason +
                    " Stage=" + (status?.BattleDataReadinessStage ?? string.Empty) +
                    " BattleDataReady=" + (status?.BattleDataReady.ToString() ?? bool.FalseString) +
                    " AssignedSide=" + (status?.AssignedSide ?? string.Empty) +
                    " HasAgent=" + (status?.HasAgent.ToString() ?? bool.FalseString) +
                    " Lifecycle=" + (status?.LifecycleState ?? string.Empty));
            }

            _reconnectSelectionContractActive = nextState;
        }

        private CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot ReadCurrentMissionEntryStatus()
        {
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status = CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (status == null)
                return null;

            if (IsEntryStatusCurrentForMission(status))
                return status;

            LogIgnoredEntryStatus(status);
            return null;
        }

        private bool IsEntryStatusCurrentForMission(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status)
        {
            if (status == null)
                return false;

            if (_missionScreenInitializedUtc != DateTime.MinValue &&
                status.UpdatedUtc != DateTime.MinValue &&
                status.UpdatedUtc < _missionScreenInitializedUtc)
            {
                return false;
            }

            string missionName = Mission?.SceneName ?? string.Empty;
            string statusMissionName = status.MissionName ?? string.Empty;
            return string.IsNullOrWhiteSpace(missionName) ||
                   string.IsNullOrWhiteSpace(statusMissionName) ||
                   string.Equals(missionName, statusMissionName, StringComparison.OrdinalIgnoreCase);
        }

        private void LogIgnoredEntryStatus(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status)
        {
            string missionName = Mission?.SceneName ?? string.Empty;
            string key = string.Join("|", new[]
            {
                missionName,
                status?.MissionName ?? string.Empty,
                status?.UpdatedUtc.ToString("O") ?? string.Empty,
                status?.HasAgent.ToString() ?? bool.FalseString,
                status?.BattleDataReady.ToString() ?? bool.FalseString,
                status?.AssignedSide ?? string.Empty,
                status?.LifecycleState ?? string.Empty
            });
            if (string.Equals(_lastIgnoredEntryStatusLogKey, key, StringComparison.Ordinal))
                return;

            _lastIgnoredEntryStatusLogKey = key;
            ModLogger.Info(
                "CoopMissionSelectionView: ignored stale entry status. " +
                "Mission=" + (missionName ?? string.Empty) +
                " MissionInitUtc=" + _missionScreenInitializedUtc.ToString("O") +
                " StatusMission=" + (status?.MissionName ?? string.Empty) +
                " StatusUpdatedUtc=" + (status?.UpdatedUtc.ToString("O") ?? string.Empty) +
                " HasAgent=" + (status?.HasAgent.ToString() ?? bool.FalseString) +
                " BattleDataReady=" + (status?.BattleDataReady.ToString() ?? bool.FalseString) +
                " AssignedSide=" + (status?.AssignedSide ?? string.Empty) +
                " Lifecycle=" + (status?.LifecycleState ?? string.Empty));
        }

        private static bool IsReconnectFinalizeStage(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status)
        {
            return string.Equals(
                status?.BattleDataReadinessStage,
                "ReconnectFinalize",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldContinueReconnectSelectionContract(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status)
        {
            if (status == null || status.HasAgent || !status.BattleDataReady)
                return false;

            if (CoopSelectionUiHelpers.NormalizeStatusSide(status.AssignedSide) == BattleSideEnum.None)
                return false;

            string readinessStage = status.BattleDataReadinessStage ?? string.Empty;
            return string.Equals(readinessStage, "RespawnSelection", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(readinessStage, "UnitSelection", StringComparison.OrdinalIgnoreCase);
        }

        private CoopSelectionScreen DetermineDesiredScreen(CoopSelectionUiSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.CanShowOverlay || _spectatorOverlayHidden)
                return CoopSelectionScreen.None;

            if (EnableManualSiegeCommanderDeployment &&
                IsNativeDeploymentUiActive() &&
                _requestedScreen != CoopSelectionScreen.CommanderDeployment &&
                _currentScreen != CoopSelectionScreen.CommanderDeployment)
            {
                return CoopSelectionScreen.None;
            }

            if (ShouldKeepOverlaySuppressedWhileAwaitingLocalSpawn(snapshot))
            {
                return CoopSelectionScreen.None;
            }

            if (DateTime.UtcNow < _overlaySuppressedUntilUtc)
                return CoopSelectionScreen.None;

            if (!snapshot.BattleDataReady)
            {
                if (ShouldKeepCommanderDeploymentScreenDuringTransient(snapshot))
                    return CoopSelectionScreen.CommanderDeployment;

                return IsReconnectFinalizePendingWithAssignedSide(snapshot)
                    ? CoopSelectionScreen.None
                    : CoopSelectionScreen.TeamSelection;
            }

            if (snapshot.ReconnectSelectionContractActive)
                return DetermineReconnectDesiredScreen(snapshot);

            if (_requestedScreen == CoopSelectionScreen.CommanderDeployment)
            {
                if (IsCommanderDeploymentReady(snapshot) ||
                    ShouldKeepCommanderDeploymentScreenDuringTransient(snapshot))
                {
                    return CoopSelectionScreen.CommanderDeployment;
                }
            }

            if (_requestedScreen == CoopSelectionScreen.ClassLoadout &&
                _selectedSideOverride != BattleSideEnum.None &&
                snapshot.EffectiveSide == _selectedSideOverride &&
                IsUnitSelectionReady(snapshot))
            {
                return CoopSelectionScreen.ClassLoadout;
            }

            return CoopSelectionScreen.TeamSelection;
        }

        private bool ShouldKeepCommanderDeploymentScreenDuringTransient(CoopSelectionUiSnapshot snapshot)
        {
            if (!EnableManualSiegeCommanderDeployment ||
                _requestedScreen != CoopSelectionScreen.CommanderDeployment ||
                _currentScreen != CoopSelectionScreen.CommanderDeployment ||
                _commanderDeploymentViewModel == null ||
                !IsCurrentCommanderDeploymentScenario(Mission))
            {
                return false;
            }

            if (snapshot?.HasLocalControlledAgent == true ||
                snapshot?.Status?.HasAgent == true ||
                snapshot?.IsBattleEnded == true)
            {
                return false;
            }

            string battlePhase = snapshot?.BattlePhase ?? string.Empty;
            if (string.Equals(battlePhase, nameof(CoopBattlePhase.BattleActive), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(battlePhase, nameof(CoopBattlePhase.BattleEnded), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            BattleSideEnum side = snapshot?.EffectiveSide ?? BattleSideEnum.None;
            if (side == BattleSideEnum.None)
                side = _selectedSideOverride;
            if (side == BattleSideEnum.None)
                return false;

            string entryId = !string.IsNullOrWhiteSpace(snapshot?.SelectedEntryId)
                ? snapshot.SelectedEntryId
                : _selectedEntryIdOverride;
            if (string.IsNullOrWhiteSpace(entryId))
                return false;

            RosterEntryState entryState = CoopSelectionUiHelpers.ResolveEntryState(side, entryId);
            return entryState == null ||
                   CoopSelectionUiHelpers.IsCommanderEntry(snapshot?.BattleState, side, entryState);
        }

        private bool IsNativeDeploymentUiActive()
        {
            ScreenBase missionScreen = MissionScreen;
            if (missionScreen == null)
                return false;

            bool? isDeploymentActive = TryGetInstanceProperty<bool>(missionScreen, "IsDeploymentActive");
            return isDeploymentActive == true;
        }

        private CoopSelectionScreen DetermineReconnectDesiredScreen(CoopSelectionUiSnapshot snapshot)
        {
            if (snapshot == null)
                return CoopSelectionScreen.None;

            if (snapshot.AuthoritativeAssignedSide != BattleSideEnum.None)
                return IsUnitSelectionReady(snapshot)
                    ? CoopSelectionScreen.ClassLoadout
                    : CoopSelectionScreen.None;

            if (_requestedScreen == CoopSelectionScreen.ClassLoadout &&
                snapshot.EffectiveSide != BattleSideEnum.None &&
                IsUnitSelectionReady(snapshot))
            {
                return CoopSelectionScreen.ClassLoadout;
            }

            return CoopSelectionScreen.TeamSelection;
        }

        private static bool IsUnitSelectionReady(CoopSelectionUiSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.BattleDataReady || snapshot.EffectiveSide == BattleSideEnum.None)
                return false;

            string readinessStage = snapshot.BattleDataReadinessStage ?? string.Empty;
            return string.Equals(readinessStage, "UnitSelection", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(readinessStage, "RespawnSelection", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsCommanderDeploymentReady(CoopSelectionUiSnapshot snapshot)
        {
            if (snapshot == null ||
                !snapshot.BattleDataReady ||
                snapshot.EffectiveSide == BattleSideEnum.None ||
                string.IsNullOrWhiteSpace(snapshot.SelectedEntryId))
            {
                return false;
            }

            string battlePhase = snapshot.BattlePhase ?? string.Empty;
            if (string.Equals(battlePhase, nameof(CoopBattlePhase.BattleActive), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(battlePhase, nameof(CoopBattlePhase.BattleEnded), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string readinessStage = snapshot.BattleDataReadinessStage ?? string.Empty;
            if (!string.Equals(readinessStage, "UnitSelection", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(readinessStage, "CommanderDeployment", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            RosterEntryState entryState = CoopSelectionUiHelpers.ResolveEntryState(
                snapshot.EffectiveSide,
                snapshot.SelectedEntryId);
            if (CoopSelectionUiHelpers.IsCommanderEntry(
                snapshot.BattleState,
                snapshot.EffectiveSide,
                entryState))
            {
                return true;
            }

            return false;
        }

        private static string ResolveCommanderDeploymentAuthorityEntryId(
            CoopSelectionUiSnapshot snapshot)
        {
            BattleSideEnum side = snapshot?.EffectiveSide ?? BattleSideEnum.None;
            if (side == BattleSideEnum.None)
                return null;

            BattleRuntimeState runtimeState =
                snapshot?.BattleState ??
                BattleSnapshotRuntimeState.GetState();
            RosterEntryState commanderEntry =
                BattleCommanderResolver.ResolveCommanderEntry(runtimeState, side);
            return string.IsNullOrWhiteSpace(commanderEntry?.EntryId)
                ? null
                : commanderEntry.EntryId;
        }

        private static bool IsReconnectFinalizePendingWithAssignedSide(CoopSelectionUiSnapshot snapshot)
        {
            if (snapshot == null || snapshot.BattleDataReady || snapshot.AuthoritativeAssignedSide == BattleSideEnum.None)
                return false;

            return string.Equals(
                snapshot.BattleDataReadinessStage,
                "ReconnectFinalize",
                StringComparison.OrdinalIgnoreCase);
        }

        private bool EnsureScreenLoaded(CoopSelectionUiSnapshot snapshot, CoopSelectionScreen desiredScreen)
        {
            if (_currentScreen == desiredScreen &&
                _viewModel != null &&
                (desiredScreen == CoopSelectionScreen.CommanderDeployment || _screenViewModel != null))
            {
                return false;
            }

            ReleaseCurrentMovie();

            if (desiredScreen == CoopSelectionScreen.TeamSelection)
            {
                var vm = new CoopTeamSelectionVM(snapshot, HandleSideSelected, HandleAutoAssignRequested, HandleSpectatorRequested);
                _viewModel = vm;
                _screenViewModel = vm;
                _movie = _gauntletLayer.LoadMovie(TeamMovieName, vm);
                _currentScreen = desiredScreen;
                _lastAppliedRefreshKey = GetRefreshKey(snapshot, desiredScreen);
                ModLogger.Info("CoopMissionSelectionView: loaded coop team selection shell.");
                return true;
            }

            if (desiredScreen == CoopSelectionScreen.CommanderDeployment)
            {
                _commanderDeploymentOrderOfBattleActive = true;
                OrderOfBattleVM commanderVm = null;
                try
                {
                    EnsureCommanderDeploymentSpriteCategoryLoaded();
                    commanderVm = CreateNativeCommanderDeploymentViewModel(snapshot);

                    _viewModel = commanderVm;
                    _commanderDeploymentViewModel = commanderVm;
                    _screenViewModel = null;
                    _movie = _gauntletLayer.LoadMovie(CommanderDeploymentMovieName, commanderVm);
                    TryEnsureCommanderDeploymentOrderMovie();
                    TryEnsureCommanderSiegeMachineDeploymentMovie(snapshot);
                    _currentScreen = desiredScreen;
                    _lastAppliedRefreshKey = GetRefreshKey(snapshot, desiredScreen);
                    ModLogger.Info("CoopMissionSelectionView: loaded native OrderOfBattle commander deployment shell.");
                    return true;
                }
                catch
                {
                    _commanderDeploymentOrderOfBattleActive = false;
                    ReleaseCurrentMovie();
                    throw;
                }
            }

            var classVm = new CoopClassLoadoutVM(snapshot, HandleUnitSelected, HandleSpawnRequested, HandleBackRequested);
            _viewModel = classVm;
            _screenViewModel = classVm;
            _movie = _gauntletLayer.LoadMovie(ClassMovieName, classVm);
            _currentScreen = desiredScreen;
            _lastAppliedRefreshKey = GetRefreshKey(snapshot, desiredScreen);
            ModLogger.Info("CoopMissionSelectionView: loaded coop class loadout shell.");
            return true;
        }

        private OrderOfBattleVM CreateNativeCommanderDeploymentViewModel(CoopSelectionUiSnapshot snapshot)
        {
            Mission mission = Mission;
            if (mission == null)
                throw new InvalidOperationException("mission-null");

            if (!TryPrepareNativeCommanderDeploymentMissionState(
                    mission,
                    snapshot,
                    out string prepareDiagnostics))
            {
                throw new InvalidOperationException("native-order-of-battle-prepare-failed " + prepareDiagnostics);
            }

            string siegeUiDiagnostics = "not-required-formation-only";
            BattleScenarioContextMessage scenarioContext =
                snapshot?.BattleState?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetScenarioContext();
            BattleSideEnum side = snapshot?.EffectiveSide ?? mission.PlayerTeam?.Side ?? BattleSideEnum.None;
            if (side == BattleSideEnum.None)
                side = _selectedSideOverride;
            bool useExactLandBattleFormationPlacement =
                ExactCampaignCommanderDeploymentRuntime.IsExactLandBattleScenario(
                    mission,
                    scenarioContext);
            bool useMountedFormationClasses =
                ShouldUseMountedCommanderDeploymentFormationClasses(
                    mission,
                    scenarioContext,
                    side);
            if (!useExactLandBattleFormationPlacement)
            {
                ExactCampaignSiegeAssaultWithDeploymentRuntime.TryEnsureCommanderDeploymentUiContract(
                    mission,
                    scenarioContext,
                    side,
                    out siegeUiDiagnostics);
            }

            Camera missionCamera = ResolveMissionScreenCombatCamera();
            if (missionCamera == null)
                throw new InvalidOperationException("combat-camera-null");

            if (useExactLandBattleFormationPlacement)
            {
                if (!ExactCampaignCommanderDeploymentRuntime.TryBeginClientManualFormationPlacement(
                        mission,
                        out string placementDiagnostics))
                {
                    throw new InvalidOperationException(
                        "manual-formation-placement-start-failed " + placementDiagnostics);
                }

                _landBattleManualFormationPlacementActive = true;
            }
            if (useMountedFormationClasses)
                CoopSiegeOrderOfBattleVM.BeginInitialMountedConfiguration();

            CoopSiegeOrderOfBattleVM commanderVm = null;
            try
            {
                commanderVm = new CoopSiegeOrderOfBattleVM(
                    projectMountedClassesToSiegeFootClasses: !useMountedFormationClasses);
                commanderVm.Initialize(
                    mission,
                    missionCamera,
                    SelectNativeCommanderFormationAtIndex,
                    DeselectNativeCommanderFormationAtIndex,
                    ClearNativeCommanderFormationSelection,
                    HandleCommanderAutoDeployRequested,
                    HandleCommanderReadyRequested,
                    new Dictionary<int, Agent>());
                if (useMountedFormationClasses)
                    commanderVm.NormalizeMountedFormationComposition();
                commanderVm.EnableReusableCompanionCaptainAssignments();
                commanderVm.IsEnabled = true;
                commanderVm.AreCameraControlsEnabled = false;
                commanderVm.CanStartMission = true;
                commanderVm.AutoDeployText = GameTexts.FindText("str_auto_deploy").ToString();
                TryRegisterOrderOfBattleHotKeys(commanderVm);
                TryCreateCommanderDeploymentOrderBridge(mission, missionCamera);
                TryAttachCommanderDeploymentOrderTroopPlacerCallback(commanderVm);
                TryRefreshNativeCommanderOrderOfBattleCounts(commanderVm, mission, "post-initialize");
            }
            catch
            {
                if (_landBattleManualFormationPlacementActive)
                {
                    ExactCampaignCommanderDeploymentRuntime.EndManualFormationPlacement(
                        mission,
                        "commander-deployment-initialization-failed");
                    _landBattleManualFormationPlacementActive = false;
                }

                throw;
            }
            finally
            {
                if (useMountedFormationClasses)
                    CoopSiegeOrderOfBattleVM.EndInitialMountedConfiguration();
            }

            if (useMountedFormationClasses)
            {
                OrderOfBattleSiegeProjectedCountsPatch.TrySyncCommanderDeploymentFormationAssignmentsForTeam(
                    mission.PlayerTeam,
                    "CoopMissionSelectionView mounted initialization completed");
            }

            ModLogger.Info(
                "CoopMissionSelectionView: prepared native OrderOfBattle commander deployment. " +
                "Diagnostics={" + (prepareDiagnostics ?? string.Empty) + "} " +
                "SiegeUi={" + (siegeUiDiagnostics ?? string.Empty) + "}");
            return commanderVm;
        }

        private static void TryRefreshNativeCommanderOrderOfBattleCounts(
            OrderOfBattleVM commanderVm,
            Mission mission,
            string source)
        {
            if (commanderVm == null)
                return;

            bool troopLookupRefreshed = TryInvokeOrderOfBattlePrivateMethod(commanderVm, "UpdateTroopTypeLookUpTable");
            bool projectedCountsRefreshed = TryRefreshProjectedSiegeOrderOfBattleCounts(commanderVm, mission);
            bool weightsRefreshed = TryInvokeOrderOfBattlePrivateMethod(commanderVm, "RefreshWeights");
            projectedCountsRefreshed |= TryRefreshProjectedSiegeOrderOfBattleCounts(commanderVm, mission);

            try
            {
                commanderVm.OnUnitDeployed();
                projectedCountsRefreshed |= TryRefreshProjectedSiegeOrderOfBattleCounts(commanderVm, mission);
            }
            catch
            {
            }

            if (CoopDebugConfig.OrderOfBattleDiagnostics)
            {
                LogNativeCommanderOrderOfBattleDiagnostics(
                    commanderVm,
                    mission,
                    source,
                    troopLookupRefreshed,
                    weightsRefreshed,
                    projectedCountsRefreshed);
            }
        }

        private static void TryRefreshCommanderDeploymentCountsAfterSelection(
            OrderOfBattleVM commanderVm,
            Mission mission,
            string source)
        {
            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetScenarioContext() ??
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            if (SiegeAmbushScenarioContract.IsSiegeAmbushScenario(scenarioContext))
            {
                // Selecting a formation does not change its composition. Re-running the
                // native and projected count pipelines here made the two presentations
                // briefly replace each other on every click in exact SallyOut deployment.
                return;
            }

            TryRefreshNativeCommanderOrderOfBattleCounts(commanderVm, mission, source);
        }

        private static bool TryInvokeOrderOfBattlePrivateMethod(OrderOfBattleVM commanderVm, string methodName)
        {
            if (commanderVm == null || string.IsNullOrWhiteSpace(methodName))
                return false;

            try
            {
                MethodInfo method = typeof(OrderOfBattleVM).GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (method == null)
                    return false;

                method.Invoke(commanderVm, null);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: native OrderOfBattle private refresh failed. " +
                    "Method=" + methodName +
                    " Error=" + ex.GetType().Name + ":" + ex.Message);
                return false;
            }
        }

        private static bool TryRefreshProjectedSiegeOrderOfBattleCounts(OrderOfBattleVM commanderVm, Mission mission)
        {
            if (commanderVm == null ||
                mission == null ||
                !IsCommanderDeploymentSiegeProjectionActive())
            {
                return false;
            }

            try
            {
                FieldInfo lookupField = typeof(OrderOfBattleVM).GetField(
                    "_visibleTroopTypeCountLookup",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo formationsField = typeof(OrderOfBattleVM).GetField(
                    "_allFormations",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (!(lookupField?.GetValue(commanderVm) is IDictionary<FormationClass, int> lookup) ||
                    !(formationsField?.GetValue(commanderVm) is IEnumerable<OrderOfBattleFormationItemVM> formationItems))
                {
                    return false;
                }

                int infantryCount = 0;
                int rangedCount = 0;
                var items = formationItems.Where(item => item?.Formation != null).ToList();
                foreach (OrderOfBattleFormationItemVM formationItem in items)
                {
                    infantryCount += CountProjectedSiegeOrderOfBattleUnitsInClass(
                        formationItem.Formation,
                        FormationClass.Infantry);
                    rangedCount += CountProjectedSiegeOrderOfBattleUnitsInClass(
                        formationItem.Formation,
                        FormationClass.Ranged);
                }

                lookup[FormationClass.Infantry] = infantryCount;
                lookup[FormationClass.Ranged] = rangedCount;
                lookup[FormationClass.Cavalry] = infantryCount;
                lookup[FormationClass.HorseArcher] = rangedCount;

                foreach (OrderOfBattleFormationItemVM formationItem in items)
                {
                    formationItem.OnSizeChanged();
                    foreach (OrderOfBattleFormationClassVM classVm in formationItem.Classes)
                    {
                        if (classVm == null)
                            continue;

                        FormationClass projectedClass = DismountSiegeOrderOfBattleFormationClass(classVm.Class.FallbackClass());
                        if (projectedClass != FormationClass.Infantry && projectedClass != FormationClass.Ranged)
                        {
                            classVm.TroopCountText = string.Empty;
                            continue;
                        }

                        int formationClassCount = CountProjectedSiegeOrderOfBattleUnitsInClass(
                            formationItem.Formation,
                            classVm.Class);
                        int totalClassCount = GetProjectedSiegeOrderOfBattleTotalCount(lookup, classVm.Class);
                        classVm.TroopCountText = FormatOrderOfBattleTroopCountText(
                            formationClassCount,
                            totalClassCount);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: projected siege OrderOfBattle count refresh failed open. " +
                    "Error=" + ex.GetType().Name + ":" + ex.Message);
                return false;
            }
        }

        private static int CountProjectedSiegeOrderOfBattleUnitsInClass(Formation formation, FormationClass formationClass)
        {
            if (formation == null)
                return 0;

            FormationClass projectedClass = DismountSiegeOrderOfBattleFormationClass(formationClass.FallbackClass());
            if (projectedClass != FormationClass.Infantry && projectedClass != FormationClass.Ranged)
                return 0;

            return formation.GetCountOfUnitsWithCondition(agent =>
                ResolveProjectedSiegeOrderOfBattleAgentClass(agent) == projectedClass);
        }

        internal static bool IsCommanderDeploymentProjectedAgentInFormationClass(Agent agent, FormationClass formationClass)
        {
            FormationClass projectedClass = DismountSiegeOrderOfBattleFormationClass(formationClass.FallbackClass());
            if (projectedClass != FormationClass.Infantry && projectedClass != FormationClass.Ranged)
                return false;

            return ResolveProjectedSiegeOrderOfBattleAgentClass(agent) == projectedClass;
        }

        private static FormationClass ResolveProjectedSiegeOrderOfBattleAgentClass(Agent agent)
        {
            if (agent == null || agent.IsMount)
                return FormationClass.NumberOfAllFormations;

            if (!agent.HasMount && agent.IsRangedCached)
                return FormationClass.Ranged;

            FormationClass formationClass = FormationClass.NumberOfAllFormations;
            BasicCharacterObject character = agent.Character;
            if (character != null)
            {
                try
                {
                    BattleSideEnum side = agent.Team?.Side ?? BattleSideEnum.None;
                    if (Mission.Current != null && side != BattleSideEnum.None)
                        formationClass = Mission.Current.GetAgentTroopClass(side, character);
                    else
                        formationClass = character.DefaultFormationClass;
                }
                catch
                {
                    formationClass = character.DefaultFormationClass;
                }
            }

            if (!IsDefaultOrderOfBattleFormationClass(formationClass))
                return agent.IsRangedCached ? FormationClass.Ranged : FormationClass.Infantry;

            formationClass = DismountSiegeOrderOfBattleFormationClass(formationClass.FallbackClass());
            if (formationClass == FormationClass.Ranged || formationClass == FormationClass.Infantry)
                return formationClass;

            return agent.IsRangedCached ? FormationClass.Ranged : FormationClass.Infantry;
        }

        private static FormationClass DismountSiegeOrderOfBattleFormationClass(FormationClass formationClass)
        {
            if (formationClass == FormationClass.Cavalry)
                return FormationClass.Infantry;

            if (formationClass == FormationClass.HorseArcher)
                return FormationClass.Ranged;

            return formationClass;
        }

        private static bool IsDefaultOrderOfBattleFormationClass(FormationClass formationClass)
        {
            return formationClass >= FormationClass.Infantry &&
                   formationClass < FormationClass.NumberOfDefaultFormations;
        }

        private static int GetProjectedSiegeOrderOfBattleTotalCount(
            IDictionary<FormationClass, int> lookup,
            FormationClass formationClass)
        {
            if (lookup == null)
                return 0;

            FormationClass projectedClass = DismountSiegeOrderOfBattleFormationClass(formationClass.FallbackClass());
            return lookup.TryGetValue(projectedClass, out int count) ? count : 0;
        }

        private static string FormatOrderOfBattleTroopCountText(int left, int right)
        {
            try
            {
                return GameTexts.FindText("str_LEFT_over_RIGHT")
                    .SetTextVariable("LEFT", left)
                    .SetTextVariable("RIGHT", right)
                    .ToString();
            }
            catch
            {
                return left + " / " + right;
            }
        }

        private static void LogNativeCommanderOrderOfBattleDiagnostics(
            OrderOfBattleVM commanderVm,
            Mission mission,
            string source,
            bool troopLookupRefreshed,
            bool weightsRefreshed,
            bool projectedCountsRefreshed)
        {
            try
            {
                Team playerTeam = mission?.PlayerTeam;
                string teamSummary =
                    playerTeam == null
                        ? "Team=null"
                        : "Team=" + playerTeam.Side + "#" + playerTeam.TeamIndex +
                          " IsPlayerGeneral=" + playerTeam.IsPlayerGeneral +
                          " Formations=" + playerTeam.FormationsIncludingEmpty.Count;

                ModLogger.Info(
                    "CoopMissionSelectionView: native OrderOfBattle diagnostics. " +
                    "Source=" + (source ?? "unknown") +
                    " TroopLookupRefreshed=" + troopLookupRefreshed +
                    " WeightsRefreshed=" + weightsRefreshed +
                    " ProjectedCountsRefreshed=" + projectedCountsRefreshed +
                    " " + teamSummary +
                    " VisibleLookup={" + BuildOrderOfBattleVisibleLookupSummary(commanderVm) + "}" +
                    " FormationItems={" + BuildOrderOfBattleFormationItemSummary(commanderVm) + "}" +
                    " NativeFormations={" + BuildNativeFormationClassSummary(playerTeam) + "}");
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: native OrderOfBattle diagnostics failed: " + ex.Message);
            }
        }

        private static void LogCommanderDeploymentSiegeMachineDiagnostics(
            MissionOrderVM orderVm,
            Mission mission,
            string source,
            string detail)
        {
            if (!CoopDebugConfig.OrderOfBattleDiagnostics)
                return;

            try
            {
                Team playerTeam = mission?.PlayerTeam;
                MissionOrderDeploymentControllerVM deploymentController = orderVm?.DeploymentController;
                ModLogger.Info(
                    "CoopMissionSelectionView: commander siege deployment diagnostics. " +
                    "Source=" + (source ?? "unknown") +
                    " Team=" + (playerTeam == null ? "<null>" : playerTeam.Side + "#" + playerTeam.TeamIndex) +
                    " HasSiegeEnginesLogic=" + (mission?.GetMissionBehavior<MissionSiegeEnginesLogic>() != null) +
                    " HasSiegeDeploymentHandler=" + (mission?.GetMissionBehavior<SiegeDeploymentHandler>() != null) +
                    " HasSiegeDeploymentController=" + (mission?.GetMissionBehavior<SiegeDeploymentMissionController>() != null) +
                    " SiegeMachineList=" + (deploymentController?.SiegeMachineList?.Count ?? -1) +
                    " DeploymentTargets=" + (deploymentController?.DeploymentTargets?.Count ?? -1) +
                    " SiegeDeploymentList=" + (deploymentController?.SiegeDeploymentList?.Count ?? -1) +
                    " Points={" + BuildCommanderDeploymentPointSummary(mission, playerTeam) + "} " +
                    " Detail={" + (detail ?? string.Empty) + "}");
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: commander siege deployment diagnostics failed: " +
                    ex.GetType().Name + ":" + ex.Message);
            }
        }

        private static string BuildCommanderDeploymentPointSummary(Mission mission, Team team)
        {
            if (mission?.ActiveMissionObjects == null)
                return "mission-null";

            try
            {
                BattleSideEnum side = team?.Side ?? BattleSideEnum.None;
                int total = 0;
                int forSide = 0;
                int deployableWeaponCount = 0;
                int deployedCount = 0;
                foreach (DeploymentPoint deploymentPoint in mission.ActiveMissionObjects.FindAllWithType<DeploymentPoint>())
                {
                    if (deploymentPoint == null || deploymentPoint.IsDisabled)
                        continue;

                    total++;
                    if (side != BattleSideEnum.None && deploymentPoint.Side != side)
                        continue;

                    forSide++;
                    try
                    {
                        deployableWeaponCount += deploymentPoint.DeployableWeapons?.Count() ?? 0;
                    }
                    catch
                    {
                    }

                    if (deploymentPoint.IsDeployed)
                        deployedCount++;
                }

                return "Total=" + total +
                       " Side=" + side +
                       " SidePoints=" + forSide +
                       " DeployableWeapons=" + deployableWeaponCount +
                       " Deployed=" + deployedCount;
            }
            catch (Exception ex)
            {
                return "failed:" + ex.GetType().Name;
            }
        }

        private static string BuildOrderOfBattleVisibleLookupSummary(OrderOfBattleVM commanderVm)
        {
            try
            {
                FieldInfo field = typeof(OrderOfBattleVM).GetField(
                    "_visibleTroopTypeCountLookup",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var lookup = field?.GetValue(commanderVm) as IDictionary<FormationClass, int>;
                if (lookup == null)
                    return "null";

                return
                    "Inf=" + GetLookupCount(lookup, FormationClass.Infantry) +
                    ",Ranged=" + GetLookupCount(lookup, FormationClass.Ranged) +
                    ",Cav=" + GetLookupCount(lookup, FormationClass.Cavalry) +
                    ",HA=" + GetLookupCount(lookup, FormationClass.HorseArcher);
            }
            catch (Exception ex)
            {
                return "failed:" + ex.GetType().Name;
            }
        }

        private static int GetLookupCount(IDictionary<FormationClass, int> lookup, FormationClass formationClass)
        {
            return lookup != null && lookup.TryGetValue(formationClass, out int count) ? count : -1;
        }

        private static string BuildOrderOfBattleFormationItemSummary(OrderOfBattleVM commanderVm)
        {
            try
            {
                FieldInfo field = typeof(OrderOfBattleVM).GetField(
                    "_allFormations",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (!(field?.GetValue(commanderVm) is System.Collections.IEnumerable items))
                    return "null";

                var summaries = new List<string>();
                foreach (object item in items)
                {
                    if (item == null)
                        continue;

                    Formation formation = TryGetPropertyValue<Formation>(item, "Formation");
                    int troopCount = TryGetPropertyValue<int>(item, "TroopCount");
                    int orderClass = TryGetPropertyValue<int>(item, "OrderOfBattleFormationClassInt");
                    string classSummary = BuildOrderOfBattleFormationClassesSummary(item);
                    summaries.Add(
                        "F" + (formation?.Index ?? -1) +
                        ":Count=" + troopCount +
                        ",OobClass=" + orderClass +
                        ",Classes=[" + classSummary + "]");
                }

                return summaries.Count == 0 ? "empty" : string.Join("; ", summaries);
            }
            catch (Exception ex)
            {
                return "failed:" + ex.GetType().Name;
            }
        }

        private static string BuildOrderOfBattleFormationClassesSummary(object formationItem)
        {
            try
            {
                object classes = TryGetPropertyValue<object>(formationItem, "Classes");
                if (!(classes is System.Collections.IEnumerable enumerable))
                    return "null";

                var summaries = new List<string>();
                foreach (object classItem in enumerable)
                {
                    if (classItem == null)
                        continue;

                    FormationClass formationClass = TryGetPropertyValue<FormationClass>(classItem, "Class");
                    int weight = TryGetPropertyValue<int>(classItem, "Weight");
                    string troopCountText = TryGetPropertyValue<string>(classItem, "TroopCountText") ?? string.Empty;
                    summaries.Add(formationClass + ":" + weight + ":" + troopCountText);
                }

                return summaries.Count == 0 ? "empty" : string.Join("|", summaries);
            }
            catch (Exception ex)
            {
                return "failed:" + ex.GetType().Name;
            }
        }

        private static T TryGetPropertyValue<T>(object instance, string propertyName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(propertyName))
                return default(T);

            try
            {
                PropertyInfo property = instance.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object value = property?.GetValue(instance, null);
                return value is T typedValue ? typedValue : default(T);
            }
            catch
            {
                return default(T);
            }
        }

        private static string BuildNativeFormationClassSummary(Team playerTeam)
        {
            if (playerTeam == null)
                return "team-null";

            try
            {
                var summaries = new List<string>();
                foreach (Formation formation in playerTeam.FormationsIncludingEmpty)
                {
                    if (formation == null)
                        continue;

                    FormationQuerySystem query = formation.QuerySystem;
                    string ratios =
                        query == null
                            ? "Q=null"
                            : "Q=" +
                              FormatRatio(query.InfantryUnitRatio) + "/" +
                              FormatRatio(query.RangedUnitRatio) + "/" +
                              FormatRatio(query.CavalryUnitRatio) + "/" +
                              FormatRatio(query.RangedCavalryUnitRatio);
                    summaries.Add(
                        "F" + formation.Index +
                        ":Count=" + formation.CountOfUnits +
                        ",Alive=" + formation.GetCountOfUnitsWithCondition(agent => agent != null && agent.Health > 0f) +
                        ",Logical=" + formation.LogicalClass +
                        ",Physical=" + formation.PhysicalClass +
                        ",Phys=" +
                        CountPhysicalClass(formation, FormationClass.Infantry) + "/" +
                        CountPhysicalClass(formation, FormationClass.Ranged) + "/" +
                        CountPhysicalClass(formation, FormationClass.Cavalry) + "/" +
                        CountPhysicalClass(formation, FormationClass.HorseArcher) +
                        "," + ratios);
                }

                return summaries.Count == 0 ? "empty" : string.Join("; ", summaries);
            }
            catch (Exception ex)
            {
                return "failed:" + ex.GetType().Name;
            }
        }

        private static int CountPhysicalClass(Formation formation, FormationClass formationClass)
        {
            try
            {
                return formation?.GetCountOfUnitsBelongingToPhysicalClass(formationClass, excludeBannerBearers: false) ?? 0;
            }
            catch
            {
                return -1;
            }
        }

        private static string FormatRatio(float value)
        {
            return value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        private bool TryPrepareNativeCommanderDeploymentMissionState(
            Mission mission,
            CoopSelectionUiSnapshot snapshot,
            out string diagnostics)
        {
            diagnostics = "mission-null";
            if (mission == null)
                return false;

            BattleSideEnum side = snapshot?.EffectiveSide ?? BattleSideEnum.None;
            if (side == BattleSideEnum.None)
                side = _selectedSideOverride;
            if (side == BattleSideEnum.None)
            {
                diagnostics = "side-none";
                return false;
            }

            BattleScenarioContextMessage scenarioContext =
                snapshot?.BattleState?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetScenarioContext();
            if (GameNetwork.IsClient &&
                !GameNetwork.IsServer &&
                ExactCampaignCommanderDeploymentRuntime.IsExactVillageBattleScenario(
                    mission,
                    scenarioContext) &&
                !ExactVillageBattleDeploymentBoundaryRuntime.IsClientApplied(
                    mission,
                    out _,
                    out _,
                    out string boundaryReadinessDiagnostics))
            {
                diagnostics =
                    "authoritative-village-boundary-not-ready " +
                    (boundaryReadinessDiagnostics ?? "unknown");
                return false;
            }

            Team playerTeam = ResolveMissionTeamForSide(mission, side);
            if (playerTeam == null)
            {
                diagnostics = "team-null Side=" + side;
                return false;
            }

            string refreshDiagnostics = string.Empty;
            bool relationReady = ReferenceEquals(mission.PlayerTeam, playerTeam) && mission.PlayerEnemyTeam != null;
            if (!relationReady)
            {
                relationReady = TryRefreshMissionPlayerTeamRelationView(
                    mission,
                    playerTeam,
                    "native-order-of-battle",
                    out refreshDiagnostics);
            }

            if (!relationReady && !ReferenceEquals(mission.PlayerTeam, playerTeam))
            {
                try
                {
                    mission.PlayerTeam = playerTeam;
                    relationReady = ReferenceEquals(mission.PlayerTeam, playerTeam);
                    refreshDiagnostics = "fallback-set-player-team RelationReady=" + relationReady;
                }
                catch (Exception ex)
                {
                    refreshDiagnostics = "fallback-set-player-team-failed " + ex.GetType().Name + ":" + ex.Message;
                }
            }

            bool setPlayerRole = false;
            try
            {
                if (!playerTeam.IsPlayerGeneral || playerTeam.IsPlayerSergeant)
                    playerTeam.SetPlayerRole(isPlayerGeneral: true, isPlayerSergeant: false);
                setPlayerRole = playerTeam.IsPlayerGeneral;
            }
            catch (Exception ex)
            {
                diagnostics =
                    "set-player-role-failed " +
                    ex.GetType().Name + ":" + ex.Message +
                    " Refresh={" + refreshDiagnostics + "}";
                return false;
            }

            BannerBearerLogic bannerBearerLogic = mission.GetMissionBehavior<BannerBearerLogic>();
            if (bannerBearerLogic == null)
            {
                diagnostics =
                    "banner-bearer-logic-null" +
                    " Side=" + side +
                    " Team=" + playerTeam.Side + "#" + playerTeam.TeamIndex +
                    " Refresh={" + refreshDiagnostics + "}";
                return false;
            }

            OrderController orderController = playerTeam.PlayerOrderController;
            if (orderController == null)
            {
                diagnostics =
                    "player-order-controller-null" +
                    " Side=" + side +
                    " Team=" + playerTeam.Side + "#" + playerTeam.TeamIndex +
                    " Refresh={" + refreshDiagnostics + "}";
                return false;
            }

            string commanderAuthorityEntryId =
                ResolveCommanderDeploymentAuthorityEntryId(snapshot);
            if (!TryResolveCameraPreviewAgentForEntry(
                    side,
                    commanderAuthorityEntryId,
                    out Agent selectedCommanderAgent) ||
                selectedCommanderAgent?.Team == null ||
                selectedCommanderAgent.Team.Side != side)
            {
                diagnostics =
                    "selected-commander-agent-null" +
                    " Side=" + side +
                    " SelectedEntryId=" + (snapshot?.SelectedEntryId ?? string.Empty) +
                    " CommanderEntryId=" + (commanderAuthorityEntryId ?? string.Empty) +
                    " Team=" + playerTeam.Side + "#" + playerTeam.TeamIndex +
                    " Refresh={" + refreshDiagnostics + "}";
                return false;
            }

            bool orderOwnerAssigned = false;
            try
            {
                orderController.Owner = selectedCommanderAgent;
                orderOwnerAssigned = ReferenceEquals(orderController.Owner, selectedCommanderAgent);
            }
            catch (Exception ex)
            {
                diagnostics =
                    "player-order-owner-set-failed " +
                    ex.GetType().Name + ":" + ex.Message +
                    " Side=" + side +
                    " SelectedEntryId=" + (snapshot?.SelectedEntryId ?? string.Empty) +
                    " CommanderEntryId=" + (commanderAuthorityEntryId ?? string.Empty) +
                    " AgentIndex=" + selectedCommanderAgent.Index +
                    " Refresh={" + refreshDiagnostics + "}";
                return false;
            }

            bool formationContractPrepared = TryPrepareNativeCommanderFormationContract(
                playerTeam,
                selectedCommanderAgent,
                out string formationContractDiagnostics);
            if (!formationContractPrepared)
            {
                diagnostics =
                    "formation-contract-prepare-failed" +
                    " Side=" + side +
                    " SelectedEntryId=" + (snapshot?.SelectedEntryId ?? string.Empty) +
                    " CommanderEntryId=" + (commanderAuthorityEntryId ?? string.Empty) +
                    " AgentIndex=" + selectedCommanderAgent.Index +
                    " FormationContract={" + formationContractDiagnostics + "}" +
                    " Refresh={" + refreshDiagnostics + "}";
                return false;
            }

            diagnostics =
                "Side=" + side +
                " Team=" + playerTeam.Side + "#" + playerTeam.TeamIndex +
                " RelationReady=" + relationReady +
                " PlayerTeam=" + (mission.PlayerTeam == null ? "null" : mission.PlayerTeam.Side + "#" + mission.PlayerTeam.TeamIndex) +
                " PlayerEnemyTeam=" + (mission.PlayerEnemyTeam == null ? "null" : mission.PlayerEnemyTeam.Side + "#" + mission.PlayerEnemyTeam.TeamIndex) +
                " SetPlayerGeneral=" + setPlayerRole +
                " BannerBearerLogic=True" +
                " OrderOwnerAssigned=" + orderOwnerAssigned +
                " OrderOwnerAgentIndex=" + selectedCommanderAgent.Index +
                " FormationContract={" + formationContractDiagnostics + "}" +
                " SelectedEntryId=" + (snapshot?.SelectedEntryId ?? string.Empty) +
                " CommanderEntryId=" + (commanderAuthorityEntryId ?? string.Empty) +
                " Refresh={" + refreshDiagnostics + "}";
            return ReferenceEquals(mission.PlayerTeam, playerTeam);
        }

        private static bool TryPrepareNativeCommanderFormationContract(
            Team playerTeam,
            Agent selectedCommanderAgent,
            out string diagnostics)
        {
            diagnostics = "team-or-commander-null";
            if (playerTeam == null || selectedCommanderAgent == null)
                return false;

            try
            {
                int formationCount = 0;
                int formationsWithUnits = 0;
                int ownedFormationsWithUnits = 0;
                int selectableFormationsWithUnits = 0;
                int physicalClassUnitCount = 0;

                foreach (Formation formation in playerTeam.FormationsIncludingEmpty)
                {
                    if (formation == null || !ReferenceEquals(formation.Team, playerTeam))
                        continue;

                    formationCount++;
                    formation.PlayerOwner = selectedCommanderAgent;

                    if (formation.CountOfUnits <= 0)
                        continue;

                    formationsWithUnits++;
                    if (ReferenceEquals(formation.PlayerOwner, selectedCommanderAgent))
                        ownedFormationsWithUnits++;

                    int aliveCount = formation.GetCountOfUnitsWithCondition(agent => agent != null && agent.Health > 0f);
                    if (aliveCount > 0)
                        selectableFormationsWithUnits++;

                    formation.QuerySystem?.ExpireAfterUnitAddRemove();
                    formation.QuerySystem?.EvaluateAllPreliminaryQueryData();
                    physicalClassUnitCount += CountNativeOrderOfBattlePhysicalClassUnits(formation);
                }

                playerTeam.QuerySystem?.ExpireAfterUnitAddRemove();
                playerTeam.QuerySystem?.Expire();

                diagnostics =
                    "Formations=" + formationCount +
                    " WithUnits=" + formationsWithUnits +
                    " OwnedWithUnits=" + ownedFormationsWithUnits +
                    " SelectableWithUnits=" + selectableFormationsWithUnits +
                    " PhysicalClassUnits=" + physicalClassUnitCount +
                    " CommanderAgentIndex=" + selectedCommanderAgent.Index;
                return formationsWithUnits > 0 &&
                       selectableFormationsWithUnits > 0 &&
                       physicalClassUnitCount > 0 &&
                       ownedFormationsWithUnits == formationsWithUnits;
            }
            catch (Exception ex)
            {
                diagnostics = ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        private static int CountNativeOrderOfBattlePhysicalClassUnits(Formation formation)
        {
            if (formation == null)
                return 0;

            return formation.GetCountOfUnitsBelongingToPhysicalClass(FormationClass.Infantry, excludeBannerBearers: false) +
                   formation.GetCountOfUnitsBelongingToPhysicalClass(FormationClass.Ranged, excludeBannerBearers: false) +
                   formation.GetCountOfUnitsBelongingToPhysicalClass(FormationClass.Cavalry, excludeBannerBearers: false) +
                   formation.GetCountOfUnitsBelongingToPhysicalClass(FormationClass.HorseArcher, excludeBannerBearers: false);
        }

        private bool TryEnsureCommanderDeploymentOrderBridge()
        {
            if (_commanderDeploymentOrderVm != null && _commanderDeploymentOrderVmInitialized)
                return true;

            Mission mission = Mission;
            Camera missionCamera = ResolveMissionScreenCombatCamera();
            if (mission == null || missionCamera == null)
                return false;

            return TryCreateCommanderDeploymentOrderBridge(mission, missionCamera);
        }

        private bool TryCreateCommanderDeploymentOrderBridge(Mission mission, Camera missionCamera)
        {
            ReleaseCommanderDeploymentOrderBridge();

            OrderController orderController = mission?.PlayerTeam?.PlayerOrderController;
            if (orderController == null || missionCamera == null)
                return false;

            try
            {
                string siegeUiDiagnostics = "coop-siege-machine-overlay";
                TryEnsureCommanderDeploymentVisualOrderProviderRegistered();
                _commanderDeploymentOrderVm = new MissionOrderVM(orderController, isDeployment: true, isMultiplayer: false);
                _commanderDeploymentOrderVm.IsDeployment = true;
                _commanderDeploymentOrderVm.SetCallbacks(CreateCommanderDeploymentOrderCallbacks());
                _commanderDeploymentOrderVm.SetDeploymentParemeters(
                    missionCamera,
                    BuildCommanderDeploymentPointList(mission, mission.PlayerTeam));
                TryRegisterMissionOrderHotKeys(_commanderDeploymentOrderVm);
                _commanderDeploymentOrderVm.AfterInitialize();
                TryInvokeInstanceMethodSuccessfully(_commanderDeploymentOrderVm.TroopController, "UpdateTroops");
                _commanderDeploymentOrderVm.UpdateCanUseShortcuts(true);
                _commanderDeploymentOrderVmInitialized = true;
                TryEnsureCommanderDeploymentOrderMovie();
                LogCommanderDeploymentSiegeMachineDiagnostics(
                    _commanderDeploymentOrderVm,
                    mission,
                    "create-mission-order-bridge",
                    siegeUiDiagnostics);
                ModLogger.Info("CoopMissionSelectionView: prepared safe commander MissionOrderVM bridge.");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: failed to prepare safe commander MissionOrderVM bridge: " +
                    ex.GetType().Name + ":" + ex.Message);
                ReleaseCommanderDeploymentOrderBridge();
                return false;
            }
        }

        private void TryEnsureCommanderDeploymentVisualOrderProviderRegistered()
        {
            if (_commanderDeploymentVisualOrderProviderRegistered)
                return;

            try
            {
                _commanderDeploymentVisualOrderProvider = new CoopCommanderDeploymentVisualOrderProvider();
                VisualOrderFactory.RegisterProvider(_commanderDeploymentVisualOrderProvider);
                _commanderDeploymentVisualOrderProviderRegistered = true;
                ModLogger.Info("CoopMissionSelectionView: registered commander deployment visual order provider.");
            }
            catch (Exception ex)
            {
                _commanderDeploymentVisualOrderProvider = null;
                _commanderDeploymentVisualOrderProviderRegistered = false;
                ModLogger.Info(
                    "CoopMissionSelectionView: commander deployment visual order provider registration failed: " +
                    ex.GetType().Name + ":" + ex.Message);
            }
        }

        private void ReleaseCommanderDeploymentVisualOrderProvider()
        {
            if (!_commanderDeploymentVisualOrderProviderRegistered ||
                _commanderDeploymentVisualOrderProvider == null)
            {
                _commanderDeploymentVisualOrderProvider = null;
                _commanderDeploymentVisualOrderProviderRegistered = false;
                return;
            }

            try
            {
                VisualOrderFactory.UnregisterProvider(_commanderDeploymentVisualOrderProvider);
                ModLogger.Info("CoopMissionSelectionView: unregistered commander deployment visual order provider.");
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: commander deployment visual order provider unregister failed: " +
                    ex.GetType().Name + ":" + ex.Message);
            }
            finally
            {
                _commanderDeploymentVisualOrderProvider = null;
                _commanderDeploymentVisualOrderProviderRegistered = false;
            }
        }

        private bool TryEnsureCommanderDeploymentOrderMovie()
        {
            if (_commanderDeploymentOrderMovie != null)
                return true;

            if (_gauntletLayer == null || _commanderDeploymentOrderVm == null)
                return false;

            try
            {
                _commanderDeploymentOrderMovie = _gauntletLayer.LoadMovie(
                    CommanderDeploymentOrderMovieName,
                    _commanderDeploymentOrderVm);
                ModLogger.Info("CoopMissionSelectionView: loaded safe commander OrderRadial bridge.");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: failed to load safe commander OrderRadial bridge: " +
                    ex.GetType().Name + ":" + ex.Message);
                _commanderDeploymentOrderMovie = null;
                return false;
            }
        }

        private void ReleaseCommanderDeploymentOrderMovie()
        {
            if (_commanderDeploymentOrderMovie == null)
                return;

            try
            {
                _gauntletLayer?.ReleaseMovie(_commanderDeploymentOrderMovie);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: safe commander OrderRadial movie release failed: " + ex.Message);
            }
            finally
            {
                _commanderDeploymentOrderMovie = null;
            }
        }

        private bool TryEnsureCommanderSiegeMachineDeploymentMovie(CoopSelectionUiSnapshot snapshot)
        {
            if (_commanderSiegeMachineDeploymentMovie != null)
                return true;

            BattleScenarioContextMessage activeScenarioContext =
                snapshot?.BattleState?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetScenarioContext() ??
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            if (!ExactCampaignSiegeAssaultWithDeploymentRuntime.IsExactSiegeWithDeploymentScenario(activeScenarioContext))
                return false;

            Mission mission = Mission;
            Camera missionCamera = ResolveMissionScreenCombatCamera();
            BattleSideEnum side = snapshot?.EffectiveSide ?? mission?.PlayerTeam?.Side ?? BattleSideEnum.None;
            if (_gauntletLayer == null || mission == null || missionCamera == null || side == BattleSideEnum.None)
                return false;

            try
            {
                ExactCampaignSiegeAssaultWithDeploymentRuntime.TryEnsureCommanderDeploymentUiContract(
                    mission,
                    activeScenarioContext,
                    side,
                    out string _);

                _commanderSiegeMachineDeploymentVm = new CoopSiegeMachineDeploymentVM(mission, side, missionCamera);
                if (!_commanderSiegeMachineDeploymentVm.HasDeploymentTargets)
                {
                    _commanderSiegeMachineDeploymentVm.OnFinalize();
                    _commanderSiegeMachineDeploymentVm = null;
                    if (!_commanderSiegeMachineDeploymentNoTargetsLogged)
                    {
                        _commanderSiegeMachineDeploymentNoTargetsLogged = true;
                        ModLogger.Info(
                            "CoopMissionSelectionView: commander siege machine deployment overlay deferred; no deployment targets are materialized yet.");
                    }
                    return false;
                }

                _commanderSiegeMachineDeploymentMovie = _gauntletLayer.LoadMovie(
                    CommanderSiegeMachineDeploymentMovieName,
                    _commanderSiegeMachineDeploymentVm);
                _commanderSiegeMachineDeploymentRetryTimer = 0f;
                _commanderSiegeMachineDeploymentNoTargetsLogged = false;
                _commanderSiegeMachineDeploymentFailureLogged = false;
                ModLogger.Info("CoopMissionSelectionView: loaded coop commander siege machine deployment overlay.");
                return true;
            }
            catch (Exception ex)
            {
                if (!_commanderSiegeMachineDeploymentFailureLogged)
                {
                    _commanderSiegeMachineDeploymentFailureLogged = true;
                    ModLogger.Info(
                        "CoopMissionSelectionView: failed to load coop commander siege machine deployment overlay: " +
                        ex.GetType().Name + ":" + ex.Message);
                }
                ReleaseCommanderSiegeMachineDeploymentMovie();
                return false;
            }
        }

        private void ReleaseCommanderSiegeMachineDeploymentMovie()
        {
            if (_commanderSiegeMachineDeploymentMovie != null)
            {
                try
                {
                    _gauntletLayer?.ReleaseMovie(_commanderSiegeMachineDeploymentMovie);
                }
                catch (Exception ex)
                {
                    ModLogger.Info(
                        "CoopMissionSelectionView: coop commander siege machine deployment movie release failed: " +
                        ex.Message);
                }
                finally
                {
                    _commanderSiegeMachineDeploymentMovie = null;
                }
            }

            if (_commanderSiegeMachineDeploymentVm != null)
            {
                try
                {
                    _commanderSiegeMachineDeploymentVm.OnFinalize();
                }
                catch (Exception ex)
                {
                    ModLogger.Info(
                        "CoopMissionSelectionView: coop commander siege machine deployment VM finalize failed: " +
                        ex.Message);
                }
                finally
                {
                    _commanderSiegeMachineDeploymentVm = null;
                }
            }
        }

        private MissionOrderCallbacks CreateCommanderDeploymentOrderCallbacks()
        {
            return new MissionOrderCallbacks
            {
                RefreshVisuals = RefreshCommanderDeploymentOrderVisuals,
                OnActivateToggleOrder = ActivateCommanderDeploymentToggleOrder,
                OnDeactivateToggleOrder = DeactivateCommanderDeploymentToggleOrder,
                OnTransferTroopsFinished = OnCommanderDeploymentTransferTroopsFinished,
                OnBeforeOrder = OnBeforeCommanderDeploymentOrder,
                ToggleMissionInputs = ToggleCommanderDeploymentMissionInputs,
                SetSuspendTroopPlacer = SetCommanderDeploymentTroopPlacerSuspended,
                GetVisualOrderExecutionParameters = GetCommanderDeploymentVisualOrderExecutionParameters
            };
        }

        private static List<DeploymentPoint> BuildCommanderDeploymentPointList(Mission mission, Team deploymentTeam)
        {
            if (mission?.ActiveMissionObjects == null)
                return new List<DeploymentPoint>();

            BattleSideEnum side = deploymentTeam?.Side ?? BattleSideEnum.None;
            try
            {
                return mission.ActiveMissionObjects
                    .FindAllWithType<DeploymentPoint>()
                    .Where(deploymentPoint =>
                        deploymentPoint != null &&
                        !deploymentPoint.IsDisabled &&
                        (side == BattleSideEnum.None || deploymentPoint.Side == side))
                    .ToList();
            }
            catch
            {
                return new List<DeploymentPoint>();
            }
        }

        private void TryRegisterMissionOrderHotKeys(MissionOrderVM orderVm)
        {
            if (orderVm == null)
                return;

            try
            {
                GameKeyContext missionOrderCategory = HotKeyManager.GetCategory("MissionOrderHotkeyCategory");
                GameKeyContext genericPanelCategory = HotKeyManager.GetCategory("GenericPanelGameKeyCategory");
                object sceneInput = TryGetInstancePropertyValue(TryGetInstancePropertyValue(MissionScreen, "SceneLayer"), "Input");
                object gauntletInput = TryGetInstancePropertyValue(_gauntletLayer, "Input");
                if (missionOrderCategory != null)
                    TryRegisterHotKeyCategoryOnInputContext(sceneInput, missionOrderCategory);
                if (genericPanelCategory != null)
                    TryRegisterHotKeyCategoryOnInputContext(gauntletInput, genericPanelCategory);

                if (genericPanelCategory != null)
                {
                    orderVm.SetCancelInputKey(genericPanelCategory.GetHotKey("ToggleEscapeMenu"));
                    TryInvokeInstanceMethodSuccessfully(orderVm.TroopController, "SetDoneInputKey", genericPanelCategory.GetHotKey("Confirm"));
                    TryInvokeInstanceMethodSuccessfully(orderVm.TroopController, "SetCancelInputKey", genericPanelCategory.GetHotKey("Exit"));
                    TryInvokeInstanceMethodSuccessfully(orderVm.TroopController, "SetResetInputKey", genericPanelCategory.GetHotKey("Reset"));
                }

                if (missionOrderCategory != null)
                {
                    for (int orderIndex = 0; orderIndex < 9; orderIndex++)
                        orderVm.SetOrderIndexKey(orderIndex, missionOrderCategory.GetGameKey(69 + orderIndex));

                    orderVm.SetReturnKey(missionOrderCategory.GetGameKey(77));
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: failed to register MissionOrderVM bridge hotkeys: " + ex.Message);
            }
        }

        private void ReleaseCommanderDeploymentOrderBridge()
        {
            ReleaseCommanderDeploymentOrderMovie();

            if (_commanderDeploymentOrderVm != null)
            {
                try
                {
                    _commanderDeploymentOrderVm.OnFinalize();
                }
                catch (Exception ex)
                {
                    ModLogger.Info("CoopMissionSelectionView: safe commander MissionOrderVM bridge finalize failed: " + ex.Message);
                }
            }

            _commanderDeploymentOrderVm = null;
            _commanderDeploymentOrderVmInitialized = false;
            if (_commanderBattleOrderVm == null && !_commanderBattleOrderActive)
                ReleaseCommanderDeploymentVisualOrderProvider();
        }

        private void TryTickCommanderBattleOrderBridge()
        {
            if (!TryResolveCommanderBattleOrderContext(
                    out Mission mission,
                    out Team team,
                    out Agent mainAgent,
                    out string controlledEntryId,
                    out string commanderEntryId,
                    out string unavailableReason))
            {
                if (_commanderBattleOrderVm != null)
                    ReleaseCommanderBattleOrderBridge(unavailableReason ?? "context-lost");

                return;
            }

            if (!TryEnsureCommanderBattleOrderBridge(mission, team, mainAgent, controlledEntryId, commanderEntryId))
                return;

            TryUpdateCommanderBattleOrderVmUnchecked();
            TryTickCommanderBattleOrderHotkeys();
        }

        private bool TryResolveCommanderBattleOrderContext(
            out Mission mission,
            out Team team,
            out Agent mainAgent,
            out string controlledEntryId,
            out string commanderEntryId,
            out string unavailableReason)
        {
            mission = null;
            team = null;
            mainAgent = null;
            controlledEntryId = null;
            commanderEntryId = null;
            unavailableReason = null;

            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive)
            {
                unavailableReason = "network-inactive";
                return false;
            }

            if (_currentScreen != CoopSelectionScreen.None)
            {
                unavailableReason = "selection-screen-active";
                return false;
            }

            mission = Mission;
            if (mission == null ||
                !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName) ||
                !IsCurrentCommanderDeploymentScenario(mission))
            {
                unavailableReason = "not-exact-siege";
                return false;
            }

            NetworkCommunicator myPeer = GameNetwork.MyPeer;
            if (myPeer == null || myPeer.IsServerPeer)
            {
                unavailableReason = "peer-unavailable";
                return false;
            }

            MissionPeer missionPeer = myPeer.GetComponent<MissionPeer>();
            Agent controlledAgent = missionPeer?.ControlledAgent;
            mainAgent = Agent.Main;
            if (mainAgent == null || !mainAgent.IsActive())
            {
                unavailableReason = "main-agent-missing";
                return false;
            }

            if (controlledAgent != null &&
                controlledAgent.IsActive() &&
                !IsSameAgent(controlledAgent, mainAgent))
            {
                unavailableReason = "controlled-agent-mismatch";
                return false;
            }

            team = missionPeer?.Team ?? mainAgent.Team ?? mission.PlayerTeam;
            if (team == null || team.Side == BattleSideEnum.None)
            {
                unavailableReason = "team-unavailable";
                return false;
            }

            OrderController orderController = ResolveLocalBattleOrderController(team, mainAgent);
            if (orderController == null)
            {
                unavailableReason = "order-controller-missing";
                return false;
            }

            controlledEntryId = ResolveCommanderBattleControlledEntryId(missionPeer, mainAgent);
            bool isExactCommanderEntry = IsCommanderBattleEntryForTeam(team, controlledEntryId, out commanderEntryId);
            bool hasEstablishedCommanderControl =
                team.IsPlayerGeneral &&
                IsSameAgent(team.GeneralAgent, mainAgent) &&
                IsSameAgent(orderController.Owner, mainAgent);
            Agent localMainAgent = mainAgent;
            bool hasDelegatedFormationControl =
                CoopMissionNetworkBridge.TryResolveAuthorizedFormationIndices(
                    mission,
                    team,
                    controlledEntryId,
                    out List<int> authorizedFormationIndices,
                    out string authorityRole) &&
                authorizedFormationIndices.Count > 0 &&
                string.Equals(authorityRole, "delegated-captain", StringComparison.Ordinal) &&
                IsSameAgent(orderController.Owner, mainAgent) &&
                team.FormationsIncludingEmpty.Any(formation =>
                    formation != null &&
                    authorizedFormationIndices.Contains(formation.Index) &&
                    IsSameAgent(formation.PlayerOwner, localMainAgent));
            if (!isExactCommanderEntry && !hasDelegatedFormationControl)
            {
                unavailableReason = "not-local-commander-or-delegated-captain";
                return false;
            }

            if (isExactCommanderEntry && !hasEstablishedCommanderControl)
            {
                unavailableReason = "commander-control-pending";
                return false;
            }

            if (!ReferenceEquals(mission.PlayerTeam, team))
                mission.PlayerTeam = team;

            return true;
        }

        private static OrderController ResolveLocalBattleOrderController(Team team, Agent mainAgent)
        {
            if (team == null || mainAgent == null)
                return null;

            if (IsSameAgent(team.PlayerOrderController?.Owner, mainAgent))
                return team.PlayerOrderController;

            return team.GetOrderControllerOf(mainAgent);
        }

        private static string ResolveCommanderBattleControlledEntryId(MissionPeer missionPeer, Agent mainAgent)
        {
            if (mainAgent != null &&
                CoopMissionSpawnLogic.TryResolveAuthoritativeTrackedEntryId(mainAgent, out string entryId) &&
                !string.IsNullOrWhiteSpace(entryId))
            {
                return entryId.Trim();
            }

            if (missionPeer != null &&
                CoopBattleSpawnRuntimeState.TryGetState(missionPeer, out PeerSpawnRuntimeState spawnState) &&
                !string.IsNullOrWhiteSpace(spawnState.EntryId))
            {
                return spawnState.EntryId.Trim();
            }

            if (CoopMissionSpawnLogic.TryResolveLocalSelectedEntryIdForBattleMapCommander(out entryId) &&
                !string.IsNullOrWhiteSpace(entryId))
            {
                return entryId.Trim();
            }

            CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot selectionBridge =
                CoopBattleSelectionBridgeFile.ReadCurrentSelection();
            if (LooksLikeCommanderBattleEntryId(selectionBridge?.TroopOrEntryId))
                return selectionBridge.TroopOrEntryId.Trim();

            return null;
        }

        private static bool IsCommanderBattleEntryForTeam(Team team, string entryId, out string commanderEntryId)
        {
            commanderEntryId = null;
            if (team == null || team.Side == BattleSideEnum.None)
                return false;

            BattleRuntimeState runtimeState = BattleSnapshotRuntimeState.GetState();
            RosterEntryState commanderEntry = BattleCommanderResolver.ResolveCommanderEntry(runtimeState, team.Side);
            if (commanderEntry == null || string.IsNullOrWhiteSpace(commanderEntry.EntryId))
                return false;

            commanderEntryId = commanderEntry.EntryId;
            return !string.IsNullOrWhiteSpace(entryId) &&
                   string.Equals(commanderEntry.EntryId, entryId.Trim(), StringComparison.Ordinal);
        }

        private static bool LooksLikeCommanderBattleEntryId(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf('|') >= 0;
        }

        private static bool IsSameAgent(Agent left, Agent right)
        {
            return left != null &&
                   right != null &&
                   (ReferenceEquals(left, right) || left.Index == right.Index);
        }

        private bool TryEnsureCommanderBattleOrderBridge(
            Mission mission,
            Team team,
            Agent mainAgent,
            string controlledEntryId,
            string commanderEntryId)
        {
            if (IsCurrentExactLandBattleCommanderDeploymentScenario(mission))
            {
                return TryEnsureExactLandBattleCommanderBattleOrderProvider(
                    mission,
                    team,
                    mainAgent,
                    controlledEntryId,
                    commanderEntryId);
            }

            if (_commanderBattleOrderVm != null && _commanderBattleOrderVmInitialized)
                return true;

            if (_gauntletLayer == null || mission == null || team == null || mainAgent == null)
                return false;

            OrderController orderController = ResolveLocalBattleOrderController(team, mainAgent);
            if (orderController == null)
                return false;

            ReleaseCommanderBattleOrderBridge("recreate");

            try
            {
                EnsureCommanderBattleOrderSpriteCategoryLoaded();
                TrySetLayerActiveState(_gauntletLayer, true);
                _gauntletLayer.IsFocusLayer = false;
                TryLoseScreenManagerFocus(_gauntletLayer);
                TryInvokeLayerFocusCallback(_gauntletLayer, "HandleLoseFocus");
                _commanderBattleOrderActive = true;
                TryEnsureCommanderDeploymentVisualOrderProviderRegistered();

                _commanderBattleOrderVm = new MissionOrderVM(orderController, isDeployment: false, isMultiplayer: false);
                _commanderBattleOrderVm.IsDeployment = false;
                _commanderBattleOrderVm.SetCallbacks(CreateCommanderBattleOrderCallbacks());
                _commanderBattleOrderVm.InputRestrictions = _gauntletLayer.InputRestrictions;
                Camera missionCamera = ResolveMissionScreenCombatCamera();
                if (missionCamera != null)
                    _commanderBattleOrderVm.SetDeploymentParemeters(missionCamera, new List<DeploymentPoint>());

                TryAttachCommanderBattleFormationTargetHandler(mission);
                TryApplyCommanderBattleFocusedFormationsToVm();
                TryRegisterMissionOrderHotKeys(_commanderBattleOrderVm);
                _commanderBattleOrderVm.AfterInitialize();
                _commanderBattleOrderVm.UpdateCanUseShortcuts(true);
                TryInvokeInstanceMethodSuccessfully(_commanderBattleOrderVm.TroopController, "UpdateTroops");
                _commanderBattleOrderVmInitialized = true;
                BattleMapSpawnHandoffPatch.RegisterActiveExactCommanderMissionOrderVm(
                    _commanderBattleOrderVm,
                    "commander-battle-order-active");
                TryEnsureCommanderBattleOrderMovie();

                string logKey =
                    team.TeamIndex + "|" +
                    mainAgent.Index + "|" +
                    (controlledEntryId ?? "null") + "|" +
                    (commanderEntryId ?? "null");
                if (!string.Equals(_lastCommanderBattleOrderBridgeContextKey, logKey, StringComparison.Ordinal))
                {
                    _lastCommanderBattleOrderBridgeContextKey = logKey;
                    ModLogger.Info(
                        "CoopMissionSelectionView: prepared safe commander battle-time MissionOrderVM bridge. " +
                        "TeamIndex=" + team.TeamIndex +
                        " Side=" + team.Side +
                        " AgentMainIndex=" + mainAgent.Index +
                        " OrderOwnerIndex=" + (orderController.Owner?.Index.ToString() ?? "null") +
                        " ControlledEntryId=" + (controlledEntryId ?? "null") +
                        " CommanderEntryId=" + (commanderEntryId ?? "null") +
                        " Mission=" + (mission.SceneName ?? "null"));
                }

                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: failed to prepare commander battle-time MissionOrderVM bridge: " +
                    ex.GetType().Name + ":" + ex.Message);
                ReleaseCommanderBattleOrderBridge("create-failed");
                return false;
            }
        }

        private bool TryEnsureExactLandBattleCommanderBattleOrderProvider(
            Mission mission,
            Team team,
            Agent mainAgent,
            string controlledEntryId,
            string commanderEntryId)
        {
            if (_commanderBattleOrderActive &&
                _commanderBattleOrderVm == null &&
                _commanderDeploymentVisualOrderProviderRegistered)
            {
                return true;
            }

            ReleaseCommanderBattleOrderBridge("formation-only-native-recreate");

            _commanderBattleOrderActive = true;
            TryEnsureCommanderDeploymentVisualOrderProviderRegistered();
            if (!_commanderDeploymentVisualOrderProviderRegistered)
            {
                _commanderBattleOrderActive = false;
                return false;
            }

            string logKey =
                "formation-only-native|" +
                (team?.TeamIndex.ToString() ?? "null") + "|" +
                (mainAgent?.Index.ToString() ?? "null") + "|" +
                (controlledEntryId ?? "null") + "|" +
                (commanderEntryId ?? "null");
            if (!string.Equals(_lastCommanderBattleOrderBridgeContextKey, logKey, StringComparison.Ordinal))
            {
                _lastCommanderBattleOrderBridgeContextKey = logKey;
                ModLogger.Info(
                    "CoopMissionSelectionView: prepared formation-only native commander battle order provider. " +
                    "TeamIndex=" + (team?.TeamIndex.ToString() ?? "null") +
                    " Side=" + (team?.Side.ToString() ?? "null") +
                    " AgentMainIndex=" + (mainAgent?.Index.ToString() ?? "null") +
                    " ControlledEntryId=" + (controlledEntryId ?? "null") +
                    " CommanderEntryId=" + (commanderEntryId ?? "null") +
                    " Mission=" + (mission?.SceneName ?? "null"));
            }

            return true;
        }

        private bool TryEnsureCommanderBattleOrderMovie()
        {
            if (_commanderBattleOrderMovie != null)
                return true;

            if (_gauntletLayer == null || _commanderBattleOrderVm == null)
                return false;

            try
            {
                _commanderBattleOrderMovie = _gauntletLayer.LoadMovie(
                    CommanderDeploymentOrderMovieName,
                    _commanderBattleOrderVm);
                ModLogger.Info("CoopMissionSelectionView: loaded safe commander battle-time OrderRadial bridge.");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: failed to load commander battle-time OrderRadial bridge: " +
                    ex.GetType().Name + ":" + ex.Message);
                _commanderBattleOrderMovie = null;
                return false;
            }
        }

        private void ReleaseCommanderBattleOrderBridge(string source)
        {
            MissionOrderVM releasedVm = _commanderBattleOrderVm;
            SyncCommanderBattleMissionOrderMenuState(false, "release-" + (source ?? "unknown"));
            BattleMapSpawnHandoffPatch.ClearActiveExactCommanderMissionOrderVm(
                releasedVm,
                "commander-battle-order-bridge-release:" + (source ?? "unknown"));
            ReleaseCommanderBattleFormationTargetHandler();
            ReleaseCommanderBattleOrderMovie();
            TryDeactivateNativeCommanderBattlePlacement(MissionScreen);

            if (releasedVm != null)
            {
                try
                {
                    releasedVm.OnFinalize();
                }
                catch (Exception ex)
                {
                    ModLogger.Info(
                        "CoopMissionSelectionView: commander battle-time MissionOrderVM bridge finalize failed. " +
                        "Source=" + (source ?? "unknown") +
                        " Error=" + ex.GetType().Name + ":" + ex.Message);
                }
            }

            _commanderBattleOrderVm = null;
            _commanderBattleOrderVmInitialized = false;
            _commanderBattleOrderActive = false;
            ReleaseCommanderBattleOrderSpriteCategory();
            if (_commanderDeploymentOrderVm == null && _commanderDeploymentViewModel == null)
            {
                ReleaseCommanderDeploymentVisualOrderProvider();
                ReleaseCommanderDeploymentSpriteCategory();
            }
        }

        private void ReleaseCommanderBattleOrderMovie()
        {
            if (_commanderBattleOrderMovie == null)
                return;

            try
            {
                _gauntletLayer?.ReleaseMovie(_commanderBattleOrderMovie);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: commander battle-time OrderRadial movie release failed: " + ex.Message);
            }
            finally
            {
                _commanderBattleOrderMovie = null;
            }
        }

        private void TryAttachCommanderBattleFormationTargetHandler(Mission mission)
        {
            if (_commanderBattleFormationTargetHandler != null || mission == null)
                return;

            try
            {
                MissionFormationTargetSelectionHandler handler =
                    mission.GetMissionBehavior<MissionFormationTargetSelectionHandler>();
                if (handler == null)
                    return;

                handler.OnFormationFocused += OnCommanderBattleFormationFocused;
                _commanderBattleFormationTargetHandler = handler;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: commander battle formation target handler attach failed: " +
                    ex.GetType().Name + ":" + ex.Message);
            }
        }

        private void ReleaseCommanderBattleFormationTargetHandler()
        {
            if (_commanderBattleFormationTargetHandler == null)
            {
                _commanderBattleFocusedFormationsCache = null;
                return;
            }

            try
            {
                _commanderBattleFormationTargetHandler.OnFormationFocused -= OnCommanderBattleFormationFocused;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: commander battle formation target handler release failed: " +
                    ex.GetType().Name + ":" + ex.Message);
            }
            finally
            {
                _commanderBattleFormationTargetHandler = null;
                _commanderBattleFocusedFormationsCache = null;
            }
        }

        private void OnCommanderBattleFormationFocused(MBReadOnlyList<Formation> focusedFormations)
        {
            _commanderBattleFocusedFormationsCache = focusedFormations;
            TryApplyCommanderBattleFocusedFormationsToVm();
        }

        private void TryApplyCommanderBattleFocusedFormationsToVm()
        {
            if (_commanderBattleOrderVm == null || _commanderBattleFocusedFormationsCache == null)
                return;

            try
            {
                _commanderBattleOrderVm.SetFocusedFormations(_commanderBattleFocusedFormationsCache);
            }
            catch
            {
            }
        }

        private Formation ResolveCommanderBattleFocusedFormation()
        {
            if (_commanderBattleFocusedFormationsCache == null)
                return null;

            for (int i = 0; i < _commanderBattleFocusedFormationsCache.Count; i++)
            {
                Formation formation = _commanderBattleFocusedFormationsCache[i];
                if (formation != null && formation.CountOfUnits > 0)
                    return formation;
            }

            return null;
        }

        private void SyncCommanderBattleMissionOrderMenuState(bool isOpen, string source)
        {
            Mission mission = Mission;
            if (mission == null)
                return;

            if (!isOpen &&
                _commanderDeploymentOrderVm != null &&
                TryGetInstanceBool(_commanderDeploymentOrderVm, "IsToggleOrderShown"))
            {
                return;
            }

            mission.IsOrderMenuOpen = isOpen;
        }

        private void LogCommanderBattleOrderVisualAudit(string source)
        {
            Mission mission = Mission;
            OrderController orderController = mission?.PlayerTeam?.PlayerOrderController;
            object orderTroopPlacer = ResolveNativeCommanderOrderTroopPlacer();
            object orderFlag =
                TryGetInstancePropertyValue(orderTroopPlacer, "OrderFlag") ??
                TryGetInstancePropertyValue(MissionScreen, "OrderFlag");
            object troopList = TryGetInstanceMemberValue(_commanderBattleOrderVm?.TroopController, "TroopList");
            int focusedFormationCount = _commanderBattleFocusedFormationsCache?.Count ?? 0;
            int troopListCount = TryGetCollectionCount(troopList);
            int selectableTroopCount = CountCommanderBattleTroopItemsWithBool(troopList, "IsSelectable");
            int selectedTroopCount = CountCommanderBattleTroopItemsWithBool(troopList, "IsSelected");
            int showSelectionInputsCount = CountCommanderBattleTroopItemsWithBool(troopList, "ShowSelectionInputs");
            bool hasFormationTargetHandler = _commanderBattleFormationTargetHandler != null ||
                                             mission?.GetMissionBehavior<MissionFormationTargetSelectionHandler>() != null;
            bool hasFormationMarker = HasMissionBehaviorNamed(
                mission,
                "MissionFormationMarkerUIHandler",
                "MissionGauntletFormationMarker");
            string key =
                (source ?? "unknown") + "|" +
                (mission?.SceneName ?? "null") + "|" +
                (mission?.IsOrderMenuOpen.ToString() ?? "null") + "|" +
                hasFormationTargetHandler + "|" +
                hasFormationMarker + "|" +
                (orderTroopPlacer != null) + "|" +
                (orderFlag != null) + "|" +
                focusedFormationCount + "|" +
                troopListCount + "|" +
                selectableTroopCount + "|" +
                selectedTroopCount + "|" +
                showSelectionInputsCount + "|" +
                (_commanderBattleOrderSpriteCategory != null) + "|" +
                (orderController?.SelectedFormations?.Count.ToString() ?? "null");
            if (string.Equals(_lastCommanderBattleOrderVisualAuditKey, key, StringComparison.Ordinal))
                return;

            _lastCommanderBattleOrderVisualAuditKey = key;
            ModLogger.Info(
                "CoopMissionSelectionView: commander battle-time order visual audit. " +
                "Source=" + (source ?? "unknown") +
                " IsOrderMenuOpen=" + (mission?.IsOrderMenuOpen.ToString() ?? "null") +
                " HasFormationTargetHandler=" + hasFormationTargetHandler +
                " HasFormationMarker=" + hasFormationMarker +
                " HasOrderTroopPlacer=" + (orderTroopPlacer != null) +
                " HasOrderFlag=" + (orderFlag != null) +
                " FocusedFormationCount=" + focusedFormationCount +
                " TroopListCount=" + troopListCount +
                " SelectableTroopCount=" + selectableTroopCount +
                " SelectedTroopCount=" + selectedTroopCount +
                " ShowSelectionInputsCount=" + showSelectionInputsCount +
                " HasOrderSpriteCategory=" + (_commanderBattleOrderSpriteCategory != null) +
                " SelectedFormationCount=" + (orderController?.SelectedFormations?.Count.ToString() ?? "null") +
                " Mission=" + (mission?.SceneName ?? "null"));
        }

        private static int CountCommanderBattleTroopItemsWithBool(object troopList, string propertyName)
        {
            if (troopList == null || string.IsNullOrWhiteSpace(propertyName))
                return 0;

            int count = 0;
            if (troopList is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    if (TryGetInstanceBool(item, propertyName))
                        count++;
                }
            }

            return count;
        }

        private static bool HasMissionBehaviorNamed(Mission mission, params string[] typeNames)
        {
            if (mission?.MissionBehaviors == null || typeNames == null || typeNames.Length == 0)
                return false;

            for (int i = 0; i < mission.MissionBehaviors.Count; i++)
            {
                Type behaviorType = mission.MissionBehaviors[i]?.GetType();
                if (behaviorType == null)
                    continue;

                for (int j = 0; j < typeNames.Length; j++)
                {
                    string expected = typeNames[j];
                    if (string.IsNullOrWhiteSpace(expected))
                        continue;

                    if (string.Equals(behaviorType.Name, expected, StringComparison.Ordinal) ||
                        string.Equals(behaviorType.FullName, expected, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryCloseCommanderBattleOrderMenu(string source)
        {
            if (_commanderBattleOrderVm == null ||
                !TryGetInstanceBool(_commanderBattleOrderVm, "IsToggleOrderShown"))
            {
                return false;
            }

            bool? closeResult = TryInvokeBoolMethod(_commanderBattleOrderVm, "TryCloseToggleOrder", false);
            TryDeactivateNativeCommanderBattlePlacement(MissionScreen);
            SyncCommanderBattleMissionOrderMenuState(
                TryGetInstanceBool(_commanderBattleOrderVm, "IsToggleOrderShown"),
                "close-" + (source ?? "unknown"));
            if (closeResult != true)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: commander battle-time order close fallback. " +
                    "Source=" + (source ?? "unknown") +
                    " CloseResult=" + (closeResult.HasValue ? closeResult.Value.ToString() : "null"));
            }

            return true;
        }

        private bool HasCommanderBattleVisualOrderSets()
        {
            return GetCommanderBattleOrderSetCount() > 0;
        }

        private int GetCommanderBattleOrderSetCount()
        {
            return TryGetCollectionCount(TryGetInstanceMemberValue(_commanderBattleOrderVm, "OrderSets"));
        }

        private void LogCommanderBattleEmptyOrderSetGuard(string source, Mission mission, OrderController orderController)
        {
            int selectedFormationCount = orderController?.SelectedFormations?.Count ?? -1;
            int troopListCount = TryGetCollectionCount(TryGetInstanceMemberValue(_commanderBattleOrderVm?.TroopController, "TroopList"));
            string sceneName = mission?.SceneName ?? "null";
            string logKey =
                (source ?? "unknown") + "|" +
                sceneName + "|" +
                selectedFormationCount + "|" +
                troopListCount;

            if (string.Equals(_lastCommanderBattleOrderEmptySetGuardKey, logKey, StringComparison.Ordinal))
                return;

            _lastCommanderBattleOrderEmptySetGuardKey = logKey;
            ModLogger.Info(
                "CoopMissionSelectionView: suppressed commander battle-time order menu because no visual order sets were available. " +
                "Source=" + (source ?? "unknown") +
                " Scene=" + sceneName +
                " SelectedFormationCount=" + selectedFormationCount +
                " TroopListCount=" + troopListCount +
                " IsToggleOrderShown=" + TryGetInstanceBool(_commanderBattleOrderVm, "IsToggleOrderShown") +
                " IsTroopListShown=" + TryGetInstanceBool(_commanderBattleOrderVm, "IsTroopListShown"));
        }

        private MissionOrderCallbacks CreateCommanderBattleOrderCallbacks()
        {
            return new MissionOrderCallbacks
            {
                RefreshVisuals = RefreshCommanderBattleOrderVisuals,
                OnActivateToggleOrder = ActivateCommanderBattleToggleOrder,
                OnDeactivateToggleOrder = DeactivateCommanderBattleToggleOrder,
                OnTransferTroopsFinished = OnCommanderBattleTransferTroopsFinished,
                OnBeforeOrder = OnBeforeCommanderBattleOrder,
                ToggleMissionInputs = ToggleCommanderBattleMissionInputs,
                SetSuspendTroopPlacer = SetCommanderBattleTroopPlacerSuspended,
                GetVisualOrderExecutionParameters = GetCommanderBattleVisualOrderExecutionParameters
            };
        }

        private void RefreshCommanderBattleOrderVisuals()
        {
            TryInvokeInstanceMethodSuccessfully(_commanderBattleOrderVm?.TroopController, "UpdateTroops");
        }

        private void ActivateCommanderBattleToggleOrder()
        {
            if (HasCommanderBattleVisualOrderSets())
                TryActivateNativeCommanderBattlePlacement(MissionScreen);
        }

        private void DeactivateCommanderBattleToggleOrder()
        {
            TryDeactivateNativeCommanderBattlePlacement(MissionScreen);
        }

        private void OnCommanderBattleTransferTroopsFinished()
        {
            TryInvokeInstanceMethodSuccessfully(_commanderBattleOrderVm?.TroopController, "UpdateTroops");
        }

        private void OnBeforeCommanderBattleOrder()
        {
            if (HasCommanderBattleVisualOrderSets())
                TryActivateNativeCommanderBattlePlacement(MissionScreen);
        }

        private void ToggleCommanderBattleMissionInputs(bool isLocked)
        {
            ScreenBase missionScreen = MissionScreen;
            if (missionScreen == null)
                return;

            TryInvokeInstanceMethod(missionScreen, "SetCameraLockState", isLocked);
            TrySetInstanceProperty(missionScreen, "LockCameraMovement", isLocked);
        }

        private void SetCommanderBattleTroopPlacerSuspended(bool isSuspended)
        {
            if (isSuspended)
                TryDeactivateNativeCommanderBattlePlacement(MissionScreen);
            else if (HasCommanderBattleVisualOrderSets())
                TryActivateNativeCommanderBattlePlacement(MissionScreen);
        }

        private VisualOrderExecutionParameters GetCommanderBattleVisualOrderExecutionParameters()
        {
            OrderController orderController = Mission?.PlayerTeam?.PlayerOrderController;
            Agent agent = Agent.Main ?? orderController?.Owner;
            Formation formation = ResolveCommanderBattleFocusedFormation();
            Mission mission = Mission;
            WorldPosition? orderPosition = TryResolveCommanderBattleOrderPosition(
                mission,
                orderController?.Team ?? mission?.PlayerTeam,
                out WorldPosition resolvedOrderPosition)
                ? resolvedOrderPosition
                : (WorldPosition?)null;

            return new VisualOrderExecutionParameters(agent, formation, orderPosition);
        }

        private bool TryResolveCommanderBattleOrderPosition(
            Mission mission,
            Team team,
            out WorldPosition orderPosition)
        {
            orderPosition = WorldPosition.Invalid;
            if (mission?.Scene == null || MissionScreen == null || team == null)
                return false;

            try
            {
                Vec3 orderFlagPosition = MissionScreen.GetOrderFlagPosition();
                if (TryCreateValidCommanderBattleOrderPosition(
                        mission,
                        team,
                        orderFlagPosition,
                        out orderPosition))
                {
                    return true;
                }

                Vec2 screenPoint = MissionScreen.MouseVisible
                    ? TaleWorlds.InputSystem.Input.MousePositionRanged
                    : new Vec2(0.5f, 0.5f);
                MissionScreen.ScreenPointToWorldRay(
                    screenPoint,
                    out Vec3 rayBegin,
                    out Vec3 rayEnd);
                if (!mission.Scene.RayCastForClosestEntityOrTerrain(
                        rayBegin,
                        rayEnd,
                        out float collisionDistance,
                        out Vec3 fallbackPosition,
                        out WeakGameEntity collidedEntity,
                        0.3f,
                        (BodyFlags)67188481))
                {
                    return false;
                }

                return TryCreateValidCommanderBattleOrderPosition(
                    mission,
                    team,
                    fallbackPosition,
                    out orderPosition);
            }
            catch
            {
                orderPosition = WorldPosition.Invalid;
                return false;
            }
        }

        private static bool TryCreateValidCommanderBattleOrderPosition(
            Mission mission,
            Team team,
            Vec3 position,
            out WorldPosition orderPosition)
        {
            orderPosition = WorldPosition.Invalid;
            if (mission?.Scene == null ||
                team == null ||
                !IsFiniteCommanderBattleOrderPosition(position) ||
                position.z <= -99999f)
            {
                return false;
            }

            try
            {
                WorldPosition candidate = new WorldPosition(
                    mission.Scene,
                    UIntPtr.Zero,
                    position,
                    hasValidZ: false);
                if (!candidate.IsValid ||
                    !mission.IsFormationUnitPositionAvailable(ref candidate, team) ||
                    !mission.IsOrderPositionAvailable(candidate, team))
                {
                    return false;
                }

                Vec3 groundPosition = candidate.GetGroundVec3();
                if (!IsFiniteCommanderBattleOrderPosition(groundPosition))
                    return false;

                orderPosition = new WorldPosition(
                    mission.Scene,
                    UIntPtr.Zero,
                    groundPosition,
                    hasValidZ: true);
                return true;
            }
            catch
            {
                orderPosition = WorldPosition.Invalid;
                return false;
            }
        }

        private static bool IsFiniteCommanderBattleOrderPosition(Vec3 position)
        {
            return !float.IsNaN(position.x) && !float.IsInfinity(position.x) &&
                   !float.IsNaN(position.y) && !float.IsInfinity(position.y) &&
                   !float.IsNaN(position.z) && !float.IsInfinity(position.z);
        }

        private void TryUpdateCommanderBattleOrderVmUnchecked()
        {
            if (_commanderBattleOrderVm == null)
                return;

            try
            {
                BattleMapSpawnHandoffPatch.RegisterActiveExactCommanderMissionOrderVm(
                    _commanderBattleOrderVm,
                    "commander-battle-order-active");
                TryAttachCommanderBattleFormationTargetHandler(Mission);
                bool isToggleOrderShown = TryGetInstanceBool(_commanderBattleOrderVm, "IsToggleOrderShown");
                SyncCommanderBattleMissionOrderMenuState(isToggleOrderShown, "update");
                if (!isToggleOrderShown)
                {
                    _commanderBattleOrderVm.Update();
                    return;
                }

                TryApplyCommanderBattleFocusedFormationsToVm();
                TryUpdateCommanderBattleFormationSelectionInputs();
                TryInvokeInstanceMethodSuccessfully(_commanderBattleOrderVm.TroopController, "IntervalUpdate");
                TryInvokeInstanceMethodSuccessfully(_commanderBattleOrderVm.TroopController, "Update");
                TryInvokeInstanceMethodSuccessfully(_commanderBattleOrderVm.TroopController, "RefreshTroopFormationTargetVisuals");
                TrySetInstanceProperty(
                    _commanderBattleOrderVm,
                    "UseAlternativeFormationLayout",
                    TaleWorlds.InputSystem.Input.IsGamepadActive);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: commander battle-time order update failed: " +
                    ex.GetType().Name + ":" + ex.Message);
                ReleaseCommanderBattleOrderBridge("update-failed");
            }
        }

        private void TryTickCommanderBattleOrderHotkeys()
        {
            if (_commanderBattleOrderVm == null ||
                !_commanderBattleOrderVmInitialized)
            {
                return;
            }

            if (Input.IsKeyPressed(InputKey.BackSpace))
            {
                TrySetLayerActiveState(_gauntletLayer, true);
                TryEnsureCommanderBattleOrderMovie();
                if (TryGetInstanceBool(_commanderBattleOrderVm, "IsToggleOrderShown"))
                    _commanderBattleOrderVm.ViewOrders();
                else
                    TryOpenCommanderBattleOrderMenuUnchecked();
                return;
            }

            int formationHotkeyIndex = GetPressedCommanderBattleFormationHotkeyIndex();
            if (formationHotkeyIndex >= 0)
            {
                TryHandleCommanderBattleFormationHotkey(formationHotkeyIndex);
                return;
            }

            int orderHotkeyIndex = GetPressedCommanderDeploymentOrderHotkeyIndex();
            if (orderHotkeyIndex < 0)
                return;

            TryHandleCommanderBattleOrderHotkey(orderHotkeyIndex);
        }

        private bool TryHandleCommanderBattleFormationHotkey(int formationHotkeyIndex)
        {
            if (formationHotkeyIndex < 0 || _commanderBattleOrderVm == null)
                return false;

            TrySetLayerActiveState(_gauntletLayer, true);
            TryEnsureCommanderBattleOrderMovie();

            try
            {
                bool handled = TryInvokeInstanceMethodSuccessfully(
                    _commanderBattleOrderVm.TroopController,
                    "OnSelectFormationWithIndex",
                    formationHotkeyIndex);
                if (!handled)
                {
                    handled = TryInvokeInstanceMethodSuccessfully(
                        _commanderBattleOrderVm,
                        "OnTroopFormationSelected",
                        formationHotkeyIndex);
                }

                if (!handled)
                    return false;

                _commanderBattleOrderVm.UpdateCanUseShortcuts(true);
                TryInvokeInstanceMethodSuccessfully(_commanderBattleOrderVm, "SetActiveOrders");
                TryInvokeInstanceMethodSuccessfully(_commanderBattleOrderVm.TroopController, "UpdateTroops");
                TryInvokeInstanceMethodSuccessfully(_commanderBattleOrderVm.TroopController, "RefreshTroopFormationTargetVisuals");
                TryUpdateCommanderBattleFormationSelectionInputs();
                SyncCommanderBattleMissionOrderMenuState(
                    TryGetInstanceBool(_commanderBattleOrderVm, "IsToggleOrderShown"),
                    "formation-hotkey");
                LogCommanderBattleOrderVisualAudit("formation-hotkey");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: commander battle-time formation hotkey failed: " +
                    ex.GetType().Name + ":" + ex.Message);
                return false;
            }
        }

        private void TryUpdateCommanderBattleFormationSelectionInputs()
        {
            object troopList = TryGetInstanceMemberValue(_commanderBattleOrderVm?.TroopController, "TroopList");
            if (troopList == null)
                return;

            bool isGamepadActive = TaleWorlds.InputSystem.Input.IsGamepadActive;
            try
            {
                if (troopList is IEnumerable enumerable)
                {
                    foreach (object troopItem in enumerable)
                    {
                        if (troopItem == null)
                            continue;

                        TryInvokeInstanceMethodSuccessfully(troopItem, "UpdateSelectionKeyInfo");
                        bool isSelectable = TryGetInstanceBool(troopItem, "IsSelectable");
                        if (isGamepadActive)
                        {
                            TrySetInstanceProperty(
                                troopItem,
                                "ShowSelectionInputs",
                                TryGetInstanceBool(troopItem, "IsSelectionHighlightActive") && isSelectable);
                        }
                        else
                        {
                            TrySetInstanceProperty(troopItem, "IsSelectionHighlightActive", false);
                            TrySetInstanceProperty(troopItem, "ShowSelectionInputs", isSelectable);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: commander battle-time formation selection input sync failed: " +
                    ex.GetType().Name + ":" + ex.Message);
            }
        }

        private bool TryHandleCommanderBattleOrderHotkey(int orderHotkeyIndex)
        {
            if (orderHotkeyIndex < 0 || _commanderBattleOrderVm == null)
                return false;

            TrySetLayerActiveState(_gauntletLayer, true);
            TryEnsureCommanderBattleOrderMovie();

            try
            {
                object selectedOrderSet = TryGetInstanceMemberValue(_commanderBattleOrderVm, "SelectedOrderSet");
                if (selectedOrderSet != null)
                    return TryExecuteCommanderBattleSelectedOrderSetHotkey(selectedOrderSet, orderHotkeyIndex);

                bool openInvoked = TryOpenCommanderBattleOrderMenuUnchecked();
                if (!openInvoked)
                    return false;

                object orderSets = TryGetInstanceMemberValue(_commanderBattleOrderVm, "OrderSets");
                if (orderHotkeyIndex == 8 && OrderSetCollectionContainsReturnOnlySet(orderSets))
                {
                    bool? closeResult = TryInvokeBoolMethod(_commanderBattleOrderVm, "TryCloseToggleOrder", false);
                    return closeResult == true;
                }

                object orderSetAtIndex = TryInvokeInstanceMethodWithResult(
                    _commanderBattleOrderVm,
                    "GetOrderSetAtIndex",
                    orderHotkeyIndex);
                if (orderSetAtIndex == null || IsReturnOnlyOrderSet(orderSetAtIndex))
                    return false;

                return TrySelectCommanderBattleOrderSetUnchecked(orderSetAtIndex);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: commander battle-time order hotkey failed: " +
                    ex.GetType().Name + ":" + ex.Message);
                return false;
            }
        }

        private bool TryOpenCommanderBattleOrderMenuUnchecked()
        {
            if (_commanderBattleOrderVm == null)
                return false;

            if (TryGetInstanceBool(_commanderBattleOrderVm, "IsToggleOrderShown"))
                return true;

            Mission mission = Mission;
            OrderController orderController = mission?.PlayerTeam?.PlayerOrderController;
            if (mission == null || orderController == null)
                return false;

            try
            {
                TryAttachCommanderBattleFormationTargetHandler(mission);
                TrySetLayerActiveState(_gauntletLayer, true);
                if (orderController.SelectedFormations == null || orderController.SelectedFormations.Count == 0)
                    orderController.SelectAllFormations();

                _commanderBattleOrderVm.UpdateCanUseShortcuts(true);
                _commanderBattleOrderVm.OpenToggleOrder(fromHold: false, displayMessage: true);
                TryApplyCommanderBattleFocusedFormationsToVm();
                TryInvokeInstanceMethodSuccessfully(_commanderBattleOrderVm.TroopController, "UpdateTroops");

                bool isShown = TryGetInstanceBool(_commanderBattleOrderVm, "IsToggleOrderShown");
                SyncCommanderBattleMissionOrderMenuState(isShown, "open-toggle-order");
                if (!isShown || GetCommanderBattleOrderSetCount() <= 0)
                {
                    TryInvokeBoolMethod(_commanderBattleOrderVm, "TryCloseToggleOrder", false);
                    SyncCommanderBattleMissionOrderMenuState(false, "open-toggle-order-empty");
                    TryDeactivateNativeCommanderBattlePlacement(MissionScreen);
                    LogCommanderBattleEmptyOrderSetGuard("open-toggle-order", mission, orderController);
                    return false;
                }

                TryActivateNativeCommanderBattlePlacement(MissionScreen);
                LogCommanderBattleOrderVisualAudit("open-toggle-order");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: commander battle-time order open failed: " +
                    ex.GetType().Name + ":" + ex.Message);
                return false;
            }
        }

        private bool TrySelectCommanderBattleOrderSetUnchecked(object orderSet)
        {
            if (orderSet == null || _commanderBattleOrderVm == null)
                return false;

            try
            {
                bool? selectResult = TryInvokeBoolMethod(_commanderBattleOrderVm, "TrySelectOrderSet", orderSet);
                bool handled = selectResult == true;
                if (!handled)
                {
                    VisualOrderExecutionParameters executionParameters = GetCommanderBattleVisualOrderExecutionParameters();
                    handled = TryInvokeInstanceMethodSuccessfully(orderSet, "ExecuteAction", executionParameters);
                }

                if (!handled)
                    return false;

                _commanderBattleOrderVm.UpdateCanUseShortcuts(true);
                TryInvokeInstanceMethodSuccessfully(_commanderBattleOrderVm, "SetActiveOrders");
                TryInvokeInstanceMethodSuccessfully(_commanderBattleOrderVm.TroopController, "UpdateTroops");
                SyncCommanderBattleMissionOrderMenuState(
                    TryGetInstanceBool(_commanderBattleOrderVm, "IsToggleOrderShown"),
                    "select-order-set");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: commander battle-time order set select failed: " +
                    ex.GetType().Name + ":" + ex.Message);
                return false;
            }
        }

        private bool TryExecuteCommanderBattleSelectedOrderSetHotkey(object selectedOrderSet, int orderHotkeyIndex)
        {
            object orders = TryGetInstanceMemberValue(selectedOrderSet, "Orders");
            int selectedOrderCount = TryGetCollectionCount(orders);
            if (selectedOrderCount <= 0)
                return false;

            if (orderHotkeyIndex == 8 && OrderItemCollectionContainsReturnVisualOrder(orders))
                return TryInvokeInstanceMethodSuccessfully(selectedOrderSet, "ExecuteDeSelect");

            if (orderHotkeyIndex >= selectedOrderCount)
                return false;

            object orderItem = TryGetCollectionItem(orders, orderHotkeyIndex);
            object visualOrder = TryGetInstanceMemberValue(orderItem, "Order");
            if (IsReturnVisualOrderInstance(visualOrder))
                return TryInvokeInstanceMethodSuccessfully(selectedOrderSet, "ExecuteDeSelect");

            VisualOrderExecutionParameters executionParameters = GetCommanderBattleVisualOrderExecutionParameters();
            if (orderItem == null ||
                !TryInvokeInstanceMethodSuccessfully(orderItem, "ExecuteAction", executionParameters))
            {
                return false;
            }

            TryInvokeInstanceMethodSuccessfully(selectedOrderSet, "ExecuteDeSelect");
            if (!TryGetInstanceBool(_commanderBattleOrderVm, "IsHolding"))
                TryInvokeBoolMethod(_commanderBattleOrderVm, "TryCloseToggleOrder", false);

            TryInvokeInstanceMethodSuccessfully(_commanderBattleOrderVm.TroopController, "UpdateTroops");
            SyncCommanderBattleMissionOrderMenuState(
                TryGetInstanceBool(_commanderBattleOrderVm, "IsToggleOrderShown"),
                "execute-selected-order");
            return true;
        }

        private bool TryActivateNativeCommanderBattlePlacement(ScreenBase missionScreen)
        {
            object orderTroopPlacer = ResolveNativeCommanderOrderTroopPlacer();
            if (orderTroopPlacer == null)
                return false;

            try
            {
                TrySetInstanceProperty(orderTroopPlacer, "SuspendTroopPlacer", false);
                TryInvokeInstanceMethod(orderTroopPlacer, "RestrictOrdersToDeploymentBoundaries", false);
                object orderFlag = TryGetInstancePropertyValue(orderTroopPlacer, "OrderFlag");
                if (missionScreen != null && orderFlag != null)
                {
                    TrySetInstanceProperty(missionScreen, "OrderFlag", orderFlag);
                    TryInvokeInstanceMethod(missionScreen, "SetOrderFlagVisibility", true);
                }

                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: native commander battle placement bridge failed: " + ex.Message);
                return false;
            }
        }

        private void TryDeactivateNativeCommanderBattlePlacement(ScreenBase missionScreen)
        {
            object orderTroopPlacer = ResolveNativeCommanderOrderTroopPlacer();
            if (orderTroopPlacer == null)
                return;

            try
            {
                TryInvokeInstanceMethod(orderTroopPlacer, "RestrictOrdersToDeploymentBoundaries", false);
                TrySetInstanceProperty(orderTroopPlacer, "SuspendTroopPlacer", true);
                if (missionScreen != null)
                    TryInvokeInstanceMethod(missionScreen, "SetOrderFlagVisibility", false);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: native commander battle placement bridge release failed: " + ex.Message);
            }
        }

        private void RefreshCommanderDeploymentOrderVisuals()
        {
            TryRefreshNativeCommanderOrderOfBattleCounts(_commanderDeploymentViewModel, Mission, "mission-order-refresh-visuals");
        }

        private void ActivateCommanderDeploymentToggleOrder()
        {
            TryActivateNativeCommanderDeploymentPlacement(MissionScreen);
            TryEnsureCommanderDeploymentBoundaries("activate-toggle-order");
        }

        private void DeactivateCommanderDeploymentToggleOrder()
        {
            TryDeactivateNativeCommanderDeploymentPlacement(MissionScreen);
        }

        private void OnCommanderDeploymentTransferTroopsFinished()
        {
            TryInvokeInstanceMethodSuccessfully(_commanderDeploymentOrderVm?.TroopController, "UpdateTroops");
            TryRefreshNativeCommanderOrderOfBattleCounts(_commanderDeploymentViewModel, Mission, "mission-order-transfer-finished");
        }

        private void OnBeforeCommanderDeploymentOrder()
        {
            TryActivateNativeCommanderDeploymentPlacement(MissionScreen);
            TryEnsureCommanderDeploymentBoundaries("before-order");
        }

        private void ToggleCommanderDeploymentMissionInputs(bool isLocked)
        {
            ScreenBase missionScreen = MissionScreen;
            if (missionScreen == null)
                return;

            if (_currentScreen == CoopSelectionScreen.CommanderDeployment)
            {
                TryApplyCommanderDeploymentFreeCameraScreenState(missionScreen);
                return;
            }

            TryInvokeInstanceMethod(missionScreen, "SetCameraLockState", isLocked);
            TrySetInstanceProperty(missionScreen, "LockCameraMovement", isLocked);
        }

        private void SetCommanderDeploymentTroopPlacerSuspended(bool isSuspended)
        {
            if (isSuspended)
                TryDeactivateNativeCommanderDeploymentPlacement(MissionScreen);
            else
                TryActivateNativeCommanderDeploymentPlacement(MissionScreen);
        }

        private VisualOrderExecutionParameters GetCommanderDeploymentVisualOrderExecutionParameters()
        {
            OrderController orderController = Mission?.PlayerTeam?.PlayerOrderController;
            Agent agent = orderController?.Owner ?? Agent.Main;
            Formation formation = orderController?.SelectedFormations?.FirstOrDefault();
            return new VisualOrderExecutionParameters(agent, formation, null);
        }

        private static Team ResolveMissionTeamForSide(Mission mission, BattleSideEnum side)
        {
            if (mission == null)
                return null;

            try
            {
                if (side == BattleSideEnum.Attacker)
                    return mission.AttackerTeam ?? mission.Teams?.Attacker;

                if (side == BattleSideEnum.Defender)
                    return mission.DefenderTeam ?? mission.Teams?.Defender;
            }
            catch
            {
            }

            return null;
        }

        private static bool TryRefreshMissionPlayerTeamRelationView(
            Mission mission,
            Team playerTeam,
            string source,
            out string diagnostics)
        {
            diagnostics = "refresh-method-missing";
            if (mission == null || playerTeam == null)
                return false;

            try
            {
                Type coopBattleType = typeof(CoopMissionSelectionView).Assembly.GetType("CoopSpectator.GameMode.MissionMultiplayerCoopBattle");
                MethodInfo method = coopBattleType?.GetMethod(
                    "TryRefreshMissionPlayerTeamRelationView",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null)
                    return false;

                object[] parameters = { mission, playerTeam, source, null };
                bool result = method.Invoke(null, parameters) is bool value && value;
                diagnostics = parameters[3] as string ?? diagnostics;
                return result;
            }
            catch (Exception ex)
            {
                diagnostics = "refresh-method-failed " + ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        private Camera ResolveMissionScreenCombatCamera()
        {
            try
            {
                return MissionScreen?.CombatCamera;
            }
            catch
            {
                return null;
            }
        }

        private void TryRegisterOrderOfBattleHotKeys(OrderOfBattleVM commanderVm)
        {
            if (commanderVm == null)
                return;

            try
            {
                var category = HotKeyManager.GetCategory("OrderOfBattleHotKeyCategory");
                if (category == null)
                    return;

                TryRegisterHotKeyCategoryOnInputContext(TryGetInstancePropertyValue(_gauntletLayer, "Input"), category);
                TryRegisterHotKeyCategoryOnInputContext(TryGetInstancePropertyValue(TryGetInstancePropertyValue(MissionScreen, "SceneLayer"), "Input"), category);
                commanderVm.SetDoneInputKey(category.GetHotKey("Confirm"));
                commanderVm.SetResetInputKey(category.GetHotKey("AutoDeploy"));
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: failed to register OrderOfBattle hotkeys: " + ex.Message);
            }
        }

        private static bool TryRegisterHotKeyCategoryOnInputContext(object inputContext, object category)
        {
            if (inputContext == null || category == null)
                return false;

            try
            {
                MethodInfo method = inputContext.GetType()
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(candidate =>
                    {
                        if (!string.Equals(candidate.Name, "RegisterHotKeyCategory", StringComparison.Ordinal))
                            return false;

                        ParameterInfo[] parameters = candidate.GetParameters();
                        return parameters.Length == 1 &&
                               parameters[0].ParameterType.IsInstanceOfType(category);
                    });

                if (method == null)
                    return false;

                method.Invoke(inputContext, new[] { category });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void EnsureCommanderDeploymentSpriteCategoryLoaded()
        {
            _commanderDeploymentOrderOfBattleSpriteCategory = TryLoadCommanderDeploymentSpriteCategory(
                _commanderDeploymentOrderOfBattleSpriteCategory,
                "ui_order_of_battle",
                "commander-deployment");
            _commanderDeploymentOrderSpriteCategory = TryLoadCommanderDeploymentSpriteCategory(
                _commanderDeploymentOrderSpriteCategory,
                "ui_order",
                "commander-deployment");
        }

        private void EnsureCommanderBattleOrderSpriteCategoryLoaded()
        {
            _commanderBattleOrderSpriteCategory = TryLoadCommanderDeploymentSpriteCategory(
                _commanderBattleOrderSpriteCategory,
                "ui_order",
                "commander-battle-order");
        }

        private static SpriteCategory TryLoadCommanderDeploymentSpriteCategory(
            SpriteCategory currentCategory,
            string categoryName,
            string source)
        {
            if (currentCategory != null)
                return currentCategory;

            try
            {
                SpriteCategory category = UIResourceManager.LoadSpriteCategory(categoryName);
                ModLogger.Info(
                    "CoopMissionSelectionView: sprite category load. " +
                    "Source=" + (source ?? "unknown") +
                    " Category=" + (categoryName ?? "null") +
                    " Loaded=" + (category != null));
                return category;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: failed to load commander deployment sprite category " +
                    categoryName +
                    " Source=" + (source ?? "unknown") +
                    ": " + ex.Message);
                return null;
            }
        }

        private void ReleaseCommanderDeploymentSpriteCategory()
        {
            _commanderDeploymentOrderOfBattleSpriteCategory = ReleaseCommanderDeploymentSpriteCategory(
                _commanderDeploymentOrderOfBattleSpriteCategory,
                "ui_order_of_battle");
            _commanderDeploymentOrderSpriteCategory = ReleaseCommanderDeploymentSpriteCategory(
                _commanderDeploymentOrderSpriteCategory,
                "ui_order");
        }

        private void ReleaseCommanderBattleOrderSpriteCategory()
        {
            _commanderBattleOrderSpriteCategory = ReleaseCommanderDeploymentSpriteCategory(
                _commanderBattleOrderSpriteCategory,
                "ui_order");
        }

        private static SpriteCategory ReleaseCommanderDeploymentSpriteCategory(
            SpriteCategory category,
            string categoryName)
        {
            if (category == null)
                return null;

            try
            {
                category.Unload();
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: failed to unload commander deployment sprite category " +
                    categoryName + ": " + ex.Message);
            }

            return null;
        }

        private void TryAttachCommanderDeploymentOrderTroopPlacerCallback(OrderOfBattleVM commanderVm)
        {
            if (commanderVm == null || _commanderDeploymentOnUnitDeployedHandler != null)
                return;

            object orderTroopPlacer = ResolveNativeCommanderOrderTroopPlacer();
            if (orderTroopPlacer == null)
                return;

            Action handler = commanderVm.OnUnitDeployed;
            if (!TryUpdateActionMember(orderTroopPlacer, "OnUnitDeployed", handler, add: true))
                return;

            _commanderDeploymentOrderTroopPlacer = orderTroopPlacer;
            _commanderDeploymentOnUnitDeployedHandler = handler;
        }

        private void ReleaseCommanderDeploymentOrderTroopPlacerCallback()
        {
            if (_commanderDeploymentOrderTroopPlacer != null && _commanderDeploymentOnUnitDeployedHandler != null)
            {
                TryUpdateActionMember(
                    _commanderDeploymentOrderTroopPlacer,
                    "OnUnitDeployed",
                    _commanderDeploymentOnUnitDeployedHandler,
                    add: false);
            }

            _commanderDeploymentOrderTroopPlacer = null;
            _commanderDeploymentOnUnitDeployedHandler = null;
        }

        private static bool TryUpdateActionMember(object instance, string memberName, Action handler, bool add)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName) || handler == null)
                return false;

            try
            {
                Type type = instance.GetType();
                FieldInfo field = FindInstanceField(type, memberName);
                if (field != null && typeof(Delegate).IsAssignableFrom(field.FieldType))
                {
                    Delegate current = field.GetValue(instance) as Delegate;
                    Delegate updated = add
                        ? Delegate.Combine(current, handler)
                        : Delegate.Remove(current, handler);
                    if (updated == null || field.FieldType.IsInstanceOfType(updated))
                    {
                        field.SetValue(instance, updated);
                        return true;
                    }
                }

                PropertyInfo property = FindInstanceProperty(type, memberName);
                MethodInfo setter = property?.GetSetMethod(true);
                MethodInfo getter = property?.GetGetMethod(true);
                if (property != null &&
                    setter != null &&
                    getter != null &&
                    typeof(Delegate).IsAssignableFrom(property.PropertyType))
                {
                    Delegate current = getter.Invoke(instance, null) as Delegate;
                    Delegate updated = add
                        ? Delegate.Combine(current, handler)
                        : Delegate.Remove(current, handler);
                    if (updated == null || property.PropertyType.IsInstanceOfType(updated))
                    {
                        setter.Invoke(instance, new[] { updated });
                        return true;
                    }
                }

                EventInfo eventInfo = FindInstanceEvent(type, memberName);
                if (eventInfo != null)
                {
                    if (add)
                        eventInfo.AddEventHandler(instance, handler);
                    else
                        eventInfo.RemoveEventHandler(instance, handler);
                    return true;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: OrderTroopPlacer OnUnitDeployed bridge update failed: " +
                    ex.GetType().Name + ":" + ex.Message);
            }

            return false;
        }

        private void SelectNativeCommanderFormationAtIndex(int formationIndex)
        {
            TryActivateNativeCommanderDeploymentPlacement(MissionScreen);
            Formation formation = ResolveNativeCommanderFormationAtIndex(formationIndex);
            OrderController orderController = Mission?.PlayerTeam?.PlayerOrderController;
            if (formation == null || orderController == null)
                return;

            try
            {
                TryAlignNativeCommanderFormationOwner(formation, orderController.Owner);
                if (TrySelectCommanderDeploymentOrderBridgeFormation(formationIndex))
                    return;

                if (TryInvokeNativeCommanderOrderUiHandlerMethod("SelectFormationAtIndex", formationIndex))
                    return;

                if (!orderController.IsFormationListening(formation) &&
                    orderController.IsFormationSelectable(formation))
                {
                    orderController.SelectFormation(formation);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: native OOB select formation failed: " + ex.Message);
            }
        }

        private void DeselectNativeCommanderFormationAtIndex(int formationIndex)
        {
            TryActivateNativeCommanderDeploymentPlacement(MissionScreen);
            Formation formation = ResolveNativeCommanderFormationAtIndex(formationIndex);
            OrderController orderController = Mission?.PlayerTeam?.PlayerOrderController;
            if (formation == null || orderController == null)
                return;

            try
            {
                TryAlignNativeCommanderFormationOwner(formation, orderController.Owner);
                if (TryDeselectCommanderDeploymentOrderBridgeFormation(formationIndex))
                    return;

                if (TryInvokeNativeCommanderOrderUiHandlerMethod("DeselectFormationAtIndex", formationIndex))
                    return;

                if (orderController.IsFormationListening(formation))
                    orderController.DeselectFormation(formation);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: native OOB deselect formation failed: " + ex.Message);
            }
        }

        private static void TryAlignNativeCommanderFormationOwner(Formation formation, Agent owner)
        {
            if (formation == null || owner == null || formation.Team == null || !ReferenceEquals(formation.Team, owner.Team))
                return;

            try
            {
                if (!ReferenceEquals(formation.PlayerOwner, owner))
                    formation.PlayerOwner = owner;
            }
            catch
            {
            }
        }

        private void ClearNativeCommanderFormationSelection()
        {
            try
            {
                if (TryClearCommanderDeploymentOrderBridgeSelection())
                    return;

                if (TryInvokeNativeCommanderOrderUiHandlerMethod("ClearFormationSelection"))
                    return;

                Mission?.PlayerTeam?.PlayerOrderController?.ClearSelectedFormations();
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: native OOB clear formation selection failed: " + ex.Message);
            }
        }

        private bool TrySelectCommanderDeploymentOrderBridgeFormation(int formationIndex)
        {
            if (!TryEnsureCommanderDeploymentOrderBridge())
                return false;

            try
            {
                bool handled = TryInvokeInstanceMethodSuccessfully(
                    _commanderDeploymentOrderVm.TroopController,
                    "OnSelectFormationWithIndex",
                    formationIndex);
                if (!handled)
                {
                    handled = TryInvokeInstanceMethodSuccessfully(
                        _commanderDeploymentOrderVm,
                        "OnTroopFormationSelected",
                        formationIndex);
                }

                if (!handled)
                    return false;

                TryDeselectCommanderDeploymentSelectedOrderSet();
                TryInvokeInstanceMethodSuccessfully(_commanderDeploymentOrderVm.TroopController, "UpdateTroops");
                TryRefreshCommanderDeploymentCountsAfterSelection(
                    _commanderDeploymentViewModel,
                    Mission,
                    "mission-order-select-formation");
                TryOpenCommanderDeploymentOrderMenuUnchecked();
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: safe MissionOrderVM select bridge failed: " + ex.Message);
                return false;
            }
        }

        private bool TryDeselectCommanderDeploymentOrderBridgeFormation(int formationIndex)
        {
            if (!TryEnsureCommanderDeploymentOrderBridge())
                return false;

            try
            {
                bool handled = TryInvokeInstanceMethodSuccessfully(
                    _commanderDeploymentOrderVm.TroopController,
                    "OnDeselectFormation",
                    formationIndex);
                if (!handled)
                    return false;

                TryInvokeInstanceMethodSuccessfully(_commanderDeploymentOrderVm.TroopController, "UpdateTroops");
                TryRefreshCommanderDeploymentCountsAfterSelection(
                    _commanderDeploymentViewModel,
                    Mission,
                    "mission-order-deselect-formation");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: safe MissionOrderVM deselect bridge failed: " + ex.Message);
                return false;
            }
        }

        private bool TryClearCommanderDeploymentOrderBridgeSelection()
        {
            if (!TryEnsureCommanderDeploymentOrderBridge())
                return false;

            try
            {
                TryInvokeInstanceMethodSuccessfully(
                    _commanderDeploymentOrderVm.DeploymentController,
                    "ExecuteCancelSelectedDeploymentPoint");
                Mission?.PlayerTeam?.PlayerOrderController?.ClearSelectedFormations();
                TryInvokeInstanceMethodSuccessfully(_commanderDeploymentOrderVm, "TryCloseToggleOrder", false);
                TryInvokeInstanceMethodSuccessfully(_commanderDeploymentOrderVm.TroopController, "UpdateTroops");
                TryRefreshCommanderDeploymentCountsAfterSelection(
                    _commanderDeploymentViewModel,
                    Mission,
                    "mission-order-clear-selection");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: safe MissionOrderVM clear bridge failed: " + ex.Message);
                return false;
            }
        }

        private bool TryActivateNativeCommanderDeploymentPlacement(ScreenBase missionScreen)
        {
            if (_commanderDeploymentViewModel != null)
                TryAttachCommanderDeploymentOrderTroopPlacerCallback(_commanderDeploymentViewModel);

            object orderTroopPlacer = ResolveNativeCommanderOrderTroopPlacer();
            if (orderTroopPlacer == null)
                return false;

            try
            {
                bool boundariesReady =
                    TryEnsureCommanderDeploymentBoundaries("native-placement-activate");
                bool blockExactSallyOutPlacement =
                    IsCurrentExactSallyOutCommanderDeploymentScenario(Mission) &&
                    !boundariesReady;
                TrySetInstanceProperty(
                    orderTroopPlacer,
                    "SuspendTroopPlacer",
                    blockExactSallyOutPlacement);
                object orderFlag = TryGetInstancePropertyValue(orderTroopPlacer, "OrderFlag");
                if (missionScreen != null && orderFlag != null)
                {
                    TrySetInstanceProperty(missionScreen, "OrderFlag", orderFlag);
                    TryInvokeInstanceMethod(missionScreen, "SetOrderFlagVisibility", true);
                }

                TryActivateCommanderDeploymentFreeCamera(missionScreen, "native-placement-activate");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: native commander deployment placement bridge failed: " + ex.Message);
                return false;
            }
        }

        private void TryDeactivateNativeCommanderDeploymentPlacement(ScreenBase missionScreen)
        {
            object orderTroopPlacer = ResolveNativeCommanderOrderTroopPlacer();
            if (orderTroopPlacer == null)
                return;

            try
            {
                TryInvokeInstanceMethod(orderTroopPlacer, "RestrictOrdersToDeploymentBoundaries", false);
                TrySetInstanceProperty(orderTroopPlacer, "SuspendTroopPlacer", true);
                if (missionScreen != null)
                    TryInvokeInstanceMethod(missionScreen, "SetOrderFlagVisibility", false);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: native commander deployment placement bridge release failed: " + ex.Message);
            }
        }

        private object ResolveNativeCommanderOrderTroopPlacer()
        {
            try
            {
                List<MissionBehavior> behaviors = Mission?.MissionBehaviors;
                if (behaviors == null)
                    return null;

                for (int i = 0; i < behaviors.Count; i++)
                {
                    MissionBehavior behavior = behaviors[i];
                    Type behaviorType = behavior?.GetType();
                    if (behaviorType == null)
                        continue;

                    if (string.Equals(
                            behaviorType.FullName,
                            "TaleWorlds.MountAndBlade.View.MissionViews.Order.OrderTroopPlacer",
                            StringComparison.Ordinal) ||
                        string.Equals(behaviorType.Name, "OrderTroopPlacer", StringComparison.Ordinal))
                    {
                        return behavior;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private object ResolveNativeCommanderSingleplayerOrderUiHandler()
        {
            try
            {
                List<MissionBehavior> behaviors = Mission?.MissionBehaviors;
                if (behaviors == null)
                    return null;

                for (int i = 0; i < behaviors.Count; i++)
                {
                    MissionBehavior behavior = behaviors[i];
                    Type behaviorType = behavior?.GetType();
                    if (behaviorType == null)
                        continue;

                    if (string.Equals(
                            behaviorType.FullName,
                            "TaleWorlds.MountAndBlade.GauntletUI.Mission.Singleplayer.MissionGauntletSingleplayerOrderUIHandler",
                            StringComparison.Ordinal) ||
                        string.Equals(behaviorType.Name, "MissionGauntletSingleplayerOrderUIHandler", StringComparison.Ordinal))
                    {
                        return behavior;
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        private bool TryInvokeNativeCommanderOrderUiHandlerMethod(string methodName, params object[] arguments)
        {
            object handler = ResolveNativeCommanderSingleplayerOrderUiHandler();
            if (handler == null)
                return false;

            try
            {
                MethodInfo method = FindInstanceMethod(handler.GetType(), methodName, arguments);
                if (method == null)
                    return false;

                method.Invoke(handler, arguments);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: native order UI handler bridge failed. " +
                    "Method=" + (methodName ?? string.Empty) +
                    " Error=" + ex.GetType().Name + ":" + ex.Message);
                return false;
            }
        }

        private Formation ResolveNativeCommanderFormationAtIndex(int formationIndex)
        {
            if (formationIndex < 0)
                return null;

            try
            {
                IEnumerable<Formation> formations = Mission?.PlayerTeam?.FormationsIncludingEmpty;
                if (formations == null)
                    return null;

                foreach (Formation formation in formations)
                {
                    if (formation?.Index == formationIndex)
                        return formation;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private void HandleSideSelected(BattleSideEnum side)
        {
            if (side == BattleSideEnum.None)
                return;

            bool hasLocalControlledAgent = HasLocalControlledAgent();
            CoopSelectionUiSnapshot snapshot = BuildCurrentSnapshot(hasLocalControlledAgent);
            if (snapshot?.ReconnectSelectionContractActive == true &&
                snapshot.AuthoritativeAssignedSide != BattleSideEnum.None)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: ignored side selection during reconnect selection contract. " +
                    "RequestedSide=" + side +
                    " AuthoritativeSide=" + snapshot.AuthoritativeAssignedSide);
                RefreshOverlay(force: true, hasLocalControlledAgent);
                return;
            }

            _spectatorOverlayHidden = false;
            _selectedSideOverride = side;
            _selectedEntryIdOverride = ResolveInitialExactSiegeSelectedEntryId(side, hasLocalControlledAgent);
            _requestedScreen = CoopSelectionScreen.ClassLoadout;
            bool sideQueued = CoopBattleNetworkRequestTransport.TrySelectSide(side, "CoopTeamSelectionUI Side");
            if (sideQueued && !string.IsNullOrWhiteSpace(_selectedEntryIdOverride))
            {
                CoopBattleNetworkRequestTransport.TrySelectEntry(
                    side,
                    _selectedEntryIdOverride,
                    "CoopTeamSelectionUI InitialEntry");
            }

            RefreshOverlay(force: true, hasLocalControlledAgent);
        }

        private string ResolveInitialExactSiegeSelectedEntryId(BattleSideEnum side, bool hasLocalControlledAgent)
        {
            if (side == BattleSideEnum.None ||
                !IsCurrentCommanderDeploymentScenario(Mission))
            {
                return null;
            }

            CoopSelectionUiSnapshot sideSnapshot = BuildCurrentSnapshot(hasLocalControlledAgent);
            if (sideSnapshot == null || sideSnapshot.EffectiveSide != side)
                return null;

            if (!string.IsNullOrWhiteSpace(sideSnapshot.SelectedEntryId))
                return sideSnapshot.SelectedEntryId;

            return sideSnapshot.EffectiveSelectableEntryIds?.FirstOrDefault(entryId => !string.IsNullOrWhiteSpace(entryId));
        }

        private void HandleUnitSelected(BattleSideEnum side, string entryId)
        {
            if (side == BattleSideEnum.None || string.IsNullOrWhiteSpace(entryId))
                return;

            _spectatorOverlayHidden = false;
            _selectedSideOverride = side;
            _selectedEntryIdOverride = entryId;
            _requestedScreen = CoopSelectionScreen.ClassLoadout;
            CoopBattleNetworkRequestTransport.TrySelectEntry(side, entryId, "CoopClassLoadoutUI Entry");
            RefreshOverlay(force: true, HasLocalControlledAgent());
        }

        private void HandleAutoAssignRequested()
        {
            bool hasLocalControlledAgent = HasLocalControlledAgent();
            CoopSelectionUiSnapshot snapshot = BuildCurrentSnapshot(hasLocalControlledAgent);
            BattleSideEnum[] availableSides = new[]
            {
                (snapshot?.AttackerSelectableEntryCount ?? 0) > 0 ? BattleSideEnum.Attacker : BattleSideEnum.None,
                (snapshot?.DefenderSelectableEntryCount ?? 0) > 0 ? BattleSideEnum.Defender : BattleSideEnum.None
            }
                .Where(side => side != BattleSideEnum.None)
                .ToArray();
            if (availableSides.Length <= 0)
                return;

            BattleSideEnum chosenSide = availableSides.Length == 1
                ? availableSides[0]
                : availableSides[MBRandom.RandomInt(availableSides.Length)];
            ModLogger.Info(
                "CoopMissionSelectionView: auto assign requested. " +
                "ChosenSide=" + chosenSide +
                " AttackerSelectable=" + (snapshot?.AttackerSelectableEntryCount ?? 0) +
                " DefenderSelectable=" + (snapshot?.DefenderSelectableEntryCount ?? 0));
            HandleSideSelected(chosenSide);
        }

        private void HandleSpectatorRequested()
        {
            ClearLocalSpawnPending("spectator-requested");
            _spectatorOverlayHidden = true;
            _requestedScreen = CoopSelectionScreen.TeamSelection;
            _selectedSideOverride = BattleSideEnum.None;
            _selectedEntryIdOverride = null;
            _overlaySuppressedUntilUtc = DateTime.MinValue;
            ReleaseOverlayInput();
            ReleaseCurrentMovie();
            TryDeactivateCommanderDeploymentFreeCamera(MissionScreen, "spectator-requested");

            if (CoopBattleNetworkRequestTransport.TrySelectSpectator("CoopTeamSelectionUI Spectator"))
            {
                InformationManager.DisplayMessage(new InformationMessage("Coop Battle: spectator mode enabled. Press H to reopen selection."));
                ModLogger.Info("CoopMissionSelectionView: wrote spectator selection request.");
            }

            RefreshOverlay(force: true, HasLocalControlledAgent());
        }

        private void HandleSpawnRequested()
        {
            bool hasLocalControlledAgent = HasLocalControlledAgent();
            CoopSelectionUiSnapshot snapshot = BuildCurrentSnapshot(hasLocalControlledAgent);
            if (snapshot == null ||
                (!snapshot.CanSpawn && !snapshot.CanQueueSpawnAfterDeployment) ||
                snapshot.EffectiveSide == BattleSideEnum.None ||
                string.IsNullOrWhiteSpace(snapshot.SelectedEntryId))
                return;

            _selectedSideOverride = snapshot.EffectiveSide;
            _selectedEntryIdOverride = snapshot.SelectedEntryId;
            _spectatorOverlayHidden = false;

            if (ShouldRequestSiegeCommanderDeployment(snapshot))
            {
                ClearLocalSpawnPending("commander-deployment-requested");
                _overlaySuppressedUntilUtc = DateTime.MinValue;
                if (TrySendCommanderDeploymentRequest(snapshot, "CoopClassLoadoutUI CommanderDeployment"))
                    _requestedScreen = CoopSelectionScreen.CommanderDeployment;
                RefreshOverlay(force: true, hasLocalControlledAgent);
                return;
            }

            if (snapshot.CanQueueSpawnAfterDeployment)
            {
                MarkLocalSpawnPending(snapshot, waitsForDeployment: true);
                _overlaySuppressedUntilUtc = DateTime.UtcNow + LocalSpawnOverlaySuppressionDuration;
                bool queued = CoopBattleNetworkRequestTransport.TryQueueSpawnAfterDeployment(
                        snapshot.EffectiveSide,
                        snapshot.SelectedEntryId,
                        "CoopClassLoadoutUI DeploymentWait");
                if (!queued)
                {
                    ClearLocalSpawnPending("deployment-wait-request-write-failed");
                }
                RefreshOverlay(force: true, hasLocalControlledAgent);
                return;
            }

            MarkLocalSpawnPending(snapshot, waitsForDeployment: false);
            _overlaySuppressedUntilUtc = DateTime.UtcNow + LocalSpawnOverlaySuppressionDuration;
            if (!TrySendPendingSpawnRequests("CoopClassLoadoutUI Spawn", includeSelectEntry: true))
                ClearLocalSpawnPending("spawn-request-write-failed");
            RefreshOverlay(force: true, hasLocalControlledAgent);
        }

        private bool ShouldRequestSiegeCommanderDeployment(CoopSelectionUiSnapshot snapshot)
        {
            if (!EnableManualSiegeCommanderDeployment)
                return false;

            if (snapshot == null ||
                snapshot.EffectiveSide == BattleSideEnum.None ||
                string.IsNullOrWhiteSpace(snapshot.SelectedEntryId))
            {
                return false;
            }

            if (snapshot.HasLocalControlledAgent || snapshot.Status?.HasAgent == true)
                return false;

            BattleScenarioContextMessage scenarioContext =
                snapshot.BattleState?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetScenarioContext();
            if (!ExactCampaignCommanderDeploymentRuntime.IsCommanderDeploymentScenario(Mission, scenarioContext))
                return false;

            string readinessStage = snapshot.BattleDataReadinessStage ?? string.Empty;
            bool isDeploymentSelectionStage =
                string.Equals(readinessStage, "UnitSelection", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(readinessStage, "CommanderDeployment", StringComparison.OrdinalIgnoreCase);
            if (!isDeploymentSelectionStage)
                return false;

            string battlePhase = snapshot.BattlePhase ?? string.Empty;
            if (string.Equals(battlePhase, nameof(CoopBattlePhase.BattleActive), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(battlePhase, nameof(CoopBattlePhase.BattleEnded), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            RosterEntryState entryState = CoopSelectionUiHelpers.ResolveEntryState(
                snapshot.EffectiveSide,
                snapshot.SelectedEntryId);
            return CoopSelectionUiHelpers.IsCommanderEntry(
                snapshot.BattleState,
                snapshot.EffectiveSide,
                entryState);
        }

        private bool TrySendCommanderDeploymentRequest(CoopSelectionUiSnapshot snapshot, string source)
        {
            if (snapshot == null ||
                snapshot.EffectiveSide == BattleSideEnum.None ||
                string.IsNullOrWhiteSpace(snapshot.SelectedEntryId))
            {
                return false;
            }

            bool queued = CoopBattleNetworkRequestTransport.TryBeginCommanderDeployment(
                snapshot.EffectiveSide,
                snapshot.SelectedEntryId,
                source);
            if (queued)
            {
                InformationManager.DisplayMessage(new InformationMessage("Coop Battle: commander deployment requested."));
            }
            else
            {
                InformationManager.DisplayMessage(new InformationMessage("Coop Battle: commander deployment request failed."));
            }

            ModLogger.Info(
                "CoopMissionSelectionView: commander deployment request. " +
                "Queued=" + queued +
                " Side=" + snapshot.EffectiveSide +
                " EntryId=" + (snapshot.SelectedEntryId ?? string.Empty) +
                " Source=" + (source ?? "unknown"));
            return queued;
        }

        private void HandleCommanderAutoDeployRequested()
        {
            TryApplyCommanderDeploymentOrderBridgeAutoDeploy();
            TrySendCommanderDeploymentCompletionRequest(
                autoDeploy: true,
                source: "CoopCommanderDeploymentUI AutoDeploy");
        }

        private void HandleCommanderReadyRequested()
        {
            if (!TryApplyCommanderDeploymentOrderBridgeReady())
            {
                InformationManager.DisplayMessage(
                    new InformationMessage(
                        "Coop Battle: final commander deployment layout could not be sent."));
                RefreshOverlay(force: true, HasLocalControlledAgent());
                return;
            }

            TrySendCommanderDeploymentCompletionRequest(
                autoDeploy: false,
                source: "CoopCommanderDeploymentUI FinishDeployment");
        }

        private void TryApplyCommanderDeploymentOrderBridgeAutoDeploy()
        {
            if (!TryEnsureCommanderDeploymentOrderBridge())
                return;

            try
            {
                bool handled = TryInvokeInstanceMethodSuccessfully(
                    _commanderDeploymentOrderVm.DeploymentController,
                    "ExecuteAutoDeploy");
                if (!handled)
                    handled = TryInvokeInstanceMethodSuccessfully(_commanderDeploymentOrderVm, "OnDeployAll");

                TryClearCommanderDeploymentOrderBridgeSelection();
                TryRefreshNativeCommanderOrderOfBattleCounts(
                    _commanderDeploymentViewModel,
                    Mission,
                    "mission-order-auto-deploy");
                OrderOfBattleSiegeProjectedCountsPatch.TrySyncCommanderDeploymentFormationAssignmentsForTeam(
                    Mission?.PlayerTeam,
                    "CoopMissionSelectionView mission-order-auto-deploy");
                ModLogger.Info("CoopMissionSelectionView: applied safe MissionOrderVM auto deployment. Handled=" + handled);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: safe MissionOrderVM auto deploy bridge failed: " + ex.Message);
            }
        }

        private bool TryApplyCommanderDeploymentOrderBridgeReady()
        {
            if (!TryEnsureCommanderDeploymentOrderBridge())
                return !RequiresSynchronizedFinalFormationLayouts(Mission);

            try
            {
                if (_commanderDeploymentViewModel?.CurrentConfiguration != null)
                {
                    TryInvokeInstanceMethodSuccessfully(
                        _commanderDeploymentOrderVm,
                        "OnFiltersSet",
                        _commanderDeploymentViewModel.CurrentConfiguration);
                }

                bool preservePerSideCommanderDeployment =
                    IsCurrentCommanderDeploymentScenario(Mission);
                bool handled = preservePerSideCommanderDeployment;
                if (!preservePerSideCommanderDeployment)
                {
                    handled = TryInvokeInstanceMethodSuccessfully(
                        _commanderDeploymentOrderVm.DeploymentController,
                        "ExecuteBeginMission");
                    if (!handled)
                    {
                        handled = TryInvokeInstanceMethodSuccessfully(
                            _commanderDeploymentOrderVm,
                            "OnDeploymentFinished");
                    }
                }

                TryRefreshNativeCommanderOrderOfBattleCounts(
                    _commanderDeploymentViewModel,
                    Mission,
                    "mission-order-ready");
                bool requiresFinalFormationLayouts =
                    RequiresSynchronizedFinalFormationLayouts(Mission);
                bool finalLayoutSent =
                    OrderOfBattleSiegeProjectedCountsPatch.TrySyncCommanderDeploymentFormationAssignmentsForTeam(
                    Mission?.PlayerTeam,
                    "CoopMissionSelectionView mission-order-ready",
                    includeFormationLayouts: requiresFinalFormationLayouts,
                    requireFormationLayouts: requiresFinalFormationLayouts,
                    forceSend: requiresFinalFormationLayouts);
                ModLogger.Info(
                    "CoopMissionSelectionView: applied safe MissionOrderVM ready deployment. " +
                    "Handled=" + handled +
                    " PreservedPerSideCommanderDeployment=" + preservePerSideCommanderDeployment +
                    " FinalLayoutRequired=" + requiresFinalFormationLayouts +
                    " FinalLayoutSent=" + finalLayoutSent);
                return !requiresFinalFormationLayouts || finalLayoutSent;
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: safe MissionOrderVM ready bridge failed: " + ex.Message);
                return !RequiresSynchronizedFinalFormationLayouts(Mission);
            }
        }

        private bool TrySendCommanderDeploymentCompletionRequest(bool autoDeploy, string source)
        {
            bool hasLocalControlledAgent = HasLocalControlledAgent();
            CoopSelectionUiSnapshot snapshot = BuildCurrentSnapshot(hasLocalControlledAgent);
            if (!IsCommanderDeploymentReady(snapshot))
            {
                InformationManager.DisplayMessage(new InformationMessage("Coop Battle: commander deployment is not ready."));
                RefreshOverlay(force: true, hasLocalControlledAgent);
                return false;
            }

            _selectedSideOverride = snapshot.EffectiveSide;
            _selectedEntryIdOverride = snapshot.SelectedEntryId;
            _requestedScreen = CoopSelectionScreen.CommanderDeployment;
            _spectatorOverlayHidden = false;

            string commanderAuthorityEntryId =
                ResolveCommanderDeploymentAuthorityEntryId(snapshot);
            if (string.IsNullOrWhiteSpace(commanderAuthorityEntryId))
            {
                InformationManager.DisplayMessage(
                    new InformationMessage("Coop Battle: commander deployment authority is unavailable."));
                RefreshOverlay(force: true, hasLocalControlledAgent);
                return false;
            }

            CoopSiegeOrderOfBattleVM reusableCaptainVm =
                autoDeploy ? _commanderDeploymentViewModel as CoopSiegeOrderOfBattleVM : null;
            reusableCaptainVm?.BeginAutoDeployCaptainAssignmentPreservation(Mission?.PlayerTeam);

            bool queued = autoDeploy
                ? CoopBattleNetworkRequestTransport.TryAutoDeployCommanderDeployment(
                    snapshot.EffectiveSide,
                    commanderAuthorityEntryId,
                    source)
                : CoopBattleNetworkRequestTransport.TryFinishCommanderDeployment(
                    snapshot.EffectiveSide,
                    commanderAuthorityEntryId,
                    source);
            if (autoDeploy && !queued)
                reusableCaptainVm?.CancelAutoDeployCaptainAssignmentPreservation();
            if (queued && !autoDeploy)
            {
                MarkLocalSpawnPending(snapshot, waitsForDeployment: true);
                _overlaySuppressedUntilUtc = DateTime.UtcNow + LocalSpawnOverlaySuppressionDuration;
                ReleaseOverlayInput();
                ReleaseCurrentMovie();
            }

            InformationManager.DisplayMessage(
                new InformationMessage(
                    queued
                        ? (autoDeploy
                            ? "Coop Battle: commander auto deployment requested."
                            : "Coop Battle: commander deployment finish requested.")
                        : (autoDeploy
                            ? "Coop Battle: commander auto deployment request failed."
                            : "Coop Battle: commander deployment finish request failed.")));
            ModLogger.Info(
                "CoopMissionSelectionView: commander deployment completion request. " +
                "Queued=" + queued +
                " AutoDeploy=" + autoDeploy +
                " Side=" + snapshot.EffectiveSide +
                " SelectedEntryId=" + (snapshot.SelectedEntryId ?? string.Empty) +
                " CommanderEntryId=" + commanderAuthorityEntryId +
                " Source=" + (source ?? "unknown"));
            if (autoDeploy || !queued)
            {
                RefreshOverlay(force: true, hasLocalControlledAgent);
            }
            return queued;
        }

        private void TryCompleteAutoDeployCaptainAssignmentRestoration()
        {
            if (_currentScreen != CoopSelectionScreen.CommanderDeployment)
                return;

            (_commanderDeploymentViewModel as CoopSiegeOrderOfBattleVM)?
                .TryCompleteAutoDeployCaptainAssignmentRestorationIfStable(Mission?.PlayerTeam);
        }

        private void HandleCommanderBackRequested()
        {
            _requestedScreen = CoopSelectionScreen.ClassLoadout;
            _spectatorOverlayHidden = false;
            RefreshOverlay(force: true, HasLocalControlledAgent());
        }

        private void HandleBackRequested()
        {
            ResetSelectionFlow("class-back");
            RefreshOverlay(force: true, HasLocalControlledAgent());
        }

        private void ResetSelectionFlow(string source)
        {
            _requestedScreen = CoopSelectionScreen.TeamSelection;
            _selectedSideOverride = BattleSideEnum.None;
            _selectedEntryIdOverride = null;
            _spectatorOverlayHidden = false;
            ModLogger.Info("CoopMissionSelectionView: reset selection flow. Source=" + source);
        }

        private void HandleLostLocalAgentSelectionFlow()
        {
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status = ReadCurrentMissionEntryStatus();
            if (ShouldKeepBattleActiveSelectionSide(status, out BattleSideEnum assignedSide))
            {
                _requestedScreen = CoopSelectionScreen.ClassLoadout;
                _selectedSideOverride = assignedSide;
                _selectedEntryIdOverride = null;
                _spectatorOverlayHidden = false;
                ModLogger.Info(
                    "CoopMissionSelectionView: kept battle-active selection flow after local agent loss. " +
                    "AssignedSide=" + assignedSide +
                    " BattlePhase=" + (status?.BattlePhase ?? string.Empty) +
                    " ReadinessStage=" + (status?.BattleDataReadinessStage ?? string.Empty) +
                    " Lifecycle=" + (status?.LifecycleState ?? string.Empty) +
                    " CanRespawn=" + (status?.CanRespawn.ToString() ?? bool.FalseString));
                return;
            }

            ResetSelectionFlow("lost-local-agent");
        }

        private static bool ShouldKeepBattleActiveSelectionSide(
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status,
            out BattleSideEnum assignedSide)
        {
            assignedSide = CoopSelectionUiHelpers.NormalizeStatusSide(status?.AssignedSide);
            if (status == null || assignedSide == BattleSideEnum.None)
                return false;

            string battlePhase = !string.IsNullOrWhiteSpace(status.BattlePhase)
                ? status.BattlePhase
                : CoopBattlePhaseBridgeFile.ReadStatus()?.Phase.ToString() ?? string.Empty;
            bool battleActive =
                string.Equals(battlePhase, nameof(CoopBattlePhase.BattleActive), StringComparison.OrdinalIgnoreCase);
            if (!battleActive)
                return false;

            string readinessStage = status.BattleDataReadinessStage ?? string.Empty;
            return status.CanRespawn ||
                   string.Equals(readinessStage, "RespawnSelection", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status.LifecycleState, "Respawnable", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(status.LifecycleState, "DeadAwaitingRespawn", StringComparison.OrdinalIgnoreCase);
        }

        private void MarkLocalSpawnPending(CoopSelectionUiSnapshot snapshot, bool waitsForDeployment)
        {
            _localSpawnPending = true;
            _localSpawnPendingWaitsForDeployment = waitsForDeployment;
            _localSpawnPendingStartedUtc = DateTime.UtcNow;
            _localSpawnPendingEntryId = snapshot?.SelectedEntryId;
            _localSpawnPendingSide = snapshot?.EffectiveSide ?? BattleSideEnum.None;
            _localSpawnPendingLastRequestUtc = DateTime.MinValue;
            _localSpawnPendingRequestAttemptCount = 0;
            ModLogger.Info(
                "CoopMissionSelectionView: marked local spawn pending. " +
                "Side=" + _localSpawnPendingSide +
                " EntryId=" + (_localSpawnPendingEntryId ?? string.Empty) +
                " WaitsForDeployment=" + _localSpawnPendingWaitsForDeployment);
        }

        private void ClearLocalSpawnPending(string source)
        {
            if (!_localSpawnPending)
                return;

            ModLogger.Info(
                "CoopMissionSelectionView: cleared local spawn pending. " +
                "Source=" + source +
                " Side=" + _localSpawnPendingSide +
                " EntryId=" + (_localSpawnPendingEntryId ?? string.Empty));
            _localSpawnPending = false;
            _localSpawnPendingWaitsForDeployment = false;
            _localSpawnPendingStartedUtc = DateTime.MinValue;
            _localSpawnPendingEntryId = null;
            _localSpawnPendingSide = BattleSideEnum.None;
            _localSpawnPendingLastRequestUtc = DateTime.MinValue;
            _localSpawnPendingRequestAttemptCount = 0;
            _overlaySuppressedUntilUtc = DateTime.MinValue;
        }

        private bool ShouldKeepOverlaySuppressedWhileAwaitingLocalSpawn(CoopSelectionUiSnapshot snapshot)
        {
            if (!_localSpawnPending)
                return false;

            if (snapshot?.IsBattleEnded == true ||
                string.Equals(snapshot?.BattleDataReadinessStage, "BattleEnded", StringComparison.OrdinalIgnoreCase))
            {
                ClearLocalSpawnPending("battle-ended");
                return false;
            }

            if (snapshot?.HasLocalControlledAgent == true || snapshot?.Status?.HasAgent == true)
            {
                ClearLocalSpawnPending("authoritative-agent-ready");
                return false;
            }

            if (_localSpawnPendingWaitsForDeployment)
            {
                if (IsExactSiegeDeploymentWaitStillActive(snapshot))
                    return true;

                _localSpawnPendingWaitsForDeployment = false;
                _localSpawnPendingStartedUtc = DateTime.UtcNow;
                _localSpawnPendingLastRequestUtc = DateTime.MinValue;
                _localSpawnPendingRequestAttemptCount = 0;
                ModLogger.Info(
                    "CoopMissionSelectionView: deployment wait ended; continuing as normal local spawn pending. " +
                    "Side=" + _localSpawnPendingSide +
                    " EntryId=" + (_localSpawnPendingEntryId ?? string.Empty));
            }

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status = snapshot?.Status;
            string lifecycle = status?.LifecycleState ?? snapshot?.Lifecycle ?? string.Empty;
            if (string.Equals(lifecycle, "AwaitingSelection", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lifecycle, "NoSide", StringComparison.OrdinalIgnoreCase))
            {
                ClearLocalSpawnPending("server-returned-to-selection");
                return false;
            }

            bool pendingTimedOut =
                _localSpawnPendingStartedUtc != DateTime.MinValue &&
                DateTime.UtcNow - _localSpawnPendingStartedUtc >= LocalSpawnPendingTimeout;
            if (status == null)
            {
                TryResendPendingSpawnRequestsIfStale(status, lifecycle, pendingTimedOut);
                if (pendingTimedOut)
                {
                    ClearLocalSpawnPending("timeout-no-status");
                    return false;
                }

                return true;
            }

            bool hasExplicitPendingRequestForEntry =
                !string.IsNullOrWhiteSpace(_localSpawnPendingEntryId) &&
                (string.Equals(status.SpawnRequestEntryId, _localSpawnPendingEntryId, StringComparison.Ordinal) ||
                 string.Equals(status.SelectionRequestEntryId, _localSpawnPendingEntryId, StringComparison.Ordinal));
            bool pendingEntryStillSelectable =
                !string.IsNullOrWhiteSpace(_localSpawnPendingEntryId) &&
                (snapshot?.EffectiveSelectableEntryIds?.Contains(_localSpawnPendingEntryId, StringComparer.OrdinalIgnoreCase) ?? false);
            if (string.Equals(lifecycle, "DeadAwaitingRespawn", StringComparison.OrdinalIgnoreCase) &&
                !status.HasAgent)
            {
                if (pendingEntryStillSelectable && !pendingTimedOut)
                {
                    TryResendPendingSpawnRequestsIfStale(status, lifecycle, pendingTimedOut);
                    return true;
                }

                ClearLocalSpawnPending(pendingEntryStillSelectable
                    ? "dead-awaiting-respawn-timeout"
                    : "dead-awaiting-respawn-entry-no-longer-selectable");
                return false;
            }

            if (hasExplicitPendingRequestForEntry ||
                string.Equals(lifecycle, "Waiting", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lifecycle, "SpawnQueued", StringComparison.OrdinalIgnoreCase) ||
                (!status.CanRespawn && !status.HasAgent))
            {
                TryResendPendingSpawnRequestsIfStale(status, lifecycle, pendingTimedOut);
                return true;
            }

            if (status.CanRespawn && !status.HasAgent)
            {
                if (pendingEntryStillSelectable && !pendingTimedOut)
                {
                    TryResendPendingSpawnRequestsIfStale(status, lifecycle, pendingTimedOut);
                    return true;
                }

                ClearLocalSpawnPending(pendingEntryStillSelectable
                    ? "server-ready-for-new-selection"
                    : "pending-entry-no-longer-selectable");
                return false;
            }

            if (pendingTimedOut)
            {
                ClearLocalSpawnPending("timeout");
                return false;
            }

            ClearLocalSpawnPending("state-no-longer-pending");
            return false;
        }

        private static bool IsExactSiegeDeploymentWaitStillActive(CoopSelectionUiSnapshot snapshot)
        {
            if (snapshot == null)
                return true;

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status = snapshot.Status;
            string battlePhase = snapshot.BattlePhase ?? status?.BattlePhase ?? string.Empty;
            if (Enum.TryParse(battlePhase, true, out CoopBattlePhase parsedPhase) &&
                parsedPhase < CoopBattlePhase.PreBattleHold)
            {
                return true;
            }

            if (status?.CanRespawn == true)
                return false;

            string spawnStatus = status?.SpawnStatus ?? string.Empty;
            if (string.Equals(spawnStatus, CoopBattleSpawnStatus.Pending.ToString(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(spawnStatus, CoopBattleSpawnStatus.Validating.ToString(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(spawnStatus, CoopBattleSpawnStatus.Validated.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string lifecycle = status?.LifecycleState ?? snapshot.Lifecycle ?? string.Empty;
            if (string.Equals(lifecycle, CoopBattlePeerLifecycleStatus.SpawnQueued.ToString(), StringComparison.OrdinalIgnoreCase))
                return false;

            if (Enum.TryParse(battlePhase, true, out parsedPhase))
                return parsedPhase < CoopBattlePhase.BattleActive;

            string readinessStage = snapshot.BattleDataReadinessStage ?? status?.BattleDataReadinessStage ?? string.Empty;
            return string.Equals(readinessStage, "UnitSelection", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(readinessStage, "CommanderDeployment", StringComparison.OrdinalIgnoreCase);
        }

        private bool TrySendPendingSpawnRequests(string source, bool includeSelectEntry)
        {
            if (_localSpawnPendingSide == BattleSideEnum.None || string.IsNullOrWhiteSpace(_localSpawnPendingEntryId))
                return false;

            bool entryQueued = true;
            if (includeSelectEntry)
            {
                entryQueued = CoopBattleNetworkRequestTransport.TrySelectEntry(
                    _localSpawnPendingSide,
                    _localSpawnPendingEntryId,
                    source + " SelectEntry");
            }

            bool spawnQueued = CoopBattleNetworkRequestTransport.TryRequestSpawn(source + " SpawnNow");
            if (entryQueued || spawnQueued)
            {
                _localSpawnPendingLastRequestUtc = DateTime.UtcNow;
                _localSpawnPendingRequestAttemptCount++;
            }

            ModLogger.Info(
                "CoopMissionSelectionView: sent pending local spawn request batch. " +
                "Source=" + source +
                " Attempt=" + _localSpawnPendingRequestAttemptCount +
                " Side=" + _localSpawnPendingSide +
                " EntryId=" + (_localSpawnPendingEntryId ?? string.Empty) +
                " IncludeSelectEntry=" + includeSelectEntry +
                " EntryQueued=" + entryQueued +
                " SpawnQueued=" + spawnQueued);
            return entryQueued && spawnQueued;
        }

        private void TryResendPendingSpawnRequestsIfStale(
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status,
            string lifecycle,
            bool pendingTimedOut)
        {
            if (!_localSpawnPending ||
                pendingTimedOut ||
                _localSpawnPendingSide == BattleSideEnum.None ||
                string.IsNullOrWhiteSpace(_localSpawnPendingEntryId) ||
                _localSpawnPendingRequestAttemptCount >= LocalSpawnPendingMaxRequestAttempts)
            {
                return;
            }

            if (_localSpawnPendingLastRequestUtc != DateTime.MinValue &&
                DateTime.UtcNow - _localSpawnPendingLastRequestUtc < LocalSpawnPendingResendInterval)
            {
                return;
            }

            if (status != null)
            {
                if (status.HasAgent || !status.CanRespawn)
                    return;

                if (string.Equals(lifecycle, "Waiting", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(lifecycle, "SpawnQueued", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            TrySendPendingSpawnRequests("CoopMissionSelectionView PendingRespawnResend", includeSelectEntry: true);
        }

        private void TryTickCommanderDeploymentViewModel()
        {
            if (_currentScreen != CoopSelectionScreen.CommanderDeployment ||
                _commanderDeploymentViewModel == null)
            {
                return;
            }

            try
            {
                _commanderDeploymentViewModel.Tick();
                TryUpdateCommanderDeploymentOrderVmUnchecked();
                TryTickCommanderDeploymentOrderHotkeys();
                _commanderSiegeMachineDeploymentVm?.Tick(ResolveMissionScreenCombatCamera());
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: native OrderOfBattle tick failed: " + ex.Message);
                _commanderDeploymentViewModel = null;
                ReleaseCommanderDeploymentOrderBridge();
                ReleaseCommanderSiegeMachineDeploymentMovie();
            }
        }

        private void TryTickCommanderSiegeMachineDeploymentOverlay(float dt)
        {
            if (_currentScreen != CoopSelectionScreen.CommanderDeployment ||
                _commanderDeploymentViewModel == null ||
                _commanderSiegeMachineDeploymentMovie != null)
            {
                _commanderSiegeMachineDeploymentRetryTimer = 0f;
                return;
            }

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetScenarioContext() ??
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            if (!SiegeAmbushScenarioContract.IsSiegeAmbushScenario(scenarioContext))
                return;

            _commanderSiegeMachineDeploymentRetryTimer -= dt;
            if (_commanderSiegeMachineDeploymentRetryTimer > 0f)
                return;

            _commanderSiegeMachineDeploymentRetryTimer =
                CommanderSiegeMachineDeploymentRetryIntervalSeconds;
            TryEnsureCommanderSiegeMachineDeploymentMovie(snapshot: null);
        }

        private void TryTickCommanderDeploymentBoundaries(float dt)
        {
            if (_currentScreen != CoopSelectionScreen.CommanderDeployment)
            {
                _commanderDeploymentBoundaryRefreshTimer = 0f;
                return;
            }

            _commanderDeploymentBoundaryRefreshTimer -= dt;
            if (_commanderDeploymentBoundaryRefreshTimer > 0f)
                return;

            _commanderDeploymentBoundaryRefreshTimer = 0.5f;
            TryEnsureCommanderDeploymentBoundaries("commander-deployment-boundary-tick");
        }

        private bool TryEnsureCommanderDeploymentBoundaries(string source)
        {
            Mission mission = Mission;
            Team team = mission?.PlayerTeam;
            if (mission == null || team == null)
                return false;

            bool boundariesReady = CoopSiegeDeploymentBoundaryRuntime.TryEnsureDeploymentPlanBoundaries(
                mission,
                team,
                source ?? "unknown");
            if (boundariesReady)
            {
                CoopSiegeDeploymentBoundaryRuntime.TryEnsureVisibleDeploymentBoundaryMarkers(
                    mission,
                    MissionScreen,
                    team,
                    source ?? "unknown");
            }

            object orderTroopPlacer = ResolveNativeCommanderOrderTroopPlacer();
            if (orderTroopPlacer != null)
            {
                bool isExactSallyOut =
                    IsCurrentExactSallyOutCommanderDeploymentScenario(mission);
                bool mayRemainUnrestrictedWithoutReadyBoundaries =
                    isExactSallyOut ||
                    IsCurrentExactFieldBattleCommanderDeploymentScenario(mission) ||
                    IsCurrentExactVillageBattleCommanderDeploymentScenario(mission);
                bool restrictToBoundaries =
                    boundariesReady ||
                    !mayRemainUnrestrictedWithoutReadyBoundaries;
                TryInvokeInstanceMethod(
                    orderTroopPlacer,
                    "RestrictOrdersToDeploymentBoundaries",
                    restrictToBoundaries);
                if (isExactSallyOut)
                {
                    TrySetInstanceProperty(
                        orderTroopPlacer,
                        "SuspendTroopPlacer",
                        !boundariesReady);
                }
            }

            return boundariesReady;
        }

        private void TryTickCommanderDeploymentFreeCamera(float dt)
        {
            if (_currentScreen != CoopSelectionScreen.CommanderDeployment)
            {
                if (_commanderDeploymentFreeCameraActive || _commanderDeploymentFreeCamera != null)
                    TryDeactivateCommanderDeploymentFreeCamera(MissionScreen, "commander-deployment-screen-left");
                return;
            }

            ScreenBase missionScreen = MissionScreen;
            if (missionScreen == null)
                return;

            if (!_commanderDeploymentFreeCameraActive &&
                !TryActivateCommanderDeploymentFreeCamera(missionScreen, "commander-deployment-camera-tick"))
            {
                return;
            }

            Camera camera = _commanderDeploymentFreeCamera;
            if (camera == null)
                return;

            TryApplyCommanderDeploymentFreeCameraScreenState(missionScreen);

            float safeDt = Math.Max(0f, Math.Min(dt, 0.1f));
            MatrixFrame frame = camera.Frame;
            bool changed = false;

            if (Input.IsKeyDown(InputKey.RightMouseButton))
            {
                float lookX = Input.GetMouseMoveX();
                float lookY = Input.GetMouseMoveY();
                object sceneInput = ResolveMissionSceneInput();
                lookX += TryGetInputGameKeyAxis(sceneInput, "CameraAxisX") * 20f * safeDt;
                lookY += TryGetInputGameKeyAxis(sceneInput, "CameraAxisY") * 20f * safeDt;

                if (Math.Abs(lookX) > 0.001f || Math.Abs(lookY) > 0.001f)
                {
                    _commanderDeploymentFreeCameraYaw += lookX * CommanderDeploymentFreeCameraLookSensitivity;
                    _commanderDeploymentFreeCameraPitch -= lookY * CommanderDeploymentFreeCameraLookSensitivity;
                    _commanderDeploymentFreeCameraPitch = MBMath.ClampFloat(
                        _commanderDeploymentFreeCameraPitch,
                        CommanderDeploymentFreeCameraMinPitch,
                        CommanderDeploymentFreeCameraMaxPitch);

                    MatrixFrame rotatedFrame = MatrixFrame.Identity;
                    rotatedFrame.origin = frame.origin;
                    rotatedFrame.rotation.RotateAboutUp(_commanderDeploymentFreeCameraYaw);
                    rotatedFrame.rotation.RotateAboutSide(_commanderDeploymentFreeCameraPitch);
                    frame = rotatedFrame;
                    changed = true;
                }
            }

            object input = ResolveMissionSceneInput();
            float moveX = TryGetInputGameKeyAxis(input, "MovementAxisX");
            float moveY = TryGetInputGameKeyAxis(input, "MovementAxisY");
            if (Input.IsKeyDown(InputKey.A))
                moveX -= 1f;
            if (Input.IsKeyDown(InputKey.D))
                moveX += 1f;
            if (Input.IsKeyDown(InputKey.W))
                moveY += 1f;
            if (Input.IsKeyDown(InputKey.S))
                moveY -= 1f;

            float moveZ = 0f;
            if (Input.IsKeyDown(InputKey.Space) || Input.IsKeyDown(InputKey.E))
                moveZ += 1f;
            if (Input.IsKeyDown(InputKey.LeftControl) || Input.IsKeyDown(InputKey.RightControl) || Input.IsKeyDown(InputKey.Q))
                moveZ -= 1f;

            Vec3 movement = BuildCommanderDeploymentFreeCameraMovement(frame, moveX, moveY, moveZ);
            if (movement.IsNonZero)
            {
                float moveAmount = movement.Normalize();
                float speed = CommanderDeploymentFreeCameraMoveSpeed;
                if (Input.IsKeyDown(InputKey.LeftShift) || Input.IsKeyDown(InputKey.RightShift))
                    speed *= CommanderDeploymentFreeCameraFastMoveMultiplier;

                frame.origin += movement * (speed * safeDt * Math.Min(1f, moveAmount));
                changed = true;
            }

            if (changed)
                camera.Frame = frame;
        }

        private bool TryActivateCommanderDeploymentFreeCamera(ScreenBase missionScreen, string source)
        {
            if (missionScreen == null)
                return false;

            Camera combatCamera = ResolveMissionScreenCombatCamera();
            if (combatCamera == null)
                return false;

            try
            {
                if (_commanderDeploymentFreeCamera == null)
                    _commanderDeploymentFreeCamera = Camera.CreateCamera();

                if (!_commanderDeploymentFreeCameraActive)
                {
                    _commanderDeploymentFreeCamera.FillParametersFrom(combatCamera);
                    _commanderDeploymentFreeCamera.Frame = combatCamera.Frame;
                    MatrixFrame frame = _commanderDeploymentFreeCamera.Frame;
                    _commanderDeploymentFreeCameraYaw = frame.rotation.f.RotationZ;
                    _commanderDeploymentFreeCameraPitch = MBMath.ClampFloat(
                        frame.rotation.f.RotationX,
                        CommanderDeploymentFreeCameraMinPitch,
                        CommanderDeploymentFreeCameraMaxPitch);
                    TryResetMissionScreenCameraPreviewState(missionScreen);
                    _commanderDeploymentFreeCameraActive = true;
                }

                TryApplyCommanderDeploymentFreeCameraScreenState(missionScreen);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: commander deployment free camera activation failed. " +
                    "Source=" + (source ?? "unknown") +
                    " Error=" + ex.GetType().Name + ":" + ex.Message);
                return false;
            }
        }

        private void TryApplyCommanderDeploymentFreeCameraScreenState(ScreenBase missionScreen)
        {
            if (missionScreen == null || _commanderDeploymentFreeCamera == null)
                return;

            TrySetInstanceProperty(missionScreen, "CustomCamera", _commanderDeploymentFreeCamera);
            TrySetInstanceField(missionScreen, "AllowInputWithCustomCamera", true);
            TryInvokeInstanceMethod(missionScreen, "SetCameraLockState", false);
            TrySetInstanceProperty(missionScreen, "LockCameraMovement", false);
            Camera combatCamera = ResolveMissionScreenCombatCamera();
            combatCamera?.FillParametersFrom(_commanderDeploymentFreeCamera);
            TryResetMissionScreenCameraPreviewState(missionScreen);
        }

        private void TryDeactivateCommanderDeploymentFreeCamera(ScreenBase missionScreen, string source)
        {
            if (!_commanderDeploymentFreeCameraActive && _commanderDeploymentFreeCamera == null)
                return;

            try
            {
                if (missionScreen != null)
                {
                    TrySetInstanceProperty(missionScreen, "CustomCamera", null);
                    TrySetInstanceField(missionScreen, "AllowInputWithCustomCamera", false);
                }

                _commanderDeploymentFreeCamera?.ReleaseCamera();
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: commander deployment free camera release failed. " +
                    "Source=" + (source ?? "unknown") +
                    " Error=" + ex.GetType().Name + ":" + ex.Message);
            }
            finally
            {
                _commanderDeploymentFreeCamera = null;
                _commanderDeploymentFreeCameraActive = false;
                _commanderDeploymentFreeCameraYaw = 0f;
                _commanderDeploymentFreeCameraPitch = 0f;
            }
        }

        private object ResolveMissionSceneInput()
        {
            return TryGetInstancePropertyValue(TryGetInstancePropertyValue(MissionScreen, "SceneLayer"), "Input");
        }

        private static float TryGetInputGameKeyAxis(object inputContext, string axisName)
        {
            object value = TryInvokeInstanceMethodWithResult(inputContext, "GetGameKeyAxis", axisName);
            return value is float floatValue ? floatValue : 0f;
        }

        private static Vec3 BuildCommanderDeploymentFreeCameraMovement(
            MatrixFrame frame,
            float moveX,
            float moveY,
            float moveZ)
        {
            Vec3 forward = frame.rotation.f;
            forward.z = 0f;
            if (forward.LengthSquared > 0.0001f)
                forward.Normalize();
            else
                forward = Vec3.Forward;

            Vec3 side = frame.rotation.s;
            side.z = 0f;
            if (side.LengthSquared > 0.0001f)
                side.Normalize();
            else
                side = Vec3.Side;

            return side * moveX + forward * moveY + Vec3.Up * moveZ;
        }

        private void TryUpdateCommanderDeploymentOrderVmUnchecked()
        {
            if (_commanderDeploymentOrderVm == null)
                return;

            if (!TryGetInstanceBool(_commanderDeploymentOrderVm, "IsToggleOrderShown"))
            {
                _commanderDeploymentOrderVm.Update();
                return;
            }

            try
            {
                TryInvokeInstanceMethodSuccessfully(_commanderDeploymentOrderVm.TroopController, "IntervalUpdate");
                TryInvokeInstanceMethodSuccessfully(_commanderDeploymentOrderVm.TroopController, "Update");
                TryInvokeInstanceMethodSuccessfully(_commanderDeploymentOrderVm.TroopController, "RefreshTroopFormationTargetVisuals");
                TrySetInstanceProperty(
                    _commanderDeploymentOrderVm,
                    "UseAlternativeFormationLayout",
                    TaleWorlds.InputSystem.Input.IsGamepadActive);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: commander deployment unchecked order update failed: " +
                    ex.GetType().Name + ":" + ex.Message);
            }
        }

        private void TryTickCommanderDeploymentOrderHotkeys()
        {
            if (_commanderDeploymentOrderVm == null ||
                !_commanderDeploymentOrderVmInitialized)
            {
                return;
            }

            if (Input.IsKeyPressed(InputKey.BackSpace))
            {
                TryEnsureCommanderDeploymentOrderMovie();
                TryInvokeInstanceMethodSuccessfully(_commanderDeploymentOrderVm, "ViewOrders");
                return;
            }

            int orderHotkeyIndex = GetPressedCommanderDeploymentOrderHotkeyIndex();
            if (orderHotkeyIndex < 0)
                return;

            TryHandleCommanderDeploymentOrderHotkey(orderHotkeyIndex);
        }

        private bool TryHandleCommanderDeploymentOrderHotkey(int orderHotkeyIndex)
        {
            if (orderHotkeyIndex < 0 || !TryEnsureCommanderDeploymentOrderBridge())
                return false;

            TryEnsureCommanderDeploymentOrderMovie();

            try
            {
                object selectedOrderSet = TryGetInstanceMemberValue(_commanderDeploymentOrderVm, "SelectedOrderSet");
                if (selectedOrderSet != null)
                    return TryExecuteCommanderDeploymentSelectedOrderSetHotkey(selectedOrderSet, orderHotkeyIndex);

                bool openInvoked = TryOpenCommanderDeploymentOrderMenuUnchecked();
                if (!openInvoked)
                    return false;

                object orderSets = TryGetInstanceMemberValue(_commanderDeploymentOrderVm, "OrderSets");
                if (orderHotkeyIndex == 8 && OrderSetCollectionContainsReturnOnlySet(orderSets))
                {
                    bool? closeResult = TryInvokeBoolMethod(_commanderDeploymentOrderVm, "TryCloseToggleOrder", false);
                    return closeResult == true;
                }

                object orderSetAtIndex = TryInvokeInstanceMethodWithResult(
                    _commanderDeploymentOrderVm,
                    "GetOrderSetAtIndex",
                    orderHotkeyIndex);
                if (orderSetAtIndex == null || IsReturnOnlyOrderSet(orderSetAtIndex))
                    return false;

                return TrySelectCommanderDeploymentOrderSetUnchecked(orderSetAtIndex);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: commander deployment order hotkey failed: " +
                    ex.GetType().Name + ":" + ex.Message);
                return false;
            }
        }

        private bool TryOpenCommanderDeploymentOrderMenuUnchecked()
        {
            if (!TryEnsureCommanderDeploymentOrderBridge())
                return false;

            if (TryGetInstanceBool(_commanderDeploymentOrderVm, "IsToggleOrderShown"))
                return true;

            Mission mission = Mission;
            OrderController orderController = mission?.PlayerTeam?.PlayerOrderController;
            if (mission == null || orderController == null)
                return false;

            TryEnsureCommanderDeploymentVisualOrderProviderRegistered();
            TryEnsureCommanderDeploymentOrderMovie();

            try
            {
                if (orderController.SelectedFormations == null || orderController.SelectedFormations.Count == 0)
                    orderController.SelectAllFormations();

                TryInvokeInstanceMethodSuccessfully(_commanderDeploymentOrderVm, "PopulateOrderSets");
                _commanderDeploymentOrderVm.UpdateCanUseShortcuts(true);
                mission.IsOrderMenuOpen = true;
                TrySetInstanceProperty(_commanderDeploymentOrderVm, "IsToggleOrderShown", true);
                TryInvokeInstanceMethodSuccessfully(_commanderDeploymentOrderVm.TroopController, "UpdateTroops");
                TrySetInstanceProperty(_commanderDeploymentOrderVm.TroopController, "IsTransferActive", false);
                TryInvokeInstanceMethodSuccessfully(_commanderDeploymentOrderVm.DeploymentController, "ProcessSiegeMachines");
                if (orderController.SelectedFormations == null || orderController.SelectedFormations.Count == 0)
                    TryInvokeInstanceMethodSuccessfully(_commanderDeploymentOrderVm.TroopController, "SelectAllFormations");

                TryInvokeInstanceMethodSuccessfully(_commanderDeploymentOrderVm, "OnOrderShownToggle");
                TryInvokeInstanceMethodSuccessfully(_commanderDeploymentOrderVm, "SetActiveOrders");
                TryInvokeInstanceMethodSuccessfully(_commanderDeploymentOrderVm.TroopController, "UpdateTroops");
                TryRefreshCommanderDeploymentCountsAfterSelection(
                    _commanderDeploymentViewModel,
                    mission,
                    "mission-order-open-unchecked");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: commander deployment unchecked order open failed: " +
                    ex.GetType().Name + ":" + ex.Message);
                return false;
            }
        }

        private bool TrySelectCommanderDeploymentOrderSetUnchecked(object orderSet)
        {
            if (orderSet == null)
                return false;

            try
            {
                VisualOrderExecutionParameters executionParameters = GetCommanderDeploymentVisualOrderExecutionParameters();
                if (!TryInvokeInstanceMethodSuccessfully(orderSet, "ExecuteAction", executionParameters))
                    return false;

                if (!TryGetInstanceBool(_commanderDeploymentOrderVm, "IsToggleOrderShown") &&
                    !IsSoloOrderSet(orderSet))
                {
                    TryOpenCommanderDeploymentOrderMenuUnchecked();
                }

                _commanderDeploymentOrderVm.UpdateCanUseShortcuts(true);
                TryInvokeInstanceMethodSuccessfully(_commanderDeploymentOrderVm, "SetActiveOrders");
                TryInvokeInstanceMethodSuccessfully(_commanderDeploymentOrderVm.TroopController, "UpdateTroops");
                TryRefreshNativeCommanderOrderOfBattleCounts(
                    _commanderDeploymentViewModel,
                    Mission,
                    "mission-order-select-set-unchecked");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: commander deployment unchecked order set select failed: " +
                    ex.GetType().Name + ":" + ex.Message);
                return false;
            }
        }

        private bool TryExecuteCommanderDeploymentSelectedOrderSetHotkey(object selectedOrderSet, int orderHotkeyIndex)
        {
            object orders = TryGetInstanceMemberValue(selectedOrderSet, "Orders");
            int selectedOrderCount = TryGetCollectionCount(orders);
            if (selectedOrderCount <= 0)
                return false;

            if (orderHotkeyIndex == 8 && OrderItemCollectionContainsReturnVisualOrder(orders))
                return TryInvokeInstanceMethodSuccessfully(selectedOrderSet, "ExecuteDeSelect");

            if (orderHotkeyIndex >= selectedOrderCount)
                return false;

            object orderItem = TryGetCollectionItem(orders, orderHotkeyIndex);
            object visualOrder = TryGetInstanceMemberValue(orderItem, "Order");
            if (IsReturnVisualOrderInstance(visualOrder))
                return TryInvokeInstanceMethodSuccessfully(selectedOrderSet, "ExecuteDeSelect");

            VisualOrderExecutionParameters executionParameters = GetCommanderDeploymentVisualOrderExecutionParameters();
            if (orderItem == null ||
                !TryInvokeInstanceMethodSuccessfully(orderItem, "ExecuteAction", executionParameters))
            {
                return false;
            }

            TryInvokeInstanceMethodSuccessfully(selectedOrderSet, "ExecuteDeSelect");
            TryInvokeInstanceMethodSuccessfully(_commanderDeploymentOrderVm.TroopController, "UpdateTroops");
            TryRefreshNativeCommanderOrderOfBattleCounts(
                _commanderDeploymentViewModel,
                Mission,
                "mission-order-hotkey");
            return true;
        }

        private void TryDeselectCommanderDeploymentSelectedOrderSet()
        {
            object selectedOrderSet = TryGetInstanceMemberValue(_commanderDeploymentOrderVm, "SelectedOrderSet");
            if (selectedOrderSet != null)
                TryInvokeInstanceMethodSuccessfully(selectedOrderSet, "ExecuteDeSelect");
        }

        private static int GetPressedCommanderDeploymentOrderHotkeyIndex()
        {
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(InputKey.F1))
                return 0;
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(InputKey.F2))
                return 1;
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(InputKey.F3))
                return 2;
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(InputKey.F4))
                return 3;
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(InputKey.F5))
                return 4;
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(InputKey.F6))
                return 5;
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(InputKey.F7))
                return 6;
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(InputKey.F8))
                return 7;
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(InputKey.F9))
                return 8;

            return -1;
        }

        private static int GetPressedCommanderBattleFormationHotkeyIndex()
        {
            if (TaleWorlds.InputSystem.Input.IsKeyPressed(InputKey.D1) ||
                TaleWorlds.InputSystem.Input.IsKeyPressed(InputKey.Numpad1))
            {
                return 0;
            }

            if (TaleWorlds.InputSystem.Input.IsKeyPressed(InputKey.D2) ||
                TaleWorlds.InputSystem.Input.IsKeyPressed(InputKey.Numpad2))
            {
                return 1;
            }

            if (TaleWorlds.InputSystem.Input.IsKeyPressed(InputKey.D3) ||
                TaleWorlds.InputSystem.Input.IsKeyPressed(InputKey.Numpad3))
            {
                return 2;
            }

            if (TaleWorlds.InputSystem.Input.IsKeyPressed(InputKey.D4) ||
                TaleWorlds.InputSystem.Input.IsKeyPressed(InputKey.Numpad4))
            {
                return 3;
            }

            if (TaleWorlds.InputSystem.Input.IsKeyPressed(InputKey.D5) ||
                TaleWorlds.InputSystem.Input.IsKeyPressed(InputKey.Numpad5))
            {
                return 4;
            }

            if (TaleWorlds.InputSystem.Input.IsKeyPressed(InputKey.D6) ||
                TaleWorlds.InputSystem.Input.IsKeyPressed(InputKey.Numpad6))
            {
                return 5;
            }

            if (TaleWorlds.InputSystem.Input.IsKeyPressed(InputKey.D7) ||
                TaleWorlds.InputSystem.Input.IsKeyPressed(InputKey.Numpad7))
            {
                return 6;
            }

            if (TaleWorlds.InputSystem.Input.IsKeyPressed(InputKey.D8) ||
                TaleWorlds.InputSystem.Input.IsKeyPressed(InputKey.Numpad8))
            {
                return 7;
            }

            return -1;
        }

        private void UpdateOverlayInputState(bool shouldCaptureInput)
        {
            if (_gauntletLayer == null)
                return;

            bool isCommanderDeploymentInputMode =
                shouldCaptureInput &&
                _currentScreen == CoopSelectionScreen.CommanderDeployment;
            if (shouldCaptureInput == _inputCaptured &&
                (!shouldCaptureInput || _inputCapturedCommanderDeploymentMode == isCommanderDeploymentInputMode))
            {
                return;
            }

            ScreenBase missionScreen = MissionScreen;
            if (shouldCaptureInput)
            {
                TrySetLayerActiveState(_gauntletLayer, true);
                if (isCommanderDeploymentInputMode)
                {
                    bool placementBridgeActive = TryActivateNativeCommanderDeploymentPlacement(missionScreen);
                    _commanderDeploymentPlacementInputActive = placementBridgeActive;
                    if (placementBridgeActive)
                    {
                        TryLoseScreenManagerFocus(_gauntletLayer);
                        TryInvokeLayerFocusCallback(_gauntletLayer, "HandleLoseFocus");
                        _gauntletLayer.IsFocusLayer = false;
                    }
                    else
                    {
                        _gauntletLayer.IsFocusLayer = true;
                        TrySetScreenManagerFocus(_gauntletLayer);
                        TryInvokeLayerFocusCallback(_gauntletLayer, "HandleGainFocus");
                    }

                    _gauntletLayer.InputRestrictions.SetInputRestrictions(true, CommanderDeploymentInputMask);
                    _gauntletLayer.InputRestrictions.SetMouseVisibility(true);
                    TrySetScreenManagerMouseVisibility(true);
                    if (missionScreen != null)
                    {
                        missionScreen.MouseVisible = true;
                        if (placementBridgeActive)
                            ApplyMissionScreenCommanderPlacementMode(missionScreen);
                        else
                            ApplyMissionScreenOverlayMode(missionScreen, isOverlayActive: true);
                        LogMissionScreenOverlayDiagnostics(missionScreen, "commander-deployment-capture");
                    }

                    _inputCaptured = true;
                    _inputCapturedCommanderDeploymentMode = true;
                    return;
                }

                _commanderDeploymentPlacementInputActive = false;
                _gauntletLayer.IsFocusLayer = true;
                TrySetScreenManagerFocus(_gauntletLayer);
                TryInvokeLayerFocusCallback(_gauntletLayer, "HandleGainFocus");
                _gauntletLayer.InputRestrictions.SetInputRestrictions(true, InputUsageMask.All);
                _gauntletLayer.InputRestrictions.SetMouseVisibility(true);
                TrySetScreenManagerMouseVisibility(true);
                if (missionScreen != null)
                {
                    missionScreen.MouseVisible = true;
                    ApplyMissionScreenOverlayMode(missionScreen, isOverlayActive: true);
                    LogMissionScreenOverlayDiagnostics(missionScreen, "capture");
                }

                _inputCaptured = true;
                _inputCapturedCommanderDeploymentMode = false;
                return;
            }

            ReleaseOverlayInput();
        }

        private void ReleaseOverlayInput()
        {
            if (_gauntletLayer == null && !_inputCaptured)
                return;

            bool wasCommanderDeploymentInputMode = _inputCapturedCommanderDeploymentMode;
            try
            {
                if (_gauntletLayer != null)
                {
                    TryLoseScreenManagerFocus(_gauntletLayer);
                    TryInvokeLayerFocusCallback(_gauntletLayer, "HandleLoseFocus");
                    _gauntletLayer.InputRestrictions.ResetInputRestrictions();
                    _gauntletLayer.InputRestrictions.SetMouseVisibility(false);
                    _gauntletLayer.IsFocusLayer = false;
                    TrySetLayerActiveState(_gauntletLayer, false);
                }

                TrySetScreenManagerMouseVisibility(false);
                ScreenBase missionScreen = MissionScreen;
                if (missionScreen != null)
                {
                    if (wasCommanderDeploymentInputMode)
                    {
                        TryDeactivateNativeCommanderDeploymentPlacement(missionScreen);
                        TryDeactivateCommanderDeploymentFreeCamera(missionScreen, "release-overlay-input");
                    }

                    missionScreen.MouseVisible = false;
                    ApplyMissionScreenOverlayMode(missionScreen, isOverlayActive: false);
                    LogMissionScreenOverlayDiagnostics(missionScreen, "release");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: failed to restore mission input state: " + ex.Message);
            }
            finally
            {
                _commanderDeploymentPlacementInputActive = false;
                _inputCaptured = false;
                _inputCapturedCommanderDeploymentMode = false;
            }
        }

        private void CleanupLayerState()
        {
            try
            {
                ReleaseOverlayInput();
                ReleaseCommanderBattleOrderBridge("cleanup-layer-state");
                ReleaseCurrentMovie();
                ReleaseAiControlHintLayer();

                if (_gauntletLayer != null)
                {
                    MissionScreen?.RemoveLayer(_gauntletLayer);
                    _gauntletLayer = null;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: cleanup after failed init also failed: " + ex.Message);
            }
        }

        private void ReleaseCurrentMovie()
        {
            if (_landBattleManualFormationPlacementActive)
            {
                ExactCampaignCommanderDeploymentRuntime.EndManualFormationPlacement(
                    Mission,
                    "release-current-movie");
                _landBattleManualFormationPlacementActive = false;
            }

            bool hadPresentation =
                _movie != null ||
                _viewModel != null ||
                _screenViewModel != null ||
                _currentScreen != CoopSelectionScreen.None;
            bool hadCommanderDeploymentPresentation =
                _currentScreen == CoopSelectionScreen.CommanderDeployment ||
                _commanderDeploymentViewModel != null ||
                _commanderDeploymentOrderVm != null ||
                _commanderSiegeMachineDeploymentMovie != null ||
                _commanderSiegeMachineDeploymentVm != null ||
                _commanderDeploymentOrderTroopPlacer != null ||
                _commanderDeploymentOrderOfBattleSpriteCategory != null ||
                _commanderDeploymentOrderSpriteCategory != null;
            if (_gauntletLayer != null && _movie != null)
            {
                _gauntletLayer.ReleaseMovie(_movie);
                _movie = null;
            }

            if (hadCommanderDeploymentPresentation)
            {
                CoopSiegeDeploymentBoundaryRuntime.TryRemoveVisibleDeploymentBoundaryMarkers(
                    Mission,
                    MissionScreen,
                    "release-current-movie");
                TryDeactivateCommanderDeploymentFreeCamera(MissionScreen, "release-current-movie");
                TryDeactivateNativeCommanderDeploymentPlacement(MissionScreen);
                ReleaseCommanderSiegeMachineDeploymentMovie();
                ReleaseCommanderDeploymentOrderBridge();
                ReleaseCommanderDeploymentOrderTroopPlacerCallback();
                ReleaseCommanderDeploymentSpriteCategory();
            }

            _viewModel?.OnFinalize();
            _viewModel = null;
            _commanderDeploymentViewModel = null;
            _screenViewModel = null;
            _currentScreen = CoopSelectionScreen.None;
            _commanderDeploymentOrderOfBattleActive = false;
            _commanderDeploymentBoundaryRefreshTimer = 0f;
            _commanderSiegeMachineDeploymentRetryTimer = 0f;
            _commanderSiegeMachineDeploymentNoTargetsLogged = false;
            _commanderSiegeMachineDeploymentFailureLogged = false;
            _lastAppliedRefreshKey = string.Empty;
            if (hadPresentation)
                ClearCameraPreviewTarget("release-movie");
        }

        private void UpdateCameraPreviewTarget(
            CoopSelectionUiSnapshot snapshot,
            CoopSelectionScreen desiredScreen,
            bool hasLocalControlledAgent)
        {
            if (!GameNetwork.IsClient ||
                hasLocalControlledAgent ||
                snapshot?.ShouldSuppressLivePreview == true ||
                desiredScreen != CoopSelectionScreen.ClassLoadout ||
                snapshot == null ||
                snapshot.EffectiveSide == BattleSideEnum.None ||
                string.IsNullOrWhiteSpace(snapshot.SelectedEntryId))
            {
                ClearCameraPreviewTarget("camera-preview-not-applicable");
                return;
            }

            bool resolvedExactPreviewAgent = TryResolveCameraPreviewAgent(snapshot, out Agent previewAgent);
            if (!resolvedExactPreviewAgent &&
                !TryResolveCameraPreviewRepresentative(snapshot, out previewAgent))
            {
                ClearCameraPreviewTarget("camera-preview-target-missing");
                return;
            }

            ScreenBase missionScreen = MissionScreen;
            if (missionScreen == null)
            {
                ClearCameraPreviewTarget("camera-preview-screen-unavailable");
                return;
            }

            SetActiveCameraPreviewTarget(previewAgent, snapshot.SelectedEntryId);
            if (!TrySetMissionScreenPreviewFollowTarget(missionScreen, previewAgent))
            {
                ClearCameraPreviewTarget("camera-preview-screen-unavailable");
                return;
            }

            LogCameraPreviewState(
                "focus:" + previewAgent.Index + ":" + snapshot.SelectedEntryId,
                "focused camera preview on selected unit representative. " +
                "AgentIndex=" + previewAgent.Index +
                " EntryId=" + snapshot.SelectedEntryId +
                " Exact=" + resolvedExactPreviewAgent +
                " Side=" + snapshot.EffectiveSide);
        }

        private bool TryResolveCameraPreviewAgent(CoopSelectionUiSnapshot snapshot, out Agent previewAgent)
        {
            if (snapshot == null ||
                snapshot.EffectiveSide == BattleSideEnum.None ||
                string.IsNullOrWhiteSpace(snapshot.SelectedEntryId))
            {
                previewAgent = null;
                return false;
            }

            return TryResolveCameraPreviewAgentForEntry(
                snapshot.EffectiveSide,
                snapshot.SelectedEntryId,
                out previewAgent);
        }

        private bool TryResolveCameraPreviewAgentForEntry(
            BattleSideEnum side,
            string entryId,
            out Agent previewAgent)
        {
            previewAgent = null;
            Mission mission = Mission;
            if (mission?.AllAgents == null ||
                side == BattleSideEnum.None ||
                string.IsNullOrWhiteSpace(entryId))
            {
                return false;
            }

            for (int agentIndex = 0; agentIndex < mission.AllAgents.Count; agentIndex++)
            {
                Agent candidate = mission.AllAgents[agentIndex];
                if (!IsCameraPreviewCandidate(candidate, side))
                    continue;

                if (!CoopMissionSpawnLogic.TryResolveSelectableEntryId(candidate, out string candidateEntryId) ||
                    !string.Equals(candidateEntryId, entryId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                previewAgent = candidate;
                return true;
            }

            return false;
        }

        private static bool IsCameraPreviewCandidate(Agent candidate, BattleSideEnum side)
        {
            return candidate != null &&
                   candidate.IsActive() &&
                   !candidate.IsMount &&
                   candidate.IsCameraAttachable() &&
                   candidate.Team?.Side == side;
        }

        private void ClearCameraPreviewTarget(string source)
        {
            bool hadActivePreviewTarget =
                _activeCameraPreviewAgentIndex >= 0 ||
                !string.IsNullOrWhiteSpace(_activeCameraPreviewEntryId);
            if (!hadActivePreviewTarget)
                return;

            ScreenBase missionScreen = MissionScreen;
            if (missionScreen != null)
                TryResetMissionScreenCameraPreviewState(missionScreen);

            ClearActiveCameraPreviewTarget();

            LogCameraPreviewState(
                "clear:" + (source ?? "unknown"),
                "cleared local camera preview target. Source=" + (source ?? "unknown"));
        }

        internal static bool TryGetActiveCameraPreviewAgent(out Agent previewAgent)
        {
            previewAgent = null;
            int activeAgentIndex = _activeCameraPreviewAgentIndex;
            if (activeAgentIndex < 0)
                return false;

            Mission mission = Mission.Current;
            if (mission?.AllAgents == null)
                return false;

            for (int agentIndex = 0; agentIndex < mission.AllAgents.Count; agentIndex++)
            {
                Agent candidate = mission.AllAgents[agentIndex];
                if (candidate == null || candidate.Index != activeAgentIndex)
                    continue;

                if (!candidate.IsActive() || candidate.IsMount || !candidate.IsCameraAttachable())
                    return false;

                previewAgent = candidate;
                return true;
            }

            return false;
        }

        private bool TryResolveCameraPreviewRepresentative(
            CoopSelectionUiSnapshot snapshot,
            out Agent previewAgent)
        {
            previewAgent = null;
            Mission mission = Mission;
            RosterEntryState selectedEntryState = BattleSnapshotRuntimeState.GetEntryState(snapshot?.SelectedEntryId);
            if (selectedEntryState == null ||
                mission?.AllAgents == null ||
                snapshot.EffectiveSide == BattleSideEnum.None)
            {
                return false;
            }

            int bestScore = int.MinValue;
            for (int agentIndex = 0; agentIndex < mission.AllAgents.Count; agentIndex++)
            {
                Agent candidate = mission.AllAgents[agentIndex];
                if (!IsCameraPreviewCandidate(candidate, snapshot.EffectiveSide) ||
                    candidate.Controller == AgentControllerType.Player)
                {
                    continue;
                }

                RosterEntryState candidateEntryState = null;
                if (CoopMissionSpawnLogic.TryResolveSelectableEntryId(candidate, out string candidateEntryId))
                    candidateEntryState = BattleSnapshotRuntimeState.GetEntryState(candidateEntryId);

                int candidateScore = ScoreCameraPreviewRepresentative(
                    selectedEntryState,
                    candidateEntryState,
                    candidate.Character?.StringId);
                if (candidateScore <= 0 ||
                    (candidateScore == bestScore &&
                     previewAgent != null &&
                     candidate.Index >= previewAgent.Index))
                {
                    continue;
                }

                bestScore = candidateScore;
                previewAgent = candidate;
            }

            return previewAgent != null;
        }

        private static int ScoreCameraPreviewRepresentative(
            RosterEntryState selectedEntryState,
            RosterEntryState candidateEntryState,
            string candidateCharacterId)
        {
            if (selectedEntryState == null)
                return 0;

            int score = 0;
            if (candidateEntryState != null)
            {
                if (AreNonEmptyIdsEqual(selectedEntryState.OriginalCharacterId, candidateEntryState.OriginalCharacterId))
                    score += 1000;
                if (AreNonEmptyIdsEqual(selectedEntryState.SpawnTemplateId, candidateEntryState.SpawnTemplateId))
                    score += 500;
                if (AreNonEmptyIdsEqual(selectedEntryState.CharacterId, candidateEntryState.CharacterId))
                    score += 450;
                if (AreNonEmptyIdsEqual(selectedEntryState.CampaignFormationClass, candidateEntryState.CampaignFormationClass))
                    score += 160;
                if (selectedEntryState.IsMounted == candidateEntryState.IsMounted)
                    score += 40;
                if (selectedEntryState.IsRanged == candidateEntryState.IsRanged)
                    score += 40;
                if (selectedEntryState.HasShield == candidateEntryState.HasShield)
                    score += 10;
                if (selectedEntryState.HasThrown == candidateEntryState.HasThrown)
                    score += 10;
            }

            if (AreNonEmptyIdsEqual(selectedEntryState.SpawnTemplateId, candidateCharacterId))
                score += 500;
            if (AreNonEmptyIdsEqual(selectedEntryState.CharacterId, candidateCharacterId))
                score += 450;

            return score;
        }

        private static bool AreNonEmptyIdsEqual(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left) &&
                   !string.IsNullOrWhiteSpace(right) &&
                   string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsCommanderDeploymentPlacementInputActive()
        {
            return _commanderDeploymentPlacementInputActive;
        }

        internal static bool IsCommanderDeploymentOrderOfBattleActive()
        {
            if (!_commanderDeploymentOrderOfBattleActive && !_commanderDeploymentPlacementInputActive)
                return false;

            Mission mission = Mission.Current;
            if (mission == null)
                return false;

            return true;
        }

        internal static bool IsCommanderDeploymentSiegeProjectionActive()
        {
            if (!IsCommanderDeploymentOrderOfBattleActive())
                return false;

            Mission mission = Mission.Current;
            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetScenarioContext() ??
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            if (!ExactCampaignSiegeAssaultWithDeploymentRuntime
                    .IsExactSiegeWithDeploymentScenario(scenarioContext))
            {
                return false;
            }

            BattleSideEnum side = mission?.PlayerTeam?.Side ?? BattleSideEnum.None;
            return !ShouldUseMountedCommanderDeploymentFormationClasses(
                mission,
                scenarioContext,
                side);
        }

        internal static bool IsCommanderDeploymentMountedFormationScenarioActive()
        {
            if (!IsCommanderDeploymentOrderOfBattleActive())
                return false;

            Mission mission = Mission.Current;
            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetScenarioContext() ??
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            BattleSideEnum side = mission?.PlayerTeam?.Side ?? BattleSideEnum.None;
            return ShouldUseMountedCommanderDeploymentFormationClasses(
                mission,
                scenarioContext,
                side);
        }

        private static bool ShouldUseMountedCommanderDeploymentFormationClasses(
            Mission mission,
            BattleScenarioContextMessage scenarioContext,
            BattleSideEnum side)
        {
            return ExactCampaignCommanderDeploymentRuntime
                .ShouldPreserveMountedFormationClasses(
                    mission,
                    scenarioContext,
                    side);
        }

        internal static bool IsCommanderBattleOrderActive()
        {
            if (!_commanderBattleOrderActive)
                return false;

            Mission mission = Mission.Current;
            if (mission == null)
                return false;

            return GameNetwork.IsClient &&
                   GameNetwork.IsSessionActive &&
                   MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName) &&
                   IsCurrentCommanderDeploymentScenario(mission);
        }

        private static bool IsCurrentCommanderDeploymentScenario(Mission mission)
        {
            if (mission == null ||
                !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
            {
                return false;
            }

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetScenarioContext() ??
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            return ExactCampaignCommanderDeploymentRuntime
                .IsCommanderDeploymentScenario(mission, scenarioContext);
        }

        private static bool IsCurrentSiegeAmbushCommanderDeploymentScenario(Mission mission)
        {
            if (mission == null ||
                !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
            {
                return false;
            }

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetScenarioContext() ??
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            return SiegeAmbushScenarioContract.IsSiegeAmbushScenario(scenarioContext);
        }

        private static bool IsCurrentExactSallyOutCommanderDeploymentScenario(Mission mission)
        {
            if (mission == null ||
                !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
            {
                return false;
            }

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetScenarioContext() ??
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            return ExactCampaignCommanderDeploymentRuntime.IsExactSallyOutScenario(
                mission,
                scenarioContext);
        }

        private static bool IsCurrentExactFieldBattleCommanderDeploymentScenario(Mission mission)
        {
            if (mission == null ||
                !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
            {
                return false;
            }

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetScenarioContext() ??
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            return ExactCampaignCommanderDeploymentRuntime.IsExactFieldBattleScenario(
                mission,
                scenarioContext);
        }

        private static bool IsCurrentExactVillageBattleCommanderDeploymentScenario(Mission mission)
        {
            if (mission == null ||
                !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
            {
                return false;
            }

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetScenarioContext() ??
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            return ExactCampaignCommanderDeploymentRuntime.IsExactVillageBattleScenario(
                mission,
                scenarioContext);
        }

        private static bool RequiresSynchronizedFinalFormationLayouts(Mission mission)
        {
            if (IsCurrentSiegeAmbushCommanderDeploymentScenario(mission))
                return true;

            if (mission == null ||
                !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
            {
                return false;
            }

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetScenarioContext() ??
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            return ExactCampaignCommanderDeploymentRuntime
                       .IsExactSallyOutScenario(mission, scenarioContext) ||
                   ExactCampaignCommanderDeploymentRuntime
                       .IsExactVillageBattleScenario(mission, scenarioContext) ||
                   ExactCampaignCommanderDeploymentRuntime
                       .IsExactFieldBattleScenario(mission, scenarioContext);
        }

        private static bool IsCurrentExactLandBattleCommanderDeploymentScenario(Mission mission)
        {
            if (mission == null ||
                !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
            {
                return false;
            }

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetScenarioContext() ??
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            return ExactCampaignCommanderDeploymentRuntime
                .IsExactLandBattleScenario(mission, scenarioContext);
        }

        internal static bool ShouldSuppressLocalPreviewFollowedAgentEcho(MissionPeer missionPeer, Agent followedAgent)
        {
            if (!GameNetwork.IsClient ||
                !GameNetwork.IsSessionActive ||
                missionPeer == null)
            {
                return false;
            }

            MissionPeer localMissionPeer = GameNetwork.MyPeer?.GetComponent<MissionPeer>();
            if (localMissionPeer == null || !ReferenceEquals(localMissionPeer, missionPeer))
                return false;

            if (followedAgent == null)
                return _activeCameraPreviewAgentIndex >= 0 || _activeAiObservationAgentIndex >= 0;

            if (!followedAgent.IsActive())
                return false;

            return TryGetActiveCameraFollowAgent(out Agent previewAgent) &&
                   previewAgent != null &&
                   previewAgent.Index == followedAgent.Index;
        }

        internal static bool TryGetActiveCameraFollowAgent(out Agent agent)
        {
            if (TryGetActiveCameraPreviewAgent(out agent))
                return true;

            agent = null;
            Mission mission = Mission.Current;
            if (_activeAiObservationAgentIndex < 0 || mission == null)
                return false;

            if (!CoopBattleAgentControlRuntimeState.TryGetActiveClientObservedAgent(mission, out Agent observedAgent) ||
                observedAgent.Index != _activeAiObservationAgentIndex ||
                !observedAgent.IsCameraAttachable())
            {
                return false;
            }

            agent = observedAgent;
            return true;
        }

        internal static bool IsAiControlObservationActive()
        {
            return _activeAiObservationAgentIndex >= 0 &&
                   CoopBattleAgentControlRuntimeState.IsClientAiObserved();
        }

        private static void SetActiveCameraPreviewTarget(Agent previewAgent, string entryId)
        {
            _activeCameraPreviewAgentIndex = previewAgent?.Index ?? -1;
            _activeCameraPreviewEntryId = entryId ?? string.Empty;
        }

        private static void ClearActiveCameraPreviewTarget()
        {
            _activeCameraPreviewAgentIndex = -1;
            _activeCameraPreviewEntryId = string.Empty;
        }

        private static bool TrySetMissionScreenPreviewFollowTarget(ScreenBase missionScreen, Agent agent)
        {
            if (missionScreen == null || agent == null)
                return false;

            bool lastFollowedAgentSet = TrySetMissionScreenLastFollowedAgent(missionScreen, agent);
            if (lastFollowedAgentSet)
                TrySetInstanceProperty(missionScreen, "LastFollowedAgentVisuals", null);

            bool agentToFollowOverrideSet = TrySetMissionScreenAgentToFollowOverride(missionScreen, agent);
            return lastFollowedAgentSet || agentToFollowOverrideSet;
        }

        private static bool TrySetMissionScreenLastFollowedAgent(ScreenBase missionScreen, Agent agent)
        {
            if (missionScreen == null)
                return false;

            try
            {
                PropertyInfo property = missionScreen.GetType().GetProperty(
                    "LastFollowedAgent",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo setter = property?.GetSetMethod(true);
                if (setter == null)
                    return false;

                setter.Invoke(missionScreen, new object[] { agent });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TrySetMissionScreenAgentToFollowOverride(ScreenBase missionScreen, Agent agent)
        {
            if (missionScreen == null)
                return false;

            try
            {
                MethodInfo method = missionScreen.GetType().GetMethod(
                    "SetAgentToFollow",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(Agent) },
                    null);
                if (method == null)
                    return false;

                method.Invoke(missionScreen, new object[] { agent });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void TryResetMissionScreenCameraPreviewState(ScreenBase missionScreen)
        {
            if (missionScreen == null)
                return;

            TrySetMissionScreenLastFollowedAgent(missionScreen, null);
            TrySetMissionScreenAgentToFollowOverride(missionScreen, null);
            TrySetInstanceField(missionScreen, "_agentToFollowOverride", null);
            TrySetInstanceField(missionScreen, "_lastFollowedAgent", null);
            TrySetInstanceProperty(missionScreen, "LastFollowedAgentVisuals", null);
        }

        private void LogAiControlCameraRestoreDiagnostics(string stage)
        {
            if (!CoopDebugConfig.CombatModelDiagnostics)
                return;

            try
            {
                MissionPeer localMissionPeer = GameNetwork.MyPeer?.GetComponent<MissionPeer>();
                Agent lastFollowedAgent = TryGetInstancePropertyValue(MissionScreen, "LastFollowedAgent") as Agent;
                object customCamera = TryGetInstancePropertyValue(MissionScreen, "CustomCamera");
                ModLogger.Info(
                    "CoopMissionSelectionView: AI-control camera restore diagnostics. " +
                    "Stage=" + (stage ?? "unknown") +
                    " ObservedAgentIndex=" + _activeAiObservationAgentIndex +
                    " MainAgentIndex=" + (Mission?.MainAgent?.Index.ToString() ?? "null") +
                    " MainAgentPosition=" + (Mission?.MainAgent?.Position.ToString() ?? "null") +
                    " MainAgentServerIndex=" + (Mission?.MainAgentServer?.Index.ToString() ?? "null") +
                    " LastFollowedAgentIndex=" + (lastFollowedAgent?.Index.ToString() ?? "null") +
                    " LastFollowedAgentPosition=" + (lastFollowedAgent?.Position.ToString() ?? "null") +
                    " PeerFollowedAgentIndex=" + (localMissionPeer?.FollowedAgent?.Index.ToString() ?? "null") +
                    " PeerControlledAgentIndex=" + (localMissionPeer?.ControlledAgent?.Index.ToString() ?? "null") +
                    " CustomCamera=" + (customCamera == null ? "null" : customCamera.GetType().Name));
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopMissionSelectionView: AI-control camera restore diagnostics failed. " +
                    "Stage=" + (stage ?? "unknown") +
                    " Error=" + ex.GetType().Name + ":" + ex.Message);
            }
        }

        private void LogCameraPreviewState(string key, string message)
        {
            if (string.IsNullOrWhiteSpace(key) ||
                string.Equals(_lastCameraPreviewLogKey, key, StringComparison.Ordinal))
            {
                return;
            }

            _lastCameraPreviewLogKey = key;
            ModLogger.Info("CoopMissionSelectionView: " + message);
        }

        private static string GetRefreshKey(CoopSelectionUiSnapshot snapshot, CoopSelectionScreen desiredScreen)
        {
            if (snapshot == null)
            {
                if (desiredScreen == CoopSelectionScreen.TeamSelection)
                    return "team|null";

                return desiredScreen == CoopSelectionScreen.CommanderDeployment
                    ? "commander|null"
                    : "class|null";
            }

            if (desiredScreen == CoopSelectionScreen.TeamSelection)
                return snapshot.TeamRefreshKey ?? string.Empty;

            if (desiredScreen == CoopSelectionScreen.CommanderDeployment)
            {
                return string.Join("\n", new[]
                {
                    "commander",
                    snapshot.ClassRefreshKey ?? string.Empty,
                    snapshot.BattlePhase ?? string.Empty,
                    snapshot.Lifecycle ?? string.Empty
                });
            }

            return snapshot.ClassRefreshKey ?? string.Empty;
        }

        private bool TryHandleAiControlHotkey(float dt)
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return false;

            _agentControlHotkeyCooldown -= dt;
            if (!CoopBattleAgentControlRuntimeState.TryGetClientState(out CoopBattleAgentControlState state) ||
                !state.IsAiObserved ||
                !Input.IsKeyPressed(InputKey.H))
            {
                return false;
            }

            if (_agentControlHotkeyCooldown > 0f ||
                CoopBattleAgentControlRuntimeState.IsClientReclaimTransitionPending())
            {
                return true;
            }

            _agentControlHotkeyCooldown = ReopenSelectionHotkeyCooldownSeconds;
            if (CoopBattleNetworkRequestTransport.TryReclaimAiObservedAgent(
                    state.AgentIndex,
                    "AI observation H hotkey"))
            {
                InformationManager.DisplayMessage(
                    new InformationMessage("Coop Battle: returning control..."));
            }

            return true;
        }

        private void TryTickAiControlObservationPresentation()
        {
            Agent observedAgent = null;
            bool hasActiveObservedAgent =
                CoopBattleAgentControlRuntimeState.IsClientAiObserved() &&
                CoopBattleAgentControlRuntimeState.TryGetActiveClientObservedAgent(Mission, out observedAgent) &&
                observedAgent != null &&
                observedAgent.IsCameraAttachable();

            if (hasActiveObservedAgent)
            {
                bool targetChanged = _activeAiObservationAgentIndex != observedAgent.Index;
                if (!_wasAiObservationActive || targetChanged)
                {
                    ReleaseOverlayInput();
                    ReleaseCurrentMovie();
                    _activeAiObservationAgentIndex = observedAgent.Index;

                    try
                    {
                        if (Mission != null)
                        {
                            Mission.MainAgent = null;
                            Mission.MainAgentServer = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        ModLogger.Info("CoopMissionSelectionView: failed to release local main agent for AI camera: " + ex.Message);
                    }

                    TrySetMissionScreenPreviewFollowTarget(MissionScreen, observedAgent);
                    if (!_wasAiObservationActive)
                    {
                        InformationManager.DisplayMessage(
                            new InformationMessage("Coop Battle: AI has control. Press H to return control."));
                    }
                }

                TryEnsureAiControlHintLayer();
                _aiControlHintVm?.Update(observedAgent, ResolveMissionScreenCombatCamera());
                _wasAiObservationActive = true;
                return;
            }

            _aiControlHintVm?.Hide();
            if (_wasAiObservationActive)
            {
                bool playerControlRestored = false;
                if (CoopBattleAgentControlRuntimeState.TryGetClientState(out CoopBattleAgentControlState state) &&
                    state.Mode == CoopBattleAgentControlMode.PlayerControlled &&
                    state.AgentIndex >= 0)
                {
                    playerControlRestored = CoopMissionNetworkBridge.TrySynchronizeClientMainAgentWithControlState(
                        Mission,
                        state.Mode,
                        state.AgentIndex,
                        out _);
                    if (!playerControlRestored &&
                        CoopBattleAgentControlRuntimeState.TryResolveAgent(
                            Mission,
                            state.AgentIndex,
                            requireActive: true,
                            out _))
                    {
                        return;
                    }
                }

                if (playerControlRestored)
                {
                    LogAiControlCameraRestoreDiagnostics("before-reset");
                    TryResetMissionScreenCameraPreviewState(MissionScreen);
                    LogAiControlCameraRestoreDiagnostics("after-reset");
                    InformationManager.DisplayMessage(
                        new InformationMessage("Coop Battle: control returned."));
                }
                else
                {
                    TryResetMissionScreenCameraPreviewState(MissionScreen);
                }
            }

            _activeAiObservationAgentIndex = -1;
            _wasAiObservationActive = false;
        }

        private void TryEnsureAiControlHintLayer()
        {
            if (_aiControlHintLayer != null || MissionScreen == null)
                return;

            try
            {
                _aiControlHintLayer = new GauntletLayer("CoopAiControlHintLayer", ViewOrderPriority + 1, false)
                {
                    IsFocusLayer = false
                };
                MissionScreen.AddLayer(_aiControlHintLayer);
                _aiControlHintVm = new CoopBattleAiControlHintVM();
                _aiControlHintMovie = _aiControlHintLayer.LoadMovie(AiControlHintMovieName, _aiControlHintVm);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: failed to initialize AI control hint layer: " + ex.Message);
                ReleaseAiControlHintLayer();
            }
        }

        private void ReleaseAiControlHintLayer()
        {
            try
            {
                _aiControlHintVm?.Hide();
                if (_aiControlHintLayer != null && _aiControlHintMovie != null)
                    _aiControlHintLayer.ReleaseMovie(_aiControlHintMovie);

                _aiControlHintMovie = null;
                _aiControlHintVm?.OnFinalize();
                _aiControlHintVm = null;
                if (_aiControlHintLayer != null)
                {
                    MissionScreen?.RemoveLayer(_aiControlHintLayer);
                    _aiControlHintLayer = null;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: failed to release AI control hint layer: " + ex.Message);
                _aiControlHintMovie = null;
                _aiControlHintVm = null;
                _aiControlHintLayer = null;
            }
        }

        private void TryHandleStartBattleHotkey(float dt, bool hasLocalControlledAgent)
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return;

            if (CoopBattleAgentControlRuntimeState.IsClientAiObservationOrTransitionActive())
                return;

            _startBattleHotkeyCooldown -= dt;
            if (_startBattleHotkeyCooldown > 0f || !Input.IsKeyPressed(InputKey.H))
                return;

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot = CoopBattleEntryStatusBridgeFile.ReadStatus();
            bool isAutoDeploymentRequest =
                !hasLocalControlledAgent &&
                _localSpawnPending &&
                _localSpawnPendingWaitsForDeployment;
            if (!hasLocalControlledAgent && !isAutoDeploymentRequest)
                return;

            bool canStartBattleNow = snapshot != null && snapshot.CanStartBattle;
            if (!canStartBattleNow)
            {
                _startBattleHotkeyCooldown = StartBattleHotkeyCooldownSeconds;
                ModLogger.Info(
                    "CoopMissionSelectionView: start battle hotkey ignored because battle is not ready. " +
                    "HasLocalControlledAgent=" + hasLocalControlledAgent +
                    " CanStartBattle=" + (snapshot?.CanStartBattle ?? false) +
                    " SnapshotHasAgent=" + (snapshot?.HasAgent ?? false) +
                    " Lifecycle=" + (snapshot?.LifecycleState ?? string.Empty) +
                    " Peer=" + (snapshot?.PeerName ?? string.Empty));
                return;
            }

            string requestSource = isAutoDeploymentRequest
                ? "Battle-map host H hotkey auto-deploy via CoopMissionSelectionView"
                : "Battle-map client H hotkey via CoopMissionSelectionView";
            if (CoopBattlePhaseBridgeFile.WriteStartBattleRequest(requestSource))
            {
                _startBattleHotkeyCooldown = StartBattleHotkeyCooldownSeconds;
                InformationManager.DisplayMessage(
                    new InformationMessage(
                        isAutoDeploymentRequest
                            ? "Coop Battle: automatic deployment of both armies requested"
                            : "Coop Battle: start requested"));
                ModLogger.Info(
                    isAutoDeploymentRequest
                        ? "CoopMissionSelectionView: wrote both-armies auto-deployment request from host H hotkey."
                        : "CoopMissionSelectionView: wrote start battle request from H hotkey.");
            }
        }

        private void TryShowStartBattleInstruction(bool hasLocalControlledAgent)
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return;

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot = CoopBattleEntryStatusBridgeFile.ReadStatus();
            bool canStartBattleNow = snapshot != null && snapshot.CanStartBattle;
            if (!canStartBattleNow)
                return;

            if (!hasLocalControlledAgent)
            {
                if (_autoDeployInstructionShown ||
                    !_localSpawnPending ||
                    !_localSpawnPendingWaitsForDeployment)
                {
                    return;
                }

                _autoDeployInstructionShown = true;
                InformationManager.DisplayMessage(
                    new InformationMessage("Coop Battle: press H to auto-deploy both armies."));
                ModLogger.Info(
                    "CoopMissionSelectionView: showed one-shot both-armies auto-deployment instruction for local host peer.");
                return;
            }

            if (_startBattleInstructionShown)
                return;

            _startBattleInstructionShown = true;
            InformationManager.DisplayMessage(new InformationMessage("Coop Battle: press H to start the battle."));
            ModLogger.Info("CoopMissionSelectionView: showed one-shot start battle instruction for local host-controlled peer.");
        }

        private void TryHandleReopenSelectionHotkey(float dt, bool hasLocalControlledAgent)
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return;

            if (CoopBattleAgentControlRuntimeState.IsClientAiObservationOrTransitionActive())
                return;

            _reopenSelectionHotkeyCooldown -= dt;
            if (_reopenSelectionHotkeyCooldown > 0f || hasLocalControlledAgent || !_spectatorOverlayHidden || !Input.IsKeyPressed(InputKey.H))
                return;

            _reopenSelectionHotkeyCooldown = ReopenSelectionHotkeyCooldownSeconds;
            ResetSelectionFlow("spectator-reopen-hotkey");
            InformationManager.DisplayMessage(new InformationMessage("Coop Battle: selection reopened"));
            RefreshOverlay(force: true, hasLocalControlledAgent);
        }

        internal static bool HasLocalControlledAgent()
        {
            if (!GameNetwork.IsClient)
                return false;

            if (CoopBattleAgentControlRuntimeState.IsClientAiObserved())
                return false;

            Agent mainAgent = Agent.Main;
            if (mainAgent != null && mainAgent.IsActive() && mainAgent.MissionPeer != null)
                return true;

            MissionPeer missionPeer = GameNetwork.MyPeer?.GetComponent<MissionPeer>();
            Agent controlledAgent = missionPeer?.ControlledAgent;
            return controlledAgent != null && controlledAgent.IsActive();
        }

        internal static void TrySetLayerActiveState(ScreenLayer layer, bool isActive)
        {
            if (layer == null)
                return;

            try
            {
                PropertyInfo property = layer.GetType().GetProperty("IsActive", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                property?.GetSetMethod(true)?.Invoke(layer, new object[] { isActive });
            }
            catch
            {
            }
        }

        internal static void TryInvokeLayerFocusCallback(ScreenLayer layer, string methodName)
        {
            if (layer == null || string.IsNullOrWhiteSpace(methodName))
                return;

            try
            {
                layer.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(layer, Array.Empty<object>());
            }
            catch
            {
            }
        }

        internal static void ApplyMissionScreenOverlayMode(ScreenBase missionScreen, bool isOverlayActive)
        {
            if (missionScreen == null)
                return;

            TryInvokeInstanceMethod(missionScreen, "SetDisplayDialog", isOverlayActive);
            TryInvokeInstanceMethod(missionScreen, "SetCameraLockState", isOverlayActive);
            TrySetInstanceProperty(missionScreen, "LockCameraMovement", isOverlayActive);
        }

        internal static void ApplyMissionScreenCommanderPlacementMode(ScreenBase missionScreen)
        {
            if (missionScreen == null)
                return;

            TryInvokeInstanceMethod(missionScreen, "SetDisplayDialog", false);
            TryInvokeInstanceMethod(missionScreen, "SetCameraLockState", false);
            TrySetInstanceProperty(missionScreen, "LockCameraMovement", false);
        }

        internal static void LogMissionScreenOverlayDiagnostics(ScreenBase missionScreen, string source)
        {
            if (!CoopDebugConfig.OrderOfBattleDiagnostics || missionScreen == null)
                return;

            bool? mouseVisible = TryGetInstanceProperty<bool>(missionScreen, "MouseVisible");
            bool? lockCameraMovement = TryGetInstanceProperty<bool>(missionScreen, "LockCameraMovement");
            bool? isDeploymentActive = TryGetInstanceProperty<bool>(missionScreen, "IsDeploymentActive");
            bool? isOrderMenuOpen = TryGetInstanceProperty<bool>(missionScreen, "IsOrderMenuOpen");
            bool screenManagerMouseVisible = false;
            bool screenManagerMouseActive = false;
            string focusedLayer = "<null>";
            try
            {
                screenManagerMouseVisible = ScreenManager.GetMouseVisibility();
                screenManagerMouseActive = ScreenManager.IsMouseCursorActive();
                focusedLayer = ScreenManager.FocusedLayer?.GetType().FullName ?? "<null>";
            }
            catch
            {
            }

            ModLogger.Info(
                "CoopMissionSelectionView: mission screen overlay diagnostics. " +
                "Source=" + (source ?? "unknown") +
                " Screen=" + missionScreen.GetType().FullName +
                " MouseVisible=" + (mouseVisible.HasValue ? mouseVisible.Value.ToString() : "n/a") +
                " LockCameraMovement=" + (lockCameraMovement.HasValue ? lockCameraMovement.Value.ToString() : "n/a") +
                " IsDeploymentActive=" + (isDeploymentActive.HasValue ? isDeploymentActive.Value.ToString() : "n/a") +
                " IsOrderMenuOpen=" + (isOrderMenuOpen.HasValue ? isOrderMenuOpen.Value.ToString() : "n/a") +
                " ScreenManagerMouseVisible=" + screenManagerMouseVisible +
                " ScreenManagerMouseActive=" + screenManagerMouseActive +
                " FocusedLayer=" + focusedLayer);
        }

        internal static void TrySetScreenManagerFocus(ScreenLayer layer)
        {
            try
            {
                if (layer != null)
                    ScreenManager.TrySetFocus(layer);
            }
            catch
            {
            }
        }

        internal static void TryLoseScreenManagerFocus(ScreenLayer layer)
        {
            try
            {
                if (layer != null)
                    ScreenManager.TryLoseFocus(layer);
            }
            catch
            {
            }
        }

        internal static void TrySetScreenManagerMouseVisibility(bool isVisible)
        {
            try
            {
                typeof(ScreenManager).GetMethod("SetMouseVisible", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null)
                    ?.Invoke(null, new object[] { isVisible });
            }
            catch
            {
            }
        }

        private static MethodInfo FindInstanceMethod(Type type, string methodName, params object[] arguments)
        {
            if (type == null || string.IsNullOrWhiteSpace(methodName))
                return null;

            for (Type currentType = type; currentType != null; currentType = currentType.BaseType)
            {
                MethodInfo method = currentType
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(candidate =>
                        string.Equals(candidate.Name, methodName, StringComparison.Ordinal) &&
                        AreMethodParametersCompatible(candidate.GetParameters(), arguments));
                if (method != null)
                    return method;
            }

            return null;
        }

        private static bool AreMethodParametersCompatible(ParameterInfo[] parameters, object[] arguments)
        {
            arguments = arguments ?? Array.Empty<object>();
            if (parameters == null || parameters.Length != arguments.Length)
                return false;

            for (int i = 0; i < parameters.Length; i++)
            {
                Type parameterType = parameters[i].ParameterType;
                object argument = arguments[i];
                if (argument == null)
                {
                    if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) == null)
                        return false;
                    continue;
                }

                if (!parameterType.IsInstanceOfType(argument))
                    return false;
            }

            return true;
        }

        private static FieldInfo FindInstanceField(Type type, string fieldName)
        {
            if (type == null || string.IsNullOrWhiteSpace(fieldName))
                return null;

            for (Type currentType = type; currentType != null; currentType = currentType.BaseType)
            {
                FieldInfo field = currentType.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }

            return null;
        }

        private static PropertyInfo FindInstanceProperty(Type type, string propertyName)
        {
            if (type == null || string.IsNullOrWhiteSpace(propertyName))
                return null;

            for (Type currentType = type; currentType != null; currentType = currentType.BaseType)
            {
                PropertyInfo property = currentType.GetProperty(
                    propertyName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (property != null)
                    return property;
            }

            return null;
        }

        private static EventInfo FindInstanceEvent(Type type, string eventName)
        {
            if (type == null || string.IsNullOrWhiteSpace(eventName))
                return null;

            for (Type currentType = type; currentType != null; currentType = currentType.BaseType)
            {
                EventInfo eventInfo = currentType.GetEvent(
                    eventName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (eventInfo != null)
                    return eventInfo;
            }

            return null;
        }

        internal static void TryInvokeInstanceMethod(object target, string methodName, params object[] arguments)
        {
            if (target == null || string.IsNullOrWhiteSpace(methodName))
                return;

            try
            {
                Type[] argumentTypes = arguments?.Select(argument => argument?.GetType() ?? typeof(object)).ToArray() ?? Type.EmptyTypes;
                target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, argumentTypes, null)
                    ?.Invoke(target, arguments);
            }
            catch
            {
            }
        }

        private static bool TryInvokeInstanceMethodSuccessfully(object target, string methodName, params object[] arguments)
        {
            if (target == null || string.IsNullOrWhiteSpace(methodName))
                return false;

            try
            {
                MethodInfo method = FindInstanceMethod(target.GetType(), methodName, arguments);
                if (method == null)
                    return false;

                method.Invoke(target, arguments);
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static void TrySetInstanceProperty(object target, string propertyName, object value)
        {
            if (target == null || string.IsNullOrWhiteSpace(propertyName))
                return;

            try
            {
                PropertyInfo property = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                property?.GetSetMethod(true)?.Invoke(target, new[] { value });
            }
            catch
            {
            }
        }

        internal static void TrySetInstanceField(object target, string fieldName, object value)
        {
            if (target == null || string.IsNullOrWhiteSpace(fieldName))
                return;

            try
            {
                FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                field?.SetValue(target, value);
            }
            catch
            {
            }
        }

        internal static object TryGetInstancePropertyValue(object target, string propertyName)
        {
            if (target == null || string.IsNullOrWhiteSpace(propertyName))
                return null;

            try
            {
                return target.GetType()
                    .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(target);
            }
            catch
            {
                return null;
            }
        }

        private static object TryGetInstanceMemberValue(object target, string memberName)
        {
            if (target == null || string.IsNullOrWhiteSpace(memberName))
                return null;

            try
            {
                PropertyInfo property = FindInstanceProperty(target.GetType(), memberName);
                if (property != null && property.GetIndexParameters().Length == 0)
                    return property.GetValue(target);

                FieldInfo field = FindInstanceField(target.GetType(), memberName);
                if (field != null)
                    return field.GetValue(target);
            }
            catch
            {
            }

            return null;
        }

        private static object TryInvokeInstanceMethodWithResult(object target, string methodName, params object[] arguments)
        {
            if (target == null || string.IsNullOrWhiteSpace(methodName))
                return null;

            try
            {
                MethodInfo method = FindInstanceMethod(target.GetType(), methodName, arguments);
                return method?.Invoke(target, arguments);
            }
            catch
            {
                return null;
            }
        }

        private static bool? TryInvokeBoolMethod(object target, string methodName, params object[] arguments)
        {
            object result = TryInvokeInstanceMethodWithResult(target, methodName, arguments);
            return result is bool value ? value : (bool?)null;
        }

        private static bool TryGetInstanceBool(object target, string memberName)
        {
            object value = TryGetInstanceMemberValue(target, memberName);
            return value is bool boolValue && boolValue;
        }

        private static int TryGetCollectionCount(object collection)
        {
            if (collection == null)
                return 0;

            if (collection is ICollection nonGenericCollection)
                return nonGenericCollection.Count;

            int count = 0;
            if (collection is IEnumerable enumerable)
            {
                foreach (object _ in enumerable)
                    count++;
            }

            return count;
        }

        private static object TryGetCollectionItem(object collection, int index)
        {
            if (collection == null || index < 0)
                return null;

            if (collection is IList list)
                return index < list.Count ? list[index] : null;

            if (collection is IEnumerable enumerable)
            {
                int currentIndex = 0;
                foreach (object item in enumerable)
                {
                    if (currentIndex == index)
                        return item;
                    currentIndex++;
                }
            }

            return null;
        }

        private static bool IsReturnVisualOrderInstance(object visualOrder)
        {
            return visualOrder != null &&
                   string.Equals(visualOrder.GetType().Name, "ReturnVisualOrder", StringComparison.Ordinal);
        }

        private static bool OrderItemCollectionContainsReturnVisualOrder(object orderItems)
        {
            if (!(orderItems is IEnumerable enumerable))
                return false;

            foreach (object orderItem in enumerable)
            {
                object visualOrder = TryGetInstanceMemberValue(orderItem, "Order");
                if (IsReturnVisualOrderInstance(visualOrder))
                    return true;
            }

            return false;
        }

        private static bool IsReturnOnlyOrderSet(object orderSet)
        {
            if (orderSet == null || !IsSoloOrderSet(orderSet))
                return false;

            object orders = TryGetInstanceMemberValue(orderSet, "Orders");
            object firstOrderItem = TryGetCollectionItem(orders, 0);
            object visualOrder = TryGetInstanceMemberValue(firstOrderItem, "Order");
            return IsReturnVisualOrderInstance(visualOrder);
        }

        private static bool IsSoloOrderSet(object orderSet)
        {
            return orderSet != null && TryGetInstanceBool(orderSet, "HasSingleOrder");
        }

        private static bool OrderSetCollectionContainsReturnOnlySet(object orderSets)
        {
            if (!(orderSets is IEnumerable enumerable))
                return false;

            foreach (object orderSet in enumerable)
            {
                if (IsReturnOnlyOrderSet(orderSet))
                    return true;
            }

            return false;
        }

        private static bool ShouldIncludeSiegeAmbushDestroySiegeWeaponsOrder()
        {
            if (!IsCommanderBattleOrderActive())
                return false;

            Mission mission = Mission.Current;
            if (mission == null ||
                mission.PlayerTeam == null ||
                !ReferenceEquals(mission.PlayerTeam, mission.DefenderTeam))
            {
                return false;
            }

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetScenarioContext() ??
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            return SiegeAmbushScenarioContract.IsSiegeAmbushScenario(scenarioContext);
        }

        private static bool ShouldIncludeSiegeAssaultFormationOrderSet()
        {
            if (!IsCommanderBattleOrderActive() ||
                CoopBattlePhaseRuntimeState.GetPhase() != CoopBattlePhase.BattleActive)
            {
                return false;
            }

            Mission mission = Mission.Current;
            Team playerTeam = mission?.PlayerTeam;
            if (playerTeam == null ||
                (playerTeam.Side != BattleSideEnum.Attacker &&
                 playerTeam.Side != BattleSideEnum.Defender))
            {
                return false;
            }

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetScenarioContext() ??
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext;
            return ExactCampaignSiegeAssaultWithDeploymentRuntime
                       .IsSiegeAssaultScenario(scenarioContext) ||
                   ExactCampaignSiegeAssaultNoDeploymentRuntime
                       .IsSiegeAssaultScenario(scenarioContext);
        }

        private static void ExecuteSiegeAssaultFormationOrder(
            OrderController orderController,
            CoopSiegeAssaultFormationOrderKind orderKind)
        {
            Mission mission = Mission.Current;
            Team team = mission?.PlayerTeam;
            if (!ShouldIncludeSiegeAssaultFormationOrderSet() ||
                orderController?.SelectedFormations == null ||
                team == null)
            {
                InformationManager.DisplayMessage(
                    new InformationMessage(
                        "Coop Battle: siege order is unavailable."));
                return;
            }

            bool orderMatchesSide =
                team.Side == BattleSideEnum.Attacker
                    ? orderKind !=
                      CoopSiegeAssaultFormationOrderKind.OccupyArcherPositions
                    : orderKind ==
                      CoopSiegeAssaultFormationOrderKind.OccupyArcherPositions;
            if (!orderMatchesSide)
            {
                InformationManager.DisplayMessage(
                    new InformationMessage(
                        "Coop Battle: siege order is invalid for this side."));
                return;
            }

            int formationMask = BuildSelectedFormationMask(orderController, team);
            if (formationMask <= 0)
            {
                InformationManager.DisplayMessage(
                    new InformationMessage(
                        "Coop Battle: select at least one active formation."));
                return;
            }

            bool sent = CoopBattleNetworkRequestTransport
                .TryIssueSiegeAssaultFormationOrder(
                    team.Side,
                    formationMask,
                    orderKind,
                    "F7 siege order set");
            InformationManager.DisplayMessage(
                new InformationMessage(
                    sent
                        ? GetSiegeAssaultOrderSentMessage(orderKind)
                        : "Coop Battle: failed to send siege order."));
        }

        private static int BuildSelectedFormationMask(
            OrderController orderController,
            Team team)
        {
            if (orderController?.SelectedFormations == null || team == null)
                return 0;

            int formationMask = 0;
            foreach (Formation formation in orderController.SelectedFormations)
            {
                if (formation == null ||
                    !ReferenceEquals(formation.Team, team) ||
                    formation.CountOfUnits <= 0 ||
                    formation.Index < 0 ||
                    formation.Index >= (int)FormationClass.NumberOfRegularFormations)
                {
                    continue;
                }

                formationMask |= 1 << formation.Index;
            }

            return formationMask;
        }

        private static string GetSiegeAssaultOrderSentMessage(
            CoopSiegeAssaultFormationOrderKind orderKind)
        {
            switch (orderKind)
            {
                case CoopSiegeAssaultFormationOrderKind.AttackGate:
                    return "Coop Battle: attack gate order sent.";
                case CoopSiegeAssaultFormationOrderKind.AssaultWalls:
                    return "Coop Battle: assault walls order sent.";
                case CoopSiegeAssaultFormationOrderKind.UseSiegeMachines:
                    return "Coop Battle: use siege machines order sent.";
                case CoopSiegeAssaultFormationOrderKind.OccupyAttackerBarricades:
                    return "Coop Battle: occupy barricades order sent.";
                case CoopSiegeAssaultFormationOrderKind.OccupyArcherPositions:
                    return "Coop Battle: occupy wall positions order sent.";
                default:
                    return "Coop Battle: siege order sent.";
            }
        }

        private static void ExecuteSiegeAmbushDestroySiegeWeaponsOrder(
            OrderController orderController)
        {
            Mission mission = Mission.Current;
            Team team = mission?.PlayerTeam;
            if (!ShouldIncludeSiegeAmbushDestroySiegeWeaponsOrder() ||
                orderController?.SelectedFormations == null ||
                team == null)
            {
                InformationManager.DisplayMessage(
                    new InformationMessage(
                        "Coop Battle: destroy siege engines order is unavailable."));
                return;
            }

            int formationMask = 0;
            foreach (Formation formation in orderController.SelectedFormations)
            {
                if (formation == null ||
                    !ReferenceEquals(formation.Team, team) ||
                    formation.CountOfUnits <= 0 ||
                    formation.Index < 0 ||
                    formation.Index >= (int)FormationClass.NumberOfRegularFormations)
                {
                    continue;
                }

                formationMask |= 1 << formation.Index;
            }

            if (formationMask <= 0)
            {
                InformationManager.DisplayMessage(
                    new InformationMessage(
                        "Coop Battle: select at least one active formation."));
                return;
            }

            bool sent =
                CoopBattleNetworkRequestTransport
                    .TryOrderSelectedFormationsToDestroySiegeWeapons(
                        team.Side,
                        formationMask,
                        "F8 movement order");
            InformationManager.DisplayMessage(
                new InformationMessage(
                    sent
                        ? "Coop Battle: attack siege engines order sent."
                        : "Coop Battle: failed to send attack siege engines order."));
        }

        private sealed class CoopSiegeAmbushDestroySiegeWeaponsVisualOrder : VisualOrder
        {
            private static readonly TextObject Name =
                new TextObject("{=CoopAttackSiegeEngines}Attack Siege Engines");

            public CoopSiegeAmbushDestroySiegeWeaponsVisualOrder()
                : base("coop_attack_siege_engines")
            {
            }

            protected override string GetIconId()
            {
                return "order_movement_charge";
            }

            public override TextObject GetName(OrderController orderController)
            {
                return Name;
            }

            public override bool IsTargeted()
            {
                return false;
            }

            public override void ExecuteOrder(
                OrderController orderController,
                VisualOrderExecutionParameters executionParameters)
            {
                ExecuteSiegeAmbushDestroySiegeWeaponsOrder(orderController);
            }

            protected override bool? OnGetFormationHasOrder(Formation formation)
            {
                return false;
            }
        }

        private sealed class CoopSiegeAssaultFormationVisualOrder : VisualOrder
        {
            private readonly CoopSiegeAssaultFormationOrderKind _orderKind;
            private readonly TextObject _name;

            public CoopSiegeAssaultFormationVisualOrder(
                string iconId,
                CoopSiegeAssaultFormationOrderKind orderKind,
                TextObject name)
                : base(iconId)
            {
                _orderKind = orderKind;
                _name = name;
            }

            public override TextObject GetName(OrderController orderController)
            {
                return _name;
            }

            public override bool IsTargeted()
            {
                return false;
            }

            public override void ExecuteOrder(
                OrderController orderController,
                VisualOrderExecutionParameters executionParameters)
            {
                ExecuteSiegeAssaultFormationOrder(orderController, _orderKind);
            }

            protected override bool? OnGetFormationHasOrder(Formation formation)
            {
                return false;
            }
        }

        private static GenericVisualOrderSet CreateSiegeAssaultFormationOrderSet()
        {
            if (!ShouldIncludeSiegeAssaultFormationOrderSet())
                return null;

            Mission mission = Mission.Current;
            Team team = mission?.PlayerTeam;
            if (team == null)
                return null;

            var siegeSet = new GenericVisualOrderSet(
                "order_movement_charge",
                new TextObject("{=CoopSiegeOrders}Siege"),
                useActiveOrderForIconId: false,
                useActiveOrderForName: false);
            if (team.Side == BattleSideEnum.Attacker)
            {
                siegeSet.AddOrder(
                    new CoopSiegeAssaultFormationVisualOrder(
                        "order_movement_charge",
                        CoopSiegeAssaultFormationOrderKind.AttackGate,
                        new TextObject("{=CoopAttackGate}Attack Gate")));
                siegeSet.AddOrder(
                    new CoopSiegeAssaultFormationVisualOrder(
                        "order_movement_advance",
                        CoopSiegeAssaultFormationOrderKind.AssaultWalls,
                        new TextObject("{=CoopAssaultWalls}Assault Walls")));
                siegeSet.AddOrder(
                    new CoopSiegeAssaultFormationVisualOrder(
                        "order_movement_follow",
                        CoopSiegeAssaultFormationOrderKind.UseSiegeMachines,
                        new TextObject("{=CoopUseSiegeMachines}Use Siege Machines")));
                siegeSet.AddOrder(
                    new CoopSiegeAssaultFormationVisualOrder(
                        "order_toggle_fire",
                        CoopSiegeAssaultFormationOrderKind.OccupyAttackerBarricades,
                        new TextObject("{=CoopOccupyAttackerBarricades}Occupy Barricades")));
            }
            else if (team.Side == BattleSideEnum.Defender)
            {
                siegeSet.AddOrder(
                    new CoopSiegeAssaultFormationVisualOrder(
                        "order_toggle_fire",
                        CoopSiegeAssaultFormationOrderKind.OccupyArcherPositions,
                        new TextObject("{=CoopOccupyWallPositions}Occupy Wall Positions")));
            }

            siegeSet.AddOrder(new ReturnVisualOrder());
            return siegeSet;
        }

        private sealed class CoopCommanderDeploymentVisualOrderProvider : VisualOrderProvider
        {
            public override bool IsAvailable()
            {
                Mission mission = Mission.Current;
                if (mission?.PlayerTeam?.PlayerOrderController == null)
                    return false;

                return CoopMissionSelectionView.IsCommanderDeploymentOrderOfBattleActive() ||
                       CoopMissionSelectionView.IsCommanderBattleOrderActive();
            }

            public override MBReadOnlyList<VisualOrderSet> GetOrders()
            {
                return TaleWorlds.InputSystem.Input.IsGamepadActive
                    ? GetDefaultOrders(includeShortcutOrders: false)
                    : GetDefaultOrders(includeShortcutOrders: true);
            }

            private static MBReadOnlyList<VisualOrderSet> GetDefaultOrders(bool includeShortcutOrders)
            {
                var orders = new MBList<VisualOrderSet>();

                var movementSet = new GenericVisualOrderSet(
                    "order_type_movement",
                    new TextObject("{=KiJd6Xik}Movement"),
                    useActiveOrderForIconId: true,
                    useActiveOrderForName: true);
                movementSet.AddOrder(new MoveVisualOrder("order_movement_move"));
                movementSet.AddOrder(new FollowMeVisualOrder("order_movement_follow"));
                movementSet.AddOrder(new ChargeVisualOrder("order_movement_charge"));
                movementSet.AddOrder(new AdvanceVisualOrder("order_movement_advance"));
                movementSet.AddOrder(new FallbackVisualOrder("order_movement_fallback"));
                movementSet.AddOrder(new StopVisualOrder("order_movement_stop"));
                movementSet.AddOrder(new RetreatVisualOrder("order_movement_retreat"));
                if (ShouldIncludeSiegeAmbushDestroySiegeWeaponsOrder())
                {
                    movementSet.AddOrder(
                        new CoopSiegeAmbushDestroySiegeWeaponsVisualOrder());
                }
                movementSet.AddOrder(new ReturnVisualOrder());

                var formSet = new GenericVisualOrderSet(
                    "order_type_form",
                    new TextObject("{=iBk2wbn3}Form"),
                    useActiveOrderForIconId: true,
                    useActiveOrderForName: true);
                var lineOrder = new ArrangementVisualOrder(ArrangementOrder.ArrangementOrderEnum.Line, "order_form_line");
                var shieldWallOrder = new ArrangementVisualOrder(ArrangementOrder.ArrangementOrderEnum.ShieldWall, "order_form_close");
                formSet.AddOrder(lineOrder);
                formSet.AddOrder(shieldWallOrder);
                formSet.AddOrder(new ArrangementVisualOrder(ArrangementOrder.ArrangementOrderEnum.Loose, "order_form_loose"));
                formSet.AddOrder(new ArrangementVisualOrder(ArrangementOrder.ArrangementOrderEnum.Circle, "order_form_circular"));
                formSet.AddOrder(new ArrangementVisualOrder(ArrangementOrder.ArrangementOrderEnum.Square, "order_form_schiltron"));
                formSet.AddOrder(new ArrangementVisualOrder(ArrangementOrder.ArrangementOrderEnum.Skein, "order_form_v"));
                formSet.AddOrder(new ArrangementVisualOrder(ArrangementOrder.ArrangementOrderEnum.Column, "order_form_column"));
                formSet.AddOrder(new ArrangementVisualOrder(ArrangementOrder.ArrangementOrderEnum.Scatter, "order_form_scatter"));
                formSet.AddOrder(new ReturnVisualOrder());

                var toggleSet = new GenericVisualOrderSet(
                    "order_type_toggle",
                    new TextObject("{=0HTNYQz2}Toggle"),
                    useActiveOrderForIconId: false,
                    useActiveOrderForName: false);
                var facingOrder = new ToggleFacingVisualOrder("order_toggle_facing");
                var fireOrder = new GenericToggleVisualOrder("order_toggle_fire", (OrderType)32, (OrderType)31);
                var mountedOrder = new GenericToggleVisualOrder("order_toggle_mount", (OrderType)34, (OrderType)35);
                var delegateOrder = new GenericToggleVisualOrder("order_toggle_ai", (OrderType)36, (OrderType)37);
                var transferOrder = new TransferTroopsVisualOrder();
                toggleSet.AddOrder(facingOrder);
                toggleSet.AddOrder(fireOrder);
                toggleSet.AddOrder(mountedOrder);
                toggleSet.AddOrder(delegateOrder);
                toggleSet.AddOrder(transferOrder);
                toggleSet.AddOrder(new ReturnVisualOrder());

                GenericVisualOrderSet siegeAssaultSet =
                    CreateSiegeAssaultFormationOrderSet();

                orders.Add(movementSet);
                orders.Add(formSet);
                orders.Add(toggleSet);
                if (includeShortcutOrders)
                {
                    orders.Add(new SingleVisualOrderSet(fireOrder));
                    orders.Add(new SingleVisualOrderSet(mountedOrder));
                    orders.Add(new SingleVisualOrderSet(delegateOrder));
                    orders.Add(
                        siegeAssaultSet ??
                        (VisualOrderSet)new SingleVisualOrderSet(facingOrder));
                    orders.Add(new SingleVisualOrderSet(shieldWallOrder));
                    orders.Add(new SingleVisualOrderSet(lineOrder));
                }
                else if (siegeAssaultSet != null)
                {
                    orders.Add(siegeAssaultSet);
                }

                return orders;
            }
        }

        internal static T? TryGetInstanceProperty<T>(object target, string propertyName) where T : struct
        {
            if (target == null || string.IsNullOrWhiteSpace(propertyName))
                return null;

            try
            {
                object value = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target);
                if (value is T typed)
                    return typed;
            }
            catch
            {
            }

            return null;
        }
    }

    internal enum CoopSelectionScreen
    {
        None = 0,
        TeamSelection = 1,
        ClassLoadout = 2,
        CommanderDeployment = 3
    }

    internal interface ICoopSelectionScreenViewModel
    {
        void Refresh(CoopSelectionUiSnapshot snapshot, bool force);
    }
}
