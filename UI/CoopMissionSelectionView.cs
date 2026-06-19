using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using CoopSpectator.Network.Messages;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.MountAndBlade.ViewModelCollection.Order;
using TaleWorlds.MountAndBlade.ViewModelCollection.Order.Visual;
using TaleWorlds.MountAndBlade.ViewModelCollection.OrderOfBattle;
using TaleWorlds.ScreenSystem;
using TaleWorlds.TwoDimension;

namespace CoopSpectator.UI
{
    public sealed class CoopMissionSelectionView : MissionView
    {
        private const string TeamMovieName = "CoopTeamSelection";
        private const string ClassMovieName = "CoopClassLoadout";
        private const string CommanderDeploymentMovieName = "OrderOfBattle";
        private static readonly bool EnableManualSiegeCommanderDeployment = true;
        private const float RefreshIntervalSeconds = 0.15f;
        private const float InitialOverlayDelaySeconds = 0.75f;
        private const float StartBattleHotkeyCooldownSeconds = 0.2f;
        private const float ReopenSelectionHotkeyCooldownSeconds = 0.2f;
        private static readonly InputUsageMask CommanderDeploymentInputMask = (InputUsageMask)7;
        private static readonly TimeSpan LocalSpawnOverlaySuppressionDuration = TimeSpan.FromSeconds(2.5);
        private static readonly TimeSpan LocalSpawnPendingTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan LocalSpawnPendingResendInterval = TimeSpan.FromSeconds(1.5);
        private const int LocalSpawnPendingMaxRequestAttempts = 8;
        private static int _activeCameraPreviewAgentIndex = -1;
        private static string _activeCameraPreviewEntryId = string.Empty;
        private static bool _commanderDeploymentOrderOfBattleActive;
        private static bool _commanderDeploymentPlacementInputActive;

        private GauntletLayer _gauntletLayer;
        private GauntletMovieIdentifier _movie;
        private ViewModel _viewModel;
        private OrderOfBattleVM _commanderDeploymentViewModel;
        private MissionOrderVM _commanderDeploymentOrderVm;
        private SpriteCategory _commanderDeploymentSpriteCategory;
        private object _commanderDeploymentOrderTroopPlacer;
        private Action _commanderDeploymentOnUnitDeployedHandler;
        private bool _commanderDeploymentOrderVmInitialized;
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
        private bool _spectatorOverlayHidden;
        private DateTime _overlaySuppressedUntilUtc = DateTime.MinValue;
        private float _reopenSelectionHotkeyCooldown;
        private string _lastAppliedRefreshKey = string.Empty;
        private bool _localSpawnPending;
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
            _reconnectSelectionContractActive = false;
            _lastReconnectSelectionContractLogKey = string.Empty;
            _lastIgnoredEntryStatusLogKey = string.Empty;
            _commanderDeploymentPlacementInputActive = false;
            ClearLocalSpawnPending("mission-screen-initialize");
            ResetSelectionFlow("mission-screen-initialize");
            ModLogger.Info("CoopMissionSelectionView: OnMissionScreenInitialize, coop selection shell init deferred.");
        }

        public override void OnMissionScreenTick(float dt)
        {
            base.OnMissionScreenTick(dt);

            if (!GameNetwork.IsClient || !ExperimentalFeatures.EnableCustomCoopSelectionOverlay)
                return;

            bool hasLocalControlledAgent = HasLocalControlledAgent();
            if (_hadLocalControlledAgent && !hasLocalControlledAgent)
            {
                ClearLocalSpawnPending("lost-local-agent");
                _overlaySuppressedUntilUtc = DateTime.MinValue;
                ResetSelectionFlow("lost-local-agent");
            }
            else if (!_hadLocalControlledAgent && hasLocalControlledAgent)
            {
                ClearLocalSpawnPending("gained-local-agent");
                _selectedEntryIdOverride = null;
            }

            _hadLocalControlledAgent = hasLocalControlledAgent;
            TryHandleStartBattleHotkey(dt, hasLocalControlledAgent);
            TryShowStartBattleInstruction(hasLocalControlledAgent);
            TryHandleReopenSelectionHotkey(dt, hasLocalControlledAgent);
            TryTickCommanderDeploymentViewModel();

            if (_gauntletLayer == null)
            {
                if (_overlayLoadFailed)
                    return;

                _overlayStartupDelay -= dt;
                if (_overlayStartupDelay <= 0f)
                    TryEnsureLayer();
                return;
            }

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
                ReleaseCurrentMovie();
                _commanderDeploymentOrderOfBattleActive = false;
                _commanderDeploymentPlacementInputActive = false;

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
            _reconnectSelectionContractActive = false;
            _lastReconnectSelectionContractLogKey = string.Empty;
            base.OnMissionScreenFinalize();
        }

