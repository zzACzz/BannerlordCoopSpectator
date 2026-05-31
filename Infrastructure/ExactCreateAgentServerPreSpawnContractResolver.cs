using System;
using CoopSpectator.MissionBehaviors;

namespace CoopSpectator.Infrastructure
{
    internal sealed class ExactCreateAgentServerPreSpawnContractState
    {
        public bool ContractResolved { get; set; }
        public bool UseContractDrivenPreSpawnPath { get; set; }
        public bool UseDedicatedSafeStringIdExactEquipmentPath { get; set; }
        public bool InjectEquipment { get; set; }
        public bool IncludeWeapons { get; set; }
        public bool IncludeArmorVisuals { get; set; }
        public bool IncludeCape { get; set; }
        public bool IncludeMountVisuals { get; set; }
        public bool IncludeBodyProperties { get; set; }
        public bool PayloadDiagnosticActive { get; set; }
        public string RequestedProfile { get; set; }
        public string EffectiveProfile { get; set; }
        public string ExactEntryCompatibilitySummary { get; set; }
        public string WeaponDecisionReason { get; set; }
        public string CapeDecisionReason { get; set; }
        public ExactTransferSpawnContract Contract { get; set; }
        public ExactTransferValidationResult Validation { get; set; }
        public ExactCreateAgentPayloadDiagnosticDecision PayloadDiagnostic { get; set; }

        public bool ActualPreSpawnIncludesWeapons => InjectEquipment && IncludeWeapons;
        public bool ActualPreSpawnIncludesArmorVisuals => InjectEquipment && IncludeArmorVisuals;
        public bool ActualPreSpawnIncludesCapeVisual => InjectEquipment && IncludeCape;
        public bool ActualPreSpawnIncludesMountVisuals => InjectEquipment && IncludeMountVisuals;
    }

