using System;
using System.Collections.Generic;
using System.IO;
using CoopSpectator.Network.Messages;

namespace CoopSpectator.Infrastructure
{
    internal static class BattleSnapshotBinarySerializer
    {
        private const int Magic = 0x43534231; // "CSB1"
        private const int SchemaVersion = 1;

        public static bool TrySerialize(BattleSnapshotMessage snapshot, out byte[] payloadBytes)
        {
            payloadBytes = Array.Empty<byte>();
            if (snapshot == null)
                return false;

            try
            {
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(Magic);
                    writer.Write(SchemaVersion);
                    WriteBattleSnapshot(writer, snapshot);
                    writer.Flush();
                    payloadBytes = stream.ToArray();
                    return payloadBytes.Length > 0;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleSnapshotBinarySerializer: serialize failed. Error=" + ex.Message);
                payloadBytes = Array.Empty<byte>();
                return false;
            }
        }

        public static bool TryDeserialize(byte[] payloadBytes, out BattleSnapshotMessage snapshot)
        {
            snapshot = null;
            if (payloadBytes == null || payloadBytes.Length <= 0)
                return false;

            try
            {
                using (var stream = new MemoryStream(payloadBytes, writable: false))
                using (var reader = new BinaryReader(stream))
                {
                    int magic = reader.ReadInt32();
                    if (magic != Magic)
                    {
                        ModLogger.Info(
                            "BattleSnapshotBinarySerializer: invalid magic. " +
                            "Expected=" + Magic +
                            " Actual=" + magic);
                        return false;
                    }

                    int schemaVersion = reader.ReadInt32();
                    if (schemaVersion != SchemaVersion)
                    {
                        ModLogger.Info(
                            "BattleSnapshotBinarySerializer: unsupported schema version. " +
                            "Expected=" + SchemaVersion +
                            " Actual=" + schemaVersion);
                        return false;
                    }

                    snapshot = ReadBattleSnapshot(reader);
                    return snapshot != null;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleSnapshotBinarySerializer: deserialize failed. Error=" + ex.Message);
                snapshot = null;
                return false;
            }
        }

        private static void WriteBattleSnapshot(BinaryWriter writer, BattleSnapshotMessage snapshot)
        {
            WriteString(writer, snapshot.BattleId);
            WriteString(writer, snapshot.BattleType);
            WriteString(writer, snapshot.MapScene);
            WriteString(writer, snapshot.WorldMapScene);
            writer.Write(snapshot.MapPatchSceneIndex);
            writer.Write(snapshot.MapPatchNormalizedX);
            writer.Write(snapshot.MapPatchNormalizedY);
            writer.Write(snapshot.HasPatchEncounterDirection);
            writer.Write(snapshot.PatchEncounterDirX);
            writer.Write(snapshot.PatchEncounterDirY);
            WriteString(writer, snapshot.PatchEncounterDirectionSource);
            WriteString(writer, snapshot.MultiplayerScene);
            WriteString(writer, snapshot.MultiplayerGameType);
            WriteString(writer, snapshot.MultiplayerSceneResolverSource);
            writer.Write(snapshot.BattleSizeBudget);
            writer.Write(snapshot.ReinforcementWaveCount);
            WriteString(writer, snapshot.BattleSizeBudgetSource);
            WriteString(writer, snapshot.PlayerSide);
            WriteList(writer, snapshot.Sides, WriteBattleSide);
        }

        private static BattleSnapshotMessage ReadBattleSnapshot(BinaryReader reader)
        {
            return new BattleSnapshotMessage
            {
                BattleId = ReadString(reader),
                BattleType = ReadString(reader),
                MapScene = ReadString(reader),
                WorldMapScene = ReadString(reader),
                MapPatchSceneIndex = reader.ReadInt32(),
                MapPatchNormalizedX = reader.ReadSingle(),
                MapPatchNormalizedY = reader.ReadSingle(),
                HasPatchEncounterDirection = reader.ReadBoolean(),
                PatchEncounterDirX = reader.ReadSingle(),
                PatchEncounterDirY = reader.ReadSingle(),
                PatchEncounterDirectionSource = ReadString(reader),
                MultiplayerScene = ReadString(reader),
                MultiplayerGameType = ReadString(reader),
                MultiplayerSceneResolverSource = ReadString(reader),
                BattleSizeBudget = reader.ReadInt32(),
                ReinforcementWaveCount = reader.ReadInt32(),
                BattleSizeBudgetSource = ReadString(reader),
                PlayerSide = ReadString(reader),
                Sides = ReadList(reader, ReadBattleSide) ?? new List<BattleSideSnapshotMessage>()
            };
        }

        private static void WriteBattleSide(BinaryWriter writer, BattleSideSnapshotMessage side)
        {
            WriteString(writer, side?.SideId);
            WriteString(writer, side?.SideText);
            WriteString(writer, side?.LeaderPartyId);
            writer.Write(side?.SideMorale ?? 0f);
            writer.Write(side?.IsPlayerSide ?? false);
            writer.Write(side?.TotalManCount ?? 0);
            WriteList(writer, side?.MissionReadyEntryOrder, WriteString);
            WriteList(writer, side?.Parties, WriteBattleParty);
            WriteList(writer, side?.Troops, WriteTroopStack);
        }

        private static BattleSideSnapshotMessage ReadBattleSide(BinaryReader reader)
        {
            return new BattleSideSnapshotMessage
            {
                SideId = ReadString(reader),
                SideText = ReadString(reader),
                LeaderPartyId = ReadString(reader),
                SideMorale = reader.ReadSingle(),
                IsPlayerSide = reader.ReadBoolean(),
                TotalManCount = reader.ReadInt32(),
                MissionReadyEntryOrder = ReadList(reader, ReadString) ?? new List<string>(),
                Parties = ReadList(reader, ReadBattleParty) ?? new List<BattlePartySnapshotMessage>(),
                Troops = ReadList(reader, ReadTroopStack) ?? new List<TroopStackInfo>()
            };
        }

        private static void WriteBattleParty(BinaryWriter writer, BattlePartySnapshotMessage party)
        {
            WriteString(writer, party?.PartyId);
            WriteString(writer, party?.PartyName);
            writer.Write(party?.IsMainParty ?? false);
            writer.Write(party?.TotalManCount ?? 0);
            WriteBattlePartyModifier(writer, party?.Modifiers ?? new BattlePartyModifierSnapshotMessage());
            WriteList(writer, party?.Troops, WriteTroopStack);
        }

        private static BattlePartySnapshotMessage ReadBattleParty(BinaryReader reader)
        {
            return new BattlePartySnapshotMessage
            {
                PartyId = ReadString(reader),
                PartyName = ReadString(reader),
                IsMainParty = reader.ReadBoolean(),
                TotalManCount = reader.ReadInt32(),
                Modifiers = ReadBattlePartyModifier(reader) ?? new BattlePartyModifierSnapshotMessage(),
                Troops = ReadList(reader, ReadTroopStack) ?? new List<TroopStackInfo>()
            };
        }

        private static void WriteBattlePartyModifier(BinaryWriter writer, BattlePartyModifierSnapshotMessage modifier)
        {
            WriteString(writer, modifier?.LeaderHeroId);
            WriteString(writer, modifier?.OwnerHeroId);
            WriteString(writer, modifier?.ScoutHeroId);
            WriteString(writer, modifier?.QuartermasterHeroId);
            WriteString(writer, modifier?.EngineerHeroId);
            WriteString(writer, modifier?.SurgeonHeroId);
            writer.Write(modifier?.Morale ?? 0f);
            writer.Write(modifier?.RecentEventsMorale ?? 0f);
            writer.Write(modifier?.MoraleChange ?? 0f);
            writer.Write(modifier?.ContributionToBattle ?? 0);
            writer.Write(modifier?.LeaderLeadershipSkill ?? 0);
            writer.Write(modifier?.LeaderTacticsSkill ?? 0);
            writer.Write(modifier?.ScoutScoutingSkill ?? 0);
            writer.Write(modifier?.QuartermasterStewardSkill ?? 0);
            writer.Write(modifier?.EngineerEngineeringSkill ?? 0);
            writer.Write(modifier?.SurgeonMedicineSkill ?? 0);
            WriteList(writer, modifier?.PartyLeaderPerkIds, WriteString);
            WriteList(writer, modifier?.ArmyCommanderPerkIds, WriteString);
            WriteList(writer, modifier?.CaptainPerkIds, WriteString);
            WriteList(writer, modifier?.ScoutPerkIds, WriteString);
            WriteList(writer, modifier?.QuartermasterPerkIds, WriteString);
            WriteList(writer, modifier?.EngineerPerkIds, WriteString);
            WriteList(writer, modifier?.SurgeonPerkIds, WriteString);
        }

        private static BattlePartyModifierSnapshotMessage ReadBattlePartyModifier(BinaryReader reader)
        {
            return new BattlePartyModifierSnapshotMessage
            {
                LeaderHeroId = ReadString(reader),
                OwnerHeroId = ReadString(reader),
                ScoutHeroId = ReadString(reader),
                QuartermasterHeroId = ReadString(reader),
                EngineerHeroId = ReadString(reader),
                SurgeonHeroId = ReadString(reader),
                Morale = reader.ReadSingle(),
                RecentEventsMorale = reader.ReadSingle(),
                MoraleChange = reader.ReadSingle(),
                ContributionToBattle = reader.ReadInt32(),
                LeaderLeadershipSkill = reader.ReadInt32(),
                LeaderTacticsSkill = reader.ReadInt32(),
                ScoutScoutingSkill = reader.ReadInt32(),
                QuartermasterStewardSkill = reader.ReadInt32(),
                EngineerEngineeringSkill = reader.ReadInt32(),
                SurgeonMedicineSkill = reader.ReadInt32(),
                PartyLeaderPerkIds = ReadList(reader, ReadString) ?? new List<string>(),
                ArmyCommanderPerkIds = ReadList(reader, ReadString) ?? new List<string>(),
                CaptainPerkIds = ReadList(reader, ReadString) ?? new List<string>(),
                ScoutPerkIds = ReadList(reader, ReadString) ?? new List<string>(),
                QuartermasterPerkIds = ReadList(reader, ReadString) ?? new List<string>(),
                EngineerPerkIds = ReadList(reader, ReadString) ?? new List<string>(),
                SurgeonPerkIds = ReadList(reader, ReadString) ?? new List<string>()
            };
        }

        private static void WriteTroopStack(BinaryWriter writer, TroopStackInfo troop)
        {
            WriteString(writer, troop?.EntryId);
            WriteString(writer, troop?.SideId);
            WriteString(writer, troop?.PartyId);
            WriteString(writer, troop?.CharacterId);
            WriteString(writer, troop?.OriginalCharacterId);
            WriteString(writer, troop?.SpawnTemplateId);
            WriteString(writer, troop?.TroopName);
            WriteString(writer, troop?.CultureId);
            WriteString(writer, troop?.HeroId);
            WriteString(writer, troop?.HeroRole);
            WriteString(writer, troop?.HeroOccupationId);
            WriteString(writer, troop?.HeroClanId);
            WriteString(writer, troop?.HeroTemplateId);
            WriteString(writer, troop?.HeroBodyProperties);
            writer.Write(troop?.HeroLevel ?? 0);
            writer.Write(troop?.HeroAge ?? 0f);
            writer.Write(troop?.HeroIsFemale ?? false);
            writer.Write(troop?.Tier ?? 0);
            writer.Write(troop?.IsMounted ?? false);
            writer.Write(troop?.IsRanged ?? false);
            writer.Write(troop?.HasShield ?? false);
            writer.Write(troop?.HasThrown ?? false);
            writer.Write(troop?.AttributeVigor ?? 0);
            writer.Write(troop?.AttributeControl ?? 0);
            writer.Write(troop?.AttributeEndurance ?? 0);
            writer.Write(troop?.SkillOneHanded ?? 0);
            writer.Write(troop?.SkillTwoHanded ?? 0);
            writer.Write(troop?.SkillPolearm ?? 0);
            writer.Write(troop?.SkillBow ?? 0);
            writer.Write(troop?.SkillCrossbow ?? 0);
            writer.Write(troop?.SkillThrowing ?? 0);
            writer.Write(troop?.SkillRiding ?? 0);
            writer.Write(troop?.SkillAthletics ?? 0);
            writer.Write(troop?.BaseHitPoints ?? 0);
            WriteList(writer, troop?.PerkIds, WriteString);
            WriteString(writer, troop?.CombatItem0Id);
            WriteNullableInt32(writer, troop?.CombatItem0Amount);
            WriteString(writer, troop?.CombatItem1Id);
            WriteNullableInt32(writer, troop?.CombatItem1Amount);
            WriteString(writer, troop?.CombatItem2Id);
            WriteNullableInt32(writer, troop?.CombatItem2Amount);
            WriteString(writer, troop?.CombatItem3Id);
            WriteNullableInt32(writer, troop?.CombatItem3Amount);
            WriteString(writer, troop?.CombatHeadId);
            WriteString(writer, troop?.CombatBodyId);
            WriteString(writer, troop?.CombatLegId);
            WriteString(writer, troop?.CombatGlovesId);
            WriteString(writer, troop?.CombatCapeId);
            WriteString(writer, troop?.CombatHorseId);
            WriteString(writer, troop?.CombatHorseHarnessId);
            writer.Write(troop?.IsHero ?? false);
            writer.Write(troop?.Count ?? 0);
            writer.Write(troop?.WoundedCount ?? 0);
        }

        private static TroopStackInfo ReadTroopStack(BinaryReader reader)
        {
            return new TroopStackInfo
            {
                EntryId = ReadString(reader),
                SideId = ReadString(reader),
                PartyId = ReadString(reader),
                CharacterId = ReadString(reader),
                OriginalCharacterId = ReadString(reader),
                SpawnTemplateId = ReadString(reader),
                TroopName = ReadString(reader),
                CultureId = ReadString(reader),
                HeroId = ReadString(reader),
                HeroRole = ReadString(reader),
                HeroOccupationId = ReadString(reader),
                HeroClanId = ReadString(reader),
                HeroTemplateId = ReadString(reader),
                HeroBodyProperties = ReadString(reader),
                HeroLevel = reader.ReadInt32(),
                HeroAge = reader.ReadSingle(),
                HeroIsFemale = reader.ReadBoolean(),
                Tier = reader.ReadInt32(),
                IsMounted = reader.ReadBoolean(),
                IsRanged = reader.ReadBoolean(),
                HasShield = reader.ReadBoolean(),
                HasThrown = reader.ReadBoolean(),
                AttributeVigor = reader.ReadInt32(),
                AttributeControl = reader.ReadInt32(),
                AttributeEndurance = reader.ReadInt32(),
                SkillOneHanded = reader.ReadInt32(),
                SkillTwoHanded = reader.ReadInt32(),
                SkillPolearm = reader.ReadInt32(),
                SkillBow = reader.ReadInt32(),
                SkillCrossbow = reader.ReadInt32(),
                SkillThrowing = reader.ReadInt32(),
                SkillRiding = reader.ReadInt32(),
                SkillAthletics = reader.ReadInt32(),
                BaseHitPoints = reader.ReadInt32(),
                PerkIds = ReadList(reader, ReadString) ?? new List<string>(),
                CombatItem0Id = ReadString(reader),
                CombatItem0Amount = ReadNullableInt32(reader),
                CombatItem1Id = ReadString(reader),
                CombatItem1Amount = ReadNullableInt32(reader),
                CombatItem2Id = ReadString(reader),
                CombatItem2Amount = ReadNullableInt32(reader),
                CombatItem3Id = ReadString(reader),
                CombatItem3Amount = ReadNullableInt32(reader),
                CombatHeadId = ReadString(reader),
                CombatBodyId = ReadString(reader),
                CombatLegId = ReadString(reader),
                CombatGlovesId = ReadString(reader),
                CombatCapeId = ReadString(reader),
                CombatHorseId = ReadString(reader),
                CombatHorseHarnessId = ReadString(reader),
                IsHero = reader.ReadBoolean(),
                Count = reader.ReadInt32(),
                WoundedCount = reader.ReadInt32()
            };
        }

        private static void WriteNullableInt32(BinaryWriter writer, int? value)
        {
            writer.Write(value.HasValue);
            if (value.HasValue)
                writer.Write(value.Value);
        }

        private static int? ReadNullableInt32(BinaryReader reader)
        {
            if (!reader.ReadBoolean())
                return null;

            return reader.ReadInt32();
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            bool hasValue = value != null;
            writer.Write(hasValue);
            if (hasValue)
                writer.Write(value);
        }

        private static string ReadString(BinaryReader reader)
        {
            return reader.ReadBoolean() ? reader.ReadString() : null;
        }

        private static void WriteList<T>(BinaryWriter writer, List<T> values, Action<BinaryWriter, T> writeItem)
        {
            if (values == null)
            {
                writer.Write(-1);
                return;
            }

            writer.Write(values.Count);
            for (int i = 0; i < values.Count; i++)
                writeItem(writer, values[i]);
        }

        private static List<T> ReadList<T>(BinaryReader reader, Func<BinaryReader, T> readItem)
        {
            int count = reader.ReadInt32();
            if (count < 0)
                return null;

            var values = new List<T>(count);
            for (int i = 0; i < count; i++)
                values.Add(readItem(reader));
            return values;
        }
    }
}
