using System;
using System.Collections.Generic;

namespace CoopSpectator.Infrastructure
{
    // Host-thread identity lease, independent of transport/progress revisions.
    // T must be the complete native equipment value, not merely Item.StringId.
    // Call Clear on hidden/failed capture and session reset. No native UI actions.
    internal sealed class CoopCampaignMapPlunderIdentity<T>
    {
        private object _owner;
        private string _menuId;
        private T[] _equipment = Array.Empty<T>();
        private string[] _tokens = Array.Empty<string>();
        private readonly IEqualityComparer<T> _comparer;

        internal CoopCampaignMapPlunderIdentity(IEqualityComparer<T> comparer = null)
        { _comparer = comparer ?? EqualityComparer<T>.Default; }

        internal void Clear()
        {
            _owner = null; _menuId = null;
            _equipment = Array.Empty<T>(); _tokens = Array.Empty<string>();
        }

        internal bool TryUpdate(object owner, string menuId, IReadOnlyList<T> equipment,
            out string[] tokens)
        {
            tokens = null;
            if (owner == null || string.IsNullOrEmpty(menuId) || menuId.Length > 256 ||
                equipment == null || equipment.Count > CoopCampaignMapMenuPresentation.MaxPlunderItems)
            { Clear(); return false; }
            bool sameOwner = ReferenceEquals(owner, _owner) &&
                string.Equals(menuId, _menuId, StringComparison.Ordinal);
            var nextEquipment = new T[equipment.Count];
            var nextTokens = new string[equipment.Count];
            for (int i = 0; i < equipment.Count; i++)
            {
                nextEquipment[i] = equipment[i];
                // Slot identity prevents duplicate items from aliasing. Reorder
                // deliberately invalidates changed slots; count updates do not.
                nextTokens[i] = sameOwner && i < _equipment.Length &&
                    _comparer.Equals(_equipment[i], equipment[i])
                    ? _tokens[i] : "plunder:" + Guid.NewGuid().ToString("N");
            }
            _owner = owner; _menuId = menuId;
            _equipment = nextEquipment; _tokens = nextTokens;
            tokens = (string[])nextTokens.Clone();
            return true;
        }

        internal bool TryResolve(object owner, string menuId, string token, out T equipment)
        {
            equipment = default(T);
            if (!ReferenceEquals(owner, _owner) || owner == null ||
                !string.Equals(menuId, _menuId, StringComparison.Ordinal) || string.IsNullOrEmpty(token)) return false;
            for (int i = 0; i < _tokens.Length; i++)
                if (string.Equals(_tokens[i], token, StringComparison.Ordinal))
                { equipment = _equipment[i]; return true; }
            return false;
        }
    }
}
