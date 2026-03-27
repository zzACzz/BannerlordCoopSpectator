using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.ScreenSystem;

namespace CoopSpectator.UI
{
    public sealed class CoopMissionSelectionView : MissionView
    {
        private const string MovieName = "CoopSelection";
        private const float RefreshIntervalSeconds = 0.15f;
        private const float InitialOverlayDelaySeconds = 0.75f;

        private GauntletLayer _gauntletLayer;
        private GauntletMovieIdentifier _movie;
        private CoopSelectionVM _viewModel;
        private float _refreshTimer;
        private float _overlayStartupDelay = InitialOverlayDelaySeconds;
        private bool _overlayLoadFailed;
        private bool _inputCaptured;

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
            ModLogger.Info("CoopMissionSelectionView: OnMissionScreenInitialize, overlay init deferred.");
        }

        public override void OnMissionScreenTick(float dt)
        {
            base.OnMissionScreenTick(dt);

            if (_viewModel == null)
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
            RefreshViewModel(force: false);
            UpdateOverlayInputState();
        }

        public override void OnMissionScreenFinalize()
        {
            try
            {
                ReleaseOverlayInput();

                if (_gauntletLayer != null)
                {
                    if (_movie != null)
                    {
                        _gauntletLayer.ReleaseMovie(_movie);
                        _movie = null;
                    }

                    ScreenBase missionScreen = MissionScreen;
                    missionScreen?.RemoveLayer(_gauntletLayer);
                    _gauntletLayer = null;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: finalize failed: " + ex.Message);
            }

            _viewModel?.OnFinalize();
            _viewModel = null;
            base.OnMissionScreenFinalize();
        }

        private void TryEnsureLayer()
        {
            if (_viewModel != null || !GameNetwork.IsClient || !ExperimentalFeatures.EnableCustomCoopSelectionOverlay)
                return;

            try
            {
                ScreenBase missionScreen = MissionScreen;
                string missionScreenName = missionScreen?.GetType().FullName ?? "<null>";
                if (missionScreen == null)
                {
                    ModLogger.Info("CoopMissionSelectionView: mission screen is null, delaying overlay init.");
                    return;
                }

                if (missionScreenName.IndexOf("MissionScreen", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    ModLogger.Info("CoopMissionSelectionView: screen is not MissionScreen yet (" + missionScreenName + "), delaying overlay init.");
                    return;
                }

                ModLogger.Info("CoopMissionSelectionView: initializing overlay on " + missionScreenName + ".");
                _gauntletLayer = new GauntletLayer("CoopSelectionLayer", ViewOrderPriority, false);
                _gauntletLayer.IsFocusLayer = true;
                missionScreen.AddLayer(_gauntletLayer);
                ModLogger.Info("CoopMissionSelectionView: layer added.");

                if (!ExperimentalFeatures.EnableCustomCoopSelectionMovieLoad)
                {
                    _viewModel = new CoopSelectionVM();
                    RefreshViewModel(force: true);
                    UpdateOverlayInputState();
                    ModLogger.Info("CoopMissionSelectionView: movie load is disabled by feature flag; empty gauntlet layer initialized for crash isolation.");
                    return;
                }

                _viewModel = new CoopSelectionVM();
                ModLogger.Info("CoopMissionSelectionView: loading movie " + MovieName + ".");
                _movie = _gauntletLayer.LoadMovie(MovieName, _viewModel);
                ModLogger.Info("CoopMissionSelectionView: movie loaded.");
                RefreshViewModel(force: true);
                UpdateOverlayInputState();
                ModLogger.Info("CoopMissionSelectionView: custom overlay initialized.");
            }
            catch (Exception ex)
            {
                _overlayLoadFailed = true;
                ModLogger.Error("CoopMissionSelectionView: overlay init failed.", ex);
                CleanupLayerState();
            }
        }

        private void RefreshViewModel(bool force)
        {
            _viewModel.RefreshFromRuntime(force, HasLocalControlledAgent());
        }

        private void UpdateOverlayInputState()
        {
            bool shouldCaptureInput = _viewModel != null && _viewModel.IsVisible && !HasLocalControlledAgent();
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
                ModLogger.Info("CoopMissionSelectionView: enabled interactive input mode for custom overlay.");
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

                ModLogger.Info("CoopMissionSelectionView: released interactive input mode for custom overlay.");
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

                if (_gauntletLayer != null)
                {
                    if (_movie != null)
                    {
                        _gauntletLayer.ReleaseMovie(_movie);
                        _movie = null;
                    }

                    MissionScreen?.RemoveLayer(_gauntletLayer);
                    _gauntletLayer = null;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionSelectionView: cleanup after failed init also failed: " + ex.Message);
            }

            _viewModel?.OnFinalize();
            _viewModel = null;
        }

        private static bool HasLocalControlledAgent()
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

        private static void TrySetLayerActiveState(ScreenLayer layer, bool isActive)
        {
            if (layer == null)
                return;

            try
            {
                PropertyInfo property = layer.GetType().GetProperty(
                    "IsActive",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo setter = property?.GetSetMethod(true);
                setter?.Invoke(layer, new object[] { isActive });
            }
            catch
            {
            }
        }

        private static void TryInvokeLayerFocusCallback(ScreenLayer layer, string methodName)
        {
            if (layer == null || string.IsNullOrWhiteSpace(methodName))
                return;

            try
            {
                MethodInfo method = layer.GetType().GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                method?.Invoke(layer, Array.Empty<object>());
            }
            catch
            {
            }
        }

        private static void ApplyMissionScreenOverlayMode(ScreenBase missionScreen, bool isOverlayActive)
        {
            if (missionScreen == null)
                return;

            TryInvokeInstanceMethod(missionScreen, "SetDisplayDialog", isOverlayActive);
            TryInvokeInstanceMethod(missionScreen, "SetCameraLockState", isOverlayActive);
            TrySetInstanceProperty(missionScreen, "LockCameraMovement", isOverlayActive);
        }

        private static void LogMissionScreenOverlayDiagnostics(ScreenBase missionScreen, string source)
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

        private static void TrySetScreenManagerFocus(ScreenLayer layer)
        {
            if (layer == null)
                return;

            try
            {
                ScreenManager.TrySetFocus(layer);
            }
            catch
            {
            }
        }

        private static void TryLoseScreenManagerFocus(ScreenLayer layer)
        {
            if (layer == null)
                return;

            try
            {
                ScreenManager.TryLoseFocus(layer);
            }
            catch
            {
            }
        }

        private static void TrySetScreenManagerMouseVisibility(bool isVisible)
        {
            try
            {
                MethodInfo method = typeof(ScreenManager).GetMethod(
                    "SetMouseVisible",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(bool) },
                    modifiers: null);
                method?.Invoke(null, new object[] { isVisible });
            }
            catch
            {
            }
        }

        private static void TryInvokeInstanceMethod(object target, string methodName, params object[] arguments)
        {
            if (target == null || string.IsNullOrWhiteSpace(methodName))
                return;

            try
            {
                Type targetType = target.GetType();
                Type[] argumentTypes = arguments?.Select(argument => argument?.GetType() ?? typeof(object)).ToArray() ?? Type.EmptyTypes;
                MethodInfo method = targetType.GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: argumentTypes,
                    modifiers: null);
                method?.Invoke(target, arguments);
            }
            catch
            {
            }
        }

        private static void TrySetInstanceProperty(object target, string propertyName, object value)
        {
            if (target == null || string.IsNullOrWhiteSpace(propertyName))
                return;

            try
            {
                PropertyInfo property = target.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo setter = property?.GetSetMethod(true);
                setter?.Invoke(target, new[] { value });
            }
            catch
            {
            }
        }

        private static T? TryGetInstanceProperty<T>(object target, string propertyName) where T : struct
        {
            if (target == null || string.IsNullOrWhiteSpace(propertyName))
                return null;

            try
            {
                PropertyInfo property = target.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object value = property?.GetValue(target);
                if (value is T typed)
                    return typed;
            }
            catch
            {
            }

            return null;
        }
    }

