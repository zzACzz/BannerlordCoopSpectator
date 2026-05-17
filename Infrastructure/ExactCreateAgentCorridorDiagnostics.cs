using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using CoopSpectator.MissionBehaviors;
using NetworkMessages.FromServer;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
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

        private sealed class ServerCreateAgentPendingState
        {
            public string EntryId { get; set; }
            public string TroopId { get; set; }
            public string PayloadDiagnosticSummary { get; set; }
            public string PayloadWeaponLayoutSummary { get; set; }
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
            bool injectEquipment,
            bool spawnFromAgentVisuals)
        {
            if (!GameNetwork.IsServer || exactOrigin == null || entryState == null)
                return;

            lock (Sync)
            {
                PendingServerStatesByEntryId[entryState.EntryId ?? string.Empty] = new ServerCreateAgentPendingState
                {
                    EntryId = entryState.EntryId,
                    TroopId = exactOrigin.TroopId,
                    PayloadDiagnosticSummary = payloadDiagnostic?.ToSummary() ?? "ExactCreateAgentPayloadDiagnostic={State=absent}",
                    PayloadWeaponLayoutSummary = payloadDiagnostic?.ToWeaponLayoutSummary() ?? "ExactCreateAgentWeaponLayout={State=absent}",
                    EntryWeaponSlotVector = BuildEntryWeaponSlotVector(entryState),
                    PreSpawnWeaponSlotVector = BuildEquipmentWeaponSlotVector(exactEquipment),
                    PreSpawnMountSummary = ExactCreateAgentPayloadDiagnostics.BuildEquipmentMountLayoutSummary(exactEquipment),
                    PreSpawnNonWeaponSlotVector = BuildEquipmentNonWeaponSlotVector(exactEquipment),
                    EntryWeaponSlots = BuildEntryWeaponSlots(entryState),
                    PreSpawnWeaponSlots = BuildEquipmentWeaponSlots(exactEquipment),
                    ExpectedItemOriginById = BuildExpectedItemOriginById(exactEquipment)
                };
            }

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
                " " + (payloadDiagnostic?.ToSummary() ?? "ExactCreateAgentPayloadDiagnostic={State=absent}") +
                " " + (payloadDiagnostic?.ToWeaponLayoutSummary() ?? "ExactCreateAgentWeaponLayout={State=absent}");
            Log("server-pre-spawn-payload", details, persistToRuntimeBundle: false);
        }

        internal static void ObserveServerSpawnResult(
            ExactCampaignSnapshotAgentOrigin exactOrigin,
            ExactCreateAgentPayloadDiagnosticDecision payloadDiagnostic,
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

                string details =
                    "EntryId=" + (exactOrigin.EntryId ?? "null") +
                    " TroopId=" + (exactOrigin.TroopId ?? "null") +
                    " AgentIndex=" + (result?.Index.ToString() ?? "null") +
                    " SpawnFromAgentVisuals=" + spawnFromAgentVisuals +
                    " EquipmentInjected=" + equipmentInjected +
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
            if (!DoesServerSpawnStateMatchOutgoingCreateAgent(state, createAgent, actualMissionSlots, out string stateMismatchReason))
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
                "sanitized-to-server-spawn-baseline:" +
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

            string payloadSummary = BuildCreateAgentPayloadSummary(createAgent);
            string candidateSummary = BuildCreateAgentCandidateSummary(
                createAgent,
                out string candidateEntryId,
                out string candidatePayloadComparisonSummary);
            string snapshotSummary =
                "SnapshotReady=" + snapshotReady +
                " SnapshotReadiness=" + (snapshotReadinessSummary ?? "unknown");
            ClientCreateAgentCorridorState state = GetOrCreateState(createAgent.AgentIndex);
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
            string payloadSummary = BuildSetWieldedPayloadSummary(message);
            string resolvedEntrySummary = BuildResolvedEntrySummary(agent, out string resolvedEntryId);
            lock (Sync)
            {
                state.WieldEventCount++;
                state.LastResolvedEntrySummary = resolvedEntrySummary;
                state.LastResolvedEntryId = resolvedEntryId;
                state.LastObservedUtc = DateTime.UtcNow;
                wieldCount = state.WieldEventCount;
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

            return
                "AgentState={Index=" + agent.Index +
                ",CharacterId=" + ((agent.Character as BasicCharacterObject)?.StringId ?? "null") +
                ",TeamSide=" + (agent.Team?.Side.ToString() ?? "null") +
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

        private static string BuildEquipmentWithMountSummary(Equipment equipment)
        {
            return
                "Weapons={" + ExactCreateAgentPayloadDiagnostics.BuildEquipmentWeaponLayoutSummary(equipment) +
                "} WeaponSlots={" + BuildEquipmentWeaponSlotVector(equipment) +
                "} Mount={" + ExactCreateAgentPayloadDiagnostics.BuildEquipmentMountLayoutSummary(equipment) + "}";
        }

        private static void Log(string eventName, string details, bool persistToRuntimeBundle)
        {
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
