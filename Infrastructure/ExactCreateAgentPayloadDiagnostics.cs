using System;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Infrastructure
{
    internal enum ExactCreateAgentPayloadDiagnosticMode
    {
        Disabled = 0,
        SingleProfile = 1,
        SweepByEntryHash = 2
    }

    internal enum ExactCreateAgentPayloadDiagnosticProfile
    {
        NativeTemplateOnly = 0,
        WeaponsOnly = 1,
        ArmorOnly = 2,
        WeaponsAndArmor = 3,
        WeaponsArmorCape = 4,
        WeaponsArmorMount = 5,
        FullExact = 6
    }

    internal enum ExactCreateAgentPayloadArchetype
    {
        FootMelee = 0,
        FootRanged = 1,
        MountedMelee = 2,
        MountedRanged = 3
    }

    internal sealed class ExactCreateAgentPayloadDiagnosticDecision
    {
        public bool IsActive { get; set; }
        public string Reason { get; set; }
        public string EntryId { get; set; }
        public string TroopId { get; set; }
        public ExactCreateAgentPayloadDiagnosticMode Mode { get; set; }
        public ExactCreateAgentPayloadDiagnosticProfile RequestedProfile { get; set; }
        public ExactCreateAgentPayloadDiagnosticProfile Profile { get; set; }
        public ExactCreateAgentPayloadArchetype Archetype { get; set; }
        public string ArchetypeEvidence { get; set; }
        public bool RequestedProfileClientSafe { get; set; }
        public bool ClientCreateAgentSafe { get; set; }
        public string ClientCreateAgentSafeReason { get; set; }
        public bool RequiresCreateTimeWeapons { get; set; }
        public bool WeaponLayoutMatchesNativeTemplate { get; set; }
        public string ExactWeaponLayoutSummary { get; set; }
        public string NativeTemplateWeaponLayoutSummary { get; set; }
        public bool IncludeWeapons { get; set; }
        public bool IncludeArmorVisuals { get; set; }
        public bool IncludeCape { get; set; }
        public bool IncludeMountVisuals { get; set; }
        public bool IncludeBodyProperties { get; set; }

        public string ToSummary()
        {
            return
                "ExactCreateAgentPayloadDiagnostic={Active=" + IsActive +
                ",Reason=" + (Reason ?? "unknown") +
                ",Mode=" + Mode +
                ",RequestedProfile=" + RequestedProfile +
                ",Profile=" + Profile +
                ",Archetype=" + Archetype +
                ",ArchetypeEvidence=" + (ArchetypeEvidence ?? "unknown") +
                ",RequestedProfileClientSafe=" + RequestedProfileClientSafe +
                ",ClientCreateAgentSafe=" + ClientCreateAgentSafe +
                ",ClientCreateAgentSafeReason=" + (ClientCreateAgentSafeReason ?? "unknown") +
                ",RequiresCreateTimeWeapons=" + RequiresCreateTimeWeapons +
                ",WeaponLayoutMatchesNativeTemplate=" + WeaponLayoutMatchesNativeTemplate +
                ",Weapons=" + IncludeWeapons +
                ",Armor=" + IncludeArmorVisuals +
                ",Cape=" + IncludeCape +
                ",Mount=" + IncludeMountVisuals +
                ",Body=" + IncludeBodyProperties + "}";
        }

        public string ToWeaponLayoutSummary()
        {
            return
                "ExactCreateAgentWeaponLayout={Exact={" + (ExactWeaponLayoutSummary ?? "(none)") +
                "},NativeTemplate={" + (NativeTemplateWeaponLayoutSummary ?? "(none)") +
                "},RequiresCreateTimeWeapons=" + RequiresCreateTimeWeapons +
                ",WeaponLayoutMatchesNativeTemplate=" + WeaponLayoutMatchesNativeTemplate + "}";
        }
    }

    internal static class ExactCreateAgentPayloadDiagnostics
    {
        private static readonly ExactCreateAgentPayloadDiagnosticProfile[] FootProfiles =
        {
            ExactCreateAgentPayloadDiagnosticProfile.NativeTemplateOnly,
            ExactCreateAgentPayloadDiagnosticProfile.WeaponsOnly,
            ExactCreateAgentPayloadDiagnosticProfile.ArmorOnly,
            ExactCreateAgentPayloadDiagnosticProfile.WeaponsAndArmor,
            ExactCreateAgentPayloadDiagnosticProfile.WeaponsArmorCape,
            ExactCreateAgentPayloadDiagnosticProfile.FullExact
        };

        private static readonly ExactCreateAgentPayloadDiagnosticProfile[] MountedProfiles =
        {
            ExactCreateAgentPayloadDiagnosticProfile.NativeTemplateOnly,
            ExactCreateAgentPayloadDiagnosticProfile.WeaponsOnly,
            ExactCreateAgentPayloadDiagnosticProfile.ArmorOnly,
            ExactCreateAgentPayloadDiagnosticProfile.WeaponsAndArmor,
            ExactCreateAgentPayloadDiagnosticProfile.WeaponsArmorMount,
            ExactCreateAgentPayloadDiagnosticProfile.FullExact
        };

        private static readonly bool DiagnosticsEnabled = true;
        private static readonly ExactCreateAgentPayloadDiagnosticMode ActiveMode =
            ExactCreateAgentPayloadDiagnosticMode.SweepByEntryHash;
        private static readonly ExactCreateAgentPayloadDiagnosticProfile SingleProfile =
            ExactCreateAgentPayloadDiagnosticProfile.FullExact;

        internal static ExactCreateAgentPayloadDiagnosticDecision Resolve(
            RosterEntryState entryState,
            ExactTransferSpawnContract contract,
            bool useContractDrivenPreSpawnPath,
            bool includeWeapons,
            bool includeArmorVisuals,
            bool includeCape,
            bool includeMountVisuals,
            bool includeBodyProperties)
        {
            ExactCreateAgentPayloadArchetype archetype = ResolveArchetype(entryState, contract);
            string archetypeEvidence = BuildArchetypeEvidence(contract?.Equipment?.SpawnEquipment);
            if (!DiagnosticsEnabled)
                return BuildDisabledDecision(entryState, archetype, archetypeEvidence, "feature-disabled");

            if (ActiveMode == ExactCreateAgentPayloadDiagnosticMode.Disabled)
                return BuildDisabledDecision(entryState, archetype, archetypeEvidence, "mode-disabled");

            if (!useContractDrivenPreSpawnPath)
                return BuildDisabledDecision(entryState, archetype, archetypeEvidence, "non-contract-path");

            if (entryState == null || contract == null)
                return BuildDisabledDecision(entryState, archetype, archetypeEvidence, "missing-state");

            if (IsHeroOrLordExactPath(contract))
                return BuildDisabledDecision(entryState, archetype, archetypeEvidence, "hero-or-lord-path");

            ExactCreateAgentPayloadDiagnosticProfile requestedProfile = ActiveMode == ExactCreateAgentPayloadDiagnosticMode.SingleProfile
                ? SingleProfile
                : SelectSweepProfile(entryState?.EntryId, archetype);
            bool requiresCreateTimeWeapons = RequiresCreateTimeWeapons(entryState, contract);
            bool requestedProfileIncludesWeapons = ProfileIncludesWeapons(requestedProfile);
            Equipment exactEquipment = contract?.Equipment?.SpawnEquipment;
            Equipment nativeTemplateEquipment = ResolveNativeTemplateEquipment(entryState);
            bool weaponLayoutMatchesNativeTemplate =
                exactEquipment != null &&
                nativeTemplateEquipment != null &&
                DoWeaponLayoutsMatch(exactEquipment, nativeTemplateEquipment);
            bool requiresCreateTimeMount = RequiresCreateTimeMount(entryState, contract);
            bool requestedProfileIncludesMount = ProfileIncludesMount(requestedProfile);
            ExactCreateAgentPayloadDiagnosticProfile effectiveProfile = requestedProfile;
            var promotionReasons = new List<string>();

            if (requiresCreateTimeMount && !requestedProfileIncludesMount)
            {
                effectiveProfile = PromoteProfileForMountSafety(effectiveProfile);
                promotionReasons.Add("mounted-entry-requires-create-time-mount");
            }

            bool requestedProfileClientSafe =
                (requestedProfileIncludesWeapons || !requiresCreateTimeWeapons) &&
                (requestedProfileIncludesMount || !requiresCreateTimeMount);
            if (requiresCreateTimeWeapons && !ProfileIncludesWeapons(effectiveProfile))
            {
                effectiveProfile = PromoteProfileForWeaponSafety(effectiveProfile);
                promotionReasons.Add("native-template-weapon-layout-mismatch");
            }

            string clientCreateAgentSafeReason;
            if (promotionReasons.Count > 0)
            {
                clientCreateAgentSafeReason =
                    "promoted-to-" +
                    effectiveProfile +
                    "-because-" +
                    string.Join("-and-", promotionReasons);
            }
            else
            {
                clientCreateAgentSafeReason = requestedProfileIncludesWeapons
                    ? "create-agent-weapons-present"
                    : "native-template-weapon-layout-match";
            }

            return new ExactCreateAgentPayloadDiagnosticDecision
            {
                IsActive = true,
                Reason = "ordinary-ai-contract-driven",
                EntryId = entryState.EntryId,
                TroopId = entryState.CharacterId ?? entryState.OriginalCharacterId ?? entryState.SpawnTemplateId,
                Mode = ActiveMode,
                RequestedProfile = requestedProfile,
                Profile = effectiveProfile,
                Archetype = archetype,
                ArchetypeEvidence = archetypeEvidence,
                RequestedProfileClientSafe = requestedProfileClientSafe,
                ClientCreateAgentSafe =
                    (ProfileIncludesWeapons(effectiveProfile) || !requiresCreateTimeWeapons) &&
                    (ProfileIncludesMount(effectiveProfile) || !requiresCreateTimeMount),
                ClientCreateAgentSafeReason = clientCreateAgentSafeReason,
                RequiresCreateTimeWeapons = requiresCreateTimeWeapons,
                WeaponLayoutMatchesNativeTemplate = weaponLayoutMatchesNativeTemplate,
                ExactWeaponLayoutSummary = BuildEquipmentWeaponLayoutSummary(exactEquipment),
                NativeTemplateWeaponLayoutSummary = BuildEquipmentWeaponLayoutSummary(nativeTemplateEquipment),
                IncludeWeapons = includeWeapons && ProfileIncludesWeapons(effectiveProfile),
                IncludeArmorVisuals = includeArmorVisuals && ProfileIncludesArmor(effectiveProfile),
                IncludeCape = includeCape && ProfileIncludesCape(effectiveProfile),
                IncludeMountVisuals = includeMountVisuals && ProfileIncludesMount(effectiveProfile),
                IncludeBodyProperties = includeBodyProperties && ProfileIncludesBody(effectiveProfile)
            };
        }

        private static ExactCreateAgentPayloadDiagnosticDecision BuildDisabledDecision(
            RosterEntryState entryState,
            ExactCreateAgentPayloadArchetype archetype,
            string archetypeEvidence,
            string reason)
        {
            return new ExactCreateAgentPayloadDiagnosticDecision
            {
                IsActive = false,
                Reason = reason ?? "disabled",
                EntryId = entryState?.EntryId,
                TroopId = entryState?.CharacterId ?? entryState?.OriginalCharacterId ?? entryState?.SpawnTemplateId,
                Mode = ActiveMode,
                RequestedProfile = ExactCreateAgentPayloadDiagnosticProfile.FullExact,
                Profile = ExactCreateAgentPayloadDiagnosticProfile.FullExact,
                Archetype = archetype,
                ArchetypeEvidence = archetypeEvidence,
                RequestedProfileClientSafe = false,
                ClientCreateAgentSafe = false,
                ClientCreateAgentSafeReason = reason ?? "disabled"
            };
        }

        private static bool IsHeroOrLordExactPath(ExactTransferSpawnContract contract)
        {
            if (contract == null)
                return false;

            if (contract.SpawnPolicy?.UseStrictExactHeroPath == true)
                return true;

            ExactTransferIdentityContract identity = contract.Identity;
            return identity != null &&
                   (identity.IsHero || identity.IsLord || identity.IsCompanion || identity.IsMainHero);
        }

        private static ExactCreateAgentPayloadDiagnosticProfile SelectSweepProfile(
            string entryId,
            ExactCreateAgentPayloadArchetype archetype)
        {
            ExactCreateAgentPayloadDiagnosticProfile[] profiles =
                archetype == ExactCreateAgentPayloadArchetype.MountedMelee ||
                archetype == ExactCreateAgentPayloadArchetype.MountedRanged
                    ? MountedProfiles
                    : FootProfiles;
            if (profiles.Length == 0)
                return ExactCreateAgentPayloadDiagnosticProfile.FullExact;

            int stableHash = ComputeStableHash(entryId ?? "missing-entry-id");
            int index = (int)((uint)stableHash % (uint)profiles.Length);
            return profiles[index];
        }

        private static int ComputeStableHash(string value)
        {
            unchecked
            {
                int hash = 23;
                for (int i = 0; i < value.Length; i++)
                    hash = (hash * 31) + value[i];
                return hash;
            }
        }

        private static bool RequiresCreateTimeWeapons(
            RosterEntryState entryState,
            ExactTransferSpawnContract contract)
        {
            Equipment exactEquipment = contract?.Equipment?.SpawnEquipment;
            if (!HasAnyWeapon(exactEquipment))
                return false;

            Equipment nativeTemplateEquipment = ResolveNativeTemplateEquipment(entryState);
            if (nativeTemplateEquipment == null)
                return true;

            return !DoWeaponLayoutsMatch(exactEquipment, nativeTemplateEquipment);
        }

        private static bool RequiresCreateTimeMount(
            RosterEntryState entryState,
            ExactTransferSpawnContract contract)
        {
            if (entryState?.IsMounted != true && contract?.Mount?.IsMounted != true)
                return false;

            Equipment exactEquipment = contract?.Equipment?.SpawnEquipment;
            string exactHorseId = exactEquipment?[EquipmentIndex.Horse].Item?.StringId;
            string exactHarnessId = exactEquipment?[EquipmentIndex.HorseHarness].Item?.StringId;
            if (string.IsNullOrWhiteSpace(exactHorseId) &&
                string.IsNullOrWhiteSpace(exactHarnessId) &&
                string.IsNullOrWhiteSpace(contract?.Mount?.HorseItemId) &&
                string.IsNullOrWhiteSpace(contract?.Mount?.HarnessItemId))
            {
                return false;
            }

            return true;
        }

        private static ExactCreateAgentPayloadDiagnosticProfile PromoteProfileForWeaponSafety(
            ExactCreateAgentPayloadDiagnosticProfile profile)
        {
            switch (profile)
            {
                case ExactCreateAgentPayloadDiagnosticProfile.NativeTemplateOnly:
                    return ExactCreateAgentPayloadDiagnosticProfile.WeaponsOnly;
                case ExactCreateAgentPayloadDiagnosticProfile.ArmorOnly:
                    return ExactCreateAgentPayloadDiagnosticProfile.WeaponsAndArmor;
                default:
                    return profile;
            }
        }

        private static ExactCreateAgentPayloadDiagnosticProfile PromoteProfileForMountSafety(
            ExactCreateAgentPayloadDiagnosticProfile profile)
        {
            switch (profile)
            {
                case ExactCreateAgentPayloadDiagnosticProfile.FullExact:
                case ExactCreateAgentPayloadDiagnosticProfile.WeaponsArmorMount:
                    return profile;
                default:
                    return ExactCreateAgentPayloadDiagnosticProfile.WeaponsArmorMount;
            }
        }

        private static ExactCreateAgentPayloadArchetype ResolveArchetype(
            RosterEntryState entryState,
            ExactTransferSpawnContract contract)
        {
            bool isMounted = entryState?.IsMounted == true || contract?.Mount?.IsMounted == true;
            bool isRanged = DoesEquipmentRepresentRangedLoadout(contract?.Equipment?.SpawnEquipment);
            if (isMounted)
                return isRanged
                    ? ExactCreateAgentPayloadArchetype.MountedRanged
                    : ExactCreateAgentPayloadArchetype.MountedMelee;

            return isRanged
                ? ExactCreateAgentPayloadArchetype.FootRanged
                : ExactCreateAgentPayloadArchetype.FootMelee;
        }

        private static string BuildArchetypeEvidence(Equipment equipment)
        {
            if (equipment == null)
                return "equipment-null";

            var evidence = new List<string>();
            for (EquipmentIndex slot = EquipmentIndex.Weapon0; slot <= EquipmentIndex.Weapon3; slot++)
            {
                ItemObject item = equipment[slot].Item;
                if (item == null)
                    continue;

                evidence.Add(slot + "=" + item.StringId + ":" + item.ItemType);
            }

            return evidence.Count > 0
                ? string.Join("|", evidence)
                : "no-weapon-items";
        }

        private static bool DoesEquipmentRepresentRangedLoadout(Equipment equipment)
        {
            if (equipment == null)
                return false;

            for (EquipmentIndex slot = EquipmentIndex.Weapon0; slot <= EquipmentIndex.Weapon3; slot++)
            {
                ItemObject item = equipment[slot].Item;
                if (item == null)
                    continue;

                switch (item.ItemType)
                {
                    case ItemObject.ItemTypeEnum.Bow:
                    case ItemObject.ItemTypeEnum.Crossbow:
                    case ItemObject.ItemTypeEnum.Thrown:
                    case ItemObject.ItemTypeEnum.Arrows:
                    case ItemObject.ItemTypeEnum.Bolts:
                        return true;
                }
            }

            return false;
        }

        private static bool HasAnyWeapon(Equipment equipment)
        {
            if (equipment == null)
                return false;

            for (EquipmentIndex slot = EquipmentIndex.Weapon0; slot <= EquipmentIndex.Weapon3; slot++)
            {
                if (equipment[slot].Item != null)
                    return true;
            }

            return false;
        }

        internal static Equipment ResolveNativeTemplateEquipment(RosterEntryState entryState)
        {
            if (entryState == null)
                return null;

            BasicCharacterObject runtimeCharacter = null;
            if (!string.IsNullOrWhiteSpace(entryState.EntryId))
                runtimeCharacter = BattleSnapshotRuntimeState.TryResolveCharacterObject(entryState.EntryId);
            if (runtimeCharacter?.Equipment != null)
                return runtimeCharacter.Equipment;

            string[] candidateIds =
            {
                entryState.SpawnTemplateId,
                entryState.OriginalCharacterId,
                entryState.CharacterId,
                entryState.HeroTemplateId
            };

            for (int i = 0; i < candidateIds.Length; i++)
            {
                string candidateId = candidateIds[i];
                if (string.IsNullOrWhiteSpace(candidateId))
                    continue;

                try
                {
                    BasicCharacterObject candidate = MBObjectManager.Instance.GetObject<BasicCharacterObject>(candidateId);
                    if (candidate?.Equipment != null)
                        return candidate.Equipment;
                }
                catch
                {
                }
            }

            return null;
        }

        internal static bool DoWeaponLayoutsMatch(Equipment exactEquipment, Equipment nativeTemplateEquipment)
        {
            if (exactEquipment == null || nativeTemplateEquipment == null)
                return false;

            for (EquipmentIndex slot = EquipmentIndex.Weapon0; slot <= EquipmentIndex.Weapon3; slot++)
            {
                string exactItemId = exactEquipment[slot].Item?.StringId ?? string.Empty;
                string nativeItemId = nativeTemplateEquipment[slot].Item?.StringId ?? string.Empty;
                if (!string.Equals(exactItemId, nativeItemId, StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        internal static string BuildEntryWeaponLayoutSummary(RosterEntryState entryState)
        {
            if (entryState == null)
                return "(none)";

            var parts = new List<string>();
            AppendWeaponSlotSummary(parts, EquipmentIndex.Weapon0, entryState.CombatItem0Id, entryState.CombatItem0Amount);
            AppendWeaponSlotSummary(parts, EquipmentIndex.Weapon1, entryState.CombatItem1Id, entryState.CombatItem1Amount);
            AppendWeaponSlotSummary(parts, EquipmentIndex.Weapon2, entryState.CombatItem2Id, entryState.CombatItem2Amount);
            AppendWeaponSlotSummary(parts, EquipmentIndex.Weapon3, entryState.CombatItem3Id, entryState.CombatItem3Amount);
            return parts.Count > 0 ? string.Join(", ", parts) : "(empty)";
        }

        internal static string BuildEntryMountLayoutSummary(RosterEntryState entryState)
        {
            if (entryState == null)
                return "(none)";

            var parts = new List<string>();
            AppendWeaponSlotSummary(parts, EquipmentIndex.Horse, entryState.CombatHorseId, null);
            AppendWeaponSlotSummary(parts, EquipmentIndex.HorseHarness, entryState.CombatHorseHarnessId, null);
            return parts.Count > 0 ? string.Join(", ", parts) : "(empty)";
        }

        internal static string BuildEquipmentWeaponLayoutSummary(Equipment equipment)
        {
            if (equipment == null)
                return "(none)";

            var parts = new List<string>();
            for (EquipmentIndex slot = EquipmentIndex.Weapon0; slot <= EquipmentIndex.Weapon3; slot++)
            {
                EquipmentElement element = equipment[slot];
                AppendWeaponSlotSummary(
                    parts,
                    slot,
                    element.Item?.StringId,
                    TryGetEquipmentElementAmount(element));
            }

            return parts.Count > 0 ? string.Join(", ", parts) : "(empty)";
        }

        internal static string BuildEquipmentMountLayoutSummary(Equipment equipment)
        {
            if (equipment == null)
                return "(none)";

            var parts = new List<string>();
            AppendWeaponSlotSummary(parts, EquipmentIndex.Horse, equipment[EquipmentIndex.Horse].Item?.StringId, null);
            AppendWeaponSlotSummary(parts, EquipmentIndex.HorseHarness, equipment[EquipmentIndex.HorseHarness].Item?.StringId, null);
            return parts.Count > 0 ? string.Join(", ", parts) : "(empty)";
        }

        internal static string BuildMissionEquipmentWeaponLayoutSummary(MissionEquipment equipment)
        {
            if (equipment == null)
                return "(none)";

            var parts = new List<string>();
            for (EquipmentIndex slot = EquipmentIndex.Weapon0; slot <= EquipmentIndex.Weapon3; slot++)
            {
                MissionWeapon weapon = equipment[slot];
                AppendWeaponSlotSummary(
                    parts,
                    slot,
                    weapon.Item?.StringId,
                    weapon.Item != null && weapon.Amount > 0 ? (int?)weapon.Amount : null);
            }

            return parts.Count > 0 ? string.Join(", ", parts) : "(empty)";
        }

        private static void AppendWeaponSlotSummary(
            List<string> parts,
            EquipmentIndex slot,
            string itemId,
            int? amount)
        {
            if (parts == null || string.IsNullOrWhiteSpace(itemId))
                return;

            parts.Add(
                slot +
                "=" +
                itemId.Trim() +
                (amount.HasValue && amount.Value > 1 ? "@" + amount.Value : string.Empty));
        }

        private static int? TryGetEquipmentElementAmount(EquipmentElement element)
        {
            if (element.Item == null)
                return null;

            try
            {
                MethodInfo getModifiedStackCountForUsage = typeof(EquipmentElement).GetMethod(
                    "GetModifiedStackCountForUsage",
                    new[] { typeof(int) });
                if (getModifiedStackCountForUsage != null)
                {
                    object boxedElement = element;
                    object amountValue = getModifiedStackCountForUsage.Invoke(boxedElement, new object[] { 0 });
                    if (amountValue is int amount && amount > 0)
                        return amount;
                }
            }
            catch
            {
            }

            try
            {
                PropertyInfo amountProperty = typeof(EquipmentElement).GetProperty("Amount");
                if (amountProperty != null)
                {
                    object boxedElement = element;
                    object amountValue = amountProperty.GetValue(boxedElement, null);
                    if (amountValue is int amount && amount > 0)
                        return amount;
                }
            }
            catch
            {
            }

            return null;
        }

        private static bool ProfileIncludesWeapons(ExactCreateAgentPayloadDiagnosticProfile profile)
        {
            switch (profile)
            {
                case ExactCreateAgentPayloadDiagnosticProfile.WeaponsOnly:
                case ExactCreateAgentPayloadDiagnosticProfile.WeaponsAndArmor:
                case ExactCreateAgentPayloadDiagnosticProfile.WeaponsArmorCape:
                case ExactCreateAgentPayloadDiagnosticProfile.WeaponsArmorMount:
                case ExactCreateAgentPayloadDiagnosticProfile.FullExact:
                    return true;
                default:
                    return false;
            }
        }

        private static bool ProfileIncludesArmor(ExactCreateAgentPayloadDiagnosticProfile profile)
        {
            switch (profile)
            {
                case ExactCreateAgentPayloadDiagnosticProfile.ArmorOnly:
                case ExactCreateAgentPayloadDiagnosticProfile.WeaponsAndArmor:
                case ExactCreateAgentPayloadDiagnosticProfile.WeaponsArmorCape:
                case ExactCreateAgentPayloadDiagnosticProfile.WeaponsArmorMount:
                case ExactCreateAgentPayloadDiagnosticProfile.FullExact:
                    return true;
                default:
                    return false;
            }
        }

        private static bool ProfileIncludesCape(ExactCreateAgentPayloadDiagnosticProfile profile)
        {
            return profile == ExactCreateAgentPayloadDiagnosticProfile.WeaponsArmorCape ||
                   profile == ExactCreateAgentPayloadDiagnosticProfile.FullExact;
        }

        private static bool ProfileIncludesMount(ExactCreateAgentPayloadDiagnosticProfile profile)
        {
            return profile == ExactCreateAgentPayloadDiagnosticProfile.WeaponsArmorMount ||
                   profile == ExactCreateAgentPayloadDiagnosticProfile.FullExact;
        }

        private static bool ProfileIncludesBody(ExactCreateAgentPayloadDiagnosticProfile profile)
        {
            return profile == ExactCreateAgentPayloadDiagnosticProfile.FullExact;
        }
    }
}
