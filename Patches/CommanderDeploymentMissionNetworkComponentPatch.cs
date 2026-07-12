using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using HarmonyLib;
using NetworkMessages.FromClient;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;
using TaleWorlds.MountAndBlade.Objects.Siege;

namespace CoopSpectator.Patches
{
    public static class CommanderDeploymentMissionNetworkComponentPatch
    {
        private static readonly Dictionary<int, CommanderDeploymentSelectionState> CommanderDeploymentSelectionsByPeer =
            new Dictionary<int, CommanderDeploymentSelectionState>();
        private static readonly object CommanderDeploymentSelectionLock = new object();
        private static readonly MethodInfo MoveToLineSegmentMethod = typeof(OrderController).GetMethod(
            "MoveToLineSegment",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(IEnumerable<Formation>), typeof(WorldPosition), typeof(WorldPosition), typeof(bool) },
            modifiers: null);

        public static void Apply(Harmony harmony)
        {
            if (harmony == null)
                return;

            PatchPrivateMethod(
                harmony,
                "GetTeamOfPeer",
                nameof(MissionNetworkComponent_GetTeamOfPeer_Prefix),
                new[] { typeof(NetworkCommunicator) });
            PatchPrivateMethod(
                harmony,
                "GetOrderControllerOfPeer",
                nameof(MissionNetworkComponent_GetOrderControllerOfPeer_Prefix),
                new[] { typeof(NetworkCommunicator) });

            PatchPrivateMethod(
                harmony,
                "HandleClientEventApplyOrder",
                nameof(MissionNetworkComponent_HandleClientEventApplyOrder_Prefix),
                new[] { typeof(NetworkCommunicator), typeof(GameNetworkMessage) });
            PatchPrivateMethod(
                harmony,
                "HandleClientEventApplyOrderWithPosition",
                nameof(MissionNetworkComponent_HandleClientEventApplyOrderWithPosition_Prefix),
                new[] { typeof(NetworkCommunicator), typeof(GameNetworkMessage) });
            PatchPrivateMethod(
                harmony,
                "HandleClientEventApplyOrderWithFormation",
                nameof(MissionNetworkComponent_HandleClientEventApplyOrderWithFormation_Prefix),
                new[] { typeof(NetworkCommunicator), typeof(GameNetworkMessage) });
            PatchPrivateMethod(
                harmony,
                "HandleClientEventApplyOrderWithFormationAndPercentage",
                nameof(MissionNetworkComponent_HandleClientEventApplyOrderWithFormationAndPercentage_Prefix),
                new[] { typeof(NetworkCommunicator), typeof(GameNetworkMessage) });
            PatchPrivateMethod(
                harmony,
                "HandleClientEventApplyOrderWithFormationAndNumber",
                nameof(MissionNetworkComponent_HandleClientEventApplyOrderWithFormationAndNumber_Prefix),
                new[] { typeof(NetworkCommunicator), typeof(GameNetworkMessage) });
            PatchPrivateMethod(
                harmony,
                "HandleClientEventApplyOrderWithTwoPositions",
                nameof(MissionNetworkComponent_HandleClientEventApplyOrderWithTwoPositions_Prefix),
                new[] { typeof(NetworkCommunicator), typeof(GameNetworkMessage) });
            PatchPrivateMethod(
                harmony,
                "HandleClientEventApplySiegeWeaponOrder",
                nameof(MissionNetworkComponent_HandleClientEventApplySiegeWeaponOrder_Prefix),
                new[] { typeof(NetworkCommunicator), typeof(GameNetworkMessage) });
            PatchPrivateMethod(
                harmony,
                "HandleClientEventClearSelectedFormations",
                nameof(MissionNetworkComponent_HandleClientEventClearSelectedFormations_Prefix),
                new[] { typeof(NetworkCommunicator), typeof(GameNetworkMessage) });
            PatchPrivateMethod(
                harmony,
                "HandleClientEventSelectAllFormations",
                nameof(MissionNetworkComponent_HandleClientEventSelectAllFormations_Prefix),
                new[] { typeof(NetworkCommunicator), typeof(GameNetworkMessage) });
            PatchPrivateMethod(
                harmony,
                "HandleClientEventSelectAllSiegeWeapons",
                nameof(MissionNetworkComponent_HandleClientEventSelectAllSiegeWeapons_Prefix),
                new[] { typeof(NetworkCommunicator), typeof(GameNetworkMessage) });
            PatchPrivateMethod(
                harmony,
                "HandleClientEventSelectFormation",
                nameof(MissionNetworkComponent_HandleClientEventSelectFormation_Prefix),
                new[] { typeof(NetworkCommunicator), typeof(GameNetworkMessage) });
            PatchPrivateMethod(
                harmony,
                "HandleClientEventSelectSiegeWeapon",
                nameof(MissionNetworkComponent_HandleClientEventSelectSiegeWeapon_Prefix),
                new[] { typeof(NetworkCommunicator), typeof(GameNetworkMessage) });
            PatchPrivateMethod(
                harmony,
                "HandleClientEventUnselectFormation",
                nameof(MissionNetworkComponent_HandleClientEventUnselectFormation_Prefix),
                new[] { typeof(NetworkCommunicator), typeof(GameNetworkMessage) });
            PatchPrivateMethod(
                harmony,
                "HandleClientEventUnselectSiegeWeapon",
                nameof(MissionNetworkComponent_HandleClientEventUnselectSiegeWeapon_Prefix),
                new[] { typeof(NetworkCommunicator), typeof(GameNetworkMessage) });
        }

