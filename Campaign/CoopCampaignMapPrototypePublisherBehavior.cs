using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Infrastructure;
using SandBox.View.Map;
using SandBox.View.Map.Managers;
using SandBox.View.Map.Visuals;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Campaign
{
    public sealed class CoopCampaignMapPrototypePublisherBehavior :
        CampaignBehaviorBase
    {
        private const float PublishIntervalSeconds = 0.2f;
        private const float VisibleEntityCaptureIntervalSeconds = 0.5f;
        private const uint DefaultEntityColor = 0xFFB89A5Au;
        private const uint DefaultEntitySecondaryColor = 0xFFF2D078u;
        private static readonly TimeSpan FailureLogInterval =
            TimeSpan.FromSeconds(10d);
        private static CoopCampaignMapPrototypePublisherBehavior
            _applicationTickPublisher;

        private string _sessionId = Guid.NewGuid().ToString("N");
        private int _revision;
        private int _visibleEntitiesRevision;
        private int _lastHeading;
        private float _timeUntilNextPublish;
        private float _timeUntilNextVisibleEntityCapture;
        private List<CoopCampaignMapPrototypeEntityState> _cachedVisibleEntities =
            new List<CoopCampaignMapPrototypeEntityState>();
        private readonly Dictionary<string, int> _entityHeadings =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private bool _initialPublishLogged;
        private DateTime _nextFailureLogUtc = DateTime.MinValue;

        public override void RegisterEvents()
        {
            _applicationTickPublisher = this;
        }

        public override void SyncData(IDataStore dataStore)
        {
        }

        public static void PumpApplicationTick(float dt)
        {
            if (!ExperimentalFeatures.EnableCampaignMapPrototype)
                return;

            CoopCampaignMapPrototypePublisherBehavior publisher =
                _applicationTickPublisher;
            if (publisher == null ||
                TaleWorlds.CampaignSystem.Campaign.Current == null ||
                Mission.Current != null)
            {
                return;
            }

            if (!IsMapCameraAvailable())
                return;

            publisher.PublishTick(dt);
        }

        public static void ResetApplicationTickPublisher()
        {
            _applicationTickPublisher = null;
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

            if (!TryCaptureSnapshot(out CoopCampaignMapPrototypeHostSnapshot snapshot, out string reason))
            {
                LogFailureRateLimited("capture:" + (reason ?? "unknown"));
                return;
            }

            if (!CoopCampaignMapPrototypeBridgeFile.TryWrite(snapshot, out reason))
            {
                LogFailureRateLimited("write:" + (reason ?? "unknown"));
                return;
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
            if (Mission.Current != null)
            {
                reason = "mission-active";
                return false;
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
                }
                CoopCampaignMapPrototypeCameraState cameraState =
                    TryCaptureMapCamera();
                snapshot = new CoopCampaignMapPrototypeHostSnapshot
                {
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
                    VisibleEntities = CloneVisibleEntities(
                        _cachedVisibleEntities),
                    Camera = cameraState,
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

        private List<CoopCampaignMapPrototypeEntityState> CaptureVisibleEntities(
            TaleWorlds.CampaignSystem.Campaign campaign,
            MobileParty mainParty,
            Vec2 minimum,
            Vec2 maximum)
        {
            var candidates = new List<VisibleEntityCandidate>();
            AddMobilePartyCandidate(
                candidates,
                mainParty,
                mainParty,
                minimum,
                maximum,
                CoopCampaignMapPrototypeEntityKind.MainParty,
                isVisible: true);

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

            if (partyVisualManager != null)
            {
                foreach (MobileParty party in MobileParty.All)
                {
                    if (party == null ||
                        party == mainParty ||
                        party.IsGarrison ||
                        party.IsMilitia)
                    {
                        continue;
                    }

                    bool visible = false;
                    try
                    {
                        MapEntityVisual<PartyBase> visual =
                            partyVisualManager.GetVisualOfEntity(party.Party);
                        visible = visual?.IsVisibleOrFadingOut() == true;
                    }
                    catch
                    {
                    }
                    if (!visible)
                        continue;

                    AddMobilePartyCandidate(
                        candidates,
                        party,
                        mainParty,
                        minimum,
                        maximum,
                        CoopCampaignMapPrototypeEntityKind.MobileParty,
                        isVisible: true);
                }
            }

            if (settlementVisualManager != null)
            {
                foreach (Settlement settlement in Settlement.All)
                {
                    if (settlement == null)
                        continue;

                    bool visible = false;
                    try
                    {
                        MapEntityVisual<PartyBase> visual =
                            settlementVisualManager.GetVisualOfEntity(
                                settlement.Party);
                        visible = visual?.IsVisibleOrFadingOut() == true;
                    }
                    catch
                    {
                    }
                    if (!visible)
                        continue;

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
                }
            }

            return candidates
                .OrderBy(candidate => candidate.Entity.Kind ==
                                      CoopCampaignMapPrototypeEntityKind.MainParty
                    ? 0
                    : candidate.Entity.Kind ==
                      CoopCampaignMapPrototypeEntityKind.MobileParty
                        ? 1
                        : 2)
                .ThenBy(candidate => candidate.DistanceSquared)
                .ThenBy(
                    candidate => candidate.Entity.EntityId,
                    StringComparer.OrdinalIgnoreCase)
                .Take(CoopCampaignMapPrototypeContract.MaxVisibleEntities)
                .Select(candidate => candidate.Entity.Clone())
                .ToList();
        }

        private void AddMobilePartyCandidate(
            ICollection<VisibleEntityCandidate> candidates,
            MobileParty party,
            MobileParty mainParty,
            Vec2 minimum,
            Vec2 maximum,
            CoopCampaignMapPrototypeEntityKind kind,
            bool isVisible)
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
            if (!TryCreateEntityState(
                    entityId,
                    party.Name?.ToString(),
                    kind,
                    CoopCampaignMapPrototypeSettlementNameplateSize.None,
                    partyPosition,
                    heading,
                    party.Party.NumberOfAllMembers,
                    primaryColor,
                    secondaryColor,
                    bannerCode,
                    minimum,
                    maximum,
                    out CoopCampaignMapPrototypeEntityState entity))
            {
                return;
            }

            candidates.Add(
                new VisibleEntityCandidate(
                    entity,
                    GetDistanceSquared(
                        mainParty.VisualPosition2DWithoutError,
                        partyPosition)));
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
                BannerCode = CoopCampaignMapPrototypeContract.BoundEntityText(
                    bannerCode,
                    CoopCampaignMapPrototypeContract.MaxBannerCodeCharacters,
                    string.Empty)
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
