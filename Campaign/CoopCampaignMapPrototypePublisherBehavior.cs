using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Infrastructure;
using Helpers;
using SandBox.View.Map;
using SandBox.View.Map.Managers;
using SandBox.View.Map.Visuals;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.View;

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
        private int _catalogRevision;
        private int _dynamicRevision;
        private int _lastHeading;
        private float _timeUntilNextPublish;
        private float _timeUntilNextVisibleEntityCapture;
        private List<CoopCampaignMapPrototypeEntityState> _cachedVisibleEntities =
            new List<CoopCampaignMapPrototypeEntityState>();
        private CoopCampaignMapPrototypeCatalogSnapshot _cachedCatalog;
        private CoopCampaignMapPrototypeDynamicSnapshot _cachedDynamic;
        private string _catalogFingerprint = string.Empty;
        private bool _catalogDirty;
        private bool _dynamicDirty;
        private readonly Dictionary<string, int> _entityHeadings =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ReplicaInformation> _replicaInformation =
            new Dictionary<string, ReplicaInformation>(
                StringComparer.OrdinalIgnoreCase);
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

            if (_catalogDirty && _cachedCatalog != null)
            {
                if (!CoopCampaignMapPrototypeBridgeFile.TryWriteCatalog(
                        _cachedCatalog,
                        out reason))
                {
                    LogFailureRateLimited("catalog-write:" + (reason ?? "unknown"));
                    return;
                }
                _catalogDirty = false;
            }

            if (_dynamicDirty && _cachedDynamic != null)
            {
                if (!CoopCampaignMapPrototypeBridgeFile.TryWriteDynamic(
                        _cachedDynamic,
                        out reason))
                {
                    LogFailureRateLimited("dynamic-write:" + (reason ?? "unknown"));
                    return;
                }
                _dynamicDirty = false;
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
                    UpdateReplicaSnapshots(_cachedVisibleEntities);
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
                    CatalogRevision = _catalogRevision,
                    DynamicRevision = _dynamicRevision,
                    VisibleEntities = new List<CoopCampaignMapPrototypeEntityState>(),
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
                if (party == null ||
                    party == mainParty ||
                    party.IsGarrison ||
                    party.IsMilitia ||
                    party.CurrentSettlement != null ||
                    !party.IsActive)
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

            foreach (Settlement settlement in Settlement.All)
            {
                if (settlement == null)
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
                _replicaInformation[entity.EntityId] =
                    CaptureSettlementInformation(settlement);
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

        private void UpdateReplicaSnapshots(
            IReadOnlyList<CoopCampaignMapPrototypeEntityState> entities)
        {
            DateTime updatedUtc = DateTime.UtcNow;
            var catalogEntities = new List<
                CoopCampaignMapPrototypeCatalogEntityState>(entities?.Count ?? 0);
            var dynamicEntities = new List<
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
                            Kind = entity.Kind,
                            SettlementNameplateSize = entity.SettlementNameplateSize,
                            SettlementKind = settlementKind,
                            PrimaryColor = entity.PrimaryColor,
                            SecondaryColor = entity.SecondaryColor,
                            BannerCode = entity.BannerCode,
                            VisualCharacterId = entity.VisualCharacterId,
                            CultureId = entity.CultureId,
                            PartyVisualKind = entity.PartyVisualKind,
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
                            IsVisible = true,
                            IsMoving = information?.IsMoving ?? false,
                            ArmyPartyCount = information?.ArmyPartyCount ?? 0,
                            ArmyTotalSize = information?.ArmyTotalSize ?? 0,
                            ArmyCohesion = information?.ArmyCohesion ?? 0,
                            AppearanceRevision = 0,
                            InformationRevision = 0
                        };
                    if (!CoopCampaignMapPrototypeContract.IsValidCatalogEntity(
                            catalogEntity) ||
                        !CoopCampaignMapPrototypeContract.IsValidDynamicEntity(
                            dynamicEntity))
                    {
                        continue;
                    }

                    catalogEntities.Add(catalogEntity);
                    dynamicEntities.Add(dynamicEntity);
                }
            }

            var fingerprintSnapshot = new CoopCampaignMapPrototypeCatalogSnapshot
            {
                SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
                SessionId = _sessionId,
                Revision = 0,
                Entities = catalogEntities,
                UpdatedUtc = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };
            string fingerprint = string.Join(
                "\n",
                CoopCampaignMapPrototypeBridgeCodec.SerializeCatalog(
                    fingerprintSnapshot));
            if (!string.Equals(
                    fingerprint,
                    _catalogFingerprint,
                    StringComparison.Ordinal))
            {
                _catalogFingerprint = fingerprint;
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
            ResolvePartyVisualState(
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
                    party.Party.NumberOfAllMembers,
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

            candidates.Add(
                new VisibleEntityCandidate(
                    entity,
                    GetDistanceSquared(
                        mainParty.VisualPosition2DWithoutError,
                        partyPosition)));
            _replicaInformation[entity.EntityId] =
                CapturePartyInformation(party);
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
            }
            catch
            {
            }
            return information;
        }

        private static ReplicaInformation CaptureSettlementInformation(
            Settlement settlement)
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
