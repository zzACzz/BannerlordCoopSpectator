using System.Collections.Generic;

namespace CoopSpectator.Network.Messages
{
    public sealed class BattleStartMessage
    {
        public string MapScene { get; set; }
        public string WorldMapScene { get; set; }
        public int MapPatchSceneIndex { get; set; } = -1;
        public float MapPatchNormalizedX { get; set; }
        public float MapPatchNormalizedY { get; set; }
        public bool HasPatchEncounterDirection { get; set; }
        public float PatchEncounterDirX { get; set; }
        public float PatchEncounterDirY { get; set; }
        public string PatchEncounterDirectionSource { get; set; }
        public string MultiplayerScene { get; set; }
        public string MultiplayerGameType { get; set; }
        public string MultiplayerSceneResolverSource { get; set; }
        public int BattleSizeBudget { get; set; }
        public int ReinforcementWaveCount { get; set; }
        public string BattleSizeBudgetSource { get; set; }
        public float MapX { get; set; }
        public float MapY { get; set; }
        public string PlayerSide { get; set; }
        public BattleScenarioContextMessage ScenarioContext { get; set; }
        public int ArmySize { get; set; }
        public List<TroopStackInfo> Troops { get; set; }
        public BattleSnapshotMessage Snapshot { get; set; }
    }

    public sealed class BattleSnapshotMessage
    {
        public string BattleId { get; set; }
        public string BattleInstanceId { get; set; }
        public int CasualtyRulesVersion { get; set; }
        public int BattleDeathDifficulty { get; set; } = 2;
        public float ClanMemberDeathChanceMultiplier { get; set; }
        public bool IsPlayerMapEvent { get; set; }
        public bool StoryModeTutorialProtectionEnabled { get; set; }
        public string BattleType { get; set; }
        public string MapScene { get; set; }
        public string WorldMapScene { get; set; }
        public int MapPatchSceneIndex { get; set; } = -1;
        public float MapPatchNormalizedX { get; set; }
        public float MapPatchNormalizedY { get; set; }
        public bool HasPatchEncounterDirection { get; set; }
        public float PatchEncounterDirX { get; set; }
        public float PatchEncounterDirY { get; set; }
        public string PatchEncounterDirectionSource { get; set; }
        public string MultiplayerScene { get; set; }
        public string MultiplayerGameType { get; set; }
        public string MultiplayerSceneResolverSource { get; set; }
        public int BattleSizeBudget { get; set; }
        public int ReinforcementWaveCount { get; set; }
        public string BattleSizeBudgetSource { get; set; }
        public string PlayerSide { get; set; }
        public float PlayerTroopsReceivedDamageMultiplier { get; set; } = 1f;
        public bool HasCampaignTimeOfDay { get; set; }
        public float CampaignTimeOfDay { get; set; } = -1f;
        public string CampaignTimeOfDaySource { get; set; }
        public CampaignAtmosphereSnapshotMessage CampaignAtmosphere { get; set; }
        public BattleScenarioContextMessage ScenarioContext { get; set; }
        public List<CraftedWeaponSnapshotMessage> CraftedWeapons { get; set; } = new List<CraftedWeaponSnapshotMessage>();
        public List<BattleSideSnapshotMessage> Sides { get; set; } = new List<BattleSideSnapshotMessage>();
    }

    public sealed class CampaignAtmosphereSnapshotMessage
    {
        public string Source { get; set; }
        public uint Seed { get; set; }
        public string AtmosphereName { get; set; }
        public string InterpolatedAtmosphereName { get; set; }
        public float SunAltitude { get; set; }
        public float SunAngle { get; set; }
        public float SunColorX { get; set; }
        public float SunColorY { get; set; }
        public float SunColorZ { get; set; }
        public float SunBrightness { get; set; }
        public float SunMaxBrightness { get; set; }
        public float SunSize { get; set; }
        public float SunRayStrength { get; set; }
        public float RainDensity { get; set; }
        public float SnowDensity { get; set; }
        public float AmbientEnvironmentMultiplier { get; set; }
        public float AmbientColorX { get; set; }
        public float AmbientColorY { get; set; }
        public float AmbientColorZ { get; set; }
        public float AmbientMieScatterStrength { get; set; }
        public float AmbientRayleighConstant { get; set; }
        public float FogDensity { get; set; }
        public float FogColorX { get; set; }
        public float FogColorY { get; set; }
        public float FogColorZ { get; set; }
        public float FogFalloff { get; set; }
        public float SkyBrightness { get; set; }
        public float NauticalWaveStrength { get; set; }
        public float NauticalWindX { get; set; }
        public float NauticalWindY { get; set; }
        public int NauticalCanUseLowAltitudeAtmosphere { get; set; }
        public int NauticalUseSceneWindDirection { get; set; }
        public int NauticalIsRiverBattle { get; set; }
        public int NauticalIsInsideStorm { get; set; }
        public int NauticalUsesNavalSimulatedWater { get; set; }
        public float TimeOfDay { get; set; }
        public float NightTimeFactor { get; set; }
        public float DrynessFactor { get; set; }
        public float WinterTimeFactor { get; set; }
        public int Season { get; set; }
        public float AreaTemperature { get; set; }
        public float AreaHumidity { get; set; }
        public float PostProcessMinExposure { get; set; }
        public float PostProcessMaxExposure { get; set; }
        public float PostProcessBrightpassThreshold { get; set; }
        public float PostProcessMiddleGray { get; set; }
    }

