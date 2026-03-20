using System.Collections.Generic;

namespace CoopSpectator.Network.Messages
{
    public sealed class BattleStartMessage
    {
        public string MapScene { get; set; }
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
        public string PlayerSide { get; set; }
        public List<BattleSideSnapshotMessage> Sides { get; set; } = new List<BattleSideSnapshotMessage>();
    }

    public sealed class BattleSideSnapshotMessage
    {
        public string SideId { get; set; }
        public string SideText { get; set; }
        public bool IsPlayerSide { get; set; }
        public int TotalManCount { get; set; }
        public List<BattlePartySnapshotMessage> Parties { get; set; } = new List<BattlePartySnapshotMessage>();
        public List<TroopStackInfo> Troops { get; set; } = new List<TroopStackInfo>();
    }

    public sealed class BattlePartySnapshotMessage
    {
        public string PartyId { get; set; }
        public string PartyName { get; set; }
        public bool IsMainParty { get; set; }
        public int TotalManCount { get; set; }
        public List<TroopStackInfo> Troops { get; set; } = new List<TroopStackInfo>();
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
        public int HeroLevel { get; set; }
        public float HeroAge { get; set; }
        public bool HeroIsFemale { get; set; }
        public int Tier { get; set; }
        public bool IsMounted { get; set; }
        public bool IsRanged { get; set; }
        public bool HasShield { get; set; }
        public bool HasThrown { get; set; }
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
