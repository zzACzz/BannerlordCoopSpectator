using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    public sealed class ExactTransferSpawnContract
    {
        public ExactTransferSpawnContract()
        {
            Identity = new ExactTransferIdentityContract();
            Body = new ExactTransferBodyContract();
            Equipment = new ExactTransferEquipmentContract();
            Mount = new ExactTransferMountContract();
            PeerBinding = new ExactTransferPeerBindingContract();
            InitialWield = new ExactTransferInitialWieldContract();
            Control = new ExactTransferControlContract();
            Cleanup = new ExactTransferCleanupContract();
            SpawnPolicy = new ExactTransferSpawnPolicyContract();
        }

        public string EntryId { get; set; }
        public ExactTransferIdentityContract Identity { get; set; }
        public ExactTransferBodyContract Body { get; set; }
        public ExactTransferEquipmentContract Equipment { get; set; }
        public ExactTransferMountContract Mount { get; set; }
        public ExactTransferPeerBindingContract PeerBinding { get; set; }
        public ExactTransferInitialWieldContract InitialWield { get; set; }
        public ExactTransferControlContract Control { get; set; }
        public ExactTransferCleanupContract Cleanup { get; set; }
        public ExactTransferSpawnPolicyContract SpawnPolicy { get; set; }
    }

    public sealed class ExactTransferIdentityContract
    {
        public string CampaignCharacterId { get; set; }
        public string CampaignHeroStringId { get; set; }
        public string NativeMultiplayerCharacterId { get; set; }
        public bool IsHero { get; set; }
        public bool IsMainHero { get; set; }
        public bool IsLord { get; set; }
        public bool IsCompanion { get; set; }
        public bool IsPlayerControlledEntry { get; set; }
        public bool IsMountedExpected { get; set; }
    }

    public sealed class ExactTransferBodyContract
    {
        public bool HasExactBodyProperties { get; set; }
        public BodyProperties BodyProperties { get; set; }
        public string BodyPropertiesSource { get; set; }
        public int BodyPropertiesSeed { get; set; }
        public bool IsFemale { get; set; }
        public int? Age { get; set; }
        public string MonsterId { get; set; }
    }

    public sealed class ExactTransferEquipmentContract
    {
        public ExactTransferEquipmentContract()
        {
            SpawnEquipment = new Equipment();
            MissionEquipment = new MissionEquipment();
            Slots = new List<ExactTransferEquipmentSlotContract>();
        }

        public Equipment SpawnEquipment { get; set; }
        public MissionEquipment MissionEquipment { get; set; }
        public List<ExactTransferEquipmentSlotContract> Slots { get; private set; }
        public uint ClothingColor1 { get; set; }
        public uint ClothingColor2 { get; set; }
        public bool IncludeWeaponsInPreSpawn { get; set; }
        public bool IncludeArmorVisualsInPreSpawn { get; set; }
        public bool IncludeCapeInPreSpawn { get; set; }
        public bool IncludeMountVisualsInPreSpawn { get; set; }
        public bool MountedWeaponLayoutNormalized { get; set; }
        public string MountedWeaponLayoutSummary { get; set; }
    }

    public sealed class ExactTransferEquipmentSlotContract
    {
        public EquipmentIndex Slot { get; set; }
        public string SlotLabel { get; set; }
        public string ItemId { get; set; }
        public bool IsEmpty { get; set; }
        public bool MustExistAtCreateAgentTime { get; set; }
        public bool CanBeLateSynchronized { get; set; }
        public bool IsMountedCritical { get; set; }
    }

    public sealed class ExactTransferMountContract
    {
        public bool IsMounted { get; set; }
        public string HorseItemId { get; set; }
        public string HarnessItemId { get; set; }
        public int? ExpectedMountAgentIndex { get; set; }
        public bool RequiresVerifiedMountLink { get; set; }
    }

    public sealed class ExactTransferPeerBindingContract
    {
        public string PeerUserName { get; set; }
        public int? PeerIndex { get; set; }
        public bool IsRemotePeer { get; set; }
        public bool IsLocalPeer { get; set; }
        public bool RequiresSetAgentPeer { get; set; }
        public bool RequiresReplaceBotWithPlayer { get; set; }
        public bool AllowPeerDrivenBodyAtCreateAgentTime { get; set; }
        public bool AllowPeerDrivenBannerAtCreateAgentTime { get; set; }
        public bool UsePlayerAgentCreateBranch { get; set; }
    }

    public sealed class ExactTransferInitialWieldContract
    {
        public int? PreferredMainHandSlotIndex { get; set; }
        public int? PreferredOffHandSlotIndex { get; set; }
        public bool RequireImmediateWieldOnSpawn { get; set; }
        public bool AllowDeferredWieldAfterEquipmentSync { get; set; }
        public bool HasWeapon2Risk { get; set; }
    }

    public sealed class ExactTransferControlContract
    {
        public int TeamIndex { get; set; }
        public int FormationIndex { get; set; }
        public bool IsCommanderEntry { get; set; }
        public bool CanReceivePlayerOrders { get; set; }
        public bool EnableCommanderControlOnlyAfterExactReady { get; set; }
    }

    public sealed class ExactTransferCleanupContract
    {
        public bool ClearTransferStateOnAgentRemoved { get; set; }
        public bool ClearTransferStateOnMountRemoved { get; set; }
        public bool ClearTransferStateOnDeath { get; set; }
        public bool RejectAgentIndexReuseWithoutIdentityMatch { get; set; }
    }

    public sealed class ExactTransferSpawnPolicyContract
    {
        public bool UseStrictExactHeroPath { get; set; }
        public bool RequirePreSpawnInjection { get; set; }
        public bool AllowClientVisualOverlayAsRecoveryOnly { get; set; }
        public bool ForbidSurrogatePrimaryMaterialization { get; set; }
    }
}
