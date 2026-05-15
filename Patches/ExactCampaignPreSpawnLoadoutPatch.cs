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
        private static readonly Dictionary<string, ExactCreateAgentPayloadDiagnosticDecision> PayloadDiagnosticByEntryId =
            new Dictionary<string, ExactCreateAgentPayloadDiagnosticDecision>(StringComparer.Ordinal);
        public static bool IsOperationalOnCurrentProcess { get; private set; }

        public static void Apply(Harmony harmony)
        {
            IsOperationalOnCurrentProcess = false;
            EquipmentInjectedByEntryId.Clear();
            PayloadDiagnosticByEntryId.Clear();
            try
            {
                var target = AccessTools.Method(
                    typeof(Mission),
                    nameof(Mission.SpawnAgent),
                    new[] { typeof(AgentBuildData), typeof(bool) });
                var prefix = AccessTools.Method(
                    typeof(ExactCampaignPreSpawnLoadoutPatch),
                    nameof(Mission_SpawnAgent_Prefix));
                var postfix = AccessTools.Method(
                    typeof(ExactCampaignPreSpawnLoadoutPatch),
                    nameof(Mission_SpawnAgent_Postfix));
                if (target == null || prefix == null || postfix == null)
                {
                    ModLogger.Info("ExactCampaignPreSpawnLoadoutPatch: Mission.SpawnAgent target not found. Skip.");
                    return;
                }

                harmony.Patch(target, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
                IsOperationalOnCurrentProcess = true;
                ModLogger.Info("ExactCampaignPreSpawnLoadoutPatch: prefix/postfix applied to Mission.SpawnAgent.");
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
            bool contractPlayerControlledOrigin =
                isPlayerControlledOrigin &&
                entryState != null &&
                (entryState.IsHero ||
                 !string.IsNullOrWhiteSpace(entryState.HeroId) ||
                 string.Equals(entryState.OriginalCharacterId, "main_hero", StringComparison.OrdinalIgnoreCase));
            bool useDedicatedSafeStringIdExactEquipmentPath =
                CoopMissionSpawnLogic.UseDedicatedSafeStringIdExactEquipmentPathOnServer();
            ExactCreateAgentServerPreSpawnContractState resolvedContract =
                ExactCreateAgentServerPreSpawnContractResolver.Resolve(
                    entryState,
                    contractPlayerControlledOrigin,
                    (int)exactOrigin.Side,
                    ResolveFormationIndex(entryState),
                    useDedicatedSafeStringIdExactEquipmentPath);
            ExactTransferSpawnContract exactTransferContract = resolvedContract?.Contract;
            ExactTransferValidationResult exactTransferValidation = resolvedContract?.Validation;
            ExactTransferContractRuntimeCache.RegisterPreSpawnContract(
                exactTransferContract,
                exactTransferValidation,
                "ExactCampaignPreSpawnLoadoutPatch.Mission.SpawnAgent");
            string exactEntryCompatibilitySummary = resolvedContract?.ExactEntryCompatibilitySummary;
            string weaponDecisionReason = resolvedContract?.WeaponDecisionReason;
            string capeDecisionReason = resolvedContract?.CapeDecisionReason;
            bool includeWeapons = resolvedContract?.IncludeWeapons == true;
            bool includeCape = resolvedContract?.IncludeCape == true;
            bool includeArmorVisuals = resolvedContract?.IncludeArmorVisuals == true;
            bool includeMountVisuals = resolvedContract?.IncludeMountVisuals == true;
            bool useContractDrivenPreSpawnPath = resolvedContract?.UseContractDrivenPreSpawnPath == true;
            bool canInjectBodyPropertiesAtCreateAgentTime = resolvedContract?.IncludeBodyProperties == true;
            ExactCreateAgentPayloadDiagnosticDecision payloadDiagnostic =
                resolvedContract?.PayloadDiagnostic ??
                new ExactCreateAgentPayloadDiagnosticDecision
                {
                    IsActive = false,
                    Reason = "resolved-contract-absent",
                    EntryId = exactOrigin.EntryId
                };
            PayloadDiagnosticByEntryId[exactOrigin.EntryId] = payloadDiagnostic;
            bool injectEquipment = resolvedContract?.InjectEquipment == true;
            Equipment exactEquipment = injectEquipment
                ? BuildPreSpawnEquipment(
                    entryState,
                    includeWeapons,
                    includeArmorVisuals,
                    includeCape,
                    includeMountVisuals)
                : null;
            EquipmentInjectedByEntryId[exactOrigin.EntryId] = injectEquipment && exactEquipment != null;
            if (exactEquipment != null)
                agentBuildData.Equipment(exactEquipment);

            ExactCreateAgentCorridorDiagnostics.ObserveServerPreSpawnPayload(
                exactOrigin,
                entryState,
                exactTransferContract,
                payloadDiagnostic,
                exactEquipment,
                injectEquipment,
                spawnFromAgentVisuals);

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
                ExactTransferContractRuntimeCache.BuildContractSummary(exactOrigin.EntryId) + " " + payloadDiagnostic.ToSummary(),
                ExactTransferContractRuntimeCache.BuildValidationSummary(exactOrigin.EntryId),
                ExactTransferContractRuntimeCache.BuildRuntimeStateSummary(exactOrigin.EntryId));

            BodyProperties bodyProperties = default;
            bool hasBody = canInjectBodyPropertiesAtCreateAgentTime && (useContractDrivenPreSpawnPath
                ? TryResolveContractBodyProperties(exactTransferContract, out bodyProperties)
                : TryResolveEntryBodyProperties(entryState, out bodyProperties));
            if (hasBody)
                agentBuildData.BodyProperties(bodyProperties);

            if (hasBody && HasHeroIdentity(entryState))
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
                " ContractPlayerControlledOrigin=" + contractPlayerControlledOrigin +
                " InjectEquipment=" + (exactEquipment != null) +
                " UseDedicatedSafeStringIdExactEquipmentPath=" + useDedicatedSafeStringIdExactEquipmentPath +
                " IncludeWeapons=" + includeWeapons +
                " WeaponDecision=" + weaponDecisionReason +
                " IncludeCape=" + includeCape +
                " CapeDecision=" + capeDecisionReason +
                " UseContractDrivenPreSpawnPath=" + useContractDrivenPreSpawnPath +
                " " + payloadDiagnostic.ToSummary() +
                " " + ExactTransferContractRuntimeCache.BuildValidationSummary(exactOrigin.EntryId) +
                " " + exactEntryCompatibilitySummary +
                " SpawnFromAgentVisuals=" + spawnFromAgentVisuals +
                " Equipment=" + (exactEquipment != null ? SummarizeEquipment(exactEquipment) : "(native-template)") +
                " Body=" + (!bodyProperties.Equals(default(BodyProperties))));
        }

        private static void Mission_SpawnAgent_Postfix(AgentBuildData agentBuildData, bool spawnFromAgentVisuals, Agent __result)
        {
            if (!ExperimentalFeatures.EnableExactCampaignPreSpawnLoadoutInjection || !GameNetwork.IsServer)
                return;

            if (!(agentBuildData?.AgentOrigin is ExactCampaignSnapshotAgentOrigin exactOrigin))
                return;

            if (string.IsNullOrWhiteSpace(exactOrigin.EntryId))
                return;

            if (!PayloadDiagnosticByEntryId.TryGetValue(exactOrigin.EntryId, out ExactCreateAgentPayloadDiagnosticDecision payloadDiagnostic))
            {
                payloadDiagnostic = new ExactCreateAgentPayloadDiagnosticDecision
                {
                    IsActive = false,
                    Reason = "postfix-no-diagnostic-state",
                    EntryId = exactOrigin.EntryId,
                    TroopId = exactOrigin.TroopId
                };
            }

            string details =
                "EntryId=" + exactOrigin.EntryId +
                " TroopId=" + exactOrigin.TroopId +
                " AgentIndex=" + (__result?.Index.ToString() ?? "null") +
                " SpawnFromAgentVisuals=" + spawnFromAgentVisuals +
                " EquipmentInjected=" + WasEquipmentInjectedForEntry(exactOrigin.EntryId) +
                " " + payloadDiagnostic.ToSummary();
            ModLogger.Info("ExactCampaignPreSpawnLoadoutPatch: Mission.SpawnAgent result. " + details);
            ExactCreateAgentCorridorDiagnostics.ObserveServerSpawnResult(
                exactOrigin,
                payloadDiagnostic,
                __result,
                spawnFromAgentVisuals,
                WasEquipmentInjectedForEntry(exactOrigin.EntryId));
            ExactBattleAgentSpawnTraceBridgeFile.AppendRecord("pre-spawn-payload-result|" + details);
            ExactBattleRuntimeBundleBridgeFile.AppendContractEvent("server-pre-spawn-payload-result", details);
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
            bool includeWeapons,
            bool includeArmorVisuals,
            bool includeCape,
            bool includeMountVisuals)
        {
            Equipment equipment = CoopMissionSpawnLogic.BuildSnapshotEquipmentForExactRuntime(
                entryState,
                includeWeapons: includeWeapons,
                honorExactVisualContracts: false,
                includeArmorVisuals: includeArmorVisuals || includeCape,
                includeMountVisuals: includeMountVisuals);
            if (equipment == null)
                return null;

            if (!includeArmorVisuals)
            {
                equipment[EquipmentIndex.Head] = default(EquipmentElement);
                equipment[EquipmentIndex.Body] = default(EquipmentElement);
                equipment[EquipmentIndex.Leg] = default(EquipmentElement);
                equipment[EquipmentIndex.Gloves] = default(EquipmentElement);
            }

            if (!includeCape)
                equipment[EquipmentIndex.Cape] = default(EquipmentElement);

            if (!includeMountVisuals)
            {
                equipment[EquipmentIndex.Horse] = default(EquipmentElement);
                equipment[EquipmentIndex.HorseHarness] = default(EquipmentElement);
            }

            return equipment;
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
