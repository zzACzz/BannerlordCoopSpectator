using System;

namespace CoopSpectator.Infrastructure
{
    internal static class ListedShellClientSessionOwnershipState
    {
        private enum ListedShellClientSessionPhase
        {
            None = 0,
            WrapperStart = 1,
            ReceiveBootstrap = 2,
        }

        private static readonly object Sync = new object();
        private static readonly TimeSpan WrapperStartOwnershipLifetime = TimeSpan.FromMinutes(2);

        private static ListedShellClientSessionPhase _phase;
        private static string _gameType = string.Empty;
        private static string _serverName = string.Empty;
        private static string _serverAddress = string.Empty;
        private static int _serverPort;
        private static int _sessionKey;
        private static int _peerIndex;
        private static DateTime _armedUtc = DateTime.MinValue;

        public static void ArmWrapperStart(
            string gameType,
            string serverName,
            string serverAddress,
            int serverPort,
            int sessionKey,
            int peerIndex,
            string source)
        {
            lock (Sync)
            {
                _phase = ListedShellClientSessionPhase.WrapperStart;
                _gameType = Normalize(gameType);
                _serverName = Normalize(serverName);
                _serverAddress = Normalize(serverAddress);
                _serverPort = serverPort;
                _sessionKey = sessionKey;
                _peerIndex = peerIndex;
                _armedUtc = DateTime.UtcNow;
            }

            ModLogger.Info(
                "ListedShellClientSessionOwnershipState: armed listed client wrapper-start ownership. " +
                "GameType=" + Normalize(gameType) +
                " ServerName=" + Normalize(serverName) +
                " Address=" + Normalize(serverAddress) +
                " Port=" + serverPort +
                " SessionKey=" + sessionKey +
                " PeerIndex=" + peerIndex +
                " Source=" + Normalize(source) + ".");
        }

        public static bool ShouldOwnWrapperStart()
        {
            lock (Sync)
            {
                return IsPhaseActive_NoLock(ListedShellClientSessionPhase.WrapperStart);
            }
        }

        public static void PromoteToReceiveBootstrap(
            string gameType,
            string serverAddress,
            int serverPort,
            int sessionKey,
            int peerIndex,
            string source)
        {
            lock (Sync)
            {
                _phase = ListedShellClientSessionPhase.ReceiveBootstrap;
                _gameType = Normalize(gameType);
                _serverAddress = Normalize(serverAddress);
                _serverPort = serverPort;
                _sessionKey = sessionKey;
                _peerIndex = peerIndex;
                _armedUtc = DateTime.UtcNow;
            }

            ModLogger.Info(
                "ListedShellClientSessionOwnershipState: promoted listed client ownership to receive-bootstrap. " +
                "GameType=" + Normalize(gameType) +
                " ServerName=" + Normalize(_serverName) +
                " Address=" + Normalize(serverAddress) +
                " Port=" + serverPort +
                " SessionKey=" + sessionKey +
                " PeerIndex=" + peerIndex +
                " Source=" + Normalize(source) + ".");
        }

        public static bool ShouldOwnReceiveBootstrap()
        {
            lock (Sync)
            {
                return IsPhaseActive_NoLock(ListedShellClientSessionPhase.ReceiveBootstrap);
            }
        }

        public static bool PreserveReceiveBootstrapAcrossMissionLoop(string source)
        {
            string gameType;
            string serverName;
            string serverAddress;
            int serverPort;
            int sessionKey;
            int peerIndex;

            lock (Sync)
            {
                if (!IsPhaseActive_NoLock(ListedShellClientSessionPhase.ReceiveBootstrap))
                    return false;

                _armedUtc = DateTime.UtcNow;
                gameType = _gameType;
                serverName = _serverName;
                serverAddress = _serverAddress;
                serverPort = _serverPort;
                sessionKey = _sessionKey;
                peerIndex = _peerIndex;
            }

            ModLogger.Info(
                "ListedShellClientSessionOwnershipState: preserved listed client receive-bootstrap ownership across mission loop. " +
                "GameType=" + Normalize(gameType) +
                " ServerName=" + Normalize(serverName) +
                " Address=" + Normalize(serverAddress) +
                " Port=" + serverPort +
                " SessionKey=" + sessionKey +
                " PeerIndex=" + peerIndex +
                " Source=" + Normalize(source) + ".");
            return true;
        }

        public static bool TryResolveWrapperStartTransportContext(
            out string gameType,
            out string serverAddress,
            out int serverPort,
            out int sessionKey,
            out int peerIndex)
        {
            lock (Sync)
            {
                if (!IsPhaseActive_NoLock(ListedShellClientSessionPhase.WrapperStart))
                {
                    gameType = string.Empty;
                    serverAddress = string.Empty;
                    serverPort = 0;
                    sessionKey = 0;
                    peerIndex = 0;
                    return false;
                }

                gameType = _gameType;
                serverAddress = _serverAddress;
                serverPort = _serverPort;
                sessionKey = _sessionKey;
                peerIndex = _peerIndex;
                return !string.IsNullOrEmpty(gameType) &&
                    !string.IsNullOrEmpty(serverAddress) &&
                    serverPort > 0 &&
                    sessionKey > 0 &&
                    peerIndex >= 0;
            }
        }

        public static void Disarm(string source)
        {
            string gameType;
            string serverName;
            string serverAddress;
            int serverPort;
            int sessionKey;
            int peerIndex;
            ListedShellClientSessionPhase phase;
            bool hadState;

            lock (Sync)
            {
                hadState = _phase != ListedShellClientSessionPhase.None ||
                    !string.IsNullOrEmpty(_gameType) ||
                    !string.IsNullOrEmpty(_serverName) ||
                    !string.IsNullOrEmpty(_serverAddress) ||
                    _serverPort != 0 ||
                    _sessionKey != 0 ||
                    _peerIndex != 0 ||
                    _armedUtc != DateTime.MinValue;
                phase = _phase;
                gameType = _gameType;
                serverName = _serverName;
                serverAddress = _serverAddress;
                serverPort = _serverPort;
                sessionKey = _sessionKey;
                peerIndex = _peerIndex;
                Clear_NoLock();
            }

            if (!hadState)
                return;

            ModLogger.Info(
                "ListedShellClientSessionOwnershipState: disarmed listed client session ownership. " +
                "Phase=" + phase +
                " GameType=" + Normalize(gameType) +
                " ServerName=" + Normalize(serverName) +
                " Address=" + Normalize(serverAddress) +
                " Port=" + serverPort +
                " SessionKey=" + sessionKey +
                " PeerIndex=" + peerIndex +
                " Source=" + Normalize(source) + ".");
        }

        private static bool IsPhaseActive_NoLock(ListedShellClientSessionPhase expectedPhase)
        {
            if (_phase != expectedPhase)
                return false;

            if (_phase == ListedShellClientSessionPhase.WrapperStart &&
                (_armedUtc == DateTime.MinValue ||
                 DateTime.UtcNow - _armedUtc > WrapperStartOwnershipLifetime))
            {
                Clear_NoLock();
                return false;
            }

            return string.Equals(_gameType, CoopGameModeIds.OfficialTeamDeathmatch, StringComparison.Ordinal);
        }

        private static void Clear_NoLock()
        {
            _phase = ListedShellClientSessionPhase.None;
            _gameType = string.Empty;
            _serverName = string.Empty;
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
