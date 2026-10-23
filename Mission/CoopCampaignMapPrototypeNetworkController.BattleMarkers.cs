using System;
using System.Collections.Generic;
using System.Linq;
using CoopSpectator.Infrastructure;
using CoopSpectator.Network.Messages;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.MissionBehaviors
{
    public sealed partial class CoopCampaignMapPrototypeNetworkController
    {
        private static readonly CoopCampaignMapBattleMarkerAssembler BattleMarkersAssembler = new CoopCampaignMapBattleMarkerAssembler();
        public static CoopCampaignMapBattleMarker[] CurrentClientBattleMarkers { get; private set; }
        public static event Action<CoopCampaignMapBattleMarker[]> ClientBattleMarkersChanged;
        internal static bool IsClientBattleMarkersHostFresh => BattleMarkersAssembler.HasCompletedLatest && CurrentClientState != null &&
            _clientStateReceivedAt >= 0 && StateReceiptClock.Elapsed.TotalSeconds - _clientStateReceivedAt <= 2;
        private readonly Dictionary<int, ConversationSendState> _markersSends = new Dictionary<int, ConversationSendState>();
        private byte[] _markersContents;
        private int _markersRevision;
        private CoopCampaignMapCatalogChunkedPayload _markersPayload;

        private static void HandleBattleMarkersChunk(GameNetworkMessage message)
        {
            if (!(message is CoopCampaignMapBattleMarkerChunkMessage part) || !GameNetwork.IsClient) return;
            if (BattleMarkersAssembler.TryAccept(part.Revision, part.LogicalBytes, part.WireBytes,
                part.ChunkCount, part.Compression, part.PayloadHash, part.ChunkIndex, part.PayloadBytes,
                out var completed, out var markers) && completed)
            {
                CurrentClientBattleMarkers = markers;
                ClientBattleMarkersChanged?.Invoke(markers == null ? null : (CoopCampaignMapBattleMarker[])markers.Clone());
            }
        }

        private void UpdateServerBattleMarkers(CoopCampaignMapBattleMarker[] markers, bool force = false)
        {
            // Compare at a fixed revision so map movement alone does not restart sends.
            if (!CoopCampaignMapBattleMarkerCodec.TryEncode(1, markers, out var content))
            { markers = Array.Empty<CoopCampaignMapBattleMarker>(); CoopCampaignMapBattleMarkerCodec.TryEncode(1, markers, out content); }
            if (!force && _markersContents != null && _markersContents.SequenceEqual(content)) return;
            if (_markersRevision == int.MaxValue) return;
            _markersRevision++;
            if (!CoopCampaignMapBattleMarkerCodec.TryEncode(_markersRevision, markers, out var bytes) ||
                !CoopCampaignMapCatalogChunkCodec.TryEncode(bytes, out var prepared, out _)) return;
            _markersContents = content; _markersPayload = prepared;
            // Event removal/disclosure changes cannot wait behind obsolete data.
            _markersSends.Clear();
            if (GameNetwork.NetworkPeers != null)
                foreach (var peer in GameNetwork.NetworkPeers)
                    if (peer != null && !_markersSends.ContainsKey(peer.Index)) SendBattleMarkers(peer);
        }

        private void SendBattleMarkers(NetworkCommunicator peer)
        {
            if (_markersPayload == null || peer == null || peer.IsServerPeer ||
                !peer.IsConnectionActive || !peer.IsSynchronized) return;
            _markersSends[peer.Index] = new ConversationSendState
            { Peer = peer, Revision = _markersRevision, Payload = _markersPayload };
        }

        private void TickBattleMarkersTransports()
        {
            foreach (var send in new List<ConversationSendState>(_markersSends.Values))
            {
                var peer = FindNetworkPeer(send.Peer.Index);
                if (!ReferenceEquals(peer, send.Peer) || !peer.IsConnectionActive || !peer.IsSynchronized)
                { _markersSends.Remove(send.Peer.Index); continue; }
                try
                {
                    // Finish this bounded list before the next bridge poll can
                    // supersede it. Two chunks per tick could starve large lists
                    // indefinitely while host publications keep changing.
                    int chunkBudget = (CoopCampaignMapBattleMarkerCodec.MaximumBytes +
                        CoopCampaignMapCatalogChunkCodec.MaxChunkBytes - 1) /
                        CoopCampaignMapCatalogChunkCodec.MaxChunkBytes;
                    for (int budget = 0; budget < chunkBudget && send.NextChunk < send.Payload.ChunkCount; budget++)
                    {
                        GameNetwork.BeginModuleEventAsServer(peer);
                        GameNetwork.WriteMessage(new CoopCampaignMapBattleMarkerChunkMessage(send.Revision, send.Payload, send.NextChunk));
                        GameNetwork.EndModuleEventAsServer();
                        send.NextChunk++;
                    }
                    if (send.NextChunk == send.Payload.ChunkCount)
                    {
                        _markersSends.Remove(peer.Index);
                        if (send.Revision != _markersRevision) SendBattleMarkers(peer);
                    }
                }
                catch (Exception ex)
                {
                    _markersSends.Remove(peer.Index);
                    ModLogger.Info("BattleMarkers transfer failed: " + ex.GetType().Name);
                }
            }
        }

        private static void ResetClientBattleMarkers()
        { BattleMarkersAssembler.Reset(); CurrentClientBattleMarkers = null; ClientBattleMarkersChanged?.Invoke(null); }

        private void ResetServerBattleMarkers()
        { _markersSends.Clear(); _markersContents = null; _markersPayload = null; _markersRevision = 0; }
    }
}
