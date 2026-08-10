using System;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.Hideout;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;

namespace CoopSpectator.UI
{
    public sealed class CoopHideoutAmbushCallTroopsCinematicView : MissionView
    {
        private enum CinematicState
        {
            Idle,
            FirstFade,
            WaitForFirstFadeOut,
            WaitForFirstFadeIn,
            ArrowFlight,
            SecondFade,
            WaitForSecondFadeOut,
            WaitForSecondFadeIn,
            Completed
        }

        private CoopHideoutAmbushState _pendingNetworkState;
        private Camera _camera;
        private GameEntity _arrowPath;
        private MissionMode _previousMissionMode;
        private CinematicState _state;
        private int _activeRevision = -1;
        private float _arrowFlightEndsAt;
        private bool _missionModeCaptured;
        private bool _mainAgentHidden;

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            CoopHideoutAmbushNetworkController.ClientStateChanged +=
                OnClientStateChanged;
            if (CoopHideoutAmbushNetworkController.CurrentClientState != null)
            {
                _pendingNetworkState =
                    CoopHideoutAmbushNetworkController.CurrentClientState.Clone();
            }
        }

        public override void OnMissionScreenInitialize()
        {
            base.OnMissionScreenInitialize();
            ViewOrderPriority = 46;
        }

        public override void OnMissionScreenTick(float dt)
        {
            base.OnMissionScreenTick(dt);
            if (!GameNetwork.IsClient || MissionScreen == null)
                return;

            if (_pendingNetworkState != null)
            {
                CoopHideoutAmbushState state = _pendingNetworkState;
                _pendingNetworkState = null;
                ApplyNetworkState(state);
            }

            TickCinematic();
        }

        public override void OnMissionScreenFinalize()
        {
            CoopHideoutAmbushNetworkController.ClientStateChanged -=
                OnClientStateChanged;
            ReleaseCinematic();
            base.OnMissionScreenFinalize();
        }

        private void OnClientStateChanged(CoopHideoutAmbushState state)
        {
            if (state != null)
                _pendingNetworkState = state.Clone();
        }

        private void ApplyNetworkState(CoopHideoutAmbushState state)
        {
            if (state.Phase == CoopHideoutAmbushPhase.CallTroops &&
                state.Revision != _activeRevision)
            {
                StartCinematic(state.Revision);
                return;
            }

            if (state.Phase > CoopHideoutAmbushPhase.CallTroops &&
                _state != CinematicState.Idle &&
                _state != CinematicState.Completed)
            {
                ReleaseCinematic();
                _state = CinematicState.Completed;
            }
        }

