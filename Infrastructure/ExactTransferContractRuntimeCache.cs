using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Infrastructure
{
    internal static class ExactTransferContractRuntimeCache
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, ExactTransferSpawnContract> ContractsByEntryId =
            new Dictionary<string, ExactTransferSpawnContract>(StringComparer.Ordinal);
        private static readonly Dictionary<string, ExactTransferValidationResult> ValidationByEntryId =
            new Dictionary<string, ExactTransferValidationResult>(StringComparer.Ordinal);
        private static readonly Dictionary<string, ExactTransferRuntimeState> RuntimeStateByEntryId =
            new Dictionary<string, ExactTransferRuntimeState>(StringComparer.Ordinal);
        private static readonly Dictionary<int, string> EntryIdByRiderAgentIndex =
            new Dictionary<int, string>();
        private static readonly Dictionary<int, string> EntryIdByMountAgentIndex =
            new Dictionary<int, string>();

        public static void Reset(string source)
        {
            lock (Sync)
            {
                ContractsByEntryId.Clear();
                ValidationByEntryId.Clear();
                RuntimeStateByEntryId.Clear();
                EntryIdByRiderAgentIndex.Clear();
                EntryIdByMountAgentIndex.Clear();
            }

            string details = "Source=" + (source ?? "unknown");
            ModLogger.Info("ExactTransferContractRuntimeCache: reset. " + details);
            if (GameNetwork.IsServer)
                ExactBattleRuntimeBundleBridgeFile.AppendContractEvent("exact-transfer-contract-cache-reset", details);
        }

        public static void RegisterPreSpawnContract(
            ExactTransferSpawnContract contract,
            ExactTransferValidationResult validation,
            string source)
        {
            if (contract == null || string.IsNullOrWhiteSpace(contract.EntryId))
                return;

            ExactTransferRuntimeState runtimeState;
            lock (Sync)
            {
                ContractsByEntryId[contract.EntryId] = contract;
                ValidationByEntryId[contract.EntryId] = validation ?? new ExactTransferValidationResult();

                runtimeState = EnsureRuntimeStateLocked(contract.EntryId);
                runtimeState.EntryId = contract.EntryId;
                runtimeState.IsMountedContract = contract.Mount?.IsMounted ?? false;

                ExactTransferStageMachine.TryAdvance(
                    runtimeState,
                    ExactTransferStage.SnapshotResolved,
                    requiresMountLink: runtimeState.IsMountedContract,
                    failureContext: source);
                ExactTransferStageMachine.TryAdvance(
                    runtimeState,
                    ExactTransferStage.ContractBuilt,
                    requiresMountLink: runtimeState.IsMountedContract,
                    failureContext: source);

                if (validation?.IsValid == true)
                {
                    ExactTransferStageMachine.TryAdvance(
                        runtimeState,
                        ExactTransferStage.ContractValidated,
                        requiresMountLink: runtimeState.IsMountedContract,
                        failureContext: source);
                    ExactTransferStageMachine.TryAdvance(
                        runtimeState,
                        ExactTransferStage.PreSpawnPrepared,
                        requiresMountLink: runtimeState.IsMountedContract,
                        failureContext: source);
                }
                else
                {
                    runtimeState.MarkFailure(
                        ExactTransferFailureReason.MissingContractField,
                        BuildErrorList(validation?.Errors));
                }
            }

            string contractSummary = BuildContractSummary(contract.EntryId);
            string validationSummary = BuildValidationSummary(contract.EntryId);
            string runtimeSummary = BuildRuntimeStateSummary(contract.EntryId);
            ModLogger.Info(
                "ExactTransferContractRuntimeCache: registered pre-spawn contract. " +
                "EntryId=" + contract.EntryId +
                " Source=" + (source ?? "unknown") +
                " " + contractSummary +
                " " + validationSummary +
                " " + runtimeSummary);
            if (GameNetwork.IsServer)
            {
                ExactBattleRuntimeBundleBridgeFile.AppendContractEvent(
                    "exact-transfer-pre-spawn-contract",
                    "EntryId=" + contract.EntryId +
                    " Source=" + (source ?? "unknown") +
                    " " + contractSummary +
                    " " + validationSummary +
                    " " + runtimeSummary);
            }
        }

        public static bool TryGetContract(string entryId, out ExactTransferSpawnContract contract)
        {
            lock (Sync)
                return ContractsByEntryId.TryGetValue(entryId ?? string.Empty, out contract);
        }

        public static bool TryGetValidation(string entryId, out ExactTransferValidationResult validation)
        {
            lock (Sync)
                return ValidationByEntryId.TryGetValue(entryId ?? string.Empty, out validation);
        }

        public static bool TryGetRuntimeState(string entryId, out ExactTransferRuntimeState runtimeState)
        {
            lock (Sync)
                return RuntimeStateByEntryId.TryGetValue(entryId ?? string.Empty, out runtimeState);
        }

        public static bool TryGetEntryIdByRiderAgentIndex(int riderAgentIndex, out string entryId)
        {
            lock (Sync)
                return EntryIdByRiderAgentIndex.TryGetValue(riderAgentIndex, out entryId);
        }

        public static bool TryGetEntryIdByMountAgentIndex(int mountAgentIndex, out string entryId)
        {
            lock (Sync)
                return EntryIdByMountAgentIndex.TryGetValue(mountAgentIndex, out entryId);
        }

        public static void RegisterClientObservedContract(
            ExactTransferSpawnContract contract,
            ExactTransferValidationResult validation,
            int riderAgentIndex,
            int mountAgentIndex,
            string source)
        {
            if (contract == null || string.IsNullOrWhiteSpace(contract.EntryId) || riderAgentIndex < 0)
                return;

            ExactTransferRuntimeState runtimeState;
            lock (Sync)
            {
                ContractsByEntryId[contract.EntryId] = contract;
                ValidationByEntryId[contract.EntryId] = validation ?? new ExactTransferValidationResult();
                EntryIdByRiderAgentIndex[riderAgentIndex] = contract.EntryId;
                if (mountAgentIndex >= 0)
                    EntryIdByMountAgentIndex[mountAgentIndex] = contract.EntryId;

                runtimeState = EnsureRuntimeStateLocked(contract.EntryId);
                runtimeState.EntryId = contract.EntryId;
                runtimeState.RiderAgentIndex = riderAgentIndex;
                runtimeState.MountAgentIndex = mountAgentIndex >= 0
                    ? (int?)mountAgentIndex
                    : runtimeState.MountAgentIndex;
                runtimeState.IsMountedContract = contract.Mount?.IsMounted ?? false;

                TryAdvanceBaselineStagesLocked(runtimeState, validation, source);
                if (runtimeState.Stage != ExactTransferStage.Failed)
                {
                    ExactTransferStageMachine.TryAdvance(
                        runtimeState,
                        ExactTransferStage.CreateAgentPayloadObserved,
                        requiresMountLink: runtimeState.IsMountedContract,
                        failureContext: source);
                }
            }

            string details =
                "EntryId=" + contract.EntryId +
                " RiderAgentIndex=" + riderAgentIndex +
                " MountAgentIndex=" + mountAgentIndex +
                " Source=" + (source ?? "unknown") +
                " " + BuildContractSummary(contract.EntryId) +
                " " + BuildValidationSummary(contract.EntryId) +
                " " + BuildRuntimeStateSummary(contract.EntryId);
            ModLogger.Info("ExactTransferContractRuntimeCache: registered client observed contract. " + details);
            ExactBattleRuntimeBundleBridgeFile.AppendContractEvent("exact-transfer-client-observed-contract", details);
        }

        public static void ObserveClientMaterialized(
            int riderAgentIndex,
            Agent riderAgent,
            string source)
        {
            if (riderAgentIndex < 0 || riderAgent == null)
                return;

            string entryId;
            lock (Sync)
            {
                if (!EntryIdByRiderAgentIndex.TryGetValue(riderAgentIndex, out entryId) ||
                    string.IsNullOrWhiteSpace(entryId))
                {
                    return;
                }

                ExactTransferRuntimeState runtimeState = EnsureRuntimeStateLocked(entryId);
                runtimeState.RiderAgentIndex = riderAgentIndex;
                if (riderAgent.MountAgent != null && riderAgent.MountAgent.Index >= 0)
                {
                    runtimeState.MountAgentIndex = riderAgent.MountAgent.Index;
                    EntryIdByMountAgentIndex[riderAgent.MountAgent.Index] = entryId;
                }

                ExactTransferStageMachine.TryAdvance(
                    runtimeState,
                    ExactTransferStage.RiderMaterialized,
                    requiresMountLink: runtimeState.IsMountedContract,
                    failureContext: source);
                if (runtimeState.IsMountedContract && riderAgent.MountAgent != null)
                {
                    ExactTransferStageMachine.TryAdvance(
                        runtimeState,
                        ExactTransferStage.MountMaterialized,
                        requiresMountLink: true,
                        failureContext: source);
                    ExactTransferStageMachine.TryAdvance(
                        runtimeState,
                        ExactTransferStage.MountLinkVerified,
                        requiresMountLink: true,
                        failureContext: source);
                }
            }

            ExactBattleRuntimeBundleBridgeFile.AppendContractEvent(
                "exact-transfer-client-materialized",
                "RiderAgentIndex=" + riderAgentIndex +
                " MountAgentIndex=" + (riderAgent.MountAgent?.Index.ToString() ?? "null") +
                " Source=" + (source ?? "unknown") +
                " " + BuildRuntimeStateSummary(entryId));
        }

        public static void ObserveClientPeerBound(int riderAgentIndex, string source)
        {
            ObserveClientStage(riderAgentIndex, ExactTransferStage.PeerBound, source, "exact-transfer-client-peer-bound");
        }

        public static void ObserveClientEquipmentSynchronized(int riderAgentIndex, string source)
        {
            ObserveClientStage(
                riderAgentIndex,
                ExactTransferStage.EquipmentSynchronized,
                source,
                "exact-transfer-client-equipment-synchronized");
        }

        public static void ReportClientFailure(
            int riderAgentIndex,
            ExactTransferFailureReason failureReason,
            string failureContext,
            string source)
        {
            if (riderAgentIndex < 0)
                return;

            string entryId;
            lock (Sync)
            {
                if (!EntryIdByRiderAgentIndex.TryGetValue(riderAgentIndex, out entryId) ||
                    string.IsNullOrWhiteSpace(entryId))
                {
                    return;
                }

                ExactTransferRuntimeState runtimeState = EnsureRuntimeStateLocked(entryId);
                runtimeState.RiderAgentIndex = riderAgentIndex;
                runtimeState.MarkFailure(failureReason, failureContext);
            }

            ExactBattleRuntimeBundleBridgeFile.AppendContractEvent(
                "exact-transfer-client-failure",
                "EntryId=" + (entryId ?? "null") +
                " RiderAgentIndex=" + riderAgentIndex +
                " FailureReason=" + failureReason +
                " FailureContext=" + (failureContext ?? "null") +
                " Source=" + (source ?? "unknown") +
                " " + BuildRuntimeStateSummary(entryId));
        }

        public static void TryCompleteCleanupForAgentIndex(int agentIndex, string source)
        {
            if (agentIndex < 0)
                return;

            string entryId = null;
            string runtimeSummary = null;
            bool hadRiderMapping = false;
            bool hadMountMapping = false;
            bool removedRuntimeState = false;

            lock (Sync)
            {
                if (EntryIdByRiderAgentIndex.TryGetValue(agentIndex, out string riderEntryId) &&
                    !string.IsNullOrWhiteSpace(riderEntryId))
                {
                    entryId = riderEntryId;
                    hadRiderMapping = true;
                }
                else if (EntryIdByMountAgentIndex.TryGetValue(agentIndex, out string mountEntryId) &&
                         !string.IsNullOrWhiteSpace(mountEntryId))
                {
                    entryId = mountEntryId;
                    hadMountMapping = true;
                }

                if (string.IsNullOrWhiteSpace(entryId))
                    return;

                if (RuntimeStateByEntryId.TryGetValue(entryId, out ExactTransferRuntimeState runtimeState) &&
                    runtimeState != null)
                {
                    runtimeSummary = BuildRuntimeStateSummaryLocked(runtimeState);
                    RuntimeStateByEntryId.Remove(entryId);
                    removedRuntimeState = true;

                    if (runtimeState.RiderAgentIndex.HasValue &&
                        EntryIdByRiderAgentIndex.TryGetValue(runtimeState.RiderAgentIndex.Value, out string trackedRiderEntryId) &&
                        string.Equals(trackedRiderEntryId, entryId, StringComparison.Ordinal))
                    {
                        EntryIdByRiderAgentIndex.Remove(runtimeState.RiderAgentIndex.Value);
                    }

                    if (runtimeState.MountAgentIndex.HasValue &&
                        EntryIdByMountAgentIndex.TryGetValue(runtimeState.MountAgentIndex.Value, out string trackedMountEntryId) &&
                        string.Equals(trackedMountEntryId, entryId, StringComparison.Ordinal))
                    {
                        EntryIdByMountAgentIndex.Remove(runtimeState.MountAgentIndex.Value);
                    }
                }

                if (EntryIdByRiderAgentIndex.TryGetValue(agentIndex, out string remainingRiderEntryId) &&
                    string.Equals(remainingRiderEntryId, entryId, StringComparison.Ordinal))
                {
                    EntryIdByRiderAgentIndex.Remove(agentIndex);
                    hadRiderMapping = true;
                }

                if (EntryIdByMountAgentIndex.TryGetValue(agentIndex, out string remainingMountEntryId) &&
                    string.Equals(remainingMountEntryId, entryId, StringComparison.Ordinal))
                {
                    EntryIdByMountAgentIndex.Remove(agentIndex);
                    hadMountMapping = true;
                }
            }

            string details =
                "AgentIndex=" + agentIndex +
                " EntryId=" + (entryId ?? "null") +
                " Source=" + (source ?? "unknown") +
                " HadRiderMapping=" + hadRiderMapping +
                " HadMountMapping=" + hadMountMapping +
                " RemovedRuntimeState=" + removedRuntimeState +
                " PreviousRuntime=" + (runtimeSummary ?? "ExactTransferRuntime={State=absent}");
            ModLogger.Info("ExactTransferContractRuntimeCache: completed agent cleanup. " + details);
            ExactBattleRuntimeBundleBridgeFile.AppendContractEvent("exact-transfer-agent-cleanup", details);
        }

        public static string BuildContractSummary(string entryId)
        {
            lock (Sync)
            {
                if (!ContractsByEntryId.TryGetValue(entryId ?? string.Empty, out ExactTransferSpawnContract contract) || contract == null)
                    return "ExactTransferContract={State=absent}";

                return
                    "ExactTransferContract={StrictHeroPath=" + contract.SpawnPolicy?.UseStrictExactHeroPath +
                    ",RequirePreSpawnInjection=" + contract.SpawnPolicy?.RequirePreSpawnInjection +
                    ",ForbidSurrogatePrimaryMaterialization=" + contract.SpawnPolicy?.ForbidSurrogatePrimaryMaterialization +
                    ",MaterializationToken=" + (contract.Identity?.MaterializationEntryIdToken ?? "null") +
                    ",SurrogateShellId=" + (contract.Identity?.SurrogateShellCharacterId ?? "null") +
                    ",NativeCharacterId=" + (contract.Identity?.NativeMultiplayerCharacterId ?? "null") +
                    ",Mounted=" + contract.Mount?.IsMounted +
                    ",ExactBody=" + contract.Body?.HasExactBodyProperties +
                    ",IncludeWeapons=" + contract.Equipment?.IncludeWeaponsInPreSpawn +
                    ",IncludeArmorVisuals=" + contract.Equipment?.IncludeArmorVisualsInPreSpawn +
                    ",IncludeCape=" + contract.Equipment?.IncludeCapeInPreSpawn +
                    ",IncludeMountVisuals=" + contract.Equipment?.IncludeMountVisualsInPreSpawn +
                    ",MountedWeaponLayoutNormalized=" + contract.Equipment?.MountedWeaponLayoutNormalized +
                    ",MountedWeaponLayout=" + (contract.Equipment?.MountedWeaponLayoutSummary ?? "null") +
                    ",PreBattleWeaponStateMode=" + contract.PreBattleWeaponState?.Mode +
                    ",PreBattleReadinessMode=" + contract.PreBattleWeaponState?.ReadinessMode +
                    ",PreBattleMain=" + (contract.PreBattleWeaponState?.PreferredMainHandSlotIndex?.ToString() ?? "null") +
                    ",PreBattleOff=" + (contract.PreBattleWeaponState?.PreferredOffHandSlotIndex?.ToString() ?? "null") +
                    ",PreBattleAmmo=" + (contract.PreBattleWeaponState?.ExpectedAmmoSlotIndex?.ToString() ?? "null") +
                    ",PreBattleAmmoAttached=" + contract.PreBattleWeaponState?.ExpectAmmoAttachedToMainHand +
                    ",PreBattleInitialPreference=" + contract.PreBattleWeaponState?.InitialWeaponEquipPreference +
                    ",PreBattleSafeHoldMain=" + (contract.PreBattleWeaponState?.SafeHoldMainHandSlotIndex?.ToString() ?? "null") +
                    ",PreBattleSafeHoldOff=" + (contract.PreBattleWeaponState?.SafeHoldOffHandSlotIndex?.ToString() ?? "null") +
                    ",PreBattleSafeHoldInitialPreference=" + contract.PreBattleWeaponState?.SafeHoldInitialWeaponEquipPreference +
                    ",PreBattleReason=" + (contract.PreBattleWeaponState?.DecisionReason ?? "null") +
                    ",PeerDrivenBody=" + contract.PeerBinding?.AllowPeerDrivenBodyAtCreateAgentTime +
                    ",PeerDrivenBanner=" + contract.PeerBinding?.AllowPeerDrivenBannerAtCreateAgentTime +
                    ",UsePlayerAgentCreateBranch=" + contract.PeerBinding?.UsePlayerAgentCreateBranch + "}";
            }
        }

        public static string BuildValidationSummary(string entryId)
        {
            lock (Sync)
            {
                if (!ValidationByEntryId.TryGetValue(entryId ?? string.Empty, out ExactTransferValidationResult validation) || validation == null)
                    return "ExactTransferValidation={State=absent}";

                return
                    "ExactTransferValidation={Valid=" + validation.IsValid +
                    ",ErrorCount=" + validation.Errors.Count +
                    ",WarningCount=" + validation.Warnings.Count +
                    ",Errors=" + BuildErrorList(validation.Errors) +
                    ",Warnings=" + BuildErrorList(validation.Warnings) + "}";
            }
        }

        public static string BuildRuntimeStateSummary(string entryId)
        {
            lock (Sync)
            {
                if (!RuntimeStateByEntryId.TryGetValue(entryId ?? string.Empty, out ExactTransferRuntimeState runtimeState) || runtimeState == null)
                    return "ExactTransferRuntime={State=absent}";

                return
                    "ExactTransferRuntime={Stage=" + runtimeState.Stage +
                    ",FailureReason=" + runtimeState.FailureReason +
                    ",FailureContext=" + (runtimeState.FailureContext ?? "null") +
                    ",RiderAgentIndex=" + (runtimeState.RiderAgentIndex?.ToString() ?? "null") +
                    ",MountAgentIndex=" + (runtimeState.MountAgentIndex?.ToString() ?? "null") +
                    ",MountedContract=" + runtimeState.IsMountedContract +
                    ",RiderMaterialized=" + runtimeState.RiderMaterialized +
                    ",MountMaterialized=" + runtimeState.MountMaterialized +
                    ",MountLinkVerified=" + runtimeState.MountLinkVerified +
                    ",PeerBound=" + runtimeState.PeerBound +
                    ",EquipmentSynchronized=" + runtimeState.EquipmentSynchronized +
                    ",ExactVisualApplied=" + runtimeState.ExactVisualApplied +
                    ",CommanderControlEnabled=" + runtimeState.CommanderControlEnabled +
                    ",LastTransitionUtc=" + runtimeState.LastTransitionUtc.ToString("O") + "}";
            }
        }

        private static ExactTransferRuntimeState EnsureRuntimeStateLocked(string entryId)
        {
            if (!RuntimeStateByEntryId.TryGetValue(entryId, out ExactTransferRuntimeState runtimeState))
            {
                runtimeState = new ExactTransferRuntimeState
                {
                    EntryId = entryId
                };
                RuntimeStateByEntryId[entryId] = runtimeState;
            }

            return runtimeState;
        }

        private static string BuildErrorList(IReadOnlyCollection<string> values)
        {
            if (values == null || values.Count == 0)
                return "[]";

            return "[" + string.Join(" | ", values.Where(value => !string.IsNullOrWhiteSpace(value))) + "]";
        }

        private static string BuildRuntimeStateSummaryLocked(ExactTransferRuntimeState runtimeState)
        {
            if (runtimeState == null)
                return "ExactTransferRuntime={State=absent}";

            return
                "ExactTransferRuntime={Stage=" + runtimeState.Stage +
                ",FailureReason=" + runtimeState.FailureReason +
                ",FailureContext=" + (runtimeState.FailureContext ?? "null") +
                ",RiderAgentIndex=" + (runtimeState.RiderAgentIndex?.ToString() ?? "null") +
                ",MountAgentIndex=" + (runtimeState.MountAgentIndex?.ToString() ?? "null") +
                ",MountedContract=" + runtimeState.IsMountedContract +
                ",RiderMaterialized=" + runtimeState.RiderMaterialized +
                ",MountMaterialized=" + runtimeState.MountMaterialized +
                ",MountLinkVerified=" + runtimeState.MountLinkVerified +
                ",PeerBound=" + runtimeState.PeerBound +
                ",EquipmentSynchronized=" + runtimeState.EquipmentSynchronized +
                ",ExactVisualApplied=" + runtimeState.ExactVisualApplied +
                ",CommanderControlEnabled=" + runtimeState.CommanderControlEnabled +
                ",LastTransitionUtc=" + runtimeState.LastTransitionUtc.ToString("O") + "}";
        }

        private static void ObserveClientStage(
            int riderAgentIndex,
            ExactTransferStage stage,
            string source,
            string eventName)
        {
            if (riderAgentIndex < 0)
                return;

            string entryId;
            lock (Sync)
            {
                if (!EntryIdByRiderAgentIndex.TryGetValue(riderAgentIndex, out entryId) ||
                    string.IsNullOrWhiteSpace(entryId))
                {
                    return;
                }

                ExactTransferRuntimeState runtimeState = EnsureRuntimeStateLocked(entryId);
                runtimeState.RiderAgentIndex = riderAgentIndex;
                ExactTransferStageMachine.TryAdvance(
                    runtimeState,
                    stage,
                    requiresMountLink: runtimeState.IsMountedContract,
                    failureContext: source);
            }

            ExactBattleRuntimeBundleBridgeFile.AppendContractEvent(
                eventName,
                "EntryId=" + (entryId ?? "null") +
                " RiderAgentIndex=" + riderAgentIndex +
                " Source=" + (source ?? "unknown") +
                " " + BuildRuntimeStateSummary(entryId));
        }

        private static void TryAdvanceBaselineStagesLocked(
            ExactTransferRuntimeState runtimeState,
            ExactTransferValidationResult validation,
            string source)
        {
            ExactTransferStageMachine.TryAdvance(
                runtimeState,
                ExactTransferStage.SnapshotResolved,
                requiresMountLink: runtimeState.IsMountedContract,
                failureContext: source);
            ExactTransferStageMachine.TryAdvance(
                runtimeState,
                ExactTransferStage.ContractBuilt,
                requiresMountLink: runtimeState.IsMountedContract,
                failureContext: source);

            if (validation?.IsValid == true)
            {
                ExactTransferStageMachine.TryAdvance(
                    runtimeState,
                    ExactTransferStage.ContractValidated,
                    requiresMountLink: runtimeState.IsMountedContract,
                    failureContext: source);
                ExactTransferStageMachine.TryAdvance(
                    runtimeState,
                    ExactTransferStage.PreSpawnPrepared,
                    requiresMountLink: runtimeState.IsMountedContract,
                    failureContext: source);
            }
            else
            {
                runtimeState.MarkFailure(
                    ExactTransferFailureReason.MissingContractField,
                    BuildErrorList(validation?.Errors));
            }
        }
    }
}
