using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CoopSpectator.Infrastructure
{
    // Pure startup contract; no registration or native state access.
    internal static class CoopNavalClientModulePolicy
    {
        internal static void RequireLayout(IReadOnlyList<string> modules)
        {
            if (modules == null || modules.Count > 64 || modules.Any(string.IsNullOrWhiteSpace) ||
                modules.Distinct(StringComparer.OrdinalIgnoreCase).Count() != modules.Count)
                throw new IOException("Ambiguous Naval client module order.");
            string[] names = modules.ToArray();
            foreach (string required in new[] { "Native", "SandBoxCore", "Sandbox", "Multiplayer", "CoopSpectator",
                CoopCampaignContentProfile.NavalClientCatalogId, CoopCampaignContentProfile.MapVisualAdapterId, "NavalDLC" })
                if (!names.Contains(required, StringComparer.Ordinal))
                    throw new IOException("Naval client module is missing: " + required);
            if (names.Contains(CoopCampaignContentProfile.NavalAdapterId, StringComparer.OrdinalIgnoreCase) ||
                names.Contains("StoryMode", StringComparer.OrdinalIgnoreCase) ||
                Array.IndexOf(names, "CoopSpectator") >= Array.IndexOf(names, "NavalDLC") || names[names.Length - 1] != "NavalDLC")
                throw new IOException("Restart through the current Warhost Naval launcher; resource alias or unsafe load order detected.");
        }

        internal static void RequireKnownSubmodules(IEnumerable<string> identities)
        {
            string[] actual = identities?.ToArray();
            string[] expected = {
                "NavalDLC.dll|NavalDLC.NavalDLCSubModule",
                "NavalDLC.CustomBattle.dll|NavalDLC.CustomBattle.NavalDLCCustomBattleSubModule",
                "NavalDLC.View.dll|NavalDLC.View.NavalDLCViewSubModule",
                "NavalDLC.GauntletUI.dll|NavalDLC.GauntletUI.NavalDLCGauntletUISubModule"
            };
            if (actual == null || actual.Length != expected.Length ||
                !actual.OrderBy(s => s, StringComparer.Ordinal).SequenceEqual(expected.OrderBy(s => s, StringComparer.Ordinal)))
                throw new IOException("Unsupported Naval gameplay submodule definitions; no startup metadata was changed.");
        }
    }
}
