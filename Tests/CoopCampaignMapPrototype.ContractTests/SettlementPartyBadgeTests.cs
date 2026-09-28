using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoopSpectator.Infrastructure;

internal static class SettlementPartyBadgeTests
{
    private const string BannerCode = "11.163.166.1528.1528.764.764.1.0.0";
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Settlement badges: " + message);
    }

    public static void Run()
    {
        // Exhaustive native IsMobilePartyValid truth table plus the range owner gate.
        for (int mask = 0; mask < 128; mask++)
        {
            bool range = (mask & 1) != 0, garrison = (mask & 2) != 0,
                militia = (mask & 4) != 0, main = (mask & 8) != 0,
                disguised = (mask & 16) != 0, army = (mask & 32) != 0,
                mainLeader = (mask & 64) != 0;
            bool expected = false;
            if (range && !garrison && !militia)
                if (!main || !disguised)
                    expected = army ? mainLeader && !disguised : true;
            Check(CoopCampaignMapSettlementPartyBadgePolicy.IsAdmitted(range, garrison, militia,
                main, disguised, army, mainLeader) == expected, "host admission mask " + mask);
        }

        var badges = new List<CoopCampaignMapSettlementPartyBadge>
        {
            new CoopCampaignMapSettlementPartyBadge { Kind = 0, BannerCode = BannerCode },
            new CoopCampaignMapSettlementPartyBadge { Kind = 0 }, // Native null banner, no invented coat.
            new CoopCampaignMapSettlementPartyBadge { Kind = 1 },
            new CoopCampaignMapSettlementPartyBadge { Kind = 2, IsBandit = true },
            new CoopCampaignMapSettlementPartyBadge { Kind = 2, IsBandit = true } // Preserve multiplicity.
        };
        Check(CoopCampaignMapSettlementPartyBadgePolicy.TryEncode(badges, out var encoded), "encode");
        Check(CoopCampaignMapSettlementPartyBadgePolicy.TryDecode(encoded, out var decoded) &&
            decoded.Count == 5 && decoded[0].BannerCode == BannerCode && decoded[1].BannerCode == "" &&
            decoded[2].Kind == 1 && decoded[3].IsBandit && decoded[4].IsBandit, "ordered roundtrip");
        foreach (string malformed in new[] { null, ";", "3,0,", "0,2,", "0,0,x", "0,0,1.2",
            "2,0," + BannerCode, "0,0," + BannerCode + ".", "0,0," + BannerCode.Replace("163", "2147483648"),
            "0,0," + BannerCode.Replace("163", " 163"), new string('x', CoopCampaignMapSettlementPartyBadgePolicy.MaxEncodedCharacters + 1) })
            Check(!CoopCampaignMapSettlementPartyBadgePolicy.TryDecode(malformed, out _), "malformed rejection");
        var maximum = Enumerable.Range(0, CoopCampaignMapSettlementPartyBadgePolicy.MaxBadges)
            .Select(_ => badges[0]).ToList();
        Check(CoopCampaignMapSettlementPartyBadgePolicy.TryEncode(maximum, out var large), "maximum admitted count");
        maximum.Add(badges[0]);
        Check(!CoopCampaignMapSettlementPartyBadgePolicy.TryEncode(maximum, out _), "count overflow");
        Check(!CoopCampaignMapSettlementPartyBadgePolicy.TryDecode(large + ";2,0,", out _), "decode count overflow");

        var host = Program.CreateHostSnapshot(DateTime.UtcNow);
        var visible = host.VisibleEntities[1];
        visible.IsSettlementInRange = true;
        visible.SettlementPartyBadges = encoded;
        Check(CoopCampaignMapPrototypeContract.IsValidVisibleEntity(visible), "valid visible settlement");
        Check(visible.Clone().SettlementPartyBadges == encoded, "visible clone");
        Check(CoopCampaignMapPrototypeBridgeCodec.TryParse(CoopCampaignMapPrototypeBridgeCodec.Serialize(host),
            out var hostRoundtrip, out var reason) && hostRoundtrip.VisibleEntities[1].SettlementPartyBadges == encoded,
            "full host bridge: " + reason);
        visible.IsSettlementInRange = false;
        Check(!CoopCampaignMapPrototypeContract.IsValidVisibleEntity(visible), "out of range rejects badges");

        var catalog = new CoopCampaignMapPrototypeCatalogEntityState
        {
            EntityId = visible.EntityId, DisplayName = visible.DisplayName,
            Kind = CoopCampaignMapPrototypeEntityKind.Settlement,
            SettlementKind = CoopCampaignMapPrototypeSettlementKind.Town,
            SettlementNameplateSize = CoopCampaignMapPrototypeSettlementNameplateSize.Large,
            BannerCode = BannerCode, CultureId = "", VisualCharacterId = "",
            FactionId = "", FactionName = "", OwnerName = "", LeaderName = "", ArmyId = "", ArmyName = "",
            SettlementPartyBadges = encoded
        };
        Check(CoopCampaignMapPrototypeContract.IsValidCatalogEntity(catalog), "catalog admission");
        Check(catalog.Clone().SettlementPartyBadges == encoded, "catalog clone");
        var snapshot = new CoopCampaignMapPrototypeCatalogSnapshot
        {
            SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
            SessionId = Guid.NewGuid().ToString("N"), Revision = 8, UpdatedUtc = DateTime.UtcNow,
            Entities = new List<CoopCampaignMapPrototypeCatalogEntityState> { catalog }
        };
        Check(CoopCampaignMapPrototypeBridgeCodec.TryParseCatalog(CoopCampaignMapPrototypeBridgeCodec.SerializeCatalog(snapshot),
            out var parsed, out reason) && parsed.Entities[0].SettlementPartyBadges == encoded, "catalog bridge: " + reason);
        Check(CoopCampaignMapCatalogBinarySerializer.TrySerialize(8, snapshot.Entities, out var bytes, out reason), "binary encode: " + reason);
        Check(CoopCampaignMapCatalogBinarySerializer.TryDeserialize(bytes, out var revision, out var roundtrip, out reason) &&
            revision == 8 && roundtrip[0].SettlementPartyBadges == encoded, "binary decode: " + reason);
        catalog.SettlementPartyBadges = large;
        Check(CoopCampaignMapCatalogBinarySerializer.TrySerialize(9, snapshot.Entities, out bytes, out reason), "large encode");
        Check(CoopCampaignMapCatalogBinarySerializer.TryDeserialize(bytes, out revision, out roundtrip, out reason) &&
            roundtrip[0].SettlementPartyBadges == large, "large binary roundtrip");
        Check(CoopCampaignMapCatalogChunkCodec.TryEncode(bytes, out var chunked, out reason) &&
            chunked.Chunks.All(c => c.Length <= CoopCampaignMapCatalogChunkCodec.MaxChunkBytes), "bounded native chunks");

        var state = new CoopCampaignMapPrototypeDynamicEntityState { EntityId = catalog.EntityId, IsVisible = true, IsSettlementInRange = true };
        Check(CoopCampaignMapReplicaStateProjection.Merge(catalog, state).SettlementPartyBadges == large, "admitted merge");
        state.IsSettlementInRange = false;
        Check(CoopCampaignMapReplicaStateProjection.Merge(catalog, state).SettlementPartyBadges == "", "range loss clears cached badges");
        state.IsVisible = false;
        Check(CoopCampaignMapReplicaStateProjection.Merge(catalog, state) == null, "hidden root cannot reveal badges");
        state.IsVisible = true;
        state.IsSettlementInRange = true;
        var cleared = catalog.Clone();
        cleared.SettlementPartyBadges = "";
        Check(!CoopCampaignMapPrototypeCatalogDeltaPolicy.AreEquivalent(catalog, cleared) &&
            CoopCampaignMapPrototypeCatalogDeltaPolicy.BuildDelta(new[] { catalog }, new[] { cleared }).Count == 1,
            "departures trigger catalog delta");
        Check(CoopCampaignMapReplicaStateProjection.Merge(cleared, state).SettlementPartyBadges == "", "cleared catalog stays empty");
        catalog.Kind = CoopCampaignMapPrototypeEntityKind.MobileParty;
        Check(!CoopCampaignMapPrototypeContract.IsValidCatalogEntity(catalog), "nonsettlement badge injection");

        var root = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CoopSpectator.csproj"))) root = root.Parent;
        Check(root != null, "repository root");
        string controller = File.ReadAllText(Path.Combine(root.FullName, "Mission/CoopCampaignMapPrototypeNetworkController.cs"));
        int send = controller.IndexOf("private static bool SendCatalogDelta", StringComparison.Ordinal);
        string direct = controller.Substring(send, controller.IndexOf("private static void BroadcastDynamic", send, StringComparison.Ordinal) - send);
        Check(direct.IndexOf("entities[index]?.SettlementPartyBadges", StringComparison.Ordinal) >= 0 &&
            direct.IndexOf("entities[index]?.SettlementPartyBadges", StringComparison.Ordinal) <
            direct.IndexOf("GameNetwork.BeginModuleEventAsServer", StringComparison.Ordinal), "chunk routing before native header");
        string ui = File.ReadAllText(Path.Combine(root.FullName, "UI/CoopCampaignMapPrototypeSettlementOverlayVM.cs"));
        Check(ui.Contains("SettlementParties.Synchronize(entity.SettlementPartyBadges, entity.IsSettlementInRange)") &&
            ui.Contains("badge.BannerCode.Length == 0 ? null : new Banner(badge.BannerCode)"), "stock binding and exact heraldry");
        string publisher = File.ReadAllText(Path.Combine(root.FullName, "Campaign/CoopCampaignMapPrototypePublisherBehavior.cs"));
        int captureStart = publisher.IndexOf("private static string CaptureSettlementPartyBadges", StringComparison.Ordinal);
        string capture = publisher.Substring(captureStart,
            publisher.IndexOf("private static int CaptureSettlementLevels", captureStart, StringComparison.Ordinal) - captureStart);
        Check(capture.Contains("source.GetNameplateOfSettlement(settlement)") &&
            capture.Contains("!nameplate.IsInRange") && capture.Contains("nameplate.SettlementParties?.PartiesInSettlement") &&
            capture.Contains("foreach (SettlementNameplatePartyMarkerItemVM item in items)") &&
            capture.Contains("item.Visual.Id") && !capture.Contains("badges.Sort(") &&
            !capture.Contains("foreach (MobileParty party in settlement.Parties)"), "read existing ordered host grid without resimulation");
        Check(!capture.Contains("RegisterEvents(") && !capture.Contains("PopulatePartyList(") &&
            !capture.Contains("RefreshDynamicProperties("), "capture must not mutate host UI");
        Console.WriteLine("Settlement badge admission/codec/transport/projection contracts passed.");
    }
}
