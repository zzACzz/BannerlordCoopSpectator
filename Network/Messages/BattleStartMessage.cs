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
        public List<BattlePartySnapshotMessage> Parties { get; set; } = new List<BattlePartySnapshotMessage>();
        public List<TroopStackInfo> Troops { get; set; } = new List<TroopStackInfo>();
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
        public string CombatItem1Id { get; set; }
        public string CombatItem2Id { get; set; }
        public string CombatItem3Id { get; set; }
        public string CombatHeadId { get; set; }
        public string CombatBodyId { get; set; }
        public string CombatLegId { get; set; }
        public string CombatGlovesId { get; set; }
        public string CombatCapeId { get; set; }
        public string CombatHorseId { get; set; }
        public string CombatHorseHarnessId { get; set; }
        public bool IsHero { get; set; }
        public int Count { get; set; }
        public int WoundedCount { get; set; }
    }
}
