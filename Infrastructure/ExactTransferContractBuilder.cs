using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using CoopSpectator.MissionBehaviors;
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

        private sealed class MountedWeaponSlotState
        {
            public EquipmentIndex Slot { get; set; }
            public string SlotLabel { get; set; }
            public string ItemId { get; set; }
            public ItemObject Item { get; set; }
            public MountedWeaponRole Role { get; set; }
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
            PopulateIdentity(contract.Identity, entryState, isPlayerControlledOrigin);
            PopulateBody(contract.Body, entryState);
            PopulateEquipment(contract.Equipment, entryState, isStrictHeroEntry, isRuntimeExactSupported, buildMode);
            PopulateMount(contract.Mount, entryState);
            PopulatePeerBinding(contract.PeerBinding, entryState, isPlayerControlledOrigin);
            PopulateInitialWield(contract.InitialWield, entryState, contract.Equipment);
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
            BuildMode buildMode)
        {
            equipment.SpawnEquipment = CoopMissionSpawnLogic.BuildSnapshotEquipmentForExactRuntime(
                entryState,
                includeWeapons: true,
                honorExactVisualContracts: false,
                includeArmorVisuals: true,
                includeMountVisuals: true);
            bool exactPreSpawnWeaponCandidate = isStrictHeroEntry || isRuntimeExactSupported;
            equipment.IncludeWeaponsInPreSpawn = buildMode == BuildMode.Diagnostic
                ? exactPreSpawnWeaponCandidate && HasAnyWeaponItem(entryState)
                : isRuntimeExactSupported && HasAnyWeaponItem(entryState);
            equipment.IncludeArmorVisualsInPreSpawn = buildMode == BuildMode.Diagnostic || isRuntimeExactSupported;
            equipment.IncludeCapeInPreSpawn = buildMode == BuildMode.Diagnostic
                ? exactPreSpawnWeaponCandidate
                : CoopMissionSpawnLogic.EvaluateExactRuntimeCapeVisualContract(entryState, out _, out _);
            equipment.IncludeMountVisualsInPreSpawn =
                entryState.IsMounted && (buildMode == BuildMode.Diagnostic || isRuntimeExactSupported);

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

            List<MountedWeaponSlotState> orderedSlots = null;
            if (hasRanged && hasAmmo && hasUnsafeRangedWeapon2Layout)
                orderedSlots = BuildCanonicalStrictHeroRangedWeaponLayout(slots);
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

        private static List<MountedWeaponSlotState> BuildCanonicalStrictHeroRangedWeaponLayout(
            List<MountedWeaponSlotState> sourceSlots)
        {
            var remaining = new List<MountedWeaponSlotState>(sourceSlots ?? Enumerable.Empty<MountedWeaponSlotState>());
            var ordered = new List<MountedWeaponSlotState>(4);

            MountedWeaponSlotState slot0 = remaining.FirstOrDefault(slot => slot?.Slot == EquipmentIndex.Weapon0);
            MountedWeaponRole slot0Role = slot0?.Role ?? MountedWeaponRole.Other;
            bool rangedLeadLayout = slot0Role == MountedWeaponRole.Ranged || slot0Role == MountedWeaponRole.Ammo;

            if (rangedLeadLayout)
            {
                AddFirstByRole(ordered, remaining, MountedWeaponRole.Ranged);
                AddFirstByRole(ordered, remaining, MountedWeaponRole.Ammo);
                AddFirstByRole(ordered, remaining, MountedWeaponRole.Ammo);
                AddNextPreferred(ordered, remaining, preferLiveCandidate: true);
            }
            else
            {
                AddPrimaryLeadSlot(ordered, remaining, slot0);
                AddFirstByRole(ordered, remaining, MountedWeaponRole.Ranged);
                AddFirstByRole(ordered, remaining, MountedWeaponRole.Ammo);
            }

            while (ordered.Count < 4 && remaining.Count > 0)
            {
                ordered.Add(remaining[0]);
                remaining.RemoveAt(0);
            }

            return ordered.Take(4).ToList();
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

            MountedWeaponSlotState primaryMelee = TakeFirst(remaining, MountedWeaponRole.Polearm)
                                                  ?? TakeFirst(remaining, MountedWeaponRole.Melee);
            MountedWeaponSlotState primaryRanged = TakeFirst(remaining, MountedWeaponRole.Ranged);
            MountedWeaponSlotState shield = TakeFirst(remaining, MountedWeaponRole.Shield);
            MountedWeaponSlotState ammo = TakeFirst(remaining, MountedWeaponRole.Ammo);

            if (primaryMelee != null)
                ordered.Add(primaryMelee);
            else if (primaryRanged != null)
            {
                ordered.Add(primaryRanged);
                primaryRanged = null;
            }

            if (hasAmmo)
            {
                if (shield != null)
                {
                    ordered.Add(shield);
                    shield = null;
                }
                else if (primaryRanged != null)
                {
                    ordered.Add(primaryRanged);
                    primaryRanged = null;
                }
                else
                {
                    AddNextPreferred(ordered, remaining, preferLiveCandidate: true);
                }

                if (ammo != null)
                {
                    ordered.Add(ammo);
                    ammo = null;
                }
                else
                {
                    AddNextPreferred(ordered, remaining, preferLiveCandidate: false);
                }
            }
            else
            {
                if (primaryRanged != null)
                {
                    ordered.Add(primaryRanged);
                    primaryRanged = null;
                }
                else if (shield != null)
                {
                    ordered.Add(shield);
                    shield = null;
                }
                else
                {
                    AddNextPreferred(ordered, remaining, preferLiveCandidate: true);
                }

                if (shield != null)
                {
                    ordered.Add(shield);
                    shield = null;
                }
                else if (ammo != null)
                {
                    ordered.Add(ammo);
                    ammo = null;
                }
                else
                {
                    AddNextPreferred(ordered, remaining, preferLiveCandidate: false);
                }
            }

            if (primaryRanged != null)
                ordered.Add(primaryRanged);
            if (shield != null)
                ordered.Add(shield);
            if (ammo != null)
                ordered.Add(ammo);

            while (ordered.Count < 4 && remaining.Count > 0)
            {
                ordered.Add(remaining[0]);
                remaining.RemoveAt(0);
            }

            return ordered.Take(4).ToList();
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

        private static void AddFirstByRole(
            List<MountedWeaponSlotState> ordered,
            List<MountedWeaponSlotState> remaining,
            MountedWeaponRole role)
        {
            MountedWeaponSlotState match = TakeFirst(remaining, role);
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

        private static bool DoesEquipmentContainUnsafeRangedWeapon2Layout(Equipment equipment)
        {
            if (equipment == null)
                return false;

            MountedWeaponRole role0 = ResolveMountedWeaponRole(equipment[EquipmentIndex.Weapon0].Item);
            MountedWeaponRole role1 = ResolveMountedWeaponRole(equipment[EquipmentIndex.Weapon1].Item);
            MountedWeaponRole role2 = ResolveMountedWeaponRole(equipment[EquipmentIndex.Weapon2].Item);
            MountedWeaponRole role3 = ResolveMountedWeaponRole(equipment[EquipmentIndex.Weapon3].Item);

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

            bool leadingLiveSlot =
                role0 == MountedWeaponRole.Melee ||
                role0 == MountedWeaponRole.Polearm ||
                role0 == MountedWeaponRole.Shield ||
                role0 == MountedWeaponRole.Other;
            if (leadingLiveSlot && (role1 != MountedWeaponRole.Ranged || role2 != MountedWeaponRole.Ammo))
                return true;

            return role2 == MountedWeaponRole.Ranged &&
                   (role1 == MountedWeaponRole.Ammo || role3 == MountedWeaponRole.Ammo);
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
