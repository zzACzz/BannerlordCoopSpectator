using System;
using System.Reflection;
using CoopSpectator.Patches;

internal static class SiegeFlightCleanupTests
{
    internal static void Run()
    {
        for (int failures = 0; failures < 4; failures++)
        {
            int calls = 0;
            bool removed = CoopSiegeFlightObservationPatch.TryRemoveBoth(
                () => { calls++; if ((failures & 1) != 0) throw new InvalidOperationException(); },
                () => { calls++; if ((failures & 2) != 0) throw new InvalidOperationException(); });
            Check(calls == 2 && removed == (failures == 0), "both cleanup paths attempted and outcome honest");
        }
        var type = typeof(CoopSiegeFlightObservationPatch);
        var abort = type.GetField("_abort", BindingFlags.Static | BindingFlags.NonPublic);
        abort.SetValue(null, (Action)(() => { throw new InvalidOperationException("observer failure"); }));
        CoopSiegeFlightObservationPatch.Reset();
        foreach (string field in new[] { "_begin", "_finish", "_abort", "_observe" })
            Check(type.GetField(field, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) == null,
                "reset revokes callbacks despite abort exception");
        CoopSiegeFlightObservationPatch.Reset();
        var nativeFailure = new InvalidOperationException("native failure");
        var finalizer = type.GetMethod("FinalizeTick", BindingFlags.Static | BindingFlags.NonPublic);
        Check(ReferenceEquals(finalizer.Invoke(null, new object[] { nativeFailure }), nativeFailure),
            "native exception preserved exactly");
        Console.WriteLine("Siege flight cleanup fault-path checks passed; actual native unpatch remains unverified.");
    }
    private static void Check(bool value, string label)
    { if (!value) throw new Exception("Siege cleanup: " + label); }
}

namespace SandBox.View.Map.Managers { public class SettlementVisualManager { } }
namespace TaleWorlds.CampaignSystem.Siege { public interface ISiegeEventSide { int BattleSide { get; } } }
