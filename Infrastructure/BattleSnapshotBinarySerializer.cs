using System;
using System.Collections.Generic;
using System.IO;
using CoopSpectator.Network.Messages;

namespace CoopSpectator.Infrastructure
{
    internal static class BattleSnapshotBinarySerializer
    {
        private const int Magic = 0x43534231; // "CSB1"
        private const int SchemaVersion = 15;

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
                    if (schemaVersion != 1 &&
                        schemaVersion != 2 &&
                        schemaVersion != 3 &&
                        schemaVersion != 4 &&
                        schemaVersion != 5 &&
                        schemaVersion != 6 &&
                        schemaVersion != 7 &&
                        schemaVersion != 8 &&
                        schemaVersion != 9 &&
                        schemaVersion != 10 &&
                        schemaVersion != 11 &&
                        schemaVersion != 12 &&
                        schemaVersion != 13 &&
                        schemaVersion != 14 &&
                        schemaVersion != SchemaVersion)
                    {
                        ModLogger.Info(
                            "BattleSnapshotBinarySerializer: unsupported schema version. " +
                            "Expected=" + SchemaVersion +
                            " Actual=" + schemaVersion);
                        return false;
                    }

                    snapshot = ReadBattleSnapshot(reader, schemaVersion);
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
            WriteString(writer, snapshot.BattleInstanceId);
            writer.Write(snapshot.CasualtyRulesVersion);
            writer.Write(snapshot.BattleDeathDifficulty);
            writer.Write(snapshot.ClanMemberDeathChanceMultiplier);
            writer.Write(snapshot.IsPlayerMapEvent);
            writer.Write(snapshot.StoryModeTutorialProtectionEnabled);
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
            writer.Write(snapshot.PlayerTroopsReceivedDamageMultiplier);
            writer.Write(snapshot.HasCampaignTimeOfDay);
            writer.Write(snapshot.CampaignTimeOfDay);
            WriteString(writer, snapshot.CampaignTimeOfDaySource);
            WriteCampaignAtmosphere(writer, snapshot.CampaignAtmosphere);
            WriteBattleScenarioContext(writer, snapshot.ScenarioContext);
            WriteList(writer, snapshot.CraftedWeapons, WriteCraftedWeaponSnapshot);
            WriteList(writer, snapshot.Sides, WriteBattleSide);
            WriteList(writer, snapshot.FrozenCaptainEntryIds, WriteString);
            WriteList(writer, snapshot.FrozenCaptainCombatGroups, WriteFrozenCaptainCombatGroup);
        }

        private static BattleSnapshotMessage ReadBattleSnapshot(BinaryReader reader, int schemaVersion)
        {
            return new BattleSnapshotMessage
            {
                BattleId = ReadString(reader),
                BattleInstanceId = schemaVersion >= 12 ? ReadString(reader) : null,
                CasualtyRulesVersion = schemaVersion >= 12 ? reader.ReadInt32() : 0,
                BattleDeathDifficulty = schemaVersion >= 12 ? reader.ReadInt32() : 2,
                ClanMemberDeathChanceMultiplier = schemaVersion >= 12 ? reader.ReadSingle() : 0f,
                IsPlayerMapEvent = schemaVersion >= 12 && reader.ReadBoolean(),
                StoryModeTutorialProtectionEnabled = schemaVersion >= 12 && reader.ReadBoolean(),
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
                PlayerTroopsReceivedDamageMultiplier = schemaVersion >= 2 ? reader.ReadSingle() : 1f,
                HasCampaignTimeOfDay = schemaVersion >= 10 && reader.ReadBoolean(),
                CampaignTimeOfDay = schemaVersion >= 10 ? reader.ReadSingle() : -1f,
                CampaignTimeOfDaySource = schemaVersion >= 10 ? ReadString(reader) : null,
                CampaignAtmosphere = schemaVersion >= 11 ? ReadCampaignAtmosphere(reader) : null,
                ScenarioContext = schemaVersion >= 4 ? ReadBattleScenarioContext(reader, schemaVersion) : null,
                CraftedWeapons = schemaVersion >= 9
                    ? ReadList(reader, ReadCraftedWeaponSnapshot) ?? new List<CraftedWeaponSnapshotMessage>()
                    : new List<CraftedWeaponSnapshotMessage>(),
                Sides = ReadList(reader, itemReader => ReadBattleSide(itemReader, schemaVersion)) ?? new List<BattleSideSnapshotMessage>(),
                FrozenCaptainEntryIds = schemaVersion >= 13
                    ? ReadList(reader, ReadString) ?? new List<string>()
                    : new List<string>(),
                FrozenCaptainCombatGroups = schemaVersion >= 14
                    ? ReadList(reader, ReadFrozenCaptainCombatGroup) ?? new List<FrozenCaptainCombatGroupSnapshotMessage>()
                    : new List<FrozenCaptainCombatGroupSnapshotMessage>()
            };
        }

