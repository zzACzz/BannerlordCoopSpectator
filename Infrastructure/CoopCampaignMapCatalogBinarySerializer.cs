using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CoopSpectator.Infrastructure
{
    public static class CoopCampaignMapCatalogBinarySerializer
    {
        private const int Magic = 0x434D4331; // "CMC1"
        public const int SchemaVersion = 1;
        private static readonly Encoding StrictUtf8 =
            new UTF8Encoding(false, true);

        public static bool TrySerialize(
            int revision,
            IReadOnlyList<CoopCampaignMapPrototypeCatalogEntityState> entities,
            out byte[] payloadBytes,
            out string reason)
        {
            payloadBytes = Array.Empty<byte>();
            reason = null;
            int count = entities?.Count ?? 0;
            if (revision < 0 ||
                count > CoopCampaignMapPrototypeContract.MaxCatalogEntities)
            {
                reason = "catalog-header";
                return false;
            }

            var observedIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < count; index++)
            {
                CoopCampaignMapPrototypeCatalogEntityState entity =
                    entities[index];
                if (!CoopCampaignMapPrototypeContract.IsValidCatalogEntity(entity) ||
                    !observedIds.Add(entity.EntityId))
                {
                    reason = "catalog-entity";
                    return false;
                }
            }

            try
            {
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream, StrictUtf8))
                {
                    writer.Write(Magic);
                    writer.Write(SchemaVersion);
                    writer.Write(revision);
                    writer.Write(count);
                    for (int index = 0; index < count; index++)
                        WriteEntity(writer, entities[index]);
                    writer.Flush();
                    if (stream.Length <= 0 ||
                        stream.Length >
                            CoopCampaignMapPrototypeContract.MaxCatalogLogicalBytes)
                    {
                        reason = "logical-size";
                        return false;
                    }

                    payloadBytes = stream.ToArray();
                    return true;
                }
            }
            catch
            {
                payloadBytes = Array.Empty<byte>();
                reason = "serialize-failed";
                return false;
            }
        }

        public static bool TryDeserialize(
            byte[] payloadBytes,
            out int revision,
            out List<CoopCampaignMapPrototypeCatalogEntityState> entities,
            out string reason)
        {
            revision = -1;
            entities = null;
            reason = null;
            if (payloadBytes == null ||
                payloadBytes.Length <= 0 ||
                payloadBytes.Length >
                    CoopCampaignMapPrototypeContract.MaxCatalogLogicalBytes)
            {
                reason = "logical-size";
                return false;
            }

            try
            {
                using (var stream = new MemoryStream(payloadBytes, false))
                using (var reader = new BinaryReader(stream, StrictUtf8))
                {
                    if (reader.ReadInt32() != Magic)
                    {
                        reason = "magic";
                        return false;
                    }
                    if (reader.ReadInt32() != SchemaVersion)
                    {
                        reason = "schema";
                        return false;
                    }

                    revision = reader.ReadInt32();
                    int count = reader.ReadInt32();
                    if (revision < 0 ||
                        count < 0 ||
                        count >
                            CoopCampaignMapPrototypeContract.MaxCatalogEntities)
                    {
                        reason = "catalog-header";
                        return false;
                    }

                    var decoded =
                        new List<CoopCampaignMapPrototypeCatalogEntityState>(
                            count);
                    var observedIds = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);
                    for (int index = 0; index < count; index++)
                    {
                        CoopCampaignMapPrototypeCatalogEntityState entity =
                            ReadEntity(reader);
                        if (!CoopCampaignMapPrototypeContract.IsValidCatalogEntity(
                                entity) ||
                            !observedIds.Add(entity.EntityId))
                        {
                            reason = "catalog-entity";
                            return false;
                        }
                        decoded.Add(entity);
                    }

                    if (stream.Position != stream.Length)
                    {
                        reason = "trailing-data";
                        return false;
                    }

                    entities = decoded;
                    return true;
                }
            }
            catch
            {
                revision = -1;
                entities = null;
                reason = "deserialize-failed";
                return false;
            }
        }

        private static void WriteEntity(
            BinaryWriter writer,
            CoopCampaignMapPrototypeCatalogEntityState entity)
        {
            WriteString(writer, entity.EntityId);
            WriteString(writer, entity.DisplayName);
            writer.Write((byte)entity.Kind);
            writer.Write((byte)entity.SettlementNameplateSize);
            writer.Write((byte)entity.SettlementKind);
            writer.Write(entity.PrimaryColor);
            writer.Write(entity.SecondaryColor);
            WriteString(writer, entity.BannerCode);
            WriteString(writer, entity.VisualCharacterId);
            WriteString(writer, entity.CultureId);
            writer.Write((byte)entity.PartyVisualKind);
            WriteAgentVisual(writer, entity.HumanVisual);
            WriteAgentVisual(writer, entity.MountVisual);
            WriteAgentVisual(writer, entity.CaravanMountVisual);
            WriteString(writer, entity.FactionId);
            WriteString(writer, entity.FactionName);
            WriteString(writer, entity.OwnerName);
            WriteString(writer, entity.LeaderName);
            WriteString(writer, entity.ArmyId);
            WriteString(writer, entity.ArmyName);
            writer.Write(entity.IsArmyLeader);
            writer.Write(entity.SelectionRadius);
        }

        private static CoopCampaignMapPrototypeCatalogEntityState ReadEntity(
            BinaryReader reader)
        {
            return new CoopCampaignMapPrototypeCatalogEntityState
            {
                EntityId = ReadString(
                    reader,
                    CoopCampaignMapPrototypeContract.MaxEntityIdCharacters),
                DisplayName = ReadString(
                    reader,
                    CoopCampaignMapPrototypeContract.MaxEntityNameCharacters),
                Kind = (CoopCampaignMapPrototypeEntityKind)reader.ReadByte(),
                SettlementNameplateSize =
                    (CoopCampaignMapPrototypeSettlementNameplateSize)
                        reader.ReadByte(),
                SettlementKind =
                    (CoopCampaignMapPrototypeSettlementKind)reader.ReadByte(),
                PrimaryColor = reader.ReadUInt32(),
                SecondaryColor = reader.ReadUInt32(),
                BannerCode = ReadString(
                    reader,
                    CoopCampaignMapPrototypeContract.MaxBannerCodeCharacters),
                VisualCharacterId = ReadString(
                    reader,
                    CoopCampaignMapPrototypeContract
                        .MaxVisualCharacterIdCharacters),
                CultureId = ReadString(
                    reader,
                    CoopCampaignMapPrototypeContract.MaxCultureIdCharacters),
                PartyVisualKind =
                    (CoopCampaignMapPrototypePartyVisualKind)reader.ReadByte(),
                HumanVisual = ReadAgentVisual(reader),
                MountVisual = ReadAgentVisual(reader),
                CaravanMountVisual = ReadAgentVisual(reader),
                FactionId = ReadString(
                    reader,
                    CoopCampaignMapPrototypeContract
                        .MaxInformationTextCharacters),
                FactionName = ReadString(
                    reader,
                    CoopCampaignMapPrototypeContract
                        .MaxInformationTextCharacters),
                OwnerName = ReadString(
                    reader,
                    CoopCampaignMapPrototypeContract
                        .MaxInformationTextCharacters),
                LeaderName = ReadString(
                    reader,
                    CoopCampaignMapPrototypeContract
                        .MaxInformationTextCharacters),
                ArmyId = ReadString(
                    reader,
                    CoopCampaignMapPrototypeContract
                        .MaxInformationTextCharacters),
                ArmyName = ReadString(
                    reader,
                    CoopCampaignMapPrototypeContract
                        .MaxInformationTextCharacters),
                IsArmyLeader = reader.ReadBoolean(),
                SelectionRadius = reader.ReadInt32()
            };
        }

        private static void WriteAgentVisual(
            BinaryWriter writer,
            CoopCampaignMapPrototypeAgentVisualState visual)
        {
            writer.Write(visual != null);
            if (visual == null)
                return;

            WriteString(writer, visual.BodyProperties);
            writer.Write(visual.IsFemale);
            writer.Write((byte)visual.Race);
            writer.Write((byte)visual.SkeletonType);
            writer.Write((sbyte)visual.RightWieldedItemIndex);
            writer.Write((sbyte)visual.LeftWieldedItemIndex);
            WriteString(writer, visual.MountCreationKey);
            writer.Write(visual.HasBanner);
            writer.Write(visual.AddColorRandomness);
            for (int slot = 0;
                 slot < CoopCampaignMapPrototypeContract.EquipmentSlotCount;
                 slot++)
            {
                WriteString(writer, visual.EquipmentItemIds[slot]);
            }
        }

        private static CoopCampaignMapPrototypeAgentVisualState ReadAgentVisual(
            BinaryReader reader)
        {
            if (!reader.ReadBoolean())
                return null;

            var visual = new CoopCampaignMapPrototypeAgentVisualState
            {
                BodyProperties = ReadString(
                    reader,
                    CoopCampaignMapPrototypeContract
                        .MaxBodyPropertiesCharacters),
                IsFemale = reader.ReadBoolean(),
                Race = reader.ReadByte(),
                SkeletonType = reader.ReadByte(),
                RightWieldedItemIndex = reader.ReadSByte(),
                LeftWieldedItemIndex = reader.ReadSByte(),
                MountCreationKey = ReadString(
                    reader,
                    CoopCampaignMapPrototypeContract
                        .MaxMountCreationKeyCharacters),
                HasBanner = reader.ReadBoolean(),
                AddColorRandomness = reader.ReadBoolean(),
                EquipmentItemIds = new string[
                    CoopCampaignMapPrototypeContract.EquipmentSlotCount]
            };
            for (int slot = 0; slot < visual.EquipmentItemIds.Length; slot++)
            {
                visual.EquipmentItemIds[slot] = ReadString(
                    reader,
                    CoopCampaignMapPrototypeContract
                        .MaxVisualItemIdCharacters);
            }
            return visual;
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            byte[] bytes = StrictUtf8.GetBytes(value ?? string.Empty);
            writer.Write(bytes.Length);
            if (bytes.Length > 0)
                writer.Write(bytes);
        }

        private static string ReadString(
            BinaryReader reader,
            int maximumCharacters)
        {
            int byteCount = reader.ReadInt32();
            int maximumBytes = checked(maximumCharacters * 4);
            long remaining = reader.BaseStream.Length -
                             reader.BaseStream.Position;
            if (byteCount < 0 ||
                byteCount > maximumBytes ||
                byteCount > remaining)
            {
                throw new InvalidDataException("Invalid bounded string size.");
            }

            if (byteCount == 0)
                return string.Empty;
            byte[] bytes = reader.ReadBytes(byteCount);
            if (bytes.Length != byteCount)
                throw new EndOfStreamException();
            string value = StrictUtf8.GetString(bytes);
            if (value.Length > maximumCharacters)
                throw new InvalidDataException("Decoded string is too long.");
            return value;
        }
    }
}
