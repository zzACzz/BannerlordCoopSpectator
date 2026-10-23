using System;
using System.Collections.Generic;

namespace CoopSpectator.Infrastructure
{
    // One instance per native thumbnail cache. No game objects, rendering calls,
    // network publication or global random state are owned by this registry.
    public sealed class CoopPortraitPoseEvidence
    {
        public const int MaxRenderKeyCharacters = 8192;
        private readonly object _gate = new object();
        private readonly int _capacity;
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private long _nextTicket;
        private bool _closed;

        private sealed class Entry
        {
            public long Ticket;
            public int Pose = -1;
        }

        public CoopPortraitPoseEvidence(int capacity = 512)
        {
            if (capacity < 1 || capacity > 4096) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
        }

        // Call only for a real cache miss, with the exact native normalized key
        // (including image size). A cache hit must not start a new observation.
        public long Begin(string renderKey)
        {
            if (!ValidKey(renderKey)) return 0;
            lock (_gate)
            {
                if (_closed) return 0;
                bool replacing = _entries.Remove(renderKey);
                if (_nextTicket == long.MaxValue || (!replacing && _entries.Count >= _capacity)) return 0;
                long ticket = ++_nextTicket;
                _entries.Add(renderKey, new Entry { Ticket = ticket });
                return ticket;
            }
        }

        // Commit only after successful creation, using the pose selected for the
        // actual camera/entity, not a later discarded native selector result.
        public bool Complete(string renderKey, long ticket, int heroPose)
        {
            lock (_gate)
            {
                if (!Matches(renderKey, ticket, out var entry)) return false;
                if (heroPose < 0 || heroPose > 7) { _entries.Remove(renderKey); return false; }
                // Duplicate callbacks cannot rewrite already-published evidence.
                if (entry.Pose >= 0) return entry.Pose == heroPose;
                entry.Pose = heroPose;
                return true;
            }
        }

        public bool TryGet(string renderKey, out int heroPose)
        {
            heroPose = -1;
            if (!ValidKey(renderKey)) return false;
            lock (_gate)
            {
                if (_closed || !_entries.TryGetValue(renderKey, out var entry) || entry.Pose < 0) return false;
                heroPose = entry.Pose;
                return true;
            }
        }

        public void Cancel(string renderKey, long ticket)
        {
            lock (_gate) { if (Matches(renderKey, ticket, out _)) _entries.Remove(renderKey); }
        }

        // Synchronous native cache eviction, not an asynchronous request callback.
        public void Forget(string renderKey)
        {
            if (!ValidKey(renderKey)) return;
            lock (_gate) { _entries.Remove(renderKey); }
        }

        // Cache clearing invalidates pending callbacks too. Ticket numbers must
        // never be reset, otherwise a pre-clear callback could match a new entry.
        public void Clear()
        {
            lock (_gate) { _entries.Clear(); }
        }

        public void Close()
        {
            lock (_gate) { _closed = true; _entries.Clear(); }
        }

        private bool Matches(string key, long ticket, out Entry entry)
        {
            entry = null;
            return !_closed && ticket > 0 && ValidKey(key) &&
                _entries.TryGetValue(key, out entry) && entry.Ticket == ticket;
        }

        private static bool ValidKey(string key) => !string.IsNullOrEmpty(key) &&
            key.Length <= MaxRenderKeyCharacters;
    }
}
