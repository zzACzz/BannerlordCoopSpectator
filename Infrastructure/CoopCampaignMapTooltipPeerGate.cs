using System;

namespace CoopSpectator.Infrastructure
{
    public sealed class CoopCampaignMapTooltipCaptureTicket
    {
        public CoopCampaignMapTooltipRequest Request { get; }
        public long Sequence { get; }
        internal CoopCampaignMapTooltipCaptureTicket(CoopCampaignMapTooltipRequest request, long sequence)
        { Request = request; Sequence = sequence; }
    }

    // Unwired per-peer SERVER gate. ExactPeer must be the current native peer
    // object, never a boxed index/name. Only a trusted server adapter creates it.
    // One instance = one peer connection + one map + one host campaign session.
    // Adapter must revoke on any loss and independently authorize host disclosure.
    public sealed class CoopCampaignMapTooltipPeerGate
    {
        public string SessionId { get; }
        public Guid LeaseId { get; } = Guid.NewGuid();
        private readonly object _peer;
        private bool _revoked;
        private long _lastClock = -1;
        private long _lastCaptureAt = -1;
        private long _lastGeneration;
        private long _sequence;
        private long _pendingAt;
        private CoopCampaignMapTooltipCaptureTicket _pending;
        public CoopCampaignMapTooltipCaptureTicket PendingTicket => _pending;
        public long LatestGeneration => _lastGeneration;

        public CoopCampaignMapTooltipPeerGate(object exactPeer, string session)
        {
            if (exactPeer == null || exactPeer.GetType().IsValueType || exactPeer is string ||
                !CoopCampaignMapTooltipPayloadCodec.IsValidCorrelation(session, "grant", 1, 1))
                throw new ArgumentException("An exact peer reference and valid campaign session are required.");
            _peer = exactPeer;
            SessionId = session;
        }

        // hostAdmitted means CURRENT map/no-dialogue, fresh host state, current
        // admitted catalog target AND host visibility; it is not client supplied.
        // Newer intent cancels old capture even if denied/rate limited. Invalid or
        // replayed envelopes cannot cancel another admitted request.
        public bool TryAdmit(object exactPeer, CoopCampaignMapTooltipRequest request,
            bool hostAdmitted, long now, out CoopCampaignMapTooltipCaptureTicket ticket)
        {
            ticket = null;
            if (!Observe(exactPeer, now) || !ValidRequest(request) || request.Generation <= _lastGeneration) return false;
            _lastGeneration = request.Generation;
            _pending = null;
            if (!hostAdmitted || (_lastCaptureAt >= 0 &&
                now - _lastCaptureAt < CoopCampaignMapTooltipInterest.MinimumRequestIntervalMilliseconds)) return false;
            if (_sequence == long.MaxValue) { Revoke(); return false; }
            _pendingAt = _lastCaptureAt = now;
            ticket = _pending = new CoopCampaignMapTooltipCaptureTicket(request, ++_sequence);
            return true;
        }

        // The host result must echo the entire ticket; neither another peer nor
        // a late file response can publish into a replaced pending capture.
        // Recheck host admission immediately before sending to this exact peer.
        public bool TryComplete(object exactPeer, CoopCampaignMapTooltipCaptureTicket ticket,
            CoopCampaignMapTooltipPayload payload, bool hostAdmitted, long now, out byte[] bytes)
        {
            bytes = null;
            if (!Observe(exactPeer, now)) return false;
            if (!hostAdmitted) { _pending = null; return false; }
            if (_pending == null || !ReferenceEquals(ticket, _pending)) return false;
            // This completion is terminal even if the host payload is malformed.
            _pending = null;
            return payload != null && payload.Sequence == ticket.Sequence &&
                CoopCampaignMapTooltipPayloadCodec.MatchesInterest(payload, SessionId, ticket.Request.EntityId,
                    ticket.Request.Generation, ticket.Request.IsExtended, ticket.Sequence - 1) &&
                CoopCampaignMapTooltipPayloadCodec.TrySerialize(payload, out bytes);
        }

        public void Revoke() { _revoked = true; _pending = null; }

        private bool ValidRequest(CoopCampaignMapTooltipRequest request)
        {
            return request != null && request.LeaseId == LeaseId &&
                string.Equals(request.SessionId, SessionId, StringComparison.Ordinal) &&
                CoopCampaignMapTooltipPayloadCodec.IsValidCorrelation(request.SessionId, request.EntityId, request.Generation, 1);
        }

        private bool Observe(object peer, long now)
        {
            if (_revoked || !ReferenceEquals(peer, _peer)) return false;
            if (now < 0 || now < _lastClock) { Revoke(); return false; }
            _lastClock = now;
            if (_pending != null && now - _pendingAt >= CoopCampaignMapTooltipInterest.MaximumAgeMilliseconds) _pending = null;
            return true;
        }
    }
}
