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
using TaleWorlds.Engine;
using TaleWorlds.Library;
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
        private static bool _isApplyingProjectedWeightDistribution;

        public static void Apply(Harmony harmony)
        {
            PatchTotalCountOfUnitsInClass(harmony);
            PatchIsAgentInFormationClass(harmony);
            PatchProjectedClassMatching(harmony);
            PatchMassTransferData(harmony);
            PatchVisibleTroopTypeLookup(harmony);
            PatchRearrangeFormationsAccordingToFilters(harmony);
            PatchOrderOfBattleWeightAdjusted(harmony);
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

        private static void PatchProjectedClassMatching(Harmony harmony)
        {
            Type helperType = GetOrderOfBattleUIHelperType();
            MethodInfo matchingClassesTarget = helperType == null
                ? null
                : AccessTools.Method(
                    helperType,
                    "GetMatchingClasses",
                    new[]
                    {
                        typeof(List<OrderOfBattleFormationItemVM>),
                        typeof(OrderOfBattleFormationClassVM),
                        typeof(Func<OrderOfBattleFormationClassVM, bool>)
                    });
            MethodInfo matchingClassesPrefix = typeof(OrderOfBattleSiegeProjectedCountsPatch).GetMethod(
                nameof(OrderOfBattleUIHelper_GetMatchingClasses_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);

            if (matchingClassesTarget == null || matchingClassesPrefix == null)
            {
                ModLogger.Info("OrderOfBattleSiegeProjectedCountsPatch: OrderOfBattleUIHelper.GetMatchingClasses not found. Skip.");
            }
            else
            {
                harmony.Patch(matchingClassesTarget, prefix: new HarmonyMethod(matchingClassesPrefix));
                ModLogger.Info("OrderOfBattleSiegeProjectedCountsPatch: prefix applied to OrderOfBattleUIHelper.GetMatchingClasses.");
            }

            MethodInfo allClassesTarget = AccessTools.Method(
                typeof(OrderOfBattleVM),
                "GetAllFormationClassesWith",
                new[] { typeof(FormationClass) });
            MethodInfo allClassesPrefix = typeof(OrderOfBattleSiegeProjectedCountsPatch).GetMethod(
                nameof(OrderOfBattleVM_GetAllFormationClassesWith_Prefix),
                BindingFlags.Static | BindingFlags.NonPublic);

            if (allClassesTarget == null || allClassesPrefix == null)
            {
                ModLogger.Info("OrderOfBattleSiegeProjectedCountsPatch: OrderOfBattleVM.GetAllFormationClassesWith not found. Skip.");
                return;
            }

            harmony.Patch(allClassesTarget, prefix: new HarmonyMethod(allClassesPrefix));
            ModLogger.Info("OrderOfBattleSiegeProjectedCountsPatch: prefix applied to OrderOfBattleVM.GetAllFormationClassesWith.");
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

        private static void PatchOrderOfBattleWeightAdjusted(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(
                typeof(OrderOfBattleVM),
                "OnWeightAdjusted",
                new[] { typeof(OrderOfBattleFormationClassVM) });
            MethodInfo postfix = typeof(OrderOfBattleSiegeProjectedCountsPatch).GetMethod(
                nameof(OrderOfBattleVM_OnWeightAdjusted_Postfix),
                BindingFlags.Static | BindingFlags.NonPublic);

            if (target == null || postfix == null)
            {
                ModLogger.Info("OrderOfBattleSiegeProjectedCountsPatch: OrderOfBattleVM.OnWeightAdjusted not found. Skip.");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(postfix));
            ModLogger.Info("OrderOfBattleSiegeProjectedCountsPatch: postfix applied to OrderOfBattleVM.OnWeightAdjusted.");
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

        private static bool OrderOfBattleUIHelper_GetMatchingClasses_Prefix(
            List<OrderOfBattleFormationItemVM> formationList,
            OrderOfBattleFormationClassVM formationClass,
            Func<OrderOfBattleFormationClassVM, bool> predicate,
            ref List<OrderOfBattleFormationClassVM> __result)
        {
            try
            {
                if (!ShouldProjectSiegeOrderOfBattleCounts() ||
                    formationList == null ||
                    formationClass == null ||
                    !TryProjectSiegeFormationClass(formationClass.Class, out FormationClass projectedClass))
                {
                    return true;
                }

                var matchingClasses = new List<OrderOfBattleFormationClassVM>();
                foreach (OrderOfBattleFormationItemVM formationItem in formationList)
                {
                    if (formationItem?.Classes == null)
                        continue;

                    foreach (OrderOfBattleFormationClassVM classVm in formationItem.Classes)
                    {
                        if (classVm == null ||
                            classVm.IsUnset ||
                            !TryProjectSiegeFormationClass(classVm.Class, out FormationClass candidateProjectedClass) ||
                            candidateProjectedClass != projectedClass ||
                            predicate != null && !predicate(classVm))
                        {
                            continue;
                        }

                        matchingClasses.Add(classVm);
                    }
                }

                __result = matchingClasses;
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static bool OrderOfBattleVM_GetAllFormationClassesWith_Prefix(
            OrderOfBattleVM __instance,
            FormationClass formationClass,
            ref List<OrderOfBattleFormationClassVM> __result)
        {
            try
            {
                if (!ShouldProjectSiegeOrderOfBattleCounts() ||
                    __instance == null ||
                    !TryProjectSiegeFormationClass(formationClass, out FormationClass projectedClass))
                {
                    return true;
                }

                var matchingClasses = new List<OrderOfBattleFormationClassVM>();
                foreach (OrderOfBattleFormationItemVM formationItem in GetOrderOfBattleFormationItems(__instance))
                {
                    if (formationItem?.Classes == null)
                        continue;

                    foreach (OrderOfBattleFormationClassVM classVm in formationItem.Classes)
                    {
                        if (classVm == null ||
                            classVm.IsUnset ||
                            !TryProjectSiegeFormationClass(classVm.Class, out FormationClass candidateProjectedClass) ||
                            candidateProjectedClass != projectedClass)
                        {
                            continue;
                        }

                        matchingClasses.Add(classVm);
                    }
                }

                __result = matchingClasses;
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
            TroopTraitsMask projectedFilter = ProjectSiegeTroopFilter(filter, projectedClass);
            result = new ValueTuple<Formation, int, TroopTraitsMask, List<Agent>>(
                formationItem.Formation,
                unitCount,
                projectedFilter,
                excludedAgents);
            return true;
        }

        private static TroopTraitsMask ProjectSiegeTroopFilter(TroopTraitsMask filter, FormationClass projectedClass)
        {
            const TroopTraitsMask formationClassTraits =
                TroopTraitsMask.Melee |
                TroopTraitsMask.Ranged |
                TroopTraitsMask.Mount;

            TroopTraitsMask projectedFilter = filter & ~formationClassTraits;
            projectedFilter |= projectedClass == FormationClass.Ranged
                ? TroopTraitsMask.Ranged
                : TroopTraitsMask.Melee;
            return projectedFilter;
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

        private static void OrderOfBattleVM_OnWeightAdjusted_Postfix(
            OrderOfBattleVM __instance,
            OrderOfBattleFormationClassVM formationClass)
        {
            try
            {
                if (_isApplyingProjectedWeightDistribution ||
                    !ShouldProjectSiegeOrderOfBattleCounts() ||
                    __instance == null ||
                    formationClass == null)
                {
                    return;
                }

                FormationClass projectedClass = DismountSiegeFormationClass(formationClass.Class.FallbackClass());
                if (projectedClass != FormationClass.Infantry && projectedClass != FormationClass.Ranged)
                    return;

                _isApplyingProjectedWeightDistribution = true;
                try
                {
                    TryApplyProjectedWeightDistribution(__instance, projectedClass);
                }
                finally
                {
                    _isApplyingProjectedWeightDistribution = false;
                }
            }
            catch (Exception ex)
            {
                if (CoopDebugConfig.OrderOfBattleDiagnostics)
                {
                    ModLogger.Info(
                        "OrderOfBattleSiegeProjectedCountsPatch: projected weight distribution failed open: " +
                        ex.GetType().Name + ":" + ex.Message);
                }
            }
        }

        private static bool TryApplyProjectedWeightDistribution(
            OrderOfBattleVM orderOfBattleVm,
            FormationClass projectedClass)
        {
            if (orderOfBattleVm == null ||
                AllFormationsField == null ||
                projectedClass != FormationClass.Infantry && projectedClass != FormationClass.Ranged)
            {
                return false;
            }

            var formationItems = GetOrderOfBattleFormationItems(orderOfBattleVm);
            if (formationItems.Count <= 0)
                return false;

            var targetClasses = new List<OrderOfBattleFormationClassVM>();
            var targetFormations = new Dictionary<Formation, OrderOfBattleFormationClassVM>();
            foreach (OrderOfBattleFormationItemVM formationItem in formationItems)
            {
                if (formationItem?.Formation == null || formationItem.Classes == null)
                    continue;

                foreach (OrderOfBattleFormationClassVM classVm in formationItem.Classes)
                {
                    if (classVm == null || classVm.IsUnset)
                        continue;

                    FormationClass classProjected = DismountSiegeFormationClass(classVm.Class.FallbackClass());
                    if (classProjected != projectedClass || targetFormations.ContainsKey(formationItem.Formation))
                        continue;

                    targetClasses.Add(classVm);
                    targetFormations[formationItem.Formation] = classVm;
                }
            }

            if (targetClasses.Count <= 1)
                return false;

            List<Agent> assignableAgents = CollectProjectedAssignmentAgents(
                team: targetClasses[0]?.BelongedFormationItem?.Formation?.Team,
                formationItems: formationItems,
                projectedClass: projectedClass);
            if (assignableAgents.Count <= 0)
                return false;

            var desiredCountsByFormation = BuildProjectedDesiredCountsByFormation(targetClasses, assignableAgents.Count);
            if (desiredCountsByFormation.Count <= 0)
                return false;

            LogProjectedWeightDistributionDiagnostics(
                "before-assignment",
                projectedClass,
                targetClasses,
                desiredCountsByFormation,
                assignableAgents);

            var massTransferData = new List<ValueTuple<Formation, int, TroopTraitsMask, List<Agent>>>();
            foreach (OrderOfBattleFormationItemVM formationItem in formationItems)
            {
                Formation formation = formationItem?.Formation;
                if (formation == null)
                    continue;

                int projectedUnitsInFormation = CountProjectedUnitsInClass(formation, projectedClass);
                desiredCountsByFormation.TryGetValue(formation, out int desiredCount);
                if (projectedUnitsInFormation <= 0 && desiredCount <= 0)
                    continue;

                massTransferData.Add(new ValueTuple<Formation, int, TroopTraitsMask, List<Agent>>(
                    formation,
                    desiredCount,
                    BuildProjectedTroopFilter(formationItem, projectedClass),
                    GetProjectedExcludedAgentsForTransfer(formationItem, projectedClass)));
            }

            if (massTransferData.Count <= 1)
                return false;

            Team team = targetClasses[0]?.BelongedFormationItem?.Formation?.Team;
            Mission mission = Mission.Current;
            if (team == null || mission == null)
                return false;

            bool previousTeleportingAgents = mission.IsTeleportingAgents;
            try
            {
                mission.IsTeleportingAgents = true;
                if (!TryApplyProjectedFormationAssignments(
                        targetClasses,
                        desiredCountsByFormation,
                        assignableAgents))
                {
                    return false;
                }

                FinalizeProjectedWeightDistribution(mission, team, formationItems, massTransferData, projectedClass);
                LogProjectedWeightDistributionDiagnostics(
                    "after-assignment",
                    projectedClass,
                    targetClasses,
                    desiredCountsByFormation,
                    assignableAgents);
            }
            finally
            {
                mission.IsTeleportingAgents = previousTeleportingAgents;
            }

            TrySyncCommanderDeploymentFormationAssignmentsForTeam(
                team,
                "OrderOfBattleSiegeProjectedCountsPatch.OnWeightAdjusted");
            return true;
        }

        private static void LogProjectedWeightDistributionDiagnostics(
            string stage,
            FormationClass projectedClass,
            List<OrderOfBattleFormationClassVM> targetClasses,
            Dictionary<Formation, int> desiredCountsByFormation,
            List<Agent> assignableAgents)
        {
            if (!CoopDebugConfig.OrderOfBattleDiagnostics)
                return;

            try
            {
                var targetSummaries = new List<string>();
                if (targetClasses != null)
                {
                    foreach (OrderOfBattleFormationClassVM classVm in targetClasses)
                    {
                        Formation formation = classVm?.BelongedFormationItem?.Formation;
                        if (formation == null)
                            continue;

                        int desiredCount = 0;
                        if (desiredCountsByFormation != null)
                            desiredCountsByFormation.TryGetValue(formation, out desiredCount);
                        targetSummaries.Add(
                            "#" + (formation.Index + 1) +
                            ":class=" + classVm.Class +
                            ",weight=" + classVm.Weight +
                            ",desired=" + desiredCount +
                            ",projectedActual=" + CountProjectedUnitsInClass(formation, projectedClass) +
                            ",totalActual=" + formation.CountOfUnits);
                    }
                }

                ModLogger.Info(
                    "OrderOfBattleSiegeProjectedCountsPatch: projected distribution diagnostics. " +
                    "Stage=" + (stage ?? string.Empty) +
                    " ProjectedClass=" + projectedClass +
                    " AssignableAgents=" + (assignableAgents?.Count ?? 0) +
                    " Targets={" + string.Join("; ", targetSummaries) + "}");
            }
            catch (Exception ex)
            {
                ModLogger.Info(
                    "OrderOfBattleSiegeProjectedCountsPatch: projected distribution diagnostics failed. " +
                    "Error=" + ex.GetType().Name + ":" + ex.Message);
            }
        }

        private static List<Agent> CollectProjectedAssignmentAgents(
            Team team,
            List<OrderOfBattleFormationItemVM> formationItems,
            FormationClass projectedClass)
        {
            var agents = new List<Agent>();
            var seenAgents = new HashSet<Agent>();
            if (team == null || formationItems == null)
                return agents;

            foreach (OrderOfBattleFormationItemVM formationItem in formationItems)
            {
                Formation formation = formationItem?.Formation;
                if (formation == null)
                    continue;

                try
                {
                    foreach (Agent agent in formation.Arrangement.GetAllUnits())
                        AddProjectedAssignmentAgent(agents, seenAgents, team, agent, projectedClass);
                }
                catch
                {
                }
            }

            try
            {
                Mission mission = Mission.Current;
                if (mission?.AllAgents != null)
                {
                    for (int i = 0; i < mission.AllAgents.Count; i++)
                        AddProjectedAssignmentAgent(agents, seenAgents, team, mission.AllAgents[i], projectedClass);
                }
            }
            catch
            {
            }

            return agents;
        }

        private static void AddProjectedAssignmentAgent(
            List<Agent> agents,
            HashSet<Agent> seenAgents,
            Team team,
            Agent agent,
            FormationClass projectedClass)
        {
            if (agents == null ||
                seenAgents == null ||
                !IsProjectedAssignmentAgent(team, agent, projectedClass) ||
                seenAgents.Contains(agent))
            {
                return;
            }

            seenAgents.Add(agent);
            agents.Add(agent);
        }

        private static bool IsProjectedAssignmentAgent(Team team, Agent agent, FormationClass projectedClass)
        {
            if (team == null ||
                agent == null ||
                agent.IsMount ||
                !agent.IsActive() ||
                ReferenceEquals(agent, Agent.Main) ||
                !ReferenceEquals(agent.Team, team) ||
                agent.Formation == null ||
                !ReferenceEquals(agent.Formation.Team, team))
            {
                return false;
            }

            return CoopMissionSelectionView.IsCommanderDeploymentProjectedAgentInFormationClass(agent, projectedClass);
        }

        private static bool TryApplyProjectedFormationAssignments(
            List<OrderOfBattleFormationClassVM> targetClasses,
            Dictionary<Formation, int> desiredCountsByFormation,
            List<Agent> assignableAgents)
        {
            if (targetClasses == null ||
                desiredCountsByFormation == null ||
                assignableAgents == null ||
                targetClasses.Count <= 0 ||
                assignableAgents.Count <= 0)
            {
                return false;
            }

            var assignedCounts = new Dictionary<Formation, int>();
            var assignedAgents = new HashSet<Agent>();
            var targetByAgent = new Dictionary<Agent, Formation>();

            foreach (OrderOfBattleFormationClassVM classVm in targetClasses)
            {
                Formation targetFormation = classVm?.BelongedFormationItem?.Formation;
                if (targetFormation == null ||
                    !desiredCountsByFormation.TryGetValue(targetFormation, out int desiredCount) ||
                    desiredCount <= 0)
                {
                    continue;
                }

                foreach (Agent agent in assignableAgents)
                {
                    if (agent == null ||
                        assignedAgents.Contains(agent) ||
                        !ReferenceEquals(agent.Formation, targetFormation))
                    {
                        continue;
                    }

                    int assignedCount = GetAssignedProjectedCount(assignedCounts, targetFormation);
                    if (assignedCount >= desiredCount)
                        break;

                    targetByAgent[agent] = targetFormation;
                    assignedAgents.Add(agent);
                    assignedCounts[targetFormation] = assignedCount + 1;
                }
            }

            foreach (OrderOfBattleFormationClassVM classVm in targetClasses)
            {
                Formation targetFormation = classVm?.BelongedFormationItem?.Formation;
                if (targetFormation == null ||
                    !desiredCountsByFormation.TryGetValue(targetFormation, out int desiredCount) ||
                    desiredCount <= 0)
                {
                    continue;
                }

                foreach (Agent agent in assignableAgents)
                {
                    if (agent == null || assignedAgents.Contains(agent))
                        continue;

                    int assignedCount = GetAssignedProjectedCount(assignedCounts, targetFormation);
                    if (assignedCount >= desiredCount)
                        break;

                    targetByAgent[agent] = targetFormation;
                    assignedAgents.Add(agent);
                    assignedCounts[targetFormation] = assignedCount + 1;
                }
            }

            if (targetByAgent.Count <= 0)
                return false;

            HashSet<Formation> impactedFormations = CollectImpactedFormations(targetByAgent);
            bool changed = false;
            BeginMassUnitTransfer(impactedFormations);
            try
            {
                foreach (KeyValuePair<Agent, Formation> assignment in targetByAgent)
                {
                    Agent agent = assignment.Key;
                    Formation targetFormation = assignment.Value;
                    if (agent == null || targetFormation == null || ReferenceEquals(agent.Formation, targetFormation))
                        continue;

                    try
                    {
                        agent.Formation = targetFormation;
                        changed = true;
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                EndMassUnitTransfer(impactedFormations);
            }

            return changed || assignedAgents.Count == assignableAgents.Count;
        }

        private static HashSet<Formation> CollectImpactedFormations(Dictionary<Agent, Formation> targetByAgent)
        {
            var formations = new HashSet<Formation>();
            if (targetByAgent == null)
                return formations;

            foreach (KeyValuePair<Agent, Formation> assignment in targetByAgent)
            {
                if (assignment.Key?.Formation != null)
                    formations.Add(assignment.Key.Formation);

                if (assignment.Value != null)
                    formations.Add(assignment.Value);
            }

            return formations;
        }

        private static void BeginMassUnitTransfer(IEnumerable<Formation> formations)
        {
            if (formations == null)
                return;

            foreach (Formation formation in formations)
            {
                try
                {
                    formation?.OnMassUnitTransferStart();
                }
                catch
                {
                }
            }
        }

        private static void EndMassUnitTransfer(IEnumerable<Formation> formations)
        {
            if (formations == null)
                return;

            foreach (Formation formation in formations)
            {
                try
                {
                    formation?.OnMassUnitTransferEnd();
                }
                catch
                {
                }
            }
        }

        private static int GetAssignedProjectedCount(Dictionary<Formation, int> assignedCounts, Formation formation)
        {
            if (assignedCounts == null || formation == null)
                return 0;

            return assignedCounts.TryGetValue(formation, out int count) ? count : 0;
        }

        private static List<OrderOfBattleFormationItemVM> GetOrderOfBattleFormationItems(OrderOfBattleVM orderOfBattleVm)
        {
            var formationItems = new List<OrderOfBattleFormationItemVM>();
            if (!(AllFormationsField.GetValue(orderOfBattleVm) is IEnumerable allFormations))
                return formationItems;

            foreach (object item in allFormations)
            {
                if (item is OrderOfBattleFormationItemVM formationItem)
                    formationItems.Add(formationItem);
            }

            return formationItems;
        }

        private static Dictionary<Formation, int> BuildProjectedDesiredCountsByFormation(
            List<OrderOfBattleFormationClassVM> targetClasses,
            int totalProjectedUnits)
        {
            var desiredCountsByFormation = new Dictionary<Formation, int>();
            if (targetClasses == null || targetClasses.Count <= 0 || totalProjectedUnits <= 0)
                return desiredCountsByFormation;

            int totalWeight = 0;
            foreach (OrderOfBattleFormationClassVM classVm in targetClasses)
            {
                if (classVm?.BelongedFormationItem?.Formation != null)
                    totalWeight += Math.Max(0, classVm.Weight);
            }

            if (totalWeight <= 0)
                return desiredCountsByFormation;

            int assignedCount = 0;
            foreach (OrderOfBattleFormationClassVM classVm in targetClasses)
            {
                Formation formation = classVm?.BelongedFormationItem?.Formation;
                if (formation == null)
                    continue;

                int desiredCount = (int)Math.Ceiling((double)Math.Max(0, classVm.Weight) * totalProjectedUnits / totalWeight);
                desiredCountsByFormation[formation] = desiredCount;
                assignedCount += desiredCount;
            }

            while (desiredCountsByFormation.Count > 0 && assignedCount != totalProjectedUnits)
            {
                int delta = assignedCount - totalProjectedUnits;
                Formation formation = delta > 0
                    ? FindFormationWithExtremumDesiredCount(desiredCountsByFormation, findMaximum: true)
                    : FindFormationWithExtremumDesiredCount(desiredCountsByFormation, findMaximum: false);
                if (formation == null)
                    break;

                int previousCount = desiredCountsByFormation[formation];
                int nextCount = Math.Max(0, previousCount - Math.Sign(delta));
                if (nextCount == previousCount)
                    break;

                desiredCountsByFormation[formation] = nextCount;
                assignedCount += nextCount - previousCount;
            }

            return desiredCountsByFormation;
        }

        private static Formation FindFormationWithExtremumDesiredCount(
            Dictionary<Formation, int> desiredCountsByFormation,
            bool findMaximum)
        {
            Formation bestFormation = null;
            int bestCount = findMaximum ? int.MinValue : int.MaxValue;
            foreach (KeyValuePair<Formation, int> pair in desiredCountsByFormation)
            {
                if (pair.Key == null)
                    continue;

                if ((findMaximum && pair.Value > bestCount) ||
                    (!findMaximum && pair.Value < bestCount))
                {
                    bestFormation = pair.Key;
                    bestCount = pair.Value;
                }
            }

            return bestFormation;
        }

        private static TroopTraitsMask BuildProjectedTroopFilter(
            OrderOfBattleFormationItemVM formationItem,
            FormationClass projectedClass)
        {
            TroopTraitsMask filter = projectedClass == FormationClass.Ranged
                ? TroopTraitsMask.Ranged
                : TroopTraitsMask.Melee;

            if (formationItem?.FilterItems == null)
                return filter;

            foreach (OrderOfBattleFormationFilterSelectorItemVM filterItem in formationItem.FilterItems)
            {
                if (filterItem == null || !filterItem.IsActive)
                    continue;

                filter |= TroopFilteringUtilities.GetFilter(filterItem.FilterType);
            }

            return filter;
        }

        private static void FinalizeProjectedWeightDistribution(
            Mission mission,
            Team team,
            List<OrderOfBattleFormationItemVM> formationItems,
            List<ValueTuple<Formation, int, TroopTraitsMask, List<Agent>>> massTransferData,
            FormationClass projectedClass)
        {
            var formationsToRefresh = new HashSet<Formation>();
            foreach (ValueTuple<Formation, int, TroopTraitsMask, List<Agent>> transferData in massTransferData)
            {
                if (transferData.Item1 != null)
                    formationsToRefresh.Add(transferData.Item1);
            }

            foreach (Formation formation in formationsToRefresh)
                FinalizeProjectedWeightDistributionFormation(mission, team, formation);

            foreach (OrderOfBattleFormationItemVM formationItem in formationItems)
            {
                if (formationItem?.Formation == null)
                    continue;

                if (!formationsToRefresh.Contains(formationItem.Formation) &&
                    CountProjectedUnitsInClass(formationItem.Formation, projectedClass) <= 0)
                {
                    continue;
                }

                formationItem.OnSizeChanged();
                formationItem.MakeMarkerWorldPositionDirty();
            }
        }

        private static void FinalizeProjectedWeightDistributionFormation(
            Mission mission,
            Team team,
            Formation formation)
        {
            if (mission == null || team == null || formation == null)
                return;

            try
            {
                team.TriggerOnFormationsChanged(formation);
            }
            catch
            {
            }

            try
            {
                formation.QuerySystem?.ExpireAfterUnitAddRemove();
                formation.QuerySystem?.Expire();
                team.QuerySystem?.ExpireAfterUnitAddRemove();
                team.QuerySystem?.Expire();
            }
            catch
            {
            }

            if (formation.CountOfUnits <= 0)
                return;

            TryEnsureProjectedFormationPosition(mission, formation);

            try
            {
                formation.ApplyActionOnEachUnit(agent =>
                {
                    if (agent == null || !agent.IsActive())
                        return;

                    WorldPosition orderPosition = formation.GetOrderPositionOfUnit(agent);
                    if (orderPosition.IsValid)
                        agent.TeleportToPosition(orderPosition.GetGroundVec3());
                });
                formation.SetHasPendingUnitPositions(hasPendingUnitPositions: false);
            }
            catch
            {
            }
        }

        private static void TryEnsureProjectedFormationPosition(Mission mission, Formation formation)
        {
            if (mission?.Scene == null ||
                formation == null ||
                formation.OrderPositionIsValid)
            {
                return;
            }

            try
            {
                Vec2 averagePosition = formation.GetAveragePositionOfUnits(excludeDetachedUnits: false, excludePlayer: false);
                float height = mission.Scene.GetTerrainHeight(averagePosition);
                mission.Scene.GetHeightAtPoint(averagePosition, BodyFlags.None, ref height);
                var worldPosition = new WorldPosition(
                    mission.Scene,
                    UIntPtr.Zero,
                    new Vec3(averagePosition, height),
                    hasValidZ: false);
                formation.SetPositioning(worldPosition);
            }
            catch
            {
            }
        }

        private static bool TrySyncCommanderDeploymentFormationAssignmentsForTeam(Team team, string source)
        {
            if (!GameNetwork.IsClient ||
                !GameNetwork.IsSessionActive ||
                team == null ||
                team.Side == BattleSideEnum.None)
            {
                LogCommanderDeploymentAssignmentSyncDiagnostics(
                    "skip-inactive",
                    team,
                    source,
                    "IsClient=" + GameNetwork.IsClient +
                    " IsSessionActive=" + GameNetwork.IsSessionActive +
                    " Side=" + (team?.Side.ToString() ?? "<null>"));
                return false;
            }

            if (!TryBuildCommanderDeploymentFormationAssignmentPayload(
                    team,
                    out byte[] assignmentBytes,
                    out byte[] formationLayoutBytes,
                    out string assignmentKey))
            {
                LogCommanderDeploymentAssignmentSyncDiagnostics(
                    "skip-no-payload",
                    team,
                    source,
                    string.Empty);
                return false;
            }

            if (string.Equals(
                    _lastSentCommanderDeploymentFormationAssignmentsKey,
                    assignmentKey,
                    StringComparison.Ordinal))
            {
                LogCommanderDeploymentAssignmentSyncDiagnostics(
                    "skip-duplicate",
                    team,
                    source,
                    "AssignmentBytes=" + assignmentBytes.Length +
                    " LayoutBytes=" + formationLayoutBytes.Length);
                return false;
            }

            if (!CoopBattleNetworkRequestTransport.TrySyncCommanderDeploymentFormationAssignments(
                    team.Side,
                    assignmentBytes,
                    formationLayoutBytes,
                    source))
            {
                LogCommanderDeploymentAssignmentSyncDiagnostics(
                    "transport-failed",
                    team,
                    source,
                    "AssignmentBytes=" + assignmentBytes.Length +
                    " LayoutBytes=" + formationLayoutBytes.Length);
                return false;
            }

            _lastSentCommanderDeploymentFormationAssignmentsKey = assignmentKey;
            LogCommanderDeploymentAssignmentSyncDiagnostics(
                "sent",
                team,
                source,
                "AssignmentBytes=" + assignmentBytes.Length +
                " LayoutBytes=" + formationLayoutBytes.Length);
            return true;
        }

        private static void LogCommanderDeploymentAssignmentSyncDiagnostics(
            string stage,
            Team team,
            string source,
            string detail)
        {
            if (!CoopDebugConfig.OrderOfBattleDiagnostics)
                return;

            try
            {
                ModLogger.Info(
                    "OrderOfBattleSiegeProjectedCountsPatch: commander deployment assignment sync diagnostics. " +
                    "Stage=" + (stage ?? string.Empty) +
                    " Source=" + (source ?? string.Empty) +
                    " TeamIndex=" + (team?.TeamIndex.ToString() ?? "<null>") +
                    " Side=" + (team?.Side.ToString() ?? "<null>") +
                    " Detail=" + (detail ?? string.Empty));
            }
            catch
            {
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

                TrySyncCommanderDeploymentFormationAssignmentsForTeam(
                    targetTeam,
                    "OrderOfBattleSiegeProjectedCountsPatch.RearrangeFormationsAccordingToFilters");
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
            out byte[] formationLayoutBytes,
            out string assignmentKey)
        {
            assignmentBytes = Array.Empty<byte>();
            formationLayoutBytes = Array.Empty<byte>();
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

            TryBuildCommanderDeploymentFormationLayoutPayload(team, out formationLayoutBytes);

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
                "|A=" +
                Convert.ToBase64String(assignmentBytes) +
                "|L=" +
                Convert.ToBase64String(formationLayoutBytes);
            return true;
        }

        private static bool TryBuildCommanderDeploymentFormationLayoutPayload(
            Team team,
            out byte[] formationLayoutBytes)
        {
            formationLayoutBytes = Array.Empty<byte>();
            if (team?.FormationsIncludingEmpty == null)
                return false;

            int maxLayoutRecords =
                CoopCommanderDeploymentFormationAssignmentsMessage.MaxFormationLayoutBytes /
                CoopCommanderDeploymentFormationAssignmentsMessage.BytesPerFormationLayout;
            var layouts = new List<ValueTuple<int, float, float, float, float>>();

            foreach (Formation formation in team.FormationsIncludingEmpty)
            {
                if (formation == null ||
                    formation.Index < 0 ||
                    formation.Index >= (int)FormationClass.NumberOfDefaultFormations ||
                    !formation.OrderPositionIsValid)
                {
                    continue;
                }

                Vec2 position = formation.OrderPosition;
                Vec2 direction = formation.Direction;
                if (!position.IsValid)
                    continue;

                if (!direction.IsValid || direction.LengthSquared < 0.0001f)
                    direction = Vec2.Forward;

                layouts.Add(new ValueTuple<int, float, float, float, float>(
                    formation.Index,
                    position.x,
                    position.y,
                    direction.x,
                    direction.y));

                if (layouts.Count >= maxLayoutRecords)
                    break;
            }

            if (layouts.Count <= 0)
                return false;

            formationLayoutBytes = new byte[layouts.Count * CoopCommanderDeploymentFormationAssignmentsMessage.BytesPerFormationLayout];
            int offset = 0;
            foreach (ValueTuple<int, float, float, float, float> layout in layouts)
            {
                formationLayoutBytes[offset++] = (byte)(layout.Item1 & 0xFF);
                WriteSingleToPayload(formationLayoutBytes, ref offset, layout.Item2);
                WriteSingleToPayload(formationLayoutBytes, ref offset, layout.Item3);
                WriteSingleToPayload(formationLayoutBytes, ref offset, layout.Item4);
                WriteSingleToPayload(formationLayoutBytes, ref offset, layout.Item5);
            }

            return true;
        }

        private static void WriteSingleToPayload(byte[] payload, ref int offset, float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Buffer.BlockCopy(bytes, 0, payload, offset, bytes.Length);
            offset += bytes.Length;
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

        private static bool TryProjectSiegeFormationClass(
            FormationClass formationClass,
            out FormationClass projectedClass)
        {
            projectedClass = DismountSiegeFormationClass(formationClass.FallbackClass());
            return projectedClass == FormationClass.Infantry || projectedClass == FormationClass.Ranged;
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
