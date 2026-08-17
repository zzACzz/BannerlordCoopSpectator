using System;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Engine.Screens;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View.MissionViews;
using TaleWorlds.ScreenSystem;

namespace CoopSpectator.UI
{
    /// <summary>
    /// Experimental client-only renderer that places Main_map in a separate scene layer.
    /// It deliberately does not create Campaign.Current or any campaign managers.
    /// </summary>
    public sealed class CoopCampaignMapPrototypeMissionView : MissionView
    {
        private const string MarkerMeshName = "order_flag_small";
        private const float RenderReadinessTimeoutSeconds = 5f;

        private sealed class PassiveSceneLayer : SceneLayer
        {
            public PassiveSceneLayer(
                bool clearSceneOnFinalize,
                bool autoToggleSceneView)
                : base(clearSceneOnFinalize, autoToggleSceneView)
            {
                IsFocusLayer = false;
            }

            public override bool HitTest()
            {
                return false;
            }

            public override bool FocusTest()
            {
                return false;
            }
        }

        private SceneLayer _sceneLayer;
        private Scene _mapScene;
        private MBAgentRendererSceneController _agentRendererSceneController;
        private Camera _camera;
        private GameEntity _hostMarker;
        private Vec3 _sceneMinimum;
        private Vec3 _sceneMaximum;
        private double _displayedX = 0.5d;
        private double _displayedY = 0.5d;
        private double _displayedHeading;
        private bool _hasDisplayedCamera;
        private Vec3 _displayedCameraOrigin;
        private Vec3 _displayedCameraDirection;
        private Vec3 _displayedCameraUp;
        private float _displayedCameraFov = 0.6981317f;
        private CoopCampaignMapPrototypeState _targetState;
        private float _startupDelay = 0.25f;
        private float _renderReadinessElapsed;
        private bool _loadAttempted;
        private bool _initialRenderStateLogged;
        private bool _readyRenderStateLogged;
        private bool _timeoutRenderStateLogged;
        private bool _renderStateProbeFailed;

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            CoopCampaignMapPrototypeNetworkController.ClientStateChanged +=
                OnClientStateChanged;
        }

        public override void OnMissionScreenInitialize()
        {
            base.OnMissionScreenInitialize();
            ViewOrderPriority = 1;
        }

        public override void OnMissionScreenTick(float dt)
        {
            base.OnMissionScreenTick(dt);
            if (!GameNetwork.IsClient || !ExperimentalFeatures.EnableCampaignMapPrototype)
                return;

            if (_sceneLayer == null && !_loadAttempted)
            {
                _startupDelay -= dt;
                if (_startupDelay <= 0f)
                    TryLoadCampaignMap();
                return;
            }

            if (_sceneLayer == null)
                return;

            UpdateRenderReadiness(dt);
            if (_hostMarker == null)
                return;

            CoopCampaignMapPrototypeState current =
                _targetState ??
                CoopCampaignMapPrototypeNetworkController.CurrentClientState;
            if (current == null)
                return;

            double interpolationAmount = dt <= 0f
                ? 0d
                : Math.Min(1d, dt * 8d);
            _displayedX = CoopCampaignMapPrototypeContract.InterpolateUnit(
                _displayedX,
                CoopCampaignMapPrototypeContract.DequantizeUnit(current.NormalizedX),
                interpolationAmount);
            _displayedY = CoopCampaignMapPrototypeContract.InterpolateUnit(
                _displayedY,
                CoopCampaignMapPrototypeContract.DequantizeUnit(current.NormalizedY),
                interpolationAmount);
            _displayedHeading = InterpolateHeading(
                _displayedHeading,
                CoopCampaignMapPrototypeContract.DequantizeHeading(current.Heading),
                interpolationAmount);
            UpdateDisplayedCamera(current.Camera, interpolationAmount);
            UpdateMarkerAndCamera();
        }

        public override void OnMissionScreenFinalize()
        {
            CoopCampaignMapPrototypeNetworkController.ClientStateChanged -=
                OnClientStateChanged;
            ReleaseSceneLayer();
            base.OnMissionScreenFinalize();
        }

        private void OnClientStateChanged(CoopCampaignMapPrototypeState state)
        {
            if (state != null)
                _targetState = state;
        }

