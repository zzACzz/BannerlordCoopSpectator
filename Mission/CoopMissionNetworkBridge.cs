using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using CoopSpectator.Network.Messages;
using CoopSpectator.Patches;
using Newtonsoft.Json;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.MissionBehaviors
{
    internal static class CoopBattleNetworkRequestTransport
    {
        private enum ClientRequestDispatchResult
        {
            Sent,
            Failed,
            Suppressed
        }

        public static bool TrySelectSide(BattleSideEnum side, string source)
        {
            ClientRequestDispatchResult dispatchResult =
                TryDispatchClientSelectionOrSpawnRequest(
                    CoopBattleSelectionRequestKind.SelectSide,
                    side,
                    string.Empty,
                    source);
            if (dispatchResult == ClientRequestDispatchResult.Sent)
                return true;
            if (dispatchResult == ClientRequestDispatchResult.Suppressed)
                return false;

            return CoopBattleSelectionBridgeFile.WriteSelectSideRequest(side.ToString(), source ?? "network-fallback side");
        }

        public static bool TrySelectEntry(BattleSideEnum side, string entryId, string source)
        {
            ClientRequestDispatchResult dispatchResult =
                TryDispatchClientSelectionOrSpawnRequest(
                    CoopBattleSelectionRequestKind.SelectEntry,
                    side,
                    entryId,
                    source);
            if (dispatchResult == ClientRequestDispatchResult.Sent)
                return true;
            if (dispatchResult == ClientRequestDispatchResult.Suppressed)
                return false;

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
            ClientRequestDispatchResult dispatchResult =
                TryDispatchClientSelectionOrSpawnRequest(
                    CoopBattleSelectionRequestKind.SpawnNow,
                    BattleSideEnum.None,
                    string.Empty,
                    source);
            if (dispatchResult == ClientRequestDispatchResult.Sent)
                return true;
            if (dispatchResult == ClientRequestDispatchResult.Suppressed)
                return false;

            return CoopBattleSpawnBridgeFile.WriteSpawnNowRequest(source ?? "network-fallback spawn");
        }

        public static bool TryRequestForceRespawnable(string source)
        {
            if (TrySendClientRequest(CoopBattleSelectionRequestKind.ForceRespawnable, BattleSideEnum.None, string.Empty, source))
                return true;

            return CoopBattleSpawnBridgeFile.WriteForceRespawnableRequest(source ?? "network-fallback force-respawnable");
        }

        public static bool TryAcknowledgeBattleSnapshot(int transmissionId, string source, string catalogHash = null)
        {
            if (transmissionId <= 0)
                return false;

            return TrySendClientRequest(
                CoopBattleSelectionRequestKind.BattleSnapshotReadyAck,
                BattleSideEnum.None,
                string.IsNullOrWhiteSpace(catalogHash)
                    ? transmissionId.ToString()
                    : transmissionId + "|" + catalogHash,
                source);
        }

        public static bool TryRequestBattleSnapshotBootstrap(string source)
        {
            return TrySendClientRequest(
                CoopBattleSelectionRequestKind.BattleSnapshotBootstrapRequest,
                BattleSideEnum.None,
                string.Empty,
                source);
        }

        public static bool TryAcknowledgeBattleReconnectFinalize(int transmissionId, string source)
        {
            if (transmissionId <= 0)
                return false;

            return TrySendClientRequest(
                CoopBattleSelectionRequestKind.BattleReconnectFinalizeReadyAck,
                BattleSideEnum.None,
                transmissionId.ToString(),
                source);
        }

        private static bool ShouldSuppressClientSelectionOrSpawnRequest(
            CoopBattleSelectionRequestKind requestKind,
            out string reason)
        {
            reason = null;
            if (requestKind == CoopBattleSelectionRequestKind.SelectSide ||
                requestKind == CoopBattleSelectionRequestKind.SelectEntry ||
                requestKind == CoopBattleSelectionRequestKind.SpawnNow)
            {
                if (!CoopMissionSpawnLogic.IsClientBattlefieldSelectionMaterializationBarrierClosed(out string materializationSummary))
                {
                    reason = "battlefield-materialization-barrier-open:" + (materializationSummary ?? "unknown");
                    return true;
                }
            }

            if (requestKind != CoopBattleSelectionRequestKind.SelectEntry &&
                requestKind != CoopBattleSelectionRequestKind.SpawnNow)
            {
                return false;
            }

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status = CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (status == null)
                return false;

            string spawnStatus = status.SpawnStatus?.Trim();
            if (string.Equals(spawnStatus, CoopBattleSpawnStatus.Pending.ToString(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(spawnStatus, CoopBattleSpawnStatus.Validating.ToString(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(spawnStatus, CoopBattleSpawnStatus.Validated.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                reason = "spawn-transition-in-flight:" + spawnStatus;
                return true;
            }

            string lifecycleState = status.LifecycleState?.Trim();
            if (status.HasAgent &&
                !status.CanRespawn &&
                string.Equals(lifecycleState, CoopBattlePeerLifecycleStatus.Alive.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                reason = "peer-still-occupies-active-life";
                return true;
            }

            return false;
        }

        private static ClientRequestDispatchResult TryDispatchClientSelectionOrSpawnRequest(
            CoopBattleSelectionRequestKind requestKind,
            BattleSideEnum requestedSide,
            string selectionId,
            string source)
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return ClientRequestDispatchResult.Failed;

            if (ShouldSuppressClientSelectionOrSpawnRequest(requestKind, out string suppressionReason))
            {
                ModLogger.Info(
                    "CoopBattleNetworkRequestTransport: suppressed client request while local selection or spawn handoff is still unsafe. " +
                    "Kind=" + requestKind +
                    " Side=" + requestedSide +
                    " SelectionId=" + (selectionId ?? string.Empty) +
                    " Reason=" + (suppressionReason ?? "unknown") +
                    " Source=" + (source ?? "unknown"));
                return ClientRequestDispatchResult.Suppressed;
            }

            return TrySendClientRequest(requestKind, requestedSide, selectionId, source)
                ? ClientRequestDispatchResult.Sent
                : ClientRequestDispatchResult.Failed;
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
        internal readonly struct ClientBattleSnapshotProgressInfo
        {
            public ClientBattleSnapshotProgressInfo(
                int transmissionId,
                int chunkCount,
                int receivedChunkCount,
                int highestContiguousChunkIndex,
                bool isStalled)
            {
                TransmissionId = transmissionId;
                ChunkCount = Math.Max(0, chunkCount);
                ReceivedChunkCount = Math.Max(0, receivedChunkCount);
                HighestContiguousChunkIndex = Math.Max(-1, highestContiguousChunkIndex);
                IsStalled = isStalled;
            }

            public int TransmissionId { get; }
            public int ChunkCount { get; }
            public int ReceivedChunkCount { get; }
            public int HighestContiguousChunkIndex { get; }
            public bool IsStalled { get; }
            public int PercentComplete =>
                ChunkCount <= 0
                    ? 0
                    : Math.Max(0, Math.Min(100, (int)Math.Round((double)ReceivedChunkCount * 100d / ChunkCount)));
        }

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore,
            DefaultValueHandling = DefaultValueHandling.Ignore
        };
        private static readonly bool UseBattleSnapshotTransportV2 = true;
        private const int BattleSnapshotTransportSchemaVersion = 1;
        private const int MaxStatusChunksPerPayloadPerTick = 2;
        private const int MaxBattleSnapshotChunksPerPayloadPerTick = 2;
        private static readonly TimeSpan BattleSnapshotAckRetryDelay = TimeSpan.FromSeconds(6);
        private static readonly TimeSpan BattleSnapshotManifestRetryDelay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan BattleSnapshotRangeAckStallDelay = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan BattleSnapshotInitialChunkRequestRetryDelay = TimeSpan.FromMilliseconds(350);
        private static readonly TimeSpan BattleSnapshotAssemblyIdleTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan BattleSnapshotBootstrapRequestRetryDelay = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan BattleReconnectFinalizeReadyAckRetryDelay = TimeSpan.FromMilliseconds(900);
        private const int BattleSnapshotInitialWindowChunks = 4;
        private const int BattleSnapshotMaxInflightChunksPerPeer = 8;
        private const int BattleSnapshotRangeAckEveryNewChunks = 4;
        private const int BattleSnapshotMaxConcurrentHeavyPeers = 4;

        private readonly Dictionary<int, string> _lastSentStatusPayloadByPeer = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _lastSentMaterializedAgentEntryPayloadByPeer = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _lastSentBattleSnapshotPayloadByPeer = new Dictionary<int, string>();
        private readonly Dictionary<int, DateTime> _lastCompletedBattleSnapshotTransmissionUtcByPeer = new Dictionary<int, DateTime>();
        private readonly Dictionary<int, DateTime> _lastBattleSnapshotRetryUtcByPeer = new Dictionary<int, DateTime>();
        private readonly Dictionary<string, PendingPayloadTransmission> _pendingPayloadsByKey = new Dictionary<string, PendingPayloadTransmission>(StringComparer.Ordinal);
        private readonly Dictionary<string, PayloadAssemblyState> _clientPayloadAssemblies = new Dictionary<string, PayloadAssemblyState>(StringComparer.Ordinal);
        private readonly Dictionary<int, BattleSnapshotTransportState> _battleSnapshotTransportStatesByPeer = new Dictionary<int, BattleSnapshotTransportState>();
        private readonly Dictionary<int, BattleSnapshotClientAssemblyState> _clientBattleSnapshotAssembliesByTransmission = new Dictionary<int, BattleSnapshotClientAssemblyState>();
        private static readonly Dictionary<int, int> _expectedBattleSnapshotTransmissionIdByPeer = new Dictionary<int, int>();
        private static readonly Dictionary<int, int> _acknowledgedBattleSnapshotTransmissionIdByPeer = new Dictionary<int, int>();
        private static int _clientObservedBattleSnapshotTransmissionId;
        private static string _clientObservedBattleSnapshotPayloadHash = string.Empty;
        private static int _clientAppliedBattleSnapshotTransmissionId;
        private static string _clientAppliedBattleSnapshotPayloadHash = string.Empty;
        private string _cachedBattleSnapshotComparisonKey = string.Empty;
        private byte[] _cachedBattleSnapshotPayloadBytes = Array.Empty<byte>();
        private int _cachedBattleSnapshotLogicalBytes;
        private string _cachedBattleSnapshotPayloadHash = string.Empty;
        private CoopBattleSnapshotCompressionKind _cachedBattleSnapshotCompressionKind = CoopBattleSnapshotCompressionKind.None;
        private string _cachedBattleSnapshotV2ComparisonKey = string.Empty;
        private byte[] _cachedBattleSnapshotV2PayloadBytes = Array.Empty<byte>();
        private int _cachedBattleSnapshotV2LogicalBytes;
        private string _cachedBattleSnapshotV2PayloadHash = string.Empty;
        private CoopBattleSnapshotCompressionKind _cachedBattleSnapshotV2CompressionKind = CoopBattleSnapshotCompressionKind.None;
        private CoopBattleSnapshotPayloadEncoding _cachedBattleSnapshotV2PayloadEncoding = CoopBattleSnapshotPayloadEncoding.JsonUtf8;
        private int _nextTransmissionId = 1;
        private bool _persistedHostedLocalPeerMarker;
        private DateTime _lastClientBattleSnapshotBootstrapRequestUtc = DateTime.MinValue;
        private DateTime _lastClientBattleReconnectFinalizeReadyAckUtc = DateTime.MinValue;
        private int _lastClientBattleReconnectFinalizeReadyAckTransmissionId;
        private string _lastClientBattleReconnectFinalizeReadinessSummary = string.Empty;

        internal static bool TryGetClientBattleSnapshotProgress(out ClientBattleSnapshotProgressInfo progress)
        {
            progress = default(ClientBattleSnapshotProgressInfo);

            if (!GameNetwork.IsClient)
                return false;

            Mission mission = Mission.Current;
            if (mission == null)
                return false;

            CoopMissionNetworkBridge bridge = mission.GetMissionBehavior<CoopMissionNetworkBridge>();
            if (bridge == null || bridge._clientBattleSnapshotAssembliesByTransmission.Count <= 0)
                return false;

            bridge.TryRunClientBattleSnapshotRecoveryTick();

            BattleSnapshotClientAssemblyState assemblyState = bridge._clientBattleSnapshotAssembliesByTransmission.Values
                .Where(state => state != null)
                .OrderByDescending(state => state.LastChunkReceivedUtc)
                .ThenByDescending(state => state.LastManifestObservedUtc)
                .FirstOrDefault();
            if (assemblyState == null)
                return false;

            bool isStalled =
                !assemblyState.IsComplete &&
                DateTime.UtcNow - assemblyState.LastUsefulChunkReceivedUtc >= BattleSnapshotRangeAckStallDelay;
            progress = new ClientBattleSnapshotProgressInfo(
                assemblyState.TransmissionId,
                assemblyState.ChunkCount,
                assemblyState.ReceivedChunkCount,
                assemblyState.HighestContiguousChunkIndex,
                isStalled);
            return true;
        }

        protected override void AddRemoveMessageHandlers(GameNetwork.NetworkMessageHandlerRegistererContainer registerer)
        {
            if (GameNetwork.IsServer)
            {
                registerer.RegisterBaseHandler<CoopBattleSelectionClientRequestMessage>(HandleClientSelectionRequest);
                registerer.RegisterBaseHandler<CoopBattleSnapshotChunkRequestMessage>(HandleClientBattleSnapshotChunkRequest);
                registerer.RegisterBaseHandler<CoopBattleSnapshotRangeAckMessage>(HandleClientBattleSnapshotRangeAck);
                registerer.RegisterBaseHandler<CoopBattleSnapshotCompleteAckMessage>(HandleClientBattleSnapshotCompleteAck);
                registerer.RegisterBaseHandler<CoopBattleSnapshotAbortMessage>(HandleClientBattleSnapshotAbort);
                ModLogger.Info("CoopMissionNetworkBridge: registered server selection request handler.");
            }

            if (GameNetwork.IsClient)
            {
                registerer.RegisterBaseHandler<CoopBattlePayloadChunkMessage>(HandleServerPayloadChunk);
                registerer.RegisterBaseHandler<CoopBattleSnapshotManifestMessage>(HandleServerBattleSnapshotManifest);
                registerer.RegisterBaseHandler<CoopBattleSnapshotChunkV2Message>(HandleServerBattleSnapshotChunkV2);
                ModLogger.Info("CoopMissionNetworkBridge: registered client payload chunk handler.");
            }
        }

        protected override void OnUdpNetworkHandlerTick()
        {
            if (!GameNetwork.IsServer || Mission == null)
                return;

            TrySyncBattleSnapshotPayloads();
            TrySyncMaterializedAgentEntryPayloads();
            TrySyncEntryStatusPayloads();
        }

        public override void OnPreMissionTick(float dt)
        {
            base.OnPreMissionTick(dt);
            TryRunClientBattleSnapshotRecoveryTick();
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            TryRunClientBattleSnapshotRecoveryTick();
        }

        private void TryRunClientBattleSnapshotRecoveryTick()
        {
            TryPersistHostedLocalPeerMarker();

            if (GameNetwork.IsClient && Mission != null)
            {
                TryRequestClientBattleSnapshotBootstrapIfNeeded();
                TryResendClientBattleSnapshotChunkRequests();
                BattleMapSpawnHandoffPatch.TryProcessDeferredClientCreateAgentMessages(
                    Mission,
                    "CoopMissionNetworkBridge.TryRunClientBattleSnapshotRecoveryTick");
                BattleMapSpawnHandoffPatch.TryProcessDeferredClientMountedHeroCreateAgents(
                    Mission,
                    "CoopMissionNetworkBridge.TryRunClientBattleSnapshotRecoveryTick");
                TrySendClientBattleReconnectFinalizeReadyAckIfNeeded();
            }
        }

        private void TryResendClientBattleSnapshotChunkRequests()
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive || _clientBattleSnapshotAssembliesByTransmission.Count <= 0)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            foreach (BattleSnapshotClientAssemblyState assemblyState in _clientBattleSnapshotAssembliesByTransmission.Values
                .Where(state => state != null)
                .ToArray())
            {
                if (assemblyState.IsComplete)
                    continue;

                TimeSpan effectiveRetryDelay =
                    assemblyState.ReceivedChunkCount <= 0
                        ? BattleSnapshotInitialChunkRequestRetryDelay
                        : BattleSnapshotRangeAckStallDelay;
                bool receiveStalled = nowUtc - assemblyState.LastUsefulChunkReceivedUtc >= effectiveRetryDelay;
                bool requestCooldownElapsed =
                    assemblyState.LastControlMessageSentUtc == DateTime.MinValue ||
                    nowUtc - assemblyState.LastControlMessageSentUtc >= effectiveRetryDelay;
                if (!receiveStalled || !requestCooldownElapsed)
                    continue;

                if (assemblyState.ReceivedChunkCount <= 0)
                {
                    SendClientBattleSnapshotChunkRequest(assemblyState, CoopBattleSnapshotAssemblyStateKind.Stalled, "stalled-initial-request");
                    ModLogger.Info(
                        "CoopMissionNetworkBridge: resent stalled initial client V2 battle snapshot chunk request. " +
                        "TransmissionId=" + assemblyState.TransmissionId +
                        " HighestContiguous=" + assemblyState.HighestContiguousChunkIndex +
                        " ReceivedChunkCount=" + assemblyState.ReceivedChunkCount +
                        " ChunkCount=" + assemblyState.ChunkCount);
                    continue;
                }

                SendClientBattleSnapshotRangeAck(assemblyState, CoopBattleSnapshotAssemblyStateKind.Stalled, "stalled-progress-ack");
                ModLogger.Info(
                    "CoopMissionNetworkBridge: resent stalled client V2 battle snapshot range ack. " +
                    "TransmissionId=" + assemblyState.TransmissionId +
                    " HighestContiguous=" + assemblyState.HighestContiguousChunkIndex +
                    " ReceivedChunkCount=" + assemblyState.ReceivedChunkCount +
                    " ChunkCount=" + assemblyState.ChunkCount);
            }
        }

        private void TryRequestClientBattleSnapshotBootstrapIfNeeded()
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive || Mission == null)
                return;

            if (_clientObservedBattleSnapshotTransmissionId > 0 ||
                _clientAppliedBattleSnapshotTransmissionId > 0 ||
                _clientBattleSnapshotAssembliesByTransmission.Count > 0)
            {
                return;
            }

            DateTime nowUtc = DateTime.UtcNow;
            if (_lastClientBattleSnapshotBootstrapRequestUtc != DateTime.MinValue &&
                nowUtc - _lastClientBattleSnapshotBootstrapRequestUtc < BattleSnapshotBootstrapRequestRetryDelay)
            {
                return;
            }

            if (!CoopBattleNetworkRequestTransport.TryRequestBattleSnapshotBootstrap("CoopMissionNetworkBridge bootstrap recovery tick"))
                return;

            _lastClientBattleSnapshotBootstrapRequestUtc = nowUtc;
            ModLogger.Info(
                "CoopMissionNetworkBridge: requested battle snapshot bootstrap from client recovery tick. " +
                "ObservedTransmissionId=" + _clientObservedBattleSnapshotTransmissionId +
                " AppliedTransmissionId=" + _clientAppliedBattleSnapshotTransmissionId +
                " PendingAssemblies=" + _clientBattleSnapshotAssembliesByTransmission.Count);
        }

        private void TrySendClientBattleReconnectFinalizeReadyAckIfNeeded()
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive || Mission == null)
                return;

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status = CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (status?.HasAgent == true)
                return;

            if (_clientAppliedBattleSnapshotTransmissionId <= 0)
                return;

            if (!CoopMissionSpawnLogic.IsClientReconnectFinalizeReady(out string readinessSummary))
            {
                _lastClientBattleReconnectFinalizeReadinessSummary = readinessSummary ?? string.Empty;
                return;
            }

            DateTime nowUtc = DateTime.UtcNow;
            bool shouldSend =
                _lastClientBattleReconnectFinalizeReadyAckTransmissionId != _clientAppliedBattleSnapshotTransmissionId ||
                _lastClientBattleReconnectFinalizeReadyAckUtc == DateTime.MinValue ||
                nowUtc - _lastClientBattleReconnectFinalizeReadyAckUtc >= BattleReconnectFinalizeReadyAckRetryDelay;
            if (!shouldSend)
                return;

            bool acknowledged = CoopBattleNetworkRequestTransport.TryAcknowledgeBattleReconnectFinalize(
                _clientAppliedBattleSnapshotTransmissionId,
                "CoopMissionNetworkBridge.TryRunClientBattleSnapshotRecoveryTick reconnect-finalize-ready");
            _lastClientBattleReconnectFinalizeReadinessSummary = readinessSummary ?? string.Empty;
            if (!acknowledged)
                return;

            _lastClientBattleReconnectFinalizeReadyAckTransmissionId = _clientAppliedBattleSnapshotTransmissionId;
            _lastClientBattleReconnectFinalizeReadyAckUtc = nowUtc;
            ModLogger.Info(
                "CoopMissionNetworkBridge: sent client reconnect finalize ready ack. " +
                "TransmissionId=" + _clientAppliedBattleSnapshotTransmissionId +
                " ReadinessSummary=" + (_lastClientBattleReconnectFinalizeReadinessSummary ?? "unknown"));
        }

        private void TryPersistHostedLocalPeerMarker()
        {
            if (_persistedHostedLocalPeerMarker || !GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return;

            NetworkCommunicator myPeer = GameNetwork.MyPeer;
            if (myPeer == null || myPeer.IsServerPeer || string.IsNullOrWhiteSpace(myPeer.UserName))
                return;

            if (HostSelfJoinRedirectState.TryPersistJoinedLocalHostPeer(
                    myPeer.UserName,
                    "CoopMissionNetworkBridge.OnUdpNetworkHandlerTick"))
            {
                _persistedHostedLocalPeerMarker = true;
            }
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

        protected override void HandleNewClientAfterLoadingFinished(NetworkCommunicator networkPeer)
        {
            base.HandleNewClientAfterLoadingFinished(networkPeer);
            TryArmActiveBattleReconnectFinalizeGate(
                networkPeer,
                transmissionId: 0,
                "CoopMissionNetworkBridge.HandleNewClientAfterLoadingFinished");
            TryPrimePreSynchronizedBattleSnapshotBootstrap(
                networkPeer,
                "CoopMissionNetworkBridge.HandleNewClientAfterLoadingFinished");
        }

        protected override void HandlePlayerDisconnect(NetworkCommunicator networkPeer)
        {
            base.HandlePlayerDisconnect(networkPeer);

            if (networkPeer == null)
                return;

            _lastSentStatusPayloadByPeer.Remove(networkPeer.Index);
            _lastSentMaterializedAgentEntryPayloadByPeer.Remove(networkPeer.Index);
            _lastSentBattleSnapshotPayloadByPeer.Remove(networkPeer.Index);
            _lastCompletedBattleSnapshotTransmissionUtcByPeer.Remove(networkPeer.Index);
            _lastBattleSnapshotRetryUtcByPeer.Remove(networkPeer.Index);
            _pendingPayloadsByKey.Remove(BuildPendingTransmissionKey(networkPeer.Index, CoopBattlePayloadKind.AuthoritativeMaterializedAgentEntrySnapshot));
            _pendingPayloadsByKey.Remove(BuildPendingTransmissionKey(networkPeer.Index, CoopBattlePayloadKind.EntryStatusSnapshot));
            _pendingPayloadsByKey.Remove(BuildPendingTransmissionKey(networkPeer.Index, CoopBattlePayloadKind.BattleSnapshot));
            _battleSnapshotTransportStatesByPeer.Remove(networkPeer.Index);
            ClearPeerBattleSnapshotSyncState(networkPeer.Index);
            CoopBattlePeerReconnectState.ObserveDisconnect(
                networkPeer,
                "CoopMissionNetworkBridge.HandlePlayerDisconnect");
            LateJoinPeerBootstrapGatePatch.ClearDeferredPeerBootstrap(
                networkPeer,
                "CoopMissionNetworkBridge.HandlePlayerDisconnect");
            LateJoinPeerStateReplayOwnershipPatch.ClearDeferredPeerState(
                networkPeer,
                "CoopMissionNetworkBridge.HandlePlayerDisconnect");
        }

        public override void OnRemoveBehavior()
        {
            _lastSentStatusPayloadByPeer.Clear();
            _lastSentMaterializedAgentEntryPayloadByPeer.Clear();
            _lastSentBattleSnapshotPayloadByPeer.Clear();
            _lastCompletedBattleSnapshotTransmissionUtcByPeer.Clear();
            _lastBattleSnapshotRetryUtcByPeer.Clear();
            _pendingPayloadsByKey.Clear();
            _clientPayloadAssemblies.Clear();
            _battleSnapshotTransportStatesByPeer.Clear();
            _clientBattleSnapshotAssembliesByTransmission.Clear();
            _expectedBattleSnapshotTransmissionIdByPeer.Clear();
            _acknowledgedBattleSnapshotTransmissionIdByPeer.Clear();
            CoopBattlePeerReconnectState.Reset("CoopMissionNetworkBridge.OnRemoveBehavior");
            LateJoinPeerBootstrapGatePatch.ClearAllDeferredPeerBootstrap(
                "CoopMissionNetworkBridge.OnRemoveBehavior");
            LateJoinPeerStateReplayOwnershipPatch.ClearAllDeferredPeerState(
                "CoopMissionNetworkBridge.OnRemoveBehavior");
            ClearClientBattleSnapshotApplicationState("CoopMissionNetworkBridge.OnRemoveBehavior");
            _lastClientBattleSnapshotBootstrapRequestUtc = DateTime.MinValue;
            _lastClientBattleReconnectFinalizeReadyAckUtc = DateTime.MinValue;
            _lastClientBattleReconnectFinalizeReadyAckTransmissionId = 0;
            _lastClientBattleReconnectFinalizeReadinessSummary = string.Empty;
            base.OnRemoveBehavior();
        }

        private bool HandleClientSelectionRequest(NetworkCommunicator peer, GameNetworkMessage baseMessage)
        {
            if (!(baseMessage is CoopBattleSelectionClientRequestMessage message))
                return false;

            try
            {
                if (message.RequestKind == CoopBattleSelectionRequestKind.BattleSnapshotBootstrapRequest)
                {
                    TryHandleBattleSnapshotBootstrapRequest(peer);
                    return true;
                }

                if (message.RequestKind == CoopBattleSelectionRequestKind.BattleSnapshotReadyAck)
                {
                    bool acknowledged = TryAcknowledgePeerBattleSnapshot(peer, message.SelectionId);
                    if (acknowledged)
                    {
                        TrySendImmediatePeerStatusPayloads(peer);
                        LateJoinPeerBootstrapGatePatch.TryReplayDeferredPeerBootstrap(
                            peer,
                            "CoopMissionNetworkBridge.HandleClientSelectionRequest BattleSnapshotReadyAck");
                        LateJoinPeerStateReplayOwnershipPatch.TryReplayDeferredPeerState(
                            peer,
                            Mission,
                            "CoopMissionNetworkBridge.HandleClientSelectionRequest BattleSnapshotReadyAck");
                    }
                    return true;
                }

                if (message.RequestKind == CoopBattleSelectionRequestKind.BattleReconnectFinalizeReadyAck)
                {
                    bool acknowledged = TryAcknowledgePeerBattleReconnectFinalize(peer, message.SelectionId);
                    if (acknowledged)
                        TrySendEntryStatusToPeer(peer, force: true);
                    return true;
                }

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
                bool shouldForceImmediateStatus =
                    message.RequestKind == CoopBattleSelectionRequestKind.SpawnNow ||
                    message.RequestKind == CoopBattleSelectionRequestKind.ForceRespawnable ||
                    message.RequestKind == CoopBattleSelectionRequestKind.Spectate;
                bool shouldForceStatusAfterRejectedInteractiveRequest =
                    !applied &&
                    (message.RequestKind == CoopBattleSelectionRequestKind.SelectSide ||
                     message.RequestKind == CoopBattleSelectionRequestKind.SelectEntry ||
                     message.RequestKind == CoopBattleSelectionRequestKind.SpawnNow);
                if ((applied && shouldForceImmediateStatus) || shouldForceStatusAfterRejectedInteractiveRequest)
                    TrySendImmediatePeerStatusPayloads(peer);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: client selection request handling failed: " + ex.Message);
            }

            return true;
        }

        private bool TryAcknowledgePeerBattleReconnectFinalize(NetworkCommunicator peer, string selectionId)
        {
            if (!GameNetwork.IsServer || peer == null || peer.IsServerPeer)
                return false;

            if (!int.TryParse(selectionId, out int transmissionId) || transmissionId <= 0)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: ignored reconnect finalize ack with invalid transmission id. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " SelectionId=" + (selectionId ?? string.Empty));
                return false;
            }

            bool acknowledged = CoopBattlePeerReconnectState.TryAcknowledgeActiveBattleReconnectFinalizeGate(
                peer,
                transmissionId,
                "CoopMissionNetworkBridge.HandleClientSelectionRequest BattleReconnectFinalizeReadyAck");
            if (!acknowledged)
                return false;

            CoopMissionSpawnLogic.TryReturnPeerToReconnectSideSelection(
                Mission,
                peer,
                "CoopMissionNetworkBridge.HandleClientSelectionRequest BattleReconnectFinalizeReadyAck");
            return true;
        }

        private void TryHandleBattleSnapshotBootstrapRequest(NetworkCommunicator peer)
        {
            if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive)
                return;

            if (TryPrimePreSynchronizedBattleSnapshotBootstrap(
                    peer,
                    "CoopMissionNetworkBridge.TryHandleBattleSnapshotBootstrapRequest"))
            {
                return;
            }

            BattleSnapshotMessage snapshot = BattleSnapshotRuntimeState.GetCurrent();
            if (!TryGetBattleSnapshotTransmissionPayloadDescriptorV2(
                    snapshot,
                    out byte[] payloadBytes,
                    out int logicalByteCount,
                    out string comparisonKey,
                    out string payloadHash,
                    out CoopBattleSnapshotCompressionKind compressionKind,
                    out CoopBattleSnapshotPayloadEncoding payloadEncoding))
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: ignored client bootstrap request because no current battle snapshot payload is available. " +
                    "Peer=" + (peer.UserName ?? "null"));
                return;
            }

            BattleSnapshotTransportState transportState = GetOrCreateBattleSnapshotTransportState(
                peer,
                payloadBytes,
                logicalByteCount,
                comparisonKey,
                payloadHash,
                compressionKind,
                payloadEncoding);
            if (transportState == null)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: ignored client bootstrap request because transport state could not be created yet. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " IsSynchronized=" + peer.IsSynchronized +
                    " ComparisonKey=" + (comparisonKey ?? string.Empty));
                return;
            }

            SendBattleSnapshotManifest(peer, transportState);
            ModLogger.Info(
                "CoopMissionNetworkBridge: resent battle snapshot manifest after explicit client bootstrap request. " +
                "Peer=" + (peer.UserName ?? "null") +
                " TransmissionId=" + transportState.TransmissionId +
                " ChunkCount=" + transportState.ChunkCount);
        }

        private void TryArmActiveBattleReconnectFinalizeGate(
            NetworkCommunicator peer,
            int transmissionId,
            string source)
        {
            if (!GameNetwork.IsServer || Mission == null || peer == null || peer.IsServerPeer)
                return;

            CoopBattlePhase currentPhase = CoopBattlePhaseRuntimeState.GetPhase();
            if (currentPhase < CoopBattlePhase.BattleActive || currentPhase >= CoopBattlePhase.BattleEnded)
                return;

            MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
            if (missionPeer == null)
                return;

            Agent controlledAgent = missionPeer.ControlledAgent;
            if (controlledAgent != null && controlledAgent.IsActive())
                return;

            string reason =
                "active-battle-late-join-finalize" +
                " Phase=" + currentPhase +
                " IsSynchronized=" + peer.IsSynchronized +
                " JustReconnecting=" + peer.JustReconnecting +
                " HasControlledAgent=" + (controlledAgent != null && controlledAgent.IsActive()) +
                " Scene=" + (Mission.SceneName ?? "null");
            CoopBattlePeerReconnectState.ArmActiveBattleReconnectFinalizeGate(
                peer,
                transmissionId,
                source,
                reason);
        }

        private bool TryPrimePreSynchronizedBattleSnapshotBootstrap(NetworkCommunicator peer, string source)
        {
            if (!ShouldUsePreSynchronizedBattleSnapshotBootstrap(peer, out string eligibilitySummary))
                return false;

            BattleSnapshotMessage snapshot = BattleSnapshotRuntimeState.GetCurrent();
            if (!TryGetBattleSnapshotTransmissionPayloadDescriptorV2(
                    snapshot,
                    out byte[] payloadBytes,
                    out int logicalByteCount,
                    out string comparisonKey,
                    out string payloadHash,
                    out CoopBattleSnapshotCompressionKind compressionKind,
                    out CoopBattleSnapshotPayloadEncoding payloadEncoding))
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: skipped pre-synchronized V2 battle snapshot bootstrap because no current payload is available yet. " +
                    "Peer=" + (peer?.UserName ?? "null") +
                    " Eligibility=" + (eligibilitySummary ?? "unknown") +
                    " Source=" + (source ?? "unknown"));
                return false;
            }

            BattleSnapshotTransportState transportState = GetOrCreateBattleSnapshotTransportState(
                peer,
                payloadBytes,
                logicalByteCount,
                comparisonKey,
                payloadHash,
                compressionKind,
                payloadEncoding,
                allowUnsynchronizedPeer: true);
            if (transportState == null)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: skipped pre-synchronized V2 battle snapshot bootstrap because transport state could not be created. " +
                    "Peer=" + (peer?.UserName ?? "null") +
                    " Eligibility=" + (eligibilitySummary ?? "unknown") +
                    " Source=" + (source ?? "unknown"));
                return false;
            }

            bool manifestSentNow = false;
            if (!transportState.ManifestSent)
            {
                SendBattleSnapshotManifest(peer, transportState);
                manifestSentNow = true;
            }

            int chunksSentNow = 0;
            for (int chunkIndex = 0; chunkIndex < transportState.ChunkCount; chunkIndex++)
            {
                if (transportState.SentChunkFlags[chunkIndex])
                    continue;

                SendBattleSnapshotChunkV2(peer, transportState, chunkIndex);
                chunksSentNow++;
            }

            if (!manifestSentNow && chunksSentNow <= 0)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: pre-synchronized V2 battle snapshot bootstrap was already primed before vanilla existing-object sync. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " TransmissionId=" + transportState.TransmissionId +
                    " ChunkCount=" + transportState.ChunkCount +
                    " Eligibility=" + (eligibilitySummary ?? "unknown") +
                    " Source=" + (source ?? "unknown"));
                return true;
            }

            ModLogger.Info(
                "CoopMissionNetworkBridge: primed pre-synchronized V2 battle snapshot bootstrap before vanilla existing-object sync. " +
                "Peer=" + (peer.UserName ?? "null") +
                " TransmissionId=" + transportState.TransmissionId +
                " ChunkCount=" + transportState.ChunkCount +
                " ManifestSentNow=" + manifestSentNow +
                " ChunksSentNow=" + chunksSentNow +
                " Eligibility=" + (eligibilitySummary ?? "unknown") +
                " Source=" + (source ?? "unknown"));
            return true;
        }

        private bool ShouldUsePreSynchronizedBattleSnapshotBootstrap(NetworkCommunicator peer, out string eligibilitySummary)
        {
            eligibilitySummary = string.Empty;
            if (!UseBattleSnapshotTransportV2)
            {
                eligibilitySummary = "transport-v2-disabled";
                return false;
            }

            if (!GameNetwork.IsServer)
            {
                eligibilitySummary = "not-server";
                return false;
            }

            if (Mission == null)
            {
                eligibilitySummary = "mission-null";
                return false;
            }

            if (!MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(Mission.SceneName))
            {
                eligibilitySummary = "scene-not-battle-runtime:" + (Mission.SceneName ?? "null");
                return false;
            }

            if (!IsBattleSnapshotBootstrapEligiblePeer(peer, allowUnsynchronizedPeer: true))
            {
                eligibilitySummary =
                    "peer-ineligible:" +
                    (peer == null
                        ? "null"
                        : "ServerPeer=" + peer.IsServerPeer +
                          " ConnectionActive=" + peer.IsConnectionActive +
                          " IsSynchronized=" + peer.IsSynchronized);
                return false;
            }

            if (peer.IsSynchronized)
            {
                eligibilitySummary = "already-synchronized";
                return false;
            }

            eligibilitySummary =
                "Peer=" + (peer.UserName ?? "null") +
                " JustReconnecting=" + peer.JustReconnecting +
                " IsSynchronized=" + peer.IsSynchronized +
                " Scene=" + (Mission.SceneName ?? "null");
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

        private bool HandleClientBattleSnapshotChunkRequest(NetworkCommunicator peer, GameNetworkMessage baseMessage)
        {
            if (!(baseMessage is CoopBattleSnapshotChunkRequestMessage message))
                return false;

            try
            {
                AcceptClientBattleSnapshotChunkRequest(peer, message);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: client battle snapshot chunk request handling failed: " + ex.Message);
            }

            return true;
        }

        private bool HandleClientBattleSnapshotRangeAck(NetworkCommunicator peer, GameNetworkMessage baseMessage)
        {
            if (!(baseMessage is CoopBattleSnapshotRangeAckMessage message))
                return false;

            try
            {
                AcceptClientBattleSnapshotRangeAck(peer, message);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: client battle snapshot range ack handling failed: " + ex.Message);
            }

            return true;
        }

        private bool HandleClientBattleSnapshotCompleteAck(NetworkCommunicator peer, GameNetworkMessage baseMessage)
        {
            if (!(baseMessage is CoopBattleSnapshotCompleteAckMessage message))
                return false;

            try
            {
                AcceptClientBattleSnapshotCompleteAck(peer, message);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: client battle snapshot complete ack handling failed: " + ex.Message);
            }

            return true;
        }

        private bool HandleClientBattleSnapshotAbort(NetworkCommunicator peer, GameNetworkMessage baseMessage)
        {
            if (!(baseMessage is CoopBattleSnapshotAbortMessage message))
                return false;

            try
            {
                AcceptClientBattleSnapshotAbort(peer, message);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: client battle snapshot abort handling failed: " + ex.Message);
            }

            return true;
        }

        private void HandleServerBattleSnapshotManifest(GameNetworkMessage baseMessage)
        {
            if (!(baseMessage is CoopBattleSnapshotManifestMessage message))
                return;

            try
            {
                AcceptServerBattleSnapshotManifest(message);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: server battle snapshot manifest handling failed: " + ex.Message);
            }
        }

        private void HandleServerBattleSnapshotChunkV2(GameNetworkMessage baseMessage)
        {
            if (!(baseMessage is CoopBattleSnapshotChunkV2Message message))
                return;

            try
            {
                AcceptServerBattleSnapshotChunkV2(message);
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: server battle snapshot chunk V2 handling failed: " + ex.Message);
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

                if (!IsPeerCurrentBattleSnapshotBootstrapReady(peer, out _))
                    continue;

                TrySendEntryStatusToPeer(peer, force: false);
            }
        }

        private void TrySyncMaterializedAgentEntryPayloads()
        {
            if (GameNetwork.NetworkPeers == null)
                return;

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (!IsEligibleRemotePeer(peer))
                    continue;

                if (!IsPeerCurrentBattleSnapshotBootstrapReady(peer, out _))
                    continue;

                TrySendMaterializedAgentEntrySnapshotToPeer(peer, force: false);
            }
        }

        private void TrySendImmediatePeerStatusPayloads(NetworkCommunicator peer)
        {
            if (!IsEligibleRemotePeer(peer))
                return;

            if (!IsPeerCurrentBattleSnapshotBootstrapReady(peer, out string readinessSummary))
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: withheld bootstrap-dependent peer payloads until battle snapshot is acknowledged. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " SnapshotReadiness=" + (readinessSummary ?? "unknown"));
                return;
            }

            TrySendMaterializedAgentEntrySnapshotToPeer(peer, force: true);
            TrySendEntryStatusToPeer(peer, force: true);
        }

        private void TrySendMaterializedAgentEntrySnapshotToPeer(NetworkCommunicator peer, bool force)
        {
            if (!IsEligibleRemotePeer(peer) || Mission == null)
                return;

            CoopBattleEntryStatusBridgeFile.AuthoritativeMaterializedAgentEntrySnapshot snapshot =
                CoopMissionSpawnLogic.BuildAuthoritativeMaterializedAgentEntrySnapshot(Mission, "CoopMissionNetworkBridge");
            if (snapshot == null)
                return;

            string comparisonJson = SerializeComparableMaterializedAgentEntryPayload(snapshot);
            if (string.IsNullOrWhiteSpace(comparisonJson))
                return;

            string transmissionKey = BuildPendingTransmissionKey(peer.Index, CoopBattlePayloadKind.AuthoritativeMaterializedAgentEntrySnapshot);
            bool hasPending = _pendingPayloadsByKey.TryGetValue(transmissionKey, out PendingPayloadTransmission pendingTransmission) &&
                pendingTransmission != null;

            if (hasPending &&
                TryFinalizePendingPayloadTransmission(
                    peer,
                    transmissionKey,
                    _lastSentMaterializedAgentEntryPayloadByPeer,
                    ref pendingTransmission,
                    out bool pendingStillInFlight))
            {
                hasPending = pendingTransmission != null;
                if (pendingStillInFlight)
                    return;
            }

            if (!force &&
                !hasPending &&
                _lastSentMaterializedAgentEntryPayloadByPeer.TryGetValue(peer.Index, out string previousPayload) &&
                string.Equals(previousPayload, comparisonJson, StringComparison.Ordinal))
            {
                return;
            }

            if (!hasPending ||
                !string.Equals(pendingTransmission.ComparisonKey, comparisonJson, StringComparison.Ordinal))
            {
                pendingTransmission = CreateMaterializedAgentEntryPendingTransmission(snapshot, comparisonJson);
                if (pendingTransmission == null)
                    return;

                _pendingPayloadsByKey[transmissionKey] = pendingTransmission;
                ModLogger.Info(
                    "CoopMissionNetworkBridge: queued payload transmission. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " Kind=" + CoopBattlePayloadKind.AuthoritativeMaterializedAgentEntrySnapshot +
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
            _lastSentMaterializedAgentEntryPayloadByPeer[peer.Index] = comparisonJson;
            ModLogger.Info(
                "CoopMissionNetworkBridge: completed payload transmission. " +
                "Peer=" + (peer.UserName ?? "null") +
                " Kind=" + CoopBattlePayloadKind.AuthoritativeMaterializedAgentEntrySnapshot +
                " TransmissionId=" + pendingTransmission.TransmissionId +
                " Bytes=" + pendingTransmission.TotalBytes +
                (pendingTransmission.LogicalBytes != pendingTransmission.TotalBytes
                    ? " LogicalBytes=" + pendingTransmission.LogicalBytes
                    : string.Empty) +
                " Chunks=" + pendingTransmission.ChunkCount +
                " EntryCount=" + snapshot.EntryCount);
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

            string transmissionKey = BuildPendingTransmissionKey(peer.Index, CoopBattlePayloadKind.EntryStatusSnapshot);
            bool hasPending = _pendingPayloadsByKey.TryGetValue(transmissionKey, out PendingPayloadTransmission pendingTransmission) &&
                pendingTransmission != null;

            if (hasPending &&
                TryFinalizePendingPayloadTransmission(
                    peer,
                    transmissionKey,
                    _lastSentStatusPayloadByPeer,
                    ref pendingTransmission,
                    out bool pendingStillInFlight))
            {
                hasPending = pendingTransmission != null;
                if (pendingStillInFlight)
                    return;
            }

            if (!force &&
                !hasPending &&
                _lastSentStatusPayloadByPeer.TryGetValue(peer.Index, out string previousPayload) &&
                string.Equals(previousPayload, comparisonJson, StringComparison.Ordinal))
            {
                return;
            }

            if (!hasPending ||
                !string.Equals(pendingTransmission.ComparisonKey, comparisonJson, StringComparison.Ordinal))
            {
                pendingTransmission = CreateEntryStatusPendingTransmission(snapshot, comparisonJson);
                if (pendingTransmission == null)
                    return;

                _pendingPayloadsByKey[transmissionKey] = pendingTransmission;
                ModLogger.Info(
                    "CoopMissionNetworkBridge: queued payload transmission. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " Kind=" + CoopBattlePayloadKind.EntryStatusSnapshot +
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
            _lastSentStatusPayloadByPeer[peer.Index] = comparisonJson;
            ModLogger.Info(
                "CoopMissionNetworkBridge: completed payload transmission. " +
                "Peer=" + (peer.UserName ?? "null") +
                " Kind=" + CoopBattlePayloadKind.EntryStatusSnapshot +
                " TransmissionId=" + pendingTransmission.TransmissionId +
                " Bytes=" + pendingTransmission.TotalBytes +
                (pendingTransmission.LogicalBytes != pendingTransmission.TotalBytes
                    ? " LogicalBytes=" + pendingTransmission.LogicalBytes
                    : string.Empty) +
                " Chunks=" + pendingTransmission.ChunkCount);
        }

        private void TrySyncBattleSnapshotPayloads()
        {
            if (GameNetwork.NetworkPeers == null)
                return;

            if (UseBattleSnapshotTransportV2)
            {
                TrySyncBattleSnapshotPayloadsV2();
                return;
            }

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (!IsBattleSnapshotBootstrapEligiblePeer(peer))
                    continue;

                if (TryRetryUnacknowledgedBattleSnapshot(peer))
                    continue;

                TrySendBattleSnapshotToPeer(peer, force: false);
            }
        }

        private void TrySyncBattleSnapshotPayloadsV2()
        {
            BattleSnapshotMessage snapshot = BattleSnapshotRuntimeState.GetCurrent();
            if (snapshot?.Sides == null || snapshot.Sides.Count <= 0)
                return;

            if (!TryGetBattleSnapshotTransmissionPayloadDescriptorV2(
                    snapshot,
                    out byte[] payloadBytes,
                    out int logicalByteCount,
                    out string comparisonKey,
                    out string payloadHash,
                    out CoopBattleSnapshotCompressionKind compressionKind,
                    out CoopBattleSnapshotPayloadEncoding payloadEncoding))
            {
                return;
            }

            List<BattleSnapshotTransportState> activeStates = new List<BattleSnapshotTransportState>();
            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (!IsBattleSnapshotBootstrapEligiblePeer(peer))
                    continue;

                BattleSnapshotTransportState transportState = GetOrCreateBattleSnapshotTransportState(
                    peer,
                    payloadBytes,
                    logicalByteCount,
                    comparisonKey,
                    payloadHash,
                    compressionKind,
                    payloadEncoding);
                if (transportState != null && !transportState.IsCompleted)
                    activeStates.Add(transportState);
            }

            int concurrentHeavyPeers = 0;
            foreach (BattleSnapshotTransportState transportState in activeStates
                .OrderBy(state => state.ManifestSent ? 1 : 0)
                .ThenBy(state => state.HasPendingChunkRequests ? 0 : 1)
                .ThenBy(state => state.LastProgressUtc))
            {
                if (!_battleSnapshotTransportStatesByPeer.TryGetValue(transportState.PeerIndex, out BattleSnapshotTransportState currentState))
                    continue;

                NetworkCommunicator peer = GameNetwork.NetworkPeers.FirstOrDefault(candidate => candidate != null && candidate.Index == currentState.PeerIndex);
                if (!IsBattleSnapshotBootstrapEligiblePeer(peer))
                    continue;

                if (!currentState.IsCompleted)
                {
                    concurrentHeavyPeers++;
                    if (concurrentHeavyPeers > BattleSnapshotMaxConcurrentHeavyPeers)
                        continue;
                }

                TryAdvanceBattleSnapshotTransportState(peer, currentState);
            }
        }

        private void TrySendBattleSnapshotToPeer(NetworkCommunicator peer, bool force)
        {
            if (!IsBattleSnapshotBootstrapEligiblePeer(peer))
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

        private BattleSnapshotTransportState GetOrCreateBattleSnapshotTransportState(
            NetworkCommunicator peer,
            byte[] payloadBytes,
            int logicalByteCount,
            string comparisonKey,
            string payloadHash,
            CoopBattleSnapshotCompressionKind compressionKind,
            CoopBattleSnapshotPayloadEncoding payloadEncoding,
            bool allowUnsynchronizedPeer = false)
        {
            if (!IsBattleSnapshotBootstrapEligiblePeer(peer, allowUnsynchronizedPeer) ||
                payloadBytes == null ||
                payloadBytes.Length <= 0 ||
                string.IsNullOrWhiteSpace(comparisonKey))
            {
                return null;
            }

            if (_battleSnapshotTransportStatesByPeer.TryGetValue(peer.Index, out BattleSnapshotTransportState existingState) &&
                existingState != null &&
                string.Equals(existingState.ComparisonKey, comparisonKey, StringComparison.Ordinal))
            {
                return existingState;
            }

            BattleSnapshotTransportState newState = BattleSnapshotTransportState.Create(
                peer.Index,
                payloadBytes,
                logicalByteCount,
                comparisonKey,
                payloadHash,
                compressionKind,
                payloadEncoding,
                NextTransmissionId(),
                BattleSnapshotInitialWindowChunks,
                BattleSnapshotMaxInflightChunksPerPeer);
            if (newState == null)
                return null;

            _battleSnapshotTransportStatesByPeer[peer.Index] = newState;
            RegisterExpectedBattleSnapshotTransmission(peer.Index, newState.TransmissionId);
            _acknowledgedBattleSnapshotTransmissionIdByPeer.Remove(peer.Index);
            _lastCompletedBattleSnapshotTransmissionUtcByPeer.Remove(peer.Index);
            _lastBattleSnapshotRetryUtcByPeer.Remove(peer.Index);
            _lastSentBattleSnapshotPayloadByPeer[peer.Index] = comparisonKey;
            ModLogger.Info(
                "CoopMissionNetworkBridge: initialized V2 battle snapshot transport state. " +
                "Peer=" + (peer.UserName ?? "null") +
                " TransmissionId=" + newState.TransmissionId +
                " LogicalBytes=" + newState.LogicalBytes +
                " WireBytes=" + newState.TotalBytes +
                " Encoding=" + newState.PayloadEncoding +
                " ChunkCount=" + newState.ChunkCount +
                " ChunkBytes=" + CoopBattleSnapshotChunkV2Message.MaxChunkBytes);
            if (CoopBattlePeerReconnectState.TryGetActiveBattleReconnectFinalizeGateState(peer, out _))
            {
                TryArmActiveBattleReconnectFinalizeGate(
                    peer,
                    newState.TransmissionId,
                    "CoopMissionNetworkBridge.GetOrCreateBattleSnapshotTransportState");
            }
            return newState;
        }

        private void TryAdvanceBattleSnapshotTransportState(NetworkCommunicator peer, BattleSnapshotTransportState transportState)
        {
            if (!IsBattleSnapshotBootstrapEligiblePeer(peer) || transportState == null || transportState.IsCompleted)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            bool shouldSendManifest = !transportState.HasObservedClientRequest &&
                                      (!transportState.ManifestSent ||
                                       nowUtc - transportState.LastManifestSentUtc >= BattleSnapshotManifestRetryDelay);
            if (shouldSendManifest)
            {
                SendBattleSnapshotManifest(peer, transportState);
                if (transportState.TryPrimeInitialActiveWindow(nowUtc))
                {
                    ModLogger.Info(
                        "CoopMissionNetworkBridge: primed initial V2 battle snapshot active window after manifest. " +
                        "Peer=" + (peer.UserName ?? "null") +
                        " TransmissionId=" + transportState.TransmissionId +
                        " ActiveWindow=" + transportState.ActiveWindowStartChunkIndex + "-" + transportState.ActiveWindowEndChunkIndex +
                        " ChunkCount=" + transportState.ChunkCount);
                }
            }

            if (!transportState.HasActiveWindow)
                return;

            if (transportState.IsActiveWindowSatisfiedByClient &&
                transportState.TryAdvanceToNextWindow())
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: advanced V2 battle snapshot active window. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " TransmissionId=" + transportState.TransmissionId +
                    " ActiveWindow=" + transportState.ActiveWindowStartChunkIndex + "-" + transportState.ActiveWindowEndChunkIndex +
                    " HighestContiguous=" + transportState.HighestClientContiguousChunkIndex);
            }

            if (transportState.ShouldResendActiveWindow(nowUtc, BattleSnapshotRangeAckStallDelay))
            {
                transportState.RewindActiveWindowForResend(nowUtc);
                ModLogger.Info(
                    "CoopMissionNetworkBridge: rewound stalled V2 battle snapshot active window. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " TransmissionId=" + transportState.TransmissionId +
                    " ActiveWindow=" + transportState.ActiveWindowStartChunkIndex + "-" + transportState.ActiveWindowEndChunkIndex +
                    " HighestContiguous=" + transportState.HighestClientContiguousChunkIndex);
            }

            int chunksSentThisTick = 0;
            while (chunksSentThisTick < MaxBattleSnapshotChunksPerPayloadPerTick &&
                   transportState.CanSendActiveWindowChunks)
            {
                if (transportState.TryGetNextActiveWindowChunkToSend(out int nextChunkIndex))
                {
                    SendBattleSnapshotChunkV2(peer, transportState, nextChunkIndex);
                    chunksSentThisTick++;
                    continue;
                }

                break;
            }
        }

        private static void SendBattleSnapshotManifest(NetworkCommunicator peer, BattleSnapshotTransportState transportState)
        {
            if (peer == null || transportState == null)
                return;

            GameNetwork.BeginModuleEventAsServer(peer);
            GameNetwork.WriteMessage(new CoopBattleSnapshotManifestMessage(
                transportState.TransmissionId,
                BattleSnapshotTransportSchemaVersion,
                transportState.PayloadEncoding,
                transportState.CompressionKind,
                transportState.LogicalBytes,
                transportState.TotalBytes,
                CoopBattleSnapshotChunkV2Message.MaxChunkBytes,
                transportState.ChunkCount,
                transportState.ComparisonKey,
                transportState.PayloadHash));
            GameNetwork.EndModuleEventAsServer();
            transportState.MarkManifestSent(DateTime.UtcNow);
            ModLogger.Info(
                "CoopMissionNetworkBridge: sent V2 battle snapshot manifest. " +
                "Peer=" + (peer.UserName ?? "null") +
                " TransmissionId=" + transportState.TransmissionId +
                " Encoding=" + transportState.PayloadEncoding +
                " ChunkCount=" + transportState.ChunkCount +
                " WireBytes=" + transportState.TotalBytes);
        }

        private static void SendBattleSnapshotChunkV2(NetworkCommunicator peer, BattleSnapshotTransportState transportState, int chunkIndex)
        {
            if (peer == null || transportState == null || chunkIndex < 0 || chunkIndex >= transportState.ChunkCount)
                return;

            byte[] chunkBytes = transportState.Chunks[chunkIndex] ?? Array.Empty<byte>();
            GameNetwork.BeginModuleEventAsServer(peer);
            GameNetwork.WriteMessage(new CoopBattleSnapshotChunkV2Message(
                transportState.TransmissionId,
                chunkIndex,
                transportState.ChunkCount,
                chunkBytes));
            GameNetwork.EndModuleEventAsServer();
            transportState.MarkChunkSent(chunkIndex, DateTime.UtcNow);
            if (chunkIndex == 0)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: sent first V2 battle snapshot chunk. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " TransmissionId=" + transportState.TransmissionId +
                    " ChunkCount=" + transportState.ChunkCount +
                    " Bytes=" + chunkBytes.Length);
            }
        }

        private void AcceptClientBattleSnapshotChunkRequest(NetworkCommunicator peer, CoopBattleSnapshotChunkRequestMessage message)
        {
            if (peer == null || message == null)
                return;

            if (!_battleSnapshotTransportStatesByPeer.TryGetValue(peer.Index, out BattleSnapshotTransportState transportState) ||
                transportState == null ||
                transportState.TransmissionId != message.TransmissionId)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: ignored V2 battle snapshot chunk request with unknown transmission. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " TransmissionId=" + message.TransmissionId +
                    " Range=" + message.StartChunkIndex + "-" + message.EndChunkIndex);
                return;
            }

            transportState.ObserveClientChunkRequest(
                message.StartChunkIndex,
                message.EndChunkIndex,
                message.HighestContiguousChunkIndex,
                message.ReceivedChunkCount,
                DateTime.UtcNow);
            ModLogger.Info(
                "CoopMissionNetworkBridge: accepted V2 battle snapshot chunk request. " +
                "Peer=" + (peer.UserName ?? "null") +
                " TransmissionId=" + message.TransmissionId +
                " Range=" + message.StartChunkIndex + "-" + message.EndChunkIndex +
                " HighestContiguous=" + message.HighestContiguousChunkIndex +
                " ReceivedChunkCount=" + message.ReceivedChunkCount +
                " ActiveWindow=" + transportState.ActiveWindowStartChunkIndex + "-" + transportState.ActiveWindowEndChunkIndex);
        }

        private void AcceptClientBattleSnapshotRangeAck(NetworkCommunicator peer, CoopBattleSnapshotRangeAckMessage message)
        {
            if (peer == null || message == null)
                return;

            if (!_battleSnapshotTransportStatesByPeer.TryGetValue(peer.Index, out BattleSnapshotTransportState transportState) ||
                transportState == null ||
                transportState.TransmissionId != message.TransmissionId)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: ignored V2 battle snapshot range ack with unknown transmission. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " TransmissionId=" + message.TransmissionId +
                    " HighestContiguous=" + message.HighestContiguousChunkIndex +
                    " ReceivedChunkCount=" + message.ReceivedChunkCount);
                return;
            }

            transportState.ObserveClientProgressAck(
                message.HighestContiguousChunkIndex,
                message.ReceivedChunkCount,
                DateTime.UtcNow);
            ModLogger.Info(
                "CoopMissionNetworkBridge: accepted V2 battle snapshot range ack. " +
                "Peer=" + (peer.UserName ?? "null") +
                " TransmissionId=" + message.TransmissionId +
                " HighestContiguous=" + message.HighestContiguousChunkIndex +
                " ReceivedChunkCount=" + message.ReceivedChunkCount +
                " ActiveWindow=" + transportState.ActiveWindowStartChunkIndex + "-" + transportState.ActiveWindowEndChunkIndex +
                " State=" + message.AssemblyState);
        }

        private void AcceptClientBattleSnapshotCompleteAck(NetworkCommunicator peer, CoopBattleSnapshotCompleteAckMessage message)
        {
            if (peer == null || message == null)
                return;

            if (!_battleSnapshotTransportStatesByPeer.TryGetValue(peer.Index, out BattleSnapshotTransportState transportState) ||
                transportState == null ||
                transportState.TransmissionId != message.TransmissionId)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: ignored V2 battle snapshot complete ack with unknown transmission. " +
                    "Peer=" + (peer.UserName ?? "null") +
                    " TransmissionId=" + message.TransmissionId);
                return;
            }

            bool payloadHashMatched = string.IsNullOrWhiteSpace(message.PayloadHash) ||
                                      string.Equals(message.PayloadHash, transportState.PayloadHash, StringComparison.Ordinal);
            bool serverCatalogPrepared = BattleCatalogParityState.TryPrepareCurrentBattleCatalog(
                message.TransmissionId,
                "CoopMissionNetworkBridge.AcceptClientBattleSnapshotCompleteAck",
                out string expectedCatalogHash,
                out string expectedCatalogSummary);
            bool catalogHashMatched =
                serverCatalogPrepared &&
                !string.IsNullOrWhiteSpace(message.CatalogHash) &&
                string.Equals(message.CatalogHash, expectedCatalogHash, StringComparison.Ordinal);
            bool bootstrapReady =
                message.AppliedSuccessfully &&
                payloadHashMatched &&
                serverCatalogPrepared &&
                catalogHashMatched;

            transportState.MarkCompleted(bootstrapReady, DateTime.UtcNow);
            if (bootstrapReady)
            {
                _acknowledgedBattleSnapshotTransmissionIdByPeer[peer.Index] = message.TransmissionId;
                _lastCompletedBattleSnapshotTransmissionUtcByPeer.Remove(peer.Index);
                _lastBattleSnapshotRetryUtcByPeer.Remove(peer.Index);
                TryMarkPeerFinishedLoadingFromListedBattleSnapshotAck(
                    peer,
                    message.TransmissionId,
                    "CoopMissionNetworkBridge.AcceptClientBattleSnapshotCompleteAck");
                TrySendImmediatePeerStatusPayloads(peer);
                LateJoinPeerBootstrapGatePatch.TryReplayDeferredPeerBootstrap(
                    peer,
                    "CoopMissionNetworkBridge.AcceptClientBattleSnapshotCompleteAck");
            }

            ModLogger.Info(
                "CoopMissionNetworkBridge: processed V2 battle snapshot completion ack. " +
                "Peer=" + (peer.UserName ?? "null") +
                " TransmissionId=" + message.TransmissionId +
                " AppliedSuccessfully=" + message.AppliedSuccessfully +
                " PayloadHashMatched=" + payloadHashMatched +
                " ServerCatalogPrepared=" + serverCatalogPrepared +
                " CatalogHashMatched=" + catalogHashMatched +
                " ClientCatalogHash=" + (string.IsNullOrWhiteSpace(message.CatalogHash) ? "null" : message.CatalogHash) +
                " ExpectedCatalogHash=" + (string.IsNullOrWhiteSpace(expectedCatalogHash) ? "null" : expectedCatalogHash) +
                " BootstrapReady=" + bootstrapReady +
                " ExpectedCatalogSummary={" + (expectedCatalogSummary ?? "unknown") + "}");
        }

        private void TryMarkPeerFinishedLoadingFromListedBattleSnapshotAck(
            NetworkCommunicator peer,
            int transmissionId,
            string source)
        {
            if (!GameNetwork.IsServer || Mission == null || peer == null || peer.IsServerPeer || !peer.IsConnectionActive)
                return;

            if (peer.IsSynchronized)
                return;

            if (!SceneRuntimeClassifier.IsSceneAwareBattleRuntimeScene(Mission.SceneName ?? string.Empty))
                return;

            ListedShellSessionTransportRuntime.TryPromotePendingBattleTransportTokenToListedSession(
                Mission,
                source + " promote-pending-token");
            bool hasListedToken = ListedShellMissionSessionState.TryResolveTransportToken(Mission, out int listedToken) &&
                                  listedToken > 0;
            bool hasPendingToken = PendingBattleMissionStartupState.TryResolveAuthoritativeTransportToken(Mission, out int pendingToken) &&
                                   pendingToken > 0;

            CoopSessionTransportPrimitives.MarkPeerFinishedLoading(
                peer,
                source + " force-finished-loading");

            ModLogger.Info(
                "CoopMissionNetworkBridge: forced peer finished-loading from listed battle snapshot completion ack. " +
                "Peer=" + (peer.UserName ?? "null") +
                " TransmissionId=" + transmissionId +
                " MissionScene=" + (Mission.SceneName ?? "null") +
                " ListedToken=" + (hasListedToken ? listedToken.ToString() : "0") +
                " PendingToken=" + (hasPendingToken ? pendingToken.ToString() : "0") +
                " Source=" + (source ?? "unknown") + ".");
        }

        private void AcceptClientBattleSnapshotAbort(NetworkCommunicator peer, CoopBattleSnapshotAbortMessage message)
        {
            if (peer == null || message == null)
                return;

            if (!_battleSnapshotTransportStatesByPeer.TryGetValue(peer.Index, out BattleSnapshotTransportState transportState) ||
                transportState == null ||
                transportState.TransmissionId != message.TransmissionId)
            {
                return;
            }

            transportState.ResetForRestart(DateTime.UtcNow);
            ModLogger.Info(
                "CoopMissionNetworkBridge: client aborted V2 battle snapshot transport. " +
                "Peer=" + (peer.UserName ?? "null") +
                " TransmissionId=" + message.TransmissionId +
                " Reason=" + (message.Reason ?? string.Empty));
        }

        private void AcceptServerBattleSnapshotManifest(CoopBattleSnapshotManifestMessage message)
        {
            if (message == null || message.TransmissionId <= 0)
                return;

            string normalizedPayloadHash = message.PayloadHash ?? string.Empty;
            bool manifestMatchesAppliedSnapshot =
                _clientAppliedBattleSnapshotTransmissionId == message.TransmissionId &&
                message.TransmissionId > 0 &&
                string.Equals(_clientAppliedBattleSnapshotPayloadHash ?? string.Empty, normalizedPayloadHash, StringComparison.Ordinal);
            if (manifestMatchesAppliedSnapshot)
            {
                _clientBattleSnapshotAssembliesByTransmission.Remove(message.TransmissionId);
                bool catalogPrepared = BattleCatalogParityState.TryGetClientPreparedCatalogState(
                    message.TransmissionId,
                    out string preparedCatalogHash,
                    out string preparedCatalogSummary);
                SendClientBattleSnapshotCompleteAck(
                    message.TransmissionId,
                    normalizedPayloadHash,
                    appliedSuccessfully: catalogPrepared,
                    catalogHash: preparedCatalogHash);
                ModLogger.Info(
                    "CoopMissionNetworkBridge: ignored repeated V2 battle snapshot manifest for already applied client snapshot. " +
                    "TransmissionId=" + message.TransmissionId +
                    " CatalogPrepared=" + catalogPrepared +
                    " CatalogHash=" + (string.IsNullOrWhiteSpace(preparedCatalogHash) ? "null" : preparedCatalogHash) +
                    " CatalogSummary={" + (preparedCatalogSummary ?? "unknown") + "}");
                return;
            }

            ObserveClientBattleSnapshotManifest(message.TransmissionId, normalizedPayloadHash);

            foreach (int staleTransmissionId in _clientBattleSnapshotAssembliesByTransmission.Keys
                .Where(existingTransmissionId => existingTransmissionId != message.TransmissionId)
                .ToArray())
            {
                _clientBattleSnapshotAssembliesByTransmission.Remove(staleTransmissionId);
            }

            if (_clientBattleSnapshotAssembliesByTransmission.TryGetValue(message.TransmissionId, out BattleSnapshotClientAssemblyState existingState) &&
                existingState != null &&
                existingState.ChunkCount == message.ChunkCount &&
                string.Equals(existingState.PayloadHash, normalizedPayloadHash, StringComparison.Ordinal))
            {
                existingState.MarkManifestObserved(DateTime.UtcNow);
                if (!existingState.IsComplete)
                    SendClientBattleSnapshotChunkRequest(existingState, CoopBattleSnapshotAssemblyStateKind.Receiving, "manifest-repeat");
                return;
            }

            BattleSnapshotClientAssemblyState assemblyState = new BattleSnapshotClientAssemblyState(
                message.TransmissionId,
                message.ChunkCount,
                message.LogicalBytes,
                message.WireBytes,
                message.ComparisonKey,
                normalizedPayloadHash,
                message.PayloadEncoding,
                message.CompressionKind);
            _clientBattleSnapshotAssembliesByTransmission[message.TransmissionId] = assemblyState;
            ModLogger.Info(
                "CoopMissionNetworkBridge: received V2 battle snapshot manifest. " +
                "TransmissionId=" + message.TransmissionId +
                " ChunkCount=" + message.ChunkCount +
                " WireBytes=" + message.WireBytes +
                " LogicalBytes=" + message.LogicalBytes);
            SendClientBattleSnapshotChunkRequest(assemblyState, CoopBattleSnapshotAssemblyStateKind.Receiving, "manifest-initial");
        }

        private void AcceptServerBattleSnapshotChunkV2(CoopBattleSnapshotChunkV2Message message)
        {
            if (message == null || message.TransmissionId <= 0 || message.ChunkCount <= 0)
                return;

            if (!_clientBattleSnapshotAssembliesByTransmission.TryGetValue(message.TransmissionId, out BattleSnapshotClientAssemblyState assemblyState) ||
                assemblyState == null)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: received V2 battle snapshot chunk before manifest. " +
                    "TransmissionId=" + message.TransmissionId +
                    " ChunkIndex=" + message.ChunkIndex +
                    " ChunkCount=" + message.ChunkCount);
                return;
            }

            assemblyState.AcceptChunk(message.ChunkIndex, message.PayloadBytes ?? Array.Empty<byte>(), DateTime.UtcNow);
            if (message.ChunkIndex == 0)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: received first V2 battle snapshot chunk. " +
                    "TransmissionId=" + message.TransmissionId +
                    " ChunkCount=" + message.ChunkCount +
                    " Bytes=" + (message.PayloadBytes?.Length ?? 0));
            }

            if (!assemblyState.IsComplete &&
                assemblyState.TryGetCompletedWindowEndChunkIndex(BattleSnapshotInitialWindowChunks, out int completedWindowEndChunkIndex))
            {
                SendClientBattleSnapshotRangeAck(assemblyState, CoopBattleSnapshotAssemblyStateKind.Receiving, "window-complete");
                assemblyState.MarkWindowCompletionAcknowledged(completedWindowEndChunkIndex, DateTime.UtcNow);
            }

            if (!assemblyState.IsComplete)
                return;

            byte[] payloadBytes = assemblyState.Combine();
            if (!TryDecodeBattleSnapshotPayload(assemblyState, payloadBytes, out BattleSnapshotMessage snapshot))
            {
                SendClientBattleSnapshotAbort(assemblyState.TransmissionId, "decode-failed");
                return;
            }
            if (snapshot == null)
            {
                SendClientBattleSnapshotAbort(assemblyState.TransmissionId, "deserialize-failed");
                return;
            }

            BattleSnapshotRuntimeState.SetCurrent(snapshot, "CoopMissionNetworkBridge.V2");
            bool catalogPrepared = BattleCatalogParityState.TryPrepareCurrentBattleCatalog(
                assemblyState.TransmissionId,
                "CoopMissionNetworkBridge.V2 applied",
                out string catalogHash,
                out string catalogSummary);
            if (catalogPrepared)
            {
                MarkClientBattleSnapshotApplied(assemblyState.TransmissionId, assemblyState.PayloadHash);
                _lastClientBattleSnapshotBootstrapRequestUtc = DateTime.MinValue;
                BattleMapSpawnHandoffPatch.TryProcessDeferredClientCreateAgentMessages(
                    Mission,
                    "CoopMissionNetworkBridge.V2 applied");
                BattleMapSpawnHandoffPatch.TryProcessDeferredClientMountedHeroCreateAgents(
                    Mission,
                    "CoopMissionNetworkBridge.V2 applied");
            }

            _clientBattleSnapshotAssembliesByTransmission.Remove(assemblyState.TransmissionId);
            SendClientBattleSnapshotCompleteAck(
                assemblyState.TransmissionId,
                assemblyState.PayloadHash,
                appliedSuccessfully: catalogPrepared,
                catalogHash: catalogHash);
            if (!catalogPrepared)
            {
                ClearClientBattleSnapshotApplicationState("CoopMissionNetworkBridge.V2 catalog-prepare-failed");
            }
            ModLogger.Info(
                "CoopMissionNetworkBridge: applied V2 battle snapshot payload on client. " +
                "TransmissionId=" + assemblyState.TransmissionId +
                " BattleId=" + (snapshot.BattleId ?? string.Empty) +
                " Sides=" + (snapshot.Sides?.Count ?? 0) +
                " CatalogPrepared=" + catalogPrepared +
                " CatalogHash=" + (string.IsNullOrWhiteSpace(catalogHash) ? "null" : catalogHash) +
                " CatalogSummary={" + (catalogSummary ?? "unknown") + "}");
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

            if (hasPending &&
                TryFinalizePendingPayloadTransmission(
                    peer,
                    transmissionKey,
                    lastSentPayloadByPeer,
                    ref pendingTransmission,
                    out bool pendingStillInFlight))
            {
                hasPending = pendingTransmission != null;
                if (pendingStillInFlight)
                    return;
            }

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

                if (payloadKind == CoopBattlePayloadKind.BattleSnapshot)
                    RegisterExpectedBattleSnapshotTransmission(peer.Index, pendingTransmission.TransmissionId);

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
            if (payloadKind == CoopBattlePayloadKind.BattleSnapshot)
                MarkBattleSnapshotTransmissionCompleted(peer.Index);
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
            if (pendingTransmission == null)
                return MaxStatusChunksPerPayloadPerTick;

            return pendingTransmission.PayloadKind == CoopBattlePayloadKind.BattleSnapshot
                ? MaxBattleSnapshotChunksPerPayloadPerTick
                : MaxStatusChunksPerPayloadPerTick;
        }

        private bool TryFinalizePendingPayloadTransmission(
            NetworkCommunicator peer,
            string transmissionKey,
            Dictionary<int, string> lastSentPayloadByPeer,
            ref PendingPayloadTransmission pendingTransmission,
            out bool pendingStillInFlight)
        {
            pendingStillInFlight = false;
            if (peer == null || string.IsNullOrWhiteSpace(transmissionKey) || pendingTransmission == null)
                return false;

            if (!pendingTransmission.IsCompleted)
            {
                if (!TryFlushPendingPayload(peer, pendingTransmission) || !pendingTransmission.IsCompleted)
                {
                    pendingStillInFlight = true;
                    return true;
                }
            }

            _pendingPayloadsByKey.Remove(transmissionKey);
            if (lastSentPayloadByPeer != null)
                lastSentPayloadByPeer[peer.Index] = pendingTransmission.ComparisonKey;
            if (pendingTransmission.PayloadKind == CoopBattlePayloadKind.BattleSnapshot)
                MarkBattleSnapshotTransmissionCompleted(peer.Index);
            ModLogger.Info(
                "CoopMissionNetworkBridge: completed payload transmission. " +
                "Peer=" + (peer.UserName ?? "null") +
                " Kind=" + pendingTransmission.PayloadKind +
                " TransmissionId=" + pendingTransmission.TransmissionId +
                " Bytes=" + pendingTransmission.TotalBytes +
                (pendingTransmission.LogicalBytes != pendingTransmission.TotalBytes
                    ? " LogicalBytes=" + pendingTransmission.LogicalBytes
                    : string.Empty) +
                " Chunks=" + pendingTransmission.ChunkCount);
            pendingTransmission = null;
            return true;
        }

        private bool TryRetryUnacknowledgedBattleSnapshot(NetworkCommunicator peer)
        {
            if (!IsBattleSnapshotBootstrapEligiblePeer(peer))
                return false;

            string transmissionKey = BuildPendingTransmissionKey(peer.Index, CoopBattlePayloadKind.BattleSnapshot);
            if (_pendingPayloadsByKey.TryGetValue(transmissionKey, out PendingPayloadTransmission pendingTransmission) &&
                pendingTransmission != null)
            {
                return false;
            }

            _expectedBattleSnapshotTransmissionIdByPeer.TryGetValue(peer.Index, out int expectedTransmissionId);
            _acknowledgedBattleSnapshotTransmissionIdByPeer.TryGetValue(peer.Index, out int acknowledgedTransmissionId);
            if (expectedTransmissionId <= 0 || acknowledgedTransmissionId >= expectedTransmissionId)
                return false;

            if (!_lastSentBattleSnapshotPayloadByPeer.ContainsKey(peer.Index) ||
                !_lastCompletedBattleSnapshotTransmissionUtcByPeer.TryGetValue(peer.Index, out DateTime completedUtc))
            {
                return false;
            }

            DateTime nowUtc = DateTime.UtcNow;
            if (nowUtc - completedUtc < BattleSnapshotAckRetryDelay)
                return false;

            if (_lastBattleSnapshotRetryUtcByPeer.TryGetValue(peer.Index, out DateTime previousRetryUtc) &&
                nowUtc - previousRetryUtc < BattleSnapshotAckRetryDelay)
            {
                return false;
            }

            _lastBattleSnapshotRetryUtcByPeer[peer.Index] = nowUtc;
            ModLogger.Info(
                "CoopMissionNetworkBridge: retrying unacknowledged battle snapshot payload. " +
                "Peer=" + (peer.UserName ?? "null") +
                " ExpectedTransmissionId=" + expectedTransmissionId +
                " AcknowledgedTransmissionId=" + acknowledgedTransmissionId +
                " SecondsSinceCompleted=" + (nowUtc - completedUtc).TotalSeconds.ToString("F1"));
            TrySendBattleSnapshotToPeer(peer, force: true);
            return true;
        }

        private void MarkBattleSnapshotTransmissionCompleted(int peerIndex)
        {
            if (peerIndex < 0)
                return;

            _lastCompletedBattleSnapshotTransmissionUtcByPeer[peerIndex] = DateTime.UtcNow;
        }

        private void AcceptClientPayloadChunk(CoopBattlePayloadChunkMessage message)
        {
            if (message == null || message.ChunkCount <= 0 || message.ChunkIndex < 0 || message.ChunkIndex >= message.ChunkCount)
                return;

            if (UseBattleSnapshotTransportV2 &&
                message.PayloadKind == CoopBattlePayloadKind.BattleSnapshot &&
                message.ChunkIndex == 0)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: legacy battle snapshot payload received while V2 transport is enabled. " +
                    "TransmissionId=" + message.TransmissionId +
                    " ChunkCount=" + message.ChunkCount +
                    " Bytes=" + (message.PayloadBytes?.Length ?? 0));
            }

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
            ApplyCompletedPayload(assemblyState.PayloadKind, assemblyState.TransmissionId, payloadBytes);
        }

        private void ApplyCompletedPayload(CoopBattlePayloadKind payloadKind, int transmissionId, byte[] payloadBytes)
        {
            if (!TryDecodePayloadJson(payloadKind, payloadBytes, out string payloadJson))
                return;

            if (string.IsNullOrWhiteSpace(payloadJson))
                return;

            switch (payloadKind)
            {
                case CoopBattlePayloadKind.AuthoritativeMaterializedAgentEntrySnapshot:
                {
                    CoopBattleEntryStatusBridgeFile.AuthoritativeMaterializedAgentEntrySnapshot snapshot =
                        JsonConvert.DeserializeObject<CoopBattleEntryStatusBridgeFile.AuthoritativeMaterializedAgentEntrySnapshot>(payloadJson, JsonSettings);
                    if (snapshot != null)
                    {
                        CoopMissionSpawnLogic.ObserveClientAuthoritativeMaterializedAgentEntrySnapshot(
                            snapshot,
                            "CoopMissionNetworkBridge.ApplyCompletedPayload");
                        ModLogger.Info(
                            "CoopMissionNetworkBridge: applied client payload. " +
                            "Kind=" + payloadKind +
                            " BattleId=" + (snapshot.BattleId ?? string.Empty) +
                            " MissionName=" + (snapshot.MissionName ?? string.Empty) +
                            " UseStringIdExactEquipmentPath=" + snapshot.UseStringIdExactEquipmentPath +
                            " EntryCount=" + snapshot.EntryCount +
                            " Source=" + (snapshot.Source ?? string.Empty));
                        BattleMapSpawnHandoffPatch.TryProcessDeferredClientCreateAgentMessages(
                            Mission,
                            "CoopMissionNetworkBridge authoritative materialized snapshot applied");
                        BattleMapSpawnHandoffPatch.TryProcessDeferredClientMountedHeroCreateAgents(
                            Mission,
                            "CoopMissionNetworkBridge authoritative materialized snapshot applied");
                    }
                    break;
                }
                case CoopBattlePayloadKind.EntryStatusSnapshot:
                {
                    CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot =
                        JsonConvert.DeserializeObject<CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot>(payloadJson, JsonSettings);
                    if (snapshot != null)
                    {
                        int selectableEntryCount = CountSerializedIdList(snapshot.SelectableEntryIds);
                        int attackerSelectableEntryCount = snapshot.AttackerSelectableEntryCount > 0
                            ? snapshot.AttackerSelectableEntryCount
                            : CountSerializedIdList(snapshot.AttackerSelectableEntryIds);
                        int defenderSelectableEntryCount = snapshot.DefenderSelectableEntryCount > 0
                            ? snapshot.DefenderSelectableEntryCount
                            : CountSerializedIdList(snapshot.DefenderSelectableEntryIds);
                        CoopBattleEntryStatusBridgeFile.WriteStatus(snapshot);
                        ModLogger.Info(
                            "CoopMissionNetworkBridge: applied client payload. " +
                            "Kind=" + payloadKind +
                            " BattleDataReady=" + snapshot.BattleDataReady +
                            " BattleDataReadinessStage=" + (snapshot.BattleDataReadinessStage ?? string.Empty) +
                            " BattleDataReadinessReason=" + (snapshot.BattleDataReadinessReason ?? string.Empty) +
                            " AssignedSide=" + (snapshot.AssignedSide ?? string.Empty) +
                            " SelectedEntryId=" + (snapshot.SelectedEntryId ?? string.Empty) +
                            " SelectableEntryCount=" + selectableEntryCount +
                            " SelectableEntrySource=" + (snapshot.SelectableEntrySource ?? string.Empty) +
                            " AttackerSelectableEntryCount=" + attackerSelectableEntryCount +
                            " DefenderSelectableEntryCount=" + defenderSelectableEntryCount +
                            " AuthoritativeMaterializedAgentEntryCount=" + snapshot.AuthoritativeMaterializedAgentEntryCount +
                            " CanRespawn=" + snapshot.CanRespawn +
                            " CanStartBattle=" + snapshot.CanStartBattle +
                            " HasAgent=" + snapshot.HasAgent +
                            " Lifecycle=" + (snapshot.LifecycleState ?? string.Empty) +
                            " Peer=" + (snapshot.PeerName ?? string.Empty));
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
                        bool catalogPrepared = BattleCatalogParityState.TryPrepareCurrentBattleCatalog(
                            transmissionId,
                            "CoopMissionNetworkBridge.ApplyCompletedPayload",
                            out string catalogHash,
                            out string catalogSummary);
                        if (catalogPrepared)
                            MarkClientBattleSnapshotApplied(transmissionId, string.Empty);
                        bool acknowledged =
                            catalogPrepared &&
                            CoopBattleNetworkRequestTransport.TryAcknowledgeBattleSnapshot(
                                transmissionId,
                                "CoopMissionNetworkBridge.ApplyCompletedPayload",
                                catalogHash);
                        ModLogger.Info(
                            "CoopMissionNetworkBridge: applied client payload. " +
                            "Kind=" + payloadKind +
                            " TransmissionId=" + transmissionId +
                            " BattleId=" + (snapshot.BattleId ?? string.Empty) +
                            " MapScene=" + (snapshot.MapScene ?? string.Empty) +
                            " Parties=" + (snapshot.Sides?.Sum(side => side?.Parties?.Count ?? 0) ?? 0) +
                            " Sides=" + (snapshot.Sides?.Count ?? 0) +
                            " CatalogPrepared=" + catalogPrepared +
                            " CatalogHash=" + (string.IsNullOrWhiteSpace(catalogHash) ? "null" : catalogHash) +
                            " CatalogSummary={" + (catalogSummary ?? "unknown") + "}" +
                            " AckSent=" + acknowledged);
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
            string originalSource = snapshot.Source;
            string originalBattlePhaseSource = snapshot.BattlePhaseSource;
            string originalLifecycleSource = snapshot.LifecycleSource;
            string originalSpawnReason = snapshot.SpawnReason;
            EntryStatusTransportFieldState transportFieldState = CompactEntryStatusSnapshotForTransport(snapshot);
            try
            {
                snapshot.UpdatedUtc = DateTime.MinValue;
                snapshot.Source = string.Empty;
                snapshot.BattlePhaseSource = string.Empty;
                snapshot.LifecycleSource = string.Empty;
                snapshot.SpawnReason = string.Empty;
                return SerializePayload(snapshot);
            }
            finally
            {
                transportFieldState.Restore(snapshot);
                snapshot.UpdatedUtc = originalUpdatedUtc;
                snapshot.Source = originalSource;
                snapshot.BattlePhaseSource = originalBattlePhaseSource;
                snapshot.LifecycleSource = originalLifecycleSource;
                snapshot.SpawnReason = originalSpawnReason;
            }
        }

        private static string SerializeComparableMaterializedAgentEntryPayload(
            CoopBattleEntryStatusBridgeFile.AuthoritativeMaterializedAgentEntrySnapshot snapshot)
        {
            if (snapshot == null)
                return string.Empty;

            DateTime originalUpdatedUtc = snapshot.UpdatedUtc;
            string originalSource = snapshot.Source;
            try
            {
                snapshot.UpdatedUtc = DateTime.MinValue;
                snapshot.Source = string.Empty;
                return SerializePayload(snapshot);
            }
            finally
            {
                snapshot.UpdatedUtc = originalUpdatedUtc;
                snapshot.Source = originalSource;
            }
        }

        private bool TryGetBattleSnapshotTransmissionPayload(
            BattleSnapshotMessage snapshot,
            out byte[] payloadBytes,
            out int logicalByteCount,
            out string comparisonKey)
        {
            return TryGetBattleSnapshotTransmissionPayloadDescriptor(
                snapshot,
                out payloadBytes,
                out logicalByteCount,
                out comparisonKey,
                out _,
                out _);
        }

        private bool TryGetBattleSnapshotTransmissionPayloadDescriptorV2(
            BattleSnapshotMessage snapshot,
            out byte[] payloadBytes,
            out int logicalByteCount,
            out string comparisonKey,
            out string payloadHash,
            out CoopBattleSnapshotCompressionKind compressionKind,
            out CoopBattleSnapshotPayloadEncoding payloadEncoding)
        {
            payloadBytes = Array.Empty<byte>();
            logicalByteCount = 0;
            payloadHash = string.Empty;
            compressionKind = CoopBattleSnapshotCompressionKind.None;
            payloadEncoding = CoopBattleSnapshotPayloadEncoding.JsonUtf8;
            comparisonKey = BuildBattleSnapshotComparisonKey(snapshot, BattleSnapshotRuntimeState.GetUpdatedUtc());
            if (string.IsNullOrWhiteSpace(comparisonKey))
                return false;

            if (string.Equals(_cachedBattleSnapshotV2ComparisonKey, comparisonKey, StringComparison.Ordinal) &&
                _cachedBattleSnapshotV2PayloadBytes != null &&
                _cachedBattleSnapshotV2PayloadBytes.Length > 0)
            {
                payloadBytes = _cachedBattleSnapshotV2PayloadBytes;
                logicalByteCount = _cachedBattleSnapshotV2LogicalBytes;
                payloadHash = _cachedBattleSnapshotV2PayloadHash;
                compressionKind = _cachedBattleSnapshotV2CompressionKind;
                payloadEncoding = _cachedBattleSnapshotV2PayloadEncoding;
                return true;
            }

            if (!TrySerializeBattleSnapshotPayloadV2(snapshot, out byte[] rawBytes, out payloadEncoding))
                return false;

            byte[] wireBytes = CompressPayload(rawBytes, out bool compressed);
            payloadBytes = wireBytes ?? rawBytes;
            logicalByteCount = rawBytes.Length;
            compressionKind = compressed ? CoopBattleSnapshotCompressionKind.Gzip : CoopBattleSnapshotCompressionKind.None;
            payloadHash = ComputePayloadHash(payloadBytes);

            _cachedBattleSnapshotV2ComparisonKey = comparisonKey;
            _cachedBattleSnapshotV2PayloadBytes = payloadBytes;
            _cachedBattleSnapshotV2LogicalBytes = logicalByteCount;
            _cachedBattleSnapshotV2PayloadHash = payloadHash;
            _cachedBattleSnapshotV2CompressionKind = compressionKind;
            _cachedBattleSnapshotV2PayloadEncoding = payloadEncoding;

            int chunkCount = Math.Max(1, (payloadBytes.Length + CoopBattleSnapshotChunkV2Message.MaxChunkBytes - 1) / CoopBattleSnapshotChunkV2Message.MaxChunkBytes);
            ModLogger.Info(
                "CoopMissionNetworkBridge: prepared V2 battle snapshot transport payload. " +
                "ComparisonKey=" + comparisonKey +
                " Encoding=" + payloadEncoding +
                " RawBytes=" + rawBytes.Length +
                " WireBytes=" + payloadBytes.Length +
                " Compressed=" + compressed +
                " Chunks=" + chunkCount +
                " Entries=" + GetBattleSnapshotEntryCount(snapshot));
            return true;
        }

        private bool TryGetBattleSnapshotTransmissionPayloadDescriptor(
            BattleSnapshotMessage snapshot,
            out byte[] payloadBytes,
            out int logicalByteCount,
            out string comparisonKey,
            out string payloadHash,
            out CoopBattleSnapshotCompressionKind compressionKind)
        {
            payloadBytes = Array.Empty<byte>();
            logicalByteCount = 0;
            payloadHash = string.Empty;
            compressionKind = CoopBattleSnapshotCompressionKind.None;
            comparisonKey = BuildBattleSnapshotComparisonKey(snapshot, BattleSnapshotRuntimeState.GetUpdatedUtc());
            if (string.IsNullOrWhiteSpace(comparisonKey))
                return false;

            if (string.Equals(_cachedBattleSnapshotComparisonKey, comparisonKey, StringComparison.Ordinal) &&
                _cachedBattleSnapshotPayloadBytes != null &&
                _cachedBattleSnapshotPayloadBytes.Length > 0)
            {
                payloadBytes = _cachedBattleSnapshotPayloadBytes;
                logicalByteCount = _cachedBattleSnapshotLogicalBytes;
                payloadHash = _cachedBattleSnapshotPayloadHash;
                compressionKind = _cachedBattleSnapshotCompressionKind;
                return true;
            }

            string payloadJson = SerializePayload(snapshot);
            if (string.IsNullOrWhiteSpace(payloadJson))
                return false;

            byte[] rawBytes = Encoding.UTF8.GetBytes(payloadJson);
            if (rawBytes.Length <= 0)
                return false;

            byte[] wireBytes = CompressPayload(rawBytes, out bool compressed);
            payloadBytes = wireBytes ?? rawBytes;
            logicalByteCount = rawBytes.Length;
            compressionKind = compressed ? CoopBattleSnapshotCompressionKind.Gzip : CoopBattleSnapshotCompressionKind.None;
            payloadHash = ComputePayloadHash(payloadBytes);

            _cachedBattleSnapshotComparisonKey = comparisonKey;
            _cachedBattleSnapshotPayloadBytes = payloadBytes;
            _cachedBattleSnapshotLogicalBytes = logicalByteCount;
            _cachedBattleSnapshotPayloadHash = payloadHash;
            _cachedBattleSnapshotCompressionKind = compressionKind;

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

        private static byte[] CompressPayload(byte[] rawBytes, out bool compressed)
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
                ModLogger.Info("CoopMissionNetworkBridge: payload compression failed, falling back to raw transport bytes. Error=" + ex.Message);
            }

            return rawBytes;
        }

        private static string ComputePayloadHash(byte[] payloadBytes)
        {
            if (payloadBytes == null || payloadBytes.Length <= 0)
                return string.Empty;

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(payloadBytes);
                var builder = new StringBuilder(hashBytes.Length * 2);
                for (int i = 0; i < hashBytes.Length; i++)
                    builder.Append(hashBytes[i].ToString("x2"));
                return builder.ToString();
            }
        }

        private static bool TryDecodePayloadJson(CoopBattlePayloadKind payloadKind, byte[] payloadBytes, out string payloadJson)
        {
            payloadJson = string.Empty;
            if (payloadBytes == null || payloadBytes.Length <= 0)
                return false;

            byte[] decodedBytes = payloadBytes;
            if (IsGzipPayload(payloadBytes))
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

        private static bool TryDecodeBattleSnapshotPayload(
            BattleSnapshotClientAssemblyState assemblyState,
            byte[] payloadBytes,
            out BattleSnapshotMessage snapshot)
        {
            snapshot = null;
            if (assemblyState == null || payloadBytes == null || payloadBytes.Length <= 0)
                return false;

            byte[] decodedBytes = payloadBytes;
            if (assemblyState.CompressionKind == CoopBattleSnapshotCompressionKind.Gzip)
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
                    ModLogger.Info("CoopMissionNetworkBridge: V2 battle snapshot decompression failed. Error=" + ex.Message);
                    return false;
                }
            }

            if (decodedBytes.Length <= 0)
                return false;

            if (assemblyState.PayloadEncoding == CoopBattleSnapshotPayloadEncoding.BinaryV1)
                return BattleSnapshotBinarySerializer.TryDeserialize(decodedBytes, out snapshot);

            string payloadJson = Encoding.UTF8.GetString(decodedBytes);
            if (string.IsNullOrWhiteSpace(payloadJson))
                return false;

            snapshot = JsonConvert.DeserializeObject<BattleSnapshotMessage>(payloadJson, JsonSettings);
            return snapshot != null;
        }

        private static string BuildChunkRangesString(IEnumerable<ChunkRange> ranges)
        {
            if (ranges == null)
                return string.Empty;

            return string.Join(",",
                ranges
                    .Where(range => range.EndIndex >= range.StartIndex)
                    .Select(range => range.StartIndex == range.EndIndex
                        ? range.StartIndex.ToString()
                        : range.StartIndex + "-" + range.EndIndex));
        }

        private static List<ChunkRange> ParseChunkRanges(string rawValue)
        {
            var ranges = new List<ChunkRange>();
            if (string.IsNullOrWhiteSpace(rawValue))
                return ranges;

            string[] parts = rawValue.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawPart in parts)
            {
                string part = rawPart.Trim();
                if (part.Length <= 0)
                    continue;

                int separatorIndex = part.IndexOf('-');
                if (separatorIndex < 0)
                {
                    if (int.TryParse(part, out int singleIndex))
                        ranges.Add(new ChunkRange(singleIndex, singleIndex));
                    continue;
                }

                string startRaw = part.Substring(0, separatorIndex);
                string endRaw = part.Substring(separatorIndex + 1);
                if (int.TryParse(startRaw, out int startIndex) && int.TryParse(endRaw, out int endIndex))
                    ranges.Add(new ChunkRange(startIndex, endIndex));
            }

            return ranges;
        }

        private static void SendClientBattleSnapshotChunkRequest(
            BattleSnapshotClientAssemblyState assemblyState,
            CoopBattleSnapshotAssemblyStateKind assemblyStateKind,
            string source)
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive || assemblyState == null)
                return;

            try
            {
                if (!assemblyState.TryGetInitialWindowRange(BattleSnapshotInitialWindowChunks, out int startChunkIndex, out int endChunkIndex))
                    return;

                GameNetwork.BeginModuleEventAsClient();
                GameNetwork.WriteMessage(new CoopBattleSnapshotChunkRequestMessage(
                    assemblyState.TransmissionId,
                    startChunkIndex,
                    endChunkIndex,
                    assemblyState.HighestContiguousChunkIndex,
                    assemblyState.ReceivedChunkCount,
                    assemblyStateKind));
                GameNetwork.EndModuleEventAsClient();
                assemblyState.MarkInitialWindowRequestSent(startChunkIndex, endChunkIndex, DateTime.UtcNow);
                ModLogger.Info(
                    "CoopMissionNetworkBridge: sent client V2 battle snapshot chunk request. " +
                    "TransmissionId=" + assemblyState.TransmissionId +
                    " Range=" + startChunkIndex + "-" + endChunkIndex +
                    " HighestContiguous=" + assemblyState.HighestContiguousChunkIndex +
                    " ReceivedChunkCount=" + assemblyState.ReceivedChunkCount +
                    " State=" + assemblyStateKind +
                    " Source=" + (source ?? "unknown"));
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: client V2 battle snapshot chunk request send failed. Error=" + ex.Message);
            }
        }

        private static void SendClientBattleSnapshotRangeAck(
            BattleSnapshotClientAssemblyState assemblyState,
            CoopBattleSnapshotAssemblyStateKind assemblyStateKind,
            string source)
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive || assemblyState == null)
                return;

            try
            {
                GameNetwork.BeginModuleEventAsClient();
                GameNetwork.WriteMessage(new CoopBattleSnapshotRangeAckMessage(
                    assemblyState.TransmissionId,
                    assemblyState.HighestContiguousChunkIndex,
                    assemblyState.ReceivedChunkCount,
                    string.Empty,
                    string.Empty,
                    assemblyStateKind));
                GameNetwork.EndModuleEventAsClient();
                assemblyState.MarkProgressAckSent(DateTime.UtcNow);
                ModLogger.Info(
                    "CoopMissionNetworkBridge: sent client V2 battle snapshot range ack. " +
                    "TransmissionId=" + assemblyState.TransmissionId +
                    " HighestContiguous=" + assemblyState.HighestContiguousChunkIndex +
                    " ReceivedChunkCount=" + assemblyState.ReceivedChunkCount +
                    " State=" + assemblyStateKind +
                    " Source=" + (source ?? "unknown"));
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: client V2 battle snapshot range ack send failed. Error=" + ex.Message);
            }
        }

        private static void SendClientBattleSnapshotCompleteAck(int transmissionId, string payloadHash, bool appliedSuccessfully, string catalogHash)
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive || transmissionId <= 0)
                return;

            try
            {
                GameNetwork.BeginModuleEventAsClient();
                GameNetwork.WriteMessage(new CoopBattleSnapshotCompleteAckMessage(transmissionId, appliedSuccessfully, payloadHash, catalogHash));
                GameNetwork.EndModuleEventAsClient();
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: client V2 battle snapshot complete ack send failed. Error=" + ex.Message);
            }
        }

        private static void SendClientBattleSnapshotAbort(int transmissionId, string reason)
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive || transmissionId <= 0)
                return;

            try
            {
                GameNetwork.BeginModuleEventAsClient();
                GameNetwork.WriteMessage(new CoopBattleSnapshotAbortMessage(transmissionId, reason));
                GameNetwork.EndModuleEventAsClient();
            }
            catch (Exception ex)
            {
                ModLogger.Info("CoopMissionNetworkBridge: client V2 battle snapshot abort send failed. Error=" + ex.Message);
            }
        }

        private PendingPayloadTransmission CreateEntryStatusPendingTransmission(
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot,
            string comparisonJson)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(comparisonJson))
                return null;

            EntryStatusTransportFieldState transportFieldState = CompactEntryStatusSnapshotForTransport(snapshot);
            try
            {
                string payloadJson = SerializePayload(snapshot);
                if (string.IsNullOrWhiteSpace(payloadJson))
                    return null;

                byte[] rawBytes = Encoding.UTF8.GetBytes(payloadJson);
                if (rawBytes.Length <= 0)
                    return null;

                byte[] wireBytes = CompressPayload(rawBytes, out bool compressed);
                PendingPayloadTransmission transmission = PendingPayloadTransmission.Create(
                    CoopBattlePayloadKind.EntryStatusSnapshot,
                    wireBytes ?? rawBytes,
                    rawBytes.Length,
                    comparisonJson,
                    NextTransmissionId());
                if (transmission == null)
                    return null;

                if (compressed)
                {
                    ModLogger.Info(
                        "CoopMissionNetworkBridge: compressed entry status transport payload. " +
                        "RawBytes=" + rawBytes.Length +
                        " WireBytes=" + transmission.TotalBytes +
                        " Chunks=" + transmission.ChunkCount);
                }

                return transmission;
            }
            finally
            {
                transportFieldState.Restore(snapshot);
            }
        }

        private PendingPayloadTransmission CreateMaterializedAgentEntryPendingTransmission(
            CoopBattleEntryStatusBridgeFile.AuthoritativeMaterializedAgentEntrySnapshot snapshot,
            string comparisonJson)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(comparisonJson))
                return null;

            string payloadJson = SerializePayload(snapshot);
            if (string.IsNullOrWhiteSpace(payloadJson))
                return null;

            byte[] rawBytes = Encoding.UTF8.GetBytes(payloadJson);
            if (rawBytes.Length <= 0)
                return null;

            byte[] wireBytes = CompressPayload(rawBytes, out bool compressed);
            PendingPayloadTransmission transmission = PendingPayloadTransmission.Create(
                CoopBattlePayloadKind.AuthoritativeMaterializedAgentEntrySnapshot,
                wireBytes ?? rawBytes,
                rawBytes.Length,
                comparisonJson,
                NextTransmissionId());
            if (transmission == null)
                return null;

            if (compressed)
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: compressed materialized-agent-entry transport payload. " +
                    "RawBytes=" + rawBytes.Length +
                    " WireBytes=" + transmission.TotalBytes +
                    " Chunks=" + transmission.ChunkCount);
            }

            return transmission;
        }

        private static int CountSerializedIdList(string rawValue)
        {
            return CoopBattleEntryStatusBridgeFile.DeserializeIdList(rawValue)?.Length ?? 0;
        }

        private static bool IsGzipPayload(byte[] payloadBytes)
        {
            return payloadBytes != null &&
                   payloadBytes.Length >= 2 &&
                   payloadBytes[0] == 0x1F &&
                   payloadBytes[1] == 0x8B;
        }

        private static EntryStatusTransportFieldState CompactEntryStatusSnapshotForTransport(
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot)
        {
            if (snapshot == null)
                return default(EntryStatusTransportFieldState);

            EntryStatusTransportFieldState state = EntryStatusTransportFieldState.Capture(snapshot);
            snapshot.AllowedTroopIds = string.Empty;
            snapshot.AllowedEntryIds = string.Empty;
            snapshot.AuthoritativeMaterializedAgentEntryCount = 0;
            snapshot.AuthoritativeMaterializedAgentEntries = string.Empty;
            snapshot.AttackerAllowedTroopIds = string.Empty;
            snapshot.AttackerAllowedEntryIds = string.Empty;
            snapshot.AttackerSelectableEntryIds = string.Empty;
            snapshot.DefenderAllowedTroopIds = string.Empty;
            snapshot.DefenderAllowedEntryIds = string.Empty;
            snapshot.DefenderSelectableEntryIds = string.Empty;
            return state;
        }

        private int NextTransmissionId()
        {
            if (_nextTransmissionId >= 1048575)
                _nextTransmissionId = 1;

            return _nextTransmissionId++;
        }

        internal static bool HasPeerAcknowledgedCurrentBattleSnapshot(
            MissionPeer missionPeer,
            out int expectedTransmissionId,
            out int acknowledgedTransmissionId)
        {
            expectedTransmissionId = 0;
            acknowledgedTransmissionId = 0;

            NetworkCommunicator peer = missionPeer?.GetNetworkPeer();
            if (peer == null || peer.IsServerPeer)
                return false;

            _expectedBattleSnapshotTransmissionIdByPeer.TryGetValue(peer.Index, out expectedTransmissionId);
            _acknowledgedBattleSnapshotTransmissionIdByPeer.TryGetValue(peer.Index, out acknowledgedTransmissionId);
            return expectedTransmissionId > 0 && acknowledgedTransmissionId >= expectedTransmissionId;
        }

        internal static bool IsPeerCurrentBattleSnapshotBootstrapReady(
            NetworkCommunicator peer,
            out string readinessSummary)
        {
            readinessSummary = string.Empty;
            if (!GameNetwork.IsServer)
            {
                readinessSummary = "not-server";
                return true;
            }

            if (peer == null)
            {
                readinessSummary = "peer-null";
                return false;
            }

            if (peer.IsServerPeer)
            {
                readinessSummary = "server-peer";
                return true;
            }

            _expectedBattleSnapshotTransmissionIdByPeer.TryGetValue(peer.Index, out int expectedTransmissionId);
            _acknowledgedBattleSnapshotTransmissionIdByPeer.TryGetValue(peer.Index, out int acknowledgedTransmissionId);
            bool snapshotReady =
                expectedTransmissionId > 0 &&
                acknowledgedTransmissionId >= expectedTransmissionId;
            readinessSummary =
                "Peer=" + (peer.UserName ?? peer.Index.ToString()) +
                " ExpectedTransmissionId=" + expectedTransmissionId +
                " AcknowledgedTransmissionId=" + acknowledgedTransmissionId +
                " SnapshotReady=" + snapshotReady;
            return snapshotReady;
        }

        internal static bool HaveAllEligiblePeersAcknowledgedCurrentBattleSnapshot(
            Mission mission,
            out string readinessSummary)
        {
            readinessSummary = string.Empty;
            if (!GameNetwork.IsServer)
            {
                readinessSummary = "not-server";
                return true;
            }

            if (mission == null)
            {
                readinessSummary = "mission-null";
                return false;
            }

            int eligiblePeerCount = 0;
            int acknowledgedPeerCount = 0;
            string blockingPeer = "none";
            int blockingExpectedTransmissionId = 0;
            int blockingAcknowledgedTransmissionId = 0;

            if (GameNetwork.NetworkPeers != null)
            {
                foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
                {
                    if (!IsBattleSnapshotBootstrapEligiblePeer(peer))
                        continue;

                    eligiblePeerCount++;
                    _expectedBattleSnapshotTransmissionIdByPeer.TryGetValue(peer.Index, out int expectedTransmissionId);
                    _acknowledgedBattleSnapshotTransmissionIdByPeer.TryGetValue(peer.Index, out int acknowledgedTransmissionId);
                    bool acknowledged =
                        expectedTransmissionId > 0 &&
                        acknowledgedTransmissionId >= expectedTransmissionId;
                    if (acknowledged)
                    {
                        acknowledgedPeerCount++;
                        continue;
                    }

                    if (!string.Equals(blockingPeer, "none", StringComparison.Ordinal))
                        continue;

                    blockingPeer = peer.UserName ?? peer.Index.ToString();
                    blockingExpectedTransmissionId = expectedTransmissionId;
                    blockingAcknowledgedTransmissionId = acknowledgedTransmissionId;
                }
            }

            readinessSummary =
                "EligiblePeers=" + eligiblePeerCount +
                " AcknowledgedPeers=" + acknowledgedPeerCount +
                " BlockingPeer=" + blockingPeer +
                " ExpectedTransmissionId=" + blockingExpectedTransmissionId +
                " AcknowledgedTransmissionId=" + blockingAcknowledgedTransmissionId;
            return eligiblePeerCount <= 0 || acknowledgedPeerCount >= eligiblePeerCount;
        }

        internal static bool IsClientCurrentBattleSnapshotApplied(out string readinessSummary)
        {
            readinessSummary = string.Empty;

            if (!GameNetwork.IsClient)
            {
                readinessSummary = "not-client";
                return true;
            }

            Mission mission = Mission.Current;
            if (mission == null)
            {
                readinessSummary = "mission-null";
                return false;
            }

            CoopMissionNetworkBridge bridge = mission.GetMissionBehavior<CoopMissionNetworkBridge>();
            if (bridge == null)
            {
                readinessSummary = "bridge-null";
                return false;
            }

            int observedTransmissionId = _clientObservedBattleSnapshotTransmissionId;
            string observedPayloadHash = _clientObservedBattleSnapshotPayloadHash ?? string.Empty;
            int appliedTransmissionId = _clientAppliedBattleSnapshotTransmissionId;
            string appliedPayloadHash = _clientAppliedBattleSnapshotPayloadHash ?? string.Empty;
            BattleSnapshotClientAssemblyState observedAssemblyState = null;
            bool hasPendingObservedAssembly =
                observedTransmissionId > 0 &&
                bridge._clientBattleSnapshotAssembliesByTransmission.TryGetValue(
                    observedTransmissionId,
                    out observedAssemblyState) &&
                observedAssemblyState != null;
            bool pendingObservedAssemblyMatchesAppliedState =
                hasPendingObservedAssembly &&
                appliedTransmissionId == observedTransmissionId &&
                string.Equals(appliedPayloadHash, observedPayloadHash, StringComparison.Ordinal) &&
                string.Equals(observedAssemblyState.PayloadHash, appliedPayloadHash, StringComparison.Ordinal);
            bool hasBlockingPendingObservedAssembly =
                hasPendingObservedAssembly &&
                !pendingObservedAssemblyMatchesAppliedState;
            bool applied =
                observedTransmissionId > 0 &&
                appliedTransmissionId == observedTransmissionId &&
                string.Equals(appliedPayloadHash, observedPayloadHash, StringComparison.Ordinal) &&
                !hasBlockingPendingObservedAssembly;
            bool clientCatalogPrepared = BattleCatalogParityState.TryGetClientPreparedCatalogState(
                observedTransmissionId,
                out string preparedCatalogHash,
                out string preparedCatalogSummary);
            applied &= clientCatalogPrepared;

            readinessSummary =
                "ObservedTransmissionId=" + observedTransmissionId +
                " AppliedTransmissionId=" + appliedTransmissionId +
                " ObservedPayloadHash=" + (string.IsNullOrWhiteSpace(observedPayloadHash) ? "null" : observedPayloadHash) +
                " AppliedPayloadHash=" + (string.IsNullOrWhiteSpace(appliedPayloadHash) ? "null" : appliedPayloadHash) +
                " HasPendingObservedAssembly=" + hasPendingObservedAssembly +
                " PendingObservedAssemblyMatchesAppliedState=" + pendingObservedAssemblyMatchesAppliedState +
                " HasBlockingPendingObservedAssembly=" + hasBlockingPendingObservedAssembly +
                " CatalogPrepared=" + clientCatalogPrepared +
                " CatalogHash=" + (string.IsNullOrWhiteSpace(preparedCatalogHash) ? "null" : preparedCatalogHash) +
                " CatalogSummary={" + (preparedCatalogSummary ?? "unknown") + "}";
            return applied;
        }

        private static void RegisterExpectedBattleSnapshotTransmission(int peerIndex, int transmissionId)
        {
            if (peerIndex < 0 || transmissionId <= 0)
                return;

            _expectedBattleSnapshotTransmissionIdByPeer[peerIndex] = transmissionId;
        }

        private static void ClearPeerBattleSnapshotSyncState(int peerIndex)
        {
            if (peerIndex < 0)
                return;

            _expectedBattleSnapshotTransmissionIdByPeer.Remove(peerIndex);
            _acknowledgedBattleSnapshotTransmissionIdByPeer.Remove(peerIndex);
        }

        private bool TryAcknowledgePeerBattleSnapshot(NetworkCommunicator peer, string rawTransmissionId)
        {
            string parsedCatalogHash = string.Empty;
            int transmissionId = 0;
            if (peer == null ||
                string.IsNullOrWhiteSpace(rawTransmissionId) ||
                !TryParseBattleSnapshotReadyAck(rawTransmissionId, out transmissionId, out parsedCatalogHash))
            {
                ModLogger.Info(
                    "CoopMissionNetworkBridge: rejected battle snapshot readiness ack. " +
                    "Peer=" + (peer?.UserName ?? "null") +
                    " RawTransmissionId=" + (rawTransmissionId ?? string.Empty));
                return false;
            }

            _expectedBattleSnapshotTransmissionIdByPeer.TryGetValue(peer.Index, out int expectedTransmissionId);
            bool snapshotReady = expectedTransmissionId > 0 && transmissionId >= expectedTransmissionId;
            bool serverCatalogPrepared = BattleCatalogParityState.TryPrepareCurrentBattleCatalog(
                transmissionId,
                "CoopMissionNetworkBridge.TryAcknowledgePeerBattleSnapshot",
                out string expectedCatalogHash,
                out string expectedCatalogSummary);
            bool catalogHashMatched =
                serverCatalogPrepared &&
                !string.IsNullOrWhiteSpace(parsedCatalogHash) &&
                string.Equals(parsedCatalogHash, expectedCatalogHash, StringComparison.Ordinal);
            bool bootstrapReady = snapshotReady && serverCatalogPrepared && catalogHashMatched;
            if (bootstrapReady)
                _acknowledgedBattleSnapshotTransmissionIdByPeer[peer.Index] = transmissionId;
            if (snapshotReady)
            {
                _lastCompletedBattleSnapshotTransmissionUtcByPeer.Remove(peer.Index);
                _lastBattleSnapshotRetryUtcByPeer.Remove(peer.Index);
            }
            ModLogger.Info(
                "CoopMissionNetworkBridge: acknowledged client battle snapshot readiness. " +
                "Peer=" + (peer.UserName ?? "null") +
                " TransmissionId=" + transmissionId +
                " ExpectedTransmissionId=" + expectedTransmissionId +
                " SnapshotReady=" + snapshotReady +
                " ServerCatalogPrepared=" + serverCatalogPrepared +
                " CatalogHashMatched=" + catalogHashMatched +
                " ClientCatalogHash=" + (string.IsNullOrWhiteSpace(parsedCatalogHash) ? "null" : parsedCatalogHash) +
                " ExpectedCatalogHash=" + (string.IsNullOrWhiteSpace(expectedCatalogHash) ? "null" : expectedCatalogHash) +
                " BootstrapReady=" + bootstrapReady +
                " ExpectedCatalogSummary={" + (expectedCatalogSummary ?? "unknown") + "}");
            return bootstrapReady;
        }

        private static bool TryParseBattleSnapshotReadyAck(string rawTransmissionId, out int transmissionId, out string catalogHash)
        {
            transmissionId = 0;
            catalogHash = string.Empty;
            if (string.IsNullOrWhiteSpace(rawTransmissionId))
                return false;

            string[] parts = rawTransmissionId.Split(new[] { '|' }, 2, StringSplitOptions.None);
            if (parts.Length <= 0 || !int.TryParse(parts[0], out transmissionId))
                return false;

            if (parts.Length > 1)
                catalogHash = parts[1] ?? string.Empty;

            return transmissionId > 0;
        }

        private bool TrySerializeBattleSnapshotPayloadV2(
            BattleSnapshotMessage snapshot,
            out byte[] rawBytes,
            out CoopBattleSnapshotPayloadEncoding payloadEncoding)
        {
            rawBytes = Array.Empty<byte>();
            payloadEncoding = CoopBattleSnapshotPayloadEncoding.JsonUtf8;

            if (BattleSnapshotBinarySerializer.TrySerialize(snapshot, out rawBytes) &&
                rawBytes != null &&
                rawBytes.Length > 0)
            {
                payloadEncoding = CoopBattleSnapshotPayloadEncoding.BinaryV1;
                return true;
            }

            string payloadJson = SerializePayload(snapshot);
            if (string.IsNullOrWhiteSpace(payloadJson))
                return false;

            rawBytes = Encoding.UTF8.GetBytes(payloadJson);
            payloadEncoding = CoopBattleSnapshotPayloadEncoding.JsonUtf8;
            ModLogger.Info("CoopMissionNetworkBridge: falling back to JSON V2 battle snapshot payload encoding.");
            return rawBytes.Length > 0;
        }

        private static void ObserveClientBattleSnapshotManifest(int transmissionId, string payloadHash)
        {
            string normalizedPayloadHash = payloadHash ?? string.Empty;
            bool observedSignatureChanged =
                _clientObservedBattleSnapshotTransmissionId != transmissionId ||
                !string.Equals(_clientObservedBattleSnapshotPayloadHash, normalizedPayloadHash, StringComparison.Ordinal);
            _clientObservedBattleSnapshotTransmissionId = transmissionId;
            _clientObservedBattleSnapshotPayloadHash = normalizedPayloadHash;
            if (!observedSignatureChanged)
                return;

            _clientAppliedBattleSnapshotTransmissionId = 0;
            _clientAppliedBattleSnapshotPayloadHash = string.Empty;
        }

        private static void MarkClientBattleSnapshotApplied(int transmissionId, string payloadHash)
        {
            _clientAppliedBattleSnapshotTransmissionId = transmissionId;
            _clientAppliedBattleSnapshotPayloadHash = payloadHash ?? string.Empty;
        }

        private static void ClearClientBattleSnapshotApplicationState(string source)
        {
            _clientObservedBattleSnapshotTransmissionId = 0;
            _clientObservedBattleSnapshotPayloadHash = string.Empty;
            _clientAppliedBattleSnapshotTransmissionId = 0;
            _clientAppliedBattleSnapshotPayloadHash = string.Empty;
            BattleCatalogParityState.ResetClient(source ?? "CoopMissionNetworkBridge.ClearClientBattleSnapshotApplicationState");
            BattleMapSpawnHandoffPatch.ClearDeferredClientMountedHeroCreateAgents(
                (source ?? "CoopMissionNetworkBridge.ClearClientBattleSnapshotApplicationState") + " deferred-mounted-hero-clear");
            BattleSnapshotRuntimeState.Clear(source ?? "CoopMissionNetworkBridge.ClearClientBattleSnapshotApplicationState");
        }

        private static bool IsEligibleRemotePeer(NetworkCommunicator peer)
        {
            return peer != null &&
                !peer.IsServerPeer &&
                peer.IsConnectionActive &&
                peer.IsSynchronized;
        }

        private static bool IsBattleSnapshotBootstrapEligiblePeer(NetworkCommunicator peer)
        {
            return IsBattleSnapshotBootstrapEligiblePeer(peer, allowUnsynchronizedPeer: false);
        }

        private static bool IsBattleSnapshotBootstrapEligiblePeer(NetworkCommunicator peer, bool allowUnsynchronizedPeer)
        {
            return peer != null &&
                !peer.IsServerPeer &&
                peer.IsConnectionActive &&
                (peer.IsSynchronized || allowUnsynchronizedPeer);
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

        private readonly struct EntryStatusTransportFieldState
        {
            private EntryStatusTransportFieldState(
                string allowedTroopIds,
                string allowedEntryIds,
                int authoritativeMaterializedAgentEntryCount,
                string authoritativeMaterializedAgentEntries,
                string attackerAllowedTroopIds,
                string attackerAllowedEntryIds,
                string attackerSelectableEntryIds,
                string defenderAllowedTroopIds,
                string defenderAllowedEntryIds,
                string defenderSelectableEntryIds)
            {
                AllowedTroopIds = allowedTroopIds;
                AllowedEntryIds = allowedEntryIds;
                AuthoritativeMaterializedAgentEntryCount = authoritativeMaterializedAgentEntryCount;
                AuthoritativeMaterializedAgentEntries = authoritativeMaterializedAgentEntries;
                AttackerAllowedTroopIds = attackerAllowedTroopIds;
                AttackerAllowedEntryIds = attackerAllowedEntryIds;
                AttackerSelectableEntryIds = attackerSelectableEntryIds;
                DefenderAllowedTroopIds = defenderAllowedTroopIds;
                DefenderAllowedEntryIds = defenderAllowedEntryIds;
                DefenderSelectableEntryIds = defenderSelectableEntryIds;
            }

            public string AllowedTroopIds { get; }

            public string AllowedEntryIds { get; }

            public int AuthoritativeMaterializedAgentEntryCount { get; }

            public string AuthoritativeMaterializedAgentEntries { get; }

            public string AttackerAllowedTroopIds { get; }

            public string AttackerAllowedEntryIds { get; }

            public string AttackerSelectableEntryIds { get; }

            public string DefenderAllowedTroopIds { get; }

            public string DefenderAllowedEntryIds { get; }

            public string DefenderSelectableEntryIds { get; }

            public static EntryStatusTransportFieldState Capture(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot)
            {
                return snapshot == null
                    ? default(EntryStatusTransportFieldState)
                    : new EntryStatusTransportFieldState(
                        snapshot.AllowedTroopIds,
                        snapshot.AllowedEntryIds,
                        snapshot.AuthoritativeMaterializedAgentEntryCount,
                        snapshot.AuthoritativeMaterializedAgentEntries,
                        snapshot.AttackerAllowedTroopIds,
                        snapshot.AttackerAllowedEntryIds,
                        snapshot.AttackerSelectableEntryIds,
                        snapshot.DefenderAllowedTroopIds,
                        snapshot.DefenderAllowedEntryIds,
                        snapshot.DefenderSelectableEntryIds);
            }

            public void Restore(CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot snapshot)
            {
                if (snapshot == null)
                    return;

                snapshot.AllowedTroopIds = AllowedTroopIds;
                snapshot.AllowedEntryIds = AllowedEntryIds;
                snapshot.AuthoritativeMaterializedAgentEntryCount = AuthoritativeMaterializedAgentEntryCount;
                snapshot.AuthoritativeMaterializedAgentEntries = AuthoritativeMaterializedAgentEntries;
                snapshot.AttackerAllowedTroopIds = AttackerAllowedTroopIds;
                snapshot.AttackerAllowedEntryIds = AttackerAllowedEntryIds;
                snapshot.AttackerSelectableEntryIds = AttackerSelectableEntryIds;
                snapshot.DefenderAllowedTroopIds = DefenderAllowedTroopIds;
                snapshot.DefenderAllowedEntryIds = DefenderAllowedEntryIds;
                snapshot.DefenderSelectableEntryIds = DefenderSelectableEntryIds;
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

        private readonly struct ChunkRange
        {
            public ChunkRange(int startIndex, int endIndex)
            {
                StartIndex = startIndex;
                EndIndex = endIndex;
            }

            public int StartIndex { get; }
            public int EndIndex { get; }
        }

        private sealed class BattleSnapshotTransportState
        {
            private readonly int _windowChunkCount;

            private BattleSnapshotTransportState(
                int peerIndex,
                int transmissionId,
                int logicalBytes,
                string comparisonKey,
                string payloadHash,
                CoopBattleSnapshotCompressionKind compressionKind,
                CoopBattleSnapshotPayloadEncoding payloadEncoding,
                byte[][] chunks,
                int totalBytes,
                int initialWindowChunks,
                int maxInflightChunks)
            {
                PeerIndex = peerIndex;
                TransmissionId = transmissionId;
                LogicalBytes = logicalBytes;
                ComparisonKey = comparisonKey ?? string.Empty;
                PayloadHash = payloadHash ?? string.Empty;
                CompressionKind = compressionKind;
                PayloadEncoding = payloadEncoding;
                Chunks = chunks ?? Array.Empty<byte[]>();
                TotalBytes = totalBytes;
                SentChunkFlags = new bool[Chunks.Length];
                CreatedUtc = DateTime.UtcNow;
                LastProgressUtc = CreatedUtc;
                HighestClientContiguousChunkIndex = -1;
                LastRequestedStartChunkIndex = -1;
                LastRequestedEndChunkIndex = -1;
                NextChunkToSendIndex = -1;
                _windowChunkCount = Math.Max(1, initialWindowChunks);
            }

            public int PeerIndex { get; }
            public int TransmissionId { get; }
            public int LogicalBytes { get; }
            public string ComparisonKey { get; }
            public string PayloadHash { get; }
            public CoopBattleSnapshotCompressionKind CompressionKind { get; }
            public CoopBattleSnapshotPayloadEncoding PayloadEncoding { get; }
            public byte[][] Chunks { get; }
            public int TotalBytes { get; }
            public int ChunkCount => Chunks.Length;
            public bool[] SentChunkFlags { get; }
            public int SentChunkCount { get; private set; }
            public int HighestClientContiguousChunkIndex { get; private set; }
            public int ClientReceivedChunkCount { get; private set; }
            public int LastRequestedStartChunkIndex { get; private set; }
            public int LastRequestedEndChunkIndex { get; private set; }
            public int ActiveWindowStartChunkIndex => LastRequestedStartChunkIndex;
            public int ActiveWindowEndChunkIndex => LastRequestedEndChunkIndex;
            public int NextChunkToSendIndex { get; private set; }
            public DateTime CreatedUtc { get; }
            public DateTime LastManifestSentUtc { get; private set; }
            public DateTime LastChunkSentUtc { get; private set; }
            public DateTime LastProgressUtc { get; private set; }
            public DateTime LastClientRequestUtc { get; private set; }
            public bool ManifestSent { get; private set; }
            public bool CompleteAckReceived { get; private set; }
            public bool AppliedSuccessfully { get; private set; }
            public bool HasObservedClientRequest => LastClientRequestUtc != DateTime.MinValue;
            public bool HasActiveWindow => LastRequestedStartChunkIndex >= 0 &&
                                           LastRequestedEndChunkIndex >= LastRequestedStartChunkIndex;
            public bool IsActiveWindowSatisfiedByClient => HasActiveWindow &&
                                                           HighestClientContiguousChunkIndex >= LastRequestedEndChunkIndex;
            public bool HasPendingChunkRequests => HasActiveWindow && !IsActiveWindowSatisfiedByClient;
            public int PendingRequestedChunkCount => CanSendActiveWindowChunks
                ? Math.Max(0, LastRequestedEndChunkIndex - NextChunkToSendIndex + 1)
                : 0;
            public bool IsCompleted => CompleteAckReceived;
            public bool CanSendActiveWindowChunks => !IsCompleted &&
                                                     ManifestSent &&
                                                     HasActiveWindow &&
                                                     NextChunkToSendIndex >= LastRequestedStartChunkIndex &&
                                                     NextChunkToSendIndex <= LastRequestedEndChunkIndex;

            public static BattleSnapshotTransportState Create(
                int peerIndex,
                byte[] payloadBytes,
                int logicalByteCount,
                string comparisonKey,
                string payloadHash,
                CoopBattleSnapshotCompressionKind compressionKind,
                CoopBattleSnapshotPayloadEncoding payloadEncoding,
                int transmissionId,
                int initialWindowChunks,
                int maxInflightChunks)
            {
                if (payloadBytes == null || payloadBytes.Length <= 0)
                    return null;

                int chunkCount = Math.Max(1, (payloadBytes.Length + CoopBattleSnapshotChunkV2Message.MaxChunkBytes - 1) / CoopBattleSnapshotChunkV2Message.MaxChunkBytes);
                if (chunkCount > CoopBattleSnapshotChunkV2Message.MaxChunkCount)
                {
                    ModLogger.Info(
                        "CoopMissionNetworkBridge: V2 battle snapshot payload too large for chunk transport. " +
                        "PeerIndex=" + peerIndex +
                        " Bytes=" + payloadBytes.Length +
                        " Chunks=" + chunkCount);
                    return null;
                }

                byte[][] chunks = new byte[chunkCount][];
                for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
                {
                    int chunkOffset = chunkIndex * CoopBattleSnapshotChunkV2Message.MaxChunkBytes;
                    int chunkLength = Math.Min(CoopBattleSnapshotChunkV2Message.MaxChunkBytes, payloadBytes.Length - chunkOffset);
                    byte[] chunkBytes = chunkLength > 0 ? new byte[chunkLength] : Array.Empty<byte>();
                    if (chunkLength > 0)
                        Buffer.BlockCopy(payloadBytes, chunkOffset, chunkBytes, 0, chunkLength);
                    chunks[chunkIndex] = chunkBytes;
                }

                return new BattleSnapshotTransportState(
                    peerIndex,
                    transmissionId,
                    logicalByteCount,
                    comparisonKey,
                    payloadHash,
                    compressionKind,
                    payloadEncoding,
                    chunks,
                    payloadBytes.Length,
                    initialWindowChunks,
                    maxInflightChunks);
            }

            public void MarkManifestSent(DateTime nowUtc)
            {
                ManifestSent = true;
                LastManifestSentUtc = nowUtc;
            }

            public bool TryPrimeInitialActiveWindow(DateTime nowUtc)
            {
                if (IsCompleted || HasActiveWindow || HasObservedClientRequest || ChunkCount <= 0)
                    return false;

                int initialStartChunkIndex = 0;
                int initialEndChunkIndex = Math.Min(ChunkCount - 1, initialStartChunkIndex + _windowChunkCount - 1);
                if (initialEndChunkIndex < initialStartChunkIndex)
                    return false;

                SetActiveWindow(initialStartChunkIndex, initialEndChunkIndex, nowUtc);
                return true;
            }

            public void MarkChunkSent(int chunkIndex, DateTime nowUtc)
            {
                if (chunkIndex < 0 || chunkIndex >= ChunkCount)
                    return;

                if (!SentChunkFlags[chunkIndex])
                {
                    SentChunkFlags[chunkIndex] = true;
                    SentChunkCount++;
                }

                LastChunkSentUtc = nowUtc;
                if (chunkIndex == NextChunkToSendIndex)
                    NextChunkToSendIndex++;
            }

            public void ObserveClientChunkRequest(
                int startChunkIndex,
                int endChunkIndex,
                int highestContiguousChunkIndex,
                int receivedChunkCount,
                DateTime nowUtc)
            {
                LastClientRequestUtc = nowUtc;
                ObserveClientProgressAck(highestContiguousChunkIndex, receivedChunkCount, nowUtc);

                int clampedStart = Math.Max(0, startChunkIndex);
                int clampedEnd = Math.Min(ChunkCount - 1, endChunkIndex);
                if (ChunkCount <= 0 || clampedEnd < clampedStart)
                    return;

                if (!HasActiveWindow)
                {
                    SetActiveWindow(clampedStart, clampedEnd, nowUtc);
                    return;
                }

                bool overlapsActiveWindow =
                    clampedStart <= LastRequestedEndChunkIndex &&
                    clampedEnd >= LastRequestedStartChunkIndex;
                if (overlapsActiveWindow)
                {
                    RewindActiveWindowForResend(nowUtc);
                    return;
                }

                if (HighestClientContiguousChunkIndex >= LastRequestedEndChunkIndex)
                    SetActiveWindow(clampedStart, clampedEnd, nowUtc);
            }

            public void ObserveClientProgressAck(
                int highestContiguousChunkIndex,
                int receivedChunkCount,
                DateTime nowUtc)
            {
                HighestClientContiguousChunkIndex = Math.Max(
                    HighestClientContiguousChunkIndex,
                    Math.Min(ChunkCount - 1, highestContiguousChunkIndex));
                ClientReceivedChunkCount = Math.Max(
                    ClientReceivedChunkCount,
                    Math.Min(ChunkCount, Math.Max(0, receivedChunkCount)));
                LastProgressUtc = nowUtc;
            }

            public bool TryAdvanceToNextWindow()
            {
                if (!HasActiveWindow || !IsActiveWindowSatisfiedByClient)
                    return false;

                if (LastRequestedEndChunkIndex >= ChunkCount - 1)
                {
                    NextChunkToSendIndex = -1;
                    return false;
                }

                int nextStartChunkIndex = LastRequestedEndChunkIndex + 1;
                int nextEndChunkIndex = Math.Min(ChunkCount - 1, nextStartChunkIndex + _windowChunkCount - 1);
                SetActiveWindow(nextStartChunkIndex, nextEndChunkIndex, DateTime.UtcNow);
                return true;
            }

            public bool ShouldResendActiveWindow(DateTime nowUtc, TimeSpan stallDelay)
            {
                if (!HasActiveWindow || IsCompleted || IsActiveWindowSatisfiedByClient)
                    return false;

                if (CanSendActiveWindowChunks)
                    return false;

                if (LastChunkSentUtc == DateTime.MinValue)
                    return true;

                return nowUtc - LastChunkSentUtc >= stallDelay &&
                       nowUtc - LastProgressUtc >= stallDelay;
            }

            public void RewindActiveWindowForResend(DateTime nowUtc)
            {
                if (!HasActiveWindow)
                    return;

                NextChunkToSendIndex = Math.Max(LastRequestedStartChunkIndex, HighestClientContiguousChunkIndex + 1);
                if (NextChunkToSendIndex > LastRequestedEndChunkIndex)
                    NextChunkToSendIndex = LastRequestedStartChunkIndex;

                LastClientRequestUtc = nowUtc;
                LastChunkSentUtc = DateTime.MinValue;
            }

            public bool TryGetNextActiveWindowChunkToSend(out int chunkIndex)
            {
                if (!CanSendActiveWindowChunks)
                {
                    chunkIndex = -1;
                    return false;
                }

                chunkIndex = NextChunkToSendIndex;
                return chunkIndex >= LastRequestedStartChunkIndex &&
                       chunkIndex <= LastRequestedEndChunkIndex;
            }

            private void SetActiveWindow(int startChunkIndex, int endChunkIndex, DateTime nowUtc)
            {
                LastRequestedStartChunkIndex = startChunkIndex;
                LastRequestedEndChunkIndex = endChunkIndex;
                NextChunkToSendIndex = Math.Max(startChunkIndex, HighestClientContiguousChunkIndex + 1);
                if (NextChunkToSendIndex > endChunkIndex)
                    NextChunkToSendIndex = startChunkIndex;
                LastProgressUtc = nowUtc;
            }

            public void MarkCompleted(bool appliedSuccessfully, DateTime nowUtc)
            {
                CompleteAckReceived = true;
                AppliedSuccessfully = appliedSuccessfully;
                LastProgressUtc = nowUtc;
            }

            public void ResetForRestart(DateTime nowUtc)
            {
                ManifestSent = false;
                LastManifestSentUtc = DateTime.MinValue;
                LastChunkSentUtc = DateTime.MinValue;
                LastProgressUtc = nowUtc;
                SentChunkCount = 0;
                HighestClientContiguousChunkIndex = -1;
                ClientReceivedChunkCount = 0;
                LastRequestedStartChunkIndex = -1;
                LastRequestedEndChunkIndex = -1;
                NextChunkToSendIndex = -1;
                LastClientRequestUtc = DateTime.MinValue;
                CompleteAckReceived = false;
                AppliedSuccessfully = false;
                Array.Clear(SentChunkFlags, 0, SentChunkFlags.Length);
            }
        }

        private sealed class BattleSnapshotClientAssemblyState
        {
            public BattleSnapshotClientAssemblyState(
                int transmissionId,
                int chunkCount,
                int logicalBytes,
                int wireBytes,
                string comparisonKey,
                string payloadHash,
                CoopBattleSnapshotPayloadEncoding payloadEncoding,
                CoopBattleSnapshotCompressionKind compressionKind)
            {
                TransmissionId = transmissionId;
                ChunkCount = Math.Max(1, chunkCount);
                LogicalBytes = Math.Max(0, logicalBytes);
                WireBytes = Math.Max(0, wireBytes);
                ComparisonKey = comparisonKey ?? string.Empty;
                PayloadHash = payloadHash ?? string.Empty;
                PayloadEncoding = payloadEncoding;
                CompressionKind = compressionKind;
                Chunks = new byte[ChunkCount][];
                ReceivedChunkFlags = new bool[ChunkCount];
                CreatedUtc = DateTime.UtcNow;
                LastManifestObservedUtc = CreatedUtc;
                LastChunkReceivedUtc = CreatedUtc;
                LastUsefulChunkReceivedUtc = CreatedUtc;
                HighestContiguousChunkIndex = -1;
                HighestObservedChunkIndex = -1;
                LastRequestedStartChunkIndex = -1;
                LastRequestedEndChunkIndex = -1;
                LastConfirmedWindowEndChunkIndex = -1;
            }

            public int TransmissionId { get; }
            public int ChunkCount { get; }
            public int LogicalBytes { get; }
            public int WireBytes { get; }
            public string ComparisonKey { get; }
            public string PayloadHash { get; }
            public CoopBattleSnapshotPayloadEncoding PayloadEncoding { get; }
            public CoopBattleSnapshotCompressionKind CompressionKind { get; }
            public byte[][] Chunks { get; }
            public bool[] ReceivedChunkFlags { get; }
            public int ReceivedChunkCount { get; private set; }
            public int HighestContiguousChunkIndex { get; private set; }
            public int HighestObservedChunkIndex { get; private set; }
            public DateTime CreatedUtc { get; }
            public DateTime LastManifestObservedUtc { get; private set; }
            public DateTime LastChunkReceivedUtc { get; private set; }
            public DateTime LastUsefulChunkReceivedUtc { get; private set; }
            public DateTime LastControlMessageSentUtc { get; private set; }
            public int LastRequestedStartChunkIndex { get; private set; }
            public int LastRequestedEndChunkIndex { get; private set; }
            public int LastConfirmedWindowEndChunkIndex { get; private set; }
            public bool IsComplete => ReceivedChunkCount >= ChunkCount;

            public void MarkManifestObserved(DateTime nowUtc)
            {
                LastManifestObservedUtc = nowUtc;
            }

            public void AcceptChunk(int chunkIndex, byte[] payloadBytes, DateTime nowUtc)
            {
                if (chunkIndex < 0 || chunkIndex >= ChunkCount)
                    return;

                LastChunkReceivedUtc = nowUtc;
                if (!ReceivedChunkFlags[chunkIndex])
                {
                    ReceivedChunkFlags[chunkIndex] = true;
                    ReceivedChunkCount++;
                    LastUsefulChunkReceivedUtc = nowUtc;
                }

                Chunks[chunkIndex] = payloadBytes ?? Array.Empty<byte>();
                if (chunkIndex > HighestObservedChunkIndex)
                    HighestObservedChunkIndex = chunkIndex;
                UpdateHighestContiguousChunkIndex();
            }

            public bool TryGetInitialWindowRange(int requestWindowChunks, out int startChunkIndex, out int endChunkIndex)
            {
                startChunkIndex = -1;
                endChunkIndex = -1;
                if (IsComplete || ChunkCount <= 0)
                    return false;

                int clampedWindowSize = Math.Max(1, requestWindowChunks);
                startChunkIndex = 0;
                endChunkIndex = Math.Min(ChunkCount - 1, clampedWindowSize - 1);
                return endChunkIndex >= startChunkIndex;
            }

            public bool TryGetCompletedWindowEndChunkIndex(int requestWindowChunks, out int completedWindowEndChunkIndex)
            {
                completedWindowEndChunkIndex = -1;
                if (IsComplete || ChunkCount <= 0)
                    return false;

                int clampedWindowSize = Math.Max(1, requestWindowChunks);
                int nextWindowStartChunkIndex = LastConfirmedWindowEndChunkIndex + 1;
                if (nextWindowStartChunkIndex >= ChunkCount)
                    return false;

                int nextWindowEndChunkIndex = Math.Min(ChunkCount - 1, nextWindowStartChunkIndex + clampedWindowSize - 1);
                if (HighestContiguousChunkIndex < nextWindowEndChunkIndex)
                    return false;

                completedWindowEndChunkIndex = nextWindowEndChunkIndex;
                return true;
            }

            public void MarkInitialWindowRequestSent(int startChunkIndex, int endChunkIndex, DateTime nowUtc)
            {
                LastRequestedStartChunkIndex = startChunkIndex;
                LastRequestedEndChunkIndex = endChunkIndex;
                LastControlMessageSentUtc = nowUtc;
            }

            public void MarkWindowCompletionAcknowledged(int completedWindowEndChunkIndex, DateTime nowUtc)
            {
                LastConfirmedWindowEndChunkIndex = Math.Max(LastConfirmedWindowEndChunkIndex, completedWindowEndChunkIndex);
                LastControlMessageSentUtc = nowUtc;
            }

            public void MarkProgressAckSent(DateTime nowUtc)
            {
                LastControlMessageSentUtc = nowUtc;
            }

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

            private void UpdateHighestContiguousChunkIndex()
            {
                int index = HighestContiguousChunkIndex + 1;
                while (index < ChunkCount && ReceivedChunkFlags[index])
                    index++;

                HighestContiguousChunkIndex = index - 1;
            }
        }
    }
}
