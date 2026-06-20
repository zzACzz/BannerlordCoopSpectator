using System;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using HarmonyLib;
using NetworkMessages.FromClient;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.Network.Messages;

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
                "HandleClientEventSelectFormation",
                nameof(MissionNetworkComponent_HandleClientEventSelectFormation_Prefix),
                new[] { typeof(NetworkCommunicator), typeof(GameNetworkMessage) });
            PatchPrivateMethod(
                harmony,
                "HandleClientEventUnselectFormation",
                nameof(MissionNetworkComponent_HandleClientEventUnselectFormation_Prefix),
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
            if (!CoopMissionSpawnLogic.TryResolveCommanderDeploymentOrderLease(
                    networkPeer,
                    out Team team,
                    out _,
                    out _))
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
            if (!CoopMissionSpawnLogic.TryResolveCommanderDeploymentOrderLease(
                    networkPeer,
                    out _,
                    out OrderController orderController,
                    out _))
            {
                return true;
            }

            __result = orderController;
            return false;
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
                if (!TryResolveOrderLease(networkPeer, out Team team, out OrderController orderController))
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
                    mission.IsTeleportingAgents = true;
                    var position1 = new WorldPosition(mission.Scene, UIntPtr.Zero, message.Position1, hasValidZ: false);
                    var position2 = new WorldPosition(mission.Scene, UIntPtr.Zero, message.Position2, hasValidZ: false);
                    bool nativeSelectionMatchesShadow = !shouldUseShadowSelection ||
                        IsNativeSelectionEquivalentToShadow(orderController, shadowFormations);
                    if (nativeSelectionMatchesShadow)
                    {
                        orderController.SetOrderWithTwoPositions(
                            message.OrderType,
                            position1,
                            position2);
                        ForceCommanderDeploymentPositioning(orderController);
                    }
                    else if (!TryApplyShadowMoveToLineSegment(
                                 orderController,
                                 shadowFormations,
                                 message.OrderType,
                                 position1,
                                 position2))
                    {
                        orderController.SetOrderWithTwoPositions(
                            message.OrderType,
                            position1,
                            position2);
                        ForceCommanderDeploymentPositioning(orderController);
                    }
                }
                finally
                {
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

        private static bool TryResolveOrderLease(
            NetworkCommunicator networkPeer,
            out Team team,
            out OrderController orderController)
        {
            return CoopMissionSpawnLogic.TryResolveCommanderDeploymentOrderLease(
                networkPeer,
                out team,
                out orderController,
                out _);
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

        private static bool TryApplyShadowMoveToLineSegment(
            OrderController orderController,
            List<Formation> shadowFormations,
            OrderType orderType,
            WorldPosition position1,
            WorldPosition position2)
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

                int[] result = new int[state.FormationIndices.Count];
                state.FormationIndices.CopyTo(result);
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
                    if (formation != null && formation.CountOfUnits > 0)
                        state.FormationIndices.Add(formation.Index);
                }
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
        }
    }
}
