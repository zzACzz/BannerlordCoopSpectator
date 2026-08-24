using System;
using System.Collections.Generic;

namespace CoopSpectator.Infrastructure
{
    public enum CoopCampaignMapPrototypeEntityKind
    {
        MainParty = 0,
        MobileParty = 1,
        Settlement = 2
    }

    public enum CoopCampaignMapPrototypeSettlementNameplateSize
    {
        None = 0,
        Small = 1,
        Medium = 2,
        Large = 3
    }

    public enum CoopCampaignMapPrototypePartyVisualKind
    {
        None = 0,
        Foot = 1,
        Mounted = 2,
        Caravan = 3
    }

    public enum CoopCampaignMapPrototypeSettlementKind
    {
        None = 0,
        Village = 1,
        Castle = 2,
        Town = 3,
        Hideout = 4,
        Special = 5
    }

    /// <summary>
    /// Pure host-disclosure and client camera rules matching the campaign-map
    /// nameplate behavior used by Bannerlord 1.4.8.
    /// </summary>
    public static class CoopCampaignMapPrototypeVisibilityPolicy
    {
        public const double PartyMaximumCameraHeight = 200d;
        public const double PartyMaximumProjectedDepth = 100d;
        public const double SettlementFortificationOnlyCameraHeight = 200d;
        public const double SettlementTownOnlyCameraHeight = 400d;
        public const double SettlementNearDistanceMargin = 100d;

        public static bool ShouldReplicateMobileParty(
            bool isMainParty,
            bool isActive,
            bool isGarrison,
            bool isMilitia,
            bool hasCurrentSettlement,
            bool isSpotted)
        {
            return isMainParty ||
                   isActive &&
                   !isGarrison &&
                   !isMilitia &&
                   !hasCurrentSettlement &&
                   isSpotted;
        }

        public static bool ShouldReplicateSettlement(
            bool isVisible,
            bool isSpottable,
            bool isSpotted)
        {
            return isSpottable ? isSpotted : isVisible;
        }

        public static bool ShouldShowParty(
            double cameraHeight,
            double projectedDepth,
            bool isInsideViewport)
        {
            return IsFinite(cameraHeight) &&
                   IsFinite(projectedDepth) &&
                   isInsideViewport &&
                   cameraHeight < PartyMaximumCameraHeight &&
                   projectedDepth > 0d &&
                   projectedDepth < PartyMaximumProjectedDepth;
        }