    public sealed class CraftedWeaponSnapshotMessage
    {
        public string Key { get; set; }
        public string OriginalItemId { get; set; }
        public string MirrorItemId { get; set; }
        public string Name { get; set; }
        public string CraftingTemplateId { get; set; }
        public string CultureId { get; set; }
        public string ModifierGroupId { get; set; }
        public string WeaponDesignHash { get; set; }
        public bool IsCraftedByPlayer { get; set; }
        public List<CraftedWeaponPieceSnapshotMessage> Pieces { get; set; } = new List<CraftedWeaponPieceSnapshotMessage>();
    }

    public sealed class CraftedWeaponPieceSnapshotMessage
    {
        public string PieceId { get; set; }
        public string PieceType { get; set; }
        public int ScalePercentage { get; set; } = 100;
    }

    public sealed class BattleScenarioContextMessage
    {
        public string CampaignBattleType { get; set; }
        public string ScenarioKind { get; set; }
        public bool IsSiegeBattle { get; set; }
        public string Source { get; set; }
        public BattleSiegeContextMessage SiegeContext { get; set; }

        public BattleScenarioContextMessage Clone()
        {
            return new BattleScenarioContextMessage
            {
                CampaignBattleType = CampaignBattleType,
                ScenarioKind = ScenarioKind,
                IsSiegeBattle = IsSiegeBattle,
                Source = Source,
                SiegeContext = SiegeContext?.Clone()
            };
        }
    }

    public sealed class BattleSiegeContextMessage
    {
        public string SiegeSubtype { get; set; }
        public string MissionShell { get; set; }
        public string SettlementId { get; set; }
        public string SettlementKind { get; set; }
        public string SettlementCultureId { get; set; }
        public string SceneLocationId { get; set; }
        public string CurrentSiegeState { get; set; }
        public int WallLevel { get; set; }
        public bool HasAnySiegeTower { get; set; }
        public bool HasMissionInitializerRecord { get; set; }
        public string MissionInitializerSource { get; set; }
        public string MissionInitializerSceneName { get; set; }
        public string MissionInitializerSceneLevels { get; set; }
        public int MissionInitializerSceneUpgradeLevel { get; set; } = -1;
        public bool MissionInitializerPlayingInCampaignMode { get; set; }
        public bool MissionInitializerSceneHasMapPatch { get; set; }
        public int MissionInitializerDecalAtlasGroup { get; set; } = -1;
        public int MissionInitializerTerrainType { get; set; } = -1;
        public List<float> WallHitPointRatios { get; set; } = new List<float>();
        public List<BattleSiegeEngineSnapshotMessage> AttackerSiegeEngines { get; set; } = new List<BattleSiegeEngineSnapshotMessage>();
        public List<BattleSiegeEngineSnapshotMessage> DefenderSiegeEngines { get; set; } = new List<BattleSiegeEngineSnapshotMessage>();
        public List<string> AttackerSiegeEngineTypeIds { get; set; } = new List<string>();
        public List<string> DefenderSiegeEngineTypeIds { get; set; } = new List<string>();

