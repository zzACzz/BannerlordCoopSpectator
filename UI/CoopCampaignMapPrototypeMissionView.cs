using System;
using System.Collections.Generic;
using System.Text;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.Engine.Screens;
using TaleWorlds.GauntletUI;
using TaleWorlds.Library;
using TaleWorlds.InputSystem;
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
        private const string EntityOverlayMovieName = "PartyNameplate";
        private const string SettlementOverlayMovieName = "SettlementNameplate";
        private const string ReplicaInfoMovieName = "CoopCampaignMapReplicaInfo";
        private const float RenderReadinessTimeoutSeconds = 5f;
        private const int MaximumPartyVisualCreationAttempts = 3;
        private const float PartyVisualRetryDelaySeconds = 0.25f;
        private const float PartyVisualStableResetSeconds = 2f;

        private sealed class EntityReplica
        {
            public CoopCampaignMapPrototypeEntityState Target { get; set; }

            public double DisplayedX { get; set; }

            public double DisplayedY { get; set; }

            public double DisplayedHeading { get; set; }

            public Vec3 WorldPosition { get; set; }

            public GameEntity Marker { get; set; }

            public CoopCampaignMapPrototypePartyVisual PartyVisual { get; set; }

            public string PartyVisualAttemptKey { get; set; }

            public int PartyVisualAttemptCount { get; set; }

            public float PartyVisualRetryDelayRemaining { get; set; }

            public float PartyVisualStableSeconds { get; set; }

            public Vec3 PreviousWorldPosition { get; set; }

            public bool HasPreviousWorldPosition { get; set; }
        }

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
        private readonly Dictionary<string, EntityReplica> _entityReplicas =
            new Dictionary<string, EntityReplica>(StringComparer.OrdinalIgnoreCase);
        private IReadOnlyList<CoopCampaignMapPrototypeEntityState>
            _pendingVisibleEntities;
        private int _pendingVisibleEntitiesRevision = -1;
        private int _appliedVisibleEntitiesRevision = -1;
        private GauntletLayer _entityOverlayLayer;
        private GauntletMovieIdentifier _entityOverlayMovie;
        private CoopCampaignMapPrototypeEntityOverlayVM _entityOverlayViewModel;
        private GauntletLayer _settlementOverlayLayer;
        private GauntletMovieIdentifier _settlementOverlayMovie;
        private CoopCampaignMapPrototypeSettlementOverlayVM
            _settlementOverlayViewModel;
        private GauntletLayer _replicaInfoLayer;
        private GauntletMovieIdentifier _replicaInfoMovie;
        private CoopCampaignMapReplicaInfoVM _replicaInfoViewModel;
        private bool _missionMouseVisibilityCaptured;
        private bool _previousMissionMouseVisible;
        private readonly Dictionary<string, CoopCampaignMapPrototypeCatalogEntityState>
            _catalogById =
                new Dictionary<string, CoopCampaignMapPrototypeCatalogEntityState>(
                    StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, CoopCampaignMapPrototypeDynamicEntityState>
            _dynamicById =
                new Dictionary<string, CoopCampaignMapPrototypeDynamicEntityState>(
                    StringComparer.OrdinalIgnoreCase);
        private string _selectedReplicaId;
        private float _replicaHoverUpdateDelay;
        private Vec3 _sceneMinimum;
        private Vec3 _sceneMaximum;
        private double _displayedX = 0.5d;
        private double _displayedY = 0.5d;
        private double _displayedHeading;
        private bool _hasDisplayedCamera;
        private bool _hostCameraInitialized;
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
        private bool _mapSceneReadyToRender;
        private bool _mainPartyVisualActive;
        private int _appliedNormalizedTimeOfDay = -1;
        private int _appliedSeasonTimeFactor = -1;

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            CoopCampaignMapPrototypeNetworkController.ClientStateChanged +=
                OnClientStateChanged;
            CoopCampaignMapPrototypeNetworkController.ClientVisibleEntitiesChanged +=
                OnClientVisibleEntitiesChanged;
            CoopCampaignMapPrototypeNetworkController.ClientCatalogChanged +=
                OnClientCatalogChanged;
            CoopCampaignMapPrototypeNetworkController.ClientDynamicChanged +=
                OnClientDynamicChanged;
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

            ApplyPendingVisibleEntities();
            CoopCampaignMapPrototypeState current =
                _targetState ??
                CoopCampaignMapPrototypeNetworkController.CurrentClientState;
            if (current != null)
            {
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
                if (_mapSceneReadyToRender)
                    ApplyMapVisualState(current);
                UpdateDisplayedCamera(current.Camera, interpolationAmount);
            }
            UpdateEntityReplicas(dt);
            TickReplicaFreeCamera(dt);
            UpdateMarkerAndCamera();
            UpdateReplicaInformation(dt);
        }

        public override void OnMissionScreenFinalize()
        {
            CoopCampaignMapPrototypeNetworkController.ClientStateChanged -=
                OnClientStateChanged;
            CoopCampaignMapPrototypeNetworkController.ClientVisibleEntitiesChanged -=
                OnClientVisibleEntitiesChanged;
            CoopCampaignMapPrototypeNetworkController.ClientCatalogChanged -=
                OnClientCatalogChanged;
            CoopCampaignMapPrototypeNetworkController.ClientDynamicChanged -=
                OnClientDynamicChanged;
            ReleaseSceneLayer();
            base.OnMissionScreenFinalize();
        }

        private void OnClientStateChanged(CoopCampaignMapPrototypeState state)
        {
            if (state != null)
                _targetState = state;
        }

        private void OnClientVisibleEntitiesChanged(
            int revision,
            IReadOnlyList<CoopCampaignMapPrototypeEntityState> entities)
        {
            QueueVisibleEntities(revision, entities);
        }

        private void OnClientCatalogChanged(
            int revision,
            IReadOnlyList<CoopCampaignMapPrototypeCatalogEntityState> entities)
        {
            _catalogById.Clear();
            if (entities == null)
                return;
            foreach (CoopCampaignMapPrototypeCatalogEntityState entity in entities)
            {
                if (entity != null)
                    _catalogById[entity.EntityId] = entity.Clone();
            }
        }

        private void OnClientDynamicChanged(
            int revision,
            IReadOnlyList<CoopCampaignMapPrototypeDynamicEntityState> entities)
        {
            _dynamicById.Clear();
            if (entities == null)
                return;
            foreach (CoopCampaignMapPrototypeDynamicEntityState entity in entities)
            {
                if (entity != null)
                    _dynamicById[entity.EntityId] = entity.Clone();
            }
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
                CreateEntityOverlayLayer();
                CreateSettlementOverlayLayer();
                CreateReplicaInfoLayer();

                _mapScene.PreloadForRendering();
                _mapScene.CheckResources(checkInvisibleEntities: false);

                CoopCampaignMapPrototypeState initialState =
                    CoopCampaignMapPrototypeNetworkController.CurrentClientState;
                if (initialState != null)
                    _targetState = initialState;
                QueueVisibleEntities(
                    CoopCampaignMapPrototypeNetworkController
                        .CurrentClientVisibleEntitiesRevision,
                    CoopCampaignMapPrototypeNetworkController
                        .CurrentClientVisibleEntities);
                OnClientCatalogChanged(
                    0,
                    CoopCampaignMapPrototypeNetworkController.CurrentClientCatalog);
                OnClientDynamicChanged(
                    0,
                    CoopCampaignMapPrototypeNetworkController.CurrentClientDynamic);
                ApplyPendingVisibleEntities();
                UpdateEntityReplicas(0f);
                UpdateMarkerAndCamera();
                InitializeLocalFreeCamera();
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

        private void CreateEntityOverlayLayer()
        {
            if (_entityOverlayViewModel != null || MissionScreen == null)
                return;

            _entityOverlayViewModel =
                new CoopCampaignMapPrototypeEntityOverlayVM();
            _entityOverlayLayer = new GauntletLayer(
                "CoopCampaignMapPrototypeEntities",
                ViewOrderPriority + 1,
                false);
            MissionScreen.AddLayer(_entityOverlayLayer);
            _entityOverlayMovie = _entityOverlayLayer.LoadMovie(
                EntityOverlayMovieName,
                _entityOverlayViewModel);
        }

        private void CreateSettlementOverlayLayer()
        {
            if (_settlementOverlayViewModel != null || MissionScreen == null)
                return;

            _settlementOverlayViewModel =
                new CoopCampaignMapPrototypeSettlementOverlayVM();
            _settlementOverlayLayer = new GauntletLayer(
                "CoopCampaignMapPrototypeSettlements",
                ViewOrderPriority + 2,
                false);
            MissionScreen.AddLayer(_settlementOverlayLayer);
            _settlementOverlayMovie = _settlementOverlayLayer.LoadMovie(
                SettlementOverlayMovieName,
                _settlementOverlayViewModel);
        }

        private void CreateReplicaInfoLayer()
        {
            if (_replicaInfoViewModel != null || MissionScreen == null)
                return;
            _replicaInfoViewModel = new CoopCampaignMapReplicaInfoVM();
            _replicaInfoLayer = new GauntletLayer(
                "CoopCampaignMapReplicaInfo",
                ViewOrderPriority + 3,
                false);
            _previousMissionMouseVisible = MissionScreen.MouseVisible;
            _missionMouseVisibilityCaptured = true;
            _replicaInfoLayer.InputRestrictions.SetMouseVisibility(true);
            MissionScreen.MouseVisible = true;
            MissionScreen.AddLayer(_replicaInfoLayer);
            _replicaInfoMovie = _replicaInfoLayer.LoadMovie(
                ReplicaInfoMovieName,
                _replicaInfoViewModel);
        }

        private void QueueVisibleEntities(
            int revision,
            IReadOnlyList<CoopCampaignMapPrototypeEntityState> entities)
        {
            if (revision < 0 ||
                revision <= _appliedVisibleEntitiesRevision ||
                revision < _pendingVisibleEntitiesRevision)
            {
                return;
            }

            var clone = new List<CoopCampaignMapPrototypeEntityState>();
            if (entities != null)
            {
                foreach (CoopCampaignMapPrototypeEntityState entity in entities)
                {
                    if (entity != null)
                        clone.Add(entity.Clone());
                }
            }
            _pendingVisibleEntitiesRevision = revision;
            _pendingVisibleEntities = clone;
        }

        private void ApplyPendingVisibleEntities()
        {
            if (_mapScene == null ||
                _pendingVisibleEntities == null ||
                _pendingVisibleEntitiesRevision <= _appliedVisibleEntitiesRevision)
            {
                return;
            }

            var observed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CoopCampaignMapPrototypeEntityState entity in
                     _pendingVisibleEntities)
            {
                if (!CoopCampaignMapPrototypeContract.IsValidVisibleEntity(entity) ||
                    !observed.Add(entity.EntityId))
                {
                    continue;
                }

                if (!_entityReplicas.TryGetValue(
                        entity.EntityId,
                        out EntityReplica replica))
                {
                    replica = new EntityReplica
                    {
                        DisplayedX =
                            CoopCampaignMapPrototypeContract.DequantizeUnit(
                                entity.NormalizedX),
                        DisplayedY =
                            CoopCampaignMapPrototypeContract.DequantizeUnit(
                                entity.NormalizedY),
                        DisplayedHeading =
                            CoopCampaignMapPrototypeContract.DequantizeHeading(
                                entity.Heading),
                        Marker = entity.Kind ==
                                 CoopCampaignMapPrototypeEntityKind.MobileParty
                            ? CreateHostMarker(_mapScene)
                            : null
                    };
                    _entityReplicas.Add(entity.EntityId, replica);
                }
                string visualAttemptKey = BuildPartyVisualAttemptKey(entity);
                if (!string.Equals(
                        replica.PartyVisualAttemptKey,
                        visualAttemptKey,
                        StringComparison.Ordinal))
                {
                    ResetPartyVisual(replica);
                    replica.PartyVisualAttemptKey = visualAttemptKey;
                    replica.PartyVisualAttemptCount = 0;
                    replica.PartyVisualRetryDelayRemaining = 0f;
                }
                if (entity.Kind !=
                    CoopCampaignMapPrototypeEntityKind.MobileParty &&
                    replica.Marker != null)
                {
                    RemoveMarker(replica.Marker);
                    replica.Marker = null;
                }
                else if (entity.Kind ==
                         CoopCampaignMapPrototypeEntityKind.MobileParty &&
                         replica.Marker == null)
                {
                    replica.Marker = CreateHostMarker(_mapScene);
                }
                replica.Target = entity.Clone();
            }

            var removed = new List<string>();
            foreach (KeyValuePair<string, EntityReplica> pair in _entityReplicas)
            {
                if (!observed.Contains(pair.Key))
                    removed.Add(pair.Key);
            }
            foreach (string entityId in removed)
            {
                EntityReplica replica = _entityReplicas[entityId];
                ResetPartyVisual(replica);
                RemoveMarker(replica.Marker);
                _entityReplicas.Remove(entityId);
            }

            _entityOverlayViewModel?.Synchronize(_pendingVisibleEntities);
            _settlementOverlayViewModel?.Synchronize(_pendingVisibleEntities);
            _appliedVisibleEntitiesRevision = _pendingVisibleEntitiesRevision;
            _pendingVisibleEntitiesRevision = -1;
            _pendingVisibleEntities = null;
        }

        private void UpdateEntityReplicas(float dt)
        {
            if (_mapScene == null || _camera == null)
                return;

            _mainPartyVisualActive = false;
            double amount = dt <= 0f ? 1d : Math.Min(1d, dt * 6d);
            float horizontalSpan = Math.Max(
                _sceneMaximum.x - _sceneMinimum.x,
                _sceneMaximum.y - _sceneMinimum.y);
            float markerScale = Math.Max(0.6f, horizontalSpan * 0.0018f);
            foreach (KeyValuePair<string, EntityReplica> pair in _entityReplicas)
            {
                EntityReplica replica = pair.Value;
                CoopCampaignMapPrototypeEntityState target = replica.Target;
                if (target == null)
                    continue;

                if (target.Kind == CoopCampaignMapPrototypeEntityKind.MainParty)
                {
                    replica.DisplayedX = _displayedX;
                    replica.DisplayedY = _displayedY;
                    replica.DisplayedHeading = _displayedHeading;
                }
                else
                {
                    replica.DisplayedX =
                        CoopCampaignMapPrototypeContract.InterpolateUnit(
                            replica.DisplayedX,
                            CoopCampaignMapPrototypeContract.DequantizeUnit(
                                target.NormalizedX),
                            amount);
                    replica.DisplayedY =
                        CoopCampaignMapPrototypeContract.InterpolateUnit(
                            replica.DisplayedY,
                            CoopCampaignMapPrototypeContract.DequantizeUnit(
                                target.NormalizedY),
                            amount);
                    replica.DisplayedHeading = InterpolateHeading(
                        replica.DisplayedHeading,
                        CoopCampaignMapPrototypeContract.DequantizeHeading(
                            target.Heading),
                        amount);
                }

                replica.WorldPosition = ResolveMapWorldPosition(
                    replica.DisplayedX,
                    replica.DisplayedY);
                Vec3 movement = replica.HasPreviousWorldPosition
                    ? replica.WorldPosition - replica.PreviousWorldPosition
                    : Vec3.Zero;
                float movementDistance = (float)Math.Sqrt(
                    movement.x * movement.x +
                    movement.y * movement.y +
                    movement.z * movement.z);
                bool isMoving =
                    replica.HasPreviousWorldPosition &&
                    dt > 0f &&
                    movementDistance > 0.0005f;
                float visualSpeed = isMoving
                    ? movementDistance / dt
                    : 0f;
                replica.PreviousWorldPosition = replica.WorldPosition;
                replica.HasPreviousWorldPosition = true;

                MatrixFrame visualFrame = MatrixFrame.Identity;
                visualFrame.rotation.RotateAboutUp(
                    (float)replica.DisplayedHeading);
                visualFrame.origin = replica.WorldPosition;
                replica.PartyVisualRetryDelayRemaining = Math.Max(
                    0f,
                    replica.PartyVisualRetryDelayRemaining - Math.Max(0f, dt));
                TryEnsurePartyVisual(replica, target, visualFrame);
                if (replica.PartyVisual != null)
                {
                    try
                    {
                        replica.PartyVisual.Update(
                            visualFrame,
                            dt,
                            isMoving,
                            visualSpeed);
                        if (!replica.PartyVisual.IsUsable)
                        {
                            ResetPartyVisual(replica);
                            replica.PartyVisualRetryDelayRemaining =
                                PartyVisualRetryDelaySeconds;
                        }
                        else
                        {
                            replica.PartyVisualStableSeconds += Math.Max(0f, dt);
                            if (replica.PartyVisualStableSeconds >=
                                PartyVisualStableResetSeconds)
                            {
                                replica.PartyVisualAttemptCount = 0;
                            }
                        }
                    }
                    catch
                    {
                        ResetPartyVisual(replica);
                        replica.PartyVisualRetryDelayRemaining =
                            PartyVisualRetryDelaySeconds;
                    }
                }

                bool hasPartyVisual =
                    replica.PartyVisual?.IsUsable == true;
                if (target.Kind ==
                    CoopCampaignMapPrototypeEntityKind.MainParty)
                {
                    _mainPartyVisualActive = hasPartyVisual;
                }
                if (replica.Marker != null)
                {
                    MatrixFrame markerFrame = MatrixFrame.Identity;
                    markerFrame.rotation.RotateAboutUp(
                        (float)replica.DisplayedHeading);
                    markerFrame.origin = replica.WorldPosition +
                                         Vec3.Up * (markerScale * 0.15f);
                    Vec3 scale = Vec3.One * markerScale;
                    markerFrame.Scale(in scale);
                    replica.Marker.SetFrame(ref markerFrame, true);
                    replica.Marker.SetVisibilityExcludeParents(
                        !hasPartyVisual);
                }

                _entityOverlayViewModel?.UpdatePosition(
                    pair.Key,
                    replica.WorldPosition,
                    replica.WorldPosition + Vec3.Up * (markerScale * 1.2f),
                    _camera);
                _settlementOverlayViewModel?.UpdatePosition(
                    pair.Key,
                    replica.WorldPosition,
                    _camera);
            }
        }

        private void TryEnsurePartyVisual(
            EntityReplica replica,
            CoopCampaignMapPrototypeEntityState target,
            MatrixFrame initialFrame)
        {
            if (!_mapSceneReadyToRender ||
                _mapScene == null ||
                replica == null ||
                replica.PartyVisual != null ||
                target == null ||
                target.Kind == CoopCampaignMapPrototypeEntityKind.Settlement ||
                target.PartyVisualKind ==
                    CoopCampaignMapPrototypePartyVisualKind.None)
            {
                return;
            }

            if (replica.PartyVisualAttemptCount >=
                    MaximumPartyVisualCreationAttempts ||
                replica.PartyVisualRetryDelayRemaining > 0f)
            {
                return;
            }

            replica.PartyVisualAttemptCount++;
            if (CoopCampaignMapPrototypePartyVisual.TryCreate(
                    _mapScene,
                    target,
                    initialFrame,
                    out CoopCampaignMapPrototypePartyVisual visual))
            {
                replica.PartyVisual = visual;
                replica.PartyVisualStableSeconds = 0f;
                replica.PartyVisualRetryDelayRemaining = 0f;
            }
            else if (replica.PartyVisualAttemptCount <
                     MaximumPartyVisualCreationAttempts)
            {
                replica.PartyVisualRetryDelayRemaining =
                    PartyVisualRetryDelaySeconds;
            }
        }

        private void UpdateReplicaInformation(float dt)
        {
            if (_replicaInfoViewModel == null || _camera == null)
                return;

            bool clicked = Input.IsKeyPressed(InputKey.LeftMouseButton);
            _replicaHoverUpdateDelay -= Math.Max(0f, dt);
            if (!clicked && _replicaHoverUpdateDelay > 0f)
                return;
            _replicaHoverUpdateDelay = 0.08f;

            Vec2 rangedMouse =
                TaleWorlds.InputSystem.Input.MousePositionRanged;
            Vec2 mouse = new Vec2(
                rangedMouse.x * Screen.RealScreenResolutionWidth,
                rangedMouse.y * Screen.RealScreenResolutionHeight);
            string hoveredId = null;
            float nearestDistanceSquared = 70f * 70f;
            foreach (KeyValuePair<string, EntityReplica> pair in _entityReplicas)
            {
                EntityReplica replica = pair.Value;
                if (replica?.Target == null)
                    continue;
                try
                {
                    float x = -500f;
                    float y = -500f;
                    float depth = 0f;
                    MBWindowManager.WorldToScreenInsideUsableArea(
                        _camera,
                        replica.WorldPosition,
                        ref x,
                        ref y,
                        ref depth);
                    if (depth <= 0f)
                        continue;
                    float dx = x - mouse.x;
                    float dy = y - mouse.y;
                    float distanceSquared = dx * dx + dy * dy;
                    if (distanceSquared < nearestDistanceSquared)
                    {
                        nearestDistanceSquared = distanceSquared;
                        hoveredId = pair.Key;
                    }
                }
                catch
                {
                }
            }

            if (clicked && hoveredId != null)
            {
                _selectedReplicaId = string.Equals(
                    _selectedReplicaId,
                    hoveredId,
                    StringComparison.OrdinalIgnoreCase)
                    ? null
                    : hoveredId;
            }

            string effectiveId = !string.IsNullOrWhiteSpace(_selectedReplicaId)
                ? _selectedReplicaId
                : hoveredId;
            bool pinned = !string.IsNullOrWhiteSpace(_selectedReplicaId);
            if (effectiveId == null ||
                !_catalogById.TryGetValue(
                    effectiveId,
                    out CoopCampaignMapPrototypeCatalogEntityState catalog) ||
                !_dynamicById.TryGetValue(
                    effectiveId,
                    out CoopCampaignMapPrototypeDynamicEntityState dynamicState) ||
                !dynamicState.IsVisible)
            {
                if (pinned)
                    _selectedReplicaId = null;
                _replicaInfoViewModel.Hide();
                return;
            }

            _replicaInfoViewModel.Show(
                catalog,
                dynamicState,
                mouse,
                pinned,
                Screen.RealScreenResolutionWidth,
                Screen.RealScreenResolutionHeight);
        }

        private static string BuildPartyVisualAttemptKey(
            CoopCampaignMapPrototypeEntityState state)
        {
            if (state == null)
                return string.Empty;

            var builder = new StringBuilder(512);
            builder.Append((int)state.PartyVisualKind).Append('|');
            AppendVisualKeyToken(builder, state.VisualCharacterId);
            AppendVisualKeyToken(builder, state.CultureId);
            AppendVisualKeyToken(builder, state.BannerCode);
            builder.Append(state.PrimaryColor).Append('|')
                .Append(state.SecondaryColor).Append('|');
            AppendAgentVisualKey(builder, state.HumanVisual);
            AppendAgentVisualKey(builder, state.MountVisual);
            AppendAgentVisualKey(builder, state.CaravanMountVisual);
            return builder.ToString();
        }

        private static void AppendAgentVisualKey(
            StringBuilder builder,
            CoopCampaignMapPrototypeAgentVisualState visual)
        {
            if (visual == null)
            {
                builder.Append("null|");
                return;
            }

            builder.Append(visual.IsFemale ? '1' : '0').Append('|')
                .Append(visual.Race).Append('|')
                .Append(visual.SkeletonType).Append('|')
                .Append(visual.RightWieldedItemIndex).Append('|')
                .Append(visual.LeftWieldedItemIndex).Append('|')
                .Append(visual.HasBanner ? '1' : '0').Append('|')
                .Append(visual.AddColorRandomness ? '1' : '0').Append('|');
            AppendVisualKeyToken(builder, visual.BodyProperties);
            AppendVisualKeyToken(builder, visual.MountCreationKey);
            string[] itemIds = visual.EquipmentItemIds;
            for (int slot = 0;
                 slot < CoopCampaignMapPrototypeContract.EquipmentSlotCount;
                 slot++)
            {
                AppendVisualKeyToken(
                    builder,
                    itemIds != null && slot < itemIds.Length
                        ? itemIds[slot]
                        : string.Empty);
            }
        }

        private static void AppendVisualKeyToken(
            StringBuilder builder,
            string value)
        {
            string safeValue = value ?? string.Empty;
            builder.Append(safeValue.Length).Append(':')
                .Append(safeValue).Append('|');
        }

        private static void ResetPartyVisual(EntityReplica replica)
        {
            if (replica?.PartyVisual == null)
                return;

            CoopCampaignMapPrototypePartyVisual visual = replica.PartyVisual;
            replica.PartyVisual = null;
            replica.PartyVisualStableSeconds = 0f;
            visual.Reset();
        }

        private Vec3 ResolveMapWorldPosition(double normalizedX, double normalizedY)
        {
            float x = _sceneMinimum.x +
                      (_sceneMaximum.x - _sceneMinimum.x) * (float)normalizedX;
            float y = _sceneMinimum.y +
                      (_sceneMaximum.y - _sceneMinimum.y) * (float)normalizedY;
            float terrainHeight = _mapScene.GetTerrainHeight(new Vec2(x, y));
            if (!IsFinite(terrainHeight))
                terrainHeight = 0f;
            return new Vec3(x, y, terrainHeight);
        }

        private static void RemoveMarker(GameEntity marker)
        {
            if (marker == null)
                return;
            try
            {
                marker.Remove(103);
            }
            catch
            {
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
                if (viewReady && sceneReady)
                    _mapSceneReadyToRender = true;

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
            _hostMarker.SetVisibilityExcludeParents(
                !_mainPartyVisualActive);

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

        private void ApplyMapVisualState(
            CoopCampaignMapPrototypeState state)
        {
            if (!_mapSceneReadyToRender || _mapScene == null || state == null ||
                state.NormalizedTimeOfDay < 0 ||
                state.NormalizedTimeOfDay >
                    CoopCampaignMapPrototypeContract.UnitScale ||
                state.SeasonTimeFactor < 0 ||
                state.SeasonTimeFactor >
                    CoopCampaignMapPrototypeContract.UnitScale)
            {
                return;
            }
            if (state.NormalizedTimeOfDay == _appliedNormalizedTimeOfDay &&
                state.SeasonTimeFactor == _appliedSeasonTimeFactor)
            {
                return;
            }

            float timeOfDay = (float)
                CoopCampaignMapPrototypeContract.DequantizeTimeOfDay(
                    state.NormalizedTimeOfDay);
            float seasonTimeFactor = (float)
                CoopCampaignMapPrototypeContract.DequantizeUnit(
                    state.SeasonTimeFactor);
            _mapScene.TimeOfDay = timeOfDay;
            MBMapScene.SetSeasonTimeFactor(_mapScene, seasonTimeFactor);

            _appliedNormalizedTimeOfDay = state.NormalizedTimeOfDay;
            _appliedSeasonTimeFactor = state.SeasonTimeFactor;
        }

        private void ReleaseSceneLayer()
        {
            ReleaseReplicaInfoLayer();
            ReleaseSettlementOverlayLayer();
            ReleaseEntityOverlayLayer();
            foreach (EntityReplica replica in _entityReplicas.Values)
            {
                ResetPartyVisual(replica);
                RemoveMarker(replica.Marker);
            }
            _entityReplicas.Clear();
            _pendingVisibleEntities = null;
            _pendingVisibleEntitiesRevision = -1;
            _appliedVisibleEntitiesRevision = -1;
            _catalogById.Clear();
            _dynamicById.Clear();
            _selectedReplicaId = null;

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
            _mapSceneReadyToRender = false;
            _mainPartyVisualActive = false;
            _appliedNormalizedTimeOfDay = -1;
            _appliedSeasonTimeFactor = -1;
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

        private void ReleaseEntityOverlayLayer()
        {
            try
            {
                if (_entityOverlayLayer != null && _entityOverlayMovie != null)
                    _entityOverlayLayer.ReleaseMovie(_entityOverlayMovie);
                if (_entityOverlayLayer != null)
                    MissionScreen?.RemoveLayer(_entityOverlayLayer);
                _entityOverlayViewModel?.OnFinalize();
            }
            catch
            {
            }
            finally
            {
                _entityOverlayMovie = null;
                _entityOverlayLayer = null;
                _entityOverlayViewModel = null;
            }
        }

        private void ReleaseSettlementOverlayLayer()
        {
            try
            {
                if (_settlementOverlayLayer != null &&
                    _settlementOverlayMovie != null)
                {
                    _settlementOverlayLayer.ReleaseMovie(
                        _settlementOverlayMovie);
                }
                if (_settlementOverlayLayer != null)
                    MissionScreen?.RemoveLayer(_settlementOverlayLayer);
                _settlementOverlayViewModel?.OnFinalize();
            }
            catch
            {
            }
            finally
            {
                _settlementOverlayMovie = null;
                _settlementOverlayLayer = null;
                _settlementOverlayViewModel = null;
            }
        }

        private void ReleaseReplicaInfoLayer()
        {
            try
            {
                if (_replicaInfoLayer != null && _replicaInfoMovie != null)
                    _replicaInfoLayer.ReleaseMovie(_replicaInfoMovie);
                if (_replicaInfoLayer != null)
                {
                    _replicaInfoLayer.InputRestrictions.ResetInputRestrictions();
                    _replicaInfoLayer.InputRestrictions.SetMouseVisibility(false);
                }
                if (_replicaInfoLayer != null)
                    MissionScreen?.RemoveLayer(_replicaInfoLayer);
                if (_missionMouseVisibilityCaptured && MissionScreen != null)
                    MissionScreen.MouseVisible = _previousMissionMouseVisible;
                _replicaInfoViewModel?.OnFinalize();
            }
            catch
            {
            }
            finally
            {
                _replicaInfoMovie = null;
                _replicaInfoLayer = null;
                _replicaInfoViewModel = null;
                _missionMouseVisibilityCaptured = false;
            }
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

            if (_hostCameraInitialized)
                return;

            _displayedCameraOrigin = targetOrigin;
            _displayedCameraDirection = targetDirection;
            _displayedCameraUp = targetUp;
            _displayedCameraFov = targetFov;
            _hasDisplayedCamera = true;
            _hostCameraInitialized = true;
        }

        private void InitializeLocalFreeCamera()
        {
            if (_hasDisplayedCamera || _camera == null)
                return;

            MatrixFrame frame = _camera.Frame;
            Vec3 direction = _camera.Direction;
            Vec3 up = frame.rotation.f;
            if (!TryNormalizeCameraBasis(ref direction, ref up))
                return;

            _displayedCameraOrigin = frame.origin;
            _displayedCameraDirection = direction;
            _displayedCameraUp = up;
            _displayedCameraFov = 0.6981317f;
            _hasDisplayedCamera = true;
        }

        private void TickReplicaFreeCamera(float dt)
        {
            if (!_hasDisplayedCamera || _mapScene == null)
                return;

            float safeDt = Math.Max(0f, Math.Min(0.1f, dt));
            Vec3 direction = _displayedCameraDirection;
            Vec3 up = _displayedCameraUp;
            if (!TryNormalizeCameraBasis(ref direction, ref up))
                return;

            Vec3 side = Cross(direction, up);
            float sideLength = side.Normalize();
            if (sideLength <= 0.0001f)
                return;

            if (Input.IsKeyDown(InputKey.RightMouseButton))
            {
                float yaw = Input.GetMouseMoveX() * 0.0025f;
                float pitch = -Input.GetMouseMoveY() * 0.0025f;
                if (Math.Abs(yaw) > 0.00001f)
                {
                    direction = RotateAroundAxis(direction, Vec3.Up, yaw);
                    up = RotateAroundAxis(up, Vec3.Up, yaw);
                }
                side = Cross(direction, up);
                side.Normalize();
                if (Math.Abs(pitch) > 0.00001f)
                {
                    Vec3 candidateDirection =
                        RotateAroundAxis(direction, side, pitch);
                    Vec3 candidateUp = RotateAroundAxis(up, side, pitch);
                    if (candidateDirection.z > -0.98f &&
                        candidateDirection.z < 0.98f)
                    {
                        direction = candidateDirection;
                        up = candidateUp;
                    }
                }
                TryNormalizeCameraBasis(ref direction, ref up);
            }

            float moveForward = 0f;
            float moveSide = 0f;
            float moveUp = 0f;
            if (Input.IsKeyDown(InputKey.W))
                moveForward += 1f;
            if (Input.IsKeyDown(InputKey.S))
                moveForward -= 1f;
            if (Input.IsKeyDown(InputKey.D))
                moveSide += 1f;
            if (Input.IsKeyDown(InputKey.A))
                moveSide -= 1f;
            if (Input.IsKeyDown(InputKey.Space) || Input.IsKeyDown(InputKey.E))
                moveUp += 1f;
            if (Input.IsKeyDown(InputKey.LeftControl) ||
                Input.IsKeyDown(InputKey.RightControl) ||
                Input.IsKeyDown(InputKey.Q))
            {
                moveUp -= 1f;
            }

            side = Cross(direction, up);
            side.Normalize();
            Vec3 movement =
                direction * moveForward +
                side * moveSide +
                Vec3.Up * moveUp;
            if (movement.IsNonZero)
            {
                movement.Normalize();
                float horizontalSpan = Math.Max(
                    _sceneMaximum.x - _sceneMinimum.x,
                    _sceneMaximum.y - _sceneMinimum.y);
                float speed = Math.Max(20f, horizontalSpan * 0.08f);
                if (Input.IsKeyDown(InputKey.LeftShift) ||
                    Input.IsKeyDown(InputKey.RightShift))
                {
                    speed *= 3f;
                }
                _displayedCameraOrigin += movement * (speed * safeDt);
                _displayedCameraOrigin.x = Math.Max(
                    _sceneMinimum.x,
                    Math.Min(_sceneMaximum.x, _displayedCameraOrigin.x));
                _displayedCameraOrigin.y = Math.Max(
                    _sceneMinimum.y,
                    Math.Min(_sceneMaximum.y, _displayedCameraOrigin.y));
                _displayedCameraOrigin.z = Math.Max(
                    1f,
                    Math.Min(horizontalSpan * 2f, _displayedCameraOrigin.z));
            }

            _displayedCameraDirection = direction;
            _displayedCameraUp = up;
        }

        private static Vec3 Cross(Vec3 left, Vec3 right)
        {
            return new Vec3(
                left.y * right.z - left.z * right.y,
                left.z * right.x - left.x * right.z,
                left.x * right.y - left.y * right.x);
        }

        private static Vec3 RotateAroundAxis(Vec3 value, Vec3 axis, float angle)
        {
            float axisLength = axis.Normalize();
            if (axisLength <= 0.0001f)
                return value;
            float cosine = (float)Math.Cos(angle);
            float sine = (float)Math.Sin(angle);
            Vec3 cross = Cross(axis, value);
            float dot = axis.x * value.x + axis.y * value.y + axis.z * value.z;
            return value * cosine +
                   cross * sine +
                   axis * (dot * (1f - cosine));
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
