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
                        " ElapsedSeconds=" + (nowUtc - _pendingSinceUtc).TotalSeconds.ToString("0.000");
                    ClearNoLock("timeout");
                    return false;
                }

                if (mission == null)
                {
                    details =
                        "Pending=true PendingScene=" + _pendingSceneName +
                        " Mission=null";
                    return true;
                }

                string missionSceneName = Normalize(mission.SceneName);
                Mission.State missionState = mission.CurrentState;
                if (SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(missionSceneName) &&
                    missionState == Mission.State.Continuing)
                {
                    details =
                        "PendingResolved=true PendingScene=" + _pendingSceneName +
                        " MissionScene=" + missionSceneName +
                        " MissionState=" + missionState;
                    return false;
                }

                details =
                    "Pending=true PendingScene=" + _pendingSceneName +
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

        public static bool TryBindAuthoritativeTransportToken(Mission mission, int transportToken, string source)
        {
            if (mission == null || transportToken <= 0)
                return false;

            string missionSceneName = Normalize(mission.SceneName);
            if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(missionSceneName))
                return false;

            lock (Sync)
            {
                if (_activeTransportToken == transportToken &&
                    (ReferenceEquals(_activeMission, mission) ||
                     string.Equals(_activeSceneName, missionSceneName, StringComparison.Ordinal)))
                {
                    return true;
                }

                if (!_pending &&
                    (_activeTransportToken <= 0 ||
                     !string.Equals(_activeSceneName, missionSceneName, StringComparison.Ordinal)))
                {
                    return false;
                }

                if (_pending &&
                    !string.Equals(_pendingSceneName, missionSceneName, StringComparison.Ordinal))
                {
                    return false;
                }

                _pending = false;
                _pendingSceneName = string.Empty;
                _pendingSinceUtc = DateTime.MinValue;
                _activeMission = mission;
                _activeTransportToken = transportToken;
                _activeSceneName = missionSceneName;
            }

            ModLogger.Info(
                "PendingBattleMissionStartupState: bound authoritative pending battle transport token. " +
                "Scene=" + missionSceneName +
                " Token=" + transportToken +
                " Source=" + Normalize(source) + ".");
            return true;
        }

        private static void ClearNoLock(string source)
        {
            if (!_pending &&
                string.IsNullOrEmpty(_pendingSceneName) &&
                _pendingSinceUtc == DateTime.MinValue &&
                _activeMission == null &&
                _activeTransportToken == 0 &&
                string.IsNullOrEmpty(_activeSceneName))
            {
                return;
            }

            ModLogger.Info(
                "PendingBattleMissionStartupState: cleared pending battle mission startup. " +
                "PendingScene=" + _pendingSceneName +
                " ActiveScene=" + _activeSceneName +
                " ActiveToken=" + _activeTransportToken +
                " Source=" + Normalize(source) + ".");

            _pending = false;
            _pendingSceneName = string.Empty;
            _pendingSinceUtc = DateTime.MinValue;
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
