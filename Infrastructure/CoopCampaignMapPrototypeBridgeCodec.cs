using System;
using System.Collections.Generic;
using System.Globalization;

namespace CoopSpectator.Infrastructure
{
    public static class CoopCampaignMapPrototypeBridgeCodec
    {
        public static string[] Serialize(
            CoopCampaignMapPrototypeHostSnapshot snapshot)
        {
            if (snapshot == null)
                return Array.Empty<string>();

            var lines = new List<string>
            {
                "SchemaVersion=" + snapshot.SchemaVersion.ToString(CultureInfo.InvariantCulture),
                "SessionId=" + (snapshot.SessionId ?? string.Empty),
                "Revision=" + snapshot.Revision.ToString(CultureInfo.InvariantCulture),
                "NormalizedX=" + snapshot.NormalizedX.ToString(CultureInfo.InvariantCulture),
                "NormalizedY=" + snapshot.NormalizedY.ToString(CultureInfo.InvariantCulture),
                "Heading=" + snapshot.Heading.ToString(CultureInfo.InvariantCulture),
                "SampleTimeMilliseconds=" + snapshot.SampleTimeMilliseconds.ToString(CultureInfo.InvariantCulture),
                "IsMoving=" + snapshot.IsMoving,
                "IsActive=" + snapshot.IsActive,
                "HasCamera=" + (snapshot.Camera != null)
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
                !TryReadInt(values, "SampleTimeMilliseconds", out int sampleTimeMilliseconds) ||
                !TryReadBool(values, "IsMoving", out bool isMoving) ||
                !TryReadBool(values, "IsActive", out bool isActive) ||
                !TryReadBool(values, "HasCamera", out bool hasCamera) ||
                !TryReadDateTime(values, "UpdatedUtc", out DateTime updatedUtc))
            {
                return Fail("malformed", out reason);
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

            snapshot = new CoopCampaignMapPrototypeHostSnapshot
            {
                SchemaVersion = schemaVersion,
                SessionId = sessionId,
                Revision = revision,
                NormalizedX = normalizedX,
                NormalizedY = normalizedY,
                Heading = heading,
                SampleTimeMilliseconds = sampleTimeMilliseconds,
                IsMoving = isMoving,
                IsActive = isActive,
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

        private static bool TryReadBool(
            IReadOnlyDictionary<string, string> values,
            string key,
            out bool value)
        {
            value = false;
            return values.TryGetValue(key, out string rawValue) &&
                   bool.TryParse(rawValue, out value);
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
