using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoopSpectator.Infrastructure;

internal static class HoverDecalTests
{
    private static int _checks;
    private static void Check(bool value, string message)
    { _checks++; if (!value) throw new Exception("Hover decal: " + message); }

    internal static void Run()
    {
        for (int flags = 0; flags < 8; flags++)
            Check(CoopCampaignMapHoverDecal.ResolveColor((flags & 1) != 0, (flags & 2) != 0, (flags & 4) != 0) ==
                ((flags & 1) != 0 ? 4292093218u : (flags & 2) != 0 ? 4284183827u :
                 (flags & 4) != 0 ? 4279386828u : 4291596077u), "native diplomacy precedence");
        var descriptor = new CoopCampaignMapHoverDecal {
            Color = 4291596077u, Frame = new[] { 1.75f, 0, 0, 0, 1.75f, 0, 0, 0, 1.75f, 0, 0, 0 }
        };
        Check(CoopCampaignMapHoverDecal.TryEncode(descriptor, out var encoded), "valid native descriptor");
        Check(encoded.Length == 72 && CoopCampaignMapHoverDecal.TryDecode(encoded, out var decoded) &&
            decoded.Color == descriptor.Color && !decoded.Fortification && decoded.Frame.SequenceEqual(descriptor.Frame), "exact float roundtrip");
        Check(CoopCampaignMapHoverDecal.TryDecode("", out decoded) && decoded == null, "absent means no fabricated ring");
        foreach (string invalid in new[] { null, "x", new string('A', 72), encoded + "x", " " + encoded.Substring(1) })
            Check(!CoopCampaignMapHoverDecal.TryDecode(invalid, out _), "malformed rejection");
        // Exercise the receiving boundary independently of the validating encoder.
        byte[] wire = Convert.FromBase64String(encoded);
        foreach (byte shape in new byte[] { 0, 3, 255 })
        {
            byte[] corrupted = (byte[])wire.Clone(); corrupted[0] = shape;
            Check(!CoopCampaignMapHoverDecal.TryDecode(Convert.ToBase64String(corrupted), out decoded) &&
                decoded == null, "unknown wire shape rejected without partial output");
        }
        byte[] badColor = (byte[])wire.Clone();
        for (int i = 1; i <= 4; i++) badColor[i] = 255;
        Check(!CoopCampaignMapHoverDecal.TryDecode(Convert.ToBase64String(badColor), out decoded) &&
            decoded == null, "unknown wire palette rejected");
        for (int i = 0; i < 12; i++)
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity,
                i < 9 ? 65f : 100001f, i < 9 ? -65f : -100001f })
            {
                byte[] corrupted = (byte[])wire.Clone();
                using (var stream = new MemoryStream(corrupted, true))
                using (var writer = new BinaryWriter(stream))
                { stream.Position = 5 + i * 4; writer.Write(invalid); }
                Check(!CoopCampaignMapHoverDecal.TryDecode(Convert.ToBase64String(corrupted), out decoded) &&
                    decoded == null, "unsafe wire frame rejected without partial output");
            }
        for (int i = 0; i < 12; i++)
        {
            float old = descriptor.Frame[i];
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 100001f, -100001f })
            {
                descriptor.Frame[i] = invalid;
                Check(!CoopCampaignMapHoverDecal.TryEncode(descriptor, out _), "unsafe native frame rejected");
            }
            descriptor.Frame[i] = old;
        }
        descriptor.Frame[0] = 0;
        Check(!CoopCampaignMapHoverDecal.TryEncode(descriptor, out _), "degenerate basis rejected");
        descriptor.Frame[0] = 1.75f;
        descriptor.Color = uint.MaxValue;
        Check(!CoopCampaignMapHoverDecal.TryEncode(descriptor, out _), "unknown palette rejected");
        descriptor.Color = 4291596077u;
        descriptor.Fortification = true;
        Check(CoopCampaignMapHoverDecal.TryEncode(descriptor, out var fort) &&
            !CoopCampaignMapHoverDecal.IsValidForEntity(fort, CoopCampaignMapPrototypeEntityKind.MobileParty), "fort cannot alias party");

        var host = Program.CreateHostSnapshot(DateTime.UtcNow);
        var visible = host.VisibleEntities[1];
        visible.HoverDecal = encoded;
        Check(visible.Clone().HoverDecal == encoded, "visible clone");
        Check(CoopCampaignMapPrototypeBridgeCodec.TryParse(CoopCampaignMapPrototypeBridgeCodec.Serialize(host),
            out var parsedHost, out var reason) && parsedHost.VisibleEntities[1].HoverDecal == encoded, "host text roundtrip: " + reason);
        var catalog = new CoopCampaignMapPrototypeCatalogEntityState {
            EntityId = visible.EntityId, DisplayName = visible.DisplayName,
            Kind = CoopCampaignMapPrototypeEntityKind.Settlement,
            SettlementKind = CoopCampaignMapPrototypeSettlementKind.Village,
            SettlementNameplateSize = CoopCampaignMapPrototypeSettlementNameplateSize.Small,
            BannerCode = "", CultureId = "", VisualCharacterId = "", FactionId = "", FactionName = "",
            OwnerName = "", LeaderName = "", ArmyId = "", ArmyName = "", HoverDecal = encoded
        };
        Check(CoopCampaignMapPrototypeContract.IsValidCatalogEntity(catalog), "catalog admitted");
        Check(catalog.Clone().HoverDecal == encoded, "catalog clone");
        var snapshot = new CoopCampaignMapPrototypeCatalogSnapshot {
            SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
            SessionId = Guid.NewGuid().ToString("N"), Revision = 1, UpdatedUtc = DateTime.UtcNow,
            Entities = new List<CoopCampaignMapPrototypeCatalogEntityState> { catalog }
        };
        Check(CoopCampaignMapPrototypeBridgeCodec.TryParseCatalog(CoopCampaignMapPrototypeBridgeCodec.SerializeCatalog(snapshot),
            out var textRoundtrip, out reason) && textRoundtrip.Entities[0].HoverDecal == encoded, "catalog text roundtrip: " + reason);
        Check(CoopCampaignMapCatalogBinarySerializer.TrySerialize(1, snapshot.Entities, out var bytes, out reason), "binary encode: " + reason);
        Check(CoopCampaignMapCatalogBinarySerializer.TryDeserialize(bytes, out _, out var binary, out reason) &&
            binary[0].HoverDecal == encoded, "binary roundtrip: " + reason);
        Check(CoopCampaignMapCatalogChunkCodec.TryEncode(bytes, out var chunks, out reason) &&
            chunks.Chunks.All(c => c.Length <= CoopCampaignMapCatalogChunkCodec.MaxChunkBytes), "bounded catalog chunks");
        var dynamicState = new CoopCampaignMapPrototypeDynamicEntityState { EntityId = catalog.EntityId, IsVisible = true };
        Check(CoopCampaignMapReplicaStateProjection.Merge(catalog, dynamicState).HoverDecal == encoded, "admitted projection");
        dynamicState.IsVisible = false;
        Check(CoopCampaignMapReplicaStateProjection.Merge(catalog, dynamicState) == null, "hidden entity cannot display cached ring");
        var changed = catalog.Clone(); changed.HoverDecal = fort;
        Check(!CoopCampaignMapPrototypeCatalogDeltaPolicy.AreEquivalent(catalog, changed), "shape change causes delta");
        descriptor.Fortification = false;
        descriptor.Color = 4292093218u;
        Check(CoopCampaignMapHoverDecal.TryEncode(descriptor, out var recolored), "changed diplomacy encoded");
        changed.HoverDecal = recolored;
        Check(!CoopCampaignMapPrototypeCatalogDeltaPolicy.AreEquivalent(catalog, changed), "color-only change causes delta");
        descriptor.Color = 4291596077u;
        descriptor.Frame[9] = 123.25f;
        Check(CoopCampaignMapHoverDecal.TryEncode(descriptor, out var reframed), "changed native frame encoded");
        changed.HoverDecal = reframed;
        Check(!CoopCampaignMapPrototypeCatalogDeltaPolicy.AreEquivalent(catalog, changed), "frame-only change causes delta");
        changed.HoverDecal = "";
        Check(CoopCampaignMapPrototypeCatalogDeltaPolicy.BuildDelta(new[] { catalog }, new[] { changed }).Count == 1,
            "loss of native presentation propagates clear");

        string root = Environment.GetEnvironmentVariable("COOPSPECTATOR_REPOSITORY_ROOT");
        string view = File.ReadAllText(Path.Combine(root, "UI/CoopCampaignMapPrototypeMissionView.cs"));
        string decals = File.ReadAllText(Path.Combine(root, "UI/CoopCampaignMapPrototypeMissionView.HoverDecals.cs"));
        Check(decals.Contains("IsPickTargetAdmitted(_hoveredReplicaId)") && decals.Contains("!CanUseReplicaMapInput()"), "current admission before native render");
        Check(decals.Contains("GameEntity.CreateEmpty(_mapScene") && !decals.Contains("Mission.Scene") &&
            !decals.Contains("Campaign.Current"), "only owned isolated scene");
        int clearDecals = view.IndexOf("ClearNativeHoverDecals();", StringComparison.Ordinal);
        int clearPicking = view.IndexOf("ClearNativePicking();", StringComparison.Ordinal);
        Check(clearDecals >= 0 && clearPicking > clearDecals && view.Contains("UpdateNativeHoverDecal();"), "scene teardown and tick wiring");
        Console.WriteLine("Hover decal contracts passed: " + _checks);
    }
}
