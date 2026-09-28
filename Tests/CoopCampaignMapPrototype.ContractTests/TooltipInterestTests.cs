using System;
using CoopSpectator.Infrastructure;

internal static class TooltipInterestTests
{
    private static int _count;
    private static readonly string Session = Guid.NewGuid().ToString("N");

    internal static void Run()
    {
        for (int cycle = 0; cycle < 100; cycle++)
        {
            var interest = new CoopCampaignMapTooltipInterest();
            var lease = Guid.NewGuid();
            Check(!interest.TryRequest("party:host", false, true, 0, out _), "no grant");
            Check(!interest.TryGrant("invalid", lease, 0) && !interest.TryGrant(Session, Guid.Empty, 0), "invalid grant");
            Check(interest.TryGrant(Session, lease, 0), "grant");
            Check(interest.TryRequest("party:host", false, true, 0, out var first), "first request");
            var firstPayload = Payload(first, 1);
            Check(!interest.TryAccept(Guid.NewGuid(), firstPayload, true, 0), "wrong lease");
            Check(interest.TryAccept(lease, firstPayload, true, 0), "first accepted");
            firstPayload.Rows[0].Value = "mutated outside";
            Check(interest.TryGetDisplay(true, 0, out var display) && display.Rows[0].Value == "30", "detached input");
            display.Rows[0].Value = "mutated output";
            Check(interest.TryGetDisplay(true, 0, out display) && display.Rows[0].Value == "30", "detached output");
            Check(!interest.TryAccept(lease, Payload(first, 2), true, 1), "one terminal response");
            Check(!interest.TryRequest("settlement:town", false, true, 1, out _), "changed hover throttled");
            Check(!interest.TryGetDisplay(true, 1, out _), "changed hover hides even when throttled");
            Check(!interest.TryAccept(lease, Payload(first, 2), true, 1), "old hover rejected");
            Check(interest.TryRequest("settlement:town", false, true, 250, out var second) && second.Generation > first.Generation, "next hover");
            var wrong = Payload(second, 2); wrong.IsExtended = true;
            Check(!interest.TryAccept(lease, wrong, true, 250), "extension mismatch");
            wrong = Payload(second, 2); wrong.EntityId = "party:host";
            Check(!interest.TryAccept(lease, wrong, true, 250), "entity mismatch");
            wrong = Payload(second, 2); wrong.SessionId = Guid.NewGuid().ToString("N");
            Check(!interest.TryAccept(lease, wrong, true, 250), "session mismatch");
            Check(!interest.TryAccept(lease, Payload(second, 1), true, 250), "sequence replay");
            Check(interest.TryAccept(lease, Payload(second, 2), true, 250), "second accepted");
            Check(interest.TryRequest("settlement:town", false, true, 500, out var refresh), "refresh generation");
            Check(interest.TryGetDisplay(true, 500, out _), "same-hover refresh retains bounded card");
            Check(!interest.TryDeny(second, true, 500), "old denial cannot clear new interest");
            Check(interest.TryGetDisplay(true, 500, out _), "old denial preserves card");
            Check(!interest.TryAccept(lease, Payload(second, 3), true, 500), "old refresh response");
            Check(interest.TryDeny(refresh, true, 500), "current denial accepted");
            Check(!interest.TryGetDisplay(true, 500, out _), "current denial hides");
            Check(interest.TryRequest("settlement:town", true, true, 750, out var extended), "extended request");
            Check(interest.TryAccept(lease, Payload(extended, 3), true, 750), "extended response");
            Check(!interest.TryGetDisplay(false, 750, out _), "disclosure revoked");
            Check(!interest.TryAccept(lease, Payload(extended, 4), true, 750), "reveal cannot revive old response");
            Check(interest.TryRequest("party:host", false, true, 1000, out var beforeClose), "before close");
            interest.Clear();
            Check(interest.TryGrant(Session, lease, 1000), "duplicate grant idempotent");
            Check(!interest.TryAccept(lease, Payload(beforeClose, 4), true, 1000), "duplicate grant does not reopen");
            Check(interest.TryRequest("party:host", false, true, 1250, out var beforeReset), "before mission reset");
            interest.Reset();
            Check(!interest.TryAccept(lease, Payload(beforeReset, 4), true, 1250), "reset revokes");
            var newLease = Guid.NewGuid();
            Check(interest.TryGrant(Session, newLease, 1250) && interest.TryRequest("party:host", false, true, 1250, out _), "same campaign new map lease");
            Check(!interest.TryAccept(lease, Payload(first, 5), true, 1250), "generation reset cannot admit old lease");
        }

        var timed = new CoopCampaignMapTooltipInterest();
        var timedLease = Guid.NewGuid();
        timed.TryGrant(Session, timedLease, 0);
        timed.TryRequest("party:host", false, true, 0, out var slow);
        Check(timed.TryAccept(timedLease, Payload(slow, 1), true, 1999), "last fresh millisecond accepted");
        Check(!timed.TryGetDisplay(true, 2000, out _), "age anchored to request, not receipt");
        Check(!timed.TryRequest("party:host", false, true, 2000, out _), "slow terminal starts cooldown without extending display age");
        timed.TryRequest("party:host", false, true, 2249, out var late);
        Check(!timed.TryAccept(timedLease, Payload(late, 2), true, 4249), "late response rejected");
        Check(!timed.TryDeny(late, true, 4249), "late denial rejected");
        Check(timed.TryRequest("party:host", false, true, 4249, out var current), "new timely request");
        Check(timed.TryAccept(timedLease, Payload(current, 2), true, 4249), "new timely response");
        Check(!timed.TryGetDisplay(true, 4248, out _) && !timed.TryRequest("party:host", false, true, 4249, out _), "clock reversal revokes lease");
        Check(!timed.TryGrant(Session, timedLease, -1), "negative clock");
        Check(timed.TryGrant(Session, Guid.NewGuid(), long.MaxValue - 2000), "high clock grant");
        Check(timed.TryRequest("party:host", false, true, long.MaxValue - 2000, out var high), "high clock request");
        Check(timed.TryAccept(high.LeaseId, Payload(high, 1), true, long.MaxValue - 1), "high clock no addition overflow");
        Check(!timed.TryGetDisplay(true, long.MaxValue, out _), "high clock expiry");
        VerifyVariableTransitCadence();
        Console.WriteLine("Tooltip interest assertions passed: " + _count);
    }

