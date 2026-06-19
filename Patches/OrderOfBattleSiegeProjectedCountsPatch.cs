using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.Infrastructure;
using CoopSpectator.MissionBehaviors;
using CoopSpectator.Network.Messages;
using CoopSpectator.UI;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.ViewModelCollection.OrderOfBattle;

namespace CoopSpectator.Patches
{
    public static class OrderOfBattleSiegeProjectedCountsPatch
    {
        private static readonly FieldInfo VisibleTroopTypeCountLookupField =
            typeof(OrderOfBattleVM).GetField("_visibleTroopTypeCountLookup", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo AllFormationsField =
            typeof(OrderOfBattleVM).GetField("_allFormations", BindingFlags.Instance | BindingFlags.NonPublic);

        private static string _lastSentCommanderDeploymentFormationAssignmentsKey = string.Empty;

        public static void Apply(Harmony harmony)
        {
            PatchTotalCountOfUnitsInClass(harmony);
            PatchIsAgentInFormationClass(harmony);
            PatchMassTransferData(harmony);
            PatchVisibleTroopTypeLookup(harmony);
            PatchRearrangeFormationsAccordingToFilters(harmony);
        }

        private static Type GetOrderOfBattleUIHelperType()
        {
            return typeof(OrderOfBattleVM).Assembly.GetType(
                "TaleWorlds.MountAndBlade.ViewModelCollection.OrderOfBattle.OrderOfBattleUIHelper");
        }

        private static void PatchTotalCountOfUnitsInClass(Harmony harmony)
        {
            Type helperType = GetOrderOfBattleUIHelperType();
            MethodInfo target = helperType == null
                ? null
                : AccessTools.Method(
                    helperType,
                    "GetTotalCountOfUnitsInClass",
                    new[] { typeof(Formation), typeof(FormationClass) });
            MethodInfo prefix = typeof(OrderOfBattleSiegeProjectedCountsPatch).GetMethod(
                nameof(OrderOfBattleUIHelper_GetTotalCountOfUnitsInClass_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);

            if (target == null || prefix == null)
            {
                ModLogger.Info("OrderOfBattleSiegeProjectedCountsPatch: OrderOfBattleUIHelper.GetTotalCountOfUnitsInClass not found. Skip.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("OrderOfBattleSiegeProjectedCountsPatch: prefix applied to OrderOfBattleUIHelper.GetTotalCountOfUnitsInClass.");
        }

        private static void PatchIsAgentInFormationClass(Harmony harmony)
        {
            Type helperType = GetOrderOfBattleUIHelperType();
            MethodInfo target = helperType == null
                ? null
                : AccessTools.Method(
                    helperType,
                    "IsAgentInFormationClass",
                    new[] { typeof(Agent), typeof(FormationClass) });
            MethodInfo prefix = typeof(OrderOfBattleSiegeProjectedCountsPatch).GetMethod(
                nameof(OrderOfBattleUIHelper_IsAgentInFormationClass_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);

            if (target == null || prefix == null)
            {
                ModLogger.Info("OrderOfBattleSiegeProjectedCountsPatch: OrderOfBattleUIHelper.IsAgentInFormationClass not found. Skip.");
                return;
            }

            harmony.Patch(target, prefix: new HarmonyMethod(prefix));
            ModLogger.Info("OrderOfBattleSiegeProjectedCountsPatch: prefix applied to OrderOfBattleUIHelper.IsAgentInFormationClass.");
        }

        private static void PatchMassTransferData(Harmony harmony)
        {
            Type helperType = GetOrderOfBattleUIHelperType();
            MethodInfo classTarget = helperType == null
                ? null
                : AccessTools.Method(
                    helperType,
                    "CreateMassTransferData",
                    new[] { typeof(OrderOfBattleFormationClassVM), typeof(FormationClass), typeof(TroopTraitsMask), typeof(int) });
            MethodInfo itemTarget = helperType == null
                ? null
                : AccessTools.Method(
                    helperType,
                    "CreateMassTransferData",
                    new[] { typeof(OrderOfBattleFormationItemVM), typeof(FormationClass), typeof(TroopTraitsMask), typeof(int) });
            MethodInfo classPrefix = typeof(OrderOfBattleSiegeProjectedCountsPatch).GetMethod(
                nameof(OrderOfBattleUIHelper_CreateMassTransferData_Class_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo itemPrefix = typeof(OrderOfBattleSiegeProjectedCountsPatch).GetMethod(
                nameof(OrderOfBattleUIHelper_CreateMassTransferData_Item_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);

            if (classTarget == null || itemTarget == null || classPrefix == null || itemPrefix == null)
            {
                ModLogger.Info("OrderOfBattleSiegeProjectedCountsPatch: OrderOfBattleUIHelper.CreateMassTransferData overloads not found. Skip.");
                return;
            }

            harmony.Patch(classTarget, prefix: new HarmonyMethod(classPrefix));
            harmony.Patch(itemTarget, prefix: new HarmonyMethod(itemPrefix));
            ModLogger.Info("OrderOfBattleSiegeProjectedCountsPatch: prefixes applied to OrderOfBattleUIHelper.CreateMassTransferData overloads.");
        }

        private static void PatchVisibleTroopTypeLookup(Harmony harmony)
        {
            MethodInfo target = typeof(OrderOfBattleVM).GetMethod(
                "UpdateTroopTypeLookUpTable",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo postfix = typeof(OrderOfBattleSiegeProjectedCountsPatch).GetMethod(
                nameof(OrderOfBattleVM_UpdateTroopTypeLookUpTable_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);

            if (target == null || postfix == null || VisibleTroopTypeCountLookupField == null || AllFormationsField == null)
            {
                ModLogger.Info("OrderOfBattleSiegeProjectedCountsPatch: OrderOfBattleVM.UpdateTroopTypeLookUpTable not found. Skip.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            ModLogger.Info("OrderOfBattleSiegeProjectedCountsPatch: postfix applied to OrderOfBattleVM.UpdateTroopTypeLookUpTable.");
        }

        private static void PatchRearrangeFormationsAccordingToFilters(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(
                typeof(OrderController),
                "RearrangeFormationsAccordingToFilters",
                new[]
                {
                    typeof(Team),
                    typeof(List<ValueTuple<Formation, int, TroopTraitsMask, List<Agent>>>)
                });
            MethodInfo postfix = typeof(OrderOfBattleSiegeProjectedCountsPatch).GetMethod(
                nameof(OrderController_RearrangeFormationsAccordingToFilters_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);

            if (target == null || postfix == null)
            {
                ModLogger.Info("OrderOfBattleSiegeProjectedCountsPatch: OrderController.RearrangeFormationsAccordingToFilters not found. Skip.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            ModLogger.Info("OrderOfBattleSiegeProjectedCountsPatch: postfix applied to OrderController.RearrangeFormationsAccordingToFilters.");
        }

        private static bool OrderOfBattleUIHelper_GetTotalCountOfUnitsInClass_Prefix(
            Formation formation,
            FormationClass fc,
            ref int __result)
        {
            try
            {
                if (!ShouldProjectSiegeOrderOfBattleCounts() ||
                    formation == null ||
                    !IsDefaultFormationClass(fc))
                {
                    return true;
                }

                __result = CountProjectedUnitsInClass(formation, fc);
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool OrderOfBattleUIHelper_IsAgentInFormationClass_Prefix(
            Agent agent,
            FormationClass fc,
            ref bool __result)
        {
            try
            {
                if (!ShouldProjectSiegeOrderOfBattleCounts() ||
                    agent == null ||
                    !IsDefaultFormationClass(fc))
                {
                    return true;
                }

                __result = CoopMissionSelectionView.IsCommanderDeploymentProjectedAgentInFormationClass(agent, fc);
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool OrderOfBattleUIHelper_CreateMassTransferData_Class_Prefix(
            OrderOfBattleFormationClassVM affectedClass,
            FormationClass formationClass,
            TroopTraitsMask filter,
            int unitCount,
            ref ValueTuple<Formation, int, TroopTraitsMask, List<Agent>> __result)
        {
            try
            {
                if (!ShouldProjectSiegeOrderOfBattleCounts())
                    return true;

                OrderOfBattleFormationItemVM formationItem = affectedClass?.BelongedFormationItem;
                return !TryCreateProjectedMassTransferData(formationItem, formationClass, filter, unitCount, ref __result);
            }
            catch
            {
                return true;
            }
        }

        private static bool OrderOfBattleUIHelper_CreateMassTransferData_Item_Prefix(
            OrderOfBattleFormationItemVM affectedFormation,
            FormationClass formationClass,
            TroopTraitsMask filter,
            int unitCount,
            ref ValueTuple<Formation, int, TroopTraitsMask, List<Agent>> __result)
        {
            try
            {
                if (!ShouldProjectSiegeOrderOfBattleCounts())
                    return true;

                return !TryCreateProjectedMassTransferData(affectedFormation, formationClass, filter, unitCount, ref __result);
            }
            catch
            {
                return true;
            }
        }

        private static bool TryCreateProjectedMassTransferData(
            OrderOfBattleFormationItemVM formationItem,
            FormationClass formationClass,
            TroopTraitsMask filter,
            int unitCount,
            ref ValueTuple<Formation, int, TroopTraitsMask, List<Agent>> result)
        {
            if (formationItem?.Formation == null)
                return false;

            FormationClass projectedClass = DismountSiegeFormationClass(formationClass.FallbackClass());
            if (projectedClass != FormationClass.Infantry && projectedClass != FormationClass.Ranged)
                return false;

            List<Agent> excludedAgents = GetProjectedExcludedAgentsForTransfer(formationItem, projectedClass);
            result = new ValueTuple<Formation, int, TroopTraitsMask, List<Agent>>(
                formationItem.Formation,
                unitCount,
                filter,
                excludedAgents);
            return true;
        }

        private static List<Agent> GetProjectedExcludedAgentsForTransfer(
            OrderOfBattleFormationItemVM formationItem,
            FormationClass projectedClass)
        {
            var excludedAgents = new List<Agent>();
            if (formationItem?.Formation == null)
                return excludedAgents;

            try
            {
                if (formationItem.HasCaptain && formationItem.Captain?.Agent != null)
                    AddDistinctAgent(excludedAgents, formationItem.Captain.Agent);
            }
            catch
            {
            }

            try
            {
                if (formationItem.HeroTroops != null)
                {
                    foreach (OrderOfBattleHeroItemVM heroTroop in formationItem.HeroTroops)
                    {
                        if (heroTroop?.Agent != null)
                            AddDistinctAgent(excludedAgents, heroTroop.Agent);
                    }
                }
            }
            catch
            {
            }

            try
            {
                foreach (Agent unit in formationItem.Formation.Arrangement.GetAllUnits())
                {
                    if (unit == null)
                        continue;

                    if (unit.Banner != null ||
                        !CoopMissionSelectionView.IsCommanderDeploymentProjectedAgentInFormationClass(unit, projectedClass))
                    {
                        AddDistinctAgent(excludedAgents, unit);
                    }
                }
            }
            catch
            {
            }

            return excludedAgents;
        }

        private static void AddDistinctAgent(List<Agent> agents, Agent agent)
        {
            if (agents == null || agent == null || agents.Contains(agent))
                return;

            agents.Add(agent);
        }

        private static void OrderOfBattleVM_UpdateTroopTypeLookUpTable_Postfix(OrderOfBattleVM __instance)
        {
            try
            {
                if (!ShouldProjectSiegeOrderOfBattleCounts())
                    return;

                if (!(VisibleTroopTypeCountLookupField.GetValue(__instance) is IDictionary<FormationClass, int> lookup) ||
                    !(AllFormationsField.GetValue(__instance) is IEnumerable allFormations))
                {
                    return;
                }

                var formationItems = new List<OrderOfBattleFormationItemVM>();
                int infantryCount = 0;
                int rangedCount = 0;
                foreach (object item in allFormations)
                {
                    if (!(item is OrderOfBattleFormationItemVM formationItem) || formationItem.Formation == null)
                        continue;

                    formationItems.Add(formationItem);
                    infantryCount += CountProjectedUnitsInClass(formationItem.Formation, FormationClass.Infantry);
                    rangedCount += CountProjectedUnitsInClass(formationItem.Formation, FormationClass.Ranged);
                }

                lookup[FormationClass.Infantry] = infantryCount;
                lookup[FormationClass.Ranged] = rangedCount;
                lookup[FormationClass.Cavalry] = infantryCount;
                lookup[FormationClass.HorseArcher] = rangedCount;

                foreach (OrderOfBattleFormationItemVM formationItem in formationItems)
                    formationItem.OnSizeChanged();
            }
            catch (Exception ex)
            {
                ModLogger.Info("OrderOfBattleSiegeProjectedCountsPatch: visible troop lookup projection failed open: " + ex.Message);
            }
        }

        private static void OrderController_RearrangeFormationsAccordingToFilters_Postfix(
            OrderController __instance,
            Team team)
        {
            try
            {
                if (!ShouldProjectSiegeOrderOfBattleCounts() ||
                    !GameNetwork.IsClient ||
                    !GameNetwork.IsSessionActive)
                {
                    return;
                }

                Team targetTeam = team ?? __instance?.Team;
                if (targetTeam == null || targetTeam.Side == BattleSideEnum.None)
                    return;

                if (!TryBuildCommanderDeploymentFormationAssignmentPayload(
                        targetTeam,
                        out byte[] assignmentBytes,
                        out string assignmentKey))
                {
                    return;
                }

                if (string.Equals(
                        _lastSentCommanderDeploymentFormationAssignmentsKey,
                        assignmentKey,
                        StringComparison.Ordinal))
                {
                    return;
                }

                if (CoopBattleNetworkRequestTransport.TrySyncCommanderDeploymentFormationAssignments(
                        targetTeam.Side,
                        assignmentBytes,
                        "OrderOfBattleSiegeProjectedCountsPatch.RearrangeFormationsAccordingToFilters"))
                {
                    _lastSentCommanderDeploymentFormationAssignmentsKey = assignmentKey;
                }
            }
            catch (Exception ex)
            {
                if (CoopDebugConfig.OrderOfBattleDiagnostics)
                {
                    ModLogger.Info(
                        "OrderOfBattleSiegeProjectedCountsPatch: commander deployment formation assignment sync failed open: " +
                        ex.GetType().Name + ":" + ex.Message);
                }
            }
        }

        private static bool TryBuildCommanderDeploymentFormationAssignmentPayload(
            Team team,
            out byte[] assignmentBytes,
            out string assignmentKey)
        {
            assignmentBytes = Array.Empty<byte>();
            assignmentKey = string.Empty;

            if (team == null)
                return false;

            Mission mission = Mission.Current;
            if (mission?.AllAgents == null)
                return false;

            var assignments = new List<ValueTuple<int, int>>();
            for (int i = 0; i < mission.AllAgents.Count; i++)
            {
                Agent agent = mission.AllAgents[i];
                Formation formation = agent?.Formation;
                if (agent == null ||
                    formation == null ||
                    agent.Index < 0 ||
                    agent.Index > ushort.MaxValue ||
                    agent.IsMount ||
                    !agent.IsActive() ||
                    !ReferenceEquals(agent.Team, team) ||
                    !ReferenceEquals(formation.Team, team) ||
                    formation.Index < 0 ||
                    formation.Index >= (int)FormationClass.NumberOfDefaultFormations)
                {
                    continue;
                }

                assignments.Add(new ValueTuple<int, int>(agent.Index, formation.Index));
            }

            if (assignments.Count <= 0)
                return false;

            int maxAssignments =
                CoopCommanderDeploymentFormationAssignmentsMessage.MaxAssignmentBytes /
                CoopCommanderDeploymentFormationAssignmentsMessage.BytesPerAssignment;
            if (assignments.Count > maxAssignments)
            {
                if (CoopDebugConfig.OrderOfBattleDiagnostics)
                {
                    ModLogger.Info(
                        "OrderOfBattleSiegeProjectedCountsPatch: commander deployment formation assignment payload skipped because it is too large. " +
                        "Assignments=" + assignments.Count +
                        " MaxAssignments=" + maxAssignments +
                        " TeamIndex=" + team.TeamIndex +
                        " Side=" + team.Side);
                }

                return false;
            }

            assignments.Sort((left, right) => left.Item1.CompareTo(right.Item1));
            assignmentBytes = new byte[assignments.Count * CoopCommanderDeploymentFormationAssignmentsMessage.BytesPerAssignment];
            int offset = 0;
            foreach (ValueTuple<int, int> assignment in assignments)
            {
                int agentIndex = assignment.Item1;
                int formationIndex = assignment.Item2;
                assignmentBytes[offset++] = (byte)(agentIndex & 0xFF);
                assignmentBytes[offset++] = (byte)((agentIndex >> 8) & 0xFF);
                assignmentBytes[offset++] = (byte)(formationIndex & 0xFF);
            }

            assignmentKey =
                team.TeamIndex +
                "|" +
                team.Side +
                "|" +
                Convert.ToBase64String(assignmentBytes);
            return true;
        }

        private static bool ShouldProjectSiegeOrderOfBattleCounts()
        {
            return CoopMissionSelectionView.IsCommanderDeploymentOrderOfBattleActive();
        }

        private static int CountProjectedUnitsInClass(Formation formation, FormationClass formationClass)
        {
            if (formation == null)
                return 0;

            FormationClass projectedClass = DismountSiegeFormationClass(formationClass.FallbackClass());
            if (projectedClass != FormationClass.Infantry && projectedClass != FormationClass.Ranged)
                return 0;

            return formation.GetCountOfUnitsWithCondition(agent =>
                CoopMissionSelectionView.IsCommanderDeploymentProjectedAgentInFormationClass(agent, projectedClass));
        }

        private static FormationClass DismountSiegeFormationClass(FormationClass formationClass)
        {
            if (formationClass == FormationClass.Cavalry)
                return FormationClass.Infantry;

            if (formationClass == FormationClass.HorseArcher)
                return FormationClass.Ranged;

            return formationClass;
        }

        private static bool IsDefaultFormationClass(FormationClass formationClass)
        {
            return formationClass >= FormationClass.Infantry &&
                   formationClass < FormationClass.NumberOfDefaultFormations;
        }
    }
}
