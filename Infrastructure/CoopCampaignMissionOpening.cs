using System;

namespace CoopSpectator.Infrastructure
{
    // One synchronous native dispatch only. Never infer origin from a later Current.
    internal sealed class CoopCampaignMissionOpening<T> where T : class
    {
        private readonly int _thread;
        private T _value;
        private bool _closed, _invalid;
        private int _count;

        public CoopCampaignMissionOpening(int thread) { _thread = thread; }

        public void Observe(T value, int thread)
        {
            if (_closed) return;
            if (thread != _thread || value == null || ++_count != 1) { _invalid = true; return; }
            _value = value;
        }

        public T Complete(int thread)
        {
            if (_closed) throw new InvalidOperationException("Opening capture already closed.");
            _closed = true;
            T value = _value;
            _value = null;
            if (_invalid || _count != 1 || thread != _thread)
                throw new InvalidOperationException("Native opening return not uniquely captured.");
            return value;
        }
    }
}
