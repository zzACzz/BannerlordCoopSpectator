using System;
using System.Collections.Generic;

namespace CoopSpectator.Infrastructure
{
    // Preserves installed MobilePartyVisualManager (priority 10) / SettlementVisualManager
    // (priority 40) hover ordering without constructing campaign objects in the client.
    public sealed class CoopCampaignMapPickResolver
    {
        public const int MaximumHits = 128;
        public const int MaximumBindings = 65536;
        private sealed class Binding { public string Id; public bool Mobile; }
        private readonly Dictionary<UIntPtr, Binding> _bindings = new Dictionary<UIntPtr, Binding>();

        public void Register(UIntPtr handle, string id, bool mobile)
        {
            if (handle == UIntPtr.Zero || string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Invalid pick binding.");
            if (_bindings.TryGetValue(handle, out Binding old))
            {
                if (old == null || old.Id != id || old.Mobile != mobile) _bindings[handle] = null;
                return;
            }
            if (_bindings.Count >= MaximumBindings) throw new InvalidOperationException("Pick binding limit.");
            _bindings.Add(handle, new Binding { Id = id, Mobile = mobile });
        }

        public void Remove(UIntPtr handle) => _bindings.Remove(handle);
        public void Clear() => _bindings.Clear();

        public string Resolve(UIntPtr[] hits, int count, Func<string, bool> admitted,
            Func<UIntPtr, bool> visible)
        {
            if (hits == null || count < 0 || count > hits.Length || count > MaximumHits ||
                admitted == null || visible == null) return null;
            // Native mobile reverse traversal overwrites hoveredVisual at each hit.
            // Thus the first admitted mobile hit in the original array wins.
            for (int i = 0; i < count; i++)
                if (TryGet(hits[i], true, admitted, visible, out string mobile)) return mobile;
            // Native settlements set hoveredVisual only while it is null.
            for (int i = count - 1; i >= 0; i--)
                if (TryGet(hits[i], false, admitted, visible, out string settlement)) return settlement;
            return null;
        }

        private bool TryGet(UIntPtr handle, bool mobile, Func<string, bool> admitted,
            Func<UIntPtr, bool> visible, out string id)
        {
            id = null;
            if (!_bindings.TryGetValue(handle, out Binding binding) || binding == null ||
                binding.Mobile != mobile || !admitted(binding.Id) || !visible(handle)) return false;
            id = binding.Id;
            return true;
        }
    }
}
