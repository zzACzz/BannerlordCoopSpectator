using System;
using CoopSpectator.Infrastructure;

internal static class TooltipPeerGateTests
{
    private static int _count;
    internal static void Run()
    {
        for (int cycle = 0; cycle < 100; cycle++)
        {
            object peer = new object(), reusedIndexPeer = new object();
            var session = Guid.NewGuid().ToString("N");
            var server = new CoopCampaignMapTooltipPeerGate(peer, session);
            var client = new CoopCampaignMapTooltipInterest();
            Check(client.TryGrant(session, server.LeaseId, 0), "grant");
            client.TryRequest("party:host", false, true, 0, out var first);
            Check(!server.TryAdmit(reusedIndexPeer, first, true, 0, out _), "peer index reuse denied");
            Check(server.TryAdmit(peer, first, true, 0, out var ticket), "admission");
            Check(!server.TryAdmit(peer, first, true, 0, out _), "replay cannot consume");
            Check(!server.TryComplete(reusedIndexPeer, ticket, Payload(ticket), true, 0, out _), "other peer cannot complete");
            Check(server.TryComplete(peer, ticket, Payload(ticket), true, 0, out var bytes), "completion");
            Check(CoopCampaignMapTooltipPayloadCodec.TryDeserialize(bytes, out var payload) && client.TryAccept(server.LeaseId, payload, true, 0), "end to end pure path");
            Check(!server.TryComplete(peer, ticket, Payload(ticket), true, 0, out _), "duplicate completion");
            var second = Request(server, 2);
            Check(!server.TryAdmit(peer, second, true, 1, out _), "server rate limit independent of client");
            Check(!server.TryAdmit(peer, second, true, 250, out _), "throttled generation cannot replay later");
            Check(!server.TryAdmit(peer, Request(server, 3), false, 250, out _), "hidden target denied");
            Check(server.TryAdmit(peer, Request(server, 4), true, 250, out var old), "new intent admitted");
            Check(!server.TryAdmit(peer, Request(server, 5), true, 251, out _), "rapid newer intent rejected");
            Check(!server.TryComplete(peer, old, Payload(old), true, 251, out _), "newer throttled intent cancels old result");
            Check(server.TryAdmit(peer, Request(server, 6), true, 500, out var hidden), "capture before visibility loss");
            Check(!server.TryComplete(peer, hidden, Payload(hidden), false, 500, out _), "visibility rechecked at completion");
            Check(!server.TryComplete(peer, hidden, Payload(hidden), true, 500, out _), "visibility reveal cannot revive result");
            Check(server.TryAdmit(peer, Request(server, 7), true, 750, out var late), "capture before expiry");
            Check(!server.TryComplete(peer, late, Payload(late), true, 2750, out _), "expiry boundary");
            Check(server.TryAdmit(peer, Request(server, 8), true, 2750, out var malformed), "capture before invalid result");
            var bad = Payload(malformed); bad.RequestGeneration++;
            Check(!server.TryComplete(peer, malformed, bad, true, 2750, out _), "mismatched host result denied");
            Check(!server.TryComplete(peer, malformed, Payload(malformed), true, 2750, out _), "invalid completion terminal");
            Check(server.TryAdmit(peer, Request(server, 9), true, 3000, out var beforeRevoke), "before revoke");
            server.Revoke();
            Check(!server.TryComplete(peer, beforeRevoke, Payload(beforeRevoke), true, 3000, out _) &&
                !server.TryAdmit(peer, Request(server, 10), true, 3000, out _), "revoke permanent");
            var reconnect = new CoopCampaignMapTooltipPeerGate(reusedIndexPeer, session);
            Check(reconnect.LeaseId != server.LeaseId && !reconnect.TryAdmit(reusedIndexPeer, first, true, 3000, out _), "same session reconnect fresh lease");
        }
        var reference = new object();
        var gated = new CoopCampaignMapTooltipPeerGate(reference, Guid.NewGuid().ToString("N"));
        Check(gated.TryAdmit(reference, Request(gated, 1), true, 100, out _), "clock start");
        Check(!gated.TryAdmit(reference, Request(gated, 2), true, 99, out _) &&
            !gated.TryAdmit(reference, Request(gated, 3), true, 1000, out _), "clock reversal permanently revokes");
        foreach (object invalid in new object[] { null, "peer-index", 1 })
        {
            bool rejected = false;
            try { new CoopCampaignMapTooltipPeerGate(invalid, Guid.NewGuid().ToString("N")); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "identity cannot be null/name/value index");
        }
        Console.WriteLine("Tooltip peer-gate assertions passed: " + _count);
    }

    private static CoopCampaignMapTooltipRequest Request(CoopCampaignMapTooltipPeerGate gate, long generation)
    { return new CoopCampaignMapTooltipRequest(gate.SessionId, gate.LeaseId, "party:host", generation, false); }

    private static CoopCampaignMapTooltipPayload Payload(CoopCampaignMapTooltipCaptureTicket ticket)
    {
        return new CoopCampaignMapTooltipPayload {
            SessionId = ticket.Request.SessionId, EntityId = ticket.Request.EntityId,
            RequestGeneration = ticket.Request.Generation, Sequence = ticket.Sequence,
            IsExtended = ticket.Request.IsExtended, Kind = CoopCampaignMapTooltipKind.MobileParty,
            Rows = new System.Collections.Generic.List<CoopCampaignMapTooltipRow> { new CoopCampaignMapTooltipRow { Value = "30" } }
        };
    }
    private static void Check(bool value, string name)
    { _count++; if (!value) throw new InvalidOperationException("Tooltip peer gate: " + name); }
}