        private static void WriteFrozenCaptainCombatGroup(
            BinaryWriter writer,
            FrozenCaptainCombatGroupSnapshotMessage group)
        {
            WriteString(writer, group?.CombatGroupId);
            WriteList(writer, group?.Effects, WriteCaptainPerkEffect);
        }

        private static FrozenCaptainCombatGroupSnapshotMessage ReadFrozenCaptainCombatGroup(BinaryReader reader)
        {
            return new FrozenCaptainCombatGroupSnapshotMessage
            {
                CombatGroupId = ReadString(reader),
                Effects = ReadList(reader, ReadCaptainPerkEffect) ?? new List<CaptainPerkEffectSnapshotMessage>()
            };
        }

        private static void WriteCampaignAtmosphere(
            BinaryWriter writer,
            CampaignAtmosphereSnapshotMessage atmosphere)
        {
            writer.Write(atmosphere != null);
            if (atmosphere == null)
                return;

            WriteString(writer, atmosphere.Source);
            writer.Write(atmosphere.Seed);
            WriteString(writer, atmosphere.AtmosphereName);
            WriteString(writer, atmosphere.InterpolatedAtmosphereName);
            writer.Write(atmosphere.SunAltitude);
            writer.Write(atmosphere.SunAngle);
            writer.Write(atmosphere.SunColorX);
            writer.Write(atmosphere.SunColorY);
            writer.Write(atmosphere.SunColorZ);
            writer.Write(atmosphere.SunBrightness);
            writer.Write(atmosphere.SunMaxBrightness);
            writer.Write(atmosphere.SunSize);
            writer.Write(atmosphere.SunRayStrength);
            writer.Write(atmosphere.RainDensity);
            writer.Write(atmosphere.SnowDensity);
            writer.Write(atmosphere.AmbientEnvironmentMultiplier);
            writer.Write(atmosphere.AmbientColorX);
            writer.Write(atmosphere.AmbientColorY);
            writer.Write(atmosphere.AmbientColorZ);
            writer.Write(atmosphere.AmbientMieScatterStrength);
            writer.Write(atmosphere.AmbientRayleighConstant);
            writer.Write(atmosphere.FogDensity);
            writer.Write(atmosphere.FogColorX);
            writer.Write(atmosphere.FogColorY);
            writer.Write(atmosphere.FogColorZ);
            writer.Write(atmosphere.FogFalloff);
            writer.Write(atmosphere.SkyBrightness);
            writer.Write(atmosphere.NauticalWaveStrength);
            writer.Write(atmosphere.NauticalWindX);
            writer.Write(atmosphere.NauticalWindY);
            writer.Write(atmosphere.NauticalCanUseLowAltitudeAtmosphere);
            writer.Write(atmosphere.NauticalUseSceneWindDirection);
            writer.Write(atmosphere.NauticalIsRiverBattle);
            writer.Write(atmosphere.NauticalIsInsideStorm);
            writer.Write(atmosphere.NauticalUsesNavalSimulatedWater);
            writer.Write(atmosphere.TimeOfDay);
            writer.Write(atmosphere.NightTimeFactor);
            writer.Write(atmosphere.DrynessFactor);
            writer.Write(atmosphere.WinterTimeFactor);
            writer.Write(atmosphere.Season);
            writer.Write(atmosphere.AreaTemperature);
            writer.Write(atmosphere.AreaHumidity);
            writer.Write(atmosphere.PostProcessMinExposure);
            writer.Write(atmosphere.PostProcessMaxExposure);
            writer.Write(atmosphere.PostProcessBrightpassThreshold);
            writer.Write(atmosphere.PostProcessMiddleGray);
        }

