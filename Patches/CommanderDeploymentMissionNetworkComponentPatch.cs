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
                if (!TryResolveOrderLease(networkPeer, out _, out OrderController orderController))
                    return true;

                ApplyOrderWithTwoPositions message = baseMessage as ApplyOrderWithTwoPositions;
                Mission mission = Mission.Current;
                if (message == null || mission?.Scene == null || orderController == null)
                    return true;

                bool previousTeleportingAgents = mission.IsTeleportingAgents;
                try
                {
                    mission.IsTeleportingAgents = true;
                    orderController.SetOrderWithTwoPositions(
                        message.OrderType,
                        new WorldPosition(mission.Scene, UIntPtr.Zero, message.Position1, hasValidZ: false),
                        new WorldPosition(mission.Scene, UIntPtr.Zero, message.Position2, hasValidZ: false));
                    ForceCommanderDeploymentPositioning(orderController);
                }
                finally
                {
                    mission.IsTeleportingAgents = previousTeleportingAgents;
                }

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
                    orderController.SelectFormation(formation);

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
                if (formation != null && IsFormationSelected(orderController, formation))
                    orderController.DeselectFormation(formation);

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

            foreach (Formation formation in selectedFormations)
            {
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
                    formation.Index == formationIndex &&
                    formation.CountOfUnits > 0)
                {
                    return formation;
                }
            }

            return null;
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
    }
}
