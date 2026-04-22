using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using CoopSpectator.Infrastructure;
using CoopSpectator.Network.Messages;
using Newtonsoft.Json;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.MissionBehaviors
{
    internal static class CoopBattleNetworkRequestTransport
    {
        public static bool TrySelectSide(BattleSideEnum side, string source)
        {
            if (TrySendClientRequest(CoopBattleSelectionRequestKind.SelectSide, side, string.Empty, source))
                return true;

            return CoopBattleSelectionBridgeFile.WriteSelectSideRequest(side.ToString(), source ?? "network-fallback side");
        }

        public static bool TrySelectEntry(BattleSideEnum side, string entryId, string source)
        {
            if (TrySendClientRequest(CoopBattleSelectionRequestKind.SelectEntry, side, entryId, source))
                return true;

            bool wroteSide = CoopBattleSelectionBridgeFile.WriteSelectSideRequest(side.ToString(), source ?? "network-fallback entry-side");
            bool wroteEntry = CoopBattleSelectionBridgeFile.WriteSelectTroopRequest(entryId, source ?? "network-fallback entry");
            return wroteSide || wroteEntry;
        }

        public static bool TrySelectSpectator(string source)
        {
            if (TrySendClientRequest(CoopBattleSelectionRequestKind.Spectate, BattleSideEnum.None, string.Empty, source))
                return true;

            return CoopBattleSelectionBridgeFile.WriteSpectatorRequest(source ?? "network-fallback spectator");
        }

        public static bool TryRequestSpawn(string source)
        {
            if (TrySendClientRequest(CoopBattleSelectionRequestKind.SpawnNow, BattleSideEnum.None, string.Empty, source))
                return true;

            return CoopBattleSpawnBridgeFile.WriteSpawnNowRequest(source ?? "network-fallback spawn");
        }

        public static bool TryRequestForceRespawnable(string source)
        {
            if (TrySendClientRequest(CoopBattleSelectionRequestKind.ForceRespawnable, BattleSideEnum.None, string.Empty, source))
                return true;

            return CoopBattleSpawnBridgeFile.WriteForceRespawnableRequest(source ?? "network-fallback force-respawnable");
        }

        private static bool TrySendClientRequest(
            CoopBattleSelectionRequestKind requestKind,
            BattleSideEnum requestedSide,
            string selectionId,
            string source)
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return false;

            try
            {
                GameNetwork.BeginModuleEventAsClient();
                GameNetwork.WriteMessage(new CoopBattleSelectionClientRequestMessage(requestKind, requestedSide, selectionId));
                GameNetwork.EndModuleEventAsClient();
                ModLogger.Info(
                    "CoopBattleNetworkRequestTransport: sent client request. " +
                    "Kind=" + requestKind +
                    " Side=" + requestedSide +
                    " SelectionId=" + (selectionId ?? string.Empty) +
                    " Source=" + (source ?? "unknown"));
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopBattleNetworkRequestTransport: client request send failed. " +
                    "Kind=" + requestKind +
                    " Source=" + (source ?? "unknown") +
                    " Error=" + ex.Message);
                return false;
            }
        }
    }

    public sealed class CoopMissionNetworkBridge : MissionNetwork
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore
        };
        private const int MaxChunksPerPayloadPerTick = 2;
        private const int LargeBattleSnapshotChunkThreshold = 512;
        private const int VeryLargeBattleSnapshotChunkThreshold = 2048;
        private const int LargeBattleSnapshotChunksPerTick = 8;
        private const int VeryLargeBattleSnapshotChunksPerTick = 12;

        private readonly Dictionary<int, string> _lastSentStatusPayloadByPeer = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _lastSentBattleSnapshotPayloadByPeer = new Dictionary<int, string>();
        private readonly Dictionary<string, PendingPayloadTransmission> _pendingPayloadsByKey = new Dictionary<string, PendingPayloadTransmission>(StringComparer.Ordinal);
        private readonly Dictionary<string, PayloadAssemblyState> _clientPayloadAssemblies = new Dictionary<string, PayloadAssemblyState>(StringComparer.Ordinal);
        private string _cachedBattleSnapshotComparisonKey = string.Empty;
        private byte[] _cachedBattleSnapshotPayloadBytes = Array.Empty<byte>();
        private int _cachedBattleSnapshotLogicalBytes;
        private int _nextTransmissionId = 1;

        protected override void AddRemoveMessageHandlers(GameNetwork.NetworkMessageHandlerRegistererContainer registerer)
        {
            if (GameNetwork.IsServer)
            {
                registerer.RegisterBaseHandler<CoopBattleSelectionClientRequestMessage>(HandleClientSelectionRequest);
                ModLogger.Info("CoopMissionNetworkBridge: registered server selection request handler.");
            }

            if (GameNetwork.IsClient)
            {
                registerer.RegisterBaseHandler<CoopBattlePayloadChunkMessage>(HandleServerPayloadChunk);
                ModLogger.Info("CoopMissionNetworkBridge: registered client payload chunk handler.");
            }
        }

        protected override void OnUdpNetworkHandlerTick()
        {
            if (!GameNetwork.IsServer || Mission == null)
                return;

            TrySyncBattleSnapshotPayloads();
            TrySyncEntryStatusPayloads();
        }

        protected override void HandleNewClientAfterSynchronized(NetworkCommunicator networkPeer)
        {
            base.HandleNewClientAfterSynchronized(networkPeer);

            if (!GameNetwork.IsServer || networkPeer == null || networkPeer.IsServerPeer)
                return;

            // The authoritative sync path already runs in OnUdpNetworkHandlerTick().
            // Sending chunked payloads directly from the synchronized callback crashes the
            // dedicated runtime while writing EntryStatusSnapshot packets, so only arm the
            // next UDP tick here and let the regular sync loop send the initial payloads.
            ModLogger.Info(
                "CoopMissionNetworkBridge: deferred initial payload sync to UDP tick. " +
                "Peer=" + (networkPeer.UserName ?? "null") +
                " Reason=post-synchronize callback safety.");
        }

        protected override void HandlePlayerDisconnect(NetworkCommunicator networkPeer)
        {
            base.HandlePlayerDisconnect(networkPeer);

            if (networkPeer == null)
                return;

            _lastSentStatusPayloadByPeer.Remove(networkPeer.Index);
            _lastSentBattleSnapshotPayloadByPeer.Remove(networkPeer.Index);
            _pendingPayloadsByKey.Remove(BuildPendingTransmissionKey(networkPeer.Index, CoopBattlePayloadKind.EntryStatusSnapshot));
            _pendingPayloadsByKey.Remove(BuildPendingTransmissionKey(networkPeer.Index, CoopBattlePayloadKind.BattleSnapshot));
        }

        public override void OnRemoveBehavior()
        {
            _lastSentStatusPayloadByPeer.Clear();
            _lastSentBattleSnapshotPayloadByPeer.Clear();
            _pendingPayloadsByKey.Clear();
            _clientPayloadAssemblies.Clear();
            base.OnRemoveBehavior();
        }

        private bool HandleClientSelectionRequest(NetworkCommunicator peer, GameNetworkMessage baseMessage)
        {
            if (!(baseMessage is CoopBattleSelectionClientRequestMessage message))
                return false;

            try
            {
                bool applied = CoopMissionSpawnLogic.TryHandleNetworkSelectionRequest(
                    Mission,
                    peer,
                    message.RequestKind,
                    message.RequestedSide,
                    message.SelectionId,
                    "CoopMissionNetworkBridge");
                ModLogger.Info(
                    "CoopMissionNetworkBridge: handled client selection request. " +
                    "Peer=" + (peer?.UserName ?? "null") +
                    " Kind=" + message.RequestKind +
                    " Side=" + message.RequestedSide +
                    " SelectionId=" + (message.SelectionId ?? string.Empty) +
                    " Applied=" + applied);
                if (applied)
                    TrySendEntryStatusToPeer(peer, force: true);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: client selection request handling failed: " + ex.Message);
            }

            return true;
        }

        private void HandleServerPayloadChunk(GameNetworkMessage baseMessage)
        {
            if (!(baseMessage is CoopBattlePayloadChunkMessage message))
                return;

            try
            {
                AcceptClientPayloadChunk(message);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: server payload chunk handling failed: " + ex.Message);
            }
        }

        private void TrySyncEntryStatusPayloads()
        {
            if (GameNetwork.NetworkPeers == null)
                return;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (!IsEligibleRemotePeer(peer))
                    continue;

                TrySendEntryStatusToPeer(peer, force: false);
            }
        }

        private void TrySendEntryStatusToPeer(NetworkCommunicator peer, bool force)
        {
            if (!IsEligibleRemotePeer(peer) || Mission == null)
                return;

            MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
            if (missionPeer == null)
                return;

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot =
                CoopMissionSpawnLogic.BuildEntryStatusSnapshotForPeer(Mission, missionPeer, "CoopMissionNetworkBridge");
            if (snapshot == null)
                return;

            string comparisonJson = SerializeComparableEntryStatusPayload(snapshot);
            if (string.IsNullOrWhiteSpace(comparisonJson))
                return;

            string payloadJson = SerializePayload(snapshot);
            if (string.IsNullOrWhiteSpace(payloadJson))
                return;

            byte[] payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
            if (payloadBytes.Length <= 0)
                return;

            TryQueueOrContinuePayloadTransmission(
                peer,
                CoopBattlePayloadKind.EntryStatusSnapshot,
                payloadBytes,
                payloadBytes.Length,
                comparisonJson,
                force,
                _lastSentStatusPayloadByPeer);
        }

        private void TrySyncBattleSnapshotPayloads()
        {
            if (GameNetwork.NetworkPeers == null)
                return;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (!IsEligibleRemotePeer(peer))
                    continue;

                TrySendBattleSnapshotToPeer(peer, force: false);
            }
        }

        private void TrySendBattleSnapshotToPeer(NetworkCommunicator peer, bool force)
        {
            if (!IsEligibleRemotePeer(peer))
                return;

            BattleSnapshotMessage snapshot = BattleSnapshotRuntimeState.GetCurrent();
            if (snapshot?.Sides == null || snapshot.Sides.Count <= 0)
                return;

            if (!TryGetBattleSnapshotTransmissionPayload(
                    snapshot,
                    out byte[] payloadBytes,
                    out int logicalByteCount,
                    out string comparisonKey))
            {
                return;
            }

            TryQueueOrContinuePayloadTransmission(
                peer,
                CoopBattlePayloadKind.BattleSnapshot,
                payloadBytes,
                logicalByteCount,
                comparisonKey,
                force,
                _lastSentBattleSnapshotPayloadByPeer);
        }

        private void TryQueueOrContinuePayloadTransmission(
            NetworkCommunicator peer,
            CoopBattlePayloadKind payloadKind,
            byte[] payloadBytes,
            int logicalByteCount,
            string comparisonKey,
            bool force,
            Dictionary<int, string> lastSentPayloadByPeer)
        {
            if (peer == null || payloadBytes == null || payloadBytes.Length <= 0 || lastSentPayloadByPeer == null)
            {
                return;
            }

            string transmissionKey = BuildPendingTransmissionKey(peer.Index, payloadKind);
            bool hasPending = _pendingPayloadsByKey.TryGetValue(transmissionKey, out PendingPayloadTransmission pendingTransmission) &&
                pendingTransmission != null;

            if (!force &&
                !hasPending &&
                lastSentPayloadByPeer.TryGetValue(peer.Index, out string previousPayload) &&
                string.Equals(previousPayload, comparisonKey, StringComparison.Ordinal))
            {
                return;
            }

            if (!hasPending ||
                !string.Equals(pendingTransmission.ComparisonKey, comparisonKey, StringComparison.Ordinal))
            {
                pendingTransmission = PendingPayloadTransmission.Create(
                    payloadKind,
                    payloadBytes,
                    logicalByteCount,
                    comparisonKey,
                    NextTransmissionId());
                if (pendingTransmission == null)
                    return;

                _pendingPayloadsByKey[transmissionKey] = pendingTransmission;
                ModLogger.Info(
                    "CoopMissionNetworkBridge: queued payload transmission. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " Kind=" + payloadKind +
                    " TransmissionId=" + pendingTransmission.TransmissionId +
                    " Bytes=" + pendingTransmission.TotalBytes +
                    (pendingTransmission.LogicalBytes != pendingTransmission.TotalBytes
                        ? " LogicalBytes=" + pendingTransmission.LogicalBytes
                        : string.Empty) +
                    " Chunks=" + pendingTransmission.ChunkCount +
                    " ChunkBytes=" + CoopBattlePayloadChunkMessage.MaxChunkBytes);
            }

            if (!TryFlushPendingPayload(peer, pendingTransmission))
                return;

            if (!pendingTransmission.IsCompleted)
                return;

            _pendingPayloadsByKey.Remove(transmissionKey);
            lastSentPayloadByPeer[peer.Index] = comparisonKey;
            ModLogger.Info(
                "CoopMissionNetworkBridge: completed payload transmission. " +
                "Peer=" + (peer.UserName ?? "null") +
                " Kind=" + payloadKind +
                " TransmissionId=" + pendingTransmission.TransmissionId +
                " Bytes=" + pendingTransmission.TotalBytes +
                (pendingTransmission.LogicalBytes != pendingTransmission.TotalBytes
                    ? " LogicalBytes=" + pendingTransmission.LogicalBytes
                    : string.Empty) +
                " Chunks=" + pendingTransmission.ChunkCount);
        }

        private static bool TryFlushPendingPayload(NetworkCommunicator peer, PendingPayloadTransmission pendingTransmission)
        {
            if (peer == null || pendingTransmission == null || pendingTransmission.IsCompleted)
                return false;

            int chunksSent = 0;
            int chunkBudget = ResolveChunkBudgetPerTick(pendingTransmission);
            while (chunksSent < chunkBudget && pendingTransmission.NextChunkIndex < pendingTransmission.ChunkCount)
            {
                byte[] chunkBytes = pendingTransmission.Chunks[pendingTransmission.NextChunkIndex] ?? Array.Empty<byte>();
                GameNetwork.BeginModuleEventAsServer(peer);
                GameNetwork.WriteMessage(new CoopBattlePayloadChunkMessage(
                    pendingTransmission.PayloadKind,
                    pendingTransmission.TransmissionId,
                    pendingTransmission.NextChunkIndex,
                    pendingTransmission.ChunkCount,
                    chunkBytes));
                GameNetwork.EndModuleEventAsServer();
                pendingTransmission.NextChunkIndex++;
                chunksSent++;
            }

            return chunksSent > 0;
        }

        private static int ResolveChunkBudgetPerTick(PendingPayloadTransmission pendingTransmission)
        {
            if (pendingTransmission == null || pendingTransmission.PayloadKind != CoopBattlePayloadKind.BattleSnapshot)
                return MaxChunksPerPayloadPerTick;

            if (pendingTransmission.ChunkCount > VeryLargeBattleSnapshotChunkThreshold)
                return VeryLargeBattleSnapshotChunksPerTick;

            if (pendingTransmission.ChunkCount > LargeBattleSnapshotChunkThreshold)
                return LargeBattleSnapshotChunksPerTick;

            return MaxChunksPerPayloadPerTick;
        }

        private void AcceptClientPayloadChunk(CoopBattlePayloadChunkMessage message)
        {
            if (message == null || message.ChunkCount <= 0 || message.ChunkIndex < 0 || message.ChunkIndex >= message.ChunkCount)
                return;

            string assemblyKey = BuildAssemblyKey(message.PayloadKind, message.TransmissionId);
            bool createdAssembly = false;
            if (!_clientPayloadAssemblies.TryGetValue(assemblyKey, out PayloadAssemblyState assemblyState) ||
                assemblyState == null ||
                assemblyState.ChunkCount != message.ChunkCount)
            {
                assemblyState = new PayloadAssemblyState(message.PayloadKind, message.TransmissionId, message.ChunkCount);
                _clientPayloadAssemblies[assemblyKey] = assemblyState;
                createdAssembly = true;
            }

            if (createdAssembly)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: received first payload chunk. " +
                    "Kind=" + message.PayloadKind +
                    " TransmissionId=" + message.TransmissionId +
                    " ChunkIndex=" + message.ChunkIndex +
                    " ChunkCount=" + message.ChunkCount +
                    " Bytes=" + (message.PayloadBytes?.Length ?? 0));
            }

            if (assemblyState.Chunks[message.ChunkIndex] == null)
                assemblyState.ReceivedChunkCount++;
            assemblyState.Chunks[message.ChunkIndex] = message.PayloadBytes ?? Array.Empty<byte>();

            if (assemblyState.ReceivedChunkCount < assemblyState.ChunkCount)
                return;

            _clientPayloadAssemblies.Remove(assemblyKey);
            byte[] payloadBytes = assemblyState.Combine();
            ModLogger.Info(
                "CoopMissionNetworkBridge: assembled client payload. " +
                "Kind=" + assemblyState.PayloadKind +
                " TransmissionId=" + assemblyState.TransmissionId +
                " Bytes=" + payloadBytes.Length +
                " Chunks=" + assemblyState.ChunkCount);
            ApplyCompletedPayload(assemblyState.PayloadKind, payloadBytes);
        }

        private void ApplyCompletedPayload(CoopBattlePayloadKind payloadKind, byte[] payloadBytes)
        {
            if (!TryDecodePayloadJson(payloadKind, payloadBytes, out string payloadJson))
                return;

            if (string.IsNullOrWhiteSpace(payloadJson))
                return;

            switch (payloadKind)
            {
                case CoopBattlePayloadKind.EntryStatusSnapshot:
                {
                    CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot =
                        JsonConvert.DeserializeObject<CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot>(payloadJson, JsonSettings);
                    if (snapshot != null)
                    {
                        CoopBattleEntryStatusBridgeFile.WriteStatus(snapshot);
                        ModLogger.Info(
                            "CoopMissionNetworkBridge: applied client payload. " +
                            "Kind=" + payloadKind +
                            " AssignedSide=" + (snapshot.AssignedSide ?? string.Empty) +
                            " SelectedEntryId=" + (snapshot.SelectedEntryId ?? string.Empty) +
                            " SelectableEntryIds=" + (snapshot.SelectableEntryIds ?? string.Empty) +
                            " CanRespawn=" + snapshot.CanRespawn);
                    }
                    break;
                }
                case CoopBattlePayloadKind.BattleSnapshot:
                {
                    BattleSnapshotMessage snapshot =
                        JsonConvert.DeserializeObject<BattleSnapshotMessage>(payloadJson, JsonSettings);
                    if (snapshot != null)
                    {
                        BattleSnapshotRuntimeState.SetCurrent(snapshot, "CoopMissionNetworkBridge");
                        ModLogger.Info(
                            "CoopMissionNetworkBridge: applied client payload. " +
                            "Kind=" + payloadKind +
                            " BattleId=" + (snapshot.BattleId ?? string.Empty) +
                            " MapScene=" + (snapshot.MapScene ?? string.Empty) +
                            " Parties=" + (snapshot.Sides?.Sum(side => side?.Parties?.Count ?? 0) ?? 0) +
                            " Sides=" + (snapshot.Sides?.Count ?? 0));
                    }
                    break;
                }
            }
        }

        private static string SerializePayload(object payload)
        {
            try
            {
                return JsonConvert.SerializeObject(payload, JsonSettings);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: payload serialization failed: " + ex.Message);
                return string.Empty;
            }
        }

        private static string SerializeComparableEntryStatusPayload(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot)
        {
            if (snapshot == null)
                return string.Empty;

            DateTime originalUpdatedUtc = snapshot.UpdatedUtc;
            try
            {
                snapshot.UpdatedUtc = DateTime.MinValue;
                return SerializePayload(snapshot);
            }
            finally
            {
                snapshot.UpdatedUtc = originalUpdatedUtc;
            }
        }

        private bool TryGetBattleSnapshotTransmissionPayload(
            BattleSnapshotMessage snapshot,
            out byte[] payloadBytes,
            out int logicalByteCount,
            out string comparisonKey)
        {
            payloadBytes = Array.Empty<byte>();
            logicalByteCount = 0;
            comparisonKey = BuildBattleSnapshotComparisonKey(snapshot, BattleSnapshotRuntimeState.GetUpdatedUtc());
            if (string.IsNullOrWhiteSpace(comparisonKey))
                return false;

            if (string.Equals(_cachedBattleSnapshotComparisonKey, comparisonKey, StringComparison.Ordinal) &&
                _cachedBattleSnapshotPayloadBytes != null &&
                _cachedBattleSnapshotPayloadBytes.Length > 0)
            {
                payloadBytes = _cachedBattleSnapshotPayloadBytes;
                logicalByteCount = _cachedBattleSnapshotLogicalBytes;
                return true;
            }

            string payloadJson = SerializePayload(snapshot);
            if (string.IsNullOrWhiteSpace(payloadJson))
                return false;

            byte[] rawBytes = Encoding.UTF8.GetBytes(payloadJson);
            if (rawBytes.Length <= 0)
                return false;

            byte[] wireBytes = CompressBattleSnapshotPayload(rawBytes, out bool compressed);
            payloadBytes = wireBytes ?? rawBytes;
            logicalByteCount = rawBytes.Length;

            _cachedBattleSnapshotComparisonKey = comparisonKey;
            _cachedBattleSnapshotPayloadBytes = payloadBytes;
            _cachedBattleSnapshotLogicalBytes = logicalByteCount;

            int chunkCount = Math.Max(1, (payloadBytes.Length + CoopBattlePayloadChunkMessage.MaxChunkBytes - 1) / CoopBattlePayloadChunkMessage.MaxChunkBytes);
            ModLogger.Info(
                "CoopMissionNetworkBridge: prepared battle snapshot transport payload. " +
                "ComparisonKey=" + comparisonKey +
                " RawBytes=" + rawBytes.Length +
                " WireBytes=" + payloadBytes.Length +
                " Compressed=" + compressed +
                " Chunks=" + chunkCount +
                " Entries=" + GetBattleSnapshotEntryCount(snapshot));
            return true;
        }

        private static string BuildBattleSnapshotComparisonKey(BattleSnapshotMessage snapshot, DateTime updatedUtc)
        {
            if (snapshot == null)
                return string.Empty;

            string sidesSignature = snapshot.Sides == null
                ? "none"
                : string.Join(",",
                    snapshot.Sides.Select(side =>
                        (side?.SideId ?? "null") + ":" +
                        (side?.TotalManCount ?? 0) + ":" +
                        (side?.Troops?.Count ?? 0)));
            return (snapshot.BattleId ?? "null") +
                   "|" +
                   (snapshot.MapScene ?? "null") +
                   "|" +
                   sidesSignature +
                   "|" +
                   updatedUtc.Ticks;
        }

        private static int GetBattleSnapshotEntryCount(BattleSnapshotMessage snapshot)
        {
            return snapshot?.Sides?.Sum(side => side?.Troops?.Count ?? 0) ?? 0;
        }

        private static byte[] CompressBattleSnapshotPayload(byte[] rawBytes, out bool compressed)
        {
            compressed = false;
            if (rawBytes == null || rawBytes.Length <= 0)
                return Array.Empty<byte>();

            try
            {
                using (var output = new MemoryStream())
                {
                    using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
                    {
                        gzip.Write(rawBytes, 0, rawBytes.Length);
                    }

                    byte[] compressedBytes = output.ToArray();
                    if (compressedBytes.Length > 0 && compressedBytes.Length < rawBytes.Length)
                    {
                        compressed = true;
                        return compressedBytes;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: battle snapshot compression failed, falling back to raw payload. Error=" + ex.Message);
            }

            return rawBytes;
        }

        private static bool TryDecodePayloadJson(CoopBattlePayloadKind payloadKind, byte[] payloadBytes, out string payloadJson)
        {
            payloadJson = string.Empty;
            if (payloadBytes == null || payloadBytes.Length <= 0)
                return false;

            byte[] decodedBytes = payloadBytes;
            if (payloadKind == CoopBattlePayloadKind.BattleSnapshot && IsGzipPayload(payloadBytes))
            {
                try
                {
                    using (var input = new MemoryStream(payloadBytes))
                    using (var gzip = new GZipStream(input, CompressionMode.Decompress))
                    using (var output = new MemoryStream())
                    {
                        gzip.CopyTo(output);
                        decodedBytes = output.ToArray();
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Info("CoopMissionNetworkBridge: battle snapshot decompression failed. Error=" + ex.Message);
                    return false;
                }
            }

            payloadJson = decodedBytes.Length <= 0 ? string.Empty : Encoding.UTF8.GetString(decodedBytes);
            return !string.IsNullOrWhiteSpace(payloadJson);
        }

        private static bool IsGzipPayload(byte[] payloadBytes)
        {
            return payloadBytes != null &&
                   payloadBytes.Length >= 2 &&
                   payloadBytes[0] == 0x1F &&
                   payloadBytes[1] == 0x8B;
        }

        private int NextTransmissionId()
        {
            if (_nextTransmissionId >= 1048575)
                _nextTransmissionId = 1;

            return _nextTransmissionId++;
        }

        private static bool IsEligibleRemotePeer(NetworkCommunicator peer)
        {
            return peer != null &&
                !peer.IsServerPeer &&
                peer.IsConnectionActive &&
                peer.IsSynchronized;
        }

        private static string BuildAssemblyKey(CoopBattlePayloadKind payloadKind, int transmissionId)
        {
            return ((int)payloadKind) + "|" + transmissionId;
        }

        private static string BuildPendingTransmissionKey(int peerIndex, CoopBattlePayloadKind payloadKind)
        {
            return peerIndex + "|" + (int)payloadKind;
        }

        private sealed class PayloadAssemblyState
        {
            public PayloadAssemblyState(CoopBattlePayloadKind payloadKind, int transmissionId, int chunkCount)
            {
                PayloadKind = payloadKind;
                TransmissionId = transmissionId;
                ChunkCount = Math.Max(1, chunkCount);
                Chunks = new byte[ChunkCount][];
                ReceivedChunkCount = 0;
            }

            public CoopBattlePayloadKind PayloadKind { get; }
            public int TransmissionId { get; }
            public int ChunkCount { get; }
            public int ReceivedChunkCount { get; set; }
            public byte[][] Chunks { get; }

            public byte[] Combine()
            {
                int totalBytes = Chunks.Where(chunk => chunk != null).Sum(chunk => chunk.Length);
                byte[] combined = totalBytes > 0 ? new byte[totalBytes] : Array.Empty<byte>();
                int offset = 0;
                for (int i = 0; i < Chunks.Length; i++)
                {
                    byte[] chunk = Chunks[i];
                    if (chunk == null || chunk.Length <= 0)
                        continue;

                    Buffer.BlockCopy(chunk, 0, combined, offset, chunk.Length);
                    offset += chunk.Length;
                }

                return combined;
            }
        }

        private sealed class PendingPayloadTransmission
        {
            private PendingPayloadTransmission(
                CoopBattlePayloadKind payloadKind,
                int transmissionId,
                int logicalBytes,
                string comparisonKey,
                byte[][] chunks,
                int totalBytes)
            {
                PayloadKind = payloadKind;
                TransmissionId = transmissionId;
                LogicalBytes = Math.Max(0, logicalBytes);
                ComparisonKey = comparisonKey ?? string.Empty;
                Chunks = chunks ?? Array.Empty<byte[]>();
                TotalBytes = Math.Max(0, totalBytes);
                NextChunkIndex = 0;
            }

            public CoopBattlePayloadKind PayloadKind { get; }
            public int TransmissionId { get; }
            public int LogicalBytes { get; }
            public string ComparisonKey { get; }
            public byte[][] Chunks { get; }
            public int TotalBytes { get; }
            public int NextChunkIndex { get; set; }
            public int ChunkCount => Chunks.Length;
            public bool IsCompleted => NextChunkIndex >= ChunkCount;

            public static PendingPayloadTransmission Create(
                CoopBattlePayloadKind payloadKind,
                byte[] payloadBytes,
                int logicalByteCount,
                string comparisonKey,
                int transmissionId)
            {
                if (payloadBytes == null || payloadBytes.Length <= 0)
                    return null;

                int chunkCount = Math.Max(1, (payloadBytes.Length + CoopBattlePayloadChunkMessage.MaxChunkBytes - 1) / CoopBattlePayloadChunkMessage.MaxChunkBytes);
                if (chunkCount > CoopBattlePayloadChunkMessage.MaxChunkCount)
                {
                    ModLogger.Info(
                        "CoopMissionNetworkBridge: payload too large for staged chunk transport. " +
                        "Kind=" + payloadKind +
                        " Bytes=" + payloadBytes.Length +
                        " Chunks=" + chunkCount +
                        " ChunkBytes=" + CoopBattlePayloadChunkMessage.MaxChunkBytes);
                    return null;
                }

                byte[][] chunks = new byte[chunkCount][];
                for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
                {
                    int chunkOffset = chunkIndex * CoopBattlePayloadChunkMessage.MaxChunkBytes;
                    int chunkLength = Math.Min(CoopBattlePayloadChunkMessage.MaxChunkBytes, payloadBytes.Length - chunkOffset);
                    if (chunkLength < 0)
                        chunkLength = 0;

                    byte[] chunkBytes = chunkLength > 0 ? new byte[chunkLength] : Array.Empty<byte>();
                    if (chunkLength > 0)
                        Buffer.BlockCopy(payloadBytes, chunkOffset, chunkBytes, 0, chunkLength);
                    chunks[chunkIndex] = chunkBytes;
                }

                return new PendingPayloadTransmission(
                    payloadKind,
                    transmissionId,
                    logicalByteCount,
                    comparisonKey,
                    chunks,
                    payloadBytes.Length);
            }
        }
    }
}
