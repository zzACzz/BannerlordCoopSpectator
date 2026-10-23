using System;
using System.Collections.Generic;
using System.IO;
using CoopSpectator.Infrastructure;

internal static class Program
{
    private static int Main()
    {
        try
        {
            ValidateProtocolVersion();
            ControlHintTests.Run();
            PartyAnimationTests.Run();
            SiegeTentTests.Run();
            SiegeOverlayTests.Run();
            SiegeDecalTests.Run();
            SiegeAnimationTests.Run();
            SiegeMachineTests.Run();
            SiegeProjectileTests.Run();
            SiegeShotIdentityTests.Run();
            SiegeProjectilePlaybackTests.Run();
            SiegeFlightTests.Run();
            ValidateSiegeTentTransport();
            MapBarHintIdentityTests.Run();
            RundownPresentationTests.Run();
            ValidatePartyNameplateColor();
            ValidateHostBannerColorPreservation();
            ValidateHiddenPartyInformation();
            ValidateNativePartyLabelText();
            ValidateNativePartyPresentation();
            PartyBodyTests.Run();
            PickResolverTests.Run();
            HoverDecalTests.Run();
            MovementTargetTests.Run();
            MovementTargetTransportTests.Run();
            TooltipPayloadTests.Run();
            TooltipInterestTests.Run();
            TooltipPeerGateTests.Run();
            TooltipWireTests.Run();
            TooltipTargetTests.Run();
            TooltipRelayTests.Run();
            TooltipChunkTests.Run();
            TooltipPresenterTests.Run();
            TooltipTextTransactionTests.Run();
            TooltipThreadStateTests.Run();
            ValidateReplicaStateProjection();
            ValidateSettlementPresentation();
            SettlementRelationTests.Run();
            SettlementMapEventTests.Run();
            ReplicaCameraTests.Run();
            EscapeInputTests.Run();
            LobbyLifetimeTests.Run();
            SettlementEventDisclosureTests.Run();
            SettlementMarkerOrderTests.Run();
            SettlementMarkerTransportTests.Run();
            SettlementGeometryTests.Run();
            SettlementPartyBadgeTests.Run();
            ValidateSettlementLevels();
            ValidateConversationContract();
            ConversationTableauTests.Run();
            ConversationMissionSceneTests.Run();
            ConversationPublicationLeaseTests.Run();
            ConversationPayloadTests.Run();
            ConversationHudTests.Run();
            ConversationVisualLifetimeTests.Run();
            ConversationPoseTests.Run();
            MissionLifecycleTests.Run();
            MenuPresentationTests.Run();
            MapBarPresentationTests.Run();
            PortraitPoseEvidenceTests.Run();
            BattleMarkerTests.Run();
            MissionOpeningTests.Run();
            MissionChannelTests.Run();
            ValidateConversationSnapshotAssembler();
            ValidateVisibilityPolicy();
            ValidateInteractionPolicy();
            ValidateInformationPanelPosition();
            ValidateDynamicBatchTransportPolicy();
            ValidateDynamicDeltaPolicy();
            ValidateCatalogDeltaPolicy();
            ValidateCatalogBinaryChunkTransport();
            ValidateLargeCatalogChunkTransport();
            ValidateCatalogTransferSupersessionPolicy();
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
            ValidateTransientBridgeGapFallback();
            ValidateSceneOwnerResolution();
            ValidateDedicatedBattleObserverSuppression();
            ValidateClientSceneInitializationParity();
            ValidateClientInteractionSourceContract();
            ValidateMarkerReplicationSourceContract();
            ValidateConversationReplicationSourceContract();
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
            CoopCampaignMapPrototypeContract.ProtocolVersion == 60 &&
            CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion == 55 &&
            CoopCampaignMapCatalogBinarySerializer.SchemaVersion == 10,
            "Siege trajectory payload requires coherent protocol/bridge/catalog schema 59/54/10.");
    }

    private static void ValidateSiegeTentTransport()
    {
        var tent = new CoopCampaignMapSiegeTent {
            TentFrame = new[] { 1.2f, 0f, 0f, 0f, 1.2f, 0f, 0f, 0f, 1.2f, 0f, 0f, 0f }
        };
        Assert(CoopCampaignMapSiegeTentCodec.TryEncode(tent, out var payload), "Encode tent fixture");
        string encoded = Convert.ToBase64String(payload);
        var party = CreateCatalogParty(0);
        party.PartyVisualKind = CoopCampaignMapPrototypePartyVisualKind.SiegeTent;
        party.HumanVisual = party.MountVisual = party.CaravanMountVisual = null;
        party.SiegeTentPresentation = encoded;
        Assert(CoopCampaignMapPrototypeContract.IsValidCatalogEntity(party) &&
            CoopCampaignMapPrototypeContract.IsCompletePartyVisualState(party), "Tent is a complete actor-free presentation");
        Assert(party.Clone().SiegeTentPresentation == encoded, "Catalog clone preserves tent");
        var changed = party.Clone(); changed.SiegeTentPresentation = string.Empty;
        Assert(!CoopCampaignMapPrototypeCatalogDeltaPolicy.AreEquivalent(party, changed) &&
            !CoopCampaignMapPrototypeContract.IsValidCatalogEntity(changed), "Missing tent invalidates catalog");
        changed = party.Clone(); changed.PartyVisualKind = CoopCampaignMapPrototypePartyVisualKind.Foot;
        Assert(!CoopCampaignMapPrototypeContract.IsValidCatalogEntity(changed), "Foot figure cannot carry tent");
        var host = CreateHostSnapshot(DateTime.UtcNow);
        var visible = host.VisibleEntities[0];
        visible.PartyVisualKind = CoopCampaignMapPrototypePartyVisualKind.SiegeTent;
        visible.HumanVisual = visible.MountVisual = visible.CaravanMountVisual = null;
        visible.SiegeTentPresentation = encoded;
        Assert(visible.Clone().SiegeTentPresentation == encoded, "Visible clone preserves tent");
        Assert(CoopCampaignMapPrototypeBridgeCodec.TryParse(
            CoopCampaignMapPrototypeBridgeCodec.Serialize(host), out var parsed, out var reason) &&
            parsed.VisibleEntities[0].SiegeTentPresentation == encoded, "Host bridge preserves tent: " + reason);
        var catalog = new CoopCampaignMapPrototypeCatalogSnapshot {
            SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
            SessionId = host.SessionId, Revision = 1, UpdatedUtc = host.UpdatedUtc,
            Entities = new List<CoopCampaignMapPrototypeCatalogEntityState> { party }
        };
        Assert(CoopCampaignMapPrototypeBridgeCodec.TryParseCatalog(
            CoopCampaignMapPrototypeBridgeCodec.SerializeCatalog(catalog), out var decodedCatalog, out reason) &&
            decodedCatalog.Entities[0].SiegeTentPresentation == encoded, "Catalog bridge preserves tent: " + reason);
        Assert(CoopCampaignMapCatalogBinarySerializer.TrySerialize(1, catalog.Entities, out var bytes, out reason) &&
            CoopCampaignMapCatalogBinarySerializer.TryDeserialize(bytes, out _, out var decoded, out reason) &&
            decoded[0].SiegeTentPresentation == encoded, "Binary catalog preserves tent: " + reason);
        Assert(!CoopCampaignMapSiegeTentCodec.TryDecodeText(encoded + " ", out _), "Noncanonical text denied");
        Assert(!CoopCampaignMapSiegeTentCodec.IsValidForEntity(encoded,
            CoopCampaignMapPrototypeEntityKind.Settlement, CoopCampaignMapPrototypePartyVisualKind.SiegeTent, false),
            "Settlement cannot be a party tent");
        Assert(!CoopCampaignMapSiegeTentCodec.IsValidForEntity(encoded,
            CoopCampaignMapPrototypeEntityKind.MobileParty, CoopCampaignMapPrototypePartyVisualKind.SiegeTent, true),
            "Tent cannot coexist with actors");
    }

