using System;

namespace CoopSpectator.Infrastructure
{
    internal static class CustomGameJoinContextState
    {
        private static readonly object Sync = new object();

        private static string _serverName = string.Empty;
        private static string _serverAddress = string.Empty;
        private static int _serverPort;
        private static bool _allowLocalBattleRosterFileFallback = true;
        private static DateTime _updatedUtc = DateTime.MinValue;

        public static void Update(
            string serverName,
            string serverAddress,
            int serverPort,
            bool allowLocalBattleRosterFileFallback,
            string source)
        {
            lock (Sync)
            {
                _serverName = Normalize(serverName);
                _serverAddress = Normalize(serverAddress);
                _serverPort = serverPort;
                _allowLocalBattleRosterFileFallback = allowLocalBattleRosterFileFallback;
                _updatedUtc = DateTime.UtcNow;
            }

            ModLogger.Info(
                "CustomGameJoinContextState: updated current custom-game join context. " +
                "serverName=" + Normalize(serverName) +
                " address=" + Normalize(serverAddress) +
                " port=" + serverPort +
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