        private static CampaignAtmosphereSnapshotMessage ReadCampaignAtmosphere(BinaryReader reader)
        {
            if (!reader.ReadBoolean())
                return null;

            return new CampaignAtmosphereSnapshotMessage
            {
                Source = ReadString(reader),
                Seed = reader.ReadUInt32(),
                AtmosphereName = ReadString(reader),
                InterpolatedAtmosphereName = ReadString(reader),
                SunAltitude = reader.ReadSingle(),
                SunAngle = reader.ReadSingle(),
                SunColorX = reader.ReadSingle(),
                SunColorY = reader.ReadSingle(),
                SunColorZ = reader.ReadSingle(),
                SunBrightness = reader.ReadSingle(),
                SunMaxBrightness = reader.ReadSingle(),
                SunSize = reader.ReadSingle(),
                SunRayStrength = reader.ReadSingle(),
                RainDensity = reader.ReadSingle(),
                SnowDensity = reader.ReadSingle(),
                AmbientEnvironmentMultiplier = reader.ReadSingle(),
                AmbientColorX = reader.ReadSingle(),
                AmbientColorY = reader.ReadSingle(),
                AmbientColorZ = reader.ReadSingle(),
                AmbientMieScatterStrength = reader.ReadSingle(),
                AmbientRayleighConstant = reader.ReadSingle(),
                FogDensity = reader.ReadSingle(),
                FogColorX = reader.ReadSingle(),
                FogColorY = reader.ReadSingle(),
                FogColorZ = reader.ReadSingle(),
                FogFalloff = reader.ReadSingle(),
                SkyBrightness = reader.ReadSingle(),
                NauticalWaveStrength = reader.ReadSingle(),
                NauticalWindX = reader.ReadSingle(),
                NauticalWindY = reader.ReadSingle(),
                NauticalCanUseLowAltitudeAtmosphere = reader.ReadInt32(),
                NauticalUseSceneWindDirection = reader.ReadInt32(),
                NauticalIsRiverBattle = reader.ReadInt32(),
                NauticalIsInsideStorm = reader.ReadInt32(),
                NauticalUsesNavalSimulatedWater = reader.ReadInt32(),
                TimeOfDay = reader.ReadSingle(),
                NightTimeFactor = reader.ReadSingle(),
                DrynessFactor = reader.ReadSingle(),
                WinterTimeFactor = reader.ReadSingle(),
                Season = reader.ReadInt32(),
                AreaTemperature = reader.ReadSingle(),
                AreaHumidity = reader.ReadSingle(),
                PostProcessMinExposure = reader.ReadSingle(),
                PostProcessMaxExposure = reader.ReadSingle(),
                PostProcessBrightpassThreshold = reader.ReadSingle(),
                PostProcessMiddleGray = reader.ReadSingle()
            };
        }

