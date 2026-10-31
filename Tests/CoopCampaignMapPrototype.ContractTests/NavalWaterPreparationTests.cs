using System;
using CoopSpectator.Infrastructure;

internal static class NavalWaterPreparationTests
{
    internal static void Run()
    {
        var gate = new CoopNavalMapWaterPreparation(); object scene = new object();
        int checks = 0, creates = 0;
        Func<bool> shader = () => { checks++; return true; };
        Action create = () => creates++;
        Check(!gate.Ensure(null, true, true, shader, create), "no scene");
        Check(!gate.Ensure(scene, false, true, shader, create), "alias/base/campaign role cannot prepare native water");
        Check(!gate.Ensure(scene, true, false, shader, create), "current readiness required");
        Check(checks == 0 && creates == 0, "ineligible scenes perform no native query");
        Check(gate.Ensure(scene, true, true, shader, create) && checks == 1 && creates == 1, "one admitted preparation");
        for (int i = 0; i < 10; i++) Check(!gate.Ensure(scene, true, true, shader, create), "no tick retry");
        Check(checks == 1 && creates == 1, "no repeated shader/resource query after success");
        gate.Reset();
        try { gate.Ensure(scene, true, true, () => false, create); throw new Exception("Missing shader admitted"); }
        catch (InvalidOperationException) { }
        Check(gate.Attempted && creates == 1 && !gate.Ensure(scene, true, true, shader, create), "missing definition fails before creation and consumes attempt");
        gate.Reset();
        try { gate.Ensure(scene, true, true, shader, () => { creates++; throw new InvalidOperationException(); }); }
        catch (InvalidOperationException) { }
        Check(!gate.Ensure(scene, true, true, shader, create) && creates == 2, "failed native preparation is never retried");
        Check(gate.Ensure(new object(), true, true, shader, create) && creates == 3, "new authoritative scene owns a fresh preparation");
        Retirement();
        Console.WriteLine("PASS canonical Naval water boundary: scene/role/readiness, public shader prerequisite, once-only preparation and paired exact-scene retirement including partial/failing cleanup; no native API executed.");
    }

    private static void Retirement()
    {
        var gate = new CoopNavalMapWaterPreparation(); object scene = new object();
        string calls = "";
        Action clear = () => calls += "clear,";
        Action delete = () => calls += "delete,";
        Check(!gate.Retire(scene, clear, delete), "uninitialized scene has no native retirement");
        Check(!gate.Ensure(scene, false, true, () => true, () => { }), "noncanonical scene rejected");
        Check(!gate.Retire(scene, clear, delete), "base and alias scene native state untouched");
        try { gate.Ensure(scene, true, true, () => false, () => { }); } catch (InvalidOperationException) { }
        Check(!gate.InitializationStarted && !gate.Retire(scene, clear, delete) && calls == "", "shader failure never retires native water");
        gate.Reset();
        gate.Ensure(scene, true, true, () => true, () => { });
        Check(!gate.Retire(new object(), clear, delete) && !gate.Retire(null, clear, delete), "only exact initialized scene can retire");
        Check(gate.Retire(scene, clear, delete) && calls == "clear,delete," && gate.RetirementAttempted, "clear view before deleting wake/material binding");
        Check(!gate.Retire(scene, clear, delete) && calls == "clear,delete,", "repeated finalization does not retry native operations");

        gate.Reset(); calls = "";
        try { gate.Ensure(scene, true, true, () => true, () => { throw new InvalidOperationException(); }); } catch (InvalidOperationException) { }
        Check(gate.InitializationStarted && gate.Retire(scene, clear, delete), "partially failed native setup is paired with retirement");
        gate.Reset(); calls = "";
        gate.Ensure(scene, true, true, () => true, () => { });
        try { gate.Retire(scene, () => { calls += "clear,"; throw new InvalidOperationException(); }, delete); throw new Exception("Expected clear failure"); }
        catch (InvalidOperationException) { }
        Check(calls == "clear," && !gate.Retire(scene, clear, delete), "failed view clear skips native Delete like stock; neither operation is retried, normal scene finalization remains responsible");

        gate.Reset(); calls = "";
        Check(!gate.InitializationStarted && !gate.RetirementAttempted, "new scene lifetime resets retirement state");
        gate.Ensure(scene, true, true, () => true, () => { });
        try { gate.Retire(scene, clear, () => { calls += "delete,"; throw new InvalidOperationException(); }); } catch (InvalidOperationException) { }
        Check(calls == "clear,delete," && !gate.Retire(scene, clear, delete), "failed material/wake cleanup is not retried");
        object reloadedScene = new object();
        gate.Ensure(reloadedScene, true, true, () => true, () => { });
        Check(gate.Retire(reloadedScene, clear, delete), "replacement managed scene gets its own retirement even if native allocator reuses address");
    }

    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