        private void TryLoadCampaignMap()
        {
            _loadAttempted = true;
            try
            {
                string[] activeModules = Utilities.GetModulesNames();
                string ownerModule =
                    CoopCampaignMapPrototypeContract.ResolveLastOwningModule(
                        activeModules,
                        moduleId => Utilities.GetSingleModuleScenesOfModule(moduleId));
                if (string.IsNullOrWhiteSpace(ownerModule))
                {
                    ModLogger.Info(
                        "CoopCampaignMapPrototypeMissionView: Main_map owner module was not found among active modules.");
                    return;
                }

                _mapScene = Scene.CreateNewScene(
                    initialize_physics: false,
                    enable_decals: true,
                    atlasGroup: (DecalAtlasGroup)1,
                    sceneName: "CoopCampaignMapPrototype");
                if (_mapScene == null)
                {
                    ModLogger.Info(
                        "CoopCampaignMapPrototypeMissionView: Scene.CreateNewScene returned null.");
                    return;
                }

                _mapScene.SetClothSimulationState(true);
                _agentRendererSceneController =
                    MBAgentRendererSceneController.CreateNewAgentRendererSceneController(
                        _mapScene);
                _agentRendererSceneController.SetDoTimerBasedForcedSkeletonUpdates(false);
                _mapScene.SetOcclusionMode(true);

                SceneInitializationData initData =
                    new SceneInitializationData(initializeWithDefaults: true)
                    {
                        UsePhysicsMaterials = false,
                        EnableFloraPhysics = false,
                        UseTerrainMeshBlending = false,
                        CreateOros = false
                    };
                _mapScene.SetFetchCrcInfoOfScene(true);
                _mapScene.SetNavMeshRegionMap(CreateDefaultMapNavMeshRegionMap());

                ModLogger.Info(
                    "CoopCampaignMapPrototypeMissionView: entering Main_map Scene.Read with stock-compatible native prerequisites.");
                using (CoopCampaignMapPrototypeSceneLoadScope.Enter())
                {
                    _mapScene.Read(
                        CoopCampaignMapPrototypeContract.CampaignMapScene,
                        ownerModule,
                        ref initData,
                        string.Empty);
                }
                ModLogger.Info(
                    "CoopCampaignMapPrototypeMissionView: Main_map Scene.Read returned successfully.");

                _mapScene.DisableStaticShadows(true);
                _mapScene.InvalidateTerrainPhysicsMaterials();
                _mapScene.SetDontLoadInvisibleEntities(true);
                _mapScene.OptimizeScene(optimizeFlora: true, optimizeOro: false);

                ResolveSceneLimits(_mapScene, out _sceneMinimum, out _sceneMaximum);
                _hostMarker = CreateHostMarker(_mapScene);
                _camera = CreateMapCamera(_sceneMinimum, _sceneMaximum);
                if (_hostMarker == null || _camera == null)
                    throw new InvalidOperationException(
                        "The prototype marker or camera could not be created.");

                _sceneLayer = new PassiveSceneLayer(
                    clearSceneOnFinalize: true,
                    autoToggleSceneView: true);
                _sceneLayer.SetScene(_mapScene);
                _sceneLayer.SetCamera(_camera);
                _sceneLayer.SetSceneUsesSkybox(true);
                _sceneLayer.SetSceneUsesShadows(true);
                _sceneLayer.SetSceneUsesContour(false);
                _sceneLayer.SetRenderWithPostfx(true);
                _sceneLayer.SetPostfxFromConfig();
                _sceneLayer.SceneView.SetAcceptGlobalDebugRenderObjects(true);
                _sceneLayer.SceneView.SetResolutionScaling(true);
                MissionScreen.AddLayer(_sceneLayer);

                _mapScene.PreloadForRendering();
                _mapScene.CheckResources(checkInvisibleEntities: false);

                CoopCampaignMapPrototypeState initialState =
                    CoopCampaignMapPrototypeNetworkController.CurrentClientState;
                if (initialState != null)
                    _targetState = initialState;
                UpdateMarkerAndCamera();
                ModLogger.Info(
                    "CoopCampaignMapPrototypeMissionView: Main_map loaded in isolated client scene layer. Module=" +
                    ownerModule +
                    " Min=" + _sceneMinimum +
                    " Max=" + _sceneMaximum + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Error(
                    "CoopCampaignMapPrototypeMissionView: Main_map load failed.",
                    ex);
                ReleaseSceneLayer();
            }
        }

        private void UpdateRenderReadiness(float dt)
        {
            if (_sceneLayer == null || _renderStateProbeFailed)
                return;

            if (dt > 0f)
                _renderReadinessElapsed += dt;

            try
            {
                bool viewReady = _sceneLayer.ReadyToRender();
                bool sceneReady = _sceneLayer.SceneView.CheckSceneReadyToRender();

                if (!_initialRenderStateLogged)
                {
                    _initialRenderStateLogged = true;
                    LogRenderState("initial", viewReady, sceneReady);
                }

                if (viewReady && sceneReady && !_readyRenderStateLogged)
                {
                    _readyRenderStateLogged = true;
                    LogRenderState("ready", viewReady, sceneReady);
                }
                else if (!_readyRenderStateLogged &&
                         !_timeoutRenderStateLogged &&
                         _renderReadinessElapsed >= RenderReadinessTimeoutSeconds)
                {
                    _timeoutRenderStateLogged = true;
                    LogRenderState("timeout", viewReady, sceneReady);
                }
            }
            catch (Exception ex)
            {
                _renderStateProbeFailed = true;
                ModLogger.Info(
                    "CoopCampaignMapPrototypeMissionView: render-state probe failed once and was disabled. Error=" +
                    ex.Message + ".");
            }
        }

        private void LogRenderState(
            string stage,
            bool viewReady,
            bool sceneReady)
        {
            float x = _sceneMinimum.x +
                      (_sceneMaximum.x - _sceneMinimum.x) * (float)_displayedX;
            float y = _sceneMinimum.y +
                      (_sceneMaximum.y - _sceneMinimum.y) * (float)_displayedY;
            float terrainHeight = _mapScene.GetTerrainHeight(new Vec2(x, y));
            Vec3 target = new Vec3(x, y, terrainHeight);
            Vec2 projectedTarget = _sceneLayer.WorldPointToScreenPoint(target);
            Vec3 cameraProjectedTarget =
                _camera.WorldPointToViewPortPoint(ref target);
            MatrixFrame cameraFrame = _camera.Frame;
            int missionLayerOrder = MissionScreen.SceneLayer.ScreenOrderInLastFrame;
            int mapLayerOrder = _sceneLayer.ScreenOrderInLastFrame;

            ModLogger.Info(
                "CoopCampaignMapPrototypeMissionView: render-state " + stage +
                ". ViewReady=" + viewReady +
                " SceneReady=" + sceneReady +
                " Elapsed=" + _renderReadinessElapsed.ToString("0.000") +
                " TerrainHeight=" + terrainHeight.ToString("0.000") +
                " Target=" + target +
                " ProjectedTarget=" + projectedTarget +
                " CameraProjectedTarget=" + cameraProjectedTarget +
                " CameraOrigin=" + cameraFrame.origin +
                " CameraSide=" + cameraFrame.rotation.s +
                " CameraScreenUp=" + cameraFrame.rotation.f +
                " CameraBackward=" + cameraFrame.rotation.u +
                " CameraDirection=" + _camera.Direction +
                " MissionLayerOrder=" + missionLayerOrder +
                " MapLayerOrder=" + mapLayerOrder + ".");
        }

        private static bool[] CreateDefaultMapNavMeshRegionMap()
        {
            TerrainType[] terrainTypes =
                (TerrainType[])Enum.GetValues(typeof(TerrainType));
            int maximumTerrainType = 0;
            foreach (TerrainType terrainType in terrainTypes)
            {
                int terrainTypeIndex = (int)terrainType;
                if (terrainTypeIndex > maximumTerrainType)
                    maximumTerrainType = terrainTypeIndex;
            }

            bool[] regionMap = new bool[maximumTerrainType + 1];
            DefaultPartyNavigationModel navigationModel =
                new DefaultPartyNavigationModel();
            foreach (TerrainType terrainType in terrainTypes)
            {
                int terrainTypeIndex = (int)terrainType;
                if (terrainTypeIndex < 0)
                    continue;

                regionMap[terrainTypeIndex] =
                    navigationModel.IsTerrainTypeValidForNavigationType(
                        terrainType,
                        MobileParty.NavigationType.Default);
            }

            return regionMap;
        }

        private static void ResolveSceneLimits(
            Scene scene,
            out Vec3 minimum,
            out Vec3 maximum)
        {
            GameEntity minimumBorder = scene.GetFirstEntityWithName("border_min");
            GameEntity maximumBorder = scene.GetFirstEntityWithName("border_max");
            if (minimumBorder != null && maximumBorder != null)
            {
                MatrixFrame minimumFrame = minimumBorder.GetGlobalFrame();
                MatrixFrame maximumFrame = maximumBorder.GetGlobalFrame();
                minimum = minimumFrame.origin;
                maximum = maximumFrame.origin;
                if (HasUsableHorizontalSpan(minimum, maximum))
                    return;
            }

            scene.GetSceneLimits(out minimum, out maximum);
            if (HasUsableHorizontalSpan(minimum, maximum))
                return;

            scene.GetBoundingBox(out minimum, out maximum);
            if (!HasUsableHorizontalSpan(minimum, maximum))
            {
                throw new InvalidOperationException(
                    "Main_map returned unusable scene limits and bounding box.");
            }
        }

        private static bool HasUsableHorizontalSpan(Vec3 minimum, Vec3 maximum)
        {
            return IsFinite(minimum.x) &&
                   IsFinite(minimum.y) &&
                   IsFinite(maximum.x) &&
                   IsFinite(maximum.y) &&
                   maximum.x - minimum.x > 1f &&
                   maximum.y - minimum.y > 1f;
        }

        private static GameEntity CreateHostMarker(Scene scene)
        {
            GameEntity marker = GameEntity.CreateEmpty(scene, true, true, true);
            if (marker == null)
                return null;

            MetaMesh mesh = MetaMesh.GetCopy(MarkerMeshName, true, false);
            if (mesh == null)
            {
                marker.Remove(103);
                return null;
            }

            marker.AddComponent(mesh);
            marker.SetMobility(GameEntity.Mobility.Dynamic);
            marker.SetVisibilityExcludeParents(true);
            return marker;
        }

        private static Camera CreateMapCamera(Vec3 minimum, Vec3 maximum)
        {
            Camera camera = Camera.CreateCamera();
            if (camera == null)
                return null;

            float horizontalSpan = Math.Max(
                maximum.x - minimum.x,
                maximum.y - minimum.y);
            camera.SetFovVertical(
                0.6981317f,
                Screen.AspectRatio,
                0.1f,
                Math.Max(5000f, horizontalSpan * 10f));
            return camera;
        }

        private void UpdateMarkerAndCamera()
        {
            if (_mapScene == null || _hostMarker == null || _camera == null)
                return;

            float x = _sceneMinimum.x +
                      (_sceneMaximum.x - _sceneMinimum.x) * (float)_displayedX;
            float y = _sceneMinimum.y +
                      (_sceneMaximum.y - _sceneMinimum.y) * (float)_displayedY;
            float terrainHeight = _mapScene.GetTerrainHeight(new Vec2(x, y));
            if (!IsFinite(terrainHeight))
                terrainHeight = 0f;

            float horizontalSpan = Math.Max(
                _sceneMaximum.x - _sceneMinimum.x,
                _sceneMaximum.y - _sceneMinimum.y);
            float markerScale = Math.Max(0.75f, horizontalSpan * 0.0025f);
            MatrixFrame markerFrame = MatrixFrame.Identity;
            markerFrame.rotation.RotateAboutUp((float)_displayedHeading);
            markerFrame.origin = new Vec3(x, y, terrainHeight + markerScale * 0.15f);
            Vec3 scale = Vec3.One * markerScale;
            markerFrame.Scale(in scale);
            _hostMarker.SetFrame(ref markerFrame, true);
            _hostMarker.SetVisibilityExcludeParents(true);

            float farPlane = Math.Max(5000f, horizontalSpan * 10f);
            if (_hasDisplayedCamera)
            {
                _camera.LookAt(
                    _displayedCameraOrigin,
                    _displayedCameraOrigin + _displayedCameraDirection,
                    _displayedCameraUp);
                _camera.SetFovVertical(
                    _displayedCameraFov,
                    Screen.AspectRatio,
                    0.01f,
                    farPlane);
            }
            else
            {
                float cameraDistance = Math.Max(75f, horizontalSpan * 0.12f);
                Vec3 target = new Vec3(x, y, terrainHeight);
                Vec3 cameraPosition = target +
                                      new Vec3(
                                          0f,
                                          -cameraDistance,
                                          cameraDistance * 0.8f);
                _camera.LookAt(cameraPosition, target, Vec3.Up);
                _camera.SetFovVertical(
                    0.6981317f,
                    Screen.AspectRatio,
                    0.1f,
                    farPlane);
            }
            _sceneLayer?.SetCamera(_camera);
        }

        private void ReleaseSceneLayer()
        {
            if (_agentRendererSceneController != null && _mapScene != null)
            {
                try
                {
                    MBAgentRendererSceneController.DestructAgentRendererSceneController(
                        _mapScene,
                        _agentRendererSceneController,
                        deleteThisFrame: false);
                }
                catch (Exception ex)
                {
                    ModLogger.Info(
                        "CoopCampaignMapPrototypeMissionView: agent-renderer controller cleanup failed. Error=" +
                        ex.Message + ".");
                }
            }
            _agentRendererSceneController = null;

            bool sceneOwnedByLayer = _sceneLayer != null;
            try
            {
                if (_sceneLayer != null)
                    MissionScreen?.RemoveLayer(_sceneLayer);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopCampaignMapPrototypeMissionView: scene-layer removal failed. Error=" +
                    ex.Message + ".");
            }

            if (!sceneOwnedByLayer && _mapScene != null)
            {
                try
                {
                    _mapScene.ClearAll();
                }
                catch
                {
                }
            }

            _sceneLayer = null;
            _mapScene = null;
            _hostMarker = null;
            try
            {
                _camera?.ReleaseCamera();
            }
            catch
            {
            }
            _camera = null;
        }

