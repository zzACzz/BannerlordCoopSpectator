using System;
using System.Collections.Generic;

namespace CoopSpectator.Infrastructure
{
    public sealed class CoopMissionEquipmentAmmoUsage
    {
        public CoopMissionEquipmentAmmoUsage(bool isConsumable, string relevantSkillId)
        {
            IsConsumable = isConsumable;
            RelevantSkillId = relevantSkillId;
        }

        public bool IsConsumable { get; }
        public string RelevantSkillId { get; }
    }

    public static class CoopMissionEquipmentAmmoPerkContract
    {
        public static int SelectConsumableUsageIndex(
            int currentUsageIndex,
            IReadOnlyList<CoopMissionEquipmentAmmoUsage> usages)
        {
            if (usages == null || usages.Count == 0)
                return -1;

            if (currentUsageIndex >= 0 &&
                currentUsageIndex < usages.Count &&
                IsSupportedAmmoUsage(usages[currentUsageIndex]))
            {
                return currentUsageIndex;
            }

            for (int usageIndex = 0; usageIndex < usages.Count; usageIndex++)
            {
                if (IsSupportedAmmoUsage(usages[usageIndex]))
                    return usageIndex;
            }

            return -1;
        }

        public static int CalculateTargetAmount(
            int currentAmount,
            int? snapshotBaseAmount,
            int roundedAmmoBonus)
        {
            int nonNegativeBonus = Math.Max(0, roundedAmmoBonus);
            int baseAmount = snapshotBaseAmount.HasValue && snapshotBaseAmount.Value > 0
                ? snapshotBaseAmount.Value
                : Math.Max(0, currentAmount);
            return Math.Max(0, baseAmount + nonNegativeBonus);
        }

        private static bool IsSupportedAmmoUsage(CoopMissionEquipmentAmmoUsage usage)
        {
            if (usage == null || !usage.IsConsumable || string.IsNullOrWhiteSpace(usage.RelevantSkillId))
                return false;

            return string.Equals(usage.RelevantSkillId, "Bow", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(usage.RelevantSkillId, "Crossbow", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(usage.RelevantSkillId, "Throwing", StringComparison.OrdinalIgnoreCase);
        }
    }
}
