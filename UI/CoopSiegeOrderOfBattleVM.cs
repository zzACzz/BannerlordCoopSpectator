using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;
using TaleWorlds.MountAndBlade.ViewModelCollection.OrderOfBattle;

namespace CoopSpectator.UI
{
    internal sealed class CoopSiegeOrderOfBattleVM : OrderOfBattleVM
    {
        protected override void LoadConfiguration()
        {
            base.LoadConfiguration();

            if (_allFormations == null || _allFormations.Count <= 0)
                return;

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

                formationItem.RefreshFormation(formationItem.Formation, DeploymentFormationClass.Unset, false);
                SetPrimaryClassWeight(formationItem, 0);
                formationItem.OnSizeChanged();
                formationItem.UpdateAdjustable();
            }
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
