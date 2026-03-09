using System;
using System.Reflection;
using HarmonyLib;
using CoopSpectator.Infrastructure;

namespace CoopSpectator.Patches
{
    /// <summary>
    /// Temporary client-side guard for vanilla custom-game intermission VM.
    /// The current TdmClone lobby flow triggers exceptions on culture-vote updates
    /// before LoadMission; swallowing them lets us verify whether this is the
    /// remaining client crash source.
    /// </summary>
    public static class IntermissionVmCrashGuardPatch
    {
        public static void Apply(Harmony harmony)
        {
            try
            {
                Type vmType = AccessTools.TypeByName("TaleWorlds.MountAndBlade.Multiplayer.ViewModelCollection.Intermission.MPIntermissionVM");
                if (vmType == null)
                {
                    ModLogger.Info("IntermissionVmCrashGuardPatch: MPIntermissionVM type not found. Skip.");
                    return;
                }

                PatchFinalizer(harmony, vmType, "OnIntermissionStateUpdated");
                PatchFinalizer(harmony, vmType, "OnPlayerVotedForCulture");
                PatchFinalizer(harmony, vmType, "OnPlayerVotedForMap");
            }
            catch (Exception ex)
            {
                ModLogger.Error("IntermissionVmCrashGuardPatch.Apply failed.", ex);
            }
        }

        private static void PatchFinalizer(Harmony harmony, Type vmType, string methodName)
        {
            MethodInfo target = AccessTools.Method(vmType, methodName);
            if (target == null)
            {
                ModLogger.Info("IntermissionVmCrashGuardPatch: " + methodName + " not found on MPIntermissionVM. Skip.");
                return;
            }

            MethodInfo finalizer = typeof(IntermissionVmCrashGuardPatch).GetMethod(
                nameof(SwallowIntermissionVmException),
                BindingFlags.Static | BindingFlags.NonPublic);

            harmony.Patch(target, finalizer: new HarmonyMethod(finalizer));
            ModLogger.Info("IntermissionVmCrashGuardPatch: applied finalizer to MPIntermissionVM." + methodName);
        }

        private static Exception SwallowIntermissionVmException(Exception __exception, MethodBase __originalMethod)
        {
            if (__exception == null)
                return null;

            ModLogger.Error("IntermissionVmCrashGuardPatch: swallowed exception in " + (__originalMethod?.DeclaringType?.FullName ?? "unknown") + "." + (__originalMethod?.Name ?? "unknown"), __exception);
            return null;
        }
    }
}
