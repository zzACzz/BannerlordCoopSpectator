using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoopSpectator.Infrastructure;

internal static class TooltipChunkTests
{
    private static int _count;
    internal static void Run()
    {
        var interest = new CoopCampaignMapTooltipInterest();
        var lease = Guid.NewGuid(); var session = Guid.NewGuid().ToString("N");
        interest.TryGrant(session, lease, 0);
        interest.TryRequest("party:host", false, true, 0, out var request);
        var payload = new CoopCampaignMapTooltipPayload { SessionId = session, EntityId = request.EntityId,
            RequestGeneration = request.Generation, Sequence = 1, Kind = CoopCampaignMapTooltipKind.MobileParty };
        var random = new Random(341);
        for (int i = 0; i < 20; i++)
        {
            var letters = new char[200]; for (int j = 0; j < letters.Length; j++) letters[j] = (char)random.Next(33, 120);
            payload.Rows.Add(new CoopCampaignMapTooltipRow { Definition = "Troops", Value = new string(letters) });
        }
        CoopCampaignMapTooltipWireCodec.TryEncode(new CoopCampaignMapTooltipWireMessage { Kind = CoopCampaignMapTooltipMessageKind.Rows,
            Lease = lease, Payload = payload }, out var bytes);
        Check(CoopCampaignMapCatalogChunkCodec.TryEncode(bytes, out var transfer, out _) && transfer.ChunkCount > 1, "chunk preparation");
        var assembler = new CoopCampaignMapTooltipAssembler();
        var last = CoopCampaignMapTooltipChunks.Create(lease, request.Generation, 1, transfer, transfer.ChunkCount - 1);
        Check(!assembler.TryAccept(last, interest, true, 10, out _), "out of order start");
        Check(!assembler.TryAccept(last, interest, true, 10, out _), "identical duplicate");
        var foreign = CoopCampaignMapTooltipChunks.Create(Guid.NewGuid(), request.Generation, 1, transfer, 0);
        Check(!assembler.TryAccept(foreign, interest, true, 10, out _), "foreign lease ignored without clearing");
        CoopCampaignMapTooltipPayload restored = null;
        for (int index = 0; index < transfer.ChunkCount - 1; index++)
        {
            var chunk = CoopCampaignMapTooltipChunks.Create(lease, request.Generation, 1, transfer, index);
            Check(CoopCampaignMapTooltipChunks.TryEncode(chunk, out var encoded) && encoded.Length <= 512, "bounded native packet");
            Check(CoopCampaignMapTooltipChunks.TryDecode(encoded, out var decoded), "packet decode");
            bool completed = assembler.TryAccept(decoded, interest, true, 20, out restored);
            Check(completed == (index == transfer.ChunkCount - 2), "only complete current transfer publishes");
        }
        Check(restored != null && restored.Rows.Count == 20 && restored.Rows[0].Value == payload.Rows[0].Value, "exact rows");
        Check(!assembler.TryAccept(last, interest, true, 20, out _), "completed request cannot replay");
        Check(interest.TryRequest("party:host", false, true, 270, out var second), "refresh after terminal cooldown");
        var expired = CoopCampaignMapTooltipChunks.Create(lease, second.Generation, 2, transfer, 0);
        Check(!assembler.TryAccept(expired, interest, true, 2270, out _), "request age expires before allocation");

        CoopCampaignMapTooltipChunks.TryEncode(last, out var sample);
        for (int length = 0; length < sample.Length; length++)
            Check(!CoopCampaignMapTooltipChunks.TryDecode(sample.Take(length).ToArray(), out _), "truncated packet");
        Check(!CoopCampaignMapTooltipChunks.TryDecode(sample.Concat(new byte[] { 0 }).ToArray(), out _), "trailing data");
        last.LogicalBytes = CoopCampaignMapTooltipChunks.MaxBytes + 1;
        Check(!CoopCampaignMapTooltipChunks.TryEncode(last, out _), "logical bound");
        last.LogicalBytes = transfer.LogicalByteCount; last.Bytes = new byte[257];
        Check(!CoopCampaignMapTooltipChunks.TryEncode(last, out _), "chunk size bound");
        var revoked = new CoopCampaignMapTooltipWireMessage { Kind = CoopCampaignMapTooltipMessageKind.Revoked, Lease = lease };
        Check(CoopCampaignMapTooltipWireCodec.TryEncode(revoked, out var revokeBytes) &&
            CoopCampaignMapTooltipWireCodec.TryDecode(revokeBytes, out var revoke) && revoke.Lease == lease &&
            CoopCampaignMapTooltipWireCodec.IsServerMessage(revoke.Kind) && !CoopCampaignMapTooltipWireCodec.IsClientMessage(revoke.Kind), "lease-bound revocation");

        // A compressed transfer may not inflate beyond its advertised length,
        // even when the shared catalog global ceiling would be larger.
        Check(CoopCampaignMapCatalogChunkAccumulator.TryCreate(1, 1, bytes.Length - 1, transfer.WireByteCount, transfer.ChunkCount,
            transfer.CompressionKind, transfer.PayloadHash, out var shortDeclared, out _), "bounded decompression setup");
        for (int i = 0; i < transfer.ChunkCount; i++) shortDeclared.TryAccept(i, transfer.ChunkCount, transfer.Chunks[i], out _);
        Check(!shortDeclared.TryComplete(out _, out _), "inflation beyond declaration denied");

        string root = Environment.GetEnvironmentVariable("COOPSPECTATOR_REPOSITORY_ROOT");
        string controller = File.ReadAllText(Path.Combine(root, "Mission", "CoopCampaignMapPrototypeNetworkController.Tooltips.cs"));
        string main = File.ReadAllText(Path.Combine(root, "Mission", "CoopCampaignMapPrototypeNetworkController.cs"));
        Check(main.Contains("RegisterBaseHandler<CoopCampaignMapTooltipRequestMessage>") &&
            main.Contains("RegisterBaseHandler<CoopCampaignMapTooltipControlMessage>") && main.Contains("RegisterBaseHandler<CoopCampaignMapTooltipChunkMessage>"), "native handlers wired");
        Check(controller.Contains("!ReferenceEquals(state.Peer, peer)) return") && controller.Contains("ReferenceEquals(FindNetworkPeer(peer.Index), peer)"), "exact peer including disconnect");
        Check(controller.Contains("TooltipInterest.PendingRequest == null") && controller.Contains("_tooltipClientWasAvailable"), "pending latency and unavailable grant reset");
        Check(controller.Contains("int budget = 16") && controller.Contains("perPeer < 2") && controller.Contains("_tooltipRoundRobin"), "bounded fair scheduling");
        Check(controller.Contains("state.Gate.TryComplete") && controller.Contains("CoopCampaignMapTooltipRelayClient.TryOpen()"), "relay and completion gate wired");
        Console.WriteLine("Tooltip chunk/network assertions passed: " + _count);
    }
    private static void Check(bool value, string name)
    { _count++; if (!value) throw new InvalidOperationException("Tooltip chunks: " + name); }
}
