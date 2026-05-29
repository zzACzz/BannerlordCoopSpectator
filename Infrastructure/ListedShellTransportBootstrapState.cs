using System;

namespace CoopSpectator.Infrastructure
{
    internal static class ListedShellTransportBootstrapState
    {
        private static readonly object Sync = new object();
        private static readonly TimeSpan ClientBootstrapLifetime = TimeSpan.FromMinutes(2);

        private static bool _clientBootstrapArmed;
        private static string _gameType = string.Empty;
        private static string _serverAddress = string.Empty;
        private static int _serverPort;
        private static int _sessionKey;
        private static int _peerIndex;
        private static DateTime _armedUtc = DateTime.MinValue;

        public static void ArmClientReceiveBootstrap(
            string gameType,
            string serverAddress,
            int serverPort,
            int sessionKey,
            int peerIndex,
            string source)
        {
            lock (Sync)
            {
                _clientBootstrapArmed = true;
                _gameType = Normalize(gameType);
                _serverAddress = Normalize(serverAddress);
                _serverPort = serverPort;
                _sessionKey = sessionKey;
                _peerIndex = peerIndex;
                _armedUtc = DateTime.UtcNow;
            }

            ModLogger.Info(
                "ListedShellTransportBootstrapState: armed listed client receive-bootstrap ownership. " +
                "GameType=" + Normalize(gameType) +
                " Address=" + Normalize(serverAddress) +
                " Port=" + serverPort +
                " SessionKey=" + sessionKey +
                " PeerIndex=" + peerIndex +
                " Source=" + Normalize(source) + ".");
        }

        public static bool ShouldOwnClientReceiveBootstrap()
        {
            lock (Sync)
            {
                if (!_clientBootstrapArmed)
                    return false;

                if (_armedUtc == DateTime.MinValue ||
                    DateTime.UtcNow - _armedUtc > ClientBootstrapLifetime)
                {
                    Clear_NoLock();
                    return false;
                }

                return string.Equals(_gameType, CoopGameModeIds.OfficialTeamDeathmatch, StringComparison.Ordinal);
            }
        }

        public static void DisarmClientReceiveBootstrap(string source)
        {
            string gameType;
            string serverAddress;
            int serverPort;
            int sessionKey;
            int peerIndex;

            lock (Sync)
            {
                gameType = _gameType;
                serverAddress = _serverAddress;
                serverPort = _serverPort;
                sessionKey = _sessionKey;
                peerIndex = _peerIndex;
                Clear_NoLock();
            }

            ModLogger.Info(
                "ListedShellTransportBootstrapState: disarmed listed client receive-bootstrap ownership. " +
                "GameType=" + Normalize(gameType) +
                " Address=" + Normalize(serverAddress) +
                " Port=" + serverPort +
                " SessionKey=" + sessionKey +
                " PeerIndex=" + peerIndex +
                " Source=" + Normalize(source) + ".");
        }

        private static void Clear_NoLock()
        {
            _clientBootstrapArmed = false;
            _gameType = string.Empty;
            _serverAddress = string.Empty;
            _serverPort = 0;
            _sessionKey = 0;
            _peerIndex = 0;
            _armedUtc = DateTime.MinValue;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
