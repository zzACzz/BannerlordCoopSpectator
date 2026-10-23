using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using CoopSpectator.Infrastructure;
using TaleWorlds.MountAndBlade;

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
        private static MethodInfo _endGameGetter;

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

                _endGameGetter = AccessTools.PropertyGetter(vmType, "IsEndGameTimerEnabled");
                if (_endGameGetter != null)
                    harmony.Patch(AccessTools.Method(vmType, "OnIntermissionStateUpdated"),
                        transpiler: new HarmonyMethod(typeof(IntermissionVmCrashGuardPatch), nameof(CorrectDefenderCulture)));
                PatchFinalizer(harmony, vmType, "OnIntermissionStateUpdated");
                PatchFinalizer(harmony, vmType, "OnPlayerVotedForCulture");
                PatchFinalizer(harmony, vmType, "OnPlayerVotedForMap");
            }
            catch (Exception ex)
            {
                ModLogger.Error("IntermissionVmCrashGuardPatch.Apply failed.", ex);
            }
        }

        // Only rewrite the two audited culture-B conditional lookups. Preserve
        // labels/exception boundaries and return the entire original on mismatch.
        internal static IEnumerable<CodeInstruction> CorrectDefenderCulture(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.Select(i => new CodeInstruction(i)).ToList();
            var sites = new List<int>();
            for (int i = 0; i + 6 < code.Count; i++)
            {
                if (!IsCall(code[i], "get_NextFactionACultureID", "MPIntermissionVM") ||
                    !IsCall(code[i + 1], "IsNullOrEmpty", "String") ||
                    (code[i + 2].opcode != OpCodes.Brtrue && code[i + 2].opcode != OpCodes.Brtrue_S) ||
                    !IsCall(code[i + 3], "get_Instance", "MBObjectManager") ||
                    code[i + 4].opcode != OpCodes.Ldarg_0 ||
                    !IsCall(code[i + 5], "get_NextFactionBCultureID", "MPIntermissionVM") ||
                    !(code[i + 6].operand is MethodInfo lookup) || !lookup.IsGenericMethod ||
                    lookup.Name != "GetObject" || lookup.GetGenericArguments().Length != 1 ||
                    lookup.GetGenericArguments()[0].FullName != "TaleWorlds.Core.BasicCultureObject") continue;
                sites.Add(i);
            }
            if (sites.Count != 2)
            {
                ModLogger.Info("IntermissionVmCrashGuardPatch: culture correction skipped; native lookup shape mismatch.");
                return code;
            }
            var replacement = AccessTools.Method(typeof(IntermissionVmCrashGuardPatch), nameof(CurrentDefenderCulture));
            foreach (int site in sites)
                foreach (int offset in new[] { 0, 5 })
                {
                    code[site + offset].opcode = OpCodes.Call;
                    code[site + offset].operand = replacement;
                }
            ModLogger.Info("IntermissionVmCrashGuardPatch: corrected two native defender culture lookups.");
            return code;
        }

        private static bool IsCall(CodeInstruction instruction, string method, string declaringType)
        {
            return (instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) &&
                instruction.operand is MethodInfo target && target.Name == method && target.DeclaringType?.Name == declaringType;
        }

        private static string CurrentDefenderCulture(object instance)
        {
            // Native code sets this flag before either lookup. Read current options
            // instead of the B display property, which is assigned later on first update.
            if ((bool)_endGameGetter.Invoke(instance, null)) return string.Empty;
            return MultiplayerOptions.OptionType.CultureTeam2.GetStrValue(
                MultiplayerOptions.MultiplayerOptionsAccessMode.CurrentMapOptions);
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
