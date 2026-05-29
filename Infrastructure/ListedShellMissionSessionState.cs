using System;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    internal static class ListedShellMissionSessionState
    {
        private static readonly object Sync = new object();
        private static readonly TimeSpan PendingMissionBindTimeout = TimeSpan.FromSeconds(15);

        private static int _nextToken = 1;
        private static int _pendingServerToken;
        private static string _pendingServerScene = string.Empty;
        private static DateTime _pendingServerSinceUtc = DateTime.MinValue;

        private static Mission _activeMission;
        private static int _activeToken;
        private static string _activeScene = string.Empty;

        public static int ArmServerStartup(string sceneName, string source)
        {
            string normalizedScene = Normalize(sceneName);
            if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(normalizedScene))
                return 0;

            lock (Sync)
            {
                int token = _nextToken++;
                if (_nextToken == int.MaxValue)
                    _nextToken = 1;

                _pendingServerToken = token;
                _pendingServerScene = normalizedScene;
                _pendingServerSinceUtc = DateTime.UtcNow;
                _activeMission = null;
                _activeToken = 0;
                _activeScene = string.Empty;

                ModLogger.Info(
                    "ListedShellMissionSessionState: armed listed mission session token for server startup. " +
                    "Scene=" + normalizedScene +
                    " Token=" + token +
                    " Source=" + Normalize(source) + ".");
                return token;
            }
        }

        public static void AdoptRemoteMissionToken(string sceneName, int token, string source)
        {
            string normalizedScene = Normalize(sceneName);
            if (token <= 0)
                return;

            lock (Sync)
            {
                _pendingServerToken = 0;
                _pendingServerScene = string.Empty;
                _pendingServerSinceUtc = DateTime.MinValue;
                _activeMission = null;
                _activeToken = token;
                _activeScene = normalizedScene;
            }

            ModLogger.Info(
                "ListedShellMissionSessionState: adopted remote listed mission session token. " +
                "Scene=" + normalizedScene +
                " Token=" + token +
                " Source=" + Normalize(source) + ".");
        }

        public static void InitializeMission(Mission mission, string source)
        {
            if (mission == null)
                return;

            string missionScene = Normalize(mission.SceneName);
            lock (Sync)
            {
                if (_pendingServerToken > 0 &&
                    string.Equals(_pendingServerScene, missionScene, StringComparison.Ordinal))
                {
                    _activeMission = mission;
                    _activeToken = _pendingServerToken;
                    _activeScene = _pendingServerScene;
                    _pendingServerToken = 0;
                    _pendingServerScene = string.Empty;
                    _pendingServerSinceUtc = DateTime.MinValue;

                    ModLogger.Info(
                        "ListedShellMissionSessionState: activated pending listed mission session token on mission initialize. " +
                        "Scene=" + missionScene +
                        " Token=" + _activeToken +
                        " Source=" + Normalize(source) + ".");
                    return;
                }

                if (_activeToken > 0 &&
                    _activeMission == null &&
                    string.Equals(_activeScene, missionScene, StringComparison.Ordinal))
                {
                    _activeMission = mission;
                    ModLogger.Info(
                        "ListedShellMissionSessionState: bound existing listed mission session token to initialized mission. " +
                        "Scene=" + missionScene +
                        " Token=" + _activeToken +
                        " Source=" + Normalize(source) + ".");
                }
            }
        }

        public static bool ShouldDelayServerFinishedLoadingValidation(Mission mission, out string details)
        {
            lock (Sync)
            {
                if (_pendingServerToken <= 0)
                {
                    details = "PendingListedToken=false";
                    return false;
                }

                DateTime nowUtc = DateTime.UtcNow;
                if (_pendingServerSinceUtc != DateTime.MinValue &&
                    nowUtc - _pendingServerSinceUtc > PendingMissionBindTimeout)
                {
                    details =
                        "PendingListedTokenTimeout=true" +
                        " PendingScene=" + _pendingServerScene +
                        " PendingToken=" + _pendingServerToken;
                    _pendingServerToken = 0;
                    _pendingServerScene = string.Empty;
                    _pendingServerSinceUtc = DateTime.MinValue;
                    return false;
                }

                if (mission == null)
                {
                    details =
                        "PendingListedToken=true" +
                        " PendingScene=" + _pendingServerScene +
                        " PendingToken=" + _pendingServerToken +
                        " Mission=null";
                    return true;
                }

                string missionScene = Normalize(mission.SceneName);
                if (string.Equals(_pendingServerScene, missionScene, StringComparison.Ordinal) &&
                    mission.CurrentState == Mission.State.Continuing)
                {
                    _activeMission = mission;
                    _activeToken = _pendingServerToken;
                    _activeScene = _pendingServerScene;
                    _pendingServerToken = 0;
                    _pendingServerScene = string.Empty;
                    _pendingServerSinceUtc = DateTime.MinValue;
                    details =
                        "PendingListedTokenResolved=true" +
                        " MissionScene=" + missionScene +
                        " Token=" + _activeToken;
                    return false;
                }

                details =
                    "PendingListedToken=true" +
                    " PendingScene=" + _pendingServerScene +
                    " PendingToken=" + _pendingServerToken +
                    " MissionScene=" + missionScene +
                    " MissionState=" + mission.CurrentState;
                return true;
            }
        }

        public static bool TryResolveAuthoritativeToken(Mission mission, out int token)
        {
            lock (Sync)
            {
                if (_activeToken > 0 &&
                    (ReferenceEquals(_activeMission, mission) ||
                     (mission != null && string.Equals(_activeScene, Normalize(mission.SceneName), StringComparison.Ordinal))))
                {
                    token = _activeToken;
                    return true;
                }

                token = 0;
                return false;
            }
        }

        public static bool ShouldOwnServerFinishedLoadingValidation(Mission mission)
        {
            lock (Sync)
            {
                if (_pendingServerToken > 0)
                    return true;

                if (_activeToken <= 0)
                    return false;

                if (ReferenceEquals(_activeMission, mission))
                    return true;

                if (mission != null &&
                    string.Equals(_activeScene, Normalize(mission.SceneName), StringComparison.Ordinal))
                {
                    return true;
                }

                return false;
            }
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
