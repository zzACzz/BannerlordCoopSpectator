using System;
using System.Collections.Generic;

namespace CoopSpectator.Infrastructure
{
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

        public int ServerTimeMilliseconds { get; set; }

        public CoopCampaignMapPrototypeCameraState Camera { get; set; }

        public CoopCampaignMapPrototypeState Clone()
        {
            return new CoopCampaignMapPrototypeState
            {
                Revision = Revision,
                NormalizedX = NormalizedX,
                NormalizedY = NormalizedY,
                Heading = Heading,
                ServerTimeMilliseconds = ServerTimeMilliseconds,
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

        public int SampleTimeMilliseconds { get; set; }

        public bool IsMoving { get; set; }

        public bool IsActive { get; set; }

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
                SampleTimeMilliseconds = SampleTimeMilliseconds,
                IsMoving = IsMoving,
                IsActive = IsActive,
                Camera = Camera?.Clone(),
                UpdatedUtc = UpdatedUtc
            };
        }
    }

    /// <summary>
    /// Pure protocol and scene-selection rules for the first read-only campaign-map prototype.
    /// Coordinates are normalized so the dedicated server does not need to load Main_map.
    /// </summary>
    public static class CoopCampaignMapPrototypeContract
    {
        public const int ProtocolVersion = 2;
        public const int HostBridgeSchemaVersion = 2;
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
                ServerTimeMilliseconds = QuantizeMilliseconds(safeElapsed)
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
                   candidate.ServerTimeMilliseconds >= 0 &&
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
                snapshot.SampleTimeMilliseconds < 0 ||
                !IsValidCameraState(snapshot.Camera))
            {
                return Fail("payload", out reason);
            }
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
                ServerTimeMilliseconds = snapshot.SampleTimeMilliseconds,
                Camera = snapshot.Camera?.Clone()
            };
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

        private static bool Fail(string reasonValue, out string reason)
        {
            reason = reasonValue;
            return false;
        }
    }
}
