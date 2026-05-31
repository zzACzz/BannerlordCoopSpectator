using System;
using System.Collections.Generic;
using System.Linq;

namespace CoopSpectator.Infrastructure
{
    public sealed class ExactTransferValidationResult
    {
        public ExactTransferValidationResult()
        {
            Errors = new List<string>();
            Warnings = new List<string>();
        }

        public List<string> Errors { get; private set; }
        public List<string> Warnings { get; private set; }
        public bool IsValid
        {
            get { return Errors.Count == 0; }
        }
    }

    internal static class ExactTransferContractValidator
    {
        public static ExactTransferValidationResult Validate(ExactTransferSpawnContract contract)
        {
            ExactTransferValidationResult result = new ExactTransferValidationResult();
            if (contract == null)
            {
                result.Errors.Add("contract is null");
                return result;
            }

            ValidateIdentity(contract, result);
            ValidateBody(contract, result);
            ValidateEquipment(contract, result);
            ValidateMount(contract, result);
            ValidatePeerPolicy(contract, result);
            ValidateInitialWield(contract, result);
            ValidatePreBattleWeaponState(contract, result);
            ValidateControl(contract, result);
            return result;
        }

        private static void ValidateIdentity(ExactTransferSpawnContract contract, ExactTransferValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(contract.EntryId))
                result.Errors.Add("entry id is missing");

            if (contract.Identity == null)
            {
                result.Errors.Add("identity contract is missing");
                return;
            }

            if (contract.SpawnPolicy != null &&
                contract.SpawnPolicy.UseStrictExactHeroPath &&
                string.IsNullOrWhiteSpace(contract.Identity.NativeMultiplayerCharacterId))
            {
                result.Errors.Add("strict hero path requires native multiplayer character id");
            }
        }

        private static void ValidateBody(ExactTransferSpawnContract contract, ExactTransferValidationResult result)
        {
            if (contract.Body == null)
            {
                result.Errors.Add("body contract is missing");
                return;
            }

            if (contract.SpawnPolicy != null &&
                contract.SpawnPolicy.UseStrictExactHeroPath &&
                !contract.Body.HasExactBodyProperties)
            {
                result.Errors.Add("strict hero path requires exact body properties");
            }

            if (contract.PeerBinding != null &&
                contract.PeerBinding.AllowPeerDrivenBodyAtCreateAgentTime &&
                contract.SpawnPolicy != null &&
                contract.SpawnPolicy.UseStrictExactHeroPath)
            {
                result.Warnings.Add("strict hero path currently allows peer-driven body at create time");
            }
        }

        private static void ValidateEquipment(ExactTransferSpawnContract contract, ExactTransferValidationResult result)
        {
            if (contract.Equipment == null)
            {
                result.Errors.Add("equipment contract is missing");
                return;
            }

            if (contract.Equipment.Slots == null || contract.Equipment.Slots.Count == 0)
            {
                result.Errors.Add("equipment slots are missing");
                return;
            }

            foreach (ExactTransferEquipmentSlotContract slot in contract.Equipment.Slots)
            {
                if (slot == null)
                    continue;

                if (slot.MustExistAtCreateAgentTime && slot.IsEmpty)
                {
                    result.Errors.Add("required pre-spawn slot is empty: " + (slot.SlotLabel ?? slot.Slot.ToString()));
                }
            }
        }

        private static void ValidateMount(ExactTransferSpawnContract contract, ExactTransferValidationResult result)
        {
            if (contract.Mount == null)
            {
                result.Errors.Add("mount contract is missing");
                return;
            }

            if (!contract.Mount.IsMounted)
                return;

            if (string.IsNullOrWhiteSpace(contract.Mount.HorseItemId))
                result.Errors.Add("mounted strict path requires horse item");

            if (contract.Equipment != null)
            {
                ExactTransferEquipmentSlotContract harnessSlot = contract.Equipment.Slots
                    .FirstOrDefault(slot => slot != null && string.Equals(slot.SlotLabel, "HorseHarness", StringComparison.Ordinal));
                if (harnessSlot != null &&
                    harnessSlot.MustExistAtCreateAgentTime &&
                    harnessSlot.IsEmpty)
                {
                    result.Errors.Add("mounted strict path requires horse harness item");
                }
            }
        }

