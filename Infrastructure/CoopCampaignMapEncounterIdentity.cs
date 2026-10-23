using System;
using System.Collections.Generic;

namespace CoopSpectator.Infrastructure
{
    // Host-thread capability lifetime. Caller must clear on session/hidden/failed
    // capture and resolve only after successful-publication and peer admission.
    // Sources are exact native Party/Settlement references, never client IDs.
    internal sealed class CoopCampaignMapEncounterIdentity
    {
        private object _context, _overlay;
        private string _menu;
        private object[][] _sources = { Array.Empty<object>(), Array.Empty<object>() };
        private string[][] _tokens = { Array.Empty<string>(), Array.Empty<string>() };

        internal void Clear()
        {
            _context = _overlay = null; _menu = null;
            _sources = new[] { Array.Empty<object>(), Array.Empty<object>() };
            _tokens = new[] { Array.Empty<string>(), Array.Empty<string>() };
        }

        internal bool TryUpdate(object context, object overlay, string menu,
            IReadOnlyList<object> attackers, IReadOnlyList<object> defenders,
            out string[] attackerTokens, out string[] defenderTokens)
        {
            attackerTokens = defenderTokens = null;
            if (context == null || overlay == null || string.IsNullOrEmpty(menu) || menu.Length > 256 ||
                !Valid(attackers) || !Valid(defenders))
            { Clear(); return false; }
            bool sameOwner = ReferenceEquals(context, _context) && ReferenceEquals(overlay, _overlay) &&
                string.Equals(menu, _menu, StringComparison.Ordinal);
            var incoming = new[] { attackers, defenders };
            var sources = new object[2][];
            var tokens = new string[2][];
            for (int side = 0; side < 2; side++)
            {
                sources[side] = new object[incoming[side].Count];
                tokens[side] = new string[incoming[side].Count];
                for (int slot = 0; slot < incoming[side].Count; slot++)
                {
                    object source = incoming[side][slot];
                    sources[side][slot] = source;
                    tokens[side][slot] = sameOwner && slot < _sources[side].Length &&
                        ReferenceEquals(source, _sources[side][slot]) ? _tokens[side][slot] :
                        "encounter:" + Guid.NewGuid().ToString("N");
                }
            }
            _context = context; _overlay = overlay; _menu = menu;
            _sources = sources; _tokens = tokens;
            attackerTokens = (string[])tokens[0].Clone();
            defenderTokens = (string[])tokens[1].Clone();
            return true;
        }

        internal bool TryResolve(object context, object overlay, string menu, string token, out object source)
        {
            source = null;
            if (context == null || overlay == null || !ReferenceEquals(context, _context) ||
                !ReferenceEquals(overlay, _overlay) || !string.Equals(menu, _menu, StringComparison.Ordinal) ||
                string.IsNullOrEmpty(token)) return false;
            for (int side = 0; side < 2; side++)
                for (int slot = 0; slot < _tokens[side].Length; slot++)
                    if (string.Equals(token, _tokens[side][slot], StringComparison.Ordinal))
                    { source = _sources[side][slot]; return true; }
            return false;
        }

        private static bool Valid(IReadOnlyList<object> sources)
        {
            if (sources == null || sources.Count > CoopCampaignMapEncounterPresentation.MaxPartiesPerSide) return false;
            for (int i = 0; i < sources.Count; i++) if (sources[i] == null) return false;
            return true;
        }
    }
}
