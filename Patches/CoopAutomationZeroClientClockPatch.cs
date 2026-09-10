using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using CoopSpectator.Infrastructure.Automation;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace CoopSpectator.Patches
{
    // Explicit installation by the admitted dedicated driver; never discovered through PatchAll.
    internal static class CoopAutomationZeroClientClockPatch
    {
        private const string OwnerId = "com.coopspectator.automation.zero-client-clock";
        private static Harmony _harmony;
        private static MethodInfo _target;

        public static bool TryInstall(out string failure)
        {
            failure = string.Empty;
            if (!CoopAutomationSpawnSmokeBridge.IsRequested || !CoopAutomationSpawnSmokeBridge.IsActive ||
                !string.IsNullOrEmpty(CoopAutomationSpawnSmokeBridge.Failure) ||
                !GameNetwork.IsDedicatedServer)
            { failure = "ZeroClientClockNotAdmitted"; return false; }
            try
            {
                if (_harmony != null)
                    throw new InvalidOperationException("ZeroClientClockAlreadyInstalled");
                _target = AccessTools.Method(typeof(MissionState), "TickMission", new[] { typeof(float) });
                if (_target == null || _target.IsStatic || _target.ReturnType != typeof(void))
                    throw new MissingMethodException("MissionState.TickMission(float)");
                _harmony = new Harmony(OwnerId);
                _harmony.Patch(_target, transpiler: new HarmonyMethod(
                    typeof(CoopAutomationZeroClientClockPatch), nameof(Transpiler)));
                if (Harmony.GetPatchInfo(_target)?.Transpilers.Count(p => p.owner == OwnerId) != 1)
                    throw new InvalidOperationException("ZeroClientClockInstallationUnverified");
                return true;
            }
            catch (Exception ex)
            {
                failure = "ZeroClientClockInstallFailed:" + ex.GetType().Name + ":" + ex.Message;
                if (!Reset(out string cleanupFailure)) failure += ";" + cleanupFailure;
                CoopAutomationSpawnSmokeBridge.Fail(failure);
                return false;
            }
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            MethodInfo predicate = AccessTools.Method(typeof(GameNetwork), "DoesDedicatedServerHaveAnyNetworkPeersOrBots");
            MethodInfo missionGetter = AccessTools.PropertyGetter(typeof(MissionState), nameof(MissionState.CurrentMission));
            MethodInfo wrapper = AccessTools.Method(typeof(CoopAutomationZeroClientClockPatch), nameof(AllowMissionTick));
            if (predicate == null || missionGetter == null || wrapper == null)
                throw new MissingMethodException("ZeroClientClockRequiredMethodMissing");
            var matches = codes.Select((code, index) => new { code, index })
                .Where(value => value.code.Calls(predicate)).Select(value => value.index).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException("ZeroClientClockPredicateCount:" + matches.Length);
            int call = matches[0];
            // Exact observed corridor: call predicate; brtrue resume; ldc.r4 0; stloc delta; resume.
            if (call + 4 >= codes.Count ||
                (codes[call + 1].opcode != OpCodes.Brtrue && codes[call + 1].opcode != OpCodes.Brtrue_S) ||
                !(codes[call + 1].operand is Label resume) || !codes[call + 4].labels.Contains(resume) ||
                codes[call + 2].opcode != OpCodes.Ldc_R4 || !(codes[call + 2].operand is float zero) || zero != 0f ||
                (codes[call + 3].opcode != OpCodes.Stloc_0 && codes[call + 3].opcode != OpCodes.Stloc_1 &&
                 codes[call + 3].opcode != OpCodes.Stloc_2 && codes[call + 3].opcode != OpCodes.Stloc_3 &&
                 codes[call + 3].opcode != OpCodes.Stloc && codes[call + 3].opcode != OpCodes.Stloc_S))
                throw new InvalidOperationException("ZeroClientClockDeltaGuardShapeMismatch");

            // Retain the native predicate, its call metadata, pause/fixed-delta logic and branch targets.
            codes.InsertRange(call + 1, new[] {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Callvirt, missionGetter),
                new CodeInstruction(OpCodes.Call, wrapper)
            });
            return codes;
        }

        public static bool AllowMissionTick(bool nativeHasParticipants, Mission mission)
        {
            return nativeHasParticipants || CoopAutomationZeroClientRuntime.CanAdvancePreBattle(mission);
        }

        public static bool Reset(out string failure)
        {
            failure = string.Empty;
            try
            {
                if (_harmony != null && _target != null)
                {
                    _harmony.Unpatch(_target, HarmonyPatchType.Transpiler, OwnerId);
                    if (Harmony.GetPatchInfo(_target)?.Transpilers.Any(p => p.owner == OwnerId) == true)
                        throw new InvalidOperationException("ZeroClientClockPatchStillInstalled");
                }
                _harmony = null;
                _target = null;
                return true;
            }
            catch (Exception ex)
            {
                failure = "ZeroClientClockResetFailed:" + ex.GetType().Name + ":" + ex.Message;
                CoopAutomationSpawnSmokeBridge.Fail(failure);
                return false;
            }
        }
    }
}
