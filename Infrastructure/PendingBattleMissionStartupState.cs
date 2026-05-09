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
            }

            ModLogger.Info(
                "PendingBattleMissionStartupState: armed pending battle mission startup. " +
                "Scene=" + normalizedSceneName +
                " Source=" + Normalize(source) + ".");
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
                    ClearNoLock("mission-ready");
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

        private static void ClearNoLock(string source)
        {
            if (!_pending &&
                string.IsNullOrEmpty(_pendingSceneName) &&
                _pendingSinceUtc == DateTime.MinValue)
            {
                return;
            }

            ModLogger.Info(
                "PendingBattleMissionStartupState: cleared pending battle mission startup. " +
                "PendingScene=" + _pendingSceneName +
                " Source=" + Normalize(source) + ".");

            _pending = false;
            _pendingSceneName = string.Empty;
            _pendingSinceUtc = DateTime.MinValue;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