        private void UpdateDisplayedCamera(
            CoopCampaignMapPrototypeCameraState cameraState,
            double interpolationAmount)
        {
            if (!TryDecodeCamera(
                    cameraState,
                    out Vec3 targetOrigin,
                    out Vec3 targetDirection,
                    out Vec3 targetUp,
                    out float targetFov))
            {
                return;
            }

            if (!_hasDisplayedCamera)
            {
                _displayedCameraOrigin = targetOrigin;
                _displayedCameraDirection = targetDirection;
                _displayedCameraUp = targetUp;
                _displayedCameraFov = targetFov;
                _hasDisplayedCamera = true;
                return;
            }

            float amount = (float)interpolationAmount;
            Vec3 direction = Lerp(
                _displayedCameraDirection,
                targetDirection,
                amount);
            Vec3 up = Lerp(_displayedCameraUp, targetUp, amount);
            if (!TryNormalizeCameraBasis(ref direction, ref up))
                return;

            _displayedCameraOrigin = Lerp(
                _displayedCameraOrigin,
                targetOrigin,
                amount);
            _displayedCameraDirection = direction;
            _displayedCameraUp = up;
            _displayedCameraFov +=
                (targetFov - _displayedCameraFov) * amount;
        }

        private static bool TryDecodeCamera(
            CoopCampaignMapPrototypeCameraState cameraState,
            out Vec3 origin,
            out Vec3 direction,
            out Vec3 up,
            out float verticalFov)
        {
            origin = Vec3.Zero;
            direction = Vec3.Zero;
            up = Vec3.Zero;
            verticalFov = 0f;
            if (cameraState == null ||
                !CoopCampaignMapPrototypeContract.IsValidCameraState(cameraState))
            {
                return false;
            }

            origin = new Vec3(
                (float)CoopCampaignMapPrototypeContract.DequantizeWorldCoordinate(
                    cameraState.OriginX),
                (float)CoopCampaignMapPrototypeContract.DequantizeWorldCoordinate(
                    cameraState.OriginY),
                (float)CoopCampaignMapPrototypeContract.DequantizeWorldCoordinate(
                    cameraState.OriginZ));
            direction = new Vec3(
                (float)CoopCampaignMapPrototypeContract.DequantizeSignedUnit(
                    cameraState.DirectionX),
                (float)CoopCampaignMapPrototypeContract.DequantizeSignedUnit(
                    cameraState.DirectionY),
                (float)CoopCampaignMapPrototypeContract.DequantizeSignedUnit(
                    cameraState.DirectionZ));
            up = new Vec3(
                (float)CoopCampaignMapPrototypeContract.DequantizeSignedUnit(
                    cameraState.UpX),
                (float)CoopCampaignMapPrototypeContract.DequantizeSignedUnit(
                    cameraState.UpY),
                (float)CoopCampaignMapPrototypeContract.DequantizeSignedUnit(
                    cameraState.UpZ));
            verticalFov =
                (float)CoopCampaignMapPrototypeContract.DequantizeCameraFov(
                    cameraState.VerticalFov);
            return TryNormalizeCameraBasis(ref direction, ref up);
        }

        private static bool TryNormalizeCameraBasis(
            ref Vec3 direction,
            ref Vec3 up)
        {
            if (direction.Normalize() <= 0.0001f ||
                up.Normalize() <= 0.0001f)
            {
                return false;
            }

            Vec3 side = Vec3.CrossProduct(direction, up);
            if (side.Normalize() <= 0.0001f)
                return false;

            up = Vec3.CrossProduct(side, direction);
            return up.Normalize() > 0.0001f;
        }

        private static Vec3 Lerp(Vec3 from, Vec3 to, float amount)
        {
            float boundedAmount = amount < 0f
                ? 0f
                : amount > 1f
                    ? 1f
                    : amount;
            return from + (to - from) * boundedAmount;
        }

        private static double InterpolateHeading(
            double from,
            double to,
            double amount)
        {
            double fullTurn = Math.PI * 2d;
            double delta = (to - from) % fullTurn;
            if (delta > Math.PI)
                delta -= fullTurn;
            else if (delta < -Math.PI)
                delta += fullTurn;

            double result = from + delta * amount;
            result %= fullTurn;
            return result < 0d ? result + fullTurn : result;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