        private static void WriteCraftedWeaponSnapshot(BinaryWriter writer, CraftedWeaponSnapshotMessage craftedWeapon)
        {
            WriteString(writer, craftedWeapon?.Key);
            WriteString(writer, craftedWeapon?.OriginalItemId);
            WriteString(writer, craftedWeapon?.MirrorItemId);
            WriteString(writer, craftedWeapon?.Name);
            WriteString(writer, craftedWeapon?.CraftingTemplateId);
            WriteString(writer, craftedWeapon?.CultureId);
            WriteString(writer, craftedWeapon?.ModifierGroupId);
            WriteString(writer, craftedWeapon?.WeaponDesignHash);
            writer.Write(craftedWeapon?.IsCraftedByPlayer ?? false);
            WriteList(writer, craftedWeapon?.Pieces, WriteCraftedWeaponPieceSnapshot);
        }

        private static CraftedWeaponSnapshotMessage ReadCraftedWeaponSnapshot(BinaryReader reader)
        {
            return new CraftedWeaponSnapshotMessage
            {
                Key = ReadString(reader),
                OriginalItemId = ReadString(reader),
                MirrorItemId = ReadString(reader),
                Name = ReadString(reader),
                CraftingTemplateId = ReadString(reader),
                CultureId = ReadString(reader),
                ModifierGroupId = ReadString(reader),
                WeaponDesignHash = ReadString(reader),
                IsCraftedByPlayer = reader.ReadBoolean(),
                Pieces = ReadList(reader, ReadCraftedWeaponPieceSnapshot) ?? new List<CraftedWeaponPieceSnapshotMessage>()
            };
        }

        private static void WriteCraftedWeaponPieceSnapshot(BinaryWriter writer, CraftedWeaponPieceSnapshotMessage piece)
        {
            WriteString(writer, piece?.PieceId);
            WriteString(writer, piece?.PieceType);
            writer.Write(piece?.ScalePercentage ?? 100);
        }

        private static CraftedWeaponPieceSnapshotMessage ReadCraftedWeaponPieceSnapshot(BinaryReader reader)
        {
            return new CraftedWeaponPieceSnapshotMessage
            {
                PieceId = ReadString(reader),
                PieceType = ReadString(reader),
                ScalePercentage = reader.ReadInt32()
            };
        }

        private static void WriteBattleScenarioContext(BinaryWriter writer, BattleScenarioContextMessage context)
        {
            writer.Write(context != null);
            if (context == null)
                return;

            WriteString(writer, context.CampaignBattleType);
            WriteString(writer, context.ScenarioKind);
            writer.Write(context.IsSiegeBattle);
            WriteString(writer, context.Source);
            WriteBattleSiegeContext(writer, context.SiegeContext);
        }

        private static BattleScenarioContextMessage ReadBattleScenarioContext(BinaryReader reader, int schemaVersion)
        {
            if (!reader.ReadBoolean())
                return null;

            return new BattleScenarioContextMessage
            {
                CampaignBattleType = ReadString(reader),
                ScenarioKind = ReadString(reader),
                IsSiegeBattle = reader.ReadBoolean(),
                Source = ReadString(reader),
                SiegeContext = ReadBattleSiegeContext(reader, schemaVersion)
            };
        }

