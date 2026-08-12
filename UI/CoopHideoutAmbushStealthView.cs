using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.Hideout;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Objects;
using TaleWorlds.MountAndBlade.View.MissionViews;

namespace CoopSpectator.UI
{
    public sealed class CoopHideoutAmbushStealthView : MissionView
    {
        private const string NativeAlarmMovieName = "AgentAlarmStateMissionView";
        private const string NativeFailCounterMovieName = "MissionStealthFailCounter";
        private const string ObjectiveMovieName = "CoopHideoutAmbushStealth";
        private const int UseGameKeyIndex = 13;

        private GauntletLayer _alarmLayer;
        private GauntletLayer _failCounterLayer;
        private GauntletLayer _objectiveLayer;
        private GauntletMovieIdentifier _alarmMovie;
        private GauntletMovieIdentifier _failCounterMovie;
        private GauntletMovieIdentifier _objectiveMovie;
        private CoopHideoutAmbushStealthVM _viewModel;
        private CoopHideoutAmbushFailCounterVM _failCounterViewModel;
        private UsableMissionObject _focusedCallTroopsUsePoint;

        public override void OnMissionScreenInitialize()
        {
            base.OnMissionScreenInitialize();
            ViewOrderPriority = 44;
            TryCreateLayers();
        }

        public override void OnMissionScreenTick(float dt)
        {
            base.OnMissionScreenTick(dt);
            if (!GameNetwork.IsClient || MissionScreen == null)
                return;

            if (_viewModel == null)
                TryCreateLayers();
            _viewModel?.Update(MissionScreen.CombatCamera);
            _failCounterViewModel?.Update(
                CoopHideoutAmbushNetworkController.CurrentClientState);

            if (_focusedCallTroopsUsePoint != null &&
                MissionScreen.SceneLayer.Input.IsGameKeyPressed(UseGameKeyIndex))
            {
                TrySendUsePointRequest(
                    Agent.Main,
                    _focusedCallTroopsUsePoint);
            }
        }

        public override void OnFocusGained(
            Agent agent,
            IFocusable focusableObject,
            bool isInteractable)
        {
            base.OnFocusGained(agent, focusableObject, isInteractable);
            if (!GameNetwork.IsClient ||
                agent != Agent.Main ||
                !isInteractable ||
                !(focusableObject is UsableMissionObject usePoint) ||
                !IsCallTroopsUsePoint(usePoint))
            {
                return;
            }

            _focusedCallTroopsUsePoint = usePoint;
        }

        public override void OnFocusLost(
            Agent agent,
            IFocusable focusableObject)
        {
            base.OnFocusLost(agent, focusableObject);
            if (agent == Agent.Main &&
                ReferenceEquals(
                    focusableObject,
                    _focusedCallTroopsUsePoint))
            {
                _focusedCallTroopsUsePoint = null;
            }
        }

        public override void OnObjectUsed(
            Agent userAgent,
            UsableMissionObject usedObject)
        {
            base.OnObjectUsed(userAgent, usedObject);
            TrySendUsePointRequest(userAgent, usedObject);
        }

