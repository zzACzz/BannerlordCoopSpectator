using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.ViewModelCollection.OrderOfBattle;

namespace CoopSpectator.UI
{
    internal sealed class CoopSiegeOrderOfBattleVM : OrderOfBattleVM
    {
        internal static bool IsApplyingInitialProjectedConfiguration { get; private set; }

        protected override void LoadConfiguration()
        {
            IsApplyingInitialProjectedConfiguration = true;

            try
            {
                base.LoadConfiguration();

                if (_allFormations == null || _allFormations.Count <= 0)
                    return;

                RestrictFormationClassSelectorsToSiegeClasses();

                var usedFormationItems = new HashSet<OrderOfBattleFormationItemVM>();
                SeedProjectedClass(
                    DeploymentFormationClass.Infantry,
                    FormationClass.Infantry,
                    usedFormationItems);
                SeedProjectedClass(
                    DeploymentFormationClass.Ranged,
                    FormationClass.Ranged,
                    usedFormationItems);

                ClearUnusedFormationSlots(usedFormationItems);
                RestrictFormationClassSelectorsToSiegeClasses();
            }
            finally
            {
                IsApplyingInitialProjectedConfiguration = false;
            }
        }

        private void SeedProjectedClass(
            DeploymentFormationClass deploymentClass,
            FormationClass projectedClass,
            ISet<OrderOfBattleFormationItemVM> usedFormationItems)
        {
            int totalCount = CountProjectedUnitsInClass(projectedClass);
            if (totalCount <= 0)
                return;

            List<OrderOfBattleFormationItemVM> formationItems = CollectFormationItemsForProjectedClass(
                projectedClass,
                usedFormationItems);
            if (formationItems.Count <= 0)
            {
                OrderOfBattleFormationItemVM fallbackItem = FindUnusedFormationItem(usedFormationItems, preferEmpty: false);
                if (fallbackItem != null)
                    formationItems.Add(fallbackItem);
            }

            if (totalCount > 1 && formationItems.Count == 1)
            {
                OrderOfBattleFormationItemVM secondaryItem = FindUnusedFormationItem(usedFormationItems, preferEmpty: true);
                if (secondaryItem != null && !formationItems.Contains(secondaryItem))
                    formationItems.Add(secondaryItem);
            }

            if (formationItems.Count <= 0)
                return;

            int slotCount = Math.Min(formationItems.Count, Math.Max(1, totalCount));
            int baseWeight = 100 / slotCount;
            int weightRemainder = 100 % slotCount;
            for (int i = 0; i < slotCount; i++)
            {
                OrderOfBattleFormationItemVM formationItem = formationItems[i];
                if (formationItem == null)
                    continue;

                int weight = baseWeight + (i < weightRemainder ? 1 : 0);
                RefreshFormationSlot(formationItem, deploymentClass, weight);
                usedFormationItems?.Add(formationItem);
            }
        }

        private List<OrderOfBattleFormationItemVM> CollectFormationItemsForProjectedClass(
            FormationClass projectedClass,
            ISet<OrderOfBattleFormationItemVM> usedFormationItems)
        {
            var formationItems = new List<OrderOfBattleFormationItemVM>();
            if (_allFormations == null)
                return formationItems;

            foreach (OrderOfBattleFormationItemVM formationItem in _allFormations)
            {
                if (formationItem?.Formation == null || usedFormationItems?.Contains(formationItem) == true)
                    continue;

                if (CountProjectedUnitsInClass(formationItem.Formation, projectedClass) > 0)
                    formationItems.Add(formationItem);
            }

            return formationItems;
        }

        private OrderOfBattleFormationItemVM FindUnusedFormationItem(
            ISet<OrderOfBattleFormationItemVM> usedFormationItems,
            bool preferEmpty)
        {
            if (_allFormations == null)
                return null;

            OrderOfBattleFormationItemVM fallbackItem = null;
            foreach (OrderOfBattleFormationItemVM formationItem in _allFormations)
            {
                if (formationItem == null || usedFormationItems?.Contains(formationItem) == true)
                    continue;

                bool isEmptyProjectedSlot =
                    formationItem.Formation == null ||
                    CountProjectedUnitsInClass(formationItem.Formation, FormationClass.Infantry) <= 0 &&
                    CountProjectedUnitsInClass(formationItem.Formation, FormationClass.Ranged) <= 0;

                if (!preferEmpty || isEmptyProjectedSlot)
                    return formationItem;

                if (fallbackItem == null)
                    fallbackItem = formationItem;
            }

            return fallbackItem;
        }

        private void RefreshFormationSlot(
            OrderOfBattleFormationItemVM formationItem,
            DeploymentFormationClass deploymentClass,
            int weight)
        {
            if (formationItem?.Formation == null)
                return;

            formationItem.RefreshFormation(formationItem.Formation, deploymentClass, true);
            RestrictFormationClassSelectorToSiegeClasses(formationItem);
            SetPrimaryClassWeight(formationItem, weight);
            formationItem.OnSizeChanged();
            formationItem.UpdateAdjustable();
        }