    private static void ValidateSettlementLevels()
    {
        var host = CreateHostSnapshot(DateTime.UtcNow);
        var settlement = host.VisibleEntities.Find(e => e.Kind == CoopCampaignMapPrototypeEntityKind.Settlement);
        Assert(settlement != null, "Settlement fixture required.");
        var catalogEntity = new CoopCampaignMapPrototypeCatalogEntityState {
            EntityId = settlement.EntityId, DisplayName = settlement.DisplayName,
            Kind = settlement.Kind, SettlementNameplateSize = settlement.SettlementNameplateSize,
            SettlementKind = CoopCampaignMapPrototypeSettlementKind.Town,
            BannerCode = "", VisualCharacterId = "", CultureId = "",
            FactionId = "", FactionName = "", OwnerName = "", LeaderName = "", ArmyId = "", ArmyName = ""
        };
        var catalog = new CoopCampaignMapPrototypeCatalogSnapshot {
            SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
            SessionId = host.SessionId, Revision = 3, UpdatedUtc = host.UpdatedUtc,
            Entities = new List<CoopCampaignMapPrototypeCatalogEntityState> { catalogEntity }
        };
        foreach (int flags in new[] { 0, 1, 9, 10, 12, 17, 18, 20, 33, 34, 36 })
        {
            settlement.SettlementLevelFlags = flags;
            catalogEntity.SettlementLevelFlags = flags;
            Assert(settlement.Clone().SettlementLevelFlags == flags && catalogEntity.Clone().SettlementLevelFlags == flags,
                "Both clones must retain level flags.");
            Assert(CoopCampaignMapPrototypeBridgeCodec.TryParse(
                CoopCampaignMapPrototypeBridgeCodec.Serialize(host), out var parsedHost, out var reason) &&
                parsedHost.VisibleEntities.Find(e => e.EntityId == settlement.EntityId).SettlementLevelFlags == flags,
                "Visible bridge must retain level flags: " + reason);
            Assert(CoopCampaignMapPrototypeBridgeCodec.TryParseCatalog(
                CoopCampaignMapPrototypeBridgeCodec.SerializeCatalog(catalog), out var parsedCatalog, out reason) &&
                parsedCatalog.Entities[0].SettlementLevelFlags == flags,
                "Catalog bridge must retain level flags: " + reason);
            Assert(CoopCampaignMapCatalogBinarySerializer.TrySerialize(3, catalog.Entities, out var bytes, out reason) &&
                CoopCampaignMapCatalogBinarySerializer.TryDeserialize(bytes, out _, out var parsedBinary, out reason) &&
                parsedBinary[0].SettlementLevelFlags == flags, "Binary catalog round trip: " + reason);
            var dynamicEntity = new CoopCampaignMapPrototypeDynamicEntityState { EntityId = settlement.EntityId, IsVisible = true };
            Assert(CoopCampaignMapReplicaStateProjection.Merge(catalogEntity, dynamicEntity).SettlementLevelFlags == flags,
                "Full/late-join/delta projection must retain flags.");
            var changed = catalogEntity.Clone();
            changed.SettlementLevelFlags = flags == 9 ? 10 : 9;
            Assert(CoopCampaignMapPrototypeCatalogDeltaPolicy.BuildDelta(new[] { catalogEntity }, new[] { changed }).Count == 1,
                "Level-only changes must emit a catalog delta.");
            Array.Copy(BitConverter.GetBytes(5), 0, bytes, 4, 4);
            Assert(!CoopCampaignMapCatalogBinarySerializer.TryDeserialize(bytes, out _, out _, out reason),
                "Old schema must not be parsed as the new layout.");
        }
        var missing = new List<string>(CoopCampaignMapPrototypeBridgeCodec.SerializeCatalog(catalog));
        missing.RemoveAll(line => line.StartsWith("Entity.0.SettlementLevelFlags=", StringComparison.Ordinal));
        Assert(!CoopCampaignMapPrototypeBridgeCodec.TryParseCatalog(missing, out _, out _), "Missing catalog flags must fail closed.");
        foreach (string walls in new[] { "", "0", "01", "10", "11", new string('1', 32) })
        foreach (int preparation in new[] { 0, 1, 2 })
        {
            settlement.SettlementWallStates = catalogEntity.SettlementWallStates = walls;
            settlement.SettlementPreparationState = catalogEntity.SettlementPreparationState = preparation;
            Assert(settlement.Clone().SettlementWallStates == walls && catalogEntity.Clone().SettlementWallStates == walls &&
                settlement.Clone().SettlementPreparationState == preparation && catalogEntity.Clone().SettlementPreparationState == preparation,
                "Presentation clones must be exact.");
            Assert(CoopCampaignMapPrototypeBridgeCodec.TryParse(CoopCampaignMapPrototypeBridgeCodec.Serialize(host),
                out var parsedHost, out _) && parsedHost.VisibleEntities.Find(e => e.EntityId == settlement.EntityId).SettlementWallStates == walls &&
                parsedHost.VisibleEntities.Find(e => e.EntityId == settlement.EntityId).SettlementPreparationState == preparation,
                "Full bridge must preserve wall/preparation states.");
            Assert(CoopCampaignMapPrototypeBridgeCodec.TryParseCatalog(CoopCampaignMapPrototypeBridgeCodec.SerializeCatalog(catalog),
                out var parsedCatalog, out _) && parsedCatalog.Entities[0].SettlementWallStates == walls &&
                parsedCatalog.Entities[0].SettlementPreparationState == preparation, "Catalog bridge presentation round trip.");
            Assert(CoopCampaignMapCatalogBinarySerializer.TrySerialize(3, catalog.Entities, out var data, out _) &&
                CoopCampaignMapCatalogBinarySerializer.TryDeserialize(data, out _, out var parsedBinary, out _) &&
                parsedBinary[0].SettlementWallStates == walls && parsedBinary[0].SettlementPreparationState == preparation,
                "Bounded binary presentation round trip.");
            var merged = CoopCampaignMapReplicaStateProjection.Merge(catalogEntity,
                new CoopCampaignMapPrototypeDynamicEntityState { EntityId = settlement.EntityId, IsVisible = true });
            Assert(merged.SettlementWallStates == walls && merged.SettlementPreparationState == preparation,
                "Late-join/incremental projection preserves presentation.");
            var changed = catalogEntity.Clone();
            changed.SettlementPreparationState = (preparation + 1) % 3;
            Assert(CoopCampaignMapPrototypeCatalogDeltaPolicy.BuildDelta(new[] { catalogEntity }, new[] { changed }).Count == 1,
                "Preparation-only transition emits delta.");
            changed = catalogEntity.Clone();
            changed.SettlementWallStates = walls == "01" ? "10" : "01";
            Assert(CoopCampaignMapPrototypeCatalogDeltaPolicy.BuildDelta(new[] { catalogEntity }, new[] { changed }).Count == 1,
                "Wall-only transition emits delta.");
        }
        foreach (string field in new[] { "SettlementWallStates", "SettlementPreparationState" })
        {
            missing = new List<string>(CoopCampaignMapPrototypeBridgeCodec.SerializeCatalog(catalog));
            missing.RemoveAll(line => line.StartsWith("Entity.0." + field + "=", StringComparison.Ordinal));
            Assert(!CoopCampaignMapPrototypeBridgeCodec.TryParseCatalog(missing, out _, out _), "Required presentation field missing.");
        }
        foreach (string walls in new[] { null, "2", "0 1", "-1", new string('0', 33) })
        {
            settlement.SettlementWallStates = catalogEntity.SettlementWallStates = walls;
            Assert(!CoopCampaignMapPrototypeContract.IsValidVisibleEntity(settlement) &&
                !CoopCampaignMapPrototypeContract.IsValidCatalogEntity(catalogEntity), "Malformed walls must be rejected.");
        }
        settlement.SettlementWallStates = catalogEntity.SettlementWallStates = "01";
        foreach (int preparation in new[] { -1, 3, int.MaxValue })
        {
            settlement.SettlementPreparationState = catalogEntity.SettlementPreparationState = preparation;
            Assert(!CoopCampaignMapPrototypeContract.IsValidVisibleEntity(settlement) &&
                !CoopCampaignMapPrototypeContract.IsValidCatalogEntity(catalogEntity), "Malformed preparation must be rejected.");
        }
        settlement.SettlementPreparationState = catalogEntity.SettlementPreparationState = 1;
        foreach (int flags in new[] { -1, 2, 3, 4, 8, 16, 32, 63, 64, int.MaxValue })
        {
            catalogEntity.SettlementLevelFlags = flags;
            settlement.SettlementLevelFlags = flags;
            Assert(!CoopCampaignMapPrototypeContract.IsValidCatalogEntity(catalogEntity) &&
                !CoopCampaignMapPrototypeContract.IsValidVisibleEntity(settlement), "Invalid flag combination must be rejected.");
        }
        var party = CreateCatalogParty(0);
        party.SettlementLevelFlags = 9;
        Assert(!CoopCampaignMapPrototypeContract.IsValidCatalogEntity(party), "Parties cannot carry settlement geometry.");
        party.SettlementLevelFlags = 0;
        party.SettlementWallStates = "0";
        Assert(!CoopCampaignMapPrototypeContract.IsValidCatalogEntity(party), "Party wall state rejected.");
        party.SettlementWallStates = "";
        party.SettlementPreparationState = 1;
        Assert(!CoopCampaignMapPrototypeContract.IsValidCatalogEntity(party), "Party preparation state rejected.");
        foreach (string file in new[] { "CoopCampaignMapPrototypeNetworkMessages.cs", "CoopCampaignMapReplicaNetworkMessages.cs" })
        {
            string wire = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "Network", "Messages", file));
            Assert(wire.Contains("SettlementLevelFlags = ReadIntFromPacket(UnitCompression, ref valid)") &&
                wire.Contains(".SettlementLevelFlags, UnitCompression)"), "Both wire paths must preserve flags: " + file);
            Assert(wire.Contains("SettlementWallStates = ReadStringFromPacket(ref valid)") &&
                wire.Contains(".SettlementWallStates ?? string.Empty)") &&
                wire.Contains("SettlementPreparationState = ReadIntFromPacket(UnitCompression, ref valid)") &&
                wire.Contains(".SettlementPreparationState, UnitCompression)"), "Both native wire paths preserve presentation: " + file);
        }
    }

    private static void ValidateNativePartyPresentation()
    {
        for (int mask = 0; mask < 64; mask++)
        {
            Assert(CoopCampaignMapPartyLabelPolicy.ResolveFlags(
                (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0,
                (mask & 8) != 0, (mask & 16) != 0, (mask & 32) != 0) == mask,
                "Each native presentation flag must survive independently: " + mask);
            foreach (bool main in new[] { false, true })
                Assert(CoopCampaignMapPartyLabelPolicy.ShouldShowNameplate(main, mask) ==
                    ((mask & 6) == 0 && (main || (mask & 32) == 0)),
                    "Native attached/settlement/raft label visibility differs: " + mask);
        }
        Assert(CoopCampaignMapPartyLabelPolicy.ResolveName(true, "Army", "Leader", "Party") == "Army" &&
               CoopCampaignMapPartyLabelPolicy.ResolveName(false, "Army", "Leader", "Party") == "Leader" &&
               CoopCampaignMapPartyLabelPolicy.ResolveName(false, "Army", null, "Party") == "Party",
            "Native name precedence must distinguish attached army, leader and unnamed-leader party.");
        Assert(!CoopCampaignMapPrototypeContract.IsValidPartyPresentation(64, "4.8") &&
               !CoopCampaignMapPrototypeContract.IsValidPartyPresentation(-1, "4.8") &&
               !CoopCampaignMapPrototypeContract.IsValidPartyPresentation(0, "4\n8") &&
               !CoopCampaignMapPrototypeContract.IsValidPartyPresentation(0, new string('я', 17)) &&
               CoopCampaignMapPrototypeContract.IsValidPartyPresentation(63, "4,8"),
            "Presentation flags and localized speed must be bounded.");

        var host = CreateHostSnapshot(DateTime.UtcNow);
        var party = host.VisibleEntities[0];
        party.PartyNameplateNameText = "Армія ватажка";
        party.PartyMovementSpeedText = "4,8";
        party.PartyNameplateFlags = 25;
        Assert(CoopCampaignMapPrototypeBridgeCodec.TryParse(
            CoopCampaignMapPrototypeBridgeCodec.Serialize(host), out var decodedHost, out var reason) &&
            decodedHost.VisibleEntities[0].PartyNameplateNameText == party.PartyNameplateNameText &&
            decodedHost.VisibleEntities[0].PartyMovementSpeedText == "4,8" &&
            decodedHost.VisibleEntities[0].PartyNameplateFlags == 25 &&
            party.Clone().PartyNameplateFlags == 25,
            "Visible presentation must round-trip and clone. Reason=" + reason);
        var catalogParty = CreateCatalogParty(1);
        catalogParty.PartyNameplateNameText = party.PartyNameplateNameText;
        var catalog = new CoopCampaignMapPrototypeCatalogSnapshot {
            SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
            SessionId = host.SessionId, Revision = 1, UpdatedUtc = host.UpdatedUtc,
            Entities = new List<CoopCampaignMapPrototypeCatalogEntityState> { catalogParty }
        };
        Assert(CoopCampaignMapPrototypeBridgeCodec.TryParseCatalog(
            CoopCampaignMapPrototypeBridgeCodec.SerializeCatalog(catalog), out var decodedCatalog, out reason) &&
            decodedCatalog.Entities[0].PartyNameplateNameText == party.PartyNameplateNameText &&
            CoopCampaignMapCatalogBinarySerializer.TrySerialize(1, catalog.Entities, out var bytes, out reason) &&
            CoopCampaignMapCatalogBinarySerializer.TryDeserialize(bytes, out _, out var decodedBinary, out reason) &&
            decodedBinary[0].PartyNameplateNameText == party.PartyNameplateNameText,
            "Native caption must cross both catalog formats. Reason=" + reason);
        var renamed = catalogParty.Clone();
        renamed.PartyNameplateNameText = "Other leader";
        Assert(CoopCampaignMapPrototypeCatalogDeltaPolicy.BuildDelta(new[] { catalogParty }, new[] { renamed }).Count == 1,
            "Caption-only changes must invalidate the catalog.");

        var dynamicState = new CoopCampaignMapPrototypeDynamicEntityState {
            EntityId = catalogParty.EntityId, IsVisible = true,
            PartyMovementSpeedText = "4,8", PartyNameplateFlags = 25
        };
        var dynamicSnapshot = new CoopCampaignMapPrototypeDynamicSnapshot {
            SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
            SessionId = host.SessionId, Revision = 1, UpdatedUtc = host.UpdatedUtc,
            Entities = new List<CoopCampaignMapPrototypeDynamicEntityState> { dynamicState }
        };
        var lines = CoopCampaignMapPrototypeBridgeCodec.SerializeDynamic(dynamicSnapshot);
        Assert(CoopCampaignMapPrototypeBridgeCodec.TryParseDynamic(lines, out var decodedDynamic, out reason) &&
            decodedDynamic.Entities[0].PartyNameplateFlags == 25 &&
            decodedDynamic.Entities[0].PartyMovementSpeedText == "4,8",
            "Dynamic presentation must round-trip. Reason=" + reason);
        foreach (string name in new[] { "PartyMovementSpeedText", "PartyNameplateFlags" })
        {
            var missing = new List<string>(lines);
            missing.RemoveAll(line => line.StartsWith("Entity.0." + name + "=", StringComparison.Ordinal));
            Assert(!CoopCampaignMapPrototypeBridgeCodec.TryParseDynamic(missing, out _, out _),
                "Missing presentation data must fail admission: " + name);
        }
        var changed = dynamicState.Clone();
        changed.PartyMovementSpeedText = "4,9";
        Assert(CoopCampaignMapPrototypeDynamicDeltaPolicy.BuildDelta(new[] { dynamicState }, new[] { changed }).Count == 1,
            "Speed-only changes must propagate.");
        for (int bit = 1; bit <= 32; bit <<= 1)
        {
            changed = dynamicState.Clone();
            changed.PartyNameplateFlags ^= bit;
            Assert(CoopCampaignMapPrototypeDynamicDeltaPolicy.BuildDelta(new[] { dynamicState }, new[] { changed }).Count == 1,
                "Each status change must propagate: " + bit);
        }
        string root = FindRepositoryRoot();
        string overlay = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapPrototypeEntityOverlayVM.cs"));
        Assert(overlay.Contains("FullName = isSettlement ?") && overlay.Contains("entity.PartyNameplateNameText") &&
               overlay.Contains("MovementSpeedText = entity.PartyMovementSpeedText") &&
               overlay.Contains("ShouldShowNameplate(_isMainParty, _partyNameplateFlags)") &&
               overlay.Contains("IsVisibleOnMap = modelVisible &&") &&
               overlay.Contains("return modelVisible;") &&
               !overlay.Contains("public bool IsArmy => false") &&
               !overlay.Contains("public bool IsDisorganized => false") &&
               !overlay.Contains("public bool IsCurrentlyAtSea => false"),
            "The native widget must receive caption/speed/status and correct label visibility.");
        Assert(overlay.Contains("DistanceToCamera = worldPosition.Distance(camera.Position);") &&
               !overlay.Contains("DistanceToCamera = depth;"),
            "Native party label scaling must use spatial distance, not camera projection depth.");
        string missionView = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapPrototypeMissionView.cs"));
        Assert(overlay.Contains("internal void SetFullNamesVisible(bool visible)") &&
               overlay.Contains("nameplate.SetFullNameVisible(visible)") &&
               !overlay.Contains("ShouldShowFullName = isSettlement") &&
               missionView.Contains("_entityOverlayViewModel?.SetFullNamesVisible(") &&
               missionView.Contains("_replicaInfoLayer.Input.IsGameKeyDown(5)") &&
               missionView.Contains("HotKeyManager.GetCategory(\"MapHotKeyCategory\")"),
            "Source wiring: party label expansion must use native game key 5 and not be reset by identity updates.");
        Assert(missionView.Contains("private const float PartyNameplateHeadHeight = 0.8f;") &&
               missionView.Contains("replica.WorldPosition + Vec3.Up * PartyNameplateHeadHeight;") &&
               !missionView.Contains("Vec3.Up * (markerScale * 1.2f)"),
            "Party head anchors must keep the native 0.8-world-unit offset independently of map bounds.");
        foreach (string file in new[] { "CoopCampaignMapPrototypeNetworkMessages.cs", "CoopCampaignMapReplicaNetworkMessages.cs" })
        {
            string wire = File.ReadAllText(Path.Combine(root, "Network", "Messages", file));
            Assert(wire.Contains("PartyMovementSpeedText = ReadStringFromPacket(ref valid)") &&
                   wire.Contains("PartyNameplateFlags = ReadIntFromPacket(PartyFlagsCompression, ref valid)") &&
                   wire.Contains("PartyMovementSpeedText ?? string.Empty)") &&
                   wire.Contains("PartyFlagsCompression);"),
                "Presentation must cross both wire directions: " + file);
        }
    }

    private static void ValidateNativePartyLabelText()
    {
        Assert(CoopCampaignMapPartyLabelPolicy.ResolveCountText(false, 0) == "0" &&
               CoopCampaignMapPartyLabelPolicy.ResolveCountText(false, 73) == "73" &&
               CoopCampaignMapPartyLabelPolicy.ResolveCountText(true, 73) == "?",
            "Native healthy labels show known zero explicitly and redact hidden counts.");
        for (int mask = 0; mask < 8; mask++)
        {
            bool hidden = (mask & 1) != 0;
            int wounded = (mask & 2) == 0 ? 0 : 7;
            int prisoners = (mask & 4) == 0 ? 0 : 13;
            int formatted = 0;
            string actual = CoopCampaignMapPartyLabelPolicy.ResolveExtraInfoText(
                hidden, wounded, prisoners,
                n => { formatted++; return "+" + n + "поран."; },
                n => { formatted++; return "+" + n + "полон."; });
            string expected = (wounded == 0 ? "" : hidden ? "?" : "+7поран.") +
                (prisoners == 0 ? "" : hidden ? "?" : "+13полон.");
            Assert(actual == expected && (!hidden || formatted == 0),
                "Host-localized suffixes and hidden-presence markers must match native: " + mask);
        }
        Assert(!CoopCampaignMapPrototypeContract.IsValidPartyNameplateText(true, "73", "") &&
               !CoopCampaignMapPrototypeContract.IsValidPartyNameplateText(true, "?", "+7w") &&
               CoopCampaignMapPrototypeContract.IsValidPartyNameplateText(true, "?", "??") &&
               !CoopCampaignMapPrototypeContract.IsValidPartyNameplateText(false, "-1", "") &&
               !CoopCampaignMapPrototypeContract.IsValidPartyNameplateText(false, "<b>1</b>", "") &&
               !CoopCampaignMapPrototypeContract.IsValidPartyNameplateText(false, "1", "line\nbreak") &&
               !CoopCampaignMapPrototypeContract.IsValidPartyNameplateText(false, "1", new string('я', 65)) &&
               CoopCampaignMapPrototypeContract.IsValidPartyNameplateText(false, "1", new string('я', 64)),
            "Labels must reject hidden numbers, malformed counts and excessive UTF-8 bytes.");

        var host = CreateHostSnapshot(DateTime.UtcNow);
        var party = host.VisibleEntities[0];
        party.PartyNameplateCountText = "73";
        party.PartyNameplateExtraInfoText = "+7поран.+13полон.";
        Assert(CoopCampaignMapPrototypeBridgeCodec.TryParse(
            CoopCampaignMapPrototypeBridgeCodec.Serialize(host), out var decodedHost, out var reason) &&
            decodedHost.VisibleEntities[0].PartyNameplateCountText == "73" &&
            decodedHost.VisibleEntities[0].PartyNameplateExtraInfoText == party.PartyNameplateExtraInfoText &&
            party.Clone().PartyNameplateExtraInfoText == party.PartyNameplateExtraInfoText,
            "Visible bridge and clone must preserve host-localized labels. Reason=" + reason);

        var state = new CoopCampaignMapPrototypeDynamicEntityState {
            EntityId = party.EntityId, IsVisible = true,
            PartyNameplateCountText = party.PartyNameplateCountText,
            PartyNameplateExtraInfoText = party.PartyNameplateExtraInfoText
        };
        var snapshot = new CoopCampaignMapPrototypeDynamicSnapshot {
            SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
            SessionId = host.SessionId, Revision = 1, UpdatedUtc = host.UpdatedUtc,
            Entities = new List<CoopCampaignMapPrototypeDynamicEntityState> { state }
        };
        var lines = CoopCampaignMapPrototypeBridgeCodec.SerializeDynamic(snapshot);
        Assert(CoopCampaignMapPrototypeBridgeCodec.TryParseDynamic(lines, out var decoded, out reason) &&
            decoded.Entities[0].PartyNameplateCountText == "73" &&
            decoded.Entities[0].PartyNameplateExtraInfoText == state.PartyNameplateExtraInfoText,
            "Dynamic bridge must preserve localized labels. Reason=" + reason);
        foreach (string field in new[] { "PartyNameplateCountText", "PartyNameplateExtraInfoText" })
        {
            var missing = new List<string>(lines);
            missing.RemoveAll(line => line.StartsWith("Entity.0." + field + "=", StringComparison.Ordinal));
            Assert(!CoopCampaignMapPrototypeBridgeCodec.TryParseDynamic(missing, out _, out _),
                "Missing nameplate field must not silently fall back: " + field);
        }
        var changed = state.Clone();
        changed.PartyNameplateCountText = "74";
        Assert(CoopCampaignMapPrototypeDynamicDeltaPolicy.BuildDelta(new[] { state }, new[] { changed }).Count == 1,
            "A healthy-count-only change must propagate even if total size is unchanged.");
        changed = state.Clone();
        changed.PartyNameplateExtraInfoText = "";
        Assert(CoopCampaignMapPrototypeDynamicDeltaPolicy.BuildDelta(new[] { state }, new[] { changed }).Count == 1,
            "Removed wounded/prisoner suffixes must produce a delta.");
        changed.IsPartyInformationHidden = true;
        Assert(!CoopCampaignMapPrototypeContract.IsValidDynamicEntity(changed),
            "Hidden dynamic labels must not carry a numeric count.");

        string root = FindRepositoryRoot();
        string publisher = File.ReadAllText(Path.Combine(root, "Campaign", "CoopCampaignMapPrototypePublisherBehavior.cs"));
        string overlay = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapPrototypeEntityOverlayVM.cs"));
        Assert(publisher.Contains("SandBoxUIHelper.GetPartyHealthyCount(party)") &&
               publisher.Contains("SandBoxUIHelper.GetAllWoundedMembersAmount(party)") &&
               publisher.Contains("SandBoxUIHelper.GetAllPrisonerMembersAmount(party)") &&
               publisher.Contains("SandBoxUIHelper.GetPartyWoundedText") &&
               publisher.Contains("SandBoxUIHelper.GetPartyPrisonerText"),
            "Capture must use native attached-army aggregation and host localization helpers.");
        Assert(overlay.Contains("entity.PartyNameplateCountText") &&
               overlay.Contains("entity.PartyNameplateExtraInfoText") &&
               !overlay.Contains("entity.PartySize.ToString()"),
            "The nameplate must consume resolved host text, not total members.");
        foreach (string file in new[] { "CoopCampaignMapPrototypeNetworkMessages.cs", "CoopCampaignMapReplicaNetworkMessages.cs" })
        {
            string wire = File.ReadAllText(Path.Combine(root, "Network", "Messages", file));
            Assert(wire.Contains("PartyNameplateCountText = ReadStringFromPacket(ref valid)") &&
                   wire.Contains("PartyNameplateExtraInfoText = ReadStringFromPacket(ref valid)") &&
                   wire.Contains("PartyNameplateCountText ?? string.Empty)") &&
                   wire.Contains("PartyNameplateExtraInfoText ?? string.Empty)"),
                "Both nameplate fields must cross both wire directions: " + file);
        }
    }

    private static void ValidateHiddenPartyInformation()
    {
        Assert(CoopCampaignMapPartyLabelPolicy.ResolvePublishedCount(true, 12345) == 0 &&
               CoopCampaignMapPartyLabelPolicy.ResolvePublishedCount(true, int.MaxValue) == 0 &&
               CoopCampaignMapPartyLabelPolicy.ResolvePublishedCount(false, 12345) == 12345 &&
               CoopCampaignMapPartyLabelPolicy.ResolvePublishedCount(false, -1) == 0 &&
               CoopCampaignMapPartyLabelPolicy.ResolvePublishedCount(false, int.MaxValue) ==
                   CoopCampaignMapPrototypeContract.MaximumPartySize,
            "Only host-known bounded counts may enter the map projection.");

        var host = CreateHostSnapshot(DateTime.UtcNow);
        var visible = host.VisibleEntities[0];
        visible.IsPartyInformationHidden = true;
        visible.PartySize = 0;
        Assert(CoopCampaignMapPrototypeContract.IsValidVisibleEntity(visible) &&
               visible.Clone().IsPartyInformationHidden,
            "A redacted visible party and its clone must retain the hidden flag.");
        Assert(CoopCampaignMapPrototypeBridgeCodec.TryParse(
            CoopCampaignMapPrototypeBridgeCodec.Serialize(host), out var hostRoundTrip, out var reason) &&
            hostRoundTrip.VisibleEntities[0].IsPartyInformationHidden &&
            hostRoundTrip.VisibleEntities[0].PartySize == 0,
            "Host bridge must preserve redaction. Reason=" + reason);
        visible.PartySize = 1;
        Assert(!CoopCampaignMapPrototypeContract.IsValidVisibleEntity(visible) &&
            !CoopCampaignMapPrototypeBridgeCodec.TryParse(
                CoopCampaignMapPrototypeBridgeCodec.Serialize(host), out _, out _),
            "A hidden party with a real count must be rejected, not relayed.");

        var hidden = new CoopCampaignMapPrototypeDynamicEntityState {
            EntityId = "party:hidden", IsVisible = true,
            IsPartyInformationHidden = true
        };
        Assert(CoopCampaignMapPrototypeContract.IsValidDynamicEntity(hidden) &&
               hidden.Clone().IsPartyInformationHidden,
            "Redacted dynamic party state must validate and clone.");
        var known = hidden.Clone();
        known.IsPartyInformationHidden = false;
        Assert(!CoopCampaignMapPrototypeDynamicDeltaPolicy.AreEquivalent(hidden, known) &&
               CoopCampaignMapPrototypeDynamicDeltaPolicy.BuildDelta(new[] { known }, new[] { hidden }).Count == 1 &&
               CoopCampaignMapPrototypeDynamicDeltaPolicy.BuildDelta(new[] { hidden }, new[] { known }).Count == 1,
            "Hiding or revealing even a zero count must produce a dynamic delta.");
        for (int field = 0; field < 4; field++)
        {
            var invalid = hidden.Clone();
            if (field == 0) invalid.PartySize = 1;
            if (field == 1) invalid.ArmyPartyCount = 1;
            if (field == 2) invalid.ArmyTotalSize = 1;
            if (field == 3) invalid.ArmyCohesion = 1;
            Assert(!CoopCampaignMapPrototypeContract.IsValidDynamicEntity(invalid),
                "Hidden numeric field must fail admission: " + field);
        }
        var dynamic = new CoopCampaignMapPrototypeDynamicSnapshot {
            SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
            SessionId = host.SessionId, Revision = 1, UpdatedUtc = host.UpdatedUtc,
            Entities = new List<CoopCampaignMapPrototypeDynamicEntityState> { hidden }
        };
        string[] lines = CoopCampaignMapPrototypeBridgeCodec.SerializeDynamic(dynamic);
        Assert(CoopCampaignMapPrototypeBridgeCodec.TryParseDynamic(lines, out var decoded, out reason) &&
               decoded.Entities[0].IsPartyInformationHidden && decoded.Entities[0].PartySize == 0,
            "Dynamic bridge must preserve redaction. Reason=" + reason);
        var missing = new List<string>(lines);
        missing.RemoveAll(line => line.StartsWith("Entity.0.IsPartyInformationHidden=", StringComparison.Ordinal));
        Assert(!CoopCampaignMapPrototypeBridgeCodec.TryParseDynamic(missing, out _, out _),
            "Missing visibility permission must fail closed.");

        string root = FindRepositoryRoot();
        string publisher = File.ReadAllText(Path.Combine(root, "Campaign", "CoopCampaignMapPrototypePublisherBehavior.cs"));
        string overlay = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapPrototypeEntityOverlayVM.cs"));
        string info = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapReplicaInfoVM.cs"));
        string view = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapPrototypeMissionView.cs"));
        string wire = File.ReadAllText(Path.Combine(root, "Network", "Messages", "CoopCampaignMapReplicaNetworkMessages.cs"));
        Assert(publisher.Contains("party.IsInfoHidden, party.Party.NumberOfAllMembers") &&
               publisher.Contains("entity.IsPartyInformationHidden = party.IsInfoHidden") &&
               publisher.Contains("ArmyTotalSize = entity.IsPartyInformationHidden") &&
               view.Contains("CoopCampaignMapReplicaStateProjection.Merge("),
            "The authoritative host-hidden permission and redacted values must reach the replica.");
        Assert(overlay.Contains("entity.IsPartyInformationHidden ? \"?\"") &&
               info.Contains("dynamicState.IsPartyInformationHidden") &&
               info.Contains("!dynamicState.IsPartyInformationHidden && dynamicState.ArmyTotalSize"),
            "Nameplate and details must not present redaction zeroes as known counts.");
        Assert(wire.Contains("IsPartyInformationHidden = ReadBoolFromPacket(ref valid)") &&
               wire.Contains("WriteBoolToPacket(entity?.IsPartyInformationHidden ?? false)"),
            "Dynamic wire format must carry the visibility permission symmetrically.");
    }

    private static void ValidatePartyNameplateColor()
    {
        // Native PartyNameplateVM palette: regular / army-leader pairs.
        uint[,] expected = {
            { 4291877096u, 4294573055u }, // Neutral
            { 4279460044u, 4279476684u }, // Alliance
            { 4285650500u, 4288804731u }, // Same faction
            { 4292232774u, 4294931829u }, // War
            { 4287421380u, 4289593317u }  // Main party / attached peer
        };
        for (int mask = 0; mask < 32; mask++)
        {
            bool main = (mask & 1) != 0;
            bool war = (mask & 2) != 0;
            bool same = (mask & 4) != 0;
            bool allied = (mask & 8) != 0;
            bool leader = (mask & 16) != 0;
            int row = main ? 4 : war ? 3 : same ? 2 : allied ? 1 : 0;
            Assert(
                CoopCampaignMapPartyLabelPolicy.ResolveColor(
                    main, war, same, allied, leader) == expected[row, leader ? 1 : 0],
                "Party label palette/precedence differs from native for mask " + mask);
        }

        var party = CreateCatalogParty(0);
        party.NameplateColor = expected[3, 0];
        var clone = party.Clone();
        Assert(clone.NameplateColor == party.NameplateColor,
            "Catalog cloning must preserve label color independently of cloth colors.");
        clone.NameplateColor = expected[0, 0];
        Assert(!CoopCampaignMapPrototypeCatalogDeltaPolicy.AreEquivalent(party, clone),
            "A diplomacy-only label color change must invalidate the catalog entry.");
        Assert(CoopCampaignMapPrototypeCatalogDeltaPolicy.BuildDelta(
            new[] { party }, new[] { clone }).Count == 1,
            "A label color change must produce one catalog delta.");

        var host = CreateHostSnapshot(DateTime.UtcNow);
        host.VisibleEntities[0].NameplateColor = expected[3, 0];
        Assert(host.VisibleEntities[0].Clone().NameplateColor == expected[3, 0],
            "Visible-entity cloning must preserve the host's label color.");
        string[] lines = CoopCampaignMapPrototypeBridgeCodec.Serialize(host);
        Assert(CoopCampaignMapPrototypeBridgeCodec.TryParse(lines, out var parsed, out var reason) &&
               parsed.VisibleEntities[0].NameplateColor == expected[3, 0],
            "Host bridge must retain non-banner label color. Reason=" + reason);
        var missingColor = new List<string>(lines);
        missingColor.RemoveAll(line => line.StartsWith("VisibleEntity.0.NameplateColor=", StringComparison.Ordinal));
        Assert(!CoopCampaignMapPrototypeBridgeCodec.TryParse(missingColor, out parsed, out reason),
            "Missing label color must not silently reuse banner color.");

        var catalog = new CoopCampaignMapPrototypeCatalogSnapshot {
            SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
            SessionId = host.SessionId, Revision = 1,
            Entities = new List<CoopCampaignMapPrototypeCatalogEntityState> { party },
            UpdatedUtc = host.UpdatedUtc
        };
        Assert(CoopCampaignMapPrototypeBridgeCodec.TryParseCatalog(
            CoopCampaignMapPrototypeBridgeCodec.SerializeCatalog(catalog), out var decodedCatalog, out reason) &&
            decodedCatalog.Entities[0].NameplateColor == expected[3, 0],
            "Catalog bridge must retain label color. Reason=" + reason);
        Assert(CoopCampaignMapCatalogBinarySerializer.TrySerialize(1, catalog.Entities, out var bytes, out reason) &&
            CoopCampaignMapCatalogBinarySerializer.TryDeserialize(bytes, out _, out var decoded, out reason) &&
            decoded[0].NameplateColor == expected[3, 0],
            "Binary catalog must retain label color. Reason=" + reason);
        Array.Copy(BitConverter.GetBytes(3), 0, bytes, 4, 4);
        Assert(!CoopCampaignMapCatalogBinarySerializer.TryDeserialize(bytes, out _, out _, out reason) && reason == "schema",
            "Older binary catalogs must fail before reading the changed layout.");

        string root = FindRepositoryRoot();
        string overlay = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapPrototypeEntityOverlayVM.cs"));
        string view = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapPrototypeMissionView.cs"));
        string wire = File.ReadAllText(Path.Combine(root, "Network", "Messages", "CoopCampaignMapPrototypeNetworkMessages.cs"));
        Assert(overlay.Contains("FactionColor = Color.FromUint(entity.NameplateColor)") &&
               !overlay.Contains("FactionColor = Color.FromUint(primaryColor)") &&
               view.Contains("CoopCampaignMapReplicaStateProjection.Merge("),
            "Client label color must come from the host, separately from banner/cloth color.");
        Assert(wire.Contains("NameplateColor = ReadUintFromPacket(") &&
               wire.Contains("WriteUintToPacket(safeEntity.NameplateColor, ColorCompression)"),
            "Legacy visible-entity wire messages must carry the new field in both directions.");
    }

    private static void ValidateReplicaStateProjection()
    {
        var catalog = CreateCatalogParty(3);
        catalog.NameplateColor = 4292232774u;
        catalog.PartyNameplateNameText = "Resolved native caption";
        catalog.CaravanMountVisual = CreateMountVisualState("campaign_mule", "campaign_pack");
        catalog.SettlementProductionIconId = "grain";
        catalog.SettlementNameplateSize = CoopCampaignMapPrototypeSettlementNameplateSize.Large;
        var dynamicState = new CoopCampaignMapPrototypeDynamicEntityState {
            EntityId = catalog.EntityId, IsVisible = true,
            NormalizedX = 1234, NormalizedY = 5678, Heading = 910,
            PartySize = 73, QuestMarkerFlags = 17, SettlementEventFlags = 65,
            SettlementEventOrder = 15, SettlementQuestOrder = 41,
            IsSettlementTracked = true, IsSettlementInRange = true,
            PartyNameplateCountText = "65", PartyNameplateExtraInfoText = "+8w+3p",
            PartyNameplateFlags = 25, PartyMovementSpeedText = "4.8"
        };

        // Deliberately populate every projected field, including fields normally
        // mutually exclusive in admitted party/settlement input. This is a pure
        // mapping test, not a transport-admission fixture.
        var dynamicFields = new HashSet<string>(StringComparer.Ordinal) {
            "NormalizedX", "NormalizedY", "Heading", "PartySize",
            "IsPartyInformationHidden", "QuestMarkerFlags", "SettlementEventFlags",
            "SettlementEventOrder", "SettlementQuestOrder",
            "SettlementRelation", "SettlementMapEventType",
            "IsSettlementTracked", "IsSettlementInRange",
            "PartyNameplateCountText", "PartyNameplateExtraInfoText",
            "PartyNameplateFlags", "PartyMovementSpeedText"
        };
        var merged = CoopCampaignMapReplicaStateProjection.Merge(catalog, dynamicState);
        Assert(merged != null, "Matching visible entities must merge.");
        foreach (var field in typeof(CoopCampaignMapPrototypeEntityState).GetProperties())
        {
            object source = dynamicFields.Contains(field.Name) ? (object)dynamicState : catalog;
            var sourceField = source.GetType().GetProperty(field.Name);
            Assert(sourceField != null, "Every projected field needs a declared source: " + field.Name);
            object expected = sourceField.GetValue(source);
            object actual = field.GetValue(merged);
            if (expected is CoopCampaignMapPrototypeAgentVisualState visual)
            {
                var clone = actual as CoopCampaignMapPrototypeAgentVisualState;
                Assert(clone != null && !ReferenceEquals(visual, clone) &&
                       !ReferenceEquals(visual.EquipmentItemIds, clone.EquipmentItemIds),
                    "Visual and equipment arrays must be detached: " + field.Name);
                foreach (var visualField in typeof(CoopCampaignMapPrototypeAgentVisualState).GetProperties())
                {
                    object left = visualField.GetValue(visual);
                    object right = visualField.GetValue(clone);
                    Assert(left is string[] items
                        ? System.Linq.Enumerable.SequenceEqual(items, (string[])right)
                        : Equals(left, right),
                        "Visual projection mismatch: " + field.Name + "." + visualField.Name);
                }
                clone.EquipmentItemIds[0] = "changed_after_merge";
                Assert(visual.EquipmentItemIds[0] != "changed_after_merge",
                    "Projection mutations must not corrupt the source catalog.");
            }
            else
                Assert(Equals(expected, actual), "Projection field mismatch: " + field.Name);
        }

        dynamicState.IsPartyInformationHidden = true;
        dynamicState.PartySize = 0;
        dynamicState.QuestMarkerFlags = 0;
        dynamicState.SettlementEventFlags = 0;
        dynamicState.SettlementEventOrder = 0;
        dynamicState.SettlementQuestOrder = 0;
        dynamicState.IsSettlementTracked = false;
        dynamicState.IsSettlementInRange = false;
        catalog.SettlementProductionIconId = string.Empty;
        catalog.NameplateColor = 4279460044u;
        var updated = CoopCampaignMapReplicaStateProjection.Merge(catalog, dynamicState);
        Assert(updated.IsPartyInformationHidden && updated.PartySize == 0 &&
               updated.QuestMarkerFlags == 0 && updated.SettlementEventFlags == 0 &&
               !updated.IsSettlementTracked && !updated.IsSettlementInRange &&
               updated.SettlementProductionIconId == string.Empty &&
               updated.NameplateColor == catalog.NameplateColor && merged.PartySize == 73,
            "Updates must clear old markers, preserve redaction/color and not mutate earlier snapshots.");
        dynamicState.IsPartyInformationHidden = false;
        Assert(!CoopCampaignMapReplicaStateProjection.Merge(catalog, dynamicState).IsPartyInformationHidden,
            "Revealing a known zero must clear the hidden bit.");
        dynamicState.IsVisible = false;
        Assert(CoopCampaignMapReplicaStateProjection.Merge(catalog, dynamicState) == null,
            "Hidden entities must not produce a visible projection.");
        dynamicState.IsVisible = true;
        dynamicState.EntityId = catalog.EntityId.ToUpperInvariant();
        Assert(CoopCampaignMapReplicaStateProjection.Merge(catalog, dynamicState) != null,
            "Identity matching remains case insensitive.");
        dynamicState.EntityId = "party:other";
        Assert(CoopCampaignMapReplicaStateProjection.Merge(catalog, dynamicState) == null &&
               CoopCampaignMapReplicaStateProjection.Merge(null, dynamicState) == null &&
               CoopCampaignMapReplicaStateProjection.Merge(catalog, null) == null,
            "Missing or mismatched entities must fail closed.");
        catalog.EntityId = dynamicState.EntityId = string.Empty;
        Assert(CoopCampaignMapReplicaStateProjection.Merge(catalog, dynamicState) == null,
            "Empty identities must not form a replica.");

        string root = FindRepositoryRoot();
        foreach (string path in new[] {
            Path.Combine(root, "Mission", "CoopCampaignMapPrototypeNetworkController.cs"),
            Path.Combine(root, "UI", "CoopCampaignMapPrototypeMissionView.cs") })
        {
            string source = File.ReadAllText(path);
            Assert(source.Contains("CoopCampaignMapReplicaStateProjection.Merge(") &&
                   !source.Contains("new CoopCampaignMapPrototypeEntityState\r") &&
                   !source.Contains("new CoopCampaignMapPrototypeEntityState\n"),
                "Full and incremental consumers must use the same tested projection: " + path);
        }
    }

    private static void ValidateSettlementPresentation()
    {
        for (int mask = 0; mask < 4; mask++)
        {
            bool tracked = (mask & 1) != 0;
            bool inspected = (mask & 2) != 0;
            var host = CreateHostSnapshot(DateTime.UtcNow);
            var settlement = host.VisibleEntities[1];
            settlement.IsSettlementTracked = tracked;
            settlement.IsSettlementInRange = inspected;
            Assert(CoopCampaignMapPrototypeContract.IsValidVisibleEntity(settlement),
                "All host settlement presentation combinations must validate.");
            var clone = settlement.Clone();
            Assert(clone.IsSettlementTracked == tracked && clone.IsSettlementInRange == inspected,
                "Full entity cloning must preserve host presentation.");
            string[] lines = CoopCampaignMapPrototypeBridgeCodec.Serialize(host);
            Assert(CoopCampaignMapPrototypeBridgeCodec.TryParse(lines, out var decoded, out string reason) &&
                   VisibleEntitiesEqual(host.VisibleEntities, decoded.VisibleEntities),
                "Full bridge must preserve settlement presentation: " + reason);

            var state = new CoopCampaignMapPrototypeDynamicEntityState {
                EntityId = settlement.EntityId, IsVisible = true,
                IsSettlementTracked = tracked, IsSettlementInRange = inspected
            };
            var dynamicSnapshot = new CoopCampaignMapPrototypeDynamicSnapshot {
                SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
                SessionId = host.SessionId, Revision = 1, UpdatedUtc = host.UpdatedUtc,
                Entities = new List<CoopCampaignMapPrototypeDynamicEntityState> { state }
            };
            string[] dynamicLines = CoopCampaignMapPrototypeBridgeCodec.SerializeDynamic(dynamicSnapshot);
            Assert(CoopCampaignMapPrototypeBridgeCodec.TryParseDynamic(dynamicLines, out var parsed, out reason) &&
                   CoopCampaignMapPrototypeDynamicDeltaPolicy.AreEquivalent(state, parsed.Entities[0]),
                "Dynamic bridge must preserve settlement presentation: " + reason);
            foreach (string field in new[] { "IsSettlementTracked", "IsSettlementInRange" })
            {
                var changed = state.Clone();
                var property = typeof(CoopCampaignMapPrototypeDynamicEntityState).GetProperty(field);
                property.SetValue(changed, !(bool)property.GetValue(state));
                var delta = CoopCampaignMapPrototypeDynamicDeltaPolicy.BuildDelta(new[] { state }, new[] { changed });
                Assert(delta.Count == 1 && CoopCampaignMapPrototypeDynamicDeltaPolicy.AreEquivalent(changed, delta[0]),
                    "A presentation-only change must cross the delta channel: " + field);
                foreach (bool missing in new[] { false, true })
                {
                    var malformed = new List<string>();
                    foreach (string line in dynamicLines)
                    {
                        if (!line.Contains("." + field + "=")) malformed.Add(line);
                        else if (!missing) malformed.Add(line.Substring(0, line.IndexOf('=') + 1) + "invalid");
                    }
                    Assert(!CoopCampaignMapPrototypeBridgeCodec.TryParseDynamic(malformed, out _, out _),
                        "Missing/malformed dynamic presentation must fail closed: " + field);
                    malformed.Clear();
                    foreach (string line in lines)
                    {
                        if (!line.Contains("." + field + "=")) malformed.Add(line);
                        else if (!missing) malformed.Add(line.Substring(0, line.IndexOf('=') + 1) + "invalid");
                    }
                    Assert(!CoopCampaignMapPrototypeBridgeCodec.TryParse(malformed, out _, out _),
                        "Missing/malformed full presentation must fail closed: " + field);
                }
            }
            var hidden = CoopCampaignMapPrototypeDynamicDeltaPolicy.BuildDelta(new[] { state },
                Array.Empty<CoopCampaignMapPrototypeDynamicEntityState>());
            Assert(hidden.Count == 1 && !hidden[0].IsVisible &&
                   !hidden[0].IsSettlementTracked && !hidden[0].IsSettlementInRange,
                "Removed entities must clear presentation in the hidden delta.");
        }
        var party = CreateVisibleEntities()[0];
        party.IsSettlementTracked = true;
        Assert(!CoopCampaignMapPrototypeContract.IsValidVisibleEntity(party),
            "Party input cannot carry settlement tracking.");
        party.IsSettlementTracked = false;
        party.IsSettlementInRange = true;
        Assert(!CoopCampaignMapPrototypeContract.IsValidVisibleEntity(party),
            "Party input cannot carry settlement inspection.");

        string root = FindRepositoryRoot();
        foreach (string path in new[] { "CoopCampaignMapPrototypeNetworkMessages.cs", "CoopCampaignMapReplicaNetworkMessages.cs" })
        {
            string wire = File.ReadAllText(Path.Combine(root, "Network", "Messages", path));
            foreach (string field in new[] { "IsSettlementTracked", "IsSettlementInRange" })
                Assert(wire.Contains(field + " = ReadBoolFromPacket(ref valid)") &&
                       (wire.Contains("WriteBoolToPacket(safeEntity." + field + ")") ||
                        wire.Contains("WriteBoolToPacket(entity?." + field + " ?? false)")),
                    "Both network channels must read/write presentation: " + path + " / " + field);
        }
        string overlay = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapPrototypeSettlementOverlayVM.cs"));
        Assert(overlay.Contains("IsTracked = entity.IsSettlementTracked") &&
               overlay.Contains("IsInRange = entity.IsSettlementInRange"),
            "Stock settlement widget bindings must consume host state, not fixed false values.");
    }

    private static void ValidateConversationContract()
    {
        CoopCampaignMapConversationState conversation =
            CreateConversationState(7);
        Assert(
            CoopCampaignMapPrototypeContract.TryValidateConversationState(
                conversation,
                out string reason),
            "A bounded active host conversation must validate. Reason=" + reason);
        CoopCampaignMapConversationState clone = conversation.Clone();
        clone.Revision++;
        Assert(
            CoopCampaignMapPrototypeContract.AreConversationContentsEquivalent(
                conversation,
                clone),
            "Content equality must ignore transport revision only.");
        clone.Options[0].Text = "Different";
        Assert(
            !CoopCampaignMapPrototypeContract.AreConversationContentsEquivalent(
                conversation,
                clone),
            "A changed answer must advance the synchronized dialogue state.");

        CoopCampaignMapConversationState inactive =
            CoopCampaignMapPrototypeContract.CreateInactiveConversation(8);
        Assert(
            CoopCampaignMapPrototypeContract.TryValidateConversationState(
                inactive,
                out reason),
            "A canonical inactive dialogue state must validate. Reason=" + reason);
        inactive.DialogText = "stale";
        Assert(
            !CoopCampaignMapPrototypeContract.TryValidateConversationState(
                inactive,
                out reason) &&
            reason == "conversation-inactive-payload",
            "An inactive state must fail closed when it retains dialogue text.");

        conversation.Options[0].Text = new string(
            'x',
            CoopCampaignMapPrototypeContract.MaxConversationOptionCharacters + 1);
        Assert(
            !CoopCampaignMapPrototypeContract.TryValidateConversationState(
                conversation,
                out reason) &&
            reason == "conversation-option",
            "An oversized answer must be rejected instead of entering the UI.");
    }

    private static void ValidateConversationSnapshotAssembler()
    {
        CoopCampaignMapConversationState expected = CreateConversationState(11);
        CoopCampaignMapConversationState header = expected.Clone();
        header.Options.Clear();
        var assembler = new CoopCampaignMapConversationSnapshotAssembler();
        Assert(
            assembler.TryBegin(header, 2, out var pending) && pending == null,
            "A valid dialogue header must start an incomplete snapshot.");
        Assert(
            assembler.TryAdd(11, 1, 2, expected.Options[1], out pending) &&
            pending == null,
            "Out-of-order dialogue options must remain buffered.");
        Assert(
            assembler.TryAdd(11, 0, 2, expected.Options[0], out var completed) &&
            completed != null &&
            completed.Options.Count == 2 &&
            completed.Options[0].Text == expected.Options[0].Text &&
            completed.Options[1].Text == expected.Options[1].Text,
            "A complete dialogue snapshot must preserve authoritative option order.");
        Assert(
            !assembler.TryBegin(header, 2, out _),
            "A completed dialogue revision must reject stale replay.");

        CoopCampaignMapConversationState inactive =
            CoopCampaignMapPrototypeContract.CreateInactiveConversation(12);
        Assert(
            assembler.TryBegin(inactive, 0, out completed) &&
            completed != null &&
            !completed.IsActive,
            "An inactive header must close the client dialogue atomically.");
    }

    private static void ValidateVisibilityPolicy()
    {
        Assert(
            CoopCampaignMapPrototypeVisibilityPolicy
                .ShouldReplicateMobileParty(
                    isMainParty: true,
                    isActive: false,
                    isGarrison: true,
                    isMilitia: true,
                    hasCurrentSettlement: true,
                    isSpotted: false),
            "The main party must remain in the replica independently of spotting state.");
        Assert(
            CoopCampaignMapPrototypeVisibilityPolicy
                .ShouldReplicateMobileParty(
                    isMainParty: false,
                    isActive: true,
                    isGarrison: false,
                    isMilitia: false,
                    hasCurrentSettlement: false,
                    isSpotted: true),
            "A spotted active field party must be replicated.");
        Assert(
            !CoopCampaignMapPrototypeVisibilityPolicy
                .ShouldReplicateMobileParty(
                    isMainParty: false,
                    isActive: true,
                    isGarrison: false,
                    isMilitia: false,
                    hasCurrentSettlement: false,
                    isSpotted: false),
            "An unspotted field party must fail closed.");
        Assert(
            !CoopCampaignMapPrototypeVisibilityPolicy
                .ShouldReplicateMobileParty(
                    isMainParty: false,
                    isActive: true,
                    isGarrison: true,
                    isMilitia: false,
                    hasCurrentSettlement: false,
                    isSpotted: true),
            "A garrison must not be duplicated as a field-party replica.");
        Assert(
            CoopCampaignMapPrototypeVisibilityPolicy
                .ShouldReplicateSettlement(
                    isVisible: true,
                    isSpottable: false,
                    isSpotted: false),
            "A visible regular settlement must be replicated.");
        Assert(
            !CoopCampaignMapPrototypeVisibilityPolicy
                .ShouldReplicateSettlement(
                    isVisible: false,
                    isSpottable: false,
                    isSpotted: false),
            "A hidden regular settlement must fail closed.");
        Assert(
            !CoopCampaignMapPrototypeVisibilityPolicy
                .ShouldReplicateSettlement(
                    isVisible: true,
                    isSpottable: true,
                    isSpotted: false),
            "An unspotted hideout must not leak through Settlement.IsVisible.");
        Assert(
            CoopCampaignMapPrototypeVisibilityPolicy
                .ShouldReplicateSettlement(
                    isVisible: false,
                    isSpottable: true,
                    isSpotted: true),
            "A spotted hideout must follow its native spottable state.");

        Assert(
            CoopCampaignMapPrototypeVisibilityPolicy.ShouldShowParty(
                cameraHeight: 199.999d,
                projectedDepth: 99.999d,
                isInsideViewport: true),
            "A nearby party below the native zoom limits must be visible.");
        Assert(
            !CoopCampaignMapPrototypeVisibilityPolicy.ShouldShowParty(
                cameraHeight: 200d,
                projectedDepth: 50d,
                isInsideViewport: true),
            "A party must be hidden at the native camera-height boundary.");
        Assert(
            !CoopCampaignMapPrototypeVisibilityPolicy.ShouldShowParty(
                cameraHeight: 100d,
                projectedDepth: 100d,
                isInsideViewport: true),
            "A party must be hidden at the native projected-depth boundary.");
        Assert(
            !CoopCampaignMapPrototypeVisibilityPolicy.ShouldShowParty(
                cameraHeight: 100d,
                projectedDepth: 50d,
                isInsideViewport: false),
            "An off-screen party must be hidden.");

        Assert(
            CoopCampaignMapPrototypeVisibilityPolicy.ShouldShowSettlement(
                CoopCampaignMapPrototypeSettlementNameplateSize.Small,
                cameraHeight: 200d,
                distanceToCamera: 299.999d,
                isInFront: true,
                isInsideViewport: true),
            "A nearby village must remain visible at camera height 200.");
        Assert(
            !CoopCampaignMapPrototypeVisibilityPolicy.ShouldShowSettlement(
                CoopCampaignMapPrototypeSettlementNameplateSize.Small,
                cameraHeight: 200.001d,
                distanceToCamera: 50d,
                isInFront: true,
                isInsideViewport: true),
            "Villages must be hidden above the fortification-only threshold.");
        Assert(
            CoopCampaignMapPrototypeVisibilityPolicy.ShouldShowSettlement(
                CoopCampaignMapPrototypeSettlementNameplateSize.Medium,
                cameraHeight: 400d,
                distanceToCamera: 1000d,
                isInFront: true,
                isInsideViewport: true),
            "Castles must remain visible at camera height 400.");
        Assert(
            !CoopCampaignMapPrototypeVisibilityPolicy.ShouldShowSettlement(
                CoopCampaignMapPrototypeSettlementNameplateSize.Medium,
                cameraHeight: 400.001d,
                distanceToCamera: 50d,
                isInFront: true,
                isInsideViewport: true),
            "Castles must be hidden above the town-only threshold.");
        Assert(
            CoopCampaignMapPrototypeVisibilityPolicy.ShouldShowSettlement(
                CoopCampaignMapPrototypeSettlementNameplateSize.Large,
                cameraHeight: 500d,
                distanceToCamera: 1000d,
                isInFront: true,
                isInsideViewport: true),
            "Towns must remain visible above the town-only threshold.");
        Assert(
            !CoopCampaignMapPrototypeVisibilityPolicy.ShouldShowSettlement(
                CoopCampaignMapPrototypeSettlementNameplateSize.Large,
                cameraHeight: 100d,
                distanceToCamera: 200d,
                isInFront: true,
                isInsideViewport: true),
            "A non-tracked settlement outside the native near-distance limit must be hidden.");
        Assert(
            !CoopCampaignMapPrototypeVisibilityPolicy.ShouldShowSettlement(
                CoopCampaignMapPrototypeSettlementNameplateSize.Large,
                cameraHeight: 500d,
                distanceToCamera: 10d,
                isInFront: false,
                isInsideViewport: true),
            "A settlement behind the camera must be hidden.");

        foreach (var size in new[] {
                     CoopCampaignMapPrototypeSettlementNameplateSize.Small,
                     CoopCampaignMapPrototypeSettlementNameplateSize.Medium,
                     CoopCampaignMapPrototypeSettlementNameplateSize.Large })
        foreach (double height in new[] { 50d, 200d, 201d, 400d, 401d })
        foreach (bool inFront in new[] { false, true })
        foreach (bool inside in new[] { false, true })
        {
            Assert(CoopCampaignMapPrototypeVisibilityPolicy.ShouldShowSettlement(
                    size, height, 10000d, inFront, inside, isTracked: true),
                "A host-admitted tracked settlement keeps its native edge marker at every zoom.");
            if (!inFront || !inside)
                Assert(!CoopCampaignMapPrototypeVisibilityPolicy.ShouldShowSettlement(
                        size, height, 10000d, inFront, inside, isTracked: false),
                    "Removing tracking restores ordinary off-screen culling.");
        }
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Assert(!CoopCampaignMapPrototypeVisibilityPolicy.ShouldShowSettlement(
                    CoopCampaignMapPrototypeSettlementNameplateSize.Large,
                    invalid, 10d, true, true, true), "Tracking cannot admit an invalid camera.");
            Assert(!CoopCampaignMapPrototypeVisibilityPolicy.ShouldShowSettlement(
                    CoopCampaignMapPrototypeSettlementNameplateSize.Large,
                    10d, invalid, true, true, true), "Tracking cannot admit an invalid projection distance.");
        }
        Assert(!CoopCampaignMapPrototypeVisibilityPolicy.ShouldShowSettlement(
                CoopCampaignMapPrototypeSettlementNameplateSize.None,
                10d, 10d, true, true, true), "Tracking cannot invent an absent settlement nameplate.");
        string root = FindRepositoryRoot();
        string view = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapPrototypeMissionView.cs"));
        string overlay = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapPrototypeSettlementOverlayVM.cs"));
        Assert(view.Contains("_camera,\r\n                                target.IsSettlementTracked") ||
               view.Contains("_camera,\n                                target.IsSettlementTracked"),
            "Projection prefilter must receive the admitted host tracking bit.");
        Assert(view.Contains("isTracked: isTracked"), "Prefilter and final projection must share the tracking policy.");
        Assert(overlay.Contains("Position = IsVisibleOnMap") &&
               !overlay.Contains("Position = isInFront && isInside"),
            "Native widget must retain original tracked off-screen coordinates, not a fabricated direction.");
        Assert(overlay.Contains("isInside,\r\n                            IsTracked") ||
               overlay.Contains("isInside,\n                            IsTracked"),
            "Final projection visibility must also honor host tracking.");
    }

    private static void ValidateInteractionPolicy()
    {
        Assert(
            CoopCampaignMapPrototypeInteractionPolicy.ResolveZoomIntent(
                scrollUpPressed: true,
                scrollDownPressed: false,
                deltaMouseScroll: 0d) == 1,
            "Mouse-wheel up must request map zoom-in.");
        Assert(
            CoopCampaignMapPrototypeInteractionPolicy.ResolveZoomIntent(
                scrollUpPressed: false,
                scrollDownPressed: true,
                deltaMouseScroll: 0d) == -1,
            "Mouse-wheel down must request map zoom-out.");
        Assert(
            CoopCampaignMapPrototypeInteractionPolicy.ResolveZoomIntent(
                scrollUpPressed: true,
                scrollDownPressed: true,
                deltaMouseScroll: 1d) == 0,
            "Contradictory wheel buttons must fail closed.");
        Assert(
            CoopCampaignMapPrototypeInteractionPolicy.ResolveZoomIntent(
                scrollUpPressed: false,
                scrollDownPressed: false,
                deltaMouseScroll: 0.5d) == 1 &&
            CoopCampaignMapPrototypeInteractionPolicy.ResolveZoomIntent(
                scrollUpPressed: false,
                scrollDownPressed: false,
                deltaMouseScroll: -0.5d) == -1,
            "Raw wheel deltas must preserve their zoom direction.");
        AssertNearlyEqual(
            80d,
            CoopCampaignMapPrototypeInteractionPolicy.ResolvePanSpeed(
                horizontalSpan: 1000d,
                cameraHeight: 100d),
            "Pan speed must scale with camera height.");
        AssertNearlyEqual(
            20d,
            CoopCampaignMapPrototypeInteractionPolicy.ResolvePanSpeed(
                horizontalSpan: 1000d,
                cameraHeight: 1d),
            "Pan speed must retain its usable minimum.");
        AssertNearlyEqual(
            25d,
            CoopCampaignMapPrototypeInteractionPolicy.ResolvePanSpeed(
                horizontalSpan: 100d,
                cameraHeight: 1000d),
            "Pan speed must stay bounded by map span.");
        AssertNearlyEqual(
            18d,
            CoopCampaignMapPrototypeInteractionPolicy.ResolveZoomStep(
                horizontalSpan: 1000d,
                cameraHeight: 100d),
            "Zoom step must scale with camera height.");
        AssertNearlyEqual(
            2.5d,
            CoopCampaignMapPrototypeInteractionPolicy.ResolveZoomStep(
                horizontalSpan: 1000d,
                cameraHeight: 1d),
            "Zoom step must retain its usable minimum.");
        AssertNearlyEqual(
            12d,
            CoopCampaignMapPrototypeInteractionPolicy.ResolveZoomStep(
                horizontalSpan: 100d,
                cameraHeight: 1000d),
            "Zoom step must stay bounded by map span.");
        AssertNearlyEqual(
            52.5d,
            CoopCampaignMapPrototypeInteractionPolicy.ClampCameraAltitude(
                terrainHeight: 50d,
                candidateAltitude: 40d,
                horizontalSpan: 100d),
            "Camera altitude must remain above terrain clearance.");
        AssertNearlyEqual(
            250d,
            CoopCampaignMapPrototypeInteractionPolicy.ClampCameraAltitude(
                terrainHeight: 50d,
                candidateAltitude: 1000d,
                horizontalSpan: 100d),
            "Camera altitude must remain bounded above the map.");
        Assert(
            CoopCampaignMapPrototypeInteractionPolicy.SelectionRadiusPixels >= 70d,
            "Replica selection must retain a practical screen-space radius.");
    }

    private static void ValidateInformationPanelPosition()
    {
        CoopCampaignMapPrototypeInteractionPolicy.ResolveInformationPanelPosition(
            100d, 200d, false, 1920d, 1080d, 1d, out double x, out double y);
        AssertNearlyEqual(124d, x, "Hover panel must follow the cursor in physical pixels.");
        AssertNearlyEqual(224d, y, "Hover panel must retain its vertical cursor gap.");

        CoopCampaignMapPrototypeInteractionPolicy.ResolveInformationPanelPosition(
            1910d, 1070d, false, 1920d, 1080d, 1.5d, out x, out y);
        AssertNearlyEqual(1350d, x, "Right-edge clamp must include scaled panel width.");
        AssertNearlyEqual(720d, y, "Bottom-edge clamp must include scaled panel height.");

        CoopCampaignMapPrototypeInteractionPolicy.ResolveInformationPanelPosition(
            0d, 0d, true, 1920d, 1080d, 1.5d, out x, out y);
        AssertNearlyEqual(1350d, x, "Pinned panel must remain on the right inside the viewport.");
        AssertNearlyEqual(135d, y, "Pinned top margin must respect UI scaling.");

        CoopCampaignMapPrototypeInteractionPolicy.ResolveInformationPanelPosition(
            -100d, -100d, false, 1920d, 1080d, 1d, out x, out y);
        AssertNearlyEqual(20d, x, "Hover panel must clamp negative cursor X.");
        AssertNearlyEqual(20d, y, "Hover panel must clamp negative cursor Y.");

        CoopCampaignMapPrototypeInteractionPolicy.ResolveInformationPanelPosition(
            100d, 100d, true, 100d, 100d, 2d, out x, out y);
        AssertNearlyEqual(0d, x, "An undersized viewport must not produce a negative origin.");
        AssertNearlyEqual(0d, y, "An undersized viewport must keep the panel origin visible.");

        CoopCampaignMapPrototypeInteractionPolicy.ResolveInformationPanelPosition(
            double.NaN, double.PositiveInfinity, false,
            double.NaN, -1d, double.NaN, out x, out y);
        AssertNearlyEqual(0d, x, "Invalid viewport/input values must produce finite positions.");
        AssertNearlyEqual(0d, y, "Invalid viewport/input values must not reach Gauntlet.");
    }

    private static void ValidateCatalogBinaryChunkTransport()
    {
        var expected = new List<
            CoopCampaignMapPrototypeCatalogEntityState>
        {
            CreateCatalogParty(0),
            CreateCatalogParty(1),
            new CoopCampaignMapPrototypeCatalogEntityState
            {
                EntityId = "settlement:town_V1",
                DisplayName = "Village",
                Kind = CoopCampaignMapPrototypeEntityKind.Settlement,
                SettlementNameplateSize =
                    CoopCampaignMapPrototypeSettlementNameplateSize.Small,
                SettlementKind =
                    CoopCampaignMapPrototypeSettlementKind.Village,
                PrimaryColor = 0xFF102030u,
                SecondaryColor = 0xFF405060u,
                BannerCode = "settlement-banner",
                VisualCharacterId = string.Empty,
                CultureId = string.Empty,
                PartyVisualKind =
                    CoopCampaignMapPrototypePartyVisualKind.None,
                FactionId = "empire",
                FactionName = "Empire",
                OwnerName = "Owner clan",
                LeaderName = "Governor",
                ArmyId = string.Empty,
                ArmyName = string.Empty,
                SelectionRadius = 30000,
                SettlementProductionIconId = "grain"
            }
        };

        Assert(
            CoopCampaignMapCatalogBinarySerializer.TrySerialize(
                37,
                expected,
                out byte[] logicalBytes,
                out string reason),
            "A valid catalog must serialize. Reason=" + reason);
        Assert(
            CoopCampaignMapCatalogBinarySerializer.TryDeserialize(
                logicalBytes,
                out int decodedRevision,
                out List<CoopCampaignMapPrototypeCatalogEntityState>
                    decoded,
                out reason),
            "A serialized catalog must deserialize. Reason=" + reason);
        Assert(
            decodedRevision == 37 &&
            CatalogEntitiesEqual(expected, decoded),
            "The binary catalog serializer must preserve every field.");

        Assert(
            CoopCampaignMapCatalogChunkCodec.TryEncode(
                logicalBytes,
                out CoopCampaignMapCatalogChunkedPayload payload,
                out reason),
            "A serialized catalog must encode into chunks. Reason=" + reason);
        Assert(
            payload.ChunkCount > 0 &&
            payload.WireByteCount <=
                CoopCampaignMapCatalogChunkCodec.MaxWireBytes &&
            CoopCampaignMapCatalogChunkCodec.MaxChunkBytes == 256,
            "The catalog transport must use the native-safe 256-byte ceiling.");
        foreach (byte[] chunk in payload.Chunks)
        {
            Assert(
                chunk.Length > 0 &&
                chunk.Length <=
                    CoopCampaignMapCatalogChunkCodec.MaxChunkBytes,
                "Every catalog chunk must remain within the wire ceiling.");
        }

        Assert(
            CoopCampaignMapCatalogChunkAccumulator.TryCreate(
                19,
                37,
                payload.LogicalByteCount,
                payload.WireByteCount,
                payload.ChunkCount,
                payload.CompressionKind,
                payload.PayloadHash,
                out CoopCampaignMapCatalogChunkAccumulator accumulator,
                out reason),
            "A valid catalog manifest must create an accumulator. Reason=" +
            reason);
        for (int index = payload.ChunkCount - 1; index >= 0; index--)
        {
            Assert(
                accumulator.TryAccept(
                    index,
                    payload.ChunkCount,
                    payload.Chunks[index],
                    out reason),
                "Out-of-order chunks must be accepted. Reason=" + reason);
        }
        Assert(
            accumulator.ReceivedChunkCount == payload.ChunkCount &&
            accumulator.HighestContiguousChunkIndex ==
                payload.ChunkCount - 1,
            "The accumulator must track a complete out-of-order payload.");
        Assert(
            accumulator.TryAccept(
                0,
                payload.ChunkCount,
                payload.Chunks[0],
                out reason),
            "An identical duplicate chunk must be idempotent. Reason=" +
            reason);
        Assert(
            accumulator.TryComplete(
                out byte[] completedLogicalBytes,
                out reason) &&
            ByteArraysEqual(logicalBytes, completedLogicalBytes),
            "A complete verified catalog must restore the logical payload. Reason=" +
            reason);

        Assert(
            CoopCampaignMapCatalogChunkAccumulator.TryCreate(
                20,
                37,
                payload.LogicalByteCount,
                payload.WireByteCount,
                payload.ChunkCount,
                payload.CompressionKind,
                payload.PayloadHash,
                out CoopCampaignMapCatalogChunkAccumulator missing,
                out reason),
            "The missing-chunk test manifest must be valid.");
        for (int index = 0; index < payload.ChunkCount - 1; index++)
        {
            Assert(
                missing.TryAccept(
                    index,
                    payload.ChunkCount,
                    payload.Chunks[index],
                    out reason),
                "A bounded partial payload must be accepted.");
        }
        Assert(
            !missing.TryComplete(out _, out reason) &&
            reason == "missing-chunks",
            "An incomplete catalog must never become visible atomically.");

        Assert(
            CoopCampaignMapCatalogChunkAccumulator.TryCreate(
                21,
                37,
                payload.LogicalByteCount,
                payload.WireByteCount,
                payload.ChunkCount,
                payload.CompressionKind,
                payload.PayloadHash,
                out CoopCampaignMapCatalogChunkAccumulator conflicting,
                out reason),
            "The conflicting-duplicate test manifest must be valid.");
        Assert(
            conflicting.TryAccept(
                0,
                payload.ChunkCount,
                payload.Chunks[0],
                out reason),
            "The first chunk instance must be accepted.");
        byte[] changedChunk = (byte[])payload.Chunks[0].Clone();
        changedChunk[0] ^= 0x5A;
        Assert(
            !conflicting.TryAccept(
                0,
                payload.ChunkCount,
                changedChunk,
                out reason) &&
            reason == "conflicting-duplicate",
            "A conflicting duplicate chunk must be rejected.");

        string wrongHash = new string('0', 64);
        if (string.Equals(
                wrongHash,
                payload.PayloadHash,
                StringComparison.OrdinalIgnoreCase))
        {
            wrongHash = new string('1', 64);
        }
        Assert(
            CoopCampaignMapCatalogChunkAccumulator.TryCreate(
                22,
                37,
                payload.LogicalByteCount,
                payload.WireByteCount,
                payload.ChunkCount,
                payload.CompressionKind,
                wrongHash,
                out CoopCampaignMapCatalogChunkAccumulator corrupted,
                out reason),
            "A syntactically valid hash must create an accumulator.");
        for (int index = 0; index < payload.ChunkCount; index++)
        {
            Assert(
                corrupted.TryAccept(
                    index,
                    payload.ChunkCount,
                    payload.Chunks[index],
                    out reason),
                "The corruption test chunks must assemble.");
        }
        Assert(
            !corrupted.TryComplete(out _, out reason) && reason == "hash",
            "A payload with the wrong SHA-256 must fail closed.");

        byte[] truncated = new byte[logicalBytes.Length - 1];
        Buffer.BlockCopy(
            logicalBytes,
            0,
            truncated,
            0,
            truncated.Length);
        Assert(
            !CoopCampaignMapCatalogBinarySerializer.TryDeserialize(
                truncated,
                out _,
                out _,
                out _),
            "A truncated binary catalog must be rejected.");
    }

    private static void ValidateLargeCatalogChunkTransport()
    {
        const int observedCatalogCount = 1197;
        var expected = new List<
            CoopCampaignMapPrototypeCatalogEntityState>(
                observedCatalogCount);
        for (int index = 0; index < observedCatalogCount; index++)
            expected.Add(CreateCatalogParty(index));

        Assert(
            CoopCampaignMapCatalogBinarySerializer.TrySerialize(
                124,
                expected,
                out byte[] logicalBytes,
                out string reason),
            "The observed 1197-entity catalog must serialize. Reason=" +
            reason);
        Assert(
            logicalBytes.Length >
                CoopCampaignMapCatalogChunkCodec.MaxChunkBytes,
            "The observed catalog must exercise multi-chunk transport.");
        Assert(
            CoopCampaignMapCatalogChunkCodec.TryEncode(
                logicalBytes,
                out CoopCampaignMapCatalogChunkedPayload payload,
                out reason),
            "The observed catalog must fit the bounded chunk transport. Reason=" +
            reason);
        Assert(
            payload.ChunkCount > 1 &&
            payload.ChunkCount <=
                CoopCampaignMapCatalogChunkCodec.MaxChunkCount,
            "The observed catalog must split into a bounded chunk sequence.");

        Assert(
            CoopCampaignMapCatalogChunkAccumulator.TryCreate(
                125,
                124,
                payload.LogicalByteCount,
                payload.WireByteCount,
                payload.ChunkCount,
                payload.CompressionKind,
                payload.PayloadHash,
                out CoopCampaignMapCatalogChunkAccumulator accumulator,
                out reason),
            "The observed catalog manifest must be accepted.");
        for (int index = payload.ChunkCount - 1; index >= 0; index--)
        {
            Assert(
                accumulator.TryAccept(
                    index,
                    payload.ChunkCount,
                    payload.Chunks[index],
                    out reason),
                "Every observed catalog chunk must assemble. Reason=" +
                reason);
        }
        Assert(
            accumulator.TryComplete(
                out byte[] completedLogicalBytes,
                out reason) &&
            CoopCampaignMapCatalogBinarySerializer.TryDeserialize(
                completedLogicalBytes,
                out int decodedRevision,
                out List<CoopCampaignMapPrototypeCatalogEntityState>
                    decoded,
                out reason) &&
            decodedRevision == 124 &&
            decoded.Count == observedCatalogCount &&
            decoded[observedCatalogCount - 1].EntityId ==
                expected[observedCatalogCount - 1].EntityId,
            "The full observed catalog must survive compression, chunks and decoding. Reason=" +
            reason);
    }

    private static void ValidateCatalogTransferSupersessionPolicy()
    {
        Assert(
            CoopCampaignMapCatalogTransferPolicy
                .ShouldStartPreparedTransfer(0, -1, false, 1, 1),
            "A peer without an active catalog transfer must start the prepared transfer.");

        for (int preparedRevision = 2;
             preparedRevision <= 125;
             preparedRevision++)
        {
            Assert(
                !CoopCampaignMapCatalogTransferPolicy
                    .ShouldStartPreparedTransfer(
                        1,
                        1,
                        false,
                        preparedRevision,
                        preparedRevision),
                "A newer catalog must not supersede an incomplete transfer.");
        }

        Assert(
            CoopCampaignMapCatalogTransferPolicy
                .ShouldStartPreparedTransfer(1, 1, true, 125, 125),
            "After completion, only the latest prepared catalog may start.");
        Assert(
            !CoopCampaignMapCatalogTransferPolicy
                .ShouldStartPreparedTransfer(125, 125, true, 125, 125),
            "A completed catalog must not be queued again without a newer generation.");
        Assert(
            !CoopCampaignMapCatalogTransferPolicy
                .ShouldStartPreparedTransfer(1, 1, false, 0, 126),
            "An invalid prepared transfer must never replace an active transfer.");
    }

    private static void ValidateDynamicBatchTransportPolicy()
    {
        Assert(
            CoopCampaignMapPrototypeContract.DynamicBatchCompressionMaximum == 32,
            "The dynamic batch wire range must remain compatible with protocol 10 readers.");
        Assert(
            CoopCampaignMapPrototypeContract.MaxDynamicBatchSize == 2,
            "Localized labels require a conservative two-entity batch limit.");
        Assert(
            CoopCampaignMapPrototypeContract.MaxDynamicBatchSize <
            CoopCampaignMapPrototypeContract.DynamicBatchCompressionMaximum,
            "The safe send limit must remain below the encoded wire ceiling.");

        const int observedReplicaEntityCount = 1180;
        int batchCount =
            (observedReplicaEntityCount +
             CoopCampaignMapPrototypeContract.MaxDynamicBatchSize - 1) /
            CoopCampaignMapPrototypeContract.MaxDynamicBatchSize;
        int finalBatchSize =
            observedReplicaEntityCount %
            CoopCampaignMapPrototypeContract.MaxDynamicBatchSize;
        Assert(
            batchCount == 590 && finalBatchSize == 0,
            "The observed 1180-entity replica must split into 590 bounded batches without loss.");
    }

    private static void ValidateDynamicDeltaPolicy()
    {
        var previous = new List<CoopCampaignMapPrototypeDynamicEntityState>
        {
            new CoopCampaignMapPrototypeDynamicEntityState
            {
                EntityId = "party:main",
                NormalizedX = 100,
                NormalizedY = 200,
                Heading = 300,
                PartySize = 30,
                IsVisible = true,
                IsMoving = false
            },
            new CoopCampaignMapPrototypeDynamicEntityState
            {
                EntityId = "party:scout",
                NormalizedX = 400,
                NormalizedY = 500,
                Heading = 600,
                PartySize = 15,
                IsVisible = true,
                IsMoving = true
            },
            new CoopCampaignMapPrototypeDynamicEntityState
            {
                EntityId = "settlement:vostrum",
                NormalizedX = 700,
                NormalizedY = 800,
                Heading = 0,
                PartySize = 120,
                IsVisible = true,
                IsMoving = false
            }
        };
        var current = new List<CoopCampaignMapPrototypeDynamicEntityState>
        {
            previous[0].Clone(),
            previous[2].Clone()
        };
        current[0].NormalizedX = 125;
        current[0].IsMoving = true;
        current[0].QuestMarkerFlags =
            (int)CoopCampaignMapPrototypeQuestMarkerFlags.ActiveIssue;

        List<CoopCampaignMapPrototypeDynamicEntityState> delta =
            CoopCampaignMapPrototypeDynamicDeltaPolicy.BuildDelta(
                previous,
                current);
        Assert(
            delta.Count == 2,
            "Only the moved main party and the hidden party may enter the dynamic delta.");

        var deltaById = new Dictionary<
            string,
            CoopCampaignMapPrototypeDynamicEntityState>(
                StringComparer.OrdinalIgnoreCase);
        foreach (CoopCampaignMapPrototypeDynamicEntityState entity in delta)
            deltaById[entity.EntityId] = entity;

        Assert(
            deltaById.TryGetValue(
                "party:main",
                out CoopCampaignMapPrototypeDynamicEntityState mainUpdate) &&
            mainUpdate.NormalizedX == 125 &&
            mainUpdate.IsMoving &&
            mainUpdate.QuestMarkerFlags ==
                (int)CoopCampaignMapPrototypeQuestMarkerFlags.ActiveIssue,
            "A moving party update must preserve its changed quest marker state.");
        Assert(
            deltaById.TryGetValue(
                "party:scout",
                out CoopCampaignMapPrototypeDynamicEntityState hiddenUpdate) &&
            !hiddenUpdate.IsVisible &&
            !hiddenUpdate.IsMoving,
            "A no-longer-observed party must become an incremental visibility tombstone.");
        Assert(
            !deltaById.ContainsKey("settlement:vostrum"),
            "An unchanged settlement must not be retransmitted.");
        Assert(
            CoopCampaignMapPrototypeDynamicDeltaPolicy.BuildDelta(
                previous,
                previous).Count == 0,
            "An unchanged authoritative snapshot must produce no network update.");

        int oneEntityBatchCount =
            (1 + CoopCampaignMapPrototypeContract.MaxDynamicBatchSize - 1) /
            CoopCampaignMapPrototypeContract.MaxDynamicBatchSize;
        Assert(
            oneEntityBatchCount == 1,
            "A normal one-party movement update must fit in one network batch.");
    }

    private static void ValidateCatalogDeltaPolicy()
    {
        var previous = new List<CoopCampaignMapPrototypeCatalogEntityState>
        {
            CreateCatalogParty(0),
            CreateCatalogParty(1),
            CreateCatalogParty(3)
        };
        var current = new List<CoopCampaignMapPrototypeCatalogEntityState>
        {
            previous[0].Clone(),
            previous[1].Clone(),
            CreateCatalogParty(2)
        };
        current[1].LeaderName = "Updated leader";

        List<CoopCampaignMapPrototypeCatalogEntityState> delta =
            CoopCampaignMapPrototypeCatalogDeltaPolicy.BuildDelta(
                previous,
                current);
        Assert(
            delta.Count == 2 &&
            delta[0].EntityId == current[1].EntityId &&
            delta[1].EntityId == current[2].EntityId,
            "Only a changed and a newly discovered catalog entry may enter the live delta.");
        Assert(
            CoopCampaignMapPrototypeCatalogDeltaPolicy.BuildDelta(
                previous,
                previous).Count == 0,
            "An unchanged catalog must not trigger a live network update.");
        Assert(
            !CoopCampaignMapPrototypeCatalogDeltaPolicy.AreEquivalent(
                previous[0],
                ChangeFirstEquipmentItem(previous[0])),
            "Exact visual equipment changes must invalidate catalog equivalence.");
        CoopCampaignMapPrototypeCatalogEntityState productionChanged =
            previous[0].Clone();
        productionChanged.SettlementProductionIconId = "iron";
        Assert(
            !CoopCampaignMapPrototypeCatalogDeltaPolicy.AreEquivalent(
                previous[0],
                productionChanged),
            "A production-icon change must invalidate catalog equivalence.");
    }

    private static CoopCampaignMapPrototypeCatalogEntityState
        ChangeFirstEquipmentItem(
            CoopCampaignMapPrototypeCatalogEntityState source)
    {
        CoopCampaignMapPrototypeCatalogEntityState changed = source.Clone();
        changed.HumanVisual.EquipmentItemIds[0] = "changed_visual_item";
        return changed;
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
            north == 0,
            "Native +Y forward must quantize to zero rotation.");

        Assert(CoopCampaignMapPrototypeContract.QuantizeDirection(1d, 0d, 0) ==
            CoopCampaignMapPrototypeContract.UnitScale * 3 / 4,
            "Native +X bearing must rotate clockwise by a quarter turn.");
        Assert(CoopCampaignMapPrototypeContract.QuantizeDirection(0d, -1d, 0) ==
            CoopCampaignMapPrototypeContract.UnitScale / 2,
            "Native -Y bearing must rotate by half a turn.");
        Assert(CoopCampaignMapPrototypeContract.QuantizeDirection(-1d, 0d, 0) ==
            CoopCampaignMapPrototypeContract.UnitScale / 4,
            "Native -X bearing must rotate counterclockwise by a quarter turn.");

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
            actual.Conversation.Revision == expected.Conversation.Revision &&
            CoopCampaignMapPrototypeContract.AreConversationContentsEquivalent(
                actual.Conversation,
                expected.Conversation) &&
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
                    EntityId = "settlement:village",
                    DisplayName = "Village",
                    Kind = CoopCampaignMapPrototypeEntityKind.Settlement,
                    SettlementNameplateSize =
                        CoopCampaignMapPrototypeSettlementNameplateSize.Small,
                    SettlementKind =
                        CoopCampaignMapPrototypeSettlementKind.Village,
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
                    SelectionRadius = 30000,
                    SettlementProductionIconId = "grain"
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
            actual.Entities.Count == 4 &&
            actual.Entities[0].LeaderName == "Leader" &&
            actual.Entities[0].MountVisual != null &&
            actual.Entities[1].SettlementKind ==
                CoopCampaignMapPrototypeSettlementKind.Town &&
            actual.Entities[2].SettlementProductionIconId == "grain" &&
            actual.Entities[3].SettlementKind ==
                CoopCampaignMapPrototypeSettlementKind.Special,
            "The replica catalog codec must preserve identity, information, visuals and village production.");
        CoopCampaignMapPrototypeCatalogEntityState invalidSpecial =
            actual.Entities[3].Clone();
        invalidSpecial.SettlementKind =
            CoopCampaignMapPrototypeSettlementKind.None;
        Assert(
            !CoopCampaignMapPrototypeContract.IsValidCatalogEntity(invalidSpecial),
            "A settlement without a concrete settlement kind must remain invalid.");
        CoopCampaignMapPrototypeCatalogEntityState invalidTownProduction =
            actual.Entities[1].Clone();
        invalidTownProduction.SettlementProductionIconId = "grain";
        Assert(
            !CoopCampaignMapPrototypeContract.IsValidCatalogEntity(
                invalidTownProduction),
            "Only a village may carry a primary production icon.");
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
                    EntityId = "settlement:codec_fixture",
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
                    InformationRevision = 3,
                    SettlementEventOrder = 2,
                    SettlementQuestOrder = 4,
                    QuestMarkerFlags =
                        (int)CoopCampaignMapPrototypeQuestMarkerFlags
                            .TrackedIssue,
                    SettlementEventFlags =
                        (int)CoopCampaignMapPrototypeSettlementEventFlags
                            .AvailableIssue
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
            actual.Entities[0].ArmyCohesion == 750000 &&
            actual.Entities[0].QuestMarkerFlags == 8 &&
            actual.Entities[0].SettlementEventFlags == 2,
            "The dynamic codec must preserve movement, army and marker information.");

        CoopCampaignMapPrototypeDynamicEntityState invalid =
            actual.Entities[0].Clone();
        invalid.QuestMarkerFlags =
            CoopCampaignMapPrototypeContract.MaxQuestMarkerFlags + 1;
        Assert(
            !CoopCampaignMapPrototypeContract.IsValidDynamicEntity(invalid),
            "Unknown quest marker bits must fail closed.");
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

        invalid = entities[0].Clone();
        invalid.SettlementEventFlags =
            (int)CoopCampaignMapPrototypeSettlementEventFlags.AvailableIssue;
        Assert(
            !CoopCampaignMapPrototypeContract.IsValidVisibleEntity(invalid),
            "A party must not carry settlement event markers.");

        invalid = entities[1].Clone();
        invalid.SettlementProductionIconId = "grain";
        Assert(
            !CoopCampaignMapPrototypeContract.IsValidVisibleEntity(invalid),
            "A production icon without the production event bit must fail closed.");

        invalid = entities[1].Clone();
        invalid.QuestMarkerFlags =
            CoopCampaignMapPrototypeContract.MaxQuestMarkerFlags + 1;
        Assert(
            !CoopCampaignMapPrototypeContract.IsValidVisibleEntity(invalid),
            "Unknown quest marker bits must fail closed.");

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

    private static void ValidateTransientBridgeGapFallback()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "CoopCampaignMapPrototypeBridgeTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        CoopSpectator.Infrastructure.Automation.CoopAutomationRuntimeBridge
            .TestCoopFolderPath = directory;

        try
        {
            DateTime utcNow = new DateTime(
                2026,
                9,
                26,
                1,
                30,
                0,
                DateTimeKind.Utc);
            TimeSpan maximumAge = TimeSpan.FromSeconds(2d);
            CoopCampaignMapPrototypeHostSnapshot expected =
                CreateHostSnapshot(utcNow);

            Assert(
                CoopCampaignMapPrototypeBridgeFile.TryWrite(
                    expected,
                    out string reason),
                "A valid host bridge snapshot must be writable. Reason=" +
                reason);
            Assert(
                CoopCampaignMapPrototypeBridgeFile.TryReadFresh(
                    utcNow,
                    maximumAge,
                    out CoopCampaignMapPrototypeHostSnapshot firstRead,
                    out reason) &&
                firstRead.Revision == expected.Revision,
                "The first host bridge read must populate the validated cache. Reason=" +
                reason);

            File.Delete(CoopCampaignMapPrototypeBridgeFile.GetStateFilePath());
            Assert(
                CoopCampaignMapPrototypeBridgeFile.TryReadFresh(
                    utcNow + TimeSpan.FromMilliseconds(250d),
                    maximumAge,
                    out CoopCampaignMapPrototypeHostSnapshot cachedRead,
                    out reason) &&
                cachedRead.Revision == expected.Revision,
                "A transient replacement gap must reuse the last validated fresh snapshot. Reason=" +
                reason);
            Assert(
                !CoopCampaignMapPrototypeBridgeFile.TryReadFresh(
                    utcNow + TimeSpan.FromSeconds(3d),
                    maximumAge,
                    out _,
                    out reason) &&
                reason == "missing-file",
                "An expired cached snapshot must fail closed when the bridge file stays absent.");

            expected.UpdatedUtc = utcNow + TimeSpan.FromSeconds(4d);
            expected.Revision++;
            Assert(
                CoopCampaignMapPrototypeBridgeFile.TryWrite(
                    expected,
                    out reason) &&
                CoopCampaignMapPrototypeBridgeFile.TryReadFresh(
                    expected.UpdatedUtc,
                    maximumAge,
                    out firstRead,
                    out reason),
                "A newer valid bridge snapshot must replace the expired cache. Reason=" +
                reason);
            File.WriteAllText(
                CoopCampaignMapPrototypeBridgeFile.GetStateFilePath(),
                "truncated");
            Assert(
                CoopCampaignMapPrototypeBridgeFile.TryReadFresh(
                    expected.UpdatedUtc + TimeSpan.FromMilliseconds(250d),
                    maximumAge,
                    out cachedRead,
                    out reason) &&
                cachedRead.Revision == expected.Revision,
                "A transient partial replacement must not discard the last validated fresh snapshot. Reason=" +
                reason);
        }
        finally
        {
            CoopSpectator.Infrastructure.Automation.CoopAutomationRuntimeBridge
                .TestCoopFolderPath = null;
            Directory.Delete(directory, recursive: true);
        }
    }

    internal static CoopCampaignMapPrototypeHostSnapshot CreateHostSnapshot(
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
                Conversation = CreateConversationState(3),
                UpdatedUtc = updatedUtc
            };
    }

    private static CoopCampaignMapConversationState CreateConversationState(
        int revision)
    {
        var state = new CoopCampaignMapConversationState
        {
            Revision = revision,
            IsActive = true,
            SpeakerName = "Bandit Leader",
            DialogText = "You have cut quite a swathe through my men.",
            GoldText = "1,234",
            ContinueText = "Click to continue"
        };
        state.Options.Add(
            new CoopCampaignMapConversationOptionState
            {
                Text = "Very well.",
                IsEnabled = true,
                IsSpecial = false
            });
        state.Options.Add(
            new CoopCampaignMapConversationOptionState
            {
                Text = "I do not fight duels with brigands.",
                IsEnabled = false,
                IsSpecial = true
            });
        return state;
    }

    private static CoopCampaignMapPrototypeCatalogEntityState
        CreateCatalogParty(int index)
    {
        return new CoopCampaignMapPrototypeCatalogEntityState
        {
            EntityId = "party:CharacterObject_1600_party_" + index,
            DisplayName = "Wahan of the Beni Zilal's Party " + index,
            Kind = CoopCampaignMapPrototypeEntityKind.MobileParty,
            SettlementNameplateSize =
                CoopCampaignMapPrototypeSettlementNameplateSize.None,
            SettlementKind = CoopCampaignMapPrototypeSettlementKind.None,
            PrimaryColor = 0xFF102030u,
            SecondaryColor = 0xFF405060u,
            BannerCode =
                "11.163.166.1528.1528.764.764.1.0.0.133.171.171.483.483.764.764.0.0.0",
            VisualCharacterId = "lord_1_1",
            CultureId = "aserai",
            PartyVisualKind =
                CoopCampaignMapPrototypePartyVisualKind.Mounted,
            HumanVisual = CreateHumanVisualState(),
            MountVisual = CreateMountVisualState(
                "campaign_horse",
                "campaign_harness"),
            CaravanMountVisual = null,
            FactionId = "aserai",
            FactionName = "Aserai",
            OwnerName = "Beni Zilal",
            LeaderName = "Wahan",
            ArmyId = index % 7 == 0 ? "army:aserai-main" : string.Empty,
            ArmyName = index % 7 == 0 ? "Aserai Army" : string.Empty,
            IsArmyLeader = index % 7 == 0,
            SelectionRadius = 18000
        };
    }

    private static bool CatalogEntitiesEqual(
        IReadOnlyList<CoopCampaignMapPrototypeCatalogEntityState> left,
        IReadOnlyList<CoopCampaignMapPrototypeCatalogEntityState> right)
    {
        if (left == null || right == null || left.Count != right.Count)
            return false;
        for (int index = 0; index < left.Count; index++)
        {
            CoopCampaignMapPrototypeCatalogEntityState leftEntity =
                left[index];
            CoopCampaignMapPrototypeCatalogEntityState rightEntity =
                right[index];
            if (leftEntity == null ||
                rightEntity == null ||
                leftEntity.EntityId != rightEntity.EntityId ||
                leftEntity.DisplayName != rightEntity.DisplayName ||
                leftEntity.Kind != rightEntity.Kind ||
                leftEntity.SettlementNameplateSize !=
                    rightEntity.SettlementNameplateSize ||
                leftEntity.SettlementKind != rightEntity.SettlementKind ||
                leftEntity.PrimaryColor != rightEntity.PrimaryColor ||
                leftEntity.SecondaryColor != rightEntity.SecondaryColor ||
                leftEntity.NameplateColor != rightEntity.NameplateColor ||
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
                    rightEntity.CaravanMountVisual) ||
                leftEntity.FactionId != rightEntity.FactionId ||
                leftEntity.FactionName != rightEntity.FactionName ||
                leftEntity.OwnerName != rightEntity.OwnerName ||
                leftEntity.LeaderName != rightEntity.LeaderName ||
                leftEntity.ArmyId != rightEntity.ArmyId ||
                leftEntity.ArmyName != rightEntity.ArmyName ||
                leftEntity.IsArmyLeader != rightEntity.IsArmyLeader ||
                leftEntity.SelectionRadius != rightEntity.SelectionRadius ||
                leftEntity.SettlementProductionIconId !=
                    rightEntity.SettlementProductionIconId)
            {
                return false;
            }
        }
        return true;
    }

    private static bool ByteArraysEqual(byte[] left, byte[] right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left == null || right == null || left.Length != right.Length)
            return false;
        for (int index = 0; index < left.Length; index++)
        {
            if (left[index] != right[index])
                return false;
        }
        return true;
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
                    CoopCampaignMapPrototypePartyVisualKind.None,
                QuestMarkerFlags =
                    (int)CoopCampaignMapPrototypeQuestMarkerFlags
                        .TrackedIssue,
                SettlementEventOrder = 2,
                SettlementQuestOrder = 4,
                SettlementEventFlags =
                    (int)CoopCampaignMapPrototypeSettlementEventFlags
                        .AvailableIssue
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
                leftEntity.IsPartyInformationHidden != rightEntity.IsPartyInformationHidden ||
                leftEntity.IsSettlementTracked != rightEntity.IsSettlementTracked ||
                leftEntity.SettlementRelation != rightEntity.SettlementRelation ||
                leftEntity.IsSettlementInRange != rightEntity.IsSettlementInRange ||
                leftEntity.PrimaryColor != rightEntity.PrimaryColor ||
                leftEntity.SecondaryColor != rightEntity.SecondaryColor ||
                leftEntity.NameplateColor != rightEntity.NameplateColor ||
                leftEntity.BannerCode != rightEntity.BannerCode ||
                leftEntity.VisualCharacterId !=
                    rightEntity.VisualCharacterId ||
                leftEntity.CultureId != rightEntity.CultureId ||
                leftEntity.PartyVisualKind != rightEntity.PartyVisualKind ||
                leftEntity.QuestMarkerFlags != rightEntity.QuestMarkerFlags ||
                leftEntity.SettlementEventOrder != rightEntity.SettlementEventOrder ||
                leftEntity.SettlementQuestOrder != rightEntity.SettlementQuestOrder ||
                leftEntity.SettlementEventFlags !=
                    rightEntity.SettlementEventFlags ||
                leftEntity.SettlementProductionIconId !=
                    rightEntity.SettlementProductionIconId ||
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

    private static void ValidateClientSceneInitializationParity()
    {
        string repositoryRoot = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "UI",
            "CoopCampaignMapPrototypeMissionView.cs"));

        int sceneRead = source.IndexOf(
            "_mapScene.Read(",
            StringComparison.Ordinal);
        int atmosphereLoad = source.IndexOf(
            "MBMapScene.LoadAtmosphereData(_mapScene)",
            StringComparison.Ordinal);
        int sceneOptimize = source.IndexOf(
            "_mapScene.OptimizeScene(",
            StringComparison.Ordinal);
        int visualTick = source.IndexOf(
            "MBMapScene.TickVisuals(",
            StringComparison.Ordinal);

        Assert(
            sceneRead >= 0 &&
            atmosphereLoad > sceneRead &&
            sceneOptimize > atmosphereLoad &&
            visualTick > sceneOptimize,
            "The isolated Main_map client must load native atmosphere data " +
            "after Scene.Read and before optimization or visual ticking.");

        int circleCleanup = source.IndexOf(
            "RemoveSettlementAuthoringCircles();", StringComparison.Ordinal);
        int createLayer = source.IndexOf(
            "_sceneLayer = new PassiveSceneLayer(", StringComparison.Ordinal);
        Assert(circleCleanup > sceneRead && circleCleanup < atmosphereLoad &&
               circleCleanup < createLayer &&
               source.LastIndexOf("RemoveSettlementAuthoringCircles();",
                   StringComparison.Ordinal) == circleCleanup,
            "Native settlement authoring circles must be cleaned once, after " +
            "isolated scene read and before rendering; never each map tick.");
        int cleanupMethod = source.IndexOf(
            "private void RemoveSettlementAuthoringCircles()", StringComparison.Ordinal);
        int nextMethod = source.IndexOf(
            "private void CreateSettlementOverlayLayer()", cleanupMethod,
            StringComparison.Ordinal);
        string cleanup = source.Substring(cleanupMethod, nextMethod - cleanupMethod);
        Assert(cleanup.Contains("new List<GameEntity>(") &&
               cleanup.Contains("_mapScene.FindEntitiesWithTag(\"map_settlement_circle\")") &&
               cleanup.Contains("circle.SetVisibilityExcludeParents(false)") &&
               cleanup.Contains("circle.Remove(112)") &&
               !cleanup.Contains("Mission.Scene") &&
               !cleanup.Contains("Campaign.Current") &&
               !cleanup.Contains("decal_siege"),
            "Circle cleanup must snapshot exact-tag helpers from the owned " +
            "replica scene, not mutate campaign/battle scenes or siege decals.");
    }

    private static void ValidateClientInteractionSourceContract()
    {
        string repositoryRoot = FindRepositoryRoot();
        string missionView = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "UI",
            "CoopCampaignMapPrototypeMissionView.cs"));
        string infoViewModel = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "UI",
            "CoopCampaignMapReplicaInfoVM.cs"));
        string infoPrefab = File.ReadAllText(Path.Combine(
            repositoryRoot, "Module", "CoopSpectator", "GUI", "Prefabs",
            "CoopCampaignMapReplicaInfo.xml"));

        Assert(
            infoPrefab.Contains("ScaledPositionXOffset=\"@PositionX\"") &&
            infoPrefab.Contains("ScaledPositionYOffset=\"@PositionY\"") &&
            !infoPrefab.Contains("Position=\"@Position\"") &&
            infoPrefab.Contains("SuggestedWidth=\"@PanelWidth\"") &&
            infoPrefab.Contains("SuggestedHeight=\"@PanelHeight\"") &&
            infoViewModel.Contains("public float PositionX") &&
            infoViewModel.Contains("public float PositionY") &&
            infoViewModel.Contains("ResolveInformationPanelPosition(") &&
            missionView.Contains("_replicaInfoLayer?.UIContext?.CustomScale"),
            "Generic Widget has no Position property: replica info must bind physical-pixel " +
            "offsets and clamp with the same scaled dimensions used by Gauntlet.");

        Assert(
            missionView.Contains(
                "using GlobalInput = TaleWorlds.InputSystem.Input;") &&
            missionView.Contains("IsFocusLayer = true") &&
            missionView.Contains(
                "InputRestrictions.SetInputRestrictions(") &&
            missionView.Contains("InputUsageMask.All") &&
            missionView.Contains("GlobalInput.DeltaMouseScroll") &&
            missionView.Contains("InputKey.MouseScrollUp") &&
            missionView.Contains("InputKey.MouseScrollDown"),
            "The replica input layer must own focus and expose bounded wheel zoom.");
        Assert(
            missionView.Contains("forward.z = 0f") &&
            missionView.Contains("InputKey.Up") &&
            missionView.Contains("InputKey.Down") &&
            missionView.Contains("InputKey.Left") &&
            missionView.Contains("InputKey.Right") &&
            missionView.Contains("ClampCameraOriginToMap"),
            "Replica keyboard panning must be planar, bounded and available on WASD/arrows.");
        Assert(
            missionView.Contains(
                "CoopCampaignMapPrototypeInteractionPolicy") &&
            missionView.Contains("ResolveNativeHoveredReplica()") &&
            infoViewModel.Contains("коліщатко — масштаб") &&
            infoViewModel.Contains("ЛКМ — відомості"),
            "Native picking and discoverable interaction guidance must remain wired.");
    }

    private static void ValidateMarkerReplicationSourceContract()
    {
        string repositoryRoot = FindRepositoryRoot();
        string publisher = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Campaign",
            "CoopCampaignMapPrototypePublisherBehavior.cs"));
        string bridgeCodec = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Infrastructure",
            "CoopCampaignMapPrototypeBridgeCodec.cs"));
        string replicaMessages = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Network",
            "Messages",
            "CoopCampaignMapReplicaNetworkMessages.cs"));
        string networkController = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Mission",
            "CoopCampaignMapPrototypeNetworkController.cs"));
        string partyOverlay = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "UI",
            "CoopCampaignMapPrototypeEntityOverlayVM.cs"));
        string settlementOverlay = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "UI",
            "CoopCampaignMapPrototypeSettlementOverlayVM.cs"));

        Assert(
            publisher.Contains("CampaignUIHelper.GetQuestsRelatedToParty") &&
            publisher.Contains("nameplate?.SettlementEvents?.EventsList") &&
            publisher.Contains("nameplate?.SettlementEvents?.TrackQuests") &&
            publisher.Contains("item.AdditionalParameters") &&
            publisher.IndexOf(
                ".ShouldReplicateSettlement(",
                StringComparison.Ordinal) <
            publisher.IndexOf(
                "CaptureSettlementInformation(settlement, settlementNameplates)",
                StringComparison.Ordinal),
            "Settlement markers must read existing host lists only after visibility filtering; party quest capture is unchanged.");
        Assert(
            bridgeCodec.Contains("QuestMarkerFlags") &&
            bridgeCodec.Contains("SettlementEventFlags") &&
            bridgeCodec.Contains("SettlementProductionIconId") &&
            replicaMessages.Contains("QuestMarkerCompression") &&
            replicaMessages.Contains("SettlementEventCompression") &&
            networkController.Contains(
                "CoopCampaignMapReplicaStateProjection.Merge("),
            "Every marker field must cross bridge, network and client merge boundaries.");
        Assert(
            partyOverlay.Contains(
                "SynchronizeQuestMarkers(entity.QuestMarkerFlags)") &&
            settlementOverlay.Contains(
                "SettlementEvents.Synchronize(") &&
            settlementOverlay.Contains(
                "CoopCampaignMapPrototypeSettlementEventItemVM"),
            "Replicated marker state must reach both party and settlement nameplate view models.");
    }

    private static void ValidateConversationReplicationSourceContract()
    {
        string repositoryRoot = FindRepositoryRoot();
        string capture = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Campaign",
            "CoopCampaignMapConversationCapture.cs"));
        string publisher = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Campaign",
            "CoopCampaignMapPrototypePublisherBehavior.cs"));
        string bridgeCodec = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Infrastructure",
            "CoopCampaignMapPrototypeBridgeCodec.cs"));
        string messages = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Network",
            "Messages",
            "CoopCampaignMapConversationNetworkMessages.cs"));
        string controller = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "Mission",
            "CoopCampaignMapPrototypeNetworkController.cs"));
        string view = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "UI",
            "CoopCampaignMapConversationView.cs"));
        string viewModel = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "UI",
            "CoopCampaignMapConversationVM.cs"));
        string mode = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "GameMode",
            "MissionMultiplayerCoopCampaignMapPrototypeMode.cs"));
        int conversationUpdateStart = publisher.IndexOf(
            "private void UpdateConversationSnapshot()",
            StringComparison.Ordinal);
        int conversationUpdateEnd = publisher.IndexOf(
            "private void LogFailureRateLimited(",
            conversationUpdateStart,
            StringComparison.Ordinal);
        string conversationUpdate =
            conversationUpdateStart >= 0 &&
            conversationUpdateEnd > conversationUpdateStart
                ? publisher.Substring(
                    conversationUpdateStart,
                    conversationUpdateEnd - conversationUpdateStart)
                : string.Empty;
        int captureFailureLog = conversationUpdate.IndexOf(
            "conversation-capture:",
            StringComparison.Ordinal);
        int captureFailureReturn = captureFailureLog >= 0
            ? conversationUpdate.IndexOf(
                "return;",
                captureFailureLog,
                StringComparison.Ordinal)
            : -1;
        int conversationEquivalence = conversationUpdate.IndexOf(
            "AreConversationContentsEquivalent",
            StringComparison.Ordinal);

        Assert(
            capture.Contains("MissionConversationVM") &&
            capture.Contains("CurrentCharacterNameLbl") &&
            capture.Contains("dialog.DialogText") &&
            capture.Contains("dialog.AnswerList") &&
            capture.Contains("dataSource.IsBarterActive") &&
            !capture.Contains("DoOption(") &&
            !capture.Contains("ContinueConversation(") &&
            captureFailureReturn > captureFailureLog &&
            captureFailureReturn < conversationEquivalence &&
            conversationUpdate.Contains("if (!_clearConversationOnCaptureFailure) return;") &&
            conversationUpdate.Contains("captured = CoopCampaignMapPrototypeContract.CreateInactiveConversation();") &&
            conversationUpdate.Contains("pose = null;"),
            "Host capture must read prepared native text without advancing; only the mission/return boundary clears failed capture.");
        Assert(
            bridgeCodec.Contains("ConversationRevision") &&
            bridgeCodec.Contains("ConversationOptionCount") &&
            messages.Contains(
                "GameNetworkMessageSendType.FromServer") &&
            !messages.Contains("GameNetworkMessageSendType.FromClient") &&
            controller.Contains("BroadcastConversation(") &&
            controller.Contains("SendConversation(networkPeer") &&
            controller.Contains("ClientConversationAssembler"),
            "Dialogue must cross the existing authoritative bridge and server-only network projection, including late join.");
        Assert(
            view.Contains("LoadMovie(") &&
            view.Contains("\"SPConversation\"") &&
            view.Contains("SetConversationActive(true)") &&
            view.Contains("RestoreMissionLayerState") &&
            viewModel.Contains("public void ExecuteAction()") &&
            viewModel.Contains(
                "Display-only milestone: no client choice leaves this process.") &&
            mode.Contains("new CoopSpectator.UI.CoopCampaignMapConversationView()"),
            "The map mission must reuse the native conversation window and keep every remote answer display-only.");

        string hideoutView = File.ReadAllText(Path.Combine(
            repositoryRoot,
            "UI",
            "CoopHideoutBossCinematicView.cs"));
        Assert(
            view.Contains("MBInformationManager.HideInformations();") &&
            viewModel.Contains("new CoopCampaignMapConversationEmptyPersuasionVM()") &&
            viewModel.Contains("bool IsABlockingOption => false") &&
            viewModel.Contains("bool IsAProgressingOption => false") &&
            viewModel.Contains("int PersuasionResultIndex => -1") &&
            !viewModel.Contains("Only the campaign host can choose this response.") &&
            !viewModel.Contains("PersuasionItem => null"),
            "Ordinary read-only answers must not invent persuasion icons or leave " +
            "a dialogue hint alive after its layer closes.");
        Assert(
            hideoutView.Contains("CoopHideoutBossConversationVM") &&
            hideoutView.Contains("SendHostChoiceWithConfirmation"),
            "The map-dialogue feature must not replace the adjacent hideout host-choice path.");
    }

    private static void ValidateHostBannerColorPreservation()
    {
        string repositoryRoot = FindRepositoryRoot();
        foreach (string file in new[] {
            "CoopCampaignMapPrototypeEntityOverlayVM.cs",
            "CoopCampaignMapPrototypeSettlementOverlayVM.cs" })
        {
            string source = File.ReadAllText(Path.Combine(repositoryRoot, "UI", file));
            int start = source.IndexOf("private void UpdateBanner(", StringComparison.Ordinal);
            int end = source.IndexOf("catch", start, StringComparison.Ordinal);
            string factory = source.Substring(start, end - start);
            Assert(factory.Contains(": new Banner(bannerCode)"),
                file + " preserves encoded host banner colors without faction overrides");
            Assert(factory.Contains("string.IsNullOrWhiteSpace(bannerCode)") &&
                   factory.Contains("? new Banner(FallbackBannerCode, primaryColor, secondaryColor)"),
                file + " confines faction recoloring to the pre-existing missing-banner fallback");
            Assert(!factory.Contains("new Banner(\n                        safeBannerCode,") &&
                   !factory.Contains("new Banner(\r\n                        safeBannerCode,"),
                file + " must not recolor a supplied host banner");
        }
    }

    private static string FindRepositoryRoot()
    {
        string configuredRoot = Environment.GetEnvironmentVariable(
            "COOPSPECTATOR_REPOSITORY_ROOT");
        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            string resolvedRoot = Path.GetFullPath(configuredRoot);
            if (File.Exists(Path.Combine(resolvedRoot, "CoopSpectator.csproj")))
                return resolvedRoot;
            throw new InvalidOperationException(
                "COOPSPECTATOR_REPOSITORY_ROOT does not identify this repository.");
        }

        DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null &&
               !File.Exists(Path.Combine(
                   directory.FullName,
                   "CoopSpectator.csproj")))
        {
            directory = directory.Parent;
        }

        if (directory == null)
        {
            throw new InvalidOperationException(
                "Could not locate the repository root for source contract checks.");
        }

        return directory.FullName;
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