        private static void TrySendUsePointRequest(
            Agent userAgent,
            UsableMissionObject usedObject)
        {
            CoopHideoutAmbushState state =
                CoopHideoutAmbushNetworkController.CurrentClientState;
            if (!GameNetwork.IsClient ||
                userAgent != Agent.Main ||
                state?.Phase != CoopHideoutAmbushPhase.Stealth ||
                !state.IsUsePointAvailable ||
                !IsCallTroopsUsePoint(usedObject))
            {
                return;
            }

            if (CoopHideoutAmbushNetworkController.SendUsePointRequest())
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    GameTexts.FindText("str_coop_hideout_call_troops_waiting").ToString()));
            }
        }

        public override void OnMissionScreenFinalize()
        {
            _focusedCallTroopsUsePoint = null;
            ReleaseLayers();
            base.OnMissionScreenFinalize();
        }

        private void TryCreateLayers()
        {
            if (_viewModel != null || MissionScreen == null)
                return;

            try
            {
                _viewModel = new CoopHideoutAmbushStealthVM();
                _failCounterViewModel = new CoopHideoutAmbushFailCounterVM();
                _alarmLayer = new GauntletLayer(
                    "CoopHideoutAmbushAlarmLayer",
                    ViewOrderPriority,
                    false);
                _failCounterLayer = new GauntletLayer(
                    "CoopHideoutAmbushFailCounterLayer",
                    10,
                    false);
                _objectiveLayer = new GauntletLayer(
                    "CoopHideoutAmbushObjectiveLayer",
                    ViewOrderPriority + 1,
                    false);
                MissionScreen.AddLayer(_alarmLayer);
                MissionScreen.AddLayer(_failCounterLayer);
                MissionScreen.AddLayer(_objectiveLayer);
                _alarmMovie = _alarmLayer.LoadMovie(
                    NativeAlarmMovieName,
                    _viewModel);
                _failCounterMovie = _failCounterLayer.LoadMovie(
                    NativeFailCounterMovieName,
                    _failCounterViewModel);
                _objectiveMovie = _objectiveLayer.LoadMovie(
                    ObjectiveMovieName,
                    _viewModel);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopHideoutAmbushStealthView: UI initialization failed. " +
                    "Error=" + ex.GetType().Name + ":" + ex.Message + ".");
                ReleaseLayers();
            }
        }

        private void ReleaseLayers()
        {
            try
            {
                if (_alarmLayer != null && _alarmMovie != null)
                    _alarmLayer.ReleaseMovie(_alarmMovie);
                if (_failCounterLayer != null && _failCounterMovie != null)
                    _failCounterLayer.ReleaseMovie(_failCounterMovie);
                if (_objectiveLayer != null && _objectiveMovie != null)
                    _objectiveLayer.ReleaseMovie(_objectiveMovie);
                if (_alarmLayer != null)
                    MissionScreen?.RemoveLayer(_alarmLayer);
                if (_failCounterLayer != null)
                    MissionScreen?.RemoveLayer(_failCounterLayer);
                if (_objectiveLayer != null)
                    MissionScreen?.RemoveLayer(_objectiveLayer);
                _viewModel?.OnFinalize();
                _failCounterViewModel?.OnFinalize();
            }
            catch
            {
            }
            finally
            {
                _alarmMovie = null;
                _failCounterMovie = null;
                _objectiveMovie = null;
                _alarmLayer = null;
                _failCounterLayer = null;
                _objectiveLayer = null;
                _viewModel = null;
                _failCounterViewModel = null;
            }
        }

        private static bool IsCallTroopsUsePoint(UsableMissionObject usedObject)
        {
            if (usedObject == null)
                return false;
            if (string.Equals(
                    usedObject.GetType().FullName,
                    CoopHideoutAmbushContract.StealthAreaUsePointTypeName,
                    StringComparison.Ordinal))
            {
                return true;
            }

            try
            {
                WeakGameEntity entity = usedObject.GameEntity;
                return entity.IsValid &&
                       string.Equals(
                           entity.Name,
                           "stealth_area_use_point",
                           StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    public sealed class CoopHideoutAmbushStealthVM : ViewModel
    {
        private readonly Dictionary<int, CoopHideoutAmbushAlarmTargetVM> _targetsByAgentIndex =
            new Dictionary<int, CoopHideoutAmbushAlarmTargetVM>();
        private readonly List<StealthBox> _stealthBoxes = new List<StealthBox>();
        private bool _stealthBoxesResolved;
        private bool _isMainAgentInSafeArea;
        private bool _isObjectiveVisible;
        private Vec2 _objectivePosition;
        private string _objectiveText = string.Empty;

        public CoopHideoutAmbushStealthVM()
        {
            Targets = new MBBindingList<CoopHideoutAmbushAlarmTargetVM>();
            ObjectiveText = GameTexts.FindText(
                "str_coop_hideout_ambush_objective").ToString();
        }

        [DataSourceProperty]
        public MBBindingList<CoopHideoutAmbushAlarmTargetVM> Targets { get; }

        [DataSourceProperty]
        public bool IsMainAgentInSafeArea
        {
            get => _isMainAgentInSafeArea;
            private set
            {
                if (value == _isMainAgentInSafeArea)
                    return;
                _isMainAgentInSafeArea = value;
                OnPropertyChangedWithValue(value, nameof(IsMainAgentInSafeArea));
            }
        }

        [DataSourceProperty]
        public bool IsObjectiveVisible
        {
            get => _isObjectiveVisible;
            private set
            {
                if (value == _isObjectiveVisible)
                    return;
                _isObjectiveVisible = value;
                OnPropertyChangedWithValue(value, nameof(IsObjectiveVisible));
            }
        }

        [DataSourceProperty]
        public Vec2 ObjectivePosition
        {
            get => _objectivePosition;
            private set
            {
                if (value == _objectivePosition)
                    return;
                _objectivePosition = value;
                OnPropertyChangedWithValue(value, nameof(ObjectivePosition));
            }
        }

        [DataSourceProperty]
        public string ObjectiveText
        {
            get => _objectiveText;
            private set
            {
                string normalized = value ?? string.Empty;
                if (string.Equals(normalized, _objectiveText, StringComparison.Ordinal))
                    return;
                _objectiveText = normalized;
                OnPropertyChangedWithValue(normalized, nameof(ObjectiveText));
            }
        }

        public void Update(Camera camera)
        {
            CoopHideoutAmbushState global =
                CoopHideoutAmbushNetworkController.CurrentClientState;
            bool stealthActive =
                global?.Phase == CoopHideoutAmbushPhase.Stealth;
            SyncTargets(camera, stealthActive);
            IsMainAgentInSafeArea = stealthActive && IsMainAgentInsideStealthBox();
            UpdateObjective(
                camera,
                stealthActive &&
                global.IsUsePointAvailable &&
                !CoopHideoutAmbushNetworkController.IsUsePointRequestPending);
        }

        private void SyncTargets(Camera camera, bool stealthActive)
        {
            IReadOnlyDictionary<int, CoopHideoutAmbushState> states =
                CoopHideoutAmbushNetworkController.CurrentClientGuardStates;
            foreach (KeyValuePair<int, CoopHideoutAmbushState> pair in states)
            {
                if (!_targetsByAgentIndex.TryGetValue(
                        pair.Key,
                        out CoopHideoutAmbushAlarmTargetVM target))
                {
                    target = new CoopHideoutAmbushAlarmTargetVM(pair.Key);
                    _targetsByAgentIndex.Add(pair.Key, target);
                    Targets.Add(target);
                }
                target.Update(pair.Value, camera, stealthActive);
            }

            foreach (int agentIndex in _targetsByAgentIndex.Keys.ToArray())
            {
                if (states.ContainsKey(agentIndex))
                    continue;
                CoopHideoutAmbushAlarmTargetVM target = _targetsByAgentIndex[agentIndex];
                Targets.Remove(target);
                _targetsByAgentIndex.Remove(agentIndex);
            }
        }

        private void UpdateObjective(Camera camera, bool show)
        {
            if (!show || camera == null)
            {
                IsObjectiveVisible = false;
                return;
            }

            try
            {
                GameEntity entity = Mission.Current?.Scene?.FindEntityWithTag(
                    CoopHideoutAmbushContract.CallTroopsArrowBarrelTag);
                if (entity == null)
                {
                    IsObjectiveVisible = false;
                    return;
                }

                Vec3 worldPosition = entity.GlobalPosition + Vec3.Up * 0.9f;
                IsObjectiveVisible = TryProject(camera, worldPosition, out Vec2 position);
                if (IsObjectiveVisible)
                    ObjectivePosition = position;
            }
            catch
            {
                IsObjectiveVisible = false;
            }
        }

        private bool IsMainAgentInsideStealthBox()
        {
            Agent mainAgent = Agent.Main;
            if (mainAgent?.IsActive() != true)
                return false;

            if (!_stealthBoxesResolved)
            {
                _stealthBoxesResolved = true;
                ResolveStealthBoxes(_stealthBoxes);
            }

            foreach (StealthBox stealthBox in _stealthBoxes)
            {
                try
                {
                    if (stealthBox?.IsAgentInside(mainAgent) == true)
                        return true;
                }
                catch
                {
                }
            }
            return false;
        }

        private static void ResolveStealthBoxes(List<StealthBox> results)
        {
            if (results == null || Mission.Current?.Scene == null)
                return;

            try
            {
                var entities = new List<GameEntity>();
                Mission.Current.Scene.GetAllEntitiesWithScriptComponent<StealthBox>(
                    ref entities);
                foreach (GameEntity entity in entities)
                {
                    StealthBox script =
                        entity.GetFirstScriptOfTypeRecursive<StealthBox>();
                    if (script != null)
                        results.Add(script);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Verbose(
                    "CoopHideoutAmbushStealthVM: native stealth-box lookup unavailable. " +
                    "Error=" + ex.GetType().Name + ":" + ex.Message + ".");
            }
        }

        internal static bool TryProject(
            Camera camera,
            Vec3 worldPosition,
            out Vec2 position)
        {
            position = Vec2.Zero;
            if (camera == null)
                return false;
            float x = 0f;
            float y = 0f;
            float w = 0f;
            MBWindowManager.WorldToScreenInsideUsableArea(
                camera,
                worldPosition,
                ref x,
                ref y,
                ref w);
            position = new Vec2(x, y);
            return w > 0f &&
                   x >= -180f &&
                   x <= Screen.RealScreenResolutionWidth + 180f &&
                   y >= -80f &&
                   y <= Screen.RealScreenResolutionHeight + 80f;
        }
    }

    public sealed class CoopHideoutAmbushFailCounterVM : ViewModel
    {
        private readonly TextObject _countDownTextObject =
            new TextObject("{=pY8lnL11}Mission will fail in: {SEC}");
        private string _countDownText = string.Empty;
        private float _failCounterElapsedTime;
        private float _failCounterMaxTime =
            CoopHideoutAmbushContract.AlarmFailureSeconds;
        private bool _isCounterActive;

        [DataSourceProperty]
        public string CountDownText
        {
            get => _countDownText;
            private set
            {
                string normalized = value ?? string.Empty;
                if (string.Equals(normalized, _countDownText, StringComparison.Ordinal))
                    return;
                _countDownText = normalized;
                OnPropertyChangedWithValue(normalized, nameof(CountDownText));
            }
        }

        [DataSourceProperty]
        public float FailCounterElapsedTime
        {
            get => _failCounterElapsedTime;
            private set
            {
                if (Math.Abs(value - _failCounterElapsedTime) < 0.0001f)
                    return;
                _failCounterElapsedTime = value;
                OnPropertyChangedWithValue(value, nameof(FailCounterElapsedTime));
            }
        }

        [DataSourceProperty]
        public float FailCounterMaxTime
        {
            get => _failCounterMaxTime;
            private set
            {
                if (Math.Abs(value - _failCounterMaxTime) < 0.0001f)
                    return;
                _failCounterMaxTime = value;
                OnPropertyChangedWithValue(value, nameof(FailCounterMaxTime));
            }
        }

        [DataSourceProperty]
        public bool IsCounterActive
        {
            get => _isCounterActive;
            private set
            {
                if (value == _isCounterActive)
                    return;
                _isCounterActive = value;
                OnPropertyChangedWithValue(value, nameof(IsCounterActive));
            }
        }

        public void Update(CoopHideoutAmbushState state)
        {
            FailCounterMaxTime = CoopHideoutAmbushContract.AlarmFailureSeconds;
            float remainingSeconds = Math.Max(
                0f,
                Math.Min(
                    FailCounterMaxTime,
                    (state?.AlarmFailureRemainingMilliseconds ?? 0) / 1000f));
            bool counterActive =
                state?.Phase == CoopHideoutAmbushPhase.Stealth &&
                state.IsAlarmFailureCounterActive &&
                remainingSeconds > 0f;
            IsCounterActive =
                !BannerlordConfig.HideBattleUI &&
                !MBCommon.IsPaused &&
                counterActive;
            FailCounterElapsedTime = remainingSeconds;
            if (!IsCounterActive)
                return;

            _countDownTextObject.SetTextVariable(
                "SEC",
                (int)Math.Ceiling(remainingSeconds));
            CountDownText = _countDownTextObject.ToString();
        }
    }

    public sealed class CoopHideoutAmbushAlarmTargetVM : ViewModel
    {
        private readonly int _agentIndex;
        private bool _isStealthModeEnabled;
        private bool _isMainAgentInVisibilityRange;
        private bool _isInVision;
        private bool _hasCautiousness;
        private bool _isSuspected;
        private int _alarmProgress;
        private string _alarmState = "Invalid";
        private int _wSign = 1;
        private Vec2 _screenPosition;

        public CoopHideoutAmbushAlarmTargetVM(int agentIndex)
        {
            _agentIndex = agentIndex;
        }

        [DataSourceProperty]
        public bool IsStealthModeEnabled
        {
            get => _isStealthModeEnabled;
            private set
            {
                if (value == _isStealthModeEnabled)
                    return;
                _isStealthModeEnabled = value;
                OnPropertyChangedWithValue(value, nameof(IsStealthModeEnabled));
            }
        }

        [DataSourceProperty]
        public bool IsMainAgentInVisibilityRange
        {
            get => _isMainAgentInVisibilityRange;
            private set
            {
                if (value == _isMainAgentInVisibilityRange)
                    return;
                _isMainAgentInVisibilityRange = value;
                OnPropertyChangedWithValue(
                    value,
                    nameof(IsMainAgentInVisibilityRange));
            }
        }

        [DataSourceProperty]
        public bool IsInVision
        {
            get => _isInVision;
            private set
            {
                if (value == _isInVision)
                    return;
                _isInVision = value;
                OnPropertyChangedWithValue(value, nameof(IsInVision));
            }
        }

        [DataSourceProperty]
        public bool HasCautiousness
        {
            get => _hasCautiousness;
            private set
            {
                if (value == _hasCautiousness)
                    return;
                _hasCautiousness = value;
                OnPropertyChangedWithValue(value, nameof(HasCautiousness));
            }
        }

        [DataSourceProperty]
        public bool IsSuspected
        {
            get => _isSuspected;
            private set
            {
                if (value == _isSuspected)
                    return;
                _isSuspected = value;
                OnPropertyChangedWithValue(value, nameof(IsSuspected));
            }
        }

        [DataSourceProperty]
        public int AlarmProgress
        {
            get => _alarmProgress;
            private set
            {
                if (value == _alarmProgress)
                    return;
                _alarmProgress = value;
                OnPropertyChangedWithValue(value, nameof(AlarmProgress));
            }
        }

        [DataSourceProperty]
        public string AlarmState
        {
            get => _alarmState;
            private set
            {
                if (string.Equals(value, _alarmState, StringComparison.Ordinal))
                    return;
                _alarmState = value;
                OnPropertyChangedWithValue(value, nameof(AlarmState));
            }
        }

        [DataSourceProperty]
        public int WSign
        {
            get => _wSign;
            private set
            {
                if (value == _wSign)
                    return;
                _wSign = value;
                OnPropertyChangedWithValue(value, nameof(WSign));
            }
        }

        [DataSourceProperty]
        public Vec2 ScreenPosition
        {
            get => _screenPosition;
            private set
            {
                if (value == _screenPosition)
                    return;
                _screenPosition = value;
                OnPropertyChangedWithValue(value, nameof(ScreenPosition));
            }
        }

        public void Update(
            CoopHideoutAmbushState state,
            Camera camera,
            bool stealthActive)
        {
            Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(
                _agentIndex,
                canBeNull: true);
            IsStealthModeEnabled = stealthActive;
            if (!stealthActive || agent?.IsActive() != true || camera == null)
            {
                IsMainAgentInVisibilityRange = false;
                IsInVision = false;
                HasCautiousness = false;
                IsSuspected = false;
                AlarmProgress = 0;
                AlarmState = "Invalid";
                WSign = -1;
                return;
            }

            AlarmProgress = Math.Max(0, Math.Min(100, state.SuspicionPermille / 10));
            AlarmState = state.IsAlarmed
                ? "Alarmed"
                : AlarmProgress >= 100
                    ? "Cautious"
                    : "Default";
            Vec3 worldPosition = agent.Position;
            worldPosition.z += agent.GetEyeGlobalHeight() + 0.35f;
            bool projected = CoopHideoutAmbushStealthVM.TryProject(
                camera,
                worldPosition,
                out Vec2 screenPosition);
            ScreenPosition = screenPosition;
            WSign = projected ? 1 : -1;
            Agent mainAgent = Agent.Main;
            IsMainAgentInVisibilityRange =
                projected &&
                mainAgent?.IsActive() == true &&
                agent.Position.AsVec2.DistanceSquared(mainAgent.Position.AsVec2) <= 900f;
            IsInVision =
                projected &&
                (state.ObservedAgentIndex >= 0 ||
                 AlarmProgress > 0 ||
                 state.IsAlarmed);
            HasCautiousness = AlarmProgress > 0 || state.IsAlarmed;
            IsSuspected = IsInVision && (AlarmProgress > 0 || state.IsAlarmed);
        }

        public void ExecuteRemove()
        {
            IsSuspected = false;
        }
    }
}
