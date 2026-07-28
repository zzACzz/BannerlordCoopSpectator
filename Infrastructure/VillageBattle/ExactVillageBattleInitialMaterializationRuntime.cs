using System;
using CoopSpectator.Network.Messages;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure.VillageBattle
{
    /// <summary>
    /// Owns only the village-battle client startup pacing state.
    /// FieldBattle and SallyOut keep their existing independent state.
    /// </summary>
    internal static class ExactVillageBattleInitialMaterializationRuntime
    {
        private static readonly object Sync = new object();
        private static readonly TimeSpan ReplayInterval = TimeSpan.FromMilliseconds(16);
        private static readonly TimeSpan ReplayTimeBudgetValue = TimeSpan.FromMilliseconds(2);
        private static readonly TimeSpan AdaptiveSlowFrameGap = TimeSpan.FromMilliseconds(35);
        private const int MinReplayGroupsPerTickValue = 1;
        private const int MaxReplayGroupsPerTickValue = 3;
        private const int StableTicksBeforeIncrease = 12;

        private static Mission _activeMission;
        private static DateTime _nextReplayUtc = DateTime.MinValue;
        private static DateTime _lastReplayTickUtc = DateTime.MinValue;
        private static int _adaptiveReplayGroupLimit = MinReplayGroupsPerTickValue;
        private static int _adaptiveStableTickCount;

        public static TimeSpan ReplayTimeBudget => ReplayTimeBudgetValue;
        public static int MinReplayGroupsPerTick => MinReplayGroupsPerTickValue;
        public static int MaxReplayGroupsPerTick => MaxReplayGroupsPerTickValue;

        public static bool IsValidatedScenario(Mission mission, out string diagnostics)
        {
            diagnostics = "village-battle-initial-materialization-disabled";
            if (!ExperimentalFeatures.EnableExactVillageBattleInitialMaterializationRuntime)
                return false;

            if (mission == null)
            {
                diagnostics = "mission-null";
                return false;
            }

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetScenarioContext() ??
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext ??
                CoopPreMissionTopologyRuntimeState.GetActiveScenarioContext();
            return ExactVillageBattleScenarioContract.IsValidatedScenario(
                scenarioContext,
                mission.SceneName ?? string.Empty,
                out diagnostics);
        }

        public static bool ShouldPaceInitialClientCreateAgent(
            Mission mission,
            bool materializedMapApplied,
            string materializedMapReadinessSummary,
            out string reason)
        {
            reason = "not-village-battle-client-startup";
            if (!GameNetwork.IsClient || GameNetwork.IsServer || mission == null)
                return false;

            if (CoopBattlePhaseRuntimeState.GetPhase() >= CoopBattlePhase.BattleActive)
            {
                reason = "village-battle-already-active";
                return false;
            }

            if (!IsValidatedScenario(mission, out string scenarioDiagnostics))
            {
                reason = "village-battle-scenario-not-validated:" +
                    (scenarioDiagnostics ?? "unknown");
                return false;
            }

            if (materializedMapApplied)
            {
                reason = "authoritative-materialized-agent-map-already-applied";
                return false;
            }

            EnsureMissionState(mission);
            reason =
                "validated-village-battle-initial-materialization MaterializedMap={" +
                (materializedMapReadinessSummary ?? "pending") + "}";
            return true;
        }

        public static bool TryPrepareAdaptiveReplay(
            Mission mission,
            DateTime nowUtc,
            out int groupLimit,
            out TimeSpan replayTickGap)
        {
            groupLimit = MaxReplayGroupsPerTickValue;
            replayTickGap = TimeSpan.Zero;
            if (!GameNetwork.IsClient || GameNetwork.IsServer ||
                !IsValidatedScenario(mission, out _) ||
                CoopBattlePhaseRuntimeState.GetPhase() >= CoopBattlePhase.BattleActive)
            {
                return false;
            }

            lock (Sync)
            {
                EnsureMissionStateLocked(mission);
                replayTickGap = _lastReplayTickUtc == DateTime.MinValue
                    ? TimeSpan.Zero
                    : nowUtc - _lastReplayTickUtc;
                _lastReplayTickUtc = nowUtc;

                if (replayTickGap > AdaptiveSlowFrameGap)
                {
                    _adaptiveReplayGroupLimit = MinReplayGroupsPerTickValue;
                    _adaptiveStableTickCount = 0;
                }

                groupLimit = Math.Max(
                    MinReplayGroupsPerTickValue,
                    Math.Min(MaxReplayGroupsPerTickValue, _adaptiveReplayGroupLimit));
                return nowUtc >= _nextReplayUtc;
            }
        }

        public static void MarkReplayGroupStarted(Mission mission, DateTime replayStartUtc)
        {
            if (mission == null)
                return;

            lock (Sync)
            {
                EnsureMissionStateLocked(mission);
                _nextReplayUtc = replayStartUtc + ReplayInterval;
            }
        }

        public static void ObserveAdaptiveReplay(
            Mission mission,
            TimeSpan elapsed,
            TimeSpan replayTickGap,
            int admittedPacedGroupCount,
            int selectedBundleCount,
            string source)
        {
            if (mission == null || selectedBundleCount <= 0)
                return;

            int previousLimit;
            int currentLimit;
            lock (Sync)
            {
                EnsureMissionStateLocked(mission);
                previousLimit = _adaptiveReplayGroupLimit;
                bool exceededTimeBudget = elapsed >= ReplayTimeBudgetValue;
                bool slowFrameGap = replayTickGap > AdaptiveSlowFrameGap;
                if (exceededTimeBudget || slowFrameGap)
                {
                    _adaptiveReplayGroupLimit = MinReplayGroupsPerTickValue;
                    _adaptiveStableTickCount = 0;
                }
                else
                {
                    bool inexpensiveReplay =
                        admittedPacedGroupCount > 0 &&
                        elapsed.TotalMilliseconds <= 0.85d;
                    if (inexpensiveReplay)
                    {
                        _adaptiveStableTickCount++;
                        if (_adaptiveStableTickCount >= StableTicksBeforeIncrease)
                        {
                            _adaptiveReplayGroupLimit = Math.Min(
                                MaxReplayGroupsPerTickValue,
                                _adaptiveReplayGroupLimit + 1);
                            _adaptiveStableTickCount = 0;
                        }
                    }
                    else
                    {
                        _adaptiveStableTickCount = 0;
                    }
                }

                currentLimit = _adaptiveReplayGroupLimit;
            }

            if (previousLimit != currentLimit &&
                ExperimentalFeatures.EnableExactBattleAgentContractDiagnostics)
            {
                ModLogger.Info(
                    "ExactVillageBattleInitialMaterializationRuntime: adjusted adaptive client replay pacing. " +
                    "PreviousGroupLimit=" + previousLimit +
                    " CurrentGroupLimit=" + currentLimit +
                    " ElapsedMilliseconds=" + elapsed.TotalMilliseconds.ToString(
                        "F2",
                        System.Globalization.CultureInfo.InvariantCulture) +
                    " ReplayTickGapMilliseconds=" + replayTickGap.TotalMilliseconds.ToString(
                        "F2",
                        System.Globalization.CultureInfo.InvariantCulture) +
                    " AdmittedGroups=" + admittedPacedGroupCount +
                    " SelectedBundles=" + selectedBundleCount +
                    " Source=" + (source ?? "unknown"));
            }
        }

        public static void Reset(string source)
        {
            lock (Sync)
            {
                _activeMission = null;
                _nextReplayUtc = DateTime.MinValue;
                _lastReplayTickUtc = DateTime.MinValue;
                _adaptiveReplayGroupLimit = MinReplayGroupsPerTickValue;
                _adaptiveStableTickCount = 0;
            }
        }

        private static void EnsureMissionState(Mission mission)
        {
            lock (Sync)
            {
                EnsureMissionStateLocked(mission);
            }
        }

        private static void EnsureMissionStateLocked(Mission mission)
        {
            if (ReferenceEquals(_activeMission, mission))
                return;

            _activeMission = mission;
            _nextReplayUtc = DateTime.MinValue;
            _lastReplayTickUtc = DateTime.MinValue;
            _adaptiveReplayGroupLimit = MinReplayGroupsPerTickValue;
            _adaptiveStableTickCount = 0;
        }
    }
}
