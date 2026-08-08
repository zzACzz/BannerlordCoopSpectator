using System;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.Hideout;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Core;
using TaleWorlds.DotNet;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;

namespace CoopSpectator.UI
{
    public sealed class CoopHideoutBossCinematicView : MissionView
    {
        private CoopHideoutBossPhaseSession _pendingState;
        private CoopHideoutBossPhaseSession _activeState;
        private Camera _camera;
        private MatrixFrame _cameraFrame = MatrixFrame.Identity;
        private MissionMode _missionModeBeforeCinematic;
        private bool _missionModeCaptured;
        private int _readySentRevision = -1;
        private int _choiceInquiryRevision = -1;
        private float _cinematicElapsed;

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            CoopHideoutBossPhaseController.ClientStateChanged += OnClientStateChanged;
            if (CoopHideoutBossPhaseController.CurrentClientState != null)
                _pendingState = CoopHideoutBossPhaseController.CurrentClientState.Clone();
        }

        public override void OnMissionScreenInitialize()
        {
            base.OnMissionScreenInitialize();
            ViewOrderPriority = 45;
        }

        public override void OnMissionScreenTick(float dt)
        {
            base.OnMissionScreenTick(dt);
            if (!GameNetwork.IsClient || MissionScreen == null)
                return;

            if (_pendingState != null)
            {
                CoopHideoutBossPhaseSession state = _pendingState;
                _pendingState = null;
                ApplyState(state);
            }

            if (_activeState == null)
                return;

            if (_activeState.Phase == CoopHideoutBossPhase.Cinematic ||
                (_activeState.Phase == CoopHideoutBossPhase.Duel && !IsLocalHost(_activeState)))
            {
                _cinematicElapsed += dt;
                if (_camera == null)
                    TrySetupCamera();
                UpdateCamera();
            }
        }

        public override void OnMissionScreenFinalize()
        {
            CoopHideoutBossPhaseController.ClientStateChanged -= OnClientStateChanged;
            try
            {
                if (_choiceInquiryRevision >= 0 && InformationManager.IsAnyInquiryActive())
                    InformationManager.HideInquiry();
            }
            catch
            {
            }
            ReleaseCamera();
            RestoreMissionMode();
            base.OnMissionScreenFinalize();
        }

        private void OnClientStateChanged(CoopHideoutBossPhaseSession state, int phaseDurationMilliseconds)
        {
            if (state == null)
                return;
            _pendingState = state.Clone();
        }

        private void ApplyState(CoopHideoutBossPhaseSession state)
        {
            if (state == null)
                return;
            if (_activeState != null &&
                string.Equals(_activeState.BattleInstanceId, state.BattleInstanceId, StringComparison.Ordinal) &&
                state.Revision < _activeState.Revision)
            {
                return;
            }

            _activeState = state.Clone();
            if (state.Phase == CoopHideoutBossPhase.PreparingCinematic)
            {
                CaptureMissionMode();
                Mission.SetMissionMode(MissionMode.CutScene, false);
                ScreenFadeController.BeginFadeOut(0.4f);
                if (_readySentRevision != state.Revision)
                {
                    _readySentRevision = state.Revision;
                    CoopHideoutBossPhaseController.SendClientReady(state.Revision);
                }
                return;
            }

            if (state.Phase == CoopHideoutBossPhase.Cinematic)
            {
                CaptureMissionMode();
                Mission.SetMissionMode(MissionMode.CutScene, false);
                _cinematicElapsed = 0f;
                TrySetupCamera();
                ScreenFadeController.BeginFadeIn(0.4f);
                return;
            }

            if (state.Phase == CoopHideoutBossPhase.AwaitingHostChoice)
            {
                ScreenFadeController.BeginFadeOut(0.2f);
                ReleaseCamera();
                ScreenFadeController.BeginFadeIn(0.2f);
                if (IsLocalHost(state))
                    ShowHostChoice(state);
                else
                    InformationManager.DisplayMessage(
                        new InformationMessage("The campaign host is deciding how to face the bandit leader."));
                return;
            }

            if (state.Phase == CoopHideoutBossPhase.Duel)
            {
                CloseChoiceInquiry();
                if (IsLocalHost(state))
                {
                    ReleaseCamera();
                    RestoreMissionMode();
                    ScreenFadeController.BeginFadeIn(0.3f);
                    InformationManager.DisplayMessage(
                        new InformationMessage("Defeat the bandit leader in single combat."));
                }
                else
                {
                    CaptureMissionMode();
                    Mission.SetMissionMode(MissionMode.CutScene, false);
                    _cinematicElapsed = 0f;
                    TrySetupCamera();
                    ScreenFadeController.BeginFadeIn(0.3f);
                    InformationManager.DisplayMessage(
                        new InformationMessage("The campaign host accepted the duel."));
                }
                return;
            }

            if (state.Phase == CoopHideoutBossPhase.AllBattle)
            {
                CloseChoiceInquiry();
                ReleaseCamera();
                RestoreMissionMode();
                ScreenFadeController.BeginFadeIn(0.3f);
                InformationManager.DisplayMessage(
                    new InformationMessage("Fight together and defeat the bandit leader."));
                return;
            }

            if (state.Phase == CoopHideoutBossPhase.Completed)
            {
                CloseChoiceInquiry();
                ReleaseCamera();
                RestoreMissionMode();
            }
        }

