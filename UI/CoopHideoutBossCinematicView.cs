using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.Infrastructure.Hideout;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Core;
using TaleWorlds.DotNet;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.InputSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.ScreenSystem;
using TaleWorlds.TwoDimension;

namespace CoopSpectator.UI
{
    public sealed class CoopHideoutBossCinematicView : MissionView
    {
        private static readonly MethodInfo ScreenLayerHandleActivateMethod =
            typeof(ScreenLayer).GetMethod(
                "HandleActivate",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly MethodInfo ScreenLayerHandleDeactivateMethod =
            typeof(ScreenLayer).GetMethod(
                "HandleDeactivate",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        private const float DefaultBossFightInnerRadius = 2.5f;
        private const float DefaultBossFightOuterRadius = 6f;
        private const float DefaultBossFightWalkDistance = 3f;

        private CoopHideoutBossPhaseSession _pendingState;
        private CoopHideoutBossPhaseSession _activeState;
        private int _pendingPhaseDurationMilliseconds;
        private int _activeCinematicDurationMilliseconds =
            CoopHideoutBossPhaseContract.CinematicDurationMilliseconds;
        private Camera _camera;
        private MatrixFrame _cameraFrame = MatrixFrame.Identity;
        private Vec3 _cameraStartPosition = Vec3.Zero;
        private Vec3 _cameraMoveDirection = Vec3.Forward;
        private float _cameraSpeed;
        private bool _cameraPathReady;
        private MissionMode _missionModeBeforeCinematic;
        private bool _missionModeCaptured;
        private int _readySentRevision = -1;
        private int _conversationRevision = -1;
        private float _cinematicElapsed;
        private GauntletLayer _conversationLayer;
        private GauntletMovieIdentifier _conversationMovie;
        private CoopHideoutBossConversationVM _conversationViewModel;
        private SpriteCategory _conversationSpriteCategory;
        private MissionMainAgentController _mainAgentController;
        private Vec3 _customLookDirectionBeforeConversation = Vec3.Zero;
        private bool _customLookDirectionCaptured;
        private Agent _conversationFocusedHostAgent;
        private Agent _conversationFocusedBossAgent;
        private bool _combatCameraAlignmentPending;
        private int _combatCameraAlignmentDelayTicks;
        private readonly List<MissionLayerActivationSnapshot> _missionLayerActivationSnapshots =
            new List<MissionLayerActivationSnapshot>();
        private ScreenLayer _focusedLayerBeforeConversation;
        private bool _missionLayerStateCaptured;

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            CoopHideoutBossPhaseController.ClientStateChanged += OnClientStateChanged;
            if (CoopHideoutBossPhaseController.CurrentClientState != null)
            {
                _pendingState = CoopHideoutBossPhaseController.CurrentClientState.Clone();
                _pendingPhaseDurationMilliseconds =
                    CoopHideoutBossPhaseController.CurrentClientPhaseDurationMilliseconds;
            }
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
                int phaseDurationMilliseconds = _pendingPhaseDurationMilliseconds;
                _pendingState = null;
                _pendingPhaseDurationMilliseconds = 0;
                ApplyState(state, phaseDurationMilliseconds);
            }

            if (_activeState == null)
                return;

            if (CoopHideoutBossPhaseContract.ShouldMaintainLocalHostFacingBoss(
                    IsLocalHost(_activeState),
                    _activeState.Phase))
            {
                MaintainLocalHostFacingBoss();
            }

            if (_activeState.Phase == CoopHideoutBossPhase.Cinematic)
            {
                _cinematicElapsed += dt;
                if (_camera == null)
                    TrySetupCamera();
                UpdateCamera();
            }

            TickPendingCombatCameraAlignment();
        }

        public override void OnMissionScreenFinalize()
        {
            CoopHideoutBossPhaseController.ClientStateChanged -= OnClientStateChanged;
            CloseBossConversation();
            ReleaseLocalHostFacingOverride();
            _combatCameraAlignmentPending = false;
            _combatCameraAlignmentDelayTicks = 0;
            ReleaseCamera();
            RestoreMissionMode();
            base.OnMissionScreenFinalize();
        }

        private void OnClientStateChanged(CoopHideoutBossPhaseSession state, int phaseDurationMilliseconds)
        {
            if (state == null)
                return;
            _pendingState = state.Clone();
            _pendingPhaseDurationMilliseconds = phaseDurationMilliseconds;
        }

