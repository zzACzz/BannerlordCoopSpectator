using System;
using CoopSpectator.Infrastructure;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.Network.Messages
{
    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopCampaignMapConversationPoseMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer ProtocolCompression = new CompressionInfo.Integer(0, CoopCampaignMapPrototypeContract.ProtocolCompressionMaximum, true);
        public int ProtocolVersion { get; private set; }
        public CoopCampaignMapConversationPose Pose { get; private set; }
        private byte[] _bytes = Array.Empty<byte>();
        public CoopCampaignMapConversationPoseMessage() { }
        public CoopCampaignMapConversationPoseMessage(CoopCampaignMapConversationPose pose)
        {
            ProtocolVersion = CoopCampaignMapPrototypeContract.ProtocolVersion;
            if (!CoopCampaignMapConversationPoseCodec.TrySerialize(pose, out _bytes))
                throw new ArgumentException("Invalid dialogue pose.", nameof(pose));
            Pose = pose.Clone();
        }
        protected override bool OnRead()
        {
            bool valid = true;
            ProtocolVersion = ReadIntFromPacket(ProtocolCompression, ref valid);
            if (!valid || ProtocolVersion != CoopCampaignMapPrototypeContract.ProtocolVersion) return false;
            var buffer = new byte[CoopCampaignMapConversationPoseCodec.MaxPayloadBytes];
            int length = ReadByteArrayFromPacket(buffer, 0, buffer.Length, ref valid);
            if (!valid || ProtocolVersion != CoopCampaignMapPrototypeContract.ProtocolVersion || length <= 0 || length > buffer.Length) return false;
            _bytes = new byte[length];
            Buffer.BlockCopy(buffer, 0, _bytes, 0, length);
            if (!CoopCampaignMapConversationPoseCodec.TryDeserialize(_bytes, out var pose)) return false;
            Pose = pose;
            return true;
        }
        protected override void OnWrite()
        {
            WriteIntToPacket(ProtocolVersion, ProtocolCompression);
            WriteByteArrayToPacket(_bytes, 0, _bytes.Length);
        }
        protected override MultiplayerMessageFilter OnGetLogFilter() => MultiplayerMessageFilter.Mission;
        protected override string OnGetLogFormat() => "CoopCampaignMapConversationPose";
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopCampaignMapConversationChunkMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer ProtocolCompression = new CompressionInfo.Integer(0, CoopCampaignMapPrototypeContract.ProtocolCompressionMaximum, true);
        private static readonly CompressionInfo.Integer RevisionCompression = new CompressionInfo.Integer(0, int.MaxValue, true);
        private static readonly CompressionInfo.Integer SizeCompression = new CompressionInfo.Integer(0, CoopCampaignMapConversationPayloadCodec.MaxPayloadBytes, true);
        private static readonly CompressionInfo.Integer CountCompression = new CompressionInfo.Integer(0, CoopCampaignMapConversationPayloadCodec.MaxChunks, true);
        private static readonly CompressionInfo.Integer KindCompression = new CompressionInfo.Integer(0, 1, true);

        public CoopCampaignMapConversationChunkMessage() { }
        public CoopCampaignMapConversationChunkMessage(int revision, CoopCampaignMapCatalogChunkedPayload payload, int index)
        {
            ProtocolVersion = CoopCampaignMapPrototypeContract.ProtocolVersion;
            Revision = revision;
            LogicalBytes = payload.LogicalByteCount;
            WireBytes = payload.WireByteCount;
            ChunkCount = payload.ChunkCount;
            Compression = payload.CompressionKind;
            PayloadHash = payload.PayloadHash;
            ChunkIndex = index;
            PayloadBytes = payload.Chunks[index];
        }

        public int ProtocolVersion { get; private set; }
        public int Revision { get; private set; }
        public int LogicalBytes { get; private set; }
        public int WireBytes { get; private set; }
        public int ChunkCount { get; private set; }
        public CoopCampaignMapCatalogCompressionKind Compression { get; private set; }
        public string PayloadHash { get; private set; } = string.Empty;
        public int ChunkIndex { get; private set; }
        public byte[] PayloadBytes { get; private set; } = Array.Empty<byte>();

        protected override bool OnRead()
        {
            bool valid = true;
            ProtocolVersion = ReadIntFromPacket(ProtocolCompression, ref valid);
            if (!valid || ProtocolVersion != CoopCampaignMapPrototypeContract.ProtocolVersion) return false;
            Revision = ReadIntFromPacket(RevisionCompression, ref valid);
            LogicalBytes = ReadIntFromPacket(SizeCompression, ref valid);
            WireBytes = ReadIntFromPacket(SizeCompression, ref valid);
            ChunkCount = ReadIntFromPacket(CountCompression, ref valid);
            Compression = (CoopCampaignMapCatalogCompressionKind)ReadIntFromPacket(KindCompression, ref valid);
            PayloadHash = ReadStringFromPacket(ref valid) ?? string.Empty;
            ChunkIndex = ReadIntFromPacket(CountCompression, ref valid);
            var buffer = new byte[CoopCampaignMapCatalogChunkCodec.MaxChunkBytes];
            int length = ReadByteArrayFromPacket(buffer, 0, buffer.Length, ref valid);
            if (length <= 0 || length > buffer.Length) return false;
            PayloadBytes = new byte[length];
            Buffer.BlockCopy(buffer, 0, PayloadBytes, 0, length);
            return valid && ProtocolVersion == CoopCampaignMapPrototypeContract.ProtocolVersion &&
                CoopCampaignMapConversationPayloadCodec.IsValidEnvelope(Revision, LogicalBytes,
                    WireBytes, ChunkCount, Compression, PayloadHash, ChunkIndex, PayloadBytes);
        }

        protected override void OnWrite()
        {
            WriteIntToPacket(ProtocolVersion, ProtocolCompression);
            WriteIntToPacket(Revision, RevisionCompression);
            WriteIntToPacket(LogicalBytes, SizeCompression);
            WriteIntToPacket(WireBytes, SizeCompression);
            WriteIntToPacket(ChunkCount, CountCompression);
            WriteIntToPacket((int)Compression, KindCompression);
            WriteStringToPacket(PayloadHash);
            WriteIntToPacket(ChunkIndex, CountCompression);
            WriteByteArrayToPacket(PayloadBytes, 0, PayloadBytes.Length);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter() => MultiplayerMessageFilter.Mission;
        protected override string OnGetLogFormat() =>
            "CoopCampaignMapConversationChunk Revision=" + Revision + " Index=" + ChunkIndex + " Count=" + ChunkCount;
    }
}
