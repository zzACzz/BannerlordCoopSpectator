using System;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    internal static class PendingBattleMissionStartupState
    {
        private static readonly object Sync = new object();
        private static readonly TimeSpan PendingTimeout = TimeSpan.FromSeconds(15);

        private static bool _pending;
        private static string _pendingSceneName = string.Empty;
        private static DateTime _pendingSinceUtc = DateTime.MinValue;
        private static int _pendingTransportToken;
        private static Mission _activeMission;
        private static int _activeTransportToken;
        private static string _activeSceneName = string.Empty;

        public static void Arm(string sceneName, string source)
        {
            string normalizedSceneName = Normalize(sceneName);
            if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(normalizedSceneName))
                return;

            lock (Sync)
            {
                _pending = true;
                _pendingSceneName = normalizedSceneName;
                _pendingSinceUtc = DateTime.UtcNow;
                _pendingTransportToken = 0;
                _activeMission = null;
                _activeTransportToken = 0;
                _activeSceneName = string.Empty;
            }

            ModLogger.Info(
                "PendingBattleMissionStartupState: armed pending battle mission startup. " +
                "Scene=" + normalizedSceneName +
                " Source=" + Normalize(source) + ".");
        }

        public static bool ShouldOwnServerFinishedLoadingValidation(Mission mission)
        {
            string missionSceneName = Normalize(mission?.SceneName);

            lock (Sync)
            {
                if (_pending)
                    return true;

                if (_activeTransportToken <= 0)
                    return false;

                if (ReferenceEquals(_activeMission, mission))
                    return true;

                if (!string.IsNullOrEmpty(missionSceneName) &&
                    string.Equals(_activeSceneName, missionSceneName, StringComparison.Ordinal))
                {
                    return true;
                }

                return false;
            }
        }

        public static bool ShouldDelayServerFinishedLoadingValidation(Mission mission, out string details)
        {
            lock (Sync)
            {
                if (!_pending)
                {
                    details = "Pending=false";
                    return false;
                }

                DateTime nowUtc = DateTime.UtcNow;
                if (_pendingSinceUtc != DateTime.MinValue && nowUtc - _pendingSinceUtc > PendingTimeout)
                {
                    details =
                        "PendingTimeout=true PendingScene=" + _pendingSceneName +
                        " PendingToken=" + _pendingTransportToken +
                        " ElapsedSeconds=" + (nowUtc - _pendingSinceUtc).TotalSeconds.ToString("0.000");
                    ClearNoLock("timeout");
                    return false;
                }

                if (mission == null)
                {
                    details =
                        "Pending=true PendingScene=" + _pendingSceneName +
                        " PendingToken=" + _pendingTransportToken +
                        " Mission=null";
                    return true;
                }

                string missionSceneName = Normalize(mission.SceneName);
                Mission.State missionState = mission.CurrentState;
                if (SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(missionSceneName) &&
                    missionState == Mission.State.Continuing)
                {
                    if (_pendingTransportToken <= 0)
                    {
                        details =
                            "PendingMissionReadyAwaitingTransportToken=true PendingScene=" + _pendingSceneName +
                            " MissionScene=" + missionSceneName +
                            " MissionState=" + missionState;
                        return true;
                    }

                    string pendingSceneName = _pendingSceneName;
                    _activeMission = mission;
                    _activeTransportToken = _pendingTransportToken;
                    _activeSceneName = _pendingSceneName;
                    _pending = false;
                    _pendingSceneName = string.Empty;
                    _pendingSinceUtc = DateTime.MinValue;
                    _pendingTransportToken = 0;

                    details =
                        "PendingResolved=true PendingScene=" + pendingSceneName +
                        " MissionScene=" + missionSceneName +
                        " MissionState=" + missionState +
                        " Token=" + _activeTransportToken;
                    return false;
                }

                details =
                    "Pending=true PendingScene=" + _pendingSceneName +
                    " PendingToken=" + _pendingTransportToken +
                    " MissionScene=" + missionSceneName +
                    " MissionState=" + missionState;
                return true;
            }
        }

        public static void Clear(string source)
        {
            lock (Sync)
            {
                ClearNoLock(source);
            }
        }

        public static bool TryResolveAuthoritativeTransportToken(Mission mission, out int token)
        {
            string missionSceneName = Normalize(mission?.SceneName);

            lock (Sync)
            {
                if (_activeTransportToken > 0 &&
                    (ReferenceEquals(_activeMission, mission) ||
                     (!string.IsNullOrEmpty(missionSceneName) &&
                      string.Equals(_activeSceneName, missionSceneName, StringComparison.Ordinal))))
                {
                    token = _activeTransportToken;
                    return true;
                }

                token = 0;
                return false;
            }
        }

        public static bool TryCapturePendingTransportToken(string sceneName, int transportToken, string source)
        {
            if (transportToken <= 0)
                return false;

            string normalizedSceneName = Normalize(sceneName);
            if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(normalizedSceneName))
                return false;

            lock (Sync)
            {
                if (!_pending ||
                    !string.Equals(_pendingSceneName, normalizedSceneName, StringComparison.Ordinal))
                {
                    return false;
                }

                if (_pendingTransportToken == transportToken)
                    return true;

                _pendingTransportToken = transportToken;
            }

            ModLogger.Info(
                "PendingBattleMissionStartupState: captured pending battle transport token from native LoadMission send. " +
                "Scene=" + normalizedSceneName +
                " Token=" + transportToken +
                " Source=" + Normalize(source) + ".");
            return true;
        }

        private static void ClearNoLock(string source)
        {
            if (!_pending &&
                string.IsNullOrEmpty(_pendingSceneName) &&
                _pendingSinceUtc == DateTime.MinValue &&
                _pendingTransportToken == 0 &&
                _activeMission == null &&
                _activeTransportToken == 0 &&
                string.IsNullOrEmpty(_activeSceneName))
            {
                return;
            }

            ModLogger.Info(
                "PendingBattleMissionStartupState: cleared pending battle mission startup. " +
                "PendingScene=" + _pendingSceneName +
                " PendingToken=" + _pendingTransportToken +
                " ActiveScene=" + _activeSceneName +
                " ActiveToken=" + _activeTransportToken +
                " Source=" + Normalize(source) + ".");

            _pending = false;
            _pendingSceneName = string.Empty;
            _pendingSinceUtc = DateTime.MinValue;
            _pendingTransportToken = 0;
            _activeMission = null;
            _activeTransportToken = 0;
            _activeSceneName = string.Empty;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