    private static void VerifyVariableTransitCadence()
    {
        // The real peer gate runs on its own clock. A first slow uplink followed
        // by a fast one compresses server arrivals if only send times are paced.
        for (int delay = 1; delay <= 240; delay++)
        {
            var peer = new object();
            var server = new CoopCampaignMapTooltipPeerGate(peer, Session);
            var client = new CoopCampaignMapTooltipInterest();
            client.TryGrant(Session, server.LeaseId, 0);
            Check(client.TryRequest("party:host", false, true, 0, out var first), "transit first send");
            Check(server.TryAdmit(peer, first, true, 10000 + delay, out var ticket), "transit first admission");
            var payload = Payload(first, ticket.Sequence);
            Check(server.TryComplete(peer, ticket, payload, true, 10000 + delay, out _), "transit first completion");
            Check(client.TryAccept(server.LeaseId, payload, true, delay + 20), "transit first receipt");
            Check(!client.TryRequest("party:host", false, true, delay + 269, out _), "receipt cooldown boundary");
            Check(client.TryGetDisplay(true, delay + 269, out _), "cooldown retains fresh same-hover card");
            Check(client.TryRequest("party:host", false, true, delay + 270, out var next), "receipt cooldown elapsed");
            Check(server.TryAdmit(peer, next, true, 10000 + delay + 271, out _), "fast next uplink cannot hit server rate gate");
            Check(client.TryDeny(next, true, delay + 280), "terminal denial");
            client.Clear();
            Check(!client.TryRequest("settlement:town", true, true, delay + 529, out _), "clear and changed hover cannot bypass terminal cooldown");
            Check(client.TryRequest("settlement:town", true, true, delay + 530, out _), "changed hover resumes after cooldown");
        }
    }

    private static CoopCampaignMapTooltipPayload Payload(CoopCampaignMapTooltipRequest request, long sequence)
    {
        return new CoopCampaignMapTooltipPayload {
            SessionId = request.SessionId, EntityId = request.EntityId, RequestGeneration = request.Generation,
            Sequence = sequence, IsExtended = request.IsExtended, Kind = CoopCampaignMapTooltipKind.MobileParty,
            Rows = new System.Collections.Generic.List<CoopCampaignMapTooltipRow> { new CoopCampaignMapTooltipRow { Definition = "Troops", Value = "30" } }
        };
    }

    private static void Check(bool value, string message)
    { _count++; if (!value) throw new InvalidOperationException("Tooltip interest: " + message); }
}