        private void ApplyState(
            CoopHideoutBossPhaseSession state,
            int phaseDurationMilliseconds)
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
            if (!CoopHideoutBossPhaseContract.ShouldMaintainLocalHostFacingBoss(
                    IsLocalHost(state),
                    state.Phase))
            {
                ReleaseLocalHostFacingOverride(
                    CoopHideoutBossPhaseContract.ShouldClearBossConversationLookDirection(
                        IsLocalHost(state),
                        state.Phase));
            }
            if (!CoopHideoutBossPhaseContract.ShouldShowBossConversation(state.Phase))
                CloseBossConversation();

            if (state.Phase == CoopHideoutBossPhase.PreparingCinematic)
            {
                _activeCinematicDurationMilliseconds =
                    CoopHideoutBossPhaseContract.CinematicDurationMilliseconds;
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
                if (phaseDurationMilliseconds > 0)
                    _activeCinematicDurationMilliseconds = phaseDurationMilliseconds;
                CaptureMissionMode();
                Mission.SetMissionMode(MissionMode.CutScene, false);
                _cinematicElapsed = 0f;
                TrySetupCamera();
                ScreenFadeController.BeginFadeIn(0.4f);
                return;
            }

            if (state.Phase == CoopHideoutBossPhase.AwaitingHostChoice)
            {
                CaptureMissionMode();
                Mission.SetMissionMode(MissionMode.Conversation, false);
                bool isLocalHost = IsLocalHost(state);
                if (CoopHideoutBossPhaseContract.ShouldReleaseCinematicCameraForBossConversation(
                        state.Phase))
                {
                    ReleaseCamera(preserveCameraFrame: false);
                }
                if (CoopHideoutBossPhaseContract.ShouldUseObserverCameraForBossConversation(
                        isLocalHost,
                        state.Phase))
                {
                    TrySetupObserverConversationCamera();
                }
                if (CoopHideoutBossPhaseContract.ShouldMaintainLocalHostFacingBoss(
                        isLocalHost,
                        state.Phase))
                {
                    MaintainLocalHostFacingBoss();
                }
                ShowBossConversation(state);
                return;
            }

            if (state.Phase == CoopHideoutBossPhase.Duel)
            {
                ReleaseCamera(preserveCameraFrame: false);
                RestoreMissionMode();
                ScheduleCombatCameraAlignment(state);
                ScreenFadeController.BeginFadeIn(0.3f);
                if (IsLocalHost(state))
                {
                    InformationManager.DisplayMessage(
                        new InformationMessage("Defeat the bandit leader in single combat."));
                }
                else
                {
                    InformationManager.DisplayMessage(
                        new InformationMessage("The campaign host accepted the duel."));
                }
                return;
            }

            if (state.Phase == CoopHideoutBossPhase.AllBattle)
            {
                ReleaseCamera(preserveCameraFrame: false);
                RestoreMissionMode();
                ScheduleCombatCameraAlignment(state);
                ScreenFadeController.BeginFadeIn(0.3f);
                InformationManager.DisplayMessage(
                    new InformationMessage("Fight together and defeat the bandit leader."));
                return;
            }

            if (state.Phase == CoopHideoutBossPhase.Completed)
            {
                ReleaseCamera(preserveCameraFrame: false);
                RestoreMissionMode();
            }
        }

