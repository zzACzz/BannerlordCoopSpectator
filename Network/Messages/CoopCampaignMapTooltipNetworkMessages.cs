using System;
using CoopSpectator.Infrastructure;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.Network.Messages
{
    public abstract class CoopCampaignMapTooltipControlBase : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer Protocol = new CompressionInfo.Integer(0, 31, true);
        private byte[] _bytes = Array.Empty<byte>();
        protected abstract bool FromClient { get; }
        public CoopCampaignMapTooltipWireMessage Message { get; private set; }
        protected CoopCampaignMapTooltipControlBase() { }
        protected CoopCampaignMapTooltipControlBase(CoopCampaignMapTooltipWireMessage message)
        {
            if (!Allowed(message?.Kind ?? 0) || !CoopCampaignMapTooltipWireCodec.TryEncode(message, out _bytes) ||
                _bytes.Length > CoopCampaignMapTooltipWireCodec.MaxControlBytes) throw new ArgumentException("Invalid tooltip control.");
            Message = message;
        }
        private bool Allowed(CoopCampaignMapTooltipMessageKind kind) => FromClient
            ? CoopCampaignMapTooltipWireCodec.IsClientMessage(kind)
            : CoopCampaignMapTooltipWireCodec.IsServerMessage(kind) && kind != CoopCampaignMapTooltipMessageKind.Rows;
        protected override bool OnRead()
        {
            bool valid = true;
            int protocol = ReadIntFromPacket(Protocol, ref valid);
            var buffer = new byte[CoopCampaignMapTooltipWireCodec.MaxControlBytes];
            int length = ReadByteArrayFromPacket(buffer, 0, buffer.Length, ref valid);
            if (!valid || protocol != CoopCampaignMapPrototypeContract.ProtocolVersion || length < 1 || length > buffer.Length) return false;
            _bytes = new byte[length]; Buffer.BlockCopy(buffer, 0, _bytes, 0, length);
            if (!CoopCampaignMapTooltipWireCodec.TryDecode(_bytes, out var message) || !Allowed(message.Kind)) return false;
            Message = message; return true;
        }
        protected override void OnWrite()
        { WriteIntToPacket(CoopCampaignMapPrototypeContract.ProtocolVersion, Protocol); WriteByteArrayToPacket(_bytes, 0, _bytes.Length); }
        protected override MultiplayerMessageFilter OnGetLogFilter() => MultiplayerMessageFilter.Mission;
        protected override string OnGetLogFormat() => "CoopMapTooltipControl " + Message?.Kind;
    }
    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromClient)]
    public sealed class CoopCampaignMapTooltipRequestMessage : CoopCampaignMapTooltipControlBase
    {
        protected override bool FromClient => true;
        public CoopCampaignMapTooltipRequestMessage() { }
        public CoopCampaignMapTooltipRequestMessage(CoopCampaignMapTooltipWireMessage message) : base(message) { }
    }
    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopCampaignMapTooltipControlMessage : CoopCampaignMapTooltipControlBase
    {
        protected override bool FromClient => false;
        public CoopCampaignMapTooltipControlMessage() { }
        public CoopCampaignMapTooltipControlMessage(CoopCampaignMapTooltipWireMessage message) : base(message) { }
    }
    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopCampaignMapTooltipChunkMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer Protocol = new CompressionInfo.Integer(0, 31, true);
        private byte[] _bytes = Array.Empty<byte>();
        public CoopCampaignMapTooltipChunk Chunk { get; private set; }
        public CoopCampaignMapTooltipChunkMessage() { }
        public CoopCampaignMapTooltipChunkMessage(CoopCampaignMapTooltipChunk chunk)
        {
            if (!CoopCampaignMapTooltipChunks.TryEncode(chunk, out _bytes)) throw new ArgumentException("Invalid tooltip chunk.");
            Chunk = chunk;
        }
        protected override bool OnRead()
        {
            bool valid = true;
            int protocol = ReadIntFromPacket(Protocol, ref valid);
            var buffer = new byte[CoopCampaignMapTooltipChunks.MaxPacketBytes];
            int length = ReadByteArrayFromPacket(buffer, 0, buffer.Length, ref valid);
            if (!valid || protocol != CoopCampaignMapPrototypeContract.ProtocolVersion || length < 1 || length > buffer.Length) return false;
            _bytes = new byte[length]; Buffer.BlockCopy(buffer, 0, _bytes, 0, length);
            if (!CoopCampaignMapTooltipChunks.TryDecode(_bytes, out var chunk)) return false;
            Chunk = chunk; return true;
        }
        protected override void OnWrite()
        { WriteIntToPacket(CoopCampaignMapPrototypeContract.ProtocolVersion, Protocol); WriteByteArrayToPacket(_bytes, 0, _bytes.Length); }
        protected override MultiplayerMessageFilter OnGetLogFilter() => MultiplayerMessageFilter.Mission;
        protected override string OnGetLogFormat() => "CoopMapTooltipChunk";
    }
}
