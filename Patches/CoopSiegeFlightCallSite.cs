using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace CoopSpectator.Patches
{
    // Shape-checked insertion only; this class never installs a patch.
    internal static class CoopSiegeFlightCallSite
    {
        internal static bool TryRewrite(IEnumerable<CodeInstruction> source, MethodInfo original,
            MethodInfo calculation, Type missileType, MethodInfo sideGetter, MethodInfo observer,
            out List<CodeInstruction> result)
        {
            result = null;
            if (source == null || original == null || calculation == null || missileType == null ||
                sideGetter == null || observer == null) return false;
            var codes = source.ToList();
            var locals = original.GetMethodBody()?.LocalVariables;
            var parameters = calculation.GetParameters();
            if (locals == null || original.IsStatic || sideGetter.IsStatic || sideGetter.GetParameters().Length != 0 ||
                calculation.IsStatic || calculation.ReturnType != typeof(void) ||
                parameters.Length != 6 || !parameters[5].ParameterType.IsByRef ||
                original.DeclaringType != calculation.DeclaringType) return false;
            if (sideGetter.ReturnType != parameters[2].ParameterType) return false;
            Type dataType = parameters[5].ParameterType.GetElementType();
            if (!dataType.IsValueType || !observer.IsStatic || observer.ReturnType != typeof(void) ||
                !observer.GetParameters().Select(p => p.ParameterType).SequenceEqual(
                    new[] { original.DeclaringType, missileType, typeof(object) })) return false;
            var calls = Enumerable.Range(0, codes.Count).Where(i => codes[i].Calls(calculation)).ToArray();
            if (calls.Length != 2) return false;
            var matches = new List<Tuple<int, int, int>>();
            foreach (int call in calls)
            {
                if (call < 12 || codes[call - 12].opcode != OpCodes.Ldarg_0 ||
                    !codes[call - 6].Calls(sideGetter)) continue;
                int missile = Local(codes[call - 11], false);
                int data = Local(codes[call - 1], true);
                if (missile < 0 || data < 0 || missile >= locals.Count || data >= locals.Count ||
                    locals[missile].LocalType != missileType || locals[data].LocalType != dataType ||
                    Local(codes[call - 9], false) != missile || Local(codes[call - 5], false) != missile ||
                    Local(codes[call - 3], false) != missile) continue;
                int side = Local(codes[call - 7], false);
                if (side < 0 || side >= locals.Count || locals[side].LocalType != sideGetter.DeclaringType) continue;
                string[] names = { "ShooterSlotIndex", "ShooterSiegeEngineType", "TargetType", "TargetSlotIndex" };
                int[] offsets = { 10, 8, 4, 2 };
                int[] parameterIndices = { 0, 1, 3, 4 };
                bool fieldsMatch = true;
                for (int i = 0; i < names.Length; i++)
                {
                    var code = codes[call - offsets[i]];
                    var field = code.operand as FieldInfo;
                    fieldsMatch &= code.opcode == OpCodes.Ldfld && field != null &&
                        field.DeclaringType == missileType && field.Name == names[i] &&
                        field.FieldType == parameters[parameterIndices[i]].ParameterType;
                }
                if (fieldsMatch) matches.Add(Tuple.Create(call, missile, data));
            }
            if (matches.Count != 1) return false;
            var match = matches[0];
            // Copy instructions: never mutate the original sequence on rejection.
            result = codes.Select(c => new CodeInstruction(c)).ToList();
            result.InsertRange(match.Item1 + 1, new[] {
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldloc, match.Item2),
                new CodeInstruction(OpCodes.Ldloc, match.Item3),
                new CodeInstruction(OpCodes.Box, dataType),
                new CodeInstruction(OpCodes.Call, observer)
            });
            return true;
        }

        private static int Local(CodeInstruction code, bool address)
        {
            if (address)
            {
                if (code.opcode != OpCodes.Ldloca && code.opcode != OpCodes.Ldloca_S) return -1;
            }
            else
            {
                if (code.opcode == OpCodes.Ldloc_0) return 0;
                if (code.opcode == OpCodes.Ldloc_1) return 1;
                if (code.opcode == OpCodes.Ldloc_2) return 2;
                if (code.opcode == OpCodes.Ldloc_3) return 3;
                if (code.opcode != OpCodes.Ldloc && code.opcode != OpCodes.Ldloc_S) return -1;
            }
            if (code.operand is LocalBuilder local) return local.LocalIndex;
            if (code.operand is int index) return index;
            if (code.operand is byte small) return small;
            if (code.operand is short medium) return medium;
            return -1;
        }
    }
}