        private static void WriteBattleSiegeContext(BinaryWriter writer, BattleSiegeContextMessage siegeContext)
        {
            writer.Write(siegeContext != null);
            if (siegeContext == null)
                return;

            WriteString(writer, siegeContext.SiegeSubtype);
            WriteString(writer, siegeContext.MissionShell);
            WriteString(writer, siegeContext.SettlementId);
            WriteString(writer, siegeContext.SettlementKind);
            WriteString(writer, siegeContext.SettlementCultureId);
            WriteString(writer, siegeContext.SceneLocationId);
            WriteString(writer, siegeContext.CurrentSiegeState);
            writer.Write(siegeContext.WallLevel);
            writer.Write(siegeContext.HasAnySiegeTower);
            WriteList(writer, siegeContext.WallHitPointRatios, (listWriter, value) => listWriter.Write(value));
            WriteList(writer, siegeContext.AttackerSiegeEngineTypeIds, WriteString);
            WriteList(writer, siegeContext.DefenderSiegeEngineTypeIds, WriteString);
            WriteList(writer, siegeContext.AttackerSiegeEngines, WriteBattleSiegeEngineSnapshot);
            WriteList(writer, siegeContext.DefenderSiegeEngines, WriteBattleSiegeEngineSnapshot);
            writer.Write(siegeContext.HasMissionInitializerRecord);
            WriteString(writer, siegeContext.MissionInitializerSource);
            WriteString(writer, siegeContext.MissionInitializerSceneName);
            WriteString(writer, siegeContext.MissionInitializerSceneLevels);
            writer.Write(siegeContext.MissionInitializerSceneUpgradeLevel);
            writer.Write(siegeContext.MissionInitializerPlayingInCampaignMode);
            writer.Write(siegeContext.MissionInitializerSceneHasMapPatch);
            writer.Write(siegeContext.MissionInitializerDecalAtlasGroup);
            writer.Write(siegeContext.MissionInitializerTerrainType);
            writer.Write(siegeContext.DefenderTroopNumberForSuccessfulPullBack);
            writer.Write(siegeContext.LordsHallAreaLostRatio);
            writer.Write(siegeContext.LordsHallAttackerDefenderTroopCountRatio);
            writer.Write(siegeContext.LordsHallDefenderMaxArcherRatio);
            writer.Write(siegeContext.LordsHallMaxDefenderSideTroopCount);
            writer.Write(siegeContext.LordsHallMaxDefenderArcherCount);
            writer.Write(siegeContext.LordsHallMaxAttackerSideTroopCount);
        }

        private static BattleSiegeContextMessage ReadBattleSiegeContext(BinaryReader reader, int schemaVersion)
        {
            if (!reader.ReadBoolean())
                return null;

            return new BattleSiegeContextMessage
            {
                SiegeSubtype = ReadString(reader),
                MissionShell = schemaVersion >= 5 ? ReadString(reader) : string.Empty,
                SettlementId = ReadString(reader),
                SettlementKind = ReadString(reader),
                SettlementCultureId = ReadString(reader),
                SceneLocationId = ReadString(reader),
                CurrentSiegeState = ReadString(reader),
                WallLevel = reader.ReadInt32(),
                HasAnySiegeTower = reader.ReadBoolean(),
                WallHitPointRatios = ReadList(reader, listReader => listReader.ReadSingle()) ?? new List<float>(),
                AttackerSiegeEngineTypeIds = ReadList(reader, ReadString) ?? new List<string>(),
                DefenderSiegeEngineTypeIds = ReadList(reader, ReadString) ?? new List<string>(),
                AttackerSiegeEngines = schemaVersion >= 6
                    ? ReadList(reader, ReadBattleSiegeEngineSnapshot) ?? new List<BattleSiegeEngineSnapshotMessage>()
                    : new List<BattleSiegeEngineSnapshotMessage>(),
                DefenderSiegeEngines = schemaVersion >= 6
                    ? ReadList(reader, ReadBattleSiegeEngineSnapshot) ?? new List<BattleSiegeEngineSnapshotMessage>()
                    : new List<BattleSiegeEngineSnapshotMessage>(),
                HasMissionInitializerRecord = schemaVersion >= 7 && reader.ReadBoolean(),
                MissionInitializerSource = schemaVersion >= 7 ? ReadString(reader) : string.Empty,
                MissionInitializerSceneName = schemaVersion >= 7 ? ReadString(reader) : string.Empty,
                MissionInitializerSceneLevels = schemaVersion >= 7 ? ReadString(reader) : string.Empty,
                MissionInitializerSceneUpgradeLevel = schemaVersion >= 7 ? reader.ReadInt32() : -1,
                MissionInitializerPlayingInCampaignMode = schemaVersion >= 7 && reader.ReadBoolean(),
                MissionInitializerSceneHasMapPatch = schemaVersion >= 7 && reader.ReadBoolean(),
                MissionInitializerDecalAtlasGroup = schemaVersion >= 7 ? reader.ReadInt32() : -1,
                MissionInitializerTerrainType = schemaVersion >= 7 ? reader.ReadInt32() : -1,
                DefenderTroopNumberForSuccessfulPullBack = schemaVersion >= 15 ? reader.ReadInt32() : 20,
                LordsHallAreaLostRatio = schemaVersion >= 15 ? reader.ReadSingle() : 3f,
                LordsHallAttackerDefenderTroopCountRatio = schemaVersion >= 15 ? reader.ReadSingle() : 0.7f,
                LordsHallDefenderMaxArcherRatio = schemaVersion >= 15 ? reader.ReadSingle() : 0.7f,
                LordsHallMaxDefenderSideTroopCount = schemaVersion >= 15 ? reader.ReadInt32() : 27,
                LordsHallMaxDefenderArcherCount = schemaVersion >= 15 ? reader.ReadInt32() : 19,
                LordsHallMaxAttackerSideTroopCount = schemaVersion >= 15 ? reader.ReadInt32() : 19
            };
        }