    internal static class ExactCreateAgentServerPreSpawnContractResolver
    {
        internal static ExactCreateAgentServerPreSpawnContractState Resolve(
            RosterEntryState entryState,
            bool contractPlayerControlledOrigin,
            int teamIndex,
            int formationIndex,
            bool useDedicatedSafeStringIdExactEquipmentPath)
        {
            if (entryState == null || string.IsNullOrWhiteSpace(entryState.EntryId))
                return null;

            bool useNativeMountLifecycle = CoopMissionSpawnLogic.ShouldUseNativeMountLifecycleForExactEntry(entryState);
            ExactTransferSpawnContract exactTransferContract = ExactTransferContractBuilder.Build(
                entryState,
                contractPlayerControlledOrigin,
                teamIndex,
                formationIndex);
            ExactTransferValidationResult exactTransferValidation =
                ExactTransferContractValidator.Validate(exactTransferContract);
            bool useStrictCanonicalFieldBattleOrdinaryExactPreSpawnPath =
                ShouldUseStrictCanonicalFieldBattleOrdinaryExactPreSpawnPath(entryState);
            bool useCanonicalGeneratedTemplateNativeSpawn =
                !useStrictCanonicalFieldBattleOrdinaryExactPreSpawnPath &&
                ShouldUseCanonicalGeneratedTemplateNativeSpawn(entryState);

            string exactEntryCompatibilitySummary;
            string weaponDecisionReason;
            bool includeWeapons = exactTransferContract?.Equipment?.IncludeWeaponsInPreSpawn ?? false;
            bool includeCape = exactTransferContract?.Equipment?.IncludeCapeInPreSpawn ?? false;
            bool includeArmorVisuals = exactTransferContract?.Equipment?.IncludeArmorVisualsInPreSpawn ?? false;
            bool includeMountVisuals = exactTransferContract?.Equipment?.IncludeMountVisualsInPreSpawn ?? false;
            if (useNativeMountLifecycle)
                includeMountVisuals = false;
            bool useContractDrivenPreSpawnPath =
                !useCanonicalGeneratedTemplateNativeSpawn &&
                exactTransferContract?.SpawnPolicy?.RequirePreSpawnInjection == true &&
                exactTransferValidation?.IsValid == true;
            if (useCanonicalGeneratedTemplateNativeSpawn)
            {
                exactEntryCompatibilitySummary = "ExactEntryContract=canonical-generated-template-native-spawn";
                weaponDecisionReason =
                    "canonical field battle generated MP copy is treated as the final native spawn template";
                includeWeapons = false;
                includeCape = false;
                includeArmorVisuals = false;
                includeMountVisuals = false;
            }
            else if (useContractDrivenPreSpawnPath)
            {
                bool strictHeroPath = exactTransferContract?.SpawnPolicy?.UseStrictExactHeroPath == true;
                exactEntryCompatibilitySummary = strictHeroPath
                    ? "ExactEntryContract=contract-driven-strict-hero"
                    : "ExactEntryContract=contract-driven-full-army";
                weaponDecisionReason = includeWeapons
                    ? (strictHeroPath
                        ? "contract-driven strict exact hero weapon policy"
                        : "contract-driven full-army exact weapon policy")
                    : (strictHeroPath
                        ? "contract-driven strict exact hero weapon policy disabled"
                        : "contract-driven full-army exact weapon policy disabled");
            }
            else
            {
                includeWeapons = CoopMissionSpawnLogic.EvaluateExactRuntimePreSpawnWeaponInjectionContract(
                    entryState,
                    out exactEntryCompatibilitySummary,
                    out weaponDecisionReason);
            }

            string capeDecisionReason;
            if (useCanonicalGeneratedTemplateNativeSpawn)
            {
                capeDecisionReason =
                    "canonical field battle generated MP copy is treated as the final native spawn template";
            }
            else if (useContractDrivenPreSpawnPath)
            {
                bool strictHeroPath = exactTransferContract?.SpawnPolicy?.UseStrictExactHeroPath == true;
                capeDecisionReason = includeCape
                    ? (strictHeroPath
                        ? "contract-driven strict exact hero cape policy"
                        : "contract-driven full-army exact cape policy")
                    : (strictHeroPath
                        ? "contract-driven strict exact hero cape policy disabled"
                        : "contract-driven full-army exact cape policy disabled");
            }
            else
            {
                includeCape = CoopMissionSpawnLogic.EvaluateExactRuntimeCapeVisualContract(
                    entryState,
                    out _,
                    out capeDecisionReason);
            }

            bool canInjectBodyPropertiesAtCreateAgentTime = useCanonicalGeneratedTemplateNativeSpawn
                ? false
                : useContractDrivenPreSpawnPath
                ? exactTransferContract?.Body?.HasExactBodyProperties == true
                : !string.IsNullOrWhiteSpace(entryState.HeroBodyProperties);
            ExactCreateAgentPayloadDiagnosticDecision payloadDiagnostic =
                ExactCreateAgentPayloadDiagnostics.Resolve(
                    entryState,
                    exactTransferContract,
                    useContractDrivenPreSpawnPath,
                    includeWeapons,
                    includeArmorVisuals,
                    includeCape,
                    includeMountVisuals,
                    canInjectBodyPropertiesAtCreateAgentTime);
            if (useCanonicalGeneratedTemplateNativeSpawn)
            {
                payloadDiagnostic = new ExactCreateAgentPayloadDiagnosticDecision
                {
                    IsActive = false,
                    Reason = "canonical-generated-template-native-spawn",
                    EntryId = entryState.EntryId,
                    TroopId = entryState.SpawnTemplateId ?? entryState.CharacterId ?? entryState.OriginalCharacterId,
                    ClientCreateAgentSafe = true,
                    ClientCreateAgentSafeReason = "generated-template-native-spawn"
                };
            }
            if (payloadDiagnostic.IsActive)
            {
                includeWeapons = payloadDiagnostic.IncludeWeapons;
                includeArmorVisuals = payloadDiagnostic.IncludeArmorVisuals;
                includeCape = payloadDiagnostic.IncludeCape;
                includeMountVisuals = payloadDiagnostic.IncludeMountVisuals;
                canInjectBodyPropertiesAtCreateAgentTime = payloadDiagnostic.IncludeBodyProperties;
            }

            if (useNativeMountLifecycle)
            {
                includeMountVisuals = false;
                if (payloadDiagnostic.IsActive)
                    payloadDiagnostic.IncludeMountVisuals = false;
            }

            bool ordinaryEntryRequiresCanonicalServerNativeBaseline =
                !useStrictCanonicalFieldBattleOrdinaryExactPreSpawnPath &&
                RequiresCanonicalFieldBattleServerNativeBaselineForOrdinaryEntry(
                    entryState,
                    useContractDrivenPreSpawnPath,
                    useDedicatedSafeStringIdExactEquipmentPath);

            if (useStrictCanonicalFieldBattleOrdinaryExactPreSpawnPath)
            {
                exactEntryCompatibilitySummary = "ExactEntryContract=generated-ordinary-exact-snapshot-pre-spawn";
                weaponDecisionReason =
                    "strict canonical field battle ordinary path uses exact snapshot equipment " +
                    "at create-time instead of native generated-template baseline";
                capeDecisionReason =
                    "strict canonical field battle ordinary path uses exact snapshot visuals " +
                    "at create-time instead of native generated-template baseline";
                includeWeapons = true;
                includeArmorVisuals = true;
                includeCape = true;
                includeMountVisuals = entryState.IsMounted && !useNativeMountLifecycle;
                canInjectBodyPropertiesAtCreateAgentTime = false;
                payloadDiagnostic = new ExactCreateAgentPayloadDiagnosticDecision
                {
                    IsActive = false,
                    Reason = "generated-ordinary-exact-snapshot-pre-spawn",
                    EntryId = entryState.EntryId,
                    TroopId = entryState.SpawnTemplateId ?? entryState.CharacterId ?? entryState.OriginalCharacterId,
                    RequestedProfile = ExactCreateAgentPayloadDiagnosticProfile.FullExact,
                    Profile = ExactCreateAgentPayloadDiagnosticProfile.FullExact,
                    RequestedProfileClientSafe = true,
                    ClientCreateAgentSafe = true,
                    ClientCreateAgentSafeReason = "generated-ordinary-exact-snapshot-pre-spawn",
                    RequiresCreateTimeWeapons = true,
                    WeaponLayoutMatchesNativeTemplate = false,
                    IncludeWeapons = true,
                    IncludeArmorVisuals = true,
                    IncludeCape = true,
                    IncludeMountVisuals = entryState.IsMounted && !useNativeMountLifecycle,
                    IncludeBodyProperties = false
                };
            }

            if (!ordinaryEntryRequiresCanonicalServerNativeBaseline &&
                ShouldForceCanonicalFieldBattleCreateTimeWeapons(
                    entryState,
                    useContractDrivenPreSpawnPath,
                    useDedicatedSafeStringIdExactEquipmentPath,
                    payloadDiagnostic))
            {
                includeWeapons = true;
                weaponDecisionReason =
                    "canonical field battle ordinary AI requires create-time exact weapon materialization " +
                    "because payload layout differs from native template";
            }

            if (!useContractDrivenPreSpawnPath && contractPlayerControlledOrigin)
            {
                if (includeCape)
                {
                    capeDecisionReason = "player-controlled strict exact personal hero cape visual contract";
                }
                else if (!HasExactPersonalHeroIdentity(entryState))
                {
                    capeDecisionReason =
                        "player-controlled origin keeps native template visual slots because entry is not an exact personal hero";
                }
                else
                {
                    capeDecisionReason =
                        "player-controlled exact personal hero cape visual contract rejected: " +
                        (capeDecisionReason ?? "unknown");
                }
            }

            bool injectEquipment = useContractDrivenPreSpawnPath
                ? exactTransferContract?.SpawnPolicy?.RequirePreSpawnInjection == true &&
                  (includeWeapons || includeCape || includeArmorVisuals || includeMountVisuals)
                : includeWeapons || includeCape;
            if (useDedicatedSafeStringIdExactEquipmentPath)
            {
                bool ordinaryEntryHybridCreateAgentSafe =
                    !ordinaryEntryRequiresCanonicalServerNativeBaseline &&
                    useContractDrivenPreSpawnPath &&
                    !HasExactPersonalHeroIdentity(entryState) &&
                    payloadDiagnostic?.IsActive == true &&
                    payloadDiagnostic.ClientCreateAgentSafe &&
                    (includeWeapons || includeCape || includeArmorVisuals || includeMountVisuals);
                bool strictFootHeroHybridCreateAgentSafe =
                    IsCanonicalFieldBattleStrictFootHeroCreateAgentSafe(
                        entryState,
                        exactTransferContract,
                        exactTransferValidation,
                        useContractDrivenPreSpawnPath,
                        includeWeapons,
                        includeCape,
                        includeArmorVisuals,
                        includeMountVisuals);
                bool strictMountedHeroHybridCreateAgentSafe =
                    IsCanonicalFieldBattleStrictMountedHeroCreateAgentSafe(
                        entryState,
                        exactTransferContract,
                        exactTransferValidation,
                        useContractDrivenPreSpawnPath,
                        includeWeapons,
                        includeCape,
                        includeArmorVisuals,
                        includeMountVisuals);
                if (strictFootHeroHybridCreateAgentSafe)
                {
                    weaponDecisionReason =
                        "canonical field battle strict foot hero uses create-time exact equipment " +
                        "because native template payload desyncs weapon slots";
                }
                else if (strictMountedHeroHybridCreateAgentSafe)
                {
                    weaponDecisionReason =
                        "canonical field battle strict mounted hero uses create-time exact equipment " +
                        "because native template spawn/overlay desyncs mounted weapon slots";
                }
                else if (ordinaryEntryRequiresCanonicalServerNativeBaseline)
                {
                    weaponDecisionReason =
                        "canonical field battle ordinary AI keeps native CreateAgent baseline and " +
                        "reapplies exact weapons post-create because dedicated pre-spawn exact loadout " +
                        "leaves the live wield state invalid";
                }

                if (!ordinaryEntryHybridCreateAgentSafe &&
                    !strictFootHeroHybridCreateAgentSafe &&
                    !strictMountedHeroHybridCreateAgentSafe)
                    injectEquipment = false;
            }

            if (useStrictCanonicalFieldBattleOrdinaryExactPreSpawnPath)
                injectEquipment = true;

            return new ExactCreateAgentServerPreSpawnContractState
            {
                ContractResolved = exactTransferContract != null,
                UseContractDrivenPreSpawnPath = useContractDrivenPreSpawnPath,
                UseDedicatedSafeStringIdExactEquipmentPath = useDedicatedSafeStringIdExactEquipmentPath,
                InjectEquipment = injectEquipment,
                IncludeWeapons = includeWeapons,
                IncludeArmorVisuals = includeArmorVisuals,
                IncludeCape = includeCape,
                IncludeMountVisuals = includeMountVisuals,
                IncludeBodyProperties = canInjectBodyPropertiesAtCreateAgentTime,
                PayloadDiagnosticActive = payloadDiagnostic?.IsActive == true,
                RequestedProfile = payloadDiagnostic?.RequestedProfile.ToString() ?? string.Empty,
                EffectiveProfile = payloadDiagnostic?.Profile.ToString() ?? string.Empty,
                ExactEntryCompatibilitySummary = exactEntryCompatibilitySummary,
                WeaponDecisionReason = weaponDecisionReason,
                CapeDecisionReason = capeDecisionReason,
                Contract = exactTransferContract,
                Validation = exactTransferValidation,
                PayloadDiagnostic = payloadDiagnostic
            };
        }