        private void StartCinematic(int revision)
        {
            ReleaseCinematic();
            _activeRevision = revision;
            _state = CinematicState.FirstFade;
            try
            {
                GameEntity cameraEntity = Mission?.Scene?.FindEntityWithTag(
                    CoopHideoutAmbushContract.CallTroopsCameraTag);
                _arrowPath = Mission?.Scene?.FindEntityWithTag(
                    CoopHideoutAmbushContract.CallTroopsArrowPathTag);
                if (cameraEntity == null || _arrowPath == null)
                    throw new InvalidOperationException("authored-call-troops-cinematic-resource-missing");

                _camera = Camera.CreateCamera();
                if (MissionScreen.CombatCamera != null)
                    _camera.FillParametersFrom(MissionScreen.CombatCamera);
                Vec3 invalid = Vec3.Invalid;
                cameraEntity.GetCameraParamsFromCameraScript(_camera, ref invalid);
                _camera.SetFovVertical(
                    _camera.GetFovVertical(),
                    Screen.AspectRatio,
                    _camera.Near,
                    _camera.Far);
                _arrowPath.SetVisibilityExcludeParents(false);
                CaptureMissionMode();
                Mission.SetMissionMode(MissionMode.CutScene, false);
                CoopHideoutAmbushNetworkController.SendCinematicReady();
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopHideoutAmbushCallTroopsCinematicView: cinematic setup failed. " +
                    "Error=" + ex.GetType().Name + ":" + ex.Message + ".");
                CoopHideoutAmbushNetworkController.SendCinematicReady();
                ReleaseCinematic();
                _state = CinematicState.Completed;
            }
        }

        private void TickCinematic()
        {
            switch (_state)
            {
                case CinematicState.Idle:
                case CinematicState.Completed:
                    return;
                case CinematicState.FirstFade:
                    ScreenFadeController.BeginFadeOutAndIn(0.5f, 0.5f, 0.5f);
                    _state = CinematicState.WaitForFirstFadeOut;
                    return;
                case CinematicState.WaitForFirstFadeOut:
                    if (!ScreenFadeController.IsFadedOut)
                        return;
                    MissionScreen.CustomCamera = _camera;
                    SetMainAgentVisible(false);
                    _state = CinematicState.WaitForFirstFadeIn;
                    return;
                case CinematicState.WaitForFirstFadeIn:
                    if (ScreenFadeController.IsFadeActive)
                        return;
                    StartBurningArrow();
                    _arrowFlightEndsAt = Mission.CurrentTime + 5f;
                    _state = CinematicState.ArrowFlight;
                    return;
                case CinematicState.ArrowFlight:
                    if (Mission.CurrentTime < _arrowFlightEndsAt)
                        return;
                    _arrowPath?.SetVisibilityExcludeParents(false);
                    _state = CinematicState.SecondFade;
                    return;
                case CinematicState.SecondFade:
                    ScreenFadeController.BeginFadeOutAndIn(0.5f, 0.5f, 0.5f);
                    _state = CinematicState.WaitForSecondFadeOut;
                    return;
                case CinematicState.WaitForSecondFadeOut:
                    if (!ScreenFadeController.IsFadedOut)
                        return;
                    RestoreCameraAndAgent();
                    _state = CinematicState.WaitForSecondFadeIn;
                    return;
                case CinematicState.WaitForSecondFadeIn:
                    if (ScreenFadeController.IsFadeActive)
                        return;
                    RestoreMissionMode();
                    _state = CinematicState.Completed;
                    return;
            }
        }

        private void StartBurningArrow()
        {
            if (_arrowPath == null)
                return;
            try
            {
                _arrowPath.SetVisibilityExcludeParents(true);
                ScriptComponentBehavior behavior = _arrowPath.GetScriptComponents()
                    .FirstOrDefault(component =>
                        string.Equals(
                            component?.GetType().FullName,
                            "SandBox.Objects.Cinematics.CinematicBurningArrow",
                            StringComparison.Ordinal));
                MethodInfo startMovement = behavior?.GetType().GetMethod(
                    "StartMovement",
                    BindingFlags.Instance | BindingFlags.Public);
                startMovement?.Invoke(behavior, null);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopHideoutAmbushCallTroopsCinematicView: burning-arrow start failed. " +
                    "Error=" + ex.GetType().Name + ":" + ex.Message + ".");
            }
        }

        private void CaptureMissionMode()
        {
            if (_missionModeCaptured || Mission == null)
                return;
            _previousMissionMode = Mission.Mode;
            _missionModeCaptured = true;
        }

        private void RestoreMissionMode()
        {
            if (!_missionModeCaptured || Mission == null)
                return;
            try
            {
                Mission.SetMissionMode(_previousMissionMode, false);
            }
            catch
            {
            }
            _missionModeCaptured = false;
        }

        private void SetMainAgentVisible(bool visible)
        {
            try
            {
                if (Agent.Main?.AgentVisuals == null)
                    return;
                Agent.Main.AgentVisuals.SetVisible(visible);
                _mainAgentHidden = !visible;
            }
            catch
            {
            }
        }

        private void RestoreCameraAndAgent()
        {
            try
            {
                if (MissionScreen != null)
                    MissionScreen.CustomCamera = null;
            }
            catch
            {
            }
            if (_mainAgentHidden)
                SetMainAgentVisible(true);
        }

        private void ReleaseCinematic()
        {
            RestoreCameraAndAgent();
            RestoreMissionMode();
            try
            {
                _arrowPath?.SetVisibilityExcludeParents(false);
                _camera?.ReleaseCamera();
            }
            catch
            {
            }
            _arrowPath = null;
            _camera = null;
            _mainAgentHidden = false;
            if (_state != CinematicState.Completed)
                _state = CinematicState.Idle;
        }
    }
}
