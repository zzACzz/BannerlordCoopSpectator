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
        private const int InfantryPrimarySlot = 0;
        private const int RangedPrimarySlot = 1;
        private const int InfantrySecondarySlot = 3;
        private const int RangedSecondarySlot = 4;

        protected override void LoadConfiguration()
        {
            base.LoadConfiguration();

            if (_allFormations == null || _allFormations.Count <= 0)
                return;

            int infantryCount = CountProjectedUnitsInClass(FormationClass.Infantry);
            int rangedCount = CountProjectedUnitsInClass(FormationClass.Ranged);

            SeedProjectedClass(
                DeploymentFormationClass.Infantry,
                infantryCount,
                InfantryPrimarySlot,
                InfantrySecondarySlot);
            SeedProjectedClass(
                DeploymentFormationClass.Ranged,
                rangedCount,
                RangedPrimarySlot,
                RangedSecondarySlot);

            ClearUnusedFormationSlots(new HashSet<int>
            {
                infantryCount > 0 ? InfantryPrimarySlot : -1,
                infantryCount > 1 ? InfantrySecondarySlot : -1,
                rangedCount > 0 ? RangedPrimarySlot : -1,
                rangedCount > 1 ? RangedSecondarySlot : -1
            });
        }

        private void SeedProjectedClass(
            DeploymentFormationClass deploymentClass,
            int totalCount,
            int primarySlot,
            int secondarySlot)
        {
            if (totalCount <= 0)
                return;

            OrderOfBattleFormationItemVM primaryItem = GetFormationItem(primarySlot);
            if (primaryItem == null)
                return;

            bool canSplit = totalCount > 1 && GetFormationItem(secondarySlot) != null;
            int primaryWeight = canSplit ? 50 : 100;
            int secondaryWeight = canSplit ? 50 : 0;

            RefreshFormationSlot(primaryItem, deploymentClass, primaryWeight);

            if (!canSplit)
                return;

            OrderOfBattleFormationItemVM secondaryItem = GetFormationItem(secondarySlot);
            RefreshFormationSlot(secondaryItem, deploymentClass, secondaryWeight);
        }

        private void RefreshFormationSlot(
            OrderOfBattleFormationItemVM formationItem,
            DeploymentFormationClass deploymentClass,
            int weight)
        {
            if (formationItem == null)
                return;

            formationItem.RefreshFormation(formationItem.Formation, deploymentClass, true);
            SetPrimaryClassWeight(formationItem, weight);
            formationItem.OnSizeChanged();
        }

        private void ClearUnusedFormationSlots(ISet<int> usedSlots)
        {
            if (usedSlots == null)
                return;

            for (int i = 0; i < _allFormations.Count; i++)
            {
                if (usedSlots.Contains(i))
                    continue;

                OrderOfBattleFormationItemVM formationItem = _allFormations[i];
                if (formationItem == null)
                    continue;

                formationItem.RefreshFormation(formationItem.Formation, DeploymentFormationClass.Unset, false);
                SetPrimaryClassWeight(formationItem, 0);
                formationItem.OnSizeChanged();
            }
        }

        private OrderOfBattleFormationItemVM GetFormationItem(int index)
        {
            if (index < 0 || _allFormations == null || index >= _allFormations.Count)
                return null;

            return _allFormations[index];
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
                count += item.Formation.GetCountOfUnitsWithCondition(agent =>
                    ResolveProjectedSiegeAgentClass(agent) == projectedClass);
            }

            return count;
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
