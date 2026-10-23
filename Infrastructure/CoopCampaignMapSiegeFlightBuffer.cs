using System.Collections.Generic;

namespace CoopSpectator.Infrastructure
{
    // Owner begins/finishes a batch around the native parallel join. Workers only
    // submit detached values and IDs assigned by the owner. No native references.
    internal sealed class CoopCampaignMapSiegeFlightBuffer
    {
        internal sealed class Token { }
        private readonly object _gate = new object();
        private Token _active;
        private bool _failed;
        private readonly Dictionary<long, CoopCampaignMapSiegeFlight> _items =
            new Dictionary<long, CoopCampaignMapSiegeFlight>();
        internal const int Capacity = 256;

        internal Token Begin()
        {
            lock (_gate)
            {
                _items.Clear(); _failed = false;
                return _active = new Token();
            }
        }

        internal bool TryAdd(Token token, long shotId, CoopCampaignMapSiegeFlight flight)
        {
            lock (_gate)
            {
                if (token == null || !ReferenceEquals(token, _active) || _failed) return false;
                // Caller owns the submitted DTO while this synchronous copy runs.
                var copy = flight?.Clone();
                if (shotId <= 0 || copy == null || !copy.IsValid() ||
                    _items.ContainsKey(shotId) || _items.Count >= Capacity)
                { _failed = true; _items.Clear(); return false; }
                _items.Add(shotId, copy);
                return true;
            }
        }

        internal bool TryFinish(Token token, out Dictionary<long, CoopCampaignMapSiegeFlight> flights)
        {
            flights = null;
            lock (_gate)
            {
                if (token == null || !ReferenceEquals(token, _active)) return false;
                _active = null;
                bool valid = !_failed;
                if (valid) flights = new Dictionary<long, CoopCampaignMapSiegeFlight>(_items);
                _items.Clear();
                return valid;
            }
        }

        internal void Clear()
        {
            lock (_gate) { _active = null; _failed = false; _items.Clear(); }
        }
    }
}
