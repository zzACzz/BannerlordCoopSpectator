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
        private static CoopBattlePhaseStateSnapshot _current = new CoopBattlePhaseStateSnapshot
        {
            Phase = CoopBattlePhase.None,
            Source = "uninitialized",
            MissionName = string.Empty,
            UpdatedUtc = DateTime.MinValue
        };

        public static CoopBattlePhaseStateSnapshot GetCurrent()
        {
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

            CoopBattlePhaseBridgeFile.WriteStatus(GetCurrent());
            ModLogger.Info(
                "CoopBattlePhaseRuntimeState: phase updated. " +
                "Phase=" + phase +
                " Source=" + (source ?? "unknown") +
                " Mission=" + (missionName ?? "unknown"));
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
