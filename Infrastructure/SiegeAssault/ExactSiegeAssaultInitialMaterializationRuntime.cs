using System;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    /// <summary>
    /// Owns only the exact external-siege client startup pacing state.
    /// The server keeps full native ownership of initial armies and reinforcements.
    /// Siege cavalry is already projected to foot troops, so this runtime never
    /// introduces a rider/mount dependency.
    /// </summary>
    internal static class ExactSiegeAssaultInitialMaterializationRuntime
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
            diagnostics = "siege-assault-initial-materialization-disabled";
            if (!ExperimentalFeatures.EnableExactSiegeAssaultInitialMaterializationRuntime)
                return false;

            if (mission == null)
            {
                diagnostics = "mission-null";
                return false;
            }

            if (!ExactCampaignSiegeAssaultWithDeploymentRuntime
                    .ShouldUseFullNativeArmySpawnRuntime(mission))
            {
                diagnostics = "full-native-siege-army-runtime-not-active";
                return false;
            }

            diagnostics = "validated-exact-external-siege-full-native-army-runtime";
            return true;
        }

        public static bool ShouldPaceInitialClientCreateAgent(
            Mission mission,
            bool materializedMapApplied,
            string materializedMapReadinessSummary,
            out string reason)
        {
            reason = "not-siege-assault-client-startup";
            if (!GameNetwork.IsClient || GameNetwork.IsServer || mission == null)
                return false;

            if (CoopBattlePhaseRuntimeState.GetPhase() >= CoopBattlePhase.BattleActive)
            {
                reason = "siege-assault-already-active";
                return false;
            }

            if (!IsValidatedScenario(mission, out string scenarioDiagnostics))
            {
                reason = "siege-assault-scenario-not-validated:" +
                    (scenarioDiagnostics ?? "unknown");
                return false;
            }

            if (IsInitialClientMaterializationComplete(mission))
            {
                reason = "siege-assault-initial-materialization-complete";
                return false;
            }

            reason =
                "validated-siege-assault-initial-materialization MaterializedMapApplied=" +
                materializedMapApplied +
                " MaterializedMap={" +
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
                    "ExactSiegeAssaultInitialMaterializationRuntime: adjusted adaptive client replay pacing. " +
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
