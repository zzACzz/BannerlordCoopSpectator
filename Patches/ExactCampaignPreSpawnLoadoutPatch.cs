using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    public static class ExactCampaignPreSpawnLoadoutPatch
    {
        private static readonly HashSet<string> LoggedEntryIds = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, bool> EquipmentInjectedByEntryId = new Dictionary<string, bool>(StringComparer.Ordinal);
        public static bool IsOperationalOnCurrentProcess { get; private set; }

        public static void Apply(Harmony harmony)
        {
            IsOperationalOnCurrentProcess = false;
            EquipmentInjectedByEntryId.Clear();
            try
            {
                var target = AccessTools.Method(
                    typeof(Mission),
                    nameof(Mission.SpawnAgent),
                    new[] { typeof(AgentBuildData), typeof(bool) });
                var prefix = AccessTools.Method(
                    typeof(ExactCampaignPreSpawnLoadoutPatch),
                    nameof(Mission_SpawnAgent_Prefix));
                if (target == null || prefix == null)
                {
                    ModLogger.Info("ExactCampaignPreSpawnLoadoutPatch: Mission.SpawnAgent target not found. Skip.");
                    return;
                }

                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                IsOperationalOnCurrentProcess = true;
                ModLogger.Info("ExactCampaignPreSpawnLoadoutPatch: prefix applied to Mission.SpawnAgent.");
            }
            catch (Exception ex)
            {
                IsOperationalOnCurrentProcess = false;
                ModLogger.Error("ExactCampaignPreSpawnLoadoutPatch.Apply failed.", ex);
            }
        }

        private static void Mission_SpawnAgent_Prefix(AgentBuildData agentBuildData, bool spawnFromAgentVisuals)
        {
            if (!ExperimentalFeatures.EnableExactCampaignPreSpawnLoadoutInjection || !GameNetwork.IsServer)
                return;

            if (!(agentBuildData?.AgentOrigin is ExactCampaignSnapshotAgentOrigin exactOrigin))
                return;

            if (string.IsNullOrWhiteSpace(exactOrigin.EntryId))
                return;

            RosterEntryState entryState = BattleSnapshotRuntimeState.GetEntryState(exactOrigin.EntryId);
            if (entryState == null)
                return;

            bool isPlayerControlledOrigin = ((IAgentOriginBase)exactOrigin).IsUnderPlayersCommand;
            ExactTransferSpawnContract exactTransferContract = ExactTransferContractBuilder.Build(
                entryState,
                isPlayerControlledOrigin,
                (int)exactOrigin.Side,
                ResolveFormationIndex(entryState));
            ExactTransferValidationResult exactTransferValidation =
                ExactTransferContractValidator.Validate(exactTransferContract);
            ExactTransferContractRuntimeCache.RegisterPreSpawnContract(
                exactTransferContract,
                exactTransferValidation,
                "ExactCampaignPreSpawnLoadoutPatch.Mission.SpawnAgent");

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
                    : "ExactEntryContract=contract-driven-ranged-rollout";
                weaponDecisionReason = includeWeapons
                    ? (strictHeroPath
                        ? "contract-driven strict exact hero weapon policy"
                        : "contract-driven first-wave exact ranged weapon policy")
                    : (strictHeroPath
                        ? "contract-driven strict exact hero weapon policy disabled"
                        : "contract-driven first-wave exact ranged weapon policy disabled");
            }
            else
            {
                includeWeapons = ShouldIncludeWeaponsForPreSpawnInjection(
                    exactOrigin,
                    isPlayerControlledOrigin,
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
                        : "contract-driven first-wave exact ranged cape policy")
                    : (strictHeroPath
                        ? "contract-driven strict exact hero cape policy disabled"
                        : "contract-driven first-wave exact ranged cape policy disabled");
            }
            else
            {
                includeCape = CoopMissionSpawnLogic.EvaluateExactRuntimeCapeVisualContract(
                    entryState,
                    out _,
                    out capeDecisionReason);
            }
            if (!useContractDrivenPreSpawnPath && isPlayerControlledOrigin)
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
                ? exactTransferContract.SpawnPolicy.RequirePreSpawnInjection &&
                  (includeWeapons || includeCape || includeArmorVisuals || includeMountVisuals)
                : includeWeapons || includeCape;
            Equipment exactEquipment = injectEquipment
                ? BuildPreSpawnEquipment(entryState, exactTransferContract, useContractDrivenPreSpawnPath, includeWeapons)
                : null;
            EquipmentInjectedByEntryId[exactOrigin.EntryId] = injectEquipment && exactEquipment != null;
            if (exactEquipment != null)
                agentBuildData.Equipment(exactEquipment);

            CoopMissionSpawnLogic.TraceServerPreSpawnExactHeroContract(
                exactOrigin,
                entryState,
                exactEquipment,
                injectEquipment,
                includeWeapons,
                includeCape,
                weaponDecisionReason,
                capeDecisionReason,
                spawnFromAgentVisuals,
                ExactTransferContractRuntimeCache.BuildContractSummary(exactOrigin.EntryId),
                ExactTransferContractRuntimeCache.BuildValidationSummary(exactOrigin.EntryId),
                ExactTransferContractRuntimeCache.BuildRuntimeStateSummary(exactOrigin.EntryId));

            bool hasBody = useContractDrivenPreSpawnPath
                ? TryResolveContractBodyProperties(exactTransferContract, out BodyProperties bodyProperties)
                : TryResolveEntryBodyProperties(entryState, out bodyProperties);
            if (hasBody)
                agentBuildData.BodyProperties(bodyProperties);

            if (HasHeroIdentity(entryState))
            {
                bool isFemale = useContractDrivenPreSpawnPath
                    ? exactTransferContract?.Body?.IsFemale ?? entryState.HeroIsFemale
                    : entryState.HeroIsFemale;
                int? age = useContractDrivenPreSpawnPath
                    ? exactTransferContract?.Body?.Age
                    : entryState.HeroAge > 0.01f
                        ? (int?)Math.Max(1, Math.Min(120, (int)Math.Round(entryState.HeroAge)))
                        : null;
                agentBuildData.IsFemale(isFemale);
                if (age.HasValue)
                {
                    agentBuildData.Age(age.Value);
                }
            }

            if (!LoggedEntryIds.Add(exactOrigin.EntryId))
                return;

            ModLogger.Info(
                "ExactCampaignPreSpawnLoadoutPatch: injected snapshot loadout into Mission.SpawnAgent. " +
                "EntryId=" + exactOrigin.EntryId +
                " TroopId=" + exactOrigin.TroopId +
                " Hero=" + entryState.IsHero +
                " PlayerControlledOrigin=" + isPlayerControlledOrigin +
                " InjectEquipment=" + (exactEquipment != null) +
                " IncludeWeapons=" + includeWeapons +
                " WeaponDecision=" + weaponDecisionReason +
                " IncludeCape=" + includeCape +
                " CapeDecision=" + capeDecisionReason +
                " UseContractDrivenPreSpawnPath=" + useContractDrivenPreSpawnPath +
                " " + ExactTransferContractRuntimeCache.BuildValidationSummary(exactOrigin.EntryId) +
                " " + exactEntryCompatibilitySummary +
                " SpawnFromAgentVisuals=" + spawnFromAgentVisuals +
                " Equipment=" + (exactEquipment != null ? SummarizeEquipment(exactEquipment) : "(native-template)") +
                " Body=" + (!bodyProperties.Equals(default(BodyProperties))));
        }

        public static bool WasEquipmentInjectedForEntry(string entryId)
        {
            if (string.IsNullOrWhiteSpace(entryId))
                return false;

            return EquipmentInjectedByEntryId.TryGetValue(entryId, out bool injected) && injected;
        }

        private static bool ShouldIncludeWeaponsForPreSpawnInjection(
            ExactCampaignSnapshotAgentOrigin exactOrigin,
            bool isPlayerControlledOrigin,
            RosterEntryState entryState,
            out string exactEntryCompatibilitySummary,
            out string decisionReason)
        {
            exactEntryCompatibilitySummary = "ExactEntryContract=(none)";
            if (exactOrigin == null || entryState == null)
            {
                decisionReason = "entry state unavailable";
                return false;
            }

            bool supportsStrictExactHeroContract = CoopMissionSpawnLogic.EvaluateExactRuntimePreSpawnWeaponInjectionContract(
                entryState,
                out exactEntryCompatibilitySummary,
                out decisionReason);
            if (supportsStrictExactHeroContract)
            {
                if (isPlayerControlledOrigin)
                    decisionReason = "player-controlled strict exact personal hero contract";

                return true;
            }

            // Only strict exact hero entries may carry pre-spawn weapons through Mission.SpawnAgent.
            // Bulk native MP agents must keep weapon injection disabled until their runtime path is
            // proven safe, otherwise CreateAgent / SetWieldedItemIndex can desync the client.
            if (isPlayerControlledOrigin)
            {
                if (!HasExactPersonalHeroIdentity(entryState))
                {
                    decisionReason =
                        "player-controlled origin keeps native template spawn equipment because entry is not an exact personal hero";
                }
                else
                {
                    decisionReason =
                        "player-controlled exact personal hero contract rejected: " +
                        (decisionReason ?? "unknown");
                }
            }

            return false;
        }

        private static bool HasHeroIdentity(RosterEntryState entryState)
        {
            return entryState != null &&
                   (entryState.IsHero ||
                    !string.IsNullOrWhiteSpace(entryState.HeroId) ||
                    !string.IsNullOrWhiteSpace(entryState.HeroRole) ||
                    !string.IsNullOrWhiteSpace(entryState.HeroTemplateId) ||
                    !string.IsNullOrWhiteSpace(entryState.HeroBodyProperties));
        }

        private static bool HasExactPersonalHeroIdentity(RosterEntryState entryState)
        {
            return entryState != null &&
                   (entryState.IsHero ||
                    !string.IsNullOrWhiteSpace(entryState.HeroId) ||
                    string.Equals(entryState.OriginalCharacterId, "main_hero", StringComparison.OrdinalIgnoreCase));
        }

        private static int ResolveFormationIndex(RosterEntryState entryState)
        {
            BasicCharacterObject character = BattleSnapshotRuntimeState.TryResolveCharacterObject(entryState?.EntryId);
            if (character == null)
                return -1;

            FormationClass formationClass = character.DefaultFormationClass;
            return (int)formationClass;
        }

        private static bool TryResolveEntryBodyProperties(
            RosterEntryState entryState,
            out BodyProperties bodyProperties)
        {
            bodyProperties = default;
            if (string.IsNullOrWhiteSpace(entryState?.HeroBodyProperties))
                return false;

            try
            {
                return BodyProperties.FromString(entryState.HeroBodyProperties, out bodyProperties);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryResolveContractBodyProperties(
            ExactTransferSpawnContract contract,
            out BodyProperties bodyProperties)
        {
            bodyProperties = default;
            if (contract?.Body == null || !contract.Body.HasExactBodyProperties)
                return false;

            bodyProperties = contract.Body.BodyProperties;
            return true;
        }

        private static Equipment BuildPreSpawnEquipment(
            RosterEntryState entryState,
            ExactTransferSpawnContract contract,
            bool useStrictContractPath,
            bool includeWeapons)
        {
            if (useStrictContractPath && contract?.Equipment != null)
            {
                if (contract.Equipment.SpawnEquipment != null)
                    return contract.Equipment.SpawnEquipment.Clone(false);

                return CoopMissionSpawnLogic.BuildSnapshotEquipmentForExactRuntime(
                    entryState,
                    includeWeapons: includeWeapons,
                    honorExactVisualContracts: false,
                    includeArmorVisuals: contract.Equipment.IncludeArmorVisualsInPreSpawn,
                    includeMountVisuals: contract.Equipment.IncludeMountVisualsInPreSpawn);
            }

            return CoopMissionSpawnLogic.BuildSnapshotEquipmentForExactRuntime(
                entryState,
                includeWeapons: includeWeapons);
        }

        private static string SummarizeEquipment(Equipment equipment)
        {
            if (equipment == null)
                return "(none)";

            var parts = new List<string>();
            AppendEquipmentSummary(parts, equipment, EquipmentIndex.Weapon0, "Item0");
            AppendEquipmentSummary(parts, equipment, EquipmentIndex.Weapon1, "Item1");
            AppendEquipmentSummary(parts, equipment, EquipmentIndex.Weapon2, "Item2");
            AppendEquipmentSummary(parts, equipment, EquipmentIndex.Weapon3, "Item3");
            AppendEquipmentSummary(parts, equipment, EquipmentIndex.Head, "Head");
            AppendEquipmentSummary(parts, equipment, EquipmentIndex.Body, "Body");
            AppendEquipmentSummary(parts, equipment, EquipmentIndex.Leg, "Leg");
            AppendEquipmentSummary(parts, equipment, EquipmentIndex.Gloves, "Gloves");
            AppendEquipmentSummary(parts, equipment, EquipmentIndex.Cape, "Cape");
            AppendEquipmentSummary(parts, equipment, EquipmentIndex.Horse, "Horse");
            AppendEquipmentSummary(parts, equipment, EquipmentIndex.HorseHarness, "HorseHarness");
            return parts.Count > 0 ? string.Join(", ", parts) : "(empty)";
        }

        private static void AppendEquipmentSummary(
            List<string> parts,
            Equipment equipment,
            EquipmentIndex slot,
            string label)
        {
            EquipmentElement element = equipment[slot];
            if (element.IsEmpty || element.Item == null)
                return;

            parts.Add(label + "=" + element.Item.StringId);
        }
    }
}
