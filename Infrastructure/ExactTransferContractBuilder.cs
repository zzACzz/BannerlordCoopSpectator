using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using CoopSpectator.MissionBehaviors;
using CoopSpectator.Network.Messages;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Infrastructure
{
    internal static class ExactTransferContractBuilder
    {
        internal enum BuildMode
        {
            Runtime = 0,
            Diagnostic = 1
        }

        private enum MountedWeaponRole
        {
            Other = 0,
            Melee = 1,
            Polearm = 2,
            Shield = 3,
            Ranged = 4,
            Ammo = 5
        }

        private enum StrictHeroWeaponProfile
        {
            Unknown = 0,
            MountedBow = 1,
            MountedCrossbow = 2,
            MountedThrownWithShield = 3,
            MountedThrownWithoutShield = 4,
            MountedMeleeWithShield = 5,
            MountedMeleeWithoutShield = 6,
            FootBow = 7,
            FootCrossbow = 8,
            FootThrownWithShield = 9,
            FootThrownWithoutShield = 10,
            FootMeleeWithShield = 11,
            FootMeleeWithoutShield = 12
        }

        private sealed class MountedWeaponSlotState
        {
            public EquipmentIndex Slot { get; set; }
            public string SlotLabel { get; set; }
            public string ItemId { get; set; }
            public ItemObject Item { get; set; }
            public MountedWeaponRole Role { get; set; }
        }

        private sealed class StrictHeroWeaponProfileAnalysis
        {
            public StrictHeroWeaponProfile PrimaryProfile { get; set; }
            public bool IsMounted { get; set; }
            public bool HasBowSet { get; set; }
            public bool HasCrossbowSet { get; set; }
            public bool HasShield { get; set; }
            public bool HasThrown { get; set; }
            public bool HasMelee { get; set; }
            public bool HasShieldCompatibleMelee { get; set; }
        }

        public static ExactTransferSpawnContract Build(
            RosterEntryState entryState,
            bool isPlayerControlledOrigin,
            int teamIndex,
            int formationIndex,
            BuildMode buildMode = BuildMode.Runtime)
        {
            if (entryState == null)
                return null;

            ExactTransferSpawnContract contract = new ExactTransferSpawnContract
            {
                EntryId = entryState.EntryId
            };

            bool isStrictHeroEntry = IsStrictHeroEntry(entryState);
            bool isRuntimeExactSupported =
                buildMode == BuildMode.Diagnostic ||
                CoopMissionSpawnLogic.IsCurrentRuntimeExactEntryContractSupported(entryState);
            bool forcePostCreateWeaponOverlay =
                buildMode == BuildMode.Runtime &&
                CoopMissionSpawnLogic.RequiresPostCreateStringIdExactWeaponOverlayForCurrentRuntime(entryState) &&
                !ShouldBypassLegacyPostCreateWeaponOverlayForCanonicalFieldBattle(entryState);
            PopulateIdentity(contract.Identity, entryState, isPlayerControlledOrigin);
            PopulateBody(contract.Body, entryState);
            PopulateEquipment(
                contract.Equipment,
                entryState,
                isStrictHeroEntry,
                isRuntimeExactSupported,
                forcePostCreateWeaponOverlay,
                buildMode);
            PopulateMount(contract.Mount, entryState);
            PopulatePeerBinding(contract.PeerBinding, entryState, isPlayerControlledOrigin);
            PopulateInitialWield(contract.InitialWield, entryState, contract.Equipment);
            PopulatePreBattleWeaponState(
                contract.PreBattleWeaponState,
                contract.InitialWield,
                entryState,
                contract.Equipment,
                isPlayerControlledOrigin);
            PopulateControl(contract.Control, entryState, teamIndex, formationIndex);
            PopulateCleanup(contract.Cleanup);
            PopulateSpawnPolicy(contract.SpawnPolicy, isStrictHeroEntry, isRuntimeExactSupported);

            return contract;
        }

        private static void PopulateIdentity(
            ExactTransferIdentityContract identity,
            RosterEntryState entryState,
            bool isPlayerControlledOrigin)
        {
            string surrogateShellCharacterId =
                BattleSnapshotRuntimeState.TryResolveSurrogateShellCharacterId(entryState.EntryId) ??
                entryState.SpawnTemplateId ??
                entryState.CharacterId;

            identity.CampaignCharacterId = entryState.CharacterId;
            identity.CampaignHeroStringId = entryState.HeroId;
            identity.MaterializationEntryIdToken = entryState.EntryId;
            identity.SurrogateShellCharacterId = surrogateShellCharacterId;
            identity.NativeMultiplayerCharacterId = surrogateShellCharacterId;
            identity.IsHero = entryState.IsHero || !string.IsNullOrWhiteSpace(entryState.HeroId);
            identity.IsMainHero = string.Equals(entryState.OriginalCharacterId, "main_hero", StringComparison.OrdinalIgnoreCase);
            identity.IsLord = !string.IsNullOrWhiteSpace(entryState.HeroOccupationId) &&
                              !string.Equals(entryState.HeroOccupationId, "wanderer", StringComparison.OrdinalIgnoreCase);
            identity.IsCompanion = string.Equals(entryState.HeroOccupationId, "wanderer", StringComparison.OrdinalIgnoreCase);
            identity.IsPlayerControlledEntry = isPlayerControlledOrigin;
            identity.IsMountedExpected = entryState.IsMounted;
        }

        private static void PopulateBody(ExactTransferBodyContract body, RosterEntryState entryState)
        {
            body.BodyPropertiesSource = entryState.HeroBodyProperties;
            body.BodyPropertiesSeed = 0;
            body.IsFemale = entryState.HeroIsFemale;
            body.MonsterId = null;
            if (entryState.HeroAge > 0.01f)
                body.Age = Math.Max(1, Math.Min(120, (int)Math.Round(entryState.HeroAge)));

            if (string.IsNullOrWhiteSpace(entryState.HeroBodyProperties))
                return;

            try
            {
                if (BodyProperties.FromString(entryState.HeroBodyProperties, out BodyProperties bodyProperties))
                {
                    body.HasExactBodyProperties = true;
                    body.BodyProperties = bodyProperties;
                }
            }
            catch
            {
                body.HasExactBodyProperties = false;
            }
        }

        private static void PopulateEquipment(
            ExactTransferEquipmentContract equipment,
            RosterEntryState entryState,
            bool isStrictHeroEntry,
            bool isRuntimeExactSupported,
            bool forcePostCreateWeaponOverlay,
            BuildMode buildMode)
        {
            equipment.SpawnEquipment = CoopMissionSpawnLogic.BuildSnapshotEquipmentForExactRuntime(
                entryState,
                includeWeapons: true,
                honorExactVisualContracts: false,
                includeArmorVisuals: true,
                includeMountVisuals: true);
            bool exactPreSpawnWeaponCandidate = isStrictHeroEntry || isRuntimeExactSupported;
            bool allowRuntimeCreateAgentInjection = buildMode == BuildMode.Diagnostic || !forcePostCreateWeaponOverlay;
            bool strictHeroRuntimeCreateTimeExactAllowed =
                buildMode == BuildMode.Runtime &&
                isStrictHeroEntry &&
                allowRuntimeCreateAgentInjection;
            equipment.IncludeWeaponsInPreSpawn = buildMode == BuildMode.Diagnostic
                ? exactPreSpawnWeaponCandidate && HasAnyWeaponItem(entryState)
                : strictHeroRuntimeCreateTimeExactAllowed
                    ? HasAnyWeaponItem(entryState)
                    : allowRuntimeCreateAgentInjection && isRuntimeExactSupported && HasAnyWeaponItem(entryState);
            equipment.IncludeArmorVisualsInPreSpawn =
                buildMode == BuildMode.Diagnostic ||
                strictHeroRuntimeCreateTimeExactAllowed ||
                (allowRuntimeCreateAgentInjection && isRuntimeExactSupported);
            equipment.IncludeCapeInPreSpawn = buildMode == BuildMode.Diagnostic
                ? exactPreSpawnWeaponCandidate
                : strictHeroRuntimeCreateTimeExactAllowed
                    ? CoopMissionSpawnLogic.EvaluateExactRuntimeCapeVisualContract(entryState, out _, out _)
                    : allowRuntimeCreateAgentInjection &&
                      CoopMissionSpawnLogic.EvaluateExactRuntimeCapeVisualContract(entryState, out _, out _);
            equipment.IncludeMountVisualsInPreSpawn =
                entryState.IsMounted &&
                (buildMode == BuildMode.Diagnostic ||
                 strictHeroRuntimeCreateTimeExactAllowed ||
                 (allowRuntimeCreateAgentInjection && isRuntimeExactSupported));

            AddSlot(equipment, EquipmentIndex.Weapon0, "Item0", entryState.CombatItem0Id, mustExistAtCreateAgentTime: !string.IsNullOrWhiteSpace(entryState.CombatItem0Id));
            AddSlot(equipment, EquipmentIndex.Weapon1, "Item1", entryState.CombatItem1Id, mustExistAtCreateAgentTime: !string.IsNullOrWhiteSpace(entryState.CombatItem1Id));
            AddSlot(equipment, EquipmentIndex.Weapon2, "Item2", entryState.CombatItem2Id, mustExistAtCreateAgentTime: !string.IsNullOrWhiteSpace(entryState.CombatItem2Id));
            AddSlot(equipment, EquipmentIndex.Weapon3, "Item3", entryState.CombatItem3Id, mustExistAtCreateAgentTime: !string.IsNullOrWhiteSpace(entryState.CombatItem3Id));
            AddSlot(equipment, EquipmentIndex.Head, "Head", entryState.CombatHeadId, mustExistAtCreateAgentTime: !string.IsNullOrWhiteSpace(entryState.CombatHeadId));
            AddSlot(equipment, EquipmentIndex.Body, "Body", entryState.CombatBodyId, mustExistAtCreateAgentTime: !string.IsNullOrWhiteSpace(entryState.CombatBodyId));
            AddSlot(equipment, EquipmentIndex.Leg, "Leg", entryState.CombatLegId, mustExistAtCreateAgentTime: !string.IsNullOrWhiteSpace(entryState.CombatLegId));
            AddSlot(equipment, EquipmentIndex.Gloves, "Gloves", entryState.CombatGlovesId, mustExistAtCreateAgentTime: !string.IsNullOrWhiteSpace(entryState.CombatGlovesId));
            AddSlot(equipment, EquipmentIndex.Cape, "Cape", entryState.CombatCapeId, mustExistAtCreateAgentTime: false, canBeLateSynchronized: true);
            AddSlot(equipment, EquipmentIndex.Horse, "Horse", entryState.CombatHorseId, mustExistAtCreateAgentTime: entryState.IsMounted, canBeLateSynchronized: false, isMountedCritical: entryState.IsMounted);
            AddSlot(equipment, EquipmentIndex.HorseHarness, "HorseHarness", entryState.CombatHorseHarnessId, mustExistAtCreateAgentTime: entryState.IsMounted && !string.IsNullOrWhiteSpace(entryState.CombatHorseHarnessId), canBeLateSynchronized: false, isMountedCritical: entryState.IsMounted);

            NormalizeStrictHeroWeaponLayout(equipment, entryState, isStrictHeroEntry || isRuntimeExactSupported);
        }

        private static void PopulateMount(ExactTransferMountContract mount, RosterEntryState entryState)
        {
            mount.IsMounted = entryState.IsMounted;
            mount.HorseItemId = entryState.CombatHorseId;
            mount.HarnessItemId = entryState.CombatHorseHarnessId;
            mount.ExpectedMountAgentIndex = null;
            mount.RequiresVerifiedMountLink = entryState.IsMounted;
        }

        private static void PopulatePeerBinding(
            ExactTransferPeerBindingContract peerBinding,
            RosterEntryState entryState,
            bool isPlayerControlledOrigin)
        {
            peerBinding.IsLocalPeer = isPlayerControlledOrigin;
            peerBinding.IsRemotePeer = !isPlayerControlledOrigin && (entryState.IsHero || !string.IsNullOrWhiteSpace(entryState.HeroId));
            peerBinding.RequiresSetAgentPeer = isPlayerControlledOrigin || peerBinding.IsRemotePeer;
            peerBinding.RequiresReplaceBotWithPlayer = isPlayerControlledOrigin;
            peerBinding.AllowPeerDrivenBodyAtCreateAgentTime = false;
            peerBinding.AllowPeerDrivenBannerAtCreateAgentTime = false;
            peerBinding.UsePlayerAgentCreateBranch = false;
        }

        private static void PopulateInitialWield(
            ExactTransferInitialWieldContract initialWield,
            RosterEntryState entryState,
            ExactTransferEquipmentContract equipment)
        {
            initialWield.AllowDeferredWieldAfterEquipmentSync = true;
            initialWield.RequireImmediateWieldOnSpawn = !entryState.IsMounted;
            initialWield.HasWeapon2Risk = false;

            if (equipment?.SpawnEquipment == null)
                return;

            try
            {
                equipment.SpawnEquipment.GetInitialWeaponIndicesToEquip(
                    out EquipmentIndex mainHandWeaponIndex,
                    out EquipmentIndex offHandWeaponIndex,
                    out bool _,
                    Equipment.InitialWeaponEquipPreference.Any);
                initialWield.PreferredMainHandSlotIndex = (int)mainHandWeaponIndex;
                initialWield.PreferredOffHandSlotIndex = (int)offHandWeaponIndex;
                initialWield.HasWeapon2Risk =
                    DoesEquipmentContainUnsafeRangedWeapon2Layout(equipment.SpawnEquipment) ||
                    (entryState.IsMounted && mainHandWeaponIndex == EquipmentIndex.Weapon2);
            }
            catch
            {
                initialWield.HasWeapon2Risk =
                    DoesEquipmentContainUnsafeRangedWeapon2Layout(equipment.SpawnEquipment) ||
                    (entryState.IsMounted && !string.IsNullOrWhiteSpace(entryState.CombatItem2Id));
            }
        }

        private static void PopulatePreBattleWeaponState(
            ExactTransferPreBattleWeaponStateContract preBattleWeaponState,
            ExactTransferInitialWieldContract initialWield,
            RosterEntryState entryState,
            ExactTransferEquipmentContract equipment,
            bool isPlayerControlledOrigin)
        {
            if (preBattleWeaponState == null)
                return;

            preBattleWeaponState.Mode = ExactTransferPreBattleWeaponStateMode.None;
            preBattleWeaponState.PreferredMainHandSlotIndex = null;
            preBattleWeaponState.PreferredOffHandSlotIndex = null;
            preBattleWeaponState.ExpectedAmmoSlotIndex = null;
            preBattleWeaponState.ExpectAmmoAttachedToMainHand = false;
            preBattleWeaponState.InitialWeaponEquipPreference = Equipment.InitialWeaponEquipPreference.Any;
            preBattleWeaponState.DecisionReason = "prebattle-weapon-state-unresolved";

            if (entryState == null || equipment?.SpawnEquipment == null)
                return;

            List<MountedWeaponSlotState> slots = ResolveMountedWeaponSlots(equipment.SpawnEquipment);
            if (slots.Count == 0)
                return;

            MountedWeaponSlotState firstShield = slots.FirstOrDefault(slot => slot?.Role == MountedWeaponRole.Shield);
            MountedWeaponSlotState firstPrimaryMelee = slots.FirstOrDefault(IsShieldCompatibleMeleeSlot) ??
                                                       slots.FirstOrDefault(IsMeleeWeaponSlot);
            MountedWeaponSlotState firstBow = slots.FirstOrDefault(IsBowWeaponSlot);
            MountedWeaponSlotState firstCrossbow = slots.FirstOrDefault(IsCrossbowWeaponSlot);
            MountedWeaponSlotState firstThrown = slots.FirstOrDefault(IsThrownWeaponSlot);
            MountedWeaponSlotState firstArrowAmmo = slots.FirstOrDefault(IsArrowAmmoSlot);
            MountedWeaponSlotState firstBoltAmmo = slots.FirstOrDefault(IsBoltAmmoSlot);
            MountedWeaponSlotState firstSlingAmmo = slots.FirstOrDefault(IsSlingAmmoSlot);
            bool isMainHeroPlayerEntry =
                isPlayerControlledOrigin &&
                string.Equals(entryState.OriginalCharacterId, "main_hero", StringComparison.OrdinalIgnoreCase);

            if (isMainHeroPlayerEntry)
            {
                preBattleWeaponState.Mode = ExactTransferPreBattleWeaponStateMode.PlayerControlledOverride;
                preBattleWeaponState.PreferredMainHandSlotIndex = initialWield?.PreferredMainHandSlotIndex;
                preBattleWeaponState.PreferredOffHandSlotIndex = initialWield?.PreferredOffHandSlotIndex;
                preBattleWeaponState.InitialWeaponEquipPreference =
                    ResolveInitialWeaponEquipPreferenceFromPreferredSlot(
                        equipment.SpawnEquipment,
                        initialWield?.PreferredMainHandSlotIndex);
                preBattleWeaponState.DecisionReason = "player-controlled-main-hero-native-initial-wield-override";
                return;
            }

            if (firstCrossbow != null && firstBoltAmmo != null)
            {
                preBattleWeaponState.Mode = ExactTransferPreBattleWeaponStateMode.CrossbowLoaded;
                preBattleWeaponState.PreferredMainHandSlotIndex = (int)firstCrossbow.Slot;
                preBattleWeaponState.ExpectedAmmoSlotIndex = (int)firstBoltAmmo.Slot;
                preBattleWeaponState.ExpectAmmoAttachedToMainHand = true;
                preBattleWeaponState.InitialWeaponEquipPreference = Equipment.InitialWeaponEquipPreference.RangedForMainHand;
                preBattleWeaponState.DecisionReason = "ai-crossbow-loaded-prebattle-state";
                return;
            }

            if (firstBow != null && firstArrowAmmo != null)
            {
                preBattleWeaponState.Mode = ExactTransferPreBattleWeaponStateMode.BowArmed;
                preBattleWeaponState.PreferredMainHandSlotIndex = (int)firstBow.Slot;
                preBattleWeaponState.ExpectedAmmoSlotIndex = (int)firstArrowAmmo.Slot;
                preBattleWeaponState.ExpectAmmoAttachedToMainHand = true;
                preBattleWeaponState.InitialWeaponEquipPreference = Equipment.InitialWeaponEquipPreference.RangedForMainHand;
                preBattleWeaponState.DecisionReason = "ai-bow-armed-prebattle-state";
                return;
            }

            if (firstThrown != null && firstSlingAmmo != null)
            {
                preBattleWeaponState.Mode = ExactTransferPreBattleWeaponStateMode.SlingReady;
                preBattleWeaponState.PreferredMainHandSlotIndex = (int)firstThrown.Slot;
                preBattleWeaponState.ExpectedAmmoSlotIndex = (int)firstSlingAmmo.Slot;
                preBattleWeaponState.ExpectAmmoAttachedToMainHand = false;
                preBattleWeaponState.InitialWeaponEquipPreference = Equipment.InitialWeaponEquipPreference.RangedForMainHand;
                preBattleWeaponState.DecisionReason = "ai-sling-stone-ready-prebattle-state";
                return;
            }

            if (firstThrown != null)
            {
                preBattleWeaponState.Mode = ExactTransferPreBattleWeaponStateMode.ThrownReady;
                preBattleWeaponState.PreferredMainHandSlotIndex = (int)firstThrown.Slot;
                preBattleWeaponState.PreferredOffHandSlotIndex =
                    CanPairShieldWithWeapon(firstThrown)
                        ? (int?)firstShield?.Slot
                        : null;
                preBattleWeaponState.InitialWeaponEquipPreference = Equipment.InitialWeaponEquipPreference.RangedForMainHand;
                preBattleWeaponState.DecisionReason = firstShield != null
                    ? "ai-thrown-shield-ready-prebattle-state"
                    : "ai-thrown-ready-prebattle-state";
                return;
            }

            if (firstPrimaryMelee != null)
            {
                preBattleWeaponState.Mode = ExactTransferPreBattleWeaponStateMode.MeleeHold;
                preBattleWeaponState.PreferredMainHandSlotIndex = (int)firstPrimaryMelee.Slot;
                preBattleWeaponState.PreferredOffHandSlotIndex =
                    CanPairShieldWithWeapon(firstPrimaryMelee)
                        ? (int?)firstShield?.Slot
                        : null;
                preBattleWeaponState.InitialWeaponEquipPreference = Equipment.InitialWeaponEquipPreference.MeleeForMainHand;
                preBattleWeaponState.DecisionReason = firstShield != null
                    ? "ai-melee-hold-with-shield-prebattle-state"
                    : "ai-melee-hold-prebattle-state";
            }
        }

        private static void PopulateControl(
            ExactTransferControlContract control,
            RosterEntryState entryState,
            int teamIndex,
            int formationIndex)
        {
            control.TeamIndex = teamIndex;
            control.FormationIndex = formationIndex;
            control.IsCommanderEntry = entryState.IsHero;
            control.CanReceivePlayerOrders = entryState.IsHero;
            control.EnableCommanderControlOnlyAfterExactReady = true;
        }

        private static void PopulateCleanup(ExactTransferCleanupContract cleanup)
        {
            cleanup.ClearTransferStateOnAgentRemoved = true;
            cleanup.ClearTransferStateOnMountRemoved = true;
            cleanup.ClearTransferStateOnDeath = true;
            cleanup.RejectAgentIndexReuseWithoutIdentityMatch = true;
        }

        private static void PopulateSpawnPolicy(
            ExactTransferSpawnPolicyContract spawnPolicy,
            bool isStrictHero,
            bool isRuntimeExactSupported)
        {
            spawnPolicy.UseStrictExactHeroPath = isStrictHero;
            spawnPolicy.RequirePreSpawnInjection = isStrictHero || isRuntimeExactSupported;
            spawnPolicy.AllowClientVisualOverlayAsRecoveryOnly = true;
            spawnPolicy.ForbidSurrogatePrimaryMaterialization = true;
        }

        private static bool IsStrictHeroEntry(RosterEntryState entryState)
        {
            return entryState != null &&
                   (entryState.IsHero ||
                    !string.IsNullOrWhiteSpace(entryState.HeroId) ||
                    string.Equals(entryState.OriginalCharacterId, "main_hero", StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasAnyWeaponItem(RosterEntryState entryState)
        {
            return entryState != null &&
                   (!string.IsNullOrWhiteSpace(entryState.CombatItem0Id) ||
                    !string.IsNullOrWhiteSpace(entryState.CombatItem1Id) ||
                    !string.IsNullOrWhiteSpace(entryState.CombatItem2Id) ||
                    !string.IsNullOrWhiteSpace(entryState.CombatItem3Id));
        }

        private static bool ShouldBypassLegacyPostCreateWeaponOverlayForCanonicalFieldBattle(
            RosterEntryState entryState)
        {
            if (entryState == null)
                return false;

            BattleSnapshotMessage snapshot = BattleSnapshotRuntimeState.GetCurrent();
            if (snapshot?.CanonicalBattle == null)
                return false;

            // The canonical field-battle path already preloads exact compatibility
            // aliases into the dedicated runtime. Keeping the old post-create overlay
            // gate here forces shield-only native shells for ordinary melee troops and
            // crashes the dedicated create-agent weapon corridor before result import.
            return string.Equals(
                snapshot.CanonicalBattle.Context?.MultiplayerGameType,
                "Battle",
                StringComparison.OrdinalIgnoreCase);
        }

        internal static bool TryNormalizeStrictHeroWeaponLayoutInPlace(
            Equipment equipment,
            RosterEntryState entryState,
            out bool normalized,
            out string summary)
        {
            normalized = false;
            summary = "(none)";
            if (equipment == null || entryState == null)
                return false;

            List<MountedWeaponSlotState> slots = ResolveMountedWeaponSlots(equipment);
            if (slots.Count == 0)
                return false;

            bool hasRanged = slots.Any(slot => slot.Role == MountedWeaponRole.Ranged);
            bool hasAmmo = slots.Any(slot => slot.Role == MountedWeaponRole.Ammo);
            bool hasUnsafeRangedWeapon2Layout = DoesEquipmentContainUnsafeRangedWeapon2Layout(equipment);
            if (!hasRanged && !hasAmmo && !DoesWeapon2ContainLiveCandidate(equipment))
            {
                summary = BuildMountedLayoutSummary(slots, slots);
                return true;
            }

            bool preferStrictHeroRangedLayout = IsStrictHeroEntry(entryState);
            List<MountedWeaponSlotState> orderedSlots = null;
            string profileSummary = null;
            if (preferStrictHeroRangedLayout)
                orderedSlots = BuildCanonicalStrictHeroWeaponLayout(slots, entryState, out profileSummary);
            else if (entryState.IsMounted && (hasRanged || hasAmmo || DoesWeapon2ContainLiveCandidate(equipment)))
                orderedSlots = BuildCanonicalMountedWeaponLayout(slots, hasAmmo);

            if (orderedSlots == null)
            {
                summary = BuildMountedLayoutSummary(slots, slots);
                return true;
            }

            normalized = !DoMountedLayoutsMatch(slots, orderedSlots);
            if (normalized)
                ApplyNormalizedMountedWeaponLayout(equipment, orderedSlots);

            summary =
                (string.IsNullOrWhiteSpace(profileSummary)
                    ? string.Empty
                    : "Profile={" + profileSummary + "} ") +
                "Before={" + BuildMountedLayoutSummary(slots, slots) +
                "} After={" + BuildMountedLayoutSummary(slots, orderedSlots) + "}";
            return true;
        }

        private static void NormalizeStrictHeroWeaponLayout(
            ExactTransferEquipmentContract equipment,
            RosterEntryState entryState,
            bool isStrictHeroEntry)
        {
            if (equipment?.SpawnEquipment == null || entryState == null || !isStrictHeroEntry)
                return;

            if (!TryNormalizeStrictHeroWeaponLayoutInPlace(
                    equipment.SpawnEquipment,
                    entryState,
                    out bool normalized,
                    out string summary))
            {
                return;
            }

            equipment.MountedWeaponLayoutNormalized = normalized;
            equipment.MountedWeaponLayoutSummary = summary;
        }

        private static List<MountedWeaponSlotState> BuildCanonicalStrictHeroWeaponLayout(
            List<MountedWeaponSlotState> sourceSlots,
            RosterEntryState entryState,
            out string profileSummary)
        {
            var remaining = new List<MountedWeaponSlotState>(sourceSlots ?? Enumerable.Empty<MountedWeaponSlotState>());
            var ordered = new List<MountedWeaponSlotState>(4);
            StrictHeroWeaponProfileAnalysis profile = AnalyzeStrictHeroWeaponProfile(sourceSlots, entryState);
            profileSummary = BuildStrictHeroWeaponProfileSummary(profile);

            switch (profile.PrimaryProfile)
            {
                case StrictHeroWeaponProfile.MountedBow:
                    BuildCanonicalStrictHeroMountedRangedWeaponLayout(
                        ordered,
                        remaining,
                        primaryRangedPredicate: IsBowWeaponSlot,
                        primaryAmmoPredicate: IsArrowAmmoSlot,
                        secondaryRangedPredicate: IsCrossbowWeaponSlot,
                        secondaryAmmoPredicate: IsBoltAmmoSlot,
                        hasShield: profile.HasShield,
                        preferThrownOverMelee: true);
                    break;
                case StrictHeroWeaponProfile.MountedCrossbow:
                    BuildCanonicalStrictHeroMountedRangedWeaponLayout(
                        ordered,
                        remaining,
                        primaryRangedPredicate: IsCrossbowWeaponSlot,
                        primaryAmmoPredicate: IsBoltAmmoSlot,
                        secondaryRangedPredicate: IsBowWeaponSlot,
                        secondaryAmmoPredicate: IsArrowAmmoSlot,
                        hasShield: profile.HasShield,
                        preferThrownOverMelee: true);
                    break;
                case StrictHeroWeaponProfile.FootBow:
                    AddStrictHeroPrimaryBowSet(ordered, remaining);
                    AddStrictHeroSecondaryBowAmmo(ordered, remaining);
                    AddStrictHeroSecondaryCrossbowSet(ordered, remaining);
                    AddStrictHeroShield(ordered, remaining);
                    AddStrictHeroThrown(ordered, remaining);
                    AddStrictHeroPrimaryMelee(ordered, remaining, preferShieldCompatible: profile.HasShield);
                    AddStrictHeroRemainingMelee(ordered, remaining, preferShieldCompatible: profile.HasShield);
                    break;
                case StrictHeroWeaponProfile.FootCrossbow:
                    AddStrictHeroPrimaryCrossbowSet(ordered, remaining);
                    AddStrictHeroSecondaryCrossbowAmmo(ordered, remaining);
                    AddStrictHeroShield(ordered, remaining);
                    AddStrictHeroThrown(ordered, remaining);
                    AddStrictHeroPrimaryMelee(ordered, remaining, preferShieldCompatible: profile.HasShield);
                    AddStrictHeroRemainingMelee(ordered, remaining, preferShieldCompatible: profile.HasShield);
                    break;
                case StrictHeroWeaponProfile.MountedThrownWithShield:
                case StrictHeroWeaponProfile.FootThrownWithShield:
                    AddStrictHeroThrown(ordered, remaining);
                    AddStrictHeroShield(ordered, remaining);
                    AddStrictHeroPrimaryMelee(ordered, remaining, preferShieldCompatible: true);
                    AddStrictHeroRemainingMelee(ordered, remaining, preferShieldCompatible: false);
                    AddStrictHeroLooseRanged(ordered, remaining);
                    AddStrictHeroLooseAmmo(ordered, remaining);
                    break;
                case StrictHeroWeaponProfile.MountedThrownWithoutShield:
                case StrictHeroWeaponProfile.FootThrownWithoutShield:
                    AddStrictHeroThrown(ordered, remaining);
                    AddStrictHeroPrimaryMelee(ordered, remaining, preferShieldCompatible: false);
                    AddStrictHeroRemainingMelee(ordered, remaining, preferShieldCompatible: false);
                    AddStrictHeroShield(ordered, remaining);
                    AddStrictHeroLooseRanged(ordered, remaining);
                    AddStrictHeroLooseAmmo(ordered, remaining);
                    break;
                case StrictHeroWeaponProfile.MountedMeleeWithShield:
                case StrictHeroWeaponProfile.FootMeleeWithShield:
                    AddStrictHeroPrimaryMelee(ordered, remaining, preferShieldCompatible: true);
                    AddStrictHeroShield(ordered, remaining);
                    AddStrictHeroRemainingMelee(ordered, remaining, preferShieldCompatible: false);
                    AddStrictHeroThrown(ordered, remaining);
                    AddStrictHeroLooseRanged(ordered, remaining);
                    AddStrictHeroLooseAmmo(ordered, remaining);
                    break;
                case StrictHeroWeaponProfile.MountedMeleeWithoutShield:
                case StrictHeroWeaponProfile.FootMeleeWithoutShield:
                    AddStrictHeroPrimaryMelee(ordered, remaining, preferShieldCompatible: false);
                    AddStrictHeroRemainingMelee(ordered, remaining, preferShieldCompatible: false);
                    AddStrictHeroThrown(ordered, remaining);
                    AddStrictHeroShield(ordered, remaining);
                    AddStrictHeroLooseRanged(ordered, remaining);
                    AddStrictHeroLooseAmmo(ordered, remaining);
                    break;
                default:
                    AddStrictHeroPrimaryMelee(ordered, remaining, preferShieldCompatible: profile.HasShield);
                    AddStrictHeroShield(ordered, remaining);
                    AddStrictHeroThrown(ordered, remaining);
                    AddStrictHeroLooseRanged(ordered, remaining);
                    AddStrictHeroLooseAmmo(ordered, remaining);
                    break;
            }

            while (ordered.Count < 4 && remaining.Count > 0)
            {
                ordered.Add(remaining[0]);
                remaining.RemoveAt(0);
            }

            return ordered.Take(4).ToList();
        }

        private static StrictHeroWeaponProfileAnalysis AnalyzeStrictHeroWeaponProfile(
            List<MountedWeaponSlotState> sourceSlots,
            RosterEntryState entryState)
        {
            List<MountedWeaponSlotState> slots = sourceSlots ?? new List<MountedWeaponSlotState>();
            bool isMounted = entryState?.IsMounted == true;
            bool hasBow = slots.Any(IsBowWeaponSlot);
            bool hasArrows = slots.Any(IsArrowAmmoSlot);
            bool hasCrossbow = slots.Any(IsCrossbowWeaponSlot);
            bool hasBolts = slots.Any(IsBoltAmmoSlot);
            bool hasShield = slots.Any(slot => slot.Role == MountedWeaponRole.Shield);
            bool hasThrown = slots.Any(IsThrownWeaponSlot);
            bool hasMelee = slots.Any(IsMeleeWeaponSlot);
            bool hasShieldCompatibleMelee = slots.Any(IsShieldCompatibleMeleeSlot);
            bool hasBowSet = hasBow && hasArrows;
            bool hasCrossbowSet = hasCrossbow && hasBolts;

            StrictHeroWeaponProfile primaryProfile;
            if (isMounted)
            {
                if (hasBowSet)
                    primaryProfile = StrictHeroWeaponProfile.MountedBow;
                else if (hasCrossbowSet)
                    primaryProfile = StrictHeroWeaponProfile.MountedCrossbow;
                else if (hasThrown)
                    primaryProfile = hasShield && hasShieldCompatibleMelee
                        ? StrictHeroWeaponProfile.MountedThrownWithShield
                        : StrictHeroWeaponProfile.MountedThrownWithoutShield;
                else
                    primaryProfile = hasShield && hasShieldCompatibleMelee
                        ? StrictHeroWeaponProfile.MountedMeleeWithShield
                        : StrictHeroWeaponProfile.MountedMeleeWithoutShield;
            }
            else
            {
                if (hasBowSet)
                    primaryProfile = StrictHeroWeaponProfile.FootBow;
                else if (hasCrossbowSet)
                    primaryProfile = StrictHeroWeaponProfile.FootCrossbow;
                else if (hasThrown)
                    primaryProfile = hasShield && hasShieldCompatibleMelee
                        ? StrictHeroWeaponProfile.FootThrownWithShield
                        : StrictHeroWeaponProfile.FootThrownWithoutShield;
                else
                    primaryProfile = hasShield && hasShieldCompatibleMelee
                        ? StrictHeroWeaponProfile.FootMeleeWithShield
                        : StrictHeroWeaponProfile.FootMeleeWithoutShield;
            }

            return new StrictHeroWeaponProfileAnalysis
            {
                PrimaryProfile = primaryProfile,
                IsMounted = isMounted,
                HasBowSet = hasBowSet,
                HasCrossbowSet = hasCrossbowSet,
                HasShield = hasShield,
                HasThrown = hasThrown,
                HasMelee = hasMelee,
                HasShieldCompatibleMelee = hasShieldCompatibleMelee
            };
        }

        private static string BuildStrictHeroWeaponProfileSummary(StrictHeroWeaponProfileAnalysis profile)
        {
            if (profile == null)
                return "Primary=unknown";

            var backupProfiles = new List<string>();
            if (profile.HasBowSet && profile.HasCrossbowSet)
                backupProfiles.Add(profile.IsMounted ? "кінний-арбалетчик" : "піший-арбалетчик");
            if (profile.HasShield)
                backupProfiles.Add(profile.HasShieldCompatibleMelee ? "щитова-гілка" : "щит-без-сумісної-ближньої");
            if (profile.HasThrown)
                backupProfiles.Add(profile.IsMounted
                    ? (profile.HasShield && profile.HasShieldCompatibleMelee ? "кінний-метальник-зі-щитом" : "кінний-метальник-без-щита")
                    : (profile.HasShield && profile.HasShieldCompatibleMelee ? "піший-метальник-зі-щитом" : "піший-метальник-без-щита"));
            if (profile.HasMelee)
                backupProfiles.Add(profile.IsMounted
                    ? (profile.HasShieldCompatibleMelee ? "кінний-ближній-зі-щитом" : "кінний-ближній-без-щита")
                    : (profile.HasShieldCompatibleMelee ? "піший-ближній-зі-щитом" : "піший-ближній-без-щита"));

            return
                "Primary=" + MapStrictHeroWeaponProfileLabel(profile.PrimaryProfile) +
                ",Mounted=" + profile.IsMounted +
                ",HasBowSet=" + profile.HasBowSet +
                ",HasCrossbowSet=" + profile.HasCrossbowSet +
                ",HasShield=" + profile.HasShield +
                ",HasThrown=" + profile.HasThrown +
                ",HasMelee=" + profile.HasMelee +
                ",HasShieldCompatibleMelee=" + profile.HasShieldCompatibleMelee +
                ",Backups=" + (backupProfiles.Count > 0 ? string.Join(">", backupProfiles.Distinct()) : "(none)");
        }

        private static string MapStrictHeroWeaponProfileLabel(StrictHeroWeaponProfile profile)
        {
            switch (profile)
            {
                case StrictHeroWeaponProfile.MountedBow:
                    return "кінний-лучник";
                case StrictHeroWeaponProfile.MountedCrossbow:
                    return "кінний-арбалетчик";
                case StrictHeroWeaponProfile.MountedThrownWithShield:
                    return "кінний-метальник-зі-щитом";
                case StrictHeroWeaponProfile.MountedThrownWithoutShield:
                    return "кінний-метальник-без-щита";
                case StrictHeroWeaponProfile.MountedMeleeWithShield:
                    return "кінний-ближній-зі-щитом";
                case StrictHeroWeaponProfile.MountedMeleeWithoutShield:
                    return "кінний-ближній-без-щита";
                case StrictHeroWeaponProfile.FootBow:
                    return "піший-лучник";
                case StrictHeroWeaponProfile.FootCrossbow:
                    return "піший-арбалетчик";
                case StrictHeroWeaponProfile.FootThrownWithShield:
                    return "піший-метальник-зі-щитом";
                case StrictHeroWeaponProfile.FootThrownWithoutShield:
                    return "піший-метальник-без-щита";
                case StrictHeroWeaponProfile.FootMeleeWithShield:
                    return "піший-ближній-зі-щитом";
                case StrictHeroWeaponProfile.FootMeleeWithoutShield:
                    return "піший-ближній-без-щита";
                default:
                    return "невідомий";
            }
        }

        private static List<MountedWeaponSlotState> ResolveMountedWeaponSlots(RosterEntryState entryState)
        {
            var slots = new List<MountedWeaponSlotState>();
            TryAddMountedWeaponSlot(slots, EquipmentIndex.Weapon0, "Item0", entryState.CombatItem0Id);
            TryAddMountedWeaponSlot(slots, EquipmentIndex.Weapon1, "Item1", entryState.CombatItem1Id);
            TryAddMountedWeaponSlot(slots, EquipmentIndex.Weapon2, "Item2", entryState.CombatItem2Id);
            TryAddMountedWeaponSlot(slots, EquipmentIndex.Weapon3, "Item3", entryState.CombatItem3Id);
            return slots;
        }

        private static List<MountedWeaponSlotState> ResolveMountedWeaponSlots(Equipment equipment)
        {
            var slots = new List<MountedWeaponSlotState>();
            TryAddMountedWeaponSlot(slots, EquipmentIndex.Weapon0, equipment?[EquipmentIndex.Weapon0].Item);
            TryAddMountedWeaponSlot(slots, EquipmentIndex.Weapon1, equipment?[EquipmentIndex.Weapon1].Item);
            TryAddMountedWeaponSlot(slots, EquipmentIndex.Weapon2, equipment?[EquipmentIndex.Weapon2].Item);
            TryAddMountedWeaponSlot(slots, EquipmentIndex.Weapon3, equipment?[EquipmentIndex.Weapon3].Item);
            return slots;
        }

        private static void TryAddMountedWeaponSlot(
            List<MountedWeaponSlotState> slots,
            EquipmentIndex slot,
            string slotLabel,
            string itemId)
        {
            if (slots == null || string.IsNullOrWhiteSpace(itemId))
                return;

            ItemObject item = ResolveItem(itemId);
            if (item == null)
                return;

            slots.Add(new MountedWeaponSlotState
            {
                Slot = slot,
                SlotLabel = slotLabel,
                ItemId = itemId,
                Item = item,
                Role = ResolveMountedWeaponRole(item)
            });
        }

        private static void TryAddMountedWeaponSlot(
            List<MountedWeaponSlotState> slots,
            EquipmentIndex slot,
            ItemObject item)
        {
            if (slots == null || item == null)
                return;

            slots.Add(new MountedWeaponSlotState
            {
                Slot = slot,
                SlotLabel = slot.ToString(),
                ItemId = item.StringId,
                Item = item,
                Role = ResolveMountedWeaponRole(item)
            });
        }

        private static ItemObject ResolveItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
                return null;

            try
            {
                MBObjectManager objectManager = Game.Current?.ObjectManager ?? MBObjectManager.Instance;
                return objectManager?.GetObject<ItemObject>(itemId);
            }
            catch
            {
                return null;
            }
        }

        private static List<MountedWeaponSlotState> BuildCanonicalMountedWeaponLayout(
            List<MountedWeaponSlotState> sourceSlots,
            bool hasAmmo)
        {
            var remaining = new List<MountedWeaponSlotState>(sourceSlots ?? Enumerable.Empty<MountedWeaponSlotState>());
            var ordered = new List<MountedWeaponSlotState>(4);
            bool hasShield = remaining.Any(slot => slot.Role == MountedWeaponRole.Shield);
            bool hasRanged = remaining.Any(slot => slot.Role == MountedWeaponRole.Ranged);

            if (hasAmmo)
            {
                AddFirstByRole(ordered, remaining, MountedWeaponRole.Ranged);
                if (hasShield)
                {
                    AddFirstByRole(ordered, remaining, MountedWeaponRole.Shield);
                    AddFirstByRole(ordered, remaining, MountedWeaponRole.Ammo);
                    AddMountedPrimaryMelee(ordered, remaining, preferShieldCompatible: true);
                    AddFirstByRole(ordered, remaining, MountedWeaponRole.Other);
                }
                else
                {
                    AddFirstByRole(ordered, remaining, MountedWeaponRole.Ammo);
                    if (!TryAddMountedReserveAmmo(ordered, remaining))
                        AddEmptyMountedWeaponSlotPlaceholder(ordered);
                    AddMountedPrimaryMelee(ordered, remaining, preferShieldCompatible: false);
                    AddFirstByRole(ordered, remaining, MountedWeaponRole.Other);
                }
            }
            else if (hasRanged)
            {
                AddMountedPrimaryMelee(ordered, remaining, preferShieldCompatible: hasShield);
                if (hasShield)
                    AddFirstByRole(ordered, remaining, MountedWeaponRole.Shield);
                AddFirstByRole(ordered, remaining, MountedWeaponRole.Ranged);
                AddMountedPrimaryMelee(ordered, remaining, preferShieldCompatible: false);
                AddFirstByRole(ordered, remaining, MountedWeaponRole.Other);
            }
            else
            {
                AddMountedPrimaryMelee(ordered, remaining, preferShieldCompatible: hasShield);
                if (hasShield)
                    AddFirstByRole(ordered, remaining, MountedWeaponRole.Shield);
                AddMountedPrimaryMelee(ordered, remaining, preferShieldCompatible: false);
                AddFirstByRole(ordered, remaining, MountedWeaponRole.Other);
            }

            while (ordered.Count < 4 && remaining.Count > 0)
            {
                ordered.Add(remaining[0]);
                remaining.RemoveAt(0);
            }

            return ordered.Take(4).ToList();
        }

        private static void BuildCanonicalStrictHeroMountedRangedWeaponLayout(
            List<MountedWeaponSlotState> ordered,
            List<MountedWeaponSlotState> remaining,
            Func<MountedWeaponSlotState, bool> primaryRangedPredicate,
            Func<MountedWeaponSlotState, bool> primaryAmmoPredicate,
            Func<MountedWeaponSlotState, bool> secondaryRangedPredicate,
            Func<MountedWeaponSlotState, bool> secondaryAmmoPredicate,
            bool hasShield,
            bool preferThrownOverMelee)
        {
            AddFirst(ordered, remaining, primaryRangedPredicate);
            if (hasShield)
            {
                AddStrictHeroShield(ordered, remaining);
                AddFirst(ordered, remaining, primaryAmmoPredicate);
                AddStrictHeroShieldedMountedRangedBackup(
                    ordered,
                    remaining,
                    primaryAmmoPredicate,
                    preferThrownOverMelee,
                    preferShieldCompatibleMelee: true);
                return;
            }

            bool hasThrownBackup = remaining.Any(IsThrownWeaponSlot);
            bool hasMeleeBackup = remaining.Any(IsMeleeWeaponSlot);
            int primaryAmmoCount = remaining.Count(primaryAmmoPredicate);
            bool hasReservePrimaryAmmo = primaryAmmoCount > 1;
            bool hasSecondaryRangedSet = remaining.Any(secondaryRangedPredicate) && remaining.Any(secondaryAmmoPredicate);
            if (primaryAmmoCount > 0 &&
                hasThrownBackup &&
                hasMeleeBackup &&
                !hasReservePrimaryAmmo &&
                !hasSecondaryRangedSet)
            {
                AddFirst(ordered, remaining, primaryAmmoPredicate);
                // Keep the primary ammo immediately after the mounted ranged weapon.
                // The live weapon/ammo corridor assumes Weapon1 is the ammo slot.
                AddStrictHeroThrown(ordered, remaining);
                AddStrictHeroPrimaryMelee(ordered, remaining, preferShieldCompatible: false);
                AddStrictHeroRemainingMelee(ordered, remaining, preferShieldCompatible: false);
                return;
            }

            AddFirst(ordered, remaining, primaryAmmoPredicate);
            StrictHeroMountedRangedReserveKind reserveKind = ResolveStrictHeroMountedRangedReserveSlot(
                ordered,
                remaining,
                primaryAmmoPredicate,
                secondaryRangedPredicate,
                secondaryAmmoPredicate);
            if (reserveKind == StrictHeroMountedRangedReserveKind.EmptyPlaceholder)
                AddEmptyMountedWeaponSlotPlaceholder(ordered);

            AddStrictHeroUnshieldedMountedRangedBackup(
                ordered,
                remaining,
                secondaryRangedPredicate,
                reserveKind,
                preferThrownOverMelee,
                preferShieldCompatibleMelee: false);
        }

        private enum StrictHeroMountedRangedReserveKind
        {
            PrimaryAmmo = 0,
            SecondaryBackupAmmo = 1,
            EmptyPlaceholder = 2
        }

        private static StrictHeroMountedRangedReserveKind ResolveStrictHeroMountedRangedReserveSlot(
            List<MountedWeaponSlotState> ordered,
            List<MountedWeaponSlotState> remaining,
            Func<MountedWeaponSlotState, bool> primaryAmmoPredicate,
            Func<MountedWeaponSlotState, bool> secondaryRangedPredicate,
            Func<MountedWeaponSlotState, bool> secondaryAmmoPredicate)
        {
            if (ordered == null || remaining == null)
                return StrictHeroMountedRangedReserveKind.EmptyPlaceholder;

            MountedWeaponSlotState reserveAmmo = TakeFirst(remaining, primaryAmmoPredicate);
            if (reserveAmmo != null)
            {
                ordered.Add(reserveAmmo);
                return StrictHeroMountedRangedReserveKind.PrimaryAmmo;
            }

            if (remaining.Any(secondaryRangedPredicate))
            {
                reserveAmmo = TakeFirst(remaining, secondaryAmmoPredicate);
            }

            if (reserveAmmo == null)
                return StrictHeroMountedRangedReserveKind.EmptyPlaceholder;

            ordered.Add(reserveAmmo);
            return StrictHeroMountedRangedReserveKind.SecondaryBackupAmmo;
        }

        private static void AddStrictHeroShieldedMountedRangedBackup(
            List<MountedWeaponSlotState> ordered,
            List<MountedWeaponSlotState> remaining,
            Func<MountedWeaponSlotState, bool> primaryAmmoPredicate,
            bool preferThrownOverMelee,
            bool preferShieldCompatibleMelee)
        {
            if (ordered == null || remaining == null)
                return;

            if (TakeAndAdd(ordered, remaining, primaryAmmoPredicate))
                return;

            if (preferThrownOverMelee && TakeAndAdd(ordered, remaining, IsThrownWeaponSlot))
                return;

            if (preferShieldCompatibleMelee && TakeAndAdd(ordered, remaining, IsShieldCompatibleMeleeSlot))
                return;

            if (TakeAndAdd(ordered, remaining, IsMeleeWeaponSlot))
                return;

            if (!preferThrownOverMelee)
                TakeAndAdd(ordered, remaining, IsThrownWeaponSlot);
        }

        private static void AddStrictHeroUnshieldedMountedRangedBackup(
            List<MountedWeaponSlotState> ordered,
            List<MountedWeaponSlotState> remaining,
            Func<MountedWeaponSlotState, bool> secondaryRangedPredicate,
            StrictHeroMountedRangedReserveKind reserveKind,
            bool preferThrownOverMelee,
            bool preferShieldCompatibleMelee)
        {
            if (ordered == null || remaining == null)
                return;

            if (reserveKind == StrictHeroMountedRangedReserveKind.SecondaryBackupAmmo &&
                TakeAndAdd(ordered, remaining, secondaryRangedPredicate))
            {
                return;
            }

            if (preferThrownOverMelee && TakeAndAdd(ordered, remaining, IsThrownWeaponSlot))
                return;

            if (preferShieldCompatibleMelee && TakeAndAdd(ordered, remaining, IsShieldCompatibleMeleeSlot))
                return;

            if (TakeAndAdd(ordered, remaining, IsMeleeWeaponSlot))
                return;

            if (!preferThrownOverMelee)
                TakeAndAdd(ordered, remaining, IsThrownWeaponSlot);
        }

        private static bool TryAddMountedReserveAmmo(
            List<MountedWeaponSlotState> ordered,
            List<MountedWeaponSlotState> remaining)
        {
            if (ordered == null || remaining == null)
                return false;

            MountedWeaponSlotState reserveAmmo = TakeFirst(remaining, MountedWeaponRole.Ammo);
            if (reserveAmmo == null)
                return false;

            ordered.Add(reserveAmmo);
            return true;
        }

        private static void AddEmptyMountedWeaponSlotPlaceholder(List<MountedWeaponSlotState> ordered)
        {
            if (ordered == null || ordered.Count >= 4)
                return;

            ordered.Add(null);
        }

        private static bool TakeAndAdd(
            List<MountedWeaponSlotState> ordered,
            List<MountedWeaponSlotState> remaining,
            Func<MountedWeaponSlotState, bool> predicate)
        {
            MountedWeaponSlotState match = TakeFirst(remaining, predicate);
            if (match == null)
                return false;

            ordered?.Add(match);
            return true;
        }

        private static void AddNextPreferred(
            List<MountedWeaponSlotState> ordered,
            List<MountedWeaponSlotState> remaining,
            bool preferLiveCandidate)
        {
            if (remaining == null || remaining.Count == 0)
                return;

            MountedWeaponSlotState match = preferLiveCandidate
                ? remaining.FirstOrDefault(slot =>
                    slot.Role == MountedWeaponRole.Polearm ||
                    slot.Role == MountedWeaponRole.Melee ||
                    slot.Role == MountedWeaponRole.Ranged)
                : remaining.FirstOrDefault(slot =>
                    slot.Role == MountedWeaponRole.Ammo ||
                    slot.Role == MountedWeaponRole.Shield ||
                    slot.Role == MountedWeaponRole.Other);

            if (match == null)
                match = remaining[0];
            ordered.Add(match);
            remaining.Remove(match);
        }

        private static void AddMountedPrimaryMelee(
            List<MountedWeaponSlotState> ordered,
            List<MountedWeaponSlotState> remaining,
            bool preferShieldCompatible)
        {
            if (ordered == null || remaining == null || remaining.Count == 0)
                return;

            MountedWeaponSlotState match = null;
            if (preferShieldCompatible)
            {
                match = remaining.FirstOrDefault(slot =>
                    slot != null &&
                    (slot.Role == MountedWeaponRole.Polearm || slot.Role == MountedWeaponRole.Melee) &&
                    ((((int?)slot.Item?.PrimaryWeapon?.WeaponFlags) ?? 0) & (int)WeaponFlags.NotUsableWithOneHand) != (int)WeaponFlags.NotUsableWithOneHand);
            }

            if (match == null)
                match = TakeFirst(remaining, MountedWeaponRole.Polearm) ?? TakeFirst(remaining, MountedWeaponRole.Melee);
            else
                remaining.Remove(match);

            if (match != null)
                ordered.Add(match);
        }

        private static void AddStrictHeroPrimaryBowSet(
            List<MountedWeaponSlotState> ordered,
            List<MountedWeaponSlotState> remaining)
        {
            AddFirst(ordered, remaining, IsBowWeaponSlot);
            AddFirst(ordered, remaining, IsArrowAmmoSlot);
        }

        private static void AddStrictHeroSecondaryBowAmmo(
            List<MountedWeaponSlotState> ordered,
            List<MountedWeaponSlotState> remaining)
        {
            AddFirst(ordered, remaining, IsArrowAmmoSlot);
        }

        private static void AddStrictHeroPrimaryCrossbowSet(
            List<MountedWeaponSlotState> ordered,
            List<MountedWeaponSlotState> remaining)
        {
            AddFirst(ordered, remaining, IsCrossbowWeaponSlot);
            AddFirst(ordered, remaining, IsBoltAmmoSlot);
        }

        private static void AddStrictHeroSecondaryCrossbowAmmo(
            List<MountedWeaponSlotState> ordered,
            List<MountedWeaponSlotState> remaining)
        {
            AddFirst(ordered, remaining, IsBoltAmmoSlot);
        }

        private static void AddStrictHeroSecondaryCrossbowSet(
            List<MountedWeaponSlotState> ordered,
            List<MountedWeaponSlotState> remaining)
        {
            AddFirst(ordered, remaining, IsCrossbowWeaponSlot);
            AddFirst(ordered, remaining, IsBoltAmmoSlot);
        }

        private static void AddStrictHeroShield(
            List<MountedWeaponSlotState> ordered,
            List<MountedWeaponSlotState> remaining)
        {
            AddFirst(ordered, remaining, slot => slot?.Role == MountedWeaponRole.Shield);
        }

        private static void AddStrictHeroThrown(
            List<MountedWeaponSlotState> ordered,
            List<MountedWeaponSlotState> remaining)
        {
            AddFirst(ordered, remaining, IsThrownWeaponSlot);
        }

        private static void AddStrictHeroPrimaryMelee(
            List<MountedWeaponSlotState> ordered,
            List<MountedWeaponSlotState> remaining,
            bool preferShieldCompatible)
        {
            if (ordered == null || remaining == null)
                return;

            MountedWeaponSlotState match = preferShieldCompatible
                ? TakeFirst(remaining, IsShieldCompatibleMeleeSlot)
                : null;

            if (match == null)
                match = TakeFirst(remaining, IsMeleeWeaponSlot);

            if (match != null)
                ordered.Add(match);
        }

        private static void AddStrictHeroRemainingMelee(
            List<MountedWeaponSlotState> ordered,
            List<MountedWeaponSlotState> remaining,
            bool preferShieldCompatible)
        {
            if (ordered == null || remaining == null)
                return;

            MountedWeaponSlotState match = preferShieldCompatible
                ? TakeFirst(remaining, IsShieldCompatibleMeleeSlot)
                : null;

            if (match == null)
                match = TakeFirst(remaining, IsMeleeWeaponSlot);

            if (match != null)
                ordered.Add(match);
        }

        private static void AddStrictHeroLooseRanged(
            List<MountedWeaponSlotState> ordered,
            List<MountedWeaponSlotState> remaining)
        {
            AddFirst(ordered, remaining, slot => slot?.Role == MountedWeaponRole.Ranged);
        }

        private static void AddStrictHeroLooseAmmo(
            List<MountedWeaponSlotState> ordered,
            List<MountedWeaponSlotState> remaining)
        {
            AddFirst(ordered, remaining, slot => slot?.Role == MountedWeaponRole.Ammo);
        }

        private static MountedWeaponSlotState TakeFirst(List<MountedWeaponSlotState> remaining, MountedWeaponRole role)
        {
            if (remaining == null || remaining.Count == 0)
                return null;

            MountedWeaponSlotState match = remaining.FirstOrDefault(slot => slot.Role == role);
            if (match == null)
                return null;

            remaining.Remove(match);
            return match;
        }

        private static MountedWeaponSlotState TakeFirst(
            List<MountedWeaponSlotState> remaining,
            Func<MountedWeaponSlotState, bool> predicate)
        {
            if (remaining == null || remaining.Count == 0 || predicate == null)
                return null;

            MountedWeaponSlotState match = remaining.FirstOrDefault(predicate);
            if (match == null)
                return null;

            remaining.Remove(match);
            return match;
        }

        private static void AddFirstByRole(
            List<MountedWeaponSlotState> ordered,
            List<MountedWeaponSlotState> remaining,
            MountedWeaponRole role)
        {
            MountedWeaponSlotState match = TakeFirst(remaining, role);
            if (match != null)
                ordered.Add(match);
        }

        private static void AddFirst(
            List<MountedWeaponSlotState> ordered,
            List<MountedWeaponSlotState> remaining,
            Func<MountedWeaponSlotState, bool> predicate)
        {
            MountedWeaponSlotState match = TakeFirst(remaining, predicate);
            if (match != null)
                ordered.Add(match);
        }

        private static void AddPrimaryLeadSlot(
            List<MountedWeaponSlotState> ordered,
            List<MountedWeaponSlotState> remaining,
            MountedWeaponSlotState preferredLeadSlot)
        {
            if (ordered == null || remaining == null)
                return;

            if (preferredLeadSlot != null &&
                remaining.Contains(preferredLeadSlot) &&
                preferredLeadSlot.Role != MountedWeaponRole.Ranged &&
                preferredLeadSlot.Role != MountedWeaponRole.Ammo)
            {
                ordered.Add(preferredLeadSlot);
                remaining.Remove(preferredLeadSlot);
                return;
            }

            MountedWeaponSlotState leadSlot = TakeFirst(remaining, MountedWeaponRole.Polearm) ??
                                              TakeFirst(remaining, MountedWeaponRole.Melee) ??
                                              TakeFirst(remaining, MountedWeaponRole.Shield) ??
                                              remaining.FirstOrDefault();
            if (leadSlot == null)
                return;

            ordered.Add(leadSlot);
            remaining.Remove(leadSlot);
        }

        private static void ApplyNormalizedMountedWeaponLayout(
            ExactTransferEquipmentContract equipment,
            List<MountedWeaponSlotState> orderedSlots)
        {
            if (equipment?.SpawnEquipment == null)
                return;

            EquipmentIndex[] targetSlots =
            {
                EquipmentIndex.Weapon0,
                EquipmentIndex.Weapon1,
                EquipmentIndex.Weapon2,
                EquipmentIndex.Weapon3
            };

            foreach (EquipmentIndex targetSlot in targetSlots)
                equipment.SpawnEquipment[targetSlot] = default;

            for (int i = 0; i < targetSlots.Length; i++)
            {
                MountedWeaponSlotState normalizedSlot = orderedSlots != null && i < orderedSlots.Count
                    ? orderedSlots[i]
                    : null;
                ExactTransferEquipmentSlotContract slotContract = equipment.Slots.FirstOrDefault(slot => slot.Slot == targetSlots[i]);
                if (normalizedSlot?.Item == null)
                {
                    if (slotContract != null)
                    {
                        slotContract.ItemId = null;
                        slotContract.IsEmpty = true;
                        slotContract.MustExistAtCreateAgentTime = false;
                    }

                    continue;
                }

                equipment.SpawnEquipment[targetSlots[i]] = new EquipmentElement(normalizedSlot.Item, null, null, false);
                if (slotContract != null)
                {
                    slotContract.ItemId = normalizedSlot.ItemId;
                    slotContract.IsEmpty = false;
                    slotContract.MustExistAtCreateAgentTime = true;
                }
            }
        }

        private static void ApplyNormalizedMountedWeaponLayout(
            Equipment equipment,
            List<MountedWeaponSlotState> orderedSlots)
        {
            if (equipment == null)
                return;

            EquipmentIndex[] targetSlots =
            {
                EquipmentIndex.Weapon0,
                EquipmentIndex.Weapon1,
                EquipmentIndex.Weapon2,
                EquipmentIndex.Weapon3
            };

            foreach (EquipmentIndex targetSlot in targetSlots)
                equipment[targetSlot] = default;

            for (int i = 0; i < targetSlots.Length; i++)
            {
                MountedWeaponSlotState normalizedSlot = orderedSlots != null && i < orderedSlots.Count
                    ? orderedSlots[i]
                    : null;
                if (normalizedSlot?.Item == null)
                    continue;

                equipment[targetSlots[i]] = new EquipmentElement(normalizedSlot.Item, null, null, false);
            }
        }

        private static bool DoMountedLayoutsMatch(
            IReadOnlyList<MountedWeaponSlotState> before,
            IReadOnlyList<MountedWeaponSlotState> after)
        {
            EquipmentIndex[] targetSlots =
            {
                EquipmentIndex.Weapon0,
                EquipmentIndex.Weapon1,
                EquipmentIndex.Weapon2,
                EquipmentIndex.Weapon3
            };

            for (int i = 0; i < targetSlots.Length; i++)
            {
                string beforeItemId = ResolveMountedLayoutItemId(before, targetSlots[i]);
                string afterItemId = after != null && i < after.Count ? after[i]?.ItemId : null;
                if (!string.Equals(beforeItemId, afterItemId, StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        private static string ResolveMountedLayoutItemId(
            IReadOnlyList<MountedWeaponSlotState> slots,
            EquipmentIndex targetSlot)
        {
            if (slots == null)
                return null;

            for (int i = 0; i < slots.Count; i++)
            {
                MountedWeaponSlotState slot = slots[i];
                if (slot != null && slot.Slot == targetSlot)
                    return slot.ItemId;
            }

            return null;
        }

        private static string BuildMountedLayoutSummary(
            IReadOnlyList<MountedWeaponSlotState> sourceSlots,
            IReadOnlyList<MountedWeaponSlotState> orderedSlots)
        {
            EquipmentIndex[] targetSlots =
            {
                EquipmentIndex.Weapon0,
                EquipmentIndex.Weapon1,
                EquipmentIndex.Weapon2,
                EquipmentIndex.Weapon3
            };

            var parts = new List<string>(4);
            for (int i = 0; i < targetSlots.Length; i++)
            {
                MountedWeaponSlotState slot = orderedSlots != null && i < orderedSlots.Count
                    ? orderedSlots[i]
                    : null;
                parts.Add(
                    GetWeaponSlotLabel(targetSlots[i]) + "=" +
                    (slot == null
                        ? "empty"
                        : (slot.ItemId ?? "empty") + ":" + slot.Role));
            }

            return string.Join(", ", parts);
        }

        private static bool DoesWeapon2ContainLiveCandidate(Equipment equipment)
        {
            ItemObject item = equipment?[EquipmentIndex.Weapon2].Item;
            if (item == null)
                return false;

            MountedWeaponRole role = ResolveMountedWeaponRole(item);
            return role == MountedWeaponRole.Melee ||
                   role == MountedWeaponRole.Polearm ||
                   role == MountedWeaponRole.Ranged ||
                   role == MountedWeaponRole.Other;
        }

        internal static bool DoesEquipmentContainUnsafeRangedWeapon2Layout(Equipment equipment)
        {
            if (equipment == null)
                return false;

            if (IsSafeStrictHeroMountedRangedThrownMeleeLayout(equipment))
                return false;

            MountedWeaponRole role0 = ResolveMountedWeaponRole(equipment[EquipmentIndex.Weapon0].Item);
            MountedWeaponRole role1 = ResolveMountedWeaponRole(equipment[EquipmentIndex.Weapon1].Item);
            MountedWeaponRole role2 = ResolveMountedWeaponRole(equipment[EquipmentIndex.Weapon2].Item);
            MountedWeaponRole role3 = ResolveMountedWeaponRole(equipment[EquipmentIndex.Weapon3].Item);

            if (IsSafeFootRangedShieldAmmoLayout(role0, role1, role2, role3))
                return false;

            if (IsSafeFootMeleeShieldRangedAmmoLayout(role0, role1, role2, role3))
                return false;

            if (IsSafeMountedRangedShieldAmmoLayout(role0, role1, role2, role3))
                return false;

            bool hasRanged = role0 == MountedWeaponRole.Ranged ||
                             role1 == MountedWeaponRole.Ranged ||
                             role2 == MountedWeaponRole.Ranged ||
                             role3 == MountedWeaponRole.Ranged;
            bool hasAmmo = role0 == MountedWeaponRole.Ammo ||
                           role1 == MountedWeaponRole.Ammo ||
                           role2 == MountedWeaponRole.Ammo ||
                           role3 == MountedWeaponRole.Ammo;
            if (!hasRanged || !hasAmmo)
                return false;

            if (role0 == MountedWeaponRole.Ammo)
                return true;

            if (role0 == MountedWeaponRole.Ranged)
                return role1 != MountedWeaponRole.Ammo;

            return role2 == MountedWeaponRole.Melee ||
                   role2 == MountedWeaponRole.Polearm ||
                   role2 == MountedWeaponRole.Ranged ||
                   role2 == MountedWeaponRole.Other;
        }

        private static bool IsSafeStrictHeroMountedRangedThrownMeleeLayout(Equipment equipment)
        {
            if (equipment == null)
                return false;

            var slots = ResolveMountedWeaponSlots(equipment);
            MountedWeaponSlotState slot0 = slots.FirstOrDefault(slot => slot?.Slot == EquipmentIndex.Weapon0);
            MountedWeaponSlotState slot1 = slots.FirstOrDefault(slot => slot?.Slot == EquipmentIndex.Weapon1);
            MountedWeaponSlotState slot2 = slots.FirstOrDefault(slot => slot?.Slot == EquipmentIndex.Weapon2);
            MountedWeaponSlotState slot3 = slots.FirstOrDefault(slot => slot?.Slot == EquipmentIndex.Weapon3);
            if (slot0?.Item == null || slot1?.Item == null || slot2?.Item == null || slot3?.Item == null)
                return false;

            bool isBowPattern =
                IsBowWeaponSlot(slot0) &&
                (
                    IsArrowAmmoSlot(slot1) &&
                    IsThrownWeaponSlot(slot2)
                );
            bool isCrossbowPattern =
                IsCrossbowWeaponSlot(slot0) &&
                (
                    IsBoltAmmoSlot(slot1) &&
                    IsThrownWeaponSlot(slot2)
                );
            if (!isBowPattern && !isCrossbowPattern)
                return false;

            return IsMeleeWeaponSlot(slot3) ||
                   slot3.Role == MountedWeaponRole.Other;
        }

        private static bool IsSafeMountedRangedShieldAmmoLayout(
            MountedWeaponRole role0,
            MountedWeaponRole role1,
            MountedWeaponRole role2,
            MountedWeaponRole role3)
        {
            if (role0 != MountedWeaponRole.Ranged ||
                role1 != MountedWeaponRole.Shield ||
                role2 != MountedWeaponRole.Ammo)
            {
                return false;
            }

            return role3 == MountedWeaponRole.Melee ||
                   role3 == MountedWeaponRole.Polearm ||
                   role3 == MountedWeaponRole.Other;
        }

        private static bool IsSafeFootRangedShieldAmmoLayout(
            MountedWeaponRole role0,
            MountedWeaponRole role1,
            MountedWeaponRole role2,
            MountedWeaponRole role3)
        {
            if (role0 != MountedWeaponRole.Ranged ||
                role1 != MountedWeaponRole.Shield ||
                role2 != MountedWeaponRole.Ammo)
            {
                return false;
            }

            return role3 == MountedWeaponRole.Melee ||
                   role3 == MountedWeaponRole.Polearm ||
                   role3 == MountedWeaponRole.Other;
        }

        private static bool IsSafeFootMeleeShieldRangedAmmoLayout(
            MountedWeaponRole role0,
            MountedWeaponRole role1,
            MountedWeaponRole role2,
            MountedWeaponRole role3)
        {
            bool safePrimary =
                role0 == MountedWeaponRole.Melee ||
                role0 == MountedWeaponRole.Polearm ||
                role0 == MountedWeaponRole.Other;
            if (!safePrimary ||
                role1 != MountedWeaponRole.Shield ||
                role2 != MountedWeaponRole.Ranged ||
                role3 != MountedWeaponRole.Ammo)
            {
                return false;
            }

            return true;
        }

        private static MountedWeaponRole ResolveMountedWeaponRole(ItemObject item)
        {
            if (item == null)
                return MountedWeaponRole.Other;

            WeaponComponentData primaryWeapon = item.PrimaryWeapon;
            if (primaryWeapon != null)
            {
                if (primaryWeapon.IsShield)
                    return MountedWeaponRole.Shield;
                if (primaryWeapon.IsPolearm)
                    return MountedWeaponRole.Polearm;
                if (primaryWeapon.IsAmmo)
                    return MountedWeaponRole.Ammo;
                if (primaryWeapon.IsRangedWeapon ||
                    primaryWeapon.WeaponClass == WeaponClass.Javelin ||
                    primaryWeapon.WeaponClass == WeaponClass.ThrowingAxe ||
                    primaryWeapon.WeaponClass == WeaponClass.ThrowingKnife ||
                    primaryWeapon.WeaponClass == WeaponClass.Stone ||
                    primaryWeapon.WeaponClass == WeaponClass.SlingStone)
                {
                    return MountedWeaponRole.Ranged;
                }

                if (primaryWeapon.IsOneHanded || primaryWeapon.IsTwoHanded || primaryWeapon.IsMeleeWeapon)
                    return MountedWeaponRole.Melee;
            }

            switch (item.ItemType)
            {
                case ItemObject.ItemTypeEnum.Shield:
                    return MountedWeaponRole.Shield;
                case ItemObject.ItemTypeEnum.Polearm:
                    return MountedWeaponRole.Polearm;
                case ItemObject.ItemTypeEnum.Bow:
                case ItemObject.ItemTypeEnum.Crossbow:
                case ItemObject.ItemTypeEnum.Sling:
                case ItemObject.ItemTypeEnum.Thrown:
                    return MountedWeaponRole.Ranged;
                case ItemObject.ItemTypeEnum.Arrows:
                case ItemObject.ItemTypeEnum.Bolts:
                case ItemObject.ItemTypeEnum.SlingStones:
                    return MountedWeaponRole.Ammo;
                case ItemObject.ItemTypeEnum.OneHandedWeapon:
                case ItemObject.ItemTypeEnum.TwoHandedWeapon:
                    return MountedWeaponRole.Melee;
                default:
                    return MountedWeaponRole.Other;
            }
        }

        private static bool IsBowWeaponSlot(MountedWeaponSlotState slot)
        {
            return slot?.Item?.ItemType == ItemObject.ItemTypeEnum.Bow;
        }

        private static bool IsCrossbowWeaponSlot(MountedWeaponSlotState slot)
        {
            return slot?.Item?.ItemType == ItemObject.ItemTypeEnum.Crossbow;
        }

        private static bool IsArrowAmmoSlot(MountedWeaponSlotState slot)
        {
            return slot?.Item?.ItemType == ItemObject.ItemTypeEnum.Arrows;
        }

        private static bool IsBoltAmmoSlot(MountedWeaponSlotState slot)
        {
            return slot?.Item?.ItemType == ItemObject.ItemTypeEnum.Bolts;
        }

        private static bool IsSlingAmmoSlot(MountedWeaponSlotState slot)
        {
            return slot?.Item?.ItemType == ItemObject.ItemTypeEnum.SlingStones;
        }

        private static bool IsThrownWeaponSlot(MountedWeaponSlotState slot)
        {
            if (slot?.Item == null)
                return false;

            WeaponComponentData primaryWeapon = slot.Item.PrimaryWeapon;
            if (primaryWeapon != null)
            {
                return primaryWeapon.WeaponClass == WeaponClass.Javelin ||
                       primaryWeapon.WeaponClass == WeaponClass.ThrowingAxe ||
                       primaryWeapon.WeaponClass == WeaponClass.ThrowingKnife ||
                       primaryWeapon.WeaponClass == WeaponClass.Stone ||
                       primaryWeapon.WeaponClass == WeaponClass.SlingStone;
            }

            return slot.Item.ItemType == ItemObject.ItemTypeEnum.Thrown;
        }

        private static bool IsMeleeWeaponSlot(MountedWeaponSlotState slot)
        {
            return slot != null &&
                   (slot.Role == MountedWeaponRole.Melee || slot.Role == MountedWeaponRole.Polearm);
        }

        private static bool IsShieldCompatibleMeleeSlot(MountedWeaponSlotState slot)
        {
            if (!IsMeleeWeaponSlot(slot))
                return false;

            return CanPairShieldWithWeapon(slot);
        }

        private static bool CanPairShieldWithWeapon(MountedWeaponSlotState slot)
        {
            if (slot?.Item?.PrimaryWeapon == null)
                return false;

            int weaponFlags = (int)slot.Item.PrimaryWeapon.WeaponFlags;
            return (weaponFlags & (int)WeaponFlags.NotUsableWithOneHand) != (int)WeaponFlags.NotUsableWithOneHand;
        }

        private static Equipment.InitialWeaponEquipPreference ResolveInitialWeaponEquipPreferenceFromPreferredSlot(
            Equipment equipment,
            int? preferredMainHandSlotIndex)
        {
            EquipmentIndex preferredMainHandIndex = ToWeaponEquipmentIndex(preferredMainHandSlotIndex);
            if (preferredMainHandIndex == EquipmentIndex.None)
                return Equipment.InitialWeaponEquipPreference.Any;

            ItemObject preferredItem = equipment?[preferredMainHandIndex].Item;
            switch (ResolveMountedWeaponRole(preferredItem))
            {
                case MountedWeaponRole.Ranged:
                    return Equipment.InitialWeaponEquipPreference.RangedForMainHand;
                case MountedWeaponRole.Melee:
                case MountedWeaponRole.Polearm:
                    return Equipment.InitialWeaponEquipPreference.MeleeForMainHand;
                default:
                    return Equipment.InitialWeaponEquipPreference.Any;
            }
        }

        private static EquipmentIndex ToWeaponEquipmentIndex(int? slotIndex)
        {
            if (!slotIndex.HasValue)
                return EquipmentIndex.None;

            switch (slotIndex.Value)
            {
                case (int)EquipmentIndex.Weapon0:
                    return EquipmentIndex.Weapon0;
                case (int)EquipmentIndex.Weapon1:
                    return EquipmentIndex.Weapon1;
                case (int)EquipmentIndex.Weapon2:
                    return EquipmentIndex.Weapon2;
                case (int)EquipmentIndex.Weapon3:
                    return EquipmentIndex.Weapon3;
                default:
                    return EquipmentIndex.None;
            }
        }

        private static string GetWeaponSlotLabel(EquipmentIndex slot)
        {
            switch (slot)
            {
                case EquipmentIndex.Weapon0:
                    return "Item0";
                case EquipmentIndex.Weapon1:
                    return "Item1";
                case EquipmentIndex.Weapon2:
                    return "Item2";
                case EquipmentIndex.Weapon3:
                    return "Item3";
                default:
                    return slot.ToString();
            }
        }

        private static void AddSlot(
            ExactTransferEquipmentContract equipment,
            EquipmentIndex slot,
            string slotLabel,
            string itemId,
            bool mustExistAtCreateAgentTime,
            bool canBeLateSynchronized = false,
            bool isMountedCritical = false)
        {
            equipment.Slots.Add(new ExactTransferEquipmentSlotContract
            {
                Slot = slot,
                SlotLabel = slotLabel,
                ItemId = itemId,
                IsEmpty = string.IsNullOrWhiteSpace(itemId),
                MustExistAtCreateAgentTime = mustExistAtCreateAgentTime,
                CanBeLateSynchronized = canBeLateSynchronized,
                IsMountedCritical = isMountedCritical
            });
        }
    }
}