        private void ClearUnusedFormationSlots(ISet<OrderOfBattleFormationItemVM> usedFormationItems)
        {
            if (usedFormationItems == null || _allFormations == null)
                return;

            foreach (OrderOfBattleFormationItemVM formationItem in _allFormations)
            {
                if (formationItem?.Formation == null || usedFormationItems.Contains(formationItem))
                    continue;

                ClearFormationSlot(formationItem);
            }
        }

        private static void ClearFormationSlot(OrderOfBattleFormationItemVM formationItem)
        {
            if (formationItem?.Formation == null)
                return;

            formationItem.RefreshFormation(formationItem.Formation, DeploymentFormationClass.Unset, false);
            RestrictFormationClassSelectorToSiegeClasses(formationItem);
            TrySetFormationClassSelectorIndex(formationItem, 0);

            if (formationItem.Classes != null)
            {
                for (int i = 0; i < formationItem.Classes.Count; i++)
                {
                    OrderOfBattleFormationClassVM classVm = formationItem.Classes[i];
                    if (classVm == null)
                        continue;

                    classVm.Class = FormationClass.NumberOfAllFormations;
                    classVm.Weight = 0;
                    classVm.IsLocked = false;
                }
            }

            formationItem.HasFormation = false;
            formationItem.IsSelectable = false;
            formationItem.OnSizeChanged();
            formationItem.UpdateAdjustable();
        }

        private void RestrictFormationClassSelectorsToSiegeClasses()
        {
            if (_allFormations == null)
                return;

            foreach (OrderOfBattleFormationItemVM formationItem in _allFormations)
                RestrictFormationClassSelectorToSiegeClasses(formationItem);
        }

        private static void RestrictFormationClassSelectorToSiegeClasses(OrderOfBattleFormationItemVM formationItem)
        {
            if (formationItem == null)
                return;

            try
            {
                object selector = GetFormationClassSelector(formationItem);
                PropertyInfo itemListProperty = selector?.GetType().GetProperty(
                    "ItemList",
                    BindingFlags.Instance | BindingFlags.Public);
                if (!(itemListProperty?.GetValue(selector, null) is IEnumerable itemList))
                    return;

                var invalidItems = new List<object>();
                foreach (object selectorItem in itemList)
                {
                    if (!TryReadDeploymentFormationClass(selectorItem, out DeploymentFormationClass deploymentClass))
                        continue;

                    bool isAllowed = IsAllowedSiegeDeploymentFormationClass(deploymentClass);
                    SetSelectorItemCanBeSelected(selectorItem, isAllowed);
                    if (!isAllowed)
                        invalidItems.Add(selectorItem);
                }

                if (TryReadSelectedDeploymentFormationClass(selector, out DeploymentFormationClass selectedClass) &&
                    !IsAllowedSiegeDeploymentFormationClass(selectedClass))
                {
                    TrySetFormationClassSelectorIndex(formationItem, 0);
                }

                RemoveInvalidFormationClassSelectorItems(itemList, invalidItems);
            }
            catch
            {
            }
        }

        private static bool IsAllowedSiegeDeploymentFormationClass(DeploymentFormationClass deploymentClass)
        {
            return deploymentClass == DeploymentFormationClass.Unset ||
                   deploymentClass == DeploymentFormationClass.Infantry ||
                   deploymentClass == DeploymentFormationClass.Ranged ||
                   deploymentClass == DeploymentFormationClass.InfantryAndRanged;
        }

