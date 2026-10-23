using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using CoopSpectator.Patches;
using HarmonyLib;

internal static class SiegeFlightCallSiteTests
{
    internal interface ISide { int BattleSide { get; } }
    private sealed class Side : ISide { public int BattleSide => 1; }
    internal sealed class Missile
    {
        public int ShooterSlotIndex = 2, TargetSlotIndex = 3, TargetType = 4;
        public string ShooterSiegeEngineType = "test";
    }
    internal struct Data { public int Value; }
    internal sealed class Fixture
    {
        internal int NativeCalls, Result;
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void Tick()
        {
            Missile missile = MakeMissile();
            ISide side = MakeSide();
            Calculate(missile.ShooterSlotIndex, missile.ShooterSiegeEngineType,
                side.BattleSide, missile.TargetType, missile.TargetSlotIndex, out Data shot);
            Calculate(5, "animation", 1, 2, 3, out Data animation);
            Result = shot.Value + animation.Value;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Calculate(int slot, string type, int side, int target, int index, out Data data)
        { NativeCalls++; data = new Data { Value = slot }; }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Missile MakeMissile() => new Missile();
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ISide MakeSide() => new Side();
    }
    private static readonly MethodInfo Original = AccessTools.Method(typeof(Fixture), "Tick");
    private static readonly MethodInfo Calculation = AccessTools.Method(typeof(Fixture), "Calculate");
    private static readonly MethodInfo SideGetter = AccessTools.PropertyGetter(typeof(ISide), "BattleSide");
    private static readonly MethodInfo Observer = AccessTools.Method(typeof(SiegeFlightCallSiteTests), nameof(Observe));
    private static int _observations;
    private static void Observe(Fixture owner, Missile missile, object data)
    { if (owner != null && missile.ShooterSlotIndex == 2 && data is Data value && value.Value == 2) _observations++; }
    private static bool Rewrite(IEnumerable<CodeInstruction> codes, out List<CodeInstruction> result) =>
        CoopSiegeFlightCallSite.TryRewrite(codes, Original, Calculation, typeof(Missile), SideGetter, Observer, out result);
    private static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> codes)
    {
        if (!Rewrite(codes, out var result)) throw new Exception("Fixture shape rejected");
        return result;
    }
    internal static void Run()
    {
        var codes = PatchProcessor.GetOriginalInstructions(Original).ToList();
        Check(Rewrite(codes, out var result) && result.Count == codes.Count + 5, "unique missile call admitted");
        Check(codes.All(c => !c.Calls(Observer)), "original instructions unchanged");
        var missing = codes.Where(c => !c.Calls(Calculation)).ToList();
        Check(!Rewrite(missing, out _), "missing calculation rejected");
        var extra = codes.Concat(new[] { new CodeInstruction(OpCodes.Call, Calculation) }).ToList();
        Check(!Rewrite(extra, out _), "extra calculation rejected");
        var renamed = codes.Select(c => new CodeInstruction(c)).ToList();
        var field = renamed.First(c => c.operand is FieldInfo f && f.Name == "ShooterSlotIndex");
        field.operand = typeof(Missile).GetField("TargetSlotIndex");
        Check(!Rewrite(renamed, out _), "wrong field producer rejected");
        var harmony = new Harmony("coopspectator.tests.siege-flight-callsite");
        try
        {
            harmony.Patch(Original, transpiler: new HarmonyMethod(typeof(SiegeFlightCallSiteTests), nameof(Transpile)));
            _observations = 0;
            var fixture = new Fixture(); fixture.Tick();
            Check(fixture.NativeCalls == 2 && fixture.Result == 7, "native calculations preserved");
            Check(_observations == 1, "only missile observed");
        }
        finally { harmony.Unpatch(Original, HarmonyPatchType.All, harmony.Id); }
        Console.WriteLine("Siege flight call-site substitute patch checks passed; native installation pending.");
    }
    private static void Check(bool value, string message)
    { if (!value) throw new Exception("Siege call site: " + message); }
}
