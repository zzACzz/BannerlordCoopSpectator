using System;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.ScreenSystem;

namespace CoopSpectator.UI
{
    public sealed class CoopMissionSelectionView : MissionView
    {
        private const string TeamMovieName = "CoopTeamSelection";
        private const string ClassMovieName = "CoopClassLoadout";
        private const float RefreshIntervalSeconds = 0.15f;
        private const float InitialOverlayDelaySeconds = 0.75f;
        private const float StartBattleHotkeyCooldownSeconds = 0.2f;
        private const float ReopenSelectionHotkeyCooldownSeconds = 0.2f;
        private static readonly TimeSpan LocalSpawnOverlaySuppressionDuration = TimeSpan.FromSeconds(2.5);
        private static readonly TimeSpan LocalSpawnPendingTimeout = TimeSpan.FromSeconds(20);
        private static readonly TimeSpan LocalSpawnPendingResendInterval = TimeSpan.FromSeconds(1.5);
        private const int LocalSpawnPendingMaxRequestAttempts = 8;
        private static int _activeCameraPreviewAgentIndex = -1;
        private static string _activeCameraPreviewEntryId = string.Empty;

        private GauntletLayer _gauntletLayer;
        private GauntletMovieIdentifier _movie;
        private ViewModel _viewModel;
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
            _overlayStartupDelay = InitialOverlayDelaySeconds;
            _hadLocalControlledAgent = HasLocalControlledAgent();
            _startBattleInstructionShown = false;
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
            base.OnMissionScreenFinalize();
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

            CoopSelectionUiSnapshot snapshot = CoopSelectionUiHelpers.BuildSnapshot(
                _selectedSideOverride,
                _selectedEntryIdOverride,
                hasLocalControlledAgent);
            CoopSelectionScreen desiredScreen = DetermineDesiredScreen(snapshot);
            if (desiredScreen == CoopSelectionScreen.None)
            {
                ReleaseCurrentMovie();
                ClearCameraPreviewTarget("overlay-hidden");
                UpdateOverlayInputState(false);
                return;
            }

            bool loadedNewScreen = EnsureScreenLoaded(snapshot, desiredScreen);
            string refreshKey = GetRefreshKey(snapshot, desiredScreen);
            bool needsRefresh = force || loadedNewScreen || !string.Equals(_lastAppliedRefreshKey, refreshKey, StringComparison.Ordinal);
            if (needsRefresh && !loadedNewScreen)
                _screenViewModel?.Refresh(snapshot, force);

            if (needsRefresh)
                _lastAppliedRefreshKey = refreshKey;

            UpdateCameraPreviewTarget(snapshot, desiredScreen, hasLocalControlledAgent);
            UpdateOverlayInputState(true);
        }