        private static bool TryReadSelectedDeploymentFormationClass(
            object selector,
            out DeploymentFormationClass deploymentClass)
        {
            deploymentClass = DeploymentFormationClass.Unset;
            try
            {
                PropertyInfo selectedItemProperty = selector?.GetType().GetProperty(
                    "SelectedItem",
                    BindingFlags.Instance | BindingFlags.Public);
                object selectedItem = selectedItemProperty?.GetValue(selector, null);
                return TryReadDeploymentFormationClass(selectedItem, out deploymentClass);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadDeploymentFormationClass(
            object selectorItem,
            out DeploymentFormationClass deploymentClass)
        {
            deploymentClass = DeploymentFormationClass.Unset;
            if (selectorItem == null)
                return false;

            try
            {
                FieldInfo formationClassField = selectorItem.GetType().GetField(
                    "FormationClass",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object formationClassValue = formationClassField?.GetValue(selectorItem);
                if (formationClassValue is DeploymentFormationClass fieldClass)
                {
                    deploymentClass = fieldClass;
                    return true;
                }

                PropertyInfo formationClassIntProperty = selectorItem.GetType().GetProperty(
                    "FormationClassInt",
                    BindingFlags.Instance | BindingFlags.Public);
                object formationClassIntValue = formationClassIntProperty?.GetValue(selectorItem, null);
                if (formationClassIntValue is int classInt &&
                    Enum.IsDefined(typeof(DeploymentFormationClass), classInt))
                {
                    deploymentClass = (DeploymentFormationClass)classInt;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static void SetSelectorItemCanBeSelected(object selectorItem, bool canBeSelected)
        {
            if (selectorItem == null)
                return;

            try
            {
                PropertyInfo canBeSelectedProperty = selectorItem.GetType().GetProperty(
                    "CanBeSelected",
                    BindingFlags.Instance | BindingFlags.Public);
                if (canBeSelectedProperty == null || !canBeSelectedProperty.CanWrite)
                    return;

                object currentValue = canBeSelectedProperty.GetValue(selectorItem, null);
                if (currentValue is bool currentCanBeSelected && currentCanBeSelected == canBeSelected)
                    return;

                canBeSelectedProperty.SetValue(selectorItem, canBeSelected, null);
            }
            catch
            {
            }
        }

        private static void RemoveInvalidFormationClassSelectorItems(
            object itemList,
            List<object> invalidItems)
        {
            if (itemList == null || invalidItems == null || invalidItems.Count <= 0)
                return;

            try
            {
                Type itemListType = itemList.GetType();
                foreach (object invalidItem in invalidItems)
                {
                    if (invalidItem == null)
                        continue;

                    MethodInfo removeMethod = itemListType.GetMethod(
                        "Remove",
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new[] { invalidItem.GetType() },
                        null);
                    removeMethod?.Invoke(itemList, new[] { invalidItem });
                }
            }
            catch
            {
            }
        }

        private static void TrySetFormationClassSelectorIndex(
            OrderOfBattleFormationItemVM formationItem,
            int selectedIndex)
        {
            if (formationItem == null)
                return;

            try
            {
                object selector = GetFormationClassSelector(formationItem);
                PropertyInfo selectedIndexProperty = selector?.GetType().GetProperty(
                    "SelectedIndex",
                    BindingFlags.Instance | BindingFlags.Public);
                if (selectedIndexProperty == null || !selectedIndexProperty.CanWrite)
                    return;

                object currentValue = selectedIndexProperty.GetValue(selector, null);
                if (currentValue is int currentIndex && currentIndex == selectedIndex)
                    return;

                selectedIndexProperty.SetValue(selector, selectedIndex, null);
            }
            catch
            {
            }
        }

        private static object GetFormationClassSelector(OrderOfBattleFormationItemVM formationItem)
        {
            if (formationItem == null)
                return null;

            PropertyInfo selectorProperty = typeof(OrderOfBattleFormationItemVM).GetProperty(
                "FormationClassSelector",
                BindingFlags.Instance | BindingFlags.Public);
            return selectorProperty?.GetValue(formationItem, null);
        }

        private static void SetPrimaryClassWeight(OrderOfBattleFormationItemVM formationItem, int weight)
        {
            if (formationItem?.Classes == null)
                return;

            for (int i = 0; i < formationItem.Classes.Count; i++)
            {
                OrderOfBattleFormationClassVM classVm = formationItem.Classes[i];
                if (classVm == null)
                    continue;

                classVm.Weight = i == 0 ? weight : 0;
            }
        }

        private int CountProjectedUnitsInClass(FormationClass formationClass)
        {
            FormationClass projectedClass = DismountSiegeFormationClass(formationClass.FallbackClass());
            if (projectedClass != FormationClass.Infantry && projectedClass != FormationClass.Ranged)
                return 0;

            int count = 0;
            foreach (OrderOfBattleFormationItemVM item in _allFormations.Where(item => item?.Formation != null))
            {
                count += CountProjectedUnitsInClass(item.Formation, projectedClass);
            }

            return count;
        }

        private static int CountProjectedUnitsInClass(Formation formation, FormationClass projectedClass)
        {
            if (formation == null ||
                projectedClass != FormationClass.Infantry && projectedClass != FormationClass.Ranged)
            {
                return 0;
            }

            return formation.GetCountOfUnitsWithCondition(agent =>
                ResolveProjectedSiegeAgentClass(agent) == projectedClass);
        }

        private static FormationClass ResolveProjectedSiegeAgentClass(Agent agent)
        {
            if (agent == null || agent.IsMount)
                return FormationClass.NumberOfAllFormations;

            if (!agent.HasMount && agent.IsRangedCached)
                return FormationClass.Ranged;

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

            formationClass = DismountSiegeFormationClass(formationClass.FallbackClass());
            if (formationClass == FormationClass.Ranged || formationClass == FormationClass.Infantry)
                return formationClass;

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
    }
}
