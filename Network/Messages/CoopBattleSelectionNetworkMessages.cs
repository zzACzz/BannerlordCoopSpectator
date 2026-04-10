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
        ForceRespawnable = 4
    }

    public enum CoopBattlePayloadKind
    {
        EntryStatusSnapshot = 0,
        BattleSnapshot = 1
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromClient)]
    public sealed class CoopBattleSelectionClientRequestMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer RequestKindCompressionInfo = new CompressionInfo.Integer(0, 4, maximumValueGiven: true);
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
        public const int MaxChunkBytes = 896;

        private static readonly CompressionInfo.Integer PayloadKindCompressionInfo = new CompressionInfo.Integer(0, 1, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer TransmissionCompressionInfo = new CompressionInfo.Integer(0, 1048575, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer ChunkIndexCompressionInfo = new CompressionInfo.Integer(0, 255, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer ChunkCountCompressionInfo = new CompressionInfo.Integer(1, 255, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer ChunkLengthCompressionInfo = new CompressionInfo.Integer(0, MaxChunkBytes, maximumValueGiven: true);

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
            int payloadLength = ReadIntFromPacket(ChunkLengthCompressionInfo, ref bufferReadValid);

            PayloadBytes = payloadLength > 0 ? new byte[payloadLength] : Array.Empty<byte>();
            if (payloadLength > 0)
            {
                int bytesRead = ReadByteArrayFromPacket(PayloadBytes, 0, payloadLength, ref bufferReadValid);
                bufferReadValid = bufferReadValid && bytesRead == payloadLength;
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
            WriteIntToPacket(payloadLength, ChunkLengthCompressionInfo);
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
}
