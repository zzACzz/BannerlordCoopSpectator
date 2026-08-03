using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using CoopSpectator.MissionBehaviors;
using NetworkMessages.FromServer;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Infrastructure
{
    internal static class ExactCreateAgentCorridorDiagnostics
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<int, ClientCreateAgentCorridorState> ClientStatesByAgentIndex =
            new Dictionary<int, ClientCreateAgentCorridorState>();
        private static readonly Dictionary<string, ServerCreateAgentPendingState> PendingServerStatesByEntryId =
            new Dictionary<string, ServerCreateAgentPendingState>(StringComparer.Ordinal);
        private static readonly Dictionary<int, ServerCreateAgentExpectedState> ServerStatesByAgentIndex =
            new Dictionary<int, ServerCreateAgentExpectedState>();
        private static readonly Dictionary<int, ClientAgentPositionVisualState> ClientAgentPositionVisualStates =
            new Dictionary<int, ClientAgentPositionVisualState>();
        private static readonly Dictionary<int, ServerAgentPositionState> ServerAgentPositionStates =
            new Dictionary<int, ServerAgentPositionState>();
        private static readonly Dictionary<string, ClientPeerPreviewVisualState> ClientPeerPreviewVisualStates =
            new Dictionary<string, ClientPeerPreviewVisualState>(StringComparer.Ordinal);
        private static Dictionary<int, ClientNativeMissionTickAgentState> _clientNativeMissionTickExitStates =
            new Dictionary<int, ClientNativeMissionTickAgentState>();
        private static Mission _serverStateMission;
        private static Mission _serverPositionMission;
        private static DateTime _nextServerPositionSampleUtc = DateTime.MinValue;
        private static DateTime _serverBattleActiveObservedUtc = DateTime.MinValue;
        private static string _lastServerPositionPhase;
        private static Mission _clientPositionVisualMission;
        private static DateTime _nextClientPositionVisualSampleUtc = DateTime.MinValue;
        private static DateTime _clientBattleActiveObservedUtc = DateTime.MinValue;
        private static string _lastClientPositionVisualPhase;
        private static Mission _clientNativeMissionTickMission;
        private static long _clientNativeMissionTickSequence;
        private static long _clientNativeExecutionBoundarySequence;

        private static readonly TimeSpan ClientPositionVisualSampleInterval = TimeSpan.FromMilliseconds(250d);
        private static readonly TimeSpan ClientPositionVisualBattleActiveWindow = TimeSpan.FromSeconds(10d);
        private static readonly TimeSpan ServerPositionSampleInterval = TimeSpan.FromMilliseconds(100d);
        private static readonly TimeSpan ServerPositionBattleActiveWindow = TimeSpan.FromSeconds(10d);
        private const float ClientPositionVisualMovementThreshold = 1f;
        private const float ClientPositionVisualMismatchThreshold = 0.5f;
        private const float ServerPositionLargeMovementThreshold = 20f;
        private const float ManagedTeleportDiagnosticThreshold = 20f;
        private const float ClientNativeMissionTickLargeMovementThreshold = 20f;
        private const int MaxPeerPreviewVisualIndexToSample = 15;
        private static readonly MethodInfo MissionPeerGetVisualsMethod =
            typeof(MissionPeer).GetMethod(
                "GetVisuals",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(int) },
                null);
        private static readonly FieldInfo AgentNativePointerField =
            typeof(Agent).GetField("_pointer", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo AgentPositionPointerField =
            typeof(Agent).GetField("_positionPointer", BindingFlags.Instance | BindingFlags.NonPublic);

        private static bool IsVerboseEnabled => ExperimentalFeatures.EnableExactCreateAgentCorridorDiagnostics;

        internal static bool IsClientNativeMissionTickBoundaryDiagnosticsEnabled => IsVerboseEnabled;

        internal static bool IsClientNativeExecutionBoundaryDiagnosticsEnabled => IsVerboseEnabled;

        private sealed class ClientCreateAgentCorridorState
        {
            public int AgentIndex { get; set; }
            public DateTime FirstObservedUtc { get; set; }
            public DateTime LastObservedUtc { get; set; }
            public string LastPayloadSummary { get; set; }
            public string CandidateSummary { get; set; }
            public string CandidateEntryId { get; set; }
            public string CandidatePayloadComparisonSummary { get; set; }
            public string SnapshotReadinessSummary { get; set; }
            public string LastMutationSummary { get; set; }
            public string LastBypassReason { get; set; }
            public string LastResolvedEntrySummary { get; set; }
            public string LastResolvedEntryId { get; set; }
            public bool CreateAgentPostfixObserved { get; set; }
            public int WieldEventCount { get; set; }
            public int EquipmentSyncEventCount { get; set; }
            public int CreateAgentOnReadEventCount { get; set; }
        }

        private sealed class ClientAgentPositionVisualState
        {
            public Vec3 Position { get; set; }
            public Vec3 VisualPosition { get; set; }
            public Vec3 VisualFrameOrigin { get; set; }
            public bool HasVisualPosition { get; set; }
            public bool HasVisualFrame { get; set; }
            public bool VisualsValid { get; set; }
            public bool VisualsVisible { get; set; }
            public int TeamIndex { get; set; }
            public int FormationIndex { get; set; }
        }

        private sealed class ServerAgentPositionState
        {
            public Vec3 Position { get; set; }
            public int TeamIndex { get; set; }
            public int FormationIndex { get; set; }
        }

        private sealed class ClientPeerPreviewVisualState
        {
            public Vec3 FrameOrigin { get; set; }
            public bool HasFrame { get; set; }
            public bool VisualsValid { get; set; }
            public bool VisualsVisible { get; set; }
            public string CharacterId { get; set; }
        }

        private sealed class ClientNativeMissionTickBoundaryState
        {
            public long Sequence { get; set; }
            public Dictionary<int, ClientNativeMissionTickAgentState> EntryStates { get; set; }
        }

        private sealed class ClientNativeExecutionBoundaryState
        {
            public Mission Mission { get; set; }
            public long Sequence { get; set; }
            public string Boundary { get; set; }
            public int ManagedThreadId { get; set; }
            public Dictionary<int, ClientNativeMissionTickAgentState> EntryStates { get; set; }
        }

        private sealed class ClientNativeMissionTickAgentState
        {
            public int AgentIndex { get; set; }
            public string CharacterId { get; set; }
            public Vec3 Position { get; set; }
            public ulong AgentPointer { get; set; }
            public ulong PositionPointer { get; set; }
            public int TeamIndex { get; set; }
            public string TeamSide { get; set; }
            public int FormationIndex { get; set; }
            public AgentControllerType Controller { get; set; }
            public bool IsAiControlled { get; set; }
            public bool IsMainAgent { get; set; }
        }

        private sealed class ServerCreateAgentPendingState
        {
            public string EntryId { get; set; }
            public string TroopId { get; set; }
            public string PayloadDiagnosticSummary { get; set; }
            public string PayloadWeaponLayoutSummary { get; set; }
            public bool RequiresServerSpawnBaselineOnClientCreateAgent { get; set; }
            public string EntryWeaponSlotVector { get; set; }
            public string PreSpawnWeaponSlotVector { get; set; }
            public string PreSpawnMountSummary { get; set; }
            public string PreSpawnNonWeaponSlotVector { get; set; }
            public WeaponSlotSnapshot[] EntryWeaponSlots { get; set; }
            public WeaponSlotSnapshot[] PreSpawnWeaponSlots { get; set; }
            public Dictionary<string, string> ExpectedItemOriginById { get; set; }
        }

        private sealed class ServerCreateAgentExpectedState
        {
            public int AgentIndex { get; set; }
            public string EntryId { get; set; }
            public string TroopId { get; set; }
            public string ServerSpawnCharacterId { get; set; }
            public bool ServerSpawnMounted { get; set; }
            public string PayloadDiagnosticSummary { get; set; }
            public string PayloadWeaponLayoutSummary { get; set; }
            public bool RequiresServerSpawnBaselineOnClientCreateAgent { get; set; }
            public string ExpectedEntryWeaponSlotVector { get; set; }
            public string ExpectedPreSpawnWeaponSlotVector { get; set; }
            public string ExpectedPreSpawnNonWeaponSlotVector { get; set; }
            public string ExpectedPreSpawnMountSummary { get; set; }
            public string ServerSpawnMissionWeaponSlotVector { get; set; }
            public string ServerSpawnSpawnWeaponSlotVector { get; set; }
            public WeaponSlotSnapshot[] ExpectedEntryWeaponSlots { get; set; }
            public WeaponSlotSnapshot[] ExpectedPreSpawnWeaponSlots { get; set; }
            public WeaponSlotSnapshot[] ServerSpawnMissionWeaponSlots { get; set; }
            public WeaponSlotSnapshot[] ServerSpawnSpawnWeaponSlots { get; set; }
            public Dictionary<string, string> ExpectedItemOriginById { get; set; }
            public Equipment ServerSpawnSpawnEquipmentClone { get; set; }
            public Equipment ServerSpawnMissionEquipmentClone { get; set; }
            public int CreateAgentOnWriteEventCount { get; set; }
        }

        private sealed class PayloadCandidateMatch
        {
            public RosterEntryState EntryState { get; set; }
            public bool CharacterMatch { get; set; }
            public bool WeaponLayoutMatch { get; set; }
            public bool MountedMatch { get; set; }
            public int Score { get; set; }
        }

        private sealed class PayloadCandidateResolution
        {
            public string State { get; set; }
            public string Summary { get; set; }
            public string EntryId { get; set; }
            public string PayloadComparisonSummary { get; set; }
        }

        private sealed class WeaponSlotSnapshot
        {
            public EquipmentIndex Slot { get; set; }
            public string ItemId { get; set; }
            public int? Amount { get; set; }
        }

        private sealed class EquipmentSlotSnapshot
        {
            public EquipmentIndex Slot { get; set; }
            public string ItemId { get; set; }
        }

        internal static void ResetRuntimeState(string source)
        {
            lock (Sync)
            {
                ClientStatesByAgentIndex.Clear();
                PendingServerStatesByEntryId.Clear();
                ServerStatesByAgentIndex.Clear();
                ClientAgentPositionVisualStates.Clear();
                ServerAgentPositionStates.Clear();
                ClientPeerPreviewVisualStates.Clear();
                _clientNativeMissionTickExitStates =
                    new Dictionary<int, ClientNativeMissionTickAgentState>();
                _serverStateMission = null;
                _serverPositionMission = null;
                _nextServerPositionSampleUtc = DateTime.MinValue;
                _serverBattleActiveObservedUtc = DateTime.MinValue;
                _lastServerPositionPhase = null;
                _clientPositionVisualMission = null;
                _nextClientPositionVisualSampleUtc = DateTime.MinValue;
                _clientBattleActiveObservedUtc = DateTime.MinValue;
                _lastClientPositionVisualPhase = null;
                _clientNativeMissionTickMission = null;
                _clientNativeMissionTickSequence = 0L;
            }

            ModLogger.Info(
                "ExactCreateAgentCorridorDiagnostics: reset runtime state. " +
                "Source=" + (source ?? "unknown"));
        }

        internal static void ClearServerAgentIndexState(int agentIndex, string source)
        {
            if (agentIndex < 0)
                return;

            bool removed;
            lock (Sync)
            {
                removed = ServerStatesByAgentIndex.Remove(agentIndex);
            }

            if (!removed)
                return;

            Log(
                "server-agentindex-state-cleared",
                "AgentIndex=" + agentIndex +
                " Source=" + (source ?? "unknown"),
                persistToRuntimeBundle: false);
        }

        internal static void ObserveServerPreSpawnPayload(
            ExactCampaignSnapshotAgentOrigin exactOrigin,
            RosterEntryState entryState,
            ExactTransferSpawnContract contract,
            ExactCreateAgentPayloadDiagnosticDecision payloadDiagnostic,
            Equipment exactEquipment,
            AgentBuildData agentBuildData,
            bool injectEquipment,
            bool spawnFromAgentVisuals)
        {
            if (!GameNetwork.IsServer || exactOrigin == null || entryState == null)
                return;

            EnsureServerMissionScope(Mission.Current);
            lock (Sync)
            {
                PendingServerStatesByEntryId[entryState.EntryId ?? string.Empty] = new ServerCreateAgentPendingState
                {
                    EntryId = entryState.EntryId,
                    TroopId = exactOrigin.TroopId,
                    PayloadDiagnosticSummary = payloadDiagnostic?.ToSummary() ?? "ExactCreateAgentPayloadDiagnostic={State=absent}",
                    PayloadWeaponLayoutSummary = payloadDiagnostic?.ToWeaponLayoutSummary() ?? "ExactCreateAgentWeaponLayout={State=absent}",
                    RequiresServerSpawnBaselineOnClientCreateAgent = payloadDiagnostic?.RequiresServerSpawnBaselineOnClientCreateAgent == true,
                    EntryWeaponSlotVector = BuildEntryWeaponSlotVector(entryState),
                    PreSpawnWeaponSlotVector = BuildEquipmentWeaponSlotVector(exactEquipment),
                    PreSpawnMountSummary = ExactCreateAgentPayloadDiagnostics.BuildEquipmentMountLayoutSummary(exactEquipment),
                    PreSpawnNonWeaponSlotVector = BuildEquipmentNonWeaponSlotVector(exactEquipment),
                    EntryWeaponSlots = BuildEntryWeaponSlots(entryState),
                    PreSpawnWeaponSlots = BuildEquipmentWeaponSlots(exactEquipment),
                    ExpectedItemOriginById = BuildExpectedItemOriginById(exactEquipment)
                };
            }

            if (!IsVerboseEnabled)
                return;

            string details =
                "EntryId=" + (entryState.EntryId ?? "null") +
                " TroopId=" + (exactOrigin.TroopId ?? "null") +
                " Side=" + exactOrigin.Side +
                " Mounted=" + entryState.IsMounted +
                " Hero=" + entryState.IsHero +
                " InjectEquipment=" + injectEquipment +
                " SpawnFromAgentVisuals=" + spawnFromAgentVisuals +
                " PayloadCharacterId=" + (contract?.Identity?.NativeMultiplayerCharacterId ??
                                           entryState.SpawnTemplateId ??
                                           entryState.CharacterId ??
                                           entryState.OriginalCharacterId ??
                                           "null") +
                " EntryWeapons={" + ExactCreateAgentPayloadDiagnostics.BuildEntryWeaponLayoutSummary(entryState) + "}" +
                " EntryWeaponSlots={" + BuildEntryWeaponSlotVector(entryState) + "}" +
                " EntryMount={" + ExactCreateAgentPayloadDiagnostics.BuildEntryMountLayoutSummary(entryState) + "}" +
                " PreSpawnWeapons={" + ExactCreateAgentPayloadDiagnostics.BuildEquipmentWeaponLayoutSummary(exactEquipment) + "}" +
                " PreSpawnWeaponSlots={" + BuildEquipmentWeaponSlotVector(exactEquipment) + "}" +
                " PreSpawnMount={" + ExactCreateAgentPayloadDiagnostics.BuildEquipmentMountLayoutSummary(exactEquipment) + "}" +
                " BuildDataBeforeNative={" + BuildAgentBuildDataPositionSummary(agentBuildData) + "}" +
                " " + (payloadDiagnostic?.ToSummary() ?? "ExactCreateAgentPayloadDiagnostic={State=absent}") +
                " " + (payloadDiagnostic?.ToWeaponLayoutSummary() ?? "ExactCreateAgentWeaponLayout={State=absent}");
            Log("server-pre-spawn-payload", details, persistToRuntimeBundle: false);
        }

        internal static void ObserveServerSpawnResult(
            ExactCampaignSnapshotAgentOrigin exactOrigin,
            ExactCreateAgentPayloadDiagnosticDecision payloadDiagnostic,
            AgentBuildData agentBuildData,
            Agent result,
            bool spawnFromAgentVisuals,
            bool equipmentInjected)
        {
            if (!GameNetwork.IsServer || exactOrigin == null)
                return;

            try
            {
                ServerCreateAgentPendingState pendingState = null;
                lock (Sync)
                {
                    string entryKey = exactOrigin.EntryId ?? string.Empty;
                    if (PendingServerStatesByEntryId.TryGetValue(entryKey, out pendingState) &&
                        result != null)
                    {
                        ServerStatesByAgentIndex[result.Index] = new ServerCreateAgentExpectedState
                        {
                            AgentIndex = result.Index,
                            EntryId = pendingState.EntryId ?? exactOrigin.EntryId,
                            TroopId = pendingState.TroopId ?? exactOrigin.TroopId,
                            ServerSpawnCharacterId = result?.Character?.StringId ?? string.Empty,
                            ServerSpawnMounted = result?.MountAgent != null ||
                                                result?.SpawnEquipment?[EquipmentIndex.Horse].Item != null ||
                                                result?.SpawnEquipment?[EquipmentIndex.HorseHarness].Item != null,
                            PayloadDiagnosticSummary = pendingState.PayloadDiagnosticSummary ?? (payloadDiagnostic?.ToSummary() ?? "ExactCreateAgentPayloadDiagnostic={State=absent}"),
                            PayloadWeaponLayoutSummary = pendingState.PayloadWeaponLayoutSummary ?? (payloadDiagnostic?.ToWeaponLayoutSummary() ?? "ExactCreateAgentWeaponLayout={State=absent}"),
                            RequiresServerSpawnBaselineOnClientCreateAgent = pendingState.RequiresServerSpawnBaselineOnClientCreateAgent,
                            ExpectedEntryWeaponSlotVector = pendingState.EntryWeaponSlotVector,
                            ExpectedPreSpawnWeaponSlotVector = pendingState.PreSpawnWeaponSlotVector,
                            ExpectedPreSpawnNonWeaponSlotVector = pendingState.PreSpawnNonWeaponSlotVector,
                            ExpectedPreSpawnMountSummary = pendingState.PreSpawnMountSummary,
                            ExpectedEntryWeaponSlots = pendingState.EntryWeaponSlots,
                            ExpectedPreSpawnWeaponSlots = pendingState.PreSpawnWeaponSlots,
                            ServerSpawnMissionWeaponSlotVector = BuildMissionEquipmentWeaponSlotVector(result?.Equipment),
                            ServerSpawnSpawnWeaponSlotVector = BuildEquipmentWeaponSlotVector(result?.SpawnEquipment),
                            ServerSpawnMissionWeaponSlots = BuildMissionEquipmentWeaponSlots(result?.Equipment),
                            ServerSpawnSpawnWeaponSlots = BuildEquipmentWeaponSlots(result?.SpawnEquipment),
                            ExpectedItemOriginById = pendingState.ExpectedItemOriginById,
                            ServerSpawnSpawnEquipmentClone = CloneEquipment(result?.SpawnEquipment),
                            ServerSpawnMissionEquipmentClone = BuildEquipmentCloneFromMissionEquipment(result?.Equipment)
                        };
                    }
                }

                if (!IsVerboseEnabled)
                    return;

                string details =
                    "EntryId=" + (exactOrigin.EntryId ?? "null") +
                    " TroopId=" + (exactOrigin.TroopId ?? "null") +
                    " AgentIndex=" + (result?.Index.ToString() ?? "null") +
                    " SpawnFromAgentVisuals=" + spawnFromAgentVisuals +
                    " EquipmentInjected=" + equipmentInjected +
                    " BuildDataAfterNative={" + BuildAgentBuildDataPositionSummary(agentBuildData) + "}" +
                    " SpawnedAgent={" + BuildAgentSummary(result) + "}" +
                    " SpawnedAgentSpawnWeaponSlots={" + BuildEquipmentWeaponSlotVector(result?.SpawnEquipment) + "}" +
                    " SpawnedAgentMissionWeaponSlots={" + BuildMissionEquipmentWeaponSlotVector(result?.Equipment) + "}" +
                    " " + (payloadDiagnostic?.ToSummary() ?? "ExactCreateAgentPayloadDiagnostic={State=absent}") +
                    " " + (payloadDiagnostic?.ToWeaponLayoutSummary() ?? "ExactCreateAgentWeaponLayout={State=absent}");
                Log("server-spawn-result", details, persistToRuntimeBundle: false);
            }
            catch (Exception ex)
            {
                Log(
                    "server-spawn-result-failed-open",
                    "EntryId=" + (exactOrigin.EntryId ?? "null") +
                    " TroopId=" + (exactOrigin.TroopId ?? "null") +
                    " AgentIndex=" + (result?.Index.ToString() ?? "null") +
                    " Error=" + ex.GetType().FullName +
                    ": " + ex.Message,
                    persistToRuntimeBundle: false);
            }
        }

        internal static bool TrySanitizeServerCreateAgentToServerSpawnBaseline(
            CreateAgent createAgent,
            out string reason)
        {
            reason = "server-create-agent-state-unavailable";
            if (!GameNetwork.IsServer || createAgent == null)
                return false;

            Mission currentMission = Mission.Current;
            if (currentMission == null)
            {
                reason = "server-mission-unavailable";
                return false;
            }

            EnsureServerMissionScope(currentMission);
            ServerCreateAgentExpectedState state = null;
            lock (Sync)
            {
                ServerStatesByAgentIndex.TryGetValue(createAgent.AgentIndex, out state);
            }

            if (state == null)
                return false;

            if (state.ServerSpawnSpawnEquipmentClone == null || state.ServerSpawnMissionEquipmentClone == null)
            {
                reason = "server-spawn-baseline-equipment-unavailable";
                return false;
            }

            WeaponSlotSnapshot[] actualMissionSlots = BuildMissionEquipmentWeaponSlots(createAgent.MissionEquipment);
            WeaponSlotSnapshot[] actualSpawnSlots = BuildEquipmentWeaponSlots(createAgent.SpawnEquipment);
            bool forcedBaselineRepair = false;
            if (!DoesServerSpawnStateMatchOutgoingCreateAgent(state, createAgent, actualMissionSlots, out string stateMismatchReason))
            {
                if (ShouldForceServerSpawnBaselineRepair(state, stateMismatchReason))
                {
                    forcedBaselineRepair = true;
                }
                else
                {
                    lock (Sync)
                    {
                        if (ServerStatesByAgentIndex.TryGetValue(createAgent.AgentIndex, out ServerCreateAgentExpectedState currentState) &&
                            ReferenceEquals(currentState, state))
                        {
                            ServerStatesByAgentIndex.Remove(createAgent.AgentIndex);
                        }
                    }

                    reason = stateMismatchReason ?? "stale-server-spawn-state";
                    Log(
                        "server-create-agent-onwrite-sanitize-skipped",
                        "AgentIndex=" + createAgent.AgentIndex +
                        " Reason=" + reason +
                        " ExpectedEntryId=" + (state.EntryId ?? "unknown") +
                        " ExpectedTroopId=" + (state.TroopId ?? "unknown") +
                        " PayloadCharacter=" + (createAgent.Character?.StringId ?? "null"),
                        persistToRuntimeBundle: false);
                    return false;
                }
            }

            bool missionMismatch = HasWeaponSlotMismatch(state.ServerSpawnMissionWeaponSlots, actualMissionSlots);
            bool spawnMismatch = HasWeaponSlotMismatch(state.ServerSpawnSpawnWeaponSlots, actualSpawnSlots);
            if (!missionMismatch && !spawnMismatch)
            {
                reason = "already-matches-server-spawn-baseline";
                return false;
            }

            string beforePayloadSummary = BuildCreateAgentPayloadSummary(createAgent);
            Equipment sanitizedSpawnEquipment = CloneEquipment(state.ServerSpawnSpawnEquipmentClone);
            MissionEquipment sanitizedMissionEquipment = BuildMissionEquipmentFromEquipmentClone(state.ServerSpawnMissionEquipmentClone);
            if (sanitizedSpawnEquipment == null || sanitizedMissionEquipment == null)
            {
                reason = "failed-to-build-sanitized-create-agent-baseline";
                return false;
            }

            TrySetInstanceMemberValue(createAgent, "SpawnEquipment", sanitizedSpawnEquipment);
            TrySetInstanceMemberValue(createAgent, "<SpawnEquipment>k__BackingField", sanitizedSpawnEquipment);
            TrySetInstanceMemberValue(createAgent, "MissionEquipment", sanitizedMissionEquipment);
            TrySetInstanceMemberValue(createAgent, "<MissionEquipment>k__BackingField", sanitizedMissionEquipment);

            string afterPayloadSummary = BuildCreateAgentPayloadSummary(createAgent);
            reason =
                (forcedBaselineRepair
                    ? "forced-server-spawn-baseline-reset:"
                    : "sanitized-to-server-spawn-baseline:") +
                (forcedBaselineRepair ? "stale-weapon-overlap-missing," : string.Empty) +
                (missionMismatch ? "mission-weapons-mismatch" : "mission-weapons-match") +
                "," +
                (spawnMismatch ? "spawn-weapons-mismatch" : "spawn-weapons-match");
            string details =
                "AgentIndex=" + createAgent.AgentIndex +
                " Reason=" + reason +
                " ExpectedEntryId=" + (state.EntryId ?? "unknown") +
                " ExpectedTroopId=" + (state.TroopId ?? "unknown") +
                " BeforePayload={" + beforePayloadSummary + "}" +
                " AfterPayload={" + afterPayloadSummary + "}" +
                " ServerSpawnMissionWeaponSlots={" + (state.ServerSpawnMissionWeaponSlotVector ?? "unknown") + "}" +
                " ServerSpawnSpawnWeaponSlots={" + (state.ServerSpawnSpawnWeaponSlotVector ?? "unknown") + "}";
            Log("server-create-agent-onwrite-sanitized", details, persistToRuntimeBundle: false);
            return true;
        }

        private static void EnsureServerMissionScope(Mission mission)
        {
            if (!GameNetwork.IsServer || mission == null)
                return;

            lock (Sync)
            {
                if (ReferenceEquals(_serverStateMission, mission))
                    return;

                PendingServerStatesByEntryId.Clear();
                ServerStatesByAgentIndex.Clear();
                _serverStateMission = mission;
            }
        }

        private static bool ShouldForceServerSpawnBaselineRepair(
            ServerCreateAgentExpectedState state,
            string mismatchReason)
        {
            if (state == null ||
                !state.RequiresServerSpawnBaselineOnClientCreateAgent ||
                state.CreateAgentOnWriteEventCount > 0)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(mismatchReason) &&
                   mismatchReason.StartsWith("stale-server-spawn-state:weapon-overlap-missing", StringComparison.Ordinal);
        }

        internal static void ObserveServerCreateAgentOnWrite(
            CreateAgent createAgent,
            string source)
        {
            if (!GameNetwork.IsServer || createAgent == null)
                return;

            ServerCreateAgentExpectedState state = null;
            int onWriteCount = 0;
            lock (Sync)
            {
                if (ServerStatesByAgentIndex.TryGetValue(createAgent.AgentIndex, out state) && state != null)
                {
                    state.CreateAgentOnWriteEventCount++;
                    onWriteCount = state.CreateAgentOnWriteEventCount;
                }
            }

            if (state == null)
                return;

            if (onWriteCount > 1)
                return;

            if (!IsVerboseEnabled)
                return;

            WeaponSlotSnapshot[] actualMissionSlots = BuildMissionEquipmentWeaponSlots(createAgent.MissionEquipment);
            string details =
                "AgentIndex=" + createAgent.AgentIndex +
                " OnWriteCount=" + onWriteCount +
                " " + BuildCreateAgentPayloadSummary(createAgent) +
                " MissionWeaponFamilies={" + BuildWeaponSlotFamilyVector(actualMissionSlots) + "}" +
                " MissionWeaponOriginHints={" + BuildWeaponSlotOriginHintSummary(actualMissionSlots, state?.ExpectedItemOriginById) + "}" +
                " SpawnNonWeaponSlots={" + BuildEquipmentNonWeaponSlotVector(createAgent.SpawnEquipment) + "}" +
                " ExpectedEntryId=" + (state?.EntryId ?? "unknown") +
                " ExpectedTroopId=" + (state?.TroopId ?? "unknown") +
                " ExpectedEntryWeaponSlots={" + (state?.ExpectedEntryWeaponSlotVector ?? "unknown") + "}" +
                " ExpectedPreSpawnWeaponSlots={" + (state?.ExpectedPreSpawnWeaponSlotVector ?? "unknown") + "}" +
                " ExpectedPreSpawnNonWeaponSlots={" + (state?.ExpectedPreSpawnNonWeaponSlotVector ?? "unknown") + "}" +
                " ExpectedPreSpawnMount={" + (state?.ExpectedPreSpawnMountSummary ?? "unknown") + "}" +
                " ServerSpawnMissionWeaponSlots={" + (state?.ServerSpawnMissionWeaponSlotVector ?? "unknown") + "}" +
                " ServerSpawnSpawnWeaponSlots={" + (state?.ServerSpawnSpawnWeaponSlotVector ?? "unknown") + "}" +
                " " + BuildWeaponSlotDiffSummary("OnWriteVsPreSpawn", state?.ExpectedPreSpawnWeaponSlots, actualMissionSlots) +
                " " + BuildWeaponSlotDiffSummary("OnWriteVsServerSpawn", state?.ServerSpawnMissionWeaponSlots, actualMissionSlots) +
                " PayloadDiagnostic=" + (state?.PayloadDiagnosticSummary ?? "ExactCreateAgentPayloadDiagnostic={State=unknown}") +
                " PayloadWeaponLayout=" + (state?.PayloadWeaponLayoutSummary ?? "ExactCreateAgentWeaponLayout={State=unknown}") +
                " Source=" + (source ?? "unknown");
            Log("server-create-agent-onwrite", details, persistToRuntimeBundle: false);
        }

        private static bool DoesServerSpawnStateMatchOutgoingCreateAgent(
            ServerCreateAgentExpectedState state,
            CreateAgent createAgent,
            WeaponSlotSnapshot[] actualMissionSlots,
            out string mismatchReason)
        {
            mismatchReason = null;
            if (state == null || createAgent == null)
            {
                mismatchReason = "server-spawn-state-unavailable";
                return false;
            }

            string payloadCharacterId = createAgent.Character?.StringId ?? string.Empty;
            string expectedCharacterId = state.ServerSpawnCharacterId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(expectedCharacterId) &&
                !string.Equals(payloadCharacterId, expectedCharacterId, StringComparison.Ordinal))
            {
                mismatchReason =
                    "stale-server-spawn-state:character-mismatch:" +
                    "payload=" + payloadCharacterId +
                    ",expected=" + expectedCharacterId;
                return false;
            }

            bool payloadMounted =
                createAgent.MountAgentIndex >= 0 ||
                createAgent.SpawnEquipment?[EquipmentIndex.Horse].Item != null ||
                createAgent.SpawnEquipment?[EquipmentIndex.HorseHarness].Item != null;
            if (payloadMounted != state.ServerSpawnMounted)
            {
                mismatchReason =
                    "stale-server-spawn-state:mounted-mismatch:" +
                    "payload=" + payloadMounted +
                    ",expected=" + state.ServerSpawnMounted;
                return false;
            }

            if (actualMissionSlots != null &&
                state.ServerSpawnMissionWeaponSlots != null &&
                actualMissionSlots.Any(slot => slot != null && !string.IsNullOrWhiteSpace(slot.ItemId)) &&
                state.ServerSpawnMissionWeaponSlots.Any(slot => slot != null && !string.IsNullOrWhiteSpace(slot.ItemId)) &&
                !HasAnyWeaponSlotItemOverlap(actualMissionSlots, state.ServerSpawnMissionWeaponSlots))
            {
                mismatchReason = "stale-server-spawn-state:weapon-overlap-missing";
                return false;
            }

            return true;
        }

        private static bool HasAnyWeaponSlotItemOverlap(
            IEnumerable<WeaponSlotSnapshot> left,
            IEnumerable<WeaponSlotSnapshot> right)
        {
            if (left == null || right == null)
                return false;

            HashSet<string> leftItems = new HashSet<string>(
                left.Where(slot => slot != null && !string.IsNullOrWhiteSpace(slot.ItemId))
                    .Select(slot => slot.ItemId),
                StringComparer.Ordinal);
            if (leftItems.Count <= 0)
                return false;

            foreach (WeaponSlotSnapshot slot in right)
            {
                if (slot != null &&
                    !string.IsNullOrWhiteSpace(slot.ItemId) &&
                    leftItems.Contains(slot.ItemId))
                {
                    return true;
                }
            }

            return false;
        }

        internal static void ObserveClientCreateAgentOnRead(
            CreateAgent createAgent,
            bool bufferReadValid,
            bool snapshotReady,
            string snapshotReadinessSummary,
            string source)
        {
            if (GameNetwork.IsServer || createAgent == null)
                return;

            ClientCreateAgentCorridorState state = GetOrCreateState(createAgent.AgentIndex);
            int onReadCount;
            lock (Sync)
            {
                state.CreateAgentOnReadEventCount++;
                state.LastObservedUtc = DateTime.UtcNow;
                onReadCount = state.CreateAgentOnReadEventCount;
            }

            if (!IsVerboseEnabled)
                return;

            WeaponSlotSnapshot[] actualMissionSlots = BuildMissionEquipmentWeaponSlots(createAgent.MissionEquipment);
            if (!HasSuspiciousNonWeaponFamilies(actualMissionSlots))
                return;

            if (onReadCount > 1)
                return;

            string details =
                "AgentIndex=" + createAgent.AgentIndex +
                " BufferReadValid=" + bufferReadValid +
                " SnapshotReady=" + snapshotReady +
                " SnapshotReadiness=" + (snapshotReadinessSummary ?? "unknown") +
                " OnReadCount=" + onReadCount +
                " " + BuildCreateAgentPayloadSummary(createAgent) +
                " MissionWeaponFamilies={" + BuildWeaponSlotFamilyVector(actualMissionSlots) + "}" +
                " Source=" + (source ?? "unknown");
            Log("client-create-agent-onread", details, persistToRuntimeBundle: false);
        }

        internal static void ObserveClientCreateAgentPrefix(
            CreateAgent createAgent,
            bool snapshotReady,
            string snapshotReadinessSummary,
            bool strictExactCandidate,
            bool mountedHeroPayloadCandidate,
            string source)
        {
            if (GameNetwork.IsServer || createAgent == null)
                return;

            string snapshotSummary =
                "SnapshotReady=" + snapshotReady +
                " SnapshotReadiness=" + (snapshotReadinessSummary ?? "unknown");
            ClientCreateAgentCorridorState state = GetOrCreateState(createAgent.AgentIndex);
            if (!IsVerboseEnabled)
            {
                lock (Sync)
                {
                    state.SnapshotReadinessSummary = snapshotSummary;
                    state.LastObservedUtc = DateTime.UtcNow;
                }

                return;
            }

            string payloadSummary = BuildCreateAgentPayloadSummary(createAgent);
            string candidateSummary = BuildCreateAgentCandidateSummary(
                createAgent,
                out string candidateEntryId,
                out string candidatePayloadComparisonSummary);
            lock (Sync)
            {
                state.LastPayloadSummary = payloadSummary;
                state.CandidateSummary = candidateSummary;
                state.CandidateEntryId = candidateEntryId;
                state.CandidatePayloadComparisonSummary = candidatePayloadComparisonSummary;
                state.SnapshotReadinessSummary = snapshotSummary;
                state.LastObservedUtc = DateTime.UtcNow;
            }

            string details =
                "AgentIndex=" + createAgent.AgentIndex +
                " " + payloadSummary +
                " " + snapshotSummary +
                " StrictExactCandidate=" + strictExactCandidate +
                " MountedHeroPayloadCandidate=" + mountedHeroPayloadCandidate +
                " " + candidateSummary +
                " " + candidatePayloadComparisonSummary +
                " Source=" + (source ?? "unknown");
            Log("client-create-agent-prefix", details, persistToRuntimeBundle: false);
        }

        internal static void ObserveClientCreateAgentBypass(
            CreateAgent createAgent,
            string reason,
            string source)
        {
            if (GameNetwork.IsServer || createAgent == null)
                return;

            ClientCreateAgentCorridorState state = GetOrCreateState(createAgent.AgentIndex);
            if (!IsVerboseEnabled)
            {
                lock (Sync)
                {
                    state.LastBypassReason = reason ?? "unknown";
                    state.LastObservedUtc = DateTime.UtcNow;
                }

                return;
            }

            string payloadSummary = BuildCreateAgentPayloadSummary(createAgent);
            string candidateSummary = BuildCreateAgentCandidateSummary(
                createAgent,
                out string candidateEntryId,
                out string candidatePayloadComparisonSummary);
            lock (Sync)
            {
                state.LastPayloadSummary = payloadSummary;
                state.CandidateSummary = candidateSummary;
                state.CandidateEntryId = candidateEntryId;
                state.CandidatePayloadComparisonSummary = candidatePayloadComparisonSummary;
                state.LastBypassReason = reason ?? "unknown";
                state.LastObservedUtc = DateTime.UtcNow;
            }

            string details =
                "AgentIndex=" + createAgent.AgentIndex +
                " Reason=" + (reason ?? "unknown") +
                " " + payloadSummary +
                " " + candidateSummary +
                " " + candidatePayloadComparisonSummary +
                " Source=" + (source ?? "unknown");
            Log("client-create-agent-bypass", details, persistToRuntimeBundle: false);
        }

        internal static void ObserveClientCreateAgentMutation(
            CreateAgent createAgent,
            string mutationSummary,
            string source)
        {
            if (GameNetwork.IsServer || createAgent == null)
                return;

            ClientCreateAgentCorridorState state = GetOrCreateState(createAgent.AgentIndex);
            if (!IsVerboseEnabled)
            {
                lock (Sync)
                {
                    state.LastMutationSummary = mutationSummary;
                    state.LastObservedUtc = DateTime.UtcNow;
                }

                return;
            }

            string payloadSummary = BuildCreateAgentPayloadSummary(createAgent);
            string candidateSummary = BuildCreateAgentCandidateSummary(
                createAgent,
                out string candidateEntryId,
                out string candidatePayloadComparisonSummary);
            lock (Sync)
            {
                state.LastPayloadSummary = payloadSummary;
                state.CandidateSummary = candidateSummary;
                state.CandidateEntryId = candidateEntryId;
                state.CandidatePayloadComparisonSummary = candidatePayloadComparisonSummary;
                state.LastMutationSummary = mutationSummary;
                state.LastObservedUtc = DateTime.UtcNow;
            }

            string details =
                "AgentIndex=" + createAgent.AgentIndex +
                " Mutation={" + (mutationSummary ?? "none") + "}" +
                " PayloadAfter={" + payloadSummary + "}" +
                " " + candidateSummary +
                " " + candidatePayloadComparisonSummary +
                " Source=" + (source ?? "unknown");
            Log("client-create-agent-mutation", details, persistToRuntimeBundle: false);
        }

        internal static void ObserveClientCreateAgentPostfix(
            CreateAgent createAgent,
            Agent agent,
            bool snapshotReady,
            string snapshotReadinessSummary,
            bool exactVisualApplied,
            string source)
        {
            if (GameNetwork.IsServer || createAgent == null)
                return;

            ClientCreateAgentCorridorState state = GetOrCreateState(createAgent.AgentIndex);
            if (!IsVerboseEnabled)
            {
                lock (Sync)
                {
                    state.CreateAgentPostfixObserved = true;
                    state.LastObservedUtc = DateTime.UtcNow;
                }

                return;
            }

            string resolvedEntrySummary = BuildResolvedEntrySummary(agent, out string resolvedEntryId);
            string candidatePayloadComparisonSummary;
            lock (Sync)
            {
                state.CreateAgentPostfixObserved = true;
                state.LastResolvedEntrySummary = resolvedEntrySummary;
                state.LastResolvedEntryId = resolvedEntryId;
                state.LastObservedUtc = DateTime.UtcNow;
                candidatePayloadComparisonSummary =
                    state.CandidatePayloadComparisonSummary ?? "PayloadCompare={State=unknown}";
            }

            string details =
                "AgentIndex=" + createAgent.AgentIndex +
                " SnapshotReady=" + snapshotReady +
                " SnapshotReadiness=" + (snapshotReadinessSummary ?? "unknown") +
                " ExactVisualApplied=" + exactVisualApplied +
                " Payload={" + (state.LastPayloadSummary ?? BuildCreateAgentPayloadSummary(createAgent)) + "}" +
                " Candidate={" + (state.CandidateSummary ?? BuildCreateAgentCandidateSummary(createAgent, out _, out _)) + "}" +
                " " + candidatePayloadComparisonSummary +
                " ResolvedEntry={" + resolvedEntrySummary + "}" +
                " Agent={" + BuildAgentSummary(agent) + "}" +
                " Source=" + (source ?? "unknown");
            Log("client-create-agent-postfix", details, persistToRuntimeBundle: false);
        }

        internal static void ObserveClientDeferredCreateAgentStage(
            CreateAgent createAgent,
            Agent agent,
            string stage,
            int attempts,
            string source)
        {
            if (GameNetwork.IsServer || createAgent == null || !IsVerboseEnabled)
                return;

            string details =
                "AgentIndex=" + createAgent.AgentIndex +
                " Stage=" + (stage ?? "unknown") +
                " Attempts=" + attempts +
                " Payload={" + BuildCreateAgentPayloadSummary(createAgent) + "}" +
                " MaterializedAgent={" + BuildAgentSummary(agent) + "}" +
                " Source=" + (source ?? "unknown");
            Log("client-deferred-create-agent-stage", details, persistToRuntimeBundle: false);
        }

        internal static void ObserveClientAgentVisualsNetworkMessage(
            GameNetworkMessage baseMessage,
            string stage,
            string source)
        {
            if (!GameNetwork.IsClient || GameNetwork.IsServer || !IsVerboseEnabled || baseMessage == null)
                return;

            try
            {
                NetworkCommunicator peer = null;
                int? visualsIndex = null;
                string messageSummary;
                if (baseMessage is CreateAgentVisuals createAgentVisuals)
                {
                    peer = createAgentVisuals.Peer;
                    visualsIndex = createAgentVisuals.VisualsIndex;
                    messageSummary =
                        "CreateAgentVisuals={Peer=" + DescribePeer(peer) +
                        ",VisualsIndex=" + createAgentVisuals.VisualsIndex +
                        ",CharacterId=" + (createAgentVisuals.Character?.StringId ?? "null") +
                        ",SelectedEquipmentSetIndex=" + createAgentVisuals.SelectedEquipmentSetIndex +
                        ",TroopCountInFormation=" + createAgentVisuals.TroopCountInFormation +
                        ",Weapons={" + ExactCreateAgentPayloadDiagnostics.BuildEquipmentWeaponLayoutSummary(createAgentVisuals.Equipment) +
                        "}}";
                }
                else if (baseMessage is RemoveAgentVisualsForPeer removeAll)
                {
                    peer = removeAll.Peer;
                    messageSummary = "RemoveAgentVisualsForPeer={Peer=" + DescribePeer(peer) + "}";
                }
                else if (baseMessage is RemoveAgentVisualsFromIndexForPeer removeIndex)
                {
                    peer = removeIndex.Peer;
                    visualsIndex = removeIndex.VisualsIndex;
                    messageSummary =
                        "RemoveAgentVisualsFromIndexForPeer={Peer=" + DescribePeer(peer) +
                        ",VisualsIndex=" + removeIndex.VisualsIndex + "}";
                }
                else
                {
                    return;
                }

                MissionPeer missionPeer = peer?.GetComponent<MissionPeer>();
                string visualState = visualsIndex.HasValue
                    ? BuildPeerPreviewVisualSummary(missionPeer, visualsIndex.Value)
                    : BuildAllPeerPreviewVisualsSummary(missionPeer);
                Log(
                    "client-agent-visuals-network-message",
                    "Stage=" + (stage ?? "unknown") +
                    " Message={" + messageSummary + "}" +
                    " MissionPeer={TeamIndex=" + (missionPeer?.Team?.TeamIndex.ToString() ?? "null") +
                    ",TeamSide=" + (missionPeer?.Team?.Side.ToString() ?? "null") +
                    ",HasSpawnedAgentVisuals=" + (missionPeer?.HasSpawnedAgentVisuals.ToString() ?? "null") +
                    ",ControlledAgentIndex=" + (missionPeer?.ControlledAgent?.Index.ToString() ?? "null") + "}" +
                    " VisualState={" + visualState + "}" +
                    " Source=" + (source ?? "unknown"),
                    persistToRuntimeBundle: false);
            }
            catch (Exception ex)
            {
                Log(
                    "client-agent-visuals-network-message-error",
                    "Stage=" + (stage ?? "unknown") +
                    " MessageType=" + baseMessage.GetType().FullName +
                    " Error=" + ex.GetType().Name + ":" + ex.Message +
                    " Source=" + (source ?? "unknown"),
                    persistToRuntimeBundle: false);
            }
        }

        internal static void TrySampleServerAgentPositions(Mission mission, string source)
        {
            if (!GameNetwork.IsServer || !IsVerboseEnabled || mission == null ||
                !IsSupportedAgentPositionDiagnosticScene(mission.SceneName))
            {
                return;
            }

            DateTime nowUtc = DateTime.UtcNow;
            CoopBattlePhase phase = CoopBattlePhaseRuntimeState.GetPhase();
            lock (Sync)
            {
                if (!ReferenceEquals(_serverPositionMission, mission))
                {
                    ServerAgentPositionStates.Clear();
                    _serverPositionMission = mission;
                    _nextServerPositionSampleUtc = DateTime.MinValue;
                    _serverBattleActiveObservedUtc = DateTime.MinValue;
                    _lastServerPositionPhase = null;
                }

                if (phase >= CoopBattlePhase.BattleEnded)
                    return;
                if (phase >= CoopBattlePhase.BattleActive)
                {
                    if (_serverBattleActiveObservedUtc == DateTime.MinValue)
                        _serverBattleActiveObservedUtc = nowUtc;
                    if (nowUtc - _serverBattleActiveObservedUtc > ServerPositionBattleActiveWindow)
                        return;
                }

                if (nowUtc < _nextServerPositionSampleUtc)
                    return;
                _nextServerPositionSampleUtc = nowUtc + ServerPositionSampleInterval;
            }

            string phaseName = phase.ToString();
            bool phaseChanged;
            lock (Sync)
            {
                phaseChanged = !string.Equals(_lastServerPositionPhase, phaseName, StringComparison.Ordinal);
                if (phaseChanged)
                    _lastServerPositionPhase = phaseName;
            }

            if (phaseChanged)
            {
                Log(
                    "server-position-sample-phase",
                    "Phase=" + phaseName +
                    " Mission=" + (mission.SceneName ?? "null") +
                    " Source=" + (source ?? "unknown"),
                    persistToRuntimeBundle: false);
            }

            SampleServerLiveAgents(mission, phaseName, source);
        }

        internal static void ObserveManagedAgentTeleport(Agent agent, Vec3 targetPosition, string source)
        {
            if (!IsVerboseEnabled || agent?.Mission == null ||
                !IsSupportedAgentPositionDiagnosticScene(agent.Mission.SceneName))
            {
                return;
            }

            try
            {
                Vec3 currentPosition = agent.Position;
                float distanceSquared = DistanceSquared(currentPosition, targetPosition);
                float thresholdSquared =
                    ManagedTeleportDiagnosticThreshold * ManagedTeleportDiagnosticThreshold;
                if (distanceSquared < thresholdSquared)
                    return;

                string stackTrace = (Environment.StackTrace ?? string.Empty)
                    .Replace("\r", string.Empty)
                    .Replace("\n", " <- ");
                if (stackTrace.Length > 4096)
                    stackTrace = stackTrace.Substring(0, 4096);

                string processRole = GameNetwork.IsServer
                    ? "server"
                    : GameNetwork.IsClient
                        ? "client"
                        : "offline";
                Log(
                    "managed-agent-teleport",
                    "Role=" + processRole +
                    " Phase=" + CoopBattlePhaseRuntimeState.GetPhase() +
                    " AgentIndex=" + agent.Index +
                    " CharacterId=" + ((agent.Character as BasicCharacterObject)?.StringId ?? "null") +
                    " TeamIndex=" + (agent.Team?.TeamIndex.ToString() ?? "null") +
                    " TeamSide=" + (agent.Team?.Side.ToString() ?? "null") +
                    " FormationIndex=" + (agent.Formation?.Index.ToString() ?? "null") +
                    " CurrentPosition=" + FormatVec3(currentPosition) +
                    " TargetPosition=" + FormatVec3(targetPosition) +
                    " Distance=" + Math.Sqrt(distanceSquared).ToString("0.###", CultureInfo.InvariantCulture) +
                    " Formation={" + BuildFormationPositionSummary(agent.Formation, currentPosition.AsVec2) + "}" +
                    " Source=" + (source ?? "unknown") +
                    " StackTrace=" + stackTrace,
                    persistToRuntimeBundle: false);
            }
            catch (Exception ex)
            {
                Log(
                    "managed-agent-teleport-observer-error",
                    "AgentIndex=" + (agent?.Index.ToString() ?? "null") +
                    " Error=" + ex.GetType().Name + ":" + ex.Message +
                    " Source=" + (source ?? "unknown"),
                    persistToRuntimeBundle: false);
            }
        }

        internal static object CaptureClientNativeMissionTickEntry(Mission mission, string source)
        {
            if (!GameNetwork.IsClient || GameNetwork.IsServer || !IsVerboseEnabled || mission == null ||
                !IsSupportedAgentPositionDiagnosticScene(mission.SceneName))
            {
                return null;
            }

            try
            {
                Dictionary<int, ClientNativeMissionTickAgentState> previousExitStates;
                long sequence;
                lock (Sync)
                {
                    if (!ReferenceEquals(_clientNativeMissionTickMission, mission))
                    {
                        _clientNativeMissionTickMission = mission;
                        _clientNativeMissionTickExitStates =
                            new Dictionary<int, ClientNativeMissionTickAgentState>();
                        _clientNativeMissionTickSequence = 0L;
                    }

                    sequence = ++_clientNativeMissionTickSequence;
                    previousExitStates = _clientNativeMissionTickExitStates;
                }

                Dictionary<int, ClientNativeMissionTickAgentState> entryStates =
                    ReadClientNativeMissionTickAgentStates(mission);
                ObserveClientNativeMissionTickPositionTransitions(
                    mission,
                    previousExitStates,
                    entryStates,
                    sequence,
                    "previous-native-tick-exit-to-current-entry",
                    source);
                return new ClientNativeMissionTickBoundaryState
                {
                    Sequence = sequence,
                    EntryStates = entryStates
                };
            }
            catch (Exception ex)
            {
                Log(
                    "client-native-mission-tick-entry-error",
                    "Error=" + ex.GetType().Name + ":" + ex.Message +
                    " Source=" + (source ?? "unknown"),
                    persistToRuntimeBundle: false);
                return null;
            }
        }

        internal static void ObserveClientNativeMissionTickExit(
            Mission mission,
            object boundaryState,
            string source)
        {
            if (!GameNetwork.IsClient || GameNetwork.IsServer || !IsVerboseEnabled || mission == null ||
                !(boundaryState is ClientNativeMissionTickBoundaryState captured) ||
                !IsSupportedAgentPositionDiagnosticScene(mission.SceneName))
            {
                return;
            }

            try
            {
                Dictionary<int, ClientNativeMissionTickAgentState> exitStates =
                    ReadClientNativeMissionTickAgentStates(mission);
                ObserveClientNativeMissionTickPositionTransitions(
                    mission,
                    captured.EntryStates,
                    exitStates,
                    captured.Sequence,
                    "inside-native-mission-tick",
                    source);
                lock (Sync)
                {
                    if (ReferenceEquals(_clientNativeMissionTickMission, mission))
                        _clientNativeMissionTickExitStates = exitStates;
                }
            }
            catch (Exception ex)
            {
                Log(
                    "client-native-mission-tick-exit-error",
                    "Sequence=" + captured.Sequence +
                    " Error=" + ex.GetType().Name + ":" + ex.Message +
                    " Source=" + (source ?? "unknown"),
                    persistToRuntimeBundle: false);
            }
        }

        internal static object CaptureClientNativeExecutionBoundaryEntry(
            Mission mission,
            string boundary,
            string source)
        {
            if (!GameNetwork.IsClient || GameNetwork.IsServer || !IsVerboseEnabled || mission == null ||
                !IsSupportedAgentPositionDiagnosticScene(mission.SceneName))
            {
                return null;
            }

            try
            {
                long sequence;
                lock (Sync)
                {
                    sequence = ++_clientNativeExecutionBoundarySequence;
                }

                return new ClientNativeExecutionBoundaryState
                {
                    Mission = mission,
                    Sequence = sequence,
                    Boundary = boundary ?? "unknown",
                    ManagedThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId,
                    EntryStates = ReadClientNativeMissionTickAgentStates(mission)
                };
            }
            catch (Exception ex)
            {
                Log(
                    "client-native-execution-boundary-entry-error",
                    "Boundary=" + (boundary ?? "unknown") +
                    " Error=" + ex.GetType().Name + ":" + ex.Message +
                    " Source=" + (source ?? "unknown"),
                    persistToRuntimeBundle: false);
                return null;
            }
        }

        internal static void ObserveClientNativeExecutionBoundaryExit(
            Mission mission,
            object boundaryState,
            string source)
        {
            if (!GameNetwork.IsClient || GameNetwork.IsServer || !IsVerboseEnabled || mission == null ||
                !(boundaryState is ClientNativeExecutionBoundaryState captured) ||
                !ReferenceEquals(captured.Mission, mission) ||
                !IsSupportedAgentPositionDiagnosticScene(mission.SceneName))
            {
                return;
            }

            try
            {
                Dictionary<int, ClientNativeMissionTickAgentState> exitStates =
                    ReadClientNativeMissionTickAgentStates(mission);
                ObserveClientNativeMissionTickPositionTransitions(
                    mission,
                    captured.EntryStates,
                    exitStates,
                    captured.Sequence,
                    "inside-" + captured.Boundary,
                    source,
                    "client-native-execution-boundary-position-transition",
                    " EntryThreadId=" + captured.ManagedThreadId +
                    " ExitThreadId=" + System.Threading.Thread.CurrentThread.ManagedThreadId);
            }
            catch (Exception ex)
            {
                Log(
                    "client-native-execution-boundary-exit-error",
                    "Boundary=" + (captured.Boundary ?? "unknown") +
                    " Sequence=" + captured.Sequence +
                    " EntryThreadId=" + captured.ManagedThreadId +
                    " ExitThreadId=" + System.Threading.Thread.CurrentThread.ManagedThreadId +
                    " Error=" + ex.GetType().Name + ":" + ex.Message +
                    " Source=" + (source ?? "unknown"),
                    persistToRuntimeBundle: false);
            }
        }

        private static Dictionary<int, ClientNativeMissionTickAgentState> ReadClientNativeMissionTickAgentStates(
            Mission mission)
        {
            var states = new Dictionary<int, ClientNativeMissionTickAgentState>();
            if (mission?.AllAgents == null)
                return states;

            foreach (Agent agent in mission.AllAgents)
            {
                if (agent == null || agent.IsMount || !agent.IsActive())
                    continue;

                try
                {
                    states[agent.Index] = new ClientNativeMissionTickAgentState
                    {
                        AgentIndex = agent.Index,
                        CharacterId = (agent.Character as BasicCharacterObject)?.StringId,
                        Position = agent.Position,
                        AgentPointer = ReadAgentNativePointer(AgentNativePointerField, agent),
                        PositionPointer = ReadAgentNativePointer(AgentPositionPointerField, agent),
                        TeamIndex = agent.Team?.TeamIndex ?? -1,
                        TeamSide = agent.Team?.Side.ToString() ?? "null",
                        FormationIndex = agent.Formation?.Index ?? -1,
                        Controller = agent.Controller,
                        IsAiControlled = agent.IsAIControlled,
                        IsMainAgent = agent.IsMainAgent
                    };
                }
                catch (Exception ex)
                {
                    Log(
                        "client-native-mission-tick-agent-read-error",
                        "AgentIndex=" + agent.Index +
                        " Error=" + ex.GetType().Name + ":" + ex.Message,
                        persistToRuntimeBundle: false);
                }
            }

            return states;
        }

        private static void ObserveClientNativeMissionTickPositionTransitions(
            Mission mission,
            IReadOnlyDictionary<int, ClientNativeMissionTickAgentState> previousStates,
            IReadOnlyDictionary<int, ClientNativeMissionTickAgentState> currentStates,
            long sequence,
            string boundary,
            string source,
            string eventName = "client-native-mission-tick-position-transition",
            string additionalFields = null)
        {
            if (previousStates == null || currentStates == null || previousStates.Count == 0)
                return;

            float thresholdSquared =
                ClientNativeMissionTickLargeMovementThreshold * ClientNativeMissionTickLargeMovementThreshold;
            foreach (KeyValuePair<int, ClientNativeMissionTickAgentState> pair in currentStates)
            {
                ClientNativeMissionTickAgentState current = pair.Value;
                if (current == null ||
                    !previousStates.TryGetValue(pair.Key, out ClientNativeMissionTickAgentState previous) ||
                    previous == null)
                {
                    continue;
                }

                float distanceSquared = DistanceSquared(previous.Position, current.Position);
                if (distanceSquared < thresholdSquared)
                    continue;

                Log(
                    eventName ?? "client-native-mission-tick-position-transition",
                    "Boundary=" + (boundary ?? "unknown") +
                    " Sequence=" + sequence +
                    " Phase=" + CoopBattlePhaseRuntimeState.GetPhase() +
                    " AgentIndex=" + current.AgentIndex +
                    " CharacterId=" + (current.CharacterId ?? "null") +
                    " PreviousPosition=" + FormatVec3(previous.Position) +
                    " Position=" + FormatVec3(current.Position) +
                    " MovementDistance=" + Math.Sqrt(distanceSquared).ToString("0.###", CultureInfo.InvariantCulture) +
                    " PreviousNative={" + BuildClientNativeMissionTickAgentStateSummary(previous) + "}" +
                    " CurrentNative={" + BuildClientNativeMissionTickAgentStateSummary(current) + "}" +
                    " AgentPointerChanged=" + (previous.AgentPointer != current.AgentPointer) +
                    " PositionPointerChanged=" + (previous.PositionPointer != current.PositionPointer) +
                    " ClosestOtherCurrentAgent={" + BuildClosestOtherNativeAgentSummary(
                        currentStates,
                        current.Position,
                        current.AgentIndex) + "}" +
                    " LocalOwnership={" + BuildClientLocalOwnershipSummary(mission) + "}" +
                    (additionalFields ?? string.Empty) +
                    " Source=" + (source ?? "unknown"),
                    persistToRuntimeBundle: false);
            }
        }

        private static ulong ReadAgentNativePointer(FieldInfo field, Agent agent)
        {
            if (field == null || agent == null)
                return 0UL;

            object value = field.GetValue(agent);
            return value is UIntPtr pointer ? pointer.ToUInt64() : 0UL;
        }

        private static string BuildClientNativeMissionTickAgentStateSummary(
            ClientNativeMissionTickAgentState state)
        {
            if (state == null)
                return "State=absent";

            return
                "AgentIndex=" + state.AgentIndex +
                ",CharacterId=" + (state.CharacterId ?? "null") +
                ",Position=" + FormatVec3(state.Position) +
                ",AgentPointer=" + FormatNativePointer(state.AgentPointer) +
                ",PositionPointer=" + FormatNativePointer(state.PositionPointer) +
                ",TeamIndex=" + state.TeamIndex +
                ",TeamSide=" + (state.TeamSide ?? "null") +
                ",FormationIndex=" + state.FormationIndex +
                ",Controller=" + state.Controller +
                ",IsAiControlled=" + state.IsAiControlled +
                ",IsMainAgent=" + state.IsMainAgent;
        }

        private static string BuildClosestOtherNativeAgentSummary(
            IReadOnlyDictionary<int, ClientNativeMissionTickAgentState> states,
            Vec3 position,
            int excludedAgentIndex)
        {
            ClientNativeMissionTickAgentState closest = null;
            float closestDistanceSquared = float.MaxValue;
            if (states != null)
            {
                foreach (ClientNativeMissionTickAgentState state in states.Values)
                {
                    if (state == null || state.AgentIndex == excludedAgentIndex)
                        continue;

                    float distanceSquared = DistanceSquared(position, state.Position);
                    if (distanceSquared >= closestDistanceSquared)
                        continue;

                    closest = state;
                    closestDistanceSquared = distanceSquared;
                }
            }

            return closest == null
                ? "State=none"
                : BuildClientNativeMissionTickAgentStateSummary(closest) +
                  ",Distance=" + Math.Sqrt(closestDistanceSquared).ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string BuildClientLocalOwnershipSummary(Mission mission)
        {
            try
            {
                MissionPeer localMissionPeer = GameNetwork.MyPeer?.GetComponent<MissionPeer>();
                return
                    "AgentMainIndex=" + (Agent.Main?.Index.ToString() ?? "null") +
                    ",MainAgentServerIndex=" + (mission?.MainAgentServer?.Index.ToString() ?? "null") +
                    ",PeerControlledAgentIndex=" + (localMissionPeer?.ControlledAgent?.Index.ToString() ?? "null") +
                    ",PeerFollowedAgentIndex=" + (localMissionPeer?.FollowedAgent?.Index.ToString() ?? "null") +
                    ",NetworkControlledAgentIndex=" + (GameNetwork.MyPeer?.ControlledAgent?.Index.ToString() ?? "null");
            }
            catch (Exception ex)
            {
                return "State=unavailable,Error=" + ex.GetType().Name + ":" + ex.Message;
            }
        }

        private static string FormatNativePointer(ulong value)
        {
            return value == 0UL ? "null" : "0x" + value.ToString("X", CultureInfo.InvariantCulture);
        }

        private static bool IsSupportedAgentPositionDiagnosticScene(string sceneName)
        {
            return SceneRuntimeClassifier.IsExactCampaignBattleScene(sceneName ?? string.Empty) ||
                   SceneRuntimeClassifier.IsValidatedLordsHallScene(sceneName ?? string.Empty);
        }

        private static void SampleServerLiveAgents(Mission mission, string phaseName, string source)
        {
            var observedAgentIndices = new HashSet<int>();
            if (mission.AllAgents != null)
            {
                foreach (Agent agent in mission.AllAgents)
                {
                    if (agent == null || agent.IsMount || !agent.IsActive())
                        continue;

                    observedAgentIndices.Add(agent.Index);
                    try
                    {
                        var current = new ServerAgentPositionState
                        {
                            Position = agent.Position,
                            TeamIndex = agent.Team?.TeamIndex ?? -1,
                            FormationIndex = agent.Formation?.Index ?? -1
                        };
                        ServerAgentPositionState previous;
                        lock (Sync)
                        {
                            ServerAgentPositionStates.TryGetValue(agent.Index, out previous);
                            ServerAgentPositionStates[agent.Index] = current;
                        }

                        float distanceSquared = previous == null
                            ? 0f
                            : DistanceSquared(previous.Position, current.Position);
                        float thresholdSquared =
                            ServerPositionLargeMovementThreshold * ServerPositionLargeMovementThreshold;
                        bool assignmentChanged = previous != null &&
                            (previous.TeamIndex != current.TeamIndex ||
                             previous.FormationIndex != current.FormationIndex);
                        if (previous != null && distanceSquared < thresholdSquared && !assignmentChanged)
                            continue;

                        string trigger = previous == null
                            ? "baseline"
                            : distanceSquared >= thresholdSquared
                                ? "large-position-jump"
                                : "assignment-changed";
                        Log(
                            "server-live-agent-position-sample",
                            "Trigger=" + trigger +
                            " Phase=" + phaseName +
                            " AgentIndex=" + agent.Index +
                            " CharacterId=" + ((agent.Character as BasicCharacterObject)?.StringId ?? "null") +
                            " TeamIndex=" + current.TeamIndex +
                            " TeamSide=" + (agent.Team?.Side.ToString() ?? "null") +
                            " FormationIndex=" + current.FormationIndex +
                            " PreviousPosition=" + (previous == null ? "unavailable" : FormatVec3(previous.Position)) +
                            " Position=" + FormatVec3(current.Position) +
                            " MovementDistance=" +
                            (previous == null
                                ? "unavailable"
                                : Math.Sqrt(distanceSquared).ToString("0.###", CultureInfo.InvariantCulture)) +
                            " Formation={" + BuildFormationPositionSummary(agent.Formation, current.Position.AsVec2) + "}" +
                            " Source=" + (source ?? "unknown"),
                            persistToRuntimeBundle: false);
                    }
                    catch (Exception ex)
                    {
                        Log(
                            "server-live-agent-position-sample-error",
                            "AgentIndex=" + agent.Index +
                            " Error=" + ex.GetType().Name + ":" + ex.Message +
                            " Source=" + (source ?? "unknown"),
                            persistToRuntimeBundle: false);
                    }
                }
            }

            lock (Sync)
            {
                foreach (int removedAgentIndex in ServerAgentPositionStates.Keys
                             .Where(index => !observedAgentIndices.Contains(index))
                             .ToList())
                {
                    ServerAgentPositionStates.Remove(removedAgentIndex);
                }
            }
        }

        internal static void TrySampleClientAgentAndPreviewVisualPositions(Mission mission, string source)
        {
            if (!GameNetwork.IsClient || GameNetwork.IsServer || !IsVerboseEnabled || mission == null)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            CoopBattlePhase phase = CoopBattlePhaseRuntimeState.GetPhase();
            lock (Sync)
            {
                if (!ReferenceEquals(_clientPositionVisualMission, mission))
                {
                    ClientAgentPositionVisualStates.Clear();
                    ClientPeerPreviewVisualStates.Clear();
                    _clientPositionVisualMission = mission;
                    _nextClientPositionVisualSampleUtc = DateTime.MinValue;
                    _clientBattleActiveObservedUtc = DateTime.MinValue;
                    _lastClientPositionVisualPhase = null;
                }

                if (phase >= CoopBattlePhase.BattleEnded)
                    return;
                if (phase >= CoopBattlePhase.BattleActive)
                {
                    if (_clientBattleActiveObservedUtc == DateTime.MinValue)
                        _clientBattleActiveObservedUtc = nowUtc;
                    if (nowUtc - _clientBattleActiveObservedUtc > ClientPositionVisualBattleActiveWindow)
                        return;
                }

                if (nowUtc < _nextClientPositionVisualSampleUtc)
                    return;
                _nextClientPositionVisualSampleUtc = nowUtc + ClientPositionVisualSampleInterval;
            }

            string phaseName = phase.ToString();
            bool phaseChanged;
            lock (Sync)
            {
                phaseChanged = !string.Equals(_lastClientPositionVisualPhase, phaseName, StringComparison.Ordinal);
                if (phaseChanged)
                    _lastClientPositionVisualPhase = phaseName;
            }

            if (phaseChanged)
            {
                Log(
                    "client-position-visual-sample-phase",
                    "Phase=" + phaseName +
                    " Mission=" + (mission.SceneName ?? "null") +
                    " Source=" + (source ?? "unknown"),
                    persistToRuntimeBundle: false);
            }

            SampleClientLiveAgents(mission, phaseName, source);
            SampleClientPeerPreviewVisuals(mission, phaseName, source);
        }

        private static void SampleClientLiveAgents(Mission mission, string phaseName, string source)
        {
            var observedAgentIndices = new HashSet<int>();
            if (mission.AllAgents != null)
            {
                foreach (Agent agent in mission.AllAgents)
                {
                    if (agent == null || agent.IsMount || !agent.IsActive())
                        continue;

                    observedAgentIndices.Add(agent.Index);
                    try
                    {
                        ClientAgentPositionVisualState current = ReadClientAgentPositionVisualState(agent);
                        ClientAgentPositionVisualState previous;
                        lock (Sync)
                        {
                            ClientAgentPositionVisualStates.TryGetValue(agent.Index, out previous);
                            ClientAgentPositionVisualStates[agent.Index] = current;
                        }

                        string trigger = BuildClientAgentPositionVisualTrigger(previous, current);
                        if (trigger == null)
                            continue;

                        Log(
                            "client-live-agent-position-visual-sample",
                            "Trigger=" + trigger +
                            " Phase=" + phaseName +
                            " AgentIndex=" + agent.Index +
                            " CharacterId=" + ((agent.Character as BasicCharacterObject)?.StringId ?? "null") +
                            " TeamIndex=" + current.TeamIndex +
                            " TeamSide=" + (agent.Team?.Side.ToString() ?? "null") +
                            " FormationIndex=" + current.FormationIndex +
                            " Position=" + FormatVec3(current.Position) +
                            " VisualPosition=" + (current.HasVisualPosition ? FormatVec3(current.VisualPosition) : "unavailable") +
                            " PositionToVisualDistance=" + FormatOptionalDistance(current.Position, current.VisualPosition, current.HasVisualPosition) +
                            " VisualFrameOrigin=" + (current.HasVisualFrame ? FormatVec3(current.VisualFrameOrigin) : "unavailable") +
                            " PositionToVisualFrameDistance=" + FormatOptionalDistance(current.Position, current.VisualFrameOrigin, current.HasVisualFrame) +
                            " VisualsValid=" + current.VisualsValid +
                            " VisualsVisible=" + current.VisualsVisible +
                            " Formation={" + BuildFormationPositionSummary(agent.Formation, current.Position.AsVec2) + "}" +
                            " Source=" + (source ?? "unknown"),
                            persistToRuntimeBundle: false);
                    }
                    catch (Exception ex)
                    {
                        Log(
                            "client-live-agent-position-visual-sample-error",
                            "AgentIndex=" + agent.Index +
                            " Error=" + ex.GetType().Name + ":" + ex.Message +
                            " Source=" + (source ?? "unknown"),
                            persistToRuntimeBundle: false);
                    }
                }
            }

            List<int> removedAgentIndices;
            lock (Sync)
            {
                removedAgentIndices = ClientAgentPositionVisualStates.Keys
                    .Where(index => !observedAgentIndices.Contains(index))
                    .ToList();
                foreach (int removedAgentIndex in removedAgentIndices)
                    ClientAgentPositionVisualStates.Remove(removedAgentIndex);
            }

            foreach (int removedAgentIndex in removedAgentIndices)
            {
                Log(
                    "client-live-agent-position-visual-ended",
                    "AgentIndex=" + removedAgentIndex +
                    " Phase=" + phaseName +
                    " Source=" + (source ?? "unknown"),
                    persistToRuntimeBundle: false);
            }
        }

        private static void SampleClientPeerPreviewVisuals(Mission mission, string phaseName, string source)
        {
            var observedPreviewKeys = new HashSet<string>(StringComparer.Ordinal);
            if (GameNetwork.NetworkPeers != null)
            {
                foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
                {
                    MissionPeer missionPeer = peer?.GetComponent<MissionPeer>();
                    if (missionPeer == null)
                        continue;

                    for (int visualsIndex = 0; visualsIndex <= MaxPeerPreviewVisualIndexToSample; visualsIndex++)
                    {
                        PeerVisualsHolder holder = TryGetPeerVisuals(missionPeer, visualsIndex);

                        if (holder?.AgentVisuals == null)
                            continue;

                        string previewKey = peer.Index + "|" + visualsIndex;
                        observedPreviewKeys.Add(previewKey);
                        try
                        {
                            ClientPeerPreviewVisualState current = ReadClientPeerPreviewVisualState(holder);
                            ClientPeerPreviewVisualState previous;
                            lock (Sync)
                            {
                                ClientPeerPreviewVisualStates.TryGetValue(previewKey, out previous);
                                ClientPeerPreviewVisualStates[previewKey] = current;
                            }

                            string trigger = BuildClientPeerPreviewVisualTrigger(previous, current);
                            if (trigger == null)
                                continue;

                            Log(
                                "client-peer-preview-visual-sample",
                                "Trigger=" + trigger +
                                " Phase=" + phaseName +
                                " Peer=" + DescribePeer(peer) +
                                " TeamIndex=" + (missionPeer.Team?.TeamIndex.ToString() ?? "null") +
                                " TeamSide=" + (missionPeer.Team?.Side.ToString() ?? "null") +
                                " HasSpawnedAgentVisuals=" + missionPeer.HasSpawnedAgentVisuals +
                                " VisualsIndex=" + visualsIndex +
                                " CharacterId=" + (current.CharacterId ?? "null") +
                                " FrameOrigin=" + (current.HasFrame ? FormatVec3(current.FrameOrigin) : "unavailable") +
                                " VisualsValid=" + current.VisualsValid +
                                " VisualsVisible=" + current.VisualsVisible +
                                " ClosestLiveAgent={" + BuildClosestLiveAgentSummary(mission, current.FrameOrigin, current.HasFrame) + "}" +
                                " Source=" + (source ?? "unknown"),
                                persistToRuntimeBundle: false);
                        }
                        catch (Exception ex)
                        {
                            Log(
                                "client-peer-preview-visual-sample-error",
                                "Peer=" + DescribePeer(peer) +
                                " VisualsIndex=" + visualsIndex +
                                " Error=" + ex.GetType().Name + ":" + ex.Message +
                                " Source=" + (source ?? "unknown"),
                                persistToRuntimeBundle: false);
                        }
                    }
                }
            }

            List<string> removedPreviewKeys;
            lock (Sync)
            {
                removedPreviewKeys = ClientPeerPreviewVisualStates.Keys
                    .Where(key => !observedPreviewKeys.Contains(key))
                    .ToList();
                foreach (string removedPreviewKey in removedPreviewKeys)
                    ClientPeerPreviewVisualStates.Remove(removedPreviewKey);
            }

            foreach (string removedPreviewKey in removedPreviewKeys)
            {
                Log(
                    "client-peer-preview-visual-ended",
                    "PreviewKey=" + removedPreviewKey +
                    " Phase=" + phaseName +
                    " Source=" + (source ?? "unknown"),
                    persistToRuntimeBundle: false);
            }
        }

        internal static void ObserveClientCreateAgentException(
            CreateAgent createAgent,
            Exception exception,
            string source)
        {
            if (GameNetwork.IsServer || createAgent == null || exception == null)
                return;

            ClientCreateAgentCorridorState state = GetOrCreateState(createAgent.AgentIndex);
            string payloadSummary;
            string candidateSummary;
            string candidatePayloadComparisonSummary;
            string snapshotSummary;
            string mutationSummary;
            string bypassReason;
            bool postfixObserved;
            lock (Sync)
            {
                payloadSummary = state.LastPayloadSummary ?? BuildCreateAgentPayloadSummary(createAgent);
                candidateSummary = state.CandidateSummary ?? BuildCreateAgentCandidateSummary(createAgent, out _, out _);
                candidatePayloadComparisonSummary =
                    state.CandidatePayloadComparisonSummary ?? "PayloadCompare={State=unknown}";
                snapshotSummary = state.SnapshotReadinessSummary ?? "SnapshotReady=unknown SnapshotReadiness=unknown";
                mutationSummary = state.LastMutationSummary;
                bypassReason = state.LastBypassReason;
                postfixObserved = state.CreateAgentPostfixObserved;
                state.LastObservedUtc = DateTime.UtcNow;
            }

            string details =
                "AgentIndex=" + createAgent.AgentIndex +
                " ExceptionType=" + exception.GetType().FullName +
                " ExceptionMessage=" + exception.Message +
                " PostfixObserved=" + postfixObserved +
                " BypassReason=" + (bypassReason ?? "none") +
                " Mutation={" + (mutationSummary ?? "none") + "}" +
                " " + snapshotSummary +
                " Payload={" + payloadSummary + "}" +
                " Candidate={" + candidateSummary + "}" +
                " " + candidatePayloadComparisonSummary +
                " Source=" + (source ?? "unknown");
            Log("client-create-agent-exception", details, persistToRuntimeBundle: true);
        }

        internal static void ObserveClientSynchronizeAgentEquipment(
            SynchronizeAgentSpawnEquipment message,
            Agent agent,
            string source)
        {
            if (GameNetwork.IsServer || message == null)
                return;

            ClientCreateAgentCorridorState state = GetOrCreateState(message.AgentIndex);
            int syncCount;
            lock (Sync)
            {
                state.EquipmentSyncEventCount++;
                state.LastObservedUtc = DateTime.UtcNow;
                syncCount = state.EquipmentSyncEventCount;
            }

            if (syncCount > 2)
                return;

            if (!IsVerboseEnabled)
                return;

            string details =
                "AgentIndex=" + message.AgentIndex +
                " SyncCount=" + syncCount +
                " PayloadEquipment={" + BuildEquipmentWithMountSummary(message.SpawnEquipment) + "}" +
                " ResolvedEntry={" + BuildResolvedEntrySummary(agent, out string _) + "}" +
                " Agent={" + BuildAgentSummary(agent) + "}" +
                " Source=" + (source ?? "unknown");
            Log("client-synchronize-agent-equipment", details, persistToRuntimeBundle: false);
        }

        internal static void ObserveClientSetWieldedItemIndex(
            SetWieldedItemIndex message,
            Agent agent,
            bool suppressed,
            string source)
        {
            if (GameNetwork.IsServer || message == null)
                return;

            ClientCreateAgentCorridorState state = GetOrCreateState(message.AgentIndex);
            int wieldCount;
            lock (Sync)
            {
                state.WieldEventCount++;
                state.LastObservedUtc = DateTime.UtcNow;
                wieldCount = state.WieldEventCount;
            }

            if (!IsVerboseEnabled)
                return;

            string payloadSummary = BuildSetWieldedPayloadSummary(message);
            string resolvedEntrySummary = BuildResolvedEntrySummary(agent, out string resolvedEntryId);
            lock (Sync)
            {
                state.LastResolvedEntrySummary = resolvedEntrySummary;
                state.LastResolvedEntryId = resolvedEntryId;
            }

            if (!suppressed &&
                !message.IsWieldedOnSpawn &&
                wieldCount > 3)
            {
                return;
            }

            string details =
                "AgentIndex=" + message.AgentIndex +
                " WieldEventCount=" + wieldCount +
                " Suppressed=" + suppressed +
                " Payload={" + payloadSummary + "}" +
                " ResolvedEntry={" + resolvedEntrySummary + "}" +
                " Agent={" + BuildAgentSummary(agent) + "}" +
                " Source=" + (source ?? "unknown");
            Log("client-set-wielded-item-index", details, persistToRuntimeBundle: false);
        }

        internal static void ObserveClientSetWieldedItemIndexException(
            SetWieldedItemIndex message,
            Agent agent,
            Exception exception,
            string source)
        {
            if (GameNetwork.IsServer || message == null || exception == null)
                return;

            ClientCreateAgentCorridorState state = GetOrCreateState(message.AgentIndex);
            string payloadSummary = BuildSetWieldedPayloadSummary(message);
            string resolvedEntrySummary = BuildResolvedEntrySummary(agent, out string resolvedEntryId);
            string candidateSummary;
            string candidatePayloadComparisonSummary;
            string createPayloadSummary;
            bool createPostfixObserved;
            lock (Sync)
            {
                state.LastResolvedEntrySummary = resolvedEntrySummary;
                state.LastResolvedEntryId = resolvedEntryId;
                state.LastObservedUtc = DateTime.UtcNow;
                candidateSummary = state.CandidateSummary ?? "CandidateResolution={State=unknown}";
                candidatePayloadComparisonSummary =
                    state.CandidatePayloadComparisonSummary ?? "PayloadCompare={State=unknown}";
                createPayloadSummary = state.LastPayloadSummary ?? "PayloadState=unknown";
                createPostfixObserved = state.CreateAgentPostfixObserved;
            }

            string details =
                "AgentIndex=" + message.AgentIndex +
                " ExceptionType=" + exception.GetType().FullName +
                " ExceptionMessage=" + exception.Message +
                " CreatePostfixObserved=" + createPostfixObserved +
                " CreatePayload={" + createPayloadSummary + "}" +
                " Candidate={" + candidateSummary + "}" +
                " " + candidatePayloadComparisonSummary +
                " WieldPayload={" + payloadSummary + "}" +
                " ResolvedEntry={" + resolvedEntrySummary + "}" +
                " Agent={" + BuildAgentSummary(agent) + "}" +
                " Source=" + (source ?? "unknown");
            Log("client-set-wielded-item-index-exception", details, persistToRuntimeBundle: true);
        }

        internal static bool TryResolveClientCreateAgentPayloadEntryId(
            CreateAgent createAgent,
            out string entryId,
            out string resolutionState,
            out string payloadComparisonSummary)
        {
            PayloadCandidateResolution resolution = ResolveCreateAgentCandidate(createAgent);
            entryId = resolution?.EntryId;
            resolutionState = resolution?.State ?? "absent";
            payloadComparisonSummary = resolution?.PayloadComparisonSummary ?? "PayloadCompare={State=unresolved}";
            return !string.IsNullOrWhiteSpace(entryId);
        }

        private static ClientCreateAgentCorridorState GetOrCreateState(int agentIndex)
        {
            lock (Sync)
            {
                if (!ClientStatesByAgentIndex.TryGetValue(agentIndex, out ClientCreateAgentCorridorState state))
                {
                    state = new ClientCreateAgentCorridorState
                    {
                        AgentIndex = agentIndex,
                        FirstObservedUtc = DateTime.UtcNow,
                        LastObservedUtc = DateTime.UtcNow
                    };
                    ClientStatesByAgentIndex[agentIndex] = state;
                }

                return state;
            }
        }

        private static string BuildCreateAgentPayloadSummary(CreateAgent createAgent)
        {
            if (createAgent == null)
                return "CreateAgentPayload={State=absent}";

            bool payloadMounted = createAgent.MountAgentIndex >= 0 || HasMountEquipment(createAgent.SpawnEquipment);
            return
                "CreateAgentPayload={CharacterId=" + (createAgent.Character?.StringId ?? "null") +
                ",TeamIndex=" + createAgent.TeamIndex +
                ",Side=" + ResolveCreateAgentPayloadBattleSide(createAgent.TeamIndex) +
                ",FormationIndex=" + createAgent.FormationIndex +
                ",Position=" + FormatVec3(createAgent.Position) +
                ",Direction=" + FormatVec2(createAgent.Direction) +
                ",IsPlayerAgent=" + createAgent.IsPlayerAgent +
                ",PeerIndex=" + (createAgent.Peer?.Index.ToString() ?? "null") +
                ",MountAgentIndex=" + createAgent.MountAgentIndex +
                ",Mounted=" + payloadMounted +
                ",MissionWeapons={" + ExactCreateAgentPayloadDiagnostics.BuildMissionEquipmentWeaponLayoutSummary(createAgent.MissionEquipment) +
                "},MissionWeaponSlots={" + BuildMissionEquipmentWeaponSlotVector(createAgent.MissionEquipment) +
                "},SpawnWeapons={" + ExactCreateAgentPayloadDiagnostics.BuildEquipmentWeaponLayoutSummary(createAgent.SpawnEquipment) +
                "},SpawnWeaponSlots={" + BuildEquipmentWeaponSlotVector(createAgent.SpawnEquipment) +
                "},SpawnArmorSlots={" + BuildEquipmentNonWeaponSlotVector(createAgent.SpawnEquipment) +
                "},SpawnMount={" + ExactCreateAgentPayloadDiagnostics.BuildEquipmentMountLayoutSummary(createAgent.SpawnEquipment) + "}}";
        }

        private static string BuildCreateAgentCandidateSummary(
            CreateAgent createAgent,
            out string candidateEntryId,
            out string candidatePayloadComparisonSummary)
        {
            PayloadCandidateResolution resolution = ResolveCreateAgentCandidate(createAgent);
            candidateEntryId = resolution?.EntryId;
            candidatePayloadComparisonSummary = resolution?.PayloadComparisonSummary ?? "PayloadCompare={State=unresolved}";
            return resolution?.Summary ?? "CandidateResolution={State=absent}";
        }

        private static PayloadCandidateResolution ResolveCreateAgentCandidate(CreateAgent createAgent)
        {
            if (createAgent == null)
            {
                return new PayloadCandidateResolution
                {
                    State = "absent",
                    Summary = "CandidateResolution={State=absent}",
                    PayloadComparisonSummary = "PayloadCompare={State=unresolved}"
                };
            }

            BattleRuntimeState runtimeState = BattleSnapshotRuntimeState.GetState();
            if (runtimeState?.EntriesById == null || runtimeState.EntriesById.Count == 0)
            {
                return new PayloadCandidateResolution
                {
                    State = "snapshot-unavailable",
                    Summary = "CandidateResolution={State=snapshot-unavailable}",
                    PayloadComparisonSummary = "PayloadCompare={State=snapshot-unavailable}"
                };
            }

            BattleSideEnum payloadSide = ResolveCreateAgentPayloadBattleSide(createAgent.TeamIndex);
            bool payloadMounted = createAgent.MountAgentIndex >= 0 || HasMountEquipment(createAgent.SpawnEquipment);
            string payloadCharacterId = createAgent.Character?.StringId;
            string payloadMissionWeaponLayout = ExactCreateAgentPayloadDiagnostics.BuildMissionEquipmentWeaponLayoutSummary(createAgent.MissionEquipment);
            string payloadSpawnWeaponLayout = ExactCreateAgentPayloadDiagnostics.BuildEquipmentWeaponLayoutSummary(createAgent.SpawnEquipment);
            List<RosterEntryState> sideEntries = runtimeState.EntriesById.Values
                .Where(entryState =>
                    entryState != null &&
                    (payloadSide == BattleSideEnum.None || DoesEntryMatchSide(entryState, payloadSide)))
                .ToList();
            if (sideEntries.Count == 0)
            {
                return new PayloadCandidateResolution
                {
                    State = "no-side-candidates",
                    Summary =
                        "CandidateResolution={State=no-side-candidates" +
                        ",PayloadCharacterId=" + (payloadCharacterId ?? "null") +
                        ",PayloadSide=" + payloadSide + "}",
                    PayloadComparisonSummary = "PayloadCompare={State=no-side-candidates}"
                };
            }

            bool payloadMissionWeaponLayoutAvailable = !string.Equals(payloadMissionWeaponLayout, "(none)", StringComparison.Ordinal) &&
                                                      !string.Equals(payloadMissionWeaponLayout, "(empty)", StringComparison.Ordinal);
            bool payloadSpawnWeaponLayoutAvailable = !string.Equals(payloadSpawnWeaponLayout, "(none)", StringComparison.Ordinal) &&
                                                    !string.Equals(payloadSpawnWeaponLayout, "(empty)", StringComparison.Ordinal);
            var candidates = new List<PayloadCandidateMatch>();
            foreach (RosterEntryState entryState in sideEntries)
            {
                bool characterMatch = DoesEntryMatchPayloadCharacter(entryState, payloadCharacterId);
                string entryWeaponLayout = ExactCreateAgentPayloadDiagnostics.BuildEntryWeaponLayoutSummary(entryState);
                bool weaponLayoutMatch =
                    payloadMissionWeaponLayoutAvailable
                        ? string.Equals(payloadMissionWeaponLayout, entryWeaponLayout, StringComparison.Ordinal)
                        : payloadSpawnWeaponLayoutAvailable &&
                          string.Equals(payloadSpawnWeaponLayout, entryWeaponLayout, StringComparison.Ordinal);
                bool mountedMatch = entryState.IsMounted == payloadMounted;
                int score = (characterMatch ? 8 : 0) + (weaponLayoutMatch ? 4 : 0) + (mountedMatch ? 2 : 0);
                if (score <= 0)
                    continue;

                candidates.Add(
                    new PayloadCandidateMatch
                    {
                        EntryState = entryState,
                        CharacterMatch = characterMatch,
                        WeaponLayoutMatch = weaponLayoutMatch,
                        MountedMatch = mountedMatch,
                        Score = score
                    });
            }

            if (candidates.Count == 0)
            {
                return new PayloadCandidateResolution
                {
                    State = "no-scored-candidates",
                    Summary =
                        "CandidateResolution={State=no-scored-candidates" +
                        ",PayloadCharacterId=" + (payloadCharacterId ?? "null") +
                        ",PayloadSide=" + payloadSide +
                        ",PayloadMounted=" + payloadMounted +
                        ",PayloadMissionWeapons={" + payloadMissionWeaponLayout +
                        "},PayloadSpawnWeapons={" + payloadSpawnWeaponLayout +
                        "},SideEntryCount=" + sideEntries.Count + "}",
                    PayloadComparisonSummary = "PayloadCompare={State=no-scored-candidates}"
                };
            }

            List<PayloadCandidateMatch> ordered = candidates
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.EntryState.EntryId, StringComparer.Ordinal)
                .ToList();
            PayloadCandidateMatch bestCandidate = ordered[0];
            bool uniqueBest = ordered.Count == 1 || bestCandidate.Score > ordered[1].Score;
            string state = "ambiguous";
            string candidateEntryId = null;
            if (bestCandidate.CharacterMatch && bestCandidate.WeaponLayoutMatch && uniqueBest)
            {
                state = "resolved-strong";
                candidateEntryId = bestCandidate.EntryState.EntryId;
            }
            else if (bestCandidate.CharacterMatch && uniqueBest)
            {
                state = "resolved-character";
                candidateEntryId = bestCandidate.EntryState.EntryId;
            }
            else if (bestCandidate.WeaponLayoutMatch && uniqueBest)
            {
                state = "resolved-layout";
                candidateEntryId = bestCandidate.EntryState.EntryId;
            }

            string payloadComparisonSummary = BuildCandidatePayloadComparisonSummary(bestCandidate.EntryState, createAgent);
            string sample = string.Join(", ", ordered.Take(4).Select(FormatCandidate));
            return new PayloadCandidateResolution
            {
                State = state,
                EntryId = candidateEntryId,
                PayloadComparisonSummary = payloadComparisonSummary,
                Summary =
                    "CandidateResolution={State=" + state +
                    ",PayloadCharacterId=" + (payloadCharacterId ?? "null") +
                    ",PayloadSide=" + payloadSide +
                    ",PayloadMounted=" + payloadMounted +
                    ",PayloadMissionWeapons={" + payloadMissionWeaponLayout +
                    "},PayloadSpawnWeapons={" + payloadSpawnWeaponLayout +
                    "},SideEntryCount=" + sideEntries.Count +
                    ",CandidateCount=" + candidates.Count +
                    ",BestEntryId=" + (bestCandidate.EntryState.EntryId ?? "null") +
                    ",BestScore=" + bestCandidate.Score +
                    ",Candidates=[" + sample + "]}"
            };
        }

        private static string BuildCandidatePayloadComparisonSummary(
            RosterEntryState entryState,
            CreateAgent createAgent)
        {
            if (entryState == null)
                return "PayloadCompare={State=entry-null}";

            if (createAgent == null)
                return "PayloadCompare={State=create-agent-null}";

            WeaponSlotSnapshot[] entrySlots = BuildEntryWeaponSlots(entryState);
            WeaponSlotSnapshot[] missionSlots = BuildMissionEquipmentWeaponSlots(createAgent.MissionEquipment);
            WeaponSlotSnapshot[] spawnSlots = BuildEquipmentWeaponSlots(createAgent.SpawnEquipment);
            return
                "PayloadCompare={EntryId=" + (entryState.EntryId ?? "null") +
                ",EntryWeaponSlots={" + BuildWeaponSlotVector(entrySlots) + "}" +
                "," + BuildWeaponSlotDiffSummary("MissionDiff", entrySlots, missionSlots) +
                "," + BuildWeaponSlotDiffSummary("SpawnDiff", entrySlots, spawnSlots) +
                "}";
        }

        private static string FormatCandidate(PayloadCandidateMatch candidate)
        {
            if (candidate?.EntryState == null)
                return "(null)";

            return
                (candidate.EntryState.EntryId ?? "null") +
                "/" +
                (candidate.EntryState.CharacterId ?? candidate.EntryState.OriginalCharacterId ?? candidate.EntryState.SpawnTemplateId ?? "null") +
                "[score=" + candidate.Score +
                ",char=" + candidate.CharacterMatch +
                ",layout=" + candidate.WeaponLayoutMatch +
                ",mounted=" + candidate.MountedMatch + "]";
        }

        private static bool DoesEntryMatchPayloadCharacter(RosterEntryState entryState, string payloadCharacterId)
        {
            if (entryState == null || string.IsNullOrWhiteSpace(payloadCharacterId))
                return false;

            return EnumerateEntryCandidateCharacterIds(entryState)
                .Any(candidateCharacterId => string.Equals(candidateCharacterId, payloadCharacterId, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> EnumerateEntryCandidateCharacterIds(RosterEntryState entryState)
        {
            if (entryState == null)
                yield break;

            var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string candidateCharacterId in new[]
                     {
                         BattleSnapshotRuntimeState.TryResolveCharacterObject(entryState.EntryId)?.StringId,
                         entryState.SpawnTemplateId,
                         entryState.CharacterId,
                         entryState.OriginalCharacterId,
                         entryState.HeroTemplateId
                     })
            {
                if (string.IsNullOrWhiteSpace(candidateCharacterId) || !yielded.Add(candidateCharacterId))
                    continue;

                yield return candidateCharacterId;
            }
        }

        private static BattleSideEnum ResolveCreateAgentPayloadBattleSide(int teamIndex)
        {
            Team missionTeam = Mission.MissionNetworkHelper.GetTeamFromTeamIndex(teamIndex);
            if (missionTeam != null && missionTeam.Side != BattleSideEnum.None)
                return missionTeam.Side;

            if (teamIndex == 0)
                return BattleSideEnum.Attacker;

            if (teamIndex == 1)
                return BattleSideEnum.Defender;

            return BattleSideEnum.None;
        }

        private static bool DoesEntryMatchSide(RosterEntryState entryState, BattleSideEnum side)
        {
            if (entryState == null || side == BattleSideEnum.None)
                return false;

            string sideId = entryState.SideId ?? string.Empty;
            if (side == BattleSideEnum.Attacker)
                return string.Equals(sideId, "Attacker", StringComparison.OrdinalIgnoreCase);

            if (side == BattleSideEnum.Defender)
                return string.Equals(sideId, "Defender", StringComparison.OrdinalIgnoreCase);

            return false;
        }

        private static bool HasMountEquipment(Equipment equipment)
        {
            if (equipment == null)
                return false;

            return equipment[EquipmentIndex.Horse].Item != null || equipment[EquipmentIndex.HorseHarness].Item != null;
        }

        private static string BuildEntryWeaponSlotVector(RosterEntryState entryState)
        {
            return BuildWeaponSlotVector(BuildEntryWeaponSlots(entryState));
        }

        private static string BuildEquipmentWeaponSlotVector(Equipment equipment)
        {
            return BuildWeaponSlotVector(BuildEquipmentWeaponSlots(equipment));
        }

        private static string BuildMissionEquipmentWeaponSlotVector(MissionEquipment equipment)
        {
            return BuildWeaponSlotVector(BuildMissionEquipmentWeaponSlots(equipment));
        }

        private static string BuildEquipmentNonWeaponSlotVector(Equipment equipment)
        {
            return BuildEquipmentSlotVector(BuildEquipmentNonWeaponSlots(equipment));
        }

        private static WeaponSlotSnapshot[] BuildEntryWeaponSlots(RosterEntryState entryState)
        {
            return new[]
            {
                new WeaponSlotSnapshot
                {
                    Slot = EquipmentIndex.Weapon0,
                    ItemId = entryState?.CombatItem0Id,
                    Amount = entryState?.CombatItem0Amount
                },
                new WeaponSlotSnapshot
                {
                    Slot = EquipmentIndex.Weapon1,
                    ItemId = entryState?.CombatItem1Id,
                    Amount = entryState?.CombatItem1Amount
                },
                new WeaponSlotSnapshot
                {
                    Slot = EquipmentIndex.Weapon2,
                    ItemId = entryState?.CombatItem2Id,
                    Amount = entryState?.CombatItem2Amount
                },
                new WeaponSlotSnapshot
                {
                    Slot = EquipmentIndex.Weapon3,
                    ItemId = entryState?.CombatItem3Id,
                    Amount = entryState?.CombatItem3Amount
                }
            };
        }

        private static WeaponSlotSnapshot[] BuildEquipmentWeaponSlots(Equipment equipment)
        {
            var slots = new List<WeaponSlotSnapshot>();
            for (EquipmentIndex slot = EquipmentIndex.Weapon0; slot <= EquipmentIndex.Weapon3; slot++)
            {
                EquipmentElement element = equipment?[slot] ?? default(EquipmentElement);
                slots.Add(
                    new WeaponSlotSnapshot
                    {
                        Slot = slot,
                        ItemId = element.Item?.StringId,
                        Amount = element.Item != null ? TryGetEquipmentElementAmount(element) : null
                    });
            }

            return slots.ToArray();
        }

        private static WeaponSlotSnapshot[] BuildMissionEquipmentWeaponSlots(MissionEquipment equipment)
        {
            var slots = new List<WeaponSlotSnapshot>();
            for (EquipmentIndex slot = EquipmentIndex.Weapon0; slot <= EquipmentIndex.Weapon3; slot++)
            {
                MissionWeapon weapon = equipment?[slot] ?? default(MissionWeapon);
                slots.Add(
                    new WeaponSlotSnapshot
                    {
                        Slot = slot,
                        ItemId = weapon.Item?.StringId,
                        Amount = weapon.Item != null && weapon.Amount > 0 ? (int?)weapon.Amount : null
                    });
            }

            return slots.ToArray();
        }

        private static Equipment CloneEquipment(Equipment equipment)
        {
            return equipment?.Clone(false);
        }

        private static Equipment BuildEquipmentCloneFromMissionEquipment(MissionEquipment missionEquipment)
        {
            if (missionEquipment == null)
                return null;

            var equipment = new Equipment();
            // MissionEquipment safely exposes the live weapon block. Non-weapon slots such as
            // Head/Body/Horse belong to SpawnEquipment and can throw or report invalid state
            // when read through MissionEquipment on dedicated bootstrap paths.
            foreach (EquipmentIndex slot in EnumerateTrackedWeaponSlots())
            {
                MissionWeapon missionWeapon;
                try
                {
                    missionWeapon = missionEquipment[slot];
                }
                catch
                {
                    continue;
                }

                ItemObject item = missionWeapon.Item;
                if (item == null || missionWeapon.IsEmpty)
                    continue;

                equipment[slot] = new EquipmentElement(item, null, null, false);
            }

            return equipment;
        }

        private static MissionEquipment BuildMissionEquipmentFromEquipmentClone(Equipment equipment)
        {
            if (equipment == null)
                return null;

            try
            {
                return new MissionEquipment(equipment, null);
            }
            catch
            {
                return null;
            }
        }

        private static EquipmentSlotSnapshot[] BuildEquipmentNonWeaponSlots(Equipment equipment)
        {
            return new[]
            {
                BuildEquipmentSlotSnapshot(equipment, EquipmentIndex.Head),
                BuildEquipmentSlotSnapshot(equipment, EquipmentIndex.Body),
                BuildEquipmentSlotSnapshot(equipment, EquipmentIndex.Leg),
                BuildEquipmentSlotSnapshot(equipment, EquipmentIndex.Gloves),
                BuildEquipmentSlotSnapshot(equipment, EquipmentIndex.Cape),
                BuildEquipmentSlotSnapshot(equipment, EquipmentIndex.Horse),
                BuildEquipmentSlotSnapshot(equipment, EquipmentIndex.HorseHarness)
            };
        }

        private static EquipmentSlotSnapshot BuildEquipmentSlotSnapshot(Equipment equipment, EquipmentIndex slot)
        {
            EquipmentElement element = equipment?[slot] ?? default(EquipmentElement);
            return new EquipmentSlotSnapshot
            {
                Slot = slot,
                ItemId = element.Item?.StringId
            };
        }

        private static string BuildWeaponSlotVector(WeaponSlotSnapshot[] slots)
        {
            if (slots == null || slots.Length == 0)
                return "State=none";

            var parts = new List<string>(slots.Length);
            int occupancyMask = 0;
            for (int index = 0; index < slots.Length; index++)
            {
                WeaponSlotSnapshot slot = slots[index];
                bool occupied = !string.IsNullOrWhiteSpace(slot?.ItemId);
                if (occupied)
                    occupancyMask |= 1 << index;

                parts.Add(
                    (slot?.Slot.ToString() ?? ("Slot" + index)) +
                    "=" +
                    BuildWeaponSlotToken(slot));
            }

            return
                "Mask=" + Convert.ToString(occupancyMask, 2).PadLeft(4, '0') +
                " Slots=[" + string.Join(", ", parts) + "]";
        }

        private static string BuildEquipmentSlotVector(EquipmentSlotSnapshot[] slots)
        {
            if (slots == null || slots.Length == 0)
                return "State=none";

            var parts = new List<string>(slots.Length);
            foreach (EquipmentSlotSnapshot slot in slots)
            {
                parts.Add(
                    (slot?.Slot.ToString() ?? "Slot") +
                    "=" +
                    (string.IsNullOrWhiteSpace(slot?.ItemId) ? "(empty)" : slot.ItemId.Trim()));
            }

            return "Slots=[" + string.Join(", ", parts) + "]";
        }

        private static string BuildWeaponSlotDiffSummary(
            string label,
            WeaponSlotSnapshot[] expectedSlots,
            WeaponSlotSnapshot[] actualSlots)
        {
            if (expectedSlots == null || expectedSlots.Length == 0)
                return (label ?? "Diff") + "={State=expected-unavailable}";

            if (actualSlots == null || actualSlots.Length == 0)
                return (label ?? "Diff") + "={State=actual-unavailable}";

            var missing = new List<string>();
            var unexpected = new List<string>();
            var changed = new List<string>();
            int matchingOccupiedSlots = 0;
            int occupiedExpected = 0;
            int occupiedActual = 0;
            int slotCount = Math.Min(expectedSlots.Length, actualSlots.Length);
            for (int index = 0; index < slotCount; index++)
            {
                WeaponSlotSnapshot expected = expectedSlots[index];
                WeaponSlotSnapshot actual = actualSlots[index];
                bool expectedOccupied = !string.IsNullOrWhiteSpace(expected?.ItemId);
                bool actualOccupied = !string.IsNullOrWhiteSpace(actual?.ItemId);
                if (expectedOccupied)
                    occupiedExpected++;

                if (actualOccupied)
                    occupiedActual++;

                if (!expectedOccupied && !actualOccupied)
                    continue;

                string slotName = expected?.Slot.ToString() ?? actual?.Slot.ToString() ?? ("Slot" + index);
                if (expectedOccupied && actualOccupied)
                {
                    if (string.Equals(BuildWeaponSlotToken(expected), BuildWeaponSlotToken(actual), StringComparison.Ordinal))
                    {
                        matchingOccupiedSlots++;
                        continue;
                    }

                    changed.Add(slotName + ":" + BuildWeaponSlotToken(expected) + "->" + BuildWeaponSlotToken(actual));
                    continue;
                }

                if (expectedOccupied)
                {
                    missing.Add(slotName + "=" + BuildWeaponSlotToken(expected));
                    continue;
                }

                unexpected.Add(slotName + "=" + BuildWeaponSlotToken(actual));
            }

            bool match = missing.Count == 0 && unexpected.Count == 0 && changed.Count == 0;
            return
                (label ?? "Diff") +
                "={State=" + (match ? "match" : "mismatch") +
                ",ExpectedMask=" + BuildWeaponOccupancyMask(expectedSlots) +
                ",ActualMask=" + BuildWeaponOccupancyMask(actualSlots) +
                ",OccupiedExpected=" + occupiedExpected +
                ",OccupiedActual=" + occupiedActual +
                ",MatchingOccupiedSlots=" + matchingOccupiedSlots +
                ",Missing=[" + JoinOrNone(missing) +
                "],Unexpected=[" + JoinOrNone(unexpected) +
                "],Changed=[" + JoinOrNone(changed) + "]}";
        }

        private static bool HasWeaponSlotMismatch(
            WeaponSlotSnapshot[] expectedSlots,
            WeaponSlotSnapshot[] actualSlots)
        {
            if (expectedSlots == null || actualSlots == null)
                return true;

            int slotCount = Math.Min(expectedSlots.Length, actualSlots.Length);
            for (int index = 0; index < slotCount; index++)
            {
                string expectedToken = BuildWeaponSlotToken(expectedSlots[index]);
                string actualToken = BuildWeaponSlotToken(actualSlots[index]);
                if (!string.Equals(expectedToken, actualToken, StringComparison.Ordinal))
                    return true;
            }

            return expectedSlots.Length != actualSlots.Length;
        }

        private static string BuildWeaponOccupancyMask(WeaponSlotSnapshot[] slots)
        {
            if (slots == null || slots.Length == 0)
                return "0000";

            int occupancyMask = 0;
            for (int index = 0; index < slots.Length; index++)
            {
                if (!string.IsNullOrWhiteSpace(slots[index]?.ItemId))
                    occupancyMask |= 1 << index;
            }

            return Convert.ToString(occupancyMask, 2).PadLeft(4, '0');
        }

        private static string BuildWeaponSlotToken(WeaponSlotSnapshot slot)
        {
            if (slot == null || string.IsNullOrWhiteSpace(slot.ItemId))
                return "(empty)";

            return slot.ItemId.Trim() + (slot.Amount.HasValue && slot.Amount.Value > 1
                ? "@" + slot.Amount.Value.ToString(CultureInfo.InvariantCulture)
                : string.Empty);
        }

        private static string BuildWeaponSlotFamilyVector(WeaponSlotSnapshot[] slots)
        {
            if (slots == null || slots.Length == 0)
                return "State=none";

            var parts = new List<string>(slots.Length);
            foreach (WeaponSlotSnapshot slot in slots)
            {
                parts.Add(
                    (slot?.Slot.ToString() ?? "Slot") +
                    "=" +
                    ResolveItemTypeLabel(slot?.ItemId));
            }

            return "Slots=[" + string.Join(", ", parts) + "]";
        }

        private static string BuildWeaponSlotOriginHintSummary(
            WeaponSlotSnapshot[] actualSlots,
            IReadOnlyDictionary<string, string> expectedItemOriginById)
        {
            if (actualSlots == null || actualSlots.Length == 0)
                return "State=none";

            var parts = new List<string>(actualSlots.Length);
            foreach (WeaponSlotSnapshot slot in actualSlots)
            {
                string itemId = slot?.ItemId;
                string originHint = "unknown";
                if (!string.IsNullOrWhiteSpace(itemId) &&
                    expectedItemOriginById != null &&
                    expectedItemOriginById.TryGetValue(itemId.Trim(), out string resolvedOrigin) &&
                    !string.IsNullOrWhiteSpace(resolvedOrigin))
                {
                    originHint = resolvedOrigin;
                }

                parts.Add(
                    (slot?.Slot.ToString() ?? "Slot") +
                    "=" +
                    BuildWeaponSlotToken(slot) +
                    "(from:" + originHint + ")");
            }

            return "Slots=[" + string.Join(", ", parts) + "]";
        }

        private static Dictionary<string, string> BuildExpectedItemOriginById(Equipment equipment)
        {
            var origins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (equipment == null)
                return origins;

            foreach (EquipmentIndex slot in EnumerateTrackedEquipmentSlots())
            {
                EquipmentElement element = equipment[slot];
                string itemId = element.Item?.StringId;
                if (string.IsNullOrWhiteSpace(itemId))
                    continue;

                string normalizedItemId = itemId.Trim();
                string slotName = slot.ToString();
                if (origins.TryGetValue(normalizedItemId, out string existingOrigin) &&
                    !string.IsNullOrWhiteSpace(existingOrigin))
                {
                    if (existingOrigin.IndexOf(slotName, StringComparison.Ordinal) < 0)
                        origins[normalizedItemId] = existingOrigin + "|" + slotName;
                }
                else
                {
                    origins[normalizedItemId] = slotName;
                }
            }

            return origins;
        }

        private static IEnumerable<EquipmentIndex> EnumerateTrackedEquipmentSlots()
        {
            foreach (EquipmentIndex slot in EnumerateTrackedWeaponSlots())
                yield return slot;

            yield return EquipmentIndex.Head;
            yield return EquipmentIndex.Body;
            yield return EquipmentIndex.Leg;
            yield return EquipmentIndex.Gloves;
            yield return EquipmentIndex.Cape;
            yield return EquipmentIndex.Horse;
            yield return EquipmentIndex.HorseHarness;
        }

        private static IEnumerable<EquipmentIndex> EnumerateTrackedWeaponSlots()
        {
            for (EquipmentIndex slot = EquipmentIndex.Weapon0; slot <= EquipmentIndex.Weapon3; slot++)
                yield return slot;
        }

        private static string ResolveItemTypeLabel(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return "empty";

            try
            {
                ItemObject item = MBObjectManager.Instance?.GetObject<ItemObject>(itemId.Trim());
                return item?.ItemType.ToString() ?? "unknown";
            }
            catch
            {
                return "lookup-failed";
            }
        }

        private static bool HasSuspiciousNonWeaponFamilies(WeaponSlotSnapshot[] slots)
        {
            if (slots == null || slots.Length == 0)
                return false;

            foreach (WeaponSlotSnapshot slot in slots)
            {
                string family = ResolveItemTypeLabel(slot?.ItemId);
                switch (family)
                {
                    case "Horse":
                    case "HeadArmor":
                    case "BodyArmor":
                    case "LegArmor":
                    case "HandArmor":
                    case "Cape":
                    case "HorseHarness":
                        return true;
                }
            }

            return false;
        }

        private static void TrySetInstanceMemberValue(object instance, string memberName, object value)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
                return;

            for (Type instanceType = instance.GetType(); instanceType != null; instanceType = instanceType.BaseType)
            {
                PropertyInfo property = instanceType.GetProperty(
                    memberName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
                {
                    try
                    {
                        property.SetValue(instance, value, null);
                        return;
                    }
                    catch
                    {
                    }
                }

                FieldInfo field = instanceType.GetField(
                    memberName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    try
                    {
                        field.SetValue(instance, value);
                        return;
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static int? TryGetEquipmentElementAmount(EquipmentElement element)
        {
            if (element.Item == null)
                return null;

            try
            {
                MethodInfo getModifiedStackCountForUsage = typeof(EquipmentElement).GetMethod(
                    "GetModifiedStackCountForUsage",
                    new[] { typeof(int) });
                if (getModifiedStackCountForUsage != null)
                {
                    object boxedElement = element;
                    object amountValue = getModifiedStackCountForUsage.Invoke(boxedElement, new object[] { 0 });
                    if (amountValue is int amount && amount > 0)
                        return amount;
                }
            }
            catch
            {
            }

            return null;
        }

        private static string JoinOrNone(IEnumerable<string> values)
        {
            if (values == null)
                return "none";

            string joined = string.Join(", ", values.Where(value => !string.IsNullOrWhiteSpace(value)));
            return string.IsNullOrWhiteSpace(joined) ? "none" : joined;
        }

        private static string BuildSetWieldedPayloadSummary(SetWieldedItemIndex message)
        {
            if (message == null)
                return "SetWieldedPayload={State=absent}";

            return
                "SetWieldedPayload={WieldedItemIndex=" + message.WieldedItemIndex +
                ",IsLeftHand=" + message.IsLeftHand +
                ",IsWieldedInstantly=" + message.IsWieldedInstantly +
                ",IsWieldedOnSpawn=" + message.IsWieldedOnSpawn +
                ",MainHandCurrentUsageIndex=" + message.MainHandCurrentUsageIndex + "}";
        }

        private static string BuildResolvedEntrySummary(Agent agent, out string entryId)
        {
            entryId = null;
            if (agent == null)
                return "ResolvedEntry={State=agent-null}";

            string resolutionSource = null;
            if (CoopMissionSpawnLogic.TryResolveAuthoritativeTrackedEntryId(agent, out entryId))
            {
                resolutionSource = "authoritative-tracked";
            }
            else if (CoopMissionSpawnLogic.TryResolveSelectableEntryId(agent, out entryId))
            {
                resolutionSource = "selectable-overlay";
            }
            else if (ExactTransferContractRuntimeCache.TryGetEntryIdByRiderAgentIndex(agent.Index, out string cachedEntryId) &&
                     !string.IsNullOrWhiteSpace(cachedEntryId))
            {
                entryId = cachedEntryId;
                resolutionSource = "exact-transfer-cache";
            }

            if (string.IsNullOrWhiteSpace(entryId))
                return "ResolvedEntry={State=unresolved}";

            RosterEntryState entryState = BattleSnapshotRuntimeState.GetEntryState(entryId);
            return
                "ResolvedEntry={EntryId=" + entryId +
                ",Source=" + (resolutionSource ?? "unknown") +
                ",TroopId=" + (entryState?.CharacterId ?? entryState?.OriginalCharacterId ?? entryState?.SpawnTemplateId ?? "null") +
                ",Mounted=" + (entryState?.IsMounted.ToString() ?? "null") +
                ",Weapons={" + ExactCreateAgentPayloadDiagnostics.BuildEntryWeaponLayoutSummary(entryState) +
                "},Mount={" + ExactCreateAgentPayloadDiagnostics.BuildEntryMountLayoutSummary(entryState) + "}}";
        }

        private static string BuildAgentSummary(Agent agent)
        {
            if (agent == null)
                return "AgentState={State=absent}";

            Equipment spawnEquipment = null;
            MissionEquipment missionEquipment = null;
            try
            {
                spawnEquipment = agent.SpawnEquipment;
            }
            catch
            {
            }

            try
            {
                missionEquipment = agent.Equipment;
            }
            catch
            {
            }

            string visualStateSummary = IsVerboseEnabled
                ? BuildAgentPositionVisualSummary(agent)
                : "PositionVisualState={State=diagnostics-disabled}";

            return
                "AgentState={Index=" + agent.Index +
                ",CharacterId=" + ((agent.Character as BasicCharacterObject)?.StringId ?? "null") +
                ",Position=" + FormatVec3(agent.Position) +
                "," + visualStateSummary +
                ",LookDirection=" + FormatVec3(agent.LookDirection) +
                ",TeamIndex=" + (agent.Team?.TeamIndex.ToString() ?? "null") +
                ",TeamSide=" + (agent.Team?.Side.ToString() ?? "null") +
                ",Formation=" + BuildFormationPositionSummary(agent.Formation, agent.Position.AsVec2) +
                ",MissionPeerIndex=" + (agent.MissionPeer?.Peer?.Index.ToString() ?? "null") +
                ",MountAgentIndex=" + (agent.MountAgent?.Index.ToString() ?? "null") +
                ",Active=" + agent.IsActive() +
                ",Health=" + agent.Health.ToString("0.0", CultureInfo.InvariantCulture) +
                ",WieldedItem=" + (agent.WieldedWeapon.Item?.StringId ?? "none") +
                ",OffhandItem=" + (agent.WieldedOffhandWeapon.Item?.StringId ?? "none") +
                ",SpawnWeapons={" + ExactCreateAgentPayloadDiagnostics.BuildEquipmentWeaponLayoutSummary(spawnEquipment) +
                "},SpawnWeaponSlots={" + BuildEquipmentWeaponSlotVector(spawnEquipment) +
                "},MissionWeapons={" + ExactCreateAgentPayloadDiagnostics.BuildMissionEquipmentWeaponLayoutSummary(missionEquipment) +
                "},MissionWeaponSlots={" + BuildMissionEquipmentWeaponSlotVector(missionEquipment) +
                "},Mount={" + ExactCreateAgentPayloadDiagnostics.BuildEquipmentMountLayoutSummary(spawnEquipment) + "}}";
        }

        private static ClientAgentPositionVisualState ReadClientAgentPositionVisualState(Agent agent)
        {
            var state = new ClientAgentPositionVisualState
            {
                Position = agent.Position,
                TeamIndex = agent.Team?.TeamIndex ?? -1,
                FormationIndex = agent.Formation?.Index ?? -1
            };

            try
            {
                state.VisualPosition = agent.VisualPosition;
                state.HasVisualPosition = state.VisualPosition.IsValid;
            }
            catch
            {
                state.HasVisualPosition = false;
            }

            try
            {
                MBAgentVisuals visuals = agent.AgentVisuals;
                state.VisualsValid = visuals != null && visuals.IsValid();
                if (state.VisualsValid)
                {
                    MatrixFrame frame = visuals.GetGlobalFrame();
                    state.VisualFrameOrigin = frame.origin;
                    state.HasVisualFrame = frame.origin.IsValid;
                    state.VisualsVisible = visuals.GetVisible();
                }
            }
            catch
            {
                state.VisualsValid = false;
                state.HasVisualFrame = false;
                state.VisualsVisible = false;
            }

            return state;
        }

        private static ClientPeerPreviewVisualState ReadClientPeerPreviewVisualState(PeerVisualsHolder holder)
        {
            var state = new ClientPeerPreviewVisualState();
            IAgentVisual agentVisual = holder?.AgentVisuals;
            if (agentVisual == null)
                return state;

            try
            {
                state.CharacterId = agentVisual.GetCharacterObjectID();
            }
            catch
            {
                state.CharacterId = null;
            }

            try
            {
                MBAgentVisuals visuals = agentVisual.GetVisuals();
                state.VisualsValid = visuals != null && visuals.IsValid();
                if (state.VisualsValid)
                {
                    MatrixFrame frame = visuals.GetGlobalFrame();
                    state.FrameOrigin = frame.origin;
                    state.HasFrame = frame.origin.IsValid;
                    state.VisualsVisible = visuals.GetVisible();
                }
            }
            catch
            {
                state.VisualsValid = false;
                state.HasFrame = false;
                state.VisualsVisible = false;
            }

            return state;
        }

        private static string BuildClientAgentPositionVisualTrigger(
            ClientAgentPositionVisualState previous,
            ClientAgentPositionVisualState current)
        {
            if (previous == null)
                return "baseline";

            var triggers = new List<string>();
            float movementThresholdSquared =
                ClientPositionVisualMovementThreshold * ClientPositionVisualMovementThreshold;
            float mismatchThresholdSquared =
                ClientPositionVisualMismatchThreshold * ClientPositionVisualMismatchThreshold;
            if (DistanceSquared(previous.Position, current.Position) >= movementThresholdSquared)
                triggers.Add("physical-position-moved");
            if (previous.HasVisualPosition != current.HasVisualPosition ||
                (previous.HasVisualPosition && current.HasVisualPosition &&
                 DistanceSquared(previous.VisualPosition, current.VisualPosition) >= movementThresholdSquared))
            {
                triggers.Add("visual-position-changed");
            }
            if (previous.HasVisualFrame != current.HasVisualFrame ||
                (previous.HasVisualFrame && current.HasVisualFrame &&
                 DistanceSquared(previous.VisualFrameOrigin, current.VisualFrameOrigin) >= movementThresholdSquared))
            {
                triggers.Add("visual-frame-changed");
            }

            bool previousPositionVisualMismatch =
                previous.HasVisualPosition &&
                DistanceSquared(previous.Position, previous.VisualPosition) >= mismatchThresholdSquared;
            bool currentPositionVisualMismatch =
                current.HasVisualPosition &&
                DistanceSquared(current.Position, current.VisualPosition) >= mismatchThresholdSquared;
            if (previousPositionVisualMismatch != currentPositionVisualMismatch)
                triggers.Add(currentPositionVisualMismatch ? "position-visual-mismatch-started" : "position-visual-mismatch-ended");

            bool previousPositionFrameMismatch =
                previous.HasVisualFrame &&
                DistanceSquared(previous.Position, previous.VisualFrameOrigin) >= mismatchThresholdSquared;
            bool currentPositionFrameMismatch =
                current.HasVisualFrame &&
                DistanceSquared(current.Position, current.VisualFrameOrigin) >= mismatchThresholdSquared;
            if (previousPositionFrameMismatch != currentPositionFrameMismatch)
                triggers.Add(currentPositionFrameMismatch ? "position-frame-mismatch-started" : "position-frame-mismatch-ended");

            if (previous.VisualsValid != current.VisualsValid)
                triggers.Add("visual-validity-changed");
            if (previous.VisualsVisible != current.VisualsVisible)
                triggers.Add("visual-visibility-changed");
            if (previous.TeamIndex != current.TeamIndex)
                triggers.Add("team-changed");
            if (previous.FormationIndex != current.FormationIndex)
                triggers.Add("formation-changed");

            return triggers.Count > 0 ? string.Join(",", triggers) : null;
        }

        private static string BuildClientPeerPreviewVisualTrigger(
            ClientPeerPreviewVisualState previous,
            ClientPeerPreviewVisualState current)
        {
            if (previous == null)
                return "created-or-first-observed";

            var triggers = new List<string>();
            float movementThresholdSquared =
                ClientPositionVisualMovementThreshold * ClientPositionVisualMovementThreshold;
            if (previous.HasFrame != current.HasFrame ||
                (previous.HasFrame && current.HasFrame &&
                 DistanceSquared(previous.FrameOrigin, current.FrameOrigin) >= movementThresholdSquared))
            {
                triggers.Add("frame-changed");
            }
            if (previous.VisualsValid != current.VisualsValid)
                triggers.Add("validity-changed");
            if (previous.VisualsVisible != current.VisualsVisible)
                triggers.Add("visibility-changed");
            if (!string.Equals(previous.CharacterId, current.CharacterId, StringComparison.Ordinal))
                triggers.Add("character-changed");

            return triggers.Count > 0 ? string.Join(",", triggers) : null;
        }

        private static string BuildAgentPositionVisualSummary(Agent agent)
        {
            try
            {
                ClientAgentPositionVisualState state = ReadClientAgentPositionVisualState(agent);
                return
                    "PositionVisualState={VisualPosition=" +
                    (state.HasVisualPosition ? FormatVec3(state.VisualPosition) : "unavailable") +
                    ",PositionToVisualDistance=" +
                    FormatOptionalDistance(state.Position, state.VisualPosition, state.HasVisualPosition) +
                    ",VisualFrameOrigin=" +
                    (state.HasVisualFrame ? FormatVec3(state.VisualFrameOrigin) : "unavailable") +
                    ",PositionToVisualFrameDistance=" +
                    FormatOptionalDistance(state.Position, state.VisualFrameOrigin, state.HasVisualFrame) +
                    ",VisualsValid=" + state.VisualsValid +
                    ",VisualsVisible=" + state.VisualsVisible + "}";
            }
            catch (Exception ex)
            {
                return "PositionVisualState={State=unavailable,Error=" + ex.GetType().Name + "}";
            }
        }

        private static string BuildPeerPreviewVisualSummary(MissionPeer missionPeer, int visualsIndex)
        {
            if (missionPeer == null)
                return "PeerPreviewVisual={State=mission-peer-absent,VisualsIndex=" + visualsIndex + "}";

            try
            {
                PeerVisualsHolder holder = TryGetPeerVisuals(missionPeer, visualsIndex);
                if (holder?.AgentVisuals == null)
                    return "PeerPreviewVisual={State=absent,VisualsIndex=" + visualsIndex + "}";

                ClientPeerPreviewVisualState state = ReadClientPeerPreviewVisualState(holder);
                return
                    "PeerPreviewVisual={State=present,VisualsIndex=" + visualsIndex +
                    ",CharacterId=" + (state.CharacterId ?? "null") +
                    ",FrameOrigin=" + (state.HasFrame ? FormatVec3(state.FrameOrigin) : "unavailable") +
                    ",VisualsValid=" + state.VisualsValid +
                    ",VisualsVisible=" + state.VisualsVisible + "}";
            }
            catch (Exception ex)
            {
                return
                    "PeerPreviewVisual={State=unavailable,VisualsIndex=" + visualsIndex +
                    ",Error=" + ex.GetType().Name + ":" + ex.Message + "}";
            }
        }

        private static PeerVisualsHolder TryGetPeerVisuals(MissionPeer missionPeer, int visualsIndex)
        {
            if (missionPeer == null || MissionPeerGetVisualsMethod == null)
                return null;

            try
            {
                return MissionPeerGetVisualsMethod.Invoke(missionPeer, new object[] { visualsIndex }) as PeerVisualsHolder;
            }
            catch
            {
                return null;
            }
        }

        private static string BuildAllPeerPreviewVisualsSummary(MissionPeer missionPeer)
        {
            if (missionPeer == null)
                return "PeerPreviewVisuals={State=mission-peer-absent}";

            var summaries = new List<string>();
            for (int visualsIndex = 0; visualsIndex <= MaxPeerPreviewVisualIndexToSample; visualsIndex++)
            {
                string summary = BuildPeerPreviewVisualSummary(missionPeer, visualsIndex);
                if (summary.IndexOf("State=present", StringComparison.Ordinal) >= 0)
                    summaries.Add(summary);
            }

            return summaries.Count > 0
                ? "PeerPreviewVisuals=[" + string.Join("; ", summaries) + "]"
                : "PeerPreviewVisuals={State=none}";
        }

        private static string BuildClosestLiveAgentSummary(Mission mission, Vec3 position, bool hasPosition)
        {
            if (!hasPosition || mission?.AllAgents == null)
                return "State=unavailable";

            Agent closest = null;
            float closestDistanceSquared = float.MaxValue;
            foreach (Agent agent in mission.AllAgents)
            {
                if (agent == null || agent.IsMount || !agent.IsActive())
                    continue;

                float distanceSquared = DistanceSquared(position, agent.Position);
                if (distanceSquared >= closestDistanceSquared)
                    continue;

                closest = agent;
                closestDistanceSquared = distanceSquared;
            }

            if (closest == null)
                return "State=none";

            return
                "AgentIndex=" + closest.Index +
                ",CharacterId=" + ((closest.Character as BasicCharacterObject)?.StringId ?? "null") +
                ",TeamIndex=" + (closest.Team?.TeamIndex.ToString() ?? "null") +
                ",TeamSide=" + (closest.Team?.Side.ToString() ?? "null") +
                ",Position=" + FormatVec3(closest.Position) +
                ",Distance=" + Math.Sqrt(closestDistanceSquared).ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string DescribePeer(NetworkCommunicator peer)
        {
            return peer == null
                ? "null"
                : (peer.UserName ?? "unnamed") + "#" + peer.Index;
        }

        private static string FormatOptionalDistance(Vec3 left, Vec3 right, bool available)
        {
            return available
                ? Math.Sqrt(DistanceSquared(left, right)).ToString("0.###", CultureInfo.InvariantCulture)
                : "unavailable";
        }

        private static float DistanceSquared(Vec3 left, Vec3 right)
        {
            float dx = left.x - right.x;
            float dy = left.y - right.y;
            float dz = left.z - right.z;
            return dx * dx + dy * dy + dz * dz;
        }

        private static string BuildAgentBuildDataPositionSummary(AgentBuildData agentBuildData)
        {
            if (agentBuildData == null)
                return "AgentBuildDataState={State=absent}";

            Vec3? initialPosition = agentBuildData.AgentInitialPosition;
            Vec2? initialDirection = agentBuildData.AgentInitialDirection;
            return
                "AgentBuildDataState={CharacterId=" + (agentBuildData.AgentCharacter?.StringId ?? "null") +
                ",TeamIndex=" + (agentBuildData.AgentTeam?.TeamIndex.ToString() ?? "null") +
                ",TeamSide=" + (agentBuildData.AgentTeam?.Side.ToString() ?? "null") +
                ",HasInitialPosition=" + initialPosition.HasValue +
                ",InitialPosition=" + FormatNullableVec3(initialPosition) +
                ",HasInitialDirection=" + initialDirection.HasValue +
                ",InitialDirection=" + FormatNullableVec2(initialDirection) +
                ",FormationTroopSpawnIndex=" + agentBuildData.AgentFormationTroopSpawnIndex +
                ",FormationTroopSpawnCount=" + agentBuildData.AgentFormationTroopSpawnCount +
                ",SpawnsIntoOwnFormation=" + agentBuildData.AgentSpawnsIntoOwnFormation +
                ",Formation=" + BuildFormationPositionSummary(
                    agentBuildData.AgentFormation,
                    initialPosition?.AsVec2) + "}";
        }

        private static string BuildFormationPositionSummary(Formation formation, Vec2? referencePosition)
        {
            if (formation == null)
                return "FormationState={State=absent}";

            bool orderPositionValid = formation.OrderPositionIsValid;
            Vec2 orderPosition = orderPositionValid ? formation.OrderPosition : Vec2.Invalid;
            string distanceToOrder = "unavailable";
            if (referencePosition.HasValue &&
                referencePosition.Value.IsValid &&
                orderPositionValid &&
                orderPosition.IsValid)
            {
                distanceToOrder = ((float)Math.Sqrt(
                        referencePosition.Value.DistanceSquared(orderPosition)))
                    .ToString("0.###", CultureInfo.InvariantCulture);
            }

            return
                "FormationState={Index=" + formation.FormationIndex +
                ",TeamIndex=" + (formation.Team?.TeamIndex.ToString() ?? "null") +
                ",TeamSide=" + (formation.Team?.Side.ToString() ?? "null") +
                ",OrderPositionValid=" + orderPositionValid +
                ",OrderPosition=" + (orderPositionValid ? FormatVec2(orderPosition) : "invalid") +
                ",Direction=" + FormatVec2(formation.Direction) +
                ",ReferenceDistanceToOrder=" + distanceToOrder + "}";
        }

        private static string FormatNullableVec3(Vec3? value)
        {
            return value.HasValue ? FormatVec3(value.Value) : "null";
        }

        private static string FormatNullableVec2(Vec2? value)
        {
            return value.HasValue ? FormatVec2(value.Value) : "null";
        }

        private static string FormatVec3(Vec3 value)
        {
            return
                "(" + value.x.ToString("0.###", CultureInfo.InvariantCulture) +
                "," + value.y.ToString("0.###", CultureInfo.InvariantCulture) +
                "," + value.z.ToString("0.###", CultureInfo.InvariantCulture) + ")";
        }

        private static string FormatVec2(Vec2 value)
        {
            return
                "(" + value.x.ToString("0.###", CultureInfo.InvariantCulture) +
                "," + value.y.ToString("0.###", CultureInfo.InvariantCulture) + ")";
        }

        private static string BuildEquipmentWithMountSummary(Equipment equipment)
        {
            return
                "Weapons={" + ExactCreateAgentPayloadDiagnostics.BuildEquipmentWeaponLayoutSummary(equipment) +
                "} WeaponSlots={" + BuildEquipmentWeaponSlotVector(equipment) +
                "} Mount={" + ExactCreateAgentPayloadDiagnostics.BuildEquipmentMountLayoutSummary(equipment) + "}";
        }

        private static void Log(string eventName, string details, bool persistToRuntimeBundle)
        {
            if (!persistToRuntimeBundle && !IsVerboseEnabled)
                return;

            ModLogger.Info(
                "ExactCreateAgentCorridorDiagnostics: " +
                (eventName ?? "unknown") +
                ". " +
                (details ?? string.Empty));

            if (!persistToRuntimeBundle)
                return;

            ExactBattleRuntimeBundleBridgeFile.AppendContractEvent(
                "create-agent-corridor-" + (eventName ?? "unknown"),
                details ?? string.Empty);
        }
    }
}
