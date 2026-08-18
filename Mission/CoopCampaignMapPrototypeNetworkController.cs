using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using CoopSpectator.Network.Messages;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.MissionBehaviors
{
    public sealed class CoopCampaignMapPrototypeNetworkController : MissionNetwork
    {
        private const float BridgePollIntervalSeconds = 0.1f;
        private static readonly TimeSpan MaximumHostStateAge =
            TimeSpan.FromSeconds(2d);

        private CoopCampaignMapPrototypeState _serverState;
        private float _nextBridgePollAt;
        private string _lastBridgeSessionId;
        private int _lastBridgeRevision;
        private int _networkRevision;
        private string _lastAvailabilityReason;
        private int _lastBridgeCatalogRevision = -1;
        private int _lastBridgeDynamicRevision = -1;
        private int _networkCatalogRevision;
        private int _networkDynamicRevision;
        private List<CoopCampaignMapPrototypeCatalogEntityState> _serverCatalog =
            new List<CoopCampaignMapPrototypeCatalogEntityState>();
        private List<CoopCampaignMapPrototypeDynamicEntityState> _serverDynamic =
            new List<CoopCampaignMapPrototypeDynamicEntityState>();
        private static readonly CoopCampaignMapPrototypeEntitySnapshotAssembler
            ClientEntityAssembler =
                new CoopCampaignMapPrototypeEntitySnapshotAssembler();

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
            IReadOnlyList<CoopCampaignMapPrototypeDynamicEntityState>>
            ClientDynamicChanged;

        public static CoopCampaignMapPrototypeState CurrentClientState { get; private set; }

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

        private static readonly Dictionary<int, CoopCampaignMapPrototypeCatalogEntityState>
            ClientCatalogByIndex =
                new Dictionary<int, CoopCampaignMapPrototypeCatalogEntityState>();
        private static readonly Dictionary<int, CoopCampaignMapPrototypeDynamicEntityState>
            ClientDynamicByIndex =
                new Dictionary<int, CoopCampaignMapPrototypeDynamicEntityState>();
        private static int _clientCatalogActiveRevision = -1;
        private static int _clientCatalogExpectedCount;
        private static int _clientCatalogCompletedRevision = -1;
        private static int _clientDynamicActiveRevision = -1;
        private static int _clientDynamicExpectedCount;
        private static int _clientDynamicCompletedRevision = -1;
        private static int _clientMergedRevision;

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            if (GameNetwork.IsClient)
            {
                CurrentClientState = null;
                ResetClientVisibleEntities();
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
            _networkCatalogRevision = 0;
            _networkDynamicRevision = 0;
            _serverCatalog.Clear();
            _serverDynamic.Clear();
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
                registerer.RegisterBaseHandler<CoopCampaignMapDynamicSnapshotMessage>(
                    HandleServerDynamicSnapshot);
                registerer.RegisterBaseHandler<CoopCampaignMapDynamicBatchMessage>(
                    HandleServerDynamicBatch);
            }
        }

        public override void OnMissionTick(float dt)
        {
            base.OnMissionTick(dt);
            if (!GameNetwork.IsServer)
                return;
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
                _serverCatalog.Clear();
                _serverDynamic.Clear();
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

            if (catalogChanged)
            {
                _lastBridgeCatalogRevision = snapshot.CatalogRevision;
                if (_networkCatalogRevision < int.MaxValue)
                    _networkCatalogRevision++;
                _serverCatalog = CloneCatalog(catalogSnapshot?.Entities);
            }
            if (dynamicChanged)
            {
                _lastBridgeDynamicRevision = snapshot.DynamicRevision;
                if (_networkDynamicRevision < int.MaxValue)
                    _networkDynamicRevision++;
                _serverDynamic = CloneDynamic(dynamicSnapshot?.Entities);
            }
            _serverState.VisibleEntitiesRevision = _networkDynamicRevision;
            _serverState.CatalogRevision = _networkCatalogRevision;
            _serverState.DynamicRevision = _networkDynamicRevision;

            LogAvailabilityTransition(
                "authoritative:session=" + snapshot.SessionId);
            BroadcastState(_serverState);

            if (catalogChanged)
                BroadcastCatalog(_networkCatalogRevision, _serverCatalog);
            if (dynamicChanged)
                BroadcastDynamic(_networkDynamicRevision, _serverDynamic);
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
                SendCatalog(
                    networkPeer,
                    _networkCatalogRevision,
                    _serverCatalog);
                SendDynamic(
                    networkPeer,
                    _networkDynamicRevision,
                    _serverDynamic);
            }
        }

        public override void OnMissionStateFinalized()
        {
            if (GameNetwork.IsClient)
            {
                CurrentClientState = null;
                ResetClientVisibleEntities();
            }
            _serverState = null;
            _lastBridgeSessionId = null;
            _lastBridgeRevision = 0;
            _networkRevision = 0;
            _lastBridgeCatalogRevision = -1;
            _lastBridgeDynamicRevision = -1;
            _networkCatalogRevision = 0;
            _networkDynamicRevision = 0;
            _serverCatalog.Clear();
            _serverDynamic.Clear();
            _lastAvailabilityReason = null;
            base.OnMissionStateFinalized();
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
                ApplyCompletedCatalog(message.Revision,
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
            ApplyCompletedCatalog(message.Revision, completed);
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

        private static void ApplyCompletedCatalog(
            int revision,
            IReadOnlyList<CoopCampaignMapPrototypeCatalogEntityState> entities)
        {
            List<CoopCampaignMapPrototypeCatalogEntityState> snapshot =
                CloneCatalog(entities);
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

        private static void ApplyCompletedDynamic(
            int revision,
            IReadOnlyList<CoopCampaignMapPrototypeDynamicEntityState> entities)
        {
            List<CoopCampaignMapPrototypeDynamicEntityState> snapshot =
                CloneDynamic(entities);
            _clientDynamicCompletedRevision = revision;
            _clientDynamicActiveRevision = -1;
            _clientDynamicExpectedCount = 0;
            ClientDynamicByIndex.Clear();
            CurrentClientDynamic = snapshot;
            try
            {
                ClientDynamicChanged?.Invoke(revision, CloneDynamic(snapshot));
            }
            catch (Exception ex)
            {
                ModLogger.Error(
                    "CoopCampaignMapPrototypeNetworkController: dynamic dispatch failed.",
                    ex);
            }
            ApplyMergedReplica();
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

                merged.Add(new CoopCampaignMapPrototypeEntityState
                {
                    EntityId = catalog.EntityId,
                    DisplayName = catalog.DisplayName,
                    Kind = catalog.Kind,
                    SettlementNameplateSize = catalog.SettlementNameplateSize,
                    NormalizedX = dynamicState.NormalizedX,
                    NormalizedY = dynamicState.NormalizedY,
                    Heading = dynamicState.Heading,
                    PartySize = dynamicState.PartySize,
                    PrimaryColor = catalog.PrimaryColor,
                    SecondaryColor = catalog.SecondaryColor,
                    BannerCode = catalog.BannerCode,
                    VisualCharacterId = catalog.VisualCharacterId,
                    CultureId = catalog.CultureId,
                    PartyVisualKind = catalog.PartyVisualKind,
                    HumanVisual = catalog.HumanVisual?.Clone(),
                    MountVisual = catalog.MountVisual?.Clone(),
                    CaravanMountVisual = catalog.CaravanMountVisual?.Clone()
                });
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
            ClientEntityAssembler.Reset();
            ClientCatalogByIndex.Clear();
            ClientDynamicByIndex.Clear();
            _clientCatalogActiveRevision = -1;
            _clientCatalogExpectedCount = 0;
            _clientCatalogCompletedRevision = -1;
            _clientDynamicActiveRevision = -1;
            _clientDynamicExpectedCount = 0;
            _clientDynamicCompletedRevision = -1;
            _clientMergedRevision = 0;
            CurrentClientCatalog =
                new List<CoopCampaignMapPrototypeCatalogEntityState>();
            CurrentClientDynamic =
                new List<CoopCampaignMapPrototypeDynamicEntityState>();
            CurrentClientVisibleEntitiesRevision = -1;
            CurrentClientVisibleEntities =
                new List<CoopCampaignMapPrototypeEntityState>();
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

        private static void BroadcastCatalog(
            int revision,
            IReadOnlyList<CoopCampaignMapPrototypeCatalogEntityState> entities)
        {
            if (revision < 0)
                return;
            try
            {
                int count = Math.Min(
                    CoopCampaignMapPrototypeContract.MaxCatalogEntities,
                    entities?.Count ?? 0);
                GameNetwork.BeginBroadcastModuleEvent();
                GameNetwork.WriteMessage(
                    new CoopCampaignMapCatalogSnapshotMessage(revision, count));
                GameNetwork.EndBroadcastModuleEvent(
                    GameNetwork.EventBroadcastFlags.None);
                for (int index = 0; index < count; index++)
                {
                    GameNetwork.BeginBroadcastModuleEvent();
                    GameNetwork.WriteMessage(
                        new CoopCampaignMapCatalogEntityMessage(
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
                    "CoopCampaignMapPrototypeNetworkController: catalog broadcast failed. Error=" +
                    ex.Message + ".");
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

        private static void SendCatalog(
            NetworkCommunicator peer,
            int revision,
            IReadOnlyList<CoopCampaignMapPrototypeCatalogEntityState> entities)
        {
            if (peer == null || revision < 0)
                return;
            try
            {
                int count = Math.Min(
                    CoopCampaignMapPrototypeContract.MaxCatalogEntities,
                    entities?.Count ?? 0);
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
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "CoopCampaignMapPrototypeNetworkController: catalog direct send failed. Peer=" +
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
    }
}
