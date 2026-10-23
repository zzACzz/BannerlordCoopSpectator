using System;
using CoopSpectator.Infrastructure;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.Network.Messages
{
    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopCampaignMapPartyPoseMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer ProtocolCompression =
            new CompressionInfo.Integer(0, CoopCampaignMapPrototypeContract.ProtocolCompressionMaximum, true);
        private byte[] _bytes = Array.Empty<byte>();
        public int ProtocolVersion { get; private set; }
        public CoopCampaignMapPartyPoseEnvelope Envelope { get; private set; }
        public CoopCampaignMapPartyPoseMessage() { }
        public CoopCampaignMapPartyPoseMessage(CoopCampaignMapPartyPoseEnvelope envelope)
        {
            if (!CoopCampaignMapPartyPoseEnvelopeCodec.TryEncode(envelope, out _bytes))
                throw new ArgumentException("Invalid party pose envelope.", nameof(envelope));
            ProtocolVersion = CoopCampaignMapPrototypeContract.ProtocolVersion;
            Envelope = envelope.Clone();
        }
        protected override bool OnRead()
        {
            bool valid = true;
            ProtocolVersion = ReadIntFromPacket(ProtocolCompression, ref valid);
            if (!valid || ProtocolVersion != CoopCampaignMapPrototypeContract.ProtocolVersion) return false;
            var buffer = new byte[CoopCampaignMapPartyPoseEnvelopeCodec.MaximumBytes];
            int length = ReadByteArrayFromPacket(buffer, 0, buffer.Length, ref valid);
            if (!valid || length <= 0 || length > buffer.Length) return false;
            var bytes = new byte[length]; Buffer.BlockCopy(buffer, 0, bytes, 0, length);
            if (!CoopCampaignMapPartyPoseEnvelopeCodec.TryDecode(bytes, out var envelope)) return false;
            _bytes = bytes; Envelope = envelope; return true;
        }
        protected override void OnWrite()
        {
            WriteIntToPacket(ProtocolVersion, ProtocolCompression);
            WriteByteArrayToPacket(_bytes, 0, _bytes.Length);
        }
        protected override MultiplayerMessageFilter OnGetLogFilter() => MultiplayerMessageFilter.Mission;
        protected override string OnGetLogFormat() => "CoopCampaignMapPartyPose";
    }
}
