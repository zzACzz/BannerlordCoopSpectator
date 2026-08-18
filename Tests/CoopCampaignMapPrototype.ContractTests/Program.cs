using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;

internal static class Program
{
    private static int Main()
    {
        try
        {
            ValidateProtocolVersion();
            ValidateSyntheticPathBounds();
            ValidateRevisionAndPayloadPolicy();
            ValidateQuantizationAndInterpolation();
            ValidateMapVisualStateQuantization();
            ValidateMapPositionNormalization();
            ValidateDirectionQuantization();
            ValidateCameraPayload();
            ValidateVisibleEntityContract();
            ValidateVisibleEntitySnapshotAssembler();
            ValidateHostBridgeCodec();
            ValidateReplicaCatalogCodec();
            ValidateReplicaDynamicCodec();
            ValidateSettlementNameplateSizeCodec();
            ValidatePartyVisualCodec();
            ValidateExactPartyVisualContract();
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

    private static void ValidateProtocolVersion()
    {
        Assert(
            CoopCampaignMapPrototypeContract.ProtocolVersion == 10 &&
            CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion == 10,
            "The full campaign-map replica requires protocol and bridge schema 9.");
    }

    private static void ValidateMapVisualStateQuantization()
    {
        Assert(
            CoopCampaignMapPrototypeContract.TryQuantizeMapVisualState(
                currentHourInDay: 18d,
                seasonTimeFactor: 0.25d,
                out int normalizedTimeOfDay,
                out int seasonTimeFactor),
            "A finite campaign map visual state must quantize.");
        Assert(
            normalizedTimeOfDay ==
                CoopCampaignMapPrototypeContract.UnitScale * 3 / 4 &&
            seasonTimeFactor ==
                CoopCampaignMapPrototypeContract.UnitScale / 4,
            "Map time and season factors must use deterministic unit quantization.");
        AssertNearlyEqual(
            18d,
            CoopCampaignMapPrototypeContract.DequantizeTimeOfDay(
                normalizedTimeOfDay),
            "Campaign time of day must survive fixed-point quantization.");
        Assert(
            CoopCampaignMapPrototypeContract.QuantizeTimeOfDay(25d) ==
                CoopCampaignMapPrototypeContract.QuantizeTimeOfDay(1d),
            "Campaign time of day must wrap after 24 hours.");
        Assert(
            !CoopCampaignMapPrototypeContract.TryQuantizeMapVisualState(
                double.NaN,
                0.5d,
                out _,
                out _),
            "A non-finite campaign map visual state must be rejected.");
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
            actual.NormalizedTimeOfDay == expected.NormalizedTimeOfDay &&
            actual.SeasonTimeFactor == expected.SeasonTimeFactor &&
            actual.SampleTimeMilliseconds == expected.SampleTimeMilliseconds &&
            actual.IsMoving == expected.IsMoving &&
            actual.IsActive == expected.IsActive &&
            actual.VisibleEntitiesRevision ==
                expected.VisibleEntitiesRevision &&
            actual.CatalogRevision == expected.CatalogRevision &&
            actual.DynamicRevision == expected.DynamicRevision &&
            VisibleEntitiesEqual(
                actual.VisibleEntities,
                expected.VisibleEntities) &&
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

    private static void ValidateReplicaCatalogCodec()
    {
        var catalog = new CoopCampaignMapPrototypeCatalogSnapshot
        {
            SchemaVersion =
                CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
            SessionId = "0123456789abcdef0123456789abcdef",
            Revision = 11,
            UpdatedUtc = new DateTime(
                2026,
                8,
                18,
                9,
                30,
                0,
                DateTimeKind.Utc),
            Entities = new List<CoopCampaignMapPrototypeCatalogEntityState>
            {
                new CoopCampaignMapPrototypeCatalogEntityState
                {
                    EntityId = "party:main",
                    DisplayName = "Main party",
                    Kind = CoopCampaignMapPrototypeEntityKind.MainParty,
                    SettlementNameplateSize =
                        CoopCampaignMapPrototypeSettlementNameplateSize.None,
                    SettlementKind =
                        CoopCampaignMapPrototypeSettlementKind.None,
                    PrimaryColor = 0xFF102030u,
                    SecondaryColor = 0xFF405060u,
                    BannerCode = "banner",
                    VisualCharacterId = "lord_1_1",
                    CultureId = "empire",
                    PartyVisualKind =
                        CoopCampaignMapPrototypePartyVisualKind.Mounted,
                    HumanVisual = CreateHumanVisualState(),
                    MountVisual = CreateMountVisualState("horse", "harness"),
                    FactionId = "empire",
                    FactionName = "Empire",
                    OwnerName = "Owner",
                    LeaderName = "Leader",
                    ArmyId = "army-main",
                    ArmyName = "Leader's Army",
                    IsArmyLeader = true,
                    SelectionRadius = 18000
                },
                new CoopCampaignMapPrototypeCatalogEntityState
                {
                    EntityId = "settlement:town",
                    DisplayName = "Town",
                    Kind = CoopCampaignMapPrototypeEntityKind.Settlement,
                    SettlementNameplateSize =
                        CoopCampaignMapPrototypeSettlementNameplateSize.Large,
                    SettlementKind =
                        CoopCampaignMapPrototypeSettlementKind.Town,
                    PrimaryColor = 0xFF102030u,
                    SecondaryColor = 0xFF405060u,
                    BannerCode = "banner",
                    VisualCharacterId = string.Empty,
                    CultureId = string.Empty,
                    PartyVisualKind =
                        CoopCampaignMapPrototypePartyVisualKind.None,
                    FactionId = "empire",
                    FactionName = "Empire",
                    OwnerName = "Clan",
                    LeaderName = "Leader",
                    ArmyId = string.Empty,
                    ArmyName = string.Empty,
                    SelectionRadius = 30000
                },
                new CoopCampaignMapPrototypeCatalogEntityState
                {
                    EntityId = "settlement:tutorial_training_field",
                    DisplayName = "Training Field",
                    Kind = CoopCampaignMapPrototypeEntityKind.Settlement,
                    SettlementNameplateSize =
                        CoopCampaignMapPrototypeSettlementNameplateSize.Small,
                    SettlementKind =
                        CoopCampaignMapPrototypeSettlementKind.Special,
                    PrimaryColor = 0xFF102030u,
                    SecondaryColor = 0xFF405060u,
                    BannerCode = string.Empty,
                    VisualCharacterId = string.Empty,
                    CultureId = string.Empty,
                    PartyVisualKind =
                        CoopCampaignMapPrototypePartyVisualKind.None,
                    FactionId = string.Empty,
                    FactionName = string.Empty,
                    OwnerName = string.Empty,
                    LeaderName = string.Empty,
                    ArmyId = string.Empty,
                    ArmyName = string.Empty,
                    SelectionRadius = 30000
                }
            }
        };

        string[] lines =
            CoopCampaignMapPrototypeBridgeCodec.SerializeCatalog(catalog);
        Assert(
            CoopCampaignMapPrototypeBridgeCodec.TryParseCatalog(
                lines,
                out CoopCampaignMapPrototypeCatalogSnapshot actual,
                out string reason),
            "A serialized replica catalog must parse. Reason=" + reason);
        Assert(
            actual.Revision == catalog.Revision &&
            actual.Entities.Count == 3 &&
            actual.Entities[0].LeaderName == "Leader" &&
            actual.Entities[0].MountVisual != null &&
            actual.Entities[1].SettlementKind ==
                CoopCampaignMapPrototypeSettlementKind.Town &&
            actual.Entities[2].SettlementKind ==
                CoopCampaignMapPrototypeSettlementKind.Special,
            "The replica catalog codec must preserve identity, information and visuals.");
        CoopCampaignMapPrototypeCatalogEntityState invalidSpecial =
            actual.Entities[2].Clone();
        invalidSpecial.SettlementKind =
            CoopCampaignMapPrototypeSettlementKind.None;
        Assert(
            !CoopCampaignMapPrototypeContract.IsValidCatalogEntity(invalidSpecial),
            "A settlement without a concrete settlement kind must remain invalid.");
        Assert(
            CoopCampaignMapPrototypeContract.IsCompletePartyVisualState(
                actual.Entities[0]),
            "A mounted catalog entity must retain a complete rider/mount descriptor.");
        actual.Entities[0].MountVisual = null;
        Assert(
            !CoopCampaignMapPrototypeContract.IsCompletePartyVisualState(
                actual.Entities[0]),
            "An incomplete mounted descriptor must fail the completeness check.");
    }

    private static void ValidateReplicaDynamicCodec()
    {
        var dynamicSnapshot = new CoopCampaignMapPrototypeDynamicSnapshot
        {
            SchemaVersion =
                CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
            SessionId = "0123456789abcdef0123456789abcdef",
            Revision = 17,
            UpdatedUtc = new DateTime(
                2026,
                8,
                18,
                9,
                31,
                0,
                DateTimeKind.Utc),
            Entities = new List<CoopCampaignMapPrototypeDynamicEntityState>
            {
                new CoopCampaignMapPrototypeDynamicEntityState
                {
                    EntityId = "party:main",
                    NormalizedX = 123000,
                    NormalizedY = 456000,
                    Heading = 789000,
                    PartySize = 30,
                    IsVisible = true,
                    IsMoving = true,
                    ArmyPartyCount = 4,
                    ArmyTotalSize = 210,
                    ArmyCohesion = 750000,
                    AppearanceRevision = 2,
                    InformationRevision = 3
                }
            }
        };

        string[] lines =
            CoopCampaignMapPrototypeBridgeCodec.SerializeDynamic(
                dynamicSnapshot);
        Assert(
            CoopCampaignMapPrototypeBridgeCodec.TryParseDynamic(
                lines,
                out CoopCampaignMapPrototypeDynamicSnapshot actual,
                out string reason),
            "A serialized dynamic snapshot must parse. Reason=" + reason);
        Assert(
            actual.Revision == dynamicSnapshot.Revision &&
            actual.Entities.Count == 1 &&
            actual.Entities[0].IsMoving &&
            actual.Entities[0].ArmyTotalSize == 210 &&
            actual.Entities[0].ArmyCohesion == 750000,
            "The dynamic codec must preserve movement and army information.");
    }

    private static void ValidateVisibleEntityContract()
    {
        List<CoopCampaignMapPrototypeEntityState> entities =
            CreateVisibleEntities();
        Assert(
            CoopCampaignMapPrototypeContract.TryValidateVisibleEntities(
                entities,
                out string reason),
            "A bounded unique visible entity list must be accepted. Reason=" +
            reason);

        List<CoopCampaignMapPrototypeEntityState> duplicate =
            CreateVisibleEntities();
        duplicate[1].EntityId = duplicate[0].EntityId.ToUpperInvariant();
        Assert(
            !CoopCampaignMapPrototypeContract.TryValidateVisibleEntities(
                duplicate,
                out reason) &&
            reason == "visible-entity-duplicate",
            "Entity identifiers must be unique without case ambiguity.");

        CoopCampaignMapPrototypeEntityState invalid = entities[0].Clone();
        invalid.DisplayName = "Unsafe\nName";
        Assert(
            !CoopCampaignMapPrototypeContract.IsValidVisibleEntity(invalid),
            "Control characters must be rejected from visible labels.");

        invalid = entities[0].Clone();
        invalid.BannerCode = new string(
            '1',
            CoopCampaignMapPrototypeContract.MaxBannerCodeCharacters + 1);
        Assert(
            !CoopCampaignMapPrototypeContract.IsValidVisibleEntity(invalid),
            "Oversized banner codes must be rejected.");

        invalid = entities[0].Clone();
        invalid.SettlementNameplateSize =
            CoopCampaignMapPrototypeSettlementNameplateSize.Small;
        Assert(
            !CoopCampaignMapPrototypeContract.IsValidVisibleEntity(invalid),
            "A mobile party must not carry a settlement nameplate size.");

        invalid = entities[1].Clone();
        invalid.SettlementNameplateSize =
            CoopCampaignMapPrototypeSettlementNameplateSize.None;
        Assert(
            !CoopCampaignMapPrototypeContract.IsValidVisibleEntity(invalid),
            "A settlement must carry an explicit nameplate size.");

        invalid = entities[1].Clone();
        invalid.SettlementNameplateSize =
            (CoopCampaignMapPrototypeSettlementNameplateSize)4;
        Assert(
            !CoopCampaignMapPrototypeContract.IsValidVisibleEntity(invalid),
            "An undefined settlement nameplate size must be rejected.");

        invalid = entities[0].Clone();
        invalid.PartyVisualKind =
            (CoopCampaignMapPrototypePartyVisualKind)4;
        Assert(
            !CoopCampaignMapPrototypeContract.IsValidVisibleEntity(invalid),
            "An undefined party visual kind must be rejected.");

        invalid = entities[1].Clone();
        invalid.PartyVisualKind =
            CoopCampaignMapPrototypePartyVisualKind.Foot;
        invalid.VisualCharacterId = "mp_coop_light_infantry_empire_troop";
        invalid.CultureId = "empire";
        Assert(
            !CoopCampaignMapPrototypeContract.IsValidVisibleEntity(invalid),
            "A settlement must not carry party visual metadata.");

        invalid = entities[0].Clone();
        invalid.VisualCharacterId = string.Empty;
        invalid.CultureId = string.Empty;
        Assert(
            !CoopCampaignMapPrototypeContract.IsValidVisibleEntity(invalid),
            "A concrete party visual must have a character or culture fallback.");

        var oversized = new List<CoopCampaignMapPrototypeEntityState>();
        for (int index = 0;
             index <= CoopCampaignMapPrototypeContract.MaxVisibleEntities;
             index++)
        {
            CoopCampaignMapPrototypeEntityState entity = entities[1].Clone();
            entity.EntityId = "party:" + index;
            oversized.Add(entity);
        }
        Assert(
            !CoopCampaignMapPrototypeContract.TryValidateVisibleEntities(
                oversized,
                out reason) &&
            reason == "visible-entities-count",
            "The visible entity list must enforce its network cap.");
    }

    private static void ValidateVisibleEntitySnapshotAssembler()
    {
        List<CoopCampaignMapPrototypeEntityState> entities =
            CreateVisibleEntities();
        var assembler =
            new CoopCampaignMapPrototypeEntitySnapshotAssembler();
        Assert(
            assembler.TryBegin(10, 2, out List<CoopCampaignMapPrototypeEntityState> completed) &&
            completed == null,
            "A non-empty entity snapshot header must begin assembly.");
        Assert(
            assembler.TryAdd(10, 1, 2, entities[1], out completed) &&
            completed == null,
            "Out-of-order entity records must be accepted until complete.");
        Assert(
            !assembler.TryAdd(10, 1, 2, entities[1], out completed),
            "A duplicate entity record index must be rejected.");
        Assert(
            assembler.TryAdd(10, 0, 2, entities[0], out completed) &&
            completed != null &&
            completed.Count == 2 &&
            completed[0].EntityId == entities[0].EntityId &&
            completed[1].EntityId == entities[1].EntityId,
            "A completed snapshot must be emitted in header index order.");
        Assert(
            !assembler.TryBegin(10, 0, out completed),
            "A completed revision must not be replayed.");
        Assert(
            assembler.TryBegin(11, 0, out completed) &&
            completed != null &&
            completed.Count == 0,
            "An empty newer snapshot must clear the client entity set.");
    }

    private static void ValidateSettlementNameplateSizeCodec()
    {
        foreach (CoopCampaignMapPrototypeSettlementNameplateSize size in new[]
                 {
                     CoopCampaignMapPrototypeSettlementNameplateSize.Small,
                     CoopCampaignMapPrototypeSettlementNameplateSize.Medium,
                     CoopCampaignMapPrototypeSettlementNameplateSize.Large
                 })
        {
            CoopCampaignMapPrototypeHostSnapshot expected =
                CreateHostSnapshot(
                    new DateTime(
                        2026,
                        8,
                        17,
                        12,
                        0,
                        0,
                        DateTimeKind.Utc));
            expected.VisibleEntities[1].SettlementNameplateSize = size;
            string[] serialized =
                CoopCampaignMapPrototypeBridgeCodec.Serialize(expected);
            Assert(
                CoopCampaignMapPrototypeBridgeCodec.TryParse(
                    serialized,
                    out CoopCampaignMapPrototypeHostSnapshot actual,
                    out string reason),
                "Every settlement nameplate size must survive the host bridge. Reason=" +
                reason);
            Assert(
                actual.VisibleEntities[1].SettlementNameplateSize == size,
                "The host bridge must preserve the settlement nameplate size.");
        }
    }

    private static void ValidatePartyVisualCodec()
    {
        foreach (CoopCampaignMapPrototypePartyVisualKind kind in new[]
                 {
                     CoopCampaignMapPrototypePartyVisualKind.None,
                     CoopCampaignMapPrototypePartyVisualKind.Foot,
                     CoopCampaignMapPrototypePartyVisualKind.Mounted,
                     CoopCampaignMapPrototypePartyVisualKind.Caravan
                 })
        {
            CoopCampaignMapPrototypeHostSnapshot expected =
                CreateHostSnapshot(
                    new DateTime(
                        2026,
                        8,
                        18,
                        12,
                        0,
                        0,
                        DateTimeKind.Utc));
            CoopCampaignMapPrototypeEntityState party =
                expected.VisibleEntities[0];
            party.PartyVisualKind = kind;
            party.VisualCharacterId = kind ==
                                      CoopCampaignMapPrototypePartyVisualKind.None
                ? string.Empty
                : "lord_1_1";
            party.CultureId = kind ==
                              CoopCampaignMapPrototypePartyVisualKind.None
                ? string.Empty
                : "empire";
            party.HumanVisual = kind ==
                                CoopCampaignMapPrototypePartyVisualKind.None
                ? null
                : CreateHumanVisualState();
            party.MountVisual = kind ==
                                CoopCampaignMapPrototypePartyVisualKind.Mounted ||
                                kind ==
                                CoopCampaignMapPrototypePartyVisualKind.Caravan
                ? CreateMountVisualState("campaign_horse", "campaign_harness")
                : null;
            party.CaravanMountVisual = kind ==
                                       CoopCampaignMapPrototypePartyVisualKind.Caravan
                ? CreateMountVisualState("campaign_mule", "campaign_pack")
                : null;

            string[] serialized =
                CoopCampaignMapPrototypeBridgeCodec.Serialize(expected);
            Assert(
                CoopCampaignMapPrototypeBridgeCodec.TryParse(
                    serialized,
                    out CoopCampaignMapPrototypeHostSnapshot actual,
                    out string reason),
                "Every party visual kind must survive the host bridge. Reason=" +
                reason);
            CoopCampaignMapPrototypeEntityState actualParty =
                actual.VisibleEntities[0];
            Assert(
                actualParty.PartyVisualKind == kind &&
                actualParty.VisualCharacterId == party.VisualCharacterId &&
                actualParty.CultureId == party.CultureId &&
                AgentVisualsEqual(actualParty.HumanVisual, party.HumanVisual) &&
                AgentVisualsEqual(actualParty.MountVisual, party.MountVisual) &&
                AgentVisualsEqual(
                    actualParty.CaravanMountVisual,
                    party.CaravanMountVisual),
                "The host bridge must preserve party visual metadata.");
        }
    }

    private static void ValidateExactPartyVisualContract()
    {
        CoopCampaignMapPrototypeEntityState exact =
            CreateVisibleEntities()[0];
        Assert(
            CoopCampaignMapPrototypeContract.IsValidVisibleEntity(exact),
            "An exact main-hero visual with body, banner and mount must be valid.");
        Assert(
            exact.HumanVisual.EquipmentItemIds.Length ==
                CoopCampaignMapPrototypeContract.EquipmentSlotCount,
            "An exact human visual must contain all 12 equipment slots.");

        CoopCampaignMapPrototypeEntityState clone = exact.Clone();
        exact.HumanVisual.EquipmentItemIds[0] = "mutated_item";
        exact.MountVisual.EquipmentItemIds[10] = "mutated_horse";
        Assert(
            clone.HumanVisual.EquipmentItemIds[0] == "campaign_sword" &&
            clone.MountVisual.EquipmentItemIds[10] == "campaign_horse",
            "Entity cloning must deeply clone exact visual equipment arrays.");

        CoopCampaignMapPrototypeEntityState invalid = clone.Clone();
        invalid.HumanVisual.EquipmentItemIds = new string[
            CoopCampaignMapPrototypeContract.EquipmentSlotCount - 1];
        Assert(
            !CoopCampaignMapPrototypeContract.IsValidVisibleEntity(invalid),
            "An exact visual with a truncated equipment layout must be rejected.");

        invalid = clone.Clone();
        invalid.HumanVisual.BodyProperties = new string(
            'x',
            CoopCampaignMapPrototypeContract.MaxBodyPropertiesCharacters + 1);
        Assert(
            !CoopCampaignMapPrototypeContract.IsValidVisibleEntity(invalid),
            "Oversized body properties must be rejected before transport.");

        invalid = clone.Clone();
        invalid.CaravanMountVisual = CreateMountVisualState(
            "campaign_mule",
            "campaign_pack");
        Assert(
            CoopCampaignMapPrototypeContract.IsValidVisibleEntity(invalid),
            "A bounded separate caravan mount descriptor must remain transport-safe.");
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
                NormalizedTimeOfDay = 750000,
                SeasonTimeFactor = 250000,
                SampleTimeMilliseconds = 12345,
                IsMoving = true,
                IsActive = true,
                VisibleEntitiesRevision = 4,
                CatalogRevision = 5,
                DynamicRevision = 6,
                VisibleEntities = CreateVisibleEntities(),
                Camera = CreateCameraState(),
                UpdatedUtc = updatedUtc
            };
    }

    private static List<CoopCampaignMapPrototypeEntityState>
        CreateVisibleEntities()
    {
        return new List<CoopCampaignMapPrototypeEntityState>
        {
            new CoopCampaignMapPrototypeEntityState
            {
                EntityId = "main:player",
                DisplayName = "Головний загін",
                Kind = CoopCampaignMapPrototypeEntityKind.MainParty,
                SettlementNameplateSize =
                    CoopCampaignMapPrototypeSettlementNameplateSize.None,
                NormalizedX = 250000,
                NormalizedY = 750000,
                Heading = 125000,
                PartySize = 30,
                PrimaryColor = 0x78563412u,
                SecondaryColor = 0x12345678u,
                BannerCode =
                    "11.163.166.1528.1528.764.764.1.0.0.133.171.171.483.483.764.764.0.0.0",
                VisualCharacterId = "lord_1_1",
                CultureId = "empire",
                PartyVisualKind =
                    CoopCampaignMapPrototypePartyVisualKind.Mounted,
                HumanVisual = CreateHumanVisualState(),
                MountVisual = CreateMountVisualState(
                    "campaign_horse",
                    "campaign_harness")
            },
            new CoopCampaignMapPrototypeEntityState
            {
                EntityId = "settlement:town_V1",
                DisplayName = "Vostrum",
                Kind = CoopCampaignMapPrototypeEntityKind.Settlement,
                SettlementNameplateSize =
                    CoopCampaignMapPrototypeSettlementNameplateSize.Large,
                NormalizedX = 260000,
                NormalizedY = 740000,
                Heading = 0,
                PartySize = 0,
                PrimaryColor = uint.MaxValue,
                SecondaryColor = 0xFF102030u,
                BannerCode = "24.193.116.1536.1536.768.768.1.0.0",
                VisualCharacterId = string.Empty,
                CultureId = string.Empty,
                PartyVisualKind =
                    CoopCampaignMapPrototypePartyVisualKind.None
            }
        };
    }

    private static bool VisibleEntitiesEqual(
        IReadOnlyList<CoopCampaignMapPrototypeEntityState> left,
        IReadOnlyList<CoopCampaignMapPrototypeEntityState> right)
    {
        if (left == null || right == null || left.Count != right.Count)
            return false;
        for (int index = 0; index < left.Count; index++)
        {
            CoopCampaignMapPrototypeEntityState leftEntity = left[index];
            CoopCampaignMapPrototypeEntityState rightEntity = right[index];
            if (leftEntity == null ||
                rightEntity == null ||
                leftEntity.EntityId != rightEntity.EntityId ||
                leftEntity.DisplayName != rightEntity.DisplayName ||
                leftEntity.Kind != rightEntity.Kind ||
                leftEntity.SettlementNameplateSize !=
                    rightEntity.SettlementNameplateSize ||
                leftEntity.NormalizedX != rightEntity.NormalizedX ||
                leftEntity.NormalizedY != rightEntity.NormalizedY ||
                leftEntity.Heading != rightEntity.Heading ||
                leftEntity.PartySize != rightEntity.PartySize ||
                leftEntity.PrimaryColor != rightEntity.PrimaryColor ||
                leftEntity.SecondaryColor != rightEntity.SecondaryColor ||
                leftEntity.BannerCode != rightEntity.BannerCode ||
                leftEntity.VisualCharacterId !=
                    rightEntity.VisualCharacterId ||
                leftEntity.CultureId != rightEntity.CultureId ||
                leftEntity.PartyVisualKind != rightEntity.PartyVisualKind ||
                !AgentVisualsEqual(
                    leftEntity.HumanVisual,
                    rightEntity.HumanVisual) ||
                !AgentVisualsEqual(
                    leftEntity.MountVisual,
                    rightEntity.MountVisual) ||
                !AgentVisualsEqual(
                    leftEntity.CaravanMountVisual,
                    rightEntity.CaravanMountVisual))
            {
                return false;
            }
        }
        return true;
    }

    private static CoopCampaignMapPrototypeAgentVisualState
        CreateHumanVisualState()
    {
        string[] itemIds = CreateEmptyEquipmentLayout();
        itemIds[0] = "campaign_sword";
        itemIds[4] = "campaign_banner_small";
        itemIds[5] = "campaign_helmet";
        itemIds[6] = "campaign_armor";
        itemIds[10] = "campaign_horse";
        itemIds[11] = "campaign_harness";
        return new CoopCampaignMapPrototypeAgentVisualState
        {
            BodyProperties = "<BodyProperties version=\"4\" />",
            IsFemale = false,
            Race = 0,
            SkeletonType = 0,
            RightWieldedItemIndex = 0,
            LeftWieldedItemIndex = 4,
            MountCreationKey = string.Empty,
            HasBanner = true,
            AddColorRandomness = false,
            EquipmentItemIds = itemIds
        };
    }

    private static CoopCampaignMapPrototypeAgentVisualState
        CreateMountVisualState(string horseItemId, string harnessItemId)
    {
        string[] itemIds = CreateEmptyEquipmentLayout();
        itemIds[10] = horseItemId;
        itemIds[11] = harnessItemId;
        return new CoopCampaignMapPrototypeAgentVisualState
        {
            BodyProperties = string.Empty,
            IsFemale = false,
            Race = 0,
            SkeletonType = 0,
            RightWieldedItemIndex = -1,
            LeftWieldedItemIndex = -1,
            MountCreationKey = "mount-key-1084",
            HasBanner = false,
            AddColorRandomness = false,
            EquipmentItemIds = itemIds
        };
    }

    private static string[] CreateEmptyEquipmentLayout()
    {
        var itemIds = new string[
            CoopCampaignMapPrototypeContract.EquipmentSlotCount];
        for (int slot = 0; slot < itemIds.Length; slot++)
            itemIds[slot] = string.Empty;
        return itemIds;
    }

    private static bool AgentVisualsEqual(
        CoopCampaignMapPrototypeAgentVisualState left,
        CoopCampaignMapPrototypeAgentVisualState right)
    {
        if (left == null || right == null)
            return left == right;
        if (left.BodyProperties != right.BodyProperties ||
            left.IsFemale != right.IsFemale ||
            left.Race != right.Race ||
            left.SkeletonType != right.SkeletonType ||
            left.RightWieldedItemIndex != right.RightWieldedItemIndex ||
            left.LeftWieldedItemIndex != right.LeftWieldedItemIndex ||
            left.MountCreationKey != right.MountCreationKey ||
            left.HasBanner != right.HasBanner ||
            left.AddColorRandomness != right.AddColorRandomness ||
            left.EquipmentItemIds == null ||
            right.EquipmentItemIds == null ||
            left.EquipmentItemIds.Length != right.EquipmentItemIds.Length)
        {
            return false;
        }

        for (int slot = 0; slot < left.EquipmentItemIds.Length; slot++)
        {
            if (left.EquipmentItemIds[slot] != right.EquipmentItemIds[slot])
                return false;
        }
        return true;
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