        public BattleSiegeContextMessage Clone()
        {
            return new BattleSiegeContextMessage
            {
                SiegeSubtype = SiegeSubtype,
                MissionShell = MissionShell,
                SettlementId = SettlementId,
                SettlementKind = SettlementKind,
                SettlementCultureId = SettlementCultureId,
                SceneLocationId = SceneLocationId,
                CurrentSiegeState = CurrentSiegeState,
                WallLevel = WallLevel,
                HasAnySiegeTower = HasAnySiegeTower,
                HasMissionInitializerRecord = HasMissionInitializerRecord,
                MissionInitializerSource = MissionInitializerSource,
                MissionInitializerSceneName = MissionInitializerSceneName,
                MissionInitializerSceneLevels = MissionInitializerSceneLevels,
                MissionInitializerSceneUpgradeLevel = MissionInitializerSceneUpgradeLevel,
                MissionInitializerPlayingInCampaignMode = MissionInitializerPlayingInCampaignMode,
                MissionInitializerSceneHasMapPatch = MissionInitializerSceneHasMapPatch,
                MissionInitializerDecalAtlasGroup = MissionInitializerDecalAtlasGroup,
                MissionInitializerTerrainType = MissionInitializerTerrainType,
                WallHitPointRatios = WallHitPointRatios != null ? new List<float>(WallHitPointRatios) : new List<float>(),
                AttackerSiegeEngines = CloneSiegeEngineList(AttackerSiegeEngines),
                DefenderSiegeEngines = CloneSiegeEngineList(DefenderSiegeEngines),
                AttackerSiegeEngineTypeIds = AttackerSiegeEngineTypeIds != null ? new List<string>(AttackerSiegeEngineTypeIds) : new List<string>(),
                DefenderSiegeEngineTypeIds = DefenderSiegeEngineTypeIds != null ? new List<string>(DefenderSiegeEngineTypeIds) : new List<string>()
            };
        }

        private static List<BattleSiegeEngineSnapshotMessage> CloneSiegeEngineList(List<BattleSiegeEngineSnapshotMessage> siegeEngines)
        {
            var clone = new List<BattleSiegeEngineSnapshotMessage>();
            if (siegeEngines == null)
                return clone;

            for (int i = 0; i < siegeEngines.Count; i++)
            {
                BattleSiegeEngineSnapshotMessage siegeEngine = siegeEngines[i];
                if (siegeEngine != null)
                    clone.Add(siegeEngine.Clone());
            }

            return clone;
        }
    }

    public sealed class BattleSiegeEngineSnapshotMessage
    {
        public string EngineTypeId { get; set; }
        public int Index { get; set; } = -1;
        public float Health { get; set; }
        public float InitialHealth { get; set; }
        public float MaxHealth { get; set; }

        public BattleSiegeEngineSnapshotMessage Clone()
        {
            return new BattleSiegeEngineSnapshotMessage
            {
                EngineTypeId = EngineTypeId,
                Index = Index,
                Health = Health,
                InitialHealth = InitialHealth,
                MaxHealth = MaxHealth
            };
        }
    }

    public sealed class BattleSideSnapshotMessage
    {
        public string SideId { get; set; }
        public string SideText { get; set; }
        public string LeaderPartyId { get; set; }
        public string CultureId { get; set; }
        public uint Color { get; set; }
        public uint Color2 { get; set; }
        public string BannerCode { get; set; }
        public string AppearanceSource { get; set; }
        public float SideMorale { get; set; }
        public bool IsPlayerSide { get; set; }
        public int TotalManCount { get; set; }
        public List<string> MissionReadyEntryOrder { get; set; } = new List<string>();
        public List<BattlePartySnapshotMessage> Parties { get; set; } = new List<BattlePartySnapshotMessage>();
        public List<TroopStackInfo> Troops { get; set; } = new List<TroopStackInfo>();
    }

    public sealed class BattlePartySnapshotMessage
    {
        public string PartyId { get; set; }
        public string PartyName { get; set; }
        public bool IsMainParty { get; set; }
        public bool HasMobileParty { get; set; }
        public int TotalManCount { get; set; }
        public BattlePartyModifierSnapshotMessage Modifiers { get; set; } = new BattlePartyModifierSnapshotMessage();
        public List<TroopStackInfo> Troops { get; set; } = new List<TroopStackInfo>();
    }

    public sealed class BattlePartyModifierSnapshotMessage
    {
        public string LeaderHeroId { get; set; }
        public string OwnerHeroId { get; set; }
        public string ScoutHeroId { get; set; }
        public string QuartermasterHeroId { get; set; }
        public string EngineerHeroId { get; set; }
        public string SurgeonHeroId { get; set; }
        public float Morale { get; set; }
        public float RecentEventsMorale { get; set; }
        public float MoraleChange { get; set; }
        public int ContributionToBattle { get; set; }
        public int LeaderLeadershipSkill { get; set; }
        public int LeaderTacticsSkill { get; set; }
        public int ScoutScoutingSkill { get; set; }
        public int QuartermasterStewardSkill { get; set; }
        public int EngineerEngineeringSkill { get; set; }
        public int SurgeonMedicineSkill { get; set; }
        public int SurvivalMedicineSkill { get; set; }
        public List<string> PartyLeaderPerkIds { get; set; } = new List<string>();
        public List<string> ArmyCommanderPerkIds { get; set; } = new List<string>();
        public List<string> CaptainPerkIds { get; set; } = new List<string>();
        public List<string> ScoutPerkIds { get; set; } = new List<string>();
        public List<string> QuartermasterPerkIds { get; set; } = new List<string>();
        public List<string> EngineerPerkIds { get; set; } = new List<string>();
        public List<string> SurgeonPerkIds { get; set; } = new List<string>();
    }

