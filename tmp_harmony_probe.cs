using System;
using HarmonyLib;

public static class DummyTarget {
    public static int Add(int a, int b) => a + b;
}

public static class DummyPatch {
    public static void Postfix(ref int __result) { __result += 1; }
}

public static class Runner {
    public static void Main() {
        try {
            var h = new Harmony("test.harmony");
            var original = typeof(DummyTarget).GetMethod("Add");
            var postfix = typeof(DummyPatch).GetMethod("Postfix");
            h.Patch(original, postfix: new HarmonyMethod(postfix));
            Console.WriteLine("PATCH_OK result=" + DummyTarget.Add(2,3));
        } catch (Exception ex) {
            Console.WriteLine("PATCH_FAIL " + ex.GetType().FullName + ": " + ex.Message);
            Console.WriteLine(ex.ToString());
        }
    }
}
