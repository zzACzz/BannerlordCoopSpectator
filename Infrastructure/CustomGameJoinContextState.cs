using System;

namespace CoopSpectator.Infrastructure
{
    internal static class CustomGameJoinContextState
    {
        private static readonly object Sync = new object();

        private static string _serverName = string.Empty;
        private static string _serverAddress = string.Empty;
        private static string _gameType = string.Empty;
        private static int _serverPort;
        private static bool _isOfficial;
        private static bool _allowLocalBattleRosterFileFallback = true;
        private static DateTime _updatedUtc = DateTime.MinValue;
        private static readonly TimeSpan ListedShellBootstrapJoinContextLifetime = TimeSpan.FromMinutes(2);

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
                _serverName = Normalize(serverName);
                _serverAddress = Normalize(serverAddress);
                _serverPort = serverPort;
                _gameType = Normalize(gameType);
                _isOfficial = isOfficial;
                _allowLocalBattleRosterFileFallback = allowLocalBattleRosterFileFallback;
                _updatedUtc = DateTime.UtcNow;
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

        public static bool ShouldOwnListedShellCustomGameBootstrap()
        {
            lock (Sync)
            {
                if (_updatedUtc == DateTime.MinValue ||
                    DateTime.UtcNow - _updatedUtc > ListedShellBootstrapJoinContextLifetime)
                {
                    return false;
                }

                return string.Equals(_gameType, CoopGameModeIds.OfficialTeamDeathmatch, StringComparison.Ordinal);
            }
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
