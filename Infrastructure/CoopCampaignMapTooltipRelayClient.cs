using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CoopSpectator.Infrastructure
{
    // Dedicated-side endpoint. Caller owns round-robin selection of at most two
    // pending peer tickets. Only one local batch may be outstanding at a time.
    internal sealed class CoopCampaignMapTooltipRelayClient
    {
        private readonly CoopTooltipRelayFrame _binding;
        private CoopTooltipRelayFrame _pending;
        private long _lastRound;
        private long _lastClock;
        private bool _revoked;
        internal bool IsRevoked => _revoked;

        internal CoopCampaignMapTooltipRelayClient(CoopTooltipRelayFrame binding, CoopMissionProcessIdentity self)
        {
            if (binding == null || binding.Kind != CoopTooltipRelayKind.Binding || self == null || !self.Matches(binding.Dedicated))
                throw new ArgumentException("Tooltip binding does not own this server.");
            _binding = CoopCampaignMapTooltipRelay.Decode(CoopCampaignMapTooltipRelay.Encode(binding));
        }
        internal static CoopCampaignMapTooltipRelayClient TryOpen()
        {
            string inherited = Environment.GetEnvironmentVariable(CoopCampaignMapTooltipRelay.SessionEnvironment);
            if (!Guid.TryParseExact(inherited, "N", out var run) || run == Guid.Empty) return null;
            try
            {
                var binding = CoopCampaignMapTooltipRelay.Read(run, CoopTooltipRelayKind.Binding);
                if (binding == null || !binding.Host.IsAlive()) return null;
                using (var self = Process.GetCurrentProcess())
                    return new CoopCampaignMapTooltipRelayClient(binding, CoopMissionProcessIdentity.Capture(self));
            }
            catch (Exception) { return null; }
        }

        internal bool TrySubmit(IReadOnlyList<CoopCampaignMapTooltipCaptureTicket> tickets, long now, long frequency,
            Func<CoopMissionProcessIdentity, bool> isAlive)
        {
            if (!AdmitClock(now) || frequency <= 0 || isAlive == null || tickets == null || tickets.Count < 1 || tickets.Count > CoopCampaignMapTooltipRelay.MaxItems) return false;
            if (_pending != null && CoopCampaignMapTooltipRelay.IsFresh(_pending, now, frequency)) return false;
            if (!isAlive(_binding.Host) || !isAlive(_binding.Dedicated)) { Revoke(); return false; }
            // Stopwatch timestamps advance across mission adapter re-creation in
            // the same process, so host round deduplication cannot get stuck at 1.
            if (now <= _lastRound) return false;
            var frame = new CoopTooltipRelayFrame { Kind = CoopTooltipRelayKind.Requests, Run = _binding.Run,
                Host = _binding.Host, Dedicated = _binding.Dedicated, Round = now, SentTimestamp = now };
            foreach (var ticket in tickets)
            {
                if (ticket == null) return false;
                frame.Items.Add(new CoopTooltipRelayItem { Request = ticket.Request, Sequence = ticket.Sequence });
            }
            try { CoopCampaignMapTooltipRelay.Write(frame); }
            catch (Exception) { Revoke(); return false; }
            _pending = frame; _lastRound = now; return true;
        }

        internal CoopTooltipRelayFrame TryReceive(long now, long frequency, Func<CoopMissionProcessIdentity, bool> isAlive)
        {
            if (!AdmitClock(now) || isAlive == null || _pending == null) return null;
            if (!CoopCampaignMapTooltipRelay.IsFresh(_pending, now, frequency)) { _pending = null; return null; }
            if (!isAlive(_binding.Host) || !isAlive(_binding.Dedicated)) { Revoke(); return null; }
            var response = CoopCampaignMapTooltipRelay.Read(_binding.Run, CoopTooltipRelayKind.Responses);
            if (!CoopCampaignMapTooltipRelay.MatchesBinding(response, _binding) || response.Round != _pending.Round ||
                response.SentTimestamp != _pending.SentTimestamp || !CoopCampaignMapTooltipRelay.IsFresh(response, now, frequency) ||
                response.Items.Count != _pending.Items.Count) return null;
            for (int i = 0; i < _pending.Items.Count; i++)
                if (response.Items[i].Sequence != _pending.Items[i].Sequence ||
                    !CoopCampaignMapTooltipRelay.SameRequest(response.Items[i].Request, _pending.Items[i].Request)) return null;
            _pending = null;
            return response; // Caller STILL rechecks exact peer, ticket and host disclosure.
        }

        internal void Revoke() { _revoked = true; _pending = null; }
        private bool AdmitClock(long now)
        {
            if (_revoked) return false;
            if (now <= 0 || now < _lastClock) { Revoke(); return false; }
            _lastClock = now; return true;
        }
    }
}
