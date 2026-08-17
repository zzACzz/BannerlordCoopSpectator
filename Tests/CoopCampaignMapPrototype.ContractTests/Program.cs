using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;

internal static class Program
{
    private static int Main()
    {
        try
        {
            ValidateSyntheticPathBounds();
            ValidateRevisionAndPayloadPolicy();
            ValidateQuantizationAndInterpolation();
            ValidateMapPositionNormalization();
            ValidateDirectionQuantization();
            ValidateCameraPayload();
            ValidateHostBridgeCodec();
            ValidateHostSnapshotFreshness();
            ValidateSceneOwnerResolution();
            ValidateDedicatedBattleObserverSuppression();
            Console.WriteLine("Coop campaign map prototype contract tests passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void ValidateSyntheticPathBounds()
    {
        CoopCampaignMapPrototypeState state =
            CoopCampaignMapPrototypeContract.CreateSyntheticState(
                elapsedSeconds: 120d,
                revision: 42);
        Assert(state.Revision == 42, "Synthetic state must retain its revision.");
        Assert(
            state.NormalizedX >= 0 &&
            state.NormalizedX <= CoopCampaignMapPrototypeContract.UnitScale,
            "Synthetic X must stay inside normalized map bounds.");
        Assert(
            state.NormalizedY >= 0 &&
            state.NormalizedY <= CoopCampaignMapPrototypeContract.UnitScale,
            "Synthetic Y must stay inside normalized map bounds.");
        Assert(
            state.Heading >= 0 &&
            state.Heading <= CoopCampaignMapPrototypeContract.UnitScale,
            "Synthetic heading must stay inside one turn.");
    }

    private static void ValidateRevisionAndPayloadPolicy()
    {
        CoopCampaignMapPrototypeState current =
            CoopCampaignMapPrototypeContract.CreateSyntheticState(1d, 10);
        CoopCampaignMapPrototypeState newer =
            CoopCampaignMapPrototypeContract.CreateSyntheticState(2d, 11);
        Assert(
            CoopCampaignMapPrototypeContract.CanAccept(current, newer),
            "A newer valid server state must be accepted.");
        Assert(
            !CoopCampaignMapPrototypeContract.CanAccept(current, current.Clone()),
            "A duplicate revision must be rejected.");

        newer.NormalizedX = CoopCampaignMapPrototypeContract.UnitScale + 1;
        Assert(
            !CoopCampaignMapPrototypeContract.CanAccept(current, newer),
            "An out-of-range coordinate must fail closed.");
    }

    private static void ValidateQuantizationAndInterpolation()
    {
        int quantized = CoopCampaignMapPrototypeContract.QuantizeUnit(0.25d);
        Assert(
            quantized == CoopCampaignMapPrototypeContract.UnitScale / 4,
            "Unit quantization must be deterministic.");
        AssertNearlyEqual(
            0.5d,
            CoopCampaignMapPrototypeContract.InterpolateUnit(0.25d, 0.75d, 0.5d),
            "Interpolation must produce the midpoint.");
        AssertNearlyEqual(
            1d,
            CoopCampaignMapPrototypeContract.InterpolateUnit(0.75d, 2d, 1d),
            "Interpolation must clamp the result to the normalized range.");
    }

    private static void ValidateMapPositionNormalization()
    {
        bool normalized =
            CoopCampaignMapPrototypeContract.TryNormalizeMapPosition(
                positionX: 150d,
                positionY: 250d,
                minimumX: 100d,
                minimumY: 200d,
                maximumX: 300d,
                maximumY: 400d,
                out int normalizedX,
                out int normalizedY);
        Assert(normalized, "Valid map borders must normalize a position.");
        Assert(
            normalizedX == CoopCampaignMapPrototypeContract.UnitScale / 4 &&
            normalizedY == CoopCampaignMapPrototypeContract.UnitScale / 4,
            "Map normalization must retain the position inside host borders.");

        Assert(
            !CoopCampaignMapPrototypeContract.TryNormalizeMapPosition(
                1d,
                1d,
                5d,
                0d,
                5d,
                10d,
                out _,
                out _),
            "A zero-width map border must be rejected.");

        Assert(
            CoopCampaignMapPrototypeContract.TryNormalizeMapPosition(
                -50d,
                150d,
                0d,
                0d,
                100d,
                100d,
                out normalizedX,
                out normalizedY) &&
            normalizedX == 0 &&
            normalizedY == CoopCampaignMapPrototypeContract.UnitScale,
            "Positions outside map borders must clamp deterministically.");
    }

    private static void ValidateDirectionQuantization()
    {
        int north = CoopCampaignMapPrototypeContract.QuantizeDirection(
            0d,
            1d,
            fallbackHeading: 0);
        Assert(
            north == CoopCampaignMapPrototypeContract.UnitScale / 4,
            "A north-facing bearing must quantize to one quarter turn.");

        int fallback = CoopCampaignMapPrototypeContract.QuantizeDirection(
            0d,
            0d,
            fallbackHeading: 123456);
        Assert(
            fallback == 123456,
            "A stationary party must retain its last valid heading.");
    }

    private static void ValidateCameraPayload()
    {
        Assert(
            CoopCampaignMapPrototypeContract.TryQuantizeCamera(
                originX: 570.625d,
                originY: 244.875d,
                originZ: 92.5d,
                directionX: 0d,
                directionY: 0.8d,
                directionZ: -0.6d,
                upX: 0d,
                upY: 0.6d,
                upZ: 0.8d,
                verticalFovRadians: Math.PI / 4d,
                out CoopCampaignMapPrototypeCameraState camera),
            "A finite orthogonal camera frame must quantize.");
        Assert(
            CoopCampaignMapPrototypeContract.IsValidCameraState(camera),
            "A quantized camera frame must remain valid.");
        AssertNearlyEqual(
            570.625d,
            CoopCampaignMapPrototypeContract.DequantizeWorldCoordinate(
                camera.OriginX),
            "Camera X must survive fixed-point quantization.");
        AssertNearlyEqual(
            244.875d,
            CoopCampaignMapPrototypeContract.DequantizeWorldCoordinate(
                camera.OriginY),
            "Camera Y must survive fixed-point quantization.");
        AssertNearlyEqual(
            Math.PI / 4d,
            CoopCampaignMapPrototypeContract.DequantizeCameraFov(
                camera.VerticalFov),
            "Camera vertical FOV must survive unit quantization.",
            tolerance: 0.00001d);

        Assert(
            !CoopCampaignMapPrototypeContract.TryQuantizeCamera(
                0d,
                0d,
                10d,
                0d,
                1d,
                0d,
                0d,
                2d,
                0d,
                Math.PI / 4d,
                out _),
            "Parallel direction and up vectors must be rejected.");

        CoopCampaignMapPrototypeState current =
            CoopCampaignMapPrototypeContract.CreateSyntheticState(1d, 1);
        CoopCampaignMapPrototypeState candidate =
            CoopCampaignMapPrototypeContract.CreateSyntheticState(2d, 2);
        candidate.Camera = camera.Clone();
        candidate.Camera.DirectionX =
            CoopCampaignMapPrototypeContract.UnitScale + 1;
        Assert(
            !CoopCampaignMapPrototypeContract.CanAccept(current, candidate),
            "A network state with an invalid camera component must be rejected.");
    }

    private static void ValidateHostBridgeCodec()
    {
        DateTime updatedUtc = new DateTime(
            2026,
            8,
            16,
            12,
            34,
            56,
            DateTimeKind.Utc);
        CoopCampaignMapPrototypeHostSnapshot expected =
            CreateHostSnapshot(updatedUtc);
        string[] lines =
            CoopCampaignMapPrototypeBridgeCodec.Serialize(expected);
        Assert(
            CoopCampaignMapPrototypeBridgeCodec.TryParse(
                lines,
                out CoopCampaignMapPrototypeHostSnapshot actual,
                out string reason),
            "A serialized host bridge snapshot must parse. Reason=" + reason);
        Assert(
            actual.SchemaVersion == expected.SchemaVersion &&
            actual.SessionId == expected.SessionId &&
            actual.Revision == expected.Revision &&
            actual.NormalizedX == expected.NormalizedX &&
            actual.NormalizedY == expected.NormalizedY &&
            actual.Heading == expected.Heading &&
            actual.SampleTimeMilliseconds == expected.SampleTimeMilliseconds &&
            actual.IsMoving == expected.IsMoving &&
            actual.IsActive == expected.IsActive &&
            CameraEquals(actual.Camera, expected.Camera) &&
            actual.UpdatedUtc == expected.UpdatedUtc,
            "The bridge codec must preserve every authoritative field.");

        Assert(
            !CoopCampaignMapPrototypeBridgeCodec.TryParse(
                new[] { "SchemaVersion=1", "broken-line" },
                out _,
                out _),
            "A truncated bridge snapshot must be rejected.");
    }

    private static void ValidateHostSnapshotFreshness()
    {
        DateTime utcNow = new DateTime(
            2026,
            8,
            16,
            12,
            0,
            0,
            DateTimeKind.Utc);
        CoopCampaignMapPrototypeHostSnapshot snapshot =
            CreateHostSnapshot(utcNow - TimeSpan.FromMilliseconds(500d));
        Assert(
            CoopCampaignMapPrototypeContract.TryValidateHostSnapshot(
                snapshot,
                utcNow,
                TimeSpan.FromSeconds(2d),
                out string reason),
            "A fresh authoritative snapshot must be accepted. Reason=" + reason);

        snapshot.UpdatedUtc = utcNow - TimeSpan.FromSeconds(3d);
        Assert(
            !CoopCampaignMapPrototypeContract.TryValidateHostSnapshot(
                snapshot,
                utcNow,
                TimeSpan.FromSeconds(2d),
                out reason) &&
            reason == "stale",
            "An expired host snapshot must fail as stale.");

        snapshot.UpdatedUtc = utcNow + TimeSpan.FromSeconds(6d);
        Assert(
            !CoopCampaignMapPrototypeContract.TryValidateHostSnapshot(
                snapshot,
                utcNow,
                TimeSpan.FromSeconds(2d),
                out reason) &&
            reason == "future",
            "A far-future host snapshot must be rejected.");

        snapshot.UpdatedUtc = utcNow;
        snapshot.IsActive = false;
        Assert(
            !CoopCampaignMapPrototypeContract.TryValidateHostSnapshot(
                snapshot,
                utcNow,
                TimeSpan.FromSeconds(2d),
                out reason) &&
            reason == "inactive",
            "An inactive host snapshot must not drive the map mission.");
    }

    private static CoopCampaignMapPrototypeHostSnapshot CreateHostSnapshot(
        DateTime updatedUtc)
    {
        return new CoopCampaignMapPrototypeHostSnapshot
        {
            SchemaVersion =
                CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
            SessionId = "0123456789abcdef0123456789abcdef",
            Revision = 7,
            NormalizedX = 250000,
            NormalizedY = 750000,
                Heading = 125000,
                SampleTimeMilliseconds = 12345,
                IsMoving = true,
                IsActive = true,
                Camera = CreateCameraState(),
                UpdatedUtc = updatedUtc
            };
    }

    private static CoopCampaignMapPrototypeCameraState CreateCameraState()
    {
        Assert(
            CoopCampaignMapPrototypeContract.TryQuantizeCamera(
                originX: 570.625d,
                originY: 244.875d,
                originZ: 92.5d,
                directionX: 0d,
                directionY: 0.8d,
                directionZ: -0.6d,
                upX: 0d,
                upY: 0.6d,
                upZ: 0.8d,
                verticalFovRadians: 0.6981317d,
                out CoopCampaignMapPrototypeCameraState camera),
            "The test camera must quantize.");
        return camera;
    }

    private static bool CameraEquals(
        CoopCampaignMapPrototypeCameraState left,
        CoopCampaignMapPrototypeCameraState right)
    {
        return left != null &&
               right != null &&
               left.OriginX == right.OriginX &&
               left.OriginY == right.OriginY &&
               left.OriginZ == right.OriginZ &&
               left.DirectionX == right.DirectionX &&
               left.DirectionY == right.DirectionY &&
               left.DirectionZ == right.DirectionZ &&
               left.UpX == right.UpX &&
               left.UpY == right.UpY &&
               left.UpZ == right.UpZ &&
               left.VerticalFov == right.VerticalFov;
    }

    private static void ValidateSceneOwnerResolution()
    {
        Dictionary<string, IEnumerable<string>> scenes =
            new Dictionary<string, IEnumerable<string>>
            {
                ["Native"] = new[] { "mp_tdm_map_001" },
                ["SandBox"] = new[] { "Main_map", "bandit_forest" },
                ["LaterOverride"] = new[] { "Main_map" }
            };
        string owner =
            CoopCampaignMapPrototypeContract.ResolveLastOwningModule(
                new[] { "Native", "SandBox", "LaterOverride" },
                module => scenes[module]);
        Assert(
            owner == "LaterOverride",
            "Scene resolution must match Bannerlord's last-active-module override rule.");

        string missing =
            CoopCampaignMapPrototypeContract.ResolveLastOwningModule(
                new[] { "Native" },
                module => scenes[module]);
        Assert(missing == null, "A missing Main_map owner must return null.");
    }

    private static void ValidateDedicatedBattleObserverSuppression()
    {
        Assert(
            !CoopCampaignMapPrototypeContract.ShouldSuppressDedicatedBattleObserver(
                hasPrototypeServerBehavior: false,
                hasPrototypeNetworkController: false),
            "A regular mission must retain the dedicated battle observer.");
        Assert(
            CoopCampaignMapPrototypeContract.ShouldSuppressDedicatedBattleObserver(
                hasPrototypeServerBehavior: true,
                hasPrototypeNetworkController: false),
            "The prototype server behavior must suppress the dedicated battle observer.");
        Assert(
            CoopCampaignMapPrototypeContract.ShouldSuppressDedicatedBattleObserver(
                hasPrototypeServerBehavior: false,
                hasPrototypeNetworkController: true),
            "The prototype network controller must suppress the dedicated battle observer.");
        Assert(
            CoopCampaignMapPrototypeContract.ShouldSuppressDedicatedBattleObserver(
                hasPrototypeServerBehavior: true,
                hasPrototypeNetworkController: true),
            "Both prototype markers must suppress the dedicated battle observer.");
    }

    private static void AssertNearlyEqual(
        double expected,
        double actual,
        string message,
        double tolerance = 0.000001d)
    {
        if (Math.Abs(expected - actual) > tolerance)
        {
            throw new InvalidOperationException(
                message + " Expected=" + expected + " Actual=" + actual + ".");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
