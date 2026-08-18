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
        private int _lastVisibleEntitiesRevision;
        private int _networkVisibleEntitiesRevision;
        private List<CoopCampaignMapPrototypeEntityState> _serverVisibleEntities =
            new List<CoopCampaignMapPrototypeEntityState>();
        private string _lastAvailabilityReason;
        private static readonly CoopCampaignMapPrototypeEntitySnapshotAssembler
            ClientEntityAssembler =
                new CoopCampaignMapPrototypeEntitySnapshotAssembler();

        public static event Action<CoopCampaignMapPrototypeState> ClientStateChanged;

        public static event Action<
            int,
            IReadOnlyList<CoopCampaignMapPrototypeEntityState>>
            ClientVisibleEntitiesChanged;

        public static CoopCampaignMapPrototypeState CurrentClientState { get; private set; }

        public static IReadOnlyList<CoopCampaignMapPrototypeEntityState>
            CurrentClientVisibleEntities { get; private set; } =
                new List<CoopCampaignMapPrototypeEntityState>();

        public static int CurrentClientVisibleEntitiesRevision { get; private set; } = -1;

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
            _lastVisibleEntitiesRevision = -1;
            _networkVisibleEntitiesRevision = 0;
            _serverVisibleEntities.Clear();
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

            if (!sameSession)
            {
                _lastVisibleEntitiesRevision = -1;
                _serverVisibleEntities.Clear();
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

            bool visibleEntitiesChanged =
                snapshot.VisibleEntitiesRevision !=
                _lastVisibleEntitiesRevision;
            if (visibleEntitiesChanged)
            {
                _lastVisibleEntitiesRevision = snapshot.VisibleEntitiesRevision;
                if (_networkVisibleEntitiesRevision < int.MaxValue)
                    _networkVisibleEntitiesRevision++;
                _serverVisibleEntities = CloneVisibleEntities(
                    snapshot.VisibleEntities);
            }
            _serverState.VisibleEntitiesRevision =
                _networkVisibleEntitiesRevision;

            LogAvailabilityTransition(
                "authoritative:session=" + snapshot.SessionId);
            BroadcastState(_serverState);

            if (visibleEntitiesChanged)
            {
                BroadcastVisibleEntities(
                    _networkVisibleEntitiesRevision,
                    _serverVisibleEntities);
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
                SendVisibleEntities(
                    networkPeer,
                    _networkVisibleEntitiesRevision,
                    _serverVisibleEntities);
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
            _lastVisibleEntitiesRevision = -1;
            _networkVisibleEntitiesRevision = 0;
            _serverVisibleEntities.Clear();
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
    }
}
