using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

namespace CoopSpectator.Network.Messages
{
    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopCampaignMapCatalogSnapshotMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer ProtocolCompression =
            new CompressionInfo.Integer(0, 15, true);
        private static readonly CompressionInfo.Integer RevisionCompression =
            new CompressionInfo.Integer(0, int.MaxValue, true);
        private static readonly CompressionInfo.Integer CountCompression =
            new CompressionInfo.Integer(
                0,
                CoopCampaignMapPrototypeContract.MaxCatalogEntities,
                true);

        public CoopCampaignMapCatalogSnapshotMessage(int revision, int count)
        {
            ProtocolVersion = CoopCampaignMapPrototypeContract.ProtocolVersion;
            Revision = Math.Max(0, revision);
            EntityCount = Math.Max(0, Math.Min(
                CoopCampaignMapPrototypeContract.MaxCatalogEntities,
                count));
        }

        public CoopCampaignMapCatalogSnapshotMessage()
        {
        }

        public int ProtocolVersion { get; private set; }

        public int Revision { get; private set; }

        public int EntityCount { get; private set; }

        protected override bool OnRead()
        {
            bool valid = true;
            ProtocolVersion = ReadIntFromPacket(ProtocolCompression, ref valid);
            Revision = ReadIntFromPacket(RevisionCompression, ref valid);
            EntityCount = ReadIntFromPacket(CountCompression, ref valid);
            return valid;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket(ProtocolVersion, ProtocolCompression);
            WriteIntToPacket(Math.Max(0, Revision), RevisionCompression);
            WriteIntToPacket(Math.Max(0, Math.Min(
                CoopCampaignMapPrototypeContract.MaxCatalogEntities,
                EntityCount)), CountCompression);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter() =>
            MultiplayerMessageFilter.Mission;

        protected override string OnGetLogFormat() =>
            "CoopCampaignMapCatalogSnapshot Revision=" + Revision +
            " Count=" + EntityCount;
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopCampaignMapCatalogEntityMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer ProtocolCompression =
            new CompressionInfo.Integer(0, 15, true);
        private static readonly CompressionInfo.Integer RevisionCompression =
            new CompressionInfo.Integer(0, int.MaxValue, true);
        private static readonly CompressionInfo.Integer CountCompression =
            new CompressionInfo.Integer(0, CoopCampaignMapPrototypeContract.MaxCatalogEntities, true);
        private static readonly CompressionInfo.Integer IndexCompression =
            new CompressionInfo.Integer(0, CoopCampaignMapPrototypeContract.MaxCatalogEntities - 1, true);
        private static readonly CompressionInfo.Integer KindCompression =
            new CompressionInfo.Integer(0, 2, true);
        private static readonly CompressionInfo.Integer NameplateCompression =
            new CompressionInfo.Integer(0, 3, true);
        private static readonly CompressionInfo.Integer SettlementKindCompression =
            new CompressionInfo.Integer(
                0,
                (int)CoopCampaignMapPrototypeSettlementKind.Special,
                true);
        private static readonly CompressionInfo.Integer VisualKindCompression =
            new CompressionInfo.Integer(0, 3, true);
        private static readonly CompressionInfo.Integer UnitCompression =
            new CompressionInfo.Integer(0, CoopCampaignMapPrototypeContract.UnitScale, true);
        private static readonly CompressionInfo.UnsignedInteger ColorCompression =
            new CompressionInfo.UnsignedInteger(0u, 32);
        private static readonly CompressionInfo.Integer RaceCompression =
            new CompressionInfo.Integer(0, CoopCampaignMapPrototypeContract.MaximumVisualRace, true);
        private static readonly CompressionInfo.Integer SkeletonCompression =
            new CompressionInfo.Integer(0, CoopCampaignMapPrototypeContract.MaximumSkeletonType, true);
        private static readonly CompressionInfo.Integer WieldedCompression =
            new CompressionInfo.Integer(0, CoopCampaignMapPrototypeContract.EquipmentSlotCount, true);

        public CoopCampaignMapCatalogEntityMessage(
            int revision,
            int index,
            int expectedCount,
            CoopCampaignMapPrototypeCatalogEntityState entity)
        {
            ProtocolVersion = CoopCampaignMapPrototypeContract.ProtocolVersion;
            Revision = Math.Max(0, revision);
            Index = Math.Max(0, Math.Min(
                CoopCampaignMapPrototypeContract.MaxCatalogEntities - 1,
                index));
            ExpectedCount = Math.Max(1, Math.Min(
                CoopCampaignMapPrototypeContract.MaxCatalogEntities,
                expectedCount));
            Entity = entity?.Clone();
        }

        public CoopCampaignMapCatalogEntityMessage()
        {
        }

        public int ProtocolVersion { get; private set; }

        public int Revision { get; private set; }

        public int Index { get; private set; }

        public int ExpectedCount { get; private set; }

        public CoopCampaignMapPrototypeCatalogEntityState Entity { get; private set; }

        protected override bool OnRead()
        {
            bool valid = true;
            ProtocolVersion = ReadIntFromPacket(ProtocolCompression, ref valid);
            Revision = ReadIntFromPacket(RevisionCompression, ref valid);
            Index = ReadIntFromPacket(IndexCompression, ref valid);
            ExpectedCount = ReadIntFromPacket(CountCompression, ref valid);
            var entity = new CoopCampaignMapPrototypeCatalogEntityState
            {
                EntityId = ReadStringFromPacket(ref valid) ?? string.Empty,
                DisplayName = ReadStringFromPacket(ref valid) ?? string.Empty,
                BannerCode = ReadStringFromPacket(ref valid) ?? string.Empty,
                VisualCharacterId = ReadStringFromPacket(ref valid) ?? string.Empty,
                CultureId = ReadStringFromPacket(ref valid) ?? string.Empty,
                FactionId = ReadStringFromPacket(ref valid) ?? string.Empty,
                FactionName = ReadStringFromPacket(ref valid) ?? string.Empty,
                OwnerName = ReadStringFromPacket(ref valid) ?? string.Empty,
                LeaderName = ReadStringFromPacket(ref valid) ?? string.Empty,
                ArmyId = ReadStringFromPacket(ref valid) ?? string.Empty,
                ArmyName = ReadStringFromPacket(ref valid) ?? string.Empty,
                Kind = (CoopCampaignMapPrototypeEntityKind)
                    ReadIntFromPacket(KindCompression, ref valid),
                SettlementNameplateSize =
                    (CoopCampaignMapPrototypeSettlementNameplateSize)
                        ReadIntFromPacket(NameplateCompression, ref valid),
                SettlementKind = (CoopCampaignMapPrototypeSettlementKind)
                    ReadIntFromPacket(SettlementKindCompression, ref valid),
                PartyVisualKind = (CoopCampaignMapPrototypePartyVisualKind)
                    ReadIntFromPacket(VisualKindCompression, ref valid),
                PrimaryColor = ReadUintFromPacket(ColorCompression, ref valid),
                SecondaryColor = ReadUintFromPacket(ColorCompression, ref valid),
                IsArmyLeader = ReadBoolFromPacket(ref valid),
                SelectionRadius = ReadIntFromPacket(UnitCompression, ref valid),
                HumanVisual = ReadAgentVisual(ref valid),
                MountVisual = ReadAgentVisual(ref valid),
                CaravanMountVisual = ReadAgentVisual(ref valid)
            };
            Entity = entity;
            return valid && ExpectedCount > 0 && Index < ExpectedCount &&
                   CoopCampaignMapPrototypeContract.IsValidCatalogEntity(entity);
        }

        protected override void OnWrite()
        {
            CoopCampaignMapPrototypeCatalogEntityState entity =
                Entity ?? CreateInvalidCatalogEntity();
            WriteIntToPacket(ProtocolVersion, ProtocolCompression);
            WriteIntToPacket(Math.Max(0, Revision), RevisionCompression);
            WriteIntToPacket(Math.Max(0, Math.Min(
                CoopCampaignMapPrototypeContract.MaxCatalogEntities - 1,
                Index)), IndexCompression);
            WriteIntToPacket(Math.Max(1, Math.Min(
                CoopCampaignMapPrototypeContract.MaxCatalogEntities,
                ExpectedCount)), CountCompression);
            WriteBounded(entity.EntityId, CoopCampaignMapPrototypeContract.MaxEntityIdCharacters, "invalid");
            WriteBounded(entity.DisplayName, CoopCampaignMapPrototypeContract.MaxEntityNameCharacters, "Invalid");
            WriteBounded(entity.BannerCode, CoopCampaignMapPrototypeContract.MaxBannerCodeCharacters, string.Empty);
            WriteBounded(entity.VisualCharacterId, CoopCampaignMapPrototypeContract.MaxVisualCharacterIdCharacters, string.Empty);
            WriteBounded(entity.CultureId, CoopCampaignMapPrototypeContract.MaxCultureIdCharacters, string.Empty);
            WriteInformation(entity.FactionId);
            WriteInformation(entity.FactionName);
            WriteInformation(entity.OwnerName);
            WriteInformation(entity.LeaderName);
            WriteInformation(entity.ArmyId);
            WriteInformation(entity.ArmyName);
            WriteIntToPacket((int)entity.Kind, KindCompression);
            WriteIntToPacket((int)entity.SettlementNameplateSize, NameplateCompression);
            WriteIntToPacket((int)entity.SettlementKind, SettlementKindCompression);
            WriteIntToPacket((int)entity.PartyVisualKind, VisualKindCompression);
            WriteUintToPacket(entity.PrimaryColor, ColorCompression);
            WriteUintToPacket(entity.SecondaryColor, ColorCompression);
            WriteBoolToPacket(entity.IsArmyLeader);
            WriteIntToPacket(ClampUnit(entity.SelectionRadius), UnitCompression);
            WriteAgentVisual(entity.HumanVisual);
            WriteAgentVisual(entity.MountVisual);
            WriteAgentVisual(entity.CaravanMountVisual);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter() =>
            MultiplayerMessageFilter.Mission;

        protected override string OnGetLogFormat() =>
            "CoopCampaignMapCatalogEntity Revision=" + Revision +
            " Index=" + Index + "/" + ExpectedCount +
            " Entity=" + (Entity?.EntityId ?? "null");

        private CoopCampaignMapPrototypeAgentVisualState ReadAgentVisual(ref bool valid)
        {
            if (!ReadBoolFromPacket(ref valid))
                return null;
            var visual = new CoopCampaignMapPrototypeAgentVisualState
            {
                BodyProperties = ReadStringFromPacket(ref valid) ?? string.Empty,
                IsFemale = ReadBoolFromPacket(ref valid),
                Race = ReadIntFromPacket(RaceCompression, ref valid),
                SkeletonType = ReadIntFromPacket(SkeletonCompression, ref valid),
                RightWieldedItemIndex = ReadIntFromPacket(WieldedCompression, ref valid) - 1,
                LeftWieldedItemIndex = ReadIntFromPacket(WieldedCompression, ref valid) - 1,
                MountCreationKey = ReadStringFromPacket(ref valid) ?? string.Empty,
                HasBanner = ReadBoolFromPacket(ref valid),
                AddColorRandomness = ReadBoolFromPacket(ref valid),
                EquipmentItemIds = new string[CoopCampaignMapPrototypeContract.EquipmentSlotCount]
            };
            for (int slot = 0; slot < visual.EquipmentItemIds.Length; slot++)
                visual.EquipmentItemIds[slot] = ReadStringFromPacket(ref valid) ?? string.Empty;
            return visual;
        }

        private void WriteAgentVisual(CoopCampaignMapPrototypeAgentVisualState visual)
        {
            bool valid = visual != null &&
                         CoopCampaignMapPrototypeContract.IsValidAgentVisualState(visual, false);
            WriteBoolToPacket(valid);
            if (!valid)
                return;
            WriteBounded(visual.BodyProperties, CoopCampaignMapPrototypeContract.MaxBodyPropertiesCharacters, string.Empty);
            WriteBoolToPacket(visual.IsFemale);
            WriteIntToPacket(Math.Max(0, Math.Min(CoopCampaignMapPrototypeContract.MaximumVisualRace, visual.Race)), RaceCompression);
            WriteIntToPacket(Math.Max(0, Math.Min(CoopCampaignMapPrototypeContract.MaximumSkeletonType, visual.SkeletonType)), SkeletonCompression);
            WriteIntToPacket(ClampWielded(visual.RightWieldedItemIndex) + 1, WieldedCompression);
            WriteIntToPacket(ClampWielded(visual.LeftWieldedItemIndex) + 1, WieldedCompression);
            WriteBounded(visual.MountCreationKey, CoopCampaignMapPrototypeContract.MaxMountCreationKeyCharacters, string.Empty);
            WriteBoolToPacket(visual.HasBanner);
            WriteBoolToPacket(visual.AddColorRandomness);
            for (int slot = 0; slot < CoopCampaignMapPrototypeContract.EquipmentSlotCount; slot++)
                WriteBounded(visual.EquipmentItemIds[slot], CoopCampaignMapPrototypeContract.MaxVisualItemIdCharacters, string.Empty);
        }

        private void WriteInformation(string value)
        {
            WriteBounded(value, CoopCampaignMapPrototypeContract.MaxInformationTextCharacters, string.Empty);
        }

        private void WriteBounded(string value, int maximum, string fallback)
        {
            WriteStringToPacket(CoopCampaignMapPrototypeContract.BoundEntityText(value, maximum, fallback));
        }

        private static int ClampUnit(int value) =>
            value < 0 ? 0 : value > CoopCampaignMapPrototypeContract.UnitScale
                ? CoopCampaignMapPrototypeContract.UnitScale
                : value;

        private static int ClampWielded(int value) =>
            value < -1 ? -1 : value >= CoopCampaignMapPrototypeContract.EquipmentSlotCount
                ? CoopCampaignMapPrototypeContract.EquipmentSlotCount - 1
                : value;

        private static CoopCampaignMapPrototypeCatalogEntityState CreateInvalidCatalogEntity()
        {
            return new CoopCampaignMapPrototypeCatalogEntityState
            {
                EntityId = "invalid",
                DisplayName = "Invalid",
                Kind = CoopCampaignMapPrototypeEntityKind.MobileParty,
                SettlementNameplateSize = CoopCampaignMapPrototypeSettlementNameplateSize.None,
                SettlementKind = CoopCampaignMapPrototypeSettlementKind.None,
                BannerCode = string.Empty,
                VisualCharacterId = string.Empty,
                CultureId = string.Empty,
                FactionId = string.Empty,
                FactionName = string.Empty,
                OwnerName = string.Empty,
                LeaderName = string.Empty,
                ArmyId = string.Empty,
                ArmyName = string.Empty,
                PartyVisualKind = CoopCampaignMapPrototypePartyVisualKind.None
            };
        }
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopCampaignMapCatalogManifestMessage :
        GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer ProtocolCompression =
            new CompressionInfo.Integer(0, 15, true);
        private static readonly CompressionInfo.Integer TransferCompression =
            new CompressionInfo.Integer(
                0,
                CoopCampaignMapCatalogChunkCodec.MaxTransferId,
                true);
        private static readonly CompressionInfo.Integer RevisionCompression =
            new CompressionInfo.Integer(0, int.MaxValue, true);
        private static readonly CompressionInfo.Integer SchemaCompression =
            new CompressionInfo.Integer(0, 255, true);
        private static readonly CompressionInfo.Integer LogicalSizeCompression =
            new CompressionInfo.Integer(
                0,
                CoopCampaignMapPrototypeContract.MaxCatalogLogicalBytes,
                true);
        private static readonly CompressionInfo.Integer WireSizeCompression =
            new CompressionInfo.Integer(
                0,
                CoopCampaignMapCatalogChunkCodec.MaxWireBytes,
                true);
        private static readonly CompressionInfo.Integer ChunkCountCompression =
            new CompressionInfo.Integer(
                1,
                CoopCampaignMapCatalogChunkCodec.MaxChunkCount,
                true);
        private static readonly CompressionInfo.Integer CompressionCompression =
            new CompressionInfo.Integer(0, 1, true);

        public CoopCampaignMapCatalogManifestMessage(
            int transferId,
            int revision,
            int logicalByteCount,
            int wireByteCount,
            int chunkCount,
            CoopCampaignMapCatalogCompressionKind compressionKind,
            string payloadHash)
        {
            ProtocolVersion =
                CoopCampaignMapPrototypeContract.ProtocolVersion;
            TransferId = transferId;
            Revision = revision;
            SchemaVersion =
                CoopCampaignMapCatalogBinarySerializer.SchemaVersion;
            LogicalByteCount = logicalByteCount;
            WireByteCount = wireByteCount;
            ChunkCount = chunkCount;
            CompressionKind = compressionKind;
            PayloadHash = payloadHash ?? string.Empty;
        }

        public CoopCampaignMapCatalogManifestMessage()
        {
            ChunkCount = 1;
            PayloadHash = string.Empty;
        }

        public int ProtocolVersion { get; private set; }

        public int TransferId { get; private set; }

        public int Revision { get; private set; }

        public int SchemaVersion { get; private set; }

        public int LogicalByteCount { get; private set; }

        public int WireByteCount { get; private set; }

        public int ChunkCount { get; private set; }

        public CoopCampaignMapCatalogCompressionKind CompressionKind
        {
            get;
            private set;
        }

        public string PayloadHash { get; private set; }

        protected override bool OnRead()
        {
            bool valid = true;
            ProtocolVersion = ReadIntFromPacket(
                ProtocolCompression,
                ref valid);
            TransferId = ReadIntFromPacket(TransferCompression, ref valid);
            Revision = ReadIntFromPacket(RevisionCompression, ref valid);
            SchemaVersion = ReadIntFromPacket(SchemaCompression, ref valid);
            LogicalByteCount = ReadIntFromPacket(
                LogicalSizeCompression,
                ref valid);
            WireByteCount = ReadIntFromPacket(
                WireSizeCompression,
                ref valid);
            ChunkCount = ReadIntFromPacket(
                ChunkCountCompression,
                ref valid);
            CompressionKind = (CoopCampaignMapCatalogCompressionKind)
                ReadIntFromPacket(CompressionCompression, ref valid);
            PayloadHash = ReadStringFromPacket(ref valid) ?? string.Empty;
            return valid;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket(ProtocolVersion, ProtocolCompression);
            WriteIntToPacket(TransferId, TransferCompression);
            WriteIntToPacket(Revision, RevisionCompression);
            WriteIntToPacket(SchemaVersion, SchemaCompression);
            WriteIntToPacket(
                LogicalByteCount,
                LogicalSizeCompression);
            WriteIntToPacket(WireByteCount, WireSizeCompression);
            WriteIntToPacket(ChunkCount, ChunkCountCompression);
            WriteIntToPacket(
                (int)CompressionKind,
                CompressionCompression);
            WriteStringToPacket(PayloadHash ?? string.Empty);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter() =>
            MultiplayerMessageFilter.Mission;

        protected override string OnGetLogFormat() =>
            "CoopCampaignMapCatalogManifest TransferId=" + TransferId +
            " Revision=" + Revision +
            " LogicalBytes=" + LogicalByteCount +
            " WireBytes=" + WireByteCount +
            " Chunks=" + ChunkCount;
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopCampaignMapCatalogChunkMessage : GameNetworkMessage
    {
        public const int MaxChunkBytes =
            CoopCampaignMapCatalogChunkCodec.MaxChunkBytes;

        private static readonly CompressionInfo.Integer ProtocolCompression =
            new CompressionInfo.Integer(0, 15, true);
        private static readonly CompressionInfo.Integer TransferCompression =
            new CompressionInfo.Integer(
                0,
                CoopCampaignMapCatalogChunkCodec.MaxTransferId,
                true);
        private static readonly CompressionInfo.Integer ChunkIndexCompression =
            new CompressionInfo.Integer(
                0,
                CoopCampaignMapCatalogChunkCodec.MaxChunkCount,
                true);
        private static readonly CompressionInfo.Integer ChunkCountCompression =
            new CompressionInfo.Integer(
                1,
                CoopCampaignMapCatalogChunkCodec.MaxChunkCount,
                true);

        public CoopCampaignMapCatalogChunkMessage(
            int transferId,
            int chunkIndex,
            int chunkCount,
            byte[] payloadBytes)
        {
            ProtocolVersion =
                CoopCampaignMapPrototypeContract.ProtocolVersion;
            TransferId = transferId;
            ChunkIndex = chunkIndex;
            ChunkCount = chunkCount;
            PayloadBytes = payloadBytes ?? Array.Empty<byte>();
        }

        public CoopCampaignMapCatalogChunkMessage()
        {
            ChunkCount = 1;
            PayloadBytes = Array.Empty<byte>();
        }

        public int ProtocolVersion { get; private set; }

        public int TransferId { get; private set; }

        public int ChunkIndex { get; private set; }

        public int ChunkCount { get; private set; }

        public byte[] PayloadBytes { get; private set; }

        protected override bool OnRead()
        {
            bool valid = true;
            ProtocolVersion = ReadIntFromPacket(
                ProtocolCompression,
                ref valid);
            TransferId = ReadIntFromPacket(TransferCompression, ref valid);
            ChunkIndex = ReadIntFromPacket(
                ChunkIndexCompression,
                ref valid);
            ChunkCount = ReadIntFromPacket(
                ChunkCountCompression,
                ref valid);
            var buffer = new byte[MaxChunkBytes];
            int bytesRead = ReadByteArrayFromPacket(
                buffer,
                0,
                buffer.Length,
                ref valid);
            if (bytesRead <= 0)
            {
                PayloadBytes = Array.Empty<byte>();
            }
            else if (bytesRead == buffer.Length)
            {
                PayloadBytes = buffer;
            }
            else
            {
                PayloadBytes = new byte[bytesRead];
                Buffer.BlockCopy(buffer, 0, PayloadBytes, 0, bytesRead);
            }
            return valid;
        }

        protected override void OnWrite()
        {
            byte[] payloadBytes = PayloadBytes ?? Array.Empty<byte>();
            int payloadLength = Math.Min(
                payloadBytes.Length,
                MaxChunkBytes);
            WriteIntToPacket(ProtocolVersion, ProtocolCompression);
            WriteIntToPacket(TransferId, TransferCompression);
            WriteIntToPacket(ChunkIndex, ChunkIndexCompression);
            WriteIntToPacket(ChunkCount, ChunkCountCompression);
            if (payloadLength > 0)
                WriteByteArrayToPacket(payloadBytes, 0, payloadLength);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter() =>
            MultiplayerMessageFilter.Mission;

        protected override string OnGetLogFormat() =>
            "CoopCampaignMapCatalogChunk TransferId=" + TransferId +
            " Chunk=" + ChunkIndex + "/" + ChunkCount +
            " Bytes=" + (PayloadBytes?.Length ?? 0);
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromClient)]
    public sealed class CoopCampaignMapCatalogRangeAckMessage :
        GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer ProtocolCompression =
            new CompressionInfo.Integer(0, 15, true);
        private static readonly CompressionInfo.Integer TransferCompression =
            new CompressionInfo.Integer(
                0,
                CoopCampaignMapCatalogChunkCodec.MaxTransferId,
                true);
        private static readonly CompressionInfo.Integer ChunkIndexCompression =
            new CompressionInfo.Integer(
                -1,
                CoopCampaignMapCatalogChunkCodec.MaxChunkCount,
                true);
        private static readonly CompressionInfo.Integer ChunkCountCompression =
            new CompressionInfo.Integer(
                0,
                CoopCampaignMapCatalogChunkCodec.MaxChunkCount,
                true);

        public CoopCampaignMapCatalogRangeAckMessage(
            int transferId,
            int requestedStartChunkIndex,
            int requestedEndChunkIndex,
            int highestContiguousChunkIndex,
            int receivedChunkCount)
        {
            ProtocolVersion =
                CoopCampaignMapPrototypeContract.ProtocolVersion;
            TransferId = transferId;
            RequestedStartChunkIndex = requestedStartChunkIndex;
            RequestedEndChunkIndex = requestedEndChunkIndex;
            HighestContiguousChunkIndex = highestContiguousChunkIndex;
            ReceivedChunkCount = receivedChunkCount;
        }

        public CoopCampaignMapCatalogRangeAckMessage()
        {
            RequestedStartChunkIndex = -1;
            RequestedEndChunkIndex = -1;
            HighestContiguousChunkIndex = -1;
        }

        public int ProtocolVersion { get; private set; }

        public int TransferId { get; private set; }

        public int RequestedStartChunkIndex { get; private set; }

        public int RequestedEndChunkIndex { get; private set; }

        public int HighestContiguousChunkIndex { get; private set; }

        public int ReceivedChunkCount { get; private set; }

        protected override bool OnRead()
        {
            bool valid = true;
            ProtocolVersion = ReadIntFromPacket(
                ProtocolCompression,
                ref valid);
            TransferId = ReadIntFromPacket(TransferCompression, ref valid);
            RequestedStartChunkIndex = ReadIntFromPacket(
                ChunkIndexCompression,
                ref valid);
            RequestedEndChunkIndex = ReadIntFromPacket(
                ChunkIndexCompression,
                ref valid);
            HighestContiguousChunkIndex = ReadIntFromPacket(
                ChunkIndexCompression,
                ref valid);
            ReceivedChunkCount = ReadIntFromPacket(
                ChunkCountCompression,
                ref valid);
            return valid;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket(ProtocolVersion, ProtocolCompression);
            WriteIntToPacket(TransferId, TransferCompression);
            WriteIntToPacket(
                RequestedStartChunkIndex,
                ChunkIndexCompression);
            WriteIntToPacket(
                RequestedEndChunkIndex,
                ChunkIndexCompression);
            WriteIntToPacket(
                HighestContiguousChunkIndex,
                ChunkIndexCompression);
            WriteIntToPacket(
                ReceivedChunkCount,
                ChunkCountCompression);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter() =>
            MultiplayerMessageFilter.Mission;

        protected override string OnGetLogFormat() =>
            "CoopCampaignMapCatalogRangeAck TransferId=" + TransferId +
            " Request=" + RequestedStartChunkIndex + "-" +
            RequestedEndChunkIndex +
            " HighestContiguous=" + HighestContiguousChunkIndex;
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromClient)]
    public sealed class CoopCampaignMapCatalogCompleteAckMessage :
        GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer ProtocolCompression =
            new CompressionInfo.Integer(0, 15, true);
        private static readonly CompressionInfo.Integer TransferCompression =
            new CompressionInfo.Integer(
                0,
                CoopCampaignMapCatalogChunkCodec.MaxTransferId,
                true);
        private static readonly CompressionInfo.Integer RevisionCompression =
            new CompressionInfo.Integer(0, int.MaxValue, true);

        public CoopCampaignMapCatalogCompleteAckMessage(
            int transferId,
            int revision,
            bool appliedSuccessfully,
            string payloadHash)
        {
            ProtocolVersion =
                CoopCampaignMapPrototypeContract.ProtocolVersion;
            TransferId = transferId;
            Revision = revision;
            AppliedSuccessfully = appliedSuccessfully;
            PayloadHash = payloadHash ?? string.Empty;
        }

        public CoopCampaignMapCatalogCompleteAckMessage()
        {
            PayloadHash = string.Empty;
        }

        public int ProtocolVersion { get; private set; }

        public int TransferId { get; private set; }

        public int Revision { get; private set; }

        public bool AppliedSuccessfully { get; private set; }

        public string PayloadHash { get; private set; }

        protected override bool OnRead()
        {
            bool valid = true;
            ProtocolVersion = ReadIntFromPacket(
                ProtocolCompression,
                ref valid);
            TransferId = ReadIntFromPacket(TransferCompression, ref valid);
            Revision = ReadIntFromPacket(RevisionCompression, ref valid);
            AppliedSuccessfully = ReadBoolFromPacket(ref valid);
            PayloadHash = ReadStringFromPacket(ref valid) ?? string.Empty;
            return valid;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket(ProtocolVersion, ProtocolCompression);
            WriteIntToPacket(TransferId, TransferCompression);
            WriteIntToPacket(Revision, RevisionCompression);
            WriteBoolToPacket(AppliedSuccessfully);
            WriteStringToPacket(PayloadHash ?? string.Empty);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter() =>
            MultiplayerMessageFilter.Mission;

        protected override string OnGetLogFormat() =>
            "CoopCampaignMapCatalogCompleteAck TransferId=" + TransferId +
            " Revision=" + Revision +
            " Applied=" + AppliedSuccessfully;
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopCampaignMapDynamicSnapshotMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer ProtocolCompression =
            new CompressionInfo.Integer(0, 15, true);
        private static readonly CompressionInfo.Integer RevisionCompression =
            new CompressionInfo.Integer(0, int.MaxValue, true);
        private static readonly CompressionInfo.Integer CountCompression =
            new CompressionInfo.Integer(0, CoopCampaignMapPrototypeContract.MaxDynamicEntities, true);

        public CoopCampaignMapDynamicSnapshotMessage(int revision, int count)
        {
            ProtocolVersion = CoopCampaignMapPrototypeContract.ProtocolVersion;
            Revision = Math.Max(0, revision);
            EntityCount = Math.Max(0, Math.Min(CoopCampaignMapPrototypeContract.MaxDynamicEntities, count));
        }

        public CoopCampaignMapDynamicSnapshotMessage()
        {
        }

        public int ProtocolVersion { get; private set; }
        public int Revision { get; private set; }
        public int EntityCount { get; private set; }

        protected override bool OnRead()
        {
            bool valid = true;
            ProtocolVersion = ReadIntFromPacket(ProtocolCompression, ref valid);
            Revision = ReadIntFromPacket(RevisionCompression, ref valid);
            EntityCount = ReadIntFromPacket(CountCompression, ref valid);
            return valid;
        }

        protected override void OnWrite()
        {
            WriteIntToPacket(ProtocolVersion, ProtocolCompression);
            WriteIntToPacket(Math.Max(0, Revision), RevisionCompression);
            WriteIntToPacket(Math.Max(0, Math.Min(CoopCampaignMapPrototypeContract.MaxDynamicEntities, EntityCount)), CountCompression);
        }

        protected override MultiplayerMessageFilter OnGetLogFilter() => MultiplayerMessageFilter.Mission;
        protected override string OnGetLogFormat() => "CoopCampaignMapDynamicSnapshot Revision=" + Revision + " Count=" + EntityCount;
    }

    [DefineGameNetworkMessageTypeForMod(GameNetworkMessageSendType.FromServer)]
    public sealed class CoopCampaignMapDynamicBatchMessage : GameNetworkMessage
    {
        private static readonly CompressionInfo.Integer ProtocolCompression = new CompressionInfo.Integer(0, 15, true);
        private static readonly CompressionInfo.Integer RevisionCompression = new CompressionInfo.Integer(0, int.MaxValue, true);
        private static readonly CompressionInfo.Integer TotalCompression = new CompressionInfo.Integer(0, CoopCampaignMapPrototypeContract.MaxDynamicEntities, true);
        private static readonly CompressionInfo.Integer IndexCompression = new CompressionInfo.Integer(0, CoopCampaignMapPrototypeContract.MaxDynamicEntities - 1, true);
        private static readonly CompressionInfo.Integer BatchCompression = new CompressionInfo.Integer(0, CoopCampaignMapPrototypeContract.DynamicBatchCompressionMaximum, true);
        private static readonly CompressionInfo.Integer UnitCompression = new CompressionInfo.Integer(0, CoopCampaignMapPrototypeContract.UnitScale, true);
        private static readonly CompressionInfo.Integer SizeCompression = new CompressionInfo.Integer(0, CoopCampaignMapPrototypeContract.MaximumPartySize, true);

        public CoopCampaignMapDynamicBatchMessage(
            int revision,
            int startIndex,
            int expectedCount,
            IReadOnlyList<CoopCampaignMapPrototypeDynamicEntityState> entities)
        {
            ProtocolVersion = CoopCampaignMapPrototypeContract.ProtocolVersion;
            Revision = Math.Max(0, revision);
            StartIndex = Math.Max(0, Math.Min(CoopCampaignMapPrototypeContract.MaxDynamicEntities - 1, startIndex));
            ExpectedCount = Math.Max(1, Math.Min(CoopCampaignMapPrototypeContract.MaxDynamicEntities, expectedCount));
            Entities = new List<CoopCampaignMapPrototypeDynamicEntityState>();
            if (entities != null)
            {
                for (int index = 0; index < entities.Count && index < CoopCampaignMapPrototypeContract.MaxDynamicBatchSize; index++)
                {
                    if (entities[index] != null)
                        Entities.Add(entities[index].Clone());
                }
            }
        }

        public CoopCampaignMapDynamicBatchMessage()
        {
        }

        public int ProtocolVersion { get; private set; }
        public int Revision { get; private set; }
        public int StartIndex { get; private set; }
        public int ExpectedCount { get; private set; }
        public List<CoopCampaignMapPrototypeDynamicEntityState> Entities { get; private set; } = new List<CoopCampaignMapPrototypeDynamicEntityState>();

        protected override bool OnRead()
        {
            bool valid = true;
            ProtocolVersion = ReadIntFromPacket(ProtocolCompression, ref valid);
            Revision = ReadIntFromPacket(RevisionCompression, ref valid);
            StartIndex = ReadIntFromPacket(IndexCompression, ref valid);
            ExpectedCount = ReadIntFromPacket(TotalCompression, ref valid);
            int batchCount = ReadIntFromPacket(BatchCompression, ref valid);
            valid = valid &&
                    batchCount <=
                    CoopCampaignMapPrototypeContract.MaxDynamicBatchSize;
            var entities = new List<CoopCampaignMapPrototypeDynamicEntityState>(batchCount);
            for (int index = 0; index < batchCount; index++)
            {
                var entity = new CoopCampaignMapPrototypeDynamicEntityState
                {
                    EntityId = ReadStringFromPacket(ref valid) ?? string.Empty,
                    NormalizedX = ReadIntFromPacket(UnitCompression, ref valid),
                    NormalizedY = ReadIntFromPacket(UnitCompression, ref valid),
                    Heading = ReadIntFromPacket(UnitCompression, ref valid),
                    PartySize = ReadIntFromPacket(SizeCompression, ref valid),
                    IsVisible = ReadBoolFromPacket(ref valid),
                    IsMoving = ReadBoolFromPacket(ref valid),
                    ArmyPartyCount = ReadIntFromPacket(SizeCompression, ref valid),
                    ArmyTotalSize = ReadIntFromPacket(SizeCompression, ref valid),
                    ArmyCohesion = ReadIntFromPacket(UnitCompression, ref valid),
                    AppearanceRevision = ReadIntFromPacket(RevisionCompression, ref valid),
                    InformationRevision = ReadIntFromPacket(RevisionCompression, ref valid)
                };
                valid = valid && CoopCampaignMapPrototypeContract.IsValidDynamicEntity(entity);
                entities.Add(entity);
            }
            Entities = entities;
            return valid && ExpectedCount > 0 && batchCount > 0 &&
                   StartIndex + batchCount <= ExpectedCount;
        }

        protected override void OnWrite()
        {
            int batchCount = Math.Min(Entities?.Count ?? 0, CoopCampaignMapPrototypeContract.MaxDynamicBatchSize);
            WriteIntToPacket(ProtocolVersion, ProtocolCompression);
            WriteIntToPacket(Math.Max(0, Revision), RevisionCompression);
            WriteIntToPacket(Math.Max(0, Math.Min(CoopCampaignMapPrototypeContract.MaxDynamicEntities - 1, StartIndex)), IndexCompression);
            WriteIntToPacket(Math.Max(1, Math.Min(CoopCampaignMapPrototypeContract.MaxDynamicEntities, ExpectedCount)), TotalCompression);
            WriteIntToPacket(batchCount, BatchCompression);
            for (int index = 0; index < batchCount; index++)
            {
                CoopCampaignMapPrototypeDynamicEntityState entity = Entities[index];
                WriteStringToPacket(CoopCampaignMapPrototypeContract.BoundEntityText(entity?.EntityId, CoopCampaignMapPrototypeContract.MaxEntityIdCharacters, "invalid"));
                WriteIntToPacket(ClampUnit(entity?.NormalizedX ?? 0), UnitCompression);
                WriteIntToPacket(ClampUnit(entity?.NormalizedY ?? 0), UnitCompression);
                WriteIntToPacket(ClampUnit(entity?.Heading ?? 0), UnitCompression);
                WriteIntToPacket(ClampSize(entity?.PartySize ?? 0), SizeCompression);
                WriteBoolToPacket(entity?.IsVisible ?? false);
                WriteBoolToPacket(entity?.IsMoving ?? false);
                WriteIntToPacket(ClampSize(entity?.ArmyPartyCount ?? 0), SizeCompression);
                WriteIntToPacket(ClampSize(entity?.ArmyTotalSize ?? 0), SizeCompression);
                WriteIntToPacket(ClampUnit(entity?.ArmyCohesion ?? 0), UnitCompression);
                WriteIntToPacket(Math.Max(0, entity?.AppearanceRevision ?? 0), RevisionCompression);
                WriteIntToPacket(Math.Max(0, entity?.InformationRevision ?? 0), RevisionCompression);
            }
        }

        protected override MultiplayerMessageFilter OnGetLogFilter() => MultiplayerMessageFilter.Mission;
        protected override string OnGetLogFormat() => "CoopCampaignMapDynamicBatch Revision=" + Revision + " Start=" + StartIndex + " Count=" + (Entities?.Count ?? 0);

        private static int ClampUnit(int value) => value < 0 ? 0 : value > CoopCampaignMapPrototypeContract.UnitScale ? CoopCampaignMapPrototypeContract.UnitScale : value;
        private static int ClampSize(int value) => value < 0 ? 0 : value > CoopCampaignMapPrototypeContract.MaximumPartySize ? CoopCampaignMapPrototypeContract.MaximumPartySize : value;
    }
}
