using System;
using System.Linq;
using CoopSpectator.Infrastructure;

internal static class MovementTargetTransportTests
{
    private static int checks;
    internal static void Run()
    {
        Check(CoopCampaignMapMovementTargetCodec.TryEncode(null, out var empty) && empty == "", "explicit absence encoding");
        Check(CoopCampaignMapMovementTargetCodec.TryDecode("", out var absent) && absent == null, "explicit absence decoding");
        foreach (string bad in new[] { null, " ", "!", new string('A', 769) })
            Check(!CoopCampaignMapMovementTargetCodec.TryDecode(bad, out absent) && absent == null, "invalid encoded input");
        foreach (var kind in new[] { CoopCampaignMapMovementTargetKind.Point, CoopCampaignMapMovementTargetKind.Party, CoopCampaignMapMovementTargetKind.Settlement })
        {
            var recipe = Recipe(kind);
            Check(CoopCampaignMapMovementTargetCodec.TryEncode(recipe, out var encoded), "recipe encoding");
            Check(encoded.Length <= CoopCampaignMapMovementTargetCodec.MaxEncodedCharacters, "bounded packet");
            Check(CoopCampaignMapMovementTargetCodec.TryDecode(encoded, out var decoded) && decoded.Kind == kind && decoded.EntityId == recipe.EntityId, "roundtrip identity");
            if (kind == CoopCampaignMapMovementTargetKind.Party)
            {
                Check(decoded.VisualOriginOffset[0] == .5f && decoded.VisualOriginOffset[1] == -.2f, "native visual offset roundtrip");
                recipe.VisualOriginOffset[0] = 900;
                Check(decoded.VisualOriginOffset[0] == .5f, "offset owns decoded array");
            }
            Check(CoopCampaignMapMovementTargetCodec.TryEncode(decoded, out var again) && again == encoded, "canonical roundtrip");
            recipe.Origin[0] = 999;
            Check(decoded.Origin[0] == 12, "decode owns arrays");
            byte[] bytes = Convert.FromBase64String(encoded);
            for (int length = 1; length < bytes.Length; length++)
                Check(!CoopCampaignMapMovementTargetCodec.TryDecode(Convert.ToBase64String(bytes.Take(length).ToArray()), out absent) && absent == null, "all truncations rejected");
            Check(!CoopCampaignMapMovementTargetCodec.TryDecode(Convert.ToBase64String(bytes.Concat(new byte[] { 0 }).ToArray()), out absent), "trailing byte rejected");
            Check(!CoopCampaignMapMovementTargetCodec.TryDecode(encoded + "\n", out absent), "noncanonical base64 rejected");
            foreach (int offset in new[] { 0, 1, 2 })
            {
                byte[] malformed = (byte[])bytes.Clone(); malformed[offset] = 255;
                Check(!CoopCampaignMapMovementTargetCodec.TryDecode(Convert.ToBase64String(malformed), out absent), "version/kind/flags rejected");
            }
            byte[] oversizedId = (byte[])bytes.Clone(); oversizedId[3] = oversizedId[4] = 255;
            Check(!CoopCampaignMapMovementTargetCodec.TryDecode(Convert.ToBase64String(oversizedId), out absent), "length before ID allocation");
            SnapshotRoundtrip(encoded);
        }
        foreach (string id in new[] { "party:", "settlement:town", "party:\n", new string('a', 97), "party:\ud800", "Party:one" })
        {
            var recipe = Recipe(CoopCampaignMapMovementTargetKind.Party); recipe.EntityId = id;
            Check(!CoopCampaignMapMovementTargetCodec.TryEncode(recipe, out _), "invalid ID encoding");
        }
        var unicode = Recipe(CoopCampaignMapMovementTargetKind.Party);
        unicode.EntityId = "party:" + new string('\u4e2d', 90);
        Check(CoopCampaignMapMovementTargetCodec.TryEncode(unicode, out var unicodeEncoded) && unicodeEncoded.Length <= 768 &&
            CoopCampaignMapMovementTargetCodec.TryDecode(unicodeEncoded, out var unicodeDecoded) && unicodeDecoded.EntityId == unicode.EntityId, "maximum multi-byte ID");
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, -1d, 100000001d })
        {
            var recipe = Recipe(CoopCampaignMapMovementTargetKind.Settlement); recipe.AnimationElapsedSeconds = bad;
            Check(!CoopCampaignMapMovementTargetCodec.TryEncode(recipe, out _), "unused unsafe timing rejected");
        }
        foreach (float[] bad in new[] { null, new float[2], new[] { float.NaN, 0f, 0f }, new[] { 0f, 100001f, 0f } })
        {
            var recipe = Recipe(CoopCampaignMapMovementTargetKind.Party); recipe.VisualOriginOffset = bad;
            Check(!CoopCampaignMapMovementTargetCodec.TryEncode(recipe, out _), "invalid visual offset rejected");
        }
        var unexpectedOffset = Recipe(CoopCampaignMapMovementTargetKind.Point); unexpectedOffset.VisualOriginOffset = new float[3];
        Check(!CoopCampaignMapMovementTargetCodec.TryEncode(unexpectedOffset, out _), "point cannot carry party offset");
        Admission();
        Freshness();
        Console.WriteLine("Movement target transport checks: " + checks);
    }

    private static CoopCampaignMapMovementTargetPresentation Recipe(CoopCampaignMapMovementTargetKind kind) =>
        new CoopCampaignMapMovementTargetPresentation {
            Kind = kind, EntityId = kind == CoopCampaignMapMovementTargetKind.Point ? null :
                kind == CoopCampaignMapMovementTargetKind.Party ? "party:one" : "settlement:town",
            Color = kind == CoopCampaignMapMovementTargetKind.Point ? CoopCampaignMapMovementTargetLayout.PointColor : 4291596077u,
            Origin = new[] { 12f, 34f, 5f }, TerrainNormal = new[] { 0f, 0f, 1f },
            VisualOriginOffset = kind == CoopCampaignMapMovementTargetKind.Party ? new[] { .5f, -.2f, 0f } : null,
            HostCameraDistance = 20, AnimationElapsedSeconds = .35,
            CircleFrame = kind == CoopCampaignMapMovementTargetKind.Point ? null : new[] { 1f, 0, 0, 0, 1f, 0, 0, 0, 1f, 0, 0, 0 }
        };

    private static void SnapshotRoundtrip(string encoded)
    {
        DateTime now = DateTime.UtcNow;
        var host = Program.CreateHostSnapshot(now); host.MovementTarget = encoded;
        Check(CoopCampaignMapPrototypeContract.TryValidateHostSnapshot(host, now, TimeSpan.FromSeconds(3), out var reason), "valid host: " + reason);
        Check(host.Clone().MovementTarget == encoded, "host clone");
        string[] lines = CoopCampaignMapPrototypeBridgeCodec.Serialize(host);
        Check(CoopCampaignMapPrototypeBridgeCodec.TryParse(lines, out var restored, out reason) && restored.MovementTarget == encoded, "bridge roundtrip: " + reason);
        Check(!CoopCampaignMapPrototypeBridgeCodec.TryParse(lines.Where(x => !x.StartsWith("MovementTarget=")).ToArray(), out _, out _), "missing field rejected");
        Check(!CoopCampaignMapPrototypeBridgeCodec.TryParse(lines.Select(x => x.StartsWith("MovementTarget=") ? "MovementTarget=invalid" : x).ToArray(), out _, out _), "corrupt field rejected");
        var state = CoopCampaignMapPrototypeContract.ToNetworkState(host, 10);
        Check(state.MovementTarget == encoded && state.Clone().MovementTarget == encoded, "state projection and clone");
        Check(CoopCampaignMapPrototypeContract.CanAccept(null, state), "admitted ordered state");
        var stale = state.Clone(); stale.Revision = 9; stale.MovementTarget = "";
        Check(!CoopCampaignMapPrototypeContract.CanAccept(state, stale), "stale clear cannot replace current target");
        var clear = state.Clone(); clear.Revision = 11; clear.MovementTarget = "";
        Check(CoopCampaignMapPrototypeContract.CanAccept(state, clear), "newer cancellation admitted");
        Check(!CoopCampaignMapPrototypeContract.CanAccept(clear, state), "old target cannot revive after clear");
        clear.MovementTarget = null;
        Check(!CoopCampaignMapPrototypeContract.CanAccept(state, clear), "null wire rejected");
        host.MovementTarget = "invalid";
        Check(!CoopCampaignMapPrototypeContract.TryValidateHostSnapshot(host, now, TimeSpan.FromSeconds(3), out _), "corrupt host rejected");
        host.MovementTarget = encoded; host.SchemaVersion--;
        Check(!CoopCampaignMapPrototypeContract.TryValidateHostSnapshot(host, now, TimeSpan.FromSeconds(3), out _), "old schema rejected");
    }

    private static void Admission()
    {
        var recipe = Recipe(CoopCampaignMapMovementTargetKind.Party);
        var catalog = new CoopCampaignMapPrototypeCatalogEntityState { EntityId = "party:one", Kind = CoopCampaignMapPrototypeEntityKind.MobileParty };
        var dynamicState = new CoopCampaignMapPrototypeDynamicEntityState { EntityId = "party:one", IsVisible = true };
        Check(CoopCampaignMapMovementTargetCodec.IsAdmitted(recipe, catalog, dynamicState), "disclosed party");
        dynamicState.IsVisible = false;
        Check(!CoopCampaignMapMovementTargetCodec.IsAdmitted(recipe, catalog, dynamicState), "catalog alone does not disclose");
        dynamicState.IsVisible = true; dynamicState.EntityId = "party:other";
        Check(!CoopCampaignMapMovementTargetCodec.IsAdmitted(recipe, catalog, dynamicState), "mismatched dynamic ID");
        dynamicState.EntityId = "party:one"; catalog.Kind = CoopCampaignMapPrototypeEntityKind.Settlement;
        Check(!CoopCampaignMapMovementTargetCodec.IsAdmitted(recipe, catalog, dynamicState), "kind mismatch");
        recipe = Recipe(CoopCampaignMapMovementTargetKind.Settlement);
        catalog.EntityId = dynamicState.EntityId = recipe.EntityId;
        catalog.SettlementKind = CoopCampaignMapPrototypeSettlementKind.Town;
        Check(!CoopCampaignMapMovementTargetCodec.IsAdmitted(recipe, catalog, dynamicState), "fort shape must agree with catalog");
        recipe.Fortification = true;
        Check(CoopCampaignMapMovementTargetCodec.IsAdmitted(recipe, catalog, dynamicState), "fort admitted");
        Check(!CoopCampaignMapMovementTargetCodec.IsAdmitted(recipe, null, dynamicState), "missing catalog");
        Check(CoopCampaignMapMovementTargetCodec.IsAdmitted(Recipe(CoopCampaignMapMovementTargetKind.Point), null, null), "point needs no entity");
    }

    private static void Freshness()
    {
        foreach (double age in new[] { 0d, .2d, 1.999d })
            Check(CoopCampaignMapMovementTargetCodec.IsFreshAndReady(age, 3, 4, 3, 4), "fresh coherent revisions");
        foreach (double age in new[] { -1d, 2d, 3d, double.NaN, double.PositiveInfinity })
            Check(!CoopCampaignMapMovementTargetCodec.IsFreshAndReady(age, 3, 4, 3, 4), "stale or invalid age");
        Check(!CoopCampaignMapMovementTargetCodec.IsFreshAndReady(.2, 3, 4, 2, 4), "wait for catalog before showing target");
        Check(!CoopCampaignMapMovementTargetCodec.IsFreshAndReady(.2, 3, 4, 3, 3), "wait for dynamic before showing target");
        Check(!CoopCampaignMapMovementTargetCodec.IsFreshAndReady(.2, -1, 4, 3, 4), "invalid required revision");
        Check(!CoopCampaignMapMovementTargetCodec.IsFreshAndReady(.2, 3, 4, -1, -1), "session reset revokes readiness");
        Check(CoopCampaignMapMovementTargetCodec.IsFreshAndReady(.2, 3, 4, 4, 5), "newer disclosure permitted but admission still required");
    }
    private static void Check(bool condition, string label)
    { checks++; if (!condition) throw new InvalidOperationException("Movement target transport: " + label); }
}