    public sealed class CoopSelectionVM : ViewModel
    {
        private MBBindingList<CoopSelectionSideItemVM> _sides = new MBBindingList<CoopSelectionSideItemVM>();
        private MBBindingList<CoopSelectionUnitItemVM> _units = new MBBindingList<CoopSelectionUnitItemVM>();
        private bool _isVisible = true;
        private bool _hasUnits;
        private bool _showEmptyUnitsText = true;
        private bool _canSpawn;
        private bool _canReset;
        private bool _canStartBattle;
        private string _panelTitle = "Coop Battle Selection";
        private string _panelSubtitle = "Очікування даних місії...";
        private string _battlePhaseText = string.Empty;
        private string _lifecycleText = string.Empty;
        private string _selectionText = string.Empty;
        private string _statusText = string.Empty;
        private string _hintText = "Side -> Unit -> Spawn.";
        private string _emptyUnitsText = "Немає доступних юнітів.";

        [DataSourceProperty] public MBBindingList<CoopSelectionSideItemVM> Sides { get => _sides; private set => SetField(ref _sides, value, nameof(Sides)); }
        [DataSourceProperty] public MBBindingList<CoopSelectionUnitItemVM> Units { get => _units; private set => SetField(ref _units, value, nameof(Units)); }
        [DataSourceProperty] public bool IsVisible { get => _isVisible; private set => SetField(ref _isVisible, value, nameof(IsVisible)); }
        [DataSourceProperty] public bool HasUnits { get => _hasUnits; private set => SetField(ref _hasUnits, value, nameof(HasUnits)); }
        [DataSourceProperty] public bool ShowEmptyUnitsText { get => _showEmptyUnitsText; private set => SetField(ref _showEmptyUnitsText, value, nameof(ShowEmptyUnitsText)); }
        [DataSourceProperty] public bool CanSpawn { get => _canSpawn; private set => SetField(ref _canSpawn, value, nameof(CanSpawn)); }
        [DataSourceProperty] public bool CanReset { get => _canReset; private set => SetField(ref _canReset, value, nameof(CanReset)); }
        [DataSourceProperty] public bool CanStartBattle { get => _canStartBattle; private set => SetField(ref _canStartBattle, value, nameof(CanStartBattle)); }
        [DataSourceProperty] public string PanelTitle { get => _panelTitle; private set => SetField(ref _panelTitle, value, nameof(PanelTitle)); }
        [DataSourceProperty] public string PanelSubtitle { get => _panelSubtitle; private set => SetField(ref _panelSubtitle, value, nameof(PanelSubtitle)); }
        [DataSourceProperty] public string BattlePhaseText { get => _battlePhaseText; private set => SetField(ref _battlePhaseText, value, nameof(BattlePhaseText)); }
        [DataSourceProperty] public string LifecycleText { get => _lifecycleText; private set => SetField(ref _lifecycleText, value, nameof(LifecycleText)); }
        [DataSourceProperty] public string SelectionText { get => _selectionText; private set => SetField(ref _selectionText, value, nameof(SelectionText)); }
        [DataSourceProperty] public string StatusText { get => _statusText; private set => SetField(ref _statusText, value, nameof(StatusText)); }
        [DataSourceProperty] public string HintText { get => _hintText; private set => SetField(ref _hintText, value, nameof(HintText)); }
        [DataSourceProperty] public string EmptyUnitsText { get => _emptyUnitsText; private set => SetField(ref _emptyUnitsText, value, nameof(EmptyUnitsText)); }