        private CoopSelectionScreen DetermineDesiredScreen(CoopSelectionUiSnapshot snapshot)
        {
            if (snapshot == null || !snapshot.CanShowOverlay || _spectatorOverlayHidden)
                return CoopSelectionScreen.None;

            if (ShouldKeepOverlaySuppressedWhileAwaitingLocalSpawn(snapshot))
                return CoopSelectionScreen.None;

            if (DateTime.UtcNow < _overlaySuppressedUntilUtc)
                return CoopSelectionScreen.None;

            if (!snapshot.BattleDataReady)
                return CoopSelectionScreen.TeamSelection;

            if (_requestedScreen == CoopSelectionScreen.ClassLoadout &&
                _selectedSideOverride != BattleSideEnum.None &&
                snapshot.EffectiveSide == _selectedSideOverride &&
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

        private bool EnsureScreenLoaded(CoopSelectionUiSnapshot snapshot, CoopSelectionScreen desiredScreen)
        {
            if (_currentScreen == desiredScreen && _screenViewModel != null && _viewModel != null)
                return false;

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

            var classVm = new CoopClassLoadoutVM(snapshot, HandleUnitSelected, HandleSpawnRequested, HandleBackRequested);
            _viewModel = classVm;
            _screenViewModel = classVm;
            _movie = _gauntletLayer.LoadMovie(ClassMovieName, classVm);
            _currentScreen = desiredScreen;
            _lastAppliedRefreshKey = GetRefreshKey(snapshot, desiredScreen);
            ModLogger.Info("CoopMissionSelectionView: loaded coop class loadout shell.");
            return true;
        }

        private void HandleSideSelected(BattleSideEnum side)
        {
            if (side == BattleSideEnum.None)
                return;

            _spectatorOverlayHidden = false;
            _selectedSideOverride = side;
            _selectedEntryIdOverride = null;
            _requestedScreen = CoopSelectionScreen.ClassLoadout;
            CoopBattleNetworkRequestTransport.TrySelectSide(side, "CoopTeamSelectionUI Side");
            RefreshOverlay(force: true, HasLocalControlledAgent());
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
            CoopSelectionUiSnapshot snapshot = CoopSelectionUiHelpers.BuildSnapshot(
                _selectedSideOverride,
                _selectedEntryIdOverride,
                hasLocalControlledAgent);
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
            CoopSelectionUiSnapshot snapshot = CoopSelectionUiHelpers.BuildSnapshot(
                _selectedSideOverride,
                _selectedEntryIdOverride,
                hasLocalControlledAgent);
            if (snapshot == null || !snapshot.CanSpawn || snapshot.EffectiveSide == BattleSideEnum.None || string.IsNullOrWhiteSpace(snapshot.SelectedEntryId))
                return;

            _selectedSideOverride = snapshot.EffectiveSide;
            _selectedEntryIdOverride = snapshot.SelectedEntryId;
            _spectatorOverlayHidden = false;
            MarkLocalSpawnPending(snapshot);
            _overlaySuppressedUntilUtc = DateTime.UtcNow + LocalSpawnOverlaySuppressionDuration;
            if (!TrySendPendingSpawnRequests("CoopClassLoadoutUI Spawn", includeSelectEntry: true))
                ClearLocalSpawnPending("spawn-request-write-failed");
            RefreshOverlay(force: true, hasLocalControlledAgent);
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

        private void UpdateOverlayInputState(bool shouldCaptureInput)
        {
            if (_gauntletLayer == null || shouldCaptureInput == _inputCaptured)
                return;

            ScreenBase missionScreen = MissionScreen;
            if (shouldCaptureInput)
            {
                TrySetLayerActiveState(_gauntletLayer, true);
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
                return;
            }

            ReleaseOverlayInput();
        }

        private void ReleaseOverlayInput()
        {
            if (_gauntletLayer == null && !_inputCaptured)
                return;

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
                _inputCaptured = false;
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
            if (_gauntletLayer != null && _movie != null)
            {
                _gauntletLayer.ReleaseMovie(_movie);
                _movie = null;
            }

            _viewModel?.OnFinalize();
            _viewModel = null;
            _screenViewModel = null;
            _currentScreen = CoopSelectionScreen.None;
            _lastAppliedRefreshKey = string.Empty;
            ClearCameraPreviewTarget("release-movie");
        }

        private void UpdateCameraPreviewTarget(
            CoopSelectionUiSnapshot snapshot,
            CoopSelectionScreen desiredScreen,
            bool hasLocalControlledAgent)
        {
            if (!GameNetwork.IsClient ||
                hasLocalControlledAgent ||
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
                return desiredScreen == CoopSelectionScreen.TeamSelection ? "team|null" : "class|null";

            return desiredScreen == CoopSelectionScreen.TeamSelection
                ? snapshot.TeamRefreshKey ?? string.Empty
                : snapshot.ClassRefreshKey ?? string.Empty;
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
        ClassLoadout = 2
    }

    internal interface ICoopSelectionScreenViewModel
    {
        void Refresh(CoopSelectionUiSnapshot snapshot, bool force);
    }
}