        private void ShowBossConversation(CoopHideoutBossPhaseSession state)
        {
            if (state == null || MissionScreen == null ||
                !CoopHideoutBossPhaseContract.ShouldShowBossConversation(state.Phase))
            {
                return;
            }
            if (_conversationRevision == state.Revision && _conversationLayer != null)
                return;

            CloseBossConversation();
            CaptureMissionLayerState();
            try
            {
                Agent bossAgent = ResolveAgent(state.BossAgentIndex);
                bool choicesEnabled =
                    CoopHideoutBossPhaseContract.ShouldEnableBossConversationChoices(
                        IsLocalHost(state),
                        state.Phase);
                _conversationViewModel = new CoopHideoutBossConversationVM(
                    ResolveBossConversationDisplayName(bossAgent),
                    choicesEnabled,
                    SendHostChoiceWithConfirmation);
                _conversationSpriteCategory =
                    UIResourceManager.LoadSpriteCategory("ui_conversation");
                _conversationLayer = new GauntletLayer(
                    "MissionConversation",
                    ViewOrderPriority + 4,
                    false)
                {
                    IsFocusLayer = true
                };
                MissionScreen.AddLayer(_conversationLayer);
                _conversationMovie = _conversationLayer.LoadMovie(
                    "SPConversation",
                    _conversationViewModel);
                RegisterConversationInputCategories();
                _conversationLayer.InputRestrictions.SetInputRestrictions(
                    true,
                    InputUsageMask.All);
                _conversationLayer.InputRestrictions.SetMouseVisibility(true);
                MissionScreen.SetConversationActive(true);
                ((ScreenBase)MissionScreen).SetLayerCategoriesStateAndDeactivateOthers(
                    new[] { "MissionConversation", "SceneLayer" },
                    true);
                ScreenManager.TrySetFocus(_conversationLayer);
                InformationManager.HideAllMessages();
                _conversationRevision = state.Revision;
            }
            catch (Exception ex)
            {
                ModLogger.Error(
                    "CoopHideoutBossCinematicView: synchronized campaign conversation failed to open.",
                    ex);
                CloseBossConversation();
            }
        }

        private static void SendHostChoiceWithConfirmation(CoopHideoutBossChoice choice)
        {
            string selectionText = choice == CoopHideoutBossChoice.Duel
                ? "Selected: fight the bandit leader one-on-one. Waiting for server confirmation."
                : "Selected: fight the bandit leader together. Waiting for server confirmation.";
            InformationManager.DisplayMessage(new InformationMessage(selectionText));
            ModLogger.Info(
                "CoopHideoutBossCinematicView: host selected boss-fight command. Choice=" + choice + ".");
            CoopHideoutBossPhaseController.SendHostChoice(choice);
        }

        private void CloseBossConversation()
        {
            if (_conversationViewModel == null &&
                _conversationLayer == null &&
                _conversationSpriteCategory == null &&
                _conversationRevision < 0 &&
                !_missionLayerStateCaptured)
            {
                return;
            }

            try
            {
                _conversationViewModel?.OnFinalize();
            }
            catch
            {
            }
            try
            {
                if (_conversationLayer != null)
                {
                    _conversationLayer.InputRestrictions.ResetInputRestrictions();
                    _conversationLayer.InputRestrictions.SetMouseVisibility(false);
                    _conversationLayer.IsFocusLayer = false;
                    ScreenManager.TryLoseFocus(_conversationLayer);
                    if (_conversationMovie != null)
                        _conversationLayer.ReleaseMovie(_conversationMovie);
                    MissionScreen?.RemoveLayer(_conversationLayer);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopHideoutBossCinematicView: synchronized campaign conversation layer release failed. Error=" +
                    ex.Message + ".");
            }
            try
            {
                MissionScreen?.SetConversationActive(false);
                RestoreMissionLayerState();
            }
            catch
            {
            }
            try
            {
                _conversationSpriteCategory?.Unload();
            }
            catch
            {
            }
            _conversationMovie = null;
            _conversationLayer = null;
            _conversationViewModel = null;
            _conversationSpriteCategory = null;
            _conversationRevision = -1;
        }

        private void CaptureMissionLayerState()
        {
            _missionLayerActivationSnapshots.Clear();
            _focusedLayerBeforeConversation = null;
            _missionLayerStateCaptured = false;
            if (MissionScreen == null)
                return;

            ScreenBase missionScreen = MissionScreen;
            foreach (ScreenLayer layer in missionScreen.Layers)
            {
                if (layer != null)
                {
                    _missionLayerActivationSnapshots.Add(
                        new MissionLayerActivationSnapshot(layer, layer.IsActive));
                }
            }
            _focusedLayerBeforeConversation = ScreenManager.FocusedLayer;
            _missionLayerStateCaptured = true;
        }