        private static void ValidatePeerPolicy(ExactTransferSpawnContract contract, ExactTransferValidationResult result)
        {
            if (contract.PeerBinding == null)
            {
                result.Errors.Add("peer binding contract is missing");
                return;
            }

            if (contract.SpawnPolicy != null &&
                contract.SpawnPolicy.UseStrictExactHeroPath &&
                contract.PeerBinding.UsePlayerAgentCreateBranch &&
                !contract.PeerBinding.AllowPeerDrivenBodyAtCreateAgentTime)
            {
                result.Errors.Add("strict hero path cannot use player-agent create branch without explicit peer-driven body policy");
            }

            if (contract.SpawnPolicy != null &&
                contract.SpawnPolicy.UseStrictExactHeroPath &&
                contract.PeerBinding.AllowPeerDrivenBannerAtCreateAgentTime)
            {
                result.Warnings.Add("strict hero path currently allows peer-driven banner at create time");
            }
        }

        private static void ValidateInitialWield(ExactTransferSpawnContract contract, ExactTransferValidationResult result)
        {
            if (contract.InitialWield == null)
            {
                result.Errors.Add("initial wield contract is missing");
                return;
            }

            if (contract.InitialWield.HasWeapon2Risk &&
                contract.SpawnPolicy != null &&
                contract.SpawnPolicy.UseStrictExactHeroPath)
            {
                result.Errors.Add("strict hero path still has live weapon2 risk");
            }
        }

        private static void ValidatePreBattleWeaponState(ExactTransferSpawnContract contract, ExactTransferValidationResult result)
        {
            if (contract.PreBattleWeaponState == null)
            {
                result.Errors.Add("pre-battle weapon state contract is missing");
                return;
            }

            ExactTransferPreBattleWeaponStateContract preBattleWeaponState = contract.PreBattleWeaponState;
            if (preBattleWeaponState.Mode == ExactTransferPreBattleWeaponStateMode.None)
                return;

            if (!IsValidWeaponSlotIndex(preBattleWeaponState.PreferredMainHandSlotIndex) &&
                !IsValidWeaponSlotIndex(preBattleWeaponState.PreferredOffHandSlotIndex))
            {
                result.Errors.Add("pre-battle weapon state does not define a valid main-hand or off-hand slot");
            }

            if (preBattleWeaponState.PreferredMainHandSlotIndex.HasValue &&
                !IsValidWeaponSlotIndex(preBattleWeaponState.PreferredMainHandSlotIndex))
            {
                result.Errors.Add("pre-battle weapon state has invalid preferred main-hand slot");
            }

            if (preBattleWeaponState.PreferredOffHandSlotIndex.HasValue &&
                !IsValidWeaponSlotIndex(preBattleWeaponState.PreferredOffHandSlotIndex))
            {
                result.Errors.Add("pre-battle weapon state has invalid preferred off-hand slot");
            }

            if (preBattleWeaponState.ExpectAmmoAttachedToMainHand &&
                !IsValidWeaponSlotIndex(preBattleWeaponState.ExpectedAmmoSlotIndex))
            {
                result.Errors.Add("pre-battle weapon state expects attached ammo but ammo slot is invalid");
            }
        }

        private static void ValidateControl(ExactTransferSpawnContract contract, ExactTransferValidationResult result)
        {
            if (contract.Control == null)
            {
                result.Errors.Add("control contract is missing");
                return;
            }

            if (contract.Control.IsCommanderEntry &&
                !contract.Control.EnableCommanderControlOnlyAfterExactReady)
            {
                result.Errors.Add("commander entry must be gated on exact-ready stage");
            }
        }

        private static bool IsValidWeaponSlotIndex(int? slotIndex)
        {
            if (!slotIndex.HasValue)
                return false;

            return slotIndex.Value == (int)TaleWorlds.Core.EquipmentIndex.Weapon0 ||
                   slotIndex.Value == (int)TaleWorlds.Core.EquipmentIndex.Weapon1 ||
                   slotIndex.Value == (int)TaleWorlds.Core.EquipmentIndex.Weapon2 ||
                   slotIndex.Value == (int)TaleWorlds.Core.EquipmentIndex.Weapon3;
        }
    }
}
