using System;
using CoopSpectator.Infrastructure;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.Network.Messages
{
    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromClient)]
    public sealed class CoopHeroCreationClientCommandMessage : GameNetworkMessage
    {
        public const int MaxSubmissionIdCharacters = 128;
        public const int MaxHashCharacters = 64;
        public const int MaxTransferId = 1048575;

        private static readonly CompressionInfo.Integer CommandCompression = new CompressionInfo.Integer(0, 2, true);
        private static readonly CompressionInfo.Integer RevisionCompression = new CompressionInfo.Integer(0, int.MaxValue, true);
        private static readonly CompressionInfo.Integer TransferCompression = new CompressionInfo.Integer(0, MaxTransferId, true);
        private static readonly CompressionInfo.Integer LogicalByteCountCompression =
            new CompressionInfo.Integer(0, CoopHeroCreationChunkCodec.MaxLogicalBytes, true);
        private static readonly CompressionInfo.Integer ChunkCountCompression =
            new CompressionInfo.Integer(0, CoopHeroCreationChunkCodec.MaxChunkCount, true);

        public CoopHeroCreationClientCommandMessage(
            int commandKind,
            int revision,
            int transferId,
            int logicalByteCount,
            int chunkCount,
            string submissionId,
            string payloadHash,
            string transportHash)
        {
            CommandKind = commandKind;
            Revision = revision;
            TransferId = transferId;
            LogicalByteCount = logicalByteCount;
            ChunkCount = chunkCount;
            SubmissionId = submissionId ?? string.Empty;
            PayloadHash = payloadHash ?? string.Empty;
            TransportHash = transportHash ?? string.Empty;
        }

        public CoopHeroCreationClientCommandMessage() { }

        public int CommandKind { get; private set; }
        public int Revision { get; private set; }
        public int TransferId { get; private set; }
        public int LogicalByteCount { get; private set; }
        public int ChunkCount { get; private set; }
        public string SubmissionId { get; private set; }
        public string PayloadHash { get; private set; }
        public string TransportHash { get; private set; }

        protected override bool OnRead()
        {
            bool valid = true;
            CommandKind = ReadIntFromPacket(CommandCompression, ref valid);
            Revision = ReadIntFromPacket(RevisionCompression, ref valid);
            TransferId = ReadIntFromPacket(TransferCompression, ref valid);
            LogicalByteCount = ReadIntFromPacket(LogicalByteCountCompression, ref valid);
            ChunkCount = ReadIntFromPacket(ChunkCountCompression, ref valid);
            SubmissionId = ReadStringFromPacket(ref valid) ?? string.Empty;
            PayloadHash = ReadStringFromPacket(ref valid) ?? string.Empty;
            TransportHash = ReadStringFromPacket(ref valid) ?? string.Empty;
            return valid;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket(CommandKind, CommandCompression);
            WriteIntToPacket(Revision, RevisionCompression);
            WriteIntToPacket(TransferId, TransferCompression);
            WriteIntToPacket(LogicalByteCount, LogicalByteCountCompression);
            WriteIntToPacket(ChunkCount, ChunkCountCompression);
            WriteStringToPacket(BoundString(SubmissionId, MaxSubmissionIdCharacters));
            WriteStringToPacket(BoundString(PayloadHash, MaxHashCharacters));
            WriteStringToPacket(BoundString(TransportHash, MaxHashCharacters));
        }

        protected override MultiplayerMessageFilter OnGetLogFilter() => MultiplayerMessageFilter.GameMode;
        protected override string OnGetLogFormat() =>
            "CoopHeroCreationClientCommand Kind=" + CommandKind +
            " Revision=" + Revision +
            " TransferId=" + TransferId +
            " ChunkCount=" + ChunkCount;

        private static string BoundString(string value, int maximumCharacters)
        {
            string safe = value ?? string.Empty;
            return safe.Length <= maximumCharacters ? safe : safe.Substring(0, maximumCharacters);
        }
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromClient)]
    public sealed class CoopHeroCreationClientPayloadChunkMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer TransferCompression =
            new CompressionInfo.Integer(0, CoopHeroCreationClientCommandMessage.MaxTransferId, true);
        private static readonly CompressionInfo.Integer ChunkIndexCompression =
            new CompressionInfo.Integer(0, CoopHeroCreationChunkCodec.MaxChunkCount, true);
        private static readonly CompressionInfo.Integer ChunkCountCompression =
            new CompressionInfo.Integer(1, CoopHeroCreationChunkCodec.MaxChunkCount, true);

        public CoopHeroCreationClientPayloadChunkMessage(int transferId, int chunkIndex, int chunkCount, byte[] payloadBytes)
        {
            TransferId = transferId;
            ChunkIndex = chunkIndex;
            ChunkCount = Math.Max(1, chunkCount);
            PayloadBytes = payloadBytes ?? Array.Empty<byte>();
        }

        public CoopHeroCreationClientPayloadChunkMessage()
        {
            ChunkCount = 1;
            PayloadBytes = Array.Empty<byte>();
        }

        public int TransferId { get; private set; }
        public int ChunkIndex { get; private set; }
        public int ChunkCount { get; private set; }
        public byte[] PayloadBytes { get; private set; }

        protected override bool OnRead()
        {
            bool valid = true;
            TransferId = ReadIntFromPacket(TransferCompression, ref valid);
            ChunkIndex = ReadIntFromPacket(ChunkIndexCompression, ref valid);
            ChunkCount = ReadIntFromPacket(ChunkCountCompression, ref valid);
            PayloadBytes = ReadBoundedPayload(ref valid);
            return valid;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket(TransferId, TransferCompression);
            WriteIntToPacket(ChunkIndex, ChunkIndexCompression);
            WriteIntToPacket(ChunkCount, ChunkCountCompression);
            WriteBoundedPayload(PayloadBytes);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter() => MultiplayerMessageFilter.GameMode;
        protected override string OnGetLogFormat() =>
            "CoopHeroCreationClientPayloadChunk TransferId=" + TransferId +
            " Chunk=" + ChunkIndex + "/" + ChunkCount;

        private static byte[] ReadBoundedPayload(ref bool valid)
        {
            byte[] buffer = new byte[CoopHeroCreationChunkCodec.MaxChunkBytes];
            int bytesRead = ReadByteArrayFromPacket(buffer, 0, buffer.Length, ref valid);
            if (bytesRead <= 0) return Array.Empty<byte>();
            if (bytesRead == buffer.Length) return buffer;
            byte[] payload = new byte[bytesRead];
            Buffer.BlockCopy(buffer, 0, payload, 0, bytesRead);
            return payload;
        }

        private static void WriteBoundedPayload(byte[] payloadBytes)
        {
            byte[] safe = payloadBytes ?? Array.Empty<byte>();
            int length = Math.Min(safe.Length, CoopHeroCreationChunkCodec.MaxChunkBytes);
            if (length > 0) WriteByteArrayToPacket(safe, 0, length);
        }
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopHeroCreationServerEnvelopeMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer TransferCompression =
            new CompressionInfo.Integer(0, CoopHeroCreationClientCommandMessage.MaxTransferId, true);
        private static readonly CompressionInfo.Integer LogicalByteCountCompression =
            new CompressionInfo.Integer(0, CoopHeroCreationChunkCodec.MaxLogicalBytes, true);
        private static readonly CompressionInfo.Integer ChunkCountCompression =
            new CompressionInfo.Integer(1, CoopHeroCreationChunkCodec.MaxChunkCount, true);

        public CoopHeroCreationServerEnvelopeMessage(
            int transferId,
            int logicalByteCount,
            int chunkCount,
            string transportHash)
        {
            TransferId = transferId;
            LogicalByteCount = logicalByteCount;
            ChunkCount = Math.Max(1, chunkCount);
            TransportHash = transportHash ?? string.Empty;
        }

        public CoopHeroCreationServerEnvelopeMessage()
        {
            ChunkCount = 1;
        }

        public int TransferId { get; private set; }
        public int LogicalByteCount { get; private set; }
        public int ChunkCount { get; private set; }
        public string TransportHash { get; private set; }

        protected override bool OnRead()
        {
            bool valid = true;
            TransferId = ReadIntFromPacket(TransferCompression, ref valid);
            LogicalByteCount = ReadIntFromPacket(LogicalByteCountCompression, ref valid);
            ChunkCount = ReadIntFromPacket(ChunkCountCompression, ref valid);
            TransportHash = ReadStringFromPacket(ref valid) ?? string.Empty;
            return valid;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket(TransferId, TransferCompression);
            WriteIntToPacket(LogicalByteCount, LogicalByteCountCompression);
            WriteIntToPacket(ChunkCount, ChunkCountCompression);
            string safeHash = TransportHash ?? string.Empty;
            WriteStringToPacket(safeHash.Length <= 64 ? safeHash : safeHash.Substring(0, 64));
        }

        protected override MultiplayerMessageFilter OnGetLogFilter() => MultiplayerMessageFilter.GameMode;
        protected override string OnGetLogFormat() =>
            "CoopHeroCreationServerEnvelope TransferId=" + TransferId +
            " ChunkCount=" + ChunkCount;
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopHeroCreationServerEnvelopeChunkMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer TransferCompression =
            new CompressionInfo.Integer(0, CoopHeroCreationClientCommandMessage.MaxTransferId, true);
        private static readonly CompressionInfo.Integer ChunkIndexCompression =
            new CompressionInfo.Integer(0, CoopHeroCreationChunkCodec.MaxChunkCount, true);
        private static readonly CompressionInfo.Integer ChunkCountCompression =
            new CompressionInfo.Integer(1, CoopHeroCreationChunkCodec.MaxChunkCount, true);

        public CoopHeroCreationServerEnvelopeChunkMessage(int transferId, int chunkIndex, int chunkCount, byte[] payloadBytes)
        {
            TransferId = transferId;
            ChunkIndex = chunkIndex;
            ChunkCount = Math.Max(1, chunkCount);
            PayloadBytes = payloadBytes ?? Array.Empty<byte>();
        }

        public CoopHeroCreationServerEnvelopeChunkMessage()
        {
            ChunkCount = 1;
            PayloadBytes = Array.Empty<byte>();
        }

        public int TransferId { get; private set; }
        public int ChunkIndex { get; private set; }
        public int ChunkCount { get; private set; }
        public byte[] PayloadBytes { get; private set; }

        protected override bool OnRead()
        {
            bool valid = true;
            TransferId = ReadIntFromPacket(TransferCompression, ref valid);
            ChunkIndex = ReadIntFromPacket(ChunkIndexCompression, ref valid);
            ChunkCount = ReadIntFromPacket(ChunkCountCompression, ref valid);
            byte[] buffer = new byte[CoopHeroCreationChunkCodec.MaxChunkBytes];
            int bytesRead = ReadByteArrayFromPacket(buffer, 0, buffer.Length, ref valid);
            if (bytesRead <= 0)
                PayloadBytes = Array.Empty<byte>();
            else if (bytesRead == buffer.Length)
                PayloadBytes = buffer;
            else
            {
                PayloadBytes = new byte[bytesRead];
                Buffer.BlockCopy(buffer, 0, PayloadBytes, 0, bytesRead);
            }
            return valid;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket(TransferId, TransferCompression);
            WriteIntToPacket(ChunkIndex, ChunkIndexCompression);
            WriteIntToPacket(ChunkCount, ChunkCountCompression);
            byte[] safe = PayloadBytes ?? Array.Empty<byte>();
            int length = Math.Min(safe.Length, CoopHeroCreationChunkCodec.MaxChunkBytes);
            if (length > 0) WriteByteArrayToPacket(safe, 0, length);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter() => MultiplayerMessageFilter.GameMode;
        protected override string OnGetLogFormat() =>
            "CoopHeroCreationServerEnvelopeChunk TransferId=" + TransferId +
            " Chunk=" + ChunkIndex + "/" + ChunkCount;
    }
}
