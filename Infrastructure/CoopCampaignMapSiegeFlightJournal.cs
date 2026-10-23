using System;
using System.Collections.Generic;

namespace CoopSpectator.Infrastructure
{
    // Owner-thread bounded delivery history, NOT a delay or a client replay clock.
    // Caller clears on hidden/modal/scene transitions. Client must separately admit
    // session, join baseline and flight time before rendering retained observations.
    internal sealed class CoopCampaignMapSiegeFlightJournal
    {
        internal const int Capacity = 256;
        internal const double RetentionSeconds = 2;
        private sealed class Entry
        {
            internal CoopCampaignMapSiegeFlight Flight;
            internal double Seen;
        }
        private object _epoch;
        private double _lastTime;
        private Dictionary<long, Entry> _entries = new Dictionary<long, Entry>();

        internal bool Merge(object epoch, double seconds, IDictionary<long, CoopCampaignMapSiegeFlight> observations)
        {
            if (epoch == null || double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 ||
                observations == null || observations.Count > Capacity) { Clear(); return false; }
            bool same = ReferenceEquals(epoch, _epoch);
            if (same && seconds < _lastTime) { Clear(); return false; }
            var next = new Dictionary<long, Entry>();
            if (same)
                foreach (var pair in _entries)
                    if (seconds - pair.Value.Seen <= RetentionSeconds) next.Add(pair.Key, pair.Value);
            foreach (var pair in observations)
            {
                var copy = pair.Value?.Clone();
                if (pair.Key <= 0 || copy == null || !copy.IsValid()) { Clear(); return false; }
                next[pair.Key] = new Entry { Flight = copy, Seen = seconds };
            }
            if (next.Count > Capacity) { Clear(); return false; }
            _entries = next; _epoch = epoch; _lastTime = seconds;
            return true;
        }

        internal Dictionary<long, CoopCampaignMapSiegeFlight> Snapshot(object epoch, double seconds)
        {
            var result = new Dictionary<long, CoopCampaignMapSiegeFlight>();
            if (!ReferenceEquals(epoch, _epoch) || double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < _lastTime)
                return result;
            foreach (var pair in _entries)
                if (seconds - pair.Value.Seen <= RetentionSeconds) result.Add(pair.Key, pair.Value.Flight.Clone());
            return result;
        }

        internal void Clear() { _entries.Clear(); _epoch = null; _lastTime = 0; }
    }
}
