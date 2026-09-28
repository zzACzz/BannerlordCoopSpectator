using System;
using System.Collections.Generic;
using System.Threading;

namespace CoopSpectator.Infrastructure
{
    // Restores dictionary entries, NOT mutations inside the referenced values.
    // The native capture adapter is restricted to audited replacement-only writes.
    public sealed class CoopCampaignMapTooltipTextTransaction<T> : IDisposable
    {
        public const int MaxVariables = 8192;
        private readonly Dictionary<string, T> _variables;
        private readonly KeyValuePair<string, T>[] _before;
        private readonly Stack<T[]> _evaluated;
        private readonly Stack<T[]> _raw;
        private readonly int _thread;
        private bool _disposed;

        private CoopCampaignMapTooltipTextTransaction(Dictionary<string, T> variables,
            Stack<T[]> evaluated, Stack<T[]> raw, int thread)
        {
            _variables = variables;
            _before = new KeyValuePair<string, T>[variables.Count];
            ((ICollection<KeyValuePair<string, T>>)variables).CopyTo(_before, 0);
            _evaluated = evaluated;
            _raw = raw;
            _thread = thread;
        }

        public static bool TryBegin(Dictionary<string, T> variables, Stack<T[]> evaluated,
            Stack<T[]> raw, int ownerThread, out CoopCampaignMapTooltipTextTransaction<T> transaction)
        {
            transaction = null;
            // A nonempty parser stack means a text operation is already active.
            // Never capture inside it, or repair someone else's incomplete work.
            if (ownerThread <= 0 || Thread.CurrentThread.ManagedThreadId != ownerThread ||
                variables == null || variables.Count > MaxVariables || evaluated == null || raw == null ||
                evaluated.Count != 0 || raw.Count != 0) return false;
            transaction = new CoopCampaignMapTooltipTextTransaction<T>(variables, evaluated, raw, ownerThread);
            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            if (Thread.CurrentThread.ManagedThreadId != _thread)
                throw new InvalidOperationException("Tooltip text transaction must finish on its owner thread.");
            _variables.Clear();
            foreach (var entry in _before) _variables.Add(entry.Key, entry.Value);
            // Native CallFunction does not pop in a finally block. A failing
            // provider can strand parameters; admission proved these were empty.
            _evaluated.Clear();
            _raw.Clear();
            _disposed = true;
        }
    }
}