        private static void WriteBattleSiegeEngineSnapshot(BinaryWriter writer, BattleSiegeEngineSnapshotMessage siegeEngine)
        {
            WriteString(writer, siegeEngine?.EngineTypeId);
            writer.Write(siegeEngine?.Index ?? -1);
            writer.Write(siegeEngine?.Health ?? 0f);
            writer.Write(siegeEngine?.InitialHealth ?? 0f);
            writer.Write(siegeEngine?.MaxHealth ?? 0f);
        }

        private static BattleSiegeEngineSnapshotMessage ReadBattleSiegeEngineSnapshot(BinaryReader reader)
        {
            return new BattleSiegeEngineSnapshotMessage
            {
                EngineTypeId = ReadString(reader),
                Index = reader.ReadInt32(),
                Health = reader.ReadSingle(),
                InitialHealth = reader.ReadSingle(),
                MaxHealth = reader.ReadSingle()
            };
        }

        private static void WriteBattleSide(BinaryWriter writer, BattleSideSnapshotMessage side)
        {
            WriteString(writer, side?.SideId);
            WriteString(writer, side?.SideText);
            WriteString(writer, side?.LeaderPartyId);
            WriteString(writer, side?.CultureId);
            writer.Write(side?.Color ?? 0u);
            writer.Write(side?.Color2 ?? 0u);
            WriteString(writer, side?.BannerCode);
            WriteString(writer, side?.AppearanceSource);
            writer.Write(side?.SideMorale ?? 0f);
            writer.Write(side?.IsPlayerSide ?? false);
            writer.Write(side?.TotalManCount ?? 0);
            WriteList(writer, side?.MissionReadyEntryOrder, WriteString);
            WriteList(writer, side?.Parties, WriteBattleParty);
            WriteList(writer, side?.Troops, WriteTroopStack);
        }

        private static BattleSideSnapshotMessage ReadBattleSide(BinaryReader reader, int schemaVersion)
        {
            return new BattleSideSnapshotMessage
            {
                SideId = ReadString(reader),
                SideText = ReadString(reader),
                LeaderPartyId = ReadString(reader),
                CultureId = schemaVersion >= 3 ? ReadString(reader) : null,
                Color = schemaVersion >= 3 ? reader.ReadUInt32() : 0u,
                Color2 = schemaVersion >= 3 ? reader.ReadUInt32() : 0u,
                BannerCode = schemaVersion >= 3 ? ReadString(reader) : null,
                AppearanceSource = schemaVersion >= 3 ? ReadString(reader) : null,
                SideMorale = reader.ReadSingle(),
                IsPlayerSide = reader.ReadBoolean(),
                TotalManCount = reader.ReadInt32(),
                MissionReadyEntryOrder = ReadList(reader, ReadString) ?? new List<string>(),
                Parties = ReadList(reader, itemReader => ReadBattleParty(itemReader, schemaVersion)) ?? new List<BattlePartySnapshotMessage>(),
                Troops = ReadList(reader, itemReader => ReadTroopStack(itemReader, schemaVersion)) ?? new List<TroopStackInfo>()
            };
        }

