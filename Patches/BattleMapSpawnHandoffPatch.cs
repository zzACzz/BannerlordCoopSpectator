using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using CoopSpectator.GameMode;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using HarmonyLib;
using NetworkMessages.FromServer;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.InputSystem;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;
using TaleWorlds.ObjectSystem;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Suppresses the local spectator-follow network echo during the narrow
    /// battle-map authoritative spawn handoff window.
    /// </summary>
    public static class BattleMapSpawnHandoffPatch
    {
        private static readonly FieldInfo FollowedAgentField =
            typeof(MissionPeer).GetField("_followedAgent", BindingFlags.Instance | BindingFlags.NonPublic);
        private static MethodInfo _missionNetworkComponentHandleServerEventCreateAgentMethod;
        private static MethodInfo _missionNetworkComponentHandleServerEventSetAgentActionSetMethod;
        private static MethodInfo _missionNetworkComponentHandleServerEventSynchronizeAgentEquipmentMethod;
        private static MethodInfo _missionNetworkComponentHandleServerEventAttachWeaponToWeaponInAgentEquipmentSlotMethod;
        private static MethodInfo _missionNetworkComponentHandleServerEventSetWeaponNetworkDataMethod;
        private static MethodInfo _missionNetworkComponentHandleServerEventSetWeaponAmmoDataMethod;
        private static MethodInfo _missionNetworkComponentHandleServerEventSetWeaponReloadPhaseMethod;
        private static MethodInfo _missionNetworkComponentHandleServerEventStartSwitchingWeaponUsageIndexMethod;
        private static MethodInfo _missionNetworkComponentHandleServerEventWeaponUsageIndexChangeMessageMethod;
        private static MethodInfo _missionNetworkComponentHandleServerEventSetWieldedItemIndexMethod;
        private static MethodInfo _missionNetworkComponentHandleServerEventSetAgentHealthMethod;
        private static MethodInfo _missionNetworkComponentHandleServerEventMakeAgentDeadMethod;

        private static string _lastSuppressedFollowSwitchKey;
        private static string _lastSuppressedWeaponDropKey;
        private static string _lastLocalVisualFinalizeKey;
        private static string _lastSuppressedAssignFormationKey;
        private static string _lastSuppressedLocalSelectAllFormationsKey;
        private static string _lastObservedLocalSelectAllFormationsKey;
        private static string _lastSuppressedServerSelectAllFormationsKey;
        private static string _lastSuppressedOrderTroopPlacerTickKey;
        private static string _lastSuppressedNonCommanderOrderUiKey;
        private static string _lastRepairedOrderTroopPlacerOwnerKey;
        private static string _lastPromotedLocalCommanderGeneralKey;
        private static string _lastFinalizedLocalCommanderOrderControlKey;
        private static string _lastMaintainedLocalCommanderOrderControlKey;
        private static string _lastResolvedControlledEntryFallbackKey;
        private static string _lastControlledEntryIdentityVerificationKey;
        private static string _lastEstablishedCommanderStateBypassKey;
        private static string _lastDeferredNonCommanderSuppressionKey;
        private static string _lastAutoSelectedAllLocalCommanderFormationsKey;
        private static string _lastPreFormationCommanderPromotionKey;
        private static string _lastForcedCampaignCommanderOrderUiKey;
        private static string _lastExactCommanderOrderVmStateKey;
        private static string _lastRebuiltSingleplayerCommanderOrderUiKey;
        private static string _lastExactCommanderOrderHotkeyFallbackKey;
        private static string _lastExactCommanderFormationHotkeyFallbackKey;
        private static string _lastExactCommanderOrderHotkeySuppressionKey;
        private static string _lastExactCommanderDisabledAfterSetOrderUpdatesKey;
        private static string _lastExactCommanderOrderBarMovieBindingKey;
        private static string _lastForcedExactCommanderTroopSelectionInputsKey;
        private static string _lastExactCommanderOrderItemExecutionKey;
        private static bool _suppressExactCommanderOrderHotkeyFallbackUntilRelease;
        private static object _activeExactCommanderMissionOrderVm;
        private static PendingLocalCommanderOrderControlFinalization _pendingLocalCommanderOrderControlFinalization;
        private static readonly Dictionary<int, DeferredMountedHeroCreateAgentPayload> DeferredMountedHeroCreateAgentPayloads =
            new Dictionary<int, DeferredMountedHeroCreateAgentPayload>();
        private static readonly List<DeferredClientCreateAgentPayload> DeferredClientCreateAgentPayloads =
            new List<DeferredClientCreateAgentPayload>();
        private static readonly List<DeferredClientSetAgentActionSetPayload> DeferredClientSetAgentActionSetPayloads =
            new List<DeferredClientSetAgentActionSetPayload>();
        private static readonly List<DeferredClientSynchronizeAgentEquipmentPayload> DeferredClientSynchronizeAgentEquipmentPayloads =
            new List<DeferredClientSynchronizeAgentEquipmentPayload>();
        private static readonly List<DeferredClientAttachWeaponToWeaponInAgentEquipmentSlotPayload> DeferredClientAttachWeaponToWeaponInAgentEquipmentSlotPayloads =
            new List<DeferredClientAttachWeaponToWeaponInAgentEquipmentSlotPayload>();
        private static readonly List<DeferredClientSetWeaponNetworkDataPayload> DeferredClientSetWeaponNetworkDataPayloads =
            new List<DeferredClientSetWeaponNetworkDataPayload>();
        private static readonly List<DeferredClientSetWeaponAmmoDataPayload> DeferredClientSetWeaponAmmoDataPayloads =
            new List<DeferredClientSetWeaponAmmoDataPayload>();
        private static readonly List<DeferredClientSetWeaponReloadPhasePayload> DeferredClientSetWeaponReloadPhasePayloads =
            new List<DeferredClientSetWeaponReloadPhasePayload>();
        private static readonly List<DeferredClientStartSwitchingWeaponUsageIndexPayload> DeferredClientStartSwitchingWeaponUsageIndexPayloads =
            new List<DeferredClientStartSwitchingWeaponUsageIndexPayload>();
        private static readonly List<DeferredClientWeaponUsageIndexChangePayload> DeferredClientWeaponUsageIndexChangePayloads =
            new List<DeferredClientWeaponUsageIndexChangePayload>();
        private static readonly List<DeferredClientSetWieldedItemIndexPayload> DeferredClientSetWieldedItemIndexPayloads =
            new List<DeferredClientSetWieldedItemIndexPayload>();
        private static readonly List<DeferredClientSetAgentHealthPayload> DeferredClientSetAgentHealthPayloads =
            new List<DeferredClientSetAgentHealthPayload>();
        private static readonly List<DeferredClientMakeAgentDeadPayload> DeferredClientMakeAgentDeadPayloads =
            new List<DeferredClientMakeAgentDeadPayload>();
        private static readonly HashSet<string> _strictExactHeroOnSpawnWieldRefreshAppliedKeys =
            new HashSet<string>(StringComparer.Ordinal);
        private static long _nextDeferredClientCreateAgentSequence;
        private static long _nextDeferredClientSetAgentActionSetSequence;
        private static long _nextDeferredClientSynchronizeAgentEquipmentSequence;
        private static long _nextDeferredClientAttachWeaponToWeaponInAgentEquipmentSlotSequence;
        private static long _nextDeferredClientSetWeaponNetworkDataSequence;
        private static long _nextDeferredClientSetWeaponAmmoDataSequence;
        private static long _nextDeferredClientSetWeaponReloadPhaseSequence;
        private static long _nextDeferredClientStartSwitchingWeaponUsageIndexSequence;
        private static long _nextDeferredClientWeaponUsageIndexChangeSequence;
        private static long _nextDeferredClientSetWieldedItemIndexSequence;
        private static long _nextDeferredClientSetAgentHealthSequence;
        private static long _nextDeferredClientMakeAgentDeadSequence;

        private sealed class PendingLocalCommanderOrderControlFinalization
        {
            public int PeerIndex;
            public int TeamIndex;
            public int AgentIndex;
            public string EntryId;
            public DateTime QueuedUtc;
            public int Attempts;
        }

        private sealed class DeferredMountedHeroCreateAgentPayload
        {
            public CreateAgent Message;
            public DateTime DeferredUtc;
            public DateTime LastAttemptUtc;
            public int Attempts;
            public string DeferralReason;
        }

        private sealed class DeferredClientCreateAgentPayload
        {
            public long Sequence;
            public CreateAgent Message;
            public DateTime DeferredUtc;
            public DateTime LastAttemptUtc;
            public int Attempts;
            public string DeferralReason;
        }

        private sealed class DeferredClientSetAgentActionSetPayload
        {
            public long Sequence;
            public SetAgentActionSet Message;
            public DateTime DeferredUtc;
            public DateTime LastAttemptUtc;
            public int Attempts;
            public string DeferralReason;
        }

        private sealed class DeferredClientSynchronizeAgentEquipmentPayload
        {
            public long Sequence;
            public SynchronizeAgentSpawnEquipment Message;
            public DateTime DeferredUtc;
            public DateTime LastAttemptUtc;
            public int Attempts;
            public string DeferralReason;
        }

        private sealed class DeferredClientAttachWeaponToWeaponInAgentEquipmentSlotPayload
        {
            public long Sequence;
            public AttachWeaponToWeaponInAgentEquipmentSlot Message;
            public DateTime DeferredUtc;
            public DateTime LastAttemptUtc;
            public int Attempts;
            public string DeferralReason;
        }

        private sealed class DeferredClientSetWeaponNetworkDataPayload
        {
            public long Sequence;
            public SetWeaponNetworkData Message;
            public DateTime DeferredUtc;
            public DateTime LastAttemptUtc;
            public int Attempts;
            public string DeferralReason;
        }

        private sealed class DeferredClientSetWeaponAmmoDataPayload
        {
            public long Sequence;
            public SetWeaponAmmoData Message;
            public DateTime DeferredUtc;
            public DateTime LastAttemptUtc;
            public int Attempts;
            public string DeferralReason;
        }

        private sealed class DeferredClientSetWeaponReloadPhasePayload
        {
            public long Sequence;
            public SetWeaponReloadPhase Message;
            public DateTime DeferredUtc;
            public DateTime LastAttemptUtc;
            public int Attempts;
            public string DeferralReason;
        }

        private sealed class DeferredClientStartSwitchingWeaponUsageIndexPayload
        {
            public long Sequence;
            public StartSwitchingWeaponUsageIndex Message;
            public DateTime DeferredUtc;
            public DateTime LastAttemptUtc;
            public int Attempts;
            public string DeferralReason;
        }

        private sealed class DeferredClientWeaponUsageIndexChangePayload
        {
            public long Sequence;
            public WeaponUsageIndexChangeMessage Message;
            public DateTime DeferredUtc;
            public DateTime LastAttemptUtc;
            public int Attempts;
            public string DeferralReason;
        }

        private sealed class DeferredClientSetWieldedItemIndexPayload
        {
            public long Sequence;
            public SetWieldedItemIndex Message;
            public DateTime DeferredUtc;
            public DateTime LastAttemptUtc;
            public int Attempts;
            public string DeferralReason;
        }

        private sealed class DeferredClientSetAgentHealthPayload
        {
            public long Sequence;
            public SetAgentHealth Message;
            public DateTime DeferredUtc;
            public DateTime LastAttemptUtc;
            public int Attempts;
            public string DeferralReason;
        }

        private sealed class DeferredClientMakeAgentDeadPayload
        {
            public long Sequence;
            public MakeAgentDead Message;
            public DateTime DeferredUtc;
            public DateTime LastAttemptUtc;
            public int Attempts;
            public string DeferralReason;
        }

        public static void Apply(Harmony harmony)
        {
            TryApplyPatchStep(nameof(PatchMissionPeerFollowedAgent), () => PatchMissionPeerFollowedAgent(harmony));
            TryApplyPatchStep(nameof(PatchMissionNetworkComponentCreateAgent), () => PatchMissionNetworkComponentCreateAgent(harmony));
            TryApplyPatchStep(nameof(PatchMissionNetworkComponentSetAgentActionSet), () => PatchMissionNetworkComponentSetAgentActionSet(harmony));
            TryApplyPatchStep(nameof(PatchMissionNetworkComponentSynchronizeAgentEquipment), () => PatchMissionNetworkComponentSynchronizeAgentEquipment(harmony));
            TryApplyPatchStep(nameof(PatchMissionNetworkComponentAttachWeaponToWeaponInAgentEquipmentSlot), () => PatchMissionNetworkComponentAttachWeaponToWeaponInAgentEquipmentSlot(harmony));
            TryApplyPatchStep(nameof(PatchMissionNetworkComponentSetWeaponNetworkData), () => PatchMissionNetworkComponentSetWeaponNetworkData(harmony));
            TryApplyPatchStep(nameof(PatchMissionNetworkComponentSetWeaponAmmoData), () => PatchMissionNetworkComponentSetWeaponAmmoData(harmony));
            TryApplyPatchStep(nameof(PatchMissionNetworkComponentSetWeaponReloadPhase), () => PatchMissionNetworkComponentSetWeaponReloadPhase(harmony));
            TryApplyPatchStep(nameof(PatchMissionNetworkComponentStartSwitchingWeaponUsageIndex), () => PatchMissionNetworkComponentStartSwitchingWeaponUsageIndex(harmony));
            TryApplyPatchStep(nameof(PatchMissionNetworkComponentWeaponUsageIndexChangeMessage), () => PatchMissionNetworkComponentWeaponUsageIndexChangeMessage(harmony));
            TryApplyPatchStep(nameof(PatchMissionNetworkComponentSetAgentPeer), () => PatchMissionNetworkComponentSetAgentPeer(harmony));
            TryApplyPatchStep(nameof(PatchMissionNetworkComponentSetAgentHealth), () => PatchMissionNetworkComponentSetAgentHealth(harmony));
            TryApplyPatchStep(nameof(PatchMissionNetworkComponentMakeAgentDead), () => PatchMissionNetworkComponentMakeAgentDead(harmony));
            TryApplyPatchStep(nameof(PatchMissionNetworkComponentSetWieldedItemIndex), () => PatchMissionNetworkComponentSetWieldedItemIndex(harmony));
            TryApplyPatchStep(nameof(PatchMissionNetworkComponentSpawnWeaponAsDropFromAgent), () => PatchMissionNetworkComponentSpawnWeaponAsDropFromAgent(harmony));
            TryApplyPatchStep(nameof(PatchMissionNetworkComponentAssignFormationToPlayer), () => PatchMissionNetworkComponentAssignFormationToPlayer(harmony));
            TryApplyPatchStep(nameof(PatchOrderControllerSelectAllFormations), () => PatchOrderControllerSelectAllFormations(harmony));
            TryApplyPatchStep(nameof(PatchOrderTroopPlacerMissionScreenTick), () => PatchOrderTroopPlacerMissionScreenTick(harmony));
            TryApplyPatchStep(nameof(PatchMissionNetworkComponentSelectAllFormations), () => PatchMissionNetworkComponentSelectAllFormations(harmony));
            TryApplyPatchStep(nameof(PatchMissionMultiplayerGameModeFlagDominationClientBotsControlledChanged), () => PatchMissionMultiplayerGameModeFlagDominationClientBotsControlledChanged(harmony));
            TryApplyPatchStep(nameof(PatchMissionGauntletMultiplayerOrderUIHandlerMissionScreenTick), () => PatchMissionGauntletMultiplayerOrderUIHandlerMissionScreenTick(harmony));
            TryApplyPatchStep(nameof(PatchMissionOrderVmViewOrders), () => PatchMissionOrderVmViewOrders(harmony));
            TryApplyPatchStep(nameof(PatchMissionOrderVmOpenToggleOrder), () => PatchMissionOrderVmOpenToggleOrder(harmony));
            TryApplyPatchStep(nameof(PatchMissionOrderVmTryCloseToggleOrder), () => PatchMissionOrderVmTryCloseToggleOrder(harmony));
            TryApplyPatchStep(nameof(PatchMissionOrderVmOnOrderExecuted), () => PatchMissionOrderVmOnOrderExecuted(harmony));
            TryApplyPatchStep(nameof(PatchOrderItemVmOnExecuteAction), () => PatchOrderItemVmOnExecuteAction(harmony));
        }

        private static void TryApplyPatchStep(string stepName, Action applyStep)
        {
            try
            {
                applyStep?.Invoke();
            }
            catch (Exception ex)
            {
                ModLogger.Error("BattleMapSpawnHandoffPatch.Apply step failed. Step=" + (stepName ?? "unknown"), ex);
            }
        }

        public static void ResetRuntimeState(string source, bool preserveCommanderOrderControlState = false)
        {
            ExactCreateAgentCorridorDiagnostics.ResetRuntimeState(source);
            _strictExactHeroOnSpawnWieldRefreshAppliedKeys.Clear();
            lock (DeferredMountedHeroCreateAgentPayloads)
            {
                DeferredMountedHeroCreateAgentPayloads.Clear();
            }
            lock (DeferredClientCreateAgentPayloads)
            {
                DeferredClientCreateAgentPayloads.Clear();
            }
            lock (DeferredClientSetAgentActionSetPayloads)
            {
                DeferredClientSetAgentActionSetPayloads.Clear();
            }
            lock (DeferredClientSynchronizeAgentEquipmentPayloads)
            {
                DeferredClientSynchronizeAgentEquipmentPayloads.Clear();
            }
            lock (DeferredClientAttachWeaponToWeaponInAgentEquipmentSlotPayloads)
            {
                DeferredClientAttachWeaponToWeaponInAgentEquipmentSlotPayloads.Clear();
            }
            lock (DeferredClientSetWeaponNetworkDataPayloads)
            {
                DeferredClientSetWeaponNetworkDataPayloads.Clear();
            }
            lock (DeferredClientSetWeaponAmmoDataPayloads)
            {
                DeferredClientSetWeaponAmmoDataPayloads.Clear();
            }
            lock (DeferredClientSetWeaponReloadPhasePayloads)
            {
                DeferredClientSetWeaponReloadPhasePayloads.Clear();
            }
            lock (DeferredClientStartSwitchingWeaponUsageIndexPayloads)
            {
                DeferredClientStartSwitchingWeaponUsageIndexPayloads.Clear();
            }
            lock (DeferredClientWeaponUsageIndexChangePayloads)
            {
                DeferredClientWeaponUsageIndexChangePayloads.Clear();
            }
            lock (DeferredClientSetWieldedItemIndexPayloads)
            {
                DeferredClientSetWieldedItemIndexPayloads.Clear();
            }
            lock (DeferredClientSetAgentHealthPayloads)
            {
                DeferredClientSetAgentHealthPayloads.Clear();
            }
            lock (DeferredClientMakeAgentDeadPayloads)
            {
                DeferredClientMakeAgentDeadPayloads.Clear();
            }
            _nextDeferredClientCreateAgentSequence = 0;
            _nextDeferredClientSetAgentActionSetSequence = 0;
            _nextDeferredClientSynchronizeAgentEquipmentSequence = 0;
            _nextDeferredClientAttachWeaponToWeaponInAgentEquipmentSlotSequence = 0;
            _nextDeferredClientSetWeaponNetworkDataSequence = 0;
            _nextDeferredClientSetWeaponAmmoDataSequence = 0;
            _nextDeferredClientSetWeaponReloadPhaseSequence = 0;
            _nextDeferredClientStartSwitchingWeaponUsageIndexSequence = 0;
            _nextDeferredClientWeaponUsageIndexChangeSequence = 0;
            _nextDeferredClientSetWieldedItemIndexSequence = 0;
            _nextDeferredClientSetAgentHealthSequence = 0;
            _nextDeferredClientMakeAgentDeadSequence = 0;
            _lastSuppressedFollowSwitchKey = null;
            _lastSuppressedWeaponDropKey = null;
            _lastLocalVisualFinalizeKey = null;
            _lastSuppressedAssignFormationKey = null;
            _lastSuppressedLocalSelectAllFormationsKey = null;
            _lastObservedLocalSelectAllFormationsKey = null;
            _lastSuppressedServerSelectAllFormationsKey = null;
            _lastSuppressedOrderTroopPlacerTickKey = null;
            _lastSuppressedNonCommanderOrderUiKey = null;
            _lastRepairedOrderTroopPlacerOwnerKey = null;
            _lastPromotedLocalCommanderGeneralKey = null;
            _lastFinalizedLocalCommanderOrderControlKey = null;
            _lastMaintainedLocalCommanderOrderControlKey = null;
            _lastResolvedControlledEntryFallbackKey = null;
            _lastControlledEntryIdentityVerificationKey = null;
            _lastEstablishedCommanderStateBypassKey = null;
            _lastDeferredNonCommanderSuppressionKey = null;
            _lastAutoSelectedAllLocalCommanderFormationsKey = null;
            _lastPreFormationCommanderPromotionKey = null;
            _lastForcedCampaignCommanderOrderUiKey = null;
            _lastExactCommanderOrderVmStateKey = null;
            _lastRebuiltSingleplayerCommanderOrderUiKey = null;
            _lastExactCommanderOrderHotkeyFallbackKey = null;
            _lastExactCommanderFormationHotkeyFallbackKey = null;
            _lastExactCommanderOrderHotkeySuppressionKey = null;
            _lastExactCommanderDisabledAfterSetOrderUpdatesKey = null;
            _lastExactCommanderOrderBarMovieBindingKey = null;
            _lastForcedExactCommanderTroopSelectionInputsKey = null;
            _lastExactCommanderOrderItemExecutionKey = null;
            if (!preserveCommanderOrderControlState)
            {
                _suppressExactCommanderOrderHotkeyFallbackUntilRelease = false;
                _activeExactCommanderMissionOrderVm = null;
                _pendingLocalCommanderOrderControlFinalization = null;
            }
            ModLogger.Info(
                "BattleMapSpawnHandoffPatch: reset runtime state. " +
                "Source=" + (source ?? "unknown") +
                " PreserveCommanderOrderControlState=" + preserveCommanderOrderControlState);
        }

        private static void PatchMissionPeerFollowedAgent(Harmony harmony)
        {
            if (FollowedAgentField == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionPeer._followedAgent field not found. Skip.");
                return;
            }

            PropertyInfo property = typeof(MissionPeer).GetProperty(
                "FollowedAgent",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo setter = property?.GetSetMethod(nonPublic: true);
            MethodInfo prefix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionPeer_FollowedAgent_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (setter == null || prefix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionPeer.FollowedAgent setter not found. Skip.");
                return;
            }

            harmony.Patch(setter, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: prefix applied to MissionPeer.FollowedAgent.");
        }

        private static void PatchMissionNetworkComponentSetAgentPeer(Harmony harmony)
        {
            MethodInfo target = typeof(MissionNetworkComponent).GetMethod(
                "HandleServerEventSetAgentPeer",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo postfix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleServerEventSetAgentPeer_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || postfix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionNetworkComponent.HandleServerEventSetAgentPeer not found. Skip.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: postfix applied to MissionNetworkComponent.HandleServerEventSetAgentPeer.");
        }

        private static void PatchMissionNetworkComponentCreateAgent(Harmony harmony)
        {
            MethodInfo target = typeof(MissionNetworkComponent).GetMethod(
                "HandleServerEventCreateAgent",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo prefix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleServerEventCreateAgent_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo postfix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleServerEventCreateAgent_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo finalizer = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleServerEventCreateAgent_Finalizer),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null || postfix == null || finalizer == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionNetworkComponent.HandleServerEventCreateAgent not found. Skip.");
                return;
            }

            _missionNetworkComponentHandleServerEventCreateAgentMethod = target;

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix),
                postfix: new HarmonyMethod(postfix),
                finalizer: new HarmonyMethod(finalizer));
            ModLogger.Info("BattleMapSpawnHandoffPatch: prefix/postfix/finalizer applied to MissionNetworkComponent.HandleServerEventCreateAgent.");
        }

        private static void PatchMissionNetworkComponentSetAgentActionSet(Harmony harmony)
        {
            MethodInfo target = typeof(MissionNetworkComponent).GetMethod(
                "HandleServerEventSetAgentActionSet",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo prefix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleServerEventSetAgentActionSet_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionNetworkComponent.HandleServerEventSetAgentActionSet not found. Skip.");
                return;
            }

            _missionNetworkComponentHandleServerEventSetAgentActionSetMethod = target;
            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: prefix applied to MissionNetworkComponent.HandleServerEventSetAgentActionSet.");
        }

        private static void PatchMissionNetworkComponentSynchronizeAgentEquipment(Harmony harmony)
        {
            MethodInfo target = typeof(MissionNetworkComponent).GetMethod(
                "HandleServerEventSynchronizeAgentEquipment",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo prefix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleServerEventSynchronizeAgentEquipment_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo postfix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleServerEventSynchronizeAgentEquipment_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null || postfix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionNetworkComponent.HandleServerEventSynchronizeAgentEquipment not found. Skip.");
                return;
            }

            _missionNetworkComponentHandleServerEventSynchronizeAgentEquipmentMethod = target;

            harmony.Patch(target, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: prefix/postfix applied to MissionNetworkComponent.HandleServerEventSynchronizeAgentEquipment.");
        }

        private static void PatchMissionNetworkComponentAttachWeaponToWeaponInAgentEquipmentSlot(Harmony harmony)
        {
            MethodInfo target = typeof(MissionNetworkComponent).GetMethod(
                "HandleServerEventAttachWeaponToWeaponInAgentEquipmentSlot",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo prefix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleServerEventAttachWeaponToWeaponInAgentEquipmentSlot_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionNetworkComponent.HandleServerEventAttachWeaponToWeaponInAgentEquipmentSlot not found. Skip.");
                return;
            }

            _missionNetworkComponentHandleServerEventAttachWeaponToWeaponInAgentEquipmentSlotMethod = target;
            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: prefix applied to MissionNetworkComponent.HandleServerEventAttachWeaponToWeaponInAgentEquipmentSlot.");
        }

        private static void PatchMissionNetworkComponentSetWeaponNetworkData(Harmony harmony)
        {
            MethodInfo target = typeof(MissionNetworkComponent).GetMethod(
                "HandleServerEventSetWeaponNetworkData",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo prefix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleServerEventSetWeaponNetworkData_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionNetworkComponent.HandleServerEventSetWeaponNetworkData not found. Skip.");
                return;
            }

            _missionNetworkComponentHandleServerEventSetWeaponNetworkDataMethod = target;
            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: prefix applied to MissionNetworkComponent.HandleServerEventSetWeaponNetworkData.");
        }

        private static void PatchMissionNetworkComponentSetWeaponAmmoData(Harmony harmony)
        {
            MethodInfo target = typeof(MissionNetworkComponent).GetMethod(
                "HandleServerEventSetWeaponAmmoData",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo prefix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleServerEventSetWeaponAmmoData_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionNetworkComponent.HandleServerEventSetWeaponAmmoData not found. Skip.");
                return;
            }

            _missionNetworkComponentHandleServerEventSetWeaponAmmoDataMethod = target;
            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: prefix applied to MissionNetworkComponent.HandleServerEventSetWeaponAmmoData.");
        }

        private static void PatchMissionNetworkComponentSetWeaponReloadPhase(Harmony harmony)
        {
            MethodInfo target = typeof(MissionNetworkComponent).GetMethod(
                "HandleServerEventSetWeaponReloadPhase",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo prefix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleServerEventSetWeaponReloadPhase_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionNetworkComponent.HandleServerEventSetWeaponReloadPhase not found. Skip.");
                return;
            }

            _missionNetworkComponentHandleServerEventSetWeaponReloadPhaseMethod = target;
            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: prefix applied to MissionNetworkComponent.HandleServerEventSetWeaponReloadPhase.");
        }

        private static void PatchMissionNetworkComponentStartSwitchingWeaponUsageIndex(Harmony harmony)
        {
            MethodInfo target = typeof(MissionNetworkComponent).GetMethod(
                "HandleServerEventStartSwitchingWeaponUsageIndex",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo prefix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleServerEventStartSwitchingWeaponUsageIndex_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionNetworkComponent.HandleServerEventStartSwitchingWeaponUsageIndex not found. Skip.");
                return;
            }

            _missionNetworkComponentHandleServerEventStartSwitchingWeaponUsageIndexMethod = target;
            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: prefix applied to MissionNetworkComponent.HandleServerEventStartSwitchingWeaponUsageIndex.");
        }

        private static void PatchMissionNetworkComponentWeaponUsageIndexChangeMessage(Harmony harmony)
        {
            MethodInfo target = typeof(MissionNetworkComponent).GetMethod(
                "HandleServerEventWeaponUsageIndexChangeMessage",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo prefix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleServerEventWeaponUsageIndexChangeMessage_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionNetworkComponent.HandleServerEventWeaponUsageIndexChangeMessage not found. Skip.");
                return;
            }

            _missionNetworkComponentHandleServerEventWeaponUsageIndexChangeMessageMethod = target;
            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: prefix applied to MissionNetworkComponent.HandleServerEventWeaponUsageIndexChangeMessage.");
        }

        private static void PatchMissionNetworkComponentSetAgentHealth(Harmony harmony)
        {
            MethodInfo target = typeof(MissionNetworkComponent).GetMethod(
                "HandleServerEventSetAgentHealth",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo prefix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleServerEventSetAgentHealth_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo postfix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleServerEventSetAgentHealth_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null || postfix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionNetworkComponent.HandleServerEventSetAgentHealth not found. Skip.");
                return;
            }

            _missionNetworkComponentHandleServerEventSetAgentHealthMethod = target;
            harmony.Patch(target, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: prefix/postfix applied to MissionNetworkComponent.HandleServerEventSetAgentHealth.");
        }

        private static void PatchMissionNetworkComponentMakeAgentDead(Harmony harmony)
        {
            MethodInfo target = typeof(MissionNetworkComponent).GetMethod(
                "HandleServerEventMakeAgentDead",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo prefix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleServerEventMakeAgentDead_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionNetworkComponent.HandleServerEventMakeAgentDead not found. Skip.");
                return;
            }

            _missionNetworkComponentHandleServerEventMakeAgentDeadMethod = target;
            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: prefix applied to MissionNetworkComponent.HandleServerEventMakeAgentDead.");
        }

        private static void PatchMissionNetworkComponentSetWieldedItemIndex(Harmony harmony)
        {
            MethodInfo target = typeof(MissionNetworkComponent).GetMethod(
                "HandleServerEventSetWieldedItemIndex",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo prefix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleServerEventSetWieldedItemIndex_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo postfix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleServerEventSetWieldedItemIndex_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo finalizer = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleServerEventSetWieldedItemIndex_Finalizer),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null || postfix == null || finalizer == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionNetworkComponent.HandleServerEventSetWieldedItemIndex not found. Skip.");
                return;
            }

            _missionNetworkComponentHandleServerEventSetWieldedItemIndexMethod = target;

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix),
                postfix: new HarmonyMethod(postfix),
                finalizer: new HarmonyMethod(finalizer));
            ModLogger.Info("BattleMapSpawnHandoffPatch: prefix/postfix/finalizer applied to MissionNetworkComponent.HandleServerEventSetWieldedItemIndex.");
        }

        private static void PatchMissionNetworkComponentSpawnWeaponAsDropFromAgent(Harmony harmony)
        {
            MethodInfo target = typeof(MissionNetworkComponent).GetMethod(
                "HandleServerEventSpawnWeaponAsDropFromAgent",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo prefix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleServerEventSpawnWeaponAsDropFromAgent_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionNetworkComponent.HandleServerEventSpawnWeaponAsDropFromAgent not found. Skip.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: prefix applied to MissionNetworkComponent.HandleServerEventSpawnWeaponAsDropFromAgent.");
        }

        private static void PatchMissionNetworkComponentAssignFormationToPlayer(Harmony harmony)
        {
            MethodInfo target = typeof(MissionNetworkComponent).GetMethod(
                "HandleServerEventAssignFormationToPlayer",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo prefix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleServerEventAssignFormationToPlayer_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo postfix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleServerEventAssignFormationToPlayer_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null || postfix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionNetworkComponent.HandleServerEventAssignFormationToPlayer not found. Skip.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: prefix/postfix applied to MissionNetworkComponent.HandleServerEventAssignFormationToPlayer.");
        }

        private static void PatchMissionNetworkComponentSelectAllFormations(Harmony harmony)
        {
            MethodInfo target = typeof(MissionNetworkComponent).GetMethod(
                "HandleClientEventSelectAllFormations",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo prefix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleClientEventSelectAllFormations_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionNetworkComponent.HandleClientEventSelectAllFormations not found. Skip.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: prefix applied to MissionNetworkComponent.HandleClientEventSelectAllFormations.");
        }

        private static void PatchMissionMultiplayerGameModeFlagDominationClientBotsControlledChanged(Harmony harmony)
        {
            MethodInfo target = typeof(MissionMultiplayerGameModeFlagDominationClient).GetMethod(
                "OnBotsControlledChanged",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(MissionPeer), typeof(int), typeof(int) },
                modifiers: null);
            MethodInfo postfix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionMultiplayerGameModeFlagDominationClient_OnBotsControlledChanged_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || postfix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionMultiplayerGameModeFlagDominationClient.OnBotsControlledChanged not found. Skip.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: postfix applied to MissionMultiplayerGameModeFlagDominationClient.OnBotsControlledChanged.");
        }

        private static void PatchMissionGauntletMultiplayerOrderUIHandlerMissionScreenTick(Harmony harmony)
        {
            Type targetType = AccessTools.TypeByName(
                "TaleWorlds.MountAndBlade.Multiplayer.GauntletUI.Mission.MissionGauntletMultiplayerOrderUIHandler");
            MethodInfo target = targetType?.GetMethod(
                "OnMissionScreenTick",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(float) },
                modifiers: null);
            MethodInfo postfix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionGauntletMultiplayerOrderUIHandler_OnMissionScreenTick_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || postfix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionGauntletMultiplayerOrderUIHandler.OnMissionScreenTick not found. Skip.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: postfix applied to MissionGauntletMultiplayerOrderUIHandler.OnMissionScreenTick.");
        }

        private static void PatchOrderTroopPlacerMissionScreenTick(Harmony harmony)
        {
            Type targetType = AccessTools.TypeByName(
                "TaleWorlds.MountAndBlade.View.MissionViews.Order.OrderTroopPlacer");
            MethodInfo target = targetType?.GetMethod(
                "OnMissionScreenTick",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(float) },
                modifiers: null);
            MethodInfo prefix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(OrderTroopPlacer_OnMissionScreenTick_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: OrderTroopPlacer.OnMissionScreenTick not found. Skip.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: prefix applied to OrderTroopPlacer.OnMissionScreenTick.");
        }

        private static void PatchMissionOrderVmViewOrders(Harmony harmony)
        {
            Type targetType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.ViewModelCollection.Order.MissionOrderVM");
            MethodInfo target = targetType?.GetMethod(
                "ViewOrders",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null);
            MethodInfo postfix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionOrderVM_ViewOrders_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || postfix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionOrderVM.ViewOrders not found. Skip.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: postfix applied to MissionOrderVM.ViewOrders.");
        }

        private static void PatchMissionOrderVmOpenToggleOrder(Harmony harmony)
        {
            Type targetType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.ViewModelCollection.Order.MissionOrderVM");
            MethodInfo target = targetType?.GetMethod(
                "OpenToggleOrder",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(bool), typeof(bool) },
                modifiers: null);
            MethodInfo postfix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionOrderVM_OpenToggleOrder_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || postfix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionOrderVM.OpenToggleOrder(bool,bool) not found. Skip.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: postfix applied to MissionOrderVM.OpenToggleOrder(bool,bool).");
        }

        private static void PatchMissionOrderVmTryCloseToggleOrder(Harmony harmony)
        {
            Type targetType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.ViewModelCollection.Order.MissionOrderVM");
            MethodInfo target = targetType?.GetMethod(
                "TryCloseToggleOrder",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(bool) },
                modifiers: null);
            MethodInfo postfix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionOrderVM_TryCloseToggleOrder_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || postfix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionOrderVM.TryCloseToggleOrder(bool) not found. Skip.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: postfix applied to MissionOrderVM.TryCloseToggleOrder(bool).");
        }

        private static void PatchMissionOrderVmOnOrderExecuted(Harmony harmony)
        {
            Type missionOrderVmType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.ViewModelCollection.Order.MissionOrderVM");
            Type orderItemVmType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.ViewModelCollection.Order.OrderItemVM");
            MethodInfo target = missionOrderVmType?.GetMethod(
                "OnOrderExecuted",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: orderItemVmType != null ? new[] { orderItemVmType } : Type.EmptyTypes,
                modifiers: null);
            MethodInfo postfix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionOrderVM_OnOrderExecuted_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || postfix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionOrderVM.OnOrderExecuted(OrderItemVM) not found. Skip.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: postfix applied to MissionOrderVM.OnOrderExecuted(OrderItemVM).");
        }

        private static void PatchOrderItemVmOnExecuteAction(Harmony harmony)
        {
            Type orderItemVmType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.ViewModelCollection.Order.OrderItemVM");
            Type visualOrderExecutionParametersType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.ViewModelCollection.Order.VisualOrderExecutionParameters");
            MethodInfo target = null;
            if (orderItemVmType != null && visualOrderExecutionParametersType != null)
            {
                target = orderItemVmType.GetMethod(
                    "OnExecuteAction",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { visualOrderExecutionParametersType },
                    modifiers: null);
            }

            if (target == null && orderItemVmType != null)
            {
                foreach (MethodInfo candidate in orderItemVmType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (string.Equals(candidate.Name, "OnExecuteAction", StringComparison.Ordinal) &&
                        candidate.GetParameters().Length == 1)
                    {
                        target = candidate;
                        break;
                    }
                }
            }

            MethodInfo postfix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(OrderItemVM_OnExecuteAction_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || postfix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: OrderItemVM.OnExecuteAction(...) not found. Skip.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: postfix applied to OrderItemVM.OnExecuteAction(...).");
        }

        private static void PatchOrderControllerSelectAllFormations(Harmony harmony)
        {
            MethodInfo target = typeof(OrderController).GetMethod(
                "SelectAllFormations",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(Agent), typeof(bool) },
                modifiers: null);
            MethodInfo prefix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(OrderController_SelectAllFormations_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: OrderController.SelectAllFormations(Agent,bool) not found. Skip.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: prefix applied to OrderController.SelectAllFormations(Agent,bool).");
        }

        private static bool MissionPeer_FollowedAgent_Prefix(MissionPeer __instance, Agent value)
        {
            try
            {
                if (!ShouldSuppressLocalFollowSwitch(__instance, value, out string logKey, out string logDetails))
                    return true;

                FollowedAgentField.SetValue(__instance, value);
                if (!string.Equals(_lastSuppressedFollowSwitchKey, logKey, StringComparison.Ordinal))
                {
                    _lastSuppressedFollowSwitchKey = logKey;
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: suppressed local MissionPeer.FollowedAgent network echo during battle-map spawn handshake. " +
                        logDetails);
                }

                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionPeer.FollowedAgent prefix failed open: " + ex.Message);
                return true;
            }
        }

        private static void MissionNetworkComponent_HandleServerEventSetAgentPeer_Postfix(GameNetworkMessage baseMessage)
        {
            try
            {
                if (!(baseMessage is SetAgentPeer setAgentPeer))
                    return;

                Mission mission = Mission.Current;
                if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                    return;

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(setAgentPeer.AgentIndex, canBeNull: true);
                NetworkCommunicator payloadPeer = setAgentPeer.Peer;
                MissionPeer payloadMissionPeer = payloadPeer?.GetComponent<MissionPeer>();
                MissionPeer agentMissionPeer = agent?.MissionPeer;
                MissionPeer effectiveMissionPeer = payloadMissionPeer ?? agentMissionPeer;
                if (agent == null || !agent.IsActive())
                    return;

                MissionPeer localMissionPeer = GameNetwork.MyPeer?.GetComponent<MissionPeer>();
                bool isLocalPayloadPeer =
                    payloadPeer?.IsMine == true ||
                    (effectiveMissionPeer != null && ReferenceEquals(effectiveMissionPeer, localMissionPeer));
                if (!isLocalPayloadPeer)
                {
                    ExactTransferContractRuntimeCache.ObserveClientPeerBound(
                        agent.Index,
                        "battle-map handoff SetAgentPeer remote");
                    bool remoteDeferImmediateExactVisualFinalize =
                        ShouldDeferImmediateClientExactVisualFinalize(agent);
                    CoopMissionSpawnLogic.TryTrackClientMountedHeroMountAgentIndex(agent);
                    bool remoteExactVisualApplied = CoopMissionSpawnLogic.TryFinalizeClientExactCampaignVisualForAgent(
                        mission,
                        agent,
                        preferredEntryId: null,
                        source: "battle-map handoff SetAgentPeer remote",
                        includeWeaponsForClientRefresh: true,
                        allowImmediateApply: !remoteDeferImmediateExactVisualFinalize);
                    bool remoteTroopExactVisualApplied = false;
                    if (!remoteExactVisualApplied)
                    {
                        remoteTroopExactVisualApplied = CoopMissionSpawnLogic.TryFinalizeClientExactCampaignTroopVisualForPeerAgent(
                            mission,
                            agent,
                            "battle-map handoff SetAgentPeer remote",
                            includeWeaponsForClientRefresh: true);
                    }
                    if (remoteExactVisualApplied && !remoteDeferImmediateExactVisualFinalize)
                        agent.MountAgent?.UpdateAgentProperties();

                    if (remoteExactVisualApplied || remoteTroopExactVisualApplied)
                    {
                        ModLogger.Info(
                            "BattleMapSpawnHandoffPatch: applied remote peer agent exact visuals after SetAgentPeer for battle-map handoff. " +
                            "AgentIndex=" + agent.Index +
                            " PeerIndex=" + (setAgentPeer.Peer?.Index.ToString() ?? "null") +
                            " HasSpawnEquipment=" + (agent.SpawnEquipment != null) +
                            " MountAgentIndex=" + (agent.MountAgent?.Index.ToString() ?? "null") +
                            " ExactVisualApplied=" + remoteExactVisualApplied +
                            " TroopExactVisualApplied=" + remoteTroopExactVisualApplied +
                            " DeferredImmediateExactVisualFinalize=" + remoteDeferImmediateExactVisualFinalize +
                            " Mission=" + (mission.SceneName ?? "null"));
                    }

                    CoopMissionSpawnLogic.TraceClientMountedHeroNetworkContract(
                        agent,
                        "client-set-agent-peer-remote",
                        "battle-map handoff SetAgentPeer remote",
                        "PayloadPeerIndex=" + (setAgentPeer.Peer?.Index.ToString() ?? "null") +
                        " DeferredImmediateExactVisualFinalize=" + remoteDeferImmediateExactVisualFinalize +
                        " ExactVisualApplied=" + remoteExactVisualApplied +
                        " TroopExactVisualApplied=" + remoteTroopExactVisualApplied);
                    return;
                }

                CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot selectionBridge =
                    CoopBattleSelectionBridgeFile.ReadCurrentSelection();
                CoopBattleEntryPolicy.ClientSnapshot entryPolicy =
                    CoopBattleEntryPolicy.BuildClientSnapshot(mission, selectionBridge);
                if (entryPolicy == null || !entryPolicy.UseAuthoritativeTroopPath)
                    return;

                MissionPeer missionPeer = effectiveMissionPeer ?? localMissionPeer;
                if (missionPeer == null)
                {
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: skipped local SetAgentPeer exact visual finalization because no local MissionPeer could be resolved. " +
                        "AgentIndex=" + agent.Index +
                        " PayloadPeerIndex=" + (payloadPeer?.Index.ToString() ?? "null") +
                        " AgentMissionPeer=" + (agentMissionPeer?.Peer?.Index.ToString() ?? "null") +
                        " Mission=" + (mission.SceneName ?? "null"));
                    return;
                }

                string preferredEntryId = ResolveControlledEntryId(
                    missionPeer,
                    agent,
                    mission,
                    entryPolicy.BridgeTroopOrEntryId,
                    out string entryResolutionSource);
                string logKey =
                    agent.Index + "|" +
                    (agent.SpawnEquipment != null).ToString() + "|" +
                    (agent.MountAgent?.Index.ToString() ?? "none") + "|" +
                    (preferredEntryId ?? entryPolicy.BridgeTroopOrEntryId ?? "none");
                if (string.Equals(_lastLocalVisualFinalizeKey, logKey, StringComparison.Ordinal))
                    return;

                ExactTransferContractRuntimeCache.ObserveClientPeerBound(
                    agent.Index,
                    "battle-map handoff SetAgentPeer");
                bool deferImmediateExactVisualFinalize =
                    ShouldDeferImmediateClientExactVisualFinalize(agent);
                bool exactVisualApplied = CoopMissionSpawnLogic.TryFinalizeClientExactCampaignVisualForAgent(
                    mission,
                    agent,
                    preferredEntryId,
                    "battle-map handoff SetAgentPeer",
                    includeWeaponsForClientRefresh: true,
                    allowImmediateApply: !deferImmediateExactVisualFinalize);
                bool troopExactVisualApplied = false;
                if (!exactVisualApplied)
                {
                    troopExactVisualApplied = CoopMissionSpawnLogic.TryFinalizeClientExactCampaignTroopVisualForPeerAgent(
                        mission,
                        agent,
                        "battle-map handoff SetAgentPeer",
                        includeWeaponsForClientRefresh: true);
                }
                if (exactVisualApplied && !deferImmediateExactVisualFinalize)
                    agent.MountAgent?.UpdateAgentProperties();

                _lastLocalVisualFinalizeKey = logKey;
                ModLogger.Info(
                    "BattleMapSpawnHandoffPatch: processed local player agent exact visuals after SetAgentPeer for battle-map handoff. " +
                    "AgentIndex=" + agent.Index +
                    " HasSpawnEquipment=" + (agent.SpawnEquipment != null) +
                    " MountAgentIndex=" + (agent.MountAgent?.Index.ToString() ?? "null") +
                    " ExactVisualApplied=" + exactVisualApplied +
                    " TroopExactVisualApplied=" + troopExactVisualApplied +
                    " DeferredImmediateExactVisualFinalize=" + deferImmediateExactVisualFinalize +
                    " PreferredEntryId=" + (preferredEntryId ?? "null") +
                    " EntryResolutionSource=" + (entryResolutionSource ?? "null") +
                    " BridgeTroop=" + (entryPolicy.BridgeTroopOrEntryId ?? "null") +
                    " Mission=" + (mission.SceneName ?? "null"));
                CoopMissionSpawnLogic.TraceClientMountedHeroNetworkContract(
                    agent,
                    "client-set-agent-peer",
                    "battle-map handoff SetAgentPeer",
                    "PayloadPeerIndex=" + (setAgentPeer.Peer?.Index.ToString() ?? "null") +
                    " PreferredEntryId=" + (preferredEntryId ?? "null") +
                    " DeferredImmediateExactVisualFinalize=" + deferImmediateExactVisualFinalize +
                    " ExactVisualApplied=" + exactVisualApplied +
                    " TroopExactVisualApplied=" + troopExactVisualApplied);
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: local SetAgentPeer visual finalization failed: " + ex.Message);
            }
        }

        private static bool MissionNetworkComponent_HandleServerEventCreateAgent_Prefix(GameNetworkMessage baseMessage)
        {
            try
            {
                if (!(baseMessage is CreateAgent createAgent))
                    return true;

                Mission mission = Mission.Current;
                if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                    return true;

                bool hasMountPayload = createAgent.MountAgentIndex >= 0;
                if (hasMountPayload)
                {
                    CoopMissionSpawnLogic.TryTrackClientMountedHeroMountAgentIndexFromPayload(
                        createAgent.AgentIndex,
                        createAgent.MountAgentIndex);
                }

                bool snapshotReadyForExactHeroHandoff =
                    CoopMissionNetworkBridge.IsClientCurrentBattleSnapshotApplied(out string snapshotReadinessSummary);
                bool safeStringIdCreateAgentPathActive =
                    ShouldUseSafeStringIdCreateAgentPathOnClient(mission);
                bool strictExactCandidate = false;
                bool mountedHeroPayloadCandidate = IsMountedHeroTemplatePayload(createAgent);
                ExactCreateAgentCorridorDiagnostics.ObserveClientCreateAgentPrefix(
                    createAgent,
                    snapshotReadyForExactHeroHandoff,
                    snapshotReadinessSummary,
                    strictExactCandidate,
                    mountedHeroPayloadCandidate,
                    "battle-map handoff CreateAgent prefix");
                CoopMissionSpawnLogic.ObserveClientCreateAgentPayloadResolvedEntry(
                    createAgent,
                    "battle-map handoff CreateAgent prefix");
                if (safeStringIdCreateAgentPathActive && !snapshotReadyForExactHeroHandoff)
                {
                    RegisterDeferredClientCreateAgentPayload(createAgent, snapshotReadinessSummary);
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: deferred client CreateAgent until current battle snapshot is applied. " +
                        "AgentIndex=" + createAgent.AgentIndex +
                        " MountAgentIndex=" + createAgent.MountAgentIndex +
                        " PayloadCharacter=" + (createAgent.Character?.StringId ?? "null") +
                        " Reason=" + (snapshotReadinessSummary ?? "unknown"));
                    ExactCreateAgentCorridorDiagnostics.ObserveClientCreateAgentBypass(
                        createAgent,
                        "deferred-generic-create-agent-snapshot-not-ready:" + (snapshotReadinessSummary ?? "unknown"),
                        "battle-map handoff CreateAgent prefix");
                    return false;
                }

                if (!safeStringIdCreateAgentPathActive && snapshotReadyForExactHeroHandoff)
                {
                    if (TryHandleStrictExactHeroCreateAgentViaContract(
                            mission,
                            createAgent,
                            out strictExactCandidate))
                    {
                        ExactCreateAgentCorridorDiagnostics.ObserveClientCreateAgentBypass(
                            createAgent,
                            "strict-exact-contract-adapter",
                            "battle-map handoff CreateAgent prefix");
                        return false;
                    }

                    if (TryHandleMountedHeroCreateAgentViaPayloadAdapter(
                            mission,
                            createAgent,
                            out mountedHeroPayloadCandidate))
                    {
                        ExactCreateAgentCorridorDiagnostics.ObserveClientCreateAgentBypass(
                            createAgent,
                            "mounted-hero-payload-adapter",
                            "battle-map handoff CreateAgent prefix");
                        return false;
                    }
                }
                else if (!safeStringIdCreateAgentPathActive && mountedHeroPayloadCandidate)
                {
                    RegisterDeferredMountedHeroCreateAgentPayload(createAgent, snapshotReadinessSummary);
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: deferred snapshot-dependent mounted hero CreateAgent handoff until current battle snapshot is applied. " +
                        "AgentIndex=" + createAgent.AgentIndex +
                        " MountAgentIndex=" + createAgent.MountAgentIndex +
                        " PayloadCharacter=" + (createAgent.Character?.StringId ?? "null") +
                        " Reason=" + snapshotReadinessSummary);
                    ExactCreateAgentCorridorDiagnostics.ObserveClientCreateAgentBypass(
                        createAgent,
                        "deferred-mounted-hero-snapshot-not-ready:" + (snapshotReadinessSummary ?? "unknown"),
                        "battle-map handoff CreateAgent prefix");
                    return false;
                }

                if (!hasMountPayload || strictExactCandidate)
                    return true;

                if (!mountedHeroPayloadCandidate || !snapshotReadyForExactHeroHandoff)
                    CanonicalizeCreateAgentPayloadForBattleMap(createAgent);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: CreateAgent prefix mount-payload tracking failed open: " + ex.Message);
                return true;
            }
        }

        private static bool MissionNetworkComponent_HandleServerEventSetAgentActionSet_Prefix(GameNetworkMessage baseMessage)
        {
            try
            {
                if (!(baseMessage is SetAgentActionSet setAgentActionSet))
                    return true;

                Mission mission = Mission.Current;
                if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                    return true;

                if (!ShouldUseSafeStringIdCreateAgentPathOnClient(mission))
                    return true;

                if (!CoopMissionNetworkBridge.IsClientCurrentBattleSnapshotApplied(out string snapshotReadinessSummary))
                {
                    RegisterDeferredClientSetAgentActionSetPayload(
                        setAgentActionSet,
                        "snapshot-not-ready:" + (snapshotReadinessSummary ?? "unknown"));
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: deferred client SetAgentActionSet until current battle snapshot is applied. " +
                        "AgentIndex=" + setAgentActionSet.AgentIndex +
                        " Reason=" + (snapshotReadinessSummary ?? "unknown"));
                    return false;
                }

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(setAgentActionSet.AgentIndex, canBeNull: true);
                if (agent == null &&
                    (HasDeferredClientCreateAgentPayload(setAgentActionSet.AgentIndex) ||
                     HasAnyDeferredClientAgentBootstrapPayload(setAgentActionSet.AgentIndex)))
                {
                    RegisterDeferredClientSetAgentActionSetPayload(
                        setAgentActionSet,
                        "agent-bootstrap-deferred");
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: deferred client SetAgentActionSet because agent bootstrap is still deferred. " +
                        "AgentIndex=" + setAgentActionSet.AgentIndex);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: SetAgentActionSet prefix failed open: " + ex.Message);
                return true;
            }
        }

        private static bool ShouldUseSafeStringIdCreateAgentPathOnClient(Mission mission)
        {
            if (GameNetwork.IsServer || mission == null)
                return false;

            if (CoopMissionSpawnLogic.UseDedicatedSafeStringIdExactEquipmentPathOnClient())
                return true;

            string sceneName = mission.SceneName ?? string.Empty;
            return
                MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(sceneName) &&
                SceneRuntimeClassifier.IsCampaignBattleScene(sceneName);
        }

        private static bool HasAnyDeferredClientAgentBootstrapPayload(int agentIndex)
        {
            if (agentIndex < 0)
                return false;

            lock (DeferredClientSetAgentActionSetPayloads)
            {
                if (DeferredClientSetAgentActionSetPayloads.Any(candidate => candidate?.Message?.AgentIndex == agentIndex))
                    return true;
            }

            lock (DeferredClientSynchronizeAgentEquipmentPayloads)
            {
                if (DeferredClientSynchronizeAgentEquipmentPayloads.Any(candidate => candidate?.Message?.AgentIndex == agentIndex))
                    return true;
            }

            lock (DeferredClientAttachWeaponToWeaponInAgentEquipmentSlotPayloads)
            {
                if (DeferredClientAttachWeaponToWeaponInAgentEquipmentSlotPayloads.Any(candidate => candidate?.Message?.AgentIndex == agentIndex))
                    return true;
            }

            lock (DeferredClientSetWeaponNetworkDataPayloads)
            {
                if (DeferredClientSetWeaponNetworkDataPayloads.Any(candidate => candidate?.Message?.AgentIndex == agentIndex))
                    return true;
            }

            lock (DeferredClientSetWeaponAmmoDataPayloads)
            {
                if (DeferredClientSetWeaponAmmoDataPayloads.Any(candidate => candidate?.Message?.AgentIndex == agentIndex))
                    return true;
            }

            lock (DeferredClientSetWeaponReloadPhasePayloads)
            {
                if (DeferredClientSetWeaponReloadPhasePayloads.Any(candidate => candidate?.Message?.AgentIndex == agentIndex))
                    return true;
            }

            lock (DeferredClientStartSwitchingWeaponUsageIndexPayloads)
            {
                if (DeferredClientStartSwitchingWeaponUsageIndexPayloads.Any(candidate => candidate?.Message?.AgentIndex == agentIndex))
                    return true;
            }

            lock (DeferredClientWeaponUsageIndexChangePayloads)
            {
                if (DeferredClientWeaponUsageIndexChangePayloads.Any(candidate => candidate?.Message?.AgentIndex == agentIndex))
                    return true;
            }

            lock (DeferredClientSetWieldedItemIndexPayloads)
            {
                if (DeferredClientSetWieldedItemIndexPayloads.Any(candidate => candidate?.Message?.AgentIndex == agentIndex))
                    return true;
            }

            lock (DeferredClientMakeAgentDeadPayloads)
            {
                if (DeferredClientMakeAgentDeadPayloads.Any(candidate => candidate?.Message?.AgentIndex == agentIndex))
                    return true;
            }

            return false;
        }

        internal static void ClearDeferredClientMountedHeroCreateAgents(string source)
        {
            int clearedCount;
            lock (DeferredMountedHeroCreateAgentPayloads)
            {
                clearedCount = DeferredMountedHeroCreateAgentPayloads.Count;
                DeferredMountedHeroCreateAgentPayloads.Clear();
            }

            if (clearedCount <= 0)
                return;

            ModLogger.Info(
                "BattleMapSpawnHandoffPatch: cleared deferred mounted hero CreateAgent payloads. " +
                "Count=" + clearedCount +
                " Source=" + (source ?? "unknown"));
        }

        internal static void TryProcessDeferredClientCreateAgentMessages(Mission mission, string source)
        {
            if (!GameNetwork.IsClient ||
                mission == null ||
                !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
            {
                return;
            }

            if (!CoopMissionNetworkBridge.IsClientCurrentBattleSnapshotApplied(out string snapshotReadinessSummary))
                return;

            TryReplayDeferredClientCreateAgents(mission, source, snapshotReadinessSummary);
            TryReplayDeferredClientSetAgentActionSet(mission, source, snapshotReadinessSummary);
            TryReplayDeferredClientSynchronizeAgentEquipment(mission, source, snapshotReadinessSummary);
            TryReplayDeferredClientAttachWeaponToWeaponInAgentEquipmentSlot(mission, source, snapshotReadinessSummary);
            TryReplayDeferredClientSetWeaponNetworkData(mission, source, snapshotReadinessSummary);
            TryReplayDeferredClientSetWeaponAmmoData(mission, source, snapshotReadinessSummary);
            TryReplayDeferredClientSetWeaponReloadPhase(mission, source, snapshotReadinessSummary);
            TryReplayDeferredClientSetWieldedItemIndex(mission, source, snapshotReadinessSummary);
            TryReplayDeferredClientStartSwitchingWeaponUsageIndex(mission, source, snapshotReadinessSummary);
            TryReplayDeferredClientWeaponUsageIndexChangeMessage(mission, source, snapshotReadinessSummary);
            TryReplayDeferredClientSetAgentHealth(mission, source, snapshotReadinessSummary);
            TryReplayDeferredClientMakeAgentDead(mission, source, snapshotReadinessSummary);
        }

        internal static void TryProcessDeferredClientMountedHeroCreateAgents(Mission mission, string source)
        {
            if (!GameNetwork.IsClient ||
                mission == null ||
                !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
            {
                return;
            }

            if (!CoopMissionNetworkBridge.IsClientCurrentBattleSnapshotApplied(out string snapshotReadinessSummary))
                return;

            List<DeferredMountedHeroCreateAgentPayload> deferredPayloads;
            lock (DeferredMountedHeroCreateAgentPayloads)
            {
                if (DeferredMountedHeroCreateAgentPayloads.Count <= 0)
                    return;

                deferredPayloads = new List<DeferredMountedHeroCreateAgentPayload>(DeferredMountedHeroCreateAgentPayloads.Values);
            }

            DateTime nowUtc = DateTime.UtcNow;
            foreach (DeferredMountedHeroCreateAgentPayload deferredPayload in deferredPayloads)
            {
                CreateAgent createAgent = deferredPayload?.Message;
                if (createAgent == null)
                    continue;

                if (deferredPayload.LastAttemptUtc != DateTime.MinValue &&
                    nowUtc - deferredPayload.LastAttemptUtc < TimeSpan.FromMilliseconds(250))
                {
                    continue;
                }
                Agent existingAgent = Mission.MissionNetworkHelper.GetAgentFromIndex(createAgent.AgentIndex, canBeNull: true);
                if (existingAgent != null && existingAgent.IsActive())
                {
                    RemoveDeferredMountedHeroCreateAgentPayload(createAgent.AgentIndex);
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: dropped deferred mounted hero CreateAgent because agent already exists. " +
                        "AgentIndex=" + createAgent.AgentIndex +
                        " Source=" + (source ?? "unknown"));
                    continue;
                }

                deferredPayload.LastAttemptUtc = nowUtc;
                deferredPayload.Attempts++;

                bool strictExactCandidate = false;
                bool handled = TryHandleStrictExactHeroCreateAgentViaContract(
                    mission,
                    createAgent,
                    out strictExactCandidate);
                if (!handled && !strictExactCandidate)
                {
                    handled = TryHandleMountedHeroCreateAgentViaPayloadAdapter(
                        mission,
                        createAgent,
                        out bool _);
                }
                if (!handled)
                {
                    if (deferredPayload.Attempts == 1 || deferredPayload.Attempts % 20 == 0)
                    {
                        ModLogger.Info(
                            "BattleMapSpawnHandoffPatch: deferred mounted hero CreateAgent still waiting for safe materialization path. " +
                            "AgentIndex=" + createAgent.AgentIndex +
                            " MountAgentIndex=" + createAgent.MountAgentIndex +
                            " PayloadCharacter=" + (createAgent.Character?.StringId ?? "null") +
                            " Attempts=" + deferredPayload.Attempts +
                            " StrictExactCandidate=" + strictExactCandidate +
                            " SnapshotReadiness=" + snapshotReadinessSummary +
                            " Source=" + (source ?? "unknown"));
                    }

                    continue;
                }

                RemoveDeferredMountedHeroCreateAgentPayload(createAgent.AgentIndex);
                ModLogger.Info(
                    "BattleMapSpawnHandoffPatch: materialized deferred mounted hero CreateAgent after battle snapshot apply. " +
                    "AgentIndex=" + createAgent.AgentIndex +
                    " MountAgentIndex=" + createAgent.MountAgentIndex +
                    " PayloadCharacter=" + (createAgent.Character?.StringId ?? "null") +
                    " Attempts=" + deferredPayload.Attempts +
                    " Source=" + (source ?? "unknown"));
            }
        }

        private static void RegisterDeferredClientCreateAgentPayload(CreateAgent createAgent, string snapshotReadinessSummary)
        {
            if (createAgent == null)
                return;

            lock (DeferredClientCreateAgentPayloads)
            {
                DeferredClientCreateAgentPayload existingPayload = DeferredClientCreateAgentPayloads
                    .FirstOrDefault(candidate => candidate?.Message?.AgentIndex == createAgent.AgentIndex);
                if (existingPayload != null)
                {
                    existingPayload.Message = createAgent;
                    existingPayload.DeferralReason = snapshotReadinessSummary;
                    return;
                }

                DeferredClientCreateAgentPayloads.Add(
                    new DeferredClientCreateAgentPayload
                    {
                        Sequence = ++_nextDeferredClientCreateAgentSequence,
                        Message = createAgent,
                        DeferredUtc = DateTime.UtcNow,
                        LastAttemptUtc = DateTime.MinValue,
                        Attempts = 0,
                        DeferralReason = snapshotReadinessSummary
                    });
            }
        }

        private static bool HasDeferredClientCreateAgentPayload(int agentIndex)
        {
            if (agentIndex < 0)
                return false;

            lock (DeferredClientCreateAgentPayloads)
            {
                return DeferredClientCreateAgentPayloads.Any(candidate => candidate?.Message?.AgentIndex == agentIndex);
            }
        }

        private static void RegisterDeferredClientSynchronizeAgentEquipmentPayload(
            SynchronizeAgentSpawnEquipment synchronizeAgentSpawnEquipment,
            string deferralReason)
        {
            if (synchronizeAgentSpawnEquipment == null)
                return;

            lock (DeferredClientSynchronizeAgentEquipmentPayloads)
            {
                DeferredClientSynchronizeAgentEquipmentPayload existingPayload = DeferredClientSynchronizeAgentEquipmentPayloads
                    .FirstOrDefault(candidate => candidate?.Message?.AgentIndex == synchronizeAgentSpawnEquipment.AgentIndex);
                if (existingPayload != null)
                {
                    existingPayload.Message = synchronizeAgentSpawnEquipment;
                    existingPayload.DeferralReason = deferralReason;
                    return;
                }

                DeferredClientSynchronizeAgentEquipmentPayloads.Add(
                    new DeferredClientSynchronizeAgentEquipmentPayload
                    {
                        Sequence = ++_nextDeferredClientSynchronizeAgentEquipmentSequence,
                        Message = synchronizeAgentSpawnEquipment,
                        DeferredUtc = DateTime.UtcNow,
                        LastAttemptUtc = DateTime.MinValue,
                        Attempts = 0,
                        DeferralReason = deferralReason
                    });
            }
        }

        private static void RegisterDeferredClientSetAgentActionSetPayload(
            SetAgentActionSet setAgentActionSet,
            string deferralReason)
        {
            if (setAgentActionSet == null)
                return;

            lock (DeferredClientSetAgentActionSetPayloads)
            {
                DeferredClientSetAgentActionSetPayload existingPayload = DeferredClientSetAgentActionSetPayloads
                    .FirstOrDefault(candidate => candidate?.Message?.AgentIndex == setAgentActionSet.AgentIndex);
                if (existingPayload != null)
                {
                    existingPayload.Message = setAgentActionSet;
                    existingPayload.DeferralReason = deferralReason;
                    return;
                }

                DeferredClientSetAgentActionSetPayloads.Add(
                    new DeferredClientSetAgentActionSetPayload
                    {
                        Sequence = ++_nextDeferredClientSetAgentActionSetSequence,
                        Message = setAgentActionSet,
                        DeferredUtc = DateTime.UtcNow,
                        LastAttemptUtc = DateTime.MinValue,
                        Attempts = 0,
                        DeferralReason = deferralReason
                    });
            }
        }

        private static void RegisterDeferredClientSetWieldedItemIndexPayload(
            SetWieldedItemIndex setWieldedItemIndex,
            string deferralReason)
        {
            if (setWieldedItemIndex == null)
                return;

            lock (DeferredClientSetWieldedItemIndexPayloads)
            {
                DeferredClientSetWieldedItemIndexPayload existingPayload = DeferredClientSetWieldedItemIndexPayloads
                    .FirstOrDefault(candidate =>
                        candidate?.Message?.AgentIndex == setWieldedItemIndex.AgentIndex &&
                        candidate.Message.WieldedItemIndex == setWieldedItemIndex.WieldedItemIndex &&
                        candidate.Message.IsWieldedOnSpawn == setWieldedItemIndex.IsWieldedOnSpawn);
                if (existingPayload != null)
                {
                    existingPayload.Message = setWieldedItemIndex;
                    existingPayload.DeferralReason = deferralReason;
                    return;
                }

                DeferredClientSetWieldedItemIndexPayloads.Add(
                    new DeferredClientSetWieldedItemIndexPayload
                    {
                        Sequence = ++_nextDeferredClientSetWieldedItemIndexSequence,
                        Message = setWieldedItemIndex,
                        DeferredUtc = DateTime.UtcNow,
                        LastAttemptUtc = DateTime.MinValue,
                        Attempts = 0,
                        DeferralReason = deferralReason
                    });
            }
        }

        private static void RegisterDeferredClientAttachWeaponToWeaponInAgentEquipmentSlotPayload(
            AttachWeaponToWeaponInAgentEquipmentSlot attachWeapon,
            string deferralReason)
        {
            if (attachWeapon == null)
                return;

            lock (DeferredClientAttachWeaponToWeaponInAgentEquipmentSlotPayloads)
            {
                DeferredClientAttachWeaponToWeaponInAgentEquipmentSlotPayload existingPayload =
                    DeferredClientAttachWeaponToWeaponInAgentEquipmentSlotPayloads.FirstOrDefault(
                        candidate =>
                            candidate?.Message?.AgentIndex == attachWeapon.AgentIndex &&
                            candidate.Message.SlotIndex == attachWeapon.SlotIndex);
                if (existingPayload != null)
                {
                    existingPayload.Message = attachWeapon;
                    existingPayload.DeferralReason = deferralReason;
                    return;
                }

                DeferredClientAttachWeaponToWeaponInAgentEquipmentSlotPayloads.Add(
                    new DeferredClientAttachWeaponToWeaponInAgentEquipmentSlotPayload
                    {
                        Sequence = ++_nextDeferredClientAttachWeaponToWeaponInAgentEquipmentSlotSequence,
                        Message = attachWeapon,
                        DeferredUtc = DateTime.UtcNow,
                        LastAttemptUtc = DateTime.MinValue,
                        Attempts = 0,
                        DeferralReason = deferralReason
                    });
            }
        }

        private static void RegisterDeferredClientSetWeaponNetworkDataPayload(
            SetWeaponNetworkData setWeaponNetworkData,
            string deferralReason)
        {
            if (setWeaponNetworkData == null)
                return;

            lock (DeferredClientSetWeaponNetworkDataPayloads)
            {
                DeferredClientSetWeaponNetworkDataPayload existingPayload = DeferredClientSetWeaponNetworkDataPayloads
                    .FirstOrDefault(candidate =>
                        candidate?.Message?.AgentIndex == setWeaponNetworkData.AgentIndex &&
                        candidate.Message.WeaponEquipmentIndex == setWeaponNetworkData.WeaponEquipmentIndex);
                if (existingPayload != null)
                {
                    existingPayload.Message = setWeaponNetworkData;
                    existingPayload.DeferralReason = deferralReason;
                    return;
                }

                DeferredClientSetWeaponNetworkDataPayloads.Add(
                    new DeferredClientSetWeaponNetworkDataPayload
                    {
                        Sequence = ++_nextDeferredClientSetWeaponNetworkDataSequence,
                        Message = setWeaponNetworkData,
                        DeferredUtc = DateTime.UtcNow,
                        LastAttemptUtc = DateTime.MinValue,
                        Attempts = 0,
                        DeferralReason = deferralReason
                    });
            }
        }

        private static void RegisterDeferredClientSetWeaponAmmoDataPayload(
            SetWeaponAmmoData setWeaponAmmoData,
            string deferralReason)
        {
            if (setWeaponAmmoData == null)
                return;

            lock (DeferredClientSetWeaponAmmoDataPayloads)
            {
                DeferredClientSetWeaponAmmoDataPayload existingPayload = DeferredClientSetWeaponAmmoDataPayloads
                    .FirstOrDefault(candidate =>
                        candidate?.Message?.AgentIndex == setWeaponAmmoData.AgentIndex &&
                        candidate.Message.WeaponEquipmentIndex == setWeaponAmmoData.WeaponEquipmentIndex &&
                        candidate.Message.AmmoEquipmentIndex == setWeaponAmmoData.AmmoEquipmentIndex);
                if (existingPayload != null)
                {
                    existingPayload.Message = setWeaponAmmoData;
                    existingPayload.DeferralReason = deferralReason;
                    return;
                }

                DeferredClientSetWeaponAmmoDataPayloads.Add(
                    new DeferredClientSetWeaponAmmoDataPayload
                    {
                        Sequence = ++_nextDeferredClientSetWeaponAmmoDataSequence,
                        Message = setWeaponAmmoData,
                        DeferredUtc = DateTime.UtcNow,
                        LastAttemptUtc = DateTime.MinValue,
                        Attempts = 0,
                        DeferralReason = deferralReason
                    });
            }
        }

        private static void RegisterDeferredClientSetWeaponReloadPhasePayload(
            SetWeaponReloadPhase setWeaponReloadPhase,
            string deferralReason)
        {
            if (setWeaponReloadPhase == null)
                return;

            lock (DeferredClientSetWeaponReloadPhasePayloads)
            {
                DeferredClientSetWeaponReloadPhasePayload existingPayload = DeferredClientSetWeaponReloadPhasePayloads
                    .FirstOrDefault(candidate =>
                        candidate?.Message?.AgentIndex == setWeaponReloadPhase.AgentIndex &&
                        candidate.Message.EquipmentIndex == setWeaponReloadPhase.EquipmentIndex);
                if (existingPayload != null)
                {
                    existingPayload.Message = setWeaponReloadPhase;
                    existingPayload.DeferralReason = deferralReason;
                    return;
                }

                DeferredClientSetWeaponReloadPhasePayloads.Add(
                    new DeferredClientSetWeaponReloadPhasePayload
                    {
                        Sequence = ++_nextDeferredClientSetWeaponReloadPhaseSequence,
                        Message = setWeaponReloadPhase,
                        DeferredUtc = DateTime.UtcNow,
                        LastAttemptUtc = DateTime.MinValue,
                        Attempts = 0,
                        DeferralReason = deferralReason
                    });
            }
        }

        private static void RegisterDeferredClientStartSwitchingWeaponUsageIndexPayload(
            StartSwitchingWeaponUsageIndex startSwitchingWeaponUsageIndex,
            string deferralReason)
        {
            if (startSwitchingWeaponUsageIndex == null)
                return;

            lock (DeferredClientStartSwitchingWeaponUsageIndexPayloads)
            {
                DeferredClientStartSwitchingWeaponUsageIndexPayload existingPayload =
                    DeferredClientStartSwitchingWeaponUsageIndexPayloads.FirstOrDefault(candidate =>
                        candidate?.Message?.AgentIndex == startSwitchingWeaponUsageIndex.AgentIndex &&
                        candidate.Message.EquipmentIndex == startSwitchingWeaponUsageIndex.EquipmentIndex &&
                        candidate.Message.UsageIndex == startSwitchingWeaponUsageIndex.UsageIndex);
                if (existingPayload != null)
                {
                    existingPayload.Message = startSwitchingWeaponUsageIndex;
                    existingPayload.DeferralReason = deferralReason;
                    return;
                }

                DeferredClientStartSwitchingWeaponUsageIndexPayloads.Add(
                    new DeferredClientStartSwitchingWeaponUsageIndexPayload
                    {
                        Sequence = ++_nextDeferredClientStartSwitchingWeaponUsageIndexSequence,
                        Message = startSwitchingWeaponUsageIndex,
                        DeferredUtc = DateTime.UtcNow,
                        LastAttemptUtc = DateTime.MinValue,
                        Attempts = 0,
                        DeferralReason = deferralReason
                    });
            }
        }

        private static void RegisterDeferredClientWeaponUsageIndexChangePayload(
            WeaponUsageIndexChangeMessage weaponUsageIndexChangeMessage,
            string deferralReason)
        {
            if (weaponUsageIndexChangeMessage == null)
                return;

            lock (DeferredClientWeaponUsageIndexChangePayloads)
            {
                DeferredClientWeaponUsageIndexChangePayload existingPayload =
                    DeferredClientWeaponUsageIndexChangePayloads.FirstOrDefault(candidate =>
                        candidate?.Message?.AgentIndex == weaponUsageIndexChangeMessage.AgentIndex &&
                        candidate.Message.SlotIndex == weaponUsageIndexChangeMessage.SlotIndex &&
                        candidate.Message.UsageIndex == weaponUsageIndexChangeMessage.UsageIndex);
                if (existingPayload != null)
                {
                    existingPayload.Message = weaponUsageIndexChangeMessage;
                    existingPayload.DeferralReason = deferralReason;
                    return;
                }

                DeferredClientWeaponUsageIndexChangePayloads.Add(
                    new DeferredClientWeaponUsageIndexChangePayload
                    {
                        Sequence = ++_nextDeferredClientWeaponUsageIndexChangeSequence,
                        Message = weaponUsageIndexChangeMessage,
                        DeferredUtc = DateTime.UtcNow,
                        LastAttemptUtc = DateTime.MinValue,
                        Attempts = 0,
                        DeferralReason = deferralReason
                    });
            }
        }

        private static void RegisterDeferredClientSetAgentHealthPayload(
            SetAgentHealth setAgentHealth,
            string deferralReason)
        {
            if (setAgentHealth == null)
                return;

            lock (DeferredClientSetAgentHealthPayloads)
            {
                DeferredClientSetAgentHealthPayload existingPayload = DeferredClientSetAgentHealthPayloads
                    .FirstOrDefault(candidate => candidate?.Message?.AgentIndex == setAgentHealth.AgentIndex);
                if (existingPayload != null)
                {
                    existingPayload.Message = setAgentHealth;
                    existingPayload.DeferralReason = deferralReason;
                    return;
                }

                DeferredClientSetAgentHealthPayloads.Add(
                    new DeferredClientSetAgentHealthPayload
                    {
                        Sequence = ++_nextDeferredClientSetAgentHealthSequence,
                        Message = setAgentHealth,
                        DeferredUtc = DateTime.UtcNow,
                        LastAttemptUtc = DateTime.MinValue,
                        Attempts = 0,
                        DeferralReason = deferralReason
                    });
            }
        }

        private static void RegisterDeferredClientMakeAgentDeadPayload(
            MakeAgentDead makeAgentDead,
            string deferralReason)
        {
            if (makeAgentDead == null)
                return;

            lock (DeferredClientMakeAgentDeadPayloads)
            {
                DeferredClientMakeAgentDeadPayload existingPayload = DeferredClientMakeAgentDeadPayloads
                    .FirstOrDefault(candidate => candidate?.Message?.AgentIndex == makeAgentDead.AgentIndex);
                if (existingPayload != null)
                {
                    existingPayload.Message = makeAgentDead;
                    existingPayload.DeferralReason = deferralReason;
                    return;
                }

                DeferredClientMakeAgentDeadPayloads.Add(
                    new DeferredClientMakeAgentDeadPayload
                    {
                        Sequence = ++_nextDeferredClientMakeAgentDeadSequence,
                        Message = makeAgentDead,
                        DeferredUtc = DateTime.UtcNow,
                        LastAttemptUtc = DateTime.MinValue,
                        Attempts = 0,
                        DeferralReason = deferralReason
                    });
            }
        }

        private static void TryReplayDeferredClientCreateAgents(
            Mission mission,
            string source,
            string snapshotReadinessSummary)
        {
            if (_missionNetworkComponentHandleServerEventCreateAgentMethod == null)
                return;

            List<DeferredClientCreateAgentPayload> deferredPayloads;
            lock (DeferredClientCreateAgentPayloads)
            {
                if (DeferredClientCreateAgentPayloads.Count <= 0)
                    return;

                deferredPayloads = DeferredClientCreateAgentPayloads
                    .OrderBy(candidate => candidate.Sequence)
                    .ToList();
            }

            MissionNetworkComponent missionNetworkComponent = mission.GetMissionBehavior<MissionNetworkComponent>();
            if (missionNetworkComponent == null)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            foreach (DeferredClientCreateAgentPayload deferredPayload in deferredPayloads)
            {
                CreateAgent createAgent = deferredPayload?.Message;
                if (createAgent == null)
                    continue;

                if (deferredPayload.LastAttemptUtc != DateTime.MinValue &&
                    nowUtc - deferredPayload.LastAttemptUtc < TimeSpan.FromMilliseconds(100))
                {
                    continue;
                }

                Agent existingAgent = Mission.MissionNetworkHelper.GetAgentFromIndex(createAgent.AgentIndex, canBeNull: true);
                if (existingAgent != null && existingAgent.IsActive())
                {
                    RemoveDeferredClientCreateAgentPayload(createAgent.AgentIndex);
                    continue;
                }

                deferredPayload.LastAttemptUtc = nowUtc;
                deferredPayload.Attempts++;
                try
                {
                    _missionNetworkComponentHandleServerEventCreateAgentMethod.Invoke(
                        missionNetworkComponent,
                        new object[] { createAgent });
                    RemoveDeferredClientCreateAgentPayload(createAgent.AgentIndex);
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: replayed deferred client CreateAgent after battle snapshot apply. " +
                        "AgentIndex=" + createAgent.AgentIndex +
                        " PayloadCharacter=" + (createAgent.Character?.StringId ?? "null") +
                        " Attempts=" + deferredPayload.Attempts +
                        " Source=" + (source ?? "unknown"));
                }
                catch (Exception ex)
                {
                    if (deferredPayload.Attempts == 1 || deferredPayload.Attempts % 20 == 0)
                    {
                        ModLogger.Info(
                            "BattleMapSpawnHandoffPatch: deferred client CreateAgent replay failed open. " +
                            "AgentIndex=" + createAgent.AgentIndex +
                            " PayloadCharacter=" + (createAgent.Character?.StringId ?? "null") +
                            " Attempts=" + deferredPayload.Attempts +
                            " SnapshotReadiness=" + (snapshotReadinessSummary ?? "unknown") +
                            " Message=" + ex.GetBaseException().Message);
                    }
                }
            }
        }

        private static void TryReplayDeferredClientSetAgentActionSet(
            Mission mission,
            string source,
            string snapshotReadinessSummary)
        {
            if (_missionNetworkComponentHandleServerEventSetAgentActionSetMethod == null)
                return;

            List<DeferredClientSetAgentActionSetPayload> deferredPayloads;
            lock (DeferredClientSetAgentActionSetPayloads)
            {
                if (DeferredClientSetAgentActionSetPayloads.Count <= 0)
                    return;

                deferredPayloads = DeferredClientSetAgentActionSetPayloads
                    .OrderBy(candidate => candidate.Sequence)
                    .ToList();
            }

            MissionNetworkComponent missionNetworkComponent = mission.GetMissionBehavior<MissionNetworkComponent>();
            if (missionNetworkComponent == null)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            foreach (DeferredClientSetAgentActionSetPayload deferredPayload in deferredPayloads)
            {
                SetAgentActionSet setAgentActionSet = deferredPayload?.Message;
                if (setAgentActionSet == null)
                    continue;

                if (deferredPayload.LastAttemptUtc != DateTime.MinValue &&
                    nowUtc - deferredPayload.LastAttemptUtc < TimeSpan.FromMilliseconds(100))
                {
                    continue;
                }

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(setAgentActionSet.AgentIndex, canBeNull: true);
                if (agent == null || !agent.IsActive())
                    continue;

                deferredPayload.LastAttemptUtc = nowUtc;
                deferredPayload.Attempts++;
                try
                {
                    _missionNetworkComponentHandleServerEventSetAgentActionSetMethod.Invoke(
                        missionNetworkComponent,
                        new object[] { setAgentActionSet });
                    RemoveDeferredClientSetAgentActionSetPayload(setAgentActionSet.AgentIndex);
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: replayed deferred client SetAgentActionSet after battle snapshot apply. " +
                        "AgentIndex=" + setAgentActionSet.AgentIndex +
                        " Attempts=" + deferredPayload.Attempts +
                        " Source=" + (source ?? "unknown"));
                }
                catch (Exception ex)
                {
                    if (deferredPayload.Attempts == 1 || deferredPayload.Attempts % 20 == 0)
                    {
                        ModLogger.Info(
                            "BattleMapSpawnHandoffPatch: deferred client SetAgentActionSet replay failed open. " +
                            "AgentIndex=" + setAgentActionSet.AgentIndex +
                            " Attempts=" + deferredPayload.Attempts +
                            " SnapshotReadiness=" + (snapshotReadinessSummary ?? "unknown") +
                            " Message=" + ex.GetBaseException().Message);
                    }
                }
            }
        }

        private static void TryReplayDeferredClientSynchronizeAgentEquipment(
            Mission mission,
            string source,
            string snapshotReadinessSummary)
        {
            if (_missionNetworkComponentHandleServerEventSynchronizeAgentEquipmentMethod == null)
                return;

            List<DeferredClientSynchronizeAgentEquipmentPayload> deferredPayloads;
            lock (DeferredClientSynchronizeAgentEquipmentPayloads)
            {
                if (DeferredClientSynchronizeAgentEquipmentPayloads.Count <= 0)
                    return;

                deferredPayloads = DeferredClientSynchronizeAgentEquipmentPayloads
                    .OrderBy(candidate => candidate.Sequence)
                    .ToList();
            }

            MissionNetworkComponent missionNetworkComponent = mission.GetMissionBehavior<MissionNetworkComponent>();
            if (missionNetworkComponent == null)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            foreach (DeferredClientSynchronizeAgentEquipmentPayload deferredPayload in deferredPayloads)
            {
                SynchronizeAgentSpawnEquipment synchronizeAgentSpawnEquipment = deferredPayload?.Message;
                if (synchronizeAgentSpawnEquipment == null)
                    continue;

                if (deferredPayload.LastAttemptUtc != DateTime.MinValue &&
                    nowUtc - deferredPayload.LastAttemptUtc < TimeSpan.FromMilliseconds(100))
                {
                    continue;
                }

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(synchronizeAgentSpawnEquipment.AgentIndex, canBeNull: true);
                if (agent == null || !agent.IsActive())
                    continue;

                deferredPayload.LastAttemptUtc = nowUtc;
                deferredPayload.Attempts++;
                try
                {
                    _missionNetworkComponentHandleServerEventSynchronizeAgentEquipmentMethod.Invoke(
                        missionNetworkComponent,
                        new object[] { synchronizeAgentSpawnEquipment });
                    RemoveDeferredClientSynchronizeAgentEquipmentPayload(synchronizeAgentSpawnEquipment.AgentIndex);
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: replayed deferred client SynchronizeAgentSpawnEquipment after battle snapshot apply. " +
                        "AgentIndex=" + synchronizeAgentSpawnEquipment.AgentIndex +
                        " Attempts=" + deferredPayload.Attempts +
                        " Source=" + (source ?? "unknown"));
                }
                catch (Exception ex)
                {
                    if (deferredPayload.Attempts == 1 || deferredPayload.Attempts % 20 == 0)
                    {
                        ModLogger.Info(
                            "BattleMapSpawnHandoffPatch: deferred client SynchronizeAgentSpawnEquipment replay failed open. " +
                            "AgentIndex=" + synchronizeAgentSpawnEquipment.AgentIndex +
                            " Attempts=" + deferredPayload.Attempts +
                            " SnapshotReadiness=" + (snapshotReadinessSummary ?? "unknown") +
                            " Message=" + ex.GetBaseException().Message);
                    }
                }
            }
        }

        private static void TryReplayDeferredClientAttachWeaponToWeaponInAgentEquipmentSlot(
            Mission mission,
            string source,
            string snapshotReadinessSummary)
        {
            if (_missionNetworkComponentHandleServerEventAttachWeaponToWeaponInAgentEquipmentSlotMethod == null)
                return;

            List<DeferredClientAttachWeaponToWeaponInAgentEquipmentSlotPayload> deferredPayloads;
            lock (DeferredClientAttachWeaponToWeaponInAgentEquipmentSlotPayloads)
            {
                if (DeferredClientAttachWeaponToWeaponInAgentEquipmentSlotPayloads.Count <= 0)
                    return;

                deferredPayloads = DeferredClientAttachWeaponToWeaponInAgentEquipmentSlotPayloads
                    .OrderBy(candidate => candidate.Sequence)
                    .ToList();
            }

            MissionNetworkComponent missionNetworkComponent = mission.GetMissionBehavior<MissionNetworkComponent>();
            if (missionNetworkComponent == null)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            foreach (DeferredClientAttachWeaponToWeaponInAgentEquipmentSlotPayload deferredPayload in deferredPayloads)
            {
                AttachWeaponToWeaponInAgentEquipmentSlot attachWeapon = deferredPayload?.Message;
                if (attachWeapon == null)
                    continue;

                if (deferredPayload.LastAttemptUtc != DateTime.MinValue &&
                    nowUtc - deferredPayload.LastAttemptUtc < TimeSpan.FromMilliseconds(100))
                {
                    continue;
                }

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(attachWeapon.AgentIndex, canBeNull: true);
                if (agent == null || !agent.IsActive())
                    continue;

                deferredPayload.LastAttemptUtc = nowUtc;
                deferredPayload.Attempts++;
                try
                {
                    _missionNetworkComponentHandleServerEventAttachWeaponToWeaponInAgentEquipmentSlotMethod.Invoke(
                        missionNetworkComponent,
                        new object[] { attachWeapon });
                    RemoveDeferredClientAttachWeaponToWeaponInAgentEquipmentSlotPayload(
                        attachWeapon.AgentIndex,
                        attachWeapon);
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: replayed deferred client AttachWeaponToWeaponInAgentEquipmentSlot after battle snapshot apply. " +
                        "AgentIndex=" + attachWeapon.AgentIndex +
                        " SlotIndex=" + attachWeapon.SlotIndex +
                        " Attempts=" + deferredPayload.Attempts +
                        " Source=" + (source ?? "unknown"));
                }
                catch (Exception ex)
                {
                    if (deferredPayload.Attempts == 1 || deferredPayload.Attempts % 20 == 0)
                    {
                        ModLogger.Info(
                            "BattleMapSpawnHandoffPatch: deferred client AttachWeaponToWeaponInAgentEquipmentSlot replay failed open. " +
                            "AgentIndex=" + attachWeapon.AgentIndex +
                            " SlotIndex=" + attachWeapon.SlotIndex +
                            " Attempts=" + deferredPayload.Attempts +
                            " SnapshotReadiness=" + (snapshotReadinessSummary ?? "unknown") +
                            " Message=" + ex.GetBaseException().Message);
                    }
                }
            }
        }

        private static void TryReplayDeferredClientSetWeaponNetworkData(
            Mission mission,
            string source,
            string snapshotReadinessSummary)
        {
            if (_missionNetworkComponentHandleServerEventSetWeaponNetworkDataMethod == null)
                return;

            List<DeferredClientSetWeaponNetworkDataPayload> deferredPayloads;
            lock (DeferredClientSetWeaponNetworkDataPayloads)
            {
                if (DeferredClientSetWeaponNetworkDataPayloads.Count <= 0)
                    return;

                deferredPayloads = DeferredClientSetWeaponNetworkDataPayloads
                    .OrderBy(candidate => candidate.Sequence)
                    .ToList();
            }

            MissionNetworkComponent missionNetworkComponent = mission.GetMissionBehavior<MissionNetworkComponent>();
            if (missionNetworkComponent == null)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            foreach (DeferredClientSetWeaponNetworkDataPayload deferredPayload in deferredPayloads)
            {
                SetWeaponNetworkData setWeaponNetworkData = deferredPayload?.Message;
                if (setWeaponNetworkData == null)
                    continue;

                if (deferredPayload.LastAttemptUtc != DateTime.MinValue &&
                    nowUtc - deferredPayload.LastAttemptUtc < TimeSpan.FromMilliseconds(100))
                {
                    continue;
                }

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(setWeaponNetworkData.AgentIndex, canBeNull: true);
                if (agent == null || !agent.IsActive())
                    continue;

                deferredPayload.LastAttemptUtc = nowUtc;
                deferredPayload.Attempts++;
                try
                {
                    _missionNetworkComponentHandleServerEventSetWeaponNetworkDataMethod.Invoke(
                        missionNetworkComponent,
                        new object[] { setWeaponNetworkData });
                    RemoveDeferredClientSetWeaponNetworkDataPayload(
                        setWeaponNetworkData.AgentIndex,
                        setWeaponNetworkData);
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: replayed deferred client SetWeaponNetworkData after battle snapshot apply. " +
                        "AgentIndex=" + setWeaponNetworkData.AgentIndex +
                        " WeaponEquipmentIndex=" + setWeaponNetworkData.WeaponEquipmentIndex +
                        " Attempts=" + deferredPayload.Attempts +
                        " Source=" + (source ?? "unknown"));
                }
                catch (Exception ex)
                {
                    if (deferredPayload.Attempts == 1 || deferredPayload.Attempts % 20 == 0)
                    {
                        ModLogger.Info(
                            "BattleMapSpawnHandoffPatch: deferred client SetWeaponNetworkData replay failed open. " +
                            "AgentIndex=" + setWeaponNetworkData.AgentIndex +
                            " WeaponEquipmentIndex=" + setWeaponNetworkData.WeaponEquipmentIndex +
                            " Attempts=" + deferredPayload.Attempts +
                            " SnapshotReadiness=" + (snapshotReadinessSummary ?? "unknown") +
                            " Message=" + ex.GetBaseException().Message);
                    }
                }
            }
        }

        private static void TryReplayDeferredClientSetWeaponAmmoData(
            Mission mission,
            string source,
            string snapshotReadinessSummary)
        {
            if (_missionNetworkComponentHandleServerEventSetWeaponAmmoDataMethod == null)
                return;

            List<DeferredClientSetWeaponAmmoDataPayload> deferredPayloads;
            lock (DeferredClientSetWeaponAmmoDataPayloads)
            {
                if (DeferredClientSetWeaponAmmoDataPayloads.Count <= 0)
                    return;

                deferredPayloads = DeferredClientSetWeaponAmmoDataPayloads
                    .OrderBy(candidate => candidate.Sequence)
                    .ToList();
            }

            MissionNetworkComponent missionNetworkComponent = mission.GetMissionBehavior<MissionNetworkComponent>();
            if (missionNetworkComponent == null)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            foreach (DeferredClientSetWeaponAmmoDataPayload deferredPayload in deferredPayloads)
            {
                SetWeaponAmmoData setWeaponAmmoData = deferredPayload?.Message;
                if (setWeaponAmmoData == null)
                    continue;

                if (deferredPayload.LastAttemptUtc != DateTime.MinValue &&
                    nowUtc - deferredPayload.LastAttemptUtc < TimeSpan.FromMilliseconds(100))
                {
                    continue;
                }

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(setWeaponAmmoData.AgentIndex, canBeNull: true);
                if (agent == null || !agent.IsActive())
                    continue;

                deferredPayload.LastAttemptUtc = nowUtc;
                deferredPayload.Attempts++;
                try
                {
                    _missionNetworkComponentHandleServerEventSetWeaponAmmoDataMethod.Invoke(
                        missionNetworkComponent,
                        new object[] { setWeaponAmmoData });
                    RemoveDeferredClientSetWeaponAmmoDataPayload(
                        setWeaponAmmoData.AgentIndex,
                        setWeaponAmmoData);
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: replayed deferred client SetWeaponAmmoData after battle snapshot apply. " +
                        "AgentIndex=" + setWeaponAmmoData.AgentIndex +
                        " WeaponEquipmentIndex=" + setWeaponAmmoData.WeaponEquipmentIndex +
                        " AmmoEquipmentIndex=" + setWeaponAmmoData.AmmoEquipmentIndex +
                        " Attempts=" + deferredPayload.Attempts +
                        " Source=" + (source ?? "unknown"));
                }
                catch (Exception ex)
                {
                    if (deferredPayload.Attempts == 1 || deferredPayload.Attempts % 20 == 0)
                    {
                        ModLogger.Info(
                            "BattleMapSpawnHandoffPatch: deferred client SetWeaponAmmoData replay failed open. " +
                            "AgentIndex=" + setWeaponAmmoData.AgentIndex +
                            " WeaponEquipmentIndex=" + setWeaponAmmoData.WeaponEquipmentIndex +
                            " AmmoEquipmentIndex=" + setWeaponAmmoData.AmmoEquipmentIndex +
                            " Attempts=" + deferredPayload.Attempts +
                            " SnapshotReadiness=" + (snapshotReadinessSummary ?? "unknown") +
                            " Message=" + ex.GetBaseException().Message);
                    }
                }
            }
        }

        private static void TryReplayDeferredClientSetWeaponReloadPhase(
            Mission mission,
            string source,
            string snapshotReadinessSummary)
        {
            if (_missionNetworkComponentHandleServerEventSetWeaponReloadPhaseMethod == null)
                return;

            List<DeferredClientSetWeaponReloadPhasePayload> deferredPayloads;
            lock (DeferredClientSetWeaponReloadPhasePayloads)
            {
                if (DeferredClientSetWeaponReloadPhasePayloads.Count <= 0)
                    return;

                deferredPayloads = DeferredClientSetWeaponReloadPhasePayloads
                    .OrderBy(candidate => candidate.Sequence)
                    .ToList();
            }

            MissionNetworkComponent missionNetworkComponent = mission.GetMissionBehavior<MissionNetworkComponent>();
            if (missionNetworkComponent == null)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            foreach (DeferredClientSetWeaponReloadPhasePayload deferredPayload in deferredPayloads)
            {
                SetWeaponReloadPhase setWeaponReloadPhase = deferredPayload?.Message;
                if (setWeaponReloadPhase == null)
                    continue;

                if (deferredPayload.LastAttemptUtc != DateTime.MinValue &&
                    nowUtc - deferredPayload.LastAttemptUtc < TimeSpan.FromMilliseconds(100))
                {
                    continue;
                }

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(setWeaponReloadPhase.AgentIndex, canBeNull: true);
                if (agent == null || !agent.IsActive())
                    continue;

                deferredPayload.LastAttemptUtc = nowUtc;
                deferredPayload.Attempts++;
                try
                {
                    _missionNetworkComponentHandleServerEventSetWeaponReloadPhaseMethod.Invoke(
                        missionNetworkComponent,
                        new object[] { setWeaponReloadPhase });
                    RemoveDeferredClientSetWeaponReloadPhasePayload(
                        setWeaponReloadPhase.AgentIndex,
                        setWeaponReloadPhase);
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: replayed deferred client SetWeaponReloadPhase after battle snapshot apply. " +
                        "AgentIndex=" + setWeaponReloadPhase.AgentIndex +
                        " EquipmentIndex=" + setWeaponReloadPhase.EquipmentIndex +
                        " Attempts=" + deferredPayload.Attempts +
                        " Source=" + (source ?? "unknown"));
                }
                catch (Exception ex)
                {
                    if (deferredPayload.Attempts == 1 || deferredPayload.Attempts % 20 == 0)
                    {
                        ModLogger.Info(
                            "BattleMapSpawnHandoffPatch: deferred client SetWeaponReloadPhase replay failed open. " +
                            "AgentIndex=" + setWeaponReloadPhase.AgentIndex +
                            " EquipmentIndex=" + setWeaponReloadPhase.EquipmentIndex +
                            " Attempts=" + deferredPayload.Attempts +
                            " SnapshotReadiness=" + (snapshotReadinessSummary ?? "unknown") +
                            " Message=" + ex.GetBaseException().Message);
                    }
                }
            }
        }

        private static void TryReplayDeferredClientSetWieldedItemIndex(
            Mission mission,
            string source,
            string snapshotReadinessSummary)
        {
            if (_missionNetworkComponentHandleServerEventSetWieldedItemIndexMethod == null)
                return;

            List<DeferredClientSetWieldedItemIndexPayload> deferredPayloads;
            lock (DeferredClientSetWieldedItemIndexPayloads)
            {
                if (DeferredClientSetWieldedItemIndexPayloads.Count <= 0)
                    return;

                deferredPayloads = DeferredClientSetWieldedItemIndexPayloads
                    .OrderBy(candidate => candidate.Sequence)
                    .ToList();
            }

            MissionNetworkComponent missionNetworkComponent = mission.GetMissionBehavior<MissionNetworkComponent>();
            if (missionNetworkComponent == null)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            foreach (DeferredClientSetWieldedItemIndexPayload deferredPayload in deferredPayloads)
            {
                SetWieldedItemIndex setWieldedItemIndex = deferredPayload?.Message;
                if (setWieldedItemIndex == null)
                    continue;

                if (deferredPayload.LastAttemptUtc != DateTime.MinValue &&
                    nowUtc - deferredPayload.LastAttemptUtc < TimeSpan.FromMilliseconds(100))
                {
                    continue;
                }

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(setWieldedItemIndex.AgentIndex, canBeNull: true);
                if (agent == null || !agent.IsActive())
                    continue;

                deferredPayload.LastAttemptUtc = nowUtc;
                deferredPayload.Attempts++;
                try
                {
                    _missionNetworkComponentHandleServerEventSetWieldedItemIndexMethod.Invoke(
                        missionNetworkComponent,
                        new object[] { setWieldedItemIndex });
                    RemoveDeferredClientSetWieldedItemIndexPayload(setWieldedItemIndex.AgentIndex, setWieldedItemIndex);
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: replayed deferred client SetWieldedItemIndex after battle snapshot apply. " +
                        "AgentIndex=" + setWieldedItemIndex.AgentIndex +
                        " WieldedItemIndex=" + setWieldedItemIndex.WieldedItemIndex +
                        " Attempts=" + deferredPayload.Attempts +
                        " Source=" + (source ?? "unknown"));
                }
                catch (Exception ex)
                {
                    if (deferredPayload.Attempts == 1 || deferredPayload.Attempts % 20 == 0)
                    {
                        ModLogger.Info(
                            "BattleMapSpawnHandoffPatch: deferred client SetWieldedItemIndex replay failed open. " +
                            "AgentIndex=" + setWieldedItemIndex.AgentIndex +
                            " WieldedItemIndex=" + setWieldedItemIndex.WieldedItemIndex +
                            " Attempts=" + deferredPayload.Attempts +
                            " SnapshotReadiness=" + (snapshotReadinessSummary ?? "unknown") +
                            " Message=" + ex.GetBaseException().Message);
                    }
                }
            }
        }

        private static void TryReplayDeferredClientStartSwitchingWeaponUsageIndex(
            Mission mission,
            string source,
            string snapshotReadinessSummary)
        {
            if (_missionNetworkComponentHandleServerEventStartSwitchingWeaponUsageIndexMethod == null)
                return;

            List<DeferredClientStartSwitchingWeaponUsageIndexPayload> deferredPayloads;
            lock (DeferredClientStartSwitchingWeaponUsageIndexPayloads)
            {
                if (DeferredClientStartSwitchingWeaponUsageIndexPayloads.Count <= 0)
                    return;

                deferredPayloads = DeferredClientStartSwitchingWeaponUsageIndexPayloads
                    .OrderBy(candidate => candidate.Sequence)
                    .ToList();
            }

            MissionNetworkComponent missionNetworkComponent = mission.GetMissionBehavior<MissionNetworkComponent>();
            if (missionNetworkComponent == null)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            foreach (DeferredClientStartSwitchingWeaponUsageIndexPayload deferredPayload in deferredPayloads)
            {
                StartSwitchingWeaponUsageIndex startSwitchingWeaponUsageIndex = deferredPayload?.Message;
                if (startSwitchingWeaponUsageIndex == null)
                    continue;

                if (deferredPayload.LastAttemptUtc != DateTime.MinValue &&
                    nowUtc - deferredPayload.LastAttemptUtc < TimeSpan.FromMilliseconds(100))
                {
                    continue;
                }

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(startSwitchingWeaponUsageIndex.AgentIndex, canBeNull: true);
                if (agent == null || !agent.IsActive())
                    continue;

                deferredPayload.LastAttemptUtc = nowUtc;
                deferredPayload.Attempts++;
                try
                {
                    _missionNetworkComponentHandleServerEventStartSwitchingWeaponUsageIndexMethod.Invoke(
                        missionNetworkComponent,
                        new object[] { startSwitchingWeaponUsageIndex });
                    RemoveDeferredClientStartSwitchingWeaponUsageIndexPayload(
                        startSwitchingWeaponUsageIndex.AgentIndex,
                        startSwitchingWeaponUsageIndex);
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: replayed deferred client StartSwitchingWeaponUsageIndex after battle snapshot apply. " +
                        "AgentIndex=" + startSwitchingWeaponUsageIndex.AgentIndex +
                        " EquipmentIndex=" + startSwitchingWeaponUsageIndex.EquipmentIndex +
                        " UsageIndex=" + startSwitchingWeaponUsageIndex.UsageIndex +
                        " Attempts=" + deferredPayload.Attempts +
                        " Source=" + (source ?? "unknown"));
                }
                catch (Exception ex)
                {
                    if (deferredPayload.Attempts == 1 || deferredPayload.Attempts % 20 == 0)
                    {
                        ModLogger.Info(
                            "BattleMapSpawnHandoffPatch: deferred client StartSwitchingWeaponUsageIndex replay failed open. " +
                            "AgentIndex=" + startSwitchingWeaponUsageIndex.AgentIndex +
                            " EquipmentIndex=" + startSwitchingWeaponUsageIndex.EquipmentIndex +
                            " UsageIndex=" + startSwitchingWeaponUsageIndex.UsageIndex +
                            " Attempts=" + deferredPayload.Attempts +
                            " SnapshotReadiness=" + (snapshotReadinessSummary ?? "unknown") +
                            " Message=" + ex.GetBaseException().Message);
                    }
                }
            }
        }

        private static void TryReplayDeferredClientWeaponUsageIndexChangeMessage(
            Mission mission,
            string source,
            string snapshotReadinessSummary)
        {
            if (_missionNetworkComponentHandleServerEventWeaponUsageIndexChangeMessageMethod == null)
                return;

            List<DeferredClientWeaponUsageIndexChangePayload> deferredPayloads;
            lock (DeferredClientWeaponUsageIndexChangePayloads)
            {
                if (DeferredClientWeaponUsageIndexChangePayloads.Count <= 0)
                    return;

                deferredPayloads = DeferredClientWeaponUsageIndexChangePayloads
                    .OrderBy(candidate => candidate.Sequence)
                    .ToList();
            }

            MissionNetworkComponent missionNetworkComponent = mission.GetMissionBehavior<MissionNetworkComponent>();
            if (missionNetworkComponent == null)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            foreach (DeferredClientWeaponUsageIndexChangePayload deferredPayload in deferredPayloads)
            {
                WeaponUsageIndexChangeMessage weaponUsageIndexChangeMessage = deferredPayload?.Message;
                if (weaponUsageIndexChangeMessage == null)
                    continue;

                if (deferredPayload.LastAttemptUtc != DateTime.MinValue &&
                    nowUtc - deferredPayload.LastAttemptUtc < TimeSpan.FromMilliseconds(100))
                {
                    continue;
                }

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(weaponUsageIndexChangeMessage.AgentIndex, canBeNull: true);
                if (agent == null || !agent.IsActive())
                    continue;

                deferredPayload.LastAttemptUtc = nowUtc;
                deferredPayload.Attempts++;
                try
                {
                    _missionNetworkComponentHandleServerEventWeaponUsageIndexChangeMessageMethod.Invoke(
                        missionNetworkComponent,
                        new object[] { weaponUsageIndexChangeMessage });
                    RemoveDeferredClientWeaponUsageIndexChangePayload(
                        weaponUsageIndexChangeMessage.AgentIndex,
                        weaponUsageIndexChangeMessage);
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: replayed deferred client WeaponUsageIndexChangeMessage after battle snapshot apply. " +
                        "AgentIndex=" + weaponUsageIndexChangeMessage.AgentIndex +
                        " SlotIndex=" + weaponUsageIndexChangeMessage.SlotIndex +
                        " UsageIndex=" + weaponUsageIndexChangeMessage.UsageIndex +
                        " Attempts=" + deferredPayload.Attempts +
                        " Source=" + (source ?? "unknown"));
                }
                catch (Exception ex)
                {
                    if (deferredPayload.Attempts == 1 || deferredPayload.Attempts % 20 == 0)
                    {
                        ModLogger.Info(
                            "BattleMapSpawnHandoffPatch: deferred client WeaponUsageIndexChangeMessage replay failed open. " +
                            "AgentIndex=" + weaponUsageIndexChangeMessage.AgentIndex +
                            " SlotIndex=" + weaponUsageIndexChangeMessage.SlotIndex +
                            " UsageIndex=" + weaponUsageIndexChangeMessage.UsageIndex +
                            " Attempts=" + deferredPayload.Attempts +
                            " SnapshotReadiness=" + (snapshotReadinessSummary ?? "unknown") +
                            " Message=" + ex.GetBaseException().Message);
                    }
                }
            }
        }

        private static void TryReplayDeferredClientSetAgentHealth(
            Mission mission,
            string source,
            string snapshotReadinessSummary)
        {
            if (_missionNetworkComponentHandleServerEventSetAgentHealthMethod == null)
                return;

            List<DeferredClientSetAgentHealthPayload> deferredPayloads;
            lock (DeferredClientSetAgentHealthPayloads)
            {
                if (DeferredClientSetAgentHealthPayloads.Count <= 0)
                    return;

                deferredPayloads = DeferredClientSetAgentHealthPayloads
                    .OrderBy(candidate => candidate.Sequence)
                    .ToList();
            }

            MissionNetworkComponent missionNetworkComponent = mission.GetMissionBehavior<MissionNetworkComponent>();
            if (missionNetworkComponent == null)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            foreach (DeferredClientSetAgentHealthPayload deferredPayload in deferredPayloads)
            {
                SetAgentHealth setAgentHealth = deferredPayload?.Message;
                if (setAgentHealth == null)
                    continue;

                if (deferredPayload.LastAttemptUtc != DateTime.MinValue &&
                    nowUtc - deferredPayload.LastAttemptUtc < TimeSpan.FromMilliseconds(100))
                {
                    continue;
                }

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(setAgentHealth.AgentIndex, canBeNull: true);
                if (agent == null || !agent.IsActive())
                    continue;

                deferredPayload.LastAttemptUtc = nowUtc;
                deferredPayload.Attempts++;
                try
                {
                    _missionNetworkComponentHandleServerEventSetAgentHealthMethod.Invoke(
                        missionNetworkComponent,
                        new object[] { setAgentHealth });
                    RemoveDeferredClientSetAgentHealthPayload(setAgentHealth.AgentIndex);
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: replayed deferred client SetAgentHealth after battle snapshot apply. " +
                        "AgentIndex=" + setAgentHealth.AgentIndex +
                        " Health=" + setAgentHealth.Health +
                        " Attempts=" + deferredPayload.Attempts +
                        " Source=" + (source ?? "unknown"));
                }
                catch (Exception ex)
                {
                    if (deferredPayload.Attempts == 1 || deferredPayload.Attempts % 20 == 0)
                    {
                        ModLogger.Info(
                            "BattleMapSpawnHandoffPatch: deferred client SetAgentHealth replay failed open. " +
                            "AgentIndex=" + setAgentHealth.AgentIndex +
                            " Health=" + setAgentHealth.Health +
                            " Attempts=" + deferredPayload.Attempts +
                            " SnapshotReadiness=" + (snapshotReadinessSummary ?? "unknown") +
                            " Message=" + ex.GetBaseException().Message);
                    }
                }
            }
        }

        private static void TryReplayDeferredClientMakeAgentDead(
            Mission mission,
            string source,
            string snapshotReadinessSummary)
        {
            if (_missionNetworkComponentHandleServerEventMakeAgentDeadMethod == null)
                return;

            List<DeferredClientMakeAgentDeadPayload> deferredPayloads;
            lock (DeferredClientMakeAgentDeadPayloads)
            {
                if (DeferredClientMakeAgentDeadPayloads.Count <= 0)
                    return;

                deferredPayloads = DeferredClientMakeAgentDeadPayloads
                    .OrderBy(candidate => candidate.Sequence)
                    .ToList();
            }

            MissionNetworkComponent missionNetworkComponent = mission.GetMissionBehavior<MissionNetworkComponent>();
            if (missionNetworkComponent == null)
                return;

            DateTime nowUtc = DateTime.UtcNow;
            foreach (DeferredClientMakeAgentDeadPayload deferredPayload in deferredPayloads)
            {
                MakeAgentDead makeAgentDead = deferredPayload?.Message;
                if (makeAgentDead == null)
                    continue;

                if (deferredPayload.LastAttemptUtc != DateTime.MinValue &&
                    nowUtc - deferredPayload.LastAttemptUtc < TimeSpan.FromMilliseconds(100))
                {
                    continue;
                }

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(makeAgentDead.AgentIndex, canBeNull: true);
                if (agent == null || !agent.IsActive())
                    continue;

                deferredPayload.LastAttemptUtc = nowUtc;
                deferredPayload.Attempts++;
                try
                {
                    _missionNetworkComponentHandleServerEventMakeAgentDeadMethod.Invoke(
                        missionNetworkComponent,
                        new object[] { makeAgentDead });
                    RemoveDeferredClientMakeAgentDeadPayload(makeAgentDead.AgentIndex);
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: replayed deferred client MakeAgentDead after battle snapshot apply. " +
                        "AgentIndex=" + makeAgentDead.AgentIndex +
                        " IsKilled=" + makeAgentDead.IsKilled +
                        " Attempts=" + deferredPayload.Attempts +
                        " Source=" + (source ?? "unknown"));
                }
                catch (Exception ex)
                {
                    if (deferredPayload.Attempts == 1 || deferredPayload.Attempts % 20 == 0)
                    {
                        ModLogger.Info(
                            "BattleMapSpawnHandoffPatch: deferred client MakeAgentDead replay failed open. " +
                            "AgentIndex=" + makeAgentDead.AgentIndex +
                            " IsKilled=" + makeAgentDead.IsKilled +
                            " Attempts=" + deferredPayload.Attempts +
                            " SnapshotReadiness=" + (snapshotReadinessSummary ?? "unknown") +
                            " Message=" + ex.GetBaseException().Message);
                    }
                }
            }
        }

        private static void RemoveDeferredClientCreateAgentPayload(int agentIndex)
        {
            if (agentIndex < 0)
                return;

            lock (DeferredClientCreateAgentPayloads)
            {
                DeferredClientCreateAgentPayloads.RemoveAll(candidate => candidate?.Message?.AgentIndex == agentIndex);
            }
        }

        private static void RemoveDeferredClientSetAgentActionSetPayload(int agentIndex)
        {
            if (agentIndex < 0)
                return;

            lock (DeferredClientSetAgentActionSetPayloads)
            {
                DeferredClientSetAgentActionSetPayloads.RemoveAll(candidate => candidate?.Message?.AgentIndex == agentIndex);
            }
        }

        private static void RemoveDeferredClientSynchronizeAgentEquipmentPayload(int agentIndex)
        {
            if (agentIndex < 0)
                return;

            lock (DeferredClientSynchronizeAgentEquipmentPayloads)
            {
                DeferredClientSynchronizeAgentEquipmentPayloads.RemoveAll(candidate => candidate?.Message?.AgentIndex == agentIndex);
            }
        }

        private static void RemoveDeferredClientAttachWeaponToWeaponInAgentEquipmentSlotPayload(
            int agentIndex,
            AttachWeaponToWeaponInAgentEquipmentSlot referenceMessage)
        {
            if (agentIndex < 0)
                return;

            lock (DeferredClientAttachWeaponToWeaponInAgentEquipmentSlotPayloads)
            {
                DeferredClientAttachWeaponToWeaponInAgentEquipmentSlotPayloads.RemoveAll(candidate =>
                    candidate?.Message?.AgentIndex == agentIndex &&
                    (referenceMessage == null || candidate.Message.SlotIndex == referenceMessage.SlotIndex));
            }
        }

        private static void RemoveDeferredClientSetWeaponNetworkDataPayload(
            int agentIndex,
            SetWeaponNetworkData referenceMessage)
        {
            if (agentIndex < 0)
                return;

            lock (DeferredClientSetWeaponNetworkDataPayloads)
            {
                DeferredClientSetWeaponNetworkDataPayloads.RemoveAll(candidate =>
                    candidate?.Message?.AgentIndex == agentIndex &&
                    (referenceMessage == null || candidate.Message.WeaponEquipmentIndex == referenceMessage.WeaponEquipmentIndex));
            }
        }

        private static void RemoveDeferredClientSetWeaponAmmoDataPayload(
            int agentIndex,
            SetWeaponAmmoData referenceMessage)
        {
            if (agentIndex < 0)
                return;

            lock (DeferredClientSetWeaponAmmoDataPayloads)
            {
                DeferredClientSetWeaponAmmoDataPayloads.RemoveAll(candidate =>
                    candidate?.Message?.AgentIndex == agentIndex &&
                    (referenceMessage == null ||
                     (candidate.Message.WeaponEquipmentIndex == referenceMessage.WeaponEquipmentIndex &&
                      candidate.Message.AmmoEquipmentIndex == referenceMessage.AmmoEquipmentIndex)));
            }
        }

        private static void RemoveDeferredClientSetWeaponReloadPhasePayload(
            int agentIndex,
            SetWeaponReloadPhase referenceMessage)
        {
            if (agentIndex < 0)
                return;

            lock (DeferredClientSetWeaponReloadPhasePayloads)
            {
                DeferredClientSetWeaponReloadPhasePayloads.RemoveAll(candidate =>
                    candidate?.Message?.AgentIndex == agentIndex &&
                    (referenceMessage == null || candidate.Message.EquipmentIndex == referenceMessage.EquipmentIndex));
            }
        }

        private static void RemoveDeferredClientStartSwitchingWeaponUsageIndexPayload(
            int agentIndex,
            StartSwitchingWeaponUsageIndex referenceMessage)
        {
            if (agentIndex < 0)
                return;

            lock (DeferredClientStartSwitchingWeaponUsageIndexPayloads)
            {
                DeferredClientStartSwitchingWeaponUsageIndexPayloads.RemoveAll(candidate =>
                    candidate?.Message?.AgentIndex == agentIndex &&
                    (referenceMessage == null ||
                     (candidate.Message.EquipmentIndex == referenceMessage.EquipmentIndex &&
                      candidate.Message.UsageIndex == referenceMessage.UsageIndex)));
            }
        }

        private static void RemoveDeferredClientWeaponUsageIndexChangePayload(
            int agentIndex,
            WeaponUsageIndexChangeMessage referenceMessage)
        {
            if (agentIndex < 0)
                return;

            lock (DeferredClientWeaponUsageIndexChangePayloads)
            {
                DeferredClientWeaponUsageIndexChangePayloads.RemoveAll(candidate =>
                    candidate?.Message?.AgentIndex == agentIndex &&
                    (referenceMessage == null ||
                     (candidate.Message.SlotIndex == referenceMessage.SlotIndex &&
                      candidate.Message.UsageIndex == referenceMessage.UsageIndex)));
            }
        }

        private static void RemoveDeferredClientSetWieldedItemIndexPayload(int agentIndex, SetWieldedItemIndex referenceMessage)
        {
            if (agentIndex < 0)
                return;

            lock (DeferredClientSetWieldedItemIndexPayloads)
            {
                DeferredClientSetWieldedItemIndexPayloads.RemoveAll(candidate =>
                    candidate?.Message?.AgentIndex == agentIndex &&
                    (referenceMessage == null ||
                     (candidate.Message.WieldedItemIndex == referenceMessage.WieldedItemIndex &&
                      candidate.Message.IsWieldedOnSpawn == referenceMessage.IsWieldedOnSpawn &&
                      candidate.Message.IsLeftHand == referenceMessage.IsLeftHand)));
            }
        }

        private static void RemoveDeferredClientSetAgentHealthPayload(int agentIndex)
        {
            if (agentIndex < 0)
                return;

            lock (DeferredClientSetAgentHealthPayloads)
            {
                DeferredClientSetAgentHealthPayloads.RemoveAll(candidate => candidate?.Message?.AgentIndex == agentIndex);
            }
        }

        private static void RemoveDeferredClientMakeAgentDeadPayload(int agentIndex)
        {
            if (agentIndex < 0)
                return;

            lock (DeferredClientMakeAgentDeadPayloads)
            {
                DeferredClientMakeAgentDeadPayloads.RemoveAll(candidate => candidate?.Message?.AgentIndex == agentIndex);
            }
        }

        private static void RegisterDeferredMountedHeroCreateAgentPayload(CreateAgent createAgent, string snapshotReadinessSummary)
        {
            if (createAgent == null)
                return;

            lock (DeferredMountedHeroCreateAgentPayloads)
            {
                if (DeferredMountedHeroCreateAgentPayloads.TryGetValue(
                        createAgent.AgentIndex,
                        out DeferredMountedHeroCreateAgentPayload existingPayload) &&
                    existingPayload != null)
                {
                    existingPayload.Message = createAgent;
                    existingPayload.DeferralReason = snapshotReadinessSummary;
                    return;
                }

                DeferredMountedHeroCreateAgentPayloads[createAgent.AgentIndex] =
                    new DeferredMountedHeroCreateAgentPayload
                    {
                        Message = createAgent,
                        DeferredUtc = DateTime.UtcNow,
                        LastAttemptUtc = DateTime.MinValue,
                        Attempts = 0,
                        DeferralReason = snapshotReadinessSummary
                    };
            }
        }

        private static void RemoveDeferredMountedHeroCreateAgentPayload(int agentIndex)
        {
            if (agentIndex < 0)
                return;

            lock (DeferredMountedHeroCreateAgentPayloads)
            {
                DeferredMountedHeroCreateAgentPayloads.Remove(agentIndex);
            }
        }

        private static void MissionNetworkComponent_HandleServerEventCreateAgent_Postfix(GameNetworkMessage baseMessage)
        {
            try
            {
                if (!(baseMessage is CreateAgent createAgent))
                    return;

                Mission mission = Mission.Current;
                if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                    return;

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(createAgent.AgentIndex, canBeNull: true);
                if (agent == null || !agent.IsActive() || agent.IsMount || agent.Team == null || agent.Team.Side == BattleSideEnum.None)
                    return;

                ExactTransferContractRuntimeCache.ObserveClientMaterialized(
                    agent.Index,
                    agent,
                    "battle-map handoff CreateAgent");
                CoopMissionSpawnLogic.ObserveClientCreateAgentPostfix(
                    agent,
                    "battle-map handoff CreateAgent postfix");
                if (createAgent.MountAgentIndex >= 0)
                    CoopMissionSpawnLogic.TryTrackClientMountedHeroMountAgentIndex(agent, createAgent.MountAgentIndex);
                CoopMissionSpawnLogic.TryTrackClientMountedHeroMountAgentIndex(agent);
                bool snapshotReadyForExactVisual =
                    CoopMissionNetworkBridge.IsClientCurrentBattleSnapshotApplied(out string snapshotReadinessSummary);
                bool exactVisualApplied = snapshotReadyForExactVisual &&
                    CoopMissionSpawnLogic.TryFinalizeClientExactCampaignVisualForAgent(
                        mission,
                        agent,
                        preferredEntryId: null,
                        source: "battle-map handoff CreateAgent",
                        allowImmediateApply: false);
                bool troopExactVisualApplied = false;
                if (!exactVisualApplied && snapshotReadyForExactVisual)
                {
                    troopExactVisualApplied = CoopMissionSpawnLogic.TryFinalizeClientExactCampaignTroopVisualForPeerAgent(
                        mission,
                        agent,
                        "battle-map handoff CreateAgent",
                        includeWeaponsForClientRefresh: true,
                        allowImmediateApply: false);
                }
                ExactCreateAgentCorridorDiagnostics.ObserveClientCreateAgentPostfix(
                    createAgent,
                    agent,
                    snapshotReadyForExactVisual,
                    snapshotReadinessSummary,
                    exactVisualApplied || troopExactVisualApplied,
                    "battle-map handoff CreateAgent");
                CoopMissionSpawnLogic.TraceClientMountedHeroNetworkContract(
                    agent,
                    "client-create-agent",
                    "battle-map handoff CreateAgent",
                    "PayloadMissionWeapons={" + BuildMissionEquipmentWeaponSummary(createAgent.MissionEquipment) + "}" +
                    " PayloadMount={" + BuildEquipmentSummary(createAgent.SpawnEquipment, EquipmentIndex.Horse, EquipmentIndex.HorseHarness) + "}" +
                    " PayloadMountAgentIndex=" + createAgent.MountAgentIndex +
                    " PayloadIsPlayerAgent=" + createAgent.IsPlayerAgent +
                    " SnapshotReadyForExactVisual=" + snapshotReadyForExactVisual +
                    " SnapshotReadinessReason=" + (snapshotReadinessSummary ?? "unknown") +
                    " ExactVisualApplied=" + exactVisualApplied +
                    " TroopExactVisualApplied=" + troopExactVisualApplied);
                if (!exactVisualApplied && !troopExactVisualApplied)
                    return;

                ModLogger.Info(
                    "BattleMapSpawnHandoffPatch: applied client exact visuals during CreateAgent handoff. " +
                    "AgentIndex=" + agent.Index +
                    " TeamSide=" + agent.Team.Side +
                    " TroopId=" + (agent.Character?.StringId ?? "null") +
                    " ExactVisualApplied=" + exactVisualApplied +
                    " TroopExactVisualApplied=" + troopExactVisualApplied +
                    " Mission=" + (mission.SceneName ?? "null"));
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: local CreateAgent exact visual finalization failed: " + ex.Message);
            }
        }

        private static Exception MissionNetworkComponent_HandleServerEventCreateAgent_Finalizer(
            Exception __exception,
            GameNetworkMessage baseMessage)
        {
            if (__exception == null)
                return null;

            try
            {
                if (!(baseMessage is CreateAgent createAgent))
                    return __exception;

                Mission mission = Mission.Current;
                if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                    return __exception;

                string failureReason = "create-agent-handler-exception:" + __exception.GetType().Name;
                CoopMissionSpawnLogic.ReportStrictExactHeroTransferFailure(
                    createAgent.AgentIndex,
                    "battle-map handoff CreateAgent finalizer",
                    failureReason);
                ExactTransferContractRuntimeCache.ReportClientFailure(
                    createAgent.AgentIndex,
                    ExactTransferFailureReason.CreateAgentHandlerException,
                    failureReason,
                    "battle-map handoff CreateAgent finalizer");
                ExactCreateAgentCorridorDiagnostics.ObserveClientCreateAgentException(
                    createAgent,
                    __exception,
                    "battle-map handoff CreateAgent finalizer");
                string payloadSummary =
                    "AgentIndex=" + createAgent.AgentIndex +
                    " MountAgentIndex=" + createAgent.MountAgentIndex +
                    " PayloadIsPlayerAgent=" + createAgent.IsPlayerAgent +
                    " ExceptionType=" + __exception.GetType().FullName +
                    " ExceptionMessage=" + __exception.Message;
                ModLogger.Info(
                    "BattleMapSpawnHandoffPatch: CreateAgent handler threw during battle-map handoff. " +
                    payloadSummary);
                ExactBattleRuntimeBundleBridgeFile.AppendContractEvent(
                    "client-create-agent-handler-exception",
                    payloadSummary + " Source=battle-map handoff CreateAgent finalizer");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: CreateAgent finalizer failed open: " + ex.Message);
            }

            return __exception;
        }

        private static bool TryHandleStrictExactHeroCreateAgentViaContract(
            Mission mission,
            CreateAgent createAgent,
            out bool strictExactCandidate)
        {
            strictExactCandidate = false;
            if (mission == null || createAgent == null)
                return false;

            if (!CoopMissionSpawnLogic.TryResolveClientStrictExactHeroCreateAgentContract(
                    createAgent,
                    out string entryId,
                    out RosterEntryState entryState,
                    out ExactTransferSpawnContract contract,
                    out ExactTransferValidationResult validation,
                    out string resolutionSource))
            {
                return false;
            }

            strictExactCandidate = true;
            if (contract == null || validation == null || !validation.IsValid)
            {
                ModLogger.Info(
                    "BattleMapSpawnHandoffPatch: strict exact CreateAgent adapter skipped because contract validation failed. " +
                    "AgentIndex=" + createAgent.AgentIndex +
                    " EntryId=" + (entryId ?? "null") +
                    " ResolutionSource=" + (resolutionSource ?? "null") +
                    " " + ExactTransferContractRuntimeCache.BuildValidationSummary(entryId));
                return false;
            }

            if (contract.SpawnPolicy == null ||
                !contract.SpawnPolicy.UseStrictExactHeroPath ||
                !contract.SpawnPolicy.ForbidSurrogatePrimaryMaterialization)
            {
                return false;
            }

            BasicCharacterObject character = ResolveStrictExactHeroCreateAgentCharacter(createAgent, contract);
            if (character == null)
            {
                ModLogger.Info(
                    "BattleMapSpawnHandoffPatch: strict exact CreateAgent adapter skipped because contract character could not be resolved. " +
                    "AgentIndex=" + createAgent.AgentIndex +
                    " EntryId=" + (entryId ?? "null") +
                    " PayloadCharacter=" + (createAgent.Character?.StringId ?? "null"));
                return false;
            }

            Team teamFromTeamIndex = Mission.MissionNetworkHelper.GetTeamFromTeamIndex(createAgent.TeamIndex);
            if (teamFromTeamIndex == null)
            {
                ModLogger.Info(
                    "BattleMapSpawnHandoffPatch: strict exact CreateAgent adapter skipped because mission team was unavailable. " +
                    "AgentIndex=" + createAgent.AgentIndex +
                    " TeamIndex=" + createAgent.TeamIndex +
                    " EntryId=" + (entryId ?? "null"));
                return false;
            }

            Formation formation = null;
            if (createAgent.FormationIndex >= 0 && !GameNetwork.IsReplay)
                formation = teamFromTeamIndex.GetFormation((FormationClass)createAgent.FormationIndex);

            Banner banner = ResolveStrictExactHeroCreateAgentBanner(teamFromTeamIndex, formation);
            Equipment createTimeSpawnEquipment = ResolveStrictExactHeroCreateAgentSpawnEquipment(contract, createAgent);
            MissionEquipment createTimeMissionEquipment = ResolveStrictExactHeroCreateAgentMissionEquipment(
                createTimeSpawnEquipment,
                banner,
                createAgent);
            if (createTimeSpawnEquipment == null || createTimeMissionEquipment == null)
                return false;

            try
            {
                AgentBuildData buildData = new AgentBuildData(character)
                    .Monster(createAgent.Monster)
                    .TroopOrigin(new BasicBattleAgentOrigin(character))
                    .Equipment(createTimeSpawnEquipment)
                    .EquipmentSeed(createAgent.BodyPropertiesSeed)
                    .InitialPosition(createAgent.Position)
                    .InitialDirection(createAgent.Direction.Normalized())
                    .MissionEquipment(createTimeMissionEquipment)
                    .Team(teamFromTeamIndex)
                    .Index(createAgent.AgentIndex)
                    .MountIndex(createAgent.MountAgentIndex)
                    .IsFemale(contract.Body?.IsFemale ?? createAgent.IsFemale)
                    .ClothingColor1(createAgent.ClothingColor1)
                    .ClothingColor2(createAgent.ClothingColor2)
                    .BodyProperties(contract.Body.BodyProperties);
                if (contract?.Body?.Age is int exactAge)
                    buildData.Age(exactAge);
                if (formation != null)
                    buildData.Formation(formation);
                if (banner != null)
                    buildData.Banner(banner);

                Agent agent = mission.SpawnAgent(buildData);
                if (agent == null)
                {
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: strict exact CreateAgent adapter returned null agent. " +
                        "AgentIndex=" + createAgent.AgentIndex +
                        " EntryId=" + (entryId ?? "null"));
                    return false;
                }

                ExactTransferContractRuntimeCache.ObserveClientMaterialized(
                    agent.Index,
                    agent,
                    "battle-map handoff strict exact CreateAgent");
                if (createAgent.MountAgentIndex >= 0)
                    CoopMissionSpawnLogic.TryTrackClientMountedHeroMountAgentIndex(agent, createAgent.MountAgentIndex, entryId);
                CoopMissionSpawnLogic.TryTrackClientMountedHeroMountAgentIndex(agent);

                string payloadSummary =
                    "EntryId=" + (entryId ?? "null") +
                    " ResolutionSource=" + (resolutionSource ?? "null") +
                    " PayloadCharacter=" + (createAgent.Character?.StringId ?? "null") +
                    " ContractCharacter=" + (character.StringId ?? "null") +
                    " MountAgentIndex=" + createAgent.MountAgentIndex +
                    " FormationIndex=" + createAgent.FormationIndex +
                    " StrippedRemotePeer=True" +
                    " CanonicalizedCharacter=False";
                ModLogger.Info(
                    "BattleMapSpawnHandoffPatch: handled strict exact CreateAgent via contract adapter. " +
                    "AgentIndex=" + agent.Index +
                    " TeamSide=" + agent.Team?.Side +
                    " " + payloadSummary +
                    " " + ExactTransferContractRuntimeCache.BuildRuntimeStateSummary(entryId));
                ExactBattleRuntimeBundleBridgeFile.AppendContractEvent(
                    "client-create-agent-contract-adapter",
                    "AgentIndex=" + agent.Index +
                    " " + payloadSummary +
                    " Source=battle-map handoff strict exact CreateAgent");
                return true;
            }
            catch (Exception ex)
            {
                string failureReason = "strict-create-agent-adapter-exception:" + ex.GetType().Name;
                ExactTransferContractRuntimeCache.ReportClientFailure(
                    createAgent.AgentIndex,
                    ExactTransferFailureReason.CreateAgentHandlerException,
                    failureReason,
                    "battle-map handoff strict exact CreateAgent");
                ModLogger.Info(
                    "BattleMapSpawnHandoffPatch: strict exact CreateAgent adapter failed. " +
                    "AgentIndex=" + createAgent.AgentIndex +
                    " EntryId=" + (entryId ?? "null") +
                    " ResolutionSource=" + (resolutionSource ?? "null") +
                    " Message=" + ex.Message);
                ExactBattleRuntimeBundleBridgeFile.AppendContractEvent(
                    "client-create-agent-contract-adapter-failed",
                    "AgentIndex=" + createAgent.AgentIndex +
                    " EntryId=" + (entryId ?? "null") +
                    " FailureReason=" + failureReason +
                    " Message=" + ex.Message +
                    " Source=battle-map handoff strict exact CreateAgent");
                return false;
            }
        }

        private static bool TryHandleMountedHeroCreateAgentViaPayloadAdapter(
            Mission mission,
            CreateAgent createAgent,
            out bool mountedHeroPayloadCandidate)
        {
            mountedHeroPayloadCandidate = IsMountedHeroTemplatePayload(createAgent);
            if (!mountedHeroPayloadCandidate || mission == null || createAgent == null)
                return false;

            Team teamFromTeamIndex = Mission.MissionNetworkHelper.GetTeamFromTeamIndex(createAgent.TeamIndex);
            if (teamFromTeamIndex == null)
                return false;

            Formation formation = null;
            if (createAgent.FormationIndex >= 0 && !GameNetwork.IsReplay)
                formation = teamFromTeamIndex.GetFormation((FormationClass)createAgent.FormationIndex);

            Banner banner = ResolveStrictExactHeroCreateAgentBanner(teamFromTeamIndex, formation);
            Equipment createTimeSpawnEquipment = createAgent.SpawnEquipment?.Clone(false);
            MissionEquipment createTimeMissionEquipment =
                createAgent.MissionEquipment ??
                (createTimeSpawnEquipment != null ? new MissionEquipment(createTimeSpawnEquipment, banner) : null);
            if (createTimeSpawnEquipment == null || createTimeMissionEquipment == null || createAgent.Character == null)
                return false;

            try
            {
                AgentBuildData buildData = new AgentBuildData(createAgent.Character)
                    .Monster(createAgent.Monster)
                    .TroopOrigin(new BasicBattleAgentOrigin(createAgent.Character))
                    .Equipment(createTimeSpawnEquipment)
                    .EquipmentSeed(createAgent.BodyPropertiesSeed)
                    .InitialPosition(createAgent.Position)
                    .InitialDirection(createAgent.Direction.Normalized())
                    .MissionEquipment(createTimeMissionEquipment)
                    .Team(teamFromTeamIndex)
                    .Index(createAgent.AgentIndex)
                    .MountIndex(createAgent.MountAgentIndex)
                    .IsFemale(createAgent.IsFemale)
                    .ClothingColor1(createAgent.ClothingColor1)
                    .ClothingColor2(createAgent.ClothingColor2)
                    .BodyProperties(createAgent.BodyPropertiesValue);
                if (formation != null)
                    buildData.Formation(formation);
                if (banner != null)
                    buildData.Banner(banner);

                Agent agent = mission.SpawnAgent(buildData);
                if (agent == null)
                    return false;

                if (createAgent.MountAgentIndex >= 0)
                    CoopMissionSpawnLogic.TryTrackClientMountedHeroMountAgentIndex(agent, createAgent.MountAgentIndex);
                CoopMissionSpawnLogic.TryTrackClientMountedHeroMountAgentIndex(agent);

                string payloadSummary =
                    "PayloadCharacter=" + (createAgent.Character?.StringId ?? "null") +
                    " MountAgentIndex=" + createAgent.MountAgentIndex +
                    " FormationIndex=" + createAgent.FormationIndex +
                    " PayloadMissionWeapons={" + BuildMissionEquipmentWeaponSummary(createTimeMissionEquipment) + "}" +
                    " PayloadMount={" + BuildEquipmentSummary(createTimeSpawnEquipment, EquipmentIndex.Horse, EquipmentIndex.HorseHarness) + "}" +
                    " Source=battle-map payload mounted hero CreateAgent";
                ModLogger.Info(
                    "BattleMapSpawnHandoffPatch: handled mounted hero CreateAgent via payload adapter. " +
                    "AgentIndex=" + agent.Index +
                    " TeamSide=" + (agent.Team?.Side.ToString() ?? "None") +
                    " " + payloadSummary);
                ExactBattleRuntimeBundleBridgeFile.AppendContractEvent(
                    "client-create-agent-payload-mounted-hero-adapter",
                    "AgentIndex=" + agent.Index + " " + payloadSummary);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "BattleMapSpawnHandoffPatch: mounted hero CreateAgent payload adapter failed. " +
                    "AgentIndex=" + createAgent.AgentIndex +
                    " PayloadCharacter=" + (createAgent.Character?.StringId ?? "null") +
                    " Message=" + ex.Message);
                return false;
            }
        }

        private static bool IsMountedHeroTemplatePayload(CreateAgent createAgent)
        {
            if (createAgent?.Character == null || createAgent.MountAgentIndex < 0)
                return false;

            string characterId = createAgent.Character.StringId ?? string.Empty;
            return createAgent.Character.IsHero ||
                   characterId.EndsWith("_hero", StringComparison.Ordinal);
        }

        private static BasicCharacterObject ResolveStrictExactHeroCreateAgentCharacter(
            CreateAgent createAgent,
            ExactTransferSpawnContract contract)
        {
            if (contract?.EntryId != null)
            {
                BasicCharacterObject contractCharacter = BattleSnapshotRuntimeState.TryResolveCharacterObject(contract.EntryId);
                if (contractCharacter != null)
                    return contractCharacter;
            }

            if (!string.IsNullOrWhiteSpace(contract?.Identity?.NativeMultiplayerCharacterId))
            {
                try
                {
                    BasicCharacterObject character =
                        MBObjectManager.Instance.GetObject<BasicCharacterObject>(contract.Identity.NativeMultiplayerCharacterId);
                    if (character != null)
                        return character;
                }
                catch
                {
                }
            }

            return createAgent?.Character;
        }

        private static Equipment ResolveStrictExactHeroCreateAgentSpawnEquipment(
            ExactTransferSpawnContract contract,
            CreateAgent createAgent)
        {
            if (contract?.Equipment?.SpawnEquipment != null)
                return contract.Equipment.SpawnEquipment.Clone(false);

            return createAgent?.SpawnEquipment?.Clone(false);
        }

        private static MissionEquipment ResolveStrictExactHeroCreateAgentMissionEquipment(
            Equipment createTimeSpawnEquipment,
            Banner banner,
            CreateAgent createAgent)
        {
            if (createTimeSpawnEquipment != null)
                return new MissionEquipment(createTimeSpawnEquipment, banner);

            return createAgent?.MissionEquipment;
        }

        private static Banner ResolveStrictExactHeroCreateAgentBanner(Team team, Formation formation)
        {
            if (team == null)
                return null;

            if (formation != null && !string.IsNullOrEmpty(formation.BannerCode))
                return formation.Banner ?? (formation.Banner = new Banner(formation.BannerCode, team.Color, team.Color2));

            return null;
        }

        private static void CanonicalizeCreateAgentPayloadForBattleMap(CreateAgent createAgent)
        {
            if (createAgent == null)
                return;

            bool coercedRemotePlayerAgent = false;
            bool strippedRemotePeer = false;
            bool canonicalizedCharacter = false;
            bool repairedBodyPropertyRange = false;
            string characterSource = null;
            string bodyPropertyRangeSource = null;

            canonicalizedCharacter = TryCanonicalizeCreateAgentCharacterToResolvedSurrogate(
                createAgent,
                out characterSource);

            if (ShouldCoerceRemotePlayerCreateAgentToNonPlayer(createAgent))
            {
                TrySetInstanceMemberValue(createAgent, "IsPlayerAgent", false);
                TrySetInstanceMemberValue(createAgent, "<IsPlayerAgent>k__BackingField", false);
                coercedRemotePlayerAgent = !createAgent.IsPlayerAgent;
            }

            if (ShouldStripRemotePeerFromCreateAgentPayload(createAgent))
            {
                TrySetInstanceMemberValue(createAgent, "Peer", null);
                TrySetInstanceMemberValue(createAgent, "<Peer>k__BackingField", null);
                strippedRemotePeer = createAgent.Peer == null;
            }

            repairedBodyPropertyRange = TryEnsureCreateAgentCharacterBodyPropertyRange(
                createAgent.Character,
                out bodyPropertyRangeSource);

            if (!coercedRemotePlayerAgent &&
                !strippedRemotePeer &&
                !canonicalizedCharacter &&
                !repairedBodyPropertyRange)
            {
                return;
            }

            string details =
                "AgentIndex=" + createAgent.AgentIndex +
                " Character=" + (createAgent.Character?.StringId ?? "null") +
                " PeerIndex=" + (createAgent.Peer?.Index.ToString() ?? "null") +
                " MountAgentIndex=" + createAgent.MountAgentIndex +
                " CoercedRemotePlayerAgent=" + coercedRemotePlayerAgent +
                " StrippedRemotePeer=" + strippedRemotePeer +
                " EffectiveIsPlayerAgent=" + createAgent.IsPlayerAgent +
                " CanonicalizedCharacter=" + canonicalizedCharacter +
                " CharacterSource=" + (characterSource ?? "none") +
                " RepairedBodyPropertyRange=" + repairedBodyPropertyRange +
                " BodyPropertyRangeSource=" + (bodyPropertyRangeSource ?? "none");
            ModLogger.Info(
                "BattleMapSpawnHandoffPatch: canonicalized CreateAgent payload before native handler. " +
                details);
            ExactBattleRuntimeBundleBridgeFile.AppendContractEvent(
                "client-create-agent-payload-canonicalized",
                details + " Source=battle-map handoff CreateAgent prefix");
            ExactCreateAgentCorridorDiagnostics.ObserveClientCreateAgentMutation(
                createAgent,
                details,
                "battle-map handoff CreateAgent prefix");
        }

        private static bool ShouldCoerceRemotePlayerCreateAgentToNonPlayer(CreateAgent createAgent)
        {
            if (createAgent == null ||
                !createAgent.IsPlayerAgent ||
                createAgent.Peer == null ||
                createAgent.Peer.IsMine ||
                createAgent.MountAgentIndex < 0)
            {
                return false;
            }

            string characterId = createAgent.Character?.StringId ?? string.Empty;
            return createAgent.Character?.IsHero == true ||
                   characterId.EndsWith("_hero", StringComparison.Ordinal);
        }

        private static bool ShouldStripRemotePeerFromCreateAgentPayload(CreateAgent createAgent)
        {
            if (createAgent?.Peer == null || createAgent.Peer.IsMine || createAgent.MountAgentIndex < 0)
                return false;

            string characterId = createAgent.Character?.StringId ?? string.Empty;
            return createAgent.Character?.IsHero == true ||
                   characterId.EndsWith("_hero", StringComparison.Ordinal);
        }

        private static bool TryCanonicalizeCreateAgentCharacterToResolvedSurrogate(
            CreateAgent createAgent,
            out string source)
        {
            source = null;
            if (createAgent?.Character == null)
                return false;

            if (!CampaignMultiplayerHeroClassResolver.TryResolve(
                    createAgent.Character,
                    out MultiplayerClassDivisions.MPHeroClass resolvedClass,
                    out bool treatAsTroop,
                    out string _))
            {
                return false;
            }

            BasicCharacterObject canonicalCharacter = treatAsTroop
                ? resolvedClass?.TroopCharacter ?? resolvedClass?.HeroCharacter
                : resolvedClass?.HeroCharacter ?? resolvedClass?.TroopCharacter;
            if (canonicalCharacter == null)
                return false;

            string originalCharacterId = createAgent.Character.StringId ?? string.Empty;
            string canonicalCharacterId = canonicalCharacter.StringId ?? string.Empty;
            if (ReferenceEquals(canonicalCharacter, createAgent.Character) ||
                string.Equals(originalCharacterId, canonicalCharacterId, StringComparison.Ordinal))
            {
                return false;
            }

            TrySetInstanceMemberValue(createAgent, "Character", canonicalCharacter);
            TrySetInstanceMemberValue(createAgent, "<Character>k__BackingField", canonicalCharacter);
            source = (treatAsTroop ? "resolved-class-troop" : "resolved-class-hero") +
                     ":" + canonicalCharacterId;
            return ReferenceEquals(createAgent.Character, canonicalCharacter) ||
                   string.Equals(createAgent.Character?.StringId, canonicalCharacterId, StringComparison.Ordinal);
        }

        private static bool TryEnsureCreateAgentCharacterBodyPropertyRange(
            BasicCharacterObject character,
            out string source)
        {
            source = null;
            if (character == null)
                return false;

            try
            {
                if (character.BodyPropertyRange != null)
                    return false;
            }
            catch
            {
            }

            if (!CampaignMultiplayerHeroClassResolver.TryResolve(
                    character,
                    out MultiplayerClassDivisions.MPHeroClass resolvedClass,
                    out bool treatAsTroop,
                    out string _))
            {
                return false;
            }

            var fallbackCharacters = new List<(BasicCharacterObject Character, string Source)>();
            if (treatAsTroop)
            {
                fallbackCharacters.Add((resolvedClass?.TroopCharacter, "resolved-class-troop"));
                fallbackCharacters.Add((resolvedClass?.HeroCharacter, "resolved-class-hero"));
            }
            else
            {
                fallbackCharacters.Add((resolvedClass?.HeroCharacter, "resolved-class-hero"));
                fallbackCharacters.Add((resolvedClass?.TroopCharacter, "resolved-class-troop"));
            }

            foreach ((BasicCharacterObject fallbackCharacter, string fallbackSource) in fallbackCharacters)
            {
                if (fallbackCharacter == null || ReferenceEquals(fallbackCharacter, character))
                    continue;

                MBBodyProperty fallbackRange = null;
                try
                {
                    fallbackRange = fallbackCharacter.BodyPropertyRange;
                }
                catch
                {
                }

                if (fallbackRange == null)
                    continue;

                TrySetInstanceMemberValue(character, "BodyPropertyRange", fallbackRange);
                TrySetInstanceMemberValue(character, "<BodyPropertyRange>k__BackingField", fallbackRange);
                source = fallbackSource + ":" + (fallbackCharacter.StringId ?? "null");
                try
                {
                    return character.BodyPropertyRange != null;
                }
                catch
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MissionNetworkComponent_HandleServerEventSynchronizeAgentEquipment_Prefix(GameNetworkMessage baseMessage)
        {
            try
            {
                if (!(baseMessage is SynchronizeAgentSpawnEquipment synchronizeAgentSpawnEquipment))
                    return true;

                Mission mission = Mission.Current;
                if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                    return true;

                if (!ShouldUseSafeStringIdCreateAgentPathOnClient(mission))
                    return true;

                if (!CoopMissionNetworkBridge.IsClientCurrentBattleSnapshotApplied(out string snapshotReadinessSummary))
                {
                    RegisterDeferredClientSynchronizeAgentEquipmentPayload(
                        synchronizeAgentSpawnEquipment,
                        "snapshot-not-ready:" + (snapshotReadinessSummary ?? "unknown"));
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: deferred client SynchronizeAgentSpawnEquipment until current battle snapshot is applied. " +
                        "AgentIndex=" + synchronizeAgentSpawnEquipment.AgentIndex +
                        " Reason=" + (snapshotReadinessSummary ?? "unknown"));
                    return false;
                }

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(synchronizeAgentSpawnEquipment.AgentIndex, canBeNull: true);
                if (agent == null && HasDeferredClientCreateAgentPayload(synchronizeAgentSpawnEquipment.AgentIndex))
                {
                    RegisterDeferredClientSynchronizeAgentEquipmentPayload(
                        synchronizeAgentSpawnEquipment,
                        "agent-createagent-deferred");
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: deferred client SynchronizeAgentSpawnEquipment because CreateAgent is still deferred. " +
                        "AgentIndex=" + synchronizeAgentSpawnEquipment.AgentIndex);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: SynchronizeAgentSpawnEquipment prefix failed open: " + ex.Message);
                return true;
            }
        }

        private static bool MissionNetworkComponent_HandleServerEventAttachWeaponToWeaponInAgentEquipmentSlot_Prefix(GameNetworkMessage baseMessage)
        {
            try
            {
                if (!(baseMessage is AttachWeaponToWeaponInAgentEquipmentSlot attachWeapon))
                    return true;

                Mission mission = Mission.Current;
                if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                    return true;

                if (!ShouldUseSafeStringIdCreateAgentPathOnClient(mission))
                    return true;

                if (!CoopMissionNetworkBridge.IsClientCurrentBattleSnapshotApplied(out string snapshotReadinessSummary))
                {
                    RegisterDeferredClientAttachWeaponToWeaponInAgentEquipmentSlotPayload(
                        attachWeapon,
                        "snapshot-not-ready:" + (snapshotReadinessSummary ?? "unknown"));
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: deferred client AttachWeaponToWeaponInAgentEquipmentSlot until current battle snapshot is applied. " +
                        "AgentIndex=" + attachWeapon.AgentIndex +
                        " SlotIndex=" + attachWeapon.SlotIndex +
                        " Reason=" + (snapshotReadinessSummary ?? "unknown"));
                    return false;
                }

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(attachWeapon.AgentIndex, canBeNull: true);
                if (agent == null &&
                    (HasDeferredClientCreateAgentPayload(attachWeapon.AgentIndex) ||
                     HasAnyDeferredClientAgentBootstrapPayload(attachWeapon.AgentIndex)))
                {
                    RegisterDeferredClientAttachWeaponToWeaponInAgentEquipmentSlotPayload(
                        attachWeapon,
                        "agent-bootstrap-deferred");
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: deferred client AttachWeaponToWeaponInAgentEquipmentSlot because agent bootstrap is still deferred. " +
                        "AgentIndex=" + attachWeapon.AgentIndex +
                        " SlotIndex=" + attachWeapon.SlotIndex);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: AttachWeaponToWeaponInAgentEquipmentSlot prefix failed open: " + ex.Message);
                return true;
            }
        }

        private static bool MissionNetworkComponent_HandleServerEventSetWeaponNetworkData_Prefix(GameNetworkMessage baseMessage)
        {
            try
            {
                if (!(baseMessage is SetWeaponNetworkData setWeaponNetworkData))
                    return true;

                Mission mission = Mission.Current;
                if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                    return true;

                if (!ShouldUseSafeStringIdCreateAgentPathOnClient(mission))
                    return true;

                if (!CoopMissionNetworkBridge.IsClientCurrentBattleSnapshotApplied(out string snapshotReadinessSummary))
                {
                    RegisterDeferredClientSetWeaponNetworkDataPayload(
                        setWeaponNetworkData,
                        "snapshot-not-ready:" + (snapshotReadinessSummary ?? "unknown"));
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: deferred client SetWeaponNetworkData until current battle snapshot is applied. " +
                        "AgentIndex=" + setWeaponNetworkData.AgentIndex +
                        " WeaponEquipmentIndex=" + setWeaponNetworkData.WeaponEquipmentIndex +
                        " Reason=" + (snapshotReadinessSummary ?? "unknown"));
                    return false;
                }

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(setWeaponNetworkData.AgentIndex, canBeNull: true);
                if (agent == null &&
                    (HasDeferredClientCreateAgentPayload(setWeaponNetworkData.AgentIndex) ||
                     HasAnyDeferredClientAgentBootstrapPayload(setWeaponNetworkData.AgentIndex)))
                {
                    RegisterDeferredClientSetWeaponNetworkDataPayload(
                        setWeaponNetworkData,
                        "agent-bootstrap-deferred");
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: deferred client SetWeaponNetworkData because agent bootstrap is still deferred. " +
                        "AgentIndex=" + setWeaponNetworkData.AgentIndex +
                        " WeaponEquipmentIndex=" + setWeaponNetworkData.WeaponEquipmentIndex);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: SetWeaponNetworkData prefix failed open: " + ex.Message);
                return true;
            }
        }

        private static bool MissionNetworkComponent_HandleServerEventSetWeaponAmmoData_Prefix(GameNetworkMessage baseMessage)
        {
            try
            {
                if (!(baseMessage is SetWeaponAmmoData setWeaponAmmoData))
                    return true;

                Mission mission = Mission.Current;
                if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                    return true;

                if (!ShouldUseSafeStringIdCreateAgentPathOnClient(mission))
                    return true;

                if (!CoopMissionNetworkBridge.IsClientCurrentBattleSnapshotApplied(out string snapshotReadinessSummary))
                {
                    RegisterDeferredClientSetWeaponAmmoDataPayload(
                        setWeaponAmmoData,
                        "snapshot-not-ready:" + (snapshotReadinessSummary ?? "unknown"));
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: deferred client SetWeaponAmmoData until current battle snapshot is applied. " +
                        "AgentIndex=" + setWeaponAmmoData.AgentIndex +
                        " WeaponEquipmentIndex=" + setWeaponAmmoData.WeaponEquipmentIndex +
                        " AmmoEquipmentIndex=" + setWeaponAmmoData.AmmoEquipmentIndex +
                        " Reason=" + (snapshotReadinessSummary ?? "unknown"));
                    return false;
                }

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(setWeaponAmmoData.AgentIndex, canBeNull: true);
                if (agent == null &&
                    (HasDeferredClientCreateAgentPayload(setWeaponAmmoData.AgentIndex) ||
                     HasAnyDeferredClientAgentBootstrapPayload(setWeaponAmmoData.AgentIndex)))
                {
                    RegisterDeferredClientSetWeaponAmmoDataPayload(
                        setWeaponAmmoData,
                        "agent-bootstrap-deferred");
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: deferred client SetWeaponAmmoData because agent bootstrap is still deferred. " +
                        "AgentIndex=" + setWeaponAmmoData.AgentIndex +
                        " WeaponEquipmentIndex=" + setWeaponAmmoData.WeaponEquipmentIndex +
                        " AmmoEquipmentIndex=" + setWeaponAmmoData.AmmoEquipmentIndex);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: SetWeaponAmmoData prefix failed open: " + ex.Message);
                return true;
            }
        }

        private static bool MissionNetworkComponent_HandleServerEventSetWeaponReloadPhase_Prefix(GameNetworkMessage baseMessage)
        {
            try
            {
                if (!(baseMessage is SetWeaponReloadPhase setWeaponReloadPhase))
                    return true;

                Mission mission = Mission.Current;
                if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                    return true;

                if (!ShouldUseSafeStringIdCreateAgentPathOnClient(mission))
                    return true;

                if (!CoopMissionNetworkBridge.IsClientCurrentBattleSnapshotApplied(out string snapshotReadinessSummary))
                {
                    RegisterDeferredClientSetWeaponReloadPhasePayload(
                        setWeaponReloadPhase,
                        "snapshot-not-ready:" + (snapshotReadinessSummary ?? "unknown"));
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: deferred client SetWeaponReloadPhase until current battle snapshot is applied. " +
                        "AgentIndex=" + setWeaponReloadPhase.AgentIndex +
                        " EquipmentIndex=" + setWeaponReloadPhase.EquipmentIndex +
                        " Reason=" + (snapshotReadinessSummary ?? "unknown"));
                    return false;
                }

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(setWeaponReloadPhase.AgentIndex, canBeNull: true);
                if (agent == null &&
                    (HasDeferredClientCreateAgentPayload(setWeaponReloadPhase.AgentIndex) ||
                     HasAnyDeferredClientAgentBootstrapPayload(setWeaponReloadPhase.AgentIndex)))
                {
                    RegisterDeferredClientSetWeaponReloadPhasePayload(
                        setWeaponReloadPhase,
                        "agent-bootstrap-deferred");
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: deferred client SetWeaponReloadPhase because agent bootstrap is still deferred. " +
                        "AgentIndex=" + setWeaponReloadPhase.AgentIndex +
                        " EquipmentIndex=" + setWeaponReloadPhase.EquipmentIndex);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: SetWeaponReloadPhase prefix failed open: " + ex.Message);
                return true;
            }
        }

        private static bool MissionNetworkComponent_HandleServerEventStartSwitchingWeaponUsageIndex_Prefix(GameNetworkMessage baseMessage)
        {
            try
            {
                if (!(baseMessage is StartSwitchingWeaponUsageIndex startSwitchingWeaponUsageIndex))
                    return true;

                Mission mission = Mission.Current;
                if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                    return true;

                if (!ShouldUseSafeStringIdCreateAgentPathOnClient(mission))
                    return true;

                if (!CoopMissionNetworkBridge.IsClientCurrentBattleSnapshotApplied(out string snapshotReadinessSummary))
                {
                    RegisterDeferredClientStartSwitchingWeaponUsageIndexPayload(
                        startSwitchingWeaponUsageIndex,
                        "snapshot-not-ready:" + (snapshotReadinessSummary ?? "unknown"));
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: deferred client StartSwitchingWeaponUsageIndex until current battle snapshot is applied. " +
                        "AgentIndex=" + startSwitchingWeaponUsageIndex.AgentIndex +
                        " EquipmentIndex=" + startSwitchingWeaponUsageIndex.EquipmentIndex +
                        " UsageIndex=" + startSwitchingWeaponUsageIndex.UsageIndex +
                        " Reason=" + (snapshotReadinessSummary ?? "unknown"));
                    return false;
                }

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(startSwitchingWeaponUsageIndex.AgentIndex, canBeNull: true);
                if (agent == null &&
                    (HasDeferredClientCreateAgentPayload(startSwitchingWeaponUsageIndex.AgentIndex) ||
                     HasAnyDeferredClientAgentBootstrapPayload(startSwitchingWeaponUsageIndex.AgentIndex)))
                {
                    RegisterDeferredClientStartSwitchingWeaponUsageIndexPayload(
                        startSwitchingWeaponUsageIndex,
                        "agent-bootstrap-deferred");
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: deferred client StartSwitchingWeaponUsageIndex because agent bootstrap is still deferred. " +
                        "AgentIndex=" + startSwitchingWeaponUsageIndex.AgentIndex +
                        " EquipmentIndex=" + startSwitchingWeaponUsageIndex.EquipmentIndex +
                        " UsageIndex=" + startSwitchingWeaponUsageIndex.UsageIndex);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: StartSwitchingWeaponUsageIndex prefix failed open: " + ex.Message);
                return true;
            }
        }

        private static bool MissionNetworkComponent_HandleServerEventWeaponUsageIndexChangeMessage_Prefix(GameNetworkMessage baseMessage)
        {
            try
            {
                if (!(baseMessage is WeaponUsageIndexChangeMessage weaponUsageIndexChangeMessage))
                    return true;

                Mission mission = Mission.Current;
                if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                    return true;

                if (!ShouldUseSafeStringIdCreateAgentPathOnClient(mission))
                    return true;

                if (!CoopMissionNetworkBridge.IsClientCurrentBattleSnapshotApplied(out string snapshotReadinessSummary))
                {
                    RegisterDeferredClientWeaponUsageIndexChangePayload(
                        weaponUsageIndexChangeMessage,
                        "snapshot-not-ready:" + (snapshotReadinessSummary ?? "unknown"));
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: deferred client WeaponUsageIndexChangeMessage until current battle snapshot is applied. " +
                        "AgentIndex=" + weaponUsageIndexChangeMessage.AgentIndex +
                        " SlotIndex=" + weaponUsageIndexChangeMessage.SlotIndex +
                        " UsageIndex=" + weaponUsageIndexChangeMessage.UsageIndex +
                        " Reason=" + (snapshotReadinessSummary ?? "unknown"));
                    return false;
                }

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(weaponUsageIndexChangeMessage.AgentIndex, canBeNull: true);
                if (agent == null &&
                    (HasDeferredClientCreateAgentPayload(weaponUsageIndexChangeMessage.AgentIndex) ||
                     HasAnyDeferredClientAgentBootstrapPayload(weaponUsageIndexChangeMessage.AgentIndex)))
                {
                    RegisterDeferredClientWeaponUsageIndexChangePayload(
                        weaponUsageIndexChangeMessage,
                        "agent-bootstrap-deferred");
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: deferred client WeaponUsageIndexChangeMessage because agent bootstrap is still deferred. " +
                        "AgentIndex=" + weaponUsageIndexChangeMessage.AgentIndex +
                        " SlotIndex=" + weaponUsageIndexChangeMessage.SlotIndex +
                        " UsageIndex=" + weaponUsageIndexChangeMessage.UsageIndex);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: WeaponUsageIndexChangeMessage prefix failed open: " + ex.Message);
                return true;
            }
        }

        private static void MissionNetworkComponent_HandleServerEventSynchronizeAgentEquipment_Postfix(GameNetworkMessage baseMessage)
        {
            try
            {
                if (!(baseMessage is SynchronizeAgentSpawnEquipment synchronizeAgentSpawnEquipment))
                    return;

                Mission mission = Mission.Current;
                if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                    return;

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(synchronizeAgentSpawnEquipment.AgentIndex, canBeNull: true);
                if (agent == null || !agent.IsActive() || agent.IsMount || agent.Team == null || agent.Team.Side == BattleSideEnum.None)
                    return;

                ExactTransferContractRuntimeCache.ObserveClientEquipmentSynchronized(
                    agent.Index,
                    "battle-map handoff SynchronizeAgentSpawnEquipment");
                CoopMissionSpawnLogic.ObserveClientSynchronizeAgentEquipment(
                    agent.Index,
                    "battle-map handoff SynchronizeAgentSpawnEquipment");
                ExactCreateAgentCorridorDiagnostics.ObserveClientSynchronizeAgentEquipment(
                    synchronizeAgentSpawnEquipment,
                    agent,
                    "battle-map handoff SynchronizeAgentSpawnEquipment");
                bool deferImmediateExactVisualFinalize =
                    agent.MissionPeer != null &&
                    ShouldDeferImmediateClientExactVisualFinalize(agent);
                bool heroExactVisualApplied = CoopMissionSpawnLogic.TryFinalizeClientExactCampaignVisualForAgent(
                    mission,
                    agent,
                    preferredEntryId: null,
                    source: "battle-map handoff SynchronizeAgentSpawnEquipment",
                    includeWeaponsForClientRefresh: true,
                    allowImmediateApply: !deferImmediateExactVisualFinalize);
                bool troopExactVisualApplied = false;
                if (!heroExactVisualApplied)
                {
                    troopExactVisualApplied = CoopMissionSpawnLogic.TryFinalizeClientExactCampaignTroopVisualForPeerAgent(
                        mission,
                        agent,
                        "battle-map handoff SynchronizeAgentSpawnEquipment",
                        includeWeaponsForClientRefresh: true);
                }
                CoopMissionSpawnLogic.TraceClientMountedHeroNetworkContract(
                    agent,
                    "client-synchronize-agent-equipment",
                    "battle-map handoff SynchronizeAgentSpawnEquipment",
                    "PayloadEquipment={" + BuildEquipmentSummary(
                        synchronizeAgentSpawnEquipment.SpawnEquipment,
                        EquipmentIndex.Weapon0,
                        EquipmentIndex.Weapon1,
                        EquipmentIndex.Weapon2,
                        EquipmentIndex.Weapon3,
                        EquipmentIndex.Horse,
                        EquipmentIndex.HorseHarness) + "}" +
                    " DeferredImmediateExactVisualFinalize=" + deferImmediateExactVisualFinalize +
                    " ExactVisualApplied=" + heroExactVisualApplied +
                    " TroopExactVisualApplied=" + troopExactVisualApplied);
                if (heroExactVisualApplied || troopExactVisualApplied)
                    return;

                CoopMissionSpawnLogic.TryHandleClientExactCampaignSpawnEquipmentSync(
                    mission,
                    agent,
                    "battle-map handoff SynchronizeAgentSpawnEquipment");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: SynchronizeAgentSpawnEquipment exact override failed open: " + ex.Message);
            }
        }

        private static bool MissionNetworkComponent_HandleServerEventSetAgentHealth_Prefix(GameNetworkMessage baseMessage, ref bool __state)
        {
            __state = false;

            try
            {
                if (!(baseMessage is SetAgentHealth setAgentHealth))
                    return true;

                Mission mission = Mission.Current;
                if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                    return true;

                if (ShouldUseSafeStringIdCreateAgentPathOnClient(mission) &&
                    !CoopMissionNetworkBridge.IsClientCurrentBattleSnapshotApplied(out string snapshotReadinessSummary))
                {
                    RegisterDeferredClientSetAgentHealthPayload(
                        setAgentHealth,
                        "snapshot-not-ready:" + (snapshotReadinessSummary ?? "unknown"));
                    __state = true;
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: deferred client SetAgentHealth until current battle snapshot is applied. " +
                        "AgentIndex=" + setAgentHealth.AgentIndex +
                        " Health=" + setAgentHealth.Health +
                        " Reason=" + (snapshotReadinessSummary ?? "unknown"));
                    return false;
                }

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(setAgentHealth.AgentIndex, canBeNull: true);
                if (agent == null &&
                    ShouldUseSafeStringIdCreateAgentPathOnClient(mission) &&
                    (HasDeferredClientCreateAgentPayload(setAgentHealth.AgentIndex) ||
                     HasAnyDeferredClientAgentBootstrapPayload(setAgentHealth.AgentIndex)))
                {
                    RegisterDeferredClientSetAgentHealthPayload(
                        setAgentHealth,
                        "agent-bootstrap-deferred");
                    __state = true;
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: deferred client SetAgentHealth because agent bootstrap is still deferred. " +
                        "AgentIndex=" + setAgentHealth.AgentIndex +
                        " Health=" + setAgentHealth.Health);
                    return false;
                }

                if (agent != null)
                    return true;

                if (!CoopMissionSpawnLogic.TryResolveTrackedClientMountedHeroMissingMountAgentHealth(
                        setAgentHealth.AgentIndex,
                        out int riderAgentIndex,
                        out string entryId))
                {
                    return true;
                }

                __state = true;
                string payloadSummary =
                    "PayloadHealth=" + setAgentHealth.Health.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                    " MissingMountAgentIndex=" + setAgentHealth.AgentIndex +
                    " RiderAgentIndex=" + riderAgentIndex +
                    " EntryId=" + (entryId ?? "null") +
                    " SuppressedReason=missing-tracked-hero-mount-agent";
                ModLogger.Info(
                    "BattleMapSpawnHandoffPatch: suppressed SetAgentHealth for missing tracked mounted-hero agent. " +
                    payloadSummary);
                ExactBattleRuntimeBundleBridgeFile.AppendContractEvent(
                    "client-set-agent-health-suppressed-missing-mount",
                    payloadSummary +
                    " Source=battle-map handoff SetAgentHealth missing-mount guard");
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: SetAgentHealth prefix failed open: " + ex.Message);
                __state = false;
                return true;
            }
        }

        private static bool ShouldDeferImmediateClientExactVisualFinalize(Agent agent)
        {
            if (agent == null)
                return false;

            if (agent.SpawnEquipment == null)
                return true;

            if (agent.MountAgent != null)
                return true;

            if (CoopMissionSpawnLogic.HasTrackedClientMountedHeroMountAgentIndex(agent.Index))
                return true;

            return agent.SpawnEquipment[EquipmentIndex.Horse].Item != null ||
                   agent.SpawnEquipment[EquipmentIndex.HorseHarness].Item != null;
        }

        private static void MissionNetworkComponent_HandleServerEventSetAgentHealth_Postfix(GameNetworkMessage baseMessage, bool __state)
        {
            try
            {
                if (__state)
                    return;

                if (!(baseMessage is SetAgentHealth setAgentHealth))
                    return;

                Mission mission = Mission.Current;
                if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                    return;

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(setAgentHealth.AgentIndex, canBeNull: true);
                if (agent == null || agent.IsMount)
                    return;

                CoopMissionSpawnLogic.TryTrackClientMountedHeroMountAgentIndex(agent);
                if (setAgentHealth.Health > 0 && agent.MountAgent == null && agent.MissionPeer == null)
                    return;

                CoopMissionSpawnLogic.TraceClientMountedHeroNetworkContract(
                    agent,
                    "client-set-agent-health",
                    "battle-map handoff SetAgentHealth",
                    "PayloadHealth=" + setAgentHealth.Health);
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: SetAgentHealth contract trace failed: " + ex.Message);
            }
        }

        private static bool MissionNetworkComponent_HandleServerEventMakeAgentDead_Prefix(GameNetworkMessage baseMessage)
        {
            try
            {
                if (!(baseMessage is MakeAgentDead makeAgentDead))
                    return true;

                Mission mission = Mission.Current;
                if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                    return true;

                if (!ShouldUseSafeStringIdCreateAgentPathOnClient(mission))
                    return true;

                if (!CoopMissionNetworkBridge.IsClientCurrentBattleSnapshotApplied(out string snapshotReadinessSummary))
                {
                    RegisterDeferredClientMakeAgentDeadPayload(
                        makeAgentDead,
                        "snapshot-not-ready:" + (snapshotReadinessSummary ?? "unknown"));
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: deferred client MakeAgentDead until current battle snapshot is applied. " +
                        "AgentIndex=" + makeAgentDead.AgentIndex +
                        " IsKilled=" + makeAgentDead.IsKilled +
                        " Reason=" + (snapshotReadinessSummary ?? "unknown"));
                    return false;
                }

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(makeAgentDead.AgentIndex, canBeNull: true);
                if (agent == null &&
                    (HasDeferredClientCreateAgentPayload(makeAgentDead.AgentIndex) ||
                     HasAnyDeferredClientAgentBootstrapPayload(makeAgentDead.AgentIndex)))
                {
                    RegisterDeferredClientMakeAgentDeadPayload(
                        makeAgentDead,
                        "agent-bootstrap-deferred");
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: deferred client MakeAgentDead because agent bootstrap is still deferred. " +
                        "AgentIndex=" + makeAgentDead.AgentIndex +
                        " IsKilled=" + makeAgentDead.IsKilled);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MakeAgentDead prefix failed open: " + ex.Message);
                return true;
            }
        }

        private static bool MissionNetworkComponent_HandleServerEventSetWieldedItemIndex_Prefix(GameNetworkMessage baseMessage, ref bool __state)
        {
            __state = false;

            try
            {
                if (!(baseMessage is SetWieldedItemIndex setWieldedItemIndex))
                    return true;

                Mission mission = Mission.Current;
                if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                    return true;

                if (ShouldUseSafeStringIdCreateAgentPathOnClient(mission) &&
                    !CoopMissionNetworkBridge.IsClientCurrentBattleSnapshotApplied(out string snapshotReadinessSummary))
                {
                    RegisterDeferredClientSetWieldedItemIndexPayload(
                        setWieldedItemIndex,
                        "snapshot-not-ready:" + (snapshotReadinessSummary ?? "unknown"));
                    __state = true;
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: deferred client SetWieldedItemIndex until current battle snapshot is applied. " +
                        "AgentIndex=" + setWieldedItemIndex.AgentIndex +
                        " WieldedItemIndex=" + setWieldedItemIndex.WieldedItemIndex +
                        " Reason=" + (snapshotReadinessSummary ?? "unknown"));
                    return false;
                }

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(setWieldedItemIndex.AgentIndex, canBeNull: true);
                if (agent == null &&
                    ShouldUseSafeStringIdCreateAgentPathOnClient(mission) &&
                    HasDeferredClientCreateAgentPayload(setWieldedItemIndex.AgentIndex))
                {
                    RegisterDeferredClientSetWieldedItemIndexPayload(
                        setWieldedItemIndex,
                        "agent-createagent-deferred");
                    __state = true;
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: deferred client SetWieldedItemIndex because CreateAgent is still deferred. " +
                        "AgentIndex=" + setWieldedItemIndex.AgentIndex +
                        " WieldedItemIndex=" + setWieldedItemIndex.WieldedItemIndex);
                    return false;
                }

                if (agent == null)
                    return true;

                if (setWieldedItemIndex.IsWieldedOnSpawn &&
                    TrySuppressStrictExactHeroStaleOnSpawnWield(setWieldedItemIndex, agent))
                {
                    __state = true;
                    return false;
                }

                if (setWieldedItemIndex.WieldedItemIndex != EquipmentIndex.None || agent.IsMount)
                    return true;

                if (agent.IsActive() && agent.Health > 0f)
                    return true;

                string entryId = null;
                if (!CoopMissionSpawnLogic.TryResolveAuthoritativeTrackedEntryId(agent, out entryId) &&
                    !CoopMissionSpawnLogic.TryResolveSelectableEntryId(agent, out entryId))
                {
                    return true;
                }

                RosterEntryState entryState = BattleSnapshotRuntimeState.GetEntryState(entryId);
                if (entryState == null || !entryState.IsHero)
                    return true;

                __state = true;
                string payloadSummary =
                    "PayloadWieldedItemIndex=" + setWieldedItemIndex.WieldedItemIndex +
                    " PayloadIsLeftHand=" + setWieldedItemIndex.IsLeftHand +
                    " PayloadIsWieldedInstantly=" + setWieldedItemIndex.IsWieldedInstantly +
                    " PayloadIsWieldedOnSpawn=" + setWieldedItemIndex.IsWieldedOnSpawn +
                    " PayloadMainHandUsageIndex=" + setWieldedItemIndex.MainHandCurrentUsageIndex +
                    " SuppressedReason=dead-exact-hero-none-wield-reset";

                ModLogger.Info(
                    "BattleMapSpawnHandoffPatch: suppressed dead exact-hero SetWieldedItemIndex after removal. " +
                    "AgentIndex=" + agent.Index +
                    " EntryId=" + (entryId ?? "null") +
                    " AgentActive=" + agent.IsActive() +
                    " Health=" + agent.Health.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                    " " + payloadSummary);
                ExactCreateAgentCorridorDiagnostics.ObserveClientSetWieldedItemIndex(
                    setWieldedItemIndex,
                    agent,
                    suppressed: true,
                    source: "battle-map handoff SetWieldedItemIndex death guard");
                CoopMissionSpawnLogic.TraceClientMountedHeroNetworkContract(
                    agent,
                    "client-set-wielded-item-index-suppressed",
                    "battle-map handoff SetWieldedItemIndex death guard",
                    payloadSummary);
                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: SetWieldedItemIndex prefix failed open: " + ex.Message);
                __state = false;
                return true;
            }
        }

        private static bool TrySuppressStrictExactHeroStaleOnSpawnWield(
            SetWieldedItemIndex setWieldedItemIndex,
            Agent agent)
        {
            if (setWieldedItemIndex == null || agent == null || agent.IsMount)
                return false;

            if (!CoopMissionSpawnLogic.ShouldSuppressStrictExactHeroOnSpawnWield(
                    agent,
                    out string entryId,
                    out string suppressReason))
            {
                return false;
            }

            string appliedKey = agent.Index + "|" + (entryId ?? string.Empty);
            bool localInitialWieldAlreadyApplied =
                _strictExactHeroOnSpawnWieldRefreshAppliedKeys.Contains(appliedKey);
            bool localInitialWieldApplied = false;
            string localInitialWieldResult =
                localInitialWieldAlreadyApplied
                    ? "strict-exact-hero-local-initial-wield-already-applied"
                    : "(none)";
            string localInitialWieldIssue = "(none)";

            if (!localInitialWieldAlreadyApplied)
            {
                localInitialWieldApplied = CoopMissionSpawnLogic.TryApplyStrictExactHeroLocalInitialWield(
                    agent,
                    entryId,
                    "battle-map handoff strict exact hero stale on-spawn wield suppression",
                    out localInitialWieldResult,
                    out localInitialWieldIssue);
                if (localInitialWieldApplied)
                    _strictExactHeroOnSpawnWieldRefreshAppliedKeys.Add(appliedKey);
            }

            string payloadSummary =
                "PayloadWieldedItemIndex=" + setWieldedItemIndex.WieldedItemIndex +
                " PayloadIsLeftHand=" + setWieldedItemIndex.IsLeftHand +
                " PayloadIsWieldedInstantly=" + setWieldedItemIndex.IsWieldedInstantly +
                " PayloadIsWieldedOnSpawn=" + setWieldedItemIndex.IsWieldedOnSpawn +
                " PayloadMainHandUsageIndex=" + setWieldedItemIndex.MainHandCurrentUsageIndex +
                " SuppressedReason=" + (suppressReason ?? "strict-exact-hero-stale-onspawn-wield") +
                " LocalInitialWieldApplied=" + localInitialWieldApplied +
                " LocalInitialWieldAlreadyApplied=" + localInitialWieldAlreadyApplied +
                " LocalInitialWieldResult=" + (localInitialWieldResult ?? "(none)") +
                " LocalInitialWieldIssue=" + (localInitialWieldIssue ?? "(none)");

            ModLogger.Info(
                "BattleMapSpawnHandoffPatch: suppressed stale strict exact-hero on-spawn SetWieldedItemIndex after contract adapter materialization. " +
                "AgentIndex=" + agent.Index +
                " EntryId=" + (entryId ?? "null") +
                " TeamSide=" + agent.Team?.Side +
                " " + payloadSummary);
            ExactCreateAgentCorridorDiagnostics.ObserveClientSetWieldedItemIndex(
                setWieldedItemIndex,
                agent,
                suppressed: true,
                source: "battle-map handoff SetWieldedItemIndex strict exact hero guard");
            CoopMissionSpawnLogic.ObserveClientSetWieldedItemIndex(
                setWieldedItemIndex.AgentIndex,
                setWieldedItemIndex.IsWieldedOnSpawn,
                "battle-map handoff SetWieldedItemIndex strict exact hero guard");
            CoopMissionSpawnLogic.TraceClientMountedHeroNetworkContract(
                agent,
                "client-set-wielded-item-index-suppressed",
                "battle-map handoff SetWieldedItemIndex strict exact hero guard",
                payloadSummary);
            return true;
        }

        private static void MissionNetworkComponent_HandleServerEventSetWieldedItemIndex_Postfix(GameNetworkMessage baseMessage, bool __state)
        {
            try
            {
                if (__state)
                    return;

                if (!(baseMessage is SetWieldedItemIndex setWieldedItemIndex))
                    return;

                Mission mission = Mission.Current;
                if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                    return;

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(setWieldedItemIndex.AgentIndex, canBeNull: true);
                if (agent == null || agent.IsMount)
                    return;

                if (setWieldedItemIndex.WieldedItemIndex != EquipmentIndex.Weapon2 &&
                    !setWieldedItemIndex.IsWieldedOnSpawn &&
                    agent.MountAgent == null)
                {
                    return;
                }

                CoopMissionSpawnLogic.TraceClientMountedHeroNetworkContract(
                    agent,
                    "client-set-wielded-item-index",
                    "battle-map handoff SetWieldedItemIndex",
                    "PayloadWieldedItemIndex=" + setWieldedItemIndex.WieldedItemIndex +
                    " PayloadIsLeftHand=" + setWieldedItemIndex.IsLeftHand +
                    " PayloadIsWieldedInstantly=" + setWieldedItemIndex.IsWieldedInstantly +
                    " PayloadIsWieldedOnSpawn=" + setWieldedItemIndex.IsWieldedOnSpawn +
                    " PayloadMainHandUsageIndex=" + setWieldedItemIndex.MainHandCurrentUsageIndex);
                ExactCreateAgentCorridorDiagnostics.ObserveClientSetWieldedItemIndex(
                    setWieldedItemIndex,
                    agent,
                    suppressed: false,
                    source: "battle-map handoff SetWieldedItemIndex");
                CoopMissionSpawnLogic.ObserveClientSetWieldedItemIndex(
                    setWieldedItemIndex.AgentIndex,
                    setWieldedItemIndex.IsWieldedOnSpawn,
                    "battle-map handoff SetWieldedItemIndex");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: SetWieldedItemIndex contract trace failed: " + ex.Message);
            }
        }

        private static Exception MissionNetworkComponent_HandleServerEventSetWieldedItemIndex_Finalizer(
            Exception __exception,
            GameNetworkMessage baseMessage)
        {
            if (__exception == null)
                return null;

            try
            {
                if (!(baseMessage is SetWieldedItemIndex setWieldedItemIndex))
                    return __exception;

                Mission mission = Mission.Current;
                if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                    return __exception;

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(setWieldedItemIndex.AgentIndex, canBeNull: true);
                ExactCreateAgentCorridorDiagnostics.ObserveClientSetWieldedItemIndexException(
                    setWieldedItemIndex,
                    agent,
                    __exception,
                    "battle-map handoff SetWieldedItemIndex finalizer");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: SetWieldedItemIndex finalizer failed open: " + ex.Message);
            }

            return __exception;
        }

        private static bool MissionNetworkComponent_HandleServerEventSpawnWeaponAsDropFromAgent_Prefix(GameNetworkMessage baseMessage)
        {
            try
            {
                if (!(baseMessage is SpawnWeaponAsDropFromAgent spawnWeaponAsDropFromAgent))
                    return true;

                Mission mission = Mission.Current;
                if (mission == null ||
                    GameNetwork.IsServer ||
                    !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName) ||
                    !ShouldUseSafeStringIdCreateAgentPathOnClient(mission))
                {
                    return true;
                }

                if (spawnWeaponAsDropFromAgent.EquipmentIndex < EquipmentIndex.Weapon0 ||
                    spawnWeaponAsDropFromAgent.EquipmentIndex > EquipmentIndex.Weapon3)
                {
                    return true;
                }

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(spawnWeaponAsDropFromAgent.AgentIndex, canBeNull: true);
                bool suppress =
                    agent == null ||
                    ((agent.Equipment == null ||
                      agent.Equipment[spawnWeaponAsDropFromAgent.EquipmentIndex].IsEmpty ||
                      agent.Equipment[spawnWeaponAsDropFromAgent.EquipmentIndex].Item == null) &&
                     (agent.SpawnEquipment == null ||
                      agent.SpawnEquipment[spawnWeaponAsDropFromAgent.EquipmentIndex].IsEmpty ||
                      agent.SpawnEquipment[spawnWeaponAsDropFromAgent.EquipmentIndex].Item == null));
                if (!suppress)
                    return true;

                string logKey =
                    (mission.SceneName ?? "null") + "|" +
                    spawnWeaponAsDropFromAgent.AgentIndex + "|" +
                    spawnWeaponAsDropFromAgent.EquipmentIndex + "|" +
                    spawnWeaponAsDropFromAgent.ForcedIndex;
                if (!string.Equals(_lastSuppressedWeaponDropKey, logKey, StringComparison.Ordinal))
                {
                    _lastSuppressedWeaponDropKey = logKey;
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: suppressed client SpawnWeaponAsDropFromAgent because local weapon slot is unavailable. " +
                        "AgentIndex=" + spawnWeaponAsDropFromAgent.AgentIndex +
                        " EquipmentIndex=" + spawnWeaponAsDropFromAgent.EquipmentIndex +
                        " ForcedIndex=" + spawnWeaponAsDropFromAgent.ForcedIndex +
                        " AgentNull=" + (agent == null) +
                        " AgentCharacter=" + (agent?.Character?.StringId ?? "null") +
                        " MissionWeapons={" + BuildMissionEquipmentWeaponSummary(agent?.Equipment) + "}" +
                        " SpawnWeapons={" + BuildEquipmentSummary(
                            agent?.SpawnEquipment,
                            EquipmentIndex.Weapon0,
                            EquipmentIndex.Weapon1,
                            EquipmentIndex.Weapon2,
                            EquipmentIndex.Weapon3) + "}" +
                        " Scene=" + (mission.SceneName ?? "null"));
                }

                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: SpawnWeaponAsDropFromAgent prefix failed open: " + ex.Message);
                return true;
            }
        }

        private static string BuildMissionEquipmentWeaponSummary(MissionEquipment equipment)
        {
            if (equipment == null)
                return "(none)";

            var parts = new List<string>();
            AppendMissionWeaponSummary(parts, equipment, EquipmentIndex.Weapon0);
            AppendMissionWeaponSummary(parts, equipment, EquipmentIndex.Weapon1);
            AppendMissionWeaponSummary(parts, equipment, EquipmentIndex.Weapon2);
            AppendMissionWeaponSummary(parts, equipment, EquipmentIndex.Weapon3);
            return parts.Count > 0 ? string.Join(", ", parts) : "(empty)";
        }

        private static void AppendMissionWeaponSummary(List<string> parts, MissionEquipment equipment, EquipmentIndex slot)
        {
            MissionWeapon element = equipment[slot];
            if (element.IsEmpty || element.Item == null)
                return;

            parts.Add(slot + "=" + element.Item.StringId);
        }

        private static string BuildEquipmentSummary(Equipment equipment, params EquipmentIndex[] slots)
        {
            if (equipment == null)
                return "(none)";

            var parts = new List<string>();
            for (int i = 0; i < slots.Length; i++)
            {
                EquipmentIndex slot = slots[i];
                EquipmentElement element = equipment[slot];
                if (element.IsEmpty || element.Item == null)
                    continue;

                parts.Add(slot + "=" + element.Item.StringId);
            }

            return parts.Count > 0 ? string.Join(", ", parts) : "(empty)";
        }

        private static bool MissionNetworkComponent_HandleServerEventAssignFormationToPlayer_Prefix(GameNetworkMessage baseMessage)
        {
            try
            {
                if (!(baseMessage is AssignFormationToPlayer assignFormationToPlayer))
                    return true;

                if (!ShouldSuppressLocalAssignFormationToPlayer(assignFormationToPlayer, out string logKey, out string logDetails))
                    return true;

                if (!string.Equals(_lastSuppressedAssignFormationKey, logKey, StringComparison.Ordinal))
                {
                    _lastSuppressedAssignFormationKey = logKey;
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: suppressed local AssignFormationToPlayer during battle-map spawn handshake. " +
                        logDetails);
                }

                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: AssignFormationToPlayer prefix failed open: " + ex.Message);
                return true;
            }
        }

        private static void MissionNetworkComponent_HandleServerEventAssignFormationToPlayer_Postfix(GameNetworkMessage baseMessage)
        {
            // Intentionally left empty.
            // Exact-scene commander promotion is delayed until the later BotsControlledChange
            // handoff point so the client does not enter general-control state mid-spawn.
        }

        private static void MissionMultiplayerGameModeFlagDominationClient_OnBotsControlledChanged_Postfix(
            MissionPeer missionPeer,
            int botAliveCount,
            int botTotalCount)
        {
            try
            {
                if (!TryPromoteLocalExactCommanderToGeneral(missionPeer, botAliveCount, botTotalCount, out string logKey, out string logDetails))
                    return;

                if (!string.Equals(_lastPromotedLocalCommanderGeneralKey, logKey, StringComparison.Ordinal))
                {
                    _lastPromotedLocalCommanderGeneralKey = logKey;
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: promoted local exact-scene commander to general control after BotsControlledChange. " +
                        logDetails);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: delayed local general-control promotion after BotsControlledChange failed: " + ex.Message);
            }
        }

        private static void MissionGauntletMultiplayerOrderUIHandler_OnMissionScreenTick_Postfix(object __instance, float dt)
        {
            try
            {
                TryPromoteLocalExactCommanderFromOrderUiTick();
                if (TrySuppressNonCommanderOrderUi(__instance))
                    return;

                _lastSuppressedNonCommanderOrderUiKey = null;
                TryFinalizePendingLocalCommanderOrderControl(__instance);
                TryMaintainLocalCommanderOrderControl(__instance);
                TryHandleExactCommanderOrderUiHotkeys(__instance);
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: local commander order-control finalization tick failed: " + ex.Message);
            }
        }

        private static void TryPromoteLocalExactCommanderFromOrderUiTick()
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return;

            NetworkCommunicator myPeer = GameNetwork.MyPeer;
            if (myPeer == null || myPeer.IsServerPeer)
                return;

            MissionPeer myMissionPeer = myPeer.GetComponent<MissionPeer>();
            if (myMissionPeer == null)
                return;

            if (!TryPromoteLocalExactCommanderToGeneral(
                    myMissionPeer,
                    myMissionPeer.BotsUnderControlAlive,
                    myMissionPeer.BotsUnderControlTotal,
                    out string logKey,
                    out string logDetails))
            {
                return;
            }

            if (string.Equals(_lastPromotedLocalCommanderGeneralKey, logKey, StringComparison.Ordinal))
                return;

            _lastPromotedLocalCommanderGeneralKey = logKey;
            ModLogger.Info(
                "BattleMapSpawnHandoffPatch: promoted local exact-scene commander to general control during order UI tick fallback. " +
                logDetails);
        }

        private static void MissionOrderVM_ViewOrders_Postfix(object __instance)
        {
            try
            {
                TryLogExactCommanderOrderMenuInteraction("ViewOrders", __instance, applySelectedOrders: false, closeResult: null);
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionOrderVM.ViewOrders diagnostics failed: " + ex.Message);
            }
        }

        private static void MissionOrderVM_OpenToggleOrder_Postfix(object __instance, bool fromHold, bool displayMessage)
        {
            try
            {
                TryLogExactCommanderOrderMenuInteraction(
                    "OpenToggleOrder",
                    __instance,
                    applySelectedOrders: fromHold,
                    closeResult: displayMessage);
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionOrderVM.OpenToggleOrder diagnostics failed: " + ex.Message);
            }
        }

        private static void MissionOrderVM_TryCloseToggleOrder_Postfix(object __instance, bool applySelectedOrders, bool __result)
        {
            try
            {
                TryLogExactCommanderOrderMenuInteraction("TryCloseToggleOrder", __instance, applySelectedOrders, __result);
                TrySuppressExactCommanderOrderHotkeysUntilRelease(__instance, __result, "TryCloseToggleOrder");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionOrderVM.TryCloseToggleOrder diagnostics failed: " + ex.Message);
            }
        }

        private static void MissionOrderVM_OnOrderExecuted_Postfix(object __instance, object orderItem)
        {
            try
            {
                TryForceImmediateExactCommanderOrderMenuClose(__instance, orderItem, "OnOrderExecuted");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionOrderVM.OnOrderExecuted exact commander close failed: " + ex.Message);
            }
        }

        private static void OrderItemVM_OnExecuteAction_Postfix(object __instance)
        {
            try
            {
                TryLogExactCommanderOrderItemExecution(_activeExactCommanderMissionOrderVm, __instance);
                TryForceImmediateExactCommanderOrderMenuClose(_activeExactCommanderMissionOrderVm, __instance, "OnExecuteAction");
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: OrderItemVM.OnExecuteAction exact commander close failed: " + ex.Message);
            }
        }

        private static bool OrderTroopPlacer_OnMissionScreenTick_Prefix(object __instance, float dt)
        {
            try
            {
                if (!ShouldSuppressOrderTroopPlacerTick(__instance, out string logKey, out string logDetails))
                    return true;

                if (!string.Equals(_lastSuppressedOrderTroopPlacerTickKey, logKey, StringComparison.Ordinal))
                {
                    _lastSuppressedOrderTroopPlacerTickKey = logKey;
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: suppressed OrderTroopPlacer.OnMissionScreenTick via local order-control guard. " +
                        logDetails);
                }

                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: OrderTroopPlacer.OnMissionScreenTick prefix failed open: " + ex.Message);
                return true;
            }
        }

        private static bool MissionNetworkComponent_HandleClientEventSelectAllFormations_Prefix(
            NetworkCommunicator networkPeer,
            GameNetworkMessage baseMessage,
            ref bool __result)
        {
            try
            {
                if (!ShouldSuppressServerSelectAllFormations(networkPeer, out string logKey, out string logDetails))
                    return true;

                __result = true;
                if (!string.Equals(_lastSuppressedServerSelectAllFormationsKey, logKey, StringComparison.Ordinal))
                {
                    _lastSuppressedServerSelectAllFormationsKey = logKey;
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: suppressed server-side SelectAllFormations via order-control guard. " +
                        logDetails);
                }

                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: HandleClientEventSelectAllFormations prefix failed open: " + ex.Message);
                return true;
            }
        }

        private static bool OrderController_SelectAllFormations_Prefix(
            OrderController __instance,
            Agent selectorAgent,
            bool uiFeedback)
        {
            try
            {
                if (!ShouldSuppressLocalSelectAllFormations(__instance, selectorAgent, out string logKey, out string logDetails))
                {
                    LogObservedLocalSelectAllFormations(__instance, selectorAgent);
                    return true;
                }

                if (!string.Equals(_lastSuppressedLocalSelectAllFormationsKey, logKey, StringComparison.Ordinal))
                {
                    _lastSuppressedLocalSelectAllFormationsKey = logKey;
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: suppressed local OrderController.SelectAllFormations via order-control guard. " +
                        logDetails);
                }

                return false;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: OrderController.SelectAllFormations prefix failed open: " + ex.Message);
                return true;
            }
        }

        private static bool TrySuppressNonCommanderOrderUi(object orderUiHandler)
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive || orderUiHandler == null)
                return false;

            NetworkCommunicator myPeer = GameNetwork.MyPeer;
            if (myPeer == null || myPeer.IsServerPeer)
                return false;

            MissionPeer myMissionPeer = myPeer.GetComponent<MissionPeer>();
            Agent controlledAgent = myMissionPeer?.ControlledAgent;
            Agent mainAgent = Agent.Main;
            Mission mission = Mission.Current ?? controlledAgent?.Mission ?? mainAgent?.Mission;
            Team team = myMissionPeer?.Team ?? controlledAgent?.Team ?? mainAgent?.Team;
            if (myMissionPeer == null ||
                controlledAgent == null ||
                !controlledAgent.IsActive() ||
                mission == null ||
                team == null ||
                !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName) ||
                !SceneRuntimeClassifier.IsCampaignBattleScene(mission.SceneName ?? string.Empty))
            {
                return false;
            }

            CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot selectionBridge =
                CoopBattleSelectionBridgeFile.ReadCurrentSelection();
            CoopBattleEntryPolicy.ClientSnapshot entryPolicy =
                CoopBattleEntryPolicy.BuildClientSnapshot(mission, selectionBridge);
            if (entryPolicy == null || !entryPolicy.UseAuthoritativeTroopPath)
                return false;

            string controlledEntryId = ResolveControlledEntryId(
                myMissionPeer,
                controlledAgent,
                mission,
                entryPolicy.BridgeTroopOrEntryId,
                out _);
            bool isExactCommander = IsEntryIdExactCommanderForTeam(team, controlledEntryId, out string commanderEntryId);
            if (!isExactCommander &&
                string.IsNullOrWhiteSpace(controlledEntryId) &&
                TryBypassNonCommanderSuppressionFromEstablishedLocalCommanderState(
                    context: "OrderUi",
                    myPeer,
                    myMissionPeer,
                    team,
                    controlledAgent,
                    mainAgent,
                    mission))
            {
                return false;
            }

            if (!isExactCommander &&
                ShouldDeferNonCommanderSuppressionUntilControlledEntryIdentityResolves(
                    "OrderUi",
                    myPeer,
                    myMissionPeer,
                    controlledAgent,
                    team,
                    controlledEntryId,
                    commanderEntryId,
                    mission))
            {
                return false;
            }

            bool hasCommanderIdentity = !string.IsNullOrWhiteSpace(commanderEntryId);
            bool hasCommanderControlCounts = myMissionPeer.BotsUnderControlTotal > 1 || myMissionPeer.BotsUnderControlAlive > 0;
            if (isExactCommander || (!hasCommanderIdentity && hasCommanderControlCounts))
                return false;

            string suppressionStateKey =
                myPeer.Index + "|" +
                team.TeamIndex + "|" +
                controlledAgent.Index + "|" +
                (controlledEntryId ?? "none") + "|" +
                (commanderEntryId ?? "none") + "|" +
                myMissionPeer.BotsUnderControlTotal + "|" +
                myMissionPeer.BotsUnderControlAlive;
            // The order-control prefixes already keep the UI suppressed; avoid
            // re-running the destructive clear/owner-reset path every tick.
            if (string.Equals(_lastSuppressedNonCommanderOrderUiKey, suppressionStateKey, StringComparison.Ordinal))
                return true;

            object dataSource = TryGetInstanceMemberValue(orderUiHandler, "_dataSource");
            bool troopPlacerSuspended = TryInvokeSingleBoolMethod(orderUiHandler, "SetSuspendTroopPlacer", value: true);
            bool? closeToggleResult = TryInvokeBoolMethod(dataSource, "TryCloseToggleOrder", false);
            if (dataSource != null)
            {
                TrySetInstanceMemberValue(dataSource, "CanUseShortcuts", false);
                TrySetInstanceMemberValue(dataSource, "IsToggleOrderShown", false);
            }

            bool selectedFormationsCleared = false;
            bool playerOrderOwnerCleared = false;
            bool agentOrderOwnerCleared = false;
            int formationPlayerStateClears = 0;
            bool playerRoleDemoted = false;
            bool remoteLeadDisabled = false;

            OrderController playerOrderController = team.PlayerOrderController;
            if (playerOrderController != null)
            {
                try
                {
                    playerOrderController.ClearSelectedFormations();
                    selectedFormationsCleared = true;
                }
                catch
                {
                    selectedFormationsCleared = false;
                }

                try
                {
                    playerOrderController.Owner = null;
                    playerOrderOwnerCleared = playerOrderController.Owner == null;
                }
                catch
                {
                    playerOrderOwnerCleared = false;
                }
            }

            OrderController agentOrderController = team.GetOrderControllerOf(controlledAgent);
            if (agentOrderController != null)
            {
                try
                {
                    agentOrderController.Owner = null;
                    agentOrderOwnerCleared = agentOrderController.Owner == null;
                }
                catch
                {
                    agentOrderOwnerCleared = false;
                }
            }

            try
            {
                if (team.IsPlayerGeneral || team.IsPlayerSergeant)
                {
                    team.SetPlayerRole(isPlayerGeneral: false, isPlayerSergeant: false);
                    playerRoleDemoted = !team.IsPlayerGeneral && !team.IsPlayerSergeant;
                }
            }
            catch
            {
                playerRoleDemoted = false;
            }

            try
            {
                controlledAgent.SetCanLeadFormationsRemotely(value: false);
                remoteLeadDisabled = true;
            }
            catch
            {
                remoteLeadDisabled = false;
            }

            foreach (Formation formation in team.FormationsIncludingEmpty)
            {
                if (formation == null || !ReferenceEquals(formation.Team, team))
                    continue;

                Agent formationPlayerOwner = TryGetInstanceMemberValue(formation, "PlayerOwner") as Agent;
                bool ownedByLocalPlayer =
                    formationPlayerOwner != null &&
                    ((mainAgent != null && formationPlayerOwner.Index == mainAgent.Index) ||
                     formationPlayerOwner.Index == controlledAgent.Index);
                bool hasPlayerControlledTroop = TryGetInstanceBool(formation, "HasPlayerControlledTroop");
                bool isPlayerTroopInFormation = TryGetInstanceBool(formation, "IsPlayerTroopInFormation");
                if (!ownedByLocalPlayer && !hasPlayerControlledTroop && !isPlayerTroopInFormation)
                    continue;

                TrySetInstanceMemberValue(formation, "PlayerOwner", null);
                TrySetInstanceMemberValue(formation, "HasPlayerControlledTroop", false);
                TrySetInstanceMemberValue(formation, "IsPlayerTroopInFormation", false);
                formationPlayerStateClears++;
            }

            _activeExactCommanderMissionOrderVm = null;
            _pendingLocalCommanderOrderControlFinalization = null;
            _suppressExactCommanderOrderHotkeyFallbackUntilRelease = false;

            _lastSuppressedNonCommanderOrderUiKey = suppressionStateKey;
            ModLogger.Info(
                "BattleMapSpawnHandoffPatch: suppressed local commander order UI for non-commander controlled agent. " +
                "Peer=" + (myPeer.UserName ?? myPeer.Index.ToString()) +
                " TeamIndex=" + team.TeamIndex +
                " Side=" + team.Side +
                " ControlledAgentIndex=" + controlledAgent.Index +
                " AgentMainIndex=" + (mainAgent?.Index.ToString() ?? "null") +
                " ControlledFormationIndex=" + (myMissionPeer.ControlledFormation?.FormationIndex.ToString() ?? "null") +
                " ControlledEntryId=" + (controlledEntryId ?? "null") +
                " CommanderEntryId=" + (commanderEntryId ?? "null") +
                " TroopPlacerSuspended=" + troopPlacerSuspended +
                " CloseToggleResult=" + (closeToggleResult.HasValue ? closeToggleResult.Value.ToString() : "null") +
                " SelectedFormationsCleared=" + selectedFormationsCleared +
                " PlayerOrderOwnerCleared=" + playerOrderOwnerCleared +
                " AgentOrderOwnerCleared=" + agentOrderOwnerCleared +
                " FormationPlayerStateClears=" + formationPlayerStateClears +
                " PlayerRoleDemoted=" + playerRoleDemoted +
                " RemoteLeadDisabled=" + remoteLeadDisabled +
                " BotsUnderControlTotal=" + myMissionPeer.BotsUnderControlTotal +
                " BotsUnderControlAlive=" + myMissionPeer.BotsUnderControlAlive +
                " Mission=" + (mission.SceneName ?? "null"));
            return true;
        }

        private static bool TryResolveCommanderEntryIdForTeam(Team team, out string commanderEntryId)
        {
            commanderEntryId = null;
            if (team == null || team.Side == BattleSideEnum.None)
                return false;

            BattleRuntimeState runtimeState = BattleSnapshotRuntimeState.GetState();
            RosterEntryState commanderEntry = BattleCommanderResolver.ResolveCommanderEntry(runtimeState, team.Side);
            if (commanderEntry == null || string.IsNullOrWhiteSpace(commanderEntry.EntryId))
                return false;

            commanderEntryId = commanderEntry.EntryId;
            return true;
        }

        private static string ResolveAgentEntryId(Agent agent, string fallbackEntryId)
        {
            if (agent != null &&
                CoopMissionSpawnLogic.TryResolveAuthoritativeTrackedEntryId(agent, out string controlledEntryId) &&
                !string.IsNullOrWhiteSpace(controlledEntryId))
            {
                return controlledEntryId;
            }

            return string.IsNullOrWhiteSpace(fallbackEntryId)
                ? null
                : fallbackEntryId;
        }

        private static string ResolveControlledEntryId(
            MissionPeer missionPeer,
            Agent controlledAgent,
            Mission mission,
            string bridgeTroopOrEntryId,
            out string resolutionSource)
        {
            resolutionSource = null;
            NetworkCommunicator networkPeer = missionPeer?.GetNetworkPeer();
            string localSelectedEntryId = null;
            string localSelectedEntrySource = null;
            if (networkPeer?.IsMine == true)
            {
                CoopMissionSpawnLogic.TryResolveLocalSelectedEntryIdForBattleMapCommander(
                    out localSelectedEntryId,
                    out localSelectedEntrySource);
            }

            string controlledEntryId = ResolveAgentEntryId(controlledAgent, fallbackEntryId: null);
            if (!string.IsNullOrWhiteSpace(controlledEntryId))
            {
                resolutionSource = "agent-entry";
                TraceLocalControlledEntryIdentityVerification(
                    networkPeer,
                    controlledAgent,
                    mission,
                    controlledEntryId,
                    resolutionSource,
                    localSelectedEntryId,
                    localSelectedEntrySource,
                    bridgeTroopOrEntryId,
                    spawnState: default,
                    status: null);
                return controlledEntryId;
            }

            if (missionPeer != null &&
                CoopBattleSpawnRuntimeState.TryGetState(missionPeer, out PeerSpawnRuntimeState spawnState) &&
                !string.IsNullOrWhiteSpace(spawnState.EntryId))
            {
                resolutionSource = "spawn-runtime";
                controlledEntryId = spawnState.EntryId.Trim();
                LogResolvedControlledEntryIdFallback(
                    networkPeer,
                    controlledAgent,
                    mission,
                    controlledEntryId,
                    resolutionSource,
                    bridgeTroopOrEntryId,
                    spawnState,
                    status: null);
                TraceLocalControlledEntryIdentityVerification(
                    networkPeer,
                    controlledAgent,
                    mission,
                    controlledEntryId,
                    resolutionSource,
                    localSelectedEntryId,
                    localSelectedEntrySource,
                    bridgeTroopOrEntryId,
                    spawnState,
                    status: null);
                return controlledEntryId;
            }

            if (networkPeer?.IsMine == true &&
                CoopMissionSpawnLogic.TryResolveLocalSelectedEntryIdForBattleMapCommander(out controlledEntryId) &&
                !string.IsNullOrWhiteSpace(controlledEntryId))
            {
                resolutionSource = "local-selected-entry";
                LogResolvedControlledEntryIdFallback(
                    networkPeer,
                    controlledAgent,
                    mission,
                    controlledEntryId,
                    resolutionSource,
                    bridgeTroopOrEntryId,
                    spawnState: default,
                    status: null);
                TraceLocalControlledEntryIdentityVerification(
                    networkPeer,
                    controlledAgent,
                    mission,
                    controlledEntryId,
                    resolutionSource,
                    localSelectedEntryId,
                    localSelectedEntrySource,
                    bridgeTroopOrEntryId,
                    spawnState: default,
                    status: null);
                return controlledEntryId;
            }

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status = ReadRelevantBattleMapStatus(networkPeer, mission);
            if (TryResolveStatusEntryId(status, out controlledEntryId, out string statusSource))
            {
                resolutionSource = statusSource;
                LogResolvedControlledEntryIdFallback(
                    networkPeer,
                    controlledAgent,
                    mission,
                    controlledEntryId,
                    resolutionSource,
                    bridgeTroopOrEntryId,
                    spawnState: default,
                    status);
                TraceLocalControlledEntryIdentityVerification(
                    networkPeer,
                    controlledAgent,
                    mission,
                    controlledEntryId,
                    resolutionSource,
                    localSelectedEntryId,
                    localSelectedEntrySource,
                    bridgeTroopOrEntryId,
                    spawnState: default,
                    status);
                return controlledEntryId;
            }

            if (LooksLikeEntryId(bridgeTroopOrEntryId))
            {
                resolutionSource = "bridge-selection";
                controlledEntryId = bridgeTroopOrEntryId.Trim();
                LogResolvedControlledEntryIdFallback(
                    networkPeer,
                    controlledAgent,
                    mission,
                    controlledEntryId,
                    resolutionSource,
                    bridgeTroopOrEntryId,
                    spawnState: default,
                    status);
                TraceLocalControlledEntryIdentityVerification(
                    networkPeer,
                    controlledAgent,
                    mission,
                    controlledEntryId,
                    resolutionSource,
                    localSelectedEntryId,
                    localSelectedEntrySource,
                    bridgeTroopOrEntryId,
                    spawnState: default,
                    status);
                return controlledEntryId;
            }

            if (controlledAgent != null &&
                CoopMissionSpawnLogic.TryResolveSelectableEntryId(controlledAgent, out controlledEntryId) &&
                !string.IsNullOrWhiteSpace(controlledEntryId))
            {
                resolutionSource = "agent-entry-client-overlay";
                LogResolvedControlledEntryIdFallback(
                    networkPeer,
                    controlledAgent,
                    mission,
                    controlledEntryId,
                    resolutionSource,
                    bridgeTroopOrEntryId,
                    spawnState: default,
                    status);
                TraceLocalControlledEntryIdentityVerification(
                    networkPeer,
                    controlledAgent,
                    mission,
                    controlledEntryId,
                    resolutionSource,
                    localSelectedEntryId,
                    localSelectedEntrySource,
                    bridgeTroopOrEntryId,
                    spawnState: default,
                    status);
                return controlledEntryId;
            }

            TraceLocalControlledEntryIdentityVerification(
                networkPeer: networkPeer,
                controlledAgent: controlledAgent,
                mission: mission,
                resolvedEntryId: null,
                resolutionSource: "unresolved",
                localSelectedEntryId: localSelectedEntryId,
                localSelectedEntrySource: localSelectedEntrySource,
                bridgeTroopOrEntryId: bridgeTroopOrEntryId,
                spawnState: default,
                status: status);
            return null;
        }

        private static CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot ReadRelevantBattleMapStatus(
            NetworkCommunicator networkPeer,
            Mission mission)
        {
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status =
                CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (status == null)
                return null;

            int peerIndex = networkPeer?.Index ?? -1;
            string missionName = mission?.SceneName;
            return IsRelevantBattleMapStatus(status, peerIndex, missionName)
                ? status
                : null;
        }

        private static bool TryResolveStatusEntryId(
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status,
            out string entryId,
            out string source)
        {
            entryId = null;
            source = null;
            if (status == null)
                return false;

            if (!string.IsNullOrWhiteSpace(status.SelectedEntryId))
            {
                entryId = status.SelectedEntryId.Trim();
                source = "status-selected-entry";
                return true;
            }

            if (!string.IsNullOrWhiteSpace(status.SpawnRequestEntryId))
            {
                entryId = status.SpawnRequestEntryId.Trim();
                source = "status-spawn-request";
                return true;
            }

            if (!string.IsNullOrWhiteSpace(status.SelectionRequestEntryId))
            {
                entryId = status.SelectionRequestEntryId.Trim();
                source = "status-selection-request";
                return true;
            }

            if (LooksLikeEntryId(status.IntentTroopOrEntryId))
            {
                entryId = status.IntentTroopOrEntryId.Trim();
                source = "status-intent-entry";
                return true;
            }

            return false;
        }

        private static bool LooksLikeEntryId(string value)
        {
            return !string.IsNullOrWhiteSpace(value) &&
                   value.IndexOf('|') >= 0;
        }

        private static void LogResolvedControlledEntryIdFallback(
            NetworkCommunicator networkPeer,
            Agent controlledAgent,
            Mission mission,
            string resolvedEntryId,
            string resolutionSource,
            string bridgeTroopOrEntryId,
            PeerSpawnRuntimeState spawnState,
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status)
        {
            if (string.IsNullOrWhiteSpace(resolvedEntryId) || string.IsNullOrWhiteSpace(resolutionSource))
                return;

            string logKey =
                (networkPeer?.Index.ToString() ?? "null") + "|" +
                (controlledAgent?.Index.ToString() ?? "null") + "|" +
                resolutionSource + "|" +
                resolvedEntryId + "|" +
                (spawnState.EntryId ?? "null") + "|" +
                (status?.SelectedEntryId ?? "null") + "|" +
                (status?.SpawnRequestEntryId ?? "null") + "|" +
                (status?.SelectionRequestEntryId ?? "null") + "|" +
                (bridgeTroopOrEntryId ?? "null");
            if (string.Equals(_lastResolvedControlledEntryFallbackKey, logKey, StringComparison.Ordinal))
                return;

            _lastResolvedControlledEntryFallbackKey = logKey;
            ModLogger.Info(
                "BattleMapSpawnHandoffPatch: resolved local controlled entry identity via authoritative fallback. " +
                "Peer=" + (networkPeer?.UserName ?? networkPeer?.Index.ToString() ?? "null") +
                " ControlledAgentIndex=" + (controlledAgent?.Index.ToString() ?? "null") +
                " Source=" + resolutionSource +
                " EntryId=" + resolvedEntryId +
                " SpawnRuntimeEntryId=" + (spawnState.EntryId ?? "null") +
                " StatusSelectedEntryId=" + (status?.SelectedEntryId ?? "null") +
                " StatusSpawnRequestEntryId=" + (status?.SpawnRequestEntryId ?? "null") +
                " StatusSelectionRequestEntryId=" + (status?.SelectionRequestEntryId ?? "null") +
                " BridgeTroopOrEntryId=" + (bridgeTroopOrEntryId ?? "null") +
                " Mission=" + (mission?.SceneName ?? "null"));
        }

        private static void TraceLocalControlledEntryIdentityVerification(
            NetworkCommunicator networkPeer,
            Agent controlledAgent,
            Mission mission,
            string resolvedEntryId,
            string resolutionSource,
            string localSelectedEntryId,
            string localSelectedEntrySource,
            string bridgeTroopOrEntryId,
            PeerSpawnRuntimeState spawnState,
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status)
        {
            if (networkPeer?.IsMine != true || controlledAgent == null || mission == null)
                return;

            string exactTransferCacheEntryId = null;
            ExactTransferContractRuntimeCache.TryGetEntryIdByRiderAgentIndex(controlledAgent.Index, out exactTransferCacheEntryId);
            string verdict = DescribeControlledEntryIdentityVerdict(localSelectedEntryId, resolvedEntryId);
            string selectedSummary = CoopMissionSpawnLogic.BuildExactEntryCompatibilityDebugSummary(localSelectedEntryId);
            string resolvedSummary = string.Equals(localSelectedEntryId, resolvedEntryId, StringComparison.Ordinal)
                ? selectedSummary
                : CoopMissionSpawnLogic.BuildExactEntryCompatibilityDebugSummary(resolvedEntryId);
            string cacheVerdict = DescribeControlledEntryIdentityVerdict(resolvedEntryId, exactTransferCacheEntryId);
            string authoritativeCompatibilityEntryId = status?.AuthoritativeCompatibilityEntryId ?? string.Empty;
            string authoritativeCompatibilityEntrySource = status?.AuthoritativeCompatibilityEntrySource ?? string.Empty;
            string authoritativeCompatibilityStatus = status?.AuthoritativeCompatibilityStatus ?? string.Empty;
            bool authoritativeWeaponContractSupported = status?.AuthoritativeWeaponContractSupported ?? false;
            bool authoritativeVisualContractSupported = status?.AuthoritativeVisualContractSupported ?? false;
            string authoritativeCompatibilitySummary = status?.AuthoritativeCompatibilitySummary ?? string.Empty;
            string logKey =
                networkPeer.Index + "|" +
                controlledAgent.Index + "|" +
                (controlledAgent.Character?.StringId ?? "null") + "|" +
                (localSelectedEntryId ?? "null") + "|" +
                (localSelectedEntrySource ?? "null") + "|" +
                (resolvedEntryId ?? "null") + "|" +
                (resolutionSource ?? "null") + "|" +
                (exactTransferCacheEntryId ?? "null") + "|" +
                verdict + "|" +
                cacheVerdict + "|" +
                authoritativeCompatibilityEntryId + "|" +
                authoritativeCompatibilityEntrySource + "|" +
                authoritativeCompatibilityStatus + "|" +
                authoritativeWeaponContractSupported + "|" +
                authoritativeVisualContractSupported + "|" +
                (bridgeTroopOrEntryId ?? "null");
            if (string.Equals(_lastControlledEntryIdentityVerificationKey, logKey, StringComparison.Ordinal))
                return;

            _lastControlledEntryIdentityVerificationKey = logKey;
            ModLogger.Info(
                "BattleMapSpawnHandoffPatch: verified local controlled agent entry identity. " +
                "Peer=" + (networkPeer.UserName ?? networkPeer.Index.ToString()) +
                " ControlledAgentIndex=" + controlledAgent.Index +
                " ControlledAgentCharacter=" + (controlledAgent.Character?.StringId ?? "null") +
                " ControlledAgentTeamSide=" + controlledAgent.Team?.Side +
                " SelectedEntryId=" + (localSelectedEntryId ?? "null") +
                " SelectedEntrySource=" + (localSelectedEntrySource ?? "null") +
                " ResolvedEntryId=" + (resolvedEntryId ?? "null") +
                " ResolvedEntrySource=" + (resolutionSource ?? "null") +
                " IdentityVerdict=" + verdict +
                " ExactTransferCacheEntryId=" + (exactTransferCacheEntryId ?? "null") +
                " CacheVerdict=" + cacheVerdict +
                " SpawnRuntimeEntryId=" + (spawnState.EntryId ?? "null") +
                " StatusSelectedEntryId=" + (status?.SelectedEntryId ?? "null") +
                " StatusSpawnRequestEntryId=" + (status?.SpawnRequestEntryId ?? "null") +
                " StatusSelectionRequestEntryId=" + (status?.SelectionRequestEntryId ?? "null") +
                " HostCompatibilityEntryId=" + (string.IsNullOrWhiteSpace(authoritativeCompatibilityEntryId) ? "null" : authoritativeCompatibilityEntryId) +
                " HostCompatibilityEntrySource=" + (string.IsNullOrWhiteSpace(authoritativeCompatibilityEntrySource) ? "null" : authoritativeCompatibilityEntrySource) +
                " HostCompatibilityStatus=" + (string.IsNullOrWhiteSpace(authoritativeCompatibilityStatus) ? "null" : authoritativeCompatibilityStatus) +
                " HostWeaponContractSupported=" + authoritativeWeaponContractSupported +
                " HostVisualContractSupported=" + authoritativeVisualContractSupported +
                " BridgeTroopOrEntryId=" + (bridgeTroopOrEntryId ?? "null") +
                " SelectedSummary={" + selectedSummary + "}" +
                " ResolvedSummary={" + resolvedSummary + "}" +
                " HostCompatibilitySummary={" + (string.IsNullOrWhiteSpace(authoritativeCompatibilitySummary) ? "none" : authoritativeCompatibilitySummary) + "}" +
                " Mission=" + (mission.SceneName ?? "null"));
        }

        private static string DescribeControlledEntryIdentityVerdict(string expectedEntryId, string actualEntryId)
        {
            bool hasExpected = !string.IsNullOrWhiteSpace(expectedEntryId);
            bool hasActual = !string.IsNullOrWhiteSpace(actualEntryId);
            if (!hasExpected && !hasActual)
                return "unresolved";
            if (hasExpected && !hasActual)
                return "selected-only";
            if (!hasExpected && hasActual)
                return "resolved-only";

            return string.Equals(expectedEntryId, actualEntryId, StringComparison.Ordinal)
                ? "match"
                : "mismatch";
        }

        private static bool TryBypassNonCommanderSuppressionFromEstablishedLocalCommanderState(
            string context,
            NetworkCommunicator networkPeer,
            MissionPeer missionPeer,
            Team team,
            Agent controlledAgent,
            Agent mainAgent,
            Mission mission)
        {
            if (missionPeer == null || team == null || controlledAgent == null || !controlledAgent.IsActive())
                return false;

            if (!team.IsPlayerGeneral)
                return false;

            Agent generalAgent = team.GeneralAgent;
            if (generalAgent == null)
                return false;

            bool generalMatchesLocalCommander =
                generalAgent.Index == controlledAgent.Index ||
                (mainAgent != null && generalAgent.Index == mainAgent.Index);
            if (!generalMatchesLocalCommander)
                return false;

            OrderController playerOrderController = team.PlayerOrderController;
            OrderController agentOrderController = team.GetOrderControllerOf(controlledAgent);
            int playerOrderOwnerIndex = playerOrderController?.Owner?.Index ?? -1;
            int agentOrderOwnerIndex = agentOrderController?.Owner?.Index ?? -1;
            bool orderOwnerMatchesLocalCommander =
                playerOrderOwnerIndex == controlledAgent.Index ||
                agentOrderOwnerIndex == controlledAgent.Index ||
                (mainAgent != null &&
                 (playerOrderOwnerIndex == mainAgent.Index || agentOrderOwnerIndex == mainAgent.Index));
            bool hasOwnedFormationWithUnits = HasLocalCommanderFormationAuthority(team, controlledAgent, mainAgent);
            bool hasCommanderUnits = missionPeer.BotsUnderControlTotal > 1 || missionPeer.BotsUnderControlAlive > 0;
            if (!orderOwnerMatchesLocalCommander && !hasOwnedFormationWithUnits && !hasCommanderUnits)
                return false;

            string logKey =
                (context ?? "unknown") + "|" +
                (networkPeer?.Index.ToString() ?? "null") + "|" +
                team.TeamIndex + "|" +
                controlledAgent.Index + "|" +
                (mainAgent?.Index.ToString() ?? "null") + "|" +
                generalAgent.Index + "|" +
                playerOrderOwnerIndex + "|" +
                agentOrderOwnerIndex + "|" +
                hasOwnedFormationWithUnits + "|" +
                missionPeer.BotsUnderControlTotal + "|" +
                missionPeer.BotsUnderControlAlive;
            if (string.Equals(_lastEstablishedCommanderStateBypassKey, logKey, StringComparison.Ordinal))
                return true;

            _lastEstablishedCommanderStateBypassKey = logKey;
            ModLogger.Info(
                "BattleMapSpawnHandoffPatch: preserved local commander control via established general state despite missing controlled entry identity. " +
                "Context=" + (context ?? "unknown") +
                " Peer=" + (networkPeer?.UserName ?? networkPeer?.Index.ToString() ?? "null") +
                " TeamIndex=" + team.TeamIndex +
                " Side=" + team.Side +
                " ControlledAgentIndex=" + controlledAgent.Index +
                " AgentMainIndex=" + (mainAgent?.Index.ToString() ?? "null") +
                " GeneralAgentIndex=" + generalAgent.Index +
                " PlayerOrderOwnerIndex=" + (playerOrderOwnerIndex >= 0 ? playerOrderOwnerIndex.ToString() : "null") +
                " AgentOrderOwnerIndex=" + (agentOrderOwnerIndex >= 0 ? agentOrderOwnerIndex.ToString() : "null") +
                " HasOwnedFormationWithUnits=" + hasOwnedFormationWithUnits +
                " BotsUnderControlTotal=" + missionPeer.BotsUnderControlTotal +
                " BotsUnderControlAlive=" + missionPeer.BotsUnderControlAlive +
                " Mission=" + (mission?.SceneName ?? "null"));
            return true;
        }

        private static bool ShouldDeferNonCommanderSuppressionUntilControlledEntryIdentityResolves(
            string context,
            NetworkCommunicator networkPeer,
            MissionPeer missionPeer,
            Agent controlledAgent,
            Team team,
            string controlledEntryId,
            string commanderEntryId,
            Mission mission)
        {
            if (!string.IsNullOrWhiteSpace(controlledEntryId) || string.IsNullOrWhiteSpace(commanderEntryId))
                return false;

            string logKey =
                (context ?? "unknown") + "|" +
                (networkPeer?.Index.ToString() ?? "null") + "|" +
                (team?.TeamIndex.ToString() ?? "null") + "|" +
                (controlledAgent?.Index.ToString() ?? "null") + "|" +
                commanderEntryId + "|" +
                (missionPeer?.BotsUnderControlTotal.ToString() ?? "null") + "|" +
                (missionPeer?.BotsUnderControlAlive.ToString() ?? "null");
            if (!string.Equals(_lastDeferredNonCommanderSuppressionKey, logKey, StringComparison.Ordinal))
            {
                _lastDeferredNonCommanderSuppressionKey = logKey;
                ModLogger.Info(
                    "BattleMapSpawnHandoffPatch: keeping non-commander suppression active until controlled entry identity resolves. " +
                    "Context=" + (context ?? "unknown") +
                    " Peer=" + (networkPeer?.UserName ?? networkPeer?.Index.ToString() ?? "null") +
                    " TeamIndex=" + (team?.TeamIndex.ToString() ?? "null") +
                    " ControlledAgentIndex=" + (controlledAgent?.Index.ToString() ?? "null") +
                    " ControlledEntryId=null" +
                    " CommanderEntryId=" + commanderEntryId +
                    " BotsUnderControlTotal=" + (missionPeer?.BotsUnderControlTotal.ToString() ?? "null") +
                    " BotsUnderControlAlive=" + (missionPeer?.BotsUnderControlAlive.ToString() ?? "null") +
                    " Mission=" + (mission?.SceneName ?? "null"));
            }

            return false;
        }

        private static bool HasLocalCommanderFormationAuthority(Team team, Agent controlledAgent, Agent mainAgent)
        {
            if (team == null || controlledAgent == null)
                return false;

            foreach (Formation formation in team.FormationsIncludingEmpty)
            {
                if (formation == null || !ReferenceEquals(formation.Team, team) || formation.CountOfUnits <= 0)
                    continue;

                Agent formationPlayerOwner = TryGetInstanceMemberValue(formation, "PlayerOwner") as Agent;
                bool ownerMatchesLocalCommander =
                    formationPlayerOwner != null &&
                    (formationPlayerOwner.Index == controlledAgent.Index ||
                     (mainAgent != null && formationPlayerOwner.Index == mainAgent.Index));
                bool captainMatchesLocalCommander =
                    (formation.Captain != null && formation.Captain.Index == controlledAgent.Index) ||
                    (mainAgent != null && formation.Captain != null && formation.Captain.Index == mainAgent.Index);
                bool hasPlayerControlledTroop = TryGetInstanceBool(formation, "HasPlayerControlledTroop");
                bool isPlayerTroopInFormation = TryGetInstanceBool(formation, "IsPlayerTroopInFormation");
                if (ownerMatchesLocalCommander || captainMatchesLocalCommander || hasPlayerControlledTroop || isPlayerTroopInFormation)
                    return true;
            }

            return false;
        }

        private static bool IsEntryIdExactCommanderForTeam(Team team, string candidateEntryId, out string commanderEntryId)
        {
            commanderEntryId = null;
            if (!TryResolveCommanderEntryIdForTeam(team, out commanderEntryId) || string.IsNullOrWhiteSpace(candidateEntryId))
                return false;

            return string.Equals(
                StripEntryVariantSuffix(commanderEntryId),
                StripEntryVariantSuffix(candidateEntryId),
                StringComparison.Ordinal);
        }

        private static string StripEntryVariantSuffix(string entryId)
        {
            if (string.IsNullOrWhiteSpace(entryId))
                return entryId;

            const string variantMarker = "|variant-";
            int variantIndex = entryId.IndexOf(variantMarker, StringComparison.Ordinal);
            return variantIndex >= 0
                ? entryId.Substring(0, variantIndex)
                : entryId;
        }

        private static bool ShouldSuppressLocalFollowSwitch(
            MissionPeer missionPeer,
            Agent followedAgent,
            out string logKey,
            out string logDetails)
        {
            logKey = null;
            logDetails = null;

            if (missionPeer == null || followedAgent == null || !followedAgent.IsActive())
                return false;

            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return false;

            NetworkCommunicator myPeer = GameNetwork.MyPeer;
            if (myPeer == null || myPeer.IsServerPeer)
                return false;

            MissionPeer myMissionPeer = myPeer.GetComponent<MissionPeer>();
            if (myMissionPeer == null || !ReferenceEquals(missionPeer, myMissionPeer))
                return false;

            Mission mission = Mission.Current ?? followedAgent.Mission ?? myMissionPeer.ControlledAgent?.Mission;
            if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                return false;

            if (!SceneRuntimeClassifier.IsCampaignBattleScene(mission.SceneName ?? string.Empty))
                return false;

            Agent controlledAgent = myMissionPeer.ControlledAgent;
            bool isFollowSwitchToLocalPlayerAgent =
                followedAgent.IsMine ||
                ReferenceEquals(followedAgent, controlledAgent) ||
                ReferenceEquals(followedAgent.MissionPeer, myMissionPeer);
            if (!isFollowSwitchToLocalPlayerAgent)
                return false;

            CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot selectionBridge =
                CoopBattleSelectionBridgeFile.ReadCurrentSelection();
            CoopBattleEntryPolicy.ClientSnapshot entryPolicy =
                CoopBattleEntryPolicy.BuildClientSnapshot(mission, selectionBridge);
            if (entryPolicy == null || !entryPolicy.UseAuthoritativeTroopPath)
                return false;

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status =
                CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (status != null && !IsRelevantBattleMapStatus(status, myPeer.Index, mission.SceneName))
                status = null;

            logKey =
                myPeer.Index + "|" +
                followedAgent.Index + "|" +
                (controlledAgent?.Index.ToString() ?? "none") + "|" +
                (status?.SpawnStatus ?? "none") + "|" +
                (status?.LifecycleState ?? "none") + "|" +
                (entryPolicy.BridgeTroopOrEntryId ?? "none");
            logDetails =
                "Peer=" + (myPeer.UserName ?? myPeer.Index.ToString()) +
                " FollowedAgentIndex=" + followedAgent.Index +
                " ControlledAgentIndex=" + (controlledAgent?.Index.ToString() ?? "null") +
                " SpawnStatus=" + (status?.SpawnStatus ?? "null") +
                " LifecycleState=" + (status?.LifecycleState ?? "null") +
                " StatusHasAgent=" + (status?.HasAgent.ToString() ?? "null") +
                " BridgeTroop=" + (entryPolicy.BridgeTroopOrEntryId ?? "null") +
                " Mission=" + (mission.SceneName ?? "null");
            return true;
        }

        private static void LogObservedLocalSelectAllFormations(OrderController orderController, Agent selectorAgent)
        {
            try
            {
                if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive || orderController == null)
                    return;

                NetworkCommunicator myPeer = GameNetwork.MyPeer;
                if (myPeer == null || myPeer.IsServerPeer)
                    return;

                MissionPeer myMissionPeer = myPeer.GetComponent<MissionPeer>();
                Mission mission = Mission.Current ?? selectorAgent?.Mission ?? myMissionPeer?.ControlledAgent?.Mission;
                if (myMissionPeer == null || mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                    return;

                if (!SceneRuntimeClassifier.IsCampaignBattleScene(mission.SceneName ?? string.Empty))
                    return;

                CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot selectionBridge =
                    CoopBattleSelectionBridgeFile.ReadCurrentSelection();
                CoopBattleEntryPolicy.ClientSnapshot entryPolicy =
                    CoopBattleEntryPolicy.BuildClientSnapshot(mission, selectionBridge);
                if (entryPolicy == null || !entryPolicy.UseAuthoritativeTroopPath)
                    return;

                Agent controlledAgent = myMissionPeer.ControlledAgent;
                Agent orderOwner = orderController.Owner;
                bool hasSpawnState =
                    CoopBattleSpawnRuntimeState.TryGetState(myMissionPeer, out PeerSpawnRuntimeState spawnState);
                bool hasLifecycleState =
                    CoopBattlePeerLifecycleRuntimeState.TryGetState(myMissionPeer, out PeerLifecycleRuntimeState lifecycleState);

                string logKey =
                    myPeer.Index + "|" +
                    (controlledAgent?.Index.ToString() ?? "null") + "|" +
                    (orderOwner?.Index.ToString() ?? "null") + "|" +
                    myMissionPeer.BotsUnderControlTotal + "|" +
                    myMissionPeer.BotsUnderControlAlive + "|" +
                    (hasSpawnState ? spawnState.Status.ToString() : "none") + "|" +
                    (hasLifecycleState ? lifecycleState.Status.ToString() : "none");
                if (string.Equals(_lastObservedLocalSelectAllFormationsKey, logKey, StringComparison.Ordinal))
                    return;

                _lastObservedLocalSelectAllFormationsKey = logKey;
                ModLogger.Info(
                    "BattleMapSpawnHandoffPatch: observed local OrderController.SelectAllFormations without suppression on exact-scene spawn handshake candidate. " +
                    "Peer=" + (myPeer.UserName ?? myPeer.Index.ToString()) +
                    " ControlledAgentIndex=" + (controlledAgent?.Index.ToString() ?? "null") +
                    " OrderOwnerIndex=" + (orderOwner?.Index.ToString() ?? "null") +
                    " SelectorAgentIndex=" + (selectorAgent?.Index.ToString() ?? "null") +
                    " ControlledFormationIndex=" + (myMissionPeer.ControlledFormation?.FormationIndex.ToString() ?? "null") +
                    " BotsUnderControlTotal=" + myMissionPeer.BotsUnderControlTotal +
                    " BotsUnderControlAlive=" + myMissionPeer.BotsUnderControlAlive +
                    " SpawnStatus=" + (hasSpawnState ? spawnState.Status.ToString() : "null") +
                    " LifecycleState=" + (hasLifecycleState ? lifecycleState.Status.ToString() : "null") +
                    " BridgeTroop=" + (entryPolicy.BridgeTroopOrEntryId ?? "null") +
                    " Mission=" + (mission.SceneName ?? "null"));
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: local SelectAllFormations observation failed: " + ex.Message);
            }
        }

        private static bool ShouldSuppressLocalAssignFormationToPlayer(
            AssignFormationToPlayer assignFormationToPlayer,
            out string logKey,
            out string logDetails)
        {
            logKey = null;
            logDetails = null;

            if (assignFormationToPlayer == null || !GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return false;

            NetworkCommunicator myPeer = GameNetwork.MyPeer;
            if (myPeer == null || myPeer.IsServerPeer)
                return false;

            NetworkCommunicator targetPeer = TryResolveMessagePeer(assignFormationToPlayer);
            if (targetPeer != null && !ReferenceEquals(targetPeer, myPeer))
                return false;

            MissionPeer myMissionPeer = myPeer.GetComponent<MissionPeer>();
            Mission mission = Mission.Current ?? myMissionPeer?.ControlledAgent?.Mission;
            if (myMissionPeer == null || mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                return false;

            if (!SceneRuntimeClassifier.IsCampaignBattleScene(mission.SceneName ?? string.Empty))
                return false;

            int botsUnderControlTotal = myMissionPeer.BotsUnderControlTotal;
            int botsUnderControlAlive = myMissionPeer.BotsUnderControlAlive;
            if (botsUnderControlTotal <= 1 && botsUnderControlAlive <= 0)
                return false;

            CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot selectionBridge =
                CoopBattleSelectionBridgeFile.ReadCurrentSelection();
            CoopBattleEntryPolicy.ClientSnapshot entryPolicy =
                CoopBattleEntryPolicy.BuildClientSnapshot(mission, selectionBridge);
            if (entryPolicy == null || !entryPolicy.UseAuthoritativeTroopPath)
                return false;

            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status =
                CoopBattleEntryStatusBridgeFile.ReadStatus();
            if (status != null && !IsRelevantBattleMapStatus(status, myPeer.Index, mission.SceneName))
                status = null;

            if (status == null)
                return false;

            bool isFullySpawned =
                string.Equals(status.SpawnStatus, "Spawned", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(status.LifecycleState, "Alive", StringComparison.OrdinalIgnoreCase);
            if (isFullySpawned)
                return false;

            int formationIndex = TryResolveMessageInt(assignFormationToPlayer, "FormationIndex");
            logKey =
                myPeer.Index + "|" +
                formationIndex + "|" +
                botsUnderControlTotal + "|" +
                botsUnderControlAlive + "|" +
                (status?.SpawnStatus ?? "none") + "|" +
                (status?.LifecycleState ?? "none") + "|" +
                (entryPolicy.BridgeTroopOrEntryId ?? "none");
            logDetails =
                "Peer=" + (myPeer.UserName ?? myPeer.Index.ToString()) +
                " FormationIndex=" + formationIndex +
                " BotsUnderControlTotal=" + botsUnderControlTotal +
                " BotsUnderControlAlive=" + botsUnderControlAlive +
                " ControlledFormationIndex=" + (myMissionPeer.ControlledFormation?.FormationIndex.ToString() ?? "null") +
                " ControlledAgentIndex=" + (myMissionPeer.ControlledAgent?.Index.ToString() ?? "null") +
                " SpawnStatus=" + (status?.SpawnStatus ?? "null") +
                " LifecycleState=" + (status?.LifecycleState ?? "null") +
                " BridgeTroop=" + (entryPolicy.BridgeTroopOrEntryId ?? "null") +
                " Mission=" + (mission.SceneName ?? "null");
            return true;
        }

        private static bool ShouldSuppressServerSelectAllFormations(
            NetworkCommunicator networkPeer,
            out string logKey,
            out string logDetails)
        {
            logKey = null;
            logDetails = null;

            if (!GameNetwork.IsServer || networkPeer == null || networkPeer.IsServerPeer)
                return false;

            Mission mission = Mission.Current;
            if (mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                return false;

            if (!SceneRuntimeClassifier.IsCampaignBattleScene(mission.SceneName ?? string.Empty))
                return false;

            MissionPeer missionPeer = networkPeer.GetComponent<MissionPeer>();
            if (missionPeer == null || !missionPeer.IsControlledAgentActive)
                return false;

            Team team = missionPeer.Team ?? missionPeer.ControlledAgent?.Team;
            string controlledEntryId = ResolveAgentEntryId(
                missionPeer.ControlledAgent,
                CoopBattleSpawnRuntimeState.TryGetState(missionPeer, out PeerSpawnRuntimeState spawnIdentityState)
                    ? spawnIdentityState.EntryId
                    : null);
            bool isExactCommander = IsEntryIdExactCommanderForTeam(team, controlledEntryId, out string commanderEntryId);
            if (!isExactCommander &&
                ShouldDeferNonCommanderSuppressionUntilControlledEntryIdentityResolves(
                    "ServerSelectAllFormations",
                    networkPeer,
                    missionPeer,
                    missionPeer.ControlledAgent,
                    team,
                    controlledEntryId,
                    commanderEntryId,
                    mission))
            {
                return false;
            }

            if (!isExactCommander)
            {
                logKey =
                    networkPeer.Index + "|" +
                    (missionPeer.ControlledAgent?.Index.ToString() ?? "null") + "|" +
                    (controlledEntryId ?? "none") + "|" +
                    (commanderEntryId ?? "none") + "|" +
                    missionPeer.BotsUnderControlTotal + "|" +
                    missionPeer.BotsUnderControlAlive + "|non-commander";
                logDetails =
                    "Peer=" + (networkPeer.UserName ?? networkPeer.Index.ToString()) +
                    " ControlledAgentIndex=" + (missionPeer.ControlledAgent?.Index.ToString() ?? "null") +
                    " ControlledFormationIndex=" + (missionPeer.ControlledFormation?.FormationIndex.ToString() ?? "null") +
                    " BotsUnderControlTotal=" + missionPeer.BotsUnderControlTotal +
                    " BotsUnderControlAlive=" + missionPeer.BotsUnderControlAlive +
                    " ControlledEntryId=" + (controlledEntryId ?? "null") +
                    " CommanderEntryId=" + (commanderEntryId ?? "null") +
                    " SuppressionReason=non-commander" +
                    " Mission=" + (mission.SceneName ?? "null");
                return true;
            }

            if (!CoopBattleSpawnRuntimeState.TryGetState(missionPeer, out PeerSpawnRuntimeState spawnState) ||
                spawnState.Status != CoopBattleSpawnStatus.Spawned)
            {
                return false;
            }

            if (!CoopBattlePeerLifecycleRuntimeState.TryGetState(missionPeer, out PeerLifecycleRuntimeState lifecycleState) ||
                lifecycleState.Status != CoopBattlePeerLifecycleStatus.Alive)
            {
                return false;
            }

            if (missionPeer.BotsUnderControlTotal > 1 || missionPeer.BotsUnderControlAlive > 0)
                return false;

            logKey =
                networkPeer.Index + "|" +
                missionPeer.ControlledAgent?.Index + "|" +
                missionPeer.BotsUnderControlTotal + "|" +
                missionPeer.BotsUnderControlAlive + "|" +
                (spawnState.TroopId ?? "none") + "|" +
                (spawnState.EntryId ?? "none");
            logDetails =
                "Peer=" + (networkPeer.UserName ?? networkPeer.Index.ToString()) +
                " ControlledAgentIndex=" + (missionPeer.ControlledAgent?.Index.ToString() ?? "null") +
                " ControlledFormationIndex=" + (missionPeer.ControlledFormation?.FormationIndex.ToString() ?? "null") +
                " BotsUnderControlTotal=" + missionPeer.BotsUnderControlTotal +
                " BotsUnderControlAlive=" + missionPeer.BotsUnderControlAlive +
                " SpawnStatus=" + spawnState.Status +
                " LifecycleState=" + lifecycleState.Status +
                " TroopId=" + (spawnState.TroopId ?? "null") +
                " EntryId=" + (spawnState.EntryId ?? "null") +
                " Mission=" + (mission.SceneName ?? "null");
            return true;
        }

        private static bool ShouldSuppressLocalSelectAllFormations(
            OrderController orderController,
            Agent selectorAgent,
            out string logKey,
            out string logDetails)
        {
            logKey = null;
            logDetails = null;

            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive || orderController == null)
                return false;

            NetworkCommunicator myPeer = GameNetwork.MyPeer;
            if (myPeer == null || myPeer.IsServerPeer)
                return false;

            MissionPeer myMissionPeer = myPeer.GetComponent<MissionPeer>();
            Mission mission = Mission.Current ?? selectorAgent?.Mission ?? myMissionPeer?.ControlledAgent?.Mission;
            if (myMissionPeer == null || mission == null || !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName))
                return false;

            if (!SceneRuntimeClassifier.IsCampaignBattleScene(mission.SceneName ?? string.Empty))
                return false;

            Agent controlledAgent = myMissionPeer.ControlledAgent;
            if (controlledAgent == null || !controlledAgent.IsActive())
                return false;

            Team team = myMissionPeer.Team ?? controlledAgent.Team ?? mission.PlayerTeam;
            if (!ReferenceEquals(orderController, team?.PlayerOrderController))
                return false;

            CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot selectionBridge =
                CoopBattleSelectionBridgeFile.ReadCurrentSelection();
            CoopBattleEntryPolicy.ClientSnapshot entryPolicy =
                CoopBattleEntryPolicy.BuildClientSnapshot(mission, selectionBridge);
            if (entryPolicy == null || !entryPolicy.UseAuthoritativeTroopPath)
                return false;

            Agent mainAgent = Agent.Main;
            string controlledEntryId = ResolveControlledEntryId(
                myMissionPeer,
                controlledAgent,
                mission,
                entryPolicy.BridgeTroopOrEntryId,
                out _);
            bool isExactCommander = IsEntryIdExactCommanderForTeam(team, controlledEntryId, out string commanderEntryId);
            if (!isExactCommander &&
                string.IsNullOrWhiteSpace(controlledEntryId) &&
                TryBypassNonCommanderSuppressionFromEstablishedLocalCommanderState(
                    context: "SelectAllFormations",
                    myPeer,
                    myMissionPeer,
                    team,
                    controlledAgent,
                    mainAgent,
                    mission))
            {
                return false;
            }

            if (!isExactCommander &&
                ShouldDeferNonCommanderSuppressionUntilControlledEntryIdentityResolves(
                    "SelectAllFormations",
                    myPeer,
                    myMissionPeer,
                    controlledAgent,
                    team,
                    controlledEntryId,
                    commanderEntryId,
                    mission))
            {
                return false;
            }

            if (!isExactCommander)
            {
                logKey =
                    myPeer.Index + "|" +
                    controlledAgent.Index + "|" +
                    (controlledEntryId ?? "none") + "|" +
                    (commanderEntryId ?? "none") + "|" +
                    myMissionPeer.BotsUnderControlTotal + "|" +
                    myMissionPeer.BotsUnderControlAlive + "|non-commander";
                logDetails =
                    "Peer=" + (myPeer.UserName ?? myPeer.Index.ToString()) +
                    " ControlledAgentIndex=" + controlledAgent.Index +
                    " OrderOwnerIndex=" + (orderController.Owner?.Index.ToString() ?? "null") +
                    " SelectorAgentIndex=" + (selectorAgent?.Index.ToString() ?? "null") +
                    " ControlledFormationIndex=" + (myMissionPeer.ControlledFormation?.FormationIndex.ToString() ?? "null") +
                    " BotsUnderControlTotal=" + myMissionPeer.BotsUnderControlTotal +
                    " BotsUnderControlAlive=" + myMissionPeer.BotsUnderControlAlive +
                    " ControlledEntryId=" + (controlledEntryId ?? "null") +
                    " CommanderEntryId=" + (commanderEntryId ?? "null") +
                    " SuppressionPhase=non-commander" +
                    " Mission=" + (mission.SceneName ?? "null");
                return true;
            }

            Agent orderOwner = orderController.Owner;
            if (orderOwner != null && !ReferenceEquals(orderOwner, controlledAgent))
                return false;

            if (selectorAgent != null && !ReferenceEquals(selectorAgent, controlledAgent))
                return false;

            bool hasSpawnState = CoopBattleSpawnRuntimeState.TryGetState(myMissionPeer, out PeerSpawnRuntimeState spawnState);
            bool hasLifecycleState = CoopBattlePeerLifecycleRuntimeState.TryGetState(myMissionPeer, out PeerLifecycleRuntimeState lifecycleState);
            bool hasControlledFormation = myMissionPeer.ControlledFormation != null;
            bool hasLiveControlledBots = myMissionPeer.BotsUnderControlAlive > 0;
            bool hasMaterializedControlledFormation = myMissionPeer.BotsUnderControlTotal > 1;
            bool isEarlyExactSceneSpawnHandshake =
                !hasControlledFormation &&
                !hasLiveControlledBots &&
                !hasMaterializedControlledFormation;
            bool isLateExactSceneSpawnHandshake =
                hasSpawnState &&
                spawnState.Status == CoopBattleSpawnStatus.Spawned &&
                hasLifecycleState &&
                lifecycleState.Status == CoopBattlePeerLifecycleStatus.Alive &&
                !hasControlledFormation &&
                !hasLiveControlledBots &&
                !hasMaterializedControlledFormation;

            if (!isEarlyExactSceneSpawnHandshake && !isLateExactSceneSpawnHandshake)
                return false;

            logKey =
                myPeer.Index + "|" +
                controlledAgent.Index + "|" +
                myMissionPeer.BotsUnderControlTotal + "|" +
                myMissionPeer.BotsUnderControlAlive + "|" +
                (hasSpawnState ? (spawnState.TroopId ?? "none") : "none") + "|" +
                (hasSpawnState ? (spawnState.EntryId ?? "none") : "none") + "|" +
                (isEarlyExactSceneSpawnHandshake ? "early-preformation" : "late-spawned-alive");
            logDetails =
                "Peer=" + (myPeer.UserName ?? myPeer.Index.ToString()) +
                " ControlledAgentIndex=" + controlledAgent.Index +
                " OrderOwnerIndex=" + (orderOwner?.Index.ToString() ?? "null") +
                " SelectorAgentIndex=" + (selectorAgent?.Index.ToString() ?? "null") +
                " ControlledFormationIndex=" + (myMissionPeer.ControlledFormation?.FormationIndex.ToString() ?? "null") +
                " BotsUnderControlTotal=" + myMissionPeer.BotsUnderControlTotal +
                " BotsUnderControlAlive=" + myMissionPeer.BotsUnderControlAlive +
                " SuppressionPhase=" + (isEarlyExactSceneSpawnHandshake ? "early-preformation" : "late-spawned-alive") +
                " SpawnStatus=" + (hasSpawnState ? spawnState.Status.ToString() : "null") +
                " LifecycleState=" + (hasLifecycleState ? lifecycleState.Status.ToString() : "null") +
                " TroopId=" + (hasSpawnState ? (spawnState.TroopId ?? "null") : "null") +
                " EntryId=" + (hasSpawnState ? (spawnState.EntryId ?? "null") : "null") +
                " Mission=" + (mission.SceneName ?? "null");
            return true;
        }

        private static bool ShouldSuppressOrderTroopPlacerTick(
            object orderTroopPlacer,
            out string logKey,
            out string logDetails)
        {
            logKey = null;
            logDetails = null;

            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive || orderTroopPlacer == null)
                return false;

            NetworkCommunicator myPeer = GameNetwork.MyPeer;
            if (myPeer == null || myPeer.IsServerPeer)
                return false;

            MissionPeer myMissionPeer = myPeer.GetComponent<MissionPeer>();
            Agent controlledAgent = myMissionPeer?.ControlledAgent;
            Mission mission = Mission.Current ?? controlledAgent?.Mission;
            Team team = myMissionPeer?.Team ?? controlledAgent?.Team ?? mission?.PlayerTeam;
            if (myMissionPeer == null ||
                controlledAgent == null ||
                !controlledAgent.IsActive() ||
                mission == null ||
                team == null ||
                !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName) ||
                !SceneRuntimeClassifier.IsCampaignBattleScene(mission.SceneName ?? string.Empty))
            {
                return false;
            }

            CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot selectionBridge =
                CoopBattleSelectionBridgeFile.ReadCurrentSelection();
            CoopBattleEntryPolicy.ClientSnapshot entryPolicy =
                CoopBattleEntryPolicy.BuildClientSnapshot(mission, selectionBridge);
            if (entryPolicy == null || !entryPolicy.UseAuthoritativeTroopPath)
                return false;

            Agent mainAgent = Agent.Main;
            string controlledEntryId = ResolveControlledEntryId(
                myMissionPeer,
                controlledAgent,
                mission,
                entryPolicy.BridgeTroopOrEntryId,
                out _);
            bool isExactCommander = IsEntryIdExactCommanderForTeam(team, controlledEntryId, out string commanderEntryId);
            if (!isExactCommander &&
                string.IsNullOrWhiteSpace(controlledEntryId) &&
                TryBypassNonCommanderSuppressionFromEstablishedLocalCommanderState(
                    context: "OrderTroopPlacerTick",
                    myPeer,
                    myMissionPeer,
                    team,
                    controlledAgent,
                    mainAgent,
                    mission))
            {
                isExactCommander = true;
            }

            if (!isExactCommander &&
                ShouldDeferNonCommanderSuppressionUntilControlledEntryIdentityResolves(
                    "OrderTroopPlacerTick",
                    myPeer,
                    myMissionPeer,
                    controlledAgent,
                    team,
                    controlledEntryId,
                    commanderEntryId,
                    mission))
            {
                isExactCommander = true;
            }

            if (!isExactCommander)
            {
                logKey =
                    myPeer.Index + "|" +
                    controlledAgent.Index + "|" +
                    team.TeamIndex + "|" +
                    (controlledEntryId ?? "none") + "|" +
                    (commanderEntryId ?? "none") + "|" +
                    myMissionPeer.BotsUnderControlTotal + "|" +
                    myMissionPeer.BotsUnderControlAlive + "|non-commander";
                logDetails =
                    "Peer=" + (myPeer.UserName ?? myPeer.Index.ToString()) +
                    " ControlledAgentIndex=" + controlledAgent.Index +
                    " TeamIndex=" + team.TeamIndex +
                    " ControlledFormationIndex=" + (myMissionPeer.ControlledFormation?.FormationIndex.ToString() ?? "null") +
                    " PlayerOrderOwnerIndex=" + (team.PlayerOrderController?.Owner?.Index.ToString() ?? "null") +
                    " BotsUnderControlTotal=" + myMissionPeer.BotsUnderControlTotal +
                    " BotsUnderControlAlive=" + myMissionPeer.BotsUnderControlAlive +
                    " ControlledEntryId=" + (controlledEntryId ?? "null") +
                    " CommanderEntryId=" + (commanderEntryId ?? "null") +
                    " SuppressionReason=non-commander" +
                    " Mission=" + (mission.SceneName ?? "null");
                return true;
            }

            if (!ReferenceEquals(mission.PlayerTeam, team))
                mission.PlayerTeam = team;

            OrderController playerOrderController = team.PlayerOrderController;
            if (playerOrderController == null)
                return false;

            if (playerOrderController.Owner == null)
            {
                try
                {
                    playerOrderController.Owner = controlledAgent;
                }
                catch
                {
                }
            }

            OrderController agentOrderController = team.GetOrderControllerOf(controlledAgent);
            if (agentOrderController != null && agentOrderController.Owner == null)
            {
                try
                {
                    agentOrderController.Owner = controlledAgent;
                }
                catch
                {
                }
            }

            object missionScreen = TryGetInstanceMemberValue(orderTroopPlacer, "MissionScreen");
            object troopPlacerOrderFlag = TryGetInstanceMemberValue(orderTroopPlacer, "OrderFlag");
            object missionScreenOrderFlag = TryGetInstanceMemberValue(missionScreen, "OrderFlag");
            if (missionScreenOrderFlag == null && missionScreen != null && troopPlacerOrderFlag != null)
            {
                TrySetInstanceMemberValue(missionScreen, "OrderFlag", troopPlacerOrderFlag);
                missionScreenOrderFlag = TryGetInstanceMemberValue(missionScreen, "OrderFlag");
            }

            if (playerOrderController.Owner != null && missionScreenOrderFlag != null)
            {
                string repairedLogKey =
                    myPeer.Index + "|" +
                    controlledAgent.Index + "|" +
                    team.TeamIndex + "|" +
                    playerOrderController.Owner.Index + "|" +
                    missionScreenOrderFlag.GetType().Name + "|" +
                    (entryPolicy.BridgeTroopOrEntryId ?? "none");
                if (!string.Equals(_lastRepairedOrderTroopPlacerOwnerKey, repairedLogKey, StringComparison.Ordinal))
                {
                    _lastRepairedOrderTroopPlacerOwnerKey = repairedLogKey;
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: repaired local PlayerOrderController.Owner before OrderTroopPlacer tick. " +
                        "Peer=" + (myPeer.UserName ?? myPeer.Index.ToString()) +
                        " ControlledAgentIndex=" + controlledAgent.Index +
                        " TeamIndex=" + team.TeamIndex +
                        " OwnerIndex=" + playerOrderController.Owner.Index +
                        " AgentOrderOwnerIndex=" + (agentOrderController?.Owner?.Index.ToString() ?? "null") +
                        " MissionScreenOrderFlagType=" + missionScreenOrderFlag.GetType().Name +
                        " EntryId=" + (entryPolicy.BridgeTroopOrEntryId ?? "null") +
                        " Mission=" + (mission.SceneName ?? "null"));
                }

                return false;
            }

            bool hasSpawnState = CoopBattleSpawnRuntimeState.TryGetState(myMissionPeer, out PeerSpawnRuntimeState spawnState);
            bool hasLifecycleState = CoopBattlePeerLifecycleRuntimeState.TryGetState(myMissionPeer, out PeerLifecycleRuntimeState lifecycleState);
            logKey =
                myPeer.Index + "|" +
                controlledAgent.Index + "|" +
                team.TeamIndex + "|" +
                (hasSpawnState ? spawnState.Status.ToString() : "none") + "|" +
                (hasLifecycleState ? lifecycleState.Status.ToString() : "none") + "|" +
                (entryPolicy.BridgeTroopOrEntryId ?? "none");
            logDetails =
                "Peer=" + (myPeer.UserName ?? myPeer.Index.ToString()) +
                " ControlledAgentIndex=" + controlledAgent.Index +
                " TeamIndex=" + team.TeamIndex +
                " ControlledFormationIndex=" + (myMissionPeer.ControlledFormation?.FormationIndex.ToString() ?? "null") +
                " PlayerOrderOwnerIndex=" + (playerOrderController.Owner?.Index.ToString() ?? "null") +
                " AgentOrderOwnerIndex=" + (agentOrderController?.Owner?.Index.ToString() ?? "null") +
                " MissionScreenReady=" + (missionScreen != null) +
                " MissionScreenOrderFlagReady=" + (missionScreenOrderFlag != null) +
                " TroopPlacerOrderFlagReady=" + (troopPlacerOrderFlag != null) +
                " BotsUnderControlTotal=" + myMissionPeer.BotsUnderControlTotal +
                " BotsUnderControlAlive=" + myMissionPeer.BotsUnderControlAlive +
                " SpawnStatus=" + (hasSpawnState ? spawnState.Status.ToString() : "null") +
                " LifecycleState=" + (hasLifecycleState ? lifecycleState.Status.ToString() : "null") +
                " EntryId=" + (entryPolicy.BridgeTroopOrEntryId ?? "null") +
                " Mission=" + (mission.SceneName ?? "null");
            return true;
        }

        private static bool TryPromoteLocalExactCommanderToGeneral(
            MissionPeer myMissionPeer,
            int botAliveCount,
            int botTotalCount,
            out string logKey,
            out string logDetails)
        {
            logKey = null;
            logDetails = null;

            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return false;

            NetworkCommunicator myPeer = GameNetwork.MyPeer;
            if (myPeer == null || myPeer.IsServerPeer || myMissionPeer == null || !ReferenceEquals(myMissionPeer, myPeer.GetComponent<MissionPeer>()))
                return false;

            Agent controlledAgent = myMissionPeer?.ControlledAgent;
            Mission mission = Mission.Current ?? controlledAgent?.Mission;
            Team team = myMissionPeer?.Team ?? controlledAgent?.Team;
            if (myMissionPeer == null ||
                controlledAgent == null ||
                !controlledAgent.IsActive() ||
                mission == null ||
                team == null ||
                !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName) ||
                !SceneRuntimeClassifier.IsCampaignBattleScene(mission.SceneName ?? string.Empty))
            {
                return false;
            }

            if (botTotalCount <= 1 && botAliveCount <= 0)
                return false;

            Formation priorControlledFormation = myMissionPeer.ControlledFormation ?? controlledAgent.Formation;
            bool promotionBeforeLocalFormationAttach = priorControlledFormation == null;

            if (team.IsPlayerGeneral && ReferenceEquals(team.GeneralAgent, controlledAgent))
                return false;

            CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot selectionBridge =
                CoopBattleSelectionBridgeFile.ReadCurrentSelection();
            CoopBattleEntryPolicy.ClientSnapshot entryPolicy =
                CoopBattleEntryPolicy.BuildClientSnapshot(mission, selectionBridge);
            if (entryPolicy == null || !entryPolicy.UseAuthoritativeTroopPath)
                return false;

            BattleRuntimeState runtimeState = BattleSnapshotRuntimeState.GetState();
            RosterEntryState commanderEntry = BattleCommanderResolver.ResolveCommanderEntry(runtimeState, team.Side);
            if (commanderEntry == null || string.IsNullOrWhiteSpace(commanderEntry.EntryId))
                return false;

            string controlledEntryId = ResolveControlledEntryId(
                myMissionPeer,
                controlledAgent,
                mission,
                entryPolicy.BridgeTroopOrEntryId,
                out _);
            if (!IsEntryIdExactCommanderForTeam(team, controlledEntryId, out _))
                return false;

            if (promotionBeforeLocalFormationAttach)
            {
                string preFormationLogKey =
                    myPeer.Index + "|" +
                    team.TeamIndex + "|" +
                    controlledAgent.Index + "|" +
                    commanderEntry.EntryId + "|" +
                    botAliveCount + "|" +
                    botTotalCount;
                if (!string.Equals(_lastPreFormationCommanderPromotionKey, preFormationLogKey, StringComparison.Ordinal))
                {
                    _lastPreFormationCommanderPromotionKey = preFormationLogKey;
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: proceeding with local exact-scene commander promotion before local formation attach. " +
                        "Peer=" + (myPeer.UserName ?? myPeer.Index.ToString()) +
                        " TeamIndex=" + team.TeamIndex +
                        " Side=" + team.Side +
                        " EntryId=" + commanderEntry.EntryId +
                        " ControlledAgentIndex=" + controlledAgent.Index +
                        " ControlledFormationIndex=" + (myMissionPeer.ControlledFormation?.FormationIndex.ToString() ?? "null") +
                        " AgentFormationIndex=" + (controlledAgent.Formation?.FormationIndex.ToString() ?? "null") +
                        " BotsUnderControlAlive=" + botAliveCount +
                        " BotsUnderControlTotal=" + botTotalCount +
                        " Mission=" + (mission.SceneName ?? "null"));
                }
            }

            team.SetPlayerRole(isPlayerGeneral: true, isPlayerSergeant: false);
            controlledAgent.SetCanLeadFormationsRemotely(value: true);
            if (!ReferenceEquals(team.GeneralAgent, controlledAgent))
                team.GeneralAgent = controlledAgent;

            OrderController playerOrderController = team.PlayerOrderController;
            if (playerOrderController == null)
                return false;

            Agent orderOwnerAgent = Agent.Main ?? controlledAgent;
            playerOrderController.Owner = orderOwnerAgent;
            foreach (Formation formation in team.FormationsIncludingEmpty)
            {
                if (formation == null || !ReferenceEquals(formation.Team, team))
                    continue;

                TrySetInstanceMemberValue(formation, "PlayerOwner", orderOwnerAgent);
                bool isOwnedFormation = formation.CountOfUnits > 0;
                TrySetInstanceMemberValue(formation, "HasPlayerControlledTroop", isOwnedFormation);
                TrySetInstanceMemberValue(formation, "IsPlayerTroopInFormation", isOwnedFormation);
            }

            Formation commanderFormation = controlledAgent.Formation ?? priorControlledFormation;
            if (commanderFormation != null && ReferenceEquals(commanderFormation.Team, team))
                commanderFormation.Captain = controlledAgent;

            myMissionPeer.ControlledFormation = null;
            _pendingLocalCommanderOrderControlFinalization = new PendingLocalCommanderOrderControlFinalization
            {
                PeerIndex = myPeer.Index,
                TeamIndex = team.TeamIndex,
                AgentIndex = controlledAgent.Index,
                EntryId = commanderEntry.EntryId,
                QueuedUtc = DateTime.UtcNow,
                Attempts = 0
            };

            logKey =
                myPeer.Index + "|" +
                controlledAgent.Index + "|" +
                team.TeamIndex + "|" +
                commanderEntry.EntryId + "|" +
                botAliveCount + "|" +
                botTotalCount;
            logDetails =
                "Peer=" + (myPeer.UserName ?? myPeer.Index.ToString()) +
                " TeamIndex=" + team.TeamIndex +
                " Side=" + team.Side +
                " EntryId=" + commanderEntry.EntryId +
                " ControlledAgentIndex=" + controlledAgent.Index +
                " AgentMainIndex=" + (Agent.Main?.Index.ToString() ?? "null") +
                " OrderOwnerIndex=" + (orderOwnerAgent?.Index.ToString() ?? "null") +
                " PreviousControlledFormationIndex=" + (priorControlledFormation?.FormationIndex.ToString() ?? "null") +
                " ControlledFormationIndex=" + (myMissionPeer.ControlledFormation?.FormationIndex.ToString() ?? "null") +
                " PromotionBeforeLocalFormationAttach=" + promotionBeforeLocalFormationAttach +
                " BotsUnderControlAlive=" + botAliveCount +
                " BotsUnderControlTotal=" + botTotalCount +
                " FormationCount=" + team.FormationsIncludingEmpty.Count +
                " Mission=" + (mission.SceneName ?? "null");
            return true;
        }

        private static void TryFinalizePendingLocalCommanderOrderControl(object orderUiHandler)
        {
            PendingLocalCommanderOrderControlFinalization pending = _pendingLocalCommanderOrderControlFinalization;
            if (pending == null)
                return;

            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive)
            {
                _pendingLocalCommanderOrderControlFinalization = null;
                return;
            }

            NetworkCommunicator myPeer = GameNetwork.MyPeer;
            if (myPeer == null || myPeer.IsServerPeer || myPeer.Index != pending.PeerIndex)
                return;

            MissionPeer myMissionPeer = myPeer.GetComponent<MissionPeer>();
            Agent controlledAgent = myMissionPeer?.ControlledAgent;
            Mission mission = Mission.Current ?? controlledAgent?.Mission;
            Team team = myMissionPeer?.Team ?? controlledAgent?.Team;
            if (myMissionPeer == null ||
                controlledAgent == null ||
                !controlledAgent.IsActive() ||
                mission == null ||
                team == null ||
                !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName) ||
                !SceneRuntimeClassifier.IsCampaignBattleScene(mission.SceneName ?? string.Empty))
            {
                return;
            }

            pending.Attempts++;
            if (controlledAgent.Index != pending.AgentIndex || team.TeamIndex != pending.TeamIndex)
            {
                if (DateTime.UtcNow - pending.QueuedUtc > TimeSpan.FromSeconds(5))
                    _pendingLocalCommanderOrderControlFinalization = null;

                return;
            }

            Agent mainAgent = Agent.Main;
            if (mainAgent == null || !mainAgent.IsActive() || mainAgent.Index != controlledAgent.Index)
                return;

            if (!ReferenceEquals(mission.PlayerTeam, team))
                mission.PlayerTeam = team;

            OrderController playerOrderController = team.PlayerOrderController;
            if (playerOrderController == null)
                return;

            OrderController agentOrderController = team.GetOrderControllerOf(mainAgent);
            if (agentOrderController != null && !ReferenceEquals(agentOrderController.Owner, mainAgent))
                agentOrderController.Owner = mainAgent;
            TryDisableExactCommanderClientFormationUpdatesAfterSetOrder(
                myPeer,
                mission,
                team,
                mainAgent,
                playerOrderController,
                agentOrderController,
                pending.EntryId);

            TryInvokeParameterlessMethod(orderUiHandler, "InitializeInADisgustingManner");
            TryInvokeParameterlessMethod(orderUiHandler, "ValidateInADisgustingManner");

            object dataSource = TryGetInstanceMemberValue(orderUiHandler, "_dataSource");
            if (dataSource == null)
            {
                if (pending.Attempts == 1 || pending.Attempts % 30 == 0)
                {
                    bool isValid = TryGetInstanceBool(orderUiHandler, "_isValid");
                    bool isInitialized = TryGetInstanceBool(orderUiHandler, "_isInitialized");
                    bool shouldTick = TryGetInstanceBool(orderUiHandler, "_shouldTick");
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: waiting for local exact-scene commander order UI data source before finalization. " +
                        "Peer=" + (myPeer.UserName ?? myPeer.Index.ToString()) +
                        " TeamIndex=" + team.TeamIndex +
                        " EntryId=" + (pending.EntryId ?? "null") +
                        " ControlledAgentIndex=" + controlledAgent.Index +
                        " AgentMainIndex=" + mainAgent.Index +
                        " MissionPlayerTeamIndex=" + (mission.PlayerTeam?.TeamIndex.ToString() ?? "null") +
                        " HandlerIsValid=" + isValid +
                        " HandlerIsInitialized=" + isInitialized +
                        " HandlerShouldTick=" + shouldTick +
                        " Attempts=" + pending.Attempts +
                        " Mission=" + (mission.SceneName ?? "null"));
                }

                return;
            }

            team.SetPlayerRole(isPlayerGeneral: true, isPlayerSergeant: false);
            controlledAgent.SetCanLeadFormationsRemotely(value: true);
            if (!ReferenceEquals(team.GeneralAgent, controlledAgent))
                team.GeneralAgent = controlledAgent;

            playerOrderController.Owner = mainAgent;
            if (agentOrderController != null)
                agentOrderController.Owner = mainAgent;

            bool rebuiltSingleplayerDataSource = TryEnsureExactCommanderSingleplayerOrderDataSource(
                myPeer,
                mission,
                team,
                mainAgent,
                orderUiHandler,
                playerOrderController,
                pending.EntryId,
                ref dataSource);
            bool campaignUiSemanticsForced = TryForceCampaignCommanderOrderUiSemantics(
                myPeer,
                mission,
                team,
                mainAgent,
                orderUiHandler,
                dataSource,
                pending.EntryId);

            int ownedFormationCount = 0;
            int formationsWithUnits = 0;
            foreach (Formation formation in team.FormationsIncludingEmpty)
            {
                if (formation == null || !ReferenceEquals(formation.Team, team))
                    continue;

                TrySetInstanceMemberValue(formation, "PlayerOwner", mainAgent);
                bool isOwnedFormation = formation.CountOfUnits > 0;
                TrySetInstanceMemberValue(formation, "HasPlayerControlledTroop", isOwnedFormation);
                TrySetInstanceMemberValue(formation, "IsPlayerTroopInFormation", isOwnedFormation);
                if (formation.CountOfUnits > 0)
                {
                    formationsWithUnits++;
                    Agent formationPlayerOwner = TryGetInstanceMemberValue(formation, "PlayerOwner") as Agent;
                    if (formationPlayerOwner != null && formationPlayerOwner.Index == mainAgent.Index)
                        ownedFormationCount++;
                }
            }

            Formation commanderFormation = controlledAgent.Formation;
            if (commanderFormation != null && ReferenceEquals(commanderFormation.Team, team))
                commanderFormation.Captain = controlledAgent;

            myMissionPeer.ControlledFormation = null;

            bool afterInitializeInvoked = TryInvokeParameterlessMethod(dataSource, "AfterInitialize");
            object troopController = TryGetInstanceMemberValue(dataSource, "TroopController");
            bool updateTroopsInvoked = TryInvokeParameterlessMethod(troopController, "UpdateTroops");
            TryLogExactCommanderOrderVmState(
                context: "FinalizePending",
                myPeer,
                mission,
                team,
                controlledAgent,
                mainAgent,
                playerOrderController,
                dataSource,
                troopController);

            string logKey =
                myPeer.Index + "|" +
                team.TeamIndex + "|" +
                controlledAgent.Index + "|" +
                mainAgent.Index + "|" +
                ownedFormationCount + "|" +
                formationsWithUnits + "|" +
                pending.EntryId;
            if (!string.Equals(_lastFinalizedLocalCommanderOrderControlKey, logKey, StringComparison.Ordinal))
            {
                _lastFinalizedLocalCommanderOrderControlKey = logKey;
                ModLogger.Info(
                    "BattleMapSpawnHandoffPatch: finalized local exact-scene commander order control after Agent.Main attach. " +
                    "Peer=" + (myPeer.UserName ?? myPeer.Index.ToString()) +
                    " TeamIndex=" + team.TeamIndex +
                    " Side=" + team.Side +
                    " EntryId=" + (pending.EntryId ?? "null") +
                    " ControlledAgentIndex=" + controlledAgent.Index +
                    " AgentMainIndex=" + mainAgent.Index +
                    " MissionPlayerTeamIndex=" + (mission.PlayerTeam?.TeamIndex.ToString() ?? "null") +
                    " PlayerOrderOwnerIndex=" + (playerOrderController.Owner?.Index.ToString() ?? "null") +
                    " AgentOrderOwnerIndex=" + (agentOrderController?.Owner?.Index.ToString() ?? "null") +
                    " TeamIsPlayerGeneral=" + team.IsPlayerGeneral +
                    " ControlledFormationIndex=" + (myMissionPeer.ControlledFormation?.FormationIndex.ToString() ?? "null") +
                    " OwnedFormationsWithUnits=" + ownedFormationCount +
                    " FormationsWithUnits=" + formationsWithUnits +
                    " SingleplayerDataSourceRebuilt=" + rebuiltSingleplayerDataSource +
                    " CampaignUiSemanticsForced=" + campaignUiSemanticsForced +
                    " DataSourceAfterInitializeInvoked=" + afterInitializeInvoked +
                    " TroopControllerUpdateInvoked=" + updateTroopsInvoked +
                    " Attempts=" + pending.Attempts +
                    " Mission=" + (mission.SceneName ?? "null"));
            }

            _pendingLocalCommanderOrderControlFinalization = null;
        }

        private static void TryMaintainLocalCommanderOrderControl(object orderUiHandler)
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive)
                return;

            NetworkCommunicator myPeer = GameNetwork.MyPeer;
            if (myPeer == null || myPeer.IsServerPeer)
                return;

            MissionPeer myMissionPeer = myPeer.GetComponent<MissionPeer>();
            Agent controlledAgent = myMissionPeer?.ControlledAgent;
            Agent mainAgent = Agent.Main;
            Mission mission = Mission.Current ?? controlledAgent?.Mission ?? mainAgent?.Mission;
            Team team = myMissionPeer?.Team ?? controlledAgent?.Team ?? mainAgent?.Team;
            if (myMissionPeer == null ||
                controlledAgent == null ||
                !controlledAgent.IsActive() ||
                mainAgent == null ||
                !mainAgent.IsActive() ||
                mainAgent.Index != controlledAgent.Index ||
                mission == null ||
                team == null ||
                !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName) ||
                !SceneRuntimeClassifier.IsCampaignBattleScene(mission.SceneName ?? string.Empty) ||
                !team.IsPlayerGeneral ||
                !ReferenceEquals(team.GeneralAgent, controlledAgent))
            {
                return;
            }

            object dataSource = TryGetInstanceMemberValue(orderUiHandler, "_dataSource");
            object troopController = TryGetInstanceMemberValue(dataSource, "TroopController");
            if (dataSource == null || troopController == null)
                return;

            if (!ReferenceEquals(mission.PlayerTeam, team))
                mission.PlayerTeam = team;

            OrderController playerOrderController = team.PlayerOrderController;
            if (playerOrderController == null)
                return;

            OrderController agentOrderController = team.GetOrderControllerOf(mainAgent);
            if (!ReferenceEquals(playerOrderController.Owner, mainAgent))
                playerOrderController.Owner = mainAgent;
            if (agentOrderController != null && !ReferenceEquals(agentOrderController.Owner, mainAgent))
                agentOrderController.Owner = mainAgent;
            TryDisableExactCommanderClientFormationUpdatesAfterSetOrder(
                myPeer,
                mission,
                team,
                mainAgent,
                playerOrderController,
                agentOrderController,
                entryId: null);

            bool rebuiltSingleplayerDataSource = TryEnsureExactCommanderSingleplayerOrderDataSource(
                myPeer,
                mission,
                team,
                mainAgent,
                orderUiHandler,
                playerOrderController,
                entryId: null,
                ref dataSource);
            troopController = TryGetInstanceMemberValue(dataSource, "TroopController");
            if (troopController == null)
                return;

            bool campaignUiSemanticsForced = TryForceCampaignCommanderOrderUiSemantics(
                myPeer,
                mission,
                team,
                mainAgent,
                orderUiHandler,
                dataSource,
                entryId: null);

            int formationsWithUnits = 0;
            int ownedFormationsWithUnits = 0;
            foreach (Formation formation in team.FormationsIncludingEmpty)
            {
                if (formation == null || !ReferenceEquals(formation.Team, team))
                    continue;

                TrySetInstanceMemberValue(formation, "PlayerOwner", mainAgent);
                bool isOwnedFormation = formation.CountOfUnits > 0;
                TrySetInstanceMemberValue(formation, "HasPlayerControlledTroop", isOwnedFormation);
                TrySetInstanceMemberValue(formation, "IsPlayerTroopInFormation", isOwnedFormation);
                if (formation.CountOfUnits <= 0)
                    continue;

                formationsWithUnits++;
                Agent formationPlayerOwner = TryGetInstanceMemberValue(formation, "PlayerOwner") as Agent;
                if (formationPlayerOwner != null && formationPlayerOwner.Index == mainAgent.Index)
                    ownedFormationsWithUnits++;
            }

            int selectedFormationCount = playerOrderController.SelectedFormations?.Count ?? 0;
            bool afterInitializeInvoked = false;
            bool updateTroopsInvoked = false;
            bool autoSelectAllInvoked = false;

            string autoSelectKey =
                myPeer.Index + "|" +
                team.TeamIndex + "|" +
                mainAgent.Index + "|" +
                formationsWithUnits;
            if (formationsWithUnits > 1 &&
                selectedFormationCount == 0 &&
                !string.Equals(_lastAutoSelectedAllLocalCommanderFormationsKey, autoSelectKey, StringComparison.Ordinal))
            {
                afterInitializeInvoked = TryInvokeParameterlessMethod(dataSource, "AfterInitialize");
                updateTroopsInvoked = TryInvokeParameterlessMethod(troopController, "UpdateTroops") || updateTroopsInvoked;
                autoSelectAllInvoked = TryInvokeSingleBoolMethod(troopController, "SelectAllFormations", value: false);
                if (!autoSelectAllInvoked)
                {
                    try
                    {
                        playerOrderController.ClearSelectedFormations();
                        playerOrderController.SelectAllFormations(uiFeedback: false);
                        autoSelectAllInvoked = true;
                    }
                    catch
                    {
                        autoSelectAllInvoked = false;
                    }
                }
                if (autoSelectAllInvoked)
                    _lastAutoSelectedAllLocalCommanderFormationsKey = autoSelectKey;

                selectedFormationCount = playerOrderController.SelectedFormations?.Count ?? selectedFormationCount;
            }
            else if (formationsWithUnits > ownedFormationsWithUnits)
            {
                afterInitializeInvoked = TryInvokeParameterlessMethod(dataSource, "AfterInitialize");
                updateTroopsInvoked = TryInvokeParameterlessMethod(troopController, "UpdateTroops");
                selectedFormationCount = playerOrderController.SelectedFormations?.Count ?? selectedFormationCount;
            }

            if (afterInitializeInvoked ||
                updateTroopsInvoked ||
                autoSelectAllInvoked ||
                formationsWithUnits != ownedFormationsWithUnits ||
                selectedFormationCount == 0)
            {
                TryLogExactCommanderOrderVmState(
                    context: "Maintain",
                    myPeer,
                    mission,
                    team,
                    controlledAgent,
                    mainAgent,
                    playerOrderController,
                    dataSource,
                    troopController);
            }

            string logKey =
                myPeer.Index + "|" +
                team.TeamIndex + "|" +
                mainAgent.Index + "|" +
                formationsWithUnits + "|" +
                ownedFormationsWithUnits + "|" +
                selectedFormationCount + "|" +
                autoSelectAllInvoked;
            if (string.Equals(_lastMaintainedLocalCommanderOrderControlKey, logKey, StringComparison.Ordinal))
                return;

            _lastMaintainedLocalCommanderOrderControlKey = logKey;
            ModLogger.Info(
                "BattleMapSpawnHandoffPatch: maintained local exact-scene commander order control. " +
                "Peer=" + (myPeer.UserName ?? myPeer.Index.ToString()) +
                " TeamIndex=" + team.TeamIndex +
                " Side=" + team.Side +
                " ControlledAgentIndex=" + controlledAgent.Index +
                " AgentMainIndex=" + mainAgent.Index +
                " MissionPlayerTeamIndex=" + (mission.PlayerTeam?.TeamIndex.ToString() ?? "null") +
                " PlayerOrderOwnerIndex=" + (playerOrderController.Owner?.Index.ToString() ?? "null") +
                " AgentOrderOwnerIndex=" + (agentOrderController?.Owner?.Index.ToString() ?? "null") +
                " FormationsWithUnits=" + formationsWithUnits +
                " OwnedFormationsWithUnits=" + ownedFormationsWithUnits +
                " SelectedFormationCount=" + selectedFormationCount +
                " SingleplayerDataSourceRebuilt=" + rebuiltSingleplayerDataSource +
                " CampaignUiSemanticsForced=" + campaignUiSemanticsForced +
                " AfterInitializeInvoked=" + afterInitializeInvoked +
                " UpdateTroopsInvoked=" + updateTroopsInvoked +
                " AutoSelectAllInvoked=" + autoSelectAllInvoked +
                " Mission=" + (mission.SceneName ?? "null"));
        }

        private static bool TryEnsureExactCommanderSingleplayerOrderDataSource(
            NetworkCommunicator myPeer,
            Mission mission,
            Team team,
            Agent mainAgent,
            object orderUiHandler,
            OrderController playerOrderController,
            string entryId,
            ref object dataSource)
        {
            if (myPeer == null ||
                mission == null ||
                team == null ||
                mainAgent == null ||
                orderUiHandler == null ||
                playerOrderController == null)
            {
                return false;
            }

            object existingDataSource = dataSource ?? TryGetInstanceMemberValue(orderUiHandler, "_dataSource");
            if (existingDataSource == null)
                return false;

            int oldOrderSetCount = TryGetCollectionCount(TryGetInstanceMemberValue(existingDataSource, "OrderSets"));
            bool oldIsMultiplayer = TryGetInstanceBool(existingDataSource, "_isMultiplayer");
            if (!oldIsMultiplayer)
            {
                dataSource = existingDataSource;
                return false;
            }

            Type missionOrderVmType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.ViewModelCollection.Order.MissionOrderVM");
            ConstructorInfo missionOrderVmCtor = missionOrderVmType?.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(OrderController), typeof(bool), typeof(bool) },
                modifiers: null);
            if (missionOrderVmCtor == null)
                return false;

            object replacementDataSource;
            try
            {
                replacementDataSource = missionOrderVmCtor.Invoke(new object[] { playerOrderController, false, false });
            }
            catch
            {
                return false;
            }

            if (replacementDataSource == null)
                return false;

            TrySetInstanceMemberValue(replacementDataSource, "_isMultiplayer", false);
            bool callbacksConfigured = TryConfigureExactCommanderSingleplayerOrderDataSource(orderUiHandler, replacementDataSource);
            if (!callbacksConfigured)
                return false;

            TrySetInstanceMemberValue(orderUiHandler, "_dataSource", replacementDataSource);
            bool orderBarMovieForced = TryForceExactCommanderOrderBarMovie(orderUiHandler, replacementDataSource);
            if (!orderBarMovieForced)
            {
                TrySetInstanceMemberValue(orderUiHandler, "_dataSource", existingDataSource);
                return false;
            }

            object inputRestrictions = TryGetInstanceMemberValue(TryGetInstanceMemberValue(orderUiHandler, "_gauntletLayer"), "InputRestrictions");
            if (inputRestrictions != null)
                TrySetInstanceMemberValue(replacementDataSource, "InputRestrictions", inputRestrictions);

            TryInvokeParameterlessMethod(replacementDataSource, "AfterInitialize");
            object replacementTroopController = TryGetInstanceMemberValue(replacementDataSource, "TroopController");
            TryInvokeParameterlessMethod(replacementTroopController, "UpdateTroops");
            TryInvokeParameterlessMethod(existingDataSource, "OnFinalize");

            int newOrderSetCount = TryGetCollectionCount(TryGetInstanceMemberValue(replacementDataSource, "OrderSets"));
            bool newIsMultiplayer = TryGetInstanceBool(replacementDataSource, "_isMultiplayer");
            dataSource = replacementDataSource;

            string logKey =
                myPeer.Index + "|" +
                team.TeamIndex + "|" +
                mainAgent.Index + "|" +
                (entryId ?? "null") + "|" +
                oldOrderSetCount + "|" +
                newOrderSetCount;
            if (!string.Equals(_lastRebuiltSingleplayerCommanderOrderUiKey, logKey, StringComparison.Ordinal))
            {
                _lastRebuiltSingleplayerCommanderOrderUiKey = logKey;
                ModLogger.Info(
                    "BattleMapSpawnHandoffPatch: rebuilt local exact-scene commander order VM with exact commander callbacks. " +
                    "Peer=" + (myPeer.UserName ?? myPeer.Index.ToString()) +
                    " TeamIndex=" + team.TeamIndex +
                    " AgentMainIndex=" + mainAgent.Index +
                    " EntryId=" + (entryId ?? "null") +
                    " OldDataSourceIsMultiplayer=" + oldIsMultiplayer +
                    " NewDataSourceIsMultiplayer=" + newIsMultiplayer +
                    " OldOrderSetCount=" + oldOrderSetCount +
                    " NewOrderSetCount=" + newOrderSetCount +
                    " CallbacksConfigured=" + callbacksConfigured +
                    " OrderBarMovieForced=" + orderBarMovieForced +
                    " Mission=" + (mission.SceneName ?? "null"));
            }

            return true;
        }

        private static bool TryConfigureExactCommanderSingleplayerOrderDataSource(object orderUiHandler, object dataSource)
        {
            if (orderUiHandler == null || dataSource == null)
                return false;

            Type callbacksType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.ViewModelCollection.Order.MissionOrderCallbacks");
            object callbacks = null;
            try
            {
                callbacks = callbacksType != null ? Activator.CreateInstance(callbacksType) : null;
            }
            catch
            {
                callbacks = null;
            }

            if (callbacks == null)
                return false;

            bool callbacksConfigured =
                TrySetDelegateProperty(callbacks, "ToggleMissionInputs", orderUiHandler, "ToggleScreenRotation") &&
                TrySetDelegateProperty(callbacks, "RefreshVisuals", orderUiHandler, "RefreshVisuals") &&
                TrySetDelegateProperty(callbacks, "GetVisualOrderExecutionParameters", orderUiHandler, "GetVisualOrderExecutionParameters") &&
                TrySetDelegateProperty(callbacks, "SetSuspendTroopPlacer", orderUiHandler, "SetSuspendTroopPlacer") &&
                TrySetDelegateProperty(callbacks, "OnActivateToggleOrder", orderUiHandler, "OnActivateToggleOrder") &&
                TrySetDelegateProperty(callbacks, "OnDeactivateToggleOrder", orderUiHandler, "OnDeactivateToggleOrder") &&
                TrySetDelegateProperty(callbacks, "OnTransferTroopsFinished", orderUiHandler, "OnTransferFinished") &&
                TrySetDelegateProperty(callbacks, "OnBeforeOrder", orderUiHandler, "OnBeforeOrder");
            if (!callbacksConfigured)
                return false;

            if (!TryInvokeMethodSuccessfully(dataSource, "SetCallbacks", callbacks))
                return false;

            GameKeyContext missionOrderCategory = HotKeyManager.GetCategory("MissionOrderHotkeyCategory");
            GameKeyContext genericPanelCategory = HotKeyManager.GetCategory("GenericPanelGameKeyCategory");
            object missionScreen = TryGetInstanceMemberValue(orderUiHandler, "MissionScreen");
            object sceneLayer = TryGetInstanceMemberValue(missionScreen, "SceneLayer");
            object sceneInput = TryGetInstanceMemberValue(sceneLayer, "Input");
            object gauntletLayer = TryGetInstanceMemberValue(orderUiHandler, "_gauntletLayer");
            object gauntletInput = TryGetInstanceMemberValue(gauntletLayer, "Input");
            if (missionOrderCategory != null && sceneInput != null)
                TryInvokeMethod(sceneInput, "RegisterHotKeyCategory", missionOrderCategory);
            if (genericPanelCategory != null && gauntletInput != null)
                TryInvokeMethod(gauntletInput, "RegisterHotKeyCategory", genericPanelCategory);

            if (genericPanelCategory != null)
            {
                TryInvokeMethod(dataSource, "SetCancelInputKey", genericPanelCategory.GetHotKey("ToggleEscapeMenu"));
                object troopController = TryGetInstanceMemberValue(dataSource, "TroopController");
                TryInvokeMethod(troopController, "SetDoneInputKey", genericPanelCategory.GetHotKey("Confirm"));
                TryInvokeMethod(troopController, "SetCancelInputKey", genericPanelCategory.GetHotKey("Exit"));
                TryInvokeMethod(troopController, "SetResetInputKey", genericPanelCategory.GetHotKey("Reset"));
            }

            if (missionOrderCategory != null)
            {
                for (int orderIndex = 0; orderIndex < 9; orderIndex++)
                    TryInvokeMethod(dataSource, "SetOrderIndexKey", orderIndex, missionOrderCategory.GetGameKey(69 + orderIndex));

                TryInvokeMethod(dataSource, "SetReturnKey", missionOrderCategory.GetGameKey(77));
            }

            object combatCamera = TryGetInstanceMemberValue(missionScreen, "CombatCamera");
            TryInvokeMethod(dataSource, "SetDeploymentParemeters", combatCamera, new System.Collections.Generic.List<DeploymentPoint>());
            return true;
        }

        private static bool TryForceCampaignCommanderOrderUiSemantics(
            NetworkCommunicator myPeer,
            Mission mission,
            Team team,
            Agent mainAgent,
            object orderUiHandler,
            object dataSource,
            string entryId)
        {
            if (myPeer == null || mission == null || team == null || mainAgent == null || dataSource == null)
                return false;

            _activeExactCommanderMissionOrderVm = dataSource;
            bool wasMultiplayer = TryGetInstanceBool(dataSource, "_isMultiplayer");
            bool canUseShortcutsBefore = TryGetInstanceBool(dataSource, "CanUseShortcuts");
            bool updateCanUseShortcutsInvoked = TryInvokeSingleBoolMethod(dataSource, "UpdateCanUseShortcuts", value: true);
            bool troopSelectionInputsForced = TryForceExactCommanderTroopSelectionInputs(
                myPeer,
                mission,
                team,
                mainAgent,
                dataSource,
                entryId);
            bool orderBarMovieForced = TryForceExactCommanderOrderBarMovie(orderUiHandler, dataSource);
            bool isMultiplayerNow = TryGetInstanceBool(dataSource, "_isMultiplayer");
            bool canUseShortcutsNow = TryGetInstanceBool(dataSource, "CanUseShortcuts");
            bool changed =
                troopSelectionInputsForced ||
                orderBarMovieForced ||
                (updateCanUseShortcutsInvoked && canUseShortcutsBefore != canUseShortcutsNow);
            if (!changed)
                return false;

            string logKey =
                myPeer.Index + "|" +
                team.TeamIndex + "|" +
                mainAgent.Index + "|" +
                (entryId ?? "null") + "|" +
                isMultiplayerNow + "|" +
                canUseShortcutsNow + "|" +
                orderBarMovieForced;
            if (string.Equals(_lastForcedCampaignCommanderOrderUiKey, logKey, StringComparison.Ordinal))
                return true;

            _lastForcedCampaignCommanderOrderUiKey = logKey;
            ModLogger.Info(
                "BattleMapSpawnHandoffPatch: forced local exact-scene commander order UI toward campaign semantics. " +
                "Peer=" + (myPeer.UserName ?? myPeer.Index.ToString()) +
                " TeamIndex=" + team.TeamIndex +
                " AgentMainIndex=" + mainAgent.Index +
                " EntryId=" + (entryId ?? "null") +
                " DataSourceWasMultiplayer=" + wasMultiplayer +
                " DataSourceIsMultiplayer=" + isMultiplayerNow +
                " CanUseShortcutsBefore=" + canUseShortcutsBefore +
                " CanUseShortcutsNow=" + canUseShortcutsNow +
                " UpdateCanUseShortcutsInvoked=" + updateCanUseShortcutsInvoked +
                " TroopSelectionInputsForced=" + troopSelectionInputsForced +
                " OrderBarMovieForced=" + orderBarMovieForced +
                " Mission=" + (mission.SceneName ?? "null"));
            return true;
        }

        private static bool TryForceExactCommanderTroopSelectionInputs(
            NetworkCommunicator myPeer,
            Mission mission,
            Team team,
            Agent mainAgent,
            object dataSource,
            string entryId)
        {
            if (myPeer == null || mission == null || team == null || mainAgent == null || dataSource == null)
                return false;

            object troopController = TryGetInstanceMemberValue(dataSource, "TroopController");
            object troopList = TryGetInstanceMemberValue(troopController, "TroopList");
            if (!(troopList is IEnumerable enumerable))
                return false;

            int troopRowCount = 0;
            int rowsShowingInputs = 0;
            int rowsWithSelectionKeys = 0;
            bool changed = false;
            foreach (object troopItem in enumerable)
            {
                if (troopItem == null)
                    continue;

                troopRowCount++;
                bool showSelectionInputsBefore = TryGetInstanceBool(troopItem, "ShowSelectionInputs");
                object applySelectionKeyBefore = TryGetInstanceMemberValue(troopItem, "ApplySelectionKey");
                string applySelectionKeyIdBefore = TryGetInstanceValueText(applySelectionKeyBefore, "KeyID");

                TryInvokeParameterlessMethod(troopItem, "UpdateSelectionKeyInfo");
                TrySetInstanceMemberValue(troopItem, "ShowSelectionInputs", true);

                bool showSelectionInputsAfter = TryGetInstanceBool(troopItem, "ShowSelectionInputs");
                object applySelectionKeyAfter = TryGetInstanceMemberValue(troopItem, "ApplySelectionKey");
                string applySelectionKeyIdAfter = TryGetInstanceValueText(applySelectionKeyAfter, "KeyID");
                if (showSelectionInputsAfter)
                    rowsShowingInputs++;
                if (applySelectionKeyAfter != null)
                    rowsWithSelectionKeys++;

                if (showSelectionInputsBefore != showSelectionInputsAfter ||
                    !string.Equals(applySelectionKeyIdBefore, applySelectionKeyIdAfter, StringComparison.Ordinal))
                {
                    changed = true;
                }
            }

            string logKey =
                myPeer.Index + "|" +
                team.TeamIndex + "|" +
                mainAgent.Index + "|" +
                (entryId ?? "null") + "|" +
                troopRowCount + "|" +
                rowsShowingInputs + "|" +
                rowsWithSelectionKeys;
            if (!changed || string.Equals(_lastForcedExactCommanderTroopSelectionInputsKey, logKey, StringComparison.Ordinal))
                return changed;

            _lastForcedExactCommanderTroopSelectionInputsKey = logKey;
            ModLogger.Info(
                "BattleMapSpawnHandoffPatch: forced exact commander troop selection inputs visible. " +
                "Peer=" + (myPeer.UserName ?? myPeer.Index.ToString()) +
                " TeamIndex=" + team.TeamIndex +
                " AgentMainIndex=" + mainAgent.Index +
                " EntryId=" + (entryId ?? "null") +
                " TroopRowCount=" + troopRowCount +
                " RowsShowingInputs=" + rowsShowingInputs +
                " RowsWithSelectionKeys=" + rowsWithSelectionKeys +
                " Mission=" + (mission.SceneName ?? "null"));
            return true;
        }

        private static bool TryForceExactCommanderOrderBarMovie(object orderUiHandler, object dataSource)
        {
            if (orderUiHandler == null || dataSource == null)
                return false;

            object gauntletLayer = TryGetInstanceMemberValue(orderUiHandler, "_gauntletLayer");
            if (gauntletLayer == null)
                return false;

            string barOrderMovieName = TryGetInstanceMemberValue(orderUiHandler, "_barOrderMovieName") as string;
            if (string.IsNullOrWhiteSpace(barOrderMovieName))
                barOrderMovieName = "OrderBar";

            object currentMovie = TryGetInstanceMemberValue(orderUiHandler, "_movie");
            string currentBindingKey = BuildExactCommanderOrderBarMovieBindingKey(orderUiHandler, dataSource, currentMovie);
            if (currentMovie != null &&
                !string.IsNullOrWhiteSpace(currentBindingKey) &&
                string.Equals(_lastExactCommanderOrderBarMovieBindingKey, currentBindingKey, StringComparison.Ordinal))
            {
                return false;
            }

            bool releaseInvoked = false;
            if (currentMovie != null)
                releaseInvoked = TryInvokeSingleParameterMethod(gauntletLayer, "ReleaseMovie", currentMovie);

            object newMovie = TryInvokeTwoParameterMethod(gauntletLayer, "LoadMovie", barOrderMovieName, dataSource);
            if (newMovie == null)
            {
                if (currentMovie != null && releaseInvoked)
                {
                    object restoredMovie = TryInvokeTwoParameterMethod(
                        gauntletLayer,
                        "LoadMovie",
                        TryGetInstanceMemberValue(orderUiHandler, "_radialOrderMovieName") as string ?? "OrderRadial",
                        dataSource);
                    if (restoredMovie != null)
                    {
                        TrySetInstanceMemberValue(orderUiHandler, "_movie", restoredMovie);
                        _lastExactCommanderOrderBarMovieBindingKey =
                            BuildExactCommanderOrderBarMovieBindingKey(orderUiHandler, dataSource, restoredMovie);
                    }
                }

                return false;
            }

            TrySetInstanceMemberValue(orderUiHandler, "_movie", newMovie);
            _lastExactCommanderOrderBarMovieBindingKey =
                BuildExactCommanderOrderBarMovieBindingKey(orderUiHandler, dataSource, newMovie);
            return true;
        }

        private static string BuildExactCommanderOrderBarMovieBindingKey(object orderUiHandler, object dataSource, object movie)
        {
            if (orderUiHandler == null || dataSource == null || movie == null)
                return null;

            return
                RuntimeHelpers.GetHashCode(orderUiHandler) + "|" +
                RuntimeHelpers.GetHashCode(dataSource) + "|" +
                RuntimeHelpers.GetHashCode(movie);
        }

        private static void TryHandleExactCommanderOrderUiHotkeys(object orderUiHandler)
        {
            if (!GameNetwork.IsClient || !GameNetwork.IsSessionActive || orderUiHandler == null)
                return;

            NetworkCommunicator myPeer = GameNetwork.MyPeer;
            MissionPeer myMissionPeer = myPeer?.GetComponent<MissionPeer>();
            Agent controlledAgent = myMissionPeer?.ControlledAgent;
            Agent mainAgent = Agent.Main;
            Mission mission = Mission.Current ?? controlledAgent?.Mission ?? mainAgent?.Mission;
            Team team = myMissionPeer?.Team ?? controlledAgent?.Team ?? mainAgent?.Team;
            if (myPeer == null ||
                myPeer.IsServerPeer ||
                myMissionPeer == null ||
                controlledAgent == null ||
                !controlledAgent.IsActive() ||
                mainAgent == null ||
                !mainAgent.IsActive() ||
                mission == null ||
                team == null ||
                !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName) ||
                !SceneRuntimeClassifier.IsCampaignBattleScene(mission.SceneName ?? string.Empty) ||
                !team.IsPlayerGeneral ||
                !ReferenceEquals(team.GeneralAgent, controlledAgent))
            {
                return;
            }

            object dataSource = TryGetInstanceMemberValue(orderUiHandler, "_dataSource");
            OrderController playerOrderController = team.PlayerOrderController;
            if (dataSource == null || playerOrderController == null)
                return;

            TryEnsureExactCommanderSingleplayerOrderDataSource(
                myPeer,
                mission,
                team,
                mainAgent,
                orderUiHandler,
                playerOrderController,
                entryId: null,
                ref dataSource);

            bool backspacePressed = Input.IsKeyPressed(InputKey.BackSpace);
            int orderHotkeyIndex = GetPressedExactCommanderOrderHotkeyIndex();
            int formationHotkeyIndex = GetPressedExactCommanderFormationHotkeyIndex();
            if (_suppressExactCommanderOrderHotkeyFallbackUntilRelease)
            {
                if (AreAnyExactCommanderOrderHotkeysActive())
                {
                    if (orderHotkeyIndex >= 0 || backspacePressed)
                    {
                        string suppressedHotkey = backspacePressed ? "BackSpace" : ("F" + (orderHotkeyIndex + 1));
                        string suppressionLogKey =
                            myPeer.Index + "|" +
                            team.TeamIndex + "|" +
                            mainAgent.Index + "|" +
                            suppressedHotkey + "|" +
                            TryGetInstanceBool(dataSource, "IsToggleOrderShown");
                        if (!string.Equals(_lastExactCommanderOrderHotkeySuppressionKey, suppressionLogKey, StringComparison.Ordinal))
                        {
                            _lastExactCommanderOrderHotkeySuppressionKey = suppressionLogKey;
                            ModLogger.Info(
                                "BattleMapSpawnHandoffPatch: suppressed exact commander order hotkey fallback until key release. " +
                                "Hotkey=" + suppressedHotkey +
                                " TeamIndex=" + team.TeamIndex +
                                " AgentMainIndex=" + mainAgent.Index +
                                " IsToggleOrderShown=" + TryGetInstanceBool(dataSource, "IsToggleOrderShown") +
                                " SelectedFormationCount=" + (playerOrderController.SelectedFormations?.Count.ToString() ?? "null") +
                                " Mission=" + (mission.SceneName ?? "null"));
                        }
                    }

                    backspacePressed = false;
                    orderHotkeyIndex = -1;
                }
                else
                {
                    _suppressExactCommanderOrderHotkeyFallbackUntilRelease = false;
                    _lastExactCommanderOrderHotkeySuppressionKey = null;
                }
            }

            if (!backspacePressed && orderHotkeyIndex < 0 && formationHotkeyIndex < 0)
                return;

            int orderSetCountBefore = TryGetCollectionCount(TryGetInstanceMemberValue(dataSource, "OrderSets"));
            string selectedOrderSetBefore = TryGetInstanceValueText(TryGetInstanceMemberValue(dataSource, "SelectedOrderSet"), "OrderIconId");
            bool isToggleOrderShownBeforeAnyFallback = TryGetInstanceBool(dataSource, "IsToggleOrderShown");
            bool handled = false;
            string hotkey = backspacePressed ? "BackSpace" : ("F" + (orderHotkeyIndex + 1));
            bool autoSelectOrderSetAfterOpen = false;
            if (formationHotkeyIndex >= 0)
            {
                object troopController = TryGetInstanceMemberValue(dataSource, "TroopController");
                handled = TryInvokeMethodSuccessfully(troopController, "OnSelectFormationWithIndex", formationHotkeyIndex);
                if (!handled)
                {
                    handled = TryInvokeMethodSuccessfully(dataSource, "OnTroopFormationSelected", formationHotkeyIndex);
                }

                string formationHotkey = "D" + (formationHotkeyIndex + 1);
                string formationLogKey =
                    myPeer.Index + "|" +
                    team.TeamIndex + "|" +
                    mainAgent.Index + "|" +
                    formationHotkey + "|" +
                    handled + "|" +
                    (playerOrderController.SelectedFormations?.Count.ToString() ?? "null") + "|" +
                    TryGetInstanceBool(dataSource, "IsToggleOrderShown");
                if (!string.Equals(_lastExactCommanderFormationHotkeyFallbackKey, formationLogKey, StringComparison.Ordinal))
                {
                    _lastExactCommanderFormationHotkeyFallbackKey = formationLogKey;
                    ModLogger.Info(
                        "BattleMapSpawnHandoffPatch: exact commander formation hotkey fallback. " +
                        "Hotkey=" + formationHotkey +
                        " Handled=" + handled +
                        " TeamIndex=" + team.TeamIndex +
                        " AgentMainIndex=" + mainAgent.Index +
                        " IsToggleOrderShown=" + TryGetInstanceBool(dataSource, "IsToggleOrderShown") +
                        " SelectedFormationCount=" + (playerOrderController.SelectedFormations?.Count.ToString() ?? "null") +
                        " SelectedFormations=[" + BuildSelectedFormationSummary(playerOrderController) + "]" +
                        " Mission=" + (mission.SceneName ?? "null"));
                }

                return;
            }

            if (backspacePressed)
            {
                handled = TryInvokeParameterlessMethod(dataSource, "ViewOrders");
            }
            else if (orderHotkeyIndex >= 0)
            {
                bool isToggleOrderShownBefore = TryGetInstanceBool(dataSource, "IsToggleOrderShown");
                object selectedOrderSet = TryGetInstanceMemberValue(dataSource, "SelectedOrderSet");
                if (selectedOrderSet != null)
                {
                    object orders = TryGetInstanceMemberValue(selectedOrderSet, "Orders");
                    int selectedOrderCount = TryGetCollectionCount(orders);
                    if (selectedOrderCount > 0)
                    {
                        if (orderHotkeyIndex == 8 && OrderItemCollectionContainsReturnVisualOrder(orders))
                        {
                            handled = TryInvokeMethodSuccessfully(selectedOrderSet, "ExecuteDeSelect");
                        }
                        else if (orderHotkeyIndex < selectedOrderCount)
                        {
                            object orderItem = TryGetCollectionItem(orders, orderHotkeyIndex);
                            object visualOrder = TryGetInstanceMemberValue(orderItem, "Order");
                            if (IsReturnVisualOrderInstance(visualOrder))
                            {
                                bool? closeResult = TryInvokeBoolMethod(dataSource, "TryCloseToggleOrder", false);
                                handled = closeResult == true;
                            }
                            else
                            {
                                object visualOrderExecutionParameters = TryInvokeMethod(orderUiHandler, "GetVisualOrderExecutionParameters");
                                if (orderItem != null && visualOrderExecutionParameters != null)
                                {
                                    handled = TryInvokeMethodSuccessfully(orderItem, "ExecuteAction", visualOrderExecutionParameters);
                                    if (handled && !TryGetInstanceBool(dataSource, "IsHolding"))
                                        TryInvokeBoolMethod(dataSource, "TryCloseToggleOrder", false);
                                }
                            }
                        }
                    }
                }
                else
                {
                    bool openInvoked = isToggleOrderShownBefore || TryInvokeMethodSuccessfully(dataSource, "OpenToggleOrder", false, true);
                    bool isToggleOrderShownAfterOpen = TryGetInstanceBool(dataSource, "IsToggleOrderShown");
                    handled = openInvoked && isToggleOrderShownAfterOpen;
                    if (isToggleOrderShownAfterOpen)
                    {
                        autoSelectOrderSetAfterOpen = true;
                        if (orderHotkeyIndex == 8 && OrderSetCollectionContainsReturnOnlySet(TryGetInstanceMemberValue(dataSource, "OrderSets")))
                        {
                            bool? closeResult = TryInvokeBoolMethod(dataSource, "TryCloseToggleOrder", false);
                            handled = closeResult == true || handled;
                        }
                        else
                        {
                            object orderSetAtIndex = TryInvokeMethod(dataSource, "GetOrderSetAtIndex", orderHotkeyIndex);
                            if (orderSetAtIndex != null && !IsReturnOnlyOrderSet(orderSetAtIndex))
                            {
                                bool? selectResult = TryInvokeBoolMethod(dataSource, "TrySelectOrderSet", orderSetAtIndex);
                                handled = selectResult == true || handled;
                            }
                        }
                    }
                }
            }

            int orderSetCountAfter = TryGetCollectionCount(TryGetInstanceMemberValue(dataSource, "OrderSets"));
            string selectedOrderSetAfter = TryGetInstanceValueText(TryGetInstanceMemberValue(dataSource, "SelectedOrderSet"), "OrderIconId");
            string logKey =
                myPeer.Index + "|" +
                team.TeamIndex + "|" +
                mainAgent.Index + "|" +
                hotkey + "|" +
                handled + "|" +
                orderSetCountBefore + "|" +
                orderSetCountAfter + "|" +
                selectedOrderSetBefore + "|" +
                selectedOrderSetAfter + "|" +
                isToggleOrderShownBeforeAnyFallback + "|" +
                TryGetInstanceBool(dataSource, "IsToggleOrderShown");
            if (string.Equals(_lastExactCommanderOrderHotkeyFallbackKey, logKey, StringComparison.Ordinal))
                return;

            _lastExactCommanderOrderHotkeyFallbackKey = logKey;
            ModLogger.Info(
                "BattleMapSpawnHandoffPatch: exact commander order hotkey fallback. " +
                "Hotkey=" + hotkey +
                " Handled=" + handled +
                " TeamIndex=" + team.TeamIndex +
                " AgentMainIndex=" + mainAgent.Index +
                " OrderSetCountBefore=" + orderSetCountBefore +
                " OrderSetCountAfter=" + orderSetCountAfter +
                " SelectedOrderSetBefore=" + selectedOrderSetBefore +
                " SelectedOrderSetAfter=" + selectedOrderSetAfter +
                " AutoSelectOrderSetAfterOpen=" + autoSelectOrderSetAfterOpen +
                " WasToggleOrderShownBefore=" + isToggleOrderShownBeforeAnyFallback +
                " IsToggleOrderShown=" + TryGetInstanceBool(dataSource, "IsToggleOrderShown") +
                " SelectedFormationCount=" + (playerOrderController.SelectedFormations?.Count.ToString() ?? "null") +
                " Mission=" + (mission.SceneName ?? "null"));
        }

        private static void TryLogExactCommanderOrderVmState(
            string context,
            NetworkCommunicator myPeer,
            Mission mission,
            Team team,
            Agent controlledAgent,
            Agent mainAgent,
            OrderController playerOrderController,
            object dataSource,
            object troopController)
        {
            if (string.IsNullOrWhiteSpace(context) ||
                myPeer == null ||
                mission == null ||
                team == null ||
                controlledAgent == null ||
                mainAgent == null ||
                playerOrderController == null ||
                dataSource == null ||
                troopController == null)
            {
                return;
            }

            object troopList = TryGetInstanceMemberValue(troopController, "TroopList");
            int troopListCount = TryGetCollectionCount(troopList);
            int orderSetCount = TryGetCollectionCount(TryGetInstanceMemberValue(dataSource, "OrderSets"));
            bool isMultiplayer = TryGetInstanceBool(dataSource, "_isMultiplayer");
            bool isToggleOrderShown = TryGetInstanceBool(dataSource, "IsToggleOrderShown");
            bool isTroopListShown = TryGetInstanceBool(dataSource, "IsTroopListShown");
            bool canUseShortcuts = TryGetInstanceBool(dataSource, "CanUseShortcuts");
            string playerHasAnyTroopUnderThem = TryGetInstanceValueText(dataSource, "PlayerHasAnyTroopUnderThem");
            string selectedFormationSummary = BuildSelectedFormationSummary(playerOrderController);
            string formationStateSummary = BuildFormationStateSummary(team);
            string troopRowSummary = BuildTroopRowSummary(troopList);

            string logKey =
                context + "|" +
                myPeer.Index + "|" +
                team.TeamIndex + "|" +
                controlledAgent.Index + "|" +
                mainAgent.Index + "|" +
                orderSetCount + "|" +
                troopListCount + "|" +
                isMultiplayer + "|" +
                playerHasAnyTroopUnderThem + "|" +
                selectedFormationSummary + "|" +
                formationStateSummary + "|" +
                troopRowSummary;
            if (string.Equals(_lastExactCommanderOrderVmStateKey, logKey, StringComparison.Ordinal))
                return;

            _lastExactCommanderOrderVmStateKey = logKey;
            ModLogger.Info(
                "BattleMapSpawnHandoffPatch: exact commander order VM state. " +
                "Context=" + context +
                " Peer=" + (myPeer.UserName ?? myPeer.Index.ToString()) +
                " TeamIndex=" + team.TeamIndex +
                " Side=" + team.Side +
                " ControlledAgentIndex=" + controlledAgent.Index +
                " AgentMainIndex=" + mainAgent.Index +
                " MissionPlayerTeamIndex=" + (mission.PlayerTeam?.TeamIndex.ToString() ?? "null") +
                " TeamIsPlayerGeneral=" + team.IsPlayerGeneral +
                " TeamIsPlayerSergeant=" + team.IsPlayerSergeant +
                " TeamHasBots=" + team.HasBots +
                " DataSourceIsMultiplayer=" + isMultiplayer +
                " OrderSetCount=" + orderSetCount +
                " DataSourceToggleShown=" + isToggleOrderShown +
                " DataSourceTroopListShown=" + isTroopListShown +
                " DataSourceCanUseShortcuts=" + canUseShortcuts +
                " PlayerHasAnyTroopUnderThem=" + playerHasAnyTroopUnderThem +
                " TroopListCount=" + troopListCount +
                " SelectedFormations=[" + selectedFormationSummary + "]" +
                " FormationStates=[" + formationStateSummary + "]" +
                " TroopRows=[" + troopRowSummary + "]" +
                " Mission=" + (mission.SceneName ?? "null"));
        }

        private static void TryLogExactCommanderOrderMenuInteraction(
            string context,
            object missionOrderVm,
            bool applySelectedOrders,
            bool? closeResult)
        {
            if (string.IsNullOrWhiteSpace(context) || missionOrderVm == null)
                return;

            Mission mission = Mission.Current;
            Team playerTeam = mission?.PlayerTeam;
            Agent mainAgent = Agent.Main;
            if (mission == null ||
                playerTeam == null ||
                mainAgent == null ||
                !mainAgent.IsActive() ||
                !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName) ||
                !SceneRuntimeClassifier.IsCampaignBattleScene(mission.SceneName ?? string.Empty) ||
                !playerTeam.IsPlayerGeneral)
            {
                return;
            }

            OrderController playerOrderController = playerTeam.PlayerOrderController;
            int orderSetCount = TryGetCollectionCount(TryGetInstanceMemberValue(missionOrderVm, "OrderSets"));
            int troopListCount = TryGetCollectionCount(TryGetInstanceMemberValue(TryGetInstanceMemberValue(missionOrderVm, "TroopController"), "TroopList"));
            string selectedOrderSet = TryGetInstanceValueText(TryGetInstanceMemberValue(missionOrderVm, "SelectedOrderSet"), "OrderIconId");
            bool isToggleOrderShown = TryGetInstanceBool(missionOrderVm, "IsToggleOrderShown");
            bool isTroopListShown = TryGetInstanceBool(missionOrderVm, "IsTroopListShown");
            bool canUseShortcuts = TryGetInstanceBool(missionOrderVm, "CanUseShortcuts");
            string playerHasAnyTroopUnderThem = TryGetInstanceValueText(missionOrderVm, "PlayerHasAnyTroopUnderThem");

            ModLogger.Info(
                "BattleMapSpawnHandoffPatch: exact commander order menu interaction. " +
                "Context=" + context +
                " ApplySelectedOrders=" + applySelectedOrders +
                " CloseResult=" + (closeResult.HasValue ? closeResult.Value.ToString() : "null") +
                " TeamIndex=" + playerTeam.TeamIndex +
                " AgentMainIndex=" + mainAgent.Index +
                " SelectedFormationCount=" + (playerOrderController?.SelectedFormations?.Count.ToString() ?? "null") +
                " OrderSetCount=" + orderSetCount +
                " TroopListCount=" + troopListCount +
                " SelectedOrderSetIcon=" + selectedOrderSet +
                " IsToggleOrderShown=" + isToggleOrderShown +
                " IsTroopListShown=" + isTroopListShown +
                " CanUseShortcuts=" + canUseShortcuts +
                " PlayerHasAnyTroopUnderThem=" + playerHasAnyTroopUnderThem +
                " SelectedFormations=[" + BuildSelectedFormationSummary(playerOrderController) + "]" +
                " Mission=" + (mission.SceneName ?? "null"));
        }

        private static void TryLogExactCommanderOrderItemExecution(object missionOrderVm, object orderItem)
        {
            if (orderItem == null)
                return;

            Mission mission = Mission.Current;
            Team playerTeam = mission?.PlayerTeam;
            Agent mainAgent = Agent.Main;
            if (mission == null ||
                playerTeam == null ||
                mainAgent == null ||
                !mainAgent.IsActive() ||
                !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName) ||
                !SceneRuntimeClassifier.IsCampaignBattleScene(mission.SceneName ?? string.Empty) ||
                !playerTeam.IsPlayerGeneral)
            {
                return;
            }

            OrderController playerOrderController = playerTeam.PlayerOrderController;
            string logKey =
                playerTeam.TeamIndex + "|" +
                mainAgent.Index + "|" +
                (orderItem.GetType().FullName ?? orderItem.GetType().Name) + "|" +
                TryGetInstanceValueText(orderItem, "OrderIconId") + "|" +
                (playerOrderController?.SelectedFormations?.Count.ToString() ?? "null") + "|" +
                TryGetInstanceBool(missionOrderVm, "IsToggleOrderShown");
            if (string.Equals(_lastExactCommanderOrderItemExecutionKey, logKey, StringComparison.Ordinal))
                return;

            _lastExactCommanderOrderItemExecutionKey = logKey;
            ModLogger.Info(
                "BattleMapSpawnHandoffPatch: exact commander order item execute. " +
                "TeamIndex=" + playerTeam.TeamIndex +
                " AgentMainIndex=" + mainAgent.Index +
                " OrderItemType=" + (orderItem.GetType().FullName ?? orderItem.GetType().Name) +
                " OrderIconId=" + TryGetInstanceValueText(orderItem, "OrderIconId") +
                " VmKnown=" + (missionOrderVm != null) +
                " VmToggleShown=" + TryGetInstanceBool(missionOrderVm, "IsToggleOrderShown") +
                " VmSelectedOrderSet=" + TryGetInstanceValueText(TryGetInstanceMemberValue(missionOrderVm, "SelectedOrderSet"), "OrderIconId") +
                " SelectedFormationCount=" + (playerOrderController?.SelectedFormations?.Count.ToString() ?? "null") +
                " SelectedFormations=[" + BuildSelectedFormationSummary(playerOrderController) + "]" +
                " Mission=" + (mission.SceneName ?? "null"));
        }

        private static void TryDisableExactCommanderClientFormationUpdatesAfterSetOrder(
            NetworkCommunicator myPeer,
            Mission mission,
            Team team,
            Agent mainAgent,
            OrderController playerOrderController,
            OrderController agentOrderController,
            string entryId)
        {
            if (myPeer == null ||
                mission == null ||
                team == null ||
                mainAgent == null ||
                playerOrderController == null ||
                !GameNetwork.IsClient ||
                !GameNetwork.IsSessionActive ||
                myPeer.IsServerPeer ||
                !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName) ||
                !SceneRuntimeClassifier.IsCampaignBattleScene(mission.SceneName ?? string.Empty) ||
                !team.IsPlayerGeneral)
            {
                return;
            }

            bool playerControllerUpdated = false;
            bool agentControllerUpdated = false;
            try
            {
                playerOrderController.SetFormationUpdateEnabledAfterSetOrder(value: false);
                playerControllerUpdated = true;
            }
            catch
            {
                playerControllerUpdated = false;
            }

            if (agentOrderController != null)
            {
                try
                {
                    agentOrderController.SetFormationUpdateEnabledAfterSetOrder(value: false);
                    agentControllerUpdated = true;
                }
                catch
                {
                    agentControllerUpdated = false;
                }
            }

            string logKey =
                myPeer.Index + "|" +
                team.TeamIndex + "|" +
                mainAgent.Index + "|" +
                playerControllerUpdated + "|" +
                agentControllerUpdated + "|" +
                (entryId ?? "null");
            if (string.Equals(_lastExactCommanderDisabledAfterSetOrderUpdatesKey, logKey, StringComparison.Ordinal))
                return;

            _lastExactCommanderDisabledAfterSetOrderUpdatesKey = logKey;
            ModLogger.Info(
                "BattleMapSpawnHandoffPatch: disabled exact commander client formation updates after set order. " +
                "Peer=" + (myPeer.UserName ?? myPeer.Index.ToString()) +
                " TeamIndex=" + team.TeamIndex +
                " AgentMainIndex=" + mainAgent.Index +
                " EntryId=" + (entryId ?? "null") +
                " PlayerOrderControllerUpdated=" + playerControllerUpdated +
                " AgentOrderControllerUpdated=" + agentControllerUpdated +
                " Mission=" + (mission.SceneName ?? "null"));
        }

        private static void TrySuppressExactCommanderOrderHotkeysUntilRelease(object missionOrderVm, bool closeResult, string trigger)
        {
            if (!closeResult || missionOrderVm == null)
                return;

            Mission mission = Mission.Current;
            Team playerTeam = mission?.PlayerTeam;
            Agent mainAgent = Agent.Main;
            if (mission == null ||
                playerTeam == null ||
                mainAgent == null ||
                !mainAgent.IsActive() ||
                !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName) ||
                !SceneRuntimeClassifier.IsCampaignBattleScene(mission.SceneName ?? string.Empty) ||
                !playerTeam.IsPlayerGeneral)
            {
                return;
            }

            _suppressExactCommanderOrderHotkeyFallbackUntilRelease = true;
            string logKey =
                playerTeam.TeamIndex + "|" +
                mainAgent.Index + "|" +
                (trigger ?? "null") + "|" +
                TryGetInstanceBool(missionOrderVm, "IsToggleOrderShown") + "|" +
                TryGetInstanceValueText(TryGetInstanceMemberValue(missionOrderVm, "SelectedOrderSet"), "OrderIconId");
            if (string.Equals(_lastExactCommanderOrderHotkeySuppressionKey, logKey, StringComparison.Ordinal))
                return;

            _lastExactCommanderOrderHotkeySuppressionKey = logKey;
            ModLogger.Info(
                "BattleMapSpawnHandoffPatch: armed exact commander order hotkey suppression until key release. " +
                "Trigger=" + (trigger ?? "null") +
                " TeamIndex=" + playerTeam.TeamIndex +
                " AgentMainIndex=" + mainAgent.Index +
                " IsToggleOrderShown=" + TryGetInstanceBool(missionOrderVm, "IsToggleOrderShown") +
                " SelectedOrderSet=" + TryGetInstanceValueText(TryGetInstanceMemberValue(missionOrderVm, "SelectedOrderSet"), "OrderIconId") +
                " Mission=" + (mission.SceneName ?? "null"));
        }

        private static void TryForceImmediateExactCommanderOrderMenuClose(object missionOrderVm, object orderItem, string trigger)
        {
            if (missionOrderVm == null || orderItem == null)
                return;

            if (!TryGetInstanceBool(missionOrderVm, "_isMultiplayer"))
                return;

            Mission mission = Mission.Current;
            Team playerTeam = mission?.PlayerTeam;
            Agent mainAgent = Agent.Main;
            if (mission == null ||
                playerTeam == null ||
                mainAgent == null ||
                !mainAgent.IsActive() ||
                !MissionMultiplayerCoopBattleMode.IsBattleMapSceneName(mission.SceneName) ||
                !SceneRuntimeClassifier.IsCampaignBattleScene(mission.SceneName ?? string.Empty) ||
                !playerTeam.IsPlayerGeneral ||
                !TryGetInstanceBool(missionOrderVm, "IsToggleOrderShown"))
            {
                return;
            }

            object visualOrder = TryGetInstanceMemberValue(orderItem, "Order");
            if (visualOrder != null && string.Equals(visualOrder.GetType().Name, "ReturnVisualOrder", StringComparison.Ordinal))
                return;

            bool isHolding = TryGetInstanceBool(missionOrderVm, "IsHolding");
            bool? closeResult = null;
            if (!isHolding)
            {
                closeResult = TryInvokeBoolMethod(missionOrderVm, "TryCloseToggleOrder", false);
                if (closeResult != true)
                {
                    object selectedOrderSet = TryGetInstanceMemberValue(missionOrderVm, "SelectedOrderSet");
                    if (selectedOrderSet != null)
                        TryInvokeMethodSuccessfully(selectedOrderSet, "ExecuteDeSelect");
                }
            }

            ModLogger.Info(
                "BattleMapSpawnHandoffPatch: forced immediate exact commander order menu close. " +
                "Trigger=" + (trigger ?? "null") +
                " TeamIndex=" + playerTeam.TeamIndex +
                " AgentMainIndex=" + mainAgent.Index +
                " OrderItemType=" + (orderItem.GetType().FullName ?? orderItem.GetType().Name) +
                " VisualOrderType=" + (visualOrder?.GetType().FullName ?? "null") +
                " WasHolding=" + isHolding +
                " CloseResult=" + (closeResult.HasValue ? closeResult.Value.ToString() : "null") +
                " IsToggleOrderShownNow=" + TryGetInstanceBool(missionOrderVm, "IsToggleOrderShown") +
                " SelectedOrderSetNow=" + TryGetInstanceValueText(TryGetInstanceMemberValue(missionOrderVm, "SelectedOrderSet"), "OrderIconId") +
                " Mission=" + (mission.SceneName ?? "null"));
        }

        private static string BuildSelectedFormationSummary(OrderController orderController)
        {
            if (orderController?.SelectedFormations == null || orderController.SelectedFormations.Count == 0)
                return "none";

            StringBuilder summary = new StringBuilder();
            foreach (Formation formation in orderController.SelectedFormations)
            {
                if (summary.Length > 0)
                    summary.Append("; ");

                if (formation == null)
                {
                    summary.Append("null");
                    continue;
                }

                summary
                    .Append("Idx=").Append(formation.FormationIndex)
                    .Append(" Class=").Append(formation.PhysicalClass)
                    .Append(" Units=").Append(formation.CountOfUnits);
            }

            return summary.ToString();
        }

        private static bool IsReturnVisualOrderInstance(object visualOrder)
        {
            return visualOrder != null &&
                   string.Equals(visualOrder.GetType().Name, "ReturnVisualOrder", StringComparison.Ordinal);
        }

        private static bool OrderItemCollectionContainsReturnVisualOrder(object orderItems)
        {
            if (!(orderItems is IEnumerable enumerable))
                return false;

            foreach (object orderItem in enumerable)
            {
                object visualOrder = TryGetInstanceMemberValue(orderItem, "Order");
                if (IsReturnVisualOrderInstance(visualOrder))
                    return true;
            }

            return false;
        }

        private static bool IsReturnOnlyOrderSet(object orderSet)
        {
            if (orderSet == null || !TryGetInstanceBool(orderSet, "HasSingleOrder"))
                return false;

            object orders = TryGetInstanceMemberValue(orderSet, "Orders");
            object firstOrderItem = TryGetCollectionItem(orders, 0);
            object visualOrder = TryGetInstanceMemberValue(firstOrderItem, "Order");
            return IsReturnVisualOrderInstance(visualOrder);
        }

        private static bool OrderSetCollectionContainsReturnOnlySet(object orderSets)
        {
            if (!(orderSets is IEnumerable enumerable))
                return false;

            foreach (object orderSet in enumerable)
            {
                if (IsReturnOnlyOrderSet(orderSet))
                    return true;
            }

            return false;
        }

        private static string BuildFormationStateSummary(Team team)
        {
            if (team == null)
                return "none";

            StringBuilder summary = new StringBuilder();
            foreach (Formation formation in team.FormationsIncludingEmpty)
            {
                if (formation == null || !ReferenceEquals(formation.Team, team))
                    continue;

                Agent playerOwner = TryGetInstanceMemberValue(formation, "PlayerOwner") as Agent;
                Agent captain = formation.Captain;
                bool shouldInclude =
                    formation.CountOfUnits > 0 ||
                    playerOwner != null ||
                    captain != null ||
                    formation.IsPlayerTroopInFormation;
                if (!shouldInclude)
                    continue;

                if (summary.Length > 0)
                    summary.Append("; ");

                summary
                    .Append("Idx=").Append(formation.FormationIndex)
                    .Append(" Class=").Append(formation.PhysicalClass)
                    .Append(" Units=").Append(formation.CountOfUnits)
                    .Append(" PlayerOwner=").Append(playerOwner?.Index.ToString() ?? "null")
                    .Append(" Captain=").Append(captain?.Index.ToString() ?? "null")
                    .Append(" IsPlayerTroop=").Append(formation.IsPlayerTroopInFormation)
                    .Append(" HasPlayerControlled=").Append(TryGetInstanceValueText(formation, "HasPlayerControlledTroop"));
            }

            return summary.Length > 0 ? summary.ToString() : "none";
        }

        private static string BuildTroopRowSummary(object troopList)
        {
            if (!(troopList is IEnumerable enumerable))
                return "none";

            StringBuilder summary = new StringBuilder();
            int index = 0;
            foreach (object troopItem in enumerable)
            {
                if (troopItem == null)
                    continue;

                if (index >= 12)
                {
                    summary.Append("; ...");
                    break;
                }

                if (summary.Length > 0)
                    summary.Append("; ");

                object formationObject = TryGetInstanceMemberValue(troopItem, "Formation");
                Formation formation = formationObject as Formation;
                object applySelectionKey = TryGetInstanceMemberValue(troopItem, "ApplySelectionKey");
                summary
                    .Append("Idx=").Append(TryGetInstanceInt(troopItem, "FormationIndex"))
                    .Append(" Name=").Append(TryGetInstanceValueText(troopItem, "FormationName"))
                    .Append(" Members=").Append(TryGetInstanceInt(troopItem, "CurrentMemberCount"))
                    .Append(" Selected=").Append(TryGetInstanceBool(troopItem, "IsSelected"))
                    .Append(" Selectable=").Append(TryGetInstanceBool(troopItem, "IsSelectable"))
                    .Append(" Highlight=").Append(TryGetInstanceBool(troopItem, "IsSelectionHighlightActive"))
                    .Append(" ShowInputs=").Append(TryGetInstanceBool(troopItem, "ShowSelectionInputs"))
                    .Append(" ApplyKeyId=").Append(TryGetInstanceValueText(applySelectionKey, "KeyID"))
                    .Append(" ApplyKeyVisible=").Append(TryGetInstanceBool(applySelectionKey, "IsVisible"))
                    .Append(" FormationUnits=").Append(formation?.CountOfUnits.ToString() ?? "null");
                index++;
            }

            return summary.Length > 0 ? summary.ToString() : "none";
        }

        private static int TryGetCollectionCount(object collection)
        {
            if (collection == null)
                return 0;

            if (collection is ICollection nonGenericCollection)
                return nonGenericCollection.Count;

            object countValue = TryGetInstanceMemberValue(collection, "Count");
            if (countValue is int intCount)
                return intCount;

            if (!(collection is IEnumerable enumerable))
                return 0;

            int count = 0;
            foreach (object _ in enumerable)
                count++;

            return count;
        }

        private static object TryGetCollectionItem(object collection, int index)
        {
            if (collection == null || index < 0)
                return null;

            if (collection is IList list)
                return index < list.Count ? list[index] : null;

            if (!(collection is IEnumerable enumerable))
                return null;

            int currentIndex = 0;
            foreach (object item in enumerable)
            {
                if (currentIndex == index)
                    return item;

                currentIndex++;
            }

            return null;
        }

        private static int TryGetInstanceInt(object instance, string memberName)
        {
            object value = TryGetInstanceMemberValue(instance, memberName);
            if (value is int intValue)
                return intValue;

            if (value is Enum enumValue)
                return Convert.ToInt32(enumValue);

            return -1;
        }

        private static string TryGetInstanceValueText(object instance, string memberName)
        {
            object value = TryGetInstanceMemberValue(instance, memberName);
            return value?.ToString() ?? "null";
        }

        private static void TrySetInstanceMemberValue(object instance, string memberName, object value)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
                return;

            for (Type instanceType = instance.GetType(); instanceType != null; instanceType = instanceType.BaseType)
            {
                PropertyInfo property = instanceType.GetProperty(
                    memberName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.CanWrite && property.GetIndexParameters().Length == 0)
                {
                    try
                    {
                        property.SetValue(instance, value, null);
                        return;
                    }
                    catch
                    {
                    }
                }

                FieldInfo field = instanceType.GetField(
                    memberName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    try
                    {
                        field.SetValue(instance, value);
                        return;
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static object TryGetInstanceMemberValue(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
                return null;

            for (Type instanceType = instance.GetType(); instanceType != null; instanceType = instanceType.BaseType)
            {
                PropertyInfo property = instanceType.GetProperty(
                    memberName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.GetIndexParameters().Length == 0)
                {
                    try
                    {
                        return property.GetValue(instance, null);
                    }
                    catch
                    {
                    }
                }

                FieldInfo field = instanceType.GetField(
                    memberName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    try
                    {
                        return field.GetValue(instance);
                    }
                    catch
                    {
                    }
                }
            }

            return null;
        }

        private static bool TryGetInstanceBool(object instance, string memberName)
        {
            object value = TryGetInstanceMemberValue(instance, memberName);
            return value is bool boolValue && boolValue;
        }

        private static bool TrySetDelegateProperty(object instance, string propertyName, object target, string methodName)
        {
            if (instance == null ||
                target == null ||
                string.IsNullOrWhiteSpace(propertyName) ||
                string.IsNullOrWhiteSpace(methodName))
            {
                return false;
            }

            PropertyInfo property = null;
            FieldInfo field = null;
            for (Type instanceType = instance.GetType(); instanceType != null && property == null && field == null; instanceType = instanceType.BaseType)
            {
                property = instanceType.GetProperty(
                    propertyName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property == null)
                {
                    field = instanceType.GetField(
                        propertyName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
            }

            Type delegateType = property?.PropertyType ?? field?.FieldType;
            if (delegateType == null || !typeof(Delegate).IsAssignableFrom(delegateType))
                return false;

            MethodInfo invokeMethod = delegateType.GetMethod("Invoke");
            Type[] parameterTypes = invokeMethod?.GetParameters() != null
                ? Array.ConvertAll(invokeMethod.GetParameters(), parameter => parameter.ParameterType)
                : Type.EmptyTypes;
            MethodInfo targetMethod = FindInstanceMethod(target.GetType(), methodName, parameterTypes);
            if (targetMethod == null)
                return false;

            try
            {
                Delegate callback = Delegate.CreateDelegate(delegateType, target, targetMethod);
                if (property != null)
                    property.SetValue(instance, callback, null);
                else
                    field.SetValue(instance, callback);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryInvokeParameterlessMethod(object instance, string methodName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(methodName))
                return false;

            for (Type instanceType = instance.GetType(); instanceType != null; instanceType = instanceType.BaseType)
            {
                MethodInfo method = instanceType.GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: Type.EmptyTypes,
                    modifiers: null);
                if (method == null)
                    continue;

                try
                {
                    method.Invoke(instance, null);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private static object TryInvokeMethod(object instance, string methodName, params object[] arguments)
        {
            if (instance == null || string.IsNullOrWhiteSpace(methodName))
                return null;

            object[] actualArguments = arguments ?? Array.Empty<object>();
            for (Type instanceType = instance.GetType(); instanceType != null; instanceType = instanceType.BaseType)
            {
                foreach (MethodInfo method in instanceType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                        continue;

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length != actualArguments.Length)
                        continue;

                    bool parametersMatch = true;
                    for (int parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
                    {
                        object argument = actualArguments[parameterIndex];
                        Type parameterType = parameters[parameterIndex].ParameterType;
                        if (argument == null)
                        {
                            if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) == null)
                            {
                                parametersMatch = false;
                                break;
                            }

                            continue;
                        }

                        if (!parameterType.IsInstanceOfType(argument) && parameterType != argument.GetType())
                        {
                            parametersMatch = false;
                            break;
                        }
                    }

                    if (!parametersMatch)
                        continue;

                    try
                    {
                        return method.Invoke(instance, actualArguments);
                    }
                    catch
                    {
                        return null;
                    }
                }
            }

            return null;
        }

        private static bool TryInvokeMethodSuccessfully(object instance, string methodName, params object[] arguments)
        {
            if (instance == null || string.IsNullOrWhiteSpace(methodName))
                return false;

            object[] actualArguments = arguments ?? Array.Empty<object>();
            for (Type instanceType = instance.GetType(); instanceType != null; instanceType = instanceType.BaseType)
            {
                foreach (MethodInfo method in instanceType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                        continue;

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length != actualArguments.Length)
                        continue;

                    bool parametersMatch = true;
                    for (int parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
                    {
                        object argument = actualArguments[parameterIndex];
                        Type parameterType = parameters[parameterIndex].ParameterType;
                        if (argument == null)
                        {
                            if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) == null)
                            {
                                parametersMatch = false;
                                break;
                            }

                            continue;
                        }

                        if (!parameterType.IsInstanceOfType(argument) && parameterType != argument.GetType())
                        {
                            parametersMatch = false;
                            break;
                        }
                    }

                    if (!parametersMatch)
                        continue;

                    try
                    {
                        method.Invoke(instance, actualArguments);
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }

            return false;
        }

        private static bool? TryInvokeBoolMethod(object instance, string methodName, params object[] arguments)
        {
            if (instance == null || string.IsNullOrWhiteSpace(methodName))
                return null;

            object[] actualArguments = arguments ?? Array.Empty<object>();
            for (Type instanceType = instance.GetType(); instanceType != null; instanceType = instanceType.BaseType)
            {
                foreach (MethodInfo method in instanceType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                        continue;

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length != actualArguments.Length)
                        continue;

                    bool parametersMatch = true;
                    for (int parameterIndex = 0; parameterIndex < parameters.Length; parameterIndex++)
                    {
                        object argument = actualArguments[parameterIndex];
                        Type parameterType = parameters[parameterIndex].ParameterType;
                        if (argument == null)
                        {
                            if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) == null)
                            {
                                parametersMatch = false;
                                break;
                            }

                            continue;
                        }

                        if (!parameterType.IsInstanceOfType(argument) && parameterType != argument.GetType())
                        {
                            parametersMatch = false;
                            break;
                        }
                    }

                    if (!parametersMatch || method.ReturnType != typeof(bool))
                        continue;

                    try
                    {
                        return (bool)method.Invoke(instance, actualArguments);
                    }
                    catch
                    {
                        return null;
                    }
                }
            }

            return null;
        }

        private static bool TryInvokeSingleBoolMethod(object instance, string methodName, bool value)
        {
            if (instance == null || string.IsNullOrWhiteSpace(methodName))
                return false;

            for (Type instanceType = instance.GetType(); instanceType != null; instanceType = instanceType.BaseType)
            {
                MethodInfo method = instanceType.GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: new[] { typeof(bool) },
                    modifiers: null);
                if (method == null)
                    continue;

                try
                {
                    method.Invoke(instance, new object[] { value });
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private static bool TryInvokeSingleParameterMethod(object instance, string methodName, object argument)
        {
            if (instance == null || string.IsNullOrWhiteSpace(methodName))
                return false;

            for (Type instanceType = instance.GetType(); instanceType != null; instanceType = instanceType.BaseType)
            {
                foreach (MethodInfo method in instanceType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                        continue;

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length != 1)
                        continue;

                    if (argument != null && !parameters[0].ParameterType.IsInstanceOfType(argument))
                        continue;

                    try
                    {
                        method.Invoke(instance, new[] { argument });
                        return true;
                    }
                    catch
                    {
                        return false;
                    }
                }
            }

            return false;
        }

        private static object TryInvokeTwoParameterMethod(object instance, string methodName, object firstArgument, object secondArgument)
        {
            if (instance == null || string.IsNullOrWhiteSpace(methodName))
                return null;

            for (Type instanceType = instance.GetType(); instanceType != null; instanceType = instanceType.BaseType)
            {
                foreach (MethodInfo method in instanceType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                        continue;

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length != 2)
                        continue;

                    bool firstMatches =
                        firstArgument == null ||
                        parameters[0].ParameterType.IsInstanceOfType(firstArgument) ||
                        parameters[0].ParameterType == typeof(string);
                    bool secondMatches =
                        secondArgument == null ||
                        parameters[1].ParameterType.IsInstanceOfType(secondArgument);
                    if (!firstMatches || !secondMatches)
                        continue;

                    try
                    {
                        return method.Invoke(instance, new[] { firstArgument, secondArgument });
                    }
                    catch
                    {
                        return null;
                    }
                }
            }

            return null;
        }

        private static MethodInfo FindInstanceMethod(Type instanceType, string methodName, Type[] parameterTypes)
        {
            if (instanceType == null || string.IsNullOrWhiteSpace(methodName))
                return null;

            Type[] actualParameterTypes = parameterTypes ?? Type.EmptyTypes;
            for (Type currentType = instanceType; currentType != null; currentType = currentType.BaseType)
            {
                MethodInfo method = currentType.GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    binder: null,
                    types: actualParameterTypes,
                    modifiers: null);
                if (method != null)
                    return method;
            }

            return null;
        }

        private static int GetPressedExactCommanderOrderHotkeyIndex()
        {
            if (Input.IsKeyPressed(InputKey.F1))
                return 0;
            if (Input.IsKeyPressed(InputKey.F2))
                return 1;
            if (Input.IsKeyPressed(InputKey.F3))
                return 2;
            if (Input.IsKeyPressed(InputKey.F4))
                return 3;
            if (Input.IsKeyPressed(InputKey.F5))
                return 4;
            if (Input.IsKeyPressed(InputKey.F6))
                return 5;
            if (Input.IsKeyPressed(InputKey.F7))
                return 6;
            if (Input.IsKeyPressed(InputKey.F8))
                return 7;
            if (Input.IsKeyPressed(InputKey.F9))
                return 8;

            return -1;
        }

        private static bool AreAnyExactCommanderOrderHotkeysActive()
        {
            return
                Input.IsKeyDown(InputKey.BackSpace) ||
                Input.IsKeyPressed(InputKey.BackSpace) ||
                Input.IsKeyDown(InputKey.F1) ||
                Input.IsKeyPressed(InputKey.F1) ||
                Input.IsKeyDown(InputKey.F2) ||
                Input.IsKeyPressed(InputKey.F2) ||
                Input.IsKeyDown(InputKey.F3) ||
                Input.IsKeyPressed(InputKey.F3) ||
                Input.IsKeyDown(InputKey.F4) ||
                Input.IsKeyPressed(InputKey.F4) ||
                Input.IsKeyDown(InputKey.F5) ||
                Input.IsKeyPressed(InputKey.F5) ||
                Input.IsKeyDown(InputKey.F6) ||
                Input.IsKeyPressed(InputKey.F6) ||
                Input.IsKeyDown(InputKey.F7) ||
                Input.IsKeyPressed(InputKey.F7) ||
                Input.IsKeyDown(InputKey.F8) ||
                Input.IsKeyPressed(InputKey.F8) ||
                Input.IsKeyDown(InputKey.F9) ||
                Input.IsKeyPressed(InputKey.F9);
        }

        private static int GetPressedExactCommanderFormationHotkeyIndex()
        {
            if (Input.IsKeyPressed(InputKey.D1) || Input.IsKeyPressed(InputKey.Numpad1))
                return 0;
            if (Input.IsKeyPressed(InputKey.D2) || Input.IsKeyPressed(InputKey.Numpad2))
                return 1;
            if (Input.IsKeyPressed(InputKey.D3) || Input.IsKeyPressed(InputKey.Numpad3))
                return 2;
            if (Input.IsKeyPressed(InputKey.D4) || Input.IsKeyPressed(InputKey.Numpad4))
                return 3;
            if (Input.IsKeyPressed(InputKey.D5) || Input.IsKeyPressed(InputKey.Numpad5))
                return 4;
            if (Input.IsKeyPressed(InputKey.D6) || Input.IsKeyPressed(InputKey.Numpad6))
                return 5;
            if (Input.IsKeyPressed(InputKey.D7) || Input.IsKeyPressed(InputKey.Numpad7))
                return 6;
            if (Input.IsKeyPressed(InputKey.D8) || Input.IsKeyPressed(InputKey.Numpad8))
                return 7;

            return -1;
        }

        private static NetworkCommunicator TryResolveMessagePeer(object message)
        {
            if (message == null)
                return null;

            Type messageType = message.GetType();
            foreach (PropertyInfo property in messageType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!typeof(NetworkCommunicator).IsAssignableFrom(property.PropertyType) || property.GetIndexParameters().Length > 0)
                    continue;

                try
                {
                    NetworkCommunicator peer = property.GetValue(message, null) as NetworkCommunicator;
                    if (peer != null)
                        return peer;
                }
                catch
                {
                }
            }

            foreach (FieldInfo field in messageType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!typeof(NetworkCommunicator).IsAssignableFrom(field.FieldType))
                    continue;

                try
                {
                    NetworkCommunicator peer = field.GetValue(message) as NetworkCommunicator;
                    if (peer != null)
                        return peer;
                }
                catch
                {
                }
            }

            return null;
        }

        private static int TryResolveMessageInt(object message, string memberName)
        {
            if (message == null || string.IsNullOrWhiteSpace(memberName))
                return -1;

            Type messageType = message.GetType();
            PropertyInfo property = messageType.GetProperty(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.PropertyType == typeof(int) && property.GetIndexParameters().Length == 0)
            {
                try
                {
                    return (int)property.GetValue(message, null);
                }
                catch
                {
                }
            }

            FieldInfo field = messageType.GetField(
                memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(int))
            {
                try
                {
                    return (int)field.GetValue(message);
                }
                catch
                {
                }
            }

            return -1;
        }

        private static bool IsRelevantBattleMapStatus(
            CoopBattleEntryStatusBridgeFile.EntryStatusSnapshot status,
            int peerIndex,
            string missionName)
        {
            if (status == null)
                return true;

            if (!status.HasPeer || status.PeerIndex != peerIndex)
                return false;

            if (!string.IsNullOrWhiteSpace(missionName) &&
                !string.Equals(status.MissionName, missionName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            DateTime updatedUtc = status.UpdatedUtc;
            if (updatedUtc == DateTime.MinValue)
                return false;

            if (updatedUtc.Kind == DateTimeKind.Unspecified)
                updatedUtc = DateTime.SpecifyKind(updatedUtc, DateTimeKind.Utc);

            if (DateTime.UtcNow - updatedUtc > TimeSpan.FromSeconds(5))
                return false;

            string spawnStatus = status.SpawnStatus ?? string.Empty;
            if (string.Equals(spawnStatus, CoopBattleSpawnStatus.Pending.ToString(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(spawnStatus, CoopBattleSpawnStatus.Validating.ToString(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(spawnStatus, CoopBattleSpawnStatus.Validated.ToString(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(spawnStatus, CoopBattleSpawnStatus.Spawned.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string lifecycleState = status.LifecycleState ?? string.Empty;
            return
                string.Equals(lifecycleState, "SpawnQueued", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lifecycleState, "Alive", StringComparison.OrdinalIgnoreCase);
        }
    }
}
