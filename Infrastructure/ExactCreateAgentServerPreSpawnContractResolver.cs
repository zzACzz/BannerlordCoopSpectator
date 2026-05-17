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

            ExactTransferSpawnContract exactTransferContract = ExactTransferContractBuilder.Build(
                entryState,
                contractPlayerControlledOrigin,
                teamIndex,
                formationIndex);
            ExactTransferValidationResult exactTransferValidation =
                ExactTransferContractValidator.Validate(exactTransferContract);

            string exactEntryCompatibilitySummary;
            string weaponDecisionReason;
            bool includeWeapons = exactTransferContract?.Equipment?.IncludeWeaponsInPreSpawn ?? false;
            bool includeCape = exactTransferContract?.Equipment?.IncludeCapeInPreSpawn ?? false;
            bool includeArmorVisuals = exactTransferContract?.Equipment?.IncludeArmorVisualsInPreSpawn ?? false;
            bool includeMountVisuals = exactTransferContract?.Equipment?.IncludeMountVisualsInPreSpawn ?? false;
            bool useContractDrivenPreSpawnPath =
                exactTransferContract?.SpawnPolicy?.RequirePreSpawnInjection == true &&
                exactTransferValidation?.IsValid == true;
            if (useContractDrivenPreSpawnPath)
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
            if (useContractDrivenPreSpawnPath)
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

            bool canInjectBodyPropertiesAtCreateAgentTime = useContractDrivenPreSpawnPath
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
            if (payloadDiagnostic.IsActive)
            {
                includeWeapons = payloadDiagnostic.IncludeWeapons;
                includeArmorVisuals = payloadDiagnostic.IncludeArmorVisuals;
                includeCape = payloadDiagnostic.IncludeCape;
                includeMountVisuals = payloadDiagnostic.IncludeMountVisuals;
                canInjectBodyPropertiesAtCreateAgentTime = payloadDiagnostic.IncludeBodyProperties;
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
                injectEquipment = false;

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
    }
}