        private void RestoreMissionLayerState()
        {
            if (!_missionLayerStateCaptured)
                return;

            ScreenBase missionScreen = MissionScreen;
            ScreenLayer previousFocusedLayer = _focusedLayerBeforeConversation;
            try
            {
                if (missionScreen != null)
                {
                    foreach (MissionLayerActivationSnapshot snapshot in
                             _missionLayerActivationSnapshots)
                    {
                        ScreenLayer layer = snapshot.Layer;
                        if (layer == null ||
                            layer.IsFinalized ||
                            !missionScreen.HasLayer(layer) ||
                            layer.IsActive == snapshot.WasActive)
                        {
                            continue;
                        }

                        MethodInfo transitionMethod = snapshot.WasActive
                            ? ScreenLayerHandleActivateMethod
                            : ScreenLayerHandleDeactivateMethod;
                        transitionMethod?.Invoke(layer, Array.Empty<object>());
                    }

                    if (previousFocusedLayer != null &&
                        !previousFocusedLayer.IsFinalized &&
                        previousFocusedLayer.IsActive &&
                        missionScreen.HasLayer(previousFocusedLayer))
                    {
                        ScreenLayer currentFocusedLayer = ScreenManager.FocusedLayer;
                        if (currentFocusedLayer != null &&
                            !ReferenceEquals(currentFocusedLayer, previousFocusedLayer))
                        {
                            ScreenManager.TryLoseFocus(currentFocusedLayer);
                        }
                        ScreenManager.TrySetFocus(previousFocusedLayer);
                    }
                }
            }
            finally
            {
                _missionLayerActivationSnapshots.Clear();
                _focusedLayerBeforeConversation = null;
                _missionLayerStateCaptured = false;
            }
        }

        private static string ResolveBossConversationDisplayName(Agent bossAgent)
        {
            string exactDisplayName = null;
            if (bossAgent != null &&
                CoopMissionSpawnLogic.TryResolveExactDisplayNameForAgent(
                    bossAgent,
                    out _,
                    out var exactName))
            {
                exactDisplayName = exactName?.ToString();
            }

            return CoopHideoutAmbushContract.ResolveBossConversationDisplayName(
                exactDisplayName,
                bossAgent?.Name?.ToString());
        }

        private void RegisterConversationInputCategories()
        {
            if (_conversationLayer == null)
                return;

            GameKeyContext conversationCategory =
                HotKeyManager.GetCategory("ConversationHotKeyCategory");
            GameKeyContext genericPanelCategory =
                HotKeyManager.GetCategory("GenericPanelGameKeyCategory");
            if (conversationCategory != null)
            {
                _conversationLayer.Input.RegisterHotKeyCategory(conversationCategory);
                if (MissionScreen?.SceneLayer?.Input != null &&
                    !MissionScreen.SceneLayer.Input.IsCategoryRegistered(conversationCategory))
                {
                    MissionScreen.SceneLayer.Input.RegisterHotKeyCategory(conversationCategory);
                }
            }
            if (genericPanelCategory != null)
            {
                _conversationLayer.Input.RegisterHotKeyCategory(genericPanelCategory);
                if (MissionScreen?.SceneLayer?.Input != null &&
                    !MissionScreen.SceneLayer.Input.IsCategoryRegistered(genericPanelCategory))
                {
                    MissionScreen.SceneLayer.Input.RegisterHotKeyCategory(genericPanelCategory);
                }
            }
        }

        private sealed class MissionLayerActivationSnapshot
        {
            public MissionLayerActivationSnapshot(ScreenLayer layer, bool wasActive)
            {
                Layer = layer;
                WasActive = wasActive;
            }

            public ScreenLayer Layer { get; }
            public bool WasActive { get; }
        }

        private void MaintainLocalHostFacingBoss()
        {
            if (_activeState == null || !IsLocalHost(_activeState))
                return;

            Agent hostAgent = ResolveAgent(_activeState.HostAgentIndex);
            Agent bossAgent = ResolveAgent(_activeState.BossAgentIndex);
            if (hostAgent?.IsActive() != true || bossAgent?.IsActive() != true)
                return;

            Vec2 direction = (bossAgent.Position - hostAgent.Position).AsVec2;
            if (direction.LengthSquared < 0.0001f)
                return;

            direction.Normalize();
            Vec3 lookDirection = new Vec3(direction.x, direction.y, 0f);
            if (_mainAgentController == null)
                _mainAgentController = Mission?.GetMissionBehavior<MissionMainAgentController>();
            if (_mainAgentController != null)
            {
                if (!_customLookDirectionCaptured)
                {
                    _customLookDirectionBeforeConversation =
                        _mainAgentController.CustomLookDir;
                    _customLookDirectionCaptured = true;
                }
                _mainAgentController.CustomLookDir = lookDirection;
                if (_activeState.Phase == CoopHideoutBossPhase.AwaitingHostChoice)
                {
                    try
                    {
                        _mainAgentController.InteractionComponent.SetCurrentFocusedObject(
                            (IFocusable)(object)bossAgent,
                            null,
                            -1,
                            true);
                        _conversationFocusedHostAgent = hostAgent;
                        _conversationFocusedBossAgent = bossAgent;
                    }
                    catch
                    {
                    }
                }
            }
            hostAgent.LookDirection = lookDirection;
            if (_activeState.Phase == CoopHideoutBossPhase.AwaitingHostChoice)
            {
                try
                {
                    hostAgent.SetLookAgent(bossAgent);
                    _conversationFocusedHostAgent = hostAgent;
                    _conversationFocusedBossAgent = bossAgent;
                }
                catch
                {
                }
            }
            hostAgent.SetMovementDirection(in direction);
        }

