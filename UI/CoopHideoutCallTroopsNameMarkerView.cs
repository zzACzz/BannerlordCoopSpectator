using System;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.Hideout;
using CoopSpectator.MissionBehaviors;
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
    public sealed class CoopHideoutCallTroopsNameMarkerView : MissionView
    {
        private const int ShowIndicatorsGameKeyIndex = 5;
        private const string NativeMovieName = "NameMarker";

        private GauntletLayer _layer;
        private GauntletMovieIdentifier _movie;
        private CoopHideoutCallTroopsNameMarkerVM _viewModel;

        public override void OnMissionScreenInitialize()
        {
            base.OnMissionScreenInitialize();
            ViewOrderPriority = 45;
            TryCreateLayer();
        }

        public override void OnMissionScreenTick(float dt)
        {
            base.OnMissionScreenTick(dt);
            if (!GameNetwork.IsClient || MissionScreen == null)
                return;

            if (_viewModel == null)
                TryCreateLayer();

            _viewModel?.Update(
                dt,
                Input.IsGameKeyDown(ShowIndicatorsGameKeyIndex),
                MissionScreen.CombatCamera);
        }

        public override void OnMissionScreenFinalize()
        {
            ReleaseLayer();
            base.OnMissionScreenFinalize();
        }

        public override void OnPhotoModeActivated()
        {
            base.OnPhotoModeActivated();
            if (_layer != null)
                _layer.UIContext.ContextAlpha = 0f;
        }

        public override void OnPhotoModeDeactivated()
        {
            base.OnPhotoModeDeactivated();
            if (_layer != null)
                _layer.UIContext.ContextAlpha = 1f;
        }

        private void TryCreateLayer()
        {
            if (_viewModel != null || MissionScreen == null)
                return;

            try
            {
                _viewModel = new CoopHideoutCallTroopsNameMarkerVM();
                _layer = new GauntletLayer("MissionNameMarker", 1, false);
                MissionScreen.AddLayer(_layer);
                _movie = _layer.LoadMovie(NativeMovieName, _viewModel);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopHideoutCallTroopsNameMarkerView: native marker initialization failed. " +
                    "Error=" + ex.GetType().Name + ":" + ex.Message + ".");
                ReleaseLayer();
            }
        }

        private void ReleaseLayer()
        {
            try
            {
                if (_layer != null && _movie != null)
                    _layer.ReleaseMovie(_movie);
                if (_layer != null)
                    MissionScreen?.RemoveLayer(_layer);
                _viewModel?.OnFinalize();
            }
            catch
            {
            }
            finally
            {
                _movie = null;
                _layer = null;
                _viewModel = null;
            }
        }
    }

    internal sealed class CoopHideoutCallTroopsNameMarkerVM : ViewModel
    {
        private const float NativeFadeOutSeconds = 2f;

        private CoopHideoutCallTroopsNameMarkerTargetVM _target;
        private bool _isEnabled;
        private bool _previousEnabledState;
        private float _fadeOutTimer = NativeFadeOutSeconds;

        internal CoopHideoutCallTroopsNameMarkerVM()
        {
            Targets = new MBBindingList<CoopHideoutCallTroopsNameMarkerTargetVM>();
        }

        [DataSourceProperty]
        public MBBindingList<CoopHideoutCallTroopsNameMarkerTargetVM> Targets { get; }

        [DataSourceProperty]
        public bool IsEnabled
        {
            get => _isEnabled;
            private set
            {
                if (value == _isEnabled)
                    return;
                _isEnabled = value;
                OnPropertyChangedWithValue(value, nameof(IsEnabled));
            }
        }

        internal void Update(float dt, bool showIndicatorsHeld, Camera camera)
        {
            bool markerAvailable = TryResolveAvailableUsePoint(out UsableMissionObject usePoint);
            if (!markerAvailable)
            {
                ClearTarget();
                IsEnabled = false;
                _previousEnabledState = false;
                _fadeOutTimer = NativeFadeOutSeconds;
                return;
            }

            if (_target == null || !_target.References(usePoint))
            {
                ClearTarget();
                _target = new CoopHideoutCallTroopsNameMarkerTargetVM(usePoint);
                Targets.Add(_target);
            }

            bool enabled = showIndicatorsHeld;
            IsEnabled = enabled;
            _target.SetEnabledState(enabled);
            if (enabled)
            {
                _fadeOutTimer = 0f;
                _target.UpdatePosition(camera);
            }
            else
            {
                if (_previousEnabledState)
                    _fadeOutTimer = 0f;
                if (_fadeOutTimer < NativeFadeOutSeconds)
                {
                    _fadeOutTimer += Math.Max(0f, dt);
                    _target.UpdatePosition(camera);
                }
            }
            _previousEnabledState = enabled;
        }

        public override void OnFinalize()
        {
            ClearTarget();
            base.OnFinalize();
        }

        private void ClearTarget()
        {
            if (_target == null)
                return;
            Targets.Remove(_target);
            _target.OnFinalize();
            _target = null;
        }

        private static bool TryResolveAvailableUsePoint(
            out UsableMissionObject usePoint)
        {
            usePoint = null;
            CoopHideoutAmbushState state =
                CoopHideoutAmbushNetworkController.CurrentClientState;
            Agent mainAgent = Agent.Main;
            if (state?.Phase != CoopHideoutAmbushPhase.Stealth ||
                !state.IsUsePointAvailable ||
                CoopHideoutAmbushNetworkController.IsUsePointRequestPending ||
                mainAgent?.IsActive() != true ||
                Mission.Current == null)
            {
                return false;
            }

            foreach (MissionObject missionObject in Mission.Current.ActiveMissionObjects)
            {
                if (!(missionObject is UsableMissionObject candidate) ||
                    !IsCallTroopsUsePoint(candidate))
                {
                    continue;
                }

                try
                {
                    if (!candidate.IsUsableByAgent(mainAgent))
                        continue;
                }
                catch
                {
                    continue;
                }

                usePoint = candidate;
                return true;
            }
            return false;
        }

        private static bool IsCallTroopsUsePoint(UsableMissionObject usePoint)
        {
            if (usePoint == null)
                return false;
            if (string.Equals(
                    usePoint.GetType().FullName,
                    CoopHideoutAmbushContract.StealthAreaUsePointTypeName,
                    StringComparison.Ordinal))
            {
                return true;
            }

            try
            {
                WeakGameEntity entity = usePoint.GameEntity;
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

    internal sealed class CoopHideoutCallTroopsNameMarkerTargetVM : ViewModel
    {
        private readonly UsableMissionObject _usePoint;
        private Vec2 _screenPosition;
        private int _distance;
        private bool _isEnabled;

        internal CoopHideoutCallTroopsNameMarkerTargetVM(
            UsableMissionObject usePoint)
        {
            _usePoint = usePoint;
            Quests = new MBBindingList<CoopHideoutEmptyQuestMarkerVM>();
            Name = new TextObject("{=GmjiZk9P}Call Troops").ToString();
        }

        [DataSourceProperty]
        public MBBindingList<CoopHideoutEmptyQuestMarkerVM> Quests { get; }

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

        [DataSourceProperty]
        public string Name { get; }

        [DataSourceProperty]
        public string IconType => "call_troops";

        [DataSourceProperty]
        public string NameType => "Normal";

        [DataSourceProperty]
        public int Distance
        {
            get => _distance;
            private set
            {
                if (value == _distance)
                    return;
                _distance = value;
                OnPropertyChangedWithValue(value, nameof(Distance));
            }
        }

        [DataSourceProperty]
        public bool IsEnabled
        {
            get => _isEnabled;
            private set
            {
                if (value == _isEnabled)
                    return;
                _isEnabled = value;
                OnPropertyChangedWithValue(value, nameof(IsEnabled));
            }
        }

        [DataSourceProperty]
        public bool IsTracked => false;

        [DataSourceProperty]
        public bool IsQuestMainStory => false;

        [DataSourceProperty]
        public bool IsEnemy => false;

        [DataSourceProperty]
        public bool IsFriendly => false;

        [DataSourceProperty]
        public bool IsPersistent => false;

        internal bool References(UsableMissionObject usePoint)
        {
            return ReferenceEquals(_usePoint, usePoint);
        }

        internal void SetEnabledState(bool enabled)
        {
            IsEnabled = enabled;
        }

        internal void UpdatePosition(Camera camera)
        {
            if (camera == null || _usePoint == null)
                return;

            try
            {
                WeakGameEntity entity = _usePoint.GameEntity;
                MatrixFrame frame = entity.GetGlobalFrame();
                Vec3 worldPosition = frame.origin + Vec3.Up * 0.5f;
                float x = -100f;
                float y = -100f;
                float depth = 0f;
                MBWindowManager.WorldToScreenInsideUsableArea(
                    camera,
                    worldPosition,
                    ref x,
                    ref y,
                    ref depth);
                if (depth > 0f)
                {
                    ScreenPosition = new Vec2(x, y);
                    Distance = (int)(worldPosition - camera.Position).Length;
                }
                else
                {
                    Distance = -1;
                    ScreenPosition = new Vec2(-500f, -500f);
                }
            }
            catch
            {
                Distance = -1;
                ScreenPosition = new Vec2(-500f, -500f);
            }
        }
    }

    internal sealed class CoopHideoutEmptyQuestMarkerVM : ViewModel
    {
        [DataSourceProperty]
        public int QuestMarkerType => 0;
    }
}
