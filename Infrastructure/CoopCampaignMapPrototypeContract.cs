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
        Caravan = 3,
        SiegeTent = 4
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

    [Flags]
    public enum CoopCampaignMapPrototypeQuestMarkerFlags
    {
        None = 0,
        AvailableIssue = 1,
        ActiveIssue = 2,
        ActiveStoryQuest = 4,
        TrackedIssue = 8,
        TrackedStoryQuest = 16
    }

    [Flags]
    public enum CoopCampaignMapPrototypeSettlementEventFlags
    {
        None = 0,
        Tournament = 1 << 0,
        AvailableIssue = 1 << 1,
        ActiveQuest = 1 << 2,
        ActiveStoryQuest = 1 << 3,
        TrackedIssue = 1 << 4,
        TrackedStoryQuest = 1 << 5,
        Production = 1 << 6
    }

    public static class CoopCampaignMapSettlementEventPolicy
    {
        // Stock nameplate events are registered only while inspected/spotted.
        // UnloadEvents retains only village production; tracked quest widgets
        // are also hidden outside that same host-owned range.
        public static int FilterEvents(int flags, bool inRange) => flags &
            (inRange ? CoopCampaignMapPrototypeContract.MaxSettlementEventFlags :
                (int)CoopCampaignMapPrototypeSettlementEventFlags.Production);

        public static int FilterQuests(int flags, bool inRange) => inRange
            ? flags & CoopCampaignMapPrototypeContract.MaxQuestMarkerFlags : 0;
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
            bool isInsideViewport,
            bool isTracked = false)
        {
            if (!IsFinite(cameraHeight) ||
                !IsFinite(distanceToCamera))
            {
                return false;
            }

            // Host disclosure has already admitted this entity. Native tracked
            // nameplates remain visible; SettlementNameplateWidget places them
            // on the screen edge using the original projection and depth sign.
            if (isTracked)
                return size >= CoopCampaignMapPrototypeSettlementNameplateSize.Small &&
                       size <= CoopCampaignMapPrototypeSettlementNameplateSize.Large;

            if (!isInFront || !isInsideViewport)
                return false;

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

    /// <summary>
    /// Pure bounds and input arbitration for the client-side map-replica
    /// camera. Keeping the scale rules here makes wheel zoom and keyboard
    /// panning deterministic and independently testable.
    /// </summary>
    public static class CoopCampaignMapPrototypeInteractionPolicy
    {
        public const double SelectionRadiusPixels = 96d;
        public const double InformationPanelWidth = 360d;
        public const double InformationPanelHeight = 220d;
        public const double MinimumCameraClearance = 2.5d;
        public const double MinimumPanSpeed = 20d;
        public const double PanHeightMultiplier = 0.8d;
        public const double MaximumPanSpeedSpanMultiplier = 0.25d;
        public const double MinimumZoomStep = 2.5d;
        public const double ZoomHeightMultiplier = 0.18d;
        public const double MaximumZoomStepSpanMultiplier = 0.12d;
        public const double MaximumCameraHeightSpanMultiplier = 2d;

        public static int ResolveZoomIntent(
            bool scrollUpPressed,
            bool scrollDownPressed,
            double deltaMouseScroll)
        {
            if (scrollUpPressed && scrollDownPressed)
                return 0;
            if (scrollUpPressed)
                return 1;
            if (scrollDownPressed)
                return -1;
            if (!IsFinite(deltaMouseScroll))
                return 0;
            if (deltaMouseScroll > 0.01d)
                return 1;
            if (deltaMouseScroll < -0.01d)
                return -1;
            return 0;
        }

        public static double ResolvePanSpeed(
            double horizontalSpan,
            double cameraHeight)
        {
            if (!IsFinite(horizontalSpan) || horizontalSpan <= 0d)
                return MinimumPanSpeed;
            double height = IsFinite(cameraHeight)
                ? Math.Max(0d, cameraHeight)
                : 0d;
            return Clamp(
                height * PanHeightMultiplier,
                MinimumPanSpeed,
                Math.Max(
                    MinimumPanSpeed,
                    horizontalSpan * MaximumPanSpeedSpanMultiplier));
        }

        public static double ResolveZoomStep(
            double horizontalSpan,
            double cameraHeight)
        {
            if (!IsFinite(horizontalSpan) || horizontalSpan <= 0d)
                return MinimumZoomStep;
            double height = IsFinite(cameraHeight)
                ? Math.Max(0d, cameraHeight)
                : 0d;
            return Clamp(
                height * ZoomHeightMultiplier,
                MinimumZoomStep,
                Math.Max(
                    MinimumZoomStep,
                    horizontalSpan * MaximumZoomStepSpanMultiplier));
        }

        public static double ClampCameraAltitude(
            double terrainHeight,
            double candidateAltitude,
            double horizontalSpan)
        {
            if (!IsFinite(terrainHeight))
                terrainHeight = 0d;
            if (!IsFinite(candidateAltitude))
                candidateAltitude = terrainHeight + MinimumCameraClearance;
            double maximumHeight =
                IsFinite(horizontalSpan) && horizontalSpan > 0d
                    ? Math.Max(
                        MinimumCameraClearance,
                        horizontalSpan * MaximumCameraHeightSpanMultiplier)
                    : MinimumCameraClearance;
            return Clamp(
                candidateAltitude,
                terrainHeight + MinimumCameraClearance,
                terrainHeight + maximumHeight);
        }

        public static void ResolveInformationPanelPosition(
            double anchorX,
            double anchorY,
            bool pinned,
            double viewportWidth,
            double viewportHeight,
            double uiScale,
            out double x,
            out double y)
        {
            double scale = IsFinite(uiScale) && uiScale > 0d ? uiScale : 1d;
            double width = IsFinite(viewportWidth) ? Math.Max(0d, viewportWidth) : 0d;
            double height = IsFinite(viewportHeight) ? Math.Max(0d, viewportHeight) : 0d;
            double margin = 20d * scale;
            double maximumX = Math.Max(0d, width - InformationPanelWidth * scale - margin);
            double maximumY = Math.Max(0d, height - InformationPanelHeight * scale - margin);
            double candidateX = pinned
                ? maximumX
                : (IsFinite(anchorX) ? anchorX : 0d) + 24d * scale;
            double candidateY = pinned
                ? 90d * scale
                : (IsFinite(anchorY) ? anchorY : 0d) + 24d * scale;
            x = Clamp(candidateX, Math.Min(margin, maximumX), maximumX);
            y = Clamp(candidateY, Math.Min(margin, maximumY), maximumY);
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
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

    [Flags]
    public enum CoopCampaignMapPartyNameplateFlags
    {
        None = 0,
        ArmyLeader = 1,
        AttachedToArmy = 2,
        InSettlement = 4,
        Disorganized = 8,
        AtSea = 16,
        InRaft = 32
    }

    /// <summary>
    /// Admission for the mobile body (including its fallback marker), not the
    /// logical party/camera anchor or a settlement's separate native banner.
    /// Native MobilePartyVisual.RefreshPartyIcon never builds mobile icon
    /// components in its CurrentSettlement branch.
    /// </summary>
    public static class CoopCampaignMapPartyBodyPolicy
    {
        public static bool ShouldRenderBody(
            CoopCampaignMapPrototypeEntityKind kind, int nameplateFlags)
        {
            return (kind == CoopCampaignMapPrototypeEntityKind.MainParty ||
                    kind == CoopCampaignMapPrototypeEntityKind.MobileParty) &&
                nameplateFlags >= 0 &&
                nameplateFlags <= CoopCampaignMapPrototypeContract.MaxPartyNameplateFlags &&
                (nameplateFlags & (int)CoopCampaignMapPartyNameplateFlags.InSettlement) == 0;
        }
    }

    /// <summary>
    /// Ordinary mobile hover admission, independent of body visibility. Installed
    /// MobilePartyVisualManager skips non-main raft parties, not their visuals.
    /// Army redirection and main-hero prisoner state are separate pending rules.
    /// </summary>
    public static class CoopCampaignMapPartyPickPolicy
    {
        public static bool ShouldAllowPick(CoopCampaignMapPrototypeEntityKind kind, int flags)
        {
            return CoopCampaignMapPartyBodyPolicy.ShouldRenderBody(kind, flags) &&
                (kind == CoopCampaignMapPrototypeEntityKind.MainParty ||
                 (flags & (int)CoopCampaignMapPartyNameplateFlags.InRaft) == 0);
        }
    }

    /// <summary>
    /// Host-side party label palette and precedence verified against the
    /// installed native PartyNameplateVM. Banner/cloth colors are independent.
    /// </summary>
    public static class CoopCampaignMapPartyLabelPolicy
    {
        public static int ResolveFlags(bool armyLeader, bool attachedToArmy,
            bool inSettlement, bool disorganized, bool atSea, bool inRaft)
        {
            return (armyLeader ? 1 : 0) | (attachedToArmy ? 2 : 0) |
                (inSettlement ? 4 : 0) | (disorganized ? 8 : 0) |
                (atSea ? 16 : 0) | (inRaft ? 32 : 0);
        }

        public static bool ShouldShowNameplate(bool mainParty, int flags)
        {
            var state = (CoopCampaignMapPartyNameplateFlags)flags;
            return (state & (CoopCampaignMapPartyNameplateFlags.AttachedToArmy |
                             CoopCampaignMapPartyNameplateFlags.InSettlement)) == 0 &&
                (mainParty || (state & CoopCampaignMapPartyNameplateFlags.InRaft) == 0);
        }

        public static string ResolveName(bool inAttachedArmy, string armyName,
            string leaderName, string partyName)
        {
            return (inAttachedArmy ? armyName : leaderName ?? partyName) ?? string.Empty;
        }

        public static string ResolveCountText(bool hidden, int healthyCount)
        {
            return hidden ? "?" : Math.Max(0, healthyCount).ToString();
        }

        public static string ResolveExtraInfoText(
            bool hidden, int woundedCount, int prisonerCount,
            Func<int, string> woundedFormatter, Func<int, string> prisonerFormatter)
        {
            string wounded = woundedCount <= 0 ? string.Empty
                : hidden ? "?" : woundedFormatter(woundedCount);
            string prisoners = prisonerCount <= 0 ? string.Empty
                : hidden ? "?" : prisonerFormatter(prisonerCount);
            return wounded + prisoners;
        }

        public static int ResolvePublishedCount(bool informationHidden, int count)
        {
            return informationHidden ? 0 : Math.Max(0, Math.Min(
                CoopCampaignMapPrototypeContract.MaximumPartySize, count));
        }

        public static uint ResolveColor(
            bool mainPartyOrAttachedPeer,
            bool atWar,
            bool sameFaction,
            bool allied,
            bool armyLeader)
        {
            if (mainPartyOrAttachedPeer)
                return armyLeader ? 4289593317u : 4287421380u;
            if (atWar)
                return armyLeader ? 4294931829u : 4292232774u;
            if (sameFaction)
                return armyLeader ? 4288804731u : 4285650500u;
            if (allied)
                return armyLeader ? 4279476684u : 4279460044u;
            return armyLeader ? 4294573055u : 4291877096u;
        }
    }

    public sealed class CoopCampaignMapSettlementPartyBadge
    {
        public int Kind { get; set; } // Native sort/type: lord=0, caravan=1, default=2.
        public bool IsBandit { get; set; }
        public string BannerCode { get; set; } = string.Empty;
    }

    /// <summary>Host-local settlement grid admission; no client campaign objects.</summary>
    public static class CoopCampaignMapSettlementPartyBadgePolicy
    {
        public const int MaxBadges = 128;
        public const int MaxEncodedCharacters = MaxBadges * (512 + 5);

        public static bool IsAdmitted(bool inRange, bool isGarrison, bool isMilitia,
            bool isMainParty, bool mainHeroDisguised, bool inArmy, bool armyLeaderIsMainParty)
        {
            return inRange && !isGarrison && !isMilitia &&
                (!isMainParty || !mainHeroDisguised) &&
                (!inArmy || armyLeaderIsMainParty && !mainHeroDisguised);
        }

        public static bool TryEncode(IReadOnlyList<CoopCampaignMapSettlementPartyBadge> badges,
            out string encoded)
        {
            encoded = string.Empty;
            if (badges == null || badges.Count > MaxBadges) return false;
            var result = new System.Text.StringBuilder();
            foreach (var badge in badges)
            {
                if (!IsValid(badge)) return false;
                if (result.Length != 0) result.Append(';');
                result.Append((char)('0' + badge.Kind)).Append(',')
                    .Append(badge.IsBandit ? '1' : '0').Append(',').Append(badge.BannerCode);
            }
            if (result.Length > MaxEncodedCharacters) return false;
            encoded = result.ToString();
            return true;
        }

        public static bool TryDecode(string encoded, out List<CoopCampaignMapSettlementPartyBadge> badges)
        {
            badges = new List<CoopCampaignMapSettlementPartyBadge>();
            if (encoded == null || encoded.Length > MaxEncodedCharacters) return false;
            if (encoded.Length == 0) return true;
            string[] entries = encoded.Split(';');
            if (entries.Length > MaxBadges) return false;
            foreach (string entry in entries)
            {
                if (entry.Length < 4 || entry[1] != ',' || entry[3] != ',' ||
                    entry[0] < '0' || entry[0] > '2' || entry[2] < '0' || entry[2] > '1')
                    return false;
                var badge = new CoopCampaignMapSettlementPartyBadge
                {
                    Kind = entry[0] - '0', IsBandit = entry[2] == '1', BannerCode = entry.Substring(4)
                };
                if (!IsValid(badge)) return false;
                badges.Add(badge);
            }
            return true;
        }

        public static bool IsValidEncoded(string encoded)
        {
            return TryDecode(encoded, out _);
        }

        private static bool IsValid(CoopCampaignMapSettlementPartyBadge badge)
        {
            if (badge == null || badge.Kind < 0 || badge.Kind > 2 || badge.BannerCode == null ||
                badge.BannerCode.Length > CoopCampaignMapPrototypeContract.MaxBannerCodeCharacters)
                return false;
            if (badge.BannerCode.Length == 0) return true; // Native null clan banner.
            if (badge.Kind != 0) return false;
            // Exact serialized Banner data is ten invariant integer fields per layer.
            string[] fields = badge.BannerCode.Split('.');
            if (fields.Length % 10 != 0) return false;
            foreach (string field in fields)
            {
                if (field.Length == 0) return false;
                for (int index = 0; index < field.Length; index++)
                    if ((field[index] < '0' || field[index] > '9') && !(index == 0 && field[index] == '-'))
                        return false;
                if (!int.TryParse(field, System.Globalization.NumberStyles.AllowLeadingSign,
                    System.Globalization.CultureInfo.InvariantCulture, out _)) return false;
            }
            return true;
        }
    }

    public sealed class CoopCampaignMapPrototypeEntityState
    {
        public string HoverDecal { get; set; } = string.Empty;
        public string SettlementPartyBadges { get; set; } = string.Empty;
        public string EntityId { get; set; }

        public string DisplayName { get; set; }

        public string PartyNameplateNameText { get; set; } = string.Empty;

        public CoopCampaignMapPrototypeEntityKind Kind { get; set; }

        public CoopCampaignMapPrototypeSettlementNameplateSize
            SettlementNameplateSize { get; set; }

        public int NormalizedX { get; set; }

        public int NormalizedY { get; set; }

        public int Heading { get; set; }

        public int PartySize { get; set; }

        public bool IsPartyInformationHidden { get; set; }

        public string PartyNameplateCountText { get; set; } = string.Empty;

        public string PartyNameplateExtraInfoText { get; set; } = string.Empty;

        public string PartyMovementSpeedText { get; set; } = string.Empty;

        public int PartyNameplateFlags { get; set; }

        public uint PrimaryColor { get; set; }

        public uint SecondaryColor { get; set; }

        public uint NameplateColor { get; set; } = uint.MaxValue;

        public string BannerCode { get; set; }

        public string VisualCharacterId { get; set; }

        public string CultureId { get; set; }

        public CoopCampaignMapPrototypePartyVisualKind PartyVisualKind { get; set; }
        public string SiegeTentPresentation { get; set; } = string.Empty;

        public CoopCampaignMapPrototypeAgentVisualState HumanVisual { get; set; }

        public CoopCampaignMapPrototypeAgentVisualState MountVisual { get; set; }

        public CoopCampaignMapPrototypeAgentVisualState CaravanMountVisual { get; set; }

        public int QuestMarkerFlags { get; set; }

        public int SettlementEventFlags { get; set; }

        public int SettlementEventOrder { get; set; }

        public int SettlementQuestOrder { get; set; }

        public int SettlementLevelFlags { get; set; }

        public string SettlementWallStates { get; set; } = string.Empty;

        public int SettlementPreparationState { get; set; }

        public int SettlementRaidState { get; set; }

        public int SettlementRelation { get; set; }
        public int SettlementMapEventType { get; set; }

        public bool IsSettlementTracked { get; set; }

        public bool IsSettlementInRange { get; set; }

        public string SettlementProductionIconId { get; set; } = string.Empty;

        public CoopCampaignMapPrototypeEntityState Clone()
        {
            return new CoopCampaignMapPrototypeEntityState
            {
                EntityId = EntityId,
                HoverDecal = HoverDecal,
                DisplayName = DisplayName,
                PartyNameplateNameText = PartyNameplateNameText,
                Kind = Kind,
                SettlementNameplateSize = SettlementNameplateSize,
                NormalizedX = NormalizedX,
                NormalizedY = NormalizedY,
                Heading = Heading,
                PartySize = PartySize,
                IsPartyInformationHidden = IsPartyInformationHidden,
                PartyNameplateCountText = PartyNameplateCountText,
                PartyNameplateExtraInfoText = PartyNameplateExtraInfoText,
                PartyMovementSpeedText = PartyMovementSpeedText,
                PartyNameplateFlags = PartyNameplateFlags,
                PrimaryColor = PrimaryColor,
                SecondaryColor = SecondaryColor,
                NameplateColor = NameplateColor,
                BannerCode = BannerCode,
                VisualCharacterId = VisualCharacterId,
                CultureId = CultureId,
                PartyVisualKind = PartyVisualKind,
                SiegeTentPresentation = SiegeTentPresentation,
                HumanVisual = HumanVisual?.Clone(),
                MountVisual = MountVisual?.Clone(),
                CaravanMountVisual = CaravanMountVisual?.Clone(),
                QuestMarkerFlags = QuestMarkerFlags,
                SettlementEventFlags = SettlementEventFlags,
                SettlementEventOrder = SettlementEventOrder,
                SettlementQuestOrder = SettlementQuestOrder,
                SettlementLevelFlags = SettlementLevelFlags,
                SettlementWallStates = SettlementWallStates,
                SettlementPreparationState = SettlementPreparationState,
                SettlementRaidState = SettlementRaidState,
                SettlementRelation = SettlementRelation,
                SettlementMapEventType = SettlementMapEventType,
                IsSettlementTracked = IsSettlementTracked,
                IsSettlementInRange = IsSettlementInRange,
                SettlementProductionIconId = SettlementProductionIconId,
                SettlementPartyBadges = SettlementPartyBadges
            };
        }
    }

    /// <summary>
    /// One projection for both full-snapshot and incremental map consumers.
    /// Inputs have already passed transport admission; visuals remain detached.
    /// </summary>
    public static class CoopCampaignMapReplicaStateProjection
    {
        // Resolve at consumption time. A queued catalog notification must not
        // restore dynamic state superseded before the next rendered frame.
        public static List<CoopCampaignMapPrototypeEntityState> BuildVisible(
            IEnumerable<CoopCampaignMapPrototypeCatalogEntityState> catalog,
            IReadOnlyDictionary<string, CoopCampaignMapPrototypeDynamicEntityState> dynamicById)
        {
            var result = new List<CoopCampaignMapPrototypeEntityState>();
            if (catalog == null || dynamicById == null)
                return result;
            foreach (CoopCampaignMapPrototypeCatalogEntityState entry in catalog)
            {
                if (entry == null || string.IsNullOrEmpty(entry.EntityId) ||
                    !dynamicById.TryGetValue(entry.EntityId, out var dynamicState))
                    continue;
                CoopCampaignMapPrototypeEntityState merged = Merge(entry, dynamicState);
                if (merged != null)
                    result.Add(merged);
            }
            return result;
        }

        public static CoopCampaignMapPrototypeEntityState Merge(
            CoopCampaignMapPrototypeCatalogEntityState catalog,
            CoopCampaignMapPrototypeDynamicEntityState dynamicState)
        {
            if (catalog == null || dynamicState == null ||
                string.IsNullOrEmpty(catalog.EntityId) || !dynamicState.IsVisible ||
                !CoopCampaignMapSettlementRelationPolicy.IsValid(dynamicState.SettlementRelation) ||
                dynamicState.SettlementMapEventType < 0 || dynamicState.SettlementMapEventType > 5 ||
                (catalog.Kind != CoopCampaignMapPrototypeEntityKind.Settlement && dynamicState.SettlementMapEventType != 0) ||
                (catalog.Kind != CoopCampaignMapPrototypeEntityKind.Settlement && dynamicState.SettlementRelation != 0) ||
                !string.Equals(catalog.EntityId, dynamicState.EntityId,
                    StringComparison.OrdinalIgnoreCase))
                return null;

            return new CoopCampaignMapPrototypeEntityState
            {
                EntityId = catalog.EntityId,
                HoverDecal = catalog.HoverDecal,
                DisplayName = catalog.DisplayName,
                PartyNameplateNameText = catalog.PartyNameplateNameText,
                Kind = catalog.Kind,
                SettlementNameplateSize = catalog.SettlementNameplateSize,
                NormalizedX = dynamicState.NormalizedX,
                NormalizedY = dynamicState.NormalizedY,
                Heading = dynamicState.Heading,
                PartySize = dynamicState.PartySize,
                IsPartyInformationHidden = dynamicState.IsPartyInformationHidden,
                PartyNameplateCountText = dynamicState.PartyNameplateCountText,
                PartyNameplateExtraInfoText = dynamicState.PartyNameplateExtraInfoText,
                PartyMovementSpeedText = dynamicState.PartyMovementSpeedText,
                PartyNameplateFlags = dynamicState.PartyNameplateFlags,
                PrimaryColor = catalog.PrimaryColor,
                SecondaryColor = catalog.SecondaryColor,
                NameplateColor = catalog.NameplateColor,
                BannerCode = catalog.BannerCode,
                VisualCharacterId = catalog.VisualCharacterId,
                CultureId = catalog.CultureId,
                PartyVisualKind = catalog.PartyVisualKind,
                SiegeTentPresentation = catalog.SiegeTentPresentation,
                HumanVisual = catalog.HumanVisual?.Clone(),
                MountVisual = catalog.MountVisual?.Clone(),
                CaravanMountVisual = catalog.CaravanMountVisual?.Clone(),
                QuestMarkerFlags = dynamicState.QuestMarkerFlags,
                SettlementEventFlags = dynamicState.SettlementEventFlags,
                SettlementEventOrder = dynamicState.SettlementEventOrder,
                SettlementQuestOrder = dynamicState.SettlementQuestOrder,
                SettlementLevelFlags = catalog.SettlementLevelFlags,
                SettlementWallStates = catalog.SettlementWallStates,
                SettlementPreparationState = catalog.SettlementPreparationState,
                SettlementRaidState = catalog.SettlementRaidState,
                SettlementRelation = dynamicState.SettlementRelation,
                SettlementMapEventType = dynamicState.SettlementMapEventType,
                IsSettlementTracked = dynamicState.IsSettlementTracked,
                IsSettlementInRange = dynamicState.IsSettlementInRange,
                SettlementProductionIconId = catalog.SettlementProductionIconId,
                SettlementPartyBadges = dynamicState.IsSettlementInRange
                    ? catalog.SettlementPartyBadges : string.Empty
            };
        }
    }

    /// <summary>
    /// Slowly changing identity, information and exact visual data for one
    /// replicated campaign-map object.
    /// </summary>
    public sealed class CoopCampaignMapPrototypeCatalogEntityState
    {
        public string HoverDecal { get; set; } = string.Empty;
        public string SettlementPartyBadges { get; set; } = string.Empty;
        public string EntityId { get; set; }

        public string DisplayName { get; set; }

        public string PartyNameplateNameText { get; set; } = string.Empty;

        public CoopCampaignMapPrototypeEntityKind Kind { get; set; }

        public CoopCampaignMapPrototypeSettlementNameplateSize
            SettlementNameplateSize { get; set; }

        public CoopCampaignMapPrototypeSettlementKind SettlementKind { get; set; }

        public int SettlementLevelFlags { get; set; }

        public string SettlementWallStates { get; set; } = string.Empty;

        public int SettlementPreparationState { get; set; }

        public int SettlementRaidState { get; set; }

        public uint PrimaryColor { get; set; }

        public uint SecondaryColor { get; set; }

        public uint NameplateColor { get; set; } = uint.MaxValue;

        public string BannerCode { get; set; }

        public string VisualCharacterId { get; set; }

        public string CultureId { get; set; }

        public CoopCampaignMapPrototypePartyVisualKind PartyVisualKind { get; set; }
        public string SiegeTentPresentation { get; set; } = string.Empty;

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

        public string SettlementProductionIconId { get; set; } = string.Empty;

        public CoopCampaignMapPrototypeCatalogEntityState Clone()
        {
            return new CoopCampaignMapPrototypeCatalogEntityState
            {
                EntityId = EntityId,
                HoverDecal = HoverDecal,
                DisplayName = DisplayName,
                PartyNameplateNameText = PartyNameplateNameText,
                Kind = Kind,
                SettlementNameplateSize = SettlementNameplateSize,
                SettlementKind = SettlementKind,
                SettlementLevelFlags = SettlementLevelFlags,
                SettlementWallStates = SettlementWallStates,
                SettlementPreparationState = SettlementPreparationState,
                SettlementRaidState = SettlementRaidState,
                PrimaryColor = PrimaryColor,
                SecondaryColor = SecondaryColor,
                NameplateColor = NameplateColor,
                BannerCode = BannerCode,
                VisualCharacterId = VisualCharacterId,
                CultureId = CultureId,
                PartyVisualKind = PartyVisualKind,
                SiegeTentPresentation = SiegeTentPresentation,
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
                SelectionRadius = SelectionRadius,
                SettlementProductionIconId = SettlementProductionIconId,
                SettlementPartyBadges = SettlementPartyBadges
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
                   left.SettlementLevelFlags == right.SettlementLevelFlags &&
                   left.SettlementWallStates == right.SettlementWallStates &&
                   left.SettlementPreparationState == right.SettlementPreparationState &&
                   left.SettlementRaidState == right.SettlementRaidState &&
                   left.SettlementPartyBadges == right.SettlementPartyBadges &&
                   left.HoverDecal == right.HoverDecal &&
                   left.PrimaryColor == right.PrimaryColor &&
                   left.SecondaryColor == right.SecondaryColor &&
                   left.NameplateColor == right.NameplateColor &&
                   string.Equals(left.PartyNameplateNameText, right.PartyNameplateNameText, StringComparison.Ordinal) &&
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
                   left.SiegeTentPresentation == right.SiegeTentPresentation &&
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
                   left.SelectionRadius == right.SelectionRadius &&
                   string.Equals(
                       left.SettlementProductionIconId,
                       right.SettlementProductionIconId,
                       StringComparison.Ordinal);
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

    // Installed SettlementNameplateVM.RefreshRelationStatus precedence. Host-only
    // diplomacy supplies the inputs; clients never infer this from banner colors.
    public static class CoopCampaignMapSettlementRelationPolicy
    {
        public const int Maximum = 3;
        public static bool IsValid(int value) => value >= 0 && value <= Maximum;

        public static int Resolve(bool hasOwnerClan, bool atWar, bool sameFaction, bool allied)
        {
            if (!hasOwnerClan) return 0;
            if (atWar) return 2;
            if (sameFaction) return 1;
            return allied ? 3 : 0;
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

        public bool IsPartyInformationHidden { get; set; }

        public string PartyNameplateCountText { get; set; } = string.Empty;

        public string PartyNameplateExtraInfoText { get; set; } = string.Empty;

        public string PartyMovementSpeedText { get; set; } = string.Empty;

        public int PartyNameplateFlags { get; set; }

        public bool IsVisible { get; set; }

        public bool IsMoving { get; set; }

        public int ArmyPartyCount { get; set; }

        public int ArmyTotalSize { get; set; }

        public int ArmyCohesion { get; set; }

        public int AppearanceRevision { get; set; }

        public int InformationRevision { get; set; }

        public int QuestMarkerFlags { get; set; }

        public int SettlementEventFlags { get; set; }

        public int SettlementEventOrder { get; set; }

        public int SettlementQuestOrder { get; set; }

        public int SettlementRelation { get; set; }
        public int SettlementMapEventType { get; set; }

        public bool IsSettlementTracked { get; set; }

        public bool IsSettlementInRange { get; set; }

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
                   left.IsPartyInformationHidden == right.IsPartyInformationHidden &&
                   string.Equals(left.PartyNameplateCountText, right.PartyNameplateCountText, StringComparison.Ordinal) &&
                   string.Equals(left.PartyNameplateExtraInfoText, right.PartyNameplateExtraInfoText, StringComparison.Ordinal) &&
                   string.Equals(left.PartyMovementSpeedText, right.PartyMovementSpeedText, StringComparison.Ordinal) &&
                   left.PartyNameplateFlags == right.PartyNameplateFlags &&
                   left.IsVisible == right.IsVisible &&
                   left.IsMoving == right.IsMoving &&
                   left.ArmyPartyCount == right.ArmyPartyCount &&
                   left.ArmyTotalSize == right.ArmyTotalSize &&
                   left.ArmyCohesion == right.ArmyCohesion &&
                   left.AppearanceRevision == right.AppearanceRevision &&
                   left.InformationRevision == right.InformationRevision &&
                   left.QuestMarkerFlags == right.QuestMarkerFlags &&
                   left.SettlementEventFlags == right.SettlementEventFlags &&
                   left.SettlementEventOrder == right.SettlementEventOrder &&
                   left.SettlementQuestOrder == right.SettlementQuestOrder &&
                   left.SettlementRelation == right.SettlementRelation &&
                left.SettlementMapEventType == right.SettlementMapEventType &&
                   left.IsSettlementTracked == right.IsSettlementTracked &&
                   left.IsSettlementInRange == right.IsSettlementInRange;
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
                hidden.SettlementRelation = 0;
                hidden.SettlementMapEventType = 0;
                hidden.IsSettlementTracked = false;
                hidden.IsSettlementInRange = false;
                hidden.SettlementEventFlags = 0;
                hidden.QuestMarkerFlags = 0;
                hidden.SettlementEventOrder = 0;
                hidden.SettlementQuestOrder = 0;
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

    public sealed class CoopCampaignMapConversationOptionState
    {
        public string Text { get; set; } = string.Empty;

        public bool IsEnabled { get; set; }

        public bool IsSpecial { get; set; }

        public CoopCampaignMapConversationOptionState Clone()
        {
            return (CoopCampaignMapConversationOptionState)MemberwiseClone();
        }
    }

    public sealed class CoopCampaignMapConversationState
    {
        public int Revision { get; set; }

        public bool IsActive { get; set; }

        public string SpeakerName { get; set; } = string.Empty;

        public string DialogText { get; set; } = string.Empty;

        public string GoldText { get; set; } = string.Empty;

        public string ContinueText { get; set; } = string.Empty;

        public CoopCampaignMapConversationTableauState Tableau { get; set; }

        public CoopCampaignMapConversationHud Hud { get; set; }

        public List<CoopCampaignMapConversationOptionState> Options { get; set; } =
            new List<CoopCampaignMapConversationOptionState>();

        public CoopCampaignMapConversationState Clone()
        {
            var clone = new CoopCampaignMapConversationState
            {
                Revision = Revision,
                IsActive = IsActive,
                SpeakerName = SpeakerName ?? string.Empty,
                DialogText = DialogText ?? string.Empty,
                GoldText = GoldText ?? string.Empty,
                ContinueText = ContinueText ?? string.Empty,
                Tableau = Tableau?.Clone(),
                Hud = Hud?.Clone()
            };
            if (Options != null)
            {
                foreach (CoopCampaignMapConversationOptionState option in Options)
                {
                    if (option != null)
                        clone.Options.Add(option.Clone());
                }
            }
            return clone;
        }
    }

    public sealed class CoopCampaignMapConversationSnapshotAssembler
    {
        private int _activeRevision = -1;
        private int _completedRevision = -1;
        private int _expectedCount;
        private CoopCampaignMapConversationState _header;
        private readonly Dictionary<int, CoopCampaignMapConversationOptionState>
            _optionsByIndex =
                new Dictionary<int, CoopCampaignMapConversationOptionState>();

        public bool TryBegin(
            CoopCampaignMapConversationState header,
            int expectedCount,
            out CoopCampaignMapConversationState completed)
        {
            completed = null;
            if (!CoopCampaignMapPrototypeContract.TryValidateConversationHeader(
                    header,
                    expectedCount,
                    out _) ||
                header.Revision <= _completedRevision)
            {
                return false;
            }

            _activeRevision = header.Revision;
            _expectedCount = expectedCount;
            _header = header.Clone();
            _header.Options.Clear();
            _optionsByIndex.Clear();
            if (_expectedCount != 0)
                return true;

            completed = Complete();
            return completed != null;
        }

        public bool TryAdd(
            int revision,
            int index,
            int expectedCount,
            CoopCampaignMapConversationOptionState option,
            out CoopCampaignMapConversationState completed)
        {
            completed = null;
            if (_header == null ||
                revision != _activeRevision ||
                expectedCount != _expectedCount ||
                index < 0 ||
                index >= _expectedCount ||
                _optionsByIndex.ContainsKey(index) ||
                !CoopCampaignMapPrototypeContract.IsValidConversationOption(option))
            {
                return false;
            }

            _optionsByIndex[index] = option.Clone();
            if (_optionsByIndex.Count != _expectedCount)
                return true;

            completed = Complete();
            return completed != null;
        }

        public void Reset()
        {
            _activeRevision = -1;
            _completedRevision = -1;
            _expectedCount = 0;
            _header = null;
            _optionsByIndex.Clear();
        }

        private CoopCampaignMapConversationState Complete()
        {
            if (_header == null)
                return null;

            CoopCampaignMapConversationState completed = _header.Clone();
            completed.Options.Clear();
            for (int index = 0; index < _expectedCount; index++)
            {
                if (!_optionsByIndex.TryGetValue(
                        index,
                        out CoopCampaignMapConversationOptionState option))
                {
                    return null;
                }
                completed.Options.Add(option.Clone());
            }
            if (!CoopCampaignMapPrototypeContract.TryValidateConversationState(
                    completed,
                    out _))
            {
                return null;
            }

            _completedRevision = completed.Revision;
            _activeRevision = -1;
            _expectedCount = 0;
            _header = null;
            _optionsByIndex.Clear();
            return completed;
        }
    }

    public sealed class CoopCampaignMapPrototypeState
    {
        public string SessionId { get; set; } = string.Empty;
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

        public string MovementTarget { get; set; } = string.Empty;

        public CoopCampaignMapPrototypeState Clone()
        {
            return new CoopCampaignMapPrototypeState
            {
                SessionId = SessionId,
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
                Camera = Camera?.Clone(),
                MovementTarget = MovementTarget
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

        public CoopCampaignMapConversationState Conversation { get; set; } =
            CoopCampaignMapPrototypeContract.CreateInactiveConversation();

        public CoopCampaignMapConversationPose ConversationPose { get; set; }
        public CoopCampaignMapPartyPoseEnvelope[] PartyPoses { get; set; } = Array.Empty<CoopCampaignMapPartyPoseEnvelope>();

        public CoopCampaignMapMenuPresentation Menu { get; set; }
        public CoopCampaignMapBarPresentation MapBar { get; set; }

        public CoopCampaignMapBattleMarker[] BattleMarkers { get; set; } = Array.Empty<CoopCampaignMapBattleMarker>();

        public string MovementTarget { get; set; } = string.Empty;

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
                Conversation = Conversation?.Clone(),
                ConversationPose = ConversationPose?.Clone(),
                PartyPoses = CoopCampaignMapPartyPoseBatchCodec.Clone(PartyPoses),
                Menu = Menu?.Clone(),
                MapBar = MapBar?.Clone(),
                BattleMarkers = BattleMarkers == null ? null : (CoopCampaignMapBattleMarker[])BattleMarkers.Clone(),
                MovementTarget = MovementTarget,
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
        public const int ProtocolVersion = 71;
        public const int HostBridgeSchemaVersion = 66;
        public const int ProtocolCompressionMaximum = 127;
        public const int MaxCatalogEntities = 2048;
        public const int MaxCatalogLogicalBytes = 16777216;
        public const int MaxDynamicEntities = 2048;
        // Keep the encoded range stable so an older endpoint can still parse
        // and reject a protocol-11 packet without losing bit alignment.
        public const int DynamicBatchCompressionMaximum = 32;
        // Localized nameplate strings enlarge dynamic payloads. Two entities
        // keep even maximum UTF-8 identity/text sizes below the prior budget.
        public const int MaxDynamicBatchSize = 2;
        public const int MaxPartyNameplateCountCharacters = 10;
        public const int MaxPartyNameplateExtraInfoBytes = 128;
        public const int MaxPartyMovementSpeedBytes = 32;
        public const int MaxPartyNameplateNameCharacters = 256;
        public const int MaxPartyNameplateFlags = 63;
        // Compatibility alias while the old monolithic renderer is fed from
        // the client replica store.
        public const int MaxVisibleEntities = MaxCatalogEntities;
        public const int MaxEntityIdCharacters = 96;
        public const int MaxEntityNameCharacters = 64;
        public const int MaxBannerCodeCharacters = 512;
        public const int MaxVisualCharacterIdCharacters = 96;
        public const int MaxCultureIdCharacters = 64;
        public const int MaxInformationTextCharacters = 96;
        public const int MaxProductionIconIdCharacters = 64;
        public const int MaxQuestMarkerFlags = 31;
        public const int MaxSettlementEventFlags = 127;
        public const int EquipmentSlotCount = 12;
        public const int MaxVisualItemIdCharacters = 128;
        public const int MaxBodyPropertiesCharacters = 2048;
        public const int MaxMountCreationKeyCharacters = 1024;
        public const int MaxConversationOptions = 64;
        public const int MaxConversationSpeakerCharacters = 256;
        public const int MaxConversationDialogCharacters = 4096;
        public const int MaxConversationOptionCharacters = 2048;
        public const int MaxConversationGoldCharacters = 64;
        public const int MaxConversationContinueCharacters = 256;
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

        public static CoopCampaignMapConversationState CreateInactiveConversation(
            int revision = 0)
        {
            return new CoopCampaignMapConversationState
            {
                Revision = Math.Max(0, revision),
                IsActive = false,
                SpeakerName = string.Empty,
                DialogText = string.Empty,
                GoldText = string.Empty,
                ContinueText = string.Empty,
                Options = new List<CoopCampaignMapConversationOptionState>()
            };
        }

        public static string BoundConversationText(
            string value,
            int maximumCharacters)
        {
            string normalized = value ?? string.Empty;
            return normalized.Length <= maximumCharacters
                ? normalized
                : normalized.Substring(0, maximumCharacters);
        }

        public static bool AreConversationContentsEquivalent(
            CoopCampaignMapConversationState left,
            CoopCampaignMapConversationState right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left == null || right == null ||
                left.IsActive != right.IsActive ||
                !string.Equals(left.SpeakerName, right.SpeakerName, StringComparison.Ordinal) ||
                !string.Equals(left.DialogText, right.DialogText, StringComparison.Ordinal) ||
                !string.Equals(left.GoldText, right.GoldText, StringComparison.Ordinal) ||
                !string.Equals(left.ContinueText, right.ContinueText, StringComparison.Ordinal) ||
                !CoopCampaignMapConversationTableauCodec.Equivalent(left.Tableau, right.Tableau) ||
                !CoopConversationHudCodec.Equivalent(left.Hud, right.Hud))
            {
                return false;
            }

            int leftCount = left.Options?.Count ?? 0;
            int rightCount = right.Options?.Count ?? 0;
            if (leftCount != rightCount)
                return false;
            for (int index = 0; index < leftCount; index++)
            {
                CoopCampaignMapConversationOptionState leftOption =
                    left.Options[index];
                CoopCampaignMapConversationOptionState rightOption =
                    right.Options[index];
                if (leftOption == null || rightOption == null ||
                    leftOption.IsEnabled != rightOption.IsEnabled ||
                    leftOption.IsSpecial != rightOption.IsSpecial ||
                    !string.Equals(
                        leftOption.Text,
                        rightOption.Text,
                        StringComparison.Ordinal))
                {
                    return false;
                }
            }
            return true;
        }

        public static bool TryValidateConversationHeader(
            CoopCampaignMapConversationState state,
            int expectedOptionCount,
            out string reason)
        {
            reason = null;
            if (state == null)
                return Fail("conversation-missing", out reason);
            if (state.Revision < 0 ||
                expectedOptionCount < 0 ||
                expectedOptionCount > MaxConversationOptions ||
                !IsSafeConversationText(
                    state.SpeakerName,
                    MaxConversationSpeakerCharacters) ||
                !IsSafeConversationText(
                    state.DialogText,
                    MaxConversationDialogCharacters) ||
                !IsSafeConversationText(
                    state.GoldText,
                    MaxConversationGoldCharacters) ||
                !IsSafeConversationText(
                    state.ContinueText,
                    MaxConversationContinueCharacters) ||
                (state.Tableau != null && !CoopCampaignMapConversationTableauCodec.IsValid(state.Tableau)) ||
                !CoopConversationHudCodec.IsValid(state.Hud))
            {
                return Fail("conversation-header", out reason);
            }
            if (!state.IsActive &&
                (state.Tableau != null || state.Hud != null || expectedOptionCount != 0 ||
                 state.SpeakerName.Length != 0 ||
                 state.DialogText.Length != 0 ||
                 state.GoldText.Length != 0 ||
                 state.ContinueText.Length != 0))
            {
                return Fail("conversation-inactive-payload", out reason);
            }
            return true;
        }

        public static bool TryValidateConversationState(
            CoopCampaignMapConversationState state,
            out string reason)
        {
            int optionCount = state?.Options?.Count ?? -1;
            if (!TryValidateConversationHeader(state, optionCount, out reason))
                return false;
            foreach (CoopCampaignMapConversationOptionState option in state.Options)
            {
                if (!IsValidConversationOption(option))
                    return Fail("conversation-option", out reason);
            }
            return true;
        }

        public static bool IsValidConversationOption(
            CoopCampaignMapConversationOptionState option)
        {
            return option != null &&
                   IsSafeConversationText(
                       option.Text,
                       MaxConversationOptionCharacters);
        }

        private static bool IsSafeConversationText(
            string value,
            int maximumCharacters)
        {
            return value != null && value.Length <= maximumCharacters;
        }

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
                   CoopCampaignMapMovementTargetCodec.TryDecode(candidate.MovementTarget, out _) &&
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

            // Match Vec2.RotationInRadians: native map visuals face +Y at zero.
            return QuantizeHeading(Math.Atan2(-directionX, directionY));
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
                !IsValidCameraState(snapshot.Camera) ||
                !CoopCampaignMapMovementTargetCodec.TryDecode(snapshot.MovementTarget, out _))
            {
                return Fail("payload", out reason);
            }
            if (!TryValidateVisibleEntities(snapshot.VisibleEntities, out reason))
                return false;
            if (!TryValidateConversationState(snapshot.Conversation, out reason))
                return false;
            if (snapshot.Menu != null && (!snapshot.Menu.IsValid() || snapshot.Conversation?.IsActive == true))
                return Fail("menu-presentation", out reason);
            if (snapshot.MapBar != null && (!snapshot.MapBar.IsValid() || snapshot.Conversation?.IsActive == true))
                return Fail("map-bar-presentation", out reason);
            if (!CoopCampaignMapBattleMarkerPolicy.TrySnapshot(snapshot.BattleMarkers, out _) ||
                (snapshot.Conversation?.IsActive == true && snapshot.BattleMarkers.Length != 0))
                return Fail("battle-markers", out reason);
            if (!CoopCampaignMapPartyPoseBatchCodec.IsBound(snapshot.PartyPoses, snapshot.SessionId,
                    snapshot.Revision, snapshot.CatalogRevision, snapshot.Conversation?.IsActive == true))
                return Fail("party-pose-binding", out reason);
            if (snapshot.ConversationPose != null &&
                (!CoopCampaignMapConversationPoseCodec.Matches(snapshot.ConversationPose, snapshot.Conversation) ||
                 snapshot.ConversationPose.Sequence != snapshot.Revision))
                return Fail("conversation-pose-binding", out reason);
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
                SessionId = Guid.TryParse(snapshot.SessionId, out var session) ? session.ToString("N") : string.Empty,
                NormalizedX = snapshot.NormalizedX,
                NormalizedY = snapshot.NormalizedY,
                Heading = snapshot.Heading,
                NormalizedTimeOfDay = snapshot.NormalizedTimeOfDay,
                SeasonTimeFactor = snapshot.SeasonTimeFactor,
                ServerTimeMilliseconds = snapshot.SampleTimeMilliseconds,
                VisibleEntitiesRevision = snapshot.VisibleEntitiesRevision,
                CatalogRevision = snapshot.CatalogRevision,
                DynamicRevision = snapshot.DynamicRevision,
                Camera = snapshot.Camera?.Clone(),
                MovementTarget = snapshot.MovementTarget
            };
        }

        public static bool IsValidVisibleEntity(
            CoopCampaignMapPrototypeEntityState entity)
        {
            if (entity == null || !CoopCampaignMapHoverDecal.IsValidForEntity(entity.HoverDecal, entity.Kind)) return false;
            if (entity == null || !CoopCampaignMapSettlementPartyBadgePolicy.IsValidEncoded(entity.SettlementPartyBadges) ||
                ((!entity.IsSettlementInRange || entity.Kind != CoopCampaignMapPrototypeEntityKind.Settlement) &&
                 entity.SettlementPartyBadges.Length != 0)) return false;
            if (entity == null || !CoopCampaignMapSettlementPresentationPolicy.IsValid(
                entity.SettlementWallStates, entity.SettlementPreparationState) ||
                (entity.Kind != CoopCampaignMapPrototypeEntityKind.Settlement &&
                 (entity.SettlementWallStates.Length != 0 || entity.SettlementPreparationState != 0))) return false;
            if (!CoopCampaignMapRaidPresentationPolicy.IsValid(entity.SettlementRaidState) ||
                (entity.Kind != CoopCampaignMapPrototypeEntityKind.Settlement && entity.SettlementRaidState != 0)) return false;
            if (entity == null || !CoopCampaignMapSettlementLevelPolicy.IsValid(entity.SettlementLevelFlags) ||
                (entity.Kind != CoopCampaignMapPrototypeEntityKind.Settlement && entity.SettlementLevelFlags != 0))
                return false;
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
                !IsSafeBoundedText(
                    entity.SettlementProductionIconId,
                    MaxProductionIconIdCharacters,
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
                entity.PartySize > MaximumPartySize ||
                (entity.IsPartyInformationHidden && entity.PartySize != 0) ||
                !IsValidPartyNameplateText(entity.IsPartyInformationHidden,
                    entity.PartyNameplateCountText, entity.PartyNameplateExtraInfoText) ||
                !IsSafeBoundedText(entity.PartyNameplateNameText, MaxPartyNameplateNameCharacters, true) ||
                !IsValidPartyPresentation(entity.PartyNameplateFlags, entity.PartyMovementSpeedText) ||
                entity.QuestMarkerFlags < 0 ||
                entity.QuestMarkerFlags > MaxQuestMarkerFlags ||
                entity.SettlementEventFlags < 0 ||
                entity.SettlementEventFlags > MaxSettlementEventFlags ||
                !CoopCampaignMapSettlementRelationPolicy.IsValid(entity.SettlementRelation) ||
                entity.SettlementMapEventType < 0 || entity.SettlementMapEventType > 5)
            {
                return false;
            }

            bool isSettlement =
                entity.Kind == CoopCampaignMapPrototypeEntityKind.Settlement;
            if (!IsValidSettlementMarkerOrder(isSettlement,
                entity.SettlementEventOrder, entity.SettlementQuestOrder,
                entity.SettlementEventFlags, entity.QuestMarkerFlags)) return false;
            if (isSettlement && (entity.IsPartyInformationHidden ||
                !string.IsNullOrEmpty(entity.PartyNameplateCountText) ||
                !string.IsNullOrEmpty(entity.PartyNameplateExtraInfoText) ||
                !string.IsNullOrEmpty(entity.PartyNameplateNameText) ||
                !string.IsNullOrEmpty(entity.PartyMovementSpeedText) || entity.PartyNameplateFlags != 0))
                return false;
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

            bool hasProductionEvent =
                (entity.SettlementEventFlags &
                 (int)CoopCampaignMapPrototypeSettlementEventFlags.Production) != 0;
            if ((!isSettlement &&
                 (entity.SettlementEventFlags != 0 || entity.SettlementRelation != 0 || entity.SettlementMapEventType != 0 ||
                  entity.IsSettlementTracked || entity.IsSettlementInRange ||
                  !string.IsNullOrEmpty(entity.SettlementProductionIconId))) ||
                hasProductionEvent !=
                    !string.IsNullOrEmpty(entity.SettlementProductionIconId))
            {
                return false;
            }

            if (!isSettlement &&
                entity.PartyVisualKind !=
                    CoopCampaignMapPrototypePartyVisualKind.None &&
                entity.PartyVisualKind != CoopCampaignMapPrototypePartyVisualKind.SiegeTent &&
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

            if (!CoopCampaignMapSiegeTentCodec.IsValidForEntity(entity.SiegeTentPresentation,
                    entity.Kind, entity.PartyVisualKind, entity.HumanVisual != null ||
                    entity.MountVisual != null || entity.CaravanMountVisual != null)) return false;

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
            if (entity?.PartyVisualKind == CoopCampaignMapPrototypePartyVisualKind.SiegeTent)
                return CoopCampaignMapSiegeTentCodec.IsValidForEntity(entity.SiegeTentPresentation,
                    entity.Kind, entity.PartyVisualKind, entity.HumanVisual != null ||
                    entity.MountVisual != null || entity.CaravanMountVisual != null);
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
            if (entity == null || !CoopCampaignMapSiegeTentCodec.IsValidForEntity(entity.SiegeTentPresentation,
                entity.Kind, entity.PartyVisualKind, entity.HumanVisual != null ||
                entity.MountVisual != null || entity.CaravanMountVisual != null)) return false;
            if (entity == null || !CoopCampaignMapHoverDecal.IsValidForEntity(entity.HoverDecal, entity.Kind)) return false;
            if (entity == null || !CoopCampaignMapSettlementPartyBadgePolicy.IsValidEncoded(entity.SettlementPartyBadges) ||
                (entity.Kind != CoopCampaignMapPrototypeEntityKind.Settlement && entity.SettlementPartyBadges.Length != 0))
                return false;
            if (entity == null || !CoopCampaignMapSettlementPresentationPolicy.IsValid(
                entity.SettlementWallStates, entity.SettlementPreparationState) ||
                (entity.Kind != CoopCampaignMapPrototypeEntityKind.Settlement &&
                 (entity.SettlementWallStates.Length != 0 || entity.SettlementPreparationState != 0))) return false;
            if (!CoopCampaignMapRaidPresentationPolicy.IsValid(entity.SettlementRaidState) ||
                (entity.Kind != CoopCampaignMapPrototypeEntityKind.Settlement && entity.SettlementRaidState != 0)) return false;
            if (entity == null || !CoopCampaignMapSettlementLevelPolicy.IsValid(entity.SettlementLevelFlags) ||
                (entity.Kind != CoopCampaignMapPrototypeEntityKind.Settlement && entity.SettlementLevelFlags != 0))
                return false;
            if (entity == null ||
                !IsSafeBoundedText(entity.PartyNameplateNameText, MaxPartyNameplateNameCharacters, true) ||
                (entity.Kind == CoopCampaignMapPrototypeEntityKind.Settlement &&
                 entity.PartyNameplateNameText.Length != 0))
                return false;
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
                !IsSafeBoundedText(
                    entity.SettlementProductionIconId,
                    MaxProductionIconIdCharacters,
                    true) ||
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
            if ((!settlement ||
                 entity.SettlementKind !=
                     CoopCampaignMapPrototypeSettlementKind.Village) &&
                !string.IsNullOrEmpty(entity.SettlementProductionIconId))
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
                   IsValidPartyNameplateText(entity.IsPartyInformationHidden,
                       entity.PartyNameplateCountText, entity.PartyNameplateExtraInfoText) &&
                   IsValidPartyPresentation(entity.PartyNameplateFlags, entity.PartyMovementSpeedText) &&
                   entity.ArmyPartyCount >= 0 &&
                   entity.ArmyPartyCount <= MaximumPartySize &&
                   entity.ArmyTotalSize >= 0 &&
                   entity.ArmyTotalSize <= MaximumPartySize &&
                   (!entity.IsPartyInformationHidden ||
                    (entity.PartySize == 0 && entity.ArmyPartyCount == 0 &&
                     entity.ArmyTotalSize == 0 && entity.ArmyCohesion == 0)) &&
                   IsUnitInRange(entity.ArmyCohesion) &&
                   entity.AppearanceRevision >= 0 &&
                   entity.InformationRevision >= 0 &&
                   entity.QuestMarkerFlags >= 0 &&
                   entity.QuestMarkerFlags <= MaxQuestMarkerFlags &&
                   entity.SettlementEventFlags >= 0 &&
                   entity.SettlementEventFlags <= MaxSettlementEventFlags &&
                   IsValidSettlementMarkerOrder(
                       entity.EntityId.StartsWith("settlement:", StringComparison.Ordinal),
                       entity.SettlementEventOrder, entity.SettlementQuestOrder,
                       entity.SettlementEventFlags, entity.QuestMarkerFlags) &&
                   CoopCampaignMapSettlementRelationPolicy.IsValid(entity.SettlementRelation) &&
                   entity.SettlementMapEventType >= 0 && entity.SettlementMapEventType <= 5 &&
                   (entity.SettlementMapEventType == 0 || (entity.IsVisible &&
                    entity.EntityId.StartsWith("settlement:", StringComparison.Ordinal))) &&
                   (entity.SettlementRelation == 0 || (entity.IsVisible &&
                    entity.EntityId.StartsWith("settlement:", StringComparison.Ordinal)));
        }

        private static bool IsValidSettlementMarkerOrder(bool isSettlement,
            int eventOrder, int questOrder, int eventFlags, int questFlags)
        {
            return isSettlement
                ? CoopCampaignMapSettlementMarkerOrder.IsValidEvents(eventOrder, eventFlags) &&
                  CoopCampaignMapSettlementMarkerOrder.IsValidQuests(questOrder, questFlags)
                : eventOrder == 0 && questOrder == 0 && eventFlags == 0;
        }

        public static bool IsValidPartyPresentation(int flags, string speed)
        {
            return flags >= 0 && flags <= MaxPartyNameplateFlags &&
                IsSafeBoundedText(speed, MaxPartyMovementSpeedBytes, true) &&
                System.Text.Encoding.UTF8.GetByteCount(speed) <= MaxPartyMovementSpeedBytes;
        }

        public static bool IsValidPartyNameplateText(bool hidden, string count, string extra)
        {
            if (count == null || extra == null ||
                count.Length > MaxPartyNameplateCountCharacters ||
                !IsSafeBoundedText(extra, MaxPartyNameplateExtraInfoBytes, true) ||
                System.Text.Encoding.UTF8.GetByteCount(extra) > MaxPartyNameplateExtraInfoBytes)
                return false;
            if (hidden)
                return (count.Length == 0 || count == "?") &&
                    (extra.Length == 0 || extra == "?" || extra == "??");
            foreach (char digit in count)
                if (digit < '0' || digit > '9')
                    return false;
            return true;
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