        private static void PatchPrivateMethod(
            Harmony harmony,
            string targetMethodName,
            string prefixMethodName,
            Type[] parameterTypes)
        {
            MethodInfo target = typeof(MissionNetworkComponent).GetMethod(
                targetMethodName,
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: parameterTypes,
                modifiers: null);
            MethodInfo prefix = typeof(CommanderDeploymentMissionNetworkComponentPatch).GetMethod(
                prefixMethodName,
                BindingFlags.Static | BindingFlags.NonPublic);

            if (target == null || prefix == null)
            {
                ModLogger.Info(
                    "CommanderDeploymentMissionNetworkComponentPatch: " +
                    targetMethodName +
                    " not found. Skip.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info(
                "CommanderDeploymentMissionNetworkComponentPatch: prefix applied to MissionNetworkComponent." +
                targetMethodName +
                ".");
        }

        private static bool MissionNetworkComponent_GetTeamOfPeer_Prefix(
            NetworkCommunicator networkPeer,
            ref Team __result)
        {
            if (!TryResolveOrderLease(networkPeer, out Team team, out _))
            {
                return true;
            }

            __result = team;
            return false;
        }

        private static bool MissionNetworkComponent_GetOrderControllerOfPeer_Prefix(
            NetworkCommunicator networkPeer,
            ref OrderController __result)
        {
            if (!TryResolveOrderLease(networkPeer, out _, out OrderController orderController))
            {
                return true;
            }

            __result = orderController;
            return false;
        }

        private static bool MissionNetworkComponent_HandleClientEventApplyOrder_Prefix(
            NetworkCommunicator networkPeer,
            GameNetworkMessage baseMessage,
            ref bool __result)
        {
            try
            {
                if (!TryResolveOrderLease(networkPeer, out Team team, out OrderController orderController))
                    return true;

                ApplyOrder message = baseMessage as ApplyOrder;
                if (message == null || orderController == null)
                    return true;

                TryRefreshNativeSelectionFromShadow(networkPeer, team, orderController);
                List<Formation> selectedFormations = ResolveShadowSelectedFormations(
                    networkPeer,
                    team,
                    includeEmpty: false);
                if (selectedFormations.Count <= 0 && orderController.SelectedFormations != null)
                {
                    selectedFormations.AddRange(
                        orderController.SelectedFormations.Where(formation =>
                            formation != null &&
                            formation.CountOfUnits > 0 &&
                            ReferenceEquals(formation.Team, team)));
                }
                orderController.SetOrder(message.OrderType);
                if (message.OrderType == OrderType.AIControlOn ||
                    message.OrderType == OrderType.AIControlOff)
                {
                    CoopMissionNetworkBridge.UpdateVoluntaryFormationAiControl(
                        Mission.Current,
                        team,
                        selectedFormations,
                        message.OrderType == OrderType.AIControlOn,
                        "commander-simple-order-" + message.OrderType);
                }

                LogOrderDiagnostics(
                    "simple-order-applied",
                    networkPeer,
                    orderController,
                    "OrderType=" + message.OrderType +
                    " Shadow=[" + BuildShadowSelectionSummary(networkPeer, team) + "]");

                __result = true;
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool MissionNetworkComponent_HandleClientEventApplyOrderWithPosition_Prefix(
            NetworkCommunicator networkPeer,
            GameNetworkMessage baseMessage,
            ref bool __result)
        {
            try
            {
                if (!TryResolveOrderLease(
                        networkPeer,
                        out Team team,
                        out OrderController orderController,
                        out bool isDeploymentOrderLease))
                    return true;

                ApplyOrderWithPosition message = baseMessage as ApplyOrderWithPosition;
                Mission mission = Mission.Current;
                if (message == null || mission?.Scene == null || orderController == null)
                    return true;

                TryRefreshNativeSelectionFromShadow(networkPeer, team, orderController);
                var orderPosition = new WorldPosition(
                    mission.Scene,
                    UIntPtr.Zero,
                    message.Position,
                    hasValidZ: false);
                if (isDeploymentOrderLease)
                {
                    CoopSiegeDeploymentBoundaryRuntime.TryClampCommanderDeploymentPosition(
                        mission,
                        team,
                        ref orderPosition,
                        "server-position-order");
                }
                orderController.SetOrderWithPosition(message.OrderType, orderPosition);

                LogOrderDiagnostics(
                    "position-order-applied",
                    networkPeer,
                    orderController,
                    "OrderType=" + message.OrderType +
                    " Shadow=[" + BuildShadowSelectionSummary(networkPeer, team) + "]");

                __result = true;
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool MissionNetworkComponent_HandleClientEventApplyOrderWithFormation_Prefix(
            NetworkCommunicator networkPeer,
            GameNetworkMessage baseMessage,
            ref bool __result)
        {
            try
            {
                if (!TryResolveOrderLease(networkPeer, out Team team, out OrderController orderController))
                    return true;

                ApplyOrderWithFormation message = baseMessage as ApplyOrderWithFormation;
                Formation formation = message == null ? null : ResolveFormation(team, message.FormationIndex);
                if (formation != null)
                    orderController.SetOrderWithFormation(message.OrderType, formation);

                __result = true;
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool MissionNetworkComponent_HandleClientEventApplyOrderWithFormationAndPercentage_Prefix(
            NetworkCommunicator networkPeer,
            GameNetworkMessage baseMessage,
            ref bool __result)
        {
            try
            {
                if (!TryResolveOrderLease(networkPeer, out Team team, out OrderController orderController))
                    return true;

                ApplyOrderWithFormationAndPercentage message = baseMessage as ApplyOrderWithFormationAndPercentage;
                Formation formation = message == null ? null : ResolveFormation(team, message.FormationIndex);
                if (formation != null)
                    orderController.SetOrderWithFormationAndPercentage(
                        message.OrderType,
                        formation,
                        message.Percentage * 0.01f);

                __result = true;
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool MissionNetworkComponent_HandleClientEventApplyOrderWithFormationAndNumber_Prefix(
            NetworkCommunicator networkPeer,
            GameNetworkMessage baseMessage,
            ref bool __result)
        {
            try
            {
                if (!TryResolveOrderLease(networkPeer, out Team team, out OrderController orderController))
                    return true;

                ApplyOrderWithFormationAndNumber message = baseMessage as ApplyOrderWithFormationAndNumber;
                Formation formation = message == null ? null : ResolveFormation(team, message.FormationIndex);
                if (formation != null)
                    orderController.SetOrderWithFormationAndNumber(
                        message.OrderType,
                        formation,
                        message.Number);

                __result = true;
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool MissionNetworkComponent_HandleClientEventApplyOrderWithTwoPositions_Prefix(
            NetworkCommunicator networkPeer,
            GameNetworkMessage baseMessage,
            ref bool __result)
        {
            try
            {
                if (!TryResolveOrderLease(
                        networkPeer,
                        out Team team,
                        out OrderController orderController,
                        out bool isDeploymentOrderLease))
                    return true;

                ApplyOrderWithTwoPositions message = baseMessage as ApplyOrderWithTwoPositions;
                Mission mission = Mission.Current;
                if (message == null || mission?.Scene == null || orderController == null)
                    return true;

                var shadowFormations = ResolveShadowSelectedFormations(networkPeer, team, includeEmpty: false);
                bool shouldUseShadowSelection = shadowFormations.Count > 0;
                if (shouldUseShadowSelection)
                    TryRefreshNativeSelectionFromShadow(networkPeer, team, orderController);

                LogOrderDiagnostics(
                    "two-positions-entry",
                    networkPeer,
                    orderController,
                    "OrderType=" + message.OrderType +
                    " Shadow=[" + BuildShadowSelectionSummary(networkPeer, team) + "]");

                bool previousTeleportingAgents = mission.IsTeleportingAgents;
                try
                {
                    if (isDeploymentOrderLease)
                        mission.IsTeleportingAgents = true;
                    var position1 = new WorldPosition(mission.Scene, UIntPtr.Zero, message.Position1, hasValidZ: false);
                    var position2 = new WorldPosition(mission.Scene, UIntPtr.Zero, message.Position2, hasValidZ: false);
                    if (isDeploymentOrderLease)
                    {
                        CoopSiegeDeploymentBoundaryRuntime.TryClampCommanderDeploymentPosition(
                            mission,
                            team,
                            ref position1,
                            "server-two-position-order-start");
                        CoopSiegeDeploymentBoundaryRuntime.TryClampCommanderDeploymentPosition(
                            mission,
                            team,
                            ref position2,
                            "server-two-position-order-end");
                    }
                    bool nativeSelectionMatchesShadow = !shouldUseShadowSelection ||
                        IsNativeSelectionEquivalentToShadow(orderController, shadowFormations);
                    if (nativeSelectionMatchesShadow)
                    {
                        orderController.SetOrderWithTwoPositions(
                            message.OrderType,
                            position1,
                            position2);
                        if (isDeploymentOrderLease)
                            ForceCommanderDeploymentPositioning(orderController);
                    }
                    else if (!TryApplyShadowMoveToLineSegment(
                                 orderController,
                                 shadowFormations,
                                 message.OrderType,
                                 position1,
                                 position2,
                                 forceDeploymentPositioning: isDeploymentOrderLease))
                    {
                        orderController.SetOrderWithTwoPositions(
                            message.OrderType,
                            position1,
                            position2);
                        if (isDeploymentOrderLease)
                            ForceCommanderDeploymentPositioning(orderController);
                    }
                }
                finally
                {
                    if (isDeploymentOrderLease)
                        mission.IsTeleportingAgents = previousTeleportingAgents;
                }

                LogOrderDiagnostics(
                    "two-positions-applied",
                    networkPeer,
                    orderController,
                    "OrderType=" + message.OrderType);

                __result = true;
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool MissionNetworkComponent_HandleClientEventApplySiegeWeaponOrder_Prefix(
            NetworkCommunicator networkPeer,
            GameNetworkMessage baseMessage,
            ref bool __result)
        {
            try
            {
                if (!TryResolveOrderLease(networkPeer, out Team team, out OrderController orderController))
                    return true;

                ApplySiegeWeaponOrder message = baseMessage as ApplySiegeWeaponOrder;
                if (message == null || orderController?.SiegeWeaponController == null)
                    return true;

                orderController.SiegeWeaponController.SetOrder(message.OrderType);
                LogOrderDiagnostics(
                    "siege-weapon-order-applied",
                    networkPeer,
                    orderController,
                    "OrderType=" + message.OrderType +
                    " Team=" + FormatTeam(team));

                __result = true;
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool MissionNetworkComponent_HandleClientEventClearSelectedFormations_Prefix(
            NetworkCommunicator networkPeer,
            GameNetworkMessage baseMessage,
            ref bool __result)
        {
            try
            {
                if (!TryResolveOrderLease(networkPeer, out Team team, out OrderController orderController))
                    return true;

                ClearShadowSelection(networkPeer, team);
                orderController?.ClearSelectedFormations();

                LogOrderDiagnostics(
                    "clear-selected-formations",
                    networkPeer,
                    orderController,
                    "Shadow=[" + BuildShadowSelectionSummary(networkPeer, team) + "]");

                __result = true;
                return false;
            }
            catch
            {
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
                if (!TryResolveOrderLease(networkPeer, out Team team, out OrderController orderController))
                    return true;

                SetShadowSelectionToAllActiveFormations(networkPeer, team);
                TryRefreshNativeSelectionFromShadow(networkPeer, team, orderController);

                LogOrderDiagnostics(
                    "select-all-formations",
                    networkPeer,
                    orderController,
                    "Shadow=[" + BuildShadowSelectionSummary(networkPeer, team) + "]");

                __result = true;
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool MissionNetworkComponent_HandleClientEventSelectAllSiegeWeapons_Prefix(
            NetworkCommunicator networkPeer,
            GameNetworkMessage baseMessage,
            ref bool __result)
        {
            try
            {
                if (!TryResolveOrderLease(networkPeer, out Team team, out OrderController orderController))
                    return true;

                if (orderController?.SiegeWeaponController == null)
                    return true;

                orderController.SiegeWeaponController.SelectAll();
                LogOrderDiagnostics(
                    "select-all-siege-weapons",
                    networkPeer,
                    orderController,
                    "Team=" + FormatTeam(team));

                __result = true;
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool MissionNetworkComponent_HandleClientEventSelectFormation_Prefix(
            NetworkCommunicator networkPeer,
            GameNetworkMessage baseMessage,
            ref bool __result)
        {
            try
            {
                if (!TryResolveOrderLease(networkPeer, out Team team, out OrderController orderController))
                    return true;

                SelectFormation message = baseMessage as SelectFormation;
                Formation formation = message == null ? null : ResolveFormation(team, message.FormationIndex);
                if (formation != null && !IsFormationAuthorized(networkPeer, team, formation.Index))
                    formation = null;
                if (formation != null)
                    AddShadowSelectedFormation(networkPeer, team, formation.Index);
                if (formation != null)
                    orderController.SelectFormation(formation);

                LogOrderDiagnostics(
                    "select-formation",
                    networkPeer,
                    orderController,
                    "Requested=" + (message == null ? "<null>" : message.FormationIndex.ToString()) +
                    " Resolved=" + FormatFormation(formation) +
                    " Shadow=[" + BuildShadowSelectionSummary(networkPeer, team) + "]");

                __result = true;
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool MissionNetworkComponent_HandleClientEventSelectSiegeWeapon_Prefix(
            NetworkCommunicator networkPeer,
            GameNetworkMessage baseMessage,
            ref bool __result)
        {
            try
            {
                if (!TryResolveOrderLease(networkPeer, out Team team, out OrderController orderController))
                    return true;

                SelectSiegeWeapon message = baseMessage as SelectSiegeWeapon;
                SiegeWeapon siegeWeapon = message == null ? null : ResolveSiegeWeapon(message.SiegeWeaponId, team);
                if (siegeWeapon != null && orderController?.SiegeWeaponController != null)
                    orderController.SiegeWeaponController.Select(siegeWeapon);

                LogOrderDiagnostics(
                    "select-siege-weapon",
                    networkPeer,
                    orderController,
                    "Requested=" + (message == null ? "<null>" : message.SiegeWeaponId.ToString()) +
                    " Resolved=" + FormatSiegeWeapon(siegeWeapon) +
                    " Team=" + FormatTeam(team));

                __result = true;
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool MissionNetworkComponent_HandleClientEventUnselectFormation_Prefix(
            NetworkCommunicator networkPeer,
            GameNetworkMessage baseMessage,
            ref bool __result)
        {
            try
            {
                if (!TryResolveOrderLease(networkPeer, out Team team, out OrderController orderController))
                    return true;

                UnselectFormation message = baseMessage as UnselectFormation;
                Formation formation = message == null ? null : ResolveFormation(team, message.FormationIndex);
                if (message != null)
                    RemoveShadowSelectedFormation(networkPeer, team, message.FormationIndex);
                if (formation != null && IsFormationSelected(orderController, formation))
                    orderController.DeselectFormation(formation);

                LogOrderDiagnostics(
                    "unselect-formation",
                    networkPeer,
                    orderController,
                    "Requested=" + (message == null ? "<null>" : message.FormationIndex.ToString()) +
                    " Resolved=" + FormatFormation(formation) +
                    " Shadow=[" + BuildShadowSelectionSummary(networkPeer, team) + "]");

                __result = true;
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool MissionNetworkComponent_HandleClientEventUnselectSiegeWeapon_Prefix(
            NetworkCommunicator networkPeer,
            GameNetworkMessage baseMessage,
            ref bool __result)
        {
            try
            {
                if (!TryResolveOrderLease(networkPeer, out Team team, out OrderController orderController))
                    return true;

                UnselectSiegeWeapon message = baseMessage as UnselectSiegeWeapon;
                SiegeWeapon siegeWeapon = message == null ? null : ResolveSiegeWeapon(message.SiegeWeaponId, team);
                if (siegeWeapon != null && orderController?.SiegeWeaponController != null)
                    orderController.SiegeWeaponController.Deselect(siegeWeapon);

                LogOrderDiagnostics(
                    "unselect-siege-weapon",
                    networkPeer,
                    orderController,
                    "Requested=" + (message == null ? "<null>" : message.SiegeWeaponId.ToString()) +
                    " Resolved=" + FormatSiegeWeapon(siegeWeapon) +
                    " Team=" + FormatTeam(team));

                __result = true;
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool TryResolveOrderLease(
            NetworkCommunicator networkPeer,
            out Team team,
            out OrderController orderController)
        {
            return TryResolveOrderLease(
                networkPeer,
                out team,
                out orderController,
                out _);
        }

        private static bool TryResolveOrderLease(
            NetworkCommunicator networkPeer,
            out Team team,
            out OrderController orderController,
            out bool isDeploymentOrderLease)
        {
            if (CoopMissionSpawnLogic.TryResolveCommanderDeploymentOrderLease(
                    networkPeer,
                    out team,
                    out orderController,
                    out _))
            {
                isDeploymentOrderLease = true;
                SetAuthorizedFormationIndices(networkPeer, team, authorizedFormationIndices: null);
                return true;
            }

            isDeploymentOrderLease = false;
            if (!CoopMissionNetworkBridge.TryResolveExactBattleOrderAuthority(
                    networkPeer,
                    out team,
                    out orderController,
                    out _,
                    out List<int> authorizedFormationIndices,
                    out _))
            {
                return false;
            }

            SetAuthorizedFormationIndices(networkPeer, team, authorizedFormationIndices);
            return orderController != null;
        }

        internal static void TryRefreshCommanderDeploymentSelection(
            NetworkCommunicator networkPeer,
            Team team,
            OrderController orderController)
        {
            try
            {
                TryRefreshNativeSelectionFromShadow(networkPeer, team, orderController);
            }
            catch
            {
            }
        }

        private static void ForceCommanderDeploymentPositioning(OrderController orderController)
        {
            if (orderController?.SelectedFormations == null)
                return;

            var selectedFormations = new List<Formation>();
            foreach (Formation formation in orderController.SelectedFormations)
            {
                if (formation != null && formation.CountOfUnits > 0)
                    selectedFormations.Add(formation);
            }

            ForceCommanderDeploymentPositioning(selectedFormations);
        }

        private static void ForceCommanderDeploymentPositioning(IEnumerable<Formation> formations)
        {
            if (formations == null)
                return;

            foreach (Formation formation in formations)
            {
                if (formation == null || formation.CountOfUnits <= 0)
                    continue;

                try
                {
                    WorldPosition orderPosition = formation
                        .GetReadonlyMovementOrderReference()
                        .CreateNewOrderWorldPositionMT(formation, WorldPosition.WorldPositionEnforcedCache.None);
                    Vec2 direction = formation.FacingOrder.GetDirection(formation);
                    if (orderPosition.IsValid || direction.IsValid)
                        formation.SetPositioning(orderPosition, direction);

                    formation.ApplyActionOnEachUnit(
                        agent => agent.ForceUpdateCachedAndFormationValues(updateOnlyMovement: true, arrangementChangeAllowed: false));
                    formation.SetHasPendingUnitPositions(hasPendingUnitPositions: false);
                    formation.SetMovementOrder(MovementOrder.MovementOrderStop);
                }
                catch
                {
                }
            }
        }

        private static Formation ResolveFormation(Team team, int formationIndex)
        {
            if (team?.FormationsIncludingEmpty == null)
                return null;

            foreach (Formation formation in team.FormationsIncludingEmpty)
            {
                if (formation != null &&
                    formation.Index == formationIndex)
                {
                    return formation;
                }
            }

            return null;
        }

        private static SiegeWeapon ResolveSiegeWeapon(MissionObjectId siegeWeaponId, Team team)
        {
            if (siegeWeaponId == MissionObjectId.Invalid || team == null)
                return null;

            try
            {
                SiegeWeapon siegeWeapon = TaleWorlds.MountAndBlade.Mission.MissionNetworkHelper
                    .GetMissionObjectFromMissionObjectId(siegeWeaponId) as SiegeWeapon;
                if (siegeWeapon == null || siegeWeapon.IsDisabled || siegeWeapon.Side != team.Side)
                    return null;

                return siegeWeapon;
            }
            catch
            {
                return null;
            }
        }

        private static bool TryApplyShadowMoveToLineSegment(
            OrderController orderController,
            List<Formation> shadowFormations,
            OrderType orderType,
            WorldPosition position1,
            WorldPosition position2,
            bool forceDeploymentPositioning)
        {
            if (orderController == null ||
                shadowFormations == null ||
                shadowFormations.Count <= 0 ||
                (orderType != OrderType.MoveToLineSegment &&
                 orderType != OrderType.MoveToLineSegmentWithHorizontalLayout))
            {
                return false;
            }

            var activeFormations = new List<Formation>();
            foreach (Formation formation in shadowFormations)
            {
                if (formation != null && formation.CountOfUnitsWithoutDetachedOnes > 0)
                    activeFormations.Add(formation);
            }

            if (activeFormations.Count <= 0)
                return false;

            try
            {
                if (MoveToLineSegmentMethod != null)
                {
                    MoveToLineSegmentMethod.Invoke(
                        orderController,
                        new object[]
                        {
                            activeFormations,
                            position1,
                            position2,
                            orderType == OrderType.MoveToLineSegment
                        });
                    if (forceDeploymentPositioning)
                        ForceCommanderDeploymentPositioning(activeFormations);
                    return true;
                }
            }
            catch
            {
            }

            Vec2 direction = (position2.GetGroundVec3().AsVec2 - position1.GetGroundVec3().AsVec2);
            if (!direction.IsValid || direction.LengthSquared < 0.0001f)
                direction = Vec2.Forward;
            else
                direction = direction.Normalized();

            float width = (position2.GetGroundVec3().AsVec2 - position1.GetGroundVec3().AsVec2).Length;
            foreach (Formation formation in activeFormations)
            {
                try
                {
                    formation.SetPositioning(position1, direction);
                    if (width > 0.1f)
                        formation.SetFormOrder(FormOrder.FormOrderCustom(width), updateDesiredFileCount: true);
                    formation.SetMovementOrder(MovementOrder.MovementOrderMove(position1));
                }
                catch
                {
                }
            }

            if (forceDeploymentPositioning)
                ForceCommanderDeploymentPositioning(activeFormations);
            return true;
        }

        private static bool TryRefreshNativeSelectionFromShadow(
            NetworkCommunicator networkPeer,
            Team team,
            OrderController orderController)
        {
            if (orderController == null)
                return false;

            List<Formation> shadowFormations = ResolveShadowSelectedFormations(networkPeer, team, includeEmpty: false);
            if (shadowFormations.Count <= 0)
                return false;

            try
            {
                orderController.ClearSelectedFormations();
            }
            catch
            {
            }

            foreach (Formation formation in shadowFormations)
            {
                try
                {
                    if (formation != null && formation.CountOfUnits > 0 && !IsFormationSelected(orderController, formation))
                        orderController.SelectFormation(formation);
                }
                catch
                {
                }
            }

            return true;
        }

        private static bool IsNativeSelectionEquivalentToShadow(
            OrderController orderController,
            List<Formation> shadowFormations)
        {
            if (orderController?.SelectedFormations == null ||
                shadowFormations == null ||
                shadowFormations.Count <= 0)
            {
                return false;
            }

            int selectedCount = 0;
            foreach (Formation formation in orderController.SelectedFormations)
            {
                if (formation == null || formation.CountOfUnits <= 0)
                    continue;

                selectedCount++;
                bool found = false;
                foreach (Formation shadowFormation in shadowFormations)
                {
                    if (ReferenceEquals(formation, shadowFormation))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                    return false;
            }

            return selectedCount == shadowFormations.Count;
        }

        private static List<Formation> ResolveShadowSelectedFormations(
            NetworkCommunicator networkPeer,
            Team team,
            bool includeEmpty)
        {
            var formations = new List<Formation>();
            int[] indices = GetShadowSelectedFormationIndices(networkPeer, team);
            foreach (int formationIndex in indices)
            {
                Formation formation = ResolveFormation(team, formationIndex);
                if (formation != null && (includeEmpty || formation.CountOfUnits > 0))
                    formations.Add(formation);
            }

            return formations;
        }

        private static int[] GetShadowSelectedFormationIndices(NetworkCommunicator networkPeer, Team team)
        {
            if (!TryGetPeerKey(networkPeer, out int key) || team == null)
                return Array.Empty<int>();

            lock (CommanderDeploymentSelectionLock)
            {
                if (!CommanderDeploymentSelectionsByPeer.TryGetValue(key, out CommanderDeploymentSelectionState state) ||
                    state.TeamIndex != team.TeamIndex)
                {
                    return Array.Empty<int>();
                }

                IEnumerable<int> selectedIndices = state.FormationIndices;
                if (state.AuthorizedFormationIndices != null)
                    selectedIndices = selectedIndices.Where(state.AuthorizedFormationIndices.Contains);
                int[] result = selectedIndices.ToArray();
                Array.Sort(result);
                return result;
            }
        }

        private static void AddShadowSelectedFormation(NetworkCommunicator networkPeer, Team team, int formationIndex)
        {
            if (formationIndex < 0 || !TryGetPeerKey(networkPeer, out int key) || team == null)
                return;

            lock (CommanderDeploymentSelectionLock)
            {
                CommanderDeploymentSelectionState state = GetOrCreateShadowSelectionState(key, team);
                if (state.AuthorizedFormationIndices != null &&
                    !state.AuthorizedFormationIndices.Contains(formationIndex))
                {
                    return;
                }
                state.FormationIndices.Add(formationIndex);
            }
        }

        private static void RemoveShadowSelectedFormation(NetworkCommunicator networkPeer, Team team, int formationIndex)
        {
            if (!TryGetPeerKey(networkPeer, out int key) || team == null)
                return;

            lock (CommanderDeploymentSelectionLock)
            {
                if (CommanderDeploymentSelectionsByPeer.TryGetValue(key, out CommanderDeploymentSelectionState state) &&
                    state.TeamIndex == team.TeamIndex)
                {
                    state.FormationIndices.Remove(formationIndex);
                }
            }
        }

        private static void ClearShadowSelection(NetworkCommunicator networkPeer, Team team)
        {
            if (!TryGetPeerKey(networkPeer, out int key) || team == null)
                return;

            lock (CommanderDeploymentSelectionLock)
            {
                if (CommanderDeploymentSelectionsByPeer.TryGetValue(key, out CommanderDeploymentSelectionState state) &&
                    state.TeamIndex == team.TeamIndex)
                {
                    state.FormationIndices.Clear();
                }
            }
        }

        private static void SetShadowSelectionToAllActiveFormations(NetworkCommunicator networkPeer, Team team)
        {
            if (!TryGetPeerKey(networkPeer, out int key) || team?.FormationsIncludingEmpty == null)
                return;

            lock (CommanderDeploymentSelectionLock)
            {
                CommanderDeploymentSelectionState state = GetOrCreateShadowSelectionState(key, team);
                state.FormationIndices.Clear();
                foreach (Formation formation in team.FormationsIncludingEmpty)
                {
                    if (formation != null &&
                        formation.CountOfUnits > 0 &&
                        (state.AuthorizedFormationIndices == null ||
                         state.AuthorizedFormationIndices.Contains(formation.Index)))
                    {
                        state.FormationIndices.Add(formation.Index);
                    }
                }
            }
        }

        private static void SetAuthorizedFormationIndices(
            NetworkCommunicator networkPeer,
            Team team,
            IEnumerable<int> authorizedFormationIndices)
        {
            if (!TryGetPeerKey(networkPeer, out int key) || team == null)
                return;

            lock (CommanderDeploymentSelectionLock)
            {
                CommanderDeploymentSelectionState state = GetOrCreateShadowSelectionState(key, team);
                state.AuthorizedFormationIndices = authorizedFormationIndices == null
                    ? null
                    : new HashSet<int>(authorizedFormationIndices);
                if (state.AuthorizedFormationIndices != null)
                    state.FormationIndices.RemoveWhere(index => !state.AuthorizedFormationIndices.Contains(index));
            }
        }

        private static bool IsFormationAuthorized(
            NetworkCommunicator networkPeer,
            Team team,
            int formationIndex)
        {
            if (!TryGetPeerKey(networkPeer, out int key) || team == null)
                return false;

            lock (CommanderDeploymentSelectionLock)
            {
                if (!CommanderDeploymentSelectionsByPeer.TryGetValue(key, out CommanderDeploymentSelectionState state) ||
                    state.TeamIndex != team.TeamIndex)
                {
                    return false;
                }

                return state.AuthorizedFormationIndices == null ||
                       state.AuthorizedFormationIndices.Contains(formationIndex);
            }
        }

        private static CommanderDeploymentSelectionState GetOrCreateShadowSelectionState(int key, Team team)
        {
            if (!CommanderDeploymentSelectionsByPeer.TryGetValue(key, out CommanderDeploymentSelectionState state) ||
                state.TeamIndex != team.TeamIndex)
            {
                state = new CommanderDeploymentSelectionState(team.TeamIndex);
                CommanderDeploymentSelectionsByPeer[key] = state;
            }

            return state;
        }

        private static bool TryGetPeerKey(NetworkCommunicator peer, out int key)
        {
            key = peer == null ? -1 : peer.Index;
            return key >= 0;
        }

        private static string BuildShadowSelectionSummary(NetworkCommunicator peer, Team team)
        {
            int[] indices = GetShadowSelectedFormationIndices(peer, team);
            if (indices.Length <= 0)
                return string.Empty;

            var parts = new List<string>();
            foreach (int index in indices)
            {
                Formation formation = ResolveFormation(team, index);
                parts.Add(FormatFormation(formation));
            }

            return string.Join(",", parts.ToArray());
        }

        private static void LogOrderDiagnostics(
            string stage,
            NetworkCommunicator peer,
            OrderController orderController,
            string detail)
        {
            if (!IsOrderOfBattleDiagnosticsEnabled())
                return;

            try
            {
                ModLogger.Info(
                    "CommanderDeploymentMissionNetworkComponentPatch: order diagnostics. " +
                    "Stage=" + (stage ?? string.Empty) +
                    " Peer=" + (peer?.UserName ?? peer?.Index.ToString() ?? "<null>") +
                    " Detail=" + (detail ?? string.Empty) +
                    " Selected=[" + BuildSelectedFormationSummary(orderController) + "]");
            }
            catch
            {
            }
        }

        private static bool IsOrderOfBattleDiagnosticsEnabled()
        {
            try
            {
                string value = Environment.GetEnvironmentVariable("COOPSPECTATOR_OOB_DIAGNOSTICS");
                return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string BuildSelectedFormationSummary(OrderController orderController)
        {
            if (orderController?.SelectedFormations == null)
                return "<null>";

            var parts = new List<string>();
            foreach (Formation formation in orderController.SelectedFormations)
                parts.Add(FormatFormation(formation));

            return parts.Count <= 0 ? string.Empty : string.Join(",", parts.ToArray());
        }

        private static string FormatFormation(Formation formation)
        {
            if (formation == null)
                return "<null>";

            return "#" + formation.Index +
                "/team=" + (formation.Team?.TeamIndex.ToString() ?? "<null>") +
                "/side=" + (formation.Team?.Side.ToString() ?? "<null>") +
                "/count=" + formation.CountOfUnits;
        }

        private static string FormatTeam(Team team)
        {
            if (team == null)
                return "<null>";

            return "#" + team.TeamIndex + "/side=" + team.Side;
        }

        private static string FormatSiegeWeapon(SiegeWeapon siegeWeapon)
        {
            if (siegeWeapon == null)
                return "<null>";

            return "Id=" + siegeWeapon.Id +
                "/side=" + siegeWeapon.Side +
                "/disabled=" + siegeWeapon.IsDisabled +
                "/type=" + (siegeWeapon.GetSiegeEngineType()?.StringId ?? "<null>");
        }

        private static bool IsFormationSelected(OrderController orderController, Formation formation)
        {
            if (orderController?.SelectedFormations == null || formation == null)
                return false;

            foreach (Formation selectedFormation in orderController.SelectedFormations)
            {
                if (ReferenceEquals(selectedFormation, formation))
                    return true;
            }

            return false;
        }

        private sealed class CommanderDeploymentSelectionState
        {
            public CommanderDeploymentSelectionState(int teamIndex)
            {
                TeamIndex = teamIndex;
                FormationIndices = new HashSet<int>();
            }

            public int TeamIndex { get; }
            public HashSet<int> FormationIndices { get; }
            public HashSet<int> AuthorizedFormationIndices { get; set; }
        }
    }
}