        private static void WriteBattleParty(BinaryWriter writer, BattlePartySnapshotMessage party)
        {
            WriteString(writer, party?.PartyId);
            WriteString(writer, party?.PartyName);
            writer.Write(party?.IsMainParty ?? false);
            writer.Write(party?.HasMobileParty ?? false);
            writer.Write(party?.TotalManCount ?? 0);
            WriteBattlePartyModifier(writer, party?.Modifiers ?? new BattlePartyModifierSnapshotMessage());
            WriteList(writer, party?.Troops, WriteTroopStack);
            WriteString(writer, party?.CombatGroupId);
        }

        private static BattlePartySnapshotMessage ReadBattleParty(BinaryReader reader, int schemaVersion)
        {
            return new BattlePartySnapshotMessage
            {
                PartyId = ReadString(reader),
                PartyName = ReadString(reader),
                IsMainParty = reader.ReadBoolean(),
                HasMobileParty = schemaVersion >= 12 && reader.ReadBoolean(),
                TotalManCount = reader.ReadInt32(),
                Modifiers = ReadBattlePartyModifier(reader, schemaVersion) ?? new BattlePartyModifierSnapshotMessage(),
                Troops = ReadList(reader, itemReader => ReadTroopStack(itemReader, schemaVersion)) ?? new List<TroopStackInfo>(),
                CombatGroupId = schemaVersion >= 13 ? ReadString(reader) : null
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
            writer.Write(modifier?.SurvivalMedicineSkill ?? 0);
        }

        private static BattlePartyModifierSnapshotMessage ReadBattlePartyModifier(BinaryReader reader, int schemaVersion)
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
                SurgeonPerkIds = ReadList(reader, ReadString) ?? new List<string>(),
                SurvivalMedicineSkill = schemaVersion >= 12 ? reader.ReadInt32() : 0
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
            writer.Write(troop?.ServerCreateContractResolved ?? false);
            writer.Write(troop?.ServerCreateUseStringIdExactEquipmentPath ?? false);
            writer.Write(troop?.ServerCreateInjectEquipment ?? false);
            writer.Write(troop?.ServerCreatePreSpawnIncludesWeapons ?? false);
            writer.Write(troop?.ServerCreatePreSpawnIncludesArmorVisuals ?? false);
            writer.Write(troop?.ServerCreatePreSpawnIncludesCapeVisual ?? false);
            writer.Write(troop?.ServerCreatePreSpawnIncludesMountVisuals ?? false);
            writer.Write(troop?.ServerCreatePayloadDiagnosticActive ?? false);
            WriteString(writer, troop?.ServerCreateRequestedProfile);
            WriteString(writer, troop?.ServerCreateEffectiveProfile);
            writer.Write(troop?.IsHero ?? false);
            writer.Write(troop?.Count ?? 0);
            writer.Write(troop?.WoundedCount ?? 0);
            WriteString(writer, troop?.CampaignFormationClass);
            WriteString(writer, troop?.CombatItem0CraftedWeaponKey);
            WriteString(writer, troop?.CombatItem0ModifierId);
            WriteString(writer, troop?.CombatItem1CraftedWeaponKey);
            WriteString(writer, troop?.CombatItem1ModifierId);
            WriteString(writer, troop?.CombatItem2CraftedWeaponKey);
            WriteString(writer, troop?.CombatItem2ModifierId);
            WriteString(writer, troop?.CombatItem3CraftedWeaponKey);
            WriteString(writer, troop?.CombatItem3ModifierId);
            writer.Write(troop?.CharacterLevel ?? 0);
            writer.Write(troop?.HeroTotalArmorSum ?? 0f);
            writer.Write(troop?.IsPlayerCharacter ?? false);
            writer.Write(troop?.IsPlayerClanHero ?? false);
            writer.Write(troop?.HeroCanDieInBattle ?? true);
            writer.Write(troop?.ForceUnconscious ?? false);
            WriteList(writer, troop?.CaptainPerkEffects, WriteCaptainPerkEffect);
        }