        public void RefreshFromRuntime(bool force, bool hasLocalControlledAgent)
        {
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status = CoopBattleEntryStatusBridgeFile.ReadStatus();
            CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot currentSelection = CoopBattleSelectionBridgeFile.ReadCurrentSelection();
            BattleRuntimeState battleState = BattleSnapshotRuntimeState.GetState();
            BattleSideEnum effectiveSide = ResolveEffectiveSide(status, currentSelection);
            string[] attackerIds = ResolveAllowedSelectionIds(status, BattleSideEnum.Attacker);
            string[] defenderIds = ResolveAllowedSelectionIds(status, BattleSideEnum.Defender);
            if (effectiveSide == BattleSideEnum.None)
                effectiveSide = attackerIds.Length > 0 ? BattleSideEnum.Attacker : defenderIds.Length > 0 ? BattleSideEnum.Defender : BattleSideEnum.None;

            string selectedSelectionId = ResolveEffectiveSelectionId(status, currentSelection, effectiveSide);
            string battlePhase = !string.IsNullOrWhiteSpace(status?.BattlePhase)
                ? status.BattlePhase
                : CoopBattlePhaseBridgeFile.ReadStatus()?.Phase.ToString() ?? "Unknown";
            string lifecycle = status?.LifecycleState ?? "Unknown";

            PanelSubtitle = BuildPanelSubtitle(status, battleState, effectiveSide, attackerIds.Length, defenderIds.Length);
            BattlePhaseText = "Фаза: " + battlePhase;
            LifecycleText = "Життєвий цикл: " + lifecycle;
            SelectionText = "Вибір: " + FormatSideLabel(effectiveSide) + " / " + ResolveSelectionDisplayLabel(effectiveSide, selectedSelectionId);
            StatusText = BuildStatusText(status);
            HintText = "Вибери сторону, потім юніта, далі Spawn. Reset повертає у selection.";
            EmptyUnitsText = effectiveSide == BattleSideEnum.None
                ? "Спершу вибери сторону."
                : "Немає доступних юнітів для сторони " + FormatSideLabel(effectiveSide) + ".";
            IsVisible = ShouldOverlayBeVisible(status, battlePhase, lifecycle, hasLocalControlledAgent);
            CanSpawn = (status?.CanRespawn ?? true) && !string.IsNullOrWhiteSpace(selectedSelectionId);
            CanReset = status?.HasAgent == true || string.Equals(lifecycle, "Alive", StringComparison.OrdinalIgnoreCase);
            CanStartBattle = status?.CanStartBattle == true;

            Sides = BuildSideItems(battleState, effectiveSide, attackerIds, defenderIds);
            Units = BuildUnitItems(status, effectiveSide, selectedSelectionId);
            HasUnits = Units.Count > 0;
            ShowEmptyUnitsText = !HasUnits;

            if (force)
            {
                OnPropertyChanged(nameof(Sides));
                OnPropertyChanged(nameof(Units));
            }
        }

