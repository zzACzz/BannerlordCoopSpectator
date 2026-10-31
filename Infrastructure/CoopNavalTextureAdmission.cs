using System;
using System.Collections.Generic;
using System.Linq;

namespace CoopSpectator.Infrastructure
{
    // Native defaults activate Native/NavalDLC by their official identities.
    // The resource-only alias needs the same public activation once per process.
    internal sealed class CoopNavalTextureAdmission
    {
        private bool _loaded;
        private bool _loading;

        internal bool Ensure(bool dedicated, bool campaign, IEnumerable<string> activeModules, Action<string> load)
        {
            if (_loaded || _loading || dedicated || campaign || activeModules == null) return false;
            var modules = new HashSet<string>(activeModules, StringComparer.OrdinalIgnoreCase);
            if (!modules.Contains(CoopCampaignContentProfile.NavalAdapterId) || modules.Contains("NavalDLC")) return false;
            if (load == null) throw new ArgumentNullException(nameof(load));
            _loading = true;
            try
            {
                load(CoopCampaignContentProfile.NavalAdapterId);
                _loaded = true;
                return true;
            }
            finally { _loading = false; }
        }
    }
}
