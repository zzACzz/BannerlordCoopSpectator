using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoopSpectator.Infrastructure;

internal static class BattleMarkerTests
{
    public static void Run()
    {
        void Check(bool value, string name) { if (!value) throw new Exception("Battle marker: " + name); }
        string root = Environment.GetEnvironmentVariable("COOPSPECTATOR_REPOSITORY_ROOT");
        Check(!string.IsNullOrWhiteSpace(root), "repository root for integration checks");
        string view = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapPrototypeMissionView.cs"));
        int tickStart = view.IndexOf("public override void OnMissionScreenTick", StringComparison.Ordinal);
        int tickEnd = view.IndexOf("public override void OnMissionScreenFinalize", tickStart, StringComparison.Ordinal);
        Check(tickStart >= 0 && tickEnd > tickStart, "tick boundary");
        string tick = view.Substring(tickStart, tickEnd - tickStart);
        int cameraUpdate = tick.IndexOf("UpdateMarkerAndCamera();", StringComparison.Ordinal);
        int markerUpdate = tick.IndexOf("TickBattleMarkers();", StringComparison.Ordinal);
        int finalizer = tick.IndexOf("finally", StringComparison.Ordinal);
        Check(cameraUpdate >= 0 && finalizer > cameraUpdate && markerUpdate > finalizer,
            "marker projection follows final camera and runs on early return");
        Check(tick.LastIndexOf("TickBattleMarkers();", StringComparison.Ordinal) == markerUpdate,
            "one marker update per frame");
        string markerView = File.ReadAllText(Path.Combine(root, "UI", "CoopCampaignMapPrototypeMissionView.BattleMarkers.cs"));
        Check(markerView.Contains("IsClientBattleMarkersHostFresh") &&
            markerView.Contains("CurrentClientConversation?.IsActive != true") &&
            markerView.Contains("ScreenManager.SetSuspendLayer(_battleMarkerLayer, true)"),
            "freshness and dialogue visibility gates remain connected");
        Check(view.Contains("ReleaseBattleMarkers();"), "scene teardown releases marker view");
        for (int disclosed = 0; disclosed < 2; disclosed++)
            for (int settlement = 0; settlement < 2; settlement++)
                Check(CoopCampaignMapBattleMarkerPolicy.Admit(disclosed != 0, settlement != 0) ==
                    (disclosed == 1 && settlement == 0), "disclosure/settlement matrix");
        Check(CoopCampaignMapBattleMarkerPolicy.Show(true, 199.99f, 1), "near camera");
        Check(!CoopCampaignMapBattleMarkerPolicy.Show(true, 200, 1), "exact cutoff");
        Check(!CoopCampaignMapBattleMarkerPolicy.Show(false, 10, 1), "hidden event");
        foreach (float invalid in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            Check(!CoopCampaignMapBattleMarkerPolicy.Show(true, 10, invalid), "invalid projection");
        Check(!CoopCampaignMapBattleMarkerPolicy.Show(true, float.NaN, 1), "invalid camera");
        var first = new CoopCampaignMapBattleMarker(1, 2, 3, 4);
        var source = new List<CoopCampaignMapBattleMarker> { first };
        Check(CoopCampaignMapBattleMarkerPolicy.TrySnapshot(source, out var snapshot), "valid snapshot");
        source.Clear();
        Check(snapshot.Length == 1 && snapshot[0].X == 2, "detached list");
        Check(CoopCampaignMapBattleMarkerPolicy.TrySnapshot(source, out snapshot) && snapshot.Length == 0, "clear ended events");
        source.Add(first); source.Add(first);
        Check(!CoopCampaignMapBattleMarkerPolicy.TrySnapshot(source, out snapshot) && snapshot == null, "duplicate event rejected atomically");
        foreach (var invalid in new[] { null, new CoopCampaignMapBattleMarker(0, 0, 0, 0),
            new CoopCampaignMapBattleMarker(2, float.NaN, 0, 0), new CoopCampaignMapBattleMarker(2, 0, float.PositiveInfinity, 0),
            new CoopCampaignMapBattleMarker(2, 0, 0, float.NegativeInfinity) })
            Check(!CoopCampaignMapBattleMarkerPolicy.TrySnapshot(new[] { first, invalid }, out snapshot) && snapshot == null, "invalid entry atomic rejection");
        source.Clear();
        for (int i = 1; i <= CoopCampaignMapBattleMarkerPolicy.MaximumEvents; i++)
            source.Add(new CoopCampaignMapBattleMarker(i, 0, 0, 0));
        Check(CoopCampaignMapBattleMarkerPolicy.TrySnapshot(source, out snapshot), "capacity admitted");
        source.Add(new CoopCampaignMapBattleMarker(513, 0, 0, 0));
        Check(!CoopCampaignMapBattleMarkerPolicy.TrySnapshot(source, out snapshot), "capacity bounded");
        Check(CoopCampaignMapBattleMarkerCodec.TryEncode(7, new[] { first }, out var bytes), "encode");
        Check(CoopCampaignMapBattleMarkerCodec.TryDecode(bytes, 7, out snapshot) &&
            snapshot.Length == 1 && snapshot[0].EventId == 1 && snapshot[0].X == 2 &&
            snapshot[0].Y == 3 && snapshot[0].Z == 4, "exact roundtrip");
        Check(!CoopCampaignMapBattleMarkerCodec.TryDecode(bytes, 8, out snapshot), "revision binding");
        for (int length = 0; length < bytes.Length; length++)
        {
            var truncated = new byte[length]; Array.Copy(bytes, truncated, length);
            Check(!CoopCampaignMapBattleMarkerCodec.TryDecode(truncated, 7, out snapshot), "all truncations rejected");
        }
        var trailing = new byte[bytes.Length + 1]; Array.Copy(bytes, trailing, bytes.Length);
        Check(!CoopCampaignMapBattleMarkerCodec.TryDecode(trailing, 7, out snapshot), "trailing byte rejected");
        var corrupt = (byte[])bytes.Clone(); corrupt[0] ^= 1;
        Check(!CoopCampaignMapBattleMarkerCodec.TryDecode(corrupt, 7, out snapshot), "wrong format rejected");
        corrupt = (byte[])bytes.Clone(); Array.Copy(BitConverter.GetBytes(float.NaN), 0, corrupt, 16, 4);
        Check(!CoopCampaignMapBattleMarkerCodec.TryDecode(corrupt, 7, out snapshot), "nonfinite wire coordinate");
        corrupt = (byte[])bytes.Clone(); Array.Copy(BitConverter.GetBytes(int.MaxValue), 0, corrupt, 8, 4);
        Check(!CoopCampaignMapBattleMarkerCodec.TryDecode(corrupt, 7, out snapshot), "count overflow rejected before allocation");
        Check(CoopCampaignMapBattleMarkerCodec.TryEncode(8, Array.Empty<CoopCampaignMapBattleMarker>(), out bytes) &&
            CoopCampaignMapBattleMarkerCodec.TryDecode(bytes, 8, out snapshot) && snapshot.Length == 0, "explicit empty roundtrip");
        source.RemoveAt(source.Count - 1);
        Check(CoopCampaignMapBattleMarkerCodec.TryEncode(9, source, out bytes) &&
            bytes.Length == CoopCampaignMapBattleMarkerCodec.MaximumBytes &&
            CoopCampaignMapBattleMarkerCodec.TryDecode(bytes, 9, out snapshot) && snapshot.Length == 512, "maximum payload roundtrip");
        Console.WriteLine("Battle marker foundation checks passed.");
        var host = new CoopCampaignMapPrototypeHostSnapshot
        {
            SchemaVersion = CoopCampaignMapPrototypeContract.HostBridgeSchemaVersion,
            SessionId = "0123456789abcdef0123456789abcdef", Revision = 7,
            IsActive = true, UpdatedUtc = DateTime.UtcNow, BattleMarkers = new[] { first }
        };
        var lines = CoopCampaignMapPrototypeBridgeCodec.Serialize(host);
        Check(CoopCampaignMapPrototypeBridgeCodec.TryParse(lines, out var received, out _) &&
            received.BattleMarkers.Length == 1 && received.BattleMarkers[0].Z == 4, "host bridge roundtrip");
        Check(!CoopCampaignMapPrototypeBridgeCodec.TryParse(lines.Where(s => !s.StartsWith("BattleMarkers=")).ToArray(),
            out _, out _), "required bridge field");
        Check(!CoopCampaignMapPrototypeBridgeCodec.TryParse(lines.Select(s => s.StartsWith("BattleMarkers=") ?
            "BattleMarkers=invalid" : s).ToArray(), out _, out _), "corrupt bridge field");
        var clone = host.Clone(); host.BattleMarkers[0] = new CoopCampaignMapBattleMarker(2, 0, 0, 0);
        Check(clone.BattleMarkers[0].EventId == 1, "host clone isolates array");
        host.BattleMarkers = Array.Empty<CoopCampaignMapBattleMarker>();
        Check(CoopCampaignMapPrototypeBridgeCodec.TryParse(CoopCampaignMapPrototypeBridgeCodec.Serialize(host),
            out received, out _) && received.BattleMarkers.Length == 0, "bridge explicit clear");
        Check(CoopCampaignMapBattleMarkerCodec.TryEncode(10, source, out bytes), "chunk source");
        Check(CoopCampaignMapCatalogChunkCodec.TryEncode(bytes, out var payload, out _), "chunk encode");
        int maximumChunkBudget = (CoopCampaignMapBattleMarkerCodec.MaximumBytes +
            CoopCampaignMapCatalogChunkCodec.MaxChunkBytes - 1) / CoopCampaignMapCatalogChunkCodec.MaxChunkBytes;
        Check(maximumChunkBudget == 33 && payload.ChunkCount <= maximumChunkBudget,
            "maximum list fits a bounded single-tick send");
        string controller = File.ReadAllText(Path.Combine(root, "Mission", "CoopCampaignMapPrototypeNetworkController.BattleMarkers.cs"));
        Check(controller.Contains("budget < chunkBudget") &&
            controller.Contains("CoopCampaignMapBattleMarkerCodec.MaximumBytes +") &&
            !controller.Contains("budget < 2 &&"), "bounded complete-list scheduling is connected");
        var assembler = new CoopCampaignMapBattleMarkerAssembler();
        bool complete = false;
        for (int i = payload.ChunkCount - 1; i >= 0; i--)
        {
            Check(assembler.TryAccept(10, payload.LogicalByteCount, payload.WireByteCount, payload.ChunkCount,
                payload.CompressionKind, payload.PayloadHash, i, payload.Chunks[i], out complete, out snapshot), "reverse chunk order");
            Check(complete == (i == 0), "no partial publication");
        }
        Check(complete && snapshot.Length == 512, "assembled full list");
        Check(assembler.HasCompletedLatest, "complete latest can display");
        Check(!assembler.TryAccept(10, payload.LogicalByteCount, payload.WireByteCount, payload.ChunkCount,
            payload.CompressionKind, payload.PayloadHash, 0, payload.Chunks[0], out _, out _), "completed replay rejected");
        assembler.Reset();
        Check(!assembler.HasCompletedLatest, "reset hides old presentation");
        Check(payload.ChunkCount > 1, "supersession fixture has multiple chunks");
        Check(assembler.TryAccept(10, payload.LogicalByteCount, payload.WireByteCount, payload.ChunkCount,
            payload.CompressionKind, payload.PayloadHash, 0, payload.Chunks[0], out complete, out snapshot) &&
            !complete && snapshot == null, "old list is genuinely pending before clear");
        Check(!assembler.HasCompletedLatest, "partial cannot display");
        Check(CoopCampaignMapBattleMarkerCodec.TryEncode(11, Array.Empty<CoopCampaignMapBattleMarker>(), out var emptyBytes) &&
            CoopCampaignMapCatalogChunkCodec.TryEncode(emptyBytes, out var emptyPayload, out _), "empty payload ready");
        // Re-encode outside the assertion to satisfy definite assignment.
        CoopCampaignMapCatalogChunkCodec.TryEncode(emptyBytes, out var clearing, out _);
        Check(assembler.TryAccept(11, clearing.LogicalByteCount, clearing.WireByteCount, clearing.ChunkCount,
            clearing.CompressionKind, clearing.PayloadHash, 0, clearing.Chunks[0], out complete, out snapshot) &&
            complete && snapshot.Length == 0, "newer clear completes");
        Check(assembler.HasCompletedLatest, "verified empty is current");
        Check(!assembler.TryAccept(10, payload.LogicalByteCount, payload.WireByteCount, payload.ChunkCount,
            payload.CompressionKind, payload.PayloadHash, 0, payload.Chunks[0], out _, out _), "old data cannot resurrect clear");
        assembler.Reset();
        Check(assembler.TryAccept(11, clearing.LogicalByteCount, clearing.WireByteCount, clearing.ChunkCount,
            clearing.CompressionKind, clearing.PayloadHash, 0, clearing.Chunks[0], out _, out _), "previous list complete before corrupt newer");
        CoopCampaignMapBattleMarkerCodec.TryEncode(12, Array.Empty<CoopCampaignMapBattleMarker>(), out var newerBytes);
        CoopCampaignMapCatalogChunkCodec.TryEncode(newerBytes, out clearing, out _);
        var broken = (byte[])clearing.Chunks[0].Clone(); broken[broken.Length - 1] ^= 1;
        Check(!assembler.TryAccept(12, clearing.LogicalByteCount, clearing.WireByteCount, clearing.ChunkCount,
            clearing.CompressionKind, clearing.PayloadHash, 0, broken, out complete, out snapshot) &&
            !complete && snapshot == null, "hash mismatch cannot publish");
        Check(!assembler.HasCompletedLatest, "corrupt newer list revokes previously complete display");
        Check(assembler.TryAccept(12, clearing.LogicalByteCount, clearing.WireByteCount, clearing.ChunkCount,
            clearing.CompressionKind, clearing.PayloadHash, 0, clearing.Chunks[0], out complete, out snapshot) &&
            complete && snapshot.Length == 0, "valid retry after corrupt completed assembly");
        Check(assembler.HasCompletedLatest, "valid newest retry restores display");
        // A previously visible list must be revoked on the first newer chunk,
        // not only after a complete empty replacement or a corrupt payload.
        assembler.Reset();
        CoopCampaignMapBattleMarkerCodec.TryEncode(12, new[] { first }, out var visibleBytes);
        CoopCampaignMapCatalogChunkCodec.TryEncode(visibleBytes, out var visiblePayload, out _);
        for (int i = 0; i < visiblePayload.ChunkCount; i++)
            Check(assembler.TryAccept(12, visiblePayload.LogicalByteCount, visiblePayload.WireByteCount,
                visiblePayload.ChunkCount, visiblePayload.CompressionKind, visiblePayload.PayloadHash,
                i, visiblePayload.Chunks[i], out complete, out snapshot), "visible baseline accepted");
        Check(assembler.HasCompletedLatest && complete && snapshot.Length == 1,
            "nonempty baseline can display before disclosure update");
        CoopCampaignMapBattleMarkerCodec.TryEncode(13, source, out var replacementBytes);
        CoopCampaignMapCatalogChunkCodec.TryEncode(replacementBytes, out var replacement, out _);
        Check(replacement.ChunkCount > 1, "new disclosure requires multiple chunks");
        Check(assembler.TryAccept(13, replacement.LogicalByteCount, replacement.WireByteCount,
            replacement.ChunkCount, replacement.CompressionKind, replacement.PayloadHash,
            0, replacement.Chunks[0], out complete, out snapshot) && !complete && snapshot == null,
            "first newer chunk cannot publish a partial list");
        Check(!assembler.HasCompletedLatest, "first newer chunk hides previously visible events");
        Check(!assembler.TryAccept(12, visiblePayload.LogicalByteCount, visiblePayload.WireByteCount,
            visiblePayload.ChunkCount, visiblePayload.CompressionKind, visiblePayload.PayloadHash,
            0, visiblePayload.Chunks[0], out _, out _) && !assembler.HasCompletedLatest,
            "late old list cannot restore revoked visibility");
        for (int i = 1; i < replacement.ChunkCount; i++)
        {
            Check(assembler.TryAccept(13, replacement.LogicalByteCount, replacement.WireByteCount,
                replacement.ChunkCount, replacement.CompressionKind, replacement.PayloadHash,
                i, replacement.Chunks[i], out complete, out snapshot), "replacement chunk accepted");
            Check(assembler.HasCompletedLatest == (i == replacement.ChunkCount - 1),
                "only full replacement restores visibility");
        }
        Check(complete && snapshot.Length == source.Count, "complete disclosure replaces the whole list");
        assembler.Reset();
        Check(assembler.TryAccept(10, payload.LogicalByteCount, payload.WireByteCount, payload.ChunkCount,
            payload.CompressionKind, payload.PayloadHash, 0, payload.Chunks[0], out _, out _), "session reset permits new sequence");
        Console.WriteLine("Battle marker bridge and chunk checks passed.");
    }
}
