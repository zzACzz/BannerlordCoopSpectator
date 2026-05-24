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
        public int ArmySize { get; set; }
        public List<TroopStackInfo> Troops { get; set; }
        public BattleSnapshotMessage Snapshot { get; set; }
        public CanonicalBattleContract CanonicalBattle { get; set; }
    }

    public sealed class BattleSnapshotMessage
    {
        public string BattleId { get; set; }
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
        public CanonicalBattleContract CanonicalBattle { get; set; }
        public List<BattleSideSnapshotMessage> Sides { get; set; } = new List<BattleSideSnapshotMessage>();
    }

    public sealed class BattleSideSnapshotMessage
    {
        public string SideId { get; set; }
        public string SideText { get; set; }
        public string LeaderPartyId { get; set; }
        public float SideMorale { get; set; }
        public bool IsPlayerSide { get; set; }
        public int TotalManCount { get; set; }
        public List<string> MissionReadyEntryOrder { get; set; } = new List<string>();
        public List<MissionReadyDescriptorMessage> MissionReadyDescriptors { get; set; } = new List<MissionReadyDescriptorMessage>();
        public List<BattlePartySnapshotMessage> Parties { get; set; } = new List<BattlePartySnapshotMessage>();
        public List<TroopStackInfo> Troops { get; set; } = new List<TroopStackInfo>();
    }

    public sealed class MissionReadyDescriptorMessage
    {
        public int OrderIndex { get; set; } = -1;
        public string EntryId { get; set; }
        public string SideId { get; set; }
        public string PartyId { get; set; }
        public string TroopId { get; set; }
        public int DescriptorSeed { get; set; }
        public string DescriptorDebugText { get; set; }
    }

    public sealed class BattlePartySnapshotMessage
    {
        public string PartyId { get; set; }
        public string PartyName { get; set; }
        public bool IsMainParty { get; set; }
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
        public string CombatItem1Id { get; set; }
        public int? CombatItem1Amount { get; set; }
        public string CombatItem2Id { get; set; }
        public int? CombatItem2Amount { get; set; }
        public string CombatItem3Id { get; set; }
        public int? CombatItem3Amount { get; set; }
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

    public sealed class CanonicalBattleContract
    {
        public string SchemaVersion { get; set; } = "field_battle_v1_draft";
        public CanonicalBattleContext Context { get; set; } = new CanonicalBattleContext();
        public List<CanonicalBattleSide> Sides { get; set; } = new List<CanonicalBattleSide>();
        public List<CanonicalTroopInstance> TroopInstances { get; set; } = new List<CanonicalTroopInstance>();
    }

    public sealed class CanonicalBattleContext
    {
        public string BattleId { get; set; }
        public string BattleType { get; set; }
        public string CampaignScene { get; set; }
        public string WorldMapScene { get; set; }
        public string MultiplayerScene { get; set; }
        public string MultiplayerGameType { get; set; }
        public string PlayerSide { get; set; }
        public int BattleSizeBudget { get; set; }
        public int ReinforcementWaveCount { get; set; }
        public float PlayerTroopsReceivedDamageMultiplier { get; set; } = 1f;
        public int MapPatchSceneIndex { get; set; } = -1;
        public float MapPatchNormalizedX { get; set; }
        public float MapPatchNormalizedY { get; set; }
        public bool HasPatchEncounterDirection { get; set; }
        public float PatchEncounterDirX { get; set; }
        public float PatchEncounterDirY { get; set; }
        public string PatchEncounterDirectionSource { get; set; }
    }

    public sealed class CanonicalBattleSide
    {
        public string SideId { get; set; }
        public string SideText { get; set; }
        public string LeaderPartyId { get; set; }
        public float SideMorale { get; set; }
        public bool IsPlayerSide { get; set; }
        public int TotalManCount { get; set; }
        public List<string> MissionReadyInstanceOrder { get; set; } = new List<string>();
        public List<CanonicalBattleParty> Parties { get; set; } = new List<CanonicalBattleParty>();
    }

    public sealed class CanonicalBattleParty
    {
        public string PartyId { get; set; }
        public string PartyName { get; set; }
        public bool IsMainParty { get; set; }
        public int TotalManCount { get; set; }
        public CanonicalBattlePartyModifiers Modifiers { get; set; } = new CanonicalBattlePartyModifiers();
    }

    public sealed class CanonicalBattlePartyModifiers
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
        public List<string> PartyLeaderPerkIds { get; set; } = new List<string>();
        public List<string> ArmyCommanderPerkIds { get; set; } = new List<string>();
        public List<string> CaptainPerkIds { get; set; } = new List<string>();
        public List<string> ScoutPerkIds { get; set; } = new List<string>();
        public List<string> QuartermasterPerkIds { get; set; } = new List<string>();
        public List<string> EngineerPerkIds { get; set; } = new List<string>();
        public List<string> SurgeonPerkIds { get; set; } = new List<string>();
    }

    public sealed class CanonicalTroopInstance
    {
        public string InstanceId { get; set; }
        public string InstanceIdSource { get; set; }
        public int? CampaignTroopDescriptorSeed { get; set; }
        public string CampaignTroopDescriptorDebugText { get; set; }
        public string SideId { get; set; }
        public string PartyId { get; set; }
        public string EntryId { get; set; }
        public string CharacterId { get; set; }
        public string OriginalCharacterId { get; set; }
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
        public bool IsHero { get; set; }
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
        public bool IsMissionParticipant { get; set; }
        public bool IsPreBattleWounded { get; set; }
        public int MissionOrderIndex { get; set; } = -1;
        public int StableOrdinalWithinEntry { get; set; }
        public List<string> PerkIds { get; set; } = new List<string>();
        public CanonicalEquipmentSpec Equipment { get; set; } = new CanonicalEquipmentSpec();
    }

    public sealed class CanonicalEquipmentSpec
    {
        public string CombatItem0Id { get; set; }
        public int? CombatItem0Amount { get; set; }
        public string CombatItem1Id { get; set; }
        public int? CombatItem1Amount { get; set; }
        public string CombatItem2Id { get; set; }
        public int? CombatItem2Amount { get; set; }
        public string CombatItem3Id { get; set; }
        public int? CombatItem3Amount { get; set; }
        public string CombatHeadId { get; set; }
        public string CombatBodyId { get; set; }
        public string CombatLegId { get; set; }
        public string CombatGlovesId { get; set; }
        public string CombatCapeId { get; set; }
        public string CombatHorseId { get; set; }
        public string CombatHorseHarnessId { get; set; }
    }

    public sealed class CanonicalBattleResultContract
    {
        public string SchemaVersion { get; set; } = "field_battle_result_v1_draft";
        public string BattleId { get; set; }
        public string WinnerSideId { get; set; }
        public bool EnemyRetreated { get; set; }
        public List<CanonicalTroopOutcome> TroopOutcomes { get; set; } = new List<CanonicalTroopOutcome>();
        public List<CanonicalHeroOutcome> HeroOutcomes { get; set; } = new List<CanonicalHeroOutcome>();
    }

    public sealed class CanonicalTroopOutcome
    {
        public string InstanceId { get; set; }
        public string Outcome { get; set; }
        public bool HorseLost { get; set; }
        public int RemainingHitPoints { get; set; }
    }

    public sealed class CanonicalHeroOutcome
    {
        public string HeroId { get; set; }
        public string Outcome { get; set; }
        public int RemainingHitPoints { get; set; }
        public bool Captured { get; set; }
    }
}
