using System;

namespace CoopSpectator.Infrastructure
{
    internal static class CoopUnitSelectionSearchContract
    {
        public static string NormalizeQuery(string query)
        {
            return (query ?? string.Empty).Trim();
        }

        public static bool MatchesDisplayName(string displayName, string query)
        {
            string normalizedQuery = NormalizeQuery(query);
            if (normalizedQuery.Length == 0)
                return true;

            return (displayName ?? string.Empty).IndexOf(
                       normalizedQuery,
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
