using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    public static class MissionItemUsageSetFlagsGuardPatch
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                MethodInfo target = AccessTools.Method(
                    typeof(MissionWeapon),
                    nameof(MissionWeapon.HasAnyUsageWithItemUsageSetFlags));
                MethodInfo prefix = typeof(MissionItemUsageSetFlagsGuardPatch).GetMethod(
                    nameof(HasAnyUsageWithItemUsageSetFlags_Prefix),
                    BindingFlags.Static | BindingFlags.NonPublic);

                if (target == null || prefix == null)
                {
                    ModLogger.Info("MissionItemUsageSetFlagsGuardPatch: target or prefix not found. Skip.");
                    return;
                }

                harmony.Patch(target, prefix: new HarmonyMethod(prefix));
                ModLogger.Info("MissionItemUsageSetFlagsGuardPatch: applied prefix to MissionWeapon.HasAnyUsageWithItemUsageSetFlags.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("MissionItemUsageSetFlagsGuardPatch.Apply failed.", ex);
            }
        }

        // Ammo entries can legitimately have empty ItemUsage; do not ask native usage-flag lookup to resolve "".
        private static bool HasAnyUsageWithItemUsageSetFlags_Prefix(
            MissionWeapon __instance,
            ItemObject.ItemUsageSetFlags flags,
            ref bool __result)
        {
            int usageCount = __instance.WeaponsCount;
            for (int i = 0; i < usageCount; i++)
            {
                WeaponComponentData weapon = __instance.GetWeaponComponentDataForUsage(i);
                string itemUsage = weapon?.ItemUsage;
                if (string.IsNullOrEmpty(itemUsage))
                    continue;

                ItemObject.ItemUsageSetFlags usageFlags = MBItem.GetItemUsageSetFlags(itemUsage);
                if ((usageFlags & flags) == flags)
                {
                    __result = true;
                    return false;
                }
            }

            __result = false;
            return false;
        }
    }
}
