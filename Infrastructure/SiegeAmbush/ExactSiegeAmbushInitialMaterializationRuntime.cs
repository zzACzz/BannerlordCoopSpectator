using System;
using CoopSpectator.Network.Messages;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure.SiegeAmbush
{
    /// <summary>
    /// Owns only the exact siege-engine ambush client startup pacing state.
    /// The server remains the only army spawner. Unlike external siege, ambush
    /// armies may contain rider/mount dependencies, so replay groups preserve them.
    /// </summary>
    internal static class ExactSiegeAmbushInitialMaterializationRuntime
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
        private static bool _initialClientMaterializationComplete;

        public static TimeSpan ReplayTimeBudget => ReplayTimeBudgetValue;
        public static int MinReplayGroupsPerTick => MinReplayGroupsPerTickValue;
        public static int MaxReplayGroupsPerTick => MaxReplayGroupsPerTickValue;

        public static bool IsInitialClientMaterializationComplete(Mission mission)
        {
            if (mission == null)
                return false;

            lock (Sync)
            {
                EnsureMissionStateLocked(mission);
                return _initialClientMaterializationComplete;
            }
        }

        public static void MarkInitialClientMaterializationComplete(Mission mission)
        {
            if (!GameNetwork.IsClient || GameNetwork.IsServer || mission == null)
                return;

            lock (Sync)
            {
                EnsureMissionStateLocked(mission);
                _initialClientMaterializationComplete = true;
            }
        }

        public static bool IsValidatedScenario(Mission mission, out string diagnostics)
        {
            diagnostics = "siege-ambush-initial-materialization-disabled";
            if (!ExperimentalFeatures.EnableExactSiegeAmbushInitialMaterializationRuntime)
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
            return SiegeAmbushScenarioContract.IsValidatedScenario(
                scenarioContext,
                mission.SceneName ?? string.Empty,
                out diagnostics);
        }

        public static bool ShouldPaceInitialClientCreateAgent(
            Mission mission,
            out string reason)
        {
            reason = "not-siege-ambush-client-startup";
            if (!GameNetwork.IsClient || GameNetwork.IsServer || mission == null)
                return false;

            if (CoopBattlePhaseRuntimeState.GetPhase() >= CoopBattlePhase.BattleActive)
            {
                reason = "siege-ambush-already-active";
                return false;
            }

            if (!IsValidatedScenario(mission, out string scenarioDiagnostics))
            {
                reason = "siege-ambush-scenario-not-validated:" +
                    (scenarioDiagnostics ?? "unknown");
                return false;
            }

            if (IsInitialClientMaterializationComplete(mission))
            {
                reason = "siege-ambush-initial-materialization-complete";
                return false;
            }

            reason = "validated-siege-ambush-initial-native-materialization";
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
                if (_initialClientMaterializationComplete)
                    return false;

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
                    "ExactSiegeAmbushInitialMaterializationRuntime: adjusted adaptive client replay pacing. " +
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
                _initialClientMaterializationComplete = false;
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
            _initialClientMaterializationComplete = false;
        }
    }
}
