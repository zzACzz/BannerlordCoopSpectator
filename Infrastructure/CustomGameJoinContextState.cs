using System;

namespace CoopSpectator.Infrastructure
{
    internal static class CustomGameJoinContextState
    {
        private static readonly object Sync = new object();
        private static bool _allowLocalBattleRosterFileFallback = true;

        public static void Update(
            string serverName,
            string serverAddress,
            int serverPort,
            string gameType,
            bool isOfficial,
            bool allowLocalBattleRosterFileFallback,
            string source)
        {
            lock (Sync)
            {
                _allowLocalBattleRosterFileFallback = allowLocalBattleRosterFileFallback;
            }

            ModLogger.Info(
                "CustomGameJoinContextState: updated current custom-game join context. " +
                "serverName=" + Normalize(serverName) +
                " address=" + Normalize(serverAddress) +
                " port=" + serverPort +
                " gameType=" + Normalize(gameType) +
                " isOfficial=" + isOfficial +
                " allowLocalBattleRosterFallback=" + allowLocalBattleRosterFileFallback +
                " source=" + (source ?? "unknown") + ".");
        }

        public static bool ShouldAllowLocalBattleRosterFileFallback()
        {
            lock (Sync)
            {
                return _allowLocalBattleRosterFileFallback;
            }
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