        public void ExecuteSpawn()
        {
            if (CanSpawn)
                CoopBattleSpawnBridgeFile.WriteSpawnNowRequest("CoopSelectionUI Spawn");
        }

        public void ExecuteReset()
        {
            CoopBattleSpawnBridgeFile.WriteForceRespawnableRequest("CoopSelectionUI Reset");
        }

        public void ExecuteStartBattle()
        {
            if (CanStartBattle)
                CoopBattlePhaseBridgeFile.WriteStartBattleRequest("CoopSelectionUI StartBattle");
        }

        private MBBindingList<CoopSelectionSideItemVM> BuildSideItems(
            BattleRuntimeState battleState,
            BattleSideEnum effectiveSide,
            string[] attackerIds,
            string[] defenderIds)
        {
            var result = new MBBindingList<CoopSelectionSideItemVM>();
            result.Add(BuildSideItem(BattleSideEnum.Attacker, attackerIds, effectiveSide, battleState));
            result.Add(BuildSideItem(BattleSideEnum.Defender, defenderIds, effectiveSide, battleState));
            return result;
        }

        private CoopSelectionSideItemVM BuildSideItem(
            BattleSideEnum side,
            string[] allowedIds,
            BattleSideEnum effectiveSide,
            BattleRuntimeState battleState)
        {
            BattleSideState sideState = null;
            battleState?.SidesByKey?.TryGetValue(side.ToString(), out sideState);
            int available = allowedIds?.Length ?? 0;
            int totalMen = sideState?.TotalManCount ?? available;
            bool isSelected = side == effectiveSide;
            return new CoopSelectionSideItemVM(
                side.ToString(),
                (isSelected ? "● " : string.Empty) + FormatSideLabel(side),
                "Доступно: " + available + " | Бійців: " + totalMen,
                available > 0,
                SelectSide);
        }

        private MBBindingList<CoopSelectionUnitItemVM> BuildUnitItems(
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status,
            BattleSideEnum effectiveSide,
            string selectedSelectionId)
        {
            var result = new MBBindingList<CoopSelectionUnitItemVM>();
            if (effectiveSide == BattleSideEnum.None)
                return result;

            foreach (string selectionId in ResolveAllowedSelectionIds(status, effectiveSide))
            {
                RosterEntryState entryState = ResolveEntryState(effectiveSide, selectionId);
                bool isSelected = IsSelectionMatch(selectionId, selectedSelectionId, entryState);
                result.Add(new CoopSelectionUnitItemVM(
                    effectiveSide.ToString(),
                    selectionId,
                    (isSelected ? "● " : string.Empty) + ResolveEntryDisplayName(entryState, selectionId),
                    ResolveEntryDetailText(entryState),
                    ResolveCountText(entryState),
                    SelectUnit));
            }

            return result;
        }

        private void SelectSide(string sideKey)
        {
            if (!string.IsNullOrWhiteSpace(sideKey))
                CoopBattleSelectionBridgeFile.WriteSelectSideRequest(sideKey, "CoopSelectionUI Side");
        }

