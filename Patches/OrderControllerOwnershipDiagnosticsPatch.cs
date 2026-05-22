using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Server-side diagnostics for native order-controller ownership during
    /// exact campaign battles. The current crash signature survives after the
    /// official battle shell is quiesced, so the next most likely owner is the
    /// commander/general handoff and its selected-formation state.
    /// </summary>
    public static class OrderControllerOwnershipDiagnosticsPatch
    {
        private static readonly FieldInfo OrderControllerMissionField =
            AccessTools.Field(typeof(OrderController), "_mission");

        private static readonly Dictionary<string, string> LastLogKeyByContext =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static string _lastSuppressedMultiFormationMoveOrderKey;

        public static void Apply(Harmony harmony)
        {
            if (harmony == null)
                throw new ArgumentNullException(nameof(harmony));

            PatchWithPrefix(
                harmony,
                nameof(OrderController.SetOrder),
                new[] { typeof(OrderType) },
                nameof(OrderController_SetOrder_Prefix));
            PatchWithPrefix(
                harmony,
                nameof(OrderController.SetOrderWithPosition),
                new[] { typeof(OrderType), typeof(WorldPosition) },
                nameof(OrderController_SetOrderWithPosition_Prefix));
            PatchWithPrefix(
                harmony,
                nameof(OrderController.SetOrderWithTwoPositions),
                new[] { typeof(OrderType), typeof(WorldPosition), typeof(WorldPosition) },
                nameof(OrderController_SetOrderWithTwoPositions_Prefix));
            PatchWithPostfix(
                harmony,
                nameof(OrderController.SelectAllFormations),
                new[] { typeof(bool) },
                nameof(OrderController_SelectAllFormations_Postfix));
            PatchWithPostfix(
                harmony,
                nameof(OrderController.ClearSelectedFormations),
                Type.EmptyTypes,
                nameof(OrderController_ClearSelectedFormations_Postfix));
        }

        private static void PatchWithPrefix(Harmony harmony, string methodName, Type[] parameterTypes, string prefixName)
        {
            try
            {
                MethodInfo target = typeof(OrderController).GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    parameterTypes,
                    null);
                MethodInfo prefix = typeof(OrderControllerOwnershipDiagnosticsPatch).GetMethod(
                    prefixName,
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (target == null || prefix == null)
                {
                    ModLogger.Info(
                        "OrderControllerOwnershipDiagnosticsPatch: method not found. Method=" +
                        methodName);
                    return;
                }

                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                ModLogger.Info(
                    "OrderControllerOwnershipDiagnosticsPatch: patched OrderController." +
                    methodName +
                    ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "OrderControllerOwnershipDiagnosticsPatch: failed to patch OrderController." +
                    methodName +
                    ". Error=" +
                    ex.GetType().Name +
                    ": " +
                    ex.Message);
            }
        }

        private static void PatchWithPostfix(Harmony harmony, string methodName, Type[] parameterTypes, string postfixName)
        {
            try
            {
                MethodInfo target = typeof(OrderController).GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    parameterTypes,
                    null);
                MethodInfo postfix = typeof(OrderControllerOwnershipDiagnosticsPatch).GetMethod(
                    postfixName,
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (target == null || postfix == null)
                {
                    ModLogger.Info(
                        "OrderControllerOwnershipDiagnosticsPatch: method not found. Method=" +
                        methodName);
                    return;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                ModLogger.Info(
                    "OrderControllerOwnershipDiagnosticsPatch: patched OrderController." +
                    methodName +
                    ".");
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "OrderControllerOwnershipDiagnosticsPatch: failed to patch OrderController." +
                    methodName +
                    ". Error=" +
                    ex.GetType().Name +
                    ": " +
                    ex.Message);
            }
        }

        private static void OrderController_SetOrder_Prefix(OrderController __instance, OrderType orderType)
        {
            TryLogOrderControllerState(
                "SetOrder",
                __instance,
                "OrderType=" + orderType);
        }

        private static void OrderController_SetOrderWithPosition_Prefix(
            OrderController __instance,
            OrderType orderType,
            WorldPosition orderPosition)
        {
            TryLogOrderControllerState(
                "SetOrderWithPosition",
                __instance,
                "OrderType=" + orderType +
                " Position=" + BuildWorldPositionSummary(orderPosition));
        }

        private static bool OrderController_SetOrderWithTwoPositions_Prefix(
            OrderController __instance,
            OrderType orderType,
            WorldPosition position1,
            WorldPosition position2)
        {
            if (ShouldSuppressMultiFormationTwoPositionOrderForDiagnostic(
                    __instance,
                    orderType,
                    position1,
                    position2,
                    out string suppressionLogKey,
                    out string suppressionLogDetails))
            {
                if (!string.Equals(_lastSuppressedMultiFormationMoveOrderKey, suppressionLogKey, StringComparison.Ordinal))
                {
                    _lastSuppressedMultiFormationMoveOrderKey = suppressionLogKey;
                    ModLogger.Info(
                        "OrderControllerOwnershipDiagnosticsPatch: A/B suppressed multi-formation " +
                        "OrderController.SetOrderWithTwoPositions before native execution. " +
                        suppressionLogDetails);
                }

                TryLogOrderControllerState(
                    "SetOrderWithTwoPositionsSuppressed",
                    __instance,
                    suppressionLogDetails);
                return false;
            }

            TryLogOrderControllerState(
                "SetOrderWithTwoPositions",
                __instance,
                "OrderType=" + orderType +
                " Position1=" + BuildWorldPositionSummary(position1) +
                " Position2=" + BuildWorldPositionSummary(position2));
            return true;
        }

        private static void OrderController_SelectAllFormations_Postfix(OrderController __instance, bool uiFeedback)
        {
            TryLogOrderControllerState(
                "SelectAllFormations",
                __instance,
                "UiFeedback=" + uiFeedback);
        }

        private static void OrderController_ClearSelectedFormations_Postfix(OrderController __instance)
        {
            TryLogOrderControllerState(
                "ClearSelectedFormations",
                __instance,
                string.Empty);
        }

        private static void TryLogOrderControllerState(string context, OrderController orderController, string details)
        {
            if (!ShouldTrace(orderController, out Mission mission, out Team team, out CoopBattlePhase phase, out string reason))
                return;

            try
            {
                string orderControllerIdentity = BuildOrderControllerIdentity(orderController, team);
                string selectedFormationSummary = BuildSelectedFormationSummary(orderController);
                string teamFormationSummary = BuildTeamFormationSummary(team);
                string teamPeerSummary = BuildTeamPeerSummary(team);
                string key =
                    context + "|" +
                    (mission.SceneName ?? "null") + "|" +
                    phase + "|" +
                    orderControllerIdentity + "|" +
                    selectedFormationSummary + "|" +
                    teamFormationSummary + "|" +
                    teamPeerSummary + "|" +
                    (details ?? string.Empty);
                if (LastLogKeyByContext.TryGetValue(context, out string previousKey) &&
                    string.Equals(previousKey, key, StringComparison.Ordinal))
                {
                    return;
                }

                LastLogKeyByContext[context] = key;
                ModLogger.Info(
                    "OrderControllerOwnershipDiagnosticsPatch: observed OrderController." +
                    context +
                    ". Scene=" + (mission.SceneName ?? "null") +
                    " Phase=" + phase +
                    " Reason=" + reason +
                    " " + orderControllerIdentity +
                    " " + (string.IsNullOrWhiteSpace(details) ? string.Empty : details + " ") +
                    "SelectedFormations=[" + selectedFormationSummary + "] " +
                    "TeamFormations=[" + teamFormationSummary + "] " +
                    "TeamPeers=[" + teamPeerSummary + "]");
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "OrderControllerOwnershipDiagnosticsPatch: failed to log OrderController." +
                    context +
                    ". Error=" +
                    ex.GetType().Name +
                    ": " +
                    ex.Message);
            }
        }

        private static bool ShouldTrace(
            OrderController orderController,
            out Mission mission,
            out Team team,
            out CoopBattlePhase phase,
            out string reason)
        {
            mission = ResolveMission(orderController);
            team = orderController?.Team;
            phase = CoopBattlePhaseRuntimeState.GetPhase();

            if (!GameNetwork.IsServer)
            {
                reason = "NotServer";
                return false;
            }

            if (orderController == null)
            {
                reason = "OrderController=null";
                return false;
            }

            if (mission == null)
            {
                reason = "Mission=null";
                return false;
            }

            if (team == null || team.Side == BattleSideEnum.None || ReferenceEquals(team, mission.SpectatorTeam))
            {
                reason = "TeamInvalid";
                return false;
            }

            string sceneName = mission.SceneName ?? string.Empty;
            if (!SceneRuntimeClassifier.IsCampaignBattleScene(sceneName))
            {
                reason = "SceneNotCampaignBattle";
                return false;
            }

            if (phase < CoopBattlePhase.PreBattleHold || phase >= CoopBattlePhase.BattleEnded)
            {
                reason = "PhaseOutsideOwnership";
                return false;
            }

            reason = "Tracing";
            return true;
        }

        private static bool ShouldSuppressMultiFormationTwoPositionOrderForDiagnostic(
            OrderController orderController,
            OrderType orderType,
            WorldPosition position1,
            WorldPosition position2,
            out string logKey,
            out string logDetails)
        {
            logKey = null;
            logDetails = null;

            if (!ShouldTrace(orderController, out Mission mission, out Team team, out CoopBattlePhase phase, out string reason))
                return false;

            if (phase < CoopBattlePhase.BattleActive)
                return false;

            if (orderType != OrderType.MoveToLineSegment &&
                orderType != OrderType.MoveToLineSegmentWithHorizontalLayout)
            {
                return false;
            }

            if (team == null || !team.IsPlayerGeneral || team.IsPlayerSergeant)
                return false;

            if (!ReferenceEquals(orderController, team.PlayerOrderController))
                return false;

            int selectedFormationCount = orderController.SelectedFormations?.Count ?? 0;
            if (selectedFormationCount <= 1)
                return false;

            string selectedFormationSummary = BuildSelectedFormationSummary(orderController);
            logKey =
                (mission.SceneName ?? "null") + "|" +
                phase + "|" +
                orderType + "|" +
                selectedFormationCount + "|" +
                selectedFormationSummary + "|" +
                BuildWorldPositionSummary(position1) + "|" +
                BuildWorldPositionSummary(position2);
            logDetails =
                "Scene=" + (mission.SceneName ?? "null") +
                " Phase=" + phase +
                " Reason=" + reason +
                " OrderType=" + orderType +
                " SelectedFormationCount=" + selectedFormationCount +
                " Position1=" + BuildWorldPositionSummary(position1) +
                " Position2=" + BuildWorldPositionSummary(position2) +
                " SelectedFormations=[" + selectedFormationSummary + "]";
            return true;
        }

        private static Mission ResolveMission(OrderController orderController)
        {
            if (Mission.Current != null)
                return Mission.Current;

            try
            {
                return OrderControllerMissionField?.GetValue(orderController) as Mission;
            }
            catch
            {
                return null;
            }
        }

        private static string BuildOrderControllerIdentity(OrderController orderController, Team team)
        {
            if (orderController == null || team == null)
                return "OrderController=null";

            string kind =
                ReferenceEquals(orderController, team.PlayerOrderController)
                    ? "PlayerOrderController"
                    : "OtherOrderController";

            Agent owner = orderController.Owner;
            OrderController ownerSpecificController = owner != null ? team.GetOrderControllerOf(owner) : null;
            bool isOwnerSpecificController = owner != null && ReferenceEquals(orderController, ownerSpecificController);
            return
                "ControllerKind=" + kind +
                " IsOwnerSpecificController=" + isOwnerSpecificController +
                " TeamIndex=" + team.TeamIndex +
                " TeamSide=" + team.Side +
                " TeamIsPlayerGeneral=" + team.IsPlayerGeneral +
                " TeamIsPlayerSergeant=" + team.IsPlayerSergeant +
                " GeneralAgentIndex=" + (team.GeneralAgent?.Index.ToString() ?? "null") +
                " OrderOwnerIndex=" + (owner?.Index.ToString() ?? "null") +
                " PlayerOrderOwnerIndex=" + (team.PlayerOrderController?.Owner?.Index.ToString() ?? "null") +
                " SelectedFormationCount=" + (orderController.SelectedFormations?.Count.ToString() ?? "0");
        }

        private static string BuildSelectedFormationSummary(OrderController orderController)
        {
            if (orderController?.SelectedFormations == null || orderController.SelectedFormations.Count == 0)
                return "none";

            StringBuilder summary = new StringBuilder();
            foreach (Formation formation in orderController.SelectedFormations)
            {
                AppendFormationSummary(summary, formation);
            }

            return summary.Length == 0 ? "none" : summary.ToString();
        }

        private static string BuildTeamFormationSummary(Team team)
        {
            if (team?.FormationsIncludingEmpty == null)
                return "none";

            StringBuilder summary = new StringBuilder();
            foreach (Formation formation in team.FormationsIncludingEmpty)
            {
                if (formation == null)
                    continue;

                bool hasPlayerSignals =
                    formation.CountOfUnits > 0 ||
                    formation.PlayerOwner != null ||
                    formation.Captain != null ||
                    formation.HasPlayerControlledTroop ||
                    formation.IsPlayerTroopInFormation ||
                    !formation.IsAIControlled;
                if (!hasPlayerSignals)
                    continue;

                AppendFormationSummary(summary, formation);
            }

            return summary.Length == 0 ? "none" : summary.ToString();
        }

        private static void AppendFormationSummary(StringBuilder summary, Formation formation)
        {
            if (summary == null)
                return;

            if (summary.Length > 0)
                summary.Append("; ");

            if (formation == null)
            {
                summary.Append("null");
                return;
            }

            summary.Append("Idx=").Append((int)formation.FormationIndex);
            summary.Append("/").Append(formation.FormationIndex);
            summary.Append(" Units=").Append(formation.CountOfUnits);
            summary.Append(" AI=").Append(formation.IsAIControlled);
            summary.Append(" Owner=").Append(formation.PlayerOwner?.Index.ToString() ?? "null");
            summary.Append(" Captain=").Append(formation.Captain?.Index.ToString() ?? "null");
            summary.Append(" HasCtrl=").Append(formation.HasPlayerControlledTroop);
            summary.Append(" IsPlayerIn=").Append(formation.IsPlayerTroopInFormation);
            summary.Append(" Class=").Append(formation.RepresentativeClass);
        }

        private static string BuildTeamPeerSummary(Team team)
        {
            if (team == null || GameNetwork.NetworkPeers == null)
                return "none";

            StringBuilder summary = new StringBuilder();
            foreach (NetworkCommunicator peer in GameNetwork.NetworkPeers)
            {
                if (peer == null || peer.IsServerPeer)
                    continue;

                MissionPeer missionPeer = peer.GetComponent<MissionPeer>();
                if (missionPeer == null || !ReferenceEquals(missionPeer.Team, team))
                    continue;

                if (summary.Length > 0)
                    summary.Append("; ");

                summary.Append("Peer=").Append(peer.UserName ?? peer.Index.ToString());
                summary.Append(" Team=").Append(missionPeer.Team?.TeamIndex.ToString() ?? "null");
                summary.Append(" ControlledAgent=").Append(missionPeer.ControlledAgent?.Index.ToString() ?? "null");
                summary.Append(" ControlledFormation=").Append(missionPeer.ControlledFormation?.FormationIndex.ToString() ?? "null");
                summary.Append(" SelectedTroopIndex=").Append(missionPeer.SelectedTroopIndex);
                summary.Append(" HasSpawnedVisuals=").Append(missionPeer.HasSpawnedAgentVisuals);
            }

            return summary.Length == 0 ? "none" : summary.ToString();
        }

        private static string BuildWorldPositionSummary(WorldPosition position)
        {
            try
            {
                if (!position.IsValid)
                    return "invalid";

                return position.GetGroundVec3().ToString();
            }
            catch
            {
                return position.ToString();
            }
        }
    }
}
