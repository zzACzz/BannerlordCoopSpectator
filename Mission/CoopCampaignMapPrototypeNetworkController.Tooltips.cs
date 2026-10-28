using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CoopSpectator.Infrastructure;
using CoopSpectator.Network.Messages;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.MissionBehaviors
{
    public sealed partial class CoopCampaignMapPrototypeNetworkController
    {
        private static readonly Stopwatch TooltipClock = Stopwatch.StartNew();
        private static long TooltipNow => TooltipClock.ElapsedMilliseconds;
        private static CoopCampaignMapPrototypeNetworkController _tooltipOwner;
        private static readonly CoopCampaignMapTooltipInterest TooltipInterest = new CoopCampaignMapTooltipInterest();
        private static readonly CoopCampaignMapTooltipHandshake TooltipHandshake = new CoopCampaignMapTooltipHandshake();
        private static readonly CoopCampaignMapTooltipAssembler TooltipAssembler = new CoopCampaignMapTooltipAssembler();
        private static string _tooltipDesired;
        private static bool _tooltipExtended;
        private static long _tooltipClientStateAt = -1, _tooltipNextHello;
        private static bool _tooltipClientWasAvailable;
        public static CoopCampaignMapTooltipPayload CurrentClientTooltip { get; private set; }
        public static event Action<CoopCampaignMapTooltipPayload> ClientTooltipChanged;
        private long _tooltipHostSeenAt = -1, _tooltipNextRelayPoll, _tooltipNextRelayOpen;
        private string _tooltipServerSession;
        private bool _tooltipFaulted;
        private int _tooltipRoundRobin, _tooltipSendRoundRobin;
        private CoopCampaignMapTooltipRelayClient _tooltipRelay;
        private List<TooltipSubmission> _tooltipInFlight;
        private long _tooltipInFlightAt;
        private readonly Dictionary<int, TooltipPeer> _tooltipPeers = new Dictionary<int, TooltipPeer>();

        private sealed class TooltipPeer
        {
            public NetworkCommunicator Peer;
            public Guid Challenge;
            public CoopCampaignMapTooltipPeerGate Gate;
            public CoopCampaignMapTooltipCaptureTicket Ticket;
            public long TicketAt, LastControlAt = -1000;
            public CoopCampaignMapCatalogChunkedPayload Send;
            public CoopCampaignMapTooltipRequest SendRequest;
            public long SendSequence, SendAt;
            public int NextChunk;
        }
        private sealed class TooltipSubmission
        {
            public TooltipPeer Owner;
            public CoopCampaignMapTooltipCaptureTicket Ticket;
        }

        private void InitializeTooltips()
        {
            _tooltipFaulted = false; _tooltipHostSeenAt = -1;
            if (GameNetwork.IsClient) { _tooltipOwner = this; _tooltipClientStateAt = -1; _tooltipClientWasAvailable = false; ResetClientTooltipSession(); }
        }
        private void FinalizeTooltips()
        {
            foreach (var entry in _tooltipPeers.Values) entry.Gate?.Revoke();
            _tooltipPeers.Clear(); _tooltipRelay?.Revoke(); _tooltipRelay = null; _tooltipInFlight = null;
            if (ReferenceEquals(_tooltipOwner, this)) { _tooltipOwner = null; _tooltipClientStateAt = -1; ResetClientTooltipSession(); }
        }
        private static void ResetClientTooltipSession()
        {
            TooltipHandshake.Reset(); TooltipInterest.Reset(); TooltipAssembler.Clear(); _tooltipNextHello = 0;
            ClearClientTooltip();
        }
        public static void ClearClientTooltip()
        {
            _tooltipDesired = null; TooltipInterest.Clear(); TooltipAssembler.Clear(); PublishTooltip(null);
        }
        public static void SetClientTooltipInterest(string entityId, bool extended)
        {
            if (_tooltipOwner == null || !GameNetwork.IsClient) return;
            if (_tooltipDesired != entityId || _tooltipExtended != extended)
            { TooltipInterest.Clear(); TooltipAssembler.Clear(); PublishTooltip(null); }
            _tooltipDesired = entityId; _tooltipExtended = extended;
        }
        private static void PublishTooltip(CoopCampaignMapTooltipPayload payload)
        {
            if (payload == null && CurrentClientTooltip == null) return;
            CurrentClientTooltip = payload;
            try { ClientTooltipChanged?.Invoke(payload); }
            catch (Exception) { /* Passive UI listener cannot break mission networking. */ }
        }
        private bool ServerTooltipAvailable(long now) => _serverState != null && _lastBridgeSessionId != null &&
            _tooltipHostSeenAt >= 0 && now >= _tooltipHostSeenAt && now - _tooltipHostSeenAt < 2000;
        private static bool ClientTooltipAvailable(long now) => CurrentClientState != null && _tooltipClientStateAt >= 0 &&
            now >= _tooltipClientStateAt && now - _tooltipClientStateAt < 2000;
        private static bool PeerLive(NetworkCommunicator peer) => peer != null && !peer.IsServerPeer && peer.IsConnectionActive &&
            peer.IsSynchronized && ReferenceEquals(FindNetworkPeer(peer.Index), peer);

        private bool ServerTooltipTarget(CoopCampaignMapTooltipRequest request, long now)
        {
            if (!ServerTooltipAvailable(now) || _serverConversation.IsActive || request == null) return false;
            if (CoopCampaignMapTooltipTargetAdmission.IsAftermathAdmitted(_lastBridgeSessionId, request, _serverMenu)) return true;
            if (CoopCampaignMapTooltipTargetAdmission.IsBarHintAdmitted(_lastBridgeSessionId, request, _serverMapBar)) return true;
            if (CoopCampaignMapTooltipTargetAdmission.IsPlunderAdmitted(_lastBridgeSessionId, request, _serverMenu)) return true;
            if (CoopCampaignMapTooltipTargetAdmission.IsEncounterAdmitted(_lastBridgeSessionId, request, _serverMenu)) return true;
            if (CoopCampaignMapTooltipTargetAdmission.IsSettlementHintAdmitted(_lastBridgeSessionId, request, _serverMenu)) return true;
            return CoopCampaignMapTooltipTargetAdmission.IsAdmitted(_lastBridgeSessionId, request,
                _serverCatalog.Find(value => value.EntityId == request.EntityId), _serverDynamic.Find(value => value.EntityId == request.EntityId));
        }
        private static bool ClientTooltipTarget(long now)
        {
            if (!ClientTooltipAvailable(now) || CurrentClientConversation?.IsActive == true || string.IsNullOrEmpty(_tooltipDesired)) return false;
            if (CoopCampaignAftermathInspectionTarget.IsDisclosed(CurrentClientMenu?.Aftermath, _tooltipDesired)) return true;
            if (CoopCampaignAftermathLoadoutTarget.IsDisclosed(CurrentClientMenu?.Aftermath, _tooltipDesired)) return true;
            if (CoopCampaignAftermathTroopTarget.IsDisclosed(CurrentClientMenu?.Aftermath, _tooltipDesired)) return true;
            if (CoopCampaignMapTooltipTargetAdmission.ContainsBarHint(CurrentClientMapBar, _tooltipDesired)) return true;
            if (CoopCampaignMapTooltipTargetAdmission.ContainsPlunder(CurrentClientMenu, _tooltipDesired)) return true;
            if (CoopCampaignMapTooltipTargetAdmission.ContainsEncounter(CurrentClientMenu, _tooltipDesired)) return true;
            if (CoopCampaignMapTooltipTargetAdmission.ContainsSettlementHint(CurrentClientMenu, _tooltipDesired)) return true;
            if (!ClientCatalogById.TryGetValue(_tooltipDesired, out var catalog) || !ClientDynamicById.TryGetValue(_tooltipDesired, out var dynamicState)) return false;
            return dynamicState.IsVisible && catalog.EntityId == _tooltipDesired && dynamicState.EntityId == _tooltipDesired &&
                (catalog.Kind == CoopCampaignMapPrototypeEntityKind.Settlement || CoopCampaignMapPartyBodyPolicy.ShouldRenderBody(catalog.Kind, dynamicState.PartyNameplateFlags));
        }
        private void TickTooltips()
        {
            if (_tooltipFaulted) return;
            try
            {
                if (GameNetwork.IsClient) TickClientTooltips();
                else if (GameNetwork.IsServer) TickServerTooltips();
            }
            catch (Exception ex)
            {
                _tooltipFaulted = true; FinalizeTooltips();
                ModLogger.Info("Campaign map tooltip transport disabled: " + ex.GetType().Name);
            }
        }
        private static void TickClientTooltips()
        {
            long now = TooltipNow;
            if (!ClientTooltipAvailable(now))
            {
                if (_tooltipClientWasAvailable || TooltipInterest.HasGrant || TooltipInterest.PendingRequest != null || CurrentClientTooltip != null) ResetClientTooltipSession();
                _tooltipClientWasAvailable = false;
                return;
            }
            _tooltipClientWasAvailable = true;
            if (!TooltipInterest.HasGrant)
            {
                if (now >= _tooltipNextHello)
                {
                    _tooltipNextHello = now + 1000;
                    SendTooltipRequest(new CoopCampaignMapTooltipWireMessage { Kind = CoopCampaignMapTooltipMessageKind.Hello, Challenge = TooltipHandshake.Challenge });
                }
                return;
            }
            bool disclosed = ClientTooltipTarget(now);
            if (!TooltipInterest.HasCurrentDisplay(disclosed, now)) PublishTooltip(null);
            if (!disclosed) { TooltipAssembler.Clear(); return; }
            // Do not replace an in-flight request every frame/refresh: host relay
            // and chunk latency must get the whole bounded response window.
            if (TooltipInterest.PendingRequest == null && TooltipInterest.TryRequest(_tooltipDesired, _tooltipExtended, true, now, out var request))
            {
                TooltipAssembler.Clear();
                SendTooltipRequest(new CoopCampaignMapTooltipWireMessage { Kind = CoopCampaignMapTooltipMessageKind.Request, Request = request });
            }
        }
        private static void SendTooltipRequest(CoopCampaignMapTooltipWireMessage message)
        {
            var packet = new CoopCampaignMapTooltipRequestMessage(message);
            GameNetwork.BeginModuleEventAsClient();
            try { GameNetwork.WriteMessage(packet); } finally { GameNetwork.EndModuleEventAsClient(); }
        }
        private static void SendTooltipControl(NetworkCommunicator peer, CoopCampaignMapTooltipWireMessage message)
        {
            if (!PeerLive(peer)) return;
            var packet = new CoopCampaignMapTooltipControlMessage(message);
            GameNetwork.BeginModuleEventAsServer(peer);
            try { GameNetwork.WriteMessage(packet); } finally { GameNetwork.EndModuleEventAsServer(); }
        }
        private static void HandleTooltipControl(GameNetworkMessage raw)
        {
            if (_tooltipOwner == null || _tooltipOwner._tooltipFaulted || !(raw is CoopCampaignMapTooltipControlMessage packet)) return;
            var message = packet.Message;
            if (message.Kind == CoopCampaignMapTooltipMessageKind.Revoked)
            { if (message.Lease == TooltipInterest.LeaseId) ResetClientTooltipSession(); return; }
            if (!ClientTooltipAvailable(TooltipNow)) return;
            if (message.Kind == CoopCampaignMapTooltipMessageKind.Grant)
            {
                if (TooltipHandshake.TryAcceptGrant(message)) TooltipInterest.TryGrant(message.Session, message.Lease, TooltipNow);
            }
            else if (message.Kind == CoopCampaignMapTooltipMessageKind.Denied && TooltipInterest.TryDeny(message.Request, ClientTooltipTarget(TooltipNow), TooltipNow))
            { TooltipAssembler.Clear(); PublishTooltip(null); }
        }
        private static void HandleTooltipChunk(GameNetworkMessage raw)
        {
            if (_tooltipOwner == null || _tooltipOwner._tooltipFaulted || !(raw is CoopCampaignMapTooltipChunkMessage packet)) return;
            if (TooltipAssembler.TryAccept(packet.Chunk, TooltipInterest, ClientTooltipTarget(TooltipNow), TooltipNow, out var payload)) PublishTooltip(payload);
        }
        private bool HandleTooltipRequest(NetworkCommunicator peer, GameNetworkMessage raw)
        {
            if (_tooltipFaulted) return true; // A local tooltip fault must not penalize an otherwise valid connected player.
            if (!PeerLive(peer) || !(raw is CoopCampaignMapTooltipRequestMessage packet)) return false;
            try
            {
                long now = TooltipNow;
                if (!ServerTooltipAvailable(now)) return true;
                if (!_tooltipPeers.TryGetValue(peer.Index, out var state) || !ReferenceEquals(state.Peer, peer))
                { if (state != null) RemoveTooltipPeer(state.Peer); state = new TooltipPeer { Peer = peer }; _tooltipPeers[peer.Index] = state; }
                var message = packet.Message;
                if (message.Kind == CoopCampaignMapTooltipMessageKind.Hello)
                {
                    if (now - state.LastControlAt < 500) return true;
                    state.LastControlAt = now;
                    if (state.Gate == null || state.Challenge != message.Challenge || state.Gate.SessionId != _lastBridgeSessionId)
                    {
                        state.Gate?.Revoke(); state.Ticket = null; state.Send = null;
                        state.Challenge = message.Challenge; state.Gate = new CoopCampaignMapTooltipPeerGate(peer, _lastBridgeSessionId);
                    }
                    SendTooltipControl(peer, new CoopCampaignMapTooltipWireMessage { Kind = CoopCampaignMapTooltipMessageKind.Grant,
                        Challenge = state.Challenge, Lease = state.Gate.LeaseId, Session = state.Gate.SessionId });
                    return true;
                }
                var request = message.Request;
                if (request == null) return false;
                if (state.Gate == null || request.LeaseId != state.Gate.LeaseId || request.SessionId != state.Gate.SessionId)
                {
                    if (now - state.LastControlAt >= 500)
                    { state.LastControlAt = now; SendTooltipControl(peer, new CoopCampaignMapTooltipWireMessage { Kind = CoopCampaignMapTooltipMessageKind.Revoked, Lease = request.LeaseId }); }
                    return true;
                }
                long previous = state.Gate.LatestGeneration;
                bool admitted = state.Gate.TryAdmit(peer, request, ServerTooltipTarget(request, now), now, out var ticket);
                state.Ticket = state.Gate.PendingTicket;
                if (state.Gate.LatestGeneration > previous)
                {
                    state.Send = null;
                    if (admitted) state.TicketAt = now;
                    else if (now - state.LastControlAt >= 250)
                    { state.LastControlAt = now; SendTooltipControl(peer, new CoopCampaignMapTooltipWireMessage { Kind = CoopCampaignMapTooltipMessageKind.Denied, Request = request }); }
                }
                return true;
            }
            catch (Exception ex)
            { _tooltipFaulted = true; FinalizeTooltips(); ModLogger.Info("Tooltip request transport disabled: " + ex.GetType().Name); return true; }
        }
        private void RemoveTooltipPeer(NetworkCommunicator peer)
        {
            if (peer == null || !_tooltipPeers.TryGetValue(peer.Index, out var state) || !ReferenceEquals(state.Peer, peer)) return;
            state.Gate?.Revoke(); _tooltipPeers.Remove(peer.Index);
        }
        private void TickServerTooltips()
        {
            long now = TooltipNow;
            if (!ServerTooltipAvailable(now) || _tooltipServerSession != _lastBridgeSessionId)
            {
                foreach (var state in _tooltipPeers.Values)
                    if (state.Gate != null) SendTooltipControl(state.Peer, new CoopCampaignMapTooltipWireMessage { Kind = CoopCampaignMapTooltipMessageKind.Revoked, Lease = state.Gate.LeaseId });
                foreach (var state in _tooltipPeers.Values) state.Gate?.Revoke();
                _tooltipPeers.Clear(); _tooltipRelay?.Revoke(); _tooltipRelay = null; _tooltipInFlight = null;
                _tooltipServerSession = _lastBridgeSessionId;
                if (!ServerTooltipAvailable(now)) return;
            }
            foreach (var state in _tooltipPeers.Values.ToArray()) if (!PeerLive(state.Peer)) RemoveTooltipPeer(state.Peer);
            if (now >= _tooltipNextRelayPoll)
            {
                _tooltipNextRelayPoll = now + 100;
                PumpTooltipRelay(now);
            }
            var peers = _tooltipPeers.Values.OrderBy(value => value.Peer.Index).ToArray();
            int budget = 16;
            for (int n = 0; n < peers.Length && budget > 0; n++)
            {
                var state = peers[(_tooltipSendRoundRobin + n) % peers.Length];
                if (state.Send == null) continue;
                if (now - state.SendAt >= 2000 || !ServerTooltipTarget(state.SendRequest, now)) { state.Send = null; continue; }
                for (int perPeer = 0; perPeer < 2 && budget > 0 && state.NextChunk < state.Send.ChunkCount; perPeer++, budget--)
                {
                    var chunk = CoopCampaignMapTooltipChunks.Create(state.SendRequest.LeaseId, state.SendRequest.Generation,
                        state.SendSequence, state.Send, state.NextChunk++);
                    var packet = new CoopCampaignMapTooltipChunkMessage(chunk);
                    GameNetwork.BeginModuleEventAsServer(state.Peer);
                    try { GameNetwork.WriteMessage(packet); } finally { GameNetwork.EndModuleEventAsServer(); }
                }
                if (state.NextChunk == state.Send.ChunkCount) state.Send = null;
            }
            if (peers.Length > 0) _tooltipSendRoundRobin = (_tooltipSendRoundRobin + 1) % peers.Length;
        }
        private void PumpTooltipRelay(long now)
        {
            if ((_tooltipRelay == null || _tooltipRelay.IsRevoked) && now >= _tooltipNextRelayOpen)
            { _tooltipNextRelayOpen = now + 1000; _tooltipRelay = CoopCampaignMapTooltipRelayClient.TryOpen(); }
            if (_tooltipRelay == null) return;
            if (_tooltipInFlight != null)
            {
                var response = _tooltipRelay.TryReceive(Stopwatch.GetTimestamp(), Stopwatch.Frequency, identity => identity.IsAlive());
                if (response != null)
                {
                    for (int i = 0; i < _tooltipInFlight.Count; i++) CompleteTooltipSubmission(_tooltipInFlight[i], response.Items[i], now);
                    _tooltipInFlight = null;
                }
                else if (now - _tooltipInFlightAt >= 2000 || _tooltipRelay.IsRevoked) _tooltipInFlight = null;
            }
            if (_tooltipInFlight != null) return;
            var candidates = _tooltipPeers.Values.OrderBy(value => value.Peer.Index).ToArray();
            var batch = new List<TooltipSubmission>();
            for (int n = 0; n < candidates.Length && batch.Count < CoopCampaignMapTooltipRelay.MaxItems; n++)
            {
                var state = candidates[(_tooltipRoundRobin + n) % candidates.Length];
                if (state.Ticket == null) continue;
                if (now - state.TicketAt >= 2000 || !ServerTooltipTarget(state.Ticket.Request, now)) { state.Ticket = null; continue; }
                batch.Add(new TooltipSubmission { Owner = state, Ticket = state.Ticket });
            }
            if (candidates.Length > 0) _tooltipRoundRobin = (_tooltipRoundRobin + 1) % candidates.Length;
            if (batch.Count == 0) return;
            if (_tooltipRelay.TrySubmit(batch.Select(value => value.Ticket).ToArray(), Stopwatch.GetTimestamp(), Stopwatch.Frequency, identity => identity.IsAlive()))
            { _tooltipInFlight = batch; _tooltipInFlightAt = now; }
        }
        private void CompleteTooltipSubmission(TooltipSubmission submission, CoopTooltipRelayItem item, long now)
        {
            var state = submission.Owner;
            if (!PeerLive(state.Peer) || !_tooltipPeers.TryGetValue(state.Peer.Index, out var current) || !ReferenceEquals(current, state) ||
                !ReferenceEquals(state.Ticket, submission.Ticket) || !CoopCampaignMapTooltipWireCodec.TryDecode(item.Response, out var response)) return;
            state.Ticket = null;
            bool admitted = ServerTooltipTarget(submission.Ticket.Request, now);
            if (response.Kind == CoopCampaignMapTooltipMessageKind.Rows && state.Gate.TryComplete(state.Peer, submission.Ticket, response.Payload, admitted, now, out _) &&
                CoopCampaignMapCatalogChunkCodec.TryEncode(item.Response, out var chunked, out _))
            {
                state.Send = chunked; state.SendRequest = submission.Ticket.Request; state.SendSequence = submission.Ticket.Sequence;
                state.NextChunk = 0; state.SendAt = state.TicketAt;
            }
            else
            {
                state.Gate.TryComplete(state.Peer, submission.Ticket, null, admitted, now, out _);
                SendTooltipControl(state.Peer, new CoopCampaignMapTooltipWireMessage { Kind = CoopCampaignMapTooltipMessageKind.Denied, Request = submission.Ticket.Request });
            }
        }
    }
}