        private void SelectUnit(string sideKey, string selectionId)
        {
            if (string.IsNullOrWhiteSpace(selectionId))
                return;

            if (!string.IsNullOrWhiteSpace(sideKey))
                CoopBattleSelectionBridgeFile.WriteSelectSideRequest(sideKey, "CoopSelectionUI UnitSide");

            CoopBattleSelectionBridgeFile.WriteSelectTroopRequest(selectionId, "CoopSelectionUI Unit");
        }

        private static BattleSideEnum ResolveEffectiveSide(
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status,
            CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot currentSelection)
        {
            return ParseBattleSide(
                currentSelection?.Side ??
                status?.AssignedSide ??
                status?.RequestedSide ??
                status?.IntentSide);
        }

        private static string ResolveEffectiveSelectionId(
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status,
            CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot currentSelection,
            BattleSideEnum effectiveSide)
        {
            return currentSelection?.TroopOrEntryId ??
                   status?.SelectedEntryId ??
                   status?.SpawnRequestEntryId ??
                   status?.SelectionRequestEntryId ??
                   status?.SelectedTroopId ??
                   status?.SpawnRequestTroopId ??
                   status?.SelectionRequestTroopId ??
                   ResolveAllowedSelectionIds(status, effectiveSide).FirstOrDefault();
        }

