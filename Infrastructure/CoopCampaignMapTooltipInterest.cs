using System;

namespace CoopSpectator.Infrastructure
{
    // Immutable semantic request; never includes provider names, paths or rows.
    public sealed class CoopCampaignMapTooltipRequest
    {
        public string SessionId { get; }
        public Guid LeaseId { get; }
        public string EntityId { get; }
        public long Generation { get; }
        public bool IsExtended { get; }

        internal CoopCampaignMapTooltipRequest(string session, Guid lease, string entity, long generation, bool extended)
        { SessionId = session; LeaseId = lease; EntityId = entity; Generation = generation; IsExtended = extended; }
    }

    // Client-only correlation/lifetime policy. Not peer authorization or a wire
    // endpoint. Call only on the client owner thread, with a monotonic local clock.
    // Every response, including denial, must arrive from the admitted server and
    // carry the exact request envelope. A new map/connection needs a NEW lease.
    public sealed class CoopCampaignMapTooltipInterest
    {
        public const long MaximumAgeMilliseconds = 2000;
        public const long MinimumRequestIntervalMilliseconds = 250;
        private string _session;
        private Guid _lease;
        private long _generation;
        private long _lastClock = -1;
        private long _lastRequestAt = -1;
        private long _lastTerminalAt = -1;
        private long _requestAt;
        private long _displayRequestAt;
        private long _lastSequence;
        private string _entity;
        private bool _extended;
        private CoopCampaignMapTooltipRequest _pending;
        private byte[] _displayBytes;

        public CoopCampaignMapTooltipRequest PendingRequest => _pending;
        public bool HasGrant => _lease != Guid.Empty;
        public Guid LeaseId => _lease;
        public bool IsCurrentPending(Guid lease, long generation, bool disclosed, long now) =>
            Advance(now, disclosed) && _pending != null && _lease == lease && _pending.Generation == generation;

        // Trusted adapter only: validate server/mission identity before granting.
        // Identical grants are idempotent and cannot revive a closed interest.
        public bool TryGrant(string session, Guid lease, long now)
        {
            if (!ObserveClock(now)) return false;
            if (lease == Guid.Empty || !CoopCampaignMapTooltipPayloadCodec.IsValidCorrelation(session, "grant", 1, 1))
                return false;
            if (_lease == lease) return string.Equals(_session, session, StringComparison.Ordinal);
            Reset();
            _session = session;
            _lease = lease;
            return true;
        }

        // Retain an unexpired card only for a refresh of the SAME hover. The new
        // response has a fresh generation; delayed responses to old requests fail.
        // A rejected/throttled changed hover still clears the old display.
        public bool TryRequest(string entity, bool extended, bool currentlyDisclosed, long now,
            out CoopCampaignMapTooltipRequest request)
        {
            request = null;
            if (!Advance(now, currentlyDisclosed) || _lease == Guid.Empty) return false;
            if (!string.Equals(entity, _entity, StringComparison.Ordinal) || extended != _extended)
            {
                Clear();
                _entity = entity;
                _extended = extended;
            }
            if (!CoopCampaignMapTooltipPayloadCodec.IsValidCorrelation(_session, entity, 1, 1))
            { Clear(); return false; }
            if (_lastRequestAt >= 0 && now - _lastRequestAt < MinimumRequestIntervalMilliseconds) return false;
            // Receipt is causally after server admission. Spacing from send time
            // alone lets variable transit delay compress successive arrivals at
            // the server and trigger its independent capture-rate denial.
            if (_lastTerminalAt >= 0 && now - _lastTerminalAt < MinimumRequestIntervalMilliseconds) return false;
            if (_generation == long.MaxValue) { Reset(); return false; }
            request = new CoopCampaignMapTooltipRequest(_session, _lease, entity, ++_generation, extended);
            _pending = request;
            _requestAt = _lastRequestAt = now;
            return true;
        }

        public bool TryAccept(Guid lease, CoopCampaignMapTooltipPayload payload, bool currentlyDisclosed, long now)
        {
            if (!Advance(now, currentlyDisclosed) || _pending == null || lease != _lease ||
                !CoopCampaignMapTooltipPayloadCodec.MatchesInterest(payload, _session, _pending.EntityId,
                    _pending.Generation, _pending.IsExtended, _lastSequence) ||
                !CoopCampaignMapTooltipPayloadCodec.TrySerialize(payload, out byte[] bytes)) return false;
            _displayBytes = bytes; // No mutable caller-owned row references escape.
            _displayRequestAt = _requestAt; // Transit time consumes the display age.
            _lastSequence = payload.Sequence;
            _pending = null; // Exactly one terminal response per request.
            _lastTerminalAt = now;
            return true;
        }

        public bool TryDeny(CoopCampaignMapTooltipRequest request, bool currentlyDisclosed, long now)
        {
            if (!Advance(now, currentlyDisclosed) || !MatchesPending(request)) return false;
            _pending = null;
            _displayBytes = null;
            _lastTerminalAt = now;
            return true;
        }

        // UI must call with CURRENT disclosure on every read/tick, not a cached
        // grant. False, dialogue, hover exit or unavailable map must hide the UI.
        public bool TryGetDisplay(bool currentlyDisclosed, long now, out CoopCampaignMapTooltipPayload payload)
        {
            payload = null;
            return HasCurrentDisplay(currentlyDisclosed, now) &&
                CoopCampaignMapTooltipPayloadCodec.TryDeserialize(_displayBytes, out payload);
        }

        // Cheap per-frame expiry/disclosure check; decode only when rebuilding
        // the cached UI after an accepted card, not on each rendering tick.
        public bool HasCurrentDisplay(bool currentlyDisclosed, long now) =>
            Advance(now, currentlyDisclosed) && _displayBytes != null;

        // Hover exit/dialogue/hide: keep lease, generation and rate budget.
        public void Clear()
        { _pending = null; _displayBytes = null; _entity = null; _extended = false; }

        // Mission removal, connection loss, host-session change: revoke grant.
        // Generation/sequence reset is safe only with a fresh server-issued lease.
        public void Reset()
        {
            Clear();
            _session = null; _lease = Guid.Empty; _generation = 0; _lastSequence = 0;
            _lastRequestAt = _lastTerminalAt = -1;
        }

        private bool MatchesPending(CoopCampaignMapTooltipRequest request)
        {
            return request != null && _pending != null && request.LeaseId == _lease &&
                string.Equals(request.SessionId, _session, StringComparison.Ordinal) &&
                string.Equals(request.EntityId, _pending.EntityId, StringComparison.Ordinal) &&
                request.Generation == _pending.Generation && request.IsExtended == _pending.IsExtended;
        }

        private bool Advance(long now, bool disclosed)
        {
            if (!ObserveClock(now)) return false;
            if (!disclosed) { Clear(); return false; }
            if (_pending != null && now - _requestAt >= MaximumAgeMilliseconds) _pending = null;
            if (_displayBytes != null && now - _displayRequestAt >= MaximumAgeMilliseconds) _displayBytes = null;
            return true;
        }

        private bool ObserveClock(long now)
        {
            if (now < 0 || now < _lastClock) { Reset(); return false; }
            _lastClock = now;
            return true;
        }
    }
}
