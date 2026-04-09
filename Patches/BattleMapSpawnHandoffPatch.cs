using System;
using System.Collections;
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

        private static string _lastSuppressedFollowSwitchKey;
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

        private sealed class PendingLocalCommanderOrderControlFinalization
        {
            public int PeerIndex;
            public int TeamIndex;
            public int AgentIndex;
            public string EntryId;
            public DateTime QueuedUtc;
            public int Attempts;
        }

        public static void Apply(Harmony harmony)
        {
            TryApplyPatchStep(nameof(PatchMissionPeerFollowedAgent), () => PatchMissionPeerFollowedAgent(harmony));
            TryApplyPatchStep(nameof(PatchMissionNetworkComponentCreateAgent), () => PatchMissionNetworkComponentCreateAgent(harmony));
            TryApplyPatchStep(nameof(PatchMissionNetworkComponentSynchronizeAgentEquipment), () => PatchMissionNetworkComponentSynchronizeAgentEquipment(harmony));
            TryApplyPatchStep(nameof(PatchMissionNetworkComponentSetAgentPeer), () => PatchMissionNetworkComponentSetAgentPeer(harmony));
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

        public static void ResetRuntimeState(string source)
        {
            _lastSuppressedFollowSwitchKey = null;
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
            _suppressExactCommanderOrderHotkeyFallbackUntilRelease = false;
            _activeExactCommanderMissionOrderVm = null;
            _pendingLocalCommanderOrderControlFinalization = null;
            ModLogger.Info(
                "BattleMapSpawnHandoffPatch: reset runtime state. " +
                "Source=" + (source ?? "unknown"));
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
            MethodInfo postfix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleServerEventCreateAgent_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || postfix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionNetworkComponent.HandleServerEventCreateAgent not found. Skip.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: postfix applied to MissionNetworkComponent.HandleServerEventCreateAgent.");
        }

        private static void PatchMissionNetworkComponentSynchronizeAgentEquipment(Harmony harmony)
        {
            MethodInfo target = typeof(MissionNetworkComponent).GetMethod(
                "HandleServerEventSynchronizeAgentEquipment",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo prefix = typeof(BattleMapSpawnHandoffPatch).GetMethod(
                nameof(MissionNetworkComponent_HandleServerEventSynchronizeAgentEquipment_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            if (target == null || prefix == null)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: MissionNetworkComponent.HandleServerEventSynchronizeAgentEquipment not found. Skip.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("BattleMapSpawnHandoffPatch: prefix applied to MissionNetworkComponent.HandleServerEventSynchronizeAgentEquipment.");
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
                MissionPeer missionPeer = setAgentPeer.Peer?.GetComponent<MissionPeer>();
                if (agent == null || missionPeer == null || !missionPeer.IsMine || !agent.IsActive())
                    return;

                CoopBattleSelectionBridgeFile.SelectionBridgeSnapshot selectionBridge =
                    CoopBattleSelectionBridgeFile.ReadCurrentSelection();
                CoopBattleEntryPolicy.ClientSnapshot entryPolicy =
                    CoopBattleEntryPolicy.BuildClientSnapshot(mission, selectionBridge);
                if (entryPolicy == null || !entryPolicy.UseAuthoritativeTroopPath)
                    return;

                string logKey =
                    agent.Index + "|" +
                    (agent.SpawnEquipment != null).ToString() + "|" +
                    (agent.MountAgent?.Index.ToString() ?? "none") + "|" +
                    (entryPolicy.BridgeTroopOrEntryId ?? "none");
                if (string.Equals(_lastLocalVisualFinalizeKey, logKey, StringComparison.Ordinal))
                    return;

                bool exactVisualFinalized = CoopMissionSpawnLogic.TryFinalizeClientExactCampaignVisualForAgent(
                    mission,
                    agent,
                    entryPolicy.BridgeTroopOrEntryId,
                    "battle-map handoff SetAgentPeer");
                if (exactVisualFinalized)
                    agent.MountAgent?.UpdateAgentProperties();

                _lastLocalVisualFinalizeKey = logKey;
                ModLogger.Info(
                    "BattleMapSpawnHandoffPatch: finalized local player agent visuals after SetAgentPeer for battle-map handoff. " +
                    "AgentIndex=" + agent.Index +
                    " HasSpawnEquipment=" + (agent.SpawnEquipment != null) +
                    " MountAgentIndex=" + (agent.MountAgent?.Index.ToString() ?? "null") +
                    " ExactVisualFinalized=" + exactVisualFinalized +
                    " BridgeTroop=" + (entryPolicy.BridgeTroopOrEntryId ?? "null") +
                    " Mission=" + (mission.SceneName ?? "null"));
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: local SetAgentPeer visual finalization failed: " + ex.Message);
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

                bool exactVisualApplied = CoopMissionSpawnLogic.TryFinalizeClientExactCampaignVisualForAgent(
                    mission,
                    agent,
                    preferredEntryId: null,
                    source: "battle-map handoff CreateAgent");
                if (!exactVisualApplied)
                    return;

                ModLogger.Info(
                    "BattleMapSpawnHandoffPatch: queued delayed client exact visual finalization after CreateAgent. " +
                    "AgentIndex=" + agent.Index +
                    " TeamSide=" + agent.Team.Side +
                    " TroopId=" + (agent.Character?.StringId ?? "null") +
                    " Mission=" + (mission.SceneName ?? "null"));
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: local CreateAgent exact visual finalization failed: " + ex.Message);
            }
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

                Agent agent = Mission.MissionNetworkHelper.GetAgentFromIndex(synchronizeAgentSpawnEquipment.AgentIndex, canBeNull: true);
                if (agent == null || !agent.IsActive() || agent.IsMount || agent.Team == null || agent.Team.Side == BattleSideEnum.None)
                    return true;

                bool replaced = CoopMissionSpawnLogic.TryHandleClientExactCampaignSpawnEquipmentSync(
                    mission,
                    agent,
                    "battle-map handoff SynchronizeAgentSpawnEquipment");
                return !replaced;
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: SynchronizeAgentSpawnEquipment exact override failed open: " + ex.Message);
                return true;
            }
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
                if (TrySuppressNonCommanderOrderUi(__instance))
                    return;

                TryFinalizePendingLocalCommanderOrderControl(__instance);
                TryMaintainLocalCommanderOrderControl(__instance);
                TryHandleExactCommanderOrderUiHotkeys(__instance);
            }
            catch (Exception ex)
            {
                ModLogger.Info("BattleMapSpawnHandoffPatch: local commander order-control finalization tick failed: " + ex.Message);
            }
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

            string controlledEntryId = ResolveAgentEntryId(controlledAgent, entryPolicy.BridgeTroopOrEntryId);
            bool isExactCommander = IsEntryIdExactCommanderForTeam(team, controlledEntryId, out string commanderEntryId);
            bool hasCommanderIdentity = !string.IsNullOrWhiteSpace(commanderEntryId);
            bool hasCommanderControlCounts = myMissionPeer.BotsUnderControlTotal > 1 || myMissionPeer.BotsUnderControlAlive > 0;
            if (isExactCommander || (!hasCommanderIdentity && hasCommanderControlCounts))
                return false;

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

            string logKey =
                myPeer.Index + "|" +
                team.TeamIndex + "|" +
                controlledAgent.Index + "|" +
                (controlledEntryId ?? "none") + "|" +
                (commanderEntryId ?? "none") + "|" +
                myMissionPeer.BotsUnderControlTotal + "|" +
                myMissionPeer.BotsUnderControlAlive;
            if (string.Equals(_lastSuppressedNonCommanderOrderUiKey, logKey, StringComparison.Ordinal))
                return true;

            _lastSuppressedNonCommanderOrderUiKey = logKey;
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
                ExactCampaignArmyBootstrap.TryGetEntryId(agent, out string controlledEntryId) &&
                !string.IsNullOrWhiteSpace(controlledEntryId))
            {
                return controlledEntryId;
            }

            return string.IsNullOrWhiteSpace(fallbackEntryId)
                ? null
                : fallbackEntryId;
        }

        private static bool IsEntryIdExactCommanderForTeam(Team team, string candidateEntryId, out string commanderEntryId)
        {
            commanderEntryId = null;
            if (string.IsNullOrWhiteSpace(candidateEntryId) || !TryResolveCommanderEntryIdForTeam(team, out commanderEntryId))
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

            string controlledEntryId = ResolveAgentEntryId(controlledAgent, entryPolicy.BridgeTroopOrEntryId);
            bool isExactCommander = IsEntryIdExactCommanderForTeam(team, controlledEntryId, out string commanderEntryId);
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

            string controlledEntryId = ResolveAgentEntryId(controlledAgent, entryPolicy.BridgeTroopOrEntryId);
            bool isExactCommander = IsEntryIdExactCommanderForTeam(team, controlledEntryId, out string commanderEntryId);
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

            string controlledEntryId = null;
            if (!ExactCampaignArmyBootstrap.TryGetEntryId(controlledAgent, out controlledEntryId))
                controlledEntryId = null;

            if (string.IsNullOrWhiteSpace(controlledEntryId))
                controlledEntryId = entryPolicy.BridgeTroopOrEntryId;

            if (!string.Equals(controlledEntryId, commanderEntry.EntryId, StringComparison.Ordinal))
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