        private static bool HasExactPersonalHeroIdentity(RosterEntryState entryState)
        {
            return entryState != null &&
                   (entryState.IsHero ||
                    !string.IsNullOrWhiteSpace(entryState.HeroId) ||
                    string.Equals(entryState.OriginalCharacterId, "main_hero", StringComparison.OrdinalIgnoreCase));
        }

        private static bool ShouldForceCanonicalFieldBattleCreateTimeWeapons(
            RosterEntryState entryState,
            bool useContractDrivenPreSpawnPath,
            bool useDedicatedSafeStringIdExactEquipmentPath,
            ExactCreateAgentPayloadDiagnosticDecision payloadDiagnostic)
        {
            if (RequiresCanonicalFieldBattleServerNativeBaselineForOrdinaryEntry(
                    entryState,
                    useContractDrivenPreSpawnPath,
                    useDedicatedSafeStringIdExactEquipmentPath))
            {
                return false;
            }

            if (entryState == null ||
                !useContractDrivenPreSpawnPath ||
                !useDedicatedSafeStringIdExactEquipmentPath ||
                payloadDiagnostic?.IsActive != true ||
                !payloadDiagnostic.ClientCreateAgentSafe ||
                !payloadDiagnostic.RequiresCreateTimeWeapons ||
                payloadDiagnostic.IncludeWeapons ||
                HasExactPersonalHeroIdentity(entryState))
            {
                return false;
            }

            return BattleSnapshotRuntimeState.GetCurrent()?.CanonicalBattle != null;
        }

