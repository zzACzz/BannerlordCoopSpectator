using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using CoopSpectator.Patches;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using TaleWorlds.Core;

internal static class Program
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    static MethodInfo Getter(string name) => AccessTools.PropertyGetter(typeof(MPIntermissionVM), name);
    static List<CodeInstruction> Site() => new List<CodeInstruction> {
        new CodeInstruction(OpCodes.Ldarg_0),
        new CodeInstruction(OpCodes.Call, Getter("NextFactionACultureID")),
        new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(string), "IsNullOrEmpty")),
        new CodeInstruction(OpCodes.Brtrue_S, default(Label)),
        new CodeInstruction(OpCodes.Call, AccessTools.PropertyGetter(typeof(MBObjectManager), "Instance")),
        new CodeInstruction(OpCodes.Ldarg_0),
        new CodeInstruction(OpCodes.Call, Getter("NextFactionBCultureID")),
        new CodeInstruction(OpCodes.Callvirt, AccessTools.Method(typeof(MBObjectManager), "GetObject").MakeGenericMethod(typeof(BasicCultureObject)))
    };
    static int Main()
    {
        try
        {
            var original = Site().Concat(Site()).ToList();
            original[1].labels.Add(default(Label));
            var corrected = IntermissionVmCrashGuardPatch.CorrectDefenderCulture(original).ToList();
            int changes = Enumerable.Range(0, original.Count).Count(i => !Equals(original[i].operand, corrected[i].operand));
            Assert(changes == 4, "Exactly two guards and two B reads must change.");
            Assert(corrected[1].labels.Count == 1, "Branch labels must survive.");
            Assert(((MethodInfo)original[1].operand).Name == "get_NextFactionACultureID", "Input must remain unchanged.");
            foreach (int count in new[] { 0, 1, 3 })
            {
                var input = Enumerable.Range(0, count).SelectMany(_ => Site()).ToList();
                var output = IntermissionVmCrashGuardPatch.CorrectDefenderCulture(input).ToList();
                Assert(input.Zip(output).All(p => p.First.opcode == p.Second.opcode && Equals(p.First.operand,p.Second.operand)), "Unknown shape must stay unchanged.");
            }
            var malformed = Site().Concat(Site()).ToList();
            malformed[3].opcode = OpCodes.Brfalse_S;
            Assert(IntermissionVmCrashGuardPatch.CorrectDefenderCulture(malformed).Zip(malformed).All(p => Equals(p.First.operand,p.Second.operand)), "Partial match must not partially patch.");
            var field = typeof(IntermissionVmCrashGuardPatch).GetField("_endGameGetter", BindingFlags.Static | BindingFlags.NonPublic);
            field.SetValue(null, Getter("IsEndGameTimerEnabled"));
            var read = AccessTools.Method(typeof(IntermissionVmCrashGuardPatch), "CurrentDefenderCulture");
            var vm = new MPIntermissionVM();
            foreach (string a in new[] { null, "", "empire" })
            foreach (string b in new[] { null, "", "vlandia", "battania" })
            {
                vm.NextFactionACultureID = a;
                vm.NextFactionBCultureID = "stale";
                MultiplayerOptionsExtensions.Value = b;
                Assert(Equals(read.Invoke(null,new object[]{vm}), b), "B must use current option independently of A/stale UI.");
                Assert(MultiplayerOptionsExtensions.LastMode == MultiplayerOptions.MultiplayerOptionsAccessMode.CurrentMapOptions, "Must read current, not next options.");
                Assert(vm.NextFactionBCultureID == "stale", "Reader must not mutate VM.");
            }
            vm.IsEndGameTimerEnabled = true;
            Assert(Equals(read.Invoke(null,new object[]{vm}), ""), "End-game must hide cultures.");
            VerifyExecutableInstructions();
            Console.WriteLine("Intermission culture shape and current-option tests passed.");
            return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    static void VerifyExecutableInstructions()
    {
        var method = new DynamicMethod("CorrectedCultureLookups", typeof(void), new[] { typeof(MPIntermissionVM) }, typeof(Program).Module, true);
        var il = method.GetILGenerator();
        var instructions = new List<CodeInstruction>();
        for (int n = 0; n < 2; n++)
        {
            var site = Site();
            var end = il.DefineLabel();
            site[3].operand = end;
            if (n == 1) site[3].opcode = OpCodes.Brtrue;
            instructions.AddRange(site);
            instructions.Add(new CodeInstruction(OpCodes.Pop));
            var landing = new CodeInstruction(OpCodes.Nop);
            landing.labels.Add(end);
            instructions.Add(landing);
        }
        instructions.Add(new CodeInstruction(OpCodes.Ret));
        foreach (var instruction in IntermissionVmCrashGuardPatch.CorrectDefenderCulture(instructions))
        {
            foreach (var label in instruction.labels) il.MarkLabel(label);
            if (instruction.operand is MethodInfo target) il.Emit(instruction.opcode, target);
            else if (instruction.operand is Label label) il.Emit(instruction.opcode, label);
            else if (instruction.operand == null) il.Emit(instruction.opcode);
            else throw new Exception("Unexpected executable test operand.");
        }
        var execute = (Action<MPIntermissionVM>)method.CreateDelegate(typeof(Action<MPIntermissionVM>));
        foreach (string a in new[] { null, "", "empire" })
        foreach (string b in new[] { null, "", "vlandia" })
        foreach (bool ended in new[] { false, true })
        {
            MBObjectManager.Reads.Clear();
            MultiplayerOptionsExtensions.Value = b;
            var vm = new MPIntermissionVM { NextFactionACultureID = a, NextFactionBCultureID = "stale", IsEndGameTimerEnabled = ended };
            execute(vm);
            int expected = ended || string.IsNullOrEmpty(b) ? 0 : 2;
            Assert(MBObjectManager.Reads.Count == expected, "Executable guards must suppress empty/end-game lookups.");
            Assert(MBObjectManager.Reads.All(id => id == b), "Executable reads must use current B, never stale UI or A.");
        }
    }
}
public class MPIntermissionVM
{
    public string NextFactionACultureID {get;set;}
    public string NextFactionBCultureID {get;set;}
    public bool IsEndGameTimerEnabled {get;set;}
}
namespace TaleWorlds.Core { public class BasicCultureObject {} }
namespace TaleWorlds.ObjectSystem
{
    public class MBObjectManager
    {
        public static MBObjectManager Instance { get; } = new MBObjectManager();
        public static List<string> Reads { get; } = new List<string>();
        public T GetObject<T>(string id)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));
            Reads.Add(id);
            return default;
        }
    }
}
namespace TaleWorlds.MountAndBlade
{
    public class MultiplayerOptions
    {
        public enum OptionType { CultureTeam2 }
        public enum MultiplayerOptionsAccessMode { DefaultMapOptions, CurrentMapOptions, NextMapOptions }
    }
    public static class MultiplayerOptionsExtensions
    {
        public static string Value;
        public static MultiplayerOptions.MultiplayerOptionsAccessMode LastMode;
        public static string GetStrValue(this MultiplayerOptions.OptionType type, MultiplayerOptions.MultiplayerOptionsAccessMode mode)
        { LastMode=mode; return Value; }
    }
}
namespace CoopSpectator.Infrastructure
{
    public static class ModLogger
    {
        public static void Info(string message) {}
        public static void Error(string message, Exception exception) {}
    }
}
