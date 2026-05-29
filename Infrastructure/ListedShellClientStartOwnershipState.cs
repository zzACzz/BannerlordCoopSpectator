using System;

namespace CoopSpectator.Infrastructure
{
    internal static class ListedShellClientStartOwnershipState
    {
        private static readonly object Sync = new object();
        private static readonly TimeSpan ClientStartLifetime = TimeSpan.FromMinutes(2);

        private static bool _clientStartArmed;
        private static string _gameType = string.Empty;
        private static string _serverName = string.Empty;
        private static string _serverAddress = string.Empty;
        private static int _serverPort;
        private static DateTime _armedUtc = DateTime.MinValue;

        public static void ArmClientStart(
            string gameType,
            string serverName,
            string serverAddress,
            int serverPort,
            string source)
        {
            lock (Sync)
            {
                _clientStartArmed = true;
                _gameType = Normalize(gameType);
                _serverName = Normalize(serverName);
                _serverAddress = Normalize(serverAddress);
                _serverPort = serverPort;
                _armedUtc = DateTime.UtcNow;
            }

            ModLogger.Info(
                "ListedShellClientStartOwnershipState: armed listed client wrapper-start ownership. " +
                "GameType=" + Normalize(gameType) +
                " ServerName=" + Normalize(serverName) +
                " Address=" + Normalize(serverAddress) +
                " Port=" + serverPort +
                " Source=" + Normalize(source) + ".");
        }

        public static bool ShouldOwnClientStart()
        {
            lock (Sync)
            {
                if (!_clientStartArmed)
                    return false;

                if (_armedUtc == DateTime.MinValue ||
                    DateTime.UtcNow - _armedUtc > ClientStartLifetime)
                {
                    Clear_NoLock();
                    return false;
                }

                return string.Equals(_gameType, CoopGameModeIds.OfficialTeamDeathmatch, StringComparison.Ordinal);
            }
        }

        public static void DisarmClientStart(string source)
        {
            string gameType;
            string serverName;
            string serverAddress;
            int serverPort;
            bool hadState;

            lock (Sync)
            {
                hadState = _clientStartArmed ||
                    !string.IsNullOrEmpty(_gameType) ||
                    !string.IsNullOrEmpty(_serverName) ||
                    !string.IsNullOrEmpty(_serverAddress) ||
                    _serverPort != 0 ||
                    _armedUtc != DateTime.MinValue;
                gameType = _gameType;
                serverName = _serverName;
                serverAddress = _serverAddress;
                serverPort = _serverPort;
                Clear_NoLock();
            }

            if (!hadState)
                return;

            ModLogger.Info(
                "ListedShellClientStartOwnershipState: disarmed listed client wrapper-start ownership. " +
                "GameType=" + Normalize(gameType) +
                " ServerName=" + Normalize(serverName) +
                " Address=" + Normalize(serverAddress) +
                " Port=" + serverPort +
                " Source=" + Normalize(source) + ".");
        }

        private static void Clear_NoLock()
        {
            _clientStartArmed = false;
            _gameType = string.Empty;
            _serverName = string.Empty;
            _serverAddress = string.Empty;
            _serverPort = 0;
            _armedUtc = DateTime.MinValue;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
