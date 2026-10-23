using System;
using System.Collections.Generic;

namespace CoopSpectator.Infrastructure
{
    // Owner-thread leases for prepared native bar hint sources. VisualId is
    // deliberately absent: a changed icon must not select another provider.
    internal sealed class CoopCampaignMapBarHintIdentity<T> where T : class
    {
        private object _owner;
        private string _session;
        private Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        internal sealed class Entry
        {
            internal string Key;
            internal T Source;
            internal string Token;
        }

        internal void Clear()
        { _owner = null; _session = null; _entries.Clear(); }

        internal bool TryUpdate(object owner, string session, bool visible,
            IReadOnlyList<string> keys, IReadOnlyList<T> sources, out string[] tokens)
        {
            tokens = null;
            if (!visible || owner == null || !Guid.TryParseExact(session, "N", out var sessionId) ||
                sessionId == Guid.Empty || keys == null || sources == null || keys.Count != sources.Count ||
                keys.Count > CoopCampaignMapBarPresentation.MaxItems * 3)
            { Clear(); return false; }
            bool same = ReferenceEquals(owner, _owner) && string.Equals(session, _session, StringComparison.Ordinal);
            var next = new Dictionary<string, Entry>(StringComparer.Ordinal);
            var output = new string[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                if (!CoopCampaignMapBarPresentation.Key(key) || sources[i] == null || next.ContainsKey(key))
                { Clear(); return false; }
                string token = same && _entries.TryGetValue(key, out var old) && ReferenceEquals(old.Source, sources[i])
                    ? old.Token : "barhint:" + Guid.NewGuid().ToString("N");
                next.Add(key, new Entry { Key = key, Source = sources[i], Token = token });
                output[i] = token;
            }
            _owner = owner; _session = session; _entries = next;
            tokens = output;
            return true;
        }

        internal bool TryResolve(object owner, string session, string token, out T source)
        {
            source = null;
            if (owner == null || !ReferenceEquals(owner, _owner) ||
                !string.Equals(session, _session, StringComparison.Ordinal) || !IsToken(token)) return false;
            foreach (var entry in _entries.Values)
                if (string.Equals(entry.Token, token, StringComparison.Ordinal))
                { source = entry.Source; return true; }
            return false;
        }

        internal static bool IsToken(string token) => token != null && token.Length == 40 &&
            token.StartsWith("barhint:", StringComparison.Ordinal) &&
            Guid.TryParseExact(token.Substring(8), "N", out var id) && id != Guid.Empty;
    }
}
