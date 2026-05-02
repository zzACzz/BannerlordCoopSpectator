using System;
using TaleWorlds.Core;
using CoopSpectator.MissionBehaviors;

namespace CoopSpectator.Infrastructure
{
    internal static class ExactTransferContractBuilder
    {
        public static ExactTransferSpawnContract Build(
            RosterEntryState entryState,
            bool isPlayerControlledOrigin,
            int teamIndex,
            int formationIndex)
        {
            if (entryState == null)
                return null;

            ExactTransferSpawnContract contract = new ExactTransferSpawnContract
            {
                EntryId = entryState.EntryId
            };

            PopulateIdentity(contract.Identity, entryState, isPlayerControlledOrigin);
            PopulateBody(contract.Body, entryState);
            PopulateEquipment(contract.Equipment, entryState);
            PopulateMount(contract.Mount, entryState);
            PopulatePeerBinding(contract.PeerBinding, entryState, isPlayerControlledOrigin);
            PopulateInitialWield(contract.InitialWield, entryState, contract.Equipment);
            PopulateControl(contract.Control, entryState, teamIndex, formationIndex);
            PopulateCleanup(contract.Cleanup);
            PopulateSpawnPolicy(contract.SpawnPolicy, entryState);

            return contract;
        }

        private static void PopulateIdentity(
            ExactTransferIdentityContract identity,
            RosterEntryState entryState,
            bool isPlayerControlledOrigin)
        {
            identity.CampaignCharacterId = entryState.CharacterId;
            identity.CampaignHeroStringId = entryState.HeroId;
            identity.NativeMultiplayerCharacterId =
                BattleSnapshotRuntimeState.TryResolveCharacterObject(entryState.EntryId)?.StringId ??
                entryState.CharacterId;
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

        private static void PopulateEquipment(ExactTransferEquipmentContract equipment, RosterEntryState entryState)
        {
            equipment.SpawnEquipment = CoopMissionSpawnLogic.BuildSnapshotEquipmentForExactRuntime(
                entryState,
                includeWeapons: true,
                honorExactVisualContracts: false,
                includeArmorVisuals: true,
                includeMountVisuals: true);
            equipment.IncludeWeaponsInPreSpawn =
                CoopMissionSpawnLogic.EvaluateExactRuntimePreSpawnWeaponInjectionContract(entryState, out _, out _);
            equipment.IncludeArmorVisualsInPreSpawn = true;
            equipment.IncludeCapeInPreSpawn =
                CoopMissionSpawnLogic.EvaluateExactRuntimeCapeVisualContract(entryState, out _, out _);
            equipment.IncludeMountVisualsInPreSpawn = entryState.IsMounted;

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
                initialWield.HasWeapon2Risk = entryState.IsMounted &&
                                              mainHandWeaponIndex == EquipmentIndex.Weapon2;
            }
            catch
            {
                initialWield.HasWeapon2Risk = entryState.IsMounted &&
                                              !string.IsNullOrWhiteSpace(entryState.CombatItem2Id);
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

        private static void PopulateSpawnPolicy(ExactTransferSpawnPolicyContract spawnPolicy, RosterEntryState entryState)
        {
            bool isStrictHero = entryState.IsHero ||
                                !string.IsNullOrWhiteSpace(entryState.HeroId) ||
                                string.Equals(entryState.OriginalCharacterId, "main_hero", StringComparison.OrdinalIgnoreCase);
            spawnPolicy.UseStrictExactHeroPath = isStrictHero;
            spawnPolicy.RequirePreSpawnInjection = isStrictHero;
            spawnPolicy.AllowClientVisualOverlayAsRecoveryOnly = true;
            spawnPolicy.ForbidSurrogatePrimaryMaterialization = true;
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
