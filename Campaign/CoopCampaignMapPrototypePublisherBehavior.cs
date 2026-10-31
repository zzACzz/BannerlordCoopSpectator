using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure;
using Helpers;
using SandBox.View.Map;
using SandBox.View.Map.Managers;
using SandBox.View.Map.Visuals;
using SandBox.ViewModelCollection.Nameplate;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View;

namespace CoopSpectator.Campaign
{
    public sealed partial class CoopCampaignMapPrototypePublisherBehavior :
        CampaignBehaviorBase
    {
        private const float PublishIntervalSeconds = 0.2f;
        private const float VisibleEntityCaptureIntervalSeconds = 0.5f;
        private const uint DefaultEntityColor = 0xFFB89A5Au;
        private const uint DefaultEntitySecondaryColor = 0xFFF2D078u;
        private static readonly FieldInfo SettlementLevelMaskField = typeof(SettlementVisual)
            .GetField("_currentLevelMask", BindingFlags.Instance | BindingFlags.NonPublic);
        private static MethodInfo _settlementNameplateViewGetter;
        private static FieldInfo _settlementNameplateDataSourceField;
        private static readonly TimeSpan FailureLogInterval =
            TimeSpan.FromSeconds(10d);
        private static CoopCampaignMapPrototypePublisherBehavior
            _applicationTickPublisher;

        private string _sessionId = Guid.NewGuid().ToString("N");
        private int _revision;
        private int _visibleEntitiesRevision;
        private int _catalogRevision;
        private int _dynamicRevision;
        private int _conversationRevision;
        private int _lastHeading;
        private float _timeUntilNextPublish;
        private float _timeUntilNextVisibleEntityCapture;
        private List<CoopCampaignMapPrototypeEntityState> _cachedVisibleEntities =
            new List<CoopCampaignMapPrototypeEntityState>();
        private CoopCampaignMapPrototypeCatalogSnapshot _cachedCatalog;
        private CoopCampaignMapPrototypeDynamicSnapshot _cachedDynamic;
        private CoopCampaignMapConversationState _cachedConversation =
            CoopCampaignMapPrototypeContract.CreateInactiveConversation();
        private CoopCampaignMapConversationPose _capturedConversationPose;
        private CoopCampaignMapPartyAnimationCapture _partyAnimationCapture;
        private CoopCampaignMapSiegeTentCapture _siegeTentCapture;
        private CoopCampaignMapSiegeOverlayCapture _siegeOverlayCapture;
        private CoopCampaignMapSiegeDecalCapture _siegeDecalCapture;
        private CoopCampaignMapSiegeMachineCapture _siegeMachineCapture;
        private CoopCampaignMapSiegeProjectileCapture _siegeProjectileCapture;
        private int _partyPoseCursor;
        private bool _catalogDirty;
        private bool _dynamicDirty;
        private readonly Dictionary<string, int> _entityHeadings =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ReplicaInformation> _replicaInformation =
            new Dictionary<string, ReplicaInformation>(
                StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<
            string,
            CoopCampaignMapPrototypeCatalogEntityState> _knownCatalogById =
                new Dictionary<
                    string,
                    CoopCampaignMapPrototypeCatalogEntityState>(
                        StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<
            string,
            CoopCampaignMapPrototypeDynamicEntityState> _knownDynamicById =
                new Dictionary<
                    string,
                    CoopCampaignMapPrototypeDynamicEntityState>(
                        StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AgentVisuals>
            _knownHumanVisualReferenceById =
                new Dictionary<string, AgentVisuals>(
                    StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AgentVisuals>
            _knownMountVisualReferenceById =
                new Dictionary<string, AgentVisuals>(
                    StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, AgentVisuals>
            _knownCaravanVisualReferenceById =
                new Dictionary<string, AgentVisuals>(
                    StringComparer.OrdinalIgnoreCase);
        private bool _initialPublishLogged;
        private readonly int _tooltipOwnerThread = System.Threading.Thread.CurrentThread.ManagedThreadId;
        private CoopCampaignMapTooltipCapture _tooltipCapture;
        private TaleWorlds.CampaignSystem.Campaign _lastPublishedCampaign;
        private readonly CoopCampaignAftermathMapAnchor _aftermathAnchor = new CoopCampaignAftermathMapAnchor();
        private CoopCampaignMapPrototypeCatalogSnapshot _aftermathPublicationCatalog;
        private CoopCampaignMapPrototypeDynamicSnapshot _aftermathPublicationDynamic;
        private string _lastPublishedSession;
        private CoopCampaignMapMenuPresentation _lastPublishedMenu;
        private CoopCampaignMapBarPresentation _lastPublishedMapBar;
        private long _lastPublishedTimestamp;
        private DateTime _nextFailureLogUtc = DateTime.MinValue;

        public override void RegisterEvents()
        {
            _applicationTickPublisher?._siegeProjectileCapture?.ReleaseFlights();
            _applicationTickPublisher = this;
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnCampaignSessionLaunched);
        }

        private void OnCampaignSessionLaunched(CampaignGameStarter starter)
        {
            _aftermathAnchor.Reset();
            try { CoopCampaignContentRuntime.RememberLocalHost(); }
            catch (Exception ex) { ModLogger.Info("Campaign content profile unavailable: " + ex.Message); }
            CoopCampaignCraftedVisualCapture.Reset();
            CoopCampaignMissionHost.ObserveCampaignLoaded();
            CoopCampaignMapTooltipHost.ObserveCampaignLoaded();
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        public static void PumpApplicationTick(float dt)
        {
            if (!ExperimentalFeatures.EnableCampaignMapPrototype)
            {
                _applicationTickPublisher?.RevokeMissionConversation();
                return;
            }

            CoopCampaignMapPrototypePublisherBehavior publisher =
                _applicationTickPublisher;
            if (publisher == null ||
                TaleWorlds.CampaignSystem.Campaign.Current == null)
            {
                publisher?.RevokeMissionConversation();
                return;
            }

            Mission activeMission = Mission.Current ??
                (GameStateManager.Current?.ActiveState as MissionState)?.CurrentMission;
            if (activeMission != null)
            {
                if (!ReferenceEquals(activeMission, publisher._standaloneConversationMission))
                {
                    publisher.RevokeMissionConversation();
                    return;
                }
                publisher.PublishTick(dt);
                return;
            }
            if (!IsMapCameraAvailable() && !CoopCampaignAftermathCapture.IsAftermathState(GameStateManager.Current?.ActiveState))
                return;

            publisher.PublishTick(dt);
        }

        public static void ResetApplicationTickPublisher()
        {
            _applicationTickPublisher?._aftermathAnchor.Reset();
            _applicationTickPublisher?._siegeProjectileCapture?.ReleaseFlights();
            CoopCampaignMapMenuCapture.ResetPlunderIdentity();
            CoopCampaignMapBarCapture.ResetHintIdentity();
            CoopCampaignAftermathCapture.Reset();
            _applicationTickPublisher?.RevokeMissionConversation();
            _applicationTickPublisher = null;
        }

        // Host-only entry for the admitted local relay. NOT a client
        // endpoint: peer/lease ownership and aggregate rate limits belong to that
        // relay. Never resolve arbitrary hidden IDs.
        internal static bool TryCapturePublishedTooltip(CoopCampaignMapTooltipRequest request, long sequence,
            out CoopCampaignMapTooltipPayload payload, out string reason)
        {
            payload = null;
            reason = "tooltip-published-target-unavailable";
            var publisher = _applicationTickPublisher;
            if (publisher == null || request == null || !ExperimentalFeatures.EnableCampaignMapPrototype ||
                publisher._tooltipOwnerThread != System.Threading.Thread.CurrentThread.ManagedThreadId ||
                !ReferenceEquals(publisher._lastPublishedCampaign, TaleWorlds.CampaignSystem.Campaign.Current) ||
                publisher._lastPublishedCampaign == null || publisher._lastPublishedSession != publisher._sessionId) return false;
            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            if (now < publisher._lastPublishedTimestamp ||
                (now - publisher._lastPublishedTimestamp) / (double)System.Diagnostics.Stopwatch.Frequency >= 2d) return false;
            try
            {
                if (CoopCampaignMapTooltipTargetAdmission.IsAftermathAdmitted(publisher._lastPublishedSession, request, publisher._lastPublishedMenu) &&
                    CoopCampaignMapTooltipPayloadCodec.IsValidCorrelation(request.SessionId, request.EntityId, request.Generation, sequence) &&
                    !request.IsExtended && CoopCampaignAftermathTroopTarget.TryParse(request.EntityId, out var troop))
                {
                    if (publisher._tooltipCapture == null) publisher._tooltipCapture = new CoopCampaignMapTooltipCapture();
                    if (!publisher._tooltipCapture.TryPreparePrisonerPreview(troop.WindowId, troop.RowId, out var preview)) return false;
                    var prepared = new CoopCampaignMapTooltipPayload { SessionId = request.SessionId, EntityId = request.EntityId,
                        RequestGeneration = request.Generation, Sequence = sequence, Kind = CoopCampaignMapTooltipKind.AftermathTroop,
                        AftermathCard = new CoopCampaignAftermathPresentation { Kind = CoopCampaignAftermathKind.Prisoners,
                            WindowId = troop.WindowId, Root = preview } };
                    if (!CoopCampaignMapTooltipPayloadCodec.IsValid(prepared)) return false;
                    payload = prepared; reason = null; return true;
                }
                if (CoopCampaignMapTooltipTargetAdmission.IsAftermathAdmitted(publisher._lastPublishedSession, request, publisher._lastPublishedMenu) &&
                    CoopCampaignMapTooltipPayloadCodec.IsValidCorrelation(request.SessionId, request.EntityId, request.Generation, sequence) &&
                    CoopCampaignAftermathInspectionTarget.TryParse(request.EntityId, out var inspection))
                {
                    if (publisher._tooltipCapture == null) publisher._tooltipCapture = new CoopCampaignMapTooltipCapture();
                    if (!publisher._tooltipCapture.TryPrepareLootCard(inspection.WindowId, inspection.RowId, inspection.HeroId,
                        inspection.EquipmentMode, inspection.Usage, inspection.ComparisonOffset, out var card, inspection.EquipmentSlot)) return false;
                    var prepared = new CoopCampaignMapTooltipPayload { SessionId = request.SessionId, EntityId = request.EntityId,
                        RequestGeneration = request.Generation, Sequence = sequence, IsExtended = request.IsExtended,
                        Kind = CoopCampaignMapTooltipKind.AftermathItem,
                        AftermathCard = new CoopCampaignAftermathPresentation { Kind = CoopCampaignAftermathKind.Loot,
                            WindowId = inspection.WindowId, Root = card } };
                    if (!CoopCampaignMapTooltipPayloadCodec.IsValid(prepared)) return false;
                    payload = prepared; reason = null; return true;
                }
                if (CoopCampaignMapTooltipTargetAdmission.IsAftermathAdmitted(publisher._lastPublishedSession, request, publisher._lastPublishedMenu) &&
                    CoopCampaignMapTooltipPayloadCodec.IsValidCorrelation(request.SessionId, request.EntityId, request.Generation, sequence) &&
                    !request.IsExtended && CoopCampaignAftermathLoadoutTarget.TryParse(request.EntityId, out var loadout))
                {
                    if (publisher._tooltipCapture == null) publisher._tooltipCapture = new CoopCampaignMapTooltipCapture();
                    if (!publisher._tooltipCapture.TryPrepareLootHeroVisual(loadout.WindowId, loadout.HeroId, loadout.EquipmentMode, out var visual)) return false;
                    var prepared = new CoopCampaignMapTooltipPayload { SessionId = request.SessionId, EntityId = request.EntityId,
                        RequestGeneration = request.Generation, Sequence = sequence, Kind = CoopCampaignMapTooltipKind.AftermathLoadout,
                        AftermathCard = new CoopCampaignAftermathPresentation { Kind = CoopCampaignAftermathKind.Loot,
                            WindowId = loadout.WindowId, Root = visual } };
                    if (!CoopCampaignMapTooltipPayloadCodec.IsValid(prepared)) return false;
                    payload = prepared; reason = null; return true;
                }
                if (CoopCampaignMapTooltipTargetAdmission.IsSettlementHintAdmitted(publisher._lastPublishedSession, request, publisher._lastPublishedMenu))
                {
                    if (!CoopCampaignMapMenuCapture.TryResolveCurrentSettlementHint(request.SessionId, request.EntityId, out var hint)) return false;
                    if (publisher._tooltipCapture == null) publisher._tooltipCapture = new CoopCampaignMapTooltipCapture();
                    return publisher._tooltipCapture.TryCaptureSettlementHint(hint, request.SessionId, request.EntityId,
                        request.Generation, sequence, request.IsExtended, out payload, out reason);
                }
                if (CoopCampaignMapTooltipTargetAdmission.IsBarHintAdmitted(publisher._lastPublishedSession, request, publisher._lastPublishedMapBar))
                {
                    if (publisher._tooltipCapture == null) publisher._tooltipCapture = new CoopCampaignMapTooltipCapture();
                    if (CoopCampaignMapBarCapture.TryResolveCurrentControlHint(request.SessionId, request.EntityId, out var control))
                        return publisher._tooltipCapture.TryCaptureControl(control, request.SessionId, request.EntityId,
                            request.Generation, sequence, request.IsExtended, out payload, out reason);
                    if (!CoopCampaignMapBarCapture.TryResolveCurrentInfoHint(request.SessionId, request.EntityId, out var info)) return false;
                    return info.ItemId == "gold"
                        ? publisher._tooltipCapture.TryCaptureBarGold(info, request.SessionId, request.EntityId,
                            request.Generation, sequence, request.IsExtended, out payload, out reason)
                        : publisher._tooltipCapture.TryCaptureBarInfo(info, request.SessionId, request.EntityId,
                            request.Generation, sequence, request.IsExtended, out payload, out reason);
                }
                if (CoopCampaignMapTooltipTargetAdmission.IsPlunderAdmitted(publisher._lastPublishedSession, request, publisher._lastPublishedMenu) &&
                    CoopCampaignMapTooltipCapture.IsMapContext() &&
                    CoopCampaignMapTooltipPayloadCodec.IsValidCorrelation(request.SessionId, request.EntityId, request.Generation, sequence) &&
                    !string.IsNullOrEmpty(request.EntityId) && CoopCampaignMapMenuPlunder.IsTooltipId(request.EntityId) &&
                    publisher._lastPublishedMenu != null)
                {
                    if (!CoopCampaignMapMenuCapture.TryResolveCurrentPlunder(request.SessionId, request.EntityId, out var item)) return false;
                    if (publisher._tooltipCapture == null) publisher._tooltipCapture = new CoopCampaignMapTooltipCapture();
                    return publisher._tooltipCapture.TryCaptureItem(item, request.SessionId, request.EntityId,
                        request.Generation, sequence, request.IsExtended, out payload, out reason);
                }
                if (CoopCampaignMapTooltipTargetAdmission.IsEncounterAdmitted(publisher._lastPublishedSession, request, publisher._lastPublishedMenu))
                {
                    if (!CoopCampaignMapMenuCapture.TryResolveCurrentEncounter(request.SessionId, request.EntityId, out var participant)) return false;
                    if (publisher._tooltipCapture == null) publisher._tooltipCapture = new CoopCampaignMapTooltipCapture();
                    return publisher._tooltipCapture.TryCaptureEncounter(participant, request.SessionId, request.EntityId,
                        request.Generation, sequence, request.IsExtended, out payload, out reason);
                }
                if (!CoopCampaignMapTooltipCapture.IsMapContext() ||
                    !CoopCampaignMapTooltipPayloadCodec.IsValidCorrelation(request.SessionId, request.EntityId, request.Generation, sequence) ||
                    !publisher._knownCatalogById.TryGetValue(request.EntityId, out var catalog) ||
                    !publisher._knownDynamicById.TryGetValue(request.EntityId, out var dynamicState) ||
                    !CoopCampaignMapTooltipTargetAdmission.IsAdmitted(publisher._lastPublishedSession, request, catalog, dynamicState)) return false;
                PartyBase target = null;
                int matches = 0;
                if (catalog.Kind == CoopCampaignMapPrototypeEntityKind.Settlement)
                {
                    foreach (Settlement settlement in Settlement.All)
                    {
                        if (settlement == null || "settlement:" + settlement.StringId != request.EntityId) continue;
                        if (++matches > 1) return false; // Ambiguous native identity is never guessed.
                        target = settlement.Party;
                    }
                }
                else
                {
                    foreach (MobileParty party in MobileParty.All)
                    {
                        if (party == null || "party:" + party.StringId != request.EntityId) continue;
                        if (++matches > 1) return false;
                        target = party.Party;
                    }
                }
                if (target == null) return false;
                if (publisher._tooltipCapture == null) publisher._tooltipCapture = new CoopCampaignMapTooltipCapture();
                return publisher._tooltipCapture.TryCapture(target, request.SessionId, request.EntityId,
                    request.Generation, sequence, request.IsExtended, out payload, out reason);
            }
            catch (Exception) { reason = "tooltip-target-resolution-failed"; return false; }
        }

        private static bool IsMapCameraAvailable()
        {
            try
            {
                MapScreen mapScreen = MapScreen.Instance;
                return mapScreen?.MapCameraView?.Camera != null;
            }
            catch
            {
                return false;
            }
        }

        private void PublishTick(float dt)
        {
            _timeUntilNextVisibleEntityCapture -= Math.Max(0f, dt);
            _timeUntilNextPublish -= Math.Max(0f, dt);
            if (_timeUntilNextPublish > 0f)
                return;
            _timeUntilNextPublish = PublishIntervalSeconds;

            // A failed or partial capture/write must not admit tooltip reads from
            // newer in-memory stores using an older successful publication stamp.
            _lastPublishedCampaign = null;
            _lastPublishedMenu = null;
            _lastPublishedMapBar = null;
            bool mapPublication = Mission.Current == null &&
                !(GameStateManager.Current?.ActiveState is MissionState);
            if (mapPublication) RevokeMissionConversation();
            if (!TryCaptureSnapshot(out CoopCampaignMapPrototypeHostSnapshot snapshot, out string reason))
            {
                if (mapPublication || CoopDebugConfig.VerboseDiagnostics)
                    LogFailureRateLimited("capture:" + (reason ?? "unknown"));
                return;
            }

            bool aftermathPublication = _aftermathPublicationCatalog != null;
            var publicationCatalog = _aftermathPublicationCatalog ?? _cachedCatalog;
            var publicationDynamic = _aftermathPublicationDynamic ?? _cachedDynamic;
            // The retained bundle can be older than live caches. Keep both dirty
            // until a normal publication restores them, including after a failed
            // write of the retained frame. Never roll live revision counters back.
            if (aftermathPublication) _catalogDirty = _dynamicDirty = true;
            if (_catalogDirty && publicationCatalog != null)
            {
                if (!CoopCampaignMapPrototypeBridgeFile.TryWriteCatalog(
                        publicationCatalog,
                        out reason))
                {
                    LogFailureRateLimited("catalog-write:" + (reason ?? "unknown"));
                    return;
                }
                if (!aftermathPublication) _catalogDirty = false;
            }

            if (_dynamicDirty && publicationDynamic != null)
            {
                if (!CoopCampaignMapPrototypeBridgeFile.TryWriteDynamic(
                        publicationDynamic,
                        out reason))
                {
                    LogFailureRateLimited("dynamic-write:" + (reason ?? "unknown"));
                    return;
                }
                if (!aftermathPublication) _dynamicDirty = false;
            }

            if (!CoopCampaignMapPrototypeBridgeFile.TryWrite(snapshot, out reason))
            {
                LogFailureRateLimited("write:" + (reason ?? "unknown"));
                return;
            }

            if (mapPublication)
            {
                if (GameStateManager.Current?.ActiveState is TaleWorlds.CampaignSystem.GameState.MapState &&
                    snapshot.Conversation?.IsActive != true)
                {
                    _aftermathAnchor.Remember(TaleWorlds.CampaignSystem.Campaign.Current,
                        snapshot, _cachedCatalog, _cachedDynamic);
                }
                _lastPublishedCampaign = TaleWorlds.CampaignSystem.Campaign.Current;
                _lastPublishedSession = snapshot.SessionId;
                _lastPublishedMenu = snapshot.Menu?.Clone();
                _lastPublishedMapBar = snapshot.MapBar?.Clone();
                _lastPublishedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
                _conversationPublication.RememberPublishedMap(_lastPublishedCampaign, snapshot,
                    PublicationTime, DateTime.UtcNow);
                _clearConversationOnCaptureFailure = false;
            }
            if (!_initialPublishLogged)
            {
                _initialPublishLogged = true;
                ModLogger.Info(
                    "CoopCampaignMapPrototypePublisher: authoritative host map state active. Session=" +
                    _sessionId + " Revision=" + snapshot.Revision +
                    " Camera=" + (snapshot.Camera != null) + ".");
            }
        }

        private bool TryCaptureSnapshot(
            out CoopCampaignMapPrototypeHostSnapshot snapshot,
            out string reason)
        {
            snapshot = null;
            reason = null;
            _aftermathPublicationCatalog = null;
            _aftermathPublicationDynamic = null;
            TaleWorlds.CampaignSystem.Campaign campaign =
                TaleWorlds.CampaignSystem.Campaign.Current;
            if (campaign == null)
            {
                reason = "campaign-missing";
                return false;
            }

            MobileParty mainParty = campaign.MainParty;
            if (mainParty == null)
            {
                reason = "main-party-missing";
                return false;
            }
            var activeState = GameStateManager.Current?.ActiveState;
            if (activeState is TaleWorlds.CampaignSystem.GameState.PartyState ||
                activeState is TaleWorlds.CampaignSystem.GameState.InventoryState)
            {
                reason = "aftermath-anchor-unavailable";
                if (_revision == int.MaxValue ||
                    !_aftermathAnchor.TryAcquire(campaign, _sessionId, DateTime.UtcNow,
                        out var anchoredFrame, out var anchoredCatalog, out var anchoredDynamic)) return false;
                if (!CoopCampaignAftermathCapture.TryCapture(out var aftermathMenu, out reason)) return false;
                snapshot = anchoredFrame;
                _aftermathPublicationCatalog = anchoredCatalog;
                _aftermathPublicationDynamic = anchoredDynamic;
                snapshot.Revision = ++_revision;
                snapshot.SampleTimeMilliseconds = Environment.TickCount & int.MaxValue;
                snapshot.IsMoving = false;
                snapshot.Menu = aftermathMenu;
                snapshot.MapBar = null;
                snapshot.MovementTarget = string.Empty;
                snapshot.ConversationPose = null;
                snapshot.PartyPoses = Array.Empty<CoopCampaignMapPartyPoseEnvelope>();
                snapshot.BattleMarkers = Array.Empty<CoopCampaignMapBattleMarker>();
                reason = null;
                return true;
            }
            CoopCampaignAftermathCapture.Reset();
            Mission activeMission = Mission.Current ??
                (GameStateManager.Current?.ActiveState as MissionState)?.CurrentMission;
            if (activeMission != null)
            {
                return TryCaptureMissionConversation(activeMission, out snapshot, out reason);
            }

            try
            {
                campaign.MapSceneWrapper.GetMapBorders(
                    out Vec2 minimum,
                    out Vec2 maximum,
                    out _);
                Vec2 position = mainParty.VisualPosition2DWithoutError;
                if (!CoopCampaignMapPrototypeContract.TryNormalizeMapPosition(
                        position.x,
                        position.y,
                        minimum.x,
                        minimum.y,
                        maximum.x,
                        maximum.y,
                        out int normalizedX,
                        out int normalizedY))
                {
                    reason = "invalid-map-borders-or-position";
                    return false;
                }

                Vec2 bearing = mainParty.Bearing;
                _lastHeading = CoopCampaignMapPrototypeContract.QuantizeDirection(
                    bearing.x,
                    bearing.y,
                    _lastHeading);
                float seasonTimeFactor = 0f;
                float nextSeasonTimeFactor = 0f;
                if (campaign.Models?.MapWeatherModel == null)
                {
                    reason = "map-weather-model-missing";
                    return false;
                }
                CampaignTime campaignTime = CampaignTime.Now;
                campaign.Models.MapWeatherModel.GetSeasonTimeFactorOfCampaignTime(
                    campaignTime,
                    out seasonTimeFactor,
                    out nextSeasonTimeFactor,
                    false);
                if (!CoopCampaignMapPrototypeContract.TryQuantizeMapVisualState(
                        campaignTime.CurrentHourInDay,
                        seasonTimeFactor,
                        out int normalizedTimeOfDay,
                        out int quantizedSeasonTimeFactor))
                {
                    reason = "invalid-map-visual-state";
                    return false;
                }
                if (_revision == int.MaxValue)
                {
                    _sessionId = Guid.NewGuid().ToString("N");
                    _revision = 0;
                }

                _revision++;
                if (_timeUntilNextVisibleEntityCapture <= 0f ||
                    _cachedVisibleEntities.Count == 0)
                {
                    _timeUntilNextVisibleEntityCapture =
                        VisibleEntityCaptureIntervalSeconds;
                    _cachedVisibleEntities = CaptureVisibleEntities(
                        campaign,
                        mainParty,
                        minimum,
                        maximum);
                    if (_visibleEntitiesRevision == int.MaxValue)
                        _visibleEntitiesRevision = 0;
                    _visibleEntitiesRevision++;
                    UpdateReplicaSnapshots(_cachedVisibleEntities);
                }
                CoopCampaignMapPrototypeCameraState cameraState =
                    TryCaptureMapCamera();
                UpdateConversationSnapshot();
                // Failed captures publish a close; never retain an obsolete menu.
                CoopCampaignMapMenuCapture.TryCapture(_sessionId, out var menuPresentation, out _);
                if (menuPresentation?.IsInSiegeMode == true && !_cachedConversation.IsActive)
                {
                    if (_siegeOverlayCapture == null) _siegeOverlayCapture = new CoopCampaignMapSiegeOverlayCapture();
                    // Failure publishes no overlay, never the preceding siege's values.
                    _siegeOverlayCapture.TryCapture(out var siegeOverlay, out _);
                    if (siegeOverlay != null)
                    {
                        if (_siegeDecalCapture == null) _siegeDecalCapture = new CoopCampaignMapSiegeDecalCapture();
                        _siegeDecalCapture.TryCapture(out var decals);
                        siegeOverlay.Decals = decals;
                        if (_siegeMachineCapture == null) _siegeMachineCapture = new CoopCampaignMapSiegeMachineCapture();
                        _siegeMachineCapture.TryCapture(out var machines);
                        siegeOverlay.Machines = machines;
                        if (_siegeProjectileCapture == null) _siegeProjectileCapture = new CoopCampaignMapSiegeProjectileCapture();
                        _siegeProjectileCapture.TryCapture(out var projectiles);
                        siegeOverlay.Projectiles = projectiles;
                    }
                    menuPresentation.SiegeOverlay = siegeOverlay;
                }
                CoopCampaignMapBarCapture.TryCapture(_sessionId, out var mapBarPresentation, out _);
                CoopCampaignMapBattleMarkerCapture.TryCapture(out var battleMarkers, out _);
                snapshot = new CoopCampaignMapPrototypeHostSnapshot
                {
                    ContentProfileId = CoopCampaignContentRuntime.GetLocalId(),
                    SchemaVersion =
                        CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
                    SessionId = _sessionId,
                    Revision = _revision,
                    NormalizedX = normalizedX,
                    NormalizedY = normalizedY,
                    Heading = _lastHeading,
                    NormalizedTimeOfDay = normalizedTimeOfDay,
                    SeasonTimeFactor = quantizedSeasonTimeFactor,
                    SampleTimeMilliseconds = Environment.TickCount & int.MaxValue,
                    IsMoving = mainParty.IsMoving,
                    IsActive = true,
                    VisibleEntitiesRevision = _visibleEntitiesRevision,
                    CatalogRevision = _catalogRevision,
                    DynamicRevision = _dynamicRevision,
                    VisibleEntities = new List<CoopCampaignMapPrototypeEntityState>(),
                    Camera = cameraState,
                    MovementTarget = _cachedConversation.IsActive ? string.Empty : CaptureMovementTarget(mainParty),
                    Conversation = _cachedConversation.Clone(),
                    Menu = _cachedConversation.IsActive ? null : menuPresentation,
                    MapBar = _cachedConversation.IsActive ? null : mapBarPresentation,
                    BattleMarkers = _cachedConversation.IsActive ? Array.Empty<CoopCampaignMapBattleMarker>() : battleMarkers,
                    ConversationPose = _capturedConversationPose?.Clone(),
                    PartyPoses = CapturePartyPoses(campaign, mainParty),
                    UpdatedUtc = DateTime.UtcNow
                };
                return true;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name + ":" + ex.Message;
                return false;
            }
        }

        private CoopCampaignMapPartyPoseEnvelope[] CapturePartyPoses(
            TaleWorlds.CampaignSystem.Campaign campaign, MobileParty mainParty)
        {
            // Optional motion must not suppress otherwise valid map publication.
            // A failed native read publishes no poses, never the preceding tuple.
            try { return CaptureCurrentPartyPoses(campaign, mainParty); }
            catch (Exception) { return Array.Empty<CoopCampaignMapPartyPoseEnvelope>(); }
        }

        private CoopCampaignMapPartyPoseEnvelope[] CaptureCurrentPartyPoses(
            TaleWorlds.CampaignSystem.Campaign campaign, MobileParty mainParty)
        {
            if (_cachedConversation.IsActive || Mission.Current != null ||
                !ReferenceEquals(campaign, TaleWorlds.CampaignSystem.Campaign.Current))
                return Array.Empty<CoopCampaignMapPartyPoseEnvelope>();
            var manager = MobilePartyVisualManager.Current;
            if (manager == null) return Array.Empty<CoopCampaignMapPartyPoseEnvelope>();
            if (!CoopCampaignMapPartyPose.TryResolveTimeScale((int)campaign.TimeControlMode,
                campaign.IsMainPartyWaiting, campaign.SpeedUpMultiplier, out var timeScale))
                return Array.Empty<CoopCampaignMapPartyPoseEnvelope>();
            if (_partyAnimationCapture == null) _partyAnimationCapture = new CoopCampaignMapPartyAnimationCapture();
            var parties = new Dictionary<string, MobileParty>(StringComparer.OrdinalIgnoreCase);
            foreach (MobileParty party in MobileParty.All)
            {
                if (party == null) continue;
                string id = "party:" + party.StringId;
                // An ambiguous native ID is never resolved to an arbitrary party.
                if (parties.ContainsKey(id)) parties[id] = null;
                else parties.Add(id, party);
            }
            var candidates = _knownDynamicById.Values.Where(value => value.IsVisible &&
                _knownCatalogById.TryGetValue(value.EntityId, out var catalog) &&
                catalog.Kind != CoopCampaignMapPrototypeEntityKind.Settlement &&
                CoopCampaignMapPartyBodyPolicy.ShouldRenderBody(catalog.Kind, value.PartyNameplateFlags))
                .OrderBy(value => value.EntityId, StringComparer.OrdinalIgnoreCase).ToArray();
            var poses = new List<CoopCampaignMapPartyPoseEnvelope>();
            int count = Math.Min(candidates.Length, CoopCampaignMapPartyPoseBatchCodec.MaximumCount);
            for (int i = 0; i < count; i++)
            {
                var candidate = candidates[(_partyPoseCursor + i) % candidates.Length];
                if (!parties.TryGetValue(candidate.EntityId, out var party) || party == null ||
                    !CoopCampaignMapPrototypeVisibilityPolicy.ShouldReplicateMobileParty(
                        party == mainParty, party.IsActive, party.IsGarrison, party.IsMilitia,
                        party.CurrentSettlement != null, party.IsSpotted())) continue;
                var visual = TryGetMobilePartyVisual(manager, party.Party);
                // Catalog resources must still be the exact native instances sampled.
                if (party.Party.IsVisualDirty || visual == null ||
                    !_knownHumanVisualReferenceById.TryGetValue(candidate.EntityId, out var h) ||
                    !_knownMountVisualReferenceById.TryGetValue(candidate.EntityId, out var m) ||
                    !_knownCaravanVisualReferenceById.TryGetValue(candidate.EntityId, out var c) ||
                    !ReferenceEquals(h, visual.HumanAgentVisuals) ||
                    !ReferenceEquals(m, visual.MountAgentVisuals) ||
                    !ReferenceEquals(c, visual.CaravanMountAgentVisuals) ||
                    !_partyAnimationCapture.TryCapture(party.Party, visual, true,
                        out var human, out var mount, out var caravan)) continue;
                var pose = new CoopCampaignMapPartyPose { AppearanceRevision = _catalogRevision,
                    TimeScale = timeScale, Human = human, Mount = mount, Caravan = caravan };
                var catalog = _knownCatalogById[candidate.EntityId];
                if (!pose.MatchesAppearance(_catalogRevision, catalog.HumanVisual != null,
                    catalog.MountVisual != null, catalog.CaravanMountVisual != null)) continue;
                poses.Add(new CoopCampaignMapPartyPoseEnvelope { SessionId = _sessionId,
                    EntityId = candidate.EntityId, Sequence = _revision, Pose = pose });
            }
            _partyPoseCursor = candidates.Length == 0 ? 0 : (_partyPoseCursor + count) % candidates.Length;
            return poses.ToArray();
        }

        private List<CoopCampaignMapPrototypeEntityState> CaptureVisibleEntities(
            TaleWorlds.CampaignSystem.Campaign campaign,
            MobileParty mainParty,
            Vec2 minimum,
            Vec2 maximum)
        {
            var candidates = new List<VisibleEntityCandidate>();
            _replicaInformation.Clear();
            MobilePartyVisualManager partyVisualManager = null;
            SettlementVisualManager settlementVisualManager = null;
            try
            {
                partyVisualManager = MobilePartyVisualManager.Current;
                settlementVisualManager = SettlementVisualManager.Current;
            }
            catch
            {
            }

            MobilePartyVisual mainPartyVisual = TryGetMobilePartyVisual(
                partyVisualManager,
                mainParty?.Party);
            AddMobilePartyCandidate(
                candidates,
                mainParty,
                mainParty,
                minimum,
                maximum,
                CoopCampaignMapPrototypeEntityKind.MainParty,
                isVisible: true,
                mainPartyVisual);

            foreach (MobileParty party in MobileParty.All)
            {
                if (party == null || party == mainParty)
                    continue;

                if (!CoopCampaignMapPrototypeVisibilityPolicy
                        .ShouldReplicateMobileParty(
                            isMainParty: false,
                            isActive: party.IsActive,
                            isGarrison: party.IsGarrison,
                            isMilitia: party.IsMilitia,
                            hasCurrentSettlement:
                                party.CurrentSettlement != null,
                            isSpotted: party.IsSpotted()))
                {
                    continue;
                }

                MobilePartyVisual mobilePartyVisual = TryGetMobilePartyVisual(
                    partyVisualManager,
                    party.Party);
                AddMobilePartyCandidate(
                    candidates,
                    party,
                    mainParty,
                    minimum,
                    maximum,
                    CoopCampaignMapPrototypeEntityKind.MobileParty,
                    isVisible: true,
                    mobilePartyVisual);
            }

            SettlementNameplatesVM settlementNameplates = TryGetSettlementNameplates();
            foreach (Settlement settlement in Settlement.All)
            {
                if (settlement == null)
                    continue;

                ISpottable spottable =
                    settlement.SettlementComponent as ISpottable;
                if (!CoopCampaignMapPrototypeVisibilityPolicy
                        .ShouldReplicateSettlement(
                            settlement.IsVisible,
                            spottable != null,
                            spottable?.IsSpotted == true))
                {
                    continue;
                }

                Vec2 settlementPosition = settlement.GetPosition2D;
                ResolveAppearance(
                    settlement.Party,
                    out uint primaryColor,
                    out uint secondaryColor,
                    out string bannerCode);
                if (!TryCreateEntityState(
                        "settlement:" + settlement.StringId,
                        settlement.Name?.ToString(),
                        CoopCampaignMapPrototypeEntityKind.Settlement,
                        ResolveSettlementNameplateSize(settlement),
                        settlementPosition,
                        heading: 0,
                        partySize: settlement.Party?.NumberOfAllMembers ?? 0,
                        primaryColor,
                        secondaryColor,
                        bannerCode,
                        visualCharacterId: string.Empty,
                        cultureId: string.Empty,
                        partyVisualKind:
                            CoopCampaignMapPrototypePartyVisualKind.None,
                        humanVisual: null,
                        mountVisual: null,
                        caravanMountVisual: null,
                        minimum,
                        maximum,
                        out CoopCampaignMapPrototypeEntityState entity))
                {
                    continue;
                }

                candidates.Add(
                    new VisibleEntityCandidate(
                        entity,
                        GetDistanceSquared(
                            mainParty.VisualPosition2DWithoutError,
                            settlementPosition)));
                entity.SettlementLevelFlags = CaptureSettlementLevels(settlementVisualManager, settlement);
                entity.HoverDecal = CaptureHoverDecal(settlement.Party,
                    settlementVisualManager?.GetSettlementVisual(settlement));
                CaptureSettlementPresentation(settlement, entity);
                entity.SettlementRaidState = CoopCampaignMapRaidPresentationPolicy.Capture(true,
                    settlement.IsVillage, settlement.Party?.MapEvent?.IsRaid == true, settlement.IsRaided);
                entity.SettlementRelation = CaptureSettlementRelation(settlement);
                int eventType = settlementNameplates?.GetNameplateOfSettlement(settlement)?.MapEventVisualType ?? 0;
                entity.SettlementMapEventType = eventType >= 0 && eventType <= 5 ? eventType : 0;
                ReplicaInformation settlementInformation = CaptureSettlementInformation(settlement, settlementNameplates);
                _replicaInformation[entity.EntityId] = settlementInformation;
                entity.IsSettlementInRange = settlementInformation.IsSettlementInRange;
                entity.SettlementPartyBadges = CaptureSettlementPartyBadges(
                    settlement, settlementInformation.IsSettlementInRange, settlementNameplates);
            }

            return candidates
                .OrderBy(candidate => candidate.Entity.Kind ==
                                      CoopCampaignMapPrototypeEntityKind.MainParty
                    ? 0
                    : candidate.Entity.Kind ==
                      CoopCampaignMapPrototypeEntityKind.MobileParty
                        ? 1
                        : 2)
                .ThenBy(
                    candidate => candidate.Entity.EntityId,
                    StringComparer.OrdinalIgnoreCase)
                .Take(CoopCampaignMapPrototypeContract.MaxVisibleEntities)
                .Select(candidate => candidate.Entity.Clone())
                .ToList();
        }

        private static SettlementNameplatesVM TryGetSettlementNameplates()
        {
            // Read only the existing loaded host view. Cache metadata, never a view/VM
            // across screen lifetimes; do not load an absent UI module or create a view.
            try
            {
                MapScreen screen = MapScreen.Instance;
                if (screen == null) return null;
                if (_settlementNameplateViewGetter == null)
                {
                    Type ownerType = null;
                    foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                        if (assembly.GetName().Name == "SandBox.GauntletUI")
                            ownerType = assembly.GetType("SandBox.GauntletUI.Map.GauntletMapSettlementNameplateView", false);
                    if (ownerType == null || !typeof(MapView).IsAssignableFrom(ownerType)) return null;
                    FieldInfo field = ownerType.GetField("_dataSource", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (field?.FieldType != typeof(SettlementNameplatesVM)) return null;
                    MethodInfo getter = typeof(MapScreen).GetMethod("GetMapView", BindingFlags.Instance | BindingFlags.Public);
                    if (getter == null || !getter.IsGenericMethodDefinition || getter.GetParameters().Length != 0) return null;
                    _settlementNameplateDataSourceField = field;
                    _settlementNameplateViewGetter = getter.MakeGenericMethod(ownerType);
                }
                object owner = _settlementNameplateViewGetter.Invoke(screen, null);
                return owner == null ? null : _settlementNameplateDataSourceField.GetValue(owner) as SettlementNameplatesVM;
            }
            catch { return null; }
        }

        private static string CaptureSettlementPartyBadges(Settlement settlement, bool inRange,
            SettlementNameplatesVM source)
        {
            // Preserve the host UI's current event-owned order and cached banner.
            // Re-enumerating Settlement.Parties and sorting can reorder equal-kind items.
            if (!inRange || settlement == null || source == null) return string.Empty;
            try
            {
                SettlementNameplateVM nameplate = source.GetNameplateOfSettlement(settlement);
                if (nameplate == null || !nameplate.IsInRange) return string.Empty;
                var items = nameplate.SettlementParties?.PartiesInSettlement;
                if (items == null || items.Count > CoopCampaignMapSettlementPartyBadgePolicy.MaxBadges)
                    return string.Empty;
                bool disguised = TaleWorlds.CampaignSystem.Campaign.Current.IsMainHeroDisguised;
                var badges = new List<CoopCampaignMapSettlementPartyBadge>();
                foreach (SettlementNameplatePartyMarkerItemVM item in items)
                {
                    MobileParty party = item?.Party;
                    if (party == null || !CoopCampaignMapSettlementPartyBadgePolicy.IsAdmitted(
                        inRange, party.IsGarrison, party.IsMilitia, party.IsMainParty, disguised,
                        party.Army != null, party.Army?.LeaderParty?.IsMainParty == true)) continue;
                    if ((item.IsLord ? 1 : 0) + (item.IsCaravan ? 1 : 0) + (item.IsDefault ? 1 : 0) != 1)
                        return string.Empty;
                    int kind = item.IsLord ? 0 : item.IsCaravan ? 1 : 2;
                    if (kind == 0 && item.Visual == null) return string.Empty;
                    badges.Add(new CoopCampaignMapSettlementPartyBadge
                    {
                        Kind = kind, IsBandit = item.IsBandit,
                        BannerCode = kind == 0 ? item.Visual.Id ?? string.Empty : string.Empty
                    });
                }
                return CoopCampaignMapSettlementPartyBadgePolicy.TryEncode(badges, out string encoded)
                    ? encoded : string.Empty;
            }
            catch { return string.Empty; }
        }

        private static int CaptureSettlementLevels(SettlementVisualManager manager, Settlement settlement)
        {
            // Called only after host disclosure admission, on the existing map tick.
            // Read the native choice; never dirty, refresh or mutate host visuals.
            try
            {
                if (manager == null || SettlementLevelMaskField?.FieldType != typeof(uint))
                    return 0;
                var visual = manager.GetVisualOfEntity(settlement.Party) as SettlementVisual;
                var campaign = TaleWorlds.CampaignSystem.Campaign.Current;
                if (visual?.StrategicEntity == null || campaign?.MapSceneWrapper == null)
                    return 0;
                uint mask = (uint)SettlementLevelMaskField.GetValue(visual);
                return CoopCampaignMapSettlementLevelPolicy.Capture(mask,
                    name => campaign.MapSceneWrapper.GetSceneLevel(name));
            }
            catch
            {
                // Unknown/version-mismatched visuals remain hidden on the replica.
                return 0;
            }
        }

        private static void CaptureSettlementPresentation(Settlement settlement,
            CoopCampaignMapPrototypeEntityState entity)
        {
            // Already admitted by the host disclosure gate. Same authoritative
            // inputs as native RefreshWallState/RefreshSiegePreparations, read-only.
            try
            {
                string walls = settlement.IsFortification
                    ? CoopCampaignMapSettlementPresentationPolicy.CaptureWalls(
                        settlement.SettlementWallSectionHitPointsRatioList)
                    : string.Empty;
                float? progress = null;
                if (settlement.IsUnderSiege)
                    progress = settlement.SiegeEvent.GetSiegeEventSide(BattleSideEnum.Attacker)
                        .SiegeEngines.SiegePreparations?.Progress;
                int preparation = CoopCampaignMapSettlementPresentationPolicy.CapturePreparation(
                    settlement.IsUnderSiege, progress);
                entity.SettlementWallStates = walls;
                entity.SettlementPreparationState = preparation;
            }
            catch
            {
                entity.SettlementWallStates = string.Empty;
                entity.SettlementPreparationState = 0;
            }
        }

        private void UpdateReplicaSnapshots(
            IReadOnlyList<CoopCampaignMapPrototypeEntityState> entities)
        {
            DateTime updatedUtc = DateTime.UtcNow;
            var currentCatalogEntities = new List<
                CoopCampaignMapPrototypeCatalogEntityState>(entities?.Count ?? 0);
            var currentDynamicEntities = new List<
                CoopCampaignMapPrototypeDynamicEntityState>(entities?.Count ?? 0);

            if (entities != null)
            {
                foreach (CoopCampaignMapPrototypeEntityState entity in entities)
                {
                    if (entity == null)
                        continue;

                    _replicaInformation.TryGetValue(
                        entity.EntityId,
                        out ReplicaInformation information);
                    CoopCampaignMapPrototypeSettlementKind settlementKind =
                        information?.SettlementKind ??
                        ResolveSettlementKind(entity.SettlementNameplateSize);
                    var catalogEntity =
                        new CoopCampaignMapPrototypeCatalogEntityState
                        {
                            EntityId = entity.EntityId,
                            DisplayName = entity.DisplayName,
                            PartyNameplateNameText = entity.PartyNameplateNameText,
                            Kind = entity.Kind,
                            SettlementNameplateSize = entity.SettlementNameplateSize,
                            SettlementKind = settlementKind,
                            SettlementLevelFlags = entity.SettlementLevelFlags,
                            SettlementWallStates = entity.SettlementWallStates,
                            SettlementPartyBadges = entity.SettlementPartyBadges,
                            HoverDecal = entity.HoverDecal,
                            SettlementPreparationState = entity.SettlementPreparationState,
                            SettlementRaidState = entity.SettlementRaidState,
                            PrimaryColor = entity.PrimaryColor,
                            SecondaryColor = entity.SecondaryColor,
                            NameplateColor = entity.NameplateColor,
                            BannerCode = entity.BannerCode,
                            VisualCharacterId = entity.VisualCharacterId,
                            CultureId = entity.CultureId,
                            PartyVisualKind = entity.PartyVisualKind,
                            SiegeTentPresentation = entity.SiegeTentPresentation,
                            HumanVisual = entity.HumanVisual?.Clone(),
                            MountVisual = entity.MountVisual?.Clone(),
                            CaravanMountVisual = entity.CaravanMountVisual?.Clone(),
                            FactionId = information?.FactionId ?? string.Empty,
                            FactionName = information?.FactionName ?? string.Empty,
                            OwnerName = information?.OwnerName ?? string.Empty,
                            LeaderName = information?.LeaderName ?? string.Empty,
                            ArmyId = information?.ArmyId ?? string.Empty,
                            ArmyName = information?.ArmyName ?? string.Empty,
                            IsArmyLeader = information?.IsArmyLeader ?? false,
                            SettlementProductionIconId =
                                information?.SettlementProductionIconId ??
                                string.Empty,
                            SelectionRadius = entity.Kind ==
                                CoopCampaignMapPrototypeEntityKind.Settlement
                                ? 30000
                                : 18000
                        };
                    var dynamicEntity =
                        new CoopCampaignMapPrototypeDynamicEntityState
                        {
                            EntityId = entity.EntityId,
                            NormalizedX = entity.NormalizedX,
                            NormalizedY = entity.NormalizedY,
                            Heading = entity.Heading,
                            PartySize = entity.PartySize,
                            IsPartyInformationHidden = entity.IsPartyInformationHidden,
                            PartyNameplateCountText = entity.PartyNameplateCountText,
                            PartyNameplateExtraInfoText = entity.PartyNameplateExtraInfoText,
                            PartyMovementSpeedText = entity.PartyMovementSpeedText,
                            PartyNameplateFlags = entity.PartyNameplateFlags,
                            IsVisible = true,
                            IsMoving = information?.IsMoving ?? false,
                            ArmyPartyCount = entity.IsPartyInformationHidden
                                ? 0 : information?.ArmyPartyCount ?? 0,
                            ArmyTotalSize = entity.IsPartyInformationHidden
                                ? 0 : information?.ArmyTotalSize ?? 0,
                            ArmyCohesion = entity.IsPartyInformationHidden
                                ? 0 : information?.ArmyCohesion ?? 0,
                            AppearanceRevision = 0,
                            InformationRevision = 0,
                            QuestMarkerFlags =
                                information?.QuestMarkerFlags ?? 0,
                            SettlementEventFlags =
                                information?.SettlementEventFlags ?? 0,
                            SettlementEventOrder = information?.SettlementEventOrder ?? 0,
                            SettlementQuestOrder = information?.SettlementQuestOrder ?? 0,
                            SettlementRelation = entity.SettlementRelation,
                            SettlementMapEventType = entity.SettlementMapEventType,
                            IsSettlementTracked = information?.IsSettlementTracked ?? false,
                            IsSettlementInRange = information?.IsSettlementInRange ?? false
                        };
                    if (!CoopCampaignMapPrototypeContract.IsValidCatalogEntity(
                            catalogEntity) ||
                        !CoopCampaignMapPrototypeContract.IsValidDynamicEntity(
                            dynamicEntity))
                    {
                        continue;
                    }

                    currentCatalogEntities.Add(catalogEntity);
                    currentDynamicEntities.Add(dynamicEntity);
                }
            }

            var currentDynamicById = new Dictionary<
                string,
                CoopCampaignMapPrototypeDynamicEntityState>(
                    StringComparer.OrdinalIgnoreCase);
            foreach (CoopCampaignMapPrototypeDynamicEntityState dynamicEntity in
                     currentDynamicEntities)
            {
                currentDynamicById[dynamicEntity.EntityId] = dynamicEntity;
            }

            foreach (CoopCampaignMapPrototypeCatalogEntityState catalogEntity in
                     currentCatalogEntities)
            {
                bool alreadyKnown = _knownCatalogById.TryGetValue(
                    catalogEntity.EntityId,
                    out CoopCampaignMapPrototypeCatalogEntityState knownCatalog);
                if (!alreadyKnown &&
                    _knownCatalogById.Count >=
                    CoopCampaignMapPrototypeContract.MaxCatalogEntities)
                {
                    continue;
                }

                if (!alreadyKnown ||
                    !CoopCampaignMapPrototypeCatalogDeltaPolicy.AreEquivalent(
                        knownCatalog,
                        catalogEntity))
                {
                    _knownCatalogById[catalogEntity.EntityId] =
                        catalogEntity.Clone();
                    _catalogDirty = true;
                }
                if (currentDynamicById.TryGetValue(
                        catalogEntity.EntityId,
                        out CoopCampaignMapPrototypeDynamicEntityState currentDynamic))
                {
                    _knownDynamicById[catalogEntity.EntityId] =
                        currentDynamic.Clone();
                }
            }

            var knownIds = new List<string>(_knownDynamicById.Keys);
            foreach (string entityId in knownIds)
            {
                if (currentDynamicById.ContainsKey(entityId))
                    continue;

                CoopCampaignMapPrototypeDynamicEntityState hidden =
                    _knownDynamicById[entityId].Clone();
                hidden.IsVisible = false;
                hidden.IsMoving = false;
                hidden.QuestMarkerFlags = 0;
                hidden.SettlementEventFlags = 0;
                hidden.SettlementEventOrder = 0;
                hidden.SettlementQuestOrder = 0;
                hidden.SettlementRelation = 0;
                hidden.SettlementMapEventType = 0;
                hidden.IsSettlementTracked = false;
                hidden.IsSettlementInRange = false;
                _knownDynamicById[entityId] = hidden;
            }

            List<CoopCampaignMapPrototypeCatalogEntityState> catalogEntities;
            if (_catalogDirty || _cachedCatalog == null)
            {
                catalogEntities = new List<
                    CoopCampaignMapPrototypeCatalogEntityState>(
                        _knownCatalogById.Count);
                foreach (CoopCampaignMapPrototypeCatalogEntityState catalogEntity in
                         _knownCatalogById.Values)
                {
                    catalogEntities.Add(catalogEntity.Clone());
                }
                catalogEntities.Sort(
                    (left, right) =>
                    {
                        int kindComparison = left.Kind.CompareTo(right.Kind);
                        return kindComparison != 0
                            ? kindComparison
                            : string.Compare(
                                left.EntityId,
                                right.EntityId,
                                StringComparison.OrdinalIgnoreCase);
                    });
            }
            else
            {
                catalogEntities = _cachedCatalog.Entities;
            }

            var dynamicEntities = new List<
                CoopCampaignMapPrototypeDynamicEntityState>(
                    catalogEntities.Count);
            foreach (CoopCampaignMapPrototypeCatalogEntityState catalogEntity in
                     catalogEntities)
            {
                if (_knownDynamicById.TryGetValue(
                        catalogEntity.EntityId,
                        out CoopCampaignMapPrototypeDynamicEntityState dynamicEntity))
                {
                    dynamicEntities.Add(dynamicEntity.Clone());
                }
            }

            if (_catalogDirty || _cachedCatalog == null)
            {
                if (_catalogRevision == int.MaxValue)
                    _catalogRevision = 0;
                _catalogRevision++;
                _cachedCatalog = new CoopCampaignMapPrototypeCatalogSnapshot
                {
                    SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
                    SessionId = _sessionId,
                    Revision = _catalogRevision,
                    Entities = catalogEntities,
                    UpdatedUtc = updatedUtc
                };
                _catalogDirty = true;
            }

            if (_dynamicRevision == int.MaxValue)
                _dynamicRevision = 0;
            _dynamicRevision++;
            _cachedDynamic = new CoopCampaignMapPrototypeDynamicSnapshot
            {
                SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
                SessionId = _sessionId,
                Revision = _dynamicRevision,
                Entities = dynamicEntities,
                UpdatedUtc = updatedUtc
            };
            _dynamicDirty = true;
        }

        private static CoopCampaignMapPrototypeSettlementKind ResolveSettlementKind(
            CoopCampaignMapPrototypeSettlementNameplateSize nameplateSize)
        {
            switch (nameplateSize)
            {
                case CoopCampaignMapPrototypeSettlementNameplateSize.Large:
                    return CoopCampaignMapPrototypeSettlementKind.Town;
                case CoopCampaignMapPrototypeSettlementNameplateSize.Medium:
                    return CoopCampaignMapPrototypeSettlementKind.Castle;
                case CoopCampaignMapPrototypeSettlementNameplateSize.Small:
                    return CoopCampaignMapPrototypeSettlementKind.Village;
                default:
                    return CoopCampaignMapPrototypeSettlementKind.None;
            }
        }

        private void AddMobilePartyCandidate(
            ICollection<VisibleEntityCandidate> candidates,
            MobileParty party,
            MobileParty mainParty,
            Vec2 minimum,
            Vec2 maximum,
            CoopCampaignMapPrototypeEntityKind kind,
            bool isVisible,
            MobilePartyVisual nativeVisual)
        {
            if (!isVisible || party?.Party == null)
                return;

            string entityId = "party:" + (party.StringId ?? string.Empty);
            Vec2 bearing = party.Bearing;
            int fallbackHeading = _entityHeadings.TryGetValue(
                entityId,
                out int cachedHeading)
                ? cachedHeading
                : 0;
            int heading = CoopCampaignMapPrototypeContract.QuantizeDirection(
                bearing.x,
                bearing.y,
                fallbackHeading);
            _entityHeadings[entityId] = heading;
            Vec2 partyPosition = party.VisualPosition2DWithoutError;
            ResolveAppearance(
                party.Party,
                out uint primaryColor,
                out uint secondaryColor,
                out string bannerCode);
            ResolvePartyVisualStateCached(
                entityId,
                kind,
                party,
                nativeVisual,
                out string visualCharacterId,
                out string cultureId,
                out CoopCampaignMapPrototypePartyVisualKind partyVisualKind,
                out CoopCampaignMapPrototypeAgentVisualState humanVisual,
                out CoopCampaignMapPrototypeAgentVisualState mountVisual,
                out CoopCampaignMapPrototypeAgentVisualState caravanMountVisual);
            if (!TryCreateEntityState(
                    entityId,
                    party.Name?.ToString(),
                    kind,
                    CoopCampaignMapPrototypeSettlementNameplateSize.None,
                    partyPosition,
                    heading,
                    CoopCampaignMapPartyLabelPolicy.ResolvePublishedCount(
                        party.IsInfoHidden, party.Party.NumberOfAllMembers),
                    primaryColor,
                    secondaryColor,
                    bannerCode,
                    visualCharacterId,
                    cultureId,
                    partyVisualKind,
                    humanVisual,
                    mountVisual,
                    caravanMountVisual,
                    minimum,
                    maximum,
                    out CoopCampaignMapPrototypeEntityState entity))
            {
                return;
            }

            entity.NameplateColor = ResolvePartyNameplateColor(party);
            if (CoopCampaignMapSiegeTentCapture.IsCampParticipant(party.Party))
            {
                if (_siegeTentCapture == null) _siegeTentCapture = new CoopCampaignMapSiegeTentCapture();
                if (_siegeTentCapture.TryCapture(party.Party, nativeVisual, true, out var tent) &&
                    CoopCampaignMapSiegeTentCodec.TryEncode(tent, out var tentBytes))
                {
                    entity.PartyVisualKind = CoopCampaignMapPrototypePartyVisualKind.SiegeTent;
                    entity.SiegeTentPresentation = Convert.ToBase64String(tentBytes);
                }
                // Failed capture retains None: never invent a hero for a native tent.
            }
            entity.HoverDecal = CaptureHoverDecal(party.Party, nativeVisual);
            entity.PartyNameplateNameText = CoopCampaignMapPrototypeContract.BoundEntityText(
                CoopCampaignMapPartyLabelPolicy.ResolveName(
                    party.Army != null && party.Army.DoesLeaderPartyAndAttachedPartiesContain(party),
                    party.ArmyName?.ToString(), party.LeaderHero?.Name?.ToString(), party.Name?.ToString()),
                CoopCampaignMapPrototypeContract.MaxPartyNameplateNameCharacters, string.Empty);
            entity.PartyNameplateFlags = CoopCampaignMapPartyLabelPolicy.ResolveFlags(
                party.Army != null && party.Army.LeaderParty == party,
                party.Army != null && party.AttachedTo != null,
                party.CurrentSettlement != null, party.IsDisorganized,
                party.IsCurrentlyAtSea, party.IsInRaftState);
            float partySpeed = party.IsActive ? party.Speed : 0f;
            entity.PartyMovementSpeedText = party.IsActive &&
                !float.IsNaN(partySpeed) && !float.IsInfinity(partySpeed)
                ? partySpeed.ToString("F1") : string.Empty;
            entity.IsPartyInformationHidden = party.IsInfoHidden;
            entity.PartyNameplateCountText = CoopCampaignMapPartyLabelPolicy.ResolveCountText(
                party.IsInfoHidden, SandBox.ViewModelCollection.SandBoxUIHelper.GetPartyHealthyCount(party));
            entity.PartyNameplateExtraInfoText = CoopCampaignMapPartyLabelPolicy.ResolveExtraInfoText(
                party.IsInfoHidden,
                SandBox.ViewModelCollection.SandBoxUIHelper.GetAllWoundedMembersAmount(party),
                SandBox.ViewModelCollection.SandBoxUIHelper.GetAllPrisonerMembersAmount(party),
                SandBox.ViewModelCollection.SandBoxUIHelper.GetPartyWoundedText,
                SandBox.ViewModelCollection.SandBoxUIHelper.GetPartyPrisonerText);
            candidates.Add(
                new VisibleEntityCandidate(
                    entity,
                    GetDistanceSquared(
                        mainParty.VisualPosition2DWithoutError,
                        partyPosition)));
            _replicaInformation[entity.EntityId] =
                CapturePartyInformation(party);
        }

        private static string CaptureHoverDecal(PartyBase party, MapEntityVisual visual)
        {
            if (party == null || visual == null || Hero.MainHero?.MapFaction == null) return string.Empty;
            try
            {
                MatrixFrame frame = visual.CircleLocalFrame;
                var descriptor = new CoopCampaignMapHoverDecal {
                    Fortification = party.IsSettlement && party.Settlement.IsFortification,
                    Color = CoopCampaignMapHoverDecal.ResolveColor(
                        FactionManager.IsAtWarAgainstFaction(party.MapFaction, Hero.MainHero.MapFaction),
                        DiplomacyHelper.IsSameFactionAndNotEliminated(party.MapFaction, Hero.MainHero.MapFaction),
                        DiplomacyHelper.HasAllianceWithFaction(party.MapFaction, Hero.MainHero.MapFaction)),
                    Frame = new[] { frame.rotation.s.x, frame.rotation.s.y, frame.rotation.s.z,
                        frame.rotation.f.x, frame.rotation.f.y, frame.rotation.f.z,
                        frame.rotation.u.x, frame.rotation.u.y, frame.rotation.u.z,
                        frame.origin.x, frame.origin.y, frame.origin.z }
                };
                return CoopCampaignMapHoverDecal.TryEncode(descriptor, out string encoded) ? encoded : string.Empty;
            }
            catch { return string.Empty; } // No fabricated presentation or hot-path diagnostic.
        }

        private static int CaptureSettlementRelation(Settlement settlement)
        {
            IFaction hostFaction = Hero.MainHero?.MapFaction;
            if (settlement?.OwnerClan == null || hostFaction == null) return 0;
            return CoopCampaignMapSettlementRelationPolicy.Resolve(true,
                FactionManager.IsAtWarAgainstFaction(settlement.MapFaction, hostFaction),
                DiplomacyHelper.IsSameFactionAndNotEliminated(settlement.MapFaction, hostFaction),
                DiplomacyHelper.HasAllianceWithFaction(settlement.MapFaction, hostFaction));
        }

        private static uint ResolvePartyNameplateColor(MobileParty party)
        {
            // Match native label precedence on the real campaign host only.
            // Do not infer diplomacy or instantiate campaign objects on clients.
            Army army = party.Army;
            bool armyLeader = army != null && army.LeaderParty == party;
            bool mainPartyOrAttachedPeer = party.IsMainParty ||
                (army?.LeaderParty != null &&
                 army.LeaderParty.AttachedParties.Contains(MobileParty.MainParty) &&
                 army.LeaderParty.AttachedParties.Contains(party));
            IFaction hostFaction = Hero.MainHero?.MapFaction;
            return CoopCampaignMapPartyLabelPolicy.ResolveColor(
                mainPartyOrAttachedPeer,
                hostFaction != null &&
                    FactionManager.IsAtWarAgainstFaction(party.MapFaction, hostFaction),
                hostFaction != null &&
                    DiplomacyHelper.IsSameFactionAndNotEliminated(party.MapFaction, hostFaction),
                hostFaction != null &&
                    DiplomacyHelper.HasAllianceWithFaction(party.MapFaction, hostFaction),
                armyLeader);
        }

        private static bool TryCreateEntityState(
            string entityId,
            string displayName,
            CoopCampaignMapPrototypeEntityKind kind,
            CoopCampaignMapPrototypeSettlementNameplateSize
                settlementNameplateSize,
            Vec2 position,
            int heading,
            int partySize,
            uint primaryColor,
            uint secondaryColor,
            string bannerCode,
            string visualCharacterId,
            string cultureId,
            CoopCampaignMapPrototypePartyVisualKind partyVisualKind,
            CoopCampaignMapPrototypeAgentVisualState humanVisual,
            CoopCampaignMapPrototypeAgentVisualState mountVisual,
            CoopCampaignMapPrototypeAgentVisualState caravanMountVisual,
            Vec2 minimum,
            Vec2 maximum,
            out CoopCampaignMapPrototypeEntityState entity)
        {
            entity = null;
            if (!CoopCampaignMapPrototypeContract.TryNormalizeMapPosition(
                    position.x,
                    position.y,
                    minimum.x,
                    minimum.y,
                    maximum.x,
                    maximum.y,
                    out int normalizedX,
                    out int normalizedY))
            {
                return false;
            }

            entity = new CoopCampaignMapPrototypeEntityState
            {
                EntityId = CoopCampaignMapPrototypeContract.BoundEntityText(
                    entityId,
                    CoopCampaignMapPrototypeContract.MaxEntityIdCharacters,
                    "unknown"),
                DisplayName = CoopCampaignMapPrototypeContract.BoundEntityText(
                    displayName,
                    CoopCampaignMapPrototypeContract.MaxEntityNameCharacters,
                    "Unknown"),
                Kind = kind,
                SettlementNameplateSize = settlementNameplateSize,
                NormalizedX = normalizedX,
                NormalizedY = normalizedY,
                Heading = heading,
                PartySize = Math.Max(
                    0,
                    Math.Min(
                        CoopCampaignMapPrototypeContract.MaximumPartySize,
                        partySize)),
                PrimaryColor = primaryColor == 0u
                    ? DefaultEntityColor
                    : primaryColor,
                SecondaryColor = secondaryColor == 0u
                    ? DefaultEntitySecondaryColor
                    : secondaryColor,
                NameplateColor = primaryColor == 0u
                    ? DefaultEntityColor
                    : primaryColor,
                BannerCode = CoopCampaignMapPrototypeContract.BoundEntityText(
                    bannerCode,
                    CoopCampaignMapPrototypeContract.MaxBannerCodeCharacters,
                    string.Empty),
                VisualCharacterId =
                    CoopCampaignMapPrototypeContract.BoundEntityText(
                        visualCharacterId,
                        CoopCampaignMapPrototypeContract
                            .MaxVisualCharacterIdCharacters,
                        string.Empty),
                CultureId = CoopCampaignMapPrototypeContract.BoundEntityText(
                    cultureId,
                    CoopCampaignMapPrototypeContract.MaxCultureIdCharacters,
                    string.Empty),
                PartyVisualKind = partyVisualKind,
                HumanVisual = humanVisual?.Clone(),
                MountVisual = mountVisual?.Clone(),
                CaravanMountVisual = caravanMountVisual?.Clone()
            };
            return CoopCampaignMapPrototypeContract.IsValidVisibleEntity(entity);
        }

        private static CoopCampaignMapPrototypeSettlementNameplateSize
            ResolveSettlementNameplateSize(Settlement settlement)
        {
            if (settlement?.IsTown == true)
                return CoopCampaignMapPrototypeSettlementNameplateSize.Large;
            if (settlement?.IsCastle == true)
                return CoopCampaignMapPrototypeSettlementNameplateSize.Medium;
            return CoopCampaignMapPrototypeSettlementNameplateSize.Small;
        }

        private static void ResolveAppearance(
            PartyBase party,
            out uint primaryColor,
            out uint secondaryColor,
            out string bannerCode)
        {
            primaryColor = DefaultEntityColor;
            secondaryColor = DefaultEntitySecondaryColor;
            bannerCode = string.Empty;
            try
            {
                primaryColor = party?.MapFaction?.Color ?? DefaultEntityColor;
                secondaryColor = party?.MapFaction?.Color2 ??
                                 DefaultEntitySecondaryColor;
                bannerCode = party?.Banner?.BannerCode ?? string.Empty;
            }
            catch
            {
            }
        }

        private static ReplicaInformation CapturePartyInformation(
            MobileParty party)
        {
            var information = new ReplicaInformation();
            if (party == null)
                return information;
            try
            {
                information.FactionId = BoundInformation(
                    party.MapFaction?.StringId);
                information.FactionName = BoundInformation(
                    party.MapFaction?.Name?.ToString());
                information.OwnerName = BoundInformation(
                    party.Owner?.Name?.ToString());
                information.LeaderName = BoundInformation(
                    party.LeaderHero?.Name?.ToString());
                information.IsMoving = party.IsMoving;
                Army army = party.Army;
                if (army != null)
                {
                    information.ArmyId = BoundInformation(
                        army.LeaderParty?.StringId);
                    information.ArmyName = BoundInformation(
                        army.Name?.ToString());
                    information.IsArmyLeader = army.LeaderParty == party;
                    information.ArmyPartyCount = Math.Max(
                        0,
                        Math.Min(
                            CoopCampaignMapPrototypeContract.MaximumPartySize,
                            army.Parties?.Count ?? 0));
                    information.ArmyTotalSize = Math.Max(
                        0,
                        Math.Min(
                            CoopCampaignMapPrototypeContract.MaximumPartySize,
                            army.TotalManCount));
                    information.ArmyCohesion =
                        CoopCampaignMapPrototypeContract.QuantizeUnit(
                            army.Cohesion / 100d);
                }
                information.QuestMarkerFlags =
                    CapturePartyQuestMarkerFlags(party);
            }
            catch
            {
            }
            return information;
        }

        private static ReplicaInformation CaptureSettlementInformation(
            Settlement settlement, SettlementNameplatesVM source)
        {
            var information = new ReplicaInformation
            {
                SettlementKind = settlement == null
                    ? CoopCampaignMapPrototypeSettlementKind.None
                    : CoopCampaignMapPrototypeSettlementKind.Special
            };
            if (settlement == null)
                return information;
            try
            {
                information.SettlementKind = ResolveSettlementKind(settlement);
                information.FactionId = BoundInformation(
                    settlement.MapFaction?.StringId);
                information.FactionName = BoundInformation(
                    settlement.MapFaction?.Name?.ToString());
                information.OwnerName = BoundInformation(
                    settlement.OwnerClan?.Name?.ToString());
                information.LeaderName = BoundInformation(
                    settlement.OwnerClan?.Leader?.Name?.ToString());
                CaptureSettlementMarkers(settlement, information, source);
            }
            catch
            {
            }
            return information;
        }

        private static string BoundInformation(string value)
        {
            return CoopCampaignMapPrototypeContract.BoundEntityText(
                value,
                CoopCampaignMapPrototypeContract.MaxInformationTextCharacters,
                string.Empty);
        }

        private static int CapturePartyQuestMarkerFlags(MobileParty party)
        {
            if (party == null)
                return 0;

            int flags = party == MobileParty.MainParty
                ? 0
                : CaptureSinglePartyQuestMarkerFlags(party);
            Army army = party.Army;
            if (army?.LeaderParty != party || party.AttachedParties == null)
                return flags;

            foreach (MobileParty attachedParty in party.AttachedParties)
            {
                if (attachedParty == null ||
                    attachedParty == party ||
                    attachedParty == MobileParty.MainParty)
                {
                    continue;
                }
                flags |= CaptureSinglePartyQuestMarkerFlags(attachedParty);
            }
            return flags & CoopCampaignMapPrototypeContract.MaxQuestMarkerFlags;
        }

        private static int CaptureSinglePartyQuestMarkerFlags(MobileParty party)
        {
            int flags = 0;
            if (party?.LeaderHero?.Issue != null)
            {
                flags |= (int)CampaignUIHelper.GetIssueType(
                    party.LeaderHero.Issue);
            }

            List<QuestBase> quests =
                CampaignUIHelper.GetQuestsRelatedToParty(party);
            if (quests == null)
                return flags;

            foreach (QuestBase quest in quests)
            {
                if (quest == null)
                    continue;
                bool isQuestGiver =
                    party.LeaderHero != null &&
                    quest.QuestGiver == party.LeaderHero;
                flags |= isQuestGiver
                    ? quest.IsSpecialQuest
                        ? (int)CoopCampaignMapPrototypeQuestMarkerFlags
                            .ActiveStoryQuest
                        : (int)CoopCampaignMapPrototypeQuestMarkerFlags
                            .ActiveIssue
                    : quest.IsSpecialQuest
                        ? (int)CoopCampaignMapPrototypeQuestMarkerFlags
                            .TrackedStoryQuest
                        : (int)CoopCampaignMapPrototypeQuestMarkerFlags
                            .TrackedIssue;
            }
            return flags;
        }

        private static void CaptureSettlementMarkers(
            Settlement settlement,
            ReplicaInformation information,
            SettlementNameplatesVM source)
        {
            if (settlement == null || information == null) return;
            information.IsSettlementTracked =
                TaleWorlds.CampaignSystem.Campaign.Current.VisualTrackerManager
                    .CheckTracked(settlement);
            bool modelInRange = settlement.IsHideout
                ? (settlement.SettlementComponent as ISpottable)?.IsSpotted == true
                : settlement.IsInspected;
            SettlementNameplateVM nameplate = source?.GetNameplateOfSettlement(settlement);
            // Existing host presentation only; never populate, tick or register a VM.
            // Intersect model and displayed range so a stale UI cannot disclose more.
            information.IsSettlementInRange = modelInRange && nameplate?.IsInRange == true;
            var events = nameplate?.SettlementEvents?.EventsList;
            var quests = nameplate?.SettlementEvents?.TrackQuests;
            if (events == null || quests == null || events.Count > 7 || quests.Count > 5)
                return;

            var eventTypes = new List<int>(7);
            var questFlags = new List<int>(5);
            string productionIcon = string.Empty;
            foreach (SettlementNameplateEventItemVM item in events)
            {
                if (item == null || item.Type < 0 || item.Type > 6) return;
                if (CoopCampaignMapSettlementEventPolicy.FilterEvents(
                    1 << item.Type, information.IsSettlementInRange) == 0) continue;
                if (item.Type == 6)
                {
                    productionIcon = item.AdditionalParameters;
                    if (string.IsNullOrWhiteSpace(productionIcon) ||
                        productionIcon.Length > CoopCampaignMapPrototypeContract.MaxProductionIconIdCharacters ||
                        productionIcon.Any(char.IsControl)) return;
                }
                else if (!string.IsNullOrEmpty(item.AdditionalParameters)) return;
                eventTypes.Add(item.Type);
            }
            // Native UnloadEvents retains TrackQuests; never export that hidden cache.
            if (information.IsSettlementInRange)
            {
                foreach (var quest in quests)
                {
                    if (quest == null) return;
                    questFlags.Add((int)quest.IssueQuestFlag);
                }
            }
            if (!CoopCampaignMapSettlementMarkerOrder.TryEncodeEvents(eventTypes,
                    out int eventOrder, out int eventMask) ||
                !CoopCampaignMapSettlementMarkerOrder.TryEncodeQuests(questFlags,
                    out int questOrder, out int questMask)) return;
            information.SettlementEventFlags = eventMask;
            information.QuestMarkerFlags = questMask;
            information.SettlementEventOrder = eventOrder;
            information.SettlementQuestOrder = questOrder;
            information.SettlementProductionIconId = productionIcon;
        }

        private static CoopCampaignMapPrototypeSettlementKind ResolveSettlementKind(
            Settlement settlement)
        {
            if (settlement?.IsTown == true)
                return CoopCampaignMapPrototypeSettlementKind.Town;
            if (settlement?.IsCastle == true)
                return CoopCampaignMapPrototypeSettlementKind.Castle;
            if (settlement?.IsVillage == true)
                return CoopCampaignMapPrototypeSettlementKind.Village;
            if (settlement?.IsHideout == true)
                return CoopCampaignMapPrototypeSettlementKind.Hideout;
            return settlement == null
                ? CoopCampaignMapPrototypeSettlementKind.None
                : CoopCampaignMapPrototypeSettlementKind.Special;
        }

        private void ResolvePartyVisualStateCached(
            string entityId,
            CoopCampaignMapPrototypeEntityKind entityKind,
            MobileParty party,
            MobilePartyVisual nativeVisual,
            out string visualCharacterId,
            out string cultureId,
            out CoopCampaignMapPrototypePartyVisualKind partyVisualKind,
            out CoopCampaignMapPrototypeAgentVisualState humanVisual,
            out CoopCampaignMapPrototypeAgentVisualState mountVisual,
            out CoopCampaignMapPrototypeAgentVisualState caravanMountVisual)
        {
            if (CoopCampaignMapSiegeTentCapture.IsCampParticipant(party?.Party))
            {
                visualCharacterId = cultureId = string.Empty;
                partyVisualKind = CoopCampaignMapPrototypePartyVisualKind.None;
                humanVisual = mountVisual = caravanMountVisual = null;
                return;
            }
            if (entityKind != CoopCampaignMapPrototypeEntityKind.MainParty &&
                CanReusePartyVisualState(
                    entityId,
                    party,
                    nativeVisual,
                    out CoopCampaignMapPrototypeCatalogEntityState cached))
            {
                visualCharacterId = cached.VisualCharacterId;
                cultureId = cached.CultureId;
                partyVisualKind = cached.PartyVisualKind;
                humanVisual = cached.HumanVisual?.Clone();
                mountVisual = cached.MountVisual?.Clone();
                caravanMountVisual = cached.CaravanMountVisual?.Clone();
                return;
            }

            ResolvePartyVisualState(
                party,
                nativeVisual,
                out visualCharacterId,
                out cultureId,
                out partyVisualKind,
                out humanVisual,
                out mountVisual,
                out caravanMountVisual);
            RememberPartyVisualReferences(entityId, nativeVisual);
        }

        private bool CanReusePartyVisualState(
            string entityId,
            MobileParty party,
            MobilePartyVisual nativeVisual,
            out CoopCampaignMapPrototypeCatalogEntityState cached)
        {
            cached = null;
            if (string.IsNullOrWhiteSpace(entityId) ||
                party?.Party == null ||
                party.Party.IsVisualDirty ||
                !_knownCatalogById.TryGetValue(entityId, out cached) ||
                cached.PartyVisualKind == CoopCampaignMapPrototypePartyVisualKind.SiegeTent ||
                !_knownHumanVisualReferenceById.TryGetValue(
                    entityId,
                    out AgentVisuals knownHuman) ||
                !_knownMountVisualReferenceById.TryGetValue(
                    entityId,
                    out AgentVisuals knownMount) ||
                !_knownCaravanVisualReferenceById.TryGetValue(
                    entityId,
                    out AgentVisuals knownCaravan))
            {
                return false;
            }

            AgentVisuals currentHuman = nativeVisual?.HumanAgentVisuals;
            AgentVisuals currentMount = nativeVisual?.MountAgentVisuals;
            AgentVisuals currentCaravan = nativeVisual?.CaravanMountAgentVisuals;
            if (!ReferenceEquals(currentHuman, knownHuman) ||
                !ReferenceEquals(currentMount, knownMount) ||
                !ReferenceEquals(currentCaravan, knownCaravan))
            {
                return false;
            }

            string currentCharacterId = currentHuman?.GetCharacterObjectID();
            if (!string.IsNullOrWhiteSpace(currentCharacterId) &&
                !string.Equals(
                    currentCharacterId,
                    cached.VisualCharacterId,
                    StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        private void RememberPartyVisualReferences(
            string entityId,
            MobilePartyVisual nativeVisual)
        {
            if (string.IsNullOrWhiteSpace(entityId))
                return;

            _knownHumanVisualReferenceById[entityId] =
                nativeVisual?.HumanAgentVisuals;
            _knownMountVisualReferenceById[entityId] =
                nativeVisual?.MountAgentVisuals;
            _knownCaravanVisualReferenceById[entityId] =
                nativeVisual?.CaravanMountAgentVisuals;
        }

        private static void ResolvePartyVisualState(
            MobileParty party,
            MobilePartyVisual nativeVisual,
            out string visualCharacterId,
            out string cultureId,
            out CoopCampaignMapPrototypePartyVisualKind partyVisualKind,
            out CoopCampaignMapPrototypeAgentVisualState humanVisual,
            out CoopCampaignMapPrototypeAgentVisualState mountVisual,
            out CoopCampaignMapPrototypeAgentVisualState caravanMountVisual)
        {
            visualCharacterId = string.Empty;
            cultureId = string.Empty;
            partyVisualKind = CoopCampaignMapPrototypePartyVisualKind.None;
            humanVisual = null;
            mountVisual = null;
            caravanMountVisual = null;
            if (party?.Party == null)
                return;

            try
            {
                CharacterObject visualLeader =
                    PartyBaseHelper.GetVisualPartyLeader(party.Party);
                humanVisual = CaptureAgentVisual(
                    nativeVisual?.HumanAgentVisuals,
                    includeBodyProperties: true);
                mountVisual = CaptureAgentVisual(
                    nativeVisual?.MountAgentVisuals,
                    includeBodyProperties: false);
                caravanMountVisual = CaptureAgentVisual(
                    nativeVisual?.CaravanMountAgentVisuals,
                    includeBodyProperties: false);
                if (humanVisual == null && visualLeader != null)
                {
                    humanVisual = CaptureCharacterVisual(
                        visualLeader,
                        party.Party.Banner,
                        includeMountOnly: false);
                }
                if (mountVisual == null && visualLeader?.HasMount() == true)
                {
                    mountVisual = CaptureCharacterVisual(
                        visualLeader,
                        banner: null,
                        includeMountOnly: true);
                }
                if (party.IsCaravan && caravanMountVisual == null)
                    caravanMountVisual = mountVisual?.Clone();
                visualCharacterId =
                    nativeVisual?.HumanAgentVisuals?.GetCharacterObjectID() ??
                    visualLeader?.StringId ??
                    string.Empty;
                cultureId = visualLeader?.Culture?.StringId ??
                            party.Party.Culture?.StringId ??
                            string.Empty;

                if (humanVisual != null &&
                    party.IsCaravan &&
                    (caravanMountVisual != null || mountVisual != null))
                {
                    partyVisualKind =
                        CoopCampaignMapPrototypePartyVisualKind.Caravan;
                }
                else if (humanVisual != null && mountVisual != null)
                {
                    partyVisualKind =
                        CoopCampaignMapPrototypePartyVisualKind.Mounted;
                }
                else if (humanVisual != null)
                {
                    partyVisualKind =
                        CoopCampaignMapPrototypePartyVisualKind.Foot;
                }
                else if (party.IsCaravan)
                {
                    partyVisualKind =
                        CoopCampaignMapPrototypePartyVisualKind.Caravan;
                }
                else if (visualLeader?.HasMount() == true)
                {
                    partyVisualKind =
                        CoopCampaignMapPrototypePartyVisualKind.Mounted;
                }
                else if (visualLeader != null)
                {
                    partyVisualKind =
                        CoopCampaignMapPrototypePartyVisualKind.Foot;
                }
            }
            catch
            {
                visualCharacterId = string.Empty;
                cultureId = party.Party.Culture?.StringId ?? string.Empty;
                partyVisualKind = party.IsCaravan
                    ? CoopCampaignMapPrototypePartyVisualKind.Caravan
                    : CoopCampaignMapPrototypePartyVisualKind.None;
                humanVisual = null;
                mountVisual = null;
                caravanMountVisual = null;
            }
        }

        private static MobilePartyVisual TryGetMobilePartyVisual(
            MobilePartyVisualManager visualManager,
            PartyBase party)
        {
            if (visualManager == null || party == null)
                return null;
            try
            {
                return visualManager.GetVisualOfEntity(party) as MobilePartyVisual;
            }
            catch
            {
                return null;
            }
        }

        private static CoopCampaignMapPrototypeAgentVisualState CaptureAgentVisual(
            AgentVisuals visual,
            bool includeBodyProperties)
        {
            if (visual == null)
                return null;

            try
            {
                AgentVisualsData data = visual.GetCopyAgentVisualsData();
                Equipment equipment = visual.GetEquipment() ?? data?.EquipmentData;
                if (data == null || equipment == null)
                    return null;

                var itemIds = new string[
                    CoopCampaignMapPrototypeContract.EquipmentSlotCount];
                for (int slot = 0; slot < itemIds.Length; slot++)
                {
                    if (!includeBodyProperties &&
                        slot != (int)EquipmentIndex.Horse &&
                        slot != (int)EquipmentIndex.HorseHarness)
                    {
                        itemIds[slot] = string.Empty;
                        continue;
                    }

                    EquipmentElement element = equipment[(EquipmentIndex)slot];
                    ItemObject visualItem = element.CosmeticItem ?? element.Item;
                    itemIds[slot] =
                        CoopCampaignMapPrototypeContract.BoundEntityText(
                            visualItem?.StringId,
                            CoopCampaignMapPrototypeContract
                                .MaxVisualItemIdCharacters,
                            string.Empty);
                }

                string bodyProperties = includeBodyProperties
                    ? visual.GetBodyProperties().ToString()
                    : string.Empty;
                var captured = new CoopCampaignMapPrototypeAgentVisualState
                {
                    BodyProperties =
                        CoopCampaignMapPrototypeContract.BoundEntityText(
                            bodyProperties,
                            CoopCampaignMapPrototypeContract
                                .MaxBodyPropertiesCharacters,
                            string.Empty),
                    IsFemale = visual.GetIsFemale(),
                    Race = data.RaceData,
                    SkeletonType = (int)data.SkeletonTypeData,
                    RightWieldedItemIndex = data.RightWieldedItemIndexData,
                    LeftWieldedItemIndex = data.LeftWieldedItemIndexData,
                    MountCreationKey =
                        CoopCampaignMapPrototypeContract.BoundEntityText(
                            data.MountCreationKeyData,
                            CoopCampaignMapPrototypeContract
                                .MaxMountCreationKeyCharacters,
                            string.Empty),
                    HasBanner = data.BannerData != null,
                    AddColorRandomness = data.AddColorRandomnessData,
                    EquipmentItemIds = itemIds
                };
                return CoopCampaignMapPrototypeContract.IsValidAgentVisualState(
                    captured,
                    includeBodyProperties)
                    ? captured
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static CoopCampaignMapPrototypeAgentVisualState CaptureCharacterVisual(
            CharacterObject character,
            Banner banner,
            bool includeMountOnly)
        {
            if (character == null)
                return null;

            try
            {
                Equipment equipment = character.Equipment?.Clone(false);
                if (equipment == null)
                    return null;

                var itemIds = new string[
                    CoopCampaignMapPrototypeContract.EquipmentSlotCount];
                for (int slot = 0; slot < itemIds.Length; slot++)
                {
                    if (includeMountOnly &&
                        slot != (int)EquipmentIndex.Horse &&
                        slot != (int)EquipmentIndex.HorseHarness)
                    {
                        itemIds[slot] = string.Empty;
                        continue;
                    }

                    EquipmentElement element = equipment[(EquipmentIndex)slot];
                    ItemObject visualItem = element.CosmeticItem ?? element.Item;
                    itemIds[slot] =
                        CoopCampaignMapPrototypeContract.BoundEntityText(
                            visualItem?.StringId,
                            CoopCampaignMapPrototypeContract.MaxVisualItemIdCharacters,
                            string.Empty);
                }

                ItemObject horseItem = equipment[EquipmentIndex.Horse].Item;
                string mountCreationKey = string.Empty;
                if (includeMountOnly && horseItem != null)
                {
                    mountCreationKey = MountCreationKey.GetRandomMountKeyString(
                        horseItem,
                        character.GetMountKeySeed());
                }

                var captured = new CoopCampaignMapPrototypeAgentVisualState
                {
                    BodyProperties = includeMountOnly
                        ? string.Empty
                        : CoopCampaignMapPrototypeContract.BoundEntityText(
                            character.GetBodyProperties(
                                equipment,
                                StableVisualSeed(character.StringId)).ToString(),
                            CoopCampaignMapPrototypeContract.MaxBodyPropertiesCharacters,
                            string.Empty),
                    IsFemale = character.IsFemale,
                    Race = character.Race,
                    SkeletonType = character.IsFemale ? 1 : 0,
                    RightWieldedItemIndex = -1,
                    LeftWieldedItemIndex = -1,
                    MountCreationKey =
                        CoopCampaignMapPrototypeContract.BoundEntityText(
                            mountCreationKey,
                            CoopCampaignMapPrototypeContract.MaxMountCreationKeyCharacters,
                            string.Empty),
                    HasBanner = !includeMountOnly && banner != null,
                    AddColorRandomness = !character.IsHero,
                    EquipmentItemIds = itemIds
                };
                return CoopCampaignMapPrototypeContract.IsValidAgentVisualState(
                    captured,
                    requireBodyProperties: !includeMountOnly)
                    ? captured
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private static int StableVisualSeed(string value)
        {
            unchecked
            {
                int hash = 17;
                foreach (char character in value ?? string.Empty)
                    hash = hash * 31 + character;
                return hash & int.MaxValue;
            }
        }

        private static double GetDistanceSquared(Vec2 left, Vec2 right)
        {
            double x = left.x - right.x;
            double y = left.y - right.y;
            return x * x + y * y;
        }

        private static List<CoopCampaignMapPrototypeEntityState>
            CloneVisibleEntities(
                IEnumerable<CoopCampaignMapPrototypeEntityState> entities)
        {
            var clone = new List<CoopCampaignMapPrototypeEntityState>();
            if (entities == null)
                return clone;

            foreach (CoopCampaignMapPrototypeEntityState entity in entities)
            {
                if (entity != null)
                    clone.Add(entity.Clone());
            }
            return clone;
        }

        private sealed class VisibleEntityCandidate
        {
            public VisibleEntityCandidate(
                CoopCampaignMapPrototypeEntityState entity,
                double distanceSquared)
            {
                Entity = entity;
                DistanceSquared = distanceSquared;
            }

            public CoopCampaignMapPrototypeEntityState Entity { get; }

            public double DistanceSquared { get; }
        }

        private sealed class ReplicaInformation
        {
            public string FactionId { get; set; } = string.Empty;

            public string FactionName { get; set; } = string.Empty;

            public string OwnerName { get; set; } = string.Empty;

            public string LeaderName { get; set; } = string.Empty;

            public string ArmyId { get; set; } = string.Empty;

            public string ArmyName { get; set; } = string.Empty;

            public bool IsArmyLeader { get; set; }

            public bool IsMoving { get; set; }

            public int ArmyPartyCount { get; set; }

            public int ArmyTotalSize { get; set; }

            public int ArmyCohesion { get; set; }

            public int QuestMarkerFlags { get; set; }

            public int SettlementEventFlags { get; set; }

            public int SettlementEventOrder { get; set; }

            public int SettlementQuestOrder { get; set; }

            public bool IsSettlementTracked { get; set; }

            public bool IsSettlementInRange { get; set; }

            public string SettlementProductionIconId { get; set; } =
                string.Empty;

            public CoopCampaignMapPrototypeSettlementKind SettlementKind { get; set; }
        }

        private static CoopCampaignMapPrototypeCameraState TryCaptureMapCamera()
        {
            try
            {
                MapScreen mapScreen = MapScreen.Instance;
                MapCameraView mapCameraView = mapScreen?.MapCameraView;
                Camera camera = mapCameraView?.Camera;
                if (mapScreen == null || mapCameraView == null || camera == null)
                    return null;

                MatrixFrame frame = mapCameraView.CameraFrame;
                Vec3 direction = -frame.rotation.u;
                Vec3 up = frame.rotation.f;
                return CoopCampaignMapPrototypeContract.TryQuantizeCamera(
                    frame.origin.x,
                    frame.origin.y,
                    frame.origin.z,
                    direction.x,
                    direction.y,
                    direction.z,
                    up.x,
                    up.y,
                    up.z,
                    camera.GetFovVertical(),
                    out CoopCampaignMapPrototypeCameraState cameraState)
                    ? cameraState
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private void UpdateConversationSnapshot()
        {
            _capturedConversationPose = null;
            if (!CoopCampaignMapConversationCapture.TryCapture(
                    out CoopCampaignMapConversationState captured,
                    out CoopCampaignMapConversationPose pose,
                    out string reason))
            {
                if (!_clearConversationOnCaptureFailure || CoopDebugConfig.VerboseDiagnostics)
                    LogFailureRateLimited("conversation-capture:" + (reason ?? "unknown"));
                if (!_clearConversationOnCaptureFailure) return;
                captured = CoopCampaignMapPrototypeContract.CreateInactiveConversation();
                pose = null;
            }

            if (!CoopCampaignMapPrototypeContract
                .AreConversationContentsEquivalent(
                    _cachedConversation,
                    captured))
            {
                if (_conversationRevision == int.MaxValue)
                    _conversationRevision = 0;
                _conversationRevision++;
                captured.Revision = _conversationRevision;
                _cachedConversation = captured.Clone();
            }
            if (pose != null)
            {
                pose.ConversationRevision = _cachedConversation.Revision;
                pose.Sequence = _revision;
                _capturedConversationPose = pose;
            }
        }

        private void LogFailureRateLimited(string reason)
        {
            DateTime utcNow = DateTime.UtcNow;
            if (utcNow < _nextFailureLogUtc)
                return;

            _nextFailureLogUtc = utcNow + FailureLogInterval;
            ModLogger.Info(
                "CoopCampaignMapPrototypePublisher: state unavailable. Reason=" +
                (reason ?? "unknown") + ".");
        }
    }
}
