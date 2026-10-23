using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CoopSpectator.Infrastructure
{
    // Owner-thread helper for managed campaign missiles, never native wrappers.
    // IDs survive list compaction, but not departure, Clear or siege replacement.
    // The monotonically increasing counter deliberately survives Clear.
    internal sealed class CoopCampaignMapSiegeShotIdentity<T> where T : class
    {
        private sealed class ReferenceComparer : IEqualityComparer<T>
        {
            public bool Equals(T x, T y) => ReferenceEquals(x, y);
            public int GetHashCode(T value) => RuntimeHelpers.GetHashCode(value);
        }

        private readonly int _capacity;
        private readonly ReferenceComparer _comparer = new ReferenceComparer();
        private Dictionary<T, long> _current;
        private object _epoch;
        private long _lastId;

        internal CoopCampaignMapSiegeShotIdentity(int capacity)
        {
            if (capacity < 1 || capacity > 256) throw new ArgumentOutOfRangeException(nameof(capacity));
            _capacity = capacity;
            _current = new Dictionary<T, long>(_comparer);
        }

        // Supply the full current missile population, not just visible projectiles.
        // Caller must validate source lifetime before committing this operation.
        // Rejected batches are atomic and do not consume IDs or prune live shots.
        internal bool TryAssign(object epoch, IReadOnlyList<T> shots, out long[] ids)
        {
            ids = null;
            if (epoch == null || shots == null || shots.Count > _capacity) return false;
            var next = new Dictionary<T, long>(_comparer);
            var result = new long[shots.Count];
            long last = _lastId;
            bool sameEpoch = ReferenceEquals(epoch, _epoch);
            for (int i = 0; i < shots.Count; i++)
            {
                T shot = shots[i];
                if (shot == null || next.ContainsKey(shot)) return false;
                long id;
                if (!sameEpoch || !_current.TryGetValue(shot, out id))
                {
                    if (last == long.MaxValue) return false;
                    id = ++last;
                }
                next.Add(shot, id);
                result[i] = id;
            }
            _current = next;
            _epoch = epoch;
            _lastId = last;
            ids = result;
            return true;
        }

        internal void Clear()
        {
            _current.Clear();
            _epoch = null;
        }
    }
}
