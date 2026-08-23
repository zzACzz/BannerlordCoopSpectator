using System;
using System.Reflection;
using CoopSpectator.Infrastructure;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    internal static class ExactSiegeLadderMerlonVisualParityPatch
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                MethodInfo target = AccessTools.DeclaredMethod(
                    typeof(DestructableComponent),
                    nameof(DestructableComponent.OnAfterReadFromNetwork));
                MethodInfo postfix = typeof(ExactSiegeLadderMerlonVisualParityPatch).GetMethod(
                    nameof(OnAfterReadFromNetworkPostfix),
                    BindingFlags.Static | BindingFlags.NonPublic);

                if (target == null || postfix == null)
                {
                    ModLogger.Info(
                        "ExactSiegeLadderMerlonVisualParityPatch: target or postfix not found. Skip.");
                    return;
                }

                harmony.Patch(target, postfix: new HarmonyMethod(postfix));
                ModLogger.Info(
                    "ExactSiegeLadderMerlonVisualParityPatch: applied DestructableComponent initial-sync postfix.");
            }
            catch (Exception ex)
            {
                ModLogger.Error("ExactSiegeLadderMerlonVisualParityPatch.Apply failed.", ex);
            }
        }

        private static void OnAfterReadFromNetworkPostfix(
            DestructableComponent __instance,
            (BaseSynchedMissionObjectReadableRecord,
                ISynchedMissionObjectReadableRecord) synchedMissionObjectReadableRecord,
            bool allowVisibilityUpdate)
        {
            try
            {
                ExactSiegeLadderMerlonVisualParityRuntime.TryRestoreAfterRead(
                    __instance,
                    synchedMissionObjectReadableRecord,
                    allowVisibilityUpdate);
            }
            catch
            {
                // Fail open: a visual parity repair must never interrupt native object sync.
            }
        }
    }
}