        private void ReleaseLocalHostFacingOverride(bool clearConversationLookDirection = true)
        {
            if (!_customLookDirectionCaptured &&
                _conversationFocusedHostAgent == null &&
                _conversationFocusedBossAgent == null)
            {
                return;
            }

            try
            {
                if (_mainAgentController != null)
                {
                    _mainAgentController.CustomLookDir =
                        clearConversationLookDirection
                            ? Vec3.Zero
                            : _customLookDirectionBeforeConversation;
                }
            }
            catch
            {
            }
            try
            {
                if (_conversationFocusedHostAgent != null &&
                    _conversationFocusedHostAgent.GetLookAgent() == _conversationFocusedBossAgent)
                {
                    _conversationFocusedHostAgent.SetLookAgent(null);
                }
            }
            catch
            {
            }
            try
            {
                _mainAgentController?.InteractionComponent.SetCurrentFocusedObject(
                    null,
                    null,
                    -1,
                    true);
            }
            catch
            {
            }
            _customLookDirectionBeforeConversation = Vec3.Zero;
            _customLookDirectionCaptured = false;
            _conversationFocusedHostAgent = null;
            _conversationFocusedBossAgent = null;
            _mainAgentController = null;
        }

        private bool TrySetupObserverConversationCamera()
        {
            if (_camera != null || MissionScreen == null || _activeState == null)
                return _camera != null;

            Agent hostAgent = ResolveAgent(_activeState.HostAgentIndex);
            Agent bossAgent = ResolveAgent(_activeState.BossAgentIndex);
            if (hostAgent?.IsActive() != true || bossAgent?.IsActive() != true)
                return false;

            try
            {
                Vec3 bossPosition = bossAgent.Position;
                Vec3 hostPosition = hostAgent.Position;
                Vec3 cameraPositionFromBoss = hostPosition - bossPosition;
                cameraPositionFromBoss.RotateAboutZ(-MathF.PI / 3f);
                cameraPositionFromBoss += bossPosition;

                Vec3 cameraPositionFromHost = bossPosition - hostPosition;
                cameraPositionFromHost.RotateAboutZ(-MathF.PI / 3f);
                cameraPositionFromHost += hostPosition;

                Vec3 observerPosition = Agent.Main?.Position ?? hostPosition;
                Vec3 cameraPosition =
                    (cameraPositionFromBoss - observerPosition).LengthSquared <=
                    (cameraPositionFromHost - observerPosition).LengthSquared
                        ? cameraPositionFromBoss
                        : cameraPositionFromHost;
                cameraPosition.z += Agent.Main?.GetEyeGlobalHeight() ?? 1.6f;

                Vec3 focusPosition =
                    (hostPosition - bossPosition) * 0.5f + bossPosition;
                Vec3 direction = focusPosition - cameraPosition;
                if (direction.LengthSquared < 0.0001f)
                    direction = Vec3.Forward;
                direction.Normalize();

                _camera = Camera.CreateCamera();
                Camera combatCamera = MissionScreen.CombatCamera;
                if ((NativeObject)(object)combatCamera != (NativeObject)null)
                    _camera.FillParametersFrom(combatCamera);
                _camera.SetFovHorizontal(
                    MathF.PI / 2f,
                    Screen.AspectRatio,
                    0.1f,
                    2000f);
                SetCameraFrame(cameraPosition, direction, out _cameraFrame);
                _camera.Frame = _cameraFrame;
                _cameraPathReady = false;
                MissionScreen.CustomCamera = _camera;
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopHideoutBossCinematicView: observer conversation camera setup failed. Error=" +
                    ex.Message);
                try { _camera?.ReleaseCamera(); } catch { }
                _camera = null;
                _cameraPathReady = false;
                return false;
            }
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