        public override bool OnEscape()
        {
            if (GameNetwork.IsClient && ExperimentalFeatures.EnableCustomCoopSelectionOverlay)
            {
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

            if (EnableManualSiegeCommanderDeployment && IsNativeDeploymentUiActive())
                return CoopSelectionScreen.None;

            if (ShouldKeepOverlaySuppressedWhileAwaitingLocalSpawn(snapshot))
                return CoopSelectionScreen.None;

            if (DateTime.UtcNow < _overlaySuppressedUntilUtc)
                return CoopSelectionScreen.None;

            if (!snapshot.BattleDataReady)
                return IsReconnectFinalizePendingWithAssignedSide(snapshot)
                    ? CoopSelectionScreen.None
                    : CoopSelectionScreen.TeamSelection;

            if (snapshot.ReconnectSelectionContractActive)
                return DetermineReconnectDesiredScreen(snapshot);

            if (_requestedScreen == CoopSelectionScreen.CommanderDeployment &&
                IsCommanderDeploymentReady(snapshot))
            {
                return CoopSelectionScreen.CommanderDeployment;
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

        private static bool IsCommanderDeploymentReady(CoopSelectionUiSnapshot snapshot)
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
            return CoopSelectionUiHelpers.IsCommanderEntry(
                snapshot.BattleState,
                snapshot.EffectiveSide,
                entryState);
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

            Camera missionCamera = ResolveMissionScreenCombatCamera();
            if (missionCamera == null)
                throw new InvalidOperationException("combat-camera-null");

            var commanderVm = new CoopSiegeOrderOfBattleVM();
            commanderVm.Initialize(
                mission,
                missionCamera,
                SelectNativeCommanderFormationAtIndex,
                DeselectNativeCommanderFormationAtIndex,
                ClearNativeCommanderFormationSelection,
                HandleCommanderAutoDeployRequested,
                HandleCommanderReadyRequested,
                new Dictionary<int, Agent>());
            commanderVm.IsEnabled = true;
            commanderVm.AreCameraControlsEnabled = false;
            commanderVm.CanStartMission = true;
            commanderVm.AutoDeployText = GameTexts.FindText("str_auto_deploy").ToString();
            TryRegisterOrderOfBattleHotKeys(commanderVm);
            TryCreateCommanderDeploymentOrderBridge(mission, missionCamera);
            TryAttachCommanderDeploymentOrderTroopPlacerCallback(commanderVm);
            TryRefreshNativeCommanderOrderOfBattleCounts(commanderVm, mission, "post-initialize");

            ModLogger.Info(
                "CoopMissionSelectionView: prepared native OrderOfBattle commander deployment. " +
                "Diagnostics={" + (prepareDiagnostics ?? string.Empty) + "}");
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
                !IsCommanderDeploymentOrderOfBattleActive())
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

        private static FormationClass ResolveProjectedSiegeOrderOfBattleAgentClass(Agent agent)
        {
            if (agent == null || agent.IsMount)
                return FormationClass.NumberOfAllFormations;

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

            if (!TryResolveCameraPreviewAgent(snapshot, out Agent selectedCommanderAgent) ||
                selectedCommanderAgent?.Team == null ||
                selectedCommanderAgent.Team.Side != side)
            {
                diagnostics =
                    "selected-commander-agent-null" +
                    " Side=" + side +
                    " EntryId=" + (snapshot?.SelectedEntryId ?? string.Empty) +
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
                    " EntryId=" + (snapshot?.SelectedEntryId ?? string.Empty) +
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
                    " EntryId=" + (snapshot?.SelectedEntryId ?? string.Empty) +
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
                " EntryId=" + (snapshot?.SelectedEntryId ?? string.Empty) +
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
                return ownedFormationsWithUnits == formationsWithUnits;
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
                _commanderDeploymentOrderVm = new MissionOrderVM(orderController, isDeployment: true, isMultiplayer: false);
                _commanderDeploymentOrderVm.IsDeployment = true;
                _commanderDeploymentOrderVm.SetCallbacks(CreateCommanderDeploymentOrderCallbacks());
                _commanderDeploymentOrderVm.SetDeploymentParemeters(
                    missionCamera,
                    BuildCommanderDeploymentPointList(mission));
                TryRegisterMissionOrderHotKeys(_commanderDeploymentOrderVm);
                _commanderDeploymentOrderVm.AfterInitialize();
                TryInvokeInstanceMethodSuccessfully(_commanderDeploymentOrderVm.TroopController, "UpdateTroops");
                _commanderDeploymentOrderVm.UpdateCanUseShortcuts(true);
                _commanderDeploymentOrderVmInitialized = true;
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

        private static List<DeploymentPoint> BuildCommanderDeploymentPointList(Mission mission)
        {
            if (mission?.ActiveMissionObjects == null)
                return new List<DeploymentPoint>();

            try
            {
                return mission.ActiveMissionObjects
                    .FindAllWithType<DeploymentPoint>()
                    .Where(deploymentPoint => deploymentPoint != null && !deploymentPoint.IsDisabled)
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
        }

        private void RefreshCommanderDeploymentOrderVisuals()
        {
            TryRefreshNativeCommanderOrderOfBattleCounts(_commanderDeploymentViewModel, Mission, "mission-order-refresh-visuals");
        }

        private void ActivateCommanderDeploymentToggleOrder()
        {
            TryActivateNativeCommanderDeploymentPlacement(MissionScreen);
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
        }

        private void ToggleCommanderDeploymentMissionInputs(bool isLocked)
        {
            ScreenBase missionScreen = MissionScreen;
            if (missionScreen == null)
                return;

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
            if (_commanderDeploymentSpriteCategory != null)
                return;

            try
            {
                _commanderDeploymentSpriteCategory = UIResourceManager.LoadSpriteCategory("ui_order_of_battle");
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: failed to load OrderOfBattle sprite category: " + ex.Message);
                _commanderDeploymentSpriteCategory = null;
            }
        }

        private void ReleaseCommanderDeploymentSpriteCategory()
        {
            if (_commanderDeploymentSpriteCategory == null)
                return;

            try
            {
                _commanderDeploymentSpriteCategory.Unload();
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: failed to unload OrderOfBattle sprite category: " + ex.Message);
            }
            finally
            {
                _commanderDeploymentSpriteCategory = null;
            }
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

                TryInvokeInstanceMethodSuccessfully(_commanderDeploymentOrderVm.TroopController, "UpdateTroops");
                TryRefreshNativeCommanderOrderOfBattleCounts(
                    _commanderDeploymentViewModel,
                    Mission,
                    "mission-order-select-formation");
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
                TryRefreshNativeCommanderOrderOfBattleCounts(
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
                TryRefreshNativeCommanderOrderOfBattleCounts(
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
                TrySetInstanceProperty(orderTroopPlacer, "SuspendTroopPlacer", false);
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
                return Mission?.PlayerTeam?.FormationsIncludingEmpty?.ElementAtOrDefault(formationIndex);
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
            _selectedEntryIdOverride = null;
            _requestedScreen = CoopSelectionScreen.ClassLoadout;
            CoopBattleNetworkRequestTransport.TrySelectSide(side, "CoopTeamSelectionUI Side");
            RefreshOverlay(force: true, hasLocalControlledAgent);
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
            if (snapshot == null || !snapshot.CanSpawn || snapshot.EffectiveSide == BattleSideEnum.None || string.IsNullOrWhiteSpace(snapshot.SelectedEntryId))
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

            MarkLocalSpawnPending(snapshot);
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
            if (!ExactCampaignSiegeAssaultWithDeploymentRuntime.IsSiegeAssaultScenario(scenarioContext))
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
            TryApplyCommanderDeploymentOrderBridgeReady();
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
                ModLogger.Info("CoopMissionSelectionView: applied safe MissionOrderVM auto deployment. Handled=" + handled);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: safe MissionOrderVM auto deploy bridge failed: " + ex.Message);
            }
        }

        private void TryApplyCommanderDeploymentOrderBridgeReady()
        {
            if (!TryEnsureCommanderDeploymentOrderBridge())
                return;

            try
            {
                if (_commanderDeploymentViewModel?.CurrentConfiguration != null)
                {
                    TryInvokeInstanceMethodSuccessfully(
                        _commanderDeploymentOrderVm,
                        "OnFiltersSet",
                        _commanderDeploymentViewModel.CurrentConfiguration);
                }

                bool handled = TryInvokeInstanceMethodSuccessfully(
                    _commanderDeploymentOrderVm.DeploymentController,
                    "ExecuteBeginMission");
                if (!handled)
                    handled = TryInvokeInstanceMethodSuccessfully(_commanderDeploymentOrderVm, "OnDeploymentFinished");

                TryRefreshNativeCommanderOrderOfBattleCounts(
                    _commanderDeploymentViewModel,
                    Mission,
                    "mission-order-ready");
                ModLogger.Info("CoopMissionSelectionView: applied safe MissionOrderVM ready deployment. Handled=" + handled);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: safe MissionOrderVM ready bridge failed: " + ex.Message);
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

            bool queued = autoDeploy
                ? CoopBattleNetworkRequestTransport.TryAutoDeployCommanderDeployment(snapshot.EffectiveSide, snapshot.SelectedEntryId, source)
                : CoopBattleNetworkRequestTransport.TryFinishCommanderDeployment(snapshot.EffectiveSide, snapshot.SelectedEntryId, source);
            if (queued && !autoDeploy)
            {
                MarkLocalSpawnPending(snapshot);
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
                " EntryId=" + (snapshot.SelectedEntryId ?? string.Empty) +
                " Source=" + (source ?? "unknown"));
            if (autoDeploy || !queued)
                RefreshOverlay(force: true, hasLocalControlledAgent);
            return queued;
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

        private void MarkLocalSpawnPending(CoopSelectionUiSnapshot snapshot)
        {
            _localSpawnPending = true;
            _localSpawnPendingStartedUtc = DateTime.UtcNow;
            _localSpawnPendingEntryId = snapshot?.SelectedEntryId;
            _localSpawnPendingSide = snapshot?.EffectiveSide ?? BattleSideEnum.None;
            _localSpawnPendingLastRequestUtc = DateTime.MinValue;
            _localSpawnPendingRequestAttemptCount = 0;
            ModLogger.Info(
                "CoopMissionSelectionView: marked local spawn pending. " +
                "Side=" + _localSpawnPendingSide +
                " EntryId=" + (_localSpawnPendingEntryId ?? string.Empty));
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

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status = snapshot?.Status;
            string lifecycle = status?.LifecycleState ?? snapshot?.Lifecycle ?? string.Empty;
            if (string.Equals(lifecycle, "AwaitingSelection", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lifecycle, "NoSide", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lifecycle, "DeadAwaitingRespawn", StringComparison.OrdinalIgnoreCase))
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
                if (pendingEntryStillSelectable && DateTime.UtcNow < _overlaySuppressedUntilUtc)
                    return true;

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
                _commanderDeploymentOrderVm?.Update();
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: native OrderOfBattle tick failed: " + ex.Message);
                _commanderDeploymentViewModel = null;
                ReleaseCommanderDeploymentOrderBridge();
            }
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
                        TryDeactivateNativeCommanderDeploymentPlacement(missionScreen);

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
                ReleaseCurrentMovie();

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
            bool hadPresentation =
                _movie != null ||
                _viewModel != null ||
                _screenViewModel != null ||
                _currentScreen != CoopSelectionScreen.None;
            bool hadCommanderDeploymentPresentation =
                _currentScreen == CoopSelectionScreen.CommanderDeployment ||
                _commanderDeploymentViewModel != null ||
                _commanderDeploymentOrderVm != null ||
                _commanderDeploymentOrderTroopPlacer != null ||
                _commanderDeploymentSpriteCategory != null;
            if (_gauntletLayer != null && _movie != null)
            {
                _gauntletLayer.ReleaseMovie(_movie);
                _movie = null;
            }

            if (hadCommanderDeploymentPresentation)
            {
                TryDeactivateNativeCommanderDeploymentPlacement(MissionScreen);
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

            if (!TryResolveCameraPreviewAgent(snapshot, out Agent previewAgent))
            {
                ClearCameraPreviewTarget("camera-preview-target-missing");
                return;
            }

            ScreenBase missionScreen = MissionScreen;
            if (missionScreen == null || !TrySetMissionScreenPreviewFollowTarget(missionScreen, previewAgent))
            {
                ClearCameraPreviewTarget("camera-preview-screen-unavailable");
                return;
            }

            SetActiveCameraPreviewTarget(previewAgent, snapshot.SelectedEntryId);
            LogCameraPreviewState(
                "focus:" + previewAgent.Index + ":" + snapshot.SelectedEntryId,
                "focused camera preview on selected live unit. " +
                "AgentIndex=" + previewAgent.Index +
                " EntryId=" + snapshot.SelectedEntryId +
                " Side=" + snapshot.EffectiveSide);
        }

        private bool TryResolveCameraPreviewAgent(CoopSelectionUiSnapshot snapshot, out Agent previewAgent)
        {
            previewAgent = null;
            Mission mission = Mission;
            if (snapshot == null ||
                mission?.AllAgents == null ||
                snapshot.EffectiveSide == BattleSideEnum.None ||
                string.IsNullOrWhiteSpace(snapshot.SelectedEntryId))
            {
                return false;
            }

            for (int agentIndex = 0; agentIndex < mission.AllAgents.Count; agentIndex++)
            {
                Agent candidate = mission.AllAgents[agentIndex];
                if (!IsCameraPreviewCandidate(candidate, snapshot.EffectiveSide))
                    continue;

                if (!CoopMissionSpawnLogic.TryResolveSelectableEntryId(candidate, out string candidateEntryId) ||
                    !string.Equals(candidateEntryId, snapshot.SelectedEntryId, StringComparison.OrdinalIgnoreCase))
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
                return _activeCameraPreviewAgentIndex >= 0;

            if (!followedAgent.IsActive())
                return false;

            return TryGetActiveCameraPreviewAgent(out Agent previewAgent) &&
                   previewAgent != null &&
                   previewAgent.Index == followedAgent.Index;
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

            if (TrySetMissionScreenLastFollowedAgent(missionScreen, agent))
            {
                TrySetInstanceProperty(missionScreen, "LastFollowedAgentVisuals", null);
                return true;
            }

            return TrySetMissionScreenAgentToFollowOverride(missionScreen, agent);
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

        private void TryHandleStartBattleHotkey(float dt, bool hasLocalControlledAgent)
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return;

            _startBattleHotkeyCooldown -= dt;
            if (_startBattleHotkeyCooldown > 0f || !hasLocalControlledAgent || !Input.IsKeyPressed(InputKey.H))
                return;

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot = CoopBattleEntryStatusBridgeFile.ReadStatus();
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

            if (CoopBattlePhaseBridgeFile.WriteStartBattleRequest("Battle-map client H hotkey via CoopMissionSelectionView"))
            {
                _startBattleHotkeyCooldown = StartBattleHotkeyCooldownSeconds;
                InformationManager.DisplayMessage(new InformationMessage("Coop Battle: start requested"));
                ModLogger.Info("CoopMissionSelectionView: wrote start battle request from H hotkey.");
            }
        }

        private void TryShowStartBattleInstruction(bool hasLocalControlledAgent)
        {
            if (_startBattleInstructionShown || !hasLocalControlledAgent || !GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return;

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot = CoopBattleEntryStatusBridgeFile.ReadStatus();
            bool canStartBattleNow = snapshot != null && snapshot.CanStartBattle;
            if (!canStartBattleNow)
                return;

            _startBattleInstructionShown = true;
            InformationManager.DisplayMessage(new InformationMessage("Coop Battle: press H to start the battle."));
            ModLogger.Info("CoopMissionSelectionView: showed one-shot start battle instruction for local host-controlled peer.");
        }

        private void TryHandleReopenSelectionHotkey(float dt, bool hasLocalControlledAgent)
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive)
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
            TryInvokeInstanceMethod(missionScreen, "SetCameraLockState", true);
            TrySetInstanceProperty(missionScreen, "LockCameraMovement", true);
        }

        internal static void LogMissionScreenOverlayDiagnostics(ScreenBase missionScreen, string source)
        {
            if (missionScreen == null)
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
