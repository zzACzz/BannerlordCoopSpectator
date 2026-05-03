using System;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.Network.Messages
{
    public enum CoopBattleSelectionRequestKind
    {
        SelectSide = 0,
        SelectEntry = 1,
        Spectate = 2,
        SpawnNow = 3,
        ForceRespawnable = 4,
        BattleSnapshotReadyAck = 5
    }

    public enum CoopBattlePayloadKind
    {
        EntryStatusSnapshot = 0,
        BattleSnapshot = 1
    }

    public enum CoopBattleSnapshotPayloadEncoding
    {
        JsonUtf8 = 0,
        BinaryV1 = 1
    }

    public enum CoopBattleSnapshotCompressionKind
    {
        None = 0,
        Gzip = 1
    }

    public enum CoopBattleSnapshotAssemblyStateKind
    {
        Receiving = 0,
        Complete = 1,
        Failed = 2,
        Stalled = 3
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromClient)]
    public sealed class CoopBattleSelectionClientRequestMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer RequestKindCompressionInfo = new CompressionInfo.Integer(0, 5, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer BattleSideCompressionInfo = new CompressionInfo.Integer(-1, 1, maximumValueGiven: true);

        public CoopBattleSelectionClientRequestMessage(
            CoopBattleSelectionRequestKind requestKind,
            BattleSideEnum requestedSide,
            string selectionId)
        {
            RequestKind = requestKind;
            RequestedSide = requestedSide;
            SelectionId = string.IsNullOrWhiteSpace(selectionId) ? string.Empty : selectionId.Trim();
        }

        public CoopBattleSelectionClientRequestMessage()
        {
            RequestKind = CoopBattleSelectionRequestKind.SelectSide;
            RequestedSide = BattleSideEnum.None;
            SelectionId = string.Empty;
        }

        public CoopBattleSelectionRequestKind RequestKind { get; private set; }
        public BattleSideEnum RequestedSide { get; private set; }
        public string SelectionId { get; private set; }

        protected override bool OnRead()
        {
            bool bufferReadValid = true;
            RequestKind = (CoopBattleSelectionRequestKind)ReadIntFromPacket(RequestKindCompressionInfo, ref bufferReadValid);
            RequestedSide = (BattleSideEnum)ReadIntFromPacket(BattleSideCompressionInfo, ref bufferReadValid);
            SelectionId = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty;
            return bufferReadValid;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket((int)RequestKind, RequestKindCompressionInfo);
            WriteIntToPacket((int)RequestedSide, BattleSideCompressionInfo);
            WriteStringToPacket(SelectionId ?? string.Empty);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter()
        {
            return MultiplayerMessageFilter.GameMode;
        }

        protected override string OnGetLogFormat()
        {
            return "CoopBattleSelectionClientRequest Kind=" + RequestKind +
                " Side=" + RequestedSide +
                " SelectionId=" + (SelectionId ?? string.Empty);
        }
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopBattlePayloadChunkMessage : GameNetworkMessage
    {
        public const int MaxChunkBytes = 256;
        public const int MaxChunkCount = 8191;

        private static readonly CompressionInfo.Integer PayloadKindCompressionInfo = new CompressionInfo.Integer(0, 1, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer TransmissionCompressionInfo = new CompressionInfo.Integer(0, 1048575, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer ChunkIndexCompressionInfo = new CompressionInfo.Integer(0, MaxChunkCount, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer ChunkCountCompressionInfo = new CompressionInfo.Integer(1, MaxChunkCount, maximumValueGiven: true);
        public CoopBattlePayloadChunkMessage(
            CoopBattlePayloadKind payloadKind,
            int transmissionId,
            int chunkIndex,
            int chunkCount,
            byte[] payloadBytes)
        {
            PayloadKind = payloadKind;
            TransmissionId = transmissionId;
            ChunkIndex = chunkIndex;
            ChunkCount = Math.Max(1, chunkCount);
            PayloadBytes = payloadBytes ?? Array.Empty<byte>();
        }

        public CoopBattlePayloadChunkMessage()
        {
            PayloadKind = CoopBattlePayloadKind.EntryStatusSnapshot;
            TransmissionId = 0;
            ChunkIndex = 0;
            ChunkCount = 1;
            PayloadBytes = Array.Empty<byte>();
        }

        public CoopBattlePayloadKind PayloadKind { get; private set; }
        public int TransmissionId { get; private set; }
        public int ChunkIndex { get; private set; }
        public int ChunkCount { get; private set; }
        public byte[] PayloadBytes { get; private set; }

        protected override bool OnRead()
        {
            bool bufferReadValid = true;
            PayloadKind = (CoopBattlePayloadKind)ReadIntFromPacket(PayloadKindCompressionInfo, ref bufferReadValid);
            TransmissionId = ReadIntFromPacket(TransmissionCompressionInfo, ref bufferReadValid);
            ChunkIndex = ReadIntFromPacket(ChunkIndexCompressionInfo, ref bufferReadValid);
            ChunkCount = ReadIntFromPacket(ChunkCountCompressionInfo, ref bufferReadValid);
            byte[] payloadBuffer = new byte[MaxChunkBytes];
            int bytesRead = ReadByteArrayFromPacket(payloadBuffer, 0, MaxChunkBytes, ref bufferReadValid);
            if (bytesRead <= 0)
            {
                PayloadBytes = Array.Empty<byte>();
            }
            else if (bytesRead == payloadBuffer.Length)
            {
                PayloadBytes = payloadBuffer;
            }
            else
            {
                PayloadBytes = new byte[bytesRead];
                Buffer.BlockCopy(payloadBuffer, 0, PayloadBytes, 0, bytesRead);
            }

            return bufferReadValid;
        }

        protected override void OnWrite()
        {
            byte[] payloadBytes = PayloadBytes ?? Array.Empty<byte>();
            int payloadLength = Math.Min(payloadBytes.Length, MaxChunkBytes);
            WriteIntToPacket((int)PayloadKind, PayloadKindCompressionInfo);
            WriteIntToPacket(TransmissionId, TransmissionCompressionInfo);
            WriteIntToPacket(ChunkIndex, ChunkIndexCompressionInfo);
            WriteIntToPacket(ChunkCount, ChunkCountCompressionInfo);
            if (payloadLength > 0)
                WriteByteArrayToPacket(payloadBytes, 0, payloadLength);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter()
        {
            return MultiplayerMessageFilter.Mission;
        }

        protected override string OnGetLogFormat()
        {
            return "CoopBattlePayloadChunk Kind=" + PayloadKind +
                " TransmissionId=" + TransmissionId +
                " Chunk=" + ChunkIndex + "/" + ChunkCount +
                " Bytes=" + (PayloadBytes?.Length ?? 0);
        }
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopBattleSnapshotManifestMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer TransmissionCompressionInfo = new CompressionInfo.Integer(0, 1048575, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer SchemaVersionCompressionInfo = new CompressionInfo.Integer(0, 255, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer EncodingCompressionInfo = new CompressionInfo.Integer(0, 1, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer CompressionKindCompressionInfo = new CompressionInfo.Integer(0, 1, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer ChunkSizeCompressionInfo = new CompressionInfo.Integer(1, 4096, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer ChunkCountCompressionInfo = new CompressionInfo.Integer(1, CoopBattlePayloadChunkMessage.MaxChunkCount, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer PayloadSizeCompressionInfo = new CompressionInfo.Integer(0, 33554432, maximumValueGiven: true);

        public CoopBattleSnapshotManifestMessage(
            int transmissionId,
            int schemaVersion,
            CoopBattleSnapshotPayloadEncoding payloadEncoding,
            CoopBattleSnapshotCompressionKind compressionKind,
            int logicalBytes,
            int wireBytes,
            int chunkSize,
            int chunkCount,
            string comparisonKey,
            string payloadHash)
        {
            TransmissionId = transmissionId;
            SchemaVersion = schemaVersion;
            PayloadEncoding = payloadEncoding;
            CompressionKind = compressionKind;
            LogicalBytes = logicalBytes;
            WireBytes = wireBytes;
            ChunkSize = chunkSize;
            ChunkCount = chunkCount;
            ComparisonKey = string.IsNullOrWhiteSpace(comparisonKey) ? string.Empty : comparisonKey.Trim();
            PayloadHash = string.IsNullOrWhiteSpace(payloadHash) ? string.Empty : payloadHash.Trim();
        }

        public CoopBattleSnapshotManifestMessage()
        {
            TransmissionId = 0;
            SchemaVersion = 1;
            PayloadEncoding = CoopBattleSnapshotPayloadEncoding.JsonUtf8;
            CompressionKind = CoopBattleSnapshotCompressionKind.None;
            LogicalBytes = 0;
            WireBytes = 0;
            ChunkSize = CoopBattlePayloadChunkMessage.MaxChunkBytes;
            ChunkCount = 1;
            ComparisonKey = string.Empty;
            PayloadHash = string.Empty;
        }

        public int TransmissionId { get; private set; }
        public int SchemaVersion { get; private set; }
        public CoopBattleSnapshotPayloadEncoding PayloadEncoding { get; private set; }
        public CoopBattleSnapshotCompressionKind CompressionKind { get; private set; }
        public int LogicalBytes { get; private set; }
        public int WireBytes { get; private set; }
        public int ChunkSize { get; private set; }
        public int ChunkCount { get; private set; }
        public string ComparisonKey { get; private set; }
        public string PayloadHash { get; private set; }

        protected override bool OnRead()
        {
            bool bufferReadValid = true;
            TransmissionId = ReadIntFromPacket(TransmissionCompressionInfo, ref bufferReadValid);
            SchemaVersion = ReadIntFromPacket(SchemaVersionCompressionInfo, ref bufferReadValid);
            PayloadEncoding = (CoopBattleSnapshotPayloadEncoding)ReadIntFromPacket(EncodingCompressionInfo, ref bufferReadValid);
            CompressionKind = (CoopBattleSnapshotCompressionKind)ReadIntFromPacket(CompressionKindCompressionInfo, ref bufferReadValid);
            LogicalBytes = ReadIntFromPacket(PayloadSizeCompressionInfo, ref bufferReadValid);
            WireBytes = ReadIntFromPacket(PayloadSizeCompressionInfo, ref bufferReadValid);
            ChunkSize = ReadIntFromPacket(ChunkSizeCompressionInfo, ref bufferReadValid);
            ChunkCount = ReadIntFromPacket(ChunkCountCompressionInfo, ref bufferReadValid);
            ComparisonKey = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty;
            PayloadHash = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty;
            return bufferReadValid;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket(TransmissionId, TransmissionCompressionInfo);
            WriteIntToPacket(SchemaVersion, SchemaVersionCompressionInfo);
            WriteIntToPacket((int)PayloadEncoding, EncodingCompressionInfo);
            WriteIntToPacket((int)CompressionKind, CompressionKindCompressionInfo);
            WriteIntToPacket(LogicalBytes, PayloadSizeCompressionInfo);
            WriteIntToPacket(WireBytes, PayloadSizeCompressionInfo);
            WriteIntToPacket(ChunkSize, ChunkSizeCompressionInfo);
            WriteIntToPacket(ChunkCount, ChunkCountCompressionInfo);
            WriteStringToPacket(ComparisonKey ?? string.Empty);
            WriteStringToPacket(PayloadHash ?? string.Empty);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter()
        {
            return MultiplayerMessageFilter.Mission;
        }

        protected override string OnGetLogFormat()
        {
            return "CoopBattleSnapshotManifest TransmissionId=" + TransmissionId +
                " SchemaVersion=" + SchemaVersion +
                " Encoding=" + PayloadEncoding +
                " Compression=" + CompressionKind +
                " LogicalBytes=" + LogicalBytes +
                " WireBytes=" + WireBytes +
                " ChunkSize=" + ChunkSize +
                " ChunkCount=" + ChunkCount;
        }
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopBattleSnapshotChunkV2Message : GameNetworkMessage
    {
        public const int MaxChunkBytes = 256;
        public const int MaxChunkCount = 8191;

        private static readonly CompressionInfo.Integer TransmissionCompressionInfo = new CompressionInfo.Integer(0, 1048575, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer ChunkIndexCompressionInfo = new CompressionInfo.Integer(0, MaxChunkCount, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer ChunkCountCompressionInfo = new CompressionInfo.Integer(1, MaxChunkCount, maximumValueGiven: true);

        public CoopBattleSnapshotChunkV2Message(
            int transmissionId,
            int chunkIndex,
            int chunkCount,
            byte[] payloadBytes)
        {
            TransmissionId = transmissionId;
            ChunkIndex = chunkIndex;
            ChunkCount = Math.Max(1, chunkCount);
            PayloadBytes = payloadBytes ?? Array.Empty<byte>();
        }

        public CoopBattleSnapshotChunkV2Message()
        {
            TransmissionId = 0;
            ChunkIndex = 0;
            ChunkCount = 1;
            PayloadBytes = Array.Empty<byte>();
        }

        public int TransmissionId { get; private set; }
        public int ChunkIndex { get; private set; }
        public int ChunkCount { get; private set; }
        public byte[] PayloadBytes { get; private set; }

        protected override bool OnRead()
        {
            bool bufferReadValid = true;
            TransmissionId = ReadIntFromPacket(TransmissionCompressionInfo, ref bufferReadValid);
            ChunkIndex = ReadIntFromPacket(ChunkIndexCompressionInfo, ref bufferReadValid);
            ChunkCount = ReadIntFromPacket(ChunkCountCompressionInfo, ref bufferReadValid);
            byte[] payloadBuffer = new byte[MaxChunkBytes];
            int bytesRead = ReadByteArrayFromPacket(payloadBuffer, 0, MaxChunkBytes, ref bufferReadValid);
            if (bytesRead <= 0)
            {
                PayloadBytes = Array.Empty<byte>();
            }
            else if (bytesRead == payloadBuffer.Length)
            {
                PayloadBytes = payloadBuffer;
            }
            else
            {
                PayloadBytes = new byte[bytesRead];
                Buffer.BlockCopy(payloadBuffer, 0, PayloadBytes, 0, bytesRead);
            }

            return bufferReadValid;
        }

        protected override void OnWrite()
        {
            byte[] payloadBytes = PayloadBytes ?? Array.Empty<byte>();
            int payloadLength = Math.Min(payloadBytes.Length, MaxChunkBytes);
            WriteIntToPacket(TransmissionId, TransmissionCompressionInfo);
            WriteIntToPacket(ChunkIndex, ChunkIndexCompressionInfo);
            WriteIntToPacket(ChunkCount, ChunkCountCompressionInfo);
            if (payloadLength > 0)
                WriteByteArrayToPacket(payloadBytes, 0, payloadLength);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter()
        {
            return MultiplayerMessageFilter.Mission;
        }

        protected override string OnGetLogFormat()
        {
            return "CoopBattleSnapshotChunkV2 TransmissionId=" + TransmissionId +
                " Chunk=" + ChunkIndex + "/" + ChunkCount +
                " Bytes=" + (PayloadBytes?.Length ?? 0);
        }
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromClient)]
    public sealed class CoopBattleSnapshotRangeAckMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer TransmissionCompressionInfo = new CompressionInfo.Integer(0, 1048575, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer ChunkIndexCompressionInfo = new CompressionInfo.Integer(-1, CoopBattleSnapshotChunkV2Message.MaxChunkCount, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer ChunkCountCompressionInfo = new CompressionInfo.Integer(0, CoopBattleSnapshotChunkV2Message.MaxChunkCount, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer AssemblyStateCompressionInfo = new CompressionInfo.Integer(0, 3, maximumValueGiven: true);

        public CoopBattleSnapshotRangeAckMessage(
            int transmissionId,
            int highestContiguousChunkIndex,
            int receivedChunkCount,
            string receivedRanges,
            string missingRanges,
            CoopBattleSnapshotAssemblyStateKind assemblyState)
        {
            TransmissionId = transmissionId;
            HighestContiguousChunkIndex = highestContiguousChunkIndex;
            ReceivedChunkCount = receivedChunkCount;
            ReceivedRanges = string.IsNullOrWhiteSpace(receivedRanges) ? string.Empty : receivedRanges.Trim();
            MissingRanges = string.IsNullOrWhiteSpace(missingRanges) ? string.Empty : missingRanges.Trim();
            AssemblyState = assemblyState;
        }

        public CoopBattleSnapshotRangeAckMessage()
        {
            TransmissionId = 0;
            HighestContiguousChunkIndex = -1;
            ReceivedChunkCount = 0;
            ReceivedRanges = string.Empty;
            MissingRanges = string.Empty;
            AssemblyState = CoopBattleSnapshotAssemblyStateKind.Receiving;
        }

        public int TransmissionId { get; private set; }
        public int HighestContiguousChunkIndex { get; private set; }
        public int ReceivedChunkCount { get; private set; }
        public string ReceivedRanges { get; private set; }
        public string MissingRanges { get; private set; }
        public CoopBattleSnapshotAssemblyStateKind AssemblyState { get; private set; }

        protected override bool OnRead()
        {
            bool bufferReadValid = true;
            TransmissionId = ReadIntFromPacket(TransmissionCompressionInfo, ref bufferReadValid);
            HighestContiguousChunkIndex = ReadIntFromPacket(ChunkIndexCompressionInfo, ref bufferReadValid);
            ReceivedChunkCount = ReadIntFromPacket(ChunkCountCompressionInfo, ref bufferReadValid);
            ReceivedRanges = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty;
            MissingRanges = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty;
            AssemblyState = (CoopBattleSnapshotAssemblyStateKind)ReadIntFromPacket(AssemblyStateCompressionInfo, ref bufferReadValid);
            return bufferReadValid;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket(TransmissionId, TransmissionCompressionInfo);
            WriteIntToPacket(HighestContiguousChunkIndex, ChunkIndexCompressionInfo);
            WriteIntToPacket(ReceivedChunkCount, ChunkCountCompressionInfo);
            WriteStringToPacket(ReceivedRanges ?? string.Empty);
            WriteStringToPacket(MissingRanges ?? string.Empty);
            WriteIntToPacket((int)AssemblyState, AssemblyStateCompressionInfo);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter()
        {
            return MultiplayerMessageFilter.Mission;
        }

        protected override string OnGetLogFormat()
        {
            return "CoopBattleSnapshotRangeAck TransmissionId=" + TransmissionId +
                " HighestContiguous=" + HighestContiguousChunkIndex +
                " ReceivedChunkCount=" + ReceivedChunkCount +
                " State=" + AssemblyState;
        }
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromClient)]
    public sealed class CoopBattleSnapshotCompleteAckMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer TransmissionCompressionInfo = new CompressionInfo.Integer(0, 1048575, maximumValueGiven: true);

        public CoopBattleSnapshotCompleteAckMessage(int transmissionId, bool appliedSuccessfully, string payloadHash)
        {
            TransmissionId = transmissionId;
            AppliedSuccessfully = appliedSuccessfully;
            PayloadHash = string.IsNullOrWhiteSpace(payloadHash) ? string.Empty : payloadHash.Trim();
        }

        public CoopBattleSnapshotCompleteAckMessage()
        {
            TransmissionId = 0;
            AppliedSuccessfully = false;
            PayloadHash = string.Empty;
        }

        public int TransmissionId { get; private set; }
        public bool AppliedSuccessfully { get; private set; }
        public string PayloadHash { get; private set; }

        protected override bool OnRead()
        {
            bool bufferReadValid = true;
            TransmissionId = ReadIntFromPacket(TransmissionCompressionInfo, ref bufferReadValid);
            AppliedSuccessfully = ReadBoolFromPacket(ref bufferReadValid);
            PayloadHash = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty;
            return bufferReadValid;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket(TransmissionId, TransmissionCompressionInfo);
            WriteBoolToPacket(AppliedSuccessfully);
            WriteStringToPacket(PayloadHash ?? string.Empty);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter()
        {
            return MultiplayerMessageFilter.Mission;
        }

        protected override string OnGetLogFormat()
        {
            return "CoopBattleSnapshotCompleteAck TransmissionId=" + TransmissionId +
                " AppliedSuccessfully=" + AppliedSuccessfully;
        }
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromClient)]
    public sealed class CoopBattleSnapshotAbortMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer TransmissionCompressionInfo = new CompressionInfo.Integer(0, 1048575, maximumValueGiven: true);

        public CoopBattleSnapshotAbortMessage(int transmissionId, string reason)
        {
            TransmissionId = transmissionId;
            Reason = string.IsNullOrWhiteSpace(reason) ? string.Empty : reason.Trim();
        }

        public CoopBattleSnapshotAbortMessage()
        {
            TransmissionId = 0;
            Reason = string.Empty;
        }

        public int TransmissionId { get; private set; }
        public string Reason { get; private set; }

        protected override bool OnRead()
        {
            bool bufferReadValid = true;
            TransmissionId = ReadIntFromPacket(TransmissionCompressionInfo, ref bufferReadValid);
            Reason = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty;
            return bufferReadValid;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket(TransmissionId, TransmissionCompressionInfo);
            WriteStringToPacket(Reason ?? string.Empty);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter()
        {
            return MultiplayerMessageFilter.Mission;
        }

        protected override string OnGetLogFormat()
        {
            return "CoopBattleSnapshotAbort TransmissionId=" + TransmissionId +
                " Reason=" + (Reason ?? string.Empty);
        }
    }
}
