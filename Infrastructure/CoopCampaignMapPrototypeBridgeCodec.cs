using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CoopSpectator.Infrastructure
{
    public static class CoopCampaignMapPrototypeBridgeCodec
    {
        public static string[] Serialize(
            CoopCampaignMapPrototypeHostSnapshot snapshot)
        {
            if (snapshot == null)
                return Array.Empty<string>();

            IReadOnlyList<CoopCampaignMapPrototypeEntityState> visibleEntities =
                snapshot.VisibleEntities ??
                (IReadOnlyList<CoopCampaignMapPrototypeEntityState>)
                    Array.Empty<CoopCampaignMapPrototypeEntityState>();
            int visibleEntityCount = Math.Min(
                visibleEntities.Count,
                CoopCampaignMapPrototypeContract.MaxVisibleEntities);
            var lines = new List<string>
            {
                "SchemaVersion=" + snapshot.SchemaVersion.ToString(CultureInfo.InvariantCulture),
                "SessionId=" + (snapshot.SessionId ?? string.Empty),
                "Revision=" + snapshot.Revision.ToString(CultureInfo.InvariantCulture),
                "NormalizedX=" + snapshot.NormalizedX.ToString(CultureInfo.InvariantCulture),
                "NormalizedY=" + snapshot.NormalizedY.ToString(CultureInfo.InvariantCulture),
                "Heading=" + snapshot.Heading.ToString(CultureInfo.InvariantCulture),
                "NormalizedTimeOfDay=" + snapshot.NormalizedTimeOfDay.ToString(CultureInfo.InvariantCulture),
                "SeasonTimeFactor=" + snapshot.SeasonTimeFactor.ToString(CultureInfo.InvariantCulture),
                "SampleTimeMilliseconds=" + snapshot.SampleTimeMilliseconds.ToString(CultureInfo.InvariantCulture),
                "IsMoving=" + snapshot.IsMoving,
                "IsActive=" + snapshot.IsActive,
                "HasCamera=" + (snapshot.Camera != null),
                "VisibleEntitiesRevision=" + snapshot.VisibleEntitiesRevision.ToString(CultureInfo.InvariantCulture),
                "VisibleEntityCount=" + visibleEntityCount.ToString(CultureInfo.InvariantCulture)
            };

            if (snapshot.Camera != null)
            {
                lines.Add("CameraOriginX=" + snapshot.Camera.OriginX.ToString(CultureInfo.InvariantCulture));
                lines.Add("CameraOriginY=" + snapshot.Camera.OriginY.ToString(CultureInfo.InvariantCulture));
                lines.Add("CameraOriginZ=" + snapshot.Camera.OriginZ.ToString(CultureInfo.InvariantCulture));
                lines.Add("CameraDirectionX=" + snapshot.Camera.DirectionX.ToString(CultureInfo.InvariantCulture));
                lines.Add("CameraDirectionY=" + snapshot.Camera.DirectionY.ToString(CultureInfo.InvariantCulture));
                lines.Add("CameraDirectionZ=" + snapshot.Camera.DirectionZ.ToString(CultureInfo.InvariantCulture));
                lines.Add("CameraUpX=" + snapshot.Camera.UpX.ToString(CultureInfo.InvariantCulture));
                lines.Add("CameraUpY=" + snapshot.Camera.UpY.ToString(CultureInfo.InvariantCulture));
                lines.Add("CameraUpZ=" + snapshot.Camera.UpZ.ToString(CultureInfo.InvariantCulture));
                lines.Add("CameraVerticalFov=" + snapshot.Camera.VerticalFov.ToString(CultureInfo.InvariantCulture));
            }

            for (int index = 0; index < visibleEntityCount; index++)
            {
                CoopCampaignMapPrototypeEntityState entity = visibleEntities[index];
                string prefix = "VisibleEntity." +
                                index.ToString(CultureInfo.InvariantCulture) + ".";
                lines.Add(prefix + "EntityId=" + EncodeText(entity?.EntityId));
                lines.Add(prefix + "DisplayName=" + EncodeText(entity?.DisplayName));
                lines.Add(prefix + "Kind=" + (entity != null ? (int)entity.Kind : 0).ToString(CultureInfo.InvariantCulture));
                lines.Add(prefix + "SettlementNameplateSize=" + (entity != null ? (int)entity.SettlementNameplateSize : 0).ToString(CultureInfo.InvariantCulture));
                lines.Add(prefix + "NormalizedX=" + (entity?.NormalizedX ?? 0).ToString(CultureInfo.InvariantCulture));
                lines.Add(prefix + "NormalizedY=" + (entity?.NormalizedY ?? 0).ToString(CultureInfo.InvariantCulture));
                lines.Add(prefix + "Heading=" + (entity?.Heading ?? 0).ToString(CultureInfo.InvariantCulture));
                lines.Add(prefix + "PartySize=" + (entity?.PartySize ?? 0).ToString(CultureInfo.InvariantCulture));
                lines.Add(prefix + "PrimaryColor=" + (entity?.PrimaryColor ?? 0u).ToString(CultureInfo.InvariantCulture));
                lines.Add(prefix + "SecondaryColor=" + (entity?.SecondaryColor ?? 0u).ToString(CultureInfo.InvariantCulture));
                lines.Add(prefix + "BannerCode=" + EncodeText(entity?.BannerCode));
                lines.Add(prefix + "VisualCharacterId=" + EncodeText(entity?.VisualCharacterId));
                lines.Add(prefix + "CultureId=" + EncodeText(entity?.CultureId));
                lines.Add(prefix + "PartyVisualKind=" + (entity != null ? (int)entity.PartyVisualKind : 0).ToString(CultureInfo.InvariantCulture));
                SerializeAgentVisual(
                    lines,
                    prefix + "HumanVisual.",
                    entity?.HumanVisual);
                SerializeAgentVisual(
                    lines,
                    prefix + "MountVisual.",
                    entity?.MountVisual);
                SerializeAgentVisual(
                    lines,
                    prefix + "CaravanMountVisual.",
                    entity?.CaravanMountVisual);
            }

            lines.Add(
                "UpdatedUtc=" +
                snapshot.UpdatedUtc.ToUniversalTime().ToString(
                    "O",
                    CultureInfo.InvariantCulture));
            return lines.ToArray();
        }

        public static bool TryParse(
            IEnumerable<string> lines,
            out CoopCampaignMapPrototypeHostSnapshot snapshot,
            out string reason)
        {
            snapshot = null;
            reason = null;
            if (lines == null)
                return Fail("missing-lines", out reason);

            var values = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            foreach (string rawLine in lines)
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                    continue;

                int separatorIndex = rawLine.IndexOf('=');
                if (separatorIndex <= 0)
                    continue;

                string key = rawLine.Substring(0, separatorIndex).Trim();
                string value = rawLine.Substring(separatorIndex + 1).Trim();
                if (!string.IsNullOrWhiteSpace(key))
                    values[key] = value;
            }

            if (!TryReadInt(values, "SchemaVersion", out int schemaVersion) ||
                !values.TryGetValue("SessionId", out string sessionId) ||
                !TryReadInt(values, "Revision", out int revision) ||
                !TryReadInt(values, "NormalizedX", out int normalizedX) ||
                !TryReadInt(values, "NormalizedY", out int normalizedY) ||
                !TryReadInt(values, "Heading", out int heading) ||
                !TryReadInt(values, "NormalizedTimeOfDay", out int normalizedTimeOfDay) ||
                !TryReadInt(values, "SeasonTimeFactor", out int seasonTimeFactor) ||
                !TryReadInt(values, "SampleTimeMilliseconds", out int sampleTimeMilliseconds) ||
                !TryReadBool(values, "IsMoving", out bool isMoving) ||
                !TryReadBool(values, "IsActive", out bool isActive) ||
                !TryReadBool(values, "HasCamera", out bool hasCamera) ||
                !TryReadInt(values, "VisibleEntitiesRevision", out int visibleEntitiesRevision) ||
                !TryReadInt(values, "VisibleEntityCount", out int visibleEntityCount) ||
                !TryReadDateTime(values, "UpdatedUtc", out DateTime updatedUtc))
            {
                return Fail("malformed", out reason);
            }

            if (visibleEntityCount < 0 ||
                visibleEntityCount > CoopCampaignMapPrototypeContract.MaxVisibleEntities ||
                visibleEntitiesRevision < 0)
            {
                return Fail("malformed-visible-entity-header", out reason);
            }

            CoopCampaignMapPrototypeCameraState camera = null;
            if (hasCamera)
            {
                if (!TryReadInt(values, "CameraOriginX", out int cameraOriginX) ||
                    !TryReadInt(values, "CameraOriginY", out int cameraOriginY) ||
                    !TryReadInt(values, "CameraOriginZ", out int cameraOriginZ) ||
                    !TryReadInt(values, "CameraDirectionX", out int cameraDirectionX) ||
                    !TryReadInt(values, "CameraDirectionY", out int cameraDirectionY) ||
                    !TryReadInt(values, "CameraDirectionZ", out int cameraDirectionZ) ||
                    !TryReadInt(values, "CameraUpX", out int cameraUpX) ||
                    !TryReadInt(values, "CameraUpY", out int cameraUpY) ||
                    !TryReadInt(values, "CameraUpZ", out int cameraUpZ) ||
                    !TryReadInt(values, "CameraVerticalFov", out int cameraVerticalFov))
                {
                    return Fail("malformed-camera", out reason);
                }

                camera = new CoopCampaignMapPrototypeCameraState
                {
                    OriginX = cameraOriginX,
                    OriginY = cameraOriginY,
                    OriginZ = cameraOriginZ,
                    DirectionX = cameraDirectionX,
                    DirectionY = cameraDirectionY,
                    DirectionZ = cameraDirectionZ,
                    UpX = cameraUpX,
                    UpY = cameraUpY,
                    UpZ = cameraUpZ,
                    VerticalFov = cameraVerticalFov
                };
                if (!CoopCampaignMapPrototypeContract.IsValidCameraState(camera))
                    return Fail("invalid-camera", out reason);
            }

            var visibleEntities =
                new List<CoopCampaignMapPrototypeEntityState>(visibleEntityCount);
            for (int index = 0; index < visibleEntityCount; index++)
            {
                string prefix = "VisibleEntity." +
                                index.ToString(CultureInfo.InvariantCulture) + ".";
                if (!values.TryGetValue(prefix + "EntityId", out string encodedEntityId) ||
                    !values.TryGetValue(prefix + "DisplayName", out string encodedDisplayName) ||
                    !TryDecodeText(encodedEntityId, out string entityId) ||
                    !TryDecodeText(encodedDisplayName, out string displayName) ||
                    !TryReadInt(values, prefix + "Kind", out int kind) ||
                    !TryReadInt(values, prefix + "SettlementNameplateSize", out int settlementNameplateSize) ||
                    !TryReadInt(values, prefix + "NormalizedX", out int entityX) ||
                    !TryReadInt(values, prefix + "NormalizedY", out int entityY) ||
                    !TryReadInt(values, prefix + "Heading", out int entityHeading) ||
                    !TryReadInt(values, prefix + "PartySize", out int partySize) ||
                    !TryReadUInt(values, prefix + "PrimaryColor", out uint primaryColor) ||
                    !TryReadUInt(values, prefix + "SecondaryColor", out uint secondaryColor) ||
                    !values.TryGetValue(prefix + "BannerCode", out string encodedBannerCode) ||
                    !TryDecodeText(encodedBannerCode, out string bannerCode) ||
                    !values.TryGetValue(prefix + "VisualCharacterId", out string encodedVisualCharacterId) ||
                    !TryDecodeText(encodedVisualCharacterId, out string visualCharacterId) ||
                    !values.TryGetValue(prefix + "CultureId", out string encodedCultureId) ||
                    !TryDecodeText(encodedCultureId, out string cultureId) ||
                    !TryReadInt(values, prefix + "PartyVisualKind", out int partyVisualKind) ||
                    !TryParseAgentVisual(
                        values,
                        prefix + "HumanVisual.",
                        out CoopCampaignMapPrototypeAgentVisualState humanVisual) ||
                    !TryParseAgentVisual(
                        values,
                        prefix + "MountVisual.",
                        out CoopCampaignMapPrototypeAgentVisualState mountVisual) ||
                    !TryParseAgentVisual(
                        values,
                        prefix + "CaravanMountVisual.",
                        out CoopCampaignMapPrototypeAgentVisualState caravanMountVisual))
                {
                    return Fail("malformed-visible-entity", out reason);
                }

                var entity = new CoopCampaignMapPrototypeEntityState
                {
                    EntityId = entityId,
                    DisplayName = displayName,
                    Kind = (CoopCampaignMapPrototypeEntityKind)kind,
                    SettlementNameplateSize =
                        (CoopCampaignMapPrototypeSettlementNameplateSize)
                            settlementNameplateSize,
                    NormalizedX = entityX,
                    NormalizedY = entityY,
                    Heading = entityHeading,
                    PartySize = partySize,
                    PrimaryColor = primaryColor,
                    SecondaryColor = secondaryColor,
                    BannerCode = bannerCode,
                    VisualCharacterId = visualCharacterId,
                    CultureId = cultureId,
                    PartyVisualKind =
                        (CoopCampaignMapPrototypePartyVisualKind)partyVisualKind,
                    HumanVisual = humanVisual,
                    MountVisual = mountVisual,
                    CaravanMountVisual = caravanMountVisual
                };
                if (!CoopCampaignMapPrototypeContract.IsValidVisibleEntity(entity))
                    return Fail("invalid-visible-entity", out reason);
                visibleEntities.Add(entity);
            }

            if (!CoopCampaignMapPrototypeContract.TryValidateVisibleEntities(
                    visibleEntities,
                    out reason))
            {
                return false;
            }

            snapshot = new CoopCampaignMapPrototypeHostSnapshot
            {
                SchemaVersion = schemaVersion,
                SessionId = sessionId,
                Revision = revision,
                NormalizedX = normalizedX,
                NormalizedY = normalizedY,
                Heading = heading,
                NormalizedTimeOfDay = normalizedTimeOfDay,
                SeasonTimeFactor = seasonTimeFactor,
                SampleTimeMilliseconds = sampleTimeMilliseconds,
                IsMoving = isMoving,
                IsActive = isActive,
                VisibleEntitiesRevision = visibleEntitiesRevision,
                VisibleEntities = visibleEntities,
                Camera = camera,
                UpdatedUtc = updatedUtc
            };
            return true;
        }

        private static bool TryReadInt(
            IReadOnlyDictionary<string, string> values,
            string key,
            out int value)
        {
            value = 0;
            return values.TryGetValue(key, out string rawValue) &&
                   int.TryParse(
                       rawValue,
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out value);
        }

        private static void SerializeAgentVisual(
            ICollection<string> lines,
            string prefix,
            CoopCampaignMapPrototypeAgentVisualState visual)
        {
            lines.Add(prefix + "Present=" + (visual != null));
            if (visual == null)
                return;

            lines.Add(prefix + "BodyProperties=" + EncodeText(visual.BodyProperties));
            lines.Add(prefix + "IsFemale=" + visual.IsFemale);
            lines.Add(prefix + "Race=" + visual.Race.ToString(CultureInfo.InvariantCulture));
            lines.Add(prefix + "SkeletonType=" + visual.SkeletonType.ToString(CultureInfo.InvariantCulture));
            lines.Add(prefix + "RightWieldedItemIndex=" + visual.RightWieldedItemIndex.ToString(CultureInfo.InvariantCulture));
            lines.Add(prefix + "LeftWieldedItemIndex=" + visual.LeftWieldedItemIndex.ToString(CultureInfo.InvariantCulture));
            lines.Add(prefix + "MountCreationKey=" + EncodeText(visual.MountCreationKey));
            lines.Add(prefix + "HasBanner=" + visual.HasBanner);
            lines.Add(prefix + "AddColorRandomness=" + visual.AddColorRandomness);
            string[] itemIds = visual.EquipmentItemIds ?? Array.Empty<string>();
            for (int slot = 0;
                 slot < CoopCampaignMapPrototypeContract.EquipmentSlotCount;
                 slot++)
            {
                lines.Add(
                    prefix + "EquipmentItem." +
                    slot.ToString(CultureInfo.InvariantCulture) + "=" +
                    EncodeText(slot < itemIds.Length ? itemIds[slot] : string.Empty));
            }
        }

        private static bool TryParseAgentVisual(
            IReadOnlyDictionary<string, string> values,
            string prefix,
            out CoopCampaignMapPrototypeAgentVisualState visual)
        {
            visual = null;
            if (!TryReadBool(values, prefix + "Present", out bool present))
                return false;
            if (!present)
                return true;

            if (!values.TryGetValue(prefix + "BodyProperties", out string encodedBodyProperties) ||
                !TryDecodeText(encodedBodyProperties, out string bodyProperties) ||
                !TryReadBool(values, prefix + "IsFemale", out bool isFemale) ||
                !TryReadInt(values, prefix + "Race", out int race) ||
                !TryReadInt(values, prefix + "SkeletonType", out int skeletonType) ||
                !TryReadInt(values, prefix + "RightWieldedItemIndex", out int rightWieldedItemIndex) ||
                !TryReadInt(values, prefix + "LeftWieldedItemIndex", out int leftWieldedItemIndex) ||
                !values.TryGetValue(prefix + "MountCreationKey", out string encodedMountCreationKey) ||
                !TryDecodeText(encodedMountCreationKey, out string mountCreationKey) ||
                !TryReadBool(values, prefix + "HasBanner", out bool hasBanner) ||
                !TryReadBool(values, prefix + "AddColorRandomness", out bool addColorRandomness))
            {
                return false;
            }

            var itemIds = new string[
                CoopCampaignMapPrototypeContract.EquipmentSlotCount];
            for (int slot = 0; slot < itemIds.Length; slot++)
            {
                string key = prefix + "EquipmentItem." +
                             slot.ToString(CultureInfo.InvariantCulture);
                if (!values.TryGetValue(key, out string encodedItemId) ||
                    !TryDecodeText(encodedItemId, out itemIds[slot]))
                {
                    return false;
                }
            }

            visual = new CoopCampaignMapPrototypeAgentVisualState
            {
                BodyProperties = bodyProperties,
                IsFemale = isFemale,
                Race = race,
                SkeletonType = skeletonType,
                RightWieldedItemIndex = rightWieldedItemIndex,
                LeftWieldedItemIndex = leftWieldedItemIndex,
                MountCreationKey = mountCreationKey,
                HasBanner = hasBanner,
                AddColorRandomness = addColorRandomness,
                EquipmentItemIds = itemIds
            };
            return CoopCampaignMapPrototypeContract.IsValidAgentVisualState(
                visual,
                requireBodyProperties: bodyProperties.Length != 0);
        }

        private static bool TryReadBool(
            IReadOnlyDictionary<string, string> values,
            string key,
            out bool value)
        {
            value = false;
            return values.TryGetValue(key, out string rawValue) &&
                   bool.TryParse(rawValue, out value);
        }

        private static bool TryReadUInt(
            IReadOnlyDictionary<string, string> values,
            string key,
            out uint value)
        {
            value = 0u;
            return values.TryGetValue(key, out string rawValue) &&
                   uint.TryParse(
                       rawValue,
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out value);
        }

        private static string EncodeText(string value)
        {
            return Convert.ToBase64String(
                Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static bool TryDecodeText(string value, out string decoded)
        {
            decoded = null;
            try
            {
                decoded = Encoding.UTF8.GetString(
                    Convert.FromBase64String(value ?? string.Empty));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadDateTime(
            IReadOnlyDictionary<string, string> values,
            string key,
            out DateTime value)
        {
            value = DateTime.MinValue;
            return values.TryGetValue(key, out string rawValue) &&
                   DateTime.TryParse(
                       rawValue,
                       CultureInfo.InvariantCulture,
                       DateTimeStyles.RoundtripKind,
                       out value);
        }

        private static bool Fail(string reasonValue, out string reason)
        {
            reason = reasonValue;
            return false;
        }
    }
}
