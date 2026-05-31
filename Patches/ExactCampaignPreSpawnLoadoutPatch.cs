using System;
using System.Collections.Generic;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using CoopSpectator.Network.Messages;
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
            LoggedEntryIds.Clear();
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

            if (agentBuildData == null)
                return;

            if (!(agentBuildData.AgentOrigin is ExactCampaignSnapshotAgentOrigin exactOrigin))
            {
                TryInjectGeneratedOrdinarySnapshotLoadoutIntoMissionSpawnAgent(agentBuildData, spawnFromAgentVisuals);
                return;
            }

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
            bool forceGeneratedOrdinaryExactPreSpawnInjection =
                !entryState.IsHero &&
                BattleSnapshotRuntimeState.UsesGeneratedRuntimeBattleTemplateMaterialization(exactOrigin.EntryId);
            bool useNativeMountLifecycle = CoopMissionSpawnLogic.ShouldUseNativeMountLifecycleForExactEntry(entryState);
            bool preserveNativeMountedHeroBaseline =
                entryState.IsHero &&
                entryState.IsMounted &&
                useNativeMountLifecycle;
            if (forceGeneratedOrdinaryExactPreSpawnInjection)
            {
                includeWeapons = true;
                includeArmorVisuals = true;
                includeCape = true;
                includeMountVisuals = entryState.IsMounted && !useNativeMountLifecycle;
                payloadDiagnostic.IsActive = false;
                payloadDiagnostic.Reason = "generated-ordinary-exact-snapshot-pre-spawn";
                payloadDiagnostic.RequestedProfile = ExactCreateAgentPayloadDiagnosticProfile.FullExact;
                payloadDiagnostic.Profile = ExactCreateAgentPayloadDiagnosticProfile.FullExact;
                payloadDiagnostic.RequestedProfileClientSafe = true;
                payloadDiagnostic.ClientCreateAgentSafe = true;
                payloadDiagnostic.ClientCreateAgentSafeReason = "generated-ordinary-exact-snapshot-pre-spawn";
                payloadDiagnostic.RequiresCreateTimeWeapons = true;
                payloadDiagnostic.WeaponLayoutMatchesNativeTemplate = false;
                payloadDiagnostic.IncludeWeapons = true;
                payloadDiagnostic.IncludeArmorVisuals = true;
                payloadDiagnostic.IncludeCape = true;
                payloadDiagnostic.IncludeMountVisuals = entryState.IsMounted && !useNativeMountLifecycle;
                payloadDiagnostic.IncludeBodyProperties = false;
                weaponDecisionReason = "forced exact snapshot pre-spawn injection for generated ordinary entry";
                capeDecisionReason = "forced exact snapshot pre-spawn injection for generated ordinary entry";
                exactEntryCompatibilitySummary = "ExactEntryContract=generated-ordinary-exact-snapshot-pre-spawn";
            }
            PayloadDiagnosticByEntryId[exactOrigin.EntryId] = payloadDiagnostic;
            bool injectEquipment =
                forceGeneratedOrdinaryExactPreSpawnInjection ||
                resolvedContract?.InjectEquipment == true;
            Equipment exactEquipment = injectEquipment
                ? BuildPreSpawnEquipment(
                    entryState,
                    includeWeapons,
                    includeArmorVisuals,
                    includeCape,
                    includeMountVisuals,
                    preserveNativeMountedHeroBaseline,
                    preserveNativeMountedHeroBaseline
                        ? agentBuildData.AgentOverridenSpawnEquipment ?? agentBuildData.AgentCharacter?.Equipment
                        : null)
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

        private static bool TryInjectGeneratedOrdinarySnapshotLoadoutIntoMissionSpawnAgent(
            AgentBuildData agentBuildData,
            bool spawnFromAgentVisuals)
        {
            BasicCharacterObject generatedCharacter = agentBuildData?.AgentCharacter;
            string characterId = generatedCharacter?.StringId;
            if (generatedCharacter == null ||
                string.IsNullOrWhiteSpace(characterId) ||
                !BattleSnapshotRuntimeState.IsGeneratedRuntimeBattleTemplateCharacterId(characterId))
            {
                return false;
            }

            string entryId = ResolveGeneratedOrdinaryEntryIdForMissionSpawn(agentBuildData, characterId);
            if (string.IsNullOrWhiteSpace(entryId))
                return false;

            RosterEntryState entryState = BattleSnapshotRuntimeState.GetEntryState(entryId);
            if (entryState == null || entryState.IsHero)
                return false;

            if (!BattleSnapshotRuntimeState.UsesGeneratedRuntimeBattleTemplateMaterialization(entryId))
                return false;

            bool useNativeMountLifecycle = CoopMissionSpawnLogic.ShouldUseNativeMountLifecycleForExactEntry(entryState);
            Equipment exactEquipment = BuildPreSpawnEquipment(
                entryState,
                includeWeapons: true,
                includeArmorVisuals: true,
                includeCape: true,
                includeMountVisuals: entryState.IsMounted && !useNativeMountLifecycle);
            if (exactEquipment == null)
                return false;

            agentBuildData.Equipment(exactEquipment);
            EquipmentInjectedByEntryId[entryId] = true;
            PayloadDiagnosticByEntryId[entryId] = new ExactCreateAgentPayloadDiagnosticDecision
            {
                IsActive = false,
                Reason = "generated-ordinary-exact-snapshot-pre-spawn",
                EntryId = entryId,
                TroopId = characterId,
                Mode = ExactCreateAgentPayloadDiagnosticMode.Disabled,
                RequestedProfile = ExactCreateAgentPayloadDiagnosticProfile.FullExact,
                Profile = ExactCreateAgentPayloadDiagnosticProfile.FullExact,
                IncludeWeapons = true,
                IncludeArmorVisuals = true,
                IncludeCape = true,
                IncludeMountVisuals = entryState.IsMounted && !useNativeMountLifecycle,
                IncludeBodyProperties = false,
                ClientCreateAgentSafe = true,
                RequestedProfileClientSafe = true,
                WeaponLayoutMatchesNativeTemplate = false,
                RequiresCreateTimeWeapons = true
            };

            if (LoggedEntryIds.Add(entryId + "|generated-ordinary-pre-spawn-exact"))
            {
                ModLogger.Info(
                    "ExactCampaignPreSpawnLoadoutPatch: replaced vanilla/native MP synthesized equipment with snapshot-exact loadout for generated ordinary template spawn. " +
                    "EntryId=" + entryId +
                    " TroopId=" + characterId +
                    " TeamSide=" + ResolveAgentBuildDataSide(agentBuildData) +
                    " SpawnFromAgentVisuals=" + spawnFromAgentVisuals +
                    " Equipment={" + SummarizeEquipment(exactEquipment) + "}");
            }

            return true;
        }

        private static string ResolveGeneratedOrdinaryEntryIdForMissionSpawn(
            AgentBuildData agentBuildData,
            string characterId)
        {
            if (agentBuildData == null || string.IsNullOrWhiteSpace(characterId))
                return null;

            BattleSideEnum side = ResolveAgentBuildDataSide(agentBuildData);
            string canonicalSideKey = ToCanonicalSideKey(side);
            if (!string.IsNullOrWhiteSpace(canonicalSideKey))
            {
                string sideResolvedEntryId = BattleSnapshotRuntimeState.TryResolveEntryId(canonicalSideKey, characterId);
                if (!string.IsNullOrWhiteSpace(sideResolvedEntryId))
                    return sideResolvedEntryId;
            }

            CanonicalTroopInstance byBattleTemplate =
                BattleSnapshotRuntimeState.GetCanonicalTroopInstanceByBattleTemplateId(characterId);
            if (!string.IsNullOrWhiteSpace(byBattleTemplate?.EntryId))
                return byBattleTemplate.EntryId;

            CanonicalTroopInstance byCharacterId =
                BattleSnapshotRuntimeState.GetCanonicalTroopInstanceByCharacterId(characterId);
            return byCharacterId?.EntryId;
        }

        private static BattleSideEnum ResolveAgentBuildDataSide(AgentBuildData agentBuildData)
        {
            if (agentBuildData?.AgentTeam != null && agentBuildData.AgentTeam.Side != BattleSideEnum.None)
                return agentBuildData.AgentTeam.Side;

            if (agentBuildData?.AgentOrigin is ExactCampaignSnapshotAgentOrigin exactOrigin &&
                exactOrigin.Side != BattleSideEnum.None)
            {
                return exactOrigin.Side;
            }

            BattleSideEnum missionPeerSide = ResolveAuthoritativeSideFromMissionPeer(agentBuildData?.AgentMissionPeer);
            if (missionPeerSide != BattleSideEnum.None)
                return missionPeerSide;

            BattleSideEnum owningPeerSide = ResolveAuthoritativeSideFromMissionPeer(agentBuildData?.OwningAgentMissionPeer);
            if (owningPeerSide != BattleSideEnum.None)
                return owningPeerSide;

            return BattleSideEnum.None;
        }

        private static BattleSideEnum ResolveAuthoritativeSideFromMissionPeer(MissionPeer missionPeer)
        {
            if (missionPeer == null)
                return BattleSideEnum.None;

            return CoopBattleAuthorityState.GetSelectionState(missionPeer).Side;
        }

        private static string ToCanonicalSideKey(BattleSideEnum side)
        {
            switch (side)
            {
                case BattleSideEnum.Attacker:
                    return "attacker";
                case BattleSideEnum.Defender:
                    return "defender";
                default:
                    return null;
            }
        }

        private static void Mission_SpawnAgent_Postfix(AgentBuildData agentBuildData, bool spawnFromAgentVisuals, Agent __result)
        {
            if (!ExperimentalFeatures.EnableExactCampaignPreSpawnLoadoutInjection || !GameNetwork.IsServer)
                return;

            if (__result == null)
                return;

            if (!(agentBuildData?.AgentOrigin is ExactCampaignSnapshotAgentOrigin exactOrigin))
            {
                string generatedEntryId = ResolveGeneratedOrdinaryEntryIdForMissionSpawn(
                    agentBuildData,
                    agentBuildData?.AgentCharacter?.StringId);
                if (!string.IsNullOrWhiteSpace(generatedEntryId))
                {
                    CoopMissionSpawnLogic.TryApplyImmediateServerExactCampaignNativeRefreshAfterSpawn(
                        __result,
                        generatedEntryId,
                        "ExactCampaignPreSpawnLoadoutPatch.Mission.SpawnAgent postfix immediate refresh (generated ordinary)");
                    CoopMissionSpawnLogic.TryApplyImmediateServerBootstrapInitialWieldAfterSpawn(
                        __result,
                        generatedEntryId,
                        "ExactCampaignPreSpawnLoadoutPatch.Mission.SpawnAgent postfix immediate wield bootstrap (generated ordinary)");
                }

                return;
            }

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

            CoopMissionSpawnLogic.TryApplyImmediateServerExactCampaignNativeRefreshAfterSpawn(
                __result,
                exactOrigin.EntryId,
                "ExactCampaignPreSpawnLoadoutPatch.Mission.SpawnAgent postfix immediate refresh");
            CoopMissionSpawnLogic.TryApplyImmediateServerBootstrapInitialWieldAfterSpawn(
                __result,
                exactOrigin.EntryId,
                "ExactCampaignPreSpawnLoadoutPatch.Mission.SpawnAgent postfix immediate wield bootstrap");

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
            bool includeMountVisuals,
            bool preserveNativeMountBaselineWhenExcluded = false,
            Equipment nativeMountSeedEquipment = null)
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
                if (preserveNativeMountBaselineWhenExcluded)
                {
                    PreserveMountSlotFromSeed(equipment, nativeMountSeedEquipment, EquipmentIndex.Horse);
                    PreserveMountSlotFromSeed(equipment, nativeMountSeedEquipment, EquipmentIndex.HorseHarness);
                }
                else
                {
                    equipment[EquipmentIndex.Horse] = default(EquipmentElement);
                    equipment[EquipmentIndex.HorseHarness] = default(EquipmentElement);
                }
            }

            return equipment;
        }

        private static void PreserveMountSlotFromSeed(
            Equipment equipment,
            Equipment seedEquipment,
            EquipmentIndex slot)
        {
            if (equipment == null || seedEquipment == null || equipment[slot].Item != null)
                return;

            EquipmentElement seedElement = seedEquipment[slot];
            if (seedElement.Item == null)
                return;

            equipment[slot] = seedElement;
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
