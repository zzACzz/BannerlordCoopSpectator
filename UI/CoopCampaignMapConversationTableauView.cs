using System;
using System.Collections.Generic;
using System.Diagnostics;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Engine.GauntletUI;
using TaleWorlds.GauntletUI;
using TaleWorlds.GauntletUI.BaseTypes;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View;
using TaleWorlds.ScreenSystem;
using TaleWorlds.TwoDimension;
using Texture = TaleWorlds.Engine.Texture;

namespace CoopSpectator.UI
{
    // Visual-only scene owned by the map dialogue, never a campaign or battle mission.
    internal sealed class CoopCampaignMapConversationTableauView : IDisposable
    {
        internal const string LayerName = "CoopConversationVisual";
        private static int _lightingDescriptorSamples;

        private sealed class PassiveTextureLayer : GauntletLayer
        {
            public PassiveTextureLayer() : base(LayerName, 4, false) { IsFocusLayer = false; }
            public override bool HitTest() => false;
            public override bool FocusTest() => false;
        }

        // Instance-local drawing: no global texture-provider registration, shared
        // campaign cache or prefab override. This widget never owns the texture.
        private sealed class TableauTextureWidget : Widget
        {
            internal TaleWorlds.TwoDimension.Texture Image;
            internal string DrawFailure;
            internal bool HasSubmittedLoadedTarget;
            internal TableauTextureWidget(UIContext context) : base(context)
            {
                WidthSizePolicy = SizePolicy.Fixed;
                HeightSizePolicy = SizePolicy.Fixed;
                DoNotAcceptEvents = true;
                DoNotPassEventsToChildren = true;
                UpdateViewportSize();
            }

            protected override void OnUpdate(float dt)
            {
                base.OnUpdate(dt);
                UpdateViewportSize();
            }

            private void UpdateViewportSize()
            {
                // Match GauntletMovie's physical-pixel root sizing. UIContext.Root
                // uses unscaled SuggestedWidth/Height; stretching directly to it
                // multiplies the full-screen image by the current UI scale.
                ScaledSuggestedWidth = Context.TwoDimensionContext.Width;
                ScaledSuggestedHeight = Context.TwoDimensionContext.Height;
            }

            protected override void OnRender(TwoDimensionContext context, TwoDimensionDrawContext drawContext)
            {
                if (Image == null || DrawFailure != null) return;
                try
                {
                    if (!Image.IsValid) return;
                    SimpleMaterial material = drawContext.CreateSimpleMaterial();
                    material.Texture = Image;
                    material.Color = Color.White;
                    material.AlphaFactor = Context.ContextAlpha;
                    material.ColorFactor = 1f;
                    material.HueFactor = 0f;
                    material.SaturationFactor = 0f;
                    material.ValueFactor = 0f;
                    material.NinePatchParameters = SpriteNinePatchParameters.Empty;
                    material.OverlayEnabled = false;
                    material.CircularMaskingEnabled = false;
                    ImageDrawObject draw = ImageDrawObject.Create(in AreaRect, in Vec2.Zero, in Vec2.One);
                    draw.Scale = _scaleToUse;
                    drawContext.Draw(material, in draw);
                    // Submission is not proof of completed pixels. Together with
                    // successful target configuration and actor resources it admits
                    // bounded ticking; visual parity still requires runtime QA.
                    HasSubmittedLoadedTarget |= Image.IsLoaded();
                }
                catch (Exception ex) { DrawFailure = ex.GetType().Name; }
            }
        }

        private sealed class ResolvedActor
        {
            public CoopCampaignMapConversationActorState State;
            public BodyProperties Body;
            public Equipment Equipment;
            public Monster Monster;
            public MBActionSet ActionSet;
            public ActionIndexCache Action;
            public Banner Banner;
        }