        private static string[] ResolveAllowedSelectionIds(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status, BattleSideEnum side)
        {
            if (status == null)
                return Array.Empty<string>();

            string rawEntryIds =
                side == BattleSideEnum.Attacker ? status.AttackerAllowedEntryIds :
                side == BattleSideEnum.Defender ? status.DefenderAllowedEntryIds :
                status.AllowedEntryIds;
            string[] entryIds = CoopBattleEntryStatusBridgeFile.DeserializeIdList(rawEntryIds)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (entryIds.Length > 0)
                return entryIds;

            string rawTroopIds =
                side == BattleSideEnum.Attacker ? status.AttackerAllowedTroopIds :
                side == BattleSideEnum.Defender ? status.DefenderAllowedTroopIds :
                status.AllowedTroopIds;
            return CoopBattleEntryStatusBridgeFile.DeserializeIdList(rawTroopIds)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string BuildPanelSubtitle(
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status,
            BattleRuntimeState battleState,
            BattleSideEnum effectiveSide,
            int attackerCount,
            int defenderCount)
        {
            string missionName = status?.MissionName;
            if (string.IsNullOrWhiteSpace(missionName))
                missionName = battleState?.Snapshot?.BattleId ?? "unknown";

            return "Mission: " + missionName +
                   " | Поточна сторона: " + FormatSideLabel(effectiveSide) +
                   " | Attacker=" + attackerCount +
                   " | Defender=" + defenderCount;
        }

        private static string BuildStatusText(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status)
        {
            if (status == null)
                return "Статус ще не отримано.";

            return "Spawn=" + (status.SpawnStatus ?? "none") +
                   " | Reason=" + (status.SpawnReason ?? "none") +
                   " | HasAgent=" + status.HasAgent +
                   " | CanRespawn=" + status.CanRespawn +
                   " | CanStartBattle=" + status.CanStartBattle +
                   " | Deaths=" + status.DeathCount;
        }

        private static bool ShouldOverlayBeVisible(
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status,
            string battlePhase,
            string lifecycle,
            bool hasLocalControlledAgent)
        {
            if (hasLocalControlledAgent)
                return false;

            if (status == null)
                return true;

            if (string.Equals(battlePhase, nameof(CoopBattlePhase.BattleEnded), StringComparison.OrdinalIgnoreCase))
                return false;

            if (status.HasAgent || string.Equals(lifecycle, "Alive", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!status.HasAgent || status.CanRespawn)
                return true;

            if (string.Equals(lifecycle, "Respawnable", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lifecycle, "SpawnQueued", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lifecycle, "DeadAwaitingRespawn", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lifecycle, "AwaitingSelection", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lifecycle, "NoSide", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lifecycle, "Waiting", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static BattleSideEnum ParseBattleSide(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return BattleSideEnum.None;

            if (Enum.TryParse(raw, true, out BattleSideEnum parsed))
                return parsed;

            switch (raw.Trim().ToLowerInvariant())
            {
                case "attacker":
                case "attackers":
                case "1":
                    return BattleSideEnum.Attacker;
                case "defender":
                case "defenders":
                case "2":
                    return BattleSideEnum.Defender;
                default:
                    return BattleSideEnum.None;
            }
        }

        private static string FormatSideLabel(BattleSideEnum side)
        {
            switch (side)
            {
                case BattleSideEnum.Attacker:
                    return "Атакуючі";
                case BattleSideEnum.Defender:
                    return "Захисники";
                default:
                    return "Не вибрано";
            }
        }

        private static string ResolveSelectionDisplayLabel(BattleSideEnum side, string selectionId)
        {
            if (string.IsNullOrWhiteSpace(selectionId))
                return "не вибрано";

            return ResolveEntryDisplayName(ResolveEntryState(side, selectionId), selectionId);
        }

        private static RosterEntryState ResolveEntryState(BattleSideEnum side, string selectionId)
        {
            if (string.IsNullOrWhiteSpace(selectionId))
                return null;

            RosterEntryState directEntry = BattleSnapshotRuntimeState.GetEntryState(selectionId);
            if (directEntry != null)
                return directEntry;

            BattleSideState sideState = BattleSnapshotRuntimeState.GetSideState(side.ToString());
            return sideState?.Entries?.FirstOrDefault(entry =>
                string.Equals(entry.EntryId, selectionId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.CharacterId, selectionId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.OriginalCharacterId, selectionId, StringComparison.OrdinalIgnoreCase));
        }

        private static string ResolveEntryDisplayName(RosterEntryState entryState, string fallbackId)
        {
            return entryState?.TroopName ??
                   entryState?.OriginalCharacterId ??
                   entryState?.CharacterId ??
                   fallbackId ??
                   "unknown";
        }

        private static string ResolveEntryDetailText(RosterEntryState entryState)
        {
            if (entryState == null)
                return "Немає snapshot-даних для цього юніта.";

            List<string> parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(entryState.PartyId))
                parts.Add(entryState.PartyId);
            if (entryState.IsHero)
                parts.Add("hero");
            if (entryState.IsMounted)
                parts.Add("mounted");
            if (entryState.IsRanged)
                parts.Add("ranged");
            if (entryState.HasShield)
                parts.Add("shield");
            if (entryState.Tier > 0)
                parts.Add("tier " + entryState.Tier);
            return parts.Count > 0 ? string.Join(" | ", parts) : "Звичайний боєць";
        }

        private static string ResolveCountText(RosterEntryState entryState)
        {
            if (entryState == null)
                return string.Empty;

            int ready = Math.Max(0, entryState.Count - entryState.WoundedCount);
            return "x" + ready;
        }

        private static bool IsSelectionMatch(string selectionId, string selectedSelectionId, RosterEntryState entryState)
        {
            if (string.IsNullOrWhiteSpace(selectionId) || string.IsNullOrWhiteSpace(selectedSelectionId))
                return false;

            return string.Equals(selectionId, selectedSelectionId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(entryState?.EntryId, selectedSelectionId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(entryState?.CharacterId, selectedSelectionId, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(entryState?.OriginalCharacterId, selectedSelectionId, StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class CoopSelectionSideItemVM : ViewModel
    {
        private readonly string _sideKey;
        private readonly Action<string> _onSelect;

        public CoopSelectionSideItemVM(string sideKey, string titleText, string detailText, bool isEnabled, Action<string> onSelect)
        {
            _sideKey = sideKey;
            _onSelect = onSelect;
            TitleText = titleText;
            DetailText = detailText;
            IsEnabled = isEnabled;
        }

        [DataSourceProperty] public string TitleText { get; }
        [DataSourceProperty] public string DetailText { get; }
        [DataSourceProperty] public bool IsEnabled { get; }

        public void ExecuteSelect()
        {
            if (IsEnabled)
                _onSelect?.Invoke(_sideKey);
        }
    }

    public sealed class CoopSelectionUnitItemVM : ViewModel
    {
        private readonly string _sideKey;
        private readonly string _selectionId;
        private readonly Action<string, string> _onSelect;

        public CoopSelectionUnitItemVM(
            string sideKey,
            string selectionId,
            string titleText,
            string detailText,
            string countText,
            Action<string, string> onSelect)
        {
            _sideKey = sideKey;
            _selectionId = selectionId;
            _onSelect = onSelect;
            TitleText = titleText;
            DetailText = detailText;
            CountText = countText;
        }

        [DataSourceProperty] public string TitleText { get; }
        [DataSourceProperty] public string DetailText { get; }
        [DataSourceProperty] public string CountText { get; }

        public void ExecuteSelect()
        {
            _onSelect?.Invoke(_sideKey, _selectionId);
        }
    }
}
