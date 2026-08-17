using System;
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

        public static event Action<CoopCampaignMapPrototypeState> ClientStateChanged;

        public static CoopCampaignMapPrototypeState CurrentClientState { get; private set; }

        public override void OnBehaviorInitialize()
        {
            base.OnBehaviorInitialize();
            if (GameNetwork.IsClient)
                CurrentClientState = null;
            if (!GameNetwork.IsServer)
                return;

            _serverState = null;
            _nextBridgePollAt = 0f;
            _lastBridgeSessionId = null;
            _lastBridgeRevision = 0;
            _networkRevision = 0;
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

            _lastBridgeSessionId = snapshot.SessionId;
            _lastBridgeRevision = snapshot.Revision;
            if (_networkRevision < int.MaxValue)
                _networkRevision++;
            _serverState = CoopCampaignMapPrototypeContract.ToNetworkState(
                snapshot,
                _networkRevision);
            if (_serverState == null)
                return;

            LogAvailabilityTransition(
                "authoritative:session=" + snapshot.SessionId);
            BroadcastState(_serverState);
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
            }
        }

        public override void OnMissionStateFinalized()
        {
            if (GameNetwork.IsClient)
                CurrentClientState = null;
            _serverState = null;
            _lastBridgeSessionId = null;
            _lastBridgeRevision = 0;
            _networkRevision = 0;
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
    }
}