        private static bool RequiresCanonicalFieldBattleServerNativeBaselineForOrdinaryEntry(
            RosterEntryState entryState,
            bool useContractDrivenPreSpawnPath,
            bool useDedicatedSafeStringIdExactEquipmentPath)
        {
            if (entryState == null ||
                !useContractDrivenPreSpawnPath ||
                !useDedicatedSafeStringIdExactEquipmentPath ||
                HasExactPersonalHeroIdentity(entryState))
            {
                return false;
            }

            var snapshot = BattleSnapshotRuntimeState.GetCurrent();
            if (snapshot?.CanonicalBattle == null)
                return false;

            return string.Equals(
                snapshot.CanonicalBattle.Context?.MultiplayerGameType,
                "Battle",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCanonicalFieldBattleStrictFootHeroCreateAgentSafe(
            RosterEntryState entryState,
            ExactTransferSpawnContract exactTransferContract,
            ExactTransferValidationResult exactTransferValidation,
            bool useContractDrivenPreSpawnPath,
            bool includeWeapons,
            bool includeCape,
            bool includeArmorVisuals,
            bool includeMountVisuals)
        {
            if (entryState == null ||
                exactTransferContract == null ||
                exactTransferValidation?.IsValid != true ||
                !useContractDrivenPreSpawnPath ||
                !includeWeapons ||
                entryState.IsMounted ||
                exactTransferContract.SpawnPolicy?.UseStrictExactHeroPath != true ||
                exactTransferContract.InitialWield?.HasWeapon2Risk == true)
            {
                return false;
            }

            var snapshot = BattleSnapshotRuntimeState.GetCurrent();
            if (snapshot?.CanonicalBattle == null)
                return false;

            return string.Equals(
                snapshot.CanonicalBattle.Context?.MultiplayerGameType,
                "Battle",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCanonicalFieldBattleStrictMountedHeroCreateAgentSafe(
            RosterEntryState entryState,
            ExactTransferSpawnContract exactTransferContract,
            ExactTransferValidationResult exactTransferValidation,
            bool useContractDrivenPreSpawnPath,
            bool includeWeapons,
            bool includeCape,
            bool includeArmorVisuals,
            bool includeMountVisuals)
        {
            bool useNativeMountLifecycleForMountedHero =
                CoopMissionSpawnLogic.ShouldUseNativeMountLifecycleForExactEntry(entryState);
            if (entryState == null ||
                exactTransferContract == null ||
                exactTransferValidation?.IsValid != true ||
                !useContractDrivenPreSpawnPath ||
                !includeWeapons ||
                !includeArmorVisuals ||
                !entryState.IsMounted ||
                exactTransferContract.SpawnPolicy?.UseStrictExactHeroPath != true ||
                exactTransferContract.InitialWield?.HasWeapon2Risk == true)
            {
                return false;
            }

            // Strict mounted heroes intentionally keep the native mount lifecycle.
            // That should not suppress create-time exact rider equipment when the
            // weapon contract is otherwise safe.
            if (!includeMountVisuals && !useNativeMountLifecycleForMountedHero)
                return false;

            var snapshot = BattleSnapshotRuntimeState.GetCurrent();
            if (snapshot?.CanonicalBattle == null)
                return false;

            return string.Equals(
                snapshot.CanonicalBattle.Context?.MultiplayerGameType,
                "Battle",
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool ShouldUseCanonicalGeneratedTemplateNativeSpawn(RosterEntryState entryState)
        {
            if (entryState == null ||
                HasExactPersonalHeroIdentity(entryState) ||
                !CoopMissionSpawnLogic.UseDedicatedSafeStringIdExactEquipmentPathOnServer())
            {
                return false;
            }

            var snapshot = BattleSnapshotRuntimeState.GetCurrent();
            if (snapshot?.CanonicalBattle == null ||
                !string.Equals(
                    snapshot.CanonicalBattle.Context?.MultiplayerGameType,
                    "Battle",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return BattleSnapshotRuntimeState.UsesGeneratedRuntimeBattleTemplateMaterialization(entryState.EntryId);
        }

        private static bool ShouldUseStrictCanonicalFieldBattleOrdinaryExactPreSpawnPath(RosterEntryState entryState)
        {
            if (entryState == null ||
                HasExactPersonalHeroIdentity(entryState) ||
                !CoopMissionSpawnLogic.UseDedicatedSafeStringIdExactEquipmentPathOnServer())
            {
                return false;
            }

            var snapshot = BattleSnapshotRuntimeState.GetCurrent();
            if (snapshot?.CanonicalBattle == null ||
                !string.Equals(
                    snapshot.CanonicalBattle.Context?.MultiplayerGameType,
                    "Battle",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return BattleSnapshotRuntimeState.UsesGeneratedRuntimeBattleTemplateMaterialization(entryState.EntryId);
        }
    }
}
