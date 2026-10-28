using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using CoopSpectator.Network.Messages;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.MissionBehaviors
{
    public sealed partial class CoopCampaignMapPrototypeNetworkController : MissionNetwork
    {
        private const float BridgePollIntervalSeconds = 0.1f;
        private const int CatalogRequestWindowChunks = 4;
        private const int MaxCatalogChunksPerPeerPerTick = 2;
        private static readonly TimeSpan MaximumHostStateAge =
            TimeSpan.FromSeconds(2d);
        private static readonly TimeSpan CatalogManifestRetryDelay =
            TimeSpan.FromSeconds(2d);
        private static readonly TimeSpan CatalogRangeRetryDelay =
            TimeSpan.FromMilliseconds(500d);

        private CoopCampaignMapPrototypeState _serverState;
        private float _nextBridgePollAt;
        private string _lastBridgeSessionId;
        private int _lastBridgeRevision;
        private int _networkRevision;
        private string _lastAvailabilityReason;
        private int _lastBridgeCatalogRevision = -1;
        private int _lastBridgeDynamicRevision = -1;
        private int _lastBridgeConversationRevision = -1;
        private int _networkCatalogRevision;
        private int _networkDynamicRevision;
        private int _networkConversationRevision;
        private List<CoopCampaignMapPrototypeCatalogEntityState> _serverCatalog =
            new List<CoopCampaignMapPrototypeCatalogEntityState>();
        private List<CoopCampaignMapPrototypeDynamicEntityState> _serverDynamic =
            new List<CoopCampaignMapPrototypeDynamicEntityState>();
        private CoopCampaignMapConversationState _serverConversation =
            CoopCampaignMapPrototypeContract.CreateInactiveConversation();
        private readonly Dictionary<int, ServerCatalogTransportState>
            _serverCatalogTransportByPeer =
                new Dictionary<int, ServerCatalogTransportState>();
        private CoopCampaignMapCatalogChunkedPayload
            _preparedCatalogPayload;
        private int _preparedCatalogRevision = -1;
        private int _catalogTransferId;
        private static readonly CoopCampaignMapPrototypeEntitySnapshotAssembler
            ClientEntityAssembler =
                new CoopCampaignMapPrototypeEntitySnapshotAssembler();
        private static readonly CoopCampaignMapConversationPayloadAssembler
            ClientConversationAssembler = new CoopCampaignMapConversationPayloadAssembler();
        private static readonly CoopCampaignMapConversationPoseReceiver ClientPoseReceiver =
            new CoopCampaignMapConversationPoseReceiver();
        private const int MaxConversationChunksPerPeerPerTick = 2;
        private readonly Dictionary<int, ConversationSendState> _conversationSends =
            new Dictionary<int, ConversationSendState>();
        private CoopCampaignMapCatalogChunkedPayload _preparedConversation;
        private int _preparedConversationRevision = -1;

        private sealed class ConversationSendState
        {
            public NetworkCommunicator Peer;
            public int Revision;
            public CoopCampaignMapCatalogChunkedPayload Payload;
            public int NextChunk;
        }

        public static event Action<CoopCampaignMapPrototypeState> ClientStateChanged;

        public static event Action<
            int,
            IReadOnlyList<CoopCampaignMapPrototypeEntityState>>
            ClientVisibleEntitiesChanged;

        public static event Action<
            int,
            IReadOnlyList<CoopCampaignMapPrototypeCatalogEntityState>>
            ClientCatalogChanged;

        public static event Action<
            int,
            IReadOnlyList<CoopCampaignMapPrototypeCatalogEntityState>>
            ClientCatalogDeltaChanged;

        public static event Action<
            int,
            IReadOnlyList<CoopCampaignMapPrototypeDynamicEntityState>>
            ClientDynamicChanged;

        public static event Action<CoopCampaignMapConversationState>
            ClientConversationChanged;
        public static event Action<CoopCampaignMapConversationPose> ClientConversationPoseChanged;

        public static CoopCampaignMapPrototypeState CurrentClientState { get; private set; }

        private static readonly System.Diagnostics.Stopwatch StateReceiptClock = System.Diagnostics.Stopwatch.StartNew();
        private static double _clientStateReceivedAt = -1;

        internal static bool TryGetMovementTargetState(out CoopCampaignMapPrototypeState state, out double ageSeconds)
        {
            state = CurrentClientState;
            ageSeconds = StateReceiptClock.Elapsed.TotalSeconds - _clientStateReceivedAt;
            return state != null && _clientStateReceivedAt >= 0 &&
                CoopCampaignMapMovementTargetCodec.IsFreshAndReady(ageSeconds, state.CatalogRevision, state.DynamicRevision,
                    _clientCatalogCompletedRevision, _clientDynamicCompletedRevision);
        }

        public static IReadOnlyList<CoopCampaignMapPrototypeEntityState>
            CurrentClientVisibleEntities { get; private set; } =
                new List<CoopCampaignMapPrototypeEntityState>();

        public static int CurrentClientVisibleEntitiesRevision { get; private set; } = -1;

        public static IReadOnlyList<CoopCampaignMapPrototypeCatalogEntityState>
            CurrentClientCatalog { get; private set; } =
                new List<CoopCampaignMapPrototypeCatalogEntityState>();

        public static IReadOnlyList<CoopCampaignMapPrototypeDynamicEntityState>
            CurrentClientDynamic { get; private set; } =
                new List<CoopCampaignMapPrototypeDynamicEntityState>();

        public static CoopCampaignMapConversationState CurrentClientConversation
        {
            get;
            private set;
        }

        private static readonly Dictionary<int, CoopCampaignMapPrototypeCatalogEntityState>
            ClientCatalogByIndex =
                new Dictionary<int, CoopCampaignMapPrototypeCatalogEntityState>();
        private static readonly Dictionary<
            string,
            CoopCampaignMapPrototypeCatalogEntityState> ClientCatalogById =
                new Dictionary<
                    string,
                    CoopCampaignMapPrototypeCatalogEntityState>(
                        StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, CoopCampaignMapPrototypeDynamicEntityState>
            ClientDynamicByIndex =
                new Dictionary<int, CoopCampaignMapPrototypeDynamicEntityState>();
        private static readonly Dictionary<
            string,
            CoopCampaignMapPrototypeDynamicEntityState> ClientDynamicById =
                new Dictionary<
                    string,
                    CoopCampaignMapPrototypeDynamicEntityState>(
                        StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, int>
            ClientDynamicListIndexById =
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static int _clientCatalogActiveRevision = -1;
        private static int _clientCatalogExpectedCount;
        private static int _clientCatalogCompletedRevision = -1;
        private static int _clientDynamicActiveRevision = -1;
        private static int _clientDynamicExpectedCount;
        private static int _clientDynamicCompletedRevision = -1;
        private static int _clientMergedRevision;
        private static ClientCatalogTransportState _clientCatalogTransport;
        private static int _clientCompletedCatalogTransferId;
        private static int _clientCompletedCatalogTransferRevision = -1;
        private static string _clientCompletedCatalogTransferHash =
            string.Empty;

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            InitializeTooltips();
            if (GameNetwork.IsClient)
            {
                CurrentClientState = null;
                ResetClientVisibleEntities();
                ResetClientConversation();
            }
            if (!GameNetwork.IsServer)
                return;

            _serverState = null;
            _nextBridgePollAt = 0f;
            _lastBridgeSessionId = null;
            _lastBridgeRevision = 0;
            _networkRevision = 0;
            _lastBridgeCatalogRevision = -1;
            _lastBridgeDynamicRevision = -1;
            _lastBridgeConversationRevision = -1;
            _networkCatalogRevision = 0;
            _networkDynamicRevision = 0;
            _networkConversationRevision = 0;
            _serverCatalog.Clear();
            _serverDynamic.Clear();
            _serverConversation =
                CoopCampaignMapPrototypeContract.CreateInactiveConversation();
            _serverCatalogTransportByPeer.Clear();
            _conversationSends.Clear();
            _preparedConversation = null;
            _preparedConversationRevision = -1;
            _preparedCatalogPayload = null;
            _preparedCatalogRevision = -1;
            _catalogTransferId = 0;
            _lastAvailabilityReason = null;
            ModLogger.Info(
                "CoopCampaignMapPrototypeNetworkController: awaiting authoritative host map bridge state.");
        }

        protected override void AddRemoveMessageHandlers(
            GameNetwork.NetworkMessageHandlerRegistererContainer registerer)
        {
            if (GameNetwork.IsClient)
            {
                registerer.RegisterBaseHandler<CoopCampaignMapPrototypeStateMessage>(
                    HandleServerState);
                registerer.RegisterBaseHandler<CoopCampaignMapPrototypeEntitySnapshotMessage>(
                    HandleServerEntitySnapshot);
                registerer.RegisterBaseHandler<CoopCampaignMapPrototypeEntityStateMessage>(
                    HandleServerEntityState);
                registerer.RegisterBaseHandler<CoopCampaignMapCatalogSnapshotMessage>(
                    HandleServerCatalogSnapshot);
                registerer.RegisterBaseHandler<CoopCampaignMapCatalogEntityMessage>(
                    HandleServerCatalogEntity);
                registerer.RegisterBaseHandler<CoopCampaignMapCatalogManifestMessage>(
                    HandleServerCatalogManifest);
                registerer.RegisterBaseHandler<CoopCampaignMapCatalogChunkMessage>(
                    HandleServerCatalogChunk);
                registerer.RegisterBaseHandler<CoopCampaignMapDynamicSnapshotMessage>(
                    HandleServerDynamicSnapshot);
                registerer.RegisterBaseHandler<CoopCampaignMapDynamicBatchMessage>(
                    HandleServerDynamicBatch);
                registerer.RegisterBaseHandler<CoopCampaignMapConversationChunkMessage>(
                    HandleServerConversationChunk);
                registerer.RegisterBaseHandler<CoopCampaignMapConversationPoseMessage>(HandleServerConversationPose);
                registerer.RegisterBaseHandler<CoopCampaignMapPartyPoseMessage>(HandleServerPartyPose);
                registerer.RegisterBaseHandler<CoopCampaignMapTooltipControlMessage>(HandleTooltipControl);
                registerer.RegisterBaseHandler<CoopCampaignMapTooltipChunkMessage>(HandleTooltipChunk);
                registerer.RegisterBaseHandler<CoopCampaignMapMenuChunkMessage>(HandleMenuChunk);
                registerer.RegisterBaseHandler<CoopCampaignMapBattleMarkerChunkMessage>(HandleBattleMarkersChunk);
            }
            else if (GameNetwork.IsServer)
            {
                registerer.RegisterBaseHandler<CoopCampaignMapTooltipRequestMessage>(HandleTooltipRequest);
                registerer.RegisterBaseHandler<CoopCampaignMapCatalogRangeAckMessage>(
                    HandleClientCatalogRangeAck);
                registerer.RegisterBaseHandler<CoopCampaignMapCatalogCompleteAckMessage>(
                    HandleClientCatalogCompleteAck);
            }
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            TickTooltips();
            if (GameNetwork.IsClient)
            {
                TickClientCatalogTransport();
                TickClientPortraitResources();
                ReconcileClientPartyPoses();
                return;
            }
            if (!GameNetwork.IsServer)
                return;
            TickServerCatalogTransports();
            TickServerConversationTransports();
            TickMenuTransports();
            TickBattleMarkersTransports();
            if (Mission.CurrentTime < _nextBridgePollAt)
                return;

            _nextBridgePollAt = Mission.CurrentTime + BridgePollIntervalSeconds;
            if (!CoopCampaignMapPrototypeBridgeFile.TryReadFresh(
                    DateTime.UtcNow,
                    MaximumHostStateAge,
                    out CoopCampaignMapPrototypeHostSnapshot snapshot,
                    out string reason))
            {
                LogAvailabilityTransition("unavailable:" + (reason ?? "unknown"));
                UpdateServerMenu(null);
                UpdateServerBattleMarkers(null);
                if (_serverConversation.IsActive && _networkConversationRevision < int.MaxValue)
                {
                    _serverConversation = CoopCampaignMapPrototypeContract.CreateInactiveConversation(++_networkConversationRevision);
                    _lastBridgeConversationRevision = -1;
                    BroadcastConversation(_serverConversation);
                }
                return;
            }

            bool sameSession = string.Equals(
                _lastBridgeSessionId,
                snapshot.SessionId,
                StringComparison.Ordinal);
            if (sameSession && snapshot.Revision <= _lastBridgeRevision)
                return;

            bool catalogChanged =
                !sameSession ||
                snapshot.CatalogRevision != _lastBridgeCatalogRevision;
            bool dynamicChanged =
                !sameSession ||
                snapshot.DynamicRevision != _lastBridgeDynamicRevision;
            bool conversationChanged =
                !sameSession ||
                snapshot.Conversation.Revision !=
                    _lastBridgeConversationRevision;
            CoopCampaignMapPrototypeCatalogSnapshot catalogSnapshot = null;
            CoopCampaignMapPrototypeDynamicSnapshot dynamicSnapshot = null;
            if (catalogChanged && snapshot.CatalogRevision > 0)
            {
                if (!CoopCampaignMapPrototypeBridgeFile.TryReadFreshCatalog(
                        DateTime.UtcNow,
                        TimeSpan.Zero,
                        out catalogSnapshot,
                        out reason) ||
                    !string.Equals(
                        catalogSnapshot.SessionId,
                        snapshot.SessionId,
                        StringComparison.Ordinal) ||
                    catalogSnapshot.Revision != snapshot.CatalogRevision)
                {
                    LogAvailabilityTransition(
                        "catalog-unavailable:" + (reason ?? "revision-or-session"));
                    return;
                }
            }
            if (dynamicChanged && snapshot.DynamicRevision > 0)
            {
                if (!CoopCampaignMapPrototypeBridgeFile.TryReadFreshDynamic(
                        DateTime.UtcNow,
                        MaximumHostStateAge,
                        out dynamicSnapshot,
                        out reason) ||
                    !string.Equals(
                        dynamicSnapshot.SessionId,
                        snapshot.SessionId,
                        StringComparison.Ordinal) ||
                    dynamicSnapshot.Revision != snapshot.DynamicRevision)
                {
                    LogAvailabilityTransition(
                        "dynamic-unavailable:" + (reason ?? "revision-or-session"));
                    return;
                }
            }

            if (!sameSession)
            {
                _lastBridgeCatalogRevision = -1;
                _lastBridgeDynamicRevision = -1;
                _lastBridgeConversationRevision = -1;
                _serverCatalog.Clear();
                _serverDynamic.Clear();
                _serverCatalogTransportByPeer.Clear();
                _conversationSends.Clear();
                _preparedConversation = null;
                _preparedConversationRevision = -1;
                _preparedCatalogPayload = null;
                _preparedCatalogRevision = -1;
                _catalogTransferId = 0;
            }

            _lastBridgeSessionId = snapshot.SessionId;
            _lastBridgeRevision = snapshot.Revision;
            if (_networkRevision < int.MaxValue)
                _networkRevision++;
            _serverState = CoopCampaignMapPrototypeContract.ToNetworkState(
                snapshot,
                _networkRevision);
            if (_serverState == null)
                return;

            _tooltipHostSeenAt = TooltipNow;
            UpdateServerMenu(snapshot.Menu, !sameSession, snapshot.MapBar);
            UpdateServerBattleMarkers(snapshot.BattleMarkers, !sameSession);

            List<CoopCampaignMapPrototypeCatalogEntityState> catalogDelta = null;
            int previousNetworkCatalogRevision = _networkCatalogRevision;
            if (catalogChanged)
            {
                _lastBridgeCatalogRevision = snapshot.CatalogRevision;
                List<CoopCampaignMapPrototypeCatalogEntityState> incomingCatalog =
                    CloneCatalog(catalogSnapshot?.Entities);
                if (!sameSession)
                {
                    if (_networkCatalogRevision < int.MaxValue)
                        _networkCatalogRevision++;
                    _serverCatalog = incomingCatalog;
                    PrepareCatalogTransport(
                        _networkCatalogRevision,
                        _serverCatalog);
                }
                else
                {
                    catalogDelta =
                        CoopCampaignMapPrototypeCatalogDeltaPolicy.BuildDelta(
                            _serverCatalog,
                            incomingCatalog);
                    _serverCatalog = incomingCatalog;
                    if (catalogDelta.Count > 0)
                    {
                        if (_networkCatalogRevision < int.MaxValue)
                            _networkCatalogRevision++;
                        _preparedCatalogPayload = null;
                        _preparedCatalogRevision = -1;
                    }
                }
            }
            List<CoopCampaignMapPrototypeDynamicEntityState> dynamicDelta = null;
            if (dynamicChanged)
            {
                _lastBridgeDynamicRevision = snapshot.DynamicRevision;
                List<CoopCampaignMapPrototypeDynamicEntityState> incomingDynamic =
                    CloneDynamic(dynamicSnapshot?.Entities);
                dynamicDelta =
                    CoopCampaignMapPrototypeDynamicDeltaPolicy.BuildDelta(
                        _serverDynamic,
                        incomingDynamic);
                _serverDynamic = incomingDynamic;
                if (dynamicDelta.Count > 0 &&
                    _networkDynamicRevision < int.MaxValue)
                {
                    _networkDynamicRevision++;
                }
            }
            if (conversationChanged)
            {
                _lastBridgeConversationRevision =
                    snapshot.Conversation.Revision;
                if (_networkConversationRevision < int.MaxValue)
                    _networkConversationRevision++;
                _serverConversation = snapshot.Conversation.Clone();
                _serverConversation.Revision = _networkConversationRevision;
            }
            _serverState.VisibleEntitiesRevision = _networkDynamicRevision;
            _serverState.CatalogRevision = _networkCatalogRevision;
            _serverState.DynamicRevision = _networkDynamicRevision;
            if (!CoopCampaignMapMovementTargetCodec.TryDecode(_serverState.MovementTarget, out var movementTarget) ||
                _serverConversation.IsActive || !CoopCampaignMapMovementTargetCodec.IsAdmitted(movementTarget,
                    movementTarget?.EntityId == null ? null : _serverCatalog.Find(x => x.EntityId == movementTarget.EntityId),
                    movementTarget?.EntityId == null ? null : _serverDynamic.Find(x => x.EntityId == movementTarget.EntityId)))
                _serverState.MovementTarget = string.Empty;

            LogAvailabilityTransition(
                "authoritative:session=" + snapshot.SessionId);
            BroadcastState(_serverState);

            if (catalogChanged && !sameSession)
                QueueCatalogTransportForAllPeers();
            else if (catalogDelta != null && catalogDelta.Count > 0)
                SendCatalogDeltaToReadyPeers(
                    previousNetworkCatalogRevision,
                    _networkCatalogRevision,
                    catalogDelta);
            if (dynamicDelta != null && dynamicDelta.Count > 0)
                BroadcastDynamic(_networkDynamicRevision, dynamicDelta);
            if (conversationChanged)
                BroadcastConversation(_serverConversation);
            BroadcastPartyPoses(snapshot);
            // Motion never supersedes or restarts the reliable text/descriptor
            // transfer. One bounded latest sample per accepted host publication.
            if (snapshot.ConversationPose != null && _serverConversation.IsActive &&
                CoopCampaignMapConversationPoseCodec.Matches(snapshot.ConversationPose, snapshot.Conversation))
            {
                var pose = snapshot.ConversationPose.Clone();
                pose.ConversationRevision = _serverConversation.Revision;
                pose.Sequence = _networkRevision;
                BroadcastConversationPose(pose);
            }
        }

        protected override void HandleNewClientAfterSynchronized(
            NetworkCommunicator networkPeer)
        {
            base.HandleNewClientAfterSynchronized(networkPeer);
            if (GameNetwork.IsServer &&
                networkPeer != null &&
                !networkPeer.IsServerPeer &&
                _serverState != null)
            {
                SendState(networkPeer, _serverState);
                QueueCatalogTransport(networkPeer);
                SendDynamic(
                    networkPeer,
                    _networkDynamicRevision,
                    _serverDynamic);
                SendConversation(networkPeer, _serverConversation);
                SendMenu(networkPeer);
                SendBattleMarkers(networkPeer);
            }
        }

        public override void OnMissionStateFinalized()
        {
            FinalizeTooltips();
            ResetServerMenu();
            ResetServerBattleMarkers();
            if (GameNetwork.IsClient)
            {
                CurrentClientState = null;
                ResetClientVisibleEntities();
                ResetClientConversation();
            }
            _serverState = null;
            _lastBridgeSessionId = null;
            _lastBridgeRevision = 0;
            _networkRevision = 0;
            _lastBridgeCatalogRevision = -1;
            _lastBridgeDynamicRevision = -1;
            _lastBridgeConversationRevision = -1;
            _networkCatalogRevision = 0;
            _networkDynamicRevision = 0;
            _networkConversationRevision = 0;
            _serverCatalog.Clear();
            _serverDynamic.Clear();
            _serverConversation =
                CoopCampaignMapPrototypeContract.CreateInactiveConversation();
            _serverCatalogTransportByPeer.Clear();
            _conversationSends.Clear();
            _preparedConversation = null;
            _preparedConversationRevision = -1;
            _preparedCatalogPayload = null;
            _preparedCatalogRevision = -1;
            _catalogTransferId = 0;
            _lastAvailabilityReason = null;
            base.OnMissionStateFinalized();
        }

        protected override void HandlePlayerDisconnect(
            NetworkCommunicator networkPeer)
        {
            base.HandlePlayerDisconnect(networkPeer);
            if (networkPeer != null)
            {
                RemoveTooltipPeer(networkPeer);
                _serverCatalogTransportByPeer.Remove(networkPeer.Index);
                _conversationSends.Remove(networkPeer.Index);
                _menuSends.Remove(networkPeer.Index);
            }
        }

        private void LogAvailabilityTransition(string reason)
        {
            if (string.Equals(
                    _lastAvailabilityReason,
                    reason,
                    StringComparison.Ordinal))
            {
                return;
            }

            _lastAvailabilityReason = reason;
            ModLogger.Info(
                "CoopCampaignMapPrototypeNetworkController: host state " +
                (reason ?? "unknown") + ".");
        }

        private static void HandleServerState(GameNetworkMessage baseMessage)
        {
            CoopCampaignMapPrototypeStateMessage message =
                baseMessage as CoopCampaignMapPrototypeStateMessage;
            if (message == null ||
                message.ProtocolVersion !=
                CoopCampaignMapPrototypeContract.ProtocolVersion)
            {
                return;
            }

            CoopCampaignMapPrototypeState state = message.ToState();
            if (!CoopCampaignMapPrototypeContract.CanAccept(
                    CurrentClientState,
                    state))
            {
                return;
            }

            CurrentClientState = state;
            _clientStateReceivedAt = StateReceiptClock.Elapsed.TotalSeconds;
            _tooltipClientStateAt = TooltipNow;
            try
            {
                ClientStateChanged?.Invoke(state.Clone());
            }
            catch (Exception ex)
            {
                ModLogger.Error(
                    "CoopCampaignMapPrototypeNetworkController: client state dispatch failed.",
                    ex);
            }
        }

        private static void HandleServerEntitySnapshot(
            GameNetworkMessage baseMessage)
        {
            CoopCampaignMapPrototypeEntitySnapshotMessage message =
                baseMessage as CoopCampaignMapPrototypeEntitySnapshotMessage;
            if (message == null ||
                message.ProtocolVersion !=
                CoopCampaignMapPrototypeContract.ProtocolVersion)
            {
                return;
            }

            if (ClientEntityAssembler.TryBegin(
                    message.Revision,
                    message.EntityCount,
                    out List<CoopCampaignMapPrototypeEntityState> completed) &&
                completed != null)
            {
                ApplyCompletedVisibleEntities(message.Revision, completed);
            }
        }

        private static void HandleServerEntityState(
            GameNetworkMessage baseMessage)
        {
            CoopCampaignMapPrototypeEntityStateMessage message =
                baseMessage as CoopCampaignMapPrototypeEntityStateMessage;
            if (message == null ||
                message.ProtocolVersion !=
                CoopCampaignMapPrototypeContract.ProtocolVersion)
            {
                return;
            }

            if (ClientEntityAssembler.TryAdd(
                    message.Revision,
                    message.Index,
                    message.ExpectedCount,
                    message.Entity,
                    out List<CoopCampaignMapPrototypeEntityState> completed) &&
                completed != null)
            {
                ApplyCompletedVisibleEntities(message.Revision, completed);
            }
        }

        private static void HandleServerCatalogSnapshot(
            GameNetworkMessage baseMessage)
        {
            CoopCampaignMapCatalogSnapshotMessage message =
                baseMessage as CoopCampaignMapCatalogSnapshotMessage;
            if (message == null ||
                message.ProtocolVersion !=
                    CoopCampaignMapPrototypeContract.ProtocolVersion ||
                message.Revision <= _clientCatalogCompletedRevision)
            {
                return;
            }

            _clientCatalogActiveRevision = message.Revision;
            _clientCatalogExpectedCount = message.EntityCount;
            ClientCatalogByIndex.Clear();
            if (message.EntityCount == 0)
                ApplyCompletedCatalogDelta(message.Revision,
                    new List<CoopCampaignMapPrototypeCatalogEntityState>());
        }

        private static void HandleServerCatalogEntity(
            GameNetworkMessage baseMessage)
        {
            CoopCampaignMapCatalogEntityMessage message =
                baseMessage as CoopCampaignMapCatalogEntityMessage;
            if (message == null ||
                message.ProtocolVersion !=
                    CoopCampaignMapPrototypeContract.ProtocolVersion ||
                message.Revision != _clientCatalogActiveRevision ||
                message.ExpectedCount != _clientCatalogExpectedCount ||
                message.Index < 0 ||
                message.Index >= _clientCatalogExpectedCount ||
                ClientCatalogByIndex.ContainsKey(message.Index) ||
                !CoopCampaignMapPrototypeContract.IsValidCatalogEntity(
                    message.Entity))
            {
                return;
            }

            ClientCatalogByIndex[message.Index] = message.Entity.Clone();
            if (ClientCatalogByIndex.Count != _clientCatalogExpectedCount)
                return;

            var completed =
                new List<CoopCampaignMapPrototypeCatalogEntityState>(
                    _clientCatalogExpectedCount);
            for (int index = 0; index < _clientCatalogExpectedCount; index++)
            {
                if (!ClientCatalogByIndex.TryGetValue(
                        index,
                        out CoopCampaignMapPrototypeCatalogEntityState entity))
                {
                    return;
                }
                completed.Add(entity.Clone());
            }
            ApplyCompletedCatalogDelta(message.Revision, completed);
        }

        private static void HandleServerCatalogManifest(
            GameNetworkMessage baseMessage)
        {
            CoopCampaignMapCatalogManifestMessage message =
                baseMessage as CoopCampaignMapCatalogManifestMessage;
            if (message == null ||
                message.ProtocolVersion !=
                    CoopCampaignMapPrototypeContract.ProtocolVersion ||
                message.SchemaVersion !=
                    CoopCampaignMapCatalogBinarySerializer.SchemaVersion)
            {
                return;
            }

            if (message.Revision <= _clientCatalogCompletedRevision)
            {
                if (message.TransferId ==
                        _clientCompletedCatalogTransferId &&
                    message.Revision ==
                        _clientCompletedCatalogTransferRevision &&
                    string.Equals(
                        message.PayloadHash,
                        _clientCompletedCatalogTransferHash,
                        StringComparison.OrdinalIgnoreCase))
                {
                    SendClientCatalogCompleteAck(
                        message.TransferId,
                        message.Revision,
                        message.PayloadHash,
                        true);
                }
                return;
            }

            if (_clientCatalogTransport != null &&
                _clientCatalogTransport.Accumulator.TransferId ==
                    message.TransferId &&
                _clientCatalogTransport.Accumulator.Revision ==
                    message.Revision &&
                string.Equals(
                    _clientCatalogTransport.Accumulator.PayloadHash,
                    message.PayloadHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                SendClientCatalogRangeAck(_clientCatalogTransport);
                return;
            }

            if (!CoopCampaignMapCatalogChunkAccumulator.TryCreate(
                    message.TransferId,
                    message.Revision,
                    message.LogicalByteCount,
                    message.WireByteCount,
                    message.ChunkCount,
                    message.CompressionKind,
                    message.PayloadHash,
                    out CoopCampaignMapCatalogChunkAccumulator accumulator,
                    out _))
            {
                return;
            }

            _clientCatalogTransport =
                new ClientCatalogTransportState(accumulator);
            SendClientCatalogRangeAck(_clientCatalogTransport);
        }

        private static void HandleServerCatalogChunk(
            GameNetworkMessage baseMessage)
        {
            CoopCampaignMapCatalogChunkMessage message =
                baseMessage as CoopCampaignMapCatalogChunkMessage;
            ClientCatalogTransportState state = _clientCatalogTransport;
            if (message == null ||
                state == null ||
                message.ProtocolVersion !=
                    CoopCampaignMapPrototypeContract.ProtocolVersion ||
                message.TransferId != state.Accumulator.TransferId)
            {
                return;
            }

            int receivedBefore = state.Accumulator.ReceivedChunkCount;
            if (!state.Accumulator.TryAccept(
                    message.ChunkIndex,
                    message.ChunkCount,
                    message.PayloadBytes,
                    out _))
            {
                return;
            }
            if (state.Accumulator.ReceivedChunkCount > receivedBefore)
                state.LastUsefulChunkUtc = DateTime.UtcNow;

            if (state.Accumulator.IsComplete)
            {
                CompleteClientCatalogTransport(state);
                return;
            }

            if (state.Accumulator.HighestContiguousChunkIndex <
                state.RequestedEndChunkIndex)
            {
                return;
            }

            state.AdvanceRequestedWindow();
            SendClientCatalogRangeAck(state);
        }

        private bool HandleClientCatalogRangeAck(
            NetworkCommunicator peer,
            GameNetworkMessage baseMessage)
        {
            CoopCampaignMapCatalogRangeAckMessage message =
                baseMessage as CoopCampaignMapCatalogRangeAckMessage;
            if (peer == null ||
                message == null ||
                message.ProtocolVersion !=
                    CoopCampaignMapPrototypeContract.ProtocolVersion ||
                !_serverCatalogTransportByPeer.TryGetValue(
                    peer.Index,
                    out ServerCatalogTransportState state) ||
                message.TransferId != state.TransferId ||
                message.RequestedStartChunkIndex < 0 ||
                message.RequestedEndChunkIndex <
                    message.RequestedStartChunkIndex ||
                message.RequestedEndChunkIndex >= state.Payload.ChunkCount ||
                message.RequestedEndChunkIndex -
                    message.RequestedStartChunkIndex + 1 >
                    CatalogRequestWindowChunks ||
                message.HighestContiguousChunkIndex < -1 ||
                message.HighestContiguousChunkIndex >=
                    state.Payload.ChunkCount ||
                message.ReceivedChunkCount < 0 ||
                message.ReceivedChunkCount > state.Payload.ChunkCount)
            {
                return false;
            }

            state.RequestRange(
                message.RequestedStartChunkIndex,
                message.RequestedEndChunkIndex,
                message.HighestContiguousChunkIndex,
                message.ReceivedChunkCount);
            return true;
        }

        private bool HandleClientCatalogCompleteAck(
            NetworkCommunicator peer,
            GameNetworkMessage baseMessage)
        {
            CoopCampaignMapCatalogCompleteAckMessage message =
                baseMessage as CoopCampaignMapCatalogCompleteAckMessage;
            if (peer == null ||
                message == null ||
                message.ProtocolVersion !=
                    CoopCampaignMapPrototypeContract.ProtocolVersion ||
                !_serverCatalogTransportByPeer.TryGetValue(
                    peer.Index,
                    out ServerCatalogTransportState state) ||
                message.TransferId != state.TransferId ||
                message.Revision != state.Revision ||
                !string.Equals(
                    message.PayloadHash,
                    state.Payload.PayloadHash,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (message.AppliedSuccessfully)
            {
                state.MarkCompleted();
                if (state.AppliedRevision < _networkCatalogRevision)
                    QueueCatalogTransport(peer);
            }
            else
            {
                state.ResetForRetry();
            }
            return true;
        }

        private static void HandleServerDynamicSnapshot(
            GameNetworkMessage baseMessage)
        {
            CoopCampaignMapDynamicSnapshotMessage message =
                baseMessage as CoopCampaignMapDynamicSnapshotMessage;
            if (message == null ||
                message.ProtocolVersion !=
                    CoopCampaignMapPrototypeContract.ProtocolVersion ||
                message.Revision <= _clientDynamicCompletedRevision)
            {
                return;
            }

            _clientDynamicActiveRevision = message.Revision;
            _clientDynamicExpectedCount = message.EntityCount;
            ClientDynamicByIndex.Clear();
            if (message.EntityCount == 0)
                ApplyCompletedDynamic(message.Revision,
                    new List<CoopCampaignMapPrototypeDynamicEntityState>());
        }

        private static void HandleServerDynamicBatch(
            GameNetworkMessage baseMessage)
        {
            CoopCampaignMapDynamicBatchMessage message =
                baseMessage as CoopCampaignMapDynamicBatchMessage;
            if (message == null ||
                message.ProtocolVersion !=
                    CoopCampaignMapPrototypeContract.ProtocolVersion ||
                message.Revision != _clientDynamicActiveRevision ||
                message.ExpectedCount != _clientDynamicExpectedCount ||
                message.Entities == null ||
                message.StartIndex < 0 ||
                message.StartIndex + message.Entities.Count >
                    _clientDynamicExpectedCount)
            {
                return;
            }

            for (int offset = 0; offset < message.Entities.Count; offset++)
            {
                int index = message.StartIndex + offset;
                CoopCampaignMapPrototypeDynamicEntityState entity =
                    message.Entities[offset];
                if (ClientDynamicByIndex.ContainsKey(index) ||
                    !CoopCampaignMapPrototypeContract.IsValidDynamicEntity(entity))
                {
                    return;
                }
                ClientDynamicByIndex[index] = entity.Clone();
            }
            if (ClientDynamicByIndex.Count != _clientDynamicExpectedCount)
                return;

            var completed =
                new List<CoopCampaignMapPrototypeDynamicEntityState>(
                    _clientDynamicExpectedCount);
            for (int index = 0; index < _clientDynamicExpectedCount; index++)
            {
                if (!ClientDynamicByIndex.TryGetValue(
                        index,
                        out CoopCampaignMapPrototypeDynamicEntityState entity))
                {
                    return;
                }
                completed.Add(entity.Clone());
            }
            ApplyCompletedDynamic(message.Revision, completed);
        }

        private static void HandleServerConversationChunk(GameNetworkMessage baseMessage)
        {
            var message = baseMessage as CoopCampaignMapConversationChunkMessage;
            if (message == null || message.ProtocolVersion != CoopCampaignMapPrototypeContract.ProtocolVersion)
                return;
            if (ClientConversationAssembler.TryAccept(message.Revision, message.LogicalBytes,
                message.WireBytes, message.ChunkCount, message.Compression, message.PayloadHash,
                message.ChunkIndex, message.PayloadBytes, out var completed) && completed != null)
                ApplyCompletedConversation(completed);
        }

        private static void HandleServerConversationPose(GameNetworkMessage baseMessage)
        {
            var message = baseMessage as CoopCampaignMapConversationPoseMessage;
            if (message == null || message.ProtocolVersion != CoopCampaignMapPrototypeContract.ProtocolVersion ||
                !ClientPoseReceiver.TryAccept(message.Pose, out var pose)) return;
            ClientConversationPoseChanged?.Invoke(pose);
        }

        private void BroadcastConversationPose(CoopCampaignMapConversationPose pose)
        {
            if (GameNetwork.NetworkPeers == null) return;
            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer || !peer.IsConnectionActive || !peer.IsSynchronized ||
                    _conversationSends.ContainsKey(peer.Index)) continue;
                // Text and close have priority. If not ready, skip this motion
                // sample; the next host publication replaces it without a queue.
                GameNetwork.BeginModuleEventAsServer(peer);
                GameNetwork.WriteMessage(new CoopCampaignMapConversationPoseMessage(pose));
                GameNetwork.EndModuleEventAsServer();
            }
        }

        private static void TickClientCatalogTransport()
        {
            ClientCatalogTransportState state = _clientCatalogTransport;
            if (state == null || state.Accumulator.IsComplete)
                return;
            DateTime nowUtc = DateTime.UtcNow;
            if (state.LastRangeRequestUtc != DateTime.MinValue &&
                nowUtc - state.LastRangeRequestUtc < CatalogRangeRetryDelay)
            {
                return;
            }
            SendClientCatalogRangeAck(state);
        }

        private static void CompleteClientCatalogTransport(
            ClientCatalogTransportState state)
        {
            bool applied = false;
            if (state != null &&
                state.Accumulator.TryComplete(
                    out byte[] logicalBytes,
                    out _) &&
                CoopCampaignMapCatalogBinarySerializer.TryDeserialize(
                    logicalBytes,
                    out int decodedRevision,
                    out List<CoopCampaignMapPrototypeCatalogEntityState>
                        decodedEntities,
                    out _) &&
                decodedRevision == state.Accumulator.Revision &&
                decodedRevision > _clientCatalogCompletedRevision)
            {
                ApplyCompletedCatalog(decodedRevision, decodedEntities);
                _clientCompletedCatalogTransferId =
                    state.Accumulator.TransferId;
                _clientCompletedCatalogTransferRevision = decodedRevision;
                _clientCompletedCatalogTransferHash =
                    state.Accumulator.PayloadHash;
                applied = true;
            }

            SendClientCatalogCompleteAck(state, applied);
            _clientCatalogTransport = null;
        }

        private static void SendClientCatalogRangeAck(
            ClientCatalogTransportState state)
        {
            if (state == null ||
                state.RequestedStartChunkIndex < 0 ||
                state.RequestedEndChunkIndex <
                    state.RequestedStartChunkIndex ||
                !GameNetwork.IsClient ||
                !GameNetwork.IsSessionActive)
            {
                return;
            }

            try
            {
                GameNetwork.BeginModuleEventAsClient();
                GameNetwork.WriteMessage(
                    new CoopCampaignMapCatalogRangeAckMessage(
                        state.Accumulator.TransferId,
                        state.RequestedStartChunkIndex,
                        state.RequestedEndChunkIndex,
                        state.Accumulator.HighestContiguousChunkIndex,
                        state.Accumulator.ReceivedChunkCount));
                GameNetwork.EndModuleEventAsClient();
                state.LastRangeRequestUtc = DateTime.UtcNow;
            }
            catch
            {
                // The bounded retry timer will attempt the same range again.
            }
        }

        private static void SendClientCatalogCompleteAck(
            ClientCatalogTransportState state,
            bool appliedSuccessfully)
        {
            if (state == null)
                return;
            SendClientCatalogCompleteAck(
                state.Accumulator.TransferId,
                state.Accumulator.Revision,
                state.Accumulator.PayloadHash,
                appliedSuccessfully);
        }

        private static void SendClientCatalogCompleteAck(
            int transferId,
            int revision,
            string payloadHash,
            bool appliedSuccessfully)
        {
            if (
                !GameNetwork.IsClient ||
                !GameNetwork.IsSessionActive)
            {
                return;
            }

            try
            {
                GameNetwork.BeginModuleEventAsClient();
                GameNetwork.WriteMessage(
                    new CoopCampaignMapCatalogCompleteAckMessage(
                        transferId,
                        revision,
                        appliedSuccessfully,
                        payloadHash));
                GameNetwork.EndModuleEventAsClient();
            }
            catch
            {
                // Mission shutdown can close the connection during completion.
            }
        }

        private static void ApplyCompletedCatalog(
            int revision,
            IReadOnlyList<CoopCampaignMapPrototypeCatalogEntityState> entities)
        {
            List<CoopCampaignMapPrototypeCatalogEntityState> snapshot =
                CloneCatalog(entities);
            ClientCatalogById.Clear();
            foreach (CoopCampaignMapPrototypeCatalogEntityState entity in snapshot)
            {
                if (entity != null)
                    ClientCatalogById[entity.EntityId] = entity.Clone();
            }
            _clientCatalogCompletedRevision = revision;
            _clientCatalogActiveRevision = -1;
            _clientCatalogExpectedCount = 0;
            ClientCatalogByIndex.Clear();
            CurrentClientCatalog = snapshot;
            try
            {
                ClientCatalogChanged?.Invoke(revision, CloneCatalog(snapshot));
            }
            catch (Exception ex)
            {
                ModLogger.Error(
                    "CoopCampaignMapPrototypeNetworkController: catalog dispatch failed.",
                    ex);
            }
            ApplyMergedReplica();
        }

        private static void ApplyCompletedCatalogDelta(
            int revision,
            IReadOnlyList<CoopCampaignMapPrototypeCatalogEntityState> entities)
        {
            List<CoopCampaignMapPrototypeCatalogEntityState> updates =
                CloneCatalog(entities);
            foreach (CoopCampaignMapPrototypeCatalogEntityState update in updates)
            {
                if (update != null)
                    ClientCatalogById[update.EntityId] = update.Clone();
            }

            var current = new List<CoopCampaignMapPrototypeCatalogEntityState>(
                ClientCatalogById.Count);
            foreach (CoopCampaignMapPrototypeCatalogEntityState entity in
                     ClientCatalogById.Values)
            {
                current.Add(entity.Clone());
            }
            current.Sort(
                (left, right) =>
                {
                    int kindComparison = left.Kind.CompareTo(right.Kind);
                    return kindComparison != 0
                        ? kindComparison
                        : string.Compare(
                            left.EntityId,
                            right.EntityId,
                            StringComparison.OrdinalIgnoreCase);
                });

            _clientCatalogCompletedRevision = revision;
            _clientCatalogActiveRevision = -1;
            _clientCatalogExpectedCount = 0;
            ClientCatalogByIndex.Clear();
            CurrentClientCatalog = current;
            try
            {
                ClientCatalogDeltaChanged?.Invoke(
                    revision,
                    CloneCatalog(updates));
            }
            catch (Exception ex)
            {
                ModLogger.Error(
                    "CoopCampaignMapPrototypeNetworkController: catalog delta dispatch failed.",
                    ex);
            }
            ApplyMergedReplica();
        }

        private static void ApplyCompletedDynamic(
            int revision,
            IReadOnlyList<CoopCampaignMapPrototypeDynamicEntityState> entities)
        {
            List<CoopCampaignMapPrototypeDynamicEntityState> updates =
                CloneDynamic(entities);
            _clientDynamicCompletedRevision = revision;
            _clientDynamicActiveRevision = -1;
            _clientDynamicExpectedCount = 0;
            ClientDynamicByIndex.Clear();
            List<CoopCampaignMapPrototypeDynamicEntityState> current =
                CurrentClientDynamic as
                    List<CoopCampaignMapPrototypeDynamicEntityState>;
            if (current == null)
            {
                current = CloneDynamic(CurrentClientDynamic);
                ClientDynamicListIndexById.Clear();
                for (int index = 0; index < current.Count; index++)
                {
                    CoopCampaignMapPrototypeDynamicEntityState existing =
                        current[index];
                    if (existing != null)
                        ClientDynamicListIndexById[existing.EntityId] = index;
                }
            }
            foreach (CoopCampaignMapPrototypeDynamicEntityState update in updates)
            {
                ClientDynamicById[update.EntityId] = update.Clone();
                if (ClientDynamicListIndexById.TryGetValue(
                        update.EntityId,
                        out int index))
                {
                    current[index] = update.Clone();
                }
                else
                {
                    ClientDynamicListIndexById[update.EntityId] = current.Count;
                    current.Add(update.Clone());
                }
            }
            CurrentClientDynamic = current;
            try
            {
                ClientDynamicChanged?.Invoke(revision, CloneDynamic(updates));
            }
            catch (Exception ex)
            {
                ModLogger.Error(
                    "CoopCampaignMapPrototypeNetworkController: dynamic dispatch failed.",
                    ex);
            }
            if (CurrentClientCatalog.Count > 0 &&
                CurrentClientVisibleEntities.Count == 0)
            {
                ApplyMergedReplica();
            }
        }

        private static void ApplyCompletedConversation(
            CoopCampaignMapConversationState state)
        {
            if (!CoopCampaignMapPrototypeContract.TryValidateConversationState(
                    state,
                    out _) ||
                CurrentClientConversation != null &&
                state.Revision <= CurrentClientConversation.Revision)
            {
                return;
            }

            CurrentClientConversation = state.Clone();
            ClientPoseReceiver.Bind(state);
            try
            {
                ClientConversationChanged?.Invoke(state.Clone());
            }
            catch (Exception ex)
            {
                ModLogger.Error(
                    "CoopCampaignMapPrototypeNetworkController: conversation dispatch failed.",
                    ex);
            }
        }

        private static void ApplyMergedReplica()
        {
            var dynamicById = new Dictionary<
                string,
                CoopCampaignMapPrototypeDynamicEntityState>(
                    StringComparer.OrdinalIgnoreCase);
            foreach (CoopCampaignMapPrototypeDynamicEntityState dynamicState in
                     CurrentClientDynamic)
            {
                if (dynamicState != null)
                    dynamicById[dynamicState.EntityId] = dynamicState;
            }

            var merged = new List<CoopCampaignMapPrototypeEntityState>();
            foreach (CoopCampaignMapPrototypeCatalogEntityState catalog in
                     CurrentClientCatalog)
            {
                if (catalog == null ||
                    !dynamicById.TryGetValue(
                        catalog.EntityId,
                        out CoopCampaignMapPrototypeDynamicEntityState dynamicState) ||
                    !dynamicState.IsVisible)
                {
                    continue;
                }

                CoopCampaignMapPrototypeEntityState entity =
                    CoopCampaignMapReplicaStateProjection.Merge(catalog, dynamicState);
                if (entity != null)
                    merged.Add(entity);
            }

            if (_clientMergedRevision < int.MaxValue)
                _clientMergedRevision++;
            ApplyCompletedVisibleEntities(_clientMergedRevision, merged);
        }

        private static void ApplyCompletedVisibleEntities(
            int revision,
            IReadOnlyList<CoopCampaignMapPrototypeEntityState> entities)
        {
            List<CoopCampaignMapPrototypeEntityState> snapshot =
                CloneVisibleEntities(entities);
            CurrentClientVisibleEntitiesRevision = revision;
            CurrentClientVisibleEntities = snapshot;
            try
            {
                ClientVisibleEntitiesChanged?.Invoke(
                    revision,
                    CloneVisibleEntities(snapshot));
            }
            catch (Exception ex)
            {
                ModLogger.Error(
                    "CoopCampaignMapPrototypeNetworkController: visible entity dispatch failed.",
                    ex);
            }
        }

        private static void ResetClientVisibleEntities()
        {
            ClientPartyPoses.Clear();
            _partyPoseSession = null;
            ClientEntityAssembler.Reset();
            ClientCatalogByIndex.Clear();
            ClientCatalogById.Clear();
            ClientDynamicByIndex.Clear();
            ClientDynamicById.Clear();
            ClientDynamicListIndexById.Clear();
            _clientCatalogActiveRevision = -1;
            _clientCatalogExpectedCount = 0;
            _clientCatalogCompletedRevision = -1;
            _clientDynamicActiveRevision = -1;
            _clientDynamicExpectedCount = 0;
            _clientDynamicCompletedRevision = -1;
            _clientMergedRevision = 0;
            _clientCatalogTransport = null;
            _clientCompletedCatalogTransferId = 0;
            _clientCompletedCatalogTransferRevision = -1;
            _clientCompletedCatalogTransferHash = string.Empty;
            CurrentClientCatalog =
                new List<CoopCampaignMapPrototypeCatalogEntityState>();
            CurrentClientDynamic =
                new List<CoopCampaignMapPrototypeDynamicEntityState>();
            CurrentClientVisibleEntitiesRevision = -1;
            CurrentClientVisibleEntities =
                new List<CoopCampaignMapPrototypeEntityState>();
        }

        private static void ResetClientConversation()
        {
            ResetClientMenu();
            ResetClientBattleMarkers();
            ClientConversationAssembler.Reset();
            ClientPoseReceiver.Reset();
            CurrentClientConversation = null;
        }

        private void BroadcastConversation(CoopCampaignMapConversationState state)
        {
            // Supersession also cancels unfinished sends of the previous dialogue.
            _conversationSends.Clear();
            if (GameNetwork.NetworkPeers == null) return;
            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
                SendConversation(peer, state);
        }

        private void TickServerConversationTransports()
        {
            foreach (var send in new List<ConversationSendState>(_conversationSends.Values))
            {
                NetworkCommunicator current = FindNetworkPeer(send.Peer.Index);
                if (!ReferenceEquals(current, send.Peer) || !current.IsConnectionActive || !current.IsSynchronized)
                {
                    _conversationSends.Remove(send.Peer.Index);
                    continue;
                }
                try
                {
                    for (int budget = 0; budget < MaxConversationChunksPerPeerPerTick &&
                         send.NextChunk < send.Payload.ChunkCount; budget++)
                    {
                        GameNetwork.BeginModuleEventAsServer(current);
                        GameNetwork.WriteMessage(new CoopCampaignMapConversationChunkMessage(
                            send.Revision, send.Payload, send.NextChunk));
                        GameNetwork.EndModuleEventAsServer();
                        send.NextChunk++;
                    }
                    if (send.NextChunk == send.Payload.ChunkCount)
                        _conversationSends.Remove(send.Peer.Index);
                }
                catch (Exception ex)
                {
                    _conversationSends.Remove(send.Peer.Index);
                    ModLogger.Info("CoopCampaignMapPrototypeNetworkController: conversation transfer failed. Error=" + ex.Message + ".");
                }
            }
        }

        private static void BroadcastState(CoopCampaignMapPrototypeState state)
        {
            try
            {
                GameNetwork.BeginBroadcastModuleEvent();
                GameNetwork.WriteMessage(
                    new CoopCampaignMapPrototypeStateMessage(state));
                GameNetwork.EndBroadcastModuleEvent(
                    GameNetwork.EventBroadcastFlags.None);
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopCampaignMapPrototypeNetworkController: broadcast failed. Error=" +
                    ex.Message + ".");
            }
        }

        private static void BroadcastVisibleEntities(
            int revision,
            IReadOnlyList<CoopCampaignMapPrototypeEntityState> entities)
        {
            if (revision < 0)
                return;

            try
            {
                int count = Math.Min(
                    CoopCampaignMapPrototypeContract.MaxVisibleEntities,
                    entities?.Count ?? 0);
                GameNetwork.BeginBroadcastModuleEvent();
                GameNetwork.WriteMessage(
                    new CoopCampaignMapPrototypeEntitySnapshotMessage(
                        revision,
                        count));
                GameNetwork.EndBroadcastModuleEvent(
                    GameNetwork.EventBroadcastFlags.None);

                for (int index = 0; index < count; index++)
                {
                    GameNetwork.BeginBroadcastModuleEvent();
                    GameNetwork.WriteMessage(
                        new CoopCampaignMapPrototypeEntityStateMessage(
                            revision,
                            index,
                            count,
                            entities[index]));
                    GameNetwork.EndBroadcastModuleEvent(
                        GameNetwork.EventBroadcastFlags.None);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopCampaignMapPrototypeNetworkController: visible entity broadcast failed. Error=" +
                    ex.Message + ".");
            }
        }

        private bool PrepareCatalogTransport(
            int revision,
            IReadOnlyList<CoopCampaignMapPrototypeCatalogEntityState> entities)
        {
            _preparedCatalogPayload = null;
            _preparedCatalogRevision = -1;
            if (!CoopCampaignMapCatalogBinarySerializer.TrySerialize(
                    revision,
                    entities,
                    out byte[] logicalBytes,
                    out string reason) ||
                !CoopCampaignMapCatalogChunkCodec.TryEncode(
                    logicalBytes,
                    out CoopCampaignMapCatalogChunkedPayload payload,
                    out reason))
            {
                ModLogger.Info(
                    "CoopCampaignMapPrototypeNetworkController: catalog transport preparation failed. Reason=" +
                    (reason ?? "unknown") + ".");
                return false;
            }

            int nextTransferId = _catalogTransferId >=
                                 CoopCampaignMapCatalogChunkCodec.MaxTransferId
                ? 1
                : _catalogTransferId + 1;
            if (nextTransferId <= 0)
                nextTransferId = 1;
            _catalogTransferId = nextTransferId;
            _preparedCatalogRevision = revision;
            _preparedCatalogPayload = payload;
            ModLogger.Info(
                "CoopCampaignMapPrototypeNetworkController: prepared chunked catalog. " +
                "Revision=" + revision +
                " Entities=" + (entities?.Count ?? 0) +
                " LogicalBytes=" + payload.LogicalByteCount +
                " WireBytes=" + payload.WireByteCount +
                " Chunks=" + payload.ChunkCount + ".");
            return true;
        }

        private void QueueCatalogTransportForAllPeers()
        {
            if (GameNetwork.NetworkPeers == null)
                return;
            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
                QueueCatalogTransport(peer);
        }

        private void QueueCatalogTransport(NetworkCommunicator peer)
        {
            if (peer == null ||
                peer.IsServerPeer ||
                !peer.IsConnectionActive ||
                !peer.IsSynchronized)
            {
                return;
            }

            if ((_preparedCatalogPayload == null ||
                 _preparedCatalogRevision != _networkCatalogRevision) &&
                !PrepareCatalogTransport(
                    _networkCatalogRevision,
                    _serverCatalog))
            {
                return;
            }
            if (_catalogTransferId <= 0)
                return;

            if (_serverCatalogTransportByPeer.TryGetValue(
                    peer.Index,
                    out ServerCatalogTransportState existing) &&
                !CoopCampaignMapCatalogTransferPolicy
                    .ShouldStartPreparedTransfer(
                        existing.TransferId,
                        existing.Revision,
                        existing.IsCompleted,
                        _catalogTransferId,
                        _preparedCatalogRevision))
            {
                return;
            }

            _serverCatalogTransportByPeer[peer.Index] =
                new ServerCatalogTransportState(
                    peer.Index,
                    _catalogTransferId,
                    _preparedCatalogRevision,
                    _preparedCatalogPayload);
        }

        private void TickServerCatalogTransports()
        {
            if (GameNetwork.NetworkPeers == null)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            var states = new List<ServerCatalogTransportState>(
                _serverCatalogTransportByPeer.Values);
            foreach (ServerCatalogTransportState state in states)
            {
                NetworkCommunicator peer = FindNetworkPeer(state.PeerIndex);
                if (peer == null ||
                    !peer.IsConnectionActive ||
                    !peer.IsSynchronized ||
                    state.IsCompleted)
                {
                    continue;
                }

                if (!state.ManifestSent ||
                    !state.HasActiveRange &&
                    nowUtc - state.LastManifestSentUtc >=
                        CatalogManifestRetryDelay)
                {
                    if (SendCatalogManifest(peer, state))
                        state.MarkManifestSent(nowUtc);
                }

                int sentThisTick = 0;
                while (sentThisTick < MaxCatalogChunksPerPeerPerTick &&
                       state.TryGetNextChunkIndex(out int chunkIndex))
                {
                    if (!SendCatalogChunk(peer, state, chunkIndex))
                        break;
                    state.MarkChunkSent(chunkIndex);
                    sentThisTick++;
                }
            }
        }

        private static NetworkCommunicator FindNetworkPeer(int peerIndex)
        {
            if (GameNetwork.NetworkPeers == null)
                return null;
            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer != null && peer.Index == peerIndex)
                    return peer;
            }
            return null;
        }

        private static bool SendCatalogManifest(
            NetworkCommunicator peer,
            ServerCatalogTransportState state)
        {
            try
            {
                GameNetwork.BeginModuleEventAsServer(peer);
                GameNetwork.WriteMessage(
                    new CoopCampaignMapCatalogManifestMessage(
                        state.TransferId,
                        state.Revision,
                        state.Payload.LogicalByteCount,
                        state.Payload.WireByteCount,
                        state.Payload.ChunkCount,
                        state.Payload.CompressionKind,
                        state.Payload.PayloadHash));
                GameNetwork.EndModuleEventAsServer();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool SendCatalogChunk(
            NetworkCommunicator peer,
            ServerCatalogTransportState state,
            int chunkIndex)
        {
            if (chunkIndex < 0 ||
                chunkIndex >= state.Payload.ChunkCount)
            {
                return false;
            }

            try
            {
                GameNetwork.BeginModuleEventAsServer(peer);
                GameNetwork.WriteMessage(
                    new CoopCampaignMapCatalogChunkMessage(
                        state.TransferId,
                        chunkIndex,
                        state.Payload.ChunkCount,
                        state.Payload.Chunks[chunkIndex]));
                GameNetwork.EndModuleEventAsServer();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void SendCatalogDeltaToReadyPeers(
            int previousRevision,
            int revision,
            IReadOnlyList<CoopCampaignMapPrototypeCatalogEntityState> entities)
        {
            if (GameNetwork.NetworkPeers == null ||
                entities == null ||
                entities.Count == 0)
            {
                return;
            }

            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null ||
                    peer.IsServerPeer ||
                    !peer.IsConnectionActive ||
                    !peer.IsSynchronized)
                {
                    continue;
                }

                if (!_serverCatalogTransportByPeer.TryGetValue(
                        peer.Index,
                        out ServerCatalogTransportState state))
                {
                    QueueCatalogTransport(peer);
                    continue;
                }
                if (!state.IsCompleted)
                    continue;
                if (state.AppliedRevision != previousRevision)
                {
                    QueueCatalogTransport(peer);
                    continue;
                }

                if (SendCatalogDelta(peer, revision, entities))
                    state.MarkCatalogRevision(revision);
                else
                    QueueCatalogTransport(peer);
            }
        }

        private static bool SendCatalogDelta(
            NetworkCommunicator peer,
            int revision,
            IReadOnlyList<CoopCampaignMapPrototypeCatalogEntityState> entities)
        {
            if (peer == null || revision < 0 || entities == null)
                return false;

            int count = Math.Min(
                CoopCampaignMapPrototypeContract.MaxCatalogEntities,
                entities.Count);
            if (count <= 0)
                return true;

            // Occupant lists plus hover frames can exceed a native single-message budget. Admission
            // must happen before the header: queue the bounded binary transfer instead.
            for (int index = 0; index < count; index++)
                if (!string.IsNullOrEmpty(entities[index]?.SettlementPartyBadges) ||
                    !string.IsNullOrEmpty(entities[index]?.HoverDecal)) return false;

            try
            {
                GameNetwork.BeginModuleEventAsServer(peer);
                GameNetwork.WriteMessage(
                    new CoopCampaignMapCatalogSnapshotMessage(revision, count));
                GameNetwork.EndModuleEventAsServer();
                for (int index = 0; index < count; index++)
                {
                    GameNetwork.BeginModuleEventAsServer(peer);
                    GameNetwork.WriteMessage(
                        new CoopCampaignMapCatalogEntityMessage(
                            revision,
                            index,
                            count,
                            entities[index]));
                    GameNetwork.EndModuleEventAsServer();
                }
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopCampaignMapPrototypeNetworkController: catalog delta direct send failed. Peer=" +
                    peer.Index + " Error=" + ex.Message + ".");
                return false;
            }
        }

        private static void BroadcastDynamic(
            int revision,
            IReadOnlyList<CoopCampaignMapPrototypeDynamicEntityState> entities)
        {
            if (revision < 0)
                return;
            try
            {
                int count = Math.Min(
                    CoopCampaignMapPrototypeContract.MaxDynamicEntities,
                    entities?.Count ?? 0);
                GameNetwork.BeginBroadcastModuleEvent();
                GameNetwork.WriteMessage(
                    new CoopCampaignMapDynamicSnapshotMessage(revision, count));
                GameNetwork.EndBroadcastModuleEvent(
                    GameNetwork.EventBroadcastFlags.None);
                for (int start = 0;
                     start < count;
                     start += CoopCampaignMapPrototypeContract.MaxDynamicBatchSize)
                {
                    int batchCount = Math.Min(
                        CoopCampaignMapPrototypeContract.MaxDynamicBatchSize,
                        count - start);
                    var batch =
                        new List<CoopCampaignMapPrototypeDynamicEntityState>(
                            batchCount);
                    for (int offset = 0; offset < batchCount; offset++)
                        batch.Add(entities[start + offset]);
                    GameNetwork.BeginBroadcastModuleEvent();
                    GameNetwork.WriteMessage(
                        new CoopCampaignMapDynamicBatchMessage(
                            revision,
                            start,
                            count,
                            batch));
                    GameNetwork.EndBroadcastModuleEvent(
                        GameNetwork.EventBroadcastFlags.None);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopCampaignMapPrototypeNetworkController: dynamic broadcast failed. Error=" +
                    ex.Message + ".");
            }
        }

        private static void SendState(
            NetworkCommunicator peer,
            CoopCampaignMapPrototypeState state)
        {
            try
            {
                GameNetwork.BeginModuleEventAsServer(peer);
                GameNetwork.WriteMessage(
                    new CoopCampaignMapPrototypeStateMessage(state));
                GameNetwork.EndModuleEventAsServer();
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopCampaignMapPrototypeNetworkController: direct send failed. Peer=" +
                    peer.Index + " Error=" + ex.Message + ".");
            }
        }

        private void SendConversation(NetworkCommunicator peer, CoopCampaignMapConversationState state)
        {
            if (peer == null || !peer.IsConnectionActive || !peer.IsSynchronized ||
                !CoopCampaignMapPrototypeContract.TryValidateConversationState(state, out _)) return;
            if (_preparedConversationRevision != state.Revision)
            {
                _preparedConversationRevision = state.Revision;
                _preparedConversation = null;
                if (!CoopCampaignMapConversationPayloadCodec.TrySerialize(state, out var bytes) ||
                    !CoopCampaignMapCatalogChunkCodec.TryEncode(bytes, out _preparedConversation, out _))
                {
                    // A malformed optional presentation must not retain an older active
                    // dialogue indefinitely. Send an inactive state at the same revision.
                    var inactive = CoopCampaignMapPrototypeContract.CreateInactiveConversation(state.Revision);
                    if (CoopCampaignMapConversationPayloadCodec.TrySerialize(inactive, out bytes))
                        CoopCampaignMapCatalogChunkCodec.TryEncode(bytes, out _preparedConversation, out _);
                }
            }
            if (_preparedConversation == null) return;
            _conversationSends[peer.Index] = new ConversationSendState
            {
                Peer = peer, Revision = state.Revision, Payload = _preparedConversation, NextChunk = 0
            };
        }

        private static void SendVisibleEntities(
            NetworkCommunicator peer,
            int revision,
            IReadOnlyList<CoopCampaignMapPrototypeEntityState> entities)
        {
            if (peer == null || revision < 0)
                return;

            try
            {
                int count = Math.Min(
                    CoopCampaignMapPrototypeContract.MaxVisibleEntities,
                    entities?.Count ?? 0);
                GameNetwork.BeginModuleEventAsServer(peer);
                GameNetwork.WriteMessage(
                    new CoopCampaignMapPrototypeEntitySnapshotMessage(
                        revision,
                        count));
                GameNetwork.EndModuleEventAsServer();

                for (int index = 0; index < count; index++)
                {
                    GameNetwork.BeginModuleEventAsServer(peer);
                    GameNetwork.WriteMessage(
                        new CoopCampaignMapPrototypeEntityStateMessage(
                            revision,
                            index,
                            count,
                            entities[index]));
                    GameNetwork.EndModuleEventAsServer();
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopCampaignMapPrototypeNetworkController: visible entity direct send failed. Peer=" +
                    peer.Index + " Error=" + ex.Message + ".");
            }
        }

        private static void SendDynamic(
            NetworkCommunicator peer,
            int revision,
            IReadOnlyList<CoopCampaignMapPrototypeDynamicEntityState> entities)
        {
            if (peer == null || revision < 0)
                return;
            try
            {
                int count = Math.Min(
                    CoopCampaignMapPrototypeContract.MaxDynamicEntities,
                    entities?.Count ?? 0);
                GameNetwork.BeginModuleEventAsServer(peer);
                GameNetwork.WriteMessage(
                    new CoopCampaignMapDynamicSnapshotMessage(revision, count));
                GameNetwork.EndModuleEventAsServer();
                for (int start = 0;
                     start < count;
                     start += CoopCampaignMapPrototypeContract.MaxDynamicBatchSize)
                {
                    int batchCount = Math.Min(
                        CoopCampaignMapPrototypeContract.MaxDynamicBatchSize,
                        count - start);
                    var batch =
                        new List<CoopCampaignMapPrototypeDynamicEntityState>(
                            batchCount);
                    for (int offset = 0; offset < batchCount; offset++)
                        batch.Add(entities[start + offset]);
                    GameNetwork.BeginModuleEventAsServer(peer);
                    GameNetwork.WriteMessage(
                        new CoopCampaignMapDynamicBatchMessage(
                            revision,
                            start,
                            count,
                            batch));
                    GameNetwork.EndModuleEventAsServer();
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopCampaignMapPrototypeNetworkController: dynamic direct send failed. Peer=" +
                    peer.Index + " Error=" + ex.Message + ".");
            }
        }

        private static List<CoopCampaignMapPrototypeEntityState>
            CloneVisibleEntities(
                IEnumerable<CoopCampaignMapPrototypeEntityState> entities)
        {
            var clone = new List<CoopCampaignMapPrototypeEntityState>();
            if (entities == null)
                return clone;

            foreach (CoopCampaignMapPrototypeEntityState entity in entities)
            {
                if (entity != null)
                    clone.Add(entity.Clone());
            }
            return clone;
        }

        private static List<CoopCampaignMapPrototypeCatalogEntityState>
            CloneCatalog(
                IEnumerable<CoopCampaignMapPrototypeCatalogEntityState> entities)
        {
            var clone =
                new List<CoopCampaignMapPrototypeCatalogEntityState>();
            if (entities == null)
                return clone;
            foreach (CoopCampaignMapPrototypeCatalogEntityState entity in entities)
            {
                if (entity != null)
                    clone.Add(entity.Clone());
            }
            return clone;
        }

        private static List<CoopCampaignMapPrototypeDynamicEntityState>
            CloneDynamic(
                IEnumerable<CoopCampaignMapPrototypeDynamicEntityState> entities)
        {
            var clone =
                new List<CoopCampaignMapPrototypeDynamicEntityState>();
            if (entities == null)
                return clone;
            foreach (CoopCampaignMapPrototypeDynamicEntityState entity in entities)
            {
                if (entity != null)
                    clone.Add(entity.Clone());
            }
            return clone;
        }

        private sealed class ServerCatalogTransportState
        {
            public ServerCatalogTransportState(
                int peerIndex,
                int transferId,
                int revision,
                CoopCampaignMapCatalogChunkedPayload payload)
            {
                PeerIndex = peerIndex;
                TransferId = transferId;
                Revision = revision;
                Payload = payload;
                NextChunkIndex = -1;
            }

            public int PeerIndex { get; }

            public int TransferId { get; }

            public int Revision { get; }

            public int AppliedRevision { get; private set; } = -1;

            public CoopCampaignMapCatalogChunkedPayload Payload { get; }

            public bool ManifestSent { get; private set; }

            public DateTime LastManifestSentUtc { get; private set; }

            public bool HasActiveRange { get; private set; }

            public int RequestedStartChunkIndex { get; private set; } = -1;

            public int RequestedEndChunkIndex { get; private set; } = -1;

            public int NextChunkIndex { get; private set; }

            public int HighestClientContiguousChunkIndex { get; private set; } =
                -1;

            public int ClientReceivedChunkCount { get; private set; }

            public bool IsCompleted { get; private set; }

            public void MarkManifestSent(DateTime nowUtc)
            {
                ManifestSent = true;
                LastManifestSentUtc = nowUtc;
            }

            public void RequestRange(
                int startChunkIndex,
                int endChunkIndex,
                int highestContiguousChunkIndex,
                int receivedChunkCount)
            {
                RequestedStartChunkIndex = startChunkIndex;
                RequestedEndChunkIndex = endChunkIndex;
                NextChunkIndex = startChunkIndex;
                HighestClientContiguousChunkIndex = Math.Max(
                    HighestClientContiguousChunkIndex,
                    highestContiguousChunkIndex);
                ClientReceivedChunkCount = Math.Max(
                    ClientReceivedChunkCount,
                    receivedChunkCount);
                HasActiveRange = true;
            }

            public bool TryGetNextChunkIndex(out int chunkIndex)
            {
                chunkIndex = -1;
                if (IsCompleted ||
                    !HasActiveRange ||
                    NextChunkIndex < RequestedStartChunkIndex ||
                    NextChunkIndex > RequestedEndChunkIndex)
                {
                    return false;
                }
                chunkIndex = NextChunkIndex;
                return true;
            }

            public void MarkChunkSent(int chunkIndex)
            {
                if (!HasActiveRange || chunkIndex != NextChunkIndex)
                    return;
                NextChunkIndex++;
                if (NextChunkIndex > RequestedEndChunkIndex)
                {
                    HasActiveRange = false;
                    NextChunkIndex = -1;
                }
            }

            public void MarkCompleted()
            {
                IsCompleted = true;
                AppliedRevision = Revision;
                HasActiveRange = false;
                NextChunkIndex = -1;
            }

            public void MarkCatalogRevision(int revision)
            {
                if (IsCompleted && revision > AppliedRevision)
                    AppliedRevision = revision;
            }

            public void ResetForRetry()
            {
                ManifestSent = false;
                LastManifestSentUtc = DateTime.MinValue;
                HasActiveRange = false;
                RequestedStartChunkIndex = -1;
                RequestedEndChunkIndex = -1;
                NextChunkIndex = -1;
                HighestClientContiguousChunkIndex = -1;
                ClientReceivedChunkCount = 0;
                IsCompleted = false;
                AppliedRevision = -1;
            }
        }

        private sealed class ClientCatalogTransportState
        {
            public ClientCatalogTransportState(
                CoopCampaignMapCatalogChunkAccumulator accumulator)
            {
                Accumulator = accumulator;
                RequestedStartChunkIndex = 0;
                RequestedEndChunkIndex = Math.Min(
                    accumulator.ChunkCount - 1,
                    CatalogRequestWindowChunks - 1);
                LastUsefulChunkUtc = DateTime.UtcNow;
            }

            public CoopCampaignMapCatalogChunkAccumulator Accumulator
            {
                get;
            }

            public int RequestedStartChunkIndex { get; private set; }

            public int RequestedEndChunkIndex { get; private set; }

            public DateTime LastRangeRequestUtc { get; set; }

            public DateTime LastUsefulChunkUtc { get; set; }

            public void AdvanceRequestedWindow()
            {
                int nextStart = RequestedEndChunkIndex + 1;
                if (nextStart >= Accumulator.ChunkCount)
                {
                    RequestedStartChunkIndex = -1;
                    RequestedEndChunkIndex = -1;
                    return;
                }
                RequestedStartChunkIndex = nextStart;
                RequestedEndChunkIndex = Math.Min(
                    Accumulator.ChunkCount - 1,
                    nextStart + CatalogRequestWindowChunks - 1);
                LastRangeRequestUtc = DateTime.MinValue;
            }
        }
    }
}