        private static TroopStackInfo ReadTroopStack(BinaryReader reader, int schemaVersion)
        {
            var troop = new TroopStackInfo
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
                ServerCreateContractResolved = reader.ReadBoolean(),
                ServerCreateUseStringIdExactEquipmentPath = reader.ReadBoolean(),
                ServerCreateInjectEquipment = reader.ReadBoolean(),
                ServerCreatePreSpawnIncludesWeapons = reader.ReadBoolean(),
                ServerCreatePreSpawnIncludesArmorVisuals = reader.ReadBoolean(),
                ServerCreatePreSpawnIncludesCapeVisual = reader.ReadBoolean(),
                ServerCreatePreSpawnIncludesMountVisuals = reader.ReadBoolean(),
                ServerCreatePayloadDiagnosticActive = reader.ReadBoolean(),
                ServerCreateRequestedProfile = ReadString(reader),
                ServerCreateEffectiveProfile = ReadString(reader),
                IsHero = reader.ReadBoolean(),
                Count = reader.ReadInt32(),
                WoundedCount = reader.ReadInt32()
            };

            if (schemaVersion >= 8)
                troop.CampaignFormationClass = ReadString(reader);

            if (schemaVersion >= 9)
            {
                troop.CombatItem0CraftedWeaponKey = ReadString(reader);
                troop.CombatItem0ModifierId = ReadString(reader);
                troop.CombatItem1CraftedWeaponKey = ReadString(reader);
                troop.CombatItem1ModifierId = ReadString(reader);
                troop.CombatItem2CraftedWeaponKey = ReadString(reader);
                troop.CombatItem2ModifierId = ReadString(reader);
                troop.CombatItem3CraftedWeaponKey = ReadString(reader);
                troop.CombatItem3ModifierId = ReadString(reader);
            }

            if (schemaVersion >= 12)
            {
                troop.CharacterLevel = reader.ReadInt32();
                troop.HeroTotalArmorSum = reader.ReadSingle();
                troop.IsPlayerCharacter = reader.ReadBoolean();
                troop.IsPlayerClanHero = reader.ReadBoolean();
                troop.HeroCanDieInBattle = reader.ReadBoolean();
                troop.ForceUnconscious = reader.ReadBoolean();
            }
            else
            {
                troop.CharacterLevel = troop.HeroLevel > 0 ? troop.HeroLevel : troop.Tier;
                troop.HeroCanDieInBattle = true;
            }

            if (schemaVersion >= 13)
            {
                troop.CaptainPerkEffects =
                    ReadList(reader, ReadCaptainPerkEffect) ??
                    new List<CaptainPerkEffectSnapshotMessage>();
            }

            return troop;
        }

        private static void WriteCaptainPerkEffect(BinaryWriter writer, CaptainPerkEffectSnapshotMessage effect)
        {
            WriteString(writer, effect?.PerkId);
            writer.Write(effect?.Bonus ?? 0f);
            WriteString(writer, effect?.IncrementType);
        }

        private static CaptainPerkEffectSnapshotMessage ReadCaptainPerkEffect(BinaryReader reader)
        {
            return new CaptainPerkEffectSnapshotMessage
            {
                PerkId = ReadString(reader),
                Bonus = reader.ReadSingle(),
                IncrementType = ReadString(reader)
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
