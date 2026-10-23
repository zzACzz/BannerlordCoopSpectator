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
        private static readonly CoopCampaignMapInterfaceAssembler MenuAssembler = new CoopCampaignMapInterfaceAssembler();
        public static CoopCampaignMapBarPresentation CurrentClientMapBar { get; private set; }
        public static CoopCampaignMapMenuPresentation CurrentClientMenu { get; private set; }
        public static event Action<CoopCampaignMapMenuPresentation> ClientMenuChanged;
        internal static bool IsClientMenuHostFresh => CurrentClientState != null &&
            _clientStateReceivedAt >= 0 && StateReceiptClock.Elapsed.TotalSeconds - _clientStateReceivedAt <= 2;
        private readonly Dictionary<int, ConversationSendState> _menuSends = new Dictionary<int, ConversationSendState>();
        private byte[] _menuContents;
        private int _menuRevision;
        private string _menuId;
        private bool _hasMapBar;
        private CoopCampaignMapMenuPresentation _serverMenu;
        private CoopCampaignMapBarPresentation _serverMapBar;
        private CoopCampaignMapCatalogChunkedPayload _menuPayload;

        private static void HandleMenuChunk(GameNetworkMessage message)
        {
            if (!(message is CoopCampaignMapMenuChunkMessage part) || !GameNetwork.IsClient) return;
            if (MenuAssembler.TryAccept(part.Revision, part.LogicalBytes, part.WireBytes,
                part.ChunkCount, part.Compression, part.PayloadHash, part.ChunkIndex, part.PayloadBytes,
                out var completed, out var menu, out var bar) && completed)
            {
                CurrentClientMapBar = bar;
                CurrentClientMenu = menu;
                ClientMenuChanged?.Invoke(menu?.Clone());
            }
        }

        private void UpdateServerMenu(CoopCampaignMapMenuPresentation menu, bool force = false,
            CoopCampaignMapBarPresentation bar = null)
        {
            // Compare at a fixed revision so map movement alone does not restart sends.
            if (!CoopCampaignMapInterfaceCodec.TryEncode(1, menu, bar, out var content))
            { menu = null; bar = null; CoopCampaignMapInterfaceCodec.TryEncode(1, null, null, out content); }
            if (!force && _menuContents != null && _menuContents.SequenceEqual(content)) return;
            if (_menuRevision == int.MaxValue) return;
            _menuRevision++;
            if (!CoopCampaignMapInterfaceCodec.TryEncode(_menuRevision, menu, bar, out var bytes) ||
                !CoopCampaignMapCatalogChunkCodec.TryEncode(bytes, out var prepared, out _)) return;
            bool preempt = CoopCampaignMapMenuSendPolicy.ShouldPreempt(_menuId, menu?.MenuId, force) ||
                (_hasMapBar && bar == null);
            _hasMapBar = bar != null;
            _menuContents = content; _menuPayload = prepared; _menuId = menu?.MenuId;
            _serverMenu = menu?.Clone();
            _serverMapBar = bar?.Clone();
            if (preempt) _menuSends.Clear();
            if (GameNetwork.NetworkPeers != null)
                foreach (var peer in GameNetwork.NetworkPeers)
                    if (peer != null && !_menuSends.ContainsKey(peer.Index)) SendMenu(peer);
        }

        private void SendMenu(NetworkCommunicator peer)
        {
            if (_menuPayload == null || peer == null || peer.IsServerPeer ||
                !peer.IsConnectionActive || !peer.IsSynchronized) return;
            _menuSends[peer.Index] = new ConversationSendState
            { Peer = peer, Revision = _menuRevision, Payload = _menuPayload };
        }

        private void TickMenuTransports()
        {
            foreach (var send in new List<ConversationSendState>(_menuSends.Values))
            {
                var peer = FindNetworkPeer(send.Peer.Index);
                if (!ReferenceEquals(peer, send.Peer) || !peer.IsConnectionActive || !peer.IsSynchronized)
                { _menuSends.Remove(send.Peer.Index); continue; }
                try
                {
                    for (int budget = 0; budget < 2 && send.NextChunk < send.Payload.ChunkCount; budget++)
                    {
                        GameNetwork.BeginModuleEventAsServer(peer);
                        GameNetwork.WriteMessage(new CoopCampaignMapMenuChunkMessage(send.Revision, send.Payload, send.NextChunk));
                        GameNetwork.EndModuleEventAsServer();
                        send.NextChunk++;
                    }
                    if (send.NextChunk == send.Payload.ChunkCount)
                    {
                        _menuSends.Remove(peer.Index);
                        if (send.Revision != _menuRevision) SendMenu(peer);
                    }
                }
                catch (Exception ex)
                {
                    _menuSends.Remove(peer.Index);
                    ModLogger.Info("Menu transfer failed: " + ex.GetType().Name);
                }
            }
        }

        private static void ResetClientMenu()
        { MenuAssembler.Reset(); CurrentClientMenu = null; CurrentClientMapBar = null; ClientMenuChanged?.Invoke(null); }

        private void ResetServerMenu()
        { _menuSends.Clear(); _menuContents = null; _menuPayload = null; _menuRevision = 0; _menuId = null; _serverMenu = null; _serverMapBar = null; _hasMapBar = false; }
    }
}
