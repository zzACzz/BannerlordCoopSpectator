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
        BattleSnapshotReadyAck = 5,
        BattleSnapshotBootstrapRequest = 6,
        BattleReconnectFinalizeReadyAck = 7,
        BeginCommanderDeployment = 8,
        AutoDeployCommanderDeployment = 9,
        FinishCommanderDeployment = 10,
        QueueSpawnAfterDeployment = 11
    }

    public enum CoopBattlePayloadKind
    {
        EntryStatusSnapshot = 0,
        BattleSnapshot = 1,
        AuthoritativeMaterializedAgentEntrySnapshot = 2
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
        private static readonly CompressionInfo.Integer RequestKindCompressionInfo = new CompressionInfo.Integer(0, 11, maximumValueGiven: true);
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

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromClient)]
    public sealed class CoopCommanderDeploymentFormationAssignmentsMessage : GameNetworkMessage
    {
        public const int BytesPerAssignment = 3;
        public const byte CompositionAssignmentPayloadMarker = 0xFE;
        public const byte CompositionAssignmentPayloadVersion1 = 1;
        public const byte CompositionAssignmentPayloadVersion = 2;
        public const byte MountedCompositionAssignmentPayloadVersion = 3;
        public const int CompositionAssignmentHeaderBytes = 3;
        public const int BytesPerCompositionAssignmentVersion1 = 5;
        public const int BytesPerCompositionAssignment = 9;
        public const int BytesPerMountedCompositionAssignment = 17;
        public const int BytesPerFormationLayout = 17;
        public const int MaxAssignmentBytes = 4095;
        public const int MaxFormationLayoutBytes = 512;
        public const int MaxCaptainAssignmentBytes = 2048;

        private static readonly CompressionInfo.Integer BattleSideCompressionInfo = new CompressionInfo.Integer(-1, 1, maximumValueGiven: true);

        public CoopCommanderDeploymentFormationAssignmentsMessage(
            BattleSideEnum requestedSide,
            byte[] assignmentBytes,
            byte[] formationLayoutBytes,
            byte[] captainAssignmentBytes)
        {
            RequestedSide = requestedSide;
            AssignmentBytes = assignmentBytes ?? Array.Empty<byte>();
            FormationLayoutBytes = formationLayoutBytes ?? Array.Empty<byte>();
            CaptainAssignmentBytes = captainAssignmentBytes ?? Array.Empty<byte>();
        }

        public CoopCommanderDeploymentFormationAssignmentsMessage()
        {
            RequestedSide = BattleSideEnum.None;
            AssignmentBytes = Array.Empty<byte>();
            FormationLayoutBytes = Array.Empty<byte>();
            CaptainAssignmentBytes = Array.Empty<byte>();
        }

        public BattleSideEnum RequestedSide { get; private set; }
        public byte[] AssignmentBytes { get; private set; }
        public byte[] FormationLayoutBytes { get; private set; }
        public byte[] CaptainAssignmentBytes { get; private set; }

        protected override bool OnRead()
        {
            bool bufferReadValid = true;
            RequestedSide = (BattleSideEnum)ReadIntFromPacket(BattleSideCompressionInfo, ref bufferReadValid);
            AssignmentBytes = ReadPayload(MaxAssignmentBytes, ref bufferReadValid);
            FormationLayoutBytes = ReadPayload(MaxFormationLayoutBytes, ref bufferReadValid);
            CaptainAssignmentBytes = ReadPayload(MaxCaptainAssignmentBytes, ref bufferReadValid);

            return bufferReadValid;
        }

        protected override void OnWrite()
        {
            byte[] assignmentBytes = AssignmentBytes ?? Array.Empty<byte>();
            byte[] formationLayoutBytes = FormationLayoutBytes ?? Array.Empty<byte>();
            byte[] captainAssignmentBytes = CaptainAssignmentBytes ?? Array.Empty<byte>();
            WriteIntToPacket((int)RequestedSide, BattleSideCompressionInfo);
            WritePayload(assignmentBytes, MaxAssignmentBytes);
            WritePayload(formationLayoutBytes, MaxFormationLayoutBytes);
            WritePayload(captainAssignmentBytes, MaxCaptainAssignmentBytes);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter()
        {
            return MultiplayerMessageFilter.Mission;
        }

        protected override string OnGetLogFormat()
        {
            return "CoopCommanderDeploymentFormationAssignments Side=" + RequestedSide +
                " Bytes=" + (AssignmentBytes?.Length ?? 0) +
                " LayoutBytes=" + (FormationLayoutBytes?.Length ?? 0) +
                " CaptainBytes=" + (CaptainAssignmentBytes?.Length ?? 0);
        }

        private byte[] ReadPayload(int maxBytes, ref bool bufferReadValid)
        {
            byte[] payloadBuffer = new byte[maxBytes];
            int bytesRead = ReadByteArrayFromPacket(payloadBuffer, 0, maxBytes, ref bufferReadValid);
            if (bytesRead <= 0)
                return Array.Empty<byte>();

            if (bytesRead == payloadBuffer.Length)
                return payloadBuffer;

            byte[] payload = new byte[bytesRead];
            Buffer.BlockCopy(payloadBuffer, 0, payload, 0, bytesRead);
            return payload;
        }

        private void WritePayload(byte[] payload, int maxBytes)
        {
            byte[] safePayload = payload ?? Array.Empty<byte>();
            int payloadLength = Math.Min(safePayload.Length, maxBytes);
            WriteByteArrayToPacket(safePayload, 0, payloadLength);
        }
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopDelegatedCaptainAssignmentsStateMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer BattleSideCompressionInfo =
            new CompressionInfo.Integer(-1, 1, maximumValueGiven: true);

        public CoopDelegatedCaptainAssignmentsStateMessage(
            BattleSideEnum assignmentSide,
            byte[] captainAssignmentBytes)
        {
            AssignmentSide = assignmentSide;
            CaptainAssignmentBytes = captainAssignmentBytes ?? Array.Empty<byte>();
        }

        public CoopDelegatedCaptainAssignmentsStateMessage()
        {
            AssignmentSide = BattleSideEnum.None;
            CaptainAssignmentBytes = Array.Empty<byte>();
        }

        public BattleSideEnum AssignmentSide { get; private set; }
        public byte[] CaptainAssignmentBytes { get; private set; }

        protected override bool OnRead()
        {
            bool bufferReadValid = true;
            AssignmentSide = (BattleSideEnum)ReadIntFromPacket(BattleSideCompressionInfo, ref bufferReadValid);
            byte[] payloadBuffer = new byte[CoopCommanderDeploymentFormationAssignmentsMessage.MaxCaptainAssignmentBytes];
            int bytesRead = ReadByteArrayFromPacket(
                payloadBuffer,
                0,
                payloadBuffer.Length,
                ref bufferReadValid);
            if (bytesRead <= 0)
            {
                CaptainAssignmentBytes = Array.Empty<byte>();
            }
            else
            {
                CaptainAssignmentBytes = new byte[bytesRead];
                Buffer.BlockCopy(payloadBuffer, 0, CaptainAssignmentBytes, 0, bytesRead);
            }

            return bufferReadValid;
        }

        protected override void OnWrite()
        {
            byte[] payload = CaptainAssignmentBytes ?? Array.Empty<byte>();
            WriteIntToPacket((int)AssignmentSide, BattleSideCompressionInfo);
            WriteByteArrayToPacket(payload, 0, payload.Length);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter()
        {
            return MultiplayerMessageFilter.Mission;
        }

        protected override string OnGetLogFormat()
        {
            return "CoopDelegatedCaptainAssignmentsState Side=" + AssignmentSide +
                " CaptainBytes=" + (CaptainAssignmentBytes?.Length ?? 0);
        }
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromClient)]
    public sealed class CoopCommanderDeploymentSiegeMachineSelectionMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer BattleSideCompressionInfo = new CompressionInfo.Integer(-1, 1, maximumValueGiven: true);

        public CoopCommanderDeploymentSiegeMachineSelectionMessage(
            BattleSideEnum requestedSide,
            MissionObjectId deploymentPointId,
            Vec3 deploymentPointPosition,
            MissionObjectId siegeWeaponId,
            string siegeWeaponTypeName,
            bool clearSelection)
        {
            RequestedSide = requestedSide;
            DeploymentPointId = deploymentPointId;
            DeploymentPointPosition = deploymentPointPosition;
            SiegeWeaponId = siegeWeaponId;
            SiegeWeaponTypeName = string.IsNullOrWhiteSpace(siegeWeaponTypeName) ? string.Empty : siegeWeaponTypeName.Trim();
            ClearSelection = clearSelection;
        }

        public CoopCommanderDeploymentSiegeMachineSelectionMessage()
        {
            RequestedSide = BattleSideEnum.None;
            DeploymentPointId = MissionObjectId.Invalid;
            DeploymentPointPosition = Vec3.Zero;
            SiegeWeaponId = MissionObjectId.Invalid;
            SiegeWeaponTypeName = string.Empty;
            ClearSelection = false;
        }

        public BattleSideEnum RequestedSide { get; private set; }
        public MissionObjectId DeploymentPointId { get; private set; }
        public Vec3 DeploymentPointPosition { get; private set; }
        public MissionObjectId SiegeWeaponId { get; private set; }
        public string SiegeWeaponTypeName { get; private set; }
        public bool ClearSelection { get; private set; }

        protected override bool OnRead()
        {
            bool bufferReadValid = true;
            RequestedSide = (BattleSideEnum)ReadIntFromPacket(BattleSideCompressionInfo, ref bufferReadValid);
            DeploymentPointId = GameNetworkMessage.ReadMissionObjectIdFromPacket(ref bufferReadValid);
            DeploymentPointPosition = GameNetworkMessage.ReadVec3FromPacket(
                CompressionBasic.PositionCompressionInfo,
                ref bufferReadValid);
            SiegeWeaponId = GameNetworkMessage.ReadMissionObjectIdFromPacket(ref bufferReadValid);
            SiegeWeaponTypeName = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty;
            ClearSelection = ReadBoolFromPacket(ref bufferReadValid);
            return bufferReadValid;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket((int)RequestedSide, BattleSideCompressionInfo);
            GameNetworkMessage.WriteMissionObjectIdToPacket(DeploymentPointId);
            GameNetworkMessage.WriteVec3ToPacket(
                DeploymentPointPosition,
                CompressionBasic.PositionCompressionInfo);
            GameNetworkMessage.WriteMissionObjectIdToPacket(SiegeWeaponId);
            WriteStringToPacket(SiegeWeaponTypeName ?? string.Empty);
            WriteBoolToPacket(ClearSelection);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter()
        {
            return MultiplayerMessageFilter.Mission;
        }

        protected override string OnGetLogFormat()
        {
            return "CoopCommanderDeploymentSiegeMachineSelection Side=" + RequestedSide +
                " DeploymentPointId=" + DeploymentPointId +
                " DeploymentPointPosition=" + DeploymentPointPosition +
                " SiegeWeaponId=" + SiegeWeaponId +
                " SiegeWeaponTypeName=" + (SiegeWeaponTypeName ?? string.Empty) +
                " Clear=" + ClearSelection;
        }
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopCommanderDeploymentSiegeMachineStateMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer BattleSideCompressionInfo = new CompressionInfo.Integer(-1, 1, maximumValueGiven: true);

        public CoopCommanderDeploymentSiegeMachineStateMessage(
            BattleSideEnum requestedSide,
            MissionObjectId deploymentPointId,
            Vec3 deploymentPointPosition,
            MissionObjectId siegeWeaponId,
            string siegeWeaponTypeName,
            bool clearSelection)
        {
            RequestedSide = requestedSide;
            DeploymentPointId = deploymentPointId;
            DeploymentPointPosition = deploymentPointPosition;
            SiegeWeaponId = siegeWeaponId;
            SiegeWeaponTypeName = string.IsNullOrWhiteSpace(siegeWeaponTypeName) ? string.Empty : siegeWeaponTypeName.Trim();
            ClearSelection = clearSelection;
        }

        public CoopCommanderDeploymentSiegeMachineStateMessage()
        {
            RequestedSide = BattleSideEnum.None;
            DeploymentPointId = MissionObjectId.Invalid;
            DeploymentPointPosition = Vec3.Zero;
            SiegeWeaponId = MissionObjectId.Invalid;
            SiegeWeaponTypeName = string.Empty;
            ClearSelection = false;
        }

        public BattleSideEnum RequestedSide { get; private set; }
        public MissionObjectId DeploymentPointId { get; private set; }
        public Vec3 DeploymentPointPosition { get; private set; }
        public MissionObjectId SiegeWeaponId { get; private set; }
        public string SiegeWeaponTypeName { get; private set; }
        public bool ClearSelection { get; private set; }

        protected override bool OnRead()
        {
            bool bufferReadValid = true;
            RequestedSide = (BattleSideEnum)ReadIntFromPacket(BattleSideCompressionInfo, ref bufferReadValid);
            DeploymentPointId = GameNetworkMessage.ReadMissionObjectIdFromPacket(ref bufferReadValid);
            DeploymentPointPosition = GameNetworkMessage.ReadVec3FromPacket(
                CompressionBasic.PositionCompressionInfo,
                ref bufferReadValid);
            SiegeWeaponId = GameNetworkMessage.ReadMissionObjectIdFromPacket(ref bufferReadValid);
            SiegeWeaponTypeName = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty;
            ClearSelection = ReadBoolFromPacket(ref bufferReadValid);
            return bufferReadValid;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket((int)RequestedSide, BattleSideCompressionInfo);
            GameNetworkMessage.WriteMissionObjectIdToPacket(DeploymentPointId);
            GameNetworkMessage.WriteVec3ToPacket(
                DeploymentPointPosition,
                CompressionBasic.PositionCompressionInfo);
            GameNetworkMessage.WriteMissionObjectIdToPacket(SiegeWeaponId);
            WriteStringToPacket(SiegeWeaponTypeName ?? string.Empty);
            WriteBoolToPacket(ClearSelection);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter()
        {
            return MultiplayerMessageFilter.Mission;
        }

        protected override string OnGetLogFormat()
        {
            return "CoopCommanderDeploymentSiegeMachineState Side=" + RequestedSide +
                " DeploymentPointId=" + DeploymentPointId +
                " DeploymentPointPosition=" + DeploymentPointPosition +
                " SiegeWeaponId=" + SiegeWeaponId +
                " SiegeWeaponTypeName=" + (SiegeWeaponTypeName ?? string.Empty) +
                " Clear=" + ClearSelection;
        }
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopSiegeMissionObjectIdMapMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer BattleSideCompressionInfo = new CompressionInfo.Integer(-1, 1, maximumValueGiven: true);

        public CoopSiegeMissionObjectIdMapMessage(
            BattleSideEnum objectSide,
            MissionObjectId serverMissionObjectId,
            string signature,
            string objectTypeName,
            string entityName)
        {
            ObjectSide = objectSide;
            ServerMissionObjectId = serverMissionObjectId;
            Signature = string.IsNullOrWhiteSpace(signature) ? string.Empty : signature.Trim();
            ObjectTypeName = string.IsNullOrWhiteSpace(objectTypeName) ? string.Empty : objectTypeName.Trim();
            EntityName = string.IsNullOrWhiteSpace(entityName) ? string.Empty : entityName.Trim();
        }

        public CoopSiegeMissionObjectIdMapMessage()
        {
            ObjectSide = BattleSideEnum.None;
            ServerMissionObjectId = MissionObjectId.Invalid;
            Signature = string.Empty;
            ObjectTypeName = string.Empty;
            EntityName = string.Empty;
        }

        public BattleSideEnum ObjectSide { get; private set; }
        public MissionObjectId ServerMissionObjectId { get; private set; }
        public string Signature { get; private set; }
        public string ObjectTypeName { get; private set; }
        public string EntityName { get; private set; }

        protected override bool OnRead()
        {
            bool bufferReadValid = true;
            ObjectSide = (BattleSideEnum)ReadIntFromPacket(BattleSideCompressionInfo, ref bufferReadValid);
            ServerMissionObjectId = GameNetworkMessage.ReadMissionObjectIdFromPacket(ref bufferReadValid);
            Signature = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty;
            ObjectTypeName = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty;
            EntityName = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty;
            return bufferReadValid;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket((int)ObjectSide, BattleSideCompressionInfo);
            GameNetworkMessage.WriteMissionObjectIdToPacket(ServerMissionObjectId);
            WriteStringToPacket(Signature ?? string.Empty);
            WriteStringToPacket(ObjectTypeName ?? string.Empty);
            WriteStringToPacket(EntityName ?? string.Empty);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter()
        {
            return MultiplayerMessageFilter.Mission;
        }

        protected override string OnGetLogFormat()
        {
            return "CoopSiegeMissionObjectIdMap Side=" + ObjectSide +
                " ServerMissionObjectId=" + ServerMissionObjectId +
                " ObjectType=" + (ObjectTypeName ?? string.Empty) +
                " Entity=" + (EntityName ?? string.Empty) +
                " SignatureLength=" + (Signature?.Length ?? 0);
        }
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopBattlePayloadChunkMessage : GameNetworkMessage
    {
        public const int MaxChunkBytes = 256;
        public const int MaxChunkCount = 8191;

        private static readonly CompressionInfo.Integer PayloadKindCompressionInfo = new CompressionInfo.Integer(0, 2, maximumValueGiven: true);
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
    public sealed class CoopBattleSnapshotChunkRequestMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer TransmissionCompressionInfo = new CompressionInfo.Integer(0, 1048575, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer ChunkIndexCompressionInfo = new CompressionInfo.Integer(-1, CoopBattleSnapshotChunkV2Message.MaxChunkCount, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer ChunkCountCompressionInfo = new CompressionInfo.Integer(0, CoopBattleSnapshotChunkV2Message.MaxChunkCount, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer AssemblyStateCompressionInfo = new CompressionInfo.Integer(0, 3, maximumValueGiven: true);

        public CoopBattleSnapshotChunkRequestMessage(
            int transmissionId,
            int startChunkIndex,
            int endChunkIndex,
            int highestContiguousChunkIndex,
            int receivedChunkCount,
            CoopBattleSnapshotAssemblyStateKind assemblyState)
        {
            TransmissionId = transmissionId;
            StartChunkIndex = startChunkIndex;
            EndChunkIndex = endChunkIndex;
            HighestContiguousChunkIndex = highestContiguousChunkIndex;
            ReceivedChunkCount = receivedChunkCount;
            AssemblyState = assemblyState;
        }

        public CoopBattleSnapshotChunkRequestMessage()
        {
            TransmissionId = 0;
            StartChunkIndex = 0;
            EndChunkIndex = 0;
            HighestContiguousChunkIndex = -1;
            ReceivedChunkCount = 0;
            AssemblyState = CoopBattleSnapshotAssemblyStateKind.Receiving;
        }

        public int TransmissionId { get; private set; }
        public int StartChunkIndex { get; private set; }
        public int EndChunkIndex { get; private set; }
        public int HighestContiguousChunkIndex { get; private set; }
        public int ReceivedChunkCount { get; private set; }
        public CoopBattleSnapshotAssemblyStateKind AssemblyState { get; private set; }

        protected override bool OnRead()
        {
            bool bufferReadValid = true;
            TransmissionId = ReadIntFromPacket(TransmissionCompressionInfo, ref bufferReadValid);
            StartChunkIndex = ReadIntFromPacket(ChunkIndexCompressionInfo, ref bufferReadValid);
            EndChunkIndex = ReadIntFromPacket(ChunkIndexCompressionInfo, ref bufferReadValid);
            HighestContiguousChunkIndex = ReadIntFromPacket(ChunkIndexCompressionInfo, ref bufferReadValid);
            ReceivedChunkCount = ReadIntFromPacket(ChunkCountCompressionInfo, ref bufferReadValid);
            AssemblyState = (CoopBattleSnapshotAssemblyStateKind)ReadIntFromPacket(AssemblyStateCompressionInfo, ref bufferReadValid);
            return bufferReadValid;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket(TransmissionId, TransmissionCompressionInfo);
            WriteIntToPacket(StartChunkIndex, ChunkIndexCompressionInfo);
            WriteIntToPacket(EndChunkIndex, ChunkIndexCompressionInfo);
            WriteIntToPacket(HighestContiguousChunkIndex, ChunkIndexCompressionInfo);
            WriteIntToPacket(ReceivedChunkCount, ChunkCountCompressionInfo);
            WriteIntToPacket((int)AssemblyState, AssemblyStateCompressionInfo);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter()
        {
            return MultiplayerMessageFilter.Mission;
        }

        protected override string OnGetLogFormat()
        {
            return "CoopBattleSnapshotChunkRequest TransmissionId=" + TransmissionId +
                " Range=" + StartChunkIndex + "-" + EndChunkIndex +
                " HighestContiguous=" + HighestContiguousChunkIndex +
                " ReceivedChunkCount=" + ReceivedChunkCount +
                " State=" + AssemblyState;
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
    public sealed class CoopMaterializedAgentEntrySnapshotCompleteAckMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer TransmissionCompressionInfo = new CompressionInfo.Integer(0, 1048575, maximumValueGiven: true);
        private static readonly CompressionInfo.Integer EntryCountCompressionInfo = new CompressionInfo.Integer(0, 4096, maximumValueGiven: true);

        public CoopMaterializedAgentEntrySnapshotCompleteAckMessage(
            int transmissionId,
            bool appliedSuccessfully,
            int entryCount,
            string payloadHash)
        {
            TransmissionId = transmissionId;
            AppliedSuccessfully = appliedSuccessfully;
            EntryCount = entryCount;
            PayloadHash = string.IsNullOrWhiteSpace(payloadHash) ? string.Empty : payloadHash.Trim();
        }

        public CoopMaterializedAgentEntrySnapshotCompleteAckMessage()
        {
            TransmissionId = 0;
            AppliedSuccessfully = false;
            EntryCount = 0;
            PayloadHash = string.Empty;
        }

        public int TransmissionId { get; private set; }
        public bool AppliedSuccessfully { get; private set; }
        public int EntryCount { get; private set; }
        public string PayloadHash { get; private set; }

        protected override bool OnRead()
        {
            bool bufferReadValid = true;
            TransmissionId = ReadIntFromPacket(TransmissionCompressionInfo, ref bufferReadValid);
            AppliedSuccessfully = ReadBoolFromPacket(ref bufferReadValid);
            EntryCount = ReadIntFromPacket(EntryCountCompressionInfo, ref bufferReadValid);
            PayloadHash = ReadStringFromPacket(ref bufferReadValid) ?? string.Empty;
            return bufferReadValid;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket(TransmissionId, TransmissionCompressionInfo);
            WriteBoolToPacket(AppliedSuccessfully);
            WriteIntToPacket(EntryCount, EntryCountCompressionInfo);
            WriteStringToPacket(PayloadHash ?? string.Empty);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter()
        {
            return MultiplayerMessageFilter.Mission;
        }

        protected override string OnGetLogFormat()
        {
            return "CoopMaterializedAgentEntrySnapshotCompleteAck TransmissionId=" + TransmissionId +
                " AppliedSuccessfully=" + AppliedSuccessfully +
                " EntryCount=" + EntryCount;
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