        private readonly ScreenBase _screen;
        private readonly CoopCampaignMapConversationVisualLifetime _lifetime = new CoopCampaignMapConversationVisualLifetime();
        private readonly List<AgentVisuals> _actors = new List<AgentVisuals>();
        private readonly List<CoopConversationPosePlayback> _posePlayback = new List<CoopConversationPosePlayback>();
        private readonly List<string> _poseActions = new List<string>();
        private readonly CoopCampaignMapConversationPoseReceiver _poseReceiver = new CoopCampaignMapConversationPoseReceiver();
        private int _lastPoseTraceRevision = -1;
        private Scene _scene;
        private Camera _camera;
        private PassiveTextureLayer _layer;
        private TableauTextureWidget _widget;
        private Texture _texture;
        private object _targetEpoch;
        private TableauView _view;
        private string _callbackFailure;
        private static int _textureIndex;
        private int _textureWidth;
        private int _textureHeight;
        private MBAgentRendererSceneController _renderer;
        private float _baseFov;
        private CoopCampaignMapConversationMissionScene _missionScene;
        private float _aspect;
        private readonly Stopwatch _readinessClock = new Stopwatch();
        private double _lastReadinessSeconds;
        private bool _verboseAttempt;
        private bool _sceneProbeEnabled;
        private int _sceneProbeSample;
        private static int _sceneProbeProcessSamples;
        private bool _readinessStarted;
        private bool _targetConfigured;
        private int _lastActorReadyMask;

        internal CoopCampaignMapConversationTableauView(ScreenBase screen)
        { _screen = screen ?? throw new ArgumentNullException(nameof(screen)); }

        internal void Apply(CoopCampaignMapConversationTableauState state)
        {
            CoopConversationVisualChange change = _lifetime.Apply(state);
            if (change == CoopConversationVisualChange.Keep) return;
            ReleaseResources();
            if (change == CoopConversationVisualChange.Clear) return;
            try
            {
                if (!GameNetwork.IsClient) throw new InvalidOperationException("Client role required.");
                _readinessClock.Restart();
                _lastReadinessSeconds = 0d;
                _verboseAttempt = CoopDebugConfig.VerboseDiagnostics;
                _sceneProbeEnabled = _verboseAttempt && string.Equals(Environment.GetEnvironmentVariable(
                    "COOPSPECTATOR_MISSION_SCENE_LOADING_DIAGNOSTICS"), "1", StringComparison.Ordinal);
                _sceneProbeSample = 0;
                _readinessStarted = false;
                _targetConfigured = false;
                _lastActorReadyMask = 0;
                Create(state);
            }
            catch (Exception ex) { Fail(ex.GetType().Name); }
        }

        internal void Tick(float dt)
        {
            if (_scene == null || _layer == null || _lifetime.IsFailed) return;
            try
            {
                if (_layer.IsFinalized) { Fail("layer-finalized"); return; }
                if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0f) { Fail("invalid-tick"); return; }
                if (_callbackFailure != null || _widget.DrawFailure != null)
                { Fail(_callbackFailure ?? _widget.DrawFailure); return; }
                UpdateCameraAspect();
                EnsureTextureSize();
                ObserveMissionSceneLoading();
                bool ready = _lifetime.IsReady;
                if (!ready)
                {
                    if (!_readinessStarted) { TraceStage("readiness-begin"); _readinessStarted = true; }
                    // The installed stock tableau does not poll the world-view
                    // readiness flag; our cmt-b16 run drew a complete target
                    // while that flag stayed false. Admit target operation, not
                    // visual parity, without forcing full-scene preparation.
                    _lastActorReadyMask = 0;
                    for (int i = 0; i < _actors.Count; i++)
                    {
                        var visuals = _actors[i].GetVisuals();
                        if (visuals.GetSkeleton() != null && visuals.CheckResources(true))
                            _lastActorReadyMask |= 1 << i;
                    }
                    ready = CoopCampaignMapConversationVisualLifetime.IsTargetOperational(
                        _targetConfigured, _widget.HasSubmittedLoadedTarget, _actors.Count, _lastActorReadyMask);
                    double elapsed = _readinessClock.Elapsed.TotalSeconds;
                    ready = _lifetime.Advance(elapsed - _lastReadinessSeconds, ready);
                    _lastReadinessSeconds = elapsed;
                    if (_lifetime.IsFailed) { Fail("readiness-timeout"); return; }
                    if (ready) TraceStage("readiness-complete");
                }
                if (!ready) return;
                for (int i = 0; i < _posePlayback.Count; i++)
                {
                    Skeleton skeleton = _actors[i].GetVisuals().GetSkeleton();
                    // Native continuation may change action between host samples.
                    // Do not apply the previous action's correction to its successor.
                    if (skeleton.GetActionAtChannel(0).GetName() == _poseActions[i])
                        skeleton.SetAnimationSpeedAtChannel(0, _posePlayback[i].Step(
                            skeleton.GetAnimationParameterAtChannel(0), dt));
                }
                // Bounded per-frame work, not the native tableau's busy resource loop.
                _scene.Tick(Math.Min(dt, 0.1f));
                foreach (AgentVisuals actor in _actors) actor.TickVisuals();
            }
            catch (Exception ex) { Fail(ex.GetType().Name); }
        }

