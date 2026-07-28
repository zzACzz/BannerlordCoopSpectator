using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure.VillageBattle
{
    internal sealed class ExactVillageBattleDeploymentBoundaryContract
    {
        public int SchemaVersion { get; set; }
        public int Revision { get; set; }
        public string BattleId { get; set; }
        public string SceneName { get; set; }
        public List<ExactVillageBattleDeploymentSideBoundary> Sides { get; } =
            new List<ExactVillageBattleDeploymentSideBoundary>();
    }

    internal sealed class ExactVillageBattleDeploymentSideBoundary
    {
        public BattleSideEnum Side { get; set; }
        public float FrameOriginX { get; set; }
        public float FrameOriginY { get; set; }
        public float FrameOriginZ { get; set; }
        public float FrameForwardX { get; set; }
        public float FrameForwardY { get; set; }
        public List<ExactVillageBattleDeploymentPolygon> Boundaries { get; } =
            new List<ExactVillageBattleDeploymentPolygon>();
    }

    internal sealed class ExactVillageBattleDeploymentPolygon
    {
        public string Id { get; set; }
        public List<ExactVillageBattleDeploymentPoint> Points { get; } =
            new List<ExactVillageBattleDeploymentPoint>();
    }

    internal readonly struct ExactVillageBattleDeploymentPoint
    {
        public ExactVillageBattleDeploymentPoint(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float X { get; }
        public float Y { get; }
    }

    /// <summary>
    /// Owns the immutable server-authored VillageBattle deployment-boundary payload and
    /// the client receipt/application identity. It does not build or mutate native plans.
    /// </summary>
    internal static class ExactVillageBattleDeploymentBoundaryRuntime
    {
        public const int CurrentSchemaVersion = 1;
        public const int InitialRevision = 1;
        public const int MaxPayloadBytes = 4095;
        public const int MaxSides = 2;
        public const int MaxBoundariesPerSide = 16;
        public const int MaxPointsPerBoundary = 192;
        public const int MaxTotalPoints = 384;
        private const int MaxTextLength = 512;

        private static readonly object Sync = new object();
        private static Mission _serverMission;
        private static ExactVillageBattleDeploymentBoundaryContract _serverContract;
        private static byte[] _serverPayload = Array.Empty<byte>();
        private static string _serverPayloadHash = string.Empty;
        private static Mission _clientMission;
        private static ExactVillageBattleDeploymentBoundaryContract _clientContract;
        private static byte[] _clientPayload = Array.Empty<byte>();
        private static string _clientPayloadHash = string.Empty;
        private static int _clientAppliedRevision;
        private static string _clientAppliedPayloadHash = string.Empty;

        public static bool TryPublishServerContract(
            Mission mission,
            ExactVillageBattleDeploymentBoundaryContract contract,
            out int revision,
            out string payloadHash,
            out byte[] payload,
            out string diagnostics)
        {
            revision = 0;
            payloadHash = string.Empty;
            payload = Array.Empty<byte>();
            diagnostics = "invalid-server-contract";
            if (mission == null || contract == null)
                return false;

            lock (Sync)
            {
                if (ReferenceEquals(_serverMission, mission) &&
                    _serverContract != null &&
                    _serverPayload.Length > 0 &&
                    !string.IsNullOrWhiteSpace(_serverPayloadHash))
                {
                    revision = _serverContract.Revision;
                    payloadHash = _serverPayloadHash;
                    payload = _serverPayload;
                    diagnostics = "server-contract-already-published";
                    return true;
                }

                contract.SchemaVersion = CurrentSchemaVersion;
                contract.Revision = InitialRevision;
                if (!TryValidateContract(mission, contract, out diagnostics))
                    return false;

                if (!TrySerialize(contract, out byte[] serialized, out diagnostics))
                    return false;

                string hash = ComputePayloadHash(serialized);
                if (string.IsNullOrWhiteSpace(hash))
                {
                    diagnostics = "server-contract-hash-empty";
                    return false;
                }

                _serverMission = mission;
                _serverContract = contract;
                _serverPayload = serialized;
                _serverPayloadHash = hash;
                revision = contract.Revision;
                payloadHash = hash;
                payload = serialized;
                diagnostics = DescribeContract(contract, serialized.Length, hash);
                return true;
            }
        }

        public static bool TryGetServerContract(
            Mission mission,
            out ExactVillageBattleDeploymentBoundaryContract contract,
            out int revision,
            out string payloadHash,
            out byte[] payload)
        {
            lock (Sync)
            {
                bool available =
                    mission != null &&
                    ReferenceEquals(_serverMission, mission) &&
                    _serverContract != null &&
                    _serverPayload.Length > 0 &&
                    !string.IsNullOrWhiteSpace(_serverPayloadHash);
                contract = available ? _serverContract : null;
                revision = available ? _serverContract.Revision : 0;
                payloadHash = available ? _serverPayloadHash : string.Empty;
                payload = available ? _serverPayload : Array.Empty<byte>();
                return available;
            }
        }

        public static bool TryAcceptClientContract(
            Mission mission,
            int revision,
            string payloadHash,
            byte[] payload,
            out ExactVillageBattleDeploymentBoundaryContract contract,
            out string diagnostics)
        {
            contract = null;
            diagnostics = "invalid-client-contract";
            if (mission == null ||
                revision <= 0 ||
                string.IsNullOrWhiteSpace(payloadHash) ||
                payload == null ||
                payload.Length <= 0 ||
                payload.Length > MaxPayloadBytes)
            {
                return false;
            }

            string computedHash = ComputePayloadHash(payload);
            if (!string.Equals(computedHash, payloadHash.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                diagnostics = "payload-hash-mismatch";
                return false;
            }

            if (!TryDeserialize(payload, out ExactVillageBattleDeploymentBoundaryContract decoded, out diagnostics) ||
                decoded.Revision != revision ||
                !TryValidateContract(mission, decoded, out diagnostics))
            {
                return false;
            }

            lock (Sync)
            {
                if (ReferenceEquals(_clientMission, mission) &&
                    _clientContract != null &&
                    revision < _clientContract.Revision)
                {
                    diagnostics = "stale-client-contract";
                    return false;
                }

                bool identityChanged =
                    !ReferenceEquals(_clientMission, mission) ||
                    _clientContract == null ||
                    revision != _clientContract.Revision ||
                    !string.Equals(
                        _clientPayloadHash,
                        computedHash,
                        StringComparison.OrdinalIgnoreCase);
                _clientMission = mission;
                _clientContract = decoded;
                _clientPayload = payload;
                _clientPayloadHash = computedHash;
                if (identityChanged)
                {
                    _clientAppliedRevision = 0;
                    _clientAppliedPayloadHash = string.Empty;
                }

                contract = decoded;
                diagnostics = DescribeContract(decoded, payload.Length, computedHash);
                return true;
            }
        }

        public static bool TryGetClientContract(
            Mission mission,
            out ExactVillageBattleDeploymentBoundaryContract contract,
            out int revision,
            out string payloadHash)
        {
            lock (Sync)
            {
                bool available =
                    mission != null &&
                    ReferenceEquals(_clientMission, mission) &&
                    _clientContract != null &&
                    !string.IsNullOrWhiteSpace(_clientPayloadHash);
                contract = available ? _clientContract : null;
                revision = available ? _clientContract.Revision : 0;
                payloadHash = available ? _clientPayloadHash : string.Empty;
                return available;
            }
        }

        public static void MarkClientApplied(Mission mission, int revision, string payloadHash)
        {
            if (mission == null || revision <= 0 || string.IsNullOrWhiteSpace(payloadHash))
                return;

            lock (Sync)
            {
                if (!ReferenceEquals(_clientMission, mission) ||
                    _clientContract == null ||
                    _clientContract.Revision != revision ||
                    !string.Equals(_clientPayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _clientAppliedRevision = revision;
                _clientAppliedPayloadHash = payloadHash;
            }
        }

        public static bool IsClientApplied(
            Mission mission,
            out int revision,
            out string payloadHash,
            out string diagnostics)
        {
            lock (Sync)
            {
                revision = _clientContract?.Revision ?? 0;
                payloadHash = _clientPayloadHash ?? string.Empty;
                bool applied =
                    mission != null &&
                    ReferenceEquals(_clientMission, mission) &&
                    _clientContract != null &&
                    _clientAppliedRevision == _clientContract.Revision &&
                    string.Equals(
                        _clientAppliedPayloadHash,
                        _clientPayloadHash,
                        StringComparison.OrdinalIgnoreCase);
                diagnostics =
                    "Applied=" + applied +
                    " Revision=" + revision +
                    " AppliedRevision=" + _clientAppliedRevision +
                    " Hash=" + ShortHash(payloadHash);
                return applied;
            }
        }

        public static void Reset(string source)
        {
            lock (Sync)
            {
                _serverMission = null;
                _serverContract = null;
                _serverPayload = Array.Empty<byte>();
                _serverPayloadHash = string.Empty;
                _clientMission = null;
                _clientContract = null;
                _clientPayload = Array.Empty<byte>();
                _clientPayloadHash = string.Empty;
                _clientAppliedRevision = 0;
                _clientAppliedPayloadHash = string.Empty;
            }
        }

        private static bool TrySerialize(
            ExactVillageBattleDeploymentBoundaryContract contract,
            out byte[] payload,
            out string diagnostics)
        {
            payload = Array.Empty<byte>();
            diagnostics = "serialization-failed";
            try
            {
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
                {
                    writer.Write(contract.SchemaVersion);
                    writer.Write(contract.Revision);
                    writer.Write(contract.BattleId ?? string.Empty);
                    writer.Write(contract.SceneName ?? string.Empty);
                    writer.Write(contract.Sides.Count);
                    foreach (ExactVillageBattleDeploymentSideBoundary side in contract.Sides)
                    {
                        writer.Write((int)side.Side);
                        writer.Write(side.FrameOriginX);
                        writer.Write(side.FrameOriginY);
                        writer.Write(side.FrameOriginZ);
                        writer.Write(side.FrameForwardX);
                        writer.Write(side.FrameForwardY);
                        writer.Write(side.Boundaries.Count);
                        foreach (ExactVillageBattleDeploymentPolygon boundary in side.Boundaries)
                        {
                            writer.Write(boundary.Id ?? string.Empty);
                            writer.Write(boundary.Points.Count);
                            foreach (ExactVillageBattleDeploymentPoint point in boundary.Points)
                            {
                                writer.Write(point.X);
                                writer.Write(point.Y);
                            }
                        }
                    }

                    writer.Flush();
                    payload = stream.ToArray();
                }

                if (payload.Length <= 0 || payload.Length > MaxPayloadBytes)
                {
                    diagnostics = "payload-size-out-of-range:" + payload.Length;
                    payload = Array.Empty<byte>();
                    return false;
                }

                diagnostics = "serialized Bytes=" + payload.Length;
                return true;
            }
            catch (Exception ex)
            {
                diagnostics = "serialization-exception:" + ex.GetType().Name + ":" + ex.Message;
                payload = Array.Empty<byte>();
                return false;
            }
        }

        private static bool TryDeserialize(
            byte[] payload,
            out ExactVillageBattleDeploymentBoundaryContract contract,
            out string diagnostics)
        {
            contract = null;
            diagnostics = "deserialization-failed";
            try
            {
                using (var stream = new MemoryStream(payload, writable: false))
                using (var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true))
                {
                    var decoded = new ExactVillageBattleDeploymentBoundaryContract
                    {
                        SchemaVersion = reader.ReadInt32(),
                        Revision = reader.ReadInt32(),
                        BattleId = reader.ReadString(),
                        SceneName = reader.ReadString()
                    };
                    int sideCount = reader.ReadInt32();
                    if (sideCount <= 0 || sideCount > MaxSides)
                    {
                        diagnostics = "side-count-out-of-range:" + sideCount;
                        return false;
                    }

                    int totalPoints = 0;
                    for (int sideIndex = 0; sideIndex < sideCount; sideIndex++)
                    {
                        var side = new ExactVillageBattleDeploymentSideBoundary
                        {
                            Side = (BattleSideEnum)reader.ReadInt32(),
                            FrameOriginX = reader.ReadSingle(),
                            FrameOriginY = reader.ReadSingle(),
                            FrameOriginZ = reader.ReadSingle(),
                            FrameForwardX = reader.ReadSingle(),
                            FrameForwardY = reader.ReadSingle()
                        };
                        int boundaryCount = reader.ReadInt32();
                        if (boundaryCount <= 0 || boundaryCount > MaxBoundariesPerSide)
                        {
                            diagnostics = "boundary-count-out-of-range:" + boundaryCount;
                            return false;
                        }

                        for (int boundaryIndex = 0; boundaryIndex < boundaryCount; boundaryIndex++)
                        {
                            var boundary = new ExactVillageBattleDeploymentPolygon
                            {
                                Id = reader.ReadString()
                            };
                            int pointCount = reader.ReadInt32();
                            totalPoints += pointCount;
                            if (pointCount < 3 ||
                                pointCount > MaxPointsPerBoundary ||
                                totalPoints > MaxTotalPoints)
                            {
                                diagnostics = "point-count-out-of-range:" + pointCount;
                                return false;
                            }

                            for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
                            {
                                boundary.Points.Add(
                                    new ExactVillageBattleDeploymentPoint(
                                        reader.ReadSingle(),
                                        reader.ReadSingle()));
                            }

                            side.Boundaries.Add(boundary);
                        }

                        decoded.Sides.Add(side);
                    }

                    if (stream.Position != stream.Length)
                    {
                        diagnostics = "payload-trailing-bytes:" + (stream.Length - stream.Position);
                        return false;
                    }

                    contract = decoded;
                    diagnostics = "deserialized";
                    return true;
                }
            }
            catch (Exception ex)
            {
                diagnostics = "deserialization-exception:" + ex.GetType().Name + ":" + ex.Message;
                contract = null;
                return false;
            }
        }

        private static bool TryValidateContract(
            Mission mission,
            ExactVillageBattleDeploymentBoundaryContract contract,
            out string diagnostics)
        {
            diagnostics = "contract-invalid";
            if (mission == null || contract == null)
                return false;
            if (contract.SchemaVersion != CurrentSchemaVersion || contract.Revision <= 0)
            {
                diagnostics = "schema-or-revision-invalid";
                return false;
            }
            if (string.IsNullOrWhiteSpace(contract.SceneName) ||
                contract.SceneName.Length > MaxTextLength ||
                !string.Equals(
                    contract.SceneName.Trim(),
                    mission.SceneName ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase))
            {
                diagnostics = "scene-mismatch";
                return false;
            }
            if ((contract.BattleId ?? string.Empty).Length > MaxTextLength)
            {
                diagnostics = "battle-id-too-long";
                return false;
            }

            string currentBattleId = BattleSnapshotRuntimeState.GetCurrent()?.BattleId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(contract.BattleId) &&
                !string.IsNullOrWhiteSpace(currentBattleId) &&
                !string.Equals(contract.BattleId, currentBattleId, StringComparison.Ordinal))
            {
                diagnostics = "battle-id-mismatch";
                return false;
            }

            if (contract.Sides.Count != MaxSides)
            {
                diagnostics = "side-count-invalid:" + contract.Sides.Count;
                return false;
            }

            var observedSides = new HashSet<BattleSideEnum>();
            int totalPoints = 0;
            foreach (ExactVillageBattleDeploymentSideBoundary side in contract.Sides)
            {
                if (side == null ||
                    (side.Side != BattleSideEnum.Attacker && side.Side != BattleSideEnum.Defender) ||
                    !observedSides.Add(side.Side) ||
                    !IsFinite(side.FrameOriginX) ||
                    !IsFinite(side.FrameOriginY) ||
                    !IsFinite(side.FrameOriginZ) ||
                    !IsFinite(side.FrameForwardX) ||
                    !IsFinite(side.FrameForwardY) ||
                    side.FrameForwardX * side.FrameForwardX +
                        side.FrameForwardY * side.FrameForwardY <= 0.0001f ||
                    side.Boundaries.Count <= 0 ||
                    side.Boundaries.Count > MaxBoundariesPerSide)
                {
                    diagnostics = "side-contract-invalid";
                    return false;
                }

                foreach (ExactVillageBattleDeploymentPolygon boundary in side.Boundaries)
                {
                    if (boundary == null ||
                        string.IsNullOrWhiteSpace(boundary.Id) ||
                        boundary.Id.Length > MaxTextLength ||
                        boundary.Points.Count < 3 ||
                        boundary.Points.Count > MaxPointsPerBoundary)
                    {
                        diagnostics = "boundary-contract-invalid";
                        return false;
                    }

                    totalPoints += boundary.Points.Count;
                    if (totalPoints > MaxTotalPoints)
                    {
                        diagnostics = "total-point-count-out-of-range:" + totalPoints;
                        return false;
                    }

                    foreach (ExactVillageBattleDeploymentPoint point in boundary.Points)
                    {
                        if (!IsFinite(point.X) || !IsFinite(point.Y))
                        {
                            diagnostics = "boundary-point-invalid";
                            return false;
                        }
                    }
                }
            }

            diagnostics = "contract-valid";
            return true;
        }

        private static string ComputePayloadHash(byte[] payload)
        {
            if (payload == null || payload.Length <= 0)
                return string.Empty;

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(payload);
                var builder = new StringBuilder(hashBytes.Length * 2);
                foreach (byte hashByte in hashBytes)
                    builder.Append(hashByte.ToString("x2"));
                return builder.ToString();
            }
        }

        private static string DescribeContract(
            ExactVillageBattleDeploymentBoundaryContract contract,
            int payloadBytes,
            string payloadHash)
        {
            int boundaryCount = 0;
            int pointCount = 0;
            foreach (ExactVillageBattleDeploymentSideBoundary side in contract.Sides)
            {
                boundaryCount += side?.Boundaries.Count ?? 0;
                if (side == null)
                    continue;
                foreach (ExactVillageBattleDeploymentPolygon boundary in side.Boundaries)
                    pointCount += boundary?.Points.Count ?? 0;
            }

            return "Revision=" + contract.Revision +
                " Sides=" + contract.Sides.Count +
                " Boundaries=" + boundaryCount +
                " Points=" + pointCount +
                " Bytes=" + payloadBytes +
                " Hash=" + ShortHash(payloadHash);
        }

        private static string ShortHash(string payloadHash)
        {
            if (string.IsNullOrWhiteSpace(payloadHash))
                return "null";
            return payloadHash.Length <= 12
                ? payloadHash
                : payloadHash.Substring(0, 12);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) <= 1000000f;
        }
    }
}
