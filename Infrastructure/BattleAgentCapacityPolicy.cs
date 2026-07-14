using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.DedicatedHelper;
using CoopSpectator.Network.Messages;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    public static class BattleAgentCapacityPolicy
    {
        private const int FallbackMaximumPhysicalAgentCount = 2040;
        private const float MaximumBattleSideRatio = 0.75f;
        private const float ImmediateCorpseFadeSeconds = 0.05f;
        private const float RiderlessMountGraceSeconds = 0.5f;
        private const float RiderlessMountScanIntervalSeconds = 0.25f;

        private static Mission _mission;
        private static string _battleIdentity = string.Empty;
        private static int _requestedBattleSize;
        private static int _resolvedBattleSize;
        private static int _maximumPhysicalAgentCount;
        private static bool _settingsFrozen;
        private static bool _removeCorpsesImmediately;
        private static bool _cullRiderlessMounts;
        private static bool _corpsePolicyApplied;
        private static bool _policySummaryLogged;
        private static float _nextCapacityBlockedLogMissionTime;
        private static float _nextRiderlessMountScanMissionTime;
        private static int _culledRiderlessMountCount;
        private static readonly Dictionary<Agent, float> RiderlessMountFirstSeenTimes =
            new Dictionary<Agent, float>();
        private static readonly HashSet<Agent> CulledRiderlessMounts = new HashSet<Agent>();
        private static readonly HashSet<Agent> CulledMountsPendingFade = new HashSet<Agent>();

        public static int ResolveBattleSize(Mission mission, string source)
        {
            if (mission == null)
                return 0;

            EnsureMission(mission);
            BattleRuntimeState runtimeState = BattleSnapshotRuntimeState.GetState();
            BattleSnapshotMessage snapshot = runtimeState?.Snapshot ?? BattleSnapshotRuntimeState.GetCurrent();
            if (runtimeState == null || snapshot == null)
                return 0;

            string battleIdentity = BuildBattleIdentity(snapshot, mission);
            if (!string.Equals(_battleIdentity, battleIdentity, StringComparison.Ordinal))
                ResetResolvedPolicyForBattleIdentity(battleIdentity);

            FreezeCleanupSettings(runtimeState);
            if (_resolvedBattleSize > 0)
            {
                ReapplyResolvedBudgetIfSnapshotWasRefreshed(runtimeState, source);
                return _resolvedBattleSize;
            }

            if (!GameNetwork.IsServer)
            {
                return runtimeState.ResolvedBattleSize > 0
                    ? runtimeState.ResolvedBattleSize
                    : Math.Max(0, runtimeState.BattleSizeBudget);
            }

            int requestedBattleSize = runtimeState.RequestedBattleSize > 0
                ? runtimeState.RequestedBattleSize
                : runtimeState.BattleSizeBudget;
            if (requestedBattleSize <= 0)
                return 0;

            requestedBattleSize = DedicatedServerLaunchSettings.ClampToAllowedBattleSize(requestedBattleSize);
            int maximumPhysicalAgentCount = GetMaximumPhysicalAgentCount();
            bool dismountedBattle = IsDismountedBattle(mission, runtimeState.ScenarioContext);
            CountHealthyTroopsByPhysicalCost(
                runtimeState,
                dismountedBattle,
                out int totalHealthyTroops,
                out int mountedHealthyTroops);

            int maximumTroopsByComposition;
            if (dismountedBattle)
            {
                maximumTroopsByComposition = maximumPhysicalAgentCount;
            }
            else if (totalHealthyTroops > 0)
            {
                int halfPhysicalCapacity = maximumPhysicalAgentCount / 2;
                maximumTroopsByComposition = mountedHealthyTroops >= halfPhysicalCapacity
                    ? halfPhysicalCapacity
                    : maximumPhysicalAgentCount - mountedHealthyTroops;
            }
            else
            {
                maximumTroopsByComposition = maximumPhysicalAgentCount / 2;
            }

            int resolvedBattleSize = Math.Min(requestedBattleSize, maximumTroopsByComposition);
            if (totalHealthyTroops > 0)
                resolvedBattleSize = Math.Min(resolvedBattleSize, totalHealthyTroops);
            resolvedBattleSize = Math.Max(1, resolvedBattleSize);

            _requestedBattleSize = requestedBattleSize;
            _resolvedBattleSize = resolvedBattleSize;
            _maximumPhysicalAgentCount = maximumPhysicalAgentCount;

            string resolutionSource =
                (runtimeState.BattleSizeBudgetSource ?? "unknown") +
                ";server-capacity=" +
                (dismountedBattle ? "dismounted" : "mounted-composition");
            BattleSnapshotRuntimeState.ApplyResolvedBattleSize(
                _requestedBattleSize,
                _resolvedBattleSize,
                resolutionSource);

            LogPolicySummaryOnce(
                mission,
                totalHealthyTroops,
                mountedHealthyTroops,
                maximumTroopsByComposition,
                dismountedBattle,
                source);
            return _resolvedBattleSize;
        }

        public static int GetResolvedBattleSize(Mission mission, string source)
        {
            int resolved = ResolveBattleSize(mission, source);
            if (resolved > 0)
                return resolved;

            BattleRuntimeState runtimeState = BattleSnapshotRuntimeState.GetState();
            return Math.Max(0, runtimeState?.BattleSizeBudget ?? 0);
        }

        public static int GetMaximumPhysicalAgentCount()
        {
            try
            {
                int nativeMaximum = DefaultBattleMissionAgentSpawnLogic.MaxNumberOfAgentsForMission;
                return nativeMaximum > 0 ? nativeMaximum : FallbackMaximumPhysicalAgentCount;
            }
            catch
            {
                return FallbackMaximumPhysicalAgentCount;
            }
        }

        public static int GetAvailablePhysicalAgentSlots(Mission mission)
        {
            if (mission == null)
                return 0;

            int allAgentCount = mission.AllAgents?.Count ?? 0;
            return Math.Max(0, GetMaximumPhysicalAgentCount() - allAgentCount);
        }

        public static bool CanSpawnPhysicalAgents(
            Mission mission,
            int requiredPhysicalAgentCount,
            string source)
        {
            if (mission == null || requiredPhysicalAgentCount <= 0)
                return false;

            int availableSlots = GetAvailablePhysicalAgentSlots(mission);
            if (availableSlots >= requiredPhysicalAgentCount)
                return true;

            if (CoopDebugConfig.PossessionDiagnostics &&
                mission.CurrentTime >= _nextCapacityBlockedLogMissionTime)
            {
                _nextCapacityBlockedLogMissionTime = mission.CurrentTime + 2f;
                ModLogger.Info(
                    "BattleAgentCapacityPolicy: spawn deferred by physical agent capacity. " +
                    "AllAgents=" + (mission.AllAgents?.Count ?? 0) +
                    " AvailableSlots=" + availableSlots +
                    " RequiredSlots=" + requiredPhysicalAgentCount +
                    " MaximumSlots=" + GetMaximumPhysicalAgentCount() +
                    " Source=" + (source ?? "unknown"));
            }

            return false;
        }

        public static int GetExpectedTroopPhysicalCost(
            Mission mission,
            BattleSideEnum side,
            RosterEntryState entryState,
            bool fallbackMounted)
        {
            if (mission == null)
                return fallbackMounted ? 2 : 1;

            BattleScenarioContextMessage scenarioContext =
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext ??
                BattleSnapshotRuntimeState.GetCurrent()?.ScenarioContext;
            if (IsDismountedBattle(mission, scenarioContext))
                return 1;

            if (ExactCampaignArmyBootstrap.TryGetSpawnHorses(mission, side, out bool spawnHorses) &&
                !spawnHorses)
            {
                return 1;
            }

            bool mounted =
                entryState?.IsMounted == true ||
                !string.IsNullOrWhiteSpace(entryState?.CombatHorseId) ||
                fallbackMounted;
            return mounted ? 2 : 1;
        }

        public static void AllocateInitialTroops(
            int defenderTotal,
            int attackerTotal,
            int battleSizeBudget,
            out int defenderInitial,
            out int attackerInitial)
        {
            defenderTotal = Math.Max(0, defenderTotal);
            attackerTotal = Math.Max(0, attackerTotal);
            int total = defenderTotal + attackerTotal;
            int target = Math.Min(Math.Max(0, battleSizeBudget), total);

            defenderInitial = 0;
            attackerInitial = 0;
            if (target <= 0 || total <= 0)
                return;

            if (defenderTotal <= 0)
            {
                attackerInitial = Math.Min(attackerTotal, target);
                return;
            }

            if (attackerTotal <= 0)
            {
                defenderInitial = Math.Min(defenderTotal, target);
                return;
            }

            double defenderShare = (double)defenderTotal / total;
            defenderShare = Math.Max(1d - MaximumBattleSideRatio, Math.Min(MaximumBattleSideRatio, defenderShare));
            double attackerShare = 1d - defenderShare;

            if (defenderShare <= attackerShare)
            {
                defenderInitial = Math.Min(defenderTotal, (int)Math.Ceiling(target * defenderShare));
                attackerInitial = Math.Min(attackerTotal, target - defenderInitial);
            }
            else
            {
                attackerInitial = Math.Min(attackerTotal, (int)Math.Ceiling(target * attackerShare));
                defenderInitial = Math.Min(defenderTotal, target - attackerInitial);
            }

            int remaining = target - defenderInitial - attackerInitial;
            if (remaining <= 0)
                return;

            int defenderRoom = Math.Max(0, defenderTotal - defenderInitial);
            int attackerRoom = Math.Max(0, attackerTotal - attackerInitial);
            bool attackerHasLargerReserve = attackerRoom >= defenderRoom;
            if (attackerHasLargerReserve)
            {
                int attackerExtra = Math.Min(remaining, attackerRoom);
                attackerInitial += attackerExtra;
                remaining -= attackerExtra;
                defenderInitial += Math.Min(remaining, defenderRoom);
            }
            else
            {
                int defenderExtra = Math.Min(remaining, defenderRoom);
                defenderInitial += defenderExtra;
                remaining -= defenderExtra;
                attackerInitial += Math.Min(remaining, attackerRoom);
            }
        }

        public static void Tick(Mission mission, string source)
        {
            if (mission == null || !GameNetwork.IsServer)
                return;

            EnsureMission(mission);
            ResolveBattleSize(mission, source);
            BattleRuntimeState runtimeState = BattleSnapshotRuntimeState.GetState();
            if (runtimeState == null)
                return;

            FreezeCleanupSettings(runtimeState);
            ApplyCorpsePolicyIfNeeded(mission, source);
            FadeCulledMountsAfterRemoval(source);

            if (!_cullRiderlessMounts)
                return;

            CoopBattlePhase phase = CoopBattlePhaseRuntimeState.GetPhase();
            if (phase < CoopBattlePhase.BattleActive || phase >= CoopBattlePhase.BattleEnded)
                return;

            if (mission.CurrentTime < _nextRiderlessMountScanMissionTime)
                return;

            _nextRiderlessMountScanMissionTime = mission.CurrentTime + RiderlessMountScanIntervalSeconds;

            CullStableRiderlessMounts(mission, source);
        }

        public static void OnAgentRemoved(Agent affectedAgent)
        {
            if (affectedAgent == null || !affectedAgent.IsMount)
                return;

            if (CulledRiderlessMounts.Contains(affectedAgent))
                CulledMountsPendingFade.Add(affectedAgent);

            RiderlessMountFirstSeenTimes.Remove(affectedAgent);
        }

        private static void EnsureMission(Mission mission)
        {
            if (ReferenceEquals(_mission, mission))
                return;

            _mission = mission;
            _battleIdentity = string.Empty;
            _requestedBattleSize = 0;
            _resolvedBattleSize = 0;
            _maximumPhysicalAgentCount = 0;
            _settingsFrozen = false;
            _removeCorpsesImmediately = false;
            _cullRiderlessMounts = false;
            _corpsePolicyApplied = false;
            _policySummaryLogged = false;
            _nextCapacityBlockedLogMissionTime = 0f;
            _nextRiderlessMountScanMissionTime = 0f;
            _culledRiderlessMountCount = 0;
            RiderlessMountFirstSeenTimes.Clear();
            CulledRiderlessMounts.Clear();
            CulledMountsPendingFade.Clear();
        }

        private static void ResetResolvedPolicyForBattleIdentity(string battleIdentity)
        {
            _battleIdentity = battleIdentity ?? string.Empty;
            _requestedBattleSize = 0;
            _resolvedBattleSize = 0;
            _maximumPhysicalAgentCount = 0;
            _settingsFrozen = false;
            _removeCorpsesImmediately = false;
            _cullRiderlessMounts = false;
            _corpsePolicyApplied = false;
            _policySummaryLogged = false;
            _nextRiderlessMountScanMissionTime = 0f;
            _culledRiderlessMountCount = 0;
            RiderlessMountFirstSeenTimes.Clear();
            CulledRiderlessMounts.Clear();
            CulledMountsPendingFade.Clear();
        }

        private static string BuildBattleIdentity(BattleSnapshotMessage snapshot, Mission mission)
        {
            string snapshotIdentity = !string.IsNullOrWhiteSpace(snapshot?.BattleInstanceId)
                ? snapshot.BattleInstanceId
                : snapshot?.BattleId;
            return (snapshotIdentity ?? "unknown") + "|" + (mission?.SceneName ?? "unknown");
        }

        private static void ReapplyResolvedBudgetIfSnapshotWasRefreshed(
            BattleRuntimeState runtimeState,
            string source)
        {
            if (runtimeState == null || _resolvedBattleSize <= 0)
                return;

            if (runtimeState.ResolvedBattleSize == _resolvedBattleSize &&
                runtimeState.BattleSizeBudget == _resolvedBattleSize)
            {
                return;
            }

            BattleSnapshotRuntimeState.ApplyResolvedBattleSize(
                _requestedBattleSize,
                _resolvedBattleSize,
                (runtimeState.BattleSizeBudgetSource ?? "unknown") + ";server-capacity=reapplied");

            if (CoopDebugConfig.PossessionDiagnostics)
            {
                ModLogger.Info(
                    "BattleAgentCapacityPolicy: reapplied stable resolved battle size after snapshot refresh. " +
                    "Requested=" + _requestedBattleSize +
                    " Resolved=" + _resolvedBattleSize +
                    " Source=" + (source ?? "unknown"));
            }
        }

        private static void FreezeCleanupSettings(BattleRuntimeState runtimeState)
        {
            if (_settingsFrozen || runtimeState?.Snapshot == null)
                return;

            _removeCorpsesImmediately = runtimeState.RemoveCorpsesImmediately;
            _cullRiderlessMounts = runtimeState.CullRiderlessMounts;
            _settingsFrozen = true;
        }

        private static bool IsDismountedBattle(
            Mission mission,
            BattleScenarioContextMessage scenarioContext)
        {
            if (scenarioContext?.IsSiegeBattle == true)
                return true;

            try
            {
                return mission?.IsSiegeBattle == true;
            }
            catch
            {
                return false;
            }
        }

        private static void CountHealthyTroopsByPhysicalCost(
            BattleRuntimeState runtimeState,
            bool dismountedBattle,
            out int totalHealthyTroops,
            out int mountedHealthyTroops)
        {
            totalHealthyTroops = 0;
            mountedHealthyTroops = 0;
            if (runtimeState?.EntriesById == null)
                return;

            foreach (RosterEntryState entryState in runtimeState.EntriesById.Values)
            {
                if (entryState == null)
                    continue;

                int healthyCount = Math.Max(0, entryState.Count - entryState.WoundedCount);
                if (healthyCount <= 0)
                    continue;

                totalHealthyTroops += healthyCount;
                if (!dismountedBattle &&
                    (entryState.IsMounted || !string.IsNullOrWhiteSpace(entryState.CombatHorseId)))
                {
                    mountedHealthyTroops += healthyCount;
                }
            }
        }

        private static void ApplyCorpsePolicyIfNeeded(Mission mission, string source)
        {
            if (!_removeCorpsesImmediately || _corpsePolicyApplied || mission == null)
                return;

            // Treat this as a one-shot mission policy application. A native API failure
            // is not expected to become recoverable on the next mission tick, and retrying
            // here would turn one operational warning into hot-path log spam.
            _corpsePolicyApplied = true;
            try
            {
                mission.SetOverrideCorpseCount(0);
                mission.SetMissionCorpseFadeOutTimeInSeconds(ImmediateCorpseFadeSeconds);
                ModLogger.Info(
                    "BattleAgentCapacityPolicy: immediate native corpse cleanup enabled. " +
                    "FadeSeconds=" + ImmediateCorpseFadeSeconds.ToString(
                        "0.##",
                        System.Globalization.CultureInfo.InvariantCulture) +
                    " Source=" + (source ?? "unknown"));
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "BattleAgentCapacityPolicy: failed to apply immediate corpse cleanup. " +
                    "Message=" + ex.Message +
                    " Source=" + (source ?? "unknown"));
            }
        }

        private static void CullStableRiderlessMounts(Mission mission, string source)
        {
            float missionTime = mission.CurrentTime;
            var activeRiderlessMounts = new HashSet<Agent>();
            try
            {
                foreach (KeyValuePair<Agent, MissionTime> riderlessMount in mission.MountsWithoutRiders.ToList())
                {
                    Agent mount = riderlessMount.Key;
                    if (mount == null ||
                        !mount.IsMount ||
                        !mount.IsActive() ||
                        mount.RiderAgent != null ||
                        CulledRiderlessMounts.Contains(mount))
                    {
                        continue;
                    }

                    activeRiderlessMounts.Add(mount);
                    if (!RiderlessMountFirstSeenTimes.TryGetValue(mount, out float firstSeenTime))
                    {
                        RiderlessMountFirstSeenTimes[mount] = missionTime;
                        continue;
                    }

                    if (missionTime - firstSeenTime < RiderlessMountGraceSeconds)
                        continue;

                    CulledRiderlessMounts.Add(mount);
                    try
                    {
                        mission.KillAgentCheat(mount);
                        _culledRiderlessMountCount++;
                        if (CoopDebugConfig.PossessionDiagnostics)
                        {
                            ModLogger.Info(
                                "BattleAgentCapacityPolicy: culled stable riderless mount. " +
                                "AgentIndex=" + mount.Index +
                                " CulledCount=" + _culledRiderlessMountCount +
                                " Source=" + (source ?? "unknown"));
                        }
                    }
                    catch (Exception ex)
                    {
                        if (mount.IsActive())
                            CulledRiderlessMounts.Remove(mount);
                        if (CoopDebugConfig.PossessionDiagnostics)
                        {
                            ModLogger.Info(
                                "BattleAgentCapacityPolicy: failed to cull riderless mount. " +
                                "AgentIndex=" + mount.Index +
                                " Message=" + ex.Message +
                                " Source=" + (source ?? "unknown"));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (CoopDebugConfig.PossessionDiagnostics)
                {
                    ModLogger.Info(
                        "BattleAgentCapacityPolicy: riderless mount scan failed open. " +
                        "Message=" + ex.Message +
                        " Source=" + (source ?? "unknown"));
                }
                return;
            }

            foreach (Agent trackedMount in RiderlessMountFirstSeenTimes.Keys.ToList())
            {
                if (!activeRiderlessMounts.Contains(trackedMount))
                    RiderlessMountFirstSeenTimes.Remove(trackedMount);
            }
        }

        private static void FadeCulledMountsAfterRemoval(string source)
        {
            foreach (Agent mount in CulledMountsPendingFade.ToList())
            {
                if (mount == null || mount.State == AgentState.Deleted)
                {
                    CulledMountsPendingFade.Remove(mount);
                    continue;
                }

                if (mount.IsActive())
                    continue;

                try
                {
                    mount.FadeOut(hideInstantly: true, hideMount: false);
                    CulledMountsPendingFade.Remove(mount);
                }
                catch (Exception ex)
                {
                    if (CoopDebugConfig.PossessionDiagnostics)
                    {
                        ModLogger.Info(
                            "BattleAgentCapacityPolicy: culled mount fade failed open. " +
                            "AgentIndex=" + mount.Index +
                            " Message=" + ex.Message +
                            " Source=" + (source ?? "unknown"));
                    }
                }
            }
        }

        private static void LogPolicySummaryOnce(
            Mission mission,
            int totalHealthyTroops,
            int mountedHealthyTroops,
            int maximumTroopsByComposition,
            bool dismountedBattle,
            string source)
        {
            if (_policySummaryLogged)
                return;

            _policySummaryLogged = true;
            ModLogger.Info(
                "BattleAgentCapacityPolicy: authoritative battle capacity resolved. " +
                "Scene=" + (mission?.SceneName ?? "null") +
                " BattleIdentity=" + (_battleIdentity ?? "unknown") +
                " Requested=" + _requestedBattleSize +
                " Resolved=" + _resolvedBattleSize +
                " MaximumPhysicalAgents=" + _maximumPhysicalAgentCount +
                " HealthyTroops=" + totalHealthyTroops +
                " MountedHealthyTroops=" + mountedHealthyTroops +
                " MaximumTroopsByComposition=" + maximumTroopsByComposition +
                " DismountedBattle=" + dismountedBattle +
                " RemoveCorpsesImmediately=" + _removeCorpsesImmediately +
                " CullRiderlessMounts=" + _cullRiderlessMounts +
                " Source=" + (source ?? "unknown"));
        }
    }
}