        internal void BindPose(CoopCampaignMapConversationState state) => _poseReceiver.Bind(state);

        // Runs on the owning mission screen tick, never on the packet callback.
        // False only means the scene is still loading; the caller retains at most
        // one recent sample. No scene, texture or UI allocation occurs here.
        internal bool ApplyPose(CoopCampaignMapConversationPose pose)
        {
            if (_lifetime.IsFailed) return true;
            if (!_lifetime.IsReady) return false;
            if (!_poseReceiver.TryAccept(pose, out var accepted)) return true;
            try
            {
                if (_actors.Count != accepted.Actors.Count) throw new InvalidOperationException("Pose actor count changed.");
                var actions = new ActionIndexCache[_actors.Count];
                var skeletons = new Skeleton[_actors.Count];
                var durations = new float[_actors.Count];
                var cyclic = new bool[_actors.Count];
                // Resolve and validate every local resource before changing any actor.
                for (int i = 0; i < _actors.Count; i++)
                {
                    actions[i] = ActionIndexCache.Create(accepted.Actors[i].ActionName);
                    skeletons[i] = _actors[i].GetVisuals().GetSkeleton();
                    var actionSet = _actors[i].GetCopyAgentVisualsData().ActionSetData;
                    if (skeletons[i] == null || actions[i].Index < 0 || !actionSet.IsValid ||
                        string.IsNullOrEmpty(actionSet.GetAnimationName(in actions[i])))
                        throw new InvalidOperationException("Pose animation unavailable.");
                    durations[i] = MBActionSet.GetActionAnimationDuration(actionSet, in actions[i]);
                    cyclic[i] = (MBActionSet.GetActionAnimationFlags(actionSet, in actions[i]) & AnimFlags.anf_cyclic) != 0;
                    if (float.IsNaN(durations[i]) || float.IsInfinity(durations[i]) || durations[i] <= 0f)
                        throw new InvalidOperationException("Pose animation duration unavailable.");
                }
                for (int i = 0; i < _actors.Count; i++)
                {
                    var motion = accepted.Actors[i];
                    if (_missionScene != null)
                    {
                        MatrixFrame actorFrame = Frame(motion.Frame);
                        actorFrame.rotation.ApplyScaleLocal(_actors[i].GetScale());
                        _actors[i].GetVisuals().SetFrame(ref actorFrame);
                        _actors[i].SetLookDirection(new Vec3(motion.LookDirection[0], motion.LookDirection[1], motion.LookDirection[2]));
                    }
                    bool changedAction = skeletons[i].GetActionAtChannel(0).GetName() != motion.ActionName;
                    bool seed = i >= _posePlayback.Count || _poseActions[i] != motion.ActionName || changedAction;
                    if (changedAction)
                        _actors[i].SetAction(in actions[i], motion.Progress, false);
                    else if (seed)
                        skeletons[i].SetAnimationParameterAtChannel(0, motion.Progress);
                    if (i >= _posePlayback.Count)
                    { _posePlayback.Add(new CoopConversationPosePlayback()); _poseActions.Add(motion.ActionName); }
                    else if (seed)
                    { _posePlayback[i] = new CoopConversationPosePlayback(); _poseActions[i] = motion.ActionName; }
                    _posePlayback[i].SetTarget(motion.Progress, motion.Speed, durations[i], cyclic[i]);
                    if (seed) skeletons[i].SetAnimationSpeedAtChannel(0, motion.Speed);
                }
                // Cached admitted values only; no diagnostic native probes.
                if (_missionScene != null) _camera.Frame = Frame(accepted.CameraFrame);
                if (_verboseAttempt && _lastPoseTraceRevision != accepted.ConversationRevision)
                {
                    _lastPoseTraceRevision = accepted.ConversationRevision;
                    TraceStage("pose-applied revision=" + accepted.ConversationRevision + " sequence=" + accepted.Sequence +
                        " actions=" + string.Join(",", accepted.Actors.ConvertAll(a => a.ActionName + "@" +
                            a.Progress.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
                }
            }
            catch (Exception ex) { Fail("pose-" + ex.GetType().Name); }
            return true;
        }

        private void Create(CoopCampaignMapConversationTableauState state)
        {
            // Resolve managed descriptors/resources before allocating the scene.
            var resolved = new List<ResolvedActor>();
            foreach (var actor in state.Actors)
            {
                var item = new ResolvedActor { State = actor };
                if (!BodyProperties.FromString(actor.Visual.BodyProperties, out item.Body) ||
                    !CoopCampaignMapPrototypePartyVisual.TryBuildEquipment(actor.Visual, out item.Equipment))
                    throw new InvalidOperationException("Actor resources unavailable.");
                item.Monster = TaleWorlds.Core.FaceGen.GetBaseMonsterFromRace(actor.Visual.Race);
                item.ActionSet = MBActionSet.GetActionSet(actor.ActionSetName);
                item.Action = ActionIndexCache.Create(actor.InitialActionName);
                if (item.Monster == null || !item.ActionSet.IsValid || item.Action.Index < 0 ||
                    string.IsNullOrEmpty(item.ActionSet.GetAnimationName(in item.Action)))
                    throw new InvalidOperationException("Actor animation unavailable.");
                item.Banner = actor.Visual.HasBanner ? new Banner(actor.BannerCode) : null;
                resolved.Add(item);
            }
            _missionScene = state.MissionScene?.Clone();
            string sceneName = _missionScene?.SceneName ?? CoopCampaignMapConversationTableauCodec.SceneName;
            string owner = ResolveSceneOwner(sceneName);
            if (string.IsNullOrEmpty(owner)) throw new InvalidOperationException("Scene owner unavailable.");
            // Same asset initialization as the installed native thumbnail cache,
            // but with no shared cache, Campaign.Current or physical Agent objects.
            _scene = Scene.CreateNewScene(_missionScene == null, false, (DecalAtlasGroup)0, "CoopConversationTableau");
            var init = new SceneInitializationData(true)
            {
                InitPhysicsWorld = false, InitSkyboxFromStart = _missionScene != null,
                LoadNavMesh = _missionScene == null, EnableFloraPhysics = _missionScene == null
            };
            _scene.DisableStaticShadows(_missionScene == null);
            TraceStage("scene-read-begin");
            _scene.Read(sceneName, owner, ref init, string.Empty);
            TraceStage("scene-read-complete");
            _scene.SetShadow(true);
            _renderer = MBAgentRendererSceneController.CreateNewAgentRendererSceneController(_scene);
            if (_missionScene == null) Utilities.LoadVirtualTextureTileset("WorldMap");
            TraceStage("atmosphere-begin");
            TraceLightingDescriptor(state);
            // Outdoor backgrounds are atmosphere resources; only some interiors
            // have additional tagged entities. An empty tag group is native-valid.
            if (_missionScene == null)
            {
                _scene.SetAtmosphereWithName(state.AtmosphereName);
                foreach (GameEntity entity in _scene.FindEntitiesWithTag(state.AtmosphereName))
                    entity.SetVisibilityExcludeParents(true);
                _scene.FindEntityWithTag("raining_entity")?.SetVisibilityExcludeParents(state.IsRaining);
                _scene.FindEntityWithTag("snowing_entity")?.SetVisibilityExcludeParents(state.IsSnowing);
            }
            else ApplyMissionClimate();
            _scene.EnsurePostfxSystem();
            _scene.SetDofMode(_missionScene == null);
            _scene.SetMotionBlurMode(false);
            _scene.SetBloom(true);
            if (_missionScene == null) _scene.SetDynamicShadowmapCascadesRadiusMultiplier(0.31f);
            _camera = Camera.CreateCamera();
            if (_missionScene == null)
            {
                GameEntity cameraEntity = _scene.FindEntityWithTag("player_infantry_to_infantry");
                if (cameraEntity == null) throw new InvalidOperationException("Camera anchor unavailable.");
                Vec3 cameraTarget = Vec3.Zero;
                cameraEntity.GetCameraParamsFromCameraScript(_camera, ref cameraTarget);
            }
            _camera.Frame = Frame(state.CameraFrame);
            _baseFov = state.BaseCameraHorizontalFov;
            UpdateCameraAspect();
            TraceStage("actors-begin");

            foreach (ResolvedActor item in resolved)
            {
                var descriptor = item.State;
                var visual = descriptor.Visual;
                var data = new AgentVisualsData().Equipment(item.Equipment).BodyProperties(item.Body)
                    .Race(visual.Race).SkeletonType((SkeletonType)visual.SkeletonType)
                    .Scale(descriptor.Scale).Frame(Frame(descriptor.Frame)).UseMorphAnims(true)
                    .ActionSet(item.ActionSet).ActionCode(in item.Action).Scene(_scene).Monster(item.Monster)
                    // Stock MapConversationTableau prepares each face immediately.
                    // Scope this to the bounded owned actors, not scene-wide loads.
                    .PrepareImmediately(true).ClothColor1(descriptor.ClothColor1).ClothColor2(descriptor.ClothColor2)
                    .RightWieldedItemIndex(visual.RightWieldedItemIndex).LeftWieldedItemIndex(visual.LeftWieldedItemIndex)
                    .AddColorRandomness(visual.AddColorRandomness);
                if (item.Banner != null) data.Banner(item.Banner);
                AgentVisuals actor = AgentVisuals.Create(data, "CoopConversationActor", false, false, false);
                _actors.Add(actor);
                actor.SetAgentLodZeroOrMaxExternal(true);
                Skeleton skeleton = actor.GetVisuals().GetSkeleton();
                if (skeleton != null)
                {
                    skeleton.TickAnimationsAndForceUpdate(0.1f, MatrixFrame.Identity, true);
                    if (_missionScene == null)
                        actor.SetLookDirection(_camera.Position - actor.GetVisuals().GetGlobalStableEyePoint(true));
                }
            }

            TraceStage("actors-complete");
            _layer = new PassiveTextureLayer();
            _widget = new TableauTextureWidget(_layer.UIContext) { ParentWidget = _layer.UIContext.Root };
            EnsureTextureSize();
            _screen.AddLayer(_layer);
            // The map tableau asset has terrain disabled; standalone mission
            // scenes may contain terrain. A standalone preload experiment did
            // not establish complete rendering or correct lighting (r8).
            // Let the native texture consumer request drawing resources; a scene-wide resource
            // check coincided with terrain shader preparation in the observed MP
            // run. Keep target/actor readiness and timeout in Tick: drawing alone
            // is not acceptance of a complete image. No synchronous force-load.
            TraceStage("tableau-target-attached");
        }

        private void EnsureTextureSize()
        {
            int width = Math.Max(1, Math.Min(8192, (int)Screen.RealScreenResolutionWidth));
            int height = Math.Max(1, Math.Min(8192, (int)Screen.RealScreenResolutionHeight));
            if (_texture != null && _textureWidth == width && _textureHeight == height) return;
            bool replacing = _texture != null;
            ReleaseTexture();
            if (replacing)
            {
                _lifetime.InvalidateRenderTarget();
                _readinessClock.Restart();
                _lastReadinessSeconds = 0d;
                _readinessStarted = false;
            }
            _textureWidth = width;
            _textureHeight = height;
            object epoch = _targetEpoch = new object();
            _texture = TableauView.AddTableau("CoopConversation_" + (++_textureIndex),
                (sender, args) => OnTextureUpdate(epoch), _scene, width, height);
            _view = _texture.TableauView;
            ConfigureTableau();
            // Configuration already succeeded for this exact owned target.
            // PaintNeeded is not a completion signal: cmt-b18 drew actors while
            // waiting for it. Never require a second callback to admit ticking.
            _targetConfigured = true;
            // Drawing the target also services its native PaintNeeded callback.
            // Do not hide it behind ReadyToRender, which would prevent progress.
            _widget.Image = new TaleWorlds.TwoDimension.Texture(new EngineTexture(_texture));
        }

        private void OnTextureUpdate(object epoch)
        {
            // The engine supplies a weakly cached managed Texture wrapper; its
            // reference identity is not the identity of our registration.
            if (!ReferenceEquals(epoch, _targetEpoch) || _texture == null || _scene == null || _view == null) return;
            try { ConfigureTableau(); }
            // Cleanup is deferred to the owning mission tick, never executed
            // recursively inside a native render-target callback.
            catch (Exception ex) { _callbackFailure = ex.GetType().Name; }
        }

        private void ConfigureTableau()
        {
            _view.SetCamera(_camera);
            _view.SetScene(_scene);
            // MissionScreen explicitly enables view-level shadows. Scene-level
            // shadow flags do not establish this independent rendering contract.
            // Preserve the stock focused-shadow profile for map-only tableaux.
            if (_missionScene != null) _view.SetSceneUsesShadows(true);
            _view.SetSceneUsesSkybox(true);
            _view.SetSceneUsesContour(false);
            _view.SetPointlightResolutionMultiplier(0f);
            _view.SetRenderWithPostfx(true);
            _view.SetPostfxConfigParams(unchecked((int)(uint.MaxValue & 0xFFFFFBFFu)));
            _view.SetDeleteAfterRendering(false);
            _view.SetContinuousRendering(true);
            _view.SetDoNotRenderThisFrame(false);
            _view.SetClearColor(0u);
            _view.SetClearGbuffer(true);
            _view.DoNotClear(false);
            Vec3 shadowCenter = Vec3.Zero;
            _view.SetFocusedShadowmap(_missionScene == null, ref shadowCenter, 1.55f);
            _view.SetEnable(true);
        }

        private void ReleaseTexture()
        {
            if (_widget != null)
            {
                _widget.Image = null;
                _widget.HasSubmittedLoadedTarget = false;
            }
            _targetConfigured = false;
            _targetEpoch = null;
            Texture oldTexture = _texture;
            TableauView oldView = _view;
            // Reject a delayed callback before detaching the native view.
            _texture = null;
            _view = null;
            try
            {
                if (oldView != null)
                {
                    oldView.SetEnable(false);
                    oldView.ClearAll(false, false);
                }
            }
            finally { oldTexture?.Release(); }
        }

        private void UpdateCameraAspect()
        {
            if (_camera == null) return;
            float width = Screen.RealScreenResolutionWidth, height = Screen.RealScreenResolutionHeight;
            float aspect = height > 0f && width > 0f ? width / height : 16f / 9f;
            if (_aspect == aspect) return;
            _aspect = aspect;
            if (_missionScene != null)
                _camera.SetFovVertical(_missionScene.CameraVerticalFov, aspect,
                    _missionScene.CameraNear, _missionScene.CameraFar);
            else
                _camera.SetFovHorizontal(Math.Min(3f, _baseFov * aspect / (16f / 9f)), aspect, 0.2f, 200f);
        }

        private void ApplyMissionClimate()
        {
            // Native land mission initialization selects this photo preset, not
            // InterpolatedAtmosphereName. Keep its sky, fog, exposure and colors.
            // Selection uses initializer time/storm; current scene time is separate.
            _scene.SetPhotoAtmosphereViaTod(_missionScene.AtmosphereTimeOfDay, _missionScene.IsInsideStorm);
            _scene.TimeOfDay = _missionScene.TimeOfDay;
            _scene.SetWinterTimeFactor(_missionScene.Winter);
            _scene.SetDrynessFactor(_missionScene.Dryness);
            _scene.SetRainDensity(_missionScene.Rain);
            _scene.SetSnowDensity(_missionScene.Snow);
            _scene.SetTemperature(_missionScene.Temperature);
            _scene.SetHumidity(_missionScene.Humidity);
            _scene.SetGlobalWindStrengthVector(new Vec2(_missionScene.WindX, _missionScene.WindY));
            Vec3 sunDirection = new Vec3(_missionScene.SunDirectionX, _missionScene.SunDirectionY, _missionScene.SunDirectionZ);
            _scene.SetSunDirection(ref sunDirection);
        }

        private static MatrixFrame Frame(float[] values)
        {
            MatrixFrame frame = MatrixFrame.Identity;
            frame.rotation.s = new Vec3(values[0], values[1], values[2]);
            frame.rotation.f = new Vec3(values[3], values[4], values[5]);
            frame.rotation.u = new Vec3(values[6], values[7], values[8]);
            frame.origin = new Vec3(values[9], values[10], values[11]);
            return frame;
        }

        private static string ResolveSceneOwner(string sceneName)
        {
            string owner = null;
            foreach (string module in Utilities.GetModulesNames())
                foreach (string scene in Utilities.GetSingleModuleScenesOfModule(module) ?? Array.Empty<string>())
                    if (string.Equals(scene, sceneName, StringComparison.OrdinalIgnoreCase))
                        owner = module;
            return owner;
        }

        private void Fail(string reason)
        {
            // Cached results only: never inspect extra native objects to explain
            // an exception or a timeout. Disabled unless verbose was opted in.
            if (_verboseAttempt)
                ModLogger.Info("CoopCampaignMapConversationTableauView: readiness observation. TargetConfigured=" +
                    _targetConfigured + " LoadedTargetSubmitted=" + (_widget?.HasSubmittedLoadedTarget == true) + " ActorMask=" +
                    _lastActorReadyMask + " Actors=" + _actors.Count + " ElapsedMs=" +
                    _readinessClock.ElapsedMilliseconds + ".");
            _lifetime.Fail();
            ReleaseResources();
            // One failure per distinct visual attempt; never per-frame diagnostics.
            ModLogger.Info("CoopCampaignMapConversationTableauView: visual unavailable; text remains active. Reason=" + reason + ".");
        }

        private void TraceStage(string stage)
        {
            if (!_verboseAttempt) return;
            ModLogger.Info("CoopCampaignMapConversationTableauView: load stage=" + stage +
                " ElapsedMs=" + _readinessClock.ElapsedMilliseconds + ".");
        }

        private void TraceLightingDescriptor(CoopCampaignMapConversationTableauState state)
        {
            // Detached values only; no native getter, resource request or per-frame work.
            // Bounded for the process, including repeated dialogue replacement.
            if (!_verboseAttempt || _missionScene == null || _lightingDescriptorSamples >= 12) return;
            _lightingDescriptorSamples++;
            ModLogger.Info(FormattableString.Invariant(
                $"CoopCampaignMapConversationTableauView: lighting descriptor Scene={_missionScene.SceneName} SourceAtmosphere={state.AtmosphereName} Selection=NativePhoto PresetTime={_missionScene.AtmosphereTimeOfDay:R} Storm={_missionScene.IsInsideStorm} SceneTime={_missionScene.TimeOfDay:R} SunDirection=({_missionScene.SunDirectionX:R},{_missionScene.SunDirectionY:R},{_missionScene.SunDirectionZ:R})."));
        }

        private void ObserveMissionSceneLoading()
        {
            // Opt-in native-sensitive reads, on the owning tick only. This is
            // evidence collection, never a readiness gate or resource-load request.
            if (!_sceneProbeEnabled || _missionScene == null || _scene == null ||
                _sceneProbeSample >= 3 || _sceneProbeProcessSamples >= 12) return;
            long elapsed = _readinessClock.ElapsedMilliseconds;
            long due = _sceneProbeSample == 0 ? 0 : (_sceneProbeSample == 1 ? 2000 : 10000);
            if (elapsed < due) return;
            // Consume before native reads: an exception must not cause tick retries.
            _sceneProbeSample++;
            _sceneProbeProcessSamples++;
            try
            {
                bool terrain = _scene.ContainsTerrain;
                bool heightmap = _scene.HasTerrainHeightmap;
                bool loaded = _scene.IsLoadingFinished();
                ModLogger.Info("CoopCampaignMapConversationTableauView: scene-loading sample=" +
                    _sceneProbeSample + " ElapsedMs=" + elapsed + " Terrain=" + terrain +
                    " Heightmap=" + heightmap + " LoadingFinished=" + loaded + ".");
            }
            catch (Exception ex)
            {
                _sceneProbeEnabled = false;
                ModLogger.Info("CoopCampaignMapConversationTableauView: scene-loading probe disabled. Reason=" +
                    ex.GetType().Name + ".");
            }
        }

        private void ReleaseResources()
        {
            // Detach the draw consumer and render target before actors/scene.
            // The widget and 2D wrapper never release the shared target twice.
            int cleanupErrors = 0;
            _sceneProbeEnabled = false;
            _readinessClock.Stop();
            _lastPoseTraceRevision = -1;
            try { ReleaseTexture(); } catch { cleanupErrors++; }
            _widget = null;
            _callbackFailure = null;
            _textureWidth = _textureHeight = 0;
            foreach (AgentVisuals actor in _actors) { try { actor?.Reset(); } catch { cleanupErrors++; } }
            _actors.Clear();
            _posePlayback.Clear();
            _poseActions.Clear();
            try { _camera?.ReleaseCameraEntity(); } catch { cleanupErrors++; }
            _camera = null;
            if (_renderer != null && _scene != null)
            {
                try { MBAgentRendererSceneController.DestructAgentRendererSceneController(_scene, _renderer, false); } catch { cleanupErrors++; }
            }
            _renderer = null;
            if (_layer != null && !_layer.IsFinalized && _screen.HasLayer(_layer))
            {
                try { _screen.RemoveLayer(_layer); } catch { cleanupErrors++; }
            }
            _layer = null;
            try { _scene?.ClearAll(); } catch { cleanupErrors++; }
            _scene = null;
            _missionScene = null;
            _aspect = 0f;
            if (cleanupErrors != 0)
                ModLogger.Info("CoopCampaignMapConversationTableauView: native cleanup incomplete. FailedOperations=" + cleanupErrors + ".");
        }

        public void Dispose() { ReleaseResources(); _lifetime.Reset(); _poseReceiver.Reset(); }
    }
}
