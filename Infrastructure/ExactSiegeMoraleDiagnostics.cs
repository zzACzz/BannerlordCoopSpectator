using System;
using System.Collections.Generic;
using System.Globalization;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    /// <summary>
    /// Focused exact-siege morale diagnostics. Disabled by default and enabled only
    /// through CoopDebugConfig or COOPSPECTATOR_MORALE_DIAGNOSTICS.
    /// </summary>
    public static class ExactSiegeMoraleDiagnostics
    {
        private const float MoraleSampleIntervalSeconds = 5f;
        private const float FleeProgressSampleIntervalSeconds = 3f;
        private const float ReinforcementFrameSampleDeduplicationSeconds = 0.75f;
        private const int MaximumEventSampleLogs = 32;
        private const int MaximumFormationHandoffSampleLogs = 64;
        private const int MaximumProgressLogsPerAgent = 6;

        private sealed class FleeTracker
        {
            public Agent Agent;
            public Vec2 StartPosition;
            public Vec2 PreviousPosition;
            public WorldPosition Target;
            public float StartedAt;
            public float PreviousSampleAt;
            public float NextProgressAt;
            public int ProgressLogs;
            public string InitialPathStatus;
        }

        private sealed class SideMoraleSample
        {
            public int Agents;
            public int MissingCommonAi;
            public int CannotRetreat;
            public int BelowTen;
            public int BelowOne;
            public int AtPanicThreshold;
            public int Panicked;
            public int Retreating;
            public int RunningAway;
            public float TotalMorale;
            public float MinimumMorale = float.MaxValue;
            public float MaximumMorale = float.MinValue;
        }

        private static readonly object Sync = new object();
        private static readonly HashSet<Agent> PanickedAgentSet = new HashSet<Agent>();
        private static readonly HashSet<Agent> FleeingAgentSet = new HashSet<Agent>();
        private static readonly HashSet<string> ReinforcementDecisionLogKeys = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<Agent, FleeTracker> FleeTrackers = new Dictionary<Agent, FleeTracker>();
        private static readonly Dictionary<string, float> LastReinforcementFrameSampleTimes =
            new Dictionary<string, float>(StringComparer.Ordinal);
        private static string _battleInstanceId;
        private static int _casualtyShocks;
        private static int _panicShocks;
        private static int _recipientApplications;
        private static int _panickedAgents;
        private static int _fleeingAgents;
        private static int _routedAgents;
        private static int _fleeingRemovedKilled;
        private static int _fleeingRemovedUnconscious;
        private static int _fleeingRemovedOther;
        private static int _reinforcementWavesSpawned;
        private static int _reinforcementBodiesSpawned;
        private static int _formationAiHandoffs;
        private static int _customTargetReleases;
        private static int _panicEventSampleLogs;
        private static int _fleeEventSampleLogs;
        private static int _formationHandoffSampleLogs;
        private static int _customTargetReleaseSampleLogs;
        private static float _totalLoss;
        private static float _totalGain;
        private static float _totalRecipientChange;
        private static float _maximumRecipientChange;
        private static float _minimumObservedAttackerMorale = float.MaxValue;
        private static float _minimumObservedDefenderMorale = float.MaxValue;
        private static float _nextMoraleSampleTime;
        private static float _nextFleeProgressSampleTime;
        private static bool _sceneContractLogged;
        private static string _lastLoggedSignature;

        public static bool IsEnabled => CoopDebugConfig.MoraleDiagnostics;

        public static void Tick(Mission mission, string source)
        {
            if (!IsEnabledForMission(mission))
                return;

            float missionTime = mission.CurrentTime;
            bool logSceneContract;
            bool sampleMorale;
            bool sampleFleeProgress;
            lock (Sync)
            {
                EnsureBattleIdentity();
                logSceneContract = !_sceneContractLogged;
                if (logSceneContract)
                    _sceneContractLogged = true;

                sampleMorale = missionTime >= _nextMoraleSampleTime;
                if (sampleMorale)
                    _nextMoraleSampleTime = missionTime + MoraleSampleIntervalSeconds;

                sampleFleeProgress = FleeTrackers.Count > 0 && missionTime >= _nextFleeProgressSampleTime;
                if (sampleFleeProgress)
                    _nextFleeProgressSampleTime = missionTime + 1f;
            }

            if (logSceneContract)
                LogSceneContract(mission, source);
            if (sampleMorale)
                LogMoraleSample(mission, source);
            if (sampleFleeProgress)
                LogFleeProgress(mission, missionTime, source);
        }

        public static void RecordCasualtyShock(
            Agent affectedAgent,
            AgentState affectedState,
            float moraleLoss,
            float moraleGain)
        {
            if (!IsEnabled)
                return;

            lock (Sync)
            {
                EnsureBattleIdentity();
                _casualtyShocks++;
                _totalLoss += Math.Max(0f, moraleLoss);
                _totalGain += Math.Max(0f, moraleGain);
            }
        }

        public static void RecordPanicShock(Agent agent, float moraleLoss, float moraleGain)
        {
            if (!IsEnabled)
                return;

            lock (Sync)
            {
                EnsureBattleIdentity();
                _panicShocks++;
                _totalLoss += Math.Max(0f, moraleLoss);
                _totalGain += Math.Max(0f, moraleGain);
            }
        }

        public static void RecordRecipient(Agent agent, float maximumChange, float appliedChange)
        {
            if (!IsEnabled)
                return;

            lock (Sync)
            {
                EnsureBattleIdentity();
                _recipientApplications++;
                float absoluteChange = Math.Abs(appliedChange);
                _totalRecipientChange += absoluteChange;
                _maximumRecipientChange = Math.Max(_maximumRecipientChange, absoluteChange);
            }
        }

        public static void RecordAgentPanicked(Agent agent)
        {
            if (agent == null || !IsEnabledForMission(agent.Mission))
                return;

            bool logSample;
            lock (Sync)
            {
                EnsureBattleIdentity();
                if (PanickedAgentSet.Add(agent))
                    _panickedAgents++;
                logSample = _panicEventSampleLogs < MaximumEventSampleLogs;
                if (logSample)
                    _panicEventSampleLogs++;
            }

            if (logSample)
            {
                ModLogger.Info(
                    "ExactSiegeMoraleDiagnostics: agent panicked. " +
                    FormatAgent(agent) + " " +
                    BuildRetreatRuntimeStatus(agent, WorldPosition.Invalid) + " " +
                    BuildEnemyProximityStatus(agent.Mission, agent.Team, agent.Position.AsVec2, WorldPosition.Invalid) +
                    " Source=CoopMissionSpawnLogic.OnAgentPanicked.");
            }
        }

        public static void RecordAgentFleeing(Agent agent)
        {
            if (agent == null || !IsEnabledForMission(agent.Mission))
                return;

            Mission mission = agent.Mission;
            float missionTime = mission?.CurrentTime ?? 0f;
            Vec2 startPosition = agent.Position.AsVec2;
            WorldPosition retreatTarget = WorldPosition.Invalid;
            string pathStatus = "Path=not-queried";
            string targetStatus = "Target=invalid";
            try
            {
                retreatTarget = agent.GetRetreatPos();
                targetStatus = FormatWorldPosition("Target", retreatTarget);
                pathStatus = BuildPathStatus(mission, agent, retreatTarget);
            }
            catch (Exception ex)
            {
                pathStatus = "Path=exception:" + ex.GetType().Name + ":" + ex.Message;
            }

            bool logSample;
            lock (Sync)
            {
                EnsureBattleIdentity();
                if (FleeingAgentSet.Add(agent))
                    _fleeingAgents++;
                FleeTrackers[agent] = new FleeTracker
                {
                    Agent = agent,
                    StartPosition = startPosition,
                    PreviousPosition = startPosition,
                    Target = retreatTarget,
                    StartedAt = missionTime,
                    PreviousSampleAt = missionTime,
                    NextProgressAt = missionTime + 2f,
                    InitialPathStatus = pathStatus
                };
                logSample = _fleeEventSampleLogs < MaximumEventSampleLogs;
                if (logSample)
                    _fleeEventSampleLogs++;
            }

            if (logSample)
            {
                ModLogger.Info(
                    "ExactSiegeMoraleDiagnostics: agent fleeing. " +
                    FormatAgent(agent) + " " +
                    targetStatus + " " +
                    pathStatus + " " +
                    BuildFleePositionCountStatus(mission, agent.Team?.Side ?? BattleSideEnum.None) +
                    " " + BuildRetreatRuntimeStatus(agent, retreatTarget) +
                    " " + BuildEnemyProximityStatus(mission, agent.Team, startPosition, retreatTarget) +
                    " Source=CoopMissionSpawnLogic.OnAgentFleeing.");
            }
        }

        public static void RecordFormationAiHandoff(Formation formation, string reason, string source)
        {
            Mission mission = formation?.Team?.Mission;
            if (formation == null || !IsEnabledForMission(mission))
                return;

            bool logSample;
            lock (Sync)
            {
                EnsureBattleIdentity();
                _formationAiHandoffs++;
                logSample = _formationHandoffSampleLogs < MaximumFormationHandoffSampleLogs;
                if (logSample)
                    _formationHandoffSampleLogs++;
            }

            if (!logSample)
                return;

            try
            {
                BehaviorComponent activeBehavior = formation.AI?.ActiveBehavior;
                string activeBehaviorName = activeBehavior?.GetType().Name ?? "none";
                string activeBehaviorOrder = activeBehavior == null
                    ? "none"
                    : activeBehavior.CurrentOrder.OrderEnum.ToString();
                string appliedOrder = formation.GetReadonlyMovementOrderReference().OrderEnum.ToString();
                ModLogger.Info(
                    "ExactSiegeMoraleDiagnostics: formation AI handoff. " +
                    "MissionTime=" + FormatFloat(mission.CurrentTime) +
                    " Side=" + (formation.Team?.Side.ToString() ?? "None") +
                    " Formation=" + formation.FormationIndex +
                    " Units=" + formation.CountOfUnits +
                    " IsAIControlled=" + formation.IsAIControlled +
                    " HasPlayerOwner=" + (formation.PlayerOwner != null) +
                    " ActiveBehavior=" + activeBehaviorName +
                    " ActiveBehaviorOrder=" + activeBehaviorOrder +
                    " AppliedMovementOrder=" + appliedOrder +
                    " Reason=" + (reason ?? "unknown") +
                    " Source=" + (source ?? "unknown") + ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "ExactSiegeMoraleDiagnostics: formation AI handoff sample failed. " +
                    "Reason=" + (reason ?? "unknown") +
                    " Error=" + ex.GetType().Name + ":" + ex.Message + ".");
            }
        }

        public static void RecordCustomTargetRelease(
            Agent agent,
            bool clearedTargetAgent,
            bool clearedTargetFormation,
            string source)
        {
            if (agent == null || !IsEnabledForMission(agent.Mission))
                return;

            bool logSample;
            lock (Sync)
            {
                EnsureBattleIdentity();
                _customTargetReleases++;
                logSample = _customTargetReleaseSampleLogs < MaximumEventSampleLogs;
                if (logSample)
                    _customTargetReleaseSampleLogs++;
            }

            if (!logSample)
                return;

            ModLogger.Info(
                "ExactSiegeMoraleDiagnostics: released custom AI target for fleeing agent. " +
                FormatAgent(agent) +
                " ClearedTargetAgent=" + clearedTargetAgent +
                " ClearedTargetFormation=" + clearedTargetFormation +
                " Source=" + (source ?? "unknown") + ".");
        }

        public static void RecordAgentRemoved(Agent agent, AgentState state)
        {
            if (!IsEnabled)
                return;

            FleeTracker tracker = null;
            lock (Sync)
            {
                EnsureBattleIdentity();
                if (state == AgentState.Routed)
                    _routedAgents++;

                if (agent != null && FleeTrackers.TryGetValue(agent, out tracker))
                {
                    FleeTrackers.Remove(agent);
                    if (state == AgentState.Killed)
                        _fleeingRemovedKilled++;
                    else if (state == AgentState.Unconscious)
                        _fleeingRemovedUnconscious++;
                    else if (state != AgentState.Routed)
                        _fleeingRemovedOther++;
                }
            }

            if (tracker != null)
            {
                ModLogger.Info(
                    "ExactSiegeMoraleDiagnostics: fleeing agent removed. " +
                    FormatAgent(agent) +
                    " RemovedState=" + state +
                    " Elapsed=" + FormatFloat((agent?.Mission?.CurrentTime ?? tracker.StartedAt) - tracker.StartedAt) +
                    " ProgressLogs=" + tracker.ProgressLogs +
                    " Initial" + tracker.InitialPathStatus +
                    ".");
            }
        }

        public static void LogSummary(string source)
        {
            if (!IsEnabled)
                return;

            lock (Sync)
            {
                EnsureBattleIdentity();
                string signature =
                    _battleInstanceId + "|" + _casualtyShocks + "|" + _panicShocks + "|" +
                    _recipientApplications + "|" + _panickedAgents + "|" + _fleeingAgents + "|" + _routedAgents + "|" +
                    _reinforcementWavesSpawned + "|" + _reinforcementBodiesSpawned + "|" +
                    _formationAiHandoffs + "|" + _customTargetReleases;
                if (string.Equals(signature, _lastLoggedSignature, StringComparison.Ordinal))
                    return;

                _lastLoggedSignature = signature;
                ModLogger.Info(
                    "ExactSiegeMoraleDiagnostics: summary. " +
                    "BattleInstanceId=" + (_battleInstanceId ?? "null") +
                    " CasualtyShocks=" + _casualtyShocks +
                    " PanicShocks=" + _panicShocks +
                    " RecipientApplications=" + _recipientApplications +
                    " Panicked=" + _panickedAgents +
                    " Fleeing=" + _fleeingAgents +
                    " Routed=" + _routedAgents +
                    " FleeingRemovedKilled=" + _fleeingRemovedKilled +
                    " FleeingRemovedUnconscious=" + _fleeingRemovedUnconscious +
                    " FleeingRemovedOther=" + _fleeingRemovedOther +
                    " ReinforcementWaves=" + _reinforcementWavesSpawned +
                    " ReinforcementBodies=" + _reinforcementBodiesSpawned +
                    " FormationAiHandoffs=" + _formationAiHandoffs +
                    " CustomTargetReleases=" + _customTargetReleases +
                    " ActiveFleeTrackers=" + FleeTrackers.Count +
                    " MinimumObservedMorale=[Attacker=" + FormatOptionalFloat(_minimumObservedAttackerMorale) +
                    ",Defender=" + FormatOptionalFloat(_minimumObservedDefenderMorale) + "]" +
                    " TotalLoss=" + FormatFloat(_totalLoss) +
                    " TotalGain=" + FormatFloat(_totalGain) +
                    " TotalRecipientChange=" + FormatFloat(_totalRecipientChange) +
                    " MaximumRecipientChange=" + FormatFloat(_maximumRecipientChange) +
                    " Source=" + (source ?? "unknown") + ".");
            }
        }

        public static void RecordReinforcementWaveDecision(
            Mission mission,
            BattleSideEnum side,
            int initialActiveCount,
            int activeCount,
            int reserveCount,
            int waveSize,
            int maximumWaveCount,
            int spawnedWaveCount,
            int spawnedCount,
            string decision,
            string source)
        {
            if (!IsEnabledForMission(mission))
                return;

            lock (Sync)
            {
                EnsureBattleIdentity();
                string normalizedDecision = decision ?? "unknown";
                string logKey = side + "|" + spawnedWaveCount + "|" + normalizedDecision;
                if (!ReinforcementDecisionLogKeys.Add(logKey))
                    return;

                if (string.Equals(normalizedDecision, "wave-spawned", StringComparison.Ordinal))
                {
                    _reinforcementWavesSpawned++;
                    _reinforcementBodiesSpawned += Math.Max(0, spawnedCount);
                }

                ModLogger.Info(
                    "ExactSiegeMoraleDiagnostics: reinforcement wave decision. " +
                    "BattleInstanceId=" + (_battleInstanceId ?? "null") +
                    " Side=" + side +
                    " InitialActive=" + Math.Max(0, initialActiveCount) +
                    " Active=" + Math.Max(0, activeCount) +
                    " Deficit=" + Math.Max(0, initialActiveCount - activeCount) +
                    " Reserve=" + Math.Max(0, reserveCount) +
                    " WaveSize=" + Math.Max(1, waveSize) +
                    " MaximumWaveCount=" + Math.Max(0, maximumWaveCount) +
                    " SpawnedWaveCount=" + Math.Max(0, spawnedWaveCount) +
                    " Spawned=" + Math.Max(0, spawnedCount) +
                    " Decision=" + normalizedDecision +
                    " Source=" + (source ?? "unknown") + ".");
            }
        }

        public static void RecordReinforcementSpawnFrame(
            Mission mission,
            Team team,
            FormationClass formationClass,
            Vec3 spawnPosition,
            string frameSource,
            string source)
        {
            if (!IsEnabledForMission(mission) || team == null)
                return;

            float missionTime = mission.CurrentTime;
            string sampleKey = team.Side + "|" + formationClass;
            lock (Sync)
            {
                EnsureBattleIdentity();
                if (LastReinforcementFrameSampleTimes.TryGetValue(sampleKey, out float previousSampleAt) &&
                    missionTime - previousSampleAt < ReinforcementFrameSampleDeduplicationSeconds)
                {
                    return;
                }

                LastReinforcementFrameSampleTimes[sampleKey] = missionTime;
            }

            Vec2 position = spawnPosition.AsVec2;
            string insideBoundary;
            try
            {
                insideBoundary = mission.IsPositionInsideBoundaries(position).ToString();
            }
            catch (Exception ex)
            {
                insideBoundary = "exception:" + ex.GetType().Name;
            }

            ModLogger.Info(
                "ExactSiegeMoraleDiagnostics: reinforcement spawn frame. " +
                "MissionTime=" + FormatFloat(missionTime) +
                " Side=" + team.Side +
                " Formation=" + formationClass +
                " Position=" + FormatVec2(position) +
                " InsideBoundary=" + insideBoundary +
                " FrameSource=" + (frameSource ?? "unknown") + " " +
                BuildEnemyProximityStatus(mission, team, position, WorldPosition.Invalid) +
                " Source=" + (source ?? "unknown") + ".");
        }

        private static bool IsEnabledForMission(Mission mission)
        {
            return IsEnabled &&
                GameNetwork.IsServer &&
                mission != null &&
                BattleSnapshotRuntimeState.GetState()?.ScenarioContext?.IsSiegeBattle == true;
        }

        private static void LogSceneContract(Mission mission, string source)
        {
            int attackerFleePositions = GetFleePositionCount(mission, BattleSideEnum.Attacker);
            int defenderFleePositions = GetFleePositionCount(mission, BattleSideEnum.Defender);
            ModLogger.Info(
                "ExactSiegeMoraleDiagnostics: scene retreat contract. " +
                "Scene=" + (mission?.SceneName ?? "null") +
                " IsSiegeBattle=" + (mission?.IsSiegeBattle ?? false) +
                " MissionTeamAIType=" + (mission?.MissionTeamAIType.ToString() ?? "null") +
                " AllowAiTicking=" + (mission?.AllowAiTicking ?? false) +
                " FleePositions=[Attacker=" + attackerFleePositions + ",Defender=" + defenderFleePositions + "]" +
                " Source=" + (source ?? "unknown") + ".");
        }

        private static void LogMoraleSample(Mission mission, string source)
        {
            SideMoraleSample attacker = new SideMoraleSample();
            SideMoraleSample defender = new SideMoraleSample();
            int activeHumanAiAgents = 0;
            try
            {
                for (int i = 0; i < mission.Agents.Count; i++)
                {
                    Agent agent = mission.Agents[i];
                    if (agent == null || !agent.IsActive() || !agent.IsHuman || !agent.IsAIControlled)
                        continue;

                    activeHumanAiAgents++;
                    SideMoraleSample sample = agent.Team?.Side == BattleSideEnum.Attacker
                        ? attacker
                        : agent.Team?.Side == BattleSideEnum.Defender ? defender : null;
                    if (sample == null)
                        continue;

                    sample.Agents++;
                    CommonAIComponent commonAi = agent.CommonAIComponent;
                    if (commonAi == null)
                    {
                        sample.MissingCommonAi++;
                        continue;
                    }

                    float morale = commonAi.Morale;
                    sample.TotalMorale += morale;
                    sample.MinimumMorale = Math.Min(sample.MinimumMorale, morale);
                    sample.MaximumMorale = Math.Max(sample.MaximumMorale, morale);
                    if (morale < 10f)
                        sample.BelowTen++;
                    if (morale < 1f)
                        sample.BelowOne++;
                    if (morale < 0.01f)
                        sample.AtPanicThreshold++;
                    if (commonAi.IsPanicked)
                        sample.Panicked++;
                    if (commonAi.IsRetreating)
                        sample.Retreating++;
                    if (agent.IsRunningAway)
                        sample.RunningAway++;
                    if (!agent.GetAgentFlags().HasAnyFlag(AgentFlag.CanRetreat))
                        sample.CannotRetreat++;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("ExactSiegeMoraleDiagnostics: morale sample failed: " + ex.GetType().Name + ":" + ex.Message);
                return;
            }

            lock (Sync)
            {
                if (attacker.MinimumMorale < float.MaxValue)
                    _minimumObservedAttackerMorale = Math.Min(_minimumObservedAttackerMorale, attacker.MinimumMorale);
                if (defender.MinimumMorale < float.MaxValue)
                    _minimumObservedDefenderMorale = Math.Min(_minimumObservedDefenderMorale, defender.MinimumMorale);
            }

            ModLogger.Info(
                "ExactSiegeMoraleDiagnostics: morale sample. " +
                "MissionTime=" + FormatFloat(mission.CurrentTime) +
                " ActiveHumanAI=" + activeHumanAiAgents +
                " Attacker={" + FormatSideSample(attacker) + "}" +
                " Defender={" + FormatSideSample(defender) + "}" +
                " Source=" + (source ?? "unknown") + ".");
        }

        private static void LogFleeProgress(Mission mission, float missionTime, string source)
        {
            List<FleeTracker> trackers;
            lock (Sync)
                trackers = new List<FleeTracker>(FleeTrackers.Values);

            foreach (FleeTracker tracker in trackers)
            {
                Agent agent = tracker.Agent;
                if (agent == null || !agent.IsActive() || missionTime < tracker.NextProgressAt ||
                    tracker.ProgressLogs >= MaximumProgressLogsPerAgent)
                {
                    continue;
                }

                string status;
                try
                {
                    Vec2 position = agent.Position.AsVec2;
                    float moved = (float)Math.Sqrt(position.DistanceSquared(tracker.StartPosition));
                    float targetDistanceValue = tracker.Target.IsValid && tracker.Target.AsVec2.IsValid
                        ? (float)Math.Sqrt(position.DistanceSquared(tracker.Target.AsVec2))
                        : float.NaN;
                    string targetDistance = float.IsNaN(targetDistanceValue)
                        ? "invalid"
                        : FormatFloat(targetDistanceValue);
                    Vec2 segment = position - tracker.PreviousPosition;
                    float segmentDistance = segment.Length;
                    float segmentDuration = Math.Max(0f, missionTime - tracker.PreviousSampleAt);
                    float segmentSpeed = segmentDuration > 0.001f
                        ? segmentDistance / segmentDuration
                        : 0f;
                    float segmentTowardTarget = CalculateTowardTargetDistance(
                        tracker.PreviousPosition,
                        position,
                        tracker.Target);
                    status =
                        "Agent=" + agent.Index +
                        " Side=" + (agent.Team?.Side.ToString() ?? "None") +
                        " Elapsed=" + FormatFloat(missionTime - tracker.StartedAt) +
                        " Moved=" + FormatFloat(moved) +
                        " SegmentDistance=" + FormatFloat(segmentDistance) +
                        " SegmentDuration=" + FormatFloat(segmentDuration) +
                        " SegmentSpeed=" + FormatFloat(segmentSpeed) +
                        " SegmentTowardTarget=" + FormatOptionalMetric(segmentTowardTarget) +
                        " TargetDistance=" + targetDistance +
                        " Morale=" + FormatFloat(agent.GetMorale()) +
                        " IsPanicked=" + (agent.CommonAIComponent?.IsPanicked ?? false) +
                        " IsRetreating=" + (agent.CommonAIComponent?.IsRetreating ?? false) +
                        " IsRunningAway=" + agent.IsRunningAway +
                        " IsFadingOut=" + agent.IsFadingOut() +
                        " InsideBoundary=" + mission.IsPositionInsideBoundaries(position) + " " +
                        BuildRetreatRuntimeStatus(agent, tracker.Target) + " " +
                        BuildEnemyProximityStatus(mission, agent.Team, position, tracker.Target);
                }
                catch (Exception ex)
                {
                    status = "Agent=" + (agent?.Index ?? -1) + " Error=" + ex.GetType().Name + ":" + ex.Message;
                }

                lock (Sync)
                {
                    tracker.PreviousPosition = agent.Position.AsVec2;
                    tracker.PreviousSampleAt = missionTime;
                    tracker.ProgressLogs++;
                    tracker.NextProgressAt = missionTime + FleeProgressSampleIntervalSeconds;
                }
                ModLogger.Info(
                    "ExactSiegeMoraleDiagnostics: flee progress. " + status +
                    " Source=" + (source ?? "unknown") + ".");
            }
        }

        private static string BuildPathStatus(Mission mission, Agent agent, WorldPosition target)
        {
            if (mission?.Scene == null || agent == null || !target.IsValid || !target.AsVec2.IsValid)
                return "Path=invalid-input";

            try
            {
                WorldPosition from = agent.GetWorldPosition();
                WorldPosition to = target;
                bool reachable = mission.Scene.GetPathDistanceBetweenPositions(ref from, ref to, 0f, out float distance);
                bool targetInsideBoundary = mission.IsPositionInsideBoundaries(target.AsVec2);
                return "Path=" + (reachable ? "reachable" : "unreachable") +
                    "/Distance=" + (reachable ? FormatFloat(distance) : "n/a") +
                    "/TargetInsideBoundary=" + targetInsideBoundary;
            }
            catch (Exception ex)
            {
                return "Path=exception:" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        private static string BuildFleePositionCountStatus(Mission mission, BattleSideEnum side)
        {
            return "FleePositionsForSide=" + GetFleePositionCount(mission, side);
        }

        private static string BuildRetreatRuntimeStatus(Agent agent, WorldPosition retreatTarget)
        {
            if (agent == null)
                return "RetreatRuntime={Agent=null}";

            try
            {
                Agent targetAgent = agent.GetTargetAgent();
                Formation formation = agent.Formation;
                Vec2 velocity = agent.GetAverageRealGlobalVelocity().AsVec2;
                Vec2 movementDirection = agent.GetMovementDirection();
                float velocityTowardTarget = CalculateVectorTowardTarget(
                    agent.Position.AsVec2,
                    velocity,
                    retreatTarget);
                float movementDirectionAlignment = CalculateDirectionAlignment(
                    agent.Position.AsVec2,
                    movementDirection,
                    retreatTarget);
                string targetAgentStatus = targetAgent == null
                    ? "none"
                    : targetAgent.Index +
                      "/Active=" + targetAgent.IsActive() +
                      "/Side=" + (targetAgent.Team?.Side.ToString() ?? "None") +
                      "/Formation=" + (targetAgent.Formation?.FormationIndex.ToString() ?? "null") +
                      "/Distance=" + FormatFloat(
                          (float)Math.Sqrt(agent.Position.AsVec2.DistanceSquared(targetAgent.Position.AsVec2)));

                return "RetreatRuntime={" +
                    "NativeIsRetreating=" + agent.IsRetreating() +
                    " TargetAgent=" + targetAgentStatus +
                    " TargetFormationIndex=" + agent.GetTargetFormationIndex() +
                    " Formation=" + (formation?.FormationIndex.ToString() ?? "null") +
                    " FormationUnits=" + (formation?.CountOfUnits ?? 0) +
                    " Controller=" + agent.Controller +
                    " MovementLocked=" + agent.MovementLockedState +
                    " Velocity=" + FormatVec2(velocity) +
                    " Speed=" + FormatFloat(velocity.Length) +
                    " VelocityTowardTarget=" + FormatOptionalMetric(velocityTowardTarget) +
                    " MovementDirectionDotTarget=" + FormatOptionalMetric(movementDirectionAlignment) +
                    "}";
            }
            catch (Exception ex)
            {
                return "RetreatRuntime={Error=" + ex.GetType().Name + ":" + ex.Message + "}";
            }
        }

        private static string BuildEnemyProximityStatus(
            Mission mission,
            Team ownTeam,
            Vec2 position,
            WorldPosition retreatTarget)
        {
            if (mission == null || ownTeam == null || !position.IsValid)
                return "Enemies={invalid-input}";

            try
            {
                float nearestDistanceSquared = float.MaxValue;
                int nearestAgentIndex = -1;
                int withinTwo = 0;
                int withinFive = 0;
                int withinTen = 0;
                int withinTwenty = 0;
                int aheadTen = 0;
                int aheadTwenty = 0;
                Vec2 targetDirection = Vec2.Invalid;
                bool hasTargetDirection = retreatTarget.IsValid && retreatTarget.AsVec2.IsValid;
                if (hasTargetDirection)
                {
                    targetDirection = retreatTarget.AsVec2 - position;
                    hasTargetDirection = targetDirection.LengthSquared > 0.001f;
                    if (hasTargetDirection)
                        targetDirection.Normalize();
                }

                for (int i = 0; i < mission.Agents.Count; i++)
                {
                    Agent candidate = mission.Agents[i];
                    if (candidate == null ||
                        !candidate.IsActive() ||
                        !candidate.IsHuman ||
                        candidate.Team == null ||
                        !ownTeam.IsEnemyOf(candidate.Team))
                    {
                        continue;
                    }

                    Vec2 toEnemy = candidate.Position.AsVec2 - position;
                    float distanceSquared = toEnemy.LengthSquared;
                    if (distanceSquared < nearestDistanceSquared)
                    {
                        nearestDistanceSquared = distanceSquared;
                        nearestAgentIndex = candidate.Index;
                    }

                    if (distanceSquared <= 4f)
                        withinTwo++;
                    if (distanceSquared <= 25f)
                        withinFive++;
                    if (distanceSquared <= 100f)
                        withinTen++;
                    if (distanceSquared <= 400f)
                        withinTwenty++;

                    if (hasTargetDirection && distanceSquared > 0.001f && distanceSquared <= 400f)
                    {
                        Vec2 enemyDirection = toEnemy.Normalized();
                        if (targetDirection.DotProduct(enemyDirection) >= 0.5f)
                        {
                            if (distanceSquared <= 100f)
                                aheadTen++;
                            aheadTwenty++;
                        }
                    }
                }

                return "Enemies={" +
                    "Nearest=" + (nearestAgentIndex >= 0 ? nearestAgentIndex.ToString() : "none") +
                    "/Distance=" + (nearestDistanceSquared < float.MaxValue
                        ? FormatFloat((float)Math.Sqrt(nearestDistanceSquared))
                        : "n/a") +
                    " Within2=" + withinTwo +
                    " Within5=" + withinFive +
                    " Within10=" + withinTen +
                    " Within20=" + withinTwenty +
                    " Ahead10=" + (hasTargetDirection ? aheadTen.ToString() : "n/a") +
                    " Ahead20=" + (hasTargetDirection ? aheadTwenty.ToString() : "n/a") +
                    "}";
            }
            catch (Exception ex)
            {
                return "Enemies={Error=" + ex.GetType().Name + ":" + ex.Message + "}";
            }
        }

        private static float CalculateTowardTargetDistance(
            Vec2 previousPosition,
            Vec2 currentPosition,
            WorldPosition retreatTarget)
        {
            if (!retreatTarget.IsValid || !retreatTarget.AsVec2.IsValid)
                return float.NaN;

            float previousDistance = (float)Math.Sqrt(previousPosition.DistanceSquared(retreatTarget.AsVec2));
            float currentDistance = (float)Math.Sqrt(currentPosition.DistanceSquared(retreatTarget.AsVec2));
            return previousDistance - currentDistance;
        }

        private static float CalculateVectorTowardTarget(
            Vec2 position,
            Vec2 vector,
            WorldPosition retreatTarget)
        {
            if (!retreatTarget.IsValid ||
                !retreatTarget.AsVec2.IsValid ||
                vector.LengthSquared <= 0.0001f)
            {
                return float.NaN;
            }

            Vec2 targetDirection = retreatTarget.AsVec2 - position;
            if (targetDirection.LengthSquared <= 0.001f)
                return float.NaN;

            targetDirection.Normalize();
            return vector.DotProduct(targetDirection);
        }

        private static float CalculateDirectionAlignment(
            Vec2 position,
            Vec2 direction,
            WorldPosition retreatTarget)
        {
            if (direction.LengthSquared <= 0.0001f)
                return float.NaN;

            direction.Normalize();
            return CalculateVectorTowardTarget(position, direction, retreatTarget);
        }

        private static int GetFleePositionCount(Mission mission, BattleSideEnum side)
        {
            try
            {
                return mission?.GetFleePositionsForSide(side)?.Count ?? -1;
            }
            catch
            {
                return -1;
            }
        }

        private static string FormatSideSample(SideMoraleSample sample)
        {
            int measured = Math.Max(0, sample.Agents - sample.MissingCommonAi);
            float average = measured > 0 ? sample.TotalMorale / measured : float.NaN;
            return "Agents=" + sample.Agents +
                " Measured=" + measured +
                " Min=" + FormatOptionalFloat(sample.MinimumMorale) +
                " Avg=" + (float.IsNaN(average) ? "n/a" : FormatFloat(average)) +
                " Max=" + FormatOptionalMaximum(sample.MaximumMorale) +
                " Below10=" + sample.BelowTen +
                " Below1=" + sample.BelowOne +
                " Below0.01=" + sample.AtPanicThreshold +
                " Panicked=" + sample.Panicked +
                " Retreating=" + sample.Retreating +
                " RunningAway=" + sample.RunningAway +
                " MissingCommonAI=" + sample.MissingCommonAi +
                " CannotRetreat=" + sample.CannotRetreat;
        }

        private static string FormatAgent(Agent agent)
        {
            if (agent == null)
                return "Agent=null";

            try
            {
                return "Agent=" + agent.Index +
                    " Side=" + (agent.Team?.Side.ToString() ?? "None") +
                    " Character=" + (agent.Character?.StringId ?? "null") +
                    " Morale=" + FormatFloat(agent.GetMorale()) +
                    " CommonAI=" + (agent.CommonAIComponent != null) +
                    " CanRetreat=" + agent.GetAgentFlags().HasAnyFlag(AgentFlag.CanRetreat) +
                    " IsPanicked=" + (agent.CommonAIComponent?.IsPanicked ?? false) +
                    " IsRetreating=" + (agent.CommonAIComponent?.IsRetreating ?? false) +
                    " IsRunningAway=" + agent.IsRunningAway +
                    " IsFadingOut=" + agent.IsFadingOut() +
                    " Position=" + FormatVec2(agent.Position.AsVec2);
            }
            catch (Exception ex)
            {
                return "Agent=" + agent.Index + " FormatError=" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        private static string FormatWorldPosition(string label, WorldPosition position)
        {
            if (!position.IsValid || !position.AsVec2.IsValid)
                return label + "=invalid";
            return label + "=" + FormatVec2(position.AsVec2);
        }

        private static string FormatVec2(Vec2 position)
        {
            return "(" + FormatFloat(position.x) + "," + FormatFloat(position.y) + ")";
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string FormatOptionalFloat(float value)
        {
            return value == float.MaxValue ? "n/a" : FormatFloat(value);
        }

        private static string FormatOptionalMaximum(float value)
        {
            return value == float.MinValue ? "n/a" : FormatFloat(value);
        }

        private static string FormatOptionalMetric(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? "n/a" : FormatFloat(value);
        }

        private static void EnsureBattleIdentity()
        {
            BattleRuntimeState state = BattleSnapshotRuntimeState.GetState();
            string battleInstanceId = state?.Snapshot?.BattleInstanceId ?? state?.Snapshot?.BattleId ?? string.Empty;
            if (string.Equals(_battleInstanceId, battleInstanceId, StringComparison.Ordinal))
                return;

            _battleInstanceId = battleInstanceId;
            _casualtyShocks = 0;
            _panicShocks = 0;
            _recipientApplications = 0;
            _panickedAgents = 0;
            _fleeingAgents = 0;
            _routedAgents = 0;
            _fleeingRemovedKilled = 0;
            _fleeingRemovedUnconscious = 0;
            _fleeingRemovedOther = 0;
            _reinforcementWavesSpawned = 0;
            _reinforcementBodiesSpawned = 0;
            _formationAiHandoffs = 0;
            _customTargetReleases = 0;
            _panicEventSampleLogs = 0;
            _fleeEventSampleLogs = 0;
            _formationHandoffSampleLogs = 0;
            _customTargetReleaseSampleLogs = 0;
            _totalLoss = 0f;
            _totalGain = 0f;
            _totalRecipientChange = 0f;
            _maximumRecipientChange = 0f;
            _minimumObservedAttackerMorale = float.MaxValue;
            _minimumObservedDefenderMorale = float.MaxValue;
            _nextMoraleSampleTime = 0f;
            _nextFleeProgressSampleTime = 0f;
            _sceneContractLogged = false;
            _lastLoggedSignature = null;
            PanickedAgentSet.Clear();
            FleeingAgentSet.Clear();
            ReinforcementDecisionLogKeys.Clear();
            FleeTrackers.Clear();
            LastReinforcementFrameSampleTimes.Clear();
        }
    }
}