                SetupVanillaCameraPath(hostAgent, bossAgent);
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

            if (!_cameraPathReady)
                SetupVanillaCameraPath(hostAgent, bossAgent);
            if (!_cameraPathReady)
                return;

            Vec3 bossEye = bossAgent.GetEyeGlobalPosition();
            Vec3 desiredCameraPosition =
                _cameraStartPosition + _cameraMoveDirection * _cameraSpeed * _cinematicElapsed;
            Vec3 cameraPosition = ClampToAuthoredCameraVolume(desiredCameraPosition);
            cameraPosition = ResolveCollisionAwareCameraPosition(bossEye, cameraPosition);
            Vec3 direction = bossEye - cameraPosition;
            if (direction.LengthSquared < 0.0001f)
                direction = Vec3.Forward;
            direction.Normalize();
            SetCameraFrame(cameraPosition, direction, out _cameraFrame);
            _camera.Frame = _cameraFrame;
        }

        private void SetupVanillaCameraPath(Agent hostAgent, Agent bossAgent)
        {
            _cameraPathReady = false;
            if (hostAgent == null || bossAgent == null)
                return;

            Vec3 hostEye = hostAgent.GetEyeGlobalPosition();
            Vec3 bossEye = bossAgent.GetEyeGlobalPosition();
            Vec3 bossDirection = bossEye - hostEye;
            if (bossDirection.LengthSquared < 0.0001f)
                bossDirection = Vec3.Forward;
            bossDirection.Normalize();

            float innerRadius = ResolveAuthoredBossFightParameter(
                "InnerRadius",
                DefaultBossFightInnerRadius);
            float outerRadius = ResolveAuthoredBossFightParameter(
                "OuterRadius",
                DefaultBossFightOuterRadius);
            float walkDistance = ResolveAuthoredBossFightParameter(
                "WalkDistance",
                DefaultBossFightWalkDistance);
            float cinematicDuration = Math.Max(
                0.1f,
                _activeCinematicDurationMilliseconds / 1000f);
            _cameraSpeed =
                (innerRadius + outerRadius + 1.5f * walkDistance) /
                cinematicDuration;
            _cameraMoveDirection = -bossDirection;

            SetCameraFrame(bossEye, bossDirection, out MatrixFrame bossFrame);
            _cameraStartPosition =
                bossFrame.origin +
                0.3f * bossFrame.rotation.s +
                0.3f * bossFrame.rotation.f +
                1.2f * bossFrame.rotation.u;
            _cameraPathReady = true;
        }

        private float ResolveAuthoredBossFightParameter(string fieldName, float fallbackValue)
        {
            try
            {
                ScriptComponentBehavior behavior = ResolveAuthoredBossFightBehavior();
                FieldInfo field = behavior?.GetType().GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field?.GetValue(behavior) is float value &&
                    value >= 0f &&
                    !float.IsNaN(value) &&
                    !float.IsInfinity(value))
                {
                    return value;
                }
            }
            catch
            {
            }

            return fallbackValue;
        }

        private ScriptComponentBehavior ResolveAuthoredBossFightBehavior()
        {
            try
            {
                GameEntity entity = Mission?.Scene?.FindEntityWithTag(
                    CoopHideoutBossPhaseContract.BossFightEntityTag);
                return entity?.GetScriptComponents()
                    .FirstOrDefault(component =>
                        string.Equals(
                            component?.GetType().FullName,
                            "SandBox.Objects.Cinematics.HideoutBossFightBehavior",
                            StringComparison.Ordinal));
            }
            catch
            {
                return null;
            }
        }

        private Vec3 ClampToAuthoredCameraVolume(Vec3 desiredPosition)
        {
            try
            {
                ScriptComponentBehavior behavior = ResolveAuthoredBossFightBehavior();
                MethodInfo clampMethod = behavior?.GetType().GetMethod(
                    "ClampWorldPointToCameraVolume",
                    BindingFlags.Instance | BindingFlags.Public,
                    binder: null,
                    types: new[] { typeof(Vec3).MakeByRefType(), typeof(Vec3).MakeByRefType() },
                    modifiers: null);
                if (clampMethod == null)
                    return desiredPosition;

                object[] arguments = { desiredPosition, Vec3.Zero };
                clampMethod.Invoke(behavior, arguments);
                if (arguments[1] is Vec3 clampedPosition)
                    return clampedPosition;
            }
            catch (Exception ex)
            {
                ModLogger.Verbose(
                    "CoopHideoutBossCinematicView: authored camera-volume clamp unavailable. " +
                    "Error=" + ex.GetType().Name + ":" + ex.Message + ".");
            }

            return desiredPosition;
        }

        private Vec3 ResolveCollisionAwareCameraPosition(Vec3 focus, Vec3 desiredPosition)
        {
            try
            {
                if (Mission?.Scene == null)
                    return desiredPosition;

                Vec3 ray = desiredPosition - focus;
                float rayLength = ray.Length;
                if (rayLength < 0.5f)
                    return desiredPosition;
                ray /= rayLength;

                if (!Mission.Scene.RayCastForClosestEntityOrTerrain(
                        focus,
                        desiredPosition,
                        out float collisionDistance,
                        out Vec3 collisionPoint,
                        out WeakGameEntity collidedEntity,
                        0.15f,
                        (BodyFlags)67188481))
                {
                    return desiredPosition;
                }

                if (collisionDistance >= rayLength - 0.2f)
                    return desiredPosition;

                Vec3 corrected = collisionPoint - ray * 0.35f;
                return (corrected - focus).LengthSquared >= 2.25f
                    ? corrected
                    : focus - ray * 1.5f;
            }
            catch
            {
                return desiredPosition;
            }
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

        private void ReleaseCamera(bool preserveCameraFrame = true)
        {
            if (_camera == null)
                return;

            try
            {
                if (MissionScreen?.CustomCamera == _camera)
                {
                    if (preserveCameraFrame)
                        MissionScreen.UpdateFreeCamera(_camera.Frame);
                    MissionScreen.CustomCamera = null;
                }
            }
            catch
            {
            }
            try { _camera.ReleaseCamera(); } catch { }
            _camera = null;
            _cameraPathReady = false;
        }

        private void ScheduleCombatCameraAlignment(CoopHideoutBossPhaseSession state)
        {
            if (state == null ||
                !CoopHideoutBossPhaseContract.ShouldAlignLocalHostCombatCameraWithBoss(
                    IsLocalHost(state),
                    state.Phase))
            {
                return;
            }

            _combatCameraAlignmentPending = true;
            _combatCameraAlignmentDelayTicks = 1;
        }

        private void TickPendingCombatCameraAlignment()
        {
            if (!_combatCameraAlignmentPending)
                return;
            if (_combatCameraAlignmentDelayTicks > 0)
            {
                _combatCameraAlignmentDelayTicks--;
                return;
            }

            _combatCameraAlignmentPending = false;
            if (MissionScreen == null || _activeState == null ||
                !CoopHideoutBossPhaseContract.ShouldAlignLocalHostCombatCameraWithBoss(
                    IsLocalHost(_activeState),
                    _activeState.Phase))
            {
                return;
            }

            Agent hostAgent = ResolveAgent(_activeState.HostAgentIndex);
            Agent bossAgent = ResolveAgent(_activeState.BossAgentIndex);
            if (hostAgent?.IsActive() != true || bossAgent?.IsActive() != true)
                return;

            Vec2 direction = (bossAgent.VisualPosition - hostAgent.VisualPosition).AsVec2;
            if (direction.LengthSquared < 0.0001f)
                return;

            float previousBearing = MissionScreen.CameraBearing;
            direction.Normalize();
            float alignedBearing = new Vec3(direction.x, direction.y, 0f).RotationZ;
            MissionScreen.CameraBearing = alignedBearing;
            if (CoopDebugConfig.HideoutBossChoreographyDiagnostics)
            {
                ModLogger.Info(
                    "CoopHideoutBossCinematicView: aligned local host combat camera with boss. " +
                    "Battle=" + (_activeState?.BattleInstanceId ?? "none") +
                    " Phase=" + (_activeState?.Phase.ToString() ?? "none") +
                    " PreviousBearing=" + previousBearing +
                    " AlignedBearing=" + alignedBearing +
                    " HostVisualPosition=" + hostAgent.VisualPosition +
                    " BossVisualPosition=" + bossAgent.VisualPosition +
                    " Direction=" + direction + ".");
            }
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