        private void ShowHostChoice(CoopHideoutBossPhaseSession state)
        {
            if (_choiceInquiryRevision == state.Revision)
                return;

            _choiceInquiryRevision = state.Revision;
            InformationManager.ShowInquiry(
                new InquiryData(
                    "Bandit leader",
                    "The bandit leader challenges you. Will you face the leader alone or order everyone to fight?",
                    isAffirmativeOptionShown: true,
                    isNegativeOptionShown: true,
                    affirmativeText: "Accept the duel",
                    negativeText: "Fight together",
                    affirmativeAction: () =>
                        CoopHideoutBossPhaseController.SendHostChoice(CoopHideoutBossChoice.Duel),
                    negativeAction: () =>
                        CoopHideoutBossPhaseController.SendHostChoice(CoopHideoutBossChoice.AllBattle),
                    soundEventPath: string.Empty,
                    expireTime: CoopHideoutBossPhaseContract.HostChoiceTimeoutMilliseconds / 1000f,
                    timeoutAction: () =>
                        CoopHideoutBossPhaseController.SendHostChoice(CoopHideoutBossChoice.AllBattle)),
                pauseGameActiveState: false,
                prioritize: true);
        }

        private void CloseChoiceInquiry()
        {
            if (_choiceInquiryRevision < 0)
                return;
            try
            {
                if (InformationManager.IsAnyInquiryActive())
                    InformationManager.HideInquiry();
            }
            catch
            {
            }
            _choiceInquiryRevision = -1;
        }

        private bool TrySetupCamera()
        {
            if (_camera != null || MissionScreen == null || _activeState == null)
                return _camera != null;

            Agent hostAgent = ResolveAgent(_activeState.HostAgentIndex);
            Agent bossAgent = ResolveAgent(_activeState.BossAgentIndex);
            if (hostAgent == null || bossAgent == null)
                return false;

            try
            {
                _camera = Camera.CreateCamera();
                Camera combatCamera = MissionScreen.CombatCamera;
                if ((NativeObject)(object)combatCamera != (NativeObject)null)
                    _camera.FillParametersFrom(combatCamera);

                UpdateCamera();
                MissionScreen.CustomCamera = _camera;
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopHideoutBossCinematicView: camera setup failed. Error=" + ex.Message);
                try { _camera?.ReleaseCamera(); } catch { }
                _camera = null;
                return false;
            }
        }

        private void UpdateCamera()
        {
            if (_camera == null || _activeState == null)
                return;

            Agent hostAgent = ResolveAgent(_activeState.HostAgentIndex);
            Agent bossAgent = ResolveAgent(_activeState.BossAgentIndex);
            if (hostAgent == null || bossAgent == null)
                return;

            Vec3 hostEye = hostAgent.Position + Vec3.Up * 1.45f;
            Vec3 bossEye = bossAgent.Position + Vec3.Up * 1.45f;
            Vec3 focus = (hostEye + bossEye) * 0.5f;
            Vec2 line2 = (bossEye - hostEye).AsVec2;
            if (line2.LengthSquared < 0.0001f)
                line2 = new Vec2(0f, 1f);
            line2.Normalize();
            Vec2 side2 = new Vec2(line2.y, -line2.x);
            float orbit = (float)Math.Sin(_cinematicElapsed * 0.22f) * 0.65f;
            Vec2 cameraOffset2 = side2 * (4.5f + orbit) - line2 * 1.25f;
            Vec3 cameraPosition = focus + new Vec3(cameraOffset2.x, cameraOffset2.y, 2.25f);
            Vec3 direction = focus - cameraPosition;
            if (direction.LengthSquared < 0.0001f)
                direction = Vec3.Forward;
            direction.Normalize();
            SetCameraFrame(cameraPosition, direction, out _cameraFrame);
            _camera.Frame = _cameraFrame;
        }

        private static void SetCameraFrame(
            Vec3 position,
            Vec3 direction,
            out MatrixFrame cameraFrame)
        {
            cameraFrame = MatrixFrame.Identity;
            cameraFrame.origin = position;
            cameraFrame.rotation.s = Vec3.Side;
            cameraFrame.rotation.f = Vec3.Up;
            cameraFrame.rotation.u = -direction;
            cameraFrame.rotation.Orthonormalize();
        }

        private void ReleaseCamera()
        {
            if (_camera == null)
                return;

            try
            {
                if (MissionScreen?.CustomCamera == _camera)
                {
                    MissionScreen.UpdateFreeCamera(_camera.Frame);
                    MissionScreen.CustomCamera = null;
                }
            }
            catch
            {
            }
            try { _camera.ReleaseCamera(); } catch { }
            _camera = null;
        }

        private void CaptureMissionMode()
        {
            if (_missionModeCaptured || Mission == null)
                return;
            _missionModeBeforeCinematic = Mission.Mode;
            _missionModeCaptured = true;
        }

        private void RestoreMissionMode()
        {
            if (!_missionModeCaptured || Mission == null)
                return;

            MissionMode target = _missionModeBeforeCinematic == MissionMode.CutScene
                ? MissionMode.Battle
                : _missionModeBeforeCinematic;
            try { Mission.SetMissionMode(target, false); } catch { }
        }

        private static bool IsLocalHost(CoopHideoutBossPhaseSession state)
        {
            return state != null &&
                   GameNetwork.MyPeer != null &&
                   !GameNetwork.MyPeer.IsServerPeer &&
                   GameNetwork.MyPeer.Index == state.HostPeerIndex;
        }

        private static Agent ResolveAgent(int agentIndex)
        {
            if (agentIndex < 0)
                return null;
            try
            {
                return Mission.MissionNetworkHelper.GetAgentFromIndex(agentIndex, canBeNull: true);
            }
            catch
            {
                return null;
            }
        }
    }
}