    public sealed class TroopStackInfo
    {
        public string EntryId { get; set; }
        public string SideId { get; set; }
        public string PartyId { get; set; }
        public string CharacterId { get; set; }
        public string OriginalCharacterId { get; set; }
        public string CampaignFormationClass { get; set; }
        public string SpawnTemplateId { get; set; }
        public string TroopName { get; set; }
        public string CultureId { get; set; }
        public string HeroId { get; set; }
        public string HeroRole { get; set; }
        public string HeroOccupationId { get; set; }
        public string HeroClanId { get; set; }
        public string HeroTemplateId { get; set; }
        public string HeroBodyProperties { get; set; }
        public int HeroLevel { get; set; }
        public float HeroAge { get; set; }
        public bool HeroIsFemale { get; set; }
        public int CharacterLevel { get; set; }
        public float HeroTotalArmorSum { get; set; }
        public bool IsPlayerCharacter { get; set; }
        public bool IsPlayerClanHero { get; set; }
        public bool HeroCanDieInBattle { get; set; } = true;
        public bool ForceUnconscious { get; set; }
        public int Tier { get; set; }
        public bool IsMounted { get; set; }
        public bool IsRanged { get; set; }
        public bool HasShield { get; set; }
        public bool HasThrown { get; set; }
        public int AttributeVigor { get; set; }
        public int AttributeControl { get; set; }
        public int AttributeEndurance { get; set; }
        public int SkillOneHanded { get; set; }
        public int SkillTwoHanded { get; set; }
        public int SkillPolearm { get; set; }
        public int SkillBow { get; set; }
        public int SkillCrossbow { get; set; }
        public int SkillThrowing { get; set; }
        public int SkillRiding { get; set; }
        public int SkillAthletics { get; set; }
        public int BaseHitPoints { get; set; }
        public List<string> PerkIds { get; set; } = new List<string>();
        public string CombatItem0Id { get; set; }
        public int? CombatItem0Amount { get; set; }
        public string CombatItem0CraftedWeaponKey { get; set; }
        public string CombatItem0ModifierId { get; set; }
        public string CombatItem1Id { get; set; }
        public int? CombatItem1Amount { get; set; }
        public string CombatItem1CraftedWeaponKey { get; set; }
        public string CombatItem1ModifierId { get; set; }
        public string CombatItem2Id { get; set; }
        public int? CombatItem2Amount { get; set; }
        public string CombatItem2CraftedWeaponKey { get; set; }
        public string CombatItem2ModifierId { get; set; }
        public string CombatItem3Id { get; set; }
        public int? CombatItem3Amount { get; set; }
        public string CombatItem3CraftedWeaponKey { get; set; }
        public string CombatItem3ModifierId { get; set; }
        public string CombatHeadId { get; set; }
        public string CombatBodyId { get; set; }
        public string CombatLegId { get; set; }
        public string CombatGlovesId { get; set; }
        public string CombatCapeId { get; set; }
        public string CombatHorseId { get; set; }
        public string CombatHorseHarnessId { get; set; }
        public bool ServerCreateContractResolved { get; set; }
        public bool ServerCreateUseStringIdExactEquipmentPath { get; set; }
        public bool ServerCreateInjectEquipment { get; set; }
        public bool ServerCreatePreSpawnIncludesWeapons { get; set; }
        public bool ServerCreatePreSpawnIncludesArmorVisuals { get; set; }
        public bool ServerCreatePreSpawnIncludesCapeVisual { get; set; }
        public bool ServerCreatePreSpawnIncludesMountVisuals { get; set; }
        public bool ServerCreatePayloadDiagnosticActive { get; set; }
        public string ServerCreateRequestedProfile { get; set; }
        public string ServerCreateEffectiveProfile { get; set; }
        public bool IsHero { get; set; }
        public int Count { get; set; }
        public int WoundedCount { get; set; }
    }
}