        public static bool ShouldShowSettlement(
            CoopCampaignMapPrototypeSettlementNameplateSize size,
            double cameraHeight,
            double distanceToCamera,
            bool isInFront,
            bool isInsideViewport)
        {
            if (!IsFinite(cameraHeight) ||
                !IsFinite(distanceToCamera) ||
                !isInFront ||
                !isInsideViewport)
            {
                return false;
            }

            if (cameraHeight > SettlementTownOnlyCameraHeight)
            {
                return size ==
                       CoopCampaignMapPrototypeSettlementNameplateSize.Large;
            }

            if (cameraHeight > SettlementFortificationOnlyCameraHeight)
            {
                return size ==
                           CoopCampaignMapPrototypeSettlementNameplateSize.Large ||
                       size ==
                           CoopCampaignMapPrototypeSettlementNameplateSize.Medium;
            }

            return distanceToCamera <
                   cameraHeight + SettlementNearDistanceMargin;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    public sealed class CoopCampaignMapPrototypeAgentVisualState
    {
        public string BodyProperties { get; set; }

        public bool IsFemale { get; set; }

        public int Race { get; set; }

        public int SkeletonType { get; set; }

        public int RightWieldedItemIndex { get; set; } = -1;

        public int LeftWieldedItemIndex { get; set; } = -1;

        public string MountCreationKey { get; set; }

        public bool HasBanner { get; set; }

        public bool AddColorRandomness { get; set; }

        public string[] EquipmentItemIds { get; set; } =
            new string[CoopCampaignMapPrototypeContract.EquipmentSlotCount];

        public CoopCampaignMapPrototypeAgentVisualState Clone()
        {
            return new CoopCampaignMapPrototypeAgentVisualState
            {
                BodyProperties = BodyProperties,
                IsFemale = IsFemale,
                Race = Race,
                SkeletonType = SkeletonType,
                RightWieldedItemIndex = RightWieldedItemIndex,
                LeftWieldedItemIndex = LeftWieldedItemIndex,
                MountCreationKey = MountCreationKey,
                HasBanner = HasBanner,
                AddColorRandomness = AddColorRandomness,
                EquipmentItemIds = EquipmentItemIds == null
                    ? null
                    : (string[])EquipmentItemIds.Clone()
            };
        }
    }

    public sealed class CoopCampaignMapPrototypeEntityState
    {
        public string EntityId { get; set; }

        public string DisplayName { get; set; }

        public CoopCampaignMapPrototypeEntityKind Kind { get; set; }

        public CoopCampaignMapPrototypeSettlementNameplateSize
            SettlementNameplateSize { get; set; }

        public int NormalizedX { get; set; }

        public int NormalizedY { get; set; }

        public int Heading { get; set; }

        public int PartySize { get; set; }

        public uint PrimaryColor { get; set; }

        public uint SecondaryColor { get; set; }

        public string BannerCode { get; set; }

        public string VisualCharacterId { get; set; }

        public string CultureId { get; set; }

        public CoopCampaignMapPrototypePartyVisualKind PartyVisualKind { get; set; }

        public CoopCampaignMapPrototypeAgentVisualState HumanVisual { get; set; }

        public CoopCampaignMapPrototypeAgentVisualState MountVisual { get; set; }

        public CoopCampaignMapPrototypeAgentVisualState CaravanMountVisual { get; set; }

        public CoopCampaignMapPrototypeEntityState Clone()
        {
            return new CoopCampaignMapPrototypeEntityState
            {
                EntityId = EntityId,
                DisplayName = DisplayName,
                Kind = Kind,
                SettlementNameplateSize = SettlementNameplateSize,
                NormalizedX = NormalizedX,
                NormalizedY = NormalizedY,
                Heading = Heading,
                PartySize = PartySize,
                PrimaryColor = PrimaryColor,
                SecondaryColor = SecondaryColor,
                BannerCode = BannerCode,
                VisualCharacterId = VisualCharacterId,
                CultureId = CultureId,
                PartyVisualKind = PartyVisualKind,
                HumanVisual = HumanVisual?.Clone(),
                MountVisual = MountVisual?.Clone(),
                CaravanMountVisual = CaravanMountVisual?.Clone()
            };
        }
    }

    /// <summary>
    /// Slowly changing identity, information and exact visual data for one
    /// replicated campaign-map object.
    /// </summary>
    public sealed class CoopCampaignMapPrototypeCatalogEntityState
    {
        public string EntityId { get; set; }

        public string DisplayName { get; set; }

        public CoopCampaignMapPrototypeEntityKind Kind { get; set; }

        public CoopCampaignMapPrototypeSettlementNameplateSize
            SettlementNameplateSize { get; set; }

        public CoopCampaignMapPrototypeSettlementKind SettlementKind { get; set; }

        public uint PrimaryColor { get; set; }

        public uint SecondaryColor { get; set; }

        public string BannerCode { get; set; }

        public string VisualCharacterId { get; set; }

        public string CultureId { get; set; }

        public CoopCampaignMapPrototypePartyVisualKind PartyVisualKind { get; set; }

        public CoopCampaignMapPrototypeAgentVisualState HumanVisual { get; set; }

        public CoopCampaignMapPrototypeAgentVisualState MountVisual { get; set; }

        public CoopCampaignMapPrototypeAgentVisualState CaravanMountVisual { get; set; }

        public string FactionId { get; set; }

        public string FactionName { get; set; }

        public string OwnerName { get; set; }

        public string LeaderName { get; set; }

        public string ArmyId { get; set; }

        public string ArmyName { get; set; }

        public bool IsArmyLeader { get; set; }

        public int SelectionRadius { get; set; }

        public CoopCampaignMapPrototypeCatalogEntityState Clone()
        {
            return new CoopCampaignMapPrototypeCatalogEntityState
            {
                EntityId = EntityId,
                DisplayName = DisplayName,
                Kind = Kind,
                SettlementNameplateSize = SettlementNameplateSize,
                SettlementKind = SettlementKind,
                PrimaryColor = PrimaryColor,
                SecondaryColor = SecondaryColor,
                BannerCode = BannerCode,
                VisualCharacterId = VisualCharacterId,
                CultureId = CultureId,
                PartyVisualKind = PartyVisualKind,
                HumanVisual = HumanVisual?.Clone(),
                MountVisual = MountVisual?.Clone(),
                CaravanMountVisual = CaravanMountVisual?.Clone(),
                FactionId = FactionId,
                FactionName = FactionName,
                OwnerName = OwnerName,
                LeaderName = LeaderName,
                ArmyId = ArmyId,
                ArmyName = ArmyName,
                IsArmyLeader = IsArmyLeader,
                SelectionRadius = SelectionRadius
            };
        }
    }

    /// <summary>
    /// Pure comparison and delta construction for slowly changing catalog
    /// entries. Missing entries are intentionally retained by the stable
    /// session catalog; visibility removals belong to the dynamic state.
    /// </summary>
    public static class CoopCampaignMapPrototypeCatalogDeltaPolicy
    {
        public static bool AreEquivalent(
            CoopCampaignMapPrototypeCatalogEntityState left,
            CoopCampaignMapPrototypeCatalogEntityState right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null)
                return false;

            return string.Equals(
                       left.EntityId,
                       right.EntityId,
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(
                       left.DisplayName,
                       right.DisplayName,
                       StringComparison.Ordinal) &&
                   left.Kind == right.Kind &&
                   left.SettlementNameplateSize ==
                       right.SettlementNameplateSize &&
                   left.SettlementKind == right.SettlementKind &&
                   left.PrimaryColor == right.PrimaryColor &&
                   left.SecondaryColor == right.SecondaryColor &&
                   string.Equals(
                       left.BannerCode,
                       right.BannerCode,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       left.VisualCharacterId,
                       right.VisualCharacterId,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       left.CultureId,
                       right.CultureId,
                       StringComparison.Ordinal) &&
                   left.PartyVisualKind == right.PartyVisualKind &&
                   AgentVisualsAreEquivalent(
                       left.HumanVisual,
                       right.HumanVisual) &&
                   AgentVisualsAreEquivalent(
                       left.MountVisual,
                       right.MountVisual) &&
                   AgentVisualsAreEquivalent(
                       left.CaravanMountVisual,
                       right.CaravanMountVisual) &&
                   string.Equals(
                       left.FactionId,
                       right.FactionId,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       left.FactionName,
                       right.FactionName,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       left.OwnerName,
                       right.OwnerName,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       left.LeaderName,
                       right.LeaderName,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       left.ArmyId,
                       right.ArmyId,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       left.ArmyName,
                       right.ArmyName,
                       StringComparison.Ordinal) &&
                   left.IsArmyLeader == right.IsArmyLeader &&
                   left.SelectionRadius == right.SelectionRadius;
        }

        public static List<CoopCampaignMapPrototypeCatalogEntityState> BuildDelta(
            IEnumerable<CoopCampaignMapPrototypeCatalogEntityState> previous,
            IEnumerable<CoopCampaignMapPrototypeCatalogEntityState> current)
        {
            Dictionary<string, CoopCampaignMapPrototypeCatalogEntityState>
                previousById = ToDictionary(previous);
            Dictionary<string, CoopCampaignMapPrototypeCatalogEntityState>
                currentById = ToDictionary(current);
            var delta = new List<CoopCampaignMapPrototypeCatalogEntityState>();

            foreach (KeyValuePair<
                         string,
                         CoopCampaignMapPrototypeCatalogEntityState> pair in
                     currentById)
            {
                if (!previousById.TryGetValue(
                        pair.Key,
                        out CoopCampaignMapPrototypeCatalogEntityState oldState) ||
                    !AreEquivalent(oldState, pair.Value))
                {
                    delta.Add(pair.Value.Clone());
                }
            }

            delta.Sort(
                (left, right) => string.Compare(
                    left?.EntityId,
                    right?.EntityId,
                    StringComparison.OrdinalIgnoreCase));
            return delta;
        }

        private static bool AgentVisualsAreEquivalent(
            CoopCampaignMapPrototypeAgentVisualState left,
            CoopCampaignMapPrototypeAgentVisualState right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null)
                return false;
            if (!string.Equals(
                    left.BodyProperties,
                    right.BodyProperties,
                    StringComparison.Ordinal) ||
                left.IsFemale != right.IsFemale ||
                left.Race != right.Race ||
                left.SkeletonType != right.SkeletonType ||
                left.RightWieldedItemIndex != right.RightWieldedItemIndex ||
                left.LeftWieldedItemIndex != right.LeftWieldedItemIndex ||
                !string.Equals(
                    left.MountCreationKey,
                    right.MountCreationKey,
                    StringComparison.Ordinal) ||
                left.HasBanner != right.HasBanner ||
                left.AddColorRandomness != right.AddColorRandomness)
            {
                return false;
            }

            string[] leftEquipment = left.EquipmentItemIds;
            string[] rightEquipment = right.EquipmentItemIds;
            if (ReferenceEquals(leftEquipment, rightEquipment))
                return true;
            if (leftEquipment == null ||
                rightEquipment == null ||
                leftEquipment.Length != rightEquipment.Length)
            {
                return false;
            }

            for (int index = 0; index < leftEquipment.Length; index++)
            {
                if (!string.Equals(
                        leftEquipment[index],
                        rightEquipment[index],
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        private static Dictionary<
            string,
            CoopCampaignMapPrototypeCatalogEntityState> ToDictionary(
                IEnumerable<CoopCampaignMapPrototypeCatalogEntityState> entities)
        {
            var result = new Dictionary<
                string,
                CoopCampaignMapPrototypeCatalogEntityState>(
                    StringComparer.OrdinalIgnoreCase);
            if (entities == null)
                return result;

            foreach (CoopCampaignMapPrototypeCatalogEntityState entity in entities)
            {
                if (entity != null && !string.IsNullOrWhiteSpace(entity.EntityId))
                    result[entity.EntityId] = entity.Clone();
            }
            return result;
        }
    }

    /// <summary>
    /// Frequently changing position and lightweight information for one
    /// replicated campaign-map object.
    /// </summary>
    public sealed class CoopCampaignMapPrototypeDynamicEntityState
    {
        public string EntityId { get; set; }

        public int NormalizedX { get; set; }

        public int NormalizedY { get; set; }

        public int Heading { get; set; }

        public int PartySize { get; set; }

        public bool IsVisible { get; set; }

        public bool IsMoving { get; set; }

        public int ArmyPartyCount { get; set; }

        public int ArmyTotalSize { get; set; }

        public int ArmyCohesion { get; set; }

        public int AppearanceRevision { get; set; }

        public int InformationRevision { get; set; }

        public CoopCampaignMapPrototypeDynamicEntityState Clone()
        {
            return (CoopCampaignMapPrototypeDynamicEntityState)MemberwiseClone();
        }
    }

    /// <summary>
    /// Pure comparison and delta construction for the frequently changing
    /// campaign-map replica state.
    /// </summary>
    public static class CoopCampaignMapPrototypeDynamicDeltaPolicy
    {
        public static bool AreEquivalent(
            CoopCampaignMapPrototypeDynamicEntityState left,
            CoopCampaignMapPrototypeDynamicEntityState right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null)
                return false;

            return string.Equals(
                       left.EntityId,
                       right.EntityId,
                       StringComparison.OrdinalIgnoreCase) &&
                   left.NormalizedX == right.NormalizedX &&
                   left.NormalizedY == right.NormalizedY &&
                   left.Heading == right.Heading &&
                   left.PartySize == right.PartySize &&
                   left.IsVisible == right.IsVisible &&
                   left.IsMoving == right.IsMoving &&
                   left.ArmyPartyCount == right.ArmyPartyCount &&
                   left.ArmyTotalSize == right.ArmyTotalSize &&
                   left.ArmyCohesion == right.ArmyCohesion &&
                   left.AppearanceRevision == right.AppearanceRevision &&
                   left.InformationRevision == right.InformationRevision;
        }

        public static List<CoopCampaignMapPrototypeDynamicEntityState> BuildDelta(
            IEnumerable<CoopCampaignMapPrototypeDynamicEntityState> previous,
            IEnumerable<CoopCampaignMapPrototypeDynamicEntityState> current)
        {
            var previousById = ToDictionary(previous);
            var currentById = ToDictionary(current);
            var delta = new List<CoopCampaignMapPrototypeDynamicEntityState>();

            foreach (KeyValuePair<
                         string,
                         CoopCampaignMapPrototypeDynamicEntityState> pair in
                     currentById)
            {
                if (!previousById.TryGetValue(
                        pair.Key,
                        out CoopCampaignMapPrototypeDynamicEntityState oldState) ||
                    !AreEquivalent(oldState, pair.Value))
                {
                    delta.Add(pair.Value.Clone());
                }
            }

            foreach (KeyValuePair<
                         string,
                         CoopCampaignMapPrototypeDynamicEntityState> pair in
                     previousById)
            {
                if (currentById.ContainsKey(pair.Key))
                    continue;

                CoopCampaignMapPrototypeDynamicEntityState hidden =
                    pair.Value.Clone();
                hidden.IsVisible = false;
                hidden.IsMoving = false;
                if (!AreEquivalent(pair.Value, hidden))
                    delta.Add(hidden);
            }

            delta.Sort(
                (left, right) => string.Compare(
                    left?.EntityId,
                    right?.EntityId,
                    StringComparison.OrdinalIgnoreCase));
            return delta;
        }

        private static Dictionary<
            string,
            CoopCampaignMapPrototypeDynamicEntityState> ToDictionary(
                IEnumerable<CoopCampaignMapPrototypeDynamicEntityState> entities)
        {
            var result = new Dictionary<
                string,
                CoopCampaignMapPrototypeDynamicEntityState>(
                    StringComparer.OrdinalIgnoreCase);
            if (entities == null)
                return result;

            foreach (CoopCampaignMapPrototypeDynamicEntityState entity in entities)
            {
                if (entity != null && !string.IsNullOrWhiteSpace(entity.EntityId))
                    result[entity.EntityId] = entity.Clone();
            }
            return result;
        }
    }

    public sealed class CoopCampaignMapPrototypeCatalogSnapshot
    {
        public int SchemaVersion { get; set; }

        public string SessionId { get; set; }

        public int Revision { get; set; }

        public List<CoopCampaignMapPrototypeCatalogEntityState> Entities { get; set; } =
            new List<CoopCampaignMapPrototypeCatalogEntityState>();

        public DateTime UpdatedUtc { get; set; }

        public CoopCampaignMapPrototypeCatalogSnapshot Clone()
        {
            var clone = new CoopCampaignMapPrototypeCatalogSnapshot
            {
                SchemaVersion = SchemaVersion,
                SessionId = SessionId,
                Revision = Revision,
                UpdatedUtc = UpdatedUtc
            };
            if (Entities != null)
            {
                foreach (CoopCampaignMapPrototypeCatalogEntityState entity in Entities)
                {
                    if (entity != null)
                        clone.Entities.Add(entity.Clone());
                }
            }
            return clone;
        }
    }

    public sealed class CoopCampaignMapPrototypeDynamicSnapshot
    {
        public int SchemaVersion { get; set; }

        public string SessionId { get; set; }

        public int Revision { get; set; }

        public List<CoopCampaignMapPrototypeDynamicEntityState> Entities { get; set; } =
            new List<CoopCampaignMapPrototypeDynamicEntityState>();

        public DateTime UpdatedUtc { get; set; }

        public CoopCampaignMapPrototypeDynamicSnapshot Clone()
        {
            var clone = new CoopCampaignMapPrototypeDynamicSnapshot
            {
                SchemaVersion = SchemaVersion,
                SessionId = SessionId,
                Revision = Revision,
                UpdatedUtc = UpdatedUtc
            };
            if (Entities != null)
            {
                foreach (CoopCampaignMapPrototypeDynamicEntityState entity in Entities)
                {
                    if (entity != null)
                        clone.Entities.Add(entity.Clone());
                }
            }
            return clone;
        }
    }

    public sealed class CoopCampaignMapPrototypeCameraState
    {
        public int OriginX { get; set; }

        public int OriginY { get; set; }

        public int OriginZ { get; set; }

        public int DirectionX { get; set; }

        public int DirectionY { get; set; }

        public int DirectionZ { get; set; }

        public int UpX { get; set; }

        public int UpY { get; set; }

        public int UpZ { get; set; }

        public int VerticalFov { get; set; }

        public CoopCampaignMapPrototypeCameraState Clone()
        {
            return new CoopCampaignMapPrototypeCameraState
            {
                OriginX = OriginX,
                OriginY = OriginY,
                OriginZ = OriginZ,
                DirectionX = DirectionX,
                DirectionY = DirectionY,
                DirectionZ = DirectionZ,
                UpX = UpX,
                UpY = UpY,
                UpZ = UpZ,
                VerticalFov = VerticalFov
            };
        }
    }

    public sealed class CoopCampaignMapPrototypeState
    {
        public int Revision { get; set; }

        public int NormalizedX { get; set; }

        public int NormalizedY { get; set; }

        public int Heading { get; set; }

        public int NormalizedTimeOfDay { get; set; }

        public int SeasonTimeFactor { get; set; }

        public int ServerTimeMilliseconds { get; set; }

        public int VisibleEntitiesRevision { get; set; }

        public int CatalogRevision { get; set; }

        public int DynamicRevision { get; set; }

        public CoopCampaignMapPrototypeCameraState Camera { get; set; }

        public CoopCampaignMapPrototypeState Clone()
        {
            return new CoopCampaignMapPrototypeState
            {
                Revision = Revision,
                NormalizedX = NormalizedX,
                NormalizedY = NormalizedY,
                Heading = Heading,
                NormalizedTimeOfDay = NormalizedTimeOfDay,
                SeasonTimeFactor = SeasonTimeFactor,
                ServerTimeMilliseconds = ServerTimeMilliseconds,
                VisibleEntitiesRevision = VisibleEntitiesRevision,
                CatalogRevision = CatalogRevision,
                DynamicRevision = DynamicRevision,
                Camera = Camera?.Clone()
            };
        }
    }

    public sealed class CoopCampaignMapPrototypeHostSnapshot
    {
        public int SchemaVersion { get; set; }

        public string SessionId { get; set; }

        public int Revision { get; set; }

        public int NormalizedX { get; set; }

        public int NormalizedY { get; set; }

        public int Heading { get; set; }

        public int NormalizedTimeOfDay { get; set; }

        public int SeasonTimeFactor { get; set; }

        public int SampleTimeMilliseconds { get; set; }

        public bool IsMoving { get; set; }

        public bool IsActive { get; set; }

        public int VisibleEntitiesRevision { get; set; }

        public int CatalogRevision { get; set; }

        public int DynamicRevision { get; set; }

        public List<CoopCampaignMapPrototypeEntityState> VisibleEntities { get; set; } =
            new List<CoopCampaignMapPrototypeEntityState>();

        public CoopCampaignMapPrototypeCameraState Camera { get; set; }

        public DateTime UpdatedUtc { get; set; }

        public CoopCampaignMapPrototypeHostSnapshot Clone()
        {
            return new CoopCampaignMapPrototypeHostSnapshot
            {
                SchemaVersion = SchemaVersion,
                SessionId = SessionId,
                Revision = Revision,
                NormalizedX = NormalizedX,
                NormalizedY = NormalizedY,
                Heading = Heading,
                NormalizedTimeOfDay = NormalizedTimeOfDay,
                SeasonTimeFactor = SeasonTimeFactor,
                SampleTimeMilliseconds = SampleTimeMilliseconds,
                IsMoving = IsMoving,
                IsActive = IsActive,
                VisibleEntitiesRevision = VisibleEntitiesRevision,
                CatalogRevision = CatalogRevision,
                DynamicRevision = DynamicRevision,
                VisibleEntities = CloneVisibleEntities(VisibleEntities),
                Camera = Camera?.Clone(),
                UpdatedUtc = UpdatedUtc
            };
        }

        private static List<CoopCampaignMapPrototypeEntityState> CloneVisibleEntities(
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
    }

    public sealed class CoopCampaignMapPrototypeEntitySnapshotAssembler
    {
        private readonly Dictionary<int, CoopCampaignMapPrototypeEntityState>
            _entitiesByIndex =
                new Dictionary<int, CoopCampaignMapPrototypeEntityState>();
        private int _activeRevision = -1;
        private int _expectedCount;
        private int _completedRevision = -1;

        public int CompletedRevision => _completedRevision;

        public void Reset()
        {
            _entitiesByIndex.Clear();
            _activeRevision = -1;
            _expectedCount = 0;
            _completedRevision = -1;
        }

        public bool TryBegin(
            int revision,
            int expectedCount,
            out List<CoopCampaignMapPrototypeEntityState> completed)
        {
            completed = null;
            if (revision < 0 ||
                expectedCount < 0 ||
                expectedCount > CoopCampaignMapPrototypeContract.MaxVisibleEntities ||
                revision <= _completedRevision)
            {
                return false;
            }

            if (_activeRevision != revision)
            {
                _entitiesByIndex.Clear();
                _activeRevision = revision;
                _expectedCount = expectedCount;
            }
            else if (_expectedCount != expectedCount)
            {
                return false;
            }

            if (expectedCount != 0)
                return true;

            completed = CompleteActiveSnapshot();
            return true;
        }

        public bool TryAdd(
            int revision,
            int index,
            int expectedCount,
            CoopCampaignMapPrototypeEntityState entity,
            out List<CoopCampaignMapPrototypeEntityState> completed)
        {
            completed = null;
            if (expectedCount <= 0 ||
                expectedCount > CoopCampaignMapPrototypeContract.MaxVisibleEntities ||
                index < 0 ||
                index >= expectedCount ||
                revision < 0 ||
                revision <= _completedRevision ||
                !CoopCampaignMapPrototypeContract.IsValidVisibleEntity(entity))
            {
                return false;
            }

            if (_activeRevision != revision)
            {
                if (!TryBegin(revision, expectedCount, out completed))
                    return false;
            }
            else if (_expectedCount != expectedCount)
            {
                return false;
            }

            if (_entitiesByIndex.ContainsKey(index))
                return false;

            _entitiesByIndex[index] = entity.Clone();
            if (_entitiesByIndex.Count != _expectedCount)
                return true;

            completed = CompleteActiveSnapshot();
            return completed != null;
        }

        private List<CoopCampaignMapPrototypeEntityState> CompleteActiveSnapshot()
        {
            var completed = new List<CoopCampaignMapPrototypeEntityState>(
                _expectedCount);
            for (int index = 0; index < _expectedCount; index++)
            {
                if (!_entitiesByIndex.TryGetValue(
                        index,
                        out CoopCampaignMapPrototypeEntityState entity))
                {
                    return null;
                }
                completed.Add(entity.Clone());
            }

            if (!CoopCampaignMapPrototypeContract.TryValidateVisibleEntities(
                    completed,
                    out _))
            {
                return null;
            }

            _completedRevision = _activeRevision;
            _activeRevision = -1;
            _expectedCount = 0;
            _entitiesByIndex.Clear();
            return completed;
        }
    }

    /// <summary>
    /// Pure protocol and scene-selection rules for the first read-only campaign-map prototype.
    /// Coordinates are normalized so the dedicated server does not need to load Main_map.
    /// </summary>
    public static class CoopCampaignMapPrototypeContract
    {
        public const int ProtocolVersion = 14;
        public const int HostBridgeSchemaVersion = 10;
        public const int MaxCatalogEntities = 2048;
        public const int MaxCatalogLogicalBytes = 16777216;
        public const int MaxDynamicEntities = 2048;
        // Keep the encoded range stable so an older endpoint can still parse
        // and reject a protocol-11 packet without losing bit alignment.
        public const int DynamicBatchCompressionMaximum = 32;
        // A 32-entity packet overflows Bannerlord 1.4.8's native packet writer
        // with the full dynamic payload. Eight leaves a conservative margin.
        public const int MaxDynamicBatchSize = 8;
        // Compatibility alias while the old monolithic renderer is fed from
        // the client replica store.
        public const int MaxVisibleEntities = MaxCatalogEntities;
        public const int MaxEntityIdCharacters = 96;
        public const int MaxEntityNameCharacters = 64;
        public const int MaxBannerCodeCharacters = 512;
        public const int MaxVisualCharacterIdCharacters = 96;
        public const int MaxCultureIdCharacters = 64;
        public const int MaxInformationTextCharacters = 96;
        public const int EquipmentSlotCount = 12;
        public const int MaxVisualItemIdCharacters = 128;
        public const int MaxBodyPropertiesCharacters = 2048;
        public const int MaxMountCreationKeyCharacters = 1024;
        public const int MaximumVisualRace = 255;
        public const int MaximumSkeletonType = 15;
        public const int MaximumPartySize = 100000;
        public const int UnitScale = 1000000;
        public const int WorldCoordinateScale = 1000;
        public const int WorldCoordinateOffset = 2000;
        public const int WorldCoordinateQuantizedMaximum =
            WorldCoordinateOffset * WorldCoordinateScale * 2;
        public const string GameModeId = "CoopCampaignMapPrototype";
        public const string MissionBootstrapScene = "mp_tdm_map_001";
        public const string CampaignMapScene = "Main_map";

        public static CoopCampaignMapPrototypeState CreateSyntheticState(
            double elapsedSeconds,
            int revision)
        {
            double safeElapsed = IsFinite(elapsedSeconds) && elapsedSeconds > 0d
                ? elapsedSeconds
                : 0d;
            double angle = safeElapsed * 0.15d;
            return new CoopCampaignMapPrototypeState
            {
                Revision = Math.Max(0, revision),
                NormalizedX = QuantizeUnit(0.5d + 0.18d * Math.Cos(angle)),
                NormalizedY = QuantizeUnit(0.5d + 0.18d * Math.Sin(angle)),
                Heading = QuantizeHeading(angle + Math.PI / 2d),
                NormalizedTimeOfDay = QuantizeTimeOfDay(12d),
                SeasonTimeFactor = QuantizeUnit(0.5d),
                ServerTimeMilliseconds = QuantizeMilliseconds(safeElapsed),
                VisibleEntitiesRevision = Math.Max(0, revision),
                CatalogRevision = Math.Max(0, revision),
                DynamicRevision = Math.Max(0, revision)
            };
        }

        public static bool CanAccept(
            CoopCampaignMapPrototypeState current,
            CoopCampaignMapPrototypeState candidate)
        {
            return candidate != null &&
                   candidate.Revision >= 0 &&
                   candidate.NormalizedX >= 0 &&
                   candidate.NormalizedX <= UnitScale &&
                   candidate.NormalizedY >= 0 &&
                   candidate.NormalizedY <= UnitScale &&
                   candidate.Heading >= 0 &&
                   candidate.Heading <= UnitScale &&
                   candidate.NormalizedTimeOfDay >= 0 &&
                   candidate.NormalizedTimeOfDay <= UnitScale &&
                   candidate.SeasonTimeFactor >= 0 &&
                   candidate.SeasonTimeFactor <= UnitScale &&
                   candidate.ServerTimeMilliseconds >= 0 &&
                   candidate.VisibleEntitiesRevision >= 0 &&
                   candidate.CatalogRevision >= 0 &&
                   candidate.DynamicRevision >= 0 &&
                   IsValidCameraState(candidate.Camera) &&
                   (current == null || candidate.Revision > current.Revision);
        }

        public static bool TryNormalizeMapPosition(
            double positionX,
            double positionY,
            double minimumX,
            double minimumY,
            double maximumX,
            double maximumY,
            out int normalizedX,
            out int normalizedY)
        {
            normalizedX = 0;
            normalizedY = 0;
            if (!IsFinite(positionX) ||
                !IsFinite(positionY) ||
                !IsFinite(minimumX) ||
                !IsFinite(minimumY) ||
                !IsFinite(maximumX) ||
                !IsFinite(maximumY) ||
                maximumX - minimumX <= 0.000001d ||
                maximumY - minimumY <= 0.000001d)
            {
                return false;
            }

            normalizedX = QuantizeUnit(
                (positionX - minimumX) / (maximumX - minimumX));
            normalizedY = QuantizeUnit(
                (positionY - minimumY) / (maximumY - minimumY));
            return true;
        }

        public static int QuantizeDirection(
            double directionX,
            double directionY,
            int fallbackHeading)
        {
            double lengthSquared =
                directionX * directionX + directionY * directionY;
            if (!IsFinite(directionX) ||
                !IsFinite(directionY) ||
                !IsFinite(lengthSquared) ||
                lengthSquared <= 0.000000000001d)
            {
                return fallbackHeading < 0
                    ? 0
                    : fallbackHeading > UnitScale
                        ? UnitScale
                        : fallbackHeading;
            }

            return QuantizeHeading(Math.Atan2(directionY, directionX));
        }

        public static bool TryQuantizeCamera(
            double originX,
            double originY,
            double originZ,
            double directionX,
            double directionY,
            double directionZ,
            double upX,
            double upY,
            double upZ,
            double verticalFovRadians,
            out CoopCampaignMapPrototypeCameraState camera)
        {
            camera = null;
            if (!TryQuantizeWorldCoordinate(originX, out int quantizedOriginX) ||
                !TryQuantizeWorldCoordinate(originY, out int quantizedOriginY) ||
                !TryQuantizeWorldCoordinate(originZ, out int quantizedOriginZ) ||
                !IsFinite(directionX) ||
                !IsFinite(directionY) ||
                !IsFinite(directionZ) ||
                !IsFinite(upX) ||
                !IsFinite(upY) ||
                !IsFinite(upZ) ||
                !IsFinite(verticalFovRadians) ||
                verticalFovRadians < 0.05d ||
                verticalFovRadians > Math.PI - 0.05d)
            {
                return false;
            }

            double directionLength = Math.Sqrt(
                directionX * directionX +
                directionY * directionY +
                directionZ * directionZ);
            double upLength = Math.Sqrt(
                upX * upX + upY * upY + upZ * upZ);
            if (directionLength <= 0.000001d || upLength <= 0.000001d)
                return false;

            directionX /= directionLength;
            directionY /= directionLength;
            directionZ /= directionLength;
            upX /= upLength;
            upY /= upLength;
            upZ /= upLength;

            double sideX = directionY * upZ - directionZ * upY;
            double sideY = directionZ * upX - directionX * upZ;
            double sideZ = directionX * upY - directionY * upX;
            double sideLength = Math.Sqrt(
                sideX * sideX + sideY * sideY + sideZ * sideZ);
            if (sideLength <= 0.000001d)
                return false;

            sideX /= sideLength;
            sideY /= sideLength;
            sideZ /= sideLength;
            upX = sideY * directionZ - sideZ * directionY;
            upY = sideZ * directionX - sideX * directionZ;
            upZ = sideX * directionY - sideY * directionX;

            camera = new CoopCampaignMapPrototypeCameraState
            {
                OriginX = quantizedOriginX,
                OriginY = quantizedOriginY,
                OriginZ = quantizedOriginZ,
                DirectionX = QuantizeSignedUnit(directionX),
                DirectionY = QuantizeSignedUnit(directionY),
                DirectionZ = QuantizeSignedUnit(directionZ),
                UpX = QuantizeSignedUnit(upX),
                UpY = QuantizeSignedUnit(upY),
                UpZ = QuantizeSignedUnit(upZ),
                VerticalFov = QuantizeUnit(verticalFovRadians / Math.PI)
            };
            return IsValidCameraState(camera);
        }

        public static bool IsValidCameraState(
            CoopCampaignMapPrototypeCameraState camera)
        {
            if (camera == null)
                return true;
            if (!IsWorldCoordinateInRange(camera.OriginX) ||
                !IsWorldCoordinateInRange(camera.OriginY) ||
                !IsWorldCoordinateInRange(camera.OriginZ) ||
                !IsUnitInRange(camera.DirectionX) ||
                !IsUnitInRange(camera.DirectionY) ||
                !IsUnitInRange(camera.DirectionZ) ||
                !IsUnitInRange(camera.UpX) ||
                !IsUnitInRange(camera.UpY) ||
                !IsUnitInRange(camera.UpZ) ||
                camera.VerticalFov <= 0 ||
                camera.VerticalFov > UnitScale)
            {
                return false;
            }

            double directionX = DequantizeSignedUnit(camera.DirectionX);
            double directionY = DequantizeSignedUnit(camera.DirectionY);
            double directionZ = DequantizeSignedUnit(camera.DirectionZ);
            double upX = DequantizeSignedUnit(camera.UpX);
            double upY = DequantizeSignedUnit(camera.UpY);
            double upZ = DequantizeSignedUnit(camera.UpZ);
            double directionLengthSquared =
                directionX * directionX +
                directionY * directionY +
                directionZ * directionZ;
            double upLengthSquared =
                upX * upX + upY * upY + upZ * upZ;
            double sideX = directionY * upZ - directionZ * upY;
            double sideY = directionZ * upX - directionX * upZ;
            double sideZ = directionX * upY - directionY * upX;
            double sideLengthSquared =
                sideX * sideX + sideY * sideY + sideZ * sideZ;
            double fov = DequantizeCameraFov(camera.VerticalFov);
            return directionLengthSquared > 0.25d &&
                   upLengthSquared > 0.25d &&
                   sideLengthSquared > 0.01d &&
                   fov >= 0.05d &&
                   fov <= Math.PI - 0.05d;
        }

        public static bool TryValidateHostSnapshot(
            CoopCampaignMapPrototypeHostSnapshot snapshot,
            DateTime utcNow,
            TimeSpan maximumAge,
            out string reason)
        {
            reason = null;
            if (snapshot == null)
                return Fail("missing", out reason);
            if (snapshot.SchemaVersion != HostBridgeSchemaVersion)
                return Fail("schema", out reason);
            if (string.IsNullOrWhiteSpace(snapshot.SessionId) ||
                !Guid.TryParse(snapshot.SessionId, out _))
            {
                return Fail("session", out reason);
            }
            if (!snapshot.IsActive)
                return Fail("inactive", out reason);
            if (snapshot.Revision <= 0)
                return Fail("revision", out reason);
            if (snapshot.NormalizedX < 0 || snapshot.NormalizedX > UnitScale ||
                snapshot.NormalizedY < 0 || snapshot.NormalizedY > UnitScale ||
                snapshot.Heading < 0 || snapshot.Heading > UnitScale ||
                snapshot.NormalizedTimeOfDay < 0 ||
                snapshot.NormalizedTimeOfDay > UnitScale ||
                snapshot.SeasonTimeFactor < 0 ||
                snapshot.SeasonTimeFactor > UnitScale ||
                snapshot.SampleTimeMilliseconds < 0 ||
                snapshot.VisibleEntitiesRevision < 0 ||
                snapshot.CatalogRevision < 0 ||
                snapshot.DynamicRevision < 0 ||
                !IsValidCameraState(snapshot.Camera))
            {
                return Fail("payload", out reason);
            }
            if (!TryValidateVisibleEntities(snapshot.VisibleEntities, out reason))
                return false;
            if (snapshot.UpdatedUtc == DateTime.MinValue)
                return Fail("timestamp", out reason);

            DateTime normalizedNow = utcNow.Kind == DateTimeKind.Utc
                ? utcNow
                : utcNow.ToUniversalTime();
            DateTime normalizedUpdated = snapshot.UpdatedUtc.Kind == DateTimeKind.Utc
                ? snapshot.UpdatedUtc
                : snapshot.UpdatedUtc.ToUniversalTime();
            TimeSpan age = normalizedNow - normalizedUpdated;
            if (age < TimeSpan.FromSeconds(-5d))
                return Fail("future", out reason);
            if (maximumAge > TimeSpan.Zero && age > maximumAge)
                return Fail("stale", out reason);

            return true;
        }

        public static CoopCampaignMapPrototypeState ToNetworkState(
            CoopCampaignMapPrototypeHostSnapshot snapshot,
            int networkRevision)
        {
            if (snapshot == null)
                return null;

            return new CoopCampaignMapPrototypeState
            {
                Revision = Math.Max(0, networkRevision),
                NormalizedX = snapshot.NormalizedX,
                NormalizedY = snapshot.NormalizedY,
                Heading = snapshot.Heading,
                NormalizedTimeOfDay = snapshot.NormalizedTimeOfDay,
                SeasonTimeFactor = snapshot.SeasonTimeFactor,
                ServerTimeMilliseconds = snapshot.SampleTimeMilliseconds,
                VisibleEntitiesRevision = snapshot.VisibleEntitiesRevision,
                CatalogRevision = snapshot.CatalogRevision,
                DynamicRevision = snapshot.DynamicRevision,
                Camera = snapshot.Camera?.Clone()
            };
        }

        public static bool IsValidVisibleEntity(
            CoopCampaignMapPrototypeEntityState entity)
        {
            if (entity == null ||
                !IsSafeBoundedText(
                    entity.EntityId,
                    MaxEntityIdCharacters,
                    allowEmpty: false) ||
                !IsSafeBoundedText(
                    entity.DisplayName,
                    MaxEntityNameCharacters,
                    allowEmpty: false) ||
                !IsSafeBoundedText(
                    entity.BannerCode,
                    MaxBannerCodeCharacters,
                    allowEmpty: true) ||
                !IsSafeBoundedText(
                    entity.VisualCharacterId,
                    MaxVisualCharacterIdCharacters,
                    allowEmpty: true) ||
                !IsSafeBoundedText(
                    entity.CultureId,
                    MaxCultureIdCharacters,
                    allowEmpty: true) ||
                !Enum.IsDefined(typeof(CoopCampaignMapPrototypeEntityKind), entity.Kind) ||
                !Enum.IsDefined(
                    typeof(CoopCampaignMapPrototypeSettlementNameplateSize),
                    entity.SettlementNameplateSize) ||
                !Enum.IsDefined(
                    typeof(CoopCampaignMapPrototypePartyVisualKind),
                    entity.PartyVisualKind) ||
                !IsUnitInRange(entity.NormalizedX) ||
                !IsUnitInRange(entity.NormalizedY) ||
                !IsUnitInRange(entity.Heading) ||
                entity.PartySize < 0 ||
                entity.PartySize > MaximumPartySize)
            {
                return false;
            }

            bool isSettlement =
                entity.Kind == CoopCampaignMapPrototypeEntityKind.Settlement;
            if (isSettlement !=
                (entity.SettlementNameplateSize !=
                 CoopCampaignMapPrototypeSettlementNameplateSize.None))
            {
                return false;
            }

            if (isSettlement &&
                (entity.PartyVisualKind !=
                     CoopCampaignMapPrototypePartyVisualKind.None ||
                 entity.VisualCharacterId.Length != 0 ||
                 entity.CultureId.Length != 0 ||
                 entity.HumanVisual != null ||
                 entity.MountVisual != null ||
                 entity.CaravanMountVisual != null))
            {
                return false;
            }

            if (!isSettlement &&
                entity.PartyVisualKind !=
                    CoopCampaignMapPrototypePartyVisualKind.None &&
                entity.VisualCharacterId.Length == 0 &&
                entity.CultureId.Length == 0)
            {
                return false;
            }

            if (!isSettlement &&
                entity.PartyVisualKind ==
                    CoopCampaignMapPrototypePartyVisualKind.None &&
                (entity.HumanVisual != null ||
                 entity.MountVisual != null ||
                 entity.CaravanMountVisual != null))
            {
                return false;
            }

            if (!IsValidAgentVisualState(
                    entity.HumanVisual,
                    requireBodyProperties: true) ||
                !IsValidAgentVisualState(
                    entity.MountVisual,
                    requireBodyProperties: false) ||
                !IsValidAgentVisualState(
                    entity.CaravanMountVisual,
                    requireBodyProperties: false))
            {
                return false;
            }

            return true;
        }

        public static bool IsValidAgentVisualState(
            CoopCampaignMapPrototypeAgentVisualState visual,
            bool requireBodyProperties)
        {
            if (visual == null)
                return true;
            if (!IsSafeBoundedText(
                    visual.BodyProperties,
                    MaxBodyPropertiesCharacters,
                    allowEmpty: !requireBodyProperties) ||
                !IsSafeBoundedText(
                    visual.MountCreationKey,
                    MaxMountCreationKeyCharacters,
                    allowEmpty: true) ||
                visual.Race < 0 ||
                visual.Race > MaximumVisualRace ||
                visual.SkeletonType < 0 ||
                visual.SkeletonType > MaximumSkeletonType ||
                visual.RightWieldedItemIndex < -1 ||
                visual.RightWieldedItemIndex >= EquipmentSlotCount ||
                visual.LeftWieldedItemIndex < -1 ||
                visual.LeftWieldedItemIndex >= EquipmentSlotCount ||
                visual.EquipmentItemIds == null ||
                visual.EquipmentItemIds.Length != EquipmentSlotCount)
            {
                return false;
            }

            foreach (string itemId in visual.EquipmentItemIds)
            {
                if (!IsSafeBoundedText(
                        itemId,
                        MaxVisualItemIdCharacters,
                        allowEmpty: true))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool IsCompletePartyVisualState(
            CoopCampaignMapPrototypeCatalogEntityState entity)
        {
            if (entity == null ||
                entity.Kind == CoopCampaignMapPrototypeEntityKind.Settlement ||
                entity.PartyVisualKind ==
                    CoopCampaignMapPrototypePartyVisualKind.None ||
                entity.HumanVisual == null)
            {
                return false;
            }

            bool requiresMount =
                entity.PartyVisualKind ==
                    CoopCampaignMapPrototypePartyVisualKind.Mounted ||
                entity.PartyVisualKind ==
                    CoopCampaignMapPrototypePartyVisualKind.Caravan;
            if (!requiresMount)
                return true;

            return entity.PartyVisualKind ==
                       CoopCampaignMapPrototypePartyVisualKind.Caravan
                ? entity.CaravanMountVisual != null || entity.MountVisual != null
                : entity.MountVisual != null;
        }

        public static bool IsValidCatalogEntity(
            CoopCampaignMapPrototypeCatalogEntityState entity)
        {
            if (entity == null ||
                !IsSafeBoundedText(entity.EntityId, MaxEntityIdCharacters, false) ||
                !IsSafeBoundedText(entity.DisplayName, MaxEntityNameCharacters, false) ||
                !IsSafeBoundedText(entity.BannerCode, MaxBannerCodeCharacters, true) ||
                !IsSafeBoundedText(entity.VisualCharacterId, MaxVisualCharacterIdCharacters, true) ||
                !IsSafeBoundedText(entity.CultureId, MaxCultureIdCharacters, true) ||
                !IsInformationText(entity.FactionId) ||
                !IsInformationText(entity.FactionName) ||
                !IsInformationText(entity.OwnerName) ||
                !IsInformationText(entity.LeaderName) ||
                !IsInformationText(entity.ArmyId) ||
                !IsInformationText(entity.ArmyName) ||
                !Enum.IsDefined(typeof(CoopCampaignMapPrototypeEntityKind), entity.Kind) ||
                !Enum.IsDefined(typeof(CoopCampaignMapPrototypeSettlementNameplateSize), entity.SettlementNameplateSize) ||
                !Enum.IsDefined(typeof(CoopCampaignMapPrototypeSettlementKind), entity.SettlementKind) ||
                !Enum.IsDefined(typeof(CoopCampaignMapPrototypePartyVisualKind), entity.PartyVisualKind) ||
                entity.SelectionRadius < 0 ||
                entity.SelectionRadius > UnitScale ||
                !IsValidAgentVisualState(entity.HumanVisual, true) ||
                !IsValidAgentVisualState(entity.MountVisual, false) ||
                !IsValidAgentVisualState(entity.CaravanMountVisual, false))
            {
                return false;
            }

            bool settlement = entity.Kind == CoopCampaignMapPrototypeEntityKind.Settlement;
            if (settlement != (entity.SettlementKind != CoopCampaignMapPrototypeSettlementKind.None) ||
                settlement != (entity.SettlementNameplateSize != CoopCampaignMapPrototypeSettlementNameplateSize.None))
            {
                return false;
            }
            return !settlement ||
                   (entity.PartyVisualKind == CoopCampaignMapPrototypePartyVisualKind.None &&
                    entity.HumanVisual == null &&
                    entity.MountVisual == null &&
                    entity.CaravanMountVisual == null);
        }

        public static bool IsValidDynamicEntity(
            CoopCampaignMapPrototypeDynamicEntityState entity)
        {
            return entity != null &&
                   IsSafeBoundedText(entity.EntityId, MaxEntityIdCharacters, false) &&
                   IsUnitInRange(entity.NormalizedX) &&
                   IsUnitInRange(entity.NormalizedY) &&
                   IsUnitInRange(entity.Heading) &&
                   entity.PartySize >= 0 &&
                   entity.PartySize <= MaximumPartySize &&
                   entity.ArmyPartyCount >= 0 &&
                   entity.ArmyPartyCount <= MaximumPartySize &&
                   entity.ArmyTotalSize >= 0 &&
                   entity.ArmyTotalSize <= MaximumPartySize &&
                   IsUnitInRange(entity.ArmyCohesion) &&
                   entity.AppearanceRevision >= 0 &&
                   entity.InformationRevision >= 0;
        }

        public static bool TryValidateCatalogSnapshot(
            CoopCampaignMapPrototypeCatalogSnapshot snapshot,
            out string reason)
        {
            reason = null;
            if (snapshot == null ||
                snapshot.SchemaVersion != HostBridgeSchemaVersion ||
                string.IsNullOrWhiteSpace(snapshot.SessionId) ||
                !Guid.TryParse(snapshot.SessionId, out _) ||
                snapshot.Revision < 0 ||
                snapshot.Entities == null ||
                snapshot.Entities.Count > MaxCatalogEntities)
            {
                return Fail("catalog-header", out reason);
            }

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CoopCampaignMapPrototypeCatalogEntityState entity in snapshot.Entities)
            {
                if (!IsValidCatalogEntity(entity) || !ids.Add(entity.EntityId))
                    return Fail("catalog-entity", out reason);
            }
            return true;
        }

        public static bool TryValidateDynamicSnapshot(
            CoopCampaignMapPrototypeDynamicSnapshot snapshot,
            out string reason)
        {
            reason = null;
            if (snapshot == null ||
                snapshot.SchemaVersion != HostBridgeSchemaVersion ||
                string.IsNullOrWhiteSpace(snapshot.SessionId) ||
                !Guid.TryParse(snapshot.SessionId, out _) ||
                snapshot.Revision < 0 ||
                snapshot.Entities == null ||
                snapshot.Entities.Count > MaxDynamicEntities)
            {
                return Fail("dynamic-header", out reason);
            }

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CoopCampaignMapPrototypeDynamicEntityState entity in snapshot.Entities)
            {
                if (!IsValidDynamicEntity(entity) || !ids.Add(entity.EntityId))
                    return Fail("dynamic-entity", out reason);
            }
            return true;
        }

        public static bool TryValidateVisibleEntities(
            IReadOnlyList<CoopCampaignMapPrototypeEntityState> entities,
            out string reason)
        {
            reason = null;
            if (entities == null)
                return Fail("visible-entities-missing", out reason);
            if (entities.Count > MaxVisibleEntities)
                return Fail("visible-entities-count", out reason);

            var observedIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            int mainPartyCount = 0;
            foreach (CoopCampaignMapPrototypeEntityState entity in entities)
            {
                if (!IsValidVisibleEntity(entity))
                    return Fail("visible-entity-invalid", out reason);
                if (!observedIds.Add(entity.EntityId))
                    return Fail("visible-entity-duplicate", out reason);
                if (entity.Kind == CoopCampaignMapPrototypeEntityKind.MainParty)
                    mainPartyCount++;
            }

            return mainPartyCount <= 1 ||
                   Fail("visible-main-party-duplicate", out reason);
        }

        public static string BoundEntityText(
            string value,
            int maximumCharacters,
            string fallback)
        {
            string normalized = string.IsNullOrWhiteSpace(value)
                ? fallback ?? string.Empty
                : value.Trim();
            if (normalized.Length > maximumCharacters)
                normalized = normalized.Substring(0, maximumCharacters);
            return normalized;
        }

        public static bool ShouldSuppressDedicatedBattleObserver(
            bool hasPrototypeServerBehavior,
            bool hasPrototypeNetworkController)
        {
            return hasPrototypeServerBehavior || hasPrototypeNetworkController;
        }

        public static int QuantizeUnit(double value)
        {
            if (!IsFinite(value))
                return 0;
            double bounded = value < 0d ? 0d : value > 1d ? 1d : value;
            return (int)Math.Round(
                bounded * UnitScale,
                MidpointRounding.AwayFromZero);
        }

        public static double DequantizeUnit(int value)
        {
            int bounded = value < 0 ? 0 : value > UnitScale ? UnitScale : value;
            return bounded / (double)UnitScale;
        }

        public static bool TryQuantizeMapVisualState(
            double currentHourInDay,
            double seasonTimeFactor,
            out int normalizedTimeOfDay,
            out int quantizedSeasonTimeFactor)
        {
            normalizedTimeOfDay = 0;
            quantizedSeasonTimeFactor = 0;
            if (!IsFinite(currentHourInDay) || !IsFinite(seasonTimeFactor))
                return false;

            normalizedTimeOfDay = QuantizeTimeOfDay(currentHourInDay);
            quantizedSeasonTimeFactor = QuantizeUnit(seasonTimeFactor);
            return true;
        }

        public static int QuantizeTimeOfDay(double currentHourInDay)
        {
            if (!IsFinite(currentHourInDay))
                return 0;

            double wrappedHour = currentHourInDay % 24d;
            if (wrappedHour < 0d)
                wrappedHour += 24d;
            return QuantizeUnit(wrappedHour / 24d);
        }

        public static double DequantizeTimeOfDay(int value)
        {
            return DequantizeUnit(value) * 24d;
        }

        public static int QuantizeSignedUnit(double value)
        {
            if (!IsFinite(value))
                return UnitScale / 2;
            double bounded = value < -1d ? -1d : value > 1d ? 1d : value;
            return QuantizeUnit((bounded + 1d) * 0.5d);
        }

        public static double DequantizeSignedUnit(int value)
        {
            return DequantizeUnit(value) * 2d - 1d;
        }

        public static bool TryQuantizeWorldCoordinate(
            double value,
            out int quantized)
        {
            quantized = 0;
            if (!IsFinite(value) ||
                value < -WorldCoordinateOffset ||
                value > WorldCoordinateOffset)
            {
                return false;
            }

            quantized = (int)Math.Round(
                (value + WorldCoordinateOffset) * WorldCoordinateScale,
                MidpointRounding.AwayFromZero);
            return IsWorldCoordinateInRange(quantized);
        }

        public static double DequantizeWorldCoordinate(int value)
        {
            int bounded = value < 0
                ? 0
                : value > WorldCoordinateQuantizedMaximum
                    ? WorldCoordinateQuantizedMaximum
                    : value;
            return bounded / (double)WorldCoordinateScale -
                   WorldCoordinateOffset;
        }

        public static double DequantizeCameraFov(int value)
        {
            return DequantizeUnit(value) * Math.PI;
        }

        public static int QuantizeHeading(double radians)
        {
            if (!IsFinite(radians))
                return 0;
            double fullTurn = Math.PI * 2d;
            double normalized = radians % fullTurn;
            if (normalized < 0d)
                normalized += fullTurn;
            return QuantizeUnit(normalized / fullTurn);
        }

        public static double DequantizeHeading(int value)
        {
            return DequantizeUnit(value) * Math.PI * 2d;
        }

        public static double InterpolateUnit(double from, double to, double amount)
        {
            double boundedAmount = !IsFinite(amount) || amount <= 0d
                ? 0d
                : amount >= 1d
                    ? 1d
                    : amount;
            double result = from + (to - from) * boundedAmount;
            return result < 0d ? 0d : result > 1d ? 1d : result;
        }

        public static string ResolveLastOwningModule(
            IEnumerable<string> activeModuleIds,
            Func<string, IEnumerable<string>> sceneProvider)
        {
            if (activeModuleIds == null || sceneProvider == null)
                return null;

            string owner = null;
            foreach (string moduleId in activeModuleIds)
            {
                if (string.IsNullOrWhiteSpace(moduleId))
                    continue;

                IEnumerable<string> scenes;
                try
                {
                    scenes = sceneProvider(moduleId);
                }
                catch
                {
                    continue;
                }

                if (scenes == null)
                    continue;
                foreach (string scene in scenes)
                {
                    if (string.Equals(
                        scene,
                        CampaignMapScene,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        owner = moduleId;
                        break;
                    }
                }
            }

            return owner;
        }

        private static int QuantizeMilliseconds(double seconds)
        {
            double milliseconds = seconds * 1000d;
            if (!IsFinite(milliseconds) || milliseconds <= 0d)
                return 0;
            return milliseconds >= int.MaxValue
                ? int.MaxValue
                : (int)Math.Round(milliseconds, MidpointRounding.AwayFromZero);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsUnitInRange(int value)
        {
            return value >= 0 && value <= UnitScale;
        }

        private static bool IsWorldCoordinateInRange(int value)
        {
            return value >= 0 && value <= WorldCoordinateQuantizedMaximum;
        }

        private static bool IsSafeBoundedText(
            string value,
            int maximumCharacters,
            bool allowEmpty)
        {
            if (value == null || value.Length > maximumCharacters)
                return false;
            if (!allowEmpty && string.IsNullOrWhiteSpace(value))
                return false;

            foreach (char character in value)
            {
                if (char.IsControl(character))
                    return false;
            }
            return true;
        }

        private static bool IsInformationText(string value)
        {
            return IsSafeBoundedText(
                value ?? string.Empty,
                MaxInformationTextCharacters,
                true);
        }

        private static bool Fail(string reasonValue, out string reason)
        {
            reason = reasonValue;
            return false;
        }
    }
}
