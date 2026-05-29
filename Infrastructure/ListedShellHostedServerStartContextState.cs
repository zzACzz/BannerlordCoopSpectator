using System;

namespace CoopSpectator.Infrastructure
{
    internal static class ListedShellHostedServerStartContextState
    {
        private static readonly object Sync = new object();
        private static readonly TimeSpan ContextLifetime = TimeSpan.FromMinutes(2);

        private static string _gameType = string.Empty;
        private static string _scene = string.Empty;
        private static bool _isInGame;
        private static DateTime _armedUtc = DateTime.MinValue;

        public static void Arm(string gameType, string scene, bool isInGame, string source)
        {
            lock (Sync)
            {
                _gameType = Normalize(gameType);
                _scene = Normalize(scene);
                _isInGame = isInGame;
                _armedUtc = DateTime.UtcNow;
            }

            ModLogger.Info(
                "ListedShellHostedServerStartContextState: armed hosted listed server start context. " +
                "GameType=" + Normalize(gameType) +
                " Scene=" + Normalize(scene) +
                " IsInGame=" + isInGame +
                " Source=" + Normalize(source) + ".");
        }

        public static bool TryResolve(out string gameType, out string scene, out bool isInGame)
        {
            lock (Sync)
            {
                if (!IsActive_NoLock())
                {
                    gameType = string.Empty;
                    scene = string.Empty;
                    isInGame = false;
                    return false;
                }

                gameType = _gameType;
                scene = _scene;
                isInGame = _isInGame;
                return string.Equals(gameType, CoopGameModeIds.OfficialTeamDeathmatch, StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(scene);
            }
        }

        public static void Disarm(string source)
        {
            string gameType;
            string scene;
            bool isInGame;
            bool hadState;

            lock (Sync)
            {
                hadState = !string.IsNullOrEmpty(_gameType) ||
                    !string.IsNullOrEmpty(_scene) ||
                    _isInGame ||
                    _armedUtc != DateTime.MinValue;
                gameType = _gameType;
                scene = _scene;
                isInGame = _isInGame;
                Clear_NoLock();
            }

            if (!hadState)
                return;

            ModLogger.Info(
                "ListedShellHostedServerStartContextState: disarmed hosted listed server start context. " +
                "GameType=" + Normalize(gameType) +
                " Scene=" + Normalize(scene) +
                " IsInGame=" + isInGame +
                " Source=" + Normalize(source) + ".");
        }

        private static bool IsActive_NoLock()
        {
            if (_armedUtc == DateTime.MinValue ||
                DateTime.UtcNow - _armedUtc > ContextLifetime)
            {
                Clear_NoLock();
                return false;
            }

            return true;
        }

        private static void Clear_NoLock()
        {
            _gameType = string.Empty;
            _scene = string.Empty;
            _isInGame = false;
            _armedUtc = DateTime.MinValue;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
