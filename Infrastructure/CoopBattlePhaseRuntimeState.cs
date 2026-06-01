using System;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    public enum CoopBattlePhase
    {
        None = 0,
        Loading = 10,
        SideSelection = 20,
        UnitSelection = 30,
        Deployment = 40,
        PreBattleHold = 50,
        BattleActive = 60,
        BattleEnded = 70
    }

    public sealed class CoopBattlePhaseStateSnapshot
    {
        public CoopBattlePhase Phase { get; set; }
        public string Source { get; set; }
        public string MissionName { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    public static class CoopBattlePhaseRuntimeState
    {
        private static readonly object Sync = new object();
        private static readonly TimeSpan ClientBridgePhasePollInterval = TimeSpan.FromMilliseconds(50);
        private static CoopBattlePhaseStateSnapshot _current = new CoopBattlePhaseStateSnapshot
        {
            Phase = CoopBattlePhase.None,
            Source = "uninitialized",
            MissionName = string.Empty,
            UpdatedUtc = DateTime.MinValue
        };
        private static DateTime _lastClientBridgePhasePollUtc = DateTime.MinValue;
        private static string _lastClientBridgePhaseSnapshotKey = string.Empty;

        public static CoopBattlePhaseStateSnapshot GetCurrent()
        {
            TryRefreshClientObservedPhaseFromBridge();
            lock (Sync)
            {
                return new CoopBattlePhaseStateSnapshot
                {
                    Phase = _current.Phase,
                    Source = _current.Source,
                    MissionName = _current.MissionName,
                    UpdatedUtc = _current.UpdatedUtc
                };
            }
        }

        public static CoopBattlePhase GetPhase()
        {
            TryRefreshClientObservedPhaseFromBridge();
            lock (Sync)
            {
                return _current.Phase;
            }
        }

        public static void StartMission(Mission mission, string source)
        {
            SetPhaseInternal(CoopBattlePhase.Loading, source, mission, allowRegression: true);
        }

        public static void AdvanceToAtLeast(CoopBattlePhase phase, string source, Mission mission = null)
        {
            SetPhaseInternal(phase, source, mission, allowRegression: false);
        }

        public static void SetPhase(CoopBattlePhase phase, string source, Mission mission = null, bool allowRegression = false)
        {
            SetPhaseInternal(phase, source, mission, allowRegression);
        }

        public static void Clear(string source)
        {
            lock (Sync)
            {
                _current = new CoopBattlePhaseStateSnapshot
                {
                    Phase = CoopBattlePhase.None,
                    Source = source ?? "cleared",
                    MissionName = string.Empty,
                    UpdatedUtc = DateTime.UtcNow
                };
            }

            if (ShouldWritePhaseBridgeStatus())
                CoopBattlePhaseBridgeFile.WriteStatus(GetCurrent());
            ModLogger.Info("CoopBattlePhaseRuntimeState: cleared. Source=" + (_current.Source ?? "unknown"));
        }

        private static void SetPhaseInternal(CoopBattlePhase phase, string source, Mission mission, bool allowRegression)
        {
            string missionName = ResolveMissionName(mission);
            lock (Sync)
            {
                if (!allowRegression && phase < _current.Phase)
                    return;

                bool samePhase = _current.Phase == phase;
                bool sameMission = string.Equals(_current.MissionName, missionName, StringComparison.Ordinal);
                if (samePhase && sameMission)
                    return;

                _current = new CoopBattlePhaseStateSnapshot
                {
                    Phase = phase,
                    Source = source ?? "unknown",
                    MissionName = missionName,
                    UpdatedUtc = DateTime.UtcNow
                };
            }

            if (ShouldWritePhaseBridgeStatus())
                CoopBattlePhaseBridgeFile.WriteStatus(GetCurrent());
            ModLogger.Info(
                "CoopBattlePhaseRuntimeState: phase updated. " +
                "Phase=" + phase +
                " Source=" + (source ?? "unknown") +
                " Mission=" + (missionName ?? "unknown"));
        }

        private static bool ShouldWritePhaseBridgeStatus()
        {
            return GameNetwork.IsServer || !GameNetwork.IsClientOrReplay;
        }

        private static void TryRefreshClientObservedPhaseFromBridge()
        {
            if (!GameNetwork.IsClientOrReplay || GameNetwork.IsServer)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            lock (Sync)
            {
                if (_lastClientBridgePhasePollUtc != DateTime.MinValue &&
                    (nowUtc - _lastClientBridgePhasePollUtc) < ClientBridgePhasePollInterval)
                {
                    return;
                }

                _lastClientBridgePhasePollUtc = nowUtc;
            }

            CoopBattlePhaseStateSnapshot bridgeSnapshot = CoopBattlePhaseBridgeFile.ReadStatus();
            if (bridgeSnapshot == null)
                return;

            string currentMissionName = ResolveMissionName(Mission.Current);
            if (!string.IsNullOrWhiteSpace(currentMissionName) &&
                !string.IsNullOrWhiteSpace(bridgeSnapshot.MissionName) &&
                !string.Equals(currentMissionName, bridgeSnapshot.MissionName, StringComparison.Ordinal))
            {
                return;
            }

            string snapshotKey =
                bridgeSnapshot.Phase + "|" +
                (bridgeSnapshot.Source ?? string.Empty) + "|" +
                (bridgeSnapshot.MissionName ?? string.Empty) + "|" +
                bridgeSnapshot.UpdatedUtc.ToString("O");

            lock (Sync)
            {
                if (string.Equals(_lastClientBridgePhaseSnapshotKey, snapshotKey, StringComparison.Ordinal))
                    return;

                bool samePhase = _current.Phase == bridgeSnapshot.Phase;
                bool sameMission = string.Equals(_current.MissionName, bridgeSnapshot.MissionName, StringComparison.Ordinal);
                bool sameSource = string.Equals(_current.Source, bridgeSnapshot.Source, StringComparison.Ordinal);
                bool sameTimestamp = _current.UpdatedUtc == bridgeSnapshot.UpdatedUtc;
                if (samePhase && sameMission && sameSource && sameTimestamp)
                {
                    _lastClientBridgePhaseSnapshotKey = snapshotKey;
                    return;
                }

                _current = new CoopBattlePhaseStateSnapshot
                {
                    Phase = bridgeSnapshot.Phase,
                    Source = !string.IsNullOrWhiteSpace(bridgeSnapshot.Source)
                        ? bridgeSnapshot.Source + " client-bridge"
                        : "client-bridge",
                    MissionName = bridgeSnapshot.MissionName ?? string.Empty,
                    UpdatedUtc = bridgeSnapshot.UpdatedUtc == DateTime.MinValue
                        ? nowUtc
                        : bridgeSnapshot.UpdatedUtc
                };
                _lastClientBridgePhaseSnapshotKey = snapshotKey;
            }

            ModLogger.Info(
                "CoopBattlePhaseRuntimeState: client observed authoritative battle phase. " +
                "Phase=" + bridgeSnapshot.Phase +
                " Source=" + (bridgeSnapshot.Source ?? "unknown") +
                " Mission=" + (bridgeSnapshot.MissionName ?? "unknown"));
        }

        private static string ResolveMissionName(Mission mission)
        {
            if (mission == null)
                return string.Empty;

            try
            {
            return mission.SceneName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
