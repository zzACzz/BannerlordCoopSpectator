using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Infrastructure;

internal static class TooltipWireTests
{
    private static int _count;
    internal static void Run()
    {
        var peer = new object();
        var session = Guid.NewGuid().ToString("N");
        var gate = new CoopCampaignMapTooltipPeerGate(peer, session);
        var handshake = new CoopCampaignMapTooltipHandshake();
        var interest = new CoopCampaignMapTooltipInterest();
        var hello = RoundTrip(new CoopCampaignMapTooltipWireMessage { Kind = CoopCampaignMapTooltipMessageKind.Hello, Challenge = handshake.Challenge });
        var grant = RoundTrip(new CoopCampaignMapTooltipWireMessage { Kind = CoopCampaignMapTooltipMessageKind.Grant,
            Challenge = hello.Challenge, Lease = gate.LeaseId, Session = session });
        Check(handshake.TryAcceptGrant(grant) && interest.TryGrant(grant.Session, grant.Lease, 0), "matching grant admitted");
        Check(handshake.TryAcceptGrant(grant), "duplicate grant idempotent");
        var wrongGrant = new CoopCampaignMapTooltipWireMessage { Kind = grant.Kind, Challenge = grant.Challenge, Lease = Guid.NewGuid(), Session = session };
        Check(!handshake.TryAcceptGrant(wrongGrant), "cannot silently switch granted lease");
        wrongGrant.Lease = gate.LeaseId; wrongGrant.Session = Guid.NewGuid().ToString("N");
        Check(!handshake.TryAcceptGrant(wrongGrant), "cannot silently switch granted session");
        Check(interest.TryRequest("party:host", true, true, 0, out var request), "request issued");
        var decodedRequest = RoundTrip(new CoopCampaignMapTooltipWireMessage { Kind = CoopCampaignMapTooltipMessageKind.Request, Request = request });
        Check(gate.TryAdmit(peer, decodedRequest.Request, true, 0, out var ticket), "decoded request admitted");
        var payload = new CoopCampaignMapTooltipPayload { SessionId = session, EntityId = request.EntityId,
            RequestGeneration = request.Generation, IsExtended = true, Sequence = ticket.Sequence, Kind = CoopCampaignMapTooltipKind.MobileParty,
            Rows = new List<CoopCampaignMapTooltipRow> { new CoopCampaignMapTooltipRow { Definition = "Загін", Value = "30 👋" } } };
        Check(gate.TryComplete(peer, ticket, payload, true, 0, out _), "host completion");
        var rows = RoundTrip(new CoopCampaignMapTooltipWireMessage { Kind = CoopCampaignMapTooltipMessageKind.Rows, Lease = gate.LeaseId, Payload = payload });
        Check(interest.TryAccept(rows.Lease, rows.Payload, true, 0), "wire response accepted");
        Check(interest.HasCurrentDisplay(true, 1999) && !interest.HasCurrentDisplay(true, 2000), "cheap display expiry");
        Check(interest.TryRequest("party:host", false, true, 2000, out var deniedRequest), "denial request");
        var denial = RoundTrip(new CoopCampaignMapTooltipWireMessage { Kind = CoopCampaignMapTooltipMessageKind.Denied, Request = deniedRequest });
        Check(interest.TryDeny(denial.Request, true, 2000), "wire denial accepted");

        for (int reset = 0; reset < 100; reset++)
        {
            Guid previous = handshake.Challenge;
            handshake.Reset(); interest.Reset();
            Check(handshake.Challenge != previous && !handshake.TryAcceptGrant(grant), "old grant cannot cross reset");
            grant = new CoopCampaignMapTooltipWireMessage { Kind = CoopCampaignMapTooltipMessageKind.Grant,
                Challenge = handshake.Challenge, Lease = Guid.NewGuid(), Session = session };
            Check(handshake.TryAcceptGrant(RoundTrip(grant)), "fresh challenge admitted");
        }

        Check(CoopCampaignMapTooltipWireCodec.IsClientMessage(hello.Kind) &&
            CoopCampaignMapTooltipWireCodec.IsClientMessage(decodedRequest.Kind), "client direction");
        Check(CoopCampaignMapTooltipWireCodec.IsServerMessage(grant.Kind) &&
            CoopCampaignMapTooltipWireCodec.IsServerMessage(rows.Kind) && CoopCampaignMapTooltipWireCodec.IsServerMessage(denial.Kind), "server direction");
        Check(!CoopCampaignMapTooltipWireCodec.IsClientMessage(rows.Kind) && !CoopCampaignMapTooltipWireCodec.IsServerMessage(hello.Kind) &&
            !CoopCampaignMapTooltipWireCodec.IsClientMessage((CoopCampaignMapTooltipMessageKind)0) &&
            !CoopCampaignMapTooltipWireCodec.IsServerMessage((CoopCampaignMapTooltipMessageKind)99), "unknown and wrong direction");
        foreach (var message in new[] { hello, grant, decodedRequest, rows, denial })
        {
            Check(CoopCampaignMapTooltipWireCodec.TryEncode(message, out var bytes), "encode for negative checks");
            for (int length = 0; length < bytes.Length; length++)
                Check(!CoopCampaignMapTooltipWireCodec.TryDecode(bytes.Take(length).ToArray(), out _), "every truncated prefix rejected");
            Check(!CoopCampaignMapTooltipWireCodec.TryDecode(bytes.Concat(new byte[] { 0 }).ToArray(), out _), "trailing byte rejected");
            var corrupt = (byte[])bytes.Clone(); corrupt[0] ^= 0xff;
            Check(!CoopCampaignMapTooltipWireCodec.TryDecode(corrupt, out _), "wrong magic");
            corrupt = (byte[])bytes.Clone(); corrupt[4] = 2;
            Check(!CoopCampaignMapTooltipWireCodec.TryDecode(corrupt, out _), "future version");
            corrupt = (byte[])bytes.Clone(); corrupt[5] = 0xff;
            Check(!CoopCampaignMapTooltipWireCodec.TryDecode(corrupt, out _), "unknown kind");
        }
        CoopCampaignMapTooltipWireCodec.TryEncode(decodedRequest, out var requestBytes);
        requestBytes[requestBytes.Length - 1] = 2;
        Check(!CoopCampaignMapTooltipWireCodec.TryDecode(requestBytes, out _), "noncanonical boolean");
        CoopCampaignMapTooltipWireCodec.TryEncode(rows, out var rowsBytes);
        for (int i = 22; i < 26; i++) rowsBytes[i] = 0xff;
        Check(!CoopCampaignMapTooltipWireCodec.TryDecode(rowsBytes, out _), "negative row length");
        CoopCampaignMapTooltipWireCodec.TryEncode(decodedRequest, out requestBytes);
        requestBytes[22] = 0xff; requestBytes[23] = 0xff;
        Check(!CoopCampaignMapTooltipWireCodec.TryDecode(requestBytes, out _), "hostile text prefix");
        Check(!CoopCampaignMapTooltipWireCodec.TryDecode(new byte[CoopCampaignMapTooltipWireCodec.MaxEnvelopeBytes + 1], out _), "envelope bound");
        foreach (var invalid in new[] {
            new CoopCampaignMapTooltipWireMessage { Kind = CoopCampaignMapTooltipMessageKind.Hello },
            new CoopCampaignMapTooltipWireMessage { Kind = CoopCampaignMapTooltipMessageKind.Hello, Challenge = Guid.NewGuid(), Payload = payload },
            new CoopCampaignMapTooltipWireMessage { Kind = CoopCampaignMapTooltipMessageKind.Grant, Challenge = Guid.NewGuid(), Lease = Guid.NewGuid(), Session = "invalid" },
            new CoopCampaignMapTooltipWireMessage { Kind = CoopCampaignMapTooltipMessageKind.Request },
            new CoopCampaignMapTooltipWireMessage { Kind = CoopCampaignMapTooltipMessageKind.Denied, Request = new CoopCampaignMapTooltipRequest(session, Guid.Empty, "x", 1, false) },
            new CoopCampaignMapTooltipWireMessage { Kind = CoopCampaignMapTooltipMessageKind.Rows, Lease = Guid.NewGuid() }
        }) Check(!CoopCampaignMapTooltipWireCodec.TryEncode(invalid, out _), "invalid union shape");

        var random = new Random(527);
        for (int i = 0; i < 1000; i++)
        {
            byte[] garbage = new byte[random.Next(0, 600)]; random.NextBytes(garbage);
            Check(!CoopCampaignMapTooltipWireCodec.TryDecode(garbage, out _), "bounded malformed input");
        }
        Console.WriteLine("Tooltip wire/handshake assertions passed: " + _count);
    }

    private static CoopCampaignMapTooltipWireMessage RoundTrip(CoopCampaignMapTooltipWireMessage value)
    {
        Check(CoopCampaignMapTooltipWireCodec.TryEncode(value, out var bytes), "encode");
        Check(CoopCampaignMapTooltipWireCodec.TryDecode(bytes, out var decoded), "decode");
        Check(decoded.Kind == value.Kind && CoopCampaignMapTooltipWireCodec.TryEncode(decoded, out var repeated) && bytes.SequenceEqual(repeated), "canonical re-encode");
        return decoded;
    }
    private static void Check(bool value, string name)
    { _count++; if (!value) throw new InvalidOperationException("Tooltip wire: " + name); }
}
