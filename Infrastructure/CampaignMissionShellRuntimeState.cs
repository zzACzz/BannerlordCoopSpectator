using System;

namespace CoopSpectator.Infrastructure
{
    internal static class CampaignMissionShellRuntimeState
    {
        private const string SiegeMissionWithDeployment = "SiegeMissionWithDeployment";
        private const string SiegeMissionNoDeployment = "SiegeMissionNoDeployment";
        private const string SiegeLordsHallFightMission = "SiegeLordsHallFightMission";

        private static readonly object Sync = new object();
        private static readonly TimeSpan CaptureTtl = TimeSpan.FromMinutes(2);

        private static string _missionShell = string.Empty;
        private static string _sceneName = string.Empty;
        private static string _source = string.Empty;
        private static DateTime _capturedUtc = DateTime.MinValue;

        public static void Capture(string missionName, string sceneName, string source)
        {
            string normalizedMissionName = Normalize(missionName);
            string normalizedSceneName = Normalize(sceneName);

            lock (Sync)
            {
                if (!IsKnownSiegeMissionShell(normalizedMissionName))
                {
                    if (!string.IsNullOrEmpty(_missionShell))
                        ClearNoLock(source ?? "capture-clear");
                    return;
                }

                _missionShell = normalizedMissionName;
                _sceneName = normalizedSceneName;
                _source = Normalize(source);
                _capturedUtc = DateTime.UtcNow;
            }

            ModLogger.Info(
                "CampaignMissionShellRuntimeState: captured siege mission shell. " +
                "MissionShell=" + normalizedMissionName +
                " Scene=" + normalizedSceneName +
                " Source=" + Normalize(source) + ".");
        }

        public static bool TryGetMissionShell(string sceneName, out string missionShell, out string diagnostics)
        {
            string normalizedSceneName = Normalize(sceneName);
            lock (Sync)
            {
                missionShell = string.Empty;
                if (string.IsNullOrEmpty(_missionShell))
                {
                    diagnostics = "MissionShell=empty";
                    return false;
                }

                DateTime nowUtc = DateTime.UtcNow;
                if (_capturedUtc == DateTime.MinValue || nowUtc - _capturedUtc > CaptureTtl)
                {
                    diagnostics =
                        "MissionShell=expired StoredShell=" + _missionShell +
                        " StoredScene=" + _sceneName;
                    ClearNoLock("expired");
                    return false;
                }

                if (!string.IsNullOrEmpty(normalizedSceneName) &&
                    !string.IsNullOrEmpty(_sceneName) &&
                    !string.Equals(normalizedSceneName, _sceneName, StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics =
                        "MissionShell=scene-mismatch RequestedScene=" + normalizedSceneName +
                        " StoredScene=" + _sceneName +
                        " StoredShell=" + _missionShell;
                    return false;
                }

                missionShell = _missionShell;
                diagnostics =
                    "MissionShell=" + missionShell +
                    " Scene=" + _sceneName +
                    " Source=" + _source;
                return true;
            }
        }

        public static bool IsKnownSiegeMissionShell(string missionShell)
        {
            return string.Equals(missionShell, SiegeMissionWithDeployment, StringComparison.Ordinal) ||
                   string.Equals(missionShell, SiegeMissionNoDeployment, StringComparison.Ordinal) ||
                   string.Equals(missionShell, SiegeLordsHallFightMission, StringComparison.Ordinal);
        }

        public static bool IsWithDeploymentMissionShell(string missionShell)
        {
            return string.Equals(missionShell, SiegeMissionWithDeployment, StringComparison.Ordinal);
        }

        public static bool IsNoDeploymentMissionShell(string missionShell)
        {
            return string.Equals(missionShell, SiegeMissionNoDeployment, StringComparison.Ordinal);
        }

        public static bool IsLordsHallMissionShell(string missionShell)
        {
            return string.Equals(missionShell, SiegeLordsHallFightMission, StringComparison.Ordinal);
        }

        private static void ClearNoLock(string source)
        {
            ModLogger.Info(
                "CampaignMissionShellRuntimeState: cleared captured siege mission shell. " +
                "MissionShell=" + _missionShell +
                " Scene=" + _sceneName +
                " Source=" + Normalize(source) + ".");

            _missionShell = string.Empty;
            _sceneName = string.Empty;
            _source = string.Empty;
            _capturedUtc = DateTime.MinValue;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
