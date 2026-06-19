using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CoopSpectator.Infrastructure;
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

        public static void Apply(Harmony harmony)
        {
            PatchTotalCountOfUnitsInClass(harmony);
            PatchVisibleTroopTypeLookup(harmony);
        }

        private static void PatchTotalCountOfUnitsInClass(Harmony harmony)
        {
            Type helperType = typeof(OrderOfBattleVM).Assembly.GetType(
                "TaleWorlds.MountAndBlade.ViewModelCollection.OrderOfBattle.OrderOfBattleUIHelper");
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
                ResolveProjectedSiegeAgentClass(agent) == projectedClass);
        }

        private static FormationClass ResolveProjectedSiegeAgentClass(Agent agent)
        {
            if (agent == null || agent.IsMount)
                return FormationClass.NumberOfAllFormations;

            FormationClass formationClass = FormationClass.NumberOfAllFormations;
            BasicCharacterObject character = agent.Character;
            if (character != null)
            {
                try
                {
                    BattleSideEnum side = agent.Team?.Side ?? BattleSideEnum.None;
                    if (Mission.Current != null && side != BattleSideEnum.None)
                        formationClass = Mission.Current.GetAgentTroopClass(side, character);
                    else
                        formationClass = character.DefaultFormationClass;
                }
                catch
                {
                    formationClass = character.DefaultFormationClass;
                }
            }

            if (formationClass == FormationClass.NumberOfAllFormations ||
                formationClass == FormationClass.NumberOfRegularFormations ||
                formationClass == FormationClass.Unset)
            {
                return agent.IsRangedCached ? FormationClass.Ranged : FormationClass.Infantry;
            }

            formationClass = formationClass.FallbackClass();
            formationClass = DismountSiegeFormationClass(formationClass);
            if (formationClass == FormationClass.Ranged)
                return FormationClass.Ranged;

            if (formationClass == FormationClass.Infantry)
                return FormationClass.Infantry;

            return agent.IsRangedCached ? FormationClass.Ranged : FormationClass.Infantry;
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
