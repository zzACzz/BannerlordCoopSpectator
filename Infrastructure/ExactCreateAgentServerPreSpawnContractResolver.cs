using System;
using CoopSpectator.MissionBehaviors;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

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

            bool strictHeroPath = exactTransferContract?.SpawnPolicy?.UseStrictExactHeroPath == true;
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

            BattleSideEnum battleSide = ResolveBattleSide(teamIndex);
            if (ShouldForceDismountedSiegePreSpawnContract(
                    battleSide,
                    entryState,
                    exactTransferContract))
            {
                ApplyDismountedSiegePreSpawnContract(exactTransferContract);
                exactTransferValidation = ExactTransferContractValidator.Validate(exactTransferContract);
                useContractDrivenPreSpawnPath =
                    exactTransferContract?.SpawnPolicy?.RequirePreSpawnInjection == true &&
                    exactTransferValidation?.IsValid == true;
                includeMountVisuals = false;
                if (payloadDiagnostic != null)
                    payloadDiagnostic.IncludeMountVisuals = false;

                if (useContractDrivenPreSpawnPath)
                {
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
                    capeDecisionReason = includeCape
                        ? (strictHeroPath
                            ? "contract-driven strict exact hero cape policy"
                            : "contract-driven full-army exact cape policy")
                        : (strictHeroPath
                            ? "contract-driven strict exact hero cape policy disabled"
                            : "contract-driven full-army exact cape policy disabled");
                }
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
                bool allowExactSiegePreSpawnEquipmentInjection =
                    ShouldAllowExactSiegePreSpawnEquipmentInjectionOnDedicated(
                        useContractDrivenPreSpawnPath,
                        strictHeroPath,
                        exactTransferContract,
                        exactTransferValidation);
                if (allowExactSiegePreSpawnEquipmentInjection)
                {
                    injectEquipment = exactTransferContract?.SpawnPolicy?.RequirePreSpawnInjection == true &&
                                      (includeWeapons || includeCape || includeArmorVisuals || includeMountVisuals);
                }
                else
                {
                    bool allowMountOnlyInjection = useContractDrivenPreSpawnPath &&
                                                   !strictHeroPath &&
                                                   includeMountVisuals;
                    if (allowMountOnlyInjection)
                    {
                        // Dedicated create-agent remains too fragile for full exact gear outside
                        // the dedicated siege materialization corridor, but native mount visuals
                        // must exist at spawn time for cavalry.
                        includeWeapons = false;
                        includeArmorVisuals = false;
                        includeCape = false;
                        injectEquipment = true;
                    }
                    else
                    {
                        injectEquipment = false;
                    }
                }
            }

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

        private static BattleSideEnum ResolveBattleSide(int teamIndex)
        {
            if (teamIndex == (int)BattleSideEnum.Attacker)
                return BattleSideEnum.Attacker;

            if (teamIndex == (int)BattleSideEnum.Defender)
                return BattleSideEnum.Defender;

            return BattleSideEnum.None;
        }

        private static bool ShouldForceDismountedSiegePreSpawnContract(
            BattleSideEnum side,
            RosterEntryState entryState,
            ExactTransferSpawnContract contract)
        {
            if (entryState?.IsMounted != true && contract?.Mount?.IsMounted != true)
            {
                return false;
            }

            Mission mission = Mission.Current;
            if (mission == null)
                return false;

            if (side != BattleSideEnum.None &&
                ExactCampaignArmyBootstrap.TryGetSpawnHorses(mission, side, out bool spawnHorses))
            {
                return !spawnHorses;
            }

            return SceneRuntimeClassifier.IsExactSiegeAssaultWithDeploymentScene(mission.SceneName ?? string.Empty);
        }

        private static bool ShouldAllowExactSiegePreSpawnEquipmentInjectionOnDedicated(
            bool useContractDrivenPreSpawnPath,
            bool strictHeroPath,
            ExactTransferSpawnContract contract,
            ExactTransferValidationResult validation)
        {
            if (!useContractDrivenPreSpawnPath ||
                !strictHeroPath ||
                contract?.SpawnPolicy?.RequirePreSpawnInjection != true ||
                validation?.IsValid != true)
            {
                return false;
            }

            Mission mission = Mission.Current;
            return mission != null &&
                   SceneRuntimeClassifier.IsExactSiegeAssaultWithDeploymentScene(mission.SceneName ?? string.Empty);
        }

        private static void ApplyDismountedSiegePreSpawnContract(ExactTransferSpawnContract contract)
        {
            if (contract == null)
                return;

            if (contract.Identity != null)
                contract.Identity.IsMountedExpected = false;

            if (contract.Equipment != null)
                contract.Equipment.IncludeMountVisualsInPreSpawn = false;

            if (contract.Mount == null)
                return;

            contract.Mount.IsMounted = false;
            contract.Mount.HorseItemId = null;
            contract.Mount.HarnessItemId = null;
            contract.Mount.ExpectedMountAgentIndex = null;
            contract.Mount.RequiresVerifiedMountLink = false;
        }
    }
}
